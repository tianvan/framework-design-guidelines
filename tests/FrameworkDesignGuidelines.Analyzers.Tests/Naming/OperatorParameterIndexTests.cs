using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.OperatorParameterIndexAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class OperatorParameterIndexTests
{
    [Fact]
    public Task Book_numeric_indices_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public struct Money
            {
                public static Money operator +(Money {|FDG3086:d1|}, Money {|FDG3086:d2|})
                {
                    return d1;
                }

                public static bool operator ==(Money {|FDG3086:m1|}, Money right)
                {
                    return true;
                }

                public static bool operator !=(Money left, Money {|FDG3086:right2|})
                {
                    return false;
                }

                public static explicit operator int(Money {|FDG3086:value1|})
                {
                    return 0;
                }

                public override bool Equals(object obj)
                {
                    return false;
                }

                public override int GetHashCode()
                {
                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Book_left_right_and_value_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public struct Vector3
            {
            }

            public struct Money
            {
                public static Money operator +(Money left, Money right)
                {
                    return left;
                }

                public static Money operator -(Money value)
                {
                    return value;
                }

                public static Money operator *(Money left, Vector3 vector3)
                {
                    return left;
                }

                public static implicit operator Money(int value)
                {
                    return default(Money);
                }

                public static Money Add(Money m1, Money m2)
                {
                    return m1;
                }
            }
            """);
    }

    [Fact]
    public Task Operators_that_are_not_externally_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal struct Money
            {
                public static Money operator +(Money d1, Money d2)
                {
                    return d1;
                }
            }

            public class Outer
            {
                private struct Inner
                {
                    public static Inner operator -(Inner x1, Inner x2)
                    {
                        return x1;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_parameter()
    {
        return Verifier.VerifyWithAsync(
            """
            public struct Money
            {
                public static Money operator +(Money {|#0:d1|}, Money right)
                {
                    return d1;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3086).WithLocation(0).WithArguments("d1"));
    }
}
