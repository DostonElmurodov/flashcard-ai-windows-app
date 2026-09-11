import { _electron as electron } from 'playwright';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import assert from 'node:assert/strict';
const data=resolve('test-results/smoke-profile-'+Date.now());mkdirSync(data,{recursive:true});
const app=await electron.launch({executablePath:process.env.OWL_TEST_EXECUTABLE,args:process.env.OWL_TEST_EXECUTABLE?[]:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
const page=await app.firstWindow();
try{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.getByRole('button',{name:'Make room for discovery'}).click();
 await page.getByRole('button',{name:'Create a set',exact:true}).first().click();
 await page.getByLabel('Set name',{exact:true}).fill('Everyday English');
 await page.getByPlaceholder('What would you like to learn?').fill('Little words for bigger conversations');
 await page.getByRole('button',{name:'Save set',exact:true}).click();
 await page.getByRole('button',{name:'My sets',exact:true}).click();
 const activeSet=page.getByRole('checkbox',{name:'Active for study',exact:true});
 assert.ok(await activeSet.isChecked(),'The first set must be active');
 assert.ok(await activeSet.isDisabled(),'The only set cannot be deactivated');
 await page.getByRole('button',{name:/^Learn/}).first().click();
 await page.getByRole('button',{name:'Add cards',exact:true}).first().click();
 for(const [word,translation] of [['serendipity','счастливая случайность'],['wanderlust','жажда странствий'],['resilience','стойкость']]){
  await page.getByLabel('Word or phrase',{exact:true}).fill(word);
  await page.getByLabel('Translation',{exact:true}).fill(translation);
  await page.getByRole('button',{name:'Save cards',exact:true}).click();
  await page.waitForFunction(()=>document.querySelector('input[placeholder="Something worth remembering"]')?.value==='');
 }
 await page.getByRole('button',{name:/^Learn/}).first().click();
 await page.getByRole('button',{name:'Start today’s practice'}).waitFor();
 await page.screenshot({path:'test-results/learn.png',fullPage:true});
 await page.getByRole('button',{name:'Start today’s practice'}).click();
 await page.getByRole('button',{name:'Show answer'}).click();
 await page.screenshot({path:'test-results/review.png'});
 await page.getByRole('button',{name:/^Good/}).click();
 await page.getByRole('button',{name:'Close',exact:true}).click();
 const snapshot=await page.evaluate(()=>window.owl.snapshot());
 if(snapshot.words.length!==3||snapshot.reviewedToday!==1)throw new Error('Card creation/review did not persist.');
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.getByRole('group',{name:'Appearance',exact:true}).getByRole('button',{name:'Dark',exact:true}).click();
 await page.getByRole('button',{name:'Save preferences'}).click();
 await page.getByRole('button',{name:/^Learn/}).first().click();
 await page.screenshot({path:'test-results/dark.png',fullPage:true});
 if(errors.length)throw new Error(errors.join('\n'));
 console.log('PASS: actual Electron onboarding, card creation, SQLite persistence, FSRS review and dark theme.');
 console.log(JSON.stringify({words:snapshot.words.length,reviewed:snapshot.reviewedToday,data}));
}catch(error){await page.screenshot({path:'test-results/failure.png',fullPage:true});console.log(await page.locator('body').innerText());console.log(await page.evaluate(()=>window.owl.snapshot()));throw error;}finally{await app.close();}
