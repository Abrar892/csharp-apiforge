# ApiForge — CLI API Testing Tool

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-41%20passed-brightgreen.svg)]()
[![Zero runtime dependencies](https://img.shields.io/badge/dependencies-zero-success.svg)]()

A zero-dependency CLI for testing HTTP APIs the way developers actually test
them: write a collection of requests once, run it against any environment,
chain values from one response into the next, and assert on what matters —
status, JSON, headers, body, latency. Exit codes make it a CI gate.

```bash
apiforge init
apiforge validate ./bookstore.json
apiforge run ./bookstore.json --env staging --json report.json
```

## Problem statement

Hand-testing an API with curl is fine for one request. Real API work is a
sequence: log in, capture the token, create a resource, capture its id, fetch
it back, assert on the payload. Existing options are either a heavyweight GUI
(Postman), a paid cloud service, or a shell script that nobody can read a
month later.

ApiForge is the small, scriptable middle ground: collections are plain JSON
files you can keep in version control, review in a pull request, and run in
CI. No server, no account, no runtime dependencies beyond .NET 8.

> **How this differs from a health monitor:** tools like API Sentinel watch
> endpoints on a schedule and alert on SLA breaches. ApiForge is a *test
> runner*: it executes an ordered suite of requests with assertions and
> reports pass/fail, the way a unit-test runner does for code.

## Features

- **Collections as JSON** — requests, environments and assertions in one
  version-controlled file; `init` writes a working example.
- **Environments** — named variable sets (`staging`, `production`); `--var`
  overrides win over environment values.
- **Chained variables** — `{{token}}` in URLs, headers, query strings and
  bodies; `captures` pull values out of a response (JSON path or header)
  for later requests.
- **Six assertion types** — `status`, `jsonpath`, `header`,
  `body-contains`, `body-regex`, `latency-ms`.
- **CI-friendly reporting** — human-readable summary plus a `--json` report;
  exit code `0` all passed, `1` assertion failures, `2` usage/run error.
- **Validation** — `validate` catches bad methods, duplicate request names,
  unknown assertion types and missing variables before anything runs.

## Collection format

```json
{
  "apiforge": 1,
  "name": "Bookstore API",
  "baseUrl": "https://api.example.com",
  "environments": {
    "staging": { "token": "stg-token", "userId": "1" }
  },
  "requests": [
    {
      "name": "Login",
      "method": "POST",
      "path": "/auth/login",
      "headers": { "Content-Type": "application/json" },
      "body": "{\"username\": \"ada\", \"password\": \"{{password}}\"}",
      "captures": [
        { "name": "token", "from": "json", "path": "$.token" }
      ],
      "assertions": [
        { "type": "status", "expected": 200 },
        { "type": "body-contains", "value": "token" }
      ]
    },
    {
      "name": "Get user",
      "method": "GET",
      "path": "/users/{{userId}}",
      "headers": { "Authorization": "Bearer {{token}}" },
      "assertions": [
        { "type": "status", "expected": 200 },
        { "type": "jsonpath", "path": "$.id", "expected": "1" },
        { "type": "header", "path": "Content-Type", "expected": "application/json" },
        { "type": "latency-ms", "maxMs": 2000 }
      ]
    }
  ]
}
```

Notes on the format:

- `path` may be relative (resolved against `baseUrl`) or a full URL.
- `{{name}}` works in paths, query values, header values and bodies.
  A request that uses an undefined variable fails fast with a clear error
  instead of sending a half-built request.
- A `jsonpath` assertion compares the value at the path with `expected`
  (numbers and booleans compare as their JSON text, so `1` matches `1`).
  A `header` assertion passes when the expected value is contained in the
  header, so `application/json` matches
  `application/json; charset=utf-8`.
- Captures that match nothing don't fail the request — they produce a
  warning, and any later request that needs the missing variable fails
  with a clear message.

## Usage

```bash
# Create a starter collection
apiforge init --output bookstore.json

# Check a collection without sending anything
apiforge validate bookstore.json

# Run against an environment, overriding one variable
apiforge run bookstore.json --env staging --var password=hunter2

# Also write a machine-readable report
apiforge run bookstore.json --env production --json report.json

# Per-request timeout (default 30s)
apiforge run bookstore.json --timeout 10
```

Example output:

```
Running 'Bookstore API' — 2 request(s) (env: staging)

[PASS] Login — 200 in 142ms
[PASS] Get user — 200 in 38ms

All 2 request(s) passed in 187ms.
```

## Design decisions

1. **Zero runtime dependencies.** Everything is `System.Text.Json` and
   `HttpClient`. The hand-rolled JSON path evaluator covers the subset API
   tests actually need (properties, indexes, wildcards, recursive descent)
   without pulling in a JSONPath library.
2. **The HTTP handler is injectable.** `HttpRunner` takes an optional
   `HttpMessageHandler`, so the runner, captures and assertions are fully
   testable in-process with canned responses — no test server, no sockets,
   no flakiness.
3. **Fail fast on unknown variables.** A missing `{{variable}}` is a
   collection bug, not a request to send with a literal `{{variable}}` in
   it. The runner reports every missing name before sending.
4. **Captures warn, assertions fail.** A capture that matches nothing is
   usually a symptom, not the assertion itself — it warns, and the
   assertion that depends on the value is what fails.
5. **Requests run strictly in order.** Chaining (login → capture token →
   use token) only works if order is guaranteed, so there is no
   parallelism. Speed was traded for determinism.

## JSON path cheat sheet

| Pattern | Meaning |
| --- | --- |
| `$.store.book[0].title` | First book's title |
| `$.items[*].id` | Every item's id |
| `$..price` | Every `price` anywhere in the document |
| `$['weird key']` | Property whose name needs quoting |
| `$.items[-1]` | Last element |

## How to run

Requires .NET 8 SDK.

```bash
dotnet build ApiForge.sln -c Release
dotnet src/ApiForge/bin/Release/net8.0/apiforge.dll --help
```

Or publish a self-contained binary:

```bash
dotnet publish src/ApiForge -c Release -o ./publish
./publish/apiforge --help
```

## How to run tests

```bash
dotnet test
```

41 xUnit tests cover variable substitution, JSON path evaluation, every
assertion type, the HTTP runner (via an in-process mock handler), and
collection loading/validation.

> **Note:** `dotnet test` cannot run in some sandboxes because vstest fails
> to connect to its test host. In that case use the reflection harness in
> `/tmp/testrunner` (not part of this repo) or any machine with a normal
> .NET install — the tests are ordinary xUnit `[Fact]`s.

## Project structure

```
ApiForge.sln
src/ApiForge/
  Program.cs               CLI entry point, commands, exit codes
  Runner.cs                `run` command: environments, ordering, reporting
  HttpRunner.cs            request building, sending, response capture
  Models.cs                collection / request / capture / assertion model
  CollectionLoader.cs      JSON load/save, example collection
  VariableSubstitution.cs  {{variable}} resolution and missing-variable checks
  JsonPath.cs              minimal JSON path evaluator
  AssertionEngine.cs       the six assertion types
  Reporter.cs              text summary and JSON report
  RunReport.cs             run result model
tests/ApiForge.Tests/
  VariableSubstitutionTests.cs
  JsonPathTests.cs
  AssertionEngineTests.cs
  HttpRunnerTests.cs
  CollectionLoaderTests.cs
```

## License

MIT — see [LICENSE](LICENSE).
