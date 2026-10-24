using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4062: a hand-written <c>AssemblyFileVersionAttribute</c> has four dot-separated numbers. What each number means
/// is not checked. The attribute the SDK generates is in generated code and is not analyzed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileVersionFormatAnalyzer : DiagnosticAnalyzer
{
    private static readonly Regex _fourNumbers = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4062);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (attribute.Parent is not AttributeListSyntax { Target.Identifier.ValueText: "assembly" }
            || attribute.ArgumentList is not { Arguments.Count: 1 } arguments
            || context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol
                {
                    ContainingType: { Name: "AssemblyFileVersionAttribute" } attributeClass,
                }
            || attributeClass.ContainingNamespace?.ToDisplayString() != "System.Reflection"
            || context.SemanticModel.GetConstantValue(arguments.Arguments[0].Expression, context.CancellationToken)
                is not { HasValue: true, Value: string version }
            || _fourNumbers.IsMatch(version))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4062, attribute.GetLocation(), version));
    }
}
