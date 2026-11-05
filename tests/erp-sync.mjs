// Counts-only sync API and administrator audience; disposable synthetic SQLite only.
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
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-sync-http-'));
let child,base,checks=0;const cookies={};
function as(user){return async(route,body,status=200)=>{
 const response=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookies[user]?{Cookie:cookies[user]}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const raw=await response.text();assert.equal(response.status,status,route+': '+raw.slice(0,120));checks++;if(response.headers.get('set-cookie'))cookies[user]=response.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
};}
try{
 const probe=createServer();await new Promise((resolve,reject)=>{probe.once('error',reject);probe.listen(0,'127.0.0.1',resolve);});base='http://127.0.0.1:'+probe.address().port;await new Promise(resolve=>probe.close(resolve));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,ConnectionStrings__DefaultConnection:'',PAYROLL_DATA_DIR:data},stdio:'ignore'});
 let ready=false;for(let i=0;i<150;i++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready);
 const admin=as('admin'),other=as('other'),anon=as('anon'),password=randomBytes(24).toString('hex');
 await anon('/api/system/erp-sync',undefined,401);await admin('/api/auth/setup',{username:'syn-sync-admin',password});
 await admin('/api/users',{username:'syn-sync-accountant',password,role:'Accountant'});await other('/api/auth/login',{username:'syn-sync-accountant',password});
 await other('/api/system/erp-sync',undefined,403);await admin('/api/system/erp-sync?limit=0',undefined,400);await admin('/api/system/erp-sync?limit=101',undefined,400);await admin('/api/system/erp-sync?limit=bad',undefined,400);
 const db=new DatabaseSync(path.join(data,'payroll.db')),runId='erp-sync:'+randomBytes(16).toString('hex'),noticeId='notice:'+runId,at=new Date().toISOString();
 const put=(kind,row)=>db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(row.id,kind,JSON.stringify(row));
 put('erp_sync_run',{id:runId,startedAt:at,finishedAt:at,status:'refused',refusalCode:'new_kind_requires_acceptance:fuel_card',rawSource:'DO_NOT_EMIT_PRIVATE_PAYLOAD',counts:[{kind:'fuel_card',source:3,new:3,changed:0,unchanged:0,rejected:0,extra:'DO_NOT_EMIT_PRIVATE_PAYLOAD'}]});
 put('sys_notification',{id:noticeId,category:'approvals',targetDepartment:'ALL',audience:'administrators',requiredPermission:'users.manage',titleAr:'Synthetic sync refusal',createdBy:'erp-sync'});db.close();
 const runs=await admin('/api/system/erp-sync?limit=1');assert.equal(runs.length,1);assert.equal(runs[0].counts[0].new,3);assert.ok(!JSON.stringify(runs).includes('DO_NOT_EMIT_PRIVATE_PAYLOAD'));
 assert.ok(!(await other('/api/notifications')).some(n=>n.id===noticeId));assert.ok(!(await other('/api/notifications/center')).items.some(n=>n.id===noticeId));
 await other('/api/notifications/center/'+encodeURIComponent(noticeId)+'/toggle-todo',{},404);
 assert.ok((await admin('/api/notifications')).some(n=>n.id===noticeId));assert.ok((await admin('/api/notifications/center')).items.some(n=>n.id===noticeId));
 console.log(`PASS ERP sync: ${checks} HTTP checks; administrator scope, bounded counts projection and refusal audience.`);
}finally{if(child&&child.exitCode===null&&child.signalCode===null){const exited=once(child,'exit');child.kill();await exited;}rmSync(data,{recursive:true,force:true});}
