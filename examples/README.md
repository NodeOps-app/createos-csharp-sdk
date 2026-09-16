# C# SDK examples

These projects demonstrate the same API surfaces as the Go SDK examples. Each
project is independently runnable and references the SDK source project by
default, so no published package is required for local development.

Set `CREATEOS_SANDBOX_API_KEY` in your shell or secret manager, then run an
example from the repository root:

```bash
dotnet run --project examples/HelloWorld/HelloWorld.csproj
```

After a GitHub Release publishes a NuGet package, configure the authenticated
NodeOps-app package source as described in the [main README](../README.md#your-first-sandbox).
Set `CreateOSPackageVersion` to use the published package instead of the local
project:

```bash
NuGetPackageSourceCredentials_createos="Username=$GITHUB_USER;Password=$GITHUB_PACKAGES_TOKEN" \
  CreateOSPackageVersion=0.1.0 \
  dotnet run --project examples/HelloWorld/HelloWorld.csproj
```

Replace `0.1.0` with the release version you want to test. This switch applies
to every example project, including `ExecutionServer`; unset it to return to
the local project reference. No GitHub token or CreateOS API key belongs in
the repository.

| Example | What it demonstrates |
| --- | --- |
| [HelloWorld](HelloWorld/Program.cs) | Create a sandbox, run a command, destroy it |
| [CommandStreaming](CommandStreaming/Program.cs) | Read command output as it arrives |
| [FilesAndSnapshots](FilesAndSnapshots/Program.cs) | Upload/download files and snapshot a sandbox |
| [IngressPreview](IngressPreview/Program.cs) | Expose a service through an ingress preview URL |
| [Network](Network/Program.cs) | Connect sandboxes on a private network |
| [CustomTemplate](CustomTemplate/Program.cs) | Build and use a custom template |
| [ManagedProcess](ManagedProcess/Program.cs) | Manage a long-running process |
| [Desktop](Desktop/Program.cs) | Desktop automation and noVNC |
| [ExecutionServer](ExecutionServer/README.md) | HTTP service that executes requests in fresh sandboxes |
| [Readme](Readme/Program.cs) | Several README flows in one program |

All examples make live API calls and may create billable resources. They clean
up resources in `finally` blocks, but check your account for leftovers after
an interrupted run.
