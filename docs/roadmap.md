# Roadmap

This roadmap reflects the current implementation trajectory and near-term planning for ILC.

## Current status snapshot

- Compiler/frontend pipeline is active from syntax through bytecode emission.
- Runtime is a native, register-based interpreter (no JIT yet).
- Shipped standard library exists as `libs/shipped/system.ilc`.
- Verification scripts and benchmark scripts are in use for correctness and performance iteration.
- Secure packaging and cryptographic integer work are architected and partially prepared, with broader runtime support planned.

## Immediate focus (next 1-2 iterations)

### 1. Language feature completeness

- Stabilize currently documented language features in compiler + runtime interoperability.
- Consolidate edge-case validation for:
  - properties and indexers
  - virtual dispatch behavior
  - control-flow lowering (`try/with/case/match` interactions)
  - `uses` and shipped-library linking behavior
- Expand compiler and runtime tests for combinations that are now partly covered but not yet fully matrixed.

### 2. Standard library hardening

- Keep `System` as minimal but production-usable.
- Add incremental, stable APIs behind existing host-import conventions.
- Grow test coverage around:
  - command line and environment handling
  - path and file semantics
  - clock/time behavior consistency

### 3. Runtime correctness and perf guardrails

- Preserve current correctness while measuring regressions via:
  - `benchmarks/run-vm-vs-java.sh`
  - `ilcvm_cli --performance`
- Make the next optimization step traceable and reversible:
  - isolate one optimization at a time
  - compare benchmark aggregates + counters

## Medium-term (next 3-6 months)

### 4. Wide integer foundation (crypto path enablement)

This is currently a strategic priority:

- finalize the internal representation path for larger integers;
- move from symbolic/binder-level support toward bytecode/runtime-capable execution;
- stabilize a first-wins contract for:
  - parsing and typing
  - lowering
  - ILB/bytecode representation
  - runtime value semantics and operations

The expected first practical milestone is a robust `UInt128` baseline, with clear migration path to larger families.

### 5. Runtime maturity

- Improve allocation lifecycle clarity and object/array behavior.
- Revisit exception model edges and stack semantics with more production-like tests.
- Add dedicated instrumentation around call dispatch and host-import boundaries.
- Keep future UI integration out of VM-specific host-import growth where possible.
- Prefer a general FFI path for larger external systems:
  - primitive and enum transparency
  - explicit string marshalling rules
  - transparent POD-style records only
  - callback trampolines with strict lifetime rules
  - opaque handles for complex foreign types

### 6. Build/test engineering

- Keep build and verification as one-click path for contributors.
- Reduce “environment drift” risk for local scripts (documented prerequisites, explicit logs, deterministic settings).
- Extend test coverage for generated temporary artifacts and compile smoke outputs.

## Long-term

### 7. Security and packaging integration

- Implement package generation and consumption for:
  - signed (`.ilcp`) artifacts
  - encrypted and hardening-capable package variants
- Integrate package verification into release and smoke workflows.
- Keep runtime semantics unchanged unless policy explicitly needs a hardening switch.

### 8. Performance evolution

- Evaluate whether selective JIT-like or code-shaping strategies are beneficial after interpreter optimization plateaus.
- Preserve interpreter correctness-first guarantee until profiling data proves otherwise.
- Continue comparing against JVM baselines in representative workloads, not synthetic-only sets.

### 9. Open contribution surface

- Define contribution flow around:
  - feature proposals
  - compatibility boundaries
  - compiler diagnostics contracts
  - benchmark and test expectations for each new feature
- Expand docs as normative references for each shipped capability.

## Suggested execution principles

- Make changes in small, measurable increments.
- Never optimize before verifying semantic equivalence in tests.
- Tie every runtime performance claim to:
  - same benchmark input
  - same machine/environment settings
  - updated counter comparison
- Keep backward-compatible language behavior in priority for existing syntax and core library contracts.

## Working Definition of “Done”

For every milestone above:

- compiler behavior is green in tests
- runtime smoke + full local verification passes
- benchmark deltas are measured and documented
- docs are updated where behavior changed
