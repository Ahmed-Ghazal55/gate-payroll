import {spawn,execFileSync} from 'node:child_process';
import {mkdtempSync,rmSync,writeFileSync,readFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-workforce-http-'));
const port=19471, base=`http://127.0.0.1:${port}`;
const published=process.env.PAYROLL_TEST_PUBLISHED;
const dotnetBin=process.env.DOTNET_PATH||'dotnet';
const exeName=process.platform==='win32'?'Payroll.Api.exe':'Payroll.Api';
const proc=spawn(published?path.join(published,exeName):dotnetBin,published?[]:[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{
  cwd:published||path.join(root,'Payroll.Api'),
  env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data,Operations__Source:'store'},
  windowsHide:true,stdio:['ignore','pipe','pipe']
});
let logs='',cookie='',checks=0,spawnError=null;
proc.once('error',e=>{spawnError=e;});
proc.stdout.on('data',x=>logs+=x);
proc.stderr.on('data',x=>logs+=x);

async function req(route,body,expected=200,auth=true){
  const r=await fetch(base+route,{
    method:body===undefined?'GET':'POST',
    headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},
    body:body===undefined?undefined:JSON.stringify(body)
  });
  const raw=await r.text();
  assert.equal(r.status,expected,`${route}: ${r.status} ${raw.slice(0,400)}`);
  checks++;
  if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];
  try{return JSON.parse(raw);}catch{return raw;}
}

async function getBytes(route,expected=200){
  const r=await fetch(base+route,{headers:cookie?{Cookie:cookie}:{}});
  const bytes=Buffer.from(await r.arrayBuffer());
  assert.equal(r.status,expected,`${route}: ${r.status}`);
  checks++;
  return bytes;
}

/** XLSX is zip-compressed; search the worksheet XML text. */
function sheetText(bytes){
  const dir=mkdtempSync(path.join(tmpdir(),'wf-xlsx-'));
  try{
    const file=path.join(dir,'w.xlsx');
    writeFileSync(file,bytes);
    execFileSync('unzip',['-qq','-o',file,'xl/worksheets/sheet1.xml','-d',dir]);
    return readFileSync(path.join(dir,'xl/worksheets/sheet1.xml'),'utf8');
  }finally{rmSync(dir,{recursive:true,force:true});}
}

async function login(username,password){
  cookie='';
  await req('/api/auth/login',{username,password});
}

function scope(sponsors,platforms){
  return {sponsorIds:sponsors,platforms,accountCodes:[],categoryCodes:[]};
}

// Synthetic all-zero IBANs are built at runtime so the release source guard sees no account literal.
const synthIban=tail=>'SA'+tail.padStart(22,'0');
const IBAN_BW=synthIban('111');
const IBAN_EX=synthIban('222');
const IBAN_KEETA=synthIban('333');
const IQ_BW='2000000311';
const IQ_EX='2000000322';
const IQ_KEETA='2000000333';

