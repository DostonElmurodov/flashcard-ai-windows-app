import test from 'node:test';
import assert from 'node:assert/strict';
import {SyncScheduler} from '../electron/sync-scheduler';
import type {SyncStatus} from '../shared/types';
const minute=60_000;
function fixture(run?:()=>Promise<SyncStatus>){let now=0,calls=0;const scheduler=new SyncScheduler(async()=>{calls++;return run?run():{state:'synced'};},()=>now);return {scheduler,get calls(){return calls;},async advance(ms:number){now+=ms;await scheduler.tick();}};}
test('syncs immediately, every five minutes, and every thirty minutes while hidden',async()=>{
 const f=fixture();await f.advance(0);assert.equal(f.calls,1);await f.advance(5*minute-1);assert.equal(f.calls,1);await f.advance(1);assert.equal(f.calls,2);
 f.scheduler.setBackground(true);await f.advance(5*minute);assert.equal(f.calls,2);await f.advance(25*minute);assert.equal(f.calls,3);
 await f.advance(6*minute);f.scheduler.setBackground(false);await f.advance(0);assert.equal(f.calls,4);
});
test('coalesces edits for fifteen seconds and batches a review until it ends',async()=>{
 const f=fixture();await f.advance(0);f.scheduler.changed();await f.advance(10_000);f.scheduler.changed();await f.advance(14_999);assert.equal(f.calls,1);await f.advance(1);assert.equal(f.calls,2);
 f.scheduler.setReviewing(true);f.scheduler.changed();await f.advance(10*minute);assert.equal(f.calls,2);f.scheduler.setReviewing(false);await f.advance(0);assert.equal(f.calls,3);
});
test('backs off errors without new edits or visibility events bypassing retries',async()=>{
 let failing=true;const f=fixture(async()=>({state:failing?'error':'synced'}));await f.advance(0);
 for(const wait of [30_000,minute,2*minute,5*minute,15*minute,15*minute]){const before=f.calls;f.scheduler.changed();f.scheduler.setBackground(false);await f.advance(wait-1);assert.equal(f.calls,before);await f.advance(1);assert.equal(f.calls,before+1);}
 failing=false;await f.scheduler.runNow();const before=f.calls;await f.advance(5*minute);assert.equal(f.calls,before+1);
});
test('one request at a time; in-flight edits are uploaded later and stop cancels pending work',async()=>{
 let finish!:(s:SyncStatus)=>void;const f=fixture(()=>new Promise(r=>finish=r));const first=f.scheduler.tick();const manual=f.scheduler.runNow();assert.equal(f.calls,1);f.scheduler.changed();finish({state:'synced'});await Promise.all([first,manual]);
 const next=f.advance(15_000);assert.equal(f.calls,2);f.scheduler.stop();finish({state:'synced'});await next;await f.advance(60*minute);await f.scheduler.runNow();assert.equal(f.calls,2);
});
test('conflicts pause automatic requests until the user explicitly retries',async()=>{
 const f=fixture(async()=>({state:'conflict'}));await f.advance(0);f.scheduler.changed();await f.advance(60*minute);assert.equal(f.calls,1);await f.scheduler.runNow();assert.equal(f.calls,2);
});
test('thrown network failures also use backoff',async()=>{const f=fixture(async()=>{throw new Error('offline');});await f.advance(0);await f.advance(29_999);assert.equal(f.calls,1);await f.advance(1);assert.equal(f.calls,2);});
test('hiding an unfinished review permits its saved progress to sync in the background',async()=>{const f=fixture();await f.advance(0);f.scheduler.setReviewing(true);f.scheduler.changed();await f.advance(15_000);assert.equal(f.calls,1);f.scheduler.setBackground(true);await f.advance(0);assert.equal(f.calls,2);});
