using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The inequality operators (<c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c>) of an externally visible class or
/// struct. FDG5094: a type that declares one implements <c>IComparable&lt;T&gt;</c> of itself. FDG5096: a type that
/// declares <c>&lt;</c> and implements <c>IEquatable&lt;T&gt;</c> of itself declares <c>&lt;=</c>. FDG5097: each
/// returns bool. FDG5099: each takes only the declaring type, or <c>Nullable&lt;T&gt;</c> of it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InequalityOperatorAnalyzer : DiagnosticAnalyzer
{
    private const string LessThan = "op_LessThan";
    private const string LessThanOrEqual = "op_LessThanOrEqual";

    private static readonly Dictionary<string, string> _tokens = new()
    {
        [LessThan] = "<",
        ["op_GreaterThan"] = ">",
        [LessThanOrEqual] = "<=",
        ["op_GreaterThanOrEqual"] = ">=",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG5094, FdgDescriptors.FDG5096, FdgDescriptors.FDG5097, FdgDescriptors.FDG5099);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || !NamingSymbols.IsExternallyVisible(type))
        {
            return;
        }

        IMethodSymbol[] operators = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind == MethodKind.UserDefinedOperator && _tokens.ContainsKey(method.Name))
            .ToArray();
        if (operators.Length == 0)
        {
            return;
        }

        string typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        if (!Implements(type, "IComparable`1") && NamingSymbols.SourceLocation(type) is { } typeLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5094, typeLocation, typeName));
        }

        if (Implements(type, "IEquatable`1") && operators.All(method => method.Name != LessThanOrEqual)
            && operators.FirstOrDefault(method => method.Name == LessThan) is { } lessThan
            && NamingSymbols.SourceLocation(lessThan) is { } lessThanLocation)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5096, lessThanLocation, typeName));
        }

        foreach (IMethodSymbol method in operators)
        {
            if (NamingSymbols.SourceLocation(method) is not { } location)
            {
                continue;
            }

            string name = "operator " + _tokens[method.Name];
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5097, location, name));
            }

            if (method.Parameters.FirstOrDefault(parameter => !IsSelf(parameter.Type, type)) is { } other)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG5099,
                    location,
                    name,
                    other.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }

    /// <summary>True when <paramref name="type"/> implements the System interface over itself.</summary>
    private static bool Implements(INamedTypeSymbol type, string metadataName)
    {
        return type.AllInterfaces.Any(@interface =>
            @interface.MetadataName == metadataName
            && @interface.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            && SymbolEqualityComparer.Default.Equals(@interface.TypeArguments[0], type));
    }

    private static bool IsSelf(ITypeSymbol parameterType, INamedTypeSymbol type)
    {
        if (parameterType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && parameterType is INamedTypeSymbol nullable)
        {
            parameterType = nullable.TypeArguments[0];
        }

        return SymbolEqualityComparer.Default.Equals(parameterType, type);
    }
}
