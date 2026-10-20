using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using IR = Cnidaria.C;

namespace Cnidaria.Rust;

public sealed class CompilationOptions
{
    public string CrateName { get; init; } = "crate";
    public int PointerWidth { get; init; } = 64;
    public bool IsLibrary { get; init; }
}

public readonly record struct SemanticDiagnostic(string Code, string Message, SyntaxTree SyntaxTree, TextSpan Position) : IDiagnostic
{
    public DiagnosticSeverity Severity => DiagnosticSeverity.Error;
    public override string ToString() => $"{SyntaxTree.FilePath}{Position}: {Code}: {Message}";
    public string GetMessage(string source) => $"{Message} {Position.ToString(source)}";
}

public readonly record struct SymbolInfo(Symbol? Symbol, ImmutableArray<Symbol> CandidateSymbols);
public readonly record struct TypeInfo(TypeSymbol? Type, TypeSymbol? ConvertedType);
public readonly record struct Conversion(ConversionKind Kind)
{
    public bool Exists => Kind != ConversionKind.None;
    public bool IsIdentity => Kind == ConversionKind.Identity;
}

///<summary>Owns a crate's declarations, type identities and semantic models</summary>
public sealed class Compilation
{
    private readonly Lazy<Binder> _binding;
    private readonly ImmutableDictionary<SyntaxTree, SemanticModel> _models;
    internal ImmutableDictionary<string, SyntaxTree> ModuleSources { get; }
    public SyntaxTree RootSyntaxTree { get; }
    public ImmutableArray<SyntaxTree> SyntaxTrees { get; }
    public CompilationOptions Options { get; }
    public ModuleSymbol Crate => Binding.Crate;
    internal Binder Binding => _binding.Value;

