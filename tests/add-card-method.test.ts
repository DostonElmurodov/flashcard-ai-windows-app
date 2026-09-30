import test from 'node:test';
import assert from 'node:assert/strict';
import {readAddCardMethod,saveAddCardMethod} from '../src/add-card-method';

test('remembers a selected Add cards method per workspace and rejects unknown values',()=>{
 const values=new Map<string,string>();
 const storage={getItem:(key:string)=>values.get(key)??null,setItem:(key:string,value:string)=>{values.set(key,value);}};
 assert.equal(readAddCardMethod('workspace-a',storage),'manual');
 saveAddCardMethod('workspace-a','ai',storage);
 assert.equal(readAddCardMethod('workspace-a',storage),'ai');
 assert.equal(readAddCardMethod('workspace-b',storage),'manual');
 values.set('owl.addCardMethod.workspace-a','unsupported');
 assert.equal(readAddCardMethod('workspace-a',storage),'manual');
});

test('unavailable browser storage leaves Add cards usable with the default method',()=>{
 const storage={getItem:(_key:string):string|null=>{throw new Error('storage unavailable');},setItem:(_key:string,_value:string):void=>{throw new Error('storage unavailable');}};
 assert.equal(readAddCardMethod('workspace-a',storage),'manual');
 assert.doesNotThrow(()=>saveAddCardMethod('workspace-a','paste',storage));
});
