using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.DelegateCallbackSuffixAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class DelegateCallbackSuffixTests
{
    [Fact]
    public Task Delegates_that_no_event_uses_need_the_callback_suffix()
    {
        return Verifier.VerifyAsync(
            """
            public delegate void {|FDG3051:Completion|}(int errorCode);

            public delegate T {|FDG3051:Factory|}<T>();

            public class Outer
            {
                public delegate bool {|FDG3051:Filter|}(object value);
            }
            """);
    }

    [Fact]
    public Task Callbacks_and_event_handlers_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public delegate void IOCompletionCallback(int errorCode);

            public delegate void ClickedEventHandler(object sender, System.EventArgs e);

            public delegate void Changed(object sender, System.EventArgs e);

            public delegate void Generic<T>(object sender, T e);

            public class Control
            {
                public event Changed TextChanged;

                public event Generic<int> ValueChanged;
            }
            """);
    }

    [Fact]
    public Task Delegates_that_are_not_externally_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal delegate void Completion(int errorCode);

            public class Outer
            {
                private delegate void Filter(object value);
            }
            """);
    }
}
