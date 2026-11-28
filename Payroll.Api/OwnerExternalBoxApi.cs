using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
namespace Payroll;

/// <summary>JC1 living-tree owner EXTERNAL BOX (M-0338). Leaf cash never hits company books.
/// Money-in/out and archive-with-balance need a second person. Opening-fee and custody wallet top-ups
/// also post companion company journals (BR §2 / §22.1; مصروف شحن محافظ هنقر).</summary>
public static class OwnerExternalBoxApi
{
    public static JsonObject Tree(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,bool includeArchived=false)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        SchemaOwnerExternalBox.Ensure(c,tx);
        var nodes=LoadNodes(c,tx,includeArchived:true); // always load archived for rollup until reversed
        var balances=LoadBalances(c,tx);
        var roots=nodes.Where(n=>n.S("parentId")=="").OrderBy(n=>n.N("sortOrder")).Select(n=>ProjectNode(n,nodes,balances,includeArchived)).ToList();
        return new()
        {
            ["hitsCompanyBooks"]=false,
            ["noteAr"]="صندوق المالك الخارجي: نقد الأوراق خارج دفاتر الشركة؛ قيود الشركة المرافقة (دخل رسوم الفتح / مصروف شحن المحافظ) تُرحَّل عند اعتماد القيد بشخص ثانٍ.",
            ["companyExpenseNoteAr"]="شحن المحفظة من العهدة = خروج من العهدة ومصروف شركة «مصروف شحن محافظ هنقر»؛ نقد المندوب بعد خروجه من العهدة يدخل ورقة التحصيل (دخول/تحصيل بدون مصروف جديد).",
            ["roots"]=new JsonArray(roots.Select(x=>(JsonNode)x).ToArray()),
            ["allowedActions"]=TreeActions(actor)
        };
    }

    public static JsonObject AddNode(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        string parentId=input.Required("parentId");
        string kind=input.Required("kind").Trim().ToLowerInvariant();
        J.Need(kind is "branch" or "leaf","النوع فرع أو ورقة فقط.");
        string code=input.Required("code").Trim().ToUpperInvariant();
        J.Need(code.Length is >=3 and <=60,"رمز العقدة من 3 إلى 60.");
        string label=input.Required("labelAr").Trim();
        J.Need(label.Length is >=2 and <=200,"الاسم العربي من حرفين إلى 200.");
        var parent=GetNode(c,tx,parentId)??throw new ApiError("العقدة الأب غير موجودة.",404);
        J.Need(parent.S("archivedAt")=="","لا تُضاف عقدة تحت فرع مؤرشف.");
        J.Need(parent.S("kind")=="branch","الأوراق لا تحمل أبناء.");
        object? clash=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM {SchemaSql.Q(c,"treasury","owner_box_node")} WHERE code=@c",("@c",code));
        J.Need(clash is null or DBNull,"رمز العقدة مستخدم.");
        string from=input.S("activeFrom");
        if(from==""||!DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            from=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        int order=(int)input.N("sortOrder",NextOrder(c,tx,parentId));
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","owner_box_node")}
            (id,parent_id,kind,code,label_ar,sort_order,purpose_code,active_from,created_by,created_at)
            VALUES(@id,@p,@k,@code,@label,@ord,@pur,@from,@by,@at)
            """,("@id",id),("@p",parentId),("@k",kind),("@code",code),("@label",label),("@ord",order),
            ("@pur",input.S("purposeCode")),("@from",from),("@by",actor.Identity!.Name!),
            ("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        Store.Audit(c,tx,"owner_box.node_grown",id,actor.Identity!.Name!);
        return Tree(c,tx,actor,includeArchived:true);
    }

    public static JsonObject ArchiveNode(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        var node=GetNode(c,tx,id)??throw new ApiError("العقدة غير موجودة.",404);
        J.Need(node.S("code")!=SchemaOwnerExternalBox.RootCode,"الجذع لا يُؤرشف.");
        J.Need(node.S("archivedAt")=="","العقدة مؤرشفة.");
        bool hasActiveChild=LoadNodes(c,tx,false).Any(n=>n.S("parentId")==id);
        J.Need(!hasActiveChild,"أرشف الأبناء أولًا.");
        // Pending outs must not make the leaf look empty (JC1d HIGH #1). Pending ins still count.
        J.Need(!HasPendingEntries(c,tx,id),"توجد قيود بانتظار الاعتماد على الورقة؛ اعتمدها أو ألغِ تجهيزها قبل الأرشفة.");
        decimal bal=NodeBalance(c,tx,id);
        if(bal==0)
        {
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"treasury","owner_box_node")}
                SET archived_at=@at, archive_requested_by=NULL, archive_requested_at=NULL, updated_by=@by, updated_at=@at WHERE id=@id
                """,("@id",id),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
            Store.Audit(c,tx,"owner_box.node_fallen",id,actor.Identity!.Name!);
            return Tree(c,tx,actor,includeArchived:true);
        }
        // Balance remains — request archive; second person confirms. Rollup keeps the balance until reversed.
        J.Need(node.S("archiveRequestedBy")=="","طلب أرشفة بانتظار الاعتماد موجود.");
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","owner_box_node")}
            SET archive_requested_by=@by, archive_requested_at=@at, updated_by=@by, updated_at=@at WHERE id=@id
            """,("@id",id),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        Store.Audit(c,tx,"owner_box.archive_requested",id,actor.Identity!.Name!);
        return Tree(c,tx,actor,includeArchived:true);
    }

    public static JsonObject ConfirmArchive(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        var node=GetNode(c,tx,id)??throw new ApiError("العقدة غير موجودة.",404);
        J.Need(node.S("archivedAt")=="","العقدة مؤرشفة.");
        string maker=node.S("archiveRequestedBy");
        J.Need(maker!="","لا يوجد طلب أرشفة بانتظار الاعتماد.");
        J.Need(!HasPendingEntries(c,tx,id),"توجد قيود بانتظار الاعتماد؛ لا تُؤرشف الورقة وفيها حركة معلّقة.");
        J.Need(AccessPolicy.Independent(actor,maker,"owner_box.archive_confirmed",c,tx,node,scope:AccessPolicy.ScopeTreasuryJahez),
            "لا يجوز اعتماد أرشفة طلبتها؛ يلزم شخص ثانٍ. التصحيح بقيد عكسي.",403);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","owner_box_node")}
            SET archived_at=@at, updated_by=@by, updated_at=@at WHERE id=@id
            """,("@id",id),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        Store.Audit(c,tx,"owner_box.node_fallen",id,actor.Identity!.Name!);
        return Tree(c,tx,actor,includeArchived:true);
    }

    public static JsonObject RestoreNode(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        var node=GetNode(c,tx,id)??throw new ApiError("العقدة غير موجودة.",404);
        J.Need(node.S("archivedAt")!="","العقدة ليست مؤرشفة.");
        if(node.S("parentId")!="")
        {
            var parent=GetNode(c,tx,node.S("parentId"));
            J.Need(parent!=null&&parent.S("archivedAt")=="","الأب مؤرشف؛ استعده أولًا.");
        }
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","owner_box_node")}
            SET archived_at=NULL, archive_requested_by=NULL, archive_requested_at=NULL, updated_by=@by, updated_at=@at WHERE id=@id
            """,("@id",id),("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        Store.Audit(c,tx,"owner_box.node_restored",id,actor.Identity!.Name!);
        return Tree(c,tx,actor,includeArchived:true);
    }

    /// <summary>Prepare a money movement (pending). Second person confirms via ConfirmEntry.</summary>
    public static JsonObject PostEntry(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject input)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        string nodeId=input.Required("nodeId");
        var node=GetNode(c,tx,nodeId)??throw new ApiError("العقدة غير موجودة.",404);
        J.Need(node.S("kind")=="leaf","القيود على الأوراق فقط.");
        J.Need(node.S("archivedAt")=="","الورقة مؤرشفة.");
        string direction=input.Required("direction").Trim().ToLowerInvariant();
        J.Need(direction is "in" or "out","الاتجاه دخول أو خروج.");
        decimal amount=J.Money(PayValues.Number(input,"amount"));
        J.Need(amount>0,"المبلغ أكبر من صفر.");
        string occurred=input.Required("occurredOn");
        J.Need(DateOnly.TryParseExact(occurred,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"تاريخ القيد غير صالح.");
        string sourceKind=input.Required("sourceKind").Trim();
        J.Need(sourceKind.Length is >=2 and <=40,"مصدر القيد من حرفين إلى 40.");
        ValidateSourceDirection(node,sourceKind,direction);
        string companyId=input.S("companyId");
        if(NeedsCompanyJournal(node,sourceKind,direction))
        {
            J.Need(companyId!="","معرّف الشركة مطلوب لقيود الشركة المرافقة.");
            FinancialCore.ResolveSponsor(companyId);
        }
        // JC1e #2 / JC1f #2: every out (incl. reversal outs) must fit posted cash minus other pending outs.
        if(direction=="out")
            J.Need(AvailableCashForOut(c,tx,nodeId)>=amount,"المبلغ يتجاوز الرصيد المتاح بعد القيود المعلّقة للخروج.");
        string id=SchemaSql.GuidText(SchemaSql.NewId());
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {SchemaSql.Q(c,"treasury","owner_box_entry")}
            (id,node_id,occurred_on,direction,amount,source_kind,source_ref,notes,hits_company_books,status,company_id,created_by,created_at)
            VALUES(@id,@n,@on,@dir,@amt,@sk,@sr,@notes,0,'pending',@cid,@by,@at)
            """,("@id",id),("@n",nodeId),("@on",occurred),("@dir",direction),("@amt",amount),
            ("@sk",sourceKind),("@sr",input.S("sourceRef")),("@notes",input.S("notes")),
            ("@cid",(object?)NullIfEmpty(companyId)??DBNull.Value),
            ("@by",actor.Identity!.Name!),("@at",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)));
        Store.Audit(c,tx,"owner_box.entry_prepared",id,actor.Identity!.Name!);
        return new()
        {
            ["id"]=id,["nodeId"]=nodeId,["occurredOn"]=occurred,["direction"]=direction,["amount"]=amount,
            ["sourceKind"]=sourceKind,["sourceRef"]=input.S("sourceRef"),["notes"]=input.S("notes"),
            ["hitsCompanyBooks"]=false,["status"]="pending",["preparedBy"]=actor.Identity!.Name!,
            ["companyId"]=companyId,
            ["allowedActions"]=new JsonArray("confirm_entry","cancel_entry","view_tree")
        };
    }

    public static JsonObject ConfirmEntry(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);SchemaFinJournal.Ensure(c,tx);
        var entry=GetEntry(c,tx,id)??throw new ApiError("القيد غير موجود.",404);
        J.Need(entry.S("status")=="pending","القيد ليس بانتظار الاعتماد.");
        string maker=entry.S("createdBy");
        J.Need(AccessPolicy.Independent(actor,maker,"owner_box.entry_confirmed",c,tx,entry,scope:AccessPolicy.ScopeTreasuryJahez),
            "لا يجوز اعتماد قيد جهّزته؛ يلزم شخص ثانٍ (خروج المال يحتاج شخصين).",403);
        var node=GetNode(c,tx,entry.S("nodeId"))??throw new ApiError("العقدة غير موجودة.",404);
        J.Need(node.S("archivedAt")=="","لا يُعتمد قيد على ورقة مؤرشفة.");
        // Exclude this pending out so confirm of a fitting out still succeeds (JC1e #2 / JC1f #2 incl. reversal outs).
        if(entry.S("direction")=="out")
            J.Need(AvailableCashForOut(c,tx,entry.S("nodeId"),excludeEntryId:entry.S("id"))>=entry.N("amount"),
                "المبلغ يتجاوز الرصيد المتاح بعد القيود المعلّقة للخروج.");
        string journalId="";
        if(string.Equals(entry.S("sourceKind"),"reversal",StringComparison.OrdinalIgnoreCase))
            journalId=PostReversalCompanionJournal(c,tx,actor,entry);
        else if(NeedsCompanyJournal(node,entry.S("sourceKind"),entry.S("direction")))
            journalId=PostCompanionJournal(c,tx,actor,node,entry);
        string at=DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","owner_box_entry")}
            SET status='posted', approved_by=@by, approved_at=@at, company_journal_id=@j, updated_by=@by, updated_at=@at
            WHERE id=@id
            """,("@id",id),("@by",actor.Identity!.Name!),("@at",at),("@j",(object?)NullIfEmpty(journalId)??DBNull.Value));
        if(string.Equals(entry.S("sourceKind"),"reversal",StringComparison.OrdinalIgnoreCase)&&entry.S("sourceRef")!="")
        {
            SchemaSql.Exec(c,tx,$"""
                UPDATE {SchemaSql.Q(c,"treasury","owner_box_entry")}
                SET status='reversed', updated_by=@by, updated_at=@at WHERE id=@oid AND status='posted'
                """,("@oid",entry.S("sourceRef")),("@by",actor.Identity!.Name!),("@at",at));
        }
        Store.Audit(c,tx,"owner_box.entry_posted",id,actor.Identity!.Name!);
        entry["status"]="posted";entry["approvedBy"]=actor.Identity!.Name!;entry["approvedAt"]=at;
        entry["companyJournalId"]=journalId;entry["hitsCompanyBooks"]=false;
        entry["allowedActions"]=new JsonArray("view_tree","reverse_entry");
        return entry;
    }

    /// <summary>JC1f #2: drop an unposted pending row (never posted → delete draft; posted corrections stay reverse-only).</summary>
    public static JsonObject CancelEntry(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        var entry=GetEntry(c,tx,id)??throw new ApiError("القيد غير موجود.",404);
        J.Need(entry.S("status")=="pending","إلغاء تجهيز قيد معلّق فقط؛ المعتمد يُعكس بقيد عكسي.");
        SchemaSql.Exec(c,tx,$"""
            DELETE FROM {SchemaSql.Q(c,"treasury","owner_box_entry")} WHERE id=@id AND status='pending'
            """,("@id",id));
        Store.Audit(c,tx,"owner_box.entry_cancelled",id,actor.Identity!.Name!);
        return new()
        {
            ["id"]=id,["nodeId"]=entry.S("nodeId"),["occurredOn"]=entry.S("occurredOn"),
            ["direction"]=entry.S("direction"),["amount"]=entry.N("amount"),
            ["sourceKind"]=entry.S("sourceKind"),["sourceRef"]=entry.S("sourceRef"),["notes"]=entry.S("notes"),
            ["hitsCompanyBooks"]=false,["status"]="cancelled",["companyId"]=entry.S("companyId"),
            ["allowedActions"]=new JsonArray("view_tree","post_entry")
        };
    }

    /// <summary>Correction = reversing entry (never delete). Prepared then confirmed by a second person.
    /// Companion company journals are reversed when the original had one (BR §19).</summary>
    public static JsonObject ReverseEntry(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,string id,JsonObject? input=null)
    {
        DemandManage(actor);
        SchemaOwnerExternalBox.Ensure(c,tx);
        var original=GetEntry(c,tx,id)??throw new ApiError("القيد غير موجود.",404);
        J.Need(original.S("status")=="posted","عكس قيد معتمد فقط.");
        object? already=SchemaSql.Scalar(c,tx,$"""
            SELECT 1 FROM {SchemaSql.Q(c,"treasury","owner_box_entry")}
            WHERE reverses_entry_id=@id OR (source_kind='reversal' AND source_ref=@id AND status IN ('pending','posted'))
            """,("@id",id));
        J.Need(already is null or DBNull,"القيد معكوس بالفعل؛ ممنوع عكس ثانٍ.");
        string reverseDir=original.S("direction")=="in"?"out":"in";
        var prepared=PostEntry(c,tx,actor,new()
        {
            ["nodeId"]=original.S("nodeId"),["occurredOn"]=input?.S("occurredOn",original.S("occurredOn"))??original.S("occurredOn"),
            ["direction"]=reverseDir,["amount"]=original.N("amount"),
            ["sourceKind"]="reversal",["sourceRef"]=original.S("id"),
            ["notes"]=input?.S("notes","قيد عكسي")??"قيد عكسي",
            ["companyId"]=original.S("companyId")
        });
        SchemaSql.Exec(c,tx,$"""
            UPDATE {SchemaSql.Q(c,"treasury","owner_box_entry")} SET reverses_entry_id=@r WHERE id=@id
            """,("@id",prepared.S("id")),("@r",id));
        prepared["reversesEntryId"]=id;
        return prepared;
    }

    public static JsonObject ListEntries(DbConnection c,DbTransaction? tx,ClaimsPrincipal actor,string? nodeId,string? from,string? to)
    {
        AccessPolicy.Demand(actor,"treasury.view");
        SchemaOwnerExternalBox.Ensure(c,tx);
        DateOnly? fromD=ParseDay(from),toD=ParseDay(to);
        var rows=new JsonArray();
        using var cmd=c.CreateCommand();
        if(tx!=null)cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,node_id,occurred_on,direction,amount,source_kind,source_ref,notes,created_by,created_at,status,company_id,company_journal_id,approved_by
            FROM {SchemaSql.Q(c,"treasury","owner_box_entry")}
            ORDER BY occurred_on DESC, created_at DESC
            """;
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string nId=SchemaSql.GuidText(r.GetValue(1) is Guid g?g:Guid.Parse(r.GetValue(1).ToString()!));
            if(!string.IsNullOrWhiteSpace(nodeId)&&!string.Equals(nId,nodeId,StringComparison.OrdinalIgnoreCase))continue;
            string day=ReadDay(r.GetValue(2));
            if(fromD is {} f&&DateOnly.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)&&d<f)continue;
            if(toD is {} t&&DateOnly.TryParseExact(day,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d2)&&d2>t)continue;
            rows.Add(new JsonObject
            {
                ["id"]=SchemaSql.GuidText(r.GetValue(0) is Guid gi?gi:Guid.Parse(r.GetValue(0).ToString()!)),
                ["nodeId"]=nId,["occurredOn"]=day,["direction"]=r.GetString(3),
                ["amount"]=J.Money(Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture)),
                ["sourceKind"]=r.IsDBNull(5)?"":r.GetString(5),
                ["sourceRef"]=r.IsDBNull(6)?"":r.GetString(6),
                ["notes"]=r.IsDBNull(7)?"":r.GetString(7),
                ["hitsCompanyBooks"]=false,
                ["createdBy"]=r.IsDBNull(8)?"":r.GetString(8),
                ["status"]=r.FieldCount>10&&!r.IsDBNull(10)?r.GetString(10):"posted",
                ["companyId"]=r.FieldCount>11&&!r.IsDBNull(11)?r.GetString(11):"",
                ["companyJournalId"]=r.FieldCount>12&&!r.IsDBNull(12)?r.GetString(12):"",
                ["approvedBy"]=r.FieldCount>13&&!r.IsDBNull(13)?r.GetString(13):""
            });
        }
        return new()
        {
            ["hitsCompanyBooks"]=false,
            ["items"]=rows,
            ["total"]=rows.Count,
            ["allowedActions"]=TreeActions(actor)
        };
    }

    public static void Map(RouteGroupBuilder api,Store store)
    {
        api.MapGet("/finance/owner-external-box/tree",(bool? includeArchived,HttpContext ctx)=>
            store.Read(c=>Tree(c,null,ctx.User,includeArchived==true)))
            .WithTags("Banks and treasury").WithSummary("Owner external box living tree (outside company books)");
        api.MapGet("/finance/owner-external-box/entries",(string? nodeId,string? from,string? to,HttpContext ctx)=>
            store.Read(c=>ListEntries(c,null,ctx.User,nodeId,from,to)))
            .WithTags("Banks and treasury").WithSummary("Owner external box entries (never company books)");
        api.MapPost("/finance/owner-external-box/nodes",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>AddNode(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Grow owner-box branch or leaf");
        api.MapPost("/finance/owner-external-box/nodes/{id}/archive",(string id,HttpContext ctx)=>
            store.Write((c,tx)=>ArchiveNode(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Fall (archive) owner-box node or request archive when balance remains");
        api.MapPost("/finance/owner-external-box/nodes/{id}/confirm-archive",(string id,HttpContext ctx)=>
            store.Write((c,tx)=>ConfirmArchive(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Second person confirms archive of a leaf with balance");
        api.MapPost("/finance/owner-external-box/nodes/{id}/restore",(string id,HttpContext ctx)=>
            store.Write((c,tx)=>RestoreNode(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Restore archived owner-box node");
        api.MapPost("/finance/owner-external-box/entries",(JsonObject input,HttpContext ctx)=>
            store.Write((c,tx)=>PostEntry(c,tx,ctx.User,input)))
            .WithTags("Banks and treasury").WithSummary("Prepare owner-box entry (pending; second person confirms)");
        api.MapPost("/finance/owner-external-box/entries/{id}/confirm",(string id,HttpContext ctx)=>
            store.Write((c,tx)=>ConfirmEntry(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Second person posts owner-box entry (+ companion company journals when required)");
        api.MapPost("/finance/owner-external-box/entries/{id}/reverse",(string id,JsonObject? input,HttpContext ctx)=>
            store.Write((c,tx)=>ReverseEntry(c,tx,ctx.User,id,input)))
            .WithTags("Banks and treasury").WithSummary("Prepare reversing correction for a posted owner-box entry");
        api.MapPost("/finance/owner-external-box/entries/{id}/cancel",(string id,HttpContext ctx)=>
            store.Write((c,tx)=>CancelEntry(c,tx,ctx.User,id)))
            .WithTags("Banks and treasury").WithSummary("Cancel (drop) a pending owner-box entry that was never posted");
    }

    /// <summary>Opening-fee income on IN with an opening-fee source only (not bare collection).
    /// Custody wallet expense when cash LEAVES custody (OUT + custody source on the wallet leaf). JC1e N1/#1/#3.</summary>
    static bool NeedsCompanyJournal(JsonObject node,string sourceKind,string direction)
    {
        if(string.Equals(sourceKind,"reversal",StringComparison.OrdinalIgnoreCase))return false; // handled in ConfirmEntry via PostReversalCompanionJournal
        string code=node.S("code"),purpose=node.S("purposeCode");
        bool openingFeeLeaf=code==SchemaOwnerExternalBox.AccountOpeningFeesCode||purpose=="jahez_account_opening_fee";
        bool walletLeaf=code==SchemaOwnerExternalBox.WalletTopupCollectionsCode||purpose=="wallet_topup_collection";
        bool openingFeeSource=sourceKind is "jahez_account_opening_fee" or "account_opening_fee";
        bool custodySource=sourceKind is "custody_wallet_topup" or "wallet_topup_from_custody" or "hunger_wallet_custody";
        if(direction=="in"&&openingFeeLeaf&&openingFeeSource)return true;
        if(direction=="out"&&walletLeaf&&custodySource)return true;
        return false;
    }

    static void ValidateSourceDirection(JsonObject node,string sourceKind,string direction)
    {
        if(string.Equals(sourceKind,"reversal",StringComparison.OrdinalIgnoreCase))return;
        string code=node.S("code"),purpose=node.S("purposeCode");
        bool openingFeeLeaf=code==SchemaOwnerExternalBox.AccountOpeningFeesCode||purpose=="jahez_account_opening_fee";
        bool walletLeaf=code==SchemaOwnerExternalBox.WalletTopupCollectionsCode||purpose=="wallet_topup_collection";
        bool openingFeeSource=sourceKind is "jahez_account_opening_fee" or "account_opening_fee";
        bool custodySource=sourceKind is "custody_wallet_topup" or "wallet_topup_from_custody" or "hunger_wallet_custody";
        if(openingFeeSource)
        {
            J.Need(openingFeeLeaf,"مصدر رسوم الفتح على ورقة رسوم فتح الحساب فقط.");
            J.Need(direction=="in","رسوم فتح الحساب تُسجَّل كدخول نقدي.");
        }
        if(custodySource)
        {
            J.Need(walletLeaf,"مصدر شحن العهدة على ورقة تحصيلات الشحن فقط.");
            // Expense belongs when cash leaves custody — never on rider-cash arrival (JC1e HIGH #1).
            J.Need(direction=="out","شحن المحفظة من العهدة يُسجَّل كخروج (مصروف شركة عند مغادرة العهدة).");
        }
    }

    /// <summary>Posted/reversed in − posted/reversed out − pending outs (optional exclude).
    /// JC1f HIGH #1: keep counting <c>reversed</c> originals so a posted reversal does not double the cash.</summary>
    static decimal AvailableCashForOut(DbConnection c,DbTransaction? tx,string nodeId,string? excludeEntryId=null)
    {
        decimal available=0;
        using var cmd=c.CreateCommand();
        if(tx!=null)cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,direction,amount,status FROM {SchemaSql.Q(c,"treasury","owner_box_entry")}
            WHERE node_id=@n
            """;
        var p=cmd.CreateParameter();p.ParameterName="@n";p.Value=nodeId;cmd.Parameters.Add(p);
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string id=GuidText(r.GetValue(0));
            if(!string.IsNullOrWhiteSpace(excludeEntryId)&&string.Equals(id,excludeEntryId,StringComparison.OrdinalIgnoreCase))continue;
            string status=r.IsDBNull(3)?"posted":r.GetString(3);
            // reversed originals still count; their posted reversal offsets them (do not skip both sides).
            bool durable=string.Equals(status,"posted",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(status,"reversed",StringComparison.OrdinalIgnoreCase);
            bool pending=string.Equals(status,"pending",StringComparison.OrdinalIgnoreCase);
            if(!durable&&!pending)continue;
            string dir=r.GetString(1);
            decimal amt=J.Money(Convert.ToDecimal(r.GetValue(2),CultureInfo.InvariantCulture));
            bool isOut=string.Equals(dir,"out",StringComparison.OrdinalIgnoreCase);
            if(durable&&!isOut)available+=amt;
            else if(durable&&isOut)available-=amt;
            else if(pending&&isOut)available-=amt;
        }
        return J.Money(available);
    }

    static string PostCompanionJournal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject node,JsonObject entry)
    {
        string company=entry.S("companyId");
        var sponsor=FinancialCore.ResolveSponsor(company);
        decimal amount=entry.N("amount");
        string period=entry.S("occurredOn").Length>=7?entry.S("occurredOn")[..7]:J.Period(DateTime.UtcNow.ToString("yyyy-MM"));
        string actorName=actor.Identity!.Name!;
        string debit,credit,label,txType;
        if(node.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode||node.S("purposeCode")=="jahez_account_opening_fee"
           ||entry.S("sourceKind") is "jahez_account_opening_fee" or "account_opening_fee")
        {
            // BR §22.1 collected: Dr cash / Cr other-income. Leaf cash stays outside company books (hits_company_books=0);
            // companion journal uses صندوق رسوم فتح حسابات جاهز — never accrued 112512 (would never clear).
            debit=SchemaOwnerExternalBox.CashOpeningFee;credit=SchemaOwnerExternalBox.OtherIncomeOpeningFee;
            label="دخل آخر - رسوم فتح حساب (صندوق المالك)";txType="owner_box_opening_fee_income";
        }
        else
        {
            // Custody-funded wallet top-up: company expense when cash leaves custody.
            debit=SchemaOwnerExternalBox.HungerWalletChargeExpense;credit=SchemaOwnerExternalBox.HungerWalletCustody;
            label="مصروف شحن محافظ هنقر";txType="owner_box_custody_wallet_expense";
        }
        var lines=new JsonArray(
            FinancialCore.CreateJournalLine(debit,sponsor.Id,amount,0,label,targetChart:true,payChart:true),
            FinancialCore.CreateJournalLine(credit,sponsor.Id,0,amount,label,targetChart:true,payChart:true));
        var journal=FinancialCore.PostJournal(c,tx,txType,label,period,"owner-box:"+entry.S("id"),lines,actorName,"","",sponsor.Id,sponsor.Id,targetChart:true,payChart:true,journalDate:entry.S("occurredOn"));
        return journal.S("id");
    }

    /// <summary>When confirming a reversal out, reverse the original companion company journal if any (BR §19).</summary>
    static string PostReversalCompanionJournal(DbConnection c,DbTransaction tx,ClaimsPrincipal actor,JsonObject reversalEntry)
    {
        string originalId=reversalEntry.S("sourceRef");
        if(originalId=="")return "";
        var original=GetEntry(c,tx,originalId);
        string origJournalId=original?.S("companyJournalId")??"";
        if(origJournalId=="")return "";
        var origJournal=Store.Get(c,origJournalId,"journal",tx)??throw new ApiError("قيد الشركة الأصلي غير موجود.");
        J.Need(!Store.List(c,"journal",tx).Any(j=>j.S("reversesJournalId")==origJournalId),"قيد الشركة معكوس بالفعل.");
        string company=reversalEntry.S("companyId",original!.S("companyId"));
        var sponsor=FinancialCore.ResolveSponsor(company);
        decimal amount=reversalEntry.N("amount");
        string period=reversalEntry.S("occurredOn").Length>=7?reversalEntry.S("occurredOn")[..7]:J.Period(DateTime.UtcNow.ToString("yyyy-MM"));
        var revLines=new JsonArray();
        foreach(var old in origJournal["lines"]!.AsArray().OfType<JsonObject>())
        {
            revLines.Add(FinancialCore.CreateJournalLine(old.S("accountCode"),sponsor.Id,old.N("credit"),old.N("debit"),
                "عكس: "+old.S("note",origJournal.S("description")),targetChart:true,payChart:true));
        }
        if(revLines.Count<2)
        {
            // Fallback if lines missing account shape: swap opening-fee or custody pair by source of original.
            var node=GetNode(c,tx,original.S("nodeId"));
            if(node!=null&&(node.S("code")==SchemaOwnerExternalBox.AccountOpeningFeesCode||original.S("sourceKind") is "jahez_account_opening_fee" or "account_opening_fee"))
            {
                revLines=new JsonArray(
                    FinancialCore.CreateJournalLine(SchemaOwnerExternalBox.OtherIncomeOpeningFee,sponsor.Id,amount,0,"عكس دخل رسوم فتح حساب",targetChart:true,payChart:true),
                    FinancialCore.CreateJournalLine(SchemaOwnerExternalBox.CashOpeningFee,sponsor.Id,0,amount,"عكس دخل رسوم فتح حساب",targetChart:true,payChart:true));
            }
            else
            {
                revLines=new JsonArray(
                    FinancialCore.CreateJournalLine(SchemaOwnerExternalBox.HungerWalletCustody,sponsor.Id,amount,0,"عكس مصروف شحن محافظ",targetChart:true,payChart:true),
                    FinancialCore.CreateJournalLine(SchemaOwnerExternalBox.HungerWalletChargeExpense,sponsor.Id,0,amount,"عكس مصروف شحن محافظ",targetChart:true,payChart:true));
            }
        }
        var journal=FinancialCore.PostJournal(c,tx,"owner_box_reversal","عكس قيد صندوق المالك المرافِق",period,"owner-box-rev:"+reversalEntry.S("id"),
            revLines,actor.Identity!.Name!,"","",sponsor.Id,sponsor.Id,targetChart:true,payChart:true,
            journalDate:reversalEntry.S("occurredOn"),reversesJournalId:origJournalId);
        return journal.S("id");
    }

    static void DemandManage(ClaimsPrincipal actor)=>
        J.Need(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.jahez.manage")||AccessPolicy.Has(actor,"treasury.transfer"),
            "صلاحية إدارة صندوق المالك مطلوبة.",403);

    static JsonArray TreeActions(ClaimsPrincipal actor)
    {
        var a=new JsonArray("refresh");
        if(AccessPolicy.Administrator(actor)||AccessPolicy.Has(actor,"treasury.jahez.manage")||AccessPolicy.Has(actor,"treasury.transfer"))
        {
            a.Add("grow");a.Add("archive");a.Add("confirm_archive");a.Add("restore");
            a.Add("post_entry");a.Add("confirm_entry");a.Add("cancel_entry");a.Add("reverse_entry");
        }
        return a;
    }

    static List<JsonObject> LoadNodes(DbConnection c,DbTransaction? tx,bool includeArchived)
    {
        var list=new List<JsonObject>();
        using var cmd=c.CreateCommand();
        if(tx!=null)cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,parent_id,kind,code,label_ar,sort_order,purpose_code,active_from,archived_at,archive_requested_by,archive_requested_at
            FROM {SchemaSql.Q(c,"treasury","owner_box_node")}
            ORDER BY sort_order, code
            """;
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            string archived=r.IsDBNull(8)?"":r.GetValue(8).ToString()??"";
            if(!includeArchived&&archived!="")continue;
            list.Add(new()
            {
                ["id"]=GuidText(r.GetValue(0)),
                ["parentId"]=r.IsDBNull(1)?"":GuidText(r.GetValue(1)),
                ["kind"]=r.GetString(2),["code"]=r.GetString(3),["labelAr"]=r.GetString(4),
                ["sortOrder"]=Convert.ToInt32(r.GetValue(5),CultureInfo.InvariantCulture),
                ["purposeCode"]=r.IsDBNull(6)?"":r.GetString(6),
                ["activeFrom"]=ReadDay(r.GetValue(7)),
                ["archivedAt"]=archived,
                ["archiveRequestedBy"]=r.FieldCount>9&&!r.IsDBNull(9)?r.GetString(9):"",
                ["archiveRequestedAt"]=r.FieldCount>10&&!r.IsDBNull(10)?r.GetValue(10).ToString()??"":""
            });
        }
        return list;
    }

    static Dictionary<string,decimal> LoadBalances(DbConnection c,DbTransaction? tx)
    {
        var map=new Dictionary<string,decimal>(StringComparer.OrdinalIgnoreCase);
        using var cmd=c.CreateCommand();
        if(tx!=null)cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT node_id,direction,amount,status FROM {SchemaSql.Q(c,"treasury","owner_box_entry")}
            """;
        using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            // Pending IN counts so prepared money-in cannot archive alone (JC1c).
            // Pending OUT must NOT reduce balance — otherwise a prepared out (or reversal) equal to
            // the posted balance makes the leaf look empty and one person archives (JC1d HIGH #1).
            // JC1f HIGH #1: count reversed originals; posted reversals offset them (no double cash).
            string status=r.FieldCount>3&&!r.IsDBNull(3)?r.GetString(3):"posted";
            bool durable=string.Equals(status,"posted",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(status,"reversed",StringComparison.OrdinalIgnoreCase);
            bool pending=string.Equals(status,"pending",StringComparison.OrdinalIgnoreCase);
            if(!durable&&!pending)continue;
            string dir=r.GetString(1);
            bool isOut=string.Equals(dir,"out",StringComparison.OrdinalIgnoreCase);
            if(pending&&isOut)continue;
            string id=GuidText(r.GetValue(0));
            decimal amt=J.Money(Convert.ToDecimal(r.GetValue(2),CultureInfo.InvariantCulture));
            if(isOut)amt=-amt;
            map[id]=J.Money(map.GetValueOrDefault(id)+amt);
        }
        return map;
    }

    static bool HasPendingEntries(DbConnection c,DbTransaction? tx,string nodeId)
    {
        object? hit=SchemaSql.Scalar(c,tx,$"""
            SELECT 1 FROM {SchemaSql.Q(c,"treasury","owner_box_entry")}
            WHERE node_id=@n AND status='pending'
            """,("@n",nodeId));
        return hit is not null and not DBNull;
    }

    static decimal NodeBalance(DbConnection c,DbTransaction? tx,string nodeId)=>
        LoadBalances(c,tx).GetValueOrDefault(nodeId);

    static JsonObject ProjectNode(JsonObject node,List<JsonObject> all,Dictionary<string,decimal> balances,bool includeArchived)
    {
        var allChildren=all.Where(n=>n.S("parentId")==node.S("id")).OrderBy(n=>n.N("sortOrder")).ToList();
        var projectedAll=allChildren.Select(n=>ProjectNode(n,all,balances,includeArchived)).ToList();
        // Archived children stay in rollup until their balance is reversed (BR §19).
        decimal own=balances.GetValueOrDefault(node.S("id"));
        decimal rollup=J.Money(own+projectedAll.Sum(ch=>ch.N("balance")));
        var visible=includeArchived?projectedAll:projectedAll.Where(ch=>ch.S("archivedAt")=="").ToList();
        var actions=new JsonArray("select");
        if(node.S("archivedAt")=="")
        {
            if(node.S("kind")=="branch")actions.Add("grow");
            if(node.S("code")!=SchemaOwnerExternalBox.RootCode)
            {
                if(node.S("archiveRequestedBy")=="")actions.Add("archive");
                else actions.Add("confirm_archive");
            }
            if(node.S("kind")=="leaf")actions.Add("post_entry");
        }
        else actions.Add("restore");
        return new()
        {
            ["id"]=node.S("id"),["parentId"]=node.S("parentId"),["kind"]=node.S("kind"),
            ["code"]=node.S("code"),["labelAr"]=node.S("labelAr"),["sortOrder"]=node.N("sortOrder"),
            ["purposeCode"]=node.S("purposeCode"),["activeFrom"]=node.S("activeFrom"),
            ["archivedAt"]=node.S("archivedAt"),["archiveRequestedBy"]=node.S("archiveRequestedBy"),
            ["balance"]=rollup,["ownBalance"]=own,
            ["hitsCompanyBooks"]=false,
            ["children"]=new JsonArray(visible.Select(x=>(JsonNode)x).ToArray()),
            ["allowedActions"]=actions
        };
    }

    static JsonObject? GetNode(DbConnection c,DbTransaction? tx,string id)=>
        LoadNodes(c,tx,true).FirstOrDefault(n=>string.Equals(n.S("id"),id,StringComparison.OrdinalIgnoreCase));

    static JsonObject? GetEntry(DbConnection c,DbTransaction? tx,string id)
    {
        using var cmd=c.CreateCommand();
        if(tx!=null)cmd.Transaction=tx;
        cmd.CommandText=$"""
            SELECT id,node_id,occurred_on,direction,amount,source_kind,source_ref,notes,created_by,status,company_id,company_journal_id,approved_by
            FROM {SchemaSql.Q(c,"treasury","owner_box_entry")} WHERE id=@id
            """;
        var p=cmd.CreateParameter();p.ParameterName="@id";p.Value=id;cmd.Parameters.Add(p);
        using var r=cmd.ExecuteReader();
        if(!r.Read())return null;
        return new()
        {
            ["id"]=GuidText(r.GetValue(0)),["nodeId"]=GuidText(r.GetValue(1)),
            ["occurredOn"]=ReadDay(r.GetValue(2)),["direction"]=r.GetString(3),
            ["amount"]=J.Money(Convert.ToDecimal(r.GetValue(4),CultureInfo.InvariantCulture)),
            ["sourceKind"]=r.IsDBNull(5)?"":r.GetString(5),
            ["sourceRef"]=r.IsDBNull(6)?"":r.GetString(6),
            ["notes"]=r.IsDBNull(7)?"":r.GetString(7),
            ["createdBy"]=r.IsDBNull(8)?"":r.GetString(8),
            ["status"]=r.IsDBNull(9)?"":r.GetString(9),
            ["companyId"]=r.IsDBNull(10)?"":r.GetString(10),
            ["companyJournalId"]=r.IsDBNull(11)?"":r.GetString(11),
            ["approvedBy"]=r.IsDBNull(12)?"":r.GetString(12),
            ["hitsCompanyBooks"]=false
        };
    }

    static int NextOrder(DbConnection c,DbTransaction? tx,string parentId)
    {
        object? max=SchemaSql.Scalar(c,tx,$"SELECT MAX(sort_order) FROM {SchemaSql.Q(c,"treasury","owner_box_node")} WHERE parent_id=@p",("@p",parentId));
        if(max is null or DBNull)return 10;
        return Convert.ToInt32(max,CultureInfo.InvariantCulture)+10;
    }

    static object? NullIfEmpty(string? s)=>string.IsNullOrWhiteSpace(s)?null:s;
    static DateOnly? ParseDay(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        return DateOnly.TryParseExact(raw,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    }
    static string GuidText(object value)=>value is Guid g?SchemaSql.GuidText(g):SchemaSql.GuidText(Guid.Parse(value.ToString()!));
    static string ReadDay(object value)=>value switch
    {
        DateTime dt=>DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        _=>(value.ToString()??"")[..Math.Min(10,(value.ToString()??"").Length)]
    };
}
