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

    public static MoneyEffectKind ForJournalPurpose(string purpose)=>purpose switch
    {
        "bank_claim" or "direct_rider_payout" or "bank_payroll_payout"=>MoneyEffectKind.Payout,
        "debt_confirm" or "debt_absorb" or "debt_opening"=>MoneyEffectKind.Debt,
        _=>MoneyEffectKind.Accounting
    };

    /// <summary>WC1G / planner 2026-11-21: legacy document-store journals are always REAL (run_mode=0).
    /// Reject simulation ambient context and sal simulation source ids; read payout_blocked from stored
    /// pay run / bank file when the column exists, else false.</summary>
    public static void DemandLegacyPostJournal(DbConnection c,DbTransaction tx,string transactionType,string reference,string period,string? companyId)
    {
        if(InSimulationContext)
            throw new ApiError("وضع المحاكاة يمنع ترحيل القيود عبر المسار القديم.",409);
        if(IsSalSimulationSource(c,tx,reference))
            throw new ApiError("مرجع المحاكاة يمنع ترحيل قيد حقيقي عبر المسار القديم.",409);
        bool blocked=ResolveLegacyPayoutBlocked(c,tx,period,companyId);
        Demand(0,blocked,ForJournalPurpose(transactionType));
    }

    public static bool IsSalSimulationSource(DbConnection c,DbTransaction tx,string reference)
    {
        if(string.IsNullOrWhiteSpace(reference)||!SchemaSql.TableExists(c,tx,"sal","pay_run"))return false;
        object? mode=SchemaSql.Scalar(c,tx,$"""
            SELECT run_mode FROM {SchemaSql.Q(c,"sal","pay_run")} WHERE id=@id
            """,("@id",reference));
        return mode is not null and not DBNull&&Convert.ToInt32(mode)==1;
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
