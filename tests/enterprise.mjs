import {seedAccountingCash} from './financial-fixtures.mjs';
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-enterprise-test-'));
const port=19349, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin = process.env.DOTNET_PATH || (process.platform === 'win32' && existsSync('D:/Projectes/.codex-tools/dotnet/dotnet.exe') ? 'D:/Projectes/.codex-tools/dotnet/dotnet.exe' : 'dotnet');
const exeName = process.platform === 'win32' ? 'Payroll.Api.exe' : 'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:published||path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:['ignore','pipe','pipe']});
let logs='',cookie='',checks=0,spawnError=null;proc.once('error',e=>{spawnError=e;});proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,expected=200,auth=true,headers={}){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body)});const raw=await r.text();assert.equal(r.status,expected,`${route}: ${r.status} ${raw}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];try{return JSON.parse(raw)}catch{return raw}}
const password=randomBytes(24).toString('hex');let adminCookie='';
async function put(route,body,expected=200){const r=await fetch(base+route,{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify(body)});const d=await r.json();assert.equal(r.status,expected,JSON.stringify(d));checks++;return d;}
try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 assert.equal((await req('/health',undefined,200,false)).version,'0.7.0');
 await req('/api/auth/setup',{username:'test-admin',password});adminCookie=cookie;
 await seedAccountingCash(req);
 const accountant=await req('/api/users',{username:'test-accountant',password,role:'Accountant'});
 const reviewer=await req('/api/users',{username:'test-reviewer',password,role:'Reviewer'});
 const other=await req('/api/users',{username:'test-other',password,role:'Admin'});
 await req('/api/users',{username:'TEST-REVIEWER',password,role:'Reviewer'},400);
 assert.ok((await req('/api/users')).every(u=>!('hash'in u)&&!('salt'in u)));
 await req('/api/auth/login',{username:'TEST-REVIEWER',password});const reviewerCookie=cookie;assert.equal((await req('/api/me')).role,'Reviewer');
 await req('/api/payroll/runs',undefined,403);await req('/api/users',undefined,403);await req('/api/payments',{},403);await req('/api/excel/riders/commit',{},403);
 cookie=adminCookie;await put(`/api/users/${reviewer.id}/status`,{status:'suspended'});cookie=reviewerCookie;await req('/api/me',undefined,401);
 cookie=adminCookie;await put(`/api/users/${other.id}/status`,{role:'Reviewer'});await put('/api/users/administrator/status',{status:'suspended'},400);
 await req('/api/auth/login',{username:'test-accountant',password});assert.equal((await req('/api/me')).role,'Accountant');await req('/api/payroll/approve',{},403);await req('/api/riders/sync-baseline',{},403);await req('/api/users',undefined,403);
 cookie=adminCookie;
 const iq='2000000001';const baseline=[{iqama:iq,name:'Synthetic Rider',phone:'0501234567',status:'نشط',nationality:'Test',engagement_type:'OutsideRider',operational_role:'سيارة',personRole:'rider'},{iqama:'2000000002',name:'Synthetic Administrator',personRole:'administrative',isEmployee:true}];
 let sync=await req('/api/riders/sync-baseline',{riders:baseline,mode:'preview'});assert.equal(sync.addCount,1);assert.equal(sync.excludedAdministrative,1);assert.equal(sync.errors.length,0);
 const committed=await req('/api/riders/sync-baseline',{riders:baseline,mode:'commit',fingerprint:sync.fingerprint});assert.equal(committed.count,1);
 assert.equal((await req('/api/riders/sync-baseline',{riders:baseline,mode:'commit',fingerprint:sync.fingerprint})).alreadyImported,true);
 let rider=(await req('/api/riders'))[0];assert.equal(rider.phone,'+966501234567');assert.equal(rider.engagementType,'External');assert.equal(rider.status,'Active');assert.equal(rider.policyId,'');
 const bad=[{iqama:'2000000010',name:'Synthetic Valid'},{iqama:'bad',name:'Invalid Rider'}];sync=await req('/api/riders/sync-baseline',{riders:bad,mode:'preview'});assert.equal(sync.errors.length,1);await req('/api/riders/sync-baseline',{riders:bad,mode:'commit',fingerprint:sync.fingerprint},400);assert.equal((await req('/api/riders')).length,1);
 const campaign=await req('/api/campaigns',{title:'Persistent campaign',days:2});let campaigns=await req('/api/campaigns');assert.equal(campaigns[0].publicUrl,campaign.publicUrl);assert.ok(campaign.publicUrl.startsWith('https://'));
 assert.match(await req(campaign.qrCodeUrl,undefined,200),/<svg/);await req(campaign.path,undefined,200,false);
 const choice=await req('/api/public/forms/'+campaign.path.split('/').at(-1),{iqama:iq,name:'Synthetic Rider',workSystem:'tiers'},200,false);
 await put(`/api/users/${reviewer.id}/status`,{status:'active'});await req('/api/auth/login',{username:'test-reviewer',password});
 await req('/api/submissions/'+choice.receipt+'/review',{status:'approved',note:'Identity checked',identityReviewed:true});
 assert.equal((await req('/api/riders'))[0].policyId,'');cookie=adminCookie;
 const form=await req('/api/forms/builder',{titleAr:'اختبار',titleEn:'Test',titleUr:'ٹیسٹ',expiresAt:new Date(Date.now()+864e5).toISOString(),maxSubmissions:2,fields:[{id:'iqama',labelAr:'الإقامة',labelEn:'Iqama',labelUr:'اقامہ',type:'iqama',required:true},{id:'system',labelAr:'النظام',type:'radio',required:true,options:['tiers','commission']}]});
 const publishedForm=await req(`/api/forms/builder/${form.id}/publish`,{});assert.ok(publishedForm.publicUrl.includes('/questionnaires/'));
 const formToken=publishedForm.publicUrl.split('/').at(-1);const publicForm='/api/public/questionnaires/'+formToken;
 const publicSchema=await req(publicForm,undefined,200,false);assert.equal(publicSchema.fields.length,2);assert.equal(publicSchema.sharingToken,undefined);
 await req(publicForm,{iqama:'٢٠٠٠٠٠٠٠٠١',system:'tiers'},400,false);await req(publicForm,{iqama:iq,system:'wrong'},400,false);
 const parallel=await Promise.all(['2000000031','2000000032','2000000033'].map((q,i)=>fetch(base+publicForm,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({iqama:q,system:i%2?'commission':'tiers'})})));
 assert.equal(parallel.filter(r=>r.status===200).length,2);assert.equal(parallel.filter(r=>r.status===400).length,1);checks+=3;
 const analytics=await req('/api/forms/analytics/'+form.id);assert.equal(analytics.totalResponses,2);assert.equal(analytics.fields[0].options.reduce((s,x)=>s+x.count,0),2);assert.equal((await req('/api/forms/responses/'+form.id)).length,2);
 await put('/api/forms/builder/'+form.id,{...form,titleAr:'Changed'},400);
 assert.equal((await req('/api/ledger')).length,0);
 // Debt preview and posting: gross wage and the installment are separate ledger entries.
 let policy=await req('/api/policies',{name:'Approved pay',kind:'approved_pay',platform:'all',effectiveFrom:'2026-10',config:{}});await req(`/api/policies/${policy.id}/approve`,{});
 await req('/api/riders',{...rider,policyId:policy.id});
 const csv=`sourceRef,iqama,name,accountId,from,to,orders,riderPay,isValid\r\nenterprise-row,${iq},Synthetic Rider,1234567890123456,2026-10-01,2026-10-31,1,500,true\r\n`;
 const upload=await req('/api/imports/inspect',{filename:'enterprise.csv',dataBase64:Buffer.from(csv).toString('base64')});
 const spec={sheet:'CSV',period:'2026-10',platform:'Keeta',headerRow:1,confirmActualRider:true,mapping:{sourceRef:0,iqama:1,name:2,accountId:3,from:4,to:5,orders:6,riderPay:7,isValid:8}};const workPreview=await req(`/api/imports/${upload.id}/preview`,spec);assert.equal(workPreview.errors.length,0,JSON.stringify(workPreview.errors));await req(`/api/imports/${upload.id}/commit`,spec);
 const debt=await req('/api/debts',{iqama:iq,category:'Advance',creditorSponsorId:'BW',totalAmount:600,monthlyInstallment:400,sourceRef:'agreed-advance',agreementReviewed:true,effectiveFrom:'2026-10'});
 let pay=(await req('/api/payroll/preview?period=2026-10'))[0];assert.equal(pay.payable,100);assert.equal((await req('/api/debts'))[0].remainingBalance,600);
 await put('/api/debts/'+debt.id,{monthlyInstallment:200,agreementReviewed:true});await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:pay.fingerprint},400);
 pay=(await req('/api/payroll/preview?period=2026-10'))[0];assert.equal(pay.payable,300);await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:pay.fingerprint});await req('/api/payroll/approve',{sponsorId:'BW',iqama:iq,period:'2026-10',fingerprint:pay.fingerprint},400);
 assert.equal((await req('/api/debts'))[0].remainingBalance,400);const ledger=await req('/api/ledger/'+iq);assert.equal(ledger.balance,300);assert.equal(ledger.rows.filter(x=>x.kind==='debt_installment').length,1);assert.equal(ledger.rows.find(x=>x.kind==='approved_payroll').amount,500);
 // Financial disclosure always requires a fresh, scoped, unguessable code and phone check.
 let access=await req('/api/inquiry-access',{iqama:iq,scope:'payslip',identityReviewed:true});
 await req('/api/public/inquiry/payslip',{iqama:iq,phoneLast4:'9999',code:access.code,period:'2026-10'},401,false);
 const answer=await fetch(base+'/api/public/inquiry/payslip',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({iqama:iq,phoneLast4:'4567',code:access.code,period:'2026-10'})});assert.equal(answer.status,200);checks++;assert.equal(answer.headers.get('cache-control'),'private, max-age=300');assert.ok(answer.headers.get('etag'));const payslip=await answer.json();assert.equal(payslip.result.payable,300);assert.equal(payslip.sourceRows,undefined);
 await req('/api/public/inquiry/payslip',{iqama:iq,phoneLast4:'4567',code:access.code,period:'2026-10'},401,false);
 access=await req('/api/inquiry-access',{iqama:iq,scope:'debt',identityReviewed:true});const statement=await req('/api/public/inquiry/debt',{iqama:iq,phoneLast4:'4567',code:access.code},200,false);assert.equal(statement.totalRemaining,400);assert.equal(statement.debts[0].notes,undefined);
 await req('/api/public/inquiry/debt',{iqama:iq,phoneLast4:'4567',code:'0'.repeat(64)},401,false);
 await req('/api/public/inquiry/debt',{iqama:iq,phoneLast4:'4567',code:'0'.repeat(64)},429,false);
 assert.ok((await req('/api/audit')).some(x=>x.action==='rider.baseline_imported'));assert.ok((await req('/api/audit')).some(x=>x.action==='admin.user_update'));

 // Financial & Debt Management System v0.6 checks: 3 Sponsors, HR Expenses, SharedSplit, Direct Collection + Waiver, Inter-CR Transfer, Approvals, Mobile FCM, Dormant Promissory Flag
 const finCatalog=await req('/api/finance/catalog');
 assert.equal(finCatalog.sponsors.length,3);
 assert.equal(finCatalog.promissoryNotesArchiveEnabled,false);
 await req('/api/promissory-notes',{iqama:iq,noteNumber:'NFZ-1',amount:5000},400); // Dormant by default

 // 1. Create HR Service (WorkPermit) with SharedSplit: totalCost 9600, companyShare 4600 -> riderShare 5000 on BW
 const wpDebt=await req('/api/debts',{iqama:iq,category:'WorkPermit',allocationMode:'SharedSplit',totalCostAmount:9600,companyShareAmount:4600,monthlyInstallment:1000,creditorSponsorId:'BW',entryMode:'Disbursement',paymentVaultCode:'MAIN.BW.DEBT',sourceRef:'HR-WP-001',agreementReviewed:true,effectiveFrom:'2026-10'});
 assert.equal(wpDebt.totalAmount,5000);assert.equal(wpDebt.companyShareAmount,4600);assert.equal(wpDebt.remainingBalance,5000);assert.ok(wpDebt.journalNumber);

 // 2. Collect 1500 cash into Express internal cashbox (MAIN.EX.SET) + 500 Administrative Waiver (5202) -> auto Inter-CR clearing (1310)
 const colRes=await req(`/api/debts/${wpDebt.id}/collect`,{collectedAmount:1500,waivedAmount:500,targetVaultCode:'MAIN.EX.DEBT',receiptRef:'RCV-EX-001',waiverReason:'Early settlement discount'});
 assert.equal(colRes.debt.remainingBalance,3000);assert.equal(colRes.debt.paidAmount,1500);assert.equal(colRes.debt.waivedAmount,500);
 assert.equal(colRes.journal.totalDebit,3500);assert.equal(colRes.journal.totalCredit,3500); // 1500 MAIN.EX.SET + 500 Waiver 5202 + 1500 InterCompany 1310 = 3500

 // 3. Purchase/Transfer remaining 3000 debt from AlBawaba (BW) to Express Gate (EX)
 const trRes=await req(`/api/debts/${wpDebt.id}/transfer-sponsor`,{newCreditorSponsorId:'EX',settlementMode:'intercompany',expectedCreditorSponsorId:'BW',expectedRemainingBalance:3000,transferRef:'TRN-BW-EX-01'});
 assert.equal(trRes.debt.creditorSponsorId,'019c18d5-62e1-7000-8000-000000000042');

 // 4. Mutual Approval workflow (HR -> FIN)
 const pendingDebt=await req('/api/debts',{iqama:iq,category:'IqamaRenewal',allocationMode:'RiderFull',totalAmount:650,monthlyInstallment:650,requireMutualApproval:true,initiatingDepartment:'HR',sourceRef:'HR-IQ-002',agreementReviewed:true});
 assert.equal(pendingDebt.status,'pending_approval');assert.ok(pendingDebt.approvalRequestId);
 const decided=await req(`/api/approvals/${pendingDebt.approvalRequestId}/decide`,{decision:'approved',overrideSelf:true,note:'Explicit synthetic administrator override after verification'});
 assert.equal(decided.status,'approved');
 assert.equal((await req('/api/debts')).find(d=>d.id===pendingDebt.id).status,'active');

 // 5. Bulk Collection Commit & Rider 360 Statement
 const bulkCsv=`iqama,riderName,collectedAmount,waivedAmount,targetVaultCode,receiptRef,notes\r\n${iq},Synthetic Rider,650,0,MAIN.EX.DEBT,BLK-001,test\r\n`;
 const bulkPreview=await req('/api/debts/bulk-collect/preview-file',{filename:'bulk.csv',dataBase64:Buffer.from(bulkCsv).toString('base64')});assert.equal(bulkPreview.errorCount,0);
 const bulkInput={rows:bulkPreview.rows,fingerprint:bulkPreview.fingerprint,confirm:true};
 const bulkRes=await req('/api/debts/bulk-collect/commit',bulkInput);
 await req('/api/debts/bulk-collect/commit',bulkInput,409);
 assert.equal(bulkRes.totalCollectedBatch,650);
 const st360=await req(`/api/finance/statement/rider/${iq}`);
 assert.ok(st360.statementLines.length>=4);

 // 6. Google Play Mobile Login & Broadcast
 await req('/api/rider-mobile/login',{iqamaNo:iq},401,false);
 const mobileGrant=await req('/api/inquiry-access',{iqama:iq,scope:'mobile',identityReviewed:true});
 const mobInput={iqamaNo:iq,phoneLast4:'4567',code:mobileGrant.code,language:'ar',fcmDeviceToken:'fcm_test_token_123'};
 const mobLogin=await req('/api/rider-mobile/login',mobInput,200,false);
 await req('/api/rider-mobile/login',mobInput,401,false);
 assert.ok(mobLogin.accessToken.startsWith('rmob_'));assert.equal(mobLogin.fcmRegistered,false);assert.equal(mobLogin.fcmTokenStored,true);
 const bcast=await req('/api/rider-mobile/broadcast',{iqama:iq,category:'financial',titleAr:'تحديث كشف الحساب',bodyAr:'تم تحديث الرصيد'});
 assert.equal(bcast.deliveryChannel,'in_app');
 await req(`/api/rider-mobile/broadcasts/${bcast.id}/ack`,{iqama:iq},401,false);
 await req(`/api/rider-mobile/broadcasts/${bcast.id}/ack`,{iqama:iq},200,false,{Authorization:`Bearer ${mobLogin.accessToken}`});

 // 7. Rider Data Quality, Missing Data Recovery & 360° Document Checklist (7 ERP Document Types)
 const dq=await req('/api/riders/data-quality');
 assert.equal(dq.totalRiders,1);
 await req(`/api/riders/${iq}/documents`,{documentTypeCode:'ResidencyPermit',documentNumber:iq,issueDate:'2026-01-01',expiryDate:'2027-01-01',fileName:'iqama.pdf'});
 const p360=await req(`/api/riders/${iq}/profile-360`);
 assert.equal(p360.documentChecklist.length,7);
 assert.equal(p360.documentChecklist.find(d=>d.code==='ResidencyPermit').isUploaded,true);
 assert.equal(p360.documentChecklist.find(d=>d.code==='ResidencyPermit').documentTypeId,'019c18d5-62e1-7000-8000-000000000030');

 // 8. Opening Balance Debt Import Sheet (Preview Statement + Commit) & Rider Mobile Portal (/rider-app)
 const obCsv='iqama,riderName,debtorType,category,creditorSponsor,entryMode,allocationMode,totalAmount,priorPaidAmount,companyShareAmount,monthlyInstallment,effectiveFrom,contraAccountCode,sourceRef,notes\r\n2491234567,Opening Balance Rider,Rider,Advance,BW,OpeningBalance,RiderFull,3500,500,0,500,2026-10,3101,OB-TEST-01,Opening balance advance\r\n';
 const obPrev=await req('/api/debts/import-opening/preview',{filename:'opening.csv',dataBase64:Buffer.from(obCsv).toString('base64')});
 assert.equal(obPrev.validCount,1);assert.equal(obPrev.errorCount,0);assert.equal(obPrev.totalOpeningRemaining,3000);assert.equal(obPrev.isBalanced,true);
 assert.equal(obPrev.summaryBySponsor.length,1);assert.equal(obPrev.summaryByCategory.length,1);
 const openingInput={rows:obPrev.rows,fingerprint:obPrev.fingerprint,confirm:true};
 const tampered=structuredClone(openingInput);tampered.rows[0].totalAmount=-300;
 await req('/api/debts/import-opening/commit',tampered,409);
 const obCommit=await req('/api/debts/import-opening/commit',openingInput);
 await req('/api/debts/import-opening/commit',openingInput,409);
 assert.equal(obCommit.importedCount,1);assert.equal(obCommit.totalRemainingImported,3000);

 // Verify Firebase Config & Mobile Portal (/rider-app & /api/rider-mobile/portal/{iqama})
 await req('/api/rider-mobile/firebase-config',{projectId:'albawaba-gate-rider-prod',senderId:'99887766',androidPackageName:'com.albawaba.gate.rider'});
 const fbCfg=await req('/api/rider-mobile/firebase-config');
 assert.equal(fbCfg.projectId,'albawaba-gate-rider-prod');
 const mobPortal=await req(`/api/rider-mobile/portal/${iq}`,undefined,200,false,{Authorization:`Bearer ${mobLogin.accessToken}`});
 assert.equal(mobPortal.rider.iqama,iq);assert.ok(mobPortal.broadcasts.length>=1);
 await req('/rider-app',undefined,200,false);
 await req(`/api/rider-mobile/portal/${iq}`,undefined,401,false);
 await req(`/api/rider-mobile/portal/2000000002`,undefined,401,false,{Authorization:`Bearer ${mobLogin.accessToken}`});
 const attachment=Buffer.from('%PDF-1.4\nSynthetic private attachment');
 const uploaded=await req(`/api/riders/${iq}/documents`,{documentTypeCode:'ResidencyPermit',fileName:'rider.pdf',dataBase64:attachment.toString('base64')});
 await req(uploaded.storagePath,undefined,401,false);
 const download=await fetch(base+uploaded.storagePath,{headers:{Cookie:adminCookie}});assert.equal(download.status,200);assert.deepEqual(Buffer.from(await download.arrayBuffer()),attachment);checks++;
 const editedDocument=await req(`/api/riders/${iq}/documents`,{documentTypeCode:'ResidencyPermit',fileName:'rider.pdf',expiryDate:'2028-01-01'});assert.equal(editedDocument.hasBinary,true);assert.equal(editedDocument.history.length,2);
 const privateResult=await req('/api/system/release-check');assert.equal(privateResult.version,'0.7.0');
 const grantAgain=await req('/api/inquiry-access',{iqama:iq,scope:'mobile',identityReviewed:true});assert.ok(grantAgain.url.includes('#access_code='));
 // New financial surfaces retain original staff-role isolation.
 await req('/api/auth/login',{username:'test-reviewer',password});
 for(const route of ['/api/finance/journals','/api/debts',`/api/riders/${iq}/profile-360`,uploaded.storagePath,'/api/system/release-check'])await req(route,undefined,403);
 cookie=adminCookie;

 // 9. Treasury & Chart of Accounts Hierarchy + Jahez Box Financial Isolation + Operation-Specific Account Filtering
 // 9.a: Reject using Jahez Cashbox (SUP.*), Jahez Fee Account (*.FEE), or PetroApp (PETRO.*) in HR Requests or Advances
 await req('/api/debts',{iqama:iq,category:'WorkPermit',totalAmount:9600,monthlyInstallment:1000,paymentVaultCode:'SUP.BW.SET',sourceRef:'BAD-HR-SUP',agreementReviewed:true},400);
 await req('/api/debts',{iqama:iq,category:'Advance',totalAmount:1000,monthlyInstallment:500,paymentVaultCode:'SUP.BW.SET',sourceRef:'BAD-ADV-SUP',agreementReviewed:true},400);
 await req('/api/debts',{iqama:iq,category:'WorkPermit',totalAmount:9600,monthlyInstallment:1000,paymentVaultCode:'MAIN.BW.FEE',sourceRef:'BAD-HR-FEE',agreementReviewed:true},400);
 await req('/api/debts',{iqama:iq,category:'WorkPermit',totalAmount:9600,monthlyInstallment:1000,paymentVaultCode:'PETRO.BW',sourceRef:'BAD-HR-PETRO',agreementReviewed:true},400);
 await req('/api/debts',{iqama:iq,category:'CashAdvance',totalAmount:1000,monthlyInstallment:500,paymentVaultCode:'BANK.BW',sourceRef:'BAD-CASH-BANK',agreementReviewed:true},400);
 await req('/api/debts',{iqama:iq,category:'BankAdvance',totalAmount:1000,monthlyInstallment:500,paymentVaultCode:'MAIN.BW.DEBT',sourceRef:'BAD-BANK-MAIN',agreementReviewed:true},400);
 // Reject collecting an HR debt into Jahez Cashbox or Fee account or PetroApp
 await req(`/api/debts/${wpDebt.id}/collect`,{collectedAmount:100,targetVaultCode:'SUP.EX.SET',receiptRef:'BAD-COL-SUP'},400);
 await req(`/api/debts/${wpDebt.id}/collect`,{collectedAmount:100,targetVaultCode:'MAIN.EX.FEE',receiptRef:'BAD-COL-FEE'},400);
 await req(`/api/debts/${wpDebt.id}/collect`,{collectedAmount:100,targetVaultCode:'PETRO.EX',receiptRef:'BAD-COL-PETRO'},400);

 // 9.b: Reject invalid Vault Transfers (PetroApp, funding SUP.*.FEE, or transferring SUP.*.SET to MAIN.*.DEBT)
 await req('/api/finance/vault-transfer',{fromVaultCode:'PETRO.BW',toVaultCode:'MAIN.BW.SET',amount:500,sourceRef:'BAD-VTR-PETRO'},400);
 await req('/api/finance/vault-transfer',{fromVaultCode:'MAIN.BW.SET',toVaultCode:'SUP.BW.FEE',amount:200,sourceRef:'BAD-VTR-SUPFEE'},400);
 await req('/api/finance/vault-transfer',{fromVaultCode:'SUP.BW.SET',toVaultCode:'MAIN.BW.DEBT',amount:500,sourceRef:'BAD-VTR-SUPDEBT'},400);

 // 9.c: Valid Jahez Platform Cycle: Account Opening Fee (200 SAR -> SUP.BW.FEE) + PlatformTransferredDebt collection (800 SAR -> SUP.BW.SET) + EOD Handover Zero-Out to Main Internal Vault
 const jFee=await req('/api/finance/jahez-fee',{sponsorId:'BW',targetVaultCode:'SUP.BW.FEE',iqama:iq,amount:200,sourceRef:'JFEE-001'});
 assert.equal(jFee.totalDebit,200);
 const jDebt=await req('/api/debts',{iqama:iq,category:'PlatformTransferredDebt',platform:'Jahez',entryMode:'Accrual',totalAmount:1200,monthlyInstallment:600,creditorSponsorId:'BW',sourceRef:'JPLAT-001',agreementReviewed:true});
 const jCol=await req(`/api/debts/${jDebt.id}/collect`,{collectedAmount:2000,waivedAmount:0,targetVaultCode:'SUP.BW.SET',receiptRef:'bad-over'},400);
 await req(`/api/debts/${jDebt.id}/collect`,{collectedAmount:1200,waivedAmount:0,targetVaultCode:'SUP.BW.SET',receiptRef:'JCOL-001'});
 // The debt accrual never paid cash: SUP.SET now holds the 1200 collected from the rider, with a matching Jahez liability.
 const jHandover=await req('/api/finance/jahez-handover',{sponsorId:'BW',sourceRef:'JHND-001'});
 assert.equal(jHandover.feeHandedOver,200);assert.equal(jHandover.settlementHandedOver,1200);
 const coaAfter=await req('/api/finance/accounts');
 assert.equal(coaAfter.find(a=>a.code==='SUP.BW.FEE').netBalance,0);
 assert.equal(coaAfter.find(a=>a.code==='MAIN.BW.FEE').netBalance,200);
 assert.equal(coaAfter.find(a=>a.code==='1113').netBalance,200); // Parent group 1113 rolls up MAIN.*.FEE

 // 9.d: CompanyFull HR Order appears in Rider 360 Statement without increasing rider debt balance
 await req('/api/debts',{iqama:iq,category:'MedicalInsurance',allocationMode:'CompanyFull',totalCostAmount:1000,creditorSponsorId:'BW',entryMode:'Disbursement',paymentVaultCode:'MAIN.BW.DEBT',sourceRef:'HR-MED-COMP',agreementReviewed:true});
 const stUpdated=await req(`/api/finance/statement/rider/${iq}`);
 assert.ok(stUpdated.statementLines.some(l=>l.type==='company_borne_service'&&l.companyBorneAmount===1000&&l.debit===0));
 assert.ok(stUpdated.journalEntries.length>=4);

 // 10. RBAC Roles Catalog & Operational Roles (HRSpecialist, PlatformsSupervisor) with Department, Rank & Custom Permissions
 const rolesCat=await req('/api/roles-catalog');
 assert.ok(rolesCat.roles.length>=10);
 assert.ok(rolesCat.permissions.length>=20);
 const hrUser=await req('/api/users',{username:'hr-specialist-1',password,role:'HRSpecialist',department:'HR',rankLevel:3,customPermissions:['debts.waive']});
 assert.equal(hrUser.department,'HR');assert.equal(hrUser.rankLevel,3);
 assert.ok(hrUser.permissions.includes('debts.create.hr'));assert.ok(hrUser.permissions.includes('debts.waive'));
 await req('/api/auth/login',{username:'hr-specialist-1',password});
 const hrMe=await req('/api/me');assert.equal(hrMe.role,'HRSpecialist');assert.equal(hrMe.department,'HR');
 await req('/api/users',undefined,403); // HRSpecialist cannot manage users
 cookie=adminCookie;


 console.log(`PASS ${checks} enterprise HTTP checks: RBAC, session revocation, baseline atomicity, persistent links/QR, builder limits/analytics, debt posting, double-entry finance, multi-CR, waivers, approvals, and private enquiries.`);
}catch(e){console.error(logs.slice(-5000));throw e;}finally{if(proc.exitCode===null&&!spawnError){const stopped=new Promise(r=>proc.once('exit',r));proc.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
