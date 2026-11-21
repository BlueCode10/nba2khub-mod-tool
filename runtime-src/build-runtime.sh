#!/usr/bin/env bash
# Builds the standalone grow-to-1024 runtime (replace_tex.exe + _internal) that bundles
# Python + numpy + pillow, so users don't need Python installed.
#   Needs: python -m pip install numpy pillow pyinstaller
set -e
cd "$(dirname "$0")"
python -m PyInstaller --noconfirm --onedir --name replace_tex --console \
  --distpath dist --workpath build replace_tex_main.py
echo "Built: dist/replace_tex/  (ship this folder next to the NBA2KHUB Mod Tool exe)"
