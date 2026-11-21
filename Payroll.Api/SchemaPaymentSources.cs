using System.Data.Common;
namespace Payroll;

/// <summary>WP1 contract schedules and wallet registers, additive and retained on both providers.</summary>
public static class SchemaPaymentSources
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c),company=SchemaSql.Q(c,"hr","company"),json=SchemaSql.IsSql(c)?"nvarchar(max)":"TEXT";
        foreach(string table in new[]{"property","financier","lease_contract","finance_contract"})
        {
            SchemaSql.EnsureTable(c,tx,"pay",table,$"""
                id {g} NOT NULL {SchemaSql.PrimaryKey(c)},company_id {g} NOT NULL REFERENCES {company}(id),
                name_ar {SchemaSql.Text(c,200)} NOT NULL, metadata {json} NOT NULL,
                status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4)),
                {SchemaSql.MoneyAuditCols(c)}
                """);
            SchemaMoneyGuard.Retain(c,tx,"pay",table);SchemaSql.CreateIndexIfMissing(c,tx,"pay",table,"IX_"+table+"_company","company_id,status");
        }
        foreach(var (table,parent) in new[]{("lease_schedule","lease_contract"),("finance_schedule","finance_contract")})
        {
            SchemaSql.EnsureTable(c,tx,"pay",table,$"""
                id {g} NOT NULL {SchemaSql.PrimaryKey(c)},contract_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay",parent)}(id),
                document_id {g} NOT NULL UNIQUE REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
                period_month {SchemaSql.Date(c)} NOT NULL,due_date {SchemaSql.Date(c)} NOT NULL,
                principal {m} NOT NULL CHECK(principal>=0),interest {m} NOT NULL CHECK(interest>=0),
                {SchemaSql.MoneyAuditCols(c)},CONSTRAINT UQ_{table}_due UNIQUE(contract_id,due_date)
                """);
            SchemaMoneyGuard.Retain(c,tx,"pay",table);SchemaMoneyRegister.Immutable(c,tx,"pay",table,"contract_id","document_id","period_month","due_date","principal","interest");SchemaSql.EnsureMonthIndex(c,tx,"pay",table);
        }
        foreach(string table in new[]{"fuel_wallet","prepaid_wallet"})
        {
            SchemaSql.EnsureTable(c,tx,"pay",table,$"""
                id {g} NOT NULL {SchemaSql.PrimaryKey(c)},company_id {g} NOT NULL REFERENCES {company}(id),
                provider {SchemaSql.Text(c,100)} NOT NULL,account_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"fin","account")}(id),
                kind {SchemaSql.Varchar(c,20)} NOT NULL,status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1 CHECK(status IN (1,2)),
                {SchemaSql.MoneyAuditCols(c)},CONSTRAINT UQ_{table}_account UNIQUE(company_id,account_id)
                """);
            SchemaMoneyGuard.Retain(c,tx,"pay",table);SchemaMoneyRegister.Immutable(c,tx,"pay",table,"company_id","account_id","kind","provider");
            SchemaSql.EnsureTable(c,tx,"pay",table+"_movement",$"""
                id {g} NOT NULL {SchemaSql.PrimaryKey(c)},wallet_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay",table)}(id),
                document_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
                payment_id {g} NULL REFERENCES {SchemaSql.Q(c,"pay","payment")}(id),
                journal_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"fin","journal")}(id),
                occurred_on {SchemaSql.Date(c)} NOT NULL,period_month {SchemaSql.Date(c)} NOT NULL,
                source_type {SchemaSql.Varchar(c,30)} NOT NULL,source_id {SchemaSql.Varchar(c,100)} NOT NULL,
                amount {m} NOT NULL CHECK(amount<>0),{SchemaSql.MoneyAuditCols(c)},
                CONSTRAINT UQ_{table}_movement_source UNIQUE(wallet_id,source_type,source_id)
                """);
            SchemaMoneyGuard.Retain(c,tx,"pay",table+"_movement","o.wallet_id=NEW.wallet_id AND o.source_type=NEW.source_type AND o.source_id=NEW.source_id");SchemaMoneyRegister.Immutable(c,tx,"pay",table+"_movement","wallet_id","document_id","payment_id","journal_id","occurred_on","period_month","source_type","source_id","amount");SchemaSql.EnsureMonthIndex(c,tx,"pay",table+"_movement");
        }
        SchemaSql.EnsureTable(c,tx,"pay","wallet_reconciliation",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)},wallet_id {g} NOT NULL,wallet_kind {SchemaSql.Varchar(c,20)} NOT NULL,
            period_month {SchemaSql.Date(c)} NOT NULL,statement_balance {m} NOT NULL CHECK(statement_balance>=0),
            book_balance {m} NOT NULL,reference {SchemaSql.Varchar(c,100)} NOT NULL,{SchemaSql.MoneyAuditCols(c)}
            """);
        SchemaMoneyGuard.Retain(c,tx,"pay","wallet_reconciliation");SchemaMoneyRegister.Immutable(c,tx,"pay","wallet_reconciliation","wallet_id","wallet_kind","period_month","statement_balance","book_balance","reference");
        foreach(string table in new[]{"hr_fee_request"})
        {
            SchemaSql.EnsureTable(c,tx,"pay",table,$"""
                id {g} NOT NULL {SchemaSql.PrimaryKey(c)} REFERENCES {SchemaSql.Q(c,"pay","document")}(id),
                company_id {g} NOT NULL REFERENCES {company}(id),erp_employee_id {g} NOT NULL,
                category {SchemaSql.Varchar(c,40)} NOT NULL,bearer {SchemaSql.Varchar(c,20)} NOT NULL,
                rider_share {m} NOT NULL CHECK(rider_share>=0),company_share {m} NOT NULL CHECK(company_share>=0),
                {SchemaSql.MoneyAuditCols(c)}
                """);SchemaMoneyGuard.Retain(c,tx,"pay",table);SchemaMoneyRegister.Immutable(c,tx,"pay",table,"company_id","erp_employee_id","category","bearer","rider_share","company_share");
        }
        SchemaSql.EnsureTable(c,tx,"pay","rent_recognition",$"""
            id {g} NOT NULL {SchemaSql.PrimaryKey(c)},contract_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"pay","lease_contract")}(id),
            period_month {SchemaSql.Date(c)} NOT NULL,amount {m} NOT NULL CHECK(amount>0),
            journal_id {g} NOT NULL REFERENCES {SchemaSql.Q(c,"fin","journal")}(id),
            {SchemaSql.MoneyAuditCols(c)},CONSTRAINT UQ_rent_recognition UNIQUE(contract_id,period_month)
            """);SchemaMoneyGuard.Retain(c,tx,"pay","rent_recognition","o.contract_id=NEW.contract_id AND o.period_month=NEW.period_month");SchemaMoneyRegister.Immutable(c,tx,"pay","rent_recognition","contract_id","period_month","amount","journal_id");
    }
}
