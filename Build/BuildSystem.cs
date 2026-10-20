using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;

namespace Cnidaria;

/// <summary>Host policy for a single build. Host properties override project properties.</summary>
public sealed class BuildOptions
{
    public string BuildTarget { get; init; } = "Build";
    public IReadOnlyDictionary<string, string> Properties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public BuildLimits Limits { get; init; } = BuildLimits.Default;
    public CancellationToken CancellationToken { get; init; }

    public BuildOptions() { }
    public BuildOptions(BuildLimits limits, CancellationToken cancellationToken)
    {
        Limits = limits;
        CancellationToken = cancellationToken;
    }
    public BuildOptions(IReadOnlyDictionary<string, string> properties, BuildLimits limits, CancellationToken cancellationToken)
        : this(limits, cancellationToken) => Properties = properties ?? throw new ArgumentNullException(nameof(properties));
    public BuildOptions(string buildTarget, IReadOnlyDictionary<string, string> properties, BuildLimits limits, CancellationToken cancellationToken)
        : this(properties, limits, cancellationToken) => BuildTarget = buildTarget;
}

public readonly record struct BuildLimits(
    int MaxProjectBytes,
    int MaxXmlDepth,
    int MaxXmlNodes,
    int MaxImports,
    int MaxImportDepth,
    int MaxProperties,
    int MaxItems,
    int MaxTargets,
    int MaxTasks,
    int MaxExpansionCharacters,
    int MaxFileBytes,
    long MaxBuildReadBytes,
    long MaxBuildOutputBytes)
{
    public static readonly BuildLimits Default = new BuildLimits(
        MaxProjectBytes: 256 * 1024,
        MaxXmlDepth: 24,
        MaxXmlNodes: 4096,
        MaxImports: 32,
        MaxImportDepth: 8,
        MaxProperties: 256,
        MaxItems: 4096,
        MaxTargets: 128,
        MaxTasks: 1024,
        MaxExpansionCharacters: 32 * 1024,
        MaxFileBytes: 16 * 1024 * 1024,
        MaxBuildReadBytes: 128L * 1024 * 1024,
        MaxBuildOutputBytes: 128L * 1024 * 1024);
    public static readonly BuildLimits None = new BuildLimits(
        MaxProjectBytes: int.MaxValue,
        MaxXmlDepth: int.MaxValue,
        MaxXmlNodes: int.MaxValue,
        MaxImports: int.MaxValue,
        MaxImportDepth: int.MaxValue,
        MaxProperties: int.MaxValue,
        MaxItems: int.MaxValue,
        MaxTargets: int.MaxValue,
        MaxTasks: int.MaxValue,
        MaxExpansionCharacters: int.MaxValue,
        MaxFileBytes: int.MaxValue,
        MaxBuildReadBytes: long.MaxValue,
        MaxBuildOutputBytes: long.MaxValue);
    internal void Validate()
    {
        if (MaxProjectBytes < 64 || MaxXmlDepth < 2 || MaxXmlNodes < 1 || MaxImports < 0 ||
            MaxImportDepth < 1 || MaxProperties < 1 || MaxItems < 1 || MaxTargets < 1 ||
            MaxTasks < 1 || MaxExpansionCharacters < 32 || MaxFileBytes < 1 ||
            MaxBuildReadBytes < 1 || MaxBuildOutputBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(BuildLimits), "All build budgets must be positive.");
    }
}
/// <summary>Specifies the severity of a diagnostic</summary>
public enum DiagnosticSeverity : byte { Error, Warning, Message }

public enum Language : byte
{
    C, Cs, Cpp, Rust, Java, Python
}

public enum BuildBackend : byte { Unmanaged, Managed }
public enum BuildOutputKind : byte { Executable, Library, SharedLibrary }

internal readonly record struct BuildSource(string Path, ReadOnlyMemory<byte> Contents);

public readonly record struct BuildArtifact(string Module, BuildBackend Backend, BuildOutputKind Kind, string Path, long Length);

public sealed class BuildResult
{
    internal BuildResult(IReadOnlyDictionary<string, string> properties, IReadOnlyList<BuildArtifact> artifacts,
        IReadOnlyList<string> diagnostics, bool succeeded)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties) snapshot.Add(pair.Key, pair.Value);
        Properties = new ReadOnlyDictionary<string, string>(snapshot);
        Artifacts = Array.AsReadOnly(new List<BuildArtifact>(artifacts).ToArray());
        Diagnostics = Array.AsReadOnly(new List<string>(diagnostics).ToArray());
        Succeeded = succeeded;
    }
    public IReadOnlyDictionary<string, string> Properties { get; }
    public IReadOnlyList<BuildArtifact> Artifacts { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public bool Succeeded { get; }
}

public static class BuildSystem
{
    public static BuildResult Build(string projectPath, IFileSystem fileSystem, BuildOptions? options = null)
    {
        if (fileSystem is null)
            throw new ArgumentNullException(nameof(fileSystem));

        options ??= new BuildOptions();
        options.Limits.Validate();
        return new BuildEngine(BuildPath.Normalize(projectPath), fileSystem, options).Run();
    }
}
