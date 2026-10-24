using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5074: an externally visible extension method does not extend System.Object. FDG5075: an externally visible
/// static class that declares extension methods is not named "[ExtendedType]Extensions": "Extensions" after nothing or
/// after the name of a type that one of its methods extends (for an interface, also without its leading I). FDG5076:
/// an externally visible extension method is not in the namespace of the type it extends, unless that type is an interface, is declared in another
/// assembly, or the method restricts its generic type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExtensionMethodAnalyzer : DiagnosticAnalyzer
{
    private const string Suffix = "Extensions";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5074, FdgDescriptors.FDG5075, FdgDescriptors.FDG5076);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.IsExtensionMethod || !NamingSymbols.IsExternallyVisible(method)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        ITypeSymbol extended = method.Parameters[0].Type;
        if (extended.SpecialType == SpecialType.System_Object)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5074, location, method.Name));
        }

        if (extended is INamedTypeSymbol { TypeKind: not TypeKind.Interface } named
            && SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, method.ContainingAssembly)
            && SymbolEqualityComparer.Default.Equals(
                named.ContainingNamespace, method.ContainingType.ContainingNamespace)
            && !RestrictsGenericType(method, named))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG5076,
                location,
                method.Name,
                named.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsStatic && type.Name.EndsWith(Suffix, StringComparison.Ordinal)
            && IsNamedForExtendedType(type, type.Name.Substring(0, type.Name.Length - Suffix.Length))
            && NamingSymbols.IsExternallyVisible(type)
            && NamingSymbols.SourceLocation(type) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5075, location, type.Name));
        }
    }

    private static bool IsNamedForExtendedType(INamedTypeSymbol type, string prefix)
    {
        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (!method.IsExtensionMethod)
            {
                continue;
            }

            if (prefix.Length == 0)
            {
                return true;
            }

            ITypeSymbol extended = method.Parameters[0].Type;
            string name = extended.Name;
            if (name == prefix
                || (extended.TypeKind == TypeKind.Interface && name.Length > 1 && name[0] == 'I'
                    && char.IsUpper(name[1]) && name.Substring(1) == prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="method"/> extends a generic type more narrowly than the type itself: a type argument
    /// is not a type parameter of the method (<c>ReadOnlyMemory&lt;char&gt;</c>), or it is one with constraints.
    /// </summary>
    private static bool RestrictsGenericType(IMethodSymbol method, INamedTypeSymbol extended)
    {
        foreach (ITypeSymbol argument in extended.TypeArguments)
        {
            if (argument is not ITypeParameterSymbol parameter
                || !SymbolEqualityComparer.Default.Equals(parameter.DeclaringMethod, method)
                || HasConstraints(parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConstraints(ITypeParameterSymbol parameter)
    {
        return parameter.HasReferenceTypeConstraint || parameter.HasValueTypeConstraint
            || parameter.HasUnmanagedTypeConstraint || parameter.HasNotNullConstraint
            || parameter.HasConstructorConstraint || !parameter.ConstraintTypes.IsEmpty;
    }
}
