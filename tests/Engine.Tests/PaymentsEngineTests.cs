using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class PaymentsEngineTests
{
    internal sealed class Fixture : IDisposable
    {
        readonly string directory=Path.Combine(Path.GetTempPath(),"wp1-"+Guid.NewGuid().ToString("N"));
        readonly string? sqlDatabase;
        // Keep the password-bearing string; SqlConnection.ConnectionString strips it after open.
        internal string? SqlConnectionString {get;}
        public Store Store {get;}
        public string Company {get;}
        public static ClaimsPrincipal User(string name="wp-maker")=>UserAccess.Principal(new JsonObject{["username"]=name,["role"]="Admin",["department"]="FIN",["rankLevel"]=5});
        public ClaimsPrincipal Maker=>User();
        public ClaimsPrincipal Checker=>User("wp-checker");
        public Fixture(bool sql)
        {
            if(sql)
            {
                string configured=Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")??"";
                if(string.IsNullOrWhiteSpace(configured))Assert.Inconclusive("PAYROLL_TEST_SQL_CONNECTION not set; isolated SQL Server required.");
                var options=new SqlConnectionStringBuilder(configured){Pooling=false,PersistSecurityInfo=true};
                Assert.IsTrue(Regex.IsMatch(options.DataSource,@"^(tcp:)?(localhost|127\.0\.0\.1)(,[0-9]+)?$",RegexOptions.IgnoreCase),"Only isolated loopback SQL Server is allowed.");
                options.InitialCatalog="master";
                sqlDatabase="GateWp1_"+Guid.NewGuid().ToString("N");
                using(var admin=new SqlConnection(options.ConnectionString))
                {admin.Open();SchemaSql.Exec(admin,null,$"CREATE DATABASE [{sqlDatabase}]");}
                options.InitialCatalog=sqlDatabase;SqlConnectionString=options.ConnectionString;Store=new Store(directory,SqlConnectionString);
            }
            else Store=new Store(directory);
            Company=Store.Write((c,tx)=>{
                string company=SchemaSql.GuidText(SchemaSql.NewId());
                SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"hr","company")}(id,code,unified_number,name_ar,status,created_at) VALUES(@id,'WP1','7000000001','شركة اختبار',1,{SchemaSql.NowLiteral(c)})",("@id",company));
                foreach(string name in new[]{"wp-maker","wp-checker"})Store.Save(c,"user",new(){["id"]=name,["username"]=name,["role"]="Admin",["department"]="FIN",["rankLevel"]=5},tx);
                SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,'2026-11-01',1,{SchemaSql.NowLiteral(c)})",("@id",MoneySchemaFixture.Id()),("@c",company));return company;
            });
            Store.Write((c,tx)=>{
                string period=MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_accounting_period WHERE company_id=@c",("@c",Company))!.ToString()!;
                Guid Account(string code)=>Guid.Parse(MoneySchemaFixture.Scalar(c,tx,"SELECT id FROM fin_account WHERE code=@code",("@code",code))!.ToString()!);
                JournalPosting.Post(c,tx,new(Guid.Parse(Company),Guid.Parse(period),new(2026,11,1),new(2026,11,1),"synthetic_opening",Guid.NewGuid(),"opening",0,false,"wp-maker",[
                    new(Account("111101"),SchemaOrgCostCentre.NoneId,"none",5000,0,null),new(Account("331301"),SchemaOrgCostCentre.NoneId,"none",0,5000,null)]));return true;
            });
        }
        public JsonObject Input(string type="expense",decimal amount=100m)=>new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["companyId"]=Company,["type"]=type,["period"]="2026-11",["occurredOn"]="2026-11-05",["dueOn"]="2026-11-15",["reference"]="SYN-"+Guid.NewGuid().ToString("N"),["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=amount,["costCentreId"]=SchemaOrgCostCentre.NoneId.ToString()})};
        public JsonObject Create(JsonObject? input=null)=>Store.Write((c,tx)=>PaymentsEngine.Create(c,tx,Maker,input??Input()));
        public JsonObject Approve(string id)
        {Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,Maker,id,"submit",new()));return Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,Checker,id,"approve",new()));}
        public JsonObject Reserve(string id,decimal amount)=>Store.Write((c,tx)=>PaymentsEngine.Reserve(c,tx,Maker,id,new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["amount"]=amount,["paidOn"]="2026-11-20",["bankAccountCode"]="111101",["reference"]="SYN-PV"}));
        public void Dual(JsonObject payment)=>Store.Write((c,tx)=>ApprovalsArea.Decide(c,tx,Checker,payment.S("approvalRequestId"),new(){["decision"]="approve"}));
        public JsonObject Pay(string id)=>Store.Write((c,tx)=>PaymentsEngine.Pay(c,tx,Maker,id));
        public object? Scalar(string sql)=>Store.Read(c=>MoneySchemaFixture.Scalar(c,null,sql));
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
            if(sqlDatabase!=null)
            {
                var options=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PAYROLL_TEST_SQL_CONNECTION")){InitialCatalog="master",Pooling=false};
                using var admin=new SqlConnection(options.ConnectionString);admin.Open();
                SchemaSql.Exec(admin,null,$"ALTER DATABASE [{sqlDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{sqlDatabase}]");
            }
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void PartialReservationPayAndRetryAreAtomicAndBounded(bool sql)
    {
        using var f=new Fixture(sql);var input=f.Input();var doc=f.Create(input);Assert.AreEqual(doc.S("id"),f.Create(input).S("id"));f.Approve(doc.S("id"));
        var p=f.Reserve(doc.S("id"),40m);Assert.AreEqual(40m,Convert.ToDecimal(f.Scalar("SELECT amount_reserved FROM pay_payable")));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Reserve(doc.S("id"),61m)).Status);
        Assert.AreEqual(403,Assert.ThrowsExactly<ApiError>(()=>f.Pay(p.S("id"))).Status);
        Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM fin_journal WHERE source_type='payment_document'"));
        f.Dual(p);var paid=f.Pay(p.S("id"));Assert.AreEqual(2m,paid.N("status"));f.Pay(p.S("id"));
        Assert.AreEqual(40m,Convert.ToDecimal(f.Scalar("SELECT amount_paid FROM pay_payable")));Assert.AreEqual(0m,Convert.ToDecimal(f.Scalar("SELECT amount_reserved FROM pay_payable")));
        Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM fin_journal WHERE source_type='payment_document'"));Assert.AreEqual(40m,Convert.ToDecimal(f.Scalar("SELECT SUM(l.debit) FROM fin_journal_line l JOIN fin_journal j ON j.id=l.journal_id WHERE j.source_type='payment_document'")));
        var rest=f.Reserve(doc.S("id"),60m);f.Dual(rest);f.Pay(rest.S("id"));Assert.AreEqual(100m,Convert.ToDecimal(f.Scalar("SELECT amount_paid FROM pay_payable")));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void CancellationReleasesClaimAndRetainsDraftAndScopeIsEnforced(bool sql)
    {
        using var f=new Fixture(sql);var doc=f.Create();f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),35m);
        f.Store.Write((c,tx)=>PaymentsEngine.CancelPayment(c,tx,f.Maker,p.S("id")));
        Assert.AreEqual(0m,Convert.ToDecimal(f.Scalar("SELECT amount_reserved FROM pay_payable")));Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_payment"));
        var draft=f.Create();f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,draft.S("id"),"cancel",new()));Assert.AreEqual(2L,f.Scalar("SELECT COUNT(*) FROM pay_document"));
        var outsider=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"outsider"),new Claim("permission","payments.view")},"test"));
        Assert.AreEqual(404,Assert.ThrowsExactly<ApiError>(()=>f.Store.Read(c=>PaymentsEngine.Get(c,null,outsider,doc.S("id")))).Status);
        Assert.AreEqual(0,f.Store.Read(c=>PaymentsEngine.List(c,outsider,null,null)["items"]!.AsArray().Count));
    }

    [TestMethod][DataRow(false)][DataRow(true)]
    public void SimulationAndBlockedPaymentsCannotEscapeAndLatePostingFailureRollsBack(bool sql)
    {
        using var f=new Fixture(sql);
        var simulated=f.Input();simulated["runMode"]=1;var sim=f.Create(simulated);f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Maker,sim.S("id"),"submit",new()));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Transition(c,tx,f.Checker,sim.S("id"),"approve",new()))).Status);
        var blocked=f.Input();blocked["payoutBlocked"]=true;var d=f.Create(blocked);f.Approve(d.S("id"));Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Reserve(d.S("id"),10)).Status);
        var doc=f.Create();f.Approve(doc.S("id"));var p=f.Reserve(doc.S("id"),25);f.Dual(p);
        f.Store.Write((c,tx)=>{SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"fin","accounting_period")} SET state=2");return true;});
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Pay(p.S("id"))).Status);Assert.AreEqual(25m,Convert.ToDecimal(f.Scalar("SELECT SUM(amount_reserved) FROM pay_payable")));Assert.AreEqual(0L,f.Scalar("SELECT COUNT(*) FROM fin_journal WHERE source_type='payment_document'"));
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void DraftEditsRetainOldLinesAndRejectStaleContent(bool sql)
    {
        using var f=new Fixture(sql);var doc=f.Create();var patch=new JsonObject{["expectedHash"]=doc.S("contentHash"),["lines"]=new JsonArray(new JsonObject{["accountCode"]="526101",["amount"]=75})};
        var edited=f.Store.Write((c,tx)=>PaymentsEngine.Edit(c,tx,f.Maker,doc.S("id"),patch));Assert.AreEqual(75m,edited.N("amount"));
        Assert.AreEqual(2L,f.Scalar("SELECT COUNT(*) FROM pay_document_line"));Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_document_line WHERE active=1"));
        Assert.AreEqual(409,Assert.ThrowsExactly<ApiError>(()=>f.Store.Write((c,tx)=>PaymentsEngine.Edit(c,tx,f.Maker,doc.S("id"),patch))).Status);
    }
    [TestMethod][DataRow(false)][DataRow(true)]
    public void CompetingStoresCannotReserveMoreThanPayable(bool sql)
    {
        using var f=new Fixture(sql);var doc=f.Create();f.Approve(doc.S("id"));
        var other=new Store(f.Store.DirectoryPath,f.SqlConnectionString);
        var tasks=new[]{f.Store,other}.Select(store=>Task.Run(()=>{try{store.Write((c,tx)=>PaymentsEngine.Reserve(c,tx,f.Maker,doc.S("id"),new(){["idempotencyKey"]=Guid.NewGuid().ToString("N"),["amount"]=60,["paidOn"]="2026-11-20",["bankAccountCode"]="111101",["reference"]="SYN-RACE"}));return true;}catch(ApiError){return false;}catch(System.Data.Common.DbException){return false;}})).ToArray();
        Task.WaitAll(tasks);Assert.AreEqual(1,tasks.Count(t=>t.Result));Assert.AreEqual(60m,Convert.ToDecimal(f.Scalar("SELECT amount_reserved FROM pay_payable")));Assert.AreEqual(1L,f.Scalar("SELECT COUNT(*) FROM pay_payment"));
    }

}
