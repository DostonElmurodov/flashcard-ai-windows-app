interface ConfirmedFlags {origin:string;testMode:boolean}

export class FeatureFlags {
 private confirmed:ConfirmedFlags|null=null;
 private pending=new Map<string,Promise<void>>();
 // Legacy cache files are intentionally ignored: only a live server response can grant test access.
 constructor(_legacyCachePath:string,private base:()=>string,private request:typeof fetch=fetch){}
 private origin():string|null{
  try{
   const url=new URL(this.base());
   if(url.username||url.password||url.search||url.hash||url.pathname!=='/')return null;
   if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))return null;
   return url.origin;
  }catch{return null;}
 }
 get testMode(){const origin=this.origin();return origin!==null&&this.confirmed?.origin===origin&&this.confirmed.testMode;}
 async refresh():Promise<void>{
  const origin=this.origin();if(!origin){this.confirmed=null;return;}
  const pending=this.pending.get(origin);if(pending)return pending;
  const refresh=(async()=>{
   try{
    const response=await this.request(new URL('/owlai/config/feature-flags',origin),{method:'GET',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
    if(!response.ok)throw new Error('Feature flags unavailable');
    const value=await response.json();
    if(this.origin()!==origin)return;
    if(typeof value?.test_mode!=='boolean')throw new Error('Invalid feature flags');
    this.confirmed={origin,testMode:value.test_mode};
   }catch{
    if(this.origin()===origin)this.confirmed={origin,testMode:false};
   }
  })().finally(()=>{this.pending.delete(origin);});
  this.pending.set(origin,refresh);return refresh;
 }
}
