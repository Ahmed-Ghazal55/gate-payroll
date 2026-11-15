using System.Data.Common;
using ErpImport;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
namespace Payroll;

/// <summary>ERR: every live ERP failure → 503 with Arabic message and kind; log SQL number only (never SQL text with values).</summary>
public static class ErpLiveErrors
{
    public static ApiError Fail(string kind,Exception ex,ILogger? log=null)
    {
        if(ex is SqlException sql)
            log?.LogWarning("ERP live read failed kind={Kind} sqlNumber={Number}",kind,sql.Number);
        else if(ex is ImportFailure fail)
            log?.LogWarning("ERP live read failed kind={Kind} code={Code}",kind,fail.Code);
        else
            log?.LogWarning(ex,"ERP live read failed kind={Kind}",kind);
        return new ApiError("تعذر قراءة مصدر التشغيل ("+kind+").",503);
    }

    public static T Run<T>(string kind,Func<T> work,ILogger? log=null)
    {
        try{return work();}
        catch(Exception ex) when(ex is SqlException or ImportFailure or DbException)
        {throw Fail(kind,ex,log);}
    }
}
