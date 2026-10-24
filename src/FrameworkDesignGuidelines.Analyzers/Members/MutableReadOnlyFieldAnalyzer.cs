using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5068: an externally visible readonly field does not have a mutable type the book names: an array, a mutable
/// collection of System.Collections, System.Collections.Generic or System.Collections.ObjectModel, a StringBuilder,
/// or a Stream.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableReadOnlyFieldAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] _mutableTypes =
    [
        "System.Collections.ArrayList",
        "System.Collections.Hashtable",
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.Generic.LinkedList`1",
        "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedList`2",
        "System.Collections.Generic.SortedSet`1",
        "System.Collections.ObjectModel.Collection`1",
        "System.Collections.ObjectModel.ObservableCollection`1",
        "System.Text.StringBuilder",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5068);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var mutable = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (string name in _mutableTypes)
            {
                if (start.Compilation.GetTypeByMetadataName(name) is { } type)
                {
                    mutable.Add(type);
                }
            }

            INamedTypeSymbol? stream = start.Compilation.GetTypeByMetadataName("System.IO.Stream");
            start.RegisterSymbolAction(symbol => AnalyzeField(symbol, mutable, stream), SymbolKind.Field);
        });
    }

    private static void AnalyzeField(
        SymbolAnalysisContext context,
        HashSet<INamedTypeSymbol> mutable,
        INamedTypeSymbol? stream)
    {
        var field = (IFieldSymbol)context.Symbol;
        if (field.IsReadOnly && !field.IsImplicitlyDeclared && IsMutable(field.Type, mutable, stream)
            && NamingSymbols.IsExternallyVisible(field)
            && NamingSymbols.SourceLocation(field) is { } location)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG5068,
                location,
                field.Name,
                field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    private static bool IsMutable(ITypeSymbol type, HashSet<INamedTypeSymbol> mutable, INamedTypeSymbol? stream)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (mutable.Contains(named.OriginalDefinition))
        {
            return true;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, stream))
            {
                return true;
            }
        }

        return false;
    }
}
