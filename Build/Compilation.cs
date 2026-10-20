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
        IReadOnlyList<BuildSource> nativeLibraries,
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
    internal IReadOnlyList<BuildSource> NativeLibraries { get; }
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
            if (explicitCompiler.Equals("rust", StringComparison.OrdinalIgnoreCase) || explicitCompiler.Equals("rs", StringComparison.OrdinalIgnoreCase))
                return Language.Rust;
            if (explicitCompiler.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
                explicitCompiler.Equals("cs", StringComparison.OrdinalIgnoreCase))
                return Language.Cs;

            throw new BuildException($"Unknown compiler '{explicitCompiler}'.");
        }

        if (path.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            return Language.C;
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return Language.Cs;
        if (path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase))
            return Language.Rust;

        throw new BuildException($"Cannot infer compiler from '{path}'. Specify Compiler explicitly.");
    }

    internal static BuildBackend Backend(Language language) => language switch
    {
        Language.C or Language.Rust => BuildBackend.Unmanaged,
        Language.Cs => BuildBackend.Managed,
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    internal static BuildCompilerOutput Compile(Language language, BuildCompileRequest request) => language switch
    {
        Language.C => CompileC(request),
        Language.Rust => CompileRust(request),
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
        var sources = request.Sources.Select(s => new C.SourceFile(s.Path, Decode(s.Contents.Span))).ToArray();
        return CompileUnmanaged(request, sources, CreateUnmanagedOptions(request));
    }

    private static C.StaticLinkerOptions CreateUnmanagedOptions(BuildCompileRequest request)
    {
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
        return new C.StaticLinkerOptions(target,
            includeSearchPaths: search, includeResolver: resolver,
            linkStandardLibrary: !shared || target.IsRegisterBytecode, emitStartup: !shared, allowUndefinedSymbols: shared,
            positionIndependentCode: shared && !target.IsRegisterBytecode,
            inlining: new C.InliningOptions(enabled: release),
            gimple: new C.GimplePipelineOptions(optimization: optimizer),
            entryFunctionName: request.Properties.TryGetValue("EntryPoint", out var entry) ? entry : "main");
    }

    private static BuildCompilerOutput CompileUnmanaged(BuildCompileRequest request, IEnumerable<C.TranslationUnit> sources, C.StaticLinkerOptions options)
    {
        var target = options.CompilationOptions.Target;
        bool shared = request.OutputKind == BuildOutputKind.SharedLibrary;
        if (shared && !target.IsRegisterBytecode && request.OperatingSystem != OperatingSystemKind.Linux)
            return BuildCompilerOutput.Failure("Native shared libraries require Linux ELF; bytecode libraries require an RBC target.");
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
        if (target.IsRegisterBytecode)
        {
            var result = C.StaticLinker.LinkRegisterBytecode(sources, options);
            return Emit(result.Diagnostics.Select(x => x.ToString()), result.Program is null ? null :
                shared ? result.Program.ToLibrary(request.Module).ToBytes() : result.Program.Image.ToExecutableBytes());
        }
        return BuildCompilerOutput.Failure($"Unmanaged code generation is not available for {request.Architecture}.");
    }

    private static BuildCompilerOutput CompileRust(BuildCompileRequest request)
    {
        if (request.OutputKind == BuildOutputKind.Library)
            return BuildCompilerOutput.Failure("Rust metadata/static libraries are not implemented; use SharedLibrary for C ABI exports.");
        string root;
        if (request.Properties.TryGetValue("CrateRoot", out string? selected))
            root = BuildPath.Normalize(selected, request.Properties["ProjectDir"]);
        else if (request.Sources.Count == 1) root = request.Sources[0].Path;
        else
        {
            string filename = request.OutputKind == BuildOutputKind.Executable ? "main.rs" : "lib.rs";
            var candidates = request.Sources.Where(s => System.IO.Path.GetFileName(s.Path) == filename).ToArray();
            if (candidates.Length != 1) return BuildCompilerOutput.Failure($"Select a unique {filename} or set CrateRoot for Rust module '{request.Module}'.");
            root = candidates[0].Path;
        }
        var sources = request.Sources.ToDictionary(s => s.Path, StringComparer.Ordinal);
        if (!sources.TryGetValue(root, out BuildSource source)) return BuildCompilerOutput.Failure($"CrateRoot '{root}' is not a Compile item.");
        var used = new HashSet<string>(StringComparer.Ordinal) { root };
        var modules = new Dictionary<string, Rust.SyntaxTree>(StringComparer.Ordinal);
        Rust.SyntaxTree tree = Rust.SyntaxTree.ParseText(Decode(source.Contents.Span), filePath: root);
        string directory = BuildPath.Directory(root);
        void Modules(IEnumerable<Rust.ItemSyntax> items, string prefix, int depth)
        {
            if (depth > 256) throw new BuildException("Rust module nesting exceeds the binding limit.");
            foreach (Rust.ModuleItemSyntax module in items.OfType<Rust.ModuleItemSyntax>())
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                string identifier = Rust.Binder.Name(module.Identifier);
                if (identifier.Length == 0) continue;
                string name = prefix.Length == 0 ? identifier : prefix + "::" + identifier;
                if (module.Body is not null) { Modules(module.Body.Items, name, depth + 1); continue; }
                string path = BuildPath.Normalize(name.Replace("::", "/"), directory);
                var found = new List<BuildSource>();
                foreach (string candidate in new[] { path + ".rs", path + "/mod.rs" })
                {
                    if (sources.TryGetValue(candidate, out BuildSource included)) found.Add(included);
                    else
                    {
                        try { found.Add(new BuildSource(candidate, request.ReadFile(candidate))); }
                        catch (System.IO.FileNotFoundException) { }
                        catch (System.IO.DirectoryNotFoundException) { }
                    }
                }
                if (found.Count > 1) throw new BuildException($"Rust module '{name}' has both .rs and mod.rs sources.");
                if (found.Count == 0) continue;
                BuildSource moduleSource = found[0];
                if (!used.Add(moduleSource.Path)) throw new BuildException($"Rust module source '{moduleSource.Path}' is used more than once.");
                Rust.SyntaxTree moduleTree = Rust.SyntaxTree.ParseText(Decode(moduleSource.Contents.Span), filePath: moduleSource.Path);
                modules.Add(name, moduleTree);
                Modules(moduleTree.Root.Items, name, depth + 1);
            }
        }
        Modules(tree.Root.Items, "", 0);
        foreach (BuildSource included in request.Sources)
            if (!used.Contains(included.Path)) return BuildCompilerOutput.Failure($"Rust source '{included.Path}' is not referenced by the crate root.");
        var options = CreateUnmanagedOptions(request);
        var compilation = Rust.Compilation.Create(tree, new Rust.CompilationOptions
        {
            CrateName = request.Module, PointerWidth = options.CompilationOptions.Target.PointerSize * 8,
            IsLibrary = request.OutputKind != BuildOutputKind.Executable
        }, modules);
        var lowered = compilation.GetGimpleTree(options.CompilationOptions);
        if (lowered.Tree is null) return Emit(lowered.Diagnostics.Select(d => d.ToString() ?? d.GetMessage("")), null);
        return CompileUnmanaged(request, [new C.GimpleTranslationUnit(root, lowered.Tree)], options);
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
            neededLibraries: target.IsRegisterBytecode ? null : request.NativeLibraries.Select(l => System.IO.Path.GetFileName(l.Path)).ToArray(),
            bytecodeLibraries: target.IsRegisterBytecode ? request.NativeLibraries.Select(l => Cs.RegisterBytecodeLibrary.FromBytes(l.Contents.ToArray())).ToArray() : null);
        return new BuildCompilerOutput(bytes, diagnostics.Select(d => d.Message).ToArray());
    }
}
