import test from 'node:test';
import assert from 'node:assert/strict';
import {usesTwelveHours} from '../electron/time-format';
test('time format respects Windows overrides before regional defaults',()=>{
 assert.equal(usesTwelveHours('en-US'),true);assert.equal(usesTwelveHours('ru-RU'),false);
 assert.equal(usesTwelveHours('en-US','HH:mm'),false);assert.equal(usesTwelveHours('ru-RU','h:mm tt'),true);
});
