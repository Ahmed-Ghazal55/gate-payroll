// Track M R6 (BR §25.8): supervisor converts / declines rider-app asks in /app/debts. Synthetic data, real CSP.
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
const {chromium}=await import(process.env.PLAYWRIGHT_MODULE||'/tmp/gate-browser/node_modules/playwright/index.mjs');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-asks-ui-'));const iqama='2000000761';let child,base,browser;const jars={};
async function start(){
 const probe=createServer();await new Promise((ok,no)=>{probe.once('error',no);probe.listen(0,'127.0.0.1',ok);});
 base='http://127.0.0.1:'+probe.address().port;await new Promise(ok=>probe.close(ok));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('API timeout');
}
function as(user,token){return async(route,body,status=200)=>{
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(jars[user]?{Cookie:jars[user]}:{}),...(token?{Authorization:'Bearer '+token}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const text=await r.text();assert.equal(r.status,status,`${user} ${route}: ${text.slice(0,200)}`);
 if(r.headers.get('set-cookie'))jars[user]=r.headers.get('set-cookie').split(';')[0];return text?JSON.parse(text):null;};}
const pw=()=>randomBytes(18).toString('hex');
try{
 await start();const admin=as('admin');await admin('/api/auth/setup',{username:'syn-askui-admin',password:pw()});await seedAccountingCash(admin);
 await admin('/api/riders',{iqama,name:'Ask UI Synthetic',phone:'0501234567',financeSponsorId:'BW',status:'Active'});
 for(const [name,perms] of [['supervisor',['advances.request']],['accountant',['advances.review']]]){const p=pw();await admin('/api/users',{username:'syn-ui-'+name,password:p,role:'Reviewer',customPermissions:perms});await as(name)('/api/auth/login',{username:'syn-ui-'+name,password:p});}
 await admin('/api/org/memberships',{userId:'syn-ui-accountant',department:'FIN',rank:'A',validFrom:'2026-01-01'});
 await admin('/api/org/routing',{operation:'advance.review',department:'FIN',minRank:'B',users:['syn-ui-accountant'],validFrom:'2026-01-01'});
 await admin('/api/org/supervision',{supervisorUserId:'syn-ui-supervisor',riderIqama:iqama,validFrom:'2026-01-01'});
 const g=await admin('/api/inquiry-access',{iqama,scope:'mobile',identityReviewed:true});
 const rider=as('rider',(await as('anon')('/api/rider-mobile/login',{iqamaNo:iqama,phoneLast4:'4567',code:g.code,language:'ar'})).accessToken);
 const a1=await rider('/api/rider-mobile/requests',{type:'cash',amount:350,reason:'Synthetic rent'});
 browser=await chromium.launch();const ctx=await browser.newContext({viewport:{width:1440,height:900}});
 const [name,value]=jars.supervisor.split('=');await ctx.addCookies([{name,value,url:base}]);
 const page=await ctx.newPage();const csp=[];page.on('console',m=>{if(/Content Security Policy/i.test(m.text()))csp.push(m.text());});
 await page.goto(base+'/app/debts/?tab=advance_requests',{waitUntil:'networkidle'});
 await page.waitForSelector(`[data-testid="rider-asks"] [data-ask="${a1.id}"]`);
 await page.locator(`[data-ask="${a1.id}"] button`,{hasText:'تحويل'}).click();
 await page.fill('#rider-ask-amount','300');await page.click('#rider-ask-submit');
 await page.waitForSelector('[data-testid="rider-asks"]',{state:'detached'});
 const mine=(await rider('/api/rider-mobile/requests'))[0];assert.equal(mine.status,'converted');assert.equal(mine.advanceStatus,'under_review');assert.equal(mine.amount,300);
 // Second ask: decline needs a reason; the rider sees it.
 await as('accountant')(`/api/advance-requests/${(await as('supervisor')('/api/advance-requests?role=mine'))[0].id}/review`,{action:'reject',note:'Synthetic close'});
 const a2=await rider('/api/rider-mobile/requests',{type:'bank',amount:120,reason:'Synthetic fee'});
 await page.reload({waitUntil:'networkidle'});await page.waitForSelector(`[data-ask="${a2.id}"]`);
 await page.locator(`[data-ask="${a2.id}"] button`,{hasText:'رفض'}).click();
 assert.ok(await page.locator('#rider-ask-submit').isDisabled(),'decline needs a reason');
 await page.fill('#rider-ask-text','Synthetic: please ask next month');await page.click('#rider-ask-submit');
 await page.waitForSelector('[data-testid="rider-asks"]',{state:'detached'});
 const declined=(await rider('/api/rider-mobile/requests')).find(x=>x.id===a2.id);assert.equal(declined.status,'declined');assert.equal(declined.note,'Synthetic: please ask next month');
 assert.equal(csp.length,0,'no CSP errors');
 console.log('PASS rider asks UI: supervisor card, convert with edited amount, reason-required decline, rider sees outcome, real CSP');
}finally{if(browser)await browser.close();if(child&&child.exitCode===null){const e=once(child,'exit');child.kill();await e;}rmSync(data,{recursive:true,force:true});}
