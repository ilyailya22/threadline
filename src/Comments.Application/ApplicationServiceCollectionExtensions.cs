using System.Reflection;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Comments.Sanitization;
using Threadline.Comments.Application.Common.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Threadline.Comments.Application;

/// <summary>Registers the application layer: MediatR pipeline, validators and domain services.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);

            // Order matters: log the outcome of validation, and never let an invalid request reach
            // a handler.
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddSingleton<ICommentTextSanitizer, CommentTextSanitizer>();
        services.AddSingleton<IFileTypeSniffer, FileTypeSniffer>();
        services.AddSingleton<ICaptchaCodeGenerator, CaptchaCodeGenerator>();
        services.AddScoped<ICaptchaService, CaptchaService>();
        services.AddScoped<IAttachmentIntakeService, AttachmentIntakeService>();

        return services;
    }
}
