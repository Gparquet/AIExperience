# Lot 2 — Extracteurs multi-format + détection de langue — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ajouter 5 nouveaux extracteurs de texte (DOCX, XLSX, CSV, PPTX, TXT/MD, JSON) et détecter automatiquement la langue d'un document pour indexer correctement `content_tsv` (dictionnaire Postgres adapté au lieu de `'french'` figé).

**Architecture:** Chaque nouvel extracteur implémente `ITextExtractor` (et `IPageAwareTextExtractor` pour XLSX/PPTX) et s'enregistre dans `CompositeTextExtractor` via DI — aucune interface existante à modifier. La langue est détectée par un heuristique de mots vides (fr/en/es/de/it, zéro dépendance), propagée jusqu'à `content_tsv` via un paramètre explicite dans le SQL d'upsert (la colonne n'est plus `GENERATED`, car `to_tsvector(regconfig, text)` est `STABLE` et interdit dans une colonne générée avec langue variable par ligne).

**Tech Stack:** .NET 10, `DocumentFormat.OpenXml` 3.5.1 (DOCX/PPTX), `ClosedXML` 0.105.0 (XLSX), `System.Text.Json` (JSON natif), xUnit + FluentAssertions.

## Global Constraints

- Toutes les réponses/commentaires de code en français (CLAUDE.md racine + Step 3/src/Back).
- Chaque classe/méthode/bloc non trivial reçoit un commentaire XML `///` en C#.
- Setters privés + factory statique `Create(...)` pour toute entité Domain (déjà respecté par `Document`/`DocumentChunk`).
- Options Pattern pour toute configuration liée à `appsettings.json` (déjà en place pour `RagOptions`).
- Aucune régression : la suite de tests existante (87 tests avant ce lot) doit rester verte après chaque tâche.
- I-4 (OCR) est explicitement hors périmètre de ce plan.
- Référence spec : [`docs/superpowers/specs/2026-07-03-lot2-multi-format-langue-design.md`](../specs/2026-07-03-lot2-multi-format-langue-design.md)

---

### Task 1: PlainTextExtractor (.txt / .md)

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PlainTextExtractor.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/PlainTextExtractorTests.cs`

**Interfaces:**
- Consumes: `ITextExtractor` (Domain, existant, inchangé)
- Produces: `PlainTextExtractor : ITextExtractor` — `CanHandle(string)`, `ExtractTextAsync(string, CancellationToken)`

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/PlainTextExtractorTests.cs
using AIExperience.Rag.Application.Services.TextExtractor;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PlainTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie la prise en charge des fichiers .txt et .md, lus tels quels (le chunker
/// gère déjà nativement les titres Markdown).
/// </summary>
public sealed class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _sut = new();

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("readme.md")]
    [InlineData("RAPPORT.TXT")]
    public void CanHandle_TxtOrMdFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("rapport.docx")]
    public void CanHandle_OtherFile_ReturnsFalse(string path)
    {
        _sut.CanHandle(path).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_TxtFile_ReturnsRawContent()
    {
        var path = CreateTempFile(".txt", "Contenu texte brut.\nDeuxième ligne.");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Contenu texte brut.");
        result.Should().Contain("Deuxième ligne.");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_MarkdownFile_PreservesHeadingSyntax()
    {
        // Le "#" n'est PAS transformé ici : c'est RecursiveChunker qui le détecte en aval.
        var path = CreateTempFile(".md", "# Titre principal\n\nParagraphe de contenu.");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("# Titre principal");
        result.Should().Contain("Paragraphe de contenu.");

        File.Delete(path);
    }

    private static string CreateTempFile(string extension, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PlainTextExtractorTests"`
