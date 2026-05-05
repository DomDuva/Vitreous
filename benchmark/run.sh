#!/usr/bin/env bash
# =============================================================================
# Vitreous vs MediatR — one-click benchmark runner
#
# Usage:
#   ./benchmark/run.sh                         # build-time comparison + all runtime benchmarks
#   ./benchmark/run.sh --filter "*Dispatch*"   # build-time comparison + dispatch only
#   ./benchmark/run.sh --filter "*Startup*"    # build-time comparison + startup only
#
# Results are written to:
#   benchmark/Vitreous.Benchmarks/BenchmarkDotNet.Artifacts/
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BENCH_PROJECT="$SCRIPT_DIR/Vitreous.Benchmarks/Vitreous.Benchmarks.csproj"
MEDIATR_PROJECT="$SCRIPT_DIR/BuildTime.MediatR/BuildTime.MediatR.csproj"
VITREOUS_PROJECT="$SCRIPT_DIR/BuildTime.Vitreous/BuildTime.Vitreous.csproj"

echo "============================================================"
echo "  Vitreous vs MediatR — Performance & Build-Time Benchmarks"
echo "============================================================"
echo ""

# ── Build-time comparison ────────────────────────────────────────────────────
# Two minimal, equivalent projects:
#   BuildTime.MediatR   — plain MediatR, no source generator
#   BuildTime.Vitreous  — Vitreous with Roslyn incremental source generator
# Both are timed with --no-incremental so the compiler does a full rebuild.

echo "[ Build-time comparison ]"
echo "  Timing clean Release builds (--no-incremental)..."
echo ""

# MediatR build
T0=$SECONDS
dotnet build "$MEDIATR_PROJECT" -c Release --no-incremental --nologo -v q \
    2>&1 | grep -v "^$" || true
MEDIATR_BUILD=$(( SECONDS - T0 ))

# Vitreous build (includes source generator execution)
T0=$SECONDS
dotnet build "$VITREOUS_PROJECT" -c Release --no-incremental --nologo -v q \
    2>&1 | grep -v "^$" || true
VITREOUS_BUILD=$(( SECONDS - T0 ))

echo "  ┌───────────────────────────────────┬──────────────┐"
echo "  │ Project                           │ Build time   │"
echo "  ├───────────────────────────────────┼──────────────┤"
printf "  │ %-33s │ ~%-10ss │\n" "BuildTime.MediatR  (no codegen)"  "$MEDIATR_BUILD"
printf "  │ %-33s │ ~%-10ss │\n" "BuildTime.Vitreous (source gen.)" "$VITREOUS_BUILD"
echo "  └───────────────────────────────────┴──────────────┘"
echo ""

# ── Runtime performance benchmarks ───────────────────────────────────────────
echo "[ Runtime benchmarks ]"
echo "  Building Vitreous.Benchmarks (Release)..."
echo ""

dotnet build "$BENCH_PROJECT" -c Release --nologo -v q 2>&1 | grep -v "^$" || true

echo ""
echo "  Running BenchmarkDotNet suite (this takes a few minutes)..."
echo "  Artifacts: benchmark/Vitreous.Benchmarks/BenchmarkDotNet.Artifacts/"
echo ""

dotnet run -c Release --project "$BENCH_PROJECT" --no-build -- "$@"
