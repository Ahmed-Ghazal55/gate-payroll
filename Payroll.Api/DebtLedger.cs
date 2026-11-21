using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-20a/b/c: debt.debt + debt_movement + fin.journal in one TX through JournalPosting.
/// Simulation (run_mode=1) is rejected; payout_blocked allows accounting/debt but not disbursement payouts.</summary>
public static class DebtLedger
{
    public const byte DocDraft=1,DocSubmitted=2,DocApproved=3;
    static readonly HashSet<string> WaiverAccounts=new(StringComparer.Ordinal){"526601","526801","526802","526803","526804","526805","526806"};
    static readonly HashSet<string> AbsorbAccounts=new(StringComparer.Ordinal){"526602","526603"};
    static readonly HashSet<string> ReduceReasons=new(StringComparer.Ordinal){"تصحيح رصيد افتتاحي","خطأ إدخال","تسوية مراجعة","أخرى معتمدة"};

    public static void DemandDisburseAvailable()=>
        throw new ApiError("صرف مديونية جديدة عبر محرك المدفوعات غير متاح بعد؛ انتظر وحدة محرك المدفوعات.",409);

    public static Guid AccountId(DbConnection c,DbTransaction? tx,string code)
    {
        string mapped=code=="3101"?"331301":code;
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c AND is_postable=1 AND is_active=1",("@c",mapped));
        J.Need(id!=null,"حساب دليل الحسابات غير موجود أو غير قابل للترحيل: "+mapped,404);
        return id is Guid g?g:Guid.Parse(id!.ToString()!);
    }

