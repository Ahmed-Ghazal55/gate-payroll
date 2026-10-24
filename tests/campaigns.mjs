import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-campaign-test-'));
const port=19355, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');let adminCookie='';
async function put(route,body,expected=200){const r=await fetch(base+route,{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify(body)});const d=await r.json();assert.equal(r.status,expected,JSON.stringify(d));checks++;return d;}
async function removeForm(route,body,expected=200,auth=true){const r=await fetch(base+route,{method:'DELETE',headers:{'Content-Type':'application/json',...(auth?{Cookie:cookie}:{})},body:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,raw);checks++;return JSON.parse(raw);}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 assert.equal((await req('/health',undefined,200,false)).version,'0.5.5');
 await req('/api/auth/setup',{username:'test-admin',password});adminCookie=cookie;
 for(const [username,role] of [['test-reviewer','Reviewer'],['test-accountant','Accountant']])await req('/api/users',{username,password,role});
 const campaign=await req('/api/campaigns',{title:'Editable campaign',days:2}),other=await req('/api/campaigns',{title:'Separate campaign',days:2});
 const pub='/api/public/forms/'+campaign.path.split('/').at(-1),otherPub='/api/public/forms/'+other.path.split('/').at(-1);
 const first={iqama:'2000000991',name:'Synthetic Alpha',workSystem:'tiers'},second={iqama:'2000000992',name:'Synthetic Beta',workSystem:'commission'};
 await put('/api/campaigns/'+campaign.id,{title:'Updated campaign',fields:['city','phone']});
 const publicSchema=await req(pub,undefined,200,false);assert.deepEqual(publicSchema.fields,['city','phone']);assert.equal(publicSchema.title,'Updated campaign');
 assert.equal((await req('/api/campaigns')).find(x=>x.id===campaign.id).publicUrl,campaign.publicUrl);
 await put('/api/campaigns/'+campaign.id,{fields:['iban']},400);await put('/api/campaigns/'+campaign.id,{fields:['city','city']},400);await put('/api/campaigns/'+campaign.id,{expiresAt:'invalid'},400);
 await req(pub,first,400,false);await req(pub,{...first,city:'Riyadh',phone:'bad'},400,false);
 const a=await req(pub,{...first,city:'Riyadh',phone:'+966500000001'},200,false),b=await req(pub,{...second,city:'Jeddah',phone:'+966500000002'},200,false);
 const untouched=await req(otherPub,first,200,false);
 const prior=await req(pub+'/lookup',{iqama:first.iqama},200,false);assert.equal(prior.city,'Riyadh');assert.equal(prior.phone,'+966500000001');
 await req('/api/riders',{iqama:first.iqama,name:first.name,city:'Directory City',engagementType:'SponsoredInternal'});
 await req('/api/riders',{iqama:second.iqama,name:second.name,engagementType:'External'});
 await req(pub+'/edit',{...first,city:'Dammam',phone:'+966500000001',version:1},200,false);
 const changed=(await req('/api/submissions')).find(r=>r.id===a.receipt);assert.equal(changed.history[0].city,'Riyadh');assert.equal(changed.history[0].phone,'+966500000001');assert.equal(changed.version,2);assert.equal(changed.status,'pending');
 let analytics=await req('/api/campaigns/'+campaign.id+'/analytics');assert.equal(analytics.totalResponses,2);assert.equal(analytics.pending,2);assert.equal(analytics.edited,1);assert.equal(analytics.engagementType.find(x=>x.value==='External').percentage,50);assert.equal(analytics.city.find(x=>x.value==='Dammam').count,1);assert.equal(analytics.workSystem.find(x=>x.value==='tiers').count,1);assert.equal(analytics.recent.length,2);
 await req('/api/campaigns/'+campaign.id+'/analytics',undefined,401,false);
 async function del(id,status=200){const r=await fetch(base+'/api/submissions/'+id,{method:'DELETE',headers:{Cookie:cookie}});assert.equal(r.status,status,await r.text());checks++;}
 for(const role of ['reviewer','accountant']){
  await req('/api/auth/login',{username:'test-'+role,password});await del(a.receipt,403);await req('/api/submissions/bulk-delete',{campaignId:campaign.id,confirm:true},403);await put('/api/campaigns/'+campaign.id,{title:'Forbidden'},403);await req('/api/campaigns/'+campaign.id+'/delete-preview',undefined,403);await removeForm('/api/campaigns/'+campaign.id,{confirm:true,fingerprint:'not-authorized'},403);await req('/api/forms/builder/missing/delete-preview',undefined,403);await removeForm('/api/forms/builder/missing',{confirm:true,fingerprint:'not-authorized'},403);
  if(role==='accountant')await req('/api/submissions/bulk-approve',{campaignId:campaign.id},403);
  else {await req('/api/campaigns/'+campaign.id+'/analytics');await req('/api/submissions/bulk-approve',{campaignId:campaign.id,ids:[a.receipt,'missing']},400);assert.equal((await req('/api/submissions')).find(r=>r.id===a.receipt).status,'pending');await req('/api/submissions/bulk-approve',{campaignId:campaign.id,ids:[a.receipt,b.receipt],versions:{[a.receipt]:1,[b.receipt]:1}},409);assert.equal((await req('/api/submissions/bulk-approve',{campaignId:campaign.id})).count,2);assert.equal((await req('/api/submissions/bulk-approve',{campaignId:campaign.id})).count,0);}
 }
 cookie=adminCookie;
 assert.equal((await req('/api/submissions')).find(x=>x.id===untouched.receipt).status,'pending');
 await del(a.receipt);await del(a.receipt,404);
 const directory=(await req('/api/riders')).find(x=>x.iqama===first.iqama);assert.equal(directory.workPreference,'');assert.equal(directory.city,'Dammam');assert.equal(directory.engagementType,'SponsoredInternal');
 assert.equal((await req(pub+'/lookup',{iqama:first.iqama},200,false)).alreadySubmitted,false);
 const replacement=await req(pub,{...first,city:'Riyadh',phone:'+966500000001'},200,false);assert.notEqual(replacement.receipt,a.receipt);assert.equal(replacement.version,1);assert.equal(replacement.isEdited,false);
 await req('/api/submissions/bulk-delete',{campaignId:campaign.id,confirm:false},400);await req('/api/submissions/bulk-delete',{confirm:true},400);await req('/api/submissions/bulk-delete',{campaignId:campaign.id,confirm:true,ids:[untouched.receipt]},400);
 assert.equal((await req('/api/submissions/bulk-delete',{campaignId:campaign.id,confirm:true})).count,2);
 assert.equal((await req('/api/campaigns/'+campaign.id+'/analytics')).totalResponses,0);assert.equal((await req('/api/submissions')).length,1);
 assert.equal((await req(pub+'/lookup',{iqama:second.iqama},200,false)).alreadySubmitted,false);
 const audit=await req('/api/audit');assert.equal(audit.filter(x=>x.action==='submission.deleted').length,3);assert.ok(audit.some(x=>x.action==='campaign.updated'&&x.actor==='test-admin'));assert.ok(audit.some(x=>x.action==='submission.bulk_approved'&&x.actor==='test-reviewer'));
 await put('/api/campaigns/'+campaign.id,{fields:[],status:'closed'});await req(pub,undefined,400,false);await put('/api/campaigns/'+campaign.id,{status:'open',expiresAt:new Date(Date.now()+864e5).toISOString()});await req(pub,first,200,false);
 // Canonical v3 Excel fields remain compatible with the campaign schema and safe import workflow.
 await put('/api/campaigns/'+campaign.id,{fields:['city','phone']});
 const workbook=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({headers:['رقم الإقامة','الاسم الكامل','نظام العمل','المدينة','الجوال'],rows:[['2000000993','Imported Synthetic','نظام العمولة السابق','Jeddah','+966500000003']],numericColumns:[]})});assert.equal(workbook.status,200);checks++;
 const batch={filename:'choices-v3.xlsx',campaignId:campaign.id,dataBase64:Buffer.from(await workbook.arrayBuffer()).toString('base64')};const preview=await req('/api/excel/choices/preview',batch);assert.deepEqual(preview.errors,[]);assert.equal(preview.rows[0].data.city,'Jeddah');assert.equal(preview.rows[0].data.phone,'+966500000003');await req('/api/excel/choices/commit',{...batch,fingerprint:preview.fingerprint});
 const imported=(await req('/api/submissions')).find(x=>x.iqama==='2000000993');assert.equal(imported.city,'Jeddah');assert.equal(imported.status,'pending');assert.equal((await req('/api/campaigns/'+campaign.id+'/analytics')).totalResponses,2);
 // Whole-form deletion: explicit confirmation, stale snapshots, child cleanup and link revocation.
 let deletion=await req('/api/campaigns/'+campaign.id+'/delete-preview');assert.equal(deletion.responseCount,2);
 await removeForm('/api/campaigns/'+campaign.id,{confirm:false,fingerprint:deletion.fingerprint},400);await removeForm('/api/campaigns/'+campaign.id,{confirm:true},400);await removeForm('/api/campaigns/'+campaign.id,{confirm:true,fingerprint:deletion.fingerprint},401,false);
 await req(pub,{iqama:'2000000994',name:'Concurrent Synthetic',workSystem:'tiers',city:'Dammam',phone:'+966500000004'},200,false);
 await removeForm('/api/campaigns/'+campaign.id,{confirm:true,fingerprint:deletion.fingerprint},409);assert.equal((await req('/api/campaigns/'+campaign.id+'/analytics')).totalResponses,3);
 await req('/api/submissions/bulk-approve',{campaignId:other.id});
 deletion=await req('/api/campaigns/'+campaign.id+'/delete-preview');assert.equal((await removeForm('/api/campaigns/'+campaign.id,{confirm:true,fingerprint:deletion.fingerprint})).deletedResponses,3);
 await req(pub,undefined,404,false);await req(pub,{...first,city:'Riyadh',phone:'+966500000001'},404,false);await req('/api/campaigns/'+campaign.id+'/analytics',undefined,404);await removeForm('/api/campaigns/'+campaign.id,{confirm:true,fingerprint:deletion.fingerprint},404);
 assert.ok(!(await req('/api/campaigns')).some(f=>f.id===campaign.id));assert.equal((await req('/api/submissions')).length,1);assert.equal((await req('/api/riders')).find(r=>r.iqama===first.iqama).workPreference,'tiers');
 // Deleting the remaining approved campaign clears its attributable preference without deleting directory data.
 deletion=await req('/api/campaigns/'+other.id+'/delete-preview');await removeForm('/api/campaigns/'+other.id,{confirm:true,fingerprint:deletion.fingerprint});assert.equal((await req('/api/riders')).find(r=>r.iqama===first.iqama).workPreference,'');
 const schema={titleAr:'Custom delete test',expiresAt:new Date(Date.now()+864e5).toISOString(),maxSubmissions:10,fields:[{id:'choice',labelAr:'Choice',type:'radio',required:true,options:['yes','no']}]};
 const custom=await req('/api/forms/builder',schema),draft=await req('/api/forms/builder',{...schema,titleAr:'Empty draft'});
 const publishedCustom=await req('/api/forms/builder/'+custom.id+'/publish',{});const customPub='/api/public/questionnaires/'+publishedCustom.publicUrl.split('/').at(-1);const customReply=await req(customPub,{choice:'yes'},200,false);
 let customDelete=await req('/api/forms/builder/'+custom.id+'/delete-preview');assert.equal(customDelete.responseCount,1);await removeForm('/api/forms/builder/'+custom.id,{confirm:true,fingerprint:customDelete.fingerprint});
 await req(customPub,undefined,404,false);await req(customPub,{choice:'yes'},404,false);assert.deepEqual(await req('/api/forms/responses/'+custom.id),[]);assert.ok(!(await req('/api/forms/catalog')).some(f=>f.id===custom.id));
 customDelete=await req('/api/forms/builder/'+draft.id+'/delete-preview');assert.equal(customDelete.responseCount,0);await removeForm('/api/forms/builder/'+draft.id,{confirm:true,fingerprint:customDelete.fingerprint});
 const deletionAudit=await req('/api/audit');assert.ok(deletionAudit.some(a=>a.action==='campaign.deleted'&&a.entityId===campaign.id&&a.actor==='test-admin'));assert.ok(deletionAudit.some(a=>a.action==='builder.deleted'&&a.entityId===custom.id));assert.ok(deletionAudit.some(a=>a.action==='builder.response_deleted'&&a.entityId===customReply.receipt));assert.ok(deletionAudit.some(a=>a.action==='builder.response_received'&&a.entityId===customReply.receipt));
 console.log(`PASS ${checks} v0.5.4 HTTP checks: hard deletion and reset, campaign-scoped atomic bulk actions and RBAC, optional fields/history, campaign link retention and analytics.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
