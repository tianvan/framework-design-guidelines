using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.InequalityOperatorAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class InequalityOperatorTests
{
    [Fact]
    public Task Comparable_and_complete_operators_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Money : IComparable<Money>, IEquatable<Money>
            {
                public int CompareTo(Money other) => 0;

                public bool Equals(Money other) => true;

                public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

                public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

                public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

                public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

                public static bool operator <(Money? left, Money? right) => false;

                public static bool operator >(Money? left, Money? right) => false;
            }

            public class Version : IComparable<Version>
            {
                public int CompareTo(Version other) => 0;

                public static bool operator <(Version left, Version right) => left.CompareTo(right) < 0;

                public static bool operator >(Version left, Version right) => left.CompareTo(right) > 0;
            }

            internal struct Hidden
            {
                public static int operator <(Hidden left, int right) => 0;

                public static int operator >(Hidden left, int right) => 0;
            }

            public struct Plain : IEquatable<Plain>
            {
                public bool Equals(Plain other) => true;
            }
            """);
    }

    [Fact]
    public Task Type_with_inequality_operators_and_without_IComparable_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct {|FDG5094:Distance|}
            {
                public static bool operator <(Distance left, Distance right) => false;

                public static bool operator >(Distance left, Distance right) => false;
            }

            public class {|FDG5094:Score|} : IComparable<int>, IComparable
            {
                public int CompareTo(int other) => 0;

                public int CompareTo(object other) => 0;

                public static bool operator <=(Score left, Score right) => false;

                public static bool operator >=(Score left, Score right) => false;
            }

            public class Box<T> : IComparable<Box<T>>
            {
                public int CompareTo(Box<T> other) => 0;

                public static bool operator <(Box<T> left, Box<T> right) => false;

                public static bool operator >(Box<T> left, Box<T> right) => false;
            }
            """);
    }

    [Fact]
    public Task Message_names_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public struct {|#0:Distance|}
            {
                public static bool operator <(Distance left, Distance right) => false;

                public static bool operator >(Distance left, Distance right) => false;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5094).WithLocation(0).WithArguments("Distance"));
    }

    [Fact]
    public Task Equatable_type_with_operator_less_than_and_no_operator_less_than_or_equal_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Money : IComparable<Money>, IEquatable<Money>
            {
                public int CompareTo(Money other) => 0;

                public bool Equals(Money other) => true;

                public static bool operator {|FDG5096:<|}(Money left, Money right) => false;

                public static bool operator >(Money left, Money right) => false;
            }

            public struct Count : IComparable<Count>
            {
                public int CompareTo(Count other) => 0;

                public static bool operator <(Count left, Count right) => false;

                public static bool operator >(Count left, Count right) => false;
            }
            """);
    }

    [Fact]
    public Task Non_Boolean_inequality_operator_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Shape : IComparable<Shape>
            {
                public int CompareTo(Shape other) => 0;

                public static Shape operator {|FDG5097:<|}(Shape left, Shape right) => left;

                public static Shape operator {|FDG5097:>|}(Shape left, Shape right) => left;

                public static Shape operator +(Shape left, Shape right) => left;
            }
            """);
    }

    [Fact]
    public Task Message_names_the_operator()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public struct Shape : IComparable<Shape>
            {
                public int CompareTo(Shape other) => 0;

                public static int operator {|#0:<|}(Shape left, Shape right) => 0;

                public static int operator {|#1:>|}(Shape left, Shape right) => 0;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5097).WithLocation(0).WithArguments("operator <"),
            new DiagnosticResult(FdgDescriptors.FDG5097).WithLocation(1).WithArguments("operator >"));
    }

    [Fact]
    public Task Inequality_operator_against_another_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Money : IComparable<Money>
            {
                public int CompareTo(Money other) => 0;

                public static bool operator {|FDG5099:<|}(Money left, decimal right) => false;

                public static bool operator {|FDG5099:>|}(Money left, decimal right) => false;

                public static bool operator {|FDG5099:<|}(decimal left, Money right) => false;

                public static bool operator {|FDG5099:>|}(decimal left, Money right) => false;
            }
            """);
    }

    [Fact]
    public Task Message_names_the_operator_and_the_other_type()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public struct Money : IComparable<Money>
            {
                public int CompareTo(Money other) => 0;

                public static bool operator {|#0:<|}(Money left, DateTime right) => false;

                public static bool operator {|#1:>|}(Money left, DateTime right) => false;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5099).WithLocation(0).WithArguments("operator <", "DateTime"),
            new DiagnosticResult(FdgDescriptors.FDG5099).WithLocation(1).WithArguments("operator >", "DateTime"));
    }

    [Fact]
    public Task Interface_operators_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public interface IOrdered<TSelf>
                where TSelf : IOrdered<TSelf>
            {
                static abstract int operator <(TSelf left, int right);

                static abstract int operator >(TSelf left, int right);
            }
            """);
    }
}
