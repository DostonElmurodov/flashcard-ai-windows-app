import type {AccountState,ReviewTranslation,ReviewTranslationRequest} from '../shared/types';
import {canonicalLanguage,secondaryReviewLanguage,reviewTranslationAccess} from '../shared/secondary-review';
import type {Store} from './store';
export interface TranslationWorkspace {store:Store;scope:string;apiBase:string;account:AccountState}

function context(workspace:TranslationWorkspace,wordId:string){
 const {words,decks}=workspace.store.snapshot();
 const word=words.find(row=>row.id===wordId),deck=decks.find(row=>row.id===word?.deckId);
 if(!word||!deck)throw new Error('This card no longer exists.');
 return previewContext(workspace,word.word,word.nativeLanguage??deck.nativeLanguage,word.learningLanguage??deck.learningLanguage);
}

function previewContext(workspace:TranslationWorkspace,word:string,nativeLanguage:string,learningLanguage:string){
 const native=canonicalLanguage(nativeLanguage),learning=canonicalLanguage(learningLanguage);
 const secondary=secondaryReviewLanguage(workspace.store.settings().secondaryReviewLanguage,native??'');
 if(!native||!learning)throw new Error('This card uses an unsupported language.');
 if(!secondary)throw new Error('Choose a second language in Settings first.');
 const input:ReviewTranslationRequest={word:word.trim().normalize('NFC'),native_language:native,learning_language:learning,secondary_language:secondary};
 const key=JSON.stringify([new URL(workspace.apiBase).origin,workspace.store.owner(),input.word,native,learning,secondary]);
 return {input,key};
}

function validated(value:unknown,language:string):ReviewTranslation {
 const item=value as Partial<ReviewTranslation>|null;
 if(!item||canonicalLanguage(item.language_code)!==language||typeof item.translation!=='string'||!item.translation.trim()||item.translation.length>5000||typeof item.explanation!=='string'||!item.explanation.trim()||item.explanation.length>5000)
  throw new Error('The second-language translation was incomplete. Please try again.');
 return {language_code:language,translation:item.translation.trim(),explanation:item.explanation.trim()};
}

export class ReviewTranslations {
 private pending=new Map<string,Promise<ReviewTranslation>>();
 constructor(private current:()=>TranslationWorkspace,private request:(input:ReviewTranslationRequest)=>Promise<unknown>){}
 async get(wordId:string):Promise<ReviewTranslation>{
  return this.load(workspace=>context(workspace,wordId));
 }
 async preview(word:string,native:string,learning:string):Promise<ReviewTranslation>{
  return this.load(workspace=>previewContext(workspace,word,native,learning));
 }
 private async load(resolve:(workspace:TranslationWorkspace)=>ReturnType<typeof context>):Promise<ReviewTranslation>{
  const workspace={...this.current()},{input,key}=resolve(workspace);
  const cached=workspace.store.cachedReviewTranslation(key);
  if(cached){try{return validated(cached,input.secondary_language);}catch{/* Replace an invalid old cache entry with a fresh response. */}}
  const access=reviewTranslationAccess(workspace.account);if(access)throw new Error(access);
  const pendingKey=JSON.stringify([workspace.scope,key]),existing=this.pending.get(pendingKey);if(existing)return existing;
  const pending=(async()=>{
   const result=validated(await this.request(input),input.secondary_language);
   const current=this.current();
   // Check identity before reading the store: account switching can close the old database.
   if(current.store!==workspace.store||current.scope!==workspace.scope||resolve(current).key!==key)
    throw new Error('The card or account changed. Please try again.');
   workspace.store.saveReviewTranslation(key,result);
   return result;
  })().finally(()=>{this.pending.delete(pendingKey);});
  this.pending.set(pendingKey,pending);return pending;
 }
}
