# Draws the app icon: a pixel-tile black hole on a dark rounded tile, saved as VideoWallpaper.ico
# (16 to 48 px as classic bitmaps, 256 px as PNG). Also writes icon-preview.png at 256 px.
#   make-icon.ps1 [-Out VideoWallpaper.ico]
param([string]$Out = (Join-Path $PSScriptRoot 'VideoWallpaper.ico'))
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

public static class IconArt {
    static readonly Color[] Levels = Hex("#3a0d08 #6e170b #b1300f #e05617 #f5803a #f9aa72 #fcd2ac #fff3e6");
    static readonly Color Bg = ColorTranslator.FromHtml("#0d0b12"), Edge = ColorTranslator.FromHtml("#2c2536");
    static Color[] Hex(string s) { var p = s.Split(' '); var c = new Color[p.Length]; for (int i = 0; i < p.Length; i++) c[i] = ColorTranslator.FromHtml(p[i]); return c; }
    static double Smooth(double a, double b, double x) { double k = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return k * k * (3 - 2 * k); }

    // Same look as the wallpaper: tilted disk, its far side bent over the top, photon ring, black shadow
    static double Intensity(double u, double v) {
        double R = 0.36, X = (u - 0.03) / R, Y = (v - 0.03) / R;
        double roll = -0.32, rc = Math.Cos(roll), rs = Math.Sin(roll), si = 0.32;
        double r = Math.Sqrt(X * X + Y * Y), x = X * rc + Y * rs, y = -X * rs + Y * rc;
        double yd = y / si, rd = Math.Sqrt(x * x + yd * yd);
        Func<double, double> disk = d => d < 1.35 || d > 3.3 ? 0 : Smooth(1.35, 1.6, d) * (1 - Smooth(2.2, 3.3, d)) * Math.Pow(1.8 / d, 1.2) * 1.25;
        double I = 0;
        if (r > 1 || y > 0) I += disk(rd) * (1 + 0.45 * (-x / Math.Max(rd, 0.01)));
        if (r > 1 && r < 2 && y < 0) I += disk(1.4 + (r - 1.03) * 2.2) * 0.9;
        I += 1.3 * Math.Exp(-Math.Pow((r - 1.04) / 0.09, 2));
        if (r < 0.96 && (y <= 0 || Math.Abs(yd) > 3.3)) I = 0;
        return I;
    }

    public static Bitmap Render(int size) {
        int grid = size <= 32 ? size : size <= 64 ? size / 2 : 32;
        float cell = size / (float)grid;
        bool tiles = cell >= 6;
        double rad = grid * 0.2;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(Color.Transparent);
            Func<int, int, bool> inside = (gx, gy) => {
                if (gx < 0 || gy < 0 || gx >= grid || gy >= grid) return false;
                double dx = Math.Max(0, Math.Abs(gx + 0.5 - grid / 2.0) - (grid / 2.0 - rad));
                double dy = Math.Max(0, Math.Abs(gy + 0.5 - grid / 2.0) - (grid / 2.0 - rad));
                return dx * dx + dy * dy <= rad * rad;
            };
            for (int gy = 0; gy < grid; gy++) {
                for (int gx = 0; gx < grid; gx++) {
                    if (!inside(gx, gy)) continue;
                    var rect = new RectangleF(gx * cell, gy * cell, cell, cell);
                    bool edge = grid >= 24 && (!inside(gx - 1, gy) || !inside(gx + 1, gy) || !inside(gx, gy - 1) || !inside(gx, gy + 1));
                    using (var b = new SolidBrush(edge ? Edge : Bg)) g.FillRectangle(b, rect);
                    if (edge) continue;
                    double I = Intensity((gx + 0.5) / grid * 2 - 1, (gy + 0.5) / grid * 2 - 1);
                    Color c;
                    if (I >= 0.14) c = Levels[Math.Min(Levels.Length - 1, (int)((I - 0.14) / 0.15))];
                    else if (grid >= 24 && ((gx * 7 + gy * 13) % 53 == 0)) c = Levels[6];   // a few stars
                    else continue;
                    if (tiles) {
                        float gap = Math.Max(1, cell * 0.12f);
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, rect.X + gap, rect.Y + gap, cell - 2 * gap, cell - 2 * gap);
                        using (var p = new Pen(Color.FromArgb(90, 7, 6, 15), Math.Max(1, cell * 0.1f)))
                            g.DrawRectangle(p, rect.X + gap + cell * 0.15f, rect.Y + gap + cell * 0.15f, cell - 2 * gap - cell * 0.3f, cell - 2 * gap - cell * 0.3f);
                    } else using (var b = new SolidBrush(c)) g.FillRectangle(b, rect);
                }
            }
        }
        return bmp;
    }

    // Classic icon entry: BITMAPINFOHEADER + bottom-up BGRA + empty AND mask
    static byte[] Dib(Bitmap bmp) {
        int w = bmp.Width, h = bmp.Height, maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream()) using (var bw = new BinaryWriter(ms)) {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++) { var c = bmp.GetPixel(x, y); bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A); }
            bw.Write(new byte[maskStride * h]);
            return ms.ToArray();
        }
    }

    public static void Save(string path, int[] sizes) {
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) using (var bmp = Render(sizes[i])) {
            if (sizes[i] >= 256) using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images[i] = ms.ToArray(); }
            else images[i] = Dib(bmp);
        }
        using (var fs = File.Create(path)) using (var bw = new BinaryWriter(fs)) {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset); offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
    }
}
"@
[IconArt]::Save($Out, [int[]](16, 20, 24, 32, 40, 48, 64, 256))
$preview = [IconArt]::Render(256); $preview.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png); $preview.Dispose()
"Saved $Out"
