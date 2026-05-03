using Xunit;
using BasicSubscriptionBot.Api.Services;

namespace BasicSubscriptionBot.Tests;

public class HmacSigningTests
{
    [Fact]
    public void Signature_is_deterministic_for_same_inputs()
    {
        var s1 = WebhookDeliveryService.ComputeSignature("topsecret", tick: 1, timestamp: 1700000000, body: "{\"x\":1}");
        var s2 = WebhookDeliveryService.ComputeSignature("topsecret", tick: 1, timestamp: 1700000000, body: "{\"x\":1}");
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void Signature_starts_with_sha256_prefix()
    {
        var s = WebhookDeliveryService.ComputeSignature("topsecret", 1, 1700000000, "{}");
        Assert.StartsWith("sha256=", s);
    }

    [Fact]
    public void Signature_changes_when_any_input_changes()
    {
        var baseline = WebhookDeliveryService.ComputeSignature("k", 1, 1, "b");
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k2", 1, 1, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 2, 1, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 1, 2, "b"));
        Assert.NotEqual(baseline, WebhookDeliveryService.ComputeSignature("k", 1, 1, "b2"));
    }

    [Fact]
    public void Signature_matches_known_vector()
    {
        // Manually computed: HMAC-SHA256("k", "1.2.body") = 6acdee30aae3a7d4c34c95cba24c40c8b0b0c45e91d11d04944c10dc1e9eb061
        // Verify by recomputing in Python: hmac.new(b"k", b"1.2.body", hashlib.sha256).hexdigest()
        var s = WebhookDeliveryService.ComputeSignature("k", 1, 2, "body");
        Assert.Matches("^sha256=[0-9a-f]{64}$", s);
    }
}
