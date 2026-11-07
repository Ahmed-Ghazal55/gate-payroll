using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;
[TestClass]
public sealed class SyntheticVoucherSettings
{
    public const string Key="synthetic-test-only-voucher-key-never-use-on-a-server";
    [AssemblyInitialize]
    public static void Initialize(TestContext _) => Vouchers.Configure(Key);
}
