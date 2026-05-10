#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp_dir="$repo_root/tmp"
profile_src_dir="$tmp_dir/compiler-profile-src"
compiler_cli="$repo_root/src/ILC.Compiler.Cli/bin/Debug/net9.0/ILC.Compiler.Cli"

target="${1:-compiler-bootstrap}"

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

if [[ ! -x "$compiler_cli" ]]; then
    echo "missing compiler cli binary: $compiler_cli" >&2
    echo "run scripts/run-local-verification.sh or dotnet build src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj first" >&2
    exit 2
fi

mkdir -p "$tmp_dir"
rm -rf "$profile_src_dir"
mkdir -p "$profile_src_dir"

copy_profile_source() {
    local source_path="$1"
    local target_path="$profile_src_dir/$(basename "$source_path")"
    cp "$source_path" "$target_path"
    printf '%s\n' "$target_path"
}

system_fixture="$(copy_profile_source "$system_fixture")"
diagnostics_fixture="$(copy_profile_source "$diagnostics_fixture")"
text_fixture="$(copy_profile_source "$text_fixture")"
json_fixture="$(copy_profile_source "$json_fixture")"
net_fixture="$(copy_profile_source "$net_fixture")"
threading_fixture="$(copy_profile_source "$threading_fixture")"
collections_fixture="$(copy_profile_source "$collections_fixture")"
ui_fixture="$(copy_profile_source "$ui_fixture")"
ui_hosting_fixture="$(copy_profile_source "$ui_hosting_fixture")"
ui_qtquick_fixture="$(copy_profile_source "$ui_qtquick_fixture")"
demo_core_fixture="$(copy_profile_source "$demo_core_fixture")"
bootstrap_fixture="$(copy_profile_source "$bootstrap_fixture")"
ui_demo_fixture="$(copy_profile_source "$ui_demo_fixture")"

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

run_profile() {
    local fixture_name="$1"
    local fixture_path="$2"
    local binding_log="$tmp_dir/$fixture_name-binding-profile.log"
    local lowering_log="$tmp_dir/$fixture_name-lowering-profile.log"

    echo "==> $fixture_name"
    "$compiler_cli" --profile-binding "$fixture_path" "${shared_inputs[@]}" >"$binding_log" 2>&1
    "$compiler_cli" --profile-lowering "$fixture_path" "${shared_inputs[@]}" >"$lowering_log" 2>&1
    echo "binding profile: $binding_log"
    echo "lowering profile: $lowering_log"
}

case "$target" in
    compiler-bootstrap)
        run_profile "compiler-bootstrap" "$bootstrap_fixture"
        ;;
    ui-qtquick-demo)
        run_profile "ui-qtquick-demo" "$ui_demo_fixture"
        ;;
    all)
        run_profile "compiler-bootstrap" "$bootstrap_fixture"
        run_profile "ui-qtquick-demo" "$ui_demo_fixture"
        ;;
    *)
        echo "usage: $0 [compiler-bootstrap|ui-qtquick-demo|all]" >&2
        exit 2
        ;;
esac
