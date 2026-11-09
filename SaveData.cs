namespace NeonStack;

/// <summary>Per-user writable folder for settings, progress and scores.</summary>
public static class SaveData
{
    public static string Dir { get; } = Resolve();

    public static string File(string name) => Path.Combine(Dir, name);

    static string Resolve()
    {
        // Tests (and portable runs) can point the save files at a scratch folder.
        string? overridden = Environment.GetEnvironmentVariable("NEONSTACK_SAVEDIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            try
            {
                Directory.CreateDirectory(overridden);
                return overridden;
            }
            catch
            {
                // Fall through to the normal per-user folder.
            }
        }

        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonStack");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            // Read-only / unusual environment: fall back to the application folder.
            return AppContext.BaseDirectory;
        }
    }
}
