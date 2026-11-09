using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class Vouchers
{
    static byte[]? signingKey;
    public static void Configure(string? secret)
    {
        // Provisioned by Ghazal in server settings. Never persisted, returned or logged.
        signingKey=string.IsNullOrEmpty(secret)?null:secret.Length is >=32 and <=256?Encoding.UTF8.GetBytes(secret):throw new InvalidOperationException("Vouchers secret must have32–256characters.");
    }
    static readonly HashSet<string> Kinds=["journal","manual_journal","advance_request","debt_settlement","ledger","payment_voucher","platform_invoice","debt","finance_adjustment"];
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Url(string number)=>"/api/vouchers/verify/"+Uri.EscapeDataString(number);
    static string Company(JsonObject doc)=>doc.S("companyId",doc.S("collectionSponsorId",doc.S("sponsorId",doc.S("transactionType")!=""?doc.S("originSponsorId"):doc.S("creditorSponsorId",doc.S("originSponsorId")))));
    static string? Type(string kind,JsonObject doc)=>kind switch
    {"debt" when AccountingEvents.IsAdvance(doc.S("category"))&&doc.S("entryMode")=="Disbursement"&&doc.S("advanceRequestId")==""=>"AV",
        "journal" or "manual_journal" or "platform_invoice" or "finance_adjustment"=>"JV","advance_request"=>"AV","debt_settlement"=>doc.S("method")=="payroll_deduction"?"JV":"RV","payment_voucher"=>"PV","ledger" when doc.S("kind")=="payment"=>"PV",_=>null};
    static string Year(JsonObject doc)
    {
        string raw=doc.S("date",doc.S("collectionDate",doc.S("createdAt",doc.S("period"))));
        J.Need(raw.Length>=4&&int.TryParse(raw[..4],out int year)&&year is >=2000 and <=9999,"تاريخ السند غير صالح.");return raw[..4];
    }
    static JsonObject? Existing(DbConnection c,DbTransaction tx,string kind,string id)=>Store.BufferedRecord(tx,kind,id)??Store.Get(c,id,kind,tx);
    public static void OnSaving(DbConnection c,DbTransaction tx,string kind,JsonObject doc)
    {
        if(!Kinds.Contains(kind)||Type(kind,doc) is not {} type)return;
        string id=doc.Required("id"),company=Company(doc);var old=Existing(c,tx,kind,id);
        if(old!=null)
        {
            J.Need(doc.S("voucherNo")==old.S("voucherNo")&&doc.S("odooVoucherNo")==old.S("odooVoucherNo"),"أرقام السندات محفوظة ولا تتعدل.");
            if(old.S("voucherNo")!="")J.Need(Company(old)==company&&Year(old)==Year(doc),"شركة وسنة السند لا تتغير.");
            return; // Legacy documents are never retroactively numbered or rewritten.
        }
        if(company=="")return; // Unassigned legacy snapshots have no issuing company.
        company=FinancialCore.ResolveSponsor(company).Id;
        if(kind=="journal"&&doc.S("manualJournalId")!="")
        {
            var manual=Store.Get(c,doc.S("manualJournalId"),ManualJournals.Kind,tx)??throw new ApiError("مسودة القيد غير موجودة.");
            J.Need(manual.S("companyId")==company&&manual.S("voucherNo")!="","مرجع سند القيد اليدوي غير صالح.");
            doc["voucherNo"]=manual.S("voucherNo");doc["voucherVerifyUrl"]=Url(manual.S("voucherNo"));doc["odooVoucherNo"]=manual.S("odooVoucherNo");return;
        }
        J.Need(doc.S("voucherNo")=="","رقم السند يتولد من الخادم فقط.");
        if(signingKey==null)throw new ApiError("أكمل إعدادVouchers:Secretقبل إنشاء مستند مالي.",503);
        string year=Year(doc),sequenceId="voucher-seq:"+Hash(company+":"+type+":"+year);var sequence=Store.Get(c,sequenceId,"voucher_sequence",tx)??new JsonObject{["id"]=sequenceId,["last"]=0};
        long next=checked((long)sequence.N("last")+1);J.Need(next<=999999999999,"نفد نطاق تسلسل السند.");
        string stem=type+"-"+year+"-"+next.ToString("D6",CultureInfo.InvariantCulture);string check=Convert.ToHexString(HMACSHA256.HashData(signingKey,Encoding.UTF8.GetBytes(company+":"+stem)))[..8],number=stem+"-"+check;
        string indexId="voucher:"+Hash(number);J.Need(Store.Get(c,indexId,"voucher_index",tx)==null,"تعارض رقم تحقق؛ لا تعاد العملية آليًا.");
        string odoo=doc.S("odooVoucherNo").Trim();J.Need(odoo.Length<=200&&!odoo.Any(char.IsControl),"مرجع أودو غير صالح.");
        if(odoo!="")
        {
            string party=doc.S("riderIqama",doc.S("iqama",doc.S("counterpartyId","company"))),odooId="odoo-voucher:"+Hash(company+":"+party+":"+odoo.ToUpperInvariant());
            J.Need(Store.Get(c,odooId,"odoo_voucher_index",tx)==null,"رقم أودو مسجل لنفس الجهة؛ لا تكرر العملية.");
            Store.Save(c,"odoo_voucher_index",new(){["id"]=odooId,["voucherNo"]=number,["recordId"]=id,["recordKind"]=kind},tx,preserveSnapshot:true);
        }
        sequence["last"]=next;Store.Save(c,"voucher_sequence",sequence,tx,preserveSnapshot:true);
        Store.Save(c,"voucher_index",new(){["id"]=indexId,["voucherNo"]=number,["companyId"]=company,["type"]=type,["recordId"]=id,["recordKind"]=kind},tx,preserveSnapshot:true);
        doc["voucherNo"]=number;doc["voucherVerifyUrl"]=Url(number);if(odoo!="")doc["odooVoucherNo"]=odoo;
    }
    public static JsonObject RecordPayout(DbConnection c,DbTransaction tx,JsonObject debt,JsonObject journal,string company,string odoo="")
    {
        string id="payout:"+debt.S("id");var existing=Store.Get(c,id,"payment_voucher",tx);if(existing!=null)return existing;
        var payout=new JsonObject{["id"]=id,["companyId"]=company,["riderIqama"]=debt.S("iqama"),["amount"]=debt.N("totalCostAmount",debt.N("totalAmount")),["date"]=journal.S("date"),["advanceRequestId"]=debt.S("advanceRequestId"),["debtId"]=debt.S("id"),["journalEntryId"]=journal.S("id"),["status"]="posted",["odooVoucherNo"]=odoo};
        Store.Save(c,"payment_voucher",payout,tx);return payout;
    }
    public static JsonObject Verify(DbConnection c,string number)
    {
        JsonObject Invalid()=>new(){["status"]="invalid",["type"]=""};
        if(!Regex.IsMatch(number,@"^(PV|RV|JV|AV)-[2-9][0-9]{3}-[0-9]{6,12}-[A-F0-9]{8}$",RegexOptions.CultureInvariant))return Invalid();
        var index=Store.Get(c,"voucher:"+Hash(number),"voucher_index");if(index==null||index.S("voucherNo")!=number)return Invalid();
        var doc=Store.Get(c,index.S("recordId"),index.S("recordKind"));if(doc==null||doc.S("voucherNo")!=number)return Invalid();
        string status=doc.S("status",doc.S("approvalStatus","posted")),journal=index.S("recordKind")=="journal"?doc.S("id"):doc.S("journalEntryId");
        if(journal!=""&&Store.List(c,"journal").Any(j=>j.S("reversesJournalId")==journal))status="reversed";
        // Public fixed projection: never amount, party, company, actor, source or internal identifier.
        string[] allowed=["discarded","draft","pending","invoiced","final","approved","under_review","awaiting_response","awaiting_supervisor","paid","rejected","cancelled","posted","active","settled","reversed","disputed"];
        if(!allowed.Contains(status))status="recorded";
        return new(){["status"]=status,["type"]=number.Split('-')[0]};
    }
    public static IEnumerable<GlobalSearch.Hit> Search(DbConnection c,ClaimsPrincipal actor,string query)
    {
        foreach(string kind in Kinds)
        foreach(var doc in Store.List(c,kind).Where(d=>new[]{d.S("voucherNo"),d.S("odooVoucherNo")}.Any(v=>v.Contains(query,StringComparison.OrdinalIgnoreCase))))
        {
            if(kind=="platform_invoice"&&!InvoiceDiscard.Active(doc))continue;
            string company=Company(doc);if(!FinancialScopes.Sponsor(actor,company))continue;
            bool visible=kind switch
            {
                "finance_adjustment"=>(AccessPolicy.Has(actor,ManualJournals.CreatePermission)||AccessPolicy.Has(actor,ManualJournals.ApprovePermission))&&MadaAccounting.Visible(actor,doc),
                "manual_journal"=>(AccessPolicy.Has(actor,ManualJournals.CreatePermission)||AccessPolicy.Has(actor,ManualJournals.ApprovePermission))&&doc["lines"]!.AsArray().OfType<JsonObject>().All(l=>FinancialScopes.TargetAccount(actor,company,l.S("accountCode"))),
                "journal"=>AccessPolicy.Has(actor,"treasury.journals")&&FinancialScopes.Journal(actor,doc)!=null,
                "advance_request"=>AdvanceRequests.CanSee(c,actor,doc),
                "platform_invoice"=>AccessPolicy.Has(actor,"pay.view")&&FinancialScopes.Platform(actor,doc.S("platform")),
                "debt"=>AccessPolicy.Has(actor,"debts.view")&&FinancialScopes.Debt(actor,doc),
                "debt_settlement"=>AccessPolicy.Has(actor,"debts.view")&&Store.Get(c,doc.S("debtId"),"debt") is {} debt&&FinancialScopes.Debt(actor,debt),
                "ledger"=>AccessPolicy.Has(actor,"payroll.view")&&FinancialScopes.Ledger(actor,doc),
                "payment_voucher"=>AccessPolicy.Has(actor,"treasury.journals"),_=>false
            };
            if(visible)yield return new("voucher",doc.S("id"),doc.S("voucherNo",doc.S("odooVoucherNo")),doc.S("odooVoucherNo"),"/app/accounting?voucherNo="+Uri.EscapeDataString(doc.S("voucherNo",doc.S("odooVoucherNo"))));
        }
    }
    public static void Map(WebApplication app,Store store)=>app.MapGet("/api/vouchers/verify/{no}",(string no)=>store.Read(c=>Verify(c,no))).AllowAnonymous().RequireRateLimiting("vouchers").WithTags("Accounting and reports").WithSummary("Public rate-limited voucher validity/status/type only; no personal or financial values");
}
