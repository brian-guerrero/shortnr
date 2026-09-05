using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shortnr.Data;
using Shortnr.Data.Entities;

namespace Shortnr.Web.Features.Social;

/// <summary>
/// Manages social account CRUD operations with transparent token encryption/decryption.
/// Tokens are encrypted on write and decrypted on read using <see cref="SocialTokenEncryptionService"/>.
/// </summary>
public sealed class SocialAccountService(
    AppDbContext db,
    SocialTokenEncryptionService encryption)
{
    /// <summary>
    /// Returns all social accounts for the given owner, with tokens decrypted.
    /// </summary>
    public async Task<IReadOnlyList<SocialAccount>> GetByOwnerAsync(long ownerUserId, CancellationToken ct = default)
    {
        var accounts = await db.SocialAccounts
            .AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderBy(a => a.Platform)
            .ToListAsync(ct);

        foreach (var account in accounts)
            DecryptTokens(account);

        return accounts;
    }

    /// <summary>
    /// Returns a single social account by ID, with tokens decrypted. Null if not found.
    /// </summary>
    public async Task<SocialAccount?> GetByIdAsync(long id, long ownerUserId, CancellationToken ct = default)
    {
        var account = await db.SocialAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);

        if (account is not null)
            DecryptTokens(account);

        return account;
    }

    /// <summary>
    /// Creates or updates a social account. Tokens are encrypted before persisting.
    /// </summary>
    public async Task<SocialAccount> UpsertAsync(
        long ownerUserId,
        string platform,
        string platformAccountId,
        string displayName,
        string accessToken,
        string? refreshToken,
        DateTime? accessTokenExpiryUtc,
        DateTime? refreshTokenExpiryUtc,
        CancellationToken ct = default)
    {
        var existing = await db.SocialAccounts
            .FirstOrDefaultAsync(a =>
                a.OwnerUserId == ownerUserId &&
                a.Platform == platform &&
                a.PlatformAccountId == platformAccountId, ct);

        if (existing is not null)
        {
            existing.DisplayName = displayName;
            existing.AccessTokenEncrypted = encryption.Encrypt(accessToken);
            existing.RefreshTokenEncrypted = refreshToken is not null ? encryption.Encrypt(refreshToken) : null;
            existing.AccessTokenExpiryUtc = accessTokenExpiryUtc;
            existing.RefreshTokenExpiryUtc = refreshTokenExpiryUtc;
            existing.TokenRefreshFailed = false;
            await db.SaveChangesAsync(ct);
            DecryptTokens(existing);
            return existing;
        }

        var account = new SocialAccount
        {
            OwnerUserId = ownerUserId,
            Platform = platform,
            PlatformAccountId = platformAccountId,
            DisplayName = displayName,
            AccessTokenEncrypted = encryption.Encrypt(accessToken),
            RefreshTokenEncrypted = refreshToken is not null ? encryption.Encrypt(refreshToken) : null,
            AccessTokenExpiryUtc = accessTokenExpiryUtc,
            RefreshTokenExpiryUtc = refreshTokenExpiryUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.SocialAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        DecryptTokens(account);
        return account;
    }

    /// <summary>
    /// Deletes a social account.
    /// </summary>
    public async Task<bool> DeleteAsync(long id, long ownerUserId, CancellationToken ct = default)
    {
        var account = await db.SocialAccounts
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);

        if (account is null)
            return false;

        db.SocialAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Returns accounts whose access token expires within the given window.
    /// Tokens are NOT decrypted — the caller only needs entity metadata for refresh scheduling.
    /// </summary>
    public async Task<IReadOnlyList<SocialAccount>> GetExpiringAsync(
        int refreshWindowHours,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(refreshWindowHours);
        return await db.SocialAccounts
            .AsNoTracking()
            .Where(a =>
                a.RefreshTokenEncrypted != null &&
                !a.TokenRefreshFailed &&
                (a.AccessTokenExpiryUtc == null || a.AccessTokenExpiryUtc <= cutoff))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Updates token fields after a successful refresh.
    /// </summary>
    public async Task UpdateTokensAfterRefreshAsync(
        long accountId,
        string newAccessToken,
        string? newRefreshToken,
        DateTime? newAccessTokenExpiryUtc,
        DateTime? newRefreshTokenExpiryUtc,
        CancellationToken ct = default)
    {
        var account = await db.SocialAccounts.FindAsync([accountId], ct);
        if (account is null)
            return;

        account.AccessTokenEncrypted = encryption.Encrypt(newAccessToken);
        if (newRefreshToken is not null)
            account.RefreshTokenEncrypted = encryption.Encrypt(newRefreshToken);
        account.AccessTokenExpiryUtc = newAccessTokenExpiryUtc;
        account.RefreshTokenExpiryUtc = newRefreshTokenExpiryUtc;
        account.LastRefreshedAtUtc = DateTime.UtcNow;
        account.TokenRefreshFailed = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks a social account as having a failed token refresh.
    /// </summary>
    public async Task MarkRefreshFailedAsync(long accountId, CancellationToken ct = default)
    {
        var account = await db.SocialAccounts.FindAsync([accountId], ct);
        if (account is null)
            return;

        account.TokenRefreshFailed = true;
        await db.SaveChangesAsync(ct);
    }

    private void DecryptTokens(SocialAccount account)
    {
        if (!string.IsNullOrEmpty(account.AccessTokenEncrypted))
        {
            try
            {
                account.AccessTokenEncrypted = encryption.Decrypt(account.AccessTokenEncrypted);
            }
            catch (Exception)
            {
                // If decryption fails, the token is likely corrupted or the key ring
                // was regenerated. Leave the ciphertext in place — the UI will show
                // "re-link required".
                account.TokenRefreshFailed = true;
            }
        }

        if (!string.IsNullOrEmpty(account.RefreshTokenEncrypted))
        {
            try
            {
                account.RefreshTokenEncrypted = encryption.Decrypt(account.RefreshTokenEncrypted);
            }
            catch (Exception)
            {
                account.RefreshTokenEncrypted = null;
                account.TokenRefreshFailed = true;
            }
        }
    }
}
