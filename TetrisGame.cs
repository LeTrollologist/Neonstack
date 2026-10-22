namespace NeonStack;

public enum Key { Left, Right, Down, Up, Z, X, C, Space, Enter, Escape, P, Count }
public enum Phase { Title, Playing, Paused, Clearing, Over }

public sealed class Piece { public int Type, Rot, X, Y; }

public struct Particle { public float X, Y, Vx, Vy, Life, Max; public uint Color; public int Size; }

/// <summary>Engine-agnostic game logic: SRS rotation + kicks, 7-bag, hold, ghost, lock delay, DAS/ARR, guideline scoring.</summary>
public sealed class TetrisGame
{
    public const int Cols = 10, Rows = 20;
    public const int OriginX = 240, OriginY = 20, Cell = 16;   // board placement in framebuffer pixels
    public const float ClearDur = 0.32f;
    const float Das = 0.16f, Arr = 0.04f, LockDelay = 0.5f;

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
    public int Score, Lines, Level = 1, Best, Combo;
    public Phase Phase = Phase.Title;
    public bool Quit;
    public float Time, Shake, ClearT, MsgT;
    public string Msg = "";
    public readonly List<int> ClearRows = new();
    public readonly List<Particle> Particles = new();

    readonly Queue<int> bag = new();
    readonly Random rng = new();
    readonly bool[] prev = new bool[(int)Key.Count];
    readonly bool[] press = new bool[(int)Key.Count];
    float grav, lockT, dasT, arrT, overT;
    int lockResets, dasDir, lowY;

    static string BestPath => Path.Combine(AppContext.BaseDirectory, "best.txt");

    public TetrisGame()
    {
        try { if (File.Exists(BestPath) && int.TryParse(File.ReadAllText(BestPath).Trim(), out int b)) Best = b; }
        catch { /* no persistence is fine */ }
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

    public float GravityInterval()
    {
        float l = Math.Min(Level, 20) - 1;
        return Math.Max(0.004f, (float)Math.Pow(0.8 - l * 0.007, l));
    }

    // ---------------------------------------------------------------- update
    bool Pr(Key k) => press[(int)k];

    public void Update(float dt, bool[] down)
    {
        dt = Math.Min(dt, 0.05f);
        Time += dt;
        for (int i = 0; i < prev.Length; i++) { press[i] = down[i] && !prev[i]; prev[i] = down[i]; }
        Shake = Math.Max(0f, Shake - dt * 30f);
        if (MsgT > 0) MsgT -= dt;
        UpdateParticles(dt);

        switch (Phase)
        {
            case Phase.Title:
                if (Pr(Key.Enter) || Pr(Key.Space)) Start();
                if (Pr(Key.Escape)) Quit = true;
                break;
            case Phase.Paused:
                if (Pr(Key.P) || Pr(Key.Escape) || Pr(Key.Enter)) Phase = Phase.Playing;
                break;
            case Phase.Over:
                overT += dt;
                if (overT > 0.6f && (Pr(Key.Enter) || Pr(Key.Space))) Start();
                if (Pr(Key.Escape)) Phase = Phase.Title;
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

    void Start()
    {
        Array.Clear(Board);
        Score = Lines = Combo = 0;
        Level = 1;
        Hold = -1;
        HoldUsed = false;
        bag.Clear();
        FillBag();
        Particles.Clear();
        ClearRows.Clear();
        dasDir = 0; dasT = arrT = 0;
        MsgT = 0;
        Phase = Phase.Playing;
        Spawn(NextType());
    }

    void Play(float dt, bool[] down)
    {
        if (Pr(Key.P) || Pr(Key.Escape)) { Phase = Phase.Paused; return; }

        int dir = down[(int)Key.Left] == down[(int)Key.Right] ? 0 : (down[(int)Key.Left] ? -1 : 1);
        if (dir != dasDir)
        {
            dasDir = dir; dasT = 0; arrT = 0;
            if (dir != 0) Move(dir);
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

    void AfterMove()
    {
        if (!Fits(Cur, 0, 1) && lockResets < 15) { lockT = 0; lockResets++; }
    }

    bool Move(int dx)
    {
        if (!Fits(Cur, dx, 0)) return false;
        Cur.X += dx;
        AfterMove();
        return true;
    }

    void Rotate(int dir) // 0 = clockwise, 1 = counter-clockwise
    {
        int nr = (Cur.Rot + (dir == 0 ? 1 : 3)) & 3;
        var kicks = (Cur.Type == 0 ? KickI : KickJLSTZ)[dir][Cur.Rot];
        foreach (var k in kicks)
        {
            if (FitsAt(Cur.Type, nr, Cur.X + k.x, Cur.Y - k.y))
            {
                Cur.Rot = nr; Cur.X += k.x; Cur.Y -= k.y;
                AfterMove();
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
        Lock();
    }

    void Lock()
    {
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
        if (n == 0) { Combo = 0; Spawn(NextType()); return; }

        Combo++;
        Score += new[] { 0, 100, 300, 500, 800 }[n] * Level;
        if (Combo > 1) Score += 50 * (Combo - 1) * Level;
        Msg = n switch { 1 => "SINGLE", 2 => "DOUBLE", 3 => "TRIPLE", _ => "TETRIS" };
        if (Combo > 1 && n < 4) Msg += " X" + Combo;
        MsgT = 1.2f;
        Shake = Math.Max(Shake, n * 2.5f);
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
        ClearRows.Clear();
        int nl = 1 + Lines / 10;
        if (nl > Level) { Level = nl; Msg = "LEVEL " + Level; MsgT = 1.5f; }
        Phase = Phase.Playing;
        Spawn(NextType());
    }

    void Spawn(int type)
    {
        Cur = new Piece { Type = type, Rot = 0, X = type == 3 ? 4 : 3, Y = type == 0 ? -1 : 0 };
        grav = 0; lockT = 0; lockResets = 0; lowY = Cur.Y;
        if (!Fits(Cur, 0, 0)) GameOver();
    }

    void GameOver()
    {
        Phase = Phase.Over;
        overT = 0;
        Shake = 8f;
        if (Score > Best)
        {
            Best = Score;
            try { File.WriteAllText(BestPath, Best.ToString()); } catch { /* ignore */ }
        }
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
                if (Board[x, y] != 0)
                    Emit(OriginX + x * Cell + Cell / 2, OriginY + y * Cell + Cell / 2, Framebuffer.Hex(Palette[Board[x, y] - 1]), 1, 90f, 2, 1.2f);
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
