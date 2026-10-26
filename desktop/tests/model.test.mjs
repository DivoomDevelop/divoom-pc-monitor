import test from 'node:test';
import assert from 'node:assert/strict';
import {translator,languageCodes,LAN_NOTICE,SERVICE_ERROR,metric,timeGate,endpoint,selectedGpu,selectedDisk} from '../src/model.js';
test('same-LAN notice and service failure have all 19 translations',()=>{
  assert.equal(languageCodes().length,19);
  for(const code of languageCodes()){
    const t=translator(code);assert.ok(t(LAN_NOTICE).trim());assert.ok(t(SERVICE_ERROR).trim());
    if(code!=='zh-Hans')assert.notEqual(t(LAN_NOTICE),LAN_NOTICE,code);
  }
  assert.equal(translator('unknown')(LAN_NOTICE),translator('en')(LAN_NOTICE));
});
test('TimeGate visibility and endpoint rules remain independent',()=>{
  for(const hardware of [0,90,92,96,400,402,510,511,512,530,999]){
    assert.equal(timeGate(hardware),hardware===400||hardware===402);
    assert.equal(endpoint('192.168.1.100',hardware),[90,92,400].includes(hardware)?'http://192.168.1.100:80/post':'http://192.168.1.100:9000/divoom_api');
  }
});
test('missing, future and stale values never become a fabricated zero',()=>{
  const now=Date.now(),value={Value:0,Unit:'%',Source:'PDH',SampledAt:new Date(now).toISOString()};
  assert.equal(metric(value,now),'0.0%');assert.equal(metric({...value,Value:null},now),'--');
  assert.equal(metric({...value,Value:NaN},now),'--');
  assert.equal(metric({...value,SampledAt:new Date(now-11000).toISOString()},now),'--');
  assert.equal(metric({...value,SampledAt:new Date(now+3000).toISOString()},now),'--');
  assert.equal(metric({...value,Source:'smartctl',SampledAt:new Date(now-60000).toISOString()},now),'0.0%');
  assert.equal(metric({...value,Source:'smartctl',SampledAt:new Date(now-66000).toISOString()},now),'--');
});
test('GPU identity, discrete default and system disk selection preserve preferences',()=>{
  const snapshot={Gpus:[{Id:'a',MatchKey:'A',IsDiscrete:false},{Id:'b',MatchKey:'B',IsDiscrete:true}],Disks:[{Id:'disk/0',Index:0},{Id:'disk/1',Index:1}],Volumes:[{Name:'C:\\',DiskIndex:1}]};
  assert.equal(selectedGpu(snapshot,'A').Id,'a');assert.equal(selectedGpu(snapshot,'missing').Id,'b');
  assert.equal(selectedDisk(snapshot,'','C:\\').Index,1);assert.equal(selectedDisk(snapshot,'disk/0','C:\\').Index,0);
});
