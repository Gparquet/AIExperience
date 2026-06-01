using AIExperience.Rag.Application.Common.Behaviors;
using AIExperience.Rag.Application.Services;
using AIExperience.Rag.Application.Services.TextExtractor;
using AIExperience.Rag.Domain.Interfaces.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace AIExperience.Rag.Application
{
    /// <summary>
    /// Point d'entrée de la configuration du projet Application.
    /// </summary>
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            return services
                     .ConfigureMediatR()
                     .AddChunker()
                     .AddTextExtractors()
                     .AddIngestion();
        }

        public static IServiceCollection ConfigureMediatR(this IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);
                //cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                //cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
            });

            services.AddValidatorsFromAssembly(assembly);
            return services;
        }

        public static IServiceCollection AddChunker(this IServiceCollection services)
        {
            return services.AddScoped<ITextChunker, RecursiveChunker>();
        }

        public static IServiceCollection AddIngestion(this IServiceCollection services)
        {
            return services.AddScoped<IIngestionService, IngestionService>();
        }

        public static IServiceCollection AddTextExtractors(this IServiceCollection services)
        {
            services.AddSingleton<ITextExtractor, PdfTextExtractor>();
            services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
            services.AddSingleton<ICompositeTextExtractor, CompositeTextExtractor>();
            return services;
        }
    }
}
