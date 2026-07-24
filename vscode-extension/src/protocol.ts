// vscode-extension/src/protocol.ts
import { EngineState } from './engineHost';

export type InboundMessage =
  | { type: 'submit'; text: string }
  | { type: 'ready' }
  | { type: 'dbg'; text: string };

export type OutboundMessage =
  | { type: 'output'; chunk: string; stream: 'stdout' | 'stderr' }
  | { type: 'state'; state: EngineState }
  | { type: 'system'; text: string };
