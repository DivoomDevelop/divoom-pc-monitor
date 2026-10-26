import './styles.css';
import {invoke} from '@tauri-apps/api/core';
import {listen} from '@tauri-apps/api/event';
import {translator,LAN_NOTICE,SERVICE_ERROR,metric,gb,selectedGpu,selectedDisk,timeGate,endpoint} from './model.js';

const $=id=>document.getElementById(id);
const state={settings:null,snapshot:null,language:'en',platform:'windows',running:false,devices:[],report:null,systemVolume:''};
let t=translator('en');
const rpc=(command,payload=null)=>invoke('bridge_request',{command,payload});
const text=(id,value)=>{const el=$(id),content=value??'';if(el instanceof HTMLTextAreaElement){if(el.value!==content)el.value=content;}else el.textContent=content;el.title=content;};
function status(value,error=false){text('status',value);$('status-dot').className='status-dot'+(error?' error':state.running?' sending':'');}
function translate(){
  t=translator(state.language);
  document.documentElement.lang=state.language;
  document.querySelectorAll('[data-l10n]').forEach(el=>el.textContent=t(el.dataset.l10n));
  $('status').setAttribute('aria-label',t('诊断'));
  $('token').placeholder=t('可选：本地令牌');$('devices').setAttribute('aria-label',t('局域网设备'));
  text('permission-hint',t(state.platform==='windows'?'CPU 温度可能需要 PawnIO 和管理员权限。':'温度取决于硬件与系统支持；不可用时显示 --。'));
  text('smart-hint',t(state.platform==='windows'?'自动补充缺失的磁盘温度；部分磁盘需要管理员权限。':'自动查找 Homebrew 路径；不自动提权。'));
  $('smart-mode').options[0].textContent=t(state.platform==='windows'?'自动（推荐，使用内置 smartctl）':'自动查找 macOS smartctl');
  if($('language').options.length)$('language').options[0].textContent=t('跟随系统');
  document.querySelectorAll('[data-windows]').forEach(el=>el.hidden=state.platform!=='windows');
  setRunning(state.running);
}
function setRunning(running){state.running=running;document.querySelectorAll('[data-device]').forEach(el=>el.disabled=running||!state.settings);$('send').textContent=t(running?'停止发送':'开始发送');$('send').disabled=!state.settings||(!running&&!state.snapshot);}
function deviceSettings(){return {...state.settings,DeviceIp:$('ip').value.trim(),DeviceHardware:Number($('hardware').value),DeviceId:Number($('deviceId').value),TimeGate:timeGate($('hardware').value),LcdIndex:Number($('lcd').value),LcdIndependence:$('independent').checked?1:0,GpuId:$('gpu').value||state.settings.GpuId,DiskId:$('disk').value||state.settings.DiskId};}
function updateTarget(){const hardware=Number($('hardware').value);$('lcd-options').hidden=!timeGate(hardware);text('endpoint',endpoint($('ip').value.trim()||'192.168.1.100',hardware));}
function fillSettings(){const s=state.settings;$('ip').value=s.DeviceIp;$('hardware').value=s.DeviceHardware;$('deviceId').value=s.DeviceId;$('lcd').value=String(s.LcdIndex);$('independent').checked=s.LcdIndependence===1;$('smart-mode').value=String(s.SmartctlMode);$('smart-path').value=s.SmartctlPath;$('plugins').checked=s.EnableExternalProviders;$('language').value=s.Language;updateTarget();smartMode();}
function smartMode(){const custom=$('smart-mode').value==='2';$('smart-path').disabled=$('browse').disabled=!custom;}
function source(id,reading){text(id,reading?.Error||reading?.Source||'');}
function choices(select,values,selected){const signature=JSON.stringify(values);if(select.dataset.signature!==signature){select.replaceChildren(...values.map(([value,label])=>new Option(label,value)));select.dataset.signature=signature;}if(selected)select.value=selected;}
function render(){
  const s=state.snapshot;if(!s||!state.settings)return;
  if(!$('send').dataset.busy)$('send').disabled=false;
  const gpu=selectedGpu(s,state.settings.GpuId),disk=selectedDisk(s,state.settings.DiskId,state.systemVolume);
  choices($('gpu'),s.Gpus.map(g=>[g.Id,g.Name]),gpu?.Id);choices($('disk'),s.Disks.map(d=>[d.Id,t('硬盘')+' '+d.Index+' · '+d.Name]),disk?.Id);
  text('cpu-name',s.CpuName);text('cpu-load',metric(s.CpuLoad));text('cpu-temp',t('温度')+' '+metric(s.CpuTemperature));source('cpu-source',s.CpuTemperature);
  text('gpu-name',gpu?.Name||t('未检测到支持的显卡'));text('gpu-load',metric(gpu?.Load));text('gpu-temp',t('温度')+' '+metric(gpu?.Temperature));source('gpu-source',gpu?.Temperature);
  text('memory-load',metric(s.MemoryLoad));text('memory-bytes',gb(s.TotalMemoryBytes-s.AvailableMemoryBytes)+' / '+gb(s.TotalMemoryBytes));source('memory-source',s.MemoryLoad);
  text('disk-name',disk?.Name||t('未检测到磁盘'));text('disk-temp',metric(disk?.Temperature));text('disk-activity',t('活动率')+' '+metric(disk?.Activity));source('disk-source',disk?.Temperature);
  const rows=s.Disks.map(d=>[t('硬盘')+' '+d.Index,d.Name,t('按卷查看'),metric(d.Activity),metric(d.Temperature)]);
  s.Volumes.forEach(v=>rows.push([v.Name+(v.DiskIndex!==null?' → '+t('硬盘')+' '+v.DiskIndex:''),t('已用')+' '+gb(v.TotalBytes-v.FreeBytes)+' / '+gb(v.TotalBytes),v.TotalBytes>0?((v.TotalBytes-v.FreeBytes)*100/v.TotalBytes).toFixed(1)+'%':'--','','']));
  $('disks').replaceChildren(...rows.map(row=>{const tr=document.createElement('tr');for(const value of row){const td=document.createElement('td');td.textContent=value;td.title=value;tr.append(td);}return tr;}));
  if($('diagnostics').classList.contains('active'))renderDiagnostics();
}
function renderDiagnostics(){const el=$('diagnostic-text'),scroll=el.scrollTop,{RecentLog,...report}=state.report||{};el.value=JSON.stringify({...report,Snapshot:state.snapshot},null,2)+(RecentLog?'\n\n'+RecentLog:'');el.scrollTop=scroll;}
async function busy(button,action){button.disabled=true;button.dataset.busy='true';try{return await action();}catch(error){status(String(error),true);}finally{delete button.dataset.busy;button.disabled=button.hasAttribute('data-device')?state.running:!state.settings;}}
async function preferences(){if(!state.settings||state.running)return;const proposed=deviceSettings();await rpc('preferences',{settings:proposed});state.settings=proposed;}
function on(id,action){$(id).addEventListener('click',()=>busy($(id),action));}
document.querySelectorAll('[data-tab]').forEach(button=>button.addEventListener('click',async()=>{
  document.querySelectorAll('[data-tab]').forEach(b=>b.classList.toggle('active',b===button));
  document.querySelectorAll('.page').forEach(p=>p.classList.toggle('active',p.id===button.dataset.tab));
  if(button.dataset.tab==='diagnostics'&&state.settings){try{state.report=await rpc('diagnostics');renderDiagnostics();}catch(error){status(String(error),true);}}
}));
$('ip').addEventListener('input',()=>{$('deviceId').value='0';$('token').value='';$('devices').value='';updateTarget();});
$('hardware').addEventListener('input',updateTarget);
for(const id of ['ip','hardware','deviceId','lcd','independent','gpu','disk'])$(id).addEventListener('change',()=>{preferences().then(render).catch(e=>status(String(e),true));});
$('smart-mode').addEventListener('change',smartMode);
$('devices').addEventListener('change',async()=>{
  const device=state.devices[Number($('devices').value)];if(!device)return;
  $('token').value='';$('ip').value=device.DevicePrivateIP;$('hardware').value=device.Hardware;$('deviceId').value=device.DeviceId;updateTarget();
  const selected=device.DeviceId;
  try{if(timeGate(device.Hardware)){const mode=await rpc('lcdMode',{deviceId:selected});if(Number($('deviceId').value)===selected&&!state.running)$('independent').checked=mode===1;}}catch(error){status(t('LCD 模式未读到，可手动设置: ')+String(error),true);}
  await preferences().catch(e=>status(String(e),true));
});
on('discover',async()=>{
  status(t('正在获取同局域网设备列表…'));state.devices=await rpc('discover');
  $('devices').replaceChildren(new Option('—',''),...state.devices.map((d,i)=>new Option(d.DeviceName+' · '+d.DevicePrivateIP,String(i))));
  status(state.devices.length?t('发现 {0} 台设备，选择设备或手填 IP。').replace('{0}',state.devices.length):t('未发现设备，可手动填写 IP。'));
});
on('send',async()=>{
  if(state.running){await rpc('stop');setRunning(false);}
  else{const proposed=deviceSettings();await rpc('start',{settings:proposed,localToken:$('token').value});state.settings=proposed;setRunning(true);}
});
on('rescan',async()=>{await rpc('rescan');status(t('正在检测…'));});
on('pawnio',()=>invoke('open_resource',{resource:'pawnio'}));on('smart-download',()=>invoke('open_resource',{resource:'smartmontools'}));on('logs',()=>invoke('open_resource',{resource:'logs'}));
on('browse',async()=>{const path=await invoke('choose_smartctl');if(path)$('smart-path').value=path;});
on('export',async()=>{if(await invoke('export_diagnostics'))status(t('诊断报告已保存。'));});
on('save',async()=>{
  const proposed={...deviceSettings(),Language:$('language').value,SmartctlMode:Number($('smart-mode').value),SmartctlPath:$('smart-path').value.trim(),EnableExternalProviders:$('plugins').checked};
  const result=await rpc('save',{settings:proposed});state.settings=result.settings;state.language=result.language;translate();await invoke('set_language',{code:state.language});render();status(t('采集设置已保存，正在重新检测传感器。'));
});
on('elevate',async()=>{await preferences();await invoke('restart_as_admin');});
async function init(){
  document.querySelectorAll('input,select,button:not([data-tab])').forEach(el=>el.disabled=true);translate();
  await listen('monitor',({payload})=>{
    if(payload.type==='snapshot'){state.snapshot=payload.data;render();}
    else if(payload.type==='status'){setRunning(payload.running);status(payload.data,payload.severity==='error');}
  });
  await listen('service-exited',()=>{state.settings=null;setRunning(false);status(t(SERVICE_ERROR),true);});
  const result=await rpc('init');Object.assign(state,{settings:result.settings,language:result.language,platform:result.platform,systemVolume:result.systemVolume});
  state.snapshot=state.snapshot||result.snapshot;
  $('language').replaceChildren(new Option(t('跟随系统'),'auto'),...result.languages.map(l=>new Option(l.Name,l.Code)));
  document.querySelectorAll('input,select,button').forEach(el=>el.disabled=false);
  fillSettings();translate();text('version','v1.3.2');setRunning(result.running);status(result.status);
  await invoke('set_language',{code:state.language});render();
  // Refresh age-sensitive fields even when a collection worker stops responding.
  setInterval(render,1000);
}
init().catch(error=>{document.querySelectorAll('input,select,button:not([data-tab])').forEach(el=>el.disabled=true);status(t(SERVICE_ERROR)+' '+String(error),true);});
