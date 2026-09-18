# Changelog

## Unreleased

- Redact plaintext sandbox access tokens from response string output to prevent accidental disclosure in logs.
- Set the next NuGet version and default user agent to `0.1.3`.
- Added delegated sandbox access token lifecycle methods and a separate token scoped sandbox handle.

- Enforce the execution example's 1 MiB request-body limit for chunked requests.
- Allow longer sandbox creation from custom templates in the example.
- Prepare public nuget.org distribution with token-free installation and OIDC-based release publishing.

## [0.1.1](https://github.com/NodeOps-app/createos-csharp-sdk/releases/tag/v0.1.1) — 2026-09-16

- Changed the environment fallback for API-key authentication to `CREATEOS_API_KEY` only. Explicit `SandboxClientOptions.ApiKey` still takes precedence.
- Updated the package README to show installation from the published GitHub Packages feed and refreshed the example instructions.

## [0.1.0](https://github.com/NodeOps-app/createos-csharp-sdk/releases/tag/v0.1.0) — 2026-09-16

- Initial .NET 8 SDK release with asynchronous sandbox lifecycle, command execution and streaming, file transfer, networking, templates, disks, managed processes, and desktop automation.
- Added runnable examples, automated tests, CI validation, and GitHub Packages publishing on release.
