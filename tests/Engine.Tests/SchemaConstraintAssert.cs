using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Data.Common;

/// <summary>Assert a specific named CHECK/UNIQUE/FK constraint fired (finding 7 / R4).
/// SQL Server messages include the constraint name. SQLite CHECK messages do too;
/// UNIQUE/FK often only name columns or say "FOREIGN KEY constraint failed" — pass sqliteToken for those.</summary>
internal static class SchemaConstraintAssert
{
    public static void ThrowsNamed(Action action,string constraintName,string? sqliteToken=null)
    {
        try
        {
            action();
            Assert.Fail("Expected constraint "+constraintName+" to reject the row.");
        }
        catch(SqliteException ex)
        {
            StringAssert.Contains(ex.Message,sqliteToken??constraintName,StringComparison.OrdinalIgnoreCase);
        }
        catch(SqlException ex)
        {
            StringAssert.Contains(ex.Message,constraintName,StringComparison.OrdinalIgnoreCase);
        }
        catch(DbException ex)
        {
            StringAssert.Contains(ex.Message,constraintName,StringComparison.OrdinalIgnoreCase);
        }
    }
}
