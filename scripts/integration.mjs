import { _electron as electron } from 'playwright';
import { mkdirSync,readFileSync } from 'node:fs';
import { resolve } from 'node:path';
const data=resolve('test-results/integration-profile-'+Date.now());mkdirSync(data,{recursive:true});
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
try{
 const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);
 const email=`desktop-${Date.now()}@example.test`,password='Desktop-Test-Only-248!';
 const result=await page.evaluate(async({email,password})=>{
  await window.owl.saveSettings({apiBase:'http://127.0.0.1:5289',onboardingComplete:true});
  const registered=await window.owl.login(email,password,password);if(registered.profile?.email!==email)throw new Error('Registration profile mismatch');
  const ent=await window.owl.refreshEntitlement();if(ent.entitlement?.status!=='free')throw new Error('New account must not receive Premium');
  let rejected=false;try{await window.owl.translate('hello','ru','en-us');}catch(error){rejected=error.message.includes('Premium');}if(!rejected)throw new Error('Free account AI was not rejected');
  const deck=await window.owl.saveDeck({name:'Desktop integration set',nativeLanguage:'ru',learningLanguage:'en-us'});
  await window.owl.addWords(deck.id,[{word:'hello',translation:'привет',examples:['Hello, friend.']}]);
  const publication=await window.owl.publish(deck.id);if(!publication.status)throw new Error('Publication failed');
  const catalog=await window.owl.catalog('Desktop integration');if(!Array.isArray(catalog))throw new Error('Invalid catalog');
  await window.owl.unpublish(deck.id);await window.owl.logout();
  if((await window.owl.account()).profile)throw new Error('Logout did not clear account');
  await window.owl.login(email,password);const state=await window.owl.account();if('access_token'in state||'refresh_token'in state)throw new Error('Credential leaked into renderer');
  await window.owl.deleteAccount();if((await window.owl.snapshot()).words.length!==1)throw new Error('Account deletion removed local cards');
  return {registration:true,freeAccountAiRejected:true,publicationWithExamples:publication.status,catalog:true,logout:true,login:true,deletePreservesCards:true};
 },{email,password});
 console.log('PASS: real Electron → local .NET → PostgreSQL integration.');console.log(JSON.stringify(result));
}finally{await app.close();}
