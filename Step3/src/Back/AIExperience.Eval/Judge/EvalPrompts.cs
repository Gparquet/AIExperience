namespace AIExperience.Eval.Judge;

/// <summary>Gabarits de prompt du juge de fidélité (LLM-as-judge), en français comme le reste du corpus.</summary>
public static class EvalPrompts
{
    public const string JudgeSystem =
        "Tu es un évaluateur strict de réponses générées par un système de questions-réponses. " +
        "Tu compares une réponse générée à une liste de points clés attendus et tu attribues un score " +
        "selon exactement 3 paliers : 0 (réponse incorrecte ou aucun point clé couvert), " +
        "0.5 (réponse partiellement correcte, certains points clés couverts), " +
        "1 (réponse correcte, tous les points clés essentiels couverts). " +
        "Réponds STRICTEMENT sous ce format, sans rien ajouter avant :\n" +
        "SCORE: <0, 0.5 ou 1>\n" +
        "JUSTIFICATION: <une phrase expliquant le score>";

    public const string JudgeUser =
        "Question : {question}\n\n" +
        "Réponse générée par le système :\n{answer}\n\n" +
        "Points clés attendus dans une bonne réponse :\n{keyPoints}";
}
