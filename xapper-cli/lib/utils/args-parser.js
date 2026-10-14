function parseArgs(argv) {
  const args = {};
  let currentKey = null;

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];

    if (arg.startsWith('--')) {
      currentKey = arg.slice(2);
      // 다음이 값이 아니면 true로 설정
      if (i + 1 >= argv.length || argv[i + 1].startsWith('--')) {
        args[currentKey] = true;
        currentKey = null;
      }
    } else if (currentKey) {
      args[currentKey] = arg;
      currentKey = null;
    }
  }

  return args;
}

function getArg(args, key, defaultValue = null) {
  const value = args[key];
  if (value === undefined) return defaultValue;
  if (value === true) return true;
  if (value === 'true') return true;
  if (value === 'false') return false;
  if (!isNaN(value)) return Number(value);
  return value;
}

module.exports = { parseArgs, getArg };
