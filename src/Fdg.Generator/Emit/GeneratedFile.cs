namespace Fdg.Generator.Emit;

/// <summary>One generated file: its path relative to the repository root (with <c>/</c> separators) and its text.</summary>
public sealed record GeneratedFile(string Path, string Content);
