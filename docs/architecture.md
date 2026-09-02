# DoorPulse Architecture

## Event flow

DoorPulse uses a push-first design:

```text
Ring motion / doorbell
        |
        v
Ring push notification
        |
        v
DoorPulse recorder
        |
        +--> Live View
        +--> FFmpeg MP4
        +--> JPG thumbnail
        |
        +--> Local storage
        +--> Remote storage
```

A periodic Ring event-history poll remains enabled as a backup in case a push event is missed.

## Main Windows application

The WPF application provides:

- Dashboard
- Videos
- Settings
- Activity Logs
- Diagnostics
- Setup Wizard

## Background recorder

DoorPulse runs its recorder in background agent mode:

```text
DoorPulse.exe --agent
```

A Windows Scheduled Task starts it automatically.

## Automatic recovery

DoorPulse configures restart settings and creates a watchdog task. The watchdog checks whether the recorder task is running and attempts recovery if it is stopped.

## Ring integration

The Ring integration is implemented by Node.js helper/engine scripts under `Engine/`.

Camera discovery and push registration are intentionally separated so camera discovery does not accidentally consume the persistent push-registration bootstrap.

## Storage

DoorPulse supports:

- Cloud
- Local
- Both

Cloud storage currently uses FTP. Future versions may add FTPS/SFTP or provider-specific storage backends.
