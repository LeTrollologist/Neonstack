namespace NeonStack;

/// <summary>Per-user writable folder for settings, progress and scores.</summary>
public static class SaveData
{
    public static string Dir { get; } = Resolve();

    public static string File(string name) => Path.Combine(Dir, name);

    static string Resolve()
    {
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
