import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

// H4: call the monthly preview URL exactly as web `getRiderStatementPreview` builds it (`approvedOnly=1`).
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-stmt-preview-'));
const port=19430, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin=process.env.DOTNET_PATH||'dotnet';
const exeName=process.platform==='win32'?'Payroll.Api.exe':'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');
const iqama='2000002901';
const period='2026-10';
// Same query string as web/src/lib/api.ts getRiderStatementPreview.
const previewPath=`/api/finance/statement/rider/${encodeURIComponent(iqama)}/monthly?period=${encodeURIComponent(period)}&approvedOnly=1`;
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await req('/api/auth/setup',{username:'stmt-admin',password});
 const scope=(ids)=>({sponsorIds:ids,platforms:['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'],categoryCodes:[],accountCodes:[]});
 await req('/api/riders',{iqama,name:'Synthetic Preview',financeSponsorId:'BW',status:'Active',phone:'0500002901'});
 await req('/api/auth/login',{username:'stmt-admin',password});
 const adminPreview=await req(previewPath);
 assert.equal(adminPreview.approvedOnly,true);
 assert.equal(adminPreview.header?.rider,iqama);
 console.log(`PASS ${checks} statements preview HTTP: approvedOnly=1 binds and returns 200.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
