using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Exceptions.SetterParameterNameAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Exceptions;

public sealed class SetterParameterNameTests
{
    [Fact]
    public Task Names_other_than_value_in_setters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Document
            {
                private string _title;
                private int _size;

                public string Title
                {
                    get { return _title; }
                    set
                    {
                        if (value == null)
                        {
                            throw new ArgumentNullException({|FDG7044:nameof(Title)|});
                        }

                        _title = value;
                    }
                }

                public int Size
                {
                    get { return _size; }
                    set
                    {
                        if (value < 0)
                        {
                            throw new ArgumentOutOfRangeException({|FDG7044:"size"|}, value, "The size is negative.");
                        }

                        _size = value;
                    }
                }

                public string this[int index]
                {
                    get { return _title; }
                    set { _title = value ?? throw new ArgumentException("No title.", {|FDG7044:"Item"|}); }
                }
            }
            """);
    }

    [Fact]
    public Task Value_indexer_parameters_and_other_members_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Document
            {
                private string _title;

                public string Title
                {
                    get { return _title ?? throw new InvalidOperationException(nameof(Title)); }
                    set { _title = value ?? throw new ArgumentNullException(nameof(value)); }
                }

                public string this[int index]
                {
                    get { return _title; }
                    set
                    {
                        if (index < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(index));
                        }

                        _title = value ?? throw new ArgumentNullException("value", "No title.");
                    }
                }

                public string Summary
                {
                    set { _title = value ?? throw new ArgumentException(nameof(Summary)); }
                }

                public void SetTitle(string title)
                {
                    _title = title ?? throw new ArgumentNullException(nameof(Title));
                }
            }
            """);
    }

    [Fact]
    public Task Init_accessors_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            using System;

            public class Document
            {
                private readonly string _title;

                public string Title
                {
                    get => _title;
                    init => _title = value ?? throw new ArgumentNullException({|FDG7044:nameof(Title)|});
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_parameter_name_used()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class Document
            {
                private string _title;

                public string Title
                {
                    set { _title = value ?? throw new ArgumentNullException({|#0:nameof(Title)|}); }
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG7044).WithLocation(0).WithArguments("Title"));
    }
}
