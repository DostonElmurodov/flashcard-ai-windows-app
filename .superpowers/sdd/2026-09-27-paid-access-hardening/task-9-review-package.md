# Task 9 — base aba59fb8f5e829d2cd10065fd702cff2dc7223b3; head c05b03c2abd011b7232adde395350c36dbe3a231
c05b03c feat: enforce Windows paid access and retained content policy
 electron/api.ts                     | 37 ++++++++++++++---
 electron/feature-flags.ts           | 44 ++++++++------------
 electron/main.ts                    | 24 +++++------
 electron/review-translations.ts     |  2 +
 electron/store.ts                   | 39 ++++++++++++++----
 scripts/import-smoke.mjs            |  2 +-
 scripts/paid-access-smoke.ts        | 44 ++++++++++++++++++++
 scripts/secondary-review-ui.mjs     |  2 +-
 shared/access-policy.ts             | 31 ++++++++++++++
 shared/secondary-review.ts          |  3 +-
 shared/types.ts                     |  2 +-
 src/App.tsx                         |  7 ++--
 src/CreateSet.tsx                   | 10 +++--
 src/Profile.tsx                     |  5 ++-
 src/Review.tsx                      |  6 +--
 tests/access-policy.test.ts         | 82 +++++++++++++++++++++++++++++++++++++
 tests/access-ui.test.ts             |  5 +++
 tests/api-test-mode.test.ts         | 38 +++++++++++++++++
 tests/feature-flags.test.ts         |  9 ++++
 tests/fixtures/secondary-review.tsx |  2 +-
 tests/review-translations.test.ts   | 23 ++++++++++-
 tests/test-mode-ui.test.ts          |  4 ++
 22 files changed, 351 insertions(+), 70 deletions(-)
diff --git a/electron/api.ts b/electron/api.ts
index dbb16b3..5641709 100644
--- a/electron/api.ts
+++ b/electron/api.ts
@@ -1,45 +1,72 @@
 import {partOfSpeech} from '../shared/part-of-speech';
 import { safeStorage } from 'electron';
 import { existsSync,readFileSync,writeFileSync,renameSync,unlinkSync } from 'node:fs';
 import type { AccountState,Profile,Entitlement,Draft,CatalogDeck,ReviewTranslation,ReviewTranslationRequest } from '../shared/types';
 import {FeatureFlags} from './feature-flags';
