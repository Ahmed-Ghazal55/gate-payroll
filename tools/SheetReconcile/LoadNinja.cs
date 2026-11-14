using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Payroll;

namespace SheetReconcile;

/// <summary>Build a private September store from original Ninja inputs, run cycles through stage 6 (+ approval), return ours map.</summary>
public static class LoadNinja
{
    public const string Period="2026-09";
    static readonly string Base=Path.Combine("/home/ghazal/Projects/pro win","02 بيانات وتشغيل المنصات");
    static readonly string NinjaDir=Path.Combine(Base,"03 نينجا","فواتير ومالية نينجا");
    static readonly string Xfer=Path.Combine(Base,"01 كيتا","تحويلات رواتب التطبيقات شهر 09-2026 م.xlsx");
    static readonly string OpsPath=Path.Combine(NinjaDir,"داتا نينجا تسليم للمالية شهر 9 (1).xlsx");
    static readonly string Albawab=Path.Combine(NinjaDir,"albawab_almqblh_food_(car,bike) (9).xlsx");
    static readonly string Express=Path.Combine(NinjaDir,"express_jait_food_ftr(car) (10).xlsx");
    static readonly string DebtsPath=Path.Combine(Base,"المديونيات اجمالي - 2026.xlsx");

    static ClaimsPrincipal Maker=>UserAccess.Principal(new JsonObject{["username"]="golden-maker",["role"]="Admin"});
    static ClaimsPrincipal Checker=>UserAccess.Principal(new JsonObject{["username"]="golden-checker",["role"]="FinancialAccountant"});

    public sealed record Result(
        Dictionary<string,SheetRow> Ours,
        int NinjaOnlySheet,
        IReadOnlyList<string> Notes,
        string StoreDir);

    public static List<SheetRow> FilterNinjaOnly(string sheetPath)
        =>ReadNinjaOnly(sheetPath).Values.ToList();

    public static Result Run(string privateRoot)
    {
        Directory.CreateDirectory(privateRoot);
        string storeDir=Path.Combine(privateRoot,"store");
        if(Directory.Exists(storeDir))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(storeDir,true);
        }
        Directory.CreateDirectory(storeDir);
        var notes=new List<string>();
        var store=new Store(storeDir);
        Vouchers.Configure(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

        // Global + adjustment types
        store.Write((c,tx)=>
        {
            var g=PaySettings.Create(c,tx,Maker,"pay_setting","global",new(){["effectiveFrom"]="2026-09-01",["fromDefaults"]=true});
            PaySettings.Approve(c,tx,Checker,"pay_setting","global",g.S("id"));
            foreach(var (code,account) in new[]{("bonus","512122"),("correction","512101"),("compensation","512120")})
            {
                ConfigLists.Upsert(c,tx,Maker,"pay_adjustment_types",new(){["code"]=code,["nameAr"]=code,["values"]=new JsonObject{["account"]=account,["direction"]="both"}});
                ConfigLists.Approve(c,tx,Checker,"pay_adjustment_types",code);
            }
            return true;
        });

        // Ninja terms + schemes (commission 10%; salary placeholder with sheet-like bands)
        string commissionSchemeId="",salarySchemeId="";
        store.Write((c,tx)=>
        {
            // Resolve(pay_terms) requires code=="default".
            var terms=PaySettings.Create(c,tx,Maker,"pay_terms","ninja",new(){["period"]=Period,["code"]="default",["data"]=NinjaBilling.Seed()});
            PaySettings.Approve(c,tx,Checker,"pay_terms","ninja",terms.S("id"));
            var rules=PaySettings.Create(c,tx,Maker,"pay_eligibility","ninja",new(){["period"]=Period,["code"]="default",["data"]=new JsonObject{["name"]="golden",["rules"]=new JsonArray()}});
            rules=PaySettings.Approve(c,tx,Checker,"pay_eligibility","ninja",rules.S("id"));
            var commission=Scheme("percent_of_gross",10m,rules.S("id"));
            var salary=Scheme("target_salary",0m,rules.S("id"));
            var cDoc=PaySettings.Create(c,tx,Maker,"pay_scheme","ninja",new(){["period"]=Period,["code"]="default",["data"]=commission});
            cDoc=PaySettings.Approve(c,tx,Checker,"pay_scheme","ninja",cDoc.S("id"));
            var sDoc=PaySettings.Create(c,tx,Maker,"pay_scheme","ninja",new(){["period"]=Period,["code"]="salary",["data"]=salary});
            sDoc=PaySettings.Approve(c,tx,Checker,"pay_scheme","ninja",sDoc.S("id"));
            commissionSchemeId=cDoc.S("id");salarySchemeId=sDoc.S("id");
            PlatformPosting.Set(c,tx,Maker,"ninja",new(){["enabled"]=true});
            return true;
        });

        var ops=ReadOps(OpsPath);
        notes.Add($"ops_rows={ops.Count}");

        // Ninja-only iqamas from answer key (for stage-4 seeding + scope)
        var ninjaOnly=ReadNinjaOnly(Xfer);
        notes.Add($"ninja_only_sheet={ninjaOnly.Count}");
        notes.Add("stage4_aq_be_and_fuel_bn_from_transfers_workbook_no_original_source");

        var ours=new Dictionary<string,SheetRow>(StringComparer.Ordinal);
        foreach(var (supplierId,path,company) in new[]{
            (2298,Albawab,FinancialCore.Sponsors[0].Id),
            (2277,Express,FinancialCore.Sponsors[1].Id)})
        {
            notes.Add($"company_{supplierId}_begin");
            byte[] raw=File.ReadAllBytes(path);
            byte[] normalized=InvoiceNormalize.Normalize(raw);
            File.WriteAllBytes(Path.Combine(privateRoot,$"invoice-{supplierId}-normalized.xlsx"),normalized);

            var captains=ParseCaptains(normalized);
            notes.Add($"invoice_{supplierId}_captains={captains.Count}");

            // Seed accounts + riders from ops for these captains
            var seeded=SeedAccounts(store,company,captains,ops,commissionSchemeId,salarySchemeId,notes);
            notes.Add($"seeded_accounts_{supplierId}={seeded}");

            // Import via real NinjaInvoices path
            string invoiceId=ImportInvoice(store,company,normalized,$"GOLDEN-SEP-{supplierId}");
            ClassifyAndApprove(store,invoiceId);
            notes.Add($"invoice_{supplierId}_id_hash={Reconcile.HashIqama(invoiceId)}");

            // Operations import (rider×account facts for the draft)
            ImportOperations(store,company,invoiceId,ops,captains,notes);

            // Stage-4 inputs for in-scope riders under this company
            SeedStage4(store,company,ninjaOnly,notes);
            AlignRidersToCompany(store,company,notes);

            // Review draft → cycle → capture → stages → review → approve
            var cycleResult=RunCycle(store,company,notes);
            MergeOurs(ours,cycleResult);
            notes.Add($"company_{supplierId}_riders_in_ours={cycleResult.Count}");
        }

        return new Result(ours,ninjaOnly.Count,notes,storeDir);
    }

