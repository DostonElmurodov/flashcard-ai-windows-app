import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {mkdirSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {_electron as electron} from 'playwright';
import {Store} from '../electron/store';
async function main(){
 const root=resolve(process.env.OWL_ACCESS_RESULTS??'test-results/paid-access'),profile=join(root,'profile-'+Date.now());mkdirSync(profile,{recursive:true});
 let status='free',aiCalls=0;const records=new Map<string,any>();
 const server=createServer(async(req,res)=>{
  res.setHeader('Content-Type','application/json');
  if(req.url==='/owlai/config/feature-flags'){res.end(JSON.stringify({test_mode:false}));return;}
  if(req.url==='/owlai/account/desktop/email/session'){res.end(JSON.stringify({access_token:'local-fake',refresh_token:'local-fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'local-a',email:'local@example.test'}}));return;}
  if(req.url==='/owlai/account/entitlement'){res.end(JSON.stringify({status,expires_at:new Date(Date.now()+(status==='premium'?86400000:-1000)).toISOString(),is_trial:false,auto_renew:false,was_ever_paid:status==='premium'||status==='expired_paid'}));return;}
  if(req.url?.startsWith('/owlai/account/sync')){let text='';for await(const chunk of req)text+=chunk;for(const row of (text?JSON.parse(text).changes:[])??[])records.set(row.kind+':'+row.id,{...row,version:row.base_version+1});res.end(JSON.stringify({owner_id:'local-a',records:[...records.values()],conflicts:[],cursor:1,is_snapshot:false}));return;}
  if(req.url?.startsWith('/owlai/account/ai/')){aiCalls++;res.end(JSON.stringify({language_code:'es',translation:'viaje',explanation:'Un desplazamiento.'}));return;}
  res.statusCode=404;res.end('{}');
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));const address=server.address();assert.ok(address&&typeof address!=='string');const origin=`http://127.0.0.1:${address.port}`;
 const guest=await Store.open(join(profile,'owl.sqlite'));guest.saveSettings({apiBase:origin,onboardingComplete:true,keepInTray:false,launchAtLogin:false,reminders:false,dailyGoal:200});guest.close();
 let app:Awaited<ReturnType<typeof electron.launch>>|undefined;
 const launch=async()=>{app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:profile},timeout:30000});const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);return page;};
 try{
  let page=await launch();
  const call=async(name:string,args:unknown[]=[])=>page.evaluate(async({name,args})=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);return (window.owl.forWorkspace(snapshot.scopeRevision) as any)[name](...args);},{name,args});
  await assert.rejects(call('saveDeck',[{name:'renderer-forgery',testMode:true,entitlement:{status:'premium'}}]),/Sign in/);
  await call('login',['local@example.test','fixture-only']);await call('refreshEntitlement');
  const deck=await call('saveDeck',[{name:'manual'}]);await call('addWords',[deck.id,Array.from({length:9},(_,i)=>({word:'word'+i,translation:'value'}))]);
  await assert.rejects(call('addWords',[deck.id,[{word:'ten',translation:'value'},{word:'eleven',translation:'value'}]]),/10/);assert.equal((await call('snapshot')).words.length,9);
  await call('addWords',[deck.id,[{word:'ten',translation:'value'}]]);await call('saveSettings',[{learningLanguage:'es'}]);const other=await call('saveDeck',[{name:'another language',learningLanguage:'es'}]);
  await assert.rejects(call('addWords',[other.id,[{word:'bypass',translation:'value',testMode:true}]]),/10/);
  const before=await call('snapshot');await assert.rejects(call('importCatalog',[{id:'catalog',title:'catalog',cards:[{word:'catalog',translations:['value'],examples:[],native_language:'ru',learning_language:'en-us'}]}]),/10/);assert.equal((await call('snapshot')).decks.length,before.decks.length);
  status='premium';await call('refreshEntitlement');await call('saveSettings',[{learningLanguage:'en-us',secondaryReviewLanguage:'es'}]);await call('addWords',[deck.id,Array.from({length:91},(_,i)=>({word:'paid'+i,translation:'value'}))]);const paid=await call('snapshot');assert.equal(paid.words.length,101);assert.equal(paid.accessibleWordIds.length,101);
  const queue=await call('queue'),cachedId=queue[0].id;await call('reviewTranslation',[cachedId]);assert.equal(aiCalls,1);
  status='expired_paid';await call('refreshEntitlement');assert.equal((await call('snapshot')).accessibleWordIds.length,101);assert.equal((await call('reviewTranslation',[cachedId])).translation,'viaje');
  await assert.rejects(call('addWords',[deck.id,[{word:'staged',translation:'value'}]]));await assert.rejects(call('editWord',[cachedId,{word:'changed',translation:'value'}]));await assert.rejects(call('translate',['new','ru','en-us']));assert.equal(aiCalls,1);
  await call('review',[cachedId,3,'paid-review']);await app!.evaluate(({dialog},path)=>{dialog.showSaveDialog=async()=>({canceled:false,filePath:path});},join(root,'export.csv'));assert.equal(await call('exportDeck',[deck.id]),true);
  await app!.evaluate(({dialog},path)=>{dialog.showSaveDialog=async()=>({canceled:false,filePath:path});},join(root,'backup.sqlite'));assert.equal(await call('backup'),true);
  await call('deleteWord',[paid.words.find((w:any)=>w.id!==cachedId).id]);await app!.close();app=undefined;await new Promise<void>(resolve=>server.close(()=>resolve()));
  page=await launch();const offline=await call('snapshot');assert.equal(offline.words.length,100);assert.equal(offline.accessibleWordIds.length,100);assert.equal((await call('reviewTranslation',[cachedId])).translation,'viaje');await assert.rejects(call('addWords',[deck.id,[{word:'offline',translation:'value'}]]));
  await page.screenshot({path:join(root,'offline-paid.png'),fullPage:true});console.log('PASS: actual Electron scoped IPC, renderer forgery denial, atomic manual/staged/catalog save guards, global10 across languages, paid101 preserved, expired paid cached read/review/delete/export/backup, and offline relaunch100 without mutation; only one fake AI call.');
 }finally{await app?.close();if(server.listening)await new Promise<void>(resolve=>server.close(()=>resolve()));}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
