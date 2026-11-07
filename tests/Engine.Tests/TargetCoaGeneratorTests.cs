using System.Diagnostics;
using System.Text.Json.Nodes;
using Payroll;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class TargetCoaGeneratorTests
{
    private const string Root = "| `1` | مجموعة اختبار | — | لا | مدين | أخرى | — | الكل | — | فعال | Excel |";
    private static string Leaf(string code, string status, string source, string subledger = "—", bool posting = true) =>
        $"| `{code}` | حساب اختبار | 1 | {(posting ? "نعم" : "لا")} | مدين | أخرى | — | الكل | {subledger} | {status} | {source} |";

    private static async Task<(int Code, string Error, JsonObject? Resource)> Generate(
        string rows, int retainedExcel, int modifiedExcel, int movedExcel, int newCount, int posting, int? total = null)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string directory = Path.Combine(Path.GetTempPath(), "gate-coa-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "synthetic.md"), output = Path.Combine(directory, "generated.json");
            string markdown = $"""
                ## 1. الخلاصة
                | البند | العدد |
                | حسابات Excel المحتفظ بها بنفس الكود | {retainedExcel} |
                | منها معدّل (اسم أو حالة أو تجميع) | {modifiedExcel} |
                | حسابات Excel منقولة لكود جديد (BR §20) | {movedExcel} |
                | حسابات جديدة منقولة (BR §20) | 0 |
                | حسابات جديدة | {newCount} |
                | إجمالي الشجرة المستهدفة | {total ?? retainedExcel + movedExcel + newCount} |
                | حسابات ترحيل فعالة | {posting} |
                ## 2. قواعد اختبار
                ## 4. شجرة اختبار مصطنعة
                {Root}
                {rows}
                ## 5. ربط اختبار
                ## 6. ربط اختبار
                | `SYNTHETIC` | حساب اختبار | `1` | ربط اختبار |
                ## 7. نهاية
                """;
            await File.WriteAllTextAsync(source, markdown);
            var info = new ProcessStartInfo("python3") { WorkingDirectory = root, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string argument in new[] { Path.Combine(root, "tools/generate_target_coa.py"), "--source", source, "--output", output }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Python did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Synthetic COA generation timed out."); }
            await stdout;
            return (process.ExitCode, await stderr, File.Exists(output) ? JsonNode.Parse(await File.ReadAllTextAsync(output))!.AsObject() : null);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("موقوف — سبب اختبار", "stopped", "سبب اختبار")]
    [DataRow("موقوف\t— سبب اختبار", "stopped", "سبب اختبار")]
    [DataRow("عرض فقط — سبب عرض", "display", "سبب عرض")]
    [DataRow("محجوز", "reserved", null)]
    public async Task StatusUsesFirstWordAndKeepsOnlyItsReason(string status, string expected, string? note)
    {
        var result = await Generate(Leaf("11", status, "جديد", posting: false), 1, 0, 0, 1, 0);
        Assert.AreEqual(0, result.Code, result.Error);
        var row = result.Resource!["accounts"]![1]!.AsObject();
        Assert.AreEqual(expected, row["status"]!.GetValue<string>());
        Assert.AreEqual(note, row["statusNote"]?.GetValue<string>());
        Assert.IsFalse(row["posting"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task NewAccountSourceCanRetainAnApprovedRuleAnnotation()
    {
        var result=await Generate(Leaf("11","فعال","جديد (BR §29)"),1,0,0,1,1);Assert.AreEqual(0,result.Code,result.Error);var row=result.Resource!["accounts"]![1]!.AsObject();Assert.AreEqual("new",row.S("source"));Assert.AreEqual("جديد (BR §29)",row.S("sourceNote"));
    }
    [TestMethod]
    public async Task SourceClassifiesExcelNewModifiedAndMovedWithoutLosingOriginalText()
    {
        string moved = "منقول (كان `99`)";
        var result = await Generate(string.Join('\n', Leaf("11", "فعال", "جديد"), Leaf("12", "فعال", "Excel معدّل"), Leaf("13", "فعال", moved)), 2, 1, 1, 1, 3);
        Assert.AreEqual(0, result.Code, result.Error);
        var rows = result.Resource!["accounts"]!.AsArray();
        CollectionAssert.AreEqual(new[] { "excel", "new", "modified", "modified" }, rows.Select(a => a!["source"]!.GetValue<string>()).ToArray());
        Assert.AreEqual(moved, rows[3]!["sourceNote"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task SubledgerAndAllNewFieldsExistOnEverySyntheticAccount()
    {
        var result = await Generate(Leaf("11", "فعال", "جديد", "دفتر اختبار"), 1, 0, 0, 1, 1);
        Assert.AreEqual(0, result.Code, result.Error);
        var rows = result.Resource!["accounts"]!.AsArray();
        foreach (var row in rows.OfType<JsonObject>())
            foreach (string field in new[] { "source", "sourceNote", "statusNote", "subledger" }) Assert.IsTrue(row.ContainsKey(field));
        Assert.AreEqual("—", rows[0]!["subledger"]!.GetValue<string>());
        Assert.AreEqual("دفتر اختبار", rows[1]!["subledger"]!.GetValue<string>());
        Assert.IsNull(rows[1]!["statusNote"]);
    }

    [TestMethod]
    public async Task SummaryMismatchFailsBeforeWritingTheResource()
    {
        var result = await Generate(Leaf("11", "فعال", "جديد"), 1, 0, 0, 1, 1, total: 3);
        Assert.AreNotEqual(0, result.Code);
        StringAssert.Contains(result.Error, "COA counts differ from §1 summary");
        Assert.IsNull(result.Resource);
    }
}