+export class ApiError extends Error {
+ constructor(message:string,readonly status:number,readonly code?:string,readonly retryAfterSeconds?:number){super(message);}
+}
+export function retryAfterSeconds(value:string|null,now=Date.now()):number|undefined{
+ if(!value)return undefined;if(/^\d+$/.test(value.trim()))return Number(value.trim());const date=Date.parse(value);return Number.isFinite(date)?Math.max(0,Math.ceil((date-now)/1000)):undefined;
+}
 interface Session {access_token:string;access_token_expires_at:string;refresh_token:string;profile:Profile}
 export class Api {
- private featureFlags:FeatureFlags;
+ private featureFlags:FeatureFlags;private selectedOrigin:string;
  private session:Session|null=null; private entitlement:Entitlement|null=null; private generation=0; private refreshing:Promise<void>|null=null;
  constructor(private tokenPath:string,private base:()=>string){
+  this.selectedOrigin=new URL(base()).origin;
   this.featureFlags=new FeatureFlags(tokenPath+'.features.json',base);
   if(existsSync(tokenPath)&&safeStorage.isEncryptionAvailable())try{const stored=JSON.parse(safeStorage.decryptString(readFileSync(tokenPath)));if(stored.base===this.base())this.session=stored.session;}catch{/* An unreadable token never grants access. */}
  }
- state():AccountState{return {profile:this.session?.profile??null,entitlement:this.entitlement,testMode:this.featureFlags.testMode};}
+ invalidateOrigin(){this.selectedOrigin=new URL(this.base()).origin;this.clear();this.featureFlags.invalidate();}
+ state():AccountState{if(new URL(this.base()).origin!==this.selectedOrigin)this.invalidateOrigin();return {profile:this.session?.profile??null,entitlement:this.entitlement,testMode:this.featureFlags.testMode};}
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
- private async result<T>(response:Response):Promise<T>{if(response.status===204)return undefined as T;const text=await response.text();let data:any;try{data=JSON.parse(text);}catch{if(response.ok)throw new Error(`Server returned an unreadable response (${response.status}).`);data=null;}if(!response.ok){const message=response.status===402?'An active shared Premium subscription is needed. Link your Apple purchase in Owl AI on iPhone.':response.status===429?'You have reached the request limit. Please try again later.':response.status===404?'The server must be updated before Windows sign-in and synchronization are available. Please contact support.':data?.error??data?.title??`Request failed (${response.status}).`;throw new Error(message);}return data as T;}
+ private async result<T>(response:Response):Promise<T>{if(response.status===204)return undefined as T;const text=await response.text();let data:any;try{data=JSON.parse(text);}catch{if(response.ok)throw new Error(`Server returned an unreadable response (${response.status}).`);data=null;}if(!response.ok){const retry=retryAfterSeconds(response.headers.get('Retry-After'));const code=data?.code;const message=response.status===402?'An active shared Premium subscription is needed. Link your Apple purchase in Owl AI on iPhone.':response.status===429?(retry===undefined?'You have reached the request limit. Please try again later.':`You have reached the request limit. Try again in ${retry} seconds.`):response.status===503?(code==='subscription_reconciliation_required'?'Your purchase needs verification to recover access. Contact support; your saved content is preserved.':'The service is temporarily unavailable. Please try again later.'):response.status===404?'The server must be updated before Windows sign-in and synchronization are available. Please contact support.':data?.error??data?.title??`Request failed (${response.status}).`;throw new ApiError(message,response.status,code,retry);}return data as T;}
  async login(email:string,password:string){const generation=++this.generation;const response=await this.send('/owlai/account/desktop/email/session','POST',{email,password});const session=await this.result<Session>(response);if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');this.entitlement=null;this.save(session);return this.state();}
  private async refresh(){if(this.refreshing)return this.refreshing;const session=this.session;if(!session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;
   this.refreshing=(async()=>{const response=await this.send('/owlai/account/session/refresh','POST',{refresh_token:session.refresh_token});if(generation!==this.generation)return;if(response.status===401){this.clear();throw new Error('Your session expired. Please sign in again.');}const renewed=await this.result<Session>(response);if(generation===this.generation)this.save(renewed);})().finally(()=>{this.refreshing=null;});return this.refreshing;
  }
- async request<T>(path:string,method='POST',body?:unknown,signal?:AbortSignal):Promise<T>{if(!this.session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;if(new Date(this.session.access_token_expires_at).getTime()<Date.now()+30000)await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your account session changed.');let response=await this.send(path,method,body,this.session.access_token,signal);if(response.status===401){await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your session expired.');response=await this.send(path,method,body,this.session.access_token,signal);}if(generation!==this.generation)throw new Error('Your account session changed.');return this.result<T>(response);}
- async refreshEntitlement(){await this.featureFlags.refresh();if(!this.session)return this.state();const generation=this.generation;try{const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');if(generation===this.generation)this.entitlement=ent;return this.state();}catch(error){if(generation===this.generation)this.entitlement=null;throw error;}}
+ async request<T>(path:string,method='POST',body?:unknown,signal?:AbortSignal):Promise<T>{
+  this.state();if(!this.session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;
+  if(new Date(this.session.access_token_expires_at).getTime()<Date.now()+30000)await this.refresh();
+  if(generation!==this.generation||!this.session)throw new Error('Your account session changed.');
+  let response=await this.send(path,method,body,this.session.access_token,signal);
+  if(response.status===401){
+   if(path.startsWith('/owlai/account/ai/'))throw new ApiError('Your session needs refreshing. Sign in or refresh your profile, then try again.',401);
+   await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your session expired.');response=await this.send(path,method,body,this.session.access_token,signal);
+  }
+  try{
+   const value=await this.result<T>(response);if(generation!==this.generation)throw new Error('Your account session changed.');return value;
+  }catch(error){
+   // The response body may complete after an account/origin transition.
+   if(generation===this.generation&&error instanceof ApiError){
+    if(error.code==='subscription_reconciliation_required')this.entitlement={status:'invalid_subscription',is_trial:false,auto_renew:false,was_ever_paid:false,checked_at:new Date().toISOString()};
+    else if([402,503].includes(error.status))this.entitlement=null;
+   }
+   throw error;
+  }
+ }
+ async refreshEntitlement(){await this.featureFlags.refresh();if(!this.session)return this.state();const generation=this.generation;try{const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');if(generation===this.generation)this.entitlement={...ent,checked_at:new Date().toISOString()};return this.state();}catch(error){if(generation===this.generation&&!(error instanceof ApiError&&error.code==='subscription_reconciliation_required'))this.entitlement=null;throw error;}}
  async logout(){const refresh=this.session?.refresh_token;this.clear();if(refresh)try{await this.send('/owlai/account/session/logout','POST',{refresh_token:refresh});}catch{/* Local logout succeeds offline; server session expires normally. */}}
  async deleteAccount(){await this.request('/owlai/account','DELETE');this.clear();}
  async translate(word:string,native:string,learning:string):Promise<Draft>{const result=await this.request<any>('/owlai/account/ai/word-detail','POST',{word,native_language:native,learning_language:learning});const translation=result.translations?.join('; ')??result.translation;if(typeof translation!=='string'||!translation.trim())throw new Error('No translation returned. Try a different word.');return {word:result.corrected_word??word,translation,partOfSpeech:partOfSpeech(result.part_of_speech),pronunciation:result.pronunciation,examples:result.examples};}
  reviewTranslation(input:ReviewTranslationRequest){return this.request<ReviewTranslation>('/owlai/account/ai/review-translation','POST',input);}
  catalog(query:string){return this.request<CatalogDeck[]>('/owlai/account/public-flashcard-sets/catalog','POST',{query,limit:40});}
 }
diff --git a/electron/feature-flags.ts b/electron/feature-flags.ts
index 716d1ed..5ceb18b 100644
--- a/electron/feature-flags.ts
+++ b/electron/feature-flags.ts
@@ -1,34 +1,26 @@
-interface ConfirmedFlags {origin:string;testMode:boolean}
-
+import {ACCESS_TTL_MS} from '../shared/access-policy';
+interface ConfirmedFlags {origin:string;testMode:boolean;at:number}
 export class FeatureFlags {
  private confirmed:ConfirmedFlags|null=null;
- private pending=new Map<string,Promise<void>>();
- // Legacy cache files are intentionally ignored: only a live server response can grant test access.
- constructor(_legacyCachePath:string,private base:()=>string,private request:typeof fetch=fetch){}
+ private pending:Promise<void>|null=null;private generation=0;private selected:string|null=null;
+ constructor(_legacyCachePath:string,private base:()=>string,private request:typeof fetch=fetch,private now:()=>number=Date.now){}
  private origin():string|null{
-  try{
-   const url=new URL(this.base());
-   if(url.username||url.password||url.search||url.hash||url.pathname!=='/')return null;
-   if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))return null;
-   return url.origin;
+  try{const url=new URL(this.base());if(url.username||url.password||url.search||url.hash||url.pathname!=='/')return null;
+   if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))return null;return url.origin;
   }catch{return null;}
  }
- get testMode(){const origin=this.origin();return origin!==null&&this.confirmed?.origin===origin&&this.confirmed.testMode;}
+ invalidate(){this.generation++;this.confirmed=null;this.pending=null;this.selected=this.origin();}
+ private observe(){const origin=this.origin();if(origin!==this.selected){this.invalidate();}return origin;}
+ get testMode(){const origin=this.observe();return origin!==null&&this.confirmed?.origin===origin&&this.confirmed.testMode&&this.now()-this.confirmed.at>=0&&this.now()-this.confirmed.at<ACCESS_TTL_MS;}
  async refresh():Promise<void>{
-  const origin=this.origin();if(!origin){this.confirmed=null;return;}
-  const pending=this.pending.get(origin);if(pending)return pending;
-  const refresh=(async()=>{
-   try{
-    const response=await this.request(new URL('/owlai/config/feature-flags',origin),{method:'GET',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
-    if(!response.ok)throw new Error('Feature flags unavailable');
-    const value=await response.json();
-    if(this.origin()!==origin)return;
-    if(typeof value?.test_mode!=='boolean')throw new Error('Invalid feature flags');
-    this.confirmed={origin,testMode:value.test_mode};
-   }catch{
-    if(this.origin()===origin)this.confirmed={origin,testMode:false};
-   }
-  })().finally(()=>{this.pending.delete(origin);});
-  this.pending.set(origin,refresh);return refresh;
+  const origin=this.observe();if(!origin)return;if(this.pending)return this.pending;
+  const generation=this.generation;
+  const refresh=(async()=>{try{
+   const response=await this.request(new URL('/owlai/config/feature-flags',origin),{method:'GET',cache:'no-store',redirect:'error',signal:AbortSignal.timeout(10000)});
+   if(!response.ok)throw new Error('Feature flags unavailable');const value=await response.json();
+   if(this.observe()!==origin||generation!==this.generation)return;
+   if(typeof value?.test_mode!=='boolean')throw new Error('Invalid feature flags');this.confirmed={origin,testMode:value.test_mode,at:this.now()};
+  }catch{if(this.observe()===origin&&generation===this.generation)this.confirmed=null;}
+  })().finally(()=>{if(this.pending===refresh)this.pending=null;});this.pending=refresh;return refresh;
  }
 }
diff --git a/electron/main.ts b/electron/main.ts
index 77a51af..d42dd6d 100644
--- a/electron/main.ts
+++ b/electron/main.ts
@@ -1,43 +1,43 @@
 import {partOfSpeech} from '../shared/part-of-speech';
 import { app,BrowserWindow,ipcMain,dialog,Menu,Notification,Tray,nativeImage,nativeTheme,shell } from 'electron';
 import { join } from 'node:path';
 import { pathToFileURL } from 'node:url';
 import { z } from 'zod';
 import { Store } from './store';
 import {Workspaces} from './workspace';
 import {AccountSync} from './sync';
 import {SyncScheduler} from './sync-scheduler';
 import {systemTimeFormat} from './time-format';
 import {reminderSlot} from './reminders';
-import { Api } from './api';
+import { Api,ApiError } from './api';
 import {ReviewTranslations} from './review-translations';
 import { googleSignIn } from './google';
 declare const GOOGLE_OAUTH_CLIENT_ID:string;
 declare const GOOGLE_OAUTH_CLIENT_SECRET:string;
 let googleAttempt:AbortController|null=null;
 import { parseImport } from './imports';
 import { importFile,exportDeck } from './files';
 import type { CatalogDeck } from '../shared/types';
 let reviewTranslations:ReviewTranslations;
 let window:BrowserWindow,store:Store,api:Api,tray:Tray|null=null,quitting=false;
 let workspaces:Workspaces,sync:AccountSync|null=null,transitioning=false,epoch=0,dialogs=0;
 let scheduler:SyncScheduler|null=null;
 function scheduleSync(){scheduler?.stop();scheduler=sync?new SyncScheduler(runSync):null;scheduler?.setBackground(!!window&&(!window.isVisible()||window.isMinimized()));}
 const scope=()=>epoch+':'+store.owner();
-const accountState=()=>({...api.state(),sync:sync?.status});
+const accountState=()=>({...store.accessState()??api.state(),sync:sync?.status});
 async function selectAccount(){
  scheduler?.stop();scheduler=null;await sync?.stop();sync=null;
  const profile=api.state().profile;
- try{await workspaces.select(workspaces.guest.settings().apiBase,profile?.id??null);}catch(error){api.clear();store=workspaces.guest;epoch++;throw error;}store=workspaces.current;epoch++;
+ try{await workspaces.select(workspaces.guest.settings().apiBase,profile?.id??null);}catch(error){api.clear();store=workspaces.guest;epoch++;throw error;}store=workspaces.current;store.bindAccess(()=>api.state());epoch++;
  if(profile)sync=new AccountSync(store,profile.id,api);scheduleSync();
  if(window&&!window.isDestroyed()){configureTheme();configureTray();}
 }
 async function changeAccount(action:()=>Promise<unknown>){
  if(transitioning||dialogs)throw new Error('Finish the current operation before changing accounts.');
  transitioning=true;epoch++;
  try{scheduler?.stop();await sync?.stop();await action();await selectAccount();return accountState();}
  catch(error){await selectAccount();throw error;}finally{transitioning=false;void scheduler?.tick();}
 }
 async function runSync(){const active=sync;if(!active)return {state:'idle' as const};const result=await active.run();if(active===sync&&!api.state().profile&&!transitioning&&!dialogs){transitioning=true;try{await selectAccount();}finally{transitioning=false;}}return result;}
 
 if(process.env.OWL_TEST_DATA_DIR)app.setPath('userData',process.env.OWL_TEST_DATA_DIR);
@@ -45,83 +45,83 @@ const id=z.string().min(1).max(200),str=z.string().trim().min(1).max(500),langua
 const draft=z.object({partOfSpeech:z.string().trim().min(1).max(100).optional(),word:str,translation:z.string().trim().min(1).max(5000),pronunciation:z.string().max(500).optional(),examples:z.array(z.string().max(3000)).max(20).optional(),notes:z.string().max(10000).optional()});
 const mode=z.enum(['auto','pairs','words']);
 const apiBase=z.string().url().refine(value=>{const url=new URL(value);return !url.username&&!url.password&&!url.search&&!url.hash&&url.pathname==='/'&&(url.protocol==='https:'||(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)));},'Use an HTTPS server origin or localhost.');
 function handle(name:string,fn:(...args:any[])=>unknown){ipcMain.handle('owl:'+name,async(event,envelope,...args)=>{
  if(event.sender!==window.webContents||event.senderFrame!==window.webContents.mainFrame)return {ok:false,error:'Untrusted window.'};
  const auth=['login','loginGoogle','logout','deleteAccount'].includes(name),unscoped=['snapshot','openAppMenu','systemTimeFormat','cancelGoogleLogin'].includes(name),dialogOperation=['backup','restore','exportDeck','importFile'].includes(name);
  const started=scope();let dialogStarted=false;
  try{
   if(transitioning&&name!=='cancelGoogleLogin')throw new Error('Account is changing. Please wait.');
   if(!unscoped&&envelope?.scope!==started)throw new Error('Your account changed. Refresh this page before continuing.');
   if(dialogOperation){dialogs++;dialogStarted=true;if(name==='restore'){scheduler?.stop();await sync?.stop();sync=null;}}
   const value=await fn(...args);
-  if(!auth&&started!==scope())throw new Error('Your account changed. Please try again.');
+  if(!auth&&name!=='saveSettings'&&started!==scope())throw new Error('Your account changed. Please try again.');
   if(['saveDeck','deleteDeck','addWords','editWord','deleteWord','review','importCatalog'].includes(name))scheduler?.changed();
   return {ok:true,value,scope:scope()};
- }catch(error){return {ok:false,error:error instanceof z.ZodError?'Please check the entered values.':error instanceof Error?error.message:'The operation could not be completed.'};}
+ }catch(error){if(error instanceof ApiError&&error.code==='subscription_reconciliation_required')store.rememberAccess(api.state());return {ok:false,error:error instanceof z.ZodError?'Please check the entered values.':error instanceof Error?error.message:'The operation could not be completed.'};}
  finally{if(dialogStarted){dialogs=Math.max(0,dialogs-1);if(name==='restore'&&api.state().profile){sync=new AccountSync(store,api.state().profile!.id,api);scheduleSync();}}if(store.owner()!=='guest'&&!api.state().profile&&!transitioning&&!dialogs){transitioning=true;try{await selectAccount();}finally{transitioning=false;}}}
  });}
 function register(){
  handle('openAppMenu',(name,x,y)=>{const index=['Owl AI','Edit','View'].indexOf(z.enum(['Owl AI','Edit','View']).parse(name));const zoom=window.webContents.getZoomFactor();const left=Math.round(z.number().int().min(0).max(10000).parse(x)*zoom),top=Math.round(z.number().int().min(0).max(10000).parse(y)*zoom);const menu=Menu.getApplicationMenu()?.items[index]?.submenu;if(!menu)return;return new Promise<void>(resolve=>menu.popup({window,x:left,y:top,callback:resolve}));});
  handle('systemTimeFormat',()=>systemTimeFormat(app.getSystemLocale()));
- handle('snapshot',()=>({...store.snapshot(),scopeRevision:scope(),account:accountState(),queueCount:store.queue(undefined,new Date(),api.state().testMode).length}));
+ handle('snapshot',()=>({...store.snapshot(),scopeRevision:scope(),account:accountState(),accessibleWordIds:[...store.eligibleIds()],queueCount:store.queue(undefined,new Date(),api.state().testMode).length}));
  handle('saveDeck',input=>store.saveDeck(z.object({id:id.optional(),name:str,description:z.string().max(2000).optional(),nativeLanguage:language.optional(),learningLanguage:language.optional(),active:z.boolean().optional()}).parse(input)));
  handle('deleteDeck',value=>store.deleteDeck(id.parse(value)));
  handle('addWords',(deck,rows)=>store.addWords(id.parse(deck),z.array(draft).min(1).max(2000).parse(rows)));
  handle('editWord',(value,row)=>store.editWord(id.parse(value),draft.parse(row)));
  handle('deleteWord',value=>store.deleteWord(id.parse(value)));
  handle('queue',value=>store.queue(id.optional().parse(value),new Date(),api.state().testMode));
  handle('previews',value=>store.previews(id.parse(value)));
  handle('review',(value,grade,attempt)=>store.review(id.parse(value),z.number().int().min(1).max(4).parse(grade),id.parse(attempt),api.state().testMode));
  handle('parse',(text,m)=>parseImport(z.string().max(5_000_000).parse(text),mode.parse(m)));
  handle('importFile',m=>importFile(window,mode.parse(m),store.settings().learningLanguage));
  handle('exportDeck',value=>exportDeck(window,store,id.parse(value)));
- handle('saveSettings',async value=>{const patch=z.object({spellingPractice:z.boolean(),nativeLanguage:language,learningLanguage:language,secondaryReviewLanguage:z.string().max(24).nullable(),theme:z.enum(['light','dark','system']),accent:z.enum(['indigo','teal','rose']),darkAccent:z.enum(['indigo','teal','rose']),dailyGoal:z.number().int().min(0).max(200),direction:z.enum(['forward','reverse']),dayStart:z.number().int().min(0).max(1439),retention:z.number().min(.7).max(.97),reminders:z.boolean(),reminderTime:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderStart:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderEnd:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderCount:z.number().int().min(1).max(100),keepInTray:z.boolean(),launchAtLogin:z.boolean(),apiBase,onboardingComplete:z.boolean()}).partial().parse(value);if(patch.apiBase&&patch.apiBase!==workspaces.guest.settings().apiBase&&api.state().profile)throw new Error('Sign out before changing servers.');const settings=store.saveSettings(patch);if(patch.launchAtLogin!==undefined)configureLogin();configureTheme();configureTray();return settings;});
+ handle('saveSettings',async value=>{const patch=z.object({spellingPractice:z.boolean(),nativeLanguage:language,learningLanguage:language,secondaryReviewLanguage:z.string().max(24).nullable(),theme:z.enum(['light','dark','system']),accent:z.enum(['indigo','teal','rose']),darkAccent:z.enum(['indigo','teal','rose']),dailyGoal:z.number().int().min(0).max(200),direction:z.enum(['forward','reverse']),dayStart:z.number().int().min(0).max(1439),retention:z.number().min(.7).max(.97),reminders:z.boolean(),reminderTime:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderStart:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderEnd:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderCount:z.number().int().min(1).max(100),keepInTray:z.boolean(),launchAtLogin:z.boolean(),apiBase,onboardingComplete:z.boolean()}).partial().parse(value);if(patch.apiBase&&patch.apiBase!==workspaces.guest.settings().apiBase&&api.state().profile)throw new Error('Sign out before changing servers.');const originChanged=!!patch.apiBase&&new URL(patch.apiBase).origin!==new URL(workspaces.guest.settings().apiBase).origin;const settings=store.saveSettings(patch);if(originChanged){api.invalidateOrigin();epoch++;}if(patch.launchAtLogin!==undefined)configureLogin();configureTheme();configureTray();return settings;});
  handle('backup',async()=>{const result=await dialog.showSaveDialog(window,{title:'Back up your cards and progress',defaultPath:'Owl-AI-backup.sqlite',filters:[{name:'Owl AI backup',extensions:['sqlite']}]});if(result.canceled||!result.filePath)return false;store.backup(result.filePath);return true;});
  handle('restore',async()=>{const result=await dialog.showOpenDialog(window,{title:'Restore Owl AI backup',properties:['openFile'],filters:[{name:'Owl AI backup',extensions:['sqlite']}]});if(result.canceled)return false;const confirm=await dialog.showMessageBox(window,{type:'warning',message:'Replace local cards and progress with this backup?',detail:'A copy of your current database will be kept. Only a backup from this same account or local workspace can be restored.',buttons:['Cancel','Restore backup'],defaultId:0,cancelId:0});if(confirm.response!==1)return false;await store.restore(result.filePaths[0]);configureTheme();configureTray();return true;});
  handle('account',()=>accountState());
  handle('sync',()=>scheduler?.runNow()??Promise.resolve({state:'idle'}));
  handle('reviewSession',active=>{scheduler?.setReviewing(z.boolean().parse(active));void scheduler?.tick();});
  handle('resolveSync',async()=>{if(!sync)throw new Error('Sign in to sync.');dialogs++;scheduler?.stop();try{return await sync.useCloud();}finally{dialogs--;scheduleSync();}});
  handle('loginGoogle',async()=>{
   if(googleAttempt)throw new Error('Google sign-in is already open in your browser.');
   const controller=new AbortController();googleAttempt=controller;
   try{return await changeAccount(()=>api.loginGoogle(()=>googleSignIn({clientId:GOOGLE_OAUTH_CLIENT_ID,clientSecret:GOOGLE_OAUTH_CLIENT_SECRET},url=>shell.openExternal(url),{signal:controller.signal}),controller.signal));}
   catch(error){if(controller.signal.aborted)return null;if(error instanceof Error&&error.name==='TimeoutError')throw new Error('Google sign-in timed out. Please try again.');throw error;}
   finally{googleAttempt=null;if(!window.isDestroyed()){window.show();window.focus();}}
  });
  handle('cancelGoogleLogin',()=>{googleAttempt?.abort();});
  handle('login',(email,password)=>changeAccount(()=>api.login(z.string().email().max(254).parse(email),z.string().min(1).max(1024).parse(password))));
- handle('logout',()=>changeAccount(()=>api.logout()));handle('deleteAccount',()=>changeAccount(()=>api.deleteAccount()));handle('refreshEntitlement',async()=>{try{await api.refreshEntitlement();return accountState();}finally{if(!api.state().profile&&store.owner()!=='guest'&&!transitioning&&!dialogs)await changeAccount(async()=>{});}});
+ handle('logout',()=>changeAccount(()=>api.logout()));handle('deleteAccount',()=>changeAccount(()=>api.deleteAccount()));handle('refreshEntitlement',async()=>{try{await api.refreshEntitlement();return accountState();}finally{store.rememberAccess(api.state());if(!api.state().profile&&store.owner()!=='guest'&&!transitioning&&!dialogs)await changeAccount(async()=>{});}});
  handle('previewTranslation',(word,native,learning)=>reviewTranslations.preview(str.max(120).parse(word),language.parse(native),language.parse(learning)));
  handle('reviewTranslation',wordId=>reviewTranslations.get(id.parse(wordId)));
- handle('translate',(word,native,learning)=>api.translate(str.parse(word),language.parse(native),language.parse(learning)));
+ handle('translate',(word,native,learning)=>{store.assertAccess('ai');return api.translate(str.parse(word),language.parse(native),language.parse(learning));});
  handle('catalog',query=>api.catalog(z.string().max(200).parse(query)));
- handle('importCatalog',input=>{const item=z.object({id,title:str,description:z.string().max(4000).nullish(),cards:z.array(z.object({part_of_speech:z.string().max(100).nullish(),word:str,translations:z.array(z.string().max(5000)).min(1).max(30),pronunciation:z.string().max(500).nullish(),examples:z.array(z.string().max(3000)).max(20),notes:z.string().max(10000).nullish(),native_language:language,learning_language:language})).min(1).max(2000)}).parse(input);const deck=store.saveDeck({name:item.title,description:item.description??'',nativeLanguage:item.cards[0].native_language,learningLanguage:item.cards[0].learning_language});try{store.addWords(deck.id,item.cards.map(c=>({word:c.word,translation:c.translations.join('; '),partOfSpeech:partOfSpeech(c.part_of_speech),pronunciation:c.pronunciation??undefined,examples:c.examples,notes:c.notes??undefined})));return deck;}catch(error){store.deleteDeck(deck.id);throw error;}});
- handle('publish',value=>{const key=id.parse(value),snap=store.snapshot(),deck=snap.decks.find(x=>x.id===key);if(!deck)throw new Error('Set not found.');const cards=snap.words.filter(x=>x.deckId===key);if(!cards.length)throw new Error('Add some cards before publishing.');return api.request('/owlai/account/public-flashcard-sets/publish','POST',{client_set_id:deck.id,title:deck.name,description:deck.description,cards:cards.map(w=>({client_card_id:w.id,word:w.word,translations:[w.translation],part_of_speech:w.partOfSpeech,pronunciation:w.pronunciation,examples:w.examples??[],example_translations:(w.examples??[]).map(()=>null),notes:w.notes,native_language:deck.nativeLanguage,learning_language:deck.learningLanguage}))});});
+ handle('importCatalog',input=>{const item=z.object({id,title:str,description:z.string().max(4000).nullish(),cards:z.array(z.object({part_of_speech:z.string().max(100).nullish(),word:str,translations:z.array(z.string().max(5000)).min(1).max(30),pronunciation:z.string().max(500).nullish(),examples:z.array(z.string().max(3000)).max(20),notes:z.string().max(10000).nullish(),native_language:language,learning_language:language})).min(1).max(2000)}).parse(input);return store.importDeck({name:item.title,description:item.description??'',nativeLanguage:item.cards[0].native_language,learningLanguage:item.cards[0].learning_language},item.cards.map(c=>({word:c.word,translation:c.translations.join('; '),partOfSpeech:partOfSpeech(c.part_of_speech),pronunciation:c.pronunciation??undefined,examples:c.examples,notes:c.notes??undefined})));});
+ handle('publish',value=>{store.assertAccess('edit');const key=id.parse(value),snap=store.snapshot(),deck=snap.decks.find(x=>x.id===key);if(!deck)throw new Error('Set not found.');const cards=snap.words.filter(x=>x.deckId===key);if(!cards.length)throw new Error('Add some cards before publishing.');return api.request('/owlai/account/public-flashcard-sets/publish','POST',{client_set_id:deck.id,title:deck.name,description:deck.description,cards:cards.map(w=>({client_card_id:w.id,word:w.word,translations:[w.translation],part_of_speech:w.partOfSpeech,pronunciation:w.pronunciation,examples:w.examples??[],example_translations:(w.examples??[]).map(()=>null),notes:w.notes,native_language:deck.nativeLanguage,learning_language:deck.learningLanguage}))});});
  handle('unpublish',value=>api.request('/owlai/account/public-flashcard-sets/unpublish','POST',{client_set_id:id.parse(value)}));
  handle('openSubscriptionManagement',()=>shell.openExternal('https://apps.apple.com/account/subscriptions'));
 }
 function windowBackground(){return nativeTheme.shouldUseDarkColors?'#17191d':'#eef2f9';}
 function updateWindowTheme(){if(window&&!window.isDestroyed()){window.setBackgroundColor(windowBackground());window.setTitleBarOverlay({color:windowBackground(),symbolColor:nativeTheme.shouldUseDarkColors?'#eef0f4':'#101f39'});}}
 function configureTheme(){nativeTheme.themeSource=store.settings().theme;updateWindowTheme();}
 function configureLogin(){if(app.isPackaged&&!process.env.OWL_TEST_DATA_DIR)app.setLoginItemSettings({openAtLogin:store.settings().launchAtLogin,path:process.env.PORTABLE_EXECUTABLE_FILE??process.execPath});}
 // Windows Shell needs a real ICO path outside app.asar for the taskbar icon.
 function windowsIconPath(){return app.isPackaged?join(process.resourcesPath,'icon.ico'):join(__dirname,'../build/icon.ico');}
 function configureTray(){if(store.settings().keepInTray&&!tray){const svg='<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><rect width="32" height="32" rx="9" fill="#5144b8"/><circle cx="11" cy="14" r="6" fill="white"/><circle cx="21" cy="14" r="6" fill="white"/><circle cx="12" cy="14" r="2"/><circle cx="20" cy="14" r="2"/><path d="m13 21 3 4 3-4" fill="#f3b65a"/></svg>';tray=new Tray(nativeImage.createFromPath(join(__dirname,'../build/icon.png')).resize({width:32,height:32}));tray.setToolTip('Owl AI');tray.setContextMenu(Menu.buildFromTemplate([{label:'Open Owl AI',click:()=>window.show()},{label:'Quit',click:()=>app.quit()}]));tray.on('double-click',()=>window.show());}else if(!store.settings().keepInTray&&tray){tray.destroy();tray=null;}}
 if(!app.requestSingleInstanceLock())app.quit();else{
  app.on('second-instance',()=>{window?.show();window?.focus();});
  app.whenReady().then(async()=>{try{
   app.setAppUserModelId('com.mavrylo.owlai.windows');workspaces=await Workspaces.open(app.getPath('userData'));store=workspaces.current;api=new Api(join(app.getPath('userData'),'account.enc'),()=>workspaces.guest.settings().apiBase);await selectAccount();
-  reviewTranslations=new ReviewTranslations(()=>({store,scope:scope(),apiBase:workspaces.guest.settings().apiBase,account:api.state()}),input=>api.reviewTranslation(input));
+  reviewTranslations=new ReviewTranslations(()=>({store,scope:scope(),apiBase:workspaces.guest.settings().apiBase,account:accountState()}),input=>api.reviewTranslation(input));
   configureTheme();
   nativeTheme.on('updated',updateWindowTheme);
   window=new BrowserWindow({width:954,height:723,center:true,minWidth:940,minHeight:680,title:'Owl AI',titleBarStyle:'hidden',titleBarOverlay:{height:36,color:windowBackground(),symbolColor:nativeTheme.shouldUseDarkColors?'#eef0f4':'#101f39'},icon:process.platform==='win32'?windowsIconPath():join(__dirname,'../build/icon.png'),backgroundColor:windowBackground(),show:false,webPreferences:{preload:join(__dirname,'preload.cjs'),nodeIntegration:false,contextIsolation:true,sandbox:true}});
   Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'Owl AI',submenu:[{role:'about'},{type:'separator'},{role:'quit'}]},{label:'Edit',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},{label:'View',submenu:[{label:'Actual Size',accelerator:'CmdOrCtrl+0',click:()=>window.webContents.setZoomLevel(-.5)},{role:'zoomIn'},{role:'zoomOut'},{role:'togglefullscreen'}]}]));
   if(process.platform==='win32')window.setAppDetails({appId:'com.mavrylo.owlai.windows',appIconPath:windowsIconPath(),appIconIndex:0,relaunchDisplayName:'Owl AI'});
   window.setMenuBarVisibility(false);
   window.webContents.setWindowOpenHandler(()=>({action:'deny'}));window.webContents.on('will-navigate',event=>event.preventDefault());window.webContents.session.setPermissionRequestHandler((_web,permission,callback)=>callback(permission==='notifications'));
   register();configureLogin();configureTray();await window.loadFile(join(__dirname,'../dist/index.html'));window.webContents.setZoomLevel(-.5);window.setSize(954,723);window.center();window.show();
   const syncVisibility=()=>{scheduler?.setBackground(!window.isVisible()||window.isMinimized());};
   window.on('focus',()=>{scheduler?.foreground();});
   window.on('show',syncVisibility);window.on('hide',syncVisibility);window.on('minimize',syncVisibility);window.on('restore',syncVisibility);
   syncVisibility();void scheduler?.tick();setInterval(()=>{if(!transitioning&&!dialogs&&!quitting)void scheduler?.tick();},1000);
diff --git a/electron/review-translations.ts b/electron/review-translations.ts
index 5ea92ca..cbcac7d 100644
--- a/electron/review-translations.ts
+++ b/electron/review-translations.ts
@@ -1,18 +1,19 @@
 import type {AccountState,ReviewTranslation,ReviewTranslationRequest} from '../shared/types';
 import {canonicalLanguage,secondaryReviewLanguage,reviewTranslationAccess} from '../shared/secondary-review';
 import type {Store} from './store';
 export interface TranslationWorkspace {store:Store;scope:string;apiBase:string;account:AccountState}
 
 function context(workspace:TranslationWorkspace,wordId:string){
+ workspace.store.assertEligible(wordId);
  const {words,decks}=workspace.store.snapshot();
  const word=words.find(row=>row.id===wordId),deck=decks.find(row=>row.id===word?.deckId);
  if(!word||!deck)throw new Error('This card no longer exists.');
  return previewContext(workspace,word.word,word.nativeLanguage??deck.nativeLanguage,word.learningLanguage??deck.learningLanguage);
 }
 
 function previewContext(workspace:TranslationWorkspace,word:string,nativeLanguage:string,learningLanguage:string){
  const native=canonicalLanguage(nativeLanguage),learning=canonicalLanguage(learningLanguage);
  const secondary=secondaryReviewLanguage(workspace.store.settings().secondaryReviewLanguage,native??'');
  if(!native||!learning)throw new Error('This card uses an unsupported language.');
  if(!secondary)throw new Error('Choose a second language in Settings first.');
  const input:ReviewTranslationRequest={word:word.trim().normalize('NFC'),native_language:native,learning_language:learning,secondary_language:secondary};
@@ -39,18 +40,19 @@ export class ReviewTranslations {
  private async load(resolve:(workspace:TranslationWorkspace)=>ReturnType<typeof context>):Promise<ReviewTranslation>{
   const workspace={...this.current()},{input,key}=resolve(workspace);
   const cached=workspace.store.cachedReviewTranslation(key);
   if(cached){try{return validated(cached,input.secondary_language);}catch{/* Replace an invalid old cache entry with a fresh response. */}}
   const access=reviewTranslationAccess(workspace.account);if(access)throw new Error(access);
   const pendingKey=JSON.stringify([workspace.scope,key]),existing=this.pending.get(pendingKey);if(existing)return existing;
   const pending=(async()=>{
    const result=validated(await this.request(input),input.secondary_language);
    const current=this.current();
    // Check identity before reading the store: account switching can close the old database.
    if(current.store!==workspace.store||current.scope!==workspace.scope||resolve(current).key!==key)
     throw new Error('The card or account changed. Please try again.');
+   const access=reviewTranslationAccess(current.account);if(access)throw new Error(access);
    workspace.store.saveReviewTranslation(key,result);
    return result;
   })().finally(()=>{this.pending.delete(pendingKey);});
   this.pending.set(pendingKey,pending);return pending;
  }
 }
diff --git a/electron/store.ts b/electron/store.ts
index aa5b671..2d3f26b 100644
--- a/electron/store.ts
+++ b/electron/store.ts
@@ -1,24 +1,43 @@
 import {partOfSpeech} from '../shared/part-of-speech';
 import initSqlJs, { type Database, type SqlValue } from 'sql.js';
 import { existsSync, readFileSync, writeFileSync, renameSync, copyFileSync, mkdirSync } from 'node:fs';
 import { dirname } from 'node:path';
 import { randomUUID } from 'node:crypto';
-import type { Deck,Draft,Word,Settings,Snapshot,ReviewCard,ReviewTranslation } from '../shared/types';
+import type { AccountState,Entitlement,Deck,Draft,Word,Settings,Snapshot,ReviewCard,ReviewTranslation } from '../shared/types';
 import { newCard,scheduleCard,studyDayStart } from './scheduler';
 import type {SyncRecord} from './sync';
 import {secondaryReviewLanguage} from '../shared/secondary-review';
+import {evaluateAccess,eligibleWordIds,type AccessOperation} from '../shared/access-policy';
 const defaults:Settings={spellingPractice:false,secondaryReviewLanguage:null,nativeLanguage:'ru',learningLanguage:'en-us',theme:'light',accent:'indigo',darkAccent:'teal',dailyGoal:5,direction:'forward',dayStart:0,retention:.9,reminders:true,reminderTime:'19:00',reminderStart:'08:00',reminderEnd:'20:00',reminderCount:10,keepInTray:true,launchAtLogin:true,apiBase:'https://api.mavrylo.com',onboardingComplete:false};
 export class Store {
- private constructor(private db:Database,private path:string){if(!db.exec('PRAGMA table_info(sync_state)')[0]?.values.some(row=>row[1]==='cursor'))db.run('ALTER TABLE sync_state ADD COLUMN cursor INTEGER');if(!db.exec('PRAGMA table_info(sync_state)')[0]?.values.some(row=>row[1]==='recovery_required'))db.run('ALTER TABLE sync_state ADD COLUMN recovery_required INTEGER NOT NULL DEFAULT 0');db.run('CREATE TABLE IF NOT EXISTS review_translations(cache_key TEXT PRIMARY KEY,data TEXT NOT NULL)');}
+ private accessProvider:(()=>AccountState)|null=null;private transactionDepth=0;
+ bindAccess(provider:()=>AccountState){this.accessProvider=provider;}
+ rememberAccess(state:AccountState){
+  if(this.owner()==='guest'||!state.profile||!state.entitlement)return;
+  this.transaction(()=>this.db.run('INSERT OR REPLACE INTO access_history VALUES(1,?)',[JSON.stringify(state.entitlement)]));
+ }
+ accessState():AccountState|undefined{
+  const state=this.accessProvider?.();if(!state||state.entitlement||!state.profile)return state;
+  const raw=this.rows<{data:string}>('SELECT data FROM access_history WHERE id=1')[0]?.data;
+  let ent:Entitlement|null=null;try{ent=raw?JSON.parse(raw):null;}catch{}
+  if(ent){ent={...ent,checked_at:undefined};if(['premium','grace'].includes(ent.status))ent.status=ent.was_ever_paid===true&&Number.isFinite(Date.parse(ent.expires_at??''))?'expired_paid':'invalid_subscription';if(ent.status==='trial')ent.status='expired_trial';}
+  return {...state,entitlement:ent};
+ }
+ eligibleIds(now=new Date(),words=this.snapshot(now).words):Set<string>{return this.accessProvider?eligibleWordIds(this.accessState(),words,now):new Set(words.map(w=>w.id));}
+ assertAccess(operation:AccessOperation,count=this.snapshot().words.length){if(!this.accessProvider)return;const access=evaluateAccess(this.accessState(),operation,count);if(!access.allow)throw new Error(access.reason!);}
+ assertEligible(id:string){if(!this.eligibleIds().has(id))throw new Error('This card is outside your current saved-content access. Open your profile to manage your subscription.');}
+ importDeck(input:Partial<Deck>&{name:string},drafts:Draft[]):Deck{return this.transaction(()=>{const deck=this.saveDeck(input);this.addWords(deck.id,drafts);return deck;});}
+
+ private constructor(private db:Database,private path:string){if(!db.exec('PRAGMA table_info(sync_state)')[0]?.values.some(row=>row[1]==='cursor'))db.run('ALTER TABLE sync_state ADD COLUMN cursor INTEGER');if(!db.exec('PRAGMA table_info(sync_state)')[0]?.values.some(row=>row[1]==='recovery_required'))db.run('ALTER TABLE sync_state ADD COLUMN recovery_required INTEGER NOT NULL DEFAULT 0');db.run('CREATE TABLE IF NOT EXISTS review_translations(cache_key TEXT PRIMARY KEY,data TEXT NOT NULL)');db.run('CREATE TABLE IF NOT EXISTS access_history(id INTEGER PRIMARY KEY CHECK(id=1),data TEXT NOT NULL)');}
  static async open(path:string,owner='guest'):Promise<Store>{
   mkdirSync(dirname(path),{recursive:true});
   const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});
   const existed=existsSync(path); const db=new SQL.Database(existed?readFileSync(path):undefined);
   if(existed){const check=db.exec('PRAGMA integrity_check');if(check[0]?.values[0]?.[0]!=='ok')throw new Error('The card database needs recovery. Restore a backup; your existing file has been preserved.');}
   const version=Number(db.exec('PRAGMA user_version')[0]?.values[0]?.[0]??0);
   if(version>1)throw new Error('This database belongs to a newer Owl AI. Update the app before opening it.');
   if(version<1){if(existed)copyFileSync(path,path+'.pre-migration.bak');db.run(`BEGIN; CREATE TABLE IF NOT EXISTS decks(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS words(id TEXT PRIMARY KEY,deck_id TEXT NOT NULL REFERENCES decks(id) ON DELETE CASCADE,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS reviews(attempt TEXT PRIMARY KEY,word_id TEXT NOT NULL,direction TEXT NOT NULL,at TEXT NOT NULL,result TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1),data TEXT NOT NULL); PRAGMA user_version=1; COMMIT;`);}
   db.run('PRAGMA foreign_keys=ON');
   db.run('CREATE TABLE IF NOT EXISTS sync_state(id INTEGER PRIMARY KEY CHECK(id=1),owner TEXT NOT NULL,baseline TEXT NOT NULL)');
   const store=new Store(db,path),saved=store.rows<{owner:string}>('SELECT owner FROM sync_state WHERE id=1')[0];
   if(saved&&saved.owner!==owner){db.close();throw new Error('This database belongs to a different account.');}
@@ -30,75 +49,79 @@ export class Store {
  private migrateReminderDefaults(){
   const data=this.rows<{data:string}>('SELECT data FROM settings WHERE id=1')[0]?.data;
   const saved=data?JSON.parse(data):{};
   if(saved.startupDefaultsVersion>=1)return;
   this.db.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify({...saved,reminders:true,keepInTray:true,launchAtLogin:true,startupDefaultsVersion:1})]);
  }
  private activateOnlyDeck(){
   const rows=this.rows<{id:string,data:string}>('SELECT id,data FROM decks LIMIT 2');
   if(rows.length!==1)return;
   const deck=JSON.parse(rows[0].data) as Deck;
   if(!deck.active)this.db.run('UPDATE decks SET data=? WHERE id=?',[JSON.stringify({...deck,active:true}),rows[0].id]);
  }
- private transaction<T>(fn:()=>T):T {this.db.run('BEGIN');let result:T;try{result=fn();this.db.run('COMMIT');}catch(e){this.db.run('ROLLBACK');throw e;}this.persist();return result;}
+ private transaction<T>(fn:()=>T):T {if(this.transactionDepth)return fn();this.db.run('BEGIN');this.transactionDepth++;let result:T;try{result=fn();this.db.run('COMMIT');}catch(e){this.db.run('ROLLBACK');throw e;}finally{this.transactionDepth--;}this.persist();return result;}
  settings():Settings {const data=this.rows<{data:string}>('SELECT data FROM settings WHERE id=1')[0]?.data;const settings={...defaults,...(data?JSON.parse(data):{}),dayStart:0};return {...settings,spellingPractice:settings.spellingPractice===true,secondaryReviewLanguage:secondaryReviewLanguage(settings.secondaryReviewLanguage,settings.nativeLanguage)};}
  saveSettings(patch:Partial<Settings>):Settings {const settings={...this.settings(),...patch,dayStart:0};settings.spellingPractice=settings.spellingPractice===true;settings.secondaryReviewLanguage=secondaryReviewLanguage(settings.secondaryReviewLanguage,settings.nativeLanguage);if(settings.reminderStart===settings.reminderEnd)throw new Error('Choose different start and end reminder times.');this.transaction(()=>this.db.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify(settings)]));return settings;}
  cachedReviewTranslation(key:string):unknown {const data=this.rows<{data:string}>('SELECT data FROM review_translations WHERE cache_key=?',[key])[0]?.data;try{return data?JSON.parse(data):null;}catch{return null;}}
  saveReviewTranslation(key:string,value:ReviewTranslation){this.transaction(()=>this.db.run('INSERT OR REPLACE INTO review_translations VALUES(?,?)',[key,JSON.stringify(value)]));}
  snapshot(now=new Date()):Snapshot {
   const settings=this.settings(), start=studyDayStart(now,settings.dayStart).toISOString();
   const decks=this.rows<{data:string}>('SELECT data FROM decks ORDER BY rowid DESC').map(x=>JSON.parse(x.data) as Deck);
   const words=this.rows<{data:string}>('SELECT data FROM words ORDER BY rowid DESC').map(x=>JSON.parse(x.data) as Word);
   const logs=this.rows<{at:string}>('SELECT at FROM reviews ORDER BY at DESC');
   const activityMap=new Map<string,number>();for(const log of logs){const day=studyDayStart(new Date(log.at),settings.dayStart).toLocaleDateString('en-CA');activityMap.set(day,(activityMap.get(day)??0)+1);}
   let streak=0;const cursor=studyDayStart(now,settings.dayStart);if(!activityMap.has(cursor.toLocaleDateString('en-CA')))cursor.setDate(cursor.getDate()-1);
   while(activityMap.has(cursor.toLocaleDateString('en-CA'))){streak++;cursor.setDate(cursor.getDate()-1);}
   return {workspaceId:this.owner(),scopeRevision:this.owner(),decks,words,settings,reviewedToday:logs.filter(x=>x.at>=start).length,streak,activity:[...activityMap].map(([date,count])=>({date,count})).slice(0,100)};
  }
  saveDeck(input:Partial<Deck>&{name:string}):Deck {
   input=Object.fromEntries(Object.entries(input).filter(([,value])=>value!==undefined)) as typeof input;
   const decks=this.snapshot().decks,existing=input.id?decks.find(x=>x.id===input.id):undefined;
   if(input.id&&!existing)throw new Error('This set no longer exists.');
+  if(!existing||Object.entries(input).some(([key,value])=>key!=='active'&&value!==existing[key as keyof Deck])){this.assertAccess('edit');if(existing){const eligible=this.eligibleIds();if(this.snapshot().words.some(w=>w.deckId===existing.id&&!eligible.has(w.id)))throw new Error('This set contains cards outside your current content-edit access. Saved cards are preserved.');}}
   const settings=this.settings();const deck:Deck={id:randomUUID(),description:'',active:true,nativeLanguage:settings.nativeLanguage,learningLanguage:settings.learningLanguage,createdAt:new Date().toISOString(),...existing,...input,name:input.name.trim()};
   if(!deck.name)throw new Error('Give your set a name.');
   if(decks.length===0||(existing&&decks.length===1))deck.active=true;
   this.transaction(()=>{
    if(existing)for(const word of this.snapshot().words.filter(w=>w.deckId===deck.id)){
     if((word.nativeLanguage??existing.nativeLanguage)!==(word.nativeLanguage??deck.nativeLanguage)||(word.learningLanguage??existing.learningLanguage)!==(word.learningLanguage??deck.learningLanguage))this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify({...word,partOfSpeech:undefined}),word.id]);
    }
    this.db.run('INSERT INTO decks VALUES(?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data',[deck.id,JSON.stringify(deck)]);
   });return deck;
  }
  deleteDeck(id:string){this.transaction(()=>{this.db.run('DELETE FROM reviews WHERE word_id IN (SELECT id FROM words WHERE deck_id=?)',[id]);this.db.run('DELETE FROM words WHERE deck_id=?',[id]);this.db.run('DELETE FROM decks WHERE id=?',[id]);this.activateOnlyDeck();});}
  addWords(deckId:string,drafts:Draft[]):number {
   if(!this.snapshot().decks.some(x=>x.id===deckId))throw new Error('Choose an existing set.');
+  let total=this.snapshot().words.length;
   const existing=new Set(this.snapshot().words.filter(x=>x.deckId===deckId).map(x=>x.word.normalize('NFKC').toLowerCase()));
-  let added=0;this.transaction(()=>{for(const draft of drafts){const word=draft.word.trim(),translation=draft.translation.trim(),key=word.normalize('NFKC').toLowerCase();if(!word||!translation)throw new Error('Every selected card needs a word and translation.');if(existing.has(key))continue;const record:Word={...draft,word,translation,id:randomUUID(),deckId,createdAt:new Date().toISOString(),card:newCard(),reverse:newCard()};this.db.run('INSERT INTO words VALUES(?,?,?)',[record.id,deckId,JSON.stringify(record)]);existing.add(key);added++;}});return added;
+  let added=0;this.transaction(()=>{for(const draft of drafts){const word=draft.word.trim(),translation=draft.translation.trim(),key=word.normalize('NFKC').toLowerCase();if(!word||!translation)throw new Error('Every selected card needs a word and translation.');if(existing.has(key))continue;this.assertAccess('add',total++);const record:Word={...draft,word,translation,id:randomUUID(),deckId,createdAt:new Date().toISOString(),card:newCard(),reverse:newCard()};this.db.run('INSERT INTO words VALUES(?,?,?)',[record.id,deckId,JSON.stringify(record)]);existing.add(key);added++;}});return added;
  }
  private word(id:string):Word {const data=this.rows<{data:string}>('SELECT data FROM words WHERE id=?',[id])[0]?.data;if(!data)throw new Error('This card no longer exists.');return JSON.parse(data);}
