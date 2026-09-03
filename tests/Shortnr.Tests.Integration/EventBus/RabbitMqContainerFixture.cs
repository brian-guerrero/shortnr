using Testcontainers;
using Testcontainers.RabbitMq;
using Xunit;

namespace Shortnr.Tests.Integration.EventBus;

/// <summary>
/// Backs a real RabbitMQ instance via <see cref="Testcontainers.RabbitMQ"/> for the PRD-018
/// distributed event-bus suite. One container is shared by the whole "RabbitMQ" xunit collection
/// (collection fixtures run serially). Tests that need isolation point at a distinct vhost or use
/// exclusive auto-delete queues so messages from one test never bleed into another.
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    /// <summary>False when Docker isn't reachable on this machine. Tests skip (never fail)
    /// when unavailable, so a bare `dotnet test` passes on a machine without a container
    /// runtime — CI's Docker-enabled runners exercise them for real.</summary>
    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    /// <summary>AMQP connection string pointing at the shared container.</summary>
    public string GetConnectionString() =>
        $"amqp://guest:guest@{_container!.Hostname}:{_container.GetMappedPublicPort(5672)}";

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder()
                .WithImage("rabbitmq:3-management-alpine")
                .WithPortBinding(5672, true)
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            IsAvailable = _container.State.ToString() == "Running";
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"RabbitMQ container unavailable: {ex.Message}";
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
        if (_container is { } c && c.State.ToString() == "Running")
            await c.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Restarts the shared container after <see cref="StopAsync"/>.</summary>
    public Task StartAsync() => _container!.StartAsync();
}

[CollectionDefinition("RabbitMQ")]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqContainerFixture>;
