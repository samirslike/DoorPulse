import { RingRestClient } from 'ring-client-api/rest-client';
import { mkdirSync, writeFileSync, chmodSync } from 'node:fs';
import path from 'node:path';
import { homedir } from 'node:os';
import readline from 'node:readline/promises';
import { stdin as input, stdout as output } from 'node:process';

const dataHome = process.env.XDG_DATA_HOME || path.join(homedir(), '.local', 'share');
const root = process.env.DOORPULSE_HOME || path.join(dataHome, 'DoorPulse');
const tokenFile = process.env.DOORPULSE_TOKEN_FILE || path.join(root, 'refresh-token.txt');
const email = (process.env.DOORPULSE_RING_EMAIL || '').trim();
const password = process.env.DOORPULSE_RING_PASSWORD || '';
delete process.env.DOORPULSE_RING_PASSWORD;

if (!email || !password) {
  console.error('Ring email/password were not supplied.');
  process.exit(2);
}

const rl = readline.createInterface({ input, output });
const client = new RingRestClient({
  email,
  password,
  controlCenterDisplayName: 'DoorPulse Linux'
});

async function save(auth) {
  mkdirSync(root, { recursive: true, mode: 0o700 });
  writeFileSync(tokenFile, auth.refresh_token, { mode: 0o600 });
  try { chmodSync(tokenFile, 0o600); } catch {}
  console.log(`Ring account connected. Token saved securely to ${tokenFile}`);
}

try {
  try {
    const auth = await client.getCurrentAuth();
    await save(auth);
  } catch (error) {
    if (!client.promptFor2fa) throw error;

    console.log(client.promptFor2fa);
    while (true) {
      const code = (await rl.question('Verification code: ')).trim();
      try {
        const auth = await client.getAuth(code);
        await save(auth);
        break;
      } catch (verifyError) {
        if (!client.promptFor2fa) throw verifyError;
        console.log(client.promptFor2fa);
      }
    }
  }
} catch (error) {
  console.error(error?.message || String(error));
  process.exitCode = 1;
} finally {
  rl.close();
}
