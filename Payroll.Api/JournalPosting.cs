using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace Payroll;

/// <summary>U-W2-22a: one-TX posting into fin.journal / fin.journal_line / fin.period_balance.
/// Leaf accounts only; open period; ΣD=ΣC; MidpointRounding.AwayFromZero to 2 dp; reverse-only correction;
/// idempotent by (source_type, source_id, company_id, purpose). Design §7.1 / BR §17 / §24 / §28.</summary>
public static class JournalPosting
{
    public const byte StatusDraft=1,StatusPosted=2,StatusReversed=3,StatusCancelled=4;
    public sealed record Line(Guid AccountId,Guid CostCentreId,string AppCode,decimal Debit,decimal Credit,string? NarrationAr,Guid? ErpPlatformAccountId=null,Guid? ErpRiderId=null);
    public sealed record Draft(
        Guid CompanyId,Guid PeriodId,DateOnly PeriodMonth,DateOnly JournalDate,
        string SourceType,Guid SourceId,string Purpose,byte RunMode,bool PayoutBlocked,
        string Actor,IReadOnlyList<Line> Lines,Guid? ReversesJournalId=null,string? NarrationAr=null,string? VoucherNo=null);

    public sealed record Result(Guid JournalId,string VoucherNo,bool AlreadyPosted);

