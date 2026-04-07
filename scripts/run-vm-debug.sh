#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"
runtime_ilb="$tmp_dir/compiler-bootstrap-runtime.ilb"
runtime_cli="$repo_root/build/runtime/ilcvm_cli/ilcvm_cli"
default_debug_log="$tmp_dir/vm-debug-local.log"

cd "$repo_root"

if [[ ! -x "$runtime_cli" ]]; then
    echo "missing runtime debugger binary: $runtime_cli" >&2
    echo "build the runtime first via scripts/run-local-verification.sh" >&2
    exit 1
fi

if [[ ! -f "$runtime_ilb" ]]; then
    echo "missing runtime ilb: $runtime_ilb" >&2
    echo "compile the runtime smoke first via scripts/run-local-verification.sh" >&2
    exit 1
fi

has_debug_log=0
has_debug_repl=0
has_debug_script=0
has_debug_break=0
for argument in "$@"; do
    if [[ "$argument" == "--debug-log" ]]; then
        has_debug_log=1
    fi
    if [[ "$argument" == "--debug-repl" ]]; then
        has_debug_repl=1
    fi
    if [[ "$argument" == "--debug-script" ]]; then
        has_debug_script=1
    fi
    if [[ "$argument" == "--debug-break" || "$argument" == "--debug-break-on-entry" ]]; then
        has_debug_break=1
    fi
done

debug_arguments=(
    "--run"
    "--vm-debug"
)

if [[ $# -eq 0 ]]; then
    debug_arguments+=("--debug-repl" "--debug-break-on-entry")
elif [[ $has_debug_repl -eq 0 && $has_debug_script -eq 0 ]]; then
    if [[ $has_debug_break -eq 1 ]]; then
        debug_arguments+=("--debug-repl")
    else
        debug_arguments+=("--debug-repl" "--debug-break-on-entry")
    fi
fi

effective_repl=$has_debug_repl
for argument in "${debug_arguments[@]}"; do
    if [[ "$argument" == "--debug-repl" ]]; then
        effective_repl=1
        break
    fi
done

if [[ $effective_repl -eq 1 ]]; then
    has_debug_log=1
fi

if [[ $has_debug_repl -eq 0 && $has_debug_script -eq 0 && $has_debug_log -eq 0 ]]; then
    rm -f "$default_debug_log"
    debug_arguments+=("--debug-log" "$default_debug_log")
fi

exec "$runtime_cli" "$runtime_ilb" "${debug_arguments[@]}" "$@"
