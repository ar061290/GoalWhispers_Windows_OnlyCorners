#!/usr/bin/env bash
# Builds GoalWhispers.exe (.NET Framework 4.x, runs on Windows 10 and 11).
# On Linux/macOS use Mono's mcs. On Windows you can use the built-in compiler:
#   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:app.ico ^
#     /resource:app.ico,GoalWhispers.app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
#     /out:GoalWhispers.exe Program.cs
set -euo pipefail
cd "$(dirname "$0")"
mcs -target:winexe -sdk:4.5 -optimize+ -platform:anycpu \
  -win32icon:app.ico -resource:app.ico,GoalWhispers.app.ico \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  -out:GoalWhispers.exe Program.cs
echo "Built GoalWhispers.exe"
