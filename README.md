# Goal Whispers for Windows (v1)

One `GoalWhispers.exe` that runs on both **Windows 10 and Windows 11**. There's nothing to
install, because it uses the .NET Framework that's built into both.

Small copies of your goal image hide in the nooks of your screen, on top of every app:
- peeking up from behind the taskbar
- half-tucked against the left or right screen edge (wide logos turn sideways)
- peeking down from the top edge
- tilted across a screen corner

They ignore the mouse completely, so clicks go straight through to whatever is underneath.
They never show in Alt+Tab or the taskbar. Every few minutes they fade out and reappear
somewhere new. With more than one monitor, they spread across all of them.

## Run it
1. Put `GoalWhispers.exe` somewhere permanent, e.g. `Documents\GoalWhispers\`
   (not Downloads, if you'll turn on "Start with Windows").
2. Double-click it. Windows SmartScreen will say "Windows protected your PC", because
   the app isn't from a known publisher. Click **More info → Run anyway**. You only need to do this once.
3. A gold star appears in the system tray. On Windows 11 it may be under the **^** arrow next
   to the clock. You can drag it out onto the taskbar to keep it visible.

## The tray menu (right-click the star)
- **Pause / Resume hiding**
- **Hide them somewhere new**
- **Choose image…** (PNG, JPG, GIF or BMP). It's used exactly as it is: same colours, same background.
- **Go back to the gold star**
- **How hidden**: Subtle, Normal or Everywhere
- **Visibility**: 25% to 100% (100% = full colour)
- **Size**: Small, Medium, Large or Huge
- **Hide during full-screen videos and games** (on by default)
- **Start with Windows**
- **Quit**

You can also drop an image named `whisper.png` (or `.jpg`, `.gif`, `.bmp`) next to the
.exe. It's used whenever no image has been chosen from the menu.

## What it touches
- No internet. There's no networking code at all.
- Settings and your chosen image are stored in `%APPDATA%\GoalWhispers\`.
- "Start with Windows" adds one entry under
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`. Untick it to remove it.

To uninstall: untick **Start with Windows**, choose **Quit**, then delete the .exe and the
`%APPDATA%\GoalWhispers` folder.

## Build from source
Linux/macOS (Mono): `./build.sh`

Windows (built-in compiler, no downloads):

    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:app.ico /resource:app.ico,GoalWhispers.app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:GoalWhispers.exe Program.cs
