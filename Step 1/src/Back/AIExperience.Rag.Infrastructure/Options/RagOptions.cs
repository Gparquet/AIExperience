namespace AIExperience.Rag.Infrastructure.Options;

public sealed class RagOptions
{
    /// <summary>Stratégie RAG utilisée par défaut. Par défaut : "Adaptive".</summary>
    public string DefaultStrategy { get; set; } = "Adaptive";

    /// <summary>Options de la technique HyDE.</summary>
    public HydeOptions HyDE { get; set; } = new();

    /// <summary>Options de la génération multi-requêtes (RAG-Fusion).</summary>
    public MultiQueryOptions MultiQuery { get; set; } = new();

    /// <summary>Options de la phase de récupération vectorielle.</summary>
    public RetrievalOptions Retrieval { get; set; } = new();

    /// <summary>Options du reranker cross-encoder.</summary>
    public RerankerOptions Reranker { get; set; } = new();

    /// <summary>Options de la compression contextuelle.</summary>
    public ContextCompressionOptions ContextCompression { get; set; } = new();

    /// <summary>Options du cache Redis des réponses.</summary>
    public CacheOptions Cache { get; set; } = new();
}

/// <summary>Options pour la technique HyDE (Hypothetical Document Embeddings).</summary>
public sealed class HydeOptions
{
    /// <summary>Active ou désactive HyDE. Par défaut : true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Longueur approximative du document hypothétique généré (en tokens). Par défaut : 200.</summary>
    public int HypotheticalDocLength { get; set; } = 200;
}

/// <summary>Options pour la génération de variantes de requêtes (RAG-Fusion).</summary>
public sealed class MultiQueryOptions
{
    /// <summary>Active ou désactive la génération multi-requêtes. Par défaut : true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nombre de variantes de la question à générer. Par défaut : 3.</summary>
    public int VariantCount { get; set; } = 3;
}

/// <summary>Options pour la récupération vectorielle dans pgvector.</summary>
public sealed class RetrievalOptions
{
    /// <summary>Nombre maximum de chunks récupérés avant reranking. Par défaut : 20.</summary>
    public int TopK { get; set; } = 20;

    /// <summary>Score de similarité cosinus minimum pour retenir un chunk. Par défaut : 0.3.</summary>
    public double ScoreThreshold { get; set; } = 0.3;
}

/// <summary>Options pour le reranker cross-encoder.</summary>
public sealed class RerankerOptions
{
    /// <summary>Active ou désactive le reranking. Par défaut : true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nombre de chunks conservés après reranking. Par défaut : 5.</summary>
    public int TopKAfterRerank { get; set; } = 5;
}

/// <summary>Options pour la compression contextuelle des chunks.</summary>
public sealed class ContextCompressionOptions
{
    /// <summary>Active ou désactive la compression contextuelle. Par défaut : true.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Options pour le cache Redis des réponses RAG.</summary>
public sealed class CacheOptions
{
    /// <summary>Active ou désactive le cache. Par défaut : true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Durée de vie des entrées en cache (en minutes). Par défaut : 60.</summary>
    public int TtlMinutes { get; set; } = 60;
}

