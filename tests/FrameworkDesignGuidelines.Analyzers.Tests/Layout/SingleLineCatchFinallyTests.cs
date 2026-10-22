using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.CodeFixVerifier<
    FrameworkDesignGuidelines.Analyzers.Layout.SingleLineCatchFinallyAnalyzer,
    FrameworkDesignGuidelines.CodeFixes.Layout.SingleLineCatchFinallyCodeFix>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

public sealed class SingleLineCatchFinallyTests
{
    [Fact]
    public Task Single_line_catch_is_expanded()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.IO;

            class C
            {
                void M()
                {
                    try
                    {
                        Run();
                    }
                    {|FDGA003:catch|} (IOException ex) { Log(ex); }
                }

                void Run()
                {
                }

                void Log(Exception ex)
                {
                }
            }
            """,
            """
            using System;
            using System.IO;

            class C
            {
                void M()
                {
                    try
                    {
                        Run();
                    }
                    catch (IOException ex)
                    {
                        Log(ex);
                    }
                }

                void Run()
                {
                }

                void Log(Exception ex)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Empty_single_line_finally_is_expanded()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    {|FDGA003:finally|} { }
                }
            }
            """,
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
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Nested_single_line_blocks_are_expanded()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void {|FDGA003:M|}(bool condition) { {|FDGA003:if|} (condition) { M(false); } }
            }
            """,
            """
            class C
            {
                void M(bool condition)
                {
                    if (condition)
                    {
                        M(false);
                    }
                }
            }
            """,
            // Fixes of nested blocks touch the same gap, so a batch FixAll needs a second pass for the inner one.
            fixAllIterations: 2);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public Task Nested_try_in_single_line_catch_keeps_its_brace_on_the_next_line(string endOfLine)
    {
        // The statement's first token (try) is reformatted; the gap after it must not become an empty line.
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M()
                {
                    try
                    {
                        Log(null);
                    }
                    {|FDGA003:catch|} (System.Exception ex) { {|FDGA003:try|} { Log(ex); } {|FDGA003:catch|} { } }
                }

                static void Log(System.Exception ex)
                {
                }
            }
            """,
            """
            class C
            {
                void M()
                {
                    try
                    {
                        Log(null);
                    }
                    catch (System.Exception ex)
                    {
                        try
                        {
                            Log(ex);
                        }
                        catch
                        {
                        }
                    }
                }

                static void Log(System.Exception ex)
                {
                }
            }
            """,
            extraEditorConfig: "csharp_preserve_single_line_statements = false\n",
            endOfLine: endOfLine,
            // One incremental fix per block; a batch FixAll needs up to two passes. Negative means "at most".
            fixAllIterations: -3);
    }

    [Fact]
    public Task Trailing_comment_stays_after_the_closing_brace()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    {|FDGA003:finally|} { M(); } // cleanup
                }
            }
            """,
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
                    } // cleanup
                }
            }
            """);
    }

    [Fact]
    public Task Crlf_line_endings_are_kept()
    {
        return Verifier.VerifyAsync(
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
            """,
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
            """,
            endOfLine: "\r\n");
    }

    [Fact]
    public async Task Formatter_check_rejects_misformatted_text()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Verifier.AssertFormatterLeavesUnchangedAsync(
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
            """));
    }

    [Fact]
    public Task Multiline_catch_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            using System;

            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Several_statements_are_placed_one_per_line()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    {|FDGA003:catch|} (Exception ex) { Console.WriteLine(ex);   throw; }
                }
            }
            """,
            """
            using System;

            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        throw;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Single_line_method_body_is_expanded()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                [Obsolete]
                public void {|FDGA003:Run|}() { Console.WriteLine(); }

                public {|FDGA003:C|}() { }
            }
            """,
            """
            using System;

            class C
            {
                [Obsolete]
                public void Run()
                {
                    Console.WriteLine();
                }

                public C()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Single_line_if_body_is_expanded()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(bool condition)
                {
                    {|FDGA003:if|} (condition) { M(false); } {|FDGA003:else|} { M(true); }
                    M(condition);
                }
            }
            """,
            """
            class C
            {
                void M(bool condition)
                {
                    if (condition)
                    {
                        M(false);
                    }
                    else
                    {
                        M(true);
                    }
                    M(condition);
                }
            }
            """);
    }

    [Fact]
    public Task Single_line_switch_section_block_is_expanded()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        {|FDGA003:case|} 1: { M(2); break; }
                        default:
                            break;
                    }
                }
            }
            """,
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 1:
                            {
                                M(2);
                                break;
                            }
                        default:
                            break;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Accessor_list_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            class C
            {
                public int P { get; private set; }
            }
            """);
    }

    [Fact]
    public Task Accessor_body_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            class C
            {
                private int _value;

                public int P
                {
                    get { return _value; }
                    set { _value = value; }
                }
            }
            """);
    }

    [Fact]
    public Task Lambda_and_anonymous_method_are_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            using System;

            class C
            {
                void M()
                {
                    Action first = () => { M(); };
                    Action second = delegate { M(); };
                    first();
                    second();
                }
            }
            """);
    }

    [Fact]
    public Task Initializers_type_bodies_and_do_while_are_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            using System.Collections.Generic;

            class Empty { }

            class C
            {
                void M()
                {
                    var list = new List<int> { 1, 2 };
                    var point = new { X = 1, Y = 2 };
                    var copy = new List<int>(list) { Capacity = 4 };
                    do
                    {
                        M();
                    } while (list.Count > point.X + copy.Count);
                    do { M(); } while (false);
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
                void M()
                {
                    try
                    {
                        M();
                    }
                    {|FDGA003:catch|} (ArgumentException) { }
                    {|FDGA003:catch|} (Exception ex) { Console.WriteLine(ex); }
                    {|FDGA003:finally|} { M(); }
                }
            }
            """,
            """
            using System;

            class C
            {
                void M()
                {
                    try
                    {
                        M();
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                    finally
                    {
                        M();
                    }
                }
            }
            """);
    }
}
