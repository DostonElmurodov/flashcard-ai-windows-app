import test from 'node:test';
import assert from 'node:assert/strict';
import {reminderSlot} from '../electron/reminders';
const at=(h:number,m=0)=>new Date(2026,8,15,h,m);
test('ten daily reminders are evenly spaced inside the default window',()=>{
 const slot=(h:number,m=0)=>reminderSlot(at(h,m),'08:00','20:00');
 assert.equal(slot(7,59),null);assert.ok(slot(8));assert.equal(slot(8),slot(9,11));assert.notEqual(slot(8),slot(9,12));assert.ok(slot(19,59));assert.equal(slot(20),null);
 assert.equal(new Set(Array.from({length:720},(_,m)=>slot(8+Math.floor(m/60),m%60))).size,10);
});
test('overnight windows stay on the same schedule across midnight',()=>{
 const before=reminderSlot(at(23,45),'23:30','02:00',1);
 assert.equal(before,reminderSlot(new Date(2026,8,16,0,30),'23:30','02:00',1));
 assert.equal(reminderSlot(at(14),'23:30','02:00'),null);assert.equal(reminderSlot(at(8),'08:00','08:00'),null);
});
test('the daily count controls the number of reminder opportunities',()=>{
 for(const count of [1,3,10,24])assert.equal(new Set(Array.from({length:720},(_,m)=>reminderSlot(at(8+Math.floor(m/60),m%60),'08:00','20:00',count))).size,count);
 assert.equal(reminderSlot(at(8),'08:00','20:00',0),null);
});
