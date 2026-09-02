import { RingRestClient } from 'ring-client-api/rest-client';
import { readFileSync } from 'node:fs';

const tokenFile = process.argv[2];

if (!tokenFile) {
  console.log(JSON.stringify({
    type: 'pushStatus',
    ready: false,
    message: 'Token file path is missing.'
  }));
  process.exit(1);
}

try {
  const refreshToken = readFileSync(tokenFile, 'utf8').trim();

  const client = new RingRestClient({
    refreshToken
  });

  const credentials =
    client._internalOnly_pushNotificationCredentials;

  const ready =
    !!credentials?.config &&
    !!credentials?.fcm?.token;

  console.log(JSON.stringify({
    type: 'pushStatus',
    ready,
    message: ready
      ? 'Persistent Ring push credentials are saved.'
      : 'Ring push credentials have not been established yet.'
  }));

  process.exit(0);
} catch (error) {
  console.log(JSON.stringify({
    type: 'pushStatus',
    ready: false,
    message: error?.message || String(error)
  }));

  process.exit(1);
}
