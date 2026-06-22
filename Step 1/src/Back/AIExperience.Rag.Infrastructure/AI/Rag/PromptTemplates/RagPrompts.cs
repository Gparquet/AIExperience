namespace AIExperience.Rag.Infrastructure.AI.Rag.PromptTemplates;

public static class RagPrompts
{
    /// <summary>
    /// Prompt pour la compression contextuelle d'un chunk.
    /// </summary>
    public const string Compression = """
        Extrait uniquement les phrases de ce passage qui sont directement pertinentes pour répondre à la question.
        Si aucune phrase n'est pertinente, réponds "AUCUN".
        Ne paraphrase pas, extrais le texte original tel quel.

        Question : {question}

        Passage :
        {chunk}
        """;

    /// <summary>
    /// Prompt système pour la génération de la réponse RAG finale.
    /// </summary>
    public const string RagSystem = """
        Tu es un assistant expert dans le voyage

        Règles strictes :
        - Réponds UNIQUEMENT à partir des extraits de documents fournis dans le contexte.
        - Si la réponse ne figure pas dans les extraits, dis-le clairement.
        - Cite toujours tes sources avec le format [SOURCE: NomDocument, p.X].
        - Sois précis, structuré et professionnel.
        - Réponds en français.
        """;

    /// <summary>
    /// Template du prompt utilisateur avec injection du contexte RAG.
    /// </summary>
    public const string RagUser = """
        Contexte documentaire :
        {context}

        ---
        Question : {question}
        """;

    /// <summary>
    /// Prompt système pour le mode LLM direct (sans contexte RAG).
    /// Le LLM répond uniquement depuis ses connaissances générales.
    /// </summary>
    public const string DirectLlmSystem = """
        Tu es un assistant expert dans le voyage.
        Réponds à la question en utilisant tes connaissances générales.
        Sois précis, structuré et professionnel.
        Réponds en français.
        """;

    /// <summary>
    /// Template du prompt utilisateur pour le mode LLM direct.
    /// </summary>
    public const string DirectLlmUser = "Question : {question}";
}
