import test from 'node:test';
import assert from 'node:assert/strict';
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
 const context:TranslationWorkspace={store,scope:'1:account-a',apiBase:'https://api.example.com',account:{profile:{id:'a',email:'a@example.com'},entitlement:{status:'premium',is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false}};
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
  const reopened=await Store.open(f.path,'account-a');
  try{const restarted=new ReviewTranslations(()=>({...f.context,store:reopened,account:{...f.context.account,entitlement:null}}),async()=>{throw new Error('Offline cache miss');});assert.deepEqual(await restarted.get(f.id),spanish);}finally{reopened.close();}
 }finally{f.cleanup();}
});

test('requests use word source languages, falling back to its deck, and cache by target and API origin',async()=>{
 const f=await fixture();const inputs:unknown[]=[];
 try{
  const snap=f.store.snapshot();f.store.applySync(snap.decks,[{...snap.words[0],nativeLanguage:'uk',learningLanguage:'fr',word:'voyage'}],[]);
  const service=new ReviewTranslations(()=>f.context,async input=>{inputs.push(input);return {...spanish,language_code:input.secondary_language};});
  await service.get(f.id);assert.deepEqual(inputs[0],{word:'voyage',native_language:'uk',learning_language:'fr',secondary_language:'es'});
  f.store.saveSettings({secondaryReviewLanguage:'de'});await service.get(f.id);
  f.context.apiBase='https://other.example.com';await service.get(f.id);assert.equal(inputs.length,3);
  f.store.saveSettings({secondaryReviewLanguage:'uk'});await assert.rejects(service.get(f.id));assert.equal(inputs.length,3,'No request when target equals the actual card native language');
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
  f.context.account.entitlement!.status='premium';assert.deepEqual(await service.get(f.id),spanish);assert.equal(calls,1);
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
});

test('concurrent requests deduplicate, but late responses cannot write after account, source, or setting changes',async()=>{
 const f=await fixture();let complete!:(value:unknown)=>void,calls=0;
 try{
  const service=new ReviewTranslations(()=>f.context,()=>{calls++;return new Promise(resolve=>{complete=resolve;});});
  for(const change of ['account','word','disabled'] as const){
   f.store.saveSettings({secondaryReviewLanguage:'es'});
   const first=service.get(f.id),second=service.get(f.id);const rejected=Promise.all([assert.rejects(first),assert.rejects(second)]);
   assert.equal(calls,['account','word','disabled'].indexOf(change)+1);
   if(change==='account')f.context.scope='2:account-b';
   if(change==='word')f.store.editWord(f.id,{word:'trip',translation:'поездка'});
   if(change==='disabled')f.store.saveSettings({secondaryReviewLanguage:null});
   complete(spanish);await rejected;
  }
  f.store.saveSettings({secondaryReviewLanguage:'es'});
  const final=service.get(f.id);assert.equal(calls,4);complete(spanish);assert.deepEqual(await final,spanish);
 }finally{f.cleanup();}
});


test('AI preview fetches the second language before saving and review reuses its cache',async()=>{
 const f=await fixture();let calls=0;
 try{
  const before=f.store.snapshot();
  const service=new ReviewTranslations(()=>f.context,async input=>{
   calls++;assert.deepEqual(input,{word:'airport',native_language:'ru',learning_language:'en-us',secondary_language:'es'});
   return {...spanish,translation:'aeropuerto'};
  });
  assert.equal((await service.preview('airport','ru','en-us')).translation,'aeropuerto');
  assert.deepEqual(f.store.snapshot(),before,'Preview must not add a word or affect study progress');
  f.store.addWords(f.deck.id,[{word:'airport',translation:'аэропорт'}]);
  const word=f.store.snapshot().words.find(row=>row.word==='airport')!;
  assert.equal((await service.get(word.id)).translation,'aeropuerto');assert.equal(calls,1);
 }finally{f.cleanup();}
});

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
