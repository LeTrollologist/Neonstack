namespace NeonStack;

public sealed class ScoreEntry
{
    public string Name = "AAA";
    public int Score;
    public int Level;
    public int Lines;
    public DateTime Date = DateTime.Now;
}

/// <summary>Local top-10 table, stored as pipe-separated records in the save folder.</summary>
public static class Leaderboard
{
    public const int Max = 10;

    static readonly List<ScoreEntry> entries = Load();
    public static IReadOnlyList<ScoreEntry> Entries => entries;

    static string FilePath => SaveData.File("scores.txt");

    public static bool Qualifies(int score) =>
        score > 0 && (entries.Count < Max || score > entries[^1].Score);

    /// <summary>Inserts the score, trims to the top ten and returns the 0-based rank.</summary>
    public static int Add(string name, int score, int level, int lines)
    {
        var entry = new ScoreEntry
        {
            Name = (name.Length == 0 ? "AAA" : name).PadRight(3)[..3].ToUpperInvariant(),
            Score = score,
            Level = level,
            Lines = lines,
            Date = DateTime.Now,
        };

        int at = entries.FindIndex(e => score > e.Score);
        if (at < 0) at = entries.Count;
        entries.Insert(at, entry);
        if (entries.Count > Max) entries.RemoveRange(Max, entries.Count - Max);
        Save();
        return at;
    }

    static List<ScoreEntry> Load()
    {
        var list = new List<ScoreEntry>();
        try
        {
            if (!File.Exists(FilePath)) return list;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] p = line.Split('|');
                if (p.Length < 5) continue;
                if (!int.TryParse(p[1], out int score) || !int.TryParse(p[2], out int level) || !int.TryParse(p[3], out int lines)) continue;
                list.Add(new ScoreEntry
                {
                    Name = p[0].Length >= 3 ? p[0][..3].ToUpperInvariant() : p[0].ToUpperInvariant().PadRight(3),
                    Score = score,
                    Level = level,
                    Lines = lines,
                    Date = DateTime.TryParse(p[4], out var d) ? d : DateTime.Now,
                });
            }
        }
        catch { /* corrupt scores: start fresh */ }

        list.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
        return list;
    }

    static void Save()
    {
        try
        {
            File.WriteAllLines(FilePath, entries.Select(e =>
                $"{e.Name}|{e.Score}|{e.Level}|{e.Lines}|{e.Date:yyyyMMdd}"));
        }
        catch { /* read-only install: the table just won't persist */ }
    }
}
