# Task9 fix round2 review package
43ba166 fix: invalidate pending entitlement refreshes on direct denial
 electron/api.ts             |  7 +++++--
 tests/api-test-mode.test.ts | 19 +++++++++++++++++++
 2 files changed, 24 insertions(+), 2 deletions(-)
diff --git a/electron/api.ts b/electron/api.ts
index 7e42143..9a3d255 100644
--- a/electron/api.ts
+++ b/electron/api.ts
@@ -48,26 +48,29 @@ export class Api {
   if(new Date(this.session.access_token_expires_at).getTime()<Date.now()+30000)await this.refresh();
   if(generation!==this.generation||!this.session)throw new Error('Your account session changed.');
   let response=await this.send(path,method,body,this.session.access_token,signal);
   if(response.status===401){
    if(path.startsWith('/owlai/account/ai/'))throw new ApiError('Your session needs refreshing. Sign in or refresh your profile, then try again.',401);
    await this.refresh();if(generation!==this.generation||!this.session)throw new Error('Your session expired.');response=await this.send(path,method,body,this.session.access_token,signal);
   }
   try{
    const value=await this.result<T>(response);if(generation!==this.generation)throw new Error('Your account session changed.');return value;
   }catch(error){
    // The response body may complete after an account/origin transition.
    if(generation===this.generation&&(authorityRevision===null||authorityRevision===this.entitlementRevision)&&error instanceof ApiError){
-    if(error.code==='subscription_reconciliation_required')this.entitlement={status:'invalid_subscription',is_trial:false,auto_renew:false,was_ever_paid:false,checked_at:new Date().toISOString()};
-    else if([402,503].includes(error.status))this.entitlement=null;
+    if(error.code==='subscription_reconciliation_required'||[402,503].includes(error.status)){
+     // A direct refusal supersedes all entitlement confirmations already in flight.
+     if(authorityRevision===null)this.entitlementRevision++;
+     this.entitlement=error.code==='subscription_reconciliation_required'?{status:'invalid_subscription',is_trial:false,auto_renew:false,was_ever_paid:false,checked_at:new Date().toISOString()}:null;
+    }
    }
    throw error;
   }
  }
  async refreshEntitlement(){
   const revision=++this.entitlementRevision;await this.featureFlags.refresh();
   if(revision!==this.entitlementRevision||!this.session)return this.state();const generation=this.generation;
   try{
    const ent=await this.request<Entitlement>('/owlai/account/entitlement','GET');
    if(generation===this.generation&&revision===this.entitlementRevision)this.entitlement={...ent,checked_at:new Date().toISOString()};return this.state();
   }catch(error){
    if(generation===this.generation&&revision===this.entitlementRevision&&!(error instanceof ApiError&&error.code==='subscription_reconciliation_required'))this.entitlement=null;
diff --git a/tests/api-test-mode.test.ts b/tests/api-test-mode.test.ts
index 0b380fc..d28c920 100644
--- a/tests/api-test-mode.test.ts
+++ b/tests/api-test-mode.test.ts
@@ -72,12 +72,31 @@ for(const olderResult of ['premium','temporary','reconciliation'] as const)test(
    globalThis.fetch=async input=>String(input).includes('feature-flags')?Response.json({test_mode:false}):new Promise<Response>(resolve=>replies.push(resolve));
    const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');(api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'a',email:null}};
    const entitlement=(status:string)=>({status,expires_at:new Date(Date.now()+86400000).toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false});
    const older=api.refreshEntitlement();const handled=older.catch(()=>{});while(replies.length<1)await new Promise(resolve=>setTimeout(resolve,0));
    let finishBody!:(text:string)=>void;replies[0](new Response(new ReadableStream({start(controller){finishBody=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}),{status:olderResult==='premium'?200:503}));await new Promise(resolve=>setTimeout(resolve,0));
    const newer=api.refreshEntitlement();while(replies.length<2)await new Promise(resolve=>setTimeout(resolve,0));
    const newest=olderResult==='premium'?'revoked':'premium';replies[1](Response.json(entitlement(newest)));await newer;
    const before=api.state().entitlement;
    finishBody(JSON.stringify(olderResult==='premium'?entitlement('premium'):{code:olderResult==='reconciliation'?'subscription_reconciliation_required':'temporary'}));await handled;
    assert.deepEqual(api.state().entitlement,before,olderResult+' must not overwrite the newer accepted response');assert.equal(evaluateAccess(api.state(),'add',101).allow,newest==='premium');
  }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
 });
+
+for(const denial of ['payment','reconciliation','temporary'] as const)test('accepted direct '+denial+' denial invalidates an earlier premium body',async()=>{
+ const root=mkdtempSync(join(tmpdir(),'owl-direct-order-')),original=globalThis.fetch;let finishBody!:(text:string)=>void,refreshCount=0;
+ try{
+  const premium={status:'premium',expires_at:new Date(Date.now()+86400000).toISOString(),checked_at:new Date().toISOString(),was_ever_paid:true,is_trial:false,auto_renew:false};
+  globalThis.fetch=async input=>{
+   const path=new URL(String(input)).pathname;
+   if(path==='/owlai/config/feature-flags')return Response.json({test_mode:false});
+   if(path==='/owlai/account/entitlement'){refreshCount++;return refreshCount>1?Response.json(premium):new Response(new ReadableStream({start(controller){finishBody=text=>{controller.enqueue(new TextEncoder().encode(text));controller.close();};}}));}
+   if(path==='/owlai/account/ai/word-detail')return Response.json({code:denial==='reconciliation'?'subscription_reconciliation_required':denial==='payment'?'subscription_required':'temporary'},{status:denial==='payment'?402:503});
+   throw new Error('Unexpected fake request: '+path);
+  };
+  const api=new Api(join(root,'account.enc'),()=> 'http://127.0.0.1:9');(api as any).session={access_token:'fake',access_token_expires_at:new Date(Date.now()+3600000).toISOString(),profile:{id:'a',email:null}};(api as any).entitlement=premium;
+  const older=api.refreshEntitlement();while(!finishBody)await new Promise(resolve=>setTimeout(resolve,0));
+  await assert.rejects(api.request('/owlai/account/ai/word-detail','POST',{}));const denied=api.state().entitlement;assert.equal(evaluateAccess(api.state(),'add',101).allow,false);
+  finishBody(JSON.stringify(premium));await older;assert.deepEqual(api.state().entitlement,denied);assert.equal(evaluateAccess(api.state(),'add',101).allow,false);
+  await api.refreshEntitlement();assert.equal(api.state().entitlement?.status,'premium');assert.equal(evaluateAccess(api.state(),'add',101).allow,true,'A genuinely later refresh can confirm lawful Premium');
+ }finally{globalThis.fetch=original;rmSync(root,{recursive:true,force:true});}
+});
