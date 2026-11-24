using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-20a/b/c + WD1F: debt.debt + debt_movement + fin.journal in one TX through JournalPosting.
/// Checker is always a real authenticated principal (approvals area); never a typed Admin name.
/// Simulation (run_mode=1) is rejected from stored flags; payout_blocked allows accounting/debt but not disbursement payouts.</summary>
public static class DebtLedger
{
    public const byte DocDraft=1,DocSubmitted=2,DocApproved=3;
    static readonly HashSet<string> WaiverAccounts=new(StringComparer.Ordinal){"526601","526801","526802","526803","526804","526805","526806"};
    static readonly HashSet<string> AbsorbAccounts=new(StringComparer.Ordinal){"526602","526603"};
    static readonly HashSet<string> ReduceReasons=new(StringComparer.Ordinal){"تصحيح رصيد افتتاحي","خطأ إدخال","تسوية مراجعة","أخرى معتمدة"};
    static readonly Dictionary<string,string> PlatformWaiver=new(StringComparer.OrdinalIgnoreCase)
    {
        ["Keeta"]="526801",["HungerStation"]="526802",["Ninja"]="526803",["Amazon"]="526804",["Chefz"]="526805",["Jahez"]="526806"
    };

    public static void DemandDisburseAvailable()=>
        throw new ApiError("صرف مديونية جديدة عبر محرك المدفوعات غير متاح بعد؛ انتظر وحدة محرك المدفوعات.",409);

    public static Guid AccountId(DbConnection c,DbTransaction? tx,string code)
    {
        string mapped=code=="3101"?"331301":code;
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c AND is_postable=1 AND is_active=1",("@c",mapped));
        J.Need(id!=null,"حساب دليل الحسابات غير موجود أو غير قابل للترحيل: "+mapped,404);
        return id is Guid g?g:Guid.Parse(id!.ToString()!);
    }

