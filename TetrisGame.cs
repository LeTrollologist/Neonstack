namespace NeonStack;

public enum Key { Left, Right, Down, Up, Z, X, C, Space, Enter, Escape, P, M, S, L, Count }
public enum Phase { Title, Playing, Paused, Clearing, LevelClear, Over, Won, Credits, Settings, Leaderboard, NameEntry }

public sealed class Piece { public int Type, Rot, X, Y; }

/// <summary>One campaign level: a name, a line goal, a fixed gravity interval and its neon accent.</summary>
public readonly record struct LevelDef(string Name, int TargetLines, float Gravity, int Accent);

public struct Particle { public float X, Y, Vx, Vy, Life, Max; public uint Color; public int Size; }

/// <summary>Floating score/combo text drawn over the board.</summary>
public struct Popup { public string Text; public float X, Y, T, Life; public uint Color; }

/// <summary>Engine-agnostic game logic: SRS rotation + kicks, 7-bag, hold, ghost, lock delay, DAS/ARR, guideline scoring.</summary>
public sealed class TetrisGame
{
    public const int Cols = 10, Rows = 20;
    public const int OriginX = 240, OriginY = 20, Cell = 16;   // board placement in framebuffer pixels
    public const float ClearDur = 0.32f;
    const float Das = 0.16f, Arr = 0.04f, LockDelay = 0.5f;
    const float LevelClearDur = 3.2f;

    /// <summary>The campaign. Each level is a line goal, a fixed drop interval and an accent colour.</summary>
    public static readonly LevelDef[] Levels =
    {
        new("NEON DAWN",   10, 0.85f, 0x22E5FF),
        new("STATIC BLOOM", 12, 0.72f, 0x4D7CFF),
        new("CIRCUIT RAIN", 14, 0.62f, 0x4DFF9A),
        new("VOLT RUSH",    16, 0.53f, 0xFFD93D),
        new("GRIDLOCK",     18, 0.45f, 0xFF9A3C),
        new("CHROME FALL",  20, 0.38f, 0x9AA4D0),
        new("AFTERGLOW",    22, 0.32f, 0xFF4D6D),
        new("OVERCLOCK",    24, 0.27f, 0xFF6FD8),
        new("BLACKOUT",     26, 0.22f, 0x8B5BFF),
        new("FINAL STACK",  30, 0.17f, 0xF2F5FF),
    };

    // I J L O S T Z
    static readonly string[][] Base =
    {
        new[] { "....", "XXXX", "....", "...." },
        new[] { "X..", "XXX", "..." },
        new[] { "..X", "XXX", "..." },
        new[] { "XX", "XX" },
        new[] { ".XX", "XX.", "..." },
        new[] { ".X.", "XXX", "..." },
        new[] { "XX.", ".XX", "..." },
    };

    public static readonly int[] Palette = { 0x22E5FF, 0x4D7CFF, 0xFF9A3C, 0xFFD93D, 0x4DFF9A, 0xC65BFF, 0xFF4D6D };
    public static readonly (int x, int y)[][][] Shapes = BuildShapes();

    static (int x, int y)[][][] BuildShapes()
    {
        var all = new (int x, int y)[7][][];
        for (int t = 0; t < 7; t++)
        {
            int n = Base[t].Length;
            var cells = new List<(int x, int y)>();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (Base[t][y][x] == 'X') cells.Add((x, y));
            all[t] = new (int x, int y)[4][];
            for (int r = 0; r < 4; r++)
            {
                all[t][r] = cells.ToArray();
                for (int i = 0; i < cells.Count; i++) cells[i] = (n - 1 - cells[i].y, cells[i].x); // clockwise, y-down
            }
        }
        return all;
    }

    // SRS kick tables, written in SRS convention (y up). Indexed [dir: 0=CW,1=CCW][fromRotation][test].
    static (int x, int y)[] K(params int[] v)
    {
        var r = new (int x, int y)[v.Length / 2];
        for (int i = 0; i < r.Length; i++) r[i] = (v[i * 2], v[i * 2 + 1]);
        return r;
    }

