import {z} from 'zod';
import type {Deck,Word,SyncStatus} from '../shared/types';
import type {Store} from './store';

const identifier=z.string().min(1).max(200);
const date=z.string().datetime({offset:true});
const number=z.number().finite().nonnegative();
const card=z.object({due:date,stability:number,difficulty:number,elapsed_days:number,scheduled_days:number,reps:number.int(),lapses:number.int(),state:z.number().int().min(0).max(3),last_review:date.nullish(),learning_steps:number.int().optional().default(0)});
const deckData=z.object({name:z.string().min(1).max(512),description:z.string().max(8192).nullish().transform(v=>v??''),active:z.boolean(),native_language:identifier,learning_language:identifier,created_at:date,metadata:z.record(z.string(),z.unknown()).optional()});
const wordData=z.object({native_language:identifier.optional(),learning_language:identifier.optional(),deck_id:identifier,word:z.string().min(1).max(8192),translation:z.string().max(8192),pronunciation:z.string().max(8192).nullish(),examples:z.array(z.string().max(8192)).max(100).nullish(),notes:z.string().max(16384).nullish(),created_at:date,metadata:z.record(z.string(),z.unknown()).optional(),card,reverse:card});
export interface SyncRecord {kind:'deck'|'word';id:string;version:number;deleted:boolean;data:Record<string,unknown>|null}
export interface SyncChange extends Omit<SyncRecord,'version'> {base_version:number}
const record=z.object({kind:z.enum(['deck','word']),id:identifier,version:z.number().int().nonnegative(),deleted:z.boolean(),data:z.record(z.string(),z.unknown()).nullable()});
export const key=(row:Pick<SyncRecord,'kind'|'id'>)=>row.kind+':'+row.id;
export function canonical(value:unknown):string {if(Array.isArray(value))return '['+value.map(canonical).join(',')+']';if(value&&typeof value==='object')return '{'+Object.entries(value).filter(([,v])=>v!==undefined).sort(([a],[b])=>a.localeCompare(b)).map(([k,v])=>JSON.stringify(k)+':'+canonical(v)).join(',')+'}';return JSON.stringify(value);}
export function exportRecords(decks:Deck[],words:Word[],base:SyncRecord[]=[]):SyncRecord[]{const metadata=new Map(base.map(r=>[key(r),r.data?.metadata])),deckMap=new Map(decks.map(d=>[d.id,d]));return [
 ...decks.map(d=>({kind:'deck' as const,id:d.id,version:0,deleted:false,data:{name:d.name,description:d.description,active:d.active,native_language:d.nativeLanguage,learning_language:d.learningLanguage,created_at:d.createdAt,...(metadata.get('deck:'+d.id)?{metadata:metadata.get('deck:'+d.id)}:{})}})),
 ...words.map(w=>({kind:'word' as const,id:w.id,version:0,deleted:false,data:{native_language:w.nativeLanguage??deckMap.get(w.deckId)?.nativeLanguage,learning_language:w.learningLanguage??deckMap.get(w.deckId)?.learningLanguage,deck_id:w.deckId,word:w.word,translation:w.translation,pronunciation:w.pronunciation??null,examples:w.examples??[],notes:w.notes??null,created_at:w.createdAt,...(metadata.get('word:'+w.id)?{metadata:metadata.get('word:'+w.id)}:{}),card:{...w.card,last_review:w.card.last_review??null},reverse:{...w.reverse,last_review:w.reverse.last_review??null}}}))
 ];}
export function decodeRows(rows:SyncRecord[]):{decks:Deck[];words:Word[]}{
 const decks:Deck[]=[],words:Word[]=[];
 for(const r of rows){if(r.deleted)continue;if(r.kind==='deck'){const d=deckData.parse(r.data);decks.push({id:r.id,name:d.name,description:d.description,active:d.active,nativeLanguage:d.native_language,learningLanguage:d.learning_language,createdAt:d.created_at});}else{const w=wordData.parse(r.data);words.push({id:r.id,deckId:w.deck_id,nativeLanguage:w.native_language,learningLanguage:w.learning_language,word:w.word,translation:w.translation,pronunciation:w.pronunciation??undefined,examples:w.examples??[],notes:w.notes??undefined,createdAt:w.created_at,card:{...w.card,last_review:w.card.last_review??undefined},reverse:{...w.reverse,last_review:w.reverse.last_review??undefined}});}}
 const ids=new Set(decks.map(d=>d.id));if(words.some(w=>!ids.has(w.deckId)))throw new Error('Cloud cards reference a missing collection. Your local data was preserved.');return {decks,words};
}
export function changesSince(local:SyncRecord[],base:SyncRecord[]):SyncChange[]{
 const previous=new Map(base.map(r=>[key(r),r])),current=new Map(local.map(r=>[key(r),r]));const changes:SyncChange[]=[];
 for(const [k,row] of current){const old=previous.get(k);if(!old||old.deleted||canonical(old.data)!==canonical(row.data))changes.push({kind:row.kind,id:row.id,base_version:old?.version??0,deleted:false,data:row.data});}
 for(const [k,row] of previous)if(!row.deleted&&!current.has(k))changes.push({kind:row.kind,id:row.id,base_version:row.version,deleted:true,data:null});
 return changes;
}
export function parseReply(raw:unknown,owner:string){const reply=z.object({owner_id:identifier,records:z.array(record).max(100000),conflicts:z.array(record).optional().default([])}).parse(raw);if(reply.owner_id!==owner)throw new Error('Account mismatch. Sync stopped without changing local data.');const ids=new Set<string>();for(const r of reply.records){if(ids.has(key(r)))throw new Error('Duplicate cloud record.');ids.add(key(r));if(!r.deleted)(r.kind==='deck'?deckData:wordData).parse(r.data);}return reply;}

