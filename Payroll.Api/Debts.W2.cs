using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-20a/b/c NEW endpoints on top of DebtLedger (W1 tables + JournalPosting).</summary>
public static partial class Debts
{
    public static void MapW2(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/debts/import/template",()=>
        {
            var cols=OpeningTemplateColumns();
            cols.Add(new("وضع الصف (opening/add/reduce)",false,new[]{"opening","add","reduce"}));
            cols.Add(new("سبب التخفيض (إلزامي عند reduce)"));
            string cur=DateTime.UtcNow.ToString("yyyy-MM");
            var rows=new List<object?[]>
            {
                new object?[]{"2491234567","مثال مندوب","Rider","CashAdvance","BW","OpeningBalance","RiderFull",1000m,0m,0m,200m,cur,"3101","OB-1","افتتاحي","","opening",""},
                new object?[]{"2491234567","مثال مندوب","Rider","CashAdvance","BW","OpeningBalance","RiderFull",100m,0m,0m,50m,cur,"3101","ADD-1","إضافة","","add",""},
                new object?[]{"2491234567","مثال مندوب","","CashAdvance","BW","","",40m,0m,0m,0m,cur,"","","","","reduce","تصحيح رصيد افتتاحي"}
            };
            return Results.File(ExcelFiles.Write(cols,rows),ExcelFiles.Mime,"قالب_استيراد_المديونيات_موحّد.xlsx");
        }).WithTags("Debts and advances").WithSummary("Read unified debts import template (opening/add/reduce)");

