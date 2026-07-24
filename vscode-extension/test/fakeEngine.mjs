// Emits an initial prompt, then for each stdin line echoes a result and re-prompts.
// `ask-*` commands reproduce the CLI's mid-command prompts (batch's block reader and
// approval loop, build-app's review loop): the engine blocks on input *without* the
// top-level `CodeGenerator >> ` marker, and only re-prompts once answered.
process.stdout.write('CodeGenerator >> ');
let buf = '';
let awaiting = false;
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

// Byte-for-byte the prompts the real CLI writes before Console.ReadLine().
const MID_COMMAND_PROMPTS = {
  'ask-dots': 'Enter one command per line. A blank line ends the batch.\n... ',
  'ask-arrow': '  [a]pprove [target] [sqlite]   add <name>   [c]ancel\n> ',
  'ask-approve': '  [1] create class Foo   approve? (y/n) ',
};

function handle(line) {
  if (awaiting) {
    awaiting = false;
    process.stdout.write(`answered: ${line}\n`);
    process.stdout.write('CodeGenerator >> ');
    return;
  }
  if (Object.hasOwn(MID_COMMAND_PROMPTS, line)) {
    awaiting = true;
    process.stdout.write(MID_COMMAND_PROMPTS[line]);
    return;
  }
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
