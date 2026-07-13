using AIExperience.Rag.Application;
using AIExperience.Rag.Application.Common;
using AIExperience.Rag.Application.Common.Cqrs;
using AIExperience.Rag.Application.Document.Command;
using AIExperience.Rag.Domain.Enums;
using AIExperience.Rag.Domain.Interfaces.Repositories;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Domain.Interfaces.Services.AI;
using AIExperience.Rag.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
var services = builder.Services;
var config = builder.Configuration;
//services.AddOpenApi();

services
    .AddInfrastructure(config)
    .AddApplication();


var app = builder.Build();

var ingestionService = app.Services.GetRequiredService<IIngestionService>();
var dispatcher = app.Services.GetRequiredService<ICommandDispatcher>();
var ragPipelineService = app.Services.GetRequiredService<IRagPipelineService>();
var documentRepository = app.Services.GetRequiredService<IDocumentRepository>();
var contentTypeResolver = app.Services.GetRequiredService<IContentTypeResolver>();

var ingestedDocumentIds = new List<Guid>();
var UserId = app.Services.GetRequiredService<IOptions<DevAuthOptions>>().Value.DefaultUserId;

Console.WriteLine("=== Exemple RAG ===");

while (true)
{
    Console.WriteLine("\n--- Menu ---");
    Console.WriteLine("  1. Ingérer des documents");
    Console.WriteLine("  2. Poser une question (session courante)");
    Console.WriteLine("  3. Charger des documents déjà ingérés");
    Console.WriteLine("  0. Quitter");
    Console.Write("> Choix : ");

    var choice = Console.ReadLine()?.Trim();

    switch (choice)
    {
        case "1":
            await IngestDocumentsAsync();
            break;

        case "2":
            await AskQuestionAsync();
            break;

        case "3":
            await LoadExistingDocumentsAsync();
            break;

        case "0":
        case "exit":
            return;

        default:
            Console.WriteLine("Choix invalide, veuillez réessayer.");
            break;
    }
}

async Task IngestDocumentsAsync()
{
    Console.Write("> Chemin du dossier contenant les PDFs : ");
    var folderPath = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
    {
        Console.WriteLine("Chemin invalide ou dossier introuvable.");
        return;
    }

    var files = Directory.GetFiles(folderPath, "*.pdf");
    if (files.Length == 0)
    {
        Console.WriteLine("Aucun fichier PDF trouvé dans ce dossier.");
        return;
    }

    var ingestedCount = 0;

    foreach (var filePath in files)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var metadata = new DocumentMetadata { Title = fileInfo.Name };

            var uploadDocumentResponse = await dispatcher.SendAsync(new UploadDocumentCommand
            {
                FileName = fileInfo.Name,
                ContentType = contentTypeResolver.Resolve(fileInfo.Name),
                FileSizeBytes = fileInfo.Length,
                UserId = UserId,
                DocumentMetadata = metadata,
                ChunkingStrategy = ChunkingStrategy.Recursive,
                FilePath = filePath
            });

            if (uploadDocumentResponse.Status == IngestionStatus.Completed)
            {
                await ingestionService.IngestAsync(filePath, uploadDocumentResponse.DocumentId, metadata);
                ingestedCount++;
                ingestedDocumentIds.Add(uploadDocumentResponse.DocumentId);
                Console.WriteLine($"  ✔ {fileInfo.Name} ingéré.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✘ Erreur sur {Path.GetFileName(filePath)} : {ex.Message}");
        }
    }

    Console.WriteLine($"\n{ingestedCount} document(s) ingéré(s) avec succès.");
}

async Task LoadExistingDocumentsAsync()
{
    using var scope = app.Services.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

    var (documents, total) = await repo.GetByUserIdAsync(UserId, page: 1, pageSize: 100);

    var list = documents.ToList();
    if (list.Count == 0)
    {
        Console.WriteLine("Aucun document trouvé en base de données.");
        return;
    }

    Console.WriteLine($"\n{total} document(s) trouvé(s) :\n");
    for (int i = 0; i < list.Count; i++)
        Console.WriteLine($"  [{i + 1}] {list[i].FileName} (id: {list[i].Id})");

    Console.Write("\nEntrez les numéros à charger séparés par des virgules (ex: 1,2,3) ou 'tous' : ");
    var input = Console.ReadLine()?.Trim();

    if (string.IsNullOrWhiteSpace(input)) return;

    if (input.Equals("tous", StringComparison.OrdinalIgnoreCase))
    {
        ingestedDocumentIds.AddRange(list.Select(d => d.Id));
        Console.WriteLine($"  ✔ {list.Count} document(s) chargé(s).");
        return;
    }

    foreach (var part in input.Split(','))
    {
        if (int.TryParse(part.Trim(), out var index) && index >= 1 && index <= list.Count)
        {
            var doc = list[index - 1];
            if (!ingestedDocumentIds.Contains(doc.Id))
            {
                ingestedDocumentIds.Add(doc.Id);
                Console.WriteLine($"  ✔ {doc.FileName} chargé.");
            }
        }
        else
        {
            Console.WriteLine($"  ✘ Numéro '{part.Trim()}' invalide, ignoré.");
        }
    }

    Console.WriteLine($"\n{ingestedDocumentIds.Count} document(s) disponible(s) pour les questions.");
}

async Task AskQuestionAsync()
{
    if (ingestedDocumentIds.Count == 0)
    {
        Console.WriteLine("Aucun document ingéré. Veuillez d'abord ingérer des documents (option 1).");
        return;
    }

    Console.Write("? Question : ");
    var question = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(question))
    {
        Console.WriteLine("Question vide.");
        return;
    }

    try
    {
        var response = await ragPipelineService.AskAsync(new AIExperience.Rag.Domain.Models.RagQuery
        {
            Question = question,
            Strategy = RagStrategy.Adaptive,
            DocumentIds = ingestedDocumentIds
        }, CancellationToken.None);

        Console.WriteLine($"\n=== Réponse ===\n{response.Answer}");

        if (response.Citations?.Count > 0)
        {
            Console.WriteLine("\n--- Sources ---");
            foreach (var citation in response.Citations)
                Console.WriteLine($"  • {citation.DocumentName} (page {citation.PageNumber}) : {citation.Excerpt}");
        }

        Console.WriteLine($"\n[Stratégie: {response.StrategyUsed} | Tokens: {response.TotalTokens} | Durée: {response.DurationMs} ms]");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erreur lors de la question : {ex.Message}");
    }
}
