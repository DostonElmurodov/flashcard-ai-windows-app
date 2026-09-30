export type AddCardMethod='manual'|'ai'|'paste'|'file';

type MethodStorage=Pick<Storage,'getItem'|'setItem'>;
const methodKey=(workspaceId:string)=>`owl.addCardMethod.${workspaceId}`;

export function readAddCardMethod(workspaceId:string,storage?:MethodStorage):AddCardMethod{
 try{
  const value=(storage??localStorage).getItem(methodKey(workspaceId));
  if(value==='manual'||value==='ai'||value==='paste'||value==='file')return value;
 }catch{}
 return 'manual';
}

export function saveAddCardMethod(workspaceId:string,method:AddCardMethod,storage?:MethodStorage):void{
 try{(storage??localStorage).setItem(methodKey(workspaceId),method);}catch{}
}
