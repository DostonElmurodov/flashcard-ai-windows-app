# Task 11 Windows review package
Base: 43ba1668a4205882b8565fa0720e7fa3fb97d67c
Head: ac37325c3cae2cef5d2adaddbaa214f213a25892
Workspace: C:/Users/ForDo/.codex/worktrees/paid-access-hardening/FlashcardAI

## Commits
ac37325 Clarify paid access copy and add test-only Windows validation

## Summary
 .github/workflows/windows-validation.yml | 37 ++++++++++++++++++++++++++++++++
 src/Help.tsx                             | 11 +++++-----
 src/Profile.tsx                          |  4 ++--
 tests/test-mode-ui.test.ts               |  2 +-
 4 files changed, 45 insertions(+), 9 deletions(-)

## Full diff
diff --git a/.github/workflows/windows-validation.yml b/.github/workflows/windows-validation.yml
new file mode 100644
index 0000000..2bffffa
--- /dev/null
+++ b/.github/workflows/windows-validation.yml
@@ -0,0 +1,37 @@
+name: Windows validation
+
+on:
+  workflow_dispatch:
+
+permissions:
+  contents: read
+
+jobs:
+  test:
+    runs-on: windows-latest
+    timeout-minutes: 45
+    steps:
+      - uses: actions/checkout@v4
+      - name: Record tested source
+        run: git rev-parse HEAD
+      - uses: actions/setup-node@v4
+        with:
+          node-version: '22'
+          cache: npm
+      - name: Install locked dependencies
+        run: npm ci
+      - name: Record tool versions
+        run: |
+          node --version
+          npm --version
+          npm ls electron --depth=0
+      - name: Unit and rendered component tests
+        run: npm test
+      - name: Typecheck
+        run: npm run typecheck
+      - name: Build application
+        run: npm run build
+      - name: Fake-only paid access Electron smoke
+        env:
+          OWL_ACCESS_RESULTS: ${{ runner.temp }}/owl-paid-access
+        run: npx --no-install tsx scripts/paid-access-smoke.ts
diff --git a/src/Help.tsx b/src/Help.tsx
index c211257..0bb629f 100644
--- a/src/Help.tsx
+++ b/src/Help.tsx
@@ -1,53 +1,52 @@
 import { ArrowRight,BookOpen,Crown,GraduationCap } from 'lucide-react';
 
 export default function Help({testMode=false,onCreate,onProfile,onSettings}:{testMode?:boolean;onCreate:()=>void;onProfile:()=>void;onSettings:()=>void}){
  return <div className="help-page">
   <section className="panel help-start">
    <div className="panel-title"><GraduationCap size={20}/><h2>Learn a little. Remember for longer.</h2></div>
    <p>Owl AI helps you remember words with flashcards. Try to recall a word, check the answer, and tell the app how easy it felt. Owl AI uses your answers to decide when to show that card again.</p>
    <ol className="help-steps">
     <li><strong>Create a set.</strong> Give it a name, choose your languages, and select <b>Save set</b>. A set is simply a collection of cards.</li>
     <li><strong>Add your words.</strong> Select your collection on Learn, then <b>Add cards</b>. Enter a word and its translation and select <b>Save cards</b>. The form stays open for your next word.</li>
     <li><strong>Practice on Learn.</strong> Select <b>Start today’s practice</b>. Think of the answer before selecting <b>Show answer</b>, then choose Again, Hard, Good, or Easy.</li>
    </ol>
    <p className="help-tip">Again = I forgot · Hard = I struggled · Good = I remembered · Easy = I knew it straight away.</p>
    <button className="button" onClick={onCreate}>Create a set <ArrowRight size={16}/></button>
   </section>
   <div className="help-columns">
    <section className="panel">
     <div className="panel-title"><BookOpen size={20}/><h2>What you can do</h2></div>
     <ul className="help-features">
-     <li><strong>Create and study for free.</strong> Write cards, organize sets, and review saved cards offline. No account is needed for local study.</li>
+     <li><strong>Study for free.</strong> Free access includes up to 10 active words across all languages. Eligible saved cards can be reviewed offline. Sign in on Windows to create or add cards; iPhone local study does not require an Owl AI account.</li>
      <li><strong>Bring in existing words.</strong> Paste text or import TXT, CSV, TSV, PDF, and image files. Check the preview, complete missing translations, then select Save selected cards.</li>
      <li><strong>Make cards useful to you.</strong> Edit words, translations, pronunciation, examples, and personal notes. Listen using the speaker button when a voice is available.</li>
      <li><strong>See your progress.</strong> Learn shows words collected, cards ready to review, cards practiced today, and your day streak.</li>
      <li><strong>Discover and share sets.</strong> Sign in with your existing iPhone account to use the online library. Imported sets are saved in your current workspace. Publish a set from its menu in My sets.</li>
      <li><strong>{testMode?'Get AI translations.':'Get AI translations with Premium.'}</strong> Choose AI translation when adding cards. {testMode?'An internet connection and a signed-in account are required.':'An internet connection and a signed-in account with Premium are required.'} Check the result before saving.</li>
      <li><strong>Make it comfortable.</strong> Set your languages, daily new-card goal, review direction, theme, and reminders in Preferences.</li>
     </ul>
    </section>
-   {testMode?<section className="panel help-payment"><div className="panel-title"><GraduationCap size={20}/><h2>Test mode</h2></div><p>All learning features are available without a subscription while test mode is active. Word limits and AI quotas are lifted. Sign in to use online features and sync your cards.</p><button className="button secondary" onClick={onProfile}>Open Account <ArrowRight size={16}/></button></section>:<section className="panel help-payment">
+   {testMode?<section className="panel help-payment"><div className="panel-title"><GraduationCap size={20}/><h2>Test mode</h2></div><p>Learning features are available without a subscription in test mode. Sign in to use online features and sync your cards. AI use is still subject to service availability and limits.</p><button className="button secondary" onClick={onProfile}>Open Account <ArrowRight size={16}/></button></section>:<section className="panel help-payment">
     <div className="panel-title"><Crown size={20}/><h2>Premium & payment</h2></div>
-    <p>You can keep creating and reviewing local cards for free. Premium adds online AI translation and can be shared between iPhone and Windows through your Owl AI account.</p>
+    <p>Free access includes up to 10 active words across all languages. Saved cards remain available to view and review after a paid period ends; adding or editing content and requesting new AI work need current access. Premium can be shared between iPhone and Windows through your Owl AI account.</p>
     <ol className="help-steps">
-     <li>Create or open your account in Owl AI on iPhone first. Windows supports signing in to that existing account.</li>
-     <li>Open the Premium purchase screen. Review the available plan, price, and renewal terms, then confirm your purchase through Apple.</li>
-     <li>In the iPhone app’s Profile, link your Apple purchase to your Owl AI account. If you already paid, restore your purchase when needed.</li>
+     <li>On iPhone, open the Premium purchase screen. Review the available plan, price, and renewal terms, then confirm your purchase through Apple. An Owl AI account is not required to buy or use it on iPhone.</li>
+     <li>To share Premium with Windows, create or open your Owl AI account on iPhone and link your verified Apple purchase in Profile. If you already paid, restore your purchase when needed.</li>
      <li>Sign in to the same account on Windows. Open <b>Account & Premium</b> to see your status; it updates automatically while you are online.</li>
     </ol>
     <p className="help-tip">There is no separate Windows checkout yet. You do not need a second purchase for a linked, active iPhone subscription. Current prices are shown on the iPhone purchase screen.</p>
     <p>Manage or cancel an Apple subscription on your iPhone. Signing out of Owl AI or deleting your Owl AI account does not cancel Apple billing.</p>
     <button className="button secondary" onClick={onProfile}>Open Account & Premium <ArrowRight size={16}/></button>
    </section>}
   </div>
   <section className="panel help-faq">
    <h2>A few useful answers</h2>
    <details><summary>Where are my cards saved?</summary><p>When signed in, your collections, cards, and review schedule sync with the same Owl AI account on iPhone. Each account has its own workspace. Cards created while signed out stay in a separate local workspace and are never uploaded automatically. Sign out to return to those cards. In Preferences, use Save backup to keep a copy of your current workspace. You can also export an individual set as CSV from My sets.</p><button className="text-button" onClick={onSettings}>Open Preferences <ArrowRight size={16}/></button></details>
    <details><summary>How do I sign in and sync?</summary><p>Create or open your account in Owl AI on iPhone, then use the same email or Google account on Windows. Open {testMode?'Account':'Account & Premium'} to see synchronization status or select Sync now. Changes sync automatically when online. If the same card changed on both devices, sync preserves your local edits and reports a conflict instead of silently replacing them.</p><button className="text-button" onClick={onProfile}>Open {testMode?'Account':'Account & Premium'} <ArrowRight size={16}/></button></details>
    <details><summary>Why are there no cards ready to review?</summary><p>You may have finished today’s cards, reached your daily new-card goal, or selected different study languages. Check Preferences and the Active for study setting in My sets. Your only set is always active. With several sets, you can choose which ones to study.</p></details>
    <details><summary>What do the four numbers on Learn mean?</summary><p><b>Words collected</b> counts your saved cards. <b>Ready to review</b> counts cards available for the current session and study settings. <b>Practiced today</b> counts completed reviews, so practicing a card again counts again. <b>Day streak</b> tracks consecutive study days.</p></details>
    {!testMode&&<details><summary>I paid on iPhone. Why is Premium missing on Windows?</summary><p>Check that both apps use the same Owl AI account, that the Apple purchase is linked in the iPhone app, and that your subscription is still active. Connect to the internet and open Account & Premium on Windows. If your iPhone does not show the purchase, try Restore in its purchase screen.</p></details>}
    <details><summary>Are there keyboard shortcuts?</summary><p><b>Ctrl + F</b> opens word search on Learn. During practice, press <b>Space</b> to show the answer, then <b>1</b> for Again, <b>2</b> for Hard, <b>3</b> for Good, or <b>4</b> for Easy.</p></details>
   </section>
  </div>;
 }
diff --git a/src/Profile.tsx b/src/Profile.tsx
index 65be20e..0046894 100644
--- a/src/Profile.tsx
+++ b/src/Profile.tsx
@@ -1,23 +1,23 @@
 import {activeAccess} from '../shared/access-policy';
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
  const premium=activeAccess(account);const needsRefresh=['premium','trial','grace'].includes(account.entitlement?.status??'');const periodEnded=Date.parse(account.entitlement?.expires_at??'')<=Date.now();
  const sync=account.sync;
  const syncLabel=sync?.state==='syncing'?'Syncing your cards…':sync?.state==='synced'?'Your cards are synced':sync?.state==='conflict'?'Some changes need attention':sync?.state==='error'?'Sync could not finish':'Ready to sync';
- return <div className="profile-layout"><section className="panel"><span className="eyebrow">YOUR OWL AI ACCOUNT</span><h2>{account.profile?'One account. Your learning space.':'Your iPhone account, on Windows.'}</h2><p className="muted">{account.profile?'Your collections, cards, and review schedule sync with this same account on iPhone. Your signed-out cards stay in a separate space on this computer.':'First create or open your account in Owl AI on iPhone. Then sign in here using the same email or Google account. Windows does not create new accounts.'}</p>{error&&<p className="error" role="alert">{error}</p>}
+ return <div className="profile-layout"><section className="panel"><span className="eyebrow">YOUR OWL AI ACCOUNT</span><h2>{account.profile?'One account. Your learning space.':'Your iPhone account, on Windows.'}</h2><p className="muted">{account.profile?'Your collections, cards, and review schedule sync with this same account on iPhone. Your signed-out cards stay in a separate space on this computer.':'To create or add cards and use online features on Windows, open your Owl AI account on iPhone, then sign in here with the same email or Google account. An account is optional for buying and using Premium on iPhone.'}</p>{error&&<p className="error" role="alert">{error}</p>}
  {account.profile?<><div className="profile-person"><div className="avatar large">{(account.profile.email??'O').charAt(0).toUpperCase()}</div><div><h3>{account.profile.email??account.profile.display_name??'Owl AI learner'}</h3><span className="muted small">Your private account workspace</span></div><ShieldCheck className="accent" size={23}/></div>
  <section aria-label="Account synchronization"><h3>{syncLabel}</h3><p className="small muted" role="status">{sync?.message??(sync?.lastSyncedAt?`Last synced ${new Date(sync.lastSyncedAt).toLocaleString()}.`:'Changes sync automatically while you are online. Only data belonging to this account is included.')}</p><p className="small muted">Signed-out cards are never uploaded automatically. Sign out to return to them.</p>{sync?.state==='conflict'&&<button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(true)}>Use cloud version</button>}</section>
  <button className="button secondary" disabled={busy} onClick={()=>run(async()=>{await owl.logout();notify('Signed out. Returned to your separate local workspace.');})}><LogOut size={16}/> Sign out</button><div className="danger-zone"><h3>Delete account</h3><p className="muted small">Deletes your server account, synced data, and shared access. Your separate signed-out workspace is preserved. Apple billing must be canceled separately.</p><button className="text-button danger" disabled={busy} onClick={()=>setDeleting(true)}>Delete my account</button></div></>:<form onSubmit={e=>{e.preventDefault();void run(async()=>{await owl.login(email,password);setPassword('');notify('Welcome to your Owl AI account.');});}}><button type="button" className="button secondary full google-sign-in" disabled={busy} onClick={()=>run(async()=>{setGooglePending(true);try{const result=await owl.loginGoogle();if(result)notify('Welcome to your Owl AI account.');}finally{setGooglePending(false);}})}><span aria-hidden="true" className="google-letter">G</span> Continue with Google</button>{googlePending&&<div className="google-wait"><p className="small muted">Continue in your browser with the Google account you use in Owl AI on iPhone.</p><button type="button" className="text-button" onClick={()=>owl.cancelGoogleLogin().catch(e=>setError(errorMessage(e)))}>Cancel Google sign-in</button></div>}<p className="small muted center">or sign in with email</p><label className="field">Email<input type="email" autoComplete="email" required value={email} onChange={e=>setEmail(e.target.value)} placeholder="you@example.com"/></label><label className="field">Password<input type="password" autoComplete="current-password" required value={password} onChange={e=>setPassword(e.target.value)} placeholder="Your password"/></label><button className="button full" disabled={busy}>{busy?'Connecting…':'Sign in'}<ArrowRight size={17}/></button><p className="small muted">Sign in to create or add cards on desktop. You can still review eligible saved local cards in this separate workspace.</p></form>}</section>
