using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Patterns.DisposePatternAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Patterns;

public sealed class DisposePatternTests
{
    [Fact]
    public Task Dispose_overloads_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Holder : IDisposable
            {
                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                public void {|FDG9067:Dispose|}(int timeout)
                {
                }

                internal void {|FDG9067:Dispose|}(string reason, bool disposing)
                {
                }

                private void {|FDG9067:Dispose|}<T>()
                {
                }

                public void {|FDG9067:Dispose|}(ref bool disposing)
                {
                }

                public static void {|FDG9067:Dispose|}(Holder holder)
                {
                }

                public void Close()
                {
                    void Dispose(int value)
                    {
                    }

                    Dispose(0);
                }
            }

            public static class HolderExtensions
            {
                public static void {|FDG9067:Dispose|}(this Holder holder, int timeout)
                {
                }
            }

            internal sealed class Simple
            {
                public void Dispose()
                {
                }

                private void Dispose(bool disposing)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_of_Dispose_overloads_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public interface ICloser
            {
                void {|FDG9067:Dispose|}(int timeout);
            }

            public class Closer : ICloser
            {
                public void Dispose(int timeout)
                {
                }
            }

            public class ExplicitCloser : ICloser
            {
                void ICloser.Dispose(int timeout)
                {
                }
            }

            public class Base
            {
                public virtual void {|FDG9067:Dispose|}(int timeout)
                {
                }
            }

            public class Derived : Base
            {
                public override void Dispose(int timeout)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Dispose_overload_message_names_the_signature()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Holder
            {
                public void {|#0:Dispose|}(int timeout, string reason)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9067).WithLocation(0).WithArguments("Holder.Dispose(int, string)"));
    }

    [Fact]
    public Task DisposeAsync_overloads_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;

            public class Connection
            {
                public Task DisposeAsync()
                {
                    return Task.CompletedTask;
                }

                public Task {|FDG9081:DisposeAsync|}(CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }

                protected Task {|FDG9081:DisposeAsync|}<T>()
                {
                    return Task.CompletedTask;
                }

                private static void {|FDG9081:DisposeAsync|}(Connection connection)
                {
                }
            }

            public interface IAsyncCloser
            {
                Task {|FDG9081:DisposeAsync|}(bool force);
            }

            public class AsyncCloser : IAsyncCloser
            {
                public Task DisposeAsync(bool force)
                {
                    return Task.CompletedTask;
                }
            }
            """);
    }

    [Fact]
    public Task Finalizer_in_a_public_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Visible : IDisposable
            {
                ~{|FDG9073:Visible|}()
                {
                    Dispose(false);
                }

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }
            }

            public class Outer
            {
                protected class Nested : IDisposable
                {
                    ~{|FDG9073:Nested|}()
                    {
                    }

                    public void Dispose()
                    {
                    }
                }

                private class Holder : IDisposable
                {
                    ~Holder()
                    {
                    }

                    public void Dispose()
                    {
                    }
                }
            }

            internal class Hidden : IDisposable
            {
                ~Hidden()
                {
                }

                public void Dispose()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Finalizer_without_IDisposable_is_reported()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            internal class Leaky
            {
                ~{|FDG9074:Leaky|}()
                {
                }
            }

            public class Both
            {
                ~{|#0:Both|}()
                {
                }
            }

            internal class Base : IDisposable
            {
                public void Dispose()
                {
                }
            }

            internal class Derived : Base
            {
                ~Derived()
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9073).WithLocation(0).WithArguments("Both"),
            new DiagnosticResult(FdgDescriptors.FDG9074).WithLocation(0).WithArguments("Both"));
    }

    [Fact]
    public Task Extern_and_static_Dispose_methods_of_the_pattern_shape_are_not_reported()
    {
        // A P/Invoke declaration is not part of the Dispose Pattern; static Dispose() and Dispose(bool) have the
        // allowed signatures.
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Runtime.InteropServices;

            public class Native
            {
                [DllImport("native")]
                private static extern void Dispose(IntPtr handle);

                public static void Dispose()
                {
                }

                internal static void Dispose(bool disposing)
                {
                }
            }
            """);
    }

    [Fact]
    public Task DisposeAsync_is_checked_under_CSharp_7_3()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Resource : IAsyncDisposable
            {
                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await Task.Yield();
                }
            }

