import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { googleSignIn } from '../electron/google';

const clientId='123-test.apps.googleusercontent.com';
const token=(nonce:string)=>['test',Buffer.from(JSON.stringify({nonce})).toString('base64url'),'signature'].join('.');

test('Google uses a system-browser PKCE flow and rejects unrelated callbacks',async()=>{
 let verifier='',nonce='',callback='';
 const result=await googleSignIn({clientId},async address=>{
  const url=new URL(address);callback=url.searchParams.get('redirect_uri')!;nonce=url.searchParams.get('nonce')!;
  assert.equal(url.origin,'https://accounts.google.com');assert.equal(url.searchParams.get('scope'),'openid email profile');
  assert.equal(new URL(callback).hostname,'127.0.0.1');assert.equal(url.searchParams.get('code_challenge_method'),'S256');
  const invalid=new URL(callback);invalid.searchParams.set('code','wrong');invalid.searchParams.set('state','wrong');
  assert.equal((await fetch(invalid)).status,400);
  const valid=new URL(callback);valid.searchParams.set('code','fixture-code');valid.searchParams.set('state',url.searchParams.get('state')!);
  verifier=url.searchParams.get('code_challenge')!;assert.equal((await fetch(valid)).status,200);
 },{exchange:async(url,options)=>{
  assert.equal(String(url),'https://oauth2.googleapis.com/token');
  const body=new URLSearchParams(String(options?.body));assert.equal(body.get('code'),'fixture-code');assert.equal(body.get('redirect_uri'),callback);
  assert.equal(createHash('sha256').update(body.get('code_verifier')!).digest('base64url'),verifier);
  return new Response(JSON.stringify({id_token:token(nonce)}),{status:200});
 }});
 assert.equal(result,token(nonce));
 await assert.rejects(fetch(callback));
});

test('Google consent cancellation returns without a token',async()=>{
 const result=await googleSignIn({clientId},async address=>{
  const url=new URL(address),callback=new URL(url.searchParams.get('redirect_uri')!);
  callback.searchParams.set('state',url.searchParams.get('state')!);callback.searchParams.set('error','access_denied');await fetch(callback);
 });assert.equal(result,null);
});

test('Google can be cancelled while waiting and closes the listener',async()=>{
 const controller=new AbortController();let callback='';
 await assert.rejects(googleSignIn({clientId},async address=>{callback=new URL(address).searchParams.get('redirect_uri')!;controller.abort();},{signal:controller.signal}),{name:'AbortError'});
 await assert.rejects(fetch(callback));
});

test('Google missing configuration does not launch a browser',async()=>{
 let opened=false;await assert.rejects(googleSignIn({clientId:''},async()=>{opened=true;}),/not configured/);assert.equal(opened,false);
});

test('Google rejects a token from another authorization attempt',async()=>{
 await assert.rejects(googleSignIn({clientId},async address=>{
  const url=new URL(address),callback=new URL(url.searchParams.get('redirect_uri')!);callback.searchParams.set('state',url.searchParams.get('state')!);callback.searchParams.set('code','fixture');await fetch(callback);
 },{exchange:async()=>new Response(JSON.stringify({id_token:token('another-attempt')}))}),/verify the Google response/);
});

test('Google browser launch failure closes the callback listener',async()=>{
 let callback='';await assert.rejects(googleSignIn({clientId},async address=>{callback=new URL(address).searchParams.get('redirect_uri')!;throw new Error('browser unavailable');}),/browser unavailable/);await assert.rejects(fetch(callback));
});
