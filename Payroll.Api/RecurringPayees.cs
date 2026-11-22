using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-07c / M-0727: مدفوعات دورية — optional recurring payees under Payments (never «رواتب»).</summary>
public static class RecurringPayees
{
    public const string ExpenseAccountKey="recurring_payees.expense_account";
    public const string FeeAccountKey="recurring_payees.fee_account";
    public const string FxAccountKey="recurring_payees.fx_difference_account";
    static readonly HashSet<string> PayeeTypes=new(StringComparer.Ordinal){"trial_outside_sponsorship","egypt_office","other"};
    static readonly HashSet<string> Methods=new(StringComparer.Ordinal){"bank_transfer","cash","other"};

    static string Q(DbConnection c,string table)=>SchemaSql.Q(c,"pay",table);
    static string Me(ClaimsPrincipal actor)=>PaymentsEngine.Me(actor);
    static string SettingCode(DbConnection c,DbTransaction? tx,string key)
    {
        object? raw=SchemaSql.Scalar(c,tx,SchemaSql.IsSql(c)
            ?$"SELECT value_json FROM {SchemaSql.Q(c,"ref","setting")} WHERE [key]=@k"
            :$"SELECT value_json FROM {SchemaSql.Q(c,"ref","setting")} WHERE key=@k",("@k",key));
        if(raw==null)return "";
        try
        {
            var node=JsonNode.Parse(raw.ToString()!);
            if(node is JsonObject o)return o.S("accountCode",o.S("code"));
            return node?.ToString()?.Trim().Trim('"')??"";
        }
        catch{return "";}
    }
    static void DemandExpense(DbConnection c,DbTransaction? tx)
    {
        J.Need(SettingCode(c,tx,ExpenseAccountKey)!="","حدد حساب المصروف في الإعدادات أولاً",409);
    }
    static decimal SarOf(decimal amount,decimal rate)=>J.Money(amount*rate);
    static string PeriodDay(string period)=>J.Period(period)+"-01";
    static JsonObject LineView(JsonObject row)
    {
        // Never surface bank details in list payloads that might be logged; return only a presence flag.
        var view=row.DeepClone().AsObject();
        bool has=view.S("bankDetails")!="";
        view.Remove("bankDetails");
        view["hasBankDetails"]=has;
        return view;
    }
    static JsonObject RunView(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        var run=PaymentsEngine.Rows(c,tx,$"""
            SELECT id,company_id AS companyId,period_month AS periodMonth,status,content_hash AS contentHash,
            submitted_by AS submittedBy,approved_by AS approvedBy,created_by AS createdBy
            FROM {Q(c,"recurring_run")} WHERE id=@id
            """,("@id",PaymentsEngine.Id(id))).FirstOrDefault()??throw new ApiError("التشغيل غير موجود.",404);
        J.Need(FinancialScopes.Sponsor(actor,run.S("companyId")),"التشغيل غير موجود.",404);
        var lines=PaymentsEngine.Rows(c,tx,$"""
            SELECT id,run_id AS runId,payee_id AS payeeId,name_ar AS nameAr,amount,currency_code AS currencyCode,
            exchange_rate AS exchangeRate,transfer_fee AS transferFee,fx_difference AS fxDifference,sar_amount AS sarAmount,
            payment_method AS paymentMethod,bank_details AS bankDetails,cost_centre_id AS costCentreId,
            skipped,notes,document_id AS documentId,payment_id AS paymentId
            FROM {Q(c,"recurring_run_line")} WHERE run_id=@id ORDER BY name_ar,id
            """,("@id",run.S("id")));
        foreach(var l in lines)l["skipped"]=l.N("skipped")==1||l.B("skipped");
        run["lines"]=PaymentsEngine.Array(lines.Select(LineView));
        var actions=new JsonArray();
        int status=(int)run.N("status");
        if(status==1&&AccessPolicy.Has(actor,"payments.disburse")){actions.Add("edit_lines");actions.Add("submit");}
        if(status==2&&AccessPolicy.Has(actor,"approvals.decide"))actions.Add("approve");
        if(status==3&&AccessPolicy.Has(actor,"payments.disburse"))actions.Add("execute");
        run["allowedActions"]=actions;
        return run;
    }