            public class Pattern : IAsyncDisposable
            {
                public async ValueTask DisposeAsync()
                {
                    await DisposeAsyncCore().ConfigureAwait(false);
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }
            """,
            ReferenceAssemblies.Net.Net80,
            LanguageVersion.CSharp7_3);
    }

    [Fact]
    public Task DisposeAsync_is_not_checked_without_IAsyncDisposable()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading.Tasks;

            public class Resource
            {
                public async Task DisposeAsync()
                {
                    await Task.Yield();
                }
            }
            """,
            ReferenceAssemblies.NetFramework.Net472.Default,
            LanguageVersion.Latest);
    }

    [Fact]
    public Task Book_examples_of_finalizable_types()
    {
        // DisposableResourceHolder (section 9.4.1), FDG9073 "right" and the ComplexResourceHolder of section 9.4.2
        // are not reported; the inherited finalizer of a SafeHandle subclass is not seen. The ExplicitRehydration example of FDG9071 is a public finalizable type.
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Runtime.InteropServices;
            using Microsoft.Win32.SafeHandles;

            public class DisposableResourceHolder : IDisposable
            {
                private SafeHandle _resource;

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        _resource?.Dispose();
                    }
                }
            }

            public partial class NetworkCard : IDisposable
            {
                private SafeDeviceHandle _deviceHandle;

                public void Dispose() => Dispose(true);

                protected void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        _deviceHandle?.Dispose();
                    }
                }
            }

            internal sealed class SafeDeviceHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                internal SafeDeviceHandle()
                    : base(ownsHandle: true)
                {
                }

                protected override bool ReleaseHandle()
                {
                    return true;
                }
            }

            internal class ComplexResourceHolder : IDisposable
            {
                private IntPtr _buffer;
                private SafeHandle _resource;

                protected virtual void Dispose(bool disposing)
                {
                    if (_buffer != IntPtr.Zero)
                    {
                        _buffer = IntPtr.Zero;
                    }

                    if (disposing)
                    {
                        _resource?.Dispose();
                    }
                }

                ~ComplexResourceHolder()
                {
                    Dispose(false);
                }

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }
            }

            public partial class ExplicitRehydration : IDisposable
            {
                private IntPtr _resource;

                ~{|FDG9073:ExplicitRehydration|}()
                {
                    Dispose(false);
                }

                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                    _resource = IntPtr.Zero;
                }

                public void Open()
                {
                    GC.ReRegisterForFinalize(this);
                }
            }
            """);
    }

    [Fact]
    public Task DisposeAsync_of_the_book_example_is_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Threading.Tasks;

            public partial class SomeType : IDisposable, IAsyncDisposable
            {
                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                public async ValueTask DisposeAsync()
                {
                    await DisposeAsyncCore();
                    Dispose(false);
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore()
                {
                    return default;
                }
            }

            public class Configured : SomeType
            {
            }

            public class WithConfigureAwait : IDisposable, IAsyncDisposable
            {
                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                public async ValueTask DisposeAsync()
                {
                    await this.DisposeAsyncCore().ConfigureAwait(false);
                    this.Dispose(disposing: false);
                    GC.SuppressFinalize(this);
                }

                protected virtual async Task DisposeAsyncCore()
                {
                    await Task.Yield();
                }
            }

            public class AsyncOnly : IAsyncDisposable
            {
                public async ValueTask DisposeAsync()
                {
                    await DisposeAsyncCore().ConfigureAwait(false);
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore()
                {
                    return default;
                }
            }
            """);
    }

    [Fact]
    public Task DisposeAsync_that_departs_from_the_pattern_is_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Base : IDisposable
            {
                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }
            }

            public class Reversed : Base, IAsyncDisposable
            {
                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    Dispose(false);
                    await DisposeAsyncCore();
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class NoDisposeFalse : Base, IAsyncDisposable
            {
                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await DisposeAsyncCore();
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class DisposeTrue : Base, IAsyncDisposable
            {
                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await DisposeAsyncCore();
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class MoreWork : Base, IAsyncDisposable
            {
                private int _count;

                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await DisposeAsyncCore();
                    Dispose(false);
                    GC.SuppressFinalize(this);
                    _count++;
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class NotAwaited : IAsyncDisposable
            {
                public ValueTask {|FDG9082:DisposeAsync|}() => DisposeAsyncCore();

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class NoSuppress : IAsyncDisposable
            {
                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await DisposeAsyncCore();
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            public class OtherInstance : IAsyncDisposable
            {
                private OtherInstance _inner;

                public async ValueTask {|FDG9082:DisposeAsync|}()
                {
                    await _inner.DisposeAsyncCore();
                    GC.SuppressFinalize(this);
                }

                protected virtual ValueTask DisposeAsyncCore() => default;
            }

            internal class ExplicitImplementation : IAsyncDisposable
            {
                async ValueTask IAsyncDisposable.{|FDG9082:DisposeAsync|}()
                {
                    await Task.Yield();
                }
            }
            """);
    }

    [Fact]
    public Task DisposeAsync_exemptions_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;
            using System.Threading.Tasks;

            public sealed class Sealed : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public struct Value : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => default;
            }

            public abstract class Abstract : IAsyncDisposable
            {
                public abstract ValueTask DisposeAsync();
            }

            public class Concrete : Abstract
            {
                public override ValueTask DisposeAsync() => default;
            }

            public class Unrelated
            {
                public ValueTask DisposeAsync() => default;
            }

            public class Inherits : Unrelated, IAsyncDisposable
            {
            }
            """);
    }

    [Fact]
    public Task DisposeAsync_message_names_the_expected_steps()
    {
        return Verifier.VerifyLatestWithAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class Resource : IAsyncDisposable
            {
                public ValueTask {|#0:DisposeAsync|}() => default;
            }

            public class Finalizable : IDisposable, IAsyncDisposable
            {
                public void Dispose()
                {
                    Dispose(true);
                    GC.SuppressFinalize(this);
                }

                protected virtual void Dispose(bool disposing)
                {
                }

                public ValueTask {|#1:DisposeAsync|}() => default;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG9082).WithLocation(0).WithArguments(
                "Resource.DisposeAsync()",
                "awaiting DisposeAsyncCore() and calling GC.SuppressFinalize(this)"),
            new DiagnosticResult(FdgDescriptors.FDG9082).WithLocation(1).WithArguments(
                "Finalizable.DisposeAsync()",
                "awaiting DisposeAsyncCore(), calling Dispose(false) and calling GC.SuppressFinalize(this)"));
    }
}
