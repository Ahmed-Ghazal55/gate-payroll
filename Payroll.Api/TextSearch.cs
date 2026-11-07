using System.Globalization;
using System.Text;
namespace Payroll;

// One search normaliser for every list: Arabic letter variants, diacritics, tatweel and
// Arabic-Indic digits fold to one form, so a name, iqama, phone or city typed any way matches.
public static class TextSearch
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            char c = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch
            };
            if (c == 'ـ') continue; // tatweel
            if (c is >= 'ً' and <= 'ٟ' || c == 'ٰ') continue; // harakat
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Every query word must appear somewhere in the values (order-free), e.g. "محمد 2000".
    public static bool Matches(string query, params string?[] values)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        string text = Normalize(string.Join(' ', values.Where(v => !string.IsNullOrEmpty(v))));
        // Phones are stored with or without the 966 prefix / leading zero; digits-only also match.
        string digits = new(text.Where(char.IsAsciiDigit).ToArray());
        return words.All(w => text.Contains(w, StringComparison.Ordinal) || w.All(char.IsAsciiDigit) && w.TrimStart('0').Length >= 3 && digits.Contains(w.TrimStart('0'), StringComparison.Ordinal));
    }
}
