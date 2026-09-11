import { dialog,BrowserWindow,app } from 'electron';
import { readFile,writeFile,mkdir,stat } from 'node:fs/promises';
import { extname,basename,join } from 'node:path';
import { createWorker } from 'tesseract.js';
import { parseImport } from './imports';
import type { Store } from './store';
export async function importFile(window:BrowserWindow,mode:'auto'|'pairs'|'words',learningLanguage='en-us'){
 const result=await dialog.showOpenDialog(window,{title:'Import flashcards',properties:['openFile'],filters:[{name:'Text, PDF or image',extensions:['txt','csv','tsv','pdf','png','jpg','jpeg','webp','bmp']}]});if(result.canceled)return null;
 const path=result.filePaths[0],extension=extname(path).slice(1).toLowerCase();if((await stat(path)).size>20_000_000)throw new Error('Choose a file smaller than 20 MB.');
 let text='';
 const codes:Record<string,string>={en:'eng',es:'spa',tr:'tur',ru:'rus',it:'ita',de:'deu',fr:'fra',ja:'jpn',zh:'chi_sim',yue:'chi_tra',pt:'por',hi:'hin',bn:'ben',id:'ind',ur:'urd',vi:'vie',ko:'kor',uk:'ukr',pl:'pol',tg:'tgk',uz:'uzb',az:'aze',kk:'kaz',hy:'hye',ar:'ara'};
 const cachePath=join(app.getPath('userData'),'ocr');await mkdir(cachePath,{recursive:true});
 const ocrLanguage=codes[learningLanguage.split('-')[0]]??'eng';
 let worker:Awaited<ReturnType<typeof createWorker>>|undefined;
 const recognize=async(image:string|Buffer)=>{worker??=await createWorker(ocrLanguage,1,{cachePath});return (await worker.recognize(image)).data.text;};
 try{
 if(['png','jpg','jpeg','webp','bmp'].includes(extension)){
  text=await recognize(path);
 }else if(extension==='pdf'){
  const pdfjs=await import('pdfjs-dist/legacy/build/pdf.mjs');const loading=pdfjs.getDocument({data:new Uint8Array(await readFile(path)),useSystemFonts:true});const document=await loading.promise;
  try{if(document.numPages>100)throw new Error('Import up to 100 PDF pages at a time.');for(let i=1;i<=document.numPages;i++){const page=await document.getPage(i);const content=await page.getTextContent();let pageText=content.items.map(x=>'str'in x?x.str+('hasEOL'in x&&x.hasEOL?'\n':' '):'').join('');if(!pageText.trim()){const {createCanvas}=await import('@napi-rs/canvas');const viewport=page.getViewport({scale:2});if(viewport.width*viewport.height>30_000_000)throw new Error('A PDF page is too large to recognize safely. Export a smaller image.');const canvas=createCanvas(Math.ceil(viewport.width),Math.ceil(viewport.height));await page.render({canvasContext:canvas.getContext('2d') as any,viewport,canvas:canvas as any}).promise;pageText=await recognize(canvas.toBuffer('image/png'));}text+=pageText+'\n';}}finally{await loading.destroy();}
 }else text=await readFile(path,'utf8');
 if(!text.trim())throw new Error('No text found. Try a clearer image or another recognition language in Settings.');
 return {name:basename(path),text,drafts:parseImport(text,mode,extension)};
 }finally{await worker?.terminate();}
}
export async function exportDeck(window:BrowserWindow,store:Store,id:string){const snap=store.snapshot(),deck=snap.decks.find(x=>x.id===id);if(!deck)throw new Error('Set not found.');const result=await dialog.showSaveDialog(window,{title:'Export set',defaultPath:deck.name.replace(/[<>:"/\\|?*]/g,'_')+'.csv',filters:[{name:'CSV flashcards',extensions:['csv']}]});if(result.canceled||!result.filePath)return false;const csv=(value:string)=>'"'+value.replace(/"/g,'""')+'"';await writeFile(result.filePath,'\uFEFFword,translation\r\n'+snap.words.filter(x=>x.deckId===id).map(x=>[x.word,x.translation].map(csv).join(',')).join('\r\n'),'utf8');return true;}
