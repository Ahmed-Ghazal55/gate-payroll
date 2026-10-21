using Payroll;

public static class HostingSecurityTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok) { if (!ok) throw new Exception("Hosting security regression"); checks++; }
        var key = new string('a', 64);
        Check(!HostingSecurity.BootstrapAllowed("", "", false));
        Check(!HostingSecurity.BootstrapAllowed("SET_A_SECURE_24_CHAR_KEY_HERE", "SET_A_SECURE_24_CHAR_KEY_HERE", false));
        Check(!HostingSecurity.BootstrapAllowed("replace_with_24_char_secure_key", "replace_with_24_char_secure_key", false));
        Check(!HostingSecurity.BootstrapAllowed(key, "wrong", false));
        Check(HostingSecurity.BootstrapAllowed(key, key, false));
        Check(HostingSecurity.BootstrapAllowed("", "", true));
        Check(HostingSecurity.SameOrigin("https://payroll.example", "https", "payroll.example"));
        Check(!HostingSecurity.SameOrigin("http://payroll.example", "https", "payroll.example"));
        Check(!HostingSecurity.SameOrigin("https://payroll.example:444", "https", "payroll.example"));
        Check(!HostingSecurity.SameOrigin("https://other.example", "https", "payroll.example"));
        Check(!HostingSecurity.SameOrigin("null", "https", "payroll.example"));
        Check(!HostingSecurity.SameOrigin("https://payroll.example/path", "https", "payroll.example"));
        Check(HostingSecurity.TrustedProxies("").Length == 0);
        Check(HostingSecurity.TrustedProxies("127.0.0.1; ::1").Length == 2);
        try { HostingSecurity.TrustedProxies("*"); throw new Exception("Wildcard trusted proxy accepted"); }
        catch (InvalidOperationException) { checks++; }
        string root = Path.Combine(Path.GetTempPath(), "payroll-path-test");
        Check(HostingSecurity.DataDirectory("App_Data", root, Path.Combine(root, "wwwroot")) == Path.Combine(root, "App_Data"));
        try { HostingSecurity.DataDirectory("wwwroot/data", root, Path.Combine(root, "wwwroot")); throw new Exception("Static data path accepted"); }
        catch (InvalidOperationException) { checks++; }
        Check(DirectoryProfiles.Iqama("2000000021") == "2000000021");
        try { DirectoryProfiles.Iqama("٢٠٠٠٠٠٠٠٢١"); throw new Exception("Non-ASCII directory identity accepted"); }
        catch (ApiError) { checks++; }
        Check(DirectoryProfiles.Name("Sanjit") == "Sanjit");
        Check(DirectoryProfiles.Name("Md. Ahmed") == "Md. Ahmed");
        Check(DirectoryProfiles.Name("عبد-الرحيم محمد") == "عبد-الرحيم محمد");
        Check(DirectoryProfiles.Name("ΑΜΝ Rider") == "ΑΜΝ Rider");
        try { DirectoryProfiles.Name("<script> Rider"); throw new Exception("Unsafe directory name accepted"); }
        catch (ApiError) { checks++; }
        return checks;
    }
}
