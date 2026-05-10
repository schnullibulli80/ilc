# Benchmarks

This document describes how to run, read, and use performance measurements in this repository.

## 1) Benchmark scripts and artifacts

Compiler pipeline timing can be sampled with:

```bash
./scripts/run-compiler-performance.sh
```

It runs the already-built compiler CLI over representative fixtures in normal mode and `--timings` mode, then writes:

- `tmp/compiler-performance.log`
- per-run logs such as `tmp/compiler-performance-ui-qtquick-demo-normal-run-1.log`
- timings per-run logs such as `tmp/compiler-performance-ui-qtquick-demo-timings-run-1.log`

Use `--timings` when phase timings are needed without debug dump overhead. Use `--profile-binding` when symbol binding needs a hierarchical drill-down into Binder scopes. Use `--profile-lowering` when method lowering needs a hierarchical drill-down into Lowerer scopes. Use `--debug` when syntax, binding, symbol, IR, listing, and debug-symbol dumps are also required.
The `--timings` output also includes reachability counters such as processed methods, enqueue reasons (`root`, `directCall`, `propertyAccessor`, `nativeCallback`, `declaringType`, `typeMember`, `interfaceDispatch`), lowerer rebuilds, cache invalidations, and interface-dispatch expansions. `nativeCallback` tracks delegate `Invoke` methods that are retained because a reachable `DllImport` signature passes the delegate to unmanaged code. Lowerer cache invalidations are also grouped by `field`, `property`, and `type` so performance work can identify which symbol category forces rebuilds. `typeReferenceCacheHits` and `typeReferenceCacheMisses` measure the local reachability type-resolution cache used while scanning IR dependencies. `typesPreseededOrReplaced`, `fieldsPreseeded`, and `propertiesPreseeded` track type and member surfaces discovered before the first Lowerer instance is created, which should reduce later rebuilds. `closedGenericTypeInvalidationsSuppressed` tracks closed generic type additions that are retained for bytecode emission without rebuilding the Lowerer. `fieldInvalidationNames`, `propertyInvalidationNames`, and `typeInvalidationNames` list the highest-impact declaring type or type names when symbol additions still force Lowerer rebuilds. The reachability timing line breaks the phase down into declaration seeding, type-surface preseeding, global member seeding, root seeding, lowerer construction, method lowering, dependency scanning, and exception-handler scanning. `methodLoweringDurations` and `dependencyScanDurations` list the slowest methods for the two dominant reachability sub-phases.

For targeted Binder and Lowerer analysis, run:

```bash
./scripts/run-compiler-profile.sh
```

It profiles `compiler-bootstrap.ilc` by default and writes:

- `tmp/compiler-bootstrap-binding-profile.log`
- `tmp/compiler-bootstrap-lowering-profile.log`

Pass `ui-qtquick-demo` for the UI fixture set or `all` for both supported fixture sets:

```bash
./scripts/run-compiler-profile.sh ui-qtquick-demo
./scripts/run-compiler-profile.sh all
```

The `binding-profile:` and `lowering-profile:` lines report hierarchical scopes with total time, max single invocation time, and call count. Use the Binder profile after the `bind symbols` timing indicates that binding is dominant. Use the Lowerer profile after `methodLoweringDurations` identifies which ILC method is expensive.
By default the CLI prints the top 32 profile scopes. Increase that limit when a hotspot needs deeper child scopes:

```bash
ILC_COMPILER_PROFILE_TOP=120 ./scripts/run-compiler-profile.sh
```

The default is five measured runs after one warmup run. Override with:

```bash
ILC_COMPILER_PERF_RUNS=10 ./scripts/run-compiler-performance.sh
```

The baseline benchmark runner is:

```bash
./benchmarks/run-vm-vs-java.sh
```

It:

