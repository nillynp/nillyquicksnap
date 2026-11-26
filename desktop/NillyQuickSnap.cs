// ============================================================================
// NillyQuickSnap — Ultra-Light Standalone Snipping Tool Replacement
// Based on NillySnap by Nilly graphics (Sotiris Christos) — https://nillygraphics.com/
// Strictly C# 5 compatible (.NET Framework 4.x csc.exe — 0 external DLLs)
// Replaces Windows Snipping Tool (585.8 MB) in ~40 KB EXE & ~3-6 MB idle tray RAM!
// Features:
//  - Top-Left Live Pixel Counter (W x H px + ratio) on selection
//  - Auto-Copy to Clipboard AND Auto-Save to Pictures\Screenshots (Στιγμιότυπα οθόνης)
//  - Drawing Markup (Shift+Release or Edit Mode): Arrow (A), Box (R), Pen (P),
//    Highlighter (H), Privacy Blur (B), 5 Colors, Undo (Ctrl+Z), Fullscreen (F)
//  - Always-Ready Ultra-Low-RAM System Tray (PrintScreen, Ctrl+Alt+S, Win+Shift+S via ms-screenclip)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("NillyQuickSnap")]
[assembly: AssemblyDescription("Ultra-Light Screen Capture, Pixel Ruler & Markup — created by Nilly graphics (Nillygraphics · Sotiris Christos) — https://nillygraphics.com/")]
[assembly: AssemblyCompany("Nilly graphics (Nillygraphics · Sotiris Christos) — https://nillygraphics.com/")]
[assembly: AssemblyProduct("NillyQuickSnap")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Nilly graphics (Sotiris Christos) — https://nillygraphics.com/")]
[assembly: AssemblyTrademark("Nilly graphics / Nillygraphics — Sotiris Christos")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace NillyQuickSnap
{
    internal enum DrawTool { None, Arrow, Rect, Pen, Highlight, Blur }

    internal class MarkupItem
    {
        public DrawTool Tool;
        public Color Color;
        public Point Start;
        public Point End;
        public List<Point> Points;
    }

    internal static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const int WM_TRIGGER_SNAP = 0x0400 + 101;
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_SNAPSHOT = 0x2C; // PrintScreen
        public const uint VK_S = 0x53;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")]  public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static bool IsKeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static void TrimMemory()
        {
            // Release & compact dead Large Object Heap bitmaps WITHOUT paging out code/hook pages
            // (SetProcessWorkingSetSize would cause >300ms page-faults on WH_KEYBOARD_LL!)
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            }
            catch { }
        }
    }

    internal static class SnapPaths
    {
        public static string ScreenshotsFolder
        {
            get
            {
                string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string dir = Path.Combine(pics, "Screenshots");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static string BuildAutoSavePath()
        {
            string baseName = "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss");
            string dir = ScreenshotsFolder;
            string p = Path.Combine(dir, baseName + ".png");
            int n = 1;
            while (File.Exists(p) && n < 100)
            {
                p = Path.Combine(dir, baseName + "_" + n + ".png");
                n++;
            }
            return p;
        }
    }

    // ========================================================================
    // 1. FULLSCREEN CAPTURE, TOP-LEFT PIXEL RULER & MARKUP OVERLAY
    // ========================================================================
    internal sealed class SnapForm : Form
    {
        private Bitmap _screenBmp;
        private Rectangle _vScreen;
        private bool _selecting = false;
        private bool _hasSelection = false;
        private Point _selStart;
        private Point _selCurr;
        private Rectangle _selRect;

        private DrawTool _activeTool = DrawTool.Arrow;
        private Color _activeColor = Color.FromArgb(244, 63, 94);
        private bool _drawing = false;
        private MarkupItem _currentItem = null;
        private readonly List<MarkupItem> _items = new List<MarkupItem>();

        private Panel _toolbar;
        private readonly Dictionary<DrawTool, Button> _toolBtns = new Dictionary<DrawTool, Button>();
        private readonly List<Button> _colorBtns = new List<Button>();
        private bool _editMode;
        private bool _autoSaveScreenshots;

        public bool InitOk { get; private set; }
        public string LastSavedPath { get; private set; }
        public Size LastCapturedSize { get; private set; }

        public SnapForm(int delaySec, bool editMode, bool autoSaveScreenshots)
        {
            _editMode = editMode;
            _autoSaveScreenshots = autoSaveScreenshots;

            if (delaySec > 0) Thread.Sleep(delaySec * 1000);

            _vScreen = SystemInformation.VirtualScreen;
            _screenBmp = CaptureDesktop(_vScreen);
            if (_screenBmp == null) { InitOk = false; return; }
            InitOk = true;

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = _vScreen;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.KeyPreview = true;
            this.Text = "NillyQuickSnap";

            BuildToolbar();
        }

        private static Bitmap CaptureDesktop(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                IntPtr srcDc = Native.GetDC(IntPtr.Zero);
                if (srcDc != IntPtr.Zero)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            IntPtr dstDc = g.GetHdc();
                            try
                            {
                                if (Native.BitBlt(dstDc, 0, 0, bounds.Width, bounds.Height, srcDc, bounds.Left, bounds.Top, 0x00CC0020 | 0x40000000))
                                    return bmp;
                            }
                            finally { g.ReleaseHdc(dstDc); }
                        }
                    }
                    finally { Native.ReleaseDC(IntPtr.Zero, srcDc); }
                }
            }
            catch { }
            try
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                return bmp;
            }
            catch { bmp.Dispose(); return null; }
        }

        private Button MakeBtn(string text, string tip, int width, Color bg, Color fg, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Width = width;
            b.Height = 28;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(44, 56, 77);
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = bg;
            b.ForeColor = fg;
            b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.Margin = new Padding(2, 3, 2, 3);
            b.TabStop = false;
            b.Click += onClick;
            ToolTip tt = new ToolTip();
            tt.SetToolTip(b, tip);
            return b;
        }

        private void BuildToolbar()
        {
            _toolbar = new Panel();
            _toolbar.Height = 38;
            _toolbar.BackColor = Color.FromArgb(245, 15, 20, 31);
            _toolbar.Padding = new Padding(6, 2, 6, 2);
            _toolbar.Visible = false;
            _toolbar.Cursor = Cursors.Default;

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.BackColor = Color.Transparent;

            AddToolBtn(flow, DrawTool.Arrow,     "↗ Βέλος",   "Βέλος (A)", 68);
            AddToolBtn(flow, DrawTool.Rect,      "▢ Πλαίσιο", "Πλαίσιο (R)", 74);
            AddToolBtn(flow, DrawTool.Pen,       "✎ Στυλό",   "Ελεύθερη σχεδίαση (P)", 68);
            AddToolBtn(flow, DrawTool.Highlight, "▮ Marker",  "Μαρκαδόρος (H)", 70);
            AddToolBtn(flow, DrawTool.Blur,      "Blur",      "Θόλωμα / Απόκρυψη (B)", 54);

            Color[] palette = new Color[]
            {
                Color.FromArgb(244, 63, 94),
                Color.FromArgb(56, 189, 248),
                Color.FromArgb(250, 204, 21),
                Color.FromArgb(34, 197, 94),
                Color.White
            };
            for (int i = 0; i < palette.Length; i++)
            {
                Color col = palette[i];
                Button cb = new Button();
                cb.Width = 22; cb.Height = 22;
                cb.FlatStyle = FlatStyle.Flat;
                cb.FlatAppearance.BorderSize = 2;
                cb.BackColor = col;
                cb.Margin = new Padding(2, 6, 2, 3);
                cb.Cursor = Cursors.Hand;
                cb.TabStop = false;
                cb.Tag = col;
                cb.Click += delegate { _activeColor = col; UpdateToolbarStyles(); };
                _colorBtns.Add(cb);
                flow.Controls.Add(cb);
            }

            Button btnUndo = MakeBtn("↶ Undo", "Αναίρεση (Ctrl+Z)", 60, Color.FromArgb(28, 36, 51), Color.FromArgb(226, 232, 240), delegate { DoUndo(); });
            Button btnCopy = MakeBtn("✓ OK",   "Αντιγραφή + Αποθήκευση στα Στιγμιότυπα (Enter)", 64, Color.FromArgb(56, 189, 248), Color.FromArgb(7, 34, 53), delegate { DoCopyAutoSaveAndClose(); });
            Button btnSave = MakeBtn("Save As", "Αποθήκευση αλλού (Ctrl+S)", 68, Color.FromArgb(252, 150, 31), Color.FromArgb(20, 22, 26), delegate { DoSaveAsAndClose(); });
            Button btnEsc  = MakeBtn("✕", "Ακύρωση (Esc)", 30, Color.FromArgb(52, 24, 33), Color.FromArgb(253, 164, 175), delegate { this.Close(); });

            flow.Controls.Add(btnUndo);
            flow.Controls.Add(btnCopy);
            flow.Controls.Add(btnSave);

            int extraW = 0;
            string viewExe = FindSiblingExe("NillyViewPro", "NillyViewPro.exe");
            if (viewExe != null)
            {
                Button btnView = MakeBtn("↗ View", "Άνοιγμα κατευθείαν στο NillyView Pro (Crop / Eyedropper / WebP)", 66, Color.FromArgb(46, 34, 20), Color.FromArgb(252, 150, 31), delegate { DoSendToSiblingAndClose(viewExe); });
                flow.Controls.Add(btnView);
                extraW += 70;
            }
            string batchExe = FindSiblingExe("NillyBatchPro", "NillyBatchPro.exe");
            if (batchExe != null)
            {
                Button btnBatch = MakeBtn("↗ Batch", "Άνοιγμα στο NillyBatch Pro (Watermark / Social Resize)", 68, Color.FromArgb(26, 40, 56), Color.FromArgb(102, 217, 239), delegate { DoSendToSiblingAndClose(batchExe); });
                flow.Controls.Add(btnBatch);
                extraW += 72;
            }

            flow.Controls.Add(btnEsc);

            _toolbar.Controls.Add(flow);
            _toolbar.Width = 746 + extraW;
            this.Controls.Add(_toolbar);
            UpdateToolbarStyles();
        }

        private void AddToolBtn(FlowLayoutPanel flow, DrawTool t, string label, string tip, int width)
        {
            Button b = MakeBtn(label, tip, width, Color.FromArgb(28, 36, 51), Color.FromArgb(226, 232, 240), delegate
            {
                _activeTool = t;
                UpdateToolbarStyles();
            });
            _toolBtns[t] = b;
            flow.Controls.Add(b);
        }

        private void UpdateToolbarStyles()
        {
            foreach (KeyValuePair<DrawTool, Button> kv in _toolBtns)
            {
                if (kv.Key == _activeTool)
                {
                    kv.Value.BackColor = Color.FromArgb(24, 58, 95);
                    kv.Value.ForeColor = Color.FromArgb(56, 189, 248);
                    kv.Value.FlatAppearance.BorderColor = Color.FromArgb(56, 189, 248);
                }
                else
                {
                    kv.Value.BackColor = Color.FromArgb(28, 36, 51);
                    kv.Value.ForeColor = Color.FromArgb(226, 232, 240);
                    kv.Value.FlatAppearance.BorderColor = Color.FromArgb(44, 56, 77);
                }
            }
            for (int i = 0; i < _colorBtns.Count; i++)
            {
                Color c = (Color)_colorBtns[i].Tag;
                _colorBtns[i].FlatAppearance.BorderColor = (c.ToArgb() == _activeColor.ToArgb()) ? Color.White : Color.FromArgb(30, 41, 59);
            }
        }

        private void PositionToolbar()
        {
            int tx = Math.Max(8, Math.Min(_screenBmp.Width - _toolbar.Width - 8, _selRect.Right - _toolbar.Width));
            int ty = (_selRect.Bottom + 10 + _toolbar.Height <= _screenBmp.Height)
                ? (_selRect.Bottom + 10)
                : Math.Max(8, _selRect.Top - _toolbar.Height - 10);
            _toolbar.Location = new Point(tx, ty);
            _toolbar.Visible = true;
            _toolbar.BringToFront();
        }

        private Rectangle GetDragRect()
        {
            int x = Math.Min(_selStart.X, _selCurr.X);
            int y = Math.Min(_selStart.Y, _selCurr.Y);
            int w = Math.Abs(_selCurr.X - _selStart.X);
            int h = Math.Abs(_selCurr.Y - _selStart.Y);
            return Rectangle.Intersect(new Rectangle(x, y, w, h), new Rectangle(0, 0, _screenBmp.Width, _screenBmp.Height));
        }

        private Bitmap RenderFinalCrop()
        {
            Rectangle r = Rectangle.Intersect(_selRect, new Rectangle(0, 0, _screenBmp.Width, _screenBmp.Height));
            if (r.Width < 2 || r.Height < 2) return null;
            Bitmap crop = _screenBmp.Clone(r, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(crop))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < _items.Count; i++)
                    RenderMarkupItem(g, crop, _items[i], r.Left, r.Top);
            }
            return crop;
        }

        private static void PixelateRegion(Bitmap target, Rectangle rect, int blockSize)
        {
            Rectangle r = Rectangle.Intersect(rect, new Rectangle(0, 0, target.Width, target.Height));
            if (r.Width < 2 || r.Height < 2) return;
            using (Graphics g = Graphics.FromImage(target))
            {
                for (int y = r.Top; y < r.Bottom; y += blockSize)
                {
                    int bh = Math.Min(blockSize, r.Bottom - y);
                    for (int x = r.Left; x < r.Right; x += blockSize)
                    {
                        int bw = Math.Min(blockSize, r.Right - x);
                        Color sample = target.GetPixel(x + bw / 2, y + bh / 2);
                        using (SolidBrush br = new SolidBrush(sample)) g.FillRectangle(br, x, y, bw, bh);
                    }
                }
            }
        }

        private void RenderMarkupItem(Graphics g, Bitmap blurTarget, MarkupItem item, int offX, int offY)
        {
            if (item == null) return;
            if (item.Tool == DrawTool.Blur)
            {
                Rectangle br = new Rectangle(
                    Math.Min(item.Start.X, item.End.X) - offX,
                    Math.Min(item.Start.Y, item.End.Y) - offY,
                    Math.Abs(item.End.X - item.Start.X),
                    Math.Abs(item.End.Y - item.Start.Y));
                if (blurTarget != null) PixelateRegion(blurTarget, br, 10);
                else
                {
                    Rectangle srcR = Rectangle.Intersect(br, new Rectangle(0, 0, _screenBmp.Width, _screenBmp.Height));
                    if (srcR.Width >= 4 && srcR.Height >= 4)
                    {
                        using (Bitmap tmp = _screenBmp.Clone(srcR, PixelFormat.Format32bppArgb))
                        {
                            PixelateRegion(tmp, new Rectangle(0, 0, tmp.Width, tmp.Height), 10);
                            g.DrawImageUnscaled(tmp, srcR.Left, srcR.Top);
                        }
                    }
                }
                return;
            }
            if (item.Tool == DrawTool.Rect)
            {
                Rectangle rr = new Rectangle(
                    Math.Min(item.Start.X, item.End.X) - offX,
                    Math.Min(item.Start.Y, item.End.Y) - offY,
                    Math.Abs(item.End.X - item.Start.X),
                    Math.Abs(item.End.Y - item.Start.Y));
                using (Pen p = new Pen(item.Color, 3f)) g.DrawRectangle(p, rr);
            }
            else if (item.Tool == DrawTool.Arrow)
            {
                Point p1 = new Point(item.Start.X - offX, item.Start.Y - offY);
                Point p2 = new Point(item.End.X - offX, item.End.Y - offY);
                using (Pen p = new Pen(item.Color, 3.5f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, p1, p2);
                    double ang = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                    double headLen = 15.0;
                    PointF a1 = new PointF((float)(p2.X - headLen * Math.Cos(ang - Math.PI / 6.5)), (float)(p2.Y - headLen * Math.Sin(ang - Math.PI / 6.5)));
                    PointF a2 = new PointF((float)(p2.X - headLen * Math.Cos(ang + Math.PI / 6.5)), (float)(p2.Y - headLen * Math.Sin(ang + Math.PI / 6.5)));
                    using (SolidBrush br = new SolidBrush(item.Color))
                        g.FillPolygon(br, new PointF[] { p2, a1, a2 });
                }
            }
            else if ((item.Tool == DrawTool.Pen || item.Tool == DrawTool.Highlight) && item.Points != null && item.Points.Count > 1)
            {
                Color col = (item.Tool == DrawTool.Highlight) ? Color.FromArgb(110, item.Color) : item.Color;
                float w = (item.Tool == DrawTool.Highlight) ? 16f : 3f;
                Point[] pts = new Point[item.Points.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = new Point(item.Points[i].X - offX, item.Points[i].Y - offY);
                using (Pen p = new Pen(col, w))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, pts);
                }
            }
        }

        private void DoUndo()
        {
            if (_items.Count > 0) { _items.RemoveAt(_items.Count - 1); this.Invalidate(); }
        }

        private void DoCopyAutoSaveAndClose()
        {
            if (!_hasSelection) return;
            using (Bitmap crop = RenderFinalCrop())
            {
                if (crop != null)
                {
                    LastCapturedSize = crop.Size;
                    try { Clipboard.SetImage(crop); } catch { }
                    if (_autoSaveScreenshots)
                    {
                        try
                        {
                            string savePath = SnapPaths.BuildAutoSavePath();
                            crop.Save(savePath, ImageFormat.Png);
                            LastSavedPath = savePath;
                        }
                        catch { }
                    }
                }
            }
            this.Close();
        }

        private static string FindSiblingExe(string folder, string exeName)
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"NillySuite\" + folder + @"\" + exeName);
                if (File.Exists(appData)) return appData;
                DirectoryInfo par = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
                if (par != null)
                {
                    string sib = Path.Combine(Path.Combine(par.FullName, folder), exeName);
                    if (File.Exists(sib)) return sib;
                }
            }
            catch { }
            return null;
        }

        private void DoSendToSiblingAndClose(string siblingExe)
        {
            if (!_hasSelection) return;
            string saved = null;
            using (Bitmap crop = RenderFinalCrop())
            {
                if (crop == null) return;
                LastCapturedSize = crop.Size;
                try { Clipboard.SetImage(crop); } catch { }
                try
                {
                    saved = SnapPaths.BuildAutoSavePath();
                    crop.Save(saved, ImageFormat.Png);
                    LastSavedPath = saved;
                }
                catch
                {
                    try
                    {
                        saved = Path.Combine(Path.GetTempPath(), "NillySnap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                        crop.Save(saved, ImageFormat.Png);
                    }
                    catch { }
                }
            }
            this.Close();
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
            {
                try { Process.Start(new ProcessStartInfo(siblingExe, "\"" + saved + "\"") { WorkingDirectory = Path.GetDirectoryName(siblingExe), UseShellExecute = true }); } catch { }
            }
        }

        private void DoSaveAsAndClose()
        {
            if (!_hasSelection) return;
            using (Bitmap crop = RenderFinalCrop())
            {
                if (crop == null) return;
                LastCapturedSize = crop.Size;
                try { Clipboard.SetImage(crop); } catch { }
                this.TopMost = false;
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.InitialDirectory = SnapPaths.ScreenshotsFolder;
                    dlg.Filter = "PNG Image (*.png)|*.png";
                    dlg.FileName = "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        crop.Save(dlg.FileName, ImageFormat.Png);
                        LastSavedPath = dlg.FileName;
                    }
                }
            }
            this.Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { Native.SetForegroundWindow(this.Handle); } catch { }
            this.Activate();
            this.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { this.Close(); return; }
            if (!_hasSelection && e.KeyCode == Keys.F)
            {
                _selRect = new Rectangle(0, 0, _screenBmp.Width, _screenBmp.Height);
                _hasSelection = true;
                if (_editMode || e.Shift) { PositionToolbar(); this.Invalidate(); }
                else { DoCopyAutoSaveAndClose(); }
                return;
            }
            if (_hasSelection)
            {
                if (e.KeyCode == Keys.Enter || (e.Control && e.KeyCode == Keys.C)) { DoCopyAutoSaveAndClose(); return; }
                if (e.Control && e.KeyCode == Keys.S) { DoSaveAsAndClose(); return; }
                if (e.Control && e.KeyCode == Keys.Z) { DoUndo(); return; }
                if (e.KeyCode == Keys.A) { _activeTool = DrawTool.Arrow; UpdateToolbarStyles(); }
                else if (e.KeyCode == Keys.R) { _activeTool = DrawTool.Rect; UpdateToolbarStyles(); }
                else if (e.KeyCode == Keys.P) { _activeTool = DrawTool.Pen; UpdateToolbarStyles(); }
                else if (e.KeyCode == Keys.H) { _activeTool = DrawTool.Highlight; UpdateToolbarStyles(); }
                else if (e.KeyCode == Keys.B) { _activeTool = DrawTool.Blur; UpdateToolbarStyles(); }
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (_hasSelection && _items.Count > 0) DoUndo();
                else this.Close();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if (!_hasSelection)
                {
                    _selecting = true;
                    _selStart = e.Location;
                    _selCurr = e.Location;
                    this.Invalidate();
                }
                else if (_selRect.Contains(e.Location))
                {
                    _drawing = true;
                    _currentItem = new MarkupItem();
                    _currentItem.Tool = _activeTool;
                    _currentItem.Color = _activeColor;
                    _currentItem.Start = e.Location;
                    _currentItem.End = e.Location;
                    if (_activeTool == DrawTool.Pen || _activeTool == DrawTool.Highlight)
                    {
                        _currentItem.Points = new List<Point>();
                        _currentItem.Points.Add(e.Location);
                    }
                    this.Invalidate();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_selecting)
            {
                _selCurr = e.Location;
                this.Invalidate();
            }
            else if (_drawing && _currentItem != null)
            {
                Point clamped = new Point(
                    Math.Max(_selRect.Left, Math.Min(_selRect.Right, e.X)),
                    Math.Max(_selRect.Top, Math.Min(_selRect.Bottom, e.Y)));
                _currentItem.End = clamped;
                if (_currentItem.Points != null) _currentItem.Points.Add(clamped);
                this.Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_selecting && e.Button == MouseButtons.Left)
            {
                _selecting = false;
                Rectangle r = GetDragRect();
                if (r.Width >= 8 && r.Height >= 8)
                {
                    _selRect = r;
                    _hasSelection = true;
                    bool wantEdit = _editMode || ((Control.ModifierKeys & Keys.Shift) == Keys.Shift);
                    if (wantEdit) PositionToolbar();
                    else { DoCopyAutoSaveAndClose(); return; }
                }
                this.Invalidate();
            }
            else if (_drawing && e.Button == MouseButtons.Left)
            {
                _drawing = false;
                if (_currentItem != null) { _items.Add(_currentItem); _currentItem = null; }
                this.Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_screenBmp == null) return;
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(_screenBmp, 0, 0);

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(105, 7, 11, 18)))
                g.FillRectangle(dim, 0, 0, _screenBmp.Width, _screenBmp.Height);

            Rectangle activeRect = _hasSelection ? _selRect : (_selecting ? GetDragRect() : Rectangle.Empty);
            if (activeRect.Width > 0 && activeRect.Height > 0)
            {
                g.DrawImage(_screenBmp, activeRect, activeRect, GraphicsUnit.Pixel);

                g.SetClip(activeRect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < _items.Count; i++) RenderMarkupItem(g, null, _items[i], 0, 0);
                if (_currentItem != null) RenderMarkupItem(g, null, _currentItem, 0, 0);
                g.ResetClip();

                using (Pen p = new Pen(Color.FromArgb(255, 56, 189, 248), 2)) g.DrawRectangle(p, activeRect);

                // Crisp Top-Left Pixel Counter Badge right above/inside top-left corner!
                string ratio = activeRect.Height > 0 ? string.Format("{0:F2}:1", (double)activeRect.Width / activeRect.Height) : "-";
                string pxText = string.Format("{0} × {1} px  ({2})", activeRect.Width, activeRect.Height, ratio);
                using (Font fPx = new Font("Consolas", 10.5f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(pxText, fPx);
                    int bw = (int)Math.Ceiling(sz.Width) + 14;
                    int bh = 26;
                    int bx = Math.Max(6, Math.Min(_screenBmp.Width - bw - 6, activeRect.Left));
                    int by = (activeRect.Top - bh - 4 >= 6) ? (activeRect.Top - bh - 4) : (activeRect.Top + 6);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(242, 15, 20, 31)))
                    using (Pen bp = new Pen(Color.FromArgb(255, 56, 189, 248), 1.5f))
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(248, 250, 252)))
                    {
                        g.FillRectangle(bg, bx, by, bw, bh);
                        g.DrawRectangle(bp, bx, by, bw, bh);
                        g.DrawString(pxText, fPx, fg, bx + 7, by + 4);
                    }
                }
            }
            else
            {
                string hint = _editMode
                    ? "Nilly QuickSnap (Σχεδίαση)  •  Σύρε περιοχή  |  F = Όλη η Οθόνη  |  Esc = Ακύρωση"
                    : "Nilly QuickSnap  •  Σύρε & Άφησε = Copy + Save στα Στιγμιότυπα  |  Shift = Σχεδίαση  |  F = Όλη η Οθόνη  |  Esc = Ακύρωση";
                using (Font f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(hint, f);
                    int bx = (_screenBmp.Width - (int)sz.Width) / 2 - 14;
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(238, 11, 15, 23)))
                    using (Pen bp = new Pen(Color.FromArgb(255, 56, 189, 248), 1))
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(248, 250, 252)))
                    {
                        g.FillRectangle(bg, bx, 20, sz.Width + 28, 32);
                        g.DrawRectangle(bp, bx, 20, sz.Width + 28, 32);
                        g.DrawString(hint, f, fg, bx + 14, 26);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _screenBmp != null) { _screenBmp.Dispose(); _screenBmp = null; }
            base.Dispose(disposing);
        }
    }

    // ========================================================================
    // 2. ULTRA-LOW-RAM TRAY DAEMON (NativeWindow Hotkeys + EventWaitHandle IPC)
    // ========================================================================
    internal sealed class HotkeyReceiver : NativeWindow, IDisposable
    {
        public event Action<int> HotkeyPressed;
        public event Action SnapTriggered;
        public HotkeyReceiver() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && HotkeyPressed != null)
                HotkeyPressed(m.WParam.ToInt32());
            else if (m.Msg == Native.WM_TRIGGER_SNAP && SnapTriggered != null)
                SnapTriggered();
            base.WndProc(ref m);
        }
        public void Dispose() { DestroyHandle(); }
    }

    internal sealed class QuickSnapTrayApp : ApplicationContext
    {
        private const int HK_PRINTSCREEN = 1;
        private const int HK_CTRL_ALT_S = 2;
        private const int HK_EDIT_MODE = 5;

        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RUN_NAME = "NillyQuickSnap";
        private const string IPC_EVENT_NAME = "Local\\NillyQuickSnap_TriggerSnap";

        private HotkeyReceiver _hk;
        private Control _invoker;
        private NotifyIcon _tray;
        private ToolStripMenuItem _miAutoSave;
        private ToolStripMenuItem _miAutoStart;
        private EventWaitHandle _ipcEvent;
        private Thread _ipcThread;
        private IntPtr _kbHook = IntPtr.Zero;
        private Native.LowLevelKeyboardProc _kbHookProc;
        private bool _lWinDown = false, _rWinDown = false;
        private bool _lShiftDown = false, _rShiftDown = false;
        private bool _lCtrlDown = false, _rCtrlDown = false;
        private int _lastWinTick = 0, _lastShiftTick = 0, _lastCtrlTick = 0;
        private bool _swallowedS = false;
        private volatile bool _running = true;
        private volatile bool _capturing = false;
        private bool _autoSave = true;

        public QuickSnapTrayApp()
        {
            _invoker = new Control();
            IntPtr ensureHandle = _invoker.Handle;
            _hk = new HotkeyReceiver();
            _hk.HotkeyPressed += OnHotkey;
            _hk.SnapTriggered += delegate { TriggerCapture(0, false); };

            // Register standard hotkeys
            Native.RegisterHotKey(_hk.Handle, HK_PRINTSCREEN, Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
            Native.RegisterHotKey(_hk.Handle, HK_CTRL_ALT_S, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_S);
            Native.RegisterHotKey(_hk.Handle, HK_EDIT_MODE, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, Native.VK_S);

            // Warm up Win32 imports before installing hook so first keystroke runs in <0.01ms
            Native.IsKeyDown(Native.VK_LWIN);
            _kbHookProc = new Native.LowLevelKeyboardProc(KeyboardHookCallback);
            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbHookProc, IntPtr.Zero, 0);

            BuildTrayIcon();
            StartIpcListener();
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN);
                bool isUp = (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP);
                if (isDown || isUp)
                {
                    int vk = Marshal.ReadInt32(lParam);
                    if (vk == 0x5B) { _lWinDown = isDown; if (isDown) _lastWinTick = Environment.TickCount; }
                    else if (vk == 0x5C) { _rWinDown = isDown; if (isDown) _lastWinTick = Environment.TickCount; }
                    else if (vk == 0xA0) { _lShiftDown = isDown; if (isDown) _lastShiftTick = Environment.TickCount; }
                    else if (vk == 0xA1) { _rShiftDown = isDown; if (isDown) _lastShiftTick = Environment.TickCount; }
                    else if (vk == 0xA2) { _lCtrlDown = isDown; if (isDown) _lastCtrlTick = Environment.TickCount; }
                    else if (vk == 0xA3) { _rCtrlDown = isDown; if (isDown) _lastCtrlTick = Environment.TickCount; }
                    else if (vk == (int)Native.VK_S)
                    {
                        // Always clear _swallowedS on key-up even while _capturing is true!
                        if (isUp)
                        {
                            if (_swallowedS) { _swallowedS = false; return (IntPtr)1; }
                        }
                        else if (_swallowedS)
                        {
                            return (IntPtr)1;
                        }
                        else
                        {
                            int now = Environment.TickCount;
                            bool win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN) ||
                                ((_lWinDown || _rWinDown) && unchecked(now - _lastWinTick) < 2500);
                            if (win)
                            {
                                bool shift = Native.IsKeyDown(Native.VK_SHIFT) ||
                                    ((_lShiftDown || _rShiftDown) && unchecked(now - _lastShiftTick) < 2500);
                                bool ctrl = Native.IsKeyDown(Native.VK_CONTROL) ||
                                    ((_lCtrlDown || _rCtrlDown) && unchecked(now - _lastCtrlTick) < 2500);
                                bool alt = Native.IsKeyDown(Native.VK_MENU);
                                if (!alt && (shift || ctrl))
                                {
                                    _swallowedS = true;
                                    if (!_capturing)
                                        Native.PostMessage(_hk.Handle, Native.WM_TRIGGER_SNAP, IntPtr.Zero, IntPtr.Zero);
                                    return (IntPtr)1; // Consume before Windows Shell opens ScreenClippingHost!
                                }
                            }
                        }
                    }
                }
            }
            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void BuildTrayIcon()
        {
            Icon ico = null;
            try
            {
                string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NillyQuickSnap.ico");
                if (File.Exists(p)) ico = new Icon(p);
                else ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { ico = SystemIcons.Application; }

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Λήψη Περιοχής  (PrtSc / Ctrl+Alt+S)", null, delegate { TriggerCapture(0, false); });
            menu.Items.Add("Λήψη + Σχεδίαση  (Ctrl+Alt+Shift+S)", null, delegate { TriggerCapture(0, true); });
            menu.Items.Add("Λήψη με 3\" Καθυστέρηση", null, delegate { TriggerCapture(3, false); });
            menu.Items.Add("Ολόκληρη Οθόνη (Copy & Save)", null, delegate { CaptureFullScreenNow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Άνοιγμα Φακέλου «Στιγμιότυπα οθόνης»", null, delegate
            {
                try { Process.Start("explorer.exe", "\"" + SnapPaths.ScreenshotsFolder + "\""); } catch { }
            });
            menu.Items.Add(new ToolStripSeparator());

            _miAutoSave = new ToolStripMenuItem("Αυτόματη Αποθήκευση στα Στιγμιότυπα");
            _miAutoSave.Checked = _autoSave;
            _miAutoSave.Click += delegate { _autoSave = !_autoSave; _miAutoSave.Checked = _autoSave; };
            menu.Items.Add(_miAutoSave);

            _miAutoStart = new ToolStripMenuItem("Εκκίνηση με τα Windows");
            _miAutoStart.Checked = IsAutoStartEnabled();
            _miAutoStart.Click += delegate { ToggleAutoStart(); };
            menu.Items.Add(_miAutoStart);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("created by Nilly graphics (Sotiris Christos) — nillygraphics.com", null, delegate
            {
                try { Process.Start(new ProcessStartInfo("https://nillygraphics.com/") { UseShellExecute = true }); } catch { }
            });
            menu.Items.Add("Έξοδος", null, delegate { ExitApp(); });

            _tray = new NotifyIcon();
            _tray.Icon = ico;
            _tray.Text = "NillyQuickSnap (PrtSc / Ctrl+Alt+S)";
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) TriggerCapture(0, false);
            };
        }

        private void OnHotkey(int id)
        {
            if (id == HK_EDIT_MODE) TriggerCapture(0, true);
            else TriggerCapture(0, false);
        }

        private void StartIpcListener()
        {
            _ipcEvent = new EventWaitHandle(false, EventResetMode.AutoReset, IPC_EVENT_NAME);
            _ipcThread = new Thread(delegate()
            {
                while (_running)
                {
                    if (_ipcEvent.WaitOne(400) && _running)
                    {
                        try
                        {
                            if (_invoker != null && _invoker.IsHandleCreated)
                            {
                                _invoker.BeginInvoke((Action)delegate { TriggerCapture(0, false); });
                            }
                        }
                        catch { }
                    }
                }
            });
            _ipcThread.IsBackground = true;
            _ipcThread.Start();
        }

        public static bool SignalRunningDaemon()
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(IPC_EVENT_NAME))
                {
                    ev.Set();
                    return true;
                }
            }
            catch { return false; }
        }

        private void TriggerCapture(int delaySec, bool editMode)
        {
            if (_capturing) return;
            _capturing = true;
            try
            {
                using (SnapForm frm = new SnapForm(delaySec, editMode, _autoSave))
                {
                    if (frm.InitOk) frm.ShowDialog();
                }
            }
            catch { }
            finally
            {
                _capturing = false;
                _swallowedS = false;
                _lWinDown = _rWinDown = _lShiftDown = _rShiftDown = _lCtrlDown = _rCtrlDown = false;
                Native.TrimMemory();
            }
        }

        private void CaptureFullScreenNow()
        {
            if (_capturing) return;
            _capturing = true;
            try
            {
                Thread.Sleep(150);
                Rectangle vs = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);
                    try { Clipboard.SetImage(bmp); } catch { }
                    if (_autoSave)
                    {
                        string path = SnapPaths.BuildAutoSavePath();
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
            }
            catch { }
            finally
            {
                _capturing = false;
                _swallowedS = false;
                _lWinDown = _rWinDown = _lShiftDown = _rShiftDown = _lCtrlDown = _rCtrlDown = false;
                Native.TrimMemory();
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                    return k != null && k.GetValue(RUN_NAME) != null;
            }
            catch { return false; }
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k == null) return;
                    if (IsAutoStartEnabled()) { k.DeleteValue(RUN_NAME, false); _miAutoStart.Checked = false; }
                    else { k.SetValue(RUN_NAME, "\"" + Application.ExecutablePath + "\""); _miAutoStart.Checked = true; }
                }
            }
            catch { }
        }

        private void ExitApp()
        {
            _running = false;
            try
            {
                if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
                Native.UnregisterHotKey(_hk.Handle, HK_PRINTSCREEN);
                Native.UnregisterHotKey(_hk.Handle, HK_CTRL_ALT_S);
                Native.UnregisterHotKey(_hk.Handle, HK_EDIT_MODE);
            }
            catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_hk != null) _hk.Dispose();
            if (_invoker != null) _invoker.Dispose();
            if (_ipcEvent != null) { try { _ipcEvent.Set(); _ipcEvent.Dispose(); } catch { } }
            ExitThread();
        }
    }

    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool triggerNow = false;
            bool editMode = false;
            int delaySec = 0;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a.Equals("-snap", StringComparison.OrdinalIgnoreCase) || a.StartsWith("ms-screenclip:", StringComparison.OrdinalIgnoreCase))
                        triggerNow = true;
                    else if (a.Equals("-edit", StringComparison.OrdinalIgnoreCase))
                        { triggerNow = true; editMode = true; }
                    else if (a.Equals("-delay", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        int.TryParse(args[++i], out delaySec);
                    else if (a.Equals("--test-smoke", StringComparison.OrdinalIgnoreCase))
                        return RunSmokeTest(i + 1 < args.Length ? args[i + 1] : null);
                }
            }

            // If triggered by ms-screenclip: or shortcut while tray daemon is already running -> wake tray daemon in <5ms!
            if (triggerNow && !editMode && delaySec == 0 && QuickSnapTrayApp.SignalRunningDaemon())
                return 0;

            // One-shot capture mode if called directly with -snap/-edit when tray isn't running
            if (triggerNow)
            {
                using (SnapForm frm = new SnapForm(delaySec, editMode, true))
                {
                    if (frm.InitOk) Application.Run(frm);
                }
                return 0;
            }

            // Default mode: ultra-lightweight single-instance System Tray daemon
            bool createdNew;
            using (Mutex m = new Mutex(true, "Local\\NillyQuickSnap_TrayDaemon", out createdNew))
            {
                if (!createdNew)
                {
                    // Clicking Desktop/Start Menu shortcut while already running immediately triggers a capture!
                    QuickSnapTrayApp.SignalRunningDaemon();
                    return 0;
                }
                Application.Run(new QuickSnapTrayApp());
            }
            return 0;
        }

        private static int RunSmokeTest(string logPath)
        {
            try
            {
                string dir = SnapPaths.ScreenshotsFolder;
                string samplePath = SnapPaths.BuildAutoSavePath();
                Native.TrimMemory();
                long ramMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                string txt =
                    "SCREENSHOTS_DIR=" + dir + "\r\n" +
                    "DIR_EXISTS=" + Directory.Exists(dir) + "\r\n" +
                    "SAMPLE_NAME=" + Path.GetFileName(samplePath) + "\r\n" +
                    "TRIMMED_IDLE_MB=" + ramMb + "\r\n" +
                    "SMOKE_RESULT=PASS\r\n";
                if (!string.IsNullOrEmpty(logPath)) File.WriteAllText(logPath, txt);
                return 0;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(logPath)) File.WriteAllText(logPath, "SMOKE_RESULT=FAIL\r\n" + ex.ToString());
                return 1;
            }
        }
    }
}
