import test from 'node:test';
import assert from 'node:assert/strict';
import {SyncScheduler} from '../electron/sync-scheduler';
import type {SyncStatus} from '../shared/types';

const minute=60_000;
const hour=60*minute;
function fixture(run?:()=>Promise<SyncStatus>,hasPending=()=>false){
 let now=0,calls=0;
 const scheduler=new SyncScheduler(async()=>{calls++;return run?run():{state:'synced'};},()=>now,hasPending);
 return {scheduler,get calls(){return calls;},async advance(ms:number){now+=ms;await scheduler.tick();}};
}

test('runs once at startup, then every six hours while active and clean',async()=>{
 const f=fixture();await f.advance(0);await f.advance(0);assert.equal(f.calls,1);
 await f.advance(6*hour-1);assert.equal(f.calls,1);
 await f.advance(1);assert.equal(f.calls,2);
 await f.advance(6*hour);assert.equal(f.calls,3);
});

test('first unsynced edit starts a twenty-minute interval that later edits cannot postpone',async()=>{
 const f=fixture();await f.advance(0);
 await f.advance(minute);f.scheduler.changed();
 await f.advance(19*minute);f.scheduler.changed();assert.equal(f.calls,1);
 await f.advance(minute-1);assert.equal(f.calls,1);
 await f.advance(1);assert.equal(f.calls,2);
 await f.advance(20*minute-1);assert.equal(f.calls,2);
 await f.advance(1);assert.equal(f.calls,2);
});

test('persisted pending changes keep the twenty-minute cadence after startup',async()=>{
 let pending=true;const f=fixture(undefined,()=>pending);await f.advance(0);
 await f.advance(20*minute-1);assert.equal(f.calls,1);
 pending=false;await f.advance(1);assert.equal(f.calls,2);
 await f.advance(6*hour-1);assert.equal(f.calls,2);
 await f.advance(1);assert.equal(f.calls,3);
});

test('inactive checks stay anchored to the last attempt even when an edit arrives later',async()=>{
 const f=fixture();await f.advance(0);
 await f.advance(5*hour);f.scheduler.changed();f.scheduler.setActive(false);
 await f.advance(7*hour-1);assert.equal(f.calls,1);
 await f.advance(1);assert.equal(f.calls,2);
 await f.advance(12*hour);assert.equal(f.calls,3);
});

test('focus changes do not trigger checks or postpone the existing dirty deadline',async()=>{
 const f=fixture();await f.advance(0);f.scheduler.changed();
 for(let i=0;i<4;i++){await f.advance(4*minute);f.scheduler.setActive(false);f.scheduler.setActive(true);assert.equal(f.calls,1);}
 await f.advance(4*minute-1);assert.equal(f.calls,1);
 await f.advance(1);assert.equal(f.calls,2);
});

test('switching to inactive delays a dirty check until its twelve-hour interval',async()=>{
 const f=fixture();await f.advance(0);f.scheduler.changed();
 await f.advance(19*minute);f.scheduler.setActive(false);
 await f.advance(20*minute);assert.equal(f.calls,1);
 await f.advance(12*hour-20*minute);assert.equal(f.calls,2);
});

test('manual sync stays available and starts a fresh cadence',async()=>{
 const f=fixture();await f.advance(0);f.scheduler.changed();
 await f.advance(10*minute);await f.scheduler.runNow();assert.equal(f.calls,2);
 await f.advance(20*minute-1);assert.equal(f.calls,2);
 await f.advance(1);assert.equal(f.calls,2);
 await f.advance(6*hour-20*minute);assert.equal(f.calls,3);
});

test('failed dirty attempts retain changes and retry only on the twenty-minute cadence',async()=>{
 let failing=true;const f=fixture(async()=>({state:failing?'error':'synced'}));
 await f.advance(0);f.scheduler.changed();
 await f.advance(10*minute);f.scheduler.setActive(false);f.scheduler.setActive(true);assert.equal(f.calls,1);
 await f.advance(10*minute);assert.equal(f.calls,2);
 f.scheduler.changed();await f.advance(20*minute-1);assert.equal(f.calls,2);
 await f.advance(1);assert.equal(f.calls,3);
 failing=false;await f.advance(20*minute);assert.equal(f.calls,4);
 await f.advance(20*minute);assert.equal(f.calls,4);
});

test('failed clean attempts retry on the six-hour cadence, including thrown failures',async()=>{
 const f=fixture(async()=>{throw new Error('offline');});await f.advance(0);
 await f.advance(6*hour-1);assert.equal(f.calls,1);
 await f.advance(1);assert.equal(f.calls,2);
});

test('one request at a time and in-flight edits remain pending for the next interval',async()=>{
 let finish!:(s:SyncStatus)=>void;const f=fixture(()=>new Promise(r=>finish=r));
 const first=f.scheduler.tick();const manual=f.scheduler.runNow();assert.equal(f.calls,1);
 f.scheduler.changed();finish({state:'synced'});await Promise.all([first,manual]);
 const beforeDue=f.advance(20*minute-1);assert.equal(f.calls,1);await beforeDue;
 const next=f.advance(1);assert.equal(f.calls,2);
 f.scheduler.stop();finish({state:'synced'});await next;
 await f.advance(12*hour);await f.scheduler.runNow();assert.equal(f.calls,2);
});

test('conflicts pause automatic checks until a manual retry succeeds',async()=>{
 let conflict=true;const f=fixture(async()=>({state:conflict?'conflict':'synced'}));
 await f.advance(0);f.scheduler.changed();await f.advance(12*hour);assert.equal(f.calls,1);
 conflict=false;await f.scheduler.runNow();assert.equal(f.calls,2);
 await f.advance(6*hour);assert.equal(f.calls,3);
});
