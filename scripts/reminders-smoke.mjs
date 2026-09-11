import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {resolve} from 'node:path';
const app=await electron.launch({args:['.'],env:{...process.env,OWL_TEST_DATA_DIR:resolve('test-results/reminders-'+Date.now())}});
try{
 const page=await app.firstWindow();await page.getByRole('button',{name:'Make room for discovery'}).click();await page.getByRole('button',{name:'Settings',exact:true}).click();
 assert.ok(await page.getByRole('checkbox',{name:'Study reminders',exact:true}).isChecked());
 assert.ok(await page.getByRole('checkbox',{name:'Open when Windows starts',exact:true}).isChecked());
 assert.ok(await page.getByRole('checkbox',{name:'Keep running in the system tray',exact:true}).isChecked());
 await page.getByRole('checkbox',{name:'Study reminders',exact:true}).uncheck();
 assert.equal(await page.getByLabel('Start reminder time hour',{exact:true}).count(),0);
 await page.getByRole('checkbox',{name:'Study reminders',exact:true}).check();
 assert.equal(await page.getByLabel('Reminders per day',{exact:true}).inputValue(),'10');
 await page.getByLabel('Reminders per day',{exact:true}).fill('6');
 const format=await page.evaluate(()=>window.owl.systemTimeFormat());
 if(format.hour12){await page.getByLabel('End reminder time AM or PM',{exact:true}).waitFor();assert.equal(await page.getByLabel('Start reminder time AM or PM',{exact:true}).inputValue(),'AM');assert.equal(await page.getByLabel('End reminder time AM or PM',{exact:true}).inputValue(),'PM');}
 assert.equal(await page.getByLabel('Start reminder time hour',{exact:true}).inputValue(),'8');
 assert.equal(await page.getByLabel('End reminder time hour',{exact:true}).inputValue(),format.hour12?'8':'20');
 await page.getByLabel('Start reminder time hour',{exact:true}).selectOption('9');await page.getByRole('button',{name:'Save preferences'}).click();
 const saved=await page.evaluate(()=>window.owl.snapshot());assert.equal(saved.settings.reminderStart,'09:00');assert.equal(saved.settings.reminderEnd,'20:00');assert.equal(saved.settings.reminderCount,6);
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setSize(940,760));await page.locator('.reminder-window').screenshot({path:'test-results/reminder-window.png'});
 assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=document.documentElement.clientWidth));
 await app.evaluate(({ipcMain})=>{ipcMain.removeHandler('owl:systemTimeFormat');ipcMain.handle('owl:systemTimeFormat',()=>({ok:true,value:{hour12:false}}));});
 await page.getByRole('button',{name:/^Learn/}).first().click();await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.waitForFunction(()=>!document.querySelector('[aria-label="End reminder time AM or PM"]'));
 assert.equal(await page.getByLabel('End reminder time hour',{exact:true}).inputValue(),'20');
 await app.evaluate(({ipcMain})=>{ipcMain.removeHandler('owl:systemTimeFormat');ipcMain.handle('owl:systemTimeFormat',()=>({ok:true,value:{hour12:true}}));});
 await page.getByRole('button',{name:/^Learn/}).first().click();await page.getByRole('button',{name:'Settings',exact:true}).click();
 await page.getByLabel('Start reminder time AM or PM',{exact:true}).waitFor();
 await page.getByLabel('Start reminder time hour',{exact:true}).selectOption('12');await page.getByLabel('Start reminder time AM or PM',{exact:true}).selectOption('AM');await page.getByRole('button',{name:'Save preferences'}).click();
 assert.equal((await page.evaluate(()=>window.owl.snapshot())).settings.reminderStart,'00:00');
 await page.getByLabel('Start reminder time AM or PM',{exact:true}).selectOption('PM');await page.getByRole('button',{name:'Save preferences'}).click();
 assert.equal((await page.evaluate(()=>window.owl.snapshot())).settings.reminderStart,'12:00');
 assert.ok(await app.evaluate(({BrowserWindow})=>{const win=BrowserWindow.getAllWindows()[0];win.close();return !win.isDestroyed()&&!win.isVisible();}),'Closing the window should keep the app running in the tray');
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].show());
 console.log('PASS: enabled defaults, reminder time formats, saved settings, and closing to tray.');
}finally{await app.close();}
