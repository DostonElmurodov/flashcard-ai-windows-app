import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Workspaces,accountScope} from '../electron/workspace';
import {AccountSync,exportRecords} from '../electron/sync';
import {Store} from '../electron/store';

test('guest, account A and account B remain separate across restart and backup restore',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-scope-'));let manager=await Workspaces.open(root);
 try{
  manager.current.saveDeck({name:'Guest'});
  await manager.select('https://api.example.com','A');assert.equal(manager.current.snapshot().decks.length,0);manager.current.saveDeck({name:'Private A'});
  const backup=join(root,'private.sqlite');manager.current.backup(backup);
  await manager.select('https://api.example.com','B');assert.equal(manager.current.snapshot().decks.length,0);
  await assert.rejects(manager.current.restore(backup),/different account/);
  await manager.select('https://api.example.com',null);assert.equal(manager.current.snapshot().decks[0].name,'Guest');
  await assert.rejects(manager.current.restore(backup),/different account/);
  manager.close();manager=await Workspaces.open(root);await manager.select('https://api.example.com','A');assert.equal(manager.current.snapshot().decks[0].name,'Private A');
  assert.notEqual(accountScope('https://other.example.com','A'),manager.current.owner());
 }finally{manager.close();rmSync(root,{recursive:true,force:true});}
});

