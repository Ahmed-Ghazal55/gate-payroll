using System.Text.Json.Nodes;
namespace Payroll;

// K6: one ordered list of every import, its empty template and its columns, so the Import Center
// shows what to load first. Template columns come from the same definitions the download uses.
public static class ImportCatalog
{
    public sealed record Step(int Order,string Key,string Group,string LabelAr,string PurposeAr,string[] DependsOn,string Permission,
        string? TemplateUrl,string? PreviewUrl,string? CommitUrl,string Upload,string Status,IReadOnlyList<ExcelColumn> Columns,string[] Required,string? NoteAr=null);

    // Companies import takes JSON rows keyed by these fields (CompanyRegistryImport).
    public static readonly ExcelColumn[] CompanyColumns=[new("code",Identity:true,Choices:["BW","EX","TR"]),new("nameAr"),new("nameEn"),new("unifiedNumber",Identity:true),new("crNumber",Identity:true),
        new("crType"),new("entityType"),new("city"),new("address"),new("issueDate"),new("annualConfirmation"),new("capitalSar",Numeric:true),new("status",Choices:["active","inactive"])];

    // Per-app operations: one row per rider per platform account per period. It feeds the pay-cycle
    // invoice review accounts[].riders[] (orders, baseGross share, days, category, metrics). Codex D3
    // finalises the app-specific metric columns from the real invoices; these are the shared core.
    static ExcelColumn[] Operations(params ExcelColumn[] metrics)=>[new("period (YYYY-MM)"),new("platformAccountId",Identity:true),new("iqama",Identity:true),new("riderName"),
        new("from (YYYY-MM-DD)"),new("to (YYYY-MM-DD)"),new("days",Numeric:true),new("orders",Numeric:true),new("baseGross",Numeric:true),new("category",Choices:["car","bike"]),..metrics];
    static readonly string[] OperationsRequired=["period (YYYY-MM)","platformAccountId","iqama","days","orders","category"];

    public static IReadOnlyList<Step> Steps()=>
    [
        new(1,"companies","setup","الشركات (السجلات التجارية)","أساس كل الأرصدة والقيود: كل مديونية وحساب يتبع سجلًا.",[],"Admin",
            "/api/imports/templates/companies","/api/companies/import/preview","/api/companies/import/commit","json-rows","ready",CompanyColumns,["code","nameAr","unifiedNumber","crNumber"],
            "الصفوف تُرسل JSON بنفس أسماء الأعمدة؛ حتى 100 شركة."),
        new(2,"riders","setup","المناديب","هوية المندوب وشركته ونوع المركبة؛ كل الاستيرادات التالية تطابق برقم الإقامة.",["companies"],"riders.edit",
            "/api/excel/riders/template","/api/excel/riders/preview","/api/excel/riders/commit","xlsx","ready",TableExcelApi.Columns("riders"),["رقم الإقامة","الاسم الكامل","الكفيل"],
            "المناديب المسحوبين من ERP لا يحتاجوا رفع؛ الملف للتكملة فقط."),
        new(3,"treasury_opening","opening","الأرصدة الافتتاحية للخزائن والبنوك","رصيد كل صندوق وبنك أول المدة، قبل أي صرف.",["companies"],"treasury.opening",
            "/api/finance/opening/template","/api/finance/opening/preview","/api/finance/opening/commit","xlsx","ready",TreasuryEntries.OpeningColumns,["accountCode","sponsorCode","verifiedBalance"]),
        new(4,"opening_debts","opening","المديونيات الافتتاحية","رصيد مديونية كل مندوب بنهاية الشهر بالبند والسجل الدائن والقسط.",["riders","treasury_opening"],"debts.import_opening",
            "/api/debts/import-opening/template","/api/debts/import-opening/preview","/api/debts/import-opening/commit","xlsx","ready",Debts.OpeningTemplateColumns(),
            ["رقم الإقامة (10 أرقام)","كود البند أو الخدمة (Category)","السجل التجاري الدائن (BW/EX/TR)","إجمالي مبلغ المديونية على المندوب","القسط الشهري المستقطع","شهر بداية الاستقطاع (YYYY-MM)"],
            "شيت المديونيات الكبير (نهاية أكتوبر) يتحمّل بأداة المطابقة R6c بعد «حمّل»، مش من هنا."),
        new(5,"bulk_collect","opening","التحصيل المجمع للمديونيات","سداد نقدي أو إعفاء على مديونيات قائمة.",["opening_debts"],"debts.collect",
            "/api/debts/bulk-collect/template","/api/debts/bulk-collect/preview-file","/api/debts/bulk-collect/commit","xlsx","ready",Debts.BulkCollectColumns([]),["رقم الإقامة","المبلغ المحصل","رمز الصندوق أو البنك","رقم السند / المرجع"]),
        new(6,"ninja_invoice","invoices","فاتورة نينجا","الفاتورة المعتمدة هي النهائي (BR §31)؛ الحساب المتوقع للعلم.",["riders"],"pay.manage",
            null,"/api/platforms/ninja/invoices","/api/platforms/ninja/invoices?commit=1","platform-file","ready",[],[],"ارفع ملف الفاتورة الأصلي من نينجا كما هو (43 عمودًا)."),
        new(7,"ninja_operations","operations","تشغيل نينجا للمناديب","مين اشتغل على كل حساب، وكام طلب ويوم، مطابقًا لسطور الفاتورة.",["ninja_invoice"],"pay.manage",
            "/api/imports/templates/operations-ninja",null,null,"xlsx","template_only",Operations(new("captainId",Identity:true),new("attendanceDays",Numeric:true),new("hours8Days",Numeric:true),new("weekendDays",Numeric:true)),OperationsRequired,
            "المعاينة والاعتماد يكتملوا في R6c-D3."),
        new(8,"keeta_invoice","invoices","فاتورة كيتا","فاتورة الشرائح (الرياض / جدة).",["riders"],"pay.manage",
            null,"/api/platforms/keeta/invoices","/api/platforms/keeta/invoices?commit=1","platform-file","ready",[],[],"ارفع ورقة الفاتورة الأصلية؛ حدد sheetName لو الملف متعدد الأوراق."),
        new(9,"keeta_operations","operations","تشغيل كيتا للمناديب","الطلبات والأيام لكل مندوب على كل حساب كيتا.",["keeta_invoice"],"pay.manage",
            "/api/imports/templates/operations-keeta",null,null,"xlsx","template_only",Operations(new("keetaCourierId",Identity:true),new("city",Choices:["الرياض","جدة"]),new("onlineHours",Numeric:true),new("slab")),OperationsRequired,
            "المعاينة والاعتماد يكتملوا في R6c-D3."),
        new(10,"hungerstation_invoice","invoices","فاتورة هنقرستيشن","الفاتورة الشهرية لكل سجل.",["riders"],"platforms.hs.manage",
            null,"/api/platforms/hs/invoices/import/preview","/api/platforms/hs/invoices/import/commit","platform-file","ready",[],[]),
        new(11,"hungerstation_operations","operations","تشغيل هنقرستيشن للمناديب","الطلبات والساعات لكل مندوب على كل حساب.",["hungerstation_invoice"],"pay.manage",
            "/api/imports/templates/operations-hungerstation",null,null,"xlsx","template_only",Operations(new("hsRiderId",Identity:true),new("hours",Numeric:true)),OperationsRequired,
            "بعد اعتماد CR-003 ممكن تيجي من ERP مباشرة بدل الرفع."),
        new(12,"chefz_operations","operations","تشغيل شفز","يُسحب من Gate ERP (قراءة فقط)؛ لا يوجد ملف للرفع.",["riders"],"erp.sync.run",
            null,null,null,"erp","erp",[],[],"أول تحميل يحتاج «حمّل»."),
    ];

