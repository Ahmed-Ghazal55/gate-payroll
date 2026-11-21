using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
namespace ErpImport;

public static partial class OperationsLiveQuery
{
    /// <summary>Period report rows. VIN is never selected into list/period rows.</summary>
    public static PageResult VehiclesPeriodReport(DbConnection source, DateOnly from, DateOnly to, string mode,
        int page, int pageSize, string search, LiveOperationsQuery.Scope scope, bool detailed, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        bool riderMode = mode.Equals("rider", StringComparison.OrdinalIgnoreCase);
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@asOf", DateTimeOffset.UtcNow);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        if (search.Length > 0)
        {
            Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        }
        string sql = riderMode ? PeriodReportRiderSql(scopeSql, search.Length > 0) : PeriodReportVehicleSql(scopeSql, search.Length > 0);
        if (!detailed)
        {
            Add(cmd, "@skip", skip);
            Add(cmd, "@take", pageSize);
            sql += "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        }
        cmd.CommandText = sql;
        long total = 0;
        var summary = EmptyVehicleSummary();
        var items = new List<JsonObject>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (items.Count == 0)
                {
                    total = Long(reader, "TotalCount");
                    summary = new JsonObject
                    {
                        ["vehiclesOperating"] = Long(reader, "VehiclesOperating"),
                        ["totalOperatingDays"] = Decimal(reader, "TotalOperatingDays"),
                        ["sparePartsCost"] = Decimal(reader, "SparePartsCostAgg"),
                        ["amountToCollect"] = Decimal(reader, "AmountToCollectAgg"),
                        ["oilChangesCost"] = Decimal(reader, "OilChangesCostAgg")
                    };
                }
                var row = riderMode ? MapPeriodRiderRow(reader) : MapPeriodVehicleRow(reader);
                // Guard: never expose VIN on period rows even if a column sneaks in.
                row.Remove("vin");
                row.Remove("Vin");
                items.Add(row);
            }
        }
        if (items.Count == 0)
            summary = VehicleSummaryOnly(source, from, to, riderMode, search, scope, ct);
        return new(items, total, summary, DateTimeOffset.UtcNow);
    }

    static JsonObject VehicleSummaryOnly(DbConnection source, DateOnly from, DateOnly to, bool riderMode, string search,
        LiveOperationsQuery.Scope scope, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(cmd, "@asOf", DateTimeOffset.UtcNow);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        if (search.Length > 0) Add(cmd, "@q", "%" + EscapeLike(search) + "%");
        // Reuse page SQL with take 0 via a wrapper that only reads agg — simplest: run with take 1 skip 0 and ignore rows.
        Add(cmd, "@skip", 0);
        Add(cmd, "@take", 1);
        cmd.CommandText = (riderMode ? PeriodReportRiderSql(scopeSql, search.Length > 0) : PeriodReportVehicleSql(scopeSql, search.Length > 0))
            + "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return EmptyVehicleSummary();
        return new JsonObject
        {
            ["vehiclesOperating"] = Long(reader, "VehiclesOperating"),
            ["totalOperatingDays"] = Decimal(reader, "TotalOperatingDays"),
            ["sparePartsCost"] = Decimal(reader, "SparePartsCostAgg"),
            ["amountToCollect"] = Decimal(reader, "AmountToCollectAgg"),
            ["oilChangesCost"] = Decimal(reader, "OilChangesCostAgg")
        };
    }

    public static DetailResult VehicleDetail(DbConnection source, string vehicleId, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (!Guid.TryParse(vehicleId, out _)) throw new ImportFailure("vehicle_not_found");
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        string scopeSql = ScopeSponsor(scope, cmd, "s");
        Add(cmd, "@id", vehicleId);
        cmd.CommandText = $"""
            SELECT TOP 1 v.[Id], v.[AssetNumber], v.[SerialNumber], v.[PlateNumberAr], v.[PlateNumberEn],
                   v.[Vin], v.[ChassisNumber], v.[VehicleType], v.[ModelYear], v.[CurrentOdometer],
                   v.[CurrentOperationalStatus], v.[OperatingCityId],
                   s.[RegistryNameAr] AS SponsorNameAr, s.[UnifiedNationalNumber],
                   gc.[NameAr] AS CityNameAr
            FROM [app].[Vehicles] v
            LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
            LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=v.[OperatingCityId] AND oc.[IsDeleted]=0
            LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
            WHERE v.[IsDeleted]=0 AND v.[Id]=@id{scopeSql}
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ImportFailure("vehicle_not_found");
        var body = new JsonObject
        {
            ["id"] = Str(r, "Id"),
            ["assetNumber"] = Str(r, "AssetNumber"),
            ["serialNumber"] = Str(r, "SerialNumber"),
            ["plateNumberAr"] = Str(r, "PlateNumberAr"),
            ["plateNumberEn"] = Str(r, "PlateNumberEn"),
            ["vin"] = Str(r, "Vin"),
            ["chassisNumber"] = Str(r, "ChassisNumber"),
            ["vehicleType"] = VehicleTypeName(Str(r, "VehicleType")),
            ["modelYear"] = Str(r, "ModelYear"),
            ["currentOdometer"] = Decimal(r, "CurrentOdometer"),
            ["status"] = Str(r, "CurrentOperationalStatus"),
            ["sponsorNameAr"] = Str(r, "SponsorNameAr"),
            ["city"] = Str(r, "CityNameAr")
        };
        r.Close();
        body["assignmentHistory"] = VehicleAssignmentHistory(source, vehicleId, ct);
        return new(body, DateTimeOffset.UtcNow);
    }

    static JsonArray VehicleAssignmentHistory(DbConnection source, string vehicleId, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@id", vehicleId);
        cmd.CommandText = """
            SELECT a.[Id], a.[StartedAtUtc], a.[EndedAtUtc], a.[IsRealRider],
                   COALESCE(CASE WHEN a.[IsRealRider]=1 THEN e.[FullNameAr] ELSE rr.[Name] END, e.[FullNameAr]) AS RiderNameAr,
                   COALESCE(CASE WHEN a.[IsRealRider]=1 THEN e.[IqamaNo] ELSE rr.[IqamaNo] END, e.[IqamaNo]) AS IqamaNo
            FROM [app].[RiderVehicleAssignments] a
            LEFT JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
            LEFT JOIN [app].[Employees] e ON e.[Id]=rp.[EmployeeId] AND e.[IsDeleted]=0
            LEFT JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=a.[Id]
            WHERE a.[IsDeleted]=0 AND a.[VehicleId]=@id
            ORDER BY a.[StartedAtUtc] DESC
            """;
        return new JsonArray(ReadMapped(cmd, row => new JsonObject
        {
            ["id"] = Str(row, "Id"),
            ["startedAt"] = Str(row, "StartedAtUtc"),
            ["endedAt"] = Str(row, "EndedAtUtc"),
            ["isRealRider"] = Bool(row, "IsRealRider"),
            ["riderNameAr"] = Str(row, "RiderNameAr"),
            ["iqama"] = Str(row, "IqamaNo")
        }, ct).Select(x => (JsonNode?)x).ToArray());
    }

    public static PageResult VehicleOilChanges(DbConnection source, string vehicleId, LiveOperationsQuery.Scope scope, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (!Guid.TryParse(vehicleId, out _)) throw new ImportFailure("vehicle_not_found");
        // Ensure vehicle in scope first.
        _ = VehicleDetail(source, vehicleId, scope, ct);
        try
        {
            using var cmd = source.CreateCommand();
            cmd.CommandTimeout = CommandTimeoutSeconds;
            using var cancel = SyncCancellation.Register(cmd, ct);
            Add(cmd, "@id", vehicleId);
            cmd.CommandText = """
                SELECT o.[Id], o.[PerformedAtUtc], o.[OdometerAtChange], o.[OilQuantityLiters],
                       o.[OilCost], o.[OilFilterCost], o.[LaborCost], o.[OtherCost], o.[TotalCost],
                       o.[IsCancelled], o.[Notes]
                FROM [maintenance].[OilChangeOperations] o
                WHERE o.[VehicleId]=@id AND ISNULL(o.[IsCancelled],0)=0
                ORDER BY o.[PerformedAtUtc] DESC
                """;
            var items = ReadMapped(cmd, row => new JsonObject
            {
                ["id"] = Str(row, "Id"),
                ["performedAt"] = Str(row, "PerformedAtUtc"),
                ["odometer"] = Decimal(row, "OdometerAtChange"),
                ["oilLiters"] = Decimal(row, "OilQuantityLiters"),
                ["oilCost"] = Decimal(row, "OilCost"),
                ["filterCost"] = Decimal(row, "OilFilterCost"),
                ["laborCost"] = Decimal(row, "LaborCost"),
                ["otherCost"] = Decimal(row, "OtherCost"),
                ["totalCost"] = Decimal(row, "TotalCost"),
                ["notes"] = Str(row, "Notes")
            }, ct);
            var summary = new JsonObject
            {
                ["oilChangesCount"] = items.Count,
                ["oilChangesCost"] = items.Sum(i => NumDec(i, "totalCost"))
            };
            return new(items, items.Count, summary, DateTimeOffset.UtcNow);
        }
        catch (SqlException ex) when (IsMissingTable(ex))
        {
            return new([], 0, new JsonObject { ["oilChangesCount"] = 0, ["oilChangesCost"] = 0, ["message"] = NoSourceYet }, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>M9: all oil changes in period, vehicle-scoped like the period report.</summary>
    public static PageResult OilChangesPeriod(DbConnection source, DateOnly from, DateOnly to, int page, int pageSize,
        string search, LiveOperationsQuery.Scope scope, bool export, CancellationToken ct = default)
    {
        EnsureSql(source);
        if (to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        try
        {
            using var cmd = source.CreateCommand();
            cmd.CommandTimeout = export ? ExportCommandTimeoutSeconds : CommandTimeoutSeconds;
            using var cancel = SyncCancellation.Register(cmd, ct);
            Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            string scopeSql = ScopeSponsor(scope, cmd, "s");
            string searchPred = "";
            if (search.Length > 0)
            {
                Add(cmd, "@q", "%" + EscapeLike(search) + "%");
                searchPred = " AND (v.[PlateNumberAr] LIKE @q OR v.[AssetNumber] LIKE @q)";
            }
            if (!export)
            {
                Add(cmd, "@skip", skip);
                Add(cmd, "@take", pageSize);
            }
            string pageSql = export ? "" : "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
            cmd.CommandText = $"""
                WITH filtered AS (
                    SELECT o.[Id], o.[PerformedAtUtc], o.[OdometerAtChange], o.[OilQuantityLiters],
                           o.[OilCost], o.[OilFilterCost], o.[LaborCost], o.[OtherCost], o.[TotalCost],
                           v.[Id] AS VehicleId, v.[PlateNumberAr], v.[AssetNumber], v.[VehicleType]
                    FROM [maintenance].[OilChangeOperations] o
                    INNER JOIN [app].[Vehicles] v ON v.[Id]=o.[VehicleId] AND v.[IsDeleted]=0
                    LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                    WHERE ISNULL(o.[IsCancelled],0)=0{scopeSql}{searchPred}
                      AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) >= CONVERT(date, @from)
                      AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) <= CONVERT(date, @to)
                ),
                agg AS (
                    SELECT COUNT_BIG(*) AS TotalCount,
                           ISNULL(SUM(TotalCost),0) AS OilChangesCost,
                           ISNULL(SUM(OilQuantityLiters),0) AS TotalLiters
                    FROM filtered
                )
                SELECT f.*, a.TotalCount, a.OilChangesCost AS OilChangesCostAgg, a.TotalLiters
                FROM filtered f CROSS JOIN agg a
                ORDER BY f.PerformedAtUtc DESC, f.PlateNumberAr
                {pageSql}
                """;
            long total = 0;
            var summary = new JsonObject { ["oilChangesCount"] = 0L, ["oilChangesCost"] = 0m, ["totalLiters"] = 0m };
            var items = new List<JsonObject>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (items.Count == 0)
                    {
                        total = Long(reader, "TotalCount");
                        summary = new JsonObject
                        {
                            ["oilChangesCount"] = total,
                            ["oilChangesCost"] = Decimal(reader, "OilChangesCostAgg"),
                            ["totalLiters"] = Decimal(reader, "TotalLiters")
                        };
                    }
                    items.Add(new JsonObject
                    {
                        ["id"] = Str(reader, "Id"),
                        ["vehicleId"] = Str(reader, "VehicleId"),
                        ["plateNumberAr"] = Str(reader, "PlateNumberAr"),
                        ["assetNumber"] = Str(reader, "AssetNumber"),
                        ["vehicleType"] = VehicleTypeName(Str(reader, "VehicleType")),
                        ["performedAt"] = Str(reader, "PerformedAtUtc"),
                        ["odometer"] = Decimal(reader, "OdometerAtChange"),
                        ["oilLiters"] = Decimal(reader, "OilQuantityLiters"),
                        ["oilCost"] = Decimal(reader, "OilCost"),
                        ["filterCost"] = Decimal(reader, "OilFilterCost"),
                        ["laborCost"] = Decimal(reader, "LaborCost"),
                        ["otherCost"] = Decimal(reader, "OtherCost"),
                        ["totalCost"] = Decimal(reader, "TotalCost")
                    });
                }
            }
            return new(items, total, summary, DateTimeOffset.UtcNow);
        }
        catch (SqlException ex) when (IsMissingTable(ex))
        {
            return new([], 0, new JsonObject { ["oilChangesCount"] = 0, ["oilChangesCost"] = 0, ["message"] = NoSourceYet }, DateTimeOffset.UtcNow);
        }
    }

    static string BuildPurchaseInvoicesSql(DbCommand cmd, DateOnly from, DateOnly to, int skip, int take, string search, string? receiptId, bool unpaged = false)
    {
        var parts = new List<string> { "ISNULL(pr.[IsDeleted],0)=0" };
        if (receiptId is { Length: > 0 })
        {
            if (!Guid.TryParse(receiptId, out _)) throw new ImportFailure("purchase_not_found");
            Add(cmd, "@receiptId", receiptId);
            parts.Add("pr.[Id]=@receiptId");
        }
        else
        {
            Add(cmd, "@from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(cmd, "@to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            // Match either supplier bill date (InvoiceDate) or Riyadh receiving date (ReceivedAtUtc).
            // Omar's receipts screen orders by ReceivedAtUtc without filtering on InvoiceDate, so a receipt
            // recorded in November with an October supplier bill date must still appear in November.
            parts.Add("""
                (
                    (pr.[InvoiceDate] IS NOT NULL AND CONVERT(date, pr.[InvoiceDate]) >= CONVERT(date, @from) AND CONVERT(date, pr.[InvoiceDate]) <= CONVERT(date, @to))
                    OR (pr.[ReceivedAtUtc] IS NOT NULL AND CONVERT(date, SWITCHOFFSET(pr.[ReceivedAtUtc], '+03:00')) >= CONVERT(date, @from) AND CONVERT(date, SWITCHOFFSET(pr.[ReceivedAtUtc], '+03:00')) <= CONVERT(date, @to))
                )
                """);
        }
        if (search.Length > 0)
        {
            Add(cmd, "@q", "%" + EscapeLike(search) + "%");
            parts.Add("(pr.[ReceiptNumber] LIKE @q OR pr.[SupplierInvoiceNumber] LIKE @q OR sp.[LegalNameAr] LIKE @q)");
        }
        string where = " WHERE " + string.Join(" AND ", parts);
        string pageSql = "";
        if (!unpaged)
        {
            Add(cmd, "@skip", skip);
            Add(cmd, "@take", take);
            pageSql = "\nOFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        }
        return $"""
            WITH filtered AS (
                SELECT pr.[Id], pr.[ReceiptNumber], pr.[SupplierInvoiceNumber], pr.[InvoiceDate], pr.[ReceivedAtUtc],
                       pr.[Subtotal], pr.[TaxAmount], pr.[TotalAmount], pr.[Status],
                       sp.[LegalNameAr] AS SupplierNameAr
                FROM [maintenance].[PurchaseReceipts] pr
                LEFT JOIN [maintenance].[Suppliers] sp ON sp.[Id]=pr.[SupplierId] AND ISNULL(sp.[IsDeleted],0)=0
                {where}
            ),
            agg AS (SELECT COUNT_BIG(*) AS TotalCount, ISNULL(SUM(TotalAmount),0) AS TotalAmountSum FROM filtered)
            SELECT f.*, a.TotalCount, a.TotalAmountSum
            FROM filtered f CROSS JOIN agg a
            ORDER BY COALESCE(CONVERT(date, SWITCHOFFSET(f.ReceivedAtUtc, '+03:00')), CONVERT(date, f.InvoiceDate)) DESC,
                     f.InvoiceDate DESC, f.ReceiptNumber DESC{pageSql}
            """;
    }

    public static PageResult PurchaseInvoices(DbConnection source, DateOnly from, DateOnly to, int page, int pageSize,
        string search, LiveOperationsQuery.Scope scope, string? receiptId, CancellationToken ct = default, bool unpaged = false)
    {
        EnsureSql(source);
        if (receiptId is not { Length: > 0 } && to < from) throw new ImportFailure("invalid_period");
        pageSize = Math.Min(Math.Max(1, pageSize), MaxPageSize);
        page = Math.Max(1, page);
        int skip = checked((page - 1) * pageSize);
        try
        {
            using var cmd = source.CreateCommand();
            cmd.CommandTimeout = unpaged ? ExportCommandTimeoutSeconds : CommandTimeoutSeconds;
            using var cancel = SyncCancellation.Register(cmd, ct);
            // Purchases have no company column — company-scoped users get empty list / 404 on detail.
            if (!scope.AllCompanies)
            {
                if (receiptId is { Length: > 0 }) throw new ImportFailure("purchase_not_found");
                return new([], 0, new JsonObject { ["invoicesTotal"] = 0, ["totalAmount"] = 0, ["message"] = "فواتير المشتريات بلا عمود شركة — مخفية لنطاق شركة" }, DateTimeOffset.UtcNow);
            }
            cmd.CommandText = BuildPurchaseInvoicesSql(cmd, from, to, skip, pageSize, search, receiptId, unpaged);
            long total = 0;
            decimal totalAmount = 0;
            var items = new List<JsonObject>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    if (items.Count == 0)
                    {
                        total = Long(reader, "TotalCount");
                        totalAmount = Decimal(reader, "TotalAmountSum");
                    }
                    var row = new JsonObject
                    {
                        ["id"] = Str(reader, "Id"),
                        ["receiptNumber"] = Str(reader, "ReceiptNumber"),
                        ["supplierInvoiceNumber"] = Str(reader, "SupplierInvoiceNumber"),
                        ["invoiceDate"] = Str(reader, "InvoiceDate"),
                        ["receivedAt"] = Str(reader, "ReceivedAtUtc"),
                        ["subtotal"] = Decimal(reader, "Subtotal"),
                        ["taxAmount"] = Decimal(reader, "TaxAmount"),
                        ["totalAmount"] = Decimal(reader, "TotalAmount"),
                        ["status"] = PurchaseStatusLabel(Str(reader, "Status")),
                        ["supplierNameAr"] = Str(reader, "SupplierNameAr")
                    };
                    items.Add(row);
                }
            }
            if (receiptId is { Length: > 0 } && items.Count == 0) throw new ImportFailure("purchase_not_found");
            if (receiptId is { Length: > 0 } && items.Count == 1)
                items[0]["lines"] = PurchaseLines(source, items[0].S("id"), ct);
            if (receiptId is not { Length: > 0 } && items.Count == 0 && page > 1)
            {
                (total, totalAmount) = PurchaseSummaryOnly(source, from, to, search, ct);
            }
            var summary = new JsonObject { ["invoicesTotal"] = total, ["totalAmount"] = totalAmount };
            return new(items, total, summary, DateTimeOffset.UtcNow);
        }
        catch (SqlException ex) when (IsMissingTable(ex))
        {
            if (receiptId is { Length: > 0 }) throw new ImportFailure("purchase_not_found");
            return new([], 0, new JsonObject { ["invoicesTotal"] = 0, ["totalAmount"] = 0, ["message"] = NoSourceYet }, DateTimeOffset.UtcNow);
        }
    }

    static (long Total, decimal TotalAmount) PurchaseSummaryOnly(DbConnection source, DateOnly from, DateOnly to, string search, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        cmd.CommandText = BuildPurchaseInvoicesSql(cmd, from, to, 0, 1, search, null);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return (0L, 0m);
        return (Long(reader, "TotalCount"), Decimal(reader, "TotalAmountSum"));
    }

    static JsonArray PurchaseLines(DbConnection source, string receiptId, CancellationToken ct)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandTimeout = CommandTimeoutSeconds;
        using var cancel = SyncCancellation.Register(cmd, ct);
        Add(cmd, "@id", receiptId);
        cmd.CommandText = """
            SELECT l.[Id], l.[PurchaseUnit], l.[PackageCount], l.[ReceivedBaseQuantity],
                   l.[LineSubtotal], l.[TaxAmount], l.[InventoryValuationAmount], l.[LotNumber]
            FROM [maintenance].[PurchaseReceiptLines] l
            WHERE l.[PurchaseReceiptId]=@id
            ORDER BY l.[Id]
            """;
        return new JsonArray(ReadMapped(cmd, row => new JsonObject
        {
            ["id"] = Str(row, "Id"),
            ["purchaseUnit"] = Str(row, "PurchaseUnit"),
            ["packageCount"] = Decimal(row, "PackageCount"),
            ["receivedBaseQuantity"] = Decimal(row, "ReceivedBaseQuantity"),
            ["lineSubtotal"] = Decimal(row, "LineSubtotal"),
            ["taxAmount"] = Decimal(row, "TaxAmount"),
            ["inventoryValuationAmount"] = Decimal(row, "InventoryValuationAmount"),
            ["lotNumber"] = Str(row, "LotNumber")
        }, ct).Select(x => (JsonNode?)x).ToArray());
    }

    /// <summary>
    /// Period overlap + Omar billing-day rule clipped to from/to (Riyadh, after 14:00 bumps boundary).
    /// Spare parts = MaintenanceMaterial from MaintenanceMaterialUsage only; net out reversed originals.
    /// Oil = OilChangeOperations in the period; mileage = Start/EndOdometer else VehicleOdometerReadings.
    /// </summary>
    static string PeriodReportVehicleSql(string scopeSql, bool search = false)
    {
        string searchPred = search
            ? " AND (v.[PlateNumberAr] LIKE @q OR v.[PlateNumberEn] LIKE @q OR v.[AssetNumber] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q OR rr.[Name] LIKE @q OR rr.[IqamaNo] LIKE @q)"
            : "";
        return $"""
            WITH asg AS (
                SELECT a.[Id] AS AssignmentId, a.[VehicleId], a.[RiderProfileId], a.[IsRealRider],
                       a.[StartedAtUtc], a.[EndedAtUtc], a.[StartOdometer], a.[EndOdometer],
                       v.[AssetNumber], v.[PlateNumberAr], v.[VehicleType], v.[OperatingCityId],
                       s.[RegistryNameAr] AS SponsorNameAr, s.[UnifiedNationalNumber],
                       gc.[NameAr] AS CityNameAr,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[FullNameAr] ELSE rr.[Name] END AS RiderNameAr,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[IqamaNo] ELSE rr.[IqamaNo] END AS IqamaNo
                FROM [app].[RiderVehicleAssignments] a
                INNER JOIN [app].[Vehicles] v ON v.[Id]=a.[VehicleId] AND v.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=v.[OperatingCityId] AND oc.[IsDeleted]=0
                LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
                LEFT JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=rp.[EmployeeId] AND e.[IsDeleted]=0
                LEFT JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=a.[Id]
                WHERE a.[IsDeleted]=0{scopeSql}{searchPred}
                  AND a.[StartedAtUtc] < DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00'))
                  AND (a.[EndedAtUtc] IS NULL OR a.[EndedAtUtc] > CONVERT(datetimeoffset, @from + 'T00:00:00+03:00'))
            ),
            calc AS (
                SELECT a.*,
                       {DaysInPeriodSql("a.[StartedAtUtc]", "ISNULL(a.[EndedAtUtc], @asOf)")} AS DaysInPeriod,
                       CASE a.[VehicleType]
                         WHEN 2 THEN CAST({MonthlyCostCarSar.ToString(CultureInfo.InvariantCulture)} AS decimal(18,4)) / 30.0
                         WHEN 1 THEN CAST({MonthlyCostMotorcycleSar.ToString(CultureInfo.InvariantCulture)} AS decimal(18,4)) / 30.0
                         ELSE NULL
                       END AS DailyCost,
                       {AssignmentMileageSql("a")} AS AssignmentMileage,
                       ROW_NUMBER() OVER (PARTITION BY a.VehicleId ORDER BY a.StartedAtUtc DESC) AS LatestRn
                FROM asg a
            ),
            priced AS (
                SELECT c.*,
                       CASE WHEN c.DailyCost IS NULL THEN NULL
                            ELSE ROUND(c.DaysInPeriod * c.DailyCost, 2) END AS AssignmentCost
                FROM calc c
            ),
            by_vehicle AS (
                SELECT
                    VehicleId AS Id,
                    MAX(AssetNumber) AS AssetNumber,
                    MAX(PlateNumberAr) AS PlateNumberAr,
                    MAX(VehicleType) AS VehicleType,
                    MAX(CityNameAr) AS CityNameAr,
                    MAX(SponsorNameAr) AS SponsorNameAr,
                    MAX(CASE WHEN LatestRn=1 THEN RiderNameAr END) AS RiderNameAr,
                    MAX(CASE WHEN LatestRn=1 THEN IqamaNo END) AS IqamaNo,
                    SUM(DaysInPeriod) AS OperatingDays,
                    CASE WHEN COUNT_BIG(CASE WHEN DailyCost IS NULL THEN 1 END)=0
                         THEN SUM(AssignmentCost) ELSE NULL END AS AmountToCollect,
                    {SparePartsByVehicleSql("p.VehicleId")} AS SparePartsCost,
                    (SELECT COUNT_BIG(*) FROM [maintenance].[OilChangeOperations] o
                     WHERE o.[VehicleId]=p.VehicleId AND ISNULL(o.[IsCancelled],0)=0
                       AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) >= CONVERT(date, @from)
                       AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) <= CONVERT(date, @to)) AS OilChangesCount,
                    (SELECT ISNULL(SUM(o.[TotalCost]),0) FROM [maintenance].[OilChangeOperations] o
                     WHERE o.[VehicleId]=p.VehicleId AND ISNULL(o.[IsCancelled],0)=0
                       AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) >= CONVERT(date, @from)
                       AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) <= CONVERT(date, @to)) AS OilChangesCost,
                    CASE WHEN COUNT_BIG(CASE WHEN AssignmentMileage IS NOT NULL THEN 1 END)=0 THEN NULL
                         ELSE SUM(AssignmentMileage) END AS OilMileage
                FROM priced p
                GROUP BY VehicleId
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       COUNT_BIG(*) AS VehiclesOperating,
                       ISNULL(SUM(OperatingDays),0) AS TotalOperatingDays,
                       ISNULL(SUM(SparePartsCost),0) AS SparePartsCost,
                       ISNULL(SUM(AmountToCollect),0) AS AmountToCollect,
                       ISNULL(SUM(OilChangesCost),0) AS OilChangesCost
                FROM by_vehicle
            )
            SELECT b.*, a.TotalCount, a.VehiclesOperating, a.TotalOperatingDays, a.SparePartsCost AS SparePartsCostAgg,
                   a.AmountToCollect AS AmountToCollectAgg, a.OilChangesCost AS OilChangesCostAgg
            FROM by_vehicle b CROSS JOIN agg a
            ORDER BY b.PlateNumberAr, b.AssetNumber
            """;
    }

    static string PeriodReportRiderSql(string scopeSql, bool search = false)
    {
        string searchPred = search
            ? " AND (v.[PlateNumberAr] LIKE @q OR e.[FullNameAr] LIKE @q OR e.[IqamaNo] LIKE @q OR rr.[Name] LIKE @q OR rr.[IqamaNo] LIKE @q)"
            : "";
        return $"""
            WITH asg AS (
                SELECT a.[Id] AS AssignmentId, a.[VehicleId], a.[RiderProfileId], a.[IsRealRider],
                       a.[StartedAtUtc], a.[EndedAtUtc], a.[StartOdometer], a.[EndOdometer],
                       v.[AssetNumber], v.[PlateNumberAr], v.[VehicleType],
                       gc.[NameAr] AS CityNameAr,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[FullNameAr] ELSE rr.[Name] END AS RiderNameAr,
                       CASE WHEN a.[IsRealRider]=1 THEN e.[IqamaNo] ELSE rr.[IqamaNo] END AS IqamaNo,
                       CASE
                         WHEN a.[IsRealRider]=1 AND e.[IqamaNo] IS NOT NULL AND e.[IqamaNo]<>'' THEN N'iqama:'+e.[IqamaNo]
                         WHEN a.[IsRealRider]=1 THEN N'profile:'+CONVERT(nvarchar(36), a.[RiderProfileId])
                         WHEN rr.[IqamaNo] IS NOT NULL AND rr.[IqamaNo]<>'' THEN N'iqama:'+rr.[IqamaNo]
                         WHEN rr.[Id] IS NOT NULL THEN N'real:'+CONVERT(nvarchar(36), rr.[Id])
                         ELSE N'unidentified:'+CONVERT(nvarchar(36), a.[Id])
                       END AS RiderKey
                FROM [app].[RiderVehicleAssignments] a
                INNER JOIN [app].[Vehicles] v ON v.[Id]=a.[VehicleId] AND v.[IsDeleted]=0
                LEFT JOIN [app].[Sponsors] s ON s.[Id]=v.[SponsorId] AND s.[IsDeleted]=0
                LEFT JOIN [app].[OperatingCities] oc ON oc.[Id]=v.[OperatingCityId] AND oc.[IsDeleted]=0
                LEFT JOIN [platform].[GlobalCities] gc ON gc.[Id]=oc.[GlobalCityId] AND gc.[IsDeleted]=0
                LEFT JOIN [app].[RiderProfiles] rp ON rp.[Id]=a.[RiderProfileId] AND rp.[IsDeleted]=0
                LEFT JOIN [app].[Employees] e ON e.[Id]=rp.[EmployeeId] AND e.[IsDeleted]=0
                LEFT JOIN [app].[RealRiders] rr ON rr.[RiderVehicleAssignmentId]=a.[Id]
                WHERE a.[IsDeleted]=0{scopeSql}{searchPred}
                  AND a.[StartedAtUtc] < DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00'))
                  AND (a.[EndedAtUtc] IS NULL OR a.[EndedAtUtc] > CONVERT(datetimeoffset, @from + 'T00:00:00+03:00'))
            ),
            calc AS (
                SELECT a.*,
                       {DaysInPeriodSql("a.[StartedAtUtc]", "ISNULL(a.[EndedAtUtc], @asOf)")} AS DaysInPeriod,
                       CASE a.[VehicleType]
                         WHEN 2 THEN CAST({MonthlyCostCarSar.ToString(CultureInfo.InvariantCulture)} AS decimal(18,4)) / 30.0
                         WHEN 1 THEN CAST({MonthlyCostMotorcycleSar.ToString(CultureInfo.InvariantCulture)} AS decimal(18,4)) / 30.0
                         ELSE NULL
                       END AS DailyCost,
                       {AssignmentMileageSql("a")} AS AssignmentMileage
                FROM asg a
            ),
            priced AS (
                SELECT c.*,
                       CASE WHEN c.DailyCost IS NULL THEN NULL
                            ELSE ROUND(c.DaysInPeriod * c.DailyCost, 2) END AS AssignmentCost,
                       {SparePartsByAssignmentSql("c.AssignmentId")} AS SparePartsCost,
                       (SELECT COUNT_BIG(*) FROM [maintenance].[OilChangeOperations] o
                        WHERE o.[VehicleId]=c.VehicleId AND ISNULL(o.[IsCancelled],0)=0
                          AND o.[PerformedAtUtc] >= c.StartedAtUtc
                          AND o.[PerformedAtUtc] < ISNULL(c.EndedAtUtc, DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00')))
                          AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) >= CONVERT(date, @from)
                          AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) <= CONVERT(date, @to)) AS OilChangesCount,
                       (SELECT ISNULL(SUM(o.[TotalCost]),0) FROM [maintenance].[OilChangeOperations] o
                        WHERE o.[VehicleId]=c.VehicleId AND ISNULL(o.[IsCancelled],0)=0
                          AND o.[PerformedAtUtc] >= c.StartedAtUtc
                          AND o.[PerformedAtUtc] < ISNULL(c.EndedAtUtc, DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00')))
                          AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) >= CONVERT(date, @from)
                          AND CONVERT(date, SWITCHOFFSET(o.[PerformedAtUtc], '+03:00')) <= CONVERT(date, @to)) AS OilChangesCost
                FROM calc c
            ),
            by_rider AS (
                SELECT
                    RiderKey AS Id,
                    MAX(RiderNameAr) AS RiderNameAr,
                    MAX(IqamaNo) AS IqamaNo,
                    MAX(PlateNumberAr) AS PlateNumberAr,
                    MAX(AssetNumber) AS AssetNumber,
                    MAX(VehicleType) AS VehicleType,
                    MAX(CityNameAr) AS CityNameAr,
                    MAX(CONVERT(nvarchar(36), VehicleId)) AS VehicleId,
                    SUM(DaysInPeriod) AS OperatingDays,
                    CASE WHEN COUNT_BIG(CASE WHEN DailyCost IS NULL THEN 1 END)=0
                         THEN SUM(AssignmentCost) ELSE NULL END AS AmountToCollect,
                    SUM(SparePartsCost) AS SparePartsCost,
                    SUM(OilChangesCount) AS OilChangesCount,
                    SUM(OilChangesCost) AS OilChangesCost,
                    CASE WHEN COUNT_BIG(CASE WHEN AssignmentMileage IS NOT NULL THEN 1 END)=0 THEN NULL
                         ELSE SUM(AssignmentMileage) END AS OilMileage
                FROM priced
                GROUP BY RiderKey
            ),
            agg AS (
                SELECT COUNT_BIG(*) AS TotalCount,
                       COUNT_BIG(DISTINCT VehicleId) AS VehiclesOperating,
                       ISNULL(SUM(OperatingDays),0) AS TotalOperatingDays,
                       ISNULL(SUM(SparePartsCost),0) AS SparePartsCost,
                       ISNULL(SUM(AmountToCollect),0) AS AmountToCollect,
                       ISNULL(SUM(OilChangesCost),0) AS OilChangesCost
                FROM by_rider
            )
            SELECT b.*, a.TotalCount, a.VehiclesOperating, a.TotalOperatingDays,
                   a.SparePartsCost AS SparePartsCostAgg, a.AmountToCollect AS AmountToCollectAgg, a.OilChangesCost AS OilChangesCostAgg
            FROM by_rider b CROSS JOIN agg a
            ORDER BY b.RiderNameAr, b.IqamaNo
            """;
    }

    /// <summary>Omar Days(): billing boundaries clipped to from/to DayNumber range.</summary>
    static string DaysInPeriodSql(string startExpr, string endExpr) => $"""
        (
          SELECT CAST(CASE WHEN endDay < startDay THEN 0 ELSE endDay - startDay END AS decimal(18,2))
          FROM (
            SELECT
              CASE WHEN rawStart < fromDay THEN fromDay ELSE rawStart END AS startDay,
              CASE WHEN rawEnd > toDayEx THEN toDayEx ELSE rawEnd END AS endDay
            FROM (
              SELECT
                CASE WHEN DATEPART(HOUR, SWITCHOFFSET({startExpr}, '+03:00')) * 60 + DATEPART(MINUTE, SWITCHOFFSET({startExpr}, '+03:00')) > 14*60
                     THEN DATEDIFF(DAY, '0001-01-01', CONVERT(date, SWITCHOFFSET({startExpr}, '+03:00'))) + 1
                     ELSE DATEDIFF(DAY, '0001-01-01', CONVERT(date, SWITCHOFFSET({startExpr}, '+03:00'))) END AS rawStart,
                CASE WHEN DATEPART(HOUR, SWITCHOFFSET({endExpr}, '+03:00')) * 60 + DATEPART(MINUTE, SWITCHOFFSET({endExpr}, '+03:00')) > 14*60
                     THEN DATEDIFF(DAY, '0001-01-01', CONVERT(date, SWITCHOFFSET({endExpr}, '+03:00'))) + 1
                     ELSE DATEDIFF(DAY, '0001-01-01', CONVERT(date, SWITCHOFFSET({endExpr}, '+03:00'))) END AS rawEnd,
                DATEDIFF(DAY, '0001-01-01', CONVERT(date, @from)) AS fromDay,
                DATEDIFF(DAY, '0001-01-01', CONVERT(date, @to)) + 1 AS toDayEx
            ) x
          ) d
        )
        """;

    /// <summary>M2: material usages only; exclude originals that have a reversal row.</summary>
    static string SparePartsByVehicleSql(string vehicleIdExpr) => $"""
        (SELECT ISNULL(SUM(ve.[TotalAmount]),0) FROM [maintenance].[VehicleExpenses] ve
         WHERE ve.[VehicleId]={vehicleIdExpr}
           AND ve.[ExpenseType]=N'MaintenanceMaterial'
           AND ve.[SourceEntityType]=N'MaintenanceMaterialUsage'
           AND ve.[OccurredOn] >= CONVERT(date, @from) AND ve.[OccurredOn] <= CONVERT(date, @to)
           AND ve.[ReversalOfExpenseId] IS NULL
           AND NOT EXISTS (SELECT 1 FROM [maintenance].[VehicleExpenses] rev WHERE rev.[ReversalOfExpenseId]=ve.[Id]))
        """;

    static string SparePartsByAssignmentSql(string assignmentIdExpr) => $"""
        (SELECT ISNULL(SUM(ve.[TotalAmount]),0) FROM [maintenance].[VehicleExpenses] ve
         WHERE ve.[RiderVehicleAssignmentId]={assignmentIdExpr}
           AND ve.[ExpenseType]=N'MaintenanceMaterial'
           AND ve.[SourceEntityType]=N'MaintenanceMaterialUsage'
           AND ve.[OccurredOn] >= CONVERT(date, @from) AND ve.[OccurredOn] <= CONVERT(date, @to)
           AND ve.[ReversalOfExpenseId] IS NULL
           AND NOT EXISTS (SELECT 1 FROM [maintenance].[VehicleExpenses] rev WHERE rev.[ReversalOfExpenseId]=ve.[Id]))
        """;

    /// <summary>M4: StartOdometer/EndOdometer else readings in assignment window; null if unknown.</summary>
    static string AssignmentMileageSql(string alias) => $"""
        (
          CASE
            WHEN {alias}.[StartOdometer] IS NOT NULL AND {alias}.[EndOdometer] IS NOT NULL
              THEN CAST({alias}.[EndOdometer] - {alias}.[StartOdometer] AS decimal(18,2))
            WHEN {alias}.[StartOdometer] IS NOT NULL AND (
                  SELECT MAX(r.[Reading]) FROM [app].[VehicleOdometerReadings] r
                  WHERE r.[VehicleId]={alias}.[VehicleId] AND ISNULL(r.[IsVoided],0)=0
                    AND r.[RecordedAtUtc] >= {alias}.[StartedAtUtc]
                    AND r.[RecordedAtUtc] <= ISNULL({alias}.[EndedAtUtc], @asOf)
                ) IS NOT NULL
              THEN CAST((
                  SELECT MAX(r.[Reading]) FROM [app].[VehicleOdometerReadings] r
                  WHERE r.[VehicleId]={alias}.[VehicleId] AND ISNULL(r.[IsVoided],0)=0
                    AND r.[RecordedAtUtc] >= {alias}.[StartedAtUtc]
                    AND r.[RecordedAtUtc] <= ISNULL({alias}.[EndedAtUtc], @asOf)
                ) - {alias}.[StartOdometer] AS decimal(18,2))
            ELSE (
              SELECT CASE WHEN MIN(r.[Reading]) IS NULL OR MAX(r.[Reading]) IS NULL THEN NULL
                          ELSE CAST(MAX(r.[Reading]) - MIN(r.[Reading]) AS decimal(18,2)) END
              FROM [app].[VehicleOdometerReadings] r
              WHERE r.[VehicleId]={alias}.[VehicleId] AND ISNULL(r.[IsVoided],0)=0
                AND r.[RecordedAtUtc] >= CASE WHEN {alias}.[StartedAtUtc] > CONVERT(datetimeoffset, @from + 'T00:00:00+03:00')
                                              THEN {alias}.[StartedAtUtc]
                                              ELSE CONVERT(datetimeoffset, @from + 'T00:00:00+03:00') END
                AND r.[RecordedAtUtc] <= CASE WHEN {alias}.[EndedAtUtc] IS NOT NULL
                                               AND {alias}.[EndedAtUtc] < DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00'))
                                              THEN {alias}.[EndedAtUtc]
                                              ELSE DATEADD(day, 1, CONVERT(datetimeoffset, @to + 'T00:00:00+03:00')) END
            )
          END
        )
        """;

    static JsonObject MapPeriodVehicleRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["assetNumber"] = Str(r, "AssetNumber"),
        ["plateNumberAr"] = Str(r, "PlateNumberAr"),
        ["vehicleType"] = VehicleTypeName(Str(r, "VehicleType")),
        ["city"] = Str(r, "CityNameAr"),
        ["riderNameAr"] = Str(r, "RiderNameAr"),
        ["iqama"] = Str(r, "IqamaNo"),
        ["operatingDays"] = Decimal(r, "OperatingDays"),
        ["sparePartsCost"] = Decimal(r, "SparePartsCost"),
        ["amountToCollect"] = NullDecimal(r, "AmountToCollect"),
        ["oilChangesCount"] = Long(r, "OilChangesCount"),
        ["oilChangesCost"] = Decimal(r, "OilChangesCost"),
        ["oilMileage"] = NullDecimal(r, "OilMileage")
    };

    static JsonObject MapPeriodRiderRow(IDataRecord r) => new()
    {
        ["id"] = Str(r, "Id"),
        ["riderNameAr"] = Str(r, "RiderNameAr"),
        ["iqama"] = Str(r, "IqamaNo"),
        ["plateNumberAr"] = Str(r, "PlateNumberAr"),
        ["assetNumber"] = Str(r, "AssetNumber"),
        ["vehicleType"] = VehicleTypeName(Str(r, "VehicleType")),
        ["vehicleId"] = Str(r, "VehicleId"),
        ["city"] = Str(r, "CityNameAr"),
        ["operatingDays"] = Decimal(r, "OperatingDays"),
        ["sparePartsCost"] = Decimal(r, "SparePartsCost"),
        ["amountToCollect"] = NullDecimal(r, "AmountToCollect"),
        ["oilChangesCount"] = Long(r, "OilChangesCount"),
        ["oilChangesCost"] = Decimal(r, "OilChangesCost"),
        ["oilMileage"] = NullDecimal(r, "OilMileage")
    };

    static JsonObject EmptyVehicleSummary() => new()
    {
        ["vehiclesOperating"] = 0L,
        ["totalOperatingDays"] = 0m,
        ["sparePartsCost"] = 0m,
        ["amountToCollect"] = 0m,
        ["oilChangesCost"] = 0m
    };
}
