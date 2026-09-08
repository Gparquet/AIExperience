using AIExperience.Rag.Application.Services;
using FluentAssertions;
using System.Reflection;

namespace AIExperience.Tests;

public sealed class RecursiveChunkerTests
{
    private const int MaxChunkSize = 800;
    private readonly RecursiveChunker _sut = new();

    #region Cas de base

    [Fact]
    public void Chunk_EmptyText_ReturnsEmptyList()
    {
        // Texte vide → aucun chunk
        var result = _sut.Chunk(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_WhitespaceOnly_ReturnsEmptyList()
    {
        var result = _sut.Chunk("   \n\n   \r\n\r\n  ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        // Texte nettement inférieur à MaxChunkSize → un seul chunk
        var text = "Un texte court qui ne dépasse pas la taille maximale du chunker.";
        var result = _sut.Chunk(text);
        result.Should().HaveCount(1);
        result[0].Content.Should().Contain("texte court");
    }

    #region découpage récursif

    [Fact]
    public void Chunk_SingleLargeParagraph_SplitsIntoMultipleChunks()
    {
        var sentences = Enumerable.Range(1, 40)
            .Select(i => $"Phrase numéro {i} dans ce long paragraphe de test.");
        var text = string.Join(" ", sentences); // ~1600+ chars, sans \n\n
        text.Length.Should().BeGreaterThan(MaxChunkSize, "le texte de test doit dépasser la taille max");

        var result = _sut.Chunk(text);

        result.Should().HaveCountGreaterThan(1,
            "un paragraphe de {0} chars doit être découpé en plusieurs chunks", text.Length);
    }

    [Fact]
    public void Chunk_MultipleSmallParagraphs_AccumulatesIntoChunks()
    {
        // Plusieurs petits paragraphes (chacun < MaxChunkSize) → doivent s'accumuler
        // dans un même chunk tant que la taille le permet.
        var paragraphs = Enumerable.Range(1, 3)
            .Select(i => $"Paragraphe court numéro {i}.");
        var text = string.Join("\n\n", paragraphs);
        var result = _sut.Chunk(text);

        // 3 petits paragraphes → 1 seul chunk (accumulation)
        result.Should().HaveCount(1, "3 petits paragraphes doivent tenir dans un seul chunk");
    }

    [Fact]
    public void Chunk_AllChunks_RespectMaxChunkSizeWithTolerance()
    {
        // Chaque chunk doit rester raisonnable — une tolérance est admise
        // si une phrase seule dépasse légèrement la limite.
        var sentences = Enumerable.Range(1, 60)
            .Select(i => $"Phrase numéro {i:D2} de ce document de test avec du contenu représentatif.");
        var text = string.Join(" ", sentences);

        var result = _sut.Chunk(text);

        foreach (var chunk in result)
            chunk.Content.Length.Should().BeLessThanOrEqualTo(
                MaxChunkSize + 200,
                $"le chunk '{chunk.Content[..Math.Min(50, chunk.Content.Length)]}...' dépasse la tolérance");
    }

    #endregion

    #endregion

    #region Overlap sur frontière de mot

    [Fact]
    public void Chunk_ConsecutiveChunks_OverlapDoesNotStartMidWord()
    {
        // Le début du chunk N+1 (la partie overlap) ne doit pas commencer
        // par un fragment de mot (pas d'espace en tête, pas de lettre isolée).
        var sentences = Enumerable.Range(1, 60)
            .Select(i => $"Phrase {i:D2} avec un contenu suffisant pour forcer plusieurs chunks.");
        var text = string.Join(" ", sentences);

        var result = _sut.Chunk(text);

        if (result.Count < 2) return; // pas d'overlap si un seul chunk

        for (int i = 1; i < result.Count; i++)
        {
            result[i].Content.Should()
                .NotStartWith(" ", $"le chunk {i} ne doit pas débuter par un espace (overlap mid-word)");
            result[i].Content.Should()
                .NotBeEmpty($"le chunk {i} ne doit pas être vide après l'overlap");
        }
    }

    #endregion

    #region Propagation de SectionTitle (Markdown)

    [Fact]
    public void Chunk_MarkdownH1_PropagatesSectionTitleToChunks()
    {
        // Un titre # doit être détecté et propagé aux chunks suivants
        const string text = "# Introduction\n\nCeci est le contenu de l'introduction de ce document.";
        var result = _sut.Chunk(text);

        result.Should().HaveCountGreaterThanOrEqualTo(1, "le contenu après le titre doit produire au moins un chunk");
        result.Should().AllSatisfy(c =>
            c.SectionTitle.Should().Be("Introduction",
                "le titre Markdown # doit être propagé comme SectionTitle"));
    }

    [Fact]
    public void Chunk_MarkdownH2_PropagatesSectionTitle()
    {
        const string text = "## Méthodologie\n\nDescription de la méthodologie utilisée dans cette étude.";
        var result = _sut.Chunk(text);

        result.Should().AllSatisfy(c =>
            c.SectionTitle.Should().Be("Méthodologie"));
    }

    [Fact]
    public void Chunk_MultipleSections_EachChunkHasCorrectSectionTitle()
    {
        // Plusieurs sections → chaque chunk porte la bonne section
        const string text =
            "# Section A\n\nContenu de la section A.\n\n" +
            "# Section B\n\nContenu de la section B.";

        var result = _sut.Chunk(text);

        result.Should().HaveCount(2, "deux sections → deux chunks");
        result[0].SectionTitle.Should().Be("Section A");
        result[1].SectionTitle.Should().Be("Section B");
    }

    [Fact]
    public void Chunk_NoMarkdownHeadings_SectionTitleIsNull()
    {
        // Sans titre Markdown, SectionTitle doit rester null
        const string text = "Contenu sans aucun titre. Juste du texte brut.";
        var result = _sut.Chunk(text);

        result.Should().HaveCount(1);
        result[0].SectionTitle.Should().BeNull();
    }

    [Fact]
    public void Chunk_HeadingOnlyDocument_ReturnsEmpty()
    {
        // Un document composé uniquement de titres sans contenu → aucun chunk de contenu
        const string text = "# Titre\n\n## Sous-titre";
        var result = _sut.Chunk(text);

        result.Should().BeEmpty("les titres seuls ne produisent pas de chunks de contenu");
    }

    #endregion

    #region ChunkPages préserve PageNumber

    [Fact]
    public void ChunkPages_EmptyPages_ReturnsEmptyList()
    {
        var result = _sut.ChunkPages([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChunkPages_SinglePage_ChunkCarriesPageNumber()
    {
        // Le numéro de page doit être propagé dans chaque chunk
        var pages = new List<(int, string)>
        {
            (1, "Contenu de la première page du document test.")
        };

        var result = _sut.ChunkPages(pages);

        result.Should().HaveCount(1);
        result[0].PageNumber.Should().Be(1, "le chunk doit conserver le numéro de page source");
    }

    [Fact]
    public void ChunkPages_MultiplePages_EachChunkHasCorrectPageNumber()
    {
        // Deux pages → les chunks de la page 1 ont PageNumber=1,
        //              ceux de la page 2 ont PageNumber=2.
        var pages = new List<(int, string)>
        {
            (1, "Contenu page un."),
            (2, "Contenu page deux."),
            (3, "Contenu page trois.")
        };

        var result = _sut.ChunkPages(pages);

        result.Should().HaveCount(3);
        result[0].PageNumber.Should().Be(1);
        result[1].PageNumber.Should().Be(2);
        result[2].PageNumber.Should().Be(3);
    }

    [Fact]
    public void ChunkPages_LargePageProducesMultipleChunks_AllWithSamePageNumber()
    {
        // Une page volumineuse découpée en N chunks → tous ont le même PageNumber
        var sentences = Enumerable.Range(1, 60)
            .Select(i => $"Phrase {i:D2} de la page volumineuse.");
        var largePage = string.Join(" ", sentences);

        var pages = new List<(int, string)> { (5, largePage) };
        var result = _sut.ChunkPages(pages);

        result.Should().HaveCountGreaterThan(1, "la page est trop volumineuse pour un seul chunk");
        result.Should().AllSatisfy(c =>
            c.PageNumber.Should().Be(5, "tous les chunks d'une même page partagent le même numéro"));
    }

    [Fact]
    public void ChunkPages_MarkdownTitleOnPage_PropagatesBothPageNumberAndSectionTitle()
    {
        // Combine pagination et détection de titre Markdown
        var pages = new List<(int, string)>
        {
            (2, "# Chapitre 1\n\nLe contenu du chapitre 1 se trouve ici.")
        };

        var result = _sut.ChunkPages(pages);

        result.Should().HaveCount(1);
        result[0].PageNumber.Should().Be(2);
        result[0].SectionTitle.Should().Be("Chapitre 1");
    }

    #endregion

    #region GetWordBoundaryOverlap — 3 branches

    /// <summary>
    /// Accède à la méthode privée statique <c>GetWordBoundaryOverlap</c> via réflexion.
    /// Cette approche est acceptable dans les tests d'algorithmes où la méthode privée
    /// représente une règle métier précise (overlap) qui mérite une vérification directe.
    /// </summary>
    private static string InvokeGetWordBoundaryOverlap(string text)
    {
        var method = typeof(RecursiveChunker).GetMethod(
            "GetWordBoundaryOverlap",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(RecursiveChunker), "GetWordBoundaryOverlap");

        return (string)method.Invoke(null, [text])!;
    }

    [Fact]
    public void GetWordBoundaryOverlap_TextShorterThanOverlapSize_ReturnsEntireText()
    {
        // Quand le texte fait ≤ 100 chars (OverlapSize), on retourne tout le texte.
        // Cas typique : buffer très court avant un flush — l'intégralité du buffer
        // devient l'overlap pour éviter la perte de contexte.
        const string shortText = "Phrase courte sans dépassement."; // 31 chars < 100

        var result = InvokeGetWordBoundaryOverlap(shortText);

        result.Should().Be(shortText, "un texte plus court que OverlapSize doit être retourné en entier");
    }

    [Fact]
    public void GetWordBoundaryOverlap_TextWithSentenceBoundary_StartsAfterLastSentence()
    {
        // Quand la fenêtre contient plusieurs ". ", l'overlap commence
        // après la DERNIÈRE occurrence (LastIndexOf), pas la première (IndexOf).
        // Cela garantit un overlap d'environ OverlapSize chars, non 2×OverlapSize.
        //
        // Fenêtre (derniers 200 chars) :
        //   "Première phrase dans la fenêtre. Deuxième phrase dans la fenêtre. " +
        //   "Troisième phrase dans la fenêtre."
        // LastIndexOf(". ") → pointe vers la fin de "Deuxième phrase..."
        // Résultat attendu : commence par "Troisième phrase..."
        var padding = new string('a', 200);   // 200 chars hors fenêtre
        var window =
            "Première phrase dans la fenêtre. " +   // ~33 chars
            "Deuxième phrase dans la fenêtre. " +    // ~33 chars
            "Troisième phrase dans la fenêtre.";     // ~33 chars
        var text = padding + window;

        var result = InvokeGetWordBoundaryOverlap(text);

        result.Should()
            .StartWith("Troisième",
                "l'overlap doit démarrer après la DERNIÈRE frontière '. ' dans la fenêtre (LastIndexOf)");
        result.Should()
            .NotContain("Première phrase",
                "la première frontière (IndexOf) ne doit pas être utilisée");
    }

    [Fact]
    public void GetWordBoundaryOverlap_TextWithoutSentenceBoundary_StartsAfterLastSpace()
    {
        // Quand la fenêtre ne contient pas ". ", le fallback utilise
        // le dernier espace (LastIndexOf(' ')). L'overlap commence au dernier mot complet,
        // garantissant qu'on ne coupe jamais en milieu de mot.
        var padding = new string('x', 200);   // 200 chars pour dépasser OverlapSize
        const string lastWord = "DernierMot";
        var text = padding + " " + lastWord;  // un espace avant le dernier "mot"

        var result = InvokeGetWordBoundaryOverlap(text);

        result.Should()
            .Be(lastWord,
                "sans '. ', l'overlap doit démarrer après le dernier espace (dernier mot complet)");
    }

    #endregion
}