    public static Guid RequireOpenPeriod(DbConnection c,DbTransaction? tx,Guid companyId,DateOnly periodMonth)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m AND state=1",
            ("@c",SchemaSql.GuidText(companyId)),("@m",Month(periodMonth)));
        J.Need(id!=null,"الفترة المحاسبية غير مفتوحة؛ المالية تفتح الفترات.",409);
        return id is Guid g?g:Guid.Parse(id!.ToString()!);
    }

    public static (byte RunMode,bool PayoutBlocked) ResolveMoneyFlags(DbConnection c,DbTransaction tx,Guid companyId,DateOnly periodMonth)
    {
        string company=SchemaSql.GuidText(companyId),pm=Month(periodMonth),period=periodMonth.ToString("yyyy-MM",CultureInfo.InvariantCulture);
        return BankPayrollFiles.ResolveStoredMoneyFlags(c,tx,(company,period,period));
    }

    public static void EnsureD11WaiverAccounts(DbConnection c,DbTransaction? tx)
    {
        SchemaFinAccount.Ensure(c,tx);
        string t=SchemaSql.Q(c,"fin","account");string now=SchemaSql.NowLiteral(c);
        void Seed(string code,string name,string parent,bool postable)
        {
            if(Convert.ToInt64(SchemaSql.Scalar(c,tx,$"SELECT COUNT(1) FROM {t} WHERE code=@c",("@c",code))!)>0)return;
            object? parentId=string.IsNullOrEmpty(parent)?DBNull.Value:SchemaSql.Scalar(c,tx,$"SELECT id FROM {t} WHERE code=@p",("@p",parent));
            // COA1 A2: 5268 is a sibling of 5266 under 526 (4-digit header), not a child of 5266.
            if(parent!=""&&parentId is null or DBNull)
            {
                string grand=parent switch{"5268"=>"526","526"=>"52",_=>""};
                Seed(parent,parent=="5268"?"إعفاءات المنصات":parent,grand,false);
                parentId=SchemaSql.Scalar(c,tx,$"SELECT id FROM {t} WHERE code=@p",("@p",parent));
            }
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {t}(id,code,parent_id,name_ar,account_type,nature,is_postable,status,is_active,company_scope,level_no,created_at)
                VALUES(@id,@code,@parent,@name,5,1,@post,1,1,0,@lvl,{now})
                """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@code",code),("@parent",parentId??DBNull.Value),("@name",name),
                ("@post",postable?1:0),("@lvl",code.Length<=4?4:6));
        }
        Seed("5268","إعفاءات المنصات","526",false);
        // One-off: if 5268 was previously parented under 5266, re-point to 526 and level 4.
        object? parent526=SchemaSql.Scalar(c,tx,$"SELECT id FROM {t} WHERE code='526'");
        if(parent526 is not null and not DBNull)
            SchemaSql.Exec(c,tx,$"UPDATE {t} SET parent_id=@p, level_no=4 WHERE code='5268' AND (parent_id IS NULL OR parent_id<>(SELECT id FROM {t} WHERE code='526'))",("@p",parent526));
        Seed("526801","إعفاءات كيتا","5268",true);
        Seed("526802","إعفاءات هنقرستيشن","5268",true);
        Seed("526803","إعفاءات نينجا","5268",true);
        Seed("526804","إعفاءات أمازون","5268",true);
        Seed("526805","إعفاءات شيفز","5268",true);
        Seed("526806","إعفاءات جاهز (غير عمولة)","5268",true);
    }

    public static (string Code,Guid AccountId) ResolveWaiverExpense(DbConnection c,DbTransaction tx,Guid debtId)
    {
        EnsureD11WaiverAccounts(c,tx);
        string category,platform;
        using(var cmd=Store.Command(c,tx,$"SELECT category,platform FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId))))
        using(var reader=cmd.ExecuteReader())
        {
            J.Need(reader.Read(),"المديونية غير موجودة.",404);
            category=reader["category"].ToString()!;
            platform=reader["platform"] is DBNull?"":reader["platform"].ToString()!;
        }
        bool platformDebt=category.Equals("PlatformTransferredDebt",StringComparison.OrdinalIgnoreCase);
        if(platformDebt||platform!="")
        {
            J.Need(platform!="","منصة المديونية مطلوبة لحساب الإعفاء.",409);
            J.Need(PlatformWaiver.TryGetValue(platform,out var mapped)&&mapped is {Length:>0},"حساب إعفاء المنصة غير مربوط للمنصة: "+platform,409);
            return (mapped!,AccountId(c,tx,mapped!));
        }
        return ("526601",AccountId(c,tx,"526601"));
    }

    public static (string ExpenseCode,string ReceivableCode,Guid ExpenseId,Guid ReceivableId) ResolveAbsorbAccounts(DbConnection c,DbTransaction tx,Guid debtId)
    {
        string category,platform;
        using(var cmd=Store.Command(c,tx,$"SELECT category,platform FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId))))
        using(var reader=cmd.ExecuteReader())
        {
            J.Need(reader.Read(),"المديونية غير موجودة.",404);
            category=reader["category"].ToString()!;
            platform=reader["platform"] is DBNull?"":reader["platform"].ToString()!;
        }
        bool platformDebt=category.Equals("PlatformTransferredDebt",StringComparison.OrdinalIgnoreCase)||!string.IsNullOrWhiteSpace(platform);
        string expense=platformDebt?"526603":"526602";
        string receivable=ReceivableForCategory(category);
        return (expense,receivable,AccountId(c,tx,expense),AccountId(c,tx,receivable));
    }

    public static string ReceivableForCategory(string category)
    {
        var hit=ConfigLists.DefaultDeductions.FirstOrDefault(d=>string.Equals(d.Category,category,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrEmpty(hit.Account))return hit.Account;
        return category switch
        {
            "PlatformTransferredDebt"=>"112508",
            "NegativeSalaryCarryover"=>"112507",
            "LegacyOpeningDebt"=>"112505",
            _=>"112501"
        };
    }

    public static Guid ResolveReduceContra(DbConnection c,DbTransaction tx,string reason,string? clientContra,Guid debtId)
    {
        if(reason=="تصحيح رصيد افتتاحي")return AccountId(c,tx,"331301");
        if(reason=="خطأ إدخال")
        {
            // Reverse the original credit line (opening equity / vault / expense), never the same receivable.
            object? contraCode=SchemaSql.Scalar(c,tx,$"""
                SELECT a.code FROM {SchemaSql.Q(c,"debt","debt_movement")} m
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=m.journal_id
                JOIN {SchemaSql.Q(c,"fin","journal_line")} jl ON jl.journal_id=j.id AND jl.credit>0
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id
                WHERE m.debt_id=@d AND m.amount>0
                ORDER BY m.occurred_on DESC,m.id DESC,jl.line_no
                """,("@d",SchemaSql.GuidText(debtId)));
            if(contraCode is string code && code!="")
            {
                string recv=ReceivableForCategory(LoadCategory(c,tx,debtId));
                if(!code.Equals(recv,StringComparison.Ordinal))return AccountId(c,tx,code=="3101"?"331301":code);
            }
            object? last=SchemaSql.Scalar(c,tx,$"""
                SELECT j.purpose FROM {SchemaSql.Q(c,"debt","debt_movement")} m
                LEFT JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=m.journal_id
                WHERE m.debt_id=@d AND m.amount>0 ORDER BY m.occurred_on DESC,m.id DESC
                """,("@d",SchemaSql.GuidText(debtId)));
            string purpose=last?.ToString()??"";
            if(purpose.Contains("collection",StringComparison.OrdinalIgnoreCase))return AccountId(c,tx,"111101");
            if(purpose.Contains("opening",StringComparison.OrdinalIgnoreCase)||purpose.Contains("import",StringComparison.OrdinalIgnoreCase)||purpose.Contains("accrual",StringComparison.OrdinalIgnoreCase))
                return AccountId(c,tx,"331301");
            throw new ApiError("لا يوجد حساب مقابل أصلي لعكس خطأ الإدخال.",409);
        }
        J.Need(string.IsNullOrWhiteSpace(clientContra)||clientContra is "3101" or "331301","حساب التخفيض غير مسموح إلا لتصحيح الرصيد الافتتاحي.",400);
        return AccountId(c,tx,"331301");
    }

    static string LoadCategory(DbConnection c,DbTransaction tx,Guid debtId)=>
        SchemaSql.Scalar(c,tx,$"SELECT category FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))?.ToString()??"CashAdvance";

    public static void DemandDebtScope(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,Guid debtId,Guid companyId)
    {
        string companyKey=SchemaSql.GuidText(companyId);
        // W1 debt.company_id is hr.company. Catalog sponsors still DemandSponsor; cutover GUIDs accept finance-all scope when the company row exists.
        if(FinancialCore.FindSponsor(companyKey)!=null)FinancialScopes.DemandSponsor(actor,companyKey);
        else
        {
            object? hr=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyKey));
            J.Need(hr!=null&&(AccessPolicy.Administrator(actor)||FinancialScopes.AllCompanies(actor)),"السجل التجاري خارج النطاق المالي للمستخدم.",403);
        }
        string category,platform;
        using(var cmd=Store.Command(c,tx,$"SELECT category,platform FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id AND company_id=@c",
            ("@id",SchemaSql.GuidText(debtId)),("@c",SchemaSql.GuidText(companyId))))
        using(var reader=cmd.ExecuteReader())
        {
            J.Need(reader.Read(),"المديونية غير موجودة.",404);
            category=reader["category"].ToString()!;
            platform=reader["platform"] is DBNull?"":reader["platform"].ToString()!;
        }
        // Empty creditorSponsorId uses AllCompanies+finance-role path when company is hr GUID (not in legacy catalog).
        var fake=new JsonObject{["creditorSponsorId"]=FinancialCore.FindSponsor(companyKey)!=null?companyKey:"",["category"]=category,["platform"]=platform};
        FinancialScopes.DemandDebt(actor,fake);
    }

    public static JsonObject PreviewReduce(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor,byte[] file,string filename,DateOnly periodMonth)
    {
        AccessPolicy.Demand(actor,"debts.create.administrative");
        J.Need(file.Length>0&&file.Length<=2_000_000,"ملف الاستيراد فارغ أو أكبر من الحد.",400);
        string hash=Convert.ToHexString(SHA256.HashData(file));
        var sheets=Imports.Read(file,filename);
        J.Need(sheets.Count>0&&sheets[0].Rows.Count>1,"الملف فارغ أو بلا صفوف.",400);
        var rows=new JsonArray();var errors=new JsonArray();decimal total=0m;
        var sheet=sheets[0].Rows;
        for(int i=1;i<sheet.Count;i++)
        {
            var r=sheet[i];if(r.All(string.IsNullOrWhiteSpace))continue;
            try
            {
                string Cell(int idx)=>idx<r.Count?r[idx].Trim():"";
                string debtId=Cell(1);J.Need(Guid.TryParse(debtId,out _),"معرّف المديونية غير صالح.");
                decimal amount=decimal.Parse(J.Digits(Cell(2)),CultureInfo.InvariantCulture);
                J.Need(amount>0&&amount==J.Money(amount),"مبلغ التخفيض غير صالح.");
                string reason=Cell(3);J.Need(ReduceReasons.Contains(reason)||reason.StartsWith("أخرى",StringComparison.Ordinal),"سبب التخفيض مطلوب من القائمة المعتمدة.");
                object? bal=SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id AND company_id=@c AND status='open'",
                    ("@id",debtId),("@c",SchemaSql.GuidText(companyId)));
                J.Need(bal!=null,"المديونية غير موجودة أو مغلقة.",404);
                decimal balance=Convert.ToDecimal(bal);
                J.Need(amount<=balance,"التخفيض يتجاوز الرصيد.");
                total=J.Money(total+amount);
                rows.Add(new JsonObject{["row"]=i+1,["debtId"]=debtId,["iqama"]=Cell(0),["amount"]=amount,["reason"]=reason,["balanceBefore"]=balance});
            }
            catch(Exception ex){errors.Add($"الصف {i+1}: {ex.Message}");}
        }
        J.Need(errors.Count==0&&rows.Count>0,"معاينة التخفيض بها أخطاء أو بلا صفوف صالحة.",400);
        string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash+"|"+total.ToString("F2",CultureInfo.InvariantCulture)+"|"+rows.Count)));
        return new(){["filename"]=Path.GetFileName(filename),["fileHash"]=hash,["validCount"]=rows.Count,["errorCount"]=0,["totalReduce"]=total,
            ["periodMonth"]=periodMonth.ToString("yyyy-MM-01",CultureInfo.InvariantCulture),["fingerprint"]=fingerprint,["rows"]=rows,
            ["allowedActions"]=new JsonArray("commit_reduce","preview_reduce")};
    }

    public static JsonObject CommitReduce(DbConnection c,DbTransaction tx,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,byte[] file,string filename,string fingerprint,Guid receivableAccountId,Guid contraAccountId)
    {
        AccessPolicy.Demand(maker,"debts.create.administrative");
        var preview=PreviewReduce(c,tx,companyId,maker,file,filename,periodMonth);
        J.Need(preview.S("fingerprint")==fingerprint,"بصمة المعاينة لا تطابق الملف؛ أعد المعاينة.",409);
        DemandChecker(c,tx,maker,checker,fingerprint,preview.N("totalReduce"));
        var flags=ResolveMoneyFlags(c,tx,companyId,periodMonth);
        MoneyWriteGuards.Demand(flags.RunMode,flags.PayoutBlocked,MoneyEffectKind.Debt);
        string hash=preview.S("fileHash");
        byte[] hashBytes=Convert.FromHexString(hash);
        object? dup=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"debt","import_batch")} WHERE company_id=@c AND import_kind='reduce' AND file_hash=@h",
            ("@c",SchemaSql.GuidText(companyId)),("@h",hashBytes));
        J.Need(dup==null,"نفس ملف التخفيض سُجّل من قبل.",409);

        Guid batchId=SchemaSql.NewId();
        decimal total=preview.N("totalReduce");
        string actor=maker.Identity!.Name!;
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","import_batch")}(id,company_id,period_month,amount,reason,file_hash,import_kind,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@amt,@reason,@h,'reduce',@st,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(batchId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@amt",total),("@reason","تخفيض شيت مديونيات معتمد"),("@h",hashBytes),("@st",DocDraft),
            ("@rm",flags.RunMode),("@pb",flags.PayoutBlocked?1:0),("@by",actor),("@at",now));

        int lineNo=0;
        var posted=new List<(Guid debtId,Guid lineId,decimal amount,string reason)>();
        foreach(var row in preview["rows"]!.AsArray().OfType<JsonObject>())
        {
            lineNo++;
            Guid debtId=Guid.Parse(row.S("debtId"));
            decimal amount=row.N("amount");
            DemandDebtScope(c,tx,maker,debtId,companyId);
            decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
            J.Need(locked>=amount,"الرصيد تغيّر أثناء الاعتماد.",409);
            Guid lineId=SchemaSql.NewId();
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"debt","import_line")}(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount)
                VALUES(@id,@b,@pm,@ln,@src,@d,@c,@amt)
                """,("@id",SchemaSql.GuidText(lineId)),("@b",SchemaSql.GuidText(batchId)),("@pm",Month(periodMonth)),
                ("@ln",lineNo),("@src",row.N("row")),("@d",SchemaSql.GuidText(debtId)),("@c",SchemaSql.GuidText(companyId)),("@amt",-amount));
            posted.Add((debtId,lineId,amount,row.S("reason")));
        }
        AdvanceWorkflow(c,tx,"import_batch",batchId,checker);

        foreach(var (debtId,lineId,amount,reason) in posted)
        {
            Guid contra=ResolveReduceContra(c,tx,reason,null,debtId);
            Guid receivable=AccountId(c,tx,ReceivableForCategory(LoadCategory(c,tx,debtId)));
            var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                companyId,periodId,periodMonth,periodMonth.AddDays(14),"debt_import",lineId,"debt_reduce",flags.RunMode,flags.PayoutBlocked,actor,
                [
                    new(contra,SchemaOrgCostCentre.NoneId,"none",amount,0m,reason),
                    new(receivable,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
                ],NarrationAr:"تخفيض شيت مديونيات"));
            InsertMovement(c,tx,companyId,periodMonth,debtId,lineId,-amount,"import",journal.JournalId,actor,flags.RunMode,flags.PayoutBlocked);
        }

        WriteBulkChangeLog(c,tx,checker,SchemaSql.GuidText(batchId),lineNo,hashBytes,"استيراد تخفيض مديونيات");
        return new(){["batchId"]=SchemaSql.GuidText(batchId),["rowCount"]=lineNo,["totalReduce"]=total,["fileHash"]=hash,["allowedActions"]=new JsonArray()};
    }

    public static JsonObject Collect(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal actor,decimal amount,string reason,Guid receivableAccountId,Guid cashAccountId,byte runMode,bool payoutBlocked)
    {
        AccessPolicy.Demand(actor,"debts.collect");
        DemandDebtScope(c,tx,actor,debtId,companyId);
        var flags=ResolveMoneyFlags(c,tx,companyId,periodMonth);
        // Body flags are ignored; stored company/cycle flags win.
        runMode=flags.RunMode;payoutBlocked=flags.PayoutBlocked;
        MoneyWriteGuards.Demand(runMode,payoutBlocked,MoneyEffectKind.Debt);
        amount=J.Money(amount);J.Need(amount>0,"مبلغ التحصيل مطلوب.",400);
        string cashCode=AccountCode(c,tx,cashAccountId);
        J.Need(MoneyWriteGuards.IsBankOrCashAccountCode(cashCode),"التحصيل فقط إلى صندوق أو بنك لنفس الشركة.",400);
        DemandCashVaultScope(actor,cashCode,companyId);
        Guid receivable=AccountId(c,tx,ReceivableForCategory(LoadCategory(c,tx,debtId)));
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(locked>=amount,"المبلغ يتجاوز الرصيد.",400);
        DemandOpenDebt(c,tx,debtId,companyId);

        Guid collectionId=SchemaSql.NewId();
        string by=actor.Identity!.Name!,now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","collection")}(id,company_id,period_month,debt_id,amount,reason,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@d,@amt,@reason,@st,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(collectionId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@d",SchemaSql.GuidText(debtId)),("@amt",amount),("@reason",reason),("@st",DocDraft),("@rm",runMode),("@pb",payoutBlocked?1:0),("@by",by),("@at",now));
        AdvanceWorkflow(c,tx,"collection",collectionId,actor);

        var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            companyId,periodId,periodMonth,periodMonth.AddDays(Math.Min(27,DateTime.UtcNow.Day)),"debt_collection",collectionId,"debt_collection",runMode,payoutBlocked,by,
            [
                new(cashAccountId,SchemaOrgCostCentre.NoneId,"none",amount,0m,"تحصيل مديونية"),
                new(receivable,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
            ],NarrationAr:reason));
        InsertMovement(c,tx,companyId,periodMonth,debtId,collectionId,-amount,"collection",journal.JournalId,by,runMode,payoutBlocked);
        decimal balance=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))!);
        return new(){["debtId"]=SchemaSql.GuidText(debtId),["collectionId"]=SchemaSql.GuidText(collectionId),["balance"]=balance,
            ["journalId"]=SchemaSql.GuidText(journal.JournalId),["allowedActions"]=balance>0?new JsonArray("collect","waive","absorb","reschedule"):new JsonArray()};
    }

    public static JsonObject Waive(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId)
    {
        var resolved=ResolveWaiverExpense(c,tx,debtId);
        J.Need(expenseCode==resolved.Code,"حساب الإعفاء يُختار من المديونية (D11)؛ المطلوب "+resolved.Code+".",409);
        string receivableCode=ReceivableForCategory(LoadCategory(c,tx,debtId));
        return WaiveOrAbsorb(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,reason,resolved.Code,
            AccountId(c,tx,receivableCode),resolved.AccountId,WaiverAccounts,"debt_waiver","waiver","waived","إعفاء مديونية");
    }

    public static JsonObject Absorb(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId)
    {
        var resolved=ResolveAbsorbAccounts(c,tx,debtId);
        J.Need(expenseCode==resolved.ExpenseCode,"حساب التحمّل يُختار من المديونية؛ المطلوب "+resolved.ExpenseCode+".",409);
        return WaiveOrAbsorb(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,reason,resolved.ExpenseCode,
            resolved.ReceivableId,resolved.ExpenseId,AbsorbAccounts,"debt_absorb","absorption","absorbed","تحمل الشركة للمديونية");
    }

    static JsonObject WaiveOrAbsorb(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId,HashSet<string> allowed,string purpose,string sourceType,string zeroStatus,string labelAr)
    {
        AccessPolicy.Demand(maker,"debts.waive");
        DemandDebtScope(c,tx,maker,debtId,companyId);
        DemandDebtScope(c,tx,checker,debtId,companyId);
        J.Need(allowed.Contains(expenseCode),"حساب المصروف غير مسموح لهذا الإجراء: "+expenseCode,400);
        DemandChecker(c,tx,maker,checker,SchemaSql.GuidText(debtId),amount);
        var flags=ResolveMoneyFlags(c,tx,companyId,periodMonth);
        MoneyWriteGuards.Demand(flags.RunMode,flags.PayoutBlocked,MoneyEffectKind.Debt);
        amount=J.Money(amount);J.Need(amount>0&&!string.IsNullOrWhiteSpace(reason),"المبلغ والسبب مطلوبان.",400);
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(locked>=amount,"المبلغ يتجاوز الرصيد.",400);
        DemandOpenDebt(c,tx,debtId,companyId);

        Guid waiverId=SchemaSql.NewId();
        string by=maker.Identity!.Name!,now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","waiver")}(id,company_id,period_month,debt_id,amount,reason,expense_account_code,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@d,@amt,@reason,@exp,@st,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(waiverId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@d",SchemaSql.GuidText(debtId)),("@amt",amount),("@reason",reason),("@exp",expenseCode),("@st",DocDraft),
            ("@rm",flags.RunMode),("@pb",flags.PayoutBlocked?1:0),("@by",by),("@at",now));
        AdvanceWorkflow(c,tx,"waiver",waiverId,checker);

        var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            companyId,periodId,periodMonth,periodMonth.AddDays(14),"debt_waiver",waiverId,purpose,flags.RunMode,flags.PayoutBlocked,by,
            [
                new(expenseAccountId,SchemaOrgCostCentre.NoneId,"none",amount,0m,labelAr),
                new(receivableAccountId,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
            ],NarrationAr:reason,GuardPurpose:purpose));
        InsertMovement(c,tx,companyId,periodMonth,debtId,waiverId,-amount,sourceType,journal.JournalId,by,flags.RunMode,flags.PayoutBlocked);
        decimal balance=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))!);
        if(balance==0)
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET status=@s WHERE id=@id AND balance=0",("@s",zeroStatus),("@id",SchemaSql.GuidText(debtId)));
        return new(){["debtId"]=SchemaSql.GuidText(debtId),["waiverId"]=SchemaSql.GuidText(waiverId),["balance"]=balance,
            ["expenseAccountCode"]=expenseCode,["journalId"]=SchemaSql.GuidText(journal.JournalId),
            ["allowedActions"]=balance>0?new JsonArray("collect","waive","absorb","reschedule"):new JsonArray()};
    }

    public static JsonObject Reschedule(DbConnection c,DbTransaction tx,Guid debtId,ClaimsPrincipal actor,decimal instalment)
    {
        AccessPolicy.Demand(actor,"debts.reschedule");
        object? company=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        J.Need(company!=null,"المديونية غير موجودة.",404);
        Guid companyId=company is Guid g?g:Guid.Parse(company!.ToString()!);
        DemandDebtScope(c,tx,actor,debtId,companyId);
        instalment=J.Money(instalment);J.Need(instalment>0,"القسط الجديد مطلوب.",400);
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(instalment<=locked,"القسط يتجاوز الرصيد.",400);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET instalment=@i WHERE id=@id AND status='open'",
            ("@i",instalment),("@id",SchemaSql.GuidText(debtId)));
        // Keep the document store in sync so pay-cycle calculation cannot read a stale 25 after a ledger 10.
        string idText=SchemaSql.GuidText(debtId);
        if(Store.Get(c,idText,"debt",tx) is {} doc)
        {
            doc["monthlyInstallment"]=instalment;
            Store.Save(c,"debt",doc,tx);
        }
        return new(){["debtId"]=idText,["instalment"]=instalment,["balance"]=locked,["allowedActions"]=new JsonArray("reschedule","collect","waive","absorb")};
    }

    /// <summary>Instalment for pay-cycle deduction: ledger column wins over the document field.</summary>
    public static decimal InstalmentForPayCycle(DbConnection c,DbTransaction? tx,JsonObject debt)
    {
        string id=debt.S("id");
        if(Guid.TryParse(id,out var debtId))
        {
            object? ledger=SchemaSql.Scalar(c,tx,$"SELECT instalment FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
            if(ledger is not null and not DBNull)
            {
                decimal inst=Convert.ToDecimal(ledger);
                if(inst>0)return J.Money(inst);
            }
        }
        return J.Money(debt.N("monthlyInstallment"));
    }

    public static JsonObject Ageing(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"debts.view");
        using var cmd=Store.Command(c,tx,$"""
            SELECT d.id,d.balance,d.created_at FROM {SchemaSql.Q(c,"debt","debt")} d
            WHERE d.company_id=@c AND d.status='open' AND d.balance>0
            """,("@c",SchemaSql.GuidText(companyId)));
        using var reader=cmd.ExecuteReader();
        decimal b0_30=0,b31_60=0,b61_90=0,b90=0,total=0;
        var asOf=DateOnly.FromDateTime(DateTime.UtcNow);
        while(reader.Read())
        {
            decimal bal=Convert.ToDecimal(reader["balance"]);total=J.Money(total+bal);
            DateTime created=Convert.ToDateTime(reader["created_at"],CultureInfo.InvariantCulture);
            int days=asOf.DayNumber-DateOnly.FromDateTime(created).DayNumber;
            if(days<=30)b0_30=J.Money(b0_30+bal);
            else if(days<=60)b31_60=J.Money(b31_60+bal);
            else if(days<=90)b61_90=J.Money(b61_90+bal);
            else b90=J.Money(b90+bal);
        }
        return new(){["companyId"]=SchemaSql.GuidText(companyId),["totalOpen"]=total,
            ["buckets"]=new JsonArray(
                new JsonObject{["code"]="0_30",["labelAr"]="من 0 إلى 30 يومًا",["amount"]=b0_30},
                new JsonObject{["code"]="31_60",["labelAr"]="من 31 إلى 60 يومًا",["amount"]=b31_60},
                new JsonObject{["code"]="61_90",["labelAr"]="من 61 إلى 90 يومًا",["amount"]=b61_90},
                new JsonObject{["code"]="90_plus",["labelAr"]="أكثر من 90 يومًا",["amount"]=b90}),
            ["allowedActions"]=new JsonArray()};
    }

    public static JsonObject PrintRegister(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor,string? status=null)
    {
        AccessPolicy.Demand(actor,"debts.statement.print");
        var items=new JsonArray();
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,category,principal,balance,instalment,status,promissory_flag,erp_rider_id
            FROM {SchemaSql.Q(c,"debt","debt")} WHERE company_id=@c{(string.IsNullOrWhiteSpace(status)?"":" AND status=@s")}
            ORDER BY category,id
            """,string.IsNullOrWhiteSpace(status)?[("@c",SchemaSql.GuidText(companyId))]:[("@c",SchemaSql.GuidText(companyId)),("@s",status!)]);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            items.Add(new JsonObject{
                ["id"]=reader["id"].ToString(),["category"]=reader["category"].ToString(),
                ["principal"]=Convert.ToDecimal(reader["principal"]),["balance"]=Convert.ToDecimal(reader["balance"]),
                ["instalment"]=reader["instalment"] is DBNull?null:Convert.ToDecimal(reader["instalment"]),
                ["status"]=reader["status"].ToString(),["promissory"]=Convert.ToInt32(reader["promissory_flag"])==1,
                ["erpRiderId"]=reader["erp_rider_id"].ToString()});
        return new(){["companyId"]=SchemaSql.GuidText(companyId),["printedAt"]=DateTimeOffset.UtcNow.ToString("O"),["items"]=items,["allowedActions"]=new JsonArray()};
    }

    public static JsonObject LinkPromissory(DbConnection c,DbTransaction tx,string noteId,Guid debtId,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"debts.create.administrative");
        var note=Store.Get(c,noteId,"promissory_note",tx)??throw new ApiError("سند الأمر غير موجود.",404);
        string noteCompany=note.S("beneficiarySponsorId",note.S("companyId",note.S("creditorSponsorId",note.S("sponsorId"))));
        J.Need(noteCompany!="","سند الأمر بلا سجل مالك (beneficiarySponsorId).",400);
        FinancialScopes.DemandSponsor(actor,noteCompany);
        object? company=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        J.Need(company!=null,"المديونية غير موجودة.",404);
        Guid companyId=company is Guid g?g:Guid.Parse(company!.ToString()!);
        DemandDebtScope(c,tx,actor,debtId,companyId);
        DemandOpenDebt(c,tx,debtId,companyId);
        note["debtId"]=SchemaSql.GuidText(debtId);note["linkedAt"]=DateTimeOffset.UtcNow.ToString("O");note["linkedBy"]=actor.Identity!.Name!;
        Store.Save(c,"promissory_note",note,tx);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET promissory_flag=1 WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        return new(){["noteId"]=noteId,["debtId"]=SchemaSql.GuidText(debtId),["allowedActions"]=new JsonArray("link_debt")};
    }

    static void DemandChecker(DbConnection c,DbTransaction tx,ClaimsPrincipal maker,ClaimsPrincipal checker,string referenceId,decimal amountSar)
    {
        J.Need(checker.Identity?.Name is {Length:>0},"هوية المعتمد مطلوبة من جلسة الاعتماد.",401);
        J.Need(!AccessPolicy.SamePerson(maker,checker.Identity!.Name!),"الاعتماد لشخص مستقل عن مقدّم الطلب (صانع≠مراجع).",403);
        var request=new JsonObject
        {
            ["requestedBy"]=maker.Identity?.Name??"",
            ["targetDepartment"]=checker.FindFirst("department")?.Value??"FIN",
            ["requiredRankLevel"]=1,
            ["referenceId"]=referenceId,
            ["amountSar"]=amountSar
        };
        AccessPolicy.ValidateApproval(checker,request,new JsonObject(),"approvals.decide",c,tx,"debt.approval",request);
    }

    static void WriteBulkChangeLog(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string entityRef,int bulkCount,byte[] fileHash,string diffShort)
    {
        SchemaAuditNotify.Ensure(c,tx);
        string userId=SchemaApprovals.EnsureAuthUserId(c,tx,actor.Identity!.Name!);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"audit","change_log")}
            (id,occurred_on,occurred_at,action_code,entity_type,entity_ref,diff_short,source,bulk_count,file_hash,user_id,created_at)
            VALUES(@id,@on,@at,'import','debt',@ref,@diff,'import',@bc,@h,@u,@at)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@on",DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@at",now),("@ref",entityRef),("@diff",diffShort),("@bc",bulkCount),("@h",fileHash),("@u",userId));
    }

    static void DemandCashVaultScope(ClaimsPrincipal actor,string cashCode,Guid companyId)
    {
        if(AccessPolicy.Administrator(actor))return;
        bool isVault=FinancialCore.ChartOfAccounts.Any(a=>a.IsVault&&string.Equals(a.Code,cashCode,StringComparison.OrdinalIgnoreCase));
        if(isVault)
        {
            J.Need(actor.HasClaim("finance.accountCodes",cashCode),"الحساب المالي خارج النطاق المسند للمستخدم.",403);
            return;
        }
        // Numeric target cash/bank: require an assigned vault that maps to this account (and matches company when known).
        string companyText=SchemaSql.GuidText(companyId);
        bool companyIsSponsor=FinancialCore.Sponsors.Any(s=>string.Equals(s.Id,companyText,StringComparison.OrdinalIgnoreCase));
        bool matched=actor.FindAll("finance.accountCodes").Any(claim=>
        {
            string vault=claim.Value;
            if(!FinanceViews.VaultMapsToTarget(vault,cashCode))return false;
            var acc=FinancialCore.ChartOfAccounts.FirstOrDefault(a=>a.IsVault&&string.Equals(a.Code,vault,StringComparison.OrdinalIgnoreCase));
            if(acc==null)return false;
            if(companyIsSponsor&&acc.DefaultSponsorId!=null)
                return string.Equals(acc.DefaultSponsorId,companyText,StringComparison.OrdinalIgnoreCase);
            return true;
        });
        J.Need(matched,"الحساب المالي خارج النطاق المسند للمستخدم.",403);
    }

    static string AccountCode(DbConnection c,DbTransaction tx,Guid accountId)=>
        SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",SchemaSql.GuidText(accountId)))?.ToString()
        ??throw new ApiError("الحساب غير موجود.",404);

    static void DemandOpenDebt(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,bool checkCompany=true)
    {
        object? status=SchemaSql.Scalar(c,tx,$"SELECT status FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id{(checkCompany?" AND company_id=@c":"")}",
            checkCompany?[("@id",SchemaSql.GuidText(debtId)),("@c",SchemaSql.GuidText(companyId))]:[("@id",SchemaSql.GuidText(debtId))]);
        J.Need(status?.ToString()=="open","المديونية غير مفتوحة.",409);
    }

    static void AdvanceWorkflow(DbConnection c,DbTransaction tx,string table,Guid id,ClaimsPrincipal checker)
    {
        string t=SchemaSql.Q(c,"debt",table);
        string approvedBy=SchemaApprovals.EnsureAuthUserId(c,tx,checker.Identity!.Name!);
        SchemaSql.Exec(c,tx,$"UPDATE {t} SET status=@s WHERE id=@id AND status=@d",("@s",DocSubmitted),("@id",SchemaSql.GuidText(id)),("@d",DocDraft));
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"UPDATE {t} SET status=@s,approved_by=@u,approved_at=@at WHERE id=@id AND status=@sub",
            ("@s",DocApproved),("@u",approvedBy),("@at",now),("@id",SchemaSql.GuidText(id)),("@sub",DocSubmitted));
    }

    static void InsertMovement(DbConnection c,DbTransaction tx,Guid companyId,DateOnly periodMonth,Guid debtId,Guid sourceId,decimal amount,string sourceType,Guid journalId,string actor,byte runMode=0,bool payoutBlocked=false)
    {
        Guid movId=SchemaSql.NewId();
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        DateOnly occurred=periodMonth.AddDays(Math.Min(20,DateTime.UtcNow.Day));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
            (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,journal_id,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@pm,@d,@c,@on,@amt,@st,@sid,@j,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(movId)),("@pm",Month(periodMonth)),("@d",SchemaSql.GuidText(debtId)),
            ("@c",SchemaSql.GuidText(companyId)),("@on",Day(occurred)),("@amt",amount),("@st",sourceType),
            ("@sid",SchemaSql.GuidText(sourceId)),("@j",SchemaSql.GuidText(journalId)),("@rm",runMode),("@pb",payoutBlocked?1:0),
            ("@by",actor),("@at",now));
    }

    static string Month(DateOnly d)=>d.ToString("yyyy-MM-01",CultureInfo.InvariantCulture);
    static string Day(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
