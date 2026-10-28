using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8017, FDG8050, FDG8053 and FDG8057 (sections 8.3, 8.5 and 8.6): the interfaces that a class or struct declared
/// in source implements. FDG8017 reports a type that implements both <c>IEnumerable</c> and <c>IEnumerator</c>
/// (the generic interfaces imply them) when its base class does not already. FDG8050 reports a type that lists
/// <c>ICloneable</c>, or an interface derived from it, in its own base list. FDG8053 reports a struct with an
/// instance field that does not implement <c>IEquatable&lt;T&gt;</c> of itself; ref structs, which cannot be boxed,
/// enumerator structs, which are not compared, and structs without instance fields, whose values are all equal, are
/// left out. FDG8057 reports a type that implements <c>IComparable&lt;T&gt;</c>, when
/// its base class does not, without <c>IEquatable&lt;T&gt;</c> for the same T.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InterfaceImplementationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG8017,
            FdgDescriptors.FDG8050,
            FdgDescriptors.FDG8053,
            FdgDescriptors.FDG8057);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            Compilation compilation = start.Compilation;
            INamedTypeSymbol enumerable = compilation.GetSpecialType(SpecialType.System_Collections_IEnumerable);
            INamedTypeSymbol enumerator = compilation.GetSpecialType(SpecialType.System_Collections_IEnumerator);
            INamedTypeSymbol? cloneable = compilation.GetTypeByMetadataName("System.ICloneable");
            INamedTypeSymbol? equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");
            INamedTypeSymbol? comparable = compilation.GetTypeByMetadataName("System.IComparable`1");
            start.RegisterSymbolAction(
                symbolContext =>
                {
                    if (symbolContext.Symbol is not INamedTypeSymbol
                        {
                            TypeKind: TypeKind.Class or TypeKind.Struct, IsImplicitlyDeclared: false,
                        } type
                        || NamingSymbols.SourceLocation(type) is not { } location)
                    {
                        return;
                    }

                    string name = UsageSymbols.Display(type);
                    if (IsCollectionAndEnumerator(type, enumerable, enumerator)
                        && !IsCollectionAndEnumerator(type.BaseType, enumerable, enumerator))
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8017, location, name));
                    }

                    if (type.Interfaces.Any(@interface => UsageSymbols.Implements(@interface, cloneable)))
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8050, location, name));
                    }

                    if (type is { TypeKind: TypeKind.Struct, IsRefLikeType: false }
                        && type.GetMembers().Any(member => member is IFieldSymbol { IsStatic: false })
                        && !UsageSymbols.Implements(type, enumerator)
                        && !UsageSymbols.IsEquatableToItself(type, equatable))
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8053, location, name));
                    }

                    INamedTypeSymbol? unmatched = UnmatchedComparable(type, comparable, equatable);
                    if (unmatched is not null)
                    {
                        symbolContext.ReportDiagnostic(Diagnostic.Create(
                            FdgDescriptors.FDG8057,
                            location,
                            name,
                            UsageSymbols.Display(unmatched.TypeArguments[0])));
                    }
                },
                SymbolKind.NamedType);
        });
    }

    private static bool IsCollectionAndEnumerator(
        INamedTypeSymbol? type, INamedTypeSymbol enumerable, INamedTypeSymbol enumerator)
    {
        return UsageSymbols.Implements(type, enumerable) && UsageSymbols.Implements(type, enumerator);
    }

    /// <summary>
    /// The first <c>IComparable&lt;T&gt;</c> that the type brings in, rather than its base class, without
    /// <c>IEquatable&lt;T&gt;</c> for the same T.
    /// </summary>
    private static INamedTypeSymbol? UnmatchedComparable(
        INamedTypeSymbol type, INamedTypeSymbol? comparable, INamedTypeSymbol? equatable)
    {
        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            if (!UsageSymbols.Is(@interface, comparable)
                || type.BaseType?.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default) == true)
            {
                continue;
            }

            ITypeSymbol compared = @interface.TypeArguments[0];
            if (!type.AllInterfaces.Any(other => UsageSymbols.Is(other, equatable)
                && SymbolEqualityComparer.Default.Equals(other.TypeArguments[0], compared)))
            {
                return @interface;
            }
        }

        return null;
    }
}
