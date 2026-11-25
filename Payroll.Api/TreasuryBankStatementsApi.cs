using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>U-W2-36: bank statement upload / auto-match / adjust.
/// Adjust posts a journal through JournalPosting after approval. One audit row per file (M-0606).</summary>
public static class TreasuryBankStatementsApi
{
    public static JsonObject Upload(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        string bankAccountId=input.Required("bankAccountId");
        string companyId,registrationId,chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT company_id,registration_id,account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
        using(var r=cmd.ExecuteReader())
        {
            J.Need(r.Read(),"الحساب البنكي غير موجود.",404);
            companyId=r.GetValue(0).ToString()!;registrationId=r.GetValue(1).ToString()!;chartAccountId=r.GetValue(2).ToString()!;
        }
        FinancialScopes.DemandSponsor(actor,companyId);
        DemandTreasuryAccount(actor,companyId,chartAccountId,c,tx);
        DateOnly periodMonth=ParseMonth(input.Required("periodMonth"));
        decimal opening=J.Money(PayValues.Number(input,"openingBalance"));
        decimal closing=J.Money(PayValues.Number(input,"closingBalance"));
        var lines=PayValues.Array(input["lines"]??new JsonArray(),5000).OfType<JsonObject>().ToList();
        J.Need(lines.Count>0,"كشف البنك يحتاج سطرًا واحدًا على الأقل.");
        decimal sum=J.Money(lines.Sum(l=>PayValues.Number(l,"amount",-1000000000)));
        J.Need(J.Money(closing-opening)==sum,"مجموع أسطر الكشف يجب أن يساوي فرق الرصيد الختامي عن الافتتاحي.");
        string fileContent=input.S("fileContent",string.Join("\n",lines.Select(l=>$"{l.S("occurredOn")}|{l.S("reference")}|{l.N("amount")}")));
        byte[] hash=SHA256.HashData(Encoding.UTF8.GetBytes(fileContent));
        // Duplicate active upload of the same file hash is refused (filtered unique).
        object? dup=SchemaSql.Scalar(c,tx,$"""
            SELECT id FROM {SchemaSql.Q(c,"treasury","bank_statement")}
            WHERE bank_account_id=@b AND file_hash=@h AND status<>5
            """,("@b",bankAccountId),("@h",hash));
        J.Need(dup is null or DBNull,"نفس ملف الكشف مرفوع مسبقًا لهذا الحساب.",409);

        string statementId=SchemaSql.GuidText(SchemaSql.NewId());
        string actorName=actor.Identity!.Name!;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_statement")}
            (id,company_id,registration_id,bank_account_id,period_month,file_hash,opening_balance,closing_balance,status,created_by,created_at)
            VALUES(@id,@c,@r,@b,@m,@h,@o,@cl,1,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",statementId),("@c",companyId),("@r",registrationId),("@b",bankAccountId),
            ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
            ("@h",hash),("@o",opening),("@cl",closing),("@by",actorName));

        int lineNo=0;
        foreach(var line in lines)
        {
            lineNo++;
            DateOnly occurred=ParseDay(line.Required("occurredOn"));
            J.Need(occurred.Year==periodMonth.Year&&occurred.Month==periodMonth.Month,"تاريخ السطر خارج شهر الكشف.");
            decimal amount=J.Money(PayValues.Number(line,"amount",-1000000000));
            J.Need(amount!=0,"مبلغ سطر الكشف لا يكون صفرًا.");
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"treasury","bank_statement_line")}
                (id,statement_id,company_id,period_month,line_no,occurred_on,amount,reference)
                VALUES(@id,@s,@c,@m,@n,@d,@a,@ref)
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",statementId),("@c",companyId),
                ("@m",periodMonth.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@n",lineNo),("@d",occurred.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@a",amount),("@ref",(object?)line.S("reference")??DBNull.Value));
        }

        // One Store.Audit row per file (M-0606 / FLOW1) so Notifications.OnAudit fires.
        // entity/recordId + lineCount/fileHash required by TreasuryW2ApiTests + grok-TRIAL4.
        Store.Audit(c,tx,"treasury.bank_statement_uploaded",statementId,actorName,new()
        {
            ["lineCount"]=lineNo,
            ["fileHash"]=Convert.ToHexString(hash),
            ["bankAccountId"]=bankAccountId
        });
        return View(c,tx,actor,statementId);
    }

    public static JsonObject AutoMatch(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);
        var statement=View(c,tx,actor,id);
        J.Need(statement.N("statusCode") is 1 or 2,"الكشف مغلق للمطابقة.");
        string companyId=statement.S("companyId");
        string bankAccountId=statement.S("bankAccountId");
        string chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
            chartAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);

        int matched=0;
        var unmatched=new JsonArray();
        foreach(var line in statement["lines"]!.AsArray().OfType<JsonObject>())
        {
            if(line.B("matched"))continue;
            decimal amount=line.N("amount");
            decimal abs=J.Money(Math.Abs(amount));
            DateOnly occurred=ParseDay(line.S("occurredOn"));
            string reference=line.S("reference").Trim();
            // Blank reference never auto-matches (would steal the journal line from a later referenced row).
            if(reference==""){unmatched.Add(line.S("id"));continue;}
            // Unambiguous only: amount + date window (±3 days) + exact reference; ties stay unmatched.
            string sidePred=amount>0?"jl.debit=@amt":"jl.credit=@amt";
            string sql=$"""
                SELECT jl.id FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
                  AND {sidePred}
                  AND j.journal_date BETWEEN @from AND @to
                  AND NOT EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"treasury","bank_match")} m WHERE m.journal_line_id=jl.id AND m.status=1)
                  AND (jl.narration_ar=@ref OR j.voucher_no=@ref OR j.narration_ar=@ref)
                ORDER BY jl.line_no
                """;
            var candidates=new List<string>();
            using(var cmd=Store.Command(c,tx,sql,
                ("@c",companyId),("@a",chartAccountId),("@amt",abs),
                ("@from",occurred.AddDays(-3).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@to",occurred.AddDays(3).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@ref",reference)))
            using(var r=cmd.ExecuteReader())
            while(r.Read())candidates.Add(r.GetValue(0) switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(r.GetValue(0),CultureInfo.InvariantCulture)!});
            if(candidates.Count!=1){unmatched.Add(line.S("id"));continue;}
            string journalLineId=candidates[0];
            SchemaTreasury.LockMatchSources(c,tx,line.S("id"),journalLineId);
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {SchemaSql.Q(c,"treasury","bank_match")}
                (id,statement_line_id,journal_line_id,company_id,period_month,amount,status,created_by,created_at)
                VALUES(@id,@s,@j,@c,@m,@a,1,@by,{SchemaSql.NowLiteral(c)})
                """,
                ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",line.S("id")),("@j",journalLineId),
                ("@c",companyId),("@m",statement.S("periodMonth").Length>=10?statement.S("periodMonth")[..10]:statement.S("periodMonth")+"-01"),
                ("@a",abs),("@by",actor.Identity!.Name!));
            matched++;
        }
        Store.Audit(c,tx,"treasury.bank_statement_auto_matched",id,actor.Identity!.Name!);
        var view=View(c,tx,actor,id);
        view["matchedCount"]=matched;view["unmatchedLineIds"]=unmatched;
        return view;
    }

    public const string DispositionKind="bank_line_disposition";
    public const string BankFeeAccount="541401",BankIncomeAccount="422103",BankSuspenseAccount="114108";

    /// <summary>TRIAL6: generic adjust removed — use per-line actions (payment/receipt/journal/timing/open_original).</summary>
    public static JsonObject RequestAdjust(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject input)
    {
        _=c;_=tx;_=actor;_=id;_=input;
        throw new ApiError("التسوية العامة أُلغيت. استخدم أزرار سطر الكشف: إنشاء سند صرف أو قبض أو تسجيل قيد أو فرق توقيت أو فتح القيد الأصلي (لا يُستخدم 422202/542102).",409);
    }

    public static JsonObject ApproveAdjust(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string adjustId)
    {
        _=c;_=tx;_=actor;_=adjustId;
        throw new ApiError("اعتماد التسوية العامة أُلغي. ارحّل سند الصرف/القبض أو القيد من مساره العادي بعد الاعتماد؛ السطر يُطابق تلقائيًا.",409);
    }

    /// <summary>Start a line action: timing (no entry) or draft payment/receipt/journal prefills via ManualJournals; open_original returns reverse prefill.</summary>
    public static JsonObject LineAction(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string statementId,string lineId,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية مطابقة البنوك مطلوبة.",403);
        SchemaTreasury.Ensure(c,tx);SchemaFinAccount.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        var statement=View(c,tx,actor,statementId);
        var line=statement["lines"]!.AsArray().OfType<JsonObject>().FirstOrDefault(l=>l.S("id")==lineId)
            ??throw new ApiError("سطر الكشف غير موجود.",404);
        J.Need(!line.B("matched"),"السطر مطابق مسبقًا.",409);
        string kind=input.S("kind").Trim().ToLowerInvariant();
        J.Need(kind is "timing" or "payment" or "receipt" or "journal" or "open_original" or "clear_timing","نوع إجراء السطر غير معروف.",400);
        string note=input.S("note",input.S("reason")).Trim();
        if(kind=="clear_timing")
        {
            var disp=Store.Get(c,DispositionId(lineId),DispositionKind,tx);
            J.Need(disp!=null&&disp.S("disposition")=="timing","السطر ليس فرق توقيت.",409);
            Store.Save(c,DispositionKind,new(){["id"]=DispositionId(lineId),["statementId"]=statementId,["lineId"]=lineId,["disposition"]="cleared",["clearedBy"]=actor.Identity!.Name,["clearedAt"]=DateTimeOffset.UtcNow.ToString("O")},tx);
            Store.Audit(c,tx,"treasury.bank_line_timing_cleared",lineId,actor.Identity!.Name!);
            return View(c,tx,actor,statementId);
        }
        if(kind=="timing")
        {
            J.Need(note.Length is >=3 and <=500,"ملاحظة فرق التوقيت من 3 إلى 500 حرف.");
            var disp=new JsonObject
            {
                ["id"]=DispositionId(lineId),["statementId"]=statementId,["lineId"]=lineId,
                ["disposition"]="timing",["note"]=note,["carryToNext"]=true,
                ["amount"]=line.N("amount"),["bankAccountId"]=statement.S("bankAccountId"),
                ["periodMonth"]=statement.S("periodMonth"),["reference"]=line.S("reference"),
                ["createdBy"]=actor.Identity!.Name,["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
            };
            Store.Save(c,DispositionKind,disp,tx);
            Store.Audit(c,tx,"treasury.bank_line_timing",lineId,actor.Identity!.Name!);
            disp["allowedActions"]=new JsonArray("clear_timing");
            return disp;
        }
        if(kind=="open_original")
        {
            string chartAccountId;
            using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",statement.S("bankAccountId"))))
                chartAccountId=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);
            decimal amount=line.N("amount");decimal abs=J.Money(Math.Abs(amount));
            DateOnly occurred=ParseDay(line.S("occurredOn"));
            string sidePred=amount>0?"jl.debit=@amt":"jl.credit=@amt";
            string? journalId=null;string? journalLineId=null;
            string refText=line.S("reference");
            // grok-TRIAL6 HIGH: reference must match — no amount-only fallback within ±7 days.
            using(var cmd=Store.Command(c,tx,$"""
                SELECT j.id,jl.id FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","journal")} j ON j.id=jl.journal_id
                WHERE jl.company_id=@c AND jl.account_id=@a AND j.status=2 AND j.run_mode=0
                  AND {sidePred}
                  AND j.journal_date BETWEEN @from AND @to
                  AND (jl.narration_ar=@ref OR j.voucher_no=@ref OR j.narration_ar=@ref)
                ORDER BY jl.line_no
                """,("@c",statement.S("companyId")),("@a",chartAccountId),("@amt",abs),
                ("@from",occurred.AddDays(-7).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@to",occurred.AddDays(7).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),
                ("@ref",refText)))
            using(var r=cmd.ExecuteReader())
            if(r.Read()){journalId=r.GetValue(0).ToString();journalLineId=r.GetValue(1).ToString();}
            J.Need(journalId!=null,"لا يوجد قيد أصلي مطابق للمرجع؛ سجّل قيد تصحيح جديد.",404);

            // Build reversing draft (swap debit/credit) linked to the posted journal — second person must approve.
            string matchedJournalId=journalId!;
            var reverseLines=new JsonArray();
            using(var cmd=Store.Command(c,tx,$"""
                SELECT a.code,jl.debit,jl.credit,COALESCE(jl.narration_ar,'') FROM {SchemaSql.Q(c,"fin","journal_line")} jl
                JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=jl.account_id
                WHERE jl.journal_id=@j ORDER BY jl.line_no
                """,("@j",matchedJournalId)))
            using(var r=cmd.ExecuteReader())
            while(r.Read())
            {
                decimal deb=J.Money(Convert.ToDecimal(r.GetValue(1),CultureInfo.InvariantCulture));
                decimal cre=J.Money(Convert.ToDecimal(r.GetValue(2),CultureInfo.InvariantCulture));
                reverseLines.Add(new JsonObject
                {
                    ["accountCode"]=r.GetValue(0).ToString()!,
                    ["debit"]=cre,["credit"]=deb,
                    ["note"]=r.GetValue(3).ToString() is {Length:>0} n?n:"عكس قيد بنكي"
                });
            }
            J.Need(reverseLines.Count>=2,"القيد الأصلي بلا أسطر للعكس.",409);
            string revRef="REV-BANK:"+refText;
            if(revRef.Length>200)revRef=revRef[..200];
            // Unique reference if a prior reverse draft exists for the same line.
            if(Store.List(c,ManualJournals.Kind,tx).Any(d=>d.S("companyId")==statement.S("companyId")&&d.S("reference")==revRef))
                revRef=$"REV-BANK:{refText}:{lineId[..8]}";
            var reverseDraft=ManualJournals.Create(c,tx,actor,new()
            {
                ["companyId"]=statement.S("companyId"),["date"]=line.S("occurredOn"),
                ["reference"]=revRef,["description"]="عكس وتصحيح قيد بنكي من مطابقة الكشف",
                ["lines"]=reverseLines,["bankStatementLineId"]=lineId,["statementId"]=statementId,["reconKind"]="open_original"
            });
            reverseDraft["reversesJournalId"]=matchedJournalId;
            Store.Save(c,ManualJournals.Kind,reverseDraft,tx);
            Store.Save(c,DispositionKind,new()
            {
                ["id"]=DispositionId(lineId),["statementId"]=statementId,["lineId"]=lineId,
                ["disposition"]="pending_document",["reconKind"]="open_original",["documentId"]=reverseDraft.S("id"),
                ["originalJournalId"]=matchedJournalId,["createdBy"]=actor.Identity!.Name,
                ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
            },tx);
            Store.Audit(c,tx,"treasury.bank_line_open_original",lineId,actor.Identity!.Name!);
            string formRoute=$"/app/accounting?tab=manual_journals&draftId={Uri.EscapeDataString(reverseDraft.S("id"))}";
            return new()
            {
                ["kind"]="open_original",["formAction"]="reverse",["originalJournalId"]=matchedJournalId,
                ["originalJournalLineId"]=journalLineId??"",["documentId"]=reverseDraft.S("id"),
                ["documentKind"]="manual_journal",["status"]=reverseDraft.S("status"),
                ["prefill"]=new JsonObject
                {
                    ["companyId"]=statement.S("companyId"),["date"]=line.S("occurredOn"),
                    ["reference"]=revRef,["description"]=reverseDraft.S("description"),
                    ["reversesJournalId"]=matchedJournalId,["lines"]=reverseLines.DeepClone(),
                    ["bankStatementLineId"]=lineId,["statementId"]=statementId
                },
                ["formRoute"]=formRoute,
                ["noteAr"]="لا يُعدَّل قيد مرحّل؛ أُنشئت مسودة عكس تحتاج اعتماد شخص ثانٍ.",
                ["allowedActions"]=new JsonArray("approve","reject","cancel","edit_draft")
            };
        }

        // payment / receipt / journal → ManualJournals draft linked to the line
        J.Need(AccessPolicy.Has(actor,ManualJournals.CreatePermission)||AccessPolicy.Administrator(actor),"صلاحية إنشاء قيد يدوي مطلوبة لإنشاء سند/قيد التسوية.",403);
        J.Need(note.Length is >=3 and <=500,"البيان من 3 إلى 500 حرف.");
        decimal lineAmount=line.N("amount");
        string counter=kind switch
        {
            "payment"=>BankFeeAccount,
            "receipt"=>BankIncomeAccount,
            _=>BankSuspenseAccount
        };
        string bankCode;
        using(var cmd=Store.Command(c,tx,$"""
            SELECT a.code FROM {SchemaSql.Q(c,"treasury","bank_account")} b
            JOIN {SchemaSql.Q(c,"fin","account")} a ON a.id=b.account_id WHERE b.id=@id
            """,("@id",statement.S("bankAccountId"))))
            bankCode=cmd.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);
        // Statement +amount = money into bank (Dr bank). payment charges are typically −amount.
        decimal absAmt=J.Money(Math.Abs(lineAmount));
        bool moneyIn=lineAmount>0;
        // payment: always expense out of bank (Cr bank / Dr expense) regardless of sign convention on unmatched fee lines
        // receipt: income into bank (Dr bank / Cr income)
        // journal/suspense: follow statement sign
        JsonArray lines;
        if(kind=="payment")
            lines=new(
                new JsonObject{["accountCode"]=counter,["debit"]=absAmt,["credit"]=0,["note"]=note},
                new JsonObject{["accountCode"]=bankCode,["debit"]=0,["credit"]=absAmt,["note"]=note});
        else if(kind=="receipt")
            lines=new(
                new JsonObject{["accountCode"]=bankCode,["debit"]=absAmt,["credit"]=0,["note"]=note},
                new JsonObject{["accountCode"]=counter,["debit"]=0,["credit"]=absAmt,["note"]=note});
        else if(moneyIn)
            lines=new(
                new JsonObject{["accountCode"]=bankCode,["debit"]=absAmt,["credit"]=0,["note"]=note},
                new JsonObject{["accountCode"]=counter,["debit"]=0,["credit"]=absAmt,["note"]=note});
        else
            lines=new(
                new JsonObject{["accountCode"]=counter,["debit"]=absAmt,["credit"]=0,["note"]=note},
                new JsonObject{["accountCode"]=bankCode,["debit"]=0,["credit"]=absAmt,["note"]=note});

        string reference=$"BANK-{kind.ToUpperInvariant()}-{line.S("reference")}";
        if(reference.Length>200)reference=reference[..200];
        var draftInput=new JsonObject
        {
            ["companyId"]=statement.S("companyId"),["date"]=line.S("occurredOn"),
            ["reference"]=reference,["description"]=note,["lines"]=lines,
            ["bankStatementLineId"]=lineId,["statementId"]=statementId,["reconKind"]=kind
        };
        var draft=ManualJournals.Create(c,tx,actor,draftInput);
        Store.Save(c,DispositionKind,new()
        {
            ["id"]=DispositionId(lineId),["statementId"]=statementId,["lineId"]=lineId,
            ["disposition"]="pending_document",["reconKind"]=kind,["documentId"]=draft.S("id"),
            ["counterAccountCode"]=counter,["createdBy"]=actor.Identity!.Name,
            ["createdAt"]=DateTimeOffset.UtcNow.ToString("O")
        },tx);
        Store.Audit(c,tx,"treasury.bank_line_document_drafted",lineId,actor.Identity!.Name!);
        return new()
        {
            ["kind"]="line_document",["reconKind"]=kind,["documentId"]=draft.S("id"),
            ["documentKind"]="manual_journal",["status"]=draft.S("status"),
            ["counterAccountCode"]=counter,["bankAccountCode"]=bankCode,
            ["formRoute"]=$"/app/accounting?tab=manual_journals&draftId={Uri.EscapeDataString(draft.S("id"))}",
            ["prefill"]=draftInput,["allowedActions"]=new JsonArray("approve","reject","cancel","edit_draft"),
            ["noteAr"]="المسودة تُعتمد من مسار القيد العادي؛ بعد الترحيل يُطابق السطر تلقائيًا."
        };
    }

    /// <summary>Post the approved recon draft into fin.journal so AutoMatch / MatchAfterPostedJournal can link the line.</summary>
    public static string PostLinkedFinJournal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject draft)
    {
        SchemaFinJournal.Ensure(c,tx);SchemaFinPeriod.Ensure(c,tx);SchemaFinPeriodBalance.Ensure(c,tx);SchemaOrgCostCentre.Ensure(c,tx);
        string companyId=draft.S("companyId");
        DateOnly month=ParseMonth(draft.S("period").Length==7?draft.S("period")+"-01":draft.S("period"));
        DateOnly journalDate=ParseDay(draft.S("date"));
        Guid periodId=EnsureOpenPeriod(c,tx,companyId,month);
        Guid companyGuid=Guid.Parse(companyId);
        Guid sourceId=Guid.TryParse(draft.S("id").Replace("manual-journal:",""),out var sid)?sid:SchemaSql.NewId();
        var lines=new List<JournalPosting.Line>();
        foreach(var line in draft["lines"]!.AsArray().OfType<JsonObject>())
        {
            string code=line.S("accountCode");
            Guid accountId=Guid.Parse(AccountIdByCode(c,tx,code));
            lines.Add(new(accountId,SchemaOrgCostCentre.NoneId,"none",line.N("debit"),line.N("credit"),line.S("note",draft.S("description"))));
        }
        var result=JournalPosting.Post(c,tx,new JournalPosting.Draft(
            companyGuid,periodId,month,journalDate,
            "bank_recon",sourceId,"bank_recon",0,false,actor.Identity!.Name!,lines,
            NarrationAr:draft.S("description"),VoucherNo:null));
        // Promote draft→posted (JournalPosting inserts as draft then posts in same path when status set — PostCore posts status=2).
        return SchemaSql.GuidText(result.JournalId);
    }

    public static void MatchAfterPostedJournal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string lineId,string journalId)
    {
        var disp=Store.Get(c,DispositionId(lineId),DispositionKind,tx);
        if(disp==null||disp.S("disposition") is "matched" or "timing")return;
        string statementId=disp.S("statementId");
        var statement=View(c,tx,actor,statementId);
        var line=statement["lines"]!.AsArray().OfType<JsonObject>().FirstOrDefault(l=>l.S("id")==lineId);
        if(line==null||line.B("matched"))return;
        string chartAccountId;
        using(var cmd=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",statement.S("bankAccountId"))))
            chartAccountId=cmd.ExecuteScalar()?.ToString()??"";
        if(chartAccountId=="")return;
        decimal amount=line.N("amount");decimal abs=J.Money(Math.Abs(amount));
        string sidePred=amount>0?"jl.debit=@amt":"jl.credit=@amt";
        // payment drafts credit the bank even when statement amount was negative — match credit side for money-out
        if(disp.S("reconKind")=="payment")sidePred="jl.credit=@amt";
        if(disp.S("reconKind")=="receipt")sidePred="jl.debit=@amt";
        object? jl=SchemaSql.Scalar(c,tx,$"""
            SELECT jl.id FROM {SchemaSql.Q(c,"fin","journal_line")} jl
            WHERE jl.journal_id=@j AND jl.account_id=@a AND {sidePred}
            ORDER BY jl.line_no
            """,("@j",journalId),("@a",chartAccountId),("@amt",abs));
        if(jl is null or DBNull)return;
        string journalLineId=jl switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(jl,CultureInfo.InvariantCulture)!};
        SchemaTreasury.LockMatchSources(c,tx,lineId,journalLineId);
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","bank_match")}
            (id,statement_line_id,journal_line_id,company_id,period_month,amount,status,created_by,created_at)
            VALUES(@id,@s,@j,@c,@m,@a,1,@by,{SchemaSql.NowLiteral(c)})
            """,
            ("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@s",lineId),("@j",journalLineId),
            ("@c",statement.S("companyId")),
            ("@m",statement.S("periodMonth").Length>=10?statement.S("periodMonth")[..10]:statement.S("periodMonth")+"-01"),
            ("@a",abs),("@by",actor.Identity!.Name!));
        disp["disposition"]="matched";disp["matchedJournalId"]=journalId;disp["matchedAt"]=DateTimeOffset.UtcNow.ToString("O");
        Store.Save(c,DispositionKind,disp,tx);
        Store.Audit(c,tx,"treasury.bank_line_auto_matched",lineId,actor.Identity!.Name!);
    }

    static string DispositionId(string lineId)=>"bank-line:"+lineId;

    /// <summary>FLOW1 Critical: close dead-end pending_approval when approve cannot post yet (reject/cancel path).</summary>
    public static JsonObject RejectAdjust(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string adjustId,JsonObject input)
    {
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"approvals.decide")||AccessPolicy.Has(actor,"treasury.bank_reconcile"),"صلاحية رفض التسوية مطلوبة.",403);
        var draft=Store.Get(c,adjustId,"bank_statement_adjust",tx)??throw new ApiError("طلب التسوية غير موجود.",404);
        J.Need(draft.S("status")=="pending_approval","التسوية ليست بانتظار الاعتماد.");
        string reason=input.S("reason").Trim();
        J.Need(reason.Length is >=3 and <=500,"سبب الرفض من 3 إلى 500 حرف.");
        // Maker may cancel their own pending request; checker uses Independent (AccessPolicy.SamePerson — no raw name compare).
        J.Need(AccessPolicy.SamePerson(actor,draft.S("createdBy"))
            ||AccessPolicy.Independent(actor,draft.S("createdBy"),"treasury.bank_adjust_reject",c,tx,draft,scope:AccessPolicy.ScopePayApprovals),
            "رفض التسوية للمراجع المستقل أو لمنشئ الطلب.",403);
        draft["status"]="rejected";
        draft["rejectedBy"]=actor.Identity!.Name;
        draft["rejectedAt"]=DateTimeOffset.UtcNow.ToString("O");
        draft["rejectionReason"]=reason;
        draft["allowedActions"]=new JsonArray();
        Store.Save(c,"bank_statement_adjust",draft,tx);
        Store.Audit(c,tx,"treasury.bank_adjust_rejected",adjustId,actor.Identity!.Name!);
        return draft;
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapPost("/treasury/bank-statements/upload",(JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>Upload(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Upload bank statement file lines");
        api.MapGet("/treasury/bank-statements/{id}",(string id,HttpContext ctx)=>store.Read(c=>View(c,null,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Read one bank statement");
        api.MapPost("/treasury/bank-statements/{id}/auto-match",(string id,HttpContext ctx)=>store.Write((c,tx)=>AutoMatch(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Auto-match statement lines to journal lines");
        api.MapPost("/treasury/bank-statements/{id}/adjust",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>RequestAdjust(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Deprecated generic adjust — use line actions");
        api.MapPost("/treasury/bank-statements/{id}/lines/{lineId}/actions",(string id,string lineId,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>LineAction(c,tx,ctx.User,id,lineId,input)))
            .WithTags("Banks and treasury").WithSummary("Bank statement line action: timing, payment, receipt, journal, or open original");
        api.MapPost("/treasury/bank-adjustments/{id}/approve",(string id,HttpContext ctx)=>store.Write((c,tx)=>ApproveAdjust(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Deprecated — line documents approve via manual journals");
        api.MapPost("/treasury/bank-adjustments/{id}/reject",(string id,JsonObject input,HttpContext ctx)=>store.Write((c,tx)=>RejectAdjust(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Reject a legacy pending bank recon adjustment");
    }

    public static JsonObject View(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string id)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        using var cmd=Store.Command(c,tx,$"""
            SELECT id,company_id,registration_id,bank_account_id,period_month,opening_balance,closing_balance,status,created_by
            FROM {SchemaSql.Q(c,"treasury","bank_statement")} WHERE id=@id
            """,("@id",id));
        using var r=cmd.ExecuteReader();
        J.Need(r.Read(),"كشف البنك غير موجود.",404);
        string companyId=r["company_id"].ToString()!;
        string bankAccountId=r["bank_account_id"].ToString()!;
        FinancialScopes.DemandSponsor(actor,companyId);
        var doc=new JsonObject
        {
            ["id"]=r["id"].ToString(),["companyId"]=companyId,["registrationId"]=r["registration_id"].ToString(),
            ["bankAccountId"]=bankAccountId,
            ["periodMonth"]=DateOnlyText(r["period_month"]),
            ["openingBalance"]=J.Money(Convert.ToDecimal(r["opening_balance"],CultureInfo.InvariantCulture)),
            ["closingBalance"]=J.Money(Convert.ToDecimal(r["closing_balance"],CultureInfo.InvariantCulture)),
            ["statusCode"]=Convert.ToInt32(r["status"],CultureInfo.InvariantCulture),
            ["status"]=StatusName(Convert.ToInt32(r["status"],CultureInfo.InvariantCulture)),
            ["createdBy"]=r["created_by"] is DBNull?"":r["created_by"].ToString()
        };
        r.Close();
        string chartAccountId;
        using(var cmdAcc=Store.Command(c,tx,$"SELECT account_id FROM {SchemaSql.Q(c,"treasury","bank_account")} WHERE id=@id",("@id",bankAccountId)))
            chartAccountId=cmdAcc.ExecuteScalar()?.ToString()??throw new ApiError("حساب الدليل البنكي غير موجود.",409);
        DemandTreasuryAccount(actor,companyId,chartAccountId,c,tx!);
        var lines=new JsonArray();
        int matchedCount=0,unmatchedCount=0,timingCount=0;
        decimal matchedSum=0m,timingSum=0m;
        using var lineCmd=Store.Command(c,tx,$"""
            SELECT l.id,l.line_no,l.occurred_on,l.amount,l.reference,
                   CASE WHEN EXISTS(SELECT 1 FROM {SchemaSql.Q(c,"treasury","bank_match")} m WHERE m.statement_line_id=l.id AND m.status=1) THEN 1 ELSE 0 END AS matched
            FROM {SchemaSql.Q(c,"treasury","bank_statement_line")} l WHERE l.statement_id=@id ORDER BY l.line_no
            """,("@id",id));
        using var lr=lineCmd.ExecuteReader();
        var rawLines=new List<JsonObject>();
        while(lr.Read())
            rawLines.Add(new JsonObject
            {
                ["id"]=lr["id"].ToString(),["lineNo"]=Convert.ToInt32(lr["line_no"],CultureInfo.InvariantCulture),
                ["occurredOn"]=DateOnlyText(lr["occurred_on"]),
                ["amount"]=J.Money(Convert.ToDecimal(lr["amount"],CultureInfo.InvariantCulture)),
                ["reference"]=lr["reference"] is DBNull?"":lr["reference"].ToString(),
                ["matched"]=Convert.ToInt32(lr["matched"],CultureInfo.InvariantCulture)==1
            });
        lr.Close();
        bool canRecon=AccessPolicy.Has(actor,"treasury.bank_reconcile")||AccessPolicy.Administrator(actor);
        foreach(var line in rawLines)
        {
            var disp=Store.Get(c,DispositionId(line.S("id")),DispositionKind,tx);
            string disposition=line.B("matched")?"matched":(disp?.S("disposition")??"unmatched");
            if(disposition=="cleared")disposition="unmatched";
            line["disposition"]=disposition;
            line["timing"]=disposition=="timing";
            line["documentId"]=disp?.S("documentId")??"";
            line["reconKind"]=disp?.S("reconKind")??"";
            var lineActions=new JsonArray();
            if(canRecon&&!line.B("matched")&&doc.N("statusCode") is 1 or 2)
            {
                if(disposition=="timing")lineActions.Add("clear_timing");
                else
                {
                    lineActions.Add("create_payment");lineActions.Add("create_receipt");
                    lineActions.Add("create_journal");lineActions.Add("mark_timing");lineActions.Add("open_original");
                }
            }
            line["allowedActions"]=lineActions;
            if(line.B("matched")){matchedCount++;matchedSum+=line.N("amount");}
            else if(disposition=="timing"){timingCount++;timingSum+=line.N("amount");} // grok-TRIAL6: timing only in timingCount
            else unmatchedCount++;
            lines.Add(line);
        }
        // grok-TRIAL6 HIGH: prior uncleared timing (carryToNext) from earlier statements of the same bank.
        decimal priorTiming=0m;int priorTimingCount=0;
        string thisPeriod=doc.S("periodMonth");
        foreach(var prior in Store.List(c,DispositionKind,tx))
        {
            if(prior.S("disposition")!="timing"||!prior.B("carryToNext"))continue;
            if(!string.Equals(prior.S("bankAccountId"),bankAccountId,StringComparison.OrdinalIgnoreCase))continue;
            string priorPeriod=prior.S("periodMonth");
            if(priorPeriod==""||string.CompareOrdinal(priorPeriod,thisPeriod)>=0)continue;
            // Skip if the prior line was later matched/cleared.
            if(Store.Get(c,DispositionId(prior.S("lineId")),DispositionKind,tx) is {} live&&live.S("disposition")!="timing")continue;
            priorTiming+=prior.N("amount");
            priorTimingCount++;
        }
        timingSum+=priorTiming;
        timingCount+=priorTimingCount;
        doc["lines"]=lines;
        doc["matchedCount"]=matchedCount;doc["unmatchedCount"]=unmatchedCount;doc["timingCount"]=timingCount;
        doc["bankClosingBalance"]=doc.N("closingBalance");
        // grok-TRIAL6: opening + matched + timing when residual unmatched is 0; else opening + matched only.
        doc["reconciledBalance"]=unmatchedCount==0
            ?J.Money(doc.N("openingBalance")+matchedSum+timingSum)
            :J.Money(doc.N("openingBalance")+matchedSum);
        doc["timingCarriedAmount"]=J.Money(timingSum);
        doc["priorTimingCarriedAmount"]=J.Money(priorTiming);
        doc["priorTimingCount"]=priorTimingCount;
        doc["timingCarriedForwardAmount"]=J.Money(priorTiming);
        var actions=new JsonArray();
        if(canRecon&&doc.N("statusCode") is 1 or 2)actions.Add("auto_match");
        doc["allowedActions"]=actions;
        doc["bankFeeAccountCode"]=BankFeeAccount;
        doc["bankIncomeAccountCode"]=BankIncomeAccount;
        doc["bankSuspenseAccountCode"]=BankSuspenseAccount;
        return doc;
    }

    static void DemandTreasuryAccount(ClaimsPrincipal actor,string companyId,string accountId,DbConnection c,DbTransaction tx)
    {
        object? codeObj=SchemaSql.Scalar(c,tx,$"SELECT code FROM {SchemaSql.Q(c,"fin","account")} WHERE id=@id",("@id",accountId));
        string code=codeObj?.ToString()??"";
        J.Need(code!=""&&FinancialScopes.TargetAccount(actor,companyId,code),"الحساب المالي خارج النطاق المسند للمستخدم.",403);
    }
    static string AccountIdByCode(DbConnection c,DbTransaction tx,string code)
    {
        object? id=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","account")} WHERE code=@c",("@c",code));
        J.Need(id is not null and not DBNull,$"حساب {code} غير مهيأ.",409);
        return id switch{Guid g=>SchemaSql.GuidText(g),_=>Convert.ToString(id,CultureInfo.InvariantCulture)!};
    }
    static Guid EnsureOpenPeriod(DbConnection c,DbTransaction tx,string companyId,DateOnly month)
    {
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {SchemaSql.Q(c,"fin","accounting_period")} WHERE company_id=@c AND period_month=@m",
            ("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        if(existing is not null and not DBNull)return existing switch{Guid g=>g,_=>Guid.Parse(Convert.ToString(existing,CultureInfo.InvariantCulture)!)};
        Guid id=SchemaSql.NewId();
        SchemaSql.Exec(c,tx,$"INSERT INTO {SchemaSql.Q(c,"fin","accounting_period")}(id,company_id,period_month,state,created_at) VALUES(@id,@c,@m,1,{SchemaSql.NowLiteral(c)})",
            ("@id",SchemaSql.GuidText(id)),("@c",companyId),("@m",month.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)));
        return id;
    }
    static DateOnly ParseMonth(string raw)
    {
        if(raw.Length==7)raw+="-01";
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"شهر الكشف غير صالح.");
        return new DateOnly(d.Year,d.Month,1);
    }
    static DateOnly ParseDay(string raw)
    {
        J.Need(DateOnly.TryParseExact(raw.Length>=10?raw[..10]:raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d),"تاريخ السطر غير صالح.");
        return d;
    }
    static string DateOnlyText(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateTimeOffset dto=>DateOnly.FromDateTime(dto.UtcDateTime).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        _=>(Convert.ToString(value,CultureInfo.InvariantCulture)??"").Length>=10?(Convert.ToString(value,CultureInfo.InvariantCulture)??"")[..10]:Convert.ToString(value,CultureInfo.InvariantCulture)??""
    };
    static string StatusName(int status)=>status switch{1=>"draft",2=>"submitted",3=>"approved",4=>"rejected",5=>"cancelled",_=>"unknown"};
}
