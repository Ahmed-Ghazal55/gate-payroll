using System.Data.Common;
namespace ErpImport;

public sealed record SourceWatermark(string TableName,string ColumnKind,string LastValue,string RunId,DateTimeOffset UpdatedAtUtc);
