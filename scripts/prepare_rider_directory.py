"""Reconcile local personal data; outputs stay in ignored data/. No database writes."""
from pathlib import Path
import argparse, collections, hashlib, json, re, shutil, unicodedata
import openpyxl

def valid_name(value):
    value = re.sub(r'\s+', ' ', unicodedata.normalize('NFC', value)).strip()
    letter = lambda c: c.isalpha()
    mark = lambda c: unicodedata.category(c) in {'Mn', 'Mc'}
    return value if 2 <= len(value) <= 200 and sum(letter(c) for c in value) >= 2 and all(letter(c) or mark(c) or c in " -.'" for c in value) else None

def identity(value):
    value = str(value).removesuffix('.0').strip()
    return value if re.fullmatch(r'[12][0-9]{9}', value) else None

def workbook(path, headers, rows):
    book = openpyxl.Workbook(); sheet = book.active; sheet.title = 'البيانات'
    sheet.append(headers)
    for row in rows:
        sheet.append(row)
    for row in sheet:
        for cell in row:
            if isinstance(cell.value, str): cell.data_type = 's'; cell.number_format = '@'
    sheet.freeze_panes = 'A2'; sheet.auto_filter.ref = sheet.dimensions
    sheet.sheet_view.rightToLeft = True
    book.save(path)

