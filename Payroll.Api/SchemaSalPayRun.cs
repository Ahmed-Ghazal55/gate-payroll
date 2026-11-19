using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-14c: empty cycle core. Design §4.1–2 E8 / BR §32, §38.5, §40.4–6.
/// Engine stage 0–9 maps to UI 1–10. Level 1 → net → Level 2; debt confirmation precedes salary approval.
/// Flags describe the run; runtime posting/payout guards remain U-W2-31. Capture gate installed by SchemaOperationCapture.</summary>
public static class SchemaSalPayRun
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),run=SchemaSql.Q(c,"sal","pay_run"),rider=SchemaSql.Q(c,"sal","pay_run_rider"),hold=SchemaSql.Q(c,"sal","shared_rider_hold");
        SchemaSql.EnsureTable(c,tx,"sal","pay_run",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            app_code {SchemaSql.Varchar(c,20)} NOT NULL, company_id {g} NOT NULL,
            stage {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            run_mode {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, payout_blocked {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            currency_code {SchemaSql.Varchar(c,3)} NOT NULL DEFAULT 'SAR',
            capture_state {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0, calc_started_at {SchemaSql.DateTime(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_scope UNIQUE (id,company_id,period_month),
            CONSTRAINT UQ_sal_run_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_run_stage CHECK (stage BETWEEN 0 AND 9),
            CONSTRAINT CK_sal_run_status CHECK (status IN (1,2,3,4,5)),
            CONSTRAINT CK_sal_run_flags CHECK (run_mode IN (0,1) AND payout_blocked IN (0,1)),
            CONSTRAINT CK_sal_run_currency CHECK (currency_code='SAR'),
            CONSTRAINT CK_sal_run_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT CK_sal_run_capture CHECK (capture_state IN (0,1,2) AND (stage<=1 OR capture_state>0) AND (capture_state=0 OR calc_started_at IS NOT NULL)),
            CONSTRAINT FK_sal_run_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","pay_run","UQ_sal_run_active","app_code,company_id,period_month","status<>4");
        SchemaSql.EnsureTable(c,tx,"sal","pay_run_rider",$"""
            id {g} NOT NULL, pay_run_id {g} NOT NULL, company_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, erp_rider_id {g} NOT NULL,
            gross {m} NOT NULL, commission {m} NOT NULL, salary_net {m} NOT NULL,
            old_debt_instalment {m} NOT NULL DEFAULT 0, salary_approved_at {SchemaSql.DateTime(c)} NULL,
            debt_confirm_status {SchemaSql.Varchar(c,12)} NOT NULL DEFAULT 'pending',
            bank_eligible {SchemaSql.Bit(c)} NOT NULL DEFAULT 0, keeta_ineligible {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            payable_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run_rider {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_rider UNIQUE (pay_run_id,erp_rider_id),
            CONSTRAINT UQ_sal_rider_month UNIQUE (id,period_month),
            CONSTRAINT CK_sal_rider_instalment CHECK (old_debt_instalment>=0),
            CONSTRAINT CK_sal_rider_debt CHECK (debt_confirm_status IN ('pending','confirmed','deferred') AND (debt_confirm_status<>'deferred' OR old_debt_instalment=0)),
            CONSTRAINT CK_sal_rider_approval CHECK (salary_approved_at IS NULL OR debt_confirm_status IN ('confirmed','deferred')),
            CONSTRAINT CK_sal_rider_flags CHECK (bank_eligible IN (0,1) AND keeta_ineligible IN (0,1)),
            CONSTRAINT CK_sal_rider_bank CHECK (bank_eligible=0 OR (salary_approved_at IS NOT NULL AND debt_confirm_status IN ('confirmed','deferred') AND payable_id IS NOT NULL AND salary_net>0)),
            CONSTRAINT FK_sal_rider_run FOREIGN KEY (pay_run_id,company_id,period_month) REFERENCES {run}(id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_rider_payable FOREIGN KEY (payable_id,company_id,period_month) REFERENCES {SchemaSql.Q(c,"pay","payable")}(id,company_id,period_month) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","pay_run_item",$"""
            id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL, pay_run_rider_id {g} NOT NULL,
            line_no {SchemaSql.Int(c)} NOT NULL, stage_code {SchemaSql.Varchar(c,40)} NOT NULL,
            item_code {SchemaSql.Varchar(c,40)} NOT NULL, amount {m} NOT NULL,
            affects_net {SchemaSql.Bit(c)} NOT NULL, account_code {SchemaSql.Varchar(c,16)} NOT NULL,
            erp_platform_account_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_pay_run_item {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_run_item_line UNIQUE (pay_run_rider_id,line_no),
            CONSTRAINT CK_sal_item_line CHECK (line_no>0),
            CONSTRAINT CK_sal_item_stage CHECK (stage_code IN ('invoice','distribution','commission','level1','net','level2','old_debt','adjustment')),
            CONSTRAINT CK_sal_item_affects CHECK (affects_net IN (0,1)),
            CONSTRAINT FK_sal_item_rider FOREIGN KEY (pay_run_rider_id,period_month) REFERENCES {rider}(id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_sal_item_account FOREIGN KEY (account_code) REFERENCES {SchemaSql.Q(c,"fin","account")}(code) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","shared_rider_hold",$"""
            id {g} NOT NULL, erp_rider_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            status {SchemaSql.Varchar(c,10)} NOT NULL DEFAULT 'open',
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_shared_hold {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_sal_shared_hold_status CHECK (status IN ('open','cleared')),
            CONSTRAINT CK_sal_shared_hold_month CHECK ({SchemaSql.MonthCheck(c)})
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","shared_rider_hold","UQ_sal_shared_hold_open","erp_rider_id,period_month","status='open'");
        SchemaSql.EnsureTable(c,tx,"sal","shared_rider_hold_app",$"""
            id {g} NOT NULL, hold_id {g} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            received {SchemaSql.Bit(c)} NOT NULL DEFAULT 0,
            CONSTRAINT PK_sal_shared_hold_app {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT UQ_sal_shared_hold_app UNIQUE (hold_id,app_code),
            CONSTRAINT CK_sal_shared_hold_received CHECK (received IN (0,1)),
            CONSTRAINT FK_sal_shared_hold_app FOREIGN KEY (hold_id) REFERENCES {hold}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"sal","rider_request",$"""
            id {g} NOT NULL, erp_rider_id {g} NOT NULL, company_id {g} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL, app_code {SchemaSql.Varchar(c,20)} NOT NULL,
            kind {SchemaSql.Varchar(c,12)} NOT NULL, amount {m} NOT NULL DEFAULT 0,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_sal_rider_request {SchemaSql.PrimaryKey(c)} (id),
            CONSTRAINT CK_sal_request_kind CHECK (kind IN ('advance','hold','deduction')),
            CONSTRAINT CK_sal_request_status CHECK (status IN (1,2,3,4,5,6)),
            CONSTRAINT CK_sal_request_amount CHECK (amount>=0),
            CONSTRAINT CK_sal_request_month CHECK ({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_sal_request_company FOREIGN KEY (company_id) REFERENCES {SchemaSql.Q(c,"hr","company")}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateFilteredUniqueIndexIfMissing(c,tx,"sal","rider_request","UQ_sal_request_hold","erp_rider_id,period_month,app_code","kind='hold' AND status<4");
        foreach(string table in new[]{"pay_run","pay_run_rider","pay_run_item","rider_request"})
        {SchemaSql.EnsureMonthIndex(c,tx,"sal",table);SchemaSql.EnsureRowVersion(c,tx,"sal",table);}
        SchemaSql.EnsureRowVersion(c,tx,"sal","shared_rider_hold");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run","IX_sal_run_company","company_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_rider","IX_sal_rider_run","pay_run_id,company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_rider","IX_sal_rider_payable","payable_id,company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_item","IX_sal_item_rider","pay_run_rider_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","pay_run_item","IX_sal_item_account","account_code");
        SchemaSql.CreateIndexIfMissing(c,tx,"sal","rider_request","IX_sal_request_company","company_id");
    }
}
