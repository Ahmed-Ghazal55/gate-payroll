// Synthetic HTTP contracts for invoice imports and operation review. Loopback/SQLite only.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-r6c-http-'));
let child,base,checks=0;const cookies={};
async function call(user,route,body,status=200){
 const multipart=body instanceof FormData;
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookies[user]?{Cookie:cookies[user]}:{}),...(body===undefined||multipart?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:multipart?body:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,route+': '+raw.slice(0,160));checks++;if(r.headers.get('set-cookie'))cookies[user]=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
}
try{
 const probe=createServer();await new Promise((resolve,reject)=>{probe.once('error',reject);probe.listen(0,'127.0.0.1',resolve);});base='http://127.0.0.1:'+probe.address().port;await new Promise(resolve=>probe.close(resolve));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,ConnectionStrings__DefaultConnection:'',Jobs__Enabled:'false',Vouchers__Secret:randomBytes(32).toString('hex'),PAYROLL_DATA_DIR:data},stdio:'ignore'});
 let ready=false;for(let i=0;i<150;i++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready);
 await call('anonymous','/api/platforms/keeta/invoices',undefined,401);
 const password=randomBytes(24).toString('hex');await call('admin','/api/auth/setup',{username:'syn-r6c-admin',password});
 await call('admin','/api/users',{username:'syn-r6c-checker',password,role:'FinancialAccountant',customPermissions:['pay.view','pay.manage','pay.approve','imports.view']});await call('checker','/api/auth/login',{username:'syn-r6c-checker',password});
 const company=(await call('admin','/api/companies'))[0].id;
 const db=new DatabaseSync(path.join(data,'payroll.db'));db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run('syn-keeta-account','platform_account',JSON.stringify({id:'syn-keeta-account',externalAccountId:'SYN-K1',companyId:company,platformCode:'keeta'}));db.close();
 const catalog=await call('admin','/api/imports/catalog');assert.equal(catalog.find(s=>s.key==='keeta_invoice').status,'ready');
 const response=await fetch(base+'/api/excel/export',{method:'POST',headers:{'Content-Type':'application/json',Cookie:cookies.admin},body:JSON.stringify({headers:['المعرف','الطلبات المُسلمة','الإجمالي','الخصم','إجمالي المبلغ المستحق','صافي الراتب'],rows:[['SYN-K1',10,100,0,115,9999]],numericColumns:[1,2,3,4,5]})});assert.equal(response.status,200);checks++;const bytes=await response.arrayBuffer();
 function upload(preview){const form=new FormData();for(const [key,value]of Object.entries({companyId:company,period:'2026-10',reference:'SYNTHETIC-KEETA-HTTP',accountingRole:'principal'}))form.set(key,value);form.set('file',new Blob([bytes]),'synthetic.xlsx');if(preview){form.set('rows',JSON.stringify(preview.rows));form.set('fingerprint',preview.fingerprint);form.set('confirm','true');}return form;}
 const preview=await call('admin','/api/platforms/keeta/invoices',upload());assert.equal(preview.rows[0].amountExclVat,100);
 await call('checker','/api/platforms/keeta/invoices?commit=1',upload(preview),409);
 const invoice=await call('admin','/api/platforms/keeta/invoices?commit=1',upload(preview));
 const lines=await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/lines`);assert.equal(lines[0].invoiced,100);
 await call('admin',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/approve`,{},400);
 await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/approve`,{});
 await call('checker',`/api/platforms/keeta/invoices/${encodeURIComponent(invoice.id)}/post`,{},409); // gate remains OFF
 await call('admin','/api/platforms/keeta/invoices',upload(),400); // duplicate bytes refused
 console.log(`PASS R6c invoice HTTP contracts: ${checks} checks`);
}finally{
 if(child&&child.exitCode===null){const exit=once(child,'exit');child.kill();await exit;}
 rmSync(data,{recursive:true,force:true});
}
