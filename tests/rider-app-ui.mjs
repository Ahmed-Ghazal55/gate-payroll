// Track M R6: rider app tabs (statement → notifications → debts → devices) and server-side logout. Synthetic data, real CSP.
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
const data=mkdtempSync(path.join(tmpdir(),'gate-rider-app-'));const iqama='2000000741';
let child,base,browser,cookie='';
async function start(){
 const probe=createServer();await new Promise((ok,no)=>{probe.once('error',no);probe.listen(0,'127.0.0.1',ok);});
 base='http://127.0.0.1:'+probe.address().port;await new Promise(ok=>probe.close(ok));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('API timeout');
}
async function req(route,body,status=200){
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookie?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,`${route}: ${raw.slice(0,160)}`);if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
}
try{
 await start();
 await req('/api/auth/setup',{username:'syn-riderapp-admin',password:randomBytes(18).toString('hex')});
 await seedAccountingCash(req);
 await req('/api/riders',{iqama,name:'Synthetic App Rider',phone:'0501234567',financeSponsorId:'BW',status:'Active'});
 await req('/api/debts',{iqama,category:'WorkPermit',entryMode:'Accrual',totalAmount:500,monthlyInstallment:100,creditorSponsorId:'BW',sourceRef:'syn-app-debt',agreementReviewed:true});
 await req('/api/rider-mobile/broadcast',{iqama,category:'operational',titleAr:'إشعار تجريبي',bodyAr:'نص تجريبي للمندوب',requiresAck:true});
 const grant=await req('/api/inquiry-access',{iqama,scope:'mobile',identityReviewed:true});
 browser=await chromium.launch();const page=await (await browser.newContext({viewport:{width:360,height:780}})).newPage();
 const csp=[];page.on('console',m=>{if(/Content Security Policy/i.test(m.text()))csp.push(m.text());});
 page.on('dialog',d=>{throw Error('unexpected dialog '+d.message());});
 await page.goto(new URL(grant.url.replace(/^https?:\/\/[^/]+/,''),base).href,{waitUntil:'networkidle'});
 await page.fill('[name=phoneLast4]','4567');await page.click('#mob-login-form button');
 // Statement is the first tab; no approved run yet → clear empty state, not an error.
 await page.waitForSelector('#mob-period');await page.waitForSelector('#mob-statement-empty');
 assert.equal(await page.getAttribute('[data-tab=statement]','aria-selected'),'true');
 assert.match(await page.textContent('[data-tab=notifications]'),/\(1\)/,'unread count on the tab');
 await page.click('[data-tab=notifications]');await page.waitForSelector('#mob-notifications [data-ack]');
 await page.click('#mob-notifications [data-ack]');await page.waitForSelector('#mob-notifications .tag.good');
 assert.doesNotMatch(await page.textContent('[data-tab=notifications]'),/\(1\)/,'count clears after acknowledgement');
 await page.click('[data-tab=debts]');await page.waitForSelector('#mob-debts');assert.match(await page.textContent('#mob-debts'),/500\.00/);
 await page.click('[data-tab=devices]');await page.waitForSelector('#mob-devices .tag.good');
 const token=await page.evaluate(()=>JSON.parse(sessionStorage.getItem('gate_rider_mob')).accessToken);
 assert.ok(!(await page.content()).includes(token),'token never rendered');
 const wide=await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth+1);assert.ok(!wide,'fits 360px');
 await page.click('#mob-logout');await page.waitForSelector('#mob-login-form');
 assert.equal((await fetch(base+'/api/rider-mobile/sessions/mine',{headers:{Authorization:'Bearer '+token}})).status,401,'logout revokes on the server');
 assert.equal(csp.length,0,'no CSP errors');
 console.log('PASS rider app UI: statement first, notifications count+ack, debts, devices, server logout, 360px, real CSP');
}finally{if(browser)await browser.close();if(child&&child.exitCode===null){const e=once(child,'exit');child.kill();await e;}rmSync(data,{recursive:true,force:true});}
