using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Npgsql;

namespace NasimXray.Api.Services;

public record BackupFileInfo(string FileName, long SizeBytes, DateTime CreatedAtUtc);

public class BackupService(IConfiguration config, IOptions<BackupSettings> settingsOptions, ILogger<BackupService> logger)
{
    private static readonly byte[] Magic = "NXB1"u8.ToArray();
    private readonly BackupSettings _settings = settingsOptions.Value;

    private string ResolvedFolder =>
        string.IsNullOrWhiteSpace(_settings.Folder)
            ? Path.Combine(NasimXray.Shared.AppFolders.Resolve(Environment.SpecialFolder.CommonApplicationData), "Backups")
            : _settings.Folder;

    public List<BackupFileInfo> ListBackups()
    {
        var dir = ResolvedFolder;
        if (!Directory.Exists(dir)) return [];

        return Directory.GetFiles(dir, "*.nxb")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupFileInfo(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    public async Task<BackupFileInfo> CreateBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.EncryptionPassword))
            throw new InvalidOperationException("Backup:EncryptionPassword is not configured — set it before creating backups.");

        var connBuilder = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default"));
        var dumpPath = Path.Combine(Path.GetTempPath(), $"nasimxray_{Guid.NewGuid():N}.dump");

        try
        {
            await RunPgToolAsync("pg_dump.exe", connBuilder,
                $"-Fc -f \"{dumpPath}\" -d {connBuilder.Database}");

            var plainBytes = await File.ReadAllBytesAsync(dumpPath);
            var encrypted = Encrypt(plainBytes, _settings.EncryptionPassword);

            Directory.CreateDirectory(ResolvedFolder);
            var fileName = $"nasimxray_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.nxb";
            var fullPath = Path.Combine(ResolvedFolder, fileName);
            await File.WriteAllBytesAsync(fullPath, encrypted);

            logger.LogInformation("Backup created: {FileName} ({Size} bytes)", fileName, encrypted.Length);
            PruneOldBackups();

            var info = new FileInfo(fullPath);
            return new BackupFileInfo(fileName, info.Length, info.CreationTimeUtc);
        }
        finally
        {
            if (File.Exists(dumpPath)) File.Delete(dumpPath);
        }
    }

    public async Task RestoreBackupAsync(string fileName, string password)
    {
        var fullPath = Path.Combine(ResolvedFolder, fileName);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Backup file not found.", fileName);

        var encrypted = await File.ReadAllBytesAsync(fullPath);
        byte[] plainBytes;
        try
        {
            plainBytes = Decrypt(encrypted, password);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Incorrect backup encryption password.");
        }

        var dumpPath = Path.Combine(Path.GetTempPath(), $"nasimxray_restore_{Guid.NewGuid():N}.dump");
        try
        {
            await File.WriteAllBytesAsync(dumpPath, plainBytes);

            var connBuilder = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default"));

            // Drop connections held by this process's own pool so pg_restore's --clean DROP
            // statements aren't blocked by locks from the very connection that triggered this.
            NpgsqlConnection.ClearAllPools();

            await RunPgToolAsync("pg_restore.exe", connBuilder,
                $"--clean --if-exists -d {connBuilder.Database} \"{dumpPath}\"",
                // pg_restore on a --clean run legitimately reports "does not exist" warnings for
                // objects that were never there yet (first restore) — don't treat those as fatal.
                tolerateNonZeroExit: true);

            logger.LogWarning("Database restored from backup {FileName}", fileName);
        }
        finally
        {
            if (File.Exists(dumpPath)) File.Delete(dumpPath);
        }
    }

    private void PruneOldBackups()
    {
        var files = ListBackups();
        foreach (var stale in files.Skip(_settings.RetentionCount))
        {
            try
            {
                File.Delete(Path.Combine(ResolvedFolder, stale.FileName));
                logger.LogInformation("Pruned old backup {FileName}", stale.FileName);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not prune old backup {FileName}", stale.FileName);
            }
        }
    }

    private async Task RunPgToolAsync(string exeName, NpgsqlConnectionStringBuilder conn, string args, bool tolerateNonZeroExit = false)
    {
        var exePath = Path.Combine(_settings.PostgresBinDirectory, exeName);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"{exeName} not found at {exePath}. Check Backup:PostgresBinDirectory.");

        var psi = new ProcessStartInfo(exePath, $"-h {conn.Host} -p {conn.Port} -U {conn.Username} {args}")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["PGPASSWORD"] = conn.Password;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exeName}.");
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !tolerateNonZeroExit)
            throw new InvalidOperationException($"{exeName} failed (exit {process.ExitCode}): {stderr}");

        if (process.ExitCode != 0)
            logger.LogWarning("{Exe} exited {Code} (tolerated): {Stderr}", exeName, process.ExitCode, stderr);
    }

    private static byte[] Encrypt(byte[] plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.Write(salt);
        ms.Write(iv);
        ms.Write(cipher);
        return ms.ToArray();
    }

    private static byte[] Decrypt(byte[] data, string password)
    {
        if (data.Length < Magic.Length + 32 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidOperationException("Not a recognized .nxb backup file.");

        var offset = Magic.Length;
        var salt = data.AsSpan(offset, 16).ToArray(); offset += 16;
        var iv = data.AsSpan(offset, 16).ToArray(); offset += 16;
        var cipher = data.AsSpan(offset).ToArray();

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }
}
