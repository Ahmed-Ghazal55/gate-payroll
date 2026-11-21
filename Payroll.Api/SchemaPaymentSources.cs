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
