using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.CodeFixVerifier<
    FrameworkDesignGuidelines.Analyzers.Layout.OmittedBracesAnalyzer,
    FrameworkDesignGuidelines.CodeFixes.Layout.OmittedBracesCodeFix>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Layout;

public sealed class OmittedBracesTests
{
    [Fact]
    public Task Braceless_if_gets_braces()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                int M(int value)
                {
                    int doubled = value * 2;
                    {|FDGA004:if|} (doubled < 0)
                        return 0;
                    return doubled;
                }
            }
            """,
            """
            class C
            {
                int M(int value)
                {
                    int doubled = value * 2;
                    if (doubled < 0)
                    {
                        return 0;
                    }
                    return doubled;
                }
            }
            """);
    }

    [Fact]
    public Task Statement_on_the_keyword_line_moves_into_the_block()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    {|FDGA004:if|} (condition) M(false); // the only call
                    M(true);
                }
            }
            """,
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    if (condition)
                    {
                        M(false); // the only call
                    }
                    M(true);
                }
            }
            """);
    }

    [Fact]
    public Task Comment_after_the_keyword_line_stays_there()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    {|FDGA004:while|} (condition) // until it changes
                        M(false);
                }
            }
            """,
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    while (condition) // until it changes
                    {
                        M(false);
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Else_if_chain_reports_each_embedded_statement_but_not_the_else_if()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(int value)
                {
                    M(value);
                    {|FDGA004:if|} (value == 1)
                        M(1);
                    else {|FDGA004:if|} (value == 2)
                        M(2);
                    {|FDGA004:else|}
                        M(3);
                }
            }
            """,
            """
            class C
            {
                void M(int value)
                {
                    M(value);
                    if (value == 1)
                    {
                        M(1);
                    }
                    else if (value == 2)
                    {
                        M(2);
                    }
                    else
                    {
                        M(3);
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Loops_using_and_lock_get_braces()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;

            class C
            {
                void M(int[] values, Stream stream)
                {
                    {|FDGA004:for|} (int i = 0; i < values.Length; i++)
                        values[i]++;
                    {|FDGA004:foreach|} (int value in values)
                        M(values, stream);
                    {|FDGA004:do|}
                        M(values, stream);
                    while (values.Length > 0);
                    {|FDGA004:using|} (stream)
                        stream.Flush();
                    {|FDGA004:lock|} (values)
                        values[0] = 0;
                }
            }
            """,
            """
            using System.IO;

            class C
            {
                void M(int[] values, Stream stream)
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i]++;
                    }
                    foreach (int value in values)
                    {
                        M(values, stream);
                    }
                    do
                    {
                        M(values, stream);
                    }
                    while (values.Length > 0);
                    using (stream)
                    {
                        stream.Flush();
                    }
                    lock (values)
                    {
                        values[0] = 0;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Single_line_do_keeps_while_after_the_closing_brace()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(int count)
                {
                    {|FDGA004:do|} count--; while (count > 0);
                }
            }
            """,
            """
            class C
            {
                void M(int count)
                {
                    do
                    {
                        count--;
                    } while (count > 0);
                }
            }
            """);
    }

    [Fact]
    public Task Stacked_using_statements_report_only_the_innermost()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;

            class C
            {
                void M(Stream first, Stream second)
                {
                    using (first)
                    {|FDGA004:using|} (second)
                        first.CopyTo(second);
                }
            }
            """,
            """
            using System.IO;

            class C
            {
                void M(Stream first, Stream second)
                {
                    using (first)
                    using (second)
                    {
                        first.CopyTo(second);
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Nested_braceless_statements_are_all_fixed()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                void M(int[] values)
                {
                    {|FDGA004:foreach|} (int value in values)
                        {|FDGA004:if|} (value > 0)
                            M(values);
                }
            }
            """,
            """
            class C
            {
                void M(int[] values)
                {
                    foreach (int value in values)
                    {
                        if (value > 0)
                        {
                            M(values);
                        }
                    }
                }
            }
            """,
            fixAllIterations: 2);
    }

    [Fact]
    public Task Argument_validation_preamble_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            using System;
            using System.Collections.Generic;

            class C
            {
                private int _count;

                public C(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));
                    if (name.Length == 0)
                    {
                        throw new ArgumentException("Empty.", nameof(name));
                    }
                    if (name.Length > 10)
                        throw new ArgumentOutOfRangeException(nameof(name));

                    _count = name.Length;
                }

                public int Count
                {
                    get
                    {
                        return _count;
                    }

                    set
                    {
                        if (value < 0)
                            throw new ArgumentOutOfRangeException(nameof(value));

                        _count = value;
                    }
                }

                public static C operator +(C left, C right)
                {
                    if (left is null)
                        throw new ArgumentNullException(nameof(left));

                    return right;
                }

                public void Run(IList<int> values)
                {
                    if (values is null)
                        throw new ArgumentNullException(nameof(values));

                    void Local(int value)
                    {
                        if (value < 0)
                            throw new ArgumentOutOfRangeException(nameof(value));

                        values.Add(value);
                    }

                    Action<int> add = value =>
                    {
                        if (value < 0)
                            throw new ArgumentOutOfRangeException(nameof(value));

                        Local(value);
                    };
                    add(_count);
                }
            }
            """);
    }

    [Fact]
    public Task Throw_after_other_code_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                void M(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));
                    int length = name.Length;
                    {|FDGA004:if|} (length > 10)
                        throw new ArgumentOutOfRangeException(nameof(name));
                }
            }
            """,
            """
            using System;

            class C
            {
                void M(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));
                    int length = name.Length;
                    if (length > 10)
                    {
                        throw new ArgumentOutOfRangeException(nameof(name));
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Throw_after_a_blank_line_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                void M(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));

                    {|FDGA004:if|} (name.Length > 10)
                        throw new ArgumentOutOfRangeException(nameof(name));
                }
            }
            """,
            """
            using System;

            class C
            {
                void M(string name)
                {
                    if (name is null)
                        throw new ArgumentNullException(nameof(name));

                    if (name.Length > 10)
                    {
                        throw new ArgumentOutOfRangeException(nameof(name));
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Throw_with_else_or_in_a_nested_block_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            class C
            {
                int M(string name, bool strict)
                {
                    {|FDGA004:if|} (name is null)
                        throw new ArgumentNullException(nameof(name));
                    {|FDGA004:else|}
                        M(name, strict);
                    if (strict)
                    {
                        {|FDGA004:if|} (name.Length == 0)
                            throw new ArgumentException("Empty.", nameof(name));
                    }

                    return 0;
                }
            }
            """,
            """
            using System;

            class C
            {
                int M(string name, bool strict)
                {
                    if (name is null)
                    {
                        throw new ArgumentNullException(nameof(name));
                    }
                    else
                    {
                        M(name, strict);
                    }
                    if (strict)
                    {
                        if (name.Length == 0)
                        {
                            throw new ArgumentException("Empty.", nameof(name));
                        }
                    }

                    return 0;
                }
            }
            """);
    }

    [Fact]
    public Task Blocks_and_switch_sections_are_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            class C
            {
                void M(int value)
                {
                    if (value > 0)
                    {
                        M(value - 1);
                    }
                    else if (value < 0)
                    {
                        M(value + 1);
                    }

                    switch (value)
                    {
                        case 0:
                            M(1);
                            break;
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Embedded_statement_with_preprocessor_directives_is_not_reported()
    {
        return Verifier.VerifyNoDiagnosticsAsync(
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    if (condition)
            #if DEBUG
                        M(false);
            #else
                        M(true);
            #endif
                    while (condition)
                        // one call
            #region Call
                        M(false);
            #endregion
                }
            }
            """);
    }

    [Fact]
    public Task Fixed_and_deconstruction_foreach_get_braces()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                unsafe void M(int[] values, (int Key, int Value)[] pairs)
                {
                    {|FDGA004:fixed|} (int* pointer = values)
                        *pointer = 0;
                    {|FDGA004:foreach|} (var (key, value) in pairs)
                        values[key] = value;
                }
            }
            """,
            """
            class C
            {
                unsafe void M(int[] values, (int Key, int Value)[] pairs)
                {
                    fixed (int* pointer = values)
                    {
                        *pointer = 0;
                    }
                    foreach (var (key, value) in pairs)
                    {
                        values[key] = value;
                    }
                }
            }
            """,
            allowUnsafe: true);
    }

    [Fact]
    public Task If_and_else_both_get_braces()
    {
        return Verifier.VerifyAsync(
            """
            class C
            {
                int M(bool condition)
                {
                    M(condition);
                    {|FDGA004:if|} (condition) return 1; {|FDGA004:else|} return 2;
                }
            }
            """,
            """
            class C
            {
                int M(bool condition)
                {
                    M(condition);
                    if (condition)
                    {
                        return 1;
                    }
                    else
                    {
                        return 2;
                    }
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
                void M(bool condition)
                {
                    M(condition);
                    {|FDGA004:if|} (condition) M(false);
                }
            }
            """,
            """
            class C
            {
                void M(bool condition)
                {
                    M(condition);
                    if (condition)
                    {
                        M(false);
                    }
                }
            }
            """,
            endOfLine: "\r\n");
    }

    [Fact]
    public Task Tab_indentation_is_kept()
    {
        return Verifier.VerifyAsync(
            "class C\n{\n\tvoid M(bool condition)\n\t{\n\t\tM(condition);\n\t\t{|FDGA004:if|} (condition)\n\t\t\tM(false);\n\t}\n}\n",
            "class C\n{\n\tvoid M(bool condition)\n\t{\n\t\tM(condition);\n\t\tif (condition)\n\t\t{\n\t\t\tM(false);\n\t\t}\n\t}\n}\n",
            extraEditorConfig: "indent_style = tab\n");
    }
}
