# FAQ

## What is ILC?

ILC is a Pascal-oriented, object-oriented language implementation with its own compiler pipeline and a native register-based runtime (`ilcvm`). It is not a transpiler; it compiles to an internal `.ilb` module format that runs on the runtime VM.

## Why this project exists?

The goal is to provide a modern and readable language/runtime platform with:

- explicit, structured syntax,
- C#-level language capabilities,
- stable bytecode execution,
- and security-minded packaging evolution.

## Is the project production-ready?

No. It is currently in an implementation-and-hardening phase. The core compiler-to-VM pipeline and a strong bootstrap set of features are in place, but broad coverage for every listed feature area is still actively being expanded.

## Which parts are implemented today?

At a practical level:

- syntax/parser/binding/lowering and bytecode emission for a substantial feature set,
- runtime execution for core value, object, array, call, and exception behavior,
- shipped standard library in `libs/shipped/system.ilc`,
- local verification flow (`scripts/run-local-verification.sh`),
- benchmark tooling against Java.

## Why a custom VM instead of an existing runtime?

The VM is the core portability and experimentation point. It allows:

- explicit runtime semantics across platforms,
- deterministic performance work in interpreter path and host import boundaries,
- tighter control over future secure execution features.

## How is the project organized?

As a practical split:

- C# for compiler and tooling (`src/`),
- C++ for runtime and VM (`runtime/`),
- ILC shipped code in `libs/`,
- tests in `tests/`,
- verification and benchmarks in `scripts/` and `benchmarks/`.

## What is `uses` and `shipped libraries`?

`uses` pulls ILC modules into compilation units (for example `uses System;`). The shipped standard library lives in `libs/shipped`, currently with `system.ilc` as the first runtime-facing surface.

## How do I run a local verification?

Use:

```bash
./scripts/run-local-verification.sh
```

The workflow and log files are documented in [building-and-testing.md](building-and-testing.md).

## How do I run benchmarks?

Use:

```bash
./benchmarks/run-vm-vs-java.sh
```

Result interpretation and profiling workflow (`--performance`) are documented in [benchmarks.md](benchmarks.md).

## What is the status of performance?

The team is using a measured path:

- workload-level comparisons against Java,
- dedicated VM performance counters,
- incremental and reversible optimization steps.

No single benchmark can be treated as universal proof; benchmark corpus and counters drive optimization priorities.

## Is wide integer support already usable?

Wider integer types are defined in the roadmap and symbolic/type-level planning is in progress. Runtime execution support is the next major stage for full practical use.

## Is this open source compatible with secure packaging goals?

Yes. Open source improves trust and reviewability. Secure packaging adds distribution and hardening controls on emitted artifacts while keeping compiler/runtime implementation auditable.

## Where can I find contribution rules?

See [contributing.md](contributing.md).
