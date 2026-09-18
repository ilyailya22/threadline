using Microsoft.Extensions.Configuration;

namespace Threadline.Comments.Infrastructure;

/// <summary>Names of the <c>ConnectionStrings</c> entries every host reads, in one place.</summary>
public static class ConnectionStrings
{
    public const string SqlServer = "SqlServer";

    public const string Redis = "Redis";

    public const string RabbitMq = "RabbitMq";

    /// <summary>Reads a connection string, failing at startup rather than on the first request that needs it.</summary>
    public static string GetRequired(IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration.GetConnectionString(name);

        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.")
            : value;
    }
}