- editWord(id:string,draft:Draft){const word=this.word(id);if(!draft.word.trim()||!draft.translation.trim())throw new Error('Enter both a word and translation.');this.transaction(()=>this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify({...word,...draft,partOfSpeech:word.word.trim().toLowerCase()===draft.word.trim().toLowerCase()?(draft.partOfSpeech??word.partOfSpeech):undefined,word:draft.word.trim(),translation:draft.translation.trim()}),id]));}
+ editWord(id:string,draft:Draft){this.assertEligible(id);this.assertAccess('edit');const word=this.word(id);if(!draft.word.trim()||!draft.translation.trim())throw new Error('Enter both a word and translation.');this.transaction(()=>this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify({...word,...draft,partOfSpeech:word.word.trim().toLowerCase()===draft.word.trim().toLowerCase()?(draft.partOfSpeech??word.partOfSpeech):undefined,word:draft.word.trim(),translation:draft.translation.trim()}),id]));}
  deleteWord(id:string){this.transaction(()=>{this.db.run('DELETE FROM reviews WHERE word_id=?',[id]);this.db.run('DELETE FROM words WHERE id=?',[id]);});}
  queue(deckId?:string,now=new Date(),testMode=false):Word[]{
   const {words,decks,settings}=this.snapshot(now);const start=studyDayStart(now,settings.dayStart);const end=new Date(start);end.setDate(end.getDate()+1);
   const introduced=new Set(this.rows<{word_id:string,result:string}>('SELECT word_id,result FROM reviews WHERE direction=? AND at>=?',[settings.direction,start.toISOString()]).filter(x=>JSON.parse(x.result).firstIntroduction).map(x=>x.word_id));
   // Only the trusted main process supplies the live server flag; never persist this override.
   let remaining=testMode?words.length:Math.max(0,settings.dailyGoal-introduced.size);const active=new Set(decks.filter(x=>x.active&&(!deckId||x.id===deckId)).map(x=>x.id));
   const card=(w:Word)=>settings.direction==='forward'?w.card:w.reverse;
-  const candidates=words.filter(x=>{const d=decks.find(d=>d.id===x.deckId);return active.has(x.deckId)&&x.translation.trim().length>0&&(x.nativeLanguage??d?.nativeLanguage)===settings.nativeLanguage&&(x.learningLanguage??d?.learningLanguage)===settings.learningLanguage;}).sort((a,b)=>new Date(card(a).due).getTime()-new Date(card(b).due).getTime());
+  const eligible=this.eligibleIds(now,words);
+  const candidates=words.filter(x=>eligible.has(x.id)).filter(x=>{const d=decks.find(d=>d.id===x.deckId);return active.has(x.deckId)&&x.translation.trim().length>0&&(x.nativeLanguage??d?.nativeLanguage)===settings.nativeLanguage&&(x.learningLanguage??d?.learningLanguage)===settings.learningLanguage;}).sort((a,b)=>new Date(card(a).due).getTime()-new Date(card(b).due).getTime());
   const due=candidates.filter(w=>{const c=card(w);return c.state!==0&&new Date(c.due)<(c.state===2?end:now);});
   const fresh=candidates.filter(w=>card(w).state===0).reverse().filter(()=>remaining-->0);
   return [...due,...fresh];
  }
- previews(id:string):Record<number,string>{const word=this.word(id),settings=this.settings(),card=settings.direction==='forward'?word.card:word.reverse,now=new Date();return Object.fromEntries([1,2,3,4].map(g=>[g,scheduleCard(card,g,now,settings.retention).due]));}
+ previews(id:string):Record<number,string>{this.assertEligible(id);const word=this.word(id),settings=this.settings(),card=settings.direction==='forward'?word.card:word.reverse,now=new Date();return Object.fromEntries([1,2,3,4].map(g=>[g,scheduleCard(card,g,now,settings.retention).due]));}
  review(id:string,grade:number,attempt:string,testMode=false):Word {
+  this.assertEligible(id);
   const stored=this.rows<{result:string,word_id:string}>('SELECT result,word_id FROM reviews WHERE attempt=?',[attempt])[0];if(stored){if(stored.word_id!==id)throw new Error('Review attempt already belongs to another card.');return JSON.parse(stored.result).word;}
   const word=this.word(id),settings=this.settings(),key=settings.direction==='forward'?'card':'reverse',now=new Date();
   if(!this.queue(undefined,now,testMode).some(x=>x.id===id))throw new Error('This card is not currently due for review.');
   const previous=word[key];word[key]=scheduleCard(previous,grade,now,settings.retention);
   this.transaction(()=>{this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify(word),id]);this.db.run('INSERT INTO reviews VALUES(?,?,?,?,?)',[attempt,id,settings.direction,now.toISOString(),JSON.stringify({word,firstIntroduction:previous.reps===0})]);});return word;
  }
  backup(destination:string){if(destination.toLowerCase()===this.path.toLowerCase())throw new Error('Choose a different location for the backup.');this.persist();copyFileSync(this.path,destination);}
  preserveBeforeCloudReset(){this.backup(this.path+'.before-cloud-'+Date.now()+'.sqlite');}
  owner():string {return this.rows<{owner:string}>('SELECT owner FROM sync_state WHERE id=1')[0]?.owner??'guest';}
  syncBaseline():SyncRecord[]{return JSON.parse(this.rows<{baseline:string}>('SELECT baseline FROM sync_state WHERE id=1')[0]?.baseline??'[]');}
  syncCursor():number|null{return this.rows<{cursor:number|null}>('SELECT cursor FROM sync_state WHERE id=1')[0]?.cursor??null;}
  syncRecoveryRequired():boolean{return this.rows<{recovery_required:number}>('SELECT recovery_required FROM sync_state WHERE id=1')[0]?.recovery_required===1;}
@@ -110,16 +133,16 @@ export class Store {
    for(const d of decks)this.db.run('INSERT INTO decks VALUES(?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data',[d.id,JSON.stringify(d)]);
    for(let w of words){const previous=previousWords.get(w.id);if(!partOfSpeech(w.partOfSpeech)&&previous&&previous.word.trim().toLowerCase()===w.word.trim().toLowerCase()&&(previous.nativeLanguage??previousDecks.get(previous.deckId)?.nativeLanguage)===(w.nativeLanguage??incomingDecks.get(w.deckId)?.nativeLanguage)&&(previous.learningLanguage??previousDecks.get(previous.deckId)?.learningLanguage)===(w.learningLanguage??incomingDecks.get(w.deckId)?.learningLanguage))w={...w,partOfSpeech:previous.partOfSpeech};this.db.run('INSERT INTO words VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET deck_id=excluded.deck_id,data=excluded.data',[w.id,w.deckId,JSON.stringify(w)]);}
    // Recovery changes identity, not learning history; move reviews atomically.
    for(const [oldId,newId] of recoveredIds)if(wordIds.has(newId)&&!wordIds.has(oldId))this.db.run('UPDATE reviews SET word_id=? WHERE word_id=?',[newId,oldId]);
    for(const w of this.rows<{id:string}>('SELECT id FROM words'))if(!wordIds.has(w.id)){this.db.run('DELETE FROM reviews WHERE word_id=?',[w.id]);this.db.run('DELETE FROM words WHERE id=?',[w.id]);}
    for(const d of this.rows<{id:string}>('SELECT id FROM decks'))if(!deckIds.has(d.id))this.db.run('DELETE FROM decks WHERE id=?',[d.id]);
    this.db.run('UPDATE sync_state SET baseline=?,cursor=?,recovery_required=0 WHERE id=1',[JSON.stringify(baseline),cursor]);this.activateOnlyDeck();
   });
  }
  async restore(source:string){
 
   const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});const candidate=new SQL.Database(readFileSync(source));
-  try{if(candidate.exec('PRAGMA integrity_check')[0]?.values[0]?.[0]!=='ok'||candidate.exec('PRAGMA user_version')[0]?.values[0]?.[0]!==1)throw new Error('Choose a valid Owl AI backup from this app version.');for(const table of ['decks','words','reviews','settings'])candidate.exec(`SELECT * FROM ${table} LIMIT 1`);const tables=candidate.exec("SELECT name FROM sqlite_master WHERE type='table' AND name='sync_state'");const backupOwner=tables.length?candidate.exec('SELECT owner FROM sync_state WHERE id=1')[0]?.values[0]?.[0]:'guest';if(backupOwner!==this.owner())throw new Error('This backup belongs to a different account or local workspace. Switch to its original workspace before restoring.');candidate.run("CREATE TABLE IF NOT EXISTS sync_state(id INTEGER PRIMARY KEY CHECK(id=1),owner TEXT NOT NULL,baseline TEXT NOT NULL)");candidate.run("INSERT OR IGNORE INTO sync_state(id,owner,baseline) VALUES(1,'guest','[]')");const check=new Store(candidate,source);check.snapshot();check.activateOnlyDeck();const current=this.settings();const restored={...check.settings(),apiBase:current.apiBase,launchAtLogin:current.launchAtLogin,startupDefaultsVersion:1};candidate.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify(restored)]);copyFileSync(this.path,this.path+'.before-restore.bak');const tmp=this.path+'.restore.tmp';writeFileSync(tmp,Buffer.from(candidate.export()));renameSync(tmp,this.path);this.db.close();this.db=new SQL.Database(candidate.export());this.db.run('PRAGMA foreign_keys=ON');}finally{candidate.close();}
+  try{if(candidate.exec('PRAGMA integrity_check')[0]?.values[0]?.[0]!=='ok'||candidate.exec('PRAGMA user_version')[0]?.values[0]?.[0]!==1)throw new Error('Choose a valid Owl AI backup from this app version.');for(const table of ['decks','words','reviews','settings'])candidate.exec(`SELECT * FROM ${table} LIMIT 1`);const tables=candidate.exec("SELECT name FROM sqlite_master WHERE type='table' AND name='sync_state'");const backupOwner=tables.length?candidate.exec('SELECT owner FROM sync_state WHERE id=1')[0]?.values[0]?.[0]:'guest';if(backupOwner!==this.owner())throw new Error('This backup belongs to a different account or local workspace. Switch to its original workspace before restoring.');candidate.run("CREATE TABLE IF NOT EXISTS sync_state(id INTEGER PRIMARY KEY CHECK(id=1),owner TEXT NOT NULL,baseline TEXT NOT NULL)");candidate.run("INSERT OR IGNORE INTO sync_state(id,owner,baseline) VALUES(1,'guest','[]')");const retained=this.rows<{data:string}>('SELECT data FROM access_history WHERE id=1')[0]?.data;const check=new Store(candidate,source);if(retained)candidate.run('INSERT OR REPLACE INTO access_history VALUES(1,?)',[retained]);else candidate.run('DELETE FROM access_history');check.snapshot();check.activateOnlyDeck();const current=this.settings();const restored={...check.settings(),apiBase:current.apiBase,launchAtLogin:current.launchAtLogin,startupDefaultsVersion:1};candidate.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify(restored)]);copyFileSync(this.path,this.path+'.before-restore.bak');const tmp=this.path+'.restore.tmp';writeFileSync(tmp,Buffer.from(candidate.export()));renameSync(tmp,this.path);this.db.close();this.db=new SQL.Database(candidate.export());this.db.run('PRAGMA foreign_keys=ON');}finally{candidate.close();}
  }
  close(){this.persist();this.db.close();}
 }
