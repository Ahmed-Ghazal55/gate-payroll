using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

[TestClass]
public sealed class SchemaMigrationLockTests
{
    [TestMethod]
    public void ConcurrentSqliteStoreStartsDoNotFail()
    {
        string dir=Path.Combine(Path.GetTempPath(),"gate-schema-lock-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Exception?[] errors=new Exception?[4];
            var threads=Enumerable.Range(0,4).Select(i=>new Thread(()=>
            {
                try{var store=new Store(dir);Assert.IsTrue(store.DataPreservationVerified);}
                catch(Exception ex){errors[i]=ex;}
            })).ToArray();
            foreach(var t in threads)t.Start();
            foreach(var t in threads)t.Join();
            Assert.IsTrue(errors.All(e=>e is null),string.Join("; ",errors.Where(e=>e!=null).Select(e=>e!.Message)));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
