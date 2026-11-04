// R4 contracts use only a disposable synthetic SQLite store and loopback API.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,rmSync} from 'node:fs';
import {DatabaseSync} from 'node:sqlite';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-r4-')),base='http://127.0.0.1:19492';
let child,cookie='',checks=0;
async function start(){child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',PAYROLL_DATA_DIR:data,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base},stdio:'ignore'});for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('Synthetic R4 API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('Synthetic R4 API timeout');}
async function req(route,body,status=200,auth=true){const r=await fetch(base+route,{method:body===undefined?'GET':'POST',redirect:'manual',headers:{...(cookie&&auth?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});assert.equal(r.status,status,route);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return r.headers.get('content-type')?.includes('application/json')?r.json():r.text();}
const confirm=p=>({rows:p.rows,fingerprint:p.fingerprint,confirm:true});
try{
 await start();await req('/api/pay/catalog',undefined,401,false);
 const password=randomBytes(24).toString('hex');await req('/api/auth/setup',{username:'synthetic-r4-maker',password});const makerCookie=cookie;
 await req('/api/users',{username:'synthetic-r4-checker',password,role:'FinancialAccountant'});
 const catalog=await req('/api/pay/catalog');assert.equal(catalog.categories.length,6);assert.equal(catalog.apps.find(a=>a.app==='hs').receivable,'112805');
 const seed=new DatabaseSync(path.join(data,'payroll.db'));const preserved=[];
 for(const kind of ['run','journal','debt','custom_response','campaign','submission']){const id='synthetic-untouched-'+kind,raw=' { "id": "'+id+'", "legacy": [ 1, 2 ] } ';seed.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(id,kind,raw);preserved.push({id,kind,body:raw});}seed.close();
 const global=await req('/api/pay/settings',{effectiveFrom:'2026-10-01',fromDefaults:true});assert.equal(global.status,'draft');await req('/api/pay/settings/'+encodeURIComponent(global.id)+'/approve',{},403);
 const terms=await req('/api/pay/apps/hs/terms?period=2026-10',{data:{name:'Synthetic terms',billingModel:'per_order',parameters:{pricePerOrder:10,companyCostAccount:'526402'}}});
 const rules=await req('/api/pay/apps/hs/eligibility',{period:'2026-10',data:{name:'Synthetic eligibility',rules:[{metric:'orders',operator:'gte',threshold:10,effect:{type:'zero'}}]}});
 const cells=Object.fromEntries(catalog.categories.map(c=>[c,{enabled:true,dailyCommission:10,chargeCompanyVehicle:false}]));
 const schemeBody={period:'2026-10',code:'synthetic-commission',data:{name:'Synthetic commission',type:'daily_commission',accountingRole:'agent',parameters:{categories:cells},eligibilitySetId:rules.id}};
 const scheme=await req('/api/pay/apps/hs/schemes',schemeBody);await req('/api/pay/apps/keeta/schemes',schemeBody,400);
 await req('/api/auth/login',{username:'synthetic-r4-checker',password});const checkerCookie=cookie;
 await req('/api/pay/settings/'+encodeURIComponent(global.id)+'/approve',{});await req('/api/pay/apps/hs/terms/'+encodeURIComponent(terms.id)+'/approve',{});
 await req('/api/pay/apps/hs/schemes/'+encodeURIComponent(scheme.id)+'/approve',{},400);
 await req('/api/pay/apps/hs/eligibility/'+encodeURIComponent(rules.id)+'/approve',{});await req('/api/pay/apps/hs/schemes/'+encodeURIComponent(scheme.id)+'/approve',{});
 cookie=makerCookie;const nextTerms=await req('/api/pay/apps/hs/terms',{period:'2026-10',baseVersionId:terms.id,data:{...terms.data,parameters:{pricePerOrder:12}}});assert.equal(nextTerms.version,2);assert.equal(nextTerms.status,'draft');
 assert.equal((await req('/api/pay/apps/hs/terms?period=2026-10')).total,2);
 const copied=await req('/api/pay/apps/hs/copy?from=2026-10&to=2026-11',{});assert.equal(copied.created,3);assert.ok(copied.drafts.every(d=>d.status==='draft'));
 assert.equal((await req('/api/pay/apps/hs/copy?from=2026-10&to=2026-11',{})).created,0);
 const companies=await req('/api/companies');await req('/api/users',{username:'synthetic-r4-scoped',password,role:'FinancialAccountant',financialScope:{sponsorIds:[companies[0].id],platforms:['HungerStation'],accountCodes:[],categoryCodes:[]}});
 await req('/api/auth/login',{username:'synthetic-r4-scoped',password});await req('/api/pay/apps/hs/terms?period=2026-10');await req('/api/pay/apps/keeta/terms?period=2026-10',undefined,403);await req('/api/pay/settings',{effectiveFrom:'2026-11-01',fromDefaults:true},403);
 cookie=makerCookie;const openapi=await req('/openapi/v1.json');assert.ok(openapi.paths['/api/pay/apps/{app}/terms']);assert.ok(openapi.paths['/api/pay/settings/{id}/approve']);
 const db=new DatabaseSync(path.join(data,'payroll.db'));for(const row of preserved)assert.deepEqual(db.prepare('SELECT id,kind,body FROM records WHERE id=?').get(row.id),Object.assign(Object.create(null),row));
 assert.equal(JSON.parse(db.prepare('SELECT body FROM records WHERE id=?').get(terms.id).body).status,'approved');db.close();
 console.log('PASS R4 backend: '+checks+' HTTP checks; versioned settings, independent approval, copy, scope and legacy preservation.');
}finally{if(child&&child.exitCode===null){const exited=once(child,'exit');child.kill();await exited;}rmSync(data,{recursive:true,force:true});}
