import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const directory=mkdtempSync(path.join(tmpdir(),'gate-finance-views-http-'));
const base='http://127.0.0.1:19462';
const server=spawn(process.env.DOTNET_PATH||'dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:directory},stdio:['ignore','pipe','pipe']});
let logs='',cookie='',spawnError;
server.stdout.on('data',x=>logs+=x);server.stderr.on('data',x=>logs+=x);server.once('error',error=>spawnError=error);
async function req(route,body,status=200,auth=true){
 const response=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});
 const data=await response.json();assert.equal(response.status,status,`${route}: ${JSON.stringify(data)}`);
 if(response.headers.get('set-cookie'))cookie=response.headers.get('set-cookie').split(';')[0];return data;
}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(server.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready,logs);
 assert.equal((await req('/health',undefined,200,false)).version,'0.9.0');
 await req('/api/finance/coa/target',undefined,401,false);
 const password=randomBytes(24).toString('hex');await req('/api/auth/setup',{username:'synthetic-admin',password});const admin=cookie;
 const coa=await req('/api/finance/coa/target');assert.equal(coa.accounts.length,392);
 const body='80'+'0'.repeat(15)+'123';let remainder=0;for(const d of body+'281000')remainder=(remainder*10+Number(d))%97;const iban='SA'+String(98-remainder).padStart(2,'0')+body;
 const iqama='2000000888',period='2026-10';
 const policy=await req('/api/policies',{name:'Synthetic commission',kind:'commission',platform:'all',effectiveFrom:period,config:{companyFee:500}});await req(`/api/policies/${policy.id}/approve`,{});
 await req('/api/riders',{iqama,name:'Synthetic Rider',financeSponsorId:'BW',policyId:policy.id,iban});
 const csv=`sourceRef,iqama,name,accountId,from,to,orders,platformRevenue,isValid\r\nsynthetic-work,${iqama},Synthetic Rider,0000000000000001,2026-10-01,2026-10-31,600,1500,true\r\n`;
 const upload=await req('/api/imports/inspect',{filename:'synthetic.csv',dataBase64:Buffer.from(csv).toString('base64')});
 const importSpec={sheet:'CSV',period,platform:'Keeta',headerRow:1,confirmActualRider:true,mapping:{sourceRef:0,iqama:1,name:2,accountId:3,from:4,to:5,orders:6,platformRevenue:7,isValid:8}};
 const importPreview=await req(`/api/imports/${upload.id}/preview`,importSpec);assert.deepEqual(importPreview.errors,[]);await req(`/api/imports/${upload.id}/commit`,importSpec);
 const pay=(await req('/api/payroll/preview?period='+period))[0];assert.equal(pay.payable,1000);await req('/api/payroll/approve',{sponsorId:'BW',iqama,period,fingerprint:pay.fingerprint});
 const before=(await req('/api/system/release-check')).snapshot;
 for(const view of ['current','target']){const trial=await req(`/api/finance/trial-balance?sponsorId=BW&from=${period}&to=${period}&view=${view}`);assert.equal(trial.companies.length,1);assert.ok(trial.companies[0].isBalanced);assert.equal(trial.companies[0].totals.periodDebit,trial.companies[0].totals.periodCredit);}
 const income=await req(`/api/finance/income-statement?sponsorId=BW&from=${period}&to=${period}`);assert.equal(income.companies[0].revenue,500);
 const preview=await req('/api/finance/bank-file/preview',{sponsorId:'BW',period});assert.ok(preview.canExport);assert.equal(preview.rows[0].amount,1000);assert.equal(preview.rows[0].bank,'RJHI');
 assert.deepEqual((await req('/api/system/release-check')).snapshot,before);
 const invalid=structuredClone(preview.rows);invalid[0].amount=0;await req('/api/finance/bank-file/export',{sponsorId:'BW',period,previewToken:preview.previewToken,rows:invalid},400);
 const rows=structuredClone(preview.rows);rows[0].amount=900;rows[0].notes='Synthetic';
 const response=await fetch(base+'/api/finance/bank-file/export',{method:'POST',headers:{Cookie:cookie,'Content-Type':'application/json'},body:JSON.stringify({sponsorId:'BW',period,previewToken:preview.previewToken,rows})});assert.equal(response.status,200);
 assert.match(response.headers.get('content-disposition'),/[0-9]+_bulkPayroll\.csv/);const bytes=Buffer.from(await response.arrayBuffer());assert.deepEqual([...bytes.subarray(0,3)],[239,187,191]);assert.equal(bytes.subarray(3).toString().split('\r\n')[0],'البنك,رقم الحساب,إجمالي الراتب,ملاحظات,اسم الموظف,رقم الهوية أو الإقامة,عنوان الموظف');
 await req('/api/finance/trial-balance?from=2026-13',undefined,400);
 await req('/api/finance/trial-balance?view=invalid',undefined,400);
 await req('/api/users',{username:'synthetic-manager',password,role:'FinancialManager',financialScope:{sponsorIds:['BW'],accountCodes:[],categoryCodes:[],platforms:[]}});
 await req('/api/auth/login',{username:'synthetic-manager',password});await req('/api/finance/trial-balance?sponsorId=EX',undefined,403);await req('/api/finance/bank-file/preview',{sponsorId:'EX',period},403);
 const scoped=await req(`/api/finance/trial-balance?from=${period}&to=${period}`);assert.equal(scoped.companies.length,1);
 cookie=admin;await req('/api/users',{username:'synthetic-accountant',password,role:'Accountant'});await req('/api/auth/login',{username:'synthetic-accountant',password});await req('/api/finance/bank-file/preview',{sponsorId:'BW',period},403);
 console.log('PASS finance views HTTP: posted payroll source, resource, both balanced views, income, immutable reads, bank CSV bytes and company/permission isolation.');
}finally{if(server.exitCode===null){server.kill('SIGTERM');await new Promise(resolve=>server.once('exit',resolve));}rmSync(directory,{recursive:true,force:true});}
