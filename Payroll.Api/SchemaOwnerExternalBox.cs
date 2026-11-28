using System.Data.Common;
namespace Payroll;

/// <summary>JC1 / M-0338: owner EXTERNAL BOX as a living tree. Cash on leaves never hits company books
/// (<c>hits_company_books=0</c>). Companion company journals (opening-fee income, custody wallet expense) post separately.</summary>
public static class SchemaOwnerExternalBox
{
    public const string Schema="treasury";
    public const string NodeTable="owner_box_node";
    public const string EntryTable="owner_box_entry";
    public const string RootCode="OWNER_EXTERNAL";
    public const string AccountOpeningFeesCode="JAHEZ_ACCOUNT_OPENING_FEES";
    public const string WalletTopupCollectionsCode="WALLET_TOPUP_COLLECTIONS";
    /// <summary>BR §22.1 uncollected path only. Collected opening fees on the owner box debit cash <see cref="CashOpeningFee"/>.</summary>
    public const string AccruedOpeningFee="112512";
    /// <summary>Target-chart cash box for collected Jahez opening fees (BR §22.1 collected = Dr cash / Cr other income).</summary>
    public const string CashOpeningFee="111104";
    public const string OtherIncomeOpeningFee="422302";
    public const string HungerWalletChargeExpense="512123";
    public const string HungerWalletCustody="112306";

    public static void Ensure(DbConnection c,DbTransaction? tx=null)
    {
        string g=SchemaSql.GuidType(c),m=SchemaSql.Decimal(c);
        string node=SchemaSql.Q(c,Schema,NodeTable);
        SchemaSql.EnsureTable(c,tx,Schema,NodeTable,$"""
            id {g} NOT NULL, parent_id {g} NULL,
            kind {SchemaSql.Varchar(c,10)} NOT NULL CHECK(kind IN ('branch','leaf')),
            code {SchemaSql.Varchar(c,60)} NOT NULL, label_ar {SchemaSql.Text(c,200)} NOT NULL,
            sort_order {SchemaSql.Int(c)} NOT NULL DEFAULT 0,
            purpose_code {SchemaSql.Varchar(c,40)} NULL,
            active_from {SchemaSql.Date(c)} NOT NULL,
            archived_at {SchemaSql.DateTime(c)} NULL,
            archive_requested_by {SchemaSql.Varchar(c,80)} NULL,
            archive_requested_at {SchemaSql.DateTime(c)} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_owner_box_node {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT UQ_owner_box_node_code UNIQUE(code),
            CONSTRAINT FK_owner_box_node_parent FOREIGN KEY(parent_id) REFERENCES {node}(id) ON DELETE NO ACTION
            """);
        SchemaSql.CreateIndexIfMissing(c,tx,Schema,NodeTable,"IX_owner_box_node_parent","parent_id");
        SchemaSql.EnsureTable(c,tx,Schema,EntryTable,$"""
            id {g} NOT NULL, node_id {g} NOT NULL,
            occurred_on {SchemaSql.Date(c)} NOT NULL,
            direction {SchemaSql.Varchar(c,10)} NOT NULL CHECK(direction IN ('in','out')),
            amount {m} NOT NULL CHECK(amount>0),
            source_kind {SchemaSql.Varchar(c,40)} NOT NULL,
            source_ref {SchemaSql.Varchar(c,120)} NULL,
            notes {SchemaSql.Text(c,500)} NULL,
            hits_company_books {SchemaSql.Tiny(c)} NOT NULL DEFAULT 0 CHECK(hits_company_books=0),
            status {SchemaSql.Varchar(c,20)} NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','posted','reversed')),
            company_id {SchemaSql.Varchar(c,40)} NULL,
            company_journal_id {SchemaSql.Varchar(c,80)} NULL,
            approved_by {SchemaSql.Varchar(c,80)} NULL,
            approved_at {SchemaSql.DateTime(c)} NULL,
            reverses_entry_id {g} NULL,
            {SchemaSql.MoneyAuditCols(c)},
            CONSTRAINT PK_owner_box_entry {SchemaSql.PrimaryKey(c)}(id),
            CONSTRAINT FK_owner_box_entry_node FOREIGN KEY(node_id) REFERENCES {node}(id) ON DELETE NO ACTION
            """);
        EnsureUpgradeColumns(c,tx);
        SchemaSql.CreateIndexIfMissing(c,tx,Schema,EntryTable,"IX_owner_box_entry_node","node_id,occurred_on");
        Seed(c,tx);
    }