        api.MapPost("/debts/import/reduce/preview",(JsonObject input,HttpContext ctx)=>store.Read(c=>
        {
            string company=input.Required("companyId");
            J.Need(Guid.TryParse(company,out var companyId),"معرّف الشركة غير صالح.");
            FinancialScopes.DemandSponsor(ctx.User,company);
            byte[] bytes=FinancialImports.FileBytes(input);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            return DebtLedger.PreviewReduce(c,null,companyId,ctx.User,bytes,Path.GetFileName(input.Required("filename")),DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture));
        })).WithTags("Debts and advances").WithSummary("Preview debt sheet reduce import");

        api.MapPost("/debts/import/reduce/commit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            string company=input.Required("companyId");
            J.Need(Guid.TryParse(company,out var companyId),"معرّف الشركة غير صالح.");
            FinancialScopes.DemandSponsor(ctx.User,company);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=OpenPeriod(c,tx,companyId,periodMonth);
            byte[] bytes=FinancialImports.FileBytes(input);
            string checkerName=input.S("checkerUsername");
            J.Need(checkerName!="","معتمد مستقل مطلوب لتخفيض الشيت.",400);
            var checker=new ClaimsPrincipal(new ClaimsIdentity([new System.Security.Claims.Claim(ClaimTypes.Name,checkerName),new System.Security.Claims.Claim(ClaimTypes.Role,"Admin")],"debt-reduce"));
            return DebtLedger.CommitReduce(c,tx,companyId,periodId,periodMonth,ctx.User,checker,bytes,Path.GetFileName(input.Required("filename")),input.Required("fingerprint"),
                DebtLedger.AccountId(c,tx,input.S("receivableAccountCode","112501")),DebtLedger.AccountId(c,tx,input.S("contraAccountCode","3101")));
        })).WithTags("Debts and advances").WithSummary("Commit debt sheet reduce import with approval");

        api.MapPost("/debts/{id}/reschedule",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            return DebtLedger.Reschedule(c,tx,debtId,ctx.User,Debts.Amount(input,"monthlyInstallment"));
        })).WithTags("Debts and advances").WithSummary("Reschedule debt instalment");

        api.MapGet("/debts/ageing",(string companyId,HttpContext ctx)=>store.Read(c=>
        {
            J.Need(Guid.TryParse(companyId,out var company),"معرّف الشركة غير صالح.");
            FinancialScopes.DemandSponsor(ctx.User,companyId);
            return DebtLedger.Ageing(c,null,company,ctx.User);
        })).WithTags("Debts and advances").WithSummary("Read debt ageing buckets");

        api.MapGet("/debts/print",(string companyId,string? status,HttpContext ctx)=>store.Read(c=>
        {
            J.Need(Guid.TryParse(companyId,out var company),"معرّف الشركة غير صالح.");
            FinancialScopes.DemandSponsor(ctx.User,companyId);
            return DebtLedger.PrintRegister(c,null,company,ctx.User,status);
        })).WithTags("Debts and advances").WithSummary("Print-ready debts register");

        api.MapPost("/debts/{id}/collect-ledger",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            Guid companyId=DebtCompany(c,tx,debtId);
            FinancialScopes.DemandSponsor(ctx.User,SchemaSql.GuidText(companyId));
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=OpenPeriod(c,tx,companyId,periodMonth);
            byte runMode=(byte)input.N("runMode",0);bool blocked=input.B("payoutBlocked");
            return DebtLedger.Collect(c,tx,debtId,companyId,periodId,periodMonth,ctx.User,OptionalAmount(input,"collectedAmount",0m),
                input.S("notes",input.S("reason","تحصيل مديونية")),DebtLedger.AccountId(c,tx,input.S("receivableAccountCode","112501")),
                DebtLedger.AccountId(c,tx,input.S("cashAccountCode","111101")),runMode,blocked);
        })).WithTags("Debts and advances").WithSummary("Collect debt on W1 ledger with movement and journal");

        api.MapPost("/debts/{id}/waive",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            Guid companyId=DebtCompany(c,tx,debtId);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=OpenPeriod(c,tx,companyId,periodMonth);
            string checkerName=input.Required("checkerUsername");
            var checker=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,checkerName),new Claim(ClaimTypes.Role,"Admin")],"debt-waive"));
            string expense=input.S("expenseAccountCode","526601");
            return DebtLedger.Waive(c,tx,debtId,companyId,periodId,periodMonth,ctx.User,checker,Amount(input,"amount"),input.Required("reason"),expense,
                DebtLedger.AccountId(c,tx,input.S("receivableAccountCode","112501")),DebtLedger.AccountId(c,tx,expense));
        })).WithTags("Debts and advances").WithSummary("Waive debt per D11 accounts with maker-checker");

        api.MapPost("/debts/{id}/absorb",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            Guid companyId=DebtCompany(c,tx,debtId);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=OpenPeriod(c,tx,companyId,periodMonth);
            string checkerName=input.Required("checkerUsername");
            var checker=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,checkerName),new Claim(ClaimTypes.Role,"Admin")],"debt-absorb"));
            string expense=input.S("expenseAccountCode","526602");
            J.Need(expense is "526602" or "526603","تحمل الشركة على 526602 أو 526603 فقط.");
            return DebtLedger.Absorb(c,tx,debtId,companyId,periodId,periodMonth,ctx.User,checker,Amount(input,"amount"),input.Required("reason"),expense,
                DebtLedger.AccountId(c,tx,input.S("receivableAccountCode","112501")),DebtLedger.AccountId(c,tx,expense));
        })).WithTags("Debts and advances").WithSummary("Absorb debt to company expense 526602/526603");

        api.MapPost("/promissory-notes/{id}/link-debt",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(input.Required("debtId"),out var debtId),"معرّف المديونية غير صالح.");
            return DebtLedger.LinkPromissory(c,tx,id,debtId,ctx.User);
        })).WithTags("Debts and advances").WithSummary("Link promissory note to an open debt");

        api.MapPost("/debts/disburse",(JsonObject _,HttpContext ctx)=>{DebtLedger.DemandDisburseAvailable();return Results.Empty;})
            .WithTags("Debts and advances").WithSummary("Debt disbursement via payment engine (not available until U-W2-07)");
    }

    static Guid DebtCompany(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,Guid debtId)
    {
        object? company=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        J.Need(company!=null,"المديونية غير موجودة.",404);
        return company is Guid g?g:Guid.Parse(company!.ToString()!);
    }

    static Guid OpenPeriod(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,Guid companyId,DateOnly periodMonth)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m AND state=1",
            ("@c",SchemaSql.GuidText(companyId)),("@m",periodMonth.ToString("yyyy-MM-01",CultureInfo.InvariantCulture)));
        if(id!=null)return id is Guid g?g:Guid.Parse(id.ToString()!);
        Guid periodId=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,@at)",
            ("@id",SchemaSql.GuidText(periodId)),("@c",SchemaSql.GuidText(companyId)),("@m",periodMonth.ToString("yyyy-MM-01",CultureInfo.InvariantCulture)),
            ("@at",DateTimeOffset.UtcNow.ToString("O")));
        return periodId;
    }
}
