using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5040: a range indexer returns the type that declares it. Range indexers are an externally visible indexer whose
/// only parameter is a <c>System.Range</c>, and, in a type without one, an externally visible instance
/// <c>Slice(int, int)</c> method when the type has an <c>int</c> <c>Length</c> or <c>Count</c> property, which C#
/// then uses for range indexing. Members that override or implement another member are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RangeIndexerAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5040);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        string typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        bool hasRangeIndexer = false;
        foreach (IPropertySymbol indexer in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (!indexer.IsIndexer || indexer.Parameters.Length != 1 || !IsRange(indexer.Parameters[0].Type))
            {
                continue;
            }

            hasRangeIndexer = true;
            if (IsReported(indexer, indexer.Type, type) && NamingSymbols.SourceLocation(indexer) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5040, location, typeName, "this[Range]"));
            }
        }

        if (hasRangeIndexer || !HasCountOrLength(type))
        {
            return;
        }

        foreach (IMethodSymbol slice in type.GetMembers("Slice").OfType<IMethodSymbol>())
        {
            if (slice.MethodKind == MethodKind.Ordinary && !slice.IsStatic && slice.Parameters.Length == 2
                && slice.Parameters.All(
                    p => p.RefKind == RefKind.None && p.Type.SpecialType == SpecialType.System_Int32)
                && IsReported(slice, slice.ReturnType, type)
                && NamingSymbols.SourceLocation(slice) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5040, location, typeName, "Slice"));
            }
        }
    }

    private static bool IsReported(ISymbol member, ITypeSymbol returnType, INamedTypeSymbol type)
    {
        return NamingSymbols.IsExternallyVisible(member) && !NamingSymbols.IsInherited(member)
            && !Overloads.SameType(returnType, type);
    }

    private static bool IsRange(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "Range", ContainingNamespace: { } ns, ContainingType: null }
            && ns.ToDisplayString() == "System";
    }

    private static bool HasCountOrLength(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IPropertySymbol>().Any(property =>
                    property.Name is "Length" or "Count" && !property.IsStatic && !property.IsIndexer
                    && property.Type.SpecialType == SpecialType.System_Int32 && property.GetMethod is not null
                    && NamingSymbols.IsExternallyVisible(property.GetMethod)))
            {
                return true;
            }
        }

        return false;
    }
}
