using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Payroll;

// BR §26: synthetic captains only. Values mirror the seed contract shape, not any real captain.
[TestClass]
public sealed class NinjaBillingTests
{
    static JsonObject P=>NinjaBilling.Seed()["parameters"]!.DeepClone().AsObject();
    static readonly Dictionary<string,decimal> NoPass=NinjaBilling.PassThrough.ToDictionary(x=>x.Code,_=>0m);
    static JsonObject Eval(string cap,int days,int orders,int attended,int shortDays=0,Dictionary<string,decimal>? pass=null)=>
        NinjaBilling.Evaluate(new("SYN-1",cap,days,orders,attended,shortDays,pass??NoPass),P,31);

    [TestMethod]
    public void AboveTargetPaysTheContractPlusTheExtraOrderRate()
    {
        var r=Eval("capability_car",31,587,31);Assert.AreEqual(460m,r.N("adjustedTarget"));Assert.AreEqual(6500m+127*12,r.N("targetTotal"));Assert.AreEqual("above_target",r.S("level"));
        var b=Eval("capability_bike",31,601,31);Assert.AreEqual(5700m+151*11,b.N("targetTotal"));
    }
    [TestMethod]
    public void ProratedBelowTargetUsesTheLevelRatePerMissingOrder()
    {
        var r=Eval("capability_car",21,280,21);
        Assert.AreEqual(311m,r.N("adjustedTarget"),"floor(460×21/31)");Assert.AreEqual(682m,r.N("targetDeduction"),"31 missing × 22");
        Assert.AreEqual(Math.Round(6500m*21/31-682,6),Math.Round(r.N("targetTotal"),6));Assert.AreEqual("below_0.1",r.S("level"));
        Assert.AreEqual("below_0.2",Eval("capability_car",31,400,31).S("level"));Assert.AreEqual(60*22.5m,Eval("capability_car",31,400,31).N("targetDeduction"));
        Assert.AreEqual(138*23m,Eval("capability_car",31,322,31).N("targetDeduction"),"exactly 30% below stays on the 30% level");Assert.AreEqual("per_order",Eval("capability_car",31,321,31).S("level"));
    }
    [TestMethod]
    public void BeyondTheLastLevelPaysPerOrderOnly()
    {
        var car=Eval("capability_car",31,200,31);Assert.AreEqual("per_order",car.S("level"));Assert.AreEqual(2000m,car.N("targetTotal"));
        Assert.AreEqual(1800m,Eval("capability_bike",31,200,31).N("targetTotal"));
    }
    [TestMethod]
    public void AttendanceAndShortDaysDeductThenPassThroughAndVat()
    {
        var pass=new Dictionary<string,decimal>(NoPass){["gasolineSupport"]=100,["penalty"]=-50,["weekend40"]=40};
        var r=Eval("capability_car",31,460,26,shortDays:2,pass:pass);
        Assert.AreEqual(0.15m*6500,r.N("attendanceDeduction"),"26/31 < 85% → 15% of the target total");
        Assert.AreEqual(Math.Round(6500m/31*0.15m*2,6),Math.Round(r.N("shortDayDeduction"),6));
        decimal final=6500-975-6500m/31*0.15m*2+90;Assert.AreEqual(Math.Round(final,6),Math.Round(r.N("finalAmount"),6));Assert.AreEqual(Math.Round(final*1.15m,6),Math.Round(r.N("totalCost"),6));
        Assert.AreEqual(0m,Eval("capability_car",31,460,27).N("attendanceDeduction"),"27/31 ≥ 85%");
    }
    [TestMethod]
    public void UnknownCapabilityAndBadTermsAreRefused()
    {
        Assert.ThrowsExactly<ApiError>(()=>Eval("capability_truck",31,100,31));
        var bad=P.DeepClone().AsObject();bad["capabilities"]!["car"]!["levels"]=new JsonArray(new JsonObject{["maxBelow"]=0.2,["missingOrderRate"]=1},new JsonObject{["maxBelow"]=0.1,["missingOrderRate"]=1});
        Assert.ThrowsExactly<ApiError>(()=>PaySettings.Validate("pay_terms","ninja",new(){["name"]="Synthetic",["billingModel"]="ninja_target",["parameters"]=bad}));
        PaySettings.Validate("pay_terms","ninja",new(){["name"]="Synthetic",["billingModel"]="ninja_target",["parameters"]=P});
    }

