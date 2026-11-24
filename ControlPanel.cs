// The control panel: a pixel-styled window that pops up above the tray icon.
// Supports drag-and-drop for any MP4/MOV/WMV file or folder, auto-pause rules, scaling, multi-monitor, and speed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class ControlPanel : Form {
    public static DateTime LastHidden;

    static Color Hex(string s) { return ColorTranslator.FromHtml(s); }
    static readonly Color Bg = Hex("#0d0b12"), Raised = Hex("#1b1722"), HoverRow = Hex("#1d1926"), Line = Hex("#3a3346"),
        Ink = Hex("#f3ece4"), Muted = Hex("#9a8f99"), Accent = Hex("#f5a04a"), AccentHi = Hex("#ffc27d"), AccentDeep = Hex("#a8561c"),
        Danger = Hex("#e8675f"), Ok = Hex("#7fd88a");
    static readonly Color[] Disk = "#3a0d08 #6e170b #b1300f #e05617 #f5803a #f9aa72 #fcd2ac #fff3e6".Split(' ').Select(Hex).ToArray();

    static Color[] Swatch(string name) {
        string n = name.ToLowerInvariant();
        string p = n.Contains("earth") ? "#1d5a9c #4a7a2e #f7fbff #2f7cc0 #c9d6e3"
                 : n.Contains("saturn") ? "#7d5426 #c99a55 #efe5c8 #a6763a #4d4232"
                 : n.Contains("quasar") ? "#203584 #6f93e6 #eef3ff #2f4fae #9cb6f3"
                 : n.Contains("sagittarius") ? "#27b6cc #8648d8 #fff2ff #de4db3 #5a29a2"
                 : n.Contains("horizon") || n.Contains("ton") ? "#b1300f #f5803a #fff3e6 #e05617 #6e170b"
                 : "#4a4452 #6e6878 #c9c2d0 #5a5462 #3a3442";
        return p.Split(' ').Select(Hex).ToArray();
    }

    class Hit { public Rectangle R; public Action Do; public bool Enabled = true; public bool Drag; }

    readonly float k;
    readonly PrivateFontCollection fonts = new PrivateFontCollection();
    readonly Font titleFont, headFont, buttonFont, chipFont, nameFont, bodyFont, smallFont;
    readonly Timer anim = new Timer { Interval = 66 };
    readonly List<Hit> hits = new List<Hit>();
    readonly Action onChanged;
    List<VideoWallpaper.Clip> clips = new List<VideoWallpaper.Clip>();
    int hover = -1, ticks, scrollOffset;
    bool dragOver;
    double t;

    const int Wd = 380, Pad = 18, BannerH = 100, RowH = 34, MaxVisibleClips = 6;

    int S(float v) { return (int)Math.Round(v * k); }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    public ControlPanel(Icon icon) {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true; TopMost = true; KeyPreview = true; AllowDrop = true;
        Text = "Event Horizon Wallpaper"; Icon = icon; BackColor = Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        using (var g = CreateGraphics()) k = g.DpiX / 96f;

        FontFamily pixel = null;
        foreach (string f in new[] { "Silkscreen-Regular.ttf", "Silkscreen-Bold.ttf" }) {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, f);
            if (File.Exists(path)) try { fonts.AddFontFile(path); } catch { }
        }
        if (fonts.Families.Length > 0) pixel = fonts.Families[0];
        int px8 = Math.Max(1, (int)Math.Round(k * 2)) * 8;
        titleFont = pixel != null ? new Font(pixel, px8 + 8, FontStyle.Regular, GraphicsUnit.Pixel) : new Font("Consolas", S(20), FontStyle.Bold, GraphicsUnit.Pixel);
        headFont = pixel != null ? new Font(pixel, px8, FontStyle.Regular, GraphicsUnit.Pixel) : new Font("Consolas", S(12), FontStyle.Bold, GraphicsUnit.Pixel);
        buttonFont = headFont;
        chipFont = pixel != null ? new Font(pixel, Math.Max(8, px8 - 4), FontStyle.Regular, GraphicsUnit.Pixel) : new Font("Consolas", S(11), FontStyle.Bold, GraphicsUnit.Pixel);
        nameFont = new Font("Segoe UI Semibold", S(16.5f), GraphicsUnit.Pixel);
        bodyFont = new Font("Segoe UI", S(13.5f), GraphicsUnit.Pixel);
        smallFont = new Font("Segoe UI", S(12f), GraphicsUnit.Pixel);

        anim.Tick += (s, e) => {
            t += 0.066;
            if (++ticks % 15 == 0) Invalidate(); else Invalidate(new Rectangle(0, 0, ClientSize.Width, S(BannerH)));
        };
        onChanged = () => { if (Visible) { clips = VideoWallpaper.Clips(); ClampScroll(); Invalidate(); } };
        VideoWallpaper.Changed += onChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) { VideoWallpaper.Changed -= onChanged; anim.Dispose(); fonts.Dispose(); }
        base.Dispose(disposing);
    }

    void ClampScroll() {
        int max = Math.Max(0, clips.Count - MaxVisibleClips);
        if (scrollOffset > max) scrollOffset = max;
        if (scrollOffset < 0) scrollOffset = 0;
    }

    public void ShowNear() {
        clips = VideoWallpaper.Clips();
        ClampScroll();
        int visibleRows = Math.Min(clips.Count, MaxVisibleClips) + 1;
        int h = BannerH + 92 + 28 + RowH * visibleRows
              + 12 + 26 + 30   // Change every
              + 12 + 26 + 28 + 6 + 28 + 6 + 28 // Engine (Scale, Monitor/Speed, Auto-pause)
              + 14 + 38 + 8 + 34 + Pad;
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int ph = Math.Min(S(h), wa.Height - S(24));
        ClientSize = new Size(S(Wd), ph);
        Region = new Region(StepPath(new Rectangle(Point.Empty, ClientSize), S(3)));
        Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
        Show();
        Activate();
    }

    protected override void OnVisibleChanged(EventArgs e) {
        base.OnVisibleChanged(e);
        anim.Enabled = Visible;
        if (!Visible) LastHidden = DateTime.Now;
    }

    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Hide(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); base.OnKeyDown(e); }
    protected override void OnFormClosing(FormClosingEventArgs e) {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        if (clips.Count > MaxVisibleClips) {
            scrollOffset += e.Delta > 0 ? -1 : 1;
            ClampScroll();
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    protected override void OnDragEnter(DragEventArgs e) {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
            e.Effect = DragDropEffects.Copy;
            dragOver = true;
            Invalidate();
        }
        base.OnDragEnter(e);
    }

    protected override void OnDragLeave(EventArgs e) {
        dragOver = false;
        Invalidate();
        base.OnDragLeave(e);
    }

    protected override void OnDragDrop(DragEventArgs e) {
        dragOver = false;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null && files.Length > 0) VideoWallpaper.AddPaths(files, true);
        clips = VideoWallpaper.Clips();
        Invalidate();
        base.OnDragDrop(e);
    }

    int HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e) {
        int h = HitAt(e.Location);
        bool live = h >= 0 && hits[h].Enabled && !hits[h].Drag;
        Cursor = live ? Cursors.Hand : Cursors.Default;
        if ((live ? h : -1) != hover) { hover = live ? h : -1; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e) {
        int h = HitAt(e.Location);
        if (e.Button == MouseButtons.Left && h >= 0 && hits[h].Drag) {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseClick(MouseEventArgs e) {
        int h = HitAt(e.Location);
        if (e.Button == MouseButtons.Left && h >= 0 && hits[h].Enabled && !hits[h].Drag && hits[h].Do != null) {
            hits[h].Do();
            clips = VideoWallpaper.Clips();
            ClampScroll();
            Invalidate();
        }
        base.OnMouseClick(e);
    }

    static GraphicsPath StepPath(Rectangle r, int st) {
        var p = new GraphicsPath();
        p.AddPolygon(new[] {
            new Point(r.Left + 2 * st, r.Top), new Point(r.Right - 2 * st, r.Top), new Point(r.Right - 2 * st, r.Top + st),
            new Point(r.Right - st, r.Top + st), new Point(r.Right - st, r.Top + 2 * st), new Point(r.Right, r.Top + 2 * st),
            new Point(r.Right, r.Bottom - 2 * st), new Point(r.Right - st, r.Bottom - 2 * st), new Point(r.Right - st, r.Bottom - st),
            new Point(r.Right - 2 * st, r.Bottom - st), new Point(r.Right - 2 * st, r.Bottom), new Point(r.Left + 2 * st, r.Bottom),
            new Point(r.Left + 2 * st, r.Bottom - st), new Point(r.Left + st, r.Bottom - st), new Point(r.Left + st, r.Bottom - 2 * st),
            new Point(r.Left, r.Bottom - 2 * st), new Point(r.Left, r.Top + 2 * st), new Point(r.Left + st, r.Top + 2 * st),
            new Point(r.Left + st, r.Top + st), new Point(r.Left + 2 * st, r.Top + st),
        });
        return p;
    }

    static Color Mix(Color a, Color b, float f) {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
    }

    void PixelText(Graphics g, string s, Font f, Color c, float x, float y, bool shadow = false) {
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        if (shadow) using (var b = new SolidBrush(Color.FromArgb(200, 5, 4, 9))) g.DrawString(s, f, b, x + S(2), y + S(2));
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
    }

    SizeF PixelMeasure(Graphics g, string s, Font f) {
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic);
    }

    void PixelButton(Graphics g, Rectangle r, string text, Color face, Color edge, Color ink, bool hot, bool enabled, Action act, Font font = null) {
        if (!enabled) { face = Mix(face, Bg, 0.6f); edge = Mix(edge, Bg, 0.6f); ink = Mix(ink, Bg, 0.55f); }
        else if (hot) { face = Mix(face, Color.White, 0.14f); }
        int e = S(3), n = S(2);
        using (var b = new SolidBrush(edge)) g.FillRectangle(b, r);
        using (var b = new SolidBrush(face)) g.FillRectangle(b, r.X, r.Y, r.Width - e, r.Height - e);
        using (var b = new SolidBrush(Bg)) {
            g.FillRectangle(b, r.X, r.Y, n, n); g.FillRectangle(b, r.Right - n, r.Y, n, n);
            g.FillRectangle(b, r.X, r.Bottom - n, n, n); g.FillRectangle(b, r.Right - n, r.Bottom - n, n, n);
        }
        Font f = font ?? buttonFont;
        var sz = PixelMeasure(g, text, f);
        PixelText(g, text, f, ink, r.X + (r.Width - e - sz.Width) / 2, r.Y + (r.Height - e - sz.Height) / 2 + S(1));
        hits.Add(new Hit { R = r, Do = act, Enabled = enabled });
    }

    void Header(Graphics g, string text, int y, string note = null) {
        PixelText(g, text, headFont, Accent, S(Pad), y);
        if (note != null) {
            var w = PixelMeasure(g, text, headFont).Width;
            TextRenderer.DrawText(g, note, smallFont, new Point((int)(S(Pad) + w + S(10)), y + S(1)), Muted, TextFormatFlags.NoPadding);
        }
    }

    void Tile(Graphics g, Rectangle r, Color c, bool bevel) {
        using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        if (bevel && r.Width >= S(6)) using (var p = new Pen(Color.FromArgb(90, 7, 6, 15), Math.Max(1, S(1))))
            g.DrawRectangle(p, r.X + S(1.5f), r.Y + S(1.5f), r.Width - S(3) - 1, r.Height - S(3) - 1);
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        hits.Clear();
        int W = ClientSize.Width;
        g.Clear(dragOver ? Accent : Line);
        using (var path = StepPath(new Rectangle(S(2), S(2), W - S(4), ClientSize.Height - S(4)), S(3)))
        using (var b = new SolidBrush(Bg)) g.FillPath(b, path);

        DrawBanner(g, W);

        // Close button
        var close = new Rectangle(W - S(34), S(10), S(24), S(24));
        bool closeHot = hover == hits.Count;
        using (var b = new SolidBrush(closeHot ? Danger : Color.FromArgb(160, 13, 11, 18))) g.FillRectangle(b, close);
        using (var b = new SolidBrush(closeHot ? Bg : Ink)) {
            int u = S(2);
            for (int i = 0; i < 5; i++) {
                g.FillRectangle(b, close.X + S(7) + i * u, close.Y + S(7) + i * u, u, u);
                g.FillRectangle(b, close.X + S(7) + (4 - i) * u, close.Y + S(7) + i * u, u, u);
            }
        }
        hits.Add(new Hit { R = close, Do = Hide });
        hits.Add(new Hit { R = new Rectangle(0, 0, W, S(BannerH)), Drag = true, Enabled = false });

        // Now playing
        int y = S(BannerH + 12);
        PixelText(g, "NOW PLAYING", headFont, Muted, S(Pad), y);
        y += S(20);
        TextRenderer.DrawText(g, VideoWallpaper.NowPlaying, nameFont, new Point(S(Pad), y), Ink, TextFormatFlags.NoPadding);
        y += S(24);
        string autoReason = VideoWallpaper.AutoPauseReason;
        string status = VideoWallpaper.IsPaused ? "Paused"
            : autoReason != null ? autoReason
            : VideoWallpaper.Pinned != "" ? "Pinned · stays on this one"
            : "Rotating · next change in " + VideoWallpaper.NextChangeText();
        Color dotColor = (VideoWallpaper.IsPaused || autoReason != null) ? Muted : VideoWallpaper.Pinned != "" ? AccentHi : Ok;
        Tile(g, new Rectangle(S(Pad), y + S(5), S(8), S(8)), dotColor, false);
        TextRenderer.DrawText(g, status, smallFont, new Point(S(Pad + 14), y), Muted, TextFormatFlags.NoPadding);

        // Wallpaper list + Add button
        y = S(BannerH + 92);
        string scrollNote = clips.Count > MaxVisibleClips ? "scroll · drop MP4" : "drop MP4 to add";
        Header(g, "WALLPAPER", y, scrollNote);
        var addRect = new Rectangle(W - S(Pad) - S(64), y - S(3), S(64), S(22));
        PixelButton(g, addRect, "+ ADD", Raised, Line, AccentHi, hover == hits.Count, true, VideoWallpaper.BrowseAndAddVideo, chipFont);
        y += S(28);

        Row(g, ref y, W, "Rotate through all", null, VideoWallpaper.Pinned == "", false, VideoWallpaper.RotateAll);
        int showCount = Math.Min(clips.Count, MaxVisibleClips);
        for (int i = 0; i < showCount; i++) {
            var clip = clips[scrollOffset + i];
            bool playing = string.Equals(clip.Name, VideoWallpaper.NowPlaying, StringComparison.OrdinalIgnoreCase);
            Row(g, ref y, W, clip.Name, Swatch(clip.Name), clip.Name == VideoWallpaper.Pinned, playing, () => VideoWallpaper.Pin(clip));
        }

        // Interval chips
        y += S(12);
        bool rotating = VideoWallpaper.Pinned == "";
        Header(g, "CHANGE EVERY", y, rotating ? null : "only while rotating");
        y += S(26);
        int[] mins = VideoWallpaper.Intervals;
        int gap = S(5), cw = (W - 2 * S(Pad) - gap * (mins.Length - 1)) / mins.Length;
        for (int i = 0; i < mins.Length; i++) {
            int m = mins[i];
            var r = new Rectangle(S(Pad) + i * (cw + gap), y, cw, S(30));
            bool on = m == VideoWallpaper.Minutes;
            PixelButton(g, r, m < 60 ? m + "M" : (m / 60) + "H", on ? Accent : Raised, on ? AccentDeep : Line, on ? Bg : Ink,
                hover == hits.Count, rotating, () => VideoWallpaper.SetMinutes(m), chipFont);
        }

        // Engine controls: Scale, Monitor, Speed, Auto-pause
        y += S(30 + 12);
        Header(g, "ENGINE", y, "native GPU");
        y += S(26);
        int innerW = W - 2 * S(Pad);

        // Row 1: Scale (3) + Monitors (3)
        int segGap = S(4), halfGap = S(8);
        int leftW = (innerW - halfGap) / 2, rightW = innerW - halfGap - leftW;
        ScaleMode[] scales = { ScaleMode.Fill, ScaleMode.Fit, ScaleMode.Stretch };
        string[] scaleLabels = { "FILL", "FIT", "STR" };
        int sw3 = (leftW - 2 * segGap) / 3;
        for (int i = 0; i < 3; i++) {
            var sm = scales[i];
            bool on = sm == VideoWallpaper.CurrentScale;
            PixelButton(g, new Rectangle(S(Pad) + i * (sw3 + segGap), y, sw3, S(28)), scaleLabels[i],
                on ? Accent : Raised, on ? AccentDeep : Line, on ? Bg : Ink, hover == hits.Count, true, () => VideoWallpaper.SetScale(sm), chipFont);
        }
        MonitorMode[] mons = { MonitorMode.Duplicate, MonitorMode.Span, MonitorMode.Primary };
        string[] monLabels = { "DUPE", "SPAN", "1ST" };
        int mw3 = (rightW - 2 * segGap) / 3;
        int mx0 = S(Pad) + leftW + halfGap;
        for (int i = 0; i < 3; i++) {
            var mm = mons[i];
            bool on = mm == VideoWallpaper.CurrentMonitor;
            PixelButton(g, new Rectangle(mx0 + i * (mw3 + segGap), y, mw3, S(28)), monLabels[i],
                on ? Accent : Raised, on ? AccentDeep : Line, on ? Bg : Ink, hover == hits.Count, true, () => VideoWallpaper.SetMonitor(mm), chipFont);
        }

        // Row 2: Speed (4)
        y += S(28 + 6);
        double[] speeds = VideoWallpaper.Speeds;
        int spw = (innerW - segGap * (speeds.Length - 1)) / speeds.Length;
        for (int i = 0; i < speeds.Length; i++) {
            double sp = speeds[i];
            bool on = Math.Abs(sp - VideoWallpaper.PlaybackSpeed) < 0.01;
            string lbl = sp.ToString("0.#", CultureInfo.InvariantCulture) + "X";
            PixelButton(g, new Rectangle(S(Pad) + i * (spw + segGap), y, spw, S(28)), lbl,
                on ? Accent : Raised, on ? AccentDeep : Line, on ? Bg : Ink, hover == hits.Count, true, () => VideoWallpaper.SetSpeed(sp), chipFont);
        }

        // Row 3: Auto-pause toggles (Fullscreen/Maximized/Off + Battery On/Off)
        y += S(28 + 6);
        int apW = (innerW - halfGap) / 2;
        var curRule = VideoWallpaper.CurrentPauseRule;
        string appLbl = curRule == PauseRule.Fullscreen ? "PAUSE: FULL" : curRule == PauseRule.Maximized ? "PAUSE: MAX" : "PAUSE: OFF";
        bool appOn = curRule != PauseRule.Never;
        PixelButton(g, new Rectangle(S(Pad), y, apW, S(28)), appLbl,
            appOn ? Hex("#26302a") : Raised, appOn ? Hex("#3c5c45") : Line, appOn ? Ok : Muted,
            hover == hits.Count, true, () => {
                var next = curRule == PauseRule.Fullscreen ? PauseRule.Maximized : curRule == PauseRule.Maximized ? PauseRule.Never : PauseRule.Fullscreen;
                VideoWallpaper.SetPauseRule(next);
            }, chipFont);

        bool batOn = VideoWallpaper.PauseOnBattery;
        PixelButton(g, new Rectangle(S(Pad) + apW + halfGap, y, innerW - apW - halfGap, S(28)),
            batOn ? "BATTERY: PAUSE" : "BATTERY: RUN",
            batOn ? Hex("#26302a") : Raised, batOn ? Hex("#3c5c45") : Line, batOn ? Ok : Muted,
            hover == hits.Count, true, VideoWallpaper.TogglePauseOnBattery, chipFont);

        // Actions
        y += S(28 + 14);
        int half = (innerW - S(10)) / 2;
        PixelButton(g, new Rectangle(S(Pad), y, half, S(38)), "NEXT  >", Accent, AccentDeep, Bg, hover == hits.Count, true, VideoWallpaper.NextWallpaper);
        PixelButton(g, new Rectangle(S(Pad) + half + S(10), y, half, S(38)), VideoWallpaper.IsPaused ? "PLAY  >" : "PAUSE  II",
            Raised, Line, Ink, hover == hits.Count, true, VideoWallpaper.TogglePause);
        y += S(38 + 8);
        PixelButton(g, new Rectangle(S(Pad), y, innerW, S(34)), "STOP WALLPAPER", Hex("#2a1416"), Hex("#5a2326"), Danger,
            hover == hits.Count, true, VideoWallpaper.Quit);
    }

    void Row(Graphics g, ref int y, int W, string name, Color[] sw, bool selected, bool playing, Action act) {
        var r = new Rectangle(S(Pad - 8), y, W - 2 * S(Pad - 8), S(RowH));
        bool hot = hover == hits.Count;
        if (hot || selected) using (var b = new SolidBrush(selected ? Hex("#241c1a") : HoverRow)) g.FillRectangle(b, r);
        if (selected) using (var b = new SolidBrush(Accent)) g.FillRectangle(b, r.X, r.Y, S(3), r.Height);

        int ts = S(6), tg = S(1), sx = S(Pad + 2), sy = y + (S(RowH) - 3 * ts - 2 * tg) / 2;
        string[] all = { "Earth", "Saturn", "Quasar", "Sagittarius", "Horizon" };
        for (int j = 0; j < 9; j++) {
            Color c = sw != null ? sw[new[] { 4, 0, 3, 0, 2, 1, 3, 1, 4 }[j]] : Swatch(all[j % 5])[j % 3 == 1 ? 2 : 1];
            Tile(g, new Rectangle(sx + (j % 3) * (ts + tg), sy + (j / 3) * (ts + tg), ts, ts), c, false);
        }

        int tx = sx + 3 * ts + 2 * tg + S(12);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, name, bodyFont, new Rectangle(tx, y, W - tx - S(100), S(RowH)), selected ? Ink : Hex("#d9d0d6"), flags);

        int box = S(14), bx = r.Right - S(10) - box, by = y + (S(RowH) - box) / 2;
        if (playing) {
            var tag = "NOW";
            var sz = PixelMeasure(g, tag, chipFont);
            PixelText(g, tag, chipFont, Ok, bx - S(10) - sz.Width, y + (S(RowH) - sz.Height) / 2 + S(1));
        }
        if (selected) {
            Tile(g, new Rectangle(bx, by, box, box), Accent, false);
            Tile(g, new Rectangle(bx + S(4), by + S(4), box - S(8), box - S(8)), AccentHi, false);
        } else using (var p = new Pen(Line, S(2))) g.DrawRectangle(p, bx + S(1), by + S(1), box - S(2), box - S(2));

        hits.Add(new Hit { R = r, Do = act });
        y += S(RowH);
    }

    void DrawBanner(Graphics g, int W) {
        int cs = S(6), cols = W / cs + 1, rows = S(BannerH) / cs;
        double cx = cols * 0.75, cy = rows * 0.56, R = rows * 0.16;
        double roll = -0.3, rc = Math.Cos(roll), rs = Math.Sin(roll), si = 0.3;
        int gap = Math.Max(1, S(0.8f));
        g.SetClip(new Rectangle(S(2), S(2), W - S(4), S(BannerH)));
        using (var b = new SolidBrush(Hex("#08070c"))) g.FillRectangle(b, 0, 0, W, S(BannerH));
        for (int py = 0; py < rows; py++) {
            for (int px = 0; px < cols; px++) {
                double X = (px + 0.5 - cx) / R, Y = (py + 0.5 - cy) / R;
                double r = Math.Sqrt(X * X + Y * Y), x = X * rc + Y * rs, y = -X * rs + Y * rc;
                double yd = y / si, rd = Math.Sqrt(x * x + yd * yd), phi = Math.Atan2(yd, x);
                Func<double, double, double> disk = (d, a) => {
                    if (d < 1.35 || d > 7) return 0;
                    double prof = Sm(1.35, 1.7, d) * (1 - Sm(2.5, 7, d)) * Math.Pow(2 / d, 1.15) * 1.3 * (1 + 0.8 * Math.Exp(-Math.Pow((d - 1.8) / 0.7, 2)));
                    double rot = a - t * 1.6 * Math.Pow(d / 1.5, -1.5);
                    return prof * Math.Max(0, 0.55 + 0.25 * Math.Sin(d * 7 + 1.5 * Math.Sin(rot * 2 + d * 0.7)) + 0.2 * Math.Sin(rot * 3 + d * 2));
                };
                double I = 0;
                if (r > 1 || y > 0) I += disk(rd, phi) * (1 + 0.45 * (-x / Math.Max(rd, 0.01)));
                if (r > 1 && r < 2.4 && y < 0) I += disk(1.45 + (r - 1.03) * 3, Math.Atan2(y, x) + Math.PI);
                I += 1.4 * Math.Exp(-Math.Pow((r - 1.04) / 0.06, 2));
                if (r < 0.97 && (y <= 0 || Math.Abs(yd) > 7)) I = 0;

                var cell = new Rectangle(px * cs + gap, py * cs + gap, cs - 2 * gap, cs - 2 * gap);
                if (I >= 0.14) Tile(g, cell, Disk[Math.Min(7, (int)((I - 0.14) / 0.14))], true);
                else {
                    uint h = (uint)(px * 73856093 ^ py * 19349663) * 2654435761u;
                    if ((h >> 8) % 1000 < 22) {
                        double tw = 0.5 + 0.5 * Math.Sin(t * 2.2 + (h & 255));
                        int sz = Math.Max(2, (int)((cs - 2 * gap) * (0.3 + 0.4 * tw)));
                        using (var b = new SolidBrush(Mix(Hex("#5d5458"), Hex("#f3e9df"), (float)tw))) g.FillRectangle(b, px * cs + (cs - sz) / 2, py * cs + (cs - sz) / 2, sz, sz);
                    }
                }
            }
        }
        using (var lg = new LinearGradientBrush(new Rectangle(0, S(BannerH - 34), W, S(34)), Color.FromArgb(0, Bg), Bg, 90f))
            g.FillRectangle(lg, 0, S(BannerH - 34), W, S(34));
        g.ResetClip();
        PixelText(g, "EVENT", titleFont, Ink, S(Pad), S(18), true);
        PixelText(g, "HORIZON", titleFont, AccentHi, S(Pad), S(18) + titleFont.Height, true);
    }

    static double Sm(double a, double b, double x) { double q = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return q * q * (3 - 2 * q); }
}
