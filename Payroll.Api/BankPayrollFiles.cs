using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

public static class BankPayrollFiles
{
    public const string Header="البنك,رقم الحساب,إجمالي الراتب,ملاحظات,اسم الموظف,رقم الهوية أو الإقامة,عنوان الموظف";
    // SARIE clearing codes (IBAN digits 5-6) → SWIFT prefix, checked 2026-11-02 against ANB's clearing table.
    // 40 stays for BR §19 (former Samba, manual confirmation); 50 (Alawwal), 83 and 98 are no longer participants.
    private static readonly Dictionary<string,string> Banks=new(){["05"]="INMA",["10"]="NCBK",["15"]="ALBI",["20"]="RIBL",["30"]="ARNB",["36"]="DBAK",["40"]="NCBK",["45"]="SABB",["55"]="BSFR",["60"]="BJAZ",["65"]="SIBC",["71"]="NBOB",["72"]="QNBA",["73"]="FABM",["75"]="NBOK",["76"]="BMUS",["78"]="STCJ",["79"]="NBIQ",["80"]="RJHI",["81"]="DEUT",["82"]="NBPA",["84"]="TCZB",["85"]="BNPA",["86"]="CHAS",["87"]="ICBK",["88"]="BOTK",["89"]="CRES",["90"]="GULF",["91"]="SCBL",["92"]="BSHR",["93"]="VIIO",["95"]="EBIL"};
    public static string BankFor(string code)=>Banks.GetValueOrDefault(code,"");
    // Same Arabic names as web/src/lib/saudi-banks.ts (R1 export split: IBAN / bank code / bank name).
    private static readonly Dictionary<string,string> BankNamesAr=new()
    {
        ["05"]="مصرف الإنماء",["10"]="البنك الأهلي السعودي",["15"]="بنك البلاد",["20"]="بنك الرياض",["30"]="البنك العربي الوطني",
        ["36"]="بنك D360",["40"]="البنك الأهلي السعودي (سامبا سابقًا)",["45"]="البنك السعودي الأول (ساب)",["55"]="البنك السعودي الفرنسي",
        ["60"]="بنك الجزيرة",["65"]="البنك السعودي للاستثمار",["71"]="بنك عمان الوطني",["72"]="بنك قطر الوطني",["73"]="بنك أبوظبي الأول",
        ["75"]="بنك الكويت الوطني",["76"]="بنك مسقط",["78"]="STC Bank",["79"]="المصرف العراقي للتجارة",["80"]="مصرف الراجحي",
        ["81"]="دويتشه بنك",["82"]="البنك الوطني الباكستاني",["84"]="بنك زراعات التركي",["85"]="بي إن بي باريبا",["86"]="جي بي مورغان",
        ["87"]="بنك الصين الصناعي والتجاري",["88"]="بنك طوكيو ميتسوبيشي",["89"]="كريدي سويس",["90"]="بنك الخليج الدولي (ميم)",
        ["91"]="ستاندرد تشارترد",["92"]="بنك البحرين الوطني",["93"]="بنك فيجن",["95"]="الإمارات دبي الوطني"
    };
    /// <summary>R1: bank short code and Arabic bank name for an IBAN export split; empty strings when not a Saudi IBAN.</summary>
    public static (string Code,string NameAr) ExportBank(string iban)
    {
        string clean=NormalizeIban(iban??"");
        if(!Regex.IsMatch(clean,@"\ASA[0-9]{22}\z"))return("","");
        string code=clean.Substring(4,2);
        return(BankFor(code),BankNamesAr.GetValueOrDefault(code,""));
    }
    public sealed record Download(byte[] Bytes,string Filename);
    private static string NormalizeIban(string value)=>Regex.Replace(value.ToUpperInvariant(),@"\s","");
    public static bool ValidIban(string value)
    {
        string iban=NormalizeIban(value);if(!Regex.IsMatch(iban,@"\ASA[0-9]{22}\z"))return false;
        int remainder=0;foreach(char ch in iban[4..]+iban[..4])foreach(char digit in char.IsAsciiLetter(ch)?((int)ch-'A'+10).ToString(CultureInfo.InvariantCulture):ch.ToString())remainder=(remainder*10+digit-'0')%97;
        return remainder==1;
    }
    public static JsonArray Validate(JsonObject row,bool confirmBankCode40=false)
    {
        var errors=new JsonArray();string iban=row.S("iban");
        if(iban=="")errors.Add("missing_iban");else if(!ValidIban(iban))errors.Add("invalid_iban_mod97");
        string bankCode=iban.Length>=6?iban.Substring(4,2):"";
        // BR §19: an unknown IBAN bank code is entered manually as the 4-letter SWIFT prefix.
        if(!Banks.TryGetValue(bankCode,out var bank)){if(!Regex.IsMatch(row.S("bank"),@"\A[A-Z]{4}\z"))errors.Add("unknown_bank_code");}else if(row.S("bank")!=bank)errors.Add("bank_mismatch");
        if(bankCode=="40"&&!confirmBankCode40)errors.Add("bank_code_40_manual_confirmation_required");
        if(row["amount"] is not JsonValue amount||!amount.TryGetValue<decimal>(out var money)||money<=0||money!=J.Money(money))errors.Add("invalid_amount_positive_2dp_required");
        if(!Regex.IsMatch(row.S("iqama"),@"\A[12][0-9]{9}\z"))errors.Add("invalid_iqama");
        if(row.S("name")=="")errors.Add("missing_name");
        if(row.S("address")=="")errors.Add("missing_address");
        return errors;
    }
    /// <summary>TRIAL8 shared rider bank-meta rule (salary bank file + treasury export). Never invents fields.
    /// Prefer verifiedIban; raw iban only with <paramref name="confirmUnverifiedIbans"/>.</summary>
    public static (string? Reason,string Iban,bool Unverified,string Name,string Address,string BankCode,string BankNameAr) ClassifyRiderForBankFile(JsonObject? rider,string iqamaFallback="",bool confirmUnverifiedIbans=false)
    {
        string iqama=rider?.S("iqama",iqamaFallback)??iqamaFallback;
        string name=rider?.S("name",rider?.S("nameAr")??"")??"";
        string address=rider?.S("address",rider?.S("nationalAddress")??"")??"";
        string verified=NormalizeIban(rider?.S("verifiedIban")??"");
        string raw=NormalizeIban(rider?.S("iban")??"");
        string iban="";bool unverified=false;
        if(verified!="")
        {
            if(ValidIban(verified))iban=verified;
            else if(raw!=""&&ValidIban(raw)){iban=raw;unverified=true;}
            else return("iban_invalid",verified,false,name,address,"","");
        }
        else if(raw!="")
        {
            if(!ValidIban(raw))return("iban_invalid",raw,false,name,address,"","");
            iban=raw;unverified=true;
        }
        if(!Regex.IsMatch(iqama,@"\A[12][0-9]{9}\z"))return("iqama_invalid",iban,unverified,name,address,"","");
        if(name=="")return("name_missing",iban,unverified,name,address,"","");
        if(iban=="")return("iban_missing","",false,name,address,"","");
        if(unverified&&!confirmUnverifiedIbans)return("iban_unverified",iban,true,name,address,"","");
        if(address=="")return("address_missing",iban,unverified,name,address,"","");
        var(code,nameAr)=ExportBank(iban);
        return(null,iban,unverified,name,address,code,nameAr);
    }
    private static (string Sponsor,string From,string To) Request(ClaimsPrincipal user,JsonObject input)
    {
        AccessPolicy.Demand(user,"finance.bankfile.export");AccessPolicy.Demand(user,"payroll.view");
        string sponsor=FinancialCore.ResolveSponsor(input.Required("sponsorId")).Id;FinancialScopes.DemandSponsor(user,sponsor);
        var period=FinanceViews.Periods(input.S("from",input.S("period")),input.S("to",input.S("period")));return(sponsor,period.From,period.To);
    }
    private static string Kind((string Sponsor,string From,string To) request)=>"bank-file:"+request.Sponsor+":"+request.From+":"+request.To;
    // M1: prefer the stage-6 remainder from approved pay cycles (after instalment confirmation).
    // Legacy runs still use the posted 2110/212103 closing payable. Never use unapproved previews or 121x debt.
    private static (JsonArray Payable,JsonArray Excluded,string Basis) Rows(DbConnection c,DbTransaction? tx,ClaimsPrincipal user,(string Sponsor,string From,string To) request,bool confirmUnverifiedIbans=false)
    {
        var riders=Store.List(c,"rider",tx).GroupBy(r=>r.S("iqama",r.S("id"))).ToDictionary(g=>g.Key,g=>g.First());
        var amounts=new Dictionary<string,decimal>(StringComparer.Ordinal);
        var pendingRiders=new HashSet<string>(StringComparer.Ordinal);
        var cycles=Store.List(c,PayCycles.Kind,tx).Where(d=>d.S("companyId")==request.Sponsor&&(d.S("status") is "approved" or "partially_approved")&&string.CompareOrdinal(d.S("period"),request.From)>=0&&string.CompareOrdinal(d.S("period"),request.To)<=0).ToList();
        bool usedCycle=false;
        foreach(var cycle in cycles)
        {
            usedCycle=true;
            foreach(var row in (cycle["riders"] as JsonArray??[]).OfType<JsonObject>())
            {
                string iqama=row.S("rider");
                if(row.S("salaryApprovedAt")==""){pendingRiders.Add(iqama);continue;}
                bool hasDebt=(cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().Any(d=>d.S("rider")==iqama&&d.S("type")=="debt");
                if(hasDebt&&row.S("debtDeductionStatus")!="confirmed"){pendingRiders.Add(iqama);continue;}
                decimal remainder=J.Money(Math.Max(0,row.S("debtDeductionStatus")=="confirmed"?row.N("net"):row.N("salaryNet",row.N("net"))));
                amounts[iqama]=amounts.GetValueOrDefault(iqama)+remainder;
            }
        }
        var eligible=amounts.Keys.ToHashSet(StringComparer.Ordinal);
        if(!usedCycle)
        {
            eligible=Store.List(c,"run",tx).Where(r=>r.S("approvedAt")!=""&&r.S("sponsorId")==request.Sponsor&&string.CompareOrdinal(r.S("period"),request.From)>=0&&string.CompareOrdinal(r.S("period"),request.To)<=0).Select(r=>r.S("iqama")).ToHashSet(StringComparer.Ordinal);
            foreach(var journal in Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>string.CompareOrdinal(j.S("period"),request.To)<=0))
            foreach(var line in (journal["lines"] as JsonArray??[]).OfType<JsonObject>().Where(l=>l.S("accountCode") is "2110" or "212103"&&l.S("sponsorId")==request.Sponsor&&l.S("iqama")!=""))
            {
                string iqama=line.S("iqama");
                J.Period(journal.S("period"));
                if(string.CompareOrdinal(journal.S("period"),request.From)>=0)eligible.Add(iqama);
                amounts[iqama]=amounts.GetValueOrDefault(iqama)+line.N("credit")-line.N("debit");
            }
            foreach(var d in Store.List(c,PayCycles.Kind,tx).Where(d=>d.S("companyId")==request.Sponsor&&d.S("status") is not ("cancelled" or "reversed")&&string.CompareOrdinal(d.S("period"),request.To)<=0))
            foreach(var r in (d["riders"] as JsonArray??[]).OfType<JsonObject>().Where(r=>r.S("salaryApprovedAt")==""&&d.S("status")!="approved"||r.S("debtDeductionStatus")=="pending"))
                pendingRiders.Add(r.S("rider"));
        }
        string basis=usedCycle?"stage6_remainder":Store.List(c,PayCycles.Kind,tx).Any(d=>d.S("status")=="approved"&&d.S("companyId")==request.Sponsor&&string.CompareOrdinal(d.S("period"),request.To)<=0)?"posted_2110_212103_closing_payable":"posted_2110_closing_payable";
        var rows=new JsonArray();var excluded=new JsonArray();foreach(string iqama in eligible.Concat(pendingRiders).Distinct(StringComparer.Ordinal).OrderBy(value=>value,StringComparer.Ordinal))
        {
            riders.TryGetValue(iqama,out var rider);
            if(rider.B("isEmployee")||rider.S("personRole") is "Employee" or "employee" or "administrative")continue;
            decimal balance=amounts.GetValueOrDefault(iqama);
            if(pendingRiders.Contains(iqama)){excluded.Add(new JsonObject{["iqama"]=iqama,["name"]=rider.S("name"),["balance"]=balance,["reason"]="pay_cycle_pending_salary_or_debt_confirmation"});continue;}
            var consol=Store.Get(c,SharedRiderConsolidation.Id(iqama,request.To),SharedRiderConsolidation.Kind,tx);
            if((consol!=null&&consol.S("status")=="open"&&!consol.B("released"))||SalRiderRequests.HasActiveBankBlockingHold(c,tx,iqama,request.To+"-01","all"))
            {
                excluded.Add(new JsonObject{["iqama"]=iqama,["name"]=rider.S("name"),["balance"]=balance,["reason"]="shared_rider_or_salary_hold_open"});
                continue;
            }
            if(balance<=0)
            {
                excluded.Add(new JsonObject{["iqama"]=iqama,["name"]=rider.S("name"),["balance"]=balance,["reason"]=balance==0?"zero_balance":"rider_owes_company"});
                continue;
            }
            var(reason,iban,unverified,name,address,code,bankName)=ClassifyRiderForBankFile(rider,iqama,confirmUnverifiedIbans);
            if(reason!=null)
            {
                excluded.Add(new JsonObject{["iqama"]=iqama,["name"]=name,["balance"]=balance,["reason"]=reason,["ibanUnverified"]=unverified});
                continue;
            }
            var row=new JsonObject{["bank"]=code,["iban"]=iban,["amount"]=balance,["notes"]="",["name"]=name,["iqama"]=iqama,["address"]=address,["ibanUnverified"]=unverified};
            row["errors"]=Validate(row);rows.Add(row);
        }
        return(rows,excluded,basis);
    }
    public const string DoNotSendMark="لا يُرسل — تشغيل موازٍ";
    static bool ComparisonOnly(JsonObject input)=>input.B("comparisonOnly")||input.B("doNotSend")||input.S("mode")=="parallel_comparison";
    /// <summary>WC1H: dual approval for mark-sent binds to rider + account (IBAN) + amount lines and the total.</summary>
    public static string FileContentHash(IEnumerable<JsonObject> rows,decimal total)
    {
        var lines=rows.Select(r=>$"{r.S("iqama")}|{r.S("iban")}|{r.N("amount").ToString("F2",CultureInfo.InvariantCulture)}")
            .OrderBy(s=>s,StringComparer.Ordinal);
        string canonical=string.Join("\n",lines)+"\n"+J.Money(total).ToString("F2",CultureInfo.InvariantCulture);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    /// <summary>BR §38.5: run_mode / payout_blocked come from stored cycles, pay runs and bank files — never from the request body.</summary>
    public static (byte RunMode,bool PayoutBlocked) ResolveStoredMoneyFlags(DbConnection c,DbTransaction tx,(string Sponsor,string From,string To) request)
    {
        byte runMode=0;bool blocked=false;
        foreach(var cycle in Store.List(c,PayCycles.Kind,tx).Where(d=>d.S("companyId")==request.Sponsor
            &&string.CompareOrdinal(d.S("period"),request.From)>=0&&string.CompareOrdinal(d.S("period"),request.To)<=0
            &&d.S("status") is not ("cancelled" or "reversed")))
        {
            if(cycle.N("runMode")==1||cycle.S("runMode")=="1")runMode=1;
            if(cycle.B("payoutBlocked")||cycle.S("payoutBlocked")=="1")blocked=true;
        }
        foreach(var run in Store.List(c,"run",tx).Where(r=>r.S("sponsorId")==request.Sponsor
            &&string.CompareOrdinal(r.S("period"),request.From)>=0&&string.CompareOrdinal(r.S("period"),request.To)<=0))
        {
            if(run.N("runMode")==1||run.S("runMode")=="1")runMode=1;
            if(run.B("payoutBlocked")||run.S("payoutBlocked")=="1")blocked=true;
        }
        if(SchemaSql.TableExists(c,tx,"sal","pay_run"))
        {
            object? sim=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(run_mode) FROM {SchemaSql.Q(c,"sal","pay_run")}
                WHERE company_id=@c AND period_month>=@from AND period_month<=@to AND status<>4
                """,("@c",request.Sponsor),("@from",request.From+"-01"),("@to",request.To+"-01"));
            if(sim!=null&&sim is not DBNull&&Convert.ToInt32(sim)==1)runMode=1;
            object? pb=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(CAST(payout_blocked AS int)) FROM {SchemaSql.Q(c,"sal","pay_run")}
                WHERE company_id=@c AND period_month>=@from AND period_month<=@to AND status<>4 AND run_mode=0
                """,("@c",request.Sponsor),("@from",request.From+"-01"),("@to",request.To+"-01"));
            if(pb!=null&&pb is not DBNull&&Convert.ToInt32(pb)==1)blocked=true;
        }
        if(SchemaSql.TableExists(c,tx,"sal","bank_file"))
        {
            object? fileBlocked=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(CAST(payout_blocked AS int)) FROM {SchemaSql.Q(c,"sal","bank_file")}
                WHERE paying_company_id=@c AND period_month>=@from AND period_month<=@to AND status<>4
                """,("@c",request.Sponsor),("@from",request.From+"-01"),("@to",request.To+"-01"));
            if(fileBlocked!=null&&fileBlocked is not DBNull&&Convert.ToInt32(fileBlocked)==1)blocked=true;
            object? fileMode=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(run_mode) FROM {SchemaSql.Q(c,"sal","bank_file")}
                WHERE paying_company_id=@c AND period_month>=@from AND period_month<=@to AND status<>4
                """,("@c",request.Sponsor),("@from",request.From+"-01"),("@to",request.To+"-01"));
            if(fileMode!=null&&fileMode is not DBNull&&Convert.ToInt32(fileMode)==1)runMode=1;
        }
        return (runMode,blocked);
    }
    // BR §38.5: mark parallel-run bank files «لا يُرسل» outside the CSV body (filename, preview fields, audit), never inside BOM/CRLF/header/rows.
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal user,JsonObject input)
    {
        var request=Request(user,input);bool confirmUnverified=input.B("confirmUnverifiedIbans");
        var(rows,excluded,basis)=Rows(c,null,user,request,confirmUnverified);bool comparison=ComparisonOnly(input);
        decimal excludedTotal=J.Money(excluded.OfType<JsonObject>().Sum(e=>e.N("balance")));
        return new(){["sponsorId"]=request.Sponsor,["from"]=request.From,["to"]=request.To,["basis"]=basis,["comparisonOnly"]=comparison,["doNotSend"]=comparison,["doNotSendMark"]=comparison?DoNotSendMark:"",["rows"]=rows,["excludedRows"]=excluded,["excludedCount"]=excluded.Count,["excludedTotal"]=excludedTotal,["confirmUnverifiedIbans"]=confirmUnverified,["unverifiedIbanCount"]=rows.OfType<JsonObject>().Count(r=>r.B("ibanUnverified")),["previewToken"]=FinancialImports.Issue(c,Kind(request)+(comparison?":parallel":""),rows,user.Identity?.Name??""),["expiresInSeconds"]=1200,["canExport"]=rows.Count>0&&rows.OfType<JsonObject>().All(r=>r["errors"]!.AsArray().Count==0)};
    }
    /// <summary>M1: when the file is marked sent (not «لا يُرسل»), post Dr 212103 / Cr bank for the exported amounts.</summary>
    static void PostPayout(DbConnection c,DbTransaction tx,ClaimsPrincipal user,(string Sponsor,string From,string To) request,IReadOnlyList<JsonObject> rows,string actor,string bankAccount)
    {
        PayPlatforms.Active(bankAccount);J.Need(bankAccount.StartsWith('1'),"حساب البنك للصرف يجب أن يكون أصلًا.");
        string key="bank-payout:"+request.Sponsor+":"+request.From+":"+request.To;
        if(Store.List(c,"journal",tx).Any(j=>j.S("reference")==key&&FinanceViews.Posted(j)))return;
        var lines=new JsonArray();
        foreach(var row in rows)
        {
            decimal amount=row.N("amount");if(amount<=0)continue;
            lines.Add(PayPosting.Line("212103",request.Sponsor,amount,0,"",row.S("iqama"),request.To,key));
            lines.Add(PayPosting.Line(bankAccount,request.Sponsor,0,amount,"",row.S("iqama"),request.To,key));
        }
        if(lines.Count==0)return;
        var journal=FinancialCore.PostJournal(c,tx,"bank_payroll_payout","صرف ملف رواتب التطبيقات",request.To,key,lines,actor,originSponsorId:request.Sponsor,creditorSponsorId:request.Sponsor,targetChart:true,payChart:true);
        journal["reference"]=key;Store.Save(c,"journal",journal,tx);
    }
    public static Download Export(DbConnection c,DbTransaction tx,ClaimsPrincipal user,JsonObject input)
    {
        var request=Request(user,input);string actor=user.Identity?.Name??"";bool comparison=ComparisonOnly(input);
        var(baseline,excluded,_)=Rows(c,tx,user,request,input.B("confirmUnverifiedIbans"));
        FinancialImports.Verify(c,tx,new JsonObject{["rows"]=baseline.DeepClone(),["fingerprint"]=input.S("previewToken"),["confirm"]=true},Kind(request)+(comparison?":parallel":""),actor);
        var rows=input["rows"] as JsonArray??throw new ApiError("صفوف التصدير مطلوبة.");
        var excludedIds=excluded.OfType<JsonObject>().Select(row=>row.S("iqama")).ToHashSet(StringComparer.Ordinal);
        J.Need(!rows.OfType<JsonObject>().Any(row=>excludedIds.Contains(row.S("iqama"))),"لا يمكن تصدير الصفوف المستبعدة ذات الرصيد الصفري أو المديونية على المندوب، حتى بعد تعديل المبلغ.");
        J.Need(rows.Count>0&&rows.Count==baseline.Count,"يجب تصدير جميع صفوف المعاينة دون إضافة أو حذف.");
        int edited=0;decimal total=0;var checkedRows=new List<JsonObject>();
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i] as JsonObject??throw new ApiError("صف غير صالح.");var original=baseline[i]!.AsObject();
            J.Need(row.All(p=>new[]{"bank","iban","amount","notes","name","iqama","address","errors"}.Contains(p.Key)),"يسمح بتعديل المبلغ والملاحظات فقط.");
            foreach(string field in new[]{"iban","name","iqama","address"})J.Need(row[field] is JsonValue&&row.S(field)==original.S(field),"يسمح بتعديل المبلغ والملاحظات فقط؛ أعد المعاينة لتحديث البيانات.");
            J.Need(row["bank"] is JsonValue&&(row.S("bank")==original.S("bank")||!Banks.ContainsKey(original.S("iban").Length>=6?original.S("iban").Substring(4,2):"")),"البنك يتعدل يدويًا فقط لو كود الآيبان غير معروف.");
            J.Need(row["notes"] is JsonValue notes&&notes.TryGetValue<string>(out var note)&&note.Length<=2000,"الملاحظات نص بحد أقصى 2000 حرف.");
            J.Need(!row.S("notes").Contains(DoNotSendMark,StringComparison.Ordinal),"علامة «لا يُرسل» خارج ملف البنك فقط (اسم التصدير والتدقيق والمعاينة).");
            J.Need(Validate(row,input.B("confirmBankCode40")).Count==0,"يوجد خطأ في صفوف ملف البنك؛ صححه وأعد المعاينة قبل التصدير.");
            if(row.N("amount")!=original.N("amount")||row.S("notes")!=original.S("notes")||row.S("bank")!=original.S("bank"))edited++;
            total+=row.N("amount");checkedRows.Add(row);
        }
        // WC1I: a ready-to-send export opens the dual approval (kind, scope, file hash, amount) automatically.
        string payoutRef="bank-payout:"+request.Sponsor+":"+request.From+":"+request.To;
        string fileHash=FileContentHash(checkedRows,total);
        if(!comparison)
            ApprovalsArea.OpenMoneyOutDual(c,tx,user,"bank_sent",payoutRef,total,fileHash,
                "اعتماد إرسال ملف بنك","ملف رواتب جاهز للإرسال — بانتظار اعتماد شخصين.",
                new(){["sponsorId"]=request.Sponsor,["companyId"]=request.Sponsor,["from"]=request.From,["to"]=request.To});
        // M1 / U-W2-31: payout journal only when explicitly marked sent — never on plain export or parallel «لا يُرسل».
        if(!comparison&&(input.B("markSent")||input.B("sent")||input.S("action")=="mark_sent"))
        {
            var flags=ResolveStoredMoneyFlags(c,tx,request);
            MoneyWriteGuards.Demand(flags.RunMode,flags.PayoutBlocked,MoneyEffectKind.BankSent);
            // M-0672 / WC1H: recorded two-person dual bound to this file's content hash (edits after approval void it).
            ApprovalsArea.DemandRecordedMoneyOutDual(c,tx,user,ApprovalsArea.DualBankFile,payoutRef,fileHash);
            // COA1 A14 / B14: require a real bank leaf (not cash box 111101, not restricted Jahez 111204).
            string bank=input.S("bankAccountCode");
            if(bank=="")bank=PipelineSettings.Current().S("payrollBankAccount");
            J.Need(bank!="","اختر حساب بنك للصرف (لا صندوق نقدي افتراضي).");
            J.Need(bank!="111204"&&FinanceViews.Accounts.Any(a=>a.S("code")==bank&&a.S("type")=="bank"&&a.B("posting")&&a.S("status")=="active"),"اختر حساب بنك فعلي؛ أموال تصفيات جاهز معزولة.");
            MoneyWriteGuards.Demand(flags.RunMode,flags.PayoutBlocked,MoneyEffectKind.Payout);
            PostPayout(c,tx,user,request,checkedRows,actor,bank);
        }
        // comparisonOnly + markSent: export CSV only; never post (parallel «لا يُرسل»).
        byte[] bytes=Csv(checkedRows);string filename=(comparison?"DO-NOT-SEND_":"")+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)+"_bulkPayroll.csv";
        // Metadata only. Never persist the exported rows, identifiers, names, IBANs or notes.
        // Keep the normal export audit shape (7 fields). Parallel-run adds doNotSend outside the CSV.
        var audit=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["action"]=comparison?"finance.bankfile.export_comparison_only":"finance.bankfile.export",["actor"]=actor,["at"]=DateTimeOffset.UtcNow.ToString("O"),["rowCount"]=rows.Count,["total"]=total,["editedRowCount"]=edited};
        if(comparison)audit["doNotSend"]=true;
        Store.Save(c,"audit",audit,tx);
        return new(bytes,filename);
    }
    public static byte[] Csv(IEnumerable<JsonObject> rows)
    {
        static string Cell(string value){value=value.Replace("\r\n","\n").Replace('\r','\n').Replace("\n","\r\n");return value.IndexOfAny([',','"','\r','\n'])>=0?'"'+value.Replace("\"","\"\"")+'"':value;}
        var text=new StringBuilder(Header+"\r\n");
        foreach(var row in rows)text.Append(string.Join(",",new[]{row.S("bank"),row.S("iban"),row.N("amount").ToString("F2",CultureInfo.InvariantCulture),row.S("notes"),row.S("name"),row.S("iqama"),row.S("address")}.Select(Cell))).Append("\r\n");
        return new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/bank-file/preview",(HttpContext ctx,JsonObject input)=>store.Read(c=>Results.Json(Preview(c,ctx.User,input)))).WithTags("Banks and treasury").WithSummary("Submit finance bank file preview");
        api.MapPost("/finance/bank-file/export",(HttpContext ctx,JsonObject input)=>{var file=store.Write((c,tx)=>Export(c,tx,ctx.User,input),false);return Results.File(file.Bytes,"text/csv; charset=utf-8",file.Filename);}).WithTags("Banks and treasury").WithSummary("Submit finance bank file export");
    }
}
