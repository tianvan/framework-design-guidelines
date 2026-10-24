using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fdg.Cli.Hooks;

/// <summary>The kinds of symbol the naming tags of a rule (<c>naming.symbols</c> in rules.json) distinguish.</summary>
public enum NamingSymbolKind
{
    Namespace,
    Type,
    TypeParameter,
    Method,
    Property,
    Event,
    VisibleField,
    PrivateField,
    ConstField,
    Parameter,
    Local,
    ConstLocal,
}

/// <summary>How a name fails an IDE1006 naming rule (<c>naming.failures</c> in rules.json).</summary>
public enum NamingFailure
{
    Casing,
    Prefix,
}

/// <summary>
/// Tells what an IDE1006 diagnostic is about: the kind of symbol declared at its position and how its name fails.
/// Both are read from the source and the message, so a rule is quoted only when both are certain.
/// </summary>
public static class DeclarationLocator
{
    /// <summary>The diagnostic whose rules the symbol kind and failure of the flagged name select.</summary>
    public const string NamingDiagnosticId = "IDE1006";

    /// <summary>Parses <paramref name="source"/> for <see cref="NamingKindAt(SyntaxTree, int, int)"/>, which callers can repeat on one tree.</summary>
    public static SyntaxTree Parse(string source) =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

    /// <summary>The kind of the symbol declared at the 1-based line and column, or null when no declaration name is there.</summary>
    /// <remarks>
    /// Also null when the source has syntax errors (it may not be the code that was built), for declarations that are
    /// not members of a type, for top-level statements, and for symbols without a kind (enum members, local functions).
    /// </remarks>
    public static NamingSymbolKind? NamingKindAt(string source, int line, int column) =>
        NamingKindAt(Parse(source), line, column);

    /// <inheritdoc cref="NamingKindAt(string, int, int)"/>
    public static NamingSymbolKind? NamingKindAt(SyntaxTree tree, int line, int column)
    {
        SourceText text = tree.GetText();
        if (line < 1 || line > text.Lines.Count || column < 1)
        {
            return null;
        }

        TextLine textLine = text.Lines[line - 1];
        if (column > textLine.Span.Length)
        {
            return null;
        }

        SyntaxNode root = tree.GetRoot();
        if (root.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        int position = textLine.Start + column - 1;
        SyntaxToken token = root.FindToken(position);
        if (!token.IsKind(SyntaxKind.IdentifierToken) || !token.Span.Contains(position) ||
            token.Parent!.AncestorsAndSelf().Any(n => n is GlobalStatementSyntax))
        {
            return null;
        }

        return KindOf(token);
    }

    /// <summary>
    /// Roslyn's IDE1006 messages: "Missing prefix: '_'" and "Prefix '_' is not expected" → Prefix; the messages
    /// "These words must begin with upper case characters", "These non-leading words must begin with an upper case
    /// letter" and "The first word, 'X', must begin with a lower case character" → Casing; everything else
    /// (suffixes, "cannot contain", ...) → null.
    /// </summary>
    public static NamingFailure? ClassifyNamingFailure(string message)
    {
        if (message.Contains("Missing prefix", StringComparison.OrdinalIgnoreCase) ||
            (message.Contains("Prefix '", StringComparison.OrdinalIgnoreCase) &&
             message.Contains("is not expected", StringComparison.OrdinalIgnoreCase)))
        {
            return NamingFailure.Prefix;
        }

        return message.Contains("must begin with", StringComparison.OrdinalIgnoreCase) ? NamingFailure.Casing : null;
    }

    private static NamingSymbolKind? KindOf(SyntaxToken name)
    {
        SyntaxNode node = name.Parent!;
        switch (node)
        {
            case IdentifierNameSyntax or QualifiedNameSyntax:
                return IsNamespaceName(node) ? NamingSymbolKind.Namespace : null;
            case BaseTypeDeclarationSyntax:
                return NamingSymbolKind.Type;
            case DelegateDeclarationSyntax:
                return NamingSymbolKind.Type;
            case TypeParameterSyntax:
                return NamingSymbolKind.TypeParameter;
            case ParameterSyntax:
                return NamingSymbolKind.Parameter;
            case MethodDeclarationSyntax method when method.Parent is TypeDeclarationSyntax:
                return NamingSymbolKind.Method;
            case PropertyDeclarationSyntax property when property.Parent is TypeDeclarationSyntax:
                return NamingSymbolKind.Property;
            case EventDeclarationSyntax @event when @event.Parent is TypeDeclarationSyntax:
                return NamingSymbolKind.Event;
            case VariableDeclaratorSyntax declarator:
                return KindOfVariable(declarator);
            case SingleVariableDesignationSyntax:
            case ForEachStatementSyntax:
            case CatchDeclarationSyntax:
                return NamingSymbolKind.Local;
            default:
                return null;
        }
    }

    /// <summary>Whether <paramref name="name"/> is, or is part of, the name in a namespace declaration.</summary>
    private static bool IsNamespaceName(SyntaxNode name)
    {
        SyntaxNode? parent = name.Parent;
        while (parent is QualifiedNameSyntax)
        {
            parent = parent.Parent;
        }

        return parent is BaseNamespaceDeclarationSyntax;
    }

    private static NamingSymbolKind? KindOfVariable(VariableDeclaratorSyntax declarator)
    {
        switch (declarator.Parent?.Parent)
        {
            case FieldDeclarationSyntax { Parent: TypeDeclarationSyntax } field:
                return FieldKind(field);
            case EventFieldDeclarationSyntax { Parent: TypeDeclarationSyntax }:
                return NamingSymbolKind.Event;
            case LocalDeclarationStatementSyntax local:
                return local.Modifiers.Any(SyntaxKind.ConstKeyword) ? NamingSymbolKind.ConstLocal : NamingSymbolKind.Local;
            case UsingStatementSyntax:
            case ForStatementSyntax:
            case FixedStatementSyntax:
                return NamingSymbolKind.Local;
            default:
                return null;
        }
    }

    /// <summary>
    /// Const wins over visibility; a field without an accessibility modifier is private, except in an interface,
    /// where it is public.
    /// </summary>
    private static NamingSymbolKind FieldKind(FieldDeclarationSyntax field)
    {
        SyntaxTokenList modifiers = field.Modifiers;
        if (modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return NamingSymbolKind.ConstField;
        }

        bool visible = modifiers.Any(SyntaxKind.PublicKeyword) ||
            (modifiers.Any(SyntaxKind.ProtectedKeyword) && !modifiers.Any(SyntaxKind.PrivateKeyword)) ||
            (field.Parent is InterfaceDeclarationSyntax && !modifiers.Any(SyntaxKind.PrivateKeyword) &&
                !modifiers.Any(SyntaxKind.InternalKeyword));
        return visible ? NamingSymbolKind.VisibleField : NamingSymbolKind.PrivateField;
    }
}