- compiles ILC benchmark sources (`benchmarks/ilc/*.ilc`)
- compiles Java counterparts (`benchmarks/java/*.java`)
- runs startup benchmark plus a set of workload benchmarks
- writes all benchmark logs to:

  - `tmp/benchmarks/`
  - `tmp/benchmarks/summary.txt`

Benchmark source files:

- `benchmarks/ilc/runtime_bench.ilc`
- `benchmarks/ilc/empty_startup.ilc`
- `benchmarks/java/RuntimeBench.java`
- `benchmarks/java/EmptyStartup.java`

## 2) Environment and overrides

Compiler timing:

- `ILC_COMPILER_PERF_RUNS` controls measured compiler runs.

The script supports these common settings:

- `BENCH_ITERATIONS` (default: `2000000`)
- `BENCH_ROUNDS` (default: `5`)
- `BENCH_WARMUP` (default: `2`)
- `ILCVM_CLI` (path override for VM CLI binary)
- `JAVA_CMD`, `JAVAC_CMD`, `DOTNET_CMD` (tool overrides)

Example:

```bash
BENCH_ITERATIONS=1000000 BENCH_ROUNDS=3 ./benchmarks/run-vm-vs-java.sh
```

## 3) Interpreting `run-vm-vs-java.sh` output

The script prints:

- startup comparison (`ilcvm startup ms` vs `java startup ms`)
- workload rows with:
  - `bench`
  - `ilcvm_avg`
  - `java_avg`
  - `faster`
  - `slower`
  - `result`

Important:

- Smaller time is better (`lower ms` wins).
- `result` must match between runtimes; mismatches mean benchmark logic is broken.

`summary.txt` contains all numeric aggregates and winner annotations for reproducibility.

## 4) Direct benchmark execution with `ilcvm_cli`

For quick spot checks you can run a single benchmark directly:

```bash
build/runtime/ilcvm_cli/ilcvm_cli benchmarks/ilc/runtime_bench.ilb --run -- dispatch 2000000
```

This bypasses JVM comparison and runs one named workload repeatedly with fixed iteration count.

## 5) VM performance counters

The CLI supports `--performance` output for deeper analysis:

```bash
build/runtime/ilcvm_cli/ilcvm_cli benchmarks/ilc/runtime_bench.ilb --run --performance -- loop 2000000
```

Typical counters include:

- `performance.total_execution_ns`
- `performance.host_import_execution_ns`
- `performance.call_virt_count`
- `performance.call_virt_execution_ns`
- `performance.instructions_executed`
- `performance.functions_executed`
- `performance.new_arr_count` / `new_obj_count`
- `performance.ld_elem_count`, `st_elem_count`, `ld_len_count`
- `performance.leaf_fastpath_calls`, `performance.specialized_leaf_fastpath_calls`
- `performance.max_call_depth`
- `performance.*_execution_ns` for selected groups (e.g. array/branch/move/compare when tracked)

Use these counters to locate hotspots before codegen/VM changes:

- high call/virtual-call ns: inspect dispatch overhead
- high array read/write ns: inspect array op lowering and opcode path
- high branch/move/compare ns in loops: inspect generated loop shape
- high `leaf_fastpath_calls`: check if misses are expected or should be optimized

## 6) Recommended workflow

For optimization work:

1. Run baseline in script:

   ```bash
   ./benchmarks/run-vm-vs-java.sh
   ```

2. Re-run targeted benchmark with `--performance`.
3. change only one optimization candidate at a time.
4. re-run same benchmark with identical `BENCH_*` settings.
5. compare counters and aggregate times before and after.

## 7) Common pitfalls

- Missing logs usually means build or path setup is incomplete (especially `build/runtime/ilcvm_cli/ilcvm_cli`).
- If `ilcvm` appears too slow in one area, start with the counter-heavy benchmark for that workload (`dispatch`, `array_sum`, `loop`, ...), not with all.
- If a result mismatch appears, do not optimize timing until results are byte-identical first.
- Keep `BENCH_WARMUP` constant while comparing changes to reduce noise.
