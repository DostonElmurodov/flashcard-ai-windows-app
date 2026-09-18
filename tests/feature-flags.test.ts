import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {FeatureFlags} from '../electron/feature-flags';

test('public test mode persists across restart and a successful false restores normal access',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-flags-')),path=join(root,'flags.json');
 try{
  let enabled=true;
  const request:typeof fetch=async(url,options)=>{
   assert.equal(String(url),'https://api.example.com/owlai/config/feature-flags');
   assert.equal(options?.cache,'no-store');assert.equal(options?.redirect,'error');
   assert.equal(new Headers(options?.headers).has('Authorization'),false);
   return Response.json({test_mode:enabled});
  };
  const flags=new FeatureFlags(path,()=> 'https://api.example.com',request);
  assert.equal(flags.testMode,false);await flags.refresh();assert.equal(flags.testMode,true);
  const restarted=new FeatureFlags(path,()=> 'https://api.example.com/',request);
  assert.equal(restarted.testMode,true);enabled=false;await restarted.refresh();assert.equal(restarted.testMode,false);
  assert.equal(new FeatureFlags(path,()=> 'https://api.example.com',request).testMode,false);
 }finally{rmSync(root,{recursive:true,force:true});}
});

test('offline, malformed, and unsuccessful responses never enable test mode or erase a known value',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-flags-'));
 try{
  for(const known of [false,true])for(const response of [null,{}, {test_mode:'true'}, {test_mode:1}, {test_mode:null},'not json',503]){
   const path=join(root,'flags.json');writeFileSync(path,JSON.stringify({origin:'https://api.example.com',testMode:known}));
   const flags=new FeatureFlags(path,()=> 'https://api.example.com',async()=>{
    if(response===null)throw new Error('offline');
    if(response===503)return new Response('{"test_mode":true}',{status:503});
    if(typeof response==='string')return new Response(response);
    return Response.json(response);
   });
   await flags.refresh();assert.equal(flags.testMode,known);
  }
 }finally{rmSync(root,{recursive:true,force:true});}
});

test('a different server never inherits cached test mode or a late response',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-flags-')),path=join(root,'flags.json');
 try{
  writeFileSync(path,JSON.stringify({origin:'https://first.example.com',testMode:true}));
  let base='https://first.example.com',resolve!:(response:Response)=>void;
  const flags=new FeatureFlags(path,()=>base,()=>new Promise<Response>(r=>resolve=r));
  assert.equal(flags.testMode,true);const pending=flags.refresh();base='https://second.example.com';
  assert.equal(flags.testMode,false);resolve(Response.json({test_mode:true}));await pending;assert.equal(flags.testMode,false);
  assert.equal(new FeatureFlags(path,()=>base).testMode,false);
 }finally{rmSync(root,{recursive:true,force:true});}
});

test('invalid cache and insecure nonlocal origins cannot enable test mode',async()=>{
 const root=mkdtempSync(join(tmpdir(),'owl-flags-')),path=join(root,'flags.json');
 try{
  for(const contents of ['broken',JSON.stringify({origin:'https://api.example.com',testMode:'true'})]){
   writeFileSync(path,contents);assert.equal(new FeatureFlags(path,()=> 'https://api.example.com').testMode,false);
  }
  const flags=new FeatureFlags(path,()=> 'http://api.example.com',async()=>Response.json({test_mode:true}));
  await flags.refresh();assert.equal(flags.testMode,false);
 }finally{rmSync(root,{recursive:true,force:true});}
});
