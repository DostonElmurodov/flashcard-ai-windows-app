import test from 'node:test';
import assert from 'node:assert/strict';
import { parseImport } from '../electron/imports';
import { newCard, scheduleCard, studyDayStart } from '../electron/scheduler';
import { Store } from '../electron/store';
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import initSqlJs from 'sql.js';

test('the first and only set stays active, while multiple sets can be disabled',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));const s=await Store.open(join(dir,'db.sqlite'));
 try {
  const first=s.saveDeck({name:'First',active:false});assert.equal(first.active,true);
  assert.equal(s.saveDeck({...first,active:false}).active,true);
  const second=s.saveDeck({name:'Second',active:false});assert.equal(second.active,false);
  assert.equal(s.saveDeck({...first,active:false}).active,false);
  assert.equal(s.snapshot().decks.filter(d=>d.active).length,0);
 }finally{s.close();rmSync(dir,{recursive:true,force:true});}
});

test('deleting other sets activates the remaining set and its review cards persistently',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-')),path=join(dir,'db.sqlite');let s=await Store.open(path);
 try {
  const first=s.saveDeck({name:'First'}),remaining=s.saveDeck({name:'Remaining',active:false});
  s.addWords(remaining.id,[{word:'hello',translation:'привет'}]);assert.equal(s.queue().length,0);
  s.deleteDeck(first.id);assert.equal(s.snapshot().decks[0].active,true);assert.equal(s.queue().length,1);
  s.close();s=await Store.open(path);assert.equal(s.snapshot().decks[0].active,true);
  s.deleteDeck(remaining.id);assert.equal(s.snapshot().decks.length,0);
 }finally{s.close();rmSync(dir,{recursive:true,force:true});}
});

test('opening and restoring a legacy inactive only set activates it without altering the backup',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-')),path=join(dir,'db.sqlite'),backup=join(dir,'legacy.sqlite');
 let s=await Store.open(path);
 try {
  const deck=s.saveDeck({name:'Legacy'});s.backup(backup);
  const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});
  const legacy=new SQL.Database(readFileSync(backup));legacy.run('UPDATE decks SET data=?',[JSON.stringify({...deck,active:false})]);
  writeFileSync(backup,legacy.export());legacy.close();const original=readFileSync(backup);
  const opened=await Store.open(backup);try{assert.equal(opened.snapshot().decks[0].active,true);}finally{opened.close();}
  writeFileSync(backup,original);
  await s.restore(backup);assert.equal(s.snapshot().decks[0].active,true);assert.deepEqual(readFileSync(backup),original);
  s.close();s=await Store.open(path);assert.equal(s.snapshot().decks[0].active,true);
 }finally{s.close();rmSync(dir,{recursive:true,force:true});}
});

