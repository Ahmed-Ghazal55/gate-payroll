namespace Payroll;

// WorkerCount=1 handles the server queue; this gate also protects direct calls in this process.
// Hangfire's SQL distributed filter serializes executions across API instances.
public sealed class JobRunGate
{
    readonly SemaphoreSlim gate=new(1,1);
    public Task<bool> TryEnter(CancellationToken token)=>gate.WaitAsync(0,token);
    public void Exit()=>gate.Release();
}
