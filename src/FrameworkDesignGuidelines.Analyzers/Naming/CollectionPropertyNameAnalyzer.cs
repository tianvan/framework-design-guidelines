using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3068: an externally visible property of a collection type (an array, or a type other than <c>string</c> that
/// implements <c>IEnumerable</c>) does not end with the word <c>List</c> or <c>Collection</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CollectionPropertyNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3068);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.IsIndexer || property.IsImplicitlyDeclared || !NamingSymbols.IsExternallyVisible(property)
            || NamingSymbols.IsInherited(property) || !IsCollection(property.Type))
        {
            return;
        }

        List<string> words = NamingSymbols.Words(property.Name);
        if (words.Count > 1 && words[words.Count - 1] is "List" or "Collection"
            && NamingSymbols.SourceLocation(property) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG3068, location, property.Name));
        }
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        return type.SpecialType == SpecialType.System_Collections_IEnumerable
            || type.AllInterfaces.Any(@interface => @interface.SpecialType == SpecialType.System_Collections_IEnumerable);
    }
}
