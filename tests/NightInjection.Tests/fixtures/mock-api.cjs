// Test fixture only. RFC 8032 test vector seed; this is not a production signing key.
const crypto = require('node:crypto');
const fs = require('node:fs/promises');
const path = require('node:path');
const website = path.resolve(__dirname, '../../../../website/web-port/backend');
const { createApp, createRuntimeConfig } = require(path.join(website, 'server.js'));
const { createPackageSigner } = require(path.join(website, 'package-signing.js'));
const key = crypto.createPrivateKey({ format: 'der', type: 'pkcs8', key: Buffer.from(
  '302e020100300506032b6570042204209d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60', 'hex') });
const server = createApp({
  runtimeConfig: createRuntimeConfig({ production: false, publicOrigin: 'https://night.test',
    allowedHosts: ['127.0.0.1'], proxyMode: 'off' }),
  packageSigner: createPackageSigner(key.export({ format: 'pem', type: 'pkcs8' })),
  async generate({ appId, outputDir }) {
    if (appId === '220') return { ok: false, code: 'NOT_FOUND' };
    const folder = path.join(outputDir, 'config', 'lua'); await fs.mkdir(folder, { recursive: true });
    await fs.writeFile(path.join(folder, `${appId}.lua`), `addappid(${appId})\n-- mock fixture\n`);
    return { ok: true };
  }, logInfo() {}, logError() {},
});
server.listen(0, '127.0.0.1', () => console.log(server.address().port));
