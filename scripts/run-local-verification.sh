#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"
bootstrap_fixture="$repo_root/tests/fixtures/compiler-bootstrap.ilc"
bootstrap_runtime_source="$tmp_dir/compiler-bootstrap-runtime.ilc"
bootstrap_runtime_ilb="$tmp_dir/compiler-bootstrap-runtime.ilb"
tcp_port_file="$tmp_dir/runtime-tcp-port.txt"
tcp_server_log="$tmp_dir/runtime-tcp-server.log"
http_port_file="$tmp_dir/runtime-http-port.txt"
http_server_log="$tmp_dir/runtime-http-server.log"
ws_port_file="$tmp_dir/runtime-ws-port.txt"
ws_server_log="$tmp_dir/runtime-ws-server.log"
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

cd "$repo_root"

mkdir -p "$tmp_dir"
printf 'smoke-file-ok' >"$tmp_dir/runtime-smoke-input.txt"
rm -f "$tmp_dir/runtime-smoke-output.txt"
cp "$bootstrap_fixture" "$bootstrap_runtime_source"
rm -f "$bootstrap_runtime_ilb"
rm -f "$tcp_port_file"
rm -f "$http_port_file"
rm -f "$ws_port_file"

logs=(
    "$tmp_dir/solution-build-local.log"
    "$tmp_dir/compiler-tests-local.log"
    "$tmp_dir/compiler-runtime-cli-local.log"
    "$tmp_dir/runtime-build-local.log"
    "$tmp_dir/runtime-tests-local.log"
    "$tmp_dir/runtime-run-local.log"
    "$tcp_server_log"
    "$http_server_log"
    "$ws_server_log"
    "$tmp_dir/local-verification-status.log"
)

diagnostic_dumps=(
    "$tmp_dir/compiler-bootstrap-runtime.ildbg"
    "$tmp_dir/compiler-bootstrap-runtime.syntax.txt"
    "$tmp_dir/compiler-bootstrap-runtime.binding.txt"
    "$tmp_dir/compiler-bootstrap-runtime.symbols.txt"
    "$tmp_dir/compiler-bootstrap-runtime.ir.txt"
    "$tmp_dir/compiler-bootstrap-runtime.listing.txt"
)

rm -f "${logs[@]}" "${diagnostic_dumps[@]}"

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

tcp_server_pid=""
http_server_pid=""
ws_server_pid=""

cleanup_background_services() {
    if [[ -n "$tcp_server_pid" ]] && kill -0 "$tcp_server_pid" 2>/dev/null; then
        kill "$tcp_server_pid" 2>/dev/null || true
        wait "$tcp_server_pid" 2>/dev/null || true
    fi

    if [[ -n "$http_server_pid" ]] && kill -0 "$http_server_pid" 2>/dev/null; then
        kill "$http_server_pid" 2>/dev/null || true
        wait "$http_server_pid" 2>/dev/null || true
    fi

    if [[ -n "$ws_server_pid" ]] && kill -0 "$ws_server_pid" 2>/dev/null; then
        kill "$ws_server_pid" 2>/dev/null || true
        wait "$ws_server_pid" 2>/dev/null || true
    fi
}

trap cleanup_background_services EXIT

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
    dotnet run --project "$repo_root/src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj" -- --debug "$bootstrap_runtime_source" "$system_fixture" "$diagnostics_fixture" "$text_fixture" "$json_fixture" "$net_fixture" "$threading_fixture" "$collections_fixture" "$ui_fixture" "$ui_hosting_fixture" "$ui_qtquick_fixture" "$demo_core_fixture"

run_step \
    "Build runtime" \
    "$tmp_dir/runtime-build-local.log" \
    cmake --build "$repo_root/build"

run_step \
    "Run runtime tests" \
    "$tmp_dir/runtime-tests-local.log" \
    "$repo_root/build/runtime/ilcvm_tests/ilcvm_tests"

current_step="Start TCP smoke server"
log_status "==> START: $current_step"
python3 "$repo_root/scripts/tcp_line_server.py" "$tcp_port_file" >"$tcp_server_log" 2>&1 &
tcp_server_pid=$!

for _ in $(seq 1 50); do
    if [[ -s "$tcp_port_file" ]]; then
        break
    fi

    sleep 0.1
done

if [[ ! -s "$tcp_port_file" ]]; then
    log_status "==> FAIL:  $current_step (server did not publish a port)"
    log_status "    log: $tcp_server_log"
    exit 1
fi

export ILC_TCP_SMOKE_PORT
ILC_TCP_SMOKE_PORT="$(cat "$tcp_port_file")"
log_status "==> OK:    $current_step"
log_status "    log: $tcp_server_log"
current_step=""

current_step="Start HTTP smoke server"
log_status "==> START: $current_step"
python3 "$repo_root/scripts/http_demo_server.py" "$http_port_file" >"$http_server_log" 2>&1 &
http_server_pid=$!

for _ in $(seq 1 50); do
    if [[ -s "$http_port_file" ]]; then
        break
    fi

    sleep 0.1
done

if [[ ! -s "$http_port_file" ]]; then
    log_status "==> FAIL:  $current_step (server did not publish a port)"
    log_status "    log: $http_server_log"
    exit 1
fi

export ILC_HTTP_SMOKE_PORT
ILC_HTTP_SMOKE_PORT="$(cat "$http_port_file")"
log_status "==> OK:    $current_step"
log_status "    log: $http_server_log"
current_step=""

current_step="Start WebSocket smoke server"
log_status "==> START: $current_step"
python3 "$repo_root/scripts/ws_echo_server.py" "$ws_port_file" >"$ws_server_log" 2>&1 &
ws_server_pid=$!

for _ in $(seq 1 50); do
    if [[ -s "$ws_port_file" ]]; then
        break
    fi

    sleep 0.1
done

if [[ ! -s "$ws_port_file" ]]; then
    log_status "==> FAIL:  $current_step (server did not publish a port)"
    log_status "    log: $ws_server_log"
    exit 1
fi

export ILC_WS_SMOKE_PORT
ILC_WS_SMOKE_PORT="$(cat "$ws_port_file")"
log_status "==> OK:    $current_step"
log_status "    log: $ws_server_log"
current_step=""

run_runtime_smoke_step \
    "Run runtime smoke" \
    "$tmp_dir/runtime-run-local.log" \
    "$repo_root/build/runtime/ilcvm_cli/ilcvm_cli" "$bootstrap_runtime_ilb" --run

log_status ""
log_status "Verification complete."
