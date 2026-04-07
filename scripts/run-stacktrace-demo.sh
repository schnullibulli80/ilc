#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"

compiler_cli="$repo_root/src/ILC.Compiler.Cli/bin/Debug/net9.0/ILC.Compiler.Cli"
runtime_cli="$repo_root/build/runtime/ilcvm_cli/ilcvm_cli"

system_fixture="$repo_root/libs/shipped/system.ilc"
diagnostics_fixture="$repo_root/libs/shipped/diagnostics.ilc"
text_fixture="$repo_root/libs/shipped/text.ilc"
json_fixture="$repo_root/libs/shipped/json.ilc"
collections_fixture="$repo_root/libs/shipped/collections.ilc"
demo_core_fixture="$repo_root/tests/fixtures/demo-core.ilc"

runtime_error_fixture="$repo_root/tests/fixtures/stacktrace-runtime-error.ilc"
unhandled_exception_fixture="$repo_root/tests/fixtures/stacktrace-unhandled-exception.ilc"

mode="exception"
output_mode="symbols"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime-error)
            mode="runtime-error"
            shift
            ;;
        --exception)
            mode="exception"
            shift
            ;;
        --raw)
            output_mode="raw"
            shift
            ;;
        --symbols)
            output_mode="symbols"
            shift
            ;;
        *)
            echo "unknown option: $1" >&2
            echo "usage: scripts/run-stacktrace-demo.sh [--exception|--runtime-error] [--symbols|--raw]" >&2
            exit 1
            ;;
    esac
done

cd "$repo_root"
mkdir -p "$tmp_dir"

if [[ ! -x "$compiler_cli" ]]; then
    echo "missing compiler cli binary: $compiler_cli" >&2
    echo "run scripts/run-local-verification.sh first" >&2
    exit 1
fi

if [[ ! -x "$runtime_cli" ]]; then
    echo "missing runtime cli binary: $runtime_cli" >&2
    echo "run scripts/run-local-verification.sh first" >&2
    exit 1
fi

fixture_source="$unhandled_exception_fixture"
if [[ "$mode" == "runtime-error" ]]; then
    fixture_source="$runtime_error_fixture"
fi

demo_source="$tmp_dir/stacktrace-demo-$mode.ilc"
demo_ilb="$tmp_dir/stacktrace-demo-$mode.ilb"
demo_ildbg="$tmp_dir/stacktrace-demo-$mode.ildbg"
raw_demo_ilb="$tmp_dir/stacktrace-demo-$mode-raw.ilb"

cp "$fixture_source" "$demo_source"
rm -f \
    "$demo_ilb" \
    "$demo_ildbg" \
    "$tmp_dir/stacktrace-demo-$mode.syntax.txt" \
    "$tmp_dir/stacktrace-demo-$mode.binding.txt" \
    "$tmp_dir/stacktrace-demo-$mode.symbols.txt" \
    "$tmp_dir/stacktrace-demo-$mode.ir.txt" \
    "$tmp_dir/stacktrace-demo-$mode.listing.txt" \
    "$raw_demo_ilb"

"$compiler_cli" --debug \
    "$demo_source" \
    "$system_fixture" \
    "$diagnostics_fixture" \
    "$text_fixture" \
    "$json_fixture" \
    "$collections_fixture" \
    "$demo_core_fixture"

runtime_target="$demo_ilb"
if [[ "$output_mode" == "raw" ]]; then
    cp "$demo_ilb" "$raw_demo_ilb"
    runtime_target="$raw_demo_ilb"
fi

exec "$runtime_cli" "$runtime_target" --run
