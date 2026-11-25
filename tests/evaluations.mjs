import {spawn} from 'node:child_process';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-evaluation-http-'));
const base='http://127.0.0.1:19373';
const child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{
  cwd:path.join(root,'Payroll.Api'),stdio:['ignore','pipe','pipe'],
  env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data,
    ConnectionStrings__DefaultConnection:'',Vouchers__Secret:'synthetic-test-only-voucher-key-never-use-on-a-server'}
});
let logs='',cookie='',checks=0,spawnError;
child.on('error',e=>spawnError=e);
child.stdout.on('data',b=>logs=(logs+b).slice(-6000));child.stderr.on('data',b=>logs=(logs+b).slice(-6000));
async function request(route,body,status=200,auth=true){
  const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(body===undefined?{}:{'Content-Type':'application/json'}),...(auth&&cookie?{Cookie:cookie}:{})},body:body===undefined?undefined:JSON.stringify(body)});
  const raw=await r.text(); assert.equal(r.status,status,`${route}: ${r.status} ${raw}`);checks++;
  if(r.headers.get('set-cookie'))cookie=r.headers.get('set-cookie').split(';')[0];
  try{return JSON.parse(raw);}catch{return raw;}
}
const password=randomBytes(24).toString('hex'),prefix='/api/forms/evaluations';
try{
  let ready=false;
  for(let n=0;n<150;n++){
    if(spawnError)throw spawnError;if(child.exitCode!==null)throw Error(logs);
    try{if((await fetch(base+'/health')).ok){ready=true;break;}}catch{}
    await new Promise(r=>setTimeout(r,100));
  }
  assert.ok(ready,'Synthetic SQLite API did not start');
  await request('/api/auth/setup',{username:'synthetic-admin',password});const admin=cookie;
  await request(prefix+'/staff',undefined,401,false);
  const staff={name:'Synthetic person',cities:['Synthetic city'],departments:['الحركة والمركبات'],roles:['Synthetic role'],apps:['Ninja']};
  assert.deepEqual(await request(prefix+'/staff/import',{staff:[staff,staff,{...staff,name:'Excluded synthetic person',excluded:true}]}),{imported:2,excluded:1,kept:[]});
  const staffList=await request(prefix+'/staff');
  const staffId=staffList.find(s=>s.name==='Synthetic person').id;
  const updatedStaff=await fetch(base+prefix+'/staff/'+encodeURIComponent(staffId),{method:'PUT',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({name:'Synthetic person',cities:['Synthetic city'],departments:['الحركة والمركبات'],roles:['Synthetic role'],apps:['Ninja'],allApps:true,allCities:false,excluded:false,active:true})});
  assert.equal(updatedStaff.status,200);assert.equal((await updatedStaff.json()).allApps,true);checks++;
  // Import replaces stored lists (never merges), and a card is deleted only after its exact name is typed.
  await request(prefix+'/staff/import',{staff:[{...staff,name:'Synthetic leaver',cities:['Synthetic city','Synthetic other city'],apps:['Ninja','Keeta']}]});
  await request(prefix+'/staff/import',{staff:[{...staff,name:'Synthetic leaver'}]});
  const leaver=(await request(prefix+'/staff')).find(s=>s.name==='Synthetic leaver');
  const keptResult=await request(prefix+'/staff/import',{staff:[{...staff,name:'Synthetic leaver',apps:[]}]});
  assert.deepEqual(keptResult.kept,[{id:leaver.id,name:'Synthetic leaver',fields:['apps']}]);
  assert.deepEqual((await request(prefix+'/staff')).find(s=>s.id===leaver.id).apps,['Ninja']);
  assert.deepEqual([leaver.cities,leaver.apps,leaver.allowedActions],[['Synthetic city'],['Ninja'],['edit','delete']]);
  const deleteStaff=name=>fetch(base+prefix+'/staff/'+encodeURIComponent(leaver.id),{method:'DELETE',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({name})});
  assert.equal((await deleteStaff('Wrong name')).status,400);checks++;
  const deleted=await deleteStaff('Synthetic leaver');assert.equal(deleted.status,200);assert.deepEqual(await deleted.json(),{id:leaver.id,deleted:true});checks++;
  assert.ok(!(await request(prefix+'/staff')).some(s=>s.id===leaver.id));
  function makeLegacyRound(roundId){
    const db=new DatabaseSync(path.join(data,'payroll.db'));
    const row=db.prepare('SELECT body FROM records WHERE id=?').get(roundId);
    const body=JSON.parse(row.body);
    delete body.sections;
    db.prepare('UPDATE records SET body=? WHERE id=?').run(JSON.stringify(body),roundId);
    db.close();
    return body.sharingToken;
  }
  // Legacy combined two-page path (rounds without sections still exist on live).
  const legacyDraft=await request(prefix+'/rounds',{title:'Synthetic evaluation'});
  assert.equal(legacyDraft.staff.length,1);assert.deepEqual(legacyDraft.allowedActions,['open','refresh-staff','delete']);
  assert.ok(Array.isArray(legacyDraft.sections)&&legacyDraft.sections.length>=5);
  assert.ok(legacyDraft.sections.every(s=>/^[0-9a-f]{64}$/.test(s.sharingToken)));
  assert.equal(legacyDraft.sharingToken,undefined,'sectioned RoundView omits round sharingToken');
  await request('/api/public/evaluations/'+legacyDraft.sections.find(s=>s.key==='app:Ninja').sharingToken,undefined,409,false);
  // Stored round-level token only works after sections are stripped (legacy path).
  const storedRoundToken=makeLegacyRound(legacyDraft.id);
  const round=await request(prefix+'/rounds/'+legacyDraft.id+'/open',{});
  assert.ok(round.sharingToken);assert.equal(round.sharingToken,storedRoundToken);
  assert.ok(!round.sections);
  const pub='/api/public/evaluations/'+round.sharingToken;
  const schema=await request(pub+'?city=Synthetic%20city',undefined,200,false);
  assert.equal(schema.staff.length,1);assert.equal(schema.staffDimensions.length,5);assert.equal(schema.staffRatingMax,10);assert.equal(schema.appRatingMax,5);
  assert.ok(!schema.sectionKey);assert.ok(!JSON.stringify(schema).includes('sharingToken'));assert.ok(!JSON.stringify(schema).includes('reportingLine'));
  const answer={iqama:'2000000091',name:'Synthetic rider',city:'Synthetic city',nationality:'Synthetic nationality',workApps:['Ninja'],staffAnswers:[{staffId:round.staff[0].id,ratings:Object.fromEntries(schema.staffDimensions.map(d=>[d.id,8])),complaint:true,complaintDetails:'Synthetic complaint details'}],appAnswers:round.apps.map(app=>app==='Ninja'?{app,overall:4,factorRatings:{orderVolume:5,appProblems:2}}:{app,reason:'not_worked'})};
  await request(pub,{...answer,nationality:''},400,false);
  await request(pub,{...answer,staffAnswers:[]},400,false);
  await request(pub,{...answer,appAnswers:[...answer.appAnswers,{app:'Unknown',overall:5}]},400,false);
  const reply=await request(pub,answer,200,false);assert.ok(reply.receipt);assert.match(reply.receipt,/^[0-9a-f]{64}$/);assert.deepEqual(reply.allowedActions,['edit']);assert.equal(reply.editCount,0);assert.equal(reply.editsRemaining,3);
  const dup=await fetch(base+pub,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(answer)});assert.equal(dup.status,409);const dupBody=await dup.json();assert.equal(dupBody.code,'already_submitted');checks++;
  let stats=await request(prefix+'/rounds/'+round.id+'/statistics');
  assert.equal(stats.totalResponses,1);assert.equal(stats.bySupervisor[0].average,8);assert.equal(stats.bySupervisor[0].complaintCount,1);
  assert.equal(stats.byApp.find(a=>a.key==='Ninja').average,4);assert.equal(stats.factorAverages.length,2);assert.equal(stats.factorAverages.find(f=>f.key==='orderVolume').average,5);
  assert.ok(!JSON.stringify(stats).includes(answer.iqama));assert.ok(!JSON.stringify(stats).includes(answer.name));
  const stolen={receipt:round.id.replace(/^evround:/,'')+answer.iqama,iqama:answer.iqama,staffAnswers:answer.staffAnswers,appAnswers:answer.appAnswers};
  const stolenEdit=await fetch(base+pub,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({...stolen,receipt:round.id})});assert.equal(stolenEdit.status,403);checks++;
  const editPayload={receipt:reply.receipt,iqama:answer.iqama,staffAnswers:answer.staffAnswers.map(a=>({...a,ratings:Object.fromEntries(schema.staffDimensions.map(d=>[d.id,5])),notes:'Synthetic edit'})),appAnswers:answer.appAnswers};
  const edited=await fetch(base+pub,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(editPayload)});assert.equal(edited.status,200);const editedBody=await edited.json();assert.equal(editedBody.editCount,1);checks++;
  stats=await request(prefix+'/rounds/'+round.id+'/statistics');assert.equal(stats.bySupervisor[0].average,5);
  for(const dimension of ['city','app','supervisor','nationality','factors','trend'])assert.ok(Array.isArray((await request(prefix+'/rounds/'+round.id+'/statistics/'+dimension)).items));
  const responses=await request(prefix+'/rounds/'+round.id+'/responses?page=1&pageSize=10');assert.equal(responses.total,1);assert.equal(responses.items[0].nationality,answer.nationality);
  const complaints=await request('/api/rider-mobile/service-requests');
  assert.ok((complaints.items??complaints).some(r=>r.source==='evaluation'&&r.confidential&&r.payload.targetKind==='supervisor'));
  const exported=await fetch(base+prefix+'/rounds/'+round.id+'/export',{headers:{Cookie:cookie}});assert.equal(exported.status,200);assert.equal(Buffer.from(await exported.arrayBuffer()).subarray(0,2).toString(),'PK');checks++;
  for(const [name,role,permissions] of [
    ['ordinary','Reviewer',['forms.view','mobile.complaints.confidential']],
    ['supervisor','PlatformsSupervisor',['forms.evaluations.view','forms.evaluations.export','forms.responses.allCompanies','mobile.complaints.confidential']],
    ['unscoped','Reviewer',['forms.evaluations.view']],
    ['management','Reviewer',['forms.evaluations.view','forms.responses.allCompanies']]
  ])await request('/api/users',{username:'synthetic-'+name,password,role,customPermissions:permissions});
  for(const name of ['ordinary','supervisor']){
    await request('/api/auth/login',{username:'synthetic-'+name,password});
    await request(prefix+'/rounds/'+round.id+'/statistics',undefined,403);
    await request(prefix+'/rounds/'+round.id+'/responses',undefined,403);
    await request(prefix+'/rounds/'+round.id+'/export',undefined,403);
    const list=await request('/api/rider-mobile/service-requests');assert.ok(!(list.items??list).some(r=>r.source==='evaluation'));
  }
  await request('/api/auth/login',{username:'synthetic-unscoped',password});
  assert.equal((await request(prefix+'/rounds/'+round.id+'/statistics')).totalResponses,0);
  await request('/api/auth/login',{username:'synthetic-management',password});
  assert.equal((await request(prefix+'/rounds/'+round.id+'/statistics')).totalResponses,1);
  await request(prefix+'/rounds/'+round.id+'/export',undefined,403);
  await request(prefix+'/rounds/'+round.id+'/pause',{},403);
  cookie=admin;await request(prefix+'/rounds/'+round.id+'/pause',{});await request(pub,answer,409,false);
  await request(prefix+'/rounds/'+round.id+'/close',{});await request(prefix+'/rounds/'+round.id+'/open',{},400);

  // Sectioned public form + finance scorecard + delete with title confirm
  const sectionRound=await request(prefix+'/rounds',{title:'Synthetic sectioned evaluation'});
  assert.equal(sectionRound.sharingToken,undefined);
  const sectionNinja=sectionRound.sections.find(s=>s.key==='app:Ninja');assert.ok(sectionNinja);
  await request(prefix+'/rounds/'+sectionRound.id+'/open',{});
  // Round-level token of a sectioned round must 404; only section tokens open the form.
  {
    const db=new DatabaseSync(path.join(data,'payroll.db'));
    const sectionedStored=JSON.parse(db.prepare('SELECT body FROM records WHERE id=?').get(sectionRound.id).body);
    db.close();
    await request('/api/public/evaluations/'+sectionedStored.sharingToken,undefined,404,false);
  }
  const sectionPub='/api/public/evaluations/'+sectionNinja.sharingToken;
  const sectionSchema=await request(sectionPub+'?city=Synthetic%20city',undefined,200,false);
  assert.equal(sectionSchema.schema,'evaluation-section-v1');assert.equal(sectionSchema.sectionKey,'app:Ninja');assert.equal(sectionSchema.includeFinanceCard,true);
  assert.equal(sectionSchema.apps.length,1);assert.equal(sectionSchema.apps[0],'Ninja');
  const sectionAnswer={iqama:'2000000093',name:'Synthetic section rider',city:'Synthetic city',nationality:'Synthetic nationality',workApps:['Ninja'],staffAnswers:sectionSchema.staff.map(s=>({staffId:s.id,ratings:Object.fromEntries(sectionSchema.staffDimensions.map(d=>[d.id,7]))})),appAnswers:[{app:'Ninja',overall:5,factorRatings:{orderVolume:4}}],financeAnswer:{reason:'',overall:4,factorRatings:{responsiveness:3,accuracy:4,correctionSpeed:5,clarity:2},notes:'Synthetic finance note',complaint:false,complaintDetails:'',attachments:[]},systemsAnswer:{reason:'',overall:5,factorRatings:{formsEase:4,dataClarity:5,helpfulness:3,responsiveness:4},notes:'Synthetic systems note',complaint:false,complaintDetails:'',attachments:[]}};
  const sectionReply=await request(sectionPub,sectionAnswer,200,false);assert.ok(sectionReply.receipt);
  assert.equal((await request(prefix+'/rounds/'+sectionRound.id+'/statistics?section='+encodeURIComponent('app:Ninja'))).totalResponses,1);
  const financeCard=await request(prefix+'/rounds/'+sectionRound.id+'/scorecards/finance');
  assert.ok((financeCard.items||[]).some(i=>i.app==='Ninja'&&Number(i.ratedCount)>=1));
  const delWrong=await fetch(base+prefix+'/rounds/'+sectionRound.id,{method:'DELETE',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({title:'wrong title'})});
  assert.equal(delWrong.status,400);checks++;
  const delOk=await fetch(base+prefix+'/rounds/'+sectionRound.id,{method:'DELETE',headers:{'Content-Type':'application/json',Cookie:cookie},body:JSON.stringify({title:'Synthetic sectioned evaluation'})});
  assert.equal(delOk.status,200);const delBody=await delOk.json();assert.equal(delBody.repliesDeleted,1);checks++;
  await request('/api/rider-mobile/evaluations',undefined,401,false);
  console.log(`PASS ${checks} evaluation HTTP checks on synthetic SQLite`);
}catch(e){console.error(logs);throw e;}
finally{
  if(child.exitCode===null&&!spawnError){const stopped=new Promise(r=>child.once('exit',r));child.kill();await Promise.race([stopped,new Promise(r=>setTimeout(r,5000))]);}
  rmSync(data,{recursive:true,force:true});
}
