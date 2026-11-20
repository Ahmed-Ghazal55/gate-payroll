/**
 * FU1 (c): HTTP coverage for GET /api/riders and profile-360 —
 * field masking (IBAN, muda salary) with/without each permission,
 * company/platform scope (404 out of scope), JSON + workforce export.
 */
import {spawn,execFileSync} from 'node:child_process';
import {mkdtempSync,rmSync,writeFileSync,readFileSync,existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import assert from 'node:assert/strict';
import {randomBytes} from 'node:crypto';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-riders-mask-'));
const port=19482, base=`http://127.0.0.1:${port}`;
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
  assert.equal(r.status,expected,`${route}: ${r.status} ${raw.slice(0,500)}`);
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

function sheetText(bytes){
  const dir=mkdtempSync(path.join(tmpdir(),'rm-xlsx-'));
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

const synthIban=tail=>'SA'+tail.padStart(22,'0');
const IBAN_BW=synthIban('501');
const IBAN_EX=synthIban('502');
const IBAN_KEETA=synthIban('503');
const IQ_BW='2000000501';
const IQ_EX='2000000502';
const IQ_KEETA='2000000503';
const MUDAD='4500.00';

/** Inject muda salary into the rider document (Normalize does not accept that field). */
function injectMudad(iqama,salary){
  const db=path.join(data,'payroll.db');
  assert.ok(existsSync(db),'payroll.db after seed');
  const script=path.join(data,'inject-mudad.py');
  writeFileSync(script,`import sqlite3,json\nc=sqlite3.connect(${JSON.stringify(db)})\nc.execute("UPDATE records SET body=json_set(body,'$.mudadSalary',?,'$.verifiedIban',json_extract(body,'$.iban')) WHERE kind='rider' AND id=?",(${JSON.stringify(salary)},${JSON.stringify(iqama)}))\nc.commit();c.close()\n`);
  execFileSync('python3',[script]);
}

function asList(payload){
  return Array.isArray(payload)?payload:(payload.items||[]);
}

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
  await req('/api/auth/setup',{username:'rm-admin',password});

  await req('/api/riders',{iqama:IQ_BW,name:'Synthetic Mask BW',financeSponsorId:'BW',sponsor:'BW',iban:IBAN_BW,platforms:'Jahez',engagementType:'SponsoredInternal',status:'Active',phone:'0500000501',city:'Jeddah'});
  await req('/api/riders',{iqama:IQ_EX,name:'Synthetic Mask EX',financeSponsorId:'EX',sponsor:'EX',iban:IBAN_EX,platforms:'Jahez',engagementType:'SponsoredInternal',status:'Active',phone:'0500000502',city:'Riyadh'});
  await req('/api/riders',{iqama:IQ_KEETA,name:'Synthetic Mask Keeta',financeSponsorId:'BW',sponsor:'BW',iban:IBAN_KEETA,platforms:'Keeta',engagementType:'SponsoredInternal',status:'Active',phone:'0500000503',city:'Dammam'});
  for(const iq of [IQ_BW,IQ_EX,IQ_KEETA])injectMudad(iq,MUDAD);

  await req('/api/users',{username:'rm-plain',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-iban',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.iban.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-saudi',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.saudi_salary.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-legal',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.legal_cases.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-unified',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.unified_number.view'],financialScope:scope(['BW','EX','TR'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-company',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.iban.view','riders.saudi_salary.view'],financialScope:scope(['BW'],['Jahez','Keeta','HungerStation','Ninja','Chefz','Amazon','Other'])});
  await req('/api/users',{username:'rm-platform',password,role:'Reviewer',customPermissions:['riders.view','riders.view_360','debts.view','riders.iban.view'],financialScope:scope(['BW','EX','TR'],['Jahez'])});

  // Plain: IBAN + muda stripped on /api/riders and legacy profile-360; legalCases absent on workforce 360.
  await login('rm-plain',password);
  {
    const rows=asList(await req('/api/riders'));
    const bw=rows.find(r=>r.iqama===IQ_BW);
    assert.ok(bw,'plain sees BW rider');
    assert.equal(bw.iban||'','','plain /api/riders strips iban');
    assert.equal(bw.verifiedIban||'','','plain /api/riders strips verifiedIban');
    assert.equal(bw.mudadSalary||'','','plain /api/riders strips mudadSalary');
    const p360=await req(`/api/riders/${IQ_BW}/profile-360`);
    assert.equal(p360.person?.iban||p360.rider?.iban||'','','plain profile-360 strips iban');
    assert.equal(p360.person?.mudadSalary||p360.rider?.mudadSalary||'','','plain profile-360 strips mudadSalary');
    const wf=await req(`/api/workforce/${IQ_BW}/360`);
    assert.equal(wf.legalCases,undefined,'plain workforce 360 drops legalCases');
    assert.equal(wf.person?.iban||'','','plain workforce 360 strips iban');
    const exportXml=sheetText(await getBytes('/api/workforce/export'));
    assert.ok(!exportXml.includes(IBAN_BW),'plain export hides IBAN');
    assert.ok(!exportXml.includes(MUDAD),'plain export hides injected muda amount');
  }

  // IBAN grant: IBAN visible; muda still stripped.
  await login('rm-iban',password);
  {
    const rows=asList(await req('/api/riders'));
    const bw=rows.find(r=>r.iqama===IQ_BW);
    assert.equal(bw.iban,IBAN_BW,'iban grant keeps /api/riders iban');
    assert.equal(bw.mudadSalary||'','','iban grant still strips mudadSalary');
    const p360=await req(`/api/riders/${IQ_BW}/profile-360`);
    assert.equal(p360.person?.iban||p360.rider?.iban,IBAN_BW,'iban grant keeps profile-360 iban');
    assert.equal(p360.person?.mudadSalary||p360.rider?.mudadSalary||'','','iban grant still strips muda on profile-360');
    assert.ok(sheetText(await getBytes('/api/workforce/export')).includes(IBAN_BW),'iban grant keeps export IBAN');
  }

  // Saudi salary grant: muda visible; IBAN stripped.
  await login('rm-saudi',password);
  {
    const rows=asList(await req('/api/riders'));
    const bw=rows.find(r=>r.iqama===IQ_BW);
    assert.equal(bw.iban||'','','saudi grant strips iban');
    assert.equal(String(bw.mudadSalary),MUDAD,'saudi grant keeps mudadSalary on /api/riders');
    const p360=await req(`/api/riders/${IQ_BW}/profile-360`);
    assert.equal(String(p360.person?.mudadSalary||p360.rider?.mudadSalary),MUDAD,'saudi grant keeps muda on profile-360');
    const saudi=await req('/api/workforce/saudi-payroll-employees');
    assert.ok((saudi.allowedActions||[]).includes('view_saudi_salary'),'saudi salary action present');
    // Store-mode workforce person projection replaces muda with a placeholder; JSON /api/riders above is the amount check.
    assert.ok(!(saudi.allowedActions||[]).includes('view_iban'),'saudi-only user has no view_iban');
  }

  // Legal grant: legalCases array kept on workforce 360; /api/workforce/legal-cases allowed.
  await login('rm-legal',password);
  {
    const wf=await req(`/api/workforce/${IQ_BW}/360`);
    assert.ok(Array.isArray(wf.legalCases),'legal grant keeps legalCases');
    const cases=await req('/api/workforce/legal-cases');
    assert.equal(cases.total,0,'legal route allowed (store empty)');
  }

  // Unified number: no leak on directory JSON; saudi envelope has no unified without live ERP rows.
  await login('rm-unified',password);
  {
    const rows=asList(await req('/api/riders'));
    assert.ok(rows.every(r=>r.unifiedNationalNumber===undefined||r.unifiedNationalNumber===''),'directory has no unified number leak');
    const saudi=await req('/api/workforce/saudi-payroll-employees');
    assert.equal(saudi.total,0,'unified saudi store empty');
  }

  // Company scope: BW only; EX profile-360 → 404.
  await login('rm-company',password);
  {
    const rows=asList(await req('/api/riders'));
    const ids=rows.map(r=>r.iqama).sort();
    assert.deepEqual(ids,[IQ_BW,IQ_KEETA].sort(),'company scope /api/riders');
    assert.ok(!rows.some(r=>r.iqama===IQ_EX),'company scope hides EX');
    await req(`/api/riders/${IQ_EX}/profile-360`,undefined,404);
    await req(`/api/riders/${IQ_BW}/profile-360`);
  }

  // Platform scope: Jahez only; Keeta profile-360 → 404.
  await login('rm-platform',password);
  {
    const rows=asList(await req('/api/riders'));
    const ids=rows.map(r=>r.iqama).sort();
    assert.deepEqual(ids,[IQ_BW,IQ_EX].sort(),'platform scope /api/riders');
    await req(`/api/riders/${IQ_KEETA}/profile-360`,undefined,404);
  }

  console.log(`PASS ${checks} riders masking HTTP checks: /api/riders + profile-360 field masks, scope 404, export.`);
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
