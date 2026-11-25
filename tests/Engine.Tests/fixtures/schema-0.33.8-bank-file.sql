-- 0.33.8 sal.bank_file* schema (SQL Server column lists for SchemaSql.EnsureTable).
-- Generated once from commit e5f9bab (release 0.33.8 SchemaBankFile EnsureTable bodies).
-- Schema statements only: no data rows, no account numbers, no identity numbers.
-- ImmutableInsteadOfUpdate columns at 0.33.8: bank_file_id,period_month,pay_run_rider_id,payable_id,company_id,registration_id,payable_period_month,payment_id,allocation_id,amount,bank_short_code,generation

-- TABLE sal.bank_file
id uniqueidentifier NOT NULL, period_month date NOT NULL,
            issuance_choice varchar(16) NOT NULL CHECK(issuance_choice IN ('per_company','combined')),
            scope_company_id uniqueidentifier NULL, scope_registration_id uniqueidentifier NULL,
            paying_company_id uniqueidentifier NOT NULL, paying_registration_id uniqueidentifier NOT NULL, paying_bank_id uniqueidentifier NOT NULL,
            status tinyint NOT NULL DEFAULT 1 CHECK(status IN (1,2,3,4)),
            file_hash varbinary(32) NULL,
            run_mode tinyint NOT NULL DEFAULT 0 CHECK(run_mode=0),
            payout_blocked bit NOT NULL DEFAULT 0 CHECK(payout_blocked IN (0,1)),
            currency_code varchar(3) NOT NULL DEFAULT 'SAR' CHECK(currency_code='SAR'),
            approved_by uniqueidentifier NULL, approved_at datetime2 NULL,
            sent_by uniqueidentifier NULL, sent_at datetime2 NULL,
            created_by nvarchar(128) NULL, created_at datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), updated_by nvarchar(128) NULL, updated_at datetime2 NULL, row_version rowversion NOT NULL, generation varchar(10) NOT NULL DEFAULT 'new' CHECK (generation IN ('new','legacy')), CONSTRAINT PK_sal_bank_file PRIMARY KEY NONCLUSTERED(id),
            CONSTRAINT UQ_sal_bank_file_month UNIQUE(id,period_month),
            CONSTRAINT CK_bank_file_month CHECK(period_month = DATEFROMPARTS(YEAR(period_month), MONTH(period_month), 1)),
            CONSTRAINT CK_bank_file_scope CHECK((issuance_choice='per_company' AND scope_company_id IS NOT NULL) OR (issuance_choice='combined' AND scope_company_id IS NULL AND scope_registration_id IS NULL)),
            CONSTRAINT CK_bank_file_sent CHECK(status<>3 OR (sent_by IS NOT NULL AND sent_at IS NOT NULL)),
            CONSTRAINT CK_bank_file_approved CHECK(status NOT IN (2,3) OR (approved_by IS NOT NULL AND approved_at IS NOT NULL AND created_by IS NOT NULL)),
            CONSTRAINT FK_bank_file_bank FOREIGN KEY(paying_bank_id,paying_company_id,paying_registration_id) REFERENCES [treasury].[bank_account](id,company_id,registration_id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_scope FOREIGN KEY(scope_company_id) REFERENCES [hr].[company](id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_file_registration FOREIGN KEY(scope_registration_id) REFERENCES [hr].[commercial_registration](id) ON DELETE NO ACTION

-- TABLE sal.bank_file_line
id uniqueidentifier NOT NULL, bank_file_id uniqueidentifier NOT NULL, period_month date NOT NULL,
            pay_run_rider_id uniqueidentifier NOT NULL, payable_id uniqueidentifier NOT NULL, company_id uniqueidentifier NOT NULL,
            registration_id uniqueidentifier NOT NULL, payable_period_month date NOT NULL,
            payment_id uniqueidentifier NOT NULL, allocation_id uniqueidentifier NOT NULL,
            amount decimal(18,2) NOT NULL CHECK(amount>0), bank_short_code varchar(8) NOT NULL,
            status tinyint NOT NULL DEFAULT 1 CHECK(status IN (1,4)),
            created_by nvarchar(128) NULL, created_at datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()), updated_by nvarchar(128) NULL, updated_at datetime2 NULL, row_version rowversion NOT NULL, generation varchar(10) NOT NULL DEFAULT 'new' CHECK (generation IN ('new','legacy')), CONSTRAINT PK_sal_bank_file_line PRIMARY KEY NONCLUSTERED(id),
            CONSTRAINT UQ_bank_file_line_month UNIQUE(id,period_month),
            CONSTRAINT UQ_bank_file_allocation UNIQUE(allocation_id),
            CONSTRAINT FK_bank_line_file FOREIGN KEY(bank_file_id,period_month) REFERENCES [sal].[bank_file](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_rider FOREIGN KEY(pay_run_rider_id,payable_period_month) REFERENCES [sal].[pay_run_rider](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payable FOREIGN KEY(payable_id,company_id,payable_period_month) REFERENCES [pay].[payable](id,company_id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_payment FOREIGN KEY(payment_id,period_month) REFERENCES [pay].[payment](id,period_month) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_allocation FOREIGN KEY(allocation_id) REFERENCES [pay].[payment_allocation](id) ON DELETE NO ACTION,
            CONSTRAINT FK_bank_line_registration FOREIGN KEY(registration_id) REFERENCES [hr].[commercial_registration](id) ON DELETE NO ACTION
