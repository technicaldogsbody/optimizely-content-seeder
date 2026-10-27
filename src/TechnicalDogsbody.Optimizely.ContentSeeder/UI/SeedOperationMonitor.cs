using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Singleton that serialises background operations (only one may run at a time) and records
/// their progress, so every request — and every admin tab — sees the same state.
/// </summary>
public sealed class SeedOperationMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _sync = new();
    private SeedOperationSnapshot _current = SeedOperationSnapshot.Idle;
    private string? _requesterEmail;
    private string? _requesterName;

    /// <summary>
    /// A snapshot of the current or most recent operation. Safe to read from any thread.
    /// </summary>
    public SeedOperationSnapshot Current
    {
        get { lock (_sync) return _current; }
    }

    /// <summary>
    /// The email address of the user who started the current operation, or null when nobody did (a startup run). It is not
    /// part of the snapshot, so it is never sent to the browser.
    /// </summary>
    public string? RequesterEmail
    {
        get { lock (_sync) return _requesterEmail; }
    }

    /// <summary>The sign-in name of the user who started the current operation, or null when nobody did.</summary>
    public string? RequesterName
    {
        get { lock (_sync) return _requesterName; }
    }

    /// <summary>Claims the operation slot. Returns false if another operation is already running.</summary>
    public bool TryBegin(string name, string? requesterEmail = null, string? requesterName = null)
    {
        if (!_lock.Wait(0))
            return false;

        lock (_sync)
            (_current, _requesterEmail, _requesterName) =
                (new SeedOperationSnapshot(name, true, DateTime.UtcNow, null, null, null), requesterEmail, requesterName);
        return true;
    }

    /// <summary>Records the outcome and releases the slot. Pass the exception if the operation failed.</summary>
    public void Complete(Exception? error = null)
    {
        lock (_sync)
        {
            _current = _current with
            {
                IsRunning = false,
                CompletedUtc = DateTime.UtcNow,
                Succeeded = error is null,
                Error = error?.Message
            };
        }

        _lock.Release();
    }
}
