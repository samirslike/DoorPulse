import { RingApi } from 'ring-client-api';
import { RingRestClient } from 'ring-client-api/rest-client';
import {
  readFileSync,
  writeFileSync,
  existsSync
} from 'node:fs';

const tokenFile = process.argv[2];
const timeoutSeconds = Number(process.argv[3] || 25);

function emit(state, message, extra = {}) {
  console.log(JSON.stringify({
    type: 'pushBootstrap',
    state,
    message,
    ...extra
  }));
}

function tokenHasPushCredentials() {
  try {
    if (!existsSync(tokenFile)) return false;

    const token = readFileSync(tokenFile, 'utf8').trim();
    if (!token) return false;

    const client = new RingRestClient({
      refreshToken: token
    });

    const credentials =
      client._internalOnly_pushNotificationCredentials;

    return !!credentials?.config &&
           !!credentials?.fcm?.token;
  } catch {
    return false;
  }
}

if (!tokenFile || !existsSync(tokenFile)) {
  emit('failed', 'Ring token was not found.');
  process.exit(2);
}

if (tokenHasPushCredentials()) {
  emit('ready', 'Ring Push Service is already registered.');
  process.exit(0);
}

const refreshToken =
  readFileSync(tokenFile, 'utf8').trim();

if (!refreshToken) {
  emit('failed', 'Ring token file is empty.');
  process.exit(2);
}

emit('starting', 'Registering this PC for instant Ring notifications...');

const api = new RingApi({
  refreshToken,
  debug: false
});

let tokenUpdates = 0;

api.onRefreshTokenUpdated.subscribe(
  ({ newRefreshToken }) => {
    try {
      writeFileSync(tokenFile, newRefreshToken);
      tokenUpdates++;

      emit(
        'progress',
        'Ring registration credentials updated and saved.',
        { tokenUpdates }
      );
    } catch (error) {
      emit(
        'progress',
        `Could not save a Ring token update: ${error?.message || error}`
      );
    }
  }
);

try {
  // IMPORTANT:
  // Unlike the camera-discovery helper, this call is deliberate.
  // getCameras() initializes the persistent Ring push receiver.
  const cameras = await api.getCameras();

  emit(
    'progress',
    `Ring Push Service started for ${cameras.length} camera(s).`
  );

  const deadline =
    Date.now() + Math.max(10, timeoutSeconds) * 1000;

  while (Date.now() < deadline) {
    if (tokenHasPushCredentials()) {
      emit(
        'ready',
        'Ring Push Service is ready for instant motion and doorbell events.',
        {
          cameras: cameras.length,
          tokenUpdates
        }
      );

      try { api.disconnect(); } catch {}
      process.exit(0);
    }

    await new Promise(resolve => setTimeout(resolve, 500));
  }

  emit(
    'failed',
    'Ring Push registration did not finish in time. DoorPulse can retry.',
    {
      cameras: cameras.length,
      tokenUpdates
    }
  );

  try { api.disconnect(); } catch {}
  process.exit(3);

} catch (error) {
  emit(
    'failed',
    error?.message || String(error)
  );

  try { api.disconnect(); } catch {}
  process.exit(4);
}
