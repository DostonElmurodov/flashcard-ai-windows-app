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
  await api.logout();assert.equal(api.state().testMode,true);
  const restarted=new Api(path,base);assert.equal(restarted.state().testMode,false);
  enabled=false;assert.deepEqual(await restarted.refreshEntitlement(),{profile:null,entitlement:null,testMode:false});
 }finally{await new Promise<void>(resolve=>server.close(()=>resolve()));rmSync(root,{recursive:true,force:true});}
});