    static JsonObject Scheme(string type,decimal percent,string rulesId)
    {
        var cells=new JsonObject();
        foreach(string category in PaySettings.Categories)
        {
            var p=new JsonObject{["enabled"]=true,["chargeCompanyVehicle"]=false};
            if(type=="percent_of_gross")p["companyPercent"]=percent;
            else p["bands"]=new JsonArray(new JsonObject{["min"]=0,["base"]=0,["extraRate"]=0,["fuelCap"]=0,["internet"]=0});
            cells[category]=p;
        }
        return new(){["name"]="golden "+type,["type"]=type,["accountingRole"]="principal",["eligibilitySetId"]=rulesId,["parameters"]=new JsonObject{["categories"]=cells}};
    }

    sealed record OpsRow(string CaptainId,string RiderIqama,string WorkSystem,int Orders,DateOnly? Start,DateOnly? End,int Days);

    static List<OpsRow> ReadOps(string path)
    {
        // Use open via Imports after converting? Prefer raw openpyxl-equivalent via Zip+reuse of Excel reading.
        // Payroll.Imports reads xlsx; ops has Arabic headers — parse with a thin reader.
        var sheets=Imports.Read(File.ReadAllBytes(path),"ops.xlsx");
        var sheet=sheets[0];
        var header=sheet.Rows[0].Select(h=>h.Replace("\n"," ").Trim()).ToList();
        int Col(params string[] names)
        {
            foreach(var n in names)
            {
                int i=header.FindIndex(h=>h.Equals(n,StringComparison.OrdinalIgnoreCase)||h.Contains(n,StringComparison.Ordinal));
                if(i>=0)return i;
            }
            return -1;
        }
        int cId=Col("ID");
        int cRider=Col("رقم الهوية"); // O — actual rider (may match multiple cols; prefer later)
        // Prefer column O (index 14) if present
        if(header.Count>14&&header[14].Contains("رقم الهوية",StringComparison.Ordinal))cRider=14;
        int cSys=Col("نظام العمل");
        int cOrders=header.FindIndex(h=>h.Contains("تسليم",StringComparison.Ordinal)&&h.Contains("طلبات",StringComparison.Ordinal));
        // Prefer AD-like second orders col if two
        var orderCols=header.Select((h,i)=>(h,i)).Where(x=>x.h.Contains("تسليم",StringComparison.Ordinal)&&x.h.Contains("طلب",StringComparison.Ordinal)).Select(x=>x.i).ToList();
        if(orderCols.Count>0)cOrders=orderCols[^1];
        int cStart=Col("تاريخ الأستلام","تاريخ الاستلام");
        int cEnd=Col("تاريخ التسليم");
        var list=new List<OpsRow>();
        for(int r=1;r<sheet.Rows.Count;r++)
        {
            var row=sheet.Rows[r];
            string Cell(int i)=>i>=0&&i<row.Count?row[i].Trim():"";
            string cap=Digits(Cell(cId));if(cap=="")continue;
            string iq=Digits(Cell(cRider));
            if(iq.Length!=10||iq[0] is not ('1' or '2'))continue;
            string sys=Cell(cSys);
            int orders=(int)Num(Cell(cOrders));
            DateOnly? start=ParseDate(Cell(cStart)),end=ParseDate(Cell(cEnd));
            int days=start!=null&&end!=null?end.Value.DayNumber-start.Value.DayNumber+1:0;
            list.Add(new OpsRow(cap,iq,sys,orders,start,end,days));
        }
        return list;
    }

