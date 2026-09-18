import type {SyncStatus} from '../shared/types';
const minute=60_000;
const retries=[30_000,minute,2*minute,5*minute,15*minute];

/** One scheduler per signed-in workspace. Network work remains in AccountSync. */
export class SyncScheduler {
 private stopped=false;
 private background=false;
 private reviewing=false;
 private conflict=false;
 private lastCompleted=Number.NEGATIVE_INFINITY;
 private dirtyAt:number|null=null;
 private revision=0;
 private failures=0;
 private retryAt:number|null=null;
 private running:Promise<SyncStatus>|null=null;
 constructor(private run:()=>Promise<SyncStatus>,private now=Date.now){}
 setBackground(value:boolean){this.background=value;}
 setReviewing(value:boolean){if(this.reviewing&&!value&&this.dirtyAt!==null)this.dirtyAt=this.now();this.reviewing=value;}
 changed(){this.revision++;this.dirtyAt=this.now()+15_000;}
 stop(){this.stopped=true;}
 tick():Promise<SyncStatus|undefined>{
  if(this.stopped||(this.reviewing&&!this.background)||this.conflict)return Promise.resolve(undefined);
  const now=this.now();
  // Edits and focus changes must not defeat an offline/server-error backoff.
  const due=this.retryAt??Math.min(this.dirtyAt??Infinity,this.lastCompleted+(this.background?30:5)*minute);
  return now>=due?this.runNow():Promise.resolve(undefined);
 }
 runNow():Promise<SyncStatus>{
  if(this.stopped)return Promise.resolve({state:'idle'});
  if(this.running)return this.running;
  const revision=this.revision;
  this.running=this.perform(revision).finally(()=>{this.running=null;});
  return this.running;
 }
 private async perform(revision:number):Promise<SyncStatus>{
  let result:SyncStatus;
  try{result=await this.run();}catch(error){result={state:'error',message:error instanceof Error?error.message:'Sync failed.'};}
  if(this.stopped)return result;
  this.lastCompleted=this.now();this.conflict=result.state==='conflict';
  if(result.state==='error'){
   this.retryAt=this.now()+retries[Math.min(this.failures++,retries.length-1)];
  }else{
   this.failures=0;this.retryAt=null;
   if(result.state==='synced'&&revision===this.revision)this.dirtyAt=null;
  }
  return result;
 }
}
