import {seedAccountingCash} from './financial-fixtures.mjs';
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const directory=mkdtempSync(path.join(tmpdir(),'gate-simple-http-'));
const base='http://127.0.0.1:19461';
const dotnet=process.env.DOTNET_PATH||(process.platform==='win32'&&existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe')?'D:/Projectes/.codex-tools/dotnet/dotnet.exe':'dotnet');
const server=spawn(dotnet,[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:directory},stdio:['ignore','pipe','pipe']});
let logs='',cookie='',spawnError;
server.stdout.on('data',x=>logs+=x);server.stderr.on('data',x=>logs+=x);server.once('error',error=>spawnError=error);
async function req(route,body,status=200,auth=true){
 const response=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});
 const raw=await response.text();const data=raw?JSON.parse(raw):{};assert.equal(response.status,status,`${route}: ${JSON.stringify(data)}`);
 if(response.headers.get('set-cookie'))cookie=response.headers.get('set-cookie').split(';')[0];return data;
}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(server.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready,logs);
 assert.equal((await req('/health',undefined,200,false)).version,'0.8.0');
 const iqama='2000000999',period=new Date().toISOString().slice(0,7),route=`/api/finance/statement/rider/${iqama}/simple?from=${period}&to=${period}`;
 await req(route,undefined,401,false);
 const password=randomBytes(24).toString('hex');await req('/api/auth/setup',{username:'simple-admin',password});const admin=cookie;
 await seedAccountingCash(req);await req('/api/riders',{iqama,name:'Synthetic Rider',financeSponsorId:'BW'});
 await req(`/api/finance/statement/rider/${iqama}/simple`,undefined,400);
 await req(route.replace(`from=${period}`,'from=2026-13'),undefined,400);
 const debt=await req('/api/debts',{iqama,category:'WorkPermit',entryMode:'Accrual',totalAmount:500,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'simple-debt',agreementReviewed:true});
 await req(`/api/debts/${debt.id}/collect`,{collectedAmount:100,targetVaultCode:'MAIN.BW.DEBT',receiptRef:'simple-collection',period});
 await req('/api/debts',{iqama,category:'WorkPermit',entryMode:'Accrual',totalAmount:900,monthlyInstallment:100,creditorSponsorId:'EX',sourceRef:'other-company',agreementReviewed:true});
 const before=await req('/api/system/release-check');
 const statement=await req(route+'&sponsorId=BW');assert.equal(statement.closingNet,-400);assert.equal(statement.periodTotals.debt_added,-500);assert.equal(statement.periodTotals.debt_collected,100);
 assert.equal(statement.details.length,4);assert.ok(statement.details.every(row=>row.journalId&&row.labelAr));
 const after=await req('/api/system/release-check');assert.deepEqual(after.snapshot,before.snapshot);
 await req(route,{},405);
 await req('/api/users',{username:'simple-bw',password,role:'Accountant',financialScope:{sponsorIds:['BW'],accountCodes:['MAIN.BW.DEBT'],categoryCodes:['WorkPermit'],platforms:['Jahez']}});
 await req('/api/auth/login',{username:'simple-bw',password});
 const scoped=await req(route);assert.equal(scoped.closingNet,-400);assert.ok(scoped.details.every(row=>row.sponsorId===debt.creditorSponsorId));
 await req(route+'&sponsorId=EX',undefined,403);
 cookie=admin;assert.equal((await req(route)).closingNet,-1300);
 console.log('PASS simple statement HTTP: route, release, authentication, validation, immutable reads, sponsor isolation and administrative override.');
}finally{
 if(server.exitCode===null){server.kill('SIGTERM');await new Promise(resolve=>server.once('exit',resolve));}
 rmSync(directory,{recursive:true,force:true});
}
