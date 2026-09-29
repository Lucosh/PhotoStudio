# Security policy

## Reporting a vulnerability

Please do **not** open a public issue for security problems. Use
[**Report a vulnerability**](../../security/advisories/new) (the *Security* tab of this repository)
to contact the maintainer privately. You will get an answer as soon as possible.

## How PhotoStudio handles your data

- It has no telemetry and makes no network connections, except to the AI service you configure
  (Google Gemini, Anthropic Claude or your own Ollama server), and only when you ask for an AI edit.
- API keys are encrypted with Windows DPAPI (current user) in `%APPDATA%\PhotoStudio`.
- Release files are built by GitHub Actions from the tagged source code and come with SHA-256
  checksums and signed build provenance (`gh attestation verify`).
