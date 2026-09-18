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
