using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Payroll;

public sealed record WorkChoice(string Iqama, string Name, string WorkSystem);
public static class WorkChoices
{
    public static WorkChoice Validate(JsonObject input)
    {
        J.Need(input.All(p => p.Key is "iqama" or "name" or "workSystem"), "هذا النموذج يجمع الإقامة والاسم واختيار النظام فقط.");
        // Do not normalize Arabic/Persian digits here: reject them on both client and server.
        string iqama = input.Required("iqama");
        J.Need(Regex.IsMatch(iqama, "^[0-9]{10}$"), "رقم الإقامة يجب أن يكون 10 أرقام إنجليزية فقط (0–9)، دون مسافات أو حروف.");
        string name = Regex.Replace(input.Required("name").Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        bool Letter(char c) => char.IsAsciiLetter(c) || (c is >= '\u0600' and <= '\u06FF' && char.IsLetter(c));
        bool Mark(char c) => c is >= '\u064B' and <= '\u065F' or '\u0670';
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        J.Need(name.Length is >= 3 and <= 150 && words.Length >= 2 && words.All(w => w.Any(Letter)) && name.All(c => c == ' ' || Letter(c) || Mark(c)), "اكتب الاسم من جزأين على الأقل، بحروف عربية أو إنجليزية ومسافات فقط، وبحد أقصى 150 حرفًا.");
        string system = input.Required("workSystem");
        J.Need(system is "tiers" or "commission", "اختر نظام الراتب الجديد أو نظام العمولة السابق.");
        return new(iqama, name, system);
    }
}
