import {canonicalLanguage} from '../shared/secondary-review';

// Bundled reference artwork works on Windows without depending on flag emoji fonts.
export default function LanguageFlag({code}:{code:string}){
 const language=canonicalLanguage(code);
 return language?<img className="language-flag" src={new URL(`./assets/flags/${language}.png`,import.meta.url).href} alt="" aria-hidden="true"/>:null;
}
