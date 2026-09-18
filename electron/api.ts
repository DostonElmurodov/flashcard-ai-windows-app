import { safeStorage } from 'electron';
import { existsSync,readFileSync,writeFileSync,renameSync,unlinkSync } from 'node:fs';
import type { AccountState,Profile,Entitlement,Draft,CatalogDeck } from '../shared/types';
import {FeatureFlags} from './feature-flags';
interface Session {access_token:string;access_token_expires_at:string;refresh_token:string;profile:Profile}
export class Api {
 private featureFlags:FeatureFlags;
 private session:Session|null=null; private entitlement:Entitlement|null=null; private generation=0; private refreshing:Promise<void>|null=null;
 constructor(private tokenPath:string,private base:()=>string){
  this.featureFlags=new FeatureFlags(tokenPath+'.features.json',base);
  if(existsSync(tokenPath)&&safeStorage.isEncryptionAvailable())try{const stored=JSON.parse(safeStorage.decryptString(readFileSync(tokenPath)));if(stored.base===this.base())this.session=stored.session;}catch{/* An unreadable token never grants access. */}
 }
 state():AccountState{return {profile:this.session?.profile??null,entitlement:this.entitlement,testMode:this.featureFlags.testMode};}
 async loginGoogle(authorize:()=>Promise<string|null>,signal?:AbortSignal){
  const generation=++this.generation;
  const idToken=await authorize();
  signal?.throwIfAborted();
  if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');
  if(idToken===null)return null;
  const response=await this.send('/owlai/account/desktop/google/session','POST',{id_token:idToken},undefined,signal);
  const session=await this.result<Session>(response);
  signal?.throwIfAborted();
  if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');
  this.entitlement=null;this.save(session);return this.state();
 }
 private save(session:Session){if(!safeStorage.isEncryptionAvailable())throw new Error('Windows secure credential storage is unavailable. Sign-in was not saved.');const tmp=this.tokenPath+'.tmp';writeFileSync(tmp,safeStorage.encryptString(JSON.stringify({base:this.base(),session})));renameSync(tmp,this.tokenPath);this.session=session;}
 clear(){this.generation++;this.session=null;this.entitlement=null;if(existsSync(this.tokenPath))unlinkSync(this.tokenPath);}
 private async send(path:string,method:string,body:unknown,token?:string,signal?:AbortSignal):Promise<Response>{
  const url=new URL(this.base());if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))throw new Error('Use HTTPS, or localhost for a development server.');
  return fetch(new URL(path,url),{method,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.any([AbortSignal.timeout(30000),...(signal?[signal]:[])]),redirect:'error'});
 }
 private async result<T>(response:Response):Promise<T>{if(response.status===204)return undefined as T;const text=await response.text();let data:any;try{data=JSON.parse(text);}catch{if(response.ok)throw new Error(`Server returned an unreadable response (${response.status}).`);data=null;}if(!response.ok){const message=response.status===402?'An active shared Premium subscription is needed. Link your Apple purchase in Owl AI on iPhone.':response.status===429?'You have reached the request limit. Please try again later.':response.status===404?'The server must be updated before Windows sign-in and synchronization are available. Please contact support.':data?.error??data?.title??`Request failed (${response.status}).`;throw new Error(message);}return data as T;}
 async login(email:string,password:string){const generation=++this.generation;const response=await this.send('/owlai/account/desktop/email/session','POST',{email,password});const session=await this.result<Session>(response);if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');this.entitlement=null;this.save(session);return this.state();}
 private async refresh(){if(this.refreshing)return this.refreshing;const session=this.session;if(!session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;
  this.refreshing=(async()=>{const response=await this.send('/owlai/account/session/refresh','POST',{refresh_token:session.refresh_token});if(generation!==this.generation)return;if(response.status===401){this.clear();throw new Error('Your session expired. Please sign in again.');}const renewed=await this.result<Session>(response);if(generation===this.generation)this.save(renewed);})().finally(()=>{this.refreshing=null;});return this.refreshing;
 }
 async request<T>(path:string,method='POST',body?:unknown,signal?:AbortSignal):Promise<T>{if(!this.session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;if(new Date(this.session.access_token_expires_at).getTime()<Date.now()+30000)await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your account session changed.');let response=await this.send(path,method,body,this.session.access_token,signal);if(response.status===401){await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your session expired.');response=await this.send(path,method,body,this.session.access_token,signal);}if(generation!==this.generation)throw new Error('Your account session changed.');return this.result<T>(response);}
 async refreshEntitlement(){await this.featureFlags.refresh();if(!this.session)return this.state();const generation=this.generation;try{const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');if(generation===this.generation)this.entitlement=ent;return this.state();}catch(error){if(generation===this.generation)this.entitlement=null;throw error;}}
 async logout(){const refresh=this.session?.refresh_token;this.clear();if(refresh)try{await this.send('/owlai/account/session/logout','POST',{refresh_token:refresh});}catch{/* Local logout succeeds offline; server session expires normally. */}}
 async deleteAccount(){await this.request('/owlai/account','DELETE');this.clear();}
 async translate(word:string,native:string,learning:string):Promise<Draft>{const result=await this.request<any>('/owlai/account/ai/word-detail','POST',{word,native_language:native,learning_language:learning});const translation=result.translations?.join('; ')??result.translation;if(typeof translation!=='string'||!translation.trim())throw new Error('No translation returned. Try a different word.');return {word:result.corrected_word??word,translation,pronunciation:result.pronunciation,examples:result.examples};}
 catalog(query:string){return this.request<CatalogDeck[]>('/owlai/account/public-flashcard-sets/catalog','POST',{query,limit:40});}
}
