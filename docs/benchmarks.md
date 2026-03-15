# Benchmarks

This document describes how to run, read, and use performance measurements in this repository.

## 1) Benchmark scripts and artifacts

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

