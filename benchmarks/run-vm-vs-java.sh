#!/usr/bin/env bash

set -euo pipefail

export LC_ALL=C.UTF-8

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bench_root="$repo_root/benchmarks"
tmp_root="$repo_root/tmp/benchmarks"
ilc_tmp="$tmp_root/ilc"
java_tmp="$tmp_root/java"
summary_file="$tmp_root/summary.txt"

mkdir -p "$ilc_tmp" "$java_tmp"
rm -f "$summary_file"

iterations="${BENCH_ITERATIONS:-2000000}"
rounds="${BENCH_ROUNDS:-5}"
warmup="${BENCH_WARMUP:-2}"

java_cmd="${JAVA_CMD:-java}"
javac_cmd="${JAVAC_CMD:-javac}"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
ilcvm_cli="${ILCVM_CLI:-$repo_root/build/runtime/ilcvm_cli/ilcvm_cli}"
system_lib="$repo_root/libs/shipped/system.ilc"

require_cmd() {
    local cmd="$1"
    if ! command -v "$cmd" >/dev/null 2>&1; then
        printf 'missing required command: %s\n' "$cmd" >&2
        exit 1
    fi
}

require_cmd "$dotnet_cmd"
require_cmd "$java_cmd"
require_cmd "$javac_cmd"

if [[ ! -x "$ilcvm_cli" ]]; then
    printf 'missing ilcvm_cli binary: %s\n' "$ilcvm_cli" >&2
    printf 'build the runtime first, for example via scripts/run-local-verification.sh\n' >&2
    exit 1
fi

measure_process_ms() {
    local start_ns end_ns
    start_ns="$(date +%s%N)"
    "$@" >/dev/null 2>&1
    end_ns="$(date +%s%N)"
    printf '%s\n' "$(((end_ns - start_ns) / 1000000))"
}

average_lines() {
    awk '{ sum += $1; count += 1 } END { if (count == 0) { print "0.00"; } else { printf "%.2f", sum / count; } }'
}

extract_elapsed_ms() {
    sed -n 's/^ELAPSED_MS=//p' "$1" | tail -n 1
}

extract_result() {
    sed -n 's/^RESULT=//p' "$1" | tail -n 1
}

compile_ilc() {
    local source_file="$1"
    local output_log="$2"
    "$dotnet_cmd" run --project "$repo_root/src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj" "$source_file" "$system_lib" >"$output_log" 2>&1
}

printf 'Compiling ILC benchmarks...\n'
compile_ilc "$bench_root/ilc/empty_startup.ilc" "$tmp_root/compile-empty.log"
compile_ilc "$bench_root/ilc/runtime_bench.ilc" "$tmp_root/compile-runtime.log"

printf 'Compiling Java benchmarks...\n'
"$javac_cmd" -d "$java_tmp" "$bench_root/java/EmptyStartup.java" "$bench_root/java/RuntimeBench.java"

printf '\n'
printf 'Configuration\n'
printf '  iterations: %s\n' "$iterations"
printf '  rounds:     %s\n' "$rounds"
printf '  warmup:     %s\n' "$warmup"
printf '\n'

printf 'Startup benchmark\n'
printf '  measuring process start + trivial main\n'
ilc_startup_values=()
java_startup_values=()
for ((round = 1; round <= rounds; round++)); do
    ilc_startup_values+=("$(measure_process_ms "$ilcvm_cli" "$bench_root/ilc/empty_startup.ilb" --run)")
    java_startup_values+=("$(measure_process_ms "$java_cmd" -cp "$java_tmp" EmptyStartup)")
done

