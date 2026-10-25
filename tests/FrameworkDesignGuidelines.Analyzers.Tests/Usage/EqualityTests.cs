using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.EqualityAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class EqualityTests
{
    [Fact]
    public Task Mutable_classes_with_value_equality_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class {|FDG8070:Person|}
            {
                public string Name { get; set; }
                public override bool Equals(object obj) { return obj is Person other && other.Name == Name; }
                public override int GetHashCode() { return 0; }
            }

            internal class {|FDG8070:Counter|}
            {
                private int _count;
                public void Increment() { _count++; }
                public override bool Equals(object obj) { return obj is Counter other && other._count == _count; }
                public override int GetHashCode() { return _count; }
            }

            public class Entity
            {
                protected int Id;
            }

            public class {|FDG8070:Customer|} : Entity
            {
                public override bool Equals(object obj) { return obj is Customer other && other.Id == Id; }
                public override int GetHashCode() { return Id; }
            }
            """);
    }

    [Fact]
    public Task Immutable_classes_structs_and_reference_equality_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public sealed class Name
            {
                private static int s_created;
                private readonly string _value;
                public const int MaxLength = 10;

                public Name(string value) { _value = value; s_created++; }

                public string Value { get { return _value; } }
                public int Length { get; }

                public event EventHandler Changed;

                public override bool Equals(object obj) { return obj is Name other && other._value == _value; }
                public override int GetHashCode() { return _value.GetHashCode(); }
            }

            public class Node
            {
                public int Value;
                public bool Equals(Node other) { return other != null && other.Value == Value; }
            }

            public class Handle
            {
                public int Value;
            }

            public struct Cell
            {
                public int Value;
                public override bool Equals(object obj) { return obj is Cell other && other.Value == Value; }
                public override int GetHashCode() { return Value; }
            }
            """);
    }

    [Fact]
    public Task Records_are_checked_like_classes_that_override_Equals()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record {|FDG8070:Account|}
            {
                public decimal Balance { get; set; }
            }

            public record Point(int X, int Y);

            public record Labeled
            {
                public string Label { get; init; }
            }

            public record struct Mutable
            {
                public int X { get; set; }
            }
            """);
    }

    [Fact]
    public Task Types_with_an_equality_operator_but_no_IEquatable_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Money
            {
                public decimal Amount;
                public static bool operator {|FDG8102:==|}(Money left, Money right)
                {
                    return left.Amount == right.Amount;
                }

                public static bool operator !=(Money left, Money right) { return !(left == right); }
                public static bool operator ==(Money left, Money? right)
                {
                    return right.HasValue && left == right.Value;
                }

                public static bool operator !=(Money left, Money? right) { return !(left == right); }
            }

            public sealed class Tag : IEquatable<string>
            {
                private readonly string _text = "";
                public bool Equals(string other) { return other == _text; }
                public static bool operator {|FDG8102:==|}(Tag left, Tag right) { return Equals(left, right); }
                public static bool operator !=(Tag left, Tag right) { return !(left == right); }
            }

            public struct Size : IEquatable<Size>
            {
                public int Width;
                public bool Equals(Size other) { return Width == other.Width; }
                public static bool operator ==(Size left, Size right) { return left.Equals(right); }
                public static bool operator !=(Size left, Size right) { return !left.Equals(right); }
            }
            """);
    }

    [Fact]
    public Task Equality_operators_with_arguments_of_different_types_are_reported_with_missing_overloads()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public partial struct OneWay : IEquatable<OneWay>
            {
                public bool Equals(OneWay other) { return true; }
                public static bool operator {|#0:==|}(OneWay left, int right) { return true; }
                public static bool operator !=(OneWay left, int right) { return false; }
            }

            public partial struct BothWays : IEquatable<BothWays>
            {
                public bool Equals(BothWays other) { return true; }
                public static bool operator {|#1:==|}(BothWays left, int right) { return true; }
                public static bool operator !=(BothWays left, int right) { return false; }
                public static bool operator {|#2:==|}(int left, BothWays right) { return right == left; }
                public static bool operator !=(int left, BothWays right) { return right != left; }
            }

            public struct SomeType : IEquatable<SomeType>
            {
                public bool Equals(SomeType other) { return true; }
                public static bool operator {|#3:==|}(SomeType left, int right) { return true; }
                public static bool operator !=(SomeType left, int right) { return false; }
                public static bool operator {|#4:==|}(int left, SomeType right) { return right == left; }
                public static bool operator !=(int left, SomeType right) { return right != left; }
                public static bool operator ==(SomeType left, SomeType right) { return true; }
                public static bool operator !=(SomeType left, SomeType right) { return false; }
            }

            public sealed class Meters : IEquatable<Meters>
            {
                public bool Equals(Meters other) { return true; }
                public static bool operator ==(Meters left, Meters right) { return true; }
                public static bool operator !=(Meters left, Meters right) { return false; }
                public static bool operator {|#5:==|}(Meters left, Feet right) { return true; }
                public static bool operator !=(Meters left, Feet right) { return false; }
            }

            public sealed class Feet : IEquatable<Feet>
            {
                public bool Equals(Feet other) { return true; }
                public static bool operator ==(Feet left, Feet right) { return true; }
                public static bool operator !=(Feet left, Feet right) { return false; }
                public static bool operator {|#6:==|}(Feet left, Meters right) { return right == left; }
                public static bool operator !=(Feet left, Meters right) { return right != left; }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8103).WithLocation(0)
                .WithArguments("OneWay", "int", "int", "OneWay"),
            new DiagnosticResult(FdgDescriptors.FDG8103).WithLocation(0)
                .WithArguments("OneWay", "int", "OneWay", "OneWay"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(0).WithArguments("OneWay", "int"),
            new DiagnosticResult(FdgDescriptors.FDG8103).WithLocation(1)
                .WithArguments("BothWays", "int", "BothWays", "BothWays"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(1).WithArguments("BothWays", "int"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(2).WithArguments("int", "BothWays"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(3).WithArguments("SomeType", "int"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(4).WithArguments("int", "SomeType"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(5).WithArguments("Meters", "Feet"),
            new DiagnosticResult(FdgDescriptors.FDG8104).WithLocation(6).WithArguments("Feet", "Meters"));
    }

    [Fact]
    public Task Equality_operators_on_mutable_classes_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Account : IEquatable<Account>
            {
                public decimal Balance { get; set; }
                public bool Equals(Account other) { return other != null && other.Balance == Balance; }
                public static bool operator {|FDG8106:==|}(Account left, Account right) { return Equals(left, right); }
                public static bool operator !=(Account left, Account right) { return !(left == right); }
            }

            public sealed class Isbn : IEquatable<Isbn>
            {
                private readonly string _value = "";
                public string Value { get { return _value; } }
                public bool Equals(Isbn other) { return other != null && other._value == _value; }
                public static bool operator ==(Isbn left, Isbn right) { return Equals(left, right); }
                public static bool operator !=(Isbn left, Isbn right) { return !(left == right); }
            }

            public struct Cell : IEquatable<Cell>
            {
                public int Value;
                public bool Equals(Cell other) { return Value == other.Value; }
                public static bool operator ==(Cell left, Cell right) { return left.Equals(right); }
                public static bool operator !=(Cell left, Cell right) { return !left.Equals(right); }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public class {|#0:Person|}
            {
                public string Name;
                public override bool Equals(object obj) { return obj is Person other && other.Name == Name; }
                public override int GetHashCode() { return 0; }
                public static bool operator {|#1:==|}(Person left, Person right) { return Equals(left, right); }
                public static bool operator !=(Person left, Person right) { return !Equals(left, right); }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8070).WithLocation(0).WithArguments("Person"),
            new DiagnosticResult(FdgDescriptors.FDG8102).WithLocation(1).WithArguments("Person"),
            new DiagnosticResult(FdgDescriptors.FDG8106).WithLocation(1).WithArguments("Person"));
    }
}
