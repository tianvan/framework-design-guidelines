using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5046: a parameter of an externally visible constructor that is used only to set a property of the instance has
/// the property's name, apart from casing. The parameter's one use is <c>P = p</c> or <c>P = p ?? throw ...</c>, as a
/// statement or expression body of the constructor or, for the primary constructor of a class or struct, as a
/// property initializer. Uses inside <c>nameof</c> do not count. Positional record parameters are properties
/// themselves and are not checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConstructorParameterNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5046);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzePrimaryConstructor, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var declaration = (ConstructorDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is { } constructor)
        {
            Analyze(context, constructor, declaration);
        }
    }

    private static void AnalyzePrimaryConstructor(SyntaxNodeAnalysisContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (declaration.ParameterList is null
            || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } type)
        {
            return;
        }

        IMethodSymbol? constructor = type.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaringSyntaxReferences.Any(
                reference => reference.GetSyntax(context.CancellationToken) == declaration));
        if (constructor is not null)
        {
            Analyze(context, constructor, declaration);
        }
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, IMethodSymbol constructor, SyntaxNode scope)
    {
        if (!NamingSymbols.IsExternallyVisible(constructor))
        {
            return;
        }

        foreach (IParameterSymbol parameter in constructor.Parameters)
        {
            List<IdentifierNameSyntax> uses = Uses(context, scope, parameter);
            if (uses.Count == 1
                && SetProperty(context, uses[0]) is { } property
                && !string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG5046, location, parameter.Name, property.Name));
            }
        }
    }

    private static List<IdentifierNameSyntax> Uses(
        SyntaxNodeAnalysisContext context, SyntaxNode scope, IParameterSymbol parameter)
    {
        var uses = new List<IdentifierNameSyntax>();
        foreach (IdentifierNameSyntax name in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (name.Identifier.ValueText == parameter.Name
                && !IsInNameof(name)
                && SymbolEqualityComparer.Default.Equals(
                    context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, parameter))
            {
                uses.Add(name);
            }
        }

        return uses;
    }

    private static bool IsInNameof(SyntaxNode node)
    {
        return node.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
    }

    /// <summary>The instance property that <paramref name="use"/> sets, if that is all the use does.</summary>
    private static IPropertySymbol? SetProperty(SyntaxNodeAnalysisContext context, IdentifierNameSyntax use)
    {
        ExpressionSyntax value = use;
        if (use.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce
            && coalesce.Left == use && coalesce.Right is ThrowExpressionSyntax)
        {
            value = coalesce;
        }

        SyntaxNode? target = value.Parent switch
        {
            AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment
                when assignment.Right == value
                && assignment.Parent is ExpressionStatementSyntax or ArrowExpressionClauseSyntax
                && assignment.Left is IdentifierNameSyntax
                    or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
                => assignment.Left,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property,
            _ => null,
        };

        ISymbol? symbol = target switch
        {
            PropertyDeclarationSyntax property =>
                context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken),
            ExpressionSyntax left => context.SemanticModel.GetSymbolInfo(left, context.CancellationToken).Symbol,
            _ => null,
        };
        return symbol is IPropertySymbol { IsStatic: false, IsIndexer: false } result ? result : null;
    }
}
