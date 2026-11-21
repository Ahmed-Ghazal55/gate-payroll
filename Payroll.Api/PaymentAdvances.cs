using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PaymentAdvances
{
    // WP1 explicitly keeps the currently released mapping. 112513 is on an unreleased branch.
    public const string HsTopupAccount="112506";
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.request");string iqama=DirectoryProfiles.Iqama(input.Required("riderIqama"));
        J.Need(OrgDirectory.Supervises(c,tx,PaymentsEngine.Me(actor),iqama),"المندوب ليس تحت إشرافك.",403);
        var rider=Store.Get(c,iqama,"rider",tx)??throw new ApiError("المندوب غير موجود.",404);FinancialScopes.DemandRider(actor,rider);
        string employee=PaymentsEngine.Id(input.Required("erpEmployeeId")),account=PaymentsEngine.Id(input.Required("erpPlatformAccountId"));
        J.Need(rider.S("erpId",rider.S("erpRiderId"))==employee,"يلزم ربط هوية المندوب المعتمدة بمعرف نظام الموارد قبل طلب السلفة.",409);
        string type=input.Required("advanceType");J.Need(type is "cash" or "bank" or "internet" or "hs_topup","نوع السلفة غير صالح.");
        string companyId=PaymentsEngine.Id(input.Required("companyId"));
        string platform=PaymentDocuments.DemandPlatformAttribution(c,tx,actor,companyId,employee,account);
        DemandNoOpenAdvance(c,tx,actor,iqama,employee,input.S("idempotencyKey"));
        decimal amount=PaymentsEngine.Money(input,"amount");string fulfilment=input.S("fulfilment",type);J.Need(type!="internet"||fulfilment is "cash" or "bank" or "recharge_card","طريقة تسليم سلفة الإنترنت غير صالحة.");
        if(fulfilment=="recharge_card")
        {
            string network=input.Required("networkCode");var package=ConfigLists.Current(c,tx,"recharge_packages",input.Required("packageCode"))??throw new ApiError("الباقة غير معتمدة.");
            J.Need(ConfigLists.Current(c,tx,"recharge_networks",network)!=null&&package["values"].S("network")==network&&package["values"].N("price")==amount,"الباقة والشبكة والسعر غير متطابقة.");
        }
        var doc=input.DeepClone().AsObject();doc["type"]=type=="hs_topup"?"hs_topup":"advance";doc["fulfilment"]=fulfilment;doc["platform"]=platform;
        string code=type switch{"internet"=>"112503","hs_topup"=>HsTopupAccount,_=>"112501"};
        doc["lines"]=new JsonArray(PaymentDocuments.Line(input,code,amount,employee,account));var saved=PaymentsEngine.Create(c,tx,actor,doc);
        if(SchemaSql.Scalar(c,tx,$"SELECT id FROM {PaymentsEngine.Q(c,"advance_request")} WHERE id=@id",("@id",saved.S("id")))==null)
            SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,"advance_request")}(id,company_id,erp_employee_id,category,bearer,rider_share,company_share,created_by) VALUES(@id,@c,@e,@cat,'rider',@amount,0,@by)",("@id",saved.S("id")),("@c",saved.S("companyId")),("@e",employee),("@cat",type),("@amount",amount),("@by",PaymentsEngine.Me(actor)));
        return saved;
    }
    internal static void DemandApproval(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject input)
    {
        if(doc.S("type") is not ("advance" or "hs_topup"))return;
        AccessPolicy.Demand(actor,"advances.review");J.Need(!AccessPolicy.SamePerson(actor,doc.S("createdBy")),"صاحب طلب السلفة لا يعتمدها.",403);
        OrgDirectory.DemandResponsible(c,tx,actor,"advance.review",doc);
        var meta=doc["metadata"]!.AsObject();decimal instalment=meta.N("monthlyInstallment");
        J.Need(instalment>0&&instalment<=doc.N("amount"),"حدد قسطًا شهريًا موجبًا لا يتجاوز السلفة قبل تقديم الطلب.",409);
        var review=meta.DeepClone().AsObject();review["id"]=doc.S("id");review["type"]=meta.S("advanceType");
        var summary=AdvanceRequests.Summary(c,tx,actor,review);string reason=input.S("coverageReason").Trim();
        if(!summary.B("covered"))J.Need(reason.Length is >4 and <=500,"الصافي المتوقع لا يغطي السلفة أو غير معروف؛ اكتب سبب الاعتماد.");
        J.Need(OrgDirectory.Responsible(c,tx,"advance.payout:"+PayoutOp(meta.Required("payoutAccountCode"))).Count>0,"لا يوجد مسؤول صرف مكلّف للحساب المختار.",409);

    }
    internal static string PayoutOp(string accountCode)=>accountCode=="112404"?AdvanceRequests.Mada:accountCode;
    /// <summary>BR §25.3: one open advance per rider across legacy and payment-engine routes.</summary>
    public static void DemandNoOpenAdvance(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string iqama,string erpEmployeeId,string? idempotencyKey=null)
    {
        J.Need(!Store.List(c,AdvanceRequests.Kind,tx).Any(r=>r.S("riderIqama")==iqama&&r.S("status") is not ("rejected" or "cancelled" or "paid")),"للمندوب طلب سلفة مفتوح بالفعل.",409);
        if(erpEmployeeId=="")return;
        var existing=PaymentsEngine.Rows(c,tx,$"SELECT d.id FROM {PaymentsEngine.Q(c,"advance_request")} r JOIN {PaymentsEngine.Q(c,"document")} d ON d.id=r.id LEFT JOIN {PaymentsEngine.Q(c,"payable")} p ON p.source_table='pay.document' AND p.source_id=d.id WHERE r.erp_employee_id=@r AND d.status IN (1,2,3) AND (p.id IS NULL OR p.amount_paid<p.amount_due)",("@r",erpEmployeeId));
        var prior=existing.FirstOrDefault();
        if(prior==null)return;
        if(idempotencyKey is {Length:>0})
        {
            var old=PaymentsEngine.Get(c,tx,actor,prior.S("id"));J.Need(old["metadata"].S("idempotencyKey")==idempotencyKey,"للمندوب طلب سلفة مفتوح بالفعل.",409);
            return;
        }
        throw new ApiError("للمندوب طلب سلفة مفتوح بالفعل.",409);
    }
    internal static void RecordDebt(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject payment,Guid journal)
    {
        string type=doc.S("type");var meta=doc["metadata"]!.AsObject();decimal amount=type is "advance" or "hs_topup"?payment.N("amount"):meta.N("riderShare");
        if(type is not ("advance" or "hs_topup" or "hr_fee" or "traffic_violation")||amount<=0)return;
        // A mixed bearer request must settle as one voucher to keep its rider/company split exact.
        if(type is "hr_fee" or "traffic_violation")J.Need(payment.N("amount")==doc.N("amount"),"طلب توزيع الرسوم يصرف كاملًا للحفاظ على حصة المندوب.",409);
        string debt=PaymentsEngine.MoneyId(),company=doc.S("companyId"),employee=PaymentsEngine.Id(meta.Required("erpEmployeeId"));
        string category=type switch{"hs_topup"=>"HsWalletTopUp","hr_fee"=>meta.Required("category"),"traffic_violation"=>"TrafficFine",_=>meta.S("advanceType") switch{"internet"=>"InternetAdvance","cash"=>"CashAdvance",_=>"BankAdvance"}};
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"debt","debt")}(id,erp_rider_id,company_id,category,principal,instalment,created_by) VALUES(@id,@r,@c,@cat,@amount,@inst,@by)",("@id",debt),("@r",employee),("@c",company),("@cat",category),("@amount",amount),("@inst",meta.N("monthlyInstallment")>0?meta.N("monthlyInstallment"):(object)DBNull.Value),("@by",PaymentsEngine.Me(actor)));
        // Source type is document_payment (not debt.disbursement, which has its own required approved source).
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}(id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,payment_id,journal_id,created_by) VALUES(@id,@m,@debt,@c,@day,@amount,'document_payment',@src,@p,@j,@by)",("@id",PaymentsEngine.MoneyId()),("@m",payment.S("periodMonth")[..10]),("@debt",debt),("@c",company),("@day",payment.S("paidOn")[..10]),("@amount",amount),("@src",payment.S("id")),("@p",payment.S("id")),("@j",journal.ToString("D")),("@by",PaymentsEngine.Me(actor)));
    }
    public static JsonObject IssueCard(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"advances.payout");string id=input.Required("paymentId");var payment=PaymentsEngine.Payment(c,tx,actor,id);
        var doc=PaymentsEngine.Get(c,tx,actor,payment.S("documentId"));
        J.Need(doc.S("type")=="advance"&&doc["metadata"].S("advanceType")=="internet"&&doc["metadata"].S("fulfilment")=="recharge_card","السند لا يخص سلفة بطاقة إنترنت.",409);
        J.Need(PaymentsEngine.AccountCode(c,tx,payment.S("bankAccountId"))=="112404","سند البطاقة لا يصرف من رصيد مدى.",409);
        return PaymentsEngine.Pay(c,tx,actor,id);
    }
    internal static void RecordCardWalletWithdrawal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject payment,Guid journal)
    {
        var meta=doc["metadata"]!.AsObject();
        if(doc.S("type")!="advance"||meta.S("advanceType")!="internet"||meta.S("fulfilment")!="recharge_card")return;
        string account=PaymentsEngine.Account(c,tx,"112404").ToString("D");
        JsonObject? wallet=null;string kind="";
        foreach(string table in new[]{"prepaid","fuel"})
        {
            wallet=PaymentsEngine.Rows(c,tx,$"SELECT id,company_id AS companyId,account_id AS accountId,kind FROM {PaymentsEngine.Q(c,PaymentWallets.Table(table))} WHERE company_id=@c AND account_id=@a AND kind='mada' AND status=1",("@c",doc.S("companyId")),("@a",account)).FirstOrDefault();
            if(wallet!=null){kind=table;break;}
        }
        J.Need(wallet!=null,"لا توجد محفظة مدى للشركة لتسجيل سحب الكارت.",409);
        if(SchemaSql.Scalar(c,tx,$"SELECT id FROM {PaymentsEngine.Q(c,PaymentWallets.Table(kind)+"_movement")} WHERE wallet_id=@w AND source_type='card_issue' AND source_id=@src",("@w",wallet!.S("id")),("@src",payment.S("id")))!=null)return;
        PaymentWallets.Movement(c,tx,actor,kind,wallet.S("id"),doc.S("id"),payment.S("id"),journal,payment.S("paidOn")[..10],"card_issue",payment.S("id"),-payment.N("amount"));
    }
    public static JsonObject MonthClosePreview(JsonObject facts)
    {
        var close=HungerStationFacts.MonthClose(facts);close["currentTopupAccount"]=HsTopupAccount;
        close["blocker"]="يلزم عقد مصدر معتمد يربط رصيد المندوب بحساب المنصة والتسويات السابقة قبل إنشاء سلفة الإقفال.";return close;
    }
}
