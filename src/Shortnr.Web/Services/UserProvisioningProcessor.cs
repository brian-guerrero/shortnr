using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Shortnr.Data;
using Shortnr.Data.Entities;
using Shortnr.Web.Models;

namespace Shortnr.Web.Services;

// Drains the queue of signups/logins written by the OIDC handler's OnTokenValidated
// event (see Program.cs) and upserts the Users table. Kept off the request path so a
// slow or contended DB write never delays completing the login redirect.
public class UserProvisioningProcessor : BackgroundService
{
    private readonly Channel<PendingUserLogin> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserProvisioningProcessor> _logger;

    public UserProvisioningProcessor(Channel<PendingUserLogin> channel, IServiceScopeFactory scopeFactory, ILogger<UserProvisioningProcessor> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UserProvisioningProcessor starting");

        try
        {
            await foreach (var login in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var user = await db.Users.FirstOrDefaultAsync(
                        u => u.Issuer == login.Issuer && u.Subject == login.Subject, stoppingToken);

                    var now = DateTime.UtcNow;
                    if (user is null)
                    {
                        user = new User
                        {
                            Issuer = login.Issuer,
                            Subject = login.Subject,
                            Email = login.Email,
                            Name = login.Name,
                            CreatedAtUtc = now,
                            LastLoginAtUtc = now
                        };
                        db.Users.Add(user);
                        _logger.LogInformation("Provisioned new user for subject {Subject}", login.Subject);
                    }
                    else
                    {
                        user.Email = login.Email ?? user.Email;
                        user.Name = login.Name ?? user.Name;
                        user.LastLoginAtUtc = now;
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error provisioning user for subject {Subject}", login.Subject);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stoppingToken is cancelled
        }

        _logger.LogInformation("UserProvisioningProcessor stopping");
    }
}