    public static Result Post(DbConnection c,DbTransaction? tx,Draft draft)
    {
        MoneyWriteGuards.Demand(draft.RunMode,draft.PayoutBlocked,MoneyWriteGuards.ForJournalPurpose(draft.Purpose));
        J.Need(draft.Lines.Count>=2,"القيد من سطرين على الأقل.");
        var rounded=draft.Lines.Select(l=>l with
        {
            Debit=J.Money(l.Debit),
            Credit=J.Money(l.Credit),
            AppCode=string.IsNullOrWhiteSpace(l.AppCode)?"none":l.AppCode.Trim()
        }).ToList();
        decimal sumD=rounded.Sum(l=>l.Debit),sumC=rounded.Sum(l=>l.Credit);
        J.Need(sumD==sumC,"مجموع المدين لا يساوي مجموع الدائن.");
        J.Need(rounded.All(l=>(l.Debit==0)^(l.Credit==0)&&l.Debit+l.Credit>0),"كل سطر مدين أو دائن فقط.");

        // Idempotent retry: unique source key already posted → success if amounts match.
        if(FindExisting(c,tx,draft) is {} existing)
        {
            J.Need(ExistingMatches(c,tx,existing.JournalId,rounded),"قيد بنفس المصدر موجود بمبالغ مختلفة.",409);
            return existing with { AlreadyPosted=true };
        }

        object? periodState=SchemaSql.Scalar(c,tx,$"SELECT state FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE id=@id AND company_id=@c AND period_month=@m",
            ("@id",SchemaSql.GuidText(draft.PeriodId)),("@c",SchemaSql.GuidText(draft.CompanyId)),("@m",Month(draft.PeriodMonth)));
        J.Need(periodState!=null&&Convert.ToInt32(periodState)==1,"الفترة المحاسبية مغلقة أو غير موجودة.",409);

        foreach(var line in rounded)
        {
            object? leaf=SchemaSql.Scalar(c,tx,$"SELECT is_postable FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id AND is_active=1",("@id",SchemaSql.GuidText(line.AccountId)));
            J.Need(leaf!=null&&Convert.ToInt32(leaf)==1,"الترحيل للحسابات الورقية النشطة فقط.",409);
        }

        Guid journalId=SchemaSql.NewId();
        // Use the GUID tail — CreateVersion7 prefixes share a timestamp, so the head is not unique within a ms.
        string voucher=draft.VoucherNo??($"J-{draft.PeriodMonth:yyyyMM}-{journalId.ToString("N")[^8..].ToUpperInvariant()}");
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        // Draft first — lines are frozen once the journal is posted (TR_fin_journal_line_frozen).
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","journal")}
            (id,voucher_no,period_id,period_month,journal_date,source_type,source_id,company_id,purpose,status,run_mode,payout_blocked,currency_code,reverses_journal_id,narration_ar,created_by,created_at)
            VALUES(@id,@v,@pid,@pm,@jd,@st,@sid,@cid,@purpose,@status,@rm,@pb,'SAR',@rev,@nar,@by,@at)
            """,
            ("@id",SchemaSql.GuidText(journalId)),("@v",voucher),("@pid",SchemaSql.GuidText(draft.PeriodId)),
            ("@pm",Month(draft.PeriodMonth)),("@jd",Day(draft.JournalDate)),("@st",draft.SourceType),
            ("@sid",SchemaSql.GuidText(draft.SourceId)),("@cid",SchemaSql.GuidText(draft.CompanyId)),
            ("@purpose",draft.Purpose),("@status",StatusDraft),("@rm",draft.RunMode),("@pb",draft.PayoutBlocked?1:0),
            ("@rev",(object?)(draft.ReversesJournalId is {} r?SchemaSql.GuidText(r):null)??DBNull.Value),
            ("@at",now),("@by",draft.Actor),("@nar",(object?)draft.NarrationAr??DBNull.Value));

        int lineNo=1;
        foreach(var line in rounded)
        {
            Guid lineId=SchemaSql.NewId();
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"fin","journal_line")}
                (id,period_id,period_month,journal_id,line_no,account_id,is_postable,company_id,cost_centre_id,app_code,erp_platform_account_id,erp_rider_id,debit,credit,narration_ar,created_by,created_at)
                VALUES(@id,@pid,@pm,@jid,@ln,@aid,1,@cid,@cc,@app,@plat,@rider,@d,@cr,@nar,@by,@at)
                """,
                ("@id",SchemaSql.GuidText(lineId)),("@pid",SchemaSql.GuidText(draft.PeriodId)),("@pm",Month(draft.PeriodMonth)),
                ("@jid",SchemaSql.GuidText(journalId)),("@ln",lineNo++),("@aid",SchemaSql.GuidText(line.AccountId)),
                ("@cid",SchemaSql.GuidText(draft.CompanyId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),
                ("@app",line.AppCode),
                ("@plat",(object?)(line.ErpPlatformAccountId is {} p?SchemaSql.GuidText(p):null)??DBNull.Value),
                ("@rider",(object?)(line.ErpRiderId is {} er?SchemaSql.GuidText(er):null)??DBNull.Value),
                ("@d",line.Debit),("@cr",line.Credit),("@nar",(object?)line.NarrationAr??DBNull.Value),
                ("@by",draft.Actor),("@at",now));
            UpsertBalance(c,tx,draft,line,now);
        }
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"fin","journal")}
            SET status=@posted, posted_at=@at, posted_by=@by
            WHERE id=@id AND status=@draft
            """,
            ("@posted",StatusPosted),("@at",now),("@by",draft.Actor),
            ("@id",SchemaSql.GuidText(journalId)),("@draft",StatusDraft));
        return new(journalId,voucher,false);
    }

    public static Result Reverse(DbConnection c,DbTransaction? tx,Guid originalJournalId,string actor,Guid newSourceId,string? narrationAr=null)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT company_id,period_id,period_month,journal_date,source_type,purpose,run_mode,payout_blocked,status,voucher_no
            FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id
            """,("@id",SchemaSql.GuidText(originalJournalId)));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"القيد الأصلي غير موجود.",404);
        int status=Convert.ToInt32(reader["status"]);
        J.Need(status==StatusPosted,"العكس لقيد مرحّل فقط.",409);
        var company=Guid.Parse(reader["company_id"].ToString()!);
        var period=Guid.Parse(reader["period_id"].ToString()!);
        var month=DateOnly.Parse(reader["period_month"].ToString()![..10],CultureInfo.InvariantCulture);
        var date=DateOnly.Parse(reader["journal_date"].ToString()![..10],CultureInfo.InvariantCulture);
        string sourceType=reader["source_type"].ToString()!;
        string purpose=reader["purpose"].ToString()!;
        byte runMode=(byte)Convert.ToInt32(reader["run_mode"]);
        bool blocked=Convert.ToInt32(reader["payout_blocked"])==1;
        reader.Close();

        var lines=new List<Line>();
        using(var lineCmd=Store.Command(c,tx,$"""
            SELECT account_id,cost_centre_id,COALESCE(app_code,'none') AS app_code,debit,credit,narration_ar,erp_platform_account_id,erp_rider_id
            FROM {SchemaSql.Q(c,"fin","journal_line")} WHERE journal_id=@id ORDER BY line_no
            """,("@id",SchemaSql.GuidText(originalJournalId))))
        using(var lr=lineCmd.ExecuteReader())
        while(lr.Read())
        {
            Guid? plat=lr["erp_platform_account_id"] is DBNull?null:Guid.Parse(lr["erp_platform_account_id"].ToString()!);
            Guid? rider=lr["erp_rider_id"] is DBNull?null:Guid.Parse(lr["erp_rider_id"].ToString()!);
            lines.Add(new(
                Guid.Parse(lr["account_id"].ToString()!),
                Guid.Parse(lr["cost_centre_id"].ToString()!),
                lr["app_code"].ToString()!,
                Convert.ToDecimal(lr["credit"]),
                Convert.ToDecimal(lr["debit"]),
                narrationAr??lr["narration_ar"] as string,
                plat,rider));
        }

        var result=Post(c,tx,new Draft(company,period,month,date,sourceType,newSourceId,"reversal",runMode,blocked,actor,lines,originalJournalId,narrationAr));
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=@s WHERE id=@id",
            ("@s",StatusReversed),("@id",SchemaSql.GuidText(originalJournalId)));
        return result;
    }

    static Result? FindExisting(DbConnection c,DbTransaction? tx,Draft draft)
    {
        object? id=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"fin","journal")}
            WHERE source_type=@st AND source_id=@sid AND company_id=@cid AND purpose=@p AND status=@posted
            """,("@st",draft.SourceType),("@sid",SchemaSql.GuidText(draft.SourceId)),
            ("@cid",SchemaSql.GuidText(draft.CompanyId)),("@p",draft.Purpose),("@posted",StatusPosted));
        if(id==null)return null;
        Guid journalId=Guid.Parse(id.ToString()!);
        string voucher=(string)SchemaSql.Scalar(c,tx,$"SELECT voucher_no FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",SchemaSql.GuidText(journalId)))!;
        return new(journalId,voucher,true);
    }

    static bool ExistingMatches(DbConnection c,DbTransaction? tx,Guid journalId,IReadOnlyList<Line> lines)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT account_id,cost_centre_id,COALESCE(app_code,'none'),debit,credit FROM {SchemaSql.Q(c,"fin","journal_line")}
            WHERE journal_id=@id ORDER BY line_no
            """,("@id",SchemaSql.GuidText(journalId)));
        using var reader=cmd.ExecuteReader();
        var existing=new List<(Guid,Guid,string,decimal,decimal)>();
        while(reader.Read())
            existing.Add((Guid.Parse(reader.GetValue(0).ToString()!),Guid.Parse(reader.GetValue(1).ToString()!),
                reader.GetString(2),Convert.ToDecimal(reader.GetValue(3)),Convert.ToDecimal(reader.GetValue(4))));
        if(existing.Count!=lines.Count)return false;
        for(int i=0;i<lines.Count;i++)
        {
            var e=existing[i];var l=lines[i];
            if(e.Item1!=l.AccountId||e.Item2!=l.CostCentreId||!string.Equals(e.Item3,l.AppCode,StringComparison.Ordinal)
               ||e.Item4!=l.Debit||e.Item5!=l.Credit)return false;
        }
        return true;
    }

    static void UpsertBalance(DbConnection c,DbTransaction? tx,Draft draft,Line line,string now)
    {
        byte[] key=SchemaFinPeriodBalance.ComputeDimensionKey(draft.CompanyId,line.AccountId,line.CostCentreId,line.AppCode);
        object? existing=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"fin","period_balance")}
            WHERE period_id=@p AND company_id=@c AND account_id=@a AND cost_centre_id=@cc AND app_code=@app
            """,("@p",SchemaSql.GuidText(draft.PeriodId)),("@c",SchemaSql.GuidText(draft.CompanyId)),
            ("@a",SchemaSql.GuidText(line.AccountId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),("@app",line.AppCode));
        int nature=Convert.ToInt32(SchemaSql.Scalar(c,tx,$"SELECT nature FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",SchemaSql.GuidText(line.AccountId)))!);
        if(existing==null)
        {
            decimal opening=0,debit=line.Debit,credit=line.Credit;
            decimal closing=nature==1?opening+debit-credit:opening+credit-debit;
            if(SchemaSql.IsSql(c))
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                    (id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,opening,debit,credit,closing,created_by,created_at)
                    VALUES(@id,@p,@pm,@c,@a,@cc,@app,@o,@d,@cr,@cl,@by,@at)
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(draft.PeriodId)),
                    ("@pm",Month(draft.PeriodMonth)),("@c",SchemaSql.GuidText(draft.CompanyId)),
                    ("@a",SchemaSql.GuidText(line.AccountId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),
                    ("@app",line.AppCode),("@o",opening),("@d",debit),("@cr",credit),("@cl",J.Money(closing)),
                    ("@by",draft.Actor),("@at",now));
            else
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                    (id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,dimension_key,opening,debit,credit,closing,created_by,created_at)
                    VALUES(@id,@p,@pm,@c,@a,@cc,@app,@dk,@o,@d,@cr,@cl,@by,@at)
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(draft.PeriodId)),
                    ("@pm",Month(draft.PeriodMonth)),("@c",SchemaSql.GuidText(draft.CompanyId)),
                    ("@a",SchemaSql.GuidText(line.AccountId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),
                    ("@app",line.AppCode),("@dk",key),("@o",opening),("@d",debit),("@cr",credit),("@cl",J.Money(closing)),
                    ("@by",draft.Actor),("@at",now));
            return;
        }
        string balId=existing.ToString()!;
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"fin","period_balance")}
            SET debit=debit+@d, credit=credit+@cr,
                closing=CASE WHEN @nature=1 THEN opening+debit+@d-(credit+@cr) ELSE opening+credit+@cr-(debit+@d) END,
                updated_by=@by, updated_at=@at
            WHERE id=@id
            """,("@d",line.Debit),("@cr",line.Credit),("@nature",nature),("@by",draft.Actor),("@at",now),("@id",balId));
    }

    static string Month(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    static string Day(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