Expected: FAIL (compile error — `PlainTextExtractor` n'existe pas encore)

- [ ] **Step 3: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PlainTextExtractor.cs
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les fichiers texte brut (.txt) et Markdown (.md).
/// Lecture native, sans transformation : le Markdown est laissé tel quel car
/// <c>RecursiveChunker</c> détecte déjà les titres "#" à "####" pour peupler SectionTitle.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
        => File.ReadAllTextAsync(filePath, cancellationToken);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PlainTextExtractorTests"`
Expected: PASS (5/5)

- [ ] **Step 5: Register in DI**

In `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`, modify `AddTextExtractors`:

```csharp
public static IServiceCollection AddTextExtractors(this IServiceCollection services)
{
    services.AddSingleton<ITextExtractor, PdfTextExtractor>();
    services.AddSingleton<ITextExtractor, HtmlTextExtractor>();
    services.AddSingleton<ITextExtractor, PlainTextExtractor>();
    // VideoTextExtractor dépend de IVideoProcessorService + ITranscriptionService (Infrastructure Singletons)
    services.AddSingleton<ITextExtractor, VideoTextExtractor>();
    services.AddSingleton<ICompositeTextExtractor, CompositeTextExtractor>();
    return services;
}
```

- [ ] **Step 6: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 7: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PlainTextExtractor.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/PlainTextExtractorTests.cs"
git commit -m "feat(rag): extracteur texte brut/Markdown (.txt/.md) - I-2"
```

---

### Task 2: JsonTextExtractor

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/JsonTextExtractor.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/JsonTextExtractorTests.cs`

**Interfaces:**
- Consumes: `ITextExtractor`
- Produces: `JsonTextExtractor : ITextExtractor`

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/JsonTextExtractorTests.cs
using AIExperience.Rag.Application.Services.TextExtractor;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="JsonTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'aplatissement récursif JSON en lignes "clé.sous_clé: valeur".
/// </summary>
public sealed class JsonTextExtractorTests
{
    private readonly JsonTextExtractor _sut = new();

    [Theory]
    [InlineData("data.json")]
    [InlineData("CONFIG.JSON")]
    public void CanHandle_JsonFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonJsonFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_FlatObject_ReturnsKeyValueLines()
    {
        var path = CreateTempJson("""{ "titre": "Rapport annuel", "annee": 2026 }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("titre: Rapport annuel");
        result.Should().Contain("annee: 2026");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_NestedObject_FlattensWithDottedPath()
    {
        var path = CreateTempJson("""{ "auteur": { "nom": "Dupont", "role": "Directeur" } }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("auteur.nom: Dupont");
        result.Should().Contain("auteur.role: Directeur");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_ArrayOfObjects_FlattensWithIndexedPath()
    {
        var path = CreateTempJson("""{ "items": [ { "nom": "Stylo" }, { "nom": "Cahier" } ] }""");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("items[0].nom: Stylo");
        result.Should().Contain("items[1].nom: Cahier");

        File.Delete(path);
    }

    private static string CreateTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~JsonTextExtractorTests"`
Expected: FAIL (compile error)

- [ ] **Step 3: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/JsonTextExtractor.cs
using System.Text;
using System.Text.Json;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les fichiers JSON : aplatit récursivement la structure en lignes
/// "clé.sous_clé: valeur" (objets imbriqués → chemin en pointillés, tableaux → index entre crochets).
/// </summary>
public sealed class JsonTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        using var document = JsonDocument.Parse(json);

        var sb = new StringBuilder();
        Flatten(document.RootElement, string.Empty, sb);
        return sb.ToString().Trim();
    }

    /// <summary>Aplatit récursivement un <see cref="JsonElement"/> en lignes "chemin: valeur".</summary>
    private static void Flatten(JsonElement element, string path, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                    Flatten(property.Value, childPath, sb);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{path}[{index}]", sb);
                    index++;
                }
                break;

            default:
                // String/Number/True/False/Null : ToString() retourne la représentation textuelle adaptée.
                sb.AppendLine($"{path}: {element}");
                break;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~JsonTextExtractorTests"`
Expected: PASS (6/6)

- [ ] **Step 5: Register in DI**

In `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`, `AddTextExtractors` :

```csharp
services.AddSingleton<ITextExtractor, PlainTextExtractor>();
services.AddSingleton<ITextExtractor, JsonTextExtractor>();
```

- [ ] **Step 6: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 7: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/JsonTextExtractor.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/JsonTextExtractorTests.cs"
git commit -m "feat(rag): extracteur JSON avec aplatissement recursif - I-2"
```

---

### Task 3: DocxTextExtractor

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/AIExperience.Rag.Application.csproj` (ajout `DocumentFormat.OpenXml`)
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/DocxTextExtractor.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/DocxTextExtractorTests.cs`

**Interfaces:**
- Consumes: `ITextExtractor`
- Produces: `DocxTextExtractor : ITextExtractor` (pas de pagination — Word ne paginé pas nativement)

- [ ] **Step 1: Add the NuGet package**

Run:
```bash
cd "Step 3/src/Back/AIExperience.Rag.Application"
dotnet add package DocumentFormat.OpenXml
```
Expected: `PackageReference pour le package 'DocumentFormat.OpenXml' version '3.5.1' ajouté`

- [ ] **Step 2: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/DocxTextExtractorTests.cs
using AIExperience.Rag.Application.Services.TextExtractor;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="DocxTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction du texte et la propagation des titres Word (Heading1-4)
/// en syntaxe Markdown "#", reprise nativement par RecursiveChunker.
/// </summary>
public sealed class DocxTextExtractorTests
{
    private readonly DocxTextExtractor _sut = new();

    [Theory]
    [InlineData("rapport.docx")]
    [InlineData("RAPPORT.DOCX")]
    public void CanHandle_DocxFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonDocxFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTextAsync_PlainParagraphs_ReturnsText()
    {
        var path = CreateTempDocx(
            (null, "Premier paragraphe."),
            (null, "Deuxième paragraphe."));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Premier paragraphe.");
        result.Should().Contain("Deuxième paragraphe.");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_Heading1_PrefixedWithSingleHash()
    {
        var path = CreateTempDocx(("Heading1", "Introduction"));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("# Introduction");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_Heading3_PrefixedWithTripleHash()
    {
        var path = CreateTempDocx(("Heading3", "Sous-section"));

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("### Sous-section");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyDocument_ReturnsEmptyOrWhitespace()
    {
        var path = CreateTempDocx();

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Trim().Should().BeEmpty();

        File.Delete(path);
    }

    /// <summary>Construit un .docx minimal en mémoire pour les tests, sans fixture binaire.</summary>
    private static string CreateTempDocx(params (string? Style, string Text)[] paragraphs)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            foreach (var (style, text) in paragraphs)
            {
                var paragraph = new Paragraph();
                if (style is not null)
                    paragraph.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = style });
                paragraph.Append(new Run(new Text(text)));
                body.Append(paragraph);
            }

            mainPart.Document.Save();
        }
        return path;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~DocxTextExtractorTests"`
Expected: FAIL (compile error — `DocxTextExtractor` n'existe pas encore)

- [ ] **Step 4: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/DocxTextExtractor.cs
using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les documents Word (.docx) via DocumentFormat.OpenXml.
/// Les styles Word Heading1-Heading4 sont convertis en préfixes Markdown "#"-"####" :
/// RecursiveChunker détecte déjà cette syntaxe pour peupler TextChunk.SectionTitle,
/// évitant toute modification du chunker.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException($"Document Word sans corps de texte : {filePath}");

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var headingLevel = GetHeadingLevel(paragraph);
            if (headingLevel > 0)
                sb.AppendLine($"{new string('#', headingLevel)} {text}");
            else
                sb.AppendLine(text);
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>Retourne 1 à 4 pour les styles Heading1-Heading4, 0 pour un paragraphe normal.</summary>
    private static int GetHeadingLevel(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId switch
        {
            "Heading1" => 1,
            "Heading2" => 2,
            "Heading3" => 3,
            "Heading4" => 4,
            _ => 0
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~DocxTextExtractorTests"`
Expected: PASS (6/6)

- [ ] **Step 6: Register in DI**

```csharp
services.AddSingleton<ITextExtractor, JsonTextExtractor>();
services.AddSingleton<ITextExtractor, DocxTextExtractor>();
```

- [ ] **Step 7: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 8: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/AIExperience.Rag.Application.csproj" "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/DocxTextExtractor.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/DocxTextExtractorTests.cs"
git commit -m "feat(rag): extracteur DOCX avec propagation des titres Heading1-4 - I-2"
```

---

### Task 4: PowerPointTextExtractor

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PowerPointTextExtractor.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/PowerPointTextExtractorTests.cs`

**Interfaces:**
- Consumes: `IPageAwareTextExtractor` (Domain, existant — étend `ITextExtractor` avec `ExtractPagesAsync`)
- Produces: `PowerPointTextExtractor : IPageAwareTextExtractor` (1 page = 1 slide)

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/PowerPointTextExtractorTests.cs
using AIExperience.Rag.Application.Services.TextExtractor;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PowerPointTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction texte par slide (pagination) via DocumentFormat.OpenXml.
/// </summary>
public sealed class PowerPointTextExtractorTests
{
    private readonly PowerPointTextExtractor _sut = new();

    [Theory]
    [InlineData("presentation.pptx")]
    [InlineData("PRESENTATION.PPTX")]
    public void CanHandle_PptxFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_NonPptxFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractPagesAsync_MultipleSlides_ReturnsOnePagePerSlide()
    {
        var path = CreateTempPptx("Bienvenue", "Plan de la présentation");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(2);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("Bienvenue");
        pages[1].PageNumber.Should().Be(2);
        pages[1].Text.Should().Contain("Plan de la présentation");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_SlideText_PrefixedWithSlideHeading()
    {
        var path = CreateTempPptx("Contenu de test");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages[0].Text.Should().Contain("# Slide 1");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractTextAsync_MultipleSlides_ConcatenatesAllSlides()
    {
        var path = CreateTempPptx("Première slide", "Deuxième slide");

        var result = await _sut.ExtractTextAsync(path, CancellationToken.None);

        result.Should().Contain("Première slide");
        result.Should().Contain("Deuxième slide");

        File.Delete(path);
    }

    /// <summary>Construit un .pptx minimal en mémoire : une slide par texte fourni, sans master/layout/theme
    /// (non nécessaires pour un round-trip via DocumentFormat.OpenJml, seulement pour ouvrir dans PowerPoint).</summary>
    private static string CreateTempPptx(params string[] slideTexts)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pptx");
        using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            var presentationPart = doc.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();
            var slideIdList = new P.SlideIdList();
            presentationPart.Presentation.Append(slideIdList);

            uint slideId = 256;
            foreach (var text in slideTexts)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new P.Slide(
                    new P.CommonSlideData(
                        new P.ShapeTree(
                            new P.Shape(
                                new P.NonVisualShapeProperties(
                                    new P.NonVisualDrawingProperties { Id = 2, Name = "TextBox" },
                                    new P.NonVisualShapeDrawingProperties(),
                                    new P.ApplicationNonVisualDrawingProperties()),
                                new P.ShapeProperties(),
                                new P.TextBody(
                                    new D.BodyProperties(),
                                    new D.ListStyle(),
                                    new D.Paragraph(new D.Run(new D.Text(text))))))));
                slideIdList.Append(new P.SlideId { Id = slideId++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.Save();
        }
        return path;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PowerPointTextExtractorTests"`
Expected: FAIL (compile error). Si le test échoue à l'exécution (pas à la compilation) à cause d'un ordre d'éléments OpenXML incorrect dans le helper `CreateTempPptx`, ajuster l'ordre des constructeurs selon le message d'exception `InvalidOperationException`/`OpenXmlPackageException` obtenu — c'est un fixture de test, pas du code de production.

- [ ] **Step 3: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PowerPointTextExtractor.cs
using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les présentations PowerPoint (.pptx) via DocumentFormat.OpenXml.
/// Implémente <see cref="IPageAwareTextExtractor"/> : chaque slide devient une "page",
/// préfixée "# Slide N" pour que RecursiveChunker la reconnaisse comme une section.
/// </summary>
public sealed class PowerPointTextExtractor : IPageAwareTextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var pages = await ExtractPagesAsync(filePath, cancellationToken);
        var sb = new StringBuilder();
        foreach (var (_, text) in pages)
        {
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var result = new List<(int, string)>();
        using var doc = PresentationDocument.Open(filePath, false);

        var presentationPart = doc.PresentationPart
            ?? throw new InvalidOperationException($"Présentation PowerPoint sans PresentationPart : {filePath}");
        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<P.SlideId>().ToList() ?? [];

        for (int i = 0; i < slideIds.Count; i++)
        {
            var relId = slideIds[i].RelationshipId!.Value!;
            var slidePart = (SlidePart)presentationPart.GetPartById(relId);
            var slideText = string.Join(" ", slidePart.Slide.Descendants<D.Text>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(slideText)) continue;

            result.Add((i + 1, $"# Slide {i + 1}\n\n{slideText}"));
        }

        return Task.FromResult<IReadOnlyList<(int, string)>>(result);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PowerPointTextExtractorTests"`
Expected: PASS (5/5)

- [ ] **Step 5: Register in DI**

```csharp
services.AddSingleton<ITextExtractor, DocxTextExtractor>();
services.AddSingleton<ITextExtractor, PowerPointTextExtractor>();
```

- [ ] **Step 6: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 7: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/PowerPointTextExtractor.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/PowerPointTextExtractorTests.cs"
git commit -m "feat(rag): extracteur PPTX pagine par slide - I-2"
```

---

### Task 5: ExcelTextExtractor (.xlsx + .csv)

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/AIExperience.Rag.Application.csproj` (ajout `ClosedXML`)
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/ExcelTextExtractor.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/ExcelTextExtractorTests.cs`

**Interfaces:**
- Consumes: `IPageAwareTextExtractor`
- Produces: `ExcelTextExtractor : IPageAwareTextExtractor` (1 page = 1 feuille pour .xlsx ; .csv = 1 page unique)

- [ ] **Step 1: Add the NuGet package**

Run:
```bash
cd "Step 3/src/Back/AIExperience.Rag.Application"
dotnet add package ClosedXML
```
Expected: `PackageReference pour le package 'ClosedXML' version '0.105.0' ajouté`

- [ ] **Step 2: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/ExcelTextExtractorTests.cs
using AIExperience.Rag.Application.Services.TextExtractor;
using ClosedXML.Excel;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="ExcelTextExtractor"/> (constat I-2 du plan Lot 2).
/// Vérifie l'extraction .xlsx (pagination par feuille, format "colonne: valeur")
/// et .csv (parseur RFC 4180 minimal, sans dépendance CsvHelper).
/// </summary>
public sealed class ExcelTextExtractorTests
{
    private readonly ExcelTextExtractor _sut = new();

    [Theory]
    [InlineData("ventes.xlsx")]
    [InlineData("export.csv")]
    [InlineData("VENTES.XLSX")]
    public void CanHandle_XlsxOrCsvFile_ReturnsTrue(string path)
    {
        _sut.CanHandle(path).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_OtherFile_ReturnsFalse()
    {
        _sut.CanHandle("document.pdf").Should().BeFalse();
    }

    [Fact]
    public async Task ExtractPagesAsync_SingleSheet_ReturnsOnePage()
    {
        var path = CreateTempXlsx(wb =>
        {
            var ws = wb.Worksheets.Add("Ventes");
            ws.Cell(1, 1).Value = "Produit";
            ws.Cell(1, 2).Value = "Prix";
            ws.Cell(2, 1).Value = "Stylo";
            ws.Cell(2, 2).Value = "1.5";
        });

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(1);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("# Ventes");
        pages[0].Text.Should().Contain("Produit: Stylo");
        pages[0].Text.Should().Contain("Prix: 1.5");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_MultipleSheets_ReturnsOnePagePerSheet()
    {
        var path = CreateTempXlsx(wb =>
        {
            var ws1 = wb.Worksheets.Add("Feuille1");
            ws1.Cell(1, 1).Value = "Colonne";
            ws1.Cell(2, 1).Value = "A";

            var ws2 = wb.Worksheets.Add("Feuille2");
            ws2.Cell(1, 1).Value = "Colonne";
            ws2.Cell(2, 1).Value = "B";
        });

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(2);
        pages[0].Text.Should().Contain("Feuille1");
        pages[1].Text.Should().Contain("Feuille2");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_Csv_ReturnsSinglePageWithKeyValueLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        File.WriteAllText(path, "Produit,Prix\nStylo,1.5\nCahier,2.0");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages.Should().HaveCount(1);
        pages[0].Text.Should().Contain("Produit: Stylo");
        pages[0].Text.Should().Contain("Prix: 1.5");
        pages[0].Text.Should().Contain("Produit: Cahier");

        File.Delete(path);
    }

    [Fact]
    public async Task ExtractPagesAsync_CsvWithQuotedCommaField_ParsesCorrectly()
    {
        // Champ entre guillemets contenant une virgule : ne doit pas être coupé en deux colonnes.
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.csv");
        File.WriteAllText(path, "Nom,Adresse\nDupont,\"12 rue de la Paix, Paris\"");

        var pages = await _sut.ExtractPagesAsync(path, CancellationToken.None);

        pages[0].Text.Should().Contain("Adresse: 12 rue de la Paix, Paris");

        File.Delete(path);
    }

    private static string CreateTempXlsx(Action<XLWorkbook> configure)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.xlsx");
        using var workbook = new XLWorkbook();
        configure(workbook);
        workbook.SaveAs(path);
        return path;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~ExcelTextExtractorTests"`
Expected: FAIL (compile error)

- [ ] **Step 4: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/ExcelTextExtractor.cs
using System.Text;
using AIExperience.Rag.Domain.Interfaces.Services;
using ClosedXML.Excel;

namespace AIExperience.Rag.Application.Services.TextExtractor;

/// <summary>
/// Extracteur pour les classeurs Excel (.xlsx, via ClosedXML) et les fichiers CSV
/// (parseur RFC 4180 minimal fait maison, pour éviter une dépendance CsvHelper supplémentaire).
/// Implémente <see cref="IPageAwareTextExtractor"/> : chaque feuille .xlsx devient une "page" ;
/// un .csv est traité comme une page unique.
/// </summary>
public sealed class ExcelTextExtractor : IPageAwareTextExtractor
{
    /// <inheritdoc/>
    public bool CanHandle(string filePath) =>
        filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var pages = await ExtractPagesAsync(filePath, cancellationToken);
        var sb = new StringBuilder();
        foreach (var (_, text) in pages)
        {
            sb.AppendLine(text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            var csvText = ExtractCsv(filePath);
            IReadOnlyList<(int, string)> csvResult = string.IsNullOrWhiteSpace(csvText)
                ? []
                : [(1, csvText)];
            return Task.FromResult(csvResult);
        }

        var result = new List<(int, string)>();
        using var workbook = new XLWorkbook(filePath);

        int pageNumber = 1;
        foreach (var worksheet in workbook.Worksheets)
        {
            var sheetText = ExtractWorksheet(worksheet);
            if (!string.IsNullOrWhiteSpace(sheetText))
                result.Add((pageNumber, sheetText));
            pageNumber++;
        }

        return Task.FromResult<IReadOnlyList<(int, string)>>(result);
    }

    /// <summary>Convertit une feuille Excel en texte "# NomFeuille" + lignes "colonne: valeur".</summary>
    private static string ExtractWorksheet(IXLWorksheet worksheet)
    {
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null) return string.Empty;

        var rows = usedRange.RowsUsed().ToList();
        if (rows.Count == 0) return string.Empty;

        int columnCount = usedRange.ColumnCount();
        var headers = Enumerable.Range(1, columnCount)
            .Select(col => rows[0].Cell(col).GetString())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {worksheet.Name}");
        sb.AppendLine();

        foreach (var row in rows.Skip(1))
        {
            var line = new StringBuilder();
            for (int col = 1; col <= columnCount; col++)
            {
                var value = row.Cell(col).GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (line.Length > 0) line.Append(", ");
                line.Append($"{headers[col - 1]}: {value}");
            }
            if (line.Length > 0) sb.AppendLine(line.ToString());
        }

        return sb.ToString().Trim();
    }

    /// <summary>Convertit un fichier CSV en texte "colonne: valeur" (1ère ligne = en-têtes).</summary>
    private static string ExtractCsv(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return string.Empty;

        var headers = ParseCsvLine(lines[0]);
        var sb = new StringBuilder();

        foreach (var rawLine in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var values = ParseCsvLine(rawLine);

            var line = new StringBuilder();
            for (int i = 0; i < headers.Count && i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i])) continue;
                if (line.Length > 0) line.Append(", ");
                line.Append($"{headers[i]}: {values[i]}");
            }
            if (line.Length > 0) sb.AppendLine(line.ToString());
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Parseur CSV minimal conforme RFC 4180 : gère les champs entre guillemets contenant
    /// des virgules ou des guillemets échappés (""), sans dépendance externe (I-2 : évite CsvHelper).
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~ExcelTextExtractorTests"`
Expected: PASS (7/7)

- [ ] **Step 6: Register in DI**

```csharp
services.AddSingleton<ITextExtractor, PowerPointTextExtractor>();
services.AddSingleton<ITextExtractor, ExcelTextExtractor>();
```

- [ ] **Step 7: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 8: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/AIExperience.Rag.Application.csproj" "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/ExcelTextExtractor.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/ExcelTextExtractorTests.cs"
git commit -m "feat(rag): extracteur XLSX/CSV pagine par feuille - I-2"
```

---

### Task 6: CompositeTextExtractor — message d'erreur + dispatch de bout en bout

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/CompositeTextExtractor.cs:72-74`
- Modify: `Step 3/src/Back/AIExperience.Tests/CompositeTextExtractorTests.cs`

**Interfaces:**
- Consumes: les 5 extracteurs créés en Tasks 1-5
- Produces: aucune nouvelle interface — vérifie le câblage existant

- [ ] **Step 1: Write the failing test**

Ajouter à la fin de la classe `CompositeTextExtractorTests` (avant la dernière accolade fermante `}`) :

```csharp
    // ── Dispatch de bout en bout avec les extracteurs réels (I-2) ────────────────

    [Theory]
    [InlineData("rapport.docx")]
    [InlineData("classeur.xlsx")]
    [InlineData("export.csv")]
    [InlineData("presentation.pptx")]
    [InlineData("notes.txt")]
    [InlineData("readme.md")]
    [InlineData("data.json")]
    public void ResolveExtractor_AllNewFormats_HasMatchingExtractor(string fileName)
    {
        // Arrange — tous les extracteurs réels enregistrés (hors vidéo, qui a des dépendances Infrastructure)
        var sut = new CompositeTextExtractor(
            [
                new PdfTextExtractor(),
                new HtmlTextExtractor(),
                new PlainTextExtractor(),
                new JsonTextExtractor(),
                new DocxTextExtractor(),
                new PowerPointTextExtractor(),
                new ExcelTextExtractor()
            ],
            NullLogger<CompositeTextExtractor>.Instance);

        // Act + Assert — ne doit PAS lever NotSupportedException (un extracteur gère bien le format)
        var act = async () => await sut.ExtractTextAsync(fileName, CancellationToken.None);
        // On s'attend à une autre exception (fichier introuvable/invalide) mais jamais NotSupportedException.
        act.Should().NotThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExtractTextAsync_UnknownExtension_ErrorMessageListsSupportedFormats()
    {
        var sut = new CompositeTextExtractor([], NullLogger<CompositeTextExtractor>.Instance);

        var act = async () => await sut.ExtractTextAsync("fichier.xyz", CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*docx*");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~CompositeTextExtractorTests"`
Expected: FAIL — `ResolveExtractor_AllNewFormats_HasMatchingExtractor` échoue car `.NotThrowAsync<NotSupportedException>()` va effectivement lever (formats pas encore listés dans le message n'est pas le souci ici — le vrai souci est que le test compile mais le dernier test `ErrorMessageListsSupportedFormats` échoue car "docx" n'apparaît pas encore dans le message d'erreur actuel)

- [ ] **Step 3: Update the error message**

In `Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/CompositeTextExtractor.cs`, replace lines 67-74 :

```csharp
        var extension = Path.GetExtension(filePath);
        _logger.LogError(
            "Aucun extracteur ne prend en charge l'extension '{Extension}' pour le fichier : {File}",
            extension, filePath);

        throw new NotSupportedException(
            $"Format non supporté : '{extension}'. " +
            "Formats pris en charge : PDF, HTML, DOCX, XLSX, CSV, PPTX, TXT, Markdown, JSON, vidéo/audio.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~CompositeTextExtractorTests"`
Expected: PASS (toutes les méthodes, dont les 9 nouveaux cas)

- [ ] **Step 5: Full solution build + suite complète**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: tous les tests passent (aucune régression)

- [ ] **Step 6: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Application/Services/TextExtractor/CompositeTextExtractor.cs" "Step 3/src/Back/AIExperience.Tests/CompositeTextExtractorTests.cs"
git commit -m "feat(rag): message d'erreur composite a jour + tests de dispatch multi-format - I-2"
```

---

### Task 7: Front-end — débloquer l'upload des nouveaux formats

**Files:**
- Modify: `Step 3/src/Front/src/pages/DocumentsPage.tsx:1-6,122-133`

**Interfaces:**
- Consumes: aucune (changement purement UI)
- Produces: aucune

- [ ] **Step 1: Add the extension constant**

In `Step 3/src/Front/src/pages/DocumentsPage.tsx`, after the imports (ligne 4), ajouter :

```typescript
import type { DocumentResponse } from '../types';

// Formats supportés côté back-end (CompositeTextExtractor) — I-2 du plan Lot 2.
const ACCEPTED_EXTENSIONS =
  '.pdf,.html,.htm,.docx,.xlsx,.csv,.pptx,.txt,.md,.json,.mp4,.mkv,.webm,.avi,.mov,.wav,.mp3,.m4a,.ogg,.flac';
```

- [ ] **Step 2: Update the file input and button label**

Remplacer le bloc (lignes ~122-133) :

```tsx
          <label className={`btn btn-primary ${uploading ? 'btn-disabled' : ''}`}>
            {uploading && <span className="btn-spinner" />}
            {uploading ? 'Importation…' : '+ Ajouter un document'}
            <input
              ref={fileRef}
              type="file"
              accept={ACCEPTED_EXTENSIONS}
              hidden
              onChange={handleUpload}
              disabled={uploading}
            />
          </label>
```

- [ ] **Step 3: Verify the dev server starts and the picker shows the new formats**

Run:
```bash
cd "Step 3/src/Front"
npm run dev
```
Puis dans le navigateur (`http://localhost:5173`), ouvrir la page Documents, cliquer sur "+ Ajouter un document" et vérifier dans la boîte de dialogue système que les fichiers `.docx`/`.xlsx`/`.csv`/`.pptx`/`.txt`/`.md`/`.json` ne sont plus grisés.

- [ ] **Step 4: Commit**

```bash
git add "Step 3/src/Front/src/pages/DocumentsPage.tsx"
git commit -m "feat(front): debloque l'upload des nouveaux formats de document - I-2"
```

---

### Task 8: Détection de langue — ILanguageDetectionService + StopwordLanguageDetectionService

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ILanguageDetectionService.cs`
- Create: `Step 3/src/Back/AIExperience.Rag.Application/Services/LanguageDetection/StopwordLanguageDetectionService.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/StopwordLanguageDetectionServiceTests.cs`

**Interfaces:**
- Produces: `ILanguageDetectionService.Detect(string text) : string` (code ISO 639-1, ex. "fr")
- Produces: `StopwordLanguageDetectionService : ILanguageDetectionService`

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/StopwordLanguageDetectionServiceTests.cs
using AIExperience.Rag.Application.Services.LanguageDetection;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="StopwordLanguageDetectionService"/> (constat I-6 du plan Lot 2).
/// Vérifie la détection fr/en/es/de/it par fréquence de mots vides et le repli sur la langue
/// par défaut pour un texte trop court ou trop ambigu.
/// </summary>
public sealed class StopwordLanguageDetectionServiceTests
{
    private readonly StopwordLanguageDetectionService _sut = new();

    [Fact]
    public void Detect_EmptyText_ReturnsDefaultLanguage()
    {
        _sut.Detect("").Should().Be("fr");
    }

    [Fact]
    public void Detect_TextTooShort_ReturnsDefaultLanguage()
    {
        _sut.Detect("Short text.").Should().Be("fr");
    }

    [Fact]
    public void Detect_FrenchText_ReturnsFr()
    {
        var text = "Le chat est sur la table et il regarde par la fenêtre. " +
                    "La maison est grande et le jardin est très beau avec des fleurs partout.";
        _sut.Detect(text).Should().Be("fr");
    }

    [Fact]
    public void Detect_EnglishText_ReturnsEn()
    {
        var text = "The cat is on the table and it looks out of the window. " +
                    "The house is big and the garden is very beautiful with flowers everywhere.";
        _sut.Detect(text).Should().Be("en");
    }

    [Fact]
    public void Detect_SpanishText_ReturnsEs()
    {
        var text = "El gato está en la mesa y mira por la ventana. " +
                    "La casa es grande y el jardín es muy bonito con flores por todas partes para que todo el mundo lo vea.";
        _sut.Detect(text).Should().Be("es");
    }

    [Fact]
    public void Detect_GermanText_ReturnsDe()
    {
        var text = "Die Katze ist auf dem Tisch und sie schaut aus dem Fenster. " +
                    "Das Haus ist groß und der Garten ist sehr schön mit vielen Blumen und das ist wirklich sehr schön für alle.";
        _sut.Detect(text).Should().Be("de");
    }

    [Fact]
    public void Detect_ItalianText_ReturnsIt()
    {
        var text = "Il gatto è sul tavolo e guarda fuori dalla finestra. " +
                    "La casa è grande e il giardino è molto bello con tanti fiori che si possono vedere da lontano.";
        _sut.Detect(text).Should().Be("it");
    }

    [Fact]
    public void Detect_TextWithNoRecognizableStopwords_FallsBackToDefaultLanguage()
    {
        var text = "Xk7 Zoltar 9000 Blipverse Quixotic Zephyrian Wobblesnout Frumious Blargtastic Nizzlewomp Quorvexian.";
        _sut.Detect(text).Should().Be("fr");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~StopwordLanguageDetectionServiceTests"`
Expected: FAIL (compile error — la classe n'existe pas encore)

- [ ] **Step 3: Write the Domain interface**

```csharp
// Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ILanguageDetectionService.cs
namespace AIExperience.Rag.Domain.Interfaces.Services;

/// <summary>
/// Détecte la langue dominante d'un texte, pour indexer le full-text Postgres avec
/// le bon dictionnaire (constat I-6 du plan Lot 2) au lieu de la valeur "french" figée.
/// </summary>
public interface ILanguageDetectionService
{
    /// <summary>
    /// Retourne un code ISO 639-1 ("fr", "en", ...) détecté à partir du texte fourni.
    /// Retourne la langue par défaut de l'implémentation si le texte est trop court
    /// ou si aucune langue ne se démarque clairement.
    /// </summary>
    /// <param name="text">Texte à analyser (ex. contenu extrait d'un document).</param>
    string Detect(string text);
}
```

- [ ] **Step 4: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Application/Services/LanguageDetection/StopwordLanguageDetectionService.cs
using System.Text.RegularExpressions;
using AIExperience.Rag.Domain.Interfaces.Services;

namespace AIExperience.Rag.Application.Services.LanguageDetection;

/// <summary>
/// Détection de langue par fréquence de mots vides (stopwords), sans dépendance externe :
/// choix délibéré (constat I-6) pour rester cohérent avec la philosophie 100% locale du projet
/// (Whisper, pas d'appel cloud) et éviter le risque de compatibilité .NET 10 d'une bibliothèque
/// tierce non vérifiée. Couvre fr/en/es/de/it — suffisant pour discriminer un document mal indexé
/// avec le mauvais dictionnaire Postgres (cas concret du plan : PDF anglais indexé en français).
/// </summary>
public sealed partial class StopwordLanguageDetectionService : ILanguageDetectionService
{
    /// <summary>Langue retournée si le texte est trop court ou le score trop ambigu.</summary>
    private const string DefaultLanguage = "fr";

    /// <summary>Longueur minimale (en caractères) du texte pour tenter une détection.</summary>
    private const int MinTextLength = 50;

    /// <summary>Ratio minimal mots-vides/mots-total pour retenir une langue plutôt que le repli.</summary>
    private const double MinConfidenceRatio = 0.02;

    private static readonly Dictionary<string, HashSet<string>> StopwordsByLanguage = new()
    {
        ["fr"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "le","la","les","de","des","un","une","et","est","dans","pour","que","qui","ne","pas",
            "sur","avec","au","aux","ce","cette","ces","il","elle","nous","vous","ils","elles",
            "son","sa","ses","plus","mais","ou","donc"
        },
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","of","and","a","to","in","is","you","that","it","he","was","for","on","are","as",
            "with","his","they","at","be","this","have","from","or","one","had","by","word","out"
        },
        ["es"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "el","la","de","que","y","a","en","un","ser","se","no","por","con","su","para","como",
            "estar","tener","lo","todo","pero","hacer","o","poder","decir","este","ir","mundo","vea"
        },
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "der","die","das","und","in","zu","den","ist","von","mit","sich","des","auf","für","im",
            "dem","nicht","ein","eine","als","auch","es","an","werden","aus","er","hat","dass","sie","alle"
        },
        ["it"] = new(StringComparer.OrdinalIgnoreCase)
        {
            "il","di","che","è","e","la","per","un","in","non","mi","si","con","lo","ho","ma","ci",
            "come","da","i","questa","quello","gli","le","tu","se","noi","lei","suo","tanti"
        }
    };

    /// <inheritdoc/>
    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < MinTextLength)
            return DefaultLanguage;

        var words = WordsRegex().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0) return DefaultLanguage;

        var best = StopwordsByLanguage
            .Select(kv => (Language: kv.Key, Score: (double)words.Count(w => kv.Value.Contains(w)) / words.Count))
            .OrderByDescending(x => x.Score)
            .First();

        return best.Score >= MinConfidenceRatio ? best.Language : DefaultLanguage;
    }

    [GeneratedRegex(@"[\p{L}]+", RegexOptions.Compiled)]
    private static partial Regex WordsRegex();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~StopwordLanguageDetectionServiceTests"`
Expected: PASS (8/8). Si `Detect_SpanishText_ReturnsEs`/`Detect_GermanText_ReturnsDe`/`Detect_ItalianText_ReturnsIt` échouent parce qu'une autre langue obtient un score plus élevé sur le texte d'exemple, enrichir le texte d'exemple du test (ajouter des phrases avec plus de mots-vides propres à la langue visée) plutôt que de modifier les listes de stopwords — ce sont des tests, l'implémentation reste correcte.

- [ ] **Step 6: Register in DI**

In `Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs`, ajouter une méthode et l'inclure dans `AddApplication` :

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    return services
             .ConfigureMediatR()
             .AddChunker()
             .AddTextExtractors()
             .AddLanguageDetection()
             .AddIngestion();
}

// ... (après AddTextExtractors)

/// <summary>Enregistre le service de détection de langue (I-6).</summary>
public static IServiceCollection AddLanguageDetection(this IServiceCollection services)
{
    services.AddSingleton<ILanguageDetectionService, StopwordLanguageDetectionService>();
    return services;
}
```

Ajouter l'using en haut du fichier :
```csharp
using AIExperience.Rag.Application.Services.LanguageDetection;
```

- [ ] **Step 7: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur

- [ ] **Step 8: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/ILanguageDetectionService.cs" "Step 3/src/Back/AIExperience.Rag.Application/Services/LanguageDetection/StopwordLanguageDetectionService.cs" "Step 3/src/Back/AIExperience.Rag.Application/DependencyInjection.cs" "Step 3/src/Back/AIExperience.Tests/StopwordLanguageDetectionServiceTests.cs"
git commit -m "feat(rag): detection de langue heuristique fr/en/es/de/it sans dependance - I-6"
```

---

### Task 9: Document.SetDetectedLanguage

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/DocumentTests.cs`

**Interfaces:**
- Consumes: `Document.Create(...)` (existant), `DocumentMetadata.Create(...)` (existant)
- Produces: `Document.SetDetectedLanguage(string language) : void` — met à jour `Metadata.Language` et `UpdatedAt`

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/DocumentTests.cs
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="Document.SetDetectedLanguage"/> (constat I-6 du plan Lot 2).
/// Vérifie la mise à jour de la langue détectée sur les métadonnées du document.
/// </summary>
public sealed class DocumentTests
{
    private static Document CreateDocument(string initialLanguage = "fr")
        => Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport", language: initialLanguage));

    [Fact]
    public void SetDetectedLanguage_UpdatesMetadataLanguage()
    {
        var document = CreateDocument(initialLanguage: "fr");

        document.SetDetectedLanguage("en");

        document.Metadata.Language.Should().Be("en");
    }

    [Fact]
    public void SetDetectedLanguage_PreservesOtherMetadataFields()
    {
        var document = Document.Create(
            fileName: "rapport.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 1024,
            userId: "user-1",
            metadata: DocumentMetadata.Create(title: "Rapport annuel", author: "Service Finance", language: "fr"));

        document.SetDetectedLanguage("en");

        document.Metadata.Title.Should().Be("Rapport annuel");
        document.Metadata.Author.Should().Be("Service Finance");
    }

    [Fact]
    public void SetDetectedLanguage_UpdatesUpdatedAtTimestamp()
    {
        var document = CreateDocument();
        var before = document.UpdatedAt;

        Thread.Sleep(10);
        document.SetDetectedLanguage("en");

        document.UpdatedAt.Should().BeAfter(before);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~DocumentTests"`
Expected: FAIL (compile error — `SetDetectedLanguage` n'existe pas encore)

- [ ] **Step 3: Write minimal implementation**

In `Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs`, ajouter après `SetFileReference` (ligne 89) :

```csharp
    /// <summary>
    /// Met à jour la langue détectée du document (constat I-6 du plan Lot 2) : appelée après
    /// détection automatique sur le texte extrait, pour que <c>content_tsv</c> soit indexé
    /// avec le bon dictionnaire Postgres au lieu de "french" figé.
    /// </summary>
    /// <param name="language">Code ISO 639-1 détecté (ex. "fr", "en").</param>
    public void SetDetectedLanguage(string language)
    {
        Metadata = Metadata with { Language = language };
        UpdatedAt = DateTimeOffset.UtcNow;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~DocumentTests"`
Expected: PASS (3/3)

- [ ] **Step 5: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Entities/Document.cs" "Step 3/src/Back/AIExperience.Tests/DocumentTests.cs"
git commit -m "feat(rag): Document.SetDetectedLanguage pour propager la langue detectee - I-6"
```

---

### Task 10: PostgresTextSearchConfig — mapping ISO → regconfig

**Files:**
- Create: `Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PostgresTextSearchConfig.cs`
- Test: `Step 3/src/Back/AIExperience.Tests/PostgresTextSearchConfigTests.cs`

**Interfaces:**
- Produces: `PostgresTextSearchConfig.Resolve(string? isoLanguageCode) : string` — retourne un nom de `regconfig` Postgres valide, jamais une valeur invalide

- [ ] **Step 1: Write the failing tests**

```csharp
// Step 3/src/Back/AIExperience.Tests/PostgresTextSearchConfigTests.cs
using AIExperience.Rag.Infrastructure.VectorStore;
using FluentAssertions;

namespace AIExperience.Tests;

/// <summary>
/// Tests TDD pour <see cref="PostgresTextSearchConfig"/> (constat I-6 du plan Lot 2).
/// Vérifie le mapping code ISO 639-1 → nom de configuration Postgres (regconfig),
/// avec repli sur "simple" pour toute langue non mappée (jamais d'échec SQL).
/// </summary>
public sealed class PostgresTextSearchConfigTests
{
    [Theory]
    [InlineData("fr", "french")]
    [InlineData("FR", "french")]
    [InlineData("en", "english")]
    [InlineData("es", "spanish")]
    [InlineData("de", "german")]
    [InlineData("it", "italian")]
    public void Resolve_KnownIsoCode_ReturnsExpectedRegConfig(string isoCode, string expected)
    {
        PostgresTextSearchConfig.Resolve(isoCode).Should().Be(expected);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_UnknownOrMissingIsoCode_FallsBackToSimple(string? isoCode)
    {
        PostgresTextSearchConfig.Resolve(isoCode).Should().Be("simple");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PostgresTextSearchConfigTests"`
Expected: FAIL (compile error)

- [ ] **Step 3: Write minimal implementation**

```csharp
// Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PostgresTextSearchConfig.cs
namespace AIExperience.Rag.Infrastructure.VectorStore;

/// <summary>
/// Mappe un code de langue ISO 639-1 vers le nom de configuration de recherche texte
/// Postgres (<c>regconfig</c>) correspondant (constat I-6 du plan Lot 2).
/// Repli sur "simple" (tokenisation sans stemming) pour toute langue non mappée :
/// garantit qu'aucun appel SQL n'échoue pour cause de regconfig invalide.
/// </summary>
public static class PostgresTextSearchConfig
{
    private static readonly Dictionary<string, string> RegConfigByIsoCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = "french",
        ["en"] = "english",
        ["es"] = "spanish",
        ["de"] = "german",
        ["it"] = "italian",
    };

    /// <summary>Retourne le nom de configuration Postgres pour un code ISO, ou "simple" si inconnu.</summary>
    public static string Resolve(string? isoLanguageCode) =>
        isoLanguageCode is not null && RegConfigByIsoCode.TryGetValue(isoLanguageCode, out var regconfig)
            ? regconfig
            : "simple";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~PostgresTextSearchConfigTests"`
Expected: PASS (9/9)

- [ ] **Step 5: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PostgresTextSearchConfig.cs" "Step 3/src/Back/AIExperience.Tests/PostgresTextSearchConfigTests.cs"
git commit -m "feat(rag): mapping ISO vers regconfig Postgres avec repli simple - I-6"
```

---

### Task 11: RagOptions.Retrieval.FullTextLanguage

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs:48-55`
- Modify: `Step 3/src/Back/AIExperience.Tests/RagOptionsDefaultTests.cs`

**Interfaces:**
- Produces: `RetrievalOptions.FullTextLanguage : string` (défaut `"french"`)

- [ ] **Step 1: Write the failing test**

Ajouter à la fin de la classe `RagOptionsDefaultTests` (avant la dernière accolade `}`) :

```csharp
    // ── I-6 : langue de la recherche full-text ────────────────────────────────

    [Fact]
    public void Retrieval_DefaultFullTextLanguage_IsFrench()
    {
        // Langue de requête configurable globalement (détecter la langue d'une question
        // courte est peu fiable) — cf. constat I-6 du plan Lot 2.
        _sut.Retrieval.FullTextLanguage.Should().Be("french");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~RagOptionsDefaultTests"`
Expected: FAIL (compile error — `FullTextLanguage` n'existe pas encore)

- [ ] **Step 3: Add the property**

In `Step 3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs`, remplacer `RetrievalOptions` :

```csharp
/// <summary>Options pour la récupération vectorielle dans pgvector.</summary>
public sealed class RetrievalOptions
{
    /// <summary>Nombre maximum de chunks récupérés avant reranking. Par défaut : 20.</summary>
    public int TopK { get; set; } = 20;

    /// <summary>Score de similarité cosinus minimum pour retenir un chunk. Par défaut : 0.3.</summary>
    public double ScoreThreshold { get; set; } = 0.3;

    /// <summary>
    /// Configuration de recherche texte Postgres (regconfig) utilisée par SearchFullTextAsync
    /// et SearchLexicalAsync pour analyser la question (ex. "french", "english"). Reste globale
    /// et configurable plutôt que détectée par question : peu fiable sur un texte court.
    /// (Constat I-6 du plan Lot 2)
    /// </summary>
    public string FullTextLanguage { get; set; } = "french";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx" --filter "FullyQualifiedName~RagOptionsDefaultTests"`
Expected: PASS (7/7)

- [ ] **Step 5: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Infrastructure/Options/RagOptions.cs" "Step 3/src/Back/AIExperience.Tests/RagOptionsDefaultTests.cs"
git commit -m "feat(rag): option FullTextLanguage configurable pour la recherche texte - I-6"
```

---

### Task 12: Propagation de la langue dans le stockage vectoriel (bout en bout)

> **Note :** ce lot regroupe intentionnellement `IVectorStoreService`, `PgVectorStoreService`,
> `ReembedCorpusHandler` et `IngestionService` en une seule tâche : ce sont 4 fichiers liés par
> une même signature (`UpsertBatchAsync`) — les scinder laisserait la solution non compilable
> entre deux tâches. `PgVectorStoreService` a zéro test unitaire aujourd'hui (raw SQL contre
> Postgres réel) : ce lot suit la même convention plutôt que d'introduire une nouvelle
> infrastructure de tests d'intégration hors du périmètre approuvé. La vérification se fait par
> build + suite existante + checklist manuelle (Step final).

**Files:**
- Modify: `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IVectorStoreService.cs:60-76`
- Modify: `Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/ReembedCorpusHandler.cs`
- Modify: `Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs`

**Interfaces:**
- Consumes: `ILanguageDetectionService.Detect` (Task 8), `Document.SetDetectedLanguage` (Task 9), `PostgresTextSearchConfig.Resolve` (Task 10), `RagOptions.Retrieval.FullTextLanguage` (Task 11)
- Produces:
  - `IVectorStoreService.UpsertAsync(DocumentChunk chunk, float[] embedding, string language, CancellationToken ct = default) : Task`
  - `IVectorStoreService.UpsertBatchAsync(IReadOnlyList<(DocumentChunk Chunk, float[] Embedding, string Language)> items, CancellationToken ct = default) : Task`

- [ ] **Step 1: Update the IVectorStoreService interface**

In `Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IVectorStoreService.cs`, remplacer les lignes 60-76 :

```csharp
        /// <summary>
        /// Insère ou met à jour l'embedding d'un chunk dans pgvector.
        /// </summary>
        /// <param name="chunk">Chunk à indexer.</param>
        /// <param name="embedding">Vecteur d'embedding associé.</param>
        /// <param name="language">
        /// Code ISO 639-1 de la langue du document (ex. "fr") — détermine le dictionnaire Postgres
        /// utilisé pour indexer content_tsv (constat I-6 du plan Lot 2).
        /// </param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task UpsertAsync(DocumentChunk chunk, float[] embedding, string language, CancellationToken ct = default);

        /// <summary>
        /// Persiste plusieurs chunks et leurs embeddings en une seule transaction PostgreSQL.
        /// Équivalent à N appels <see cref="UpsertAsync"/> mais avec un seul commit — ~5-10× plus rapide.
        /// La langue est portée par item (pas globale au batch) car un même appel peut mélanger
        /// des chunks de documents différents (ex. ré-ingestion corpus complet).
        /// </summary>
        /// <param name="items">Liste de tuples (chunk, embedding, langue ISO) à insérer.</param>
        /// <param name="ct">Jeton d'annulation.</param>
        Task UpsertBatchAsync(
            IReadOnlyList<(DocumentChunk Chunk, float[] Embedding, string Language)> items,
            CancellationToken ct = default);
```

- [ ] **Step 2: Update PgVectorStoreService**

In `Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs`, ajouter les usings :

```csharp
using AIExperience.Rag.Infrastructure.Options;
using Microsoft.Extensions.Options;
```

Remplacer le constructeur (lignes 15-20) :

```csharp
    private readonly AppDbContext context;
    private readonly string fullTextLanguage;

    public PgVectorStoreService(AppDbContext context, IOptions<RagOptions> ragOptions)
    {
        this.context = context;
        this.fullTextLanguage = ragOptions.Value.Retrieval.FullTextLanguage;
    }
```

Dans `SearchFullTextAsync`, remplacer le SQL et les paramètres (lignes 114-135) :

```csharp
        // La requête cible la colonne content_tsv (indexée via GIN) pour de meilleures performances.
        // La langue de la question est configurable (RagOptions.Retrieval.FullTextLanguage) plutôt que
        // détectée : peu fiable sur un texte court (constat I-6 du plan Lot 2).
        var sql = $"""
            SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
                   dc.section_title, dc.embedding_dimensions, dc.created_at,
                   ts_rank(dc.content_tsv, plainto_tsquery(@queryLanguage::regconfig, @query))::float8 AS score,
                   d.file_name,
                   dc.start_time_seconds, dc.end_time_seconds
            FROM document_chunks dc
            JOIN documents d ON d.id = dc.document_id
            WHERE dc.content_tsv @@ plainto_tsquery(@queryLanguage::regconfig, @query)
            {documentFilter}
            ORDER BY score DESC
            LIMIT @topK
            """;

        var parameters = new List<object>
        {
            new NpgsqlParameter("queryLanguage", fullTextLanguage),
            new NpgsqlParameter("query", query),
            new NpgsqlParameter("topK", topK)
        };
```

Dans `SearchLexicalAsync`, remplacer le SQL et les paramètres (lignes 185-206) :

```csharp
        // to_tsquery(replace(plainto_tsquery(...)::text, '&', '|')) : même tokenisation/stemming
        // que plainto_tsquery, mais les mots sont combinés en OR plutôt qu'en AND.
        var sql = $"""
            WITH q AS (
                SELECT to_tsquery(@queryLanguage::regconfig,
                    replace(plainto_tsquery(@queryLanguage::regconfig, @query)::text, ' & ', ' | ')) AS tsq
            )
            SELECT dc.id, dc.document_id, dc.content, dc.chunk_index, dc.page_number,
                   dc.section_title, dc.embedding_dimensions, dc.created_at,
                   ts_rank(dc.content_tsv, q.tsq)::float8 AS score,
                   d.file_name,
                   dc.start_time_seconds, dc.end_time_seconds
            FROM document_chunks dc
            JOIN documents d ON d.id = dc.document_id, q
            WHERE dc.content_tsv @@ q.tsq
            {documentFilter}
            ORDER BY score DESC
            LIMIT @topK
            """;

        var parameters = new List<object>
        {
            new NpgsqlParameter("queryLanguage", fullTextLanguage),
            new NpgsqlParameter("query", query),
            new NpgsqlParameter("topK", topK)
        };
```

Remplacer `UpsertAsync` et `UpsertBatchAsync` (lignes 242-286) :

```csharp
    /// <inheritdoc/>
    public async Task UpsertAsync(DocumentChunk chunk, float[] embedding, string language, CancellationToken ct = default)
    {
        var regconfig = PostgresTextSearchConfig.Resolve(language);

        var sql = """
            INSERT INTO document_chunks
                ("id", document_id, content, chunk_index, page_number, section_title,
                 embedding_dimensions, embedding, created_at, start_time_seconds, end_time_seconds, content_tsv)
            VALUES
                (@id, @documentId, @content, @chunkIndex, @pageNumber, @sectionTitle,
                 @embDims, @embedding::vector, NOW(), @startTimeSecs, @endTimeSecs,
                 to_tsvector(@regconfig::regconfig, @content))
            ON CONFLICT ("id") DO UPDATE SET
                content = EXCLUDED.content,
                embedding = EXCLUDED.embedding,
                start_time_seconds = EXCLUDED.start_time_seconds,
                end_time_seconds = EXCLUDED.end_time_seconds,
                content_tsv = EXCLUDED.content_tsv;
            """;

        await context.Database.ExecuteSqlRawAsync(sql,
            new NpgsqlParameter("id", chunk.Id),
            new NpgsqlParameter("documentId", chunk.DocumentId),
            new NpgsqlParameter("content", chunk.Content),
            new NpgsqlParameter("chunkIndex", chunk.ChunkIndex),
            new NpgsqlParameter("pageNumber", (object?)chunk.PageNumber ?? DBNull.Value),
            new NpgsqlParameter("sectionTitle", (object?)chunk.SectionTitle ?? DBNull.Value),
            new NpgsqlParameter("embDims", chunk.EmbeddingDimensions),
            new NpgsqlParameter("embedding", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Real) { Value = embedding },
            new NpgsqlParameter("startTimeSecs", (object?)(chunk.StartTime?.TotalSeconds) ?? DBNull.Value),
            new NpgsqlParameter("endTimeSecs", (object?)(chunk.EndTime?.TotalSeconds) ?? DBNull.Value),
            new NpgsqlParameter("regconfig", regconfig));
    }

    /// <inheritdoc/>
    public async Task UpsertBatchAsync(
        IReadOnlyList<(DocumentChunk Chunk, float[] Embedding, string Language)> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        // Transaction unique : tous les INSERTs partagent un seul commit PostgreSQL.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        foreach (var (chunk, embedding, language) in items)
            await UpsertAsync(chunk, embedding, language, ct);

        await transaction.CommitAsync(ct);
    }
```

- [ ] **Step 3: Update ReembedCorpusHandler**

Replace `Step 3/src/Back/AIExperience.Rag.Application/Document/Command/ReembedCorpusHandler.cs` entièrement :

```csharp
using System.Diagnostics;
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using MediatR;

namespace AIExperience.Rag.Application.Document.Command;

/// <summary>
/// Ré-embed le corpus existant avec le rôle <see cref="EmbeddingTaskType.Document"/>
/// pour le rendre compatible avec des requêtes désormais préfixées "search_query: ".
/// Réutilise le contenu déjà chunké : aucune ré-extraction des fichiers sources.
/// </summary>
public sealed class ReembedCorpusHandler(
    IVectorStoreService vectorStoreService,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository) : IRequestHandler<ReembedCorpusCommand, ReembedCorpusResult>
{
    public async Task<ReembedCorpusResult> Handle(ReembedCorpusCommand request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var chunks = await vectorStoreService.GetAllChunksAsync(cancellationToken);
        if (chunks.Count == 0)
            return new ReembedCorpusResult { ChunksReembedded = 0, DurationMs = sw.ElapsedMilliseconds };

        // Réutilise le sous-batching 96 + résilience Polly déjà en place pour l'ingestion normale.
        var embeddings = await embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Content), EmbeddingTaskType.Document, cancellationToken);

        if (embeddings.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk lors de la ré-ingestion : {embeddings.Count} vecteurs retournés " +
                $"pour {chunks.Count} chunks.");

        // La langue est résolue par document (pas globale au batch, un ré-embed traverse tout le
        // corpus) : cache pour éviter un aller-retour DB par chunk (constat I-6 du plan Lot 2).
        var languageByDocumentId = new Dictionary<Guid, string>();
        var items = new List<(DocumentChunk Chunk, float[] Embedding, string Language)>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (!languageByDocumentId.TryGetValue(chunk.DocumentId, out var language))
            {
                var document = await documentRepository.GetByIdAsync(chunk.DocumentId, cancellationToken);
                language = document?.Metadata.Language ?? "fr";
                languageByDocumentId[chunk.DocumentId] = language;
            }
            items.Add((chunk, embeddings[i], language));
        }

        // Même id, même contenu → UpsertAsync (ON CONFLICT sur "id") ne fait que remplacer l'embedding.
        await vectorStoreService.UpsertBatchAsync(items, cancellationToken);

        sw.Stop();
        return new ReembedCorpusResult { ChunksReembedded = chunks.Count, DurationMs = sw.ElapsedMilliseconds };
    }
}
```

- [ ] **Step 4: Update IngestionService**

Replace `Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs` entièrement :

```csharp
using AIExperience.Rag.Domain.Entities;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Models.Video;

namespace AIExperience.Rag.Application.Services;

/// <summary>
/// Implémentation de <see cref="IIngestionService"/>.
/// Orchestre le pipeline complet d'ingestion : parsing → chunking → embedding → stockage pgvector.
/// Le chunker est injecté par DI pour pouvoir être remplacé ou testé sans modifier ce service.
/// </summary>
public sealed class IngestionService(
    ICompositeTextExtractor compositeTextExtractor,
    IEmbeddingService embeddingService,
    IDocumentRepository documentRepository,
    IVectorStoreService vectorStoreService,
    ITemporalChunker temporalChunker,
    ITextChunker textChunker,
    ILanguageDetectionService languageDetectionService) : IIngestionService
{
    /// <inheritdoc/>
    public async Task IngestAsync(
        string filePath,
        Guid documentId,
        DocumentMetadata metadata,
        ChunkingStrategy strategy = ChunkingStrategy.Recursive,
        CancellationToken ct = default)
    {
        // 1. Extraction page par page, préserve PageNumber si l'extracteur le supporte.
        var pages = await compositeTextExtractor.ExtractPagesAsync(filePath, ct);

        // 2. Chunking avec propagation du numéro de page
        var textChunks = textChunker.ChunkPages(pages);

        // Garde-fou : 0 chunk = extraction vide → le document serait persisté sans contenu interrogeable.
        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId}. " +
                "L'extraction de texte a retourné un contenu vide ou non découpable.");

        // 2bis. Détection de la langue du document (I-6) : utilisée pour indexer content_tsv
        // avec le bon dictionnaire Postgres, et persistée sur le document pour traçabilité.
        var sampleText = string.Join("\n", pages.Select(p => p.Text));
        var detectedLanguage = languageDetectionService.Detect(sampleText);

        var document = await documentRepository.GetByIdAsync(documentId, ct);
        if (document is not null)
        {
            document.SetDetectedLanguage(detectedLanguage);
            await documentRepository.UpdateAsync(document, ct);
        }

        // 3. Embedding + stockage batch dans pgvector (1 transaction pour tous les chunks)
        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

        // Vérification de cohérence avant l'accès indexé embeddings[i].
        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (document {documentId}).");

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i], detectedLanguage);
        }).ToList<(DocumentChunk, float[], string)>();

        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task IngestTextAsync(
        string text,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        // 1. Chunking du texte brut (l'étape d'extraction est déjà faite — transcription)
        var textChunks = textChunker.Chunk(text);

        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestTextAsync). " +
                "Le texte fourni est vide ou entièrement non découpable.");

        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (IngestTextAsync, document {documentId}).");

        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                tc.PageNumber,
                string.IsNullOrEmpty(tc.SectionTitle) ? string.Empty : CleanString(tc.SectionTitle));
            return (chunk, embeddings[i], metadata.Language);
        }).ToList<(DocumentChunk, float[], string)>();

        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task IngestFromSegmentsAsync(
        IReadOnlyList<TranscriptionSegment> segments,
        Guid documentId,
        DocumentMetadata metadata,
        CancellationToken ct = default)
    {
        // Chunking temporel : respecte les frontières des segments Whisper et préserve les timestamps
        var textChunks = temporalChunker.ChunkSegments(segments);

        if (textChunks.Count == 0)
            throw new InvalidOperationException(
                $"Aucun chunk produit pour le document {documentId} (IngestFromSegmentsAsync). " +
                "La transcription ne contient aucun segment (vidéo silencieuse ou corrompue ?).");

        var embeddings = await embeddingService.EmbedBatchAsync(textChunks.Select(c => c.Content), EmbeddingTaskType.Document, ct);

        if (embeddings.Count != textChunks.Count)
            throw new InvalidOperationException(
                $"Incohérence embedding/chunk : {embeddings.Count} vecteurs retournés " +
                $"pour {textChunks.Count} chunks (IngestFromSegmentsAsync, document {documentId}).");

        // Stockage batch : 1 transaction pour tous les chunks (vs N commits auto-isolés)
        // Langue déjà connue via Whisper (metadata.Language peuplé par TranscribeVideoHandler).
        var items = textChunks.Select((tc, i) =>
        {
            var chunk = DocumentChunk.Create(
                documentId,
                CleanString(tc.Content),
                i,
                embeddings[i].Length,
                startTime: tc.StartTime,
                endTime: tc.EndTime);
            return (chunk, embeddings[i], metadata.Language);
        }).ToList<(DocumentChunk, float[], string)>();

        await vectorStoreService.UpsertBatchAsync(items, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid documentId, CancellationToken ct = default)
        => await vectorStoreService.DeleteByDocumentIdAsync(documentId, ct);

    private static string CleanString(string? input) => input?.Replace("\0", string.Empty) ?? string.Empty;
}
```

- [ ] **Step 5: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur (vérifie que tous les call sites compilent avec les nouvelles signatures)

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: tous les tests passent (aucune régression sur les 87 tests existants + tous les nouveaux tests des Tasks 1-11)

- [ ] **Step 7: Commit**

```bash
git add "Step 3/src/Back/AIExperience.Rag.Domain/Interfaces/Services/IVectorStoreService.cs" "Step 3/src/Back/AIExperience.Rag.Infrastructure/VectorStore/PgVectorStoreService.cs" "Step 3/src/Back/AIExperience.Rag.Application/Document/Command/ReembedCorpusHandler.cs" "Step 3/src/Back/AIExperience.Rag.Application/Services/IngestionService.cs"
git commit -m "feat(rag): propage la langue detectee jusqu'a content_tsv (upsert + recherche) - I-6"
```

---

### Task 13: Schéma SQL — content_tsv n'est plus une colonne générée

**Files:**
- Modify: `Step 3/scripts/init.sql:59-84`
- Create: `Step 3/scripts/migrate-i6-language-fts.sql`

**Interfaces:**
- Consumes: aucune (schéma SQL, pas de code C#)
- Produces: aucune

- [ ] **Step 1: Update init.sql**

In `Step 3/scripts/init.sql`, remplacer les lignes 59-68 (colonnes temporelles + `content_tsv`) :

```sql
    -- I-18 : colonnes temporelles pour les chunks issus de vidéos (Whisper)
    -- Valeur en secondes (double precision) ; NULL pour les documents non-vidéo.
    start_time_seconds   DOUBLE PRECISION,
    end_time_seconds     DOUBLE PRECISION,
    -- I-6 (Lot 2) : content_tsv N'EST PLUS une colonne GENERATED.
    -- to_tsvector(regconfig, text) est STABLE (pas IMMUTABLE) : Postgres interdit les colonnes
    -- générées dont le regconfig varie par ligne. La valeur est donc calculée explicitement par
    -- l'application dans l'INSERT/UPDATE (PgVectorStoreService.UpsertAsync), avec le regconfig
    -- correspondant à la langue détectée du document (au lieu de 'french' figé).
    content_tsv          tsvector,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

- [ ] **Step 2: Create the migration script for existing databases**

```sql
-- Step 3/scripts/migrate-i6-language-fts.sql
-- ============================================================
-- Migration I-6 (Lot 2) : content_tsv n'est plus une colonne GENERATED.
-- À exécuter sur une base Step 3 déjà créée avec l'ancien init.sql
-- (colonne content_tsv générée avec 'french' figé).
--
-- to_tsvector(regconfig, text) est STABLE (pas IMMUTABLE) : Postgres interdit
-- les colonnes générées dont le regconfig varie par ligne. Cette migration
-- convertit content_tsv en colonne normale, peuplée par l'application.
-- ============================================================

ALTER TABLE document_chunks DROP COLUMN IF EXISTS content_tsv;
ALTER TABLE document_chunks ADD COLUMN content_tsv tsvector;

-- Backfill : les chunks déjà indexés gardent la langue historique 'french'
-- (pas de ré-détection automatique — cohérent avec la limitation déjà actée pour R-15).
UPDATE document_chunks SET content_tsv = to_tsvector('french', content);

CREATE INDEX IF NOT EXISTS ix_document_chunks_content_tsv
    ON document_chunks
    USING gin (content_tsv);
```

- [ ] **Step 3: Verify manually against a running database**

Run:
```bash
cd "Step 3"
docker-compose up -d
```

Sur une base **neuve** : supprimer le volume Docker existant si besoin (`docker-compose down -v`) puis relancer `docker-compose up -d` pour ré-exécuter `init.sql` à partir de zéro, et vérifier :
```bash
docker exec -it <container_postgres> psql -U postgres -d ragdocumentchat -c "\d document_chunks"
```
Expected : `content_tsv` apparaît comme colonne `tsvector` simple (pas de mention `generated always as ...` dans la sortie).

Sur une base **existante** (déjà migrée par un précédent Lot) :
```bash
docker exec -it <container_postgres> psql -U postgres -d ragdocumentchat -f /chemin/vers/migrate-i6-language-fts.sql
```

- [ ] **Step 4: Commit**

```bash
git add "Step 3/scripts/init.sql" "Step 3/scripts/migrate-i6-language-fts.sql"
git commit -m "feat(rag): content_tsv n'est plus une colonne generee - I-6 migration schema"
```

---

### Task 14: Vérification finale — build, suite complète, checklist manuelle

**Files:** aucun fichier modifié — tâche de vérification uniquement.

**Interfaces:** N/A

- [ ] **Step 1: Full solution build**

Run: `dotnet build "Step 3/src/Back/AIExperience.slnx"`
Expected: Build succeeded, 0 erreur, 0 avertissement nouveau

- [ ] **Step 2: Full test suite**

Run: `dotnet test "Step 3/src/Back/AIExperience.slnx"`
Expected: 100% des tests passent (87 tests pré-existants + ~55 nouveaux tests de ce plan)

- [ ] **Step 3: Front-end build**

Run:
```bash
cd "Step 3/src/Front"
npm run build
```
Expected: build réussi, aucune erreur TypeScript

- [ ] **Step 4: Checklist de vérification manuelle (nécessite `docker-compose up -d` + API + front lancés)**

- [ ] Uploader un `.docx` avec des titres Word (Heading1/2) → vérifier dans les citations du Chat que `SectionTitle` est peuplé.
- [ ] Uploader un `.xlsx` multi-feuilles → vérifier que chaque feuille apparaît comme une section distincte dans les citations.
- [ ] Uploader un `.csv` avec un champ contenant une virgule entre guillemets → vérifier que la colonne n'est pas coupée.
- [ ] Uploader un `.pptx` → vérifier la pagination par slide dans les citations (`p. 1`, `p. 2`...).
- [ ] Uploader un `.txt` et un `.md` avec des titres `#` → vérifier l'extraction et la section Markdown.
- [ ] Uploader un `.json` → vérifier que le contenu aplati est interrogeable dans le Chat.
- [ ] Uploader un document en **anglais** → interroger via le Chat en mode "Classique" (full-text) avec une question en anglais → vérifier que des résultats pertinents remontent (avant ce lot, le stemming français aurait dégradé le score).
- [ ] Vérifier en base (`SELECT metadata_language FROM documents WHERE file_name = '...'`) que la langue détectée correspond à la langue réelle du document uploadé.

- [ ] **Step 5: Update the plan (this file) — mark all tasks complete**

Cocher toutes les cases `- [ ]` restantes de ce document une fois la checklist manuelle validée.
