import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-submission-test-'));
const port=19354, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');let adminCookie='';
async function put(route,body,expected=200){const r=await fetch(base+route,{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify(body)});const d=await r.json();assert.equal(r.status,expected,JSON.stringify(d));checks++;return d;}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await req('/api/auth/setup',{username:'test-admin',password});adminCookie=cookie;
 await req('/api/users',{username:'test-reviewer',password,role:'Reviewer'});
 await req('/api/users',{username:'test-accountant',password,role:'Accountant'});
 const campaign=await req('/api/campaigns',{title:'Response edit contracts',days:2}),other=await req('/api/campaigns',{title:'Isolated campaign',days:2});
 const pub='/api/public/forms/'+campaign.path.split('/').at(-1),otherPub='/api/public/forms/'+other.path.split('/').at(-1);
 const form={iqama:'2000000888',name:'Synthetic Rider',workSystem:'tiers'};
 assert.equal((await req(pub+'/lookup',{iqama:form.iqama},200,false)).alreadySubmitted,false);
 await req(pub+'/lookup',{iqama:'٢٠٠٠٠٠٠٨٨٨'},400,false);await req(pub+'/lookup',{iqama:form.iqama,phone:'extra'},400,false);
 const receipt=await req(pub,form,200,false);assert.equal(receipt.version,1);assert.equal(receipt.editToken,undefined);assert.equal(receipt.status,'pending');assert.ok(!Number.isNaN(Date.parse(receipt.submittedAt)));assert.equal(receipt.name,form.name);
 const duplicate=await req(pub,{...form,name:'Another Person'},409,false);assert.equal(duplicate.alreadySubmitted,true);assert.equal(duplicate.name,form.name);assert.equal(duplicate.currentChoice,'tiers');assert.equal(duplicate.iqama,form.iqama);assert.equal(duplicate.version,1);
 for(const key of ['receipt','editToken','history','reviewNote','phone','iban','policyId','debt'])assert.equal(duplicate[key],undefined);
 assert.deepEqual(await req(pub+'/lookup',{iqama:form.iqama},200,false),duplicate);
 await req(otherPub+'/edit',form,404,false);await req(pub+'/edit',{...form,iqama:'2000000889'},404,false);await req(pub+'/edit',{...form,receipt:'wrong'},400,false);
 await req('/api/auth/login',{username:'test-accountant',password});await req('/api/submissions',undefined,403);await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved'},403);
 await req('/api/auth/login',{username:'test-reviewer',password});
 let reviewed=await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',version:1});assert.equal(reviewed.reviewNote,'');assert.equal(reviewed.reviewedBy,'test-reviewer');
 // Editing needs neither a receipt nor an edit code, including for previously approved replies.
 let edited=await req(pub+'/edit',{...form,workSystem:'commission',version:1},200,false);assert.equal(edited.version,2);assert.equal(edited.isEdited,true);assert.equal(edited.receipt,receipt.receipt);assert.equal(edited.status,'pending');assert.equal(edited.editToken,undefined);
 let rows=await req('/api/submissions');assert.equal(rows.length,1);let row=rows[0];assert.equal(row.history.length,1);assert.equal(row.history[0].workSystem,'tiers');assert.equal(row.history[0].status,'approved');assert.equal(row.history[0].reviewedBy,'test-reviewer');assert.equal(row.history[0].version,1);assert.ok(row.history[0].submittedAt);assert.equal(row.reviewedBy,undefined);
 await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',version:1},409);await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',identityReviewed:false,version:2},400);
 await req('/api/submissions/'+receipt.receipt+'/review',{status:'rejected',version:2});
 edited=await req(pub+'/edit',{...form,version:2,name:'Changed Rider'},200,false);assert.equal(edited.version,3);
 row=(await req('/api/submissions'))[0];assert.equal(row.history.length,2);assert.equal(row.history[1].status,'rejected');assert.equal(row.history[1].workSystem,'commission');assert.equal(row.history[0].name,'Synthetic Rider');
 await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',note:'Optional checked note',version:3});
 edited=await req(pub+'/edit',{...form,version:3,name:'Changed Rider'},200,false);assert.equal(edited.version,3);assert.equal(edited.status,'approved');row=(await req('/api/submissions'))[0];assert.equal(row.history.length,2);assert.equal(row.reviewNote,'Optional checked note');
 await req(pub+'/edit',{...form,version:2},409,false);await req(pub+'/edit',{...form,version:3,name:'Bad123 Name'},400,false);
 const results=await Promise.all(['Concurrent Alpha','Concurrent Beta'].map(name=>fetch(base+pub+'/edit',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...form,version:3,name})})));
 assert.deepEqual(results.map(r=>r.status).sort(),[200,409]);checks+=2;
 // Legacy/simple callers may omit version; both sequential writes must be archived, never lost.
 const simple=await Promise.all(['Simple Alpha','Simple Beta'].map(name=>fetch(base+pub+'/edit',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...form,name})})));
 assert.ok(simple.every(r=>r.status===200));checks+=2;row=(await req('/api/submissions'))[0];assert.equal(row.version,6);assert.equal(row.history.length,5);assert.deepEqual(row.history.map(h=>h.version),[1,2,3,4,5]);assert.equal(row.history[2].reviewNote,'Optional checked note');
 const duplicates=await Promise.all(Array.from({length:2},()=>fetch(base+pub,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(form)})));assert.ok(duplicates.every(r=>r.status===409));checks+=2;assert.equal((await req('/api/submissions')).length,1);
 const xlsx=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({headers:['الإقامة','التعديل','النسخة','ملاحظة المراجعة'],rows:[[row.iqama,'رد معدل',row.version,row.reviewNote||'']],numericColumns:[2]})});assert.equal(xlsx.status,200);assert.equal(Buffer.from(await xlsx.arrayBuffer()).subarray(0,2).toString(),'PK');checks++;
 cookie=adminCookie;const audit=await req('/api/audit');assert.equal(audit.filter(x=>x.action==='submission.edited').length,5);assert.ok(audit.some(x=>x.action==='submission.approved'&&x.actor==='test-reviewer'));assert.ok(audit.some(x=>x.action==='submission.rejected'&&x.actor==='test-reviewer'));
 const firstRace=await Promise.all(Array.from({length:2},()=>fetch(base+pub,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...form,iqama:'2000000889'})})));assert.deepEqual(firstRace.map(r=>r.status).sort(),[200,409]);checks+=2;assert.equal((await req('/api/submissions')).length,2);
 // Imported replies participate in the same Iqama-only editing flow.
 const workbook=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({headers:['رقم الإقامة','الاسم الكامل','نظام العمل'],rows:[['2000000890','Legacy Rider','نظام الراتب الجديد']],numericColumns:[]})});assert.equal(workbook.status,200);checks++;
 const batch={filename:'legacy.xlsx',campaignId:campaign.id,dataBase64:Buffer.from(await workbook.arrayBuffer()).toString('base64')};const preview=await req('/api/excel/choices/preview',batch);assert.deepEqual(preview.errors,[]);await req('/api/excel/choices/commit',{...batch,fingerprint:preview.fingerprint});
 const imported=await req(pub+'/lookup',{iqama:'2000000890'},200,false);assert.equal(imported.name,'Legacy Rider');const updated=await req(pub+'/edit',{iqama:'2000000890',name:'Legacy Updated',workSystem:'commission'},200,false);assert.equal(updated.version,2);
 // Keep the old receipt recovery route usable for already-loaded v0.5.1 pages.
 assert.equal((await req(pub+'/resume',{receipt:receipt.receipt},200,false)).iqama,form.iqama);
 await req('/api/campaigns/'+campaign.id+'/close',{});
 await req(pub+'/edit',{...form,version:6},400,false);await req(pub+'/lookup',{iqama:form.iqama},400,false);await req(pub,form,400,false);
 console.log(`PASS ${checks} response HTTP checks: Iqama-only lookup/edit, canonical receipts, quick reviews/RBAC, history and notes, duplicate and version races, Excel, closed campaigns.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