def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data')
    args = parser.parse_args(); ws = args.workspace.resolve(); data = args.output.resolve()
    baseline = data / 'riders-baseline.json'
    backup = data.parent / 'local-backups/antigravity-before-directory-fix'
    backup.mkdir(parents=True, exist_ok=True)
    for path in data.glob('*'):
        if path.is_file() and not (backup / path.name).exists(): shutil.copyfile(path, backup / path.name)
    # Always rebuild from the original input, keeping an auditable source name.
    rows = json.loads((backup / 'riders-baseline.json').read_text())
    candidates = collections.defaultdict(list)
    provenance = []
    def load(path, sheets, id_col, name_cols, first=2):
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        provenance.append({'path':str(path.relative_to(ws)), 'sha256':digest})
        book = openpyxl.load_workbook(path, read_only=True, data_only=True)
        for sheet in book:
            if sheets and sheet.title not in sheets: continue
            for num, values in enumerate(sheet.iter_rows(values_only=True), 1):
                if num < first or id_col >= len(values): continue
                iq = identity(values[id_col])
                if not iq: continue
                for col in name_cols:
                    if col >= len(values) or not isinstance(values[col], str): continue
                    name = valid_name(values[col])
                    if name: candidates[iq].append((name, {'path':str(path.relative_to(ws)), 'sheet':sheet.title, 'row':num, 'column':col + 1}))
        book.close()
    hr = ws / '05_Platforms_Housing_&_HR_Data/03_HR_Employees_Sponsorships_&_Templates'
    for path in sorted((ws / '04_IBAN_&_Payroll_Verification/02_Official_Mudad_IBAN_Sheets').glob('*.xlsm')):
        load(path, ['معلومات الموظفين'], 1, [0], 3)
    load(hr / 'employees-2026-10-06.xlsx', None, 3, [1])
    load(hr / 'الكفالات.xlsx', None, 0, [1])
    form = ws / '04_IBAN_&_Payroll_Verification/03_Courier_Forms_&_Platform_IBANs/استمارة بيانات راتب شهر سبتمبر(1-444).xlsx'
    load(form, ['السجل المجمع الشامل'], 2, [12, 3])
    employee_book = openpyxl.load_workbook(hr / 'employees-2026-10-06.xlsx', read_only=True, data_only=True)
    staff = {identity(r[3]):r for r in employee_book.worksheets[0].iter_rows(min_row=2, values_only=True) if identity(r[3])}
    employee_book.close()
    corrected = []; unresolved = []; ready = []; admin = []; seen = set()
    for index, row in enumerate(rows, 2):
        iq = row['iqama']; assert iq not in seen and identity(iq); seen.add(iq)
        source_name = row['name']; accepted = valid_name(source_name)
        row['sourceName'] = source_name; row['policyId'] = ''
        if not accepted:
            options = candidates.get(iq, [])
            if options:
                accepted, reference = options[0]
                row['nameEvidence'] = reference
                corrected.append({'originalRow':index, 'iqama':iq, 'sourceName':source_name, 'acceptedName':accepted, 'evidence':reference})
            else:
                row['directoryStatus'] = 'needs_name_review'; unresolved.append(row)
        if accepted: row['name'] = accepted; row['directoryStatus'] = 'needs_identity_review'
        is_admin = iq in staff and str(staff[iq][2]) == 'إداري'
        row['isEmployee'] = is_admin; row['personRole'] = 'administrative' if is_admin else 'rider'
        # Do not invent ERP GUIDs or turn source statuses into Active automatically.
        row['erpEmployeeId'] = None; row['erpSponsorId'] = None
        row['bankOwnershipReviewed'] = False
        if is_admin: admin.append(row)
        elif accepted: ready.append(row)
    baseline.write_text(json.dumps(rows, ensure_ascii=False, indent=2))
    (data / 'directory-name-corrections.json').write_text(json.dumps(corrected, ensure_ascii=False, indent=2))
    (data / 'directory-needs-review.json').write_text(json.dumps(unresolved, ensure_ascii=False, indent=2))
    headers = ['رقم الإقامة', 'الاسم الكامل', 'الجوال', 'المدينة', 'معرف سياسة الحساب']
    fields = lambda r: [r['iqama'], r['name'], r.get('phone',''), r.get('city',''), '']
    workbook(data / 'riders-template-ready.xlsx', headers, [fields(r) for r in ready])
    workbook(data / 'people-template-all.xlsx', headers, [fields(r) for r in rows if valid_name(r['name'])])
    workbook(data / 'administrative-reference.xlsx', headers, [fields(r) for r in admin if valid_name(r['name'])])
    workbook(data / 'riders-erp-comprehensive.xlsx', headers + ['نوع الكادر', 'العلاقة', 'الكفيل من المصدر', 'الحالة من المصدر', 'الجنسية', 'معرف ERP'],
             [fields(r) + [r['personRole'],r.get('engagement_type',''),r.get('sponsor',''),r.get('status',''),r.get('nationality',''),''] for r in rows if valid_name(r['name'])])
    (data / 'riders-template-862.xlsx').unlink(missing_ok=True)
    # Retire unsafe direct-ERP writes. Staging SQL has no INSERT/UPDATE against ERP.
    sql = ['-- Read-only reconciliation staging. Does not modify ERP or the payroll database.',
           'SET NOCOUNT ON;', 'CREATE TABLE #RiderDirectory (IqamaNo varchar(10) PRIMARY KEY, FullName nvarchar(200), IsEmployee bit, SourceStatus nvarchar(100), SourceSponsor nvarchar(200));']
    quote = lambda v: "N'" + str(v).replace("'", "''") + "'"
    for row in rows:
        sql.append('INSERT INTO #RiderDirectory VALUES (' + ','.join([quote(row['iqama']),quote(row['name']),str(int(row['isEmployee'])),quote(row.get('status','')),quote(row.get('sponsor',''))]) + ');')
    sql += ['SELECT s.*, e.Id AS ExistingEmployeeId, e.SponsorId AS ExistingSponsorId, e.Status AS ExistingStatus,',
            "CASE WHEN e.Id IS NULL THEN N'needs_review_before_create' ELSE N'existing_identity' END AS ReconciliationStatus",
            'FROM #RiderDirectory s LEFT JOIN app.Employees e ON e.IqamaNo = s.IqamaNo AND e.IsDeleted = 0;', 'DROP TABLE #RiderDirectory;']
    (data / 'riders-erp-seed.sql').write_text('\n'.join(sql) + '\n')
    summary = {'people':len(rows), 'uniqueIdentities':len(seen), 'ridersReadyForPreview':len(ready), 'administrative':len(admin),
               'namesRecoveredFromReferencedSources':len(corrected), 'unresolvedNames':len(unresolved), 'sourceEvidence':provenance,
               'databaseWrites':0, 'policyAssignments':0, 'erpScript':'read-only staging reconciliation'}
    (data / 'directory-validation.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2))
    print(json.dumps({k:v for k,v in summary.items() if k != 'sourceEvidence'}, ensure_ascii=False))

if __name__ == '__main__': main()