    static readonly (int x, int y)[][][] KickJLSTZ =
    {
        new[] { K(0,0,-1,0,-1,1,0,-2,-1,-2), K(0,0,1,0,1,-1,0,2,1,2), K(0,0,1,0,1,1,0,-2,1,-2), K(0,0,-1,0,-1,-1,0,2,-1,2) },
        new[] { K(0,0,1,0,1,1,0,-2,1,-2), K(0,0,1,0,1,-1,0,2,1,2), K(0,0,-1,0,-1,1,0,-2,-1,-2), K(0,0,-1,0,-1,-1,0,2,-1,2) },
    };

    static readonly (int x, int y)[][][] KickI =
    {
        new[] { K(0,0,-2,0,1,0,-2,-1,1,2), K(0,0,-1,0,2,0,-1,2,2,-1), K(0,0,2,0,-1,0,2,1,-1,-2), K(0,0,1,0,-2,0,1,-2,-2,1) },
        new[] { K(0,0,-1,0,2,0,-1,2,2,-1), K(0,0,2,0,-1,0,2,1,-1,-2), K(0,0,1,0,-2,0,1,-2,-2,1), K(0,0,-2,0,1,0,-2,-1,1,2) },
    };

    // ---- public state (read by the renderer) ----
    public readonly int[,] Board = new int[Cols, Rows];   // 0 = empty, else pieceType + 1
    public Piece Cur = new();
    public int Hold = -1;
    public bool HoldUsed;
    public int Score, Lines, LevelLines, Best, Combo;

    /// <summary>0-based campaign level.</summary>
    public int LevelIndex;
    public int Level => LevelIndex + 1;
    public LevelDef LevelInfo => Levels[LevelIndex];
    public int TargetLines => LevelInfo.TargetLines;
    public string LevelName => LevelInfo.Name;

    /// <summary>Highest level index the player has unlocked; the title screen can start any unlocked level.</summary>
    public int Unlocked { get; private set; }
    public int SelectedLevel { get; private set; }

    public Phase Phase = Phase.Title;
    public bool Quit;
    public float Time, Shake, ClearT, MsgT, LevelClearT, Flash;
    public uint FlashColor = 0xFFFFFFFF;
    int scoreAtLevelStart;
    public string Msg = "";
    public readonly List<int> ClearRows = new();
    public readonly List<Particle> Particles = new();
    public readonly List<Popup> Popups = new();

    /// <summary>Result of the corner test at the last lock (read by the renderer and the tests).</summary>
    public bool SpinDetected { get; private set; }
    public bool SpinMini { get; private set; }

    /// <summary>True while the last clear was a "difficult" clear, so the next one pays the 1.5x B2B bonus.</summary>
    public bool BackToBack => backToBack;

    // Settings / leaderboard UI state (read by Scene).
    public int SettingsIndex { get; private set; }
    public const int SettingsRows = 7;
    public string Initials { get; private set; } = "AAA";
    public int InitialIndex { get; private set; }
    public int NewRank { get; private set; } = -1;
    float nameT;
    bool fullscreenRequest;

    /// <summary>Program consumes this to flip the actual window.</summary>
    public bool ConsumeFullscreenRequest()
    {
        bool v = fullscreenRequest;
        fullscreenRequest = false;
        return v;
    }

    readonly Queue<int> bag = new();
    readonly Random rng = new();
    readonly bool[] prev = new bool[(int)Key.Count];
    readonly bool[] press = new bool[(int)Key.Count];
    float grav, lockT, dasT, arrT, overT;
    int lockResets, dasDir, lowY;
    bool lastRotate;        // the last successful movement was a rotation (T-spin requirement)
    bool lastKickSpecial;   // that rotation used the fifth kick test (a full, not mini, T-spin)
    bool backToBack;
    float confirmBlock;     // ignores Enter/Space for a moment after the intro hands over

    // Save data goes to a writable per-user folder, not next to the exe (which may be read-only).
    static string BestPath => SaveData.File("best.txt");
    static string ProgressPath => SaveData.File("progress.txt");

