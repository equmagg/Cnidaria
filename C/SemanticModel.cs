using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Cnidaria.C
{
    /// <summary>Describes a semantic diagnostic and its source span</summary>
    public readonly struct SemanticDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message => _message;
        public TextSpan Position { get; }
        private readonly string _message;

        public SemanticDiagnostic(DiagnosticSeverity severity, string? message, TextSpan position)
        {
            Severity = severity;
            _message = message ?? string.Empty;
            Position = position;
        }

        /// <summary>Formats the diagnostic with its source location</summary>
        public string GetMessage(string source) => _message + $" {Position.ToString(source)}";

        /// <summary>Creates an error diagnostic</summary>
        public static SemanticDiagnostic Error(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Error, message, position);

        /// <summary>Creates a warning diagnostic</summary>
        public static SemanticDiagnostic Warning(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Warning, message, position);

        /// <summary>Creates an informational diagnostic</summary>
        public static SemanticDiagnostic MessageInfo(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Message, message, position);
    }

    /// <summary>C syntax tree root</summary>
    /// <remarks>Owns source text, and the parsed translation unit</remarks>
    public sealed class SyntaxTree
    {
        public string Text { get; }
        public string? FilePath { get; }
        public PreprocessorOptions PreprocessorOptions { get; }
        public ParseResult ParseResult { get; }

        public TranslationUnitSyntax Root => ParseResult.Root;
        public ImmutableArray<SyntaxDiagnostic> Diagnostics => ParseResult.Diagnostics;

        private SyntaxTree(string text, PreprocessorOptions options, ParseResult parseResult)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            PreprocessorOptions = options ?? throw new ArgumentNullException(nameof(options));
            FilePath = options.FilePath;
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        /// <summary>Parses source text</summary>
        public static SyntaxTree ParseText(string text, PreprocessorOptions? options = null)
        {
            var effectiveOptions = options ?? PreprocessorOptions.CreateDefault();
            var parseResult = Parser.Parse(text, effectiveOptions);
            return new SyntaxTree(text, effectiveOptions, parseResult);
        }

        /// <summary>Parses source text with an in-memory include environment</summary>
        public static SyntaxTree ParseSource(
            string text,
            string? filePath = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            PreprocessorEnvironment? environment = null,
            bool includeStandardHeaders = true)
        {
            var effectiveOptions = PreprocessorOptions.CreateForInMemoryFiles(
                filePath: filePath,
                includeFiles: includeFiles,
                includeSearchPaths: includeSearchPaths,
                includeResolver: includeResolver,
                predefinedMacros: predefinedMacros,
                environment: environment,
                includeStandardHeaders: includeStandardHeaders);

            return ParseText(text, effectiveOptions);
        }
    }

    /// <summary>Groups target and whole-compilation transformation options</summary>
    public sealed class CompilationOptions
    {
        public TargetInfo Target { get; }
        public InliningOptions Inlining { get; }
        public TrimmingOptions Trimming { get; }
        public CompilationOptions(
            TargetInfo? target = null,
            InliningOptions? inlining = null,
            TrimmingOptions? trimming = null)
        {
            Target = target ?? TargetInfo.Default;
            Inlining = inlining ?? InliningOptions.Default;
            Trimming = trimming ?? TrimmingOptions.Default;
        }
    }

    /// <summary>Represents an immutable set of syntax trees compiled for one target</summary>
    /// <remarks>Declaration collection is lazy and shared by all semantic models from this instance</remarks>
    public sealed class Compilation
    {
        private readonly Lazy<SemanticState> _semanticState;

        public string? AssemblyName { get; }
        public ImmutableArray<SyntaxTree> SyntaxTrees { get; }
        public CompilationOptions Options { get; }

        private Compilation(
            string? assemblyName,
            ImmutableArray<SyntaxTree> syntaxTrees,
            CompilationOptions options)
        {
            AssemblyName = assemblyName;
            SyntaxTrees = syntaxTrees;
            Options = options ?? new CompilationOptions();

            _semanticState = new Lazy<SemanticState>(
                () => DeclarationCollector.Collect(this),
                isThreadSafe: true);
        }

        /// <summary>Gets the lazily collected declaration state</summary>
        internal SemanticState SemanticState => _semanticState.Value;

        /// <summary>Gets the translation-unit scope shared by all syntax trees</summary>
        public Scope GlobalScope => SemanticState.GlobalScope;

        /// <summary>Gets diagnostics produced while collecting declarations</summary>
        public ImmutableArray<SemanticDiagnostic> SemanticDiagnostics
            => SemanticState.Diagnostics;

        /// <summary>Creates a single-source compilation for the selected target</summary>
        public static Compilation Create(
            string text,
            TargetInfo? target)
            => Create(text, null, new CompilationOptions(target));

        /// <summary>Creates a single-source compilation</summary>
        public static Compilation Create(
            string text,
            string? assemblyName = null,
            CompilationOptions? options = null,
            PreprocessorOptions? preprocessorOptions = null)
        {
            options ??= new CompilationOptions();
            return Create(
                new[] { SyntaxTree.ParseText(text, preprocessorOptions
                ?? new PreprocessorOptions(environment: PreprocessorEnvironment.ForTarget(options.Target))) },
                assemblyName,
                options);
        }

        /// <summary>Creates a single-source compilation with an in-memory include environment</summary>
        public static Compilation CreateFromSource(
            string text,
            string? filePath = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            PreprocessorEnvironment? environment = null,
            bool includeStandardHeaders = true,
            string? assemblyName = null,
            CompilationOptions? options = null)
        {
            options ??= new CompilationOptions();
            return Create(
                new[]
                {
                    SyntaxTree.ParseSource(
                        text,
                        filePath,
                        includeFiles,
                        includeSearchPaths,
                        includeResolver,
                        predefinedMacros,
                        environment ?? PreprocessorEnvironment.ForTarget(options.Target),
                        includeStandardHeaders)
                },
                assemblyName,
                options);
        }

        /// <summary>Creates a compilation from pre-parsed syntax trees</summary>
        public static Compilation Create(
            IEnumerable<SyntaxTree> syntaxTrees,
            string? assemblyName = null,
            CompilationOptions? options = null)
        {
            if (syntaxTrees is null)
                throw new ArgumentNullException(nameof(syntaxTrees));

            return new Compilation(
                assemblyName,
                syntaxTrees.ToImmutableArray(),
                options ?? new CompilationOptions());
        }

        /// <summary>Creates a compilation with additional syntax trees</summary>
        public Compilation AddSyntaxTrees(params SyntaxTree[] syntaxTrees)
        {
            if (syntaxTrees is null)
                throw new ArgumentNullException(nameof(syntaxTrees));

            return new Compilation(
                AssemblyName,
                SyntaxTrees.AddRange(syntaxTrees),
                Options);
        }

        /// <summary>Creates a semantic view for a syntax tree in this compilation</summary>
        public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
        {
            if (syntaxTree is null)
                throw new ArgumentNullException(nameof(syntaxTree));

            if (!SyntaxTrees.Contains(syntaxTree))
                throw new ArgumentException("The syntax tree is not part of this compilation.", nameof(syntaxTree));

            return new SemanticModel(this, syntaxTree);
        }

        /// <summary>Collects syntax, declaration, and binding diagnostics</summary>
        /// <remarks>Binding diagnostics are appended after the declaration diagnostics already present in each bound tree</remarks>
        public ImmutableArray<IDiagnostic> GetDiagnostics()
        {
            var builder = ImmutableArray.CreateBuilder<IDiagnostic>();

            foreach (var tree in SyntaxTrees)
            {
                foreach (var diagnostic in tree.Diagnostics)
                    builder.Add(diagnostic);
            }

            var semanticDiagnostics = SemanticDiagnostics;
            foreach (var diagnostic in semanticDiagnostics)
                builder.Add(diagnostic);

            foreach (var tree in SyntaxTrees)
            {
                var boundDiagnostics = GetSemanticModel(tree).GetBoundTree().Diagnostics;
                for (var i = semanticDiagnostics.Length; i < boundDiagnostics.Length; i++)
                    builder.Add(boundDiagnostics[i]);
            }

            return builder.ToImmutable();
        }
    }

    /// <summary>Provides semantic queries and lowering entry points for one syntax tree</summary>
    public sealed class SemanticModel
    {
        private readonly Compilation _compilation;
        private readonly SyntaxTree _syntaxTree;

        internal SemanticModel(Compilation compilation, SyntaxTree syntaxTree)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _syntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        }

        public Compilation Compilation => _compilation;
        public SyntaxTree SyntaxTree => _syntaxTree;
        public TranslationUnitSyntax Root => _syntaxTree.Root;

        /// <summary>Binds the syntax tree into a typed semantic tree</summary>
        public BoundTree GetBoundTree() => Binder.BindTree(this);

        /// <summary>Lowers the bound tree into explicit control-flow form</summary>
        public GimpleTree GetGimpleTree() => GimpleTree.Lower(this);

        /// <summary>Gets the symbol introduced by a declaration syntax node</summary>
        public Symbol? GetDeclaredSymbol(SyntaxNode node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            return _compilation.SemanticState.GetDeclaredSymbol(node);
        }

        /// <summary>Gets the symbol referenced by an expression</summary>
        public Symbol? GetSymbolInfo(ExpressionSyntax expression)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            return _compilation.SemanticState.GetReferencedSymbol(expression);
        }

        /// <summary>Gets the lexical scope associated with a syntax node</summary>
        public Scope? GetScope(SyntaxNode node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            return _compilation.SemanticState.GetScope(node);
        }

        /// <summary>Looks up an ordinary identifier from the scope of a syntax node</summary>
        public Symbol? LookupOrdinaryName(string name, SyntaxNode context)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));
            if (context is null)
                throw new ArgumentNullException(nameof(context));

            return GetScope(context)?.LookupOrdinary(name);
        }

        /// <summary>Looks up a tag name from the scope of a syntax node</summary>
        public TagSymbol? LookupTag(string name, SyntaxNode context)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));
            if (context is null)
                throw new ArgumentNullException(nameof(context));

            return GetScope(context)?.LookupTag(name);
        }
    }

    /// <summary>Stores declaration, reference, and scope maps shared by semantic models</summary>
    internal sealed class SemanticState
    {
        private readonly Dictionary<SyntaxNode, Symbol> _declaredSymbols = new();
        private readonly Dictionary<ExpressionSyntax, Symbol> _referencedSymbols = new();
        private readonly Dictionary<SyntaxNode, Scope> _scopes = new();

        public Scope GlobalScope { get; }
        public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }

        public SemanticState(
            Scope globalScope,
            Dictionary<SyntaxNode, Symbol> declaredSymbols,
            Dictionary<ExpressionSyntax, Symbol> referencedSymbols,
            Dictionary<SyntaxNode, Scope> scopes,
            ImmutableArray<SemanticDiagnostic> diagnostics)
        {
            GlobalScope = globalScope ?? throw new ArgumentNullException(nameof(globalScope));

            foreach (var pair in declaredSymbols)
                _declaredSymbols[pair.Key] = pair.Value;

            foreach (var pair in referencedSymbols)
                _referencedSymbols[pair.Key] = pair.Value;

            foreach (var pair in scopes)
                _scopes[pair.Key] = pair.Value;

            Diagnostics = diagnostics;
        }

        public Symbol? GetDeclaredSymbol(SyntaxNode node)
            => _declaredSymbols.TryGetValue(node, out var symbol) ? symbol : null;

        public Symbol? GetReferencedSymbol(ExpressionSyntax expression)
            => _referencedSymbols.TryGetValue(expression, out var symbol) ? symbol : null;

        public Scope? GetScope(SyntaxNode node)
            => _scopes.TryGetValue(node, out var scope) ? scope : null;
    }

    /// <summary>Describes the target-dependent signedness of plain char</summary>
    public enum CharSignedness : byte { Signed, Unsigned, ImplementationDefined }


    /// <summary>Defines the size and alignment of a primitive target type</summary>
    public readonly struct PrimitiveLayout
    {
        public int Size { get; }
        public int Alignment { get; }

        public PrimitiveLayout(int size, int alignment)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (alignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(alignment));

            Size = size;
            Alignment = alignment;
        }
    }

    /// <summary>Defines target data layout, architecture, operating system, and features</summary>
    public sealed class TargetInfo
    {
        public static TargetInfo RegisterBytecode32 { get; } = CreateRegisterBytecode(pointerSize: 4);
        public static TargetInfo RegisterBytecode64 { get; } = CreateRegisterBytecode(pointerSize: 8);
        public static TargetInfo RiscV32 { get; } = ForArchitecture(TargetArchitectureKind.RiscV32);
        public static TargetInfo RiscV64 { get; } = ForArchitecture(TargetArchitectureKind.RiscV64);
        public static TargetInfo X86 { get; } = ForArchitecture(TargetArchitectureKind.I386);
        public static TargetInfo X64 { get; } = ForArchitecture(TargetArchitectureKind.X86_64);
        public static TargetInfo Arm32 { get; } = ForArchitecture(TargetArchitectureKind.Arm32, OperatingSystemKind.None,
            TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32 | TargetArchitectureFeatures.ArmNeon);
        public static TargetInfo Arm64 { get; } = ForArchitecture(TargetArchitectureKind.Arm64);
        public static TargetInfo Arm64Linux { get; } = ForArchitecture(TargetArchitectureKind.Arm64, OperatingSystemKind.Linux);
        public static TargetInfo Arm64Windows { get; } = ForArchitecture(TargetArchitectureKind.Arm64, OperatingSystemKind.Windows);
        public static TargetInfo RV64GLinux { get; } = ForArchitecture(TargetArchitectureKind.RiscV64, OperatingSystemKind.Linux, TargetArchitectureFeatures.RiscVG);
        public static TargetInfo RVA32Linux { get; } = ForArchitecture(TargetArchitectureKind.RiscV64, OperatingSystemKind.Linux, TargetArchitectureFeatures.RVA23);
        public static TargetInfo X64Windows { get; } = ForArchitecture(TargetArchitectureKind.X86_64, OperatingSystemKind.Windows);
        public static TargetInfo X64Linux { get; } = ForArchitecture(TargetArchitectureKind.X86_64, OperatingSystemKind.Linux);
        public static TargetInfo Default => RegisterBytecode32;

        /// <summary>Creates the canonical data model for an architecture and operating system</summary>
        /// <remarks>Mandatory baseline features are added before the target layout is created</remarks>
        public static TargetInfo ForArchitecture(
            TargetArchitectureKind architecture,
            OperatingSystemKind operatingSystem = OperatingSystemKind.None,
            TargetArchitectureFeatures features = TargetArchitectureFeatures.None)
        {
            if (architecture is TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64)
                features |= TargetArchitectureFeatures.X86Sse2;
            if (architecture == TargetArchitectureKind.Arm64)
                features |= TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32
                    | TargetArchitectureFeatures.ArmNeon | TargetArchitectureFeatures.ArmHardFloat;
            if (architecture == TargetArchitectureKind.Arm32 && operatingSystem == OperatingSystemKind.Windows)
                features |= TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32
                    | TargetArchitectureFeatures.ArmNeon | TargetArchitectureFeatures.ArmHardFloat;
            if (architecture is TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64)
                features |= TargetArchitectureFeatures.RiscVM | TargetArchitectureFeatures.RiscVF | TargetArchitectureFeatures.RiscVA;
            if (architecture == TargetArchitectureKind.RiscV64)
                features |= TargetArchitectureFeatures.RiscVD;
            return architecture switch
            {
                TargetArchitectureKind.RegisterBytecode => RegisterBytecode32.WithFeatures(features),
                TargetArchitectureKind.RegisterBytecode64 => RegisterBytecode64.WithFeatures(features),
                TargetArchitectureKind.RiscV32 => CreateILP32(TargetArchitectureKind.RiscV32, features, operatingSystem),
                TargetArchitectureKind.I386 => CreateILP32(TargetArchitectureKind.I386, features, operatingSystem),
                TargetArchitectureKind.Arm32 => CreateArm32(features, operatingSystem),
                TargetArchitectureKind.RiscV64 => CreateLP64(TargetArchitectureKind.RiscV64, features, operatingSystem),
                TargetArchitectureKind.X86_64 => operatingSystem == OperatingSystemKind.Windows
                    ? CreateWindowsX64(features)
                    : CreateLP64(TargetArchitectureKind.X86_64, features, operatingSystem),
                TargetArchitectureKind.Arm64 => operatingSystem == OperatingSystemKind.Windows
                    ? CreateWindowsArm64(features)
                    : CreateArm64(features, operatingSystem),
                _ => throw new ArgumentOutOfRangeException(nameof(architecture)),
            };
        }

        public TargetArchitectureKind Architecture { get; }
        public TargetArchitectureFeatures ArchitectureFeatures { get; }
        public OperatingSystemKind OperatingSystem { get; }
        public int PointerSize { get; }
        public int PointerAlignment { get; }
        public int RegisterSize { get; }
        public int RegisterAlignment { get; }

        public PrimitiveLayout CharLayout { get; }
        public PrimitiveLayout ShortLayout { get; }
        public PrimitiveLayout IntLayout { get; }
        public PrimitiveLayout LongLayout { get; }
        public PrimitiveLayout LongLongLayout { get; }
        public PrimitiveLayout FloatLayout { get; }
        public PrimitiveLayout DoubleLayout { get; }
        public PrimitiveLayout LongDoubleLayout { get; }
        public PrimitiveLayout BoolLayout { get; }
        public TargetEndianness Endianness { get; }
        public CharSignedness CharSignedness { get; }
        public bool Is32Bit => PointerSize == 4;
        public bool Is64Bit => PointerSize == 8;
        public bool IsRegisterBytecode => Architecture is TargetArchitectureKind.RegisterBytecode or TargetArchitectureKind.RegisterBytecode64;
        public bool IsRiscV => Architecture is TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64;
        public bool IsX86 => Architecture is TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64;
        public bool IsArm => Architecture is TargetArchitectureKind.Arm32 or TargetArchitectureKind.Arm64;

        /// <summary>Tests whether every requested architecture feature is enabled</summary>
        public bool HasFeature(TargetArchitectureFeatures feature)
            => (ArchitectureFeatures & feature) == feature;

        /// <summary>Creates an explicit target data layout</summary>
        public TargetInfo(
            int pointerSize,
            int pointerAlignment,
            int registerSize,
            int registerAlignment,
            PrimitiveLayout charLayout,
            PrimitiveLayout shortLayout,
            PrimitiveLayout intLayout,
            PrimitiveLayout longLayout,
            PrimitiveLayout longLongLayout,
            PrimitiveLayout floatLayout,
            PrimitiveLayout doubleLayout,
            PrimitiveLayout longDoubleLayout,
            PrimitiveLayout boolLayout,
            TargetEndianness endianness,
            CharSignedness charSignedness,
            TargetArchitectureKind architecture = TargetArchitectureKind.RegisterBytecode,
            OperatingSystemKind operatingSystem = OperatingSystemKind.None,
            TargetArchitectureFeatures features = TargetArchitectureFeatures.None)
        {
            if (pointerSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pointerSize));
            if (pointerAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(pointerAlignment));
            if (registerSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerSize));
            if (registerAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerAlignment));

            if (architecture is TargetArchitectureKind.I386 or TargetArchitectureKind.X86_64)
                features |= TargetArchitectureFeatures.X86Sse2;
            if (architecture == TargetArchitectureKind.Arm64)
                features |= TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32
                    | TargetArchitectureFeatures.ArmNeon | TargetArchitectureFeatures.ArmHardFloat;
            if (architecture == TargetArchitectureKind.Arm32 && operatingSystem == OperatingSystemKind.Windows)
                features |= TargetArchitectureFeatures.ArmVfp | TargetArchitectureFeatures.ArmVfpD32
                    | TargetArchitectureFeatures.ArmNeon | TargetArchitectureFeatures.ArmHardFloat;
            if (architecture is TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64)
                features |= TargetArchitectureFeatures.RiscVM;

            Architecture = architecture;
            ArchitectureFeatures = features;
            PointerSize = pointerSize;
            PointerAlignment = pointerAlignment;
            RegisterSize = registerSize;
            RegisterAlignment = registerAlignment;
            CharLayout = charLayout;
            ShortLayout = shortLayout;
            IntLayout = intLayout;
            LongLayout = longLayout;
            LongLongLayout = longLongLayout;
            FloatLayout = floatLayout;
            DoubleLayout = doubleLayout;
            LongDoubleLayout = longDoubleLayout;
            BoolLayout = boolLayout;
            Endianness = endianness;
            OperatingSystem = operatingSystem;
            CharSignedness = charSignedness;
        }


        /// <summary>Creates a target description with the supplied feature set</summary>
        /// <remarks>The supplied set replaces optional features while the constructor restores mandatory baselines</remarks>
        public TargetInfo WithFeatures(TargetArchitectureFeatures features)
            => new TargetInfo(
                PointerSize,
                PointerAlignment,
                RegisterSize,
                RegisterAlignment,
                CharLayout,
                ShortLayout,
                IntLayout,
                LongLayout,
                LongLongLayout,
                FloatLayout,
                DoubleLayout,
                LongDoubleLayout,
                BoolLayout,
                Endianness,
                CharSignedness,
                Architecture,
                OperatingSystem,
                features);

        private static TargetInfo CreateRegisterBytecode(int pointerSize)
            => new TargetInfo(
                pointerSize: pointerSize,
                pointerAlignment: 4,
                registerSize: 8,
                registerAlignment: 8,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(8, 8),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(16, 16),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: CharSignedness.ImplementationDefined,
                architecture: TargetArchitectureKind.RegisterBytecode);

        private static TargetInfo CreateILP32(TargetArchitectureKind architecture, TargetArchitectureFeatures features, OperatingSystemKind operatingSystem)
            => new TargetInfo(
                pointerSize: 4,
                pointerAlignment: 4,
                registerSize: 4,
                registerAlignment: 4,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(4, 4),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(16, 16),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: CharSignedness.ImplementationDefined,
                architecture: architecture,
                operatingSystem: operatingSystem,
                features: features);

        private static TargetInfo CreateLP64(TargetArchitectureKind architecture, TargetArchitectureFeatures features, OperatingSystemKind operatingSystem)
            => new TargetInfo(
                pointerSize: 8,
                pointerAlignment: 8,
                registerSize: 8,
                registerAlignment: 8,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(8, 8),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(16, 16),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: CharSignedness.ImplementationDefined,
                architecture: architecture,
                operatingSystem: operatingSystem,
                features: features);

        private static TargetInfo CreateArm32(TargetArchitectureFeatures features, OperatingSystemKind operatingSystem)
            => new TargetInfo(
                pointerSize: 4,
                pointerAlignment: 4,
                registerSize: 4,
                registerAlignment: 4,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(4, 4),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(8, 8),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: operatingSystem == OperatingSystemKind.Windows ? CharSignedness.Signed : CharSignedness.Unsigned,
                architecture: TargetArchitectureKind.Arm32,
                operatingSystem: operatingSystem,
                features: features);

        private static TargetInfo CreateArm64(TargetArchitectureFeatures features, OperatingSystemKind operatingSystem)
            => new TargetInfo(
                pointerSize: 8,
                pointerAlignment: 8,
                registerSize: 8,
                registerAlignment: 8,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(8, 8),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: operatingSystem == OperatingSystemKind.MacOs ? new PrimitiveLayout(8, 8) : new PrimitiveLayout(16, 16),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: operatingSystem is OperatingSystemKind.Windows or OperatingSystemKind.MacOs ? CharSignedness.Signed : CharSignedness.Unsigned,
                architecture: TargetArchitectureKind.Arm64,
                operatingSystem: operatingSystem,
                features: features);

        private static TargetInfo CreateWindowsArm64(TargetArchitectureFeatures features)
            => new TargetInfo(
                pointerSize: 8,
                pointerAlignment: 8,
                registerSize: 8,
                registerAlignment: 8,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(4, 4),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(8, 8),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: CharSignedness.Signed,
                architecture: TargetArchitectureKind.Arm64,
                operatingSystem: OperatingSystemKind.Windows,
                features: features);

        private static TargetInfo CreateWindowsX64(TargetArchitectureFeatures features)
            => new TargetInfo(
                pointerSize: 8,
                pointerAlignment: 8,
                registerSize: 8,
                registerAlignment: 8,
                charLayout: new PrimitiveLayout(1, 1),
                shortLayout: new PrimitiveLayout(2, 2),
                intLayout: new PrimitiveLayout(4, 4),
                longLayout: new PrimitiveLayout(4, 4),
                longLongLayout: new PrimitiveLayout(8, 8),
                floatLayout: new PrimitiveLayout(4, 4),
                doubleLayout: new PrimitiveLayout(8, 8),
                longDoubleLayout: new PrimitiveLayout(8, 8),
                boolLayout: new PrimitiveLayout(1, 1),
                endianness: TargetEndianness.Little,
                charSignedness: CharSignedness.ImplementationDefined,
                architecture: TargetArchitectureKind.X86_64,
                operatingSystem: OperatingSystemKind.Windows,
                 features: features);

        /// <summary>Gets the storage size of a qualified type in bytes</summary>
        public int SizeOf(QualifiedType type)
            => SizeOf(type.Type);

        /// <summary>Gets the required alignment of a qualified type in bytes</summary>
        public int AlignOf(QualifiedType type)
            => AlignOf(type.Type);

        /// <summary>Gets the storage size of a type in bytes</summary>
        /// <remarks>Incomplete arrays, functions, and incomplete tags have size zero</remarks>
        public int SizeOf(CType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            switch (type)
            {
                case BuiltinType builtin:
                    return GetPrimitiveLayout(builtin.BuiltinKind).Size;

                case RVVectorType:
                    return Math.Max(1, TargetRegisterInfo.VectorRegisterSize(this));

                case PointerType:
                    return PointerSize;

                case ArrayType array when array.Length.HasValue:
                    return checked(SizeOf(array.ElementType) * (int)array.Length.Value);

                case ArrayType:
                    return 0;

                case EnumType:
                    return IntLayout.Size;

                case FunctionType:
                    return 0;

                case TagType tag:
                    return SizeOfTag(tag.Symbol);

                case CErrorType:
                    return 0;

                default:
                    return 0;
            }
        }

        /// <summary>Gets the required alignment of a type in bytes</summary>
        /// <remarks>Error and function types use alignment one to keep recovery paths total</remarks>
        public int AlignOf(CType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            switch (type)
            {
                case BuiltinType builtin:
                    return GetPrimitiveLayout(builtin.BuiltinKind).Alignment;

                case RVVectorType:
                    return Math.Max(1, TargetRegisterInfo.VectorRegisterSize(this));

                case PointerType:
                    return PointerAlignment;

                case ArrayType array:
                    return AlignOf(array.ElementType);

                case EnumType:
                    return IntLayout.Alignment;

                case FunctionType:
                    return 1;

                case TagType tag:
                    return AlignOfTag(tag.Symbol);

                case CErrorType:
                    return 1;

                default:
                    return 1;
            }
        }

        private int SizeOfTag(TagSymbol symbol)
        {
            if (symbol is null || !symbol.IsComplete)
                return 0;

            if (symbol.TagKind == TagKind.Union)
            {
                var unionSize = 0;
                var unionAlignment = 1;

                foreach (var field in symbol.Fields)
                {
                    unionSize = Math.Max(unionSize, SizeOf(field.Type));
                    unionAlignment = Math.Max(unionAlignment, AlignOf(field.Type));
                }

                return AlignTo(unionSize, unionAlignment);
            }

            var offset = 0;
            var structAlignment = 1;

            foreach (var field in symbol.Fields)
            {
                var fieldAlignment = AlignOf(field.Type);
                offset = AlignTo(offset, fieldAlignment);
                offset += SizeOf(field.Type);
                structAlignment = Math.Max(structAlignment, fieldAlignment);
            }

            return AlignTo(offset, structAlignment);
        }

        private int AlignOfTag(TagSymbol symbol)
        {
            if (symbol is null || !symbol.IsComplete)
                return 1;

            var alignment = 1;
            foreach (var field in symbol.Fields)
                alignment = Math.Max(alignment, AlignOf(field.Type));

            return alignment;
        }

        private static int AlignTo(int value, int alignment)
        {
            if (alignment <= 1)
                return value;

            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private PrimitiveLayout GetPrimitiveLayout(BuiltinTypeKind kind)
        {
            switch (kind)
            {
                case BuiltinTypeKind.Void:
                    return new PrimitiveLayout(1, 1);
                case BuiltinTypeKind.Bool:
                    return BoolLayout;
                case BuiltinTypeKind.Char:
                case BuiltinTypeKind.SignedChar:
                case BuiltinTypeKind.UnsignedChar:
                    return CharLayout;
                case BuiltinTypeKind.Short:
                case BuiltinTypeKind.UnsignedShort:
                    return ShortLayout;
                case BuiltinTypeKind.Int:
                case BuiltinTypeKind.UnsignedInt:
                    return IntLayout;
                case BuiltinTypeKind.Long:
                case BuiltinTypeKind.UnsignedLong:
                    return LongLayout;
                case BuiltinTypeKind.LongLong:
                case BuiltinTypeKind.UnsignedLongLong:
                    return LongLongLayout;
                case BuiltinTypeKind.Float:
                    return FloatLayout;
                case BuiltinTypeKind.Double:
                    return DoubleLayout;
                case BuiltinTypeKind.LongDouble:
                    return LongDoubleLayout;
                default:
                    return IntLayout;
            }
        }
    }

    /// <summary>Identifies qualifiers applied to a type occurrence</summary>
    [Flags]
    public enum TypeQualifiers : byte
    {
        None = 0,
        Const = 1,
        Volatile = 2,
        Restrict = 4,
        Atomic = 8
    }

    /// <summary>Identifies the semantic category of a type</summary>
    public enum TypeKind : byte
    {
        Error,
        Builtin,
        Pointer,
        Array,
        Function,
        Struct,
        Union,
        Enum,
        Vector
    }

    /// <summary>Identifies a built-in scalar or void type</summary>
    public enum BuiltinTypeKind : byte
    {
        Void,
        Bool,
        Char,
        SignedChar,
        UnsignedChar,
        Short,
        UnsignedShort,
        Int,
        UnsignedInt,
        Long,
        UnsignedLong,
        LongLong,
        UnsignedLongLong,
        Float,
        Double,
        LongDouble
    }

    /// <summary>Pairs a semantic type with qualifiers applied at this occurrence</summary>
    public readonly struct QualifiedType
    {
        public CType Type { get; }
        public TypeQualifiers Qualifiers { get; }

        public QualifiedType(CType type, TypeQualifiers qualifiers = TypeQualifiers.None)
        {
            Type = type ?? CErrorType.Instance;
            Qualifiers = qualifiers;
        }

        /// <summary>Gets whether the type represents semantic recovery</summary>
        public bool IsError => Type is CErrorType;

        /// <summary>Formats the type and its qualifiers for diagnostics</summary>
        public string ToDisplayString()
        {
            if (Qualifiers == TypeQualifiers.None)
                return Type.ToDisplayString();

            return Type.ToDisplayString() + " " + Qualifiers.ToString().ToLowerInvariant();
        }

        public override string ToString()
            => ToDisplayString();
    }

    /// <summary>Base class for semantic types</summary>
    public abstract class CType
    {
        public abstract TypeKind Kind { get; }
        public abstract string ToDisplayString();

        public override string ToString()
            => ToDisplayString();
    }

    /// <summary>Represents an invalid or unresolved type while preserving tree shape</summary>
    public sealed class CErrorType : CType
    {
        public static CErrorType Instance { get; } = new CErrorType();

        private CErrorType()
        {
        }

        public override TypeKind Kind => TypeKind.Error;

        public override string ToDisplayString()
            => "<error-type>";
    }

    /// <summary>Represents a built-in scalar or void type</summary>
    public sealed class BuiltinType : CType
    {
        public BuiltinTypeKind BuiltinKind { get; }

        public override TypeKind Kind => TypeKind.Builtin;

        public BuiltinType(BuiltinTypeKind kind)
        {
            BuiltinKind = kind;
        }

        public override string ToDisplayString()
        {
            switch (BuiltinKind)
            {
                case BuiltinTypeKind.Void:
                    return "void";
                case BuiltinTypeKind.Bool:
                    return "_Bool";
                case BuiltinTypeKind.Char:
                    return "char";
                case BuiltinTypeKind.SignedChar:
                    return "signed char";
                case BuiltinTypeKind.UnsignedChar:
                    return "unsigned char";
                case BuiltinTypeKind.Short:
                    return "short";
                case BuiltinTypeKind.UnsignedShort:
                    return "unsigned short";
                case BuiltinTypeKind.Int:
                    return "int";
                case BuiltinTypeKind.UnsignedInt:
                    return "unsigned int";
                case BuiltinTypeKind.Long:
                    return "long";
                case BuiltinTypeKind.UnsignedLong:
                    return "unsigned long";
                case BuiltinTypeKind.LongLong:
                    return "long long";
                case BuiltinTypeKind.UnsignedLongLong:
                    return "unsigned long long";
                case BuiltinTypeKind.Float:
                    return "float";
                case BuiltinTypeKind.Double:
                    return "double";
                case BuiltinTypeKind.LongDouble:
                    return "long double";
                default:
                    return "<builtin>";
            }
        }
    }

    /// <summary>Identifies a supported scalable vector built-in type</summary>
    public enum RVVectorTypeKind : byte
    {
        Bool64,
        Bool32,
        Bool16,
        Bool8,
        Int8M1,
        UInt8M1,
        Int16M1,
        UInt16M1,
        Int32M1,
        UInt32M1,
        Int64M1,
        UInt64M1,
        Float32M1,
        Float64M1
    }

    /// <summary>Describes a scalable vector built-in and its element properties</summary>
    public sealed class RVVectorType : CType
    {
        public RVVectorTypeKind VectorKind { get; }
        public int ElementWidth { get; }
        public bool IsMask { get; }
        public bool IsFloating { get; }
        public bool IsUnsigned { get; }
        public string BuiltinName { get; }

        public override TypeKind Kind => TypeKind.Vector;

        public RVVectorType(RVVectorTypeKind kind)
        {
            VectorKind = kind;
            (ElementWidth, IsMask, IsFloating, IsUnsigned, BuiltinName) = kind switch
            {
                RVVectorTypeKind.Bool64 => (1, true, false, false, "__rvv_bool64_t"),
                RVVectorTypeKind.Bool32 => (1, true, false, false, "__rvv_bool32_t"),
                RVVectorTypeKind.Bool16 => (1, true, false, false, "__rvv_bool16_t"),
                RVVectorTypeKind.Bool8 => (1, true, false, false, "__rvv_bool8_t"),
                RVVectorTypeKind.Int8M1 => (8, false, false, false, "__rvv_int8m1_t"),
                RVVectorTypeKind.UInt8M1 => (8, false, false, true, "__rvv_uint8m1_t"),
                RVVectorTypeKind.Int16M1 => (16, false, false, false, "__rvv_int16m1_t"),
                RVVectorTypeKind.UInt16M1 => (16, false, false, true, "__rvv_uint16m1_t"),
                RVVectorTypeKind.Int32M1 => (32, false, false, false, "__rvv_int32m1_t"),
                RVVectorTypeKind.UInt32M1 => (32, false, false, true, "__rvv_uint32m1_t"),
                RVVectorTypeKind.Int64M1 => (64, false, false, false, "__rvv_int64m1_t"),
                RVVectorTypeKind.UInt64M1 => (64, false, false, true, "__rvv_uint64m1_t"),
                RVVectorTypeKind.Float32M1 => (32, false, true, false, "__rvv_float32m1_t"),
                RVVectorTypeKind.Float64M1 => (64, false, true, false, "__rvv_float64m1_t"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        public override string ToDisplayString()
            => BuiltinName;

        /// <summary>Maps a vector built-in name to its semantic kind</summary>
        public static bool TryParseBuiltinName(string name, out RVVectorTypeKind kind)
        {
            kind = name switch
            {
                "__rvv_bool64_t" => RVVectorTypeKind.Bool64,
                "__rvv_bool32_t" => RVVectorTypeKind.Bool32,
                "__rvv_bool16_t" => RVVectorTypeKind.Bool16,
                "__rvv_bool8_t" => RVVectorTypeKind.Bool8,
                "__rvv_int8m1_t" => RVVectorTypeKind.Int8M1,
                "__rvv_uint8m1_t" => RVVectorTypeKind.UInt8M1,
                "__rvv_int16m1_t" => RVVectorTypeKind.Int16M1,
                "__rvv_uint16m1_t" => RVVectorTypeKind.UInt16M1,
                "__rvv_int32m1_t" => RVVectorTypeKind.Int32M1,
                "__rvv_uint32m1_t" => RVVectorTypeKind.UInt32M1,
                "__rvv_int64m1_t" => RVVectorTypeKind.Int64M1,
                "__rvv_uint64m1_t" => RVVectorTypeKind.UInt64M1,
                "__rvv_float32m1_t" => RVVectorTypeKind.Float32M1,
                "__rvv_float64m1_t" => RVVectorTypeKind.Float64M1,
                _ => default,
            };

            return name is "__rvv_bool64_t" or "__rvv_bool32_t" or "__rvv_bool16_t" or "__rvv_bool8_t"
                or "__rvv_int8m1_t" or "__rvv_uint8m1_t" or "__rvv_int16m1_t" or "__rvv_uint16m1_t"
                or "__rvv_int32m1_t" or "__rvv_uint32m1_t" or "__rvv_int64m1_t" or "__rvv_uint64m1_t"
                or "__rvv_float32m1_t" or "__rvv_float64m1_t";
        }
    }

    /// <summary>Represents a pointer to a qualified type</summary>
    public sealed class PointerType : CType
    {
        public QualifiedType PointeeType { get; }

        public PointerType(QualifiedType pointeeType)
        {
            PointeeType = pointeeType;
        }

        public override TypeKind Kind => TypeKind.Pointer;

        public override string ToDisplayString()
            => PointeeType.ToDisplayString() + "*";
    }

    /// <summary>Represents an array with an optional constant length</summary>
    /// <remarks>A null length denotes an incomplete array type</remarks>
    public sealed class ArrayType : CType
    {
        public QualifiedType ElementType { get; }
        public long? Length { get; }

        public ArrayType(QualifiedType elementType, long? length)
        {
            ElementType = elementType;
            Length = length;
        }

        public override TypeKind Kind => TypeKind.Array;

        public override string ToDisplayString()
            => ElementType.ToDisplayString() + "[" + (Length.HasValue ? Length.Value.ToString() : string.Empty) + "]";
    }

    /// <summary>Represents a function signature</summary>
    /// <remarks>Parameter types are meaningful only when a prototype is present</remarks>
    public sealed class FunctionType : CType
    {
        public QualifiedType ReturnType { get; }
        public ImmutableArray<ParameterSymbol> Parameters { get; }
        public bool HasPrototype { get; }
        public bool IsVariadic { get; }

        public FunctionType(
            QualifiedType returnType,
            ImmutableArray<ParameterSymbol> parameters,
            bool hasPrototype,
            bool isVariadic)
        {
            ReturnType = returnType;
            Parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
            HasPrototype = hasPrototype;
            IsVariadic = isVariadic;
        }

        public override TypeKind Kind => TypeKind.Function;

        public override string ToDisplayString()
        {
            var parameters = HasPrototype
                ? string.Join(", ", Parameters.Select(static p => p.Type.ToDisplayString()))
                : string.Empty;

            if (IsVariadic)
                parameters = parameters.Length == 0 ? "..." : parameters + ", ...";

            return ReturnType.ToDisplayString() + " (" + parameters + ")";
        }
    }

    /// <summary>Identifies the declaration category of a tag symbol</summary>
    public enum TagKind : byte { Struct, Union, Enum }

    /// <summary>Represents a struct, union, or enum through its tag symbol</summary>
    public sealed class TagType : CType
    {
        public TagSymbol Symbol { get; }

        public TagType(TagSymbol symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public override TypeKind Kind
        {
            get
            {
                switch (Symbol.TagKind)
                {
                    case TagKind.Struct:
                        return TypeKind.Struct;
                    case TagKind.Union:
                        return TypeKind.Union;
                    default:
                        return TypeKind.Enum;
                }
            }
        }

        public override string ToDisplayString()
            => Symbol.TagKind.ToString().ToLowerInvariant() + " " + Symbol.Name;
    }

    /// <summary>Represents an enum through its tag symbol</summary>
    public sealed class EnumType : CType
    {
        public TagSymbol Symbol { get; }

        public EnumType(TagSymbol symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public override TypeKind Kind => TypeKind.Enum;

        public override string ToDisplayString()
            => "enum " + Symbol.Name;
    }

    /// <summary>Provides canonical built-in types and factories for composite types</summary>
    public sealed class TypeCatalog
    {
        public static TypeCatalog Instance { get; } = new TypeCatalog();

        public BuiltinType Void { get; } = new BuiltinType(BuiltinTypeKind.Void);
        public BuiltinType Bool { get; } = new BuiltinType(BuiltinTypeKind.Bool);
        public BuiltinType Char { get; } = new BuiltinType(BuiltinTypeKind.Char);
        public BuiltinType SignedChar { get; } = new BuiltinType(BuiltinTypeKind.SignedChar);
        public BuiltinType UnsignedChar { get; } = new BuiltinType(BuiltinTypeKind.UnsignedChar);
        public BuiltinType Short { get; } = new BuiltinType(BuiltinTypeKind.Short);
        public BuiltinType UnsignedShort { get; } = new BuiltinType(BuiltinTypeKind.UnsignedShort);
        public BuiltinType Int { get; } = new BuiltinType(BuiltinTypeKind.Int);
        public BuiltinType UnsignedInt { get; } = new BuiltinType(BuiltinTypeKind.UnsignedInt);
        public BuiltinType Long { get; } = new BuiltinType(BuiltinTypeKind.Long);
        public BuiltinType UnsignedLong { get; } = new BuiltinType(BuiltinTypeKind.UnsignedLong);
        public BuiltinType LongLong { get; } = new BuiltinType(BuiltinTypeKind.LongLong);
        public BuiltinType UnsignedLongLong { get; } = new BuiltinType(BuiltinTypeKind.UnsignedLongLong);
        public BuiltinType Float { get; } = new BuiltinType(BuiltinTypeKind.Float);
        public BuiltinType Double { get; } = new BuiltinType(BuiltinTypeKind.Double);
        public BuiltinType LongDouble { get; } = new BuiltinType(BuiltinTypeKind.LongDouble);

        public RVVectorType RiscVBool64 { get; } = new RVVectorType(RVVectorTypeKind.Bool64);
        public RVVectorType RiscVBool32 { get; } = new RVVectorType(RVVectorTypeKind.Bool32);
        public RVVectorType RiscVBool16 { get; } = new RVVectorType(RVVectorTypeKind.Bool16);
        public RVVectorType RiscVBool8 { get; } = new RVVectorType(RVVectorTypeKind.Bool8);
        public RVVectorType RiscVInt8M1 { get; } = new RVVectorType(RVVectorTypeKind.Int8M1);
        public RVVectorType RiscVUInt8M1 { get; } = new RVVectorType(RVVectorTypeKind.UInt8M1);
        public RVVectorType RiscVInt16M1 { get; } = new RVVectorType(RVVectorTypeKind.Int16M1);
        public RVVectorType RiscVUInt16M1 { get; } = new RVVectorType(RVVectorTypeKind.UInt16M1);
        public RVVectorType RiscVInt32M1 { get; } = new RVVectorType(RVVectorTypeKind.Int32M1);
        public RVVectorType RiscVUInt32M1 { get; } = new RVVectorType(RVVectorTypeKind.UInt32M1);
        public RVVectorType RiscVInt64M1 { get; } = new RVVectorType(RVVectorTypeKind.Int64M1);
        public RVVectorType RiscVUInt64M1 { get; } = new RVVectorType(RVVectorTypeKind.UInt64M1);
        public RVVectorType RiscVFloat32M1 { get; } = new RVVectorType(RVVectorTypeKind.Float32M1);
        public RVVectorType RiscVFloat64M1 { get; } = new RVVectorType(RVVectorTypeKind.Float64M1);

        private TypeCatalog() { }

        /// <summary>Creates a pointer type</summary>
        public PointerType PointerTo(QualifiedType pointee)
            => new PointerType(pointee);

        /// <summary>Creates a complete or incomplete array type</summary>
        public ArrayType ArrayOf(QualifiedType elementType, long? length)
            => new ArrayType(elementType, length);

        /// <summary>Creates a function type</summary>
        public FunctionType FunctionReturning(
            QualifiedType returnType,
            ImmutableArray<ParameterSymbol> parameters,
            bool hasPrototype,
            bool isVariadic)
        {
            return new FunctionType(returnType, parameters, hasPrototype, isVariadic);
        }

        /// <summary>Gets a qualified canonical built-in type</summary>
        public QualifiedType Builtin(BuiltinTypeKind kind, TypeQualifiers qualifiers = TypeQualifiers.None)
        {
            switch (kind)
            {
                case BuiltinTypeKind.Void:
                    return new QualifiedType(Void, qualifiers);
                case BuiltinTypeKind.Bool:
                    return new QualifiedType(Bool, qualifiers);
                case BuiltinTypeKind.Char:
                    return new QualifiedType(Char, qualifiers);
                case BuiltinTypeKind.SignedChar:
                    return new QualifiedType(SignedChar, qualifiers);
                case BuiltinTypeKind.UnsignedChar:
                    return new QualifiedType(UnsignedChar, qualifiers);
                case BuiltinTypeKind.Short:
                    return new QualifiedType(Short, qualifiers);
                case BuiltinTypeKind.UnsignedShort:
                    return new QualifiedType(UnsignedShort, qualifiers);
                case BuiltinTypeKind.Int:
                    return new QualifiedType(Int, qualifiers);
                case BuiltinTypeKind.UnsignedInt:
                    return new QualifiedType(UnsignedInt, qualifiers);
                case BuiltinTypeKind.Long:
                    return new QualifiedType(Long, qualifiers);
                case BuiltinTypeKind.UnsignedLong:
                    return new QualifiedType(UnsignedLong, qualifiers);
                case BuiltinTypeKind.LongLong:
                    return new QualifiedType(LongLong, qualifiers);
                case BuiltinTypeKind.UnsignedLongLong:
                    return new QualifiedType(UnsignedLongLong, qualifiers);
                case BuiltinTypeKind.Float:
                    return new QualifiedType(Float, qualifiers);
                case BuiltinTypeKind.Double:
                    return new QualifiedType(Double, qualifiers);
                case BuiltinTypeKind.LongDouble:
                    return new QualifiedType(LongDouble, qualifiers);
                default:
                    return new QualifiedType(CErrorType.Instance, qualifiers);
            }
        }

        /// <summary>Gets a qualified canonical vector built-in type</summary>
        public QualifiedType RiscVVector(RVVectorTypeKind kind, TypeQualifiers qualifiers = TypeQualifiers.None)
        {
            var type = kind switch
            {
                RVVectorTypeKind.Bool64 => RiscVBool64,
                RVVectorTypeKind.Bool32 => RiscVBool32,
                RVVectorTypeKind.Bool16 => RiscVBool16,
                RVVectorTypeKind.Bool8 => RiscVBool8,
                RVVectorTypeKind.Int8M1 => RiscVInt8M1,
                RVVectorTypeKind.UInt8M1 => RiscVUInt8M1,
                RVVectorTypeKind.Int16M1 => RiscVInt16M1,
                RVVectorTypeKind.UInt16M1 => RiscVUInt16M1,
                RVVectorTypeKind.Int32M1 => RiscVInt32M1,
                RVVectorTypeKind.UInt32M1 => RiscVUInt32M1,
                RVVectorTypeKind.Int64M1 => RiscVInt64M1,
                RVVectorTypeKind.UInt64M1 => RiscVUInt64M1,
                RVVectorTypeKind.Float32M1 => RiscVFloat32M1,
                RVVectorTypeKind.Float64M1 => RiscVFloat64M1,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return new QualifiedType(type, qualifiers);
        }
    }


    /// <summary>Stores ordinary identifiers, tags, and labels for one lexical scope</summary>
    public sealed class Scope
    {
        private readonly Dictionary<string, Symbol> _ordinarySymbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TagSymbol> _tagSymbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LabelSymbol> _labelSymbols = new(StringComparer.Ordinal);

        public Scope? Parent { get; }
        public SyntaxNode? DeclaringSyntax { get; }

        public Scope(Scope? parent, SyntaxNode? declaringSyntax)
        {
            Parent = parent;
            DeclaringSyntax = declaringSyntax;
        }

        public IEnumerable<Symbol> OrdinarySymbols => _ordinarySymbols.Values;
        public IEnumerable<TagSymbol> Tags => _tagSymbols.Values;
        public IEnumerable<LabelSymbol> Labels => _labelSymbols.Values;

        /// <summary>Adds an ordinary symbol when the current scope has no matching name</summary>
        public bool TryDeclareOrdinary(Symbol symbol, out Symbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_ordinarySymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _ordinarySymbols.Add(symbol.Name, symbol);
            return true;
        }

        /// <summary>Replaces the ordinary symbol associated with its name in this scope</summary>
        public void ReplaceOrdinary(Symbol symbol)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            _ordinarySymbols[symbol.Name] = symbol;
        }

        /// <summary>Adds a tag when the current scope has no matching name</summary>
        public bool TryDeclareTag(TagSymbol symbol, out TagSymbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_tagSymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _tagSymbols.Add(symbol.Name, symbol);
            return true;
        }

        /// <summary>Adds a label when the current scope has no matching name</summary>
        public bool TryDeclareLabel(LabelSymbol symbol, out LabelSymbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_labelSymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _labelSymbols.Add(symbol.Name, symbol);
            return true;
        }

        /// <summary>Looks up an ordinary identifier through the parent chain</summary>
        public Symbol? LookupOrdinary(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._ordinarySymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }

        /// <summary>Looks up a tag through the parent chain</summary>
        public TagSymbol? LookupTag(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._tagSymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }

        /// <summary>Looks up a label through the parent chain</summary>
        public LabelSymbol? LookupLabel(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._labelSymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }
    }



}
