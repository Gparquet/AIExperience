namespace AIExperience.Rag.Domain.Models.Video
{
    /// <summary>
    /// Un segment de transcription = un morceau de parole avec son timing.
    /// Analogie : comme le sous-titre d'un film — un texte avec son début et sa fin.
    /// </summary>
    public sealed record TranscriptionSegment
    {
        public required TimeSpan Start { get; init; }
        public required TimeSpan End { get; init; }
        public required string Text { get; init; }
    }
}