    static readonly Dictionary<string,(string File,Func<IReadOnlyList<ExcelColumn>> Columns)> Templates=new(StringComparer.Ordinal)
    {
        ["companies"]=("companies-template-v1.xlsx",()=>CompanyColumns),
        ["operations-ninja"]=("ninja-operations-template-v1.xlsx",()=>Steps().Single(s=>s.Key=="ninja_operations").Columns),
        ["operations-keeta"]=("keeta-operations-template-v1.xlsx",()=>Steps().Single(s=>s.Key=="keeta_operations").Columns),
        ["operations-hungerstation"]=("hungerstation-operations-template-v1.xlsx",()=>Steps().Single(s=>s.Key=="hungerstation_operations").Columns),
    };

    public static JsonArray View()
    {
        var result=new JsonArray();
        foreach(var s in Steps())
        {
            var columns=new JsonArray(s.Columns.Select(c=>(JsonNode)new JsonObject{["name"]=c.Header,["required"]=s.Required.Contains(c.Header),
                ["type"]=c.Numeric?"number":c.Choices is {Length:>0}?"choice":"text",["identity"]=c.Identity,["choices"]=c.Choices is {Length:>0 and <=40}?new JsonArray(c.Choices.Select(x=>(JsonNode)x).ToArray()):null}).ToArray());
            result.Add(new JsonObject{["order"]=s.Order,["key"]=s.Key,["group"]=s.Group,["labelAr"]=s.LabelAr,["purposeAr"]=s.PurposeAr,["dependsOn"]=new JsonArray(s.DependsOn.Select(x=>(JsonNode)x).ToArray()),
                ["permission"]=s.Permission,["templateUrl"]=s.TemplateUrl,["previewUrl"]=s.PreviewUrl,["commitUrl"]=s.CommitUrl,["upload"]=s.Upload,["status"]=s.Status,["noteAr"]=s.NoteAr,["columns"]=columns});
        }
        return result;
    }

    public static IResult Template(string name)
    {
        if(!Templates.TryGetValue(name,out var t))throw new ApiError("لا يوجد نموذج بهذا الاسم.",404);
        // Row 2 is a marked example so the shape is obvious; the importers reject it if left in.
        var columns=t.Columns();var example=columns.Select(c=>c.Numeric?(object?)0m:c==columns[0]?"مثال — احذف هذا الصف":"").ToArray();
        return Results.File(ExcelFiles.Write(columns,[example]),ExcelFiles.Mime,t.File);
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/imports/catalog",()=>View()).WithTags("Administration").WithSummary("Ordered import steps with template, preview and commit links and the required columns");
        api.MapGet("/imports/templates/{name}",(string name)=>Template(name)).WithTags("Administration").WithSummary("Download an empty import template from the catalog");
    }
}
