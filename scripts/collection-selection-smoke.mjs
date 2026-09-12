import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const data=resolve('test-results/collection-selection-'+Date.now());
const launch=()=>electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
let app=await launch();
try{
 let page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();
 const ids=await page.evaluate(async()=>{const a=await window.owl.saveDeck({name:'First'}),b=await window.owl.saveDeck({name:'Second'});return [a.id,b.id];});await page.reload();
 await page.getByLabel('Filter set').selectOption(ids[0]);await page.waitForFunction(id=>localStorage.getItem('owl.selectedCollection')===id,ids[0]);
 await app.close();app=await launch();page=await app.firstWindow();await page.getByLabel('Filter set').waitFor();assert.equal(await page.getByLabel('Filter set').inputValue(),ids[0]);
 await page.getByLabel('Filter set').selectOption('');await page.waitForFunction(()=>localStorage.getItem('owl.selectedCollection')==='');await page.reload();assert.equal(await page.getByLabel('Filter set').inputValue(),'');
 await page.getByLabel('Filter set').selectOption(ids[1]);await page.evaluate(async id=>window.owl.deleteDeck(id),ids[1]);await page.reload();assert.equal(await page.getByLabel('Filter set').inputValue(),ids[0]);
 await page.locator('.collection-mini').click();assert.equal(await page.getByLabel('Filter set').inputValue(),ids[0]);
 await app.close();app=await launch();page=await app.firstWindow();await page.getByLabel('Filter set').waitFor();assert.equal(await page.getByLabel('Filter set').inputValue(),ids[0]);assert.equal((await page.evaluate(()=>window.owl.snapshot())).decks[0].active,true);
 await page.evaluate(async id=>window.owl.deleteDeck(id),ids[0]);await page.reload();await page.getByLabel('Filter set').waitFor();assert.equal(await page.getByLabel('Filter set').inputValue(),'');
 console.log('PASS: selection and All sets persist, sole collection stays selected and active after restart, deleted selections recover.');
}finally{await app.close();}
