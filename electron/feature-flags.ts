import {ACCESS_TTL_MS} from '../shared/access-policy';
interface ConfirmedFlags {origin:string;testMode:boolean;at:number}
export class FeatureFlags {
 private confirmed:ConfirmedFlags|null=null;
 private pending:Promise<void>|null=null;private generation=0;private selected:string|null=null;
 constructor(_legacyCachePath:string,private base:()=>string,private request:typeof fetch=fetch,private now:()=>number=Date.now){}
 private origin():string|null{
  try{const url=new URL(this.base());if(url.username||url.password||url.search||url.hash||url.pathname!=='/')return null;
   if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))return null;return url.origin;
  }catch{return null;}
 }
 invalidate(){this.generation++;this.confirmed=null;this.pending=null;this.selected=this.origin();}
 private observe(){const origin=this.origin();if(origin!==this.selected){this.invalidate();}return origin;}
 get testMode(){const origin=this.observe();return origin!==null&&this.confirmed?.origin===origin&&this.confirmed.testMode&&this.now()-this.confirmed.at>=0&&this.now()-this.confirmed.at<ACCESS_TTL_MS;}
 async refresh():Promise<void>{
  const origin=this.observe();if(!origin)return;if(this.pending)return this.pending;
  const generation=this.generation;
  const refresh=(async()=>{try{
   const response=await this.request(new URL('/owlai/config/feature-flags',origin),{method:'GET',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
   if(!response.ok)throw new Error('Feature flags unavailable');const value=await response.json();
   if(this.observe()!==origin||generation!==this.generation)return;
   if(typeof value?.test_mode!=='boolean')throw new Error('Invalid feature flags');this.confirmed={origin,testMode:value.test_mode,at:this.now()};
  }catch{if(this.observe()===origin&&generation===this.generation)this.confirmed=null;}
  })().finally(()=>{if(this.pending===refresh)this.pending=null;});this.pending=refresh;return refresh;
 }
}
