using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7010 (section 7.1): exception filters do not throw. A throw expression in the when clause of a catch is
/// reported, unless it is inside a lambda, anonymous method or local function, which runs later. Exceptions that the
/// members a filter calls throw cannot be seen.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionFilterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7010);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        if (ExceptionSymbols.IsInFilter(context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7010, context.Node.GetLocation()));
        }
    }
}
