#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"
demo_source="$repo_root/tests/fixtures/ui-qtquick-demo.ilc"
demo_runtime_source="$tmp_dir/ui-qtquick-demo.ilc"
demo_runtime_ilb="$tmp_dir/ui-qtquick-demo.ilb"

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
qtbridge_build_dir="$repo_root/build/runtime/ilcvm_qtbridge"

compile_log="$tmp_dir/ui-qtquick-demo-compile.log"
run_log="$tmp_dir/ui-qtquick-demo-run.log"

mkdir -p "$tmp_dir"
rm -f "$demo_runtime_source" "$demo_runtime_ilb" "$compile_log" "$run_log"
cp "$demo_source" "$demo_runtime_source"

if [[ -d "$qtbridge_build_dir" ]]; then
    if [[ -n "${LD_LIBRARY_PATH:-}" ]]; then
        export LD_LIBRARY_PATH="$qtbridge_build_dir:$LD_LIBRARY_PATH"
    else
        export LD_LIBRARY_PATH="$qtbridge_build_dir"
    fi
fi

if [[ -z "${QT_QPA_PLATFORM:-}" ]]; then
    if [[ -n "${WAYLAND_DISPLAY:-}" ]]; then
        export QT_QPA_PLATFORM=wayland
    elif [[ -n "${DISPLAY:-}" ]]; then
        export QT_QPA_PLATFORM=xcb
    else
        export QT_QPA_PLATFORM=offscreen
    fi
fi

: "${ILC_QTBRIDGE_BLOCKING_RUN:=1}"
export ILC_QTBRIDGE_BLOCKING_RUN

printf 'Compiling QtQuick UI demo to %s\n' "$demo_runtime_ilb"
dotnet run --project "$repo_root/src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj" -- --debug \
    "$demo_runtime_source" \
    "$system_fixture" \
    "$diagnostics_fixture" \
    "$text_fixture" \
    "$json_fixture" \
    "$net_fixture" \
    "$threading_fixture" \
    "$collections_fixture" \
    "$ui_fixture" \
    "$ui_hosting_fixture" \
    "$ui_qtquick_fixture" \
    "$demo_core_fixture" \
    >"$compile_log" 2>&1

printf 'Compile log: %s\n' "$compile_log"
printf 'Running QtQuick UI demo with QT_QPA_PLATFORM=%s and ILC_QTBRIDGE_BLOCKING_RUN=%s\n' "$QT_QPA_PLATFORM" "$ILC_QTBRIDGE_BLOCKING_RUN"
"$repo_root/build/runtime/ilcvm_cli/ilcvm_cli" "$demo_runtime_ilb" --run >"$run_log" 2>&1
printf 'Run log: %s\n' "$run_log"
cat "$run_log"