    public TetrisGame()
    {
        try { if (File.Exists(BestPath) && int.TryParse(File.ReadAllText(BestPath).Trim(), out int b)) Best = b; }
        catch { /* no persistence is fine */ }
        try
        {
            if (File.Exists(ProgressPath) && int.TryParse(File.ReadAllText(ProgressPath).Trim(), out int u))
                Unlocked = Math.Clamp(u, 0, Levels.Length - 1);
        }
        catch { /* no persistence is fine */ }

        SelectedLevel = Unlocked;
        FillBag();
        for (int i = 0; i < 40; i++) EmitTitleDust();
    }

    public int Preview(int i) { FillBag(); return bag.ElementAt(i); }

    public int GhostY()
    {
        int d = 0;
        while (Fits(Cur, 0, d + 1)) d++;
        return Cur.Y + d;
    }

    public float GravityInterval() => LevelInfo.Gravity;

    /// <summary>
    /// Ignores Enter/Space for a short time. The intro and the title screen share those keys, so
    /// without this the Enter that advances the BRUTAL splash would also start a campaign.
    /// </summary>
    public void SuppressConfirm(float seconds) => confirmBlock = Math.Max(confirmBlock, seconds);

    // ---------------------------------------------------------------- update
    bool Pr(Key k) => press[(int)k];

    public void Update(float dt, bool[] down)
    {
        dt = Math.Min(dt, 0.05f);
        Time += dt;
        for (int i = 0; i < prev.Length; i++) { press[i] = down[i] && !prev[i]; prev[i] = down[i]; }
        if (confirmBlock > 0)
        {
            confirmBlock -= dt;
            press[(int)Key.Enter] = false;
            press[(int)Key.Space] = false;
        }
        Shake = Math.Max(0f, Shake - dt * 30f);
        if (MsgT > 0) MsgT -= dt;
        if (Flash > 0) Flash = Math.Max(0f, Flash - dt * 3.2f);
        UpdateParticles(dt);
        UpdatePopups(dt);

        switch (Phase)
        {
            case Phase.Title:
                if (Pr(Key.Left)) { SelectedLevel = Math.Max(0, SelectedLevel - 1); }
                if (Pr(Key.Right)) { SelectedLevel = Math.Min(Unlocked, SelectedLevel + 1); }
                if (Pr(Key.Enter) || Pr(Key.Space)) { Audio.Play(Sfx.Start); StartCampaign(); }
                if (Pr(Key.C)) { Audio.Play(Sfx.Menu); Phase = Phase.Credits; }
                if (Pr(Key.S)) { Audio.Play(Sfx.Menu); SettingsIndex = 0; Phase = Phase.Settings; }
                if (Pr(Key.L)) { Audio.Play(Sfx.Menu); NewRank = -1; Phase = Phase.Leaderboard; }
                if (Pr(Key.Escape)) Quit = true;
                break;
            case Phase.Credits:
                if (Pr(Key.Enter) || Pr(Key.Escape) || Pr(Key.C)) { Audio.Play(Sfx.Menu); Phase = Phase.Title; }
                break;
            case Phase.Settings:
                UpdateSettings();
                break;
            case Phase.Leaderboard:
                if (Pr(Key.Enter) || Pr(Key.Escape) || Pr(Key.L)) { Audio.Play(Sfx.Menu); NewRank = -1; Phase = Phase.Title; }
                break;
            case Phase.NameEntry:
                UpdateNameEntry(dt);
                break;
            case Phase.Paused:
                if (Pr(Key.P) || Pr(Key.Escape) || Pr(Key.Enter)) { Audio.Play(Sfx.Pause); Phase = Phase.Playing; }
                break;
            case Phase.Over:
                overT += dt;
                if (overT > 0.6f && (Pr(Key.Enter) || Pr(Key.Space)))
                {
                    Score = scoreAtLevelStart;
                    StartLevel(LevelIndex);
                }
                if (Pr(Key.Escape)) Phase = Phase.Title;
                break;
            case Phase.LevelClear:
                LevelClearT -= dt;
                if (LevelClearT <= 0 || (LevelClearT < LevelClearDur - 0.7f && (Pr(Key.Enter) || Pr(Key.Space)))) AdvanceLevel();
                break;
            case Phase.Won:
                overT += dt;
                if (overT > 0.8f && (Pr(Key.Enter) || Pr(Key.Space) || Pr(Key.Escape))) Phase = Phase.Title;
                break;
            case Phase.Clearing:
                ClearT -= dt;
                if (ClearT <= 0) FinishClear();
                break;
            case Phase.Playing:
                Play(dt, down);
                break;
        }
    }

