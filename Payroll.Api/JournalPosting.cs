using System.Data.Common;
using System.Globalization;
namespace Payroll;

/// <summary>U-W2-22a: one-TX posting into fin.journal / fin.journal_line / fin.period_balance.
/// Leaf accounts only; open period locked; ΣD=ΣC; MidpointRounding.AwayFromZero to 2 dp; reverse-only correction;
/// idempotent fingerprint includes period/date/purpose/source/reversal/rider/platform. Design §7.1 / BR §17 / §24 / §28.</summary>
public static class JournalPosting
{
    public const byte StatusDraft=1,StatusPosted=2,StatusReversed=3,StatusCancelled=4;
    public sealed record Line(Guid AccountId,Guid CostCentreId,string AppCode,decimal Debit,decimal Credit,string? NarrationAr,Guid? ErpPlatformAccountId=null,Guid? ErpRiderId=null,Guid? SupplierId=null);
    public sealed record Draft(
        Guid CompanyId,Guid PeriodId,DateOnly PeriodMonth,DateOnly JournalDate,
        string SourceType,Guid SourceId,string Purpose,byte RunMode,bool PayoutBlocked,
        string Actor,IReadOnlyList<Line> Lines,Guid? ReversesJournalId=null,string? NarrationAr=null,string? VoucherNo=null,
        string? GuardPurpose=null);

    public sealed record Result(Guid JournalId,string VoucherNo,bool AlreadyPosted);

    public static Result Post(DbConnection c,DbTransaction? tx,Draft draft)
    {
        if(tx!=null)return PostCore(c,tx,draft);
        // WC1H: SQLite IMMEDIATE (deferred:false) so a period cannot close mid-post on another connection.
        using var own=BeginPostingTx(c);
        try
        {
            var result=PostCore(c,own,draft);
            own.Commit();
            return result;
        }
        catch
        {
            try{own.Rollback();}catch{/* already failed */}
            throw;
        }
    }

    public static Result Reverse(DbConnection c,DbTransaction? tx,Guid originalJournalId,string actor,Guid newSourceId,string? narrationAr=null,Guid? destinationPeriodId=null,DateOnly? destinationDate=null)
    {
        if(tx!=null)return ReverseCore(c,tx,originalJournalId,actor,newSourceId,narrationAr,destinationPeriodId,destinationDate);
        using var own=BeginPostingTx(c);
        try
        {
            var result=ReverseCore(c,own,originalJournalId,actor,newSourceId,narrationAr,destinationPeriodId,destinationDate);
            own.Commit();
            return result;
        }
        catch
        {
            try{own.Rollback();}catch{/* already failed */}
            throw;
        }
    }

    static DbTransaction BeginPostingTx(DbConnection c)=>
        c is Microsoft.Data.Sqlite.SqliteConnection sqlite?sqlite.BeginTransaction(deferred:false):c.BeginTransaction();

