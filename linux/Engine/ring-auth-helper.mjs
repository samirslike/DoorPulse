import { RingRestClient } from 'ring-client-api/rest-client';
import readline from 'node:readline';

const rl = readline.createInterface({
  input: process.stdin,
  crlfDelay: Infinity
});

const iterator = rl[Symbol.asyncIterator]();

function send(value) {
  process.stdout.write(JSON.stringify(value) + '\n');
}

async function readJson() {
  const { value, done } = await iterator.next();
  if (done) return null;
  return JSON.parse(value);
}

const first = await readJson();

if (!first?.email || !first?.password) {
  send({ type: 'error', message: 'Ring email and password are required.' });
  process.exit(1);
}

const client = new RingRestClient({
  email: first.email,
  password: first.password,
  controlCenterDisplayName: 'DoorPulse'
});

async function finish(auth) {
  send({
    type: 'success',
    message: 'Ring account connected.',
    token: auth.refresh_token
  });
  process.exit(0);
}

try {
  const auth = await client.getCurrentAuth();
  await finish(auth);
} catch (error) {
  if (!client.promptFor2fa) {
    send({
      type: 'error',
      message: error?.message || 'Ring login failed.'
    });
    process.exit(1);
  }

  send({
    type: 'need2fa',
    message: client.promptFor2fa
  });
}

while (true) {
  const next = await readJson();

  if (!next?.code) {
    send({ type: 'error', message: 'Verification code is required.' });
    process.exit(1);
  }

  try {
    const auth = await client.getAuth(String(next.code).trim());
    await finish(auth);
  } catch (error) {
    if (client.promptFor2fa) {
      send({
        type: 'need2fa',
        message: client.promptFor2fa
      });
      continue;
    }

    send({
      type: 'error',
      message: error?.message || 'Ring verification failed.'
    });
    process.exit(1);
  }
}
