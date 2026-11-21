import {seedAccountingCash} from './financial-fixtures.mjs';
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-accounting-http-'));
const port=19357, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');let adminCookie='';
async function put(route,body,expected=200){const r=await fetch(base+route,{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify(body)});const d=await r.json();assert.equal(r.status,expected,JSON.stringify(d));checks++;return d;}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready,logs);
 const password=randomBytes(24).toString('hex');await req('/api/auth/setup',{username:'accounting-admin',password});const adminCookie=cookie;
 await seedAccountingCash(req);
 const iq='2000000001';await req('/api/riders',{iqama:iq,name:'Accounting Synthetic',phone:'0501234567',financeSponsorId:'BW'});
 const debt=await req('/api/debts',{iqama:iq,category:'WorkPermit',entryMode:'Accrual',allocationMode:'SharedSplit',totalCostAmount:900,companyShareAmount:300,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'invoice',agreementReviewed:true});
 let accounts=await req('/api/finance/accounts');const amount=code=>accounts.find(a=>a.code===code).netBalance;
 assert.equal(amount('MAIN.BW.DEBT'),100000);assert.equal(amount('2130'),900);assert.equal(debt.totalAmount,600);
 await req('/api/finance/liability-payment',{debtId:debt.id,paymentVaultCode:'BANK.BW',amount:100,sourceRef:'invoice-payment'});
 await req('/api/finance/liability-payment',{debtId:debt.id,paymentVaultCode:'BANK.BW',amount:801,sourceRef:'invoice-over'},400);
 await req('/api/finance/liability-payment',{debtId:debt.id,paymentVaultCode:'BANK.EX',amount:200,sourceRef:'invoice-other-bank'},400);
 await req('/api/finance/liability-payment',{debtId:debt.id,paymentVaultCode:'BANK.EX',amount:200,sourceRef:'invoice-other-bank',onBehalf:true,onBehalfNote:'EX pays BW vendor per voucher'});
 accounts=await req('/api/finance/accounts');assert.equal(amount('BANK.EX'),99800);assert.equal(amount('BANK.BW'),99900);assert.equal(amount('2130'),600);
 const move=await req('/api/debts',{iqama:iq,category:'WorkPermit',entryMode:'Accrual',totalAmount:1000,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'movable',agreementReviewed:true});
 await req(`/api/debts/${move.id}/collect`,{collectedAmount:200,targetVaultCode:'BANK.BW',receiptRef:'before-transfer'});
 await req(`/api/debts/${move.id}/transfer-sponsor`,{newCreditorSponsorId:'EX',expectedCreditorSponsorId:'BW',expectedRemainingBalance:800,transferRef:'bw-ex'});
 await req(`/api/debts/${move.id}/collect`,{collectedAmount:100,targetVaultCode:'MAIN.EX.DEBT',receiptRef:'after-first-transfer'});
 await req(`/api/debts/${move.id}/transfer-sponsor`,{newCreditorSponsorId:'TR',expectedCreditorSponsorId:'EX',expectedRemainingBalance:700,transferRef:'ex-tr'});
 await req(`/api/debts/${move.id}/collect`,{waivedAmount:50,targetVaultCode:'MAIN.TR.DEBT',waiverReason:'Synthetic approved waiver',receiptRef:'tr-waiver'});
 const current=await req('/api/debts');assert.equal(current.find(d=>d.id===move.id).ownershipHistory.length,2);
 for(const owner of ['BW','EX','TR']){const st=await req(`/api/finance/statement/rider/${iq}?sponsorId=${owner}`);const remaining=current.filter(d=>d.creditorSponsorId===({BW:'019c18d5-62e1-7000-8000-000000000041',EX:'019c18d5-62e1-7000-8000-000000000042',TR:'019c18d5-62e1-7000-8000-000000000040'})[owner]&&['active','paused'].includes(d.status)).reduce((n,d)=>n+d.remainingBalance,0);assert.equal(st.totalRemainingDebt,remaining);assert.equal(st.reconciliationDifference,0);}
 const j=await req('/api/debts',{iqama:iq,category:'PlatformTransferredDebt',platform:'Jahez',entryMode:'Accrual',totalAmount:1200,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'jahez-referred',agreementReviewed:true});
 for(const code of ['BANK.BW','MAIN.BW.DEBT'])await req(`/api/debts/${j.id}/collect`,{collectedAmount:1200,targetVaultCode:code,receiptRef:'wrong:'+code},400);
 await req(`/api/debts/${j.id}/collect`,{collectedAmount:1200,targetVaultCode:'SUP.BW.SET',receiptRef:'jahez-collected'});
 await req('/api/finance/jahez-fee',{sponsorId:'BW',targetVaultCode:'SUP.BW.FEE',iqama:iq,amount:200,sourceRef:'company-fee'});
 await req('/api/finance/jahez-handover',{sponsorId:'BW',settlementAmount:1201,feeAmount:200,sourceRef:'over-handover'},400);
 const handover=await req('/api/finance/jahez-handover',{sponsorId:'BW',sourceRef:'handover'});assert.equal(handover.status,'pending');await req(`/api/finance/jahez-handovers/${handover.id}/confirm`,{settlementCounted:1200,feeCounted:200},403);
 await req('/api/users',{username:'jahez-receiver',password,role:'Accountant'});await req('/api/auth/login',{username:'jahez-receiver',password});await req(`/api/finance/jahez-handovers/${handover.id}/confirm`,{settlementCounted:1400,feeCounted:0},400);const received=await req(`/api/finance/jahez-handovers/${handover.id}/confirm`,{settlementCounted:1200,feeCounted:200});assert.equal(received.status,'approved');cookie=adminCookie;
 await req('/api/finance/vault-transfer',{fromVaultCode:'MAIN.BW.SET',toVaultCode:'MAIN.BW.DEBT',amount:1,sourceRef:'laundering'},400);
 await req('/api/finance/jahez-transfer',{fromVaultCode:'MAIN.BW.SET',toVaultCode:'BANK.BW.JAHEZ',amount:1200,sourceRef:'custody-bank',notes:'Synthetic bank deposit'});
 await req('/api/finance/jahez-platform-settlement',{sponsorId:'BW',action:'pay',amount:1201,sourceRef:'jahez-overpay',notes:'Synthetic'},400);
 await req('/api/finance/jahez-platform-settlement',{sponsorId:'BW',action:'pay',amount:1200,sourceRef:'jahez-paid',notes:'Synthetic provider settlement'});
 await req('/api/finance/jahez-fee-deposit',{sponsorId:'BW',amount:200,sourceRef:'company-fee-bank',notes:'Synthetic company income deposit'});
 accounts=await req('/api/finance/accounts');assert.equal(amount('2140'),0);assert.equal(amount('BANK.BW.JAHEZ'),0);assert.equal(amount('MAIN.BW.FEE'),0);assert.equal(amount('4101'),200);
 // Financial write permissions and document scope are separate, including requests made directly to the API.
 const scope={sponsorIds:['BW'],accountCodes:['MAIN.BW.DEBT'],categoryCodes:['CashAdvance','WorkPermit'],platforms:['Jahez']};
 await req('/api/users',{username:'hr-scoped',password,role:'HRSpecialist',department:'HR',rankLevel:2,financialScope:scope});
 await req('/api/auth/login',{username:'hr-scoped',password});const hrCookie=cookie;
 await req('/api/debts',{iqama:iq,category:'TrafficFine',entryMode:'Accrual',totalAmount:100,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'hr-wrong-role',agreementReviewed:true},403);
 const requested=await req('/api/debts',{iqama:iq,category:'CashAdvance',entryMode:'Disbursement',paymentVaultCode:'MAIN.BW.DEBT',totalAmount:100,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'hr-request',agreementReviewed:true});assert.equal(requested.status,'pending_approval');
 await req(`/api/approvals/${requested.approvalRequestId}/decide`,{decision:'approved'},403);
 cookie=adminCookie;await req(`/api/approvals/${requested.approvalRequestId}/decide`,{decision:'approved',note:'Independent finance reviewer'});
 await req('/api/users',{username:'finance-scoped',password,role:'Accountant',financialScope:scope});await req('/api/auth/login',{username:'finance-scoped',password});
 assert.ok((await req('/api/debts')).every(d=>d.creditorSponsorId==='019c18d5-62e1-7000-8000-000000000041'));
 assert.ok((await req('/api/finance/accounts')).filter(a=>a.isVault).every(a=>a.code==='MAIN.BW.DEBT'));
 await req(`/api/debts/${move.id}/collect`,{collectedAmount:1,targetVaultCode:'MAIN.BW.DEBT',receiptRef:'outside-company'},403);
 await req('/api/finance/vault-transfer',{fromVaultCode:'MAIN.EX.DEBT',toVaultCode:'BANK.EX',amount:1,sourceRef:'outside-account'},403);
 // Revocation must affect an existing authenticated session immediately.
 const limitedCookie=cookie;cookie=adminCookie;const users=await req('/api/users');const limited=users.find(u=>u.username==='finance-scoped');await put(`/api/users/${limited.id}/status`,{deniedPermissions:['debts.collect']});cookie=limitedCookie;await req('/api/me',undefined,401);cookie=adminCookie;
 // Burst collections serialize at both SQLite and SQL Server: no overdraft, duplicate money or HTTP 500.
 const concurrent=await req('/api/debts',{iqama:iq,category:'Advance',totalAmount:600,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'concurrent',agreementReviewed:true});
 const statuses=await Promise.all(Array.from({length:8},(_,i)=>fetch(base+`/api/debts/${concurrent.id}/collect`,{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({collectedAmount:100,targetVaultCode:'MAIN.BW.DEBT',receiptRef:`concurrent:${i}`})}).then(r=>r.status)));
 assert.equal(statuses.filter(s=>s===200).length,6);assert.equal(statuses.filter(s=>s===400).length,2);assert.ok(!statuses.includes(500));
 const journals=await req('/api/finance/journals');for(const journal of journals){const sums=new Map();for(const line of journal.lines)sums.set(line.sponsorId,(sums.get(line.sponsorId)||0)+line.debit-line.credit);for(const value of sums.values())assert.equal(Math.round(value*100),0);}
 console.log(`PASS ${checks} accounting HTTP checks plus parallel collection invariants: accrual/cash isolation, supplier payment, immutable transfers, per-company statements, Jahez custody/bank/company fees, scope enforcement and live session revocation.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
