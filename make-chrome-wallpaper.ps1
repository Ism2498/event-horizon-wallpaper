# Renders a pixel-tile ringed planet as a still image for Chrome's new tab page.
#   make-chrome-wallpaper.ps1 [-Out path.png] [-Width 2560] [-Height 1440] [-Seed 7]
param(
    [string]$Out = (Join-Path $PSScriptRoot 'ringed-planet-chrome.png'),
    [int]$Width = 2560,
    [int]$Height = 1440,
    [int]$Seed = 7
)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;

public static class RingedPlanet {
    static readonly Color[] Body = Hex("#0f0b26 #1b1748 #282673 #3a3aa0 #5559c9 #7d86e6 #b1bcf8 #eef1ff");
    static readonly Color[] Ring = Hex("#2a2140 #4d3f6e #7d6aa3 #ac9bd0 #d8ccf0 #f7f0ff");
    static readonly Color[] Dust = Hex("#1d1b2e #2b2942 #3c3a5a");
    static readonly Color Cyan = ColorTranslator.FromHtml("#5ce1e6");
    static readonly Color Rose = ColorTranslator.FromHtml("#e77fd0");

    static Color[] Hex(string s) {
        var p = s.Split(' '); var c = new Color[p.Length];
        for (int i = 0; i < p.Length; i++) c[i] = ColorTranslator.FromHtml(p[i]);
        return c;
    }

