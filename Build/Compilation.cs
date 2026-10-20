using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Text;

namespace Cnidaria;

internal sealed class BuildCompileRequest
{
    private readonly Func<string, byte[]> _read;

    internal BuildCompileRequest(
        string module,
        BuildOutputKind outputKind,
        TargetArchitectureKind architecture,
        OperatingSystemKind operatingSystem,
        TargetArchitectureFeatures features,
        string configuration,
        IReadOnlyList<BuildSource> sources,
        IReadOnlyList<string> nativeLibraries,
        IReadOnlyDictionary<string, string> properties,
        Func<string, byte[]> read,
        CancellationToken cancellationToken)
    {
        Module = module;
        OutputKind = outputKind;
        Architecture = architecture;
        OperatingSystem = operatingSystem;
        Features = features;
        Configuration = configuration;
        Sources = sources;
        NativeLibraries = nativeLibraries;
        Properties = properties;
        _read = read;
        CancellationToken = cancellationToken;
    }

    internal string Module { get; }
    internal BuildOutputKind OutputKind { get; }
    internal TargetArchitectureKind Architecture { get; }
    internal OperatingSystemKind OperatingSystem { get; }
    internal TargetArchitectureFeatures Features { get; }
    internal string Configuration { get; }
    internal IReadOnlyList<BuildSource> Sources { get; }
    internal IReadOnlyList<string> NativeLibraries { get; }
    internal IReadOnlyDictionary<string, string> Properties { get; }
    internal CancellationToken CancellationToken { get; }

    internal byte[] ReadFile(string path) => _read(BuildPath.Normalize(path));
}

internal readonly record struct BuildCompilerOutput(byte[]? Bytes, IReadOnlyList<string> Diagnostics)
{
    internal static BuildCompilerOutput Failure(string message) => new(null, new[] { message });
}

internal static class BuildCompilation
{
    internal static Language Resolve(string path, string? explicitCompiler)
    {
        if (explicitCompiler is not null)
        {
            if (explicitCompiler.Equals("c", StringComparison.OrdinalIgnoreCase))
                return Language.C;
            if (explicitCompiler.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
                explicitCompiler.Equals("cs", StringComparison.OrdinalIgnoreCase))
                return Language.Cs;

            throw new BuildException($"Unknown compiler '{explicitCompiler}'.");
        }

        if (path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            return Language.C;
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return Language.Cs;

        throw new BuildException($"Cannot infer compiler from '{path}'. Specify Compiler explicitly.");
    }

    internal static BuildBackend Backend(Language language) => language switch
    {
        Language.C => BuildBackend.Unmanaged,
        Language.Cs => BuildBackend.Managed,
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    internal static BuildCompilerOutput Compile(Language language, BuildCompileRequest request) => language switch
    {
        Language.C => CompileC(request),
        Language.Cs => CompileCSharp(request),
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        return StrictUtf8.GetString(bytes).TrimStart('\uFEFF');
    }

    private static BuildCompilerOutput CompileC(BuildCompileRequest request)
    {
        if (request.OutputKind == BuildOutputKind.Library)
            return BuildCompilerOutput.Failure("The C compiler does not emit relocatable or static libraries.");
        if (request.OutputKind == BuildOutputKind.SharedLibrary && request.OperatingSystem != OperatingSystemKind.Linux)
            return BuildCompilerOutput.Failure("The C compiler emits shared libraries only for Linux ELF.");
        var sources = request.Sources.Select(s => new C.SourceFile(s.Path, Decode(s.Contents.Span))).ToArray();
        var search = request.Properties.TryGetValue("IncludePath", out var include) ?
            include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => BuildPath.Normalize(p, request.Properties["ProjectDir"])).ToArray() : Array.Empty<string>();
        var target = C.TargetInfo.ForArchitecture(request.Architecture, request.OperatingSystem, request.Features);
        var shared = request.OutputKind == BuildOutputKind.SharedLibrary;
        var release = request.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);
        var optimizer = release ? C.SsaOptimizationOptions.Default : new C.SsaOptimizationOptions(
            enableConstantFolding: false, enableCopyPropagation: false,
            enableBranchFolding: false, enableDeadCodeElimination: false,
            enableCommonSubexpressionElimination: false, enableLoopInvariantCodeMotion: false,
            enableInductionVariables: false, enableAssertionPropagation: false);
        var resolver = new VirtualIncludeResolver(request, search);
        var options = new C.StaticLinkerOptions(target,
            includeSearchPaths: search, includeResolver: resolver,
            linkStandardLibrary: !shared, emitStartup: !shared, allowUndefinedSymbols: shared,
            positionIndependentCode: shared,
            inlining: new C.InliningOptions(enabled: release),
            gimple: new C.GimplePipelineOptions(optimization: optimizer),
            entryFunctionName: request.Properties.TryGetValue("EntryPoint", out var entry) ? entry : "main");
        request.CancellationToken.ThrowIfCancellationRequested();
        if (target.IsX86)
        {
            var result = C.StaticLinker.LinkX86(sources, options);
            return Emit(result.Diagnostics.Select(x => x.ToString()), result.Program is null ? null :
                shared ? result.Program.ToSharedObjectBytes("lib" + request.Module + ".so") : result.Program.ToExecutableBytes());
        }
        if (target.IsArm)
        {
            var result = C.StaticLinker.LinkArm(sources, options);
            return Emit(result.Diagnostics.Select(x => x.ToString()), result.Program is null ? null :
                shared ? result.Program.ToSharedObjectBytes("lib" + request.Module + ".so") : result.Program.ToExecutableBytes());
        }
        if (target.IsRiscV)
        {
            var result = C.StaticLinker.LinkRiscV(sources, options);
            return Emit(result.Diagnostics.Select(x => x.ToString()), result.Program is null ? null :
                shared ? result.Program.ToSharedObjectBytes("lib" + request.Module + ".so") : result.Program.ToExecutableBytes());
        }
        if (target.IsRegisterBytecode && !shared)
        {
            var result = C.StaticLinker.LinkRegisterBytecode(sources, options);
            return Emit(result.Diagnostics.Select(x => x.ToString()), result.Program?.Image.ToExecutableBytes());
        }
        return BuildCompilerOutput.Failure($"C code generation is not available for {request.Architecture}.");
    }

