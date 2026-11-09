namespace NeonStack;

/// <summary>Draws a TetrisGame into a Framebuffer. No platform code in here.</summary>
public sealed class Scene
{
    const int C = TetrisGame.Cell;
    const uint White = 0xFFFFFFFF, Black = 0xFF000000;
    static readonly uint Dim = Framebuffer.Hex(0x7C84A8);
    static readonly uint Ink = Framebuffer.Hex(0xEEF1FF);

    // Official BRUTAL logo (white on transparent), the generated Audax Softworks logo and the
    // generated NEON STACK title lockup.
    static readonly ImageData BrutalLogo = ImageData.LoadEmbedded("NeonStack.Assets.logo_brutal_wht.png");
    static readonly ImageData AudaxLogo = ImageData.LoadEmbedded("NeonStack.Assets.logo_audax.png");
    static readonly ImageData TitleLogo = ImageData.LoadEmbedded("NeonStack.Assets.logo_neonstack.png");

    static uint Col(int type) => Framebuffer.Hex(TetrisGame.Palette[type]);

    /// <summary>Per-level accent from the campaign table; the title screen previews the selected level.</summary>
    static uint Accent(TetrisGame g)
    {
        int index = g.Phase == Phase.Title ? g.SelectedLevel : g.LevelIndex;
        return Framebuffer.Hex(TetrisGame.Levels[index].Accent);
    }