export interface SyncTransport {request<T>(path:string,method?:string,body?:unknown,signal?:AbortSignal):Promise<T>}
export class AccountSync {
 private controller=new AbortController();private running:Promise<SyncStatus>|null=null;
 status:SyncStatus={state:'idle'};
 constructor(private store:Store,readonly owner:string,private transport:SyncTransport){}
 async useCloud(){
  if(this.running)await this.running;
  this.controller.signal.throwIfAborted();
  this.running=this.resetFromCloud().finally(()=>{this.running=null;});return this.running;
 }
 private async resetFromCloud(){
  const raw=await this.transport.request('/owlai/account/sync','GET',undefined,this.controller.signal),reply=parseReply(raw,this.owner);this.controller.signal.throwIfAborted();const decoded=decodeRows(reply.records);this.store.preserveBeforeCloudReset();this.store.applySync(decoded.decks,decoded.words,reply.records);this.status={state:'synced',lastSyncedAt:new Date().toISOString(),message:'Cloud version restored. A local backup of your previous cards was saved.'} as SyncStatus;return this.status;
 }
 async stop(){this.controller.abort();await this.running?.catch(()=>{});}
 run():Promise<SyncStatus>{if(this.running)return this.running;this.running=this.perform().finally(()=>{this.running=null;});return this.running;}
 private async perform():Promise<SyncStatus>{
  if(this.controller.signal.aborted)return this.status;
  this.status={...this.status,state:'syncing',message:undefined};
  try{
   for(let batch=0;batch<25;batch++){
    const base=this.store.syncBaseline(),snapshot=this.store.snapshot(),sent=exportRecords(snapshot.decks,snapshot.words,base),changes=changesSince(sent,base);
    // Parents before cards; deleted parents only after their card tombstones.
    const priority=(c:SyncChange)=>c.kind==='deck'?(c.deleted?2:0):1;
    changes.sort((a,b)=>priority(a)-priority(b));
    const submitted=changes.slice(0,500),pending=changes.slice(500);
    const raw=await this.transport.request('/owlai/account/sync',submitted.length?'POST':'GET',submitted.length?{changes:submitted}:undefined,this.controller.signal);
    this.controller.signal.throwIfAborted();const reply=parseReply(raw,this.owner);
    if(reply.conflicts.length){this.status={state:'conflict',message:'These cards changed on both devices. Sync is paused to preserve your local edits.'};return this.status;}
    const now=this.store.snapshot(),current=exportRecords(now.decks,now.words,base),localEdits=changesSince(current,sent);
    const merged=new Map(reply.records.map(r=>[key(r),r]));
    for(const c of [...pending,...localEdits])merged.set(key(c),{...c,version:0});
    // Keep the old versions of unsent edits: a later batch must still detect conflicts.
    const baseline=new Map(reply.records.map(r=>[key(r),r])),old=new Map(base.map(r=>[key(r),r]));
    for(const c of pending){const k=key(c),previous=old.get(k);if(previous)baseline.set(k,previous);else baseline.delete(k);}
    const decoded=decodeRows([...merged.values()]);this.controller.signal.throwIfAborted();
    this.store.applySync(decoded.decks,decoded.words,[...baseline.values()]);
    if(!pending.length)break;
    if(batch===24)throw new Error('More changes remain. Sync again to continue.');
   }
   this.status={state:'synced',lastSyncedAt:new Date().toISOString()};
  }catch(error){if(!this.controller.signal.aborted)this.status={state:'error',message:error instanceof Error?error.message:'Sync failed. Your local cards are safe.'};}
  return this.status;
 }
}