    void UpdateSettings()
    {
        Settings s = Settings.Current;
        if (Pr(Key.Up)) { SettingsIndex = (SettingsIndex + SettingsRows - 1) % SettingsRows; Audio.Play(Sfx.Move); }
        if (Pr(Key.Down)) { SettingsIndex = (SettingsIndex + 1) % SettingsRows; Audio.Play(Sfx.Move); }

        int dir = Pr(Key.Left) ? -1 : Pr(Key.Right) ? 1 : 0;
        if (dir != 0)
        {
            switch (SettingsIndex)
            {
                case 0: s.MusicVolume = Math.Clamp(s.MusicVolume + dir, 0, 10); Audio.Play(Sfx.Move); break;
                case 1: s.SfxVolume = Math.Clamp(s.SfxVolume + dir, 0, 10); Audio.Play(Sfx.Move); break;
                case 2: s.Ghost = !s.Ghost; Audio.Play(Sfx.Menu); break;
                case 3: s.Scanlines = !s.Scanlines; Audio.Play(Sfx.Menu); break;
                case 4: s.Shake = !s.Shake; Audio.Play(Sfx.Menu); break;
                case 5: s.Fullscreen = !s.Fullscreen; fullscreenRequest = true; Audio.Play(Sfx.Menu); break;
                case 6: break;   // BACK only responds to Enter/Esc, never to Left/Right
            }
            s.Save();
        }

        if (Pr(Key.Enter) || Pr(Key.Escape) || Pr(Key.S)) { Audio.Play(Sfx.Menu); Phase = Phase.Title; }
    }

    void UpdateNameEntry(float dt)
    {
        nameT += dt;
        if (nameT < 0.45f) return;

        if (Pr(Key.Left)) { InitialIndex = (InitialIndex + 2) % 3; Audio.Play(Sfx.Move); }
        if (Pr(Key.Right)) { InitialIndex = (InitialIndex + 1) % 3; Audio.Play(Sfx.Move); }
        if (Pr(Key.Up)) { CycleLetter(1); Audio.Play(Sfx.Rotate); }
        if (Pr(Key.Down)) { CycleLetter(-1); Audio.Play(Sfx.Rotate); }

        if (Pr(Key.Enter) || Pr(Key.Space))
        {
            NewRank = Leaderboard.Add(Initials, Score, Level, Lines);
            Audio.Play(Sfx.Start);
            Phase = Phase.Leaderboard;
        }
        else if (Pr(Key.Escape))
        {
            Phase = Phase.Over;
        }
    }

    void CycleLetter(int delta)
    {
        char[] c = Initials.ToCharArray();
        int v = c[InitialIndex] - 'A';
        v = (v + delta + 26) % 26;
        c[InitialIndex] = (char)('A' + v);
        Initials = new string(c);
    }

    void UpdatePopups(float dt)
    {
        for (int i = Popups.Count - 1; i >= 0; i--)
        {
            var p = Popups[i];
            p.T += dt;
            p.Y -= 20f * dt;
            if (p.T >= p.Life) Popups.RemoveAt(i);
            else Popups[i] = p;
        }
    }

    void StartCampaign()
    {
        Score = 0;
        Lines = 0;
        Combo = 0;
        StartLevel(SelectedLevel);
    }

