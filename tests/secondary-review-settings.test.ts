import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,readFileSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import initSqlJs from 'sql.js';
import {Store} from '../electron/store';

test('secondary review language defaults off and persists a canonical choice independently of study languages',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-secondary-settings-')),path=join(root,'db.sqlite');let store=await Store.open(path);
 try{
  assert.equal(store.settings().secondaryReviewLanguage,null);
  const deck=store.saveDeck({name:'Practice'});store.addWords(deck.id,[{word:'journey',translation:'путешествие'}]);
  const before=store.snapshot(),queue=store.queue();
  store.saveSettings({secondaryReviewLanguage:' ES '});store.close();store=await Store.open(path);
  assert.equal(store.settings().secondaryReviewLanguage,'es');
  assert.equal(store.settings().nativeLanguage,'ru');assert.equal(store.settings().learningLanguage,'en-us');
  assert.deepEqual(store.snapshot().words,before.words);assert.deepEqual(store.queue(),queue);
  assert.equal(store.snapshot().reviewedToday,0);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('secondary choice clears when disabled, invalid, or equal to the native language including English aliases',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-secondary-settings-'));const store=await Store.open(join(root,'db.sqlite'));
 try{
  store.saveSettings({secondaryReviewLanguage:'es'});store.saveSettings({nativeLanguage:'es'});
  assert.equal(store.settings().secondaryReviewLanguage,null);
  for(const choice of ['unsupported','', ' ES ',null]){
   assert.equal(store.saveSettings({secondaryReviewLanguage:choice}).secondaryReviewLanguage,null);
  }
  store.saveSettings({nativeLanguage:'en',secondaryReviewLanguage:'en-US'});
  assert.equal(store.settings().secondaryReviewLanguage,null);
  store.saveSettings({nativeLanguage:'ru',secondaryReviewLanguage:'EN_us'});
  assert.equal(store.settings().secondaryReviewLanguage,'en-us');
  assert.equal(store.saveSettings({secondaryReviewLanguage:null}).secondaryReviewLanguage,null);
  store.saveSettings({nativeLanguage:'en-gb',secondaryReviewLanguage:'en'});assert.equal(store.settings().secondaryReviewLanguage,null);
  store.saveSettings({nativeLanguage:'ru',secondaryReviewLanguage:'zh-Hans'});assert.equal(store.settings().secondaryReviewLanguage,'zh');
  store.saveSettings({nativeLanguage:'zh-CN'});assert.equal(store.settings().secondaryReviewLanguage,null);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('opening legacy or malformed settings cannot enable an invalid secondary language',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-secondary-settings-')),path=join(root,'db.sqlite');let store=await Store.open(path);store.close();
 try{
  const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});
  for(const choice of [undefined,true,42,'xx','ru']){
   const db=new SQL.Database(readFileSync(path));
   db.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify({nativeLanguage:'ru',secondaryReviewLanguage:choice})]);
   writeFileSync(path,db.export());db.close();store=await Store.open(path);
   assert.equal(store.settings().secondaryReviewLanguage,null);store.close();
  }
 }finally{rmSync(root,{recursive:true,force:true});}
});