diff --git a/scripts/import-smoke.mjs b/scripts/import-smoke.mjs
index 5c40eb3..15cf028 100644
--- a/scripts/import-smoke.mjs
+++ b/scripts/import-smoke.mjs
@@ -1,13 +1,13 @@
 import { _electron as electron } from 'playwright';
 import { createCanvas } from '@napi-rs/canvas';
 import { mkdirSync,writeFileSync } from 'node:fs';
 import { resolve } from 'node:path';
 const dir=resolve('test-results/imports');mkdirSync(dir,{recursive:true});
 const canvas=createCanvas(1000,300),ctx=canvas.getContext('2d');ctx.fillStyle='white';ctx.fillRect(0,0,1000,300);ctx.fillStyle='black';ctx.font='54px Arial';ctx.fillText('hello - privet',60,100);ctx.fillText('world - mir',60,200);writeFileSync(resolve(dir,'words.png'),canvas.toBuffer('image/png'));
 writeFileSync(resolve(dir,'words.csv'),'word,translation\n"hello","привет"\nworld,мир');
 function pdf(objects,path){let bytes=Buffer.from('%PDF-1.4\n'),offsets=[0];for(let i=0;i<objects.length;i++){offsets.push(bytes.length);bytes=Buffer.concat([bytes,Buffer.from(`${i+1} 0 obj\n`),objects[i],Buffer.from('\nendobj\n')]);}const xref=bytes.length;bytes=Buffer.concat([bytes,Buffer.from(`xref\n0 ${objects.length+1}\n0000000000 65535 f \n`+offsets.slice(1).map(n=>String(n).padStart(10,'0')+' 00000 n \n').join('')+`trailer\n<< /Size ${objects.length+1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`)]);writeFileSync(path,bytes);}
 const b=x=>Buffer.from(x),stream=x=>Buffer.concat([b(`<< /Length ${x.length} >>\nstream\n`),x,b('\nendstream')]);
 pdf([b('<< /Type /Catalog /Pages 2 0 R >>'),b('<< /Type /Pages /Kids [3 0 R] /Count 1 >>'),b('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 400] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>'),b('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'),stream(b('BT /F1 24 Tf 50 300 Td (hello - privet) Tj 0 -40 Td (world - mir) Tj ET'))],resolve(dir,'text.pdf'));
 const jpeg=canvas.toBuffer('image/jpeg');pdf([b('<< /Type /Catalog /Pages 2 0 R >>'),b('<< /Type /Pages /Kids [3 0 R] /Count 1 >>'),b('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 150] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>'),Buffer.concat([b(`<< /Type /XObject /Subtype /Image /Width 1000 /Height 300 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`),jpeg,b('\nendstream')]),stream(b('q 500 0 0 150 0 0 cm /Im1 Do Q'))],resolve(dir,'scan.pdf'));
 const data=resolve('test-results/import-profile');mkdirSync(data,{recursive:true});const app=await electron.launch({executablePath:process.env.OWL_TEST_EXECUTABLE,args:process.env.OWL_TEST_EXECUTABLE?[]:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
-try{const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);for(const filename of ['words.csv','text.pdf','words.png','scan.pdf']){await app.evaluate(({dialog},path)=>{dialog.showOpenDialog=async()=>({canceled:false,filePaths:[path]});},resolve(dir,filename));const result=await page.evaluate(()=>window.owl.importFile('auto'));if(!result.drafts.some(x=>x.word.toLowerCase().includes('hello')))throw new Error(`No expected word from ${filename}: ${JSON.stringify(result)}`);console.log(`PASS ${filename}: ${result.drafts.length} drafts`);}}finally{await app.close();}
+try{const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);for(const filename of ['words.csv','text.pdf','words.png','scan.pdf']){await app.evaluate(({dialog},path)=>{dialog.showOpenDialog=async()=>({canceled:false,filePaths:[path]});},resolve(dir,filename));const result=await page.evaluate(async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);return window.owl.forWorkspace(snapshot.scopeRevision).importFile('auto');});if(!result.drafts.some(x=>x.word.toLowerCase().includes('hello')))throw new Error(`No expected word from ${filename}: ${JSON.stringify(result)}`);console.log(`PASS ${filename}: ${result.drafts.length} drafts`);}}finally{await app.close();}
diff --git a/scripts/paid-access-smoke.ts b/scripts/paid-access-smoke.ts
new file mode 100644
index 0000000..f788c5d
--- /dev/null
+++ b/scripts/paid-access-smoke.ts
@@ -0,0 +1,44 @@
+import assert from 'node:assert/strict';
+import {createServer} from 'node:http';
+import {mkdirSync} from 'node:fs';
+import {resolve,join} from 'node:path';
+import {_electron as electron} from 'playwright';
+import {Store} from '../electron/store';
+async function main(){
+ const root=resolve(process.env.OWL_ACCESS_RESULTS??'test-results/paid-access'),profile=join(root,'profile-'+Date.now());mkdirSync(profile,{recursive:true});
+ let status='free',aiCalls=0;const records=new Map<string,any>();
+ const server=createServer(async(req,res)=>{
+  res.setHeader('Content-Type','application/json');
+  if(req.url==='/owlai/config/feature-flags'){res.end(JSON.stringify({test_mode:false}));return;}
+  if(req.url==='/owlai/account/desktop/email/session'){res.end(JSON.stringify({access_token:'local-fake',refresh_token:'local-fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'local-a',email:'local@example.test'}}));return;}
+  if(req.url==='/owlai/account/entitlement'){res.end(JSON.stringify({status,expires_at:new Date(Date.now()+(status==='premium'?86400000:-1000)).toISOString(),is_trial:false,auto_renew:false,was_ever_paid:status==='premium'||status==='expired_paid'}));return;}
+  if(req.url?.startsWith('/owlai/account/sync')){let text='';for await(const chunk of req)text+=chunk;for(const row of (text?JSON.parse(text).changes:[])??[])records.set(row.kind+':'+row.id,{...row,version:row.base_version+1});res.end(JSON.stringify({owner_id:'local-a',records:[...records.values()],conflicts:[],cursor:1,is_snapshot:false}));return;}
+  if(req.url?.startsWith('/owlai/account/ai/')){aiCalls++;res.end(JSON.stringify({language_code:'es',translation:'viaje',explanation:'Un desplazamiento.'}));return;}
+  res.statusCode=404;res.end('{}');
+ });
+ await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));const address=server.address();assert.ok(address&&typeof address!=='string');const origin=`http://127.0.0.1:${address.port}`;
+ const guest=await Store.open(join(profile,'owl.sqlite'));guest.saveSettings({apiBase:origin,onboardingComplete:true,keepInTray:false,launchAtLogin:false,reminders:false,dailyGoal:200});guest.close();
+ let app:Awaited<ReturnType<typeof electron.launch>>|undefined;
+ const launch=async()=>{app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:profile},timeout:30000});const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);return page;};
+ try{
+  let page=await launch();
+  const call=async(name:string,args:unknown[]=[])=>page.evaluate(async({name,args})=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);return (window.owl.forWorkspace(snapshot.scopeRevision) as any)[name](...args);},{name,args});
+  await assert.rejects(call('saveDeck',[{name:'renderer-forgery',testMode:true,entitlement:{status:'premium'}}]),/Sign in/);
+  await call('login',['local@example.test','fixture-only']);await call('refreshEntitlement');
+  const deck=await call('saveDeck',[{name:'manual'}]);await call('addWords',[deck.id,Array.from({length:9},(_,i)=>({word:'word'+i,translation:'value'}))]);
+  await assert.rejects(call('addWords',[deck.id,[{word:'ten',translation:'value'},{word:'eleven',translation:'value'}]]),/10/);assert.equal((await call('snapshot')).words.length,9);
+  await call('addWords',[deck.id,[{word:'ten',translation:'value'}]]);await call('saveSettings',[{learningLanguage:'es'}]);const other=await call('saveDeck',[{name:'another language',learningLanguage:'es'}]);
+  await assert.rejects(call('addWords',[other.id,[{word:'bypass',translation:'value',testMode:true}]]),/10/);
+  const before=await call('snapshot');await assert.rejects(call('importCatalog',[{id:'catalog',title:'catalog',cards:[{word:'catalog',translations:['value'],examples:[],native_language:'ru',learning_language:'en-us'}]}]),/10/);assert.equal((await call('snapshot')).decks.length,before.decks.length);
+  status='premium';await call('refreshEntitlement');await call('saveSettings',[{learningLanguage:'en-us',secondaryReviewLanguage:'es'}]);await call('addWords',[deck.id,Array.from({length:91},(_,i)=>({word:'paid'+i,translation:'value'}))]);const paid=await call('snapshot');assert.equal(paid.words.length,101);assert.equal(paid.accessibleWordIds.length,101);
+  const queue=await call('queue'),cachedId=queue[0].id;await call('reviewTranslation',[cachedId]);assert.equal(aiCalls,1);
+  status='expired_paid';await call('refreshEntitlement');assert.equal((await call('snapshot')).accessibleWordIds.length,101);assert.equal((await call('reviewTranslation',[cachedId])).translation,'viaje');
+  await assert.rejects(call('addWords',[deck.id,[{word:'staged',translation:'value'}]]));await assert.rejects(call('editWord',[cachedId,{word:'changed',translation:'value'}]));await assert.rejects(call('translate',['new','ru','en-us']));assert.equal(aiCalls,1);
+  await call('review',[cachedId,3,'paid-review']);await app!.evaluate(({dialog},path)=>{dialog.showSaveDialog=async()=>({canceled:false,filePath:path});},join(root,'export.csv'));assert.equal(await call('exportDeck',[deck.id]),true);
+  await app!.evaluate(({dialog},path)=>{dialog.showSaveDialog=async()=>({canceled:false,filePath:path});},join(root,'backup.sqlite'));assert.equal(await call('backup'),true);
+  await call('deleteWord',[paid.words.find((w:any)=>w.id!==cachedId).id]);await app!.close();app=undefined;await new Promise<void>(resolve=>server.close(()=>resolve()));
+  page=await launch();const offline=await call('snapshot');assert.equal(offline.words.length,100);assert.equal(offline.accessibleWordIds.length,100);assert.equal((await call('reviewTranslation',[cachedId])).translation,'viaje');await assert.rejects(call('addWords',[deck.id,[{word:'offline',translation:'value'}]]));
+  await page.screenshot({path:join(root,'offline-paid.png'),fullPage:true});console.log('PASS: actual Electron scoped IPC, renderer forgery denial, atomic manual/staged/catalog save guards, global10 across languages, paid101 preserved, expired paid cached read/review/delete/export/backup, and offline relaunch100 without mutation; only one fake AI call.');
+ }finally{await app?.close();if(server.listening)await new Promise<void>(resolve=>server.close(()=>resolve()));}
+}
+main().catch(error=>{console.error(error);process.exitCode=1;});
diff --git a/scripts/secondary-review-ui.mjs b/scripts/secondary-review-ui.mjs
index 0addaac..514eba7 100644
--- a/scripts/secondary-review-ui.mjs
+++ b/scripts/secondary-review-ui.mjs
@@ -2,25 +2,25 @@ import assert from 'node:assert/strict';
 import {mkdirSync,existsSync} from 'node:fs';
 import {chromium} from 'playwright';
 import {createServer} from 'vite';
 
 // Real React components and CSS with a deterministic IPC boundary. Store/cache tests run separately.
 const server=await createServer({server:{host:'127.0.0.1',port:0},logLevel:'error'});await server.listen();
 const executablePath=process.env.OWL_TEST_BROWSER??(existsSync('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome')?'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome':undefined);
 let browser;
 try{
  browser=await chromium.launch({executablePath,headless:true});
  const page=await browser.newPage({viewport:{width:1120,height:900}});page.setDefaultTimeout(6000);
  const errors=[];page.on('pageerror',error=>errors.push(String(error)));
- await page.goto(server.resolvedUrls.local[0]+'tests/fixtures/secondary-review.html');
+ await page.goto(server.resolvedUrls.local[0]+'tests/fixtures/secondary-review.html',{waitUntil:'domcontentloaded',timeout:30000});
  const toggle=page.getByRole('checkbox',{name:'Second language in review',exact:true});
  assert.equal(await toggle.isChecked(),false);
  await toggle.click();await page.getByRole('heading',{name:'Choose a second language',exact:true}).waitFor();
  assert.equal(await page.getByRole('button',{name:'Russian',exact:true}).count(),0);
  await page.waitForFunction(()=>Array.from(document.querySelectorAll('.secondary-language-list img')).every(image=>image.complete&&image.naturalWidth>0));
  mkdirSync('test-results/secondary-review',{recursive:true});await page.screenshot({path:'test-results/secondary-review/picker.png',fullPage:true});
  await page.keyboard.press('Escape');assert.equal(await toggle.isChecked(),false,'Cancelling must keep secondary language disabled');
  await toggle.click();await page.getByRole('button',{name:'Spanish',exact:true}).click();assert.equal(await toggle.isChecked(),true);
  await page.getByRole('button',{name:'Save preferences',exact:true}).click();
  assert.equal(await page.evaluate(()=>window.secondaryFixture.snapshot().settings.secondaryReviewLanguage),'es');
  mkdirSync('test-results/secondary-review',{recursive:true});
  await page.screenshot({path:'test-results/secondary-review/settings.png',fullPage:true});
diff --git a/shared/access-policy.ts b/shared/access-policy.ts
new file mode 100644
index 0000000..facf7c2
--- /dev/null
+++ b/shared/access-policy.ts
@@ -0,0 +1,31 @@
+import type {AccountState} from './types';
+export const ACCESS_TTL_MS=5*60*1000;
+export type AccessOperation='add'|'edit'|'ai'|'read'|'review'|'delete'|'export';
+export function activeAccess(state:AccountState|undefined,now=new Date()):boolean {
+ const ent=state?.entitlement;if(!state?.profile)return false;
+ if(state.testMode)return true;
+ if(!ent||!['premium','trial','grace'].includes(ent.status))return false;
+ const expiry=Date.parse(ent.expires_at??''),checked=Date.parse(ent.checked_at??'');
+ return Number.isFinite(expiry)&&expiry>now.getTime()&&Number.isFinite(checked)&&checked<=now.getTime()&&now.getTime()-checked<ACCESS_TTL_MS;
+}
+export function retainedPaidAccess(state:AccountState|undefined):boolean {
+ const ent=state?.entitlement;
+ return !!state?.profile&&ent?.was_ever_paid===true&&(['premium','grace','expired_paid'].includes(ent.status))&&(!['premium','grace'].includes(ent.status)||Number.isFinite(Date.parse(ent.expires_at??'')));
+}
+export function evaluateAccess(state:AccountState|undefined,operation:AccessOperation,count:number,now=new Date()):{allow:boolean;reason:string|null}{
+ const allow=()=>({allow:true,reason:null});const deny=(reason:string)=>({allow:false,reason});
+ if(['delete','export','read','review'].includes(operation))return allow();
+ if(!state?.profile)return deny('Sign in to your Owl AI account to continue on desktop.');
+ if(activeAccess(state,now))return allow();
+ if(operation==='ai')return deny('An active shared Premium subscription is needed for AI translations. Open your profile to manage access.');
+ const status=state.entitlement?.status??'free';
+ if(!['free','expired_trial','revoked','premium','trial','grace','expired_paid'].includes(status))return deny('Subscription access needs verification. Contact support to recover access.');
+ if(status!=='free')return deny('Refresh or renew your subscription before adding or editing content. Saved cards remain available.');
+ if(operation==='add'&&count>=10)return deny('Free access includes 10 saved cards across all sets and languages. Open your profile to manage your subscription.');
+ return allow();
+}
+export function eligibleWordIds(state:AccountState|undefined,words:readonly {id:string;createdAt:string}[],now=new Date()):Set<string>{
+ const timestamp=(word:{createdAt:string})=>{const at=Date.parse(word.createdAt);return Number.isFinite(at)?at:Infinity;};
+ const ordered=[...words].sort((a,b)=>timestamp(a)-timestamp(b)||(a.id<b.id?-1:a.id>b.id?1:0));
+ return new Set((activeAccess(state,now)||retainedPaidAccess(state)?ordered:ordered.slice(0,10)).map(w=>w.id));
+}
diff --git a/shared/secondary-review.ts b/shared/secondary-review.ts
index c8cfe9b..ec13260 100644
--- a/shared/secondary-review.ts
+++ b/shared/secondary-review.ts
@@ -1,20 +1,21 @@
+import {evaluateAccess} from './access-policy';
 import {languages,type AccountState} from './types';
 
 export function canonicalLanguage(value:unknown):string|null {
  if(typeof value!=='string')return null;
  const code=value.trim().toLowerCase().replaceAll('_','-');
  if(['en','en-us','en-gb'].includes(code))return 'en-us';
  if(['zh','zh-cn','zh-hans'].includes(code))return 'zh';
  return languages.some(([supported])=>supported===code)?code:null;
 }
 
 export function secondaryReviewLanguage(value:unknown,nativeLanguage:string):string|null {
  const code=canonicalLanguage(value);
  return code&&code!==canonicalLanguage(nativeLanguage)?code:null;
 }
 
 export function reviewTranslationAccess(account:AccountState|undefined):string|null {
  if(!account?.profile)return 'Sign in to see a second-language translation.';
- if(!account.testMode&&!['premium','trial','grace'].includes(account.entitlement?.status??''))return 'An active shared Premium subscription is needed for AI translations.';
+ const access=evaluateAccess(account,'ai',0);if(!access.allow)return access.reason;
  return null;
 }
diff --git a/shared/types.ts b/shared/types.ts
index f047edf..1ba5901 100644
--- a/shared/types.ts
+++ b/shared/types.ts
@@ -1,20 +1,20 @@
 export interface ReviewTranslation {language_code:string;translation:string;explanation:string}
 export interface ReviewTranslationRequest {word:string;native_language:string;learning_language:string;secondary_language:string}
 export interface Draft { word:string; translation:string; partOfSpeech?:string; pronunciation?:string; examples?:string[]; notes?:string }
 export interface Deck { id:string; name:string; description:string; nativeLanguage:string; learningLanguage:string; active:boolean; createdAt:string }
 export interface ReviewCard { due:string; stability:number; difficulty:number; elapsed_days:number; scheduled_days:number; reps:number; lapses:number; state:number; last_review?:string; learning_steps:number }
 export interface Word extends Draft { nativeLanguage?:string; learningLanguage?:string; id:string; deckId:string; createdAt:string; card:ReviewCard; reverse:ReviewCard }
 export interface Settings { spellingPractice?:boolean; secondaryReviewLanguage:string|null; nativeLanguage:string; learningLanguage:string; theme:'light'|'dark'|'system'; accent:'indigo'|'teal'|'rose'; darkAccent:'indigo'|'teal'|'rose'; dailyGoal:number; direction:'forward'|'reverse'; dayStart:number; retention:number; reminders:boolean; reminderTime:string; reminderStart:string; reminderEnd:string; reminderCount:number; reminderLastSlot?:string; keepInTray:boolean; launchAtLogin:boolean; apiBase:string; onboardingComplete:boolean }
-export interface Snapshot { workspaceId:string; scopeRevision:string; account?:AccountState; queueCount?:number; decks:Deck[]; words:Word[]; settings:Settings; reviewedToday:number; streak:number; activity:{date:string;count:number}[] }
+export interface Snapshot { accessibleWordIds?:string[]; workspaceId:string; scopeRevision:string; account?:AccountState; queueCount?:number; decks:Deck[]; words:Word[]; settings:Settings; reviewedToday:number; streak:number; activity:{date:string;count:number}[] }
 export interface Profile { id:string; email:string|null; display_name?:string; provider?:string }
 export interface Entitlement { status:string; product_id?:string|null; expires_at?:string|null; is_trial:boolean; auto_renew:boolean; was_ever_paid:boolean; source?:string|null; checked_at?:string }
 export interface SyncStatus { state:'idle'|'syncing'|'synced'|'conflict'|'error'; lastSyncedAt?:string; message?:string }
 export interface AccountState { profile:Profile|null; entitlement:Entitlement|null; testMode:boolean; sync?:SyncStatus }
 export interface CatalogCard { client_card_id:string; word:string; translations:string[]; part_of_speech?:string; pronunciation?:string; examples:string[]; notes?:string; native_language:string; learning_language:string }
 export interface CatalogDeck { id:string; title:string; description?:string; word_count:number; cards:CatalogCard[] }
 export interface Bridge { forWorkspace(scopeRevision:string):Bridge; activateWorkspace(scopeRevision:string):void; resolveSync():Promise<SyncStatus>; openAppMenu(name:'Owl AI'|'Edit'|'View',x:number,y:number):Promise<void>; systemTimeFormat():Promise<{hour12:boolean}>;
  loginGoogle():Promise<AccountState|null>; cancelGoogleLogin():Promise<void>;
  snapshot():Promise<Snapshot>; saveDeck(input:Partial<Deck>&{name:string}):Promise<Deck>; deleteDeck(id:string):Promise<void>;
  addWords(deckId:string,words:Draft[]):Promise<number>; editWord(id:string,word:Draft):Promise<void>; deleteWord(id:string):Promise<void>;
  saveSettings(settings:Partial<Settings>):Promise<Settings>; queue(deckId?:string):Promise<Word[]>; previews(wordId:string):Promise<Record<number,string>>; review(wordId:string,grade:number,attemptId:string):Promise<Word>; reviewSession(active:boolean):Promise<void>;
  importFile(mode:'auto'|'pairs'|'words'):Promise<{name:string;text:string;drafts:Draft[]}|null>; parse(text:string,mode:'auto'|'pairs'|'words'):Promise<Draft[]>; exportDeck(id:string):Promise<boolean>;
diff --git a/src/App.tsx b/src/App.tsx
index d4951cb..63a45aa 100644
--- a/src/App.tsx
+++ b/src/App.tsx
@@ -1,12 +1,13 @@
+import {activeAccess,evaluateAccess,eligibleWordIds} from '../shared/access-policy';
 import PartOfSpeech from './PartOfSpeech';
 import { WorkspaceBridgeProvider } from './WorkspaceBridge';
 import { useCallback,useEffect,useLayoutEffect,useMemo,useRef,useState } from 'react';
 import { ArrowLeft,ArrowRight,BookOpen,CalendarDays,Check,ChevronDown,ChevronRight,Flame,FolderOpen,GraduationCap,LayoutGrid,LogIn,MoreHorizontal,Plus,Search,Settings as SettingsIcon,Sparkles,Target,Trash2,X,Download,Upload,PenLine,CloudOff,Crown,CircleHelp,RefreshCw } from 'lucide-react';
 import type { AccountState,Deck,Draft,Snapshot,Word } from '../shared/types';
 import { languageName } from '../shared/types';
 import { Owl,Empty,Modal,Speak,LanguageSelect,errorMessage } from './components';
 import CreateSet from './CreateSet';
 import WordCardGrid from './WordCardGrid';
 import Review from './Review';
 import Profile from './Profile';
 import Settings from './Settings';
@@ -49,29 +50,29 @@ export default function App(){
   if(target===page)return;
   if(hasBack(target))returnPages.current.push({page,query,selected,editDeck});else returnPages.current=[];
   setPage(target);setQuery('');setMenu('');setError('');if(target==='create'||target==='add')setEditDeck(deck);
  };
  const syncNow=()=>run(async()=>{setSyncPending(true);try{const result=await owl.sync();notify(result.message??(result.state==='synced'?'Your cards are synced.':'Sync could not finish.'));}finally{setSyncPending(false);}});
  const addCards=(deck:Deck)=>go('add',deck);
  const goBack=()=>{const previous=returnPages.current.pop();setPage(previous?.page??'learn');setQuery(previous?.query??'');setSelected(previous?.selected??'');setEditDeck(previous?.editDeck);setMenu('');setError('');};
  if(!snapshot)return <div className="boot"><Owl size={70}/><h1>Owl AI</h1><p>{error||'Opening your learning space…'}</p>{error&&window.owl&&<button className="button" onClick={()=>reload().catch(e=>setError(errorMessage(e)))}>Try again</button>}</div>;
  const {decks,words,settings}=snapshot,selectedDeck=decks.find(x=>x.id===selected),filtered=words.filter(x=>(!selected||x.deckId===selected)&&(!query||[x.word,x.translation,x.notes].some(s=>s?.toLowerCase().includes(query.toLowerCase()))));
  const titles:Record<Page,[string,string]>={learn:['Your learning space','A little progress, every day.'],sets:['My flashcard sets','Everything you’re curious about, together.'],library:['Explore the library','Find something worth remembering.'],add:['Add cards',editDeck?.name??'Choose a collection for your words.'],create:[editDeck?'Edit flashcard set':'Create a flashcard set','Turn everyday discoveries into lasting knowledge.'],profile:[account.testMode?'Account':'Account & Premium',account.testMode?'Test mode is active.':'A shared subscription. A familiar experience.'],settings:['Your preferences','A learning routine that feels like you.'],help:['Help',account.testMode?'A simple guide to cards and practice.':'A simple guide to cards, practice, and Premium.']};
  // Keep authentication feedback through failed attempts; real account changes still reset the profile.
  const pageKey=page==='profile'?'profile:'+snapshot.workspaceId:snapshot.scopeRevision;
- const premium=['premium','trial','grace'].includes(account.entitlement?.status??'');
+ const premium=activeAccess(account);const eligible=new Set(snapshot.accessibleWordIds??eligibleWordIds(account,words));const editable=evaluateAccess(account,'edit',words.length).allow;
  return <WorkspaceBridgeProvider bridge={owl}><div className="app-shell"><aside className="sidebar"><button className="brand" onClick={()=>go('learn')}><Owl size={38}/><span>owl<span className="brand-ai">ai</span><small>WORDS THAT STAY.</small></span></button><span className="nav-label">YOUR SPACE</span><nav>{navigation.map(([target,Icon,label])=><button key={target} className={`nav-item ${page===target?'active':''}`} onClick={()=>go(target)}><Icon size={20}/><span>{label}</span>{target==='learn'&&due>0&&<span className="nav-count">{due}</span>}</button>)}</nav><button className="create-nav" onClick={()=>go('create')}><Plus size={20}/> Create a set</button><div className="sidebar-divider"/><span className="nav-label">YOUR COLLECTIONS <span>{decks.length}</span></span><div className="sidebar-sets">{decks.slice(0,8).map((deck,i)=><button key={deck.id} className={`sidebar-set ${selected===deck.id&&page==='learn'?'chosen':''}`} onClick={()=>{go('learn');setSelected(deck.id);}}><span className={`set-dot dot-${i%4}`}/><span>{deck.name}</span><small>{words.filter(x=>x.deckId===deck.id).length}</small></button>)}{!decks.length&&<p className="sidebar-empty">Your first collection<br/>starts with one word.</p>}</div><div className="sidebar-bottom">{account.testMode?<div className="premium-nudge"><span className="premium-mini"><Sparkles size={16}/>Test mode</span><p>All learning features are available.</p></div>:<div className="premium-nudge"><span className="premium-mini"><Sparkles size={16}/>{premium?'Your Premium travels with you':'One curious mind. Every screen.'}</span><p>{premium?'Enjoy Owl AI on iPhone and Windows.':'Bring your iPhone Premium to Windows.'}</p><button onClick={()=>go('profile')}>{premium?'View subscription':'Discover Premium'} <ArrowRight size={15}/></button></div>}<button className="nav-item" disabled={!account.profile||busy||syncPending||account.sync?.state==='syncing'} title={account.profile?'Sync your cards':'Sign in to sync your cards'} onClick={()=>void syncNow()}><RefreshCw size={19} className={syncPending||account.sync?.state==='syncing'?'spin':undefined}/><span>{syncPending||account.sync?.state==='syncing'?'Syncing…':'Sync now'}</span></button><button className={`nav-item ${page==='settings'?'active':''}`} onClick={()=>go('settings')}><SettingsIcon size={19}/><span>Settings</span></button><button className="account-nav" onClick={()=>go('profile')}><div className="avatar">{account.profile?.email?.charAt(0).toUpperCase()??<LogIn size={18}/>}</div><span>{account.profile?.email?.split('@')[0]??'Make yourself at home'}<small>{account.profile?'Your Owl AI account':'Sign in to connect'}</small></span><ChevronRight size={16}/></button></div></aside>
  <main className="main"><div key={pageKey} className={`page-content ${page==='learn'?'learn-page':''}`}><header className="page-heading"><div className="page-heading-leading">{hasBack(page)&&<button className="back-button" onClick={goBack}><ArrowLeft size={17}/> Back</button>}<div><h1>{page==='learn'?'Learn':titles[page][0]}</h1><p>{titles[page][1]}</p></div></div><div className="heading-actions">{(page==='learn'||page==='sets')&&<button className="button" onClick={()=>go('create')}><Plus size={16}/> Create a set</button>}<button className="icon-button" aria-label="Preferences" title="Preferences" onClick={()=>go('settings')}><SettingsIcon size={18}/></button><button className="help-button" aria-label="Help" aria-current={page==='help'?'page':undefined} onClick={()=>go('help')}><CircleHelp size={17}/><span>Help</span></button><button className="avatar small-avatar" aria-label="Profile" title="Profile" onClick={()=>go('profile')}>{account.profile?.email?.charAt(0).toUpperCase()??'O'}</button></div></header>{error&&<div className="error global-error" role="alert">{error}<button className="icon-button" aria-label="Dismiss error" onClick={()=>setError('')}><X size={16}/></button></div>}
  {page==='learn'&&<><div className="stats-grid"><Stat Icon={BookOpen} label="Words collected" value={words.length} detail="A growing world of vocabulary" color="purple"/><Stat Icon={Target} label="Ready to review" value={due} detail="A little practice, a lasting memory" color="orange"/><Stat Icon={Check} label="Practiced today" value={snapshot.reviewedToday} detail="Every repetition counts" color="green"/><Stat Icon={Flame} label="Day streak" value={snapshot.streak} detail={snapshot.streak?'Keep your momentum going':'Your next habit starts today'} color="pink"/></div>
  <section className="practice-bar" aria-label="Daily practice"><div className="practice-summary"><span className="practice-icon"><GraduationCap size={24}/></span><div><h2>{due?'Ready for a little practice?':'Make room for a new word.'}</h2><p>{due?`${due} cards ready · pick up where you left off.`:'Add a few words to start your next session.'}</p></div></div><button className="button" onClick={()=>due?setReview(true):go('create')}>{due?'Start today’s practice':'Create your first cards'}<ArrowRight size={17}/></button></section>
  <section className="collections-section" aria-label="Your collections"><div className="section-line"><h2>Your collections <span className="count">{decks.length}</span></h2><div className="collection-actions"><button className="text-button" onClick={()=>go('sets')}>View all sets <ArrowRight size={16}/></button><button className="button secondary" onClick={()=>go('create')}><Plus size={16}/> Create a set</button></div></div>{decks.length>0&&<div className="collection-strip">{decks.slice(0,3).map(deck=><CollectionTile key={deck.id} deck={deck} count={words.filter(x=>x.deckId===deck.id).length} selected={selected===deck.id} onClick={()=>setSelected(selected===deck.id?'':deck.id)}/>)}</div>}</section>
- <div className="word-section"><div className="section-line"><h2>{selectedDeck?.name??'Your vocabulary'} <span className="count">{filtered.length}</span></h2><div className="word-tools">{selectedDeck&&<button className="button secondary" onClick={()=>addCards(selectedDeck)}><Plus size={16}/> Add cards</button>}<select aria-label="Filter set" value={selected} onChange={e=>setSelected(e.target.value)}><option value="">All sets</option>{decks.map(d=><option value={d.id} key={d.id}>{d.name}</option>)}</select><div className="search-field"><Search size={16}/><input ref={searchRef} value={query} onChange={e=>setQuery(e.target.value)} placeholder="Find a word…"/><kbd>Ctrl F</kbd></div></div></div>{filtered.length?<WordCardGrid>{filtered.map(word=>{const deck=decks.find(x=>x.id===word.deckId),card=settings.direction==='forward'?word.card:word.reverse;return <div className="word-row-wrap word-mini-card" key={word.id}><div className="word-row"><div className="word-primary"><button className="word-expand" aria-expanded={expanded===word.id} onClick={()=>setExpanded(expanded===word.id?'':word.id)}><strong>{word.word}</strong><PartOfSpeech value={word.partOfSpeech}/><span>{word.translation||'Translation needed'}</span></button><Speak word={word.word} language={word.learningLanguage??deck?.learningLanguage??settings.learningLanguage} onError={notify}/></div><span className="set-tag" title={deck?.name}><FolderOpen size={13}/>{deck?.name}</span><span className={`review-date ${card.state===0?'new':''}`}>{card.state===0?'New word':new Date(card.due)<new Date()?'Ready now':new Date(card.due).toLocaleDateString(undefined,{month:'short',day:'numeric'})}</span><div className="word-actions"><button className="icon-button" title="Edit card" onClick={()=>setEditWord(word)}><PenLine size={16}/></button><button className="icon-button" title="Delete card" onClick={()=>setDeleteTarget({kind:'word',id:word.id,name:word.word})}><Trash2 size={16}/></button></div></div>{expanded===word.id&&<div className="word-detail">{word.pronunciation&&<p className="pronunciation">{word.pronunciation}</p>}{word.examples?.length?word.examples.map((example,i)=><p key={i}>“{example}”</p>):<p className="muted">Add an example or a personal note using Edit card.</p>}{word.notes&&<p>{word.notes}</p>}<span className="muted small">{card.reps} reviews · {Math.round(card.stability*10)/10} days stability</span></div>}</div>;})}</WordCardGrid>:<Empty title={query?'No words found':'Every collection starts with a word'} body={query?'Try another word or translation.':'Create a set, add something you’re curious about, and let practice do the rest.'} action={!query&&<button className="button secondary" onClick={()=>selectedDeck?addCards(selectedDeck):go('create')}><Plus size={16}/> {selectedDeck?'Add cards':'Create a set'}</button>}/>}</div></>}
+ <div className="word-section"><div className="section-line"><h2>{selectedDeck?.name??'Your vocabulary'} <span className="count">{filtered.length}</span></h2><div className="word-tools">{selectedDeck&&<button className="button secondary" onClick={()=>addCards(selectedDeck)}><Plus size={16}/> Add cards</button>}<select aria-label="Filter set" value={selected} onChange={e=>setSelected(e.target.value)}><option value="">All sets</option>{decks.map(d=><option value={d.id} key={d.id}>{d.name}</option>)}</select><div className="search-field"><Search size={16}/><input ref={searchRef} value={query} onChange={e=>setQuery(e.target.value)} placeholder="Find a word…"/><kbd>Ctrl F</kbd></div></div></div>{filtered.length?<WordCardGrid>{filtered.map(word=>{const deck=decks.find(x=>x.id===word.deckId),card=settings.direction==='forward'?word.card:word.reverse;return <div className="word-row-wrap word-mini-card" key={word.id}><div className="word-row"><div className="word-primary"><button className="word-expand" disabled={!eligible.has(word.id)} aria-expanded={expanded===word.id} onClick={()=>setExpanded(expanded===word.id?'':word.id)}><strong>{eligible.has(word.id)?word.word:'Saved card'}</strong>{eligible.has(word.id)&&<PartOfSpeech value={word.partOfSpeech}/>}<span>{eligible.has(word.id)?word.translation||'Translation needed':'Outside your current access · preserved on this computer'}</span></button>{eligible.has(word.id)&&<Speak word={word.word} language={word.learningLanguage??deck?.learningLanguage??settings.learningLanguage} onError={notify}/>}</div><span className="set-tag" title={deck?.name}><FolderOpen size={13}/>{deck?.name}</span><span className={`review-date ${card.state===0?'new':''}`}>{card.state===0?'New word':new Date(card.due)<new Date()?'Ready now':new Date(card.due).toLocaleDateString(undefined,{month:'short',day:'numeric'})}</span><div className="word-actions"><button className="icon-button" title="Edit card" disabled={!editable||!eligible.has(word.id)} onClick={()=>setEditWord(word)}><PenLine size={16}/></button><button className="icon-button" title="Delete card" onClick={()=>setDeleteTarget({kind:'word',id:word.id,name:word.word})}><Trash2 size={16}/></button></div></div>{eligible.has(word.id)&&expanded===word.id&&<div className="word-detail">{word.pronunciation&&<p className="pronunciation">{word.pronunciation}</p>}{word.examples?.length?word.examples.map((example,i)=><p key={i}>“{example}”</p>):<p className="muted">Add an example or a personal note using Edit card.</p>}{word.notes&&<p>{word.notes}</p>}<span className="muted small">{card.reps} reviews · {Math.round(card.stability*10)/10} days stability</span></div>}</div>;})}</WordCardGrid>:<Empty title={query?'No words found':'Every collection starts with a word'} body={query?'Try another word or translation.':'Create a set, add something you’re curious about, and let practice do the rest.'} action={!query&&<button className="button secondary" onClick={()=>selectedDeck?addCards(selectedDeck):go('create')}><Plus size={16}/> {selectedDeck?'Add cards':'Create a set'}</button>}/>}</div></>}
  {page==='sets'&&<><div className="deck-grid">{decks.map((deck,i)=><div className="managed-deck" key={deck.id}><DeckTile deck={deck} count={words.filter(w=>w.deckId===deck.id).length} index={i} onClick={()=>{setSelected(deck.id);go('learn');}}/><div className="deck-controls"><label><input type="checkbox" checked={deck.active} disabled={busy||decks.length===1} title={decks.length===1?'Your only set is always active for study':undefined} onChange={e=>run(async()=>{await owl.saveDeck({...deck,active:e.target.checked});})}/> Active for study</label><button className="icon-button" aria-label={`Options for ${deck.name}`} onClick={()=>setMenu(menu===deck.id?'':deck.id)}><MoreHorizontal size={19}/></button></div>{menu===deck.id&&<div className="deck-menu"><button onClick={()=>go('create',deck)}><PenLine size={15}/> Edit set</button><button onClick={()=>addCards(deck)}><Plus size={15}/> Add cards</button><button onClick={()=>run(async()=>{if(await owl.exportDeck(deck.id))notify('Set exported.');setMenu('');})}><Download size={15}/> Export CSV</button><button onClick={()=>run(async()=>{const result=await owl.publish(deck.id);notify(`Publication status: ${result.status}`);setMenu('');})}><Upload size={15}/> Publish to library</button><button onClick={()=>run(async()=>{await owl.unpublish(deck.id);notify('Set unpublished.');setMenu('');})}><CloudOff size={15}/> Unpublish</button><button className="danger" onClick={()=>{setDeleteTarget({kind:'deck',id:deck.id,name:deck.name});setMenu('');}}><Trash2 size={15}/> Delete set</button></div>}</div>)}<button className="new-deck-tile" onClick={()=>go('create')}><span><Plus size={24}/></span><strong>Something new to learn</strong><small>Create a set</small></button></div>{!decks.length&&<Empty title="A home for your discoveries" body="Build collections around your travels, conversations, books, or whatever makes you curious."/>}</>}
  {(page==='create'||page==='add')&&<CreateSet cardsOnly={page==='add'} key={page+(editDeck?.id??'new')} snapshot={snapshot} deck={editDeck} onSaved={deck=>{if(workspace.current!==snapshot.scopeRevision)return;setSelected(deck.id);if(page==='create'){returnPages.current=[];setPage('learn');}void reload();}} notify={notify}/>}{page==='profile'&&<Profile account={account} onChanged={reload} notify={notify}/>}{page==='settings'&&<Settings key={JSON.stringify(settings)} settings={settings} onChanged={reload} notify={notify}/>}{page==='help'&&<Help testMode={account.testMode} onCreate={()=>go('create')} onProfile={()=>go('profile')} onSettings={()=>go('settings')}/>}{page==='library'&&<Library onChanged={reload} notify={notify}/>}<footer className="page-footer"><Owl size={17}/><span>Made for curious minds.</span><span>OWL AI · WINDOWS</span></footer></div></main>
  {notice&&<div className="toast" role="status"><Check size={18}/><span>{notice}</span><button aria-label="Dismiss notification" onClick={()=>setNotice('')}><X size={15}/></button></div>}{review&&<Review key={snapshot.scopeRevision} snapshot={snapshot} onClose={()=>{setReview(false);void reload();}} onChanged={reload}/>}{editWord&&<EditWord key={snapshot.scopeRevision+editWord.id} word={editWord} onClose={()=>setEditWord(null)} onSave={draft=>run(async()=>{await owl.editWord(editWord.id,draft);setEditWord(null);notify('Card updated.');})}/>}{deleteTarget&&<Modal title={`Delete ${deleteTarget.kind==='deck'?'set':'card'}?`} onClose={()=>setDeleteTarget(null)}><p>Delete <strong>{deleteTarget.name}</strong>{deleteTarget.kind==='deck'?' and all its local cards and review history':''}? This cannot be undone.</p>{deleteTarget.kind==='deck'&&<p className="muted small">If published, unpublish it first to remove the separate library copy.</p>}<div className="modal-actions"><button className="button secondary" onClick={()=>setDeleteTarget(null)}>Keep it</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{if(deleteTarget.kind==='deck')await owl.deleteDeck(deleteTarget.id);else await owl.deleteWord(deleteTarget.id);setDeleteTarget(null);})}>Delete</button></div></Modal>}
  {!settings.onboardingComplete&&<Onboarding key={snapshot.scopeRevision} settings={settings} onSave={(native,learning)=>run(async()=>{await owl.saveSettings({nativeLanguage:native,learningLanguage:learning,onboardingComplete:true});})}/>}</div></WorkspaceBridgeProvider>;
 }
 function CollectionTile({deck,count,selected,onClick}:{deck:Deck;count:number;selected:boolean;onClick:()=>void}){return <button className="collection-mini" aria-pressed={selected} onClick={onClick}><span className="collection-name">{deck.name}</span><span className="collection-meta"><span>{count} {count===1?'card':'cards'}</span><span className="deck-languages">{deck.learningLanguage.split('-')[0].toUpperCase()} <ArrowRight size={10}/> {deck.nativeLanguage.toUpperCase()}</span></span></button>;}
 function Stat({Icon,label,value,detail,color}:{Icon:typeof BookOpen;label:string;value:number;detail:string;color:string}){return <div className="stat-card"><div className={`stat-icon ${color}`}><Icon size={20}/></div><span>{label}</span><strong>{value}<small>{label==='Day streak'?'days':''}</small></strong><p>{detail}</p></div>;}
 function DeckTile({deck,count,index,onClick,selected=false}:{deck:Deck;count:number;index:number;onClick:()=>void;selected?:boolean}){return <button className={`deck-tile tile-${index%4} ${selected?'selected':''}`} onClick={onClick}><div className="deck-tile-top"><div className="deck-icon"><BookOpen size={23}/></div><span className="deck-languages">{deck.learningLanguage.split('-')[0].toUpperCase()} <ArrowRight size={10}/> {deck.nativeLanguage.toUpperCase()}</span></div><h3>{deck.name}</h3><p>{deck.description||`${languageName(deck.learningLanguage)} · a little, every day`}</p><div className="deck-tile-bottom"><span>{count} cards</span><span className="tile-arrow"><ArrowRight size={16}/></span></div></button>;}
 function EditWord({word,onClose,onSave}:{word:Word;onClose:()=>void;onSave:(draft:Draft)=>Promise<void>}){const [draft,setDraft]=useState<Draft>(word),[busy,setBusy]=useState(false);return <Modal title="A word worth remembering" onClose={onClose}><form onSubmit={async e=>{e.preventDefault();setBusy(true);await onSave(draft);setBusy(false);}}><label className="field">Word<input required value={draft.word} onChange={e=>setDraft({...draft,word:e.target.value,partOfSpeech:undefined})}/></label><label className="field">Translation<input required value={draft.translation} onChange={e=>setDraft({...draft,translation:e.target.value})}/></label><label className="field">Pronunciation<input value={draft.pronunciation??''} onChange={e=>setDraft({...draft,pronunciation:e.target.value})}/></label><label className="field">Examples · one per line<textarea rows={3} value={draft.examples?.join('\n')??''} onChange={e=>setDraft({...draft,examples:e.target.value.split('\n').filter(Boolean)})}/></label><label className="field">Personal notes<textarea rows={2} value={draft.notes??''} onChange={e=>setDraft({...draft,notes:e.target.value})}/></label><div className="modal-actions"><button type="button" className="button secondary" onClick={onClose}>Cancel</button><button className="button" disabled={busy}>Save card</button></div></form></Modal>;}