    public static JsonObject PreviewReduce(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor,byte[] file,string filename,DateOnly periodMonth)
    {
        AccessPolicy.Demand(actor,"debts.create.administrative");
        J.Need(file.Length>0&&file.Length<=2_000_000,"ملف الاستيراد فارغ أو أكبر من الحد.",400);
        string hash=Convert.ToHexString(SHA256.HashData(file));
        var sheets=Imports.Read(file,filename);
        J.Need(sheets.Count>0&&sheets[0].Rows.Count>1,"الملف فارغ أو بلا صفوف.",400);
        var rows=new JsonArray();var errors=new JsonArray();decimal total=0m;
        var sheet=sheets[0].Rows;
        for(int i=1;i<sheet.Count;i++)
        {
            var r=sheet[i];if(r.All(string.IsNullOrWhiteSpace))continue;
            try
            {
                string Cell(int idx)=>idx<r.Count?r[idx].Trim():"";
                string debtId=Cell(1);J.Need(Guid.TryParse(debtId,out _),"معرّف المديونية غير صالح.");
                decimal amount=decimal.Parse(J.Digits(Cell(2)),CultureInfo.InvariantCulture);
                J.Need(amount>0&&amount==J.Money(amount),"مبلغ التخفيض غير صالح.");
                string reason=Cell(3);J.Need(ReduceReasons.Contains(reason)||reason.StartsWith("أخرى",StringComparison.Ordinal),"سبب التخفيض مطلوب من القائمة المعتمدة.");
                object? bal=SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id AND company_id=@c AND status='open'",
                    ("@id",debtId),("@c",SchemaSql.GuidText(companyId)));
                J.Need(bal!=null,"المديونية غير موجودة أو مغلقة.",404);
                decimal balance=Convert.ToDecimal(bal);
                J.Need(amount<=balance,"التخفيض يتجاوز الرصيد.");
                total=J.Money(total+amount);
                rows.Add(new JsonObject{["row"]=i+1,["debtId"]=debtId,["iqama"]=Cell(0),["amount"]=amount,["reason"]=reason,["balanceBefore"]=balance});
            }
            catch(Exception ex){errors.Add($"الصف {i+1}: {ex.Message}");}
        }
        J.Need(errors.Count==0&&rows.Count>0,"معاينة التخفيض بها أخطاء أو بلا صفوف صالحة.",400);
        string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash+"|"+total.ToString("F2",CultureInfo.InvariantCulture)+"|"+rows.Count)));
        return new(){["filename"]=Path.GetFileName(filename),["fileHash"]=hash,["validCount"]=rows.Count,["errorCount"]=0,["totalReduce"]=total,
            ["periodMonth"]=periodMonth.ToString("yyyy-MM-01",CultureInfo.InvariantCulture),["fingerprint"]=fingerprint,["rows"]=rows,
            ["allowedActions"]=new JsonArray("commit_reduce","preview_reduce")};
    }

    public static JsonObject CommitReduce(DbConnection c,DbTransaction tx,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,byte[] file,string filename,string fingerprint,Guid receivableAccountId,Guid contraAccountId)
    {
        AccessPolicy.Demand(maker,"debts.create.administrative");
        J.Need(!AccessPolicy.SamePerson(maker,checker.Identity?.Name??""),"اعتماد تخفيض الشيت لشخص مستقل عن مقدّم الطلب.",403);
        AccessPolicy.Demand(checker,"approvals.decide");
        var preview=PreviewReduce(c,tx,companyId,maker,file,filename,periodMonth);
        J.Need(preview.S("fingerprint")==fingerprint,"بصمة المعاينة لا تطابق الملف؛ أعد المعاينة.",409);
        MoneyWriteGuards.Demand(0,false,MoneyEffectKind.Debt);
        string hash=preview.S("fileHash");
        byte[] hashBytes=Convert.FromHexString(hash);
        object? dup=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"debt","import_batch")} WHERE company_id=@c AND import_kind='reduce' AND file_hash=@h",
            ("@c",SchemaSql.GuidText(companyId)),("@h",hashBytes));
        J.Need(dup==null,"نفس ملف التخفيض سُجّل من قبل.",409);

        Guid batchId=SchemaSql.NewId();
        decimal total=preview.N("totalReduce");
        string actor=maker.Identity!.Name!;
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","import_batch")}(id,company_id,period_month,amount,reason,file_hash,import_kind,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@amt,@reason,@h,'reduce',@st,0,0,@by,@at)
            """,("@id",SchemaSql.GuidText(batchId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@amt",total),("@reason","تخفيض شيت مديونيات معتمد"),("@h",hashBytes),("@st",DocDraft),("@by",actor),("@at",now));

        // Lines must land while the batch is still draft; frozen trigger blocks inserts after status>=3.
        int lineNo=0;
        var posted=new List<(Guid debtId,Guid lineId,decimal amount,string reason)>();
        foreach(var row in preview["rows"]!.AsArray().OfType<JsonObject>())
        {
            lineNo++;
            Guid debtId=Guid.Parse(row.S("debtId"));
            decimal amount=row.N("amount");
            decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
            J.Need(locked>=amount,"الرصيد تغيّر أثناء الاعتماد.",409);
            Guid lineId=SchemaSql.NewId();
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"debt","import_line")}(id,import_batch_id,period_month,line_no,source_row,debt_id,company_id,amount)
                VALUES(@id,@b,@pm,@ln,@src,@d,@c,@amt)
                """,("@id",SchemaSql.GuidText(lineId)),("@b",SchemaSql.GuidText(batchId)),("@pm",Month(periodMonth)),
                ("@ln",lineNo),("@src",row.N("row")),("@d",SchemaSql.GuidText(debtId)),("@c",SchemaSql.GuidText(companyId)),("@amt",-amount));
            posted.Add((debtId,lineId,amount,row.S("reason")));
        }
        AdvanceWorkflow(c,tx,"import_batch",batchId,SchemaSql.NewId());

        foreach(var (debtId,lineId,amount,reason) in posted)
        {
            var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
                companyId,periodId,periodMonth,periodMonth.AddDays(14),"debt_import",lineId,"debt_reduce",0,false,actor,
                [
                    new(contraAccountId,SchemaOrgCostCentre.NoneId,"none",amount,0m,reason),
                    new(receivableAccountId,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
                ],NarrationAr:"تخفيض شيت مديونيات"));
            InsertMovement(c,tx,companyId,periodMonth,debtId,lineId,-amount,"import",journal.JournalId,actor);
        }

        LedgerAudit(c,tx,"debt.import.reduce",SchemaSql.GuidText(batchId),actor,new(){["rowCount"]=lineNo,["fileHash"]=hash});
        return new(){["batchId"]=SchemaSql.GuidText(batchId),["rowCount"]=lineNo,["totalReduce"]=total,["fileHash"]=hash,["allowedActions"]=new JsonArray()};
    }

    public static JsonObject Collect(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal actor,decimal amount,string reason,Guid receivableAccountId,Guid cashAccountId,byte runMode,bool payoutBlocked)
    {
        AccessPolicy.Demand(actor,"debts.collect");
        MoneyWriteGuards.Demand(runMode,payoutBlocked,MoneyEffectKind.Debt);
        amount=J.Money(amount);J.Need(amount>0,"مبلغ التحصيل مطلوب.",400);
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(locked>=amount,"المبلغ يتجاوز الرصيد.",400);
        DemandOpenDebt(c,tx,debtId,companyId);

        Guid collectionId=SchemaSql.NewId();
        string by=actor.Identity!.Name!,now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","collection")}(id,company_id,period_month,debt_id,amount,reason,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@d,@amt,@reason,@st,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(collectionId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@d",SchemaSql.GuidText(debtId)),("@amt",amount),("@reason",reason),("@st",DocDraft),("@rm",runMode),("@pb",payoutBlocked?1:0),("@by",by),("@at",now));
        AdvanceWorkflow(c,tx,"collection",collectionId,SchemaSql.NewId());

        var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            companyId,periodId,periodMonth,periodMonth.AddDays(Math.Min(27,DateTime.UtcNow.Day)),"debt_collection",collectionId,"debt_collection",runMode,payoutBlocked,by,
            [
                new(cashAccountId,SchemaOrgCostCentre.NoneId,"none",amount,0m,"تحصيل مديونية"),
                new(receivableAccountId,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
            ],NarrationAr:reason));
        InsertMovement(c,tx,companyId,periodMonth,debtId,collectionId,-amount,"collection",journal.JournalId,by,runMode,payoutBlocked);
        LedgerAudit(c,tx,"debt.collected",SchemaSql.GuidText(debtId),by);
        decimal balance=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))!);
        return new(){["debtId"]=SchemaSql.GuidText(debtId),["collectionId"]=SchemaSql.GuidText(collectionId),["balance"]=balance,
            ["journalId"]=SchemaSql.GuidText(journal.JournalId),["allowedActions"]=balance>0?new JsonArray("collect","waive","absorb","reschedule"):new JsonArray()};
    }

    public static JsonObject Waive(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId)=>
        WaiveOrAbsorb(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,reason,expenseCode,receivableAccountId,expenseAccountId,WaiverAccounts,"debt_waiver","إعفاء مديونية");

    public static JsonObject Absorb(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId)=>
        WaiveOrAbsorb(c,tx,debtId,companyId,periodId,periodMonth,maker,checker,amount,reason,expenseCode,receivableAccountId,expenseAccountId,AbsorbAccounts,"debt_absorb","تحمل الشركة للمديونية");

    static JsonObject WaiveOrAbsorb(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,Guid periodId,DateOnly periodMonth,ClaimsPrincipal maker,ClaimsPrincipal checker,decimal amount,string reason,string expenseCode,Guid receivableAccountId,Guid expenseAccountId,HashSet<string> allowed,string purpose,string labelAr)
    {
        AccessPolicy.Demand(maker,"debts.waive");
        J.Need(allowed.Contains(expenseCode),"حساب المصروف غير مسموح لهذا الإجراء: "+expenseCode,400);
        J.Need(!AccessPolicy.SamePerson(maker,checker.Identity?.Name??""),"الاعتماد لشخص مستقل عن مقدّم الطلب (صانع≠مراجع).",403);
        AccessPolicy.Demand(checker,"approvals.decide");
        MoneyWriteGuards.Demand(0,false,MoneyEffectKind.Debt);
        amount=J.Money(amount);J.Need(amount>0&&!string.IsNullOrWhiteSpace(reason),"المبلغ والسبب مطلوبان.",400);
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(locked>=amount,"المبلغ يتجاوز الرصيد.",400);
        DemandOpenDebt(c,tx,debtId,companyId);

        Guid waiverId=SchemaSql.NewId();
        string by=maker.Identity!.Name!,now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","waiver")}(id,company_id,period_month,debt_id,amount,reason,expense_account_code,status,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@c,@pm,@d,@amt,@reason,@exp,@st,0,0,@by,@at)
            """,("@id",SchemaSql.GuidText(waiverId)),("@c",SchemaSql.GuidText(companyId)),("@pm",Month(periodMonth)),
            ("@d",SchemaSql.GuidText(debtId)),("@amt",amount),("@reason",reason),("@exp",expenseCode),("@st",DocDraft),("@by",by),("@at",now));
        AdvanceWorkflow(c,tx,"waiver",waiverId,SchemaSql.NewId());

        var journal=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            companyId,periodId,periodMonth,periodMonth.AddDays(14),"debt_waiver",waiverId,purpose,0,false,by,
            [
                new(expenseAccountId,SchemaOrgCostCentre.NoneId,"none",amount,0m,labelAr),
                new(receivableAccountId,SchemaOrgCostCentre.NoneId,"none",0m,amount,"تخفيض ذمة المندوب")
            ],NarrationAr:reason,GuardPurpose:purpose));
        InsertMovement(c,tx,companyId,periodMonth,debtId,waiverId,-amount,"waiver",journal.JournalId,by);
        LedgerAudit(c,tx,purpose=="debt_absorb"?"debt.absorbed":"debt.waived",SchemaSql.GuidText(debtId),by);
        decimal balance=Convert.ToDecimal(SchemaSql.Scalar(c,tx,$"SELECT balance FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id",("@id",SchemaSql.GuidText(debtId)))!);
        if(balance==0)
            SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET status=@s WHERE id=@id AND balance=0",("@s",purpose=="debt_absorb"?"waived":"waived"),("@id",SchemaSql.GuidText(debtId)));
        return new(){["debtId"]=SchemaSql.GuidText(debtId),["waiverId"]=SchemaSql.GuidText(waiverId),["balance"]=balance,
            ["expenseAccountCode"]=expenseCode,["journalId"]=SchemaSql.GuidText(journal.JournalId),
            ["allowedActions"]=balance>0?new JsonArray("collect","waive","absorb","reschedule"):new JsonArray()};
    }

    public static JsonObject Reschedule(DbConnection c,DbTransaction tx,Guid debtId,ClaimsPrincipal actor,decimal instalment)
    {
        AccessPolicy.Demand(actor,"debts.reschedule");
        instalment=J.Money(instalment);J.Need(instalment>0,"القسط الجديد مطلوب.",400);
        decimal locked=SchemaDebt.LockDebt(c,tx,SchemaSql.GuidText(debtId));
        J.Need(instalment<=locked,"القسط يتجاوز الرصيد.",400);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET instalment=@i WHERE id=@id AND status='open'",
            ("@i",instalment),("@id",SchemaSql.GuidText(debtId)));
        LedgerAudit(c,tx,"debt.rescheduled",SchemaSql.GuidText(debtId),actor.Identity!.Name!);
        return new(){["debtId"]=SchemaSql.GuidText(debtId),["instalment"]=instalment,["balance"]=locked,["allowedActions"]=new JsonArray("reschedule","collect","waive","absorb")};
    }

    public static JsonObject Ageing(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"debts.view");
        using var cmd=Store.Command(c,tx,$"""
            SELECT d.id,d.balance,d.created_at FROM {SchemaSql.Q(c,"debt","debt")} d
            WHERE d.company_id=@c AND d.status='open' AND d.balance>0
            """,("@c",SchemaSql.GuidText(companyId)));
        using var reader=cmd.ExecuteReader();
        decimal b0_30=0,b31_60=0,b61_90=0,b90=0,total=0;
        var asOf=DateOnly.FromDateTime(DateTime.UtcNow);
        while(reader.Read())
        {
            decimal bal=Convert.ToDecimal(reader["balance"]);total=J.Money(total+bal);
            DateTime created=Convert.ToDateTime(reader["created_at"],CultureInfo.InvariantCulture);
            int days=asOf.DayNumber-DateOnly.FromDateTime(created).DayNumber;
            if(days<=30)b0_30=J.Money(b0_30+bal);
            else if(days<=60)b31_60=J.Money(b31_60+bal);
            else if(days<=90)b61_90=J.Money(b61_90+bal);
            else b90=J.Money(b90+bal);
        }
        return new(){["companyId"]=SchemaSql.GuidText(companyId),["totalOpen"]=total,
            ["buckets"]=new JsonArray(
                new JsonObject{["code"]="0_30",["labelAr"]="من 0 إلى 30 يومًا",["amount"]=b0_30},
                new JsonObject{["code"]="31_60",["labelAr"]="من 31 إلى 60 يومًا",["amount"]=b31_60},
                new JsonObject{["code"]="61_90",["labelAr"]="من 61 إلى 90 يومًا",["amount"]=b61_90},
                new JsonObject{["code"]="90_plus",["labelAr"]="أكثر من 90 يومًا",["amount"]=b90}),
            ["allowedActions"]=new JsonArray()};
    }

    public static JsonObject PrintRegister(DbConnection c,DbTransaction? tx,Guid companyId,ClaimsPrincipal actor,string? status=null)
    {
        AccessPolicy.Demand(actor,"debts.statement.print");
        var items=new JsonArray();
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,category,principal,balance,instalment,status,promissory_flag,erp_rider_id
            FROM {SchemaSql.Q(c,"debt","debt")} WHERE company_id=@c{(string.IsNullOrWhiteSpace(status)?"":" AND status=@s")}
            ORDER BY category,id
            """,string.IsNullOrWhiteSpace(status)?[("@c",SchemaSql.GuidText(companyId))]:[("@c",SchemaSql.GuidText(companyId)),("@s",status!)]);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            items.Add(new JsonObject{
                ["id"]=reader["id"].ToString(),["category"]=reader["category"].ToString(),
                ["principal"]=Convert.ToDecimal(reader["principal"]),["balance"]=Convert.ToDecimal(reader["balance"]),
                ["instalment"]=reader["instalment"] is DBNull?null:Convert.ToDecimal(reader["instalment"]),
                ["status"]=reader["status"].ToString(),["promissory"]=Convert.ToInt32(reader["promissory_flag"])==1,
                ["erpRiderId"]=reader["erp_rider_id"].ToString()});
        return new(){["companyId"]=SchemaSql.GuidText(companyId),["printedAt"]=DateTimeOffset.UtcNow.ToString("O"),["items"]=items,["allowedActions"]=new JsonArray()};
    }

    public static JsonObject LinkPromissory(DbConnection c,DbTransaction tx,string noteId,Guid debtId,ClaimsPrincipal actor)
    {
        AccessPolicy.Demand(actor,"debts.create.administrative");
        var note=Store.Get(c,noteId,"promissory_note",tx)??throw new ApiError("سند الأمر غير موجود.",404);
        DemandOpenDebt(c,tx,debtId,Guid.Empty,checkCompany:false);
        note["debtId"]=SchemaSql.GuidText(debtId);note["linkedAt"]=DateTimeOffset.UtcNow.ToString("O");note["linkedBy"]=actor.Identity!.Name!;
        Store.Save(c,"promissory_note",note,tx);
        SchemaSql.Exec(c,tx,$"UPDATE {SchemaSql.Q(c,"debt","debt")} SET promissory_flag=1 WHERE id=@id",("@id",SchemaSql.GuidText(debtId)));
        LedgerAudit(c,tx,"promissory.link_debt",noteId,actor.Identity!.Name!);
        return new(){["noteId"]=noteId,["debtId"]=SchemaSql.GuidText(debtId),["allowedActions"]=new JsonArray("link_debt")};
    }

    static void LedgerAudit(DbConnection c,DbTransaction tx,string action,string entity,string actor,JsonObject? extra=null)
    {
        EnsureDocumentStore(c,tx);
        var doc=new JsonObject{["id"]=Guid.NewGuid().ToString("N"),["action"]=action,["entityId"]=entity,["actor"]=actor,
            ["at"]=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)};
        if(extra!=null)foreach(var kv in extra)doc[kv.Key]=kv.Value?.DeepClone();
        Store.Save(c,"audit",doc,tx);
    }

    static void EnsureDocumentStore(DbConnection c,DbTransaction tx)
    {
        if(SchemaSql.IsSql(c))
            SchemaSql.Exec(c,tx,"""
                IF OBJECT_ID(N'dbo.records', N'U') IS NULL
                  CREATE TABLE dbo.records(id nvarchar(128) NOT NULL PRIMARY KEY, kind nvarchar(40) NOT NULL, body nvarchar(max) NOT NULL CHECK(ISJSON(body)=1));
                """);
        else
            SchemaSql.Exec(c,tx,"""
                CREATE TABLE IF NOT EXISTS records(id TEXT PRIMARY KEY, kind TEXT NOT NULL, body TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_records_kind ON records(kind);
                """);
    }

    static void DemandOpenDebt(DbConnection c,DbTransaction tx,Guid debtId,Guid companyId,bool checkCompany=true)
    {
        object? status=SchemaSql.Scalar(c,tx,$"SELECT status FROM {SchemaSql.Q(c,"debt","debt")} WHERE id=@id{(checkCompany?" AND company_id=@c":"")}",
            checkCompany?[("@id",SchemaSql.GuidText(debtId)),("@c",SchemaSql.GuidText(companyId))]:[("@id",SchemaSql.GuidText(debtId))]);
        J.Need(status?.ToString()=="open","المديونية غير مفتوحة.",409);
    }

    static void AdvanceWorkflow(DbConnection c,DbTransaction tx,string table,Guid id,Guid approvedBy)
    {
        string t=SchemaSql.Q(c,"debt",table);
        SchemaSql.Exec(c,tx,$"UPDATE {t} SET status=@s WHERE id=@id AND status=@d",("@s",DocSubmitted),("@id",SchemaSql.GuidText(id)),("@d",DocDraft));
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"UPDATE {t} SET status=@s,approved_by=@u,approved_at=@at WHERE id=@id AND status=@sub",
            ("@s",DocApproved),("@u",SchemaSql.GuidText(approvedBy)),("@at",now),("@id",SchemaSql.GuidText(id)),("@sub",DocSubmitted));
    }

    static void InsertMovement(DbConnection c,DbTransaction tx,Guid companyId,DateOnly periodMonth,Guid debtId,Guid sourceId,decimal amount,string sourceType,Guid journalId,string actor,byte runMode=0,bool payoutBlocked=false)
    {
        Guid movId=SchemaSql.NewId();
        string now=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        DateOnly occurred=periodMonth.AddDays(Math.Min(20,DateTime.UtcNow.Day));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"debt","debt_movement")}
            (id,period_month,debt_id,company_id,occurred_on,amount,source_type,source_id,journal_id,run_mode,payout_blocked,created_by,created_at)
            VALUES(@id,@pm,@d,@c,@on,@amt,@st,@sid,@j,@rm,@pb,@by,@at)
            """,("@id",SchemaSql.GuidText(movId)),("@pm",Month(periodMonth)),("@d",SchemaSql.GuidText(debtId)),
            ("@c",SchemaSql.GuidText(companyId)),("@on",Day(occurred)),("@amt",amount),("@st",sourceType),
            ("@sid",SchemaSql.GuidText(sourceId)),("@j",SchemaSql.GuidText(journalId)),("@rm",runMode),("@pb",payoutBlocked?1:0),
            ("@by",actor),("@at",now));
    }

    static string Month(DateOnly d)=>d.ToString("yyyy-MM-01",CultureInfo.InvariantCulture);
    static string Day(DateOnly d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
}
