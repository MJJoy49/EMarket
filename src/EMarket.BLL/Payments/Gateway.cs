using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace EMarket.BLL.Payments;

public sealed record GatewaySession(string RedirectUrl);

/// <summary>What the gateway itself says about a transaction, read server to server (NFR-38).</summary>
public sealed record GatewayCheck(string TransactionId, string Status, decimal Amount, string Currency, string? BankTransactionId)
{
    public bool IsPaid => Status is "VALID" or "VALIDATED";
    public bool IsFailed => Status is "FAILED" or "CANCELLED" or "EXPIRED" or "UNATTEMPTED";
}

public sealed record GatewayRefundResult(bool Success, string? RefundReference, string? Error);

public sealed record GatewayCustomer(string Name, string Email, string Phone, string Address);

/// <summary>The payment gateway (SSLCommerz). Card numbers never reach EMarket (NFR-39).</summary>
public interface IPaymentGateway
{
    Task<GatewaySession> StartAsync(string transactionId, decimal amount, GatewayCustomer customer, string method, CancellationToken ct);

    /// <summary>Checks one transaction with the gateway; null when the gateway does not know it.</summary>
    Task<GatewayCheck?> CheckAsync(string transactionId, CancellationToken ct);

    /// <summary>The refund id is sent as the reference, so a retry can never refund twice (FR-13.10).</summary>
    Task<GatewayRefundResult> RefundAsync(string bankTransactionId, decimal amount, Guid refundId, CancellationToken ct);
}

public sealed class GatewayOptions
{
    public string Mode { get; set; } = "Fake"; // "Fake" for local work, "SslCommerz" with sandbox or live credentials
    public string BaseUrl { get; set; } = "https://sandbox.sslcommerz.com";
    public string StoreId { get; set; } = "";
    public string StorePassword { get; set; } = "";
    public string CallbackBaseUrl { get; set; } = "https://localhost:7001";

    public static GatewayOptions From(IConfiguration config) => config.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
}

/// <summary>
/// SSLCommerz client. Only the read calls are safe to retry; starting a session and refunding are sent once (AD-7).
/// Not yet tried against the live sandbox from this machine: the store id and password are needed first.
/// </summary>
public sealed class SslCommerzGateway(HttpClient http, GatewayOptions options) : IPaymentGateway
{
    public async Task<GatewaySession> StartAsync(string transactionId, decimal amount, GatewayCustomer customer, string method, CancellationToken ct)
    {
        var callback = options.CallbackBaseUrl.TrimEnd('/') + "/api/v1/payments/gateway";
        var form = new Dictionary<string, string>
        {
            ["store_id"] = options.StoreId,
            ["store_passwd"] = options.StorePassword,
            ["total_amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "BDT",
            ["tran_id"] = transactionId,
            ["success_url"] = $"{callback}/success",
            ["fail_url"] = $"{callback}/fail",
            ["cancel_url"] = $"{callback}/cancel",
            ["ipn_url"] = $"{callback}/ipn",
            ["cus_name"] = customer.Name,
            ["cus_email"] = customer.Email,
            ["cus_phone"] = customer.Phone,
            ["cus_add1"] = customer.Address,
            ["cus_city"] = "Dhaka",
            ["cus_country"] = "Bangladesh",
            ["shipping_method"] = "NO",
            ["product_name"] = "EMarket order",
            ["product_category"] = "general",
            ["product_profile"] = "general",
            ["multi_card_name"] = method switch { "bKash" => "bkash", "Nagad" => "nagad", _ => "" },
        };
        using var response = await http.PostAsync($"{options.BaseUrl}/gwprocess/v4/api.php", new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadFromJsonAsync<SessionResponse>(ct);
        return body is { Status: "SUCCESS", GatewayPageUrl: { Length: > 0 } url }
            ? new GatewaySession(url)
            : throw new InvalidOperationException($"Gateway refused the session: {body?.FailedReason}");
    }

    public async Task<GatewayCheck?> CheckAsync(string transactionId, CancellationToken ct)
    {
        var url = $"{options.BaseUrl}/validator/api/merchantTransIDvalidationAPI.php?tran_id={Uri.EscapeDataString(transactionId)}"
            + $"&store_id={Uri.EscapeDataString(options.StoreId)}&store_passwd={Uri.EscapeDataString(options.StorePassword)}&format=json";
        var body = await http.GetFromJsonAsync<QueryResponse>(url, ct);
        var latest = body?.Element?.OrderByDescending(e => e.Status is "VALID" or "VALIDATED").FirstOrDefault();
        return latest is null
            ? null
            : new GatewayCheck(latest.TranId ?? transactionId, latest.Status ?? "", decimal.Parse(latest.Amount ?? "0", CultureInfo.InvariantCulture),
                latest.Currency ?? "", latest.BankTranId);
    }

    public async Task<GatewayRefundResult> RefundAsync(string bankTransactionId, decimal amount, Guid refundId, CancellationToken ct)
    {
        var url = $"{options.BaseUrl}/validator/api/merchantTransIDvalidationAPI.php?bank_tran_id={Uri.EscapeDataString(bankTransactionId)}"
            + $"&refund_amount={amount.ToString("0.00", CultureInfo.InvariantCulture)}&refund_remarks=EMarket%20refund&refe_id={refundId:N}"
            + $"&store_id={Uri.EscapeDataString(options.StoreId)}&store_passwd={Uri.EscapeDataString(options.StorePassword)}&format=json";
        var body = await http.GetFromJsonAsync<RefundResponse>(url, ct);
        return body?.Status == "success"
            ? new GatewayRefundResult(true, body.RefundRefId, null)
            : new GatewayRefundResult(false, null, body?.ErrorReason ?? "Gateway refused the refund.");
    }

    private sealed record SessionResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("GatewayPageURL")] string? GatewayPageUrl,
        [property: JsonPropertyName("failedreason")] string? FailedReason);

