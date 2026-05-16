using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using BasicSubscriptionBot.Api.Models;
using Microsoft.Extensions.Configuration;

namespace BasicSubscriptionBot.Api.Services;

public class WebhookDeliveryService
{
    private readonly HttpClient _http;
    private readonly ILogger<WebhookDeliveryService> _logger;
    private readonly bool _allowInsecure;
    private const int BodyCapBytes = 1_048_576; // 1 MB
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public WebhookDeliveryService(HttpClient http, IConfiguration cfg, ILogger<WebhookDeliveryService> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = Timeout;
        _allowInsecure = string.Equals(cfg["ALLOW_INSECURE_WEBHOOKS"], "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeSignature(string secret, int tick, long timestamp, string body)
    {
        var canonical = $"{tick}.{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<DeliveryResult> DeliverAsync(Subscription sub, int tickNumber, string bodyJson, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(bodyJson) > BodyCapBytes)
            return new DeliveryResult(false, $"payload exceeds {BodyCapBytes} bytes");

        // Re-validate at delivery time. The subscribe endpoint validates on
        // insert, but a) older rows may pre-date the check, b) DNS for the
        // host may have rebound to a private IP since insert.
        var urlCheck = WebhookUrlValidator.Validate(sub.WebhookUrl, _allowInsecure);
        if (!urlCheck.Ok)
            return new DeliveryResult(false, $"webhookUrl rejected: {urlCheck.Error}");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sig = ComputeSignature(sub.WebhookSecret, tickNumber, ts, bodyJson);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, sub.WebhookUrl);
            req.Content = new StringContent(bodyJson, Encoding.UTF8);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Add("X-Subscription-Id", sub.Id);
            req.Headers.Add("X-Subscription-Tick", tickNumber.ToString());
            req.Headers.Add("X-Subscription-Timestamp", ts.ToString());
            req.Headers.Add("X-Subscription-Signature", sig);

            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode is >= 200 and < 300)
                return new DeliveryResult(true, null);

            return new DeliveryResult(false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DeliveryResult(false, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new DeliveryResult(false, $"http: {ex.Message}");
        }
    }
}

public record DeliveryResult(bool Ok, string? Error);
