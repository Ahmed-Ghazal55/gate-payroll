using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaOperationCaptureTests
{
    static DbConnection Open(bool sqlServer)
    {
        var c=MoneySchemaFixture.Payments(sqlServer);SchemaSalInvoice.Ensure(c);SchemaSalPayRun.Ensure(c);SchemaOperationCapture.Ensure(c);return c;
    }
    static void Empty(DbConnection c,string run,string kind,string? reason="No synthetic inputs",int status=2)
    {
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_operation_capture_manifest(id,operation_type,operation_id,required_kind,status,empty_reason,period_month) VALUES(@id,'pay_run',@run,@kind,@status,@reason,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@run",run),("@kind",kind),("@status",status),("@reason",(object?)reason??DBNull.Value));
    }
    static void FillEmpty(DbConnection c,string run,string app="ninja",string? except=null)
    {
        using var cmd=MoneySchemaFixture.Command(c,null,"SELECT required_kind FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@app",("@app",app));
        using var reader=cmd.ExecuteReader();var kinds=new List<string>();while(reader.Read())kinds.Add(reader.GetString(0));reader.Close();
        foreach(string kind in kinds.Where(k=>k!=except))Empty(c,run,kind);
    }
    static string Snapshot(DbConnection c,string company,string kind="ninja_daily",string app="ninja",string month="2026-11-01",string? erpRow=null,byte[]? fingerprint=null)
    {
        string id=MoneySchemaFixture.Id();
        MoneySchemaFixture.Exec(c,null,"INSERT INTO sal_ops_snapshot(id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,payload) VALUES(@id,@month,@kind,@erp,@company,@app,datetime('now'),@by,@hash,'{}')",("@id",id),("@month",month),("@kind",kind),("@erp",erpRow??MoneySchemaFixture.Id()),("@company",company),("@app",app),("@by",MoneySchemaFixture.Id()),("@hash",fingerprint??new byte[32]));return id;
    }
    static string Link(DbConnection c,string run,string snapshot,string? kind=null,string month="2026-11-01")
    {
        string id=MoneySchemaFixture.Id();MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_operation_snapshot_link(id,operation_type,operation_id,snapshot_id,snapshot_kind,erp_row_id,source_fingerprint,period_month) SELECT @id,'pay_run',@run,id,COALESCE(@kind,kind),erp_row_id,source_fingerprint,@month FROM sal_ops_snapshot WHERE id=@snap",("@id",id),("@run",run),("@kind",(object?)kind??DBNull.Value),("@snap",snapshot),("@month",month));return id;
    }
    static void Captured(DbConnection c,string run,string link,string kind="ninja_daily")=>MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_operation_capture_manifest(id,operation_type,operation_id,required_kind,status,snapshot_link_id,period_month) VALUES(@id,'pay_run',@run,@kind,1,@link,'2026-11-01')",("@id",MoneySchemaFixture.Id()),("@run",run),("@kind",kind),("@link",link));
    static void Freeze(DbConnection c,string run)=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1,stage=2,calc_started_at=datetime('now') WHERE id=@run",("@run",run));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FrozenRunAndApprovedInvoiceAndPaymentKeepPayoutFlags(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET payout_blocked=1 WHERE id=@id",("@id",run));
        FillEmpty(c,run);Freeze(c,run);
        string invoice=MoneySchemaFixture.Invoice(c,company),payment=MoneySchemaFixture.Payment(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_platform_invoice SET approved_at=datetime('now'),status=2,payout_blocked=1 WHERE id=@id",("@id",invoice));
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET payout_blocked=1 WHERE id=@id",("@id",payment));
        foreach(var (table,id) in new[]{("sal_pay_run",run),("sal_platform_invoice",invoice),("pay_payment",payment)})
        foreach(string set in new[]{"run_mode=1","payout_blocked=0"})
            MoneySchemaFixture.RejectAny(()=>MoneySchemaFixture.Exec(c,null,$"UPDATE {table} SET {set} WHERE id=@id",("@id",id)),
                "Frozen money operation cannot change run mode or remove payout block","Invalid payment transition or frozen identity/flags");
        string payable=MoneySchemaFixture.Payable(c,company),simulation=MoneySchemaFixture.Payment(c,company);
        MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment SET run_mode=1 WHERE id=@id",("@id",simulation));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,simulation,payable),"Simulation or payout-blocked payment cannot claim");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Claim(c,company,payment,payable,status:2),"Simulation or payout-blocked payment cannot claim");
        string reserved=MoneySchemaFixture.Claim(c,company,payment,payable);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE pay_payment_allocation SET claim_status=2 WHERE id=@id",("@id",reserved)),"Simulation or payout-blocked payment cannot claim");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KindAddedAfterFreezeBlocksOnlyNewFreezes(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),frozen=MoneySchemaFixture.Run(c,company);
        FillEmpty(c,frozen);Freeze(c,frozen);
        MoneySchemaFixture.Exec(c,null,"INSERT INTO fin_operation_required_kind(id,operation_type,app_code,required_kind) VALUES(@id,'pay_run','ninja','late_kind')",("@id",MoneySchemaFixture.Id()));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET stage=3 WHERE id=@id",("@id",frozen));
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=2 WHERE id=@id",("@id",frozen));
        string next=MoneySchemaFixture.Run(c,MoneySchemaFixture.Company(c));
        FillEmpty(c,next,except:"late_kind");MoneySchemaFixture.Reject(()=>Freeze(c,next),"Capture incomplete or snapshot scope mismatch");
        Empty(c,next,"late_kind");Freeze(c,next);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CatalogueIsExhaustiveAndIdempotentWithGuidV7Keys(bool sqlServer)
    {
        using var c=Open(sqlServer);Assert.AreEqual(41L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind"));
        SchemaOperationCapture.Ensure(c);Assert.AreEqual(41L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind"));
        foreach(var (app,count) in new[]{("ninja",9L),("keeta",8L),("hs",9L),("amazon",8L)})
            Assert.AreEqual(count,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code=@app",("@app",app)));
        Assert.AreEqual(0L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_required_kind WHERE operation_type='pay_run' AND app_code='chefz'"));
        using var cmd=MoneySchemaFixture.Command(c,null,"SELECT id FROM fin_operation_required_kind");using var reader=cmd.ExecuteReader();
        while(reader.Read())Assert.AreEqual(7,Guid.Parse(Convert.ToString(reader.GetValue(0))!).Version);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingKindBlockerBlankReasonAndUnknownAppCannotFreeze(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        MoneySchemaFixture.Reject(()=>Freeze(c,run),"Capture incomplete or snapshot scope mismatch");
        FillEmpty(c,run,except:"housing");MoneySchemaFixture.Reject(()=>Freeze(c,run),"Capture incomplete or snapshot scope mismatch");
        SchemaConstraintAssert.ThrowsNamed(()=>Empty(c,run,"housing"," "),"CK_manifest_empty");
        Empty(c,run,"housing",null,status:3);MoneySchemaFixture.Reject(()=>Freeze(c,run),"Capture incomplete or snapshot scope mismatch");
        MoneySchemaFixture.Exec(c,null,"UPDATE fin_operation_capture_manifest SET status=2,empty_reason='No housing in synthetic scope' WHERE operation_id=@run AND required_kind='housing'",("@run",run));
        Freeze(c,run);
        Assert.AreEqual(1L,MoneySchemaFixture.Scalar(c,null,"SELECT capture_state FROM sal_pay_run WHERE id=@id",("@id",run)));
        string unknown=MoneySchemaFixture.Run(c,company,"other");MoneySchemaFixture.Reject(()=>Freeze(c,unknown),"Capture incomplete or snapshot scope mismatch");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TypedLinksRejectWrongKindOperationPeriodAndDuplicateContent(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),other=MoneySchemaFixture.Run(c,company,"keeta");
        string erp=MoneySchemaFixture.Id(),snap=Snapshot(c,company,erpRow:erp);
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,snap,kind:"fuel"),"FK_op_snap_snapshot",sqliteToken:"FOREIGN KEY");
        // Wrong period must be checked before a successful link: UQ_op_snap_content omits period_month, so SQL Server
        // would report that unique key instead of FK_op_snap_snapshot once the same content is already linked.
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,snap,month:"2026-12-01"),"FK_op_snap_snapshot",sqliteToken:"Capture owner or period mismatch");
        string link=Link(c,run,snap);
        SchemaConstraintAssert.ThrowsNamed(()=>Captured(c,other,link),"FK_manifest_link",sqliteToken:"FOREIGN KEY");
        SchemaConstraintAssert.ThrowsNamed(()=>Captured(c,run,link,kind:"fuel"),"FK_manifest_link",sqliteToken:"FOREIGN KEY");
        string duplicate=Snapshot(c,company,erpRow:erp);
        SchemaConstraintAssert.ThrowsNamed(()=>Link(c,run,duplicate),"UQ_op_snap_content",sqliteToken:"fin_operation_snapshot_link.operation_type, fin_operation_snapshot_link.operation_id, fin_operation_snapshot_link.snapshot_kind");
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_ops_snapshot SET payload='{\"changed\":true}' WHERE id=@id",("@id",snap)),"Snapshots are append-only evidence");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FrozenEvidenceCannotBeEditedReplacedDeletedOrMovedAndSealIsMonotonic(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),snap=Snapshot(c,company),link=Link(c,run,snap);
        Captured(c,run,link);FillEmpty(c,run,except:"ninja_daily");Freeze(c,run);
        foreach(var (sql,name) in new[]{
            ("UPDATE fin_operation_capture_manifest SET empty_reason='changed' WHERE operation_id=@id","Frozen operation evidence cannot change"),
            ("DELETE FROM fin_operation_capture_manifest WHERE operation_id=@id","Frozen operation evidence cannot change"),
            ("UPDATE fin_operation_snapshot_link SET operation_type='payment' WHERE operation_id=@id","Frozen operation evidence cannot change"),
            ("DELETE FROM fin_operation_snapshot_link WHERE operation_id=@id","Frozen operation evidence cannot change"),
            ("UPDATE sal_pay_run SET capture_state=0,stage=1,calc_started_at=NULL WHERE id=@id","Frozen run cannot reopen or change capture scope")})
            MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,sql,("@id",run)),name);
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"DELETE FROM sal_ops_snapshot WHERE id=@id",("@id",snap)),"Snapshots are append-only evidence");
        if(!SchemaSql.IsSql(c))MoneySchemaFixture.Exec(c,null,"PRAGMA recursive_triggers=OFF");
        if(!SchemaSql.IsSql(c))MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"INSERT OR REPLACE INTO sal_ops_snapshot SELECT id,period_month,kind,erp_row_id,scope_company_id,scope_app,captured_at,captured_by,source_fingerprint,'{\"changed\":true}',generation FROM sal_ops_snapshot WHERE id=@id",("@id",snap)),"Snapshot replacement is forbidden");
        MoneySchemaFixture.Reject(()=>Link(c,run,Snapshot(c,company,"fuel")),"Frozen operation evidence cannot change");
        MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=2 WHERE id=@id",("@id",run));
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE sal_pay_run SET capture_state=1 WHERE id=@id",("@id",run)),"Frozen run cannot reopen or change capture scope");
        SchemaOperationCapture.Ensure(c);Assert.AreEqual(9L,MoneySchemaFixture.Scalar(c,null,"SELECT COUNT(*) FROM fin_operation_capture_manifest WHERE operation_id=@id",("@id",run)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OpenEvidenceIdChangeIsRejectedOnBothProviders(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company),snap=Snapshot(c,company),link=Link(c,run,snap);
        string next=MoneySchemaFixture.Id();
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_operation_snapshot_link SET id=@next WHERE id=@id",("@id",link),("@next",next)),"Evidence id cannot change");
        Empty(c,run,"housing");
        string manifest=(string)MoneySchemaFixture.Scalar(c,null,"SELECT id FROM fin_operation_capture_manifest WHERE operation_id=@run AND required_kind='housing'",("@run",run))!;
        MoneySchemaFixture.Reject(()=>MoneySchemaFixture.Exec(c,null,"UPDATE fin_operation_capture_manifest SET id=@next WHERE id=@id",("@id",manifest),("@next",MoneySchemaFixture.Id())),"Evidence id cannot change");
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SnapshotFromAnotherCompanyOrAppCannotSatisfyCapture(bool sqlServer)
    {
        using var c=Open(sqlServer);string company=MoneySchemaFixture.Company(c),other=MoneySchemaFixture.Company(c),run=MoneySchemaFixture.Run(c,company);
        string link=Link(c,run,Snapshot(c,other));Captured(c,run,link);FillEmpty(c,run,except:"ninja_daily");
        MoneySchemaFixture.Reject(()=>Freeze(c,run),"Capture incomplete or snapshot scope mismatch");
        MoneySchemaFixture.Exec(c,null,"DELETE FROM fin_operation_capture_manifest WHERE operation_id=@id AND required_kind='ninja_daily'",("@id",run));
        Captured(c,run,Link(c,run,Snapshot(c,company,app:"keeta")));
        MoneySchemaFixture.Reject(()=>Freeze(c,run),"Capture incomplete or snapshot scope mismatch");
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
