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
    private static readonly Dictionary<string,string> Banks=new(){["05"]="INMA",["10"]="NCBK",["15"]="ALBI",["20"]="RIBL",["30"]="ARNB",["40"]="NCBK",["45"]="SABB",["50"]="SABB",["55"]="BSFR",["60"]="BJAZ",["65"]="SIBC",["71"]="NBOB",["75"]="NBOK",["76"]="BMUS",["80"]="RJHI",["81"]="DEUT",["82"]="NBOP",["83"]="SBOI",["84"]="TCZT",["86"]="CHAS",["87"]="ICBK",["90"]="GULF",["95"]="EBIL",["98"]="BNPA"};
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
        return errors;
    }
    private static (string Sponsor,string From,string To) Request(ClaimsPrincipal user,JsonObject input)
    {
        AccessPolicy.Demand(user,"finance.bankfile.export");AccessPolicy.Demand(user,"payroll.view");
        string sponsor=FinancialCore.ResolveSponsor(input.Required("sponsorId")).Id;FinancialScopes.DemandSponsor(user,sponsor);
        var period=FinanceViews.Periods(input.S("from",input.S("period")),input.S("to",input.S("period")));return(sponsor,period.From,period.To);
    }
    private static string Kind((string Sponsor,string From,string To) request)=>"bank-file:"+request.Sponsor+":"+request.From+":"+request.To;
    // The closing posted 2110 liability includes prior approved dues and subtracts deductions/payments.
    // Never use an unapproved preview result, the legacy ledger, or the 121x debt accounts as payable.
    private static JsonArray Rows(DbConnection c,DbTransaction? tx,ClaimsPrincipal user,(string Sponsor,string From,string To) request)
    {
        var riders=Store.List(c,"rider",tx).GroupBy(r=>r.S("iqama",r.S("id"))).ToDictionary(g=>g.Key,g=>g.First());
        var amounts=new Dictionary<string,decimal>();
        var eligible=Store.List(c,"run",tx).Where(r=>r.S("approvedAt")!=""&&r.S("sponsorId")==request.Sponsor&&string.CompareOrdinal(r.S("period"),request.From)>=0&&string.CompareOrdinal(r.S("period"),request.To)<=0).Select(r=>r.S("iqama")).ToHashSet(StringComparer.Ordinal);
        foreach(var journal in Store.List(c,"journal",tx).Where(FinanceViews.Posted).Where(j=>string.CompareOrdinal(j.S("period"),request.To)<=0))
        foreach(var line in (journal["lines"] as JsonArray??[]).OfType<JsonObject>().Where(l=>l.S("accountCode")=="2110"&&l.S("sponsorId")==request.Sponsor&&l.S("iqama")!=""))
        {
            string iqama=line.S("iqama");
            J.Period(journal.S("period"));
            if(string.CompareOrdinal(journal.S("period"),request.From)>=0)eligible.Add(iqama);
            amounts[iqama]=amounts.GetValueOrDefault(iqama)+line.N("credit")-line.N("debit");
        }
        var rows=new JsonArray();foreach(var pair in amounts.Where(p=>p.Value>0&&eligible.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            riders.TryGetValue(pair.Key,out var rider);
            if(rider.B("isEmployee")||rider.S("personRole") is "Employee" or "employee" or "administrative")continue;
            string iban=NormalizeIban(rider.S("verifiedIban"));if(iban=="")iban=NormalizeIban(rider.S("iban"));
            string code=iban.Length>=6?iban.Substring(4,2):"";
            var row=new JsonObject{["bank"]=Banks.GetValueOrDefault(code,""),["iban"]=iban,["amount"]=pair.Value,["notes"]="",["name"]=rider.S("name"),["iqama"]=pair.Key,["address"]=rider.S("address",rider.S("nationalAddress"))};
            row["errors"]=Validate(row);rows.Add(row);
        }
        return rows;
    }
    public static JsonObject Preview(DbConnection c,ClaimsPrincipal user,JsonObject input)
    {
        var request=Request(user,input);var rows=Rows(c,null,user,request);
        return new(){["sponsorId"]=request.Sponsor,["from"]=request.From,["to"]=request.To,["basis"]="posted_2110_closing_payable",["rows"]=rows,["previewToken"]=FinancialImports.Issue(c,Kind(request),rows,user.Identity?.Name??""),["expiresInSeconds"]=1200,["canExport"]=rows.Count>0&&rows.OfType<JsonObject>().All(r=>r["errors"]!.AsArray().Count==0)};
    }
    public static Download Export(DbConnection c,DbTransaction tx,ClaimsPrincipal user,JsonObject input)
    {
        var request=Request(user,input);string actor=user.Identity?.Name??"";
        var baseline=Rows(c,tx,user,request);
        FinancialImports.Verify(c,tx,new JsonObject{["rows"]=baseline.DeepClone(),["fingerprint"]=input.S("previewToken"),["confirm"]=true},Kind(request),actor);
        var rows=input["rows"] as JsonArray??throw new ApiError("صفوف التصدير مطلوبة.");
        J.Need(rows.Count>0&&rows.Count==baseline.Count,"يجب تصدير جميع صفوف المعاينة دون إضافة أو حذف.");
        int edited=0;decimal total=0;var checkedRows=new List<JsonObject>();
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i] as JsonObject??throw new ApiError("صف غير صالح.");var original=baseline[i]!.AsObject();
            J.Need(row.All(p=>new[]{"bank","iban","amount","notes","name","iqama","address","errors"}.Contains(p.Key)),"يسمح بتعديل المبلغ والملاحظات فقط.");
            foreach(string field in new[]{"iban","name","iqama","address"})J.Need(row[field] is JsonValue&&row.S(field)==original.S(field),"يسمح بتعديل المبلغ والملاحظات فقط؛ أعد المعاينة لتحديث البيانات.");
            J.Need(row["bank"] is JsonValue&&(row.S("bank")==original.S("bank")||!Banks.ContainsKey(original.S("iban").Length>=6?original.S("iban").Substring(4,2):"")),"البنك يتعدل يدويًا فقط لو كود الآيبان غير معروف.");
            J.Need(row["notes"] is JsonValue notes&&notes.TryGetValue<string>(out var note)&&note.Length<=2000,"الملاحظات نص بحد أقصى 2000 حرف.");
            J.Need(Validate(row,input.B("confirmBankCode40")).Count==0,"يوجد خطأ في صفوف ملف البنك؛ صححه وأعد المعاينة قبل التصدير.");
            if(row.N("amount")!=original.N("amount")||row.S("notes")!=original.S("notes")||row.S("bank")!=original.S("bank"))edited++;
            total+=row.N("amount");checkedRows.Add(row);
        }
        byte[] bytes=Csv(checkedRows);string filename=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)+"_bulkPayroll.csv";
        // Metadata only. Never persist the exported rows, identifiers, names, IBANs or notes.
        Store.Save(c,"audit",new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["action"]="finance.bankfile.export",["actor"]=actor,["at"]=DateTimeOffset.UtcNow.ToString("O"),["rowCount"]=rows.Count,["total"]=total,["editedRowCount"]=edited},tx);
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
        api.MapPost("/finance/bank-file/preview",(HttpContext ctx,JsonObject input)=>store.Read(c=>Results.Json(Preview(c,ctx.User,input))));
        api.MapPost("/finance/bank-file/export",(HttpContext ctx,JsonObject input)=>{var file=store.Write((c,tx)=>Export(c,tx,ctx.User,input),false);return Results.File(file.Bytes,"text/csv; charset=utf-8",file.Filename);});
    }
}
