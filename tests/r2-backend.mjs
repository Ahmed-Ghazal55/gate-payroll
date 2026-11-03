// Release-train behavior checks: local process and disposable synthetic SQLite only.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {DatabaseSync} from 'node:sqlite';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-r2-'));
const webroot=path.join(data,'synthetic-webroot'),base='http://127.0.0.1:19481';
mkdirSync(path.join(webroot,'app/login'),{recursive:true});
writeFileSync(path.join(webroot,'index.html'),'<html>synthetic legacy entry</html>');
writeFileSync(path.join(webroot,'app/index.html'),'<html>synthetic app entry</html>');
writeFileSync(path.join(webroot,'app/login/index.html'),'<html>synthetic login</html>');
let child,cookie='',checks=0;
async function start(){
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll'),'--webroot',webroot],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('Synthetic API not ready');
}
async function stop(){if(child&&child.exitCode===null){const ended=once(child,'exit');child.kill();await ended;}}
async function request(route,{body,method=body===undefined?'GET':'POST',auth=true,status=200}={}){
 const r=await fetch(base+route,{method,redirect:'manual',headers:{...(auth&&cookie?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 assert.equal(r.status,status,method+' '+route);checks++;
 if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return r;
}
async function json(route,body,status=200){return (await request(route,{body,status})).json();}
try{
 await start();
 assert.equal((await request('/',{status:302})).headers.get('location'),'/app/');
 assert.equal((await request('/legacy/',{status:302,auth:false})).headers.get('location'),'/app/login/?returnUrl=/legacy/');
 assert.equal((await request('/index.html',{status:302})).headers.get('location'),'/legacy/');
 for(const value of ['https://outside.invalid/app/','//outside.invalid/app/','/inquiry','/app/../inquiry','/app/%252e%252e/inquiry','/app/\\outside.invalid'])await request('/app/login/?returnUrl='+encodeURIComponent(value),{status:400});
 for(const value of ['/legacy/','/app/riders/?page=2'])await request('/app/login/?returnUrl='+encodeURIComponent(value));
 await json('/api/auth/setup',{username:'synthetic-r2-admin',password:randomBytes(24).toString('hex')});
 assert.equal(await (await request('/legacy/')).text(),'<html>synthetic legacy entry</html>');
 assert.equal((await request('/legacy',{status:302})).headers.get('location'),'/legacy/');
 assert.equal((await request('/',{status:302})).headers.get('location'),'/app/');
 const campaign=await json('/api/campaigns',{title:'Synthetic R2 form',days:2});
 const form=await json('/api/forms/builder',{titleAr:'Synthetic questionnaire',expiresAt:new Date(Date.now()+86400000).toISOString(),maxSubmissions:10,fields:[{id:'identity',labelAr:'Identity',type:'iqama',required:true}]});
 const published=await json('/api/forms/builder/'+form.id+'/publish',{});
 for(const route of [campaign.path,new URL(published.publicUrl).pathname,'/inquiry','/rider-app','/health'])await request(route,{auth:false});
 const db=new DatabaseSync(path.join(data,'payroll.db'));
 for(const kind of ['custom_response','submission','user','audit','journal']){
  const raw=' { "id": "synthetic-preserve-'+kind+'", "name": "Synthetic", "iban": "SYNTHETIC_ONLY", "extra": [ 1, 2 ] } ';
  db.prepare('INSERT INTO records(id,kind,body) VALUES(?,?,?)').run('synthetic-preserve-'+kind,kind,raw);
 }
 const before=db.prepare('SELECT id,kind,body FROM records ORDER BY id').all();db.close();
 await stop();await start();
 const afterDb=new DatabaseSync(path.join(data,'payroll.db'));
 const after=afterDb.prepare('SELECT id,kind,body FROM records ORDER BY id').all();afterDb.close();
 assert.deepEqual(after,before,'Startup must preserve every existing document byte-for-byte');checks++;
 assert.equal((await (await request('/health')).json()).dataPreservationVerified,true);
 assert.equal(await (await request('/legacy/')).text(),'<html>synthetic legacy entry</html>');
 console.log('PASS R2 backend: '+checks+' HTTP checks; public routes, session gate, safe return paths and byte-identical restart');
}finally{await stop();rmSync(data,{recursive:true,force:true});}
