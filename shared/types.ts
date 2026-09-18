export interface Draft { word:string; translation:string; pronunciation?:string; examples?:string[]; notes?:string }
export interface Deck { id:string; name:string; description:string; nativeLanguage:string; learningLanguage:string; active:boolean; createdAt:string }
export interface ReviewCard { due:string; stability:number; difficulty:number; elapsed_days:number; scheduled_days:number; reps:number; lapses:number; state:number; last_review?:string; learning_steps:number }
export interface Word extends Draft { nativeLanguage?:string; learningLanguage?:string; id:string; deckId:string; createdAt:string; card:ReviewCard; reverse:ReviewCard }
export interface Settings { nativeLanguage:string; learningLanguage:string; theme:'light'|'dark'|'system'; accent:'indigo'|'teal'|'rose'; darkAccent:'indigo'|'teal'|'rose'; dailyGoal:number; direction:'forward'|'reverse'; dayStart:number; retention:number; reminders:boolean; reminderTime:string; reminderStart:string; reminderEnd:string; reminderCount:number; reminderLastSlot?:string; keepInTray:boolean; launchAtLogin:boolean; apiBase:string; onboardingComplete:boolean }
export interface Snapshot { workspaceId:string; scopeRevision:string; account?:AccountState; queueCount?:number; decks:Deck[]; words:Word[]; settings:Settings; reviewedToday:number; streak:number; activity:{date:string;count:number}[] }
export interface Profile { id:string; email:string|null; display_name?:string; provider?:string }
export interface Entitlement { status:string; product_id?:string|null; expires_at?:string|null; is_trial:boolean; auto_renew:boolean; was_ever_paid:boolean; source?:string|null; checked_at?:string }
export interface SyncStatus { state:'idle'|'syncing'|'synced'|'conflict'|'error'; lastSyncedAt?:string; message?:string }
export interface AccountState { profile:Profile|null; entitlement:Entitlement|null; testMode:boolean; sync?:SyncStatus }
export interface CatalogCard { client_card_id:string; word:string; translations:string[]; pronunciation?:string; examples:string[]; notes?:string; native_language:string; learning_language:string }
export interface CatalogDeck { id:string; title:string; description?:string; word_count:number; cards:CatalogCard[] }
export interface Bridge { forWorkspace(scopeRevision:string):Bridge; activateWorkspace(scopeRevision:string):void; resolveSync():Promise<SyncStatus>; openAppMenu(name:'Owl AI'|'Edit'|'View',x:number,y:number):Promise<void>; systemTimeFormat():Promise<{hour12:boolean}>;
 loginGoogle():Promise<AccountState|null>; cancelGoogleLogin():Promise<void>;
 snapshot():Promise<Snapshot>; saveDeck(input:Partial<Deck>&{name:string}):Promise<Deck>; deleteDeck(id:string):Promise<void>;
 addWords(deckId:string,words:Draft[]):Promise<number>; editWord(id:string,word:Draft):Promise<void>; deleteWord(id:string):Promise<void>;
 saveSettings(settings:Partial<Settings>):Promise<Settings>; queue(deckId?:string):Promise<Word[]>; previews(wordId:string):Promise<Record<number,string>>; review(wordId:string,grade:number,attemptId:string):Promise<Word>; reviewSession(active:boolean):Promise<void>;
 importFile(mode:'auto'|'pairs'|'words'):Promise<{name:string;text:string;drafts:Draft[]}|null>; parse(text:string,mode:'auto'|'pairs'|'words'):Promise<Draft[]>; exportDeck(id:string):Promise<boolean>;
 backup():Promise<boolean>; restore():Promise<boolean>; account():Promise<AccountState>; sync():Promise<SyncStatus>; login(email:string,password:string):Promise<AccountState>; logout():Promise<void>; deleteAccount():Promise<void>; refreshEntitlement():Promise<AccountState>;
 translate(word:string,native:string,learning:string):Promise<Draft>; catalog(query:string):Promise<CatalogDeck[]>; importCatalog(deck:CatalogDeck):Promise<Deck>;
 publish(id:string):Promise<{status:string}>; unpublish(id:string):Promise<void>; openSubscriptionManagement():Promise<void>;
}
declare global { interface Window { owl:Bridge } }
export const languages=[['en-us','English'],['es','Spanish'],['tr','Turkish'],['ru','Russian'],['it','Italian'],['de','German'],['fr','French'],['ja','Japanese'],['zh','Chinese'],['yue','Cantonese'],['pt','Portuguese'],['hi','Hindi'],['bn','Bengali'],['id','Indonesian'],['ur','Urdu'],['vi','Vietnamese'],['ko','Korean'],['uk','Ukrainian'],['pl','Polish'],['tg','Tajik'],['uz','Uzbek'],['az','Azerbaijani'],['kk','Kazakh'],['hy','Armenian'],['ar','Arabic']];
export const languageName=(code:string)=>languages.find(x=>x[0]===code)?.[1]??code;