    public static JsonObject CreatePayee(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        string company=PaymentsEngine.Id(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,company);
        string type=input.Required("payeeType");J.Need(PayeeTypes.Contains(type),"نوع المستفيد غير صالح.");
        string method=input.Required("paymentMethod");J.Need(Methods.Contains(method),"طريقة الدفع غير صالحة.");
        string currency=input.Required("currencyCode").Trim().ToUpperInvariant();J.Need(currency.Length==3,"العملة غير صالحة.");
        decimal amount=PaymentsEngine.Money(input,"amount");
        string from=PaymentsEngine.Day(input.Required("activeFrom"));
        string? to=string.IsNullOrWhiteSpace(input.S("activeTo"))?null:PaymentsEngine.Day(input.S("activeTo"));
        if(to!=null)J.Need(string.CompareOrdinal(to,from)>=0,"تاريخ الانتهاء قبل البداية.");
        string centre=PaymentsEngine.Id(input.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D")));
        string bank=input.S("bankDetails");J.Need(bank.Length<=500,"تفاصيل التحويل أطول من الحد.");
        string id=PaymentsEngine.MoneyId();
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {Q(c,"recurring_payee")}(id,company_id,name_ar,payee_type,amount,currency_code,payment_method,bank_details,cost_centre_id,erp_person_id,active_from,active_to,notes,created_by)
            VALUES(@id,@c,@name,@type,@amount,@cur,@method,@bank,@cc,@person,@from,@to,@notes,@by)
            """,("@id",id),("@c",company),("@name",PaymentsEngine.Text(input,"nameAr",200)),("@type",type),("@amount",amount),
            ("@cur",currency),("@method",method),("@bank",bank),("@cc",centre),("@person",PaymentsEngine.NullId(input.S("erpPersonId"))),
            ("@from",from),("@to",(object?)to??DBNull.Value),("@notes",input.S("notes")),("@by",Me(actor)));
        Store.Audit(c,tx,"recurring_payee.created",id,Me(actor));
        return GetPayee(c,tx,actor,id);
    }

    public static JsonObject UpdatePayee(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        var existing=GetPayee(c,tx,actor,id);
        string type=input.S("payeeType",existing.S("payeeType"));J.Need(PayeeTypes.Contains(type),"نوع المستفيد غير صالح.");
        string method=input.S("paymentMethod",existing.S("paymentMethod"));J.Need(Methods.Contains(method),"طريقة الدفع غير صالحة.");
        string currency=input.S("currencyCode",existing.S("currencyCode")).Trim().ToUpperInvariant();J.Need(currency.Length==3,"العملة غير صالحة.");
        decimal amount=input.ContainsKey("amount")?PaymentsEngine.Money(input,"amount"):existing.N("amount");
        string from=PaymentsEngine.Day(input.S("activeFrom",existing.S("activeFrom")));
        string toRaw=input.ContainsKey("activeTo")?input.S("activeTo"):existing.S("activeTo");
        string? to=string.IsNullOrWhiteSpace(toRaw)?null:PaymentsEngine.Day(toRaw);
        string bank;
        if(input.B("clearBankDetails"))bank="";
        else if(input.ContainsKey("bankDetails")&&input["bankDetails"] is not null)
            bank=input.S("bankDetails");
        else
            bank=SchemaSql.Scalar(c,tx,$"SELECT bank_details FROM {Q(c,"recurring_payee")} WHERE id=@id",("@id",PaymentsEngine.Id(id)))?.ToString()??"";
        J.Need(bank.Length<=500,"تفاصيل التحويل أطول من الحد.");
        int status=input.ContainsKey("status")?(int)input.N("status"):(int)existing.N("status");J.Need(status is 1 or 2,"الحالة غير صالحة.");
        SchemaSql.Exec(c,tx,$"""
            UPDATE {Q(c,"recurring_payee")} SET name_ar=@name,payee_type=@type,amount=@amount,currency_code=@cur,payment_method=@method,
            bank_details=@bank,cost_centre_id=@cc,erp_person_id=@person,active_from=@from,active_to=@to,notes=@notes,status=@status,
            updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id
            """,("@name",PaymentsEngine.Text(input,"nameAr",200)),("@type",type),("@amount",amount),("@cur",currency),("@method",method),
            ("@bank",bank),("@cc",PaymentsEngine.Id(input.S("costCentreId",existing.S("costCentreId")))),
            ("@person",PaymentsEngine.NullId(input.S("erpPersonId",existing.S("erpPersonId")))),
            ("@from",from),("@to",(object?)to??DBNull.Value),("@notes",input.S("notes",existing.S("notes"))),("@status",status),
            ("@by",Me(actor)),("@id",PaymentsEngine.Id(id)));
        Store.Audit(c,tx,"recurring_payee.updated",PaymentsEngine.Id(id),Me(actor));
        return GetPayee(c,tx,actor,id);
    }

    public static JsonObject GetPayee(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"payments.view");
        var row=PaymentsEngine.Rows(c,tx,$"""
            SELECT id,company_id AS companyId,name_ar AS nameAr,payee_type AS payeeType,amount,currency_code AS currencyCode,
            payment_method AS paymentMethod,bank_details AS bankDetails,cost_centre_id AS costCentreId,erp_person_id AS erpPersonId,
            active_from AS activeFrom,active_to AS activeTo,notes,status
            FROM {Q(c,"recurring_payee")} WHERE id=@id
            """,("@id",PaymentsEngine.Id(id))).FirstOrDefault()??throw new ApiError("المستفيد غير موجود.",404);
        J.Need(FinancialScopes.Sponsor(actor,row.S("companyId")),"المستفيد غير موجود.",404);
        var view=LineView(row);
        view["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("edit"):new JsonArray();
        return view;
    }

    public static JsonObject ListPayees(DbConnection c,ClaimsPrincipal actor,string? companyId=null)
    {
        AccessPolicy.Demand(actor,"payments.view");
        var rows=PaymentsEngine.Rows(c,null,$"""
            SELECT id,company_id AS companyId,name_ar AS nameAr,payee_type AS payeeType,amount,currency_code AS currencyCode,
            payment_method AS paymentMethod,bank_details AS bankDetails,cost_centre_id AS costCentreId,erp_person_id AS erpPersonId,
            active_from AS activeFrom,active_to AS activeTo,notes,status
            FROM {Q(c,"recurring_payee")} ORDER BY name_ar
            """).Where(r=>FinancialScopes.Sponsor(actor,r.S("companyId"))&&(companyId==null||r.S("companyId")==PaymentsEngine.Id(companyId))).Select(LineView);
        return new(){["items"]=PaymentsEngine.Array(rows),["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("create","propose_run","one_off"):new JsonArray()};
    }

    public static JsonObject PayeeHistory(DbConnection c,ClaimsPrincipal actor,string payeeId)
    {
        AccessPolicy.Demand(actor,"payments.view");
        var payee=GetPayee(c,null,actor,payeeId);
        var items=PaymentsEngine.Rows(c,null,$"""
            SELECT l.id,l.run_id AS runId,r.period_month AS periodMonth,l.amount,l.currency_code AS currencyCode,l.sar_amount AS sarAmount,
            l.skipped,l.payment_id AS paymentId,l.document_id AS documentId,r.status AS runStatus
            FROM {Q(c,"recurring_run_line")} l
            JOIN {Q(c,"recurring_run")} r ON r.id=l.run_id
            WHERE l.payee_id=@p ORDER BY r.period_month DESC,l.id
            """,("@p",PaymentsEngine.Id(payeeId)));
        return new(){["payeeId"]=payee.S("id"),["items"]=PaymentsEngine.Array(items),["allowedActions"]=new JsonArray()};
    }

    public static JsonObject ProposeRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        string company=PaymentsEngine.Id(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,company);
        string period=J.Period(input.Required("period"));string month=PeriodDay(period);
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"recurring_run")} WHERE company_id=@c AND period_month=@m",("@c",company),("@m",month));
        if(existing!=null)return RunView(c,tx,actor,existing.ToString()!);
        string id=PaymentsEngine.MoneyId();
        var payees=PaymentsEngine.Rows(c,tx,$"""
            SELECT id,name_ar AS nameAr,amount,currency_code AS currencyCode,payment_method AS paymentMethod,bank_details AS bankDetails,
            cost_centre_id AS costCentreId,notes
            FROM {Q(c,"recurring_payee")}
            WHERE company_id=@c AND status=1 AND active_from<=@m AND (active_to IS NULL OR active_to>=@m)
            ORDER BY name_ar
            """,("@c",company),("@m",month));
        string hash=PaymentsEngine.Hash(new JsonObject{["companyId"]=company,["period"]=period,["count"]=payees.Count});
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {Q(c,"recurring_run")}(id,company_id,period_month,status,content_hash,created_by)
            VALUES(@id,@c,@m,1,@hash,@by)
            """,("@id",id),("@c",company),("@m",month),("@hash",hash),("@by",Me(actor)));
        foreach(var p in payees)
        {
            decimal amount=p.N("amount");string currency=p.S("currencyCode");
            decimal rate=currency=="SAR"?1m:0m;
            decimal sar=currency=="SAR"?amount:0m;
            InsertLine(c,tx,id,p.S("id"),p.S("nameAr"),amount,currency,rate,0,0,sar,p.S("paymentMethod"),p.S("bankDetails"),p.S("costCentreId"),false,p.S("notes"),Me(actor));
        }
        Store.Audit(c,tx,"recurring_run.proposed",id,Me(actor));
        return RunView(c,tx,actor,id);
    }

