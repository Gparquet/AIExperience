using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace AIExperience.Rag.Application.Common.Cqrs;

/// <summary>Enregistrement DI du socle CQRS maison
public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le dispatcher, la chaîne de maillons transverses par défaut, et découvre par
    /// réflexion tous les handlers et validateurs de commande présents dans <paramref name="assembly"/>.
    /// </summary>
    /// <remarks>
    /// L'ORDRE D'ENREGISTREMENT des maillons définit l'ordre d'EXÉCUTION : le premier enregistré est
    /// le plus externe. Ici, Logging enveloppe Validation, pour que les échecs de validation soient
    /// eux aussi journalisés.
    /// </remarks>
    public static IServiceCollection AddCqrs(this IServiceCollection services, Assembly assembly)
    {
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();

        services.AddScoped(typeof(ICommandPipelineStep<,>), typeof(LoggingStep<,>));
        services.AddScoped(typeof(ICommandPipelineStep<,>), typeof(ValidationStep<,>));

        RegisterImplementationsOf(services, assembly, typeof(ICommandHandler<,>));
        RegisterImplementationsOf(services, assembly, typeof(ICommandValidator<>));

        return services;
    }

    /// <summary>Enregistre chaque classe concrète de l'assembly qui implémente l'interface générique ouverte donnée, sous cette interface fermée.</summary>
    private static void RegisterImplementationsOf(IServiceCollection services, Assembly assembly, Type openGenericInterface)
    {
        var concreteTypes = assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true });

        foreach (var type in concreteTypes)
        {
            var matchingInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

            foreach (var closedInterface in matchingInterfaces)
                services.AddScoped(closedInterface, type);
        }
    }
}
