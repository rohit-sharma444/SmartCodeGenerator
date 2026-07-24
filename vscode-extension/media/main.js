// vscode-extension/media/main.js
import { PROMPT, echoPrefix, isClear, stripTrailingPrompt } from './prompt.js';

const vscode = acquireVsCodeApi();
const transcript = document.getElementById('transcript');
const form = document.getElementById('inputform');
const input = document.getElementById('input');
const promptLabel = document.getElementById('promptlabel');
const history = [];
let histIndex = -1;
let state = 'starting';
// Whether the input held focus at the moment it was disabled, so it can be handed back when
// the engine returns. See the state handler for why this is remembered rather than assumed.
let refocusWhenReady = false;

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

  // `clear`/`cls` is the panel's own business — see isClear. Not forwarded to the engine:
  // it would wipe nothing there and cost a pointless busy/ready round trip. Not recorded in
  // history either, matching CommandLineEditor, which never records it.
  // Only intercepted at the top level: while `prompting` the engine is waiting on an answer,
  // and "clear" may well BE that answer.
  if (state !== 'prompting' && isClear(text)) {
    transcript.replaceChildren();
    input.value = '';
    return;
  }

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
    if (msg.stream === 'stderr') {
      // stderr carries background chatter (e.g. Wikipedia store-update progress), not
      // errors — this CLI reports real errors on stdout. Render it muted, not alarming.
      // It never carries the prompt marker, so no stripping applies here.
      append(msg.chunk.replace(/\s+$/, ''), 'info');
    } else {
      // The engine's own `CodeGenerator >> ` prompt arrives on stdout (engineHost's ready
      // detection needs to see it, so it can't be suppressed at the source) and would
      // otherwise double up with the gutter label / echo prefix in the transcript. Strip
      // a trailing occurrence before rendering; if nothing is left, the chunk was the
      // prompt alone, so append nothing rather than a blank line.
      const text = stripTrailingPrompt(msg.chunk).replace(/\s+$/, '');
      if (text !== '') append(text, 'out');
    }
  } else if (msg.type === 'state') {
    // `prompting` means the engine is mid-command and blocked on a question (build-app's
    // review loop, batch's approvals). It cannot proceed until answered, so input stays
    // enabled — and takes focus, since the answer is the only thing that unblocks it.
    state = msg.state;

    const disable = msg.state === 'busy' || msg.state === 'starting' || msg.state === 'stopped';

    // Disabling an element drops the browser's focus from it, and re-enabling does not give it
    // back — so without this you finish a command and have to click the box before you can type
    // the next one. Record whether the input actually held focus, BEFORE disabling, because
    // once it is disabled document.activeElement can no longer tell us.
    if (disable && !input.disabled) refocusWhenReady = document.activeElement === input;

    input.disabled = disable;
    promptLabel.classList.toggle('dim', input.disabled);
    input.placeholder = msg.state === 'busy' ? 'working…'
      : msg.state === 'starting' ? 'starting engine…'
      : msg.state === 'stopped' ? 'engine stopped'
      : msg.state === 'prompting' ? 'answer the question above…'
      : 'Type a command…';

    if (msg.state === 'prompting') {
      // The engine is blocked on its own question and the answer is the only thing that
      // unblocks it, so take focus unconditionally here.
      input.focus();
    } else if (!disable && refocusWhenReady) {
      // document.hasFocus() is the guard against stealing keystrokes: if the command ran long
      // enough that the user moved to an editor, this webview no longer has focus and must not
      // grab it back. Only refocus when the panel still has their attention.
      if (document.hasFocus()) input.focus();
      refocusWhenReady = false;
    }
    vscode.postMessage({ type: 'dbg', text: 'received state=' + msg.state + ' -> input.disabled=' + input.disabled });
  } else if (msg.type === 'system') {
    if (msg.text === '__clear__') transcript.replaceChildren();
    else if (msg.text === '__focus__') input.focus();
    else append(msg.text, 'sys');
  }
});

vscode.postMessage({ type: 'dbg', text: 'webview script loaded' });
vscode.postMessage({ type: 'ready' });
