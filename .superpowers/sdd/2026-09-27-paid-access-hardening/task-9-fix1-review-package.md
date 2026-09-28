# Task9 fix round1 review package
f0a08c0 fix: order entitlement refreshes and guard cached draft previews
 electron/api.ts                   | 18 ++++++++++++++----
 electron/review-translations.ts   |  5 ++++-
 src/Profile.tsx                   |  2 +-
 tests/api-test-mode.test.ts       | 22 ++++++++++++++++++++--
 tests/review-translations.test.ts | 13 +++++++++++++
 tests/test-mode-ui.test.ts        |  4 ++++
 6 files changed, 56 insertions(+), 8 deletions(-)
diff --git a/electron/api.ts b/electron/api.ts
index 5641709..7e42143 100644
--- a/electron/api.ts
+++ b/electron/api.ts
@@ -3,25 +3,25 @@ import { safeStorage } from 'electron';
 import { existsSync,readFileSync,writeFileSync,renameSync,unlinkSync } from 'node:fs';
 import type { AccountState,Profile,Entitlement,Draft,CatalogDeck,ReviewTranslation,ReviewTranslationRequest } from '../shared/types';
 import {FeatureFlags} from './feature-flags';
 export class ApiError extends Error {
  constructor(message:string,readonly status:number,readonly code?:string,readonly retryAfterSeconds?:number){super(message);}
 }
 export function retryAfterSeconds(value:string|null,now=Date.now()):number|undefined{
  if(!value)return undefined;if(/^\d+$/.test(value.trim()))return Number(value.trim());const date=Date.parse(value);return Number.isFinite(date)?Math.max(0,Math.ceil((date-now)/1000)):undefined;
 }
 interface Session {access_token:string;access_token_expires_at:string;refresh_token:string;profile:Profile}
 export class Api {
  private featureFlags:FeatureFlags;private selectedOrigin:string;
- private session:Session|null=null; private entitlement:Entitlement|null=null; private generation=0; private refreshing:Promise<void>|null=null;
+ private session:Session|null=null; private entitlement:Entitlement|null=null; private generation=0; private entitlementRevision=0; private refreshing:Promise<void>|null=null;
  constructor(private tokenPath:string,private base:()=>string){
   this.selectedOrigin=new URL(base()).origin;
   this.featureFlags=new FeatureFlags(tokenPath+'.features.json',base);
   if(existsSync(tokenPath)&&safeStorage.isEncryptionAvailable())try{const stored=JSON.parse(safeStorage.decryptString(readFileSync(tokenPath)));if(stored.base===this.base())this.session=stored.session;}catch{/* An unreadable token never grants access. */}
  }
  invalidateOrigin(){this.selectedOrigin=new URL(this.base()).origin;this.clear();this.featureFlags.invalidate();}
  state():AccountState{if(new URL(this.base()).origin!==this.selectedOrigin)this.invalidateOrigin();return {profile:this.session?.profile??null,entitlement:this.entitlement,testMode:this.featureFlags.testMode};}
  async loginGoogle(authorize:()=>Promise<string|null>,signal?:AbortSignal){
   const generation=++this.generation;
   const idToken=await authorize();
   signal?.throwIfAborted();
   if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');
@@ -35,38 +35,48 @@ export class Api {
  private save(session:Session){if(!safeStorage.isEncryptionAvailable())throw new Error('Windows secure credential storage is unavailable. Sign-in was not saved.');const tmp=this.tokenPath+'.tmp';writeFileSync(tmp,safeStorage.encryptString(JSON.stringify({base:this.base(),session})));renameSync(tmp,this.tokenPath);this.session=session;}
  clear(){this.generation++;this.session=null;this.entitlement=null;if(existsSync(this.tokenPath))unlinkSync(this.tokenPath);}
  private async send(path:string,method:string,body:unknown,token?:string,signal?:AbortSignal):Promise<Response>{
   const url=new URL(this.base());if(url.protocol!=='https:'&&!(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)))throw new Error('Use HTTPS, or localhost for a development server.');
   return fetch(new URL(path,url),{method,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{})},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.any([AbortSignal.timeout(30000),...(signal?[signal]:[])]),redirect:'error'});
  }
  private async result<T>(response:Response):Promise<T>{if(response.status===204)return undefined as T;const text=await response.text();let data:any;try{data=JSON.parse(text);}catch{if(response.ok)throw new Error(`Server returned an unreadable response (${response.status}).`);data=null;}if(!response.ok){const retry=retryAfterSeconds(response.headers.get('Retry-After'));const code=data?.code;const message=response.status===402?'An active shared Premium subscription is needed. Link your Apple purchase in Owl AI on iPhone.':response.status===429?(retry===undefined?'You have reached the request limit. Please try again later.':`You have reached the request limit. Try again in ${retry} seconds.`):response.status===503?(code==='subscription_reconciliation_required'?'Your purchase needs verification to recover access. Contact support; your saved content is preserved.':'The service is temporarily unavailable. Please try again later.'):response.status===404?'The server must be updated before Windows sign-in and synchronization are available. Please contact support.':data?.error??data?.title??`Request failed (${response.status}).`;throw new ApiError(message,response.status,code,retry);}return data as T;}
  async login(email:string,password:string){const generation=++this.generation;const response=await this.send('/owlai/account/desktop/email/session','POST',{email,password});const session=await this.result<Session>(response);if(generation!==this.generation)throw new Error('The account changed. Please sign in again.');this.entitlement=null;this.save(session);return this.state();}
  private async refresh(){if(this.refreshing)return this.refreshing;const session=this.session;if(!session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;
   this.refreshing=(async()=>{const response=await this.send('/owlai/account/session/refresh','POST',{refresh_token:session.refresh_token});if(generation!==this.generation)return;if(response.status===401){this.clear();throw new Error('Your session expired. Please sign in again.');}const renewed=await this.result<Session>(response);if(generation===this.generation)this.save(renewed);})().finally(()=>{this.refreshing=null;});return this.refreshing;
  }
  async request<T>(path:string,method='POST',body?:unknown,signal?:AbortSignal):Promise<T>{
-  this.state();if(!this.session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation;
+  this.state();if(!this.session)throw new Error('Sign in to your Owl AI account to continue.');const generation=this.generation,authorityRevision=path==='/owlai/account/entitlement'?this.entitlementRevision:null;
   if(new Date(this.session.access_token_expires_at).getTime()<Date.now()+30000)await this.refresh();
   if(generation!==this.generation||!this.session)throw new Error('Your account session changed.');
   let response=await this.send(path,method,body,this.session.access_token,signal);
   if(response.status===401){
    if(path.startsWith('/owlai/account/ai/'))throw new ApiError('Your session needs refreshing. Sign in or refresh your profile, then try again.',401);
    await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your session expired.');response=await this.send(path,method,body,this.session.access_token,signal);
   }
   try{
    const value=await this.result<T>(response);if(generation!==this.generation)throw new Error('Your account session changed.');return value;
   }catch(error){
    // The response body may complete after an account/origin transition.
-   if(generation===this.generation&&error instanceof ApiError){
+   if(generation===this.generation&&(authorityRevision===null||authorityRevision===this.entitlementRevision)&&error instanceof ApiError){
     if(error.code==='subscription_reconciliation_required')this.entitlement={status:'invalid_subscription',is_trial:false,auto_renew:false,was_ever_paid:false,checked_at:new Date().toISOString()};
     else if([402,503].includes(error.status))this.entitlement=null;
    }
    throw error;
   }
  }
- async refreshEntitlement(){await this.featureFlags.refresh();if(!this.session)return this.state();const generation=this.generation;try{const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');if(generation===this.generation)this.entitlement={...ent,checked_at:new Date().toISOString()};return this.state();}catch(error){if(generation===this.generation&&!(error instanceof ApiError&&error.code==='subscription_reconciliation_required'))this.entitlement=null;throw error;}}
+ async refreshEntitlement(){
+  const revision=++this.entitlementRevision;await this.featureFlags.refresh();
+  if(revision!==this.entitlementRevision||!this.session)return this.state();const generation=this.generation;
+  try{
+   const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');
+   if(generation===this.generation&&revision===this.entitlementRevision)this.entitlement={...ent,checked_at:new Date().toISOString()};return this.state();
+  }catch(error){
+   if(generation===this.generation&&revision===this.entitlementRevision&&!(error instanceof ApiError&&error.code==='subscription_reconciliation_required'))this.entitlement=null;
+   throw error;
+  }
+ }
  async logout(){const refresh=this.session?.refresh_token;this.clear();if(refresh)try{await this.send('/owlai/account/session/logout','POST',{refresh_token:refresh});}catch{/* Local logout succeeds offline; server session expires normally. */}}
  async deleteAccount(){await this.request('/owlai/account','DELETE');this.clear();}
  async translate(word:string,native:string,learning:string):Promise<Draft>{const result=await this.request<any>('/owlai/account/ai/word-detail','POST',{word,native_language:native,learning_language:learning});const translation=result.translations?.join('; ')??result.translation;if(typeof translation!=='string'||!translation.trim())throw new Error('No translation returned. Try a different word.');return {word:result.corrected_word??word,translation,partOfSpeech:partOfSpeech(result.part_of_speech),pronunciation:result.pronunciation,examples:result.examples};}
  reviewTranslation(input:ReviewTranslationRequest){return this.request<ReviewTranslation>('/owlai/account/ai/review-translation','POST',input);}
  catalog(query:string){return this.request<CatalogDeck[]>('/owlai/account/public-flashcard-sets/catalog','POST',{query,limit:40});}
 }
diff --git a/electron/review-translations.ts b/electron/review-translations.ts
index cbcac7d..ea6e71e 100644
--- a/electron/review-translations.ts
+++ b/electron/review-translations.ts
@@ -26,25 +26,28 @@ function validated(value:unknown,language:string):ReviewTranslation {
  if(!item||canonicalLanguage(item.language_code)!==language||typeof item.translation!=='string'||!item.translation.trim()||item.translation.length>5000||typeof item.explanation!=='string'||!item.explanation.trim()||item.explanation.length>5000)
   throw new Error('The second-language translation was incomplete. Please try again.');
  return {language_code:language,translation:item.translation.trim(),explanation:item.explanation.trim()};
 }
 
 export class ReviewTranslations {
  private pending=new Map<string,Promise<ReviewTranslation>>();
  constructor(private current:()=>TranslationWorkspace,private request:(input:ReviewTranslationRequest)=>Promise<unknown>){}
  async get(wordId:string):Promise<ReviewTranslation>{
   return this.load(workspace=>context(workspace,wordId));
  }
  async preview(word:string,native:string,learning:string):Promise<ReviewTranslation>{
-  return this.load(workspace=>previewContext(workspace,word,native,learning));
+  return this.load(workspace=>{
+   const access=reviewTranslationAccess(workspace.account);if(access)throw new Error(access);
+   return previewContext(workspace,word,native,learning);
+  });
  }
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
diff --git a/src/Profile.tsx b/src/Profile.tsx
index 60502f8..65be20e 100644
--- a/src/Profile.tsx
+++ b/src/Profile.tsx
@@ -7,17 +7,17 @@ import { Modal,errorMessage } from './components';
 export default function Profile({account,onChanged,notify}:{account:AccountState;onChanged:()=>Promise<void>;notify:(message:string)=>void}){
  const owl=useOwl();
  // Cancellation is unscoped: only leaving the profile cancels, not refreshing its IPC bridge.
  useEffect(()=>()=>{void owl.cancelGoogleLogin().catch(()=>{});},[]);
  const [googlePending,setGooglePending]=useState(false),[email,setEmail]=useState(''),[password,setPassword]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState(''),[deleting,setDeleting]=useState(false),[replacingWithCloud,setReplacingWithCloud]=useState(false);
  const run=async(fn:()=>Promise<void>)=>{setBusy(true);setError('');try{await fn();}catch(e){setError(errorMessage(e));}finally{try{await onChanged();}catch(e){setError(errorMessage(e));}setBusy(false);}};
  const premium=activeAccess(account);const needsRefresh=['premium','trial','grace'].includes(account.entitlement?.status??'');const periodEnded=Date.parse(account.entitlement?.expires_at??'')<=Date.now();
  const sync=account.sync;
  const syncLabel=sync?.state==='syncing'?'Syncing your cards…':sync?.state==='synced'?'Your cards are synced':sync?.state==='conflict'?'Some changes need attention':sync?.state==='error'?'Sync could not finish':'Ready to sync';
  return <div className="profile-layout"><section className="panel"><span className="eyebrow">YOUR OWL AI ACCOUNT</span><h2>{account.profile?'One account. Your learning space.':'Your iPhone account, on Windows.'}</h2><p className="muted">{account.profile?'Your collections, cards, and review schedule sync with this same account on iPhone. Your signed-out cards stay in a separate space on this computer.':'First create or open your account in Owl AI on iPhone. Then sign in here using the same email or Google account. Windows does not create new accounts.'}</p>{error&&<p className="error" role="alert">{error}</p>}
  {account.profile?<><div className="profile-person"><div className="avatar large">{(account.profile.email??'O').charAt(0).toUpperCase()}</div><div><h3>{account.profile.email??account.profile.display_name??'Owl AI learner'}</h3><span className="muted small">Your private account workspace</span></div><ShieldCheck className="accent" size={23}/></div>
  <section aria-label="Account synchronization"><h3>{syncLabel}</h3><p className="small muted" role="status">{sync?.message??(sync?.lastSyncedAt?`Last synced ${new Date(sync.lastSyncedAt).toLocaleString()}.`:'Changes sync automatically while you are online. Only data belonging to this account is included.')}</p><p className="small muted">Signed-out cards are never uploaded automatically. Sign out to return to them.</p>{sync?.state==='conflict'&&<button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(true)}>Use cloud version</button>}</section>
- <button className="button secondary" disabled={busy} onClick={()=>run(async()=>{await owl.logout();notify('Signed out. Returned to your separate local workspace.');})}><LogOut size={16}/> Sign out</button><div className="danger-zone"><h3>Delete account</h3><p className="muted small">Deletes your server account, synced data, and shared access. Your separate signed-out workspace is preserved. Apple billing must be canceled separately.</p><button className="text-button danger" disabled={busy} onClick={()=>setDeleting(true)}>Delete my account</button></div></>:<form onSubmit={e=>{e.preventDefault();void run(async()=>{await owl.login(email,password);setPassword('');notify('Welcome to your Owl AI account.');});}}><button type="button" className="button secondary full google-sign-in" disabled={busy} onClick={()=>run(async()=>{setGooglePending(true);try{const result=await owl.loginGoogle();if(result)notify('Welcome to your Owl AI account.');}finally{setGooglePending(false);}})}><span aria-hidden="true" className="google-letter">G</span> Continue with Google</button>{googlePending&&<div className="google-wait"><p className="small muted">Continue in your browser with the Google account you use in Owl AI on iPhone.</p><button type="button" className="text-button" onClick={()=>owl.cancelGoogleLogin().catch(e=>setError(errorMessage(e)))}>Cancel Google sign-in</button></div>}<p className="small muted center">or sign in with email</p><label className="field">Email<input type="email" autoComplete="email" required value={email} onChange={e=>setEmail(e.target.value)} placeholder="you@example.com"/></label><label className="field">Password<input type="password" autoComplete="current-password" required value={password} onChange={e=>setPassword(e.target.value)} placeholder="Your password"/></label><button className="button full" disabled={busy}>{busy?'Connecting…':'Sign in'}<ArrowRight size={17}/></button><p className="small muted">You can still create and review local cards without signing in. These cards are kept separate from your account.</p></form>}</section>
+ <button className="button secondary" disabled={busy} onClick={()=>run(async()=>{await owl.logout();notify('Signed out. Returned to your separate local workspace.');})}><LogOut size={16}/> Sign out</button><div className="danger-zone"><h3>Delete account</h3><p className="muted small">Deletes your server account, synced data, and shared access. Your separate signed-out workspace is preserved. Apple billing must be canceled separately.</p><button className="text-button danger" disabled={busy} onClick={()=>setDeleting(true)}>Delete my account</button></div></>:<form onSubmit={e=>{e.preventDefault();void run(async()=>{await owl.login(email,password);setPassword('');notify('Welcome to your Owl AI account.');});}}><button type="button" className="button secondary full google-sign-in" disabled={busy} onClick={()=>run(async()=>{setGooglePending(true);try{const result=await owl.loginGoogle();if(result)notify('Welcome to your Owl AI account.');}finally{setGooglePending(false);}})}><span aria-hidden="true" className="google-letter">G</span> Continue with Google</button>{googlePending&&<div className="google-wait"><p className="small muted">Continue in your browser with the Google account you use in Owl AI on iPhone.</p><button type="button" className="text-button" onClick={()=>owl.cancelGoogleLogin().catch(e=>setError(errorMessage(e)))}>Cancel Google sign-in</button></div>}<p className="small muted center">or sign in with email</p><label className="field">Email<input type="email" autoComplete="email" required value={email} onChange={e=>setEmail(e.target.value)} placeholder="you@example.com"/></label><label className="field">Password<input type="password" autoComplete="current-password" required value={password} onChange={e=>setPassword(e.target.value)} placeholder="Your password"/></label><button className="button full" disabled={busy}>{busy?'Connecting…':'Sign in'}<ArrowRight size={17}/></button><p className="small muted">Sign in to create or add cards on desktop. You can still review eligible saved local cards in this separate workspace.</p></form>}</section>
  {account.testMode?<section className="premium-panel"><div className="premium-mark"><Check size={25}/><span>Test mode</span></div><h1>All learning features are available.</h1><p>Word limits and AI quotas are lifted while test mode is active. Sign in to use online features.</p></section>:<section className="premium-panel"><div className="premium-mark"><Crown size={25}/><span>OWL AI PREMIUM</span></div><h1>One subscription.<br/>More places to grow.</h1><p>Your iPhone subscription comes with you to Windows.</p><div className="devices"><Smartphone size={42}/><Link2 size={23}/><Monitor size={52}/></div><div className="premium-status"><span>{premium?'Premium is active':needsRefresh?(periodEnded?'Subscription period ended':'Refresh subscription status'):account.entitlement?account.entitlement.status.replace(/_/g,' '):'Connect your subscription'}</span>{premium&&<Check size={19}/>}</div>{account.entitlement?.expires_at&&<p className="small">{account.entitlement.auto_renew?'Current period ends':'Access until'} {new Date(account.entitlement.expires_at).toLocaleDateString()}</p>}<ol className="steps"><li>Open Owl AI on iPhone and sign in to this account.</li><li>Link your verified Apple purchase in Profile.</li><li>Your Premium updates automatically on both devices.</li></ol><p className="small premium-note">No second purchase needed. Use the same account on both devices for your cards and Premium.</p></section>}
  {replacingWithCloud&&<Modal title="Use cloud version?" onClose={()=>!busy&&setReplacingWithCloud(false)}><p>This replaces your local account cards and review schedule with the server copy. Unsynced local edits will be removed from this workspace.</p><p>A recoverable backup is saved on this computer before replacement. You can restore it from Settings → Restore backup while signed into this account.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(false)}>Keep local cards</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{const result=await owl.resolveSync();setReplacingWithCloud(false);notify(result.message??'Cloud version restored.');})}>Use cloud version</button></div></Modal>}
  {deleting&&<Modal title="Delete your Owl AI account?" onClose={()=>!busy&&setDeleting(false)}><p>This removes your server account, synced data, and shared subscription access. A linked purchase cannot be automatically attached to a new account.</p><p>Your separate signed-out workspace is preserved. Deleting your account does not cancel Apple billing.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setDeleting(false)}>Keep account</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{await owl.deleteAccount();setDeleting(false);notify('Account deleted. Returned to your separate local workspace.');})}>Delete account</button></div></Modal>}</div>;
 }
