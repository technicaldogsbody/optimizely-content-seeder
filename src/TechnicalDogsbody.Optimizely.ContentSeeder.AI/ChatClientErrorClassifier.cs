using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.AI;

/// <summary>
/// Decides whether an exception from an <c>IChatClient</c> means every further request will fail too
/// (bad credentials, no credit, unknown model, rejected request, unreachable service) rather than just
/// this one (rate limit, overloaded service, dropped connection).
/// <para>
/// Provider SDKs each throw their own exception types, so this reads the HTTP status the way they
/// expose it — <c>HttpRequestException.StatusCode</c>, or a public <c>StatusCode</c> / <c>Status</c>
/// property (Anthropic, OpenAI, Azure and others) — without referencing any of them.
/// </para>
/// </summary>
public static class ChatClientErrorClassifier
{
    private static readonly ConcurrentDictionary<Type, Func<Exception, int?>> _readers = new();

    /// <summary>
    /// True when the exception means every request will fail: a bad key, an unknown model or an invalid request. Recognised by its status code, so no vendor SDK is referenced.
    /// </summary>
    public static bool IsConfigurationError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException { StatusCode: null, InnerException: SocketException })
                return true;

            if (GetHttpStatus(e) is { } status)
            {
                return status switch
                {
                    400 or 401 or 402 or 403 or 404 => true,
                    // OpenAI reports an empty balance as a 429 (insufficient_quota), unlike a real rate limit.
                    429 => IsOutOfCredit(e.Message),
                    _ => false,
                };
            }
        }

        return false;
    }

    private static bool IsOutOfCredit(string message)
        => message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase)
           || message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
           || message.Contains("billing", StringComparison.OrdinalIgnoreCase);

    private static int? GetHttpStatus(Exception exception) => _readers.GetOrAdd(exception.GetType(), BuildReader)(exception);

    private static Func<Exception, int?> BuildReader(Type type)
    {
        if (typeof(HttpRequestException).IsAssignableFrom(type))
            return e => (int?)((HttpRequestException)e).StatusCode;

        foreach (var name in new[] { "StatusCode", "Status" })
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || property.GetIndexParameters().Length > 0)
                continue;

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType == typeof(HttpStatusCode) || propertyType == typeof(int))
                return e => property.GetValue(e) is { } value ? Convert.ToInt32(value) : null;
        }

        return static _ => null;
    }
}
