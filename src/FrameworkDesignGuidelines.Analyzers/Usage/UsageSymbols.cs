using Microsoft.CodeAnalysis;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>Helpers shared by the usage analyzers of chapter 8.</summary>
internal static class UsageSymbols
{
    /// <summary>
    /// True when <paramref name="type"/> is <paramref name="definition"/>, or a type constructed from it, or implements
    /// it, directly or through a base type or another interface.
    /// </summary>
    public static bool Implements(ITypeSymbol? type, INamedTypeSymbol? definition)
    {
        return type is not null
            && definition is not null
            && (Is(type, definition) || type.AllInterfaces.Any(@interface => Is(@interface, definition)));
    }

    /// <summary>
    /// True when <paramref name="type"/> is <paramref name="definition"/> or a type constructed from it.
    /// </summary>
    public static bool Is(ITypeSymbol? type, INamedTypeSymbol? definition)
    {
        return type is not null
            && definition is not null
            && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, definition);
    }

    /// <summary>
    /// True when <paramref name="type"/> implements <c>IEquatable&lt;T&gt;</c> with itself as T.
    /// </summary>
    public static bool IsEquatableToItself(INamedTypeSymbol type, INamedTypeSymbol? equatable)
    {
        return type.AllInterfaces.Any(@interface => Is(@interface, equatable)
            && SymbolEqualityComparer.Default.Equals(@interface.TypeArguments[0], type));
    }

    /// <summary>
    /// True when the class, or one of its base classes declared in source, has an instance field that is neither
    /// readonly nor const. The field of an auto-property with a set accessor counts; the field of an init-only
    /// auto-property is readonly, and the field of an event does not count.
    /// </summary>
    public static bool HasMutableField(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type;
            current is not null && current.Locations.Any(location => location.IsInSource);
            current = current.BaseType)
        {
            if (current.GetMembers().Any(member => member is IFieldSymbol
                {
                    IsStatic: false,
                    IsConst: false,
                    IsReadOnly: false,
                    AssociatedSymbol: not IEventSymbol,
                }))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name of the type as diagnostics show it, such as <c>Dictionary&lt;string, int&gt;</c>, without a nullable
    /// annotation at the top level.
    /// </summary>
    public static string Display(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.None)
            .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