- {account.testMode?<section className="premium-panel"><div className="premium-mark"><Check size={25}/><span>Test mode</span></div><h1>All learning features are available.</h1><p>Word limits and AI quotas are lifted while test mode is active. Sign in to use online features.</p></section>:<section className="premium-panel"><div className="premium-mark"><Crown size={25}/><span>OWL AI PREMIUM</span></div><h1>One subscription.<br/>More places to grow.</h1><p>Your iPhone subscription comes with you to Windows.</p><div className="devices"><Smartphone size={42}/><Link2 size={23}/><Monitor size={52}/></div><div className="premium-status"><span>{premium?'Premium is active':needsRefresh?(periodEnded?'Subscription period ended':'Refresh subscription status'):account.entitlement?account.entitlement.status.replace(/_/g,' '):'Connect your subscription'}</span>{premium&&<Check size={19}/>}</div>{account.entitlement?.expires_at&&<p className="small">{account.entitlement.auto_renew?'Current period ends':'Access until'} {new Date(account.entitlement.expires_at).toLocaleDateString()}</p>}<ol className="steps"><li>Open Owl AI on iPhone and sign in to this account.</li><li>Link your verified Apple purchase in Profile.</li><li>Your Premium updates automatically on both devices.</li></ol><p className="small premium-note">No second purchase needed. Use the same account on both devices for your cards and Premium.</p></section>}
+ {account.testMode?<section className="premium-panel"><div className="premium-mark"><Check size={25}/><span>Test mode</span></div><h1>Test mode is active.</h1><p>Learning features are available without a subscription in test mode. Sign in to use online features; AI use is still subject to service availability and limits.</p></section>:<section className="premium-panel"><div className="premium-mark"><Crown size={25}/><span>OWL AI PREMIUM</span></div><h1>One subscription.<br/>More places to grow.</h1><p>Your iPhone subscription comes with you to Windows.</p><div className="devices"><Smartphone size={42}/><Link2 size={23}/><Monitor size={52}/></div><div className="premium-status"><span>{premium?'Premium is active':needsRefresh?(periodEnded?'Subscription period ended':'Refresh subscription status'):account.entitlement?account.entitlement.status.replace(/_/g,' '):'Connect your subscription'}</span>{premium&&<Check size={19}/>}</div>{account.entitlement?.expires_at&&<p className="small">{account.entitlement.auto_renew?'Current period ends':'Access until'} {new Date(account.entitlement.expires_at).toLocaleDateString()}</p>}<ol className="steps"><li>On iPhone, an Owl AI account is optional for buying or restoring Premium.</li><li>To use Premium on Windows, sign in to this account on iPhone and link your verified Apple purchase in Profile.</li><li>Sign in to the same account on Windows to see shared Premium.</li></ol><p className="small premium-note">No second purchase needed. Use the same account on both devices for your cards and Premium.</p></section>}
  {replacingWithCloud&&<Modal title="Use cloud version?" onClose={()=>!busy&&setReplacingWithCloud(false)}><p>This replaces your local account cards and review schedule with the server copy. Unsynced local edits will be removed from this workspace.</p><p>A recoverable backup is saved on this computer before replacement. You can restore it from Settings → Restore backup while signed into this account.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setReplacingWithCloud(false)}>Keep local cards</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{const result=await owl.resolveSync();setReplacingWithCloud(false);notify(result.message??'Cloud version restored.');})}>Use cloud version</button></div></Modal>}
  {deleting&&<Modal title="Delete your Owl AI account?" onClose={()=>!busy&&setDeleting(false)}><p>This removes your server account, synced data, and shared subscription access. A linked purchase cannot be automatically attached to a new account.</p><p>Your separate signed-out workspace is preserved. Deleting your account does not cancel Apple billing.</p><div className="modal-actions"><button className="button secondary" disabled={busy} onClick={()=>setDeleting(false)}>Keep account</button><button className="button destructive" disabled={busy} onClick={()=>run(async()=>{await owl.deleteAccount();setDeleting(false);notify('Account deleted. Returned to your separate local workspace.');})}>Delete account</button></div></Modal>}</div>;
 }
