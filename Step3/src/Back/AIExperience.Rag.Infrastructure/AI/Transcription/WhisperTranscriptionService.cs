using AIExperience.Rag.Domain.Interfaces.Services.Video;
using AIExperience.Rag.Domain.Models.Video;
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Whisper.net;

namespace AIExperience.Rag.Infrastructure.AI.Transcription;

/// <summary>
/// Transcrit un fichier audio en texte via Whisper.net (modèle local, 100% offline).
/// Le modèle (<see cref="WhisperFactory"/>) est chargé une seule fois (Singleton).
/// Les processeurs sont mis en cache par langue pour supporter le multilingue.
/// Corrige le constat I-19 : l'ancienne implémentation construisait le processeur
/// avec la première langue reçue et l'utilisait pour toutes les langues suivantes.
/// </summary>
public sealed class WhisperTranscriptionService : ITranscriptionService, IDisposable
{
    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly WhisperOptions _options;

    /// <summary>
    /// La factory Whisper est initialisée une seule fois (chargement du modèle .bin lourd).
    /// Partagée entre tous les processeurs (une factory par instance de service = un modèle en mémoire).
    /// </summary>
    private WhisperFactory? _factory;

    /// <summary>
    /// Cache des processeurs par code langue (ex. "fr", "en").
    /// Utilise Lazy&lt;WhisperProcessor&gt; pour garantir qu'un seul processeur est construit
    /// par langue même en cas d'accès concurrent — évite la fuite de handle natif que
    /// produirait un ConcurrentDictionary&lt;,&gt;.GetOrAdd avec un Func (la factory peut être
    /// appelée plusieurs fois pour la même clé selon la documentation MSDN).
    /// I-19 : un seul processeur global était réutilisé quelle que soit la langue demandée.
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<WhisperProcessor>> _processors = new();

    /// <summary>Verrou d'initialisation de la factory (chargement du modèle) — thread-safe.</summary>
    private readonly SemaphoreSlim _factoryInitLock = new(1, 1);

    public WhisperTranscriptionService(
        IOptions<WhisperOptions> options,
        ILogger<WhisperTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        string language = "fr",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioPath))
            throw new FileNotFoundException("Fichier audio introuvable.", audioPath);

        // Récupère ou crée le processeur dédié à cette langue
        var processor = await GetOrCreateProcessorAsync(language);

        _logger.LogInformation("Début de la transcription : {File} (langue: {Lang})", audioPath, language);

        var segments = new List<TranscriptionSegment>();
        var fullText = new System.Text.StringBuilder();

        await using var fileStream = File.OpenRead(audioPath);

        await foreach (var segment in processor.ProcessAsync(fileStream, cancellationToken))
        {
            var ts = new TranscriptionSegment
            {
                Start = segment.Start,
                End   = segment.End,
                Text  = segment.Text.Trim()
            };

            segments.Add(ts);

            // Format avec timestamps : [00:00:05 → 00:00:12] Voici le contenu...
            fullText.AppendLine($"[{segment.Start:hh\\:mm\\:ss} → {segment.End:hh\\:mm\\:ss}] {segment.Text.Trim()}");
        }

        var duration = segments.LastOrDefault()?.End ?? TimeSpan.Zero;

        _logger.LogInformation(
            "Transcription terminée : {Count} segments, durée {Duration}, langue {Lang}",
            segments.Count, duration, language);

        return new TranscriptionResult
        {
            FullText  = fullText.ToString(),
            Segments  = segments.AsReadOnly(),
            Duration  = duration,
            Language  = language
        };
    }

    /// <summary>
    /// Retourne le processeur Whisper pour la langue demandée.
    /// La factory est initialisée une seule fois (verrou) ; les processeurs sont créés
    /// à la demande par langue et mis en cache dans <see cref="_processors"/>.
    /// </summary>
    private async Task<WhisperProcessor> GetOrCreateProcessorAsync(string language)
    {
        // Chemin rapide : Lazy déjà enregistré pour cette langue (sans lock d'init)
        if (_processors.TryGetValue(language, out var existing))
            return existing.Value;

        // Initialisation thread-safe de la factory si nécessaire
        await _factoryInitLock.WaitAsync();
        try
        {
            if (_factory is null)
            {
                _logger.LogInformation("Chargement du modèle Whisper : {Model}", _options.ModelPath);

                if (!File.Exists(_options.ModelPath))
                    throw new FileNotFoundException(
                        $"Modèle Whisper introuvable à '{_options.ModelPath}'. " +
                        "Téléchargez ggml-medium.bin depuis Hugging Face (ggerganov/whisper.cpp).",
                        _options.ModelPath);

                _factory = WhisperFactory.FromPath(_options.ModelPath);
                _logger.LogInformation("Modèle Whisper chargé avec succès.");
            }
        }
        finally
        {
            _factoryInitLock.Release();
        }

        // Enregistre un Lazy pour cette langue. GetOrAdd avec une valeur (pas une Func) est
        // atomique côté dictionnaire — deux threads peuvent créer deux Lazy différents pour la
        // même clé, mais un seul sera inséré. Le Lazy perdant est abandonné SANS avoir exécuté
        // sa factory (Lazy est lazy : la factory ne tourne qu'à l'accès à .Value), donc aucun
        // handle natif n'est jamais alloué et perdu.
        var lazy = _processors.GetOrAdd(language, lang =>
            new Lazy<WhisperProcessor>(() =>
            {
                _logger.LogDebug("Création du processeur Whisper pour la langue : {Lang}", lang);
                // WithTranslate() activerait la traduction vers l'anglais — intentionnellement absent.
                return _factory!.CreateBuilder()
                    .WithLanguage(lang)
                    .WithThreads(_options.Threads)
                    .Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Libère uniquement les Lazy dont la valeur a été effectivement créée
        // (IsValueCreated = false → aucun handle natif alloué → rien à libérer)
        foreach (var lazy in _processors.Values.Where(l => l.IsValueCreated))
            lazy.Value.Dispose();

        _factory?.Dispose();
        _factoryInitLock.Dispose();
    }
}