diff --git a/tests/api-test-mode.test.ts b/tests/api-test-mode.test.ts
index bc7360a..0b380fc 100644
--- a/tests/api-test-mode.test.ts
+++ b/tests/api-test-mode.test.ts
@@ -24,26 +24,26 @@ test('signed-out API refresh exposes test mode without inventing a subscription
   const restarted=new Api(path,base);assert.equal(restarted.state().testMode,false);
   enabled=false;assert.deepEqual(await restarted.refreshEntitlement(),{profile:null,entitlement:null,testMode:false});
  }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
 });
 
 test('HTTP refusals retain status and standard retry timing without replaying AI calls',async()=>{
  const root=mkdtempSync(join(tmpdir(),'owl-api-errors-'));let status=429,code='quota',calls=0,retry='120';
  const server=createServer((request,response)=>{calls++;response.statusCode=status;response.setHeader('Content-Type','application/json');if(retry)response.setHeader('Retry-After',retry);response.end(JSON.stringify({code}));});
  await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
  try{
   const address=server.address();assert.ok(address&&typeof address!=='string');const api=new Api(join(root,'account.enc'),()=>`http://127.0.0.1:${address.port}`);
   (api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:'fake',profile:{id:'a',email:null}};
-  for(const value of [429,402,503]){status=value;code=value===503?'subscription_reconciliation_required':'quota';const before=calls;
-   await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),(error:any)=>{assert.equal(error.status,value);if(value===429){assert.equal(error.retryAfterSeconds,120);assert.match(error.message,/120/);}if(value===503){assert.match(error.message,/support|recover/i);assert.equal(api.state().entitlement?.status,'invalid_subscription');}return true;});assert.equal(calls,before+1);
+  for(const value of [429,402,503]){(api as any).entitlement={status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false};status=value;code=value===503?'subscription_reconciliation_required':'quota';const before=calls;
+   await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),(error:any)=>{assert.equal(error.status,value);if(value===402)assert.equal(api.state().entitlement,null);if(value===429){assert.equal(api.state().entitlement?.status,'premium');assert.equal(error.retryAfterSeconds,120);assert.match(error.message,/120/);}if(value===503){assert.match(error.message,/support|recover/i);assert.equal(api.state().entitlement?.status,'invalid_subscription');}return true;});assert.equal(calls,before+1);
   }
   status=503;code='apple_temporarily_unavailable';retry='';await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),/temporarily unavailable/i);
   status=401;const before=calls;await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}));assert.equal(calls,before+1,'AI denial is never automatically replayed');
  }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
 });
 
 import {retryAfterSeconds} from '../electron/api';
 test('standard Retry-After dates parse without guessing missing or malformed timing',()=>{
  const now=Date.parse('2026-09-27T12:00:00Z');assert.equal(retryAfterSeconds('Sun, 27 Sep 2026 12:02:00 GMT',now),120);assert.equal(retryAfterSeconds(null,now),undefined);assert.equal(retryAfterSeconds('bad',now),undefined);
 });
 test('selected origin invalidation rejects late entitlement replies even on a round trip',async()=>{
  const root=mkdtempSync(join(tmpdir(),'owl-generation-'));let release!:()=>void;
@@ -54,12 +54,30 @@ test('selected origin invalidation rejects late entitlement replies even on a ro
  }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
 });
 
 test('a delayed error body cannot revoke the new account entitlement',async()=>{
  const root=mkdtempSync(join(tmpdir(),'owl-body-')),original=globalThis.fetch;let finish!:(text:string)=>void;
  try{globalThis.fetch=async()=>new Response(new ReadableStream({start(controller){finish=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}),{status:503});
  const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');const session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'old',email:null}};(api as any).session=session;
  const pending=api.request('/owlai/account/ai/word-detail','POST',{}),rejected=assert.rejects(pending);await new Promise(resolve=>setTimeout(resolve,0));api.clear();(api as any).session={...session,profile:{id:'new',email:null}};
  const entitlement={status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false};(api as any).entitlement=entitlement;
  finish(JSON.stringify({code:'subscription_reconciliation_required'}));await rejected;assert.deepEqual(api.state().entitlement,entitlement);
  }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
 });
+
+import {evaluateAccess} from '../shared/access-policy';
+for(const olderResult of ['premium','temporary','reconciliation'] as const)test('newer same-account authority wins over earlier '+olderResult,async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-refresh-order-')),original=globalThis.fetch;
+ try{
+   const replies:((response:Response)=>void)[]=[];
+   globalThis.fetch=async input=>String(input).includes('feature-flags')?Response.json({test_mode:false}):new Promise<Response>(resolve=>replies.push(resolve));
+   const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');(api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'a',email:null}};
+   const entitlement=(status:string)=>({status,expires_at:new Date(Date.now()+86400000).toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false});
+   const older=api.refreshEntitlement();const handled=older.catch(()=>{});while(replies.length<1)await new Promise(resolve=>setTimeout(resolve,0));
+   let finishBody!:(text:string)=>void;replies[0](new Response(new ReadableStream({start(controller){finishBody=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}),{status:olderResult==='premium'?200:503}));await new Promise(resolve=>setTimeout(resolve,0));
+   const newer=api.refreshEntitlement();while(replies.length<2)await new Promise(resolve=>setTimeout(resolve,0));
+   const newest=olderResult==='premium'?'revoked':'premium';replies[1](Response.json(entitlement(newest)));await newer;
+   const before=api.state().entitlement;
+   finishBody(JSON.stringify(olderResult==='premium'?entitlement('premium'):{code:olderResult==='reconciliation'?'subscription_reconciliation_required':'temporary'}));await handled;
+   assert.deepEqual(api.state().entitlement,before,olderResult+' must not overwrite the newer accepted response');assert.equal(evaluateAccess(api.state(),'add',101).allow,newest==='premium');
+ }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
+});
diff --git a/tests/review-translations.test.ts b/tests/review-translations.test.ts
index 2424370..16a8142 100644
--- a/tests/review-translations.test.ts
+++ b/tests/review-translations.test.ts
@@ -122,12 +122,25 @@ test('cached translations require eligible saved card and stale entitlement cann
   f.context.account.entitlement!.status='revoked';f.store.bindAccess(()=>f.context.account);
   await assert.rejects(service.get(f.id));assert.equal(calls,1);
   f.context.account.entitlement!.status='premium';f.context.account.entitlement!.expires_at=new Date(Date.now()-1000).toISOString();
   await assert.rejects(service.preview('new-word','ru','en-us'));assert.equal(calls,1);
  }finally{f.cleanup();}
 });
 
 test('late AI response cannot populate cache after authority expires',async()=>{
  const f=await fixture();let complete!:(value:unknown)=>void;
  try{const service=new ReviewTranslations(()=>f.context,()=>new Promise(resolve=>complete=resolve));const pending=service.get(f.id),rejected=assert.rejects(pending);f.context.account.entitlement!.status='revoked';complete(spanish);await rejected;
  }finally{f.cleanup();}
 });
