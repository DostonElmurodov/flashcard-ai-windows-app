import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:resolve('test-results/titlebar-menu-'+Date.now())}});
try{
 const page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();
 assert.equal(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].isMenuBarVisible()),false);
 for(const [index,name] of ['Owl AI','Edit','View'].entries()){
  await app.evaluate(({Menu},index)=>{globalThis.menuShown=false;Menu.getApplicationMenu().items[index].submenu.once('menu-will-show',()=>{globalThis.menuShown=true;});},index);
  await page.getByRole('navigation',{name:'Application menu'}).getByRole('button',{name,exact:true}).click();
  assert.equal(await app.evaluate(()=>globalThis.menuShown),true);
  await app.evaluate(({Menu},index)=>Menu.getApplicationMenu().items[index].submenu.closePopup(),index);
 }
 for(const theme of ['dark','light']){
  await page.evaluate(theme=>window.owl.saveSettings({theme}),theme);await page.reload();await page.locator('.page-heading').waitFor();
  for(const width of [1380,940]){
   await app.evaluate(({BrowserWindow},w)=>BrowserWindow.getAllWindows()[0].setSize(w,850),width);
   assert.ok(await page.evaluate(()=>{const bar=document.querySelector('.window-titlebar').getBoundingClientRect(),menu=document.querySelector('.window-menus').getBoundingClientRect();return bar.top===0&&Math.abs(bar.height-36)<1&&menu.right<innerWidth-140&&document.documentElement.scrollWidth<=innerWidth;}));
  }
  await page.screenshot({path:`test-results/titlebar-${theme}.png`});
 }
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].maximize());assert.ok(await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].isMaximized()));
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].unmaximize());
 console.log('PASS: all three native menus open from the title bar, original menu row hidden, light/dark layouts at 1380/940 and maximize/restore.');
}finally{await app.close();}
