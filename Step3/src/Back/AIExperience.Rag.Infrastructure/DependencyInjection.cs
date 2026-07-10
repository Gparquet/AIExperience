using AIExperience.Rag.Application.Jobs;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Infrastructure.AI.Embedding;
using AIExperience.Rag.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AIExperience.Rag.Infrastructure.AI.Rag;
using AIExperience.Rag.Infrastructure.AI.Transcription;
using AIExperience.Rag.Infrastructure.AI.Video;
using AIExperience.Rag.Infrastructure.Options;
using AIExperience.Rag.Infrastructure.Persistence;
using AIExperience.Rag.Infrastructure.Persistence.Repositories;
using AIExperience.Rag.Infrastructure.VectorStore;
using Azure;
using Azure.AI.OpenAI;
using FFMpegCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using OpenAI;
using Polly;
using Polly.Retry;
using System.ClientModel;

namespace AIExperience.Rag.Infrastructure;

/// <summary>
/// Point d'entrée de la configuration du projet Infrastructure.
/// Enregistre EF Core, pgvector, Redis, Semantic Kernel, le pipeline RAG et OpenTelemetry.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Ajoute tous les services de la couche Infrastructure dans le conteneur DI.
    /// </summary>
    /// <param name="services">Collection de services.</param>
    /// <param name="configuration">Configuration de l'application.</param>
    /// <returns>La collection de services enrichie.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services
            .ConfigurationOptions(configuration)
            .AddPersistence(configuration)
            .AddVectorStore()
            .AddAIClients(configuration)
            .ConfigureAiService()
            .AddSemanticKernel(configuration)
            .AddRagPipeline()
            .AddVideoTranscription(configuration)
            .AddIngestionWorkerInfrastructure(configuration);

    }

    private static IServiceCollection ConfigurationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiProviderOptions>().Bind(configuration.GetSection("AI"));
        services.AddOptions<RagOptions>().Bind(configuration.GetSection("RagOptions"));
        return services;
    }

    /// <summary>
    /// Enregistre les briques dont a besoin le worker d'ingestion pour tourner (le worker
    /// lui-même, en couche Application, est ajouté par <c>AddApplication()</c>).
    /// </summary>
    private static IServiceCollection AddIngestionWorkerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IngestionOptions>().Bind(configuration.GetSection(IngestionOptions.SectionName));
        // Singleton : partagé par toutes les requêtes HTTP et par le worker lui-même, pour que
        // n'importe quel upload puisse le réveiller immédiatement.
        services.AddSingleton<IngestionSignal>();
        // TryAdd : ne s'applique que si le composition root (ex. l'application console) n'a pas
        // déjà enregistré sa propre implémentation — le Web.Api enregistre la sienne (SignalR)
        // avant d'appeler AddInfrastructure, qui doit donc rester prioritaire.
        services.TryAddSingleton<IIngestionNotifier, NullIngestionNotifier>();
        // Singleton : conserve en mémoire l'état de throttling par document. Résout AppDbContext
        // via un scope à chaque persistance (le worker est mono-instance, pas de contention).
        services.AddSingleton<IIngestionProgressReporter, IngestionProgressReporter>();
        return services;
    }

    /// <summary>
    /// Enregistre les services de transcription vidéo/audio 100% locaux (FFmpeg + Whisper).
    /// </summary>
    private static IServiceCollection AddVideoTranscription(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind des options Whisper depuis appsettings.json
        services.AddOptions<WhisperOptions>()
            .Bind(configuration.GetSection(WhisperOptions.SectionName));

        // Configurer le chemin du binaire FFmpeg (si spécifié dans la config)
        var ffmpegPath = configuration["FFmpeg:BinaryPath"];
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
        }

        // Enregistrement en Singleton : FFmpeg est stateless, Whisper charge le modèle une seule fois
        services.AddSingleton<IVideoProcessorService, FFmpegVideoProcessorService>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(configuration.GetConnectionString("Postgres"),
                npgsql => npgsql.UseVector()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IConversationRepository,  ConversationRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();

        return services;
    }

    private static IServiceCollection AddVectorStore(this IServiceCollection services)
    {
        services.AddScoped<IVectorStoreService, PgVectorStoreService>();
        return services;
    }

    private static IServiceCollection AddRagPipeline(this IServiceCollection services)
    {
        // Pipeline de résilience Polly pour les appels embeddings.
        // Retry exponentiel avec jitter sur erreurs HTTP transitoires (429, 503...).
        services.AddSingleton(_ => new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .Build());

        services.AddScoped<IEmbeddingService, OpenAIEmbeddingService>();
        return services;
    }

    private static IServiceCollection AddSemanticKernel(this IServiceCollection services, IConfiguration configuration)
    {
        var ai = configuration.GetSection("AI").Get<AiProviderOptions>() ?? new AiProviderOptions();

        services.AddTransient(sp =>
        {
            var builder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0070
            switch (ai.Provider)
            {
                case AiProvider.AzureOpenAI:
                    builder.AddAzureOpenAIChatCompletion(ai.ChatModel, ai.Endpoint, ai.ApiKey);
                    break;

                case AiProvider.GitHubModels:
                    builder.AddOpenAIChatCompletion(
                        ai.ChatModel,
                        apiKey: ai.ApiKey,
                        endpoint: new Uri(ai.Endpoint));
                    break;

                case AiProvider.OpenAI:
                default:
                    builder.AddOpenAIChatCompletion(ai.ChatModel, apiKey: ai.ApiKey, endpoint: new Uri(ai.Endpoint));
                    break;
            }
#pragma warning restore SKEXP0070

            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Ajoute <see cref="IChatClient"/> et <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> via Microsoft.Extensions.AI.
    /// Le provider est sélectionné dynamiquement selon <see cref="AiProviderOptions.Provider"/>.
    /// </summary>
    /// <param name="services">Collection de services.</param>
    /// <param name="configuration">Configuration de l'application.</param>
    /// <returns>La collection de services enrichie.</returns>
    private static IServiceCollection AddAIClients(this IServiceCollection services, IConfiguration configuration)
    {
        var ai = configuration.GetSection("AI").Get<AiProviderOptions>() ?? new AiProviderOptions();

#pragma warning disable SKEXP0070
        switch (ai.Provider)
        {
            case AiProvider.AzureOpenAI:
                {
                    var azureClient = new AzureOpenAIClient(
                        new Uri(ai.Endpoint),
                        new AzureKeyCredential(ai.ApiKey));

                    services.AddSingleton(
                        azureClient.GetChatClient(ai.ChatModel)
                            .AsIChatClient().AsBuilder().UseOpenTelemetry().Build());

                    services.AddSingleton(
                        azureClient.GetEmbeddingClient(ai.EmbeddingModel)
                            .AsIEmbeddingGenerator().AsBuilder().UseOpenTelemetry().Build());
                    break;
                }

            case AiProvider.GitHubModels:
            case AiProvider.OpenAI:
                {
                    var openAiClient = new OpenAIClient(
                        new ApiKeyCredential(ai.ApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(ai.Endpoint) });

                    services.AddSingleton(
                        openAiClient.GetChatClient(ai.ChatModel)
                            .AsIChatClient().AsBuilder().UseOpenTelemetry().Build());

                    services.AddSingleton(
                        openAiClient.GetEmbeddingClient(ai.EmbeddingModel)
                            .AsIEmbeddingGenerator().AsBuilder().UseOpenTelemetry().Build());
                    break;
                }

            default:
                {

                    break;
                }
        }
#pragma warning restore SKEXP0070

        return services;
    }

    private static IServiceCollection ConfigureAiService(this IServiceCollection services)
    {
        services.AddScoped<IAdaptiveQueryRouter, AdaptiveQueryRouter>();
        services.AddScoped<IContextCompressorService, ContextCompressorService>();

        // Stratégies de récupération avancées (Step 2 — Axe 1)
        services.AddScoped<IHydeService, HydeService>();
        services.AddScoped<IMultiQueryService, MultiQueryService>();
        services.AddScoped<IRerankerService, LlmRerankerService>();

        services.AddScoped<IRagPipelineService, RagPipelineService>();

        return services;
    }
}

