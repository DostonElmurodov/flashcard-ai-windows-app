import { _electron as electron } from 'playwright';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdirSync } from 'node:fs';
import { join,resolve } from 'node:path';
import { Store } from '../electron/store';

async function main(){
 const root=resolve('test-results/add-card-method-'+Date.now()),data=join(root,'profile');mkdirSync(data,{recursive:true});
 let aiRequests=0;
 const server=createServer((request,response)=>{
  if(request.url==='/owlai/account/ai/word-detail')aiRequests++;
  response.setHeader('Content-Type','application/json');
  if(request.url==='/owlai/config/feature-flags')return response.end(JSON.stringify({test_mode:false}));
  if(request.url==='/owlai/account/desktop/email/session')return response.end(JSON.stringify({access_token:'local-fake',refresh_token:'local-fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'local-a',email:'local@example.test'}}));
  if(request.url==='/owlai/account/entitlement')return response.end(JSON.stringify({status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),is_trial:false,auto_renew:false,was_ever_paid:true}));
  response.statusCode=404;response.end('{}');
 });
 let app:Awaited<ReturnType<typeof electron.launch>>|undefined;
 try{
  await new Promise<void>(done=>server.listen(0,'127.0.0.1',done));
  const address=server.address();assert.ok(address&&typeof address!=='string');
  const store=await Store.open(join(data,'owl.sqlite'));
  try{store.saveSettings({apiBase:`http://127.0.0.1:${address.port}`,onboardingComplete:true,keepInTray:false,launchAtLogin:false,reminders:false});}finally{store.close();}
  const launch=()=>electron.launch({executablePath:process.env.OWL_TEST_EXECUTABLE,args:process.env.OWL_TEST_EXECUTABLE?[]:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
  app=await launch();let page=await app.firstWindow();
  await page.evaluate(async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);await window.owl.forWorkspace(snapshot.scopeRevision).login('local@example.test','fixture-only');});
  await page.evaluate(async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);await window.owl.forWorkspace(snapshot.scopeRevision).refreshEntitlement();});
  await page.evaluate(async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);await window.owl.forWorkspace(snapshot.scopeRevision).saveDeck({name:'Travel'});});
  await page.reload();
  const openAdd=async()=>{await page.locator('.page-heading .heading-actions').getByRole('button',{name:'Add cards',exact:true}).click();await page.getByRole('heading',{name:'Add cards',exact:true}).waitFor();};
  const selected=async()=>page.locator('.method-grid .method.selected').innerText();
  const back=async()=>{await page.locator('.sidebar nav').getByRole('button',{name:/^Learn/}).click();};
  await openAdd();assert.match(await selected(),/Write a card/);
  await page.getByRole('button',{name:'AI translation'}).click();
  await page.getByPlaceholder('Something worth remembering').fill('temporary word');
  await back();await openAdd();assert.match(await selected(),/AI translation/);
  assert.equal(await page.getByPlaceholder('Something worth remembering').inputValue(),'');
  await app.close();app=undefined;
  app=await launch();page=await app.firstWindow();
  await openAdd();assert.match(await selected(),/AI translation/);
  assert.equal(await page.getByPlaceholder('Something worth remembering').inputValue(),'');
  await page.getByRole('button',{name:'Paste text'}).click();
  await back();await openAdd();assert.match(await selected(),/Paste text/);
  assert.equal(aiRequests,0,'Selecting or restoring AI translation must not call the AI endpoint');
  console.log('PASS: Add cards remembers the last method across reopen and app restart without restoring draft text.');
 }finally{try{await app?.close();}finally{if(server.listening)await new Promise<void>(done=>server.close(()=>done()));}}
}
main().catch(error=>{console.error(error);process.exitCode=1;});
