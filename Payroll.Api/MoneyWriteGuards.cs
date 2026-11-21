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
        "bank_claim" or "direct_rider_payout"=>MoneyEffectKind.Payout,
        "debt_confirm" or "debt_absorb" or "debt_opening"=>MoneyEffectKind.Debt,
        _=>MoneyEffectKind.Accounting
    };
}
