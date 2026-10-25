using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7036 (section 7.2.3): a wrapping exception carries the caught one as its inner exception. A new exception that a
/// throw inside a catch block creates is reported when its type has an accessible constructor with an
/// <see cref="System.Exception"/> parameter and no argument of the creation is an exception. Throws inside a lambda,
/// anonymous method or local function run later and are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InnerExceptionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7036);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? exception = start.Compilation.GetTypeByMetadataName("System.Exception");
            start.RegisterOperationAction(
                operationContext => AnalyzeThrow(operationContext, exception),
                OperationKind.Throw);
        });
    }

    private static void AnalyzeThrow(OperationAnalysisContext context, INamedTypeSymbol? exception)
    {
        var @throw = (IThrowOperation)context.Operation;
        if (Unwrap(@throw.Exception) is not IObjectCreationOperation { Type: INamedTypeSymbol type } creation
            || ExceptionSymbols.EnclosingCatch(@throw.Syntax) is null
            || creation.Arguments.Any(argument => argument.ArgumentKind != ArgumentKind.DefaultValue
                && ExceptionSymbols.DerivesFrom(Unwrap(argument.Value)?.Type, exception))
            || !type.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Any(parameter =>
                    SymbolEqualityComparer.Default.Equals(parameter.Type, exception))
                && context.Compilation.IsSymbolAccessibleWithin(
                    constructor,
                    context.ContainingSymbol.ContainingType ?? (ISymbol)context.Compilation.Assembly)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            FdgDescriptors.FDG7036,
            creation.Syntax.GetLocation(),
            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
