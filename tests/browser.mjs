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
 const launchOpts = {headless:true};
browser = await chromium.launch(launchOpts);
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const password=randomBytes(24).toString('hex');
 assert.equal((await page.request.post(base+'/api/auth/setup',{data:{username:'browser-test',password}})).status(),200);
 const cookies=await page.request.storageState();
 const ctx=await browser.newContext({viewport:{width:1440,height:1000},storageState:cookies});
 const admin=await ctx.newPage();admin.on('pageerror',e=>errors.push(e.message));
 assert.equal((await fetch(base+'/',{redirect:'manual'})).headers.get('location'),'/app/');
 assert.equal((await fetch(base+'/legacy/',{redirect:'manual'})).headers.get('location'),'/app/');
 if(existsSync(path.join(root,'Payroll.Api/bin/Debug/net10.0/wwwroot/app/index.html'))||existsSync(path.join(root,'Payroll.Api/wwwroot/app/index.html'))){
  await admin.goto(base+'/app/',{waitUntil:'networkidle'});
  await admin.waitForFunction(()=>!location.pathname.includes('/login')||document.body.innerText.length>0);
  for(const route of ['/app/workforce/','/app/payroll/','/app/rider-app/?tab=campaigns','/app/debts/']){
   await admin.goto(base+route,{waitUntil:'domcontentloaded'});
   assert.ok((await admin.content()).length>100,route);
  }
  await admin.screenshot({path:path.join(out,'dashboard.png'),fullPage:true});
 }
 const campaign=await page.evaluate(async()=>await(await fetch('/api/campaigns',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({title:'اختبار نموذج المناديب',days:2})})).json());assert.ok(campaign.path);
 const mobile=await browser.newPage({viewport:{width:390,height:844}});mobile.on('pageerror',e=>errors.push(e.message));await mobile.goto(base+campaign.path);await mobile.locator('#rider-choice').waitFor();
 assert.equal(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Mobile page overflow');await mobile.screenshot({path:path.join(out,'rider-form-mobile.png'),fullPage:true});
 assert.deepEqual(await mobile.locator('#rider-choice').evaluate(f=>[...new Set([...f.querySelectorAll('[name]')].map(e=>e.name))].sort()),['iqama','name','workSystem']);
 await mobile.locator('[name=iqama]').fill('٢٠٠٠٠٠٠٠٠١');assert.equal(await mobile.locator('[name=iqama]').evaluate(e=>e.checkValidity()),false);
 await mobile.locator('[name=name]').fill('مندوب تجريبي');await mobile.locator('[name=iqama]').fill('2000000002');
 await mobile.locator('[name=workSystem][value=commission]').check();
 mobile.once('dialog',d=>d.accept());await mobile.locator('button[type=submit]').click();await mobile.getByRole('heading',{name:'تم تسجيل ردك بنجاح'}).waitFor();
 const submissions=await(await page.request.get(base+'/api/submissions')).json();assert.equal(submissions.length,1);assert.equal(submissions[0].name,'مندوب تجريبي');
 const template=await page.request.get(base+'/api/excel/choices/template');assert.equal(template.status(),200);assert.match(template.headers()['content-type']||'','spreadsheetml');
 assert.deepEqual(errors,[]);console.log('PASS browser: Next redirects, admin routes when packaged, mobile form layout, public submit, Excel template, no JS errors.');
}finally{if(browser)await browser.close();if(server.exitCode===null&&!spawnError){const stopped=new Promise(r=>server.once('exit',r));server.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
