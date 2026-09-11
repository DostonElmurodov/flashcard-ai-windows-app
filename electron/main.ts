import { app,BrowserWindow,ipcMain,dialog,Menu,Notification,Tray,nativeImage,shell } from 'electron';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { z } from 'zod';
import { Store } from './store';
import {systemTimeFormat} from './time-format';
import {reminderSlot} from './reminders';
import { Api } from './api';
import { googleSignIn } from './google';
declare const GOOGLE_OAUTH_CLIENT_ID:string;
declare const GOOGLE_OAUTH_CLIENT_SECRET:string;
let googleAttempt:AbortController|null=null;
import { parseImport } from './imports';
import { importFile,exportDeck } from './files';
import type { CatalogDeck } from '../shared/types';
let window:BrowserWindow,store:Store,api:Api,tray:Tray|null=null,quitting=false;
if(process.env.OWL_TEST_DATA_DIR)app.setPath('userData',process.env.OWL_TEST_DATA_DIR);
const id=z.string().min(1).max(200),str=z.string().trim().min(1).max(500),language=z.string().regex(/^[a-z]{2,3}(-[a-z0-9]{2,8})?$/);
const draft=z.object({word:str,translation:z.string().trim().min(1).max(5000),pronunciation:z.string().max(500).optional(),examples:z.array(z.string().max(3000)).max(20).optional(),notes:z.string().max(10000).optional()});
const mode=z.enum(['auto','pairs','words']);
const apiBase=z.string().url().refine(value=>{const url=new URL(value);return !url.username&&!url.password&&!url.search&&!url.hash&&url.pathname==='/'&&(url.protocol==='https:'||(url.protocol==='http:'&&['localhost','127.0.0.1','[::1]'].includes(url.hostname)));},'Use an HTTPS server origin or localhost.');
function handle(name:string,fn:(...args:any[])=>unknown){ipcMain.handle('owl:'+name,async(event,...args)=>{if(event.sender!==window.webContents||event.senderFrame!==window.webContents.mainFrame)return {ok:false,error:'Untrusted window.'};try{return {ok:true,value:await fn(...args)};}catch(error){return {ok:false,error:error instanceof z.ZodError?'Please check the entered values.':error instanceof Error?error.message:'The operation could not be completed.'};}});}
function register(){
 handle('systemTimeFormat',()=>systemTimeFormat(app.getSystemLocale()));
 handle('snapshot',()=>store.snapshot());
 handle('saveDeck',input=>store.saveDeck(z.object({id:id.optional(),name:str,description:z.string().max(2000).optional(),nativeLanguage:language.optional(),learningLanguage:language.optional(),active:z.boolean().optional()}).parse(input)));
 handle('deleteDeck',value=>store.deleteDeck(id.parse(value)));
 handle('addWords',(deck,rows)=>store.addWords(id.parse(deck),z.array(draft).min(1).max(2000).parse(rows)));
 handle('editWord',(value,row)=>store.editWord(id.parse(value),draft.parse(row)));
 handle('deleteWord',value=>store.deleteWord(id.parse(value)));
 handle('queue',value=>store.queue(id.optional().parse(value)));
 handle('previews',value=>store.previews(id.parse(value)));
 handle('review',(value,grade,attempt)=>store.review(id.parse(value),z.number().int().min(1).max(4).parse(grade),id.parse(attempt)));
 handle('parse',(text,m)=>parseImport(z.string().max(5_000_000).parse(text),mode.parse(m)));
 handle('importFile',m=>importFile(window,mode.parse(m),store.settings().learningLanguage));
 handle('exportDeck',value=>exportDeck(window,store,id.parse(value)));
 handle('saveSettings',async value=>{const patch=z.object({nativeLanguage:language,learningLanguage:language,theme:z.enum(['light','dark','system']),accent:z.enum(['indigo','teal','rose']),darkAccent:z.enum(['indigo','teal','rose']),dailyGoal:z.number().int().min(0).max(200),direction:z.enum(['forward','reverse']),dayStart:z.number().int().min(0).max(1439),retention:z.number().min(.7).max(.97),reminders:z.boolean(),reminderTime:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderStart:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderEnd:z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/),reminderCount:z.number().int().min(1).max(100),keepInTray:z.boolean(),launchAtLogin:z.boolean(),apiBase,onboardingComplete:z.boolean()}).partial().parse(value);if(patch.apiBase&&patch.apiBase!==store.settings().apiBase)await api.logout();if(patch.apiBase&&patch.apiBase!==store.settings().apiBase)api.clear();const settings=store.saveSettings(patch);if(patch.launchAtLogin!==undefined)configureLogin();configureTray();return settings;});
 handle('backup',async()=>{const result=await dialog.showSaveDialog(window,{title:'Back up your cards and progress',defaultPath:'Owl-AI-backup.sqlite',filters:[{name:'Owl AI backup',extensions:['sqlite']}]});if(result.canceled||!result.filePath)return false;store.backup(result.filePath);return true;});
 handle('restore',async()=>{const result=await dialog.showOpenDialog(window,{title:'Restore Owl AI backup',properties:['openFile'],filters:[{name:'Owl AI backup',extensions:['sqlite']}]});if(result.canceled)return false;const confirm=await dialog.showMessageBox(window,{type:'warning',message:'Replace local cards and progress with this backup?',detail:'A copy of your current database will be kept. Your account is not changed.',buttons:['Cancel','Restore backup'],defaultId:0,cancelId:0});if(confirm.response!==1)return false;await store.restore(result.filePaths[0]);configureTray();return true;});
 handle('account',()=>api.state());
 handle('loginGoogle',async()=>{
  if(googleAttempt)throw new Error('Google sign-in is already open in your browser.');
  const controller=new AbortController();googleAttempt=controller;
  try{return await api.loginGoogle(()=>googleSignIn({clientId:GOOGLE_OAUTH_CLIENT_ID,clientSecret:GOOGLE_OAUTH_CLIENT_SECRET},url=>shell.openExternal(url),{signal:controller.signal}),controller.signal);}
  catch(error){if(controller.signal.aborted)return null;if(error instanceof Error&&error.name==='TimeoutError')throw new Error('Google sign-in timed out. Please try again.');throw error;}
  finally{googleAttempt=null;if(!window.isDestroyed()){window.show();window.focus();}}
 });
 handle('cancelGoogleLogin',()=>{googleAttempt?.abort();});
 handle('login',(email,password,confirmation)=>api.login(z.string().email().max(254).parse(email),z.string().min(1).max(1024).parse(password),z.string().max(1024).optional().parse(confirmation)));
 handle('logout',()=>api.logout());handle('deleteAccount',()=>api.deleteAccount());handle('refreshEntitlement',()=>api.refreshEntitlement());
 handle('translate',(word,native,learning)=>api.translate(str.parse(word),language.parse(native),language.parse(learning)));
 handle('catalog',query=>api.catalog(z.string().max(200).parse(query)));
 handle('importCatalog',input=>{const item=z.object({id,title:str,description:z.string().max(4000).nullish(),cards:z.array(z.object({word:str,translations:z.array(z.string().max(5000)).min(1).max(30),pronunciation:z.string().max(500).nullish(),examples:z.array(z.string().max(3000)).max(20),notes:z.string().max(10000).nullish(),native_language:language,learning_language:language})).min(1).max(2000)}).parse(input);const deck=store.saveDeck({name:item.title,description:item.description??'',nativeLanguage:item.cards[0].native_language,learningLanguage:item.cards[0].learning_language});try{store.addWords(deck.id,item.cards.map(c=>({word:c.word,translation:c.translations.join('; '),pronunciation:c.pronunciation??undefined,examples:c.examples,notes:c.notes??undefined})));return deck;}catch(error){store.deleteDeck(deck.id);throw error;}});
 handle('publish',value=>{const key=id.parse(value),snap=store.snapshot(),deck=snap.decks.find(x=>x.id===key);if(!deck)throw new Error('Set not found.');const cards=snap.words.filter(x=>x.deckId===key);if(!cards.length)throw new Error('Add some cards before publishing.');return api.request('/owlai/account/public-flashcard-sets/publish','POST',{client_set_id:deck.id,title:deck.name,description:deck.description,cards:cards.map(w=>({client_card_id:w.id,word:w.word,translations:[w.translation],pronunciation:w.pronunciation,examples:w.examples??[],example_translations:(w.examples??[]).map(()=>null),notes:w.notes,native_language:deck.nativeLanguage,learning_language:deck.learningLanguage}))});});
 handle('unpublish',value=>api.request('/owlai/account/public-flashcard-sets/unpublish','POST',{client_set_id:id.parse(value)}));
 handle('openSubscriptionManagement',()=>shell.openExternal('https://apps.apple.com/account/subscriptions'));
}
function configureLogin(){if(app.isPackaged&&!process.env.OWL_TEST_DATA_DIR)app.setLoginItemSettings({openAtLogin:store.settings().launchAtLogin,path:process.env.PORTABLE_EXECUTABLE_FILE??process.execPath});}
function configureTray(){if(store.settings().keepInTray&&!tray){const svg='<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32"><rect width="32" height="32" rx="9" fill="#5144b8"/><circle cx="11" cy="14" r="6" fill="white"/><circle cx="21" cy="14" r="6" fill="white"/><circle cx="12" cy="14" r="2"/><circle cx="20" cy="14" r="2"/><path d="m13 21 3 4 3-4" fill="#f3b65a"/></svg>';tray=new Tray(nativeImage.createFromPath(join(__dirname,'../build/icon.png')).resize({width:32,height:32}));tray.setToolTip('Owl AI');tray.setContextMenu(Menu.buildFromTemplate([{label:'Open Owl AI',click:()=>window.show()},{label:'Quit',click:()=>app.quit()}]));tray.on('double-click',()=>window.show());}else if(!store.settings().keepInTray&&tray){tray.destroy();tray=null;}}
if(!app.requestSingleInstanceLock())app.quit();else{
 app.on('second-instance',()=>{window?.show();window?.focus();});
 app.whenReady().then(async()=>{try{
  app.setAppUserModelId('com.mavrylo.owlai.windows');store=await Store.open(join(app.getPath('userData'),'owl.sqlite'));api=new Api(join(app.getPath('userData'),'account.enc'),()=>store.settings().apiBase);
  window=new BrowserWindow({width:1380,height:930,minWidth:940,minHeight:680,title:'Owl AI',icon:join(__dirname,'../build/icon.png'),backgroundColor:'#f8f7fc',show:false,webPreferences:{preload:join(__dirname,'preload.cjs'),nodeIntegration:false,contextIsolation:true,sandbox:true}});
  Menu.setApplicationMenu(Menu.buildFromTemplate([{label:'Owl AI',submenu:[{role:'about'},{type:'separator'},{role:'quit'}]},{label:'Edit',submenu:[{role:'undo'},{role:'redo'},{type:'separator'},{role:'cut'},{role:'copy'},{role:'paste'},{role:'selectAll'}]},{label:'View',submenu:[{role:'resetZoom'},{role:'zoomIn'},{role:'zoomOut'},{role:'togglefullscreen'}]}]));
  window.webContents.setWindowOpenHandler(()=>({action:'deny'}));window.webContents.on('will-navigate',event=>event.preventDefault());window.webContents.session.setPermissionRequestHandler((_web,permission,callback)=>callback(permission==='notifications'));
  register();configureLogin();configureTray();await window.loadFile(join(__dirname,'../dist/index.html'));window.show();
  window.on('close',event=>{if(!quitting&&store.settings().keepInTray){event.preventDefault();window.hide();}});
  let lastReminder=store.settings().reminderLastSlot??'';setInterval(()=>{const settings=store.settings(),now=new Date(),key=reminderSlot(now,settings.reminderStart,settings.reminderEnd,settings.reminderCount);if(settings.reminders&&key&&lastReminder!==key&&Notification.isSupported()){const count=store.queue().length;if(count){lastReminder=key;const notification=new Notification({title:'A little practice goes a long way',body:`You have ${count} cards ready to review in Owl AI.`});notification.on('click',()=>window.show());notification.show();store.saveSettings({reminderLastSlot:key});}}},15000);
 }catch(error){dialog.showErrorBox('Owl AI could not start',error instanceof Error?error.message:String(error));app.quit();}});
 app.on('before-quit',()=>{quitting=true;googleAttempt?.abort();});app.on('will-quit',()=>{store?.close();});app.on('window-all-closed',()=>{if(!tray)app.quit();});
}
