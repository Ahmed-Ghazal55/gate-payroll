import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';
const {chromium}=await import(pathToFileURL(process.env.PLAYWRIGHT_MODULE||'/tmp/gate-browser/node_modules/playwright/index.mjs'));
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-browser-test-')),out=path.join(root,'test-results');mkdirSync(out,{recursive:true});
const base='http://127.0.0.1:19348';const server=spawn((process.env.DOTNET_PATH || 'dotnet'),[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},windowsHide:true,stdio:'ignore'});
let spawnError;server.once('error',e=>{spawnError=e;});
let browser;try{
 let ready=false;for(let n=0;n<150;n++){if(spawnError)throw spawnError;if(server.exitCode!==null)throw Error('Browser server exited');try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}await new Promise(r=>setTimeout(r,100))}assert.ok(ready,'Browser server did not start');
 const launchOpts = {headless:true,...(process.env.PLAYWRIGHT_EXECUTABLE_PATH?{executablePath:process.env.PLAYWRIGHT_EXECUTABLE_PATH}:{})};
browser = await chromium.launch(launchOpts);
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const password=randomBytes(24).toString('hex');
 assert.equal((await page.request.post(base+'/api/auth/setup',{data:{username:'browser-test',password}})).status(),200);
 const cookies=await page.request.storageState();
 const ctx=await browser.newContext({viewport:{width:1440,height:1000},storageState:cookies});
 const admin=await ctx.newPage();admin.on('pageerror',e=>errors.push(e.message));
 assert.equal((await fetch(base+'/',{redirect:'manual'})).headers.get('location'),'/app/');
 {
  const legacyLoc=(await fetch(base+'/legacy/',{redirect:'manual'})).headers.get('location')||'';
  assert.ok(legacyLoc==='/app/'||legacyLoc.startsWith('/app/login'),`legacy redirect unexpected: ${legacyLoc}`);
 }
 const packaged=existsSync(path.join(root,'Payroll.Api/bin/Debug/net10.0/wwwroot/app/index.html'))||existsSync(path.join(root,'Payroll.Api/wwwroot/app/index.html'));
 assert.ok(packaged,'App pages missing: run `python3 scripts/package_web.py` after `npm --prefix web run build` before browser tests');
 await admin.goto(base+'/app/',{waitUntil:'networkidle'});
 await admin.waitForFunction(()=>!location.pathname.includes('/login')||document.body.innerText.length>0);
 for(const route of ['/app/workforce/','/app/payroll/?tab=platforms&period=2026-10','/app/approvals/','/app/rider-app/?tab=campaigns','/app/debts/?tab=debts','/app/treasury/?tab=vaults','/app/accounting/?tab=coa','/app/admin/?tab=users']){
  await admin.goto(base+route,{waitUntil:'domcontentloaded'});
  assert.ok((await admin.content()).length>100,route);
 }
 // UT1: legacy ?tab= redirects preserve ?period=YYYY-MM and land on canonical tabs without duplicate top tab bars.
 await admin.goto(base+'/app/accounting/?tab=trial&period=2026-10',{waitUntil:'networkidle'});
 await admin.waitForURL(/\/app\/accounting\/\?.*(tab=trial_balance.*period=2026-10|period=2026-10.*tab=trial_balance)/,{timeout:10000});
 assert.equal(await admin.locator('[role="tablist"]').count(),0,'accounting should have no duplicate top tab bar');
 await admin.goto(base+'/app/debts/?tab=statements&period=2026-10',{waitUntil:'networkidle'});
 await admin.waitForURL(/\/app\/debts\/\?.*(tab=statement.*period=2026-10|period=2026-10.*tab=statement)/,{timeout:10000});
 await admin.goto(base+'/app/payroll/?tab=current_cycle&period=2026-10',{waitUntil:'networkidle'});
 await admin.waitForURL(/\/app\/payroll\/\?.*(tab=current-cycle.*period=2026-10|period=2026-10.*tab=current-cycle)/,{timeout:10000});
 // UT1: page month input reads URL ?period=2026-10 and changing it updates the URL ?period=.
 const monthInput=admin.locator('main input[type="month"]').first();
 await monthInput.waitFor({state:'visible',timeout:10000});
 assert.equal(await monthInput.inputValue(),'2026-10','payroll page picker must read URL ?period=');
 await monthInput.fill('2026-09');
 await admin.waitForURL(/period=2026-09/,{timeout:10000});
 await admin.screenshot({path:path.join(out,'dashboard.png'),fullPage:true});
 const campaignRes=await page.request.post(base+'/api/campaigns',{data:{title:'اختبار نموذج المناديب',days:2}});assert.equal(campaignRes.status(),200);const campaign=await campaignRes.json();assert.ok(campaign.path);
 const mobile=await browser.newPage({viewport:{width:390,height:844}});mobile.on('pageerror',e=>errors.push(e.message));await mobile.goto(base+campaign.path);await mobile.locator('#rider-choice').waitFor();
 assert.equal(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Mobile page overflow');await mobile.screenshot({path:path.join(out,'rider-form-mobile.png'),fullPage:true});
 assert.deepEqual(await mobile.locator('#rider-choice').evaluate(f=>[...new Set([...f.querySelectorAll('[name]')].map(e=>e.name))].sort()),['iqama','name','workSystem']);
 await mobile.locator('[name=iqama]').fill('٢٠٠٠٠٠٠٠٠١');assert.equal(await mobile.locator('[name=iqama]').evaluate(e=>e.checkValidity()),false);
 await mobile.locator('[name=name]').fill('مندوب تجريبي');await mobile.locator('[name=iqama]').fill('2000000002');
 // CW1: choosing commission opens a warning; "back" clears the choice; "confirm" keeps it.
 await mobile.locator('[name=workSystem][value=commission]').check();
 await mobile.locator('#commission-dialog[open]').waitFor();
 assert.match(await mobile.locator('#commission-dialog').innerText(),/لا تتنازل الشركة عن أي ريال/);
 await mobile.locator('#commission-back').click();
 assert.equal(await mobile.locator('[name=workSystem][value=commission]').isChecked(),false,'back must clear the commission choice');
 assert.equal(await mobile.locator('#commission-dialog[open]').count(),0);
 await mobile.locator('[name=workSystem][value=commission]').check();
 await mobile.locator('#commission-confirm').click();
 assert.equal(await mobile.locator('[name=workSystem][value=commission]').isChecked(),true);
 mobile.once('dialog',d=>d.accept());await mobile.locator('button[type=submit]').click();await mobile.getByRole('heading',{name:'تم تسجيل ردك بنجاح'}).waitFor();
 // DV1 (Ghazal 2026-11-23): the same device cannot submit a second reply for a different Iqama.
 await mobile.reload();await mobile.locator('#rider-choice').waitFor();await mobile.locator('#device-notice:not([hidden])').waitFor();
 await mobile.locator('[name=iqama]').fill('2000000099');await mobile.locator('[name=name]').fill('Other Person');await mobile.locator('[name=workSystem][value=tiers]').check();await mobile.locator('button[type=submit]').click();
 assert.match(await mobile.locator('#form-error').innerText(),/من هذا الجهاز/);
 const submissions=await(await page.request.get(base+'/api/submissions')).json();assert.equal(submissions.length,1);assert.equal(submissions[0].name,'مندوب تجريبي');
 const template=await page.request.get(base+'/api/excel/choices/template');assert.equal(template.status(),200);assert.match(template.headers()['content-type']||'',/spreadsheetml/);
 assert.deepEqual(errors,[]);console.log('PASS browser: Next redirects, packaged app routes, UT1 period/tabs, mobile form layout, public submit, Excel template, no JS errors.');
}finally{if(browser)await browser.close();if(server.exitCode===null&&!spawnError){const stopped=new Promise(r=>server.once('exit',r));server.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
