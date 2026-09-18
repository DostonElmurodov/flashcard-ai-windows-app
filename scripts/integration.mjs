import { _electron as electron } from 'playwright';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

const email=process.env.OWL_TEST_ACCOUNT_EMAIL,password=process.env.OWL_TEST_ACCOUNT_PASSWORD;
if(!email||!password)throw new Error('Set OWL_TEST_ACCOUNT_EMAIL and OWL_TEST_ACCOUNT_PASSWORD for an existing iOS-enrolled local test account. This test never creates or deletes accounts.');
const base=process.env.OWL_TEST_API_BASE??'http://127.0.0.1:5289';
const url=new URL(base);
if(url.protocol!=='http:'||url.hostname!=='127.0.0.1'||url.username||url.password||url.search||url.hash||url.pathname!=='/')throw new Error('Integration tests require a local http://127.0.0.1 server origin.');
const data=resolve('test-results/integration-profile-'+Date.now());mkdirSync(data,{recursive:true});
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
try{
 const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);
 const result=await page.evaluate(async({email,password,base,runId})=>{
  const activate=async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);return snapshot;};
  const requireSynced=async()=>{const status=await window.owl.sync();if(status.state!=='synced')throw new Error(`Test account synchronization did not finish: ${status.state}`);};
  await activate();await window.owl.saveSettings({apiBase:base,onboardingComplete:true});
  const guestDeck=await window.owl.saveDeck({name:`Integration guest ${runId}`,nativeLanguage:'ru',learningLanguage:'en-us'});
  await window.owl.addWords(guestDeck.id,[{word:'guest-only',translation:'локальная карточка'}]);
  const guestScope=(await activate()).workspaceId;
  let rejected=false;
  try{await window.owl.login(`missing-${runId}@example.test`,'No-Such-Account-248!');}catch{rejected=true;}
  let snapshot=await activate();
  if(!rejected||snapshot.account?.profile)throw new Error('Desktop sign-in accepted a missing account.');
  if(snapshot.workspaceId!==guestScope||!snapshot.decks.some(d=>d.id===guestDeck.id))throw new Error('Rejected login changed guest data.');
  let accountDeckId=null,ownerId=null;
  try{
   const loggedIn=await window.owl.login(email,password);ownerId=loggedIn.profile?.id;
   if(!ownerId)throw new Error('Existing enrolled test account login failed.');
   snapshot=await activate();await requireSynced();snapshot=await activate();
   const accountScope=snapshot.workspaceId;
   if(accountScope===guestScope||snapshot.decks.some(d=>d.id===guestDeck.id)||snapshot.words.some(w=>w.deckId===guestDeck.id))throw new Error('Guest cards leaked into the account workspace.');
   const deck=await window.owl.saveDeck({name:`Desktop integration ${runId}`,nativeLanguage:'ru',learningLanguage:'en-us'});accountDeckId=deck.id;
   await window.owl.addWords(deck.id,[{word:'hello',translation:'привет',examples:['Hello, friend.']}]);
   await requireSynced();
   const state=await window.owl.account();
   if('access_token' in state||'refresh_token' in state)throw new Error('Credentials leaked into renderer account state.');
   await window.owl.logout();snapshot=await activate();
   if(snapshot.account?.profile||snapshot.workspaceId!==guestScope)throw new Error('Logout did not restore the guest workspace.');
   if(!snapshot.decks.some(d=>d.id===guestDeck.id)||snapshot.decks.some(d=>d.id===accountDeckId))throw new Error('Account and guest cards mixed on logout.');
   await window.owl.login(email,password);snapshot=await activate();await requireSynced();snapshot=await activate();
   if(snapshot.account?.profile?.id!==ownerId||snapshot.workspaceId!==accountScope)throw new Error('Relogin selected a different account workspace.');
   if(!snapshot.decks.some(d=>d.id===accountDeckId)||!snapshot.words.some(w=>w.deckId===accountDeckId)||snapshot.decks.some(d=>d.id===guestDeck.id))throw new Error('Relogin did not restore only account-owned cards.');
   return {missingAccountRejected:true,existingAccountLogin:true,synchronization:true,guestPreserved:true,accountIsolation:true,relogin:true};
  }finally{
   // Only the newly generated deck belongs to this test. Never delete the account or unrelated records.
   snapshot=await activate();
   if(accountDeckId){
    if(!snapshot.account?.profile){await window.owl.login(email,password);snapshot=await activate();}
    if(snapshot.account?.profile?.id!==ownerId)throw new Error('Cleanup refused: active account differs from the test owner.');
    if(snapshot.decks.some(d=>d.id===accountDeckId)){await window.owl.deleteDeck(accountDeckId);await requireSynced();}
   }
   if((await activate()).account?.profile){await window.owl.logout();await activate();}
  }
 },{email,password,base,runId:randomUUID()});
 console.log('PASS: Electron → local API → PostgreSQL existing-account login and isolated synchronization.');console.log(JSON.stringify(result));
}finally{await app.close();}
