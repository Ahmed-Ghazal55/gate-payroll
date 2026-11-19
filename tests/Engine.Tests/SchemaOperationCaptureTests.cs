using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaOperationCaptureTests
{
    static SqliteConnection Open()
    {
        var c=MoneySchemaFixture.Payments();SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);return c;
    }
    static void Empty(SqliteConnection c,string run,string kind,string? reason="No synthetic inputs",int status=2)
    {
        SchemaSql.Exec(c,null,"INSERT INTO fin_operation_capture_manifest(id,operation_type,operation_id,required_kind,status,empty_reason,period_month) VALUES(@id,'pay_run',@run,@kind,@status,@reason,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@run",run),("@kind",kind),("@status",status),("@reason",(object?)reason??DBNull.Value));
    }
    static void FillEmpty(SqliteConnection c,string run,string app="ninja",string? except=null)
    {
        using var cmd=Store.Command(c,null,"SELECT required_kind FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@app",("@app",app));
        using var reader=cmd.ExecuteReader();var kinds=new List<string>();while(reader.Read())kinds.Add(reader.GetString(0));reader.Close();
        foreach(string kind in kinds.Where(k=>k!=except))Empty(c,run,kind);
    }
    static string Snapshot(SqliteConnection c,string company,string kind="ninja_daily",string app="ninja",string month="2026-11-01",string? erpRow=null,byte[]? fingerprint=null)
    {
        string id=MoneySchemaFixture.Id();
        SchemaSql.Exec(c,null,"INSERT INTO sal_ops_snapshot(id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,payload) VALUES(@id,@month,@kind,@erp,@company,@app,datetime('now'),@by,@hash,'{}')",("@id",id),("@month",month),("@kind",kind),("@erp",erpRow??MoneySchemaFixture.Id()),("@company",company),("@app",app),("@by",MoneySchemaFixture.Id()),("@hash",fingerprint??new byte[32]));return id;
    }
    static string Link(SqliteConnection c,string run,string snapshot,string? kind=null,string month="2026-11-01")
    {
        string id=MoneySchemaFixture.Id();SchemaSql.Exec(c,null,"INSERT INTO fin_operation_snapshot_link(id,operation_type,operation_id,snapshot_id,snapshot_kind,erp_row_id,source_fingerprint,period_month) SELECT @id,'pay_run',@run,id,COALESCE(@kind,kind),erp_row_id,source_fingerprint,@month FROM sal_ops_snapshot WHERE id=@snap",("@id",id),("@run",run),("@kind",(object?)kind??DBNull.Value),("@snap",snapshot),("@month",month));return id;
    }
    static void Captured(SqliteConnection c,string run,string link,string kind="ninja_daily")=>SchemaSql.Exec(c,null,"INSERT INTO fin_operation_capture_manifest(id,operation_type,operation_id,required_kind,status,snapshot_link_id,period_month) VALUES(@id,'pay_run',@run,@kind,1,@link,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@run",run),("@kind",kind),("@link",link));
    static void Freeze(SqliteConnection c,string run)=>SchemaSql.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1,stage=2,calc_started_at=datetime('now') WHERE id=@run",("@run",run));

    [TestMethod]
    public void CatalogueIsExhaustiveAndIdempotentWithGuidV7Keys()
    {
        using var c=Open();Assert.AreEqual(40L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind"));
        SchemaOperationCapture.Ensure(c);Assert.AreEqual(40L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind"));
        foreach(var (app,count) in new[]{("ninja",8L),("keeta",8L),("hs",9L),("amazon",8L)})
            Assert.AreEqual(count,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@app",("@app",app)));
        Assert.AreEqual(0L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code='chefz'"));
        using var cmd=Store.Command(c,null,"SELECT id FROM fin_operation_required_kind");using var reader=cmd.ExecuteReader();
        while(reader.Read())Assert.AreEqual(7,Guid.Parse(reader.GetString(0)).Version);
    }

    [TestMethod]
    public void MissingKindBlockerBlankReasonAndUnknownAppCannotFreeze()
    {
        using var c=Open();string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,run));
        FillEmpty(c,run,except:"housing");Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,run));
        SchemaConstraintAssert.ThrowsNamed(()=>Empty(c,run,"housing"," "),"CK_manifest_empty");
        Empty(c,run,"housing",null,status:3);Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,run));
        SchemaSql.Exec(c,null,"UPDATE fin_operation_capture_manifest SET status=2,empty_reason='No housing in synthetic scope' WHERE operation_id=@run AND required_kind='housing'",("@run",run));
        Freeze(c,run);
        Assert.AreEqual(1L,SchemaSql.Scalar(c,null,"SELECT capture_state FROM sal_pay_run WHERE id=@id",("@id",run)));
        string unknown=MoneySchemaFixture.Run(c,company,"other");Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,unknown));
    }

    [TestMethod]
    public void TypedLinksRejectWrongKindOperationPeriodAndDuplicateContent()
    {
        using var c=Open();string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),other=MoneySchemaFixture.Run(c,company,"keeta");
        string erp=MoneySchemaFixture.Id(),snap=Snapshot(c,company,erpRow:erp);
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,snap,kind:"fuel"),"FK_op_snap_snapshot",sqliteToken:"FOREIGN KEY");
        string link=Link(c,run,snap);
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,snap,month:"2026-12-01"),"FK_op_snap_snapshot",sqliteToken:"Capture owner or period mismatch");
        SchemaConstraintAssert.ThrowsNamed(()=>Captured(c,other,link),"FK_manifest_link",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>Captured(c,run,link,kind:"fuel"),"FK_manifest_link",sqliteToken:"FOREIGN KEY");
        string duplicate=Snapshot(c,company,erpRow:erp);
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,duplicate),"UQ_op_snap_content",sqliteToken:"fin_operation_snapshot_link.operation_type, fin_operation_snapshot_link.operation_id, fin_operation_snapshot_link.snapshot_kind");
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"UPDATE sal_ops_snapshot SET payload='{\"changed\":true}' WHERE id=@id",("@id",snap)));
    }

    [TestMethod]
    public void FrozenEvidenceCannotBeEditedReplacedDeletedOrMovedAndSealIsMonotonic()
    {
        using var c=Open();string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),snap=Snapshot(c,company),link=Link(c,run,snap);
        Captured(c,run,link);FillEmpty(c,run,except:"ninja_daily");Freeze(c,run);
        foreach(string sql in new[]{
            "UPDATE fin_operation_capture_manifest SET empty_reason='changed' WHERE operation_id=@id",
            "DELETE FROM fin_operation_capture_manifest WHERE operation_id=@id",
            "UPDATE fin_operation_snapshot_link SET operation_type='payment' WHERE operation_id=@id",
            "DELETE FROM fin_operation_snapshot_link WHERE operation_id=@id",
            "UPDATE sal_pay_run SET capture_state=0,stage=1,calc_started_at=NULL WHERE id=@id"})
            Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,sql,("@id",run)),sql);
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"DELETE FROM sal_ops_snapshot WHERE id=@id",("@id",snap)));
        SchemaSql.Exec(c,null,"PRAGMA recursive_triggers=OFF");
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"INSERT OR REPLACE INTO sal_ops_snapshot SELECT id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,'{\"changed\":true}',generation FROM sal_ops_snapshot WHERE id=@id",("@id",snap)));
        Assert.ThrowsExactly<SqliteException>(()=>Link(c,run,Snapshot(c,company,"fuel")));
        SchemaSql.Exec(c,null,"UPDATE sal_pay_run SET capture_state=2 WHERE id=@id",("@id",run));
        Assert.ThrowsExactly<SqliteException>(()=>SchemaSql.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1 WHERE id=@id",("@id",run)));
        SchemaOperationCapture.Ensure(c);Assert.AreEqual(8L,SchemaSql.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_capture_manifest WHERE operation_id=@id",("@id",run)));
    }

    [TestMethod]
    public void SnapshotFromAnotherCompanyOrAppCannotSatisfyCapture()
    {
        using var c=Open();string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        string link=Link(c,run,Snapshot(c,other));Captured(c,run,link);FillEmpty(c,run,except:"ninja_daily");
        Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,run));
        SchemaSql.Exec(c,null,"DELETE FROM fin_operation_capture_manifest WHERE operation_id=@id AND required_kind='ninja_daily'",("@id",run));
        Captured(c,run,Link(c,run,Snapshot(c,company,app:"keeta")));
        Assert.ThrowsExactly<SqliteException>(()=>Freeze(c,run));
    }
    [TestMethod]
    public void StartupCreatesTypedSnapshotManifestAndRequiredKindCatalogue()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-capture-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(dir);
            store.Read(c=>{Assert.IsTrue(SchemaSql.TableExists(c,null,"sal","ops_snapshot"));foreach(string t in new[]{"operation_snapshot_link","operation_capture_manifest","operation_required_kind"})Assert.IsTrue(SchemaSql.TableExists(c,null,"fin",t),t);return true;});
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
