#!/usr/bin/env bash
# Publica o MOUSEMAPPER.exe portable a partir de Linux/macOS (cross-compile).
# O executável gerado roda no Windows; ele NÃO executa fora do Windows.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/MouseMapper/MouseMapper.csproj"
OUTPUT="${1:-$ROOT/dist}"

dotnet publish "$PROJECT" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUTPUT"

echo "Gerado: $OUTPUT/MouseMapper.exe"
ls -lh "$OUTPUT/MouseMapper.exe"
