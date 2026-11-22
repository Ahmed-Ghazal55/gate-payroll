using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PaymentWallets
{
    internal static string Table(string kind)=>kind switch{"fuel"=>"fuel_wallet","prepaid"=>"prepaid_wallet",_=>throw new ApiError("نوع المحفظة غير صالح.")};
    internal static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string kind,string id)
    {
        var wallet=PaymentsEngine.Rows(c,tx,$"SELECT id,company_id AS companyId,provider,account_id AS accountId,kind,status FROM {PaymentsEngine.Q(c,Table(kind))} WHERE id=@id",("@id",PaymentsEngine.Id(id))).SingleOrDefault()??throw new ApiError("المحفظة غير موجودة.",404);
        J.Need(FinancialScopes.Sponsor(actor,wallet.S("companyId")),"المحفظة غير موجودة.",404);return wallet;
    }
    public static JsonObject Create(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.fuel.manage");string company=PaymentsEngine.Id(input.Required("companyId")),code=input.Required("accountCode");FinancialScopes.DemandSponsor(actor,company);string table=Table(kind),walletKind=input.S("kind",kind);
        J.Need(walletKind is "fuel" or "prepaid" or "mada","نوع المحفظة غير صالح.");
        J.Need(walletKind=="mada"?code=="112404":code.StartsWith("1124",StringComparison.Ordinal)||code.StartsWith("1141",StringComparison.Ordinal),"حساب المحفظة يجب أن يكون أصلًا مدفوعًا مقدمًا أو حساب تسوية معتمدًا.");
        string account=PaymentsEngine.Account(c,tx,code).ToString("D");
        foreach(string other in new[]{"fuel_wallet","prepaid_wallet"})J.Need(SchemaSql.Scalar(c,tx,$"SELECT id FROM {PaymentsEngine.Q(c,other)} WHERE company_id=@c AND account_id=@a",("@c",company),("@a",account))==null,"الحساب مرتبط بمحفظة بالفعل.",409);
        string id=PaymentsEngine.MoneyId();SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,table)}(id,company_id,provider,account_id,kind,created_by) VALUES(@id,@c,@p,@a,@kind,@by)",("@id",id),("@c",company),("@p",PaymentsEngine.Text(input,"provider")),("@a",account),("@kind",walletKind),("@by",PaymentsEngine.Me(actor)));
        Store.Audit(c,tx,"payment.wallet.created",id,PaymentsEngine.Me(actor));return Get(c,tx,actor,kind,id);
    }
    public static JsonObject List(DbConnection c,ClaimsPrincipal actor,string kind,bool madaOnly=false)
    {
        AccessPolicy.Demand(actor,"payments.fuel.manage");var rows=PaymentsEngine.Rows(c,null,$"SELECT id,company_id AS companyId,provider,account_id AS accountId,kind,status FROM {PaymentsEngine.Q(c,Table(kind))}").Where(w=>FinancialScopes.Sponsor(actor,w.S("companyId"))&&(!madaOnly||w.S("kind")=="mada")).ToList();
        foreach(var w in rows){w["bookBalance"]=Balance(c,null,Table(kind),w.S("id"),"9999-12-31");w["allowedActions"]=w.N("status")==1?new JsonArray("create_topup","reconcile"):new();}
        return new(){["items"]=PaymentsEngine.Array(rows),["allowedActions"]=new JsonArray("create_wallet")};
    }
    public static JsonObject Fund(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.fuel.manage");var w=Get(c,tx,actor,kind,id);J.Need(w.N("status")==1,"المحفظة مغلقة.",409);
        var doc=input.DeepClone().AsObject();doc["companyId"]=w.S("companyId");doc["type"]=w.S("kind")=="mada"?"mada_topup":kind=="fuel"?"fuel_topup":"prepaid_topup";doc["walletId"]=w.S("id");doc["walletKind"]=kind;
        doc["lines"]=new JsonArray(PaymentDocuments.Line(input,PaymentsEngine.AccountCode(c,tx,w.S("accountId")),PaymentsEngine.Money(input,"amount")));
        return PaymentsEngine.Create(c,tx,actor,doc);
    }
    internal static void RecordPayment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject p,Guid journal)
    {
        var meta=doc["metadata"]!.AsObject();string kind=meta.S("walletKind"),id=meta.S("walletId");if(id=="")return;
        // Only authorized wallet funding documents may increase the register (RWP1).
        J.Need(doc.S("type") is "mada_topup" or "fuel_topup" or "prepaid_topup","ربط المحفظة مسموح لمستندات التعبئة المعتمدة فقط.",409);
        AccessPolicy.Demand(actor,"payments.fuel.manage");
        var w=Get(c,tx,actor,kind,id);J.Need(w.S("companyId")==doc.S("companyId"),"المحفظة خارج شركة المستند.",409);
        string walletAccount=w.S("accountId");
        J.Need(doc["lines"]!.AsArray().OfType<JsonObject>().Any(l=>l.S("accountId")==walletAccount),"قيد المستند لا يمدين حساب المحفظة.",409);
        Movement(c,tx,actor,kind,id,doc.S("id"),p.S("id"),journal,p.S("paidOn")[..10],"funding",p.S("id"),p.N("amount"));
    }
    internal static void Movement(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string wallet,string? document,string? payment,Guid journal,string day,string sourceType,string source,decimal amount)
    {
        SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,Table(kind)+"_movement")}(id,wallet_id,document_id,payment_id,journal_id,occurred_on,period_month,source_type,source_id,amount,created_by) VALUES(@id,@w,@doc,@pay,@j,@day,@pm,@st,@src,@a,@by)",("@id",PaymentsEngine.MoneyId()),("@w",PaymentsEngine.Id(wallet)),("@doc",PaymentsEngine.NullId(document??"")),("@pay",PaymentsEngine.NullId(payment??"")),("@j",journal.ToString("D")),("@day",day),("@pm",day[..7]+"-01"),("@st",sourceType),("@src",source),("@a",amount),("@by",PaymentsEngine.Me(actor)));
    }
    internal static decimal Balance(DbConnection c,DbTransaction? tx,string table,string id,string until)=>Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT COALESCE(SUM(amount),0) FROM {PaymentsEngine.Q(c,table+"_movement")} WHERE wallet_id=@id AND occurred_on<=@day",("@id",PaymentsEngine.Id(id)),("@day",until)));
    public static JsonObject Reconcile(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string kind,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.fuel.manage");_=Get(c,tx,actor,kind,id);string month=J.Period(input.Required("period"))+"-01";var start=DateOnly.ParseExact(month,"yyyy-MM-dd",CultureInfo.InvariantCulture);string end=start.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        J.Need(input["statementBalance"]!=null&&input.N("statementBalance")>=0&&input.N("statementBalance")==J.Money(input.N("statementBalance")),"رصيد كشف المزود مطلوب وبمنزلتين عشريتين.");
        decimal statement=input.N("statementBalance"),book=Balance(c,tx,Table(kind),id,end),opening=Balance(c,tx,Table(kind),id,start.AddDays(-1).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        SchemaSql.Exec(c,tx,$"INSERT INTO {PaymentsEngine.Q(c,"wallet_reconciliation")}(id,wallet_id,wallet_kind,period_month,statement_balance,book_balance,reference,created_by) VALUES(@id,@w,@kind,@m,@s,@b,@ref,@by)",("@id",PaymentsEngine.MoneyId()),("@w",PaymentsEngine.Id(id)),("@kind",kind),("@m",month),("@s",statement),("@b",book),("@ref",PaymentsEngine.Text(input,"reference")),("@by",PaymentsEngine.Me(actor)));
        Store.Audit(c,tx,"payment.wallet.reconciled",id,PaymentsEngine.Me(actor));return new(){["walletId"]=id,["period"]=month[..7],["openingBalance"]=opening,["bookBalance"]=book,["statementBalance"]=statement,["difference"]=J.Money(book-statement),["result"]=book==statement?"matched":"difference",["allowedActions"]=new JsonArray("reconcile")};
    }
}

public static partial class PaymentsEngine
{
    static partial void AfterPayment(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject payment,Guid journal)
    {
        PaymentWallets.RecordPayment(c,tx,actor,doc,payment,journal);
        PaymentAdvances.RecordCardWalletWithdrawal(c,tx,actor,doc,payment,journal);
        PaymentAdvances.RecordDebt(c,tx,actor,doc,payment,journal);
        RecurringPayees.ApplyFxGainReclass(c,tx,actor,doc,payment);
    }
}
