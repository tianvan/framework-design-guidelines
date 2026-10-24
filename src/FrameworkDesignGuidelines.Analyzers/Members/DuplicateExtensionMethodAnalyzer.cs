using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5077: no two externally visible extension methods of the compilation, in different classes, have the same name,
/// number of type parameters and parameter types. Each one is reported, with the class of another.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateExtensionMethodAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5077);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var methods = new ConcurrentBag<IMethodSymbol>();
            start.RegisterSymbolAction(
                symbol =>
                {
                    var method = (IMethodSymbol)symbol.Symbol;
                    if (method.IsExtensionMethod && NamingSymbols.IsExternallyVisible(method))
                    {
                        methods.Add(method);
                    }
                },
                SymbolKind.Method);
            start.RegisterCompilationEndAction(end => Report(end, methods));
        });
    }

    private static void Report(CompilationAnalysisContext context, IEnumerable<IMethodSymbol> methods)
    {
        foreach (IGrouping<string, IMethodSymbol> group in methods.GroupBy(Signature))
        {
            IMethodSymbol[] same = group.ToArray();
            foreach (IMethodSymbol method in same)
            {
                if (same.FirstOrDefault(other => !SymbolEqualityComparer.Default.Equals(
                        other.ContainingType, method.ContainingType)) is { } other
                    && NamingSymbols.SourceLocation(method) is { } location)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        FdgDescriptors.FDG5077,
                        location,
                        method.Name,
                        other.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            }
        }
    }

    /// <summary>The name, type parameter count and parameter types, with method type parameters by position.</summary>
    private static string Signature(IMethodSymbol method)
    {
        var builder = new StringBuilder(method.Name).Append('`').Append(method.TypeParameters.Length).Append('(');
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            builder.Append(parameter.RefKind).Append(' ');
            AppendType(builder, parameter.Type);
            builder.Append(',');
        }

        return builder.Append(')').ToString();
    }

    private static void AppendType(StringBuilder builder, ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter:
                builder.Append("!!").Append(parameter.Ordinal);
                break;
            case IArrayTypeSymbol array:
                AppendType(builder, array.ElementType);
                builder.Append('[').Append(array.Rank).Append(']');
                break;
            case INamedTypeSymbol { IsGenericType: true } named:
                builder.Append(named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append('<');
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    AppendType(builder, argument);
                    builder.Append(',');
                }

                builder.Append('>');
                break;
            default:
                builder.Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                break;
        }
    }
}