    /// <summary>Resets the board and begins the level, keeping the score earned in earlier levels.</summary>
    void StartLevel(int index)
    {
        Array.Clear(Board);
        LevelIndex = Math.Clamp(index, 0, Levels.Length - 1);
        LevelLines = 0;
        Combo = 0;
        backToBack = false;
        SpinDetected = false;
        SpinMini = false;
        Hold = -1;
        HoldUsed = false;
        bag.Clear();
        FillBag();
        Particles.Clear();
        ClearRows.Clear();
        dasDir = 0; dasT = arrT = 0;
        scoreAtLevelStart = Score;
        Msg = "LEVEL " + Level + "  " + LevelName;
        MsgT = 1.6f;
        Phase = Phase.Playing;
        Spawn(NextType());
    }

    void AdvanceLevel()
    {
        int next = LevelIndex + 1;
        if (next >= Levels.Length)
        {
            Phase = Phase.Won;
            overT = 0;
            SaveBest();
            Audio.Play(Sfx.Win);
            return;
        }

        Unlocked = Math.Max(Unlocked, next);
        SelectedLevel = Math.Max(SelectedLevel, next);
        SaveProgress();
        StartLevel(next);
    }

    void Play(float dt, bool[] down)
    {
        if (Pr(Key.P) || Pr(Key.Escape)) { Audio.Play(Sfx.Pause); Phase = Phase.Paused; return; }

        int dir = down[(int)Key.Left] == down[(int)Key.Right] ? 0 : (down[(int)Key.Left] ? -1 : 1);
        if (dir != dasDir)
        {
            dasDir = dir; dasT = 0; arrT = 0;
            if (dir != 0 && Move(dir)) Audio.Play(Sfx.Move);
        }
        else if (dir != 0)
        {
            dasT += dt;
            if (dasT >= Das)
            {
                arrT += dt;
                while (arrT >= Arr) { arrT -= Arr; if (!Move(dir)) { arrT = 0; break; } }
            }
        }

        if (Pr(Key.Up) || Pr(Key.X)) Rotate(0);
        if (Pr(Key.Z)) Rotate(1);
        if (Pr(Key.C)) { DoHold(); return; }
        if (Pr(Key.Space)) { HardDrop(); return; }

        bool soft = down[(int)Key.Down];
        float interval = GravityInterval();
        if (soft) interval = Math.Min(interval, Math.Max(interval / 20f, 0.012f));

        if (Fits(Cur, 0, 1))
        {
            lockT = 0;
            grav += dt;
            while (grav >= interval)
            {
                grav -= interval;
                if (Fits(Cur, 0, 1)) { Cur.Y++; if (soft) Score++; } else break;
            }
            if (Cur.Y > lowY) { lowY = Cur.Y; lockResets = 0; }
        }
        else
        {
            grav = 0;
            lockT += dt;
            if (lockT >= LockDelay) Lock();
        }
    }

    // ---------------------------------------------------------------- piece ops
    bool FitsAt(int type, int rot, int px, int py)
    {
        foreach (var c in Shapes[type][rot])
        {
            int x = px + c.x, y = py + c.y;
            if (x < 0 || x >= Cols || y >= Rows) return false;
            if (y >= 0 && Board[x, y] != 0) return false;
        }
        return true;
    }

    bool Fits(Piece p, int dx, int dy) => FitsAt(p.Type, p.Rot, p.X + dx, p.Y + dy);

    /// <summary>A cell that blocks a T-spin corner: the walls, the floor and any settled block.</summary>
    bool Blocked(int x, int y) => x < 0 || x >= Cols || y >= Rows || (y >= 0 && Board[x, y] != 0);

