import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {join} from 'node:path';
export function usesTwelveHours(locale:string,pattern?:string){
 if(pattern){const tokens=pattern.replace(/'[^']*'/g,'');if(tokens.includes('H'))return false;if(tokens.includes('h'))return true;}
 return !!new Intl.DateTimeFormat(locale,{hour:'numeric'}).resolvedOptions().hour12;
}
export async function systemTimeFormat(locale:string):Promise<{hour12:boolean}>{
 let pattern:string|undefined;
 if(process.platform==='win32')try{
  const executable=join(process.env.SystemRoot??'C:\\Windows','System32','WindowsPowerShell','v1.0','powershell.exe');
  const result=await promisify(execFile)(executable,['-NoProfile','-NonInteractive','-Command',"(Get-ItemProperty -LiteralPath 'HKCU:\\Control Panel\\International' -Name sShortTime).sShortTime"],{windowsHide:true,timeout:4000});pattern=result.stdout.trim();
 }catch{/* Fall back to the Windows region when its custom format is unavailable. */}
 return {hour12:usesTwelveHours(locale,pattern)};
}
