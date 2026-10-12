// Loads the game's own look from the player's installation at runtime: the title logo and
// the two bitmap fonts from Resource.rpk, and the main-menu background from a local cache.
// Nothing from the game is shipped with the launcher; if anything cannot be read, the
// launcher falls back to system fonts and a plain background.
//
// Formats (as read by the game):
//   .rpk  "AFBF0C01" magic, table size, then 32-byte entries: name[16], offset, size, 2 x pad.
//   .rfi  32-byte header: magic, width, height (uint32 at 4 and 8), format at 0x10, flags
//         (uint16 at 0x1A, 0x4000 = run-length compressed), unpacked size at 0x1C. Rows are
//         stored bottom-up. Format 0xE608 = BGRA, 0xC600 = BGR, 0x6208/0x8400 = 2 bytes,
//         anything else 1 byte per pixel.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExanimaLauncher
{
    class Rpk
    {
        readonly string path;
        readonly long dataStart;
        readonly Dictionary<string, KeyValuePair<long, int>> entries =
            new Dictionary<string, KeyValuePair<long, int>>(StringComparer.OrdinalIgnoreCase);

        public Rpk(string path)
        {
            this.path = path;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadUInt32() != 0xAFBF0C01) throw new InvalidDataException("Not an RPK file: " + path);
                int size = r.ReadInt32();
                for (int i = 0; i < size / 32; i++)
                {
                    string name = Encoding.GetEncoding(1252).GetString(r.ReadBytes(16)).TrimEnd('\0');
                    uint offset = r.ReadUInt32(), length = r.ReadUInt32();
                    r.ReadUInt32();
                    r.ReadUInt32();
                    if (!entries.ContainsKey(name)) entries[name] = new KeyValuePair<long, int>(offset, (int)length);
                }
                dataStart = 8 + size;
            }
        }

        public byte[] Read(string name)
        {
            KeyValuePair<long, int> e;
            if (!entries.TryGetValue(name, out e)) throw new FileNotFoundException(name + " is not in " + path);
            using (var f = File.OpenRead(path))
            {
                f.Seek(dataStart + e.Key, SeekOrigin.Begin);
                var data = new byte[e.Value];
                int got = 0;
                while (got < data.Length)
                {
                    int n = f.Read(data, got, data.Length - got);
                    if (n <= 0) throw new EndOfStreamException(name);
                    got += n;
                }
                return data;
            }
        }
    }

    class RfiImage
    {
        public int Width, Height, Bpp;
        public byte[] Pixels;   // top-down rows

        public static RfiImage Decode(byte[] d)
        {
            int w = BitConverter.ToInt32(d, 4), h = BitConverter.ToInt32(d, 8);
            uint format = BitConverter.ToUInt32(d, 0x10) & 0x800cffff;
            ushort flags = BitConverter.ToUInt16(d, 0x1a);
            int size = BitConverter.ToInt32(d, 0x1c);
            int bpp = format == 0xe608 ? 4 : format == 0xc600 ? 3 : (format == 0x6208 || format == 0x8400) ? 2 : 1;
            if (w <= 0 || h <= 0 || size != w * h * bpp) throw new InvalidDataException("Unexpected .rfi layout");

            var raw = new byte[size];
            int i = 0x20, o = 0;
            if ((flags & 0x4000) == 0) Buffer.BlockCopy(d, i, raw, 0, size);
            else if (bpp == 1)
            {
                const int T = 257 - 194;
                while (o < size)
                {
                    int b = d[i++];
                    if (b > T) { int n = Math.Min(b - T + 2, size - o); byte v = d[i++]; for (int k = 0; k < n; k++) raw[o++] = v; }
                    else { int n = Math.Min(b + 1, size - o); Buffer.BlockCopy(d, i, raw, o, n); i += b + 1; o += n; }
                }
            }
            else
            {
                const int T = 256 - 194;
                while (o < size)
                {
                    int b = d[i++];
                    if (b > T)
                    {
                        int n = b - T + 1;
                        for (int k = 0; k < n && o < size; k++, o += bpp) Buffer.BlockCopy(d, i, raw, o, bpp);
                        i += bpp;
                    }
                    else
                    {
                        int n = Math.Min((b + 1) * bpp, size - o);
                        Buffer.BlockCopy(d, i, raw, o, n);
                        i += (b + 1) * bpp;
                        o += n;
                    }
                }
            }

            var img = new RfiImage { Width = w, Height = h, Bpp = bpp, Pixels = new byte[size] };
            int stride = w * bpp;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, (h - 1 - y) * stride, img.Pixels, y * stride, stride);
            return img;
        }

        // Converts to a 32-bit ARGB bitmap with the given per-pixel mapping.
        public Bitmap ToBitmap(Func<byte[], int, int> argb)
        {
            var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var buf = new int[Width * Height];
            for (int p = 0; p < buf.Length; p++) buf[p] = argb(Pixels, p * Bpp);
            for (int y = 0; y < Height; y++)
                Marshal.Copy(buf, y * Width, data.Scan0 + y * data.Stride, Width);
            bmp.UnlockBits(data);
            return bmp;
        }
    }

    // A bitmap font laid out as a grid of equal cells; glyph widths come from each cell's ink.
    class GlyphFont
    {
        readonly Bitmap sheet;
        readonly int cell;
        readonly Dictionary<char, Rectangle> glyphs = new Dictionary<char, Rectangle>();
        readonly float spaceWidth, gap;
        readonly bool upperOnly;

        public GlyphFont(Bitmap sheet, int cell, string charset, Func<int, int, byte> ink, float spaceWidth, float gap, bool upperOnly,
                         bool rowsFromBottom)
        {
            this.sheet = sheet;
            this.cell = cell;
            this.spaceWidth = spaceWidth;
            this.gap = gap;
            this.upperOnly = upperOnly;
            int cols = sheet.Width / cell, rows = sheet.Height / cell;
            for (int n = 0; n < charset.Length; n++)
            {
                char c = charset[n];
                if (c == '\0' || glyphs.ContainsKey(c)) continue;
                int row = n / cols;
                if (row >= rows) break;
                if (rowsFromBottom) row = rows - 1 - row;   // the body font's first characters are in the bottom row
                int cx = (n % cols) * cell, cy = row * cell;
                int left = cell, right = -1;
                for (int x = 0; x < cell; x++)
                    for (int y = 0; y < cell; y++)
                        if (ink(cx + x, cy + y) > 40) { left = Math.Min(left, x); right = Math.Max(right, x); break; }
                if (right >= left) glyphs[c] = new Rectangle(cx + left, cy, right - left + 1, cell);
            }
        }

        public bool Has(char c) { return glyphs.ContainsKey(upperOnly ? char.ToUpperInvariant(c) : c); }

        public float Measure(string text, float size)
        {
            float s = size / cell, w = 0;
            foreach (char raw in text)
            {
                char c = upperOnly ? char.ToUpperInvariant(raw) : raw;
                Rectangle g;
                w += glyphs.TryGetValue(c, out g) ? (g.Width + gap) * s : spaceWidth * s;
            }
            return w;
        }

        // size = cell height in pixels; tint multiplies the glyph colors.
        public void Draw(Graphics gfx, string text, float x, float y, float size, Color tint)
        {
            float s = size / cell;
            var cm = new ColorMatrix(new[]
            {
                new[] { tint.R / 255f, 0, 0, 0, 0f }, new[] { 0, tint.G / 255f, 0, 0, 0f },
                new[] { 0, 0, tint.B / 255f, 0, 0f }, new[] { 0, 0, 0, tint.A / 255f, 0f }, new[] { 0, 0, 0, 0, 1f },
            });
            using (var attr = new ImageAttributes())
            {
                attr.SetColorMatrix(cm);
                var oldMode = gfx.InterpolationMode;
                var oldPixel = gfx.PixelOffsetMode;
                gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gfx.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (char raw in text)
                {
                    char c = upperOnly ? char.ToUpperInvariant(raw) : raw;
                    Rectangle g;
                    if (!glyphs.TryGetValue(c, out g)) { x += spaceWidth * s; continue; }
                    var dest = new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Ceiling(g.Width * s), (int)Math.Ceiling(g.Height * s));
                    gfx.DrawImage(sheet, dest, g.X, g.Y, g.Width, g.Height, GraphicsUnit.Pixel, attr);
                    x += (g.Width + gap) * s;
                }
                gfx.InterpolationMode = oldMode;
                gfx.PixelOffsetMode = oldPixel;
            }
        }
    }

    static class GameAssets
    {
        public static GlyphFont Body, Title;
        public static Image Logo, Background;
        public static string Error;   // why the game's look could not be loaded, if it couldn't

        public static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExanimaLauncher");
        public static string BackgroundPath { get { return Path.Combine(CacheDir, "menu_background.png"); } }

        public static void Load(string gameDir)
        {
            try
            {
                var rpk = new Rpk(Path.Combine(gameDir, "Resource.rpk"));

                // Body font: 16 x 16 cells of 64 px, Windows-1252 order starting in the bottom row; alpha only.
                var body = RfiImage.Decode(rpk.Read("fontbase24r.rfi"));
                var bodySheet = body.ToBitmap((p, o) => (p[o] << 24) | 0xFFFFFF);
                var cp = Encoding.GetEncoding(1252);
                var chars = new StringBuilder();
                for (int n = 0; n < 256; n++) chars.Append(n < 32 ? '\0' : cp.GetChars(new[] { (byte)n })[0]);
                Body = new GlyphFont(bodySheet, 64, chars.ToString(), (x, y) => body.Pixels[y * body.Width + x], 18f, 3f, false, true);

                // Title font: 4 x 8 cells of 128 px, capitals; channel 0 = metallic shading, 1 = alpha with glow.
                var title = RfiImage.Decode(rpk.Read("titlefont.rfi"));
                var titleSheet = title.ToBitmap((p, o) => (p[o + 1] << 24) | (p[o] << 16) | (p[o] << 8) | p[o]);
                Title = new GlyphFont(titleSheet, 128, "ABCDEFGHIJKLMNOPQRSTUVWXYZ+", (x, y) => title.Pixels[(y * title.Width + x) * 2], 40f, 6f, true, false);

                var logo = RfiImage.Decode(rpk.Read("maintitle01.rfi"));
                Logo = logo.ToBitmap((p, o) => logo.Bpp == 4
                    ? (p[o + 3] << 24) | (p[o + 2] << 16) | (p[o + 1] << 8) | p[o]
                    : (255 << 24) | (p[o] << 16) | (p[o] << 8) | p[o]);
            }
            catch (Exception ex)
            {
                Body = Title = null;
                Logo = null;
                Error = ex.Message;
            }

            // The main-menu scene is rendered by the game, so it is captured from a game session into
            // a local cache (see MODDING.md); without it the launcher uses a plain dark background.
            try
            {
                if (File.Exists(BackgroundPath))
                    using (var img = Image.FromFile(BackgroundPath)) Background = new Bitmap(img);
            }
            catch { Background = null; }
        }
    }
}