    static double Hash(int x, int y, int seed) {
        unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653); h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216.0; }
    }
    static double Noise(double x, double y, int seed) {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
    static double Fbm(double x, double y, int seed) {
        double s = 0, amp = 0.5;
        for (int o = 0; o < 5; o++) { s += amp * Noise(x, y, seed + o * 17); x *= 2.03; y *= 2.03; amp *= 0.5; }
        return s;
    }
    static double Smooth(double a, double b, double x) { double k = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return k * k * (3 - 2 * k); }

    // Brightness of the ring material at ring radius rr (in planet radii); gaps like Cassini's division
    static double RingDensity(double rr) {
        if (rr < 1.32 || rr > 2.35) return 0;
        double d = Smooth(1.32, 1.42, rr) * (1 - Smooth(2.2, 2.35, rr));
        d *= 0.62 + 0.25 * Math.Sin(rr * 41) + 0.13 * Math.Sin(rr * 97 + 1.3);
        if (rr > 1.86 && rr < 1.93) d *= 0.08;          // main gap
        if (rr > 2.08 && rr < 2.1) d *= 0.3;            // thin outer gap
        if (rr < 1.55) d *= 0.55;                        // faint inner ring
        return Math.Max(0, d);
    }

    public static Bitmap Render(int W, int H, int seed) {
        var bmp = new Bitmap(W, H);
        using (var g = Graphics.FromImage(bmp)) {
            g.Clear(ColorTranslator.FromHtml("#07060f"));
            int cell = Math.Max(4, (int)Math.Round(H / 120.0));
            int cols = W / cell + 1, rows = H / cell + 1;
            int gap = Math.Max(1, (int)Math.Round(cell * 0.12)), bevel = Math.Max(1, (int)Math.Round(cell * 0.1));
            var bevelPen = new Pen(Color.FromArgb(110, 7, 6, 15), Math.Max(1, cell * 0.1f));

            double R = rows * 0.40;                        // planet radius in cells
            double pcx = cols * 0.17, pcy = rows * 0.84;   // planet centre: lower left, partly off-screen
            double roll = -21 * Math.PI / 180, cr = Math.Cos(roll), sr = Math.Sin(roll);
            double tilt = 0.24;                            // how open the rings look
            double lx = 0.62, ly = -0.48, lz = 0.62;      // light from upper right
            double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ll; ly /= ll; lz /= ll;
            double mcx = cols * 0.57, mcy = rows * 0.30, mR = rows * 0.035;   // small moon

            for (int py = 0; py < rows; py++) {
                for (int px = 0; px < cols; px++) {
                    double X = (px + 0.5 - pcx) / R, Y = (py + 0.5 - pcy) / R;
                    double x = X * cr + Y * sr, y = -X * sr + Y * cr;     // planet frame (rings along x)
                    double r2 = x * x + y * y;
                    double n = Hash(px, py, seed);
                    Color c = Color.Empty; bool hollow = false; double size = 1;

                    double rr = Math.Sqrt(x * x + (y / tilt) * (y / tilt));
                    double ring = RingDensity(rr);
                    bool ringFront = y > 0;
                    bool onPlanet = r2 < 1;

                    if (onPlanet && !(ring > 0.05 && ringFront)) {
                        double z = Math.Sqrt(1 - r2);
                        double lambert = Math.Max(0, x * lx + y * ly + z * lz);
                        double lat = Math.Asin(Math.Max(-1, Math.Min(1, y)));
                        double band = 0.5 + 0.28 * Math.Sin(lat * 13 + 2.2 * (Fbm(x * 2.2, lat * 3, seed) - 0.5) * 3)
                                          + 0.12 * Math.Sin(lat * 31 + x * 1.5);
                        double storm = Math.Exp(-(Math.Pow((x - 0.38) / 0.11, 2) + Math.Pow((lat + 0.42) / 0.05, 2)));
                        // Shadow of the rings across the planet, offset along the light
                        double sx = x - lx * 0.18, sy = y - ly * 0.18;
                        double shadow = RingDensity(Math.Sqrt(sx * sx + (sy / tilt) * (sy / tilt))) > 0.08 && y < 0.05 ? 0.45 : 1;
                        double v = (0.08 + 0.92 * Math.Pow(lambert, 0.85)) * (0.65 + 0.5 * band) * shadow + storm * 0.35 * lambert;
                        v += 0.25 * Math.Pow(1 - z, 3) * lambert;                   // thin bright limb
                        int lvl = (int)Math.Round(Math.Max(0, Math.Min(1, v)) * (Body.Length - 1));
                        c = Body[lvl];
                        if (storm > 0.4 && lambert > 0.2) c = Rose;
                    } else if (ring > 0.05 && (ringFront || !onPlanet)) {
                        // Planet's shadow falls on the rings behind it, away from the light
                        double behind = (x * -lx + y * -ly) > 0 && Math.Abs(x * ly - y * lx) < 1 && !ringFront ? 0.35 : 1;
                        double lit = (0.55 + 0.45 * Math.Max(0, (x * lx + y * ly) / Math.Max(rr, 0.01) + 0.4)) * behind;
                        double v = ring * lit * (0.85 + 0.3 * n);
                        if (v < 0.12) { c = Dust[(int)(n * 3)]; hollow = true; size = 0.6; }
                        else c = Ring[(int)Math.Max(0, Math.Min(Ring.Length - 1, Math.Floor(v * Ring.Length)))];
                    } else {
                        // Moon
                        double mx = (px + 0.5 - mcx) / mR, my = (py + 0.5 - mcy) / mR, m2 = mx * mx + my * my;
                        if (m2 < 1) {
                            double mz = Math.Sqrt(1 - m2), ml = Math.Max(0, mx * lx + my * ly + mz * lz);
                            double crater = Fbm(mx * 3, my * 3, seed + 99);
                            c = Ring[(int)Math.Max(0, Math.Min(Ring.Length - 1, Math.Round((0.1 + ml * (0.6 + 0.6 * crater)) * (Ring.Length - 1))))];
                        } else {
                            // Faint nebula in the upper right, plus stars
                            // Soft rounded cloud centred in the upper right, broken up by noise
                            double ndx = ((double)px / cols - 0.84) / 0.22, ndy = ((double)py / rows - 0.16) / 0.26;
                            double falloff = Math.Exp(-(ndx * ndx + ndy * ndy));
                            double neb = falloff * (0.35 + 0.9 * (Fbm(px * 0.045, py * 0.045, seed + 5) - 0.35));
                            double wisp = Fbm(px * 0.09 + 40, py * 0.09, seed + 11);
                            if (n > 0.9972) { c = n > 0.9992 ? Cyan : Body[7]; size = n > 0.999 ? 0.8 : 0.45; }
                            else if (n > 0.985) { c = Body[4]; size = 0.3; }
                            else if (neb > 0.18 && n < (neb - 0.18) * 3.2) {
                                if (neb > 0.42 && wisp > 0.62) { c = Rose; size = 0.5; }
                                else if (neb > 0.36) { c = Body[2]; size = 0.6; }
                                else { c = Dust[(int)(n * 3)]; hollow = true; size = 0.45 + neb; }
                            }
                        }
                    }

                    if (c.IsEmpty) continue;
                    float s = (float)((cell - 2 * gap) * size);
                    float ox = px * cell + (cell - s) / 2f, oy = py * cell + (cell - s) / 2f;
                    if (hollow) { using (var pen = new Pen(c, Math.Max(1, cell * 0.1f))) g.DrawRectangle(pen, ox, oy, s, s); }
                    else {
                        using (var br = new SolidBrush(c)) g.FillRectangle(br, ox, oy, s, s);
                        if (size >= 0.95) g.DrawRectangle(bevelPen, ox + 1.5f * bevel, oy + 1.5f * bevel, s - 3 * bevel, s - 3 * bevel);
                    }
                }
            }
        }
        return bmp;
    }
}
"@
$bmp = [RingedPlanet]::Render($Width, $Height, $Seed)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Saved $Out"
