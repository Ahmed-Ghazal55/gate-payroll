import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,mkdirSync,writeFileSync,existsSync} from 'node:fs';
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
 await page.goto(base);await page.locator('[name=username]').fill('browser-test');await page.locator('[name=password]').fill(randomBytes(24).toString('hex'));await page.locator('#auth-form button').click();await page.getByRole('heading',{name:'كل مستحق في مكانه'}).waitFor();
 await page.screenshot({path:path.join(out,'dashboard.png'),fullPage:true});
 for(const view of ['imports','riders','policies','payroll','forms','responses','audit']){await page.locator(`nav [data-view="${view}"]`).click();await page.waitForFunction(()=>!document.querySelector('#content .loading'));}
 const campaign=await page.evaluate(async()=>await(await fetch('/api/campaigns',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({title:'اختبار نموذج المناديب',days:2})})).json());assert.ok(campaign.path);
 const mobile=await browser.newPage({viewport:{width:390,height:844}});mobile.on('pageerror',e=>errors.push(e.message));await mobile.goto(base+campaign.path);await mobile.locator('#rider-choice').waitFor();
 assert.equal(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true,'Mobile page overflow');await mobile.screenshot({path:path.join(out,'rider-form-mobile.png'),fullPage:true});
 assert.deepEqual(await mobile.locator('#rider-choice').evaluate(f=>[...new Set([...f.querySelectorAll('[name]')].map(e=>e.name))].sort()),['iqama','name','workSystem']);
 await mobile.locator('[name=iqama]').fill('٢٠٠٠٠٠٠٠٠١');assert.equal(await mobile.locator('[name=iqama]').evaluate(e=>e.checkValidity()),false);
 await mobile.locator('[name=name]').fill('مندوب تجريبي');await mobile.locator('[name=iqama]').fill('2000000002');
 await mobile.locator('[name=workSystem][value=commission]').check();
 mobile.once('dialog',d=>d.accept());await mobile.locator('button[type=submit]').click();await mobile.getByRole('heading',{name:'تم تسجيل ردك بنجاح'}).waitFor();
 await page.locator('nav [data-view="responses"]').click();await page.getByText('مندوب تجريبي',{exact:true}).waitFor();await page.screenshot({path:path.join(out,'responses.png'),fullPage:true});

 // Download template and import a real XLSX through the admin UI (synthetic data only).
 const templateDownload=page.waitForEvent('download');await page.locator('a[href="/api/excel/choices/template"]').click();assert.match((await templateDownload).suggestedFilename(),/\.xlsx$/);
 const fixture=await page.evaluate(async()=>{const r=await fetch('/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({headers:['رقم الإقامة','الاسم الكامل','نظام العمل'],rows:[['2000000003','مندوب إضافي','نظام الراتب الجديد'],['2000000004','مندوب آخر','نظام العمولة السابق']]})});return [...new Uint8Array(await r.arrayBuffer())];});
 await page.locator('[data-table-import="choices"]').click();await page.locator('#table-import-campaign').selectOption(campaign.id);await page.locator('#table-import-file').setInputFiles({name:'choices.xlsx',mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:Buffer.from(fixture)});
 await page.locator('#table-import-preview').click();await page.locator('#table-import-commit:not([disabled])').waitFor();await page.locator('#table-import-commit').click();await page.locator('#modal').waitFor({state:'hidden'});await page.locator('#content').getByText('مندوب إضافي',{exact:true}).waitFor();
 const grid=page.locator('#content .data-grid');assert.equal(await grid.locator('thead [data-column-filter]').count(),10);
 await grid.getByRole('searchbox',{name:'فلتر النظام المختار',exact:true}).fill('نظام الراتب الجديد');await grid.getByRole('searchbox',{name:'فلتر المندوب',exact:true}).fill('إضافي');assert.equal(await grid.locator('tbody tr:visible').count(),1);
 const filteredDownload=page.waitForEvent('download');await grid.locator('[data-grid-export="filtered"]').click();const filteredFile=await filteredDownload;await filteredFile.saveAs(path.join(out,'filtered-choices.xlsx'));
 await page.screenshot({path:path.join(out,'choices-excel-filters.png'),fullPage:true});
 await grid.locator('[data-grid-clear]').click();assert.equal(await grid.locator('tbody tr:visible').count(),3);
 await page.locator('nav [data-view="audit"]').click();await page.locator('#content .column-filters input').first().waitFor();assert.equal(await page.locator('#content .column-filters input').count(),3);
 assert.deepEqual(errors,[]);console.log('PASS browser: initial setup, 8 admin views, mobile form layout, public submit, response visible, Excel template/import/download, combined column filters and clear, no JS errors.');
}finally{if(browser)await browser.close();if(server.exitCode===null&&!spawnError){const stopped=new Promise(r=>server.once('exit',r));server.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}rmSync(data,{recursive:true,force:true});}
