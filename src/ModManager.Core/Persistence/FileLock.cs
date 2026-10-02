namespace ModManager.Core.Persistence;

/// <summary>An exclusive lock file beside a shared state file, so a second process (another launcher
/// window, the agent's server) waits its turn instead of writing over a change it never saw. Used by
/// games.json (<c>RegistryStore</c>) and app-settings.json (<c>AppSettingsFile</c>). Source-linked into
/// the app-settings test project, so it names them rather than linking them.</summary>
internal static class FileLock
{
    /// <summary>Hold <paramref name="lockPath"/> exclusively until the returned stream is disposed (the
    /// file is deleted on close). Waits up to <paramref name="timeout"/>, then throws what
    /// <paramref name="busy"/> builds, so the caller writes nothing rather than writing unlocked.</summary>
    public static FileStream Acquire(string lockPath, TimeSpan timeout, Func<Exception?, IOException> busy)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            // Access denied is how Windows answers a lock file another handle still holds pending
            // delete; it clears as soon as that handle closes, so it is waited out like a sharing clash.
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline) throw busy(e);
                Thread.Sleep(25);
            }
        }
    }
}