    /// <summary>
    /// Classic three-corner T-spin test. The piece must be a T, its last successful movement must
    /// have been a rotation, and three of the four corners of its 3x3 box must be blocked. The spin is
    /// "mini" when the two corners in front of the nub are not both blocked, unless that rotation
    /// needed the fifth (last) kick test, which always produces a full T-spin.
    /// </summary>
    bool DetectTSpin(out bool mini)
    {
        mini = false;
        if (Cur.Type != 5 || !lastRotate) return false;

        int cx = Cur.X + 1, cy = Cur.Y + 1;
        bool tl = Blocked(cx - 1, cy - 1), tr = Blocked(cx + 1, cy - 1);
        bool bl = Blocked(cx - 1, cy + 1), br = Blocked(cx + 1, cy + 1);
        int corners = (tl ? 1 : 0) + (tr ? 1 : 0) + (bl ? 1 : 0) + (br ? 1 : 0);
        if (corners < 3) return false;

        bool front = (Cur.Rot & 3) switch
        {
            0 => tl && tr,     // nub pointing up
            1 => tr && br,     // pointing right
            2 => bl && br,     // pointing down
            _ => tl && bl,     // pointing left
        };
        mini = !front && !lastKickSpecial;
        return true;
    }

    static string ClearLabel(int n) => n switch { 1 => "SINGLE", 2 => "DOUBLE", 3 => "TRIPLE", _ => "TETRIS" };

    void AfterMove()
    {
        if (!Fits(Cur, 0, 1) && lockResets < 15) { lockT = 0; lockResets++; }
    }

    bool Move(int dx)
    {
        if (!Fits(Cur, dx, 0)) return false;
        Cur.X += dx;
        lastRotate = false;
        AfterMove();
        return true;
    }

    void Rotate(int dir) // 0 = clockwise, 1 = counter-clockwise
    {
        int nr = (Cur.Rot + (dir == 0 ? 1 : 3)) & 3;
        var kicks = (Cur.Type == 0 ? KickI : KickJLSTZ)[dir][Cur.Rot];
        for (int i = 0; i < kicks.Length; i++)
        {
            var k = kicks[i];
            if (FitsAt(Cur.Type, nr, Cur.X + k.x, Cur.Y - k.y))
            {
                Cur.Rot = nr; Cur.X += k.x; Cur.Y -= k.y;
                lastRotate = true;
                lastKickSpecial = i == 4;   // the 5th test produces the T-spin triple, never a mini
                AfterMove();
                Audio.Play(Sfx.Rotate);
                return;
            }
        }
    }

    void DoHold()
    {
        if (HoldUsed) return;
        HoldUsed = true;
        int t = Cur.Type;
        if (Hold < 0) { Hold = t; Spawn(NextType()); }
        else { int h = Hold; Hold = t; Spawn(h); }
        Audio.Play(Sfx.Hold);
    }

    void HardDrop()
    {
        int d = 0;
        while (Fits(Cur, 0, 1)) { Cur.Y++; d++; }
        Score += 2 * d;
        uint col = Framebuffer.Hex(Palette[Cur.Type]);
        foreach (var c in Shapes[Cur.Type][Cur.Rot])
        {
            int px = OriginX + (Cur.X + c.x) * Cell + Cell / 2, py = OriginY + (Cur.Y + c.y) * Cell + Cell / 2;
            for (int i = 0; i < Math.Min(d, 12); i++) Emit(px + Rnd(-6, 6), py - i * Cell * 0.8f, col, 1, 25f, 2, 0.35f);
        }
        Shake = Math.Max(Shake, 4f);
        Audio.Play(Sfx.HardDrop);
        Lock(silent: true);
    }

