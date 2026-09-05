using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Shortnr.Cli.Models;

namespace Shortnr.Cli.Services;

public interface IShortnrClient
{
    Task<Result<LinkResponse>> CreateLinkAsync(string url, string? slug, string? domain, CancellationToken ct);
    Task<Result<LinkListResponse>> ListLinksAsync(int? page, int? pageSize, CancellationToken ct);
    Task<Result<LinkResponse>> GetLinkAsync(string shortCode, CancellationToken ct);
    Task<Result<ClickListResponse>> GetClicksAsync(string shortCode, int? page, int? pageSize, CancellationToken ct);
    Task<Result> DeleteLinkAsync(string shortCode, CancellationToken ct);
}

public sealed class ShortnrClient : IShortnrClient
{
    private readonly HttpClient _http;

    public ShortnrClient(HttpClient http, string apiKey)
    {
        _http = http;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<Result<LinkResponse>> CreateLinkAsync(string url, string? slug, string? domain, CancellationToken ct)
    {
        var body = new CreateLinkRequest(url, slug, domain);
        var response = await _http.PostAsJsonAsync("/api/v1/links", body, ApiJsonContext.Default.CreateLinkRequest, ct);
        return await HandleResponseAsync<LinkResponse>(response, ApiJsonContext.Default.LinkResponse, ct);
    }

    public async Task<Result<LinkListResponse>> ListLinksAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["page"] = page?.ToString(),
            ["pageSize"] = pageSize?.ToString()
        });
        var response = await _http.GetAsync($"/api/v1/links{query}", ct);
        return await HandleResponseAsync<LinkListResponse>(response, ApiJsonContext.Default.LinkListResponse, ct);
    }

    public async Task<Result<LinkResponse>> GetLinkAsync(string shortCode, CancellationToken ct)
    {
        var response = await _http.GetAsync($"/api/v1/links/{shortCode}", ct);
        return await HandleResponseAsync<LinkResponse>(response, ApiJsonContext.Default.LinkResponse, ct);
    }

    public async Task<Result<ClickListResponse>> GetClicksAsync(string shortCode, int? page, int? pageSize, CancellationToken ct)
    {
        var query = BuildQuery(new Dictionary<string, string?>
        {
            ["page"] = page?.ToString(),
            ["pageSize"] = pageSize?.ToString()
        });
        var response = await _http.GetAsync($"/api/v1/links/{shortCode}/clicks{query}", ct);
        return await HandleResponseAsync<ClickListResponse>(response, ApiJsonContext.Default.ClickListResponse, ct);
    }

    public async Task<Result> DeleteLinkAsync(string shortCode, CancellationToken ct)
    {
        var response = await _http.DeleteAsync($"/api/v1/links/{shortCode}", ct);
        return await HandleResponseAsync(response, ct);
    }

    private static string BuildQuery(Dictionary<string, string?> parameters)
    {
        var parts = parameters
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");
        var query = string.Join("&", parts);
        return string.IsNullOrEmpty(query) ? "" : $"?{query}";
    }

    private async Task<Result<T>> HandleResponseAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync(typeInfo, ct);
            if (value is null)
                return Result<T>.Failure("Response body was empty");
            return Result<T>.Success(value);
        }

        return Result<T>.Failure(await ExtractErrorAsync(response, ct));
    }

    private async Task<Result> HandleResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return Result.Success();

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return Result.Success();

        return Result.Failure(await ExtractErrorAsync(response, ct));
    }

    private async Task<string> ExtractErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.ErrorResponse, ct);
            if (error?.Errors is { Count: > 0 })
            {
                var messages = error.Errors.SelectMany(kv => kv.Value).ToArray();
                return string.Join("; ", messages);
            }
            if (error?.Title is not null)
                return error.Title;
        }
        catch
        {
        }

        return $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase ?? "Request failed"}";
    }
}

public readonly struct Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}

[JsonSerializable(typeof(CreateLinkRequest))]
[JsonSerializable(typeof(LinkResponse))]
[JsonSerializable(typeof(LinkListResponse))]
[JsonSerializable(typeof(ClickListResponse))]
[JsonSerializable(typeof(ClickRow))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ApiJsonContext : JsonSerializerContext
{
}
