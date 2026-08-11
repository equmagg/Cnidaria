using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Python
{
    [Flags]
    public enum SymbolFlags : uint
    {
        None = 0,
        Global = 1u << 0,
        Local = 1u << 1,
        Parameter = 1u << 2,
        Nonlocal = 1u << 3,
        Used = 1u << 4,
        FreeClass = 1u << 5,
        Imported = 1u << 6,
        Annotated = 1u << 7,
        ComprehensionIterationVariable = 1u << 8,
        TypeParameter = 1u << 9,
        ComprehensionCell = 1u << 10,
    }

    public enum SymbolScope : byte
    {
        Local,
        GlobalExplicit,
        GlobalImplicit,
        Free,
        Cell,
    }

    public enum SymbolTableKind : byte
    {
        Module,
        Function,
        Class,
        Annotation,
        TypeAlias,
        TypeParameters,
        TypeVariable,
    }

    public enum ComprehensionKind : byte
    {
        None,
        List,
        Set,
        Dictionary,
        Generator,
    }

    public enum TypeVariableScopeKind : byte
    {
        None,
        Bound,
        Default,
    }

    public enum SymbolOccurrenceKind : byte
    {
        Definition,
        Reference,
        Declaration,
        Import,
        Annotation,
        Parameter,
        ComprehensionTarget,
    }

    public enum SymbolDiagnosticCode : byte
    {
        DuplicateParameter,
        DuplicateTypeParameter,
        GlobalAfterUse,
        GlobalAfterAssignment,
        GlobalAfterAnnotation,
        GlobalAfterParameter,
        NonlocalAfterUse,
        NonlocalAfterAssignment,
        NonlocalAfterAnnotation,
        NonlocalAfterParameter,
        GlobalAndNonlocal,
        NonlocalAtModuleLevel,
        NonlocalBindingNotFound,
        ImportStarOutsideModule,
        AssignmentToDebug,
        DeleteDebug,
        ReturnOutsideFunction,
        ReturnWithValueInAsyncGenerator,
        YieldOutsideFunction,
        YieldInsideComprehension,
        AwaitOutsideAsyncFunction,
        AsyncComprehensionOutsideAsyncFunction,
        NamedExpressionInComprehensionIterable,
        NamedExpressionRebindsComprehensionVariable,
        NamedExpressionInClassComprehension,
        NamedExpressionInTypeScopeComprehension,
        ExpressionNotAllowedInAnnotation,
        ExpressionNotAllowedInTypeVariable,
        ExpressionNotAllowedInTypeAlias,
        ExpressionNotAllowedInTypeParameters,
        NonlocalTypeParameter,
        ReservedTypeParameterName,
    }

    public sealed class SymbolTableOptions
    {
        public bool AllowTopLevelAwait { get; init; }
        public bool? FutureAnnotations { get; init; }
        public bool InlineComprehensions { get; init; } = true;
    }

    public readonly struct SymbolOccurrence
    {
        public readonly SymbolOccurrenceKind Kind;
        public readonly TextSpan Span;
        public SymbolOccurrence(SymbolOccurrenceKind kind, TextSpan span)
        {
            Kind = kind;
            Span = span;
        }
    }
    public readonly struct SymbolDiagnostic
    {
        public readonly SymbolDiagnosticCode Code;
        public readonly TextSpan Span;
        public readonly string Message;
        public SymbolDiagnostic(SymbolDiagnosticCode code, TextSpan span, string message)
        {
            Code = code;
            Span = span;
            Message = message;
        }
    }

    public sealed class Symbol
    {
        internal Symbol(
            string name,
            SymbolFlags flags,
            SymbolScope scope,
            ImmutableArray<SymbolOccurrence> occurrences,
            bool isModule)
        {
            Name = name;
            Flags = flags;
            Scope = scope;
            Occurrences = occurrences;
            IsModuleSymbol = isModule;
        }

        public string Name { get; }
        public SymbolFlags Flags { get; }
        public SymbolScope Scope { get; }
        public ImmutableArray<SymbolOccurrence> Occurrences { get; }
        internal bool IsModuleSymbol { get; }
        public bool IsReferenced => (Flags & SymbolFlags.Used) != 0;
        public bool IsAssigned => (Flags & SymbolFlags.Local) != 0;
        public bool IsParameter => (Flags & SymbolFlags.Parameter) != 0;
        public bool IsImported => (Flags & SymbolFlags.Imported) != 0;
        public bool IsAnnotated => (Flags & SymbolFlags.Annotated) != 0;
        public bool IsDeclaredGlobal => (Flags & SymbolFlags.Global) != 0;
        public bool IsNonlocal => (Flags & SymbolFlags.Nonlocal) != 0;
        public bool IsTypeParameter => (Flags & SymbolFlags.TypeParameter) != 0;
        public bool IsComprehensionIterationVariable =>
            (Flags & SymbolFlags.ComprehensionIterationVariable) != 0;
        public bool IsLocal => IsModuleSymbol
            ? (Flags & (SymbolFlags.Local | SymbolFlags.Parameter | SymbolFlags.Imported | SymbolFlags.TypeParameter)) != 0
            : Scope is SymbolScope.Local or SymbolScope.Cell;
        public bool IsGlobal =>
            Scope is SymbolScope.GlobalExplicit or SymbolScope.GlobalImplicit ||
            (IsModuleSymbol && Scope == SymbolScope.Local);
        public bool IsFree => Scope == SymbolScope.Free;
        public bool IsCell => Scope == SymbolScope.Cell;
    }

    public sealed class SymbolTable
    {
        private ImmutableArray<SymbolTable> _children = [];
        private ImmutableArray<Symbol> _symbols = [];
        private ImmutableDictionary<string, Symbol> _symbolsByName =
            ImmutableDictionary<string, Symbol>.Empty.WithComparers(StringComparer.Ordinal);
        private ImmutableArray<string> _parameterNames = [];
        private ImmutableArray<string> _localNames = [];
        private ImmutableArray<string> _cellNames = [];
        private ImmutableArray<string> _freeNames = [];
        private ImmutableArray<string> _globalNames = [];
        private ImmutableArray<SymbolDiagnostic> _diagnostics = [];

        internal SymbolTable(
            string name,
            SymbolTableKind kind,
            SymbolTable? parent,
            SyntaxNode declaration)
        {
            Name = name;
            Kind = kind;
            Parent = parent;
            Declaration = declaration;
        }

        public string Name { get; }
        public SymbolTableKind Kind { get; }
        public SymbolTable? Parent { get; }
        public SyntaxNode Declaration { get; }
        public ImmutableArray<SymbolTable> Children => _children;
        public ImmutableArray<Symbol> Symbols => _symbols;
        public ImmutableArray<string> ParameterNames => _parameterNames;
        public ImmutableArray<string> LocalNames => _localNames;
        public ImmutableArray<string> CellNames => _cellNames;
        public ImmutableArray<string> FreeNames => _freeNames;
        public ImmutableArray<string> GlobalNames => _globalNames;
        public ImmutableArray<SymbolDiagnostic> Diagnostics => _diagnostics;

        public bool IsNested { get; internal set; }
        public bool IsGenerator { get; internal set; }
        public bool IsCoroutine { get; internal set; }
        public bool HasVarArgs { get; internal set; }
        public bool HasVarKeywords { get; internal set; }
        public bool ReturnsValue { get; internal set; }
        public bool NeedsClassClosure { get; internal set; }
        public bool NeedsClassDictionary { get; internal set; }
        public bool NeedsConditionalAnnotationsClosure { get; internal set; }
        public bool CanSeeClassScope { get; internal set; }
        public bool HasDocString { get; internal set; }
        public bool HasAnnotations { get; internal set; }
        public bool HasImportStar { get; internal set; }
        public bool HasConditionalAnnotations { get; internal set; }
        public bool FutureAnnotations { get; internal set; }
        public bool IsMethod { get; internal set; }
        public ComprehensionKind ComprehensionKind { get; internal set; }
        public bool IsComprehensionInlined { get; internal set; }
        public TypeVariableScopeKind TypeVariableScopeKind { get; internal set; }

        public IEnumerable<SymbolTable> GetCodeBlockChildren()
        {
            foreach (var child in Children)
            {
                if (child.IsComprehensionInlined)
                {
                    foreach (var nested in child.GetCodeBlockChildren())
                        yield return nested;
                }
                else
                {
                    yield return child;
                }
            }
        }

        public IEnumerable<SymbolTable> DescendantsAndSelf()
        {
            yield return this;
            foreach (var child in Children)
            {
                foreach (var descendant in child.DescendantsAndSelf())
                    yield return descendant;
            }
        }

        public SymbolTable? FindTable(SyntaxNode declaration, SymbolTableKind? kind = null)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            foreach (var table in DescendantsAndSelf())
            {
                if ((kind is null || table.Kind == kind.Value) &&
                    ReferenceEquals(table.Declaration.SyntaxTree, declaration.SyntaxTree) &&
                    ReferenceEquals(table.Declaration.Green, declaration.Green) &&
                    table.Declaration.Position == declaration.Position)
                {
                    return table;
                }
            }
            return null;
        }

        public bool TryLookup(string name, out Symbol symbol) =>
            _symbolsByName.TryGetValue(name, out symbol!);

        public Symbol? Lookup(string name) =>
            _symbolsByName.TryGetValue(name, out var symbol) ? symbol : null;

        internal void Complete(
            ImmutableArray<SymbolTable> children,
            ImmutableArray<Symbol> symbols,
            ImmutableArray<string> parameterNames,
            ImmutableArray<SymbolDiagnostic> diagnostics)
        {
            _children = children;
            _symbols = symbols;
            _parameterNames = parameterNames;
            _diagnostics = diagnostics;
            _symbolsByName = symbols.ToImmutableDictionary(
                static symbol => symbol.Name,
                StringComparer.Ordinal);
            _localNames = symbols
                .Where(static symbol => symbol.IsLocal)
                .Select(static symbol => symbol.Name)
                .ToImmutableArray();
            _cellNames = SelectNames(symbols, SymbolScope.Cell);
            _freeNames = SelectNames(symbols, SymbolScope.Free);
            _globalNames = symbols
                .Where(static symbol => symbol.IsGlobal)
                .Select(static symbol => symbol.Name)
                .ToImmutableArray();
        }

        private static ImmutableArray<string> SelectNames(
            ImmutableArray<Symbol> symbols,
            SymbolScope scope) =>
            symbols
                .Where(symbol => symbol.Scope == scope)
                .Select(static symbol => symbol.Name)
                .ToImmutableArray();
    }

    internal static class SymbolTableBuilder
    {
        public static SymbolTable Build(SyntaxTree syntaxTree, SymbolTableOptions? options)
        {
            ArgumentNullException.ThrowIfNull(syntaxTree);
            options ??= new SymbolTableOptions();

            var builder = new Builder(syntaxTree, options);
            return builder.Build();
        }

        private sealed class Builder
        {
            private const string ClassCellName = "__class__";
            private const string ClassDictionaryCellName = "__classdict__";
            private const string ConditionalAnnotationsCellName = "__conditional_annotations__";

            private readonly SyntaxTree _syntaxTree;
            private readonly SymbolTableOptions _options;
            private readonly List<SymbolDiagnostic> _diagnostics = [];
            private MutableTable _current = null!;
            private bool _futureAnnotations;
            private int _comprehensionIterableDepth;

            public Builder(SyntaxTree syntaxTree, SymbolTableOptions options)
            {
                _syntaxTree = syntaxTree;
                _options = options;
            }

            public SymbolTable Build()
            {
                var rootNode = _syntaxTree.GetRoot();
                _futureAnnotations = _options.FutureAnnotations ?? DetectFutureAnnotations(rootNode);
                _current = new MutableTable(
                    "top",
                    SymbolTableKind.Module,
                    parent: null,
                    rootNode,
                    privateName: null);
                _current.HasDocString = HasDocString(rootNode);

                VisitNode(rootNode);
                Analyze(
                    _current,
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal),
                    classEntry: null);
                return Freeze(_current, parent: null);
            }

            private static bool DetectFutureAnnotations(SyntaxNode root)
            {
                foreach (var statement in EnumerateTopLevelStatements(root))
                {
                    if (statement.Kind != SyntaxKind.FromImportStatement)
                        continue;

                    var module = Node(statement, 1);
                    if (!string.Equals(GetDottedName(module), "__future__", StringComparison.Ordinal))
                        continue;

                    var targets = Node(statement, 3);
                    if (targets is null)
                        continue;

                    foreach (var alias in targets.DescendantNodeChildren(SyntaxKind.ImportAlias))
                    {
                        var importedName = Node(alias, 0) is { } nameNode
                            ? FirstToken(nameNode, SyntaxKind.IdentifierToken)
                            : Token(alias, 0);
                        if (importedName is { Kind: SyntaxKind.IdentifierToken } token &&
                            string.Equals(token.Text, "annotations", StringComparison.Ordinal))
                            return true;
                    }
                }

                return false;
            }

            private static IEnumerable<SyntaxNode> EnumerateTopLevelStatements(SyntaxNode root)
            {
                var list = Node(root, 0);
                if (list is null)
                    yield break;

                foreach (var child in list.ChildNodes())
                {
                    if (child.Kind == SyntaxKind.SimpleStatementList)
                    {
                        foreach (var statement in child.ChildNodes())
                            yield return statement;
                    }
                    else
                    {
                        yield return child;
                    }
                }
            }

            private void VisitNode(SyntaxNode? node)
            {
                if (node is null)
                    return;

                switch (node.Kind)
                {
                    case SyntaxKind.CompilationUnit:
                    case SyntaxKind.SyntaxList:
                    case SyntaxKind.SeparatedSyntaxList:
                    case SyntaxKind.SimpleStatementList:
                    case SyntaxKind.Suite:
                    case SyntaxKind.SkippedTokens:
                    case SyntaxKind.ErrorStatement:
                    case SyntaxKind.ErrorExpression:
                    case SyntaxKind.MissingExpression:
                    case SyntaxKind.ErrorPattern:
                    case SyntaxKind.MissingPattern:
                        VisitChildren(node);
                        break;

                    case SyntaxKind.ExpressionStatement:
                        VisitExpression(Node(node, 0));
                        break;
                    case SyntaxKind.AssignmentStatement:
                        VisitAssignment(node);
                        break;
                    case SyntaxKind.AnnotatedAssignmentStatement:
                        VisitAnnotatedAssignment(node);
                        break;
                    case SyntaxKind.AugmentedAssignmentStatement:
                        VisitAugmentedAssignment(node);
                        break;
                    case SyntaxKind.ReturnStatement:
                        VisitReturn(node);
                        break;
                    case SyntaxKind.YieldStatement:
                        VisitExpression(Node(node, 0));
                        break;
                    case SyntaxKind.RaiseStatement:
                        VisitExpression(Node(node, 1));
                        VisitExpression(Node(node, 3));
                        break;
                    case SyntaxKind.AssertStatement:
                        VisitExpression(Node(node, 1));
                        VisitExpression(Node(node, 3));
                        break;
                    case SyntaxKind.DeleteStatement:
                        VisitTarget(Node(node, 1), TargetContext.Delete);
                        break;
                    case SyntaxKind.GlobalStatement:
                        VisitDirective(node, isGlobal: true);
                        break;
                    case SyntaxKind.NonlocalStatement:
                        VisitDirective(node, isGlobal: false);
                        break;
                    case SyntaxKind.ImportStatement:
                        VisitImport(node);
                        break;
                    case SyntaxKind.FromImportStatement:
                        VisitFromImport(node);
                        break;
                    case SyntaxKind.TypeAliasStatement:
                        VisitTypeAlias(node);
                        break;
                    case SyntaxKind.IfStatement:
                        VisitIf(node);
                        break;
                    case SyntaxKind.ElifClause:
                        VisitExpression(Node(node, 1));
                        VisitNode(Node(node, 3));
                        break;
                    case SyntaxKind.ElseClause:
                        VisitNode(Node(node, 2));
                        break;
                    case SyntaxKind.WhileStatement:
                        VisitExpression(Node(node, 1));
                        VisitConditional(() =>
                        {
                            VisitNode(Node(node, 3));
                            VisitNode(Node(node, 4));
                        });
                        break;
                    case SyntaxKind.ForStatement:
                        VisitFor(node);
                        break;
                    case SyntaxKind.FunctionDefinition:
                        VisitFunction(node);
                        break;
                    case SyntaxKind.ClassDefinition:
                        VisitClass(node);
                        break;
                    case SyntaxKind.WithStatement:
                        VisitWith(node);
                        break;
                    case SyntaxKind.TryStatement:
                        VisitTry(node);
                        break;
                    case SyntaxKind.ExceptClause:
                    case SyntaxKind.ExceptStarClause:
                        VisitExcept(node);
                        break;
                    case SyntaxKind.FinallyClause:
                        VisitNode(Node(node, 2));
                        break;
                    case SyntaxKind.MatchStatement:
                        VisitMatch(node);
                        break;
                    case SyntaxKind.CaseClause:
                        VisitCase(node);
                        break;
                    case SyntaxKind.PassStatement:
                    case SyntaxKind.BreakStatement:
                    case SyntaxKind.ContinueStatement:
                        break;
                    default:
                        if (IsExpression(node.Kind))
                            VisitExpression(node);
                        else if (IsPattern(node.Kind))
                            VisitPattern(node);
                        else
                            VisitChildren(node);
                        break;
                }
            }

            private void VisitChildren(SyntaxNode node)
            {
                foreach (var child in node.ChildNodes())
                    VisitNode(child);
            }

            private void VisitAssignment(SyntaxNode node)
            {
                var directNodes = node.ChildNodes().ToArray();
                if (directNodes.Length == 0)
                    return;

                for (var i = 0; i < directNodes.Length - 1; i++)
                    VisitTarget(directNodes[i], TargetContext.Store);
                VisitExpression(directNodes[^1]);
            }

            private void VisitAnnotatedAssignment(SyntaxNode node)
            {
                var target = Node(node, 0);
                var annotation = Node(node, 2);
                var value = Node(node, 4);
                var unwrappedTarget = UnwrapParenthesizedTarget(target);
                var isSimpleName = target?.Kind == SyntaxKind.NameExpression;

                if (unwrappedTarget?.Kind == SyntaxKind.NameExpression)
                {
                    var token = Token(unwrappedTarget, 0);
                    if (token is { } name)
                    {
                        if (isSimpleName)
                        {
                            var mangled = Mangle(_current, name.Text);
                            if (_current.Kind != SymbolTableKind.Module && _current.TryGetEntry(mangled, out var prior))
                            {
                                if ((prior.Flags & SymbolFlags.Global) != 0)
                                {
                                    AddDiagnostic(
                                        SymbolDiagnosticCode.GlobalAfterAnnotation,
                                        name.Span,
                                        $"annotated name '{name.Text}' cannot be global");
                                }
                                else if ((prior.Flags & SymbolFlags.Nonlocal) != 0)
                                {
                                    AddDiagnostic(
                                        SymbolDiagnosticCode.NonlocalAfterAnnotation,
                                        name.Span,
                                        $"annotated name '{name.Text}' cannot be nonlocal");
                                }
                            }

                            AddDefinition(
                                _current,
                                name,
                                SymbolFlags.Local | SymbolFlags.Annotated,
                                SymbolOccurrenceKind.Annotation);
                        }
                        else if (value is not null)
                        {
                            AddDefinition(_current, name, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                        }
                    }
                }
                else
                {
                    VisitTarget(target, TargetContext.Store);
                }

                VisitAnnotation(annotation, node);
                VisitExpression(value);
            }

            private void VisitAugmentedAssignment(SyntaxNode node)
            {
                var target = Node(node, 0);
                if (target?.Kind == SyntaxKind.NameExpression)
                {
                    var token = Token(target, 0);
                    if (token is { } name)
                        AddDefinition(_current, name, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                }
                else
                {
                    VisitTarget(target, TargetContext.Augmented);
                }

                VisitExpression(Node(node, 2));
            }

            private void VisitReturn(SyntaxNode node)
            {
                var value = Node(node, 1);
                var function = FindEnclosingFunction(_current);
                if (function is null)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.ReturnOutsideFunction,
                        node.Span,
                        "'return' outside function");
                }
                else if (value is not null)
                {
                    function.ReturnsValue = true;
                }

                VisitExpression(value);
            }

            private void VisitYield(SyntaxNode? value, TextSpan span)
            {
                if (!ValidateExpressionAllowed("yield expression", span))
                {
                    VisitExpression(value);
                    return;
                }

                var function = FindEnclosingFunction(_current);
                if (function is null)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.YieldOutsideFunction,
                        span,
                        "'yield' outside function");
                }
                else if (_current.IsComprehension)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.YieldInsideComprehension,
                        span,
                        "'yield' inside a comprehension");
                }
                else
                {
                    function.IsGenerator = true;
                }

                VisitExpression(value);
            }

            private void VisitDirective(SyntaxNode node, bool isGlobal)
            {
                var names = Node(node, 1);
                if (names is null)
                    return;

                foreach (var token in names.DescendantTokens())
                {
                    if (token.Kind != SyntaxKind.IdentifierToken)
                        continue;

                    var name = Mangle(_current, token.Text);
                    var entry = _current.GetOrAdd(name);
                    var prior = entry.Flags;

                    if (isGlobal)
                    {
                        if ((prior & SymbolFlags.Parameter) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAfterParameter, token, "name is parameter and global");
                        else if ((prior & SymbolFlags.Used) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAfterUse, token, "name is used prior to global declaration");
                        else if ((prior & SymbolFlags.Annotated) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAfterAnnotation, token, "annotated name cannot be global");
                        else if ((prior & SymbolFlags.Local) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAfterAssignment, token, "name is assigned to before global declaration");
                        if ((prior & SymbolFlags.Nonlocal) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAndNonlocal, token, "name is nonlocal and global");

                        AddDefinition(
                            _current,
                            token,
                            SymbolFlags.Global,
                            SymbolOccurrenceKind.Declaration,
                            validateStore: false);
                    }
                    else
                    {
                        if (_current.Kind == SymbolTableKind.Module)
                            DirectiveDiagnostic(SymbolDiagnosticCode.NonlocalAtModuleLevel, token, "nonlocal declaration not allowed at module level");
                        if ((prior & SymbolFlags.Parameter) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.NonlocalAfterParameter, token, "name is parameter and nonlocal");
                        else if ((prior & SymbolFlags.Used) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.NonlocalAfterUse, token, "name is used prior to nonlocal declaration");
                        else if ((prior & SymbolFlags.Annotated) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.NonlocalAfterAnnotation, token, "annotated name cannot be nonlocal");
                        else if ((prior & SymbolFlags.Local) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.NonlocalAfterAssignment, token, "name is assigned to before nonlocal declaration");
                        if ((prior & SymbolFlags.Global) != 0)
                            DirectiveDiagnostic(SymbolDiagnosticCode.GlobalAndNonlocal, token, "name is nonlocal and global");

                        entry.Add(SymbolFlags.Nonlocal, SymbolOccurrenceKind.Declaration, token.Span);
                    }
                }
            }

            private void DirectiveDiagnostic(SymbolDiagnosticCode code, SyntaxToken token, string message) =>
                AddDiagnostic(code, token.Span, message);

            private void VisitImport(SyntaxNode node)
            {
                var aliases = Node(node, 1);
                if (aliases is null)
                    return;

                foreach (var alias in aliases.ChildNodes())
                {
                    if (alias.Kind == SyntaxKind.ImportAlias)
                        BindImportAlias(alias);
                }
            }

            private void VisitFromImport(SyntaxNode node)
            {
                var targets = Node(node, 3);
                if (targets is null)
                    return;

                var star = FirstToken(targets, SyntaxKind.StarToken);
                if (star is { } starToken)
                {
                    if (_current.Kind != SymbolTableKind.Module)
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.ImportStarOutsideModule,
                            starToken.Span,
                            "import * only allowed at module level");
                    }
                    _current.HasImportStar = true;
                    return;
                }

                foreach (var alias in targets.DescendantNodeChildren(SyntaxKind.ImportAlias))
                    BindImportAlias(alias);
            }

            private void BindImportAlias(SyntaxNode alias)
            {
                var aliasToken = Token(alias, 2);
                if (aliasToken is { Kind: SyntaxKind.IdentifierToken } explicitAlias)
                {
                    AddDefinition(_current, explicitAlias, SymbolFlags.Imported, SymbolOccurrenceKind.Import);
                    return;
                }

                var nameNode = Node(alias, 0);
                SyntaxToken? bindingToken = null;
                if (nameNode is not null)
                    bindingToken = FirstToken(nameNode, SyntaxKind.IdentifierToken);
                else
                {
                    var token = Token(alias, 0);
                    if (token is { Kind: SyntaxKind.IdentifierToken })
                        bindingToken = token;
                }

                if (bindingToken is { } binding)
                    AddDefinition(_current, binding, SymbolFlags.Imported, SymbolOccurrenceKind.Import);
            }

            private void VisitTypeAlias(SyntaxNode node)
            {
                var name = Token(node, 1);
                if (name is { } aliasName)
                    AddDefinition(_current, aliasName, SymbolFlags.Local, SymbolOccurrenceKind.Definition);

                var typeParameters = Node(node, 2);
                var value = Node(node, 4);
                var definingTable = _current;
                var parent = definingTable;

                if (typeParameters is not null)
                {
                    var parameters = EnterChild(
                        name?.Text ?? "type alias",
                        SymbolTableKind.TypeParameters,
                        typeParameters,
                        privateName: parent.PrivateName);
                    parameters.CanSeeClassScope = parent.Kind == SymbolTableKind.Class || parent.CanSeeClassScope;
                    if (parameters.CanSeeClassScope)
                        parameters.AddSyntheticUse(ClassDictionaryCellName);
                    VisitTypeParameters(typeParameters, parameters);
                    parent = parameters;
                    ExitChild();
                }

                var alias = EnterChild(
                    name?.Text ?? "type alias",
                    SymbolTableKind.TypeAlias,
                    node,
                    privateName: parent.PrivateName,
                    explicitParent: parent);
                alias.CanSeeClassScope = parent.Kind == SymbolTableKind.Class || parent.CanSeeClassScope;
                if (alias.CanSeeClassScope)
                    alias.AddSyntheticUse(ClassDictionaryCellName);
                VisitExpression(value);
                ExitChild();
                _current = definingTable;
            }

            private void VisitIf(SyntaxNode node)
            {
                VisitExpression(Node(node, 1));
                VisitConditional(() =>
                {
                    VisitNode(Node(node, 3));
                    VisitNode(Node(node, 4));
                });
            }

            private void VisitFor(SyntaxNode node)
            {
                var asyncToken = Token(node, 0);
                if (asyncToken is { Kind: SyntaxKind.AsyncKeyword })
                    ValidateAsyncContext(node.Span);

                VisitTarget(Node(node, 2), TargetContext.Store);
                VisitExpression(Node(node, 4));
                VisitConditional(() =>
                {
                    VisitNode(Node(node, 6));
                    VisitNode(Node(node, 7));
                });
            }

            private void VisitFunction(SyntaxNode node)
            {
                var name = Token(node, 3);
                if (name is not { } nameToken)
                    return;

                AddDefinition(_current, nameToken, SymbolFlags.Local, SymbolOccurrenceKind.Definition);

                var parametersNode = Node(node, 6);
                VisitParameterDefaults(parametersNode);
                VisitDecorators(Node(node, 0));

                var definingTable = _current;
                var typeParameters = Node(node, 4);
                MutableTable semanticParent = definingTable;
                if (typeParameters is not null)
                {
                    var typeParameterTable = EnterChild(
                        nameToken.Text,
                        SymbolTableKind.TypeParameters,
                        typeParameters,
                        privateName: definingTable.PrivateName);
                    typeParameterTable.CanSeeClassScope = definingTable.Kind == SymbolTableKind.Class || definingTable.CanSeeClassScope;
                    if (typeParameterTable.CanSeeClassScope)
                        typeParameterTable.AddSyntheticUse(ClassDictionaryCellName);
                    var defaultKinds = GetFunctionDefaultKinds(parametersNode);
                    if (defaultKinds.HasPositionalDefaults)
                        typeParameterTable.AddHiddenParameter(".defaults");
                    if (defaultKinds.HasKeywordOnlyDefaults)
                        typeParameterTable.AddHiddenParameter(".kwdefaults");
                    VisitTypeParameters(typeParameters, typeParameterTable);
                    semanticParent = typeParameterTable;
                    ExitChild();
                }

                var hasAnnotations = HasFunctionAnnotations(parametersNode, Node(node, 9));
                var annotationTable = EnterChild(
                    "__annotate__",
                    SymbolTableKind.Annotation,
                    parametersNode ?? node,
                    privateName: definingTable.PrivateName,
                    explicitParent: semanticParent);
                annotationTable.CanSeeClassScope = definingTable.Kind == SymbolTableKind.Class || definingTable.CanSeeClassScope;
                annotationTable.HasAnnotations = hasAnnotations;
                if (annotationTable.CanSeeClassScope)
                    annotationTable.AddSyntheticUse(ClassDictionaryCellName);
                VisitParameterAnnotations(parametersNode);
                VisitAnnotationExpression(Node(node, 9));
                ExitChild();

                var function = EnterChild(
                    nameToken.Text,
                    SymbolTableKind.Function,
                    node,
                    privateName: definingTable.PrivateName,
                    explicitParent: semanticParent);
                function.IsCoroutine = Token(node, 1) is { Kind: SyntaxKind.AsyncKeyword };
                BindParameters(parametersNode, function);
                function.HasDocString = HasDocString(Node(node, 11));
                VisitNode(Node(node, 11));
                if (function.IsCoroutine && function.IsGenerator && function.ReturnsValue)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.ReturnWithValueInAsyncGenerator,
                        node.Span,
                        "'return' with value in async generator");
                }
                ExitChild();
                _current = definingTable;
            }

            private void VisitClass(SyntaxNode node)
            {
                var name = Token(node, 2);
                if (name is not { } nameToken)
                    return;

                AddDefinition(_current, nameToken, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                VisitDecorators(Node(node, 0));

                var definingTable = _current;
                var typeParameters = Node(node, 3);
                MutableTable semanticParent = definingTable;
                if (typeParameters is not null)
                {
                    var typeParameterTable = EnterChild(
                        nameToken.Text,
                        SymbolTableKind.TypeParameters,
                        typeParameters,
                        privateName: nameToken.Text);
                    typeParameterTable.CanSeeClassScope = definingTable.Kind == SymbolTableKind.Class || definingTable.CanSeeClassScope;
                    typeParameterTable.MangledNames = new HashSet<string>(StringComparer.Ordinal);
                    typeParameterTable.AddHiddenDefinition(".type_params");
                    typeParameterTable.AddSyntheticUse(".type_params");
                    typeParameterTable.AddHiddenDefinition(".generic_base");
                    typeParameterTable.AddSyntheticUse(".generic_base");
                    if (typeParameterTable.CanSeeClassScope)
                        typeParameterTable.AddSyntheticUse(ClassDictionaryCellName);
                    VisitTypeParameters(typeParameters, typeParameterTable);
                    VisitClassArguments(Node(node, 4));
                    semanticParent = typeParameterTable;
                    ExitChild();
                }
                else
                {
                    VisitClassArguments(Node(node, 4));
                }

                var classTable = EnterChild(
                    nameToken.Text,
                    SymbolTableKind.Class,
                    node,
                    privateName: nameToken.Text,
                    explicitParent: semanticParent);
                classTable.HasDocString = HasDocString(Node(node, 6));
                if (typeParameters is not null)
                {
                    classTable.AddHiddenDefinition("__type_params__");
                    classTable.AddSyntheticUse(".type_params");
                }
                VisitNode(Node(node, 6));
                ExitChild();
                _current = definingTable;
            }

            private void VisitWith(SyntaxNode node)
            {
                if (Token(node, 0) is { Kind: SyntaxKind.AsyncKeyword })
                    ValidateAsyncContext(node.Span);

                VisitConditional(() =>
                {
                    var items = Node(node, 3);
                    if (items is not null)
                    {
                        foreach (var item in items.ChildNodes())
                        {
                            if (item.Kind != SyntaxKind.WithItem)
                                continue;
                            VisitExpression(Node(item, 0));
                            VisitTarget(Node(item, 2), TargetContext.Store);
                        }
                    }
                    VisitNode(Node(node, 6));
                });
            }

            private void VisitTry(SyntaxNode node)
            {
                VisitConditional(() =>
                {
                    VisitNode(Node(node, 2));
                    VisitNode(Node(node, 3));
                });
            }

            private void VisitExcept(SyntaxNode node)
            {
                VisitExpression(Node(node, 2));
                var name = Token(node, 4);
                if (name is { Kind: SyntaxKind.IdentifierToken } token)
                    AddDefinition(_current, token, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                VisitNode(Node(node, 6));
            }

            private void VisitMatch(SyntaxNode node)
            {
                VisitExpression(Node(node, 1));
                VisitConditional(() => VisitNode(Node(node, 5)));
            }

            private void VisitCase(SyntaxNode node)
            {
                VisitPattern(Node(node, 1));
                VisitExpression(Node(node, 3));
                VisitNode(Node(node, 5));
            }

            private void VisitConditional(Action action)
            {
                var owner = _current;
                var wasConditional = owner.InConditionalBlock;
                owner.InConditionalBlock = true;
                try
                {
                    action();
                }
                finally
                {
                    owner.InConditionalBlock = wasConditional;
                    _current = owner;
                }
            }

            private void VisitExpression(SyntaxNode? node)
            {
                if (node is null)
                    return;

                switch (node.Kind)
                {
                    case SyntaxKind.NameExpression:
                        {
                            var name = Token(node, 0);
                            if (name is { } token && !_current.InUnevaluatedAnnotation)
                            {
                                AddUse(_current, token);
                                if (string.Equals(token.Text, "super", StringComparison.Ordinal) && IsFunctionLike(_current))
                                    _current.AddSyntheticUse(ClassCellName);
                            }
                            break;
                        }
                    case SyntaxKind.LiteralExpression:
                        break;
                    case SyntaxKind.StringConcatenationExpression:
                    case SyntaxKind.ParenthesizedExpression:
                    case SyntaxKind.TupleExpression:
                    case SyntaxKind.ListExpression:
                    case SyntaxKind.SetExpression:
                    case SyntaxKind.DictionaryExpression:
                    case SyntaxKind.KeyValuePair:
                    case SyntaxKind.StarredExpression:
                    case SyntaxKind.DoubleStarredExpression:
                    case SyntaxKind.ConditionalExpression:
                    case SyntaxKind.UnaryExpression:
                    case SyntaxKind.BinaryExpression:
                    case SyntaxKind.ComparisonExpression:
                    case SyntaxKind.SliceExpression:
                    case SyntaxKind.SliceList:
                    case SyntaxKind.ArgumentList:
                        VisitExpressionChildren(node);
                        break;
                    case SyntaxKind.KeywordArgument:
                        {
                            if (Token(node, 0) is { } keyword)
                                ValidateStoreName(keyword);
                            VisitExpression(Node(node, 2));
                            break;
                        }
                    case SyntaxKind.FStringExpression:
                    case SyntaxKind.TStringExpression:
                    case SyntaxKind.Interpolation:
                    case SyntaxKind.FormatSpecClause:
                        VisitExpressionChildren(node);
                        break;
                    case SyntaxKind.ConversionClause:
                        break;
                    case SyntaxKind.NamedExpression:
                        VisitNamedExpression(node);
                        break;
                    case SyntaxKind.LambdaExpression:
                        VisitLambda(node);
                        break;
                    case SyntaxKind.YieldExpression:
                        VisitYield(Node(node, 2), node.Span);
                        break;
                    case SyntaxKind.AwaitExpression:
                        VisitAwait(node);
                        break;
                    case SyntaxKind.AttributeExpression:
                        VisitExpression(Node(node, 0));
                        break;
                    case SyntaxKind.CallExpression:
                        VisitExpression(Node(node, 0));
                        VisitExpression(Node(node, 1));
                        break;
                    case SyntaxKind.SubscriptExpression:
                        VisitExpression(Node(node, 0));
                        VisitExpression(Node(node, 2));
                        break;
                    case SyntaxKind.GeneratorExpression:
                        VisitComprehension(node, ComprehensionKind.Generator);
                        break;
                    case SyntaxKind.ListComprehensionExpression:
                        VisitComprehension(node, ComprehensionKind.List);
                        break;
                    case SyntaxKind.SetComprehensionExpression:
                        VisitComprehension(node, ComprehensionKind.Set);
                        break;
                    case SyntaxKind.DictionaryComprehensionExpression:
                        VisitComprehension(node, ComprehensionKind.Dictionary);
                        break;
                    case SyntaxKind.ErrorExpression:
                    case SyntaxKind.MissingExpression:
                        VisitExpressionChildren(node);
                        break;
                    default:
                        VisitExpressionChildren(node);
                        break;
                }
            }

            private void VisitExpressionChildren(SyntaxNode node)
            {
                foreach (var child in node.ChildNodes())
                {
                    if (IsExpression(child.Kind))
                        VisitExpression(child);
                    else if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SeparatedSyntaxList)
                        VisitExpressionChildren(child);
                }
            }

            private void VisitNamedExpression(SyntaxNode node)
            {
                var target = Token(node, 0);
                if (!ValidateExpressionAllowed("named expression", node.Span))
                {
                    VisitExpression(Node(node, 2));
                    return;
                }

                if (target is not { } token)
                {
                    VisitExpression(Node(node, 2));
                    return;
                }

                if (_comprehensionIterableDepth != 0)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.NamedExpressionInComprehensionIterable,
                        token.Span,
                        "assignment expression cannot be used in a comprehension iterable expression");
                    return;
                }

                VisitExpression(Node(node, 2));
                if (!_current.IsComprehension)
                {
                    AddDefinition(_current, token, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                    return;
                }

                var mangled = Mangle(_current, token.Text);
                for (MutableTable? table = _current; table is not null && table.IsComprehension; table = table.Parent)
                {
                    if (table.TryGetEntry(mangled, out var existing) &&
                        (existing.Flags & SymbolFlags.ComprehensionIterationVariable) != 0)
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.NamedExpressionRebindsComprehensionVariable,
                            token.Span,
                            $"assignment expression cannot rebind comprehension iteration variable '{token.Text}'");
                        return;
                    }
                }

                var owner = FindNamedExpressionOwner(_current.Parent);
                if (owner is null)
                    return;

                if (owner.Kind == SymbolTableKind.Class)
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.NamedExpressionInClassComprehension,
                        token.Span,
                        "assignment expression within a comprehension cannot be used in a class body");
                    return;
                }

                if (owner.Kind is SymbolTableKind.TypeParameters or SymbolTableKind.TypeAlias or SymbolTableKind.TypeVariable)
                {
                    var message = owner.Kind switch
                    {
                        SymbolTableKind.TypeParameters => "assignment expression within a comprehension cannot be used within the definition of a generic",
                        SymbolTableKind.TypeAlias => "assignment expression within a comprehension cannot be used in a type alias",
                        _ => "assignment expression within a comprehension cannot be used in a type variable bound or default",
                    };
                    AddDiagnostic(SymbolDiagnosticCode.NamedExpressionInTypeScopeComprehension, token.Span, message);
                    return;
                }

                if (owner.Kind == SymbolTableKind.Module)
                {
                    AddDefinition(_current, token, SymbolFlags.Global, SymbolOccurrenceKind.Declaration);
                    return;
                }

                var ownerName = Mangle(owner, token.Text);
                var ownerHasGlobal = owner.TryGetEntry(ownerName, out var ownerEntry) &&
                    (ownerEntry.Flags & SymbolFlags.Global) != 0;
                AddDefinition(
                    _current,
                    token,
                    ownerHasGlobal ? SymbolFlags.Global : SymbolFlags.Nonlocal,
                    SymbolOccurrenceKind.Declaration);
                AddDefinition(owner, token, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
            }

            private static MutableTable? FindNamedExpressionOwner(MutableTable? table)
            {
                for (var current = table; current is not null; current = current.Parent)
                {
                    if (current.IsComprehension || current.Kind == SymbolTableKind.Annotation)
                        continue;
                    return current;
                }
                return null;
            }

            private void VisitLambda(SyntaxNode node)
            {
                var parameters = Node(node, 1);
                VisitParameterDefaults(parameters);
                var parent = _current;
                var lambda = EnterChild("lambda", SymbolTableKind.Function, node, parent.PrivateName);
                BindParameters(parameters, lambda);
                VisitExpression(Node(node, 3));
                ExitChild();
            }

            private void VisitAwait(SyntaxNode node)
            {
                if (!ValidateExpressionAllowed("await expression", node.Span))
                {
                    VisitExpression(Node(node, 1));
                    return;
                }

                if (_current.IsComprehension)
                {
                    _current.IsCoroutine = true;
                }
                else if (_current.Kind == SymbolTableKind.Function && _current.IsCoroutine)
                {
                }
                else if (_current.Kind == SymbolTableKind.Module && _options.AllowTopLevelAwait)
                {
                    _current.IsCoroutine = true;
                }
                else
                {
                    AddDiagnostic(
                        SymbolDiagnosticCode.AwaitOutsideAsyncFunction,
                        node.Span,
                        "'await' outside async function");
                }
                VisitExpression(Node(node, 1));
            }

            private void VisitComprehension(SyntaxNode node, ComprehensionKind kind)
            {
                var isUnparenthesizedGenerator =
                    node.Kind == SyntaxKind.GeneratorExpression &&
                    Token(node, 0) is not { Kind: SyntaxKind.LeftParenthesisToken };
                var elementIndex = isUnparenthesizedGenerator ? 0 : 1;
                var clausesIndex = isUnparenthesizedGenerator ? 1 : 2;

                var element = Node(node, elementIndex);
                var clauses = Node(node, clausesIndex);
                var forClauses = clauses?.DescendantNodeChildren(SyntaxKind.ForComprehensionClause)
                    .ToArray() ?? [];
                if (forClauses.Length == 0)
                {
                    VisitExpression(element);
                    return;
                }

                var outerFor = forClauses[0];
                _comprehensionIterableDepth++;
                VisitExpression(Node(outerFor, 4));
                _comprehensionIterableDepth--;

                var parent = _current;
                var name = kind switch
                {
                    ComprehensionKind.List => "listcomp",
                    ComprehensionKind.Set => "setcomp",
                    ComprehensionKind.Dictionary => "dictcomp",
                    _ => "genexpr",
                };
                var comprehension = EnterChild(name, SymbolTableKind.Function, node, parent.PrivateName);
                comprehension.IsComprehension = true;
                comprehension.ComprehensionKind = kind;
                comprehension.IsComprehensionInlined = kind != ComprehensionKind.Generator && _options.InlineComprehensions;
                comprehension.IsGenerator = kind == ComprehensionKind.Generator;
                comprehension.AddParameter(".0", node.Span);

                VisitTarget(Node(outerFor, 2), TargetContext.ComprehensionStore);
                VisitComprehensionClauses(clauses, skipFirstForIterable: true);
                VisitExpression(element);
                var isAsyncComprehension = comprehension.IsCoroutine && kind != ComprehensionKind.Generator;
                ExitChild();

                if (isAsyncComprehension)
                {
                    if (parent.IsComprehension)
                    {
                        parent.IsCoroutine = true;
                    }
                    else if (parent.Kind == SymbolTableKind.Function && parent.IsCoroutine)
                    {
                    }
                    else if (parent.Kind == SymbolTableKind.Module && _options.AllowTopLevelAwait)
                    {
                        parent.IsCoroutine = true;
                    }
                    else
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.AsyncComprehensionOutsideAsyncFunction,
                            node.Span,
                            "asynchronous comprehension outside of an asynchronous function");
                    }
                }
            }

            private void VisitComprehensionClauses(SyntaxNode? clauses, bool skipFirstForIterable)
            {
                if (clauses is null)
                    return;

                var skipped = false;
                foreach (var clause in clauses.DescendantNodeChildren())
                {
                    if (clause.Kind == SyntaxKind.ForComprehensionClause)
                    {
                        if (Token(clause, 0) is { Kind: SyntaxKind.AsyncKeyword })
                            _current.IsCoroutine = true;

                        if (!skipped && skipFirstForIterable)
                        {
                            skipped = true;
                        }
                        else
                        {
                            VisitTarget(Node(clause, 2), TargetContext.ComprehensionStore);
                            _comprehensionIterableDepth++;
                            VisitExpression(Node(clause, 4));
                            _comprehensionIterableDepth--;
                        }
                    }
                    else if (clause.Kind == SyntaxKind.IfComprehensionClause)
                    {
                        VisitExpression(Node(clause, 1));
                    }
                }
            }

            private void VisitTarget(SyntaxNode? node, TargetContext context)
            {
                if (node is null)
                    return;

                switch (node.Kind)
                {
                    case SyntaxKind.NameExpression:
                        {
                            var name = Token(node, 0);
                            if (name is not { } token)
                                return;
                            if (context == TargetContext.Delete &&
                                string.Equals(token.Text, "__debug__", StringComparison.Ordinal))
                            {
                                AddDiagnostic(
                                    SymbolDiagnosticCode.DeleteDebug,
                                    token.Span,
                                    "cannot delete __debug__");
                            }

                            var flags = SymbolFlags.Local;
                            var occurrence = SymbolOccurrenceKind.Definition;
                            if (context == TargetContext.ComprehensionStore)
                            {
                                var mangled = Mangle(_current, token.Text);
                                if (_current.TryGetEntry(mangled, out var prior) &&
                                    (prior.Flags & (SymbolFlags.Global | SymbolFlags.Nonlocal)) != 0)
                                {
                                    AddDiagnostic(
                                        SymbolDiagnosticCode.NamedExpressionRebindsComprehensionVariable,
                                        token.Span,
                                        $"comprehension inner loop cannot rebind assignment expression target '{token.Text}'");
                                    return;
                                }

                                flags |= SymbolFlags.ComprehensionIterationVariable;
                                occurrence = SymbolOccurrenceKind.ComprehensionTarget;
                            }
                            AddDefinition(
                                _current,
                                token,
                                flags,
                                occurrence,
                                validateStore: context != TargetContext.Delete);
                            break;
                        }
                    case SyntaxKind.ParenthesizedExpression:
                        VisitTarget(Node(node, 1), context);
                        break;
                    case SyntaxKind.TupleExpression:
                    case SyntaxKind.ListExpression:
                    case SyntaxKind.StarredExpression:
                        foreach (var child in node.ChildNodes())
                        {
                            if (IsExpression(child.Kind))
                                VisitTarget(child, context);
                            else if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SeparatedSyntaxList)
                                VisitTargetList(child, context);
                        }
                        break;
                    case SyntaxKind.AttributeExpression:
                        if (Token(node, 2) is { } attribute)
                        {
                            if (context == TargetContext.Delete &&
                                string.Equals(attribute.Text, "__debug__", StringComparison.Ordinal))
                            {
                                AddDiagnostic(
                                    SymbolDiagnosticCode.DeleteDebug,
                                    attribute.Span,
                                    "cannot delete __debug__");
                            }
                            else if (context != TargetContext.Delete)
                            {
                                ValidateStoreName(attribute);
                            }
                        }
                        VisitExpression(Node(node, 0));
                        break;
                    case SyntaxKind.SubscriptExpression:
                        VisitExpression(Node(node, 0));
                        VisitExpression(Node(node, 2));
                        break;
                    default:
                        VisitExpression(node);
                        break;
                }
            }

            private void VisitTargetList(SyntaxNode node, TargetContext context)
            {
                foreach (var child in node.ChildNodes())
                {
                    if (IsExpression(child.Kind))
                        VisitTarget(child, context);
                    else
                        VisitTargetList(child, context);
                }
            }

            private void VisitPattern(SyntaxNode? node)
            {
                if (node is null)
                    return;

                switch (node.Kind)
                {
                    case SyntaxKind.CapturePattern:
                        {
                            var name = Token(node, 0);
                            if (name is { } token && !string.Equals(token.Text, "_", StringComparison.Ordinal))
                                AddDefinition(_current, token, SymbolFlags.Local, SymbolOccurrenceKind.Definition);
                            break;
                        }
                    case SyntaxKind.WildcardPattern:
                    case SyntaxKind.LiteralPattern:
                    case SyntaxKind.MissingPattern:
                        break;
                    case SyntaxKind.ValuePattern:
                        VisitExpression(Node(node, 0));
                        break;
                    case SyntaxKind.AsPattern:
                        VisitPattern(Node(node, 0));
                        VisitPattern(Node(node, 2));
                        break;
                    case SyntaxKind.StarPattern:
                    case SyntaxKind.DoubleStarPattern:
                        VisitPattern(Node(node, 1));
                        break;
                    case SyntaxKind.MappingPatternItem:
                        VisitExpression(Node(node, 0));
                        VisitPattern(Node(node, 2));
                        break;
                    case SyntaxKind.ClassPattern:
                        VisitExpression(Node(node, 0));
                        VisitPatternChildren(Node(node, 2));
                        break;
                    case SyntaxKind.KeywordPattern:
                        if (Token(node, 0) is { } keyword)
                            ValidateStoreName(keyword);
                        VisitPattern(Node(node, 2));
                        break;
                    default:
                        VisitPatternChildren(node);
                        break;
                }
            }

            private void VisitPatternChildren(SyntaxNode? node)
            {
                if (node is null)
                    return;
                foreach (var child in node.ChildNodes())
                {
                    if (IsPattern(child.Kind))
                        VisitPattern(child);
                    else if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SeparatedSyntaxList)
                        VisitPatternChildren(child);
                }
            }

            private void VisitAnnotation(SyntaxNode? annotation, SyntaxNode declaration)
            {
                if (annotation is null)
                    return;

                var owner = _current;
                owner.HasAnnotations = true;
                if (owner.Kind == SymbolTableKind.Module ||
                    (owner.Kind == SymbolTableKind.Class && owner.InConditionalBlock))
                {
                    owner.HasConditionalAnnotations = true;
                    owner.AddSyntheticUse(ConditionalAnnotationsCellName);
                }

                var existing = owner.AnnotationBlock;
                if (existing is null)
                {
                    existing = new MutableTable(
                        "__annotate__",
                        SymbolTableKind.Annotation,
                        owner,
                        declaration,
                        owner.PrivateName)
                    {
                        CanSeeClassScope = owner.Kind == SymbolTableKind.Class && !_futureAnnotations,
                        IsNested = owner.IsNested || IsFunctionLike(owner),
                        MangledNames = owner.MangledNames,
                    };
                    InitializeImplicitParameters(existing);
                    owner.AnnotationBlock = existing;
                    owner.Children.Add(existing);
                    if (existing.CanSeeClassScope)
                        existing.AddSyntheticUse(ClassDictionaryCellName);
                }

                var previous = _current;
                var wasUnevaluated = existing.InUnevaluatedAnnotation;
                _current = existing;
                existing.InUnevaluatedAnnotation = owner.Kind == SymbolTableKind.Function;
                VisitAnnotationExpression(annotation);
                existing.InUnevaluatedAnnotation = wasUnevaluated;
                _current = previous;
            }

            private void VisitAnnotationExpression(SyntaxNode? annotation) =>
                VisitExpression(annotation);

            private void VisitDecorators(SyntaxNode? decorators)
            {
                if (decorators is null)
                    return;
                foreach (var decorator in decorators.DescendantNodeChildren(SyntaxKind.Decorator))
                    VisitExpression(Node(decorator, 1));
            }

            private void VisitClassArguments(SyntaxNode? arguments)
            {
                if (arguments is null)
                    return;
                VisitExpression(arguments);
            }

            private static bool HasFunctionAnnotations(SyntaxNode? parameters, SyntaxNode? returnAnnotation)
            {
                if (returnAnnotation is not null)
                    return true;
                if (parameters is null)
                    return false;

                foreach (var parameter in parameters.DescendantNodeChildren(SyntaxKind.Parameter))
                {
                    if (Token(parameter, 2) is { Kind: SyntaxKind.ColonToken })
                        return true;
                }
                return false;
            }

            private void VisitParameterDefaults(SyntaxNode? parameters)
            {
                if (parameters is null)
                    return;
                foreach (var parameter in parameters.DescendantNodeChildren(SyntaxKind.Parameter))
                {
                    if (Token(parameter, 4) is { Kind: SyntaxKind.EqualToken })
                        VisitExpression(Node(parameter, 5));
                    else if (Token(parameter, 2) is { Kind: SyntaxKind.EqualToken })
                        VisitExpression(Node(parameter, 3));
                }
            }

            private void VisitParameterAnnotations(SyntaxNode? parameters)
            {
                if (parameters is null)
                    return;
                foreach (var parameter in parameters.DescendantNodeChildren(SyntaxKind.Parameter))
                {
                    if (Token(parameter, 2) is { Kind: SyntaxKind.ColonToken })
                        VisitAnnotationExpression(Node(parameter, 3));
                }
            }

            private static (bool HasPositionalDefaults, bool HasKeywordOnlyDefaults) GetFunctionDefaultKinds(
                SyntaxNode? parameters)
            {
                var list = parameters is null ? null : Node(parameters, 0);
                if (list is null)
                    return default;

                var keywordOnly = false;
                var positionalDefaults = false;
                var keywordOnlyDefaults = false;
                foreach (var child in list.ChildNodesAndTokens())
                {
                    if (child.IsToken)
                    {
                        if (child.AsToken().Kind == SyntaxKind.StarToken)
                            keywordOnly = true;
                        continue;
                    }

                    var parameter = child.AsNode();
                    if (parameter.Kind != SyntaxKind.Parameter)
                        continue;

                    var prefix = Token(parameter, 0);
                    if (prefix is { Kind: SyntaxKind.StarToken or SyntaxKind.DoubleStarToken })
                        keywordOnly = true;

                    var hasDefault =
                        Token(parameter, 4) is { Kind: SyntaxKind.EqualToken } ||
                        Token(parameter, 2) is { Kind: SyntaxKind.EqualToken };
                    if (!hasDefault)
                        continue;

                    if (keywordOnly)
                        keywordOnlyDefaults = true;
                    else
                        positionalDefaults = true;
                }

                return (positionalDefaults, keywordOnlyDefaults);
            }

            private void BindParameters(SyntaxNode? parameters, MutableTable table)
            {
                if (parameters is null)
                    return;

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in parameters.DescendantNodeChildren(SyntaxKind.Parameter))
                {
                    var name = Token(parameter, 1);
                    if (name is not { Kind: SyntaxKind.IdentifierToken } token)
                        continue;

                    var mangled = Mangle(table, token.Text);
                    if (!seen.Add(mangled))
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.DuplicateParameter,
                            token.Span,
                            $"duplicate argument '{token.Text}' in function definition");
                        continue;
                    }

                    ValidateStoreName(token);
                    table.AddParameter(mangled, token.Span);
                    var prefix = Token(parameter, 0);
                    if (prefix is { Kind: SyntaxKind.StarToken })
                        table.HasVarArgs = true;
                    else if (prefix is { Kind: SyntaxKind.DoubleStarToken })
                        table.HasVarKeywords = true;
                }
            }

            private void VisitTypeParameters(SyntaxNode node, MutableTable table)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in node.DescendantNodeChildren(SyntaxKind.TypeParameter))
                {
                    var name = Token(parameter, 1);
                    if (name is not { Kind: SyntaxKind.IdentifierToken } token)
                        continue;

                    table.MangledNames?.Add(token.Text);

                    if (string.Equals(token.Text, ClassDictionaryCellName, StringComparison.Ordinal))
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.ReservedTypeParameterName,
                            token.Span,
                            $"reserved name '{token.Text}' cannot be used for type parameter");
                    }

                    var mangled = Mangle(table, token.Text);
                    if (!seen.Add(mangled))
                    {
                        AddDiagnostic(
                            SymbolDiagnosticCode.DuplicateTypeParameter,
                            token.Span,
                            $"duplicate type parameter '{token.Text}'");
                        continue;
                    }

                    ValidateStoreName(token);
                    table.GetOrAdd(mangled).Add(
                        SymbolFlags.Local | SymbolFlags.TypeParameter,
                        SymbolOccurrenceKind.Parameter,
                        token.Span);

                    VisitTypeVariableExpression(Node(parameter, 3), TypeVariableScopeKind.Bound, table, token.Text);
                    VisitTypeVariableExpression(Node(parameter, 5), TypeVariableScopeKind.Default, table, token.Text);
                }
            }

            private void VisitTypeVariableExpression(
                SyntaxNode? expression,
                TypeVariableScopeKind scopeKind,
                MutableTable parent,
                string name)
            {
                if (expression is null)
                    return;

                var typeVariable = EnterChild(
                    name,
                    SymbolTableKind.TypeVariable,
                    expression,
                    parent.PrivateName,
                    explicitParent: parent);
                typeVariable.TypeVariableScopeKind = scopeKind;
                typeVariable.CanSeeClassScope = parent.CanSeeClassScope;
                if (typeVariable.CanSeeClassScope)
                    typeVariable.AddSyntheticUse(ClassDictionaryCellName);
                VisitExpression(expression);
                ExitChild();
            }

            private bool ValidateExpressionAllowed(string expressionName, TextSpan span)
            {
                switch (_current.Kind)
                {
                    case SymbolTableKind.Annotation:
                        AddDiagnostic(
                            SymbolDiagnosticCode.ExpressionNotAllowedInAnnotation,
                            span,
                            $"{expressionName} cannot be used within an annotation");
                        return false;
                    case SymbolTableKind.TypeVariable:
                        var location = _current.TypeVariableScopeKind == TypeVariableScopeKind.Default
                            ? "a type variable default"
                            : "a type variable bound";
                        AddDiagnostic(
                            SymbolDiagnosticCode.ExpressionNotAllowedInTypeVariable,
                            span,
                            $"{expressionName} cannot be used within {location}");
                        return false;
                    case SymbolTableKind.TypeAlias:
                        AddDiagnostic(
                            SymbolDiagnosticCode.ExpressionNotAllowedInTypeAlias,
                            span,
                            $"{expressionName} cannot be used within a type alias");
                        return false;
                    case SymbolTableKind.TypeParameters:
                        AddDiagnostic(
                            SymbolDiagnosticCode.ExpressionNotAllowedInTypeParameters,
                            span,
                            $"{expressionName} cannot be used within the definition of a generic");
                        return false;
                    default:
                        return true;
                }
            }

            private void ValidateAsyncContext(TextSpan span)
            {
                var function = FindEnclosingFunction(_current);
                if (function?.IsCoroutine == true)
                    return;
                if (function?.IsComprehension == true && function.Parent?.IsCoroutine == true)
                    return;
                if (_current.Kind == SymbolTableKind.Module && _options.AllowTopLevelAwait)
                {
                    _current.IsCoroutine = true;
                    return;
                }

                AddDiagnostic(
                    SymbolDiagnosticCode.AsyncComprehensionOutsideAsyncFunction,
                    span,
                    "asynchronous construct outside of an asynchronous function");
            }

            private MutableTable EnterChild(
                string name,
                SymbolTableKind kind,
                SyntaxNode declaration,
                string? privateName,
                MutableTable? explicitParent = null)
            {
                var parent = explicitParent ?? _current;
                var child = new MutableTable(name, kind, parent, declaration, privateName)
                {
                    IsNested = parent.IsNested || IsFunctionLike(parent),
                    IsMethod = kind == SymbolTableKind.Function && parent.Kind == SymbolTableKind.Class,
                    MangledNames = kind == SymbolTableKind.Class ? null : parent.MangledNames,
                };
                InitializeImplicitParameters(child);
                parent.Children.Add(child);
                _current = child;
                return child;
            }

            private static void InitializeImplicitParameters(MutableTable table)
            {
                if (table.Kind is not (SymbolTableKind.Annotation or SymbolTableKind.TypeVariable or SymbolTableKind.TypeAlias))
                    return;

                table.AddHiddenParameter(".format");
                table.AddSyntheticUse(".format");
            }

            private void ExitChild()
            {
                _current = _current.Parent ?? throw new InvalidOperationException("Cannot leave the module symbol table.");
            }

            private void AddDefinition(
                MutableTable table,
                SyntaxToken token,
                SymbolFlags flags,
                SymbolOccurrenceKind kind,
                bool validateStore = true)
            {
                if (validateStore &&
                    (flags & (SymbolFlags.Local | SymbolFlags.Parameter | SymbolFlags.Imported)) != 0)
                {
                    ValidateStoreName(token);
                }

                var name = Mangle(table, token.Text);
                table.GetOrAdd(name).Add(flags, kind, token.Span);

                if ((flags & SymbolFlags.Global) == 0 || table.Kind == SymbolTableKind.Module)
                    return;

                var module = table;
                while (module.Parent is not null)
                    module = module.Parent;
                module.GetOrAdd(name).Add(
                    SymbolFlags.Global,
                    SymbolOccurrenceKind.Declaration,
                    token.Span);
            }

            private void ValidateStoreName(SyntaxToken token)
            {
                if (!string.Equals(token.Text, "__debug__", StringComparison.Ordinal))
                    return;

                AddDiagnostic(
                    SymbolDiagnosticCode.AssignmentToDebug,
                    token.Span,
                    "cannot assign to __debug__");
            }

            private void AddUse(MutableTable table, SyntaxToken token)
            {
                var name = Mangle(table, token.Text);
                table.GetOrAdd(name).Add(SymbolFlags.Used, SymbolOccurrenceKind.Reference, token.Span);
            }

            private void AddDiagnostic(SymbolDiagnosticCode code, TextSpan span, string message) =>
                _diagnostics.Add(new SymbolDiagnostic(code, span, message));

            private static MutableTable? FindEnclosingFunction(MutableTable table)
            {
                for (MutableTable? current = table; current is not null; current = current.Parent)
                {
                    if (current.Kind == SymbolTableKind.Function)
                        return current;
                    if (current.Kind == SymbolTableKind.Class)
                        return null;
                }
                return null;
            }

            private static bool IsFunctionLike(MutableTable table) =>
                table.Kind is SymbolTableKind.Function or SymbolTableKind.Annotation or
                    SymbolTableKind.TypeAlias or SymbolTableKind.TypeParameters or SymbolTableKind.TypeVariable;

            private HashSet<string> Analyze(
                MutableTable table,
                HashSet<string> outerBound,
                HashSet<string> outerGlobals,
                HashSet<string> outerTypeParameters,
                MutableTable? classEntry)
            {
                var locals = new HashSet<string>(StringComparer.Ordinal);
                var explicitGlobals = new HashSet<string>(outerGlobals, StringComparer.Ordinal);
                var visibleTypeParameters = new HashSet<string>(outerTypeParameters, StringComparer.Ordinal);
                var ownFree = new HashSet<string>(StringComparer.Ordinal);

                foreach (var entry in table.Entries)
                {
                    var flags = entry.Flags;
                    if ((flags & SymbolFlags.Global) != 0)
                    {
                        entry.Scope = SymbolScope.GlobalExplicit;
                        explicitGlobals.Add(entry.Name);
                    }
                    else if ((flags & SymbolFlags.Nonlocal) != 0)
                    {
                        if (table.Kind != SymbolTableKind.Module && !outerBound.Contains(entry.Name))
                        {
                            AddDiagnostic(
                                SymbolDiagnosticCode.NonlocalBindingNotFound,
                                entry.FirstSpan,
                                $"no binding for nonlocal '{entry.Name}' found");
                        }
                        else if (outerTypeParameters.Contains(entry.Name))
                        {
                            AddDiagnostic(
                                SymbolDiagnosticCode.NonlocalTypeParameter,
                                entry.FirstSpan,
                                $"nonlocal binding not allowed for type parameter '{entry.Name}'");
                        }
                        entry.Scope = SymbolScope.Free;
                        ownFree.Add(entry.Name);
                    }
                    else if ((flags & (SymbolFlags.Local | SymbolFlags.Parameter | SymbolFlags.Imported | SymbolFlags.TypeParameter)) != 0)
                    {
                        entry.Scope = SymbolScope.Local;
                        locals.Add(entry.Name);
                        if ((flags & SymbolFlags.TypeParameter) != 0)
                            visibleTypeParameters.Add(entry.Name);
                        else
                            visibleTypeParameters.Remove(entry.Name);
                    }
                    else if (TryResolveThroughClassScope(classEntry, entry.Name, out var classScope))
                    {
                        entry.Scope = classScope;
                    }
                    else if (outerBound.Contains(entry.Name))
                    {
                        entry.Scope = SymbolScope.Free;
                        ownFree.Add(entry.Name);
                    }
                    else
                    {
                        entry.Scope = SymbolScope.GlobalImplicit;
                    }
                }

                var childBound = new HashSet<string>(outerBound, StringComparer.Ordinal);
                var childGlobals = table.Kind == SymbolTableKind.Class
                    ? new HashSet<string>(outerGlobals, StringComparer.Ordinal)
                    : explicitGlobals;
                if (table.Kind != SymbolTableKind.Class)
                {
                    foreach (var entry in table.Entries)
                    {
                        if ((entry.Flags & SymbolFlags.Global) != 0)
                            childBound.Remove(entry.Name);
                    }
                }
                if (IsFunctionLike(table))
                    childBound.UnionWith(locals);
                if (table.Kind == SymbolTableKind.Class)
                {
                    childBound.Add(ClassCellName);
                    childBound.Add(ClassDictionaryCellName);
                    childBound.Add(ConditionalAnnotationsCellName);
                }

                var allFree = new HashSet<string>(ownFree, StringComparer.Ordinal);
                var inlinedCells = new HashSet<string>(StringComparer.Ordinal);

                foreach (var child in table.Children)
                {
                    MutableTable? childClassEntry = null;
                    if (child.CanSeeClassScope)
                        childClassEntry = table.Kind == SymbolTableKind.Class ? table : classEntry;

                    child.IsComprehensionInlined =
                        child.IsComprehension &&
                        child.ComprehensionKind != ComprehensionKind.Generator &&
                        _options.InlineComprehensions &&
                        !table.CanSeeClassScope;

                    var childFree = Analyze(child, childBound, childGlobals, visibleTypeParameters, childClassEntry);
                    if (child.IsComprehensionInlined)
                        InlineComprehension(table, child, childFree, inlinedCells, locals);
                    allFree.UnionWith(childFree);
                }

                if (table.Kind == SymbolTableKind.Class)
                {
                    if (allFree.Remove(ClassCellName))
                        table.NeedsClassClosure = true;
                    if (allFree.Remove(ClassDictionaryCellName))
                        table.NeedsClassDictionary = true;
                    if (allFree.Remove(ConditionalAnnotationsCellName))
                    {
                        table.NeedsConditionalAnnotationsClosure = true;
                        table.HasConditionalAnnotations = true;
                    }
                }
                else if (IsFunctionLike(table))
                {
                    foreach (var name in locals)
                    {
                        if (!allFree.Contains(name) && !inlinedCells.Contains(name))
                            continue;
                        if (table.TryGetEntry(name, out var localEntry))
                        {
                            localEntry.Scope = SymbolScope.Cell;
                            if (inlinedCells.Contains(name))
                                localEntry.Flags |= SymbolFlags.ComprehensionCell;
                        }
                        allFree.Remove(name);
                    }
                }

                var classFlag = table.Kind == SymbolTableKind.Class || table.CanSeeClassScope;
                foreach (var name in allFree)
                {
                    if (table.TryGetEntry(name, out var existing))
                    {
                        if (classFlag)
                            existing.Flags |= SymbolFlags.FreeClass;
                        continue;
                    }

                    if (outerBound.Contains(name))
                        table.GetOrAdd(name).Scope = SymbolScope.Free;
                }

                return allFree;
            }

            private static bool TryResolveThroughClassScope(
                MutableTable? classEntry,
                string name,
                out SymbolScope scope)
            {
                scope = default;
                if (classEntry is null || !classEntry.TryGetEntry(name, out var entry))
                    return false;

                if ((entry.Flags & SymbolFlags.Global) != 0)
                {
                    scope = SymbolScope.GlobalExplicit;
                    return true;
                }

                if ((entry.Flags & (SymbolFlags.Local | SymbolFlags.Parameter | SymbolFlags.Imported | SymbolFlags.TypeParameter)) != 0 &&
                    (entry.Flags & SymbolFlags.Nonlocal) == 0)
                {
                    scope = SymbolScope.GlobalImplicit;
                    return true;
                }

                return false;
            }

            private static void InlineComprehension(
                MutableTable parent,
                MutableTable comprehension,
                HashSet<string> childFree,
                HashSet<string> inlinedCells,
                HashSet<string> parentLocals)
            {
                foreach (var entry in comprehension.Entries)
                {
                    if ((entry.Flags & SymbolFlags.Parameter) != 0)
                        continue;

                    var scope = entry.Scope;
                    if (parent.Kind == SymbolTableKind.Class &&
                        scope == SymbolScope.Free &&
                        IsSpecialClassFreeName(entry.Name))
                    {
                        scope = SymbolScope.GlobalImplicit;
                        if (!IsFreeInAnyChild(comprehension, entry.Name))
                            childFree.Remove(entry.Name);
                    }

                    var isInlinedCell = scope == SymbolScope.Cell ||
                        (entry.Flags & SymbolFlags.ComprehensionCell) != 0;
                    if (isInlinedCell)
                        inlinedCells.Add(entry.Name);

                    if (!parent.TryGetEntry(entry.Name, out var existing))
                    {
                        existing = parent.GetOrAdd(entry.Name);
                        existing.Flags |= entry.Flags;
                        if (isInlinedCell)
                            existing.Flags |= SymbolFlags.ComprehensionCell;
                        existing.Scope = scope;
                        existing.Occurrences.AddRange(entry.Occurrences);
                        if (scope is SymbolScope.Local or SymbolScope.Cell)
                            parentLocals.Add(entry.Name);
                    }
                    else
                    {
                        if (isInlinedCell)
                            existing.Flags |= SymbolFlags.ComprehensionCell;
                        if (IsBound(existing.Flags) && parent.Kind != SymbolTableKind.Class &&
                            !IsFreeInAnyChild(comprehension, entry.Name))
                        {
                            childFree.Remove(entry.Name);
                        }
                    }
                }
            }

            private static bool IsSpecialClassFreeName(string name) =>
                name is ClassCellName or ClassDictionaryCellName or ConditionalAnnotationsCellName;

            private static bool IsFreeInAnyChild(MutableTable table, string name)
            {
                foreach (var child in table.Children)
                {
                    if (child.TryGetEntry(name, out var entry) && entry.Scope == SymbolScope.Free)
                        return true;
                }
                return false;
            }

            private static bool IsBound(SymbolFlags flags) =>
                (flags & (SymbolFlags.Local | SymbolFlags.Parameter | SymbolFlags.Imported | SymbolFlags.TypeParameter)) != 0;

            private SymbolTable Freeze(MutableTable mutable, SymbolTable? parent)
            {
                var table = new SymbolTable(mutable.Name, mutable.Kind, parent, mutable.Declaration)
                {
                    IsNested = mutable.IsNested,
                    IsGenerator = mutable.IsGenerator,
                    IsCoroutine = mutable.IsCoroutine,
                    HasVarArgs = mutable.HasVarArgs,
                    HasVarKeywords = mutable.HasVarKeywords,
                    ReturnsValue = mutable.ReturnsValue,
                    NeedsClassClosure = mutable.NeedsClassClosure,
                    NeedsClassDictionary = mutable.NeedsClassDictionary,
                    NeedsConditionalAnnotationsClosure = mutable.NeedsConditionalAnnotationsClosure,
                    CanSeeClassScope = mutable.CanSeeClassScope,
                    HasDocString = mutable.HasDocString,
                    HasAnnotations = mutable.HasAnnotations,
                    HasImportStar = mutable.HasImportStar,
                    HasConditionalAnnotations = mutable.HasConditionalAnnotations,
                    FutureAnnotations = _futureAnnotations,
                    IsMethod = mutable.IsMethod,
                    ComprehensionKind = mutable.ComprehensionKind,
                    IsComprehensionInlined = mutable.IsComprehensionInlined,
                    TypeVariableScopeKind = mutable.TypeVariableScopeKind,
                };

                var children = mutable.Children.Select(child => Freeze(child, table)).ToImmutableArray();
                var symbols = mutable.Entries
                    .Select(entry => new Symbol(
                        entry.Name,
                        entry.Flags,
                        entry.Scope,
                        entry.Occurrences.ToImmutableArray(),
                        mutable.Kind == SymbolTableKind.Module))
                    .ToImmutableArray();
                var diagnostics = mutable.Parent is null ? _diagnostics.ToImmutableArray() : ImmutableArray<SymbolDiagnostic>.Empty;
                table.Complete(children, symbols, mutable.ParameterNames.ToImmutableArray(), diagnostics);
                return table;
            }

            private static bool HasDocString(SyntaxNode? container)
            {
                var firstStatement = GetFirstStatement(container);
                if (firstStatement?.Kind != SyntaxKind.ExpressionStatement)
                    return false;

                var expression = Node(firstStatement, 0);
                if (expression?.Kind == SyntaxKind.LiteralExpression)
                {
                    var token = expression.DescendantTokens()
                        .FirstOrDefault(static token => token.Kind == SyntaxKind.StringToken);
                    return token.Kind == SyntaxKind.StringToken && IsUnicodeStringToken(token);
                }

                if (expression?.Kind != SyntaxKind.StringConcatenationExpression)
                    return false;

                var sawString = false;
                foreach (var token in expression.DescendantTokens())
                {
                    if (token.Kind != SyntaxKind.StringToken || !IsUnicodeStringToken(token))
                        return false;
                    sawString = true;
                }
                return sawString;
            }

            private static bool IsUnicodeStringToken(SyntaxToken token)
            {
                foreach (var character in token.Text)
                {
                    if (character is '\'' or '"')
                        return true;
                    if (character is 'b' or 'B')
                        return false;
                }
                return false;
            }

            private static SyntaxNode? GetFirstStatement(SyntaxNode? container)
            {
                if (container is null)
                    return null;

                if (container.Kind == SyntaxKind.CompilationUnit)
                    return GetFirstStatement(Node(container, 0));

                if (container.Kind == SyntaxKind.Suite)
                    return GetFirstStatement(Node(container, 0)) ?? GetFirstStatement(Node(container, 2));

                if (container.Kind is SyntaxKind.SyntaxList or SyntaxKind.SimpleStatementList)
                {
                    foreach (var child in container.ChildNodes())
                    {
                        var statement = child.Kind == SyntaxKind.SimpleStatementList
                            ? GetFirstStatement(child)
                            : child;
                        if (statement is not null)
                            return statement;
                    }
                    return null;
                }

                return container;
            }

            private static string? GetDottedName(SyntaxNode? node)
            {
                if (node is null)
                    return null;
                var names = node.DescendantTokens()
                    .Where(static token => token.Kind == SyntaxKind.IdentifierToken)
                    .Select(static token => token.Text)
                    .ToArray();
                return names.Length == 0 ? null : string.Join('.', names);
            }

            private static SyntaxToken? FirstToken(SyntaxNode node, SyntaxKind kind)
            {
                foreach (var token in node.DescendantTokens())
                {
                    if (token.Kind == kind)
                        return token;
                }
                return null;
            }

            private static SyntaxNode? Node(SyntaxNode node, int index)
            {
                foreach (var child in node.ChildNodesAndTokens())
                {
                    if (child.IsNode && child.AsNode().Index == index)
                        return child.AsNode();
                }
                return null;
            }

            private static SyntaxToken? Token(SyntaxNode node, int index)
            {
                foreach (var child in node.ChildNodesAndTokens())
                {
                    if (child.IsToken && child.AsToken().Index == index)
                        return child.AsToken();
                }
                return null;
            }

            private static bool IsExpression(SyntaxKind kind)
            {
                var value = (ushort)kind;
                return value >= (ushort)SyntaxKind.NameExpression &&
                    value <= (ushort)SyntaxKind.IfComprehensionClause ||
                    kind is SyntaxKind.ErrorExpression or SyntaxKind.MissingExpression;
            }

            private static bool IsPattern(SyntaxKind kind)
            {
                var value = (ushort)kind;
                return value >= (ushort)SyntaxKind.OrPattern &&
                    value <= (ushort)SyntaxKind.MissingPattern;
            }

            private static SyntaxNode? UnwrapParenthesizedTarget(SyntaxNode? node)
            {
                while (node?.Kind == SyntaxKind.ParenthesizedExpression)
                    node = Node(node, 1);
                return node;
            }

            private static string Mangle(MutableTable table, string identifier)
            {
                if (table.MangledNames is not null && !table.MangledNames.Contains(identifier))
                    return identifier;
                return Mangle(table.PrivateName, identifier);
            }

            private static string Mangle(string? privateName, string identifier)
            {
                if (string.IsNullOrEmpty(privateName) ||
                    identifier.Length < 2 ||
                    identifier[0] != '_' ||
                    identifier[1] != '_' ||
                    identifier.Contains(".", StringComparison.Ordinal) ||
                    (identifier.EndsWith("__", StringComparison.Ordinal)))
                    return identifier;

                var start = 0;
                while (start < privateName.Length && privateName[start] == '_')
                    start++;
                if (start == privateName.Length)
                    return identifier;

                return "_" + privateName[start..] + identifier;
            }

            private enum TargetContext : byte
            {
                Store,
                Delete,
                Augmented,
                ComprehensionStore,
            }
        }

        private sealed class MutableTable
        {
            private readonly Dictionary<string, MutableSymbol> _entriesByName = new(StringComparer.Ordinal);

            public MutableTable(
                string name,
                SymbolTableKind kind,
                MutableTable? parent,
                SyntaxNode declaration,
                string? privateName)
            {
                Name = name;
                Kind = kind;
                Parent = parent;
                Declaration = declaration;
                PrivateName = privateName;
            }

            public string Name { get; }
            public SymbolTableKind Kind { get; }
            public MutableTable? Parent { get; }
            public SyntaxNode Declaration { get; }
            public string? PrivateName { get; set; }
            public HashSet<string>? MangledNames { get; set; }
            public MutableTable? AnnotationBlock { get; set; }
            public List<MutableTable> Children { get; } = [];
            public List<MutableSymbol> Entries { get; } = [];
            public List<string> ParameterNames { get; } = [];

            public bool IsNested { get; set; }
            public bool IsGenerator { get; set; }
            public bool IsCoroutine { get; set; }
            public bool HasVarArgs { get; set; }
            public bool HasVarKeywords { get; set; }
            public bool ReturnsValue { get; set; }
            public bool NeedsClassClosure { get; set; }
            public bool NeedsClassDictionary { get; set; }
            public bool NeedsConditionalAnnotationsClosure { get; set; }
            public bool CanSeeClassScope { get; set; }
            public bool HasDocString { get; set; }
            public bool HasAnnotations { get; set; }
            public bool HasConditionalAnnotations { get; set; }
            public bool IsMethod { get; set; }
            public bool IsComprehension { get; set; }
            public bool IsComprehensionInlined { get; set; }
            public bool HasImportStar { get; set; }
            public bool InConditionalBlock { get; set; }
            public bool InUnevaluatedAnnotation { get; set; }
            public ComprehensionKind ComprehensionKind { get; set; }
            public TypeVariableScopeKind TypeVariableScopeKind { get; set; }

            public MutableSymbol GetOrAdd(string name)
            {
                if (_entriesByName.TryGetValue(name, out var entry))
                    return entry;
                entry = new MutableSymbol(name);
                _entriesByName.Add(name, entry);
                Entries.Add(entry);
                return entry;
            }

            public bool TryGetEntry(string name, out MutableSymbol entry) =>
                _entriesByName.TryGetValue(name, out entry!);

            public void AddParameter(string name, TextSpan span)
            {
                ParameterNames.Add(name);
                GetOrAdd(name).Add(
                    SymbolFlags.Parameter,
                    SymbolOccurrenceKind.Parameter,
                    span);
            }

            public void AddHiddenDefinition(string name) =>
                GetOrAdd(name).Add(
                    SymbolFlags.Local,
                    SymbolOccurrenceKind.Definition,
                    Declaration.Span);

            public void AddHiddenParameter(string name)
            {
                ParameterNames.Add(name);
                GetOrAdd(name).Add(
                    SymbolFlags.Parameter,
                    SymbolOccurrenceKind.Parameter,
                    Declaration.Span);
            }

            public void AddSyntheticUse(string name) =>
                GetOrAdd(name).Add(
                    SymbolFlags.Used,
                    SymbolOccurrenceKind.Reference,
                    Declaration.Span);
        }

        private sealed class MutableSymbol
        {
            public MutableSymbol(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public SymbolFlags Flags { get; set; }
            public SymbolScope Scope { get; set; }
            public List<SymbolOccurrence> Occurrences { get; } = [];
            public TextSpan FirstSpan => Occurrences.Count == 0 ? default : Occurrences[0].Span;

            public void Add(SymbolFlags flags, SymbolOccurrenceKind kind, TextSpan span)
            {
                Flags |= flags;
                Occurrences.Add(new SymbolOccurrence(kind, span));
            }
        }
    }

    internal static class SymbolSyntaxExtensions
    {
        public static IEnumerable<SyntaxNode> DescendantNodeChildren(this SyntaxNode node, SyntaxKind kind)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child.Kind == kind)
                    yield return child;
                else if (IsTransparentContainer(child.Kind))
                {
                    foreach (var descendant in child.DescendantNodeChildren(kind))
                        yield return descendant;
                }
            }
        }

        public static IEnumerable<SyntaxNode> DescendantNodeChildren(this SyntaxNode node)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child.Kind is SyntaxKind.ForComprehensionClause or SyntaxKind.IfComprehensionClause)
                    yield return child;
                else if (IsTransparentContainer(child.Kind))
                {
                    foreach (var descendant in child.DescendantNodeChildren())
                        yield return descendant;
                }
            }
        }

        private static bool IsTransparentContainer(SyntaxKind kind) =>
            kind is SyntaxKind.SyntaxList or SyntaxKind.SeparatedSyntaxList or
                SyntaxKind.ParameterList or SyntaxKind.TypeParameterList or
                SyntaxKind.DecoratorList or SyntaxKind.ComprehensionClauseList;
    }
}
