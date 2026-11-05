// Synthetic, temporary SQLite only. Exercises real HTTP authentication and stored session history.
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash,randomBytes} from 'node:crypto';
import {DatabaseSync} from 'node:sqlite';
import {createServer} from 'node:net';
import assert from 'node:assert/strict';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const data=mkdtempSync(path.join(tmpdir(),'gate-mobile-http-'));
const rider='2000000701',other='2000000702',fcm='synthetic-fcm-device-token',reason={reason:'Synthetic lost device'};
let child,base,checks=0;const jars={};
async function start(){
 const probe=createServer();await new Promise((resolve,reject)=>{probe.once('error',reject);probe.listen(0,'127.0.0.1',resolve);});
 base='http://127.0.0.1:'+probe.address().port;await new Promise(resolve=>probe.close(resolve));
 child=spawn('dotnet',[path.join(root,'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll')],{cwd:path.join(root,'Payroll.Api'),env:{...process.env,ConnectionStrings__DefaultConnection:'',ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:base,PAYROLL_DATA_DIR:data},stdio:'ignore'});
 for(let n=0;n<150;n++){if(child.exitCode!==null)throw Error('Synthetic API exited');try{if((await fetch(base+'/health')).ok)return;}catch{}await new Promise(r=>setTimeout(r,100));}throw Error('Synthetic API timeout');
}
function as(user,token){return async(route,body,status=200)=>{
 const r=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{...(jars[user]?{Cookie:jars[user]}:{}),...(token?{Authorization:'Bearer '+token}:{}),...(body===undefined?{}:{'Content-Type':'application/json'})},body:body===undefined?undefined:JSON.stringify(body)});
 const raw=await r.text();assert.equal(r.status,status,`${user} ${route}: ${raw.slice(0,180)}`);checks++;
 if(r.headers.get('set-cookie'))jars[user]=r.headers.get('set-cookie').split(';')[0];return raw?JSON.parse(raw):null;
};}
const sessionId=token=>'mobile:'+createHash('sha256').update(token).digest('hex').toUpperCase();
function dbAction(fn){const db=new DatabaseSync(path.join(data,'payroll.db'));try{return fn(db);}finally{db.close();}}
const stored=token=>dbAction(db=>JSON.parse(db.prepare('SELECT body FROM records WHERE id=?').get(sessionId(token)).body));
function change(token,fields){dbAction(db=>{const row=JSON.parse(db.prepare('SELECT body FROM records WHERE id=?').get(sessionId(token)).body);db.prepare('UPDATE records SET body=? WHERE id=?').run(JSON.stringify({...row,...fields}),row.id);});}
try{
 await start();const admin=as('admin'),anon=as('anon');
 await anon('/api/rider-mobile/sessions',undefined,401);await anon('/api/rider-mobile/sessions/mine',undefined,401);
 const password=randomBytes(20).toString('hex');await admin('/api/auth/setup',{username:'syn-mobile-admin',password});
 for(const [iqama,company] of [[rider,'BW'],[other,'EX']])await admin('/api/riders',{iqama,name:'Synthetic Mobile Rider',phone:'0501234567',financeSponsorId:company,status:'Active'});
 for(const [user,perms] of [['scoped',['mobile.sessions']],['broadcaster',['mobile.broadcast']],['none',[]]]){
  await admin('/api/users',{username:'syn-mobile-'+user,password,role:'Reviewer',customPermissions:perms,financialScope:{sponsorIds:['BW']}});
  await as(user)('/api/auth/login',{username:'syn-mobile-'+user,password});
 }
 const scoped=as('scoped'),none=as('none'),broadcaster=as('broadcaster');
 await none('/api/rider-mobile/sessions',undefined,403);await broadcaster('/api/rider-mobile/sessions',undefined,403);
 await none('/api/rider-mobile/sessions/revoke-all?iqama='+rider,reason,403);
 async function login(iqama,device=fcm){const grant=await admin('/api/inquiry-access',{iqama,scope:'mobile',identityReviewed:true});return (await anon('/api/rider-mobile/login',{iqamaNo:iqama,phoneLast4:'4567',code:grant.code,language:'en',fcmDeviceToken:device})).accessToken;}
 const token=await login(rider),second=await login(rider,''),otherToken=await login(other);
 const mobile=as('rider',token),mobile2=as('rider2',second),foreign=as('foreign',otherToken);
 let rows=await scoped('/api/rider-mobile/sessions');assert.equal(rows.length,2);assert.ok(rows.every(s=>s.iqama===rider));
 assert.equal((await admin('/api/rider-mobile/sessions')).length,3);
 assert.equal((await scoped('/api/rider-mobile/sessions?iqama='+other)).length,0);
 assert.equal((await scoped('/api/rider-mobile/sessions?page=1&pageSize=1')).total,2);
 await scoped('/api/rider-mobile/sessions?status=unknown',undefined,400);
 const own=await mobile('/api/rider-mobile/sessions/mine');assert.equal(own.length,2);
 const id=sessionId(token).slice(7,23),otherId=sessionId(otherToken).slice(7,23),view=own.find(s=>s.id===id);
 assert.equal(view.deviceFingerprint,createHash('sha256').update(fcm).digest('hex').slice(0,8));assert.equal(view.fcmRegistered,true);assert.equal(view.language,'en');assert.ok(view.lastSeenAt);
 assert.equal(own.find(s=>s.id!==id).deviceFingerprint,'');assert.equal(own.find(s=>s.id!==id).fcmRegistered,false);
 const safe=JSON.stringify([rows,own]);for(const secret of [token,second,otherToken,fcm,sessionId(token).slice(7),'accessToken','fcmDeviceToken'])assert.ok(!safe.includes(secret));
 await scoped('/api/rider-mobile/sessions/'+otherId+'/revoke',reason,404);
 await scoped('/api/rider-mobile/sessions/revoke-all?iqama='+other,reason,404);
 await scoped('/api/rider-mobile/sessions/'+id+'/revoke',{},400);
 await scoped('/api/rider-mobile/sessions/'+id+'/revoke',{reason:'  '},400);
 await scoped('/api/rider-mobile/sessions/'+sessionId(token).slice(7)+'/revoke',reason,404);
 const broadcast=await admin('/api/rider-mobile/broadcast',{iqama:rider,titleAr:'Synthetic notification',bodyAr:'Synthetic only'});
 await admin('/api/rider-mobile/broadcast',{iqama:other,titleAr:'Synthetic other',bodyAr:'Synthetic only'});
 const all=await admin('/api/rider-mobile/broadcast',{titleAr:'Synthetic all',bodyAr:'Synthetic only'});
 assert.equal((await mobile('/api/rider-mobile/broadcasts')).length,2);
 assert.equal((await broadcaster('/api/rider-mobile/broadcasts')).length,2);
 assert.equal((await foreign('/api/rider-mobile/broadcasts')).length,2);
 await mobile('/api/rider-mobile/broadcasts/'+broadcast.id+'/ack',{iqama:rider});
 await mobile('/api/rider-mobile/portal/'+rider);await mobile('/api/rider-mobile/statement?period=2026-10');
 const seen=stored(token).lastSeenAt;
 await Promise.all(Array.from({length:6},()=>mobile('/api/rider-mobile/sessions/mine')));assert.equal(stored(token).lastSeenAt,seen);
 change(token,{lastSeenAt:new Date(Date.now()-360000).toISOString()});await mobile('/api/rider-mobile/broadcasts');
 const newSeen=stored(token).lastSeenAt;assert.notEqual(newSeen,seen);await mobile('/api/rider-mobile/statement?period=2026-10');assert.equal(stored(token).lastSeenAt,newSeen);
 assert.equal((await scoped('/api/rider-mobile/sessions/'+id+'/revoke',reason)).status,'revoked');
 // A valid staff cookie cannot rescue a revoked rider bearer.
 const revoked=as('admin',token);
 async function deniedEverywhere(client){
  await client('/api/rider-mobile/sessions/mine',undefined,401);await client('/api/rider-mobile/portal/'+rider,undefined,401);
  await client('/api/rider-mobile/statement?period=2026-10',undefined,401);await client('/api/rider-mobile/broadcasts',undefined,401);
  await client('/api/rider-mobile/broadcasts/'+all.id+'/ack',{iqama:rider},401);await client('/api/rider-mobile/logout',{},401);
 }
 await deniedEverywhere(revoked);await mobile2('/api/rider-mobile/sessions/mine');
 assert.equal((await scoped('/api/rider-mobile/sessions?status=revoked')).length,1);
 await scoped('/api/rider-mobile/sessions/'+id+'/revoke',reason);
 change(second,{expiresAtUtc:new Date(Date.now()-1000).toISOString()});await deniedEverywhere(mobile2);
 assert.equal((await scoped('/api/rider-mobile/sessions?status=expired')).length,1);
 assert.equal((await scoped('/api/rider-mobile/sessions/revoke-all?iqama='+rider,reason)).revoked,1);
 assert.equal((await scoped('/api/rider-mobile/sessions/revoke-all?iqama='+rider,reason)).revoked,0);
 const third=await login(rider),logout=as('third',third);
 await logout('/api/rider-mobile/logout',{});await deniedEverywhere(logout);await foreign('/api/rider-mobile/sessions/mine');
 const session=stored(token);assert.equal(session.revokedBy,'syn-mobile-scoped');assert.equal(session.revokeReason,reason.reason);
 dbAction(db=>{assert.equal(db.prepare("SELECT count(*) n FROM records WHERE kind='mobile_session'").get().n,4);const audit=db.prepare("SELECT body FROM records WHERE kind='audit' AND json_extract(body,'$.action')='mobile.session_revoked'").all();assert.equal(audit.length,2);assert.ok(audit.every(a=>!a.body.includes(sessionId(token))));});
 console.log(`PASS mobile sessions: ${checks} HTTP checks; scope, secret projection, throttled lastSeen, revocation, expiry and logout.`);
}finally{if(child&&child.exitCode===null&&child.signalCode===null){const ended=once(child,'exit');child.kill();await ended;}rmSync(data,{recursive:true,force:true});}
