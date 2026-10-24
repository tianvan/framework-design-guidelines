using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.EventInvocationAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class EventInvocationTests
{
    [Fact]
    public Task Null_sender_and_null_event_data_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                public static event EventHandler Created;

                internal event EventHandler<EventArgs> Snoozed;

                public void Raise(EventArgs e)
                {
                    AlarmRaised?.Invoke({|FDG5061:null|}, e);
                    AlarmRaised({|FDG5061:null|}, {|FDG5063:null|});
                    AlarmRaised.Invoke(this, {|FDG5063:default(EventArgs)|});
                    Snoozed?.Invoke(e: {|FDG5063:null|}, sender: {|FDG5061:null|});
                    Created?.Invoke({|FDG5062:this|}, EventArgs.Empty);
                    Created?.Invoke({|FDG5062:typeof(AlarmClock)|}, {|FDG5063:null|});
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_event()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                public void Raise()
                {
                    AlarmRaised?.Invoke({|#0:null|}, EventArgs.Empty);
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5061).WithLocation(0).WithArguments("AlarmRaised"));
    }

    [Fact]
    public Task Conforming_calls_and_other_delegates_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                public static event EventHandler Created;

                public event Action<string, EventArgs> Named;

                public event Action<object> Single;

                public event Action<object, string> Texted;

                private Action<object, EventArgs> _callback;

                public void Raise(object sender)
                {
                    AlarmRaised?.Invoke(this, EventArgs.Empty);
                    AlarmRaised?.Invoke(sender, EventArgs.Empty);
                    Created?.Invoke(null, EventArgs.Empty);
                    Created?.Invoke(default(object), EventArgs.Empty);
                    Named?.Invoke(null, EventArgs.Empty);
                    Single?.Invoke(null);
                    Texted?.Invoke(this, null);
                    _callback?.Invoke(null, null);

                    EventHandler handler = AlarmRaised;
                    if (handler != null)
                    {
                        handler(null, null);
                    }
                }
            }
            """);
    }
}