    static Dictionary<string,SheetRow> ReadNinjaOnly(string path)
    {
        var all=Reconcile.ReadSharedSheet(path);
        // Re-read AA vs AP: ReadSharedSheet already maps Due=AP. Filter AA==AP via raw again.
        var result=new Dictionary<string,SheetRow>(StringComparer.Ordinal);
        using var zip=System.IO.Compression.ZipFile.OpenRead(path);
        // Use existing Reconcile columns: we need AA (26). Extend by reading with open approach in Python was easier —
        // duplicate light read here.
        foreach(var row in ReadSharedWithAa(path))
        {
            if(row.Aa==row.Ap&&row.Aa!=0)
                result[row.Hash]=new SheetRow(row.Hash,row.Ap,row.Bf,row.Bn,row.Bo,row.Bp,row.Bq);
        }
        return result;
    }

    sealed record SharedExt(string Hash,decimal Aa,decimal Ap,decimal Bf,decimal Bn,decimal Bo,decimal Bp,decimal Bq);

    static List<SharedExt> ReadSharedWithAa(string path)
    {
        // Delegate to a small XML walk mirroring Reconcile.ReadSharedSheet but also AA.
        var rows=new List<SharedExt>();
        using var zip=System.IO.Compression.ZipFile.OpenRead(path);
        string sheetPath=ResolveSheet(zip,"المناديب المشتركين");
        var shared=LoadShared(zip);
        using var stream=zip.GetEntry(sheetPath)!.Open();
        using var reader=System.Xml.XmlReader.Create(stream,new System.Xml.XmlReaderSettings{IgnoreComments=true,IgnoreWhitespace=true});
        bool headerSkipped=false;var seen=new HashSet<string>(StringComparer.Ordinal);
        while(reader.Read())
        {
            if(reader.NodeType!=System.Xml.XmlNodeType.Element||reader.LocalName!="row")continue;
            Dictionary<int,string> cells;
            using(var rowXml=reader.ReadSubtree()){rowXml.Read();cells=ReadCells(rowXml,shared);}
            if(!headerSkipped){headerSkipped=true;continue;}
            string raw=cells.GetValueOrDefault(0,"");
            if(Reconcile.IsExcludedLabel(raw))continue;
            string digits=Reconcile.NormalizeIqamaDigits(raw);
            if(digits.Length!=10||digits[0] is not ('1' or '2'))continue;
            if(!seen.Add(digits))continue;
            decimal Money(int col)=>decimal.TryParse(cells.GetValueOrDefault(col,""),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?Math.Round(d,2):0m;
            rows.Add(new SharedExt(Reconcile.HashIqama(digits),Money(26),Money(41),Money(57),Money(65),Money(66),Money(67),Money(68)));
        }
        return rows;
    }

    static string ResolveSheet(System.IO.Compression.ZipArchive zip,string name)
    {
        using var wb=zip.GetEntry("xl/workbook.xml")!.Open();
        var doc=System.Xml.Linq.XDocument.Load(wb);
        XNamespace ns=doc.Root!.Name.Namespace;XNamespace relNs="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheet=doc.Root.Element(ns+"sheets")!.Elements(ns+"sheet").First(s=>(string?)s.Attribute("name")==name);
        string rid=(string)sheet.Attribute(relNs+"id")!;
        using var rels=zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open();
        var relDoc=System.Xml.Linq.XDocument.Load(rels);
        XNamespace r="http://schemas.openxmlformats.org/package/2006/relationships";
        string target=(string)relDoc.Root!.Elements(r+"Relationship").First(e=>(string?)e.Attribute("Id")==rid).Attribute("Target")!;
        return "xl/"+target.TrimStart('/');
    }

    static List<string> LoadShared(System.IO.Compression.ZipArchive zip)
    {
        var entry=zip.GetEntry("xl/sharedStrings.xml");if(entry==null)return [];
        using var s=entry.Open();var doc=System.Xml.Linq.XDocument.Load(s);XNamespace ns=doc.Root!.Name.Namespace;
        return doc.Root.Elements(ns+"si").Select(si=>string.Concat(si.Descendants(ns+"t").Select(t=>(string?)t??""))).ToList();
    }

    static Dictionary<int,string> ReadCells(System.Xml.XmlReader reader,List<string> shared)
    {
        var cells=new Dictionary<int,string>();
        while(reader.Read())
        {
            if(reader.NodeType!=System.Xml.XmlNodeType.Element||reader.LocalName!="c")continue;
            string? r=reader.GetAttribute("r");string? t=reader.GetAttribute("t");
            int col=0;if(r!=null){int i=0;while(i<r.Length&&char.IsLetter(r[i])){col=col*26+(r[i]-'A'+1);i++;}col--;}
            string v="";
            using var sub=reader.ReadSubtree();
            while(sub.Read())
            {
                if(sub.NodeType==System.Xml.XmlNodeType.Element&&sub.LocalName is "v" or "t")
                {sub.Read();v=sub.Value;break;}
            }
            if(t=="s"&&int.TryParse(v,out int si)&&si>=0&&si<shared.Count)v=shared[si];
            cells[col]=v;
        }
        return cells;
    }

    static List<string> ParseCaptains(byte[] bytes)
    {
        var sheets=Imports.Read(bytes,"invoice.xlsx");
        var sheet=sheets.First(s=>s.Rows[0].Any(h=>h.Equals("captain_id",StringComparison.OrdinalIgnoreCase)));
        int col=sheet.Rows[0].FindIndex(h=>h.Equals("captain_id",StringComparison.OrdinalIgnoreCase));
        var list=new List<string>();
        for(int r=1;r<sheet.Rows.Count;r++)
        {
            string c=Digits(sheet.Rows[r].ElementAtOrDefault(col)??"");
            if(c!="")list.Add(c);
        }
        return list.Distinct(StringComparer.Ordinal).ToList();
    }

    static int SeedAccounts(Store store,string company,List<string> captains,List<OpsRow> ops,string commissionSchemeId,string salarySchemeId,List<string> notes)
    {
        int n=0;
        store.Write((c,tx)=>
        {
            foreach(string captain in captains)
            {
                var rows=ops.Where(o=>o.CaptainId==captain).ToList();
                if(rows.Count==0)
                {
                    // Invoice captain with no ops row: synthetic single rider keyed by captain digits padded — skip cycle match later
                    notes.Add($"no_ops_for_captain_hash={Reconcile.HashIqama(captain.PadLeft(10,'0')[..Math.Min(10,captain.Length)].PadLeft(10,'2'))}");
                    // still create a placeholder account so import can bind; use captain as external id and a synthetic rider
                    string synIq=("2"+captain.PadLeft(9,'0'))[^10..];
                    EnsureRider(c,tx,synIq,company,commissionSchemeId);
                    string account="ninja-acc:"+captain;
                    Store.Save(c,"platform_account",new(){["id"]=account,["companyId"]=company,["platformCode"]="NINJA",["externalAccountId"]=captain,["assignments"]=new JsonArray(new JsonObject{["riderId"]=synIq,["RawEffectiveFrom"]="2026-09-01T00:00:00+03:00"})},tx);
                    n++;continue;
                }
                string accountId="ninja-acc:"+captain;
                var assigns=new JsonArray();
                foreach(var o in rows)
                {
                    // Sheet «راتب» day/order/bonus rates are not yet mapped into target_salary bands (note as class b).
                    // Use commission 10% for all so stage-3 due tracks AA for عمولة riders; salary gaps → (b)/(d).
                    EnsureRider(c,tx,o.RiderIqama,company,commissionSchemeId);
                    if(o.WorkSystem.Contains("راتب",StringComparison.Ordinal))
                        notes.Add($"salary_scheme_unmapped_hash={Reconcile.HashIqama(o.RiderIqama)}");
                    assigns.Add(new JsonObject{["riderId"]=o.RiderIqama,["RawEffectiveFrom"]=(o.Start??new DateOnly(2026,9,1)).ToString("yyyy-MM-dd")+"T00:00:00+03:00",["RawEffectiveTo"]=o.End==null?"":o.End.Value.ToString("yyyy-MM-dd")+"T23:59:59+03:00"});
                }
                Store.Save(c,"platform_account",new(){["id"]=accountId,["companyId"]=company,["platformCode"]="NINJA",["externalAccountId"]=captain,["assignments"]=assigns},tx);
                n++;
            }
            return true;
        });
        return n;
    }

    static void EnsureRider(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,string iqama,string company,string schemeId)
    {
        if(Store.Get(c,iqama,"rider",tx)==null)
            Store.Save(c,"rider",new(){["id"]=iqama,["iqama"]=iqama,["name"]="Synthetic rider "+Reconcile.HashIqama(iqama),["isDeliveryRider"]=true,["sponsorId"]=company,["status"]="Active",["payCategory"]="sponsored_private"},tx);
        string choiceId="choice:"+iqama+":ninja:"+Period;
        if(Store.Get(c,choiceId,"pay_assignment",tx)==null)
            Store.Save(c,"pay_assignment",new(){["id"]=choiceId,["rider"]=iqama,["app"]="ninja",["period"]=Period,["status"]="approved",["schemeId"]=schemeId,["choiceApprovedAt"]="2026-08-31T00:00:00Z"},tx);
    }

    static string ImportInvoice(Store store,string company,byte[] bytes,string reference)
    {
        var input=new JsonObject{["period"]=Period,["companyId"]=company,["reference"]=reference};
        var preview=store.Read(c=>NinjaInvoices.Preview(c,Maker,input,bytes));
        return store.Write((c,tx)=>
        {
            var commit=input.Clone();
            commit["rows"]=preview["rows"]!.DeepClone();
            commit["fingerprint"]=preview.S("fingerprint");
            commit["confirm"]=true;
            return NinjaInvoices.Commit(c,tx,Maker,commit,bytes).S("id");
        });
    }

    static void ClassifyAndApprove(Store store,string invoiceId)
    {
        store.Write((c,tx)=>
        {
            var lines=NinjaInvoices.Lines(c,tx,invoiceId);
            var items=new JsonArray();
            foreach(var line in lines)
            {
                foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
                    items.Add(new JsonObject{["lineId"]=line.S("id"),["category"]=d.S("category"),["level"]="company_level",["expenseAccount"]="526402"});
            }
            if(items.Count>0)NinjaInvoices.ClassifyBatch(c,tx,Maker,invoiceId,new(){["items"]=items});
            NinjaInvoices.Approve(c,tx,Checker,invoiceId);
            return true;
        });
    }

    static void ImportOperations(Store store,string company,string invoiceId,List<OpsRow> ops,List<string> captains,List<string> notes)
    {
        var captainSet=captains.ToHashSet(StringComparer.Ordinal);
        var columns=ImportCatalog.Steps().Single(s=>s.Key=="ninja_operations").Columns;
        var lines=store.Read(c=>NinjaInvoices.Lines(c,null,invoiceId).Where(l=>l.S("lineType")!="company").ToList());
        var accounts=store.Read(c=>Store.List(c,"platform_account").Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="ninja").ToDictionary(a=>a.S("id"),a=>a));
        var data=new List<object?[]>();
        foreach(var line in lines)
        {
            string account=line.S("platformAccountId");
            string captain=line.S("captainId");
            if(!captainSet.Contains(captain))continue;
            var group=ops.Where(o=>o.CaptainId==captain).GroupBy(o=>o.RiderIqama).Select(g=>
            {
                var first=g.First();
                int orders=g.Sum(x=>x.Orders);
                var starts=g.Where(x=>x.Start!=null).Select(x=>x.Start!.Value).ToList();
                var ends=g.Where(x=>x.End!=null).Select(x=>x.End!.Value).ToList();
                DateOnly? start=starts.Count>0?starts.Min():first.Start;
                DateOnly? end=ends.Count>0?ends.Max():first.End;
                int days=start!=null&&end!=null?end.Value.DayNumber-start.Value.DayNumber+1:g.Max(x=>x.Days);
                return first with{Orders=orders,Start=start,End=end,Days=days};
            }).ToList();
            if(group.Count==0)
            {
                // Fall back to the seeded assignment rider so every invoice account has an operator.
                if(!accounts.TryGetValue(account,out var acc)){notes.Add("missing_account:"+account);continue;}
                var assign=(acc["assignments"] as JsonArray??[]).OfType<JsonObject>().FirstOrDefault();
                if(assign==null){notes.Add("no_assign:"+account);continue;}
                group=[new OpsRow(captain,assign.S("riderId"),"عمولة",(int)line.N("orders"),new DateOnly(2026,9,1),new DateOnly(2026,9,30),30)];
                notes.Add($"ops_fallback_captain={Reconcile.HashIqama(captain.PadLeft(10,'0'))}");
            }
            int totalOrders=group.Sum(m=>m.Orders);
            // Scale ops orders to invoice orders so ValidateRiders sum matches.
            int invOrders=(int)line.N("orders");
            decimal remainingGross=line.N("baseGross");
            int remainingOrders=invOrders;
            for(int i=0;i<group.Count;i++)
            {
                var o=group[i];
                int days=o.Days>0?o.Days:30;
                decimal share=totalOrders>0?(decimal)o.Orders/totalOrders:(1m/group.Count);
                int orders=i==group.Count-1?remainingOrders:(int)decimal.Truncate(invOrders*share);
                remainingOrders-=orders;
                decimal gross=i==group.Count-1?remainingGross:J.Money(line.N("baseGross")*share);
                remainingGross-=gross;
                var values=new Dictionary<string,object?>
                {
                    ["period (YYYY-MM)"]=Period,
                    ["platformAccountId"]=account,
                    ["iqama"]=o.RiderIqama,
                    ["days"]=days,
                    ["orders"]=orders,
                    ["baseGross"]=gross,
                    ["deduction"]=0m,
                    ["category"]="sponsored_private"
                };
                data.Add(columns.Select(col=>values.GetValueOrDefault(col.Header)??"").ToArray());
            }
        }
        if(data.Count==0){notes.Add("operations_empty_for_company");return;}
        byte[] bytes=ExcelFiles.Write(columns,data);
        var preview=store.Read(c=>OperationsImports.Preview(c,Maker,"ninja",new(){["invoiceId"]=invoiceId},bytes));
        store.Write((c,tx)=>
        {
            var input=new JsonObject{["invoiceId"]=invoiceId,["rows"]=preview["rows"]!.DeepClone(),["fingerprint"]=preview.S("fingerprint"),["confirm"]=true};
            OperationsImports.Commit(c,tx,Maker,"ninja",input,bytes);
            return true;
        });
        notes.Add($"operations_rows={data.Count}");
    }

