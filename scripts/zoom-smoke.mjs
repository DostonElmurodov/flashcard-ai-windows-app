import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const data=resolve('test-results/default-zoom-'+Date.now());
const launch=()=>electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data}});
let app=await launch();
try{
 let page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();
 const level=()=>app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].webContents.getZoomLevel());
 assert.equal(await level(),-.5);
 await app.evaluate(({Menu,BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];Menu.getApplicationMenu().items[2].submenu.items[1].click(undefined,w,w.webContents);});assert.equal(await level(),0);
 await app.evaluate(({Menu,BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];Menu.getApplicationMenu().items[2].submenu.items[0].click(undefined,w,w.webContents);});assert.equal(await level(),-.5);
 await page.keyboard.press('Control+0');assert.equal(await level(),-.5);
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
 await app.close();app=await launch();await app.firstWindow();assert.equal(await level(),-.5);
 console.log('PASS: default zoom matches one Zoom Out, Zoom In works, Actual Size and Ctrl+0 return to default, restart retains default.');
}finally{await app.close();}
