using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Payroll;

/// <summary>U-W2-17 / U-W2-17b: sal.bank_file + sal.bank_file_line build, export (bulk_csv + xlsx), and mark-sent.
/// Evaluates N2 eligibility atomically with pay.payable reservation (SchemaPayAllocation.LockPayable);
/// 4-letter bank short-code first column; excludes held shared riders from single-platform files and consolidates
/// multi-app riders to appear once in the exported sheet; honours payout_blocked («لا يُرسل» / 409 on mark-sent);
/// requires recorded two-person decision (approvals.dual.bank_file) bound to canonical file content hash.</summary>
public static partial class SalBankFiles
{
    public const string PermExport="finance.bankfile.export";
    public const string PermMarkSent="salaries.bankfile.mark_sent";

    public const byte StatusDraft=1,StatusApproved=2,StatusSent=3,StatusCancelled=4;

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        SchemaHrCompany.Ensure(c,tx);
        SchemaOrgCostCentre.Ensure(c,tx);
        SchemaFinAccount.Ensure(c,tx);
        SchemaFinPeriod.Ensure(c,tx);
        SchemaFinJournal.Ensure(c,tx);
        SchemaFinPeriodBalance.Ensure(c,tx);
        SchemaPayPayable.Ensure(c,tx);
        SchemaPayAllocation.Ensure(c,tx);
        SchemaTreasury.Ensure(c,tx);
        SchemaSalPayRun.Ensure(c,tx);
        SchemaBankFile.Ensure(c,tx);
        SchemaAuthCatalog.Ensure(c,tx);
        SchemaApprovals.Ensure(c,tx);
        SalRiderRequests.Ensure(c,tx);
    }

    public static string ActorGuidText(ClaimsPrincipal actor)
    {
        string raw=actor.FindFirstValue(ClaimTypes.NameIdentifier)??actor.Identity?.Name??"";
        return ActorGuidTextFromName(raw);
    }

    public static string ActorGuidTextFromName(string? nameOrId)
    {
        string raw=(nameOrId??"").Trim();
        if(Guid.TryParse(raw,out var g))return SchemaSql.GuidText(g);
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes("actor:"+raw.ToLowerInvariant()));
        return SchemaSql.GuidText(new Guid(hash.AsSpan(0,16)));
    }

    public static JsonObject List(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? periodMonth,string? companyId,string? status)
    {
        DemandView(actor);
        Ensure(c,tx);
        string? normMonth=string.IsNullOrWhiteSpace(periodMonth)?null:NormalizeMonth(periodMonth!);
        if(!string.IsNullOrWhiteSpace(companyId))FinancialScopes.DemandSponsor(actor,companyId!);

        var ids=new List<string>();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT id,period_month,scope_company_id,paying_company_id,status
                FROM {SchemaSql.Q(c,"sal","bank_file_export")}
                ORDER BY created_at DESC,id
                """;
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                string rowMonth=ReadMonth(r["period_month"]);
                if(normMonth!=null&&rowMonth!=normMonth)continue;
                string payingComp=r["paying_company_id"].ToString()!;
                string scopeComp=Str(r["scope_company_id"]);
                if(!FinancialScopes.Sponsor(actor,payingComp)&&(scopeComp==""||!FinancialScopes.Sponsor(actor,scopeComp)))continue;
                if(!string.IsNullOrWhiteSpace(companyId)&&!string.Equals(payingComp,companyId,StringComparison.OrdinalIgnoreCase)&&!string.Equals(scopeComp,companyId,StringComparison.OrdinalIgnoreCase))continue;
                int st=Convert.ToInt32(r["status"]);
                if(!string.IsNullOrWhiteSpace(status)&&!MatchesStatus(st,status!))continue;
                ids.Add(r["id"].ToString()!);
            }
        }
        var items=new JsonArray();
        foreach(string id in ids)
            items.Add(LoadFileView(c,tx,actor,id));
        var actions=new JsonArray();
        if(AccessPolicy.Has(actor,PermExport)||AccessPolicy.Has(actor,"pay.approve"))actions.Add("build");
        return new(){["items"]=items,["total"]=items.Count,["allowedActions"]=actions};
    }

    public static JsonObject Get(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        DemandView(actor);
        Ensure(c,tx);
        return LoadFileView(c,tx,actor,id);
    }

    public static JsonObject Build(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandAny(actor,PermExport,"pay.approve");
        Ensure(c,tx);

        string periodMonth=NormalizeMonth(input.S("periodMonth",input.S("period")));
        string rawChoice=input.S("issuanceChoice",input.S("issuance_choice",input.S("sheetShape","per_company"))).Trim().ToLowerInvariant();
        string sheetShape=rawChoice switch
        {
            "per_registration" or "registration"=> "per_registration",
            "per_company" or "company"=> "per_company",
            "combined" or "all"=> "combined",
            _=>throw new ApiError("خيار إصدار ملف البنك غير صالح (per_registration أو per_company أو combined).",400)
        };
        string dbIssuanceChoice=sheetShape=="combined"?"combined":"per_company";

        string payingBankId=input.S("payingBankId");
        if(string.IsNullOrWhiteSpace(payingBankId))
        {
            string reqComp=input.S("payingCompanyId",input.S("scopeCompanyId",input.S("companyId")));
            string reqReg=input.S("payingRegistrationId",input.S("scopeRegistrationId",input.S("registrationId")));
            payingBankId=ResolvePayingBankId(c,tx,reqComp,reqReg);
        }
        var bankInfo=LoadPayingBank(c,tx,payingBankId);
        if(!string.IsNullOrWhiteSpace(input.S("payingCompanyId")))
            J.Need(string.Equals(input.S("payingCompanyId"),bankInfo.CompanyId,StringComparison.OrdinalIgnoreCase),"الشركة الدافعة لا تطابق الحساب البنكي المختار.",400);
        if(!string.IsNullOrWhiteSpace(input.S("payingRegistrationId")))
            J.Need(string.Equals(input.S("payingRegistrationId"),bankInfo.RegistrationId,StringComparison.OrdinalIgnoreCase),"السجل التجاري الدافع لا يطابق الحساب البنكي المختار.",400);
        FinancialScopes.DemandSponsor(actor,bankInfo.CompanyId);

        string? scopeCompanyId=null;
        string? scopeRegistrationId=null;
        if(sheetShape=="per_registration")
        {
            scopeRegistrationId=input.S("scopeRegistrationId",input.S("registrationId",bankInfo.RegistrationId));
            J.Need(!string.IsNullOrWhiteSpace(scopeRegistrationId),"حدد السجل التجاري لإصدار ملف البنك حسب السجل.",400);
            string regCompany=LoadRegistrationCompany(c,tx,scopeRegistrationId!);
            scopeCompanyId=input.S("scopeCompanyId",input.S("companyId",regCompany));
            J.Need(string.Equals(scopeCompanyId,regCompany,StringComparison.OrdinalIgnoreCase),"السجل التجاري لا يتبع الشركة المحددة.",400);
            FinancialScopes.DemandSponsor(actor,scopeCompanyId!);
        }
        else if(sheetShape=="per_company")
        {
            scopeCompanyId=input.S("scopeCompanyId",input.S("companyId",bankInfo.CompanyId));
            J.Need(!string.IsNullOrWhiteSpace(scopeCompanyId),"حدد الشركة لإصدار ملف البنك حسب الشركة.",400);
            FinancialScopes.DemandSponsor(actor,scopeCompanyId!);
        }

        string appFilter=input.S("appCode",input.S("app","all")).Trim().ToLowerInvariant();
        if(appFilter!=""&&appFilter!="all")
        {
            appFilter=PayPlatforms.For(appFilter).App;
            J.Need(FinancialScopes.Platform(actor,PayPlatforms.For(appFilter).Platform),"المنصة خارج نطاق المستخدم.",403);
        }
        else appFilter="all";

        bool confirmBankCode40=input.B("confirmBankCode40");
        var worked=SalSharedRiders.WorkedApps(c,tx,periodMonth);
        var riderDocs=LoadRiderDocs(c,tx);
        var defaultRegByCompany=LoadDefaultRegistrations(c,tx);

        // Scan sal.pay_run_rider rows for periodMonth to classify eligible vs excluded riders (N2 predicate).
        var candidates=new List<CandidateLine>();
        var excluded=new JsonArray();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT r.id,r.pay_run_id,r.company_id,r.erp_rider_id,r.period_month,r.salary_net,
                       r.debt_confirm_status,r.salary_approved_at,r.payable_id,r.bank_eligible,r.run_mode,
                       pr.app_code,pr.run_mode AS pr_run_mode,pr.payout_blocked AS pr_payout_blocked,pr.status AS pr_status
                FROM {SchemaSql.Q(c,"sal","pay_run_rider")} r
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE r.period_month=@pm AND pr.status<>4
                ORDER BY r.erp_rider_id,pr.app_code,r.id
                """;
            Add(cmd,"@pm",periodMonth);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                string rowCompany=r["company_id"].ToString()!;
                if(!FinancialScopes.Sponsor(actor,rowCompany))continue;
                if(sheetShape!="combined"&&!string.Equals(rowCompany,scopeCompanyId,StringComparison.OrdinalIgnoreCase))continue;
                string rowApp=r["app_code"].ToString()!;
                if(!FinancialScopes.Platform(actor,PayPlatforms.For(rowApp).Platform))continue;
                if(appFilter!="all"&&!string.Equals(rowApp,appFilter,StringComparison.OrdinalIgnoreCase))continue;

                string prRiderId=r["id"].ToString()!;
                string erpRider=r["erp_rider_id"].ToString()!;
                decimal net=J.Money(Convert.ToDecimal(r["salary_net"]));
                string debtStatus=r["debt_confirm_status"].ToString()!;
                string approvedAt=Str(r["salary_approved_at"]);
                string payableId=Str(r["payable_id"]);
                int runMode=Convert.ToInt32(r["run_mode"]);
                int prRunMode=Convert.ToInt32(r["pr_run_mode"]);
                bool pb=Convert.ToInt32(r["pr_payout_blocked"])==1;

                var riderMeta=ResolveRiderMeta(erpRider,rowCompany,riderDocs,defaultRegByCompany);
                if(sheetShape=="per_registration"&&!string.Equals(riderMeta.RegistrationId,scopeRegistrationId,StringComparison.OrdinalIgnoreCase))
                    continue;

                if(runMode!=0||prRunMode!=0)
                {
                    excluded.Add(Excluded(erpRider,riderMeta,rowApp,net,"simulation_run_mode"));
                    continue;
                }
                if(approvedAt=="")
                {
                    excluded.Add(Excluded(erpRider,riderMeta,rowApp,net,"salary_not_approved"));
                    continue;
                }
                if(debtStatus is not ("confirmed" or "deferred"))
                {
                    excluded.Add(Excluded(erpRider,riderMeta,rowApp,net,"debt_confirmation_pending"));
                    continue;
                }
                if(net<=0||payableId=="")
                {
                    excluded.Add(Excluded(erpRider,riderMeta,rowApp,net,net==0?"zero_balance":"negative_net_carryover"));
                    continue;
                }
                var riderApps=SalSharedRiders.AppsFor(worked,erpRider,rowApp);
                if(appFilter!="all"&&riderApps.Count>1)
                {
                    // X26 / §40.5: multi-app shared riders never enter a single-platform bank file.
                    excluded.Add(Excluded(erpRider,riderMeta,rowApp,net,"shared_rider_excluded_from_single_platform"));
                    continue;
                }
                candidates.Add(new CandidateLine(prRiderId,erpRider,rowCompany,riderMeta.RegistrationId,rowApp,payableId,net,pb,riderMeta));
            }
        }

        // Filter out held riders (open sal.shared_rider_hold or active/individual_transfer sal.rider_request hold) and already-claimed payables.
        var eligible=new List<CandidateLine>();
        foreach(var cand in candidates)
        {
            if(HasOpenSharedHold(c,tx,cand.ErpRiderId,periodMonth))
            {
                excluded.Add(Excluded(cand.ErpRiderId,cand.Meta,cand.AppCode,cand.Amount,"shared_rider_hold_open"));
                continue;
            }
            if(SalRiderRequests.HasActiveBankBlockingHold(c,tx,cand.ErpRiderId,periodMonth,cand.AppCode))
            {
                excluded.Add(Excluded(cand.ErpRiderId,cand.Meta,cand.AppCode,cand.Amount,"salary_hold_active_or_individual_transfer"));
                continue;
            }
            if(HasActiveBankLineForRider(c,tx,cand.PayRunRiderId))
            {
                excluded.Add(Excluded(cand.ErpRiderId,cand.Meta,cand.AppCode,cand.Amount,"already_in_active_bank_file"));
                continue;
            }
            var rowObj=new JsonObject
            {
                ["bank"]=cand.Meta.BankShortCode,["iban"]=cand.Meta.Iban,["amount"]=cand.Amount,
                ["notes"]="",["name"]=cand.Meta.Name,["iqama"]=cand.Meta.Iqama,["address"]=cand.Meta.Address
            };
            var errs=BankPayrollFiles.Validate(rowObj,confirmBankCode40);
            if(errs.Count>0)
                throw new ApiError($"بيانات الحساب البنكي للمندوب ({cand.Meta.Iqama}) غير مكتملة أو غير صالحة: {string.Join(", ",errs.Select(e=>e!.ToString()))}",400);
            eligible.Add(cand);
        }

        J.Need(eligible.Count>0,"لا توجد مستحقات رواتب مؤهلة لإصدار ملف البنك في هذا النطاق.",409);

        // Lock payables in ascending payable_id order BEFORE inserting claims (SchemaPayAllocation.LockPayable contract).
        var lockedPayables=new Dictionary<string,decimal>(StringComparer.OrdinalIgnoreCase);
        foreach(string payableId in eligible.Select(e=>e.PayableId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase))
        {
            decimal avail=SchemaPayAllocation.LockPayable(c,tx,payableId);
            lockedPayables[payableId]=avail;
        }
        foreach(var cand in eligible)
        {
            decimal avail=lockedPayables[cand.PayableId];
            J.Need(avail>=cand.Amount,"المستحق محجوز أو مصروف مسبقاً في ملف بنكي آخر.",409);
            lockedPayables[cand.PayableId]=J.Money(avail-cand.Amount);
        }

        bool filePayoutBlocked=eligible.Any(e=>e.PayoutBlocked);
        if(!filePayoutBlocked)
        {
            string monthKey=periodMonth[..7];
            var flags=BankPayrollFiles.ResolveStoredMoneyFlags(c,tx,(bankInfo.CompanyId,monthKey,monthKey));
            if(flags.PayoutBlocked)filePayoutBlocked=true;
        }

        string fileId=SchemaSql.GuidText(SchemaSql.NewId());
        string actorGuid=ActorGuidText(actor);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);

        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"sal","bank_file")}
            (id,period_month,issuance_choice,scope_company_id,scope_registration_id,
             paying_company_id,paying_registration_id,paying_bank_id,status,run_mode,payout_blocked,currency_code,created_by,created_at)
            VALUES(@id,@pm,@ic,@sc,@sr,@pc,@pr,@pb,1,0,@blk,'SAR',@by,@at)
            """,
            ("@id",fileId),("@pm",periodMonth),("@ic",dbIssuanceChoice),
            ("@sc",(object?)scopeCompanyId??DBNull.Value),("@sr",(object?)scopeRegistrationId??DBNull.Value),
            ("@pc",bankInfo.CompanyId),("@pr",bankInfo.RegistrationId),("@pb",bankInfo.BankId),
            ("@blk",filePayoutBlocked?1:0),("@by",actorGuid),("@at",now));

        // Group eligible lines by company_id to create one queued pay.payment per entitlement company (with payer_company_id = bankInfo.CompanyId).
        var paymentByCompany=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        int seq=0;
        foreach(var grp in eligible.GroupBy(e=>e.CompanyId,StringComparer.OrdinalIgnoreCase))
        {
            seq++;
            string paymentId=SchemaSql.GuidText(SchemaSql.NewId());
            string paymentNo=$"BF-{fileId[^12..].ToUpperInvariant()}-{seq:D2}";
            decimal compTotal=J.Money(grp.Sum(x=>x.Amount));
            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"pay","payment")}
                (id,payment_no,company_id,payer_company_id,period_month,paid_on,amount,currency_code,status,run_mode,payout_blocked,created_by,created_at)
                VALUES(@id,@no,@c,@payer,@pm,@on,@amt,'SAR',1,0,@blk,@by,@at)
                """,
                ("@id",paymentId),("@no",paymentNo),("@c",grp.Key),("@payer",bankInfo.CompanyId),
                ("@pm",periodMonth),("@on",periodMonth),("@amt",compTotal),
                ("@blk",filePayoutBlocked?1:0),("@by",actorGuid),("@at",now));
            paymentByCompany[grp.Key]=paymentId;
        }

        // Insert pay.payment_allocation (claim_status=1) and sal.bank_file_line (status=1) for each eligible payable.
        foreach(var cand in eligible)
        {
            string lineId=SchemaSql.GuidText(SchemaSql.NewId());
            string allocId=SchemaSql.GuidText(SchemaSql.NewId());
            string paymentId=paymentByCompany[cand.CompanyId];

            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"pay","payment_allocation")}
                (id,payment_id,payment_line_id,payable_id,company_id,payable_period_month,period_month,amount,currency_code,claim_status,reserved_at,created_by,created_at)
                VALUES(@id,@pid,@lid,@payable,@c,@pm,@pm,@amt,'SAR',1,@at,@by,@at)
                """,
                ("@id",allocId),("@pid",paymentId),("@lid",lineId),("@payable",cand.PayableId),
                ("@c",cand.CompanyId),("@pm",periodMonth),("@amt",cand.Amount),("@at",now),("@by",actorGuid));

            Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"sal","bank_file_line")}
                (id,bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,payable_period_month,payment_id,allocation_id,amount,bank_short_code,status,created_by,created_at)
                VALUES(@id,@fid,@pm,@pr,@payable,@c,@reg,@pm,@pid,@aid,@amt,@bank,1,@by,@at)
                """,
                ("@id",lineId),("@fid",fileId),("@pm",periodMonth),("@pr",cand.PayRunRiderId),
                ("@payable",cand.PayableId),("@c",cand.CompanyId),("@reg",cand.RegistrationId),
                ("@pid",paymentId),("@aid",allocId),("@amt",cand.Amount),("@bank",cand.Meta.BankShortCode),
                ("@by",actorGuid),("@at",now));

            if(!string.Equals(cand.CompanyId,bankInfo.CompanyId,StringComparison.OrdinalIgnoreCase))
            {
                string settlementId=SchemaSql.GuidText(SchemaSql.NewId());
                Exec(c,tx,$"""
                    INSERT INTO {SchemaSql.Q(c,"fin","inter_company_settlement")}
                    (id,bank_file_line_id,period_month,paying_company_id,entitlement_company_id,amount,status,created_by,created_at)
                    VALUES(@id,@lid,@pm,@payer,@ent,@amt,1,@by,@at)
                    """,
                    ("@id",settlementId),("@lid",lineId),("@pm",periodMonth),
                    ("@payer",bankInfo.CompanyId),("@ent",cand.CompanyId),("@amt",cand.Amount),
                    ("@by",actorGuid),("@at",now));
            }
        }

        // Compute canonical file_hash over consolidated sheet rows while status = 1.
        var view=LoadFileView(c,tx,actor,fileId,excluded);
        string hashHex=view.S("fileContentHash");
        byte[] hashBytes=Convert.FromHexString(hashHex);
        Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"sal","bank_file")} SET file_hash=@h,updated_by=@by,updated_at=@at WHERE id=@id",
            ("@h",hashBytes),("@by",actorGuid),("@at",now),("@id",fileId));
        view["fileHash"]=hashHex;
        Store.Audit(c,tx,"sal.bank_file_built",fileId,actor.Identity?.Name??actorGuid);
        return view;
    }

    public static BankPayrollFiles.Download Export(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,string? format="bulk_csv")
    {
        DemandAny(actor,PermExport,"pay.approve");
        Ensure(c,tx);
        var view=LoadFileView(c,tx,actor,id);
        var sheetRows=(view["sheetRows"] as JsonArray??[]).OfType<JsonObject>().ToList();
        J.Need(sheetRows.Count>0,"ملف البنك لا يحتوي على صفوف للتصدير.",409);

        bool blocked=view.B("payoutBlocked");
        string prefix=blocked?"DO-NOT-SEND_":"";
        string stamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string fmt=(format??"bulk_csv").Trim().ToLowerInvariant();
        if(fmt is "xlsx" or "excel")
        {
            var cols=new[]
            {
                new ExcelColumn("البنك"),
                new ExcelColumn("رقم الحساب"),
                new ExcelColumn("إجمالي الراتب",Numeric:true),
                new ExcelColumn("ملاحظات"),
                new ExcelColumn("اسم الموظف"),
                new ExcelColumn("رقم الهوية أو الإقامة",Identity:true),
                new ExcelColumn("عنوان الموظف")
            };
            var rows=sheetRows.Select(r=>new object?[]
            {
                r.S("bank"),r.S("iban"),r.N("amount"),r.S("notes"),r.S("name"),r.S("iqama"),r.S("address")
            }).ToList();
            byte[] xlsxBytes=ExcelFiles.Write(cols,rows);
            return new(xlsxBytes,$"{prefix}{stamp}_bulkPayroll.xlsx");
        }
        byte[] csvBytes=BankPayrollFiles.Csv(sheetRows);
        return new(csvBytes,$"{prefix}{stamp}_bulkPayroll.csv");
    }

    public static JsonObject RequestMarkSent(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject? input=null)
    {
        _=input;
        DemandAny(actor,PermMarkSent,PermExport,"pay.approve",ApprovalsArea.DualBankFile);
        Ensure(c,tx);
        var view=LoadFileView(c,tx,actor,id);
        int st=(int)view.N("statusCode");
        J.Need(st is StatusDraft or StatusApproved,"ملف البنك ليس في حالة مسودة أو معتمد للإرسال.",409);
        MoneyWriteGuards.Demand((byte)view.N("runMode"),view.B("payoutBlocked"),MoneyEffectKind.BankSent);

        decimal total=J.Money(view.N("totalAmount"));
        string fileHash=view.S("fileContentHash");
        var req=ApprovalsArea.OpenMoneyOutDual(c,tx,actor,"bank_sent",id,total,fileHash,
            "اعتماد إرسال ملف بنك","ملف رواتب جاهز للإرسال للبنك — بانتظار اعتماد شخصين.",
            new JsonObject
            {
                ["bankFileId"]=id,["sponsorId"]=view.S("payingCompanyId"),["companyId"]=view.S("payingCompanyId"),
                ["periodMonth"]=view.S("periodMonth"),["issuanceChoice"]=view.S("issuanceChoice"),
                ["sheetShape"]=view.S("sheetShape"),["fileContentHash"]=fileHash,["payloadHash"]=fileHash
            });
        Store.Audit(c,tx,"sal.bank_file_mark_sent_requested",id,actor.Identity!.Name!);
        return req;
    }

    public static JsonObject MarkSent(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject? input=null)
    {
        _=input;
        DemandAny(actor,PermMarkSent,PermExport,"pay.approve",ApprovalsArea.DualBankFile);
        Ensure(c,tx);
        var view=LoadFileView(c,tx,actor,id);
        int st=(int)view.N("statusCode");
        J.Need(st is StatusDraft or StatusApproved,"ملف البنك مرسل أو ملغى مسبقاً.",409);
        byte runMode=(byte)view.N("runMode");
        bool payoutBlocked=view.B("payoutBlocked");
        MoneyWriteGuards.Demand(runMode,payoutBlocked,MoneyEffectKind.BankSent);
        MoneyWriteGuards.Demand(runMode,payoutBlocked,MoneyEffectKind.Payout);

        string currentHash=view.S("fileContentHash");
        string storedHash=view.S("fileHash");
        if(storedHash!=""&&!string.Equals(storedHash,currentHash,StringComparison.OrdinalIgnoreCase))
            throw new ApiError("تغيرت بصمة محتوى ملف البنك بعد الإنشاء؛ ألغِ الملف وأعد الإصدار.",403);

        ApprovalsArea.DemandRecordedMoneyOutDual(c,tx,actor,ApprovalsArea.DualBankFile,id,currentHash);
        var approvedReq=Store.List(c,"approval_request",tx)
            .Where(d=>string.Equals(d.S("referenceId"),id,StringComparison.OrdinalIgnoreCase)&&d.S("status")=="approved")
            .OrderByDescending(d=>d.S("decidedAt"))
            .FirstOrDefault();
        string checkerGuid=ActorGuidTextFromName(approvedReq?.S("decidedBy"));
        string creatorGuid=view.S("createdBy");
        if(string.IsNullOrEmpty(approvedReq?.S("decidedBy"))||string.Equals(checkerGuid,creatorGuid,StringComparison.OrdinalIgnoreCase))
            checkerGuid=ActorGuidText(actor);
        J.Need(!string.Equals(checkerGuid,creatorGuid,StringComparison.OrdinalIgnoreCase),"اعتماد ملف البنك وإرساله يتطلب شخصين مختلفين (المنشئ ≠ المعتمد).",403);

        string senderGuid=ActorGuidText(actor);
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);

        // Step 1: transition sal.bank_file from status=1 to status=2 (approved) so TR_bank_claim_state allows claim_status=2.
        if(st==StatusDraft)
        {
            Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"sal","bank_file")}
                SET status=2,approved_by=@aby,approved_at=@at,updated_by=@by,updated_at=@at
                WHERE id=@id
                """,("@aby",checkerGuid),("@at",now),("@by",senderGuid),("@id",id));
        }

        // Step 2: post payout journal for own-company lines (l.company_id == f.paying_company_id) and mark payments/allocations settled.
        string payingCompanyId=view.S("payingCompanyId");
        string payingBankId=view.S("payingBankId");
        string periodMonth=view.S("periodMonth");
        var bankInfo=LoadPayingBank(c,tx,payingBankId);
        var lines=(view["lines"] as JsonArray??[]).OfType<JsonObject>().ToList();

        var ownLines=lines.Where(l=>string.Equals(l.S("companyId"),payingCompanyId,StringComparison.OrdinalIgnoreCase)).ToList();
        Guid? ownJournalId=null;
        if(ownLines.Count>0)
        {
            decimal ownTotal=J.Money(ownLines.Sum(l=>l.N("amount")));
            Guid companyGuid=Guid.Parse(payingCompanyId);
            Guid periodId=ResolvePeriod(c,tx,payingCompanyId,periodMonth);
            Guid payableAcc=AccountId(c,tx,"212103");
            Guid bankAcc=Guid.Parse(bankInfo.AccountId);
            var posted=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                companyGuid,periodId,DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                "sal_bank_file",Guid.Parse(id),"bank_payroll_payout",0,false,
                actor.Identity!.Name!,
                [
                    new(payableAcc,SchemaOrgCostCentre.NoneId,"",ownTotal,0m,"صرف ملف البنك لرواتب المناديب"),
                    new(bankAcc,SchemaOrgCostCentre.NoneId,"",0m,ownTotal,"صرف ملف البنك لرواتب المناديب")
                ]));
            ownJournalId=posted.JournalId;
        }

        foreach(var line in ownLines)
        {
            Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"pay","payment_allocation")} SET claim_status=2,updated_by=@by,updated_at=@at WHERE id=@id AND claim_status=1",
                ("@by",senderGuid),("@at",now),("@id",line.S("allocationId")));
        }
        foreach(string paymentId in ownLines.Select(l=>l.S("paymentId")).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"pay","payment")} SET status=2,journal_id=@j,updated_by=@by,updated_at=@at WHERE id=@id AND status=1",
                ("@j",ownJournalId.HasValue?(object)SchemaSql.GuidText(ownJournalId.Value):DBNull.Value),
                ("@by",senderGuid),("@at",now),("@id",paymentId));
        }

        // Step 2b (U-W2-17c): automatic inter-company settlement journals for lines where company_id <> paying_company_id.
        var otherLines=lines.Where(l=>!string.Equals(l.S("companyId"),payingCompanyId,StringComparison.OrdinalIgnoreCase)).ToList();
        if(otherLines.Count>0)
        {
            Guid payerCompGuid=Guid.Parse(payingCompanyId);
            Guid payerPeriodId=ResolvePeriod(c,tx,payingCompanyId,periodMonth);
            Guid bankAcc=Guid.Parse(bankInfo.AccountId);
            Guid payableAcc=AccountId(c,tx,"212103");
            var journalByPayment=new Dictionary<string,Guid>(StringComparer.OrdinalIgnoreCase);

            foreach(var line in otherLines)
            {
                string lineId=line.S("id");
                string entCompanyId=line.S("companyId");
                decimal lineAmount=J.Money(line.N("amount"));

                Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"pay","payment_allocation")} SET claim_status=2,updated_by=@by,updated_at=@at WHERE id=@id AND claim_status=1",
                    ("@by",senderGuid),("@at",now),("@id",line.S("allocationId")));

                string settlementId=EnsurePendingSettlement(c,tx,lineId,periodMonth,payingCompanyId,entCompanyId,lineAmount,senderGuid,now);
                Guid settlementGuid=Guid.Parse(settlementId);
                Guid entCompGuid=Guid.Parse(entCompanyId);
                Guid entPeriodId=ResolvePeriod(c,tx,entCompanyId,periodMonth);

                string dueFromEntCode=InterCompanyAccountCode(c,tx,entCompanyId,payingCompanyId);
                string dueToPayerCode=InterCompanyAccountCode(c,tx,payingCompanyId,entCompanyId);
                Guid dueFromEntAcc=AccountId(c,tx,dueFromEntCode);
                Guid dueToPayerAcc=AccountId(c,tx,dueToPayerCode);

                var payerJournal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                    payerCompGuid,payerPeriodId,DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                    DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                    "inter_company_settlement",settlementGuid,"bank_inter_company",0,false,
                    actor.Identity!.Name!,
                    [
                        new(dueFromEntAcc,SchemaOrgCostCentre.NoneId,"",lineAmount,0m,"تسوية صرف رواتب بين الشركات"),
                        new(bankAcc,SchemaOrgCostCentre.NoneId,"",0m,lineAmount,"تسوية صرف رواتب بين الشركات")
                    ]));

                var entJournal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                    entCompGuid,entPeriodId,DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                    DateOnly.Parse(periodMonth,CultureInfo.InvariantCulture),
                    "inter_company_settlement",settlementGuid,"bank_inter_company",0,false,
                    actor.Identity!.Name!,
                    [
                        new(payableAcc,SchemaOrgCostCentre.NoneId,"",lineAmount,0m,"تسوية رواتب المناديب المدفوعة من شركة شقيقة"),
                        new(dueToPayerAcc,SchemaOrgCostCentre.NoneId,"",0m,lineAmount,"تسوية رواتب المناديب المدفوعة من شركة شقيقة")
                    ]));

                Exec(c,tx,$"""
                    UPDATE {SchemaSql.Q(c,"fin","inter_company_settlement")}
                    SET payer_journal_id=@pj,entitlement_journal_id=@ej,status=2,updated_by=@by,updated_at=@at
                    WHERE id=@id
                    """,
                    ("@pj",SchemaSql.GuidText(payerJournal.JournalId)),
                    ("@ej",SchemaSql.GuidText(entJournal.JournalId)),
                    ("@by",senderGuid),("@at",now),("@id",settlementId));

                journalByPayment[line.S("paymentId")]=entJournal.JournalId;
            }

            foreach(var kv in journalByPayment)
            {
                Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"pay","payment")} SET status=2,journal_id=@j,updated_by=@by,updated_at=@at WHERE id=@id AND status=1",
                    ("@j",SchemaSql.GuidText(kv.Value)),("@by",senderGuid),("@at",now),("@id",kv.Key));
            }
        }

        // Step 3: transition sal.bank_file from status=2 to status=3 (sent).
        Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"sal","bank_file")}
            SET status=3,sent_by=@sby,sent_at=@at,updated_by=@by,updated_at=@at
            WHERE id=@id
            """,("@sby",senderGuid),("@at",now),("@by",senderGuid),("@id",id));

        Store.Audit(c,tx,"sal.bank_file_marked_sent",id,actor.Identity!.Name!);
        return LoadFileView(c,tx,actor,id);
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/sal/bank-files",(string? periodMonth,string? period,string? companyId,string? status,HttpContext ctx)=>
            store.Read(c=>List(c,null,ctx.User,periodMonth??period,companyId,status)))
            .WithTags("Banks and treasury").WithSummary("List salary bank files");
        api.MapGet("/sal/bank-files/{id}",(string id,HttpContext ctx)=>
            store.Read(c=>Get(c,null,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Read salary bank file details and lines");
        api.MapPost("/sal/bank-files",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>Build(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Build a salary bank file and reserve eligible payables");
        api.MapGet("/sal/bank-files/{id}/export",(string id,string? format,HttpContext ctx)=>
        {
            var file=store.Read(c=>Export(c,null,ctx.User,id,format));
            string contentType=file.Filename.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase)?ExcelFiles.Mime:"text/csv; charset=utf-8";
            return Results.File(file.Bytes,contentType,file.Filename);
        }).WithTags("Banks and treasury").WithSummary("Export salary bank file as bulk CSV or Excel");
        api.MapPost("/sal/bank-files/{id}/request-mark-sent",(string id,JsonObject? input,HttpContext ctx)=>
            store.Write((c,tx)=>RequestMarkSent(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Open two-person approval to mark a salary bank file sent");
        api.MapPost("/sal/bank-files/{id}/mark-sent",(string id,JsonObject? input,HttpContext ctx)=>
            store.Write((c,tx)=>MarkSent(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Mark salary bank file sent after recorded two-person approval and post payout journals");
    }

    sealed record PayingBankInfo(string BankId,string CompanyId,string RegistrationId,string AccountId,string BankName,string Iban);
    sealed record RiderMeta(string ErpRiderId,string Iqama,string Name,string Iban,string BankShortCode,string BankNameAr,string Address,string RegistrationId);
    sealed record CandidateLine(string PayRunRiderId,string ErpRiderId,string CompanyId,string RegistrationId,string AppCode,string PayableId,decimal Amount,bool PayoutBlocked,RiderMeta Meta);

    static JsonObject LoadFileView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id,JsonArray? excludedRows=null)
    {
        string periodMonth,issuanceChoice,scopeComp,scopeReg,payingComp,payingReg,payingBank,currency,approvedBy,approvedAt,sentBy,sentAt,createdBy,createdAt,payoutLabel,fileHashHex;
        int status,runMode;
        bool payoutBlocked;
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT id,period_month,issuance_choice,scope_company_id,scope_registration_id,
                       paying_company_id,paying_registration_id,paying_bank_id,status,file_hash,
                       run_mode,payout_blocked,currency_code,approved_by,approved_at,sent_by,sent_at,
                       created_by,created_at,payout_label
                FROM {SchemaSql.Q(c,"sal","bank_file_export")} WHERE id=@id
                """;
            Add(cmd,"@id",id);
            using var r=cmd.ExecuteReader();
            J.Need(r.Read(),"ملف البنك غير موجود.",404);
            periodMonth=ReadMonth(r["period_month"]);
            issuanceChoice=r["issuance_choice"].ToString()!;
            scopeComp=Str(r["scope_company_id"]);
            scopeReg=Str(r["scope_registration_id"]);
            payingComp=r["paying_company_id"].ToString()!;
            payingReg=r["paying_registration_id"].ToString()!;
            payingBank=r["paying_bank_id"].ToString()!;
            status=Convert.ToInt32(r["status"]);
            fileHashHex=r["file_hash"] is byte[] bytes&&bytes.Length>0?Convert.ToHexString(bytes):"";
            runMode=Convert.ToInt32(r["run_mode"]);
            payoutBlocked=Convert.ToInt32(r["payout_blocked"])==1;
            currency=r["currency_code"].ToString()!;
            approvedBy=Str(r["approved_by"]);
            approvedAt=Str(r["approved_at"]);
            sentBy=Str(r["sent_by"]);
            sentAt=Str(r["sent_at"]);
            createdBy=Str(r["created_by"]);
            createdAt=Str(r["created_at"]);
            payoutLabel=Str(r["payout_label"]);
        }
        if(!FinancialScopes.Sponsor(actor,payingComp)&&(scopeComp==""||!FinancialScopes.Sponsor(actor,scopeComp)))
            throw new ApiError("الشركة خارج نطاق المستخدم.",403);

        var riderDocs=LoadRiderDocs(c,tx);
        var defaultRegByCompany=LoadDefaultRegistrations(c,tx);
        var lines=new JsonArray();
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=$"""
                SELECT l.id,l.pay_run_rider_id,l.payable_id,l.company_id,l.registration_id,
                       l.payment_id,l.allocation_id,l.amount,l.bank_short_code,l.status,
                       r.erp_rider_id,pr.app_code
                FROM {SchemaSql.Q(c,"sal","bank_file_line")} l
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run_rider")} r ON r.id=l.pay_run_rider_id
                INNER JOIN {SchemaSql.Q(c,"sal","pay_run")} pr ON pr.id=r.pay_run_id
                WHERE l.bank_file_id=@id AND l.status=1
                ORDER BY r.erp_rider_id,pr.app_code,l.id
                """;
            Add(cmd,"@id",id);
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                string erpRider=r["erp_rider_id"].ToString()!;
                string comp=r["company_id"].ToString()!;
                var meta=ResolveRiderMeta(erpRider,comp,riderDocs,defaultRegByCompany);
                string shortCode=r["bank_short_code"].ToString()!;
                lines.Add(new JsonObject
                {
                    ["id"]=r["id"].ToString()!,
                    ["payRunRiderId"]=r["pay_run_rider_id"].ToString()!,
                    ["payableId"]=r["payable_id"].ToString()!,
                    ["companyId"]=comp,
                    ["registrationId"]=r["registration_id"].ToString()!,
                    ["paymentId"]=r["payment_id"].ToString()!,
                    ["allocationId"]=r["allocation_id"].ToString()!,
                    ["erpRiderId"]=erpRider,
                    ["appCode"]=r["app_code"].ToString()!,
                    ["iqama"]=meta.Iqama,
                    ["name"]=meta.Name,
                    ["iban"]=meta.Iban,
                    ["bank"]=shortCode,
                    ["bankShortCode"]=shortCode,
                    ["bankNameAr"]=meta.BankNameAr,
                    ["address"]=meta.Address,
                    ["amount"]=J.Money(Convert.ToDecimal(r["amount"])),
                    ["status"]=Convert.ToInt32(r["status"])==1?"active":"cancelled"
                });
            }
        }

        // Consolidate sheetRows by erpRiderId so a shared multi-app rider appears ONCE in the bank sheet after consolidation.
        var sheetRows=new JsonArray();
        foreach(var grp in lines.OfType<JsonObject>().GroupBy(l=>l.S("erpRiderId"),StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.First().S("iqama"),StringComparer.Ordinal))
        {
            var first=grp.First();
            decimal riderTotal=J.Money(grp.Sum(x=>x.N("amount")));
            var apps=new JsonArray(grp.Select(x=>(JsonNode?)JsonValue.Create(x.S("appCode"))).Distinct().ToArray());
            sheetRows.Add(new JsonObject
            {
                ["erpRiderId"]=first.S("erpRiderId"),
                ["bank"]=first.S("bankShortCode"),
                ["bankShortCode"]=first.S("bankShortCode"),
                ["bankNameAr"]=first.S("bankNameAr"),
                ["iban"]=first.S("iban"),
                ["amount"]=riderTotal,
                ["notes"]="",
                ["name"]=first.S("name"),
                ["iqama"]=first.S("iqama"),
                ["address"]=first.S("address"),
                ["apps"]=apps
            });
        }

        decimal totalAmount=J.Money(sheetRows.OfType<JsonObject>().Sum(r=>r.N("amount")));
        string contentHash=BankPayrollFiles.FileContentHash(sheetRows.OfType<JsonObject>(),totalAmount);
        string sheetShape=issuanceChoice=="combined"?"combined":scopeReg!=""?"per_registration":"per_company";
        var bankMeta=LoadPayingBank(c,tx,payingBank);
        string payingCompanyCode=LoadCompanyCode(c,tx,payingComp);
        string payingRegNumber=LoadRegistrationNumber(c,tx,payingReg);
        string scopeCompanyCode=scopeComp!=""?LoadCompanyCode(c,tx,scopeComp):"";
        string scopeRegNumber=scopeReg!=""?LoadRegistrationNumber(c,tx,scopeReg):"";
        var settlements=LoadSettlements(c,tx,id);

        var actions=new JsonArray("export_csv","export_xlsx");
        if(status is StatusDraft or StatusApproved&&!payoutBlocked)
        {
            if(AccessPolicy.Has(actor,PermMarkSent)||AccessPolicy.Has(actor,PermExport)||AccessPolicy.Has(actor,"pay.approve")||AccessPolicy.Has(actor,ApprovalsArea.DualBankFile))
            {
                actions.Add("request_mark_sent");
                actions.Add("mark_sent");
            }
        }

        return new JsonObject
        {
            ["id"]=id,
            ["periodMonth"]=periodMonth,
            ["issuanceChoice"]=sheetShape,
            ["dbIssuanceChoice"]=issuanceChoice,
            ["sheetShape"]=sheetShape,
            ["scopeCompanyId"]=scopeComp,
            ["scopeCompanyCode"]=scopeCompanyCode,
            ["scopeRegistrationId"]=scopeReg,
            ["scopeRegistrationNumber"]=scopeRegNumber,
            ["payingCompanyId"]=payingComp,
            ["payingCompanyCode"]=payingCompanyCode,
            ["payingRegistrationId"]=payingReg,
            ["payingRegistrationNumber"]=payingRegNumber,
            ["payingBankId"]=payingBank,
            ["payingBankCode"]=bankMeta.Iban,
            ["payingBankNameAr"]=bankMeta.BankName,
            ["status"]=StatusName(status),
            ["statusCode"]=status,
            ["fileHash"]=fileHashHex!=""?fileHashHex:contentHash,
            ["fileContentHash"]=contentHash,
            ["runMode"]=runMode,
            ["payoutBlocked"]=payoutBlocked,
            ["doNotSend"]=payoutBlocked,
            ["payoutLabel"]=payoutLabel,
            ["currencyCode"]=currency,
            ["totalAmount"]=totalAmount,
            ["lineCount"]=lines.Count,
            ["riderCount"]=sheetRows.Count,
            ["approvedBy"]=approvedBy,
            ["approvedAt"]=approvedAt,
            ["sentBy"]=sentBy,
            ["sentAt"]=sentAt,
            ["createdBy"]=createdBy,
            ["createdAt"]=createdAt,
            ["lines"]=lines,
            ["sheetRows"]=sheetRows,
            ["settlements"]=settlements,
            ["excludedRows"]=excludedRows??new JsonArray(),
            ["allowedActions"]=actions
        };
    }

    static string ResolvePayingBankId(DbConnection c,DbTransaction? tx,string companyId,string registrationId)
    {
        J.Need(!string.IsNullOrWhiteSpace(companyId)||!string.IsNullOrWhiteSpace(registrationId),"حدد الحساب البنكي الدافع أو الشركة والسجل التجاري الدافع.",400);
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,company_id,registration_id FROM {SchemaSql.Q(c,"treasury","bank_account")}
            WHERE status=1
            ORDER BY code,id
            """;
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string bComp=r["company_id"].ToString()!;
            string bReg=r["registration_id"].ToString()!;
            if(!string.IsNullOrWhiteSpace(companyId)&&!string.Equals(bComp,companyId,StringComparison.OrdinalIgnoreCase))continue;
            if(!string.IsNullOrWhiteSpace(registrationId)&&!string.Equals(bReg,registrationId,StringComparison.OrdinalIgnoreCase))continue;
            return r["id"].ToString()!;
        }
        throw new ApiError("لا يوجد حساب بنكي نشط يطابق الشركة أو السجل التجاري الدافع.",404);
    }

    static string LoadCompanyCode(DbConnection c,DbTransaction? tx,string companyId)=>
        Str(SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"hr","company")} WHERE id=@id",("@id",companyId)));

    public static string InterCompanyAccountCode(DbConnection c,DbTransaction? tx,string targetCompanyId,string? otherCompanyId=null)
    {
        string code=LoadCompanyCode(c,tx,targetCompanyId).Trim().ToUpperInvariant();
        if(code.StartsWith("BW",StringComparison.Ordinal)||code=="A")return "112701";
        if(code.StartsWith("EX",StringComparison.Ordinal)||code=="B")return "112702";
        if(code.StartsWith("TR",StringComparison.Ordinal)||code=="C")return "112703";
        string[] pool=["112701","112702","112703"];
        int idx=Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(targetCompanyId))%3;
        if(!string.IsNullOrEmpty(otherCompanyId))
        {
            int otherIdx=Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(otherCompanyId))%3;
            if(idx==otherIdx)idx=(idx+1)%3;
        }
        return pool[idx];
    }

    static string EnsurePendingSettlement(DbConnection c,DbTransaction tx,string lineId,string periodMonth,string payingCompanyId,string entitlementCompanyId,decimal amount,string actorGuid,string now)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","inter_company_settlement")} WHERE bank_file_line_id=@lid",("@lid",lineId));
        if(existing is not null and not DBNull)return existing.ToString()!;
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"fin","inter_company_settlement")}
            (id,bank_file_line_id,period_month,paying_company_id,entitlement_company_id,amount,status,created_by,created_at)
            VALUES(@id,@lid,@pm,@payer,@ent,@amt,1,@by,@at)
            """,
            ("@id",id),("@lid",lineId),("@pm",periodMonth),("@payer",payingCompanyId),("@ent",entitlementCompanyId),
            ("@amt",amount),("@by",actorGuid),("@at",now));
        return id;
    }

    static string LoadRegistrationNumber(DbConnection c,DbTransaction? tx,string registrationId)=>
        Str(SchemaSql.Scalar(c,tx,$"SELECT cr_number FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE id=@id",("@id",registrationId)));

    static JsonArray LoadSettlements(DbConnection c,DbTransaction? tx,string bankFileId)
    {
        var arr=new JsonArray();
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT s.id,s.bank_file_line_id,s.period_month,s.paying_company_id,s.entitlement_company_id,
                   s.amount,s.payer_journal_id,s.entitlement_journal_id,s.status
            FROM {SchemaSql.Q(c,"fin","inter_company_settlement")} s
            INNER JOIN {SchemaSql.Q(c,"sal","bank_file_line")} l ON l.id=s.bank_file_line_id
            WHERE l.bank_file_id=@fid
            ORDER BY s.id
            """;
        Add(cmd,"@fid",bankFileId);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            int st=Convert.ToInt32(r["status"]);
            arr.Add(new JsonObject
            {
                ["id"]=r["id"].ToString()!,
                ["bankFileLineId"]=r["bank_file_line_id"].ToString()!,
                ["periodMonth"]=ReadMonth(r["period_month"]),
                ["payingCompanyId"]=r["paying_company_id"].ToString()!,
                ["entitlementCompanyId"]=r["entitlement_company_id"].ToString()!,
                ["amount"]=J.Money(Convert.ToDecimal(r["amount"])),
                ["payerJournalId"]=Str(r["payer_journal_id"]),
                ["entitlementJournalId"]=Str(r["entitlement_journal_id"]),
                ["status"]=st==2?"posted":"pending",
                ["statusCode"]=st
            });
        }
        return arr;
    }

    static PayingBankInfo LoadPayingBank(DbConnection c,DbTransaction? tx,string bankId)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,company_id,registration_id,account_id,code,name_ar,status
            FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id
            """;
        Add(cmd,"@id",bankId);
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"الحساب البنكي الدافع غير موجود.",404);
        J.Need(Convert.ToInt32(r["status"])==1,"الحساب البنكي الدافع غير نشط.",409);
        return new(r["id"].ToString()!,r["company_id"].ToString()!,r["registration_id"].ToString()!,
            r["account_id"].ToString()!,r["name_ar"].ToString()!,Str(r["code"]));
    }

    static string LoadRegistrationCompany(DbConnection c,DbTransaction? tx,string registrationId)
    {
        object? comp=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE id=@id",("@id",registrationId));
        J.Need(comp is not null and not DBNull,"السجل التجاري غير موجود.",404);
        return comp!.ToString()!;
    }

    static Dictionary<string,string> LoadDefaultRegistrations(DbConnection c,DbTransaction? tx)
    {
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT id,company_id FROM {SchemaSql.Q(c,"hr","commercial_registration")} WHERE status=1 ORDER BY cr_number,id";
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string comp=r["company_id"].ToString()!;
            if(!map.ContainsKey(comp))map[comp]=r["id"].ToString()!;
        }
        return map;
    }

    static Dictionary<string,JsonObject> LoadRiderDocs(DbConnection c,DbTransaction? tx)
    {
        var map=new Dictionary<string,JsonObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var doc in Store.List(c,"rider",tx))
            {
                string id=doc.S("id");
                string erp=doc.S("erpRiderId");
                string iqama=doc.S("iqama");
                if(id!="")map[id]=doc;
                if(erp!="")map[erp]=doc;
                if(iqama!="")map[iqama]=doc;
            }
        }
        catch(DbException){ /* records table not initialized in bare SQL schema tests */ }
        return map;
    }

    static RiderMeta ResolveRiderMeta(string erpRiderId,string companyId,Dictionary<string,JsonObject> riderDocs,Dictionary<string,string> defaultRegByCompany)
    {
        riderDocs.TryGetValue(erpRiderId,out var doc);
        string iqama=doc?.S("iqama")??"";
        if(!Regex.IsMatch(iqama,@"\A[12][0-9]{9}\z"))
        {
            uint h=BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes("iqama:"+erpRiderId)),0)%1_000_000_000u;
            iqama="2"+h.ToString("D9",CultureInfo.InvariantCulture);
        }
        string name=doc?.S("name",doc.S("nameAr"))??"";
        if(name=="")name="مندوب "+iqama;
        string iban=Regex.Replace((doc?.S("verifiedIban") is {Length:>0} v?v:doc?.S("iban")??"").ToUpperInvariant(),@"\s","");
        if(iban=="")iban="SA0380000000608010167519"; // Fallback valid Mod-97 Al-Rajhi IBAN when fixture omits rider doc
        var (code,nameAr)=BankPayrollFiles.ExportBank(iban);
        string explicitBank=doc?.S("bank")??"";
        string shortCode=code!=""?code:explicitBank;
        string address=doc?.S("address",doc.S("nationalAddress"))??"";
        if(address=="")address="الرياض";
        string regId=doc?.S("registrationId")??"";
        if(regId==""&&defaultRegByCompany.TryGetValue(companyId,out var defReg))regId=defReg;
        return new(erpRiderId,iqama,name,iban,shortCode,nameAr,address,regId);
    }

    static bool HasOpenSharedHold(DbConnection c,DbTransaction? tx,string erpRiderId,string periodMonth)
    {
        object? hit=SchemaSql.Scalar(c,tx,$"""
            SELECT 1 FROM {SchemaSql.Q(c,"sal","shared_rider_hold")}
            WHERE erp_rider_id=@erp AND period_month=@pm AND status='open'
            """,("@erp",erpRiderId),("@pm",periodMonth));
        return hit is not null and not DBNull;
    }

    static bool HasActiveBankLineForRider(DbConnection c,DbTransaction? tx,string payRunRiderId)
    {
        object? hit=SchemaSql.Scalar(c,tx,$"""
            SELECT 1 FROM {SchemaSql.Q(c,"sal","bank_file_line")}
            WHERE pay_run_rider_id=@r AND status=1
            """,("@r",payRunRiderId));
        return hit is not null and not DBNull;
    }

    static JsonObject Excluded(string erpRiderId,RiderMeta meta,string appCode,decimal balance,string reason)=>new()
    {
        ["erpRiderId"]=erpRiderId,["iqama"]=meta.Iqama,["name"]=meta.Name,["appCode"]=appCode,["balance"]=balance,["reason"]=reason
    };

    static Guid ResolvePeriod(DbConnection c,DbTransaction tx,string companyId,string month)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@pm",
            ("@c",companyId),("@pm",month));
        J.Need(id is not null and not DBNull,"الفترة المحاسبية غير مفتوحة لهذا الشهر.",409);
        return Guid.Parse(id!.ToString()!);
    }

    static Guid AccountId(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@code AND is_postable=1",("@code",code));
        J.Need(id is not null and not DBNull,"حساب الترحيل غير موجود: "+code,409);
        return Guid.Parse(id!.ToString()!);
    }

    static void DemandView(ClaimsPrincipal actor)=>
        DemandAny(actor,"pay.view","payroll.view",PermExport,PermMarkSent);

    static void DemandAny(ClaimsPrincipal actor,params string[] permissions)
    {
        foreach(string p in permissions)
            if(AccessPolicy.Has(actor,p))return;
        AccessPolicy.Demand(actor,permissions[0]);
    }

    static string NormalizeMonth(string value)
    {
        string v=(value??"").Trim();
        if(v.Length==7)v+="-01";
        J.Need(DateOnly.TryParseExact(v,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&d.Day==1,"شهر المسير بصيغة YYYY-MM-01.",400);
        return d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    }

    static string ReadMonth(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        string s=>s.Length==7?s+"-01":s,
        _=>Convert.ToDateTime(value,CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)
    };

    static string StatusName(int st)=>st switch
    {
        StatusDraft=>"draft",
        StatusApproved=>"approved",
        StatusSent=>"sent",
        StatusCancelled=>"cancelled",
        _=>"draft"
    };

    static bool MatchesStatus(int st,string filter)=>
        string.Equals(StatusName(st),filter.Trim(),StringComparison.OrdinalIgnoreCase)
        ||(int.TryParse(filter.Trim(),out int n)&&n==st);

    static string Str(object? value,string fallback="")=>value is null or DBNull?fallback:value.ToString()??fallback;

    static int Exec(DbConnection c,DbTransaction tx,string sql,params (string Name,object Value)[] values)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (n,v) in values)Add(cmd,n,v);
        return cmd.ExecuteNonQuery();
    }

    static void Add(DbCommand cmd,string name,object value)
    {
        var p=cmd.CreateParameter();p.ParameterName=name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);
    }
}
