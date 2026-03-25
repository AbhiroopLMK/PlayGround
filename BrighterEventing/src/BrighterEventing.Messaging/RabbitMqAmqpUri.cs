using System;

namespace BrighterEventing.Messaging;

/// <summary>
/// Builds an AMQP URI from <c>RabbitMqSettings</c> (host, port, username, password) or falls back to <c>RabbitMQ:AmqpUri</c>.
/// </summary>
public static class RabbitMqAmqpUri
{
    /// <summary>Configuration section name for component-style Rabbit settings.</summary>
    public const string SettingsSection = "RabbitMqSettings";

    /// <summary>
    /// When <paramref name="username"/> and <paramref name="password"/> and <paramref name="hostName"/> are set,
    /// builds <c>amqp(s)://user:pass@host:port/</c> (credentials are encoded). Otherwise uses <paramref name="legacyAmqpUri"/>,
    /// or a full URI if <paramref name="hostName"/> is already an amqp(s) URL, or localhost guest.
    /// </summary>
    public static string Resolve(
        string? legacyAmqpUri,
        string? hostName,
        string? port,
        string? username,
        string? password,
        string defaultLocal = "amqp://guest:guest@localhost:5672")
    {
        var hasComponentCreds = !string.IsNullOrWhiteSpace(username)
            && !string.IsNullOrWhiteSpace(password)
            && !string.IsNullOrWhiteSpace(hostName);

        if (hasComponentCreds)
        {
            var (host, scheme, portIfInUri) = ParseHostAndScheme(hostName!.Trim());
            var portNum = ParsePort(port, portIfInUri ?? (string.Equals(scheme, "amqps", StringComparison.OrdinalIgnoreCase) ? 5671 : 5672));

            var ub = new UriBuilder
            {
                Scheme = scheme,
                Host = host,
                Port = portNum,
                UserName = username,
                Password = password,
                Path = "/"
            };
            return ub.Uri.AbsoluteUri;
        }

        if (!string.IsNullOrWhiteSpace(legacyAmqpUri))
            return legacyAmqpUri!;

        if (!string.IsNullOrWhiteSpace(hostName)
            && Uri.TryCreate(hostName, UriKind.Absolute, out var existing)
            && (existing.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase)
                || existing.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
            return hostName!;

        return defaultLocal;
    }

    private static (string Host, string Scheme, int? PortFromUri) ParseHostAndScheme(string hostName)
    {
        if (Uri.TryCreate(hostName, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
        {
            var p = uri.Port > 0 ? uri.Port : (int?)null;
            return (uri.Host, uri.Scheme.ToLowerInvariant(), p);
        }

        return (hostName, "amqp", null);
    }

    private static int ParsePort(string? portConfig, int defaultPort)
    {
        if (!string.IsNullOrWhiteSpace(portConfig) && int.TryParse(portConfig, out var p) && p > 0)
            return p;
        return defaultPort;
    }
}
