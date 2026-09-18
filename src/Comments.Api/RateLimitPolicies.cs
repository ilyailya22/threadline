using System.Globalization;
using System.Threading.RateLimiting;
using Threadline.Comments.Api.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace Threadline.Comments.Api;

/// <summary>
/// Rate-limiting policy names and their configuration.
/// </summary>
/// <remarks>
/// <para>
/// Different endpoints deserve different budgets. Reading the board is cheap and should stay
/// generous even for an enthusiastic user; posting is expensive and is the thing a spam script
/// wants to do thousands of times; the preview endpoint is cheap per call but a client can fire it
/// on every keystroke; issuing CAPTCHAs is the one an attacker hits to harvest challenges.
/// </para>
/// <para>
/// The partition key is the client-id cookie when present and the remote IP otherwise. Using the
/// cookie first means several people behind one NAT are not throttled as if they were one person,
/// while the IP fallback means clearing cookies does not reset the budget for free.
/// </para>
/// </remarks>
public static class RateLimitPolicies
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Preview = "preview";
    public const string Captcha = "captcha";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Limits are configuration, not constants: a load test or an integration test that posts
        // thirty comments in a second is legitimate there and abuse in production. Defaults are the
        // production values.
        var section = configuration.GetSection("RateLimiting");
        var read = section.GetValue("ReadPerMinute", 300);
        var preview = section.GetValue("PreviewPerMinute", 60);
        var captcha = section.GetValue("CaptchaPerMinute", 30);
        var write = section.GetValue("WritePerMinute", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { title = "Too many requests", status = 429 },
                    cancellationToken);
            };

            options.AddPolicy(Read, context => Partition(context, permitLimit: read, window: 1));
            options.AddPolicy(Preview, context => Partition(context, permitLimit: preview, window: 1));
            options.AddPolicy(Captcha, context => Partition(context, permitLimit: captcha, window: 1));

            // Ten comments a minute is far more than a person types and far less than a script wants.
            options.AddPolicy(Write, context => Partition(context, permitLimit: write, window: 1));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, int permitLimit, int window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(window),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });

    private static string PartitionKey(HttpContext context) =>
        context.Request.Cookies.TryGetValue(HttpClientContext.ClientIdCookie, out var clientId)
        && !string.IsNullOrWhiteSpace(clientId)
            ? $"cid:{clientId}"
            : $"ip:{context.Connection.RemoteIpAddress}";
}
