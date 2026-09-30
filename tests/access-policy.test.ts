import test from 'node:test';
import assert from 'node:assert/strict';
import {evaluateAccess,eligibleWordIds} from '../shared/access-policy';
import type {AccountState} from '../shared/types';
const now=new Date('2026-09-27T12:00:00Z');
const account=(status:string,expiry='2026-09-28T12:00:00Z',checked=now.toISOString()):AccountState=>({profile:{id:'a',email:null},testMode:false,entitlement:{status,expires_at:expiry,checked_at:checked,is_trial:status==='trial',auto_renew:false,was_ever_paid:status!=='trial'}});
test('fresh paid access allows mutation; elapsed expiry and stale confirmation do not',()=>{
 assert.equal(evaluateAccess(account('premium'),'add',101,now).allow,true);
 for(const state of [account('premium',now.toISOString()),account('premium',undefined,'2026-09-27T10:00:00Z'),account('premium','invalid')])assert.equal(evaluateAccess(state,'add',101,now).allow,false);
});
test('ordinary expired paid retains saved read/review only; inactive and unknown statuses stay bounded',()=>{
 const words=Array.from({length:101},(_,i)=>({id:String(i).padStart(3,'0'),createdAt:now.toISOString()})).reverse();
 const paid=account('expired_paid');assert.equal(eligibleWordIds(paid,words,now).size,101);
 for(const operation of ['add','edit','ai'] as const)assert.equal(evaluateAccess(paid,operation,0,now).allow,false);
 for(const status of ['free','expired_trial','revoked','invalid_subscription','unexpected'])assert.deepEqual([...eligibleWordIds(account(status),words,now)],Array.from({length:10},(_,i)=>String(i).padStart(3,'0')));
 assert.equal(evaluateAccess(paid,'delete',101,now).allow,true);assert.equal(evaluateAccess(paid,'export',101,now).allow,true);
});
test('free admission is global and desktop mutation requires an account',()=>{
 assert.equal(evaluateAccess(account('free'),'add',9,now).allow,true);assert.equal(evaluateAccess(account('free'),'add',10,now).allow,false);
 assert.equal(evaluateAccess({...account('premium'),profile:null,testMode:true},'add',0,now).allow,false);
 assert.equal(evaluateAccess(account('free'),'ai',0,now).allow,false);
});

const live=(status:string)=>account(status,new Date(Date.now()+86400000).toISOString(),new Date().toISOString());
import {Store} from '../electron/store';
import {mkdtempSync,rmSync} from 'node:fs';import {tmpdir} from 'node:os';import {join} from 'node:path';
test('store guards manual and staged imports globally and atomically; catalog cannot leave a deck',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-access-'));const store=await Store.open(join(root,'db.sqlite'),'account');let state=live('free');
 try{
  store.bindAccess(()=>state);const deck=store.saveDeck({name:'one'});
  store.addWords(deck.id,Array.from({length:9},(_,i)=>({word:'word'+i,translation:'value'})));
  assert.throws(()=>store.addWords(deck.id,[{word:'ten',translation:'value'},{word:'eleven',translation:'value'}]),/10/);assert.equal(store.snapshot().words.length,9);
  const next=store.saveDeck({name:'two',learningLanguage:'es'});store.addWords(next.id,[{word:'ten',translation:'value'}]);
  assert.throws(()=>store.addWords(next.id,[{word:'eleven',translation:'value'}]),/10/);
  const before=store.snapshot();assert.throws(()=>store.importDeck({name:'catalog'},[{word:'catalog',translation:'value'}]),/10/);assert.deepEqual(store.snapshot().decks,before.decks);
  state=live('premium');store.addWords(deck.id,[{word:'paid',translation:'value'}]);state=live('expired_paid');
  assert.throws(()=>store.addWords(deck.id,[{word:'staged',translation:'value'}]));assert.throws(()=>store.editWord(store.snapshot().words[0].id,{word:'changed',translation:'value'}));
  const saved=store.snapshot();state=live('revoked');assert.equal(store.eligibleIds().size,10);assert.equal(store.snapshot().words.length,11);
  assert.throws(()=>store.editWord(saved.words.find(w=>!store.eligibleIds().has(w.id))!.id,{word:'changed',translation:'value'}));
  store.deleteWord(saved.words[0].id);assert.equal(store.snapshot().words.length,10);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});
