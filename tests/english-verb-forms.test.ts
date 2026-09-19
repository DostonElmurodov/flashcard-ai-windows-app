import test from 'node:test';
import assert from 'node:assert/strict';
import {englishReviewVerbForms} from '../shared/english-verb-forms';

test('English Do gives past and participle independent of capitalization or region',()=>{
 for(const language of ['en','en-us','en-GB','en_US'])assert.deepEqual(englishReviewVerbForms(' Do ',language),{v2:'did',v3:'done'});
});
test('identical forms are retained and be includes was and were',()=>{
 assert.deepEqual(englishReviewVerbForms('put','en'),{v2:'put',v3:'put'});
 assert.deepEqual(englishReviewVerbForms('Be','en'),{v2:'was / were',v3:'been'});
});
test('other languages and unknown words do not get guessed forms',()=>{
 assert.equal(englishReviewVerbForms('do','es'),null);
 assert.equal(englishReviewVerbForms('car','en'),null);
 assert.equal(englishReviewVerbForms('','en'),null);
});
