using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;
using Xunit;

namespace Shortnr.Tests.Integration.RateLimiting;

/// <summary>
/// Backs a real Redis instance via <see cref="Testcontainers.Redis"/> for the PRD-017
/// distributed rate-limit suite. One container is shared by the whole "Redis" xunit
/// collection (collection fixtures run serially). Tests that need key isolation select a
/// distinct Redis database index via <see cref="GetConnectionString(int)"/> so counters
/// from one test never bleed into another.
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>False when Docker isn't reachable on this machine. Tests skip (never fail)
    /// when unavailable, so a bare `dotnet test` passes on a machine without a container
    /// runtime — CI's Docker-enabled runners exercise them for real.</summary>
    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    private int _nextDatabase;

    /// <summary>Base connection string pointing at the shared container (DB 0).</summary>
    public string GetConnectionString() => GetConnectionString(0);

    /// <summary>
    /// Connection string for a specific Redis database index, so tests sharing the container
    /// get isolated key spaces. Also pins command timeouts so the graceful-degradation path
    /// fails fast once Redis is killed.
    /// </summary>
    public string GetConnectionString(int database) =>
        $"{_container!.Hostname}:{_container.GetMappedPublicPort(6379)}" +
        $",abortConnect=false,syncTimeout=1000,defaultDatabase={database}";

    /// <summary>Rotates through Redis's 16 databases so each test gets a fresh key space.</summary>
    public int NextDatabase() => Interlocked.Increment(ref _nextDatabase) % 16;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder("redis:7-alpine")
                .WithPortBinding(6379, true)
                .Build();

            await _container.StartAsync()
                .ConfigureAwait(false);

            IsAvailable = _container.State == TestcontainersStates.Running;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Redis container unavailable: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Stops the shared container mid-suite (for the graceful-degradation tests).</summary>
    public async Task StopAsync()
    {
        if (_container is { } c && c.State == TestcontainersStates.Running)
            await c.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Restarts the shared container after <see cref="StopAsync"/>.</summary>
    public Task StartAsync() => _container!.StartAsync();
}

[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisContainerFixture>;