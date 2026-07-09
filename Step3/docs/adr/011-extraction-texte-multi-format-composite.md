# ADR-011 — Extraction de texte multi-format via un extracteur composite

**Date :** 2026-07-05  
**Statut :** Accepté  
**Auteur :** Geoffrey

---

## Contexte

Jusqu'à Step 2, l'ingestion ne savait extraire que le **PDF** (PdfPig). Or un système RAG utile
doit avaler les formats bureautiques courants : **DOCX, Excel, PowerPoint, HTML, JSON, texte
brut**, en plus du PDF (et, depuis Step 3, des transcriptions vidéo — traitées à part, cf.
[ADR-006](006-chunking-temporel-video.md)).

Chaque format a sa propre bibliothèque et sa propre logique d'extraction. Le pipeline
d'ingestion, lui, ne veut pas s'en soucier : il a juste besoin d'« un texte » à partir d'« un
chemin de fichier ». Il faut donc un point d'entrée **unique** qui masque la diversité des
formats, tout en restant **ouvert à l'extension** (ajouter un format sans toucher au pipeline).

---

## Décision

Appliquer le **pattern Strategy + Composite** : une interface `ITextExtractor` par format, et un
**`CompositeTextExtractor`** (implémentant `ICompositeTextExtractor`) qui, selon l'extension du
fichier, **sélectionne** le bon extracteur et lui délègue le travail.

L'injection de dépendances fournit **toutes** les implémentations de `ITextExtractor` ; le
composite en dispose comme d'une liste et résout la bonne à la volée.

---

## Architecture de la solution

### Une interface, plusieurs stratégies

```csharp
public interface ITextExtractor
{
    bool CanHandle(string filePath);                                   // « je sais traiter ce format »
    Task<string> ExtractTextAsync(string filePath, CancellationToken ct);
}
```

Implémentations enregistrées : `PdfTextExtractor`, `DocxTextExtractor`, `ExcelTextExtractor`,
`PowerPointTextExtractor`, `HtmlTextExtractor`, `JsonTextExtractor`, `PlainTextExtractor`.

### Le composite — résolution par extension et délégation

```csharp
public sealed class CompositeTextExtractor : ICompositeTextExtractor
{
    private readonly IReadOnlyList<ITextExtractor> _textExtractors;

    public CompositeTextExtractor(IEnumerable<ITextExtractor> textExtractors, ILogger<CompositeTextExtractor> logger)
    {
        // Exclut une éventuelle référence circulaire (le composite lui-même dans la liste DI).
        _textExtractors = textExtractors.Where(e => e.GetType() != typeof(CompositeTextExtractor)).ToList();
        _logger = logger;
    }

    private ITextExtractor ResolveExtractor(string filePath)
    {
        var extractor = _textExtractors.FirstOrDefault(e => e.CanHandle(filePath));
        if (extractor is not null) return extractor;
        // Aucun extracteur → NotSupportedException, jamais un "Completed" à 0 chunk silencieux.
        throw new NotSupportedException(...);
    }
}
```

Deux décisions de conception notables :

- **Échec explicite si format non supporté.** Plutôt que de laisser passer un document
  « Completed » avec **0 chunk** (silencieusement inutilisable), le composite **lève une
  exception**. Le document sera marqué `Failed` avec un message clair, ce qui est honnête.
- **Auto-exclusion.** Comme le composite implémente lui-même une interface parente et que la DI
  pourrait l'injecter dans sa propre liste, il **se retire** de l'ensemble pour éviter une
  récursion infinie.

### Extraction paginée optionnelle — `IPageAwareTextExtractor`

Certains formats (PDF) portent une **notion de page** utile pour situer une citation. Le
composite gère ça de façon dégradée :

```csharp
public async Task<IReadOnlyList<(int PageNumber, string Text)>> ExtractPagesAsync(string filePath, CancellationToken ct)
{
    var extractor = ResolveExtractor(filePath);
    if (extractor is IPageAwareTextExtractor paged)   // le format sait paginer
        return await paged.ExtractPagesAsync(filePath, ct);

    // Fallback : format non paginé → une unique "page 1" contenant tout le texte.
    return [(1, await extractor.ExtractTextAsync(filePath, ct))];
}
```

Ainsi, un extracteur non paginé reste compatible (page 1 unique), et les numéros de page des
formats paginés sont **propagés jusqu'aux chunks** (et donc aux citations).

### Ajouter un format = ajouter une classe

Introduire un nouveau format se limite à écrire une classe `ITextExtractor` et à l'enregistrer
en DI. **Aucune modification** du composite ni du pipeline d'ingestion — principe ouvert/fermé
respecté.

---

## Conséquences

### Positives

- Le pipeline d'ingestion dépend d'**une seule abstraction** (`ICompositeTextExtractor`), agnostique du format.
- **Extensible** sans risque : un nouveau format n'impacte ni le composite ni le pipeline.
- **Échec explicite** sur format inconnu : plus de documents « réussis » mais vides et inutilisables.
- **Numéros de page préservés** quand le format le permet, améliorant la traçabilité des citations.
- Chaque extracteur est **testable en isolation** (un projet de tests couvre chacun d'eux).

### Négatives / points d'attention

- La résolution se fait par **extension de fichier** (`CanHandle`) : un fichier mal nommé ou sans
  extension fiable est mal routé. Une détection par signature (magic bytes) serait plus robuste.
- **Premier extracteur gagnant** (`FirstOrDefault`) : si deux extracteurs revendiquent la même
  extension, l'ordre d'enregistrement DI décide — à surveiller.
- Chaque format ajoute une **dépendance NuGet** (OpenXML, etc.), donc de la surface de maintenance.
- `HtmlTextExtractor` reste un point d'attention historique (placeholder à l'origine) — à vérifier
  qu'il produit bien du texte exploitable.

---

## Alternatives rejetées

| Alternative | Raison du rejet |
|-------------|-----------------|
| Un gros `switch` sur l'extension dans le pipeline | Viole ouvert/fermé ; chaque format toucherait au code du pipeline ; non testable en isolation. |
| Un seul extracteur « universel » (bibliothèque tout-en-un type Tika) | Dépendance lourde (souvent JVM), moins de contrôle sur chaque format, difficile à ajuster finement. |
| Ignorer silencieusement les formats non supportés | Produit des documents vides « réussis » — précisément l'écueil que l'exception explicite évite. |
| Détection par magic bytes d'emblée | Sur-ingénierie au regard du besoin actuel ; la résolution par extension suffit, l'évolution reste possible. |
