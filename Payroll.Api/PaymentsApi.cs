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
        string permission=path.EndsWith("/approve",StringComparison.Ordinal)?"approvals.decide":"payments.disburse";
        if(path.Contains("/documents/",StringComparison.Ordinal))return AccessPolicy.Has(user,permission)||AccessPolicy.Has(user,"payments.disburse");
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
    }
}
