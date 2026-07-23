// vscode-extension/test/fakeEngine.mjs
// Emits an initial prompt, then for each stdin line echoes a result and re-prompts.
process.stdout.write('CodeGenerator >> ');
let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (d) => {
  buf += d;
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim();
    buf = buf.slice(i + 1);
    handle(line);
  }
});
function handle(line) {
  if (line === 'boom') {
    process.stderr.write('error: boom\n');
  } else if (line === 'crash') {
    process.exit(1);
    return;
  } else {
    process.stdout.write(`ran: ${line}\n`);
  }
  process.stdout.write('CodeGenerator >> ');
}
