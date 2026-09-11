import initSqlJs, { type Database, type SqlValue } from 'sql.js';
import { existsSync, readFileSync, writeFileSync, renameSync, copyFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { randomUUID } from 'node:crypto';
import type { Deck,Draft,Word,Settings,Snapshot,ReviewCard } from '../shared/types';
import { newCard,scheduleCard,studyDayStart } from './scheduler';
const defaults:Settings={nativeLanguage:'ru',learningLanguage:'en-us',theme:'light',accent:'indigo',dailyGoal:5,direction:'forward',dayStart:240,retention:.9,reminders:false,reminderTime:'19:00',keepInTray:false,launchAtLogin:false,apiBase:'https://api.mavrylo.com',onboardingComplete:false};
export class Store {
 private constructor(private db:Database,private path:string){}
 static async open(path:string):Promise<Store>{
  mkdirSync(dirname(path),{recursive:true});
  const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});
  const existed=existsSync(path); const db=new SQL.Database(existed?readFileSync(path):undefined);
  if(existed){const check=db.exec('PRAGMA integrity_check');if(check[0]?.values[0]?.[0]!=='ok')throw new Error('The card database needs recovery. Restore a backup; your existing file has been preserved.');}
  const version=Number(db.exec('PRAGMA user_version')[0]?.values[0]?.[0]??0);
  if(version>1)throw new Error('This database belongs to a newer Owl AI. Update the app before opening it.');
  if(version<1){if(existed)copyFileSync(path,path+'.pre-migration.bak');db.run(`BEGIN; CREATE TABLE IF NOT EXISTS decks(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS words(id TEXT PRIMARY KEY,deck_id TEXT NOT NULL REFERENCES decks(id) ON DELETE CASCADE,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS reviews(attempt TEXT PRIMARY KEY,word_id TEXT NOT NULL,direction TEXT NOT NULL,at TEXT NOT NULL,result TEXT NOT NULL); CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY CHECK(id=1),data TEXT NOT NULL); PRAGMA user_version=1; COMMIT;`);}
  db.run('PRAGMA foreign_keys=ON'); const store=new Store(db,path);store.activateOnlyDeck();store.persist();return store;
 }
 private rows<T>(sql:string,params:SqlValue[]=[]):T[]{const stmt=this.db.prepare(sql);try{stmt.bind(params);const result:T[]=[];while(stmt.step())result.push(stmt.getAsObject() as T);return result;}finally{stmt.free();}}
 private persist(){const tmp=this.path+'.tmp';writeFileSync(tmp,Buffer.from(this.db.export()));renameSync(tmp,this.path);}
 private activateOnlyDeck(){
  const rows=this.rows<{id:string,data:string}>('SELECT id,data FROM decks LIMIT 2');
  if(rows.length!==1)return;
  const deck=JSON.parse(rows[0].data) as Deck;
  if(!deck.active)this.db.run('UPDATE decks SET data=? WHERE id=?',[JSON.stringify({...deck,active:true}),rows[0].id]);
 }
 private transaction<T>(fn:()=>T):T {this.db.run('BEGIN');let result:T;try{result=fn();this.db.run('COMMIT');}catch(e){this.db.run('ROLLBACK');throw e;}this.persist();return result;}
 settings():Settings {const data=this.rows<{data:string}>('SELECT data FROM settings WHERE id=1')[0]?.data;return {...defaults,...(data?JSON.parse(data):{})};}
 saveSettings(patch:Partial<Settings>):Settings {const settings={...this.settings(),...patch};this.transaction(()=>this.db.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify(settings)]));return settings;}
 snapshot(now=new Date()):Snapshot {
  const settings=this.settings(), start=studyDayStart(now,settings.dayStart).toISOString();
  const decks=this.rows<{data:string}>('SELECT data FROM decks ORDER BY rowid DESC').map(x=>JSON.parse(x.data) as Deck);
  const words=this.rows<{data:string}>('SELECT data FROM words ORDER BY rowid DESC').map(x=>JSON.parse(x.data) as Word);
  const logs=this.rows<{at:string}>('SELECT at FROM reviews ORDER BY at DESC');
  const activityMap=new Map<string,number>();for(const log of logs){const day=studyDayStart(new Date(log.at),settings.dayStart).toLocaleDateString('en-CA');activityMap.set(day,(activityMap.get(day)??0)+1);}
  let streak=0;const cursor=studyDayStart(now,settings.dayStart);if(!activityMap.has(cursor.toLocaleDateString('en-CA')))cursor.setDate(cursor.getDate()-1);
  while(activityMap.has(cursor.toLocaleDateString('en-CA'))){streak++;cursor.setDate(cursor.getDate()-1);}
  return {decks,words,settings,reviewedToday:logs.filter(x=>x.at>=start).length,streak,activity:[...activityMap].map(([date,count])=>({date,count})).slice(0,100)};
 }
 saveDeck(input:Partial<Deck>&{name:string}):Deck {
  input=Object.fromEntries(Object.entries(input).filter(([,value])=>value!==undefined)) as typeof input;
  const decks=this.snapshot().decks,existing=input.id?decks.find(x=>x.id===input.id):undefined;
  if(input.id&&!existing)throw new Error('This set no longer exists.');
  const settings=this.settings();const deck:Deck={id:randomUUID(),description:'',active:true,nativeLanguage:settings.nativeLanguage,learningLanguage:settings.learningLanguage,createdAt:new Date().toISOString(),...existing,...input,name:input.name.trim()};
  if(!deck.name)throw new Error('Give your set a name.');
  if(decks.length===0||(existing&&decks.length===1))deck.active=true;
  this.transaction(()=>this.db.run('INSERT INTO decks VALUES(?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data',[deck.id,JSON.stringify(deck)]));return deck;
 }
 deleteDeck(id:string){this.transaction(()=>{this.db.run('DELETE FROM reviews WHERE word_id IN (SELECT id FROM words WHERE deck_id=?)',[id]);this.db.run('DELETE FROM words WHERE deck_id=?',[id]);this.db.run('DELETE FROM decks WHERE id=?',[id]);this.activateOnlyDeck();});}
 addWords(deckId:string,drafts:Draft[]):number {
  if(!this.snapshot().decks.some(x=>x.id===deckId))throw new Error('Choose an existing set.');
  const existing=new Set(this.snapshot().words.filter(x=>x.deckId===deckId).map(x=>x.word.normalize('NFKC').toLowerCase()));
  let added=0;this.transaction(()=>{for(const draft of drafts){const word=draft.word.trim(),translation=draft.translation.trim(),key=word.normalize('NFKC').toLowerCase();if(!word||!translation)throw new Error('Every selected card needs a word and translation.');if(existing.has(key))continue;const record:Word={...draft,word,translation,id:randomUUID(),deckId,createdAt:new Date().toISOString(),card:newCard(),reverse:newCard()};this.db.run('INSERT INTO words VALUES(?,?,?)',[record.id,deckId,JSON.stringify(record)]);existing.add(key);added++;}});return added;
 }
 private word(id:string):Word {const data=this.rows<{data:string}>('SELECT data FROM words WHERE id=?',[id])[0]?.data;if(!data)throw new Error('This card no longer exists.');return JSON.parse(data);}
 editWord(id:string,draft:Draft){const word=this.word(id);if(!draft.word.trim()||!draft.translation.trim())throw new Error('Enter both a word and translation.');this.transaction(()=>this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify({...word,...draft,word:draft.word.trim(),translation:draft.translation.trim()}),id]));}
 deleteWord(id:string){this.transaction(()=>{this.db.run('DELETE FROM reviews WHERE word_id=?',[id]);this.db.run('DELETE FROM words WHERE id=?',[id]);});}
 queue(deckId?:string,now=new Date()):Word[]{
  const {words,decks,settings}=this.snapshot(now);const start=studyDayStart(now,settings.dayStart);const end=new Date(start);end.setDate(end.getDate()+1);
  const introduced=new Set(this.rows<{word_id:string,result:string}>('SELECT word_id,result FROM reviews WHERE direction=? AND at>=?',[settings.direction,start.toISOString()]).filter(x=>JSON.parse(x.result).firstIntroduction).map(x=>x.word_id));
  let remaining=Math.max(0,settings.dailyGoal-introduced.size);const active=new Set(decks.filter(x=>x.active&&(!deckId||x.id===deckId)&&x.nativeLanguage===settings.nativeLanguage&&x.learningLanguage===settings.learningLanguage).map(x=>x.id));
  const card=(w:Word)=>settings.direction==='forward'?w.card:w.reverse;
  const candidates=words.filter(x=>active.has(x.deckId)).sort((a,b)=>new Date(card(a).due).getTime()-new Date(card(b).due).getTime());
  const due=candidates.filter(w=>{const c=card(w);return c.state!==0&&new Date(c.due)<(c.state===2?end:now);});
  const fresh=candidates.filter(w=>card(w).state===0).reverse().filter(()=>remaining-->0);
  return [...due,...fresh];
 }
 previews(id:string):Record<number,string>{const word=this.word(id),settings=this.settings(),card=settings.direction==='forward'?word.card:word.reverse,now=new Date();return Object.fromEntries([1,2,3,4].map(g=>[g,scheduleCard(card,g,now,settings.retention).due]));}
 review(id:string,grade:number,attempt:string):Word {
  const stored=this.rows<{result:string,word_id:string}>('SELECT result,word_id FROM reviews WHERE attempt=?',[attempt])[0];if(stored){if(stored.word_id!==id)throw new Error('Review attempt already belongs to another card.');return JSON.parse(stored.result).word;}
  const word=this.word(id),settings=this.settings(),key=settings.direction==='forward'?'card':'reverse',now=new Date();
  if(!this.queue(undefined,now).some(x=>x.id===id))throw new Error('This card is not currently due for review.');
  const previous=word[key];word[key]=scheduleCard(previous,grade,now,settings.retention);
  this.transaction(()=>{this.db.run('UPDATE words SET data=? WHERE id=?',[JSON.stringify(word),id]);this.db.run('INSERT INTO reviews VALUES(?,?,?,?,?)',[attempt,id,settings.direction,now.toISOString(),JSON.stringify({word,firstIntroduction:previous.reps===0})]);});return word;
 }
 backup(destination:string){if(destination.toLowerCase()===this.path.toLowerCase())throw new Error('Choose a different location for the backup.');this.persist();copyFileSync(this.path,destination);}
 async restore(source:string){
  const SQL=await initSqlJs({locateFile:()=>require.resolve('sql.js/dist/sql-wasm.wasm')});const candidate=new SQL.Database(readFileSync(source));
  try{if(candidate.exec('PRAGMA integrity_check')[0]?.values[0]?.[0]!=='ok'||candidate.exec('PRAGMA user_version')[0]?.values[0]?.[0]!==1)throw new Error('Choose a valid Owl AI backup from this app version.');for(const table of ['decks','words','reviews','settings'])candidate.exec(`SELECT * FROM ${table} LIMIT 1`);const check=new Store(candidate,source);check.snapshot();check.activateOnlyDeck();const current=this.settings();const restored={...check.settings(),apiBase:current.apiBase,launchAtLogin:current.launchAtLogin};candidate.run('INSERT OR REPLACE INTO settings VALUES(1,?)',[JSON.stringify(restored)]);copyFileSync(this.path,this.path+'.before-restore.bak');const tmp=this.path+'.restore.tmp';writeFileSync(tmp,Buffer.from(candidate.export()));renameSync(tmp,this.path);this.db.close();this.db=new SQL.Database(candidate.export());}finally{candidate.close();}
 }
 close(){this.persist();this.db.close();}
}
