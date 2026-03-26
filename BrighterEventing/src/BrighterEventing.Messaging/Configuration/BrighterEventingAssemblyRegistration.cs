using System.Reflection;
using BrighterEventing.Messaging.Events;

namespace BrighterEventing.Messaging.Configuration;

/// <summary>
/// Resolves which assemblies Brighter should scan for handlers, mappers, and related types.
/// </summary>
public static class BrighterEventingAssemblyRegistration
{
    /// <summary>
    /// When <paramref name="assemblies"/> is non-empty, returns it unchanged.
    /// Otherwise returns <see cref="Assembly.GetEntryAssembly"/> (the host) plus the shared
    /// <c>BrighterEventing.Messaging</c> assembly (shared primitives such as <see cref="DomainBrighterEvent"/>).
    /// This avoids requiring every host to pass <c>typeof(Program).Assembly</c> while still discovering
    /// handlers in the executable. Application-specific events and mappers live in referenced assemblies;
    /// pass them explicitly when they are not the entry assembly.
    /// </summary>
    /// <remarks>
    /// Pass explicit assemblies when handlers live in additional loaded assemblies (e.g. plugins) or in tests
    /// where <see cref="Assembly.GetEntryAssembly"/> is not the application under test.
    /// </remarks>
    public static Assembly[] ResolveAutoFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies.Length > 0)
            return assemblies;

        var messagingAssembly = typeof(DomainBrighterEvent).Assembly;
        var entry = Assembly.GetEntryAssembly();
        if (entry is null)
            return [messagingAssembly];

        return ReferenceEquals(entry, messagingAssembly)
            ? [messagingAssembly]
            : [entry, messagingAssembly];
    }
}
