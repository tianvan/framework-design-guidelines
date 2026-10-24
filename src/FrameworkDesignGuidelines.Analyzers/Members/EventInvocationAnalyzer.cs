using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5061, FDG5062 and FDG5063: a call that raises a field-like event directly (<c>Event(...)</c> or
/// <c>Event?.Invoke(...)</c>), when the event's delegate takes an object sender and a second parameter, passes a
/// non-null sender for an instance event (FDG5061), a null sender for a static event (FDG5062), and non-null event
/// data when the second parameter is an EventArgs or a subclass (FDG5063). A null is a constant null argument; other
/// arguments, variables included, count as non-null.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventInvocationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5061, FdgDescriptors.FDG5062, FdgDescriptors.FDG5063);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName("System.EventArgs") is { } eventArgs)
            {
                start.RegisterOperationAction(
                    operation => AnalyzeInvocation(operation, eventArgs),
                    OperationKind.Invocation);
            }
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol eventArgs)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod is not
                { MethodKind: MethodKind.DelegateInvoke, Parameters: { Length: 2 } parameters }
            || parameters[0].Type.SpecialType != SpecialType.System_Object
            || RaisedEvent(invocation.Instance) is not { } @event)
        {
            return;
        }

        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.Parameter is null)
            {
                continue;
            }

            bool isNull = argument.Value.ConstantValue is { HasValue: true, Value: null };
            DiagnosticDescriptor? descriptor = argument.Parameter.Ordinal switch
            {
                0 when @event.IsStatic && !isNull => FdgDescriptors.FDG5062,
                0 when !@event.IsStatic && isNull => FdgDescriptors.FDG5061,
                1 when isNull && DerivesFrom(argument.Parameter.Type, eventArgs) => FdgDescriptors.FDG5063,
                _ => null,
            };
            if (descriptor is not null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(descriptor, argument.Value.Syntax.GetLocation(), @event.Name));
            }
        }
    }

    /// <summary>
    /// The event whose delegate <paramref name="instance"/> is: an event reference, or the receiver of a conditional
    /// access on one.
    /// </summary>
    private static IEventSymbol? RaisedEvent(IOperation? instance)
    {
        if (instance is IConditionalAccessInstanceOperation)
        {
            for (IOperation? parent = instance.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is IConditionalAccessOperation access)
                {
                    instance = access.Operation;
                    break;
                }
            }
        }

        return (instance as IEventReferenceOperation)?.Event;
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
