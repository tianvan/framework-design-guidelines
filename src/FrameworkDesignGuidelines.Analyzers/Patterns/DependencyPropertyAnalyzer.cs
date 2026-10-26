using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Exceptions;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9052 to FDG9057 (section 9.3.1): WPF dependency properties, found through the
/// <c>System.Windows.DependencyObject</c> and <c>System.Windows.DependencyProperty</c> types of the compilation. A
/// registration is a field initialized with a call to <c>DependencyProperty.Register</c>, <c>RegisterReadOnly</c> or
/// <c>RegisterAttached</c>. FDG9052 reports a static field initialized with Register or RegisterReadOnly in a class
/// that does not derive from DependencyObject. For a DependencyProperty field initialized with Register or
/// RegisterAttached, FDG9053 reports a field that is not public static readonly, and FDG9055 a static field whose name
/// is not the registered name, when it is a constant, followed by "Property". The CLR property of a dependency property
/// is an instance property P of a class derived from DependencyObject that declares a static DependencyProperty field
/// named P followed by "Property". FDG9054 reports its get accessor when it does not call GetValue with that field, and
/// its set accessor when it does not call SetValue with that field (or a DependencyPropertyKey field named P followed
/// by "PropertyKey"). FDG9057 reports an accessor that makes the call but holds other code: the get accessor may only
/// return the result, cast or converted, and the set accessor may only make the call, with the value parameter as the
/// value. FDG9056 reports, in the body of an instance constructor of a class derived from DependencyObject (not in a
/// lambda or local function), an assignment to the CLR property of a dependency property of the class, or a SetValue
/// call with a DependencyProperty field of the class, when the value is a constant, default, typeof, or a static field
/// or property.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyPropertyAnalyzer : DiagnosticAnalyzer
{
    private const string FieldSuffix = "Property";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG9052,
            FdgDescriptors.FDG9053,
            FdgDescriptors.FDG9054,
            FdgDescriptors.FDG9055,
            FdgDescriptors.FDG9056,
            FdgDescriptors.FDG9057);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new WpfTypes(start.Compilation);
            if (types.DependencyObject is null || types.DependencyProperty is null)
            {
                return;
            }

            start.RegisterOperationAction(
                operation => AnalyzeRegistration(operation, types),
                OperationKind.FieldInitializer);
            start.RegisterSyntaxNodeAction(node => AnalyzeProperty(node, types), SyntaxKind.PropertyDeclaration);
            start.RegisterOperationAction(
                operation => AnalyzeConstructorCode(operation, types),
                OperationKind.SimpleAssignment,
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeRegistration(OperationAnalysisContext context, WpfTypes types)
    {
        if (context.Operation is not IFieldInitializerOperation { Value: { } value } initializer
            || UnwrapConversions(value) is not IInvocationOperation { TargetMethod: { IsStatic: true } target } call
            || !SymbolEqualityComparer.Default.Equals(target.ContainingType, types.DependencyProperty)
            || target.Name is not ("Register" or "RegisterReadOnly" or "RegisterAttached"))
        {
            return;
        }

        foreach (IFieldSymbol field in initializer.InitializedFields)
        {
            if (NamingSymbols.SourceLocation(field) is not { } location)
            {
                continue;
            }

            if (field.IsStatic
                && target.Name != "RegisterAttached"
                && !ExceptionSymbols.DerivesFrom(field.ContainingType, types.DependencyObject))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG9052,
                    location,
                    UsageSymbols.Display(field.ContainingType),
                    field.Name));
            }

            if (target.Name == "RegisterReadOnly"
                || !SymbolEqualityComparer.Default.Equals(field.Type, types.DependencyProperty))
            {
                continue;
            }

            if (field is not { DeclaredAccessibility: Accessibility.Public, IsStatic: true, IsReadOnly: true })
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG9053, location, field.Name));
            }

            if (field.IsStatic
                && Argument(call, 0) is { ConstantValue: { HasValue: true, Value: string registered } }
                && field.Name != registered + FieldSuffix)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG9055, location, field.Name, registered + FieldSuffix));
            }
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context, WpfTypes types)
    {
        var declaration = (PropertyDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } property
            || Field(property, types) is not { } field)
        {
            return;
        }

        IFieldSymbol? key = property.ContainingType.GetMembers(property.Name + FieldSuffix + "Key")
            .OfType<IFieldSymbol>()
            .FirstOrDefault(member => member.IsStatic
                && SymbolEqualityComparer.Default.Equals(member.Type, types.DependencyPropertyKey));
        if (declaration.ExpressionBody is { } arrow)
        {
            CheckGetter(context, types, property, field, arrow.Expression, null, declaration.Identifier.GetLocation());
            return;
        }

        foreach (AccessorDeclarationSyntax accessor in declaration.AccessorList?.Accessors ?? default)
        {
            SyntaxNode? body = (SyntaxNode?)accessor.ExpressionBody?.Expression ?? accessor.Body;
            Location location = accessor.Keyword.GetLocation();
            if (accessor.Keyword.IsKind(SyntaxKind.GetKeyword))
            {
                CheckGetter(context, types, property, field, body as ExpressionSyntax, accessor.Body, location);
            }
            else if (accessor.Keyword.IsKind(SyntaxKind.SetKeyword))
            {
                CheckSetter(context, types, property, field, key, body, location);
            }
        }
    }

    private static void CheckGetter(
        SyntaxNodeAnalysisContext context,
        WpfTypes types,
        IPropertySymbol property,
        IFieldSymbol field,
        ExpressionSyntax? expression,
        BlockSyntax? block,
        Location location)
    {
        SyntaxNode? body = (SyntaxNode?)expression ?? block;
        IInvocationOperation? call = body is null
            ? null
            : Calls(context, body, "GetValue", types)
                .FirstOrDefault(invocation => IsFieldArgument(invocation, field, null));
        if (call is null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9054, location, property.Name, "GetValue"));
            return;
        }

        ExpressionSyntax? returned = expression
            ?? (block is { Statements: { Count: 1 } statements } && statements[0] is ReturnStatementSyntax only
                ? only.Expression
                : null);
        if (returned is null
            || context.SemanticModel.GetOperation(returned, context.CancellationToken) is not { } operation
            || UnwrapConversions(operation).Syntax != call.Syntax)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9057, location, property.Name, "GetValue"));
        }
    }

    private static void CheckSetter(
        SyntaxNodeAnalysisContext context,
        WpfTypes types,
        IPropertySymbol property,
        IFieldSymbol field,
        IFieldSymbol? key,
        SyntaxNode? body,
        Location location)
    {
        IInvocationOperation? call = body is null
            ? null
            : Calls(context, body, "SetValue", types).FirstOrDefault(invocation =>
                IsFieldArgument(invocation, field, key));
        if (call is null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9054, location, property.Name, "SetValue"));
            return;
        }

        SyntaxNode? statement = body is BlockSyntax block
            ? block.Statements.Count == 1 && block.Statements[0] is ExpressionStatementSyntax only
                ? only.Expression
                : null
            : body;
        if (statement != call.Syntax
            || Argument(call, 1) is not { } value
            || UnwrapConversions(value) is not IParameterReferenceOperation { Parameter.Name: "value" })
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9057, location, property.Name, "SetValue"));
        }
    }

    private static void AnalyzeConstructorCode(OperationAnalysisContext context, WpfTypes types)
    {
        if (context.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } ctor
            || !ExceptionSymbols.DerivesFrom(ctor.ContainingType, types.DependencyObject)
            || IsInNestedFunction(context.Operation))
        {
            return;
        }

        string? name = null;
        IOperation? value = null;
        switch (context.Operation)
        {
            case ISimpleAssignmentOperation
                {
                    Target: IPropertyReferenceOperation { Instance: IInstanceReferenceOperation, Property: { } target },
                } assignment
                when SymbolEqualityComparer.Default.Equals(target.ContainingType, ctor.ContainingType)
                    && Field(target, types) is not null:
                name = target.Name;
                value = assignment.Value;
                break;
            case IInvocationOperation { Instance: IInstanceReferenceOperation, TargetMethod.Name: "SetValue" } call
                when ExceptionSymbols.DerivesFrom(call.TargetMethod.ContainingType, types.DependencyObject)
                    && Argument(call, 0) is { } first
                    && UnwrapConversions(first) is IFieldReferenceOperation { Field: { IsStatic: true } field }
                    && SymbolEqualityComparer.Default.Equals(field.ContainingType, ctor.ContainingType)
                    && SymbolEqualityComparer.Default.Equals(field.Type, types.DependencyProperty):
                name = field.Name.EndsWith(FieldSuffix, StringComparison.Ordinal)
                    ? field.Name.Substring(0, field.Name.Length - FieldSuffix.Length)
                    : field.Name;
                value = Argument(call, 1);
                break;
        }

        if (name is not null && value is not null && IsMetadataValue(value))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9056, context.Operation.Syntax.GetLocation(), name));
        }
    }

    /// <summary>
    /// The static DependencyProperty field named after the property, declared in the property's own class, when the
    /// property is an instance property of a class derived from DependencyObject.
    /// </summary>
    private static IFieldSymbol? Field(IPropertySymbol property, WpfTypes types)
    {
        if (property is { IsStatic: true } or { IsIndexer: true }
            || !ExceptionSymbols.DerivesFrom(property.ContainingType, types.DependencyObject))
        {
            return null;
        }

        return property.ContainingType.GetMembers(property.Name + FieldSuffix)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.IsStatic
                && SymbolEqualityComparer.Default.Equals(field.Type, types.DependencyProperty));
    }

    /// <summary>The calls in <paramref name="body"/> to the DependencyObject method of the given name.</summary>
    private static IEnumerable<IInvocationOperation> Calls(
        SyntaxNodeAnalysisContext context, SyntaxNode body, string name, WpfTypes types)
    {
        if (context.SemanticModel.GetOperation(body, context.CancellationToken) is not { } operation)
        {
            return [];
        }

        return operation.DescendantsAndSelf().OfType<IInvocationOperation>().Where(invocation =>
            invocation.TargetMethod.Name == name
            && ExceptionSymbols.DerivesFrom(invocation.TargetMethod.ContainingType, types.DependencyObject));
    }

    private static bool IsFieldArgument(IInvocationOperation invocation, IFieldSymbol field, IFieldSymbol? key)
    {
        return Argument(invocation, 0) is { } first
            && UnwrapConversions(first) is IFieldReferenceOperation { Field: { } used }
            && (SymbolEqualityComparer.Default.Equals(used, field) || SymbolEqualityComparer.Default.Equals(used, key));
    }

    /// <summary>The value passed for the parameter at <paramref name="ordinal"/>, or null.</summary>
    private static IOperation? Argument(IInvocationOperation invocation, int ordinal)
    {
        return invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == ordinal)?.Value;
    }

    /// <summary>
    /// True for a value that PropertyMetadata can hold as the default of every instance: a constant, default,
    /// typeof, or a static field or property.
    /// </summary>
    private static bool IsMetadataValue(IOperation value)
    {
        IOperation unwrapped = UnwrapConversions(value);
        return unwrapped.ConstantValue.HasValue
            || unwrapped is IDefaultValueOperation or ITypeOfOperation
            || unwrapped is IFieldReferenceOperation { Field.IsStatic: true }
            || unwrapped is IPropertyReferenceOperation { Property.IsStatic: true };
    }

    private static bool IsInNestedFunction(IOperation operation)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private sealed class WpfTypes(Compilation compilation)
    {
        public INamedTypeSymbol? DependencyObject { get; } =
            compilation.GetTypeByMetadataName("System.Windows.DependencyObject");

        public INamedTypeSymbol? DependencyProperty { get; } =
            compilation.GetTypeByMetadataName("System.Windows.DependencyProperty");

        public INamedTypeSymbol? DependencyPropertyKey { get; } =
            compilation.GetTypeByMetadataName("System.Windows.DependencyPropertyKey");
    }
}
