import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import path from 'node:path';
// Explicit opt-in only on disposable CI runners: Start persists the mock target.
// Never point these tests at a physical device or a user's settings directory.
test('CI loopback device: real bridge sends clock 3191, six API metrics and numeric LocalToken',{skip:process.env.DIVOOM_CI_SENDING_TESTS!=='1',timeout:45000},async t=>{
  const messages=[];
  const server=createServer((req,res)=>{let raw='';req.setEncoding('utf8');req.on('data',part=>raw+=part);req.on('end',()=>{messages.push({path:req.url,length:Number(req.headers['content-length']),bytes:Buffer.byteLength(raw),body:JSON.parse(raw)});res.writeHead(200,{'Content-Type':'application/json'});res.end('{"ReturnCode":0}');});});
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(9000,'127.0.0.1',resolve);});
  t.after(()=>{server.closeAllConnections();server.close();});
  const executable=process.env.DIVOOM_BACKEND||path.resolve('backend',process.platform==='win32'?'Divoom.HardwareMonitor.exe':'Divoom.MacMonitor');
  const child=spawn(executable,['--bridge'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let id=0,snapshot=null;const pending=new Map();
  const exited=new Promise((resolve,reject)=>{child.once('exit',resolve);child.once('error',reject);});
  const lines=createInterface({input:child.stdout});
  lines.on('line',line=>{const value=JSON.parse(line);if(value.type==='snapshot')snapshot=value.data;else if(pending.has(value.id)){pending.get(value.id)(value);pending.delete(value.id);}});
  child.stderr.resume();
  t.after(()=>{lines.close();if(child.exitCode===null)child.kill();});
  const request=(command,payload=null)=>new Promise(resolve=>{const next=++id;pending.set(next,resolve);child.stdin.write(JSON.stringify({id:next,command,payload})+'\n');});
  const init=await request('init');assert.equal(init.ok,true);
  const deadline=Date.now()+20000;while(!snapshot&&Date.now()<deadline)await new Promise(resolve=>setTimeout(resolve,100));assert.ok(snapshot);
  const settings={...init.data.settings,DeviceIp:'127.0.0.1',DeviceHardware:530,DeviceId:42,LcdIndex:0,LcdIndependence:0,GpuId:snapshot.Gpus[0]?.Id||'',DiskId:snapshot.Disks[0]?.Id||''};
  assert.equal((await request('start',{settings,localToken:'1234'})).ok,true);
  const sentDeadline=Date.now()+8000;while(messages.length<2&&Date.now()<sentDeadline)await new Promise(resolve=>setTimeout(resolve,50));
  assert.equal((await request('stop')).ok,true);
  const clock=messages.find(m=>m.body.Command==='Channel/SetClockSelectId'),data=messages.find(m=>m.body.Command==='Device/UpdatePCParaInfo');
  assert.ok(clock);assert.equal(clock.body.ClockId,3191);assert.ok(data);assert.equal(data.body.DispData.length,6);
  assert.equal(data.body.DeviceId,42);assert.equal('ClockId' in data.body,false);
  for(const message of [clock,data]){assert.equal(message.path,'/divoom_api');assert.equal(message.length,message.bytes);assert.equal(message.body.LocalToken,1234);assert.equal('DeviceToken' in message.body,false);}
  const after=await request('init');assert.equal('LocalToken' in after.data.settings,false);
  assert.equal((await request('quit')).ok,true);assert.equal(await exited,0);
});
