using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.OverloadParameterOrderAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class OverloadParameterOrderTests
{
    [Fact]
    public Task Parameter_at_another_position_in_an_overload_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class EventLog
            {
                public EventLog(string logName)
                {
                }

                public EventLog(string machineName, string {|FDG5003:logName|})
                {
                }

                public int IndexOf(string value) => 0;

                public int IndexOf(int startIndex, string {|FDG5003:value|}) => 0;
            }
            """);
    }

    [Fact]
    public Task Message_names_the_parameter_and_the_overloads()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Box<T>
            {
                public Box(T value)
                {
                }

                public Box(int count, T {|#0:value|})
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5003).WithLocation(0).WithArguments("value", "Box<T>"));
    }

    [Fact]
    public Task Allowed_orders_and_hidden_overloads_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;

            public class EventLog
            {
                public EventLog()
                {
                }

                public EventLog(string logName)
                {
                }

                public EventLog(string logName, string machineName)
                {
                }

                public EventLog(string logName, string machineName, string source)
                {
                }

                public void Write(params object[] values)
                {
                }

                public void Write(string format, params object[] values)
                {
                }

                public void Add(System.Func<System.Exception> generator, int weight)
                {
                }

                public void Add<TException>(int weight)
                {
                }

                public void Sink(object sink, int level)
                {
                }

                public void Sink<TSink>(int level, bool enabled)
                {
                }

                public bool TryParse(string text, out int result)
                {
                    result = 0;
                    return true;
                }

                public bool TryParse(string text, bool strict, out int result)
                {
                    result = 0;
                    return true;
                }

                public void Load(string path, CancellationToken cancellationToken = default)
                {
                }

                public void Load(string path, bool strict, CancellationToken cancellationToken = default)
                {
                }

                public void Hidden(string value)
                {
                }

                internal void Hidden(int count, string value)
                {
                }
            }

            internal class Internal
            {
                public void M(string value)
                {
                }

                public void M(int count, string value)
                {
                }
            }

            public class TimeoutException : System.Exception
            {
                public TimeoutException(System.TimeSpan timeout)
                {
                }

                public TimeoutException(string message, System.TimeSpan timeout)
                {
                }

                public TimeoutException(string message, System.Exception innerException)
                {
                }

                public TimeoutException(string message, System.TimeSpan timeout, System.Exception innerException)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported_but_their_overloads_are()
    {
        return Verifier.VerifyAsync(
            """
            public interface IWriter
            {
                void Write(int count, string value);

                void Flush(int count, string {|FDG5003:value|});

                void Flush(string value);
            }

            public class Base
            {
                public virtual void Send(int count, string value)
                {
                }
            }

            public class Writer : Base, IWriter
            {
                public void Write(string {|FDG5003:value|})
                {
                }

                public void Write(int count, string value)
                {
                }

                public void Send(string {|FDG5003:value|})
                {
                }

                public override void Send(int count, string value)
                {
                }

                public void Flush(int count, string value)
                {
                }

                public void Flush(string value)
                {
                }
            }
            """);
    }
}
