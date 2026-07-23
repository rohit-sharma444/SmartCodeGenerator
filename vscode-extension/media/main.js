// vscode-extension/media/main.js
const vscode = acquireVsCodeApi();
const transcript = document.getElementById('transcript');
const form = document.getElementById('inputform');
const input = document.getElementById('input');
const history = [];
let histIndex = -1;

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
  if (text.trim() === '') return;
  append('> ' + text, 'cmd');
  history.push(text);
  histIndex = history.length;
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
    append(msg.chunk.replace(/\s+$/, ''), msg.stream === 'stderr' ? 'err' : 'out');
  } else if (msg.type === 'state') {
    input.disabled = msg.state === 'busy' || msg.state === 'starting' || msg.state === 'stopped';
    input.placeholder = msg.state === 'busy' ? 'working…'
      : msg.state === 'stopped' ? 'engine stopped'
      : 'Type a command…';
  } else if (msg.type === 'system') {
    if (msg.text === '__clear__') transcript.replaceChildren();
    else if (msg.text === '__focus__') input.focus();
    else append(msg.text, 'sys');
  }
});

vscode.postMessage({ type: 'ready' });
