import {useEffect,useState} from 'react';
import {languageName,type ReviewTranslation} from '../shared/types';
import {useOwl} from './WorkspaceBridge';
import {errorMessage} from './components';
import LanguageFlag from './LanguageFlag';

export default function SecondaryReview({wordId,language,accessRevision}:{wordId:string;language:string;accessRevision:string}){
 const owl=useOwl();
 const [content,setContent]=useState<ReviewTranslation|null>(null),[error,setError]=useState(''),[retry,setRetry]=useState(0);
 useEffect(()=>{
  let active=true;setContent(null);setError('');
  void owl.reviewTranslation(wordId).then(value=>{if(active)setContent(value);}).catch(reason=>{if(active)setError(errorMessage(reason));});
  return()=>{active=false;};
 },[owl,wordId,language,accessRevision,retry]);
 return <section className="secondary-review" aria-label={`Second language: ${languageName(language)}`} aria-live="polite">
  <div className="secondary-review-label"><LanguageFlag code={language}/><span>{languageName(language)}</span></div>
  {content?<><p className="secondary-translation" lang={language} dir="auto">{content.translation}</p><p className="secondary-explanation" lang={language} dir="auto">{content.explanation}</p></>:error?<><p className="secondary-error">{error}</p><button className="text-button" onClick={()=>setRetry(value=>value+1)}>Retry translation</button></>:<p className="secondary-loading" role="status">Loading translation…</p>}
 </section>;
}
