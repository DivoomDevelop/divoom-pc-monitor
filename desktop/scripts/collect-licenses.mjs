import fs from 'node:fs';import path from 'node:path';import {execFileSync} from 'node:child_process';
const root=path.resolve(import.meta.dirname,'../..'),desktop=path.join(root,'desktop'),destination=path.join(desktop,'backend/licenses/tauri');
fs.mkdirSync(destination,{recursive:true});
const triple=process.platform==='win32'?'x86_64-pc-windows-msvc':process.arch==='arm64'?'aarch64-apple-darwin':'x86_64-apple-darwin';
const metadata=JSON.parse(execFileSync('cargo',['metadata','--locked','--format-version','1','--filter-platform',triple],{cwd:path.join(desktop,'src-tauri'),maxBuffer:32*1024*1024,encoding:'utf8'}));
const inventory=[];
for(const pkg of metadata.packages.filter(p=>p.source)){
  inventory.push({name:pkg.name,version:pkg.version,license:pkg.license,repository:pkg.repository});
  const folder=path.dirname(pkg.manifest_path),out=path.join(destination,pkg.name+'-'+pkg.version);fs.mkdirSync(out,{recursive:true});
  const names=fs.readdirSync(folder).filter(n=>/^(license|licence|copying|copyright|notice)/i.test(n));
  for(const name of names){const file=path.join(folder,name);if(fs.statSync(file).isFile())fs.copyFileSync(file,path.join(out,name));else if(fs.statSync(file).isDirectory())fs.cpSync(file,path.join(out,name),{recursive:true});}
  if(pkg.license_file){const file=path.resolve(folder,pkg.license_file);if(file.startsWith(folder+path.sep)&&fs.existsSync(file))fs.copyFileSync(file,path.join(out,path.basename(file)));}
}
fs.writeFileSync(path.join(destination,'inventory.json'),JSON.stringify(inventory,null,2));
const api=path.join(desktop,'node_modules/@tauri-apps/api');for(const file of fs.readdirSync(api).filter(n=>/^(license|notice)/i.test(n)))fs.copyFileSync(path.join(api,file),path.join(destination,'tauri-api-'+file));
console.log('Collected '+inventory.length+' Rust dependency records and available license files.');
