namespace NeonStack;

/// <summary>CPU-side 640x360 RGBA framebuffer with a tiny software rasteriser and a 3x5 pixel font.</summary>
public sealed class Framebuffer
{
    public const int W = 640, H = 360;

    // Memory layout is R,G,B,A bytes (little-endian uint = 0xAABBGGRR), which is what GL_RGBA/GL_UNSIGNED_BYTE expects.
    public readonly uint[] Pixels = new uint[W * H];

    public static uint Rgb(int r, int g, int b) => 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r;
    public static uint Hex(int rgb) => Rgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    public static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int r = (int)((a & 255) + (((b & 255) - (a & 255)) * t));
        int g = (int)(((a >> 8) & 255) + ((((b >> 8) & 255) - ((a >> 8) & 255)) * t));
        int bl = (int)(((a >> 16) & 255) + ((((b >> 16) & 255) - ((a >> 16) & 255)) * t));
        return Rgb(r, g, bl);
    }

    public void Clear(uint c) => Array.Fill(Pixels, c);

    public void Rect(int x, int y, int w, int h, uint c)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0), x1 = Math.Min(x + w, W), y1 = Math.Min(y + h, H);
        if (x0 >= x1 || y0 >= y1) return;
        for (int yy = y0; yy < y1; yy++) Pixels.AsSpan(yy * W + x0, x1 - x0).Fill(c | 0xFF000000u);
    }

    /// <summary>Alpha blend, a = 0..255.</summary>
    public void RectA(int x, int y, int w, int h, uint c, int a)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0), x1 = Math.Min(x + w, W), y1 = Math.Min(y + h, H);
        if (x0 >= x1 || y0 >= y1) return;
        int ia = 255 - a;
        int sr = (int)(c & 255) * a, sg = (int)((c >> 8) & 255) * a, sb = (int)((c >> 16) & 255) * a;
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * W + x0;
            for (int xx = x0; xx < x1; xx++, i++)
            {
                uint d = Pixels[i];
                int r = ((int)(d & 255) * ia + sr) / 255;
                int g = ((int)((d >> 8) & 255) * ia + sg) / 255;
                int b = ((int)((d >> 16) & 255) * ia + sb) / 255;
                Pixels[i] = Rgb(r, g, b);
            }
        }
    }

    /// <summary>Additive blend (light), a = 0..255.</summary>
    public void RectAdd(int x, int y, int w, int h, uint c, int a)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0), x1 = Math.Min(x + w, W), y1 = Math.Min(y + h, H);
        if (x0 >= x1 || y0 >= y1) return;
        int ar = (int)(c & 255) * a / 255, ag = (int)((c >> 8) & 255) * a / 255, ab = (int)((c >> 16) & 255) * a / 255;
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * W + x0;
            for (int xx = x0; xx < x1; xx++, i++)
            {
                uint d = Pixels[i];
                Pixels[i] = Rgb(Math.Min(255, (int)(d & 255) + ar), Math.Min(255, (int)((d >> 8) & 255) + ag), Math.Min(255, (int)((d >> 16) & 255) + ab));
            }
        }
    }

    /// <summary>Soft radial light, additive. intensity 0..255 at the centre.</summary>
    public void Glow(int cx, int cy, int radius, uint c, int intensity)
    {
        int x0 = Math.Max(cx - radius, 0), x1 = Math.Min(cx + radius, W - 1);
        int y0 = Math.Max(cy - radius, 0), y1 = Math.Min(cy + radius, H - 1);
        float r2 = (float)radius * radius;
        int cr = (int)(c & 255), cg = (int)((c >> 8) & 255), cb = (int)((c >> 16) & 255);
        for (int y = y0; y <= y1; y++)
        {
            int dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx;
                float d2 = dx * dx + dy * dy;
                if (d2 >= r2) continue;
                float f = 1f - d2 / r2;
                int a = (int)(f * f * intensity);
                int i = y * W + x;
                uint d = Pixels[i];
                Pixels[i] = Rgb(Math.Min(255, (int)(d & 255) + cr * a / 255), Math.Min(255, (int)((d >> 8) & 255) + cg * a / 255), Math.Min(255, (int)((d >> 16) & 255) + cb * a / 255));
            }
        }
    }

    /// <summary>CRT-style darkening of every second row. Cheap, and it sells the retro look.</summary>
    public void Scanlines()
    {
        for (int y = 1; y < H; y += 2)
        {
            int i = y * W;
            for (int x = 0; x < W; x++, i++)
            {
                uint d = Pixels[i];
                Pixels[i] = Rgb((int)(d & 255) * 228 >> 8, (int)((d >> 8) & 255) * 228 >> 8, (int)((d >> 16) & 255) * 228 >> 8);
            }
        }
    }

    // ---- font: 3x5 glyphs, rows top to bottom, '1' = lit ----
    static readonly Dictionary<char, string> Glyphs = new()
    {
        ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['3'] = "111001111001111",
        ['4'] = "101101111001001", ['5'] = "111100111001111", ['6'] = "111100111101111", ['7'] = "111001001001001",
        ['8'] = "111101111101111", ['9'] = "111101111001111",
        ['A'] = "010101111101101", ['B'] = "110101110101110", ['C'] = "011100100100011", ['D'] = "110101101101110",
        ['E'] = "111100110100111", ['F'] = "111100110100100", ['G'] = "011100101101011", ['H'] = "101101111101101",
        ['I'] = "111010010010111", ['J'] = "001001001101010", ['K'] = "101101110101101", ['L'] = "100100100100111",
        ['M'] = "101111111101101", ['N'] = "110101101101101", ['O'] = "010101101101010", ['P'] = "110101110100100",
        ['Q'] = "010101101110011", ['R'] = "110101110101101", ['S'] = "011100010001110", ['T'] = "111010010010010",
        ['U'] = "101101101101111", ['V'] = "101101101101010", ['W'] = "101101111111101", ['X'] = "101101010101101",
        ['Y'] = "101101010010010", ['Z'] = "111001010100111",
        [':'] = "000010000010000", ['-'] = "000000111000000", ['.'] = "000000000000010",
    };

    public static int TextWidth(string s, int scale) => s.Length == 0 ? 0 : s.Length * 4 * scale - scale;

    public void Text(string s, int x, int y, int scale, uint c)
    {
        foreach (char ch in s)
        {
            if (Glyphs.TryGetValue(char.ToUpperInvariant(ch), out var bits))
                for (int i = 0; i < 15; i++)
                    if (bits[i] == '1') Rect(x + (i % 3) * scale, y + (i / 3) * scale, scale, scale, c);
            x += 4 * scale;
        }
    }

    public void TextCentered(string s, int y, int scale, uint c) => Text(s, (W - TextWidth(s, scale)) / 2, y, scale, c);
}
