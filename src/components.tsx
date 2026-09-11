import { useEffect,useRef,type ReactNode } from 'react';
import { X,Volume2 } from 'lucide-react';
import { languages } from '../shared/types';
export function Owl({size=44}:{size?:number}){return <svg width={size} height={size} viewBox="0 0 80 80" fill="none" aria-hidden="true"><path d="M12 19 9 5l21 10a43 43 0 0 1 21 0L71 5l-4 18c17 29 0 51-27 51S-4 48 12 19Z" fill="currentColor"/><ellipse cx="26" cy="34" rx="18" ry="20" fill="#fff"/><ellipse cx="54" cy="34" rx="18" ry="20" fill="#fff"/><circle cx="30" cy="35" r="8" fill="#292344"/><circle cx="50" cy="35" r="8" fill="#292344"/><circle cx="32" cy="32" r="2.8" fill="white"/><circle cx="52" cy="32" r="2.8" fill="white"/><path d="m33 51 7 10 7-10" fill="#efb36a"/><path d="M26 70v7m28-7v7" stroke="currentColor" strokeWidth="5" strokeLinecap="round"/></svg>;}
export function Modal({title,children,onClose,wide=false}:{title:string;children:ReactNode;onClose:()=>void;wide?:boolean}){
 const ref=useRef<HTMLDialogElement>(null);useEffect(()=>{ref.current?.showModal();},[]);
 return <dialog ref={ref} className={wide?'modal wide':'modal'} onCancel={e=>{e.preventDefault();onClose();}} onClick={e=>{if(e.target===e.currentTarget)onClose();}}><div className="modal-head"><h2>{title}</h2><button className="icon-button" onClick={onClose} aria-label="Close"><X size={20}/></button></div>{children}</dialog>;
}
export function LanguageSelect({value,onChange,label}:{value:string;onChange:(value:string)=>void;label:string}){return <label className="field">{label}<select value={value} onChange={e=>onChange(e.target.value)}>{languages.map(([code,name])=><option key={code} value={code}>{name}</option>)}</select></label>;}
let lastSpoken='',slow=false;
export function Speak({word,language,onError}:{word:string;language:string;onError:(message:string)=>void}){
 return <button className="icon-button" title="Listen · tap again for slow playback" aria-label={`Pronounce ${word}`} onClick={()=>{if(!('speechSynthesis'in window)){onError('Speech is unavailable on this computer.');return;}const voices=speechSynthesis.getVoices(),voice=voices.find(x=>x.lang.toLowerCase()===language.toLowerCase())??voices.find(x=>x.lang.split('-')[0]===language.split('-')[0]);if(!voice){onError('Install a Windows speech voice for this language in Settings → Time & language → Speech.');return;}slow=lastSpoken===word?!slow:false;lastSpoken=word;speechSynthesis.cancel();const utterance=new SpeechSynthesisUtterance(word);utterance.voice=voice;utterance.lang=language;utterance.rate=slow?.3:1;speechSynthesis.speak(utterance);}}><Volume2 size={17}/></button>;
}
export function Empty({title,body,action}:{title:string;body:string;action?:ReactNode}){return <div className="empty"><div className="empty-owl"><Owl size={54}/></div><h3>{title}</h3><p>{body}</p>{action}</div>;}
export const errorMessage=(error:unknown)=>error instanceof Error?error.message:'Something went wrong. Please try again.';
