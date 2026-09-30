import type {SyncStatus} from '../shared/types';

const minute=60_000;
const hour=60*minute;

/** One scheduler per signed-in workspace. Network work remains in AccountSync. */
export class SyncScheduler {
 private stopped=false;
 private active=true;
 private conflict=false;
 private lastAttemptAt:number|null=null;
 private dirtySince:number|null=null;
 private revision=0;
 private running:Promise<SyncStatus>|null=null;
 constructor(private run:()=>Promise<SyncStatus>,private now=Date.now,private hasPendingChanges=()=>false){
  if(hasPendingChanges())this.dirtySince=now();
 }
 setActive(value:boolean){this.active=value;}
 changed(){this.revision++;this.dirtySince??=this.now();}
 stop(){this.stopped=true;}
 tick():Promise<SyncStatus|undefined>{
  if(this.stopped||this.conflict)return Promise.resolve(undefined);
  if(this.lastAttemptAt===null)return this.runNow();
  const interval=this.active?(this.dirtySince===null?6*hour:20*minute):12*hour;
  const anchor=this.active&&this.dirtySince!==null?Math.max(this.lastAttemptAt,this.dirtySince):this.lastAttemptAt;
  return this.now()>=anchor+interval?this.runNow():Promise.resolve(undefined);
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
  this.lastAttemptAt=this.now();
  this.conflict=result.state==='conflict';
  if(result.state==='synced'&&revision===this.revision){
   this.dirtySince=this.hasPendingChanges()?this.now():null;
  }
  return result;
 }
}
