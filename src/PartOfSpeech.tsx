import {partOfSpeech} from '../shared/part-of-speech';

export default function PartOfSpeech({value}:{value?:string}){
 const label=partOfSpeech(value);
 return label?<span className="part-of-speech" aria-label={`Part of speech: ${label}`}>{label}</span>:null;
}