    public void Render(Framebuffer fb, TetrisGame g)
    {
        Background(fb, g);

        float sh = Settings.Current.Shake ? g.Shake : 0f;
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

        foreach (var p in g.Popups)
        {
            float k = 1f - p.T / p.Life;
            uint c = Framebuffer.Lerp(Framebuffer.Hex(0x0A0B14), p.Color, Math.Clamp(k * 1.5f, 0f, 1f));
            fb.Text(p.Text, (int)p.X - Framebuffer.TextWidth(p.Text, 3) / 2, (int)p.Y, 3, c);
        }

        switch (g.Phase)
        {
            case Phase.Title: Title(fb, g); break;
            case Phase.Paused:
                fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 150);
                fb.TextCentered("PAUSED", 140, 6, Ink);
                fb.TextCentered("P TO RESUME", 190, 2, Dim);
                break;
            case Phase.LevelClear: LevelClearScreen(fb, g); break;
            case Phase.Over:
                fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 160);
                fb.TextCentered("GAME OVER", 100, 6, Framebuffer.Hex(0xFF4D6D));
                fb.TextCentered("LEVEL " + g.Level + "  " + g.LevelName, 160, 2, Dim);
                fb.TextCentered("SCORE " + g.Score, 184, 3, Ink);
                if (g.Score > 0 && g.Score >= g.Best) fb.TextCentered("NEW BEST", 212, 2, Framebuffer.Hex(0xFFD93D));
                if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO RETRY LEVEL   ESC FOR TITLE", 254, 2, Dim);
                break;
            case Phase.Won: WonScreen(fb, g); break;
            case Phase.Credits: CreditsScreen(fb, g); break;
            case Phase.Settings: SettingsScreen(fb, g); break;
            case Phase.Leaderboard: LeaderboardScreen(fb, g); break;
            case Phase.NameEntry: NameEntryScreen(fb, g); break;
        }

        if (g.Flash > 0) fb.RectAdd(0, 0, Framebuffer.W, Framebuffer.H, g.FlashColor, (int)(g.Flash * 90f));
        if (Settings.Current.Scanlines) fb.Scanlines();
    }

    // ------------------------------------------------------------ background
    void Background(Framebuffer fb, TetrisGame g)
    {
        uint accent = Accent(g);
        uint top = Framebuffer.Lerp(Framebuffer.Hex(0x0A0B14), accent, 0.05f);
        uint bot = Framebuffer.Lerp(Framebuffer.Hex(0x1A1033), accent, 0.10f);
        for (int y = 0; y < Framebuffer.H; y++)
            fb.FillRow(y, Framebuffer.Lerp(top, bot, y / (float)Framebuffer.H));

        float t = g.Time * 0.25f;
        int lv = g.Level;
        fb.Glow((int)(320 + Math.Cos(t) * 230), (int)(180 + Math.Sin(t * 1.3f) * 90), 190, accent, 40);
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
        uint accent = Accent(g);
        fb.RectA(bx - 6, by - 6, bw + 12, bh + 12, accent, 22);
        fb.RectA(bx - 3, by - 3, bw + 6, bh + 6, Black, 215);
        for (int i = 1; i < TetrisGame.Cols; i++) fb.RectA(bx + i * C, by, 1, bh, White, 9);
        for (int j = 1; j < TetrisGame.Rows; j++) fb.RectA(bx, by + j * C, bw, 1, White, 9);
        uint edge = accent;
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
            if (Settings.Current.Ghost && gy != g.Cur.Y)
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
        if (g.BackToBack) fb.Text("B2B CHAIN", 60, 112, 2, Framebuffer.Hex(0xFF9A3C));
        Stat(fb, "BEST", g.Best.ToString(), 60, 180, Dim);
        Stat(fb, "LEVEL", g.Level + "/" + TetrisGame.Levels.Length, 60, 226, Ink);
        Stat(fb, "GOAL", g.LevelLines + "/" + g.TargetLines, 60, 276, Ink);
        fb.Text(g.LevelName, 60, 334, 2, Accent(g));

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

        // The generated NEON STACK lockup, bobbing gently where the text title used to sit.
        int w = 528;
        int h = Math.Max(1, (int)MathF.Round(w * (float)TitleLogo.H / TitleLogo.W));
        int bob = (int)(Math.Sin(g.Time * 1.6f) * 3);
        fb.Blit(TitleLogo, (Framebuffer.W - w) / 2, 32 + bob, w, h);

        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("PRESS ENTER", 150, 3, Ink);

        fb.TextCentered("ARROWS OR WASD  MOVE", 192, 2, Dim);
        fb.TextCentered("UP OR X  ROTATE   Z  ROTATE BACK", 208, 2, Dim);
        fb.TextCentered("SPACE  HARD DROP   C  HOLD", 224, 2, Dim);
        fb.TextCentered("P  PAUSE   ESC  QUIT   F11  FULLSCREEN", 240, 2, Dim);
        fb.TextCentered("LEFT RIGHT LEVEL   S SETTINGS   L SCORES   C CREDITS   M MUSIC", 258, 2, Framebuffer.Hex(0x5B6288));

        var lv = TetrisGame.Levels[g.SelectedLevel];
        fb.TextCentered("<  " + (g.SelectedLevel + 1) + "  " + lv.Name + "  >", 280, 2, Framebuffer.Hex(0xFFD93D));
        if (g.Best > 0) fb.TextCentered("BEST " + g.Best, 302, 2, Framebuffer.Hex(0x5B6288));
        fb.TextCentered("BUILT ON BRUTAL", 322, 2, Framebuffer.Hex(0x5B6288));
    }

    // ------------------------------------------------------------ campaign screens
    void LevelClearScreen(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 175);
        fb.TextCentered("LEVEL CLEAR", 84, 6, Framebuffer.Hex(0x4DFF9A));
        fb.TextCentered("LEVEL " + g.Level + "  " + g.LevelName, 146, 3, Ink);
        fb.TextCentered("GOAL " + g.TargetLines + " LINES CLEARED", 178, 2, Dim);
        fb.TextCentered("SCORE " + g.Score, 204, 3, Framebuffer.Hex(0xFFD93D));
        if (g.Level < TetrisGame.Levels.Length)
            fb.TextCentered("NEXT  " + (g.Level + 1) + "  " + TetrisGame.Levels[g.Level].Name, 240, 2, Dim);

        int secs = Math.Max(0, (int)MathF.Ceiling(g.LevelClearT));
        fb.TextCentered("CONTINUING IN " + secs, 272, 2, Framebuffer.Hex(0x5B6288));
        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO CONTINUE", 300, 2, Dim);
    }

    void WonScreen(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 180);
        fb.TextCentered("CAMPAIGN CLEAR", 92, 5, Framebuffer.Hex(0xFFD93D));
        fb.TextCentered("ALL " + TetrisGame.Levels.Length + " LEVELS STACKED", 140, 2, Dim);
        fb.TextCentered("FINAL SCORE " + g.Score, 176, 3, Ink);
        fb.TextCentered("BEST " + g.Best, 208, 2, Framebuffer.Hex(0x5B6288));
        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO RETURN", 264, 2, Dim);
    }

    void CreditsScreen(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 250);
        fb.TextCentered("NEON STACK", 44, 6, Ink);
        fb.TextCentered("A NEON TETRIS CLONE BY AUDAX SOFTWORKS", 90, 2, Dim);

        fb.TextCentered("BUILT WITH BRUTAL", 126, 3, Framebuffer.Hex(0x22E5FF));
        fb.TextCentered("BRUTAL GAME DEVELOPMENT FRAMEWORK", 158, 2, Ink);
        fb.TextCentered("COPYRIGHT C 2024 AHWOO LIMITED", 176, 2, Dim);
        fb.TextCentered("LICENSED UNDER THE BRUTAL LICENSE V1.0", 192, 2, Dim);
        fb.TextCentered("WWW.AHWOO.COM/BRUTAL", 208, 2, Framebuffer.Hex(0x5B6288));

        fb.TextCentered("MUSIC AND SOUND SYNTHESISED AT RUNTIME", 244, 2, Dim);
        fb.TextCentered(Audio.MusicEnabled ? "MUSIC ON  ( M )" : "MUSIC OFF  ( M )", 262, 2, Framebuffer.Hex(0x5B6288));
        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO RETURN", 306, 2, Dim);
    }

    // ------------------------------------------------------------ settings + scores
    void SettingsScreen(Framebuffer fb, TetrisGame g)
    {
        Settings s = Settings.Current;
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 250);
        fb.TextCentered("SETTINGS", 30, 5, Ink);

        string[] labels = { "MUSIC VOLUME", "SFX VOLUME", "GHOST PIECE", "SCANLINES", "SCREEN SHAKE", "FULLSCREEN", "BACK" };
        for (int i = 0; i < labels.Length; i++)
        {
            int y = 86 + i * 28;
            bool sel = i == g.SettingsIndex;
            uint c = sel ? Framebuffer.Hex(0xFFD93D) : Dim;

            if (sel) fb.Text(">", 94, y, 2, c);
            fb.Text(labels[i], 114, y, 2, c);

            string val = i switch
            {
                0 => s.MusicVolume + "/10",
                1 => s.SfxVolume + "/10",
                2 => s.Ghost ? "ON" : "OFF",
                3 => s.Scanlines ? "ON" : "OFF",
                4 => s.Shake ? "ON" : "OFF",
                5 => s.Fullscreen ? "ON" : "OFF",
                _ => "",
            };
            if (val.Length > 0) fb.Text("< " + val + " >", 428, y, 2, c);
        }

        fb.TextCentered("UP DOWN SELECT   LEFT RIGHT CHANGE   ENTER BACK", 318, 2, Framebuffer.Hex(0x5B6288));
    }

    void LeaderboardScreen(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 250);
        fb.TextCentered("LEADERBOARD", 24, 5, Ink);

        var entries = Leaderboard.Entries;
        fb.Text("RK  NAME       SCORE   LV  LN", 156, 72, 2, Framebuffer.Hex(0x5B6288));

        if (entries.Count == 0)
        {
            fb.TextCentered("NO SCORES YET", 150, 2, Dim);
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ScoreEntry e = entries[i];
                bool isNew = i == g.NewRank;
                uint c = isNew ? Framebuffer.Hex(0xFFD93D) : i == 0 ? Ink : Dim;
                string line = $"{i + 1,2}  {e.Name}   {e.Score,7}   {e.Level,2}  {e.Lines,2}";
                fb.Text(line, 156, 98 + i * 18, 2, c);
            }
        }

        if ((int)(g.Time * 2) % 2 == 0) fb.TextCentered("ENTER TO RETURN", 328, 2, Dim);
    }

    void NameEntryScreen(Framebuffer fb, TetrisGame g)
    {
        fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, 190);
        fb.TextCentered("NEW HIGH SCORE", 66, 5, Framebuffer.Hex(0xFFD93D));
        fb.TextCentered("SCORE " + g.Score, 118, 3, Ink);
        fb.TextCentered("ENTER YOUR INITIALS", 164, 2, Dim);

        for (int i = 0; i < 3; i++)
        {
            bool sel = i == g.InitialIndex;
            uint c = sel ? Framebuffer.Hex(0xFFD93D) : Ink;
            int x = 250 + i * 60;
            fb.Text(g.Initials[i].ToString(), x, 196, 7, c);
            if (sel) fb.Rect(x, 238, 21, 3, c);
        }

        fb.TextCentered("UP DOWN  LETTER   LEFT RIGHT  MOVE", 274, 2, Dim);
        fb.TextCentered("ENTER CONFIRM   ESC SKIP", 292, 2, Dim);
    }

    // ------------------------------------------------------------ startup sequence
    public void RenderIntro(Framebuffer fb, Intro intro)
    {
        IntroBackground(fb, intro.T);
        if (intro.Screen == IntroScreen.Audax) AudaxScreen(fb, intro.T);
        else BrutalScreen(fb, intro.T);

        int a = (int)((1f - intro.Fade) * 255f);
        if (a > 0) fb.RectA(0, 0, Framebuffer.W, Framebuffer.H, Black, Math.Clamp(a, 0, 255));

        // No scanlines over the BRUTAL ident: licence 2.2 requires the logo to be shown unaltered
        // apart from scaling.
        if (intro.Screen == IntroScreen.Audax) fb.Scanlines();
    }

    void IntroBackground(Framebuffer fb, float t)
    {
        uint top = Framebuffer.Hex(0x05060C), bot = Framebuffer.Hex(0x120A26);
        for (int y = 0; y < Framebuffer.H; y++)
            fb.FillRow(y, Framebuffer.Lerp(top, bot, y / (float)Framebuffer.H));

        fb.Glow((int)(320 + Math.Cos(t * 0.7f) * 180), (int)(180 + Math.Sin(t * 0.9f) * 70), 200, Framebuffer.Hex(0x22E5FF), 26);
        fb.Glow((int)(320 + Math.Cos(t * 0.5f + 2f) * 210), (int)(180 + Math.Sin(t * 0.6f + 1.5f) * 80), 180, Framebuffer.Hex(0xC65BFF), 24);

        int off = (int)(t * 10) % 40;
        for (int x = off; x < Framebuffer.W; x += 40) fb.RectA(x, 0, 1, Framebuffer.H, White, 5);
        for (int y = off; y < Framebuffer.H; y += 40) fb.RectA(0, y, Framebuffer.W, 1, White, 5);
    }

    /// <summary>Audax Softworks ident: the generated logo asset, shown above "PRESENTS".</summary>
    void AudaxScreen(Framebuffer fb, float t)
    {
        int w = 560;
        int h = Math.Max(1, (int)MathF.Round(w * (float)AudaxLogo.H / AudaxLogo.W));
        int x = (Framebuffer.W - w) / 2;
        int y = 64;

        fb.Glow(Framebuffer.W / 2, y + h / 2, 200, Framebuffer.Hex(0x22E5FF), 14);
        fb.Blit(AudaxLogo, x, y, w, h);

        fb.TextCentered("P R E S E N T S", y + h + 24, 2, Framebuffer.Hex(0x7C84A8));
        if ((int)(t * 2) % 2 == 0) fb.TextCentered("ENTER", 330, 2, Framebuffer.Hex(0x3A4070));
    }

    /// <summary>Official BRUTAL logo, unaltered apart from uniform scaling (licence section 2.2).</summary>
    void BrutalScreen(Framebuffer fb, float t)
    {
        int w = 560;
        int h = Math.Max(1, (int)MathF.Round(w * (float)BrutalLogo.H / BrutalLogo.W));
        int x = (Framebuffer.W - w) / 2;
        int y = 92;

        fb.Glow(Framebuffer.W / 2, y + h / 2, 210, White, 12);
        fb.Blit(BrutalLogo, x, y, w, h);

        fb.TextCentered("GAME DEVELOPMENT FRAMEWORK", 254, 2, Dim);
        fb.TextCentered("BY AHWOO LIMITED", 276, 2, Framebuffer.Hex(0x5B6288));
        if ((int)(t * 2) % 2 == 0) fb.TextCentered("ENTER", 330, 2, Framebuffer.Hex(0x3A4070));
    }
}
