using System.Data.Common;
namespace Payroll;

/// <summary>U-W2-31 / N1 / BR §38.5: one shared gate for every money write (pay, sal, fin posting, bank mark-sent).
/// run_mode=1 (simulation) → no real effects. payout_blocked=1 → accounting/debt OK; bank-sent and payout rejected.</summary>
public enum MoneyEffectKind
{
    Accounting,
    Debt,
    BankSent,
    Payout
}

public static class MoneyWriteGuards
{
    static readonly AsyncLocal<int> SimulationDepth=new();

    /// <summary>Marks the call stack as a sal simulation path. Legacy PostJournal must refuse while this is active.</summary>
    public static IDisposable BeginSimulationContext()
    {
        SimulationDepth.Value=SimulationDepth.Value+1;
        return new SimulationScope();
    }

    public static bool InSimulationContext=>SimulationDepth.Value>0;

    sealed class SimulationScope:IDisposable
    {
        public void Dispose()
        {
            if(SimulationDepth.Value>0)SimulationDepth.Value=SimulationDepth.Value-1;
        }
    }

    public static void Demand(byte runMode,bool payoutBlocked,MoneyEffectKind effect)
    {
        if(runMode==1)
            throw new ApiError("وضع المحاكاة يمنع أي أثر حقيقي (قيود أو مديونية أو صرف أو إرسال بنك).",409);
        if(payoutBlocked&&effect is MoneyEffectKind.BankSent or MoneyEffectKind.Payout)
            throw new ApiError("التشغيل الموازي يمنع إرسال ملف البنك والصرف؛ القيود والمديونية مسموحة.",409);
    }

    public static void Demand(int runMode,bool payoutBlocked,MoneyEffectKind effect)
        =>Demand((byte)runMode,payoutBlocked,effect);

    /// <summary>WC1H: while payout is blocked, only these purposes may credit bank/cash (accruals and their reversals).</summary>
    public static readonly HashSet<string> AccountingOnlyPurposes=new(StringComparer.Ordinal)
    {
        "payroll_accrual","debt_accrual","accrual","accrual_reversal","reversal_of_accrual"
    };

    static readonly HashSet<string> BankCashTypes=new(StringComparer.Ordinal){"bank","cash"};
    static readonly HashSet<string> LegacyBankCashVaultKinds=new(StringComparer.Ordinal){"Bank","MainInternal"};

    /// <summary>WC1I: one shared classifier for target-chart bank/cash codes and legacy (0.31.0) vault codes.</summary>
    static readonly Lazy<HashSet<string>> BankOrCashCodes=new(()=>
    {
        var set=new HashSet<string>(StringComparer.Ordinal);
        foreach(var a in FinanceViews.Accounts)
            if(BankCashTypes.Contains(a.S("type")))set.Add(a.S("code"));
        foreach(var a in FinancialCore.ChartOfAccounts)
            if(a.IsVault&&LegacyBankCashVaultKinds.Contains(a.VaultKind))set.Add(a.Code);
        return set;
    });

    public static bool IsBankOrCashAccountCode(string? code)=>
        !string.IsNullOrWhiteSpace(code)&&BankOrCashCodes.Value.Contains(code);

    public static bool IsAccountingOnlyPurpose(string? purpose)=>
        !string.IsNullOrWhiteSpace(purpose)&&AccountingOnlyPurposes.Contains(purpose);

    public static MoneyEffectKind ForJournalPurpose(string purpose)=>purpose switch
    {
        "bank_claim" or "direct_rider_payout" or "bank_payroll_payout" or "bank_inter_company"=>MoneyEffectKind.Payout,
        "debt_confirm" or "debt_absorb" or "debt_opening"=>MoneyEffectKind.Debt,
        _=>MoneyEffectKind.Accounting
    };

    /// <summary>Payout when a bank/cash account is credited under a blocked run, unless purpose is accounting-only whitelist.</summary>
    public static MoneyEffectKind EffectForJournal(string purpose,IEnumerable<string?> creditedAccountCodes,bool payoutBlocked=false)
    {
        if(payoutBlocked&&!IsAccountingOnlyPurpose(purpose)&&creditedAccountCodes.Any(IsBankOrCashAccountCode))
            return MoneyEffectKind.Payout;
        return ForJournalPurpose(purpose);
    }

