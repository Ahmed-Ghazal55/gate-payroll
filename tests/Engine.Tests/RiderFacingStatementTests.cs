using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class RiderFacingStatementTests
{
    const string Iqama="2000000888";
    static string Bw=>FinancialCore.Sponsors[0].Id;
    static System.Security.Claims.ClaimsPrincipal Admin=>UserAccess.Principal(new(){["role"]="Admin",["username"]="synthetic-admin"});

    [TestMethod]
    public void ShortFormSupportsArabicEnglishUrduFromOneSourceWithoutOmar()
    {
        string path=Path.Combine(Path.GetTempPath(),"gate-rider-facing-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new Store(path);
            store.Write((c,tx)=>{Store.Save(c,"rider",new(){["id"]=Iqama,["iqama"]=Iqama,["name"]="Synthetic Rider",["financeSponsorId"]=Bw},tx);return true;});
            store.Write((c,tx)=>
            {
                var lines=new JsonArray(
                    FinancialCore.CreateJournalLine("2110",Bw,0,100,"مستحق",Iqama),
                    FinancialCore.CreateJournalLine("5301",Bw,100,0,"مصروف",Iqama));
                FinancialCore.PostJournal(c,tx,"payroll_accrual","Synthetic", "2026-10","RF-1",lines,"synthetic");
                return true;
            });
            var ar=store.Read(c=>RiderStatements.BuildRiderFacing(c,Iqama,"2026-10","2026-10",null,"ar",Admin));
            var en=store.Read(c=>RiderStatements.BuildRiderFacing(c,Iqama,"2026-10","2026-10",null,"en",Admin));
            var ur=store.Read(c=>RiderStatements.BuildRiderFacing(c,Iqama,"2026-10","2026-10",null,"ur",Admin));
            Assert.AreEqual(ar.N("closingNet"),en.N("closingNet"));
            Assert.AreEqual(ar.N("closingNet"),ur.N("closingNet"));
            Assert.IsTrue(ar.S("disclaimer").Contains("للاطلاع",StringComparison.Ordinal));
            Assert.IsTrue(en.S("disclaimer").Contains("information only",StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(ur.B("liveOmarRead"));
            Assert.AreEqual("posted_journal_lines",ar.S("source"));
            store.Write((c,tx)=>{RiderStatements.SaveApprovalSnapshot(c,tx,Iqama,"2026-10","2026-10",null,Admin,ar);return true;});
            var fromSnap=store.Read(c=>RiderStatements.BuildRiderFacing(c,Iqama,"2026-10","2026-10",null,"en",Admin));
            Assert.AreEqual("final_approval_snapshot",fromSnap.S("source"));
            Assert.AreEqual(false,fromSnap.B("liveOmarRead"));
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
