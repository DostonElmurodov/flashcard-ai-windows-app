import {useEffect,useRef,useState} from 'react';
import {Owl} from './components';
export default function TitleBar(){
 const buttons=useRef<(HTMLButtonElement|null)[]>([]),[opened,setOpened]=useState<string|null>(null);
 const names=['Owl AI','Edit','View'] as const;
 const open=async(index:number)=>{const button=buttons.current[index];if(!button)return;const box=button.getBoundingClientRect();setOpened(names[index]);try{await window.owl.openAppMenu(names[index],Math.round(box.left),Math.round(box.bottom));}finally{setOpened(null);button.focus();}};
 useEffect(()=>{const key=(event:KeyboardEvent)=>{if(event.key==='F10'&&!event.shiftKey){event.preventDefault();buttons.current[0]?.focus();}else if(event.altKey&&!event.ctrlKey){const index=['o','e','v'].indexOf(event.key.toLowerCase());if(index>=0){event.preventDefault();void open(index);}}};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key);},[]);
 return <header className="window-titlebar"><div className="window-title"><Owl size={19}/></div><nav className="window-menus" aria-label="Application menu">{names.map((name,index)=><button key={name} ref={node=>{buttons.current[index]=node;}} aria-haspopup="menu" aria-expanded={opened===name} onClick={()=>void open(index)} onKeyDown={event=>{if(event.key==='ArrowRight'||event.key==='ArrowLeft'){event.preventDefault();buttons.current[(index+(event.key==='ArrowRight'?1:2))%3]?.focus();}else if(event.key==='ArrowDown'){event.preventDefault();void open(index);}}}>{name}</button>)}</nav></header>;
}
