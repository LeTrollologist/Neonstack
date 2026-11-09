namespace NeonStack.Tests;

/// <summary>Drives the rules engine through its public input/state surface: no test-only hooks.</summary>
public static class GameTests
{
    static bool[] None() => new bool[(int)Key.Count];

    /// <summary>Taps a key: one frame pressed, one frame released.</summary>
    static void Tap(TetrisGame g, Key key)
    {
        var down = None();
        down[(int)key] = true;
        g.Update(0.001f, down);
        g.Update(0.001f, None());
    }

    static void Wait(TetrisGame g, float seconds)
    {
        int steps = Math.Max(1, (int)MathF.Ceiling(seconds / 0.05f));
        for (int i = 0; i < steps; i++) g.Update(0.05f, None());
    }

    static TetrisGame Playing(int level = 0)
    {
        // Every test starts from a clean campaign so persisted progress cannot leak between cases.
        try { File.Delete(SaveData.File("progress.txt")); } catch { /* nothing to delete */ }
        try { File.Delete(SaveData.File("best.txt")); } catch { /* nothing to delete */ }
        var g = new TetrisGame { Phase = Phase.Playing, LevelIndex = level };
        Array.Clear(g.Board);
        return g;
    }

    static void ClearBoard(TetrisGame g)
    {
        Array.Clear(g.Board);
        g.ClearRows.Clear();
    }

    static void FillRowExcept(TetrisGame g, int y, params int[] holes)
    {
        for (int x = 0; x < TetrisGame.Cols; x++)
            if (Array.IndexOf(holes, x) < 0) g.Board[x, y] = 1;
    }

