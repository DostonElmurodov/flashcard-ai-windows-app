import {_electron as electron} from 'playwright';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {mkdirSync} from 'node:fs';
import {resolve} from 'node:path';

// A local protocol fixture, never a production endpoint or account-creation bypass.
const createdAt='2026-01-01T00:00:00.000Z';
const card={due:createdAt,stability:0,difficulty:0,elapsed_days:0,scheduled_days:0,reps:0,lapses:0,state:0,last_review:null,learning_steps:0};
const initial=owner=>[
 {kind:'deck',id:'shared-deck-id',version:1,deleted:false,data:{name:`${owner} private collection`,description:'',active:true,native_language:'ru',learning_language:'en-us',created_at:createdAt}},
 {kind:'word',id:'shared-word-id',version:1,deleted:false,data:{deck_id:'shared-deck-id',word:`${owner} private word`,translation:owner==='A'?'перевод A':'',pronunciation:null,examples:[],notes:null,created_at:createdAt,card:{...card},reverse:{...card}}}
];
const clouds=new Map(['A','B'].map(owner=>[owner,new Map(initial(owner).map(r=>[`${r.kind}:${r.id}`,r]))]));
const requests=[],uploads=[];
let heldA=null,delayNextA=false,wrongOwnerNextB=false,signalHeld;
const heldSignal=()=>new Promise(resolve=>{signalHeld=resolve;});
const server=createServer(async(req,res)=>{
 try{
  requests.push({method:req.method,path:req.url});
  let raw='';for await(const chunk of req)raw+=chunk;const body=raw?JSON.parse(raw):null;
  const json=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(value));};
  if(req.url==='/owlai/account/desktop/email/session'&&req.method==='POST'){
   const owner=body?.email==='a@example.test'?'A':body?.email==='b@example.test'?'B':null;
   if(!owner||body.password!=='Fixture-Only-248!'){json(403,{code:'ios_account_required',error:'Create or open your account in Owl AI on iPhone first.'});return;}
   json(200,{access_token:`fixture-${owner}`,access_token_expires_at:new Date(Date.now()+3600000).toISOString(),refresh_token:`refresh-${owner}`,profile:{id:`owner-${owner}`,email:body.email,provider:'email'}});return;
  }
  if(req.url==='/owlai/account/session/logout'&&req.method==='POST'){res.writeHead(204);res.end();return;}
  const owner=req.headers.authorization==='Bearer fixture-A'?'A':req.headers.authorization==='Bearer fixture-B'?'B':null;
  if(!owner){json(401,{error:'Unauthorized fixture request.'});return;}
  if(req.url==='/owlai/account/entitlement'&&req.method==='GET'){json(200,{status:'free',is_trial:false,auto_renew:false,was_ever_paid:false});return;}
  if(req.url==='/owlai/account/sync'&&['GET','POST'].includes(req.method)){
   const records=clouds.get(owner);
   let conflicts=[];
   if(req.method==='POST'){
    assert.ok(Array.isArray(body?.changes));
    uploads.push({owner,changes:structuredClone(body.changes)});
    conflicts=body.changes.flatMap(change=>{const row=records.get(`${change.kind}:${change.id}`);return (row?.version??0)!==change.base_version?[row??{kind:change.kind,id:change.id,version:0,deleted:true,data:null}]:[];});
    if(!conflicts.length)for(const change of body.changes){const key=`${change.kind}:${change.id}`;records.set(key,{kind:change.kind,id:change.id,version:(records.get(key)?.version??0)+1,deleted:change.deleted,data:change.data});}
   }
   const reply={owner_id:`owner-${owner}`,records:structuredClone([...records.values()]),conflicts};
   if(owner==='A'&&delayNextA){delayNextA=false;heldA=()=>json(200,reply);signalHeld?.();return;}
   if(owner==='B'&&wrongOwnerNextB){wrongOwnerNextB=false;json(200,{owner_id:'owner-A',records:initial('A'),conflicts:[]});return;}
   json(200,reply);return;
  }
  json(404,{error:'No such fixture route.'});
 }catch(error){res.writeHead(500,{'Content-Type':'application/json'});res.end(JSON.stringify({error:'Fixture handler failed.'}));console.error(error);}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const origin=`http://127.0.0.1:${server.address().port}`;
