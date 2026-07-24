// vscode-extension/media/main.js
import { PROMPT, echoPrefix } from './prompt.js';

const vscode = acquireVsCodeApi();
const transcript = document.getElementById('transcript');
const form = document.getElementById('inputform');
const input = document.getElementById('input');
const promptLabel = document.getElementById('promptlabel');
const history = [];
let histIndex = -1;
let state = 'starting';

// Single source of truth: the label and the transcript echo both come from PROMPT.
// Trailing space trimmed because the flex `gap` provides the spacing on screen.
promptLabel.textContent = PROMPT.trimEnd();

function append(text, cls) {
  const line = document.createElement('div');
  line.className = 'line ' + cls;
  line.textContent = text;
  transcript.appendChild(line);
  transcript.scrollTop = transcript.scrollHeight;
}

form.addEventListener('submit', (e) => {
  e.preventDefault();
  const text = input.value;
  // At the top-level prompt a blank line is noise, but as an *answer* it is meaningful and
  // often the only way forward: it ends a batch block and aborts the snapshot confirmation.
  if (text.trim() === '' && state !== 'prompting') return;
  append(echoPrefix(state) + text, 'cmd');
  if (text.trim() !== '') {
    history.push(text);
    histIndex = history.length;
  }
  vscode.postMessage({ type: 'submit', text });
  input.value = '';
});

input.addEventListener('keydown', (e) => {
  if (e.key === 'ArrowUp' && histIndex > 0) {
    histIndex--; input.value = history[histIndex];
    e.preventDefault();
  } else if (e.key === 'ArrowDown' && histIndex < history.length - 1) {
    histIndex++; input.value = history[histIndex];
    e.preventDefault();
  }
});

window.addEventListener('message', (event) => {
  const msg = event.data;
  if (msg.type === 'output') {
    // stderr carries background chatter (e.g. Wikipedia store-update progress), not errors —
    // this CLI reports real errors on stdout. Render it muted, not alarming.
    append(msg.chunk.replace(/\s+$/, ''), msg.stream === 'stderr' ? 'info' : 'out');
  } else if (msg.type === 'state') {
    // `prompting` means the engine is mid-command and blocked on a question (build-app's
    // review loop, batch's approvals). It cannot proceed until answered, so input stays
    // enabled — and takes focus, since the answer is the only thing that unblocks it.
    state = msg.state;
    input.disabled = msg.state === 'busy' || msg.state === 'starting' || msg.state === 'stopped';
    promptLabel.classList.toggle('dim', input.disabled);
    input.placeholder = msg.state === 'busy' ? 'working…'
      : msg.state === 'starting' ? 'starting engine…'
      : msg.state === 'stopped' ? 'engine stopped'
      : msg.state === 'prompting' ? 'answer the question above…'
      : 'Type a command…';
    if (msg.state === 'prompting') input.focus();
    vscode.postMessage({ type: 'dbg', text: 'received state=' + msg.state + ' -> input.disabled=' + input.disabled });
  } else if (msg.type === 'system') {
    if (msg.text === '__clear__') transcript.replaceChildren();
    else if (msg.text === '__focus__') input.focus();
    else append(msg.text, 'sys');
  }
});

vscode.postMessage({ type: 'dbg', text: 'webview script loaded' });
vscode.postMessage({ type: 'ready' });
