using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PaymentsEngine
{
    public static JsonObject Edit(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject patch)
    {
        var old=Raw(c,tx,id);Scope(actor,old);AccessPolicy.Demand(actor,Permission(old.S("type")));
        J.Need(old.N("status")==1,"التعديل لمسودة فقط.",409);
        J.Need(old.S("type") is "quick" or "expense" or "purchase" or "government","عدل الطلب من مصدره للحفاظ على ربط الحساب والمتحمل.",409);
        J.Need(patch.S("expectedHash")==old.S("contentHash"),"المسودة تغيرت؛ أعد تحميلها قبل التعديل.",409);
        var input=old["metadata"]!.DeepClone().AsObject();
        string[] allowed=["reference","period","occurredOn","dueOn","lines","vatAmount","validTaxInvoice","supplierId"];
        J.Need(patch.All(p=>p.Key=="expectedHash"||allowed.Contains(p.Key)),"الحقل غير قابل للتعديل في هذه المسودة.");
        foreach(var p in patch.Where(p=>p.Key!="expectedHash"))input[p.Key]=p.Value?.DeepClone();
        string company=old.S("companyId"),month=J.Period(input.Required("period"))+"-01",day=Day(input.Required("occurredOn")),due=Day(input.Required("dueOn")),reference=Text(input,"reference");
        J.Need(month[..7]==day[..7],"تاريخ المستند خارج شهر الفترة.");
        var lines=input["lines"] as JsonArray??throw new ApiError("البنود مطلوبة.");J.Need(lines.Count is >0 and <=100&&lines.All(l=>l is JsonObject),"عدد البنود غير صالح.");
        decimal amount=lines.OfType<JsonObject>().Sum(l=>Money(l,"amount")),vat=input.N("vatAmount");
        J.Need(vat>=0&&vat==J.Money(vat)&&vat<amount&&(vat==0||input.B("validTaxInvoice")),"ضريبة المدخلات تحتاج فاتورة صالحة وبنودًا مطابقة.");
        J.Need(lines.OfType<JsonObject>().Where(l=>l.S("accountCode")=="112601").Sum(l=>l.N("amount"))==vat,"بنود الضريبة لا تطابق المستند.");
        string supplier=input.S("supplierId");J.Need(old.S("type")!="purchase"||supplier!="","المورد مطلوب.");
        if(supplier!="")J.Need(SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"supplier")} WHERE id=@id AND company_id=@c AND status=1",("@id",Id(supplier)),("@c",company))!=null,"المورد غير موجود في نطاق الشركة.",404);
        foreach(var line in lines.OfType<JsonObject>())
        {
            _=Account(c,tx,line.Required("accountCode"));ValidateCentre(c,tx,company,Id(line.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D"))));
            J.Need(line.S("erpRiderId")==""&&line.S("erpPlatformAccountId")==""&&line.S("appCode","none")=="none","المستند المنسوب لمندوب أو منصة يعدل من مسار مصدره.",409);
        }
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"document")} SET period_month=@m,occurred_on=@day,due_on=@due,reference=@ref,amount=@amount,vat_amount=@vat,supplier_id=@s,metadata=@meta,content_hash=@hash,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id AND status=1",("@m",month),("@day",day),("@due",due),("@ref",reference),("@amount",amount),("@vat",vat),("@s",NullId(supplier)),("@meta",input.ToJsonString()),("@hash",Hash(input)),("@by",Me(actor)),("@id",Id(id)));
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"document_line")} SET active=0 WHERE document_id=@id AND active=1",("@id",Id(id)));
        int index=0;foreach(var line in lines.OfType<JsonObject>())
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"document_line")}(id,document_id,company_id,line_no,period_month,account_id,cost_centre_id,amount,created_by) VALUES(@id,@doc,@c,@ln,@pm,@a,@cc,@amount,@by)",("@id",MoneyId()),("@doc",Id(id)),("@c",company),("@ln",++index),("@pm",month),("@a",Account(c,tx,line.S("accountCode")).ToString("D")),("@cc",line.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D"))),("@amount",line.N("amount")),("@by",Me(actor)));
        Store.Audit(c,tx,"payment.document.edited",Id(id),Me(actor));return Actions(c,tx,actor,Raw(c,tx,id));
    }
}