    static Result PostCore(DbConnection c,DbTransaction tx,Draft draft)
    {
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
        string purpose=draft.GuardPurpose??draft.Purpose;
        foreach(var line in rounded)DemandRiderAttribution(purpose,line);
        var creditedCodes=rounded.Where(l=>l.Credit>0).Select(l=>AccountCode(c,tx,l.AccountId));
        MoneyWriteGuards.Demand(draft.RunMode,draft.PayoutBlocked,MoneyWriteGuards.EffectForJournal(purpose,creditedCodes,draft.PayoutBlocked));

        if(FindExisting(c,tx,draft) is {} existing)
        {
            J.Need(ExistingMatches(c,tx,existing.JournalId,draft,rounded),"قيد بنفس المصدر موجود بمحتوى مختلف.",409);
            return existing with { AlreadyPosted=true };
        }

        LockOpenPeriod(c,tx,draft.PeriodId,draft.CompanyId,draft.PeriodMonth);
        foreach(var line in rounded)ValidateDimensions(c,tx,draft.CompanyId,line);

        Guid journalId=SchemaSql.NewId();
        string voucher=draft.VoucherNo??($"J-{draft.PeriodMonth:yyyyMM}-{journalId.ToString("N")[^8..].ToUpperInvariant()}");
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        try
        {
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
                    (id,period_id,period_month,journal_id,line_no,account_id,is_postable,company_id,cost_centre_id,app_code,erp_platform_account_id,erp_rider_id,supplier_id,debit,credit,narration_ar,created_by,created_at)
                    VALUES(@id,@pid,@pm,@jid,@ln,@aid,1,@cid,@cc,@app,@plat,@rider,@supplier,@d,@cr,@nar,@by,@at)
                    """,
                    ("@id",SchemaSql.GuidText(lineId)),("@pid",SchemaSql.GuidText(draft.PeriodId)),("@pm",Month(draft.PeriodMonth)),
                    ("@jid",SchemaSql.GuidText(journalId)),("@ln",lineNo++),("@aid",SchemaSql.GuidText(line.AccountId)),
                    ("@cid",SchemaSql.GuidText(draft.CompanyId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),
                    ("@app",line.AppCode),
                    ("@plat",(object?)(line.ErpPlatformAccountId is {} p?SchemaSql.GuidText(p):null)??DBNull.Value),
                    ("@rider",(object?)(line.ErpRiderId is {} er?SchemaSql.GuidText(er):null)??DBNull.Value),
                    ("@supplier",line.SupplierId is {} supplier?SchemaSql.GuidText(supplier):DBNull.Value),("@d",line.Debit),("@cr",line.Credit),("@nar",(object?)line.NarrationAr??DBNull.Value),
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
        catch(DbException)
        {
            if(FindExisting(c,tx,draft) is {} raced)
            {
                J.Need(ExistingMatches(c,tx,raced.JournalId,draft,rounded),"قيد بنفس المصدر موجود بمحتوى مختلف.",409);
                return raced with { AlreadyPosted=true };
            }
            throw new ApiError("تعذر ترحيل القيد؛ التصحيح الوحيد هو قيد عكسي.",409);
        }
    }

    static Result ReverseCore(DbConnection c,DbTransaction tx,Guid originalJournalId,string actor,Guid newSourceId,string? narrationAr,Guid? destinationPeriodId,DateOnly? destinationDate)
    {
        using var cmd=Store.Command(c,tx,$"""
            SELECT company_id,period_id,period_month,journal_date,source_type,purpose,run_mode,payout_blocked,status,voucher_no
            FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id
            """,("@id",SchemaSql.GuidText(originalJournalId)));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"القيد الأصلي غير موجود.",404);
        int status=Convert.ToInt32(reader["status"]);
        var company=ParseGuid(reader["company_id"]);
        var originalPeriod=ParseGuid(reader["period_id"]);
        var originalMonth=ReadDate(reader["period_month"]);
        var originalDate=ReadDate(reader["journal_date"]);
        string sourceType=reader["source_type"].ToString()!;
        string purpose=reader["purpose"].ToString()!;
        byte runMode=(byte)Convert.ToInt32(reader["run_mode"]);
        bool blocked=Convert.ToInt32(reader["payout_blocked"])==1;
        reader.Close();

        var existing=FindExisting(c,tx,new Draft(company,destinationPeriodId??originalPeriod,originalMonth,originalDate,sourceType,newSourceId,"reversal",runMode,blocked,actor,[],originalJournalId))
            ??FindReversalByLink(c,tx,originalJournalId,newSourceId);
        if(existing!=null)
        {
            J.Need(status==StatusReversed||status==StatusPosted,"حالة القيد الأصلي لا تسمح بإعادة العكس.",409);
            return existing with { AlreadyPosted=true };
        }

        J.Need(status==StatusPosted,"العكس لقيد مرحّل فقط.",409);

        Guid periodId=destinationPeriodId??originalPeriod;
        DateOnly month=originalMonth,date=destinationDate??originalDate;
        if(destinationPeriodId!=null||destinationDate!=null)
        {
            object? periodMonth=SchemaSql.Scalar(c,tx,$"SELECT period_month FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE id=@id AND company_id=@c",
                ("@id",SchemaSql.GuidText(periodId)),("@c",SchemaSql.GuidText(company)));
            J.Need(periodMonth!=null,"الفترة المحاسبية للعكس غير موجودة.",404);
            month=ReadDate(periodMonth!);
            date=destinationDate??month;
            J.Need(date.Year==month.Year&&date.Month==month.Month,"تاريخ العكس خارج شهر الفترة المفتوحة.",400);
        }

        var lines=new List<Line>();
        using(var lineCmd=Store.Command(c,tx,$"""
            SELECT account_id,cost_centre_id,COALESCE(app_code,'none') AS app_code,debit,credit,narration_ar,erp_platform_account_id,erp_rider_id,supplier_id
            FROM {SchemaSql.Q(c,"fin","journal_line")} WHERE journal_id=@id ORDER BY line_no
            """,("@id",SchemaSql.GuidText(originalJournalId))))
        using(var lr=lineCmd.ExecuteReader())
        while(lr.Read())
        {
            Guid? plat=lr["erp_platform_account_id"] is DBNull?null:ParseGuid(lr["erp_platform_account_id"]);
            Guid? rider=lr["erp_rider_id"] is DBNull?null:ParseGuid(lr["erp_rider_id"]);
            lines.Add(new(
                ParseGuid(lr["account_id"]),
                ParseGuid(lr["cost_centre_id"]),
                lr["app_code"].ToString()!,
                Convert.ToDecimal(lr["credit"]),
                Convert.ToDecimal(lr["debit"]),
                narrationAr??lr["narration_ar"] as string,
                plat,rider,lr["supplier_id"] is DBNull?null:ParseGuid(lr["supplier_id"])));
        }

        // Keep the original payout block and purpose effect for the reversal guard (Grok).
        var result=PostCore(c,tx,new Draft(company,periodId,month,date,sourceType,newSourceId,"reversal",runMode,blocked,actor,lines,originalJournalId,narrationAr,GuardPurpose:purpose));
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","journal")} SET status=@s WHERE id=@id",
            ("@s",StatusReversed),("@id",SchemaSql.GuidText(originalJournalId)));
        return result;
    }

    static void LockOpenPeriod(DbConnection c,DbTransaction tx,Guid periodId,Guid companyId,DateOnly month)
    {
        string q=SchemaSql.Q(c,"fin","accounting_period");
        string sql=SchemaSql.IsSql(c)
            ?$"SELECT state FROM {q} WITH (UPDLOCK, HOLDLOCK) WHERE id=@id AND company_id=@c AND period_month=@m"
            :$"SELECT state FROM {q} WHERE id=@id AND company_id=@c AND period_month=@m";
        object? periodState=SchemaSql.Scalar(c,tx,sql,("@id",SchemaSql.GuidText(periodId)),("@c",SchemaSql.GuidText(companyId)),("@m",Month(month)));
        J.Need(periodState!=null&&Convert.ToInt32(periodState)==1,"الفترة المحاسبية مغلقة أو غير موجودة.",409);
    }

    static void ValidateDimensions(DbConnection c,DbTransaction tx,Guid companyId,Line line)
    {
        object? leaf=SchemaSql.Scalar(c,tx,$"SELECT is_postable FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id AND is_active=1",("@id",SchemaSql.GuidText(line.AccountId)));
        J.Need(leaf!=null&&Convert.ToInt32(leaf)==1,"الترحيل للحسابات الورقية النشطة فقط.",409);
        object? scope=SchemaSql.Scalar(c,tx,$"SELECT company_scope FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",SchemaSql.GuidText(line.AccountId)));
        J.Need(scope!=null&&Convert.ToInt32(scope) is 0 or 1 or 2,"نطاق شركة الحساب غير صالح.",409);

        if(line.SupplierId is {} supplier)
            J.Need(SchemaSql.TableExists(c,tx,"pay","supplier")&&SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"pay","supplier")} WHERE id=@id AND company_id=@c AND status=1",("@id",SchemaSql.GuidText(supplier)),("@c",SchemaSql.GuidText(companyId)))!=null,"المورد خارج نطاق الشركة أو غير نشط.",409);
        if(line.CostCentreId==SchemaOrgCostCentre.NoneId)return;
        using var cmd=Store.Command(c,tx,$"""
            SELECT company_id,status FROM {SchemaSql.Q(c,"org","cost_centre")} WHERE id=@id
            """,("@id",SchemaSql.GuidText(line.CostCentreId)));
        using var reader=cmd.ExecuteReader();
        J.Need(reader.Read(),"مركز التكلفة غير موجود.",409);
        J.Need(Convert.ToInt32(reader["status"])==1,"مركز التكلفة غير نشط.",409);
        if(reader["company_id"] is DBNull)return;
        J.Need(ParseGuid(reader["company_id"])==companyId,"مركز التكلفة خارج نطاق شركة القيد.",409);
    }

    static readonly HashSet<string> RiderAttributedPurposes=new(StringComparer.OrdinalIgnoreCase)
    {
        "salary","agent","agent_commission"
    };

    static void DemandRiderAttribution(string? purpose,Line line)
    {
        bool hasRider=line.ErpRiderId!=null,hasPlat=line.ErpPlatformAccountId!=null;
        if(!string.IsNullOrWhiteSpace(purpose)&&RiderAttributedPurposes.Contains(purpose))
            J.Need(hasRider&&hasPlat,"سطر الراتب أو الوكيل يتطلب معرف المندوب ومعرف حساب المنصة معاً.",409);
        else if(hasRider||hasPlat)
            J.Need(hasRider&&hasPlat,"سطر المندوب يتطلب معرف المندوب ومعرف حساب المنصة معاً.",409);
    }

    static string? AccountCode(DbConnection c,DbTransaction tx,Guid accountId)
    {
        object? code=SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",SchemaSql.GuidText(accountId)));
        return code as string??code?.ToString();
    }

    static Result? FindExisting(DbConnection c,DbTransaction tx,Draft draft)
    {
        object? id=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"fin","journal")}
            WHERE source_type=@st AND source_id=@sid AND company_id=@cid AND purpose=@p AND status=@posted
            """,("@st",draft.SourceType),("@sid",SchemaSql.GuidText(draft.SourceId)),
            ("@cid",SchemaSql.GuidText(draft.CompanyId)),("@p",draft.Purpose),("@posted",StatusPosted));
        if(id==null)return null;
        Guid journalId=ParseGuid(id);
        string voucher=(string)SchemaSql.Scalar(c,tx,$"SELECT voucher_no FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",SchemaSql.GuidText(journalId)))!;
        return new(journalId,voucher,true);
    }

    static Result? FindReversalByLink(DbConnection c,DbTransaction tx,Guid originalJournalId,Guid sourceId)
    {
        object? id=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"fin","journal")}
            WHERE reverses_journal_id=@rev AND source_id=@sid AND purpose=@p AND status=@posted
            """,("@rev",SchemaSql.GuidText(originalJournalId)),("@sid",SchemaSql.GuidText(sourceId)),
            ("@p","reversal"),("@posted",StatusPosted));
        if(id==null)return null;
        Guid journalId=ParseGuid(id);
        string voucher=(string)SchemaSql.Scalar(c,tx,$"SELECT voucher_no FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id",("@id",SchemaSql.GuidText(journalId)))!;
        return new(journalId,voucher,true);
    }

    static bool ExistingMatches(DbConnection c,DbTransaction tx,Guid journalId,Draft draft,IReadOnlyList<Line> lines)
    {
        using var header=Store.Command(c,tx,$"""
            SELECT period_id,period_month,journal_date,purpose,source_type,source_id,reverses_journal_id,run_mode,payout_blocked
            FROM {SchemaSql.Q(c,"fin","journal")} WHERE id=@id
            """,("@id",SchemaSql.GuidText(journalId)));
        using var hr=header.ExecuteReader();
        if(!hr.Read())return false;
        if(ParseGuid(hr["period_id"])!=draft.PeriodId)return false;
        if(ReadDate(hr["period_month"])!=draft.PeriodMonth)return false;
        if(ReadDate(hr["journal_date"])!=draft.JournalDate)return false;
        if(!string.Equals(hr["purpose"].ToString(),draft.Purpose,StringComparison.Ordinal))return false;
        if(!string.Equals(hr["source_type"].ToString(),draft.SourceType,StringComparison.Ordinal))return false;
        if(ParseGuid(hr["source_id"])!=draft.SourceId)return false;
        Guid? rev=hr["reverses_journal_id"] is DBNull?null:ParseGuid(hr["reverses_journal_id"]);
        if(rev!=draft.ReversesJournalId)return false;
        if(Convert.ToInt32(hr["run_mode"])!=draft.RunMode)return false;
        if((Convert.ToInt32(hr["payout_blocked"])==1)!=draft.PayoutBlocked)return false;
        hr.Close();

        using var cmd=Store.Command(c,tx,$"""
            SELECT account_id,cost_centre_id,COALESCE(app_code,'none'),debit,credit,erp_platform_account_id,erp_rider_id,supplier_id
            FROM {SchemaSql.Q(c,"fin","journal_line")} WHERE journal_id=@id ORDER BY line_no
            """,("@id",SchemaSql.GuidText(journalId)));
        using var reader=cmd.ExecuteReader();
        var existing=new List<Line>();
        while(reader.Read())
            existing.Add(new(
                ParseGuid(reader.GetValue(0)),ParseGuid(reader.GetValue(1)),reader.GetString(2),
                Convert.ToDecimal(reader.GetValue(3)),Convert.ToDecimal(reader.GetValue(4)),null,
                reader.IsDBNull(5)?null:ParseGuid(reader.GetValue(5)),
                reader.IsDBNull(6)?null:ParseGuid(reader.GetValue(6)),reader.IsDBNull(7)?null:ParseGuid(reader.GetValue(7))));
        if(existing.Count!=lines.Count)return false;
        for(int i=0;i<lines.Count;i++)
        {
            var e=existing[i];var l=lines[i];
            if(e.AccountId!=l.AccountId||e.CostCentreId!=l.CostCentreId||!string.Equals(e.AppCode,l.AppCode,StringComparison.Ordinal)
               ||e.Debit!=l.Debit||e.Credit!=l.Credit||e.ErpPlatformAccountId!=l.ErpPlatformAccountId||e.ErpRiderId!=l.ErpRiderId||e.SupplierId!=l.SupplierId)
                return false;
        }
        return true;
    }

    static void UpsertBalance(DbConnection c,DbTransaction tx,Draft draft,Line line,string now)
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
            decimal closing=J.Money(nature==1?opening+debit-credit:opening+credit-debit);
            if(SchemaSql.IsSql(c))
                SchemaSql.Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","period_balance")}
                    (id,period_id,period_month,company_id,account_id,cost_centre_id,app_code,opening,debit,credit,closing,created_by,created_at)
                    VALUES(@id,@p,@pm,@c,@a,@cc,@app,@o,@d,@cr,@cl,@by,@at)
                    """,
                    ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",SchemaSql.GuidText(draft.PeriodId)),
                    ("@pm",Month(draft.PeriodMonth)),("@c",SchemaSql.GuidText(draft.CompanyId)),
                    ("@a",SchemaSql.GuidText(line.AccountId)),("@cc",SchemaSql.GuidText(line.CostCentreId)),
                    ("@app",line.AppCode),("@o",opening),("@d",debit),("@cr",credit),("@cl",closing),
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
                    ("@app",line.AppCode),("@dk",key),("@o",opening),("@d",debit),("@cr",credit),("@cl",closing),
                    ("@by",draft.Actor),("@at",now));
            return;
        }
        string balId=existing.ToString()!;
        if(SchemaSql.IsSql(c))
        {
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"fin","period_balance")}
                SET debit=debit+@d, credit=credit+@cr,
                    closing=CASE WHEN @nature=1 THEN opening+debit+@d-(credit+@cr) ELSE opening+credit+@cr-(debit+@d) END,
                    updated_by=@by, updated_at=@at
                WHERE id=@id
                """,("@d",line.Debit),("@cr",line.Credit),("@nature",nature),("@by",draft.Actor),("@at",now),("@id",balId));
            return;
        }
        using var bal=Store.Command(c,tx,$"SELECT opening,debit,credit FROM {SchemaSql.Q(c,"fin","period_balance")} WHERE id=@id",("@id",balId));
        using var br=bal.ExecuteReader();
        br.Read();
        decimal openingBal=Convert.ToDecimal(br["opening"]);
        decimal newDebit=J.Money(Convert.ToDecimal(br["debit"])+line.Debit);
        decimal newCredit=J.Money(Convert.ToDecimal(br["credit"])+line.Credit);
        br.Close();
        decimal newClosing=J.Money(nature==1?openingBal+newDebit-newCredit:openingBal+newCredit-newDebit);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"fin","period_balance")}
            SET debit=@d, credit=@cr, closing=@cl, updated_by=@by, updated_at=@at
            WHERE id=@id
            """,("@d",newDebit),("@cr",newCredit),("@cl",newClosing),("@by",draft.Actor),("@at",now),("@id",balId));
    }

    static DateOnly ReadDate(object value)=>value switch
    {
        DateOnly d=>d,
        DateTime dt=>DateOnly.FromDateTime(dt),
        string s=>DateOnly.ParseExact(s.Length>=10?s[..10]:s,"yyyy-MM-dd",CultureInfo.InvariantCulture),
        _=>DateOnly.FromDateTime(Convert.ToDateTime(value,CultureInfo.InvariantCulture))
    };
    static Guid ParseGuid(object value)=>value switch
    {
        Guid g=>g,
        _=>Guid.Parse(value.ToString()!)
    };
    static string Month(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    static string Day(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
