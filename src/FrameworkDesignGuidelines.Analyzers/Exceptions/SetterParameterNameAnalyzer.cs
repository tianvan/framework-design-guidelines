using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7044 (section 7.3.4): argument exceptions thrown by setters name the parameter <c>value</c>. A constant passed to
/// the <c>paramName</c> parameter of an exception derived from <see cref="System.ArgumentException"/>, created in a
/// property or indexer setter or init accessor, is reported when it is neither "value" nor a parameter of the indexer.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetterParameterNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7044);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? argumentException = start.Compilation.GetTypeByMetadataName("System.ArgumentException");
            start.RegisterOperationAction(
                operationContext => AnalyzeCreation(operationContext, argumentException),
                OperationKind.ObjectCreation);
        });
    }

    private static void AnalyzeCreation(OperationAnalysisContext context, INamedTypeSymbol? argumentException)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (context.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.PropertySet } setter
            || !ExceptionSymbols.DerivesFrom(creation.Type, argumentException))
        {
            return;
        }

        foreach (IArgumentOperation argument in creation.Arguments)
        {
            if (argument.Parameter is { Name: "paramName", Type.SpecialType: SpecialType.System_String }
                && argument.Value.ConstantValue is { HasValue: true, Value: string name }
                && !setter.Parameters.Any(parameter => parameter.Name == name))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG7044, argument.Value.Syntax.GetLocation(), name));
            }
        }
    }
}
