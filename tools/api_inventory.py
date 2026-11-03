"""Regenerate the human route index from Minimal API declarations (no runtime data)."""
from pathlib import Path
import re
ROOT = Path(__file__).resolve().parents[1]
DEPRECATED = {'/api/template.csv', '/api/payroll/fingerprint', '/api/imports', '/api/debts/settlements', '/api/public/forms/{token}/resume', '/api/system/release-check'}
GROUPS = {'Home':'الرئيسية', 'Riders':'المناديب', 'Companies and registrations':'الشركات والسجلات', 'Payroll':'الرواتب', 'Debts and advances':'المديونيات والسلف', 'Banks and treasury':'البنوك والصناديق والخزن', 'Accounting and reports':'المحاسبة والتقارير', 'Forms and responses':'النماذج والردود', 'Administration':'الإدارة'}
def permission(method,p):
 if p.startswith(('/api/public/', '/api/auth/')) or not p.startswith('/api/'): return 'public (rider portal: own authenticated rider)'
 if p in ('/api/me','/api/roles-catalog','/api/search') or p.startswith('/api/notifications'): return 'authenticated; notifications: category/owner visibility'
 if p.startswith('/api/companies'): return 'companies.view' if method=='GET' else 'companies.manage'
 if p.startswith(('/api/users','/api/system','/api/rider-mobile/firebase')) or method=='DELETE' or p=='/api/submissions/bulk-delete' or method=='PUT' and p.startswith('/api/campaigns/'):return 'Admin / SuperAdmin'
 if p.startswith('/api/riders/sync') or p.startswith('/api/riders/enrich'):return 'Admin / SuperAdmin'
 if p.startswith('/api/riders'):return ('riders.view_360 + debts.view' if p.endswith('/profile-360') else 'riders.view_360' if '/documents' in p and method=='GET' else 'riders.view' if method=='GET' else 'riders.edit (+ riders.financial_edit for financial fields)')
 if p.startswith('/api/debts'):
  if '/reconcile-opening/' in p:return 'treasury.opening'
  if method=='GET':return 'debts.view'
  if '/import-opening/' in p:return 'debts.import_opening' + (' + treasury.opening' if p.endswith('/commit') else '')
  if 'collect' in p:return 'debts.collect (+ debts.waive when waived)'
  if p.endswith('/transfer-sponsor'):return 'debts.transfer_sponsor'
  if method=='PUT':return 'debts.reschedule'
  return 'debts.create.<category> (+ treasury.opening for opening balances)'
 if p.startswith('/api/finance/statement/') or p.startswith('/api/promissory'):return 'debts.view (promissory writes: Admin + feature enabled)'
 if '/bank-file/' in p:return 'finance.bankfile.export + payroll.view'
 if '/trial-balance' in p or '/income-statement' in p:return 'treasury.journals'
 if p=='/api/finance/catalog':return 'debts.view OR treasury.view'
 if '/opening/' in p or '/legacy-cash-allocation/' in p:return 'treasury.opening'
 if '/jahez-handovers/' in p:return 'treasury.jahez.receive'
 if p.startswith('/api/finance'):
  if method=='GET':return 'treasury.view'
  return {'vault-transfer':'treasury.transfer','liability-payment':'treasury.liability.settle','jahez-transfer':'treasury.transfer','jahez-platform-settlement':'treasury.transfer','jahez-fee-deposit':'treasury.transfer','jahez-fee':'treasury.jahez.manage','jahez-handover':'treasury.jahez.handover','promissory-notes':'Admin / SuperAdmin'}.get(p.split('/')[-1], 'Admin / SuperAdmin')
 if p.startswith('/api/approvals'):return 'approvals.view' if method=='GET' else 'approvals.decide + department/rank + maker-checker'
 if p.startswith('/api/rider-mobile'):return 'mobile.broadcast'
 if p.startswith('/api/excel'):
  if p.endswith('/export'):return 'riders.view'
  kind=p.split('/')[3];return ('riders.view' if method=='GET' else 'riders.edit + riders.financial_edit' if p.endswith('/commit') else 'riders.edit') if kind=='riders' else ('forms.view' if method=='GET' else 'forms.manage') if kind=='choices' else 'imports.commit' if p.endswith('/commit') else 'imports.view'
 if p.startswith('/api/imports') or '/template.csv' in p:return 'imports.commit' if p.endswith('/commit') else 'imports.view'
 if p.startswith('/api/policies'):return 'policies.view' if method=='GET' else 'policies.manage'
 if p.startswith('/api/payroll'):return 'payroll.view' if method=='GET' else 'payroll.approve'
 if p.startswith('/api/ledger') or p=='/api/dashboard':return 'payroll.view'
 if p=='/api/adjustments':return 'adjustments.create'
 if p=='/api/payments':return 'payments.record'
 if p.startswith(('/api/campaigns','/api/forms','/api/submissions','/api/responses')):return 'forms.manage' if '/builder' in p or method!='GET' and p.startswith('/api/campaigns') else 'forms.review' if method!='GET' else 'forms.view'
 if p=='/api/audit':return 'Admin / SuperAdmin'
 return 'see AccessPolicy + handler'
