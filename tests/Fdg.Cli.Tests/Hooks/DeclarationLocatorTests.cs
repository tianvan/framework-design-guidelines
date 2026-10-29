using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class DeclarationLocatorTests
{
    [Theory]
    [InlineData("class C { private int Count; }", 1, 23, NamingSymbolKind.PrivateField)]
    [InlineData("class C { int count; }", 1, 15, NamingSymbolKind.PrivateField)]
    [InlineData("class C { protected int count; }", 1, 25, NamingSymbolKind.VisibleField)]
    [InlineData("class C { private const int max = 1; }", 1, 29, NamingSymbolKind.ConstField)]
    [InlineData("class C { void M(int Value) { } }", 1, 22, NamingSymbolKind.Parameter)]
    [InlineData("class C { void M() { int X = 0; } }", 1, 26, NamingSymbolKind.Local)]
    [InlineData("class C { void M() { const int x = 0; } }", 1, 32, NamingSymbolKind.ConstLocal)]
    [InlineData("interface foo { }", 1, 11, NamingSymbolKind.Type)]
    [InlineData("class C<t> { }", 1, 9, NamingSymbolKind.TypeParameter)]
    [InlineData("class C { public static int count; }", 1, 29, NamingSymbolKind.VisibleField)]
    [InlineData("class C { protected internal int count; }", 1, 34, NamingSymbolKind.VisibleField)]
    [InlineData("class C { private protected int count; }", 1, 33, NamingSymbolKind.PrivateField)]
    [InlineData("class C { void m() { } }", 1, 16, NamingSymbolKind.Method)]
    [InlineData("class C { int count { get; } }", 1, 15, NamingSymbolKind.Property)]
    [InlineData("class C { event System.EventHandler changed; }", 1, 37, NamingSymbolKind.Event)]
    [InlineData("class C { event System.EventHandler changed { add { } remove { } } }", 1, 37, NamingSymbolKind.Event)]
    [InlineData("namespace foo.Bar { }", 1, 11, NamingSymbolKind.Namespace)]
    [InlineData("namespace foo;", 1, 11, NamingSymbolKind.Namespace)]
    [InlineData("struct s { }", 1, 8, NamingSymbolKind.Type)]
    [InlineData("enum e { A }", 1, 6, NamingSymbolKind.Type)]
    [InlineData("record r(int A);", 1, 8, NamingSymbolKind.Type)]
    [InlineData("delegate void d();", 1, 15, NamingSymbolKind.Type)]
    [InlineData("class C { void M() { foreach (var X in new int[0]) { } } }", 1, 35, NamingSymbolKind.Local)]
    [InlineData("class C { void M() { for (int I = 0; ; ) { } } }", 1, 31, NamingSymbolKind.Local)]
    [InlineData("class C { void M() { try { } catch (System.Exception E) { } } }", 1, 54, NamingSymbolKind.Local)]
    [InlineData("class C { void M() { int.TryParse(\"1\", out var X); } }", 1, 48, NamingSymbolKind.Local)]
    [InlineData("class C { void M() { System.Action<int> a = X => { }; } }", 1, 45, NamingSymbolKind.Parameter)]
    public void Finds_the_kind_of_the_declared_name(string source, int line, int column, NamingSymbolKind expected)
    {
        Assert.Equal([expected], DeclarationLocator.NamingKindsAt(source, line, column));
    }

    [Theory]
    [InlineData("class C { public const int minValue = 0; }", 1, 28)]
    [InlineData("class C { protected const int minValue = 0; }", 1, 31)]
    [InlineData("class C { protected internal const int minValue = 0; }", 1, 40)]
    public void Visible_const_has_both_kinds(string source, int line, int column)
    {
        Assert.Equal([NamingSymbolKind.ConstField, NamingSymbolKind.VisibleField], DeclarationLocator.NamingKindsAt(source, line, column));
    }

    [Fact]
    public void Private_const_is_only_const_field()
    {
        Assert.Equal([NamingSymbolKind.ConstField], DeclarationLocator.NamingKindsAt("class C { private const int x = 0; }", 1, 29));
        Assert.Equal([NamingSymbolKind.ConstField], DeclarationLocator.NamingKindsAt("class C { const int x = 0; }", 1, 21));
        Assert.Equal([NamingSymbolKind.ConstField], DeclarationLocator.NamingKindsAt("class C { private protected const int x = 0; }", 1, 39));
    }

    [Fact]
    public void Field_without_modifier_in_an_interface_is_visible()
    {
        Assert.Equal([NamingSymbolKind.VisibleField], DeclarationLocator.NamingKindsAt("interface I { static int count; }", 1, 26));
        Assert.Equal([NamingSymbolKind.PrivateField], DeclarationLocator.NamingKindsAt("interface I { private static int count; }", 1, 34));
        Assert.Equal([NamingSymbolKind.PrivateField], DeclarationLocator.NamingKindsAt("struct S { int count; }", 1, 16));
    }

    [Fact]
    public void Finds_the_declaration_on_a_later_line()
    {
        const string source = "namespace N\n{\n    class C\n    {\n        private int count;\n    }\n}\n";

        Assert.Equal([NamingSymbolKind.PrivateField], DeclarationLocator.NamingKindsAt(source, 5, 21));
        Assert.Equal([NamingSymbolKind.PrivateField], DeclarationLocator.NamingKindsAt(source.Replace("\n", "\r\n"), 5, 21));
    }

    [Fact]
    public void Position_outside_a_declaration_name_gives_null()
    {
        // The columns are on the space before the name, on the type and the modifiers, past the end of the line, and
        // on identifiers that are expressions or types in a method body.
        const string field = "class C { private int Count; }";
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 22));
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 11));
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 19));
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 28));
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 0));
        Assert.Null(DeclarationLocator.NamingKindsAt(field, 1, 99));

        const string body = "class C { void M(Foo p) { int y = Bar + 1; Baz(y); } }";
        Assert.Null(DeclarationLocator.NamingKindsAt(body, 1, 18));
        Assert.Null(DeclarationLocator.NamingKindsAt(body, 1, 35));
        Assert.Null(DeclarationLocator.NamingKindsAt(body, 1, 44));
        Assert.Null(DeclarationLocator.NamingKindsAt(body, 1, 48));
    }

    [Fact]
    public void Line_out_of_range_gives_null()
    {
        const string source = "class C { private int Count; }";

        Assert.Null(DeclarationLocator.NamingKindsAt(source, 0, 23));
        Assert.Null(DeclarationLocator.NamingKindsAt(source, 2, 23));
        Assert.Null(DeclarationLocator.NamingKindsAt(string.Empty, 1, 1));
    }

    [Fact]
    public void Unparsable_source_still_gives_null_not_an_exception()
    {
        Assert.Null(DeclarationLocator.NamingKindsAt("class C { private int Count", 1, 23));
        Assert.Null(DeclarationLocator.NamingKindsAt("class C { private int Count; ", 1, 23));
        Assert.Null(DeclarationLocator.NamingKindsAt("public static string describe_failure(", 1, 24));
        Assert.Null(DeclarationLocator.NamingKindsAt("\0\u0001 {{{ ??? \"", 1, 3));
    }

    [Fact]
    public void Members_outside_a_type_and_top_level_statements_give_null()
    {
        Assert.Null(DeclarationLocator.NamingKindsAt("private int Count;", 1, 13));
        Assert.Null(DeclarationLocator.NamingKindsAt("int X = 0;", 1, 5));
        Assert.Null(DeclarationLocator.NamingKindsAt("System.Console.WriteLine(1);\nint X = 0;\nclass C { }\n", 2, 5));
    }

    [Fact]
    public void Symbols_without_a_naming_kind_give_null()
    {
        Assert.Null(DeclarationLocator.NamingKindsAt("enum E { a }", 1, 10));
        Assert.Null(DeclarationLocator.NamingKindsAt("class C { void M() { void local() { } } }", 1, 27));
    }

    [Theory]
    [InlineData("Naming rule violation: Missing prefix: '_'", NamingFailure.Prefix)]
    [InlineData("Naming rule violation: Prefix '_' is not expected", NamingFailure.Prefix)]
    [InlineData("Naming rule violation: Prefix 'm_' is not expected", NamingFailure.Prefix)]
    [InlineData("Naming rule violation: These words must begin with upper case characters: describe_failure", NamingFailure.Casing)]
    [InlineData("Naming rule violation: The first word, 'Value', must begin with a lower case character", NamingFailure.Casing)]
    [InlineData("Naming rule violation: These non-leading words must begin with an upper case letter: my_Value", NamingFailure.Casing)]
    [InlineData("Naming rule violation: Suffix 'Async' is not expected", null)]
    [InlineData("Naming rule violation: Missing suffix: 'Async'", null)]
    [InlineData("Naming rule violation: These words cannot contain lower case characters: Value", null)]
    [InlineData("Naming rule violation: These words must not contain underscores: a_b", null)]
    [InlineData("", null)]
    public void Classifies_the_failure_from_the_message(string message, NamingFailure? expected)
    {
        Assert.Equal(expected, DeclarationLocator.ClassifyNamingFailure(message));
    }
}