test('CSV handles quoted multiline values and deduplicates normalized pairs',()=>{
 const rows=parseImport('word,translation\n"hello","привет\nздравствуй"\ncat,кот\ncat,кот','auto','csv');
 assert.equal(rows.length,2); assert.equal(rows[0].translation,'привет\nздравствуй');
});
test('pairs retain incomplete final word for editing',()=>{
 assert.deepEqual(parseImport('hello\nпривет\nworld','pairs'),[{word:'hello',translation:'привет'},{word:'world',translation:''}]);
});
test('FSRS uses four distinct previews and updates repetitions',()=>{
 const now=new Date('2026-09-08T12:00:00Z'); const card=newCard(now);
 const again=scheduleCard(card,1,now), easy=scheduleCard(card,4,now);
 assert.equal(again.reps,1); assert.ok(new Date(easy.due).getTime()>new Date(again.due).getTime());
 assert.ok(easy.stability>0);
});
test('study day before boundary belongs to previous calendar day',()=>{
 const d=new Date(2026,8,8,3,30); assert.equal(studyDayStart(d,240).getDate(),7);
});
test('store survives restart, prevents duplicate review attempts and preserves words',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-')); const path=join(dir,'cards.sqlite');
 try {
  const s=await Store.open(path); const deck=s.saveDeck({name:'Travel',description:'',nativeLanguage:'ru',learningLanguage:'en-us'});
  s.addWords(deck.id,[{word:'hello',translation:'привет'}]); const word=s.snapshot().words[0];
  const first=s.review(word.id,3,'attempt-1'); const second=s.review(word.id,3,'attempt-1');
  assert.equal(first.card.reps,second.card.reps); s.close();
  const reopened=await Store.open(path); assert.equal(reopened.snapshot().words.length,1); assert.equal(reopened.snapshot().words[0].card.reps,1); reopened.close();
 } finally {rmSync(dir,{recursive:true,force:true});}
});
test('renaming a set preserves cards and their scheduling',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));
 try {const s=await Store.open(join(dir,'db.sqlite'));const deck=s.saveDeck({name:'Old'});s.addWords(deck.id,[{word:'one',translation:'один'}]);s.saveDeck({...deck,name:'New'});assert.equal(s.snapshot().words.length,1);s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('desktop create payload with absent optional ID gets a generated ID',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));try{const s=await Store.open(join(dir,'db.sqlite'));const d=s.saveDeck({id:undefined,name:'New',description:''});assert.ok(d.id);s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('oversized imports are rejected rather than silently truncated',()=>{
 assert.throws(()=>parseImport(Array.from({length:2001},(_,i)=>`word${i}\ttranslation${i}`).join('\n'),'auto'),/2,000/);
});
test('failed batch import rolls back all cards',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));try{const s=await Store.open(join(dir,'db.sqlite'));const deck=s.saveDeck({name:'Batch'});assert.throws(()=>s.addWords(deck.id,[{word:'good',translation:'хорошо'},{word:'bad',translation:''}]));assert.equal(s.snapshot().words.length,0);s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('reverse direction has independent state and daily goal',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));try{const s=await Store.open(join(dir,'db.sqlite'));s.saveSettings({dailyGoal:1});const deck=s.saveDeck({name:'Directions'});s.addWords(deck.id,[{word:'one',translation:'один'},{word:'two',translation:'два'}]);assert.equal(s.queue().length,1);const first=s.queue()[0];s.review(first.id,4,'forward');assert.equal(s.queue().filter(w=>w.card.state===0).length,0);s.saveSettings({direction:'reverse'});assert.equal(s.queue().length,1);const before=s.snapshot().words.find(w=>w.id===first.id)!;assert.equal(before.reverse.reps,0);assert.equal(before.card.reps,1);s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('backup restore preserves server selection and does not alter source',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));try{const s=await Store.open(join(dir,'db.sqlite')),backup=join(dir,'backup.sqlite');const deck=s.saveDeck({name:'Backed up'});s.addWords(deck.id,[{word:'one',translation:'один'}]);s.backup(backup);const original=readFileSync(backup);s.saveSettings({apiBase:'http://127.0.0.1:5289'});s.deleteDeck(deck.id);await s.restore(backup);assert.equal(s.snapshot().words.length,1);assert.equal(s.settings().apiBase,'http://127.0.0.1:5289');assert.deepEqual(readFileSync(backup),original);s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('invalid backup leaves current database usable',async()=>{
 const dir=mkdtempSync(join(tmpdir(),'owl-test-'));try{const s=await Store.open(join(dir,'db.sqlite'));s.saveDeck({name:'Keep me'});const bad=join(dir,'bad.sqlite');writeFileSync(bad,'invalid sqlite');await assert.rejects(()=>s.restore(bad));assert.equal(s.snapshot().decks[0].name,'Keep me');s.close();}finally{rmSync(dir,{recursive:true,force:true});}
});
test('scheduler rounds the configured hard step to a minute and does not mutate input',()=>{
 const now=new Date(1_800_000_000_000),card=newCard(now),copy=JSON.stringify(card);const again=scheduleCard(card,1,now),hard=scheduleCard(card,2,now),good=scheduleCard(card,3,now);
 assert.equal(new Date(again.due).getTime()-now.getTime(),60_000);assert.equal(new Date(hard.due).getTime()-now.getTime(),360_000);assert.equal(new Date(good.due).getTime()-now.getTime(),600_000);assert.equal(JSON.stringify(card),copy);
});