    /// <summary>JC1c: EnsureTable does not ALTER existing tables — add columns introduced after 0bc5fffd.</summary>
    static void EnsureUpgradeColumns(DbConnection c,DbTransaction? tx)
    {
        string node=SchemaSql.Q(c,Schema,NodeTable),entry=SchemaSql.Q(c,Schema,EntryTable);
        if(SchemaSql.IsSql(c))
        {
            foreach(var (col,type) in new[]
            {
                ("archive_requested_by","varchar(80) NULL"),
                ("archive_requested_at","datetime2 NULL")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'{Schema}.{NodeTable}',N'{col}') IS NULL ALTER TABLE {node} ADD [{col}] {type};");
            foreach(var (col,type) in new[]
            {
                // Existing DBs (0bc5fffd) treated every row as posted — backfill after add.
                ("status","varchar(20) NULL"),
                ("company_id","varchar(40) NULL"),
                ("company_journal_id","varchar(80) NULL"),
                ("approved_by","varchar(80) NULL"),
                ("approved_at","datetime2 NULL"),
                ("reverses_entry_id",$"{SchemaSql.GuidType(c)} NULL")
            })
                SchemaSql.Exec(c,tx,$"IF COL_LENGTH(N'{Schema}.{EntryTable}',N'{col}') IS NULL ALTER TABLE {entry} ADD [{col}] {type};");
            SchemaSql.Exec(c,tx,$"UPDATE {entry} SET status='posted' WHERE status IS NULL;");
        }
        else
        {
            foreach(string col in new[]{"archive_requested_by","archive_requested_at"})
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('{Schema}_{NodeTable}') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {node} ADD COLUMN {col} TEXT NULL;");
            }
            foreach(var (col,ddl) in new[]
            {
                ("status","TEXT NULL"),
                ("company_id","TEXT NULL"),
                ("company_journal_id","TEXT NULL"),
                ("approved_by","TEXT NULL"),
                ("approved_at","TEXT NULL"),
                ("reverses_entry_id","TEXT NULL")
            })
            {
                object? has=SchemaSql.Scalar(c,tx,$"SELECT 1 FROM pragma_table_info('{Schema}_{EntryTable}') WHERE name='{col}'");
                if(has is null or DBNull)SchemaSql.Exec(c,tx,$"ALTER TABLE {entry} ADD COLUMN {col} {ddl};");
            }
            SchemaSql.Exec(c,tx,$"UPDATE {entry} SET status='posted' WHERE status IS NULL;");
        }
    }

    static void Seed(DbConnection c,DbTransaction? tx)
    {
        string node=SchemaSql.Q(c,Schema,NodeTable);
        object? root=SchemaSql.Scalar(c,tx,$"SELECT id FROM {node} WHERE code=@c",("@c",RootCode));
        string rootId;
        string today=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        if(root is null or DBNull)
        {
            rootId=SchemaSql.GuidText(SchemaSql.NewId());
            SchemaSql.Exec(c,tx,$"""
                INSERT INTO {node}(id,parent_id,kind,code,label_ar,sort_order,purpose_code,active_from,created_at)
                VALUES(@id,NULL,'branch',@code,@label,0,'owner_root',@from,@at)
                """,("@id",rootId),("@code",RootCode),("@label","صندوق المالك الخارجي"),("@from",today),("@at",DateTimeOffset.UtcNow.ToString("O")));
        }
        else rootId=SchemaSql.GuidText(root is Guid g?g:Guid.Parse(root.ToString()!));

        EnsureChild(c,tx,rootId,AccountOpeningFeesCode,"تحصيل رسوم فتح حساب جاهز",10,"jahez_account_opening_fee",today);
        EnsureChild(c,tx,rootId,WalletTopupCollectionsCode,"تحصيلات شحن محافظ (نقد المندوب → مشرف → صندوق المالك)",20,"wallet_topup_collection",today);
    }

    static void EnsureChild(DbConnection c,DbTransaction? tx,string parentId,string code,string labelAr,int order,string purpose,string from)
    {
        string node=SchemaSql.Q(c,Schema,NodeTable);
        object? existing=SchemaSql.Scalar(c,tx,$"SELECT id FROM {node} WHERE code=@c",("@c",code));
        if(existing is not null and not DBNull)return;
        SchemaSql.Exec(c,tx,$"""
            INSERT INTO {node}(id,parent_id,kind,code,label_ar,sort_order,purpose_code,active_from,created_at)
            VALUES(@id,@p,'leaf',@code,@label,@ord,@pur,@from,@at)
            """,("@id",SchemaSql.GuidText(SchemaSql.NewId())),("@p",parentId),("@code",code),("@label",labelAr),
            ("@ord",order),("@pur",purpose),("@from",from),("@at",DateTimeOffset.UtcNow.ToString("O")));
    }
}
