using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.DefaultParameterAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class DefaultParameterTests
{
    [Fact]
    public Task Method_with_two_defaults_and_no_simple_overload_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Parser
            {
                public static int {|FDG5010:Parse|}(string value, int style = 0, object provider = null) => 0;

                public {|FDG5010:Parser|}(int capacity = 4, bool strict = false)
                {
                }

                public static int {|FDG5010:Format|}(string value, int width = 0, char fill = ' ') => 0;

                public static int Format(string value, int width, char fill, bool upper) => 0;
            }
            """);
    }

    [Fact]
    public Task Method_with_a_simple_overload_or_one_default_is_not_reported_by_FDG5010()
    {
        return Verifier.VerifyAsync(
            """
            public class Parser
            {
                public static int Parse(string value) => Parse(value, provider: null);

                public static int Parse(string value, int style = 0, object provider = null) => 0;

                public static int Trim(string value, char trimChar = ' ') => 0;

                internal static int Hidden(string value, int a = 0, int b = 0) => 0;
            }
            """);
    }

    [Fact]
    public Task Default_parameter_on_an_interface_or_virtual_member_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Threading;

            public interface IExample
            {
                void PrintValue(int {|FDG5011:value|} = 5);

                void Run(CancellationToken cancellationToken = default(CancellationToken));
            }

            public class Example : IExample
            {
                public virtual void PrintValue(int {|FDG5011:value|} = 10)
                {
                }

                public void Run(CancellationToken cancellationToken = default(CancellationToken))
                {
                }
            }

            public class DerivedExample : Example
            {
                public override void PrintValue(int {|FDG5011:value|} = 20)
                {
                }
            }

            public abstract class Shape
            {
                public abstract void Draw(bool {|FDG5011:fill|} = false);
            }
            """);
    }

    [Fact]
    public Task Message_names_the_parameter_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public interface IExample
            {
                void PrintValue(int {|#0:value|} = 5);
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5011).WithLocation(0).WithArguments("value", "PrintValue"));
    }

    [Fact]
    public Task Template_method_pattern_and_extension_methods_are_not_reported_by_FDG5011()
    {
        return Verifier.VerifyAsync(
            """
            public interface IExample
            {
                void PrintValue(int value);
            }

            public static class ExampleExtensions
            {
                public static void PrintValue(this IExample example, int value = 10)
                {
                    example.PrintValue(value);
                }
            }

            public enum BoundsSpecified
            {
                All,
            }

            public partial class Control
            {
                public void SetBounds(int x, int y, BoundsSpecified specified = BoundsSpecified.All)
                {
                    SetBoundsCore(x, y, specified);
                }

                protected virtual void SetBoundsCore(int x, int y, BoundsSpecified specified)
                {
                }
            }

            internal interface IHidden
            {
                void M(int value = 1);
            }
            """);
    }

    [Fact]
    public Task Overloads_with_compatible_required_parameters_and_defaults_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Base64
            {
                public static int DecodeFromUtf8(byte[] utf8, byte[] bytes, out int bytesConsumed)
                {
                    bytesConsumed = 0;
                    return 0;
                }

                public static int DecodeFromUtf8(
                    byte[] utf8, byte[] bytes, out int bytesConsumed, bool isFinalBlock = true)
                {
                    bytesConsumed = 0;
                    return 0;
                }

                public static int {|FDG5013:DecodeFromUtf8|}(
                    byte[] utf8,
                    byte[] bytes,
                    out int bytesConsumed,
                    bool isFinalBlock = true,
                    int bufferSize = 512)
                {
                    bytesConsumed = 0;
                    return 0;
                }

                public static int Encode(string text, int offset = 0)
                {
                    return 0;
                }

                public static int {|FDG5013:Encode|}(string text, int offset, bool strict = false)
                {
                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Overloads_with_incompatible_required_parameters_are_not_reported_by_FDG5013()
    {
        return Verifier.VerifyAsync(
            """
            public class Base64
            {
                public static int DecodeFromUtf8(
                    byte[] utf8, byte[] bytes, out int bytesConsumed, bool isFinalBlock = true)
                {
                    bytesConsumed = 0;
                    return 0;
                }

                public static byte[] DecodeFromUtf8(byte[] utf8, out int bytesConsumed, bool isFinalBlock = true)
                {
                    bytesConsumed = 0;
                    return null;
                }

                public static int Encode(string text, int offset, int count, bool strict = false)
                {
                    return 0;
                }

                public static int Encode(string text, bool upper = false)
                {
                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Different_defaults_for_the_same_parameter_are_reported()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Base64
            {
                public static int DecodeFromUtf8(
                    byte[] utf8, byte[] bytes, out int bytesConsumed, bool isFinalBlock = true)
                {
                    bytesConsumed = 0;
                    return 0;
                }

                public static byte[] DecodeFromUtf8(
                    byte[] utf8, out int bytesConsumed, bool {|#0:isFinalBlock|} = false)
                {
                    bytesConsumed = 0;
                    return null;
                }

                public static void Write(string text, string separator = ",")
                {
                }

                public static void Write(char[] text, string separator = ",")
                {
                }

                public static void Write(int[] text, int separator = 0)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5015)
                .WithLocation(0)
                .WithArguments("isFinalBlock", "DecodeFromUtf8"));
    }

    [Fact]
    public Task Static_abstract_interface_members_are_reported_but_their_implementations_are_not()
    {
        // C# 11 adds static abstract interface members.
        return Verifier.VerifyLatestAsync(
            """
            public interface IParser<T>
                where T : IParser<T>
            {
                static abstract T Parse(string text, int {|FDG5011:start|} = 0);
            }

            public class Number : IParser<Number>
            {
                public static Number Parse(string text, int start = 0) => null;
            }
            """);
    }

    [Fact]
    public Task Overrides_of_members_from_referenced_assemblies_are_not_reported_by_FDG5011()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;

            public class Writer : TextWriter
            {
                public override System.Text.Encoding Encoding => null;

                public override void Write(char[] buffer, int index, int count)
                {
                }
            }

            public class Base
            {
                public virtual void Print(int {|FDG5011:value|} = 1)
                {
                }
            }

            public class Derived : Base
            {
                public override void Print(int {|FDG5011:value|} = 1)
                {
                }
            }

            public abstract class Converter : System.ComponentModel.TypeConverter
            {
                public override object ConvertTo(
                    System.ComponentModel.ITypeDescriptorContext context,
                    System.Globalization.CultureInfo culture,
                    object value,
                    System.Type destinationType = null) => null;
            }
            """);
    }

    [Fact]
    public Task Generated_code_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            // <auto-generated/>
            public interface IExample
            {
                void PrintValue(int value = 5);
            }
            """);
    }
}
