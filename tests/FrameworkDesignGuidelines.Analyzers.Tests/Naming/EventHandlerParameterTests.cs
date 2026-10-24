using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.EventHandlerParameterAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class EventHandlerParameterTests
{
    [Fact]
    public Task Book_pattern_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public delegate void ClickedEventHandler(object sender, ClickedEventArgs e);

            public class ClickedEventArgs : EventArgs
            {
            }

            public class Button
            {
                public event ClickedEventHandler Clicked;

                public event EventHandler<ClickedEventArgs> Pressed;
            }
            """);
    }

    [Fact]
    public Task Event_handlers_with_other_parameters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public delegate void {|FDG3075:ClosedEventHandler|}(object source, EventArgs args);

            public delegate void {|FDG3075:OpenedEventHandler|}(object sender);

            public delegate void {|FDG3075:Changed|}(object sender, EventArgs e, int value);

            public class Window
            {
                public event Changed TextChanged;
            }
            """);
    }

    [Fact]
    public Task Other_delegates_and_hidden_handlers_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public delegate void CompletionCallback(int errorCode);

            internal delegate void ClosedEventHandler(object source, EventArgs args);

            public class Window
            {
                private delegate void Changed(int value);

                private event Changed TextChanged;
            }
            """);
    }
}
