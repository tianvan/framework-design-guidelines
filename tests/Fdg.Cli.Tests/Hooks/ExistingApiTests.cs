using Fdg.Cli.Hooks;

namespace Fdg.Cli.Tests.Hooks;

public sealed class ExistingApiTests
{
    [Fact]
    public void Method_present_at_turn_start_is_existing()
    {
        string baseline = "class C { void M() { } }";
        string current = "class C { void M() { } }";

        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 16));
    }

    [Fact]
    public void Parameter_of_existing_method_is_existing()
    {
        string baseline = "class C { void M(int x) { } }";
        string current = "class C { void M(int x) { } }";

        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 22));
    }

    [Fact]
    public void New_method_in_existing_type_is_new()
    {
        string baseline = "class C { }";
        string current = "class C { void M() { } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 16));
    }

    [Fact]
    public void Renamed_type_is_new()
    {
        string baseline = "class OldName { }";
        string current = "class NewName { }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 7));
    }

    [Fact]
    public void Field_turned_into_property_with_same_name_is_new()
    {
        string baseline = "class C { int Count; }";
        string current = "class C { int Count { get; } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 19));
    }

    [Fact]
    public void Same_name_in_another_type_is_new()
    {
        string baseline = "class A { void M() { } }";
        string current = "class B { void M() { } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 16));
    }

    [Fact]
    public void Empty_baseline_means_new()
    {
        string baseline = "";
        string current = "class C { void M() { } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 16));
    }

    [Theory]
    [InlineData("class C { public C(int x) { } }", 1, 18)]
    [InlineData("class C { ~C() { } }", 1, 13)]
    [InlineData("class C { static C() { } }", 1, 18)]
    [InlineData("class C { int this[int i] { get => i; } }", 1, 17)]
    [InlineData("class C { public static C operator +(C a, C b) => a; }", 1, 38)]
    [InlineData("class C { public static implicit operator int(C a) => 0; }", 1, 51)]
    [InlineData("class C { event System.EventHandler E; }", 1, 37)]
    [InlineData("class C { int a, b; }", 1, 18)]
    [InlineData("class C { void M<T>(T t) { } }", 1, 16)]
    [InlineData("class C { int P { get; set; } }", 1, 23)]
    [InlineData("class C { void I.M() { } }", 1, 18)]
    [InlineData("class Outer { class Inner { void M() { } } }", 1, 34)]
    [InlineData("namespace N { class C { void M() { } } }", 1, 30)]
    [InlineData("namespace N; class C { void M() { } }", 1, 29)]
    [InlineData("enum E { A, B }", 1, 12)]
    [InlineData("delegate void D(int x);", 1, 15)]
    [InlineData("record R(int X);", 1, 8)]
    public void Declarations_unchanged_since_turn_start_are_existing(string source, int line, int column)
    {
        Assert.True(ExistingApi.IsExisting(source, source, line, column));
    }

    // Only the API surface counts: a position in code is a local, a lambda parameter or a pattern variable, never API.
    [Theory]
    [InlineData("class C { void M() { int local = 0; } }", 1, 26)]
    [InlineData("class C { void M() { void Local() { } } }", 1, 28)]
    [InlineData("class C { void M() { System.Func<int, int> f = Bad => Bad; } }", 1, 49)]
    [InlineData("class C { void M(object o) { if (o is int Bad) { } } }", 1, 43)]
    [InlineData("class C { int M() => 0; }", 1, 22)]
    [InlineData("class C { int P { get { int local = 0; return local; } } }", 1, 30)]
    [InlineData("class C { int field = 0; }", 1, 23)]
    [InlineData("class C { int P { get; } = 0; }", 1, 28)]
    [InlineData("class C { C() : this(0) { } C(int x) { } }", 1, 17)]
    [InlineData("class C { [System.Obsolete] void M() { } }", 1, 21)]
    [InlineData("int local = 0;", 1, 5)]
    [InlineData("class C { void M() { } }\nint local = 0;", 2, 5)]
    public void Positions_in_code_or_on_attributes_are_new_even_in_an_unchanged_file(string source, int line, int column)
    {
        Assert.False(ExistingApi.IsExisting(source, source, line, column));
        Assert.Null(DeclarationLocator.DeclarationAt(source, line, column));
    }

    [Fact]
    public void Renamed_parameter_of_existing_method_is_new()
    {
        string baseline = "class C { void M(int x) { } }";
        string current = "class C { void M(int Bad) { } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 22));
        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 16));
    }

    [Fact]
    public void Renamed_type_parameter_of_existing_method_is_new()
    {
        string baseline = "class C { void M<T>(int t) { } }";
        string current = "class C { void M<Bad>(int t) { } }";

        // The arity and parameter types are the same, so the method is the same; only the name of its type parameter is new.
        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 18));
        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 16));
        Assert.True(ExistingApi.IsExisting(baseline, baseline, 1, 18));
    }

    [Fact]
    public void Type_parameter_of_existing_type_is_existing_and_a_renamed_one_is_new()
    {
        Assert.True(ExistingApi.IsExisting("class C<T> { }", "class C<T> { }", 1, 9));
        Assert.False(ExistingApi.IsExisting("class C<Bad> { }", "class C<T> { }", 1, 9));
    }

    [Fact]
    public void Added_record_parameter_is_new()
    {
        string baseline = "record R(int X);";
        string current = "record R(int X, int Y);";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 21));
        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 8));
        Assert.True(ExistingApi.IsExisting(baseline, baseline, 1, 14));
    }

    [Fact]
    public void Added_primary_constructor_parameter_of_a_class_is_new()
    {
        Assert.False(ExistingApi.IsExisting("class C(int x, int y) { }", "class C(int x) { }", 1, 20));
        Assert.True(ExistingApi.IsExisting("class C(int x) { }", "class C(int x) { }", 1, 13));
    }

    [Fact]
    public void Attribute_position_is_null_so_the_issue_stays_blocking()
    {
        string source = "class C { [System.Obsolete] void M() { } }";

        Assert.Null(DeclarationLocator.DeclarationAt(source, 1, 12));
        Assert.Equal(
            new DeclarationKey("C", "M", "MethodDeclaration", "()"),
            DeclarationLocator.DeclarationAt(source, 1, 34));
    }

    [Fact]
    public void Base_list_and_constraints_belong_to_the_type()
    {
        string source = "interface I { } class C : I { } class D<T> where T : class { }";

        Assert.Equal(new DeclarationKey("", "C", "ClassDeclaration"), DeclarationLocator.DeclarationAt(source, 1, 27));
        Assert.True(ExistingApi.IsExisting(source, source, 1, 27));
        Assert.True(ExistingApi.IsExisting(source, source, 1, 56));
        // The base list is not part of the key: a type that gains a base type is still the existing type.
        Assert.True(ExistingApi.IsExisting("interface I { } class C : I { }", "interface I { } class C { }", 1, 27));
    }

    [Fact]
    public void Partial_type_is_existing_when_its_name_was_declared_before()
    {
        string baseline = "partial class C { void A() { } }";
        string current = "partial class C { void A() { } }\npartial class C { void B() { } }";

        Assert.True(ExistingApi.IsExisting(current, baseline, 2, 15));
        Assert.False(ExistingApi.IsExisting(current, baseline, 2, 24));
        Assert.False(ExistingApi.IsExisting("partial class D { }", baseline, 1, 15));
    }

    [Fact]
    public void Declared_keys_are_computed_once_per_file_and_reused()
    {
        var current = DeclarationLocator.Parse("class C { void M(int Bad) { } void N() { } }");
        var keys = DeclarationLocator.DeclaredKeys(DeclarationLocator.Parse("class C { void M(int x) { } }"));

        Assert.NotNull(keys);
        Assert.False(ExistingApi.IsExisting(current, keys, 1, 22));
        Assert.True(ExistingApi.IsExisting(current, keys, 1, 16));
        Assert.False(ExistingApi.IsExisting(current, keys, 1, 36));
        Assert.False(ExistingApi.IsExisting(current, (IReadOnlySet<DeclarationKey>?)null, 1, 16));
        Assert.Null(DeclarationLocator.DeclaredKeys(DeclarationLocator.Parse("class C {")));
    }

    [Theory]
    [InlineData("namespace Old { class C { } }", "namespace New { class C { } }", 1, 11)]
    [InlineData("namespace N { class C { } }", "namespace N { class D { } }", 1, 21)]
    [InlineData("class C { void M<T>(T t) { } }", "class C { void M(int t) { } }", 1, 16)]
    [InlineData("class C { void M(int t) { } }", "class C { void M(ref int t) { } }", 1, 16)]
    [InlineData("class C { void M(int t) { } }", "class C { int M(long t) { } }", 1, 15)]
    [InlineData("class C { int a; }", "class C { int a, b; }", 1, 18)]
    [InlineData("class C { C() { } }", "class C { static C() { } }", 1, 11)]
    [InlineData("class C { void M() { } }", "class C { void M() { } void N() { } }", 1, 29)]
    [InlineData("class C<T> { }", "class C { }", 1, 7)]
    [InlineData("class C { enum E { } }", "class C { enum E { A } }", 1, 20)]
    public void Declarations_that_differ_from_the_turn_start_are_new(string baseline, string current, int line, int column)
    {
        Assert.False(ExistingApi.IsExisting(current, baseline, line, column));
    }

    [Fact]
    public void Formatting_and_parameter_names_do_not_make_a_declaration_new()
    {
        string baseline = "class C { void M(int  x,string y) { } }";
        string current = "class C\n{\n    void M(int a, string  b)\n    {\n    }\n}\n";

        Assert.True(ExistingApi.IsExisting(current, baseline, 3, 10));
    }

    [Fact]
    public void Syntax_error_in_either_source_means_new()
    {
        string valid = "class C { void M() { } }";
        string broken = "class C { void M() { }";

        Assert.False(ExistingApi.IsExisting(broken, valid, 1, 16));
        Assert.False(ExistingApi.IsExisting(valid, broken, 1, 16));
    }

    [Fact]
    public void Position_outside_any_declaration_means_new()
    {
        string source = "using System;\nclass C { void M() { } }";

        Assert.False(ExistingApi.IsExisting(source, source, 1, 7));
        Assert.False(ExistingApi.IsExisting(source, source, 5, 1));
    }

    [Fact]
    public void Unreadable_baseline_text_means_new()
    {
        Assert.False(ExistingApi.IsExisting("class C { void M() { } }", "\0\0 not C#", 1, 16));
    }

    [Fact]
    public void Position_on_the_type_of_a_multi_variable_field_is_none_of_its_variables()
    {
        string source = "class C { int a, b; }";

        Assert.Null(DeclarationLocator.DeclarationAt(source, 1, 11));
        Assert.Equal(new DeclarationKey("C", "b", "FieldDeclaration"), DeclarationLocator.DeclarationAt(source, 1, 18));
    }

    [Fact]
    public void Key_names_container_name_kind_and_overload()
    {
        string source = "namespace N.M { class Outer<T> { class Inner { void Run(ref int a, string b) { } } } }";

        Assert.Equal(
            new DeclarationKey("N.M.Outer.Inner", "Run", "MethodDeclaration", "(ref int,string)"),
            DeclarationLocator.DeclarationAt(source, 1, 56));
        Assert.Equal(
            new DeclarationKey("N.M.Outer.Inner", "Run", "MethodDeclaration", "(ref int,string)", "parameter a"),
            DeclarationLocator.DeclarationAt(source, 1, 65));
        Assert.Equal(new DeclarationKey("N.M", "Outer", "ClassDeclaration", "`1"), DeclarationLocator.DeclarationAt(source, 1, 23));
        Assert.True(DeclarationLocator.Declares(source, new DeclarationKey("N.M", "Outer", "ClassDeclaration", "`1")));
        Assert.False(DeclarationLocator.Declares(source, new DeclarationKey("N.M", "Outer", "ClassDeclaration")));
    }

    [Fact]
    public void New_overload_is_new()
    {
        string baseline = "class C { void M(int x) { } }";
        string current = "class C { void M(int x) { } void M(string s) { } }";

        Assert.False(ExistingApi.IsExisting(current, baseline, 1, 34));
    }

    [Fact]
    public void Existing_overload_is_existing()
    {
        string baseline = "class C { void M(int x) { } void M(string s) { } }";
        string current = "class C { void M(int x) { } void M(string s) { } }";

        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 16));
        Assert.True(ExistingApi.IsExisting(current, baseline, 1, 34));
    }
}
