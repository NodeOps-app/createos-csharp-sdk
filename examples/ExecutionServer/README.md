# Execution server

This example exposes `POST /v1/execute`. Every request creates a fresh sandbox,
runs one command without shell interpolation, returns its output, and destroys
the sandbox.
See [Program.cs](Program.cs) for the code.

```bash
CREATEOS_API_KEY=your-api-key \
  dotnet run --project examples/ExecutionServer/ExecutionServer.csproj
```

```bash
curl --fail-with-body http://127.0.0.1:8080/v1/execute \
  --header 'Content-Type: application/json' \
  --data '{"command":"python3","arguments":["-c","print(sum(range(10)))"]}'
```

Kestrel limits request bodies to 1 MiB, including chunked requests, and the
server allows four concurrent executions. It intentionally binds to localhost.
Add authentication, authorization, rate limiting, audit logging, and workload policy before
exposing a similar service to a network.
