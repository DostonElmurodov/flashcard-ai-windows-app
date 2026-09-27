import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {Api} from '../electron/api';

test('signed-out API refresh exposes test mode without inventing a subscription or bypassing authentication',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-api-flags-'));let enabled=true;
 const server=createServer((request,response)=>{
  assert.equal(request.url,'/owlai/config/feature-flags');assert.equal(request.headers.authorization,undefined);
  response.setHeader('Content-Type','application/json');response.setHeader('Cache-Control','no-store');response.end(JSON.stringify({test_mode:enabled}));
 });
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  const address=server.address();assert.ok(address&&typeof address!=='string');
  const base=()=>`http://127.0.0.1:${address.port}`,path=join(root,'account.enc'),api=new Api(path,base);
  assert.deepEqual(api.state(),{profile:null,entitlement:null,testMode:false});
  assert.deepEqual(await api.refreshEntitlement(),{profile:null,entitlement:null,testMode:true});
  await assert.rejects(api.translate('hello','en-us','es'),/Sign in/);
  await assert.rejects(api.reviewTranslation({word:'hello',native_language:'en-us',learning_language:'es',secondary_language:'fr'}),/Sign in/);
  await api.logout();assert.equal(api.state().testMode,true);
  const restarted=new Api(path,base);assert.equal(restarted.state().testMode,false);
  enabled=false;assert.deepEqual(await restarted.refreshEntitlement(),{profile:null,entitlement:null,testMode:false});
 }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
});

test('HTTP refusals retain status and standard retry timing without replaying AI calls',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-api-errors-'));let status=429,code='quota',calls=0,retry='120';
 const server=createServer((request,response)=>{calls++;response.statusCode=status;response.setHeader('Content-Type','application/json');if(retry)response.setHeader('Retry-After',retry);response.end(JSON.stringify({code}));});
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{
  const address=server.address();assert.ok(address&&typeof address!=='string');const api=new Api(join(root,'account.enc'),()=>`http://127.0.0.1:${address.port}`);
  (api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:'fake',profile:{id:'a',email:null}};
  for(const value of [429,402,503]){status=value;code=value===503?'subscription_reconciliation_required':'quota';const before=calls;
   await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),(error:any)=>{assert.equal(error.status,value);if(value===429){assert.equal(error.retryAfterSeconds,120);assert.match(error.message,/120/);}if(value===503){assert.match(error.message,/support|recover/i);assert.equal(api.state().entitlement?.status,'invalid_subscription');}return true;});assert.equal(calls,before+1);
  }
  status=503;code='apple_temporarily_unavailable';retry='';await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}),/temporarily unavailable/i);
  status=401;const before=calls;await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}));assert.equal(calls,before+1,'AI denial is never automatically replayed');
 }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
});

import {retryAfterSeconds} from '../electron/api';
test('standard Retry-After dates parse without guessing missing or malformed timing',()=>{
 const now=Date.parse('2026-09-27T12:00:00Z');assert.equal(retryAfterSeconds('Sun, 27 Sep 2026 12:02:00 GMT',now),120);assert.equal(retryAfterSeconds(null,now),undefined);assert.equal(retryAfterSeconds('bad',now),undefined);
});
test('selected origin invalidation rejects late entitlement replies even on a round trip',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-generation-'));let release!:()=>void;
 const server=createServer(async(request,response)=>{response.setHeader('Content-Type','application/json');if(request.url==='/owlai/config/feature-flags'){response.end('{"test_mode":false}');return;}await new Promise<void>(resolve=>release=resolve);response.end(JSON.stringify({status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),was_ever_paid:true}));});
 await new Promise<void>(resolve=>server.listen(0,'127.0.0.1',resolve));
 try{const address=server.address();assert.ok(address&&typeof address!=='string');const first=`http://127.0.0.1:${address.port}`;let base=first;const api=new Api(join(root,'account.enc'),()=>base);(api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'a',email:null}};
 const pending=api.refreshEntitlement();while(!release)await new Promise(resolve=>setTimeout(resolve,1));const rejected=assert.rejects(pending);base='https://second.example.com';api.state();base=first;api.state();release();await rejected;assert.equal(api.state().entitlement,null);assert.equal(api.state().testMode,false);
 }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
});

test('a delayed error body cannot revoke the new account entitlement',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-body-')),original=globalThis.fetch;let finish!:(text:string)=>void;
 try{globalThis.fetch=async()=>new Response(new ReadableStream({start(controller){finish=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}),{status:503});
 const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');const session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'old',email:null}};(api as any).session=session;
 const pending=api.request('/owlai/account/ai/word-detail','POST',{}),rejected=assert.rejects(pending);await new Promise(resolve=>setTimeout(resolve,0));api.clear();(api as any).session={...session,profile:{id:'new',email:null}};
 const entitlement={status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false};(api as any).entitlement=entitlement;
 finish(JSON.stringify({code:'subscription_reconciliation_required'}));await rejected;assert.deepEqual(api.state().entitlement,entitlement);
 }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
});
