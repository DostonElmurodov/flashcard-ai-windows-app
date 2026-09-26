import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Api} from '../electron/api';
import {Store} from '../electron/store';
import {exportRecords,decodeRows} from '../electron/sync';

test('AI part of speech survives local save and sync metadata round trip',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-pos-')),store=await Store.open(join(root,'db'),'A');
 try{
  const api=new Api(join(root,'account.enc'),()=> 'https://example.test');
  api.request=async<T>()=>({corrected_word:'bright',translation:'яркий',part_of_speech:' adjective '} as T);
  const draft=await api.translate('bright','ru','en-us');assert.equal((draft as any).partOfSpeech,'adjective');
  const deck=store.saveDeck({name:'General'});store.addWords(deck.id,[draft]);
  const snap=store.snapshot(),rows=exportRecords(snap.decks,snap.words);
  assert.equal((rows.find(r=>r.kind==='word')!.data!.metadata as any).part_of_speech,'adjective');
  const decoded=decodeRows(rows);assert.equal((decoded.words[0] as any).partOfSpeech,'adjective');
  store.applySync(decoded.decks,decoded.words,rows);
  store.editWord(decoded.words[0].id,{...decoded.words[0],word:'run',translation:'бежать'});
  const changed=store.snapshot(),out=exportRecords(changed.decks,changed.words,rows);
  assert.equal((changed.words[0] as any).partOfSpeech,undefined);
  assert.equal((out.find(r=>r.kind==='word')!.data!.metadata as any)?.part_of_speech,undefined);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('legacy sync metadata stays intact and missing part of speech is optional',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-pos-legacy-')),store=await Store.open(join(root,'db'),'A');
 try{
  const deck=store.saveDeck({name:'General'});store.addWords(deck.id,[{word:'word',translation:'слово'}]);const snap=store.snapshot();
  const rows=exportRecords(snap.decks,snap.words);rows[1].data!.metadata={ios_set_ids:['extra'],part_of_speech:'noun'};
  const exported=exportRecords(snap.decks,snap.words,rows);assert.deepEqual(exported[1].data!.metadata,{ios_set_ids:['extra'],part_of_speech:'noun'});
  assert.equal((decodeRows(rows).words[0] as any).partOfSpeech,'noun');
  delete (rows[1].data!.metadata as any).part_of_speech;assert.equal((decodeRows(rows).words[0] as any).partOfSpeech,undefined);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});

test('older clients cannot erase a known label but changed word identity clears it',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-pos-old-')),store=await Store.open(join(root,'db'),'A');
 try{
  const deck=store.saveDeck({name:'General'});store.addWords(deck.id,[{word:'bright',translation:'яркий',partOfSpeech:'adjective'}]);
  const snap=store.snapshot(),rows=exportRecords(snap.decks,snap.words);delete (rows[1].data!.metadata as any).part_of_speech;
  const decoded=decodeRows(rows);store.applySync(decoded.decks,decoded.words,rows);assert.equal(store.snapshot().words[0].partOfSpeech,'adjective');
  decoded.words[0].word='run';store.applySync(decoded.decks,decoded.words,rows);assert.equal(store.snapshot().words[0].partOfSpeech,undefined);
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});


test('changing inherited collection languages clears labels but explicit word languages keep them',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-pos-language-')),store=await Store.open(join(root,'db'),'A');
 try{
  const deck=store.saveDeck({name:'General',learningLanguage:'en-us'});store.addWords(deck.id,[{word:'bright',translation:'яркий',partOfSpeech:'adjective'}]);
  const snap=store.snapshot(),rows=exportRecords(snap.decks,snap.words);
  store.saveDeck({...deck,learningLanguage:'fr'});let updated=store.snapshot();assert.equal(updated.words[0].partOfSpeech,undefined);assert.equal((exportRecords(updated.decks,updated.words,rows)[1].data!.metadata as any)?.part_of_speech,undefined);
  const explicit={...snap.words[0],nativeLanguage:deck.nativeLanguage,learningLanguage:'en-us'};store.applySync([deck],[explicit],rows);
  store.saveDeck({...deck,learningLanguage:'fr'});assert.equal(store.snapshot().words[0].partOfSpeech,'adjective');
 }finally{store.close();rmSync(root,{recursive:true,force:true});}
});
