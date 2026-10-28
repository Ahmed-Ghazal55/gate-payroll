using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace Payroll;

public static class ReleaseSafety
{
    public const string Version="0.8.0";
    public static object Capture(DbConnection c,DbTransaction? tx=null)
    {
        using var cmd=Store.Command(c,tx,"SELECT id,kind,body FROM records ORDER BY id");using var reader=cmd.ExecuteReader();
        var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while(reader.Read())
        {
            string kind=reader.GetString(1);counts[kind]=counts.GetValueOrDefault(kind)+1;
            for(int i=0;i<3;i++){byte[] value=Encoding.UTF8.GetBytes(reader.GetString(i));digest.AppendData(Encoding.UTF8.GetBytes(value.Length+":"));digest.AppendData(value);}
        }
        return new{counts,recordsSha256=Convert.ToHexString(digest.GetHashAndReset())};
    }
    // This checkpoint stays under private App_Data and never enters a publish artifact.
    public static string? Checkpoint(DbConnection c,string dataDirectory)
    {
        using(var exists=c.CreateCommand())
        {
            exists.CommandText=c is SqliteConnection?"SELECT count(*) FROM sqlite_master WHERE type='table' AND name='records'":"SELECT CASE WHEN OBJECT_ID(N'dbo.records',N'U') IS NULL THEN 0 ELSE 1 END";
            if(Convert.ToInt32(exists.ExecuteScalar())==0)return null;
        }
        string directory=Path.Combine(dataDirectory,"release-backups",Version);Directory.CreateDirectory(directory);
        string manifest=Path.Combine(directory,"checkpoint.json");if(File.Exists(manifest))return JsonSerializer.Serialize(Capture(c));
        string snapshot=Path.Combine(directory,"records-"+Guid.NewGuid().ToString("N"));object before;
        if(c is SqliteConnection sqlite)
        {
            snapshot+=".db";
            using var target=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=snapshot,Pooling=false}.ToString());target.Open();sqlite.BackupDatabase(target);
            using var integrity=target.CreateCommand();integrity.CommandText="PRAGMA integrity_check";
            if((string?)integrity.ExecuteScalar()!="ok")throw new InvalidOperationException("Release backup integrity check failed.");
            before=Capture(target);
        }
        else
        {
            snapshot+=".json";using var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable);
            before=Capture(c,tx);
            using(var file=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new Utf8JsonWriter(file))
            {
                writer.WriteStartArray();using var cmd=Store.Command(c,tx,"SELECT id,kind,body FROM records ORDER BY id");using var rows=cmd.ExecuteReader();
                while(rows.Read()){writer.WriteStartObject();writer.WriteString("id",rows.GetString(0));writer.WriteString("kind",rows.GetString(1));writer.WriteString("body",rows.GetString(2));writer.WriteEndObject();}writer.WriteEndArray();
            }
            tx.Commit();
        }
        string keys=Path.Combine(dataDirectory,"keys");if(Directory.Exists(keys)){string target=Path.Combine(directory,"keys");Directory.CreateDirectory(target);foreach(string key in Directory.GetFiles(keys,"*.xml"))File.Copy(key,Path.Combine(target,Path.GetFileName(key)),false);}
        string local=Path.Combine(AppContext.BaseDirectory,"appsettings.Local.json");if(File.Exists(local))File.Copy(local,Path.Combine(directory,"appsettings.Local.json"),false);
        var evidence=new{version=Version,createdAt=DateTimeOffset.UtcNow.ToString("O"),snapshot=Path.GetFileName(snapshot),before};
        string temp=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(evidence));
        try{File.Move(temp,manifest,false);}catch(IOException)when(File.Exists(manifest)){File.Delete(temp);}
        return JsonSerializer.Serialize(before);
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/system/release-check",()=>store.Read(c=>
        {
            string integrity="not_checked";if(c is SqliteConnection){using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA quick_check";integrity=(string?)cmd.ExecuteScalar()??"unknown";}
            var campaigns=Store.List(c,"campaign").Select(x=>x.S("id")).ToHashSet();
            var riders=Store.List(c,"rider").Select(x=>x.S("iqama")).ToHashSet();
            string checkpoint=Path.Combine(store.DirectoryPath,"release-backups",Version,"checkpoint.json");
            return new{version=Version,provider=store.Provider,integrity,snapshot=Capture(c),checkpoint=File.Exists(checkpoint)?JsonDocument.Parse(File.ReadAllText(checkpoint)).RootElement.Clone():default(JsonElement?),orphanCampaignResponses=Store.List(c,"submission").Count(x=>!campaigns.Contains(x.S("campaignId"))),orphanDebts=Store.List(c,"debt").Count(x=>!riders.Contains(x.S("iqama")))};
        }));
    }
}
