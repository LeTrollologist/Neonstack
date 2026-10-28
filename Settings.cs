namespace NeonStack;

/// <summary>User options, persisted as simple key=value lines in the save folder.</summary>
public sealed class Settings
{
    public static Settings Current { get; private set; } = Load();

    public bool Fullscreen = true;
    public int MusicVolume = 7;      // 0..10
    public int SfxVolume = 8;        // 0..10
    public bool Ghost = true;
    public bool Scanlines = true;
    public bool Shake = true;

    static string FilePath => SaveData.File("settings.txt");

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw[..eq].Trim().ToLowerInvariant();
                string val = raw[(eq + 1)..].Trim();

                switch (key)
                {
                    case "fullscreen": s.Fullscreen = ParseBool(val, s.Fullscreen); break;
                    case "musicvolume": s.MusicVolume = ParseInt(val, s.MusicVolume); break;
                    case "sfxvolume": s.SfxVolume = ParseInt(val, s.SfxVolume); break;
                    case "ghost": s.Ghost = ParseBool(val, s.Ghost); break;
                    case "scanlines": s.Scanlines = ParseBool(val, s.Scanlines); break;
                    case "shake": s.Shake = ParseBool(val, s.Shake); break;
                }
            }
        }
        catch { /* defaults */ }

        s.MusicVolume = Math.Clamp(s.MusicVolume, 0, 10);
        s.SfxVolume = Math.Clamp(s.SfxVolume, 0, 10);
        return s;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(FilePath, new[]
            {
                "fullscreen=" + (Fullscreen ? 1 : 0),
                "musicvolume=" + MusicVolume,
                "sfxvolume=" + SfxVolume,
                "ghost=" + (Ghost ? 1 : 0),
                "scanlines=" + (Scanlines ? 1 : 0),
                "shake=" + (Shake ? 1 : 0),
            });
        }
        catch { /* read-only install: options just won't persist */ }
    }

    public static void Reload() => Current = Load();

    static int ParseInt(string s, int fallback) => int.TryParse(s, out int v) ? v : fallback;
    static bool ParseBool(string s, bool fallback) => s is "1" or "true" or "on" or "yes" ? true : s is "0" or "false" or "off" or "no" ? false : fallback;
}
