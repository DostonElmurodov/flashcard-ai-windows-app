import {createHash} from 'node:crypto';
import {existsSync} from 'node:fs';
import {join} from 'node:path';
import {Store} from './store';

// A server and an immutable account ID define one private local workspace.
export function accountScope(origin:string,owner:string):string {
 return createHash('sha256').update(new URL(origin).origin+'\0'+owner).digest('hex');
}
export class Workspaces {
 private constructor(readonly guest:Store,public current:Store,private root:string){}
 static async open(root:string){const guest=await Store.open(join(root,'owl.sqlite'));return new Workspaces(guest,guest,root);}
 async select(origin:string,owner:string|null){
  const scope=owner?accountScope(origin,owner):'guest';
  if(this.current.owner()===scope)return;
  let next=this.guest;
  if(owner){try{const path=join(this.root,'accounts',scope+'.sqlite'),existed=existsSync(path);next=await Store.open(path,scope);if(!existed)next.saveSettings(this.guest.settings());}catch(error){const previous=this.current;this.current=this.guest;if(previous!==this.guest)previous.close();throw error;}}
  const previous=this.current;this.current=next;
  if(previous!==this.guest)previous.close();
 }
 close(){if(this.current!==this.guest)this.current.close();this.guest.close();}
}
