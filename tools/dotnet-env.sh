#!/usr/bin/env bash
# Source this file from the repository root; it configures only project-local state.
# Caller must supply an authorized SDK. No SDK is downloaded or installed here.
if [[ -z "${PDFFORM_DOTNET_ROOT:-}" ]]; then
  printf '%s\n' 'Set PDFFORM_DOTNET_ROOT to the approved SDK directory.' >&2
  return 2 2>/dev/null || exit 2
fi
export DOTNET_ROOT="$PDFFORM_DOTNET_ROOT"
export HOME="$PWD/.runtime/home"
export XDG_RUNTIME_DIR="$PWD/.runtime/run"
export XDG_DATA_HOME="$PWD/.runtime/data"
export XDG_CONFIG_HOME="$PWD/.runtime/config"
export XDG_CACHE_HOME="$PWD/.runtime/cache"
export DOTNET_CLI_HOME="$PWD/.runtime/cli-home"
export NUGET_PACKAGES="$PWD/.runtime/nuget"
export NUGET_HTTP_CACHE_PATH="$PWD/.runtime/nuget-http"
export TMPDIR="$PWD/.runtime/tmp"
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$HOME" "$XDG_RUNTIME_DIR" "$XDG_DATA_HOME" "$XDG_CONFIG_HOME" "$XDG_CACHE_HOME" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$TMPDIR"
