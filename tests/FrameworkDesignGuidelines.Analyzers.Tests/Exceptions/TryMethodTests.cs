using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.TryMethodAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class TryMethodTests
{
    [Fact]
    public Task Value_producing_try_methods_that_do_not_return_bool_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public enum ConvertResult
            {
                Success,
                Overflow,
            }

            public class Parser
            {
                public int {|FDG7064:TryParse|}(string text, out int value)
                {
                    value = 0;
                    return 0;
                }

                internal ConvertResult {|FDG7064:TryConvert|}(object input, out object value)
                {
                    value = input;
                    return ConvertResult.Success;
                }

                private static void {|FDG7064:TryRead|}(out string text)
                {
                    text = null;
                }
            }

            public interface IStore
            {
                object {|FDG7064:TryLoad|}(string key, out bool found);
            }
            """);
    }

    [Fact]
    public Task Boolean_try_methods_attempts_other_names_and_inherited_members_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading.Tasks;

            public interface IStore
            {
                object {|FDG7064:TryLoad|}(string key, out bool found);
            }

            public abstract class StoreBase
            {
                public abstract string {|FDG7064:TryFind|}(string key, out int index);
            }

            public class Store : StoreBase, IStore
            {
                public object TryLoad(string key, out bool found)
                {
                    found = false;
                    return null;
                }

                public override string TryFind(string key, out int index)
                {
                    index = 0;
                    return null;
                }

                public bool TryGetValue(string key, out string value)
                {
                    value = null;
                    return false;
                }

                public static bool TryParse(string text, out Store store)
                {
                    store = null;
                    return false;
                }

                public void TryReset()
                {
                }

                public Task TryDisposeAsync()
                {
                    return Task.FromResult(0);
                }

                public string TryGetName(int id, ref int attempts)
                {
                    return null;
                }

                public int Try(out int value)
                {
                    value = 0;
                    return 0;
                }

                public int Trying(out int value)
                {
                    value = 0;
                    return 0;
                }

                public int Retry(out int value)
                {
                    value = 0;
                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_method()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Parser
            {
                public int {|#0:TryParse|}(string text, out int value)
                {
                    value = 0;
                    return 0;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG7064).WithLocation(0).WithArguments("TryParse"));
    }
}
