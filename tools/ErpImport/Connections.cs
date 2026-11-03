using Microsoft.Data.SqlClient;
namespace ErpImport;

public static class Connections
{
    public static string Env(string path,string key)
    {
        // Values never enter command output, diagnostics or a persisted import plan.
        var lines=File.ReadLines(Path.GetFullPath(path.Replace("~/",Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"/")))
            .Where(l=>l.TrimStart().StartsWith(key+"=",StringComparison.Ordinal)).ToArray();
        if(lines.Length!=1)throw new ImportFailure("env_key_missing_or_duplicate");
        string value=lines[0].Trim().Split('=',2)[1].Trim();
        if(value.Length>=2&&value[0] is '\'' or '"'&&value[^1]==value[0])value=value[1..^1];
        if(string.IsNullOrWhiteSpace(value))throw new ImportFailure("env_key_empty");return value;
    }
    public static SqlConnection Source(string connection)
    {
        var b=new SqlConnectionStringBuilder(connection){ConnectTimeout=20,ApplicationName="GateERP.ReadOnlyImport"};
        bool local=b.InitialCatalog=="erp_copy"&&System.Text.RegularExpressions.Regex.IsMatch(b.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool live=b.InitialCatalog=="db67927"&&b.UserID=="db67927_readonly";
        if(!local&&!live)throw new ImportFailure("unapproved_source");
        b.ApplicationIntent=ApplicationIntent.ReadOnly;b.Encrypt=true;return new SqlConnection(b.ConnectionString);
    }
    public static SqlConnection Target(string envPath)
    {
        var b=new SqlConnectionStringBuilder(Env(envPath,"STAGING_DB")){ConnectTimeout=20,ApplicationName="GateERP.StagingImport"};
        // Ghazal approved this exact catalog; never accept a production/other target.
        if(b.InitialCatalog!="db77738")throw new ImportFailure("unapproved_target");
        b.Encrypt=true;return new SqlConnection(b.ConnectionString);
    }
}
public sealed class ImportFailure(string code):Exception { public string Code { get; }=code; }
