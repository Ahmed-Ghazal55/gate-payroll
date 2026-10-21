using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Payroll;

public static class DirectoryProfiles
{
    public static string Iqama(string value)
    {
        string identity = value.Trim();
        J.Need(Regex.IsMatch(identity, "^[12][0-9]{9}$"), "الإقامة في قالب الدليل يجب أن تكون10أرقام إنجليزية وتبدأ بـ1أو2.");
        return identity;
    }
    // Administrative source names are evidence, including mononyms and other scripts.
    // This is separate from the stricter three-field public choice form.
    public static string Name(string value)
    {
        string name = Regex.Replace(value.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        bool Mark(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
        J.Need(name.Length is >= 2 and <= 200 && name.Count(char.IsLetter) >= 2
            && name.All(c => char.IsLetter(c) || Mark(c) || c is ' ' or '-' or '.' or '\''),
            "اسم الدليل يجب أن يحتوي حروفًا فعلية دون أرقام أو محارف تحكم أو رموز غير مسموحة، وبحد أقصى200حرف.");
        return name;
    }
}
