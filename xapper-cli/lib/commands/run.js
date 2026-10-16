const fs = require('fs');
const path = require('path');

async function run(client, scriptPath) {
  if (!fs.existsSync(scriptPath)) {
    throw new Error(`Script not found: ${scriptPath}`);
  }

  const script = fs.readFileSync(scriptPath, 'utf8');

  try {
    const result = await client.call('xapper_run', { script });

    // 결과 배열 출력
    console.log(JSON.stringify(result, null, 2));

    // 실패 여부 판단
    const hasError = Array.isArray(result) && result.some(r => r.status === 'error' || r.failed);

    return hasError ? 1 : 0;
  } catch (error) {
    console.error(JSON.stringify({
      status: 'error',
      error: error.message,
      scriptPath
    }, null, 2));
    return 1;
  }
}

module.exports = { run };
