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
  store.applySync([],[],[],0);
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
   if(_method==='GET')return {owner_id:'A',records:[...remote.values()]} as T;
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
   if(_method==='GET')return {owner_id:'A',records:[...remote.values()]} as T;
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

test('incremental replies retain unrelated records, persist cursor and avoid unchanged database writes',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-delta-')),path=join(root,'db');let store=await Store.open(path,'A');
 try{
  const a=store.saveDeck({name:'A'}),b=store.saveDeck({name:'B'}),records=exportRecords([a,b],[]).map(r=>({...r,version:1}));
  let reply:any={owner_id:'A',records,cursor:2,is_snapshot:true};const requests:any[]=[];
  const transport={request:async<T>(path:string,method?:string,body?:unknown)=>{requests.push({path,method,body});return {...reply,is_snapshot:method==='POST'?false:reply.is_snapshot} as T;}};
  const sync=new AccountSync(store,'A',transport);assert.equal((await sync.run()).state,'synced');
  reply={owner_id:'A',records:[{...records[0],version:2,data:{...records[0].data,name:'Remote A'}}],cursor:3,is_snapshot:false};
  assert.equal((await sync.run()).state,'synced');assert.deepEqual(store.snapshot().decks.map(d=>d.name).sort(),['B','Remote A']);
  const {statSync}=await import('node:fs');const before=statSync(path).ino;reply={owner_id:'A',records:[],cursor:3,is_snapshot:false};
  await sync.run();assert.equal(statSync(path).ino,before);assert.equal(requests.at(-1).path,'/owlai/account/sync?since=3');
  store.close();store=await Store.open(path,'A');await new AccountSync(store,'A',transport).run();assert.equal(requests.at(-1).path,'/owlai/account/sync?since=3');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('edits to clean rows during a pull retain their original conflict version and cursor',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-delta-race-')),store=await Store.open(join(root,'db'),'A');
 try{
  const d=store.saveDeck({name:'Original'}),records=exportRecords([d],[]).map(r=>({...r,version:1}));
  await new AccountSync(store,'A',{request:async<T>(_path:string,method?:string)=>({owner_id:'A',records,cursor:1,is_snapshot:method!=='POST'}) as T}).run();
  let finish!:(reply:any)=>void;const requests:any[]=[];
  const sync=new AccountSync(store,'A',{request:<T>(path:string,method?:string,body?:unknown)=>{requests.push({path,method,body});return new Promise<T>(resolve=>finish=resolve);}});
  const pulling=sync.run();store.saveDeck({...d,name:'Concurrent local'});
  finish({owner_id:'A',records:[{...records[0],version:2,data:{...records[0].data,name:'Concurrent remote'}}],cursor:2,is_snapshot:false});await pulling;
  assert.equal(store.snapshot().decks[0].name,'Concurrent local');assert.equal(store.syncBaseline()[0].version,1);
  const pushing=sync.run();assert.equal(requests.at(-1).body.changes[0].base_version,1);assert.equal(requests.at(-1).body.since,1);
  finish({owner_id:'A',records:[],conflicts:[{...records[0],version:2}],cursor:2,is_snapshot:false});assert.equal((await pushing).state,'conflict');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('missing acknowledgement never clears a submitted local edit',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-missing-ack-')),store=await Store.open(join(root,'db'),'A');
 try{store.saveDeck({name:'Keep me'});const sync=new AccountSync(store,'A',{request:async<T>()=>({owner_id:'A',records:[],cursor:0,is_snapshot:false}) as T});assert.equal((await sync.run()).state,'error');assert.equal(store.snapshot().decks[0].name,'Keep me');assert.equal(store.syncBaseline().length,0);}
 finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('deltas delete tombstones but a reset preserves local data until explicit backed-up cloud replacement',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-delta-delete-')),store=await Store.open(join(root,'db'),'A');
 try{
  const a=store.saveDeck({name:'A'}),b=store.saveDeck({name:'B'}),records=exportRecords([a,b],[]).map(r=>({...r,version:1}));let reply:any={owner_id:'A',records,cursor:2,is_snapshot:true};const requests:any[]=[];
  const sync=new AccountSync(store,'A',{request:async<T>(path:string,method?:string,body?:unknown)=>{requests.push({path,method,body});return {...reply,is_snapshot:method==='POST'?false:reply.is_snapshot} as T;}});await sync.run();
  reply={owner_id:'A',records:[{...records[0],version:2,deleted:true,data:null}],cursor:3,is_snapshot:false};await sync.run();assert.deepEqual(store.snapshot().decks.map(d=>d.name),['B']);
  for(const resetCursor of [3,0]){reply={owner_id:'A',records:[],cursor:resetCursor,is_snapshot:true};assert.equal((await sync.run()).state,'conflict');assert.deepEqual(store.snapshot().decks.map(d=>d.name),['B']);assert.equal(store.syncCursor(),3);}
  await sync.useCloud();assert.equal(store.snapshot().decks.length,0);assert.equal(store.syncCursor(),0);
  const {readdirSync}=await import('node:fs');assert.ok(readdirSync(root).some(name=>name.includes('.before-cloud-')));
  reply={owner_id:'A',records:[]};await sync.run();await sync.run();assert.equal(requests.at(-1).path,'/owlai/account/sync');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('lost-response retries resend the same pending change and accept its exact acknowledgement',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-delta-retry-')),store=await Store.open(join(root,'db'),'A');
 try{
  let reply:any={owner_id:'A',records:[],cursor:0,is_snapshot:true};const requests:any[]=[];let fail=false;
  const sync=new AccountSync(store,'A',{request:async<T>(path:string,method?:string,body?:any)=>{requests.push({path,method,body});if(fail)throw new Error('response lost');return reply as T;}});await sync.run();
  store.saveDeck({name:'Pending'});fail=true;assert.equal((await sync.run()).state,'error');const submitted=requests.at(-1).body.changes;assert.equal(store.syncCursor(),0);
  fail=false;reply={owner_id:'A',records:submitted.map((c:any)=>({...c,version:1})),cursor:1,is_snapshot:false};assert.equal((await sync.run()).state,'synced');assert.deepEqual(requests.at(-1).body.changes,submitted);assert.equal(store.syncCursor(),1);
  reply={owner_id:'A',records:[],cursor:1,is_snapshot:false};await sync.run();assert.equal(requests.at(-1).method,'GET');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('first upload negotiates incremental support before a lost acknowledgement and retries safely',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-first-retry-')),store=await Store.open(join(root,'db'),'A');
 try{
  store.saveDeck({name:'First offline change'});const requests:any[]=[];let accepted:any[]=[];let loseReply=true;
  const sync=new AccountSync(store,'A',{request:async<T>(path:string,method?:string,body?:any)=>{
   requests.push({path,method,body});
   if(method==='GET')return {owner_id:'A',records:accepted,cursor:accepted.length?1:0,is_snapshot:true} as T;
   if(body.since===undefined)return {owner_id:'A',records:[],conflicts:accepted.length?accepted:[],cursor:1,is_snapshot:true} as T;
   accepted=body.changes.map((c:any)=>({...c,version:1}));if(loseReply){loseReply=false;throw new Error('acknowledgement lost');}
   return {owner_id:'A',records:accepted,cursor:1,is_snapshot:false} as T;
  }});
  assert.equal((await sync.run()).state,'error');assert.equal(requests[0].method,'GET');assert.equal(requests[1].body.since,0);assert.equal(store.syncCursor(),0);
  assert.equal((await sync.run()).state,'synced');assert.equal(requests[2].body.since,0);assert.equal(requests[2].body.changes[0].base_version,0);assert.equal(store.syncCursor(),1);assert.equal(store.snapshot().decks[0].name,'First offline change');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('legacy capability negotiation happens once before uploading pending changes',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-legacy-negotiate-')),store=await Store.open(join(root,'db'),'A');
 try{
  store.saveDeck({name:'Legacy upload'});const methods:string[]=[];
  const sync=new AccountSync(store,'A',{request:async<T>(_path:string,method?:string,body?:any)=>{methods.push(method!);return {owner_id:'A',records:method==='POST'?body.changes.map((c:any)=>({...c,version:1})):[]} as T;}});
  assert.equal((await sync.run()).state,'synced');assert.deepEqual(methods,['GET','POST']);assert.equal(store.syncCursor(),null);assert.equal(store.snapshot().decks[0].name,'Legacy upload');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('reset recovery stays required across retries and restart until cloud replacement succeeds',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-reset-required-')),path=join(root,'db');let store=await Store.open(path,'A');
 try{
  const d=store.saveDeck({name:'Preserved'}),baseline=exportRecords([d],[]).map(r=>({...r,version:1}));store.applySync([d],[],baseline,1);
  let requests=0,failRecovery=false;const transport={request:async<T>()=>{requests++;if(failRecovery)throw new Error('offline');return {owner_id:'A',records:[],cursor:1,is_snapshot:true} as T;}};
  let sync=new AccountSync(store,'A',transport);assert.equal((await sync.run()).state,'conflict');assert.equal(requests,1);
  assert.equal((await sync.run()).state,'conflict');assert.equal(requests,1);
  store.close();store=await Store.open(path,'A');sync=new AccountSync(store,'A',transport);assert.equal((await sync.run()).state,'conflict');assert.equal(requests,1);assert.equal(store.snapshot().decks[0].name,'Preserved');assert.deepEqual(store.syncBaseline(),baseline);assert.equal(store.syncCursor(),1);
  failRecovery=true;await assert.rejects(sync.useCloud(),/offline/);const failedRequests=requests;assert.equal((await sync.run()).state,'conflict');assert.equal(requests,failedRequests);assert.equal(store.snapshot().decks[0].name,'Preserved');
  failRecovery=false;await sync.useCloud();assert.equal(store.snapshot().decks.length,0);const recoveredRequests=requests;
  const ordinary=new AccountSync(store,'A',{request:async<T>()=>{requests++;return {owner_id:'A',records:[],cursor:1,is_snapshot:false} as T;}});assert.equal((await ordinary.run()).state,'synced');assert.equal(requests,recoveredRequests+1);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});
