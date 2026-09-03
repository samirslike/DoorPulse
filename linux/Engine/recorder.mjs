import {
    RingApi,
    PushNotificationAction
} from 'ring-client-api';

import {
    readFileSync,
    writeFileSync,
    existsSync,
    mkdirSync,
    readdirSync,
    statSync,
    unlinkSync,
    rmdirSync
} from 'node:fs';

import path from 'node:path';
import { homedir } from 'node:os';
import { spawn } from 'node:child_process';

const XDG_DATA_HOME =
    process.env.XDG_DATA_HOME ||
    path.join(homedir(), '.local', 'share');

const DOORPULSE_HOME =
    process.env.DOORPULSE_HOME ||
    path.join(XDG_DATA_HOME, 'DoorPulse');

const CONFIG_FILE =
    process.env.DOORPULSE_CONFIG ||
    path.join(DOORPULSE_HOME, 'config.json');

const TOKEN_FILE =
    process.env.DOORPULSE_TOKEN_FILE ||
    path.join(DOORPULSE_HOME, 'refresh-token.txt');

const FTP_PASSWORD_FILE =
    process.env.DOORPULSE_FTP_PASSWORD_FILE ||
    path.join(DOORPULSE_HOME, 'ftp-password.txt');

const config = JSON.parse(
    readFileSync(CONFIG_FILE, 'utf8')
);

const STORAGE_MODE =
    (config.storageMode || 'cloud').toLowerCase();

const RECORDING_DIR =
    config.recordingDirectory || path.join(homedir(), 'Videos', 'DoorPulse');

const FFMPEG_EXE =
    config.ffmpegPath || 'ffmpeg';

const CURL_EXE =
    config.curlPath || 'curl';

const LEGACY_CAMERA_NAME =
    (config.cameraName || '').trim();

const LEGACY_CAMERA_ID =
    String(config.cameraId || '').trim();

const CONFIGURED_CAMERAS =
    Array.isArray(config.cameras)
        ? config.cameras
            .filter(c => c && c.enabled !== false)
            .map(c => ({
                id: String(c.id || '').trim(),
                name: String(c.name || '').trim(),
                monitorMotion: c.monitorMotion !== false,
                monitorDoorbell: c.monitorDoorbell !== false
            }))
        : [];

const RECORDING_PRESET =
    (config.recordingPreset || 'normal').toLowerCase();

const RECORDING_PRESETS = {
    short: 15,
    normal: 25,
    long: 45
};

const RECORD_SECONDS =
    RECORDING_PRESETS[RECORDING_PRESET] ??
    RECORDING_PRESETS.normal;

const BACKUP_POLL_SECONDS =
    Number(config.backupPollSeconds || 60);

const COOLDOWN_SECONDS =
    Number(config.cooldownSeconds || 15);

const RETENTION_HOURS =
    Number(config.retentionHours || 24);

const THUMBNAIL_SECONDS =
    Number(config.thumbnailSecond || 1);

const FTP_HOST =
    (config.ftpHost || '').trim();

const FTP_USERNAME =
    (config.ftpUsername || '').trim();

const FTP_REMOTE =
    (config.ftpRemotePath || '').trim();

const FTP_PASSWORD =
    existsSync(FTP_PASSWORD_FILE)
        ? readFileSync(FTP_PASSWORD_FILE, 'utf8').trim()
        : '';

const MAX_RECORD_ATTEMPTS = 2;
const RETRY_DELAY_SECONDS = 5;
const MIN_VALID_MP4_BYTES = 100000;
const MIN_VALID_JPG_BYTES = 1000;
const DUPLICATE_EVENT_WINDOW_SECONDS = 45;
const FTP_RETRY_SECONDS = 5 * 60;
const MAX_SEEN_EVENTS = 1000;

const CLOUD_LEDGER_FILE =
    path.join(path.dirname(CONFIG_FILE), 'cloud-upload-ledger.json');

function loadCloudLedger() {
    try {
        if (!existsSync(CLOUD_LEDGER_FILE)) return {};

        const parsed = JSON.parse(
            readFileSync(CLOUD_LEDGER_FILE, 'utf8')
        );

        return parsed && typeof parsed === 'object'
            ? parsed
            : {};
    } catch {
        return {};
    }
}

const cloudLedger = loadCloudLedger();

