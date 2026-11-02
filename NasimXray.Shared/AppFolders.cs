namespace NasimXray.Shared;

// Where the apps keep their per-machine data (settings, logs, cached clinic profile, default backup folder).
// The folder was originally named after the first clinic; it carries the product name now, and an existing
// folder is moved across once so nobody loses their server settings, trusted certificate or backups.
public static class AppFolders
{
    public const string Name = "DiagnosticClinicManager";
    private const string LegacyName = "NasimXrayClinic";

    // parent = %LocalAppData% (client, API logs) or %ProgramData% (API backups). Returns the folder to use.
    public static string Resolve(Environment.SpecialFolder parent)
    {
        var root = Environment.GetFolderPath(parent);
        var current = Path.Combine(root, Name);
        var legacy = Path.Combine(root, LegacyName);

        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, current);
            }
            catch
            {
                // A locked file (another copy still running) makes the move fail — fall back to copying so the
                // settings still come across; the old folder is left in place for the other process.
                try { CopyDirectory(legacy, current); }
                catch { /* nothing more to do — the app starts with fresh settings rather than failing */ }
            }
        }

        return current;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
