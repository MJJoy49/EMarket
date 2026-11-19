using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EMarket.Shared.Constants;

namespace EMarket.Web.Clients;

public sealed record Empty;

public sealed record FormFilePart(string Field, string FileName, byte[] Content);

public sealed record FileData(byte[] Content, string ContentType, string? FileName);

/// <summary>The answer of one API call. A call never throws for a normal API error; the page decides what to show.</summary>
public sealed class ApiResult<T>
{
    public bool Ok { get; init; }

    public int Status { get; init; }

    public T? Data { get; init; }

    public string Message { get; init; } = "";

    /// <summary>Field errors from the API's own validation, keyed by property name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

    public bool Unreachable => Status == 0;
}

/// <summary>Thrown for the API's maintenance answer so one middleware can show the Maintenance page (FR-21.9).</summary>
public sealed class MaintenanceException : Exception;

/// <summary>
/// The typed client for the API (AD-3). The UI never talks to the database; every call goes through here,
/// with the bearer token added by <see cref="TokenHandler"/>.
/// </summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<T>> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, path, null, null, ct);

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body = null, string? idempotencyKey = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Post, path, body ?? new { }, idempotencyKey, ct);

    public Task<ApiResult<T>> PutAsync<T>(string path, object body, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Put, path, body, null, ct);

    public Task<ApiResult<T>> DeleteAsync<T>(string path, object? body = null, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Delete, path, body, null, ct);

    public async Task<ApiResult<T>> PostFormAsync<T>(string path, IReadOnlyDictionary<string, string?> fields, IEnumerable<FormFilePart>? files = null, HttpMethod? method = null, CancellationToken ct = default)
    {
        var content = new MultipartFormDataContent();
        foreach (var (key, value) in fields.Where(f => f.Value is not null))
        {
            content.Add(new StringContent(value!), key);
        }

        foreach (var file in files ?? [])
        {
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, file.Field, file.FileName);
        }

        return await SendAsync<T>(method ?? HttpMethod.Post, path, content, null, ct);
    }

    public async Task<ApiResult<FileData>> DownloadAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                return await FailureAsync<FileData>(response, ct);
            }

            return new ApiResult<FileData>
            {
                Ok = true, Status = (int)response.StatusCode,
                Data = new FileData(await response.Content.ReadAsByteArrayAsync(ct), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                    response.Content.Headers.ContentDisposition?.FileName?.Trim('"')),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return Unreachable<FileData>();
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = body switch
                {
                    null => null,
                    HttpContent content => content,
                    _ => JsonContent.Create(body, options: Json),
                },
            };
            if (idempotencyKey is not null)
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);
            }

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return await FailureAsync<T>(response, ct);
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            return new ApiResult<T>
            {
                Ok = true, Status = (int)response.StatusCode,
                Data = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            return Unreachable<T>(); // FR-22.4: the page shows MSG31 and keeps what the user typed
        }
    }

    private static ApiResult<T> Unreachable<T>() => new() { Ok = false, Status = 0, Message = Messages.ServiceDown };

    private static async Task<ApiResult<T>> FailureAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        if (status == (int)HttpStatusCode.ServiceUnavailable)
        {
            throw new MaintenanceException();
        }

        string message = status switch
        {
            401 => Messages.NotAllowed,
            403 => Messages.NotAllowed,
            404 => Messages.NotFound,
            429 => Messages.TooManyRequests,
            _ => Messages.Invalid,
        };
        var errors = new Dictionary<string, string[]>();
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    message = m.GetString()!;
                }

                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in e.EnumerateObject())
                    {
                        errors[field.Name] = field.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // An empty or non-JSON body keeps the generic message for the status.
        }

        return new ApiResult<T> { Ok = false, Status = status, Message = message, Errors = errors };
    }
}
