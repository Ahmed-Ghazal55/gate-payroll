// BR §25 advance requests over HTTP: synthetic users and rider only.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
import {seedAccountingCash} from './financial-fixtures.mjs';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-adv-')),base='http://127.0.0.1:19491';
let child,checks=0;const jars={};
async function start(){
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('API not ready');
}
async function stop(){if(child&&child.exitCode===null){const ended=once(child,'exit');child.kill();await ended;}}
function as(user){return async(route,body,status=200,method=body===undefined?'GET':'POST')=>{
 const r=await fetch(base+route,{method,headers:{...(jars[user]?{Cookie:jars[user]}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const text=await r.text();assert.equal(r.status,status,`${user} ${method} ${route}: ${text.slice(0,200)}`);checks++;
 if(r.headers.get('set-cookie'))jars[user]=r.headers.get('set-cookie').split(';')[0];return text?JSON.parse(text):null;};}
const pw=()=>randomBytes(18).toString('hex');
try{
 await start();
 const admin=as('admin');const adminPw=pw();await admin('/api/auth/setup',{username:'syn-adv-admin',password:adminPw});
 await seedAccountingCash(admin);
 const iqama='2000000611';await admin('/api/riders',{iqama,name:'Advance Synthetic',phone:'0501234567',financeSponsorId:'BW'});
 const users={supervisor:['advances.request'],accountant:['advances.review'],custodian:['advances.payout'],outsider:['advances.review','advances.payout']};
 const creds={};
 for(const [name,perms] of Object.entries(users)){creds[name]=pw();await admin('/api/users',{username:'syn-'+name,password:creds[name],role:'Reviewer',customPermissions:perms});}
 for(const name of Object.keys(users))await as(name)('/api/auth/login',{username:'syn-'+name,password:creds[name]});
 const sup=as('supervisor'),acc=as('accountant'),cus=as('custodian'),out=as('outsider');
 // Org setup by the admin (BR §25.1).
 await admin('/api/org/memberships',{userId:'syn-accountant',department:'FIN',rank:'A',validFrom:'2026-01-01'});
 await admin('/api/org/memberships',{userId:'syn-custodian',department:'FIN',rank:'C',validFrom:'2026-01-01'});
 await admin('/api/org/routing',{operation:'advance.review',department:'FIN',minRank:'B',users:['syn-accountant'],validFrom:'2026-01-01'});
 for(const vault of ['MAIN.BW.DEBT','BANK.BW'])await admin('/api/org/routing',{operation:'advance.payout:'+vault,department:'FIN',minRank:'C',users:['syn-custodian'],validFrom:'2026-01-01'});
 await sup('/api/advance-requests',{riderIqama:iqama,type:'cash',amount:400,reason:'Synthetic need'},403);
 await admin('/api/org/supervision',{supervisorUserId:'syn-supervisor',riderIqama:iqama,validFrom:'2026-01-01'});
 assert.equal((await sup('/api/org/supervision?mine=1')).length,1);
 await sup('/api/org/memberships',undefined,403);
 // Cycle: request → propose bank → accept → approve → payout.
 const r=await sup('/api/advance-requests',{riderIqama:iqama,type:'cash',amount:400,reason:'Synthetic need'});
 assert.ok((await acc('/api/notifications')).some(n=>n.entityId===r.id&&n.requiresAction));
 assert.ok(!(await out('/api/notifications')).some(n=>n.entityId===r.id));
 const summary=await acc(`/api/advance-requests/${r.id}/summary`);assert.equal(summary.vaults.length,2);assert.equal(summary.coverageKnown,false);
 await out(`/api/advance-requests/${r.id}/summary`,undefined,403);
 await out(`/api/advance-requests/${r.id}/review`,{action:'reject',note:'not assigned'},403);
 await acc(`/api/advance-requests/${r.id}/review`,{action:'propose',note:'Use bank transfer',proposed:{type:'bank'}});
 assert.ok((await sup('/api/notifications')).some(n=>n.entityId===r.id&&n.requiresAction&&n.actionStatus==='open'));
 await sup(`/api/advance-requests/${r.id}/respond`,{action:'accept'});
 await acc(`/api/advance-requests/${r.id}/review`,{action:'approve',monthlyInstallment:200},400);
 await acc(`/api/advance-requests/${r.id}/review`,{action:'approve',monthlyInstallment:200,coverageReason:'Synthetic reviewed reason'});
 assert.equal((await cus('/api/advance-requests?role=payout')).length,1);
 const paid=await cus(`/api/advance-requests/${r.id}/payout`,{reference:'SYN-TRF-9'});
 assert.equal(paid.status,'paid');assert.ok(paid.debtId&&paid.journalEntryId);
 await cus(`/api/advance-requests/${r.id}/payout`,{reference:'SYN-TRF-10'},409);
 const debt=(await admin('/api/debts?iqama='+iqama)).find(d=>d.id===paid.debtId);
 assert.equal(debt.category,'BankAdvance');assert.equal(debt.monthlyInstallment,200);assert.equal(debt.paymentVaultCode,'BANK.BW');
 const history=(await sup(`/api/advance-requests/${r.id}`)).decisions.map(d=>d.action);
 assert.deepEqual(history,['submitted','proposed','accepted_proposal','approved','paid']);
 console.log(`PASS advances: ${checks} HTTP checks`);
}finally{await stop();rmSync(data,{recursive:true,force:true});}