test('101 synced cards and backups survive restrictions; paid read history survives scoped offline relaunch',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-access-')),path=join(root,'db.sqlite');let store=await Store.open(path,'origin-account');
 try{
  const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:101},(_,i)=>({word:'word'+i,translation:'value'})));
  store.bindAccess(()=>live('premium'));store.rememberAccess(live('premium'));store.close();store=await Store.open(path,'origin-account');
  store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,101);assert.throws(()=>store.addWords(deck.id,[{word:'staged',translation:'value'}]));
  store.rememberAccess(live('invalid_subscription'));assert.equal(store.eligibleIds().size,10);assert.equal(store.snapshot().words.length,101);
  assert.throws(()=>store.editWord(store.snapshot().words[0].id,{word:'changed',translation:'value'}));
  store.backup(join(root,'backup.sqlite'));await store.restore(join(root,'backup.sqlite'));assert.equal(store.snapshot().words.length,101);
  const snap=store.snapshot();store.applySync(snap.decks,snap.words,[]);assert.equal(store.snapshot().words.length,101);
  const other=await Store.open(join(root,'other.sqlite'),'different-origin-account');other.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(other.accessState()?.entitlement,null);other.close();
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('expired trial, revoked, invalid and unknown cannot mutate even below10',()=>{
 for(const status of ['expired_trial','revoked','invalid_subscription','unexpected'])for(const operation of ['add','edit','ai'] as const)assert.equal(evaluateAccess(account(status),operation,0,now).allow,false);
});

test('authoritative inactive read state replaces old paid history across offline relaunch',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-history-')),path=join(root,'db.sqlite');let store=await Store.open(path,'scope');
 try{
  const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:11},(_,i)=>({word:'word'+i,translation:'value'})));
  for(const status of ['revoked','invalid_subscription','free']){
   store.rememberAccess(live('premium'));store.rememberAccess({...live(status),entitlement:{...live(status).entitlement!,was_ever_paid:false}});store.close();store=await Store.open(path,'scope');store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,10);assert.equal(store.accessState()?.entitlement?.status,status);
  }
  store.rememberAccess({...live('premium'),entitlement:{...live('premium').entitlement!,expires_at:'invalid'}});store.close();store=await Store.open(path,'scope');store.bindAccess(()=>({...live('free'),entitlement:null}));assert.equal(store.eligibleIds().size,10);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('free deck language/content changes cannot edit locked cards; study activation remains allowed',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-deck-'));const store=await Store.open(join(root,'db.sqlite'),'scope');
 try{const deck=store.saveDeck({name:'saved'});store.addWords(deck.id,Array.from({length:11},(_,i)=>({word:'word'+i,translation:'value'})));store.bindAccess(()=>live('free'));
 assert.throws(()=>store.saveDeck({...deck,learningLanguage:'es'}));assert.equal(store.saveDeck({...deck,active:false}).id,deck.id);assert.equal(store.snapshot().words.length,11);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('global creation ordering compares instants across timezone offsets before ID ties',()=>{
 const early=Array.from({length:9},(_,i)=>({id:'early'+i,createdAt:'2026-09-26T12:00:00Z'}));
 const words=[...early,{id:'later',createdAt:'2026-09-27T11:30:00Z'},{id:'offset',createdAt:'2026-09-27T13:00:00+02:00'}];const eligible=eligibleWordIds(account('free'),words,now);assert.equal(eligible.has('offset'),true);assert.equal(eligible.has('later'),false);
});
