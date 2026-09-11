import { createServer } from 'node:http';
import { _electron as electron } from 'playwright';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
const listen=server=>new Promise(done=>server.listen(0,'127.0.0.1',done));
let receivedAtSecond=0;const first=createServer((req,res)=>{res.setHeader('Content-Type','application/json');if(req.url.endsWith('logout')){setTimeout(()=>{res.writeHead(204);res.end();},400);return;}setTimeout(()=>res.end(JSON.stringify({access_token:'test-only-old-origin-token',access_token_expires_at:new Date(Date.now()+60000).toISOString(),refresh_token:'test-only-refresh',profile:{id:'local-test',email:'test@example.test'}})),250);});
const second=createServer((req,res)=>{if(req.headers.authorization)receivedAtSecond++;res.writeHead(401,{'Content-Type':'application/json'});res.end('{}');});await listen(first);await listen(second);
const data=resolve('test-results/security-profile-'+Date.now());mkdirSync(data,{recursive:true});const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
try{const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);const origins=[`http://127.0.0.1:${first.address().port}`,`http://127.0.0.1:${second.address().port}`];const state=await page.evaluate(async([a,b])=>{
 await window.owl.saveSettings({apiBase:a});await window.owl.login('test@example.test','test-password');
 const switching=window.owl.saveSettings({apiBase:b});await new Promise(r=>setTimeout(r,50));
 const overlapping=window.owl.login('test@example.test','test-password').catch(()=>null);await Promise.all([switching,overlapping]);
 try{await window.owl.refreshEntitlement();}catch{}
 return window.owl.account();
},origins);if(state.profile||receivedAtSecond)throw new Error('Credentials crossed an API origin boundary');console.log('PASS: concurrent origin change and login cannot send old credentials to a new server.');}finally{await app.close();first.close();second.close();}
