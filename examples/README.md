# C# SDK examples

These projects demonstrate the same API surfaces as the Go SDK examples. Each
project is independently runnable and references the SDK source project by
default, so no published package is required for local development.

Set `CREATEOS_API_KEY` in your shell or secret manager, then run an
example from the repository root:

```bash
dotnet run --project examples/HelloWorld/HelloWorld.csproj
```

To use a published package from nuget.org instead of the local project, set
`CreateOSPackageVersion` to the release you want to test:

```bash
CreateOSPackageVersion=YOUR_VERSION dotnet run --project examples/HelloWorld/HelloWorld.csproj
```

This switch applies to every example project, including `ExecutionServer`; unset it to return to
the local project reference. No CreateOS API key belongs in the repository.

| Example | What it demonstrates |
| --- | --- |
| [HelloWorld](HelloWorld/README.md) | Create a sandbox, run a command, destroy it |
| [CommandStreaming](CommandStreaming/README.md) | Read command output as it arrives |
| [FilesAndSnapshots](FilesAndSnapshots/README.md) | Upload/download files and snapshot a sandbox |
| [IngressPreview](IngressPreview/README.md) | Expose a service through an ingress preview URL |
| [Network](Network/README.md) | Connect sandboxes on a private network |
| [CustomTemplate](CustomTemplate/README.md) | Build and use a custom template |
| [ManagedProcess](ManagedProcess/README.md) | Manage a long-running process |
| [Desktop](Desktop/README.md) | Desktop automation and noVNC |
| [ExecutionServer](ExecutionServer/README.md) | HTTP service that executes requests in fresh sandboxes |
| [Readme](Readme/README.md) | Several README flows in one program |

All examples make live API calls and may create billable resources. They clean
up resources in `finally` blocks, but check your account for leftovers after
an interrupted run.
