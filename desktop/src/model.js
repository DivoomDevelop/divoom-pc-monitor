import catalog from '../../locales/catalog.json' with { type: 'json' };
export const LAN_NOTICE='仅支持连接与本电脑处于同一局域网的设备。';
export const SERVICE_ERROR='连接硬件采集后台失败，请重新启动应用。';
export function translator(code){const keys=catalog.keys.split('|'),values=(catalog[code]||catalog.en).split('|'),map=new Map(keys.map((k,i)=>[k,values[i]]));return key=>map.get(key)??key;}
export function languageCodes(){return Object.keys(catalog).filter(k=>k!=='keys');}
export function timeGate(hardware){return Number(hardware)===400||Number(hardware)===402;}
export function endpoint(ip,hardware){return [90,92,400].includes(Number(hardware))?'http://'+ip+':80/post':'http://'+ip+':9000/divoom_api';}
export function metric(value,now=Date.now()){if(!value||!Number.isFinite(value.Value))return '--';const age=now-Date.parse(value.SampledAt),limit=(value.Source||'').toLowerCase().startsWith('smartctl')?65000:10000;return Number.isFinite(age)&&age>=-2000&&age<=limit?value.Value.toFixed(1)+(value.Unit||''):'--';}
export function gb(bytes){return (Number(bytes||0)/1073741824).toFixed(1)+' GB';}
export function selectedGpu(snapshot,id){return snapshot?.Gpus.find(g=>g.Id===id||g.MatchKey===id)??snapshot?.Gpus.find(g=>g.IsDiscrete)??snapshot?.Gpus[0];}
export function selectedDisk(snapshot,id,systemVolume){return snapshot?.Disks.find(d=>d.Id===id)??snapshot?.Disks.find(d=>d.Index===snapshot?.Volumes.find(v=>v.Name.toLowerCase()===systemVolume?.toLowerCase())?.DiskIndex)??snapshot?.Disks[0];}
