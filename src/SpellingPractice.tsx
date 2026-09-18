import {useId,useState} from 'react';
import {matchesSpelling} from '../shared/spelling-practice';

export default function SpellingPractice({expected,language}:{expected:string;language:string}){
 const [answer,setAnswer]=useState(''),[correct,setCorrect]=useState<boolean|null>(null);
 const id=useId(),feedbackId=id+'-feedback';
 return <form className={`spelling-practice${correct===null?'':correct?' spelling-correct':' spelling-incorrect'}`} onKeyDown={event=>event.stopPropagation()} onSubmit={event=>{event.preventDefault();if(answer.trim())setCorrect(matchesSpelling(answer,expected));}}>
  <label htmlFor={id}>Your answer</label>
  <div className="spelling-entry"><input id={id} value={answer} lang={language} dir="auto" autoComplete="off" autoCorrect="off" autoCapitalize="none" spellCheck={false} aria-invalid={correct===false} aria-describedby={feedbackId} onChange={event=>{setAnswer(event.target.value);setCorrect(null);}}/><button className="button" type="submit" disabled={!answer.trim()}>Check</button></div>
  <p className="spelling-feedback" id={feedbackId} role="status">{correct===null?'Type the word you’re learning.':correct?'Correct':'Try again'}</p>
 </form>;
}
