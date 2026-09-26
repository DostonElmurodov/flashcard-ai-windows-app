import {_electron as electron} from 'playwright';
import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
import assert from 'node:assert/strict';
const data=resolve('test-results/part-of-speech-'+Date.now());mkdirSync(data,{recursive:true});
const app=await electron.launch({executablePath:process.env.OWL_TEST_EXECUTABLE,args:process.env.OWL_TEST_EXECUTABLE?[]:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
try{
 const page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();
 await page.getByRole('button',{name:'Create a set',exact:true}).first().click();await page.getByLabel('Set name',{exact:true}).fill('Grammar');await page.getByRole('button',{name:'Save set',exact:true}).click();
 await page.evaluate(async()=>{const s=await window.owl.snapshot();await window.owl.addWords(s.decks[0].id,[{word:'bright',translation:'яркий',partOfSpeech:'adjective'},{word:'run',translation:'бежать',partOfSpeech:'verb'},{word:'legacy',translation:'наследие'}]);});
 await page.reload();await page.getByLabel('Part of speech: adjective',{exact:true}).waitFor();await page.getByLabel('Part of speech: verb',{exact:true}).waitFor();assert.equal(await page.locator('.part-of-speech').count(),2);
 await page.getByRole('button',{name:'Start today’s practice'}).click();
 const dialog=page.getByRole('dialog');const word=await dialog.locator('h1').textContent();
 if(word==='bright'||word==='run')await dialog.getByLabel('Part of speech: '+(word==='bright'?'adjective':'verb'),{exact:true}).waitFor();else assert.equal(await dialog.locator('.part-of-speech').count(),0);
 const s=await page.evaluate(()=>window.owl.snapshot());assert.equal(s.words.find(w=>w.word==='bright').partOfSpeech,'adjective');
 console.log('PASS: native IPC saves part of speech; labels survive reload and render in Learn and Review; legacy cards have no invented label.');
}finally{await app.close();}
