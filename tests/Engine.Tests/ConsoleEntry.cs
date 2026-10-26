if(args.Contains("--accounting-review"))
{
    int passed=0;
    var methods=new[]{typeof(AccountingReviewTests),typeof(AccountingPostingTests)}.SelectMany(t=>t.GetMethods().Where(m=>m.GetCustomAttributes(typeof(Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute),false).Length>0)).ToList();
    foreach(var method in methods)
    {
        try { method.Invoke(Activator.CreateInstance(method.DeclaringType!),null); Console.WriteLine("PASS "+method.Name);passed++; }
        catch(System.Reflection.TargetInvocationException error) { Console.Error.WriteLine("FAIL "+method.Name+": "+error.InnerException);Environment.ExitCode=1; }
    }
    Console.WriteLine($"Accounting review: {passed}/{methods.Count} tests passed. This offline subset does not run HTTP, browser or SQL Server tests.");
    new EnterpriseTests().FinancialDoubleEntryAndAllocationAndInterSponsorSettlementWork();
    Console.WriteLine("PASS existing financial shared-cost SQLite test with purpose-correct HR funding.");
}
Console.WriteLine($"Verified {LegacyAssertions.Run()} legacy checks.");
