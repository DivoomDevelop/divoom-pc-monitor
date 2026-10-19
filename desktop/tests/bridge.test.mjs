import test from 'node:test';
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import path from 'node:path';
test('real hardware bridge: compact RPC, native snapshot, validation and clean exit',{timeout:30000},async t=>{
  const executable=process.env.DIVOOM_BACKEND||path.resolve('backend',process.platform==='win32'?'Divoom.HardwareMonitor.exe':'Divoom.MacMonitor');
  const child=spawn(executable,['--bridge'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  const pending=new Map();let id=0,snapshot=null,stderr='';
  child.stderr.on('data',chunk=>stderr+=chunk);
  const exited=new Promise((resolve,reject)=>{child.once('exit',code=>resolve(code));child.once('error',reject);});
  const lines=createInterface({input:child.stdout});
  lines.on('line',line=>{try{const data=JSON.parse(line);if(data.type==='snapshot')snapshot=data.data;else if(pending.has(data.id)){pending.get(data.id)(data);pending.delete(data.id);}}catch{assert.fail('Invalid NDJSON from bridge');}});
  function request(command,payload=null){return new Promise(resolve=>{const sequence=++id;pending.set(sequence,resolve);child.stdin.write(JSON.stringify({id:sequence,command,payload})+'\n');});}
  t.after(()=>{lines.close();if(child.exitCode===null)child.kill();});
  const init=await request('init');assert.equal(init.ok,true);assert.equal(init.data.languages.length,19);assert.equal(init.data.running,false);
  assert.equal('LocalToken' in init.data.settings,false);
  const invalid={...init.data.settings,DeviceIp:'0.0.0.0',DeviceHardware:530,DeviceId:1};
  assert.equal((await request('start',{settings:invalid,localToken:''})).ok,false);
  assert.equal((await request('start',{settings:{...invalid,DeviceIp:'127.0.0.1'},localToken:'-1'})).ok,false);
  assert.equal((await request('preferences',{settings:{...invalid,DeviceHardware:-1}})).ok,false);
  assert.equal((await request('not-a-command')).ok,false);
  const deadline=Date.now()+12000;
  while(!snapshot&&Date.now()<deadline)await new Promise(resolve=>setTimeout(resolve,100));
  assert.ok(snapshot,'Native snapshot not received: '+stderr);assert.ok(snapshot.TotalMemoryBytes>0);assert.ok(Array.isArray(snapshot.Disks));
  const report=await request('diagnostics');assert.equal(report.ok,true);assert.equal(report.data.UI,'Tauri');assert.equal('LocalToken' in report.data,false);
  assert.equal((await request('stop')).ok,true);assert.equal((await request('quit')).ok,true);
  assert.equal(await exited,0,stderr);
});
