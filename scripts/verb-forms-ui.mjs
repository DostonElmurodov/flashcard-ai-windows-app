import assert from 'node:assert/strict';
import {existsSync,mkdirSync} from 'node:fs';
import {chromium} from 'playwright';
import {createServer} from 'vite';

const server=await createServer({server:{host:'127.0.0.1',port:0},logLevel:'error'});await server.listen();
let browser;
try {
 browser=await chromium.launch({headless:true,executablePath:process.env.OWL_TEST_BROWSER??(existsSync('/Applications/Google Chrome.app/Contents/MacOS/Google Chrome')?'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome':undefined)});
 const page=await browser.newPage({viewport:{width:1120,height:900}});page.setDefaultTimeout(6000);
 await page.goto(server.resolvedUrls.local[0]+'tests/fixtures/secondary-review.html');
 await page.evaluate(()=>window.secondaryFixture.updateWord('journey','Do'));
 await page.getByRole('button',{name:'Open review',exact:true}).click();
 const review=page.getByRole('dialog'),forms=review.locator('.review-verb-forms');
 await review.getByRole('heading',{name:'Do',exact:true}).waitFor();
 await forms.waitFor();assert.equal(await forms.textContent(),'did · done');
 assert.equal(await review.getByRole('button',{name:'Show answer'}).count(),1);
 assert.ok(await forms.evaluate(el=>parseFloat(getComputedStyle(el).fontSize)<parseFloat(getComputedStyle(el.previousElementSibling).fontSize)));
 mkdirSync('test-results/verb-forms',{recursive:true});
 await page.screenshot({path:'test-results/verb-forms/before-reveal.png',fullPage:true});
 await review.getByRole('button',{name:'Spelling practice',exact:true}).click();
 assert.equal(await forms.count(),0,'Spelling prompt must conceal verb forms');
 await review.getByRole('button',{name:'Show answer'}).click();
 await forms.waitFor();assert.equal(await forms.textContent(),'did · done');
 for(const name of ['Again','Hard','Good','Easy'])assert.equal(await review.getByRole('button',{name:new RegExp('^'+name+'\\b')}).isEnabled(),true);
 console.log('PASS: Do shows smaller did · done before reveal; spelling conceals forms until reveal; grading remains available.');
} finally {await browser?.close();await server.close();}
