using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.MutableStructAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class MutableStructTests
{
    [Fact]
    public Task Settable_properties_and_writable_fields_of_a_visible_struct_are_reported()
    {
        // The book's bad design: get/set properties.
        return Verifier.VerifyAsync(
            """
            public struct ZipCode
            {
                public int {|FDG4027:FiveDigitCode|} { get; set; }

                public int {|FDG4027:PlusFourExtension|} { get; set; }

                public int {|FDG4027:Extra|};

                public int {|FDG4027:this|}[int index]
                {
                    get { return index; }
                    set { }
                }
            }
            """);
    }

    [Fact]
    public Task Immutable_members_and_hidden_setters_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public struct ZipCode
            {
                public static int Shared;
                public const int Max = 99999;
                public readonly int Code;
                private int _cache;

                public ZipCode(int fiveDigitCode, int plusFourExtension)
                {
                    Code = 0;
                    _cache = 0;
                    FiveDigitCode = fiveDigitCode;
                    PlusFourExtension = plusFourExtension;
                    Internal = 0;
                }

                public int FiveDigitCode { get; }

                public int PlusFourExtension { get; private set; }

                internal int Internal { get; set; }

                public static int Total { get; set; }
            }

            internal struct Hidden
            {
                public int Value { get; set; }
            }

            public class Reference
            {
                public int Value { get; set; }
            }
            """);
    }

    [Fact]
    public Task Readonly_structs_ref_structs_and_init_accessors_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public readonly struct ZipCode
            {
                public int FiveDigitCode { get; init; }
            }

            public struct Init
            {
                public int Value { get; init; }

                public int {|FDG4027:Other|} { get; set; }
            }

            public ref struct Span
            {
                public int Length { get; set; }
            }

            public readonly record struct Point(int X, int Y);

            // A positional record struct that is not readonly has get/set properties.
            public record struct Mutable(int {|FDG4027:X|}, int {|FDG4027:Y|});
            """);
    }
}