+
+test('draft preview cannot bypass saved-card cache restrictions after paid access ends',async()=>{
+ const f=await fixture();let calls=0;
+ try{
+  f.store.addWords(f.deck.id,Array.from({length:10},(_,i)=>({word:'earlier'+i,translation:'value'})));const snap=f.store.snapshot();f.store.applySync(snap.decks,snap.words.map(w=>({...w,createdAt:w.id===f.id?'2026-01-02':'2026-01-01'})),[]);f.store.bindAccess(()=>f.context.account);
+  const service=new ReviewTranslations(()=>f.context,async()=>{calls++;return spanish;});await service.get(f.id);
+  f.context.account.entitlement!.status='expired_paid';assert.deepEqual(await service.get(f.id),spanish,'Eligible saved paid cache remains readable');
+  await assert.rejects(service.preview('journey','ru','en-us'));
+  f.context.account.entitlement!.status='revoked';await assert.rejects(service.get(f.id));await assert.rejects(service.preview('journey','ru','en-us'));
+  f.context.account.entitlement!.status='premium';f.context.account.entitlement!.expires_at=new Date(Date.now()-1000).toISOString();await assert.rejects(service.preview('journey','ru','en-us'));
+  f.context.account.profile=null;await assert.rejects(service.preview('journey','ru','en-us'));assert.equal(calls,1);
+ }finally{f.cleanup();}
+});
diff --git a/tests/test-mode-ui.test.ts b/tests/test-mode-ui.test.ts
index 98364b5..f46d3ae 100644
--- a/tests/test-mode-ui.test.ts
+++ b/tests/test-mode-ui.test.ts
@@ -17,12 +17,16 @@ test('test mode help describes account requirements without purchase instruction
  const html=renderToStaticMarkup(createElement(Help,{testMode:true,onCreate:()=>{},onProfile:()=>{},onSettings:()=>{}}));
  assert.match(html,/Test mode/);assert.match(html,/signed-in account/);assert.doesNotMatch(html,/confirm your purchase through Apple|Get AI translations with Premium/);
 });
 
 test('signed-in sync conflicts expose cloud recovery while normal sync hides it',()=>{
  const render=(state:'conflict'|'synced')=>renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'A',email:'a@example.test',provider:'email'},entitlement:null,testMode:false,sync:{state}},onChanged:async()=>{},notify:()=>{}})}));
  assert.match(render('conflict'),/Use cloud version/);assert.doesNotMatch(render('synced'),/Use cloud version/);
 });
 
 test('profile does not label elapsed premium expiry as active',()=>{
  const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'a',email:null},testMode:false,entitlement:{status:'premium',expires_at:new Date(Date.now()-1000).toISOString(),checked_at:new Date().toISOString(),is_trial:false,auto_renew:false,was_ever_paid:true}},onChanged:async()=>{},notify:()=>{}})}));assert.doesNotMatch(html,/Premium is active/);
 });
+
+test('signed-out desktop profile requires sign-in to create and keeps eligible saved reviews',()=>{
+ const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:null,entitlement:null,testMode:false},onChanged:async()=>{},notify:()=>{}})}));assert.match(html,/Sign in to create or add cards on desktop/);assert.match(html,/review eligible saved local cards/);assert.doesNotMatch(html,/create and review local cards without signing in/);
+});
