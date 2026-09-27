import type {AccountState} from './types';
export const ACCESS_TTL_MS=5*60*1000;
export type AccessOperation='add'|'edit'|'ai'|'read'|'review'|'delete'|'export';
export function activeAccess(state:AccountState|undefined,now=new Date()):boolean {
 const ent=state?.entitlement;if(!state?.profile)return false;
 if(state.testMode)return true;
 if(!ent||!['premium','trial','grace'].includes(ent.status))return false;
 const expiry=Date.parse(ent.expires_at??''),checked=Date.parse(ent.checked_at??'');
 return Number.isFinite(expiry)&&expiry>now.getTime()&&Number.isFinite(checked)&&checked<=now.getTime()&&now.getTime()-checked<ACCESS_TTL_MS;
}
export function retainedPaidAccess(state:AccountState|undefined):boolean {
 const ent=state?.entitlement;
 return !!state?.profile&&ent?.was_ever_paid===true&&(['premium','grace','expired_paid'].includes(ent.status))&&(!['premium','grace'].includes(ent.status)||Number.isFinite(Date.parse(ent.expires_at??'')));
}
export function evaluateAccess(state:AccountState|undefined,operation:AccessOperation,count:number,now=new Date()):{allow:boolean;reason:string|null}{
 const allow=()=>({allow:true,reason:null});const deny=(reason:string)=>({allow:false,reason});
 if(['delete','export','read','review'].includes(operation))return allow();
 if(!state?.profile)return deny('Sign in to your Owl AI account to continue on desktop.');
 if(activeAccess(state,now))return allow();
 if(operation==='ai')return deny('An active shared Premium subscription is needed for AI translations. Open your profile to manage access.');
 const status=state.entitlement?.status??'free';
 if(!['free','expired_trial','revoked','premium','trial','grace','expired_paid'].includes(status))return deny('Subscription access needs verification. Contact support to recover access.');
 if(status!=='free')return deny('Refresh or renew your subscription before adding or editing content. Saved cards remain available.');
 if(operation==='add'&&count>=10)return deny('Free access includes 10 saved cards across all sets and languages. Open your profile to manage your subscription.');
 return allow();
}
export function eligibleWordIds(state:AccountState|undefined,words:readonly {id:string;createdAt:string}[],now=new Date()):Set<string>{
 const timestamp=(word:{createdAt:string})=>{const at=Date.parse(word.createdAt);return Number.isFinite(at)?at:Infinity;};
 const ordered=[...words].sort((a,b)=>timestamp(a)-timestamp(b)||(a.id<b.id?-1:a.id>b.id?1:0));
 return new Set((activeAccess(state,now)||retainedPaidAccess(state)?ordered:ordered.slice(0,10)).map(w=>w.id));
}
