using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;
public static class DebtTransfers
{
    public static JsonObject Post(DbConnection c,DbTransaction tx,string id,JsonObject input,ClaimsPrincipal user)
    {
            var d = Store.Get(c, id, "debt", tx) ?? throw new ApiError("الدين غير موجود.", 404);
            FinancialScopes.DemandDebt(user,d);
            J.Need(d.S("category")!="PlatformTransferredDebt","إحالة المنصة مرتبطة بحق والتزام؛ لا تُنقل بمسار شراء السلف العام.");
            decimal remaining = d.N("remainingBalance");
            J.Need(remaining > 0&&d.S("status") is "active" or "paused", "لا يمكن نقل مديونية غير نشطة أو مسددة.");

            var oldSp = FinancialCore.RequireDebtSponsor(d);
            var newSp = FinancialCore.ResolveSponsor(input.Required("newCreditorSponsorId"));
            FinancialScopes.DemandSponsor(user,newSp.Id);
            J.Need(FinancialCore.FindSponsor(input.S("expectedCreditorSponsorId"))?.Id==oldSp.Id,"تغير مالك الدين أو لم تؤكد المالك؛ أعد المعاينة.");
            J.Need(input.ContainsKey("expectedRemainingBalance")&&input.N("expectedRemainingBalance")==remaining,"تغير الرصيد أو لم تؤكد معاينته؛ أعد فتح نقل المديونية.");
            J.Need(AccountingEvents.HasDebtOpening(d),"طابق الرصيد الافتتاحي قبل نقل الدين القديم.");
            J.Need(AccountingEvents.DebtBalance(c,tx,d,oldSp.Id)==remaining,"رصيد الذمة يختلف عن رصيد الدين؛ طابقه قبل النقل.");
            J.Need(oldSp.Id != newSp.Id, "السجل المشتري للمديونية يجب أن يكون مختلفاً عن السجل الدائن الحالي.");

            string transferRef = input.Required("transferRef");
            string receivCode = AccountingEvents.ReceivableCode(d);
            string settlementMode = input.S("settlementMode", "intercompany");
            J.Need(settlementMode is "intercompany" or "cash_purchase","طريقة نقل غير صالحة."); // intercompany | cash_purchase

            var lines = new JsonArray
            {
                FinancialCore.CreateJournalLine(receivCode, newSp.Id, remaining, 0m, $"إثبات شراء/استحواذ ({newSp.ShortNameAr}) على مديونية المندوب ({d.S("iqama")})", d.S("iqama"), id),
                FinancialCore.CreateJournalLine(receivCode, oldSp.Id, 0m, remaining, $"إقفال وتنازل ({oldSp.ShortNameAr}) عن مديونية المندوب لصالح ({newSp.ShortNameAr})", d.S("iqama"), id)
            };

            if (settlementMode == "cash_purchase")
            {
                string buyerVault = FinancialCore.DefaultVaultForSponsor(newSp.Id, "MAIN");
                string sellerVault = FinancialCore.DefaultVaultForSponsor(oldSp.Id, "MAIN");
                FinancialScopes.DemandAccount(user,buyerVault);FinancialScopes.DemandAccount(user,sellerVault);AccountingEvents.DemandAvailable(c,tx,buyerVault,remaining);
                lines.Add(FinancialCore.CreateJournalLine(sellerVault, oldSp.Id, remaining, 0m, $"قبض ثمن بيع المديونية من ({newSp.ShortNameAr})", d.S("iqama"), id));
                lines.Add(FinancialCore.CreateJournalLine(buyerVault, newSp.Id, 0m, remaining, $"سداد ثمن شراء المديونية إلى ({oldSp.ShortNameAr})", d.S("iqama"), id));
            }
            else
            {
                lines.Add(FinancialCore.CreateJournalLine("1310", oldSp.Id, remaining, 0m, $"إثبات مديونية على ({newSp.ShortNameAr}) مقابل شراء ذمة المندوب", d.S("iqama"), id));
                lines.Add(FinancialCore.CreateJournalLine("1310", newSp.Id, 0m, remaining, $"إثبات التزام لصالح ({oldSp.ShortNameAr}) مقابل شراء ذمة المندوب", d.S("iqama"), id));
            }

            var journal = FinancialCore.PostJournal(
                c, tx,
                "intercompany_debt_transfer",
                $"شراء وترحيل مديونية المندوب ({d.S("iqama")}) من ({oldSp.ShortNameAr}) إلى ({newSp.ShortNameAr})",
                DateTime.UtcNow.ToString("yyyy-MM"),
                transferRef,
                lines,
                user.Identity!.Name!,
                d.S("iqama"),
                id,
                oldSp.Id,
                newSp.Id
            );

            var transfer=new JsonObject{["id"]="debt-transfer:"+journal.S("id"),["debtId"]=id,["iqama"]=d.S("iqama"),["fromSponsorId"]=oldSp.Id,["toSponsorId"]=newSp.Id,["amount"]=remaining,["createdAt"]=journal.S("createdAt"),["journalEntryId"]=journal.S("id"),["sourceRef"]=transferRef,["actor"]=user.Identity!.Name!,["version"]=(int)d.N("ownershipVersion",1)+1};
            Store.Save(c,"debt_transfer",transfer,tx);
            var history=d["ownershipHistory"] as JsonArray??new JsonArray();if(d["ownershipHistory"]==null)d["ownershipHistory"]=history;history.Add(transfer.Clone());d["ownershipVersion"]=transfer.N("version");
            d["previousCreditorSponsorId"] = oldSp.Id;
            d["previousCreditorSponsorNameAr"] = oldSp.ShortNameAr;
            d["creditorSponsorId"] = newSp.Id;
            d["creditorSponsorNameAr"] = newSp.ShortNameAr;
            d["transferredAt"] = DateTimeOffset.UtcNow.ToString("O");
            d["transferJournalId"] = journal.S("id");
            Store.Save(c, "debt", d, tx);

            Store.Audit(c, tx, "debt.transferred_sponsor", id, user.Identity!.Name!);
            return new JsonObject{["debt"]=d,["journal"]=journal};
    }
}
