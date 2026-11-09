namespace NeonStack.Tests;

/// <summary>Assets, persistence, framebuffer safety and a render smoke test for every screen.</summary>
public static class AppTests
{
    static bool[] None() => new bool[(int)Key.Count];

    static ulong Hash(Framebuffer fb)
    {
        ulong h = 1469598103934665603UL;
        foreach (uint p in fb.Pixels) h = (h ^ p) * 1099511628211UL;
        return h;
    }

    static int BrightPixels(Framebuffer fb, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                uint p = fb.Pixels[y * fb.PixelW + x];
                if ((p & 255) + ((p >> 8) & 255) + ((p >> 16) & 255) > 180) count++;
            }
        return count;
    }

    [Test]
    public static void EmbeddedImagesDecode()
    {
        var brutal = ImageData.LoadEmbedded("NeonStack.Assets.logo_brutal_wht.png");
        Harness.True(brutal.W > 100 && brutal.H > 20, "BRUTAL licence logo decoded");

        var audax = ImageData.LoadEmbedded("NeonStack.Assets.logo_audax.png");
        Harness.True(audax.W > 400 && audax.H > 100, "Audax splash decoded");

        var title = ImageData.LoadEmbedded("NeonStack.Assets.logo_neonstack.png");
        Harness.True(title.W > 800 && title.H > 100, "title lockup decoded");

        var icon = ImageData.LoadEmbedded("NeonStack.Assets.icon_neonstack.png");
        Harness.Equal(256, icon.W, "icon width");
        Harness.Equal(256, icon.H, "icon height");
        Harness.Equal(0u, icon.Pixels[0] >> 24, "the icon's corner is transparent");
        Harness.True(icon.Pixels.Any(p => (p >> 24) == 255 && (p & 255) > 100), "the icon has opaque neon pixels");
    }

    [Test]
    public static void LevelThemesAreDistinct()
    {
        var accents = TetrisGame.Levels.Select(l => l.Accent).ToHashSet();
        Harness.Equal(TetrisGame.Levels.Length, accents.Count, "every level has its own accent");

        var scene = new Scene();
        var a = new Framebuffer(1);
        var b = new Framebuffer(1);

        var first = new TetrisGame { Phase = Phase.Playing, LevelIndex = 0 };
        var last = new TetrisGame { Phase = Phase.Playing, LevelIndex = TetrisGame.Levels.Length - 1 };
        scene.Render(a, first);
        scene.Render(b, last);

        Harness.True(Hash(a) != Hash(b), "changing level changes the rendered theme");
    }

    [Test]
    public static void FramebufferClipsOutOfBounds()
    {
        var fb = new Framebuffer(1);
        fb.Rect(-50, -50, 2000, 2000, 0xFFFFFFFF);
        Harness.Equal(0xFFFFFFFFu, fb.Pixels[0], "an oversized rect fills what it can");

        fb.RectA(600, 350, 500, 500, 0xFF00FF00, 128);
        fb.RectAdd(-20, 300, 60, 400, 0xFF0000FF, 200);
        fb.Glow(0, 0, 400, 0xFFFFFFFF, 255);
        fb.Scanlines();
        fb.Text("OUT OF BOUNDS 12345", 620, 352, 4, 0xFFFFFFFF);
        Harness.Equal(TetrisGame.Rows > 0, true, "drawing never throws");
    }

    [Test]
    public static void TextWidthMatchesAdvance()
    {
        Harness.Equal(0, Framebuffer.TextWidth("", 3), "empty text");
        Harness.Equal(4 * 3 - 3, Framebuffer.TextWidth("A", 3), "single glyph");
        Harness.Equal(3 * (4 * 2) - 2, Framebuffer.TextWidth("ABC", 2), "three glyphs");
    }

    [Test]
    public static void TitleScreenDrawsTheLogo()
    {
        var fb = new Framebuffer(1);
        var g = new TetrisGame { Phase = Phase.Title };
        new Scene().Render(fb, g);

        int bright = BrightPixels(fb, 40, 28, 600, 118);
        Harness.True(bright > 4000, $"the generated logo should dominate the title band (got {bright} bright pixels)");
    }

    [Test]
    public static void EveryPhaseRenders()
    {
        var scene = new Scene();
        var fb = new Framebuffer(1);
        foreach (Phase phase in Enum.GetValues<Phase>())
        {
            fb.Clear(0);
            var g = new TetrisGame
            {
                Phase = phase,
                LevelIndex = 2,
                Score = 1234,
                Lines = 12,
                LevelLines = 5,
                Msg = "LEVEL 3  CIRCUIT RAIN",
                MsgT = 1f,
                Flash = 0.4f,
            };
            g.Cur = new Piece { Type = 5, Rot = 1, X = 4, Y = 6 };
            g.Board[0, 19] = 3;
            g.Board[1, 19] = 3;
            if (phase == Phase.Clearing) { g.ClearRows.Add(19); g.ClearT = 0.2f; }
            if (phase == Phase.LevelClear) g.LevelClearT = 2f;

            scene.Render(fb, g);
            Harness.True(fb.Pixels.Any(p => p != 0), "phase " + phase + " renders something");
        }
    }

    [Test]
    public static void IntroRendersBothSplashes()
    {
        var scene = new Scene();
        var fb = new Framebuffer(1);
        var intro = new Intro();

        for (int i = 0; i < 12; i++) intro.Update(0.05f, None());   // fade in
        scene.RenderIntro(fb, intro);
        Harness.True(BrightPixels(fb, 0, 0, Framebuffer.W, Framebuffer.H) > 200, "Audax ident draws");

        intro.Update(2.9f, None());
        Harness.Equal(IntroScreen.Brutal, intro.Screen, "the second splash is the BRUTAL logo");
        for (int i = 0; i < 20; i++) intro.Update(0.05f, None());

        fb.Clear(0);
        scene.RenderIntro(fb, intro);
        Harness.True(BrightPixels(fb, 0, 0, Framebuffer.W, Framebuffer.H) > 200, "BRUTAL ident draws");

        var down = None();
        down[(int)Key.Escape] = true;
        intro.Update(0.05f, down);
        Harness.False(intro.Active, "Escape skips the startup sequence");
    }

    [Test]
    public static void SaveDataUsesTheOverride()
    {
        Harness.True(SaveData.Dir.Contains("neonstack-tests", StringComparison.Ordinal),
            "NEONSTACK_SAVEDIR redirects the save folder: " + SaveData.Dir);
        Harness.True(Directory.Exists(SaveData.Dir), "the save folder is created");
    }

    [Test]
    public static void SettingsPersistAndClamp()
    {
        var s = Settings.Current;
        s.MusicVolume = 3;
        s.SfxVolume = 2;
        s.Ghost = false;
        s.Scanlines = false;
        s.Shake = false;
        s.Fullscreen = false;
        s.Save();

        Settings.Reload();
        Harness.Equal(3, Settings.Current.MusicVolume, "music volume round-trips");
        Harness.Equal(2, Settings.Current.SfxVolume, "sfx volume round-trips");
        Harness.False(Settings.Current.Ghost, "ghost piece round-trips");
        Harness.False(Settings.Current.Fullscreen, "fullscreen round-trips");

        File.WriteAllLines(SaveData.File("settings.txt"), new[]
        {
            "musicvolume=99", "sfxvolume=-4", "ghost=maybe", "scanlines=0",
        });
        Settings.Reload();
        Harness.Equal(10, Settings.Current.MusicVolume, "out-of-range volume is clamped");
        Harness.Equal(0, Settings.Current.SfxVolume, "negative volume is clamped");
        Harness.True(Settings.Current.Ghost, "an unparsable flag keeps the default");

        File.Delete(SaveData.File("settings.txt"));
        Settings.Reload();
    }

    [Test]
    public static void LeaderboardRanksAndTrims()
    {
        int rank = Leaderboard.Add("abc", 5000, 3, 20);
        Harness.Equal(0, rank, "the first entry tops the table");
        Harness.Equal("ABC", Leaderboard.Entries[0].Name, "initials are upper-cased");

        Harness.Equal(0, Leaderboard.Add("zzz", 6000, 5, 30), "a higher score takes rank 1");
        Harness.Equal(1, Leaderboard.Add("mmm", 5500, 4, 25), "a middle score lands second");

        for (int i = 0; i < 12; i++) Leaderboard.Add("p" + (char)('a' + i), 1000 + i * 100, 1 + i, i);
        Harness.Equal(Leaderboard.Max, Leaderboard.Entries.Count, "the table keeps ten entries");
        for (int i = 1; i < Leaderboard.Entries.Count; i++)
            Harness.True(Leaderboard.Entries[i - 1].Score >= Leaderboard.Entries[i].Score, "entries stay sorted");

        Harness.False(Leaderboard.Qualifies(1), "a low score does not qualify");
        Harness.True(Leaderboard.Qualifies(999999), "a high score qualifies");
        Harness.False(Leaderboard.Qualifies(0), "zero never qualifies");

        string saved = File.ReadAllText(SaveData.File("scores.txt"));
        Harness.True(saved.Contains('|'), "scores are persisted as pipe records");
    }
}
