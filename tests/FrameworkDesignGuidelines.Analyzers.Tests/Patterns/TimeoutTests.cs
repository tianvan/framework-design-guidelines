using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.TimeoutAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class TimeoutTests
{
    [Fact]
    public Task Integer_timeouts_without_a_unit_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Connection
            {
                public Connection(int {|FDG9130:timeout|}) { }

                public int {|FDG9130:ReadTimeout|} { get; set; }

                public long {|FDG9130:WriteTimeout|} => 0;

                public void Open(uint {|FDG9130:connectTimeout|}) { }

                public int this[short {|FDG9130:timeout|}] => 0;

                public void Wait(TimeSpan timeout) { }

                public void Wait(int millisecondsTimeout) { }

                public void Poll(int timeoutMs) { }

                public void Spin(long nanosecondsTimeout) { }

                public void Ping(int msTimeout) { }

                public void Idle(int secondsTimeout) { }

                public void Retry(int timeoutInSeconds) { }

                public void Send(int timeouts) { }

                public void Close(int timeoutCount) { }

                public void Sleep(double timeout) { }

                internal void Hidden(int timeout) { }

                public int timeout;
            }

            public delegate void Callback(byte {|FDG9130:timeout|});

            internal class Internal
            {
                public void Open(int timeout) { }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public interface IChannel
            {
                int {|FDG9130:Timeout|} { get; }

                void Open(int {|FDG9130:timeout|});
            }

            public class Channel : IChannel
            {
                public int Timeout => 0;

                public void Open(int timeout) { }
            }

            public abstract class Base
            {
                public abstract void Close(int {|FDG9130:timeout|});
            }

            public class Derived : Base
            {
                public override void Close(int timeout) { }
            }
            """);
    }

    [Fact]
    public Task Positional_record_parameters_are_reported_once()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record Settings(int {|FDG9130:Timeout|});
            """);
    }

    [Fact]
    public Task Message_names_the_member_and_the_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Connection
            {
                public void Open(ulong {|#0:timeout|}) { }

                public short {|#1:IdleTimeout|} { get; set; }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9130).WithLocation(0)
                .WithArguments("timeout", "Connection.Open", "ulong"),
            new DiagnosticResult(FdgDescriptors.FDG9130).WithLocation(1)
                .WithArguments("IdleTimeout", "Connection", "short"));
    }
}