def routes():
 rows=[]
 for f in sorted((ROOT/'Payroll.Api').glob('*.cs')):
  s=f.read_text()
  for m in re.finditer(r'\b(app|api)\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"',s):
   method=m[2].upper();p=('/api'+m[3] if m[1]=='api' else m[3]).replace('/api/api/','/api/')
   # Metadata belongs to the declaration before the next route.
   end=re.search(r'\b(?:app|api)\.Map(?:Get|Post|Put|Delete|Patch)\(',s[m.end():]);tail=s[m.end():m.end()+end.start()] if end else s[m.end():]
   tag=re.search(r'\.WithTags\("([^"]+)"',tail);group=tag[1] if tag else 'Administration'
   caller='rider form' if p.startswith('/api/public/forms') else 'rider app' if p.startswith('/api/rider-mobile') and ('portal' in p or 'ack' in p or 'login' in p) else 'none'
   if caller=='none':
    prefix=re.sub(r'\{[^}]+\}', '',p).rstrip('/')
    if any(prefix in x.read_text() for x in (ROOT/'Frontend/src').glob('*.ts')):caller='old UI'
   status='deprecated' if p in DEPRECATED and (p!='/api/imports' or method=='GET') else 'new' if p.startswith(('/api/companies','/api/search','/api/responses')) or p.startswith('/api/notifications') and '/center' not in p else 'keep'
   if p=='/api/users/{id}':status='new'
   for actual in (['/api/public/inquiry/payslip','/api/public/inquiry/debt'] if p=='/api/public/inquiry/' else [p]):
    rows.append((group,method,actual,f'{f.relative_to(ROOT)}:{s[:m.start()].count(chr(10))+1}',permission(method,actual),caller,status))
 for prefix in ("/api/campaigns", "/api/forms/builder"):
  for method,suffix,needle in (("GET","/{id}/delete-preview","api.MapGet(path"),("DELETE","/{id}","api.MapDelete(path")):
   f=ROOT/"Payroll.Api/FormDeletion.cs";line=next(i for i,l in enumerate(f.read_text().splitlines(),1) if needle in l)
   rows.append(("Forms and responses",method,prefix+suffix,f"Payroll.Api/FormDeletion.cs:{line}","Admin / SuperAdmin","old UI","keep"))
 return rows

def main():
 rows=routes();out=['# Endpoint index — 0.10.0 API / 1.0-A','', 'Generated by `python3 tools/api_inventory.py`; built-in admin-only OpenAPI: `/openapi/v1.json`.','', 'Permissions below are minimum route gates; existing handler checks, company scope and maker-checker also apply. Lists use `period=yyyy-MM`, ranges `from/to=yyyy-MM-dd`; opt-in `page>=1`, `pageSize<=200`, `q`, `sort=field|-field` return `{items,total,page,pageSize}`. Without `page`, legacy responses are unchanged.','', 'Review actions stay on their existing routes. `/promissory-notes` is the document archive; `/finance/features/promissory-notes` is its feature setting. Deprecated routes remain callable.','']
 for group,label in GROUPS.items():
  out += ['## '+label,'','| Method | Path | Source | Permission | Caller | Status |','|---|---|---|---|---|---|']
  for g,m,p,f,perm,c,st in sorted(rows):
   if g==group:out.append(f'| {m} | `{p}` | {f} | {perm} | {c} | {st} |')
  out.append('')
 out += ['## Compatibility and deprecations', '', '| Route | Replacement / reason |', '|---|---|',
 '| `GET /api/template.csv` | Prefer `/api/excel/work/template`; keep the CSV compatibility template. |',
 '| `GET /api/payroll/fingerprint` | Use the fingerprint returned by payroll preview. |',
 '| `GET /api/imports` | Legacy batch history; keep available while the new imports UI lands. |',
 '| `GET /api/debts/settlements` | Legacy settlement list; existing rider statement and journals remain authoritative. |',
 '| `POST /api/public/forms/{token}/resume` | New callers use `/lookup` and `/edit`; retain the receipt-based legacy route. |',
 '| `GET /api/system/release-check` | Tests/diagnostics only, administrator access; public deployment metadata is `/health`. |', '',
 'No route is removed. OpenAPI flags the same six operations as deprecated. HTML/static routes and `/health` are included in the inventory but remain outside `/api`.', '',
 'Company DELETE actions archive metadata and retain financial references and attachments. New companies do not create new treasury accounts or change posting policy. The finance sponsor read alias keeps its balance fields.', '',
 'Unified response rows: `id,source,campaignId,title,status,submittedAt,updatedAt,iqama,name,answers,version,reviewUrl`. For custom forms, `campaignId` is the form ID and `reviewUrl` is null because no custom review mutation exists. Campaign reviews continue on the original POST route.', '']
 (ROOT/'docs/api/ENDPOINTS.md').write_text('\n'.join(out))
 print(f'Routes: {len(rows)} total, {sum(p.startswith("/api/") for _,_,p,*_ in rows)} API, {sum(r[-1]=="new" for r in rows)} new, {sum(r[-1]=="deprecated" for r in rows)} deprecated')
if __name__=='__main__':main()
