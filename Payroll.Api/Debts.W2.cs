using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-20a/b/c + WD1F: NEW endpoints on top of DebtLedger (W1 tables + JournalPosting).</summary>
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
            Guid periodId=DebtLedger.RequireOpenPeriod(c,tx,companyId,periodMonth);
            byte[] bytes=FinancialImports.FileBytes(input);
            // Checker = authenticated user of the approvals decision (never a typed Admin name).
            var checker=ctx.User;
            string fingerprint=input.Required("fingerprint");
            var maker=ResolveMaker(c,tx,input,ctx.User,expectedReference:fingerprint,expectedAmount:input.ContainsKey("totalReduce")?input.N("totalReduce"):null);
            Guid receivable=DebtLedger.AccountId(c,tx,"112501");
            Guid contra=DebtLedger.AccountId(c,tx,"331301");
            return DebtLedger.CommitReduce(c,tx,companyId,periodId,periodMonth,maker,checker,bytes,Path.GetFileName(input.Required("filename")),fingerprint,receivable,contra);
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
            Guid periodId=DebtLedger.RequireOpenPeriod(c,tx,companyId,periodMonth);
            var flags=DebtLedger.ResolveMoneyFlags(c,tx,companyId,periodMonth);
            string cashCode=input.S("cashAccountCode","111101");
            string receivable=DebtLedger.ReceivableForCategory(DebtCategory(c,tx,debtId));
            return DebtLedger.Collect(c,tx,debtId,companyId,periodId,periodMonth,ctx.User,OptionalAmount(input,"collectedAmount",0m),
                input.S("notes",input.S("reason","تحصيل مديونية")),DebtLedger.AccountId(c,tx,receivable),
                DebtLedger.AccountId(c,tx,cashCode),flags.RunMode,flags.PayoutBlocked);
        })).WithTags("Debts and advances").WithSummary("Collect debt on W1 ledger with movement and journal");

        api.MapPost("/debts/{id}/waive",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            Guid companyId=DebtCompany(c,tx,debtId);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=DebtLedger.RequireOpenPeriod(c,tx,companyId,periodMonth);
            var checker=ctx.User;
            decimal amount=Amount(input,"amount");
            var maker=ResolveMaker(c,tx,input,ctx.User,expectedReference:SchemaSql.GuidText(debtId),expectedAmount:amount);
            var expense=DebtLedger.ResolveWaiverExpense(c,tx,debtId);
            string receivable=DebtLedger.ReceivableForCategory(DebtCategory(c,tx,debtId));
            return DebtLedger.Waive(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,input.Required("reason"),expense.Code,
                DebtLedger.AccountId(c,tx,receivable),expense.AccountId);
        })).WithTags("Debts and advances").WithSummary("Waive debt per D11 accounts with maker-checker");

        api.MapPost("/debts/{id}/absorb",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(id,out var debtId),"معرّف المديونية غير صالح.");
            Guid companyId=DebtCompany(c,tx,debtId);
            string period=J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM")));
            var periodMonth=DateOnly.ParseExact(period+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture);
            Guid periodId=DebtLedger.RequireOpenPeriod(c,tx,companyId,periodMonth);
            var checker=ctx.User;
            decimal amount=Amount(input,"amount");
            var maker=ResolveMaker(c,tx,input,ctx.User,expectedReference:SchemaSql.GuidText(debtId),expectedAmount:amount);
            var accounts=DebtLedger.ResolveAbsorbAccounts(c,tx,debtId);
            return DebtLedger.Absorb(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,input.Required("reason"),accounts.ExpenseCode,
                accounts.ReceivableId,accounts.ExpenseId);
        })).WithTags("Debts and advances").WithSummary("Absorb debt to company expense 526602/526603");

        api.MapPost("/promissory-notes/{id}/link-debt",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            J.Need(Guid.TryParse(input.Required("debtId"),out var debtId),"معرّف المديونية غير صالح.");
            return DebtLedger.LinkPromissory(c,tx,id,debtId,ctx.User);
        })).WithTags("Debts and advances").WithSummary("Link promissory note to an open debt");

        api.MapPost("/debts/disburse",(JsonObject _,HttpContext ctx)=>{DebtLedger.DemandDisburseAvailable();return Results.Empty;})
            .WithTags("Debts and advances").WithSummary("Debt disbursement via payment engine (not available until U-W2-07)");
    }

    /// <summary>Maker comes from a prior authenticated approval request (requestedBy), never a typed elevated role.</summary>
    static ClaimsPrincipal ResolveMaker(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction? tx,JsonObject input,ClaimsPrincipal checker,string? expectedReference=null,decimal? expectedAmount=null)
    {
        string requestId=input.S("approvalRequestId");
        if(requestId!="")
        {
            var req=Store.Get(c,requestId,"approval_request",tx)??throw new ApiError("طلب الاعتماد غير موجود.",404);
            J.Need(req.S("status") is "pending" or "approved","طلب الاعتماد غير قابل لإعادة الاستخدام.",409);
            if(expectedReference!=null)J.Need(req.S("referenceId")==expectedReference,"طلب الاعتماد لا يطابق مرجع المديونية أو البصمة.",409);
            if(expectedAmount is decimal amt)
            {
                decimal approved=req.N("amountSar",req.N("amount",req.N("total")));
                J.Need(approved==amt,"مبلغ طلب الاعتماد لا يطابق إجمالي الحركة.",409);
            }
            string makerName=req.S("requestedBy",req.S("createdBy"));
            J.Need(makerName!="","مقدّم الطلب غير معروف على طلب الاعتماد.",400);
            var doc=Store.List(c,"user",tx).FirstOrDefault(u=>UserAccess.Normalize(u.S("username"))==UserAccess.Normalize(makerName))
                ??new JsonObject{["username"]=makerName,["role"]="FinancialAccountant",["department"]=req.S("fromDepartment","FIN"),["rankLevel"]=req.N("requiredRankLevel",1)};
            return UserAccess.Principal(doc);
        }
        // Same authenticated actor may only post when maker≠checker is not required yet; still reject self-check via DemandChecker.
        return checker;
    }
    public static ClaimsPrincipal ResolveMakerForTests(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction? tx,JsonObject input,ClaimsPrincipal checker,string? expectedReference=null,decimal? expectedAmount=null)
        =>ResolveMaker(c,tx,input,checker,expectedReference,expectedAmount);

    static Guid DebtCompany(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,Guid debtId)
    {
        object? company=SchemaSql.Scalar(c,tx,$"SELECT company_id FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        J.Need(company!=null,"المديونية غير موجودة.",404);
        return company is Guid g?g:Guid.Parse(company!.ToString()!);
    }

    static string DebtCategory(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction tx,Guid debtId)=>
        SchemaSql.Scalar(c,tx,$"SELECT category FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))?.ToString()??"CashAdvance";
}
