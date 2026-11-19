import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-ops-live-'));
const port=19481, base=`http://127.0.0.1:${port}`;
const dotnetBin=process.env.DOTNET_PATH||'dotnet';
const proc=spawn(dotnetBin,[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{
  cwd:path.join(root,'Payroll.Api'),
  env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data,Operations__Source:'store'},
  windowsHide:true,stdio:['ignore','pipe','pipe']
});
let logs='',cookie='',checks=0,spawnError=null;
proc.once('error',e=>{spawnError=e;});
proc.stdout.on('data',x=>logs+=x);
proc.stderr.on('data',x=>logs+=x);

async function req(route,body,expected=200,auth=true,method){
  const m=method||(body===undefined?'GET':'POST');
  const r=await fetch(base+route,{
    method:m,
    headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},
    body:body===undefined?undefined:JSON.stringify(body)
  });
  const raw=await r.text();
  assert.equal(r.status,expected,`${m} ${route}: ${r.status} ${raw.slice(0,400)}`);
  checks++;
  if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];
  try{return JSON.parse(raw);}catch{return raw;}
}

async function login(username,password){cookie='';await req('/api/auth/login',{username,password});}
function scope(sponsors,platforms){return {sponsorIds:sponsors,platforms,accountCodes:[],categoryCodes:[]};}

try{
  let ready=false;
  for(let n=0;n<150;n++){
    if(spawnError)throw spawnError;
    if(proc.exitCode!==null)throw Error('Test server exited\n'+logs.slice(-2000));
    try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}
    await new Promise(r=>setTimeout(r,100));
  }
  assert.ok(ready,'server not ready\n'+logs.slice(-2000));

  const password=randomBytes(24).toString('hex');
  await req('/api/auth/setup',{username:'ops-admin',password});
  await req('/api/users',{username:'ops-company',password,role:'Reviewer',customPermissions:['operations.view'],financialScope:scope(['BW'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'ops-none',password,role:'Reviewer',customPermissions:['riders.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});

  await login('ops-admin',password);
  for(const route of [
    '/api/operations/cities-summary',
    '/api/operations/housing',
    '/api/operations/vehicles/period-report?from=2026-11-01&to=2026-11-30&mode=vehicle',
    '/api/operations/fuel-cards?month=2026-11',
    '/api/operations/fuel-cards/assignments?from=2026-11-01&to=2026-11-30',
    '/api/operations/vehicles/purchase-invoices?from=2026-11-01&to=2026-11-30'
  ]){
    const body=await req(route);
    assert.equal(body.source,'store');
    assert.ok(Array.isArray(body.allowedActions));
    assert.ok(body.allowedActions.includes('export'));
    assert.ok(body.allowedActions.includes('refresh'));
    assert.ok('items' in body || body.id);
  }

  await req('/api/operations/cities/00000000-0000-0000-0000-000000000099',undefined,404);
  await req('/api/operations/housing/00000000-0000-0000-0000-000000000099/rooms',undefined,404);
  await req('/api/operations/vehicles/00000000-0000-0000-0000-000000000099',undefined,404);

  // No write verbs
  await req('/api/operations/cities-summary',undefined,405,true,'POST');
  await req('/api/operations/housing',undefined,405,true,'PUT');
  await req('/api/operations/fuel-cards',undefined,405,true,'DELETE');

  await login('ops-none',password);
  await req('/api/operations/cities-summary',undefined,403);
  await req('/api/operations/housing',undefined,403);

  await login('ops-company',password);
  const company=await req('/api/operations/cities-summary');
  assert.equal(company.source,'store');

  console.log(`Operations live HTTP: ${checks} passed`);
}finally{
  proc.kill('SIGTERM');
  try{rmSync(data,{recursive:true,force:true});}catch{}
}