test('sync rejects another owner and preserves edits on version conflicts',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-sync-')),store=await Store.open(join(root,'db'),'A');
 try{
  const d=store.saveDeck({name:'Private'});let reply:any={owner_id:'B',records:[]};
  const sync=new AccountSync(store,'A',{request:async<T>()=>reply as T});
  assert.equal((await sync.run()).state,'error');assert.equal(store.snapshot().decks[0].id,d.id);
  const records=exportRecords(store.snapshot().decks,[]).map(r=>({...r,version:1}));reply={owner_id:'A',records};assert.equal((await sync.run()).state,'synced');
  store.saveDeck({...d,name:'Local edit'});reply={owner_id:'A',records,conflicts:records};assert.equal((await sync.run()).state,'conflict');assert.equal(store.snapshot().decks[0].name,'Local edit');assert.equal(store.syncBaseline()[0].version,1);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('a failed account database switch falls back to guest, never the previous owner',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-failed-switch-')),manager=await Workspaces.open(root);
 try{
  manager.guest.saveDeck({name:'Guest'});await manager.select('https://api.example.com','A');manager.current.saveDeck({name:'Private A'});
  const bad=await Store.open(join(root,'accounts',accountScope('https://api.example.com','B')+'.sqlite'),'wrong-owner');bad.close();
  await assert.rejects(manager.select('https://api.example.com','B'),/different account/);assert.equal(manager.current,manager.guest);assert.equal(manager.current.snapshot().decks[0].name,'Guest');
 }finally{manager.close();rmSync(root,{recursive:true,force:true});}
});

test('late response after account switch cannot write and in-flight local edits survive',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-race-')),store=await Store.open(join(root,'db'),'A');
 try{
  const d=store.saveDeck({name:'Before'}),records=exportRecords([d],[]).map(r=>({...r,version:1}));let resolve!:(v:any)=>void;
  const sync=new AccountSync(store,'A',{request:<T>()=>new Promise<T>(r=>resolve=r)});
  const pending=sync.run();store.saveDeck({...d,name:'During request'});resolve({owner_id:'A',records});await pending;assert.equal(store.snapshot().decks[0].name,'During request');
  const late=sync.run(),stopped=sync.stop();resolve({owner_id:'A',records:[]});await Promise.all([late,stopped]);assert.equal(store.snapshot().decks[0].name,'During request');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('large sync batches retain iOS metadata and detect conflicts on unsent records',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-batch-')),store=await Store.open(join(root,'db'),'A');
 try{
  const deck=store.saveDeck({name:'Words'});store.addWords(deck.id,Array.from({length:510},(_,i)=>({word:'Word '+i,translation:'Translation '+i})));
  let remote=new Map<string,any>(),requests=0;
  const sync=new AccountSync(store,'A',{request:async<T>(_path:string,_method?:string,body?:any)=>{
   requests++;assert.ok(body.changes.length<=500);
   for(const c of body.changes){const k=c.kind+':'+c.id,old=remote.get(k);assert.equal(c.base_version,old?.version??0);remote.set(k,{...c,version:(old?.version??0)+1});}
   return {owner_id:'A',records:[...remote.values()]} as T;
  }});
  assert.equal((await sync.run()).state,'synced');assert.equal(requests,2);assert.equal(store.snapshot().words.length,510);
  const records=store.syncBaseline(),word=records.find(r=>r.kind==='word')!;word.data!.metadata={ios_set_ids:[deck.id],example_translations:['Example']};
  store.applySync(store.snapshot().decks,store.snapshot().words,records);
  const local=store.snapshot();assert.deepEqual(exportRecords(local.decks,local.words,store.syncBaseline()).find(r=>r.id===word.id)?.data?.metadata,word.data!.metadata);
  for(const w of local.words)store.editWord(w.id,{...w,translation:'Local change'});
  let injected=false,conflicted=false;
  const conflictSync=new AccountSync(store,'A',{request:async<T>(_path:string,_method?:string,body?:any)=>{
   const conflicts=body.changes.filter((c:any)=>c.base_version!==(remote.get(c.kind+':'+c.id)?.version??0)).map((c:any)=>remote.get(c.kind+':'+c.id));
   if(conflicts.length){conflicted=true;return {owner_id:'A',records:[...remote.values()],conflicts} as T;}
   for(const c of body.changes){const k=c.kind+':'+c.id,old=remote.get(k);remote.set(k,{...c,version:old.version+1});}
   if(!injected){injected=true;const keys=new Set(body.changes.map((c:any)=>c.kind+':'+c.id)),next=[...remote.entries()].find(([k,r])=>r.kind==='word'&&!keys.has(k))!;remote.set(next[0],{...next[1],version:next[1].version+1,data:{...next[1].data,translation:'Concurrent iPhone edit'}});}
   return {owner_id:'A',records:[...remote.values()]} as T;
  }});
  assert.equal((await conflictSync.run()).state,'conflict');assert.ok(conflicted);assert.ok(store.snapshot().words.every(w=>w.translation==='Local change'));
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('mixed-language collections preserve word languages and skip unfinished translations',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-languages-')),store=await Store.open(join(root,'db'),'A');
 try{
  const d=store.saveDeck({name:'Mixed',nativeLanguage:'ru',learningLanguage:'en-us'});store.addWords(d.id,[{word:'hello',translation:'привет'},{word:'bonjour',translation:'привет'},{word:'unfinished',translation:'pending'}]);
  const words=store.snapshot().words.map(w=>({...w,nativeLanguage:'ru',learningLanguage:w.word==='bonjour'?'fr':'en-us',translation:w.word==='unfinished'?'':w.translation}));
  store.applySync([d],words,exportRecords([d],words));store.saveSettings({learningLanguage:'fr',nativeLanguage:'ru'});assert.deepEqual(store.queue().map(w=>w.word),['bonjour']);
  store.saveSettings({learningLanguage:'en-us'});assert.deepEqual(store.queue().map(w=>w.word),['hello']);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('cloud replacement saves a recoverable same-account backup',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-reset-')),store=await Store.open(join(root,'db'),'A');
 try{
  store.saveDeck({name:'Local edit'});
  const sync=new AccountSync(store,'A',{request:async<T>()=>({owner_id:'A',records:[]}) as T});
  await sync.useCloud();assert.equal(store.snapshot().decks.length,0);
  const {readdirSync}=await import('node:fs');const backup=readdirSync(root).find(f=>f.includes('.before-cloud-'))!;assert.ok(backup);
  await store.restore(join(root,backup));assert.equal(store.snapshot().decks[0].name,'Local edit');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});