-function Onboarding({settings,onSave}:{settings:Snapshot['settings'];onSave:(native:string,learning:string)=>Promise<void>}){const [native,setNative]=useState(settings.nativeLanguage),[learning,setLearning]=useState(settings.learningLanguage),[busy,setBusy]=useState(false);return <Modal title="Welcome to your next chapter." onClose={()=>{}}><div className="welcome-owl"><Owl size={75}/></div><p className="muted">A little curiosity, a few words, a daily moment to yourself. Let’s make this space yours.</p><div className="two-cols"><LanguageSelect label="I speak" value={native} onChange={setNative}/><LanguageSelect label="I want to learn" value={learning} onChange={setLearning}/></div><button className="button full" disabled={busy} onClick={async()=>{setBusy(true);await onSave(native,learning);setBusy(false);}}>Make room for discovery <ArrowRight size={17}/></button><p className="small muted center">No account needed to begin. Your words stay on your computer.</p></Modal>;}
+function Onboarding({settings,onSave}:{settings:Snapshot['settings'];onSave:(native:string,learning:string)=>Promise<void>}){const [native,setNative]=useState(settings.nativeLanguage),[learning,setLearning]=useState(settings.learningLanguage),[busy,setBusy]=useState(false);return <Modal title="Welcome to your next chapter." onClose={()=>{}}><div className="welcome-owl"><Owl size={75}/></div><p className="muted">A little curiosity, a few words, a daily moment to yourself. Let’s make this space yours.</p><div className="two-cols"><LanguageSelect label="I speak" value={native} onChange={setNative}/><LanguageSelect label="I want to learn" value={learning} onChange={setLearning}/></div><button className="button full" disabled={busy} onClick={async()=>{setBusy(true);await onSave(native,learning);setBusy(false);}}>Make room for discovery <ArrowRight size={17}/></button><p className="small muted center">Sign in to add cards on desktop. Your saved words stay on your computer.</p></Modal>;}
diff --git a/src/CreateSet.tsx b/src/CreateSet.tsx
index 287b16d..0694266 100644
--- a/src/CreateSet.tsx
+++ b/src/CreateSet.tsx
@@ -1,43 +1,45 @@
+import {evaluateAccess} from '../shared/access-policy';
 import PartOfSpeech from './PartOfSpeech';
 import { useOwl } from './WorkspaceBridge';
 import { useRef,useState } from 'react';
 import SecondaryReview from './SecondaryReview';
 import {secondaryReviewLanguage} from '../shared/secondary-review';
 import { ArrowRight,FileUp,PenLine,Sparkles,Clipboard,Trash2,Check,LoaderCircle } from 'lucide-react';
 import type { Snapshot,Draft,Deck } from '../shared/types';
 import { LanguageSelect,errorMessage } from './components';
 export default function CreateSet({snapshot,deck,cardsOnly=false,onSaved,notify}:{snapshot:Snapshot;deck?:Deck;cardsOnly?:boolean;onSaved:(deck:Deck)=>void;notify:(message:string)=>void}){
  const owl=useOwl();
  const [name,setName]=useState(deck?.name??''),[description,setDescription]=useState(deck?.description??''),[native,setNative]=useState(deck?.nativeLanguage??snapshot.settings.nativeLanguage),[learning,setLearning]=useState(deck?.learningLanguage??snapshot.settings.learningLanguage);
  const [method,setMethod]=useState<'manual'|'ai'|'paste'|'file'>('manual'),[rows,setRows]=useState<(Draft&{selected:boolean;secondarySourceWord?:string})[]>([]),[word,setWord]=useState(''),[translation,setTranslation]=useState(''),[text,setText]=useState(''),[mode,setMode]=useState<'auto'|'pairs'|'words'>('auto'),[busy,setBusy]=useState(''),[error,setError]=useState('');
+ const localAccess=evaluateAccess(snapshot.account,cardsOnly?'add':'edit',snapshot.words.length);const aiAccess=evaluateAccess(snapshot.account,'ai',snapshot.words.length);
  const secondaryLanguage=secondaryReviewLanguage(snapshot.settings.secondaryReviewLanguage,native);
  const saving=useRef(false),wordInput=useRef<HTMLInputElement>(null);
  const append=(drafts:Draft[])=>setRows(previous=>[...previous,...drafts.map(x=>({...x,selected:true,secondarySourceWord:x.word}))]);
  const run=async(label:string,fn:()=>Promise<void>)=>{if(saving.current)return;saving.current=true;setBusy(label);setError('');try{await fn();}catch(e){setError(errorMessage(e));}finally{saving.current=false;setBusy('');}};
  const add=async()=>{if(!word.trim()||!translation.trim())return;await run('Saving your card',async()=>{
   if(!deck)throw new Error('Choose a set before adding cards.');
   const count=await owl.addWords(deck.id,[{word:word.trim(),translation:translation.trim()}]);
   setWord('');setTranslation('');notify(count?'Card saved. Add your next word.':'This card is already in your set.');onSaved(deck);
   requestAnimationFrame(()=>wordInput.current?.focus());
  });};
  async function save(){await run(cardsOnly?'Saving your cards':'Saving your set',async()=>{
   if(cardsOnly){
    if(!deck)throw new Error('Choose a set before adding cards.');
    const selected=rows.filter(x=>x.selected);
    if(!selected.length)throw new Error('Add at least one card to the preview.');
    if(selected.some(x=>!x.word.trim()||!x.translation.trim()))throw new Error('Complete the word and translation on every selected card.');
    const count=await owl.addWords(deck.id,selected.map(({selected,secondarySourceWord,...row})=>row));
    setRows(previous=>previous.filter(row=>!row.selected));setText('');notify(count?count+' cards saved.':'These cards are already in your set.');onSaved(deck);
   }else{
    if(!name.trim())throw new Error('Give your set a name.');
    const saved=await owl.saveDeck({id:deck?.id,name,description,nativeLanguage:native,learningLanguage:learning});
    notify(deck?'Set updated.':'Your set is ready. Add your first cards whenever you like.');onSaved(saved);
   }
  });}
- return <div className="create-layout single-panel">{!cardsOnly&&<section className="panel"><div className="section-number">01 <span>MAKE IT YOURS</span></div><h2>{deck?'Edit your set':'A new collection of words'}</h2><p className="muted">Give your ideas a place to grow. All cards are saved on this computer.</p><label className="field">Set name<input autoFocus maxLength={100} placeholder="e.g. A little French, every day" value={name} onChange={e=>setName(e.target.value)}/></label><label className="field">Description <span className="optional">optional</span><input maxLength={500} placeholder="What would you like to learn?" value={description} onChange={e=>setDescription(e.target.value)}/></label><div className="two-cols"><LanguageSelect label="I speak" value={native} onChange={setNative}/><LanguageSelect label="I'm learning" value={learning} onChange={setLearning}/></div><div className="local-note"><Check size={15}/> Yours to keep. Available offline.</div>{error&&<div className="error" role="alert">{error}</div>}<div className="save-bar"><span className="muted small">Add cards after creating your set.</span><button className="button" disabled={!!busy||!name.trim()} onClick={save}>Save set <ArrowRight size={17}/></button></div></section>}
- {cardsOnly&&<section className="panel"><fieldset disabled={!!busy} className="card-entry-fields"><div className="section-number">02 <span>ADD YOUR CARDS</span></div><h2>{deck?.name}</h2><p className="muted">Save a word and its translation, then keep adding to your collection.</p><div className="method-grid">{([['manual',PenLine,'Write a card'],['ai',Sparkles,'AI translation'],['paste',Clipboard,'Paste text'],['file',FileUp,'Import a file']] as const).map(([id,Icon,label])=><button key={id} className={`method ${method===id?'selected':''}`} onClick={()=>setMethod(id)}><Icon size={21}/>{label}</button>)}</div>
- {(method==='manual'||method==='ai')&&<><div className="card-entry-fields"><label className="field">Word or phrase<input ref={wordInput} value={word} onChange={e=>setWord(e.target.value)} placeholder="Something worth remembering" onKeyDown={e=>{if(e.key==='Enter'&&method==='manual'){e.preventDefault();void add();}}}/></label>{method==='manual'&&<label className="field">Translation<textarea aria-label="Translation" rows={4} value={translation} onChange={e=>setTranslation(e.target.value)} placeholder="What does it mean?"/></label>}</div>{method==='manual'?<button className="button secondary" disabled={!word.trim()||!translation.trim()||!!busy} onClick={add}><Check size={17}/> Save cards</button>:<><p className="muted small">{snapshot.account?.testMode?'Test mode: sign in to use AI translation.':'Uses your shared Premium subscription.'} Review the translation, then choose Save selected cards.</p><button className="button" disabled={!word.trim()||!!busy} onClick={()=>run('Translating',async()=>{append([await owl.translate(word,native,learning)]);setWord('');})}><Sparkles size={17}/> Translate with AI</button></>}</>}
+ return <div className="create-layout single-panel">{!localAccess.allow&&<p className="error" role="alert">{localAccess.reason}</p>}{!cardsOnly&&<section className="panel"><div className="section-number">01 <span>MAKE IT YOURS</span></div><h2>{deck?'Edit your set':'A new collection of words'}</h2><p className="muted">Give your ideas a place to grow. All cards are saved on this computer.</p><label className="field">Set name<input autoFocus maxLength={100} placeholder="e.g. A little French, every day" value={name} onChange={e=>setName(e.target.value)}/></label><label className="field">Description <span className="optional">optional</span><input maxLength={500} placeholder="What would you like to learn?" value={description} onChange={e=>setDescription(e.target.value)}/></label><div className="two-cols"><LanguageSelect label="I speak" value={native} onChange={setNative}/><LanguageSelect label="I'm learning" value={learning} onChange={setLearning}/></div><div className="local-note"><Check size={15}/> Yours to keep. Available offline.</div>{error&&<div className="error" role="alert">{error}</div>}<div className="save-bar"><span className="muted small">Add cards after creating your set.</span><button className="button" disabled={!!busy||!name.trim()||!localAccess.allow} onClick={save}>Save set <ArrowRight size={17}/></button></div></section>}
+ {cardsOnly&&<section className="panel"><fieldset disabled={!!busy||!localAccess.allow} className="card-entry-fields"><div className="section-number">02 <span>ADD YOUR CARDS</span></div><h2>{deck?.name}</h2><p className="muted">Save a word and its translation, then keep adding to your collection.</p><div className="method-grid">{([['manual',PenLine,'Write a card'],['ai',Sparkles,'AI translation'],['paste',Clipboard,'Paste text'],['file',FileUp,'Import a file']] as const).map(([id,Icon,label])=><button key={id} className={`method ${method===id?'selected':''}`} onClick={()=>setMethod(id)}><Icon size={21}/>{label}</button>)}</div>
+ {(method==='manual'||method==='ai')&&<><div className="card-entry-fields"><label className="field">Word or phrase<input ref={wordInput} value={word} onChange={e=>setWord(e.target.value)} placeholder="Something worth remembering" onKeyDown={e=>{if(e.key==='Enter'&&method==='manual'){e.preventDefault();void add();}}}/></label>{method==='manual'&&<label className="field">Translation<textarea aria-label="Translation" rows={4} value={translation} onChange={e=>setTranslation(e.target.value)} placeholder="What does it mean?"/></label>}</div>{method==='manual'?<button className="button secondary" disabled={!word.trim()||!translation.trim()||!!busy} onClick={add}><Check size={17}/> Save cards</button>:<><p className="muted small">{snapshot.account?.testMode?'Test mode: sign in to use AI translation.':'Uses your shared Premium subscription.'} Review the translation, then choose Save selected cards.</p><button className="button" disabled={!word.trim()||!!busy||!aiAccess.allow} onClick={()=>run('Translating',async()=>{append([await owl.translate(word,native,learning)]);setWord('');})}><Sparkles size={17}/> Translate with AI</button></>}</>}
  {(method==='paste'||method==='file')&&<><div className="segmented">{(['words','pairs','auto'] as const).map(m=><button className={mode===m?'selected':''} onClick={()=>setMode(m)} key={m}>{m==='words'?'Words':m==='pairs'?'Pairs':'Auto'}</button>)}</div>{method==='paste'?<><textarea value={text} onChange={e=>setText(e.target.value)} rows={6} placeholder={'hello — привет\nthank you — спасибо'}/><button className="button secondary" disabled={!text.trim()||!!busy} onClick={()=>run('Reading text',async()=>append(await owl.parse(text,mode)))}>Preview cards <ArrowRight size={16}/></button></>:<div className="drop-zone"><FileUp size={30}/><h3>Bring your words along</h3><p>TXT, CSV, TSV, PDF, or an image · up to 20 MB</p><button className="button secondary" disabled={!!busy} onClick={()=>run('Reading file · first OCR use downloads language data',async()=>{const file=await owl.importFile(mode);if(file){setText(file.text);append(file.drafts);}})}>Choose a file</button><span className="muted small">Files are processed on your computer.</span></div>}</>}
- {rows.length>0&&<div className="draft-area"><div className="section-line"><h3>Preview <span className="count">{rows.filter(x=>x.selected).length}</span></h3><button className="text-button" disabled={!!busy} onClick={()=>run('Translating missing words',async()=>{const updated=[...rows];for(let i=0;i<updated.length;i++)if(updated[i].selected&&!updated[i].translation.trim())updated[i]={...updated[i],...await owl.translate(updated[i].word,native,learning)};setRows(updated);})}><Sparkles size={14}/> Fill missing translations</button></div><div className="draft-list">{rows.map((row,index)=><div className="draft-item" key={index}><div className={`draft-row ${method==='ai'?'draft-row-ai':''}`}><input type="checkbox" checked={row.selected} aria-label={`Include card ${index+1}`} onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,selected:e.target.checked}:x))}/><input aria-label={`Word ${index+1}`} value={row.word} onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,word:e.target.value,partOfSpeech:undefined}:x))}/>{method==='ai'?<label className="field">Translation<textarea aria-label={`Translation ${index+1}`} rows={4} value={row.translation} placeholder="What does it mean?" onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,translation:e.target.value}:x))}/></label>:<><ArrowRight size={14}/><input aria-label={`Translation ${index+1}`} value={row.translation} placeholder="Translation" onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,translation:e.target.value}:x))}/></>}<button className="icon-button" aria-label="Remove draft" onClick={()=>setRows(rows.filter((_,i)=>i!==index))}><Trash2 size={15}/></button></div>{method==='ai'&&<PartOfSpeech value={row.partOfSpeech}/>}{method==='ai'&&secondaryLanguage&&row.word===row.secondarySourceWord&&row.word.trim()&&row.translation.trim()&&<SecondaryReview key={JSON.stringify([snapshot.scopeRevision,row.word,native,learning,secondaryLanguage])} preview={{word:row.word,native,learning}} language={secondaryLanguage} accessRevision={JSON.stringify([snapshot.scopeRevision,snapshot.account?.entitlement?.status,snapshot.account?.testMode])}/>}</div>)}</div></div>}
+ {rows.length>0&&<div className="draft-area"><div className="section-line"><h3>Preview <span className="count">{rows.filter(x=>x.selected).length}</span></h3><button className="text-button" disabled={!!busy||!aiAccess.allow} onClick={()=>run('Translating missing words',async()=>{const updated=[...rows];for(let i=0;i<updated.length;i++)if(updated[i].selected&&!updated[i].translation.trim())updated[i]={...updated[i],...await owl.translate(updated[i].word,native,learning)};setRows(updated);})}><Sparkles size={14}/> Fill missing translations</button></div><div className="draft-list">{rows.map((row,index)=><div className="draft-item" key={index}><div className={`draft-row ${method==='ai'?'draft-row-ai':''}`}><input type="checkbox" checked={row.selected} aria-label={`Include card ${index+1}`} onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,selected:e.target.checked}:x))}/><input aria-label={`Word ${index+1}`} value={row.word} onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,word:e.target.value,partOfSpeech:undefined}:x))}/>{method==='ai'?<label className="field">Translation<textarea aria-label={`Translation ${index+1}`} rows={4} value={row.translation} placeholder="What does it mean?" onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,translation:e.target.value}:x))}/></label>:<><ArrowRight size={14}/><input aria-label={`Translation ${index+1}`} value={row.translation} placeholder="Translation" onChange={e=>setRows(rows.map((x,i)=>i===index?{...x,translation:e.target.value}:x))}/></>}<button className="icon-button" aria-label="Remove draft" onClick={()=>setRows(rows.filter((_,i)=>i!==index))}><Trash2 size={15}/></button></div>{method==='ai'&&<PartOfSpeech value={row.partOfSpeech}/>}{method==='ai'&&secondaryLanguage&&row.word===row.secondarySourceWord&&row.word.trim()&&row.translation.trim()&&<SecondaryReview key={JSON.stringify([snapshot.scopeRevision,row.word,native,learning,secondaryLanguage])} preview={{word:row.word,native,learning}} language={secondaryLanguage} accessRevision={JSON.stringify([snapshot.scopeRevision,snapshot.account?.entitlement?.status,snapshot.account?.testMode])}/>}</div>)}</div></div>}
  {error&&<div className="error" role="alert">{error}</div>}{busy&&<p className="loading-line" role="status"><LoaderCircle className="spin" size={16}/>{busy}…</p>}{rows.length>0&&<div className="save-bar"><span className="muted small">{rows.filter(x=>x.selected).length} new cards ready to save</span><button className="button" disabled={!!busy||!rows.some(row=>row.selected)} onClick={save}>Save selected cards <ArrowRight size={17}/></button></div>}</fieldset></section>}</div>;
 }
