using System.Collections.Concurrent;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class RedisDistributedLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, string LockId)> ActiveLocks = new();
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    public RedisDistributedLockProvider(ILogger<RedisDistributedLockProvider> logger)
    {
        _logger = logger;
    }

    public Task<IAsyncDisposable?> TryAcquireLockAsync(string lockKey, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Evict expired locks
        if (ActiveLocks.TryGetValue(lockKey, out var existing))
        {
            if (existing.Expiry <= now)
            {
                ActiveLocks.TryRemove(lockKey, out _);
            }
            else
            {
                // Lock currently held by another background worker thread or node
                return Task.FromResult<IAsyncDisposable?>(null);
            }
        }

        string lockId = Guid.NewGuid().ToString();
        var newExpiry = now.Add(expiry);

        if (ActiveLocks.TryAdd(lockKey, (newExpiry, lockId)))
        {
            _logger.LogDebug("Acquired distributed queue lock '{LockKey}' (ID: {LockId}, Expiry: {Expiry})", lockKey, lockId, newExpiry);
            IAsyncDisposable lockReleaser = new LockReleaser(lockKey, lockId, _logger);
            return Task.FromResult<IAsyncDisposable?>(lockReleaser);
        }

        return Task.FromResult<IAsyncDisposable?>(null);
    }

    private class LockReleaser : IAsyncDisposable
    {
        private readonly string _lockKey;
        private readonly string _lockId;
        private readonly ILogger _logger;
        private bool _disposed;

        public LockReleaser(string lockKey, string lockId, ILogger logger)
        {
            _lockKey = lockKey;
            _lockId = lockId;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (ActiveLocks.TryGetValue(_lockKey, out var existing) && existing.LockId == _lockId)
                {
                    ActiveLocks.TryRemove(_lockKey, out _);
                    _logger.LogDebug("Released distributed lock '{LockKey}' (ID: {LockId})", _lockKey, _lockId);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