const data=resolve('test-results/account-sync-'+Date.now());mkdirSync(data,{recursive:true});
let app;
try{
 app=await electron.launch({executablePath:process.env.OWL_TEST_EXECUTABLE,args:process.env.OWL_TEST_EXECUTABLE?[]:['.'],env:{...process.env,OWL_TEST_DATA_DIR:data},timeout:30000});
 const page=await app.firstWindow();await page.waitForFunction(()=>!!window.owl);
 const activate=()=>page.evaluate(async()=>{const snapshot=await window.owl.snapshot();window.owl.activateWorkspace(snapshot.scopeRevision);return snapshot;});
 const login=async owner=>{await activate();await page.evaluate(owner=>window.owl.login(`${owner.toLowerCase()}@example.test`,'Fixture-Only-248!'),owner);return activate();};
 const logout=async()=>{await activate();await page.evaluate(()=>window.owl.logout());return activate();};
 const sync=async()=>{await activate();return page.evaluate(()=>window.owl.sync());};
 const expectOwner=async(owner,scope)=>{
  const snap=await activate();assert.equal(snap.account.profile.id,`owner-${owner}`);if(scope)assert.equal(snap.workspaceId,scope);
  assert.equal(snap.words.find(w=>w.id==='shared-word-id')?.word,`${owner} private word`);
  assert.equal(snap.decks.find(d=>d.id==='shared-deck-id')?.name,`${owner} private collection`);return snap;
 };
 await activate();await page.evaluate(origin=>window.owl.saveSettings({apiBase:origin,onboardingComplete:true}),origin);
 const guestDeck=await page.evaluate(async()=>{const deck=await window.owl.saveDeck({name:'Guest only',nativeLanguage:'ru',learningLanguage:'en-us'});await window.owl.addWords(deck.id,[{word:'guest secret',translation:'guest translation'}]);return deck.id;});
 const guestScope=(await activate()).workspaceId;
 await assert.rejects(page.evaluate(()=>window.owl.login('missing@example.test','Fixture-Only-248!')),/iPhone first/);
 let snapshot=await activate();assert.equal(snapshot.account.profile,null);assert.equal(snapshot.workspaceId,guestScope);assert.ok(snapshot.decks.some(d=>d.id===guestDeck));
 await login('A');assert.equal((await sync()).state,'synced');snapshot=await expectOwner('A');const scopeA=snapshot.workspaceId;
 await page.evaluate(async()=>{const s=await window.owl.snapshot();window.staleAccountBridge=window.owl.forWorkspace(s.scopeRevision);});
 assert.notEqual(scopeA,guestScope);assert.ok(!snapshot.decks.some(d=>d.id===guestDeck));
 const accountDeck=await page.evaluate(async()=>{const deck=await window.owl.saveDeck({name:'A local addition',nativeLanguage:'ru',learningLanguage:'en-us'});await window.owl.addWords(deck.id,[{word:'A added word',translation:'A added translation'}]);return deck.id;});
 assert.equal((await sync()).state,'synced');assert.ok(clouds.get('A').has(`deck:${accountDeck}`));
 // Exercise the production IPC hooks and timer, not only the pure scheduler.
 const beforeAuto=uploads.length;
 await page.evaluate(()=>window.owl.saveDeck({name:'Automatic edit batch',nativeLanguage:'ru',learningLanguage:'en-us'}));
 await new Promise(resolve=>setTimeout(resolve,17_000));
 assert.equal(uploads.length,beforeAuto+1);assert.ok([...clouds.get('A').values()].some(r=>r.data?.name==='Automatic edit batch'));
 await page.evaluate(async id=>{await window.owl.reviewSession(true);const s=await window.owl.snapshot();const word=s.words.find(w=>w.deckId===id);await window.owl.review(word.id,4,'auto-review-fixture');},accountDeck);
 const beforeReview=uploads.length;await new Promise(resolve=>setTimeout(resolve,17_000));assert.equal(uploads.length,beforeReview);
 await page.evaluate(()=>window.owl.reviewSession(false));
 await new Promise(resolve=>setTimeout(resolve,1_500));assert.equal(uploads.length,beforeReview+1);
 assert.ok([...clouds.get('A').values()].some(r=>r.kind==='word'&&r.data?.deck_id===accountDeck&&r.data.card.reps===1));
 snapshot=await logout();assert.equal(snapshot.workspaceId,guestScope);assert.equal(snapshot.words.length,1);assert.equal(snapshot.words[0].word,'guest secret');assert.ok(!snapshot.decks.some(d=>d.id===accountDeck));
 await login('B');assert.equal((await sync()).state,'synced');snapshot=await expectOwner('B');const scopeB=snapshot.workspaceId;
 await assert.rejects(page.evaluate(()=>window.staleAccountBridge.editWord('shared-word-id',{word:'A stale draft',translation:'Do not copy'})),/account changed/);await expectOwner('B',scopeB);
 assert.notEqual(scopeA,scopeB);assert.ok(!snapshot.decks.some(d=>d.id===guestDeck||d.id===accountDeck));
 // Ask the renderer to reload its scope and confirm empty iOS translations have useful UI text.
 await page.evaluate(()=>window.dispatchEvent(new Event('focus')));await page.getByText('Translation needed',{exact:true}).waitFor();
 await logout();await login('A');assert.equal((await sync()).state,'synced');snapshot=await expectOwner('A',scopeA);assert.ok(snapshot.decks.some(d=>d.id===accountDeck));
 await activate();const awaitingHeld=heldSignal();delayNextA=true;
 const lateSync=page.evaluate(()=>window.owl.sync().then(value=>({value}),error=>({error:String(error)})));
 let holdTimeout;await Promise.race([awaitingHeld,new Promise((_,reject)=>{holdTimeout=setTimeout(()=>reject(new Error('Delayed A sync did not reach fixture.')),10000);})]).finally(()=>clearTimeout(holdTimeout));
 await logout();await login('B');assert.equal((await sync()).state,'synced');await expectOwner('B',scopeB);
 heldA();heldA=null;await lateSync;await expectOwner('B',scopeB);
 wrongOwnerNextB=true;const wrongOwner=await sync();assert.equal(wrongOwner.state,'error');assert.match(wrongOwner.message,/Account mismatch/);await expectOwner('B',scopeB);
 assert.equal((await sync()).state,'synced');
 assert.ok(!requests.some(r=>/register|\/account\/email\/session$/.test(r.path)),'Desktop must only use existing-account endpoints.');
 assert.ok(uploads.every(upload=>upload.changes.every(change=>change.id!==guestDeck&&change.data?.deck_id!==guestDeck)),'Guest records must never upload.');
 assert.ok(uploads.filter(upload=>upload.owner==='B').every(upload=>upload.changes.every(change=>change.id!==accountDeck&&change.data?.deck_id!==accountDeck)),'A records must never upload into B.');
 console.log('PASS: automatic edit/review batches; Electron login-only routes; guest/A/B isolation with identical entity IDs; synced relogin; aborted late sync; wrong-owner rejection; empty translation hint.');
}finally{
 if(heldA)heldA();
 if(app)await app.close();server.closeAllConnections();await new Promise(resolve=>server.close(resolve));
}
