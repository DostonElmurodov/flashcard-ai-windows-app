// Keep this offline list aligned with the iOS EnglishIrregularVerbForms table.
const rows: readonly (readonly [string,string,string])[] = [
 ["be","was","been"],
 ["be","were","been"],
 ["become","became","become"],
 ["begin","began","begun"],
 ["break","broke","broken"],
 ["bring","brought","brought"],
 ["build","built","built"],
 ["buy","bought","bought"],
 ["catch","caught","caught"],
 ["choose","chose","chosen"],
 ["come","came","come"],
 ["do","did","done"],
 ["drink","drank","drunk"],
 ["drive","drove","driven"],
 ["eat","ate","eaten"],
 ["fall","fell","fallen"],
 ["feel","felt","felt"],
 ["find","found","found"],
 ["fly","flew","flown"],
 ["forget","forgot","forgotten"],
 ["get","got","gotten"],
 ["give","gave","given"],
 ["go","went","gone"],
 ["grow","grew","grown"],
 ["have","had","had"],
 ["hear","heard","heard"],
 ["hold","held","held"],
 ["keep","kept","kept"],
 ["know","knew","known"],
 ["leave","left","left"],
 ["lose","lost","lost"],
 ["make","made","made"],
 ["meet","met","met"],
 ["pay","paid","paid"],
 ["put","put","put"],
 ["read","read","read"],
 ["run","ran","run"],
 ["say","said","said"],
 ["see","saw","seen"],
 ["sell","sold","sold"],
 ["send","sent","sent"],
 ["sing","sang","sung"],
 ["sit","sat","sat"],
 ["sleep","slept","slept"],
 ["speak","spoke","spoken"],
 ["spend","spent","spent"],
 ["stand","stood","stood"],
 ["swim","swam","swum"],
 ["take","took","taken"],
 ["teach","taught","taught"],
 ["tell","told","told"],
 ["think","thought","thought"],
 ["understand","understood","understood"],
 ["wake","woke","woken"],
 ["wear","wore","worn"],
 ["win","won","won"],
 ["write","wrote","written"]
];

/** Unknown words are omitted rather than guessing an inflection. */
export function englishReviewVerbForms(word:string,language:string): {v2:string;v3:string}|null {
 if(language.trim().toLowerCase().replaceAll('_','-').split('-')[0]!=='en')return null;
 const normalized=word.trim().toLowerCase(),base=rows.find(row=>row.includes(normalized))?.[0];
 if(!base)return null;
 const matches=rows.filter(row=>row[0]===base);
 return {v2:[...new Set(matches.map(row=>row[1]))].join(' / '),v3:[...new Set(matches.map(row=>row[2]))].join(' / ')};
}
