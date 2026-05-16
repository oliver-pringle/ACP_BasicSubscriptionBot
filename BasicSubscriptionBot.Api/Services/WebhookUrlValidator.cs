using System.Net;
using System.Net.Sockets;

namespace BasicSubscriptionBot.Api.Services;

// Server-side validation for buyer-supplied webhook URLs. Defends against SSRF
// where an attacker registers a subscription pointed at internal services
// reachable from inside the docker network (loopback, the cloud metadata
// endpoint, RFC1918 IPs of other containers/host services).
//
// Set ALLOW_INSECURE_WEBHOOKS=true in dev to permit http:// and loopback —
// the boilerplate ships with that flag on for local testing. Production
// must leave it unset (or set to false).
public static class WebhookUrlValidator
{
    public readonly record struct Result(bool Ok, string? Error);

    public static Result Validate(string? url, bool allowInsecure)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new Result(false, "webhookUrl required");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new Result(false, "webhookUrl must be an absolute URI");

        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp))
            return new Result(false, "webhookUrl must use https://");

        if (uri.IsDefaultPort == false && (uri.Port < 1 || uri.Port > 65535))
            return new Result(false, "webhookUrl port out of range");

        // ALLOW_INSECURE_WEBHOOKS=true is a dev/test escape hatch that also
        // bypasses DNS resolution — tests use stub hosts like `buyer.test`
        // that legitimately don't resolve, and we don't want CI to need DNS.
        if (allowInsecure) return new Result(true, null);

        // Resolve the host and check every resolved address. A hostname can
        // round-robin or rebind to an internal address; checking only the
        // literal would let an attacker DNS-rebind past us.
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = Dns.GetHostAddresses(uri.Host);
            }
            catch (SocketException)
            {
                return new Result(false, "webhookUrl host did not resolve");
            }
            if (addresses.Length == 0)
                return new Result(false, "webhookUrl host did not resolve");
        }

        foreach (var addr in addresses)
        {
            if (IsBlocked(addr, out var reason))
                return new Result(false, $"webhookUrl resolves to {reason} ({addr})");
        }

        return new Result(true, null);
    }

    private static bool IsBlocked(IPAddress addr, out string reason)
    {
        if (IPAddress.IsLoopback(addr))           { reason = "loopback";          return true; }
        if (addr.IsIPv6LinkLocal)                  { reason = "ipv6 link-local";   return true; }
        if (addr.IsIPv6SiteLocal)                  { reason = "ipv6 site-local";   return true; }
        if (addr.IsIPv6UniqueLocal)                { reason = "ipv6 unique-local"; return true; }
        if (addr.IsIPv6Multicast)                  { reason = "ipv6 multicast";    return true; }
        if (IPAddress.IsLoopback(addr.MapToIPv4())){ reason = "loopback";          return true; }

        if (addr.AddressFamily == AddressFamily.InterNetwork ||
            (addr.IsIPv4MappedToIPv6))
        {
            var b = addr.MapToIPv4().GetAddressBytes();
            // 10.0.0.0/8
            if (b[0] == 10)                             { reason = "rfc1918 10/8";       return true; }
            // 172.16.0.0/12
            if (b[0] == 172 && (b[1] & 0xf0) == 16)     { reason = "rfc1918 172.16/12";  return true; }
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168)             { reason = "rfc1918 192.168/16"; return true; }
            // 169.254.0.0/16 (link-local + AWS/Azure/GCP metadata 169.254.169.254)
            if (b[0] == 169 && b[1] == 254)             { reason = "link-local/metadata"; return true; }
            // 127.0.0.0/8 (already covered by IsLoopback but be explicit)
            if (b[0] == 127)                            { reason = "loopback";           return true; }
            // 100.64.0.0/10 carrier-grade NAT
            if (b[0] == 100 && (b[1] & 0xc0) == 64)     { reason = "cgnat 100.64/10";    return true; }
            // 0.0.0.0/8
            if (b[0] == 0)                              { reason = "unspecified";        return true; }
        }

        reason = "";
        return false;
    }
}
