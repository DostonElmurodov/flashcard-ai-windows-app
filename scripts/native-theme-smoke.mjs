import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const data=resolve('test-results/native-theme-'+Date.now());
const launch=()=>electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
let app=await launch();
try{
 let page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();
 await page.getByRole('button',{name:'Settings',exact:true}).click();
 for(const theme of ['Dark','Light','System','Dark']){
  await page.getByRole('group',{name:'Appearance',exact:true}).getByRole('button',{name:theme,exact:true}).click();
  await page.getByRole('button',{name:'Save preferences'}).click();
  await page.waitForFunction(theme=>document.documentElement.dataset.theme===(theme==='system'?(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'):theme),theme.toLowerCase());
  const native=await app.evaluate(({nativeTheme,BrowserWindow})=>({source:nativeTheme.themeSource,dark:nativeTheme.shouldUseDarkColors,bg:BrowserWindow.getAllWindows()[0].getBackgroundColor()}));
  assert.equal(native.source,theme.toLowerCase());assert.equal(native.bg.toLowerCase(),native.dark?'#17191d':'#eef2f9');
 }
 await app.close();app=await launch();page=await app.firstWindow();await page.locator('.page-heading').waitFor();
 assert.equal(await app.evaluate(({nativeTheme})=>nativeTheme.themeSource),'dark');
 console.log('PASS: native theme and window background follow Light/Dark/System and saved dark theme on restart.');
}finally{await app.close();}
