using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.RefKindOverloadAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class RefKindOverloadTests
{
    [Fact]
    public Task Overloads_that_differ_only_in_a_modifier_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public struct Big
            {
                public long A;
            }

            public class SomeType
            {
                public void SomeMethod(string name)
                {
                }

                public void SomeMethod(out string {|FDG5005:name|})
                {
                    name = null;
                }

                public void Swap(int value, int other)
                {
                }

                public void Swap(ref int {|FDG5005:value|}, int other)
                {
                }

                public SomeType(Big big)
                {
                }

                public SomeType(in Big {|FDG5005:big|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_member_the_modifier_and_the_parameter()
    {
        return Verifier.VerifyWithAsync(
            """
            public class SomeType
            {
                public void Fill(int[] items)
                {
                }

                public void Fill(ref int[] {|#0:items|})
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5005).WithLocation(0).WithArguments("Fill", "ref", "items"));
    }

    [Fact]
    public Task Ref_readonly_parameter_is_reported()
    {
        // C# 12 adds ref readonly parameters.
        return Verifier.VerifyLatestAsync(
            """
            public class SomeType
            {
                public void Read(int value)
                {
                }

                public void Read(ref readonly int {|FDG5005:value|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Different_types_counts_and_hidden_overloads_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class SomeType
            {
                public bool TryGet(string key, out int value)
                {
                    value = 0;
                    return true;
                }

                public bool TryGet(int key, out int value)
                {
                    value = 0;
                    return true;
                }

                public void Parse(string text)
                {
                }

                public void Parse(string text, out int value)
                {
                    value = 0;
                }

                public void Hidden(int value)
                {
                }

                private void Hidden(ref int value)
                {
                }
            }
            """);
    }
}
