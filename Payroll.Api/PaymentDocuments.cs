using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PaymentDocuments
{
    public static JsonObject Supplier(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");string company=PaymentsEngine.Id(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,company);
        string id=input.S("id")==""?PaymentsEngine.MoneyId():PaymentsEngine.Id(input.S("id"));
        SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,"supplier")}(id,company_id,name_ar,vat_no,erp_maintenance_supplier_id,created_by) VALUES(@id,@c,@name,@vat,@erp,@by)",("@id",id),("@c",company),("@name",PaymentsEngine.Text(input,"nameAr",200)),("@vat",input.S("vatNo")),("@erp",PaymentsEngine.NullId(input.S("erpMaintenanceSupplierId"))),("@by",PaymentsEngine.Me(actor)));
        Store.Audit(c,tx,"payment.supplier.created",id,PaymentsEngine.Me(actor));return new(){["id"]=id,["companyId"]=company,["allowedActions"]=new JsonArray()};
    }
    public static JsonObject Suppliers(DbConnection c,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"payments.view");return new(){["items"]=PaymentsEngine.Array(PaymentsEngine.Rows(c,null,$"SELECT id,company_id AS companyId,name_ar AS nameAr,vat_no AS vatNo,status FROM {PaymentsEngine.Q(c,"supplier")} ORDER BY name_ar").Where(r=>FinancialScopes.Sponsor(actor,r.S("companyId")))),["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("create"):new()};
    }
    public static JsonObject SupplierLedger(DbConnection c,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"payments.view");string company=SchemaSql.Scalar(c,null,$"SELECT company_id FROM {PaymentsEngine.Q(c,"supplier")} WHERE id=@id",("@id",PaymentsEngine.Id(id)))?.ToString()??throw new ApiError("المورد غير موجود.",404);J.Need(FinancialScopes.Sponsor(actor,company),"المورد غير موجود.",404);
        var docs=PaymentsEngine.Rows(c,null,$"SELECT d.id,d.company_id AS companyId,d.platform,d.reference,d.amount,d.status,COALESCE(p.amount_paid,0) AS amountPaid,COALESCE(p.amount_reserved,0) AS amountReserved FROM {PaymentsEngine.Q(c,"document")} d LEFT JOIN {PaymentsEngine.Q(c,"payable")} p ON p.source_table='pay.document' AND p.source_id=d.id WHERE d.supplier_id=@id AND d.type='purchase' AND d.status=3 ORDER BY d.occurred_on,d.id",("@id",PaymentsEngine.Id(id))).Where(d=>PaymentsEngine.Visible(actor,d)).ToList();
        decimal invoiced=docs.Sum(d=>d.N("amount")),paid=docs.Sum(d=>d.N("amountPaid"));return new(){["items"]=PaymentsEngine.Array(docs),["invoiced"]=invoiced,["paid"]=paid,["outstanding"]=invoiced-paid,["allowedActions"]=new JsonArray()};
    }
    public static JsonObject BearerDocument(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string type,JsonObject input)
    {
        J.Need(type is "hr_fee" or "traffic_violation","نوع الطلب غير صالح.");AccessPolicy.Demand(actor,type=="hr_fee"?"payments.hr_request.create":"payments.disburse");
        string bearer=input.Required("bearer");J.Need(bearer is "rider" or "company" or "split" or "cash_collection","المتحمل غير صالح.");
        J.Need(bearer!="cash_collection","التحصيل النقدي يحتاج ربط سجل صندوق الشريك خارج الدفاتر قبل تفعيله.",409);
        decimal amount=PaymentsEngine.Money(input,"amount"),rider=input.N("riderShare"),company=input.N("companyShare");
        J.Need(rider>=0&&company>=0&&rider==J.Money(rider)&&company==J.Money(company)&&rider+company==amount,"حصتا المندوب والشركة لا تساويان الإجمالي.");
        J.Need(bearer!="rider"||rider==amount,"حصة المندوب لا تطابق المتحمل.");J.Need(bearer!="company"||company==amount,"حصة الشركة لا تطابق المتحمل.");J.Need(bearer!="split"||rider>0&&company>0,"الطلب المشترك يحتاج حصتين موجبتين.");
        string employee=PaymentsEngine.Id(input.Required("erpEmployeeId")),platformAccount=PaymentsEngine.Id(input.Required("erpPlatformAccountId"));
        string companyId=PaymentsEngine.Id(input.Required("companyId"));
        string platform=DemandPlatformAttribution(c,tx,actor,companyId,employee,platformAccount);
        var doc=input.DeepClone().AsObject();doc["type"]=type;doc["platform"]=platform;var lines=new JsonArray();string code=type=="hr_fee"?"112506":"112502";
        if(rider>0)lines.Add(Line(input,code,rider,employee,platformAccount));
        if(company>0)lines.Add(Line(input,input.Required("companyExpenseAccountCode"),company,employee,platformAccount));
        doc["lines"]=lines;var saved=PaymentsEngine.Create(c,tx,actor,doc);
        if(type=="hr_fee"&&SchemaSql.Scalar(c,tx,$"SELECT id FROM {PaymentsEngine.Q(c,"hr_fee_request")} WHERE id=@id",("@id",saved.S("id")))==null)
            SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,"hr_fee_request")}(id,company_id,erp_employee_id,category,bearer,rider_share,company_share,created_by) VALUES(@id,@c,@e,@cat,@bearer,@r,@co,@by)",("@id",saved.S("id")),("@c",saved.S("companyId")),("@e",employee),("@cat",input.Required("category")),("@bearer",bearer),("@r",rider),("@co",company),("@by",PaymentsEngine.Me(actor)));
        return saved;
    }
    internal static JsonObject Line(JsonObject input,string code,decimal amount,string? rider=null,string? platformAccount=null)=>new(){["accountCode"]=code,["amount"]=amount,["costCentreId"]=input.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D")),["appCode"]=input.S("appCode","none"),["erpRiderId"]=rider,["erpPlatformAccountId"]=platformAccount};
    /// <summary>Resolve owning registration/platform/rider from the trusted platform_account record (RWP1).</summary>
    internal static string DemandPlatformAttribution(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string companyId,string erpEmployeeId,string erpPlatformAccountId)
    {
        var account=Store.Get(c,erpPlatformAccountId,"platform_account",tx)??throw new ApiError("حساب المنصة غير موجود.",404);
        string owner=account.S("companyId",account.S("sponsorId"));
        string ownerId=FinancialCore.FindSponsor(owner)?.Id??owner;
        J.Need(ownerId==companyId,"حساب المنصة خارج سجل المستند.",409);
        FinancialScopes.DemandSponsor(actor,companyId);
        var plat=PayPlatforms.For(account.S("platformCode"));
        J.Need(FinancialScopes.Platform(actor,plat.Platform),"المنصة خارج نطاقك.",403);
        string registered=account.S("registeredEmployeeId",account.S("registeredRiderId",account.S("erpId")));
        bool linked=registered==erpEmployeeId;
        if(!linked&&account["assignments"] is JsonArray assigns)
            linked=assigns.OfType<JsonObject>().Any(a=>a.S("riderId")==erpEmployeeId||a.S("erpRiderId")==erpEmployeeId||a.S("employeeId")==erpEmployeeId);
        J.Need(linked,"المندوب غير مرتبط بحساب المنصة.",409);
        return plat.Platform;
    }
}
