import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-iban-test-'));
const port=19371, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');let adminCookie='';
// Release sources may only contain the allow-listed synthetic IBAN; other banks are generated here.
const synthetic=(bank,account)=>{let r=0;for(const c of bank+account+'281000')r=(r*10+Number(c))%97;return 'SA'+String(98-r).padStart(2,'0')+bank+account;};
const IBAN10=synthetic('10','000000000000000002'),IBAN78=synthetic('78','000000000000000003'),IBAN40=synthetic('40','000000000000000004'),IBAN82=synthetic('82','000000000000000005');
async function put(route,body,expected=200){const r=await fetch(base+route,{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify(body)});const d=await r.json();assert.equal(r.status,expected,JSON.stringify(d));checks++;return d;}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await req('/api/auth/setup',{username:'test-admin',password});adminCookie=cookie;
 const campaign=await req('/api/campaigns',{title:'Synthetic IBAN campaign',days:2,kind:'iban'});const work=await req('/api/campaigns',{title:'Synthetic work campaign',days:2});
 await req('/api/campaigns',{title:'Bad kind',days:2,kind:'unknown'},400);await req('/api/campaigns',{title:'Bad fields',days:2,kind:'iban',fields:['city']},400);
 assert.equal((await req('/api/campaigns')).find(c=>c.id===campaign.id).kind,'iban');assert.equal((await req('/api/campaigns')).find(c=>c.id===work.id).kind,'work-choice');
 await put('/api/campaigns/'+campaign.id,{fields:['city']},400);await put('/api/campaigns/'+campaign.id,{title:'Synthetic IBAN campaign (renamed)'});
 const pub='/api/public/forms/'+campaign.path.split('/').at(-1),workPub='/api/public/forms/'+work.path.split('/').at(-1);
 assert.equal((await req(pub,undefined,200,false)).schema,'iban-v1');assert.equal((await req(workPub,undefined,200,false)).schema,'work-choice-v1');
 const form={iqama:'2000000901',name:'Synthetic Rider',phone:'0500000901',iban:'sa03 8000 0000 6080 1016 7519',platforms:['jahez','keeta']};
 assert.equal((await req(pub+'/lookup',{iqama:form.iqama},200,false)).alreadySubmitted,false);
 for(const bad of [{...form,iban:'SA0080000000608010167519'},{...form,iban:'SA948000'},{...form,bankCode:'10'},{...form,platforms:['uber']},{...form,phone:''},{...form,workSystem:'tiers'}])await req(pub,bad,400,false);
 await req(workPub,{iqama:'2000000902',name:'Synthetic Other',workSystem:'tiers',iban:'SA0380000000608010167519'},400,false);
 const receipt=await req(pub,form,200,false);assert.equal(receipt.ibanLast4,'7519');assert.equal(receipt.bankCode,'80');assert.deepEqual(receipt.platforms,['keeta','jahez']);assert.equal(receipt.status,'pending');assert.equal(receipt.iban,undefined);
 const duplicate=await req(pub,{...form,name:'Another Person'},409,false);assert.equal(duplicate.alreadySubmitted,true);
 for(const key of ['iban','phone','history','id','receipt'])assert.equal(duplicate[key],undefined);assert.ok(!JSON.stringify(duplicate).includes('500000901'));
 assert.deepEqual(await req(pub+'/lookup',{iqama:form.iqama},200,false),duplicate);
 let rows=await req('/api/submissions');let row=rows.find(r=>r.id===receipt.receipt);assert.equal(row.iban,'SA0380000000608010167519');assert.equal(row.bank,'RJHI');assert.equal(row.phone,'+966500000901');assert.equal(row.schema,'iban-v1');
 await req(pub+'/edit',{...form,iban:IBAN10,version:2},409,false);
 const edited=await req(pub+'/edit',{...form,iban:IBAN10,city:'Riyadh',platforms:[],version:1},200,false);assert.equal(edited.version,2);assert.equal(edited.isEdited,true);assert.equal(edited.bankCode,'10');
 assert.equal((await req(pub+'/edit',{...form,iban:IBAN10,city:'Riyadh',platforms:[],version:2},200,false)).version,2);
 row=(await req('/api/submissions')).find(r=>r.id===receipt.receipt);assert.equal(row.history.length,1);assert.equal(row.history[0].iban,'SA0380000000608010167519');assert.deepEqual(row.history[0].platforms,['keeta','jahez']);assert.equal(row.iban,IBAN10);assert.equal(row.status,'pending');
 await req('/api/submissions/bulk-approve',{campaignId:campaign.id},400);
 await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',version:2,identityReviewed:true},400);
 const reviewed=await req('/api/submissions/'+receipt.receipt+'/review',{status:'approved',version:2,identityReviewed:true,ibanOwnershipReviewed:true});assert.equal(reviewed.status,'approved');
 const riders=await req('/api/riders');assert.ok(!JSON.stringify(riders).includes(IBAN10),'approval must not write the rider IBAN');
 const analytics=await req('/api/campaigns/'+campaign.id+'/analytics');assert.equal(analytics.kind,'iban');assert.equal(analytics.approved,1);assert.equal(analytics.bank[0].value,'NCBK');
 const stc=await req(pub,{iqama:'2000000903',name:'Synthetic Third',phone:'+966500000903',iban:IBAN78},200,false);assert.equal(stc.bankCode,'78');
 for(const iban of [IBAN40,IBAN82,synthetic('99','000000000000000006')])await req(pub,{iqama:'2000000905',name:'Synthetic Fourth',phone:'+966500000905',iban},400,false);
 await req('/api/campaigns/'+campaign.id+'/close',{});await req(pub,{...form,iqama:'2000000904'},400,false);
 console.log(`PASS ${checks} IBAN form HTTP checks: kind, paste normalization, mod97, bank match, apps, masked lookup, edit history, stale version, review without rider IBAN write, close.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