    static readonly string[] Header=["supplier_id","supplier_name","CAPTAIN ACTIVE STATUS","capabilty","captain_id","Zone Name","First Attended Date","Total Delivered Orders","Contract Amount","Contract Target",
        "Count of First Attended Date to End of Month","Adjusted Target","Adjusted Amount","Below Target Percentage","Target Level Type","Target Deductions Amount","Target Bonus Amount","Target Total Amount",
        "Count of Attended Days","Attendance Deduction","Count of Days Less than 8 Hr","8 Hour Deductions","Deduction refund","Gasoline Support","Weekend 40 Bonus","Weekend 42 Bonus","Weekend 43 Bonus",
        "100 SAR BONUS","Supplier Performance Bonus","NAQIL BLOCKED","Penalty","on demand","STOPS WITHOUT NOTICE DEDUCTION","Final Amount","VAT","TOTAL COST"];
    static List<string> Row(string captain,string cap,int days,int orders,int attended,decimal finalOverride=-1)
    {
        var e=Eval(cap,days,orders,attended);string S(decimal v)=>v.ToString(CultureInfo.InvariantCulture);decimal final=finalOverride>=0?finalOverride:e.N("finalAmount");
        var v=new Dictionary<string,string>{["supplier_id"]="9001",["capabilty"]=cap,["captain_id"]=captain,["Total Delivered Orders"]=S(orders),["Contract Amount"]=cap.EndsWith("bike")?"5700":"6500",
            ["Count of First Attended Date to End of Month"]=S(days),["Adjusted Target"]=S(e.N("adjustedTarget")),["Adjusted Amount"]=S(e.N("adjustedAmount")),["Target Total Amount"]=S(e.N("targetTotal")),
            ["Count of Attended Days"]=S(attended),["Attendance Deduction"]=S(e.N("attendanceDeduction")),["Count of Days Less than 8 Hr"]="0",["8 Hour Deductions"]="0",
            ["Final Amount"]=S(final),["VAT"]=S(final*0.15m),["TOTAL COST"]=S(final*1.15m)};
        return Header.Select(h=>v.GetValueOrDefault(h,"0")).ToList();
    }
    [TestMethod]
    public void ReconcileMatchesCaptainsFlagsDifferencesAndKeepsAdjustments()
    {
        var rows=new List<List<string>>{Header.ToList(),Row("SYN-A","capability_car",31,500,31),Row("SYN-B","capability_bike",20,250,20),Row("SYN-C","capability_car",31,460,31,finalOverride:6400)};
        var adj=Header.Select(h=>h switch{"captain_id"=>"SYN-ADJ","Target Level Type"=>"DEDUCTIONS ","Contract Amount"=>"-","Final Amount"=>"-33.12","TOTAL COST"=>"-38.09",_=>"0"}).ToList();rows.Add(adj);
        var r=NinjaBilling.Reconcile([new("Sheet1",rows)],"2026-10",P,"SYNHASH");
        Assert.AreEqual(3m,r.N("captains"));Assert.AreEqual(2m,r.N("matched"));Assert.AreEqual(1m,r.N("different"));Assert.AreEqual(1,r["adjustments"]!.AsArray().Count);
        var diff=r["lines"]!.AsArray().OfType<JsonObject>().Single(l=>l.S("status")=="difference");Assert.AreEqual("SYN-C",diff.S("captainId"));
        Assert.IsTrue(diff["differences"]!.AsArray().OfType<JsonObject>().Any(d=>d.S("field")=="finalAmount"&&d.N("difference")==-100));
        Assert.AreEqual(-100m,r["totals"]!["finalAmount"]!.AsObject().N("difference"),"the adjustment line counts on both sides");
    }
    [TestMethod]
    public void ReconcileRefusesAnInvoiceWithMissingColumns()
    {
        var rows=new List<List<string>>{Header.Where(h=>h!="Penalty").ToList()};
        var e=Assert.ThrowsExactly<ApiError>(()=>NinjaBilling.Reconcile([new("Sheet1",rows)],"2026-10",P,"SYNHASH"));StringAssert.Contains(e.Message,"Penalty");
    }
}
