import { _electron as electron, expect } from 'playwright/test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

// Exercise the real renderer, IPC, loopback callback and account transitions.
// Only external Google/API responses are fixtures; no real account is used.
const data=resolve('test-results/google-login-ui-'+Date.now());
mkdirSync(data,{recursive:true});
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
try {
 const page=await app.firstWindow();
 await page.getByRole('button',{name:'Make room for discovery'}).click();
 await page.getByRole('button',{name:'Discover Premium'}).click();
 await app.evaluate(({shell})=>{
  const originalFetch=globalThis.fetch;
  globalThis.loginFixture={authorization:'',requests:0,release:null};
  shell.openExternal=async url=>{globalThis.loginFixture.authorization=url;};
  globalThis.fetch=async(input,options)=>{
   const url=String(input),fixture=globalThis.loginFixture;
   if(url==='https://oauth2.googleapis.com/token'){
    const nonce=new URL(fixture.authorization).searchParams.get('nonce');
    const idToken=['fixture',Buffer.from(JSON.stringify({nonce})).toString('base64url'),'fixture'].join('.');
    return new Response(JSON.stringify({id_token:idToken}));
   }
   if(url.endsWith('/owlai/account/desktop/google/session')){
    fixture.requests++;
    return new Promise(resolve=>{fixture.release=(status,body)=>resolve(new Response(JSON.stringify(body),{status}));});
   }
   if(url.endsWith('/owlai/account/desktop/email/session'))return new Response(JSON.stringify({error:'Fixture request rejected'}),{status:401});
   if(url.endsWith('/owlai/account/entitlement'))return new Response(JSON.stringify({status:'free'}));
   if(url.startsWith('https://api.mavrylo.com/'))return new Response('{}',{status:404});
   return originalFetch(input,options);
  };
 });
 const google=page.getByRole('button',{name:'Continue with Google',exact:true});
 const cancel=page.getByRole('button',{name:'Cancel Google sign-in',exact:true});
 const begin=async()=>{
  await app.evaluate(()=>{globalThis.loginFixture.authorization='';globalThis.loginFixture.release=null;});
  await google.click();
  await expect.poll(()=>app.evaluate(()=>globalThis.loginFixture.authorization)).not.toBe('');
 };
 const callback=async()=>{
  const authorization=await app.evaluate(()=>globalThis.loginFixture.authorization);
  const url=new URL(authorization),redirect=new URL(url.searchParams.get('redirect_uri'));
  redirect.searchParams.set('state',url.searchParams.get('state'));
  redirect.searchParams.set('code','fixture-code');
  assert.match(await (await fetch(redirect)).text(),/Google response received/);
  await expect.poll(()=>app.evaluate(()=>!!globalThis.loginFixture.release)).toBe(true);
 };
 const release=async(status,body)=>app.evaluate((_,{status,body})=>globalThis.loginFixture.release(status,body),{status,body});

 await begin();await callback();
 await page.evaluate(()=>window.dispatchEvent(new Event('focus')));
 await expect(cancel).toBeVisible();
 await release(401,{error:'Google account is not linked to an Owl AI account.'});
 await expect(google).toBeEnabled();
 await expect(page.getByRole('alert')).toContainText('Google account is not linked to an Owl AI account.');
 assert.equal(existsSync(resolve(data,'account.enc')),false);
 // Refreshing the same guest workspace must not erase the failure.
 await page.evaluate(()=>window.dispatchEvent(new Event('focus')));
 await expect(page.getByRole('alert')).toContainText('Google account is not linked');

 await begin();
 await expect(page.getByRole('alert')).toHaveCount(0);
 await cancel.click();await expect(google).toBeEnabled();
 await begin();
 await page.getByRole('button',{name:'Learn',exact:true}).click();
 // Wait for the real cancellation to release the account-transition lock.
 await expect.poll(()=>page.evaluate(()=>window.owl.snapshot().then(()=>true,()=>false))).toBe(true);
 await page.getByRole('button',{name:'Discover Premium'}).click();

 await page.getByLabel('Email',{exact:true}).fill('fixture@example.test');
 await page.getByLabel('Password',{exact:true}).fill('fixture-password');
 await page.getByRole('button',{name:'Sign in',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('Fixture request rejected');

 await begin();await callback();
 await release(200,{access_token:'fixture-access',refresh_token:'fixture-refresh',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'fixture-google-user',email:'google@example.test',provider:'google'}});
 await expect(page.getByRole('heading',{name:'google@example.test',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();
 await expect(page.getByRole('alert')).toHaveCount(0);
 assert.equal(existsSync(resolve(data,'account.enc')),true);
 console.log('PASS: Google callback, visible auth failures, focus refresh, cancellation, navigation and successful retry.');
} finally {await app.close();}
