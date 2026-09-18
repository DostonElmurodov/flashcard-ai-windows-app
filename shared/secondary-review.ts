import {languages,type AccountState} from './types';

export function canonicalLanguage(value:unknown):string|null {
 if(typeof value!=='string')return null;
 const code=value.trim().toLowerCase().replaceAll('_','-');
 if(['en','en-us','en-gb'].includes(code))return 'en-us';
 if(['zh','zh-cn','zh-hans'].includes(code))return 'zh';
 return languages.some(([supported])=>supported===code)?code:null;
}

export function secondaryReviewLanguage(value:unknown,nativeLanguage:string):string|null {
 const code=canonicalLanguage(value);
 return code&&code!==canonicalLanguage(nativeLanguage)?code:null;
}

export function reviewTranslationAccess(account:AccountState|undefined):string|null {
 if(!account?.profile)return 'Sign in to see a second-language translation.';
 if(!account.testMode&&!['premium','trial','grace'].includes(account.entitlement?.status??''))return 'An active shared Premium subscription is needed for AI translations.';
 return null;
}
