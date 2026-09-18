import { _electron as electron } from 'playwright';
import { build } from 'esbuild';
import { createServer } from 'node:http';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
const data=resolve('test-results/google-session-'+Date.now());mkdirSync(data,{recursive:true});
const modulePath=resolve(data,'api.cjs');
await build({entryPoints:['electron/api.ts'],outfile:modulePath,bundle:true,platform:'node',format:'cjs',external:['electron']});
let received=0;
const server=createServer(async(req,res)=>{
 if(req.url!=='/owlai/account/desktop/google/session'||req.method!=='POST'){res.writeHead(404);res.end();return;}
 let raw='';for await(const chunk of req)raw+=chunk;
 if(JSON.parse(raw).id_token!=='test-only-id-token'){res.writeHead(400);res.end();return;}
 received++;await new Promise(resolve=>setTimeout(resolve,80));
 res.setHeader('Content-Type','application/json');
 res.end(JSON.stringify({access_token:'test-only-access-token',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:'test-only-refresh-token',profile:{id:'google-fixture',email:'google@example.test',provider:'google'}}));
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://127.0.0.1:${server.address().port}`;
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
try{
 await app.firstWindow();
 const result=await app.evaluate(async({app},{modulePath,data,origin})=>{
  const require=process.getBuiltinModule('module').createRequire(modulePath);
  const {Api}=require(modulePath),fs=require('node:fs'),assert=require('node:assert/strict');
  const path=require('node:path').join(data,'fixture-account.enc');
  const api=new Api(path,()=>origin);
  await assert.rejects(api.login('missing@example.test','fixture-password'),/server must be updated/i);
  const state=await api.loginGoogle(async()=> 'test-only-id-token');
  assert.equal(state.profile.provider,'google');assert.equal(state.access_token,undefined);
  assert.ok(!fs.readFileSync(path).includes(Buffer.from('test-only-access-token')));
  assert.equal(new Api(path,()=>origin).state().profile.id,'google-fixture');
  assert.equal(await api.loginGoogle(async()=>null),null);assert.equal(api.state().profile.id,'google-fixture');
  api.clear();
  let release;const authorization=new Promise(resolve=>{release=resolve;});
  const stale=api.loginGoogle(()=>authorization);api.clear();release('test-only-id-token');
  await assert.rejects(stale,/account changed/);assert.equal(api.state().profile,null);
  const controller=new AbortController();
  const cancelled=api.loginGoogle(async()=> 'test-only-id-token',controller.signal);
  setTimeout(()=>controller.abort(),20);
  await assert.rejects(cancelled,{name:'AbortError'});assert.equal(api.state().profile,null);assert.equal(fs.existsSync(path),false);
  return true;
 },{modulePath,data,origin});
 if(!result||received!==2)throw new Error('Unexpected Google session exchange');
 console.log('PASS: Google session exchange, encrypted persistence, safe cancellation and account-change rejection.');
}finally{await app.close();server.closeAllConnections();server.close();}
