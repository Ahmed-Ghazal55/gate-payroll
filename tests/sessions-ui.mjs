// R6: /app rider-app «الحسابات والأجهزة» lists sessions and revokes with a reason. Synthetic data, temporary SQLite, real CSP.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {createServer} from 'node:net';
import assert from 'node:assert/strict';
const {chromium}=await import(process.env.PLAYWRIGHT_MODULE||'/tmp/gate-browser/node_modules/playwright/index.mjs');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-sessions-ui-'));const rider='2000000731';
let child,base,browser,cookie='';
async function start(){
 const probe=createServer();await new Promise((ok,no)=>{probe.once('error',no);probe.listen(0,'127.0.0.1',ok);});
 base='http://127.0.0.1:'+probe.address().port;await new Promise(ok=>probe.close(ok));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('API timeout');
}
async function req(route,body,status=200,token){
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookie?{Cookie:cookie}:{}),...(token?{Authorization:'Bearer '+token}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,`${route}: ${raw.slice(0,160)}`);if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
}
async function login(device){const g=await req('/api/inquiry-access',{iqama:rider,scope:'mobile',identityReviewed:true});const saved=cookie;cookie='';const r=await req('/api/rider-mobile/login',{iqamaNo:rider,phoneLast4:'4567',code:g.code,language:'ar',fcmDeviceToken:device});cookie=saved;return r.accessToken;}
try{
 await start();
 await req('/api/auth/setup',{username:'syn-sessions-admin',password:randomBytes(18).toString('hex')});
 await req('/api/riders',{iqama:rider,name:'Synthetic Session Rider',phone:'0501234567',financeSponsorId:'BW',status:'Active'});
 const t1=await login('synthetic-device-one'),t2=await login('synthetic-device-two');
 browser=await chromium.launch();const ctx=await browser.newContext({viewport:{width:1440,height:900}});
 const [name,value]=cookie.split('=');await ctx.addCookies([{name,value,url:base}]);
 const page=await ctx.newPage();const csp=[];page.on('console',m=>{if(m.text().startsWith('WIDE'))console.error(m.text());if(/Content Security Policy/i.test(m.text()))csp.push(m.text());});
 await page.goto(base+'/app/rider-app/?tab=accounts',{waitUntil:'networkidle'});
 await page.waitForSelector('[data-testid="rider-sessions"] tbody tr');
 assert.equal(await page.locator('[data-testid="rider-sessions"] tbody tr').count(),2,'two active sessions listed');
 const html=await page.content();assert.ok(!html.includes(t1)&&!html.includes(t2),'no bearer token in the page');
 assert.ok(!html.includes('غير متاح بعد — بانتظار اكتمال واجهات خادم الجلسات'),'placeholder removed');
 // Revoke one: reason required.
 await page.locator('[data-testid="rider-sessions"] tbody tr button[data-session]').first().click();
 assert.ok(await page.locator('#sessions-revoke-confirm').isDisabled(),'confirm disabled without a reason');
 await page.fill('#sessions-revoke-reason','Synthetic lost phone');await page.click('#sessions-revoke-confirm');
 await page.waitForFunction(()=>document.querySelectorAll('[data-testid="rider-sessions"] tbody tr').length===1);
 const left=await req('/api/rider-mobile/sessions?iqama='+rider+'&status=active');assert.equal(left.length,1);
 const revoked=(await req('/api/rider-mobile/sessions?iqama='+rider+'&status=revoked'))[0];
 // Exactly one of the two tokens is refused now.
 const s1=(await fetch(base+'/api/rider-mobile/sessions/mine',{headers:{Authorization:'Bearer '+t1}})).status,s2=(await fetch(base+'/api/rider-mobile/sessions/mine',{headers:{Authorization:'Bearer '+t2}})).status;
 assert.deepEqual([s1,s2].sort(),[200,401]);assert.ok(revoked.id.length===16);
 // Revoke all for the rider.
 await page.fill('#sessions-iqama',rider);await page.press('#sessions-iqama','Enter');
 await page.click('#sessions-revoke-all');await page.fill('#sessions-revoke-reason','Synthetic account reset');await page.click('#sessions-revoke-confirm');
 await page.waitForSelector('text=لا توجد جلسات');
 for(const t of [t1,t2])assert.equal((await fetch(base+'/api/rider-mobile/sessions/mine',{headers:{Authorization:'Bearer '+t}})).status,401);
 await page.setViewportSize({width:360,height:800});await page.selectOption('#sessions-status','revoked');await page.waitForSelector('[data-testid="rider-sessions"] tbody tr');
 const wide=await page.evaluate(()=>{const w=window.innerWidth;const bad=[...document.querySelectorAll('body *')].filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&(r.right>w+1||r.left<-1)&&!e.closest('.overflow-x-auto')}).slice(0,4).map(e=>e.tagName+'.'+String(e.className).slice(0,60)+' '+Math.round(e.getBoundingClientRect().left)+'..'+Math.round(e.getBoundingClientRect().right));if(bad.length)console.log('WIDE',JSON.stringify(bad));return document.documentElement.scrollWidth>w+1;});assert.ok(!wide,'no page-level horizontal scroll at 360px');
 assert.equal(csp.length,0,'no CSP errors');
 console.log('PASS sessions UI: list, no token exposure, reason-required revoke, revoke-all, 360px, real CSP');
}finally{if(browser)await browser.close();if(child&&child.exitCode===null){const e=once(child,'exit');child.kill();await e;}rmSync(data,{recursive:true,force:true});}
