// HTTP contracts: isolated SQLite, synthetic accounts and workbook; no external services.
import {spawn,execFileSync} from 'node:child_process';
import {once} from 'node:events';
import {createServer} from 'node:net';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import {randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),data=mkdtempSync(path.join(tmpdir(),'gate-r6b-http-'));
let child,base,checks=0;const cookies={};
async function call(user,route,body,status=200){
 const multipart=body instanceof FormData;const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(cookies[user]?{Cookie:cookies[user]}:{}),...(body===undefined||multipart?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:multipart?body:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,route+': '+raw.slice(0,180));checks++;if(r.headers.get('set-cookie'))cookies[user]=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
}
try{
 const probe=createServer();await new Promise((resolve,reject)=>{probe.once('error',reject);probe.listen(0,'127.0.0.1',resolve);});base='http://127.0.0.1:'+probe.address().port;await new Promise(resolve=>probe.close(resolve));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,ConnectionStrings__DefaultConnection:'',Jobs__Enabled:'false',PAYROLL_DATA_DIR:data},stdio:'ignore'});
 let ready=false;for(let i=0;i<150;i++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(resolve=>setTimeout(resolve,100));}assert.ok(ready);
 const password=randomBytes(24).toString('hex');await call('admin','/api/auth/setup',{username:'syn-r6b-admin',password});
 await call('admin','/api/users',{username:'syn-r6b-checker',password,role:'FinancialAccountant'});await call('checker','/api/auth/login',{username:'syn-r6b-checker',password});
 const companies=await call('admin','/api/companies');const company=companies[0].id;
 const db=new DatabaseSync(path.join(data,'payroll.db'));const put=(kind,row)=>db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run(row.id,kind,JSON.stringify(row));
 put('platform_account',{id:'synthetic-ninja-platform',companyId:company,platformCode:'ninja'});put('ninja_account',{id:'synthetic-ninja-account',platformAccountId:'synthetic-ninja-platform',CaptainId:'SYN-A'});db.close();
 const workbook=execFileSync('python3',['-c',`
import io,zipfile,sys,xml.etree.ElementTree as E
headers=['supplier_id','supplier_name','CAPTAIN ACTIVE STATUS','capabilty','captain_id','Zone Name','First Attended Date','Total Delivered Orders','Contract Amount','Contract Target','Count of First Attended Date to End of Month','Adjusted Target','Adjusted Amount','Below Target Percentage','Target Level Type','Target Deductions Amount','Target Bonus Amount','Target Total Amount','Count of Attended Days','Attendance Deduction','Count of Days Less than 8 Hr','8 Hour Deductions','Deduction refund','Gasoline Support','Weekend 40 Bonus','Weekend 42 Bonus','Weekend 43 Bonus','100 SAR BONUS','Supplier Performance Bonus','NAQIL BLOCKED','Penalty','on demand','STOPS WITHOUT NOTICE DEDUCTION','Final Amount','VAT','TOTAL COST']+['synthetic optional '+str(n) for n in range(7)]
values={'supplier_id':'SYN-SUPPLIER','capabilty':'BIKE','captain_id':'SYN-A','Total Delivered Orders':'460','Contract Amount':'6500','Contract Target':'460','Count of First Attended Date to End of Month':'31','Adjusted Target':'460','Adjusted Amount':'6500','Target Total Amount':'6500','Count of Attended Days':'31','Final Amount':'6500','VAT':'975','TOTAL COST':'7475'}
ns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'
root=E.Element('{'+ns+'}worksheet');data=E.SubElement(root,'{'+ns+'}sheetData')
def col(n):
 out=''
 while n: n,r=divmod(n-1,26);out=chr(65+r)+out
 return out
for number,valuesrow in enumerate([headers,[values.get(h,'0') for h in headers]],1):
 row=E.SubElement(data,'{'+ns+'}row',r=str(number))
 for n,v in enumerate(valuesrow,1):
  cell=E.SubElement(row,'{'+ns+'}c',r=col(n)+str(number),t='inlineStr');E.SubElement(E.SubElement(cell,'{'+ns+'}is'),'{'+ns+'}t').text=v
out=io.BytesIO()
with zipfile.ZipFile(out,'w') as z:
 z.writestr('xl/workbook.xml','<workbook xmlns="'+ns+'" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Synthetic" r:id="r1" /></sheets></workbook>')
 z.writestr('xl/_rels/workbook.xml.rels','<Relationships><Relationship Id="r1" Target="worksheets/sheet1.xml" /></Relationships>')
 z.writestr('xl/worksheets/sheet1.xml',E.tostring(root))
sys.stdout.buffer.write(out.getvalue())
`]);
 const form=(preview)=>{const f=new FormData();f.set('file',new Blob([workbook]),'synthetic.xlsx');f.set('companyId',company);f.set('period','2026-10');f.set('reference','SYNTHETIC-HTTP-INVOICE');if(preview){f.set('rows',JSON.stringify(preview.rows));f.set('fingerprint',preview.fingerprint);f.set('confirm','true');}return f;};
 await call('anon','/api/platforms/ninja/invoices',undefined,401);
 const preview=await call('admin','/api/platforms/ninja/invoices',form());assert.equal(preview.canCommit,true);assert.equal(preview.rows[0].lines[0].invoiced,6500);
 const invoice=await call('admin','/api/platforms/ninja/invoices?commit=1',form(preview));const id=encodeURIComponent(invoice.id);
 await call('admin',`/api/platforms/ninja/invoices/${id}/approve`,{},400);
 const final=await call('checker',`/api/platforms/ninja/invoices/${id}/approve`,{});assert.equal(final.status,'final');
 const lines=await call('checker',`/api/platforms/ninja/invoices/${id}/lines`);assert.equal(lines[0].invoiced,6500);assert.equal(lines[0].reason,'vehicle type edited by the platform');
 await call('checker',`/api/platforms/ninja/invoices/${id}/post`,{},409);await call('admin','/api/platforms/ninja/posting',{enabled:true});
 const posted=await call('checker',`/api/platforms/ninja/invoices/${id}/post`,{});assert.ok(posted.journalEntryId);
 await call('admin','/api/platforms/ninja/invoices',form(),400);await call('checker','/api/platforms/ninja/invoices');
 console.log(`PASS R6b accounting: ${checks} HTTP checks on synthetic SQLite.`);
}finally{if(child&&child.exitCode===null){child.kill('SIGTERM');await once(child,'exit');}rmSync(data,{recursive:true,force:true});}
