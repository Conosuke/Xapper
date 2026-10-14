const { parseArgs, getArg } = require('../utils/args-parser');

async function launch(client, args) {
  const exePath = getArg(args, 'exe');
  if (!exePath) {
    throw new Error('--exe is required');
  }

  const result = await client.call('xapper_launch', { exePath });

  // PID 또는 전체 결과 출력
  if (typeof result === 'object' && result.pid) {
    console.log(JSON.stringify({ pid: result.pid }, null, 2));
  } else {
    console.log(JSON.stringify(result, null, 2));
  }

  return 0;
}

async function click(client, args) {
  const target = getArg(args, 'target');
  const x = getArg(args, 'x');
  const y = getArg(args, 'y');

  if (!target) {
    throw new Error('--target is required');
  }

  const toolArgs = { target };
  if (x !== null) toolArgs.x = x;
  if (y !== null) toolArgs.y = y;

  const result = await client.call('xapper_click', toolArgs);
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function doubleclick(client, args) {
  const target = getArg(args, 'target');
  const x = getArg(args, 'x');
  const y = getArg(args, 'y');

  if (!target) {
    throw new Error('--target is required');
  }

  const toolArgs = { target };
  if (x !== null) toolArgs.x = x;
  if (y !== null) toolArgs.y = y;

  const result = await client.call('xapper_doubleclick', toolArgs);
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function type(client, args) {
  const target = getArg(args, 'target');
  const text = getArg(args, 'text');

  if (!target) {
    throw new Error('--target is required');
  }
  if (!text) {
    throw new Error('--text is required');
  }

  const result = await client.call('xapper_type', { target, text });
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function key(client, args) {
  const keyName = getArg(args, 'key');

  if (!keyName) {
    throw new Error('--key is required');
  }

  const result = await client.call('xapper_key', { key: keyName });
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function drag(client, args) {
  const target = getArg(args, 'target');
  const fromX = getArg(args, 'fromX');
  const fromY = getArg(args, 'fromY');
  const toX = getArg(args, 'toX');
  const toY = getArg(args, 'toY');

  if (!target) {
    throw new Error('--target is required');
  }

  const toolArgs = { target };
  if (fromX !== null) toolArgs.fromX = fromX;
  if (fromY !== null) toolArgs.fromY = fromY;
  if (toX !== null) toolArgs.toX = toX;
  if (toY !== null) toolArgs.toY = toY;

  const result = await client.call('xapper_drag', toolArgs);
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function select(client, args) {
  const target = getArg(args, 'target');
  const value = getArg(args, 'value');

  if (!target) {
    throw new Error('--target is required');
  }
  if (!value) {
    throw new Error('--value is required');
  }

  const result = await client.call('xapper_select', { target, value });
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function screenshot(client, args) {
  const ref = getArg(args, 'ref');
  const annotate = getArg(args, 'annotate', false) === true || getArg(args, 'annotate') === 'true';

  const toolArgs = {};
  if (ref) toolArgs.ref = ref;
  if (annotate) toolArgs.annotate = annotate;

  const result = await client.call('xapper_screenshot', toolArgs);
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

async function snapshot(client, args) {
  const ref = getArg(args, 'ref');

  const toolArgs = {};
  if (ref) toolArgs.ref = ref;

  const result = await client.call('xapper_snapshot', toolArgs);
  console.log(JSON.stringify(result, null, 2));

  return 0;
}

module.exports = {
  launch,
  click,
  doubleclick,
  type,
  key,
  drag,
  select,
  screenshot,
  snapshot
};
