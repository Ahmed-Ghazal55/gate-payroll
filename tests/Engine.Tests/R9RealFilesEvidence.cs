using System.Net;
using System.Text.Json.Nodes;
using Payroll;

// Explicit local invocation only. Real workbooks are read-only; all identities, scheme rates
// and opening facts in the disposable API are synthetic. Never emit invoice rows or filenames.
public static class R9RealFilesEvidence
{
    public static async Task Run(string manifest)
    {
        Vouchers.Configure(SyntheticVoucherSettings.Key);
        int files=0,sourceLines=0,negativeCaptains=0,completed=0,journals=0;
        foreach(string filename in File.ReadAllLines(manifest).Where(p=>p!=""))
        {
            byte[] bytes=File.ReadAllBytes(filename);var reconciled=NinjaBilling.Reconcile(Imports.Read(bytes,"invoice.xlsx"),"2026-10",NinjaBilling.Seed()["parameters"]!.AsObject(),"local-read-only");
            await using var host=await R9PayrollHttpTests.Host.Start(false,Directory.GetCurrentDirectory());var store=host.Store;string company=FinancialCore.Sponsors[0].Id;
            var comparisons=reconciled["lines"]!.AsArray().OfType<JsonObject>().ToList();
            store.Write((c,tx)=>
            {
                var schemeData=PayFixture.Scheme("daily_commission");schemeData["accountingRole"]="principal";foreach(var cell in schemeData["parameters"]!["categories"]!.AsObject())cell.Value!["dailyCommission"]=0;
                var scheme=PaySettings.Create(c,tx,PayFixture.Maker,"pay_scheme","ninja",new(){["period"]="2026-10",["code"]="synthetic-real-file",["data"]=schemeData});scheme=PaySettings.Approve(c,tx,PayFixture.Checker,"pay_scheme","ninja",scheme.S("id"));
                for(int i=0;i<comparisons.Count;i++)
                {
                    string rider=(2000100000L+i).ToString(),account="synthetic-real-account-"+i;
                    Store.Save(c,"rider",new(){["id"]=rider,["iqama"]=rider,["name"]="Synthetic real-file operator",["isDeliveryRider"]=true,["sponsorId"]=company,["status"]="Active",["payCategory"]="sponsored_private"},tx);
                    Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="NINJA",["externalAccountId"]=comparisons[i].S("captainId"),["assignments"]=new JsonArray(new JsonObject{["riderId"]=rider,["RawEffectiveFrom"]="2026-10-01T00:00:00+03:00"})},tx);
                    Store.Save(c,"pay_assignment",new(){["id"]="synthetic-real-choice-"+i,["rider"]=rider,["app"]="ninja",["period"]="2026-10",["status"]="approved",["schemeId"]=scheme.S("id"),["choiceApprovedAt"]="2026-09-29T00:00:00Z"},tx);
                }
                return true;
            });
            async Task<JsonObject> Upload(JsonObject? preview=null)
            {
                using var form=new MultipartFormDataContent();form.Add(new StringContent(company),"companyId");form.Add(new StringContent("2026-10"),"period");form.Add(new StringContent("synthetic-real-file-evidence"),"reference");form.Add(new ByteArrayContent(bytes),"file","invoice.xlsx");
                if(preview!=null){form.Add(new StringContent(preview["rows"]!.ToJsonString()),"rows");form.Add(new StringContent(preview.S("fingerprint")),"fingerprint");form.Add(new StringContent("true"),"confirm");}
                using var response=await host.Maker.PostAsync("/api/platforms/ninja/invoices"+(preview==null?"":"?commit=1"),form);if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidOperationException("Real invoice upload failed; no private payload emitted.");return(await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<JsonObject>(response.Content))!;
            }
            var preview=await Upload();var invoice=await Upload(preview);string invoicePath="/api/platforms/ninja/invoices/"+Uri.EscapeDataString(invoice.S("id"));var lines=(await host.Request(host.Maker,invoicePath+"/lines")).AsArray();
            var items=new JsonArray(lines.OfType<JsonObject>().SelectMany(line=>(line["deductions"] as JsonArray??[]).OfType<JsonObject>().Select(d=>(JsonNode?)new JsonObject{["lineId"]=line.S("id"),["category"]=d.S("category"),["level"]="company_level",["expenseAccount"]="526402"})).ToArray());
            if(items.Count>0)await host.Request(host.Maker,invoicePath+"/classify-batch",new(){["items"]=items});await host.Request(host.Checker,invoicePath+"/approve",new());
            var columns=ImportCatalog.Steps().Single(s=>s.Key=="ninja_operations").Columns;var data=new List<object?[]>();
            foreach(var line in lines.OfType<JsonObject>().Where(l=>l.S("lineType")!="company"))
            {
                int i=comparisons.FindIndex(l=>l.S("captainId")==line.S("captainId"));var values=new Dictionary<string,object?>{["period (YYYY-MM)"]="2026-10",["platformAccountId"]=line.S("platformAccountId"),["iqama"]=(2000100000L+i).ToString(),["days"]=1,["orders"]=line.N("orders"),["baseGross"]=line.N("baseGross"),["deduction"]=0,["category"]="sponsored_private"};data.Add(columns.Select(col=>values.GetValueOrDefault(col.Header)??"").ToArray());
            }
            var operation=new JsonObject{["invoiceId"]=invoice.S("id"),["dataBase64"]=Convert.ToBase64String(ExcelFiles.Write(columns,data))};var opPreview=(await host.Request(host.Maker,"/api/pay/apps/ninja/operations/import/preview",operation)).AsObject();operation["rows"]=opPreview["rows"]!.DeepClone();operation["fingerprint"]=opPreview.S("fingerprint");operation["confirm"]=true;await host.Request(host.Maker,"/api/pay/apps/ninja/operations/import/commit",operation);
            var draft=(await host.Request(host.Maker,"/api/pay/review-draft?app=ninja&period=2026-10&companyId="+company)).AsObject();if(!draft.B("canCreate"))throw new InvalidOperationException("Real-file draft blocked; no private payload emitted.");
            var cycle=(await host.Request(host.Maker,"/api/pay/cycles",new(){["app"]="ninja",["period"]="2026-10",["companyId"]=company,["invoices"]=draft["invoices"]!.DeepClone()})).AsObject();string cyclePath="/api/pay/cycles/"+Uri.EscapeDataString(cycle.S("id"));
            for(int stage=1;stage<=6;stage++)await host.Request(host.Maker,cyclePath+"/advance",new(){["stage"]=stage});await host.Request(host.Maker,cyclePath+"/review/approve-all",new());
            var review=(await host.Request(host.Maker,cyclePath+"/review")).AsObject();foreach(var row in review["rows"]!.AsArray().OfType<JsonObject>().Where(r=>r.S("review")!="approved"))await host.Request(host.Maker,cyclePath+"/review/"+row.S("rider"),new(){["action"]="approve",["reason"]="Synthetic commission review of actual invoice amounts"});
            await host.Request(host.Checker,cyclePath+"/advance",new(){["stage"]=7});await host.Request(host.Checker,cyclePath+"/debt-deductions",new(){["action"]="confirm-all"});
            var posted=store.Read(c=>Store.List(c,"journal").Where(j=>j.S("payCycleId")==cycle.S("id")).ToList());if(posted.Any(j=>j.N("totalDebit")!=j.N("totalCredit")||j["lines"]!.AsArray().Any(l=>l!.N("debit")<0||l.N("credit")<0)))throw new InvalidOperationException("Real-file posting balance check failed.");
            sourceLines+=lines.Count;negativeCaptains+=lines.OfType<JsonObject>().Count(l=>l.S("lineType")!="company"&&l.N("invoiced")<0);journals+=posted.Count;completed++;files++;
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{files,sourceLines,negativeCaptains,completed,journals,negativeCaptainsWithUnverifiedScheme=negativeCaptains,actualSalaryAssignmentsVerified=0}));
    }
}
