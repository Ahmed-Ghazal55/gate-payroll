// HTTP responses from an isolated synthetic API, including nonempty list items.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdtempSync,rmSync,readFileSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import Ajv from '../web/node_modules/ajv/dist/2020.js';
import assert from 'node:assert/strict';
const root=new URL('../',import.meta.url).pathname,data=mkdtempSync(tmpdir()+'/gate-r8-contract-');
const port=createServer();await new Promise(r=>port.listen(0,'127.0.0.1',r));const base='http://127.0.0.1:'+port.address().port;await new Promise(r=>port.close(r));
let cookie='',checks=0;
const child=spawn('dotnet',[root+'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll'],{cwd:root+'Payroll.Api',env:{...process.env,ConnectionStrings__DefaultConnection:'',PAYROLL_DATA_DIR:data,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,Jobs__Enabled:'false',Vouchers__Secret:'GateSynthetic_CI_Only_Voucher_Key_000001'},stdio:'ignore'});
async function call(route,body,status=200,method=body===undefined?'GET':'POST'){const response=await fetch(base+route,{method,headers:{Cookie:cookie,'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body)});const raw=await response.text();assert.equal(response.status,status,route+": "+raw.slice(0,240));if(response.headers.get('set-cookie'))cookie=response.headers.get('set-cookie').split(';')[0];return JSON.parse(raw);}
try {
 let ready=false;for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready);
 await call('/openapi/v1.json',undefined,401);
 await call('/api/auth/setup',{username:'synthetic-r8-admin',password:randomBytes(24).toString('hex')});
 const doc=await call('/openapi/v1.json');
 const snapshot=root+'docs/api/openapi.v1.json';
 if(process.argv.includes('--update-snapshot'))writeFileSync(snapshot,JSON.stringify(doc,null,2)+'\n');
 const saved=JSON.parse(readFileSync(snapshot,'utf8'));
 const ajv=new Ajv({strict:false,validateFormats:false});
 const db=new DatabaseSync(data+'/payroll.db');
 function put(kind,record){db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(record.id,kind,JSON.stringify(record));}
 const company=(await call('/api/companies'))[0].id;
 put('debt',{id:'syn-r8-debt',iqama:'2000000001',category:'Advance',status:'pending_approval',totalAmount:125,remainingBalance:100,monthlyInstallment:25,creditorSponsorId:company,originSponsorId:company});
 put('rider',{id:'2000000001',iqama:'2000000001',name:'Synthetic',sponsorId:company,isDeliveryRider:true});
 put('platform_invoice',{id:'syn-r8-invoice',platform:'Ninja',companyId:company,period:'2026-10',reference:'SYN-R8',status:'invoiced',approvalStatus:'pending',createdBy:'synthetic-r8-admin',amountExclVat:100,deductionsTotal:0,vat:15});
 put('platform_invoice_line',{id:'syn-r8-line',invoiceId:'syn-r8-invoice',sourceRow:2,baseGross:100,incentives:0,deductionAmount:0,orders:10,vat:15,invoiced:100,status:'invoiced'});
 put('pay_cycle',{id:'syn-r8-cycle',companyId:company,period:'2026-10',stage:6,status:'review',deductions:[],riders:[{rider:'2000000001',status:'ready',review:'pending',gross:100,commission:0,afterCommission:100,phase1:0,preliminaryDue:100,phase2:0,phase3:0,net:100}],invoices:[{invoiceId:'syn-r8-invoice',app:'ninja',accounts:[],deductions:[]}]});
 put('pay_cycle_line',{id:'syn-r8-cycle-line',cycleId:'syn-r8-cycle',rider:'2000000001',app:'ninja',period:'2026-10',status:'ready',platformGross:100,entitlement:100,gross:100,sources:[{sourceLineId:'syn-r8-line',accountId:'syn-r8-account',baseGross:100}]});
 for(const [section,kind]of [['terms','pay_terms'],['schemes','pay_scheme'],['eligibility','pay_eligibility']])put(kind,{id:'syn-r8-'+section,kind,app:'ninja',period:'2026-10',status:'draft',version:1,data:{}});
 put('pay_setting',{id:'syn-r8-global',app:'global',period:'2026-10',status:'draft',version:1,data:{}});
 db.close();
 await call('/api/campaigns',{title:'Synthetic R8',days:2});
 await call('/api/finance/journals/manual',{companyId:company,date:'2026-10-01',reference:'SYN-R8',description:'Synthetic',lines:[{accountCode:'512101',debit:10,credit:0},{accountCode:'212103',debit:0,credit:10}]});
 const cases=[['/api/debts','/api/debts'],['/api/debts?page=1','/api/debts'],['/api/riders','/api/riders'],['/api/pay/cycles','/api/pay/cycles'],['/api/pay/cycles/syn-r8-cycle/lines','/api/pay/cycles/{id}/lines'],['/api/platforms/ninja/invoices','/api/platforms/ninja/invoices'],['/api/platforms/ninja/invoices/syn-r8-invoice/lines','/api/platforms/ninja/invoices/{id}/lines'],...['terms','schemes','eligibility'].map(s=>['/api/pay/apps/ninja/'+s,'/api/pay/apps/{app}/'+s]),['/api/pay/settings','/api/pay/settings'],['/api/finance/journals/manual','/api/finance/journals/manual'],['/api/campaigns','/api/campaigns']];
 for(const [url,path]of cases){const value=await call(url);const live=doc.paths[path].get.responses['200'].content['application/json'].schema;const schema=saved.paths[path].get.responses['200'].content['application/json'].schema;assert.deepEqual(live,schema,'Live schema changed: '+path);const validate=ajv.compile(schema);assert.ok(validate(value),path+': '+JSON.stringify(validate.errors));const rows=Array.isArray(value)?value:value.items;assert.ok(rows.length>0,'Nonempty contract fixture: '+path);checks++;}
 const draft=await call('/api/pay/review-draft?app=ninja&period=2026-10&companyId='+company);
 const draftSchema=saved.paths['/api/pay/review-draft'].get.responses['200'].content['application/json'].schema;
 const validateDraft=ajv.compile(draftSchema);assert.ok(validateDraft(draft),JSON.stringify(validateDraft.errors));assert.ok(draft.blockers.length>0);assert.equal(draft.canCreate,false);checks++;
 for(const [url,path]of [['/api/pay/cycles/syn-r8-cycle/review','/api/pay/cycles/{id}/review'],['/api/finance/statement/rider/2000000001/monthly?period=2026-10','/api/finance/statement/rider/{iqama}/monthly']]){
  const value=await call(url),schema=saved.paths[path].get.responses['200'].content['application/json'].schema,validate=ajv.compile(schema);assert.ok(validate(value),path+': '+JSON.stringify(validate.errors));checks++;
 }
 // Exercise lifecycle routing as an ordinary accountant, including DELETE authorization.
 const password=randomBytes(24).toString('hex');
 await call('/api/users',{username:'synthetic-r8-accountant',password,role:'FinancialAccountant'});
 await call('/api/auth/login',{username:'synthetic-r8-accountant',password});
 const terms=()=>({period:'2026-10',code:'synthetic-owned',data:{name:'Synthetic terms',billingModel:'per_order',parameters:{pricePerOrder:10}}});
 const deletable=await call('/api/pay/apps/ninja/terms',terms());
 const removed=await call('/api/pay/apps/ninja/terms/'+encodeURIComponent(deletable.id),undefined,200,'DELETE');assert.equal(removed.deleted,true);checks++;
 const rejectable=await call('/api/pay/apps/ninja/terms',terms());
 const rejected=await call('/api/pay/apps/ninja/terms/'+encodeURIComponent(rejectable.id)+'/reject',{reason:'Synthetic correction'});assert.equal(rejected.status,'rejected');checks++;
 const validateDebt=ajv.compile(saved.paths['/api/debts'].get.responses['200'].content['application/json'].schema);
 assert.equal(validateDebt([{id:'synthetic',originalAmount:125,remainingAmount:100,status:'active'}]),false);checks++;
 console.log(`R8 HTTP contracts: ${checks} passed`);
} finally {child.kill('SIGTERM');await once(child,'exit');rmSync(data,{recursive:true,force:true});}
