// Track M R6 (BR §25.8): rider asks from the app → current supervisor converts or declines. Synthetic data only.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {createServer} from 'node:net';
import assert from 'node:assert/strict';
import {seedAccountingCash} from './financial-fixtures.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-asks-'));let child,base,checks=0;const jars={};
async function start(){
 const probe=createServer();await new Promise((ok,no)=>{probe.once('error',no);probe.listen(0,'127.0.0.1',ok);});
 base='http://127.0.0.1:'+probe.address().port;await new Promise(ok=>probe.close(ok));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('API timeout');
}
function as(user,token){return async(route,body,status=200)=>{
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(jars[user]?{Cookie:jars[user]}:{}),...(token?{Authorization:'Bearer '+token}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const text=await r.text();assert.equal(r.status,status,`${user} ${route}: ${text.slice(0,200)}`);checks++;
 if(r.headers.get('set-cookie'))jars[user]=r.headers.get('set-cookie').split(';')[0];return text?JSON.parse(text):null;};}
const pw=()=>randomBytes(18).toString('hex');
try{
 await start();const admin=as('admin');await admin('/api/auth/setup',{username:'syn-ask-admin',password:pw()});
 await seedAccountingCash(admin);
 const iqama='2000000751',other='2000000752';
 for(const q of [iqama,other])await admin('/api/riders',{iqama:q,name:'Ask Synthetic',phone:'0501234567',financeSponsorId:'BW',status:'Active'});
 const users={supervisor:['advances.request'],accountant:['advances.review'],outsider:['advances.request']};
 for(const [name,perms] of Object.entries(users)){const p=pw();await admin('/api/users',{username:'syn-'+name,password:p,role:'Reviewer',customPermissions:perms});await as(name)('/api/auth/login',{username:'syn-'+name,password:p});}
 const sup=as('supervisor'),acc=as('accountant'),out=as('outsider');
 await admin('/api/org/memberships',{userId:'syn-accountant',department:'FIN',rank:'A',validFrom:'2026-01-01'});
 await admin('/api/org/routing',{operation:'advance.review',department:'FIN',minRank:'B',users:['syn-accountant'],validFrom:'2026-01-01'});
 async function token(q){const g=await admin('/api/inquiry-access',{iqama:q,scope:'mobile',identityReviewed:true});return (await as('anon')('/api/rider-mobile/login',{iqamaNo:q,phoneLast4:'4567',code:g.code,language:'ar'})).accessToken;}
 const rider=as('rider-a',await token(iqama)),otherRider=as('rider-b',await token(other)),anon=as('nobody');
 await anon('/api/rider-mobile/requests',undefined,401);
 await rider('/api/rider-mobile/requests',{type:'cash',amount:300,reason:'Synthetic need'},409); // no supervisor yet
 await admin('/api/org/supervision',{supervisorUserId:'syn-supervisor',riderIqama:iqama,validFrom:'2026-01-01'});
 // Ask → decline with a reason the rider sees.
 const a1=await rider('/api/rider-mobile/requests',{type:'cash',amount:300,reason:'Synthetic need'});assert.equal(a1.status,'open');
 await rider('/api/rider-mobile/requests',{type:'cash',amount:100,reason:'Second synthetic'},409);
 assert.ok((await sup('/api/notifications')).some(n=>n.entityId===a1.id&&n.requiresAction&&n.actionStatus==='open'));
 assert.ok(!(await out('/api/notifications')).some(n=>n.entityId===a1.id));
 assert.equal((await sup('/api/advance-requests/rider-asks?status=open')).length,1);
 assert.equal((await out('/api/advance-requests/rider-asks')).length,0);
 await out(`/api/advance-requests/rider-asks/${a1.id}/decline`,{note:'not mine'},404);
 await acc(`/api/advance-requests/rider-asks/${a1.id}/decline`,{note:'no permission'},403);
 await sup(`/api/advance-requests/rider-asks/${a1.id}/decline`,{note:''},400);
 await sup(`/api/advance-requests/rider-asks/${a1.id}/decline`,{note:'Synthetic: ask again next week'});
 const seen=(await rider('/api/rider-mobile/requests'))[0];assert.equal(seen.status,'declined');assert.equal(seen.note,'Synthetic: ask again next week');
 assert.equal((await sup('/api/notifications')).find(n=>n.entityId===a1.id).actionStatus,'decided');
 assert.equal((await otherRider('/api/rider-mobile/requests')).length,0);
 await otherRider(`/api/rider-mobile/requests/${a1.id}/cancel`,{},404);
 // Ask → convert (supervisor edits the amount) → formal §25 request reaches the accountant.
 const a2=await rider('/api/rider-mobile/requests',{type:'bank',amount:500,reason:'Synthetic rent'});
 const conv=await sup(`/api/advance-requests/rider-asks/${a2.id}/convert`,{amount:400});
 assert.equal(conv.advanceRequest.status,'under_review');assert.equal(conv.advanceRequest.amount,400);assert.equal(conv.advanceRequest.requestedBy,'syn-supervisor');
 await sup(`/api/advance-requests/rider-asks/${a2.id}/convert`,{},409);
 assert.ok((await acc('/api/notifications')).some(n=>n.entityId===conv.advanceRequest.id&&n.requiresAction));
 const mine=(await rider('/api/rider-mobile/requests')).find(x=>x.id===a2.id);
 assert.equal(mine.status,'converted');assert.equal(mine.advanceStatus,'under_review');assert.equal(mine.amount,400);
 assert.ok(!('decisions' in mine)&&!('vaultCode' in mine),'rider sees the stage only');
 await rider(`/api/rider-mobile/requests/${a2.id}/cancel`,{},409);
 await rider('/api/rider-mobile/requests',{type:'cash',amount:50,reason:'Synthetic third'},409); // formal request still open
 // Ask → cancel by the rider closes the supervisor task.
 await acc(`/api/advance-requests/${conv.advanceRequest.id}/review`,{action:'reject',note:'Synthetic rejection'});
 assert.equal((await rider('/api/rider-mobile/requests')).find(x=>x.id===a2.id).advanceStatus,'rejected');
 const a3=await rider('/api/rider-mobile/requests',{type:'cash',amount:60,reason:'Synthetic fourth'});
 assert.equal((await rider(`/api/rider-mobile/requests/${a3.id}/cancel`,{})).status,'cancelled');
 assert.equal((await sup('/api/notifications')).find(n=>n.entityId===a3.id).actionStatus,'decided');
 console.log(`PASS rider asks: ${checks} HTTP checks`);
}finally{if(child&&child.exitCode===null){const e=once(child,'exit');child.kill();await e;}rmSync(data,{recursive:true,force:true});}
