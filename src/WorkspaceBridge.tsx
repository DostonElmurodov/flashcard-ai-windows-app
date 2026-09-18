import { createContext,useContext } from 'react';
import type { ReactNode } from 'react';
import type { Bridge } from '../shared/types';

const WorkspaceBridgeContext=createContext<Bridge|null>(null);

export function WorkspaceBridgeProvider({bridge,children}:{bridge:Bridge;children:ReactNode}){
 return <WorkspaceBridgeContext.Provider value={bridge}>{children}</WorkspaceBridgeContext.Provider>;
}

// Every callback keeps the bridge from its render, including after an await.
// Its immutable scope is rejected by the main process once the account changes.
export function useOwl():Bridge{
 const bridge=useContext(WorkspaceBridgeContext);
 if(!bridge)throw new Error('This page requires an active Owl AI workspace.');
 return bridge;
}
