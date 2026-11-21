using System.Data.Common;
using System.Text.Json.Nodes;
namespace Payroll;

public static class PaymentsApi
{
    static T Write<T>(Store store,Func<DbConnection,DbTransaction,T> action)
    {try{return store.Write(action);}catch(DbException){throw new ApiError("تعذر حفظ العملية؛ راجع تكرار المرجع والرصيد والحالة ثم أعد المحاولة.",409);}}
    static JsonObject Document(Store store,HttpContext ctx,JsonObject input,string? type=null)
    {
        var d=input.DeepClone().AsObject();if(type!=null)d["type"]=type;
        J.Need(d.S("type") is "quick" or "expense" or "purchase" or "government","استخدم مسار المصدر المخصص لهذا النوع.");
        return Write(store,(c,tx)=>PaymentsEngine.Create(c,tx,ctx.User,d));
    }
    public static bool Allowed(System.Security.Claims.ClaimsPrincipal user,string method,string path)
    {
        if(method=="GET")return AccessPolicy.Has(user,"payments.view");
        if(method is not ("POST" or "PUT"))return false;
        string permission=path.EndsWith("/approve",StringComparison.Ordinal)?"approvals.decide":path=="/api/payments/hr-fees"?"payments.hr_request.create":"payments.disburse";
        if(path.Contains("/documents/",StringComparison.Ordinal))return AccessPolicy.Has(user,permission)||AccessPolicy.Has(user,"payments.hr_request.create")||AccessPolicy.Has(user,"payments.disburse");
        if(path.Contains("/vouchers/",StringComparison.Ordinal))return AccessPolicy.Has(user,"payments.disburse");
        return AccessPolicy.Has(user,permission);
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/payments/documents",(string? type,string? companyId,HttpContext ctx)=>store.Read(c=>PaymentsEngine.List(c,ctx.User,type,companyId))).WithTags("Banks and treasury").WithSummary("List scoped payment documents");
        api.MapPost("/payments/documents",(JsonObject input,HttpContext ctx)=>Document(store,ctx,input)).WithTags("Banks and treasury").WithSummary("Create a draft expense or purchase document");
        api.MapPut("/payments/documents/{id}",(string id,JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Edit(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Edit a draft with a stale-content guard while retaining previous lines");
        api.MapGet("/payments/documents/{id}",(string id,HttpContext ctx)=>store.Read(c=>PaymentsEngine.Get(c,null,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Read a payment document and allowed actions");
        api.MapPost("/payments/documents/{id}/submit",(string id,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Transition(c,tx,ctx.User,id,"submit",new()))).WithTags("Banks and treasury").WithSummary("Submit a payment document");
        api.MapPost("/payments/documents/{id}/approve",(string id,JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Transition(c,tx,ctx.User,id,"approve",input))).WithTags("Banks and treasury").WithSummary("Approve a submitted payment document");
        api.MapPost("/payments/documents/{id}/cancel",(string id,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Transition(c,tx,ctx.User,id,"cancel",new()))).WithTags("Banks and treasury").WithSummary("Cancel and retain a draft document");
        api.MapPost("/payments/documents/{id}/payment-requests",(string id,JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Reserve(c,tx,ctx.User,id,input))).WithTags("Banks and treasury").WithSummary("Atomically reserve a partial payment and open dual approval");
        api.MapGet("/payments/vouchers/{id}",(string id,HttpContext ctx)=>store.Read(c=>PaymentsEngine.Payment(c,null,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Read a scoped payment voucher");
        api.MapPost("/payments/vouchers/{id}/pay",(string id,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.Pay(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Pay a voucher after recorded independent approval");
        api.MapPost("/payments/vouchers/{id}/cancel",(string id,HttpContext ctx)=>Write(store,(c,tx)=>PaymentsEngine.CancelPayment(c,tx,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Cancel an unpaid voucher and release its claim");
        api.MapPost("/payments/quick",(JsonObject input,HttpContext ctx)=>Document(store,ctx,input,"quick")).WithTags("Banks and treasury").WithSummary("Create a quick payment through the document engine");
        api.MapGet("/payments/suppliers",(HttpContext ctx)=>store.Read(c=>PaymentDocuments.Suppliers(c,ctx.User))).WithTags("Banks and treasury").WithSummary("List scoped payment suppliers");
        api.MapPost("/payments/suppliers",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentDocuments.Supplier(c,tx,ctx.User,input))).WithTags("Banks and treasury").WithSummary("Create a supplier");
        api.MapGet("/payments/suppliers/{id}/ledger",(string id,HttpContext ctx)=>store.Read(c=>PaymentDocuments.SupplierLedger(c,ctx.User,id))).WithTags("Banks and treasury").WithSummary("Read supplier invoices, payments and outstanding balance");
        api.MapGet("/payments/hr-fees",(HttpContext ctx)=>store.Read(c=>PaymentsEngine.List(c,ctx.User,"hr_fee",null))).WithTags("Banks and treasury").WithSummary("List scoped HR fee requests");
        api.MapPost("/payments/hr-fees",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentDocuments.BearerDocument(c,tx,ctx.User,"hr_fee",input))).WithTags("Banks and treasury").WithSummary("Create an HR fee request with an exact bearer split");
        api.MapGet("/payments/traffic-violations",(HttpContext ctx)=>store.Read(c=>PaymentsEngine.List(c,ctx.User,"traffic_violation",null))).WithTags("Banks and treasury").WithSummary("List traffic violation payment documents");
        api.MapPost("/payments/traffic-violations",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentDocuments.BearerDocument(c,tx,ctx.User,"traffic_violation",input))).WithTags("Banks and treasury").WithSummary("Create a traffic violation payment with rider and company shares");
        api.MapGet("/payments/government",(HttpContext ctx)=>store.Read(c=>PaymentsEngine.List(c,ctx.User,"government",null))).WithTags("Banks and treasury").WithSummary("List government payment documents");
        api.MapPost("/payments/government",(JsonObject input,HttpContext ctx)=>Document(store,ctx,input,"government")).WithTags("Banks and treasury").WithSummary("Create a government payment document");
        api.MapGet("/payments/properties",(HttpContext ctx)=>store.Read(c=>PaymentContracts.List(c,ctx.User,"properties"))).WithTags("Banks and treasury").WithSummary("List payment properties");
        api.MapPost("/payments/properties",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.Create(c,tx,ctx.User,"properties",input))).WithTags("Banks and treasury").WithSummary("Create an office or housing property");
        api.MapGet("/payments/financiers",(HttpContext ctx)=>store.Read(c=>PaymentContracts.List(c,ctx.User,"financiers"))).WithTags("Banks and treasury").WithSummary("List financiers");
        api.MapPost("/payments/financiers",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.Create(c,tx,ctx.User,"financiers",input))).WithTags("Banks and treasury").WithSummary("Create a financier");
        api.MapGet("/payments/leases",(HttpContext ctx)=>store.Read(c=>PaymentContracts.List(c,ctx.User,"leases"))).WithTags("Banks and treasury").WithSummary("List lease contracts");
        api.MapPost("/payments/leases",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.Create(c,tx,ctx.User,"leases",input))).WithTags("Banks and treasury").WithSummary("Create a lease and its due payment documents");
        api.MapGet("/payments/leases/{id}/schedule",(string id,HttpContext ctx)=>store.Read(c=>PaymentContracts.Schedule(c,ctx.User,"leases",id))).WithTags("Banks and treasury").WithSummary("Read lease due schedule");
        api.MapPost("/payments/leases/{id}/recognize",(string id,JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.RecognizeRent(c,tx,ctx.User,id,input.Required("period")))).WithTags("Banks and treasury").WithSummary("Recognize a funded monthly rent expense exactly once");
        api.MapGet("/payments/finance-contracts",(HttpContext ctx)=>store.Read(c=>PaymentContracts.List(c,ctx.User,"finance-contracts"))).WithTags("Banks and treasury").WithSummary("List vehicle financing contracts");
        api.MapPost("/payments/finance-contracts",(JsonObject input,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.Create(c,tx,ctx.User,"finance-contracts",input))).WithTags("Banks and treasury").WithSummary("Create an effective-interest financing schedule");
        api.MapGet("/payments/finance-contracts/{id}/schedule",(string id,HttpContext ctx)=>store.Read(c=>PaymentContracts.Schedule(c,ctx.User,"finance-contracts",id))).WithTags("Banks and treasury").WithSummary("Read financing schedule principal and interest");
        api.MapPost("/payments/contracts/{kind}/{id}/archive",(string kind,string id,HttpContext ctx)=>Write(store,(c,tx)=>PaymentContracts.Archive(c,tx,ctx.User,kind,id))).WithTags("Banks and treasury").WithSummary("Archive a payment master without deletion");
        api.MapGet("/payments/due-calendar",(string? companyId,string? from,string? to,HttpContext ctx)=>store.Read(c=>PaymentContracts.DueCalendar(c,ctx.User,companyId,from??DateTime.UtcNow.ToString("yyyy-MM-01"),to??DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-01")))).WithTags("Banks and treasury").WithSummary("Read due amounts after paid and reserved claims");
    }
}
