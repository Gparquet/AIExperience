using AIExperience.Rag.Application;
using AIExperience.Rag.Domain.Interfaces.Services;
using AIExperience.Rag.Infrastructure;
using AIExperience.Web.Api.ExceptionHandling;
using AIExperience.Web.Api.Hubs;
using AIExperience.Web.Api.Notifications;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IIngestionNotifier, SignalRIngestionNotifier>();

// Traduit les CommandValidationException (échec de validation d'une commande CQRS) en 400
// ValidationProblemDetails, plutôt que de les laisser remonter en 500 non gérées.
builder.Services.AddExceptionHandler<CommandValidationExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddApplication();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              // Nécessaire pour le repli en long-polling de SignalR si les WebSockets sont indisponibles.
              .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// En production uniquement — évite le problème de certificat auto-signé entre Vite et Kestrel en dev
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseCors();
app.MapControllers();
app.MapHub<IngestionHub>("/hubs/ingestion");

app.Run();
