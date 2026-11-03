using System.Data.Common;
namespace Payroll;

// Additive indexes only: row JSON and existing financial indexes remain untouched.
public static class ErpDataIndexes
{
    public static void Ensure(DbConnection c)
    {
        using var cmd=c.CreateCommand();
        cmd.CommandText=c is Microsoft.Data.Sqlite.SqliteConnection ? """
            CREATE INDEX IF NOT EXISTS ix_erp_source ON records(kind,json_extract(body,'$.erpSourceId')) WHERE json_extract(body,'$.erpSourceId') IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_erp_company ON records(kind,coalesce(json_extract(body,'$.companyId'),json_extract(body,'$.sponsorId')));
            CREATE INDEX IF NOT EXISTS ix_erp_housing ON records(kind,json_extract(body,'$.housingId'));
            CREATE INDEX IF NOT EXISTS ix_erp_city ON records(kind,json_extract(body,'$.cityId'));
            CREATE INDEX IF NOT EXISTS ix_erp_rider ON records(kind,coalesce(json_extract(body,'$.riderId'),json_extract(body,'$.registeredRiderId')));
            CREATE INDEX IF NOT EXISTS ix_erp_vehicle ON records(kind,json_extract(body,'$.vehicleId'));
            CREATE INDEX IF NOT EXISTS ix_erp_assignment ON records(kind,json_extract(body,'$.assignmentId'));
            CREATE INDEX IF NOT EXISTS ix_erp_effective ON records(kind,json_extract(body,'$.effectiveFrom'),json_extract(body,'$.effectiveTo'));
            """ : """
            SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
            IF COL_LENGTH('dbo.records','erp_source_id') IS NULL ALTER TABLE dbo.records ADD erp_source_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.erpSourceId'));
            IF COL_LENGTH('dbo.records','erp_company_id') IS NULL ALTER TABLE dbo.records ADD erp_company_id AS CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.companyId'),JSON_VALUE(body,'$.sponsorId')));
            IF COL_LENGTH('dbo.records','erp_housing_id') IS NULL ALTER TABLE dbo.records ADD erp_housing_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.housingId'));
            IF COL_LENGTH('dbo.records','erp_city_id') IS NULL ALTER TABLE dbo.records ADD erp_city_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.cityId'));
            IF COL_LENGTH('dbo.records','erp_rider_id') IS NULL ALTER TABLE dbo.records ADD erp_rider_id AS CONVERT(nvarchar(128),COALESCE(JSON_VALUE(body,'$.riderId'),JSON_VALUE(body,'$.registeredRiderId')));
            IF COL_LENGTH('dbo.records','erp_vehicle_id') IS NULL ALTER TABLE dbo.records ADD erp_vehicle_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.vehicleId'));
            IF COL_LENGTH('dbo.records','erp_assignment_id') IS NULL ALTER TABLE dbo.records ADD erp_assignment_id AS CONVERT(nvarchar(128),JSON_VALUE(body,'$.assignmentId'));
            IF COL_LENGTH('dbo.records','erp_effective_from') IS NULL ALTER TABLE dbo.records ADD erp_effective_from AS CONVERT(nvarchar(40),JSON_VALUE(body,'$.effectiveFrom'));
            IF COL_LENGTH('dbo.records','erp_effective_to') IS NULL ALTER TABLE dbo.records ADD erp_effective_to AS CONVERT(nvarchar(40),JSON_VALUE(body,'$.effectiveTo'));
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_source') EXEC('CREATE INDEX ix_erp_source ON dbo.records(kind,erp_source_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_company') EXEC('CREATE INDEX ix_erp_company ON dbo.records(kind,erp_company_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_housing') EXEC('CREATE INDEX ix_erp_housing ON dbo.records(kind,erp_housing_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_city') EXEC('CREATE INDEX ix_erp_city ON dbo.records(kind,erp_city_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_rider') EXEC('CREATE INDEX ix_erp_rider ON dbo.records(kind,erp_rider_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_vehicle') EXEC('CREATE INDEX ix_erp_vehicle ON dbo.records(kind,erp_vehicle_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_assignment') EXEC('CREATE INDEX ix_erp_assignment ON dbo.records(kind,erp_assignment_id)');
            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.records') AND name='ix_erp_effective') EXEC('CREATE INDEX ix_erp_effective ON dbo.records(kind,erp_effective_from,erp_effective_to)');
            """;
        cmd.ExecuteNonQuery();
    }
}
