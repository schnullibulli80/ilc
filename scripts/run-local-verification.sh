#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"

cd "$repo_root"

mkdir -p "$tmp_dir"
printf 'smoke-file-ok' >"$tmp_dir/runtime-smoke-input.txt"
rm -f "$tmp_dir/runtime-smoke-output.txt"

logs=(
    "$tmp_dir/solution-build-local.log"
    "$tmp_dir/compiler-tests-local.log"
    "$tmp_dir/compiler-runtime-cli-local.log"
    "$tmp_dir/runtime-build-local.log"
    "$tmp_dir/runtime-tests-local.log"
    "$tmp_dir/runtime-run-local.log"
    "$tmp_dir/local-verification-status.log"
)

rm -f "${logs[@]}"

status_log="$tmp_dir/local-verification-status.log"
current_step=""

log_status() {
    local message="$1"
    printf '%s\n' "$message" | tee -a "$status_log"
}

on_error() {
    local exit_code=$?
    if [[ -n "$current_step" ]]; then
        log_status "==> FAIL:  $current_step (exit $exit_code)"
    else
        log_status "==> FAIL:  verification script aborted before a step completed (exit $exit_code)"
    fi
    exit "$exit_code"
}

trap on_error ERR

run_step() {
    local label="$1"
    local log_file="$2"
    shift 2

    current_step="$label"
    log_status "==> START: $label"
    "$@" >"$log_file" 2>&1
    log_status "==> OK:    $label"
    log_status "    log: $log_file"
    current_step=""
}

run_runtime_smoke_step() {
    local label="$1"
    local log_file="$2"
    shift 2

    current_step="$label"
    log_status "==> START: $label"

    set +e
    "$@" >"$log_file" 2>&1
    local exit_code=$?
    set -e

    local smoke_result=""
    if grep -q "^SMOKE_RESULT=" "$log_file"; then
        smoke_result="$(grep "^SMOKE_RESULT=" "$log_file" | tail -n 1 | cut -d'=' -f2-)"
    fi

    if grep -q "^execution result: " "$log_file"; then
        log_status "==> OK:    $label"
        log_status "    log: $log_file"
        if [[ -n "$smoke_result" ]]; then
            log_status "    note: smoke reported result $smoke_result"
        fi
        log_status "    note: runtime process exit $exit_code"
        current_step=""
        return 0
    fi

    log_status "==> FAIL:  $label (exit $exit_code)"
    log_status "    log: $log_file"
    current_step=""
    return "$exit_code"
}

run_step \
    "Build solution" \
    "$tmp_dir/solution-build-local.log" \
    dotnet build "$repo_root/ILC.sln"

run_step \
    "Run compiler tests" \
    "$tmp_dir/compiler-tests-local.log" \
    dotnet run --project "$repo_root/tests/ILC.Compiler.Tests/ILC.Compiler.Tests.csproj"

run_step \
    "Compile runtime smoke" \
    "$tmp_dir/compiler-runtime-cli-local.log" \
    dotnet run --project "$repo_root/src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj" "$repo_root/tmp/ilc-runtime-smoke.ilc" "$repo_root/libs/shipped/system.ilc"

run_step \
    "Build runtime" \
    "$tmp_dir/runtime-build-local.log" \
    cmake --build "$repo_root/build"

run_step \
    "Run runtime tests" \
    "$tmp_dir/runtime-tests-local.log" \
    "$repo_root/build/runtime/ilcvm_tests/ilcvm_tests"

run_runtime_smoke_step \
    "Run runtime smoke" \
    "$tmp_dir/runtime-run-local.log" \
    "$repo_root/build/runtime/ilcvm_cli/ilcvm_cli" "$repo_root/tmp/ilc-runtime-smoke.ilb" --run

log_status ""
log_status "Verification complete."
