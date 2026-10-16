import { createLanServer } from './lan-server.mjs';

const value = process.env.TRON_PUBLIC_ORIGIN;
if (!value) throw new Error("Set TRON_PUBLIC_ORIGIN to this server\'s public HTTPS URL.");
let origin;
try { origin = new URL(value); }
catch { throw new Error('TRON_PUBLIC_ORIGIN must be a valid URL.'); }
if ((origin.protocol !== 'https:' &&
    !(origin.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(origin.hostname))) ||
    origin.pathname !== '/' || origin.search || origin.hash)
  throw new Error('TRON_PUBLIC_ORIGIN must be a public HTTPS origin without a path.');
process.env.TRON_PUBLIC_ORIGIN = origin.origin;
const port = Number(process.env.PORT || 8000);
if (!Number.isInteger(port) || port < 1 || port > 65535)
  throw new Error('PORT must be 1 to 65535.');
const server = createLanServer();
server.listen(port, '0.0.0.0', () => {
  console.log('TRON online game server listening on port ' + port + '.');
});
