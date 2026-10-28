using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Exceptions;

/// <summary>
/// FDG7021 and FDG7022 (section 7.2.1): exception messages end with a period and contain no question marks or
/// exclamation points. The argument for a <c>message</c> parameter of a constructor of a type derived from
/// <see cref="System.Exception"/>, in a creation or a constructor initializer, is checked when it is a constant
/// string or an interpolated string: FDG7021 when its text does not end with a period (an interpolated string only
/// when it ends with text, and a message that ends with a question mark or exclamation point is left to FDG7022),
/// FDG7022 when its text contains a question mark or exclamation point other than the operators <c>!=</c>,
/// <c>??</c>, <c>?.</c> and <c>?[</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionMessageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG7021, FdgDescriptors.FDG7022);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? exception = start.Compilation.GetTypeByMetadataName("System.Exception");
            start.RegisterOperationAction(
                operationContext => AnalyzeCall(operationContext, exception),
                OperationKind.ObjectCreation,
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeCall(OperationAnalysisContext context, INamedTypeSymbol? exception)
    {
        (IMethodSymbol? constructor, ImmutableArray<IArgumentOperation> arguments) = context.Operation switch
        {
            IObjectCreationOperation creation => (creation.Constructor, creation.Arguments),
            IInvocationOperation { TargetMethod.MethodKind: MethodKind.Constructor } invocation =>
                (invocation.TargetMethod, invocation.Arguments),
            _ => (null, ImmutableArray<IArgumentOperation>.Empty),
        };
        if (constructor is null || !ExceptionSymbols.DerivesFrom(constructor.ContainingType, exception))
        {
            return;
        }

        foreach (IArgumentOperation argument in arguments)
        {
            if (argument is { ArgumentKind: not ArgumentKind.DefaultValue, Parameter: { Name: "message" } parameter }
                && parameter.Type.SpecialType == SpecialType.System_String
                && Text(argument.Value) is ({ } parts, bool endsWithText))
            {
                Check(context, argument.Value.Syntax.GetLocation(), parts, endsWithText);
            }
        }
    }

    private static void Check(
        OperationAnalysisContext context, Location location, List<string> parts, bool endsWithText)
    {
        if (parts.Any(HasQuestionOrExclamation))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7022, location));
        }

        string last = endsWithText ? parts[parts.Count - 1].TrimEnd() : string.Empty;
        if (last.Length > 0 && last[last.Length - 1] is not ('.' or '?' or '!'))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG7021, location));
        }
    }

    /// <summary>
    /// The literal text of a constant string or the text parts of an interpolated string, with whether the string ends
    /// with text; null for other messages.
    /// </summary>
    private static (List<string> Parts, bool EndsWithText)? Text(IOperation value)
    {
        if (value.ConstantValue is { HasValue: true, Value: string constant })
        {
            return ([constant], true);
        }

        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (value is not IInterpolatedStringOperation interpolated)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (IInterpolatedStringContentOperation part in interpolated.Parts)
        {
            if (part is IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } })
            {
                parts.Add(text);
            }
        }

        return (parts, interpolated.Parts.LastOrDefault() is IInterpolatedStringTextOperation);
    }

    private static bool HasQuestionOrExclamation(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (text[i] == '!' && next != '=')
            {
                return true;
            }

            if (text[i] == '?')
            {
                if (next is not ('?' or '.' or '['))
                {
                    return true;
                }

                i++;
            }
        }

        return false;
    }
}
