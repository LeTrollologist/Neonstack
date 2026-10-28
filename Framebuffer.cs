namespace NeonStack;

/// <summary>
/// CPU-side framebuffer. All drawing is authored in a fixed 640x360 logical space, but the buffer
/// itself is allocated at an integer <see cref="Scale"/> (1..6). That way the game is rendered at
/// (or near) the window's native resolution and blitted 1:1, instead of depending on a fractional
/// GL pixel zoom, which produces uneven pixels, shimmering hairlines and stretched aspect ratios.
/// </summary>
public sealed class Framebuffer
{
    public const int W = 640, H = 360;          // logical design space, used by Scene/TetrisGame
    public const int MaxScale = 6;              // 6x = 3840x2160

    /// <summary>Physical pixels per logical pixel.</summary>
    public readonly int Scale;

    public readonly int PixelW, PixelH;

    // Memory layout is R,G,B,A bytes (little-endian uint = 0xAABBGGRR), which is what GL_RGBA/GL_UNSIGNED_BYTE expects.
    public readonly uint[] Pixels;

    public Framebuffer(int scale)
    {
        Scale = Math.Clamp(scale, 1, MaxScale);
        PixelW = W * Scale;
        PixelH = H * Scale;
        Pixels = new uint[PixelW * PixelH];
    }

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

    /// <summary>Fills one logical row across the full logical width (used for the gradient background).</summary>
    public void FillRow(int y, uint c)
    {
        if (y < 0 || y >= H) return;
        int row = y * Scale;
        for (int yy = row; yy < row + Scale; yy++)
            Pixels.AsSpan(yy * PixelW, PixelW).Fill(c);
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        int s = Scale;
        int x0 = Math.Max(x * s, 0), y0 = Math.Max(y * s, 0);
        int x1 = Math.Min((x + w) * s, PixelW), y1 = Math.Min((y + h) * s, PixelH);
        if (x0 >= x1 || y0 >= y1) return;
        for (int yy = y0; yy < y1; yy++) Pixels.AsSpan(yy * PixelW + x0, x1 - x0).Fill(c | 0xFF000000u);
    }