    void Lock(bool silent = false)
    {
        if (!silent) Audio.Play(Sfx.Lock);
        uint col = Framebuffer.Hex(Palette[Cur.Type]);
        bool lockedOut = false;
        foreach (var c in Shapes[Cur.Type][Cur.Rot])
        {
            int x = Cur.X + c.x, y = Cur.Y + c.y;
            if (y < 0) { lockedOut = true; continue; }
            Board[x, y] = Cur.Type + 1;
            Emit(OriginX + x * Cell + Cell / 2, OriginY + y * Cell + Cell / 2, col, 3, 60f, 2, 0.4f);
        }
        Shake = Math.Max(Shake, 1.5f);
        if (lockedOut) { GameOver(); return; }

        HoldUsed = false;
        ClearRows.Clear();
        for (int y = 0; y < Rows; y++)
        {
            bool full = true;
            for (int x = 0; x < Cols && full; x++) if (Board[x, y] == 0) full = false;
            if (full) ClearRows.Add(y);
        }

        int n = ClearRows.Count;

        bool spin = DetectTSpin(out bool mini);
        SpinDetected = spin;
        SpinMini = mini;

        // A T-spin with no lines still pays (400 x level) and keeps the B2B chain alive.
        if (n == 0)
        {
            Combo = 0;
            if (spin)
            {
                Score += 400 * Level;
                Msg = "";
                MsgT = 0f;
                Shake = Math.Max(Shake, 2f);
                Flash = 0.35f;
                FlashColor = Framebuffer.Hex(0xC65BFF);
                Audio.Play(Sfx.Clear1);
                Popups.Add(new Popup
                {
                    Text = mini ? "T-SPIN MINI" : "T-SPIN", X = 320, Y = 118,
                    T = 0f, Life = 0.9f, Color = Framebuffer.Hex(0xC65BFF),
                });
            }
            Spawn(NextType());
            return;
        }

        Combo++;

        int baseScore;
        string label;
        if (spin)
        {
            // Guideline: mini singles/doubles 200/400, full singles/doubles/triples 800/1200/1600.
            baseScore = mini ? n switch { 1 => 200, 2 => 400, _ => 200 * n }
                             : n switch { 1 => 800, 2 => 1200, _ => 1600 };
            label = (mini ? "T-SPIN MINI " : "T-SPIN ") + ClearLabel(n);
        }
        else
        {
            baseScore = n switch { 1 => 100, 2 => 300, 3 => 500, _ => 800 };
            label = ClearLabel(n);
        }

        // Back-to-back: consecutive difficult clears (tetrises and T-spin clears) pay 1.5x.
        bool difficult = n >= 4 || spin;
        bool chained = difficult && backToBack;
        if (chained) baseScore = baseScore * 3 / 2;
        backToBack = difficult;

        Score += baseScore * Level;
        if (Combo > 1) Score += 50 * (Combo - 1) * Level;
        Msg = "";
        MsgT = 0f;
        Shake = Math.Max(Shake, n * 2.5f);
        Audio.Play(n switch { 1 => Sfx.Clear1, 2 => Sfx.Clear2, 3 => Sfx.Clear3, _ => Sfx.Clear4 });
        Flash = 0.6f;
        FlashColor = spin ? Framebuffer.Hex(0xC65BFF)
                    : n >= 4 ? Framebuffer.Hex(0xFFD93D)
                    : Framebuffer.Hex(0x22E5FF);
        Popups.Add(new Popup { Text = label, X = 320, Y = 118, T = 0f, Life = 1.0f, Color = FlashColor });
        if (chained)
            Popups.Add(new Popup { Text = "BACK TO BACK", X = 320, Y = 92, T = 0f, Life = 1.2f, Color = Framebuffer.Hex(0xFF9A3C) });
        if (Combo > 1)
            Popups.Add(new Popup { Text = "COMBO " + Combo, X = 320, Y = 148, T = 0f, Life = 1.1f, Color = Framebuffer.Hex(0xC65BFF) });
        foreach (int y in ClearRows)
            for (int x = 0; x < Cols; x++)
                Emit(OriginX + x * Cell + Cell / 2, OriginY + y * Cell + Cell / 2, Framebuffer.Hex(Palette[Board[x, y] - 1]), n >= 4 ? 4 : 2, 120f, 3, 0.8f);
        Phase = Phase.Clearing;
        ClearT = ClearDur;
    }

    void FinishClear()
    {
        int write = Rows - 1;
        for (int read = Rows - 1; read >= 0; read--)
        {
            if (ClearRows.Contains(read)) continue;
            if (write != read) for (int x = 0; x < Cols; x++) Board[x, write] = Board[x, read];
            write--;
        }
        for (int y = write; y >= 0; y--) for (int x = 0; x < Cols; x++) Board[x, y] = 0;

        Lines += ClearRows.Count;
        LevelLines += ClearRows.Count;
        ClearRows.Clear();

        if (LevelLines >= TargetLines)
        {
            Score += 500 * Level;
            SaveBest();

            // Record the unlock immediately so quitting on the Level Clear screen still counts.
            Unlocked = Math.Max(Unlocked, Math.Min(LevelIndex + 1, Levels.Length - 1));
            SaveProgress();

            Audio.Play(Sfx.LevelUp);
            Flash = 0.9f;
            FlashColor = Framebuffer.Hex(0x4DFF9A);
            LevelClearT = LevelClearDur;
            Phase = Phase.LevelClear;
            return;
        }

        Phase = Phase.Playing;
        Spawn(NextType());
    }

