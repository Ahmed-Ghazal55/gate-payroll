using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;

namespace Payroll;

public static class FormDeletion
{
    static void Admin(HttpContext ctx)
    {
        if(ctx.User.FindFirstValue(ClaimTypes.Role)!="Admin")throw new ApiError("حذف النموذج لمسؤول النظام فقط.",403);
    }
    static (JsonObject Form,List<JsonObject> Rows,string Fingerprint) Snapshot(DbConnection c,DbTransaction? tx,string id,bool custom)
    {
        var form=Store.Get(c,id,custom?"custom_form":"campaign",tx)??throw new ApiError("النموذج غير موجود.",404);
        var rows=Store.List(c,custom?"custom_response":"submission",tx).Where(r=>r.S(custom?"formId":"campaignId")==id).OrderBy(r=>r.S("id")).ToList();
        string snapshot=form.ToJsonString()+"\n"+string.Join("\n",rows.Select(r=>r.ToJsonString()));
        return(form,rows,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))));
    }
    static void MapKind(RouteGroupBuilder api,Store store,string path,bool custom)
    {
        api.MapGet(path+"/{id}/delete-preview",(string id,HttpContext ctx)=>
        {
            Admin(ctx);return store.Read(c=>{var s=Snapshot(c,null,id,custom);return new{id,title=s.Form.S(custom?"titleAr":"title"),responseCount=s.Rows.Count,fingerprint=s.Fingerprint};});
        });
        api.MapDelete(path+"/{id}",(string id,[FromBody] JsonObject input,HttpContext ctx)=>
        {
            Admin(ctx);J.Need(input.B("confirm"),"أكد حذف النموذج وردوده نهائيًا.");string fingerprint=input.Required("fingerprint");
            return store.Write((c,tx)=>
            {
                var s=Snapshot(c,tx,id,custom);
                if(s.Fingerprint!=fingerprint)throw new ApiError("تغير النموذج أو ردوده؛ أعد معاينة الحذف وراجع العدد الحالي.",409);
                string actor=ctx.User.Identity!.Name!;
                if(!custom)CampaignOperations.DeleteSubmissions(c,tx,s.Rows,actor);
                else foreach(var row in s.Rows)
                {
                    using var remove=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind='custom_response'",("@id",row.S("id")));remove.ExecuteNonQuery();
                    Store.Audit(c,tx,"builder.response_deleted",row.S("id"),actor);
                }
                using var cmd=Store.Command(c,tx,"DELETE FROM records WHERE id=@id AND kind=@kind",("@id",id),("@kind",custom?"custom_form":"campaign"));cmd.ExecuteNonQuery();
                Store.Audit(c,tx,custom?"builder.deleted":"campaign.deleted",id,actor);
                return new{ok=true,deletedResponses=s.Rows.Count,message="تم حذف النموذج وردوده نهائيًا."};
            });
        });
    }
    public static void Map(RouteGroupBuilder api,Store store)
    {
        MapKind(api,store,"/campaigns",false);
        MapKind(api,store,"/forms/builder",true);
    }
}
