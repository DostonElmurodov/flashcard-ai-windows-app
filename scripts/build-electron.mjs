import { build } from 'esbuild';
import { existsSync,readFileSync } from 'node:fs';
const configPath='build/google-oauth.local.json';
const config=existsSync(configPath)?JSON.parse(readFileSync(configPath,'utf8')):{};
const clientId=process.env.OWL_GOOGLE_CLIENT_ID??config.clientId??'';
const clientSecret=process.env.OWL_GOOGLE_CLIENT_SECRET??config.clientSecret??'';
if(clientId&&!/^[a-zA-Z0-9_-]+\.apps\.googleusercontent\.com$/.test(clientId))throw new Error('Invalid Google desktop OAuth client ID.');
await build({entryPoints:['electron/main.ts'],bundle:true,platform:'node',format:'cjs',outfile:'dist-electron/main.cjs',packages:'external',target:'node22',define:{GOOGLE_OAUTH_CLIENT_ID:JSON.stringify(clientId),GOOGLE_OAUTH_CLIENT_SECRET:JSON.stringify(clientSecret)}});
await build({entryPoints:['electron/preload.ts'],bundle:true,platform:'node',format:'cjs',outfile:'dist-electron/preload.cjs',external:['electron'],target:'node22'});
