import { RingApi } from 'ring-client-api';
import { readFileSync, writeFileSync } from 'node:fs';

const tokenFile = process.argv[2];

if (!tokenFile) {
  console.error('Token file path is required.');
  process.exit(1);
}

const refreshToken = readFileSync(tokenFile, 'utf8').trim();

const api = new RingApi({
  refreshToken,
  debug: false
});

// Authentication itself can rotate the token. Persist those rotations.
// IMPORTANT: this helper intentionally does NOT call getCameras() or
// getLocations(), because those methods initialize Ring push notifications.
// Push initialization must happen only once, inside the long-running recorder.
api.onRefreshTokenUpdated.subscribe(({ newRefreshToken }) => {
  try {
    writeFileSync(tokenFile, newRefreshToken);
  } catch {}
});

try {
  const devices = await api.fetchRingDevices();

  for (const camera of devices.allCameras) {
    console.log(JSON.stringify({
      type: 'camera',
      id: String(camera.id),
      name: camera.description || camera.name || `Camera ${camera.id}`
    }));
  }

  // Give any normal auth token rotation a brief chance to flush.
  await new Promise(resolve => setTimeout(resolve, 500));

  api.disconnect();
  process.exit(0);
} catch (error) {
  console.error(error?.stack || error?.message || String(error));
  try { api.disconnect(); } catch {}
  process.exit(1);
}
