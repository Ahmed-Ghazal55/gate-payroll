using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static class JahezTreasury
{
    public static decimal ControlBalance(DbConnection c,DbTransaction tx,string account,string sponsor)=>J.Money(Store.List(c,"journal",tx).SelectMany(j=>j["lines"]!.AsArray()).OfType<JsonObject>().Where(l=>l.S("accountCode")==account&&l.S("sponsorId")==sponsor).Sum(l=>l.N("debit")-l.N("credit")));
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/finance/jahez-transfer",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            string from=input.Required("fromVaultCode"),to=input.Required("toVaultCode");var source=FinancialCore.ResolveVault(from);var target=FinancialCore.ResolveVault(to);
            J.Need(source.DefaultSponsorId==target.DefaultSponsorId&&from!=to,"حسابان مختلفان لنفس السجل مطلوبان.");
            J.Need(source.VaultKind!="JahezCashbox","خروج عهدة المشرف من مسار تسليم العهدة فقط.");
            J.Need(source.VaultPurpose is "GeneralAndSettlement" or "JahezSettlement"&&target.VaultPurpose is "GeneralAndSettlement" or "JahezSettlement","هذا المسار خاص بأموال تصفيات جاهز فقط.");
            FinancialScopes.DemandAccount(ctx.User,from);FinancialScopes.DemandAccount(ctx.User,to);decimal amount=Debts.Amount(input,"amount");AccountingEvents.DemandAvailable(c,tx,from,amount);
            string owner=source.DefaultSponsorId!;var journal=FinancialCore.PostJournal(c,tx,"jahez_custody_transfer","نقل عهدة جاهز دون إيراد أو تغيير ذمة مندوب",J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM"))),input.Required("sourceRef"),new JsonArray(
                FinancialCore.CreateJournalLine(to,owner,amount,0,input.Required("notes")),FinancialCore.CreateJournalLine(from,owner,0,amount,input.S("notes"))),ctx.User.Identity!.Name!,"","",owner,owner);
            Store.Audit(c,tx,"jahez.custody_transferred",journal.S("id"),ctx.User.Identity!.Name!);return journal;
        }));
        api.MapPost("/finance/jahez-platform-settlement",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            string action=input.Required("action");J.Need(action is "pay" or "receive","اتجاه التسوية غير صالح.");var sponsor=FinancialCore.ResolveSponsor(input.Required("sponsorId"));string vault="BANK."+sponsor.Code+".JAHEZ";
            FinancialScopes.DemandAccount(ctx.User,vault);decimal amount=Debts.Amount(input,"amount");string control=action=="pay"?"2140":"1240";
            decimal remaining=ControlBalance(c,tx,control,sponsor.Id)*(action=="pay"?-1:1);J.Need(amount<=remaining,"التسوية تتجاوز الرصيد المثبت لشركة جاهز.");
            if(action=="pay")AccountingEvents.DemandAvailable(c,tx,vault,amount);
            var lines=action=="pay"?new JsonArray(FinancialCore.CreateJournalLine(control,sponsor.Id,amount,0,"سداد مستحق جاهز"),FinancialCore.CreateJournalLine(vault,sponsor.Id,0,amount,input.Required("notes"))):new JsonArray(FinancialCore.CreateJournalLine(vault,sponsor.Id,amount,0,"تحصيل تسوية منصة جاهز"),FinancialCore.CreateJournalLine(control,sponsor.Id,0,amount,input.Required("notes")));
            var journal=FinancialCore.PostJournal(c,tx,"jahez_platform_settlement","تسوية مباشرة مع شركة جاهز",J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM"))),input.Required("sourceRef"),lines,ctx.User.Identity!.Name!,"","",sponsor.Id,sponsor.Id);
            Store.Audit(c,tx,"jahez.platform_settled",journal.S("id"),ctx.User.Identity!.Name!);return journal;
        }));
        api.MapPost("/finance/jahez-fee-deposit",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>
        {
            var sponsor=FinancialCore.ResolveSponsor(input.Required("sponsorId"));string from="MAIN."+sponsor.Code+".FEE",to="BANK."+sponsor.Code;
            FinancialScopes.DemandAccount(ctx.User,from);FinancialScopes.DemandAccount(ctx.User,to);decimal amount=Debts.Amount(input,"amount");AccountingEvents.DemandAvailable(c,tx,from,amount);
            var journal=FinancialCore.PostJournal(c,tx,"company_fee_deposit","إيداع حصيلة رسوم الشركة في البنك دون إثبات إيراد ثان",J.Period(input.S("period",DateTime.UtcNow.ToString("yyyy-MM"))),input.Required("sourceRef"),new JsonArray(
                FinancialCore.CreateJournalLine(to,sponsor.Id,amount,0,input.Required("notes")),FinancialCore.CreateJournalLine(from,sponsor.Id,0,amount,"إيداع حصيلة رسوم محصلة سابقًا")),ctx.User.Identity!.Name!,"","",sponsor.Id,sponsor.Id);
            Store.Audit(c,tx,"jahez.fee_deposited",journal.S("id"),ctx.User.Identity!.Name!);return journal;
        }));
    }
}