    void Spawn(int type)
    {
        Cur = new Piece { Type = type, Rot = 0, X = type == 3 ? 4 : 3, Y = type == 0 ? -1 : 0 };
        grav = 0; lockT = 0; lockResets = 0; lowY = Cur.Y;
        lastRotate = false;
        lastKickSpecial = false;
        if (!Fits(Cur, 0, 0)) GameOver();
    }

    void GameOver()
    {
        Phase = Phase.Over;
        overT = 0;
        Shake = 8f;
        SaveBest();
        Audio.Play(Sfx.GameOver);

        if (Leaderboard.Qualifies(Score))
        {
            Phase = Phase.NameEntry;
            nameT = 0f;
            Initials = "AAA";
            InitialIndex = 0;
        }

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (Board[x, y] != 0)
                    Emit(OriginX + x * Cell + Cell / 2, OriginY + y * Cell + Cell / 2, Framebuffer.Hex(Palette[Board[x, y] - 1]), 1, 90f, 2, 1.2f);
    }

    void SaveBest()
    {
        if (Score <= Best) return;
        Best = Score;
        try { File.WriteAllText(BestPath, Best.ToString()); } catch { /* read-only install: fine */ }
    }

    void SaveProgress()
    {
        try { File.WriteAllText(ProgressPath, Unlocked.ToString()); } catch { /* read-only install: fine */ }
    }

    // ---------------------------------------------------------------- bag
    void FillBag()
    {
        while (bag.Count < 8)
        {
            var seven = new[] { 0, 1, 2, 3, 4, 5, 6 };
            for (int i = 6; i > 0; i--) { int j = rng.Next(i + 1); (seven[i], seven[j]) = (seven[j], seven[i]); }
            foreach (int t in seven) bag.Enqueue(t);
        }
    }

    int NextType()
    {
        FillBag();
        int t = bag.Dequeue();
        FillBag();
        return t;
    }

    // ---------------------------------------------------------------- particles
    float Rnd(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    void Emit(float x, float y, uint color, int count, float speed, int size, float life)
    {
        for (int i = 0; i < count && Particles.Count < 900; i++)
        {
            double ang = rng.NextDouble() * Math.PI * 2;
            float s = Rnd(0.2f, 1f) * speed;
            Particles.Add(new Particle
            {
                X = x, Y = y, Vx = (float)Math.Cos(ang) * s, Vy = (float)Math.Sin(ang) * s - speed * 0.25f,
                Life = life * Rnd(0.6f, 1f), Max = life, Color = color, Size = size,
            });
        }
    }

    void EmitTitleDust()
    {
        Particles.Add(new Particle
        {
            X = Rnd(0, Framebuffer.W), Y = Rnd(0, Framebuffer.H), Vx = Rnd(-6, 6), Vy = Rnd(-14, -4),
            Life = Rnd(2, 8), Max = 8, Color = Framebuffer.Hex(Palette[rng.Next(7)]), Size = 1,
        });
    }

    void UpdateParticles(float dt)
    {
        for (int i = 0; i < Particles.Count; i++)
        {
            var p = Particles[i];
            p.Life -= dt;
            p.X += p.Vx * dt; p.Y += p.Vy * dt;
            if (p.Max < 8f) p.Vy += 260f * dt; // sparks fall, dust drifts
            Particles[i] = p;
        }
        Particles.RemoveAll(p => p.Life <= 0);
        if (Phase == Phase.Title && Particles.Count < 60) EmitTitleDust();
    }
}
