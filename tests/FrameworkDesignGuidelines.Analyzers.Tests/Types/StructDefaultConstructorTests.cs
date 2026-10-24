using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.StructDefaultConstructorAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class StructDefaultConstructorTests
{
    [Fact]
    public Task Parameterless_struct_constructor_is_reported()
    {
        // C# 10 and later allow it.
        return Verifier.VerifyLatestAsync(
            """
            public struct Counter
            {
                public {|FDG4026:Counter|}()
                {
                    Value = 1;
                }

                public int Value { get; }
            }

            internal record struct Pair
            {
                public {|FDG4026:Pair|}()
                {
                    First = 1;
                }

                public int First { get; init; }
            }
            """);
    }

    [Fact]
    public Task Structs_without_a_parameterless_constructor_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public struct ZipCode
            {
                public ZipCode(int fiveDigitCode)
                {
                    FiveDigitCode = fiveDigitCode;
                }

                public int FiveDigitCode { get; }
            }

            public struct Empty
            {
            }

            public class Widget
            {
                public Widget()
                {
                }
            }
            """);
    }
}
