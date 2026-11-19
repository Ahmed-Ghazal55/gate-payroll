using System.Data.Common;
namespace Payroll;

// Additive indexes for rider-app filters. SQLite already has these in Store startup;
// SQL Server gets persisted JSON computed columns + filtered indexes (ErpDataIndexes pattern).
public static class RiderDataIndexes
{
    public static void Ensure(DbConnection c)
    {
        using var cmd = c.CreateCommand();
        if (c is Microsoft.Data.Sqlite.SqliteConnection)
        {
            // Idempotent no-op: Store constructor already creates the SQLite rider indexes.
            cmd.CommandText = "SELECT 1;";
            cmd.ExecuteNonQuery();
            return;
        }

        cmd.CommandText = """
            SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
            IF COL_LENGTH('dbo.records','rec_iqama') IS NULL ALTER TABLE dbo.records ADD rec_iqama AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.iqama'));
            IF COL_LENGTH('dbo.records','rec_rider') IS NULL ALTER TABLE dbo.records ADD rec_rider AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.rider'));
            IF COL_LENGTH('dbo.records','rec_rider_iqama') IS NULL ALTER TABLE dbo.records ADD rec_rider_iqama AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.riderIqama'));
            IF COL_LENGTH('dbo.records','rec_period') IS NULL ALTER TABLE dbo.records ADD rec_period AS CONVERT(nvarchar(40),JSON_VALUE(body,'$.period'));
            IF COL_LENGTH('dbo.records','rec_target_iqama') IS NULL ALTER TABLE dbo.records ADD rec_target_iqama AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.targetIqama'));
            IF COL_LENGTH('dbo.records','rec_status') IS NULL ALTER TABLE dbo.records ADD rec_status AS CONVERT(nvarchar(64),JSON_VALUE(body,'$.status'));
            IF COL_LENGTH('dbo.records','rec_created_at') IS NULL ALTER TABLE dbo.records ADD rec_created_at AS CONVERT(nvarchar(40),JSON_VALUE(body,'$.createdAt'));
            IF COL_LENGTH('dbo.records','rec_campaign_id') IS NULL ALTER TABLE dbo.records ADD rec_campaign_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.campaignId'));
            IF COL_LENGTH('dbo.records','rec_expires_at') IS NULL ALTER TABLE dbo.records ADD rec_expires_at AS CONVERT(nvarchar(40),JSON_VALUE(body,'$.expiresAt'));
            IF COL_LENGTH('dbo.records','rec_company_id') IS NULL ALTER TABLE dbo.records ADD rec_company_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.companyId'));
            IF COL_LENGTH('dbo.records','rec_sponsor_id') IS NULL ALTER TABLE dbo.records ADD rec_sponsor_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.sponsorId'));
            IF COL_LENGTH('dbo.records','rec_target_company_id') IS NULL ALTER TABLE dbo.records ADD rec_target_company_id AS CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.targetCompanyId'),JSON_VALUE(body,'$.targetSponsorId')));
            IF COL_LENGTH('dbo.records','rec_target') IS NULL ALTER TABLE dbo.records ADD rec_target AS CONVERT(nvarchar(64),COALESCE(JSON_VALUE(body,'$.target'),JSON_VALUE(body,'$.targetAudience')));
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_debt_iqama') EXEC('CREATE INDEX ix_debt_iqama ON dbo.records(rec_iqama,rec_status) WHERE kind=''debt''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_pay_cycle_line_rider_period') EXEC('CREATE INDEX ix_pay_cycle_line_rider_period ON dbo.records(rec_rider,rec_period) WHERE kind=''pay_cycle_line''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_pay_cycle_line_period') EXEC('CREATE INDEX ix_pay_cycle_line_period ON dbo.records(rec_period) WHERE kind=''pay_cycle_line''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_pay_cycle_period') EXEC('CREATE INDEX ix_pay_cycle_period ON dbo.records(rec_period) WHERE kind=''pay_cycle''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_run_iqama_period') EXEC('CREATE INDEX ix_run_iqama_period ON dbo.records(rec_iqama,rec_period) WHERE kind=''run''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_mobile_broadcast_target') EXEC('CREATE INDEX ix_mobile_broadcast_target ON dbo.records(rec_target_iqama,rec_created_at) WHERE kind=''mobile_broadcast''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_rider_ask_iqama') EXEC('CREATE INDEX ix_rider_ask_iqama ON dbo.records(rec_rider_iqama) WHERE kind=''rider_ask''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_rider_sreq_iqama') EXEC('CREATE INDEX ix_rider_sreq_iqama ON dbo.records(rec_rider_iqama) WHERE kind=''rider_service_request''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_mobile_session_iqama') EXEC('CREATE INDEX ix_mobile_session_iqama ON dbo.records(rec_iqama) WHERE kind=''mobile_session''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_submission_iqama') EXEC('CREATE INDEX ix_submission_iqama ON dbo.records(rec_iqama) WHERE kind=''submission''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_submission_campaign_iqama') EXEC('CREATE INDEX ix_submission_campaign_iqama ON dbo.records(rec_campaign_id,rec_iqama) WHERE kind=''submission''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_advance_req_iqama') EXEC('CREATE INDEX ix_advance_req_iqama ON dbo.records(rec_rider_iqama) WHERE kind=''advance_request''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_push_outbox_status') EXEC('CREATE INDEX ix_push_outbox_status ON dbo.records(rec_status,rec_created_at) WHERE kind=''push_outbox''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_campaign_open_expiry') EXEC('CREATE INDEX ix_campaign_open_expiry ON dbo.records(rec_status,rec_expires_at,rec_company_id,rec_target_iqama) WHERE kind=''campaign''');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_custom_form_open_expiry') EXEC('CREATE INDEX ix_custom_form_open_expiry ON dbo.records(rec_status,rec_expires_at,rec_target,rec_target_iqama) WHERE kind=''custom_form''');
            """;
        cmd.ExecuteNonQuery();
    }
}
