namespace AIExperience.Rag.Domain.Enums;

/// <summary>
/// Rôle joué par un texte lors de sa vectorisation. Certains modèles d'embedding (ex. nomic-embed-text)
/// sont asymétriques : ils ont été entraînés avec des préfixes de tâche différents côté corpus et côté requête.
/// Sans cette distinction, la séparation pertinent / non-pertinent se dégrade fortement (voir R-15).
/// </summary>
public enum EmbeddingTaskType
{
    /// <summary>Texte appartenant au corpus indexé (chunk de document, ingestion). Préfixe nomic : "search_document:".</summary>
    Document,

    /// <summary>Texte de requête utilisé pour interroger le corpus (question, doc hypothétique HyDE, reformulation). Préfixe nomic : "search_query:".</summary>
    Query
}
