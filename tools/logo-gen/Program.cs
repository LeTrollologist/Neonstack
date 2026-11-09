using System.IO.Compression;

// Generates the game's neon art assets, rasterised into a uint[] and written as PNG (no image libraries):
//   logo-gen audax <out.png>  - "AUDAX SOFTWORKS" splash lockup (default)
//   logo-gen title <out.png>  - "NEON STACK" title wordmark + block mark
//   logo-gen mark  <out.png>  - square app/window icon
// The old single-argument form (logo-gen out.png) still means "audax".

const string audaxDefault = "logo_audax.png";
const string titleDefault = "logo_neonstack.png";
const string markDefault = "icon_neonstack.png";

string mode = "audax";
string outPath = audaxDefault;
if (args.Length > 0)
{
    if (args[0] is "audax" or "title" or "mark")
    {
        mode = args[0];
        outPath = args.Length > 1 ? args[1] : mode switch { "title" => titleDefault, "mark" => markDefault, _ => audaxDefault };
    }
    else
    {
        outPath = args[0];
    }
}

if (mode == "title") { Art.Title(outPath); return; }
if (mode == "mark") { Art.Mark(outPath); return; }

// ------------------------------------------------------------------ Audax splash
const int W = 1024, H = 384;
var pixels = new uint[W * H];   // 0xAABBGGRR, straight alpha

