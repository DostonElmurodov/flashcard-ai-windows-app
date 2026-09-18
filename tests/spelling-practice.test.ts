import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,readFileSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import initSqlJs from 'sql.js';
import {Store} from '../electron/store';

test('spelling preference defaults off and persists without changing scheduling or direction',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-spelling-')),path=join(root,'db.sqlite');let store=await Store.open(path);
 try{
  assert.equal(store.settings().spellingPractice,false);
  store.saveSettings({direction:'reverse'});
  const deck=store.saveDeck({name:'Practice'});store.addWords(deck.id,[{word:'journey',translation:'путешествие'}]);
  const before=store.snapshot(),queue=store.queue();
  store.saveSettings({spellingPractice:true});store.close();store=await Store.open(path);
  assert.equal(store.settings().spellingPractice,true);
  assert.equal(store.settings().direction,'reverse');
  assert.deepEqual(store.snapshot().words,before.words);assert.deepEqual(store.queue(),queue);
  assert.equal(store.snapshot().reviewedToday,0);
  assert.equal(store.saveSettings({spellingPractice:false}).spellingPractice,false);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('legacy and malformed persisted spelling preferences remain off',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-spelling-')),path=join(root,'db.sqlite');let store=await Store.open(path);store.close();
 try{
  const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});
  for(const value of [undefined,'true',1,null]){
   const db=new SQL.Database(readFileSync(path));
   db.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify({spellingPractice:value})]);
   writeFileSync(path,db.export());db.close();store=await Store.open(path);
   assert.equal(store.settings().spellingPractice,false);store.close();
  }
 }finally{rmSync(root,{recursive:true,force:true});}
});

test('spelling ignores case and repeated whitespace, but retains accents and punctuation',async()=>{
 const {matchesSpelling}=await import('../shared/spelling-practice');
 for(const [answer,expected,want] of [
  [' CAR ','Car',true],['  New\t York\n','new york',true],['cafe','café',false],
  ['cafe\u0301','café',true],["dont","don't",false],['journey!','journey',false],
  ['','',false],[' \t\n','journey',false],['journeys','journey',false]
 ] as const)assert.equal(matchesSpelling(answer,expected),want,`${JSON.stringify(answer)} vs ${JSON.stringify(expected)}`);
});
