namespace AIExperience.Rag.Infrastructure.Options;

/// <summary>
/// Fournisseur IA sélectionné pour le chat et les embeddings.
/// </summary>
public enum AiProvider
{
    /// <summary>Azure OpenAI Service (GPT-4o, text-embedding-3-large).</summary>
    AzureOpenAI,

    /// <summary>GitHub Models via le endpoint d'inférence Azure AI (accès gratuit avec PAT GitHub).</summary>
    GitHubModels,

    /// <summary>Ollama local (llama3.2, nomic-embed-text, etc.) — aucun compte requis.</summary>
    Ollama,

    OpenAI
}

/// <summary>
/// Options de configuration multi-provider IA.
/// Mappées depuis la section "AI" de appsettings.json.
/// </summary>
public sealed class AiProviderOptions
{
    /// <summary>Fournisseur IA actif. Par défaut : <see cref="AiProvider.OpenAI"/>.</summary>
    public AiProvider Provider { get; set; } = AiProvider.OpenAI;

    /// <summary>
    /// URL du endpoint.
    /// <list type="bullet">
    ///   <item>AzureOpenAI : https://&lt;resource&gt;.openai.azure.com/</item>
    ///   <item>GitHubModels : https://models.inference.ai.azure.com</item>
    ///   <item>Ollama : http://localhost:11434</item>
    /// </list>
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Clé d'API ou PAT GitHub. Non requis pour Ollama local.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Nom du modèle de chat (ex: gpt-4o, llama3.2, mistral).</summary>
    public string ChatModel { get; set; } = "llama3.2";

    /// <summary>Nom du modèle d'embedding (ex: text-embedding-3-large, nomic-embed-text).</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Nombre de dimensions du vecteur d'embedding. 768 pour Ollama, 3072 pour AzureOpenAI/GitHubModels.</summary>
    public int EmbeddingDimensions { get; set; } = 768;
}
