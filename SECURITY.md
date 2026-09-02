# Security

Do not post or commit:

- Ring passwords
- Ring two-factor authentication codes
- Ring refresh tokens
- FTP passwords
- API keys
- private signing keys/certificates
- customer recordings

Sensitive DoorPulse runtime files normally live under:

```text
C:\ProgramData\DoorPulse\
```

Typical sensitive files include:

```text
refresh-token.txt
ftp-password.txt
config.json
```

If a secret is accidentally committed, revoke or rotate it immediately.