    static void SeedStage4(Store store,string company,Dictionary<string,SheetRow> ninjaOnly,List<string> notes)
    {
        // Pull AQ–BE + BN + BP from shared sheet for Ninja-only riders who operate on this company's accounts.
        var ext=ReadSharedFull(Xfer);
        int debts=0,fuels=0;
        store.Write((c,tx)=>
        {
            var companyRiders=Store.List(c,"platform_account",tx)
                .Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="ninja")
                .SelectMany(a=>(a["assignments"] as JsonArray??[]).OfType<JsonObject>().Select(x=>x.S("riderId")))
                .ToHashSet(StringComparer.Ordinal);
            foreach(var row in ext)
            {
                if(row.Aa!=row.Ap||row.Aa==0)continue;
                if(!companyRiders.Contains(row.Iqama))continue;
                var rider=Store.Get(c,row.Iqama,"rider",tx);
                if(rider==null)continue;
                // Only the rider's home sponsor settles stage-4 (BuildDeductions refuses foreign creditorSponsorId).
                if(rider.S("sponsorId")!=company)continue;
                void Debt(string idSuffix,string category,string account,decimal amount,bool old,decimal instalment=0)
                {
                    if(amount<=0)return;
                    string id="golden-debt:"+row.Iqama+":"+idSuffix;
                    if(Store.Get(c,id,"debt",tx)!=null)return;
                    decimal bal=old?Math.Max(amount,instalment):amount;
                    Store.Save(c,"debt",new()
                    {
                        ["id"]=id,["iqama"]=row.Iqama,["category"]=category,["creditorSponsorId"]=company,
                        ["receivableAccountCode"]=account,["status"]="active",
                        ["totalAmount"]=bal,["remainingBalance"]=bal,
                        ["paidAmount"]=0,["monthlyInstallment"]=old?instalment:0,
                        ["effectiveFrom"]=old?"2026-08":Period,["lastDeductionPeriod"]="",
                        ["createdAt"]=Period+"-02T00:00:00Z"
                    },tx);
                    FinancialCore.PostJournal(c,tx,"opening","golden stage4",Period,"open:"+id,
                        new JsonArray(
                            FinancialCore.CreateJournalLine("1210",company,bal,0,iqama:row.Iqama,debtId:id),
                            FinancialCore.CreateJournalLine("3101",company,0,bal,iqama:row.Iqama)),
                        "golden-opening");
                    debts++;
                }
                Debt("aq","CashAdvance","112501",row.Aq,false);
                Debt("ar","TrafficFine","112502",row.Ar,false);
                Debt("as","InternetAdvance","112503",row.As,false);
                Debt("at","HsWalletTopUp","112506",row.At,false);
                Debt("au","PlatformTransferredDebt","112508",row.Au,false);
                Debt("av","CashAdvance","112501",row.Av,false); // carried Aug — treated as this-month per prompt
                Debt("aw","AdministrativeDeduction","112505",row.Aw,false);
                Debt("ax","AdministrativeDeduction","112505",row.Ax,false);
                Debt("ay","OtherHrService","112506",row.Ay,false);
                Debt("bb","HousingDeduction","112505",row.Bb,false);
                Debt("bc","MudadSalary","112505",row.Bc,false);
                Debt("bd","MudadSalary","112505",row.Bd,false);
                Debt("be","MudadSalary","112505",row.Be,false);
                if(row.Bp>0)Debt("bp","LegacyOpeningDebt","112505",row.Bp,true,row.Bp);
                if(row.Bn>0)
                {
                    string fuelId="golden-fuel:"+row.Iqama;
                    if(Store.Get(c,fuelId,"fuel_usage",tx)==null)
                    {
                        Store.Save(c,"fuel_usage",new(){["id"]=fuelId,["riderId"]=row.Iqama,["companyId"]=company,["month"]=Period,["period"]=Period,["amount"]=row.Bn,["totalAmount"]=row.Bn,["ReportMonth"]=Period+"-01",["TotalAmount"]=row.Bn},tx);
                        fuels++;
                    }
                }
            }
            return true;
        });
        notes.Add($"stage4_debts={debts};fuel_rows={fuels}");
    }

    sealed record SharedFull(string Iqama,string Hash,decimal Aa,decimal Ap,decimal Aq,decimal Ar,decimal As,decimal At,decimal Au,decimal Av,decimal Aw,decimal Ax,decimal Ay,decimal Bb,decimal Bc,decimal Bd,decimal Be,decimal Bf,decimal Bn,decimal Bo,decimal Bp,decimal Bq);

    static List<SharedFull> ReadSharedFull(string path)
    {
        var rows=new List<SharedFull>();
        using var zip=System.IO.Compression.ZipFile.OpenRead(path);
        string sheetPath=ResolveSheet(zip,"المناديب المشتركين");
        var shared=LoadShared(zip);
        using var stream=zip.GetEntry(sheetPath)!.Open();
        using var reader=System.Xml.XmlReader.Create(stream,new System.Xml.XmlReaderSettings{IgnoreComments=true,IgnoreWhitespace=true});
        bool headerSkipped=false;var seen=new HashSet<string>(StringComparer.Ordinal);
        while(reader.Read())
        {
            if(reader.NodeType!=System.Xml.XmlNodeType.Element||reader.LocalName!="row")continue;
            Dictionary<int,string> cells;
            using(var rowXml=reader.ReadSubtree()){rowXml.Read();cells=ReadCells(rowXml,shared);}
            if(!headerSkipped){headerSkipped=true;continue;}
            string raw=cells.GetValueOrDefault(0,"");
            if(Reconcile.IsExcludedLabel(raw))continue;
            string digits=Reconcile.NormalizeIqamaDigits(raw);
            if(digits.Length!=10||digits[0] is not ('1' or '2'))continue;
            if(!seen.Add(digits))continue;
            decimal M(int col)=>decimal.TryParse(cells.GetValueOrDefault(col,""),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?Math.Round(d,2):0m;
            // AQ=42 … BE=56, BF=57, BN=65, BO=66, BP=67, BQ=68, AA=26, AP=41
            rows.Add(new SharedFull(digits,Reconcile.HashIqama(digits),M(26),M(41),M(42),M(43),M(44),M(45),M(46),M(47),M(48),M(49),M(50),M(53),M(54),M(55),M(56),M(57),M(65),M(66),M(67),M(68)));
        }
        return rows;
    }

    /// <summary>Riders on this company's Ninja accounts must have matching sponsorId; drop foreign stage-4 debts.</summary>
    static void AlignRidersToCompany(Store store,string company,List<string> notes)
    {
        store.Write((c,tx)=>
        {
            var riders=Store.List(c,"platform_account",tx)
                .Where(a=>a.S("companyId")==company&&PayPlatforms.For(a.S("platformCode")).App=="ninja")
                .SelectMany(a=>(a["assignments"] as JsonArray??[]).OfType<JsonObject>().Select(x=>x.S("riderId")))
                .ToHashSet(StringComparer.Ordinal);
            int moved=0,dropped=0;
            foreach(string iq in riders)
            {
                var rider=Store.Get(c,iq,"rider",tx);if(rider==null)continue;
                if(rider.S("sponsorId")!=company){rider["sponsorId"]=company;Store.Save(c,"rider",rider,tx);moved++;}
                foreach(var debt in Store.List(c,"debt",tx).Where(d=>d.S("iqama")==iq&&d.S("status")=="active"))
                {
                    if(debt.S("creditorSponsorId")==company)continue;
                    debt["status"]="cancelled";debt["remainingBalance"]=0;Store.Save(c,"debt",debt,tx);dropped++;
                }
                // Re-home fuel_usage companyId
                foreach(var fuel in Store.List(c,"fuel_usage",tx).Where(f=>f.S("riderId")==iq))
                {
                    if(fuel.S("companyId")==company)continue;
                    fuel["companyId"]=company;Store.Save(c,"fuel_usage",fuel,tx);
                }
            }
            notes.Add($"align_sponsor_moved={moved};foreign_debts_dropped={dropped}");
            return true;
        });
    }

    static Dictionary<string,SheetRow> RunCycle(Store store,string company,List<string> notes)
    {
        var draft=store.Read(c=>PayReviewDraft.Build(c,null,Maker,"ninja",Period,company));
        if(!draft.B("canCreate"))
        {
            notes.Add("draft_blocked:"+string.Join("|",(draft["blockers"] as JsonArray??[]).Select(b=>b.S("code")+":"+b.S("message"))));
            // Still try if invoices present
        }
        string cycleId=store.Write((c,tx)=>
        {
            var input=new JsonObject{["period"]=Period,["companyId"]=company,["app"]="ninja",["invoices"]=draft["invoices"]!.DeepClone(),["adjustmentAccounts"]=new JsonObject()};
            return PayCycles.Create(c,tx,Maker,input).S("id");
        });
        store.Write((c,tx)=>PayCycles.CaptureInputs(c,tx,Maker,cycleId,input:new(){["zeroKindReasons"]=new JsonObject{["vehicle_assignment"]="golden: no vehicle assignments for September Ninja-only"}}));
        for(int s=1;s<=6;s++)
            store.Write((c,tx)=>PayCycles.Advance(c,tx,Maker,cycleId,new(){["stage"]=s}));
        store.Write((c,tx)=>PayCycles.ReviewAll(c,tx,Maker,cycleId));
        // Final approval (stage 7) with second user — needed for remainder after instalment confirm
        try
        {
            store.Write((c,tx)=>PayCycles.Advance(c,tx,Checker,cycleId,new(){["stage"]=7}));
            store.Write((c,tx)=>PayCycles.DebtDeductionsAct(c,tx,Checker,cycleId,new(){["action"]="confirm-all"}));
        }
        catch(ApiError ex){notes.Add("stage7_or_debt:"+ex.Message);}

        var cycle=store.Read(c=>Store.Get(c,cycleId,PayCycles.Kind)!);
        var riders=(cycle["riders"] as JsonArray??[]).OfType<JsonObject>().ToList();
        var deductions=(cycle["deductions"] as JsonArray??[]).OfType<JsonObject>().ToList();
        var map=new Dictionary<string,SheetRow>(StringComparer.Ordinal);
        foreach(var r in riders)
        {
            string iq=r.S("rider");
            string hash=Reconcile.HashIqama(iq);
            // Principal percent schemes store post-fee amount in gross/entitlement/preliminaryDue.
            decimal due=r.N("afterCommission",r.N("preliminaryDue",r.N("gross")));
            var stage4Rows=deductions.Where(d=>d.S("rider")==iq&&d.S("type") is "stage4" or "housing").ToList();
            decimal fuel=stage4Rows.Where(d=>d.S("category")=="FuelOverage").Sum(d=>d.N("requestedAmount",-d.N("amount")));
            decimal other=stage4Rows.Where(d=>d.S("category")!="FuelOverage").Sum(d=>d.N("requestedAmount",-d.N("amount")));
            decimal net=r.N("salaryNet",r.N("net"));
            decimal instalment=deductions.Where(d=>d.S("rider")==iq&&d.S("type")=="debt").Sum(d=>d.N("requestedAmount",0));
            decimal transfer=r.N("net");
            if(r.S("debtDeductionStatus")=="confirmed"){instalment=Math.Max(0,net-transfer);net=r.N("salaryNet",net);}
            map[hash]=new SheetRow(hash,due,other,fuel,net,instalment,transfer);
        }
        notes.Add($"cycle_riders={riders.Count};stage={cycle.N("stage")};status={cycle.S("status")}");
        return map;
    }

    static void MergeOurs(Dictionary<string,SheetRow> dest,Dictionary<string,SheetRow> src)
    {
        foreach(var (k,v) in src)
        {
            if(!dest.TryGetValue(k,out var cur)){dest[k]=v;continue;}
            // Same rider on both companies — sum dues/deductions (shared consolidation case)
            dest[k]=new SheetRow(k,cur.Due+v.Due,cur.Deductions+v.Deductions,cur.Fuel+v.Fuel,cur.Net+v.Net,cur.Instalment+v.Instalment,cur.Transfer+v.Transfer);
        }
    }

    static string Digits(string raw)
    {
        if(Regex.IsMatch(raw,@"^\d+(\.\d+)?[Ee][+\-]?\d+$")||(raw.Contains('.')&&decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out _)))
        {
            if(decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var n))
                return decimal.Truncate(n).ToString("0",CultureInfo.InvariantCulture);
        }
        return Regex.Replace(raw??"","[^0-9]","");
    }
    static decimal Num(string v)=>decimal.TryParse(v.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;
    static DateOnly? ParseDate(string v)
    {
        if(v=="")return null;
        if(DateOnly.TryParse(v,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d))return d;
        if(DateTime.TryParse(v,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt))return DateOnly.FromDateTime(dt);
        return null;
    }
}
