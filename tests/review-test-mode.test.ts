import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Store} from '../electron/store';
import {FeatureFlags} from '../electron/feature-flags';

test('confirmed test mode includes newly added words after the daily allowance, and false restores it',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-review-test-mode-'));
 const store=await Store.open(join(root,'db.sqlite'));
 try {
  let enabled=true;
  const flags=new FeatureFlags('',()=> 'https://api.example.com',async()=>Response.json({test_mode:enabled}));
  store.saveSettings({dailyGoal:1});
  const deck=store.saveDeck({name:'English'});
  store.addWords(deck.id,[{word:'first',translation:'первый'}]);
  const first=store.queue()[0];store.review(first.id,4,'first');
  store.addWords(deck.id,[{word:'swap',translation:'обменять'},{word:'other',translation:'другой'}]);
  assert.equal(store.queue().length,0,'Reproduces hidden new words after the normal daily goal');
  await flags.refresh();
  assert.deepEqual(new Set(store.queue(undefined,new Date(),flags.testMode).map(w=>w.word)),new Set(['swap','other']));
  assert.equal(store.settings().dailyGoal,1,'Test mode never rewrites the saved goal');
  const swap=store.snapshot().words.find(w=>w.word==='swap')!;
  enabled=false;await flags.refresh();
  assert.equal(store.queue(undefined,new Date(),flags.testMode).length,0);
  assert.throws(()=>store.review(swap.id,4,'blocked',flags.testMode),/not currently due/);
  enabled=true;await flags.refresh();
  store.review(swap.id,4,'swap',flags.testMode);
  assert.deepEqual(store.queue(undefined,new Date(),flags.testMode).map(w=>w.word),['other'],'Grading works and future reviews stay scheduled');
 }finally {store.close();rmSync(root,{recursive:true,force:true});}
});

test('test mode retains active-set and language filters with a zero daily goal',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-review-test-filters-'));
 const store=await Store.open(join(root,'db.sqlite'));
 try {
  store.saveSettings({dailyGoal:0});
  const active=store.saveDeck({name:'Active'}),inactive=store.saveDeck({name:'Inactive',active:false}),french=store.saveDeck({name:'French',learningLanguage:'fr'});
  store.addWords(active.id,[{word:'swap',translation:'обменять'}]);
  store.addWords(inactive.id,[{word:'hidden',translation:'скрытый'}]);
  store.addWords(french.id,[{word:'bonjour',translation:'привет'}]);
  assert.equal(store.queue().length,0);
  assert.deepEqual(store.queue(undefined,new Date(),true).map(w=>w.word),['swap']);
  store.saveSettings({direction:'reverse'});
  assert.deepEqual(store.queue(undefined,new Date(),true).map(w=>w.word),['swap']);
  assert.equal(store.queue(undefined,new Date(),false).length,0);
 }finally {store.close();rmSync(root,{recursive:true,force:true});}
});
