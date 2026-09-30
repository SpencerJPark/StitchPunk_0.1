#!/usr/bin/env bash
# Offline compile gate for the Playtest Copilot package.
#
# The Unity Editor is often closed while this package is built in a git worktree, so the
# usual refresh_unity + read_console gate is unavailable. This compiles the package with
# the Roslyn that ships inside the Unity install, against the reference assemblies the
# Editor would use. It catches CS errors; it cannot catch anything that only shows up at
# import, bake or run time, so it is a floor, not a substitute for opening the Editor.
#
# Usage: bash Tools~/compile-gate.sh [path/to/package]   (default: the script's parent)
set -u

UNITY_DATA="${UNITY_DATA:-C:/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Data}"
DOTNET="$UNITY_DATA/DotNetSdk/dotnet.exe"
CSC="$UNITY_DATA/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
PACKAGE_ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
PROJECT_ROOT="${PROJECT_ROOT:-$(cd "$PACKAGE_ROOT/../.." && pwd)}"
OUT_DIR="${TMPDIR:-/tmp}/playtest-copilot-gate"

# In a git worktree there is no Library, so the test assemblies are borrowed from the main
# checkout. Without this the Tests pass is silently skipped in exactly the case it is needed.
if [ ! -d "$PROJECT_ROOT/Library/ScriptAssemblies" ]; then
  MAIN_WORKTREE=$(git -C "$PROJECT_ROOT" worktree list --porcelain 2>/dev/null | head -1 | sed 's/^worktree //')
  if [ -n "$MAIN_WORKTREE" ] && [ -d "$MAIN_WORKTREE/Library/ScriptAssemblies" ]; then
    PROJECT_ROOT="$MAIN_WORKTREE"
  fi
fi

if [ ! -f "$CSC" ]; then
  echo "GATE UNAVAILABLE: no Roslyn at $CSC. Set UNITY_DATA to a Unity install." >&2
  exit 2
fi
mkdir -p "$OUT_DIR"

REFERENCE_ARGS=()
for dll in "$UNITY_DATA/Managed/UnityEngine/"*.dll; do REFERENCE_ARGS+=("-r:$dll"); done
REFERENCE_ARGS+=("-r:$UNITY_DATA/Managed/UnityEditor.dll")
REFERENCE_ARGS+=("-r:$UNITY_DATA/NetStandard/ref/2.1.0/netstandard.dll")

# Test assemblies live in the host project, not the Unity install. Without them the Tests
# pass is skipped and said so, rather than reported as a failure.
TEST_REFERENCE_ARGS=()
NUNIT_DLL=$(find "$PROJECT_ROOT/Library/PackageCache" -iname 'nunit.framework.dll' 2>/dev/null | head -1)
[ -n "$NUNIT_DLL" ] && TEST_REFERENCE_ARGS+=("-r:$NUNIT_DLL")
for name in UnityEngine.TestRunner UnityEditor.TestRunner; do
  test_dll="$PROJECT_ROOT/Library/ScriptAssemblies/$name.dll"
  [ -f "$test_dll" ] && TEST_REFERENCE_ARGS+=("-r:$test_dll")
done

compile_assembly() {
  assembly_name="$1"; shift
  sources=()
  for root in "$@"; do
    [ -d "$PACKAGE_ROOT/$root" ] || continue
    while IFS= read -r file; do sources+=("$file"); done < <(find "$PACKAGE_ROOT/$root" -name '*.cs')
  done
  if [ ${#sources[@]} -eq 0 ]; then
    echo "  $assembly_name: no sources, skipped"
    return 0
  fi
  all_references=("${REFERENCE_ARGS[@]}")
  if [ "$assembly_name" = "PlaytestCopilot.Tests" ]; then
    if [ ${#TEST_REFERENCE_ARGS[@]} -eq 0 ]; then
      echo "  $assembly_name: skipped, no nunit/TestRunner assemblies under $PROJECT_ROOT/Library"
      return 0
    fi
    all_references+=("${TEST_REFERENCE_ARGS[@]}")
  fi
  log="$OUT_DIR/$assembly_name.log"
  "$DOTNET" "$CSC" -nostdlib -noconfig -nologo -target:library -langversion:9.0 -unsafe- \
    -define:UNITY_EDITOR -define:UNITY_2023_1_OR_NEWER -define:UNITY_6000_0_OR_NEWER -define:UNITY_INCLUDE_TESTS \
    -out:"$OUT_DIR/$assembly_name.dll" "${all_references[@]}" "${sources[@]}" > "$log" 2>&1
  error_count=$(grep -c "error CS" "$log")
  echo "  $assembly_name: ${#sources[@]} files, $error_count errors"
  if [ "$error_count" -gt 0 ]; then
    grep "error CS" "$log" | sort -u | head -40
    return 1
  fi
  return 0
}

echo "Compile gate: $PACKAGE_ROOT"
overall=0
compile_assembly "PlaytestCopilot.Runtime" "Runtime" || overall=1
# The Editor assembly sees Runtime, so feed it both trees rather than referencing a built dll.
compile_assembly "PlaytestCopilot.Editor" "Runtime" "Editor" || overall=1
compile_assembly "PlaytestCopilot.Tests" "Runtime" "Editor" "Tests" || overall=1

if [ $overall -eq 0 ]; then echo "GATE PASS"; else echo "GATE FAIL"; fi
exit $overall
