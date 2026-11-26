using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Singleton that serialises background operations (only one may run at a time) and records
/// their progress, so every request — and every admin tab — sees the same state. A running operation can be asked to stop
/// with <see cref="RequestCancel"/>; it stops at its next cancellation check, and what it has created so far is kept, so
/// the next Seed resumes from there.
/// </summary>
public sealed class SeedOperationMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly object _sync = new();
    private SeedOperationSnapshot _current = SeedOperationSnapshot.Idle;
    private string? _requesterEmail;
    private string? _requesterName;
    private CancellationTokenSource? _cancellation;

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

    /// <summary>
    /// The token of the current operation, cancelled by <see cref="RequestCancel"/>. The operation passes it to its work.
    /// <see cref="CancellationToken.None"/> when no operation is running.
    /// </summary>
    public CancellationToken Token
    {
        get { lock (_sync) return _cancellation?.Token ?? CancellationToken.None; }
    }

    /// <summary>Claims the operation slot. Returns false if another operation is already running.</summary>
    public bool TryBegin(string name, string? requesterEmail = null, string? requesterName = null)
    {
        if (!_lock.Wait(0))
            return false;

        lock (_sync)
        {
            (_current, _requesterEmail, _requesterName) =
                (new SeedOperationSnapshot(name, true, DateTime.UtcNow, null, null, null), requesterEmail, requesterName);
            _cancellation = new CancellationTokenSource();
        }
        return true;
    }

    /// <summary>
    /// Asks the running operation to stop. Returns false when no operation is running. The operation keeps running until
    /// it reaches its next cancellation check; the snapshot shows it as stopping until then.
    /// </summary>
    public bool RequestCancel()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (!_current.IsRunning || _cancellation is null)
                return false;

            _current = _current with { IsCancelling = true };
            cancellation = _cancellation;
        }

        // Outside the lock: callbacks registered on the token run synchronously here.
        cancellation.Cancel();
        return true;
    }

    /// <summary>
    /// Records the outcome and releases the slot. Pass the exception if the operation failed. A cancellation after
    /// <see cref="RequestCancel"/> is recorded as stopped, not failed.
    /// </summary>
    public void Complete(Exception? error = null)
    {
        lock (_sync)
        {
            bool cancelled = error is OperationCanceledException && _cancellation?.IsCancellationRequested == true;
            _current = _current with
            {
                IsRunning = false,
                IsCancelling = false,
                CompletedUtc = DateTime.UtcNow,
                Succeeded = error is null,
                Cancelled = cancelled,
                Error = cancelled ? null : error?.Message
            };

            _cancellation?.Dispose();
            _cancellation = null;
        }

        _lock.Release();
    }
}
