# Hello world

Creates a sandbox with a 10-minute lifetime and final-minute SIGTERM warning,
prints the returned deadline, runs a command, and destroys the sandbox. See
[Program.cs](Program.cs) for the code.

From the repository root:

```sh
export CREATEOS_API_KEY="your-api-key"
dotnet run --project examples/HelloWorld/HelloWorld.csproj
```

This example makes live API calls and may create billable resources.
