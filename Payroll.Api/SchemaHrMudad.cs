using System.Data.Common;
namespace Payroll;

/// <summary>U-W1-11c / U-W2-04: hr.mudad_upload + hr.mudad_line. Design §5b / file 61 / H33 / S11 / M-0606.
/// One audit row per upload file; lines clustered by period_month; unique upload+iqama.</summary>
public static class SchemaHrMudad
{
    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        string company=SchemaSql.Q(c,"hr","company");
        string hashLen=SchemaSql.IsSql(c)?"DATALENGTH(file_hash)=32":"length(file_hash)=32";
        SchemaSql.EnsureTable(c,tx,"hr","mudad_upload",$"""
            id {g} NOT NULL, company_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            file_kind {SchemaSql.Varchar(c,20)} NOT NULL, file_name {SchemaSql.Text(c,260)} NOT NULL,
            file_hash {SchemaSql.Binary(c,32)} NOT NULL, row_count {SchemaSql.Int(c)} NOT NULL,
            status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            uploaded_by {SchemaSql.Text(c,128)} NOT NULL, uploaded_at {SchemaSql.DateTime(c)} NOT NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_hr_mudad_upload {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_hr_mudad_upload_file UNIQUE(company_id,period_month,file_kind,file_hash),
            CONSTRAINT CK_hr_mudad_upload_kind CHECK(file_kind IN ('bank_accounts','salaries')),
            CONSTRAINT CK_hr_mudad_upload_status CHECK(status IN (1,2,3)),
            CONSTRAINT CK_hr_mudad_upload_rows CHECK(row_count>=0),
            CONSTRAINT CK_hr_mudad_upload_hash CHECK({hashLen}),
            CONSTRAINT CK_hr_mudad_upload_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_hr_mudad_upload_company FOREIGN KEY(company_id) REFERENCES {company}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureTable(c,tx,"hr","mudad_line",$"""
            id {g} NOT NULL, upload_id {g} NOT NULL, period_month {SchemaSql.Date(c)} NOT NULL,
            company_id {g} NOT NULL, iqama {SchemaSql.Varchar(c,20)} NOT NULL,
            erp_employee_id {g} NULL, official_name {SchemaSql.Text(c,200)} NOT NULL,
            iban {SchemaSql.Varchar(c,34)} NULL, bank_name {SchemaSql.Text(c,100)} NULL,
            basic_salary {m} NULL, housing_allowance {m} NULL, transport_allowance {m} NULL,
            gross_salary {m} NULL, overtime_qty {m} NULL, bonus_qty {m} NULL,
            sales_commission_qty {m} NULL, other_entitlements_qty {m} NULL,
            gosi_deduction_rate {m} NULL, absence_qty {m} NULL, other_deductions_qty {m} NULL,
            header_variant {SchemaSql.Varchar(c,12)} NOT NULL DEFAULT 'base11',
            match_status {SchemaSql.Tiny(c)} NOT NULL DEFAULT 1,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_hr_mudad_line {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_hr_mudad_line_iqama UNIQUE(upload_id,iqama),
            CONSTRAINT CK_hr_mudad_line_variant CHECK(header_variant IN ('base11','togary13')),
            CONSTRAINT CK_hr_mudad_line_match CHECK(match_status IN (1,2,3,4)),
            CONSTRAINT CK_hr_mudad_line_month CHECK({SchemaSql.MonthCheck(c)}),
            CONSTRAINT FK_hr_mudad_line_upload FOREIGN KEY(upload_id) REFERENCES {SchemaSql.Q(c,"hr","mudad_upload")}(id) ON DELETE NO ACTION,
            CONSTRAINT FK_hr_mudad_line_company FOREIGN KEY(company_id) REFERENCES {company}(id) ON DELETE NO ACTION
            """);
        SchemaSql.EnsureMonthIndex(c,tx,"hr","mudad_line");
        SchemaSql.EnsureRowVersion(c,tx,"hr","mudad_upload");
        SchemaSql.EnsureRowVersion(c,tx,"hr","mudad_line");
        SchemaSql.CreateIndexIfMissing(c,tx,"hr","mudad_upload","IX_hr_mudad_upload_company","company_id,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"hr","mudad_line","IX_hr_mudad_line_upload","upload_id");
        SchemaSql.CreateIndexIfMissing(c,tx,"hr","mudad_line","IX_hr_mudad_line_iqama","iqama,period_month");
        SchemaSql.CreateIndexIfMissing(c,tx,"hr","mudad_line","IX_hr_mudad_line_employee","erp_employee_id");
        SchemaMoneyGuard.Retain(c,tx,"hr","mudad_upload","o.company_id=NEW.company_id AND o.period_month=NEW.period_month AND o.file_kind=NEW.file_kind AND o.file_hash=NEW.file_hash");
        SchemaMoneyGuard.Retain(c,tx,"hr","mudad_line","o.upload_id=NEW.upload_id AND o.iqama=NEW.iqama");
    }
}
