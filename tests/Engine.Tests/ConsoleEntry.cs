Payroll.Vouchers.Configure(SyntheticVoucherSettings.Key);
if(args.Contains("--schema-money-tests"))
{
    int passed=0,failed=0;
    var classes=typeof(SchemaPayPayableTests).Assembly.GetTypes().Where(t=>t.Name.StartsWith("SchemaPay",StringComparison.Ordinal)||t.Name.StartsWith("SchemaSal",StringComparison.Ordinal)||t.Name=="SchemaOperationCaptureTests"||t.Name=="ConfigListMappingTests");
    foreach(var type in classes.OrderBy(t=>t.Name))
    foreach(var method in type.GetMethods().Where(m=>m.GetCustomAttributes(typeof(Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute),true).Length>0))
    {
        try{method.Invoke(Activator.CreateInstance(type),null);Console.WriteLine($"PASS {type.Name}.{method.Name}");passed++;}
        catch(System.Reflection.TargetInvocationException error){Console.Error.WriteLine($"FAIL {type.Name}.{method.Name}: {error.InnerException}");failed++;}
    }
    Console.WriteLine($"Money schema tests: {passed} passed, {failed} failed.");
    return failed==0&&passed>0?0:1;
}
if(args.Contains("--accounting-review"))
{
    int passed=0;
    var methods=new[]{typeof(AccountingReviewTests),typeof(AccountingPostingTests),typeof(TreasuryControlTests)}.SelectMany(t=>t.GetMethods().Where(m=>m.GetCustomAttributes(typeof(Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute),false).Length>0)).ToList();
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
return Environment.ExitCode;
