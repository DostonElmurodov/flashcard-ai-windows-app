import {readFileSync,writeFileSync,renameSync} from 'node:fs';

interface CachedFlags {origin:string;testMode:boolean}

export class FeatureFlags {
 private cached:CachedFlags|null=null;
 private pending=new Map<string,Promise<void>>();
 constructor(private path:string,private base:()=>string,private request:typeof fetch=fetch){
  try{
   const value=JSON.parse(readFileSync(path,'utf8'));
   if(typeof value?.origin==='string'&&typeof value?.testMode==='boolean')this.cached=value;
  }catch{/* A missing or unreadable cache never enables test mode. */}
 }
 private origin():string|null{
  try{
   const url=new URL(this.base());
   if(url.username||url.password||url.search||url.hash||url.pathname!=='/')return null;
   if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))return null;
   return url.origin;
  }catch{return null;}
 }
 get testMode(){const origin=this.origin();return origin!==null&&this.cached?.origin===origin&&this.cached.testMode;}
 async refresh():Promise<void>{
  const origin=this.origin();if(!origin)return;
  const pending=this.pending.get(origin);if(pending)return pending;
  const refresh=(async()=>{
   try{
    const response=await this.request(new URL('/owlai/config/feature-flags',origin),{method:'GET',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
    if(!response.ok)return;
    const value=await response.json();
    if(typeof value?.test_mode!=='boolean'||this.origin()!==origin)return;
    this.cached={origin,testMode:value.test_mode};
    const tmp=this.path+'.tmp';writeFileSync(tmp,JSON.stringify(this.cached));renameSync(tmp,this.path);
   }catch{/* Keep only the last successful value for this server when offline. */}
  })().finally(()=>{this.pending.delete(origin);});
  this.pending.set(origin,refresh);return refresh;
 }
}
