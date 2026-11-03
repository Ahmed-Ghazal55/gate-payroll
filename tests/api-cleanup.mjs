// All fixtures live in a new temporary SQLite store. No production settings or data.
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,readFileSync,mkdirSync,writeFileSync} from 'node:fs';
import http from 'node:http';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-api-cleanup-'));
const base='http://127.0.0.1:19473';
const webroot=path.join(data,'synthetic-webroot');mkdirSync(path.join(webroot,'app/_next'),{recursive:true});mkdirSync(path.join(webroot,'app/forms'),{recursive:true});writeFileSync(path.join(webroot,'app/index.html'),'<html>synthetic Next entry</html>');writeFileSync(path.join(webroot,'app/forms/index.html'),'<html>synthetic forms entry</html>');writeFileSync(path.join(webroot,'app/_next/synthetic.js'),'synthetic asset');
const proc=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll'),'--webroot',webroot],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_WEBROOT:webroot,ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:['ignore','pipe','pipe']});
let cookie='',admin='',checks=0,logs='';proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,method=body===undefined?'GET':'POST',status=200){const r=await fetch(base+route,{method,headers:{...(cookie?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});const text=await r.text();assert.equal(r.status,status,`${method} ${route}: ${text}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return text?JSON.parse(text):null;}
try{
 let ready=false;for(let n=0;n<150;n++){if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready,'API ready');
 for(const route of ['/app','/app/index.html']){const r=await fetch(base+route);assert.equal(r.status,200,route);assert.equal(await r.text(),'<html>synthetic Next entry</html>');checks++;}
 for(const route of ['/app/forms/','/app/forms']){const r=await fetch(base+route);assert.equal(r.status,200,route);assert.equal(await r.text(),'<html>synthetic forms entry</html>');checks++;}
 const asset=await fetch(base+'/app/_next/synthetic.js');assert.equal(await asset.text(),'synthetic asset');checks++;
 for(const route of ['/app/missing.js','/app/unknown.json','/app/x/../../appsettings.json']){assert.equal((await fetch(base+route)).status,404,route);checks++;}
 const rawStatus=await new Promise(r=>{http.get({host:'127.0.0.1',port:19473,path:'/app/x/../../appsettings.json'},res=>r(res.statusCode)).on('error',()=>r(400));});assert.ok(rawStatus===400||rawStatus===404,'raw traversal refused');checks++;
 await req('/openapi/v1.json',undefined,'GET',401);
 const password=randomBytes(24).toString('hex');
 await req('/api/auth/setup',{username:'synthetic-admin',password});admin=cookie;
 const doc=await req('/openapi/v1.json');
 for(const [method,p] of [['get','/api/template.csv'],['get','/api/payroll/fingerprint'],['get','/api/imports'],['get','/api/debts/settlements'],['post','/api/public/forms/{token}/resume'],['get','/api/system/release-check']]){assert.equal(doc.paths[p][method].deprecated,true);checks++;}
 assert.ok(!doc.paths['/api/riders'].get.deprecated);
 const index=readFileSync(path.join(root,'docs/api/ENDPOINTS.md'),'utf8');
 for(const line of index.split('\n')){const m=line.match(/^\| (GET|POST|PUT|DELETE|PATCH) \| `(\/api\/[^`]+)`/);if(m){assert.ok(doc.paths[m[2]]?.[m[1].toLowerCase()],`OpenAPI missing ${m[1]} ${m[2]}`);assert.ok(doc.paths[m[2]][m[1].toLowerCase()].tags?.length);assert.ok(doc.paths[m[2]][m[1].toLowerCase()].summary);checks++;}}
 for(const [p,methods] of Object.entries(doc.paths)){if(p.startsWith('/api/'))for(const m of Object.keys(methods)){if(['get','post','put','delete','patch'].includes(m))assert.ok(index.includes(`| ${m.toUpperCase()} | \`${p}\``),`Inventory missing ${m} ${p}`);}}
 await req('/api/users',{username:'synthetic-reader',password,role:'Reviewer'});
 await req('/api/auth/login',{username:'synthetic-reader',password});
 await req('/openapi/v1.json',undefined,'GET',403);cookie=admin;
 // Company CRUD, registrations, attachments, scope and audit all use synthetic input.
 assert.equal((await req('/api/companies')).length,3);
 const company=await req('/api/companies',{id:'SYNTHETIC-CO',nameAr:'شركة مصطنعة'});
 assert.equal(company.sponsorId,company.id);
 await req(`/api/companies/${company.id}`,{nameAr:'شركة اختبار مصطنعة'},'PUT');
 assert.equal((await req('/api/finance/sponsors')).find(x=>x.id===company.id).nameAr,'شركة اختبار مصطنعة');
 await req('/api/search?q=x',undefined,'GET',400);
 const matches=await req('/api/search?q='+encodeURIComponent('اختبار'));assert.ok(matches.some(x=>x.type==='company'&&x.id===company.id&&x.url.includes('/app/companies')));assert.ok(matches.length<=20);
 const registration=await req(`/api/companies/${company.id}/registrations`,{number:'SYN-CR',city:'Synthetic City',issueDate:'2026-01-01',expiryDate:'2026-11-04'});
 assert.equal((await req(`/api/companies/${company.id}/registrations/${registration.id}`)).companyId,company.id);
 await req(`/api/companies/${company.id}/registrations/${registration.id}`,{number:'SYN-CR-UPDATED'},'PUT');
 const attachment=await req(`/api/companies/${company.id}/documents`,{type:'tax',number:'SYN-DOC',expiryDate:'2026-11-04',fileName:'synthetic.pdf',dataBase64:Buffer.from('%PDF-1.4 synthetic').toString('base64')});
 await req(`/api/companies/${company.id}/documents/${attachment.id}`,{number:'SYN-DOC-UPDATED'},'PUT');
 const download=await fetch(base+attachment.file,{headers:{Cookie:admin}});assert.equal(download.status,200);assert.equal(await download.text(),'%PDF-1.4 synthetic');checks++;
 assert.ok((await req('/api/companies/expiring?days=3650')).some(x=>x.id===registration.id));
 await req(`/api/companies/${company.id}/registrations/${registration.id}`,undefined,'DELETE');
 await req(`/api/companies/${company.id}/documents/${attachment.id}`,undefined,'DELETE');
 assert.equal((await req(`/api/companies/${company.id}/registrations`)).length,0);assert.equal((await req(`/api/companies/${company.id}/documents`)).length,0);
 await req(`/api/companies/${company.id}`,undefined,'DELETE');assert.equal((await req(`/api/companies/${company.id}`)).status,'archived');
 assert.ok((await req('/api/audit')).some(x=>x.action==='company_document.archived'));
 await req('/api/auth/login',{username:'synthetic-reader',password});await req('/api/companies',undefined,'GET',403);cookie=admin;
 // Opt-in pagination: untouched legacy bytes, bounded pages, search and sorting.
 for(const suffix of ['riders','debts','finance/journals','submissions','audit','ledger','approvals','campaigns','forms/builder']){
  const a=await fetch(base+'/api/'+suffix,{headers:{Cookie:admin}});const original=await a.text();assert.equal(a.status,200);
  const b=await fetch(base+'/api/'+suffix+'?q=nonexistent&sort=-name&pageSize=invalid',{headers:{Cookie:admin}});assert.equal(await b.text(),original,'Legacy bytes changed: '+suffix);
  const paged=await req('/api/'+suffix+'?page=1&pageSize=999');assert.ok(Array.isArray(paged.items));assert.equal(paged.pageSize,200);assert.equal(paged.total,JSON.parse(original).length);checks++;
 }
 await req('/api/riders?page=0',undefined,'GET',400);
 // Read state belongs to the user; personal and permission-gated notifications stay private.
 const general=await req('/api/notifications/center',{titleAr:'إشعار عام مصطنع',bodyAr:'اختبار'});
 const personal=await req('/api/notifications/center',{titleAr:'إشعار خاص مصطنع',bodyAr:'اختبار',assignedTo:'synthetic-admin'});
 let inbox=await req('/api/notifications?category=general&unread=true&page=1');assert.ok(inbox.items.some(x=>x.id===general.id));
 await req(`/api/notifications/${general.id}/read`,{});assert.ok(!(await req('/api/notifications?unread=true')).some(x=>x.id===general.id));
 await req('/api/auth/login',{username:'synthetic-reader',password});
 inbox=await req('/api/notifications?unread=true');assert.ok(inbox.some(x=>x.id===general.id));assert.ok(!inbox.some(x=>x.id===personal.id));
 await req(`/api/notifications/${personal.id}/read`,{},'POST',404);
 await req('/api/notifications/read-all',{});assert.equal((await req('/api/notifications/summary')).general,0);
 cookie=admin;assert.ok((await req('/api/notifications?category=personal')).some(x=>x.id===personal.id));
 // Appendix B: edit alias, ownership, errors, lockout and rate limiting.
 const reader=(await req('/api/users')).find(x=>x.username==='synthetic-reader');
 await req(`/api/users/${reader.id}`,{department:'OPS'},'PUT');
 await req(`/api/users/${reader.id}/status`,{department:'OPS'},'PUT');
 const task=await req('/api/notifications/center',{tab:'todo',titleAr:'مهمة مصطنعة',bodyAr:'اختبار',assignedTo:'synthetic-reader'});
 await req('/api/auth/login',{username:'synthetic-reader',password});
 assert.equal((await req(`/api/notifications/center/${task.id}/toggle-todo`,{})).completed,true);
 assert.equal((await req('/api/campaigns/missing',{},'PUT',403)).code,'forbidden');
 assert.equal((await req('/api/users',undefined,'GET',403)).code,'forbidden');
 cookie=''; assert.equal((await req('/api/me',undefined,'GET',401)).code,'unauthorized');
 cookie=admin;
 assert.equal((await req('/api/rider-mobile/broadcast',{},'POST',400)).code,'invalid_request');
 assert.equal((await req('/api/not-a-route',undefined,'GET',404)).code,'not_found');
 for(let i=0;i<5;i++) assert.equal((await req('/api/auth/login',{username:'synthetic-reader',password:'invalid'},'POST',401)).code,'unauthorized');
 assert.equal((await req('/api/auth/login',{username:'synthetic-reader',password:'invalid'},'POST',423)).code,'account_locked');
 const campaign=await req('/api/campaigns',{title:'Synthetic responses',days:2});
 const token=campaign.path.split("/").at(-1);const answer={iqama:'2000000099',name:'Synthetic Answer',workSystem:'commission'};
 await req(`/api/public/forms/${token}`,answer);
 const duplicate=await req(`/api/public/forms/${token}`,answer,'POST',409);assert.equal(duplicate.code,'already_submitted');assert.equal(duplicate.alreadySubmitted,true);assert.equal(duplicate.iqama,answer.iqama);
 const responsePage=await req(`/api/responses?source=campaign&status=pending&campaignId=${campaign.id}&page=1`);assert.equal(responsePage.total,1);assert.equal(responsePage.items[0].source,'campaign');assert.ok(responsePage.items[0].reviewUrl.endsWith('/review'));
 await req('/api/responses?source=invalid',undefined,'GET',400);
 let limited=false;for(let i=0;i<35;i++){const r=await fetch(base+'/api/public/forms/missing');if(r.status===429){assert.equal((await r.json()).code,'rate_limited');limited=true;break;}}assert.ok(limited);checks++;
 console.log(`PASS API cleanup: ${checks} checks`);
}finally{proc.kill();await new Promise(resolve=>{if(proc.exitCode!==null||proc.signalCode!==null)resolve();else proc.once('exit',resolve);});rmSync(data,{recursive:true,force:true});}