function saveCloudLedger() {
    try {
        const entries = Object.entries(cloudLedger);

        // Keep the ledger bounded.
        if (entries.length > 10000) {
            const trimmed = entries.slice(entries.length - 10000);

            for (const key of Object.keys(cloudLedger)) {
                delete cloudLedger[key];
            }

            for (const [key, value] of trimmed) {
                cloudLedger[key] = value;
            }
        }

        writeFileSync(
            CLOUD_LEDGER_FILE,
            JSON.stringify(cloudLedger, null, 2)
        );
    } catch (error) {
        console.error(
            `${timestamp()} Could not save cloud upload ledger:`,
            error.message ?? error
        );
    }
}

function fileFingerprint(localFile) {
    const info = statSync(localFile);

    return {
        size: info.size,
        mtimeMs: Math.round(info.mtimeMs)
    };
}

function cloudCopyAlreadySaved(localFile, relativePath) {
    try {
        const current = fileFingerprint(localFile);
        const saved = cloudLedger[relativePath];

        return !!saved &&
            saved.size === current.size &&
            saved.mtimeMs === current.mtimeMs;
    } catch {
        return false;
    }
}

function markCloudCopySaved(localFile, relativePath) {
    try {
        cloudLedger[relativePath] = {
            ...fileFingerprint(localFile),
            uploadedAt: new Date().toISOString()
        };

        saveCloudLedger();
    } catch {}
}

if (!existsSync(RECORDING_DIR)) {
    mkdirSync(RECORDING_DIR, { recursive: true });
}

