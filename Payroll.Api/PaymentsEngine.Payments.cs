using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static partial class PaymentsEngine
{
    internal static string AccountCode(DbConnection c,DbTransaction? tx,string id)=>SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",Id(id)))?.ToString()??throw new ApiError("الحساب غير موجود.",409);
    static void DemandPay(ClaimsPrincipal actor,JsonObject doc)
    {
        AccessPolicy.Demand(actor,doc.S("type") is "advance" or "hs_topup"?"advances.payout":"payments.disburse");
        Scope(actor,doc);MoneyWriteGuards.Demand((int)doc.N("runMode"),doc.B("payoutBlocked"),MoneyEffectKind.Payout);
    }
    internal static void DemandBank(ClaimsPrincipal actor,DbConnection c,DbTransaction tx,string code,JsonObject doc)
    {
        // BR §25.9: 112404 only when advanceType=internet and fulfilment=recharge_card.
        bool card=doc.S("type")=="advance"&&doc["metadata"].S("advanceType")=="internet"&&doc["metadata"].S("fulfilment")=="recharge_card";
        J.Need(MoneyWriteGuards.IsBankOrCashAccountCode(code)||card&&code=="112404","اختر حساب بنك أو صندوق صالح للصرف.",409);
        // Claims hold legacy vault codes (BANK.BW / MAIN.*); chart codes resolve through TargetAccount.
        J.Need(AccessPolicy.Administrator(actor)||FinancialScopes.TargetAccount(actor,doc.S("companyId"),code)||card&&code=="112404"&&FinancialScopes.Sponsor(actor,doc.S("companyId")),"حساب الصرف خارج نطاقك.",403);
        _=Account(c,tx,code);
    }
    public static JsonObject Reserve(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string documentId,JsonObject input)
    {
        var doc=Raw(c,tx,documentId);DemandPay(actor,doc);J.Need(doc.N("status")==3,"طلب الدفع لمستند معتمد فقط.",409);
        string key=Text(input,"idempotencyKey"),day=Day(input.Required("paidOn")),code=Text(input,"bankAccountCode"),reference=Text(input,"reference");decimal amount=Money(input,"amount");
        DemandBank(actor,c,tx,code,doc);
        if(doc.S("type") is "advance" or "hs_topup")
        {
            var meta=doc["metadata"]!.AsObject();J.Need(code==meta.S("payoutAccountCode"),"حساب الصرف مختلف عن الحساب المعتمد للسلفة.",409);
            // RWP7a3: one full voucher so a single debt keeps one monthly instalment (1000/250 example).
            J.Need(amount==doc.N("amount"),"السلفة وشحن المحفظة تُصرف كاملة في سند واحد.",409);
            J.Need(!AccessPolicy.SamePerson(actor,doc.S("createdBy")),"صاحب طلب السلفة لا يطلب صرفها.",403);
            OrgDirectory.DemandResponsible(c,tx,actor,"advance.payout:"+PaymentAdvances.PayoutOp(code),doc);
        }
        string hash=ApprovalsArea.PayloadContentHash(documentId,amount,("documentHash",doc.S("contentHash")),("paidOn",day),("bankAccountCode",code),("reference",reference));
        var old=Rows(c,tx,$"SELECT id,content_hash AS contentHash FROM {Q(c,"payment_detail")} WHERE document_id=@d AND idempotency_key=@k",("@d",Id(documentId)),("@k",key)).SingleOrDefault();
        if(old!=null){J.Need(old.S("contentHash")==hash,"مفتاح الدفع مستخدم بمحتوى مختلف.",409);return Payment(c,tx,actor,old.S("id"));}
        decimal available=SchemaPayAllocation.LockPayable(c,tx,doc.S("payableId"));J.Need(amount<=available,"المبلغ يتجاوز الرصيد المتاح بعد الحجوزات.",409);
        J.Need(!MoneyWriteGuards.ResolveLegacyPayoutBlocked(c,tx,day[..7],doc.S("companyId")),"التشغيل الموازي يمنع الصرف.",409);
        string payment=MoneyId(),request=MoneyId(),month=day[..7]+"-01";
        // The filtered open-request key prevents duplicate due requests. An accepted request is status 2;
        // its active allocations, rather than the request header, own the reservation counters.
        SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payment_request")}(id,company_id,payable_id,payable_period_month,period_month,amount,status,run_mode,payout_blocked,created_by) VALUES(@id,@c,@p,@pm,@m,@a,2,@rm,@pb,@by)",("@id",request),("@c",doc.S("companyId")),("@p",doc.S("payableId")),("@pm",doc.S("periodMonth")[..10]),("@m",month),("@a",amount),("@rm",(int)doc.N("runMode")),("@pb",doc.B("payoutBlocked")?1:0),("@by",Me(actor)));
        SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payment")}(id,payment_no,company_id,payer_company_id,request_id,request_period_month,period_month,paid_on,amount,run_mode,payout_blocked,created_by) VALUES(@id,@no,@c,@c,@r,@m,@m,@day,@a,@rm,@pb,@by)",("@id",payment),("@no","PV-"+payment.Replace("-","")),("@c",doc.S("companyId")),("@r",request),("@m",month),("@day",day),("@a",amount),("@rm",(int)doc.N("runMode")),("@pb",doc.B("payoutBlocked")?1:0),("@by",Me(actor)));
        decimal remaining=amount;
        var sourceLines=doc["lines"]!.AsArray().OfType<JsonObject>().ToList();
        foreach(var line in sourceLines)
        {
            decimal claimed=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(a.amount),0) FROM {Q(c,"payment_allocation")} a JOIN {Q(c,"payment_source_line")} l ON l.id=a.payment_line_id WHERE l.document_line_id=@l AND a.claim_status IN (1,2)",("@l",line.S("id"))));
            decimal share=Math.Min(line.N("amount")-claimed,remaining);if(share<=0)continue;
            string sourceLine=MoneyId();
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payment_source_line")}(id,payment_id,document_line_id,period_month,amount,created_by) VALUES(@id,@p,@line,@m,@a,@by)",("@id",sourceLine),("@p",payment),("@line",line.S("id")),("@m",month),("@a",share),("@by",Me(actor)));
            SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payment_allocation")}(id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,claim_status,created_by) VALUES(@id,@pay,@line,@p,@c,@pm,@m,@a,1,@by)",("@id",MoneyId()),("@pay",payment),("@line",sourceLine),("@p",doc.S("payableId")),("@c",doc.S("companyId")),("@pm",doc.S("periodMonth")[..10]),("@m",month),("@a",share),("@by",Me(actor)));
            remaining-=share;
        }
        J.Need(remaining==0,"بنود المستند لا تغطي مبلغ الحجز.",409);
        var approval=ApprovalsArea.OpenMoneyOutDual(c,tx,actor,"payment_out",payment,amount,hash,"اعتماد سند صرف","صرف مستند معتمد",new(){["companyId"]=doc.S("companyId"),["platform"]=doc.S("platform"),["documentId"]=Id(documentId)});
        SchemaSql.Exec(c,tx,$"INSERT INTO {Q(c,"payment_detail")}(id,document_id,idempotency_key,bank_account_id,reference,approval_request_id,content_hash,created_by) VALUES(@id,@d,@k,@b,@ref,@approval,@hash,@by)",("@id",payment),("@d",Id(documentId)),("@k",key),("@b",Account(c,tx,code).ToString("D")),("@ref",reference),("@approval",approval.S("id")),("@hash",hash),("@by",Me(actor)));
        Store.Audit(c,tx,"payment.reserved",payment,Me(actor));return Payment(c,tx,actor,payment);
    }
    public static JsonObject Payment(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var p=Rows(c,tx,$"SELECT p.id,p.payment_no AS paymentNo,p.company_id AS companyId,p.payer_company_id AS payerCompanyId,p.period_month AS periodMonth,p.paid_on AS paidOn,p.amount,p.status,p.run_mode AS runMode,p.payout_blocked AS payoutBlocked,p.journal_id AS journalId,d.document_id AS documentId,d.bank_account_id AS bankAccountId,d.reference,d.approval_request_id AS approvalRequestId,d.content_hash AS contentHash FROM {Q(c,"payment")} p JOIN {Q(c,"payment_detail")} d ON d.id=p.id WHERE p.id=@id",("@id",Id(id))).SingleOrDefault()??throw new ApiError("سند الصرف غير موجود.",404);
        var doc=Raw(c,tx,p.S("documentId"));Scope(actor,doc);p["platform"]=doc.S("platform");
        var actions=new JsonArray();if(p.N("status")==1&&AccessPolicy.Has(actor,doc.S("type") is "advance" or "hs_topup"?"advances.payout":"payments.disburse")){actions.Add("cancel_draft");if(CanPay(c,tx,actor,p))actions.Add("pay_voucher");}
        p["allowedActions"]=actions;return p;
    }
    static bool CanPay(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,JsonObject p)
    {
        if(p.N("runMode")!=0||p.B("payoutBlocked"))return false;
        try{ApprovalsArea.DemandRecordedMoneyOutDual(c,tx!,actor,ApprovalsArea.DualPaymentOut,p.S("id"),p.S("contentHash"));return true;}catch(ApiError){return false;}
    }
    public static JsonObject Pay(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var p=Payment(c,tx,actor,id);var doc=Raw(c,tx,p.S("documentId"));DemandPay(actor,doc);
        MoneyWriteGuards.Demand((int)p.N("runMode"),p.B("payoutBlocked"),MoneyEffectKind.Payout);
        J.Need(p.N("status") is 1 or 2,"سند الصرف ملغي أو معكوس.",409);
        ApprovalsArea.DemandRecordedMoneyOutDual(c,tx,actor,ApprovalsArea.DualPaymentOut,Id(id),p.S("contentHash"));
        string bank=AccountCode(c,tx,p.S("bankAccountId"));DemandBank(actor,c,tx,bank,doc);
        J.Need(p.S("companyId")==doc.S("companyId")&&p.S("payerCompanyId")==doc.S("companyId"),"شركة السند لا تطابق المستند؛ التسوية بين الشركات تحتاج مسارًا معتمدًا.",409);
        string currentHash=ApprovalsArea.PayloadContentHash(doc.S("id"),p.N("amount"),("documentHash",doc.S("contentHash")),("paidOn",p.S("paidOn")[..10]),("bankAccountCode",bank),("reference",p.S("reference")));
        J.Need(currentHash==p.S("contentHash"),"بيانات سند الصرف تغيرت بعد طلب الاعتماد؛ ألغ السند وأعد طلبه.",409);
        if(doc.S("type") is "advance" or "hs_topup")OrgDirectory.DemandResponsible(c,tx,actor,"advance.payout:"+PaymentAdvances.PayoutOp(bank),doc);
        if(p.N("status")==2)return p;
        J.Need(!MoneyWriteGuards.ResolveLegacyPayoutBlocked(c,tx,p.S("paidOn")[..7],doc.S("companyId")),"التشغيل الموازي يمنع الصرف.",409);
        // Lock the paying account across all pending payouts before reading its real GL balance.
        string hint=SchemaSql.IsSql(c)?" WITH (UPDLOCK,HOLDLOCK)":"";
        _=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")}{hint} WHERE id=@id",("@id",p.S("bankAccountId")));
        decimal balance=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(l.debit-l.credit),0) FROM {SchemaSql.Q(c,"fin","journal_line")} l JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=l.journal_id WHERE l.company_id=@c AND l.account_id=@a AND j.status IN (2,3)",("@c",doc.S("companyId")),("@a",p.S("bankAccountId"))));
        J.Need(balance>=p.N("amount"),"رصيد البنك أو الصندوق لا يكفي للصرف.",409);
        // Debit only the exact source lines reserved by this voucher (partial payments retain dimensions).
        var claims=Rows(c,tx,$"SELECT s.document_line_id AS lineId,a.amount FROM {Q(c,"payment_allocation")} a JOIN {Q(c,"payment_source_line")} s ON s.id=a.payment_line_id AND s.payment_id=a.payment_id WHERE a.payment_id=@id AND a.claim_status=1",("@id",Id(id)));
        J.Need(claims.Sum(x=>x.N("amount"))==p.N("amount"),"تخصيصات السند لا تطابق مبلغ الصرف.",409);
        var paidDoc=doc.DeepClone().AsObject();paidDoc["amount"]=p.N("amount");
        paidDoc["lines"]=Array(claims.Select(claim=>{var line=doc["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("id")==claim.S("lineId")).DeepClone().AsObject();line["amount"]=claim.N("amount");return line;}));
        Guid journal=PostDocument(c,tx,actor,paidDoc,"payment_out",Id(id),bank,p.N("amount"),p.S("paidOn")[..10],doc.S("type")=="purchase");
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"payment")} SET status=2,journal_id=@j WHERE id=@id AND status=1",("@j",journal.ToString("D")),("@id",Id(id)));
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"payment_allocation")} SET claim_status=2 WHERE payment_id=@id AND claim_status=1",("@id",Id(id)));
        AfterPayment(c,tx,actor,doc,p,journal);
        Store.Audit(c,tx,"payment.paid",Id(id),Me(actor));return Payment(c,tx,actor,id);
    }
    public static JsonObject CancelPayment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        var p=Payment(c,tx,actor,id);var doc=Raw(c,tx,p.S("documentId"));AccessPolicy.Demand(actor,doc.S("type") is "advance" or "hs_topup"?"advances.payout":"payments.disburse");
        J.Need(p.N("status") is 1 or 4,"الإلغاء لسند غير مصروف فقط؛ السند المصروف يصحح بقيد عكسي.",409);
        if(p.N("status")==4)return p;
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"payment_allocation")} SET claim_status=3 WHERE payment_id=@id AND claim_status=1",("@id",Id(id)));
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"payment")} SET status=4 WHERE id=@id AND status=1",("@id",Id(id)));
        Store.Audit(c,tx,"payment.cancelled",Id(id),Me(actor));return Payment(c,tx,actor,id);
    }
    static partial void AfterPayment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject payment,Guid journal);
}