    private sealed record QueryResponse([property: JsonPropertyName("element")] List<QueryElement>? Element);

    private sealed record QueryElement(
        [property: JsonPropertyName("tran_id")] string? TranId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("amount")] string? Amount,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("bank_tran_id")] string? BankTranId);

    private sealed record RefundResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("refund_ref_id")] string? RefundRefId,
        [property: JsonPropertyName("errorReason")] string? ErrorReason);
}

/// <summary>
/// A stand-in gateway for local work and demos without SSLCommerz credentials. Its page lives in the API
/// (/api/v1/payments/fake/{id}) and lets the tester choose Pay, Fail or Cancel. Never used when Mode is SslCommerz.
/// </summary>
public sealed class FakeGateway(GatewayOptions options) : IPaymentGateway
{
    private static readonly ConcurrentDictionary<string, GatewayCheck> Transactions = new();

    public Task<GatewaySession> StartAsync(string transactionId, decimal amount, GatewayCustomer customer, string method, CancellationToken ct)
    {
        Transactions.TryAdd(transactionId, new GatewayCheck(transactionId, "UNATTEMPTED", amount, "BDT", null));
        return Task.FromResult(new GatewaySession($"{options.CallbackBaseUrl.TrimEnd('/')}/api/v1/payments/fake/{transactionId}"));
    }

    public Task<GatewayCheck?> CheckAsync(string transactionId, CancellationToken ct) =>
        Task.FromResult(Transactions.TryGetValue(transactionId, out var check) ? check : null);

    public Task<GatewayRefundResult> RefundAsync(string bankTransactionId, decimal amount, Guid refundId, CancellationToken ct) =>
        Task.FromResult(new GatewayRefundResult(true, $"FAKE-RF-{refundId:N}", null));

    /// <summary>What the fake page does when the tester presses a button.</summary>
    public static void Decide(string transactionId, string status)
    {
        if (Transactions.TryGetValue(transactionId, out var check))
        {
            Transactions[transactionId] = check with { Status = status, BankTransactionId = status == "VALID" ? $"FAKE-{transactionId}" : null };
        }
    }
}