    /// <summary>Alpha blend, a = 0..255.</summary>
    public void RectA(int x, int y, int w, int h, uint c, int a)
    {
        int s = Scale;
        int x0 = Math.Max(x * s, 0), y0 = Math.Max(y * s, 0);
        int x1 = Math.Min((x + w) * s, PixelW), y1 = Math.Min((y + h) * s, PixelH);
        if (x0 >= x1 || y0 >= y1) return;
        int ia = 255 - a;
        int sr = (int)(c & 255) * a, sg = (int)((c >> 8) & 255) * a, sb = (int)((c >> 16) & 255) * a;
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * PixelW + x0;
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
        int s = Scale;
        int x0 = Math.Max(x * s, 0), y0 = Math.Max(y * s, 0);
        int x1 = Math.Min((x + w) * s, PixelW), y1 = Math.Min((y + h) * s, PixelH);
        if (x0 >= x1 || y0 >= y1) return;
        int ar = (int)(c & 255) * a / 255, ag = (int)((c >> 8) & 255) * a / 255, ab = (int)((c >> 16) & 255) * a / 255;
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * PixelW + x0;
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
        int s = Scale;
        int px = cx * s, py = cy * s, pr = radius * s;
        int x0 = Math.Max(px - pr, 0), x1 = Math.Min(px + pr, PixelW - 1);
        int y0 = Math.Max(py - pr, 0), y1 = Math.Min(py + pr, PixelH - 1);
        if (x0 > x1 || y0 > y1 || pr <= 0) return;
        float r2 = (float)pr * pr;
        int cr = (int)(c & 255), cg = (int)((c >> 8) & 255), cb = (int)((c >> 16) & 255);
        for (int y = y0; y <= y1; y++)
        {
            int dy = y - py;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - px;
                float d2 = dx * dx + dy * dy;
                if (d2 >= r2) continue;
                float f = 1f - d2 / r2;
                int a = (int)(f * f * intensity);
                int i = y * PixelW + x;
                uint d = Pixels[i];
                Pixels[i] = Rgb(Math.Min(255, (int)(d & 255) + cr * a / 255), Math.Min(255, (int)((d >> 8) & 255) + cg * a / 255), Math.Min(255, (int)((d >> 16) & 255) + cb * a / 255));
            }
        }
    }

    /// <summary>
    /// CRT-style darkening. One physical row per <see cref="Scale"/> is dimmed, so the scanline
    /// density stays constant (thin lines) whether the game runs at 1x or 6x.
    /// </summary>
    public void Scanlines()
    {
        int period = Math.Max(2, Scale);
        for (int y = period - 1; y < PixelH; y += period)
        {
            int i = y * PixelW;
            for (int x = 0; x < PixelW; x++, i++)
            {
                uint d = Pixels[i];
                Pixels[i] = Rgb((int)(d & 255) * 228 >> 8, (int)((d >> 8) & 255) * 228 >> 8, (int)((d >> 16) & 255) * 228 >> 8);
            }
        }
    }

    /// <summary>
    /// Bilinear, alpha-blended image blit. Destination is a logical rect; sampling happens in the
    /// physical framebuffer so the logo stays smooth at any render scale.
    /// </summary>
    public void Blit(ImageData img, int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0 || img.W <= 0 || img.H <= 0) return;

        int s = Scale;
        int dx0 = x * s, dy0 = y * s;
        int x0 = Math.Max(dx0, 0), y0 = Math.Max(dy0, 0);
        int x1 = Math.Min(dx0 + w * s, PixelW), y1 = Math.Min(dy0 + h * s, PixelH);
        if (x0 >= x1 || y0 >= y1) return;

        float fw = w * s, fh = h * s;
        for (int yy = y0; yy < y1; yy++)
        {
            float v = ((yy - dy0) + 0.5f) / fh * img.H - 0.5f;
            int v0 = (int)MathF.Floor(v);
            float fv = v - v0;
            int row0 = Math.Clamp(v0, 0, img.H - 1) * img.W;
            int row1 = Math.Clamp(v0 + 1, 0, img.H - 1) * img.W;

            int i = yy * PixelW + x0;
            for (int xx = x0; xx < x1; xx++, i++)
            {
                float u = ((xx - dx0) + 0.5f) / fw * img.W - 0.5f;
                int u0 = (int)MathF.Floor(u);
                float fu = u - u0;
                int c0 = Math.Clamp(u0, 0, img.W - 1);
                int c1 = Math.Clamp(u0 + 1, 0, img.W - 1);

                uint p00 = img.Pixels[row0 + c0], p10 = img.Pixels[row0 + c1];
                uint p01 = img.Pixels[row1 + c0], p11 = img.Pixels[row1 + c1];

                float w00 = (1f - fu) * (1f - fv), w10 = fu * (1f - fv);
                float w01 = (1f - fu) * fv, w11 = fu * fv;

                float a00 = p00 >> 24, a10 = p10 >> 24, a01 = p01 >> 24, a11 = p11 >> 24;
                float alpha = (a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11) / 255f;
                if (alpha <= 0.002f) continue;

                // Interpolate premultiplied so transparent edges don't produce dark fringes.
                float sr = ((p00 & 255) * a00 * w00 + (p10 & 255) * a10 * w10 + (p01 & 255) * a01 * w01 + (p11 & 255) * a11 * w11) / 255f;
                float sg = (((p00 >> 8) & 255) * a00 * w00 + ((p10 >> 8) & 255) * a10 * w10 + ((p01 >> 8) & 255) * a01 * w01 + ((p11 >> 8) & 255) * a11 * w11) / 255f;
                float sb = (((p00 >> 16) & 255) * a00 * w00 + ((p10 >> 16) & 255) * a10 * w10 + ((p01 >> 16) & 255) * a01 * w01 + ((p11 >> 16) & 255) * a11 * w11) / 255f;

                uint d = Pixels[i];
                float inv = 1f - alpha;
                int r = (int)((d & 255) * inv + sr + 0.5f);
                int g = (int)(((d >> 8) & 255) * inv + sg + 0.5f);
                int b = (int)(((d >> 16) & 255) * inv + sb + 0.5f);
                Pixels[i] = Rgb(Math.Min(r, 255), Math.Min(g, 255), Math.Min(b, 255));
            }
        }
    }

    // ---- font: 3x5 glyphs, rows top to bottom, '1' = lit (coordinates are logical) ----
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
        ['/'] = "001001010100100", ['!'] = "010010010000010", ['+'] = "000010111010000",
        ['<'] = "001010100010001", ['>'] = "100010001010100", [','] = "000000000010100",
        ['('] = "001010010010001", [')'] = "100010010010100",
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