diff --git a/tests/test-mode-ui.test.ts b/tests/test-mode-ui.test.ts
index f46d3ae..8e6d03b 100644
--- a/tests/test-mode-ui.test.ts
+++ b/tests/test-mode-ui.test.ts
@@ -1,32 +1,32 @@
 import test from 'node:test';
 import assert from 'node:assert/strict';
 import {createElement} from 'react';
 import {renderToStaticMarkup} from 'react-dom/server';
 import Profile from '../src/Profile';
 import Help from '../src/Help';
 import {WorkspaceBridgeProvider} from '../src/WorkspaceBridge';
 import type {Bridge} from '../shared/types';
 
 test('test mode profile suppresses purchase promotion while preserving account sign-in',()=>{
  const render=(testMode:boolean)=>renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:null,entitlement:null,testMode},onChanged:async()=>{},notify:()=>{}})}));
  const enabled=render(true);assert.match(enabled,/Test mode/);assert.match(enabled,/Sign in/);assert.doesNotMatch(enabled,/Link your verified Apple purchase|Connect your subscription|One subscription/);
- const disabled=render(false);assert.match(disabled,/Link your verified Apple purchase/);assert.doesNotMatch(disabled,/Test mode/);
+ const disabled=render(false);assert.match(disabled,/link your verified Apple purchase/i);assert.doesNotMatch(disabled,/Test mode/);
 });
 
 test('test mode help describes account requirements without purchase instructions',()=>{
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
 
 test('signed-out desktop profile requires sign-in to create and keeps eligible saved reviews',()=>{
  const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:null,entitlement:null,testMode:false},onChanged:async()=>{},notify:()=>{}})}));assert.match(html,/Sign in to create or add cards on desktop/);assert.match(html,/review eligible saved local cards/);assert.doesNotMatch(html,/create and review local cards without signing in/);
 });
