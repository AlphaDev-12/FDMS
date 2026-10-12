namespace FdmsGatewayApi.Services.Interfaces;

public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire an execution lock for the specified key.
    /// </summary>
    /// <param name="lockKey">Unique string identifying the resource/queue lock.</param>
    /// <param name="expiry">Time duration before the lock automatically releases if not extended.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Disposable lock handle if acquired, or null if lock is currently held by another node.</returns>
    Task<IAsyncDisposable?> TryAcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken = default);
}