    /// <summary>WC1H / planner 2026-11-21: legacy document-store journals read stored run_mode (not a constant 0).
    /// Reject simulation ambient context and sal simulation source ids; read payout_blocked from stored
    /// pay run / bank file when the column exists, else false.</summary>
    public static void DemandLegacyPostJournal(DbConnection c,DbTransaction tx,string transactionType,string reference,string period,string? companyId,System.Text.Json.Nodes.JsonArray? lines=null)
    {
        if(InSimulationContext)
            throw new ApiError("وضع المحاكاة يمنع ترحيل القيود عبر المسار القديم.",409);
        if(IsSalSimulationSource(c,tx,reference))
            throw new ApiError("مرجع المحاكاة يمنع ترحيل قيد حقيقي عبر المسار القديم.",409);
        // WC1I: run_mode comes only from the actual source row (cycle / run / sal.pay_run id) — never company+month aggregation.
        byte runMode=ResolveLegacyRunMode(c,tx,reference);
        bool blocked=ResolveLegacyPayoutBlocked(c,tx,period,companyId);
        var credited=(lines??[]).OfType<System.Text.Json.Nodes.JsonObject>()
            .Where(l=>l.N("credit")>0)
            .Select(l=>l.S("accountCode"));
        Demand(runMode,blocked,EffectForJournal(transactionType,credited,blocked));
    }

    /// <summary>Known posting prefixes that point at a run_mode source row. Unresolvable prefixed refs are rejected (never default to live).</summary>
    public static readonly string[] RunModeSourcePrefixes=["PAY-CYCLE:","PAYROLL:","PAY-RUN:","SAL-PAY-RUN:"];
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> AmbientRunModes=new(StringComparer.Ordinal);

    /// <summary>Register an in-flight source row's run_mode (e.g. payroll approve before the run document is saved).</summary>
    public static void NoteSourceRunMode(string sourceId,byte runMode)
    {
        if(!string.IsNullOrWhiteSpace(sourceId))AmbientRunModes[sourceId]=runMode;
    }
    public static void ClearSourceRunMode(string sourceId)
    {
        if(!string.IsNullOrWhiteSpace(sourceId))AmbientRunModes.TryRemove(sourceId,out _);
    }

    /// <summary>WMRG / RWC1d: parse posting prefixes (PAY-CYCLE:… etc.) and read run_mode from the actual source row.</summary>
    public static byte ResolveLegacyRunMode(DbConnection c,DbTransaction tx,string? reference)
    {
        if(string.IsNullOrWhiteSpace(reference))return 0;
        var (prefixed,rest)=StripRunModePrefix(reference);
        foreach(string sourceId in SourceIdCandidates(rest))
        {
            if(AmbientRunModes.TryGetValue(sourceId,out var ambient))return ambient;
            if(TryReadDocumentRunMode(c,tx,sourceId,out var mode))return mode;
            if(TryReadSalPayRunMode(c,tx,sourceId,out mode))return mode;
        }
        if(prefixed)
            throw new ApiError("مرجع الترحيل غير قابل للحل؛ لا يُفترض وضع التشغيل الحي لمرجع غير معروف.",409);
        return 0;
    }

