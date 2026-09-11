import { parse } from 'csv-parse/sync';
import type { Draft } from '../shared/types';
export function parseImport(text:string,mode:'auto'|'pairs'|'words',extension=''):Draft[] {
 if(text.length>5_000_000) throw new Error('Import is too large. Use a file smaller than 5 MB of text.');
 const lines=text.replace(/^\uFEFF/,'').replace(/\r\n?/g,'\n').split('\n').map(x=>x.trim()).filter(Boolean);
 let rows:Draft[]=[];
 if(mode==='pairs') for(let i=0;i<lines.length;i+=2) rows.push({word:lines[i],translation:lines[i+1]??''});
 else if(mode==='words') rows=Array.from(new Intl.Segmenter(undefined,{granularity:'word'}).segment(text)).filter(x=>x.isWordLike).map(x=>({word:x.segment,translation:''}));
 else if(extension==='csv'||extension==='tsv') rows=(parse(text,{bom:true,skip_empty_lines:true,relax_column_count:true,delimiter:extension==='tsv'?'\t':','}) as string[][]).map(x=>({word:x[0]?.trim()??'',translation:x.slice(1).join(', ').trim()}));
 else rows=lines.map(line=>{const sep=['\t',' = ',' — ',' – ',' - ',';'].find(s=>line.includes(s));if(!sep)return {word:line,translation:''};const i=line.indexOf(sep);return {word:line.slice(0,i).trim(),translation:line.slice(i+sep.length).trim()};});
 const seen=new Set<string>();
 const unique=rows.filter((row,i)=>{if(!row.word)return false;if(i===0&&/^(word|term|front)$/i.test(row.word)&&/^(translation|definition|back)$/i.test(row.translation))return false;const key=row.word.normalize('NFKC').toLowerCase()+'\0'+row.translation.normalize('NFKC').toLowerCase();if(seen.has(key))return false;seen.add(key);return true;});
 if(unique.length>2000)throw new Error('Import up to 2,000 cards at a time. Split this file into smaller sets.');
 return unique;
}