ilc_startup_avg="$(printf '%s\n' "${ilc_startup_values[@]}" | average_lines)"
java_startup_avg="$(printf '%s\n' "${java_startup_values[@]}" | average_lines)"
startup_winner="$(awk -v ilc="$ilc_startup_avg" -v java="$java_startup_avg" 'BEGIN {
    if (ilc < java) {
        print "ilcvm";
    } else if (java < ilc) {
        print "java";
    } else {
        print "equal";
    }
}')"
printf '  ilcvm startup ms: %s\n' "$ilc_startup_avg"
printf '  java  startup ms: %s\n' "$java_startup_avg"
printf '  startup faster:   %s\n' "$startup_winner"
printf 'startup ilcvm_ms=%s java_ms=%s faster=%s\n' "$ilc_startup_avg" "$java_startup_avg" "$startup_winner" >>"$summary_file"
printf '\n'

run_internal_bench() {
    local runtime_name="$1"
    local bench_name="$2"
    local output_file="$3"
    shift 3

    "$@" >"$output_file" 2>&1
    local elapsed
    elapsed="$(extract_elapsed_ms "$output_file")"
    if [[ -z "$elapsed" ]]; then
        printf 'missing ELAPSED_MS for %s %s\n' "$runtime_name" "$bench_name" >&2
        cat "$output_file" >&2
        exit 1
    fi

    printf '%s\n' "$elapsed"
}

printf 'Workload benchmarks\n'
printf '%-12s %-12s %-12s %-12s %-12s %-12s\n' "bench" "ilcvm_ms" "java_ms" "faster" "slower" "result"
for bench_name in loop array dispatch; do
    for ((round = 1; round <= warmup; round++)); do
        run_internal_bench "ilcvm" "$bench_name" "$tmp_root/warmup-ilc-$bench_name-$round.log" \
            "$ilcvm_cli" "$bench_root/ilc/runtime_bench.ilb" --run -- "$bench_name" "$iterations" >/dev/null
        run_internal_bench "java" "$bench_name" "$tmp_root/warmup-java-$bench_name-$round.log" \
            "$java_cmd" -cp "$java_tmp" RuntimeBench "$bench_name" "$iterations" >/dev/null
    done

    ilc_values=()
    java_values=()
    ilc_result=""
    java_result=""
    for ((round = 1; round <= rounds; round++)); do
        ilc_log="$tmp_root/ilc-$bench_name-$round.log"
        java_log="$tmp_root/java-$bench_name-$round.log"
        ilc_values+=("$(run_internal_bench "ilcvm" "$bench_name" "$ilc_log" "$ilcvm_cli" "$bench_root/ilc/runtime_bench.ilb" --run -- "$bench_name" "$iterations")")
        java_values+=("$(run_internal_bench "java" "$bench_name" "$java_log" "$java_cmd" -cp "$java_tmp" RuntimeBench "$bench_name" "$iterations")")
        ilc_result="$(extract_result "$ilc_log")"
        java_result="$(extract_result "$java_log")"
    done

    if [[ "$ilc_result" != "$java_result" ]]; then
        printf 'result mismatch for %s: ilcvm=%s java=%s\n' "$bench_name" "$ilc_result" "$java_result" >&2
        exit 1
    fi

    ilc_avg="$(printf '%s\n' "${ilc_values[@]}" | average_lines)"
    java_avg="$(printf '%s\n' "${java_values[@]}" | average_lines)"
    winner="$(awk -v ilc="$ilc_avg" -v java="$java_avg" 'BEGIN {
        if (ilc < java) {
            print "ilcvm";
        } else if (java < ilc) {
            print "java";
        } else {
            print "equal";
        }
    }')"
    slower="equal"
    if [[ "$winner" == "ilcvm" ]]; then
        slower="java"
    elif [[ "$winner" == "java" ]]; then
        slower="ilcvm"
    fi
    printf '%-12s %-12s %-12s %-12s %-12s %-12s\n' "$bench_name" "$ilc_avg" "$java_avg" "$winner" "$slower" "$ilc_result"
    printf '%s ilcvm_ms=%s java_ms=%s faster=%s slower=%s result=%s\n' "$bench_name" "$ilc_avg" "$java_avg" "$winner" "$slower" "$ilc_result" >>"$summary_file"
done

printf '\nLogs written to %s\n' "$tmp_root"
printf 'Summary written to %s\n' "$summary_file"
