import test from 'node:test';import assert from 'node:assert/strict';import {createElement} from 'react';import {renderToStaticMarkup} from 'react-dom/server';import CreateSet from '../src/CreateSet';import {WorkspaceBridgeProvider} from '../src/WorkspaceBridge';import type {Bridge,Snapshot} from '../shared/types';
test('expired paid create form explains retained cards and disables new content',()=>{
 const snapshot={scopeRevision:'a',words:[],settings:{nativeLanguage:'ru',learningLanguage:'en-us',secondaryReviewLanguage:null},account:{profile:{id:'a',email:null},entitlement:{status:'expired_paid',was_ever_paid:true,is_trial:false,auto_renew:false},testMode:false}} as unknown as Snapshot;
 const html=renderToStaticMarkup(createElement(WorkspaceBridgeProvider,{bridge:{} as Bridge,children:createElement(CreateSet,{snapshot,onSaved:()=>{},notify:()=>{}})}));assert.match(html,/Saved cards remain available/);assert.match(html,/disabled/);
});
