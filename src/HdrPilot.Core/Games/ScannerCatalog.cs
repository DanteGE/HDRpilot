using System.Reflection;
using HdrPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HdrPilot.Core.Games;

/// <summary>
/// Findet alle Scanner im Kern per Reflexion. Vertrag für jeden Scanner: öffentliche, nicht abstrakte Klasse,
/// die <see cref="IGameScanner"/> implementiert und einen öffentlichen Konstruktor
/// <c>(IFileSystem, IRegistry, IAppEnvironment, ILogger? logger = null)</c> hat.
/// So kann ein neuer Scanner hinzukommen, ohne eine zentrale Liste zu ändern.
/// </summary>
public static class ScannerCatalog
{
    public static IReadOnlyList<Type> ScannerTypes { get; } = typeof(ScannerCatalog).Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IGameScanner).IsAssignableFrom(t))
        .Where(t => FindConstructor(t) is not null)
        .ToList();

    public static IReadOnlyList<IGameScanner> CreateAll(
        IFileSystem fileSystem, IRegistry registry, IAppEnvironment environment, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        return ScannerTypes
            .Select(t => (IGameScanner)FindConstructor(t)!.Invoke([fileSystem, registry, environment, loggerFactory.CreateLogger(t.Name)]))
            .OrderBy(s => s.Source)
            .ToList();
    }

    private static ConstructorInfo? FindConstructor(Type type) => type.GetConstructors().FirstOrDefault(c =>
    {
        var p = c.GetParameters();
        return p.Length == 4
            && p[0].ParameterType == typeof(IFileSystem)
            && p[1].ParameterType == typeof(IRegistry)
            && p[2].ParameterType == typeof(IAppEnvironment)
            && p[3].ParameterType == typeof(ILogger);
    });
}
