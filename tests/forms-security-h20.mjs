import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-forms-h20-'));
const port=19428, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || 'dotnet';
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await req('/api/auth/setup',{username:'h20-admin',password});
 const scope=(ids)=>({sponsorIds:ids,platforms:['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'],categoryCodes:[],accountCodes:[]});
 await req('/api/users',{username:'h20-reviewer',password,role:'Reviewer',financialScope:scope(['BW'])});
 await req('/api/users',{username:'h20-iban',password,role:'Reviewer',customPermissions:['forms.view','forms.review','riders.iban.view'],financialScope:scope(['BW'])});
 // Seed riders in two companies.
 await req('/api/riders',{iqama:'2000002801',name:'Synthetic BW',financeSponsorId:'BW',status:'Active',phone:'0500002801'});
 await req('/api/riders',{iqama:'2000002802',name:'Synthetic Other',financeSponsorId:'EX',status:'Active',phone:'0500002802'});
 const campaign=await req('/api/campaigns',{title:'H20 IBAN campaign',days:2,kind:'iban'});
 const pub='/api/public/forms/'+campaign.path.split('/').at(-1);
 const formBw={iqama:'2000002801',name:'Synthetic BW',phone:'0500002801',iban:'SA0380000000608010167519',bankCode:'80',city:'الرياض',platforms:['keeta']};
 const formOther={iqama:'2000002802',name:'Synthetic Other',phone:'0500002802',iban:'SA0380000000608010167519',bankCode:'80',city:'الرياض',platforms:['keeta']};
 const receiptBw=await req(pub,formBw,200,false);const receiptOther=await req(pub,formOther,200,false);
 assert.equal(typeof receiptBw.editToken,'string');
 // Public edit: missing / wrong token → 404; correct token → 200.
 await req(pub+'/edit',{...formBw,version:1},404,false);
 await req(pub+'/edit',{...formBw,editToken:'f'.repeat(64),version:1},404,false);
 const edited=await req(pub+'/edit',{...formBw,editToken:receiptBw.editToken,city:'جدة',version:1},200,false);assert.equal(edited.version,2);

 await req('/api/auth/login',{username:'h20-reviewer',password});
 let rows=await req('/api/submissions');
 assert.equal(rows.length,1);assert.equal(rows[0].iqama,'2000002801');
 assert.ok(rows[0].iban==='••••7519'||rows[0].iban.endsWith('7519')&&rows[0].iban.includes('•'),'IBAN masked without riders.iban.view');
 assert.ok(!String(rows[0].iban).startsWith('SA03')||rows[0].iban.startsWith('••••'),'full IBAN hidden');
 if(rows[0].history?.length)assert.ok(String(rows[0].history[0].iban||'').includes('•')||!rows[0].history[0].iban?.startsWith('SA'),'history IBAN masked');
 const inbox=await req('/api/responses');
 const inboxRows=Array.isArray(inbox)?inbox:(inbox.items||inbox.rows||[]);
 assert.ok(inboxRows.every(r=>r.iqama!=='2000002802'),'responses inbox hides other company');
 await req('/api/submissions/'+receiptOther.receipt+'/review',{status:'approved',version:1,identityReviewed:true,ibanOwnershipReviewed:true},404);

 await req('/api/auth/login',{username:'h20-iban',password});
 rows=await req('/api/submissions');
 const mine=rows.find(r=>r.iqama==='2000002801');
 assert.ok(mine);assert.equal(mine.iban,'SA0380000000608010167519','IBAN unmasked with riders.iban.view');
 assert.ok(!rows.some(r=>r.iqama==='2000002802'),'other company still hidden');
 console.log(`PASS ${checks} forms H20 HTTP checks: editToken, IBAN mask by permission, company scope 404.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
