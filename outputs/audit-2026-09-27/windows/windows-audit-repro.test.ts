import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Store} from '../../../electron/store';
import {parseImport} from '../../../electron/imports';
import {FeatureFlags} from '../../../electron/feature-flags';
import {Api} from '../../../electron/api';
import {ReviewTranslations} from '../../../electron/review-translations';

test('false mode does not cap local manual/imported words for guest or account workspaces',async()=>{
 const root=mkdtempSync(join(tmpdir(),'audit-local-'));
 try{
  const flags=new FeatureFlags('',()=> 'https://api.example.test',async()=>Response.json({test_mode:false}));
  await flags.refresh();assert.equal(flags.testMode,false);
  for(const owner of ['guest','account-workspace']){
   const store=await Store.open(join(root,owner+'.sqlite'),owner);
   try{
    const deck=store.saveDeck({name:'Unpaid local library'});
    const drafts=parseImport(Array.from({length:100},(_,i)=>`word${i} = translation${i}`).join('\n'),'auto');
    assert.equal(store.addWords(deck.id,drafts),100);
    assert.equal(store.addWords(deck.id,[{word:'manual101',translation:'manual translation'}]),1);
    assert.equal(store.snapshot().words.length,101);
    store.saveSettings({dailyGoal:200});
    assert.equal(store.queue(undefined,new Date(),flags.testMode).length,101);
    console.log(JSON.stringify({owner,testMode:flags.testMode,storedWords:101,normalModeReviewQueue:101}));
   }finally{store.close();}
  }
 }finally{rmSync(root,{recursive:true,force:true});}
});

test('unpaid signed-in Api.translate delegates commercial enforcement to the server',async()=>{
 const api=new Api(join(tmpdir(),'nonexistent-audit-account.enc'),()=> 'https://api.example.test');
 (api as any).session={access_token:'FAKE-NOT-A-REAL-TOKEN',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:'FAKE',profile:{id:'unpaid',email:null}};
 const original=globalThis.fetch;let requests=0;
 globalThis.fetch=async(input,options)=>{
  requests++;assert.equal(String(input),'https://api.example.test/owlai/account/ai/word-detail');
  assert.equal(new Headers(options?.headers).get('Authorization'),'Bearer FAKE-NOT-A-REAL-TOKEN');
  return Response.json({error:'subscription_required'},{status:402});
 };
 try{
  assert.deepEqual(api.state(),{profile:{id:'unpaid',email:null},entitlement:null,testMode:false});
  await assert.rejects(api.translate('hello','ru','en-us'),/active shared Premium/);
  assert.equal(requests,1);console.log(JSON.stringify({unpaidAiRequestsSentToStub:requests,server402Propagated:true}));
 }finally{globalThis.fetch=original;}
});

test('confirmed test mode has no TTL until the next refresh completes',async()=>{
 let complete!:(value:Response)=>void,hold=false;
 const flags=new FeatureFlags('',()=> 'https://api.example.test',()=>hold?new Promise(resolve=>complete=resolve):Promise.resolve(Response.json({test_mode:true})));
 await flags.refresh();hold=true;const pending=flags.refresh();
 assert.equal(flags.testMode,true);complete(Response.json({test_mode:false}));await pending;assert.equal(flags.testMode,false);
});

test('successful review cache remains available after entitlement is revoked without AI calls',async()=>{
 const root=mkdtempSync(join(tmpdir(),'audit-cache-')),store=await Store.open(join(root,'cache.sqlite'),'account-workspace');
 try{
  const deck=store.saveDeck({name:'Cache test'});store.addWords(deck.id,[{word:'hello',translation:'привет'}]);store.saveSettings({secondaryReviewLanguage:'es'});
  const workspace={store,scope:'1:account-workspace',apiBase:'https://api.example.test',account:{profile:{id:'unpaid',email:null},entitlement:{status:'premium',is_trial:false,auto_renew:false,was_ever_paid:true},testMode:false}};
  let calls=0;const service=new ReviewTranslations(()=>workspace,async()=>{calls++;return {language_code:'es',translation:'hola',explanation:'Un saludo'};});
  await service.get(store.snapshot().words[0].id);workspace.account.entitlement.status='revoked';
  await service.get(store.snapshot().words[0].id);assert.equal(calls,1);
  console.log(JSON.stringify({revokedCachedReadAllowed:true,newAiRequestsForRevokedRead:0}));
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});
