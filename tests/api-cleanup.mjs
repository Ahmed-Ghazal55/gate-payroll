// All fixtures live in a new temporary SQLite store. No production settings or data.
import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync,readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-api-cleanup-'));
const base='http://127.0.0.1:19473';
const proc=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:['ignore','pipe','pipe']});
let cookie='',admin='',checks=0,logs='';proc.stdout.on('data',x=>logs+=x);proc.stderr.on('data',x=>logs+=x);
async function req(route,body,method=body===undefined?'GET':'POST',status=200){const r=await fetch(base+route,{method,headers:{...(cookie?{Cookie:cookie}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});const text=await r.text();assert.equal(r.status,status,`${method} ${route}: ${text}`);checks++;if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];return text?JSON.parse(text):null;}
try{
 let ready=false;for(let n=0;n<150;n++){if(proc.exitCode!==null)throw Error(logs);try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}await new Promise(r=>setTimeout(r,100));}assert.ok(ready,'API ready');
 await req('/openapi/v1.json',undefined,'GET',401);
 const password=randomBytes(24).toString('hex');
 await req('/api/auth/setup',{username:'synthetic-admin',password});admin=cookie;
 const doc=await req('/openapi/v1.json');
 const index=readFileSync(path.join(root,'docs/api/ENDPOINTS.md'),'utf8');
 for(const line of index.split('\n')){const m=line.match(/^\| (GET|POST|PUT|DELETE|PATCH) \| `(\/api\/[^`]+)`/);if(m){assert.ok(doc.paths[m[2]]?.[m[1].toLowerCase()],`OpenAPI missing ${m[1]} ${m[2]}`);assert.ok(doc.paths[m[2]][m[1].toLowerCase()].tags?.length);assert.ok(doc.paths[m[2]][m[1].toLowerCase()].summary);checks++;}}
 for(const [p,methods] of Object.entries(doc.paths)){if(p.startsWith('/api/'))for(const m of Object.keys(methods)){if(['get','post','put','delete','patch'].includes(m))assert.ok(index.includes(`| ${m.toUpperCase()} | \`${p}\``),`Inventory missing ${m} ${p}`);}}
 await req('/api/users',{username:'synthetic-reader',password,role:'Reviewer'});
 await req('/api/auth/login',{username:'synthetic-reader',password});
 await req('/openapi/v1.json',undefined,'GET',403);cookie=admin;
 console.log(`PASS API cleanup: ${checks} checks`);
}finally{proc.kill();await new Promise(resolve=>{if(proc.exitCode!==null||proc.signalCode!==null)resolve();else proc.once('exit',resolve);});rmSync(data,{recursive:true,force:true});}
