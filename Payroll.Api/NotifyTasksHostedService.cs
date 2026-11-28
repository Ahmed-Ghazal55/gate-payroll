using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
namespace Payroll;

/// <summary>Hosted timer (no Hangfire) for overdue task escalation and unanswered notification reminders.</summary>
public sealed class NotifyTasksHostedService(Store store,IConfiguration config,ILogger<NotifyTasksHostedService> log):BackgroundService
{
    static readonly TimeSpan Interval=TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(string.Equals(config["Jobs:Enabled"],"false",StringComparison.OrdinalIgnoreCase))return;
        try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
        catch(OperationCanceledException){return;}
        while(!stoppingToken.IsCancellationRequested)
        {
            try{store.Write((c,tx)=>{NotifyTasksApi.ProcessReminders(c,tx);return true;},invalidateSummaries:false);}
            catch(Exception ex){log.LogWarning(ex,"notify/tasks reminder tick failed");}
            try{await Task.Delay(Interval,stoppingToken);}
            catch(OperationCanceledException){break;}
        }
    }
}
