using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Payroll;

public static class HostingSecurity
{
    public static bool SameOrigin(string origin, string scheme, string host)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme is "http" or "https")
            && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/"
            && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase);
    }

    public static bool BootstrapAllowed(string configured, string supplied, bool localDevelopment)
    {
        if (localDevelopment) return true;
        if (!StrongBootstrapKey(configured)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configured), Encoding.UTF8.GetBytes(supplied));
    }

    public static bool StrongBootstrapKey(string value)
    {
        if (value.Length < 32 || value.Length > 256 || value.Any(char.IsWhiteSpace)) return false;
        return !new[] { "replace", "set_a_", "example", "change", "placeholder" }
            .Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static IPAddress[] TrustedProxies(string configured) => configured
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => IPAddress.TryParse(value, out var address) ? address
            : throw new InvalidOperationException("PAYROLL_KNOWN_PROXIES must contain IP addresses only."))
        .ToArray();

    public static string DataDirectory(string configured, string contentRoot, string webRoot)
    {
        var path = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured));
        var web = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Equals(web, comparison) || path.StartsWith(web + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Payroll data must be outside the static web root.");
        return path;
    }
}
