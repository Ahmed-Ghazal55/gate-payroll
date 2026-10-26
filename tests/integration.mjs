import {seedAccountingCash} from './financial-fixtures.mjs';
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-payroll-test-'));
const port=19347, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const iq='2000000001';
const csv=(revenue,ref='row-1',period='2026-10')=>`sourceRef,iqama,name,accountId,from,to,orders,platformRevenue,isValid\r\n${ref},${iq},Synthetic Rider,1234567890123456,${period}-01,${period}-31,600,${revenue},false\r\n`;
async function upload(text,period='2026-10'){const u=await req('/api/imports/inspect',{filename:'synthetic.csv',dataBase64:Buffer.from(text).toString('base64')});const spec={sheet:'CSV',period,platform:'Keeta',headerRow:1,confirmActualRider:true,mapping:{sourceRef:0,iqama:1,name:2,accountId:3,from:4,to:5,orders:6,platformRevenue:7,isValid:8}};return {u,spec}}
try{
 let ready=false; for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error('Test server exited before startup');try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready,'Test server did not become ready');
 await req('/api/riders',undefined,401,false);
 assert.match(await req('/app.css',undefined,200,false),/sidebar/);
 assert.match(await req('/app.js',undefined,200,false),/dashboard/);
 await req('/api/auth/setup',{username:'test-admin',password:randomBytes(24).toString('hex')});
 await seedAccountingCash(req);
 await req('/api/riders',{iqama:iq,name:'Synthetic Rider'},403,true,{Origin:'https://different.invalid'});
 await req('/api/policies',{name:'Invalid fee',kind:'commission',platform:'all',effectiveFrom:'2026-10',config:{companyFee:'invalid'}},400);
 let policy=await req('/api/policies',{name:'Test commission',kind:'commission',platform:'all',effectiveFrom:'2026-10',config:{companyFee:2500}});
 await req(`/api/policies/${policy.id}/approve`,{});
 await req('/api/riders',{iqama:iq,name:'Synthetic Rider',financeSponsorId:'BW',phone:'0501234567',city:'Test',policyId:policy.id});
 let {u,spec}=await upload(csv(3000)+csv(1000,'row-2').split('\r\n').slice(1).join('\r\n'));
 const preview=await req(`/api/imports/${u.id}/preview`,spec);assert.equal(preview.errors.length,0);assert.equal(preview.rows[0].accountId,'1234567890123456');
 await req(`/api/imports/${u.id}/commit`,spec);await req(`/api/imports/${u.id}/commit`,spec,400);
 let result=(await req('/api/payroll/preview?period=2026-10'))[0];assert.equal(result.periodMovement,1500);assert.equal(result.blockers.length,0);
 await req('/api/adjustments',{iqama:iq,period:'2026-10',amount:-200,category:'fuel',description:'Actual fuel',sourceRef:'fuel-1'});
 await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:result.fingerprint},400);
 result=(await req('/api/payroll/preview?period=2026-10'))[0];assert.equal(result.periodMovement,1300);
 await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:result.fingerprint});await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:result.fingerprint},400);
 await req('/api/adjustments',{iqama:iq,period:'2026-10',amount:-1,category:'fuel',description:'Late',sourceRef:'late'},400);
 await req('/api/payments',{sponsorId:'BW',paymentVaultCode:'BANK.BW',iqama:iq,period:'2026-10',amount:1000,sourceRef:'transfer-1'});assert.equal((await req('/api/ledger/'+iq)).balance,300);
 let nov=await upload(csv(2000,'nov-1','2026-11').replace('2026-11-31','2026-11-30'),'2026-11');await req(`/api/imports/${nov.u.id}/commit`,nov.spec);
 result=(await req('/api/payroll/preview?period=2026-11'))[0];assert.equal(result.openingBalance,300);assert.equal(result.closingBalance,-200);assert.equal(result.debt,200);assert.equal(result.payable,0);
 await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-11',fingerprint:result.fingerprint});assert.equal((await req('/api/ledger/'+iq)).balance,-200);
 const campaign=await req('/api/campaigns',{title:'Synthetic form',days:2});const token=campaign.path.split('/').at(-1);const pub='/api/public/forms/'+token;
 assert.match(await req(campaign.path,undefined,200,false),/rider-form.js/);
 const form={iqama:iq,name:'Synthetic Rider',workSystem:'tiers'};
 await req(pub,{...form,iqama:'٢٠٠٠٠٠٠٠٠١'},400,false);
 await req(pub,{...form,iqama:'200000000'},400,false);
 await req(pub,{...form,name:'Name123 Rider'},400,false);
 await req(pub,{...form,workSystem:'discuss'},400,false);
 await req(pub,{...form,phone:'0501234567'},400,false);
 const receipt=await req(pub,form,200,false);assert.ok(receipt.receipt);await req(pub,form,409,false);
 let submissions=await req('/api/submissions');assert.equal(submissions[0].status,'pending');assert.equal((await req('/api/riders'))[0].workPreference,undefined);
 await req(`/api/submissions/${receipt.receipt}/review`,{status:'approved',note:'Test',identityReviewed:false},400);
 await req(`/api/submissions/${receipt.receipt}/review`,{status:'approved',note:'Synthetic external identity review',identityReviewed:true});
 const rider=(await req('/api/riders'))[0];assert.equal(rider.workPreference,'tiers');assert.equal(rider.policyId,policy.id);assert.equal(rider.phone,'+966501234567');assert.equal(rider.city,'Test');assert.equal(submissions[0].phone,undefined);assert.equal(submissions[0].iban,undefined);
 await req(`/api/campaigns/${campaign.id}/close`,{});await req(pub,undefined,400,false);
 assert.equal((await req('/api/payroll/runs')).length,2);

 // Excel runtime round trip: generate through the same endpoint used by every table.
 const output=path.join(root,'test-results');mkdirSync(output,{recursive:true});
 async function book(headers,rows,numericColumns=[]){const r=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({headers,rows,numericColumns})});assert.equal(r.status,200);checks++;assert.match(r.headers.get('content-type'),/spreadsheetml/);return Buffer.from(await r.arrayBuffer());}
 const choiceHeaders=['رقم الإقامة','الاسم الكامل','نظام العمل'];
 const riderHeaders=['رقم الإقامة','الاسم الكامل','الجوال','المدينة','معرف سياسة الحساب'];
 for(const kind of ['choices','riders','work']){const r=await fetch(base+'/api/excel/'+kind+'/template',{headers:{Cookie:cookie}});assert.equal(r.status,200);checks++;const bytes=Buffer.from(await r.arrayBuffer());assert.equal(bytes.subarray(0,2).toString(),'PK');writeFileSync(path.join(output,kind+'-template.xlsx'),bytes);}
 await req('/api/excel/choices/template',undefined,401,false);
 const excelCamp=await req('/api/campaigns',{title:'Excel synthetic campaign',days:2});
 const excelPayload=bytes=>({filename:'filled-template.xlsx',dataBase64:bytes.toString('base64'),campaignId:excelCamp.id});
 const good=await book(choiceHeaders,[['2000000011','Excel Rider','نظام الراتب الجديد'],['0000000001','Zero Prefix','نظام العمولة السابق']]);writeFileSync(path.join(output,'choices-filled.xlsx'),good);
 const bad=await book(choiceHeaders,[['2000000011','Excel Rider','نظام الراتب الجديد'],['٢٠٠٠٠٠٠٠١٢','Bad Identity','نظام العمولة السابق']]);
 const badSpec=excelPayload(bad);const badPreview=await req('/api/excel/choices/preview',badSpec);assert.equal(badPreview.errors[0].row,3);assert.equal(badPreview.rows.length,1);await req('/api/excel/choices/commit',{...badSpec,fingerprint:badPreview.fingerprint},400);assert.equal((await req('/api/submissions')).length,1);
 const wrong=await book(['الاسم الكامل','رقم الإقامة','نظام العمل'],[['Excel Rider','2000000011','نظام الراتب الجديد']]);await req('/api/excel/choices/preview',excelPayload(wrong),400);
 const twice=await book(choiceHeaders,[['2000000011','Excel Rider','نظام الراتب الجديد'],['2000000011','Excel Rider','نظام العمولة السابق']]);assert.equal((await req('/api/excel/choices/preview',excelPayload(twice))).errors.length,1);
 const goodSpec=excelPayload(good),goodPreview=await req('/api/excel/choices/preview',goodSpec);assert.equal(goodPreview.errors.length,0);assert.equal(goodPreview.rows[1].data.iqama,'0000000001');
 await req('/api/excel/choices/commit',{...goodSpec,fingerprint:'stale'},400);await req('/api/excel/choices/commit',{...goodSpec,fingerprint:goodPreview.fingerprint});await req('/api/excel/choices/commit',{...goodSpec,fingerprint:goodPreview.fingerprint},400);
 const imported=(await req('/api/submissions')).filter(r=>r.campaignId===excelCamp.id);assert.equal(imported.length,2);assert.ok(imported.every(r=>r.status==='pending'));assert.equal((await req('/api/riders')).length,1);
 const riderBook=await book(riderHeaders,[['2000000015','Imported Rider','0501234567','جدة',policy.id]]);
 const riderSpec={filename:'riders.xlsx',dataBase64:riderBook.toString('base64')};const riderPreview=await req('/api/excel/riders/preview',riderSpec);assert.equal(riderPreview.errors.length,0);await req('/api/excel/riders/commit',{...riderSpec,fingerprint:riderPreview.fingerprint});assert.equal((await req('/api/excel/riders/preview',riderSpec)).errors.length,1);
 const sourceNames=await book(riderHeaders,[['2000000021','SingleName','','',''],['2000000022','عبد-الرحيم محمد','','',''],['2000000023','ΑΜΝ Rider','','','']]);const sourceSpec={filename:'source-names.xlsx',dataBase64:sourceNames.toString('base64')};const sourcePreview=await req('/api/excel/riders/preview',sourceSpec);assert.equal(sourcePreview.errors.length,0);await req('/api/excel/riders/commit',{...sourceSpec,fingerprint:sourcePreview.fingerprint});assert.ok((await req('/api/riders')).filter(r=>['2000000021','2000000022','2000000023'].includes(r.iqama)).every(r=>r.policyId===''&&r.status==='needs_review'));
 const unsafe=await book(['الإقامة','نص','المبلغ'],[['0000000001','=HYPERLINK("https://example.invalid","test")',123.45]],[2]);writeFileSync(path.join(output,'typed-safe-export.xlsx'),unsafe);
 console.log(`PASS ${checks} HTTP checks + financial, duplicate, identity, review, debt and stale-preview assertions.`);
}catch(e){console.error(e);console.error(logs.slice(-3000));process.exitCode=1}finally{if(proc.pid&&proc.exitCode===null){const exited=new Promise(r=>proc.once('exit',r));proc.kill();await exited;}rmSync(data,{recursive:true,force:true});}
