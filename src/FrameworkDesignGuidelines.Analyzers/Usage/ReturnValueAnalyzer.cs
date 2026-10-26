using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8026 and FDG8082 (sections 8.3.2 and 8.9.3): values that return statements and expression bodies return. A
/// value counts as null when it is a null or default literal, directly, through a conversion or as a branch of a
/// conditional expression. FDG8026 reports such a value returned from a method, local function, property getter or
/// indexer getter as an array or a collection: <c>IEnumerable</c>, <c>IEnumerable&lt;T&gt;</c>, or a type that
/// implements <c>ICollection</c>, <c>ICollection&lt;T&gt;</c> or <c>IReadOnlyCollection&lt;T&gt;</c> and is, or
/// derives from, a type of the System.Collections namespaces. Other types that implement those interfaces, such as
/// the nodes of a document tree, are not collections first, and null is a usual value for them. The type is the one
/// the value is converted to, which is the T of a returned <c>Task&lt;T&gt;</c> in an async method. FDG8082 reports
/// such a value, <c>""</c> or <c>string.Empty</c>, returned from an override of <c>Object.ToString()</c>. Lambdas
/// and anonymous methods are not checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReturnValueAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG8026, FdgDescriptors.FDG8082);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            Compilation compilation = start.Compilation;
            INamedTypeSymbol?[] collections =
            [
                compilation.GetTypeByMetadataName("System.Collections.ICollection"),
                compilation.GetSpecialType(SpecialType.System_Collections_Generic_ICollection_T),
                compilation.GetSpecialType(SpecialType.System_Collections_Generic_IReadOnlyCollection_T),
            ];
            start.RegisterOperationAction(
                operationContext => AnalyzeReturn(operationContext, collections),
                OperationKind.Return);
        });
    }

    private static void AnalyzeReturn(OperationAnalysisContext context, INamedTypeSymbol?[] collections)
    {
        if (context.Operation is not IReturnOperation { ReturnedValue: { Type: { } type } value }
            || Function(context) is not { } function)
        {
            return;
        }

        if (function is { MethodKind: MethodKind.Ordinary, Name: nameof(ToString), IsOverride: true }
            && function.Parameters.IsEmpty
            && function.ReturnType.SpecialType == SpecialType.System_String)
        {
            foreach (IOperation leaf in Leaves(value).Where(leaf => IsNull(leaf) || IsEmptyString(leaf)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG8082,
                    leaf.Syntax.GetLocation(),
                    UsageSymbols.Display(function.ContainingType)));
            }

            return;
        }

        if (function.MethodKind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation
                or MethodKind.LocalFunction or MethodKind.PropertyGet
            && IsCollection(type, collections))
        {
            string name = function.AssociatedSymbol switch
            {
                IPropertySymbol { IsIndexer: true } => "this",
                IPropertySymbol property => property.Name,
                _ => function.Name,
            };
            foreach (IOperation leaf in Leaves(value).Where(IsNull))
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8026, leaf.Syntax.GetLocation(), name));
            }
        }
    }

    /// <summary>
    /// The method or local function that the return belongs to, or null inside a lambda or anonymous method.
    /// </summary>
    private static IMethodSymbol? Function(OperationAnalysisContext context)
    {
        for (IOperation? current = context.Operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation:
                    return null;
                case ILocalFunctionOperation localFunction:
                    return localFunction.Symbol;
            }
        }

        return context.ContainingSymbol as IMethodSymbol;
    }

    /// <summary>
    /// The values the operation can return: conversions are looked through, and conditional branches.
    /// </summary>
    private static IEnumerable<IOperation> Leaves(IOperation operation)
    {
        return operation switch
        {
            IConversionOperation conversion => Leaves(conversion.Operand),
            IConditionalOperation { WhenFalse: { } whenFalse } conditional =>
                Leaves(conditional.WhenTrue).Concat(Leaves(whenFalse)),
            _ => [operation],
        };
    }

    private static bool IsNull(IOperation operation)
    {
        return operation is IDefaultValueOperation
            || operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    private static bool IsEmptyString(IOperation operation)
    {
        return operation is ILiteralOperation { ConstantValue: { HasValue: true, Value: "" } }
            || operation is IFieldReferenceOperation
            {
                Field: { Name: nameof(string.Empty), ContainingType.SpecialType: SpecialType.System_String },
            };
    }

    private static bool IsCollection(ITypeSymbol type, INamedTypeSymbol?[] collections)
    {
        return type is IArrayTypeSymbol
            || type.OriginalDefinition.SpecialType is SpecialType.System_Collections_IEnumerable
                or SpecialType.System_Collections_Generic_IEnumerable_T
            || (collections.Any(collection => UsageSymbols.Implements(type, collection))
                && IsFromCollectionNamespace(type));
    }

    /// <summary>
    /// True when the type or one of its base classes is declared in System.Collections or a namespace inside it.
    /// </summary>
    private static bool IsFromCollectionNamespace(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            string name = current.ContainingNamespace?.ToDisplayString() ?? "";
            if (name == "System.Collections" || name.StartsWith("System.Collections.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