    public Compilation(SyntaxTree root, CompilationOptions? options = null, IReadOnlyDictionary<string, SyntaxTree>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        Options = options ?? new CompilationOptions();
        if (Options.PointerWidth is not (32 or 64)) throw new ArgumentOutOfRangeException(nameof(options), "Pointer width must be 32 or 64.");
        if (string.IsNullOrWhiteSpace(Options.CrateName)) throw new ArgumentException("Crate name must not be empty.", nameof(options));
        RootSyntaxTree = root;
        ModuleSources = modules?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, SyntaxTree>.Empty;
        if (ModuleSources.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null))
            throw new ArgumentException("Module paths and syntax trees must be nonempty.", nameof(modules));
        SyntaxTrees = new[] { root }.Concat(ModuleSources.Values).Distinct().ToImmutableArray();
        if (SyntaxTrees.Length != ModuleSources.Count + 1)
            throw new ArgumentException("Each module must have its own syntax tree.", nameof(modules));
        _models = SyntaxTrees.ToImmutableDictionary(t => t, t => new SemanticModel(this, t));
        _binding = new Lazy<Binder>(() =>
        {
            var binder = new Binder(this);
            binder.Bind();
            return binder;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public static Compilation Create(SyntaxTree root, CompilationOptions? options = null, IReadOnlyDictionary<string, SyntaxTree>? modules = null)
        => new(root, options, modules);
    public SemanticModel GetSemanticModel(SyntaxTree tree) => _models.TryGetValue(tree, out SemanticModel? model)
        ? model : throw new ArgumentException("Syntax tree does not belong to this compilation.", nameof(tree));
    public ImmutableArray<IDiagnostic> GetDiagnostics()
    {
        _ = Binding;
        return SyntaxTrees.SelectMany(t => GetSemanticModel(t).GetDiagnostics()).ToImmutableArray();
    }

    public (BoundCompilationUnit? Tree, ImmutableArray<IDiagnostic> Diagnostics) GetLoweredTree()
    {
        ImmutableArray<IDiagnostic> diagnostics = GetDiagnostics();
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return (null, diagnostics);
        try { return (Lowerer.Lower(this), diagnostics); }
        catch (LoweringException exception) { return (null, diagnostics.Add(LoweringDiagnostic(exception))); }
    }

    public (IR.GimpleTree? Tree, ImmutableArray<IDiagnostic> Diagnostics) GetGimpleTree(IR.CompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (tree, diagnostics) = GetLoweredTree();
        if (tree is null) return (null, diagnostics);
        try { return (Gimplifier.Lower(this, tree, options), diagnostics); }
        catch (LoweringException exception) { return (null, diagnostics.Add(LoweringDiagnostic(exception))); }
    }

    public (byte[]? Image, ImmutableArray<IDiagnostic> Diagnostics) Emit(IR.StaticLinkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (tree, diagnostics) = GetGimpleTree(options.CompilationOptions);
        if (tree is null) return (null, diagnostics);
        string path = string.IsNullOrWhiteSpace(RootSyntaxTree.FilePath) ? Options.CrateName + ".rs" : RootSyntaxTree.FilePath;
        var inputs = new[] { new IR.GimpleTranslationUnit(path, tree) };
        IR.TargetInfo target = options.CompilationOptions.Target;
        byte[]? image;
        ImmutableArray<IR.LinkerDiagnostic> linking;
        if (target.IsX86)
        {
            var result = IR.StaticLinker.LinkX86(inputs, options);
            image = result.Program?.ToExecutableBytes(); linking = result.Diagnostics;
        }
        else if (target.IsRiscV)
        {
            var result = IR.StaticLinker.LinkRiscV(inputs, options);
            image = result.Program?.ToExecutableBytes(); linking = result.Diagnostics;
        }
        else if (target.IsArm)
        {
            var result = IR.StaticLinker.LinkArm(inputs, options);
            image = result.Program?.ToExecutableBytes(); linking = result.Diagnostics;
        }
        else if (target.IsRegisterBytecode)
        {
            var result = IR.StaticLinker.LinkRegisterBytecode(inputs, options);
            image = result.Program?.Image.ToExecutableBytes(); linking = result.Diagnostics;
        }
        else throw new ArgumentException("No code generator is available for the selected target.", nameof(options));
        return (image, diagnostics.AddRange(linking.Select(d => (IDiagnostic)new CodeGenerationDiagnostic(d))));
    }

    private SemanticDiagnostic LoweringDiagnostic(LoweringException exception)
    {
        SyntaxTree tree = SyntaxTrees.First(t => t.Root == exception.Syntax || t.Root.DescendantNodes().Contains(exception.Syntax));
        return new SemanticDiagnostic("RLOW0001", exception.Message, tree, exception.Syntax.Span);
    }

    private readonly record struct CodeGenerationDiagnostic(IR.LinkerDiagnostic Diagnostic) : IDiagnostic
    {
        public string Message => Diagnostic.Message;
        public DiagnosticSeverity Severity => Diagnostic.Severity;
        public TextSpan Position => Diagnostic.Position;
        public string GetMessage(string source) => Diagnostic.ToString();
        public override string ToString() => Diagnostic.ToString();
    }
}

///<summary>Provides bound nodes, symbols, types and diagnostics for one source tree</summary>
public sealed class SemanticModel
{
    private readonly HashSet<SyntaxNode> _nodes;
    internal readonly SemanticData Data = new();
    public Compilation Compilation { get; }
    public SyntaxTree SyntaxTree { get; }
    internal SemanticModel(Compilation compilation, SyntaxTree tree)
    {
        Compilation = compilation; SyntaxTree = tree;
        _nodes = tree.Root.DescendantNodes().Prepend(tree.Root).ToHashSet();
        foreach (SyntaxNode parent in _nodes)
            foreach (SyntaxNode child in parent.ChildNodes()) Data.Parents.Add(child, parent);
    }
    private void Check(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodes.Contains(node)) throw new ArgumentException("Syntax node does not belong to this semantic model.", nameof(node));
        _ = Compilation.Binding;
    }
    public ImmutableArray<IDiagnostic> GetDiagnostics(TextSpan? span = null)
    {
        _ = Compilation.Binding;
        return SyntaxTree.Diagnostics.Cast<IDiagnostic>().Concat(Data.Diagnostics.Cast<IDiagnostic>())
            .Where(d => span is null || d.Position.Start < span.Value.End && d.Position.End >= span.Value.Start)
            .OrderBy(d => d.Position.Start).ToImmutableArray();
    }
    public Symbol? GetDeclaredSymbol(SyntaxNode node) { Check(node); return Data.Declarations.GetValueOrDefault(node); }
    public SymbolInfo GetSymbolInfo(SyntaxNode node) { Check(node); return Data.Symbols.GetValueOrDefault(node, new SymbolInfo(null, [])); }
    public TypeInfo GetTypeInfo(SyntaxNode node)
    {
        Check(node);
        TypeInfo info = Data.Types.GetValueOrDefault(node);
        return new TypeInfo(info.Type?.Resolve(), info.ConvertedType?.Resolve());
    }
    public ConstantValue GetConstantValue(SyntaxNode node)
    {
        Check(node);
        return Data.BoundNodes.GetValueOrDefault(node) is BoundExpression expression ? expression.ConstantValue : default;
    }
    public Conversion GetConversion(SyntaxNode node) { Check(node); return Data.Conversions.GetValueOrDefault(node); }
    public BoundNode? GetBoundNode(SyntaxNode node) { Check(node); return Data.BoundNodes.GetValueOrDefault(node); }
    public BoundCompilationUnit GetBoundTree() { Check(SyntaxTree.Root); return (BoundCompilationUnit)Data.BoundNodes[SyntaxTree.Root]; }
    public Symbol? GetEnclosingSymbol(int position) => FindScope(position)?.Owner;
    public ImmutableArray<Symbol> LookupSymbols(int position, string? name = null, SymbolNamespace space = SymbolNamespace.Value)
    {
        BindingScope? scope = FindScope(position);
        var result = ImmutableArray.CreateBuilder<Symbol>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (BindingScope? current = scope; current is not null; current = current.Parent)
        {
            foreach (ScopeEntry entry in current.Entries)
            {
                if (entry.Namespace != space || entry.VisibleFrom > position || name is not null && entry.Name != name || !names.Add(entry.Name)) continue;
                result.AddRange(current.Find(entry.Name, space, position));
            }
        }
        return result.ToImmutable();
    }
    private BindingScope? FindScope(int position)
    {
        if (position < 0 || position > SyntaxTree.Text.Length) throw new ArgumentOutOfRangeException(nameof(position));
        _ = Compilation.Binding;
        return Data.Scopes.Where(s => s.Span.Start <= position && position <= s.Span.End)
            .OrderBy(s => s.Span.Length).ThenByDescending(s => Data.Scopes.IndexOf(s)).FirstOrDefault();
    }
}

internal sealed class SemanticData
{
    internal readonly Dictionary<SyntaxNode, SyntaxNode> Parents = new();
    internal readonly HashSet<SyntaxNode> ErrorNodes = new();
    internal readonly Dictionary<SyntaxNode, Symbol> Declarations = new();
    internal readonly Dictionary<SyntaxNode, SymbolInfo> Symbols = new();
    internal readonly Dictionary<SyntaxNode, TypeInfo> Types = new();
    internal readonly Dictionary<SyntaxNode, Conversion> Conversions = new();
    internal readonly Dictionary<SyntaxNode, BoundNode> BoundNodes = new();
    internal readonly List<SemanticDiagnostic> Diagnostics = new();
    internal readonly List<BindingScope> Scopes = new();
}