function timestamp() {
    return new Date().toLocaleString();
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function safeName(name) {
    return name
        .replace(/[^a-z0-9_-]/gi, '_')
        .replace(/_+/g, '_');
}

function filenameTimestamp(date) {
    return date
        .toISOString()
        .replace(/T/, '_')
        .replace(/[:]/g, '-')
        .replace(/\.\d{3}Z$/, '');
}

function localDateFolders(date) {
    return {
        year: String(date.getFullYear()),
        month: String(date.getMonth() + 1).padStart(2, '0'),
        day: String(date.getDate()).padStart(2, '0')
    };
}

const FTP_BASE_URL =
    `ftp://${FTP_HOST}` +
    `${FTP_REMOTE.startsWith('/') ? '' : '/'}` +
    `${FTP_REMOTE.replace(/\/+$/, '')}`;

function curlConfigEscape(value) {
    return value
        .replace(/\\/g, '\\\\')
        .replace(/"/g, '\\"');
}

function uploadToHostinger(localFile, remoteRelativePath) {
    return new Promise((resolve, reject) => {
        if (!FTP_HOST || !FTP_USERNAME || !FTP_REMOTE || !FTP_PASSWORD) {
            reject(new Error('FTP settings are incomplete.'));
            return;
        }

        const remotePath = remoteRelativePath
            .split('/')
            .map(part => encodeURIComponent(part))
            .join('/');

        const remoteUrl = `${FTP_BASE_URL}/${remotePath}`;

        const args = [
            '--ftp-create-dirs',
            '--fail',
            '--silent',
            '--show-error',
            '--upload-file',
            localFile,
            '--config',
            '-',
            remoteUrl
        ];

        const child = spawn(
            CURL_EXE,
            args
        );

        let stderr = '';

        child.stderr.on('data', data => {
            stderr += data.toString();
        });

        child.on('error', reject);

        child.on('close', code => {
            if (code === 0) {
                resolve();
            } else {
                reject(
                    new Error(
                        `FTP upload failed with curl exit code ${code}: ${stderr}`
                    )
                );
            }
        });

        child.stdin.write(
            `user = "${curlConfigEscape(`${FTP_USERNAME}:${FTP_PASSWORD}`)}"\n`
        );

        child.stdin.end();
    });
}

function createThumbnail(videoFile, thumbnailFile) {
    return new Promise((resolve, reject) => {
        const child = spawn(
            FFMPEG_EXE,
            [
                '-y',
                '-loglevel', 'error',
                '-ss', String(THUMBNAIL_SECONDS),
                '-i', videoFile,
                '-frames:v', '1',
                '-vf', 'scale=640:-2',
                '-q:v', '3',
                thumbnailFile
            ]
        );

        let stderr = '';

        child.stderr.on('data', data => {
            stderr += data.toString();
        });

        child.on('error', reject);

        child.on('close', code => {
            if (code === 0) {
                resolve();
            } else {
                reject(new Error(`Thumbnail generation failed: ${stderr}`));
            }
        });
    });
}

function getEventId(event) {
    return String(
        event.ding_id_str ??
        event.id ??
        `${event.kind}-${event.created_at}`
    );
}

const seenEvents = new Set();
const recordingCameras = new Set();
const activeRecordingFiles = new Set();
const cooldownUntil = new Map();
const lastTriggeredAt = new Map();

let pollRunning = false;
let retryRunning = false;

function rememberEvent(id) {
    seenEvents.add(id);

    while (seenEvents.size > MAX_SEEN_EVENTS) {
        const oldest = seenEvents.values().next().value;
        seenEvents.delete(oldest);
    }
}

function isCameraCoolingDown(cameraId) {
    const until = cooldownUntil.get(cameraId) || 0;

    if (Date.now() >= until) {
        cooldownUntil.delete(cameraId);
        return false;
    }

    return true;
}

function cooldownSecondsRemaining(cameraId) {
    const until = cooldownUntil.get(cameraId) || 0;
    return Math.max(0, Math.ceil((until - Date.now()) / 1000));
}

function startCooldown(cameraId) {
    cooldownUntil.set(
        cameraId,
        Date.now() + COOLDOWN_SECONDS * 1000
    );

    console.log(
        `${timestamp()} Camera cooldown started for ${COOLDOWN_SECONDS} seconds.`
    );
}

function isNearRecentTrigger(cameraId, eventTimeMs) {
    const last = lastTriggeredAt.get(cameraId);

    if (!last) return false;

    return (
        Math.abs(eventTimeMs - last) / 1000
        <= DUPLICATE_EVENT_WINDOW_SECONDS
    );
}

function findPendingFiles(directory) {
    const results = [];

    if (!existsSync(directory)) return results;

    for (const item of readdirSync(directory)) {
        const fullPath = path.join(directory, item);

        try {
            const stats = statSync(fullPath);

            if (stats.isDirectory()) {
                results.push(...findPendingFiles(fullPath));
            } else if (
                stats.isFile() &&
                (
                    item.toLowerCase().endsWith('.mp4') ||
                    item.toLowerCase().endsWith('.jpg')
                )
            ) {
                results.push(fullPath);
            }
        } catch {}
    }

    return results;
}

function removeEmptyFolders(directory) {
    if (!existsSync(directory)) return;

    for (const item of readdirSync(directory)) {
        const fullPath = path.join(directory, item);

        try {
            if (statSync(fullPath).isDirectory()) {
                removeEmptyFolders(fullPath);

                if (readdirSync(fullPath).length === 0) {
                    rmdirSync(fullPath);
                }
            }
        } catch {}
    }
}

function isValidPendingFile(localFile) {
    if (!existsSync(localFile)) return false;

    const size = statSync(localFile).size;
    const ext = path.extname(localFile).toLowerCase();

    if (ext === '.mp4') return size >= MIN_VALID_MP4_BYTES;
    if (ext === '.jpg') return size >= MIN_VALID_JPG_BYTES;

    return false;
}

async function uploadAndDelete(localFile) {
    if (activeRecordingFiles.has(localFile)) return false;
    if (!isValidPendingFile(localFile)) return false;

    const relativePath = path
        .relative(RECORDING_DIR, localFile)
        .split(path.sep)
        .join('/');

    console.log(
        `${timestamp()} Uploading to Hostinger: ${relativePath}`
    );

    try {
        await uploadToHostinger(localFile, relativePath);

        console.log(
            `${timestamp()} UPLOAD SUCCESS: ${relativePath}`
        );

        unlinkSync(localFile);

        console.log(
            `${timestamp()} Local copy deleted: ${localFile}`
        );

        removeEmptyFolders(RECORDING_DIR);

        return true;

    } catch (error) {
        console.error(
            `${timestamp()} UPLOAD FAILED: ${relativePath}`
        );
        console.error(
            `${timestamp()} ${error.message}`
        );
        console.log(
            `${timestamp()} Local file kept for retry.`
        );

        return false;
    }
}

async function retryPendingUploads() {
    if (retryRunning) return;

    retryRunning = true;

    try {
        const files = findPendingFiles(RECORDING_DIR)
            .filter(file => !activeRecordingFiles.has(file));

        if (files.length === 0) return;

        console.log(
            `${timestamp()} Found ${files.length} local file(s) waiting for upload.`
        );

        for (const file of files) {
            if (!existsSync(file)) continue;

            if (!isValidPendingFile(file)) {
                try { unlinkSync(file); } catch {}
                continue;
            }

            await uploadAndDelete(file);
        }

        removeEmptyFolders(RECORDING_DIR);

    } finally {
        retryRunning = false;
    }
}

async function retryBothModeCloudCopies() {
    if (retryRunning) return;

    retryRunning = true;

    try {
        const files = findPendingFiles(RECORDING_DIR)
            .filter(file => !activeRecordingFiles.has(file))
            .filter(file => isValidPendingFile(file));

        if (files.length === 0) return;

        for (const file of files) {
            const relativePath = path
                .relative(RECORDING_DIR, file)
                .split(path.sep)
                .join('/');

            // Persistently avoid re-uploading an unchanged local file that has
            // already been copied successfully to cloud, including after reboot.
            if (cloudCopyAlreadySaved(file, relativePath))
                continue;

            try {
                console.log(`${timestamp()} Retrying cloud copy: ${relativePath}`);
                await uploadToHostinger(file, relativePath);
                markCloudCopySaved(file, relativePath);
                console.log(`${timestamp()} CLOUD COPY RETRY SUCCESS: ${relativePath}`);
            } catch (error) {
                console.error(`${timestamp()} CLOUD COPY RETRY FAILED: ${relativePath}`);
                console.error(`${timestamp()} ${error.message}`);
            }
        }
    } finally {
        retryRunning = false;
    }
}

function cleanupExpiredLocalFiles() {
    const cutoff =
        Date.now() -
        RETENTION_HOURS * 60 * 60 * 1000;

    for (const file of findPendingFiles(RECORDING_DIR)) {
        if (activeRecordingFiles.has(file)) continue;

        try {
            if (statSync(file).mtimeMs < cutoff) {
                unlinkSync(file);
                console.log(
                    `${timestamp()} Deleted expired local fallback file: ${file}`
                );
            }
        } catch {}
    }

    removeEmptyFolders(RECORDING_DIR);
}

const refreshToken =
    readFileSync(TOKEN_FILE, 'utf8').trim();

if (!refreshToken) {
    throw new Error('Ring refresh token is empty.');
}

const ringApi = new RingApi({
    refreshToken,
    ffmpegPath: FFMPEG_EXE,
    debug: false
});

ringApi.onRefreshTokenUpdated.subscribe(
    ({ newRefreshToken }) => {
        try {
            writeFileSync(
                TOKEN_FILE,
                newRefreshToken
            );

            console.log(
                `${timestamp()} Refresh token updated and saved.`
            );
        } catch (error) {
            console.error(
                `${timestamp()} Could not save refresh token:`,
                error
            );
        }
    }
);

const allCameras = await ringApi.getCameras();

function configuredSettingFor(camera) {
    const id = String(camera.id);

    let setting = CONFIGURED_CAMERAS.find(c => c.id && c.id === id);

    if (!setting) {
        setting = CONFIGURED_CAMERAS.find(
            c => c.name &&
                 c.name.toLowerCase() === camera.name.toLowerCase()
        );
    }

    return setting || null;
}

let cameras;

if (CONFIGURED_CAMERAS.length > 0) {
    cameras = allCameras.filter(camera => configuredSettingFor(camera));
} else if (LEGACY_CAMERA_ID) {
    cameras = allCameras.filter(
        camera => String(camera.id) === LEGACY_CAMERA_ID
    );
} else if (LEGACY_CAMERA_NAME) {
    cameras = allCameras.filter(
        camera => camera.name.toLowerCase() === LEGACY_CAMERA_NAME.toLowerCase()
    );
} else {
    cameras = allCameras;
}

function cameraAllowsEvent(camera, eventType) {
    const setting = configuredSettingFor(camera);

    // Legacy configuration monitors both event types.
    if (!setting) return true;

    if (eventType === 'motion')
        return setting.monitorMotion;

    if (eventType === 'doorbell')
        return setting.monitorDoorbell;

    return false;
}

console.log('');
console.log('========================================');
console.log(' DoorPulse Recorder');
console.log('========================================');
console.log(`${timestamp()} Cameras available: ${allCameras.length}`);
console.log(`${timestamp()} Cameras monitored: ${cameras.length}`);

if (cameras.length === 0) {
    if (CONFIGURED_CAMERAS.length > 0) {
        throw new Error(
            'None of the cameras selected in DoorPulse were found on this Ring account.'
        );
    }

    if (LEGACY_CAMERA_NAME || LEGACY_CAMERA_ID) {
        throw new Error(
            'The configured Ring camera was not found.'
        );
    }

    throw new Error('No Ring cameras found.');
}

for (const camera of cameras) {
    const setting = configuredSettingFor(camera);

    const modes = [];
    if (!setting || setting.monitorMotion) modes.push('motion');
    if (!setting || setting.monitorDoorbell) modes.push('doorbell');

    console.log(
        `${timestamp()} Monitoring: ${camera.name} (${camera.id}) [${modes.join(' + ')}]`
    );
}

console.log(`${timestamp()} PRIMARY trigger: TRUE PUSH`);
console.log(`${timestamp()} Backup polling: every ${BACKUP_POLL_SECONDS} seconds`);
console.log(`${timestamp()} Recording preset: ${RECORDING_PRESET} (${RECORD_SECONDS} seconds)`);
console.log(`${timestamp()} Cooldown: ${COOLDOWN_SECONDS} seconds`);
console.log(`${timestamp()} Storage mode: ${STORAGE_MODE.toUpperCase()}`);
console.log(`${timestamp()} Hostinger FTP: ${(STORAGE_MODE === 'cloud' || STORAGE_MODE === 'both') ? (FTP_HOST || 'not configured') : 'not used (local mode)'}`);
console.log('');

console.log(`${timestamp()} Loading current Ring event history...`);

for (const camera of cameras) {
    try {
        const response =
            await camera.getEvents({ limit: 30 });

        for (const event of response.events) {
            rememberEvent(getEventId(event));
        }

        console.log(
            `${timestamp()} ${camera.name}: seeded ${response.events.length} existing event(s).`
        );
    } catch (error) {
        console.error(
            `${timestamp()} Could not seed events for ${camera.name}:`,
            error
        );
    }
}

console.log('');
console.log(`${timestamp()} Checking for pending local uploads...`);
if (STORAGE_MODE === 'cloud') {
    await retryPendingUploads();
} else if (STORAGE_MODE === 'both') {
    await retryBothModeCloudCopies();
} else {
    console.log(`${timestamp()} Local storage mode: FTP upload check skipped.`);
}

console.log('');
console.log(`${timestamp()} Recorder is ready.`);
console.log(`${timestamp()} Waiting for Ring PUSH notifications...`);
console.log('');

async function recordCamera(camera, eventType, eventTime, source) {
    if (recordingCameras.has(camera.id)) {
        console.log(
            `${timestamp()} ${camera.name} is already recording. ${source} ${eventType} ignored.`
        );
        return;
    }

    if (isCameraCoolingDown(camera.id)) {
        console.log(
            `${timestamp()} ${camera.name} is in cooldown for another ` +
            `${cooldownSecondsRemaining(camera.id)} second(s). ${source} ${eventType} ignored.`
        );
        return;
    }

    lastTriggeredAt.set(
        camera.id,
        eventTime.getTime()
    );

    recordingCameras.add(camera.id);

    const folders = localDateFolders(eventTime);

    const localFolder = path.join(
        RECORDING_DIR,
        folders.year,
        folders.month,
        folders.day
    );

    mkdirSync(localFolder, { recursive: true });

    const baseFilename =
        `${safeName(camera.name)}_${eventType}_${filenameTimestamp(eventTime)}`;

    const outputFile =
        path.join(localFolder, `${baseFilename}.mp4`);

    const thumbnailFile =
        path.join(localFolder, `${baseFilename}.jpg`);

    console.log('');
    console.log('----------------------------------------');
    console.log(`${timestamp()} NEW ${eventType.toUpperCase()} EVENT`);
    console.log(`${timestamp()} Trigger source: ${source}`);
    console.log(`${timestamp()} Camera: ${camera.name}`);
    console.log(`${timestamp()} Event time: ${eventTime.toISOString()}`);
    console.log(`${timestamp()} Recording ${RECORD_SECONDS} seconds...`);

    let recordingGood = false;

    try {
        activeRecordingFiles.add(outputFile);

        for (
            let attempt = 1;
            attempt <= MAX_RECORD_ATTEMPTS;
            attempt++
        ) {
            console.log(
                `${timestamp()} Recording attempt ${attempt} of ${MAX_RECORD_ATTEMPTS}...`
            );

            try {
                if (existsSync(outputFile)) {
                    unlinkSync(outputFile);
                }

                await camera.recordToFile(
                    outputFile,
                    RECORD_SECONDS
                );

                await sleep(2000);

                if (existsSync(outputFile)) {
                    const recordedSize =
                        statSync(outputFile).size;

                    console.log(
                        `${timestamp()} Recorded MP4 size: ${recordedSize} bytes`
                    );

                    if (
                        recordedSize >= MIN_VALID_MP4_BYTES
                    ) {
                        recordingGood = true;
                        break;
                    }

                    try { unlinkSync(outputFile); } catch {}
                }

            } catch (error) {
                console.error(
                    `${timestamp()} Recording attempt ${attempt} failed:`,
                    error
                );

                if (existsSync(outputFile)) {
                    try {
                        if (
                            statSync(outputFile).size <
                            MIN_VALID_MP4_BYTES
                        ) {
                            unlinkSync(outputFile);
                        }
                    } catch {}
                }
            }

            if (attempt < MAX_RECORD_ATTEMPTS) {
                console.log(
                    `${timestamp()} Waiting ${RETRY_DELAY_SECONDS} seconds before retry...`
                );
                await sleep(RETRY_DELAY_SECONDS * 1000);
            }
        }

        activeRecordingFiles.delete(outputFile);

        if (!recordingGood) {
            console.error(
                `${timestamp()} RECORDING FAILED - no valid MP4.`
            );
            return;
        }

        console.log(
            `${timestamp()} RECORDING VERIFIED (${statSync(outputFile).size} bytes)`
        );

        try {
            if (existsSync(thumbnailFile)) {
                unlinkSync(thumbnailFile);
            }

            console.log(
                `${timestamp()} Creating JPG thumbnail...`
            );

            await createThumbnail(
                outputFile,
                thumbnailFile
            );

            if (
                existsSync(thumbnailFile) &&
                statSync(thumbnailFile).size >=
                MIN_VALID_JPG_BYTES
            ) {
                console.log(
                    `${timestamp()} THUMBNAIL CREATED (${statSync(thumbnailFile).size} bytes)`
                );
            }

        } catch (error) {
            console.error(
                `${timestamp()} Thumbnail generation failed:`,
                error.message ?? error
            );
        }

        if (STORAGE_MODE === 'cloud') {
            await uploadAndDelete(outputFile);

            if (
                existsSync(thumbnailFile) &&
                isValidPendingFile(thumbnailFile)
            ) {
                await uploadAndDelete(thumbnailFile);
            }
        } else if (STORAGE_MODE === 'both') {
            const relativeVideoPath = path
                .relative(RECORDING_DIR, outputFile)
                .split(path.sep)
                .join('/');

            console.log(
                `${timestamp()} BOTH STORAGE: uploading cloud copy and keeping local recording.`
            );

            try {
                await uploadToHostinger(outputFile, relativeVideoPath);
                markCloudCopySaved(outputFile, relativeVideoPath);
                console.log(`${timestamp()} CLOUD COPY SUCCESS: ${relativeVideoPath}`);
            } catch (error) {
                console.error(`${timestamp()} CLOUD COPY FAILED: ${relativeVideoPath}`);
                console.error(`${timestamp()} ${error.message}`);
                console.log(`${timestamp()} Local recording remains available. Cloud upload will retry.`);
            }

            if (
                existsSync(thumbnailFile) &&
                isValidPendingFile(thumbnailFile)
            ) {
                const relativeThumbPath = path
                    .relative(RECORDING_DIR, thumbnailFile)
                    .split(path.sep)
                    .join('/');

                try {
                    await uploadToHostinger(thumbnailFile, relativeThumbPath);
                    markCloudCopySaved(thumbnailFile, relativeThumbPath);
                    console.log(`${timestamp()} CLOUD THUMBNAIL SUCCESS: ${relativeThumbPath}`);
                } catch (error) {
                    console.error(`${timestamp()} CLOUD THUMBNAIL FAILED: ${relativeThumbPath}`);
                    console.error(`${timestamp()} ${error.message}`);
                }
            }

            console.log(`${timestamp()} LOCAL COPY KEPT: ${outputFile}`);
            if (existsSync(thumbnailFile)) {
                console.log(`${timestamp()} LOCAL THUMBNAIL KEPT: ${thumbnailFile}`);
            }
        } else {
            console.log(`${timestamp()} LOCAL STORAGE: recording kept at ${outputFile}`);
            if (existsSync(thumbnailFile)) {
                console.log(`${timestamp()} LOCAL STORAGE: thumbnail kept at ${thumbnailFile}`);
            }
        }

    } finally {
        activeRecordingFiles.delete(outputFile);
        recordingCameras.delete(camera.id);
        startCooldown(camera.id);
        console.log('----------------------------------------');
        console.log('');
    }
}

// PRIMARY: true Ring push
for (const camera of cameras) {
    camera.onNewNotification.subscribe(notification => {
        const action =
            notification.android_config?.category;

        let eventType = null;

        if (action === PushNotificationAction.Motion) {
            eventType = 'motion';
        } else if (action === PushNotificationAction.Ding) {
            eventType = 'doorbell';
        } else {
            return;
        }

        if (!cameraAllowsEvent(camera, eventType)) {
            console.log(
                `${timestamp()} PUSH RECEIVED: ${camera.name} / ${eventType} - disabled for this camera, ignored.`
            );
            return;
        }

        const eventTime = new Date();

        console.log(
            `${timestamp()} PUSH RECEIVED: ${camera.name} / ${eventType}`
        );

        recordCamera(
            camera,
            eventType,
            eventTime,
            'PUSH'
        ).catch(error => {
            console.error(
                `${timestamp()} Push-triggered recording error:`,
                error
            );
        });
    });
}

// BACKUP: slow history polling
async function pollRingEvents() {
    if (pollRunning) return;

    pollRunning = true;

    try {
        for (const camera of cameras) {
            let response;

            try {
                response =
                    await camera.getEvents({ limit: 30 });
            } catch (error) {
                console.error(
                    `${timestamp()} Backup event check failed for ${camera.name}:`,
                    error.message ?? error
                );
                continue;
            }

            for (const event of [...response.events].reverse()) {
                const eventId = getEventId(event);

                if (seenEvents.has(eventId)) continue;

                rememberEvent(eventId);

                if (
                    event.kind !== 'motion' &&
                    event.kind !== 'ding'
                ) {
                    continue;
                }

                const eventType =
                    event.kind === 'ding'
                        ? 'doorbell'
                        : 'motion';

                if (!cameraAllowsEvent(camera, eventType)) {
                    continue;
                }

                const eventTime =
                    new Date(event.created_at);

                if (
                    isNearRecentTrigger(
                        camera.id,
                        eventTime.getTime()
                    )
                ) {
                    console.log(
                        `${timestamp()} Backup poll found ${eventType}, ` +
                        `but it matches a recent push-triggered event. Ignored.`
                    );
                    continue;
                }

                console.log(
                    `${timestamp()} BACKUP POLL found missed event: ${camera.name} / ${eventType}`
                );

                recordCamera(
                    camera,
                    eventType,
                    eventTime,
                    'BACKUP POLL'
                ).catch(error => {
                    console.error(
                        `${timestamp()} Backup recording error:`,
                        error
                    );
                });
            }
        }

    } finally {
        pollRunning = false;
    }
}

cleanupExpiredLocalFiles();
await pollRingEvents();

setInterval(
    () => {
        pollRingEvents().catch(error => {
            console.error(
                `${timestamp()} Backup poll error:`,
                error
            );
        });
    },
    BACKUP_POLL_SECONDS * 1000
);

if (STORAGE_MODE === 'cloud') {
    setInterval(
        () => {
            retryPendingUploads().catch(error => {
                console.error(
                    `${timestamp()} Retry error:`,
                    error
                );
            });
        },
        FTP_RETRY_SECONDS * 1000
    );
} else if (STORAGE_MODE === 'both') {
    setInterval(
        () => {
            retryBothModeCloudCopies().catch(error => {
                console.error(
                    `${timestamp()} Both-mode cloud retry error:`,
                    error
                );
            });
        },
        FTP_RETRY_SECONDS * 1000
    );
}

setInterval(
    () => {
        cleanupExpiredLocalFiles();
    },
    60 * 60 * 1000
);

console.log(
    `${timestamp()} DoorPulse push-first recorder is fully running.`
);
