using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// Shared symbol and name helpers for the chapter 3 analyzers, which check only names that are visible outside the
/// assembly and that the declaration chooses itself.
/// </summary>
internal static class NamingSymbols
{
    /// <summary>The symbol kinds whose declarations <see cref="DeclaredNames"/> walks.</summary>
    public static readonly SymbolKind[] DeclarationKinds =
    [
        SymbolKind.Namespace,
        SymbolKind.NamedType,
        SymbolKind.Method,
        SymbolKind.Property,
        SymbolKind.Event,
        SymbolKind.Field,
    ];

    /// <summary>
    /// True when the symbol and every type that contains it are public, or protected or protected internal in a type
    /// that can be derived from (a protected member of a sealed type cannot be reached from outside). A parameter
    /// or type parameter is visible when the symbol that declares it is, and a namespace when it holds a visible type
    /// declared in source.
    /// </summary>
    public static bool IsExternallyVisible(ISymbol symbol)
    {
        if (symbol is INamespaceSymbol @namespace)
        {
            return @namespace.GetTypeMembers()
                    .Any(type => type.Locations.Any(location => location.IsInSource) && IsExternallyVisible(type))
                || @namespace.GetNamespaceMembers().Any(IsExternallyVisible);
        }

        if (symbol is IParameterSymbol or ITypeParameterSymbol)
        {
            return symbol.ContainingSymbol is { } owner && IsExternallyVisible(owner);
        }

        for (ISymbol? current = symbol; current is not null && current.Kind != SymbolKind.Namespace;
            current = current.ContainingSymbol)
        {
            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    break;
                case Accessibility.Protected or Accessibility.ProtectedOrInternal
                    when current.ContainingType is { IsSealed: false }:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The names that the declaration of <paramref name="symbol"/> chooses, with whether each one is camel-cased:
    /// the symbol itself, and the parameters and type parameters it declares. Names that an override or an interface
    /// implementation takes from the member it overrides or implements are left out, as are implicit declarations,
    /// accessors and other compiler-named methods.
    /// </summary>
    public static IEnumerable<(ISymbol Symbol, bool IsCamelCase)> DeclaredNames(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared || !IsExternallyVisible(symbol) || IsInherited(symbol))
        {
            yield break;
        }

        switch (symbol)
        {
            case INamespaceSymbol { IsGlobalNamespace: false }:
                yield return (symbol, false);
                break;
            case INamedTypeSymbol type:
                yield return (type, false);
                foreach (ITypeParameterSymbol parameter in type.TypeParameters)
                {
                    yield return (parameter, false);
                }

                if (type.DelegateInvokeMethod is { } invoke)
                {
                    foreach (IParameterSymbol parameter in invoke.Parameters)
                    {
                        yield return (parameter, true);
                    }
                }

                break;
            case IMethodSymbol method:
                if (method.MethodKind == MethodKind.Ordinary)
                {
                    yield return (method, false);
                    foreach (ITypeParameterSymbol parameter in method.TypeParameters)
                    {
                        yield return (parameter, false);
                    }
                }

                if (method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.UserDefinedOperator
                    or MethodKind.Conversion)
                {
                    foreach (IParameterSymbol parameter in method.Parameters)
                    {
                        yield return (parameter, true);
                    }
                }

                break;
            case IPropertySymbol property:
                if (!property.IsIndexer && !IsPositionalProperty(property))
                {
                    yield return (property, false);
                }

                foreach (IParameterSymbol parameter in property.Parameters)
                {
                    yield return (parameter, true);
                }

                break;
            case IEventSymbol or IFieldSymbol:
                yield return (symbol, false);
                break;
        }
    }

    /// <summary>
    /// True for the property a record declares through a parameter of its primary constructor: the name is the
    /// parameter's, which is already reported once.
    /// </summary>
    public static bool IsPositionalProperty(IPropertySymbol property)
    {
        return property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ParameterSyntax);
    }

    /// <summary>The first source location of the symbol, where its diagnostics are reported.</summary>
    public static Location? SourceLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault(location => location.IsInSource);
    }

    /// <summary>
    /// The delegate types, as original definitions, that events declared in the compilation's own source use.
    /// </summary>
    public static HashSet<INamedTypeSymbol> EventDelegates(Compilation compilation)
    {
        var delegates = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var namespaces = new Stack<INamespaceSymbol>();
        var types = new Stack<INamedTypeSymbol>();
        namespaces.Push(compilation.Assembly.GlobalNamespace);
        while (namespaces.Count > 0)
        {
            INamespaceSymbol @namespace = namespaces.Pop();
            foreach (INamespaceSymbol child in @namespace.GetNamespaceMembers())
            {
                namespaces.Push(child);
            }

            foreach (INamedTypeSymbol type in @namespace.GetTypeMembers())
            {
                types.Push(type);
            }
        }

        while (types.Count > 0)
        {
            INamedTypeSymbol type = types.Pop();
            foreach (INamedTypeSymbol nested in type.GetTypeMembers())
            {
                types.Push(nested);
            }

            foreach (IEventSymbol @event in type.GetMembers().OfType<IEventSymbol>())
            {
                if (@event.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } eventType)
                {
                    delegates.Add(eventType.OriginalDefinition);
                }
            }
        }

        return delegates;
    }

    /// <summary>
    /// True for an override, an explicit interface implementation, or a member that implicitly implements an
    /// interface member, static abstract and static virtual members included: its name comes from that member.
    /// </summary>
    public static bool IsInherited(ISymbol symbol)
    {
        if (symbol.IsOverride)
        {
            return true;
        }

        bool isExplicit = symbol switch
        {
            IMethodSymbol method => !method.ExplicitInterfaceImplementations.IsEmpty,
            IPropertySymbol property => !property.ExplicitInterfaceImplementations.IsEmpty,
            IEventSymbol @event => !@event.ExplicitInterfaceImplementations.IsEmpty,
            _ => false,
        };
        if (isExplicit)
        {
            return true;
        }

        if (symbol is not (IMethodSymbol or IPropertySymbol or IEventSymbol) || symbol.ContainingType is not { } type)
        {
            return false;
        }

        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            foreach (ISymbol member in @interface.GetMembers(symbol.Name))
            {
                if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Splits an identifier into its words: a word starts at an uppercase letter that follows a lowercase letter,
    /// or that is followed by a lowercase letter after other uppercase letters (<c>HTMLElement</c> is <c>HTML</c>,
    /// <c>Element</c>). Characters other than letters separate words and belong to none.
    /// </summary>
    public static List<string> Words(string name)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= name.Length; i++)
        {
            char c = i < name.Length ? name[i] : ' ';
            if (!char.IsLetter(c))
            {
                if (start >= 0)
                {
                    words.Add(name.Substring(start, i - start));
                    start = -1;
                }

                continue;
            }

            if (start < 0)
            {
                start = i;
            }
            else if (char.IsUpper(c)
                && (char.IsLower(name[i - 1])
                    || (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                words.Add(name.Substring(start, i - start));
                start = i;
            }
        }

        return words;
    }
}
