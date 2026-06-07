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
    /// Prompt pour la génération d'un document hypothétique (stratégie HyDE).
    /// Le LLM produit un passage factuel dont l'embedding est plus proche des vrais chunks que celui de la question brute.
    /// </summary>
    public const string Hyde = """
        Tu es un expert dans le domaine concerné par la question.
        Génère un court passage de texte (environ {length} mots) qui RÉPONDRAIT directement à la question suivante.
        Ce passage doit ressembler à un extrait de document factuel — pas une réponse directe adressée à quelqu'un, mais un texte source.
        Réponds UNIQUEMENT avec le passage, sans introduction ni explication.

        Question : {question}
        """;

    /// <summary>
    /// Prompt pour la génération de reformulations de question (stratégie RAG-Fusion / MultiQuery).
    /// Chaque reformulation couvre un angle sémantique différent pour maximiser la couverture de recherche.
    /// </summary>
    public const string MultiQuery = """
        Tu es un expert en recherche documentaire.
        Génère exactement {count} reformulations différentes de la question suivante.
        Chaque reformulation doit couvrir un angle ou une formulation différente pour maximiser la couverture de recherche.
        Réponds UNIQUEMENT avec les reformulations, une par ligne, sans numérotation ni explication.

        Question originale : {question}
        """;

    /// <summary>
    /// Prompt pour le reclassement d'un chunk par pertinence réelle (Reranker LLM).
    /// Le LLM retourne un entier 0-10 qui est ensuite normalisé en score 0.0-1.0.
    /// </summary>
    public const string Reranker = """
        Évalue la pertinence de l'extrait suivant pour répondre à la question donnée.
        Réponds UNIQUEMENT avec un nombre entier entre 0 et 10.
        (0 = totalement hors sujet, 10 = répond parfaitement à la question)
        Aucune explication, juste le chiffre.

        Question : {question}

        Extrait :
        {chunk}
        """;
}
