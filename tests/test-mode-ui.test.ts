import test from 'node:test';
import assert from 'node:assert/strict';
import {createElement} from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import Profile from '../src/Profile';
import Help from '../src/Help';
import {WorkspaceBridgeProvider} from '../src/WorkspaceBridge';
import type {Bridge} from '../shared/types';

test('test mode profile suppresses purchase promotion while preserving account sign-in',()=>{
 const render=(testMode:boolean)=>renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:null,entitlement:null,testMode},onChanged:async()=>{},notify:()=>{}})}));
 const enabled=render(true);assert.match(enabled,/Test mode/);assert.match(enabled,/Sign in/);assert.doesNotMatch(enabled,/Link your verified Apple purchase|Connect your subscription|One subscription/);
 const disabled=render(false);assert.match(disabled,/Link your verified Apple purchase/);assert.doesNotMatch(disabled,/Test mode/);
});

test('test mode help describes account requirements without purchase instructions',()=>{
 const html=renderToStaticMarkup(createElement(Help,{testMode:true,onCreate:()=>{},onProfile:()=>{},onSettings:()=>{}}));
 assert.match(html,/Test mode/);assert.match(html,/signed-in account/);assert.doesNotMatch(html,/confirm your purchase through Apple|Get AI translations with Premium/);
});

test('signed-in sync conflicts expose cloud recovery while normal sync hides it',()=>{
 const render=(state:'conflict'|'synced')=>renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'A',email:'a@example.test',provider:'email'},entitlement:null,testMode:false,sync:{state}},onChanged:async()=>{},notify:()=>{}})}));
 assert.match(render('conflict'),/Use cloud version/);assert.doesNotMatch(render('synced'),/Use cloud version/);
});

test('profile does not label elapsed premium expiry as active',()=>{
 const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:{id:'a',email:null},testMode:false,entitlement:{status:'premium',expires_at:new Date(Date.now()-1000).toISOString(),checked_at:new Date().toISOString(),is_trial:false,auto_renew:false,was_ever_paid:true}},onChanged:async()=>{},notify:()=>{}})}));assert.doesNotMatch(html,/Premium is active/);
});

test('signed-out desktop profile requires sign-in to create and keeps eligible saved reviews',()=>{
 const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(Profile,{account:{profile:null,entitlement:null,testMode:false},onChanged:async()=>{},notify:()=>{}})}));assert.match(html,/Sign in to create or add cards on desktop/);assert.match(html,/review eligible saved local cards/);assert.doesNotMatch(html,/create and review local cards without signing in/);
});
