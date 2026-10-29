// Goal Whispers for Windows (v1)
// One .exe for Windows 10 and Windows 11 (.NET Framework 4.x, built into both).
// Small copies of your goal image hide in the nooks of your screen, on top of every
// app. They ignore the mouse completely and move to new spots every few minutes.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Goal Whispers")]
[assembly: AssemblyProduct("Goal Whispers")]
[assembly: AssemblyDescription("Hides your goal image in the nooks of your screen.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace GoalWhispers
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "GoalWhispers.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show(
                        "Goal Whispers is already running.\n\nLook for the gold star in the system tray " +
                        "(click the ^ arrow next to the clock).",
                        "Goal Whispers", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }

    // ------------------------------------------------------------------ settings

    class Settings
    {
        public bool Paused = false;
        public int Density = 1;          // 0 subtle, 1 normal, 2 everywhere
        public int Opacity = 85;         // percent
        public int Size = 48;            // logical pixels (scaled for display DPI)
        public bool HideFullscreen = true;
        public bool FirstRun = true;

        public static string Dir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData), "GoalWhispers");
            }
        }

        public static string ImagePath { get { return Path.Combine(Dir, "whisper.png"); } }
        static string IniPath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(IniPath)) return s;
                foreach (var raw in File.ReadAllLines(IniPath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 1) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    int n;
                    bool isNum = int.TryParse(v, out n);
                    switch (k)
                    {
                        case "paused": s.Paused = v == "1"; break;
                        case "density": if (isNum) s.Density = Clamp(n, 0, 2); break;
                        case "opacity": if (isNum) s.Opacity = Clamp(n, 10, 100); break;
                        case "size": if (isNum) s.Size = Clamp(n, 16, 200); break;
                        case "hideFullscreen": s.HideFullscreen = v == "1"; break;
                        case "firstRun": s.FirstRun = v == "1"; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(IniPath, new[]
                {
                    "paused=" + (Paused ? "1" : "0"),
                    "density=" + Density,
                    "opacity=" + Opacity,
                    "size=" + Size,
                    "hideFullscreen=" + (HideFullscreen ? "1" : "0"),
                    "firstRun=" + (FirstRun ? "1" : "0"),
                });
            }
            catch { }
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "GoalWhispers";

        public static bool IsOn()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    var v = k == null ? null : k.GetValue(Name) as string;
                    return v != null && v.Trim('"').Equals(Application.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }

    // ------------------------------------------------------------------ images

    static class Images
    {
        static readonly string[] NextToExe = { "whisper.png", "whisper.jpg", "whisper.jpeg", "whisper.gif", "whisper.bmp" };

        /// Picked image, else a whisper.* file next to the .exe, else the built-in gold star.
        public static Bitmap Load()
        {
            var b = TryLoad(Settings.ImagePath);
            if (b != null) return b;
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                foreach (var name in NextToExe)
                {
                    b = TryLoad(Path.Combine(dir, name));
                    if (b != null) return b;
                }
            }
            catch { }
            return DefaultStar();
        }

        static Bitmap TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (var src = Image.FromStream(ms))
                {
                    return Copy32(src, src.Width, src.Height);
                }
            }
            catch { return null; }
        }

        /// Saves the picked image exactly as it looks: same colours, same background.
        /// Only fixes phone-photo rotation, shrinks huge files and crops empty transparent margins.
        public static void Import(string path)
        {
            Bitmap result;
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (var src = Image.FromStream(ms))
            {
                ApplyExifRotation(src);
                int w = src.Width, h = src.Height;
                float scale = Math.Min(1f, 1024f / Math.Max(w, h));
                result = Copy32(src, Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
            }
            result = TrimTransparent(result);
            Directory.CreateDirectory(Settings.Dir);
            string tmp = Settings.ImagePath + ".tmp";
            result.Save(tmp, ImageFormat.Png);
            result.Dispose();
            if (File.Exists(Settings.ImagePath)) File.Delete(Settings.ImagePath);
            File.Move(tmp, Settings.ImagePath);
        }

        static Bitmap Copy32(Image src, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var attr = new ImageAttributes())
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }
            return b;
        }

        static void ApplyExifRotation(Image img)
        {
            try
            {
                if (Array.IndexOf(img.PropertyIdList, 0x0112) < 0) return;
                int o = img.GetPropertyItem(0x0112).Value[0];
                RotateFlipType t;
                switch (o)
                {
                    case 2: t = RotateFlipType.RotateNoneFlipX; break;
                    case 3: t = RotateFlipType.Rotate180FlipNone; break;
                    case 4: t = RotateFlipType.Rotate180FlipX; break;
                    case 5: t = RotateFlipType.Rotate90FlipX; break;
                    case 6: t = RotateFlipType.Rotate90FlipNone; break;
                    case 7: t = RotateFlipType.Rotate270FlipX; break;
                    case 8: t = RotateFlipType.Rotate270FlipNone; break;
                    default: return;
                }
                img.RotateFlip(t);
            }
            catch { }
        }

        static Bitmap TrimTransparent(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            for (int y = 0; y < h; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), px, y * w, w);
            b.UnlockBits(data);

            int top = h, left = w, right = -1, bottom = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (((uint)px[y * w + x] >> 24) > 8)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
            if (right < left || bottom < top) return b;
            if (left == 0 && top == 0 && right == w - 1 && bottom == h - 1) return b;
            var cropped = b.Clone(new Rectangle(left, top, right - left + 1, bottom - top + 1), PixelFormat.Format32bppArgb);
            b.Dispose();
            return cropped;
        }

        public static Bitmap DefaultStar()
        {
            const int S = 256;
            var b = new Bitmap(S, S, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var brush = new SolidBrush(Color.FromArgb(255, 201, 165, 76)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var pts = new PointF[8];
                for (int i = 0; i < 8; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 4;
                    double r = i % 2 == 0 ? S * 0.48 : S * 0.12;
                    pts[i] = new PointF((float)(S / 2 + r * Math.Cos(a)), (float)(S / 2 + r * Math.Sin(a)));
                }
                g.FillPolygon(brush, pts);
            }
            return b;
        }
    }

    // ------------------------------------------------------------------ one whisper on screen

    class WhisperWindow : Form
    {
        readonly Bitmap bmp;
        readonly Rectangle rect;
        readonly byte target;
        byte current;
        System.Windows.Forms.Timer fade;

        public WhisperWindow(Bitmap bitmap, Rectangle where, byte targetAlpha)
        {
            bmp = bitmap;
            rect = where;
            target = targetAlpha;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = where;
            Text = "Goal Whisper";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                              | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST) { m.Result = (IntPtr)Native.HTTRANSPARENT; return; }
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Push(current);
        }

        public void FadeIn() { Animate(0, target, 1500, null); }

        public void FadeOut(Action done) { Animate(current, 0, 800, done); }

        public void KeepOnTop()
        {
            if (!IsHandleCreated || IsDisposed) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void Animate(byte from, byte to, int ms, Action done)
        {
            if (fade != null) { fade.Stop(); fade.Dispose(); }
            var started = DateTime.UtcNow;
            fade = new System.Windows.Forms.Timer { Interval = 30 };
            fade.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, (DateTime.UtcNow - started).TotalMilliseconds / ms);
                Push((byte)Math.Round(from + (to - from) * t));
                if (t >= 1.0)
                {
                    fade.Stop();
                    if (done != null) done();
                }
            };
            Push(from);
            fade.Start();
        }

        void Push(byte alpha)
        {
            current = alpha;
            if (!IsHandleCreated || IsDisposed) return;
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBitmap);
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT(rect.X, rect.Y);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = alpha,
                    AlphaFormat = Native.AC_SRC_ALPHA
                };
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    Native.SelectObject(memDc, old);
                    Native.DeleteObject(hBitmap);
                }
                Native.DeleteDC(memDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (fade != null) { fade.Stop(); fade.Dispose(); fade = null; }
                bmp.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ the tray app

    class TrayApp : ApplicationContext
    {
        const int SPOT_TASKBAR = 0, SPOT_LEFT = 1, SPOT_RIGHT = 2, SPOT_TOP = 3, SPOT_CORNER = 4;

        readonly Settings s = Settings.Load();
        readonly Random rnd = new Random();
        readonly List<WhisperWindow> shown = new List<WhisperWindow>();
        readonly List<WhisperWindow> fading = new List<WhisperWindow>();
        readonly System.Windows.Forms.Timer cycle = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly NotifyIcon tray = new NotifyIcon();
        Bitmap image;
        bool hiddenForFullscreen;

        ToolStripMenuItem pauseItem, fullscreenItem, startupItem;
        readonly List<ToolStripMenuItem> densityItems = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> opacityItems = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> sizeItems = new List<ToolStripMenuItem>();

        static readonly int[] OpacityChoices = { 25, 40, 60, 85, 100 };
        static readonly int[] SizeChoices = { 32, 48, 72, 110 };
        static readonly string[] SizeNames = { "Small", "Medium", "Large", "Huge" };

        public TrayApp()
        {
            image = Images.Load();

            tray.Icon = LoadTrayIcon();
            tray.Text = "Goal Whispers";
            tray.ContextMenuStrip = BuildMenu();
            tray.Visible = true;

            cycle.Tick += (o, e) => FadeOutThenPlace();
            poll.Tick += (o, e) => Poll();
            poll.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            if (s.FirstRun)
            {
                s.FirstRun = false;
                s.Save();
                tray.ShowBalloonTip(10000, "Goal Whispers is hiding",
                    "Right-click the gold star in the tray to choose your image. " +
                    "On Windows 11 it may be under the ^ arrow next to the clock.", ToolTipIcon.Info);
            }

            Restart();
        }

        // ---------------- menu

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();

            var header = new ToolStripMenuItem("Goal Whispers") { Enabled = false };
            header.Font = new Font(header.Font, FontStyle.Bold);
            m.Items.Add(header);
            m.Items.Add(new ToolStripSeparator());

            pauseItem = new ToolStripMenuItem("Pause hiding", null, (o, e) =>
            {
                s.Paused = !s.Paused;
                s.Save();
                RefreshMenu();
                Restart();
            });
            m.Items.Add(pauseItem);
            m.Items.Add(new ToolStripMenuItem("Hide them somewhere new", null, (o, e) => Restart()));
            m.Items.Add(new ToolStripSeparator());

            m.Items.Add(new ToolStripMenuItem("Choose image…", null, (o, e) => ChooseImage()));
            m.Items.Add(new ToolStripMenuItem("Go back to the gold star", null, (o, e) =>
            {
                try { if (File.Exists(Settings.ImagePath)) File.Delete(Settings.ImagePath); } catch { }
                ReloadImage();
            }));
            m.Items.Add(new ToolStripSeparator());

            var density = new ToolStripMenuItem("How hidden");
            string[] dn = { "Subtle  (1, sometimes none, moves every 25 min)", "Normal  (2, moves every 12 min)", "Everywhere  (4, moves every 5 min)" };
            for (int i = 0; i < 3; i++)
            {
                int v = i;
                var it = new ToolStripMenuItem(dn[i], null, (o, e) => { s.Density = v; s.Save(); RefreshMenu(); Restart(); });
                densityItems.Add(it);
                density.DropDownItems.Add(it);
            }
            m.Items.Add(density);

            var opacity = new ToolStripMenuItem("Visibility");
            foreach (int pct in OpacityChoices)
            {
                int v = pct;
                var it = new ToolStripMenuItem(pct + "%" + (pct == 100 ? "  (full colour)" : ""), null,
                    (o, e) => { s.Opacity = v; s.Save(); RefreshMenu(); Restart(); });
                opacityItems.Add(it);
                opacity.DropDownItems.Add(it);
            }
            m.Items.Add(opacity);

            var size = new ToolStripMenuItem("Size");
            for (int i = 0; i < SizeChoices.Length; i++)
            {
                int v = SizeChoices[i];
                var it = new ToolStripMenuItem(SizeNames[i], null, (o, e) => { s.Size = v; s.Save(); RefreshMenu(); Restart(); });
                sizeItems.Add(it);
                size.DropDownItems.Add(it);
            }
            m.Items.Add(size);
            m.Items.Add(new ToolStripSeparator());

            fullscreenItem = new ToolStripMenuItem("Hide during full-screen videos and games", null, (o, e) =>
            {
                s.HideFullscreen = !s.HideFullscreen;
                s.Save();
                RefreshMenu();
            });
            m.Items.Add(fullscreenItem);

            startupItem = new ToolStripMenuItem("Start with Windows", null, (o, e) =>
            {
                try { Startup.Set(!Startup.IsOn()); }
                catch (Exception ex) { MessageBox.Show("Couldn't change that: " + ex.Message, "Goal Whispers"); }
                RefreshMenu();
            });
            m.Items.Add(startupItem);
            m.Items.Add(new ToolStripSeparator());

            m.Items.Add(new ToolStripMenuItem("Quit", null, (o, e) => Quit()));

            m.Opening += (o, e) => RefreshMenu();
            RefreshMenu();
            return m;
        }

        void RefreshMenu()
        {
            pauseItem.Text = s.Paused ? "Resume hiding" : "Pause hiding";
            for (int i = 0; i < densityItems.Count; i++) densityItems[i].Checked = i == s.Density;
            for (int i = 0; i < opacityItems.Count; i++) opacityItems[i].Checked = OpacityChoices[i] == s.Opacity;
            for (int i = 0; i < sizeItems.Count; i++) sizeItems[i].Checked = SizeChoices[i] == s.Size;
            fullscreenItem.Checked = s.HideFullscreen;
            startupItem.Checked = Startup.IsOn();
        }

        void ChooseImage()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose your goal image";
                dlg.Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    Images.Import(dlg.FileName);
                    ReloadImage();
                    tray.ShowBalloonTip(4000, "Image saved", "Your whispers will use it from now on.", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Couldn't use that image.\n\n" + ex.Message, "Goal Whispers",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ReloadImage()
        {
            var old = image;
            image = Images.Load();
            Restart();
            if (old != null) old.Dispose();
        }

        Icon LoadTrayIcon()
        {
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("GoalWhispers.app.ico"))
                    if (st != null) return new Icon(st, SystemInformation.SmallIconSize);
            }
            catch { }
            using (var b = new Bitmap(Images.DefaultStar(), 32, 32))
                return Icon.FromHandle(b.GetHicon());
        }

        // ---------------- timing

        int IntervalMs { get { return (s.Density == 0 ? 25 : (s.Density == 1 ? 12 : 5)) * 60 * 1000; } }

        void Restart()
        {
            cycle.Stop();
            ClearAll();
            if (s.Paused) return;
            PlaceAll();
            cycle.Interval = IntervalMs;
            cycle.Start();
        }

        void Poll()
        {
            bool busy = s.HideFullscreen && IsFullscreenBusy();
            if (busy && !hiddenForFullscreen)
            {
                hiddenForFullscreen = true;
                ClearAll();
            }
            else if (!busy && hiddenForFullscreen)
            {
                hiddenForFullscreen = false;
                if (!s.Paused) PlaceAll();
            }
            foreach (var w in shown) w.KeepOnTop();
        }

        static bool IsFullscreenBusy()
        {
            try
            {
                int state;
                if (Native.SHQueryUserNotificationState(out state) != 0) return false;
                // 2 = full-screen app, 3 = full-screen game/video, 4 = presentation mode
                return state == 2 || state == 3 || state == 4;
            }
            catch { return false; }
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Restart();
        }

        void FadeOutThenPlace()
        {
            var old = new List<WhisperWindow>(shown);
            shown.Clear();
            if (old.Count == 0) { PlaceAll(); return; }
            fading.AddRange(old);
            int remaining = old.Count;
            foreach (var w in old)
            {
                var win = w;
                win.FadeOut(() =>
                {
                    fading.Remove(win);
                    win.Close();
                    win.Dispose();
                    if (--remaining == 0) PlaceAll();
                });
            }
        }

        void ClearAll()
        {
            foreach (var w in shown) { w.Close(); w.Dispose(); }
            foreach (var w in fading) { w.Close(); w.Dispose(); }
            shown.Clear();
            fading.Clear();
        }

        // ---------------- placing

        void PlaceAll()
        {
            if (s.Paused || hiddenForFullscreen || image == null) return;
            if (shown.Count > 0) return;
            int count = s.Density == 0 ? 1 : (s.Density == 1 ? 2 : 4);
            if (s.Density == 0 && rnd.NextDouble() > 0.6) return; // subtle: sometimes nothing at all

            var kinds = new List<int> { SPOT_TASKBAR, SPOT_LEFT, SPOT_RIGHT, SPOT_TOP, SPOT_CORNER };
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = kinds[i]; kinds[i] = kinds[j]; kinds[j] = t;
            }
            int placed = 0;
            foreach (int k in kinds)
            {
                if (placed >= count) break;
                try { if (Place(k)) placed++; } catch { }
            }
        }

        bool Place(int kind)
        {
            var screens = Screen.AllScreens;
            var scr = screens[rnd.Next(screens.Length)];
            Rectangle B = scr.Bounds, A = scr.WorkingArea;
            float scale = Native.SystemScale();

            float aspect = image.Width / (float)image.Height;
            float sq = (float)Math.Sqrt(aspect);
            float size = s.Size * scale;
            float dw = size * sq, dh = size / sq;
            float maxW = Math.Min(size * 5f, A.Width * 0.4f);
            if (dw > maxW) { dw = maxW; dh = dw / aspect; }
            int iw = Math.Max(1, (int)Math.Round(dw)), ih = Math.Max(1, (int)Math.Round(dh));
            bool wide = aspect > 1.4f;
            int margin = (int)(16 * scale);

            Bitmap full;
            int x, y;
            Rectangle clip = A; // never draw over the taskbar itself: it looks tucked behind it

            switch (kind)
            {
                case SPOT_TASKBAR:
                {
                    string edge = TaskbarEdge(B, A);
                    if (edge == "left" || edge == "right")
                    {
                        full = Render(iw, ih, wide ? (edge == "left" ? 270f : 90f) : 0f);
                        x = edge == "left" ? A.Left - (int)(full.Width * 0.45f) : A.Right - (int)(full.Width * 0.55f);
                        y = RandInt(A.Top + (int)(A.Height * 0.2f), A.Top + (int)(A.Height * 0.8f) - full.Height);
                    }
                    else if (edge == "top")
                    {
                        full = Render(iw, ih, 0f);
                        x = RandInt(A.Left + margin, A.Right - full.Width - margin);
                        y = A.Top - (int)(full.Height * 0.45f);
                    }
                    else // bottom taskbar, or none: peek up from the bottom edge
                    {
                        full = Render(iw, ih, 0f);
                        x = RandInt(A.Left + margin, A.Right - full.Width - margin);
                        y = A.Bottom - (int)(full.Height * 0.55f);
                    }
                    break;
                }
                case SPOT_LEFT:
                case SPOT_RIGHT:
                {
                    bool left = kind == SPOT_LEFT;
                    full = Render(iw, ih, wide ? (left ? 270f : 90f) : 0f);
                    x = left ? A.Left - (int)(full.Width * 0.4f) : A.Right - (int)(full.Width * 0.6f);
                    y = RandInt(A.Top + (int)(A.Height * 0.15f), A.Top + (int)(A.Height * 0.8f) - full.Height);
                    break;
                }
                case SPOT_TOP:
                {
                    full = Render(iw, ih, 0f);
                    x = RandInt(A.Left + margin, A.Right - full.Width - margin);
                    y = A.Top - (int)(full.Height * 0.45f);
                    break;
                }
                case SPOT_CORNER:
                {
                    int corner = rnd.Next(4); // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
                    float angle = (corner == 0 || corner == 3) ? -45f : 45f;
                    full = Render(iw, ih, angle);
                    float k = dw * 0.3f + dh * 0.25f;
                    float cx = (corner == 0 || corner == 2) ? A.Left + k : A.Right - k;
                    float cy = (corner == 0 || corner == 1) ? A.Top + k : A.Bottom - k;
                    x = (int)Math.Round(cx - full.Width / 2f);
                    y = (int)Math.Round(cy - full.Height / 2f);
                    break;
                }
                default:
                    return false;
            }

            Rectangle visible;
            var part = Crop(full, x, y, clip, out visible);
            if (part == null) return false;

            byte alpha = (byte)Math.Round(255 * s.Opacity / 100.0);
            var win = new WhisperWindow(part, visible, alpha);
            win.Show();
            win.KeepOnTop();
            win.FadeIn();
            shown.Add(win);
            return true;
        }

        static string TaskbarEdge(Rectangle B, Rectangle A)
        {
            if (A.Bottom < B.Bottom) return "bottom";
            if (A.Top > B.Top) return "top";
            if (A.Left > B.Left) return "left";
            if (A.Right < B.Right) return "right";
            return "none";
        }

        Bitmap Render(int w, int h, float angle)
        {
            double rad = angle * Math.PI / 180.0;
            double c = Math.Abs(Math.Cos(rad)), sn = Math.Abs(Math.Sin(rad));
            int bw = Math.Max(1, (int)Math.Ceiling(w * c + h * sn - 0.001));
            int bh = Math.Max(1, (int)Math.Ceiling(w * sn + h * c - 0.001));
            var b = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var attr = new ImageAttributes())
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                attr.SetWrapMode(WrapMode.TileFlipXY);
                g.TranslateTransform(bw / 2f, bh / 2f);
                g.RotateTransform(angle);
                g.DrawImage(image, new Rectangle(-w / 2, -h / 2, w, h), 0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attr);
            }
            return b;
        }

        static Bitmap Crop(Bitmap full, int x, int y, Rectangle clip, out Rectangle visible)
        {
            var r = new Rectangle(x, y, full.Width, full.Height);
            visible = Rectangle.Intersect(r, clip);
            if (visible.Width < 4 || visible.Height < 4) { full.Dispose(); return null; }
            if (visible == r) return full;
            var part = new Bitmap(visible.Width, visible.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(part))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(full, new Rectangle(0, 0, visible.Width, visible.Height),
                    new Rectangle(visible.X - x, visible.Y - y, visible.Width, visible.Height), GraphicsUnit.Pixel);
            }
            full.Dispose();
            return part;
        }

        int RandInt(int min, int max)
        {
            if (max <= min) return min;
            return rnd.Next(min, max + 1);
        }

        void Quit()
        {
            cycle.Stop();
            poll.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            ClearAll();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }
    }

    // ------------------------------------------------------------------ Windows API

    static class Native
    {
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int HTTRANSPARENT = -1;
        public const int MA_NOACTIVATE = 3;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int ULW_ALPHA = 0x00000002;
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

        public static float SystemScale()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int dpi = GetDeviceCaps(dc, 88); // LOGPIXELSX
                ReleaseDC(IntPtr.Zero, dc);
                return dpi > 0 ? dpi / 96f : 1f;
            }
            catch { return 1f; }
        }
    }
}
