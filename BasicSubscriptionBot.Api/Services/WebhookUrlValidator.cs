using System.Net;
using System.Net.Sockets;

namespace BasicSubscriptionBot.Api.Services;

// Server-side validation for buyer-supplied webhook URLs. Defends against SSRF
// where an attacker registers a subscription pointed at internal services
// reachable from inside the docker network (loopback, the cloud metadata
// endpoint, RFC1918 IPs of other containers/host services).
//
// Set ALLOW_INSECURE_WEBHOOKS=true in dev to permit http:// and unresolvable
// stub hosts (e.g. `buyer.test` in CI). Production must leave it unset (or
// set to false).
//
// 2026-05-24 SSRF-hardening pass (portfolio-wide):
//   - Special-use IPv4 ranges added: multicast 224.0.0.0/4, reserved/future
//     240.0.0.0/4 (covers broadcast 255.255.255.255), TEST-NET docs
//     192.0.2/24, 198.51.100/24, 203.0.113/24, benchmark 198.18/15.
//   - Special-use IPv6 ranges added: unspecified ::, documentation
//     2001:db8::/32, IPv4-translated 64:ff9b::/96.
//   - allowInsecure=true no longer skips IP-block checks wholesale; the bypass
//     now applies ONLY when DNS resolution fails or returns 0 records, so the
//     test escape hatch for unresolvable stub hosts is preserved while IP
//     literals + resolved IPs always get the block-list check.
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
                // Test escape hatch: unresolvable stub hosts (`buyer.test`)
                // are accepted under ALLOW_INSECURE_WEBHOOKS so CI doesn't
                // need DNS. IP literals + resolved IPs still hit IsBlocked
                // below; this bypass is scoped to unresolvable hosts only.
                if (allowInsecure) return new Result(true, null);
                return new Result(false, "webhookUrl host did not resolve");
            }
            if (addresses.Length == 0)
            {
                if (allowInsecure) return new Result(true, null);
                return new Result(false, "webhookUrl host did not resolve");
            }
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

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Any.Equals(addr))     { reason = "ipv6 unspecified";  return true; }
            var v6 = addr.GetAddressBytes();
            if (v6[0] == 0x20 && v6[1] == 0x01 && v6[2] == 0x0d && v6[3] == 0xb8)
                                                    { reason = "ipv6 documentation 2001:db8::/32"; return true; }
            if (v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b)
                                                    { reason = "ipv6 ipv4-translation 64:ff9b::/96"; return true; }
        }

        if (addr.AddressFamily == AddressFamily.InterNetwork ||
            (addr.IsIPv4MappedToIPv6))
        {
            var b = addr.MapToIPv4().GetAddressBytes();
            if (b[0] == 10)                             { reason = "rfc1918 10/8";       return true; }
            if (b[0] == 172 && (b[1] & 0xf0) == 16)     { reason = "rfc1918 172.16/12";  return true; }
            if (b[0] == 192 && b[1] == 168)             { reason = "rfc1918 192.168/16"; return true; }
            if (b[0] == 169 && b[1] == 254)             { reason = "link-local/metadata"; return true; }
            if (b[0] == 127)                            { reason = "loopback";           return true; }
            if (b[0] == 100 && (b[1] & 0xc0) == 64)     { reason = "cgnat 100.64/10";    return true; }
            if (b[0] == 0)                              { reason = "unspecified 0.0.0.0/8"; return true; }
            if ((b[0] & 0xf0) == 0xe0)                  { reason = "multicast 224/4";    return true; }
            if ((b[0] & 0xf0) == 0xf0)                  { reason = "reserved 240/4";     return true; }
            if (b[0] == 192 && b[1] == 0   && b[2] == 2)   { reason = "docs 192.0.2.0/24";   return true; }
            if (b[0] == 198 && b[1] == 51  && b[2] == 100) { reason = "docs 198.51.100.0/24"; return true; }
            if (b[0] == 203 && b[1] == 0   && b[2] == 113) { reason = "docs 203.0.113.0/24"; return true; }
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19)) { reason = "benchmark 198.18/15"; return true; }
        }

        reason = "";
        return false;
    }
}
