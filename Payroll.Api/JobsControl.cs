using System.Data.Common;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ErpImport;
namespace Payroll;

public static class JobsControl
{
    public const string Kind="job_control",Id="job-control:erp-sync";
    public static bool Paused(DbConnection c,DbTransaction? tx=null)=>Store.Get(c,Id,Kind,tx)?.B("paused")==true;
    public static void EnsureMayCommit(DbConnection c,DbTransaction tx,IConfiguration config)
    {
        if(!config.GetValue<bool>("Jobs:Enabled")||!config.GetValue<bool>("Jobs:ErpSync:Enabled")||Paused(c,tx))throw new ImportFailure("erp_sync_paused_or_disabled");
    }
    public static void Change(Store store,ClaimsPrincipal actor,JsonObject input,bool paused,JobsOptions options,IJobsSchedule schedule)
    {
        if(!actor.IsInRole("SuperAdmin"))throw new ApiError("التحكم في الجدولة للمسؤول الأعلى فقط.",403);
        string reason=input.Required("reason");J.Need(reason.Length is >=3 and <=500&&!reason.Any(char.IsControl),"اكتب سببًا واضحًا، بحد أقصى 500 حرف.");
        J.Need(options.Enabled,"خدمة المهام غير مفعلة.",409);
        if(!paused)J.Need(options.ErpSyncEnabled,"فعّل إعداد مزامنة ERP بعد أول تحميل معتمد.",409);
        store.Write((c,tx)=>
        {
            Store.Save(c,Kind,new(){["id"]=Id,["paused"]=paused,["reason"]=reason,["changedBy"]=actor.Identity!.Name,["changedAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
            Store.Audit(c,tx,paused?"jobs.erp_sync_paused":"jobs.erp_sync_resumed",Id,actor.Identity!.Name!);return true;
        },false);
        // A paused flag is checked again inside the business-write transaction. Even an
        // already queued job cannot write after pause, if schedule removal is interrupted.
        schedule.Reconcile(paused);
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/system/jobs",(HttpContext ctx,JobsOptions options,IJobsSchedule schedule)=>
        {AccessPolicy.Demand(ctx.User,"users.manage");if(!AccessPolicy.Administrator(ctx.User))throw new ApiError("للمسؤول فقط.",403);return schedule.List();}).WithTags("Administration").WithSummary("Read safe background job status");
        api.MapPost("/system/jobs/erp-sync/dry-run",(HttpContext ctx,JobsOptions options,IJobsSchedule schedule)=>
        {if(!AccessPolicy.Administrator(ctx.User))throw new ApiError("للمسؤول فقط.",403);J.Need(options.Enabled,"خدمة المهام غير مفعلة.",409);return new{queued=true,jobId=schedule.EnqueueDryRun()};}).WithTags("Administration").WithSummary("Queue counts-only ERP preview");
        api.MapPost("/system/jobs/erp-sync/pause",(HttpContext ctx,JsonObject input,JobsOptions options,IJobsSchedule schedule,Store store)=>
        {Change(store,ctx.User,input,true,options,schedule);return new{paused=true};}).WithTags("Administration").WithSummary("Pause ERP scheduling with SuperAdmin reason");
        api.MapPost("/system/jobs/erp-sync/resume",(HttpContext ctx,JsonObject input,JobsOptions options,IJobsSchedule schedule,Store store)=>
        {Change(store,ctx.User,input,false,options,schedule);return new{paused=false};}).WithTags("Administration").WithSummary("Resume approved ERP scheduling with SuperAdmin reason");
    }
}