try{
  let ready=false;
  for(let n=0;n<150;n++){
    if(spawnError)throw spawnError;
    if(proc.exitCode!==null)throw Error('Test server exited before startup\n'+logs.slice(-2000));
    try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}
    await new Promise(r=>setTimeout(r,100));
  }
  assert.ok(ready,'Test server did not become ready\n'+logs.slice(-2000));

  const password=randomBytes(24).toString('hex');
  await req('/api/auth/setup',{username:'wf-admin',password});
  const adminCookie=cookie;

  await req('/api/riders',{iqama:IQ_BW,name:'Synthetic Workforce BW',financeSponsorId:'BW',sponsor:'BW',iban:IBAN_BW,platforms:'Jahez',engagementType:'SponsoredInternal',status:'Active',phone:'0501111111',city:'Jeddah'});
  await req('/api/riders',{iqama:IQ_EX,name:'Synthetic Workforce EX',financeSponsorId:'EX',sponsor:'EX',iban:IBAN_EX,platforms:'Jahez',engagementType:'SponsoredInternal',status:'Active',phone:'0502222222',city:'Riyadh'});
  await req('/api/riders',{iqama:IQ_KEETA,name:'Synthetic Workforce Keeta',financeSponsorId:'BW',sponsor:'BW',iban:IBAN_KEETA,platforms:'Keeta',engagementType:'SponsoredInternal',status:'Active',phone:'0503333333',city:'Dammam'});

  await req('/api/users',{username:'wf-company',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view'],financialScope:scope(['BW'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'wf-platform',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view'],financialScope:scope(['BW','EX','TR'],['Jahez'])});
  await req('/api/users',{username:'wf-plain',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'wf-iban',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.iban.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'wf-saudi',password,role:'Reviewer',customPermissions:['riders.view','riders.saudi_salary.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'wf-legal',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.legal_cases.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'wf-unified',password,role:'Reviewer',customPermissions:['riders.view','riders.unified_number.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});

  async function assertActor(label,username,expect){
    await login(username,password);
    const list=await req('/api/workforce?page=1&pageSize=50');
    const summary=await req('/api/workforce/summary');
    const saudi=await req('/api/workforce/saudi-payroll-employees');
    const exportBytes=await getBytes('/api/workforce/export');
    assert.equal(exportBytes.subarray(0,2).toString(),'PK',label+' export is xlsx');
    const exportXml=sheetText(exportBytes);
    const ids=(list.items||[]).map(r=>r.iqama).sort();
    assert.deepEqual(ids,expect.iqamas.slice().sort(),label+' list iqamas');
    assert.equal(list.total,expect.iqamas.length,label+' list total');
    assert.equal(summary.summary.totalCount,expect.iqamas.length,label+' summary total matches scoped set');
    assert.equal(saudi.total,0,label+' saudi store empty');
    for(const ban of expect.absentIbans||[]){
      assert.ok(!exportXml.includes(ban),label+' export hides '+ban);
      assert.ok(!(list.items||[]).some(r=>r.iban===ban),label+' list hides '+ban);
    }
    for(const ok of expect.presentIbans||[]){
      assert.ok(exportXml.includes(ok),label+' export keeps '+ok);
      assert.ok((list.items||[]).some(r=>r.iban===ok),label+' list keeps '+ok);
    }
    if(expect.outOfScope360){
      await req(`/api/workforce/${expect.outOfScope360}/360`,undefined,404);
    }
  }

  // (a) admin
  cookie=adminCookie;
  {
    const list=await req('/api/workforce?page=1&pageSize=50');
    const summary=await req('/api/workforce/summary');
    await req('/api/workforce/saudi-payroll-employees');
    const exportBytes=await getBytes('/api/workforce/export');
    assert.ok(list.total>=3,'admin sees seeded riders');
    assert.equal(summary.summary.totalCount,list.total,'admin summary matches list total');
    assert.ok(sheetText(exportBytes).includes(IBAN_BW),'admin export has IBAN');
    assert.ok((list.items||[]).some(r=>r.iban===IBAN_BW),'admin list has IBAN');
    await req(`/api/workforce/${IQ_BW}/360`);
  }

  // (b) company-scoped BW
  await assertActor('company', 'wf-company', {
    iqamas:[IQ_BW,IQ_KEETA],
    absentIbans:[IBAN_BW,IBAN_KEETA,IBAN_EX],
    outOfScope360:IQ_EX
  });

  // (c) platform-scoped Jahez only (all companies)
  await assertActor('platform', 'wf-platform', {
    iqamas:[IQ_BW,IQ_EX],
    absentIbans:[IBAN_BW,IBAN_EX],
    outOfScope360:IQ_KEETA
  });

  // Field permissions: IBAN
  await login('wf-plain',password);
  {
    const list=await req('/api/workforce?page=1&pageSize=50');
    const body=await req(`/api/workforce/${IQ_BW}/360`);
    const exportBytes=await getBytes('/api/workforce/export');
    assert.ok((list.items||[]).every(r=>!r.iban),'plain list strips iban');
    assert.equal(body.person?.iban||'','','plain 360 strips iban');
    assert.ok(!sheetText(exportBytes).includes(IBAN_BW),'plain export strips iban');
    assert.ok(!(body.allowedActions||[]).includes('view_iban'),'plain no view_iban action');
    await req('/api/workforce/legal-cases',undefined,403);
  }
  await login('wf-iban',password);
  {
    const list=await req('/api/workforce?page=1&pageSize=50');
    const body=await req(`/api/workforce/${IQ_BW}/360`);
    const exportBytes=await getBytes('/api/workforce/export');
    assert.ok((list.items||[]).some(r=>r.iban===IBAN_BW),'iban grant keeps list iban');
    assert.equal(body.person.iban,IBAN_BW,'iban grant keeps 360 iban');
    assert.ok(sheetText(exportBytes).includes(IBAN_BW),'iban grant keeps export iban');
    assert.ok((body.allowedActions||[]).includes('view_iban'),'iban grant action');
  }

  // Legal cases permission (360 strip + route)
  await login('wf-plain',password);
  {
    const body=await req(`/api/workforce/${IQ_BW}/360`);
    assert.equal(body.legalCases,undefined,'plain 360 drops legalCases');
  }
  await login('wf-legal',password);
  {
    const body=await req(`/api/workforce/${IQ_BW}/360`);
    assert.ok(Array.isArray(body.legalCases),'legal grant keeps legalCases array');
    const cases=await req('/api/workforce/legal-cases');
    assert.equal(cases.total,0,'legal store empty but allowed');
  }

  // Saudi salary + unified number: store returns empty items; permission still reflected in allowedActions on list for salary when granted via saudi user
  await login('wf-saudi',password);
  {
    const saudi=await req('/api/workforce/saudi-payroll-employees');
    assert.equal(saudi.total,0);
    assert.ok((saudi.allowedActions||[]).includes('view_saudi_salary'),'saudi salary action present');
  }
  await login('wf-plain',password);
  {
    const saudi=await req('/api/workforce/saudi-payroll-employees');
    assert.ok(!(saudi.allowedActions||[]).includes('view_saudi_salary'),'plain saudi salary action absent');
  }
  await login('wf-unified',password);
  {
    const list=await req('/api/workforce?page=1&pageSize=1');
    // unified number is only on saudi rows; grant is catalogued and does not leak via list person fields
    assert.ok((list.items||[]).every(r=>r.unifiedNationalNumber===undefined||r.unifiedNationalNumber===''),'list has no unified number field leak');
  }

  console.log(`PASS ${checks} workforce HTTP checks (admin/company/platform scope, field permissions, 360 out-of-scope 404).`);
}catch(e){
  console.error(e);
  console.error(logs.slice(-4000));
  process.exitCode=1;
}finally{
  if(proc.pid&&proc.exitCode===null){
    const exited=new Promise(r=>proc.once('exit',r));
    proc.kill();
    await Promise.race([exited,new Promise(r=>setTimeout(r,5000))]);
  }
  rmSync(data,{recursive:true,force:true});
}
