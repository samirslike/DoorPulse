# Release Build Notes

The public GitHub repository is source-first.

The development customer installer has been built as a self-contained Windows executable that can bundle:

- .NET runtime
- Node.js runtime
- FFmpeg
- tested Ring runtime
- DoorPulse engine

Those third-party binaries are deliberately excluded from the public source repository.

Before publishing a GitHub Release containing `DoorPulseSetup.exe`:

1. Verify the exact third-party versions.
2. Review every applicable license.
3. Include required copyright/license notices.
4. Confirm no credentials or customer configuration are embedded.
5. Code-sign the installer if possible.
6. Test on a clean Windows PC.
7. Verify Ring push.
8. Verify recording.
9. Verify Local, Cloud, and Both storage.
10. Verify automatic recovery.
