namespace AIExperience.Rag.Domain.Enums
{
    /// <summary>
    /// Représente le rôle de l'auteur d'un message dans une conversation.
    /// </summary>
    public enum MessageRole
    {
        /// <summary>Message système définissant le comportement du modèle (instructions, contexte).</summary>
        System,

        /// <summary>Message envoyé par l'utilisateur humain.</summary>
        User,

        /// <summary>Message généré par le modèle IA (réponse RAG).</summary>
        Assistant
    }
}