static uint Rgb(int r, int g, int b, int a = 255) =>
    ((uint)Math.Clamp(a, 0, 255) << 24) | ((uint)Math.Clamp(b, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(r, 0, 255);

static uint Lerp(uint a, uint b, float t)
{
    t = Math.Clamp(t, 0f, 1f);
    return Rgb(
        (int)((a & 255) + ((b & 255) - (a & 255)) * t),
        (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * t),
        (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * t),
        (int)(((a >> 24) & 255) + (((b >> 24) & 255) - ((a >> 24) & 255)) * t));
}

void Blend(int x, int y, uint c)
{
    if (x < 0 || y < 0 || x >= W || y >= H) return;
    int i = y * W + x;
    uint d = pixels[i];
    float sa = (c >> 24) / 255f;
    if (sa <= 0) return;
    float da = (d >> 24) / 255f;
    float oa = sa + da * (1 - sa);
    if (oa <= 0) { pixels[i] = 0; return; }
    pixels[i] = Rgb(
        (int)(((c & 255) * sa + (d & 255) * da * (1 - sa)) / oa),
        (int)((((c >> 8) & 255) * sa + ((d >> 8) & 255) * da * (1 - sa)) / oa),
        (int)((((c >> 16) & 255) * sa + ((d >> 16) & 255) * da * (1 - sa)) / oa),
        (int)(oa * 255));
}

void Rect(int x, int y, int w, int h, uint c)
{
    for (int yy = y; yy < y + h; yy++)
        for (int xx = x; xx < x + w; xx++)
            Blend(xx, yy, c);
}

var glyphs = new Dictionary<char, string>
{
    ['A'] = "010101111101101", ['U'] = "101101101101111", ['D'] = "110101101101110",
    ['X'] = "101101010101101", ['S'] = "011100010001110", ['O'] = "010101101101010",
    ['F'] = "111100110100100", ['T'] = "111010010010010", ['W'] = "101101111111101",
    ['R'] = "110101110101101", ['K'] = "101101110101101",
};

void Text(string s, int x, int y, int cell, Func<int, uint> color, int gap)
{
    int cursor = x;
    for (int i = 0; i < s.Length; i++)
    {
        if (glyphs.TryGetValue(s[i], out var bits))
            for (int p = 0; p < 15; p++)
                if (bits[p] == '1')
                    Rect(cursor + (p % 3) * cell, y + (p / 3) * cell, cell, cell, color(i));
        cursor += 4 * cell * gap;
    }
}

uint Cyan = Rgb(0x22, 0xE5, 0xFF), Magenta = Rgb(0xC6, 0x5B, 0xFF), Pink = Rgb(0xFF, 0x4D, 0x6D);
uint Dim = Rgb(0x7C, 0x84, 0xA8);

// ---- mark: a chunky chevron "A" on the left ---------------------------------
const int apexX = 176, top = 74, bottom = 306, thick = 26;
for (int y = top; y < bottom; y += 6)
{
    float k = (y - top) / (float)(bottom - top);
    int half = 22 + (int)(k * 96);
    uint c = Lerp(Rgb(0x0F, 0xC9, 0xF0), Magenta, k);
    Rect(apexX - half, y, thick, 7, c);
    Rect(apexX + half - thick, y, thick, 7, c);
}

// crossbar, spanning the gap between the legs
int barY = 248;
float kb = (barY - top) / (float)(bottom - top);
int halfB = 22 + (int)(kb * 96);
Rect(apexX - halfB + thick, barY, (halfB - thick) * 2, 22, Pink);

// ---- wordmark ---------------------------------------------------------------
const int cellA = 26;
const int wordW = 19 * cellA;
const int wordX = 330;
uint[] stops =
{
    Rgb(0x22, 0xE5, 0xFF), Rgb(0x4D, 0x9A, 0xFF), Rgb(0x8B, 0x5B, 0xFF),
    Rgb(0xC6, 0x5B, 0xFF), Rgb(0xFF, 0x4D, 0x6D),
};
Text("AUDAX", wordX, 84, cellA, i => stops[i], gap: 1);

const int cellB = 8;
const int softW = 8 * (4 * cellB) + 3 * cellB;
int softX = wordX + (wordW - softW) / 2;
Text("SOFTWORKS", softX, 262, cellB, _ => Dim, gap: 1);

// ---- glow: blur premultiplied colour + alpha, add it back -------------------
int n = W * H;
var pr = new float[n];
var pg = new float[n];
var pb = new float[n];
var pa = new float[n];
for (int i = 0; i < n; i++)
{
    uint c = pixels[i];
    float a = (c >> 24) / 255f;
    pr[i] = (c & 255) * a;
    pg[i] = ((c >> 8) & 255) * a;
    pb[i] = ((c >> 16) & 255) * a;
    pa[i] = a;
}

static void Blur(float[] buf, int w, int h, int radius)
{
    var tmp = new float[buf.Length];
    float norm = 1f / (radius * 2 + 1);

    for (int y = 0; y < h; y++)
    {
        int row = y * w;
        float sum = 0;
        for (int x = -radius; x <= radius; x++) sum += buf[row + Math.Clamp(x, 0, w - 1)];
        for (int x = 0; x < w; x++)
        {
            tmp[row + x] = sum * norm;
            sum += buf[row + Math.Clamp(x + radius + 1, 0, w - 1)] - buf[row + Math.Clamp(x - radius, 0, w - 1)];
        }
    }

    for (int x = 0; x < w; x++)
    {
        float sum = 0;
        for (int y = -radius; y <= radius; y++) sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
        for (int y = 0; y < h; y++)
        {
            buf[y * w + x] = sum * norm;
            sum += tmp[Math.Clamp(y + radius + 1, 0, h - 1) * w + x] - tmp[Math.Clamp(y - radius, 0, h - 1) * w + x];
        }
    }
}

foreach (var chan in new[] { pr, pg, pb, pa }) { Blur(chan, W, H, 12); Blur(chan, W, H, 12); }

// Composite the bloom *under* the artwork so bright shapes keep their colour instead of
// saturating to white.
var withGlow = new uint[n];
for (int i = 0; i < n; i++)
{
    float ga = Math.Clamp(pa[i] * 1.5f, 0f, 1f);
    float inv = pa[i] > 1e-4f ? 1f / pa[i] : 0f;
    float gr = Math.Min(255f, pr[i] * inv);
    float gg = Math.Min(255f, pg[i] * inv);
    float gb = Math.Min(255f, pb[i] * inv);

    uint c = pixels[i];
    float ca = (c >> 24) / 255f;
    float oa = ca + ga * (1f - ca);
    if (oa <= 0) continue;

    withGlow[i] = Rgb(
        (int)(((c & 255) * ca + gr * ga * (1f - ca)) / oa),
        (int)((((c >> 8) & 255) * ca + gg * ga * (1f - ca)) / oa),
        (int)((((c >> 16) & 255) * ca + gb * ga * (1f - ca)) / oa),
        (int)(oa * 255));
}

// Crop to the artwork plus a small margin so the splash displays it tightly.
int minX = W, minY = H, maxX = -1, maxY = -1;
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        if ((withGlow[y * W + x] >> 24) < 6) continue;
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

int pad = 18;
minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
maxX = Math.Min(W - 1, maxX + pad); maxY = Math.Min(H - 1, maxY + pad);
int cw = maxX - minX + 1, ch = maxY - minY + 1;
var cropped = new uint[cw * ch];
for (int y = 0; y < ch; y++)
    Array.Copy(withGlow, (minY + y) * W + minX, cropped, y * cw, cw);

Png.Write(outPath, cw, ch, cropped);
Console.WriteLine($"Wrote {Path.GetFullPath(outPath)} ({cw}x{ch})");

// ------------------------------------------------------------------ extra art
static class Art
{
    static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = "010101111101101", ['C'] = "011100100100011", ['E'] = "111100110100111",
        ['K'] = "101101110101101", ['N'] = "110101101101101", ['O'] = "010101101101010",
        ['S'] = "011100010001110", ['T'] = "111010010010010", [' '] = "000000000000000",
    };

    static readonly uint[] Stops =
    {
        Rgb(0x22, 0xE5, 0xFF), Rgb(0x4D, 0x9A, 0xFF), Rgb(0x4D, 0xFF, 0x9A),
        Rgb(0xFF, 0xD9, 0x3D), Rgb(0xFF, 0x9A, 0x3C), Rgb(0xC6, 0x5B, 0xFF), Rgb(0xFF, 0x4D, 0x6D),
    };

    public static uint Rgb(int r, int g, int b, int a = 255) =>
        ((uint)Math.Clamp(a, 0, 255) << 24) | ((uint)Math.Clamp(b, 0, 255) << 16) | ((uint)Math.Clamp(g, 0, 255) << 8) | (uint)Math.Clamp(r, 0, 255);

    public static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Rgb(
            (int)((a & 255) + ((b & 255) - (a & 255)) * t),
            (int)(((a >> 8) & 255) + (((b >> 8) & 255) - ((a >> 8) & 255)) * t),
            (int)(((a >> 16) & 255) + (((b >> 16) & 255) - ((a >> 16) & 255)) * t),
            (int)(((a >> 24) & 255) + (((b >> 24) & 255) - ((a >> 24) & 255)) * t));
    }

    /// <summary>Transparent RGBA canvas with the drawing primitives the logo lockups need.</summary>
    public sealed class Canvas
    {
        public readonly int W, H;
        public readonly uint[] Px;

        public Canvas(int w, int h) { W = w; H = h; Px = new uint[w * h]; }

        public void Blend(int x, int y, uint c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            uint d = Px[i];
            float sa = (c >> 24) / 255f;
            if (sa <= 0) return;
            float da = (d >> 24) / 255f;
            float oa = sa + da * (1 - sa);
            if (oa <= 0) { Px[i] = 0; return; }
            Px[i] = Rgb(
                (int)(((c & 255) * sa + (d & 255) * da * (1 - sa)) / oa),
                (int)((((c >> 8) & 255) * sa + ((d >> 8) & 255) * da * (1 - sa)) / oa),
                (int)((((c >> 16) & 255) * sa + ((d >> 16) & 255) * da * (1 - sa)) / oa),
                (int)(oa * 255));
        }

        public void Rect(int x, int y, int w, int h, uint c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Blend(xx, yy, c);
        }

        /// <summary>One neon block, bevelled the same way the in-game cells are.</summary>
        public void Tile(int x, int y, int size, uint c)
        {
            Rect(x, y, size, size, Lerp(c, Rgb(0, 0, 0), 0.62f));
            Rect(x + 2, y + 2, size - 4, size - 4, c);
            Rect(x + 3, y + 3, size - 6, 3, Rgb(255, 255, 255, 110));
            Rect(x + 3, y + size - 6, size - 6, 3, Rgb(0, 0, 0, 110));
            Rect(x + 6, y + 6, size - 12, size - 12, Rgb(255, 255, 255, 40));
        }

        public void Text(string s, int x, int y, int cell, Func<int, uint> color, int gap = 1)
        {
            int cursor = x;
            for (int i = 0; i < s.Length; i++)
            {
                if (Glyphs.TryGetValue(s[i], out var bits))
                    for (int p = 0; p < 15; p++)
                        if (bits[p] == '1')
                            Rect(cursor + (p % 3) * cell, y + (p / 3) * cell, cell, cell, color(i));
                cursor += 4 * cell * gap;
            }
        }

        static void Blur(float[] buf, int w, int h, int radius)
        {
            var tmp = new float[buf.Length];
            float norm = 1f / (radius * 2 + 1);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float sum = 0;
                for (int x = -radius; x <= radius; x++) sum += buf[row + Math.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = sum * norm;
                    sum += buf[row + Math.Clamp(x + radius + 1, 0, w - 1)] - buf[row + Math.Clamp(x - radius, 0, w - 1)];
                }
            }

            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++) sum += tmp[Math.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    buf[y * w + x] = sum * norm;
                    sum += tmp[Math.Clamp(y + radius + 1, 0, h - 1) * w + x] - tmp[Math.Clamp(y - radius, 0, h - 1) * w + x];
                }
            }
        }

        /// <summary>Adds a blurred copy of the artwork underneath it (neon bloom).</summary>
        public void Bloom(int radius, float gain)
        {
            int n = W * H;
            var pr = new float[n];
            var pg = new float[n];
            var pb = new float[n];
            var pa = new float[n];
            for (int i = 0; i < n; i++)
            {
                uint c = Px[i];
                float a = (c >> 24) / 255f;
                pr[i] = (c & 255) * a;
                pg[i] = ((c >> 8) & 255) * a;
                pb[i] = ((c >> 16) & 255) * a;
                pa[i] = a;
            }

            foreach (var chan in new[] { pr, pg, pb, pa }) { Blur(chan, W, H, radius); Blur(chan, W, H, radius); }

            var result = new uint[n];
            for (int i = 0; i < n; i++)
            {
                float ga = Math.Clamp(pa[i] * gain, 0f, 1f);
                float inv = pa[i] > 1e-4f ? 1f / pa[i] : 0f;
                float gr = Math.Min(255f, pr[i] * inv);
                float gg = Math.Min(255f, pg[i] * inv);
                float gb = Math.Min(255f, pb[i] * inv);

                uint c = Px[i];
                float ca = (c >> 24) / 255f;
                float oa = ca + ga * (1f - ca);
                if (oa <= 0) continue;

                result[i] = Rgb(
                    (int)(((c & 255) * ca + gr * ga * (1f - ca)) / oa),
                    (int)((((c >> 8) & 255) * ca + gg * ga * (1f - ca)) / oa),
                    (int)((((c >> 16) & 255) * ca + gb * ga * (1f - ca)) / oa),
                    (int)(oa * 255));
            }

            Array.Copy(result, Px, n);
        }

        public void Save(string path)
        {
            Png.Write(path, W, H, Px);
            Console.WriteLine($"Wrote {Path.GetFullPath(path)} ({W}x{H})");
        }

        /// <summary>Trims empty margin, keeping <paramref name="pad"/> transparent pixels, then writes.</summary>
        public void SaveCropped(string path, int pad)
        {
            int minX = W, minY = H, maxX = -1, maxY = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if ((Px[y * W + x] >> 24) < 6) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

            if (maxX < 0) { Save(path); return; }
            minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
            maxX = Math.Min(W - 1, maxX + pad); maxY = Math.Min(H - 1, maxY + pad);
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var cropped = new uint[cw * ch];
            for (int y = 0; y < ch; y++)
                Array.Copy(Px, (minY + y) * W + minX, cropped, y * cw, cw);

            Png.Write(path, cw, ch, cropped);
            Console.WriteLine($"Wrote {Path.GetFullPath(path)} ({cw}x{ch})");
        }
    }

    /// <summary>"NEON STACK" wordmark with a stacking-block mark: the title screen logo.</summary>
    public static void Title(string path)
    {
        var c = new Canvas(1700, 400);

        // Mark: an S-tetromino stack, one tone per tile.
        const int tile = 96, gap = 8, step = tile + gap;
        int mx = 66, my = 108;
        (int x, int y, int color)[] sShape = { (0, 1, 0), (1, 1, 1), (1, 0, 2), (2, 0, 3) };
        foreach (var (sx, sy, ci) in sShape)
            c.Tile(mx + sx * step, my + sy * step, tile, Stops[ci]);

        // Wordmark: 10 glyphs ("NEON STACK") on one line, cycling the neon palette.
        const int cell = 30;
        int tx = 66 + 3 * step + 52;
        c.Text("NEON STACK", tx, 128, cell, i => Stops[i % Stops.Length]);

        c.Bloom(radius: 5, gain: 0.85f);
        c.SaveCropped(path, 14);
    }

    /// <summary>Square app / window icon: a four-tile neon stack.</summary>
    public static void Mark(string path)
    {
        var c = new Canvas(256, 256);

        (int x, int y, int color)[] sShape = { (0, 1, 0), (1, 1, 1), (1, 0, 5), (2, 0, 4) };
        const int tile = 74, gap = 6, step = tile + gap;
        int mx = (256 - (3 * step - gap)) / 2, my = (256 - (2 * step - gap)) / 2;
        foreach (var (sx, sy, ci) in sShape)
            c.Tile(mx + sx * step, my + sy * step, tile, Stops[ci]);

        c.Bloom(radius: 4, gain: 0.9f);
        c.Save(path);
    }
}

