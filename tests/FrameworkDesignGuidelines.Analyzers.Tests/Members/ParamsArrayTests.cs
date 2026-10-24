using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.ParamsArrayAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class ParamsArrayTests
{
    [Fact]
    public Task Params_arrays_that_the_member_modifies_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Numbers
            {
                public Numbers(params int[] {|FDG5118:values|})
                {
                    values[0] = 1;
                    values[1] = 2;
                }

                public static int Sum(params int[] values)
                {
                    int sum = 0;
                    foreach (int value in values)
                    {
                        sum += value;
                    }

                    return sum;
                }

                public static void Normalize(params double[] {|FDG5118:values|})
                {
                    values[0] /= 2;
                }

                public static void Bump(params int[] {|FDG5118:values|})
                {
                    values[0]++;
                }

                public static void Fill(params string[] {|FDG5118:names|})
                {
                    Action clear = () => names[0] = null;
                    clear();
                }

                public static void Fill(string name, params string[] {|FDG5118:names|})
                {
                    Local();

                    void Local()
                    {
                        names[0] = name;
                    }
                }

                public static void Sort(params int[] values)
                {
                    Array.Sort(values);
                }

                public static void Copy(int[] values)
                {
                    values[0] = 1;
                }

                public static void Copy(int[] target, params int[] values)
                {
                    target[0] = values[0];
                }

                internal static void Hidden(params int[] values)
                {
                    values[0] = 1;
                }
            }
            """);
    }

    [Fact]
    public Task Null_coalescing_assignments_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public static class Names
            {
                public static void Default(params string[] {|FDG5118:names|})
                {
                    names[0] ??= "none";
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public interface IWriter
            {
                void Write(params int[] values);
            }

            public class Writer : IWriter
            {
                public void Write(params int[] values)
                {
                    values[0] = 0;
                }

                public virtual void Clear(params int[] {|FDG5118:values|})
                {
                    values[0] = 0;
                }
            }

            public class DerivedWriter : Writer
            {
                public override void Clear(params int[] values)
                {
                    values[0] = 0;
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_parameter_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public static class Numbers
            {
                public static void Normalize(params double[] {|#0:values|})
                {
                    values[0] = 0;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5118).WithLocation(0).WithArguments("values", "Normalize"));
    }
}
