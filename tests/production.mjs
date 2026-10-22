import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes } from 'node:crypto';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dotnet = process.env.DOTNET_PATH || 'dotnet';
const dll = path.join(root, 'Payroll.Api/bin/Debug/net10.0/Payroll.Api.dll');
let checks = 0;

async function scenario(secret, expectedSetup, port) {
  const data = mkdtempSync(path.join(tmpdir(), 'gate-production-'));
  const base = `http://127.0.0.1:${port}`;
  const proc = spawn(dotnet, [dll], {cwd:path.join(root,'Payroll.Api'), env:{...process.env,
    ASPNETCORE_ENVIRONMENT:'Production', ASPNETCORE_URLS:base, PAYROLL_DATA_DIR:data,
    PAYROLL_BOOTSTRAP_KEY:secret, AllowedHosts:'127.0.0.1;localhost', PAYROLL_KNOWN_PROXIES:''}, stdio:['ignore','pipe','pipe']});
  let logs = ''; proc.stdout.on('data', x=>logs+=x); proc.stderr.on('data', x=>logs+=x);
  const started = new Promise((resolve,reject)=>{proc.once('spawn',resolve);proc.once('error',reject)});
  async function request(route, body, headers={}) {
    const res=await fetch(base+route,{method:body===undefined?'GET':'POST',headers:{'X-Forwarded-Proto':'https',...(body===undefined?{}:{'Content-Type':'application/json'}),...headers},body:body===undefined?undefined:JSON.stringify(body)});
    checks++; return res;
  }
  try {
    await started;
    let ready=false;
    for(let n=0;n<150;n++){try{if((await fetch(base+'/health')).ok){ready=true;break}}catch{}if(proc.exitCode!==null)throw Error('Server exited');await new Promise(r=>setTimeout(r,100));}
    assert.ok(ready,'Production server did not start');
    const missingTls=await fetch(base+'/api/auth/status');assert.equal(missingTls.status,403);checks++;
    const body={username:'release-admin',password:randomBytes(24).toString('hex'),bootstrapKey:secret};
    const hostile=await request('/api/auth/setup',body,{Origin:'https://attacker.invalid','X-Forwarded-Host':'attacker.invalid','Sec-Fetch-Site':'cross-site'});assert.equal(hostile.status,403);
    if(expectedSetup===200){const wrong=await request('/api/auth/setup',{...body,bootstrapKey:'incorrect'});assert.equal(wrong.status,400);}
    const setup=await request('/api/auth/setup',body);assert.equal(setup.status,expectedSetup);
    if(expectedSetup!==200)return;
    const cookie=setup.headers.get('set-cookie');assert.match(cookie,/secure/i);assert.match(cookie,/httponly/i);
    const session=cookie.split(';')[0];
    assert.equal((await request('/api/auth/setup',body)).status,400);
    const campaign=await request('/api/campaigns',{title:'Release smoke test',days:1},{Cookie:session});assert.equal(campaign.status,200);
    const c=await campaign.json();
    const form=await request(c.path);assert.equal(form.status,200);assert.match(await form.text(),/rider-form.js/);
    const token=c.path.split('/').at(-1), endpoint='/api/public/forms/'+token;
    const payload={iqama:'2000000999',name:'Release Rider',workSystem:'tiers'};
    assert.equal((await request(endpoint,{...payload,iqama:'٢٠٠٠٠٠٠٩٩٩'})).status,400);
    assert.equal((await request(endpoint,payload)).status,200);
    assert.equal((await request(endpoint,payload)).status,409);
    const responses=await request('/api/submissions',undefined,{Cookie:session});const rows=await responses.json();assert.equal(rows.length,1);assert.equal(rows[0].status,'pending');
    const riders=await request('/api/riders',undefined,{Cookie:session});assert.deepEqual(await riders.json(),[]);
    assert.equal((await request('/api/submissions')).status,401);
    const preflight=await request(endpoint,undefined,{Origin:'https://attacker.invalid'});assert.equal(preflight.headers.get('access-control-allow-origin'),null);
  } catch(e){throw new Error(`${e.message}\n${logs.slice(-1600)}`);} finally {
    if(proc.exitCode===null && proc.pid){const exited=new Promise(r=>proc.once('exit',r));proc.kill();await exited;}
    rmSync(data,{recursive:true,force:true});
  }
}
assert.equal(JSON.parse(readFileSync(path.join(root,'Payroll.Api/appsettings.Production.json'),'utf8')).Payroll.BootstrapKey,'');
await scenario('',503,19351);
await scenario('replace_with_24_char_secure_key',503,19352);
await scenario(randomBytes(32).toString('hex'),200,19353);
console.log(`PASS ${checks} production HTTPS/bootstrap/origin/session/form checks.`);
