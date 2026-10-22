namespace NeonStack;

/// <summary>Draws a TetrisGame into a Framebuffer. No platform code in here.</summary>
public sealed class Scene
{
    const int C = TetrisGame.Cell;
    const uint White = 0xFFFFFFFF, Black = 0xFF000000;
    static readonly uint Dim = Framebuffer.Hex(0x7C84A8);
    static readonly uint Ink = Framebuffer.Hex(0xEEF1FF);

    static uint Col(int type) => Framebuffer.Hex(TetrisGame.Palette[type]);

    public void Render(Framebuffer fb, TetrisGame g)
    {
        Background(fb, g);

        float sh = g.Shake;
        int ox = (int)(Math.Sin(g.Time * 97f) * sh), oy = (int)(Math.Cos(g.Time * 83f) * sh);
        int bx = TetrisGame.OriginX + ox, by = TetrisGame.OriginY + oy;

        Board(fb, g, bx, by);
        SidePanels(fb, g);

        foreach (var p in g.Particles)
        {
            float k = Math.Clamp(p.Life / Math.Max(0.01f, Math.Min(p.Max, 1.5f)), 0f, 1f);
            fb.RectAdd((int)p.X + ox, (int)p.Y + oy, p.Size, p.Size, p.Color, (int)(255 * k));
        }

        if (g.MsgT > 0 && g.Phase != Phase.Title)
        {
            float k = Math.Min(1f, g.MsgT * 2f);
            int rise = (int)((1.2f - Math.Min(g.MsgT, 1.2f)) * 18);
            uint c = Framebuffer.Lerp(Framebuffer.Hex(0x0A0B14), Framebuffer.Hex(0xFFD93D), k);
            fb.TextCentered(g.Msg, by + 110 - rise, 3, c);
        }

        switch (g.Phase)
        {
            case Phase.Title: Title(fb, g); break;
            case Phase.Paused:
                fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 150);
                fb.TextCentered("PAUSED", 140, 6, Ink);
                fb.TextCentered("P TO RESUME", 190, 2, Dim);
                break;
            case Phase.Over:
                fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 160);
                fb.TextCentered("GAME OVER", 110, 6, Framebuffer.Hex(0xFF4D6D));
                fb.TextCentered("SCORE " + g.Score, 175, 3, Ink);
                if (g.Score > 0 && g.Score >= g.Best) fb.TextCentered("NEW BEST", 205, 2, Framebuffer.Hex(0xFFD93D));
                if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO RETRY", 250, 2, Dim);
                break;
        }

        fb.Scanlines();
    }

    // ------------------------------------------------------------ background
    void Background(Framebuffer fb, TetrisGame g)
    {
        uint top = Framebuffer.Hex(0x0A0B14), bot = Framebuffer.Hex(0x1A1033);
        for (int y = 0; y < Framebuffer.H; y++)
            Array.Fill(fb.Pixels, Framebuffer.Lerp(top, bot, y / (float)Framebuffer.H), y * Framebuffer.W, Framebuffer.W);

        float t = g.Time * 0.25f;
        int lv = g.Level;
        fb.Glow((int)(320 + Math.Cos(t) * 230), (int)(180 + Math.Sin(t * 1.3f) * 90), 190, Col((lv + 0) % 7), 38);
        fb.Glow((int)(320 + Math.Cos(t * 0.8f + 2f) * 250), (int)(180 + Math.Sin(t + 1f) * 110), 170, Col((lv + 2) % 7), 34);
        fb.Glow((int)(320 + Math.Cos(t * 1.1f + 4f) * 200), (int)(180 + Math.Sin(t * 0.7f + 3f) * 100), 150, Col((lv + 4) % 7), 30);

        // slow perspective-less grid
        int off = (int)(g.Time * 6) % 32;
        for (int x = -off; x < Framebuffer.W; x += 32) fb.RectA(x, 0, 1, Framebuffer.H, White, 7);
        for (int y = off; y < Framebuffer.H; y += 32) fb.RectA(0, y, Framebuffer.W, 1, White, 7);
    }

    // ------------------------------------------------------------ board
    void Board(Framebuffer fb, TetrisGame g, int bx, int by)
    {
        int bw = TetrisGame.Cols * C, bh = TetrisGame.Rows * C;
        fb.RectA(bx - 6, by - 6, bw + 12, bh + 12, Framebuffer.Hex(0x22E5FF), 22);
        fb.RectA(bx - 3, by - 3, bw + 6, bh + 6, Black, 215);
        for (int i = 1; i < TetrisGame.Cols; i++) fb.RectA(bx + i * C, by, 1, bh, White, 9);
        for (int j = 1; j < TetrisGame.Rows; j++) fb.RectA(bx, by + j * C, bw, 1, White, 9);
        uint edge = Framebuffer.Hex(0x22E5FF);
        fb.RectA(bx - 3, by - 3, bw + 6, 1, edge, 200);
        fb.RectA(bx - 3, by + bh + 2, bw + 6, 1, edge, 200);
        fb.RectA(bx - 3, by - 3, 1, bh + 6, edge, 200);
        fb.RectA(bx + bw + 2, by - 3, 1, bh + 6, edge, 200);

        bool clearing = g.Phase == Phase.Clearing;
        float prog = clearing ? 1f - g.ClearT / TetrisGame.ClearDur : 0f;

        for (int y = 0; y < TetrisGame.Rows; y++)
        {
            bool flashRow = clearing && g.ClearRows.Contains(y);
            for (int x = 0; x < TetrisGame.Cols; x++)
            {
                int v = g.Board[x, y];
                if (v == 0) continue;
                uint c = Col(v - 1);
                if (flashRow)
                {
                    if (Math.Abs(x - 4.5f) < prog * 5.2f) continue;           // wipe from the centre outwards
                    c = Framebuffer.Lerp(c, White, 0.5f + 0.5f * (float)Math.Sin(prog * 25f));
                }
                Cell(fb, bx + x * C, by + y * C, C, c);
            }
        }

        if (g.Phase == Phase.Playing || g.Phase == Phase.Paused)
        {
            var cells = TetrisGame.Shapes[g.Cur.Type][g.Cur.Rot];
            uint c = Col(g.Cur.Type);
            int gy = g.GhostY();
            if (gy != g.Cur.Y)
                foreach (var s in cells)
                {
                    int px = bx + (g.Cur.X + s.x) * C, py = by + (gy + s.y) * C;
                    if (gy + s.y < 0) continue;
                    fb.RectA(px + 1, py + 1, C - 2, C - 2, c, 28);
                    fb.RectA(px + 1, py + 1, C - 2, 1, c, 170);
                    fb.RectA(px + 1, py + C - 2, C - 2, 1, c, 170);
                    fb.RectA(px + 1, py + 1, 1, C - 2, c, 170);
                    fb.RectA(px + C - 2, py + 1, 1, C - 2, c, 170);
                }
            foreach (var s in cells)
                if (g.Cur.Y + s.y >= 0) Cell(fb, bx + (g.Cur.X + s.x) * C, by + (g.Cur.Y + s.y) * C, C, c);
        }
    }

    static void Cell(Framebuffer fb, int px, int py, int size, uint c)
    {
        fb.RectAdd(px - 3, py - 3, size + 6, size + 6, c, 9);
        fb.RectAdd(px - 1, py - 1, size + 2, size + 2, c, 18);
        fb.Rect(px, py, size, size, Framebuffer.Lerp(c, Black, 0.55f));
        fb.Rect(px + 1, py + 1, size - 2, size - 2, c);
        fb.RectA(px + 1, py + 1, size - 2, 3, White, 80);
        fb.RectA(px + 1, py + size - 4, size - 2, 3, Black, 70);
        fb.RectA(px + 4, py + 4, size - 8, size - 8, White, 26);
    }

    // ------------------------------------------------------------ panels
    void SidePanels(Framebuffer fb, TetrisGame g)
    {
        fb.Text("HOLD", 60, 24, 2, Dim);
        Box(fb, 60, 44, 104, 64);
        if (g.Hold >= 0) Mini(fb, g.Hold, 60, 44, 104, 64, 12, g.HoldUsed ? 70 : 255);

        Stat(fb, "SCORE", g.Score.ToString(), 60, 130, Ink);
        Stat(fb, "BEST", g.Best.ToString(), 60, 180, Dim);
        Stat(fb, "LEVEL", g.Level.ToString(), 60, 230, Ink);
        Stat(fb, "LINES", g.Lines.ToString(), 60, 280, Ink);

        fb.Text("NEXT", 440, 24, 2, Dim);
        for (int i = 0; i < 3; i++)
        {
            Box(fb, 440, 44 + i * 74, 104, 64);
            Mini(fb, g.Preview(i), 440, 44 + i * 74, 104, 64, i == 0 ? 12 : 10, i == 0 ? 255 : 190);
        }
    }

    static void Stat(Framebuffer fb, string label, string value, int x, int y, uint c)
    {
        fb.Text(label, x, y, 2, Framebuffer.Hex(0x5B6288));
        fb.Text(value, x, y + 16, 3, c);
    }

    static void Box(Framebuffer fb, int x, int y, int w, int h)
    {
        fb.RectA(x, y, w, h, Black, 150);
        uint e = Framebuffer.Hex(0x3A4070);
        fb.RectA(x, y, w, 1, e, 255); fb.RectA(x, y + h - 1, w, 1, e, 255);
        fb.RectA(x, y, 1, h, e, 255); fb.RectA(x + w - 1, y, 1, h, e, 255);
    }

    static void Mini(Framebuffer fb, int type, int bx, int by, int bw, int bh, int size, int alpha)
    {
        var cells = TetrisGame.Shapes[type][0];
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in cells) { minX = Math.Min(minX, c.x); maxX = Math.Max(maxX, c.x); minY = Math.Min(minY, c.y); maxY = Math.Max(maxY, c.y); }
        int w = (maxX - minX + 1) * size, h = (maxY - minY + 1) * size;
        int sx = bx + (bw - w) / 2, sy = by + (bh - h) / 2;
        uint col = Framebuffer.Lerp(Framebuffer.Hex(0x0A0B14), Col(type), alpha / 255f);
        foreach (var c in cells) Cell(fb, sx + (c.x - minX) * size, sy + (c.y - minY) * size, size, col);
    }

    // ------------------------------------------------------------ title
    void Title(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 170);
        const string name = "NEON STACK";
        int scale = 8;
        int x = (Framebuffer.W - Framebuffer.TextWidth(name, scale)) / 2;
        for (int i = 0; i < name.Length; i++)
        {
            int bob = (int)(Math.Sin(g.Time * 3f + i * 0.6f) * 4);
            uint c = Col(i % 7);
            fb.Text(name[i].ToString(), x + i * 4 * scale + 2, 78 + bob + 2, scale, Framebuffer.Lerp(c, Black, 0.65f));
            fb.Text(name[i].ToString(), x + i * 4 * scale, 78 + bob, scale, c);
        }
        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("PRESS ENTER", 170, 3, Ink);

        fb.TextCentered("ARROWS OR WASD  MOVE", 218, 2, Dim);
        fb.TextCentered("UP OR X  ROTATE   Z  ROTATE BACK", 234, 2, Dim);
        fb.TextCentered("SPACE  HARD DROP   C  HOLD", 250, 2, Dim);
        fb.TextCentered("P  PAUSE   ESC  QUIT", 266, 2, Dim);
        if (g.Best > 0) fb.TextCentered("BEST " + g.Best, 296, 2, Framebuffer.Hex(0xFFD93D));
        fb.TextCentered("BUILT ON BRUTAL", 332, 2, Framebuffer.Hex(0x5B6288));
    }
}
