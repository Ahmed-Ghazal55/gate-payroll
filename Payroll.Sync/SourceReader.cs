using System.Data;
using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public sealed record SourceTable(string Schema,string Name,string Fields,bool Optional=false);
public sealed record SourceSnapshot(Dictionary<string,List<JsonObject>> Tables,DateTimeOffset StartedAt,DateTimeOffset FinishedAt,string Consistency);
public static class SourceReader
{
    // Explicit allowlist: final operational/financial facts; no credential versions, passwords or binary files.
    public static readonly SourceTable[] Catalog =
    [
        new("app","Sponsors","Id CompanyProfileId EmployerIdentityNumber RegistryNameAr RegistryNameEn CommercialRegistrationNumber UnifiedNationalNumber SponsorType Status ActiveFrom ActiveTo AddressCity IsDeleted"),
        new("platform","CompanyProfile","Id Code LegalNameAr LegalNameEn CommercialRegistrationNumber UnifiedNationalNumber Status"),
        new("platform","GlobalCities","Id Code NameAr NameEn RegionAr RegionEn CountryCode Status IsDeleted"),
        new("app","OperatingCities","Id GlobalCityId EnabledFrom DisabledAt Status IsDeleted"),
        new("app","Employees","Id IqamaNo FullNameAr FullNameEn Nationality PrimaryPhone Iban IsEmployee EngagementType Status HireDate SponsorId OperatingCityId OperationalWorkTypeId ContractStartDate ContractEndDate TerminationDate ResidencyProfession IsDeleted"),
        new("app","RiderProfiles","Id EmployeeId TShirtSize IsDeleted"),
        new("app","Housing","Id Code NameAr NameEn CityId OpenedDate ClosedDate Status IsDeleted"),
        new("app","HousingFloors","Id HousingId Name IsDeleted"),
        new("app","HousingRooms","Id HousingId FloorId Name Capacity CurrentOccupancy IsDeleted"),
        new("app","HousingExternalOccupants","Id RoomId Name IsDeleted"),
        new("app","HousingResidencePeriods","Id EmployeeId RoomId EffectiveFrom EffectiveTo"),
        new("app","Vehicles","Id AssetNumber SerialNumber PlateNumberAr PlateNumberEn Vin ChassisNumber VehicleManufacturerId VehicleModelId ModelYear VehicleType FuelType OwnershipType AcquisitionDate CurrentOdometer CurrentOperationalStatus OperatingCityId SponsorId RegisteredOwnerSponsorId RegisteredOwnerSupplierId RegistrationType CurrentAssignmentId IsDeleted"),
        new("app","VehicleRegistrations","Id VehicleId RegistrationNumber IssuingAuthority IssueDate ExpiryDate Status IsCurrent IsDeleted"),
        new("app","VehicleInsurancePolicies","Id VehicleId ProviderName PolicyNumber CoverageType EffectiveFrom ExpiryDate Status IsCurrent IsDeleted"),
        new("app","RiderVehicleAssignments","Id RiderProfileId VehicleId PreviousAssignmentId StartedAtUtc EndedAtUtc StartOdometer EndOdometer StartVehicleCondition EndVehicleCondition Status IsRealRider PermissionStartsOn PermissionEndsOn IsDeleted"),
        new("app","RealRiders","Id RiderVehicleAssignmentId Name IqamaNo RelationshipToAssignedRider"),
        new("app","RiderVehicleAssignmentEvents","Id RiderVehicleAssignmentId EventType OccurredAtUtc"),
        new("platform","ClientPlatforms","Id Code NameAr NameEn Status"),
        new("app","PlatformRiderAccounts","Id ClientPlatformId Code ExternalAccountId Status AcquisitionDate StartDate EndDate OperatingCityId RegisteredEmployeeId PaymentModel SponsorId IsDeleted"),
        new("app","RiderClientAssignments","Id RiderProfileId ClientContractId PlatformRiderAccountId EffectiveFrom EffectiveTo Status PaymentModel RiderAccountSlot NinjaRiderType IsDeleted"),
        new("app","PlatformAccountRegistrations","Id RegisteredEmployeeId RiderProfileId ClientPlatformId ClientContractId SponsorId OperatingCityId RegistrationType Status RequestedAtUtc ActivatedAtUtc PlatformRiderAccountId IsDeleted"),
        new("app","HrLegalCases","Id CaseNumber PersonName PersonType EmployeeId RiderProfileId SponsorId SponsorPartyRole CaseDate CaseTime Status Details IsDeleted"),
        new("app","HrLegalCaseHearings","Id LegalCaseId HearingNumber HearingDate HearingTime Status Details Location IsDeleted"),
        ..PullContracts.Tables
    ];
    public static bool SnapshotAvailable(SqlConnection source)
    {
        using var cmd=source.CreateCommand();cmd.CommandText="SELECT snapshot_isolation_state FROM sys.databases WHERE name=DB_NAME()";
        return Convert.ToInt32(cmd.ExecuteScalar())==1;
    }
    public static SourceSnapshot Read(DbConnection connection,bool requireStableRowversion=true,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset started=DateTimeOffset.UtcNow;
        bool sql=connection is SqlConnection;
        byte[]? Stamp(){if(!sql)return null;using var cmd=connection.CreateCommand();cmd.CommandText="SELECT @@DBTS";return (byte[]?)cmd.ExecuteScalar();}
        var before=requireStableRowversion?Stamp():null;
        var tables=new Dictionary<string,List<JsonObject>>(StringComparer.Ordinal);
        foreach(var table in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(table.Optional&&!OptionalAvailable(connection,table,cancellationToken))continue;
            using var cmd=connection.CreateCommand();using var cancel=SyncCancellation.Register(cmd,cancellationToken);cmd.CommandTimeout=120;
            cmd.CommandText=$"SELECT {string.Join(',',table.Fields.Split(' ').Select(f=>"["+f+"]"))} FROM {(sql?"["+table.Schema+"].":"")}[{table.Name}] ORDER BY [Id]";
            using var reader=cmd.ExecuteReader(CommandBehavior.SequentialAccess);var rows=new List<JsonObject>();
            while(reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();var row=new JsonObject();
                for(int i=0;i<reader.FieldCount;i++)
                {
                    string field=reader.GetName(i);object? value=reader.IsDBNull(i)?null:reader.GetValue(i);
                    row[field]=value!=null&&field is "IsDeleted" or "IsEmployee" or "IsRealRider" or "IsCurrent"?JsonValue.Create(Convert.ToBoolean(value)):Value(value);
                }
                rows.Add(row);
            }
            tables.Add(table.Name,rows);
        }
        HsFactsSource.Read(connection,tables,cancellationToken);
        var after=requireStableRowversion?Stamp():null;
        if(before!=null&&after!=null&&!before.SequenceEqual(after))throw new ImportFailure("source_changed_during_read");
        return new(tables,started,DateTimeOffset.UtcNow,sql?(connection.Database=="erp_copy"?"local_read_only_copy":"read_committed_database_rowversion_stable"):"synthetic_sqlite");
    }
    static bool OptionalAvailable(DbConnection connection,SourceTable table,CancellationToken cancellationToken)
    {
        var fields=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd=connection.CreateCommand();using var cancel=SyncCancellation.Register(cmd,cancellationToken);
        if(connection is SqlConnection)
        {
            cmd.CommandText="SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table";
            var schema=cmd.CreateParameter();schema.ParameterName="@schema";schema.Value=table.Schema;cmd.Parameters.Add(schema);
            var name=cmd.CreateParameter();name.ParameterName="@table";name.Value=table.Name;cmd.Parameters.Add(name);
        }
        else cmd.CommandText="PRAGMA table_info(["+table.Name+"])";
        using var reader=cmd.ExecuteReader();while(reader.Read())fields.Add(reader.GetString(connection is SqlConnection?0:1));
        if(fields.Count==0)return false;
        if(!table.Fields.Split(' ').All(fields.Contains))throw new ImportFailure("source_contract_missing_fields:"+table.Name);
        return true;
    }
    internal static JsonNode? Value(object? value)=>value switch
    {
        null=>null,Guid g=>JsonValue.Create(g.ToString("D")), DateTimeOffset d=>JsonValue.Create(d.ToString("O")),DateTime d=>JsonValue.Create(d.ToString("yyyy-MM-ddTHH:mm:ss.fffffff")),TimeSpan t=>JsonValue.Create(t.ToString("c")),byte[]=>throw new ImportFailure("binary_source_field_not_allowed"),_=>System.Text.Json.JsonSerializer.SerializeToNode(value)
    };
    public static void VerifyReadOnly(SqlConnection source)
    {
        using var cmd=source.CreateCommand();cmd.CommandText="SELECT CASE WHEN DATABASEPROPERTYEX(DB_NAME(),'Updateability')='READ_ONLY' THEN 0 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','INSERT')=1 OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','UPDATE')=1 OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','DELETE')=1 OR HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','ALTER')=1 OR IS_SRVROLEMEMBER('sysadmin')=1 THEN 1 ELSE 0 END";
        if(Convert.ToInt32(cmd.ExecuteScalar())!=0)throw new ImportFailure("source_has_write_permissions");
    }
}
