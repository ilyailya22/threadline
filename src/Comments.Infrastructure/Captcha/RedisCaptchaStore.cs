using Threadline.Comments.Application.Captcha;
using StackExchange.Redis;

namespace Threadline.Comments.Infrastructure.Captcha;

/// <summary>
/// Stores pending CAPTCHA answers in Redis.
/// </summary>
/// <remarks>
/// <para>
/// Redis rather than a server-side session because the API runs as N stateless replicas behind a
/// load balancer: the request that issues the challenge and the request that answers it routinely
/// land on different instances.
/// </para>
/// <para>
/// <c>GETDEL</c> makes reading the answer and invalidating it a single atomic operation. A
/// read-then-delete pair would leave a window in which two concurrent submissions both read the
/// same live challenge — which is exactly the race a flooding script would exploit.
/// </para>
/// </remarks>
public sealed class RedisCaptchaStore(IConnectionMultiplexer redis) : ICaptchaStore
{
    private const string KeyPrefix = "captcha:";

    public async Task StoreAsync(
        Guid challengeId,
        string answer,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await redis.GetDatabase().StringSetAsync(Key(challengeId), answer, ttl);
    }

    public async Task<string?> ConsumeAsync(Guid challengeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(challengeId));

        return value.IsNullOrEmpty ? null : value.ToString();
    }

    private static RedisKey Key(Guid challengeId) => $"{KeyPrefix}{challengeId:N}";
}
