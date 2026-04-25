# Building and Testing

This document describes how to build and verify the current ILC workspace on a Linux/Unix-style shell.

## Prerequisites

You need:

- .NET SDK for `dotnet` (solution and compiler tests)
- CMake and a C++ compiler for runtime projects
- `pkg-config` and `libffi` development files for runtime `DllImport` dispatch
- Qt 6 development/runtime packages for the Qt Quick bridge and UI smoke
- Java/JDK and `javac` only when running benchmark scripts
- a writable `build/` directory (or enough permission to create one)

## Default Verification Workflow

Use this one-liner for the full local verification flow:

```bash
./scripts/run-local-verification.sh
```

The script runs:

1. solution build
2. compiler tests
3. compiler smoke compilation using `tests/fixtures/compiler-bootstrap.ilc` plus shipped libraries
4. runtime CMake build (`cmake --build build`)
5. runtime tests
6. TCP / HTTP / WebSocket smoke server setup
7. runtime smoke execution (`ilcvm_cli ... --run`)

Step-by-step status is appended to:

- `tmp/local-verification-status.log`

## Log Files

Each verification step writes to one file:

- `tmp/solution-build-local.log`
- `tmp/compiler-tests-local.log`
- `tmp/compiler-runtime-cli-local.log`
- `tmp/runtime-build-local.log`
- `tmp/runtime-tests-local.log`
- `tmp/runtime-run-local.log`
- `tmp/runtime-tcp-server.log`
- `tmp/runtime-http-server.log`
- `tmp/runtime-ws-server.log`

When a step fails, that step exits with error code and the script stops.

## Manual Build and Test Steps

If you only need part of the flow, run the pieces directly.

1) Build .NET solution

```bash
dotnet build ILC.sln
```

2) Run compiler tests

```bash
dotnet run --project tests/ILC.Compiler.Tests/ILC.Compiler.Tests.csproj
```

3) Compile runtime smoke input

```bash
cp tests/fixtures/compiler-bootstrap.ilc tmp/compiler-bootstrap-runtime.ilc
dotnet run --project src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj \
  -- --debug \
  tmp/compiler-bootstrap-runtime.ilc \
  libs/shipped/system.ilc \
  libs/shipped/diagnostics.ilc \
  libs/shipped/text.ilc \
  libs/shipped/json.ilc \
  libs/shipped/net.ilc \
  libs/shipped/threading.ilc \
  libs/shipped/collections.ilc \
  libs/shipped/ui.ilc \
  libs/shipped/ui-hosting.ilc \
  libs/shipped/ui-backends-qtquick.ilc \
  tests/fixtures/demo-core.ilc
```

4) Configure runtime build

```bash
cmake -S . -B build
```

5) Build runtime artifacts

```bash
cmake --build build
```

6) Run runtime tests

```bash
build/runtime/ilcvm_tests/ilcvm_tests
```

7) Run runtime smoke

```bash
build/runtime/ilcvm_cli/ilcvm_cli tmp/compiler-bootstrap-runtime.ilb --run
```

`compiler-bootstrap-runtime.ilb` is generated in step 3 and is the immediate verification input.

## Qt Quick UI Smoke

Use the dedicated UI smoke when touching `System.Ui`, `System.Ui.Hosting`,
`System.Ui.Backends.QtQuick`, the Qt bridge, callbacks, or native UI refresh behavior:

```bash
ILC_QTBRIDGE_DEBUG=1 ./scripts/run-ui-qtquick-smoke.sh
```

The script:

1. copies `tests/fixtures/ui-qtquick-demo.ilc` to `tmp/ui-qtquick-demo.ilc`
2. compiles it together with the shipped libraries
3. runs the resulting `.ilb` through `ilcvm_cli --run`

Logs:

- compile: `tmp/ui-qtquick-demo-compile.log`
- run / Qt bridge: `tmp/ui-qtquick-demo-run.log`

Useful expected markers:

- `refresh mode=full-refresh` for active UI interaction refreshes
- no `refresh mode=experimental-patch` unless intentionally testing the inactive patch path
- no QML `ReferenceError`
- no `missing_object` element patch errors

If no display server is available, the script falls back to `QT_QPA_PLATFORM=offscreen`.

## Debugging Failed Builds

- For compiler-side errors:
  - start with `tmp/solution-build-local.log` and `tmp/compiler-tests-local.log`
- For runtime integration issues:
  - check `tmp/runtime-build-local.log` for compile/link failures
  - then `tmp/runtime-tests-local.log`
  - and `tmp/runtime-run-local.log` for execution-time host/import problems
- For verification-script orchestration problems:
  - inspect `tmp/local-verification-status.log` for step order and the failing command

## Benchmark and profiling workflow

Use the benchmark script when performance checks are needed:

```bash
./benchmarks/run-vm-vs-java.sh
```

It compiles ILC benchmarks, runs JVM and `ilcvm_cli`, and writes run data to:

- `tmp/benchmarks/`
- `tmp/benchmarks/summary.txt`

Common environment overrides:

- `BENCH_ITERATIONS`
- `BENCH_ROUNDS`
- `BENCH_WARMUP`
- `ILCVM_CLI`

Example:

```bash
BENCH_ITERATIONS=2_000_000 BENCH_ROUNDS=5 ./benchmarks/run-vm-vs-java.sh
```

`ilcvm_cli` supports dedicated profiling output with:

```bash
build/runtime/ilcvm_cli/ilcvm_cli benchmarks/ilc/runtime_bench.ilb --run --performance -- dispatch 2000000
```

That output is useful to validate optimization targets before changing VM internals.

## Recommended Development Order

For changes that touch both compiler and runtime:

1. build and test C# solution
2. run `./scripts/run-local-verification.sh`
3. inspect runtime test and smoke logs before micro-optimizing benchmark output