static class Png
{
    static readonly uint[] Table = BuildTable();

    public static void Write(string path, int w, int h, uint[] rgba)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[w * 4 + 1];
            for (int y = 0; y < h; y++)
            {
                row[0] = 0;                         // filter: none
                int i = y * w;
                for (int x = 0; x < w; x++, i++)
                {
                    uint c = rgba[i];
                    row[1 + x * 4] = (byte)(c & 255);
                    row[2 + x * 4] = (byte)((c >> 8) & 255);
                    row[3 + x * 4] = (byte)((c >> 16) & 255);
                    row[4 + x * 4] = (byte)((c >> 24) & 255);
                }
                z.Write(row);
            }
        }
        byte[] idat = ms.ToArray();

        using var f = File.Create(path);
        using var bw = new BinaryWriter(f);
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        Big(ihdr, 0, w);
        Big(ihdr, 4, h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // RGBA
        Chunk(bw, "IHDR", ihdr);
        Chunk(bw, "IDAT", idat);
        Chunk(bw, "IEND", Array.Empty<byte>());
    }

    static void Chunk(BinaryWriter bw, string type, byte[] data)
    {
        Big(bw, data.Length);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        bw.Write(t);
        bw.Write(data);
        Big(bw, (int)(Crc(t, data) ^ 0xFFFFFFFF));
    }

    static void Big(byte[] buf, int off, int v)
    {
        buf[off] = (byte)(v >> 24);
        buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8);
        buf[off + 3] = (byte)v;
    }

    static void Big(BinaryWriter bw, int v)
    {
        bw.Write((byte)(v >> 24));
        bw.Write((byte)(v >> 16));
        bw.Write((byte)(v >> 8));
        bw.Write((byte)v);
    }

    static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in type) c = Table[(c ^ b) & 255] ^ (c >> 8);
        foreach (byte b in data) c = Table[(c ^ b) & 255] ^ (c >> 8);
        return c;
    }

    static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
}
