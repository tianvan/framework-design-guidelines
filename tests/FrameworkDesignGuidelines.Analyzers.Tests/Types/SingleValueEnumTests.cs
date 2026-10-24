using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.SingleValueEnumAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class SingleValueEnumTests
{
    [Fact]
    public Task Visible_enum_with_one_value_is_reported()
    {
        // The book's bad design: a reserved parameter expressed as a one-value enum.
        return Verifier.VerifyAsync(
            """
            public enum {|FDG4038:SomeOption|}
            {
                DefaultOption,
            }

            public class Outer
            {
                public enum {|FDG4038:Nested|} : byte
                {
                    Only = 3,
                }
            }
            """);
    }

    [Fact]
    public Task Enums_with_zero_or_several_values_and_hidden_enums_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public enum Empty
            {
            }

            public enum CasingOptions
            {
                CaseSensitive,
                CaseInsensitive,
            }

            internal enum Hidden
            {
                Only,
            }

            public class Outer
            {
                private enum Private
                {
                    Only,
                }
            }
            """);
    }
}
