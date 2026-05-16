#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/src"
OUT="$SCRIPT_DIR/out"

echo "=== BeamQuest Viewer build ==="

# ── Compile GLSL shaders to SPIR-V ──────────────────────────────────────────
if command -v glslc &>/dev/null; then
  echo "Compiling shaders..."
  mkdir -p "$SCRIPT_DIR/shaders/spv"
  glslc "$SCRIPT_DIR/shaders/vehicle.vert" -o "$SCRIPT_DIR/shaders/spv/vehicle.vert.spv"
  glslc "$SCRIPT_DIR/shaders/vehicle.frag" -o "$SCRIPT_DIR/shaders/spv/vehicle.frag.spv"
  glslc "$SCRIPT_DIR/shaders/terrain.vert" -o "$SCRIPT_DIR/shaders/spv/terrain.vert.spv"
  glslc "$SCRIPT_DIR/shaders/terrain.frag" -o "$SCRIPT_DIR/shaders/spv/terrain.frag.spv"

  # Embed SPIR-V as C# byte arrays
  echo "Generating ShaderBytecode.g.cs..."
  python3 "$SCRIPT_DIR/tools/embed_shaders.py" \
    "$SCRIPT_DIR/shaders/spv" \
    "$SRC/Rendering/ShaderBytecode.g.cs" \
    "BeamQuest.Rendering"
else
  echo "Warning: glslc not found — using pre-embedded shaders"
fi

# ── Build the .NET Android app ───────────────────────────────────────────────
echo "Building Android APK..."
cd "$SRC"
dotnet publish -c Release \
  -r android-arm64 \
  -p:AndroidPackageFormat=apk \
  --output "$OUT"

echo "Build complete → $OUT"

# ── Deploy to connected Quest ────────────────────────────────────────────────
APK=$(find "$OUT" -name "*.apk" | head -1)
if [ -n "$APK" ] && command -v adb &>/dev/null && adb devices | grep -q "device$"; then
  echo "Installing $APK..."
  adb install -r "$APK"
  echo "Installed."
else
  echo "APK: $APK (not deploying — no device or adb not found)"
fi
