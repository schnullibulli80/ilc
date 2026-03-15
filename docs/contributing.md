# Contributing

This document defines how to contribute changes to ILC with minimal friction and high signal quality.

## 1) Before you start

- Read the relevant docs for the area you change:
  - [architecture.md](/home/pgraf/repos/ilc/docs/architecture.md)
  - [language-overview.md](/home/pgraf/repos/ilc/docs/language-overview.md)
  - [runtime-and-bytecode.md](/home/pgraf/repos/ilc/docs/runtime-and-bytecode.md)
  - [standard-library.md](/home/pgraf/repos/ilc/docs/standard-library.md)
  - [building-and-testing.md](/home/pgraf/repos/ilc/docs/building-and-testing.md)
- Keep PRs scoped to one coherent change set (language rule, runtime behavior, docs, or tests).
- Prefer small, reversible edits first; optimize and refactor in follow-up commits.

## 2) Branching and PR flow

- Work on a dedicated branch per change.
- Keep commit messages concise and task-focused.
- In PR descriptions include:
  - what changed,
  - why the change is needed,
  - validation performed,
  - and risk areas for rollback.

## 3) Code conventions

### General

- Keep code readable and explicit.
- Avoid broad abstractions when a narrow implementation is sufficient for current scope.
- Minimize duplicated logic across compiler stages (syntax/binding/lowering/bytecode).

### C# projects (`src/`, `tests/`)

- Stay consistent with existing style and naming in neighboring files.
- Prefer deterministic and explicit behavior over clever shortcuts.
- Add tests for behavior changes immediately in compiler tests or language tests where applicable.
- When touching semantic behavior, add negative tests for error cases too.

### C++ runtime (`runtime/`)

- Keep the VM fast path safe and correctness-first.
- Preserve performance instrumentation semantics where they exist.
- Update tests in `runtime/ilcvm_tests` for runtime-facing behavior changes.

## 4) Required local validation

Run at least the full local verification for substantial changes:

```bash
./scripts/run-local-verification.sh
```

For narrow parser/binder/lowering changes, still run the full command before merging unless there is a clear reason not to.

For runtime-focused changes, also run targeted benchmark/profiling checks:

```bash
./benchmarks/run-vm-vs-java.sh
```

For hotspot-focused work, run direct perf output:

```bash
build/runtime/ilcvm_cli/ilcvm_cli benchmarks/ilc/runtime_bench.ilb --run --performance -- dispatch 2000000
```

## 5) Testing expectations

- New language features require compiler tests.
- New host/runtime interactions require both compiler and runtime tests where applicable.
- Benchmark-driven changes should include before/after numbers and note methodology.
- If behavior changes, add at least one regression fixture that would fail on old behavior.

## 6) Documentation expectations

- Update docs when behavior changes or when new feature/API/CLI flags are added.
- If standard-library surface changes, update [standard-library.md].
- If pipeline/verification changes, update [building-and-testing.md].
- If roadmap impact exists, update [roadmap.md].

## 7) Review checklist (for contributors and reviewers)

- Does the change match the request scope and leave unrelated areas untouched?
- Are compiler and runtime boundaries respected (clear ownership, no leaky abstractions)?
- Are tests updated/added and passing?
- Are log outputs and diagnostics useful and stable?
- Is performance impact measured for runtime changes?

## 8) Security-minded contributions

- Do not add non-deterministic behavior in packaging or execution-sensitive paths without a reasoned design note.
- Keep secure packaging and hardened execution code paths explicit and isolated.
- Avoid hidden compatibility assumptions in package loading/execution logic.

## 9) Definition of acceptance

A PR is acceptable when:

- verification succeeds,
- tests reflect the changed behavior,
- docs are current for user-visible changes,
- and the change can be reverted without undocumented side effects.

