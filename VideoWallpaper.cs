// Event Horizon Video Wallpaper — Lightweight native Windows video wallpaper engine.
//
// Media Foundation decodes video on the GPU (hardware H.264/HEVC) and converts it to RGB frames;
// GDI paints them into a window slotted in behind the desktop icons.
//
// Features:
//   - Zero third-party runtimes: compiles with Windows' built-in csc.exe (~120 KB exe)
//   - Auto-pause on fullscreen/maximized apps and on battery power
//   - Multi-monitor modes: Span, Duplicate (per-monitor), Primary only
//   - Scaling modes: Fill (aspect crop), Fit (letterbox), Stretch
//   - Variable playback speed (0.5x - 2x)
//   - Add any video file or folder (or drag-and-drop onto the control panel)
//   - Smooth star-streak warp transition between wallpapers
//
// Notes for Windows 11 24H2 and later, where the desktop icons (SHELLDLL_DefView) and the
// wallpaper (WorkerW) are both children of Progman:
//   - our window must be a layered child of Progman, placed between those two;
//   - the exe needs a manifest declaring Windows 8+ (layered child windows) and per-monitor DPI;
//   - after attaching, the window has to be hidden and shown again before DWM composes it.
//
// Build: csc /target:winexe /optimize+ /platform:x64 /win32manifest:VideoWallpaper.manifest /win32icon:VideoWallpaper.ico
//            /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll VideoWallpaper.cs ControlPanel.cs
// Run:   VideoWallpaper.exe ["<video to start with>"] ["<wallpaper to restore on exit>"] [--panel]
//        Launching it again while it runs opens the control panel.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFAttributes {
    void a03(); void a04(); void a05(); void a06();
    [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
    [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
    void a09(); void a10(); void a11(); void a12(); void a13(); void a14();
    void a15(); void a16(); void a17(); void a18(); void a19(); void a20();
    [PreserveSig] int SetUINT32([In] ref Guid key, int value);
    void a22(); void a23();
    [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
    void a25(); void a26();
    [PreserveSig] int SetUnknown([In] ref Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
    void a28(); void a29(); void a30(); void a31(); void a32();
}

[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFMediaType {
    void a03(); void a04(); void a05(); void a06();
    [PreserveSig] int GetUINT32([In] ref Guid key, out int value);
    [PreserveSig] int GetUINT64([In] ref Guid key, out long value);
    void a09(); void a10(); void a11(); void a12(); void a13(); void a14();
    void a15(); void a16(); void a17(); void a18(); void a19(); void a20();
    [PreserveSig] int SetUINT32([In] ref Guid key, int value);
    void a22(); void a23();
    [PreserveSig] int SetGUID([In] ref Guid key, [In] ref Guid value);
    void a25(); void a26(); void a27(); void a28(); void a29(); void a30(); void a31(); void a32();
}

[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFSample {
    void a03(); void a04(); void a05(); void a06(); void a07(); void a08(); void a09(); void a10();
    void a11(); void a12(); void a13(); void a14(); void a15(); void a16(); void a17(); void a18();
    void a19(); void a20(); void a21(); void a22(); void a23(); void a24(); void a25(); void a26();
    void a27(); void a28(); void a29(); void a30(); void a31(); void a32();
    void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime();
    [PreserveSig] int GetSampleDuration(out long duration);
    void SetSampleDuration(); void GetBufferCount(); void GetBufferByIndex();
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
}

[ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFMediaBuffer {
    [PreserveSig] int Lock(out IntPtr data, out int maxLength, out int currentLength);
    [PreserveSig] int Unlock();
}

[ComImport, Guid("eb533d5d-2db6-40f8-97a9-494692014f07"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFDXGIDeviceManager {
    void CloseDeviceHandle(); void GetVideoService(); void LockDevice(); void OpenDeviceHandle();
    [PreserveSig] int ResetDevice([MarshalAs(UnmanagedType.IUnknown)] object device, int resetToken);
}

[ComImport, Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ID3D10Multithread {
    void Enter(); void Leave();
    [PreserveSig] int SetMultithreadProtected([MarshalAs(UnmanagedType.Bool)] bool protect);
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public long value; public long pad; }

[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMFSourceReader {
    void GetStreamSelection();
    [PreserveSig] int SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
    void GetNativeMediaType();
    [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
    [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
    [PreserveSig] int SetCurrentPosition([In] ref Guid format, [In] ref PROPVARIANT position);
    [PreserveSig] int ReadSample(int stream, int flags, out int actualStream, out int streamFlags, out long timestamp, out IMFSample sample);
}

public class WallpaperForm : Form {
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) {
        IntPtr dc = e.Graphics.GetHdc();
        VideoWallpaper.PaintFrame(dc, ClientSize.Width, ClientSize.Height);
        e.Graphics.ReleaseHdc(dc);
    }
}

public enum ScaleMode { Fill, Fit, Stretch }
public enum MonitorMode { Span, Duplicate, Primary }
public enum PauseRule { Fullscreen, Maximized, Never }

public static class VideoWallpaper {
    const string ShowPanelEvent = "Local\\VideoWallpaper.EventHorizon.ShowPanel";
    static readonly string[] SupportedExts = { ".mp4", ".m4v", ".mov", ".wmv" };

    [STAThread]
    public static void Main(string[] args) {
        bool showPanel = args.Contains("--panel");
        args = args.Where(a => a != "--panel").ToArray();
        bool first;
        using (var single = new Mutex(true, "Local\\VideoWallpaper.EventHorizon", out first)) {
            if (!first) {
                try { using (var ev = EventWaitHandle.OpenExisting(ShowPanelEvent)) ev.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                return;
            }
            try { Run(args.Length > 0 ? Path.GetFullPath(args[0]) : null, args.Length > 1 ? args[1] : null, showPanel); }
            catch (Exception ex) { MessageBox.Show("The video wallpaper could not start: " + ex.Message, "Event Horizon Wallpaper"); }
        }
    }

    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr rect, bool erase);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("gdi32.dll")] static extern int SetDIBitsToDevice(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int startScan, int lines, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage, uint rop);
    [DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, int flags, IntPtr levels, int numLevels, int sdkVersion, out IntPtr device, out int level, out IntPtr context);
    [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IMFAttributes attrs, int size);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
    [DllImport("mfplat.dll")] static extern int MFCreateDXGIDeviceManager(out int resetToken, out IMFDXGIDeviceManager manager);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes attrs, out IMFSourceReader reader);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new Guid("0f81da2c-b537-4672-a8b2-a681b17307a3");
    static readonly Guid MF_SOURCE_READER_D3D_MANAGER = new Guid("ec822da2-e1e9-4b29-a0d8-563c719f5269");
    static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
    static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
    static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
    static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
    const int FIRST_VIDEO_STREAM = -4, ALL_STREAMS = -2, END_OF_STREAM = 0x2;

    static volatile bool stop, paused, autoPausedApp, autoPausedBattery;
    static volatile string requested;
    static double playbackSpeed = 1.0;
    static ScaleMode scaleMode = ScaleMode.Fill;
    static MonitorMode monitorMode = MonitorMode.Duplicate;
    static PauseRule pauseRule = PauseRule.Fullscreen;
    static bool pauseOnBattery = true;
    static int virtX, virtY;
    static Rectangle[] monitorRects = new Rectangle[0];

    static WallpaperForm form;
    static NotifyIcon tray;

    // The frame on screen: its buffer stays locked until the next frame replaces it, so painting needs no copy
    static readonly object gate = new object();
    static IMFMediaBuffer shownBuffer;
    static IntPtr shownData;
    static BITMAPINFOHEADER frameInfo;

    static void Check(int hr, string what) { if (hr < 0) throw new COMException(what + " failed", hr); }

    static bool EffectivelyPaused { get { return paused || autoPausedApp || autoPausedBattery; } }

    // Blits the current frame into target rectangle (dx, dy, dw, dh) honoring scaleMode
    static void BlitFrame(IntPtr dc, int dx, int dy, int dw, int dh, int fw, int fh) {
        if (dw <= 0 || dh <= 0 || fw <= 0 || fh <= 0) return;
        if (scaleMode == ScaleMode.Stretch) {
            if (fw == dw && fh == dh && dx == 0 && dy == 0)
                SetDIBitsToDevice(dc, 0, 0, dw, dh, 0, 0, 0, fh, shownData, ref frameInfo, 0);
            else
                StretchDIBits(dc, dx, dy, dw, dh, 0, 0, fw, fh, shownData, ref frameInfo, 0, 0x00CC0020);
            return;
        }
        double srcRatio = (double)fw / fh, dstRatio = (double)dw / dh;
        if (scaleMode == ScaleMode.Fill) {
            if (Math.Abs(srcRatio - dstRatio) < 0.002) {
                if (fw == dw && fh == dh) SetDIBitsToDevice(dc, dx, dy, dw, dh, 0, 0, 0, fh, shownData, ref frameInfo, 0);
                else StretchDIBits(dc, dx, dy, dw, dh, 0, 0, fw, fh, shownData, ref frameInfo, 0, 0x00CC0020);
            } else if (srcRatio > dstRatio) {
                int sw = Math.Max(1, (int)Math.Round(fh * dstRatio));
                int sx = (fw - sw) / 2;
                StretchDIBits(dc, dx, dy, dw, dh, sx, 0, sw, fh, shownData, ref frameInfo, 0, 0x00CC0020);
            } else {
                int sh = Math.Max(1, (int)Math.Round(fw / dstRatio));
                int sy = (fh - sh) / 2;
                StretchDIBits(dc, dx, dy, dw, dh, 0, sy, fw, sh, shownData, ref frameInfo, 0, 0x00CC0020);
            }
        } else {
            // Fit: letterbox with dark space background
            if (spaceBrush == IntPtr.Zero) spaceBrush = CreateSolidBrush(0x0B0705);
            var bg = new RECT { L = dx, T = dy, R = dx + dw, B = dy + dh };
            FillRect(dc, ref bg, spaceBrush);
            int rw = dw, rh = dh;
            if (srcRatio > dstRatio) rh = Math.Max(1, (int)Math.Round(dw / srcRatio));
            else rw = Math.Max(1, (int)Math.Round(dh * srcRatio));
            int rx = dx + (dw - rw) / 2, ry = dy + (dh - rh) / 2;
            StretchDIBits(dc, rx, ry, rw, rh, 0, 0, fw, fh, shownData, ref frameInfo, 0, 0x00CC0020);
        }
    }

    static void RefreshMonitors() {
        virtX = GetSystemMetrics(76);
        virtY = GetSystemMetrics(77);
        var screens = Screen.AllScreens;
        var list = new List<Rectangle>(screens.Length);
        foreach (var sc in screens)
            list.Add(new Rectangle(sc.Bounds.X - virtX, sc.Bounds.Y - virtY, sc.Bounds.Width, sc.Bounds.Height));
        monitorRects = list.ToArray();
    }

    public static void PaintFrame(IntPtr dc, int w, int h) {
        if (flight == Flight.Leave || flight == Flight.Cruise || flight == Flight.Arrive) { PaintFlight(dc, w, h); return; }
        if (flight == Flight.Settle && !LabelShowing() && backDC != IntPtr.Zero && backW == w && backH == h) {
            DrawScene(w, h, 0, false, false, 0, true);
            BitBlt(dc, 0, 0, w, h, backDC, 0, 0, 0x00CC0020);
            return;
        }
        lock (gate) {
            if (shownData == IntPtr.Zero) return;
            int fw = frameInfo.biWidth, fh = Math.Abs(frameInfo.biHeight);
            SetStretchBltMode(dc, 3);
            if (monitorMode == MonitorMode.Span || monitorRects.Length <= 1) {
                BlitFrame(dc, 0, 0, w, h, fw, fh);
            } else if (monitorMode == MonitorMode.Duplicate) {
                for (int i = 0; i < monitorRects.Length; i++) {
                    var r = monitorRects[i];
                    BlitFrame(dc, r.X, r.Y, r.Width, r.Height, fw, fh);
                }
            } else {
                // Primary only
                if (spaceBrush == IntPtr.Zero) spaceBrush = CreateSolidBrush(0x0B0705);
                var bg = new RECT { L = 0, T = 0, R = w, B = h };
                FillRect(dc, ref bg, spaceBrush);
                var p = Screen.PrimaryScreen.Bounds;
                BlitFrame(dc, p.X - virtX, p.Y - virtY, p.Width, p.Height, fw, fh);
            }
        }
    }

    // ---------- Auto-pause detection (fullscreen/maximized apps + battery) ----------
    static readonly HashSet<string> DesktopClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "DV2ControlHost", "Windows.UI.Core.CoreWindow"
    };

    static void PollAutoPause() {
        bool bat = pauseOnBattery && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        bool app = false;
        if (pauseRule != PauseRule.Never) {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != form.Handle && (panel == null || panel.IsDisposed || fg != panel.Handle)
                && IsWindowVisible(fg) && !IsIconic(fg)) {
                var sb = new StringBuilder(64);
                GetClassName(fg, sb, sb.Capacity);
                string cls = sb.ToString();
                if (!DesktopClasses.Contains(cls)) {
                    if (pauseRule == PauseRule.Maximized && IsZoomed(fg)) {
                        app = true;
                    } else {
                        RECT r;
                        if (GetWindowRect(fg, out r)) {
                            var sc = Screen.FromRectangle(new Rectangle(r.L, r.T, Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)));
                            var b = sc.Bounds;
                            // Fullscreen covers the entire monitor (including taskbar area)
                            if (r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom) app = true;
                        }
                    }
                }
            }
        }
        if (bat != autoPausedBattery || app != autoPausedApp) {
            autoPausedBattery = bat;
            autoPausedApp = app;
            UpdateTrayTip();
            Notify();
        }
    }

    // ---------- Flying between wallpapers ----------
    [DllImport("msimg32.dll")] static extern bool AlphaBlend(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint blend);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    enum Flight { None, Leave, Cruise, Arrive, Settle }
    const double LeaveSeconds = 1.6, CruiseSeconds = 0.7, ArriveSeconds = 1.7, SettleSeconds = 1.2;
    static Flight flight;
    static readonly Stopwatch flightClock = new Stopwatch(), frameClock = new Stopwatch();
    static string flightTarget;
    static volatile string shownPath;
    static System.Windows.Forms.Timer flightTicker;
    static IntPtr backDC, backBmp, backOldBmp, backBits, spaceBrush, featherDC, featherBmp;
    static int backW, backH;

    class Streak { public double X, Y, Depth, Cut, Bright; public int R, G, B, Thick; }
    static List<Streak> streaks;
    static volatile List<Streak> foundStars;
    static volatile int flightId;
    static int[] streakRow;
    static readonly Random rng = new Random();
    [DllImport("gdi32.dll")] static extern bool GdiFlush();

    static void MakeBack(IntPtr dc, int w, int h) {
        var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        backBmp = CreateDIBSection(dc, ref bi, 0, out backBits, IntPtr.Zero, 0);
        backDC = CreateCompatibleDC(dc); backOldBmp = SelectObject(backDC, backBmp);
        SetStretchBltMode(backDC, 3);
        backW = w; backH = h;
    }

    static void FreeFlightSurfaces() {
        if (backDC == IntPtr.Zero) return;
        SelectObject(backDC, backOldBmp); DeleteObject(backBmp); DeleteDC(backDC);
        backDC = IntPtr.Zero; backBits = IntPtr.Zero;
    }

    static List<Streak> FindStars(int w, int h) {
        int[] px; int fw, fh; bool topDown;
        lock (gate) {
            if (shownData == IntPtr.Zero) return new List<Streak>();
            fw = frameInfo.biWidth; fh = Math.Abs(frameInfo.biHeight); topDown = frameInfo.biHeight < 0;
            px = new int[fw * fh];
            Marshal.Copy(shownData, px, 0, px.Length);
        }
        double k = fh / 1080.0;
        int ring = Math.Max(4, (int)(7 * k)), diag = (int)(ring * 0.7), cell = Math.Max(6, (int)(12 * k));
        int labelX = (int)(fw * 0.818 - 760 * k), labelY = fh - (int)(64 * k);
        int[] rx = { ring, -ring, 0, 0, diag, diag, -diag, -diag }, ry = { 0, 0, ring, -ring, diag, -diag, diag, -diag };
        Func<int, int, int> at = (x, y) => px[(topDown ? y : fh - 1 - y) * fw + x];
        Func<int, int> lum = v => Math.Max(v & 255, Math.Max(v >> 8 & 255, v >> 16 & 255));
        var hist = new int[256];
        for (int y = 0; y < fh; y += 4) for (int x = 0; x < fw; x += 4) hist[lum(at(x, y))]++;
        int sky = Array.IndexOf(hist, hist.Max());
        var best = new Dictionary<int, int[]>();
        for (int y = ring * 2; y < fh - ring * 2; y += 2)
            for (int x = ring * 2; x < fw - ring * 2; x += 2) {
                int v = at(x, y), l = lum(v);
                if (l < 140 || (x > labelX && y > labelY)) continue;
                int limit = Math.Min(sky + 7, l * 30 / 100); bool alone = true;
                for (int i = 0; i < 16 && alone; i++) alone = lum(at(x + rx[i % 8] * (1 + i / 8), y + ry[i % 8] * (1 + i / 8))) < limit;
                if (!alone) continue;
                int key = y / cell * (fw / cell + 1) + x / cell; int[] had;
                if (!best.TryGetValue(key, out had) || had[2] < l) best[key] = new[] { x, y, l, v };
            }
        double sx = w / (double)fw, sy = h / (double)fh;
        var found = best.Values.OrderByDescending(s => s[2]).Take(450).Select(s => {
            int v = s[3], r = v >> 16 & 255, g = v >> 8 & 255, b = v & 255, m = Math.Max(1, s[2]);
            return new Streak { X = s[0] * sx, Y = s[1] * sy, R = r * 255 / m, G = g * 255 / m, B = b * 255 / m,
                Bright = s[2] / 255.0, Thick = (s[2] > 215 ? 2 : 1) * Math.Max(1, (int)Math.Round(h / 1080.0)),
                Depth = 0.55 + rng.NextDouble() * 0.9, Cut = rng.NextDouble() * 0.85 };
        }).ToList();
        int n = found.Count, extra = n == 0 ? 0 : Math.Max(160 - n, n / 3);
        for (int i = 0; i < extra; i++) {
            var o = found[rng.Next(n)];
            found.Add(new Streak { X = w + rng.NextDouble() * w, Y = rng.NextDouble() * h, R = o.R, G = o.G, B = o.B,
                Bright = o.Bright, Thick = o.Thick, Depth = 0.55 + rng.NextDouble() * 0.9, Cut = rng.NextDouble() * 0.85 });
        }
        return found;
    }

    static void DrawStreaks(int w, int h, double speed, double dt, double locked, double keep) {
        if (streaks == null || keep <= 0) return;
        GdiFlush();
        if (streakRow == null || streakRow.Length < w) streakRow = new int[w];
        double k = h / 1080.0;
        foreach (var s in streaks) {
            double depth = 1 + (s.Depth - 1) * locked, v = speed * depth, len = Math.Max(2 * k, v * 0.085);
            s.X -= v * dt;
            if (s.X + len < 0) { s.X = w + rng.NextDouble() * w * 0.4; s.Y = rng.NextDouble() * h; }
            double a = Math.Max(0, Math.Min(1, (keep - s.Cut) / 0.15)) * s.Bright;
            int x0 = Math.Max(0, (int)s.X), x1 = Math.Min(w, (int)(s.X + len) + 1);
            if (a <= 0.01 || x1 <= x0) continue;
            for (int row = (int)s.Y; row < (int)s.Y + s.Thick; row++) {
                if (row < 0 || row >= h) continue;
                IntPtr p = backBits + (row * w + x0) * 4; int n = x1 - x0;
                Marshal.Copy(p, streakRow, 0, n);
                for (int i = 0; i < n; i++) {
                    double f = (x0 + i - s.X) / len; f = f <= 0 ? a : a * Math.Pow(Math.Max(0, 1 - f), 1.6);
                    int c = streakRow[i], r = (int)(s.R * f), g = (int)(s.G * f), b = (int)(s.B * f);
                    streakRow[i] = Math.Max(c >> 16 & 255, r) << 16 | Math.Max(c >> 8 & 255, g) << 8 | Math.Max(c & 255, b);
                }
                Marshal.Copy(streakRow, 0, p, n);
            }
        }
    }

    static void FlyTo(string path) {
        bool showing;
        lock (gate) showing = shownData != IntPtr.Zero;
        if (!showing || EffectivelyPaused) { requested = path; return; }
        flightTarget = path;
        if (flight == Flight.Cruise || flight == Flight.Arrive) { requested = path; return; }
        if (flight == Flight.Leave) return;
        flight = Flight.Leave; streaks = null; foundStars = null;
        flightClock.Restart(); frameClock.Restart();
        int id = ++flightId, w = form.ClientSize.Width, h = form.ClientSize.Height;
        ThreadPool.QueueUserWorkItem(_ => {
            try { var found = FindStars(w, h); if (id == flightId) foundStars = found; }
            catch (Exception) { }
        });
        if (flightTicker == null) {
            flightTicker = new System.Windows.Forms.Timer { Interval = 15 };
            flightTicker.Tick += (s, e) => FlightTick();
        }
        flightTicker.Start();
    }

    static void FlightTick() {
        double t = flightClock.Elapsed.TotalSeconds;
        if (flight == Flight.Leave && t >= LeaveSeconds) {
            flight = Flight.Cruise; flightClock.Restart();
            requested = flightTarget;
        } else if (flight == Flight.Cruise && ((t >= CruiseSeconds && shownPath == flightTarget) || t > 6)) {
            flight = Flight.Arrive; flightClock.Restart();
        } else if (flight == Flight.Arrive && t >= ArriveSeconds) {
            flight = HasLabel(flightTarget) ? Flight.Settle : Flight.None; flightClock.Restart();
            streaks = null;
            if (flight == Flight.None) FreeFlightSurfaces();
        } else if (flight == Flight.Settle && t >= SettleSeconds) {
            flight = Flight.None;
            FreeFlightSurfaces();
        }
        if (flight == Flight.None) flightTicker.Stop();
        form.Invalidate();
    }

    static bool HasLabel(string path) {
        string f = Path.GetFileName(path ?? "").ToLowerInvariant();
        return f.StartsWith("sagittarius") || f.StartsWith("event-horizon") || f.StartsWith("quasar");
    }

    static bool LabelShowing() {
        double t = flightClock.Elapsed.TotalSeconds;
        return t > 1.0 || (t > 0.12 && t < 0.17) || (t > 0.3 && t < 0.34) || (t > 0.46 && t < 0.62) || (t > 0.72 && t < 0.9);
    }

    static void CoverLabel(IntPtr dc, int w, int h, int ox) {
        double k = h / 1080.0;
        int l = ox + (int)(w * 0.818 - 760 * k), r = ox + (int)(w * 0.818 + 32 * k), t = h - (int)(64 * k);
        BitBlt(dc, l, t, r - l, h - t, dc, l, t - (int)Math.Round(72 * k), 0x00CC0020);
    }

    static void EnsureFeather(IntPtr dc) {
        if (featherDC != IntPtr.Zero) return;
        var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = 512, biHeight = -1, biPlanes = 1, biBitCount = 32 };
        IntPtr bits;
        featherBmp = CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
        var px = new int[512];
        for (int i = 0; i < 256; i++) {
            double u = i / 255.0, a = 1 - u * u * (3 - 2 * u);
            px[i] = (int)Math.Round(a * 255) << 24 | (int)Math.Round(a * 5) << 16 | (int)Math.Round(a * 7) << 8 | (int)Math.Round(a * 11);
            px[511 - i] = px[i];
        }
        Marshal.Copy(px, 0, bits, 512);
        featherDC = CreateCompatibleDC(dc);
        SelectObject(featherDC, featherBmp);
    }

    static uint Blend(int alpha) { return (uint)((alpha & 255) << 16 | 1 << 24); }

    static void DrawScene(int w, int h, int ox, bool fadeLeft, bool fadeRight, int featherAlpha, bool hideLabel) {
        lock (gate) {
            if (shownData == IntPtr.Zero) return;
            int fw = frameInfo.biWidth, fh = Math.Abs(frameInfo.biHeight);
            BlitFrame(backDC, ox, 0, w, h, fw, fh);
        }
        if (hideLabel) CoverLabel(backDC, w, h, ox);
        int fwid = (int)(w * 0.32);
        if (featherAlpha > 0 && fadeLeft) AlphaBlend(backDC, ox, 0, fwid, h, featherDC, 0, 0, 256, 1, Blend(featherAlpha));
        if (featherAlpha > 0 && fadeRight) AlphaBlend(backDC, ox + w - fwid, 0, fwid, h, featherDC, 256, 0, 256, 1, Blend(featherAlpha));
    }

    static double Smooth(double a, double b, double x) { double k = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return k * k * (3 - 2 * k); }

    static void PaintFlight(IntPtr dc, int w, int h) {
        if (spaceBrush == IntPtr.Zero) spaceBrush = CreateSolidBrush(0x0B0705);
        double t = flightClock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.05, frameClock.Elapsed.TotalSeconds); frameClock.Restart();

        if (backDC == IntPtr.Zero || backW != w || backH != h) { FreeFlightSurfaces(); MakeBack(dc, w, h); }
        EnsureFeather(dc);
        var bg = new RECT { L = 0, T = 0, R = w, B = h };
        FillRect(backDC, ref bg, spaceBrush);
        double leaveTop = 1.35 * 3 / LeaveSeconds * w, arriveTop = 1.35 * 3 / ArriveSeconds * w;

        if (flight == Flight.Leave) {
            double u = Math.Min(1, t / LeaveSeconds);
            if (streaks == null && foundStars != null) {
                streaks = foundStars; foundStars = null;
                double moved = w * 1.35 * u * u * u;
                foreach (var s in streaks) s.X -= moved;
            }
            DrawScene(w, h, -(int)(w * 1.35 * u * u * u), false, true, (int)(255 * Smooth(0, 0.2, u)), false);
            DrawStreaks(w, h, leaveTop * u * u, dt, Smooth(0.3, 1, u), 1);
        } else if (flight == Flight.Cruise) {
            double c = Math.Min(1, t / CruiseSeconds);
            DrawStreaks(w, h, leaveTop + (arriveTop - leaveTop) * c, dt, 1, 1 - 0.25 * c);
        } else {
            double v = Math.Min(1, t / ArriveSeconds), e = 1 - Math.Pow(1 - v, 3), fade = 1 - Smooth(0.72, 1, v);
            DrawStreaks(w, h, arriveTop * (1 - v) * (1 - v), dt, 1, 0.75 * (1 - Smooth(0, 0.5, v)));
            if (shownPath == flightTarget)
                DrawScene(w, h, (int)(w * 1.35 * (1 - e)), true, false, (int)(255 * fade), HasLabel(flightTarget));
        }
        BitBlt(dc, 0, 0, w, h, backDC, 0, 0, 0x00CC0020);
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dc, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int bgr);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static void ShowFrame(IMFMediaBuffer buffer, IntPtr data, BITMAPINFOHEADER info, IntPtr hwnd, string path) {
        IMFMediaBuffer old;
        lock (gate) {
            shownPath = path;
            old = shownBuffer;
            shownBuffer = buffer;
            shownData = data;
            frameInfo = info;
        }
        if (old != null) { old.Unlock(); Marshal.ReleaseComObject(old); }
        InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    // ---------- Library and settings ----------
    internal class Clip { public string Name, Path; }

    static readonly string[] DownloadPrefixes = { "event-horizon", "saturn", "earth-moon", "quasar", "sagittarius" };
    static readonly int[] MinuteChoices = { 5, 10, 15, 30, 60, 120 };
    static readonly double[] SpeedChoices = { 0.5, 1.0, 1.5, 2.0 };
    static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoWallpaper", "settings.txt");
    static string pinned = "", lastPlayed = "";
    static int minutes = 30;
    static readonly List<string> extraDirs = new List<string>();
    static readonly List<string> extraFiles = new List<string>();

    static bool IsSupportedVideo(string path) {
        string ext = Path.GetExtension(path ?? "");
        return SupportedExts.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    // "saturn-1920x1080-30s-60fps.mp4" -> "Saturn"
    static string NiceName(string file) {
        string s = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"-\d+x\d+.*$", "").Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.Trim());
    }

    static List<Clip> Library() {
        var found = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dirs = new List<string> { baseDir, Path.Combine(baseDir, "wallpapers"), downloads };
        dirs.AddRange(extraDirs);

        foreach (string dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!Directory.Exists(dir)) continue;
            bool isDownloads = string.Equals(dir, downloads, StringComparison.OrdinalIgnoreCase);
            FileInfo[] files;
            try { files = new DirectoryInfo(dir).GetFiles(); } catch (Exception) { continue; }
            foreach (var f in files) {
                if (!IsSupportedVideo(f.Name)) continue;
                if (isDownloads && !DownloadPrefixes.Any(p => f.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                FileInfo have;
                string name = NiceName(f.Name);
                if (!found.TryGetValue(name, out have) || f.LastWriteTimeUtc > have.LastWriteTimeUtc) found[name] = f;
            }
        }
        foreach (string fp in extraFiles) {
            if (!File.Exists(fp) || !IsSupportedVideo(fp)) continue;
            var f = new FileInfo(fp);
            string name = NiceName(f.Name);
            FileInfo have;
            if (!found.TryGetValue(name, out have) || f.LastWriteTimeUtc > have.LastWriteTimeUtc) found[name] = f;
        }
        return found.OrderBy(p => p.Key).Select(p => new Clip { Name = p.Key, Path = p.Value.FullName }).ToList();
    }

    static void LoadSettings() {
        if (!File.Exists(SettingsPath)) return;
        foreach (string line in File.ReadAllLines(SettingsPath)) {
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            if (key == "pinned") pinned = value;
            else if (key == "current") lastPlayed = value;
            else if (key == "minutes") int.TryParse(value, out minutes);
            else if (key == "scale") { ScaleMode sm; if (Enum.TryParse(value, true, out sm)) scaleMode = sm; }
            else if (key == "monitor") { MonitorMode mm; if (Enum.TryParse(value, true, out mm)) monitorMode = mm; }
            else if (key == "pauseRule") { PauseRule pr; if (Enum.TryParse(value, true, out pr)) pauseRule = pr; }
            else if (key == "pauseBattery") { bool pb; if (bool.TryParse(value, out pb)) pauseOnBattery = pb; }
            else if (key == "speed") { double sp; if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out sp) && sp >= 0.25 && sp <= 4.0) playbackSpeed = sp; }
            else if (key == "extraDirs" && value.Length > 0) {
                foreach (var d in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    if (!extraDirs.Contains(d, StringComparer.OrdinalIgnoreCase)) extraDirs.Add(d);
            } else if (key == "extraFiles" && value.Length > 0) {
                foreach (var f in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    if (!extraFiles.Contains(f, StringComparer.OrdinalIgnoreCase)) extraFiles.Add(f);
            }
        }
        if (minutes < 1) minutes = 30;
    }

    static void SaveSettings() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllLines(SettingsPath, new[] {
                "pinned=" + pinned,
                "current=" + lastPlayed,
                "minutes=" + minutes,
                "scale=" + scaleMode,
                "monitor=" + monitorMode,
                "pauseRule=" + pauseRule,
                "pauseBattery=" + pauseOnBattery,
                "speed=" + playbackSpeed.ToString("0.##", CultureInfo.InvariantCulture),
                "extraDirs=" + string.Join("|", extraDirs.ToArray()),
                "extraFiles=" + string.Join("|", extraFiles.ToArray()),
            });
        } catch (IOException) { }
    }

    static void UpdateTrayTip() {
        if (tray == null) return;
        string suffix = paused ? " (paused)"
            : autoPausedBattery ? " (battery)"
            : autoPausedApp ? " (auto-paused)"
            : "";
        string tip = "Event Horizon: " + lastPlayed + suffix;
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
    }

    static void SwitchTo(Clip clip) {
        FlyTo(clip.Path);
        lastPlayed = clip.Name;
        UpdateTrayTip();
        SaveSettings();
        Notify();
    }

    static void Next() {
        var lib = Library();
        if (lib.Count == 0) return;
        int at = lib.FindIndex(c => string.Equals(c.Path, requested, StringComparison.OrdinalIgnoreCase));
        SwitchTo(lib[(at + 1) % lib.Count]);
    }

    static string Every(int m) { return m < 60 ? m + " minutes" : m == 60 ? "1 hour" : (m / 60) + " hours"; }

    // ---------- Actions shared by the tray menu and the control panel ----------
    static System.Windows.Forms.Timer rotate, autoPauseTimer;
    static DateTime rotateStarted;
    static ControlPanel panel;
    static Icon appIcon;
    internal static event Action Changed;
    static void Notify() { var c = Changed; if (c != null) c(); }

    internal static string Pinned { get { return pinned; } }
    internal static string NowPlaying { get { return lastPlayed; } }
    internal static int Minutes { get { return minutes; } }
    internal static bool IsPaused { get { return paused; } }
    internal static bool IsAutoPaused { get { return autoPausedApp || autoPausedBattery; } }
    internal static string AutoPauseReason {
        get {
            if (autoPausedBattery) return "Auto-paused · on battery";
            if (autoPausedApp) return pauseRule == PauseRule.Maximized ? "Auto-paused · maximized app" : "Auto-paused · fullscreen app";
            return null;
        }
    }
    internal static ScaleMode CurrentScale { get { return scaleMode; } }
    internal static MonitorMode CurrentMonitor { get { return monitorMode; } }
    internal static PauseRule CurrentPauseRule { get { return pauseRule; } }
    internal static bool PauseOnBattery { get { return pauseOnBattery; } }
    internal static double PlaybackSpeed { get { return playbackSpeed; } }
    internal static int[] Intervals { get { return MinuteChoices; } }
    internal static double[] Speeds { get { return SpeedChoices; } }
    internal static List<Clip> Clips() { return Library(); }

    internal static string NextChangeText() {
        double left = minutes - (DateTime.Now - rotateStarted).TotalMinutes;
        return left < 1 ? "under a minute" : left < 1.5 ? "1 minute" : Math.Round(left) + " minutes";
    }

    static void RestartRotation() { rotate.Stop(); rotate.Start(); rotateStarted = DateTime.Now; }

    internal static void Pin(Clip c) { pinned = c.Name; rotate.Stop(); SwitchTo(c); }
    internal static void RotateAll() { pinned = ""; SaveSettings(); RestartRotation(); Notify(); }
    internal static void SetMinutes(int m) { minutes = m; rotate.Interval = m * 60000; if (pinned == "") RestartRotation(); SaveSettings(); Notify(); }
    internal static void SetScale(ScaleMode m) { scaleMode = m; SaveSettings(); if (form != null) form.Invalidate(); Notify(); }
    internal static void SetMonitor(MonitorMode m) { monitorMode = m; RefreshMonitors(); SaveSettings(); if (form != null) form.Invalidate(); Notify(); }
    internal static void SetPauseRule(PauseRule r) { pauseRule = r; PollAutoPause(); SaveSettings(); Notify(); }
    internal static void TogglePauseOnBattery() { pauseOnBattery = !pauseOnBattery; PollAutoPause(); SaveSettings(); Notify(); }
    internal static void SetSpeed(double s) { playbackSpeed = s; SaveSettings(); Notify(); }
    internal static void NextWallpaper() {
        Next();
        if (pinned == "") RestartRotation(); else { pinned = lastPlayed; SaveSettings(); }
        Notify();
    }
    internal static void TogglePause() { paused = !paused; UpdateTrayTip(); Notify(); }
    internal static void Quit() { Application.Exit(); }

    internal static void AddPaths(string[] paths, bool playFirst) {
        if (paths == null || paths.Length == 0) return;
        Clip firstClip = null;
        foreach (string p in paths) {
            if (Directory.Exists(p)) {
                if (!extraDirs.Contains(p, StringComparer.OrdinalIgnoreCase)) extraDirs.Add(p);
                if (firstClip == null) {
                    var any = new DirectoryInfo(p).GetFiles().FirstOrDefault(f => IsSupportedVideo(f.Name));
                    if (any != null) firstClip = new Clip { Name = NiceName(any.Name), Path = any.FullName };
                }
            } else if (File.Exists(p) && IsSupportedVideo(p)) {
                if (!extraFiles.Contains(p, StringComparer.OrdinalIgnoreCase)) extraFiles.Add(p);
                if (firstClip == null) firstClip = new Clip { Name = NiceName(p), Path = p };
            }
        }
        SaveSettings();
        if (playFirst && firstClip != null) Pin(firstClip);
        else Notify();
    }

    internal static void BrowseAndAddVideo() {
        using (var dlg = new OpenFileDialog {
            Title = "Add video wallpaper",
            Filter = "Video files (*.mp4;*.m4v;*.mov;*.wmv)|*.mp4;*.m4v;*.mov;*.wmv|All files (*.*)|*.*",
            Multiselect = true
        }) {
            if (dlg.ShowDialog() == DialogResult.OK) AddPaths(dlg.FileNames, true);
        }
    }

    internal static void BrowseAndAddFolder() {
        using (var dlg = new FolderBrowserDialog { Description = "Choose a folder of video wallpapers" }) {
            if (dlg.ShowDialog() == DialogResult.OK) AddPaths(new[] { dlg.SelectedPath }, false);
        }
    }

    static Icon LoadIcon(int size) {
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VideoWallpaper.ico");
        try { if (File.Exists(ico)) return new Icon(ico, size, size); } catch (Exception) { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { return SystemIcons.Application; }
    }

    static void ShowPanel() {
        if (panel == null || panel.IsDisposed) panel = new ControlPanel(LoadIcon(32));
        panel.ShowNear();
    }

    static void TogglePanel() {
        if (panel != null && !panel.IsDisposed && panel.Visible) { panel.Hide(); return; }
        if ((DateTime.Now - ControlPanel.LastHidden).TotalMilliseconds < 300) return;
        ShowPanel();
    }

    public static void Run(string videoPath, string restoreWallpaper, bool showPanel) {
        Application.EnableVisualStyles();
        LoadSettings();
        for (int i = 0; i < 120 && FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero
                                && FindWindow("WorkerW", null) == IntPtr.Zero; i++)
            Thread.Sleep(500);

        RefreshMonitors();
        int w = GetSystemMetrics(78), h = GetSystemMetrics(79);

        form = new WallpaperForm {
            FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, BackColor = Color.Black,
            StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Size = new Size(w, h),
        };
        form.Show();
        Attach(form.Handle, w, h);

        SystemEvents.DisplaySettingsChanged += (s, e) => {
            if (form == null || form.IsDisposed) return;
            form.BeginInvoke((Action)(() => {
                RefreshMonitors();
                int nw = GetSystemMetrics(78), nh = GetSystemMetrics(79);
                form.Size = new Size(nw, nh);
                Reveal(form.Handle, nw, nh);
                form.Invalidate();
            }));
        };

        appIcon = LoadIcon(SystemInformation.SmallIconSize.Width);
        tray = new NotifyIcon { Icon = appIcon, Text = "Event Horizon Wallpaper", Visible = true };
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };

        var lib = Library();
        Clip start = lib.FirstOrDefault(c => c.Name == pinned) ?? lib.FirstOrDefault(c => c.Name == lastPlayed);
        if (start == null && videoPath != null && File.Exists(videoPath)) start = new Clip { Name = NiceName(videoPath), Path = videoPath };
        if (start == null) start = lib.FirstOrDefault();
        if (start == null) throw new Exception("No videos found. Place an MP4 next to VideoWallpaper.exe or pass one on the command line.");
        if (pinned != "" && start.Name != pinned) pinned = "";
        SwitchTo(start);

        rotate = new System.Windows.Forms.Timer { Interval = minutes * 60000 };
        rotate.Tick += (s, e) => { rotateStarted = DateTime.Now; if (!EffectivelyPaused) Next(); };
        if (pinned == "") RestartRotation();

        autoPauseTimer = new System.Windows.Forms.Timer { Interval = 500 };
        autoPauseTimer.Tick += (s, e) => PollAutoPause();
        autoPauseTimer.Start();
        PollAutoPause();

        var menu = new ContextMenuStrip();
        menu.Opening += (s, e) => {
            e.Cancel = false;
            menu.Items.Clear();
            var open = new ToolStripMenuItem("Open control panel");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += (a, b) => ShowPanel();
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());

            var choose = new ToolStripMenuItem("Wallpaper");
            var all = new ToolStripMenuItem("Rotate through all") { Checked = pinned == "" };
            all.Click += (a, b) => RotateAll();
            choose.DropDownItems.Add(all);
            choose.DropDownItems.Add(new ToolStripSeparator());
            foreach (var clip in Library()) {
                var c = clip;
                bool playing = string.Equals(c.Path, requested, StringComparison.OrdinalIgnoreCase);
                var item = new ToolStripMenuItem(c.Name.Replace("&", "&&") + (playing && pinned == "" ? "  (now)" : "")) { Checked = c.Name == pinned };
                item.Click += (a, b) => Pin(c);
                choose.DropDownItems.Add(item);
            }
            choose.DropDownItems.Add(new ToolStripSeparator());
            choose.DropDownItems.Add("Add video file...", null, (a, b) => BrowseAndAddVideo());
            choose.DropDownItems.Add("Add video folder...", null, (a, b) => BrowseAndAddFolder());
            menu.Items.Add(choose);

            var every = new ToolStripMenuItem("Change every") { Enabled = pinned == "" };
            foreach (int m in MinuteChoices) {
                int mm = m;
                var item = new ToolStripMenuItem(Every(m)) { Checked = m == minutes };
                item.Click += (a, b) => SetMinutes(mm);
                every.DropDownItems.Add(item);
            }
            menu.Items.Add(every);

            var scaleMenu = new ToolStripMenuItem("Scale");
            foreach (ScaleMode sm in Enum.GetValues(typeof(ScaleMode))) {
                var mode = sm;
                var item = new ToolStripMenuItem(sm.ToString()) { Checked = sm == scaleMode };
                item.Click += (a, b) => SetScale(mode);
                scaleMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(scaleMenu);

            var monMenu = new ToolStripMenuItem("Monitors");
            foreach (MonitorMode mm in Enum.GetValues(typeof(MonitorMode))) {
                var mode = mm;
                var item = new ToolStripMenuItem(mm.ToString()) { Checked = mm == monitorMode };
                item.Click += (a, b) => SetMonitor(mode);
                monMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(monMenu);

            var speedMenu = new ToolStripMenuItem("Speed");
            foreach (double sp in SpeedChoices) {
                double val = sp;
                var item = new ToolStripMenuItem(sp.ToString("0.##", CultureInfo.InvariantCulture) + "x") { Checked = Math.Abs(sp - playbackSpeed) < 0.01 };
                item.Click += (a, b) => SetSpeed(val);
                speedMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(speedMenu);

            var perfMenu = new ToolStripMenuItem("Auto-pause");
            foreach (PauseRule pr in Enum.GetValues(typeof(PauseRule))) {
                var rule = pr;
                string label = pr == PauseRule.Fullscreen ? "On fullscreen app" : pr == PauseRule.Maximized ? "On maximized or fullscreen" : "Never for apps";
                var item = new ToolStripMenuItem(label) { Checked = pr == pauseRule };
                item.Click += (a, b) => SetPauseRule(rule);
                perfMenu.DropDownItems.Add(item);
            }
            perfMenu.DropDownItems.Add(new ToolStripSeparator());
            var batItem = new ToolStripMenuItem("Pause on battery") { Checked = pauseOnBattery };
            batItem.Click += (a, b) => TogglePauseOnBattery();
            perfMenu.DropDownItems.Add(batItem);
            menu.Items.Add(perfMenu);

            menu.Items.Add("Next wallpaper", null, (a, b) => NextWallpaper());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(paused ? "Play" : "Pause", null, (a, b) => TogglePause());
            menu.Items.Add("Stop video wallpaper", null, (a, b) => Quit());
        };
        menu.Items.Add("...");
        tray.ContextMenuStrip = menu;

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowPanelEvent);
        ThreadPool.RegisterWaitForSingleObject(showEvent, (o, timedOut) => form.BeginInvoke((Action)ShowPanel), null, -1, false);
        if (showPanel) form.BeginInvoke((Action)ShowPanel);

        IntPtr hwnd = form.Handle;
        var player = new Thread(() => {
            try { Play(hwnd); }
            catch (Exception ex) {
                if (!stop) form.BeginInvoke((Action)(() => {
                    MessageBox.Show("The video wallpaper stopped: " + ex.Message, "Event Horizon Wallpaper");
                    Application.Exit();
                }));
            }
        });
        player.IsBackground = true;
        player.SetApartmentState(ApartmentState.MTA);
        player.Start();

        int ticks = 0;
        var reveal = new System.Windows.Forms.Timer { Interval = 1500 };
        reveal.Tick += (s, e) => { Reveal(hwnd, w, h); if (++ticks >= 6) reveal.Stop(); };
        reveal.Start();

        Application.Run();

        stop = true;
        player.Join(1000);
        tray.Visible = false;
        tray.Dispose();
        form.Close();
        if (!string.IsNullOrEmpty(restoreWallpaper)) SystemParametersInfo(0x14, 0, restoreWallpaper, 0x2);
    }

    static void Attach(IntPtr wnd, int w, int h) {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr r;
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out r);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out r);
        SetWindowLong(wnd, -16, (GetWindowLong(wnd, -16) & ~unchecked((int)0x80000000)) | 0x40000000);

        IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero) {
            SetWindowLong(wnd, -20, GetWindowLong(wnd, -20) | 0x80000);
            SetLayeredWindowAttributes(wnd, 0, 255, 0x2);
            SetParent(wnd, progman);
            SetWindowPos(wnd, defView, 0, 0, w, h, 0x10 | 0x40);
            IntPtr workerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (workerW != IntPtr.Zero) SetWindowPos(workerW, wnd, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        } else {
            IntPtr target = IntPtr.Zero;
            EnumWindows((top, l) => {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    target = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            if (target == IntPtr.Zero) throw new Exception("Could not find the desktop layer behind the icons.");
            SetParent(wnd, target);
            SetWindowPos(wnd, IntPtr.Zero, 0, 0, w, h, 0x10 | 0x40);
        }

        Reveal(wnd, w, h);
    }

    static void Reveal(IntPtr wnd, int w, int h) {
        IntPtr defView = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        uint z = defView == IntPtr.Zero ? 0x4u : 0u;
        ShowWindow(wnd, 0);
        ShowWindow(wnd, 8);
        SetWindowPos(wnd, defView, 0, 0, w - 1, h - 1, 0x10 | 0x40 | z);
        SetWindowPos(wnd, defView, 0, 0, w, h, 0x20 | 0x10 | 0x40 | z);
        RedrawWindow(wnd, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x100 | 0x400);
    }

    static IMFDXGIDeviceManager CreateGpuManager() {
        IntPtr device, context; int level;
        if (D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20 | 0x800, IntPtr.Zero, 0, 7, out device, out level, out context) < 0) return null;
        try {
            var d3d = Marshal.GetObjectForIUnknown(device);
            var mt = d3d as ID3D10Multithread;
            if (mt != null) mt.SetMultithreadProtected(true);
            int token; IMFDXGIDeviceManager manager;
            if (MFCreateDXGIDeviceManager(out token, out manager) < 0) return null;
            if (manager.ResetDevice(d3d, token) < 0) return null;
            return manager;
        } finally {
            Marshal.Release(device);
            if (context != IntPtr.Zero) Marshal.Release(context);
        }
    }

    static IMFSourceReader OpenReader(string path, bool gpu) {
        IMFAttributes attrs;
        Check(MFCreateAttributes(out attrs, 3), "MFCreateAttributes");
        Guid k = MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING;
        attrs.SetUINT32(ref k, 1);
        if (gpu) {
            var manager = CreateGpuManager();
            if (manager == null) return null;
            k = MF_SOURCE_READER_D3D_MANAGER; attrs.SetUnknown(ref k, manager);
            k = MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS; attrs.SetUINT32(ref k, 1);
        }
        IMFSourceReader reader;
        if (MFCreateSourceReaderFromURL(path, attrs, out reader) < 0) return null;
        reader.SetStreamSelection(ALL_STREAMS, false);
        reader.SetStreamSelection(FIRST_VIDEO_STREAM, true);

        IMFMediaType want;
        Check(MFCreateMediaType(out want), "MFCreateMediaType");
        Guid a = MF_MT_MAJOR_TYPE, b = MFMediaType_Video; want.SetGUID(ref a, ref b);
        a = MF_MT_SUBTYPE; b = MFVideoFormat_RGB32; want.SetGUID(ref a, ref b);
        if (reader.SetCurrentMediaType(FIRST_VIDEO_STREAM, IntPtr.Zero, want) < 0) return null;
        return reader;
    }

    static void Play(IntPtr hwnd) {
        Check(MFStartup(0x20070, 0), "MFStartup");
        timeBeginPeriod(1);
        while (!stop) {
            string path = requested;
            IMFSourceReader reader = OpenReader(path, true) ?? OpenReader(path, false);
            if (reader == null) {
                form.BeginInvoke((Action)(() => tray.ShowBalloonTip(5000, "Event Horizon Wallpaper", "Windows could not play " + Path.GetFileName(path), ToolTipIcon.Warning)));
                while (!stop && requested == path) Thread.Sleep(200);
                continue;
            }
            try { PlayOne(reader, path, hwnd); }
            finally { Marshal.ReleaseComObject(reader); }
        }
    }

    static void PlayOne(IMFSourceReader reader, string path, IntPtr hwnd) {
        IMFMediaType got;
        Check(reader.GetCurrentMediaType(FIRST_VIDEO_STREAM, out got), "Reading the frame format");
        long size; Guid a = MF_MT_FRAME_SIZE;
        Check(got.GetUINT64(ref a, out size), "Reading the frame size");
        int vw = (int)(size >> 32), vh = (int)(size & 0xFFFFFFFF);
        int stride; a = MF_MT_DEFAULT_STRIDE;
        if (got.GetUINT32(ref a, out stride) < 0) stride = vw * 4;
        var info = new BITMAPINFOHEADER { biSize = 40, biWidth = vw, biHeight = stride < 0 ? vh : -vh, biPlanes = 1, biBitCount = 32 };

        var clock = Stopwatch.StartNew();
        long lastTick = 0, now = 0;
        long loopBase = 0, lastEnd = 0;

        while (!stop && requested == path) {
            // If auto-paused or manually paused, sleep without decoding frames (near-0% CPU/GPU)
            while (!stop && requested == path && EffectivelyPaused) {
                lastTick = clock.Elapsed.Ticks;
                Thread.Sleep(100);
            }
            if (stop || requested != path) break;

            int actual, flags; long ts; IMFSample sample;
            Check(reader.ReadSample(FIRST_VIDEO_STREAM, 0, out actual, out flags, out ts, out sample), "Decoding");

            if ((flags & END_OF_STREAM) != 0 || sample == null) {
                if (sample != null) Marshal.ReleaseComObject(sample);
                loopBase += lastEnd;
                lastEnd = 0;
                var start = new PROPVARIANT { vt = 20, value = 0 };
                Guid timeFormat = Guid.Empty;
                Check(reader.SetCurrentPosition(ref timeFormat, ref start), "Looping");
                continue;
            }

            long dur; if (sample.GetSampleDuration(out dur) < 0 || dur <= 0) dur = 166667;
            lastEnd = Math.Max(lastEnd, ts + dur);
            long due = loopBase + ts;

            while (!stop && requested == path) {
                long tick = clock.Elapsed.Ticks;
                bool ep = EffectivelyPaused;
                if (!ep) now += (long)((tick - lastTick) * playbackSpeed);
                lastTick = tick;
                if (ep) { Thread.Sleep(100); continue; }
                long left = due - now;
                if (left <= 0) break;
                Thread.Sleep((int)Math.Max(1, Math.Min(left / 10000 - 1, 50)));
            }
            if (now - due > 5000000) now = due;

            IMFMediaBuffer buffer;
            if (sample.ConvertToContiguousBuffer(out buffer) >= 0) {
                IntPtr data; int max, len;
                if (buffer.Lock(out data, out max, out len) >= 0) ShowFrame(buffer, data, info, hwnd, path);
                else Marshal.ReleaseComObject(buffer);
            }
            Marshal.ReleaseComObject(sample);
        }
    }
}