diff --git a/src/Profile.tsx b/src/Profile.tsx
index 2573e4a..60502f8 100644
--- a/src/Profile.tsx
+++ b/src/Profile.tsx
@@ -1,22 +1,23 @@
+import {activeAccess} from '../shared/access-policy';
 import { useOwl } from './WorkspaceBridge';
 import { useEffect,useState } from 'react';
 import { ShieldCheck,Monitor,Smartphone,ArrowRight,LogOut,Crown,Link2,Check } from 'lucide-react';
 import type { AccountState } from '../shared/types';
 import { Modal,errorMessage } from './components';
 export default function Profile({account,onChanged,notify}:{account:AccountState;onChanged:()=>Promise<void>;notify:(message:string)=>void}){
  const owl=useOwl();
  // Cancellation is unscoped: only leaving the profile cancels, not refreshing its IPC bridge.
  useEffect(()=>()=>{void owl.cancelGoogleLogin().catch(()=>{});},[]);
  const [googlePending,setGooglePending]=useState(false),[email,setEmail]=useState(''),[password,setPassword]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[deleting,setDeleting]=useState(false),[replacingWithCloud,setReplacingWithCloud]=useState(false);
  const run=async(fn:()=>Promise<void>)=>{setBusy(true);setError('');try{await fn();}catch(e){setError(errorMessage(e));}finally{try{await onChanged();}catch(e){setError(errorMessage(e));}setBusy(false);}};
- const premium=['premium','trial','grace'].includes(account.entitlement?.status??'');
+ const premium=activeAccess(account);const needsRefresh=['premium','trial','grace'].includes(account.entitlement?.status??'');const periodEnded=Date.parse(account.entitlement?.expires_at??'')<=Date.now();
  const sync=account.sync;
  const syncLabel=sync?.state==='syncing'?'Syncing your cards…':sync?.state==='synced'?'Your cards are synced':sync?.state==='conflict'?'Some changes need attention':sync?.state==='error'?'Sync could not finish':'Ready to sync';
  return <div className="profile-layout"><section className="panel"><span className="eyebrow">YOUR OWL AI ACCOUNT</span><h2>{account.profile?'One account. Your learning space.':'Your iPhone account, on Windows.'}</h2><p className="muted">{account.profile?'Your collections, cards, and review schedule sync with this same account on iPhone. Your signed-out cards stay in a separate space on this computer.':'First create or open your account in Owl AI on iPhone. Then sign in here using the same email or Google account. Windows does not create new accounts.'}</p>{error&&<p className="error" role="alert">{error}</p>}
  {account.profile?<><div className="profile-person"><div className="avatar large">{(account.profile.email??'O').charAt(0).toUpperCase()}</div><div><h3>{account.profile.email??account.profile.display_name??'Owl AI learner'}</h3><span className="muted small">Your private account workspace</span></div><ShieldCheck className="accent" size={23}/></div>
  <section aria-label="Account synchronization"><h3>{syncLabel}</h3><p className="small muted" role="status">{sync?.message??(sync?.lastSyncedAt?`Last synced ${new Date(sync.lastSyncedAt).toLocaleString()}.`:'Changes sync automatically while you are online. Only data belonging to this account is included.')}</p><p className="small muted">Signed-out cards are never uploaded automatically. Sign out to return to them.</p>{sync?.state==='conflict'&&<button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(true)}>Use cloud version</button>}</section>
  <button className="button secondary" disabled={busy} onClick={()=>run(async()=>{await owl.logout();notify('Signed out. Returned to your separate local workspace.');})}><LogOut size={16}/> Sign out</button><div className="danger-zone"><h3>Delete account</h3><p className="muted small">Deletes your server account, synced data, and shared access. Your separate signed-out workspace is preserved. Apple billing must be canceled separately.</p><button className="text-button danger" disabled={busy} onClick={()=>setDeleting(true)}>Delete my account</button></div></>:<form onSubmit={e=>{e.preventDefault();void run(async()=>{await owl.login(email,password);setPassword('');notify('Welcome to your Owl AI account.');});}}><button type="button" className="button secondary full google-sign-in" disabled={busy} onClick={()=>run(async()=>{setGooglePending(true);try{const result=await owl.loginGoogle();if(result)notify('Welcome to your Owl AI account.');}finally{setGooglePending(false);}})}><span aria-hidden="true" className="google-letter">G</span> Continue with Google</button>{googlePending&&<div className="google-wait"><p className="small muted">Continue in your browser with the Google account you use in Owl AI on iPhone.</p><button type="button" className="text-button" onClick={()=>owl.cancelGoogleLogin().catch(e=>setError(errorMessage(e)))}>Cancel Google sign-in</button></div>}<p className="small muted center">or sign in with email</p><label className="field">Email<input type="email" autoComplete="email" required value={email} onChange={e=>setEmail(e.target.value)} placeholder="you@example.com"/></label><label className="field">Password<input type="password" autoComplete="current-password" required value={password} onChange={e=>setPassword(e.target.value)} placeholder="Your password"/></label><button className="button full" disabled={busy}>{busy?'Connecting…':'Sign in'}<ArrowRight size={17}/></button><p className="small muted">You can still create and review local cards without signing in. These cards are kept separate from your account.</p></form>}</section>