    public static (bool Prefixed,string SourceId) StripRunModePrefix(string reference)
    {
        foreach(string prefix in RunModeSourcePrefixes)
            if(reference.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
                return (true,reference[prefix.Length..]);
        return (false,reference);
    }

    /// <summary>Cycle ids are pay-cycle:…; PayPosting then appends :salary:… / :debtRef / _reversal. Try longest id first.</summary>
    public static IEnumerable<string> SourceIdCandidates(string rest)
    {
        if(string.IsNullOrWhiteSpace(rest))yield break;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var list=new List<string>();
        void Add(string value){if(value!=""&&seen.Add(value))list.Add(value);}
        Add(rest);
        int under=rest.IndexOf('_');
        if(under>0)Add(rest[..under]);
        for(int i=rest.Length-1;i>=0;i--)
            if(rest[i]==':')Add(rest[..i]);
        foreach(var id in list)yield return id;
    }

    static bool TryReadDocumentRunMode(DbConnection c,DbTransaction tx,string id,out byte mode)
    {
        mode=0;
        var cycle=Store.Get(c,id,PayCycles.Kind,tx);
        if(cycle!=null){mode=cycle.N("runMode")==1||cycle.S("runMode")=="1"?(byte)1:(byte)0;return true;}
        var run=Store.Get(c,id,"run",tx);
        if(run!=null){mode=run.N("runMode")==1||run.S("runMode")=="1"?(byte)1:(byte)0;return true;}
        return false;
    }

    static bool TryReadSalPayRunMode(DbConnection c,DbTransaction tx,string id,out byte mode)
    {
        mode=0;
        // Bind D-format text: N-format document ids parse as Guid but SQL uniqueidentifier rejects raw N strings (8169).
        if(!Guid.TryParse(id,out var gid)||!SchemaSql.TableExists(c,tx,"sal","pay_run"))return false;
        object? value=SchemaSql.Scalar(c,tx,$"""
            SELECT run_mode FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",SchemaSql.GuidText(gid)));
        if(value is null or DBNull)return false;
        mode=Convert.ToInt32(value)==1?(byte)1:(byte)0;
        return true;
    }

    public static bool IsSalSimulationSource(DbConnection c,DbTransaction tx,string reference)
    {
        if(string.IsNullOrWhiteSpace(reference)||!SchemaSql.TableExists(c,tx,"sal","pay_run"))return false;
        var (_,rest)=StripRunModePrefix(reference);
        foreach(string sourceId in SourceIdCandidates(rest))
        {
            if(!Guid.TryParse(sourceId,out var gid))continue;
            object? mode=SchemaSql.Scalar(c,tx,$"""
                SELECT run_mode FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
                """,("@id",SchemaSql.GuidText(gid)));
            if(mode is not null and not DBNull)return Convert.ToInt32(mode)==1;
        }
        return false;
    }

    public static bool ResolveLegacyPayoutBlocked(DbConnection c,DbTransaction tx,string period,string? companyId)
    {
        if(string.IsNullOrWhiteSpace(companyId))return false;
        string month=J.Period(period);
        foreach(var cycle in Store.List(c,PayCycles.Kind,tx).Where(d=>d.S("companyId")==companyId&&d.S("period")==month
            &&d.S("status") is not ("cancelled" or "reversed")))
            if(cycle.B("payoutBlocked")||cycle.S("payoutBlocked")=="1")return true;
        foreach(var run in Store.List(c,"run",tx).Where(r=>r.S("sponsorId")==companyId&&r.S("period")==month))
            if(run.B("payoutBlocked")||run.S("payoutBlocked")=="1")return true;
        // sal.* company_id is uniqueidentifier; document-store sponsor codes (BW/…) must not be bound there.
        if(!Guid.TryParse(companyId,out _))return false;
        if(SchemaSql.TableExists(c,tx,"sal","pay_run"))
        {
            object? pb=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(CAST(payout_blocked AS int)) FROM {SchemaSql.Q(c,"sal","pay_run")}
                WHERE company_id=@c AND period_month=@pm AND status<>4 AND run_mode=0
                """,("@c",companyId),("@pm",month+"-01"));
            if(pb is not null and not DBNull&&Convert.ToInt32(pb)==1)return true;
        }
        if(SchemaSql.TableExists(c,tx,"sal","bank_file"))
        {
            object? fileBlocked=SchemaSql.Scalar(c,tx,$"""
                SELECT MAX(CAST(payout_blocked AS int)) FROM {SchemaSql.Q(c,"sal","bank_file")}
                WHERE paying_company_id=@c AND period_month=@pm AND status<>4
                """,("@c",companyId),("@pm",month+"-01"));
            if(fileBlocked is not null and not DBNull&&Convert.ToInt32(fileBlocked)==1)return true;
        }
        return false;
    }
}
