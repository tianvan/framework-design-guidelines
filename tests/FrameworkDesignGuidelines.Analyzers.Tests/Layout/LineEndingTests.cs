using CatchFinally = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.CodeFixVerifier<
    FrameworkDesignGuidelines.Analyzers.Layout.SingleLineCatchFinallyAnalyzer,
    FrameworkDesignGuidelines.CodeFixes.Layout.SingleLineCatchFinallyCodeFix>;
using LongList = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.CodeFixVerifier<
    FrameworkDesignGuidelines.Analyzers.Layout.LongListWrappingAnalyzer,
    FrameworkDesignGuidelines.CodeFixes.Layout.LongListWrappingCodeFix>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

/// <summary>
/// The fixes write the document's own line ending: no CRLF in an LF file and no LF in a CRLF file, including the
/// cases where the formatter has to adjust an edited gap. The verifier checks every line break of the result.
/// </summary>
public sealed class LineEndingTests
{
    public static TheoryData<string> EndOfLines => new() { "\n", "\r\n" };

    [Theory]
    [MemberData(nameof(EndOfLines))]
    public Task Tab_indented_block_is_expanded_with_tabs(string endOfLine)
    {
        return CatchFinally.VerifyAsync(
            Tabs(
                """
                class C
                {
                    void M()
                    {
                        try
                        {
                            M();
                        }
                        {|FDGA003:finally|} { M(); }
                    }
                }
                """),
            Tabs(
                """
                class C
                {
                    void M()
                    {
                        try
                        {
                            M();
                        }
                        finally
                        {
                            M();
                        }
                    }
                }
                """),
            extraEditorConfig: "indent_style = tab\n",
            endOfLine: endOfLine);
    }

    [Theory]
    [MemberData(nameof(EndOfLines))]
    public Task Tab_indented_list_is_chopped_with_tabs(string endOfLine)
    {
        return LongList.VerifyAsync(
            Tabs(
                """
                class C
                {
                    void M(string a, string b, string c)
                    {
                        Report{|FDGA023:(|}a, b, c, "a message argument");
                    }

                    static void Report(
                        string a,
                        string b,
                        string c,
                        string d)
                    {
                    }
                }
                """),
            Tabs(
                """
                class C
                {
                    void M(string a, string b, string c)
                    {
                        Report(
                            a,
                            b,
                            c,
                            "a message argument");
                    }

                    static void Report(
                        string a,
                        string b,
                        string c,
                        string d)
                    {
                    }
                }
                """),
            extraEditorConfig: "indent_style = tab\nmax_line_length = 38\n",
            endOfLine: endOfLine);
    }

    [Theory]
    [MemberData(nameof(EndOfLines))]
    public Task Directives_inside_an_argument_list_stay_in_column_zero(string endOfLine)
    {
        return LongList.VerifyAsync(
            """
            class C
            {
                void M(string first, string second, string third)
                {
                    Report{|FDGA023:(|}first, second, "a fairly long message argument that pushes the line",
            #if DEBUG
                        third,
            #else
                        second,
            #endif
                        "end");
                }

                static void Report(
                    string a,
                    string b,
                    string c,
                    string d,
                    string e)
                {
                }
            }
            """,
            """
            class C
            {
                void M(string first, string second, string third)
                {
                    Report(
                        first,
                        second,
                        "a fairly long message argument that pushes the line",
            #if DEBUG
                        third,
            #else
                        second,
            #endif
                        "end");
                }

                static void Report(
                    string a,
                    string b,
                    string c,
                    string d,
                    string e)
                {
                }
            }
            """,
            extraEditorConfig: "max_line_length = 80\n",
            endOfLine: endOfLine);
    }

    [Theory]
    [MemberData(nameof(EndOfLines))]
    public Task Member_on_the_type_declaration_line_is_expanded(string endOfLine)
    {
        // The type's own "class X {" layout is outside FDGA003, so the result is not formatter-clean as a whole.
        return CatchFinally.VerifyAsync(
            """
            class X { void {|FDGA003:M|}() { M(); }
            }
            """,
            """
            class X { void M()
                {
                    M();
                }
            }
            """,
            endOfLine: endOfLine,
            checkFormatter: false);
    }

    private static string Tabs(string text)
    {
        return text.Replace("    ", "\t");
    }
}
