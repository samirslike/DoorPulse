# DoorPulse for Linux

Linux version of DoorPulse, the open-source Ring camera recorder and video manager.

> **Status:** Preview / active development.  
> The Windows release is the current stable public release. Linux recording,
> push notifications, local/cloud storage, and the Avalonia GUI have been
> successfully tested under Ubuntu in WSL2.

## Current Linux features

- Ring authentication + 2FA
- Camera discovery
- Ring push bootstrap
- Push-first motion and doorbell recording
- Backup event polling
- FFmpeg MP4 recording and JPG thumbnails
- 15 / 25 / 45 second recording presets
- Local / Cloud / Both storage
- FTP cloud upload with retry behavior
- Avalonia desktop GUI
- Dashboard
- Videos library
- Settings
- Activity Logs
- Diagnostics
- systemd automatic recorder restart
- WSL2/WSLg development and test support

## Requirements

- Ubuntu / Debian Linux
- Node.js 20+
- npm
- FFmpeg
- curl
- .NET 10 SDK for the GUI

The Linux code uses the current user's home directory dynamically.

Default application data:

```text
~/.local/share/DoorPulse/
```

Default recordings:

```text
~/Videos/DoorPulse/
```

The recording directory can also be changed from the GUI.

## First-time core setup

```bash
chmod +x *.sh
./setup.sh
./auth.sh
./cameras.sh
./push-bootstrap.sh
```

After authentication and camera discovery, run the recorder in the foreground:

```bash
./run.sh
```

Trigger motion or press the Ring doorbell. A successful event should show:

```text
PUSH RECEIVED
Trigger source: PUSH
RECORDING VERIFIED
THUMBNAIL CREATED
```

## Linux GUI

Install/build:

```bash
chmod +x gui-setup.sh gui-run.sh
./gui-setup.sh
```

Run:

```bash
./gui-run.sh
```

Under WSL2 with WSLg, the Linux GUI can appear directly on the Windows desktop.

## Background service

After foreground testing succeeds:

```bash
./install-service.sh
```

Status:

```bash
systemctl --user status doorpulse-recorder
```

Live logs:

```bash
journalctl --user -u doorpulse-recorder -f
```

The service uses systemd automatic restart for recovery.

## Storage modes

### Local

Keeps MP4 recordings and JPG thumbnails on the Linux computer.

### Cloud

Uploads recordings and thumbnails by FTP. Local temporary files are retained
when an upload fails so they can be retried.

### Both

Keeps the local copy and uploads a cloud copy.

Cloud settings are available in the GUI:

- FTP host
- FTP username
- FTP remote path
- FTP password
- optional viewer URL
- Test Connection
- Open Viewer

## Security

Never commit or share:

```text
~/.local/share/DoorPulse/refresh-token.txt
~/.local/share/DoorPulse/ftp-password.txt
~/.local/share/DoorPulse/config.json
```

Do not commit recordings, API credentials, signing keys, or private server details.

The Ring password is used during authentication and is not saved by DoorPulse.

## Linux roadmap

- Continue native Linux testing outside WSL
- Improve GUI parity with Windows
- Multi-camera validation
- Long-running reliability testing
- Native Linux video playback improvements
- `.deb` installer/package
- Release signing and packaging review
