// Optional linguistic detail; older cards and cached AI replies may omit it.
export function partOfSpeech(value:unknown):string|undefined {
 if(typeof value!=='string')return undefined;
 const text=value.trim();return text&&text.length<=100?text:undefined;
}
