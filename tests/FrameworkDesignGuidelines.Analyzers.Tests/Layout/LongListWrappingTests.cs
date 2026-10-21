using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.CodeFixVerifier<
    FrameworkDesignGuidelines.Analyzers.Layout.LongListWrappingAnalyzer,
    FrameworkDesignGuidelines.CodeFixes.Layout.LongListWrappingCodeFix>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

public sealed class LongListWrappingTests
{
    private const string Values =
        """
            private static readonly string firstValue = "1", secondValue = "2", thirdValue = "3", fourthValue = "4";
        """;

    [Fact]
    public async Task Long_invocation_is_chopped_before_first_argument()
    {
        const string Call =
            """        Failed(firstValue, secondValue, null, thirdValue, fourthValue, fifthValue, sixthValue, seventhValue, "a ninth argument.");""";
        Assert.Equal(130, Call.Length);

        await Verifier.VerifyAsync(
            """
            class C
            {
                void M(string firstValue, string secondValue, string thirdValue, string fourthValue)
                {
                    string fifthValue = "5", sixthValue = "6", seventhValue = "7";
                    Failed{|FDGA023:(|}firstValue, secondValue, null, thirdValue, fourthValue, fifthValue, sixthValue, seventhValue, "a ninth argument.");
                }

                static void Failed(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }
            }
            """,
            """
            class C
            {
                void M(string firstValue, string secondValue, string thirdValue, string fourthValue)
                {
                    string fifthValue = "5", sixthValue = "6", seventhValue = "7";
                    Failed(
                        firstValue,
                        secondValue,
                        null,
                        thirdValue,
                        fourthValue,
                        fifthValue,
                        sixthValue,
                        seventhValue,
                        "a ninth argument.");
                }

                static void Failed(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Long_parameter_list_is_chopped()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                public static void Register{|FDGA023:(|}string name, int firstOrdinal, int secondOrdinal, bool isRequired, TimeSpan timeout, object state)
                {
                }
            }
            """,
            """
            using System;

            class C
            {
                public static void Register(
                    string name,
                    int firstOrdinal,
                    int secondOrdinal,
                    bool isRequired,
                    TimeSpan timeout,
                    object state)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Long_indexer_parameter_list_is_chopped()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                public int this{|FDGA023:[|}int firstDimensionIndex, int secondDimensionIndex, int thirdDimensionIndex, int fourthDimensionIndex, int fifth]
                {
                    get { return firstDimensionIndex + secondDimensionIndex + thirdDimensionIndex + fourthDimensionIndex + fifth; }
                }
            }
            """,
            """
            class C
            {
                public int this[
                    int firstDimensionIndex,
                    int secondDimensionIndex,
                    int thirdDimensionIndex,
                    int fourthDimensionIndex,
                    int fifth]
                {
                    get { return firstDimensionIndex + secondDimensionIndex + thirdDimensionIndex + fourthDimensionIndex + fifth; }
                }
            }
            """);
    }

    [Fact]
    public Task Comments_between_items_are_kept()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M()
                {
                    Report{|FDGA023:(|}"the first message argument", "the second message argument", // the third follows
                        "the third message argument" /* last */);
                }

