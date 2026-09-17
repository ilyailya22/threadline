using Microsoft.Extensions.DependencyInjection;

namespace Threadline.Comments.Application.Common;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wraps an already-registered service in a decorator.
    /// </summary>
    /// <remarks>
    /// Thirty lines instead of taking a dependency on Scrutor, which would be a whole package for
    /// this one call. The original registration is replaced by one that builds the inner instance
    /// from its own descriptor and hands it to the decorator's constructor, so the inner type keeps
    /// its lifetime and its own dependencies are still resolved normally.
    /// </remarks>
    public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException(
                $"Cannot decorate {typeof(TService).Name}: it has not been registered.");

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(TService),
            provider => ActivatorUtilities.CreateInstance<TDecorator>(provider, CreateInner(provider, descriptor)),
            descriptor.Lifetime));

        return services;
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(provider);
        }

        return ActivatorUtilities.CreateInstance(
            provider,
            descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                $"Cannot decorate {descriptor.ServiceType.Name}: the registration has no implementation type."));
    }
}
