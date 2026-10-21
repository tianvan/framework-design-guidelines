using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Layout;

/// <summary>
/// FDGA003: a statement or member body block must not open and close on the same line.
/// </summary>
/// <remarks>
/// The profile globalconfig keeps <c>csharp_preserve_single_line_blocks = true</c> so that IDE0055 leaves
/// <c>{ get; private set; }</c> alone; this analyzer takes over expanding the blocks the book wants expanded.
/// Accessors, initializers, lambdas, anonymous methods, type bodies and <c>do</c> bodies are not reported.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleLineCatchFinallyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDGA003);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // Registered on the owners rather than on Block so the diagnostic (on the owner) stays inside the node.
        context.RegisterSyntaxNodeAction(
            AnalyzeOwner,
            SyntaxKind.CatchClause,
            SyntaxKind.FinallyClause,
            SyntaxKind.TryStatement,
            SyntaxKind.IfStatement,
            SyntaxKind.ElseClause,
            SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement,
            SyntaxKind.WhileStatement,
            SyntaxKind.UsingStatement,
            SyntaxKind.LockStatement,
            SyntaxKind.FixedStatement,
            SyntaxKind.CheckedStatement,
            SyntaxKind.UncheckedStatement,
            SyntaxKind.UnsafeStatement,
            SyntaxKind.SwitchSection,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.DestructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.LocalFunctionStatement);
    }

    /// <summary>
    /// Finds the token that owns <paramref name="block"/>: the keyword of the clause or statement, the first
    /// label keyword of a switch section, or the name of the member or local function.
    /// </summary>
    internal static bool TryGetOwner(BlockSyntax block, out SyntaxToken owner)
    {
        owner = block.Parent switch
        {
            CatchClauseSyntax node => node.CatchKeyword,
            FinallyClauseSyntax node => node.FinallyKeyword,
            TryStatementSyntax node when node.Block == block => node.TryKeyword,
            IfStatementSyntax node when node.Statement == block => node.IfKeyword,
            ElseClauseSyntax node => node.ElseKeyword,
            ForStatementSyntax node => node.ForKeyword,
            CommonForEachStatementSyntax node => node.ForEachKeyword,
            WhileStatementSyntax node => node.WhileKeyword,
            UsingStatementSyntax node => node.UsingKeyword,
            LockStatementSyntax node => node.LockKeyword,
            FixedStatementSyntax node => node.FixedKeyword,
            CheckedStatementSyntax node => node.Keyword,
            UnsafeStatementSyntax node => node.UnsafeKeyword,
            SwitchSectionSyntax node => node.Labels.First().Keyword,
            MethodDeclarationSyntax node when node.Body == block => node.Identifier,
            ConstructorDeclarationSyntax node when node.Body == block => node.Identifier,
            DestructorDeclarationSyntax node when node.Body == block => node.Identifier,
            OperatorDeclarationSyntax node when node.Body == block => node.OperatorToken,
            ConversionOperatorDeclarationSyntax node when node.Body == block => node.OperatorKeyword,
            LocalFunctionStatementSyntax node when node.Body == block => node.Identifier,
            _ => default,
        };

        return owner.RawKind != (int)SyntaxKind.None && !owner.IsMissing;
    }

    internal static bool IsSingleLine(BlockSyntax block)
    {
        SyntaxToken open = block.OpenBraceToken;
        SyntaxToken close = block.CloseBraceToken;
        if (open.IsMissing || close.IsMissing)
        {
            return false;
        }

        SyntaxTree tree = block.SyntaxTree;
        return tree.GetLineSpan(open.Span).StartLinePosition.Line == tree.GetLineSpan(close.Span).StartLinePosition.Line;
    }

    private static void AnalyzeOwner(SyntaxNodeAnalysisContext context)
    {
        foreach (SyntaxNode child in context.Node.ChildNodes())
        {
            if (child is BlockSyntax block && TryGetOwner(block, out SyntaxToken owner) && IsSingleLine(block))
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDGA003, owner.GetLocation()));
            }
        }
    }
}
