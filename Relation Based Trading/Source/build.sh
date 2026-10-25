#!/usr/bin/env bash
# Builds Relation Based Trading for RimWorld 1.5, 1.6 and 1.7 (needs .NET SDK 8+ and 10 for 1.7).
set -euo pipefail
cd "$(dirname "$0")"
for v in 1.5 1.6 1.7; do
  rm -rf obj bin
  dotnet build -c Release -p:RimVersion=$v -nologo -v q
done
rm -rf obj bin
