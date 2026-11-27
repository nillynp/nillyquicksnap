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
[assembly: AssemblyVersion("1.2.2.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]

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

        private AkButton MakeAkBtn(string text, AkKind kind, AkIcon icon, string tip, int width, EventHandler onClick)
        {
            AkButton b = new AkButton();
            b.Kind = kind;
            b.Icon = icon;
            b.Dark = true;
            b.Text = text;
            b.Width = width;
            b.Height = 28;
            b.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            b.Margin = new Padding(2, 4, 2, 4);
            b.Click += onClick;
            ToolTip tt = new ToolTip();
            tt.SetToolTip(b, tip);
            return b;
        }

        private void BuildToolbar()
        {
            _toolbar = new Panel();
            _toolbar.Height = 40;
            _toolbar.BackColor = Color.FromArgb(245, 29, 31, 32);
            _toolbar.Padding = new Padding(6, 2, 6, 2);
            _toolbar.Visible = false;
            _toolbar.Cursor = Cursors.Default;
            _toolbar.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen p = new Pen(Color.FromArgb(54, 57, 61), 1f))
                    pe.Graphics.DrawRectangle(p, 0, 0, _toolbar.Width - 1, _toolbar.Height - 1);
            };

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = false;
            flow.BackColor = Color.FromArgb(29, 31, 32);

            AddToolBtn(flow, DrawTool.Arrow,     "Βέλος",   AkIcon.External, "Βέλος (A)", 74);
            AddToolBtn(flow, DrawTool.Rect,      "Πλαίσιο", AkIcon.None,     "Πλαίσιο (R)", 66);
            AddToolBtn(flow, DrawTool.Pen,       "Στυλό",   AkIcon.Edit,     "Ελεύθερη σχεδίαση (P)", 74);
            AddToolBtn(flow, DrawTool.Highlight, "Marker",  AkIcon.None,     "Μαρκαδόρος (H)", 62);
            AddToolBtn(flow, DrawTool.Blur,      "Blur",    AkIcon.None,     "Θόλωμα / Απόκρυψη (B)", 50);

            Color[] palette = new Color[]
            {
                Color.FromArgb(252, 150, 31),
                Color.FromArgb(244, 63, 94),
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
                cb.Margin = new Padding(2, 7, 2, 3);
                cb.Cursor = Cursors.Hand;
                cb.TabStop = false;
                cb.Tag = col;
                cb.Click += delegate { _activeColor = col; UpdateToolbarStyles(); };
                _colorBtns.Add(cb);
                flow.Controls.Add(cb);
            }

            AkButton btnUndo = MakeAkBtn("Undo",    AkKind.Secondary,     AkIcon.Undo,  "Αναίρεση (Ctrl+Z)", 66, delegate { DoUndo(); });
            AkButton btnCopy = MakeAkBtn("OK",      AkKind.Primary,       AkIcon.Check, "Αντιγραφή + Αποθήκευση στα Στιγμιότυπα (Enter)", 62, delegate { DoCopyAutoSaveAndClose(); });
            AkButton btnSave = MakeAkBtn("Save As", AkKind.AccentOutline, AkIcon.Save,  "Αποθήκευση αλλού (Ctrl+S)", 82, delegate { DoSaveAsAndClose(); });
            AkButton btnEsc  = MakeAkBtn("",        AkKind.Danger,        AkIcon.Close, "Ακύρωση (Esc)", 30, delegate { this.Close(); });

            flow.Controls.Add(btnUndo);
            flow.Controls.Add(btnCopy);
            flow.Controls.Add(btnSave);

            int extraW = 0;
            string viewExe = FindSiblingExe("NillyViewPro", "NillyViewPro.exe");
            if (viewExe != null)
            {
                AkButton btnView = MakeAkBtn("View", AkKind.Secondary, AkIcon.External, "Άνοιγμα κατευθείαν στο NillyView Pro (Crop / Eyedropper / WebP)", 66, delegate { DoSendToSiblingAndClose(viewExe); });
                flow.Controls.Add(btnView);
                extraW += 70;
            }
            string batchExe = FindSiblingExe("NillyBatchPro", "NillyBatchPro.exe");
            if (batchExe != null)
            {
                AkButton btnBatch = MakeAkBtn("Batch", AkKind.Secondary, AkIcon.External, "Άνοιγμα στο NillyBatch Pro (Watermark / Social Resize)", 70, delegate { DoSendToSiblingAndClose(batchExe); });
                flow.Controls.Add(btnBatch);
                extraW += 74;
            }

            flow.Controls.Add(btnEsc);

            _toolbar.Controls.Add(flow);
            _toolbar.Width = 760 + extraW;
            this.Controls.Add(_toolbar);
            UpdateToolbarStyles();
        }

        private void AddToolBtn(FlowLayoutPanel flow, DrawTool t, string label, AkIcon icon, string tip, int width)
        {
            AkButton b = MakeAkBtn(label, AkKind.Secondary, icon, tip, width, delegate
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
                AkButton ab = kv.Value as AkButton;
                if (ab != null)
                {
                    ab.Active = (kv.Key == _activeTool);
                    ab.Invalidate();
                }
            }
            for (int i = 0; i < _colorBtns.Count; i++)
            {
                Color c = (Color)_colorBtns[i].Tag;
                _colorBtns[i].FlatAppearance.BorderColor = (c.ToArgb() == _activeColor.ToArgb()) ? Color.White : Color.FromArgb(54, 57, 61);
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

                using (Pen p = new Pen(Color.FromArgb(255, 252, 150, 31), 2)) g.DrawRectangle(p, activeRect);

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
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(242, 29, 31, 32)))
                    using (Pen bp = new Pen(Color.FromArgb(255, 252, 150, 31), 1.5f))
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(242, 244, 247)))
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
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(242, 29, 31, 32)))
                    using (Pen bp = new Pen(Color.FromArgb(255, 252, 150, 31), 1.2f))
                    using (SolidBrush fg = new SolidBrush(Color.FromArgb(242, 244, 247)))
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
            SuiteUpdater.AttachTray(_tray, _invoker, "nillyquicksnap", "NillyQuickSnap", "Install_NillyQuickSnap.ps1", delegate { ExitApp(); });
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

    internal static class SuiteUpdater
    {
        public static void AttachTray(System.Windows.Forms.NotifyIcon tray, System.Windows.Forms.Control invoker, string repoName, string pkgPrefix, string installerScript, System.Action onExit)
        {
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                try
                {
                    System.Threading.Thread.Sleep(3500);
                    string curTag, newTag;
                    if (!CheckForUpdate(repoName, out curTag, out newTag)) return;
                    if (invoker == null || invoker.IsDisposed || !invoker.IsHandleCreated) return;
                    invoker.BeginInvoke((System.Action)delegate
                    {
                        if (tray == null || tray.ContextMenuStrip == null) return;
                        System.Action runUpd = delegate
                        {
                            System.Threading.Thread uth = new System.Threading.Thread(delegate()
                            {
                                try { ApplySelfUpdate(repoName, pkgPrefix, installerScript, null, delegate { try { invoker.BeginInvoke(onExit); } catch { } }); }
                                catch { }
                            });
                            uth.IsBackground = true;
                            uth.Start();
                        };
                        System.Windows.Forms.ToolStripMenuItem mi = new System.Windows.Forms.ToolStripMenuItem("↑ Ενημέρωση σε " + newTag + " (έχεις " + curTag + ")");
                        mi.ForeColor = System.Drawing.Color.FromArgb(224, 118, 12);
                        mi.Click += delegate { mi.Enabled = false; mi.Text = "Ενημέρωση..."; runUpd(); };
                        tray.ContextMenuStrip.Items.Insert(0, new System.Windows.Forms.ToolStripSeparator());
                        tray.ContextMenuStrip.Items.Insert(0, mi);
                        tray.BalloonTipClicked += delegate { runUpd(); };
                        try { tray.ShowBalloonTip(5000, "NillyQuickSnap " + newTag, "Διαθέσιμη νέα έκδοση — κλικ για αυτόματη ενημέρωση.", System.Windows.Forms.ToolTipIcon.Info); } catch { }
                    });
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }
        private const string CATALOG_URL = "https://raw.githubusercontent.com/nillynp/nillyhub/main/catalog.json";

        private static bool TryParseVer(string raw, out System.Version ver)
        {
            ver = null;
            if (string.IsNullOrEmpty(raw)) return false;
            string s = raw.Trim();
            if (s.StartsWith("v", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            string[] p = s.Split('.');
            if (p.Length < 2 || p.Length > 4) return false;
            int maj, min, pat = 0;
            if (!int.TryParse(p[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out maj)) return false;
            if (!int.TryParse(p[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out min)) return false;
            if (p.Length >= 3 && !int.TryParse(p[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out pat)) return false;
            ver = new System.Version(maj, min, pat, 0);
            return true;
        }

        private static string FmtVer(System.Version v)
        {
            return v.Build > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "v{0}.{1}.{2}", v.Major, v.Minor, v.Build)
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "v{0}.{1}", v.Major, v.Minor);
        }

        public static bool CheckForUpdate(string repoName, out string curTag, out string newTag)
        {
            curTag = null; newTag = null;
            System.Version rawCur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Version curVer = new System.Version(rawCur.Major, rawCur.Minor, rawCur.Build >= 0 ? rawCur.Build : 0, 0);
            curTag = FmtVer(curVer);

            string suiteDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "NillySuite");
            string cachePath = System.IO.Path.Combine(suiteDir, ".nillyhub_catalog.json");
            string json = null;
            try
            {
                if (System.IO.File.Exists(cachePath))
                {
                    System.DateTime wt = System.IO.File.GetLastWriteTimeUtc(cachePath);
                    if ((System.DateTime.UtcNow - wt).TotalHours < 24.0)
                    {
                        string cached = System.IO.File.ReadAllText(cachePath, System.Text.Encoding.UTF8);
                        if (cached.IndexOf("\"latestVersion\"", System.StringComparison.Ordinal) >= 0) json = cached;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(json))
            {
                json = HttpGetText(CATALOG_URL, 5000);
                if (string.IsNullOrEmpty(json) || json.Length > 256 * 1024) return false;
                try
                {
                    System.IO.Directory.CreateDirectory(suiteDir);
                    System.IO.File.WriteAllText(cachePath, json, new System.Text.UTF8Encoding(false));
                }
                catch { }
            }

            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                json,
                "\"repo\"\\s*:\\s*\"" + System.Text.RegularExpressions.Regex.Escape(repoName) + "\"[^\\}]{0,500}?\"latestVersion\"\\s*:\\s*\"(v[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            System.Version remVer;
            if (!TryParseVer(m.Groups[1].Value, out remVer) || remVer <= curVer) return false;
            newTag = FmtVer(remVer);
            return true;
        }

        private static string HttpGetText(string url, int timeoutMs)
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | System.Net.SecurityProtocolType.Tls12; } catch { }
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "NillySuite-Updater/1.0 (https://nillygraphics.com/)";
            req.Accept = "application/vnd.github+json, application/json";
            req.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (System.IO.Stream st = resp.GetResponseStream())
            using (System.IO.StreamReader sr = new System.IO.StreamReader(st, System.Text.Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static string ExtractAssetBlock(string json, string assetName)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                json, "\"name\"\\s*:\\s*\"" + System.Text.RegularExpressions.Regex.Escape(assetName) + "\"");
            if (!m.Success) return null;
            int start = m.Index;
            int depth = 0;
            while (start > 0)
            {
                char c = json[start];
                if (c == '}') depth++;
                else if (c == '{') { if (depth == 0) break; depth--; }
                start--;
            }
            int end = start;
            depth = 0;
            bool inStr = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            return end > start ? json.Substring(start, end - start + 1) : null;
        }

        public static void ApplySelfUpdate(string repoName, string pkgPrefix, string installerScript, System.Action<string> onStatus, System.Action onExit)
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string appDir = System.IO.Path.GetDirectoryName(exePath);
            if (System.IO.Directory.Exists(System.IO.Path.Combine(appDir, ".git")))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/nillynp/" + repoName + "/releases/latest") { UseShellExecute = true });
                if (onStatus != null) onStatus("Άνοιξε το GitHub Release (φάκελος Git).");
                return;
            }

            if (onStatus != null) onStatus("Έλεγχος SHA-256 στο GitHub...");
            string relJson = HttpGetText("https://api.github.com/repos/nillynp/" + repoName + "/releases/latest", 12000);
            if (string.IsNullOrEmpty(relJson)) throw new System.IO.InvalidDataException("Κενή απόκριση GitHub.");
            if (System.Text.RegularExpressions.Regex.IsMatch(relJson, "\"draft\"\\s*:\\s*true") ||
                System.Text.RegularExpressions.Regex.IsMatch(relJson, "\"prerelease\"\\s*:\\s*true"))
                throw new System.IO.InvalidDataException("Μη σταθερή έκδοση.");

            System.Text.RegularExpressions.Match tm = System.Text.RegularExpressions.Regex.Match(relJson, "\"tag_name\"\\s*:\\s*\"(v[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)\"");
            if (!tm.Success) throw new System.IO.InvalidDataException("Μη έγκυρο tag έκδοσης.");
            string tag = tm.Groups[1].Value;

            System.Version curRaw = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            System.Version curVer = new System.Version(curRaw.Major, curRaw.Minor, curRaw.Build >= 0 ? curRaw.Build : 0, 0);
            System.Version remVer;
            if (!TryParseVer(tag, out remVer) || remVer <= curVer)
                throw new System.InvalidOperationException("Έχεις ήδη την τελευταία έκδοση.");

            string zipName = pkgPrefix + "-" + tag + "-Portable.zip";
            string expectedUrl = "https://github.com/nillynp/" + repoName + "/releases/download/" + tag + "/" + zipName;
            string blk = ExtractAssetBlock(relJson, zipName);
            if (string.IsNullOrEmpty(blk)) throw new System.IO.FileNotFoundException("Δεν βρέθηκε το Portable ZIP.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(blk, "\"state\"\\s*:\\s*\"uploaded\""))
                throw new System.IO.InvalidDataException("Το αρχείο δεν είναι έτοιμο.");
            System.Text.RegularExpressions.Match um = System.Text.RegularExpressions.Regex.Match(blk, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
            if (!um.Success || !string.Equals(um.Groups[1].Value, expectedUrl, System.StringComparison.Ordinal))
                throw new System.IO.InvalidDataException("Μη έγκυρο URL λήψης.");
            System.Text.RegularExpressions.Match dm = System.Text.RegularExpressions.Regex.Match(blk, "\"digest\"\\s*:\\s*\"sha256:([0-9a-f]{64})\"");
            if (!dm.Success) throw new System.IO.InvalidDataException("Απουσιάζει SHA-256.");
            string expectedSha = dm.Groups[1].Value;

            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NillyUpd-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tmpDir);
            string zipFile = System.IO.Path.Combine(tmpDir, "pkg.zip");

            if (onStatus != null) onStatus("Λήψη " + tag + "...");
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | System.Net.SecurityProtocolType.Tls12; } catch { }
            System.Net.HttpWebRequest dreq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(expectedUrl);
            dreq.UserAgent = "NillySuite-Updater/1.0 (https://nillygraphics.com/)";
            dreq.Timeout = 20000;
            dreq.ReadWriteTimeout = 30000;
            using (System.Net.HttpWebResponse dresp = (System.Net.HttpWebResponse)dreq.GetResponse())
            using (System.IO.Stream src = dresp.GetResponseStream())
            using (System.IO.FileStream dst = new System.IO.FileStream(zipFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                byte[] buf = new byte[16384];
                int n; long total = 0;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > 32L * 1024L * 1024L) throw new System.IO.InvalidDataException("Υπέρβαση μεγέθους ZIP.");
                    dst.Write(buf, 0, n);
                }
            }

            if (onStatus != null) onStatus("Επαλήθευση SHA-256...");
            string actualSha;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            using (System.IO.FileStream fs = System.IO.File.OpenRead(zipFile))
            {
                byte[] hash = sha.ComputeHash(fs);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                actualSha = sb.ToString();
            }
            if (!string.Equals(actualSha, expectedSha, System.StringComparison.OrdinalIgnoreCase))
            {
                try { System.IO.Directory.Delete(tmpDir, true); } catch { }
                throw new System.IO.InvalidDataException("Αποτυχία ελέγχου SHA-256.");
            }

            if (onStatus != null) onStatus("Εγκατάσταση " + tag + " & επανεκκίνηση...");
            string ps1 = System.IO.Path.Combine(tmpDir, "apply.ps1");
            string script =
                "param($myPid,$zip,$appDir,$instName,$exePath,$tag)\r\n" +
                "for($i=0;$i -lt 60;$i++){ if(-not (Get-Process -Id $myPid -ErrorAction SilentlyContinue)){break}; Start-Sleep -Milliseconds 150 }\r\n" +
                "Add-Type -AssemblyName System.IO.Compression.FileSystem\r\n" +
                "$za = [System.IO.Compression.ZipFile]::OpenRead($zip)\r\n" +
                "try {\r\n" +
                "  $pref = $null; $shared = $true\r\n" +
                "  foreach($e in $za.Entries){ $fn=$e.FullName -replace '\\\\','/'; $sl=$fn.IndexOf('/'); if($sl -le 0){$shared=$false;break}; $p=$fn.Substring(0,$sl+1); if(-not $pref){$pref=$p}elseif(-not $pref.Equals($p,[StringComparison]::OrdinalIgnoreCase)){$shared=$false;break} }\r\n" +
                "  $rootFull = [System.IO.Path]::GetFullPath($appDir).TrimEnd('\\') + '\\'\r\n" +
                "  foreach($e in $za.Entries){\r\n" +
                "    $rel = $e.FullName -replace '\\\\','/'\r\n" +
                "    if($shared -and $pref -and $rel.StartsWith($pref,[StringComparison]::OrdinalIgnoreCase)){ $rel=$rel.Substring($pref.Length) }\r\n" +
                "    if([string]::IsNullOrEmpty($rel) -or $rel.EndsWith('/') -or $rel.Contains(':')){ continue }\r\n" +
                "    $dst = [System.IO.Path]::GetFullPath((Join-Path $appDir ($rel -replace '/','\\')))\r\n" +
                "    if(-not $dst.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)){ continue }\r\n" +
                "    $par = Split-Path -Parent $dst; if($par){ New-Item -ItemType Directory -Force -Path $par | Out-Null }\r\n" +
                "    for($k=0;$k -lt 5;$k++){ try { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e,$dst,$true); break } catch { Start-Sleep -Milliseconds 250 } }\r\n" +
                "  }\r\n" +
                "} finally { $za.Dispose() }\r\n" +
                "$bak = $exePath + '.bak'\r\n" +
                "if(Test-Path $exePath){ Copy-Item -LiteralPath $exePath -Destination $bak -Force -ErrorAction SilentlyContinue; for($k=0;$k -lt 5;$k++){ try { Remove-Item -LiteralPath $exePath -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 250 } } }\r\n" +
                "Get-ChildItem (Join-Path $appDir 'desktop') -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object { $_.LastWriteTimeUtc = [DateTime]::UtcNow }\r\n" +
                "if($instName -and (Test-Path (Join-Path $appDir $instName))){ & (Join-Path $appDir $instName) -Silent 2>$null | Out-Null }\r\n" +
                "if(-not (Test-Path $exePath) -and (Test-Path $bak)){ Move-Item -LiteralPath $bak -Destination $exePath -Force -ErrorAction SilentlyContinue } else { Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue }\r\n" +
                "if($tag){ Set-Content -LiteralPath (Join-Path $appDir '.nilly_version') -Value $tag -Encoding UTF8 -ErrorAction SilentlyContinue }\r\n" +
                "if(Test-Path $exePath){ Start-Process -FilePath $exePath -WorkingDirectory $appDir }\r\n" +
                "Start-Sleep -Milliseconds 400\r\n" +
                "Remove-Item -LiteralPath (Split-Path $zip) -Recurse -Force -ErrorAction SilentlyContinue\r\n";
            System.IO.File.WriteAllText(ps1, script, new System.Text.UTF8Encoding(false));

            int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\" " +
                myPid + " \"" + zipFile + "\" \"" + appDir + "\" \"" + installerScript + "\" \"" + exePath + "\" \"" + tag + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            System.Diagnostics.Process.Start(psi);
            if (onExit != null) onExit();
        }
    }

    // ========================================================================
    // ARIAKIT-INSPIRED MINIMAL VECTOR UI KIT (Zero DLLs · 1.5px Stroke Icons)
    // ========================================================================
    internal enum AkKind { Primary, Secondary, Ghost, Tab, AccentOutline, Danger }

    internal enum AkIcon
    {
        None, Refresh, Sun, Moon, Search, Pin, Info, Play, Pause, Stop,
        Plus, Minus, Close, Up, Down, Folder, Copy, Trash, Check, Swap,
        Download, Link, Split, Pipette, Bolt, List, Filter, Shield, Lock, Unlock,
        Edit, Save, External, Bold, Italic, Undo, ChevLeft, ChevRight, Star
    }

    internal static class AkDraw
    {
        public static System.Drawing.Drawing2D.GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            float d = System.Math.Min(r * 2f, System.Math.Min(w, h));
            if (d <= 0.5f) { p.AddRectangle(new System.Drawing.RectangleF(x, y, w, h)); return p; }
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawIcon(System.Drawing.Graphics g, AkIcon icon, float cx, float cy, float sz, System.Drawing.Color color)
        {
            if (icon == AkIcon.None) return;
            System.Drawing.Drawing2D.SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float r = sz * 0.5f;
            using (System.Drawing.Pen pen = new System.Drawing.Pen(color, 1.5f))
            using (System.Drawing.SolidBrush br = new System.Drawing.SolidBrush(color))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                switch (icon)
                {
                    case AkIcon.Refresh:
                        g.DrawArc(pen, cx - r * 0.82f, cy - r * 0.82f, r * 1.64f, r * 1.64f, -55, 295);
                        g.DrawLine(pen, cx + r * 0.28f, cy - r * 0.85f, cx + r * 0.62f, cy - r * 0.52f);
                        g.DrawLine(pen, cx + r * 0.62f, cy - r * 0.52f, cx + r * 0.20f, cy - r * 0.42f);
                        break;
                    case AkIcon.Sun:
                        g.DrawEllipse(pen, cx - r * 0.42f, cy - r * 0.42f, r * 0.84f, r * 0.84f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * System.Math.PI / 4.0;
                            float x1 = cx + (float)(System.Math.Cos(a) * r * 0.68f);
                            float y1 = cy + (float)(System.Math.Sin(a) * r * 0.68f);
                            float x2 = cx + (float)(System.Math.Cos(a) * r * 0.94f);
                            float y2 = cy + (float)(System.Math.Sin(a) * r * 0.94f);
                            g.DrawLine(pen, x1, y1, x2, y2);
                        }
                        break;
                    case AkIcon.Moon:
                        g.DrawArc(pen, cx - r * 0.78f, cy - r * 0.78f, r * 1.56f, r * 1.56f, 40, 235);
                        g.DrawArc(pen, cx - r * 0.30f, cy - r * 0.92f, r * 1.32f, r * 1.32f, 115, 130);
                        break;
                    case AkIcon.Search:
                        g.DrawEllipse(pen, cx - r * 0.82f, cy - r * 0.82f, r * 1.24f, r * 1.24f);
                        g.DrawLine(pen, cx + r * 0.25f, cy + r * 0.25f, cx + r * 0.82f, cy + r * 0.82f);
                        break;
                    case AkIcon.Pin:
                        g.DrawLine(pen, cx - r * 0.48f, cy - r * 0.72f, cx + r * 0.48f, cy - r * 0.72f);
                        g.DrawLine(pen, cx - r * 0.34f, cy - r * 0.72f, cx - r * 0.56f, cy + r * 0.14f);
                        g.DrawLine(pen, cx + r * 0.34f, cy - r * 0.72f, cx + r * 0.56f, cy + r * 0.14f);
                        g.DrawLine(pen, cx - r * 0.66f, cy + r * 0.14f, cx + r * 0.66f, cy + r * 0.14f);
                        g.DrawLine(pen, cx, cy + r * 0.14f, cx, cy + r * 0.86f);
                        break;
                    case AkIcon.Info:
                        g.DrawEllipse(pen, cx - r * 0.85f, cy - r * 0.85f, r * 1.70f, r * 1.70f);
                        g.DrawLine(pen, cx, cy - r * 0.08f, cx, cy + r * 0.48f);
                        g.FillEllipse(br, cx - 1.1f, cy - r * 0.52f, 2.2f, 2.2f);
                        break;
                    case AkIcon.Play:
                        g.FillPolygon(br, new System.Drawing.PointF[] {
                            new System.Drawing.PointF(cx - r * 0.45f, cy - r * 0.68f),
                            new System.Drawing.PointF(cx + r * 0.68f, cy),
                            new System.Drawing.PointF(cx - r * 0.45f, cy + r * 0.68f)
                        });
                        break;
                    case AkIcon.Pause:
                        g.DrawLine(pen, cx - r * 0.36f, cy - r * 0.62f, cx - r * 0.36f, cy + r * 0.62f);
                        g.DrawLine(pen, cx + r * 0.36f, cy - r * 0.62f, cx + r * 0.36f, cy + r * 0.62f);
                        break;
                    case AkIcon.Stop:
                        g.FillRectangle(br, cx - r * 0.56f, cy - r * 0.56f, r * 1.12f, r * 1.12f);
                        break;
                    case AkIcon.Plus:
                        g.DrawLine(pen, cx - r * 0.68f, cy, cx + r * 0.68f, cy);
                        g.DrawLine(pen, cx, cy - r * 0.68f, cx, cy + r * 0.68f);
                        break;
                    case AkIcon.Minus:
                        g.DrawLine(pen, cx - r * 0.68f, cy, cx + r * 0.68f, cy);
                        break;
                    case AkIcon.Close:
                        g.DrawLine(pen, cx - r * 0.58f, cy - r * 0.58f, cx + r * 0.58f, cy + r * 0.58f);
                        g.DrawLine(pen, cx + r * 0.58f, cy - r * 0.58f, cx - r * 0.58f, cy + r * 0.58f);
                        break;
                    case AkIcon.Up:
                        g.DrawLine(pen, cx, cy + r * 0.68f, cx, cy - r * 0.64f);
                        g.DrawLine(pen, cx - r * 0.52f, cy - r * 0.14f, cx, cy - r * 0.66f);
                        g.DrawLine(pen, cx + r * 0.52f, cy - r * 0.14f, cx, cy - r * 0.66f);
                        break;
                    case AkIcon.Down:
                        g.DrawLine(pen, cx, cy - r * 0.68f, cx, cy + r * 0.64f);
                        g.DrawLine(pen, cx - r * 0.52f, cy + r * 0.14f, cx, cy + r * 0.66f);
                        g.DrawLine(pen, cx + r * 0.52f, cy + r * 0.14f, cx, cy + r * 0.66f);
                        break;
                    case AkIcon.Folder:
                        g.DrawPolygon(pen, new System.Drawing.PointF[] {
                            new System.Drawing.PointF(cx - r * 0.85f, cy - r * 0.56f),
                            new System.Drawing.PointF(cx - r * 0.22f, cy - r * 0.56f),
                            new System.Drawing.PointF(cx - r * 0.04f, cy - r * 0.34f),
                            new System.Drawing.PointF(cx + r * 0.85f, cy - r * 0.34f),
                            new System.Drawing.PointF(cx + r * 0.85f, cy + r * 0.64f),
                            new System.Drawing.PointF(cx - r * 0.85f, cy + r * 0.64f)
                        });
                        break;
                    case AkIcon.Copy:
                        g.DrawRectangle(pen, cx - r * 0.26f, cy - r * 0.26f, r * 1.02f, r * 1.02f);
                        g.DrawLine(pen, cx - r * 0.68f, cy + r * 0.30f, cx - r * 0.68f, cy - r * 0.68f);
                        g.DrawLine(pen, cx - r * 0.68f, cy - r * 0.68f, cx + r * 0.30f, cy - r * 0.68f);
                        break;
                    case AkIcon.Trash:
                        g.DrawLine(pen, cx - r * 0.76f, cy - r * 0.44f, cx + r * 0.76f, cy - r * 0.44f);
                        g.DrawLine(pen, cx - r * 0.28f, cy - r * 0.74f, cx + r * 0.28f, cy - r * 0.74f);
                        g.DrawRectangle(pen, cx - r * 0.54f, cy - r * 0.42f, r * 1.08f, r * 1.16f);
                        break;
                    case AkIcon.Check:
                        g.DrawLine(pen, cx - r * 0.68f, cy + r * 0.04f, cx - r * 0.14f, cy + r * 0.58f);
                        g.DrawLine(pen, cx - r * 0.14f, cy + r * 0.58f, cx + r * 0.74f, cy - r * 0.52f);
                        break;
                    case AkIcon.Swap:
                        g.DrawLine(pen, cx - r * 0.70f, cy - r * 0.32f, cx + r * 0.70f, cy - r * 0.32f);
                        g.DrawLine(pen, cx + r * 0.32f, cy - r * 0.68f, cx + r * 0.72f, cy - r * 0.32f);
                        g.DrawLine(pen, cx + r * 0.70f, cy + r * 0.34f, cx - r * 0.70f, cy + r * 0.34f);
                        g.DrawLine(pen, cx - r * 0.32f, cy + r * 0.70f, cx - r * 0.72f, cy + r * 0.34f);
                        break;
                    case AkIcon.Download:
                        g.DrawLine(pen, cx, cy - r * 0.72f, cx, cy + r * 0.28f);
                        g.DrawLine(pen, cx - r * 0.44f, cy - r * 0.12f, cx, cy + r * 0.32f);
                        g.DrawLine(pen, cx + r * 0.44f, cy - r * 0.12f, cx, cy + r * 0.32f);
                        g.DrawLine(pen, cx - r * 0.74f, cy + r * 0.72f, cx + r * 0.74f, cy + r * 0.72f);
                        break;
                    case AkIcon.Link:
                    case AkIcon.External:
                        g.DrawLine(pen, cx - r * 0.58f, cy + r * 0.58f, cx + r * 0.62f, cy - r * 0.62f);
                        g.DrawLine(pen, cx - r * 0.06f, cy - r * 0.64f, cx + r * 0.64f, cy - r * 0.64f);
                        g.DrawLine(pen, cx + r * 0.64f, cy - r * 0.64f, cx + r * 0.64f, cy + r * 0.06f);
                        break;
                    case AkIcon.Split:
                        g.DrawRectangle(pen, cx - r * 0.82f, cy - r * 0.64f, r * 1.64f, r * 1.28f);
                        g.DrawLine(pen, cx, cy - r * 0.64f, cx, cy + r * 0.64f);
                        break;
                    case AkIcon.Pipette:
                        g.DrawLine(pen, cx - r * 0.66f, cy + r * 0.66f, cx + r * 0.40f, cy - r * 0.40f);
                        g.DrawLine(pen, cx + r * 0.12f, cy - r * 0.68f, cx + r * 0.68f, cy - r * 0.12f);
                        break;
                    case AkIcon.Bolt:
                        g.DrawPolygon(pen, new System.Drawing.PointF[] {
                            new System.Drawing.PointF(cx + r * 0.14f, cy - r * 0.82f),
                            new System.Drawing.PointF(cx - r * 0.62f, cy + r * 0.08f),
                            new System.Drawing.PointF(cx - r * 0.04f, cy + r * 0.08f),
                            new System.Drawing.PointF(cx - r * 0.18f, cy + r * 0.82f),
                            new System.Drawing.PointF(cx + r * 0.62f, cy - r * 0.10f),
                            new System.Drawing.PointF(cx + r * 0.04f, cy - r * 0.10f)
                        });
                        break;
                    case AkIcon.List:
                        g.DrawLine(pen, cx - r * 0.72f, cy - r * 0.46f, cx + r * 0.72f, cy - r * 0.46f);
                        g.DrawLine(pen, cx - r * 0.72f, cy, cx + r * 0.72f, cy);
                        g.DrawLine(pen, cx - r * 0.72f, cy + r * 0.46f, cx + r * 0.72f, cy + r * 0.46f);
                        break;
                    case AkIcon.Filter:
                        g.DrawPolygon(pen, new System.Drawing.PointF[] {
                            new System.Drawing.PointF(cx - r * 0.78f, cy - r * 0.60f),
                            new System.Drawing.PointF(cx + r * 0.78f, cy - r * 0.60f),
                            new System.Drawing.PointF(cx + r * 0.16f, cy + r * 0.08f),
                            new System.Drawing.PointF(cx + r * 0.16f, cy + r * 0.68f),
                            new System.Drawing.PointF(cx - r * 0.16f, cy + r * 0.52f),
                            new System.Drawing.PointF(cx - r * 0.16f, cy + r * 0.08f)
                        });
                        break;
                    case AkIcon.Shield:
                        g.DrawPolygon(pen, new System.Drawing.PointF[] {
                            new System.Drawing.PointF(cx, cy - r * 0.82f),
                            new System.Drawing.PointF(cx + r * 0.72f, cy - r * 0.44f),
                            new System.Drawing.PointF(cx + r * 0.58f, cy + r * 0.34f),
                            new System.Drawing.PointF(cx, cy + r * 0.82f),
                            new System.Drawing.PointF(cx - r * 0.58f, cy + r * 0.34f),
                            new System.Drawing.PointF(cx - r * 0.72f, cy - r * 0.44f)
                        });
                        break;
                    case AkIcon.Lock:
                        g.DrawRectangle(pen, cx - r * 0.58f, cy - r * 0.08f, r * 1.16f, r * 0.84f);
                        g.DrawArc(pen, cx - r * 0.38f, cy - r * 0.76f, r * 0.76f, r * 0.84f, 180, 180);
                        break;
                    case AkIcon.Unlock:
                        g.DrawRectangle(pen, cx - r * 0.58f, cy - r * 0.08f, r * 1.16f, r * 0.84f);
                        g.DrawArc(pen, cx - r * 0.38f, cy - r * 0.76f, r * 0.76f, r * 0.84f, 180, 130);
                        break;
                    case AkIcon.Save:
                        g.DrawRectangle(pen, cx - r * 0.72f, cy - r * 0.72f, r * 1.44f, r * 1.44f);
                        g.DrawRectangle(pen, cx - r * 0.42f, cy - r * 0.72f, r * 0.72f, r * 0.48f);
                        g.DrawRectangle(pen, cx - r * 0.44f, cy + r * 0.14f, r * 0.88f, r * 0.58f);
                        break;
                    case AkIcon.Edit:
                        g.DrawLine(pen, cx - r * 0.62f, cy + r * 0.62f, cx + r * 0.56f, cy - r * 0.56f);
                        g.DrawLine(pen, cx + r * 0.24f, cy - r * 0.74f, cx + r * 0.74f, cy - r * 0.24f);
                        g.DrawLine(pen, cx - r * 0.72f, cy + r * 0.72f, cx - r * 0.34f, cy + r * 0.62f);
                        break;
                    case AkIcon.Undo:
                        g.DrawArc(pen, cx - r * 0.52f, cy - r * 0.34f, r * 1.24f, r * 1.04f, -135, 225);
                        g.DrawLine(pen, cx - r * 0.72f, cy - r * 0.22f, cx - r * 0.28f, cy - r * 0.62f);
                        g.DrawLine(pen, cx - r * 0.72f, cy - r * 0.22f, cx - r * 0.24f, cy + r * 0.10f);
                        break;
                    case AkIcon.ChevLeft:
                        g.DrawLine(pen, cx + r * 0.28f, cy - r * 0.58f, cx - r * 0.34f, cy);
                        g.DrawLine(pen, cx - r * 0.34f, cy, cx + r * 0.28f, cy + r * 0.58f);
                        break;
                    case AkIcon.ChevRight:
                        g.DrawLine(pen, cx - r * 0.28f, cy - r * 0.58f, cx + r * 0.34f, cy);
                        g.DrawLine(pen, cx + r * 0.34f, cy, cx - r * 0.28f, cy + r * 0.58f);
                        break;
                    case AkIcon.Star:
                        System.Drawing.PointF[] sp = new System.Drawing.PointF[10];
                        for (int k = 0; k < 10; k++)
                        {
                            double ang = -System.Math.PI / 2.0 + k * System.Math.PI / 5.0;
                            float rad = (k % 2 == 0) ? r * 0.85f : r * 0.38f;
                            sp[k] = new System.Drawing.PointF(cx + (float)(System.Math.Cos(ang) * rad), cy + (float)(System.Math.Sin(ang) * rad));
                        }
                        g.DrawPolygon(pen, sp);
                        break;
                }
            }
            g.SmoothingMode = oldSm;
        }
    }

    internal sealed class AkButton : System.Windows.Forms.Button
    {
        public AkKind Kind = AkKind.Secondary;
        public AkIcon Icon = AkIcon.None;
        public bool Active = false;
        public bool Dark = true;
        public int Radius = 6;
        private bool _hov = false;
        private bool _down = false;

        public AkButton()
        {
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                     System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                     System.Windows.Forms.ControlStyles.UserPaint |
                     System.Windows.Forms.ControlStyles.ResizeRedraw, true);
            FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = System.Windows.Forms.Cursors.Hand;
            TabStop = false;
            UseMnemonic = false;
        }

        protected override void OnMouseEnter(System.EventArgs e) { _hov = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { _hov = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            System.Drawing.Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            System.Drawing.Color parentBg = Parent != null ? Parent.BackColor : (Dark ? System.Drawing.Color.FromArgb(29, 31, 32) : System.Drawing.Color.FromArgb(248, 249, 250));
            g.Clear(parentBg);

            System.Drawing.Color accent = Dark ? System.Drawing.Color.FromArgb(252, 150, 31) : System.Drawing.Color.FromArgb(224, 118, 12);
            System.Drawing.Color accentHov = Dark ? System.Drawing.Color.FromArgb(255, 169, 64) : System.Drawing.Color.FromArgb(204, 102, 8);
            System.Drawing.Color surf = Dark ? System.Drawing.Color.FromArgb(39, 41, 43) : System.Drawing.Color.White;
            System.Drawing.Color surfHov = Dark ? System.Drawing.Color.FromArgb(50, 53, 56) : System.Drawing.Color.FromArgb(236, 239, 243);
            System.Drawing.Color border = Dark ? System.Drawing.Color.FromArgb(58, 62, 66) : System.Drawing.Color.FromArgb(210, 216, 224);
            System.Drawing.Color borderHov = Dark ? System.Drawing.Color.FromArgb(84, 89, 95) : System.Drawing.Color.FromArgb(176, 184, 196);
            System.Drawing.Color fgMain = Dark ? System.Drawing.Color.FromArgb(242, 244, 247) : System.Drawing.Color.FromArgb(26, 29, 33);
            System.Drawing.Color fgMuted = Dark ? System.Drawing.Color.FromArgb(154, 160, 168) : System.Drawing.Color.FromArgb(95, 104, 116);
            System.Drawing.Color fgDis = Dark ? System.Drawing.Color.FromArgb(92, 97, 104) : System.Drawing.Color.FromArgb(170, 178, 188);

            bool en = Enabled;
            System.Drawing.Color bg = parentBg;
            System.Drawing.Color bd = System.Drawing.Color.Transparent;
            System.Drawing.Color fg = en ? fgMain : fgDis;
            bool drawFill = false;

            if (Kind == AkKind.Primary)
            {
                drawFill = true;
                bg = en ? (_hov ? accentHov : accent) : (Dark ? System.Drawing.Color.FromArgb(56, 48, 38) : System.Drawing.Color.FromArgb(228, 224, 218));
                fg = en ? (Dark ? System.Drawing.Color.FromArgb(20, 21, 23) : System.Drawing.Color.White) : fgDis;
            }
            else if (Kind == AkKind.AccentOutline)
            {
                drawFill = true;
                bg = en ? (_hov ? (Dark ? System.Drawing.Color.FromArgb(52, 42, 30) : System.Drawing.Color.FromArgb(255, 243, 228)) : surf) : (Dark ? System.Drawing.Color.FromArgb(32, 34, 36) : System.Drawing.Color.FromArgb(244, 246, 248));
                bd = en ? accent : border;
                fg = en ? accent : fgDis;
            }
            else if (Kind == AkKind.Danger)
            {
                drawFill = true;
                System.Drawing.Color red = Dark ? System.Drawing.Color.FromArgb(240, 104, 118) : System.Drawing.Color.FromArgb(196, 40, 56);
                bg = en ? (_hov ? (Dark ? System.Drawing.Color.FromArgb(62, 30, 36) : System.Drawing.Color.FromArgb(254, 228, 231)) : (Dark ? System.Drawing.Color.FromArgb(46, 28, 32) : System.Drawing.Color.FromArgb(255, 240, 242))) : surf;
                bd = en ? (Dark ? System.Drawing.Color.FromArgb(116, 48, 58) : System.Drawing.Color.FromArgb(236, 154, 164)) : border;
                fg = en ? red : fgDis;
            }
            else if (Kind == AkKind.Tab)
            {
                drawFill = _hov && en;
                bg = surfHov;
                fg = en ? (Active ? fgMain : (_hov ? fgMain : fgMuted)) : fgDis;
            }
            else if (Kind == AkKind.Ghost)
            {
                drawFill = (Active || _hov) && en;
                bg = Active ? (Dark ? System.Drawing.Color.FromArgb(52, 42, 30) : System.Drawing.Color.FromArgb(255, 243, 228)) : surfHov;
                bd = Active ? accent : System.Drawing.Color.Transparent;
                fg = en ? (Active ? accent : (_hov ? fgMain : fgMuted)) : fgDis;
            }
            else // Secondary (Ariakit tactile elevated pill)
            {
                drawFill = true;
                bg = en ? (Active ? (Dark ? System.Drawing.Color.FromArgb(52, 42, 30) : System.Drawing.Color.FromArgb(255, 243, 228)) : (_hov ? surfHov : surf)) : (Dark ? System.Drawing.Color.FromArgb(32, 34, 36) : System.Drawing.Color.FromArgb(245, 247, 249));
                bd = en ? (Active ? accent : (_hov ? borderHov : border)) : (Dark ? System.Drawing.Color.FromArgb(44, 47, 50) : System.Drawing.Color.FromArgb(224, 228, 234));
                fg = en ? (Active ? accent : fgMain) : fgDis;
            }

            float ox = 0.5f, oy = (_down && en) ? 1.5f : 0.5f;
            float rw = Width - 1.5f, rh = Height - 1.5f - ((_down && en) ? 1f : 0f);
            if (drawFill || bd.A > 0)
            {
                using (System.Drawing.Drawing2D.GraphicsPath gp = AkDraw.RoundRect(ox, oy, rw, rh, Radius))
                {
                    if (drawFill) using (System.Drawing.SolidBrush br = new System.Drawing.SolidBrush(bg)) g.FillPath(br, gp);
                    if (bd.A > 0) using (System.Drawing.Pen p = new System.Drawing.Pen(bd, 1f)) g.DrawPath(p, gp);
                }
            }
            if (Kind == AkKind.Tab && Active)
            {
                using (System.Drawing.SolidBrush ab = new System.Drawing.SolidBrush(accent))
                    g.FillRectangle(ab, 6, Height - 3, System.Math.Max(8, Width - 12), 2.5f);
            }

            string txt = Text ?? "";
            float yShift = (_down && en) ? 1f : 0f;
            if (Icon != AkIcon.None && txt.Length == 0)
            {
                AkDraw.DrawIcon(g, Icon, Width * 0.5f, Height * 0.5f + yShift, 13.5f, fg);
            }
            else if (Icon != AkIcon.None)
            {
                System.Drawing.Size ts = System.Windows.Forms.TextRenderer.MeasureText(txt, Font, new System.Drawing.Size(Width, Height), System.Windows.Forms.TextFormatFlags.NoPrefix | System.Windows.Forms.TextFormatFlags.NoPadding);
                float iconW = 13f, gap = 6f;
                float totalW = iconW + gap + ts.Width;
                float startX = System.Math.Max(8f, (Width - totalW) * 0.5f);
                AkDraw.DrawIcon(g, Icon, startX + iconW * 0.5f, Height * 0.5f + yShift, 12.5f, fg);
                System.Drawing.Rectangle tr = new System.Drawing.Rectangle((int)(startX + iconW + gap), (int)yShift, System.Math.Max(10, Width - (int)(startX + iconW + gap) - 4), Height);
                System.Windows.Forms.TextRenderer.DrawText(g, txt, Font, tr, fg, System.Windows.Forms.TextFormatFlags.Left | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.NoPrefix | System.Windows.Forms.TextFormatFlags.EndEllipsis);
            }
            else
            {
                System.Drawing.Rectangle tr = new System.Drawing.Rectangle(4, (int)yShift, System.Math.Max(8, Width - 8), Height);
                System.Windows.Forms.TextRenderer.DrawText(g, txt, Font, tr, fg, System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter | System.Windows.Forms.TextFormatFlags.NoPrefix | System.Windows.Forms.TextFormatFlags.EndEllipsis);
            }
        }
    }

}