- {account.testMode?<section className="premium-panel"><div className="premium-mark"><Check size={25}/><span>Test mode</span></div><h1>All learning features are available.</h1><p>Word limits and AI quotas are lifted while test mode is active. Sign in to use online features.</p></section>:<section className="premium-panel"><div className="premium-mark"><Crown size={25}/><span>OWL AI PREMIUM</span></div><h1>One subscription.<br/>More places to grow.</h1><p>Your iPhone subscription comes with you to Windows.</p><div className="devices"><Smartphone size={42}/><Link2 size={23}/><Monitor size={52}/></div><div className="premium-status"><span>{premium?'Premium is active':account.entitlement?account.entitlement.status.replace(/_/g,' '):'Connect your subscription'}</span>{premium&&<Check size={19}/>}</div>{account.entitlement?.expires_at&&<p className="small">{account.entitlement.auto_renew?'Current period ends':'Access until'} {new Date(account.entitlement.expires_at).toLocaleDateString()}</p>}<ol className="steps"><li>Open Owl AI on iPhone and sign in to this account.</li><li>Link your verified Apple purchase in Profile.</li><li>Your Premium updates automatically on both devices.</li></ol><p className="small premium-note">No second purchase needed. Use the same account on both devices for your cards and Premium.</p></section>}
+ {account.testMode?<section className="premium-panel"><div className="premium-mark"><Check size={25}/><span>Test mode</span></div><h1>All learning features are available.</h1><p>Word limits and AI quotas are lifted while test mode is active. Sign in to use online features.</p></section>:<section className="premium-panel"><div className="premium-mark"><Crown size={25}/><span>OWL AI PREMIUM</span></div><h1>One subscription.<br/>More places to grow.</h1><p>Your iPhone subscription comes with you to Windows.</p><div className="devices"><Smartphone size={42}/><Link2 size={23}/><Monitor size={52}/></div><div className="premium-status"><span>{premium?'Premium is active':needsRefresh?(periodEnded?'Subscription period ended':'Refresh subscription status'):account.entitlement?account.entitlement.status.replace(/_/g,' '):'Connect your subscription'}</span>{premium&&<Check size={19}/>}</div>{account.entitlement?.expires_at&&<p className="small">{account.entitlement.auto_renew?'Current period ends':'Access until'} {new Date(account.entitlement.expires_at).toLocaleDateString()}</p>}<ol className="steps"><li>Open Owl AI on iPhone and sign in to this account.</li><li>Link your verified Apple purchase in Profile.</li><li>Your Premium updates automatically on both devices.</li></ol><p className="small premium-note">No second purchase needed. Use the same account on both devices for your cards and Premium.</p></section>}
  {replacingWithCloud&&<Modal title="Use cloud version?" onClose={()=>!busy&&setReplacingWithCloud(false)}><p>This replaces your local account cards and review schedule with the server copy. Unsynced local edits will be removed from this workspace.</p><p>A recoverable backup is saved on this computer before replacement. You can restore it from Settings → Restore backup while signed into this account.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(false)}>Keep local cards</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{const result=await owl.resolveSync();setReplacingWithCloud(false);notify(result.message??'Cloud version restored.');})}>Use cloud version</button></div></Modal>}
  {deleting&&<Modal title="Delete your Owl AI account?" onClose={()=>!busy&&setDeleting(false)}><p>This removes your server account, synced data, and shared subscription access. A linked purchase cannot be automatically attached to a new account.</p><p>Your separate signed-out workspace is preserved. Deleting your account does not cancel Apple billing.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setDeleting(false)}>Keep account</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{await owl.deleteAccount();setDeleting(false);notify('Account deleted. Returned to your separate local workspace.');})}>Delete account</button></div></Modal>}</div>;
 }
diff --git a/src/Review.tsx b/src/Review.tsx
index de8b151..4d78f61 100644
--- a/src/Review.tsx
+++ b/src/Review.tsx
@@ -4,31 +4,31 @@ import { useEffect,useState,useCallback,useRef } from 'react';
 import { ArrowRight,Check,RotateCcw,Keyboard } from 'lucide-react';
 import type { Snapshot,Word } from '../shared/types';
 import { Modal,Speak,errorMessage } from './components';
 import {secondaryReviewLanguage} from '../shared/secondary-review';
 import SecondaryReview from './SecondaryReview';
 import SpellingPractice from './SpellingPractice';
 import {englishReviewVerbForms} from '../shared/english-verb-forms';
 function interval(date:string){const minutes=Math.max(1,Math.round((new Date(date).getTime()-Date.now())/60000));return minutes<60?`${minutes}m`:minutes<1440?`${Math.round(minutes/60)}h`:`${Math.round(minutes/1440)}d`;}
 export default function Review({snapshot,deckId,onClose,onChanged}:{snapshot:Snapshot;deckId?:string;onClose:()=>void;onChanged:()=>Promise<void>}){
  const owl=useOwl();
  const [spelling,setSpelling]=useState(snapshot.settings.spellingPractice===true),[savingMode,setSavingMode]=useState(false);
  useEffect(()=>setSpelling(snapshot.settings.spellingPractice===true),[snapshot.settings.spellingPractice]);
- const [queue,setQueue]=useState<Word[]>([]),[revealed,setRevealed]=useState(false),[previews,setPreviews]=useState<Record<number,string>>({}),[busy,setBusy]=useState(true),[error,setError]=useState(''),[completed,setCompleted]=useState(0),[loaded,setLoaded]=useState(false);const grading=useRef(false),attempt=useRef(crypto.randomUUID());const word=snapshot.words.find(row=>row.id===queue[0]?.id)??queue[0],reverse=spelling||snapshot.settings.direction==='reverse';
+ const [queue,setQueue]=useState<Word[]>([]),[revealed,setRevealed]=useState(false),[previews,setPreviews]=useState<Record<number,string>>({}),[busy,setBusy]=useState(true),[error,setError]=useState(''),[completed,setCompleted]=useState(0),[loaded,setLoaded]=useState(false);const grading=useRef(false),attempt=useRef(crypto.randomUUID());const candidate=snapshot.words.find(row=>row.id===queue[0]?.id)??queue[0],word=candidate&&(!snapshot.accessibleWordIds||snapshot.accessibleWordIds.includes(candidate.id))?candidate:undefined,reverse=spelling||snapshot.settings.direction==='reverse';
  const toggleSpelling=async()=>{setSavingMode(true);try{const settings=await owl.saveSettings({spellingPractice:!spelling});setSpelling(settings.spellingPractice===true);await onChanged();}catch(e){setError(errorMessage(e));}finally{setSavingMode(false);}};
  useEffect(()=>{void owl.reviewSession(true).catch(()=>{});return()=>{void owl.reviewSession(false).catch(()=>{});};},[owl]);
  useEffect(()=>{if(loaded)void owl.reviewSession(queue.length>0).catch(()=>{});},[loaded,queue.length,owl]);
- const load=useCallback(async()=>{const rows=await owl.queue(deckId);setQueue(rows);setPreviews(rows[0]?await owl.previews(rows[0].id):{});setLoaded(true);},[deckId,owl,snapshot.account?.testMode]);
+ const load=useCallback(async()=>{const rows=await owl.queue(deckId);setQueue(rows);setPreviews(rows[0]?await owl.previews(rows[0].id):{});setLoaded(true);},[deckId,owl,snapshot.account?.testMode,snapshot.accessibleWordIds?.join(',')]);
  useEffect(()=>{setRevealed(false);load().catch(e=>setError(errorMessage(e))).finally(()=>setBusy(false));},[load]);
  const grade=useCallback(async(rating:number)=>{if(!word||!revealed||grading.current)return;grading.current=true;setBusy(true);try{await owl.review(word.id,rating,attempt.current);attempt.current=crypto.randomUUID();setCompleted(x=>x+1);setRevealed(false);await load();await onChanged();}catch(e){setError(errorMessage(e));}finally{grading.current=false;setBusy(false);}},[word,revealed,load,onChanged,owl]);
  useEffect(()=>{const handler=(e:KeyboardEvent)=>{if((e.target as HTMLElement).matches('input,textarea,select'))return;if(e.code==='Space'){e.preventDefault();setRevealed(true);}else if(['1','2','3','4'].includes(e.key)){e.preventDefault();void grade(Number(e.key));}};window.addEventListener('keydown',handler);return()=>window.removeEventListener('keydown',handler);},[grade]);
  const deck=snapshot.decks.find(x=>x.id===word?.deckId);
  const source=snapshot.words.find(row=>row.id===word?.id)??word,sourceDeck=snapshot.decks.find(row=>row.id===source?.deckId);
  const native=source?.nativeLanguage??sourceDeck?.nativeLanguage??'',learning=source?.learningLanguage??sourceDeck?.learningLanguage??'';
  const forms=word?englishReviewVerbForms(word.word,learning||snapshot.settings.learningLanguage):null;
  const verbForms=forms?<p className="review-verb-forms" aria-label={`V2: ${forms.v2}, V3: ${forms.v3}`}>{forms.v2} · {forms.v3}</p>:null;
  const secondary=secondaryReviewLanguage(snapshot.settings.secondaryReviewLanguage,native);
  const secondaryKey=JSON.stringify([snapshot.scopeRevision,word?.id,source?.word,native,learning,secondary]);
- const accessRevision=JSON.stringify([snapshot.account?.profile?.id,snapshot.account?.entitlement?.status,snapshot.account?.testMode]);
+ const accessRevision=JSON.stringify([snapshot.account?.profile?.id,snapshot.account?.entitlement?.status,snapshot.account?.entitlement?.expires_at,snapshot.account?.testMode]);
  return <Modal title="A moment to remember" onClose={onClose} wide><div className="review-meta"><span>{deck?.name??'Daily practice'}</span><span>{completed} reviewed · {queue.length} ready</span></div><div className="progress-track"><div style={{width:`${completed/(completed+queue.length||1)*100}%`}}/></div>{error&&<p className="error" role="alert">{error}</p>}
  {word?<><div className="review-card"><span className="eyebrow">{reverse?'RECALL THE WORD':'WHAT DOES THIS MEAN?'}</span><h1>{reverse?word.translation:word.word}</h1>{!reverse&&<PartOfSpeech value={word.partOfSpeech}/>}{!reverse&&verbForms}{!reverse&&word.pronunciation&&<p className="pronunciation">{word.pronunciation}</p>}<Speak word={reverse?word.translation:word.word} language={reverse?(word.nativeLanguage??deck?.nativeLanguage??snapshot.settings.nativeLanguage):(word.learningLanguage??deck?.learningLanguage??snapshot.settings.learningLanguage)} onError={setError}/>{spelling&&<SpellingPractice key={JSON.stringify([snapshot.scopeRevision,word.id,source?.word,completed])} expected={source?.word??word.word} language={word.learningLanguage??deck?.learningLanguage??snapshot.settings.learningLanguage}/>} {revealed?<div className="answer"><span className="eyebrow">{reverse?'WORD':'TRANSLATION'}</span><h2>{reverse?word.word:word.translation}</h2>{reverse&&<PartOfSpeech value={word.partOfSpeech}/>}{reverse&&verbForms}{word.examples?.[0]&&<p>“{word.examples[0]}”</p>}{word.notes&&<p className="muted">{word.notes}</p>}{secondary&&<SecondaryReview key={secondaryKey} wordId={word.id} language={secondary} accessRevision={accessRevision}/>}</div>:<p className="review-prompt">Take a breath. Try to remember before you reveal.</p>}</div><div className="review-tools"><button className="icon-button spelling-toggle" aria-label="Spelling practice" title={`Spelling practice: ${spelling?'On':'Off'}`} aria-pressed={spelling} disabled={savingMode} onKeyDown={event=>event.stopPropagation()} onClick={()=>void toggleSpelling()}><Keyboard size={20}/>{spelling&&<Check size={10} className="spelling-toggle-check"/>}</button></div>{!revealed?<button className="button full" disabled={busy} onClick={()=>setRevealed(true)}>Show answer <kbd>Space</kbd></button>:<div className="grade-grid">{['Again','Hard','Good','Easy'].map((label,i)=><button key={label} className={`grade grade-${i+1}`} disabled={busy} onClick={()=>grade(i+1)}><span>{label} <kbd>{i+1}</kbd></span><small>{previews[i+1]?interval(previews[i+1]):'—'}</small></button>)}</div>}<p className="review-footer">Small repetitions build lasting memories.</p></>:loaded?<div className="review-done"><span className="done-icon"><Check size={34}/></span><h1>{completed?'Well done. Make it a habit.':'You’re all caught up.'}</h1><p>{completed?`You practiced ${completed} cards. Your next reviews are scheduled.`:'No more cards are ready right now. New cards follow your daily goal and selected languages.'}</p><button className="button" onClick={onClose}>Back to your space <ArrowRight size={16}/></button><button className="text-button" onClick={()=>load().catch(e=>setError(errorMessage(e)))}><RotateCcw size={14}/> Check for due cards</button></div>:<p className="muted">Preparing your cards…</p>}</Modal>;
 }
diff --git a/tests/access-policy.test.ts b/tests/access-policy.test.ts
new file mode 100644
index 0000000..6edbd9d
--- /dev/null
+++ b/tests/access-policy.test.ts
@@ -0,0 +1,82 @@
+import test from 'node:test';
+import assert from 'node:assert/strict';
+import {evaluateAccess,eligibleWordIds} from '../shared/access-policy';
+import type {AccountState} from '../shared/types';
+const now=new Date('2026-09-27T12:00:00Z');
+const account=(status:string,expiry='2026-09-28T12:00:00Z',checked=now.toISOString()):AccountState=>({profile:{id:'a',email:null},testMode:false,entitlement:{status,expires_at:expiry,checked_at:checked,is_trial:status==='trial',auto_renew:false,was_ever_paid:status!=='trial'}});
+test('fresh paid access allows mutation; elapsed expiry and stale confirmation do not',()=>{
+ assert.equal(evaluateAccess(account('premium'),'add',101,now).allow,true);
+ for(const state of [account('premium',now.toISOString()),account('premium',undefined,'2026-09-27T10:00:00Z'),account('premium','invalid')])assert.equal(evaluateAccess(state,'add',101,now).allow,false);
+});
+test('ordinary expired paid retains saved read/review only; inactive and unknown statuses stay bounded',()=>{
+ const words=Array.from({length:101},(_,i)=>({id:String(i).padStart(3,'0'),createdAt:now.toISOString()})).reverse();
+ const paid=account('expired_paid');assert.equal(eligibleWordIds(paid,words,now).size,101);
+ for(const operation of ['add','edit','ai'] as const)assert.equal(evaluateAccess(paid,operation,0,now).allow,false);
+ for(const status of ['free','expired_trial','revoked','invalid_subscription','unexpected'])assert.deepEqual([...eligibleWordIds(account(status),words,now)],Array.from({length:10},(_,i)=>String(i).padStart(3,'0')));
+ assert.equal(evaluateAccess(paid,'delete',101,now).allow,true);assert.equal(evaluateAccess(paid,'export',101,now).allow,true);
+});
+test('free admission is global and desktop mutation requires an account',()=>{
+ assert.equal(evaluateAccess(account('free'),'add',9,now).allow,true);assert.equal(evaluateAccess(account('free'),'add',10,now).allow,false);
+ assert.equal(evaluateAccess({...account('premium'),profile:null,testMode:true},'add',0,now).allow,false);
+ assert.equal(evaluateAccess(account('free'),'ai',0,now).allow,false);
+});
+
+const live=(status:string)=>account(status,new Date(Date.now()+86400000).toISOString(),new Date().toISOString());
+import {Store} from '../electron/store';
+import {mkdtempSync,rmSync} from 'node:fs';import {tmpdir} from 'node:os';import {join} from 'node:path';
+test('store guards manual and staged imports globally and atomically; catalog cannot leave a deck',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-access-'));const store=await Store.open(join(root,'db.sqlite'),'account');let state=live('free');
+ try{
+  store.bindAccess(()=>state);const deck=store.saveDeck({name:'one'});
+  store.addWords(deck.id,Array.from({length:9},(_,i)=>({word:'word'+i,translation:'value'})));
+  assert.throws(()=>store.addWords(deck.id,[{word:'ten',translation:'value'},{word:'eleven',translation:'value'}]),/10/);assert.equal(store.snapshot().words.length,9);
+  const next=store.saveDeck({name:'two',learningLanguage:'es'});store.addWords(next.id,[{word:'ten',translation:'value'}]);
+  assert.throws(()=>store.addWords(next.id,[{word:'eleven',translation:'value'}]),/10/);
+  const before=store.snapshot();assert.throws(()=>store.importDeck({name:'catalog'},[{word:'catalog',translation:'value'}]),/10/);assert.deepEqual(store.snapshot().decks,before.decks);
+  state=live('premium');store.addWords(deck.id,[{word:'paid',translation:'value'}]);state=live('expired_paid');
+  assert.throws(()=>store.addWords(deck.id,[{word:'staged',translation:'value'}]));assert.throws(()=>store.editWord(store.snapshot().words[0].id,{word:'changed',translation:'value'}));
+  const saved=store.snapshot();state=live('revoked');assert.equal(store.eligibleIds().size,10);assert.equal(store.snapshot().words.length,11);
+  assert.throws(()=>store.editWord(saved.words.find(w=>!store.eligibleIds().has(w.id))!.id,{word:'changed',translation:'value'}));
+  store.deleteWord(saved.words[0].id);assert.equal(store.snapshot().words.length,10);
+ }finally{store.close();rmSync(root,{recursive:true,force:true});}
+});
+test('101 synced cards and backups survive restrictions; paid read history survives scoped offline relaunch',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-access-')),path=join(root,'db.sqlite');let store=await Store.open(path,'origin-account');
+ try{
+  const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:101},(_,i)=>({word:'word'+i,translation:'value'})));
+  store.bindAccess(()=>live('premium'));store.rememberAccess(live('premium'));store.close();store=await Store.open(path,'origin-account');
+  store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,101);assert.throws(()=>store.addWords(deck.id,[{word:'staged',translation:'value'}]));
+  store.rememberAccess(live('invalid_subscription'));assert.equal(store.eligibleIds().size,10);assert.equal(store.snapshot().words.length,101);
+  assert.throws(()=>store.editWord(store.snapshot().words[0].id,{word:'changed',translation:'value'}));
+  store.backup(join(root,'backup.sqlite'));await store.restore(join(root,'backup.sqlite'));assert.equal(store.snapshot().words.length,101);
+  const snap=store.snapshot();store.applySync(snap.decks,snap.words,[]);assert.equal(store.snapshot().words.length,101);
+  const other=await Store.open(join(root,'other.sqlite'),'different-origin-account');other.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(other.accessState()?.entitlement,null);other.close();
+ }finally{store.close();rmSync(root,{recursive:true,force:true});}
+});
+
+test('expired trial, revoked, invalid and unknown cannot mutate even below10',()=>{
+ for(const status of ['expired_trial','revoked','invalid_subscription','unexpected'])for(const operation of ['add','edit','ai'] as const)assert.equal(evaluateAccess(account(status),operation,0,now).allow,false);
+});
+
+test('authoritative inactive read state replaces old paid history across offline relaunch',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-history-')),path=join(root,'db.sqlite');let store=await Store.open(path,'scope');
+ try{
+  const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:11},(_,i)=>({word:'word'+i,translation:'value'})));
+  for(const status of ['revoked','invalid_subscription','free']){
+   store.rememberAccess(live('premium'));store.rememberAccess({...live(status),entitlement:{...live(status).entitlement!,was_ever_paid:false}});store.close();store=await Store.open(path,'scope');store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,10);assert.equal(store.accessState()?.entitlement?.status,status);
+  }
+  store.rememberAccess({...live('premium'),entitlement:{...live('premium').entitlement!,expires_at:'invalid'}});store.close();store=await Store.open(path,'scope');store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,10);
+ }finally{store.close();rmSync(root,{recursive:true,force:true});}
+});
+
+test('free deck language/content changes cannot edit locked cards; study activation remains allowed',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-deck-'));const store=await Store.open(join(root,'db.sqlite'),'scope');
+ try{const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:11},(_,i)=>({word:'word'+i,translation:'value'})));store.bindAccess(()=>live('free'));
+ assert.throws(()=>store.saveDeck({...deck,learningLanguage:'es'}));assert.equal(store.saveDeck({...deck,active:false}).id,deck.id);assert.equal(store.snapshot().words.length,11);
+ }finally{store.close();rmSync(root,{recursive:true,force:true});}
+});
+
+test('global creation ordering compares instants across timezone offsets before ID ties',()=>{
+ const early=Array.from({length:9},(_,i)=>({id:'early'+i,createdAt:'2026-09-26T12:00:00Z'}));
+ const words=[...early,{id:'later',createdAt:'2026-09-27T11:30:00Z'},{id:'offset',createdAt:'2026-09-27T13:00:00+02:00'}];const eligible=eligibleWordIds(account('free'),words,now);assert.equal(eligible.has('offset'),true);assert.equal(eligible.has('later'),false);
+});
diff --git a/tests/access-ui.test.ts b/tests/access-ui.test.ts
new file mode 100644
index 0000000..2a43145
--- /dev/null
+++ b/tests/access-ui.test.ts
@@ -0,0 +1,5 @@
+import test from 'node:test';import assert from 'node:assert/strict';import {createElement} from 'react';import {renderToStaticMarkup} from 'react-dom/server';import CreateSet from '../src/CreateSet';import {WorkspaceBridgeProvider} from '../src/WorkspaceBridge';import type {Bridge,Snapshot} from '../shared/types';
+test('expired paid create form explains retained cards and disables new content',()=>{
+ const snapshot={scopeRevision:'a',words:[],settings:{nativeLanguage:'ru',learningLanguage:'en-us',secondaryReviewLanguage:null},account:{profile:{id:'a',email:null},entitlement:{status:'expired_paid',was_ever_paid:true,is_trial:false,auto_renew:false},testMode:false}} as unknown as Snapshot;
+ const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(CreateSet,{snapshot,onSaved:()=>{},notify:()=>{}})}));assert.match(html,/Saved cards remain available/);assert.match(html,/disabled/);
+});
diff --git a/tests/api-test-mode.test.ts b/tests/api-test-mode.test.ts
index 800cf13..bc7360a 100644
--- a/tests/api-test-mode.test.ts
+++ b/tests/api-test-mode.test.ts
@@ -16,12 +16,50 @@ test('signed-out API refresh exposes test mode without inventing a subscription
  try{
   const address=server.address();assert.ok(address&&typeof address!=='string');
   const base=()=>`http://127.0.0.1:${address.port}`,path=join(root,'account.enc'),api=new Api(path,base);
   assert.deepEqual(api.state(),{profile:null,entitlement:null,testMode:false});
   assert.deepEqual(await api.refreshEntitlement(),{profile:null,entitlement:null,testMode:true});
   await assert.rejects(api.translate('hello','en-us','es'),/Sign in/);
   await assert.rejects(api.reviewTranslation({word:'hello',native_language:'en-us',learning_language:'es',secondary_language:'fr'}),/Sign in/);
   await api.logout();assert.equal(api.state().testMode,true);
   const restarted=new Api(path,base);assert.equal(restarted.state().testMode,false);
   enabled=false;assert.deepEqual(await restarted.refreshEntitlement(),{profile:null,entitlement:null,testMode:false});
  }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
 });
