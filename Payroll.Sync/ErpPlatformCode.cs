namespace ErpImport;

/// <summary>Single map from payroll app codes to live ERP <c>ClientPlatforms.Code</c> values (A4).</summary>
public static class ErpPlatformCode
{
    // Primary ERP code first; further values are accepted on read.
    static readonly Dictionary<string,string[]> Map=new(StringComparer.Ordinal)
    {
        ["ninja"]=["NINJA"],
        ["keeta"]=["KEETA"],
        ["hs"]=["HUNGER"],
        ["chefz"]=["SHIFTZ","CHEFZ","THECHEFZ","CHEFSZ"],
        ["jahez"]=["JAHEZ"],
        ["amazon"]=["AMAZON"],
    };

    public static string Primary(string app)
    {
        string code=Normalize(app);
        if(!Map.TryGetValue(code,out var codes))throw new ImportFailure("live_ops_unknown_platform_app:"+app);
        return codes[0];
    }

    public static IReadOnlyList<string> Accept(string app)
    {
        string code=Normalize(app);
        if(!Map.TryGetValue(code,out var codes))throw new ImportFailure("live_ops_unknown_platform_app:"+app);
        return codes;
    }

    public static string? AppFromErp(string erpCode)
    {
        string c=(erpCode??"").Trim();
        if(c.Length==0)return null;
        foreach(var (app,codes) in Map)
            if(codes.Any(x=>string.Equals(x,c,StringComparison.OrdinalIgnoreCase)))return app;
        return null;
    }

    static string Normalize(string value)
    {
        string code=value.Trim().ToLowerInvariant().Replace(" ","-").Replace("_","-");
        return code switch
        {
            "hungerstation" or "hunger-station" or "hunger"=>"hs",
            "the-chefz" or "thechefz" or "shiftz"=>"chefz",
            _=>code
        };
    }
}
