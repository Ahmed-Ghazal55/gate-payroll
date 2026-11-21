using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PaymentContracts
{
    static string Table(string kind)=>kind switch{"properties"=>"property","financiers"=>"financier","leases"=>"lease_contract","finance-contracts"=>"finance_contract",_=>throw new ApiError("نوع السجل غير صالح.")};
    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,string kind)
    {
        AccessPolicy.Demand(actor,"payments.view");string table=Table(kind);
        return new(){["items"]=PaymentsEngine.Array(PaymentsEngine.Rows(c,null,$"SELECT id,company_id AS companyId,name_ar AS nameAr,status,metadata FROM {PaymentsEngine.Q(c,table)} ORDER BY name_ar").Where(d=>FinancialScopes.Sponsor(actor,d.S("companyId"))).Select(d=>{d["metadata"]=JsonNode.Parse(d.S("metadata"));d["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("archive"):new();return d;})),["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("create"):new()};
    }
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");string table=Table(kind),company=PaymentsEngine.Id(input.Required("companyId")),name=PaymentsEngine.Text(input,"nameAr",200);
        FinancialScopes.DemandSponsor(actor,company);string id=input.S("id")==""?PaymentsEngine.MoneyId():PaymentsEngine.Id(input.S("id"));
        var existing=PaymentsEngine.Rows(c,tx,$"SELECT metadata FROM {PaymentsEngine.Q(c,table)} WHERE id=@id",("@id",id)).SingleOrDefault();
        if(existing!=null){J.Need(existing.S("metadata")==input.ToJsonString(),"المعرف مستخدم بمحتوى مختلف.",409);return new(){["id"]=id,["allowedActions"]=new JsonArray()};}
        if(kind=="properties")
        {
            J.Need(input.S("propertyType") is "rider_housing" or "admin_housing" or "office","نوع العقار غير صالح.");PaymentsEngine.ValidateCentre(c,tx,company,PaymentsEngine.Id(input.Required("costCentreId")));
            if(input.S("propertyType")=="rider_housing")_=PaymentsEngine.Id(input.Required("erpHousingId"));
        }
        SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,table)}(id,company_id,name_ar,metadata,created_by) VALUES(@id,@c,@n,@meta,@by)",("@id",id),("@c",company),("@n",name),("@meta",input.ToJsonString()),("@by",PaymentsEngine.Me(actor)));
        if(kind is "leases" or "finance-contracts")Generate(c,tx,actor,kind,id,input);
        Store.Audit(c,tx,"payment."+table+".created",id,PaymentsEngine.Me(actor));return new(){["id"]=id,["companyId"]=company,["allowedActions"]=new JsonArray("archive")};
    }
    static void Generate(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string id,JsonObject input)
    {
        bool rent=kind=="leases";string company=input.S("companyId"),start=PaymentsEngine.Day(input.Required("startOn"));DateOnly day=DateOnly.ParseExact(start,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        int months=(int)input.N("months");J.Need(months is >=1 and <=360&&months==input.N("months"),"مدة العقد من شهر إلى ثلاثمائة وستين شهرًا.");
        string linked=PaymentsEngine.Id(input.Required(rent?"propertyId":"financierId"));string master=rent?"property":"financier";
        var related=PaymentsEngine.Rows(c,tx,$"SELECT metadata FROM {PaymentsEngine.Q(c,master)} WHERE id=@id AND company_id=@c AND status=1",("@id",linked),("@c",company)).SingleOrDefault()??throw new ApiError("العقار أو الممول غير موجود في نطاق الشركة.",404);
        var meta=JsonNode.Parse(related.S("metadata"))!.AsObject();
        string supplier=rent?PaymentsEngine.Id(input.Required("landlordSupplierId")):"";
        if(!rent)_=PaymentsEngine.Id(input.Required("erpVehicleId"));
        string cc=rent?meta.Required("costCentreId"):input.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D"));
        PaymentsEngine.ValidateCentre(c,tx,company,PaymentsEngine.Id(cc));
        int frequency=rent?(int)input.N("frequencyMonths"):1;
        J.Need(!rent||frequency is 3 or 6 or 12,"دورة الإيجار ربع سنوية أو نصف سنوية أو سنوية.");J.Need(months%frequency==0,"مدة العقد يجب أن تضم دورات سداد كاملة.");
        decimal principal=PaymentsEngine.Money(input,rent?"rentPerPayment":"principal"),rate=input.N("monthlyEffectiveRate");J.Need(rate>=0&&rate<=1,"معدل الفائدة الشهري الفعلي غير صالح.");
        decimal payment=rent?principal:rate==0?J.Money(principal/months):J.Money(principal*rate/(1-(decimal)Math.Pow((double)(1+rate),-months)));
        decimal outstanding=principal;
        for(int i=0;i<months/frequency;i++)
        {
            DateOnly due=day.AddMonths(i*frequency);decimal interest=rent?0:J.Money(outstanding*rate),part=rent?principal:i==months-1?outstanding:payment-interest;
            J.Need(part>0&&part<=outstanding||rent,"جدول التمويل لا يخفض أصل الالتزام.");
            if(!rent)outstanding=J.Money(outstanding-part);
            var lines=new JsonArray(new JsonObject{["accountCode"]=rent?"112401":input.S("liabilityAccountCode","221201"),["amount"]=part,["costCentreId"]=cc});
            if(!rent)J.Need(input.S("liabilityAccountCode","221201") is "221201" or "214203","حساب أصل التمويل غير صالح.");
            if(interest>0)lines.Add(new JsonObject{["accountCode"]="541301",["amount"]=interest,["costCentreId"]=cc});
            var doc=PaymentsEngine.Create(c,tx,actor,new(){["type"]=rent?"rent":"installment",["companyId"]=company,["supplierId"]=rent?supplier:null,["idempotencyKey"]=id+":"+i,["reference"]=id+":"+i,["period"]=due.ToString("yyyy-MM"),["occurredOn"]=due.ToString("yyyy-MM-dd"),["dueOn"]=due.ToString("yyyy-MM-dd"),["lines"]=lines,["contractId"]=id,["contractKind"]=kind,["runMode"]=input.N("runMode"),["payoutBlocked"]=input.B("payoutBlocked")});
            string schedule=rent?"lease_schedule":"finance_schedule";
            SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,schedule)}(id,contract_id,document_id,period_month,due_date,principal,interest,created_by) VALUES(@id,@contract,@doc,@month,@date,@p,@i,@by)",("@id",PaymentsEngine.MoneyId()),("@contract",id),("@doc",doc.S("id")),("@month",due.ToString("yyyy-MM")+"-01"),("@date",due.ToString("yyyy-MM-dd")),("@p",part),("@i",interest),("@by",PaymentsEngine.Me(actor)));
        }
        if(rent&&input.N("deposit")>0)
        {
            var deposit=PaymentsEngine.Create(c,tx,actor,new(){["type"]="expense",["companyId"]=company,["supplierId"]=supplier,["idempotencyKey"]=id+":deposit",["reference"]=id+":deposit",["period"]=day.ToString("yyyy-MM"),["occurredOn"]=start,["dueOn"]=start,["lines"]=new JsonArray(new JsonObject{["accountCode"]="125101",["amount"]=input.N("deposit"),["costCentreId"]=cc}),["contractId"]=id,["runMode"]=input.N("runMode"),["payoutBlocked"]=input.B("payoutBlocked")});
            _=deposit;
        }
    }
    public static JsonObject Schedule(DbConnection c,ClaimsPrincipal actor,string kind,string id)
    {
        AccessPolicy.Demand(actor,"payments.view");bool rent=kind=="leases";string table=rent?"lease_contract":"finance_contract";
        string company=SchemaSql.Scalar(c,null,$"SELECT company_id FROM {PaymentsEngine.Q(c,table)} WHERE id=@id",("@id",PaymentsEngine.Id(id)))?.ToString()??throw new ApiError("العقد غير موجود.",404);J.Need(FinancialScopes.Sponsor(actor,company),"العقد غير موجود.",404);
        return new(){["items"]=PaymentsEngine.Array(PaymentsEngine.Rows(c,null,$"SELECT s.id,s.document_id AS documentId,s.due_date AS dueOn,s.principal,s.interest,d.status FROM {PaymentsEngine.Q(c,rent?"lease_schedule":"finance_schedule")} s JOIN {PaymentsEngine.Q(c,"document")} d ON d.id=s.document_id WHERE s.contract_id=@id ORDER BY s.due_date",("@id",PaymentsEngine.Id(id)))),["allowedActions"]=new JsonArray()};
    }
    public static JsonObject RecognizeRent(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,string period)
    {
        AccessPolicy.Demand(actor,"payments.disburse");string month=J.Period(period)+"-01";
        var contract=PaymentsEngine.Rows(c,tx,$"SELECT company_id AS companyId,metadata FROM {PaymentsEngine.Q(c,"lease_contract")} WHERE id=@id",("@id",PaymentsEngine.Id(id))).SingleOrDefault()??throw new ApiError("العقد غير موجود.",404);
        FinancialScopes.DemandSponsor(actor,contract.S("companyId"));
        var prior=PaymentsEngine.Rows(c,tx,$"SELECT amount,journal_id AS journalId FROM {PaymentsEngine.Q(c,"rent_recognition")} WHERE contract_id=@id AND period_month=@m",("@id",PaymentsEngine.Id(id)),("@m",month)).SingleOrDefault();if(prior!=null){prior["allowedActions"]=new JsonArray();return prior;}
        var meta=JsonNode.Parse(contract.S("metadata"))!.AsObject();string start=meta.Required("startOn");
        J.Need(start.EndsWith("-01",StringComparison.Ordinal),"توزيع إيجار يبدأ وسط الشهر يحتاج سياسة مدة معتمدة؛ لا يفترض توزيع نسبي.",409);
        DateOnly begin=DateOnly.ParseExact(start,"yyyy-MM-dd",CultureInfo.InvariantCulture),on=DateOnly.ParseExact(month,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        int index=(on.Year-begin.Year)*12+on.Month-begin.Month,frequency=(int)meta.N("frequencyMonths");
        J.Need(index>=0&&index<meta.N("months"),"شهر التوزيع خارج مدة العقد.");
        decimal monthly=J.Money(meta.N("rentPerPayment")/frequency),amount=index%frequency==frequency-1?meta.N("rentPerPayment")-monthly*(frequency-1):monthly;
        DateOnly fundingDue=begin.AddMonths(index/frequency*frequency);
        decimal funded=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(p.amount_paid),0) FROM {PaymentsEngine.Q(c,"lease_schedule")} s JOIN {PaymentsEngine.Q(c,"payable")} p ON p.source_table='pay.document' AND p.source_id=s.document_id WHERE s.contract_id=@id AND s.due_date=@due",("@id",PaymentsEngine.Id(id)),("@due",fundingDue.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))));
        decimal recognized=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(amount),0) FROM {PaymentsEngine.Q(c,"rent_recognition")} WHERE contract_id=@id AND period_month>=@from AND period_month<@to",("@id",PaymentsEngine.Id(id)),("@from",fundingDue.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),("@to",fundingDue.AddMonths(frequency).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture))));
        J.Need(funded-recognized>=amount,"لم يسدد إيجار مقدم يغطي مصروف الشهر.",409);
        var property=JsonNode.Parse(SchemaSql.Scalar(c,tx,$"SELECT metadata FROM {PaymentsEngine.Q(c,"property")} WHERE id=@id AND company_id=@c",("@id",PaymentsEngine.Id(meta.Required("propertyId"))),("@c",contract.S("companyId")))!.ToString()!)!.AsObject();
        string expense=property.S("propertyType") switch{"rider_housing"=>"512109","admin_housing"=>"521102","office"=>"522101",_=>throw new ApiError("نوع العقار غير صالح.",409)};
        Guid centre=Guid.Parse(property.Required("costCentreId"));var journal=JournalPosting.Post(c,tx,new(Guid.Parse(contract.S("companyId")),PaymentsEngine.Period(c,tx,contract.S("companyId"),month),on,on,"rent_contract",Guid.Parse(id),"rent_recognition:"+period,(byte)meta.N("runMode"),meta.B("payoutBlocked"),PaymentsEngine.Me(actor),[
            new(PaymentsEngine.Account(c,tx,expense),centre,"none",amount,0,"توزيع إيجار مقدم"),new(PaymentsEngine.Account(c,tx,"112401"),centre,"none",0,amount,"توزيع إيجار مقدم")]));
        SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,"rent_recognition")}(id,contract_id,period_month,amount,journal_id,created_by) VALUES(@id,@contract,@m,@a,@j,@by)",("@id",PaymentsEngine.MoneyId()),("@contract",PaymentsEngine.Id(id)),("@m",month),("@a",amount),("@j",journal.JournalId.ToString("D")),("@by",PaymentsEngine.Me(actor)));
        Store.Audit(c,tx,"payment.rent.recognized",id,PaymentsEngine.Me(actor));return new(){["amount"]=amount,["journalId"]=journal.JournalId.ToString("D"),["allowedActions"]=new JsonArray()};
    }
    public static JsonObject Archive(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string id)
    {
        AccessPolicy.Demand(actor,"payments.disburse");string table=Table(kind);var row=PaymentsEngine.Rows(c,tx,$"SELECT company_id AS companyId FROM {PaymentsEngine.Q(c,table)} WHERE id=@id",("@id",PaymentsEngine.Id(id))).SingleOrDefault()??throw new ApiError("السجل غير موجود.",404);J.Need(FinancialScopes.Sponsor(actor,row.S("companyId")),"السجل غير موجود.",404);
        SchemaSql.Exec(c,tx,$"UPDATE {PaymentsEngine.Q(c,table)} SET status=2 WHERE id=@id",("@id",PaymentsEngine.Id(id)));Store.Audit(c,tx,"payment."+table+".archived",id,PaymentsEngine.Me(actor));return new(){["id"]=id,["status"]=2,["allowedActions"]=new JsonArray()};
    }
    public static JsonObject DueCalendar(DbConnection c,ClaimsPrincipal actor,string? companyId,string from,string to)
    {
        AccessPolicy.Demand(actor,"payments.view");PaymentsEngine.Day(from);PaymentsEngine.Day(to);J.Need(string.CompareOrdinal(from,to)<=0,"نطاق الاستحقاق غير صالح.");
        var rows=PaymentsEngine.Rows(c,null,$"""
            SELECT d.id AS documentId,d.company_id AS companyId,d.platform,d.type,d.reference,d.due_on AS dueOn,d.amount,d.status,
            COALESCE(p.amount_paid,0) AS amountPaid,COALESCE(p.amount_reserved,0) AS amountReserved,
            d.amount-COALESCE(p.amount_paid,0)-COALESCE(p.amount_reserved,0) AS availableBalance
            FROM {PaymentsEngine.Q(c,"document")} d LEFT JOIN {PaymentsEngine.Q(c,"payable")} p ON p.source_table='pay.document' AND p.source_id=d.id
            WHERE d.due_on>=@from AND d.due_on<=@to AND d.status IN (1,2,3){(companyId==null?"":" AND d.company_id=@c")} ORDER BY d.due_on,d.id
            """,("@from",from),("@to",to),("@c",companyId==null?DBNull.Value:PaymentsEngine.Id(companyId))).Where(d=>PaymentsEngine.Visible(actor,d)&&d.N("amount")>d.N("amountPaid")).ToList();
        foreach(var r in rows)r["allowedActions"]=r.N("status")==3&&r.N("availableBalance")>0&&AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("create_payment_request"):new();
        return new(){["items"]=PaymentsEngine.Array(rows),["amountDue"]=rows.Sum(r=>r.N("amount")-r.N("amountPaid")),["amountReserved"]=rows.Sum(r=>r.N("amountReserved")),["allowedActions"]=new JsonArray()};
    }
}
