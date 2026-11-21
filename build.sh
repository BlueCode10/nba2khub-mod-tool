#!/usr/bin/env bash
# Builds "3DM NBA2K12 MOD Tool.exe": one file with the original tool + DLLs embedded.
set -e
cd "$(dirname "$0")"
CSC=/c/Windows/Microsoft.NET/Framework/v4.0.30319/csc.exe
mkdir -p build dist
"$CSC" -nologo -codepage:65001 -optimize+ -platform:x86 -target:winexe \
  '-win32icon:assets\nba2khub.ico' \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  '-resource:payload\NBA2K12ModCore.exe,payload.NBA2K12ModCore.exe' \
  '-resource:payload\DXRender_4.dll,payload.DXRender_4.dll' \
  '-resource:payload\zlib.dll,payload.zlib.dll' \
  '-resource:assets\logo.png,app.logo.png' \
  '-resource:assets\nba2khub.ico,app.icon.ico' \
  '-out:dist\3DM NBA2K12 MOD Tool.exe' \
  'src\Program.cs' 'src\Translator.cs' 'src\Native.cs' 'src\Widgets.cs' 'src\Shell.cs' 'src\HelpForm.cs' 'src\SelfTest.cs'
ls -la dist
