using System.Collections.Concurrent;
using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Extensibility;

/// <summary>
/// FDG6019: a base class intended for use in public APIs has no "Base" suffix. An unsealed, externally visible base
/// class of the compilation whose last word is <c>Base</c> is reported when an externally visible member uses it in
/// its signature (return, parameter, property, indexer, field or event type, or a constraint of a type parameter,
/// also as an array element type or a type argument), or an externally visible delegate or generic type does.
/// Members that the compiler declares do not count. A class is a base class (section 6.2: designed for inheritance)
/// when it is abstract, declares a protected, virtual or abstract member, or another class of the compilation
/// derives from it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BaseSuffixAnalyzer : DiagnosticAnalyzer
{
    private const string Suffix = "Base";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG6019);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var used = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
            var derived = new ConcurrentDictionary<INamedTypeSymbol, byte>(SymbolEqualityComparer.Default);
            IAssemblySymbol assembly = start.Compilation.Assembly;
            start.RegisterSymbolAction(
                symbol =>
                {
                    if (symbol.Symbol is INamedTypeSymbol { BaseType: { } baseType })
                    {
                        derived.TryAdd(baseType.OriginalDefinition, 0);
                    }

                    // A generic type's constraint on itself (EntityBase<TSelf> where TSelf : EntityBase<TSelf>) only
                    // says how the type is derived from; its type arguments still count.
                    var self = symbol.Symbol as INamedTypeSymbol;
                    foreach (ITypeSymbol type in SignatureTypes(symbol.Symbol))
                    {
                        Collect(type, assembly, used, self);
                    }
                },
                SymbolKind.NamedType,
                SymbolKind.Method,
                SymbolKind.Property,
                SymbolKind.Field,
                SymbolKind.Event);
            start.RegisterCompilationEndAction(
                end => Report(end, used.Keys.Where(type => IsBaseClass(type, derived))));
        });
    }

    /// <summary>
    /// The types in the signature of an externally visible member, delegate or generic type declared in source,
    /// constraints of the type parameters it declares included.
    /// </summary>
    private static IEnumerable<ITypeSymbol> SignatureTypes(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared || !NamingSymbols.IsExternallyVisible(symbol))
        {
            return [];
        }

        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke } type =>
                Parameters(invoke.ReturnType, invoke.Parameters).Concat(Constraints(type.TypeParameters)),
            INamedTypeSymbol type => Constraints(type.TypeParameters),
            IMethodSymbol { MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet
                or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise) } method =>
                Parameters(method.ReturnType, method.Parameters).Concat(Constraints(method.TypeParameters)),
            IPropertySymbol property => Parameters(property.Type, property.Parameters),
            IFieldSymbol field => [field.Type],
            IEventSymbol @event => [@event.Type],
            _ => [],
        };
    }

    private static IEnumerable<ITypeSymbol> Parameters(ITypeSymbol type, ImmutableArray<IParameterSymbol> parameters)
    {
        return parameters.Select(parameter => parameter.Type).Prepend(type);
    }

    private static IEnumerable<ITypeSymbol> Constraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        return typeParameters.SelectMany(typeParameter => typeParameter.ConstraintTypes);
    }

    /// <summary>Adds the candidate classes in <paramref name="type"/>, except <paramref name="self"/> itself.</summary>
    private static void Collect(
        ITypeSymbol type,
        IAssemblySymbol assembly,
        ConcurrentDictionary<INamedTypeSymbol, byte> used,
        INamedTypeSymbol? self)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                Collect(array.ElementType, assembly, used, self);
                break;
            case INamedTypeSymbol named:
                if (!SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, self)
                    && IsCandidate(named.OriginalDefinition, assembly))
                {
                    used.TryAdd(named.OriginalDefinition, 0);
                }

                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    Collect(argument, assembly, used, self);
                }

                break;
        }
    }

    private static bool IsCandidate(INamedTypeSymbol type, IAssemblySymbol assembly)
    {
        if (type.TypeKind != TypeKind.Class || type.IsSealed
            || !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, assembly)
            || !type.Name.EndsWith(Suffix, StringComparison.Ordinal)
            || !NamingSymbols.IsExternallyVisible(type))
        {
            return false;
        }

        List<string> words = NamingSymbols.Words(type.Name);
        return words.Count > 1 && words[words.Count - 1] == Suffix;
    }

    /// <summary>
    /// True for a class designed for inheritance: abstract, with a protected (protected internal, private protected),
    /// virtual or abstract member that the source declares, or the base class of another class of the compilation.
    /// </summary>
    private static bool IsBaseClass(INamedTypeSymbol type, ConcurrentDictionary<INamedTypeSymbol, byte> derived)
    {
        return type.IsAbstract
            || derived.ContainsKey(type)
            || type.GetMembers().Any(member => !member.IsImplicitlyDeclared
                && (member.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal
                        or Accessibility.ProtectedAndInternal
                    || ((member.IsVirtual || member.IsAbstract) && !member.IsOverride)));
    }

    private static void Report(CompilationAnalysisContext context, IEnumerable<INamedTypeSymbol> used)
    {
        foreach (INamedTypeSymbol type in used.OrderBy(
            type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            StringComparer.Ordinal))
        {
            if (NamingSymbols.SourceLocation(type) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG6019,
                    location,
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
