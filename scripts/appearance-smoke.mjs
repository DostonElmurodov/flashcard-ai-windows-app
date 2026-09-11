import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const data=resolve('test-results/appearance-'+Date.now());let app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
try{
 let page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();await page.getByRole('button',{name:'Settings',exact:true}).click();
 const appearance=()=>page.getByRole('group',{name:'Appearance',exact:true});
 assert.equal(await appearance().getByRole('button').count(),3);
 await appearance().getByRole('button',{name:'Dark',exact:true}).click();await page.getByRole('button',{name:'Save preferences'}).click();
 for(const [name,color] of [['Teal','#72b8ad'],['Blue','#9aade6'],['Rose','#d6a0b8']]){
  await page.getByRole('button',{name:name+' dark accent',exact:true}).click();await page.getByRole('button',{name:'Save preferences'}).click();
  await page.waitForFunction(color=>getComputedStyle(document.documentElement).getPropertyValue('--action').trim()===color,color);
 }
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(940,760));
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth));
 await page.locator('.settings-stack>.panel').first().screenshot({path:'test-results/appearance-cards.png'});
 await app.close();app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});page=await app.firstWindow();await page.locator('.page-heading').waitFor();
 const saved=await page.evaluate(()=>window.owl.snapshot());assert.equal(saved.settings.darkAccent,'rose');assert.equal(saved.settings.theme,'dark');
 await page.getByRole('button',{name:'Settings',exact:true}).click();await appearance().getByRole('button',{name:'Light',exact:true}).click();await page.getByRole('button',{name:'Save preferences'}).click();
 assert.equal(await page.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--action').trim()),'#3158ed');
 await appearance().getByRole('button',{name:'System',exact:true}).click();await page.getByRole('button',{name:'Save preferences'}).click();await page.emulateMedia({colorScheme:'dark'});await page.waitForFunction(()=>document.documentElement.dataset.theme==='dark');await page.emulateMedia({colorScheme:'light'});await page.waitForFunction(()=>document.documentElement.dataset.theme==='light');
 console.log('PASS: three appearance cards, all dark accents, independent light color, restart persistence, system theme, small layout.');
}finally{await app.close();}