    static void InsertLine(DbConnection c,DbTransaction tx,string runId,string? payeeId,string name,decimal amount,string currency,decimal rate,decimal fee,decimal fx,decimal sar,string method,string bank,string centre,bool skipped,string notes,string by)
    {
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {Q(c,"recurring_run_line")}(id,run_id,payee_id,name_ar,amount,currency_code,exchange_rate,transfer_fee,fx_difference,sar_amount,payment_method,bank_details,cost_centre_id,skipped,notes,created_by)
            VALUES(@id,@run,@payee,@name,@amount,@cur,@rate,@fee,@fx,@sar,@method,@bank,@cc,@skip,@notes,@by)
            """,("@id",PaymentsEngine.MoneyId()),("@run",runId),("@payee",(object?)(string.IsNullOrWhiteSpace(payeeId)?null:PaymentsEngine.Id(payeeId))??DBNull.Value),
            ("@name",name),("@amount",amount),("@cur",currency),("@rate",rate),("@fee",fee),("@fx",fx),("@sar",sar),
            ("@method",method),("@bank",bank),("@cc",centre),("@skip",skipped?1:0),("@notes",notes),("@by",by));
    }

    public static JsonObject UpdateRunLines(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string runId,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        var run=RunView(c,tx,actor,runId);
        J.Need((int)run.N("status")==1,"التعديل مسموح في المسودة فقط.",409);
        var lines=input["lines"] as JsonArray??throw new ApiError("البنود مطلوبة.");
        foreach(var node in lines.OfType<JsonObject>())
        {
            if(node.S("id")!="")
            {
                string lineId=PaymentsEngine.Id(node.S("id"));
                object? owned=SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"recurring_run_line")} WHERE id=@id AND run_id=@run",("@id",lineId),("@run",PaymentsEngine.Id(runId)));
                J.Need(owned!=null,"البند غير موجود.",404);
                bool skipped=node.B("skipped")||node.S("skipped")=="1";
                decimal amount=node.ContainsKey("amount")?PaymentsEngine.Money(node,"amount"):Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT amount FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId)));
                string currency=(node.S("currencyCode")==""?SchemaSql.Scalar(c,tx,$"SELECT currency_code FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId))!.ToString()!:node.S("currencyCode")).ToUpperInvariant();
                decimal rate=node.ContainsKey("exchangeRate")?node.N("exchangeRate"):Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT exchange_rate FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId)));
                if(!skipped)J.Need(rate>0,"سعر الصرف غير صالح.");
                else if(rate<0)throw new ApiError("سعر الصرف غير صالح.");
                rate=decimal.Round(rate,6,MidpointRounding.AwayFromZero);
                decimal fee=node.ContainsKey("transferFee")?node.N("transferFee"):Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT transfer_fee FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId)));
                decimal fx=node.ContainsKey("fxDifference")?node.N("fxDifference"):Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT fx_difference FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId)));
                J.Need(fee>=0&&fee==J.Money(fee),"رسوم التحويل غير صالحة.");
                J.Need(fx==J.Money(fx),"فرق العملة غير صالح.");
                decimal sar=skipped?0m:(rate>0?SarOf(amount,rate):0m);
                if(!skipped)J.Need(sar>0,"المبلغ بالريال يجب أن يكون موجباً.");
                string bank=node.ContainsKey("bankDetails")?node.S("bankDetails"):SchemaSql.Scalar(c,tx,$"SELECT bank_details FROM {Q(c,"recurring_run_line")} WHERE id=@id",("@id",lineId))?.ToString()??"";
                SchemaSql.Exec(c,tx,$"""
                    UPDATE {Q(c,"recurring_run_line")} SET amount=@amount,currency_code=@cur,exchange_rate=@rate,transfer_fee=@fee,fx_difference=@fx,
                    sar_amount=@sar,skipped=@skip,bank_details=@bank,notes=@notes,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id
                    """,("@amount",amount),("@cur",currency),("@rate",rate),("@fee",fee),("@fx",fx),("@sar",sar),("@skip",skipped?1:0),
                    ("@bank",bank),("@notes",node.S("notes")),("@by",Me(actor)),("@id",lineId));
            }
            else
            {
                // one-off line on the run
                string name=PaymentsEngine.Text(node,"nameAr",200);
                decimal amount=PaymentsEngine.Money(node,"amount");
                string currency=node.Required("currencyCode").Trim().ToUpperInvariant();
                decimal rate=currency=="SAR"?1m:node.N("exchangeRate");J.Need(rate>0,"سعر الصرف مطلوب.");
                rate=decimal.Round(rate,6,MidpointRounding.AwayFromZero);
                decimal fee=node.N("transferFee");decimal fx=node.N("fxDifference");
                J.Need(fee>=0&&fee==J.Money(fee),"رسوم التحويل غير صالحة.");
                J.Need(fx==J.Money(fx),"فرق العملة غير صالح.");
                string method=node.S("paymentMethod","other");J.Need(Methods.Contains(method),"طريقة الدفع غير صالحة.");
                InsertLine(c,tx,PaymentsEngine.Id(runId),null,name,amount,currency,rate,fee,fx,SarOf(amount,rate),method,node.S("bankDetails"),
                    PaymentsEngine.Id(node.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D"))),node.B("skipped"),node.S("notes"),Me(actor));
            }
        }
        string hash=PaymentsEngine.Hash(new JsonObject{["runId"]=runId,["at"]=DateTime.UtcNow.ToString("O")});
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run")} SET content_hash=@h,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@h",hash),("@by",Me(actor)),("@id",PaymentsEngine.Id(runId)));
        Store.Audit(c,tx,"recurring_run.lines_updated",PaymentsEngine.Id(runId),Me(actor));
        return RunView(c,tx,actor,runId);
    }

    public static JsonObject SubmitRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string runId)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        var run=RunView(c,tx,actor,runId);
        J.Need((int)run.N("status")==1,"التشغيل ليس مسودة.",409);
        var active=run["lines"]!.AsArray().OfType<JsonObject>().Where(l=>!l.B("skipped")&&l.S("skipped")!="1").ToList();
        J.Need(active.Count>0,"لا توجد بنود للدفع.",409);
        foreach(var l in active)
        {
            if(l.S("currencyCode")=="SAR")continue;
            J.Need(l.N("exchangeRate")>0&&l.N("sarAmount")>0,"حدد سعر الصرف للعملة الأجنبية قبل الإرسال.",409);
        }
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run")} SET status=2,submitted_by=@by,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@by",Me(actor)),("@id",PaymentsEngine.Id(runId)));
        Store.Audit(c,tx,"recurring_run.submitted",PaymentsEngine.Id(runId),Me(actor));
        return RunView(c,tx,actor,runId);
    }

    public static JsonObject ApproveRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string runId)
    {
        AccessPolicy.Demand(actor,"approvals.decide");
        var run=RunView(c,tx,actor,runId);
        J.Need((int)run.N("status")==2,"التشغيل غير مُرسل للاعتماد.",409);
        string maker=run.S("submittedBy");
        J.Need(!AccessPolicy.SamePerson(actor,maker),"لا تعتمد تشغيلاً أنشأته أنت.",403);
        SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run")} SET status=3,approved_by=@by,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@by",Me(actor)),("@id",PaymentsEngine.Id(runId)));
        Store.Audit(c,tx,"recurring_run.approved",PaymentsEngine.Id(runId),Me(actor));
        return RunView(c,tx,actor,runId);
    }

    public static JsonObject GetRun(DbConnection c,ClaimsPrincipal actor,string runId)=>RunView(c,null,actor,runId);

    public static JsonObject ListRuns(DbConnection c,ClaimsPrincipal actor,string? companyId=null)
    {
        AccessPolicy.Demand(actor,"payments.view");
        var rows=PaymentsEngine.Rows(c,null,$"""
            SELECT id,company_id AS companyId,period_month AS periodMonth,status,submitted_by AS submittedBy,approved_by AS approvedBy
            FROM {Q(c,"recurring_run")} ORDER BY period_month DESC
            """).Where(r=>FinancialScopes.Sponsor(actor,r.S("companyId"))&&(companyId==null||r.S("companyId")==PaymentsEngine.Id(companyId)));
        return new(){["items"]=PaymentsEngine.Array(rows),["allowedActions"]=AccessPolicy.Has(actor,"payments.disburse")?new JsonArray("propose"):new JsonArray()};
    }

    /// <summary>Build expense document lines for SAR amount + fee + signed FX (gain nets expense then reclass on pay).</summary>
    static (JsonArray lines,decimal total,JsonObject? fxGain) BuildExpenseLines(string expense,string feeCode,string fxCode,string centre,decimal sar,decimal fee,decimal fx)
    {
        J.Need(fee>=0&&fee==J.Money(fee),"رسوم التحويل غير صالحة.");
        J.Need(fx==J.Money(fx),"فرق العملة غير صالح.");
        if(fee>0)J.Need(feeCode!="","حدد حساب رسوم التحويل في الإعدادات أولاً",409);
        if(fx!=0)J.Need(fxCode!="","حدد حساب فرق العملة في الإعدادات أولاً",409);
        decimal total=sar+fee+fx;J.Need(total>0,"إجمالي الصرف يجب أن يكون موجباً.");
        JsonObject? fxGain=null;
        decimal expenseAmount=sar;
        if(fx<0)
        {
            expenseAmount=sar+fx;J.Need(expenseAmount>0,"فرق العملة السالب أكبر من المبلغ.",409);
            fxGain=new JsonObject{["expenseAccountCode"]=expense,["fxAccountCode"]=fxCode,["amount"]=-fx,["costCentreId"]=centre};
        }
        var docLines=new JsonArray(new JsonObject{["accountCode"]=expense,["amount"]=expenseAmount,["costCentreId"]=centre});
        if(fee>0)docLines.Add(new JsonObject{["accountCode"]=feeCode,["amount"]=fee,["costCentreId"]=centre});
        if(fx>0)docLines.Add(new JsonObject{["accountCode"]=fxCode,["amount"]=fx,["costCentreId"]=centre});
        return (docLines,total,fxGain);
    }

    static JsonObject ApproveDocumentAsActor(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc)
    {
        doc=PaymentsEngine.Get(c,tx,actor,doc.S("id"));
        if((int)doc.N("status")==1){PaymentsEngine.Transition(c,tx,actor,doc.S("id"),"submit",new());doc=PaymentsEngine.Get(c,tx,actor,doc.S("id"));}
        if((int)doc.N("status")==2)
        {
            // BR §35 / M-0672: expense document approve is permission-based; dual money-out is on the voucher.
            // Always use the authenticated actor — never fabricate another Admin principal.
            PaymentsEngine.Transition(c,tx,actor,doc.S("id"),"approve",new());
            doc=PaymentsEngine.Get(c,tx,actor,doc.S("id"));
        }
        return doc;
    }

    /// <summary>Create expense documents + reserve vouchers for each active line. Dual money-out still required before Pay.</summary>
    public static JsonObject ExecuteRun(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string runId,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        DemandExpense(c,tx);
        var run=RunView(c,tx,actor,runId);
        J.Need((int)run.N("status") is 3 or 4,"التشغيل غير معتمد.",409);
        string expense=SettingCode(c,tx,ExpenseAccountKey);
        string feeCode=SettingCode(c,tx,FeeAccountKey);
        string fxCode=SettingCode(c,tx,FxAccountKey);
        string paidOn=PaymentsEngine.Day(input.Required("paidOn"));
        string bank=input.Required("bankAccountCode");
        // Run month stays on the run; payment-document accounting month follows paidOn (cross-month pay).
        string period=paidOn[..7];
        var vouchers=new JsonArray();
        var documents=new JsonArray();
        var lines=PaymentsEngine.Rows(c,tx,$"""
            SELECT id,payee_id AS payeeId,name_ar AS nameAr,amount,currency_code AS currencyCode,exchange_rate AS exchangeRate,
            transfer_fee AS transferFee,fx_difference AS fxDifference,sar_amount AS sarAmount,payment_method AS paymentMethod,
            bank_details AS bankDetails,cost_centre_id AS costCentreId,skipped,document_id AS documentId,payment_id AS paymentId
            FROM {Q(c,"recurring_run_line")} WHERE run_id=@id
            """,("@id",PaymentsEngine.Id(runId)));
        foreach(var line in lines)
        {
            if(line.B("skipped")||line.S("skipped")=="1")continue;
            if(line.S("paymentId")!=""){vouchers.Add(PaymentsEngine.Payment(c,tx,actor,line.S("paymentId")));continue;}
            string currency=line.S("currencyCode");
            decimal rate=line.N("exchangeRate");
            if(currency!="SAR")J.Need(rate>0&&SarOf(line.N("amount"),rate)==line.N("sarAmount"),"سعر الصرف لا يطابق المبلغ بالريال.",409);
            else J.Need(line.N("sarAmount")==line.N("amount"),"مبلغ الريال غير متطابق.",409);
            decimal fee=line.N("transferFee");decimal fx=line.N("fxDifference");
            var built=BuildExpenseLines(expense,feeCode,fxCode,line.S("costCentreId"),line.N("sarAmount"),fee,fx);
            var docInput=new JsonObject
            {
                ["type"]="expense",["companyId"]=run.S("companyId"),["period"]=period,["occurredOn"]=paidOn,["dueOn"]=paidOn,
                ["idempotencyKey"]="rp:"+runId+":"+line.S("id"),["reference"]="RP-"+line.S("id").Replace("-","")[..Math.Min(30,line.S("id").Replace("-","").Length)],
                ["lines"]=built.lines,["recurringRunId"]=runId,["recurringLineId"]=line.S("id"),["payeeName"]=line.S("nameAr")
            };
            if(built.fxGain!=null)docInput["fxGainReclass"]=built.fxGain;
            JsonObject doc;
            if(line.S("documentId")!="")doc=PaymentsEngine.Get(c,tx,actor,line.S("documentId"));
            else
            {
                doc=PaymentsEngine.Create(c,tx,actor,docInput);
                SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run_line")} SET document_id=@d,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",
                    ("@d",doc.S("id")),("@by",Me(actor)),("@id",line.S("id")));
            }
            doc=ApproveDocumentAsActor(c,tx,actor,doc);
            if((int)doc.N("status")!=3){documents.Add(doc);continue;}
            var reserved=PaymentsEngine.Reserve(c,tx,actor,doc.S("id"),new()
            {
                ["idempotencyKey"]="rp-pay:"+line.S("id"),["amount"]=built.total,["paidOn"]=paidOn,["bankAccountCode"]=bank,["reference"]="RP-"+line.S("id")[..8]
            });
            SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run_line")} SET payment_id=@p,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",
                ("@p",reserved.S("id")),("@by",Me(actor)),("@id",line.S("id")));
            vouchers.Add(reserved);
        }
        if(vouchers.Count>0&&documents.Count==0)
            SchemaSql.Exec(c,tx,$"UPDATE {Q(c,"recurring_run")} SET status=4,updated_by=@by,updated_at={SchemaSql.NowLiteral(c)} WHERE id=@id",("@by",Me(actor)),("@id",PaymentsEngine.Id(runId)));
        Store.Audit(c,tx,"recurring_run.executed",PaymentsEngine.Id(runId),Me(actor));
        return new(){["id"]=runId,["vouchers"]=vouchers,["documents"]=documents,["allowedActions"]=new JsonArray("pay_vouchers")};
    }

    public static JsonObject CreateOneOff(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        AccessPolicy.Demand(actor,"payments.disburse");
        DemandExpense(c,tx);
        string company=PaymentsEngine.Id(input.Required("companyId"));FinancialScopes.DemandSponsor(actor,company);
        string expense=SettingCode(c,tx,ExpenseAccountKey);
        string feeCode=SettingCode(c,tx,FeeAccountKey);
        string fxCode=SettingCode(c,tx,FxAccountKey);
        string currency=input.Required("currencyCode").Trim().ToUpperInvariant();
        decimal amount=PaymentsEngine.Money(input,"amount");
        decimal rate=currency=="SAR"?1m:input.N("exchangeRate");J.Need(rate>0,"سعر الصرف مطلوب.");
        rate=decimal.Round(rate,6,MidpointRounding.AwayFromZero);
        decimal fee=input.N("transferFee");decimal fx=input.N("fxDifference");
        decimal sar=SarOf(amount,rate);
        string paidOn=PaymentsEngine.Day(input.Required("paidOn"));
        string period=J.Period(input.S("period",paidOn[..7]));
        string centre=PaymentsEngine.Id(input.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D")));
        var built=BuildExpenseLines(expense,feeCode,fxCode,centre,sar,fee,fx);
        string key=input.Required("idempotencyKey").Trim();J.Need(key.Length is >=8 and <=64,"مفتاح العملية مطلوب وطوله من 8 إلى 64.",400);
        var docBody=new JsonObject
        {
            ["type"]="expense",["companyId"]=company,["period"]=period,["occurredOn"]=paidOn,["dueOn"]=paidOn,
            ["idempotencyKey"]=key,["reference"]=input.S("reference","ONEOFF-"+key[..Math.Min(8,key.Length)]),["lines"]=built.lines,
            ["payeeName"]=PaymentsEngine.Text(input,"nameAr",200),["oneOff"]=true
        };
        if(built.fxGain!=null)docBody["fxGainReclass"]=built.fxGain;
        var doc=PaymentsEngine.Create(c,tx,actor,docBody);
        // Idempotent retry: if a voucher already exists for this key, return it.
        object? existingPay=SchemaSql.Scalar(c,tx,$"SELECT id FROM {Q(c,"payment_detail")} WHERE document_id=@d AND idempotency_key=@k",("@d",doc.S("id")),("@k","oneoff:"+key));
        if(existingPay!=null)return PaymentsEngine.Payment(c,tx,actor,existingPay.ToString()!);
        doc=ApproveDocumentAsActor(c,tx,actor,doc);
        J.Need((int)doc.N("status")==3,"اعتماد المستند مطلوب قبل حجز سند الصرف.",409);
        var reserved=PaymentsEngine.Reserve(c,tx,actor,doc.S("id"),new()
        {
            ["idempotencyKey"]="oneoff:"+key,["amount"]=built.total,["paidOn"]=paidOn,
            ["bankAccountCode"]=input.Required("bankAccountCode"),["reference"]="ONEOFF"
        });
        Store.Audit(c,tx,"recurring_payee.one_off",reserved.S("id"),Me(actor));
        return reserved;
    }

    /// <summary>After Pay: reclass FX gain so expense shows full SAR and FX account is credited separately.</summary>
    internal static void ApplyFxGainReclass(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject doc,JsonObject payment)
    {
        var meta=doc["metadata"] as JsonObject;var gain=meta?["fxGainReclass"] as JsonObject;
        if(gain==null)return;
        decimal amount=gain.N("amount");if(amount<=0)return;
        string day=payment.S("paidOn")[..10];
        Guid expense=PaymentsEngine.Account(c,tx,gain.Required("expenseAccountCode"));
        Guid fx=PaymentsEngine.Account(c,tx,gain.Required("fxAccountCode"));
        Guid centre=Guid.Parse(gain.S("costCentreId",SchemaOrgCostCentre.NoneId.ToString("D")));
        JournalPosting.Post(c,tx,new(Guid.Parse(doc.S("companyId")),PaymentsEngine.Period(c,tx,doc.S("companyId"),day),
            DateOnly.ParseExact(day[..7]+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture),DateOnly.ParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture),
            "payment_document",Guid.Parse(payment.S("id")),"fx_gain_reclass",(byte)doc.N("runMode"),doc.B("payoutBlocked"),Me(actor),[
                new(expense,centre,"none",amount,0,"إعادة تصنيف ربح فرق العملة"),
                new(fx,centre,"none",0,amount,"ربح فرق العملة")
            ]));
    }
}
