using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.AbstractConstructorAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class AbstractConstructorTests
{
    [Fact]
    public Task Protected_internal_constructor_of_a_visible_abstract_class_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class Claim
            {
                protected internal {|FDG4013:Claim|}()
                {
                }

                protected internal {|FDG4013:Claim|}(int value)
                {
                }
            }

            public class Outer
            {
                public abstract class Nested
                {
                    protected internal {|FDG4013:Nested|}()
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Protected_internal_and_private_constructors_and_public_ones_are_not_reported()
    {
        // Public constructors are CA1012's; the rule maps both.
        return Verifier.VerifyAsync(
            """
            public abstract class Protected
            {
                protected Protected()
                {
                }
            }

            public abstract class Internal
            {
                internal Internal()
                {
                }
            }

            public abstract class Public
            {
                public Public()
                {
                }
            }

            public abstract class Implicit
            {
            }
            """);
    }

    [Fact]
    public Task Classes_that_are_not_abstract_or_not_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Concrete
            {
                protected internal Concrete()
                {
                }
            }

            internal abstract class Hidden
            {
                protected internal Hidden()
                {
                }
            }
            """);
    }
}
