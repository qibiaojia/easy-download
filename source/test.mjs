import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs/promises';
import crypto from 'node:crypto';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';

const exec = promisify(execFile);
const source = path.dirname(fileURLToPath(import.meta.url));
const app = String.raw`C:\Users\yuzihang\Documents\Codex\Apps\轻松下载`;
const work = path.resolve(source, '../work/easy-download');
const runner = path.join(work, 'test-bin/SmokeTests.exe');
const engine = path.join(app, 'engine/aria2c.exe');
const output = path.join(work, 'test-downloads', new Date().toISOString().replaceAll(':', '-'));
await fs.mkdir(output, {recursive: true});
// Nonrepeating content detects misplaced or duplicated blocks after parallel downloads.
const payload = crypto.randomBytes(8 * 1024 * 1024 + 357);
const digest = data => crypto.createHash('sha256').update(data).digest('hex');
const expectedHash = digest(payload);
const requests = [];
const sockets = new Set();
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  requests.push({route: url.pathname, range: req.headers.range ?? null});
  if (url.pathname === '/redirect') {res.writeHead(302, {Location: '/unicode'}).end(); return;}
  if (url.pathname === '/missing') {res.writeHead(404).end('Not found'); return;}
  const noRange = url.pathname === '/no-range' || url.pathname === '/chunked';
  let start = 0, end = payload.length - 1;
  if (req.headers.range && !noRange) {
    const match = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range);
    if (!match) {res.writeHead(416).end(); return;}
    start = +match[1];
    if (match[2]) end = Math.min(+match[2], end);
    if (start > end) {res.writeHead(416, {'Content-Range': `bytes */${payload.length}`}).end(); return;}
    res.statusCode = 206;
    res.setHeader('Content-Range', `bytes ${start}-${end}/${payload.length}`);
  }
  if (url.pathname === '/unicode') res.setHeader('Content-Disposition', "attachment; filename=download.zip; filename*=UTF-8''%E6%B5%8B%E8%AF%95%20%E8%B5%84%E6%96%99.zip");
  if (url.pathname === '/ascii') res.setHeader('Content-Disposition', 'attachment; filename="tool-package.exe"');
  if (url.pathname !== '/chunked') res.setHeader('Content-Length', end - start + 1);
  res.setHeader('Content-Type', 'application/octet-stream');
  res.setHeader('Accept-Ranges', noRange ? 'none' : 'bytes');
  if (req.method === 'HEAD') {res.end(); return;}
  const slow = url.pathname === '/slow';
  const timer = setInterval(() => {
    if (res.destroyed) {clearInterval(timer); return;}
    const next = Math.min(start + 96 * 1024, end + 1);
    res.write(payload.subarray(start, next)); start = next;
    if (start > end) {clearInterval(timer); res.end();}
  }, slow ? 50 : 8);
  res.on('close', () => clearInterval(timer));
});
server.on('connection', s => {sockets.add(s); s.on('close', () => sockets.delete(s));});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;
const results = [];
async function run(testName, endpoint, manual = '', cancelMs = 0, code = 0) {
  const dir = path.join(output, testName);
  await fs.mkdir(dir, {recursive: true});
  let outcome;
  try {outcome = await exec(runner, [engine, endpoint.startsWith('/') ? base + endpoint : endpoint, dir, manual, String(cancelMs)], {timeout: 30000, encoding: 'utf8', windowsHide: true});}
  catch (error) {if (error.code !== code) throw error; outcome = error;}
  const data = JSON.parse(outcome.stdout.trim());
  const names = await fs.readdir(dir);
  assert(!names.some(n => n.startsWith('.轻松下载-')), 'Temporary directory was not cleaned');
  if (code === 0) {
    assert.equal(data.ok, true);
    assert.equal(digest(await fs.readFile(data.path)), expectedHash, 'Downloaded content differs');
    assert.equal(data.bytes, payload.length);
  }
  return {data, dir, names};
}
async function test(name, body) {
  if (process.env.TEST_MATCH && !name.includes(process.env.TEST_MATCH)) return;
  const began = Date.now();
  await body();
  results.push({test: name, status: 'PASS', milliseconds: Date.now() - began});
  console.log('PASS ' + name);
}
try {
  await test('Parallel Range download preserves byte order and content', async () => {
    const offset = requests.length;
    const r = await run('parallel', '/file.zip');
    assert.equal(path.basename(r.data.path), 'file.zip');
    assert(requests.slice(offset).filter(q => q.range && !q.range.startsWith('bytes=0-')).length >= 2, 'Multiple nonzero Range requests expected');
  });
  await test('Redirect and UTF-8 filename* retain Chinese name and extension', async () => {
    const r = await run('unicode', '/redirect');
    assert.equal(path.basename(r.data.path), '测试 资料.zip');
  });
  await test('Content-Disposition overrides URL path', async () => {
    const r = await run('header', '/ascii');
    assert.equal(path.basename(r.data.path), 'tool-package.exe');
  });
  await test('Manual file name takes priority', async () => {
    const r = await run('manual', '/ascii', '我的安装包.zip');
    assert.equal(path.basename(r.data.path), '我的安装包.zip');
  });
  await test('Server without Range completes without corruption', async () => {await run('no-range', '/no-range', '完整文件.bin');});
  await test('Unknown length chunked response completes', async () => {await run('chunked', '/chunked', '未知大小.bin');});
  await test('Query filename repairs extensionless download name', async () => {
    const r = await run('query', '/uuid?filename=%E5%8E%8B%E7%BC%A9%E5%8C%85.zip');
    assert.equal(path.basename(r.data.path), '压缩包.zip');
  });
  await test('Existing file is preserved and new file is numbered', async () => {
    const dir = path.join(output, 'collision'); await fs.mkdir(dir, {recursive: true});
    await fs.writeFile(path.join(dir, 'file.zip'), 'KEEP EXISTING FILE');
    const r = await run('collision', '/file.zip');
    assert.equal(path.basename(r.data.path), 'file (1).zip');
    assert.equal(await fs.readFile(path.join(dir, 'file.zip'), 'utf8'), 'KEEP EXISTING FILE');
  });
  await test('Cancellation stops download and cleans temporary data', async () => {
    const r = await run('cancel', '/slow', '', 900, 2);
    assert.equal(r.data.canceled, true); assert.deepEqual(r.names, []);
  });
  await test('404 produces a readable error and no final file', async () => {
    const r = await run('missing', '/missing', '', 0, 1);
    assert(r.data.error.includes('找不到'), JSON.stringify(r.data)); assert.deepEqual(r.names, []);
  });
  await test('Reject non-HTTP protocols', async () => {
    const r = await run('protocol', 'file:///C:/Windows/win.ini', '', 0, 1);
    assert.equal(r.data.type, 'ArgumentException'); assert.deepEqual(r.names, []);
  });
  await test('Reject traversal and Windows reserved names', async () => {
    for (const [i, name] of ['../escape.zip', 'NUL.txt', 'bad.exe.', 'COM1.zip', 'bad\nname.zip'].entries()) {
      const r = await run('invalid-' + i, '/file.zip', name, 0, 1);
      assert.equal(r.data.type, 'ArgumentException'); assert.deepEqual(r.names, []);
    }
  });
  if (process.env.TEST_GUI === '1') {
    await test('GUI downloads using actual input fields and buttons', async () => {
      const dir = path.join(output, 'gui'); await fs.mkdir(dir, {recursive: true});
      const r = await exec(path.join(work, 'test-bin/GuiSmoke.exe'), [path.join(app, '轻松下载.exe'), base + '/file.zip', dir, path.join(work, 'gui-complete.png')], {timeout: 35000, encoding: 'utf8', windowsHide: true});
      assert(r.stdout.includes('PASS GUI'));
      assert.equal(digest(await fs.readFile(path.join(dir, '界面测试.zip'))), expectedHash);
      assert.deepEqual(await fs.readdir(dir), ['界面测试.zip']);
    });
    await test('GUI cancel button stops download and clears temporary files', async () => {
      const dir = path.join(output, 'gui-cancel'); await fs.mkdir(dir, {recursive: true});
      const r = await exec(path.join(work, 'test-bin/GuiSmoke.exe'), [path.join(app, '轻松下载.exe'), base + '/slow', dir, path.join(work, 'gui-canceled.png'), 'cancel'], {timeout: 35000, encoding: 'utf8', windowsHide: true});
      assert(r.stdout.includes('PASS GUI cancellation'));
      assert.deepEqual(await fs.readdir(dir), []);
    });
  }
} finally {
  for (const s of sockets) s.destroy();
  await new Promise(resolve => server.close(resolve));
  await fs.writeFile(path.join(work, 'test-results.json'), JSON.stringify({asOf: new Date().toISOString(), payloadBytes: payload.length, expectedHash, results, requests, output}, null, 2));
}
console.log(JSON.stringify({passed: results.length, output}, null, 2));
