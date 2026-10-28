using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Exceptions;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8002, FDG8014, FDG8015, FDG8016, FDG8031, FDG8051 and FDG8096 (sections 8.1, 8.3, 8.5 and 8.12): the types
/// that externally visible members use in their signatures. FDG8002 reports a readonly field whose type is an array:
/// readonly protects the field, not the elements. A field initialized with an empty array (<c>new T[0]</c>,
/// <c>new T[] { }</c>, <c>{ }</c>, <c>[]</c> or <c>Array.Empty&lt;T&gt;()</c>) is not reported: it has no elements
/// to change (a constructor that assigns it a new array is not seen). The others report a type used as the type of
/// a parameter, a return value (or the result type of a returned <c>Task&lt;T&gt;</c> or
/// <c>ValueTask&lt;T&gt;</c>), a property or indexer, or a field:
/// <c>ArrayList</c> (FDG8014); <c>Hashtable</c> or <c>Dictionary&lt;TKey, TValue&gt;</c> (FDG8015); a type that is
/// or implements <c>IEnumerator</c>, except as the return type of a method named GetEnumerator (FDG8016); a type
/// that implements <c>ICollection&lt;byte&gt;</c> or <c>IReadOnlyCollection&lt;byte&gt;</c>, other than
/// <c>ArraySegment&lt;byte&gt;</c> and <c>ImmutableArray&lt;byte&gt;</c>, which are views over a byte array, the
/// design the text asks for (FDG8031); <c>ICloneable</c>
/// (FDG8051); and <c>XmlNode</c> or a type derived from it (FDG8096). Types derived from the reported types, other
/// than those of XmlNode, and uses as a type argument are not reported. Overrides and interface implementations take
/// their signature from another member and are not reported, and neither are the properties that a record declares
/// through its primary constructor, whose parameters are.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsageSignatureAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG8002,
            FdgDescriptors.FDG8014,
            FdgDescriptors.FDG8015,
            FdgDescriptors.FDG8016,
            FdgDescriptors.FDG8031,
            FdgDescriptors.FDG8051,
            FdgDescriptors.FDG8096);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new SignatureTypes(start.Compilation);
            start.RegisterSymbolAction(
                symbolContext => AnalyzeMember(symbolContext, types),
                SymbolKind.Method,
                SymbolKind.Property,
                SymbolKind.Field);
        });
    }

    private static void AnalyzeMember(SymbolAnalysisContext context, SignatureTypes types)
    {
        ISymbol member = context.Symbol;
        if (member.IsImplicitlyDeclared
            || NamingSymbols.IsInherited(member)
            || !NamingSymbols.IsExternallyVisible(member))
        {
            return;
        }

        string name;
        ImmutableArray<IParameterSymbol> parameters;
        ITypeSymbol? type;
        bool isEnumeratorAllowed = false;
        switch (member)
        {
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                name = constructor.ContainingType.Name;
                parameters = constructor.Parameters;
                type = null;
                break;
            case IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion,
                } method:
                name = method.Name;
                parameters = method.Parameters;
                type = Result(method.ReturnType, types.Awaitables);
                isEnumeratorAllowed = method.Name == "GetEnumerator";
                break;
            case IPropertySymbol property:
                name = property.IsIndexer ? "this" : property.Name;
                parameters = property.Parameters;
                type = NamingSymbols.IsPositionalProperty(property) ? null : property.Type;
                break;
            case IFieldSymbol field:
                name = field.Name;
                parameters = ImmutableArray<IParameterSymbol>.Empty;
                type = field.Type;
                if (field.IsReadOnly
                    && field.Type is IArrayTypeSymbol
                    && !IsInitializedEmpty(field, context.CancellationToken)
                    && NamingSymbols.SourceLocation(field) is { } fieldLocation)
                {
                    context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8002, fieldLocation, field.Name));
                }

                break;
            default:
                return;
        }

        foreach (IParameterSymbol parameter in parameters)
        {
            if (NamingSymbols.SourceLocation(parameter) is { } location)
            {
                Check(context, types, parameter.Type, location, name, isEnumeratorAllowed: false);
            }
        }

        if (type is not null && NamingSymbols.SourceLocation(member) is { } memberLocation)
        {
            Check(context, types, type, memberLocation, name, isEnumeratorAllowed);
        }
    }

    private static void Check(
        SymbolAnalysisContext context,
        SignatureTypes types,
        ITypeSymbol type,
        Location location,
        string member,
        bool isEnumeratorAllowed)
    {
        if (UsageSymbols.Is(type, types.ArrayList))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8014, location, member));
        }

        if (UsageSymbols.Is(type, types.Hashtable) || UsageSymbols.Is(type, types.Dictionary))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG8015, location, UsageSymbols.Display(type), member));
        }

        if (!isEnumeratorAllowed && type is INamedTypeSymbol && UsageSymbols.Implements(type, types.Enumerator))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG8016, location, UsageSymbols.Display(type), member));
        }

        if (IsByteCollection(type, types))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG8031, location, UsageSymbols.Display(type), member));
        }

        if (UsageSymbols.Is(type, types.Cloneable))
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG8051, location, member));
        }

        if (ExceptionSymbols.DerivesFrom(type, types.XmlNode))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG8096, location, UsageSymbols.Display(type), member));
        }
    }

    /// <summary>
    /// True when the declarator of the field has an empty array as its initializer: an array creation with a size of
    /// 0 in its first rank or with an empty initializer, an empty array initializer or collection expression, or a
    /// call to <c>Array.Empty&lt;T&gt;()</c>. The check is syntactic; a size that is a named constant is not seen.
    /// </summary>
    private static bool IsInitializedEmpty(IFieldSymbol field, CancellationToken cancellationToken)
    {
        return field.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer.Value: { } value }
            && IsEmptyArray(value));
    }

    private static bool IsEmptyArray(ExpressionSyntax value)
    {
        return value switch
        {
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions.Count == 0,
            ArrayCreationExpressionSyntax creation => creation.Type.RankSpecifiers[0].Sizes.Any(
                size => size is LiteralExpressionSyntax { Token.ValueText: "0" }),
            InitializerExpressionSyntax initializer => initializer.Expressions.Count == 0,
            CollectionExpressionSyntax collection => collection.Elements.Count == 0,
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax { Identifier.ValueText: "Empty", TypeArgumentList.Arguments.Count: 1 },
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "Array" }
                        or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Array" },
                },
                ArgumentList.Arguments.Count: 0,
            } => true,
            _ => false,
        };
    }

    /// <summary>
    /// True for a named type other than <c>ArraySegment&lt;byte&gt;</c> and <c>ImmutableArray&lt;byte&gt;</c> that
    /// is or implements <c>ICollection&lt;byte&gt;</c> or <c>IReadOnlyCollection&lt;byte&gt;</c>; <c>byte[]</c> is an
    /// array type.
    /// </summary>
    private static bool IsByteCollection(ITypeSymbol type, SignatureTypes types)
    {
        return type is INamedTypeSymbol named
            && !UsageSymbols.Is(named, types.ArraySegment)
            && !UsageSymbols.Is(named, types.ImmutableArray)
            && new[] { named }.Concat(named.AllInterfaces).Any(@interface =>
                (UsageSymbols.Is(@interface, types.Collection) || UsageSymbols.Is(@interface, types.ReadOnlyCollection))
                && @interface.TypeArguments[0].SpecialType == SpecialType.System_Byte);
    }

    /// <summary>
    /// The result type of a <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>, otherwise the type itself.
    /// </summary>
    private static ITypeSymbol Result(ITypeSymbol type, INamedTypeSymbol?[] awaitables)
    {
        return type is INamedTypeSymbol { TypeArguments: { Length: 1 } arguments } named
            && awaitables.Any(awaitable => SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, awaitable))
            ? arguments[0]
            : type;
    }

    private sealed class SignatureTypes(Compilation compilation)
    {
        public INamedTypeSymbol? ArrayList { get; } =
            compilation.GetTypeByMetadataName("System.Collections.ArrayList");

        public INamedTypeSymbol? Hashtable { get; } =
            compilation.GetTypeByMetadataName("System.Collections.Hashtable");

        public INamedTypeSymbol? Dictionary { get; } =
            compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");

        public INamedTypeSymbol? Enumerator { get; } =
            compilation.GetSpecialType(SpecialType.System_Collections_IEnumerator);

        public INamedTypeSymbol? Collection { get; } =
            compilation.GetSpecialType(SpecialType.System_Collections_Generic_ICollection_T);

        public INamedTypeSymbol? ReadOnlyCollection { get; } =
            compilation.GetSpecialType(SpecialType.System_Collections_Generic_IReadOnlyCollection_T);

        public INamedTypeSymbol? ArraySegment { get; } = compilation.GetTypeByMetadataName("System.ArraySegment`1");

        public INamedTypeSymbol? ImmutableArray { get; } =
            compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1");

        public INamedTypeSymbol? Cloneable { get; } = compilation.GetTypeByMetadataName("System.ICloneable");

        public INamedTypeSymbol? XmlNode { get; } = compilation.GetTypeByMetadataName("System.Xml.XmlNode");

        public INamedTypeSymbol?[] Awaitables { get; } =
        [
            compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"),
            compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"),
        ];
    }
}
