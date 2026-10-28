using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class UsageDescriptorTests
{
    private static readonly string[] _ids =
    [
        "FDG8002", "FDG8011", "FDG8014", "FDG8015", "FDG8016", "FDG8017", "FDG8026", "FDG8031", "FDG8050", "FDG8051",
        "FDG8053", "FDG8057", "FDG8070", "FDG8082", "FDG8088", "FDG8089", "FDG8096", "FDG8102", "FDG8103", "FDG8104",
        "FDG8106",
    ];

    private static readonly ImmutableArray<DiagnosticAnalyzer> _analyzers =
    [
        new UsageSignatureAnalyzer(),
        new AttributeConstructorAnalyzer(),
        new InterfaceImplementationAnalyzer(),
        new ReturnValueAnalyzer(),
        new EqualityAnalyzer(),
        new SerializationAnalyzer(),
    ];

    [Fact]
    public async Task Descriptors_are_disabled_by_default_and_in_the_usage_category()
    {
        DiagnosticDescriptor[] descriptors = _analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).ToArray();
        Assert.Equal(_ids, descriptors.Select(descriptor => descriptor.Id).Order());
        Assert.All(descriptors, descriptor => Assert.False(descriptor.IsEnabledByDefault));
        Assert.All(descriptors, descriptor => Assert.Equal("FrameworkDesignGuidelines.Usage", descriptor.Category));

        const string Source =
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.Serialization;
            using System.Xml;

            public class Holder
            {
                public static readonly int[] Values = new int[1];

                public ArrayList Items { get; }

                public Hashtable Index { get; }

                public IEnumerator Cursor { get; }

                public List<byte> Bytes { get; }

                public ICloneable Prototype { get; }

                public XmlNode Node { get; }

                private List<int> Nothing()
                {
                    return null;
                }
            }

            public sealed class TwoAttribute : Attribute
            {
                public TwoAttribute() { }
                public TwoAttribute(int value) { }
            }

            internal class Both : IEnumerable, IEnumerator
            {
                public object Current { get { return null; } }
                public bool MoveNext() { return false; }
                public void Reset() { }
                public IEnumerator GetEnumerator() { return this; }
            }

            internal class Copyable : ICloneable
            {
                public object Clone() { return null; }
            }

            public struct Point
            {
                public int X;
            }

            public class Version2 : IComparable<Version2>
            {
                public int CompareTo(Version2 other) { return 0; }
            }

            public class Person
            {
                public string Name;
                public override bool Equals(object obj) { return false; }
                public override int GetHashCode() { return 0; }
                public override string ToString() { return null; }
            }

            public class Account
            {
                public int Balance;
                public static bool operator ==(Account left, Account right) { return true; }
                public static bool operator !=(Account left, Account right) { return false; }
                public static bool operator ==(Account left, int right) { return true; }
                public static bool operator !=(Account left, int right) { return false; }
            }

            public class Data : ISerializable
            {
                public Data(SerializationInfo info, StreamingContext context) { }
                public void GetObjectData(SerializationInfo info, StreamingContext context) { }
            }
            """;
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Disabled",
            [CSharpSyntaxTree.ParseText(Source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(System.Xml.XmlNode).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Empty(diagnostics);

        // Control: the same source does produce diagnostics once a configuration turns the rules on.
        CSharpCompilation enabled = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            descriptors.ToDictionary(descriptor => descriptor.Id, _ => ReportDiagnostic.Warn)));
        ImmutableArray<Diagnostic> enabledDiagnostics = await enabled
            .WithAnalyzers(_analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(_ids, enabledDiagnostics.Select(diagnostic => diagnostic.Id).Order());
    }
}
