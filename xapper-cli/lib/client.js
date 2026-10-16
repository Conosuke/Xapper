const { spawn } = require('child_process');
const path = require('path');
const { EventEmitter } = require('events');

class McpClient extends EventEmitter {
  constructor(serverPath) {
    super();
    this.serverPath = serverPath;
    this.process = null;
    this.messageId = 0;
    this.pendingRequests = new Map();
    this.buffer = '';
  }

  start() {
    return new Promise((resolve, reject) => {
      const dotnetPath = process.env.DOTNET_PATH || 'dotnet';
      this.process = spawn(dotnetPath, ['run', '--project', this.serverPath], {
        cwd: path.dirname(this.serverPath),
        stdio: ['pipe', 'pipe', 'pipe']
      });

      this.process.stdout.on('data', (data) => this._handleData(data));
      this.process.stderr.on('data', (data) => {
        console.error('[McpServer]', data.toString());
      });
      this.process.on('error', reject);
      this.process.on('exit', (code) => {
        if (code !== 0 && code !== null) {
          reject(new Error(`McpServer exited with code ${code}`));
        }
      });

      setTimeout(() => resolve(), 500); // 서버 시작 대기
    });
  }

  _handleData(data) {
    this.buffer += data.toString();
    const lines = this.buffer.split('\n');
    this.buffer = lines.pop() || ''; // 마지막 불완전한 라인은 다음 호출까지 유지

    for (const line of lines) {
      if (line.trim()) {
        try {
          const message = JSON.parse(line);
          this._processMessage(message);
        } catch (e) {
          console.error('Failed to parse message:', line, e.message);
        }
      }
    }
  }

  _processMessage(message) {
    if (message.id && this.pendingRequests.has(message.id)) {
      const { resolve, reject } = this.pendingRequests.get(message.id);
      this.pendingRequests.delete(message.id);

      if (message.error) {
        reject(new Error(message.error.message || JSON.stringify(message.error)));
      } else {
        resolve(message.result);
      }
    }
  }

  async call(toolName, args) {
    if (!this.process) {
      throw new Error('Client not started. Call start() first.');
    }

    const id = ++this.messageId;
    const request = {
      jsonrpc: '2.0',
      id,
      method: 'tools/call',
      params: {
        name: toolName,
        arguments: args
      }
    };

    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pendingRequests.delete(id);
        reject(new Error(`Tool call timeout: ${toolName}`));
      }, 30000);

      this.pendingRequests.set(id, {
        resolve: (result) => {
          clearTimeout(timeout);
          resolve(result);
        },
        reject: (error) => {
          clearTimeout(timeout);
          reject(error);
        }
      });

      this.process.stdin.write(JSON.stringify(request) + '\n');
    });
  }

  close() {
    return new Promise((resolve) => {
      if (this.process) {
        this.process.stdin.end();
        this.process.on('exit', resolve);
        setTimeout(resolve, 5000);
      } else {
        resolve();
      }
    });
  }
}

module.exports = McpClient;
