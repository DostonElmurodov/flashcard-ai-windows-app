import React,{useState} from 'react';
import {createRoot} from 'react-dom/client';
import Settings from '../../src/Settings';
import Review from '../../src/Review';
import {WorkspaceBridgeProvider} from '../../src/WorkspaceBridge';
import type {Bridge,Snapshot,ReviewTranslation} from '../../shared/types';
import {secondaryReviewLanguage} from '../../shared/secondary-review';
import '../../src/styles.css';
import '../../src/ink-indigo.css';

const card={due:new Date().toISOString(),stability:0,difficulty:0,elapsed_days:0,scheduled_days:0,reps:0,lapses:0,state:0,learning_steps:0};
let data:Snapshot={workspaceId:'a',scopeRevision:'1:a',account:{profile:{id:'a',email:'learner@example.com'},entitlement:{status:'premium',is_trial:false,auto_renew:true,was_ever_paid:true},testMode:false},settings:{nativeLanguage:'ru',learningLanguage:'en-us',secondaryReviewLanguage:null,theme:'light',accent:'indigo',darkAccent:'teal',dailyGoal:5,direction:'forward',dayStart:0,retention:.9,reminders:false,reminderTime:'19:00',reminderStart:'08:00',reminderEnd:'20:00',reminderCount:10,keepInTray:false,launchAtLogin:false,apiBase:'https://api.example.com',onboardingComplete:true},decks:[{id:'travel',name:'Travel words',description:'',nativeLanguage:'ru',learningLanguage:'en-us',active:true,createdAt:new Date().toISOString()}],words:[{id:'journey',word:'journey',translation:'путешествие',examples:['Every journey begins with a single step.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}},{id:'airport',word:'airport',translation:'аэропорт',examples:['We arrived at the airport early.'],deckId:'travel',createdAt:new Date().toISOString(),card:{...card},reverse:{...card}}],reviewedToday:0,streak:0,activity:[]};
let publish=()=>{};
const calls:{wordId:string;language:string;resolve:(value:ReviewTranslation)=>void;reject:(error:Error)=>void}[]=[];
const bridge={
 systemTimeFormat:async()=>({hour12:false}),
 saveSettings:async(patch:Partial<Snapshot['settings']>)=>{const settings={...data.settings,...patch};settings.secondaryReviewLanguage=secondaryReviewLanguage(settings.secondaryReviewLanguage,settings.nativeLanguage);data={...data,settings};return settings;},
 reviewSession:async()=>{},queue:async()=>data.words,
 previews:async()=>Object.fromEntries([1,2,3,4].map(x=>[x,new Date(Date.now()+x*86400000).toISOString()])),
 review:async(id:string)=>{const word=data.words.find(x=>x.id===id)!;data={...data,words:data.words.filter(x=>x.id!==id),reviewedToday:data.reviewedToday+1};return word;},
 reviewTranslation:async(wordId:string)=>new Promise<ReviewTranslation>((resolve,reject)=>calls.push({wordId,language:data.settings.secondaryReviewLanguage!,resolve,reject}))
} as unknown as Bridge;
(window as any).secondaryFixture={
 calls:()=>calls.map(({wordId,language})=>({wordId,language})),
 resolve:(index:number,translation:string,explanation:string)=>calls[index].resolve({language_code:calls[index].language,translation,explanation}),
 reject:(index:number)=>calls[index].reject(new Error('Connection lost. Please try again.')),
 setSecondary:(value:string|null)=>{data={...data,settings:{...data.settings,secondaryReviewLanguage:value}};publish();},
 snapshot:()=>data
};
function Harness(){
 const [snapshot,setSnapshot]=useState(data),[review,setReview]=useState(false);
 publish=()=>setSnapshot({...data});
 return <WorkspaceBridgeProvider bridge={bridge}><main style={{maxWidth:1050,margin:'30px auto',padding:'0 24px'}}><h1>Your preferences</h1><p className="muted">A learning routine that feels like you.</p><Settings key={JSON.stringify(snapshot.settings)} settings={snapshot.settings} onChanged={async()=>publish()} notify={()=>{}}/><button className="button" onClick={()=>setReview(true)}>Open review</button>{review&&<Review snapshot={snapshot} onClose={()=>setReview(false)} onChanged={async()=>publish()}/>}</main></WorkspaceBridgeProvider>;
}
createRoot(document.getElementById('root')!).render(<Harness/>);
