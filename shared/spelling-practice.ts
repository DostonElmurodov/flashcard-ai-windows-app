export function matchesSpelling(answer:string,expected:string):boolean {
 const normalize=(value:string)=>value.normalize('NFC').toLowerCase().trim().replace(/\s+/gu,' ');
 const normalized=normalize(answer);
 return normalized.length>0&&normalized===normalize(expected);
}
