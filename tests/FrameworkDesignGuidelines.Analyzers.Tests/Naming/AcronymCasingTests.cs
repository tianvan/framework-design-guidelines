using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.AcronymCasingAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class AcronymCasingTests
{
    [Fact]
    public Task Book_examples_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;

            namespace System.Xml.UI
            {
                public delegate void IOCompletionCallback(int errorCode);

                public class XmlNode
                {
                    public void StartIO(Stream ioStream, bool closeIOStream)
                    {
                    }

                    public void ProcessHtmlTag(string htmlTag)
                    {
                    }

                    public static int FromArgb(int argb, int rgbValue) => argb;

                    public string Ratio { get; set; }

                    public string Xmlns { get; set; }
                }
            }
            """);
    }

    [Fact]
    public Task Two_character_acronyms_not_in_capitals_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Sample.{|FDG3003:Io|}
            {
                public class {|FDG3003:UiElement|}
                {
                    public void {|FDG3003:StartIo|}(bool {|FDG3003:closeIoStream|})
                    {
                    }

                    public event System.EventHandler {|FDG3003:IoCompleted|};
                }
            }
            """);
    }

    [Fact]
    public Task Longer_acronyms_in_capitals_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class {|FDG3004:XMLNode|}
            {
                public string {|FDG3004:HTMLTag|};

                public void {|FDG3004:ProcessHTML|}(string {|FDG3004:rawHTML|})
                {
                }

                public static int {|FDG3004:FromARGB|}(int {|FDG3004:valueRGB|}) => 0;

                public void Convert<{|FDG3004:TXML|}>()
                {
                }
            }
            """);
    }

    [Fact]
    public Task First_word_of_a_camel_cased_parameter_is_left_to_FDG3005()
    {
        return Verifier.VerifyAsync(
            """
            public class C
            {
                public void M(string htmlTag, string HTMLTag, string IOStream)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Names_that_are_not_externally_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal class XMLNode
            {
                public void StartIo()
                {
                }
            }

            public class C
            {
                private string HTMLTag;

                internal void StartIo(int closeIoStream)
                {
                    int localHTML = closeIoStream;
                }

                private class UiNested
                {
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public interface {|FDG3004:IHTMLWriter|}
            {
                void {|FDG3003:WriteIo|}(int {|FDG3004:rawXML|});
            }

            public abstract class {|FDG3004:HTMLBase|}
            {
                public abstract void {|FDG3003:StartIo|}();
            }

            public class Writer : HTMLBase, IHTMLWriter
            {
                public override void StartIo()
                {
                }

                public void WriteIo(int rawXML)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Protected_members_of_sealed_types_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public sealed class Sealed
            {
                protected internal void StartIo()
                {
                }
            }

            public struct Value
            {
                public void {|FDG3003:StartIo|}()
                {
                }
            }

            public class Open
            {
                protected void {|FDG3003:StopIo|}()
                {
                }

                public sealed class Nested
                {
                    protected internal int HTMLLength;
                }
            }
            """);
    }

    [Fact]
    public Task Implementations_of_static_abstract_interface_members_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public interface {|FDG3004:IHTMLParser|}<TSelf>
                where TSelf : IHTMLParser<TSelf>
            {
                static abstract TSelf {|FDG3004:ParseHTML|}(string {|FDG3004:rawHTML|});

                static virtual void {|FDG3003:StartIo|}()
                {
                }
            }

            public sealed class Parser : IHTMLParser<Parser>
            {
                public static Parser ParseHTML(string rawHTML) => new Parser();

                public static void StartIo()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Record_and_primary_constructor_parameters_are_reported_once()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record Page(string {|FDG3004:RawHTML|});

            public class Reader(int {|FDG3003:maxIo|})
            {
                public int Limit => maxIo;
            }
            """);
    }

    [Fact]
    public Task Record_positional_parameters_are_pascal_cased_property_names()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record Page(string {|FDG3004:HTMLBody|}, string {|FDG3003:IoName|}, string Title);

            public record struct Frame(int {|FDG3003:UiIndex|});

            public class Reader(int ioLimit, string HTMLSource)
            {
                public int Limit => ioLimit + HTMLSource.Length;
            }
            """);
    }
}