                static void Report(string a, string b, string c)
                {
                }
            }
            """,
            """
            class C
            {
                void M()
                {
                    Report(
                        "the first message argument",
                        "the second message argument", // the third follows
                        "the third message argument" /* last */);
                }

                static void Report(string a, string b, string c)
                {
                }
            }
            """,
            extraEditorConfig: "max_line_length = 80\n");
    }

    [Fact]
    public Task Crlf_line_endings_are_kept()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                public static void Register{|FDGA023:(|}string name, int firstOrdinal, int secondOrdinal, bool isRequired, TimeSpan timeout, object state)
                {
                }
            }
            """,
            """
            using System;

            class C
            {
                public static void Register(
                    string name,
                    int firstOrdinal,
                    int secondOrdinal,
                    bool isRequired,
                    TimeSpan timeout,
                    object state)
                {
                }
            }
            """,
            endOfLine: "\r\n");
    }

    [Fact]
    public Task Object_creation_and_constructor_initializer_are_chopped()
    {
        return Verifier.VerifyAsync(
            """
            class Widget
            {
                public Widget(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }
            }

            class C : Widget
            {
            """ + "\n" + Values + "\n" + """

                public C()
                    : base{|FDGA023:(|}firstValue, secondValue, thirdValue, fourthValue, firstValue, secondValue, thirdValue, fourthValue, "null")
                {
                    var widget = new Widget{|FDGA023:(|}firstValue, secondValue, thirdValue, fourthValue, firstValue, secondValue, null, null, null);
                }
            }
            """,
            """
            class Widget
            {
                public Widget(string a, string b, string c, string d, string e, string f, string g, string h, string i)
                {
                }
            }

            class C : Widget
            {
            """ + "\n" + Values + "\n" + """

                public C()
                    : base(
                        firstValue,
                        secondValue,
                        thirdValue,
                        fourthValue,
                        firstValue,
                        secondValue,
                        thirdValue,
                        fourthValue,
                        "null")
                {
                    var widget = new Widget(
                        firstValue,
                        secondValue,
                        thirdValue,
                        fourthValue,
                        firstValue,
                        secondValue,
                        null,
                        null,
                        null);
                }
            }
            """);
    }

    [Fact]
    public Task Single_argument_is_not_reported()
    {
        const string Source =
            """
            class C
            {
                void M()
                {
                    Report("a single argument that is much longer than the configured maximum line length of the file, really, truly");
                }

                static void Report(string message)
                {
                }
            }
            """;
        AssertHasLineLongerThan(120, Source);
        return Verifier.VerifyNoDiagnosticsAsync(Source);
    }

    [Fact]
    public Task Line_within_limit_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Report(firstValue, secondValue, thirdValue, fourthValue, "a message that is longer");
                }

                static void Report(string a, string b, string c, string d, string message)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Max_line_length_from_editorconfig_is_respected()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Report{|FDGA023:(|}firstValue, secondValue, thirdValue, fourthValue, "a message that is longer");
                    Report(firstValue, secondValue, thirdValue, fourthValue, "a message");
                }

                static void Report(
                    string a,
                    string b,
                    string c,
                    string d,
                    string message)
                {
                }
            }
            """,
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Report(
                        firstValue,
                        secondValue,
                        thirdValue,
                        fourthValue,
                        "a message that is longer");
                    Report(firstValue, secondValue, thirdValue, fourthValue, "a message");
                }

                static void Report(
                    string a,
                    string b,
                    string c,
                    string d,
                    string message)
                {
                }
            }
            """,
            extraEditorConfig: "max_line_length = 80\n");
    }

    [Fact]
    public Task Already_chopped_list_is_not_reported()
    {
        const string Source =
            """
            class C
            {
                void M()
                {
                    var widgetRegistrationResultForTheCurrentlyConfiguredEnvironmentValue = RegisterWidgetWithTheConfiguredEnvironment(
                        1,
                        2);
                }

                static int RegisterWidgetWithTheConfiguredEnvironment(int first, int second)
                {
                    return first + second;
                }
            }
            """;
        AssertHasLineLongerThan(120, Source);
        return Verifier.VerifyNoDiagnosticsAsync(Source);
    }

    [Fact]
    public Task Only_outermost_list_on_line_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Outer{|FDGA023:(|}Inner(firstValue, secondValue, thirdValue), firstValue, secondValue, thirdValue, fourthValue, "an outer argument");
                }

                static string Inner(string a, string b, string c)
                {
                    return a + b + c;
                }

                static void Outer(string a, string b, string c, string d, string e, string f)
                {
                }
            }
            """,
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Outer(
                        Inner(firstValue, secondValue, thirdValue),
                        firstValue,
                        secondValue,
                        thirdValue,
                        fourthValue,
                        "an outer argument");
                }

                static string Inner(string a, string b, string c)
                {
                    return a + b + c;
                }

                static void Outer(string a, string b, string c, string d, string e, string f)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Inner_list_is_indented_from_its_own_line()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Outer(
                        Inner{|FDGA023:(|}firstValue, secondValue, thirdValue, fourthValue, firstValue, secondValue, thirdValue, "an inner argument"),
                        firstValue);
                }

                static string Inner(string a, string b, string c, string d, string e, string f, string g, string h)
                {
                    return a + h;
                }

                static void Outer(string a, string b)
                {
                }
            }
            """,
            """
            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Outer(
                        Inner(
                            firstValue,
                            secondValue,
                            thirdValue,
                            fourthValue,
                            firstValue,
                            secondValue,
                            thirdValue,
                            "an inner argument"),
                        firstValue);
                }

                static string Inner(string a, string b, string c, string d, string e, string f, string g, string h)
                {
                    return a + h;
                }

                static void Outer(string a, string b)
                {
                }
            }
            """);
    }

    [Fact]
    public Task FixAll_in_document_fixes_all_occurrences()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Register{|FDGA023:(|}firstValue, 1, 2, true, TimeSpan.Zero, new object(), "and a fairly long trailing string argument here");
                    Register{|FDGA023:(|}secondValue, 3, 4, false, TimeSpan.Zero, null, "and yet another fairly long trailing string argument"   );
                }

                public static void Register{|FDGA023:(|}string name, int firstOrdinal, int secondOrdinal, bool isRequired, TimeSpan timeout, object state, string note)
                {
                }
            }
            """,
            """
            using System;

            class C
            {
            """ + "\n" + Values + "\n" + """

                void M()
                {
                    Register(
                        firstValue,
                        1,
                        2,
                        true,
                        TimeSpan.Zero,
                        new object(),
                        "and a fairly long trailing string argument here");
                    Register(
                        secondValue,
                        3,
                        4,
                        false,
                        TimeSpan.Zero,
                        null,
                        "and yet another fairly long trailing string argument");
                }

                public static void Register(
                    string name,
                    int firstOrdinal,
                    int secondOrdinal,
                    bool isRequired,
                    TimeSpan timeout,
                    object state,
                    string note)
                {
                }
            }
            """);
    }

    private static void AssertHasLineLongerThan(int length, string source)
    {
        Assert.Contains(source.Split('\n'), line => line.TrimEnd('\r').Length > length);
    }
}
