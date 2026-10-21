using Payroll;
using System.Text;
using System.Text.Json.Nodes;
using System.IO.Compression;
public static class LegacyAssertions
{
public static int Run()
{
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
void Reject(Action action, string label) { try { action(); } catch (ApiError) { checks++; return; } throw new Exception(label); }
JsonObject policy = new() { ["id"]="test", ["name"]="Test", ["kind"]="commission", ["platform"]="all", ["effectiveFrom"]="2026-10", ["status"]="approved", ["config"]=new JsonObject { ["companyFee"]=2500 } };
WorkLine row = new("r1","2000000001","Test","Keeta","1234567890123456","2026-10","2026-10-01","2026-10-31",600,1000,null,false,"Test","Test",true);
var result = Engine.Calculate(row.Iqama,row.Period,policy,[row,row with {SourceRef="r2", PlatformRevenue=1000}],[],0);
Check(result.PeriodMovement==-500 && result.Debt==500 && result.ClosingBalance==-500,"Commission once per month regardless of validity; keep debt");
Check(Engine.Calculate(row.Iqama,row.Period,policy,[row with {PlatformRevenue=null}],[],0).Blockers.Count>0,"Missing actual settlement blocks");
policy["kind"]="tiers"; policy["config"]=JsonNode.Parse("{\"tiers\":[{\"minOrders\":500,\"salary\":3000,\"extraRate\":5,\"fuelAllowance\":200}],\"internetAllowance\":100}");
Check(Engine.Calculate(row.Iqama,row.Period,policy,[row],[],0).Blockers.Count>0,"600 orders cannot establish validity");
row=row with {IsValid=true};result=Engine.Calculate(row.Iqama,row.Period,policy,[row],[],0);Check(result.PeriodMovement==3800 && result.Blockers.Count==0,"Company tiers explicit rates");
Check(Engine.Calculate(row.Iqama,row.Period,policy,[row with {From="2026-10-02"}],[],0).Blockers.Count>0,"Partial month blocked without proration policy");
Check(Engine.Calculate(row.Iqama,row.Period,policy,[row with {DatesConfirmed=false}],[],0).Blockers.Count>0,"Default dates cannot prove full month");
policy["status"]="draft";Check(Engine.Calculate(row.Iqama,row.Period,policy,[row],[],0).Blockers.Count>0,"Draft policy blocked");
Check(J.Money(1.005m)==1.01m && J.Money(-1.005m)==-1.01m,"Decimal rounding");
Check(J.Iqama("٢٠٠٠٠٠٠٠٠١")=="2000000001" && J.Phone("0501234567")=="966501234567","Arabic digits and phone normalization");
Check(J.Iban("SA0380000000608010167519")=="SA0380000000608010167519","IBAN MOD97");Reject(()=>J.Iban("SA0080000000608010167519"),"Invalid IBAN");
JsonObject spec=JsonNode.Parse("{\"period\":\"2026-10\",\"platform\":\"Keeta\",\"confirmActualRider\":true,\"mapping\":{\"iqama\":1,\"name\":2,\"accountId\":3,\"platformRevenue\":4}}")!.AsObject();
var sheets=Imports.Read(Encoding.UTF8.GetBytes("original,actual,name,account,revenue\r\n1000000001,2000000001,\"Name, quoted\",1234567890123456,3000\r\n"),"test.csv");
var normalized=Imports.Normalize(sheets[0],spec);Check(normalized.Errors.Count==0 && normalized.Rows[0].Iqama=="2000000001" && normalized.Rows[0].Name=="Name, quoted" && normalized.Rows[0].AccountId=="1234567890123456","Actual substitute mapping; CSV quotes; long ID text");
Check(!normalized.Rows[0].DatesConfirmed,"Missing source dates tracked");
spec["confirmActualRider"]=false;Reject(()=>Imports.Normalize(sheets[0],spec),"Actual identity confirmation required");
using(var stream=new MemoryStream()) {
 using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
  void Entry(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}
  Entry("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Test' r:id='rId1'/></sheets></workbook>");
  Entry("xl/_rels/workbook.xml.rels","<Relationships><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
  Entry("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='2'><c r='B2' t='inlineStr'><is><t>2000000001</t></is></c><c r='D2' t='inlineStr'><is><t>1234567890123456</t></is></c></row></sheetData></worksheet>");
 }
 var xlsx=Imports.Read(stream.ToArray(),"test.xlsx");Check(xlsx[0].Rows.Count==2 && xlsx[0].Rows[1][0]=="" && xlsx[0].Rows[1][3]=="1234567890123456","Sparse XLSX preserves row/column position and identifiers");
}
JsonObject choice=new(){["iqama"]="2000000001",["name"]="  Test   Rider  ",["workSystem"]="tiers"};
Check(WorkChoices.Validate(choice).Name=="Test Rider","Normalize name spacing");
foreach(var invalidId in new[]{"٢٠٠٠٠٠٠٠٠١","۲۰۰۰۰۰۰۰۰۱","200000001","20000000011","20000A0001","20000 0001"}) { var bad=choice.Clone();bad["iqama"]=invalidId;Reject(()=>WorkChoices.Validate(bad),"Strict English-only 10 digit identity"); }
foreach(var invalidName in new[]{"Single","Test 123","<script> Rider"}) { var bad=choice.Clone();bad["name"]=invalidName;Reject(()=>WorkChoices.Validate(bad),"Name validation"); }
var third=choice.Clone();third["workSystem"]="discuss";Reject(()=>WorkChoices.Validate(third),"Exactly two choices");
var extra=choice.Clone();extra["phone"]="0501234567";Reject(()=>WorkChoices.Validate(extra),"Only three payload fields");
checks += HostingSecurityTests.Run();
Console.WriteLine($"PASS {checks} engine/import validation checks.");
return checks;
}
}
