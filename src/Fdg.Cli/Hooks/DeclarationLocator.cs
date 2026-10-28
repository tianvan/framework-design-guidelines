using System.Runtime.CompilerServices;
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

/// <summary>Identifies a declaration across two versions of a file.</summary>
/// <param name="Container">The names of the namespaces and types around it, outermost first, joined with dots.</param>
/// <param name="Name">The declared name: <c>this</c> for an indexer, the operator token for an operator.</param>
/// <param name="Kind">The syntax kind's name, for example <c>MethodDeclaration</c>; a field and a property are different kinds.</param>
/// <param name="Signature">
/// Tells overloads apart: the generic arity and the parameter types as written, with no whitespace; empty for a
/// declaration without parameters. A type includes the parameters of its primary constructor.
/// </param>
/// <param name="Part">
/// Empty for the declaration itself; <c>parameter x</c> or <c>type parameter T</c> for a parameter or type parameter
/// of it, which exists only if the other version has one of that name.
/// </param>
public sealed record DeclarationKey(string Container, string Name, string Kind, string Signature = "", string Part = "");

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
        if (PositionOf(tree, line, column) is not int position)
        {
            return null;
        }

        SyntaxNode root = tree.GetRoot();
        if (HasSyntaxErrors(tree))
        {
            return null;
        }

        SyntaxToken token = root.FindToken(position);
        if (!token.IsKind(SyntaxKind.IdentifierToken) || !token.Span.Contains(position) ||
            token.Parent!.AncestorsAndSelf().Any(n => n is GlobalStatementSyntax))
        {
            return null;
        }

        return KindOf(token);
    }

    /// <summary>
    /// The innermost type or member declaration (type, method, constructor, property, indexer, event, field variable,
    /// enum member, delegate) whose declaration, not its code, contains the 1-based line and column. A parameter or
    /// type parameter gives its owner's key with <see cref="DeclarationKey.Part"/> set. A position on the name of a
    /// namespace declaration gives the namespace.
    /// </summary>
    /// <remarks>
    /// Null when the source has syntax errors; when no such declaration contains the position; when the position is
    /// in code (a method or accessor body, an expression body, an initializer, a constructor initializer, top-level
    /// statements), so a local, a lambda parameter or a pattern variable is never an existing API; when it is on an
    /// attribute; and when it is in a field or event field that declares several variables but not in one of them.
    /// Anywhere else in a declaration's header (modifiers, types, base list, constraints) it gives the declaration.
    /// </remarks>
    public static DeclarationKey? DeclarationAt(string source, int line, int column) =>
        DeclarationAt(Parse(source), line, column);

    /// <inheritdoc cref="DeclarationAt(string, int, int)"/>
    public static DeclarationKey? DeclarationAt(SyntaxTree tree, int line, int column)
    {
        if (PositionOf(tree, line, column) is not int position || HasSyntaxErrors(tree))
        {
            return null;
        }

        string part = string.Empty;
        foreach (SyntaxNode node in tree.GetRoot().FindToken(position).Parent!.AncestorsAndSelf())
        {
            switch (node)
            {
                case BlockSyntax or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax or ConstructorInitializerSyntax
                    or PrimaryConstructorBaseTypeSyntax or AttributeListSyntax or GlobalStatementSyntax:
                    return null;
                case ParameterSyntax parameter when part.Length == 0:
                    part = "parameter " + parameter.Identifier.Text;
                    break;
                case TypeParameterSyntax typeParameter when part.Length == 0:
                    part = "type parameter " + typeParameter.Identifier.Text;
                    break;
                case BaseNamespaceDeclarationSyntax @namespace:
                    return @namespace.Name.Span.Contains(position) ? KeyOf(@namespace) : null;
                case BaseFieldDeclarationSyntax field when field.Span.Contains(position):
                    // The variables of a field are keyed one by one; a position outside them is none of them.
                    return field.Declaration.Variables.Count == 1 ? KeyOf(field.Declaration.Variables[0]) : null;
                default:
                    if (node.Span.Contains(position) && KeyOf(node) is DeclarationKey key)
                    {
                        return key with { Part = part };
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="source"/> declares <paramref name="key"/>; false when it has syntax errors.</summary>
    public static bool Declares(string source, DeclarationKey key) =>
        DeclaredKeys(Parse(source))?.Contains(key) == true;

    /// <inheritdoc cref="Declares(string, DeclarationKey)"/>
    public static bool Declares(SyntaxTree tree, DeclarationKey key) =>
        DeclaredKeys(tree)?.Contains(key) == true;

    /// <summary>
    /// The key of every declaration in <paramref name="tree"/>, and for each of them one key per parameter and type
    /// parameter, with <see cref="DeclarationKey.Part"/> set. Null when the tree has syntax errors. Callers that ask
    /// about several positions can compute it once.
    /// </summary>
    public static IReadOnlySet<DeclarationKey>? DeclaredKeys(SyntaxTree tree)
    {
        if (HasSyntaxErrors(tree))
        {
            return null;
        }

        var keys = new HashSet<DeclarationKey>();
        foreach (SyntaxNode node in tree.GetRoot().DescendantNodes(n => n is not (BlockSyntax or ArrowExpressionClauseSyntax)))
        {
            if (KeyOf(node) is not DeclarationKey key)
            {
                continue;
            }

            keys.Add(key);
            foreach (string part in PartsOf(node))
            {
                keys.Add(key with { Part = part });
            }
        }

        return keys;
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

    /// <summary>The offset of the 1-based line and column, or null when they are not inside the text.</summary>
    private static int? PositionOf(SyntaxTree tree, int line, int column)
    {
        SourceText text = tree.GetText();
        if (line < 1 || line > text.Lines.Count || column < 1)
        {
            return null;
        }

        TextLine textLine = text.Lines[line - 1];
        return column > textLine.Span.Length ? null : textLine.Start + column - 1;
    }

    // Walking a tree for its diagnostics is done once per tree, however many positions are asked about.
    private static readonly ConditionalWeakTable<SyntaxTree, object> SyntaxErrors = new();

    private static bool HasSyntaxErrors(SyntaxTree tree) =>
        (bool)SyntaxErrors.GetValue(
            tree,
            t => t.GetRoot().GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error));

    /// <summary>The parameters and type parameters a declaration declares, as <see cref="DeclarationKey.Part"/> values.</summary>
    private static IEnumerable<string> PartsOf(SyntaxNode node)
    {
        BaseParameterListSyntax? parameters = node switch
        {
            DelegateDeclarationSyntax @delegate => @delegate.ParameterList,
            BaseMethodDeclarationSyntax method => method.ParameterList,
            IndexerDeclarationSyntax indexer => indexer.ParameterList,
            TypeDeclarationSyntax type => type.ParameterList,
            _ => null,
        };
        TypeParameterListSyntax? typeParameters = node switch
        {
            DelegateDeclarationSyntax @delegate => @delegate.TypeParameterList,
            MethodDeclarationSyntax method => method.TypeParameterList,
            TypeDeclarationSyntax type => type.TypeParameterList,
            _ => null,
        };
        return (parameters?.Parameters ?? []).Select(p => "parameter " + p.Identifier.Text)
            .Concat((typeParameters?.Parameters ?? []).Select(t => "type parameter " + t.Identifier.Text));
    }

    /// <summary>
    /// The key of a namespace, type or member declaration; null for any other node. Overloads differ in their
    /// signature, so a new overload next to an existing one is not the existing one.
    /// </summary>
    private static DeclarationKey? KeyOf(SyntaxNode node)
    {
        (string Name, string Signature)? declared = node switch
        {
            BaseNamespaceDeclarationSyntax @namespace => (Compact(@namespace.Name), string.Empty),
            BaseTypeDeclarationSyntax type => (
                type.Identifier.Text,
                Signature((type as TypeDeclarationSyntax)?.TypeParameterList, (type as TypeDeclarationSyntax)?.ParameterList)),
            DelegateDeclarationSyntax @delegate => (@delegate.Identifier.Text, Signature(@delegate.TypeParameterList, @delegate.ParameterList)),
            MethodDeclarationSyntax method => (
                Explicit(method.ExplicitInterfaceSpecifier) + method.Identifier.Text,
                Signature(method.TypeParameterList, method.ParameterList)),
            ConstructorDeclarationSyntax constructor => (
                constructor.Identifier.Text,
                (constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? "static " : string.Empty) + Signature(null, constructor.ParameterList)),
            DestructorDeclarationSyntax destructor => ("~" + destructor.Identifier.Text, string.Empty),
            OperatorDeclarationSyntax @operator => (@operator.OperatorToken.Text, Signature(null, @operator.ParameterList)),
            ConversionOperatorDeclarationSyntax conversion => (
                conversion.ImplicitOrExplicitKeyword.Text + " " + Compact(conversion.Type),
                Signature(null, conversion.ParameterList)),
            PropertyDeclarationSyntax property => (Explicit(property.ExplicitInterfaceSpecifier) + property.Identifier.Text, string.Empty),
            IndexerDeclarationSyntax indexer => (Explicit(indexer.ExplicitInterfaceSpecifier) + "this", Signature(null, indexer.ParameterList)),
            EventDeclarationSyntax @event => (Explicit(@event.ExplicitInterfaceSpecifier) + @event.Identifier.Text, string.Empty),
            VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } variable => (variable.Identifier.Text, string.Empty),
            EnumMemberDeclarationSyntax member => (member.Identifier.Text, string.Empty),
            _ => null,
        };
        if (declared is not var (name, signature))
        {
            return null;
        }

        // The kind of a variable is the one of the field it belongs to.
        SyntaxKind kind = node is VariableDeclaratorSyntax ? node.Parent!.Parent!.Kind() : node.Kind();
        return new DeclarationKey(ContainerOf(node), name, kind.ToString(), signature);
    }

    /// <summary>The names of the namespaces and types around <paramref name="node"/>, outermost first, joined with dots.</summary>
    private static string ContainerOf(SyntaxNode node) => string.Join(
        '.',
        node.Ancestors()
            .Select(a => a switch
            {
                BaseNamespaceDeclarationSyntax @namespace => Compact(@namespace.Name),
                BaseTypeDeclarationSyntax type => type.Identifier.Text,
                _ => null,
            })
            .OfType<string>()
            .Reverse());

    /// <summary>The generic arity and the parameter types: <c>`1(int,ref string)</c>; empty for neither.</summary>
    private static string Signature(TypeParameterListSyntax? typeParameters, BaseParameterListSyntax? parameters)
    {
        string arity = typeParameters is null ? string.Empty : "`" + typeParameters.Parameters.Count;
        if (parameters is null)
        {
            return arity;
        }

        IEnumerable<string> types = parameters.Parameters.Select(p =>
            string.Concat(p.Modifiers
                .Where(m => m.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword or SyntaxKind.ReadOnlyKeyword)
                .Select(m => m.Text + " ")) + (p.Type is null ? string.Empty : Compact(p.Type)));
        return arity + "(" + string.Join(',', types) + ")";
    }

    private static string Explicit(ExplicitInterfaceSpecifierSyntax? specifier) =>
        specifier is null ? string.Empty : Compact(specifier.Name) + ".";

    /// <summary>The node's text without trivia and whitespace, so formatting does not make a different key.</summary>
    private static string Compact(SyntaxNode node) =>
        string.Concat(node.WithoutTrivia().ToString().Where(c => !char.IsWhiteSpace(c)));

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
