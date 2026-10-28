namespace NeonStack;

/// <summary>Simple RGBA image in 0xAABBGGRR layout (same as the framebuffer) plus a bilinear blitter.</summary>
public sealed class ImageData
{
    public readonly int W, H;
    public readonly uint[] Pixels;

    public ImageData(int w, int h, uint[] pixels)
    {
        W = w;
        H = h;
        Pixels = pixels;
    }

    public static ImageData LoadEmbedded(string resourceName)
    {
        using var stream = typeof(ImageData).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{resourceName}'");
        return PngDecoder.Decode(stream);
    }
}

/// <summary>Minimal PNG reader: 8-bit, non-interlaced, colour type 6 (RGBA). Enough for the BRUTAL logo.</summary>
static class PngDecoder
{
    public static ImageData Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] png = ms.ToArray();

        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!png.AsSpan(0, 8).SequenceEqual(sig)) throw new InvalidDataException("Not a PNG");

        int w = 0, h = 0, pos = 8;
        using var idat = new MemoryStream();

        while (pos + 8 <= png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            pos += 8;

            if (type == "IHDR")
            {
                w = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                h = (png[pos + 4] << 24) | (png[pos + 5] << 16) | (png[pos + 6] << 8) | png[pos + 7];
                int depth = png[pos + 8], colour = png[pos + 9], interlace = png[pos + 12];
                if (depth != 8 || colour != 6 || interlace != 0)
                    throw new NotSupportedException("Only 8-bit RGBA non-interlaced PNGs are supported");
            }
            else if (type == "IDAT")
            {
                idat.Write(png, pos, len);
            }
            else if (type == "IEND") break;

            pos += len + 4; // skip data + CRC
        }

        if (w <= 0 || h <= 0) throw new InvalidDataException("PNG has no IHDR");

        int stride = w * 4;
        byte[] raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read != raw.Length) throw new InvalidDataException("Truncated PNG data");
        }

        var pixels = new uint[w * h];
        var line = new byte[stride];
        var prev = new byte[stride];

        for (int y = 0; y < h; y++)
        {
            int src = y * (stride + 1);
            byte filter = raw[src];
            for (int x = 0; x < stride; x++)
            {
                int i = src + 1 + x;
                byte val = raw[i];
                int a = x >= 4 ? line[x - 4] : 0;
                int b = prev[x];
                int c = x >= 4 ? prev[x - 4] : 0;
                line[x] = filter switch
                {
                    0 => val,
                    1 => (byte)(val + a),
                    2 => (byte)(val + b),
                    3 => (byte)(val + (a + b) / 2),
                    4 => (byte)(val + Paeth(a, b, c)),
                    _ => throw new InvalidDataException("Bad PNG filter"),
                };
            }

            for (int x = 0; x < w; x++)
            {
                int o = x * 4;
                byte r = line[o], g = line[o + 1], bl = line[o + 2], a = line[o + 3];
                pixels[y * w + x] = ((uint)a << 24) | ((uint)bl << 16) | ((uint)g << 8) | r;
            }

            (line, prev) = (prev, line);
        }

        return new ImageData(w, h, pixels);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
