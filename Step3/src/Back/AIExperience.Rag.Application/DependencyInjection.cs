using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Application.Services;
using AIExperience.Rag.Application.Services.LanguageDetection;
using AIExperience.Rag.Application.Services.TextExtractor;
using AIExperience.Rag.Domain.Interfaces.Services;
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
                     .AddCqrs(Assembly.GetExecutingAssembly())
                     .AddChunker()
                     .AddTextExtractors()
                     .AddLanguageDetection()
                     .AddIngestion()
                     .AddFileHashing()
                     .AddContentTypeResolution()
                     .AddIngestionWorker();
        }

        public static IServiceCollection AddChunker(this IServiceCollection services)
        {
            services.AddScoped<ITextChunker, RecursiveChunker>();
            // Singleton : TemporalChunker est sans état — peut être réutilisé en concurrence
            services.AddSingleton<ITemporalChunker, TemporalChunker>();
            return services;
        }

        public static IServiceCollection AddIngestion(this IServiceCollection services)
        {
            services.AddScoped<DocumentIngestionStatusUpdater>();
            return services.AddScoped<IIngestionService, IngestionService>();
        }

        public static IServiceCollection AddTextExtractors(this IServiceCollection services)
        {
            services.AddSingleton<ITextExtractor, PdfTextExtractor>();
            services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
            services.AddSingleton<ITextExtractor, PlainTextExtractor>();
            services.AddSingleton<ITextExtractor, JsonTextExtractor>();
            services.AddSingleton<ITextExtractor, DocxTextExtractor>();
            services.AddSingleton<ITextExtractor, PowerPointTextExtractor>();
            services.AddSingleton<ITextExtractor, ExcelTextExtractor>();
            services.AddSingleton<ICompositeTextExtractor, CompositeTextExtractor>();
            return services;
        }

        /// <summary>Enregistre le service de détection de langue (I-6).</summary>
        public static IServiceCollection AddLanguageDetection(this IServiceCollection services)
        {
            services.AddSingleton<ILanguageDetectionService, StopwordLanguageDetectionService>();
            return services;
        }

        /// <summary>Enregistre le service de calcul de hash de fichier (détection de doublon à l'upload).</summary>
        public static IServiceCollection AddFileHashing(this IServiceCollection services)
        {
            // Singleton : sans état, réutilisable en concurrence (même pattern que TemporalChunker).
            services.AddSingleton<IFileHashService, FileHashService>();
            return services;
        }

        /// <summary>Enregistre le résolveur de content-type, partagé par tous les points d'entrée qui reçoivent un fichier.</summary>
        public static IServiceCollection AddContentTypeResolution(this IServiceCollection services)
        {
            // Singleton : sans état, réutilisable en concurrence (même pattern que FileHashService).
            services.AddSingleton<IContentTypeResolver, ContentTypeResolver>();
            return services;
        }

        /// <summary>Enregistre le worker qui consomme la file d'ingestion en arrière-plan.</summary>
        public static IServiceCollection AddIngestionWorker(this IServiceCollection services)
        {
            services.AddHostedService<IngestionWorker>();
            return services;
        }
    }
}
