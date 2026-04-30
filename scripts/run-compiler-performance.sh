#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"
perf_src_dir="$tmp_dir/compiler-performance-src"
summary_log="$tmp_dir/compiler-performance.log"
compiler_cli="$repo_root/src/ILC.Compiler.Cli/bin/Debug/net9.0/ILC.Compiler.Cli"

runs="${ILC_COMPILER_PERF_RUNS:-5}"

system_fixture="$repo_root/libs/shipped/system.ilc"
diagnostics_fixture="$repo_root/libs/shipped/diagnostics.ilc"
text_fixture="$repo_root/libs/shipped/text.ilc"
json_fixture="$repo_root/libs/shipped/json.ilc"
net_fixture="$repo_root/libs/shipped/net.ilc"
threading_fixture="$repo_root/libs/shipped/threading.ilc"
collections_fixture="$repo_root/libs/shipped/collections.ilc"
ui_fixture="$repo_root/libs/shipped/ui.ilc"
ui_hosting_fixture="$repo_root/libs/shipped/ui-hosting.ilc"
ui_qtquick_fixture="$repo_root/libs/shipped/ui-backends-qtquick.ilc"
demo_core_fixture="$repo_root/tests/fixtures/demo-core.ilc"

bootstrap_fixture="$repo_root/tests/fixtures/compiler-bootstrap.ilc"
ui_demo_fixture="$repo_root/tests/fixtures/ui-qtquick-demo.ilc"

if [[ ! "$runs" =~ ^[0-9]+$ ]] || [[ "$runs" -lt 1 ]]; then
    echo "ILC_COMPILER_PERF_RUNS must be a positive integer, got: $runs" >&2
    exit 2
fi

if [[ ! -x "$compiler_cli" ]]; then
    echo "missing compiler cli binary: $compiler_cli" >&2
    echo "run scripts/run-local-verification.sh or dotnet build src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj first" >&2
    exit 2
fi

mkdir -p "$tmp_dir"
rm -rf "$perf_src_dir"
mkdir -p "$perf_src_dir"
rm -f "$summary_log"

copy_perf_source() {
    local source_path="$1"
    local target_path="$perf_src_dir/$(basename "$source_path")"
    cp "$source_path" "$target_path"
    printf '%s\n' "$target_path"
}

system_fixture="$(copy_perf_source "$system_fixture")"
diagnostics_fixture="$(copy_perf_source "$diagnostics_fixture")"
text_fixture="$(copy_perf_source "$text_fixture")"
json_fixture="$(copy_perf_source "$json_fixture")"
net_fixture="$(copy_perf_source "$net_fixture")"
threading_fixture="$(copy_perf_source "$threading_fixture")"
collections_fixture="$(copy_perf_source "$collections_fixture")"
ui_fixture="$(copy_perf_source "$ui_fixture")"
ui_hosting_fixture="$(copy_perf_source "$ui_hosting_fixture")"
ui_qtquick_fixture="$(copy_perf_source "$ui_qtquick_fixture")"
demo_core_fixture="$(copy_perf_source "$demo_core_fixture")"
bootstrap_fixture="$(copy_perf_source "$bootstrap_fixture")"
ui_demo_fixture="$(copy_perf_source "$ui_demo_fixture")"

fixtures=(
    "compiler-bootstrap|$bootstrap_fixture"
    "ui-qtquick-demo|$ui_demo_fixture"
)

shared_inputs=(
    "$system_fixture"
    "$diagnostics_fixture"
    "$text_fixture"
    "$json_fixture"
    "$net_fixture"
    "$threading_fixture"
    "$collections_fixture"
    "$ui_fixture"
    "$ui_hosting_fixture"
    "$ui_qtquick_fixture"
    "$demo_core_fixture"
)

printf 'ILC compiler performance\n' | tee -a "$summary_log"
printf 'runs: %s\n' "$runs" | tee -a "$summary_log"
printf 'compiler: %s\n' "$compiler_cli" | tee -a "$summary_log"
printf '\n' | tee -a "$summary_log"

run_fixture() {
    local fixture_name="$1"
    local fixture_path="$2"

    printf '==> %s\n' "$fixture_name" | tee -a "$summary_log"

    # Warm up the managed process and JIT once without including it in the measured run count.
    "$compiler_cli" "$fixture_path" "${shared_inputs[@]}" >"$tmp_dir/compiler-performance-$fixture_name-normal-warmup.log" 2>&1

    for run_index in $(seq 1 "$runs"); do
        local run_log="$tmp_dir/compiler-performance-$fixture_name-normal-run-$run_index.log"
        "$compiler_cli" "$fixture_path" "${shared_inputs[@]}" >"$run_log" 2>&1

        local compiled_line
        compiled_line="$(grep '^compiled ' "$run_log" | tail -n 1 || true)"
        if [[ -z "$compiled_line" ]]; then
            printf 'run %s: failed to find compile timing line, log: %s\n' "$run_index" "$run_log" | tee -a "$summary_log"
            return 1
        fi

        printf 'normal run %s: %s\n' "$run_index" "$compiled_line" | tee -a "$summary_log"
    done

    "$compiler_cli" --timings "$fixture_path" "${shared_inputs[@]}" >"$tmp_dir/compiler-performance-$fixture_name-timings-warmup.log" 2>&1

    for run_index in $(seq 1 "$runs"); do
        local run_log="$tmp_dir/compiler-performance-$fixture_name-timings-run-$run_index.log"
        "$compiler_cli" --timings "$fixture_path" "${shared_inputs[@]}" >"$run_log" 2>&1

        local compiled_line
        compiled_line="$(grep '^compiled ' "$run_log" | tail -n 1 || true)"
        if [[ -z "$compiled_line" ]]; then
            printf 'timings run %s: failed to find compile timing line, log: %s\n' "$run_index" "$run_log" | tee -a "$summary_log"
            return 1
        fi

        printf 'timings run %s: %s\n' "$run_index" "$compiled_line" | tee -a "$summary_log"
        grep '^reachability: ' "$run_log" | sed 's/^/  /' | tee -a "$summary_log" >/dev/null
        grep '^timing: ' "$run_log" | sed 's/^/  /' | tee -a "$summary_log" >/dev/null
    done

    printf '\n' | tee -a "$summary_log"
}

for fixture in "${fixtures[@]}"; do
    fixture_name="${fixture%%|*}"
    fixture_path="${fixture#*|}"
    run_fixture "$fixture_name" "$fixture_path"
done

printf 'summary log: %s\n' "$summary_log"
