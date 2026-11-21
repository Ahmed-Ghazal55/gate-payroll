import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-forms-allco-'));
const port=19431, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || 'dotnet';
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');
const scope=(ids)=>({sponsorIds:ids,platforms:['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'],categoryCodes:[],accountCodes:[]});
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await req('/api/auth/setup',{username:'fp1-admin',password});
 await req('/api/users',{username:'fp1-unscoped',password,role:'Reviewer'});
 await req('/api/users',{username:'fp1-allco',password,role:'Reviewer',customPermissions:['forms.responses.allCompanies']});
 await req('/api/users',{username:'fp1-scoped',password,role:'Reviewer',financialScope:scope(['BW'])});
 await req('/api/riders',{iqama:'2000002901',name:'Synthetic BW',financeSponsorId:'BW',status:'Active',phone:'0500002901'});
 await req('/api/riders',{iqama:'2000002902',name:'Synthetic Other',financeSponsorId:'EX',status:'Active',phone:'0500002902'});
 const campaign=await req('/api/campaigns',{title:'FP1 all-companies campaign',days:2});
 const pub='/api/public/forms/'+campaign.path.split('/').at(-1);
 const bw=await req(pub,{iqama:'2000002901',name:'Synthetic BW',workSystem:'tiers'},200,false);
 const other=await req(pub,{iqama:'2000002902',name:'Synthetic Other',workSystem:'commission'},200,false);

 await req('/api/auth/login',{username:'fp1-unscoped',password});
 let rows=await req('/api/submissions');
 assert.equal(rows.length,0,'unscoped without permission sees no submissions');
 const inbox=await req('/api/responses');
 const inboxRows=Array.isArray(inbox)?inbox:(inbox.items||inbox.rows||[]);
 assert.equal(inboxRows.length,0,'unscoped without permission sees empty responses inbox');
 await req('/api/submissions/'+bw.receipt+'/review',{status:'approved',version:1,identityReviewed:true},404);
 assert.equal((await req('/api/campaigns/'+campaign.id+'/analytics')).totalResponses,0);

 await req('/api/auth/login',{username:'fp1-allco',password});
 rows=await req('/api/submissions');
 assert.equal(rows.length,2,'unscoped with forms.responses.allCompanies sees every company');
 assert.ok(rows.some(r=>r.iqama==='2000002901')&&rows.some(r=>r.iqama==='2000002902'));
 assert.equal((await req('/api/campaigns/'+campaign.id+'/analytics')).totalResponses,2);
 await req('/api/submissions/'+other.receipt+'/review',{status:'approved',version:1,identityReviewed:true});

 await req('/api/auth/login',{username:'fp1-scoped',password});
 rows=await req('/api/submissions');
 assert.equal(rows.length,1,'scoped reviewer still sees only in-scope company');
 assert.equal(rows[0].iqama,'2000002901');
 await req('/api/submissions/'+other.receipt+'/review',{status:'rejected',version:1,identityReviewed:true},404);
 await req('/api/submissions/'+bw.receipt+'/review',{status:'approved',version:1,identityReviewed:true});

 await req('/api/auth/login',{username:'fp1-admin',password});
 rows=await req('/api/submissions');
 assert.equal(rows.length,2,'admin sees all companies');

 // Custom-form analytics + responses: deny / allow
 const custom=await req('/api/forms/builder',{titleAr:'نموذج FP1',titleEn:'FP1',titleUr:'FP1',expiresAt:new Date(Date.now()+864e5).toISOString(),maxSubmissions:10,fields:[{id:'iqama',labelAr:'الإقامة',type:'iqama',required:true},{id:'system',labelAr:'النظام',type:'radio',required:true,options:['tiers','commission']}]});
 const publishedCustom=await req(`/api/forms/builder/${custom.id}/publish`,{});
 const customPub='/api/public/questionnaires/'+publishedCustom.publicUrl.split('/').at(-1);
 await req(customPub,{iqama:'2000002901',system:'tiers'},200,false);
 await req(customPub,{iqama:'2000002902',system:'commission'},200,false);
 await req('/api/auth/login',{username:'fp1-unscoped',password});
 assert.equal((await req('/api/forms/analytics/'+custom.id)).totalResponses,0,'unscoped deny custom analytics');
 assert.equal((await req('/api/forms/responses/'+custom.id)).length,0,'unscoped deny custom responses');
 await req('/api/auth/login',{username:'fp1-allco',password});
 assert.equal((await req('/api/forms/analytics/'+custom.id)).totalResponses,2,'all-companies allow custom analytics');
 assert.equal((await req('/api/forms/responses/'+custom.id)).length,2,'all-companies allow custom responses');
 await req('/api/auth/login',{username:'fp1-scoped',password});
 assert.equal((await req('/api/forms/analytics/'+custom.id)).totalResponses,1,'scoped custom analytics stays company-bound');
 assert.equal((await req('/api/forms/responses/'+custom.id)).length,1,'scoped custom responses stay company-bound');

 // Bulk-approve: unscoped deny (count 0), all-companies allow
 await req('/api/auth/login',{username:'fp1-admin',password});
 const bulkCamp=await req('/api/campaigns',{title:'FP1 bulk scope',days:2});
 const bulkPub='/api/public/forms/'+bulkCamp.path.split('/').at(-1);
 await req(bulkPub,{iqama:'2000002901',name:'Synthetic BW',workSystem:'tiers'},200,false);
 await req(bulkPub,{iqama:'2000002902',name:'Synthetic Other',workSystem:'commission'},200,false);
 await req('/api/auth/login',{username:'fp1-unscoped',password});
 assert.equal((await req('/api/submissions/bulk-approve',{campaignId:bulkCamp.id})).count,0,'unscoped deny bulk-approve');
 assert.equal((await req('/api/submissions')).length,0);
 await req('/api/auth/login',{username:'fp1-allco',password});
 assert.equal((await req('/api/submissions/bulk-approve',{campaignId:bulkCamp.id})).count,2,'all-companies allow bulk-approve');
 assert.equal((await req('/api/submissions')).filter(r=>r.campaignId===bulkCamp.id&&r.status==='approved').length,2);

 // Custom form without iqama field: deny / allow (empty iqama is still gated for unscoped)
 await req('/api/auth/login',{username:'fp1-admin',password});
 const noIqama=await req('/api/forms/builder',{titleAr:'بدون إقامة',titleEn:'No iqama',titleUr:'No iqama',expiresAt:new Date(Date.now()+864e5).toISOString(),maxSubmissions:5,fields:[{id:'note',labelAr:'ملاحظة',type:'text',required:true}]});
 const publishedNoIqama=await req(`/api/forms/builder/${noIqama.id}/publish`,{});
 await req('/api/public/questionnaires/'+publishedNoIqama.publicUrl.split('/').at(-1),{note:'hello'},200,false);
 await req('/api/auth/login',{username:'fp1-unscoped',password});
 assert.equal((await req('/api/forms/analytics/'+noIqama.id)).totalResponses,0,'unscoped deny no-iqama analytics');
 assert.equal((await req('/api/forms/responses/'+noIqama.id)).length,0,'unscoped deny no-iqama responses');
 await req('/api/auth/login',{username:'fp1-allco',password});
 assert.equal((await req('/api/forms/analytics/'+noIqama.id)).totalResponses,1,'all-companies allow no-iqama analytics');
 assert.equal((await req('/api/forms/responses/'+noIqama.id)).length,1,'all-companies allow no-iqama responses');
 await req('/api/auth/login',{username:'fp1-scoped',password});
 assert.equal((await req('/api/forms/analytics/'+noIqama.id)).totalResponses,1,'scoped allow no-iqama (not company-bound)');
 assert.equal((await req('/api/forms/responses/'+noIqama.id)).length,1,'scoped allow no-iqama responses');

 console.log(`PASS ${checks} forms all-companies HTTP checks: submissions, custom analytics/responses, bulk-approve, no-iqama.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