+
+test('HTTP refusals retain status and standard retry timing without replaying AI calls',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-api-errors-'));let status=429,code='quota',calls=0,retry='120';
+ const server=createServer((request,response)=>{calls++;response.statusCode=status;response.setHeader('Content-Type','application/json');if(retry)response.setHeader('Retry-After',retry);response.end(JSON.stringify({code}));});
+ await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
+ try{
+  const address=server.address();assert.ok(address&&typeof address!=='string');const api=new Api(join(root,'account.enc'),()=>`http://127.0.0.1:${address.port}`);
+  (api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:'fake',profile:{id:'a',email:null}};
+  for(const value of [429,402,503]){status=value;code=value===503?'subscription_reconciliation_required':'quota';const before=calls;
+   await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),(error:any)=>{assert.equal(error.status,value);if(value===429){assert.equal(error.retryAfterSeconds,120);assert.match(error.message,/120/);}if(value===503){assert.match(error.message,/support|recover/i);assert.equal(api.state().entitlement?.status,'invalid_subscription');}return true;});assert.equal(calls,before+1);
+  }
+  status=503;code='apple_temporarily_unavailable';retry='';await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),/temporarily unavailable/i);
+  status=401;const before=calls;await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}));assert.equal(calls,before+1,'AI denial is never automatically replayed');
+ }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
+});
+
+import {retryAfterSeconds} from '../electron/api';
+test('standard Retry-After dates parse without guessing missing or malformed timing',()=>{
+ const now=Date.parse('2026-09-27T12:00:00Z');assert.equal(retryAfterSeconds('Sun, 27 Sep 2026 12:02:00 GMT',now),120);assert.equal(retryAfterSeconds(null,now),undefined);assert.equal(retryAfterSeconds('bad',now),undefined);
+});
+test('selected origin invalidation rejects late entitlement replies even on a round trip',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-generation-'));let release!:()=>void;
+ const server=createServer(async(request,response)=>{response.setHeader('Content-Type','application/json');if(request.url==='/owlai/config/feature-flags'){response.end('{"test_mode":false}');return;}await new Promise<void>(resolve=>release=resolve);response.end(JSON.stringify({status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),was_ever_paid:true}));});
+ await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
+ try{const address=server.address();assert.ok(address&&typeof address!=='string');const first=`http://127.0.0.1:${address.port}`;let base=first;const api=new Api(join(root,'account.enc'),()=>base);(api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'a',email:null}};
+ const pending=api.refreshEntitlement();while(!release)await new Promise(resolve=>setTimeout(resolve,1));const rejected=assert.rejects(pending);base='https://second.example.com';api.state();base=first;api.state();release();await rejected;assert.equal(api.state().entitlement,null);assert.equal(api.state().testMode,false);
+ }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
+});
+
+test('a delayed error body cannot revoke the new account entitlement',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-body-')),original=globalThis.fetch;let finish!:(text:string)=>void;
+ try{globalThis.fetch=async()=>new Response(new ReadableStream({start(controller){finish=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}),{status:503});
+ const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');const session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'old',email:null}};(api as any).session=session;
+ const pending=api.request('/owlai/account/ai/word-detail','POST',{}),rejected=assert.rejects(pending);await new Promise(resolve=>setTimeout(resolve,0));api.clear();(api as any).session={...session,profile:{id:'new',email:null}};
+ const entitlement={status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false};(api as any).entitlement=entitlement;
+ finish(JSON.stringify({code:'subscription_reconciliation_required'}));await rejected;assert.deepEqual(api.state().entitlement,entitlement);
+ }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
+});
diff --git a/tests/feature-flags.test.ts b/tests/feature-flags.test.ts
index 89730cd..07eacab 100644
--- a/tests/feature-flags.test.ts
+++ b/tests/feature-flags.test.ts
@@ -78,12 +78,21 @@ test('persisted true cannot enable an offline launch',async()=>{
 test('false stays authoritative after a later failed refresh',async()=>{
  const root=mkdtempSync(join(tmpdir(),'owl-flags-')),path=join(root,'flags.json');
  try{
   let value:boolean|null=true;
   const flags=new FeatureFlags(path,()=> 'https://api.example.com',async()=>{
    if(value===null)throw new Error('offline');return Response.json({test_mode:value});
   });
   await flags.refresh();assert.equal(flags.testMode,true);
   value=false;await flags.refresh();assert.equal(flags.testMode,false);
   value=null;await flags.refresh();assert.equal(flags.testMode,false);
  }finally{rmSync(root,{recursive:true,force:true});}
 });
+
+test('live true expires and origin round trips cannot revive an earlier generation',async()=>{
+ let base='https://first.example.com',now=0,resolve!:(response:Response)=>void;
+ const flags=new FeatureFlags('unused',()=>base,()=>new Promise<Response>(r=>resolve=r),()=>now);
+ const pending=flags.refresh();base='https://second.example.com';assert.equal(flags.testMode,false);
+ base='https://first.example.com';assert.equal(flags.testMode,false);resolve(Response.json({test_mode:true}));await pending;assert.equal(flags.testMode,false);
+ const fresh=flags.refresh();resolve(Response.json({test_mode:true}));await fresh;assert.equal(flags.testMode,true);
+ now=300001;assert.equal(flags.testMode,false);
+});
diff --git a/tests/fixtures/secondary-review.tsx b/tests/fixtures/secondary-review.tsx
index e244959..8d1dc11 100644
--- a/tests/fixtures/secondary-review.tsx
+++ b/tests/fixtures/secondary-review.tsx
@@ -1,25 +1,25 @@
 import React,{useState} from 'react';
 import {createRoot} from 'react-dom/client';
 import Settings from '../../src/Settings';
 import Review from '../../src/Review';
 import CreateSet from '../../src/CreateSet';
 import {WorkspaceBridgeProvider} from '../../src/WorkspaceBridge';
 import type {Bridge,Snapshot,ReviewTranslation} from '../../shared/types';
 import {secondaryReviewLanguage} from '../../shared/secondary-review';
 import '../../src/styles.css';
 import '../../src/ink-indigo.css';
 
 const card={due:new Date().toISOString(),stability:0,difficulty:0,elapsed_days:0,scheduled_days:0,reps:0,lapses:0,state:0,learning_steps:0};
-let data:Snapshot={workspaceId:'a',scopeRevision:'1:a',account:{profile:{id:'a',email:'learner@example.com'},entitlement:{status:'premium',is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false},settings:{nativeLanguage:'ru',learningLanguage:'en-us',secondaryReviewLanguage:null,theme:'light',accent:'indigo',darkAccent:'teal',dailyGoal:5,direction:'forward',dayStart:0,retention:.9,reminders:false,reminderTime:'19:00',reminderStart:'08:00',reminderEnd:'20:00',reminderCount:10,keepInTray:false,launchAtLogin:false,apiBase:'https://api.example.com',onboardingComplete:true},decks:[{id:'travel',name:'Travel words',description:'',nativeLanguage:'ru',learningLanguage:'en-us',active:true,createdAt:new Date().toISOString()}],words:[{id:'journey',word:'journey',translation:'путешествие',examples:['Every journey begins with a single step.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}},{id:'airport',word:'airport',translation:'аэропорт',examples:['We arrived at the airport early.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}}],reviewedToday:0,streak:0,activity:[]};
+let data:Snapshot={workspaceId:'a',scopeRevision:'1:a',account:{profile:{id:'a',email:'learner@example.com'},entitlement:{status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false},settings:{nativeLanguage:'ru',learningLanguage:'en-us',secondaryReviewLanguage:null,theme:'light',accent:'indigo',darkAccent:'teal',dailyGoal:5,direction:'forward',dayStart:0,retention:.9,reminders:false,reminderTime:'19:00',reminderStart:'08:00',reminderEnd:'20:00',reminderCount:10,keepInTray:false,launchAtLogin:false,apiBase:'https://api.example.com',onboardingComplete:true},decks:[{id:'travel',name:'Travel words',description:'',nativeLanguage:'ru',learningLanguage:'en-us',active:true,createdAt:new Date().toISOString()}],words:[{id:'journey',word:'journey',translation:'путешествие',examples:['Every journey begins with a single step.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}},{id:'airport',word:'airport',translation:'аэропорт',examples:['We arrived at the airport early.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}}],reviewedToday:0,streak:0,activity:[]};
 let publish=()=>{};
 const calls:{wordId:string;language:string;resolve:(value:ReviewTranslation)=>void;reject:(error:Error)=>void}[]=[];
 let savedDrafts:unknown[]=[];
 const bridge={
  translate:async(word:string)=>({word,translation:'аэропорт'}),
  addWords:async(_id:string,rows:unknown[])=>{savedDrafts=rows;return rows.length;},
  previewTranslation:async(word:string)=>new Promise<ReviewTranslation>((resolve,reject)=>calls.push({wordId:word,language:data.settings.secondaryReviewLanguage!,resolve,reject})),
  systemTimeFormat:async()=>({hour12:false}),
  saveSettings:async(patch:Partial<Snapshot['settings']>)=>{const settings={...data.settings,...patch};settings.secondaryReviewLanguage=secondaryReviewLanguage(settings.secondaryReviewLanguage,settings.nativeLanguage);data={...data,settings};return settings;},
  reviewSession:async()=>{},queue:async()=>data.words,
  previews:async()=>Object.fromEntries([1,2,3,4].map(x=>[x,new Date(Date.now()+x*86400000).toISOString()])),
  review:async(id:string)=>{const word=data.words.find(x=>x.id===id)!;data={...data,words:data.words.filter(x=>x.id!==id),reviewedToday:data.reviewedToday+1};return word;},
diff --git a/tests/review-translations.test.ts b/tests/review-translations.test.ts
index ae28142..2424370 100644
--- a/tests/review-translations.test.ts
+++ b/tests/review-translations.test.ts
@@ -3,25 +3,25 @@ import assert from 'node:assert/strict';
 import {mkdtempSync,rmSync} from 'node:fs';
 import {tmpdir} from 'node:os';
 import {join} from 'node:path';
 import {Store} from '../electron/store';
 import {ReviewTranslations,type TranslationWorkspace} from '../electron/review-translations';
 
 const spanish={language_code:'es',translation:'viaje',explanation:'Un desplazamiento de un lugar a otro.'};
 async function fixture(){
  const root=mkdtempSync(join(tmpdir(),'owl-review-translation-')),path=join(root,'db.sqlite');
  const store=await Store.open(path,'account-a'),deck=store.saveDeck({name:'Travel'});
  store.addWords(deck.id,[{word:'journey',translation:'путешествие'}]);
  store.saveSettings({secondaryReviewLanguage:'es'});
- const context:TranslationWorkspace={store,scope:'1:account-a',apiBase:'https://api.example.com',account:{profile:{id:'a',email:'a@example.com'},entitlement:{status:'premium',is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false}};
+ const context:TranslationWorkspace={store,scope:'1:account-a',apiBase:'https://api.example.com',account:{profile:{id:'a',email:'a@example.com'},entitlement:{status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false}};
  return {root,path,store,deck,id:store.snapshot().words[0].id,context,cleanup:()=>{store.close();rmSync(root,{recursive:true,force:true});}};
 }
 
 test('review translations cache successful content across relaunch without changing cards, queue, or review counts',async()=>{
  const f=await fixture();let calls=0;
  try{
   const before=f.store.snapshot(),queue=f.store.queue();
   const service=new ReviewTranslations(()=>f.context,async input=>{
    calls++;assert.deepEqual(input,{word:'journey',native_language:'ru',learning_language:'en-us',secondary_language:'es'});return spanish;
   });
   assert.deepEqual(await service.get(f.id),spanish);assert.deepEqual(await service.get(f.id),spanish);assert.equal(calls,1);
   assert.deepEqual(f.store.snapshot().words,before.words);assert.deepEqual(f.store.queue(),queue);assert.equal(f.store.snapshot().reviewedToday,0);
@@ -42,25 +42,25 @@ test('requests use word source languages, falling back to its deck, and cache by
  }finally{f.cleanup();}
 });
 
 test('disabled, missing-card, signed-out, and locked requests cannot invoke AI; active paid access needs no test mode',async()=>{
  const f=await fixture();let calls=0;
  try{
   const service=new ReviewTranslations(()=>f.context,async()=>{calls++;return spanish;});
   f.store.saveSettings({secondaryReviewLanguage:null});await assert.rejects(service.get(f.id));
   f.store.saveSettings({secondaryReviewLanguage:'es'});await assert.rejects(service.get('another-account-card'));
   f.context.account={profile:null,entitlement:null,testMode:true};await assert.rejects(service.get(f.id));
   f.context.account={profile:{id:'a',email:null},entitlement:{status:'expired_paid',is_trial:false,auto_renew:false,was_ever_paid:true},testMode:false};await assert.rejects(service.get(f.id));
   assert.equal(calls,0);
-  f.context.account.entitlement!.status='premium';assert.deepEqual(await service.get(f.id),spanish);assert.equal(calls,1);
+  f.context.account.entitlement={...f.context.account.entitlement!,status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString()};assert.deepEqual(await service.get(f.id),spanish);assert.equal(calls,1);
  }finally{f.cleanup();}
 });
 
 test('failures and malformed or wrong-language responses remain retryable and are never cached',async()=>{
  const f=await fixture();let result:unknown=new Error('Offline'),calls=0;
  try{
   const service=new ReviewTranslations(()=>f.context,async()=>{calls++;if(result instanceof Error)throw result;return result;});
   for(const invalid of [new Error('Offline'),null,{}, {...spanish,language_code:'de'}, {...spanish,translation:' '},{...spanish,explanation:4}]){
    result=invalid;await assert.rejects(service.get(f.id));
   }
   result=spanish;assert.deepEqual(await service.get(f.id),spanish);assert.equal(calls,7);await service.get(f.id);assert.equal(calls,7);
  }finally{f.cleanup();}
@@ -103,12 +103,31 @@ test('AI preview fetches the second language before saving and review reuses its
 
 test('AI preview rejects a late result after the secondary language or account changes',async()=>{
  const f=await fixture();let complete!:(value:unknown)=>void;
  try{
   const service=new ReviewTranslations(()=>f.context,()=>new Promise(resolve=>{complete=resolve;}));
   const pending=service.preview('airport','ru','en-us'),rejected=assert.rejects(pending);
   f.store.saveSettings({secondaryReviewLanguage:'de'});complete(spanish);await rejected;
   f.store.saveSettings({secondaryReviewLanguage:'es'});
   const next=service.preview('airport','ru','en-us'),accountRejected=assert.rejects(next);
   f.context.scope='changed';complete(spanish);await accountRejected;
  }finally{f.cleanup();}
 });
+
+test('cached translations require eligible saved card and stale entitlement cannot invoke AI',async()=>{
+ const f=await fixture();let calls=0;
+ try{
+  const service=new ReviewTranslations(()=>f.context,async()=>{calls++;return spanish;});await service.get(f.id);
+  f.store.addWords(f.deck.id,Array.from({length:10},(_,i)=>({word:'earlier'+i,translation:'value'})));
+  const snap=f.store.snapshot();f.store.applySync(snap.decks,snap.words.map(w=>({...w,createdAt:w.id===f.id?'2026-01-02':'2026-01-01'})),[]);
+  f.context.account.entitlement!.status='revoked';f.store.bindAccess(()=>f.context.account);
+  await assert.rejects(service.get(f.id));assert.equal(calls,1);
+  f.context.account.entitlement!.status='premium';f.context.account.entitlement!.expires_at=new Date(Date.now()-1000).toISOString();
+  await assert.rejects(service.preview('new-word','ru','en-us'));assert.equal(calls,1);
+ }finally{f.cleanup();}
+});
+
+test('late AI response cannot populate cache after authority expires',async()=>{
+ const f=await fixture();let complete!:(value:unknown)=>void;
+ try{const service=new ReviewTranslations(()=>f.context,()=>new Promise(resolve=>complete=resolve));const pending=service.get(f.id),rejected=assert.rejects(pending);f.context.account.entitlement!.status='revoked';complete(spanish);await rejected;
+ }finally{f.cleanup();}
+});
diff --git a/tests/test-mode-ui.test.ts b/tests/test-mode-ui.test.ts
index 0cc88ad..98364b5 100644
--- a/tests/test-mode-ui.test.ts
+++ b/tests/test-mode-ui.test.ts
@@ -13,12 +13,16 @@ test('test mode profile suppresses purchase promotion while preserving account s
  const disabled=render(false);assert.match(disabled,/Link your verified Apple purchase/);assert.doesNotMatch(disabled,/Test mode/);
 });
 
 test('test mode help describes account requirements without purchase instructions',()=>{
  const html=renderToStaticMarkup(createElement(Help,{testMode:true,onCreate:()=>{},onProfile:()=>{},onSettings:()=>{}}));
  assert.match(html,/Test mode/);assert.match(html,/signed-in account/);assert.doesNotMatch(html,/confirm your purchase through Apple|Get AI translations with Premium/);
 });
 
 test('signed-in sync conflicts expose cloud recovery while normal sync hides it',()=>{
  const render=(state:'conflict'|'synced')=>renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'A',email:'a@example.test',provider:'email'},entitlement:null,testMode:false,sync:{state}},onChanged:async()=>{},notify:()=>{}})}));
  assert.match(render('conflict'),/Use cloud version/);assert.doesNotMatch(render('synced'),/Use cloud version/);
 });
+
+test('profile does not label elapsed premium expiry as active',()=>{
+ const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'a',email:null},testMode:false,entitlement:{status:'premium',expires_at:new Date(Date.now()-1000).toISOString(),checked_at:new Date().toISOString(),is_trial:false,auto_renew:false,was_ever_paid:true}},onChanged:async()=>{},notify:()=>{}})}));assert.doesNotMatch(html,/Premium is active/);
+});
