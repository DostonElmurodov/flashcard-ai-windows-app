import { createHash, randomBytes } from 'node:crypto';
import { createServer } from 'node:http';
import type { AddressInfo } from 'node:net';

export interface GoogleConfiguration { clientId:string; clientSecret?:string }

/** Installed-app OAuth: browser + ephemeral IPv4 loopback, state, nonce and PKCE.
 * The returned ID token still needs signature/issuer/audience verification by our server.
 */
export async function googleSignIn(
 configuration:GoogleConfiguration,
 openBrowser:(url:string)=>Promise<void>,
 options:{signal?:AbortSignal;exchange?:typeof fetch}={},
):Promise<string|null> {
 const {clientId,clientSecret}=configuration;
 if(!/^[a-zA-Z0-9_-]+\.apps\.googleusercontent\.com$/.test(clientId))
  throw new Error('Google sign-in is not configured in this build. Please use email for now.');
 const signal=AbortSignal.any([AbortSignal.timeout(180000),...(options.signal?[options.signal]:[])]);
 signal.throwIfAborted();
 const verifier=randomBytes(32).toString('base64url');
 const state=randomBytes(32).toString('base64url');
 const nonce=randomBytes(32).toString('base64url');
 let complete!:(code:string|null)=>void,fail!:(error:unknown)=>void;
 const callback=new Promise<string|null>((resolve,reject)=>{complete=resolve;fail=reject;});
 // Register rejection handling before launching the external browser.
 void callback.catch(()=>{});
 let used=false,redirect='';
 const server=createServer((req,res)=>{
  res.setHeader('Content-Type','text/plain; charset=utf-8');
  res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
  res.setHeader('Content-Security-Policy',"default-src 'none'; frame-ancestors 'none'");
  let url:URL;
  try{url=new URL(req.url??'/',redirect);}catch{res.writeHead(400);res.end('Invalid request.');return;}
  if(req.method!=='GET'||req.headers.host!==new URL(redirect).host||url.pathname!=='/oauth/callback'){
   res.writeHead(404);res.end('Not found.');return;
  }
  if(used||url.searchParams.getAll('state').length!==1||url.searchParams.get('state')!==state){
   res.writeHead(400);res.end('This sign-in response is not valid. Return to Owl AI and try again.');return;
  }
  const error=url.searchParams.get('error'),code=url.searchParams.get('code');
  if(!error&&(!code||code.length>4096||url.searchParams.getAll('code').length!==1)){
   res.writeHead(400);res.end('Missing authorization code.');return;
  }
  used=true;
  res.end(error?'Google sign-in was cancelled. You can return to Owl AI.':'Google response received. Return to Owl AI to finish signing in.');
  if(error==='access_denied')complete(null);
  else if(error)fail(new Error('Google could not complete sign-in. Please try again.'));
  else complete(code);
 });
 server.requestTimeout=10000;server.headersTimeout=10000;
 const abort=()=>fail(signal.reason);
 signal.addEventListener('abort',abort,{once:true});
 try {
  await new Promise<void>((resolve,reject)=>{server.once('error',reject);server.listen(0,'127.0.0.1',()=>{server.removeListener('error',reject);resolve();});});
  signal.throwIfAborted();
  redirect=`http://127.0.0.1:${(server.address() as AddressInfo).port}/oauth/callback`;
  const auth=new URL('https://accounts.google.com/o/oauth2/v2/auth');
  auth.search=new URLSearchParams({client_id:clientId,redirect_uri:redirect,response_type:'code',scope:'openid email profile',state,nonce,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256',prompt:'select_account'}).toString();
  await openBrowser(auth.href);
  const code=await callback;
  if(code===null)return null;
  signal.throwIfAborted();
  const body=new URLSearchParams({client_id:clientId,code,code_verifier:verifier,redirect_uri:redirect,grant_type:'authorization_code'});
  if(clientSecret)body.set('client_secret',clientSecret);
  const response=await (options.exchange??fetch)('https://oauth2.googleapis.com/token',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body,signal,redirect:'error'});
  if(!response.ok)throw new Error('Google could not complete sign-in. Please try again or use email.');
  const data=await response.json() as {id_token?:unknown};
  const idToken=data.id_token;
  if(typeof idToken!=='string'||idToken.length>16384)throw new Error('Could not verify the Google response. Please try again.');
  let claims:{nonce?:string};
  try{claims=JSON.parse(Buffer.from(idToken.split('.')[1],'base64url').toString('utf8'));}
  catch{throw new Error('Could not verify the Google response. Please try again.');}
  if(claims?.nonce!==nonce)throw new Error('Could not verify the Google response. Please try again.');
  signal.throwIfAborted();return idToken;
 } finally {
  signal.removeEventListener('abort',abort);
  server.close();server.closeAllConnections();
 }
}