    static bool HasPopup(TetrisGame g, string prefix) =>
        g.Popups.Any(p => p.Text.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Stack of four rows with only column 0 open, and an I piece standing in it.</summary>
    static void ArmTetris(TetrisGame g)
    {
        ClearBoard(g);
        for (int y = 16; y < TetrisGame.Rows; y++) FillRowExcept(g, y, 0);
        g.Cur = new Piece { Type = 0, Rot = 1, X = -2, Y = 16 };   // vertical I in column 0
    }

    /// <summary>Row 19 open only under the O piece, which locks on top of the stack.</summary>
    static void ArmSingle(TetrisGame g)
    {
        ClearBoard(g);
        FillRowExcept(g, 19, 0, 1);
        g.Cur = new Piece { Type = 3, Rot = 0, X = 0, Y = 18 };
    }

    /// <summary>T piece rotated into a notch whose three corners are blocked (full T-spin single).</summary>
    static void ArmTSpinSingle(TetrisGame g)
    {
        ClearBoard(g);
        FillRowExcept(g, 19, 4);      // the nub lands in column 4 and completes the row
        g.Board[5, 17] = 1;           // third corner, above the right arm
        g.Cur = new Piece { Type = 5, Rot = 1, X = 3, Y = 17 };
    }

    // ---------------------------------------------------------------- fundamentals

    [Test]
    public static void SevenBagContainsEveryPiece()
    {
        var g = new TetrisGame();
        for (int bag = 0; bag < 2; bag++)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < 7; i++) seen.Add(g.Preview(bag * 7 + i));
            Harness.Equal(7, seen.Count, $"bag {bag} should hold each of the seven pieces once");
        }
    }

    [Test]
    public static void RotationUsesWallKicks()
    {
        var g = Playing();
        g.Cur = new Piece { Type = 0, Rot = 0, X = 6, Y = 5 };

        // Column 8 is blocked, so a plain CW rotation of the horizontal I cannot fit there.
        for (int y = 5; y <= 8; y++) g.Board[8, y] = 1;

        Tap(g, Key.X);

        Harness.Equal(1, g.Cur.Rot, "I should rotate clockwise");
        Harness.Equal(4, g.Cur.X, "I should take the -2 kick instead of overlapping the stack");
    }

    [Test]
    public static void HardDropPaysTwoPointsPerCell()
    {
        var g = Playing();
        g.Cur = new Piece { Type = 3, Rot = 0, X = 4, Y = 0 };

        Tap(g, Key.Space);

        Harness.Equal(36, g.Score, "O dropped 18 cells pays 2 per cell");
    }

    [Test]
    public static void SoftDropPaysPerCell()
    {
        var g = Playing();
        g.Cur = new Piece { Type = 3, Rot = 0, X = 4, Y = 0 };
        int startY = g.Cur.Y;

        var down = None();
        down[(int)Key.Down] = true;
        for (int i = 0; i < 8; i++) g.Update(0.05f, down);

        Harness.True(g.Cur.Y > startY, "soft drop should move the piece down");
        Harness.Equal(g.Cur.Y - startY, g.Score, "soft drop pays one point per cell");
    }

    [Test]
    public static void GhostSitsAtLandingPosition()
    {
        var g = Playing();
        g.Cur = new Piece { Type = 3, Rot = 0, X = 4, Y = 0 };
        Harness.Equal(18, g.GhostY(), "O on an empty board lands on the floor");

        g.Board[4, 10] = 1;
        Harness.Equal(8, g.GhostY(), "the ghost stops on top of the stack");
    }

    [Test]
    public static void HoldSwapsOncePerPiece()
    {
        var g = Playing();
        int first = g.Cur.Type;

        Tap(g, Key.C);
        Harness.Equal(first, g.Hold, "hold stores the active piece");
        Harness.True(g.HoldUsed, "hold is spent until the next lock");
        int second = g.Cur.Type;

        Tap(g, Key.C);
        Harness.Equal(second, g.Cur.Type, "a second hold in the same piece is ignored");
        Harness.Equal(first, g.Hold, "the held piece is unchanged");

        Tap(g, Key.Space);
        Harness.False(g.HoldUsed, "locking refreshes the hold");
    }

    // ---------------------------------------------------------------- scoring

    [Test]
    public static void SingleClearScoresByLevel()
    {
        var g = Playing(level: 3);            // level 4
        ArmSingle(g);

        Tap(g, Key.Space);

        Harness.Equal(400, g.Score, "single is 100 x level 4");
        Harness.Equal(Phase.Clearing, g.Phase, "clear animation starts");
        Wait(g, 0.4f);
        Harness.Equal(1, g.Lines, "one line cleared");
        Harness.Equal(Phase.Playing, g.Phase, "play resumes after the clear");
    }

    [Test]
    public static void ComboAddsFiftyPerStep()
    {
        var g = Playing();
        ArmSingle(g);
        Tap(g, Key.Space);
        Wait(g, 0.4f);

        ArmSingle(g);
        Tap(g, Key.Space);

        Harness.Equal(2, g.Combo, "two clears in a row");
        Harness.Equal(100 + 150, g.Score, "second consecutive single pays 100 + 50 combo");
    }

    [Test]
    public static void TSpinSingleScoresGuidelineValue()
    {
        var g = Playing();
        ArmTSpinSingle(g);

        Tap(g, Key.X);        // rotate into the notch
        Tap(g, Key.Space);    // hard drop is a no-op here, then lock

        Harness.True(g.SpinDetected, "three blocked corners after a rotation is a T-spin");
        Harness.False(g.SpinMini, "both front corners are blocked, so the spin is full");
        Harness.Equal(800, g.Score, "T-spin single is 800 x level 1");
        Harness.True(HasPopup(g, "T-SPIN SINGLE"), "the popup names the T-spin");
    }

    [Test]
    public static void MiniTSpinWithThreeCorners()
    {
        var g = Playing();
        ClearBoard(g);
        FillRowExcept(g, 19, 4, 5);   // nub column and the right corner stay open
        g.Board[3, 17] = 1;
        g.Board[5, 17] = 1;
        g.Cur = new Piece { Type = 5, Rot = 1, X = 3, Y = 17 };

        Tap(g, Key.X);
        Tap(g, Key.Space);

        Harness.True(g.SpinDetected, "three corners are enough for a spin");
        Harness.True(g.SpinMini, "the front corners are not both blocked, so it is a mini");
        Harness.Equal(400, g.Score, "a mini T-spin without lines scores 400 x level");
        Harness.True(HasPopup(g, "T-SPIN MINI"), "the popup marks the mini");
    }

    [Test]
    public static void TwoCornerRotationIsNotASpin()
    {
        var g = Playing();
        ClearBoard(g);
        FillRowExcept(g, 19, 3, 4);   // hole under the arm and under the nub: no line can clear
        g.Board[3, 17] = 1;           // only one top corner and one bottom corner are blocked
        g.Cur = new Piece { Type = 5, Rot = 1, X = 3, Y = 17 };

        Tap(g, Key.X);
        Tap(g, Key.Space);

        Harness.False(g.SpinDetected, "two blocked corners is not a T-spin");
        Harness.Equal(0, g.Score, "without a spin and without a clear there is no score");
    }

    [Test]
    public static void TSpinZeroKeepsTheChainAlive()
    {
        var g = Playing();
        ArmTSpinSingle(g);
        Tap(g, Key.X);
        Tap(g, Key.Space);
        int afterSingle = g.Score;
        Wait(g, 0.4f);

        // A T-spin that clears nothing: score 400 and the B2B chain must survive. Column 0 is left
        // open so the nub cannot complete the bottom row.
        ClearBoard(g);
        FillRowExcept(g, 19, 0, 4);
        g.Board[5, 17] = 1;
        g.Cur = new Piece { Type = 5, Rot = 1, X = 3, Y = 17 };
        Tap(g, Key.X);
        Tap(g, Key.Space);

        Harness.True(g.SpinDetected, "spin detected with no line clear");
        Harness.Equal(afterSingle + 400, g.Score, "T-spin without lines pays 400");
        Harness.True(g.BackToBack, "a line-less T-spin keeps the chain");
        Harness.Equal(0, g.Combo, "no lines means no combo");
    }

    [Test]
    public static void BackToBackPaysOneAndAHalf()
    {
        var g = Playing();
        ArmTetris(g);
        Tap(g, Key.Space);
        Harness.Equal(800, g.Score, "first tetris pays the base value");
        Harness.True(g.BackToBack, "a tetris opens a back-to-back chain");
        Wait(g, 0.4f);

        ArmTetris(g);
        Tap(g, Key.Space);
        Harness.Equal(800 + 1200 + 50, g.Score, "second tetris pays 1.5x plus the combo");
    }

    [Test]
    public static void NonDifficultClearBreaksBackToBack()
    {
        var g = Playing();
        ArmTetris(g);
        Tap(g, Key.Space);
        Wait(g, 0.4f);

        ArmSingle(g);
        Tap(g, Key.Space);

        Harness.False(g.BackToBack, "a plain single ends the difficult-clear chain");
        Harness.Equal(800 + 100 + 50, g.Score, "the single is not multiplied");
    }

    // ---------------------------------------------------------------- campaign

    [Test]
    public static void LevelClearUnlocksTheNextLevel()
    {
        var g = Playing();
        g.LevelLines = TetrisGame.Levels[0].TargetLines - 1;
        ArmSingle(g);

        Tap(g, Key.Space);
        Wait(g, 0.4f);                 // finish the clear

        Harness.Equal(Phase.LevelClear, g.Phase, "reaching the goal shows the clear screen");
        Harness.Equal(1, g.Unlocked, "the next level unlocks immediately");
        Harness.Equal(100 + 500, g.Score, "the level bonus is added");
        Harness.Equal("1", File.ReadAllText(SaveData.File("progress.txt")).Trim(), "progress is saved");

        Wait(g, 3.3f);                 // the clear screen advances by itself
        Harness.Equal(Phase.Playing, g.Phase, "the next level starts");
        Harness.Equal(2, g.Level, "level counter moved on");
        Harness.Equal(0, g.LevelLines, "the new level starts from zero lines");
    }

    [Test]
    public static void FinalLevelClearWinsTheCampaign()
    {
        var g = Playing(level: TetrisGame.Levels.Length - 1);
        g.LevelLines = TetrisGame.Levels[^1].TargetLines - 1;
        ArmSingle(g);

        Tap(g, Key.Space);
        Wait(g, 0.4f);
        Harness.Equal(Phase.LevelClear, g.Phase, "final goal reached");

        Wait(g, 3.3f);
        Harness.Equal(Phase.Won, g.Phase, "clearing the last level wins the campaign");
        Harness.True(int.TryParse(File.ReadAllText(SaveData.File("best.txt")), out int best) && best > 0,
            "the run's score is recorded as a best");
    }

    [Test]
    public static void BlockedSpawnEndsTheRun()
    {
        var g = Playing();
        for (int y = 0; y < 2; y++) FillRowExcept(g, y, 9);
        g.Cur = new Piece { Type = 3, Rot = 0, X = 4, Y = 4 };

        Tap(g, Key.Space);

        Harness.True(g.Phase is Phase.Over or Phase.NameEntry, "a blocked spawn is game over");
    }

    [Test]
    public static void SuppressedConfirmDoesNotStartTheGame()
    {
        var g = new TetrisGame();
        var down = None();
        down[(int)Key.Enter] = true;

        g.SuppressConfirm(0.5f);
        g.Update(0.05f, down);
        Harness.Equal(Phase.Title, g.Phase, "the intro's Enter must not start a campaign");

        for (int i = 0; i < 12; i++) g.Update(0.05f, None());   // let the block expire
        g.Update(0.05f, down);
        Harness.Equal(Phase.Playing, g.Phase, "a fresh Enter starts the game");
    }

    [Test]
    public static void PauseTogglesAndResumes()
    {
        var g = Playing();
        Tap(g, Key.P);
        Harness.Equal(Phase.Paused, g.Phase, "P pauses");

        Tap(g, Key.P);
        Harness.Equal(Phase.Playing, g.Phase, "P resumes");
    }

    [Test]
    public static void SettingsBackRowIgnoresLeftRight()
    {
        var g = new TetrisGame();
        Tap(g, Key.S);
        Harness.Equal(Phase.Settings, g.Phase, "S opens settings");

        for (int i = 0; i < TetrisGame.SettingsRows - 1; i++) Tap(g, Key.Down);
        Harness.Equal(TetrisGame.SettingsRows - 1, g.SettingsIndex, "the cursor is on BACK");

        Tap(g, Key.Right);
        Harness.Equal(Phase.Settings, g.Phase, "Left/Right on BACK must not leave the screen");

        Tap(g, Key.Enter);
        Harness.Equal(Phase.Title, g.Phase, "Enter leaves settings");
    }
}
