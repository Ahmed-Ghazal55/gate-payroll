// Synthetic HTTP contracts for invoice imports and operation review. Loopback/SQLite only.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-r6c-http-'));
let child,base,checks=0;const cookies={};
async function call(user,route,body,status=200){
 const multipart=body instanceof FormData;
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookies[user]?{Cookie:cookies[user]}:{}),...(body===undefined||multipart?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:multipart?body:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,route+': '+raw.slice(0,160));checks++;if(r.headers.get('set-cookie'))cookies[user]=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
}
try{
 const probe=createServer();await new Promise((resolve,reject)=>{probe.once('error',reject);probe.listen(0,'127.0.0.1',resolve);});base='http://127.0.0.1:'+probe.address().port;await new Promise(resolve=>probe.close(resolve));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,ConnectionStrings__DefaultConnection:'',Jobs__Enabled:'false',Vouchers__Secret:randomBytes(32).toString('hex'),PAYROLL_DATA_DIR:data},stdio:'ignore'});
 let ready=false;for(let i=0;i<150;i++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready);
 await call('anonymous','/api/platforms/keeta/invoices',undefined,401);
 const password=randomBytes(24).toString('hex');await call('admin','/api/auth/setup',{username:'syn-r6c-admin',password});
 await call('admin','/api/users',{username:'syn-r6c-checker',password,role:'FinancialAccountant',customPermissions:['pay.view','pay.manage','pay.approve','imports.view']});await call('checker','/api/auth/login',{username:'syn-r6c-checker',password});
 const company=(await call('admin','/api/companies'))[0].id;
 const db=new DatabaseSync(path.join(data,'payroll.db'));db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run('syn-keeta-account','platform_account',JSON.stringify({id:'syn-keeta-account',externalAccountId:'SYN-K1',companyId:company,platformCode:'keeta'}));db.close();
 // Catalog HTTP removed with imports centre; assert platform invoice + ops template still served.
 assert.equal((await fetch(base+'/api/imports/catalog',{headers:{Cookie:cookies.admin}})).status,404);checks++;
 assert.equal((await fetch(base+'/api/imports/templates/operations-keeta',{headers:{Cookie:cookies.admin}})).status,200);checks++;
 const response=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookies.admin},body:JSON.stringify({headers:['المعرف','الطلبات المُسلمة','الإجمالي','الخصم','إجمالي المبلغ المستحق','صافي الراتب'],rows:[['SYN-K1',10,100,0,115,9999]],numericColumns:[1,2,3,4,5]})});assert.equal(response.status,200);checks++;const bytes=await response.arrayBuffer();
 function upload(preview){const form=new FormData();for(const [key,value]of Object.entries({companyId:company,period:'2026-10',reference:'SYNTHETIC-KEETA-HTTP',accountingRole:'principal'}))form.set(key,value);form.set('file',new Blob([bytes]),'synthetic.xlsx');if(preview){form.set('rows',JSON.stringify(preview.rows));form.set('fingerprint',preview.fingerprint);form.set('confirm','true');}return form;}
 const preview=await call('admin','/api/platforms/keeta/invoices',upload());assert.equal(preview.rows[0].amountExclVat,100);
 await call('checker','/api/platforms/keeta/invoices?commit=1',upload(preview),409);
 const invoice=await call('admin','/api/platforms/keeta/invoices?commit=1',upload(preview));
 const lines=await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/lines`);assert.equal(lines[0].invoiced,100);
 await call('admin',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/approve`,{},403);
 await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/approve`,{});
 await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/post`,{},409); // gate remains OFF
 await call('admin','/api/platforms/keeta/invoices',upload(),400); // duplicate bytes refused
 // D3: actual routes, native JSON/XLSX transport and source-bound cycle review, all synthetic.
 const setupDb=new DatabaseSync(path.join(data,'payroll.db'));
 function put(kind,doc){setupDb.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET body=excluded.body').run(doc.id,kind,JSON.stringify(doc));}
 const coreOps=['period (YYYY-MM)','platformAccountId','iqama','riderName','from (YYYY-MM-DD)','to (YYYY-MM-DD)','days','orders','baseGross','deduction','category'];
 const opsByApp={
  ninja:{previewUrl:'/api/pay/apps/ninja/operations/import/preview',commitUrl:'/api/pay/apps/ninja/operations/import/commit',template:'operations-ninja',extra:['captainId','attendanceDays','hours8Days','weekendDays','workingHours','activeWorkingHours','committedWorkingHours','shiftCompliancePercent','onTimeAttendancePercent']},
  hs:{previewUrl:'/api/pay/apps/hs/operations/import/preview',commitUrl:'/api/pay/apps/hs/operations/import/commit',template:'operations-hungerstation',extra:['hsRiderId','hours','distanceKm']},
 };
 for(const app of ['ninja','hs']){
  const account='syn-ops-'+app,invoiceId='syn-ops-invoice-'+app,rider='2000000091';
  put('rider',{id:rider,iqama:rider,sponsorId:company,isDeliveryRider:true});
  put('platform_account',{id:account,companyId:company,platformCode:app,assignments:[{riderId:rider,RawEffectiveFrom:'2026-10-01T00:00:00+03:00'}]});
  put('platform_invoice',{id:invoiceId,companyId:company,platform:{ninja:'Ninja',hs:'HungerStation'}[app],period:'2026-10',approvalStatus:'approved',amountExclVat:100,vat:15,deductionsTotal:0});
  put('platform_invoice_line',{id:'syn-ops-line-'+app,invoiceId,platformAccountId:account,baseGross:100,incentives:0,orders:5,deductionAmount:0});
  const step=opsByApp[app];
  const templateRes=await fetch(base+'/api/imports/templates/'+step.template,{headers:{Cookie:cookies.admin}});assert.equal(templateRes.status,200);checks++;
  const headers=[...coreOps,...step.extra];
  const values={'period (YYYY-MM)':'2026-10',platformAccountId:account,iqama:rider,days:1,orders:5,baseGross:100,deduction:0,category:'sponsored_private'};
  const numericColumns=headers.map((c,i)=>['days','orders','baseGross','deduction','attendanceDays','hours8Days','weekendDays','workingHours','activeWorkingHours','committedWorkingHours','shiftCompliancePercent','onTimeAttendancePercent','hours','distanceKm'].includes(c)?i:-1).filter(i=>i>=0);
  const xlsx=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookies.admin},body:JSON.stringify({headers,rows:[headers.map(c=>values[c]??'')],numericColumns})});assert.equal(xlsx.status,200);checks++;
  const input={invoiceId,dataBase64:Buffer.from(await xlsx.arrayBuffer()).toString('base64')};
  await call('anonymous',step.previewUrl,input,401);
  const view=await call('admin',step.previewUrl,input);assert.equal(view.rows[0].accounts[0].riders[0].rider,rider);
  await call('checker',step.commitUrl,{...input,...view,confirm:true},409);
  const saved=await call('admin',step.commitUrl,{...input,...view,confirm:true});assert.deepEqual(saved.review,view.rows[0]);
  await call('checker',`/api/pay/apps/${app}/operations/imports/${encodeURIComponent(saved.id)}`);
  await call('admin',step.commitUrl,{...input,...view,confirm:true},409);
 }
 // Keeta ops: Arabic A..AH register sheet + register before upload.
 {
  const account='syn-ops-keeta',invoiceId='syn-ops-invoice-keeta',rider='2000000091',external='1000000000000099';
  put('rider',{id:rider,iqama:rider,sponsorId:company,isDeliveryRider:true});
  put('platform_account',{id:account,externalAccountId:external,companyId:company,platformCode:'keeta',assignments:[{riderId:rider,RawEffectiveFrom:'2026-10-01T00:00:00+03:00'}]});
  put('platform_invoice',{id:invoiceId,companyId:company,platform:'Keeta',period:'2026-10',approvalStatus:'approved',amountExclVat:100,vat:15,deductionsTotal:0,status:'final'});
  put('platform_invoice_line',{id:'syn-ops-line-keeta',invoiceId,platformAccountId:account,baseGross:100,incentives:0,orders:5,deductionAmount:0});
  const templateRes=await fetch(base+'/api/imports/templates/operations-keeta',{headers:{Cookie:cookies.admin}});assert.equal(templateRes.status,200);checks++;
  // Build XLSX without /excel/export (that path splits الايبان into bank columns).
  const headers=['المعرف','الاسم بالكامل','رقم بطاقة الهوية','كفالة اليوزر','المركبة','الحالة','رقم الهوية1','البديل 1','رقم التواصل','نوع البديل','نوع المركبة','رقم كرت البنزين1','الأدوات1','الجنسية 1','الرقم المسجل','الايبان 1','تاريخ الاستلام1','تاريخ التسليم','الأيام الصالحة','الطلبات','المخالفات','المسافة المدفوعة','عدد ايام عمل المندوب','الصلاحية المتوقعة','اجمالي المسافه','مطابقة المسافة','الملاحظات','اجمالي طلبات المعادلة','اجمالي طلبات اليوزر','مطابقة الطلبات','عدد البدلاء','اجمالي ايام المعادلة','اجمالي ايام اليوزر','مطابقة الايام'];
  const values=[external,'Synthetic',rider,'سجل تجريبي','خاصة','TRUE',rider,'','','','خاصة','','','','','','2026-10-01','2026-10-31','1','5','0','1','1','شريحة متوقعة','1','','','','','','','','',''];
  const py=`import zipfile,io
from xml.sax.saxutils import escape
headers=${JSON.stringify(headers)}
values=${JSON.stringify(values)}
def col(i):
 s=''
 while True:
  i,r=divmod(i,26); s=chr(65+r)+s
  if i==0: break
  i-=1
 return s
def cell(c,r,v,n=False):
 ref=col(c)+str(r)
 if n:
  return f'<c r="{ref}"><v>{escape(str(v))}</v></c>'
 return f'<c r="{ref}" t="inlineStr"><is><t>{escape(str(v))}</t></is></c>'
rows=['<row r="1">'+''.join(cell(i,1,h) for i,h in enumerate(headers))+'</row>']
rows.append('<row r="2">'+''.join(cell(i,2,values[i], str(values[i]).replace('.','',1).isdigit()) for i in range(len(headers)))+'</row>')
sheet='<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>'+''.join(rows)+'</sheetData></worksheet>'
buf=io.BytesIO()
with zipfile.ZipFile(buf,'w') as z:
 z.writestr('[Content_Types].xml',\"\"\"<?xml version='1.0' encoding='UTF-8'?><Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/><Override PartName='/xl/worksheets/sheet1.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml'/></Types>\"\"\")
 z.writestr('_rels/.rels',\"\"\"<?xml version='1.0' encoding='UTF-8'?><Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>\"\"\")
 z.writestr('xl/workbook.xml',\"\"\"<?xml version='1.0' encoding='UTF-8'?><workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='البيانات' sheetId='1' r:id='rId1'/></sheets></workbook>\"\"\")
 z.writestr('xl/_rels/workbook.xml.rels',\"\"\"<?xml version='1.0' encoding='UTF-8'?><Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/></Relationships>\"\"\")
 z.writestr('xl/worksheets/sheet1.xml',sheet)
import sys; sys.stdout.buffer.write(buf.getvalue())
`;
  const {spawnSync: sp}=await import('node:child_process');
  const built=sp('python3',['-c',py],{encoding:null,maxBuffer:5*1024*1024});
  assert.equal(built.status,0,String(built.stderr||''));checks++;
  const companies=await call('admin','/api/companies');
  const register=(companies.find(c=>c.id===company)?.code)||'BW';
  const input={invoiceId,register,dataBase64:Buffer.from(built.stdout).toString('base64')};
  await call('anonymous','/api/pay/apps/keeta/operations/import/preview',input,401);
  await call('admin','/api/pay/apps/keeta/operations/import/preview',{invoiceId,dataBase64:input.dataBase64},400);
  const view=await call('admin','/api/pay/apps/keeta/operations/import/preview',input);
  assert.equal(view.rows[0].accounts[0].riders[0].rider,rider);
  assert.equal(view.register,register);
  assert.equal(view.displayRows.length,1);
  assert.equal(view.rows[0].accounts[0].riders[0].metrics.expectedApproval,'expected_only');
  await call('checker','/api/pay/apps/keeta/operations/import/commit',{...input,...view,confirm:true},409);
  const saved=await call('admin','/api/pay/apps/keeta/operations/import/commit',{...input,...view,confirm:true});
  assert.deepEqual(saved.review,view.rows[0]);
  assert.equal(saved.register,register);
  await call('checker',`/api/pay/apps/keeta/operations/imports/${encodeURIComponent(saved.id)}`);
  await call('admin','/api/pay/apps/keeta/operations/import/commit',{...input,...view,confirm:true},409);
 }
 // D4: read model only, counts and immutable source records checked around the HTTP request.
 const chefzAccount='syn-chefz-account',chefzRider='2000000091';
 put('platform_account',{id:chefzAccount,companyId:company,platformCode:'chefz',assignments:[{riderId:chefzRider,RawEffectiveFrom:'2026-10-01T00:00:00+03:00'}]});
 put('rider_account_assignment',{id:'syn-chefz-assignment',erpSourceId:'SYN-CA',companyId:company,platformAccountId:chefzAccount,riderId:chefzRider,effectiveFrom:'2026-10-01',effectiveTo:'2026-10-15'});
 put('chefz_settlement',{id:'syn-chefz-wallet',erpSourceId:'SYN-CW',companyId:company,platformAccountId:chefzAccount,riderId:chefzRider,assignmentSourceId:'SYN-CA',periodFrom:'2026-10-01',periodTo:'2026-10-15',chargedFrom:'2026-10-01',chargedTo:'2026-10-02',assignedDays:2,gross:100,deductions:0,vat:15,companyAmount:10,riderAmount:90});
 put('chefz_daily',{id:'syn-chefz-daily',erpSourceId:'SYN-CD',companyId:company,platformAccountId:chefzAccount,riderId:chefzRider,assignmentSourceId:'SYN-CA',date:'2026-10-01',totalValidOffers:10,acceptedOffers:8,declinedOffers:1,missedOffers:1});
 await call('admin','/api/users',{username:'syn-r6c-viewer',password,role:'Reviewer',customPermissions:['pay.view'],financialScope:{sponsorIds:[company],platforms:['Chefz']}});await call('viewer','/api/auth/login',{username:'syn-r6c-viewer',password});
 const beforeRead=setupDb.prepare('SELECT id,kind,body FROM records ORDER BY id').all();
 const read=await call('viewer','/api/pay/apps/chefz/operations/review',{companyId:company,period:'2026-10'});
 assert.equal(read.readOnly,true);assert.equal(read.canUseInCycle,false);assert.equal(read.counts.settlements,1);assert.equal(read.review.accounts[0].riders[0].metrics.acceptedOffers,8);assert.equal(read.review.accounts[0].riders[0].orders,0);
 assert.deepEqual(setupDb.prepare('SELECT id,kind,body FROM records ORDER BY id').all(),beforeRead,'read model must not write');
 await call('viewer','/api/pay/apps/ninja/operations/import/commit',{},403);
 // Chefz is review-only (no commit route).
 await call('viewer','/api/pay/apps/chefz/operations/review',{companyId:company,period:'2026-10'});
 setupDb.close();
 console.log(`PASS R6c invoice/operations HTTP contracts: ${checks} checks`);
}finally{
 if(child&&child.exitCode===null){const exit=once(child,'exit');child.kill();await exit;}
 rmSync(data,{recursive:true,force:true});
}
