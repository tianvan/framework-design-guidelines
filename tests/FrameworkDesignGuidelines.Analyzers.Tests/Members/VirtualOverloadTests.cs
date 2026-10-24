using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.VirtualOverloadAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class VirtualOverloadTests
{
    [Fact]
    public Task Virtual_shorter_overload_is_reported_whatever_the_accessibility_of_either()
    {
        return Verifier.VerifyAsync(
            """
            public enum NormalizationForm
            {
                FormC,
                FormD,
            }

            public class Encoding
            {
                public virtual bool {|FDG5004:IsAlwaysNormalized|}()
                {
                    return IsAlwaysNormalized(NormalizationForm.FormC);
                }

                public virtual bool IsAlwaysNormalized(NormalizationForm form)
                {
                    return true;
                }
            }

            public abstract class Reader
            {
                public abstract int {|FDG5004:Read|}(byte[] buffer);

                public abstract int {|FDG5004:Read|}(byte[] buffer, int offset);

                public abstract int Read(byte[] buffer, int offset, int count);
            }

            public class TextWriter
            {
                public virtual void {|FDG5004:WriteEnd|}()
                {
                    WriteEnd(0);
                }

                protected virtual void WriteEnd(int token)
                {
                }

                protected internal virtual void {|FDG5004:Flush|}()
                {
                }

                protected virtual void Flush(bool all)
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
            public class Printer
            {
                public virtual void {|#0:Print|}(string text)
                {
                }

                public virtual void Print(string text, int copies)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5004).WithLocation(0).WithArguments("Print"));
    }

    [Fact]
    public Task Longest_virtual_overload_and_unrelated_parameter_lists_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public enum NormalizationForm
            {
                FormC,
            }

            public class Encoding
            {
                public bool IsAlwaysNormalized()
                {
                    return IsAlwaysNormalized(NormalizationForm.FormC);
                }

                public virtual bool IsAlwaysNormalized(NormalizationForm form)
                {
                    return true;
                }
            }

            public class TextWriter
            {
                public virtual void Write(int value)
                {
                }

                public virtual void Write(string value)
                {
                }

                public virtual void Write(string format, object arg0)
                {
                }
            }

            public class Derived : TextWriter
            {
                public override void Write(string value)
                {
                }

                public void Write(string value, int count)
                {
                }
            }

            public sealed class Sealed
            {
                public void M()
                {
                }

                public void M(int value)
                {
                }
            }

            internal class Internal
            {
                public virtual void M()
                {
                }

                public virtual void M(int value)
                {
                }
            }
            """);
    }
}