    private static BuildCompilerOutput Emit(IEnumerable<string> diagnostics, byte[]? bytes) =>
        new(bytes, diagnostics.ToArray());

    private sealed class VirtualIncludeResolver : C.IIncludeResolver
    {
        private readonly BuildCompileRequest _request;
        private readonly string[] _search;
        public VirtualIncludeResolver(BuildCompileRequest request, string[] search)
        {
            _request = request;
            _search = search;
        }
        public bool TryResolveInclude(C.IncludeDirective directive, out C.IncludeFile file)
        {
            file = default;
            if (string.IsNullOrWhiteSpace(directive.Name) || directive.Name[0] == '/' ||
                directive.Name.StartsWith("\\", StringComparison.Ordinal) || directive.Name.Contains(':'))
                return false;
            var directories = new List<string>();
            if (!directive.IsAngled && directive.CurrentFilePath is { Length: > 0 })
                directories.Add(BuildPath.Directory(directive.CurrentFilePath));
            directories.AddRange(_search);
            foreach (var root in directories)
            {
                string path;
                try { path = BuildPath.Normalize(directive.Name, root); }
                catch (BuildException) { continue; }
                try
                {
                    var contents = _request.ReadFile(path);
                    file = new C.IncludeFile(path, Decode(contents));
                    return true;
                }
                catch (System.IO.FileNotFoundException) { }
                catch (System.IO.DirectoryNotFoundException) { }
            }
            return false;
        }
    }

    private static BuildCompilerOutput CompileCSharp(BuildCompileRequest request)
    {
        if (request.OutputKind == BuildOutputKind.SharedLibrary)
            return BuildCompilerOutput.Failure("The managed compiler cannot emit native shared libraries.");
        if (request.OutputKind == BuildOutputKind.Library && request.NativeLibraries.Count != 0)
            return BuildCompilerOutput.Failure("NativeReference requires a managed executable, not a metadata library.");
        var sources = request.Sources.Select(s => (s.Path, Decode(s.Contents.Span))).ToImmutableArray();
        var target = Cs.TargetInfo.ForArchitecture(request.Architecture, request.OperatingSystem, request.Features);
        request.CancellationToken.ThrowIfCancellationRequested();
        var (bytes, diagnostics) = Cs.CSharp.CompileProjectSources(sources, target,
            library: request.OutputKind == BuildOutputKind.Library, moduleName: request.Module,
            neededLibraries: request.NativeLibraries);
        return new BuildCompilerOutput(bytes, diagnostics.Select(d => d.Message).ToArray());
    }
}