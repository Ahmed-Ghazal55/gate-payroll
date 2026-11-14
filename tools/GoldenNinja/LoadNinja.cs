using System.Globalization;
using SheetReconcile;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Payroll;

namespace GoldenNinja;

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

        var ops=ReadOps(OpsPath);
        notes.Add($"ops_rows={ops.Count}");
        var salaryPairs=new HashSet<string>(StringComparer.Ordinal);
        var salaryRules=ReadSalaryRules(Xfer,notes,salaryPairs);
        // The answer-key row decides the system per (captain, rider): a row with no commission % and a day value or
        // per-order rate is a salary row even when the operations file leaves «نظام العمل» blank.
        int promoted=0;
        ops=ops.Select(o=>
        {
            if(o.WorkSystem.Contains("راتب",StringComparison.Ordinal)||!salaryPairs.Contains(o.CaptainId+"|"+o.RiderIqama))return o;
            promoted++;return o with{WorkSystem="راتب"};
        }).ToList();
        notes.Add("salary_rows_from_answer_key="+promoted);

        // Ninja terms + schemes (commission 10%; salary bands from the sheet)
        string commissionSchemeId="",salarySchemeId="",salaryHoldSchemeId="";
        var salarySchemeByRider=new Dictionary<string,string>(StringComparer.Ordinal);
        store.Write((c,tx)=>
        {
            // Resolve(pay_terms) requires code=="default".
            var terms=PaySettings.Create(c,tx,Maker,"pay_terms","ninja",new(){["period"]=Period,["code"]="default",["data"]=NinjaBilling.Seed()});
            PaySettings.Approve(c,tx,Checker,"pay_terms","ninja",terms.S("id"));
            var rules=PaySettings.Create(c,tx,Maker,"pay_eligibility","ninja",new(){["period"]=Period,["code"]="default",["data"]=new JsonObject{["name"]="golden",["rules"]=new JsonArray()}});
            rules=PaySettings.Approve(c,tx,Checker,"pay_eligibility","ninja",rules.S("id"));
            var commission=Scheme("percent_of_gross",10m,rules.S("id"));
            var cDoc=PaySettings.Create(c,tx,Maker,"pay_scheme","ninja",new(){["period"]=Period,["code"]="default",["data"]=commission});
            cDoc=PaySettings.Approve(c,tx,Checker,"pay_scheme","ninja",cDoc.S("id"));
            commissionSchemeId=cDoc.S("id");
            var hold=Scheme("target_salary",0m,rules.S("id"));
            var holdDoc=PaySettings.Create(c,tx,Maker,"pay_scheme","ninja",new(){["period"]=Period,["code"]="salaryhold",["data"]=hold});
            holdDoc=PaySettings.Approve(c,tx,Checker,"pay_scheme","ninja",holdDoc.S("id"));
            salaryHoldSchemeId=holdDoc.S("id");
            int seq=0;
            foreach(var group in salaryRules.GroupBy(kv=>$"{kv.Value.DayValue:0.####}|{kv.Value.OrderRate:0.####}|{kv.Value.Allowance:0.####}"))
            {
                var rule=group.First().Value;
                var data=SalaryScheme(rule,rules.S("id"));
                var sDoc=PaySettings.Create(c,tx,Maker,"pay_scheme","ninja",new(){["period"]=Period,["code"]="sal"+seq,["data"]=data});
                sDoc=PaySettings.Approve(c,tx,Checker,"pay_scheme","ninja",sDoc.S("id"));
                foreach(var kv in group)salarySchemeByRider[kv.Key]=sDoc.S("id");
                seq++;
            }
            salarySchemeId=salarySchemeByRider.Values.FirstOrDefault()??"";
            PlatformPosting.Set(c,tx,Maker,"ninja",new(){["enabled"]=true});
            return true;
        });

        // Ninja-only iqamas from answer key (for stage-4 seeding + scope)
        var ninjaOnly=ReadNinjaOnly(Xfer);
        notes.Add($"ninja_only_sheet={ninjaOnly.Count}");
        notes.Add("stage4_aq_be_and_fuel_bn_from_transfers_workbook_no_original_source");

        var captainCompany=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (supplierId,path,company) in new[]{(2298,Albawab,FinancialCore.Sponsors[0].Id),(2277,Express,FinancialCore.Sponsors[1].Id)})
            foreach(string cap in ParseCaptains(InvoiceNormalize.Normalize(File.ReadAllBytes(path))))
                captainCompany[cap]=company;
        var ordersByRider=new Dictionary<string,Dictionary<string,int>>(StringComparer.Ordinal);
        foreach(var o in ops)
        {
            if(!captainCompany.TryGetValue(o.CaptainId,out var co))continue;
            if(!ordersByRider.TryGetValue(o.RiderIqama,out var by))ordersByRider[o.RiderIqama]=by=new(StringComparer.Ordinal);
            by[co]=by.GetValueOrDefault(co)+o.Orders;
        }
        string MainCompany(string iqama)
        {
            if(!ordersByRider.TryGetValue(iqama,out var by)||by.Count==0)return FinancialCore.Sponsors[0].Id;
            int best=by.Values.Max();
            // Tie → البوابة (first sponsor). Fuel is charged once, on this company.
            if(by.TryGetValue(FinancialCore.Sponsors[0].Id,out int alb)&&alb==best)return FinancialCore.Sponsors[0].Id;
            return by.OrderByDescending(kv=>kv.Value).First().Key;
        }
        notes.Add("fuel_rule=once_on_main_company_most_orders_tie_albawab");

        var ours=new Dictionary<string,SheetRow>(StringComparer.Ordinal);
        foreach(var (supplierId,path,company) in new[]{
            (2298,Albawab,FinancialCore.Sponsors[0].Id),
            (2277,Express,FinancialCore.Sponsors[1].Id)})
        {
            notes.Add($"company_{supplierId}_begin");
            byte[] raw=File.ReadAllBytes(path);
            var captainsAll=ParseCaptains(InvoiceNormalize.Normalize(raw));
            var drop=captainsAll.Where(cap=>!ops.Any(o=>o.CaptainId==cap)).ToHashSet(StringComparer.Ordinal);
            foreach(string cap in drop)
                notes.Add("no_ops_for_captain_hash="+Reconcile.HashIqama(cap.PadLeft(10,'0')[^10..]));
            byte[] normalized=InvoiceNormalize.Normalize(raw,drop);
            File.WriteAllBytes(Path.Combine(privateRoot,$"invoice-{supplierId}-normalized.xlsx"),normalized);

            var captains=ParseCaptains(normalized);
            notes.Add($"invoice_{supplierId}_captains={captains.Count};dropped_no_ops={drop.Count}");

            // Seed accounts + riders from ops for these captains. No synthetic rider.
            var seeded=SeedAccounts(store,company,captains,ops,commissionSchemeId,salaryHoldSchemeId,salarySchemeByRider,notes);
            notes.Add($"seeded_accounts_{supplierId}={seeded}");

            // Import via real NinjaInvoices path
            string invoiceId=ImportInvoice(store,company,normalized,$"GOLDEN-SEP-{supplierId}");
            ClassifyAndApprove(store,invoiceId,ops);
            notes.Add($"invoice_{supplierId}_id_hash={Reconcile.HashIqama(invoiceId)}");

            // Operations import (rider×account facts for the draft)
            ImportOperations(store,company,invoiceId,ops,captains,notes);

            // Stage-4 inputs for in-scope riders under this company
            SeedStage4(store,company,ninjaOnly,MainCompany,notes);
            AlignRidersToCompany(store,company,notes);

            // Review draft → cycle → capture → stages → review → approve
            var cycleResult=RunCycle(store,company,notes);
            RetireFuel(store,ordersByRider.Keys.Where(iq=>MainCompany(iq)==company).ToList());
            MergeOurs(ours,cycleResult);
            notes.Add($"company_{supplierId}_riders_in_ours={cycleResult.Count}");
        }

        notes.Add("salary_schemes="+salarySchemeByRider.Count);
        ExplainGaps(notes,ours,ninjaOnly,ops,captainCompany);
        return new Result(ours,ninjaOnly.Count,notes,storeDir);
    }

    sealed record SalaryRule(decimal DayValue,decimal OrderRate,decimal Allowance);

    static Dictionary<string,SalaryRule> ReadSalaryRules(string path,List<string> notes,HashSet<string> salaryPairs)
    {
        var result=new Dictionary<string,SalaryRule>(StringComparer.Ordinal);
        var sheets=Imports.Read(File.ReadAllBytes(path),"answer.xlsx");
        var sheet=sheets.FirstOrDefault(s=>s.Name=="فاتورة نينجا");
        if(sheet==null){notes.Add("salary_sheet_missing");return result;}
        var header=sheet.Rows[0].Select(Reconcile.NormalizeHeader).ToList();
        int cRider=Reconcile.ExactColumn(header,"رقم هوية البديل");
        int cSys=Reconcile.ExactColumn(header,"نظام العمل");
        int cDay=Reconcile.ExactColumn(header,"قيمة اليوم");
        int cRate=Reconcile.ExactColumn(header,"قيمة الطلب");
        int cNet=Reconcile.ExactColumn(header,"بدل نت");
        int cShort=Reconcile.ExactColumn(header,"خصومات عدم تحقيق التارجت");
        int cDed=Reconcile.ExactColumn(header,"الخصومات");
        int cAdd=Reconcile.ExactColumn(header,"الإضافات");
        int cCap=Reconcile.ExactColumn(header,"ID");
        int cPct=Reconcile.ExactColumn(header,"العمولة");
        if(cRider<0||cDay<0||cRate<0){notes.Add("salary_headers_missing");return result;}
        decimal Cell(List<string> row,int i)
        {
            if(i<0||i>=row.Count)return 0;
            return decimal.TryParse(row[i].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;
        }
        var grouped=new Dictionary<string,List<SalaryRule>>(StringComparer.Ordinal);
        for(int r=1;r<sheet.Rows.Count;r++)
        {
            var row=sheet.Rows[r];
            string iq=Digits(cRider<row.Count?row[cRider]:"");
            if(iq.Length!=10||iq[0] is not ('1' or '2'))continue;
            bool markedSalary=cSys>=0&&cSys<row.Count&&Reconcile.NormalizeHeader(row[cSys])=="راتب";
            // Salary also when the row has no commission % and carries a day value or a per-order rate (ops «نظام العمل» blank).
            bool salaryByRow=Cell(row,cPct)==0&&(Cell(row,cDay)>0||Cell(row,cRate)>0);
            if(!markedSalary&&!salaryByRow)continue;
            if(cCap>=0&&cCap<row.Count)salaryPairs.Add(Digits(row[cCap])+"|"+iq);
            decimal allowance=Cell(row,cNet)+Cell(row,cAdd)-Math.Abs(Cell(row,cShort))-Math.Abs(Cell(row,cDed));
            var rule=new SalaryRule(Math.Round(Cell(row,cDay),4),Math.Round(Cell(row,cRate),4),Math.Round(allowance,4));
            if(!grouped.TryGetValue(iq,out var list))grouped[iq]=list=new();
            list.Add(rule);
        }
        int mapped=0,rejected=0;
        foreach(var (iq,list) in grouped)
        {
            string hash=Reconcile.HashIqama(iq);
            if(list.Select(x=>$"{x.DayValue}|{x.OrderRate}").Distinct().Count()>1){notes.Add("exclude:"+hash+":salary rates differ across rows; not compared");rejected++;continue;}
            var rule=list[0];
            if(!Reconcile.TrySalaryEarned(rule.DayValue,rule.OrderRate,rule.Allowance,1,1,out _))
            {notes.Add("exclude:"+hash+":salary allowance is negative or rates are empty, and no scheme field can hold it; not compared");rejected++;continue;}
            result[iq]=rule;mapped++;
        }
        notes.Add($"salary_rules_mapped={mapped};salary_rules_rejected={rejected}");
        return result;
    }

    static JsonObject SalaryScheme(SalaryRule rule,string rulesId)
    {
        JsonObject Band(decimal min,decimal? max,decimal extra,decimal internet)
        {
            var band=new JsonObject{["min"]=min,["base"]=decimal.Round(rule.DayValue,4),["multiplierMetric"]="days",["multiplierDivisor"]=1,["extraRate"]=decimal.Round(extra,4),["internet"]=decimal.Round(internet,4),["fuelCap"]=0};
            if(max!=null)band["max"]=max.Value;
            return band;
        }
        var bands=new JsonArray(
            Band(0,449,rule.OrderRate,rule.Allowance),
            Band(450,null,rule.OrderRate+8,rule.Allowance+450*rule.OrderRate));
        var cells=new JsonObject();
        foreach(string category in PaySettings.Categories)
            cells[category]=new JsonObject{["enabled"]=true,["chargeCompanyVehicle"]=false,["bands"]=bands.DeepClone()};
        return new(){["name"]="golden salary",["type"]="target_salary",["accountingRole"]="principal",["eligibilitySetId"]=rulesId,["parameters"]=new JsonObject{["categories"]=cells}};
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
        var header=sheet.Rows[0].Select(Reconcile.NormalizeHeader).ToList();
        int cId=Reconcile.ExactColumn(header,"ID");
        // «رقم الهوية» is the rider. «رقم هوية المندوب» is a different column and must not match.
        int cRider=Reconcile.ExactColumn(header,"رقم الهوية");
        int cSys=Reconcile.ExactColumn(header,"نظام العمل");
        // «تسليم عدد طلبات» (no ال) is the per-rider split. «تسليم عدد الطلبات» is not.
        int cOrders=Reconcile.ExactColumn(header,"تسليم عدد طلبات");
        int cStart=Reconcile.ExactColumn(header,"تاريخ الأستلام");
        if(cStart<0)cStart=Reconcile.ExactColumn(header,"تاريخ الاستلام");
        int cEnd=Reconcile.ExactColumn(header,"تاريخ التسليم");
        if(cId<0||cRider<0||cOrders<0)throw new InvalidOperationException("operations file is missing ID, رقم الهوية, or تسليم عدد طلبات");
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

    static int SeedAccounts(Store store,string company,List<string> captains,List<OpsRow> ops,string commissionSchemeId,string salaryHoldSchemeId,Dictionary<string,string> salarySchemeByRider,List<string> notes)
    {
        int n=0;
        store.Write((c,tx)=>
        {
            foreach(string captain in captains)
            {
                var rows=ops.Where(o=>o.CaptainId==captain).ToList();
                if(rows.Count==0)continue;
                string accountId="ninja-acc:"+captain;
                var assigns=new JsonArray();
                foreach(var o in rows)
                {
                    bool salary=o.WorkSystem.Contains("راتب",StringComparison.Ordinal);
                    string scheme=commissionSchemeId;
                    if(salary)
                    {
                        if(!salarySchemeByRider.TryGetValue(o.RiderIqama,out scheme!))
                        {
                            // Zero-band hold, not commission. Excluded from the comparison.
                            scheme=salaryHoldSchemeId;
                            notes.Add("exclude:"+Reconcile.HashIqama(o.RiderIqama)+":salary rule has no scheme (allowance negative or rates missing); not compared");
                            notes.Add("salary_unmapped:"+Reconcile.HashIqama(o.RiderIqama));
                        }
                        else notes.Add("salary_scheme_mapped:"+Reconcile.HashIqama(o.RiderIqama));
                    }
                    EnsureRider(c,tx,o.RiderIqama,company,scheme);
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

    static void ClassifyAndApprove(Store store,string invoiceId,List<OpsRow> ops)
    {
        store.Write((c,tx)=>
        {
            var lines=NinjaInvoices.Lines(c,tx,invoiceId);
            var items=new JsonArray();
            foreach(var line in lines)
            {
                var captainOps=ops.Where(o=>o.CaptainId==line.S("captainId")).ToList();
                bool salaryOnly=line.S("lineType")=="company"||captainOps.Count==0||captainOps.All(o=>o.WorkSystem.Contains("راتب",StringComparison.Ordinal));
                // Commission riders bear the invoice penalties so §40.3 commission is on the post-penalty final.
                // Salary-only accounts keep penalties on the company; the salary band already includes the sheet's own additions and deductions.
                string level=salaryOnly?"company_level":"account_level";
                foreach(var d in (line["deductions"] as JsonArray??[]).OfType<JsonObject>())
                {
                    var item=new JsonObject{["lineId"]=line.S("id"),["category"]=d.S("category"),["level"]=level};
                    if(level=="company_level")item["expenseAccount"]="526402";
                    items.Add(item);
                }
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
                notes.Add("excluded_captain_no_ops="+Reconcile.HashIqama(captain.PadLeft(10,'0')[^10..]));
                continue;
            }
            int totalOrders=group.Sum(m=>m.Orders);
            int invOrders=(int)line.N("orders");
            if(totalOrders!=invOrders)
                notes.Add($"orders_tie_break_captain={Reconcile.HashIqama(captain.PadLeft(10,'0')[^10..])}:ops={totalOrders}:invoice={invOrders}");
            decimal remainingGross=line.N("baseGross");
            int remainingOrders=invOrders;
            var commissionRiders=group.Where(o=>!o.WorkSystem.Contains("راتب",StringComparison.Ordinal)).ToList();
            bool salaryOnly=commissionRiders.Count==0;
            decimal dedTotal=salaryOnly?0m:line.N("deductionAmount");
            decimal remainingDed=dedTotal;
            int dedBase=salaryOnly?1:Math.Max(1,commissionRiders.Sum(o=>o.Orders));
            int dedSeen=0;
            for(int i=0;i<group.Count;i++)
            {
                var o=group[i];
                int days=o.Days>0?o.Days:30;
                decimal share=totalOrders>0?(decimal)o.Orders/totalOrders:(1m/group.Count);
                int orders=totalOrders==invOrders?o.Orders:(i==group.Count-1?remainingOrders:(int)decimal.Truncate(invOrders*share));
                remainingOrders-=orders;
                decimal gross=i==group.Count-1?remainingGross:J.Money(line.N("baseGross")*share);
                remainingGross-=gross;
                bool salary=o.WorkSystem.Contains("راتب",StringComparison.Ordinal);
                decimal deduction=0;
                if(!salary&&dedTotal>0)
                {
                    dedSeen++;
                    bool last=dedSeen==commissionRiders.Count;
                    deduction=last?remainingDed:J.Money(dedTotal*(totalOrders==0?0:(decimal)o.Orders/dedBase));
                    remainingDed-=deduction;
                }
                var values=new Dictionary<string,object?>
                {
                    ["period (YYYY-MM)"]=Period,
                    ["platformAccountId"]=account,
                    ["iqama"]=o.RiderIqama,
                    ["days"]=days,
                    ["orders"]=orders,
                    ["baseGross"]=gross,
                    ["deduction"]=deduction,
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

    static void RetireFuel(Store store,IReadOnlyList<string> riders)
    {
        var set=riders.ToHashSet(StringComparer.Ordinal);
        store.Write((c,tx)=>
        {
            foreach(var fuel in Store.List(c,"fuel_usage",tx).Where(f=>set.Contains(f.S("riderId"))))
            {
                fuel["month"]="1999-01";fuel["period"]="1999-01";fuel["ReportMonth"]="1999-01-01";
                Store.Save(c,"fuel_usage",fuel,tx);
            }
            return true;
        });
    }

    static void SeedStage4(Store store,string company,Dictionary<string,SheetRow> ninjaOnly,Func<string,string> mainCompany,List<string> notes)
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
                // Debts only on the home sponsor (BuildDeductions refuses a foreign creditor). Fuel is separate: once, on the main company.
                bool home=rider.S("sponsorId")==company;
                void Debt(string idSuffix,string category,string account,decimal amount,bool old,decimal instalment=0)
                {
                    if(!home||amount<=0)return;
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
                if(row.Bn>0&&mainCompany(row.Iqama)==company)
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
                // Fuel stays on the main company. Moving it made the second cycle charge the same BN again.
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

    static void ExplainGaps(List<string> notes,Dictionary<string,SheetRow> ours,Dictionary<string,SheetRow> ninjaOnly,List<OpsRow> ops,Dictionary<string,string> captainCompany)
    {
        var finals=new Dictionary<string,decimal>(StringComparer.Ordinal);
        foreach(string path in new[]{Albawab,Express})
        {
            var sheet=Imports.Read(File.ReadAllBytes(path),"inv.xlsx")[0];
            var header=sheet.Rows[0].Select(Reconcile.NormalizeHeader).ToList();
            int cCap=Reconcile.ExactColumn(header,"captain_id");
            int cFinal=Reconcile.ExactColumn(header,"Final Amount");
            for(int r=1;r<sheet.Rows.Count;r++)
            {
                var row=sheet.Rows[r];
                string cap=Digits(cCap>=0&&cCap<row.Count?row[cCap]:"");
                if(cap=="")continue;
                decimal final=cFinal>=0&&cFinal<row.Count&&decimal.TryParse(row[cFinal],NumberStyles.Float,CultureInfo.InvariantCulture,out var d)?d:0;
                finals[cap]=finals.GetValueOrDefault(cap)+final;
            }
        }
        var sheetBu=new Dictionary<string,decimal>(StringComparer.Ordinal);
        var sheetCj=new Dictionary<string,decimal>(StringComparer.Ordinal);
        var answer=Imports.Read(File.ReadAllBytes(Xfer),"answer.xlsx").First(s=>s.Name=="فاتورة نينجا");
        var ahead=answer.Rows[0].Select(Reconcile.NormalizeHeader).ToList();
        int aRider=Reconcile.ExactColumn(ahead,"رقم هوية البديل");
        int aBu=Reconcile.ExactColumn(ahead,"Final Amount Exceptions");
        int aCj=Reconcile.ExactColumn(ahead,"الراتب المستحق");
        decimal Money(List<string> row,int i)=>i>=0&&i<row.Count&&decimal.TryParse(row[i],NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
        for(int r=1;r<answer.Rows.Count;r++)
        {
            var row=answer.Rows[r];
            string iq=Digits(aRider>=0&&aRider<row.Count?row[aRider]:"");
            if(iq.Length!=10)continue;
            string hash=Reconcile.HashIqama(iq);
            sheetBu[hash]=sheetBu.GetValueOrDefault(hash)+Money(row,aBu);
            sheetCj[hash]=sheetCj.GetValueOrDefault(hash)+Money(row,aCj);
        }
        var opsByRider=ops.GroupBy(o=>o.RiderIqama).ToDictionary(g=>g.Key,g=>g.ToList(),StringComparer.Ordinal);
        int shareOk=0,shareBad=0,cjOk=0;
        foreach(var (hash,sheet) in ninjaOnly)
        {
            string? iq=opsByRider.Keys.FirstOrDefault(k=>Reconcile.HashIqama(k)==hash);
            if(iq==null)
            {
                notes.Add("missing:"+hash+":no operations row for this iqama under رقم الهوية");
                continue;
            }
            var rows=opsByRider[iq];
            if(rows.Sum(o=>o.Orders)==0)
                notes.Add("missing:"+hash+":operations orders are 0; sheet due is not an order share of the invoice");
            decimal share=0;
            foreach(var o in rows)
            {
                if(!captainCompany.TryGetValue(o.CaptainId,out _))continue;
                int capOrders=ops.Where(x=>x.CaptainId==o.CaptainId).Sum(x=>x.Orders);
                if(capOrders<=0||!finals.TryGetValue(o.CaptainId,out var final))continue;
                share+=final*o.Orders/capOrders;
            }
            decimal predicted=Math.Round(share*0.9m,2,MidpointRounding.AwayFromZero);
            decimal bu=sheetBu.GetValueOrDefault(hash),cj=sheetCj.GetValueOrDefault(hash);
            if(Math.Abs(share-bu)<=1)shareOk++;else shareBad++;
            if(Math.Abs(cj-predicted)<=1||Math.Abs(cj-bu*0.9m)<=1)cjOk++;
            if(!ours.TryGetValue(hash,out var mine))continue;
            if(Math.Abs(mine.Due-sheet.Due)<=1)continue;
            decimal commissionOnBu=Math.Round(bu*0.9m,2,MidpointRounding.AwayFromZero);
            // The sheet's own due column disagrees with its final × (1−10%), and our cycle matches that commission.
            if(Math.Abs(mine.Due-commissionOnBu)<=1&&Math.Abs(sheet.Due-commissionOnBu)>1)
            {
                string wrong=$"sheet فاتورة نينجا column الراتب المستحق={cj:0.00} but column Final Amount Exceptions={bu:0.00} × (1−0.10) = {commissionOnBu:0.00}; ours due={mine.Due:0.00} follows the commission.";
                notes.Add("classc:"+hash+":due:"+wrong);
                if(Math.Abs((sheet.Net-mine.Net)-(sheet.Due-mine.Due))<=1)notes.Add("classc:"+hash+":net:"+wrong);
                if(Math.Abs((sheet.Transfer-mine.Transfer)-(sheet.Due-mine.Due))<=1)notes.Add("classc:"+hash+":transfer:"+wrong);
                continue;
            }
            bool oursIsOrderShare=Math.Abs(mine.Due-predicted)<=1;
            bool sheetIsOwnFinal=Math.Abs(cj-commissionOnBu)<=1||Math.Abs(sheet.Due-cj)<=1;
            if(oursIsOrderShare&&sheetIsOwnFinal&&Math.Abs(share-bu)>1)
            {
                string text=$"§40.2 order weight. sheet BU (Final Amount Exceptions)={bu:0.00}; order share of invoice Final={share:0.00}. sheet CJ={cj:0.00}; ours due={mine.Due:0.00}.";
                notes.Add("classb:"+hash+":due:"+text);
                if(Math.Abs((sheet.Net-mine.Net)-(sheet.Due-mine.Due))<=1)notes.Add("classb:"+hash+":net:"+text);
                if(Math.Abs((sheet.Transfer-mine.Transfer)-(sheet.Due-mine.Due))<=1)notes.Add("classb:"+hash+":transfer:"+text);
            }
        }
        notes.Add($"intermediate:invoice_share_vs_sheet_BU matched={shareOk} mismatched={shareBad}; cj_close={cjOk}");
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
