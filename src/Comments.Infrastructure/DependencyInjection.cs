using System.Reflection;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Caching;
using Threadline.Comments.Infrastructure.Captcha;
using Threadline.Comments.Infrastructure.Common;
using Threadline.Comments.Infrastructure.Media;
using Threadline.Comments.Infrastructure.Messaging;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Persistence.Repositories;
using Threadline.Comments.Infrastructure.Search;
using Threadline.Comments.Infrastructure.Storage;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Threadline.Comments.Infrastructure;

/// <summary>Composition root for everything that talks to the outside world.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptionsSection<PrivacyOptions>(configuration, PrivacyOptions.SectionName)
            .AddOptionsSection<StorageOptions>(configuration, StorageOptions.SectionName)
            .AddOptionsSection<ElasticsearchOptions>(configuration, ElasticsearchOptions.SectionName)
            .AddOptionsSection<OutboxOptions>(configuration, OutboxOptions.SectionName);

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IIpAddressHasher, IpAddressHasher>();
        services.AddSingleton<IAttachmentUrlBuilder, AttachmentUrlBuilder>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
        services.AddSingleton<ICaptchaImageRenderer, SkiaCaptchaRenderer>();

        services.AddPersistence(configuration);
        services.AddCache(configuration);
        services.AddSearch();
        services.AddBlobStorage();

        return services;
    }

    /// <summary>
    /// SQL Server + repositories.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(Assembly.GetExecutingAssembly().GetName().Name);

                // Azure SQL closes idle connections and throttles; without this, every transient
                // blip becomes a 500 for a user instead of a retried command.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                sql.CommandTimeout(30);
            });

            // Reads are projected into DTOs and never mutated, so tracking would be pure overhead;
            // the write path opts back in explicitly where it needs it.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<ICommentReadRepository, CommentReadRepository>();

        return services;
    }

    /// <summary>Redis as the L2 of a two-level cache, plus the CAPTCHA store.</summary>
    public static IServiceCollection AddCache(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false; // start even if Redis is briefly unavailable
            options.ConnectRetry = 5;

            return ConnectionMultiplexer.Connect(options);
        });

        // Registered as the IDistributedCache that HybridCache uses as its L2. It gets the
        // connection string rather than the multiplexer above: resolving that here would mean
        // building a second service provider, and with it a second singleton multiplexer.
        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);

#pragma warning disable EXTEXP0018 // HybridCache is still marked experimental in this package version.
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 4 * 1024 * 1024;
            options.MaximumKeyLength = 256;
        });
#pragma warning restore EXTEXP0018

        services.AddSingleton<ICommentCache, HybridCommentCache>();
        services.AddSingleton<ICaptchaStore, RedisCaptchaStore>();

        return services;
    }

    public static IServiceCollection AddSearch(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;

            var settings = new ElasticsearchClientSettings(new Uri(options.Url))
                .RequestTimeout(options.RequestTimeout)
                .DefaultIndex(options.Alias);

            if (!string.IsNullOrWhiteSpace(options.Username))
            {
                settings = settings.Authentication(
                    new BasicAuthentication(options.Username, options.Password ?? string.Empty));
            }

            return new ElasticsearchClient(settings);
        });

        services.AddSingleton<ICommentSearchIndex, ElasticsearchCommentIndex>();
        services.AddScoped<CommentSearchProjector>();

        return services;
    }

    public static IServiceCollection AddBlobStorage(this IServiceCollection services)
    {
        services.AddSingleton<BlobFileStorage>();
        services.AddSingleton<IFileStorage>(provider => provider.GetRequiredService<BlobFileStorage>());

        return services;
    }

    /// <summary>
    /// RabbitMQ through MassTransit. Consumers are supplied by the host, so the API can publish
    /// without hosting any consumer and the worker can host all of them.
    /// </summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var rabbit = configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured.");

        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();

            configureConsumers?.Invoke(bus);

            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(rabbit));

                // Retry transient faults in-process a few times; anything that survives that goes to
                // _error so it can be inspected instead of spinning forever.
                cfg.UseMessageRetry(retry => retry.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromSeconds(30),
                    intervalDelta: TimeSpan.FromSeconds(2)));

                // A broker outage must not turn into a thundering herd the moment it comes back.
                cfg.UseKillSwitch(k => k.SetActivationThreshold(10).SetTripThreshold(0.5).SetRestartTimeout(m: 1));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    /// <summary>Registers the background service that drains the outbox. Hosted by the worker only.</summary>
    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services)
    {
        services.AddHostedService<OutboxPublisher>();

        return services;
    }

    private static IServiceCollection AddOptionsSection<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()

            // Validating on start turns a misconfigured deployment into a container that refuses to
            // boot, instead of one that serves errors at 3 a.m. on the first request that needs it.
            .ValidateOnStart();

        return services;
    }
}
