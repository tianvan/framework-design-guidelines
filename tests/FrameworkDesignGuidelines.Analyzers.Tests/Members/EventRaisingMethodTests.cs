using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.EventRaisingMethodAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class EventRaisingMethodTests
{
    [Fact]
    public Task Event_without_a_protected_virtual_raising_method_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.ComponentModel;

            public class AlarmRaisedEventArgs : EventArgs
            {
            }

            public class AlarmClock : INotifyPropertyChanged
            {
                public event EventHandler<AlarmRaisedEventArgs> {|FDG5059:AlarmRaised|};

                public event EventHandler {|FDG5059:Snoozed|};

                public event EventHandler {|FDG5059:Stopped|};

                public event PropertyChangedEventHandler {|FDG5059:PropertyChanged|};

                public event EventHandler {|FDG5059:Reset|}
                {
                    add { }
                    remove { }
                }

                public virtual void OnSnoozed(EventArgs e)
                {
                }

                private protected virtual void OnStopped(EventArgs e)
                {
                }

                protected void OnPropertyChanged(PropertyChangedEventArgs e)
                {
                    PropertyChanged?.Invoke(this, e);
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
                public event EventHandler {|#0:AlarmRaised|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5059).WithLocation(0).WithArguments("AlarmRaised"));
    }

    [Fact]
    public Task Events_the_text_exempts_or_that_have_a_raising_method_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                public static event EventHandler Created;

                protected event EventHandler Snoozed;

                private event EventHandler Hidden;

                protected virtual void OnAlarmRaised(EventArgs e)
                {
                    AlarmRaised?.Invoke(this, e);
                    Hidden?.Invoke(this, e);
                }

                protected internal virtual void OnSnoozed(EventArgs e)
                {
                    Snoozed?.Invoke(this, e);
                }
            }

            public class TravelClock : AlarmClock
            {
                public event EventHandler Moved;

                protected override void OnAlarmRaised(EventArgs e)
                {
                    base.OnAlarmRaised(e);
                }

                protected virtual void OnMoved(EventArgs e)
                {
                    Moved?.Invoke(this, e);
                }
            }

            public sealed class SealedClock
            {
                public event EventHandler AlarmRaised;
            }

            public struct ClockState
            {
                public event EventHandler Changed;
            }

            public static class Clocks
            {
                public static event EventHandler Created;
            }

            internal class InternalClock
            {
                public event EventHandler AlarmRaised;
            }

            public abstract class BaseTimer
            {
                public abstract event EventHandler Elapsed;

                public virtual event EventHandler Started;

                protected virtual void OnStarted(EventArgs e)
                {
                }
            }

            public class Timer : BaseTimer
            {
                public override event EventHandler Elapsed;

                public override event EventHandler Started;
            }

            public interface ITicker
            {
                event EventHandler Ticked;
            }

            public class Ticker : ITicker
            {
                event EventHandler ITicker.Ticked
                {
                    add { }
                    remove { }
                }
            }
            """);
    }

    [Fact]
    public Task Raising_method_that_does_not_take_one_parameter_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                public event EventHandler Snoozed;

                protected virtual void {|FDG5060:OnAlarmRaised|}()
                {
                    AlarmRaised?.Invoke(this, EventArgs.Empty);
                }

                protected virtual void {|FDG5060:OnSnoozed|}(object sender, EventArgs e)
                {
                    Snoozed?.Invoke(sender, e);
                }
            }

            public class TravelClock : AlarmClock
            {
                public event EventHandler Moved;

                protected new void {|FDG5060:OnAlarmRaised|}()
                {
                }

                protected virtual void OnMoved(EventArgs e)
                {
                    Moved?.Invoke(this, e);
                }

                protected void {|FDG5060:OnMoved|}(string reason, EventArgs e)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_method()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class AlarmClock
            {
                public event EventHandler AlarmRaised;

                protected virtual void {|#0:OnAlarmRaised|}()
                {
                    AlarmRaised?.Invoke(this, EventArgs.Empty);
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5060).WithLocation(0).WithArguments("OnAlarmRaised"));
    }

    [Fact]
    public Task Overrides_hidden_methods_and_methods_named_for_no_event_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public abstract class Control
            {
                public event EventHandler Click;

                protected abstract void OnClick(EventArgs e);

                protected virtual void OnLoad()
                {
                }

                private void OnClick()
                {
                    Click?.Invoke(this, EventArgs.Empty);
                }

                internal void OnClick(object sender, EventArgs e)
                {
                }
            }

            public class Button : Control
            {
                protected override void OnClick(EventArgs e)
                {
                }
            }

            internal class Hidden
            {
                public event EventHandler Click;

                protected virtual void OnClick()
                {
                    Click?.Invoke(this, EventArgs.Empty);
                }
            }
            """);
    }
}
