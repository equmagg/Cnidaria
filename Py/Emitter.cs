using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Cnidaria.Python
{
    /// <summary> Emits baseline CPython 3.14.6 wordcode </summary>
    public static class PythonEmitter
    {
        public static EmitResult Emit(string source, EmitOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Emit(SyntaxTree.Parse(source), options);
        }

        public static EmitResult Emit(SyntaxTree syntaxTree, EmitOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(syntaxTree);
            options ??= new EmitOptions();

            var diagnostics = ImmutableArray.CreateBuilder<EmitDiagnostic>();
            if (options.OptimizationLevel is < 0 or > 2)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidOptions,
                    EmitDiagnosticSeverity.Error,
                    default,
                    $"Optimization level {options.OptimizationLevel} is outside of the supported range 0..2."));
            }
            if (options.FileName is null || options.ModuleName is null)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidOptions,
                    EmitDiagnosticSeverity.Error,
                    default,
                    "FileName and ModuleName must not be null."));
            }
            if (HasErrors(diagnostics))
                return new EmitResult(null, diagnostics.ToImmutable());

            if (options.BytecodeVersion != PythonBytecodeVersion.CPython3_14_6)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.UnsupportedSyntax,
                    EmitDiagnosticSeverity.Error,
                    default,
                    $"Bytecode target {options.BytecodeVersion} is not implemented."));
                return new EmitResult(null, diagnostics.ToImmutable());
            }

            foreach (var diagnostic in syntaxTree.GetDiagnostics())
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.SyntaxTreeContainsErrors,
                    EmitDiagnosticSeverity.Error,
                    diagnostic.Span,
                    diagnostic.Message));
            }

            SymbolTable symbols;
            try
            {
                symbols = syntaxTree.GetSymbolTable(new SymbolTableOptions
                {
                    FutureAnnotations = null,
                    InlineComprehensions = true,
                });
            }
            catch (Exception exception)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InternalEmitterError,
                    EmitDiagnosticSeverity.Error,
                    default,
                    $"Symbol-table construction failed: {exception.Message}"));
                return new EmitResult(null, diagnostics.ToImmutable());
            }

            foreach (var table in symbols.DescendantsAndSelf())
            {
                foreach (var diagnostic in table.Diagnostics)
                {
                    diagnostics.Add(new EmitDiagnostic(
                        EmitDiagnosticCode.SymbolTableContainsErrors,
                        EmitDiagnosticSeverity.Error,
                        diagnostic.Span,
                        diagnostic.Message));
                }
            }

            if (HasErrors(diagnostics))
                return new EmitResult(null, diagnostics.ToImmutable());

            PythonCodeObject? codeObject = null;
            try
            {
                var compiler = new CodeUnitCompiler(
                    syntaxTree,
                    options,
                    symbols,
                    diagnostics);
                codeObject = compiler.CompileModule(syntaxTree.GetRoot());
            }
            catch (Exception exception)
            {
                diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InternalEmitterError,
                    EmitDiagnosticSeverity.Error,
                    default,
                    $"Emitter failed: {exception.Message}"));
            }

            if (HasErrors(diagnostics))
                codeObject = null;

            return new EmitResult(codeObject, diagnostics.ToImmutable());
        }

        private static bool HasErrors(ImmutableArray<EmitDiagnostic>.Builder diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == EmitDiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }

    internal enum AssignmentTargetMode : byte
    {
        Store,
        Delete,
    }

    internal enum CodeUnitKind : byte
    {
        Module,
        Function,
        Class,
        Comprehension,
    }

    internal sealed class LoopContext
    {
        public LoopContext(BytecodeLabel breakTarget, BytecodeLabel continueTarget, bool breakPopsIterator)
        {
            BreakTarget = breakTarget;
            ContinueTarget = continueTarget;
            BreakPopsIterator = breakPopsIterator;
        }

        public BytecodeLabel BreakTarget { get; }
        public BytecodeLabel ContinueTarget { get; }
        public bool BreakPopsIterator { get; }
    }

    internal enum ControlBlockKind : byte
    {
        Loop,
        Finally,
        FinallyHandler,
        ExceptHandler,
    }

    internal sealed class ControlBlock
    {
        private ControlBlock(
            ControlBlockKind kind,
            LoopContext? loop,
            SyntaxNode? body,
            string? exceptionName,
            BytecodeExceptionRegion? exceptionRegion)
        {
            Kind = kind;
            Loop = loop;
            Body = body;
            ExceptionName = exceptionName;
            ExceptionRegion = exceptionRegion;
        }

        public ControlBlockKind Kind { get; }
        public LoopContext? Loop { get; }
        public SyntaxNode? Body { get; }
        public string? ExceptionName { get; }
        public BytecodeExceptionRegion? ExceptionRegion { get; }

        public static ControlBlock ForLoop(LoopContext loop) =>
            new(ControlBlockKind.Loop, loop, null, null, null);

        public static ControlBlock ForFinally(
            SyntaxNode body,
            BytecodeExceptionRegion exceptionRegion) =>
            new(ControlBlockKind.Finally, null, body, null, exceptionRegion);

        public static ControlBlock ForFinallyHandler(
            BytecodeExceptionRegion exceptionRegion) =>
            new(ControlBlockKind.FinallyHandler, null, null, null, exceptionRegion);

        public static ControlBlock ForExceptHandler(
            string? exceptionName,
            BytecodeExceptionRegion exceptionRegion) =>
            new(ControlBlockKind.ExceptHandler, null, null, exceptionName, exceptionRegion);
    }

    internal readonly struct FunctionParameter
    {
        public readonly string Name;
        public readonly SyntaxNode Node;
        public readonly SyntaxKind PrefixKind;
        public readonly SyntaxNode? Annotation;
        public readonly SyntaxNode? DefaultValue;
        public readonly FunctionParameterKind Kind;
        public FunctionParameter(
            string name,
            SyntaxNode node,
            SyntaxKind prefixKind,
            SyntaxNode? annotation,
            SyntaxNode? defaultValue,
            FunctionParameterKind kind)
        {
            Name = name;
            Node = node;
            PrefixKind = prefixKind;
            Annotation = annotation;
            DefaultValue = defaultValue;
            Kind = kind;
        }
    }

    internal enum FunctionParameterKind : byte
    {
        PositionalOnly,
        PositionalOrKeyword,
        KeywordOnly,
        VarArgs,
        VarKeywords,
    }

    internal enum PythonFormatConversion : byte
    {
        None = 0,
        String = 1,
        Representation = 2,
        Ascii = 3,
    }

    internal sealed class FunctionSignature
    {
        public List<FunctionParameter> Parameters { get; } = [];
        public List<FunctionParameter> Positional { get; } = [];
        public List<FunctionParameter> KeywordOnly { get; } = [];
        public FunctionParameter? VarArgs { get; set; }
        public FunctionParameter? VarKeywords { get; set; }
        public int PositionalOnlyCount { get; set; }

        public int ArgumentCount => Positional.Count;
        public int KeywordOnlyCount => KeywordOnly.Count;
    }

    internal sealed class CodeUnitCompiler
    {
        private const int FunctionAttributeDefaults = 0x01;
        private const int FunctionAttributeKeywordDefaults = 0x02;
        private const int FunctionAttributeClosure = 0x08;

        private readonly SyntaxTree _syntaxTree;
        private readonly EmitOptions _options;
        private readonly SymbolTable _rootSymbols;
        private readonly SymbolTable _symbols;
        private readonly ImmutableArray<EmitDiagnostic>.Builder _diagnostics;
        private readonly string? _privateName;
        private readonly BytecodeBuilder _bytecode = new();
        private readonly List<PythonConstant> _constants = [];
        private readonly List<string> _names = [];
        private readonly Dictionary<string, int> _nameIndices = new(StringComparer.Ordinal);
        private readonly List<string> _localsPlusNames = [];
        private readonly List<LocalKind> _localsPlusKinds = [];
        private readonly Dictionary<string, int> _localIndices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _hiddenComprehensionLocalIndices =
            new(StringComparer.Ordinal);
        private readonly Stack<LoopContext> _loops = new();
        private readonly Stack<ControlBlock> _controlBlocks = new();

        private SymbolTable _nameSymbols;
        private CodeUnitKind _codeUnitKind;
        private bool _inInlinedComprehension;
        private bool _isFunction;
        private string _codeName = "<module>";
        private string _qualifiedName = "<module>";
        private int _firstLineNumber;
        private int _argumentCount;
        private int _positionalOnlyArgumentCount;
        private int _keywordOnlyArgumentCount;
        private CodeFlags _flags;

        public CodeUnitCompiler(
            SyntaxTree syntaxTree,
            EmitOptions options,
            SymbolTable rootSymbols,
            ImmutableArray<EmitDiagnostic>.Builder diagnostics,
            SymbolTable? symbols = null)
        {
            _syntaxTree = syntaxTree;
            _options = options;
            _rootSymbols = rootSymbols;
            _symbols = symbols ?? rootSymbols;
            _diagnostics = diagnostics;
            _privateName = GetPrivateName(_symbols);
            _nameSymbols = _symbols;
            _firstLineNumber = Math.Max(1, options.FirstLineNumber);
        }

        public PythonCodeObject? CompileModule(SyntaxNode root)
        {
            _codeUnitKind = CodeUnitKind.Module;
            _isFunction = false;
            _codeName = _options.ModuleName;
            _qualifiedName = _options.ModuleName;
            _flags = _options.EmitNoMonitoringFlag
                ? CodeFlags.NoMonitoring
                : CodeFlags.None;
            if (_symbols.FutureAnnotations)
                _flags |= CodeFlags.FutureAnnotations;

            InitializeHiddenComprehensionLocals();

            var statements = SyntaxAccess.GetNode(root, 0);
            SyntaxNode? docStatement = null;
            var parsedDocString = _options.ReplMode ? null : TryGetDocString(statements, out docStatement);
            var moduleDocString = _options.OptimizationLevel < 2
                ? parsedDocString
                : null;
            if (moduleDocString is not null)
                _constants.Add(moduleDocString);
            else
                EnsureNoneConstant();

            _bytecode.Emit(PythonOpcode.Resume, 0, root.Span);
            if (moduleDocString is not null)
            {
                EmitLoadConstant(moduleDocString, docStatement?.Span ?? root.Span);
                EmitNameStore("__doc__", docStatement?.Span ?? root.Span);
            }
            EmitStatementContainer(statements, allowDocString: true, docStatement);
            EmitLoadConstant(PythonNoneConstant.Instance, root.Span);
            _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: root.Span);

            return BuildCodeObject();
        }

        public PythonCodeObject? CompileFunction(
            SyntaxNode functionNode,
            FunctionSignature signature,
            string name,
            string qualifiedName)
        {
            _codeUnitKind = CodeUnitKind.Function;
            _isFunction = true;
            _codeName = name;
            _qualifiedName = qualifiedName;
            _firstLineNumber = GetLineNumber(functionNode.Span.Start);
            _argumentCount = signature.ArgumentCount;
            _positionalOnlyArgumentCount = signature.PositionalOnlyCount;
            _keywordOnlyArgumentCount = signature.KeywordOnlyCount;
            _flags = CodeFlags.Optimized | CodeFlags.NewLocals;

            if (_symbols.IsNested)
                _flags |= CodeFlags.Nested;
            if (_symbols.FutureAnnotations)
                _flags |= CodeFlags.FutureAnnotations;
            if (_symbols.IsMethod)
                _flags |= CodeFlags.Method;
            if (_options.EmitNoMonitoringFlag)
                _flags |= CodeFlags.NoMonitoring;
            if (signature.VarArgs is not null)
                _flags |= CodeFlags.VarArgs;
            if (signature.VarKeywords is not null)
                _flags |= CodeFlags.VarKeywords;

            if (_symbols.IsCoroutine)
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, functionNode,
                    "Coroutine and async-generator code units are not emitted yet.");
                return null;
            }

            if (_symbols.IsGenerator)
                _flags |= CodeFlags.Generator;

            InitializeFunctionLocals(signature);

            var body = SyntaxAccess.GetNode(functionNode, 11);
            var parsedDocString = TryGetDocString(body, out var docStatement);
            var docString = _options.OptimizationLevel < 2
                ? parsedDocString
                : null;
            if (docString is not null)
            {
                _constants.Add(docString);
                _flags |= CodeFlags.HasDocString;
            }
            else
            {
                EnsureNoneConstant();
            }

            foreach (var cellName in _symbols.CellNames)
            {
                if (!_localIndices.TryGetValue(cellName, out var localIndex))
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, functionNode,
                        $"Cell name {cellName} is missing from locals-plus layout.");
                    continue;
                }
                _bytecode.Emit(PythonOpcode.MakeCell, localIndex, functionNode.Span);
            }

            if (!_symbols.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.CopyFreeVariables,
                    _symbols.FreeNames.Length,
                    functionNode.Span);
            }

            if (_symbols.IsGenerator)
            {
                _bytecode.Emit(PythonOpcode.ReturnGenerator, sourceSpan: functionNode.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: functionNode.Span);
            }

            _bytecode.Emit(PythonOpcode.Resume, 0, functionNode.Span);
            EmitStatementContainer(body, allowDocString: true, docStatement);
            EmitLoadConstant(PythonNoneConstant.Instance, body?.Span ?? functionNode.Span);
            _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: body?.Span ?? functionNode.Span);

            return BuildCodeObject();
        }

        public PythonCodeObject? CompileLambda(
            SyntaxNode lambdaNode,
            FunctionSignature signature,
            string qualifiedName)
        {
            _codeUnitKind = CodeUnitKind.Function;
            _isFunction = true;
            _codeName = "<lambda>";
            _qualifiedName = qualifiedName;
            _firstLineNumber = GetLineNumber(lambdaNode.Span.Start);
            _argumentCount = signature.ArgumentCount;
            _positionalOnlyArgumentCount = signature.PositionalOnlyCount;
            _keywordOnlyArgumentCount = signature.KeywordOnlyCount;
            _flags = CodeFlags.Optimized | CodeFlags.NewLocals;

            if (_symbols.IsNested)
                _flags |= CodeFlags.Nested;
            if (_symbols.FutureAnnotations)
                _flags |= CodeFlags.FutureAnnotations;
            if (_symbols.IsMethod)
                _flags |= CodeFlags.Method;
            if (_options.EmitNoMonitoringFlag)
                _flags |= CodeFlags.NoMonitoring;
            if (signature.VarArgs is not null)
                _flags |= CodeFlags.VarArgs;
            if (signature.VarKeywords is not null)
                _flags |= CodeFlags.VarKeywords;

            if (_symbols.IsCoroutine)
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, lambdaNode,
                    "Coroutine lambda code units are not emitted yet.");
                return null;
            }

            if (_symbols.IsGenerator)
                _flags |= CodeFlags.Generator;

            InitializeFunctionLocals(signature);
            EnsureNoneConstant();

            foreach (var cellName in _symbols.CellNames)
            {
                if (!_localIndices.TryGetValue(cellName, out var localIndex))
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, lambdaNode,
                        $"Cell name {cellName} is missing from locals-plus layout.");
                    continue;
                }
                _bytecode.Emit(PythonOpcode.MakeCell, localIndex, lambdaNode.Span);
            }

            if (!_symbols.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.CopyFreeVariables,
                    _symbols.FreeNames.Length,
                    lambdaNode.Span);
            }

            if (_symbols.IsGenerator)
            {
                _bytecode.Emit(PythonOpcode.ReturnGenerator, sourceSpan: lambdaNode.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: lambdaNode.Span);
            }

            _bytecode.Emit(PythonOpcode.Resume, 0, lambdaNode.Span);
            var body = SyntaxAccess.GetNode(lambdaNode, 3);
            EmitExpression(body);
            _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: body?.Span ?? lambdaNode.Span);

            return BuildCodeObject();
        }

        public PythonCodeObject? CompileClass(
            SyntaxNode classNode,
            string name,
            string qualifiedName)
        {
            _codeUnitKind = CodeUnitKind.Class;
            _isFunction = false;
            _codeName = name;
            _qualifiedName = qualifiedName;
            _firstLineNumber = GetLineNumber(classNode.Span.Start);
            _flags = CodeFlags.None;

            if (_symbols.FutureAnnotations)
                _flags |= CodeFlags.FutureAnnotations;
            if (_options.EmitNoMonitoringFlag)
                _flags |= CodeFlags.NoMonitoring;

            InitializeClassLocals();
            var freeNames = GetClassFreeNames(_symbols);

            foreach (var cellName in GetClassCellNames())
            {
                if (!_localIndices.TryGetValue(cellName, out var localIndex))
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, classNode,
                        $"Class cell {cellName} is missing from locals-plus layout.");
                    continue;
                }
                _bytecode.Emit(PythonOpcode.MakeCell, localIndex, classNode.Span);
            }

            if (freeNames.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.CopyFreeVariables,
                    freeNames.Count,
                    classNode.Span);
            }

            var body = SyntaxAccess.GetNode(classNode, 6);
            var parsedDocString = TryGetDocString(body, out var docStatement);
            var docString = _options.OptimizationLevel < 2
                ? parsedDocString
                : null;
            EnsureNoneConstant();

            _bytecode.Emit(PythonOpcode.Resume, 0, classNode.Span);
            EmitNameLoad("__name__", classNode.Span);
            EmitClassNamespaceStore("__module__", classNode.Span);
            EmitLoadConstant(new StringConstant(qualifiedName), classNode.Span);
            EmitClassNamespaceStore("__qualname__", classNode.Span);
            EmitLoadConstant(new IntegerConstant(_firstLineNumber), classNode.Span);
            EmitClassNamespaceStore("__firstlineno__", classNode.Span);

            if (_symbols.NeedsClassDictionary)
            {
                _bytecode.Emit(PythonOpcode.LoadLocals, sourceSpan: classNode.Span);
                EmitDirectDereferenceStore("__classdict__", classNode.Span);
            }

            if (_symbols.HasConditionalAnnotations)
            {
                _bytecode.Emit(PythonOpcode.BuildSet, 0, classNode.Span);
                EmitDirectDereferenceStore("__conditional_annotations__", classNode.Span);
            }

            if (docString is not null)
            {
                EmitLoadConstant(docString, docStatement?.Span ?? classNode.Span);
                EmitClassNamespaceStore("__doc__", docStatement?.Span ?? classNode.Span);
            }

            EmitStatementContainer(body, allowDocString: true, docStatement);

            var staticAttributeNames = CollectStaticAttributeNames(classNode);
            EmitLoadConstant(
                new TupleConstant(staticAttributeNames
                    .Select(static name => (PythonConstant)new StringConstant(name))
                    .ToArray()),
                classNode.Span);
            EmitClassNamespaceStore("__static_attributes__", classNode.Span);

            if (_symbols.NeedsClassDictionary)
            {
                EmitClosureCellLoad("__classdict__", classNode.Span);
                EmitClassNamespaceStore("__classdictcell__", classNode.Span);
            }

            if (_symbols.NeedsClassClosure)
            {
                EmitClosureCellLoad("__class__", classNode.Span);
                _bytecode.Emit(PythonOpcode.Copy, 1, classNode.Span);
                EmitClassNamespaceStore("__classcell__", classNode.Span);
            }
            else
            {
                EmitLoadConstant(PythonNoneConstant.Instance, classNode.Span);
            }

            _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: classNode.Span);
            return BuildCodeObject();
        }

        public PythonCodeObject? CompileComprehension(
            SyntaxNode expression,
            ComprehensionKind kind,
            string qualifiedName)
        {
            _codeUnitKind = CodeUnitKind.Comprehension;
            _isFunction = true;
            _codeName = kind switch
            {
                ComprehensionKind.List => "<listcomp>",
                ComprehensionKind.Set => "<setcomp>",
                ComprehensionKind.Dictionary => "<dictcomp>",
                _ => "<genexpr>",
            };
            _qualifiedName = qualifiedName;
            _firstLineNumber = GetLineNumber(expression.Span.Start);
            _argumentCount = 1;
            _flags = CodeFlags.Optimized | CodeFlags.NewLocals;

            if (_symbols.IsNested)
                _flags |= CodeFlags.Nested;
            if (_symbols.FutureAnnotations)
                _flags |= CodeFlags.FutureAnnotations;
            if (_options.EmitNoMonitoringFlag)
                _flags |= CodeFlags.NoMonitoring;
            if (kind == ComprehensionKind.Generator)
                _flags |= CodeFlags.Generator;

            if (_symbols.IsCoroutine)
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, expression,
                    "Asynchronous comprehensions are not emitted yet.");
                return null;
            }

            InitializeComprehensionLocals();
            EnsureNoneConstant();

            foreach (var cellName in _symbols.CellNames)
            {
                if (!_localIndices.TryGetValue(cellName, out var localIndex))
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, expression,
                        $"Comprehension cell {cellName} is missing from locals-plus layout.");
                    continue;
                }
                _bytecode.Emit(PythonOpcode.MakeCell, localIndex, expression.Span);
            }

            if (!_symbols.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.CopyFreeVariables,
                    _symbols.FreeNames.Length,
                    expression.Span);
            }

            if (kind == ComprehensionKind.Generator)
            {
                _bytecode.Emit(PythonOpcode.ReturnGenerator, sourceSpan: expression.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
            }

            _bytecode.Emit(PythonOpcode.Resume, 0, expression.Span);
            if (kind == ComprehensionKind.Generator)
            {
                var bodyStart = _bytecode.DefineLabel();
                var bodyEnd = _bytecode.DefineLabel();
                var handler = _bytecode.DefineLabel();

                _bytecode.MarkLabel(bodyStart);
                EmitComprehensionBody(
                    expression,
                    kind,
                    firstIteratorOnStack: false,
                    firstIteratorFromParameter: true);
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: expression.Span);
                _bytecode.MarkLabel(bodyEnd);

                _bytecode.MarkLabel(handler);
                _bytecode.Emit(
                    PythonOpcode.CallIntrinsic1,
                    (int)PythonIntrinsic1.StopIterationError,
                    expression.Span);
                _bytecode.Emit(PythonOpcode.Reraise, 1, expression.Span);
                _bytecode.AddExceptionRegion(
                    bodyStart,
                    bodyEnd,
                    handler,
                    preserveLastInstruction: true);
            }
            else
            {
                EmitComprehensionBody(
                    expression,
                    kind,
                    firstIteratorOnStack: false,
                    firstIteratorFromParameter: true);
                _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: expression.Span);
            }
            return BuildCodeObject();
        }

        private PythonCodeObject? BuildCodeObject()
        {
            BytecodeAssemblyResult assembly;
            try
            {
                assembly = CPython3146Assembler.Assemble(_bytecode, _diagnostics);
            }
            catch (Exception exception)
            {
                _diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidBytecode,
                    EmitDiagnosticSeverity.Error,
                    default,
                    $"Bytecode assembly failed for {_qualifiedName}: {exception.Message}"));
                return null;
            }

            if (HasErrors())
                return null;

            return new PythonCodeObject(
                PythonBytecodeVersion.CPython3_14_6,
                _argumentCount,
                _positionalOnlyArgumentCount,
                _keywordOnlyArgumentCount,
                assembly.StackSize,
                _flags,
                assembly.Bytecode,
                [.. _constants],
                [.. _names],
                [.. _localsPlusNames],
                [.. _localsPlusKinds],
                _options.FileName,
                _codeName,
                _qualifiedName,
                _firstLineNumber,
                lineTable: [],
                exceptionTable: assembly.ExceptionTable);
        }

        private void InitializeFunctionLocals(FunctionSignature signature)
        {
            foreach (var parameter in signature.Positional)
            {
                var kind = LocalKind.Local | LocalKind.PositionalArgument;
                if (parameter.Kind == FunctionParameterKind.PositionalOrKeyword)
                    kind |= LocalKind.KeywordArgument;
                AddOrMergeLocal(MangleName(parameter.Name), kind);
            }

            foreach (var parameter in signature.KeywordOnly)
            {
                AddOrMergeLocal(MangleName(parameter.Name),
                    LocalKind.Local | LocalKind.KeywordArgument);
            }

            if (signature.VarArgs is { } varArgs)
            {
                AddOrMergeLocal(MangleName(varArgs.Name),
                    LocalKind.Local |
                    LocalKind.VariadicArgument |
                    LocalKind.PositionalArgument);
            }

            if (signature.VarKeywords is { } varKeywords)
            {
                AddOrMergeLocal(MangleName(varKeywords.Name),
                    LocalKind.Local |
                    LocalKind.VariadicArgument |
                    LocalKind.KeywordArgument);
            }

            foreach (var local in _symbols.LocalNames)
            {
                AddOrMergeLocal(local, LocalKind.Local);
            }

            foreach (var cell in _symbols.CellNames)
            {
                AddOrMergeLocal(cell, LocalKind.Local | LocalKind.Cell);
            }

            foreach (var free in _symbols.FreeNames)
            {
                AddOrMergeLocal(free, LocalKind.Free);
            }
        }

        private void InitializeClassLocals()
        {
            InitializeHiddenComprehensionLocals();

            foreach (var cell in GetClassCellNames())
                AddClassClosureLocal(cell, LocalKind.Local | LocalKind.Cell);

            foreach (var free in GetClassFreeNames(_symbols))
                AddClassClosureLocal(free, LocalKind.Free);
        }

        private void AddClassClosureLocal(string name, LocalKind kind)
        {
            if (_hiddenComprehensionLocalIndices.ContainsKey(name))
            {
                var index = AddLocalSlot(name, kind);
                _localIndices[name] = index;
                return;
            }

            AddOrMergeLocal(name, kind);
        }

        private void InitializeComprehensionLocals()
        {
            AddOrMergeLocal(
                ".0",
                LocalKind.Local | LocalKind.PositionalArgument);

            foreach (var local in _symbols.LocalNames)
            {
                if (string.Equals(local, ".0", StringComparison.Ordinal))
                    continue;
                AddOrMergeLocal(local, LocalKind.Local);
            }

            foreach (var cell in _symbols.CellNames)
                AddOrMergeLocal(cell, LocalKind.Local | LocalKind.Cell);

            foreach (var free in _symbols.FreeNames)
                AddOrMergeLocal(free, LocalKind.Free);
        }

        private void InitializeHiddenComprehensionLocals()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddSymbols(_symbols.Symbols, includeAll: false);

            foreach (var child in _symbols.Children)
                AddInlinedChild(child, includeAll: _codeUnitKind == CodeUnitKind.Class);

            void AddInlinedChild(SymbolTable table, bool includeAll)
            {
                if (!table.IsComprehensionInlined)
                    return;

                AddSymbols(table.Symbols, includeAll);
                foreach (var child in table.Children)
                    AddInlinedChild(child, includeAll: false);
            }

            void AddSymbols(IEnumerable<Symbol> symbols, bool includeAll)
            {
                foreach (var symbol in symbols)
                {
                    if (!includeAll &&
                        !symbol.IsComprehensionIterationVariable &&
                        (symbol.Flags & SymbolFlags.ComprehensionCell) == 0)
                    {
                        continue;
                    }

                    var kind = LocalKind.Local | LocalKind.Hidden;
                    if (symbol.Scope == SymbolScope.Cell ||
                        (symbol.Flags & SymbolFlags.ComprehensionCell) != 0)
                    {
                        kind |= LocalKind.Cell;
                    }

                    if (!seen.Add(symbol.Name))
                    {
                        var existing = _hiddenComprehensionLocalIndices[symbol.Name];
                        _localsPlusKinds[existing] |= kind;
                        continue;
                    }

                    var index = AddOrMergeLocal(symbol.Name, kind);
                    _hiddenComprehensionLocalIndices[symbol.Name] = index;
                }
            }
        }

        private static IReadOnlyList<string> GetClassFreeNames(SymbolTable table)
        {
            var names = new List<string>(table.FreeNames.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var symbol in table.Symbols)
            {
                if ((symbol.Flags & SymbolFlags.FreeClass) != 0 && seen.Add(symbol.Name))
                    names.Add(symbol.Name);
            }

            foreach (var name in table.FreeNames)
            {
                if (seen.Add(name))
                    names.Add(name);
            }

            return names;
        }

        private IReadOnlyList<string> GetClassCellNames()
        {
            var names = new List<string>(_symbols.CellNames.Length + 3);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in _symbols.CellNames)
            {
                if (seen.Add(name))
                    names.Add(name);
            }

            AddSynthetic("__class__", _symbols.NeedsClassClosure);
            AddSynthetic("__classdict__", _symbols.NeedsClassDictionary);
            AddSynthetic("__conditional_annotations__", _symbols.HasConditionalAnnotations);
            return names;

            void AddSynthetic(string name, bool required)
            {
                if (required && seen.Add(name))
                    names.Add(name);
            }
        }

        private static IReadOnlyList<string> CollectStaticAttributeNames(SyntaxNode classNode)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            Visit(SyntaxAccess.GetNode(classNode, 6));
            return names.ToArray();

            void Visit(SyntaxNode? node)
            {
                if (node is null)
                    return;

                // Attribute stores belong to the nearest lexically enclosing class.
                if (node.Kind == SyntaxKind.ClassDefinition)
                    return;

                switch (node.Kind)
                {
                    case SyntaxKind.AssignmentStatement:
                        {
                            var nodes = SyntaxAccess.GetChildNodes(node);
                            for (var index = 0; index + 1 < nodes.Count; index++)
                                VisitTarget(nodes[index]);
                            break;
                        }

                    case SyntaxKind.AnnotatedAssignmentStatement:
                    case SyntaxKind.AugmentedAssignmentStatement:
                        VisitTarget(SyntaxAccess.GetNode(node, 0));
                        break;

                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForComprehensionClause:
                        VisitTarget(SyntaxAccess.GetNode(node, 2));
                        break;

                    case SyntaxKind.WithItem:
                        VisitTarget(SyntaxAccess.GetNode(node, 2));
                        break;
                }

                foreach (var child in node.ChildNodes())
                    Visit(child);
            }

            void VisitTarget(SyntaxNode? target)
            {
                if (target is null)
                    return;

                switch (target.Kind)
                {
                    case SyntaxKind.AttributeExpression:
                        {
                            var receiver = SyntaxAccess.GetNode(target, 0);
                            if (receiver?.Kind == SyntaxKind.NameExpression &&
                                string.Equals(GetName(receiver), "self", StringComparison.Ordinal))
                            {
                                names.Add(SyntaxAccess.GetToken(target, 2).Text);
                            }
                            return;
                        }

                    case SyntaxKind.ParenthesizedExpression:
                    case SyntaxKind.TupleExpression:
                    case SyntaxKind.ListExpression:
                    case SyntaxKind.StarredExpression:
                    case SyntaxKind.SyntaxList:
                    case SyntaxKind.SeparatedSyntaxList:
                        foreach (var child in target.ChildNodes())
                            VisitTarget(child);
                        return;
                }
            }
        }

        private int AddOrMergeLocal(string name, LocalKind kind)
        {
            if (_localIndices.TryGetValue(name, out var existingIndex))
            {
                _localsPlusKinds[existingIndex] |= kind;
                return existingIndex;
            }

            var index = AddLocalSlot(name, kind);
            _localIndices.Add(name, index);
            return index;
        }

        private int AddLocalSlot(string name, LocalKind kind)
        {
            var index = _localsPlusNames.Count;
            _localsPlusNames.Add(name);
            _localsPlusKinds.Add(kind);
            return index;
        }

        private void EmitStatementContainer(
            SyntaxNode? container,
            bool allowDocString,
            SyntaxNode? knownDocStatement = null)
        {
            if (container is null)
                return;

            switch (container.Kind)
            {
                case SyntaxKind.CompilationUnit:
                    EmitStatementContainer(SyntaxAccess.GetNode(container, 0), allowDocString, knownDocStatement);
                    return;

                case SyntaxKind.SyntaxList:
                case SyntaxKind.SeparatedSyntaxList:
                    foreach (var child in container.ChildNodes())
                    {
                        if (ReferenceEquals(child.Green, knownDocStatement?.Green) &&
                            child.Position == knownDocStatement.Position)
                        {
                            continue;
                        }
                        EmitStatement(child);
                    }
                    return;

                case SyntaxKind.Suite:
                    foreach (var child in container.ChildNodes())
                    {
                        if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SimpleStatementList)
                            EmitStatementContainer(child, allowDocString, knownDocStatement);
                    }
                    return;

                case SyntaxKind.SimpleStatementList:
                    foreach (var child in container.ChildNodes())
                    {
                        if (ReferenceEquals(child.Green, knownDocStatement?.Green) &&
                            child.Position == knownDocStatement.Position)
                        {
                            continue;
                        }
                        EmitStatement(child);
                    }
                    return;

                default:
                    EmitStatement(container);
                    return;
            }
        }

        private void EmitStatement(SyntaxNode statement)
        {
            switch (statement.Kind)
            {
                case SyntaxKind.SimpleStatementList:
                case SyntaxKind.Suite:
                case SyntaxKind.SyntaxList:
                    EmitStatementContainer(statement, allowDocString: false);
                    return;

                case SyntaxKind.ExpressionStatement:
                    EmitExpression(SyntaxAccess.GetNode(statement, 0));
                    if (_options.ReplMode && _codeUnitKind == CodeUnitKind.Module)
                    {
                        _bytecode.Emit(PythonOpcode.CallIntrinsic1, (int)PythonIntrinsic1.Print, statement.Span);
                    }
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: statement.Span);
                    return;

                case SyntaxKind.AssignmentStatement:
                    EmitAssignment(statement);
                    return;

                case SyntaxKind.AnnotatedAssignmentStatement:
                    EmitAnnotatedAssignment(statement);
                    return;

                case SyntaxKind.AugmentedAssignmentStatement:
                    EmitAugmentedAssignment(statement);
                    return;

                case SyntaxKind.ReturnStatement:
                    EmitReturn(statement);
                    return;

                case SyntaxKind.YieldStatement:
                    EmitExpression(SyntaxAccess.GetNode(statement, 0));
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: statement.Span);
                    return;

                case SyntaxKind.RaiseStatement:
                    EmitRaise(statement);
                    return;

                case SyntaxKind.PassStatement:
                case SyntaxKind.GlobalStatement:
                case SyntaxKind.NonlocalStatement:
                    _bytecode.Emit(PythonOpcode.Nop, sourceSpan: statement.Span);
                    return;

                case SyntaxKind.BreakStatement:
                    EmitBreak(statement);
                    return;

                case SyntaxKind.ContinueStatement:
                    EmitContinue(statement);
                    return;

                case SyntaxKind.AssertStatement:
                    EmitAssert(statement);
                    return;

                case SyntaxKind.DeleteStatement:
                    EmitTarget(SyntaxAccess.GetNode(statement, 1), AssignmentTargetMode.Delete);
                    return;

                case SyntaxKind.ImportStatement:
                    EmitImport(statement);
                    return;

                case SyntaxKind.FromImportStatement:
                    EmitFromImport(statement);
                    return;

                case SyntaxKind.IfStatement:
                    EmitIf(statement);
                    return;

                case SyntaxKind.WhileStatement:
                    EmitWhile(statement);
                    return;

                case SyntaxKind.ForStatement:
                    EmitFor(statement);
                    return;

                case SyntaxKind.TryStatement:
                    EmitTry(statement);
                    return;

                case SyntaxKind.FunctionDefinition:
                    EmitFunctionDefinition(statement);
                    return;

                case SyntaxKind.ClassDefinition:
                    EmitClassDefinition(statement);
                    return;

                case SyntaxKind.ErrorStatement:
                case SyntaxKind.SkippedTokens:
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                        $"Cannot emit malformed syntax node {statement.Kind}.");
                    return;

                default:
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                        $"Statement kind {statement.Kind} is not emitted yet.");
                    _bytecode.Emit(PythonOpcode.Nop, sourceSpan: statement.Span);
                    return;
            }
        }

        private void EmitAssignment(SyntaxNode statement)
        {
            var nodes = SyntaxAccess.GetChildNodes(statement);
            if (nodes.Count < 2)
            {
                AddError(EmitDiagnosticCode.InvalidAssignmentTarget, statement,
                    "Assignment does not contain a target and value.");
                return;
            }

            var value = nodes[^1];
            EmitExpression(value);
            for (var i = 0; i < nodes.Count - 1; i++)
            {
                if (i + 1 < nodes.Count - 1)
                    _bytecode.Emit(PythonOpcode.Copy, 1, nodes[i].Span);
                EmitTarget(nodes[i], AssignmentTargetMode.Store);
            }
        }

        private void EmitAnnotatedAssignment(SyntaxNode statement)
        {
            var target = SyntaxAccess.GetNode(statement, 0);
            var annotation = SyntaxAccess.GetNode(statement, 2);
            var value = SyntaxAccess.GetNode(statement, 4);

            if (value is not null)
            {
                EmitExpression(value);
                EmitTarget(target, AssignmentTargetMode.Store);
            }

            if (annotation is not null)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, annotation,
                    "Runtime annotation dictionary emission is not implemented yet.");
            }
        }

        private void EmitAugmentedAssignment(SyntaxNode statement)
        {
            var target = SyntaxAccess.GetNode(statement, 0);
            var operatorToken = SyntaxAccess.GetToken(statement, 1);
            var value = SyntaxAccess.GetNode(statement, 2);
            if (target is null || value is null)
            {
                AddError(EmitDiagnosticCode.InvalidAssignmentTarget, statement,
                    "Augmented assignment is incomplete.");
                return;
            }

            if (!TryMapBinaryOperator(operatorToken.Kind, inPlace: true, out var operation))
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                    $"Augmented operator {operatorToken.Kind} is not supported.");
                return;
            }

            switch (target.Kind)
            {
                case SyntaxKind.NameExpression:
                    EmitNameLoad(GetName(target), target.Span);
                    EmitExpression(value);
                    _bytecode.Emit(PythonOpcode.BinaryOperation, (int)operation, statement.Span);
                    EmitNameStore(GetName(target), target.Span);
                    return;

                case SyntaxKind.AttributeExpression:
                    {
                        var receiver = SyntaxAccess.GetNode(target, 0);
                        var name = MangleName(SyntaxAccess.GetToken(target, 2).Text);
                        EmitExpression(receiver);
                        _bytecode.Emit(PythonOpcode.Copy, 1, target.Span);
                        _bytecode.Emit(PythonOpcode.LoadAttribute, GetNameIndex(name) << 1, target.Span);
                        EmitExpression(value);
                        _bytecode.Emit(PythonOpcode.BinaryOperation, (int)operation, statement.Span);
                        _bytecode.Emit(PythonOpcode.Swap, 2, target.Span);
                        _bytecode.Emit(PythonOpcode.StoreAttribute, GetNameIndex(name), target.Span);
                        return;
                    }

                case SyntaxKind.SubscriptExpression:
                    {
                        EmitExpression(SyntaxAccess.GetNode(target, 0));
                        EmitSubscriptIndex(SyntaxAccess.GetNode(target, 2));
                        _bytecode.Emit(PythonOpcode.Copy, 2, target.Span);
                        _bytecode.Emit(PythonOpcode.Copy, 2, target.Span);
                        _bytecode.Emit(PythonOpcode.BinaryOperation, (int)PythonBinaryOperation.Subscript, target.Span);
                        EmitExpression(value);
                        _bytecode.Emit(PythonOpcode.BinaryOperation, (int)operation, statement.Span);
                        _bytecode.Emit(PythonOpcode.Swap, 3, target.Span);
                        _bytecode.Emit(PythonOpcode.Swap, 2, target.Span);
                        _bytecode.Emit(PythonOpcode.StoreSubscript, sourceSpan: target.Span);
                        return;
                    }

                default:
                    AddError(EmitDiagnosticCode.InvalidAssignmentTarget, target,
                        $"Augmented assignment target {target.Kind} is not supported.");
                    return;
            }
        }

        private void EmitReturn(SyntaxNode statement)
        {
            if (!_isFunction)
            {
                AddError(EmitDiagnosticCode.InvalidControlFlow, statement,
                    "return may only be emitted inside a function code unit.");
                return;
            }

            var expression = SyntaxAccess.GetNode(statement, 1);
            if (expression is null)
                EmitLoadConstant(PythonNoneConstant.Instance, statement.Span);
            else
                EmitExpression(expression);

            EmitControlTransferUnwind(targetLoop: null, preserveTop: true, statement.Span);
            _bytecode.Emit(PythonOpcode.ReturnValue, sourceSpan: statement.Span);
        }

        private void EmitControlTransferUnwind(
            LoopContext? targetLoop,
            bool preserveTop,
            TextSpan span)
        {
            var removed = new List<ControlBlock>();
            var exclusions = new List<(BytecodeExceptionRegion Region, BytecodeLabel Start)>();
            while (_controlBlocks.Count != 0)
            {
                var block = _controlBlocks.Peek();
                if (targetLoop is not null &&
                    block.Kind == ControlBlockKind.Loop &&
                    ReferenceEquals(block.Loop, targetLoop))
                {
                    break;
                }

                removed.Add(_controlBlocks.Pop());
                if (block.ExceptionRegion is not null)
                {
                    var exclusionStart = _bytecode.DefineLabel();
                    _bytecode.MarkLabel(exclusionStart);
                    exclusions.Add((block.ExceptionRegion, exclusionStart));
                }
                EmitControlBlockUnwind(block, preserveTop, span);
            }

            if (exclusions.Count != 0)
            {
                var exclusionEnd = _bytecode.DefineLabel();
                _bytecode.MarkLabel(exclusionEnd);
                foreach (var exclusion in exclusions)
                {
                    _bytecode.AddExceptionExclusion(
                        exclusion.Region,
                        exclusion.Start,
                        exclusionEnd);
                }
            }

            for (var index = removed.Count - 1; index >= 0; index--)
                _controlBlocks.Push(removed[index]);
        }

        private void EmitControlBlockUnwind(
            ControlBlock block,
            bool preserveTop,
            TextSpan span)
        {
            switch (block.Kind)
            {
                case ControlBlockKind.Loop:
                    if (block.Loop!.BreakPopsIterator)
                    {
                        if (preserveTop)
                            _bytecode.Emit(PythonOpcode.Swap, 2, span);
                        _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                    }
                    return;

                case ControlBlockKind.Finally:
                    EmitStatementContainer(block.Body, allowDocString: false);
                    return;

                case ControlBlockKind.FinallyHandler:
                    if (preserveTop)
                    {
                        _bytecode.Emit(PythonOpcode.Swap, 2, span);
                        _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                        _bytecode.Emit(PythonOpcode.Swap, 2, span);
                    }
                    else
                    {
                        _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                    }
                    _bytecode.Emit(PythonOpcode.PopExcept, sourceSpan: span);
                    return;

                case ControlBlockKind.ExceptHandler:
                    if (preserveTop)
                        _bytecode.Emit(PythonOpcode.Swap, 2, span);
                    _bytecode.Emit(PythonOpcode.PopExcept, sourceSpan: span);
                    if (block.ExceptionName is not null)
                        EmitNameClear(block.ExceptionName, span);
                    return;

                default:
                    throw new InvalidOperationException("Unknown control block kind.");
            }
        }

        private void EmitRaise(SyntaxNode statement)
        {
            var exception = SyntaxAccess.GetNode(statement, 1);
            var cause = SyntaxAccess.GetNode(statement, 3);
            if (exception is null)
            {
                _bytecode.Emit(PythonOpcode.RaiseVariableArguments, 0, statement.Span);
                return;
            }

            EmitExpression(exception);
            if (cause is null)
            {
                _bytecode.Emit(PythonOpcode.RaiseVariableArguments, 1, statement.Span);
                return;
            }

            EmitExpression(cause);
            _bytecode.Emit(PythonOpcode.RaiseVariableArguments, 2, statement.Span);
        }

        private void EmitBreak(SyntaxNode statement)
        {
            if (_loops.Count == 0)
            {
                AddError(EmitDiagnosticCode.InvalidControlFlow, statement,
                    "break is not inside a loop.");
                return;
            }

            var loop = _loops.Peek();
            EmitControlTransferUnwind(loop, preserveTop: false, statement.Span);
            if (loop.BreakPopsIterator)
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: statement.Span);
            _bytecode.EmitJump(loop.BreakTarget, sourceSpan: statement.Span);
        }

        private void EmitContinue(SyntaxNode statement)
        {
            if (_loops.Count == 0)
            {
                AddError(EmitDiagnosticCode.InvalidControlFlow, statement,
                    "continue is not inside a loop.");
                return;
            }

            var loop = _loops.Peek();
            EmitControlTransferUnwind(loop, preserveTop: false, statement.Span);
            _bytecode.EmitJump(loop.ContinueTarget, noInterrupt: true, sourceSpan: statement.Span);
        }

        private void EmitAssert(SyntaxNode statement)
        {
            if (_options.OptimizationLevel > 0)
                return;

            var end = _bytecode.DefineLabel();
            EmitExpression(SyntaxAccess.GetNode(statement, 1));
            _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: statement.Span);
            _bytecode.EmitConditionalJump(PythonOpcode.PopJumpIfTrue, end, statement.Span);

            _bytecode.Emit(PythonOpcode.LoadCommonConstant, 0, statement.Span);
            var message = SyntaxAccess.GetNode(statement, 3);
            if (message is not null)
            {
                EmitExpression(message);
                _bytecode.Emit(PythonOpcode.Call, 0, statement.Span);
            }

            _bytecode.Emit(PythonOpcode.RaiseVariableArguments, 1, statement.Span);
            _bytecode.MarkLabel(end);
        }

        private void EmitIf(SyntaxNode statement)
        {
            var end = _bytecode.DefineLabel();
            var next = _bytecode.DefineLabel();

            EmitConditionJumpFalse(SyntaxAccess.GetNode(statement, 1), next, statement.Span);
            EmitStatementContainer(SyntaxAccess.GetNode(statement, 3), allowDocString: false);
            _bytecode.EmitJump(end, sourceSpan: statement.Span);
            _bytecode.MarkLabel(next);

            var clauses = SyntaxAccess.GetNode(statement, 4);
            if (clauses is not null)
            {
                var clauseNodes = clauses.ChildNodes().ToList();
                for (var i = 0; i < clauseNodes.Count; i++)
                {
                    var clause = clauseNodes[i];
                    if (clause.Kind == SyntaxKind.ElifClause)
                    {
                        var afterElif = _bytecode.DefineLabel();
                        EmitConditionJumpFalse(SyntaxAccess.GetNode(clause, 1), afterElif, clause.Span);
                        EmitStatementContainer(SyntaxAccess.GetNode(clause, 3), allowDocString: false);
                        _bytecode.EmitJump(end, sourceSpan: clause.Span);
                        _bytecode.MarkLabel(afterElif);
                    }
                    else if (clause.Kind == SyntaxKind.ElseClause)
                    {
                        EmitStatementContainer(SyntaxAccess.GetNode(clause, 2), allowDocString: false);
                    }
                }
            }

            _bytecode.MarkLabel(end);
        }

        private void EmitWhile(SyntaxNode statement)
        {
            var conditionLabel = _bytecode.DefineLabel();
            var elseLabel = _bytecode.DefineLabel();
            var endLabel = _bytecode.DefineLabel();

            _bytecode.MarkLabel(conditionLabel);
            EmitConditionJumpFalse(SyntaxAccess.GetNode(statement, 1), elseLabel, statement.Span);
            var loop = new LoopContext(endLabel, conditionLabel, breakPopsIterator: false);
            _loops.Push(loop);
            var loopBlock = ControlBlock.ForLoop(loop);
            _controlBlocks.Push(loopBlock);
            EmitStatementContainer(SyntaxAccess.GetNode(statement, 3), allowDocString: false);
            if (!ReferenceEquals(_controlBlocks.Pop(), loopBlock) || !ReferenceEquals(_loops.Pop(), loop))
                throw new InvalidOperationException("Loop-control stack is corrupt.");
            _bytecode.EmitJump(conditionLabel, noInterrupt: true, sourceSpan: statement.Span);

            _bytecode.MarkLabel(elseLabel);
            var elseClause = SyntaxAccess.GetNode(statement, 4);
            if (elseClause is not null)
                EmitStatementContainer(SyntaxAccess.GetNode(elseClause, 2), allowDocString: false);
            _bytecode.MarkLabel(endLabel);
        }

        private void EmitFor(SyntaxNode statement)
        {
            if (SyntaxAccess.GetToken(statement, 0).Kind == SyntaxKind.AsyncKeyword)
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, statement,
                    "async for is not emitted yet.");
                return;
            }

            var iteratorLabel = _bytecode.DefineLabel();
            var cleanupLabel = _bytecode.DefineLabel();
            var endLabel = _bytecode.DefineLabel();

            EmitExpression(SyntaxAccess.GetNode(statement, 4));
            _bytecode.Emit(PythonOpcode.GetIterator, sourceSpan: statement.Span);
            _bytecode.MarkLabel(iteratorLabel);
            _bytecode.EmitForIterator(cleanupLabel, statement.Span);
            EmitTarget(SyntaxAccess.GetNode(statement, 2), AssignmentTargetMode.Store);

            var loop = new LoopContext(endLabel, iteratorLabel, breakPopsIterator: true);
            _loops.Push(loop);
            var loopBlock = ControlBlock.ForLoop(loop);
            _controlBlocks.Push(loopBlock);
            EmitStatementContainer(SyntaxAccess.GetNode(statement, 6), allowDocString: false);
            if (!ReferenceEquals(_controlBlocks.Pop(), loopBlock) || !ReferenceEquals(_loops.Pop(), loop))
                throw new InvalidOperationException("Loop-control stack is corrupt.");
            _bytecode.EmitJump(iteratorLabel, noInterrupt: true, sourceSpan: statement.Span);

            _bytecode.MarkLabel(cleanupLabel);
            _bytecode.Emit(PythonOpcode.EndFor, sourceSpan: statement.Span);
            _bytecode.Emit(PythonOpcode.PopIterator, sourceSpan: statement.Span);

            var elseClause = SyntaxAccess.GetNode(statement, 7);
            if (elseClause is not null)
                EmitStatementContainer(SyntaxAccess.GetNode(elseClause, 2), allowDocString: false);

            _bytecode.MarkLabel(endLabel);
        }

        private void EmitTry(SyntaxNode statement)
        {
            var clausesNode = SyntaxAccess.GetNode(statement, 3);
            var exceptClauses = new List<SyntaxNode>();
            SyntaxNode? elseClause = null;
            SyntaxNode? finallyClause = null;

            if (clausesNode is not null)
            {
                foreach (var clause in clausesNode.ChildNodes())
                {
                    switch (clause.Kind)
                    {
                        case SyntaxKind.ExceptClause:
                            exceptClauses.Add(clause);
                            break;
                        case SyntaxKind.ExceptStarClause:
                            AddError(EmitDiagnosticCode.UnsupportedSyntax, clause,
                                "except* and exception groups are not emitted yet.");
                            return;
                        case SyntaxKind.ElseClause:
                            elseClause = clause;
                            break;
                        case SyntaxKind.FinallyClause:
                            finallyClause = clause;
                            break;
                    }
                }
            }

            if (finallyClause is not null)
            {
                var finallyBody = SyntaxAccess.GetNode(finallyClause, 2)!;
                EmitTryFinally(
                    statement,
                    finallyBody,
                    () => EmitTryExceptCore(statement, exceptClauses, elseClause));
                return;
            }

            EmitTryExceptCore(statement, exceptClauses, elseClause);
        }

        private void EmitTryFinally(
            SyntaxNode statement,
            SyntaxNode finallyBody,
            Action emitProtectedBody)
        {
            var protectedStart = _bytecode.DefineLabel();
            var protectedEnd = _bytecode.DefineLabel();
            var exceptionalFinally = _bytecode.DefineLabel();
            var cleanupStart = _bytecode.DefineLabel();
            var cleanupEnd = _bytecode.DefineLabel();
            var cleanupHandler = _bytecode.DefineLabel();
            var end = _bytecode.DefineLabel();

            var protectedRegion = _bytecode.AddExceptionRegion(
                protectedStart,
                protectedEnd,
                exceptionalFinally,
                stackDepthAdjustment: 0,
                preserveLastInstruction: false);
            var cleanupRegion = _bytecode.AddExceptionRegion(
                cleanupStart,
                cleanupEnd,
                cleanupHandler,
                stackDepthAdjustment: -1,
                preserveLastInstruction: true);
            _bytecode.MarkLabel(protectedStart);
            var block = ControlBlock.ForFinally(finallyBody, protectedRegion);
            _controlBlocks.Push(block);
            emitProtectedBody();
            if (!ReferenceEquals(_controlBlocks.Pop(), block))
                throw new InvalidOperationException("Control-block stack is corrupt.");
            _bytecode.MarkLabel(protectedEnd);

            EmitStatementContainer(finallyBody, allowDocString: false);
            _bytecode.EmitJump(end, sourceSpan: statement.Span);

            _bytecode.MarkLabel(exceptionalFinally);
            _bytecode.Emit(PythonOpcode.PushExceptionInfo, sourceSpan: statement.Span);
            _bytecode.MarkLabel(cleanupStart);
            var handlerBlock = ControlBlock.ForFinallyHandler(cleanupRegion);
            _controlBlocks.Push(handlerBlock);
            EmitStatementContainer(finallyBody, allowDocString: false);
            if (!ReferenceEquals(_controlBlocks.Pop(), handlerBlock))
                throw new InvalidOperationException("Control-block stack is corrupt.");
            _bytecode.Emit(PythonOpcode.Reraise, 0, statement.Span);
            _bytecode.MarkLabel(cleanupEnd);

            _bytecode.MarkLabel(cleanupHandler);
            EmitExceptionCleanup(statement.Span);
            _bytecode.MarkLabel(end);
        }

        private void EmitTryExceptCore(
            SyntaxNode statement,
            IReadOnlyList<SyntaxNode> exceptClauses,
            SyntaxNode? elseClause)
        {
            if (exceptClauses.Count == 0)
            {
                EmitStatementContainer(SyntaxAccess.GetNode(statement, 2), allowDocString: false);
                return;
            }

            var tryStart = _bytecode.DefineLabel();
            var tryEnd = _bytecode.DefineLabel();
            var exceptionHandler = _bytecode.DefineLabel();
            var handlerCleanupStart = _bytecode.DefineLabel();
            var handlerCleanupEnd = _bytecode.DefineLabel();
            var cleanupHandler = _bytecode.DefineLabel();
            var end = _bytecode.DefineLabel();

            _bytecode.MarkLabel(tryStart);
            EmitStatementContainer(SyntaxAccess.GetNode(statement, 2), allowDocString: false);
            _bytecode.MarkLabel(tryEnd);

            if (elseClause is not null)
                EmitStatementContainer(SyntaxAccess.GetNode(elseClause, 2), allowDocString: false);
            _bytecode.EmitJump(end, sourceSpan: statement.Span);

            _bytecode.MarkLabel(exceptionHandler);
            _bytecode.Emit(PythonOpcode.PushExceptionInfo, sourceSpan: statement.Span);
            _bytecode.MarkLabel(handlerCleanupStart);
            var handlerCleanupRegion = _bytecode.AddExceptionRegion(
                handlerCleanupStart,
                handlerCleanupEnd,
                cleanupHandler,
                stackDepthAdjustment: -1,
                preserveLastInstruction: true);

            foreach (var clause in exceptClauses)
            {
                var exceptionType = SyntaxAccess.GetNode(clause, 2);
                var nextClause = _bytecode.DefineLabel();
                if (exceptionType is not null)
                {
                    EmitExpression(exceptionType);
                    _bytecode.Emit(PythonOpcode.CheckExceptionMatch, sourceSpan: clause.Span);
                    _bytecode.EmitConditionalJump(PythonOpcode.PopJumpIfFalse, nextClause, clause.Span);
                }

                var nameToken = SyntaxAccess.GetToken(clause, 4);
                var exceptionName = nameToken.Kind == SyntaxKind.IdentifierToken
                    ? nameToken.Text
                    : null;
                if (exceptionName is null)
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: clause.Span);
                else
                    EmitNameStore(exceptionName, clause.Span);

                var bodyStart = _bytecode.DefineLabel();
                var bodyEnd = _bytecode.DefineLabel();
                var nameCleanupHandler = exceptionName is null
                    ? null
                    : _bytecode.DefineLabel();
                var bodyRegion = _bytecode.AddExceptionRegion(
                    bodyStart,
                    bodyEnd,
                    nameCleanupHandler ?? cleanupHandler,
                    stackDepthAdjustment: 0,
                    preserveLastInstruction: true);
                _bytecode.MarkLabel(bodyStart);
                var handlerBlock = ControlBlock.ForExceptHandler(exceptionName, bodyRegion);
                _controlBlocks.Push(handlerBlock);
                EmitStatementContainer(SyntaxAccess.GetNode(clause, 6), allowDocString: false);
                if (!ReferenceEquals(_controlBlocks.Pop(), handlerBlock))
                    throw new InvalidOperationException("Control-block stack is corrupt.");
                _bytecode.MarkLabel(bodyEnd);
                _bytecode.AddExceptionExclusion(handlerCleanupRegion, bodyStart, bodyEnd);

                var normalCleanupStart = _bytecode.DefineLabel();
                var normalCleanupEnd = _bytecode.DefineLabel();
                _bytecode.MarkLabel(normalCleanupStart);
                _bytecode.Emit(PythonOpcode.PopExcept, sourceSpan: clause.Span);
                if (exceptionName is not null)
                    EmitNameClear(exceptionName, clause.Span);
                _bytecode.EmitJump(end, sourceSpan: clause.Span);
                _bytecode.MarkLabel(normalCleanupEnd);
                _bytecode.AddExceptionExclusion(
                    handlerCleanupRegion,
                    normalCleanupStart,
                    normalCleanupEnd);

                if (nameCleanupHandler is not null)
                {
                    _bytecode.MarkLabel(nameCleanupHandler);
                    EmitNameClear(exceptionName!, clause.Span);
                    _bytecode.Emit(PythonOpcode.Reraise, 1, clause.Span);
                }

                _bytecode.MarkLabel(nextClause);

                if (exceptionType is null)
                    break;
            }

            _bytecode.Emit(PythonOpcode.Reraise, 0, statement.Span);
            _bytecode.MarkLabel(handlerCleanupEnd);

            _bytecode.MarkLabel(cleanupHandler);
            EmitExceptionCleanup(statement.Span);
            _bytecode.MarkLabel(end);

            _bytecode.AddExceptionRegion(
                tryStart,
                tryEnd,
                exceptionHandler,
                stackDepthAdjustment: 0,
                preserveLastInstruction: false);
        }

        private void EmitExceptionCleanup(TextSpan span)
        {
            _bytecode.Emit(PythonOpcode.Copy, 3, span);
            _bytecode.Emit(PythonOpcode.PopExcept, sourceSpan: span);
            _bytecode.Emit(PythonOpcode.Reraise, 1, span);
        }

        private void EmitNameClear(string name, TextSpan span)
        {
            EmitLoadConstant(PythonNoneConstant.Instance, span);
            EmitNameStore(name, span);
            EmitNameDelete(name, span);
        }

        private void EmitImport(SyntaxNode statement)
        {
            var aliases = SyntaxAccess.GetNode(statement, 1);
            if (aliases is null)
                return;

            foreach (var alias in aliases.ChildNodes())
            {
                if (alias.Kind != SyntaxKind.ImportAlias)
                    continue;

                var dotted = SyntaxAccess.GetNode(alias, 0);
                var fullName = GetDottedName(dotted);
                var aliasToken = SyntaxAccess.GetToken(alias, 2);

                EmitLoadConstant(new IntegerConstant(0), alias.Span);
                EmitLoadConstant(PythonNoneConstant.Instance, alias.Span);
                _bytecode.Emit(
                    PythonOpcode.ImportName,
                    GetNameIndex(fullName),
                    alias.Span);

                if (aliasToken.Kind != SyntaxKind.IdentifierToken)
                {
                    EmitNameStore(fullName.Split('.')[0], alias.Span);
                    continue;
                }

                var parts = fullName.Split('.');
                if (parts.Length == 1)
                {
                    EmitNameStore(aliasToken.Text, alias.Span);
                    continue;
                }

                for (var i = 1; i < parts.Length; i++)
                {
                    _bytecode.Emit(PythonOpcode.ImportFrom, GetNameIndex(parts[i]), alias.Span);
                    if (i + 1 < parts.Length)
                    {
                        _bytecode.Emit(PythonOpcode.Swap, 2, alias.Span);
                        _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: alias.Span);
                    }
                }

                EmitNameStore(aliasToken.Text, alias.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: alias.Span);
            }
        }

        private void EmitFromImport(SyntaxNode statement)
        {
            var moduleParts = SyntaxAccess.GetNode(statement, 1);
            var level = 0;
            var moduleName = string.Empty;
            if (moduleParts is not null)
            {
                foreach (var child in moduleParts.ChildNodesAndTokens())
                {
                    if (child.IsToken)
                    {
                        level += child.Kind == SyntaxKind.EllipsisToken ? 3 : 1;
                    }
                    else if (child.AsNode().Kind == SyntaxKind.DottedName)
                    {
                        moduleName = GetDottedName(child.AsNode());
                    }
                }
            }

            if (level == 0 && string.Equals(moduleName, "__future__", StringComparison.Ordinal))
            {
                // Future statements affect compiler flags and do not execute a runtime import.
                _bytecode.Emit(PythonOpcode.Nop, sourceSpan: statement.Span);
                return;
            }

            var targetNode = SyntaxAccess.GetNode(statement, 3);
            if (targetNode is null)
            {
                var star = SyntaxAccess.GetToken(statement, 3);
                if (star.Kind == SyntaxKind.StarToken)
                {
                    EmitLoadConstant(new IntegerConstant(level), statement.Span);
                    EmitLoadConstant(
                        new TupleConstant([new StringConstant("*")]),
                        statement.Span);
                    _bytecode.Emit(
                        PythonOpcode.ImportName,
                        GetNameIndex(moduleName),
                        statement.Span);
                    _bytecode.Emit(
                        PythonOpcode.CallIntrinsic1,
                        (int)PythonIntrinsic1.ImportStar,
                        statement.Span);
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: statement.Span);
                }
                return;
            }

            var aliases = UnwrapImportAliases(targetNode);
            var importedNames = new List<PythonConstant>();
            foreach (var alias in aliases)
            {
                var imported = SyntaxAccess.GetToken(alias, 0).Text;
                importedNames.Add(new StringConstant(imported));
            }

            EmitLoadConstant(new IntegerConstant(level), statement.Span);
            EmitLoadConstant(new TupleConstant(importedNames), statement.Span);
            _bytecode.Emit(
                PythonOpcode.ImportName,
                GetNameIndex(moduleName),
                statement.Span);

            foreach (var alias in aliases)
            {
                var imported = SyntaxAccess.GetToken(alias, 0).Text;
                var aliasToken = SyntaxAccess.GetToken(alias, 2);
                var binding = aliasToken.Kind == SyntaxKind.IdentifierToken
                    ? aliasToken.Text
                    : imported;
                _bytecode.Emit(
                    PythonOpcode.ImportFrom,
                    GetNameIndex(imported),
                    alias.Span);
                EmitNameStore(binding, alias.Span);
            }

            _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: statement.Span);
        }

        private static List<SyntaxNode> UnwrapImportAliases(SyntaxNode targetNode)
        {
            if (targetNode.Kind == SyntaxKind.SeparatedSyntaxList)
                return targetNode.ChildNodes().Where(static n => n.Kind == SyntaxKind.ImportAlias).ToList();

            foreach (var child in targetNode.ChildNodes())
            {
                if (child.Kind == SyntaxKind.SeparatedSyntaxList)
                    return child.ChildNodes().Where(static n => n.Kind == SyntaxKind.ImportAlias).ToList();
            }

            return [];
        }

        private void EmitFunctionDefinition(SyntaxNode statement)
        {
            var decorators = SyntaxAccess.GetNode(statement, 0)?
                .DescendantNodeChildren(SyntaxKind.Decorator)
                .ToArray() ?? [];
            foreach (var decorator in decorators)
                EmitExpression(SyntaxAccess.GetNode(decorator, 1));

            if (SyntaxAccess.GetToken(statement, 1).Kind == SyntaxKind.AsyncKeyword)
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, statement,
                    "async def is not emitted yet.");
                EmitLoadConstant(PythonNoneConstant.Instance, statement.Span);
                EmitNameStore(SyntaxAccess.GetToken(statement, 3).Text, statement.Span);
                return;
            }

            if (SyntaxAccess.GetNode(statement, 4) is not null)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                    "PEP 695 type parameters are not emitted yet.");
            }

            if (SyntaxAccess.GetNode(statement, 9) is not null)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                    "Function return annotations are not emitted yet.");
            }

            var signature = ParseFunctionSignature(SyntaxAccess.GetNode(statement, 6));
            foreach (var parameter in signature.Parameters)
            {
                if (parameter.Annotation is not null)
                {
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, parameter.Annotation,
                        "Function parameter annotations are not emitted yet.");
                }
            }

            var positionalDefaults = signature.Positional
                .Where(static parameter => parameter.DefaultValue is not null)
                .ToList();
            if (positionalDefaults.Count != 0)
            {
                foreach (var parameter in positionalDefaults)
                    EmitExpression(parameter.DefaultValue);
                _bytecode.Emit(PythonOpcode.BuildTuple, positionalDefaults.Count, statement.Span);
            }

            var keywordDefaults = signature.KeywordOnly
                .Where(static parameter => parameter.DefaultValue is not null)
                .ToList();
            if (keywordDefaults.Count != 0)
            {
                foreach (var parameter in keywordDefaults)
                {
                    EmitLoadConstant(
                        new StringConstant(MangleName(parameter.Name)),
                        parameter.Node.Span);
                    EmitExpression(parameter.DefaultValue);
                }
                _bytecode.Emit(PythonOpcode.BuildMap, keywordDefaults.Count, statement.Span);
            }

            var name = SyntaxAccess.GetToken(statement, 3).Text;
            var qualifiedName = GetNestedQualifiedName(name);

            var table = _rootSymbols.FindTable(statement, SymbolTableKind.Function);
            if (table is null)
            {
                AddError(EmitDiagnosticCode.InternalEmitterError, statement,
                    $"No symbol table was found for function {name}.");
                EmitLoadConstant(PythonNoneConstant.Instance, statement.Span);
                EmitNameStore(name, statement.Span);
                return;
            }

            var nestedCompiler = new CodeUnitCompiler(
                _syntaxTree,
                _options,
                _rootSymbols,
                _diagnostics,
                table);
            var nestedCode = nestedCompiler.CompileFunction(statement, signature, name, qualifiedName);
            if (nestedCode is null)
            {
                EmitLoadConstant(PythonNoneConstant.Instance, statement.Span);
                EmitNameStore(name, statement.Span);
                return;
            }

            if (!table.FreeNames.IsEmpty)
            {
                foreach (var freeName in table.FreeNames)
                    EmitClosureCellLoad(freeName, statement.Span);
                _bytecode.Emit(PythonOpcode.BuildTuple, table.FreeNames.Length, statement.Span);
            }

            EmitLoadConstant(new CodeConstant(nestedCode), statement.Span);
            _bytecode.Emit(PythonOpcode.MakeFunction, sourceSpan: statement.Span);

            if (!table.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeClosure,
                    statement.Span);
            }

            if (keywordDefaults.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeKeywordDefaults,
                    statement.Span);
            }

            if (positionalDefaults.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeDefaults,
                    statement.Span);
            }

            for (var i = 0; i < decorators.Length; i++)
                _bytecode.Emit(PythonOpcode.Call, 0, statement.Span);

            EmitNameStore(name, statement.Span);
        }

        private void EmitClassDefinition(SyntaxNode statement)
        {
            var decorators = SyntaxAccess.GetNode(statement, 0)?
                .DescendantNodeChildren(SyntaxKind.Decorator)
                .ToArray() ?? [];

            if (SyntaxAccess.GetNode(statement, 3) is not null)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, statement,
                    "PEP 695 class type parameters are not emitted yet.");
                return;
            }

            var name = SyntaxAccess.GetToken(statement, 2).Text;
            var qualifiedName = GetNestedQualifiedName(name);
            var table = _rootSymbols.FindTable(statement, SymbolTableKind.Class);
            if (table is null)
            {
                AddError(EmitDiagnosticCode.InternalEmitterError, statement,
                    $"No symbol table was found for class {name}.");
                return;
            }

            var nestedCompiler = new CodeUnitCompiler(
                _syntaxTree,
                _options,
                _rootSymbols,
                _diagnostics,
                table);
            var nestedCode = nestedCompiler.CompileClass(statement, name, qualifiedName);
            if (nestedCode is null)
                return;

            foreach (var decorator in decorators)
                EmitExpression(SyntaxAccess.GetNode(decorator, 1));

            _bytecode.Emit(PythonOpcode.LoadBuildClass, sourceSpan: statement.Span);
            _bytecode.Emit(PythonOpcode.PushNull, sourceSpan: statement.Span);

            var freeNames = GetClassFreeNames(table);
            if (freeNames.Count != 0)
            {
                foreach (var freeName in freeNames)
                    EmitClosureCellLoad(freeName, statement.Span);
                _bytecode.Emit(PythonOpcode.BuildTuple, freeNames.Count, statement.Span);
            }

            EmitLoadConstant(new CodeConstant(nestedCode), statement.Span);
            _bytecode.Emit(PythonOpcode.MakeFunction, sourceSpan: statement.Span);
            if (freeNames.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeClosure,
                    statement.Span);
            }

            EmitLoadConstant(new StringConstant(name), statement.Span);
            EmitClassConstructionCall(SyntaxAccess.GetNode(statement, 4), statement.Span);

            for (var i = 0; i < decorators.Length; i++)
                _bytecode.Emit(PythonOpcode.Call, 0, statement.Span);

            EmitNameStore(name, statement.Span);
        }

        private void EmitClassConstructionCall(SyntaxNode? argumentList, TextSpan span)
        {
            var separated = argumentList is null
                ? null
                : SyntaxAccess.GetNode(argumentList, 1);
            var arguments = separated?.ChildNodes().ToList() ?? [];
            var positionalArguments = arguments
                .Where(static argument => argument.Kind is not (
                    SyntaxKind.KeywordArgument or SyntaxKind.DoubleStarredExpression))
                .ToList();
            var keywordArguments = arguments
                .Where(static argument => argument.Kind is
                    SyntaxKind.KeywordArgument or SyntaxKind.DoubleStarredExpression)
                .ToList();

            ValidateExplicitKeywordArguments(keywordArguments);

            var expanded = positionalArguments.Any(static argument =>
                    argument.Kind == SyntaxKind.StarredExpression) ||
                keywordArguments.Any(static argument =>
                    argument.Kind == SyntaxKind.DoubleStarredExpression);
            if (!expanded)
            {
                foreach (var argument in positionalArguments)
                    EmitExpression(argument);

                var keywordNames = new List<PythonConstant>(keywordArguments.Count);
                foreach (var argument in keywordArguments)
                {
                    keywordNames.Add(new StringConstant(SyntaxAccess.GetToken(argument, 0).Text));
                    EmitExpression(SyntaxAccess.GetNode(argument, 2));
                }

                var total = checked(2 + positionalArguments.Count + keywordArguments.Count);
                if (keywordNames.Count == 0)
                {
                    _bytecode.Emit(PythonOpcode.Call, total, span);
                }
                else
                {
                    EmitLoadConstant(new TupleConstant(keywordNames), span);
                    _bytecode.Emit(PythonOpcode.CallKeyword, total, span);
                }
                return;
            }

            // The class body function and class name are the first two positional
            // arguments to __build_class__. They are already on the value stack.
            _bytecode.Emit(PythonOpcode.BuildList, 2, span);
            foreach (var argument in positionalArguments)
            {
                if (argument.Kind == SyntaxKind.StarredExpression)
                {
                    EmitExpression(SyntaxAccess.GetNode(argument, 1));
                    _bytecode.Emit(PythonOpcode.ListExtend, 1, argument.Span);
                }
                else
                {
                    EmitExpression(argument);
                    _bytecode.Emit(PythonOpcode.ListAppend, 1, argument.Span);
                }
            }
            _bytecode.Emit(
                PythonOpcode.CallIntrinsic1,
                (int)PythonIntrinsic1.ListToTuple,
                span);

            if (keywordArguments.Count == 0)
                _bytecode.Emit(PythonOpcode.PushNull, sourceSpan: span);
            else
                EmitExpandedKeywordArguments(keywordArguments, span);

            _bytecode.Emit(PythonOpcode.CallFunctionEx, sourceSpan: span);
        }

        private string GetNestedQualifiedName(string name)
        {
            if (_nameSymbols.Lookup(MangleName(name))?.Scope == SymbolScope.GlobalExplicit)
                return name;

            return _codeUnitKind switch
            {
                CodeUnitKind.Module => name,
                CodeUnitKind.Class => $"{_qualifiedName}.{name}",
                _ => $"{_qualifiedName}.<locals>.{name}",
            };
        }

        private string GetComprehensionQualifiedName(ComprehensionKind kind)
        {
            var name = kind switch
            {
                ComprehensionKind.List => "<listcomp>",
                ComprehensionKind.Set => "<setcomp>",
                ComprehensionKind.Dictionary => "<dictcomp>",
                _ => "<genexpr>",
            };
            return GetNestedQualifiedName(name);
        }

        private void EmitLambda(SyntaxNode expression)
        {
            var signature = ParseFunctionSignature(SyntaxAccess.GetNode(expression, 1));

            var positionalDefaults = signature.Positional
                .Where(static parameter => parameter.DefaultValue is not null)
                .ToList();
            if (positionalDefaults.Count != 0)
            {
                foreach (var parameter in positionalDefaults)
                    EmitExpression(parameter.DefaultValue);
                _bytecode.Emit(PythonOpcode.BuildTuple, positionalDefaults.Count, expression.Span);
            }

            var keywordDefaults = signature.KeywordOnly
                .Where(static parameter => parameter.DefaultValue is not null)
                .ToList();
            if (keywordDefaults.Count != 0)
            {
                foreach (var parameter in keywordDefaults)
                {
                    EmitLoadConstant(
                        new StringConstant(MangleName(parameter.Name)),
                        parameter.Node.Span);
                    EmitExpression(parameter.DefaultValue);
                }
                _bytecode.Emit(PythonOpcode.BuildMap, keywordDefaults.Count, expression.Span);
            }

            var table = _rootSymbols.FindTable(expression, SymbolTableKind.Function);
            if (table is null)
            {
                AddError(EmitDiagnosticCode.InternalEmitterError, expression,
                    "No symbol table was found for lambda expression.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            var qualifiedName = GetNestedQualifiedName("<lambda>");
            var nestedCompiler = new CodeUnitCompiler(
                _syntaxTree,
                _options,
                _rootSymbols,
                _diagnostics,
                table);
            var nestedCode = nestedCompiler.CompileLambda(expression, signature, qualifiedName);
            if (nestedCode is null)
            {
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (!table.FreeNames.IsEmpty)
            {
                foreach (var freeName in table.FreeNames)
                    EmitClosureCellLoad(freeName, expression.Span);
                _bytecode.Emit(PythonOpcode.BuildTuple, table.FreeNames.Length, expression.Span);
            }

            EmitLoadConstant(new CodeConstant(nestedCode), expression.Span);
            _bytecode.Emit(PythonOpcode.MakeFunction, sourceSpan: expression.Span);

            if (!table.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeClosure,
                    expression.Span);
            }

            if (keywordDefaults.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeKeywordDefaults,
                    expression.Span);
            }

            if (positionalDefaults.Count != 0)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeDefaults,
                    expression.Span);
            }
        }

        private FunctionSignature ParseFunctionSignature(SyntaxNode? parameterList)
        {
            var signature = new FunctionSignature();
            if (parameterList is null)
                return signature;

            var separated = SyntaxAccess.GetNode(parameterList, 0);
            if (separated is null)
                return signature;

            var positional = new List<FunctionParameter>();
            var keywordOnly = false;
            var slashSeen = false;

            foreach (var item in separated.ChildNodesAndTokens())
            {
                if (item.IsToken)
                {
                    if (item.Kind == SyntaxKind.SlashToken)
                    {
                        signature.PositionalOnlyCount = positional.Count;
                        slashSeen = true;
                    }
                    else if (item.Kind == SyntaxKind.StarToken)
                    {
                        keywordOnly = true;
                    }
                    continue;
                }

                var node = item.AsNode();
                if (node.Kind != SyntaxKind.Parameter)
                    continue;

                var prefix = SyntaxAccess.GetToken(node, 0).Kind;
                var name = SyntaxAccess.GetToken(node, 1).Text;
                var isLambdaParameter = node.Green.SlotCount == 4;
                var annotation = isLambdaParameter
                    ? null
                    : SyntaxAccess.GetNode(node, 3);
                var defaultValue = isLambdaParameter
                    ? SyntaxAccess.GetNode(node, 3)
                    : SyntaxAccess.GetNode(node, 5);
                FunctionParameter parameter;

                if (prefix == SyntaxKind.StarToken)
                {
                    parameter = new FunctionParameter(
                        name, node, prefix, annotation, defaultValue,
                        FunctionParameterKind.VarArgs);
                    signature.VarArgs = parameter;
                    keywordOnly = true;
                }
                else if (prefix == SyntaxKind.DoubleStarToken)
                {
                    parameter = new FunctionParameter(
                        name, node, prefix, annotation, defaultValue,
                        FunctionParameterKind.VarKeywords);
                    signature.VarKeywords = parameter;
                    keywordOnly = true;
                }
                else if (keywordOnly)
                {
                    parameter = new FunctionParameter(
                        name, node, SyntaxKind.None, annotation, defaultValue,
                        FunctionParameterKind.KeywordOnly);
                    signature.KeywordOnly.Add(parameter);
                }
                else
                {
                    parameter = new FunctionParameter(
                        name, node, SyntaxKind.None, annotation, defaultValue,
                        FunctionParameterKind.PositionalOrKeyword);
                    positional.Add(parameter);
                }

                signature.Parameters.Add(parameter);
            }

            if (!slashSeen)
                signature.PositionalOnlyCount = 0;

            for (var i = 0; i < positional.Count; i++)
            {
                var parameter = positional[i];
                if (i < signature.PositionalOnlyCount)
                {
                    parameter = new FunctionParameter(
                        parameter.Name,
                        parameter.Node,
                        parameter.PrefixKind,
                        parameter.Annotation,
                        parameter.DefaultValue,
                        FunctionParameterKind.PositionalOnly);
                    var allIndex = signature.Parameters.FindIndex(p =>
                        ReferenceEquals(p.Node.Green, parameter.Node.Green) &&
                        p.Node.Position == parameter.Node.Position);
                    if (allIndex >= 0)
                        signature.Parameters[allIndex] = parameter;
                }
                signature.Positional.Add(parameter);
            }

            return signature;
        }

        private void EmitExpression(SyntaxNode? expression)
        {
            if (expression is null)
            {
                EmitLoadConstant(PythonNoneConstant.Instance, default);
                return;
            }

            switch (expression.Kind)
            {
                case SyntaxKind.NameExpression:
                    EmitNameLoad(GetName(expression), expression.Span);
                    return;

                case SyntaxKind.LiteralExpression:
                    EmitLiteral(expression);
                    return;

                case SyntaxKind.StringConcatenationExpression:
                    EmitStringConcatenation(expression);
                    return;

                case SyntaxKind.FStringExpression:
                    EmitFormattedString(expression);
                    return;

                case SyntaxKind.TStringExpression:
                    EmitTemplateString(expression);
                    return;

                case SyntaxKind.LambdaExpression:
                    EmitLambda(expression);
                    return;

                case SyntaxKind.ParenthesizedExpression:
                    EmitExpression(SyntaxAccess.GetNode(expression, 1));
                    return;

                case SyntaxKind.TupleExpression:
                    EmitCollection(expression, PythonOpcode.BuildTuple);
                    return;

                case SyntaxKind.ExceptionTypeList:
                    {
                        var items = SyntaxAccess.GetNode(expression, 0);
                        var count = 0;
                        if (items is not null)
                        {
                            foreach (var item in items.ChildNodes())
                            {
                                EmitExpression(item);
                                count++;
                            }
                        }
                        _bytecode.Emit(PythonOpcode.BuildTuple, count, expression.Span);
                        return;
                    }

                case SyntaxKind.ListExpression:
                    EmitCollection(expression, PythonOpcode.BuildList);
                    return;

                case SyntaxKind.SetExpression:
                    EmitCollection(expression, PythonOpcode.BuildSet);
                    return;

                case SyntaxKind.DictionaryExpression:
                    EmitDictionary(expression);
                    return;

                case SyntaxKind.ListComprehensionExpression:
                    EmitComprehension(expression, ComprehensionKind.List);
                    return;

                case SyntaxKind.SetComprehensionExpression:
                    EmitComprehension(expression, ComprehensionKind.Set);
                    return;

                case SyntaxKind.DictionaryComprehensionExpression:
                    EmitComprehension(expression, ComprehensionKind.Dictionary);
                    return;

                case SyntaxKind.GeneratorExpression:
                    EmitComprehension(expression, ComprehensionKind.Generator);
                    return;

                case SyntaxKind.UnaryExpression:
                    EmitUnary(expression);
                    return;

                case SyntaxKind.BinaryExpression:
                    EmitBinary(expression);
                    return;

                case SyntaxKind.ComparisonExpression:
                    EmitComparison(expression);
                    return;

                case SyntaxKind.ConditionalExpression:
                    EmitConditionalExpression(expression);
                    return;

                case SyntaxKind.NamedExpression:
                    EmitNamedExpression(expression);
                    return;

                case SyntaxKind.YieldExpression:
                    EmitYieldExpression(expression);
                    return;

                case SyntaxKind.AttributeExpression:
                    EmitExpression(SyntaxAccess.GetNode(expression, 0));
                    _bytecode.Emit(
                        PythonOpcode.LoadAttribute,
                        GetNameIndex(MangleName(SyntaxAccess.GetToken(expression, 2).Text)) << 1,
                        expression.Span);
                    return;

                case SyntaxKind.CallExpression:
                    EmitCall(expression);
                    return;

                case SyntaxKind.SubscriptExpression:
                    EmitExpression(SyntaxAccess.GetNode(expression, 0));
                    EmitSubscriptIndex(SyntaxAccess.GetNode(expression, 2));
                    _bytecode.Emit(
                        PythonOpcode.BinaryOperation,
                        (int)PythonBinaryOperation.Subscript,
                        expression.Span);
                    return;

                case SyntaxKind.SliceExpression:
                    EmitSlice(expression);
                    return;

                case SyntaxKind.StarredExpression:
                    EmitExpression(SyntaxAccess.GetNode(expression, 1));
                    return;

                case SyntaxKind.ErrorExpression:
                case SyntaxKind.MissingExpression:
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                        "Cannot emit a missing or erroneous expression.");
                    EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                    return;

                default:
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                        $"Expression kind {expression.Kind} is not emitted yet.");
                    EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                    return;
            }
        }

        private void EmitLiteral(SyntaxNode expression)
        {
            var token = expression.ChildTokens().FirstOrDefault();
            if (!LiteralParser.TryParse(token, out var constant, out var error))
            {
                AddError(EmitDiagnosticCode.InvalidLiteral, expression,
                    error ?? "Invalid literal.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            EmitLoadConstant(constant, expression.Span);
        }

        private void EmitYieldExpression(SyntaxNode expression)
        {
            if (!_isFunction || !_symbols.IsGenerator)
            {
                AddError(EmitDiagnosticCode.InvalidControlFlow, expression,
                    "yield may only be emitted inside generator code.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (SyntaxAccess.GetToken(expression, 1).Kind == SyntaxKind.FromKeyword)
            {
                var value = SyntaxAccess.GetNode(expression, 2);
                if (value is null)
                {
                    AddError(EmitDiagnosticCode.InvalidControlFlow, expression,
                        "yield from requires an expression.");
                    EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                    return;
                }

                var send = _bytecode.DefineLabel();
                var completed = _bytecode.DefineLabel();

                EmitExpression(value);
                _bytecode.Emit(PythonOpcode.GetYieldFromIterator, sourceSpan: expression.Span);
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);

                _bytecode.MarkLabel(send);
                _bytecode.EmitSend(completed, expression.Span);
                _bytecode.Emit(PythonOpcode.YieldValue, 1, expression.Span);
                _bytecode.Emit(PythonOpcode.Resume, 2, expression.Span);
                _bytecode.EmitJump(send, noInterrupt: true, sourceSpan: expression.Span);

                _bytecode.MarkLabel(completed);
                _bytecode.Emit(PythonOpcode.EndSend, sourceSpan: expression.Span);
                return;
            }

            {
                var value = SyntaxAccess.GetNode(expression, 2);
                if (value is null)
                    EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                else
                    EmitExpression(value);

                _bytecode.Emit(PythonOpcode.YieldValue, 0, expression.Span);
                _bytecode.Emit(PythonOpcode.Resume, 1, expression.Span);
            }
        }
        private void EmitStringConcatenation(SyntaxNode expression)
        {
            var list = SyntaxAccess.GetNode(expression, 0);
            if (list is null)
            {
                EmitLoadConstant(new StringConstant(string.Empty), expression.Span);
                return;
            }

            var items = list.ChildNodes().ToList();
            if (items.All(static item => item.Kind == SyntaxKind.LiteralExpression))
            {
                var constants = new List<PythonConstant>();
                foreach (var item in items)
                {
                    var token = item.ChildTokens().FirstOrDefault();
                    if (!LiteralParser.TryParse(token, out var constant, out var error))
                    {
                        AddError(EmitDiagnosticCode.InvalidLiteral, item,
                            error ?? "Invalid string literal.");
                        EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                        return;
                    }
                    constants.Add(constant);
                }

                if (constants.All(static value => value is StringConstant))
                {
                    var text = string.Concat(constants.Cast<StringConstant>()
                        .Select(static value => value.Value));
                    EmitLoadConstant(new StringConstant(text), expression.Span);
                    return;
                }

                if (constants.All(static value => value is BytesConstant))
                {
                    var bytes = constants.Cast<BytesConstant>()
                        .SelectMany(static value => value.Value)
                        .ToArray();
                    EmitLoadConstant(new BytesConstant(bytes), expression.Span);
                    return;
                }

                AddError(EmitDiagnosticCode.InvalidLiteral, expression,
                    "Cannot concatenate bytes and Unicode literals.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (items.Any(static item => item.Kind == SyntaxKind.TStringExpression))
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                    "Template strings cannot be implicitly concatenated by this emitter yet.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            var partCount = 0;
            foreach (var item in items)
            {
                switch (item.Kind)
                {
                    case SyntaxKind.LiteralExpression:
                        {
                            var token = item.ChildTokens().FirstOrDefault();
                            if (!LiteralParser.TryParse(token, out var constant, out var error) ||
                                constant is not StringConstant textConstant)
                            {
                                AddError(EmitDiagnosticCode.InvalidLiteral, item,
                                    error ?? "Formatted strings may only concatenate with Unicode literals.");
                                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                                return;
                            }
                            EmitLoadConstant(textConstant, item.Span);
                            partCount++;
                            break;
                        }

                    case SyntaxKind.FStringExpression:
                        EmitFormattedString(item);
                        partCount++;
                        break;

                    default:
                        AddError(EmitDiagnosticCode.UnsupportedSyntax, item,
                            $"String concatenation item {item.Kind} is not emitted.");
                        EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                        return;
                }
            }

            if (partCount == 0)
                EmitLoadConstant(new StringConstant(string.Empty), expression.Span);
            else if (partCount > 1)
                _bytecode.Emit(PythonOpcode.BuildString, partCount, expression.Span);
        }

        private void EmitFormattedString(SyntaxNode expression)
        {
            var raw = IsRawFormattedString(SyntaxAccess.GetToken(expression, 0).Text);
            EmitJoinedFormattedParts(SyntaxAccess.GetNode(expression, 1), raw, expression.Span);
        }

        private void EmitJoinedFormattedParts(SyntaxNode? parts, bool raw, TextSpan span)
        {
            var partCount = 0;
            if (parts is not null)
            {
                foreach (var part in parts.ChildNodesAndTokens())
                {
                    if (part.IsToken)
                    {
                        var token = part.AsToken();
                        if (token.Kind is not (SyntaxKind.FStringMiddleToken or SyntaxKind.TStringMiddleToken))
                            continue;

                        if (!LiteralParser.TryParseFormattedText(token.Text, raw, out var value, out var error))
                        {
                            AddError(EmitDiagnosticCode.InvalidLiteral, token.Span,
                                error ?? "Invalid formatted-string text.");
                            EmitLoadConstant(PythonNoneConstant.Instance, span);
                            return;
                        }

                        if (value.Length != 0)
                        {
                            EmitLoadConstant(new StringConstant(value), token.Span);
                            partCount++;
                        }
                        continue;
                    }

                    var node = part.AsNode();
                    if (node.Kind == SyntaxKind.Interpolation)
                    {
                        partCount += EmitFormattedInterpolation(node, raw);
                    }
                    else if (node.Kind is SyntaxKind.ErrorExpression or SyntaxKind.MissingExpression)
                    {
                        AddError(EmitDiagnosticCode.UnsupportedSyntax, node,
                            "Cannot emit a malformed formatted-string part.");
                        EmitLoadConstant(PythonNoneConstant.Instance, span);
                        return;
                    }
                }
            }

            if (partCount == 0)
                EmitLoadConstant(new StringConstant(string.Empty), span);
            else if (partCount > 1)
                _bytecode.Emit(PythonOpcode.BuildString, partCount, span);
        }

        private int EmitFormattedInterpolation(SyntaxNode interpolation, bool raw)
        {
            var emittedParts = 0;
            var debugEqual = SyntaxAccess.GetToken(interpolation, 2);
            if (debugEqual.Kind == SyntaxKind.EqualToken)
            {
                EmitLoadConstant(
                    new StringConstant(GetDebugInterpolationText(interpolation)),
                    interpolation.Span);
                emittedParts++;
            }

            EmitExpression(SyntaxAccess.GetNode(interpolation, 1));

            var conversion = GetFormatConversion(SyntaxAccess.GetNode(interpolation, 3));
            var formatSpec = SyntaxAccess.GetNode(interpolation, 4);
            if (debugEqual.Kind == SyntaxKind.EqualToken &&
                conversion == PythonFormatConversion.None &&
                formatSpec is null)
            {
                conversion = PythonFormatConversion.Representation;
            }

            if (conversion != PythonFormatConversion.None)
            {
                _bytecode.Emit(
                    PythonOpcode.ConvertValue,
                    (int)conversion,
                    interpolation.Span);
            }

            if (formatSpec is null)
            {
                _bytecode.Emit(PythonOpcode.FormatSimple, sourceSpan: interpolation.Span);
            }
            else
            {
                EmitJoinedFormattedParts(
                    SyntaxAccess.GetNode(formatSpec, 1),
                    raw,
                    formatSpec.Span);
                _bytecode.Emit(PythonOpcode.FormatWithSpec, sourceSpan: interpolation.Span);
            }

            return emittedParts + 1;
        }

        private void EmitTemplateString(SyntaxNode expression)
        {
            var strings = new List<string>();
            var interpolations = new List<(SyntaxNode Node, bool Raw)>();
            var currentString = new StringBuilder();
            if (!CollectTemplateParts(expression, currentString, strings, interpolations))
            {
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            strings.Add(currentString.ToString());
            foreach (var value in strings)
                EmitLoadConstant(new StringConstant(value), expression.Span);
            _bytecode.Emit(PythonOpcode.BuildTuple, strings.Count, expression.Span);

            foreach (var (interpolation, raw) in interpolations)
                EmitTemplateInterpolation(interpolation, raw);
            _bytecode.Emit(PythonOpcode.BuildTuple, interpolations.Count, expression.Span);
            _bytecode.Emit(PythonOpcode.BuildTemplate, sourceSpan: expression.Span);
        }

        private bool CollectTemplateParts(
            SyntaxNode expression,
            StringBuilder currentString,
            List<string> strings,
            List<(SyntaxNode Node, bool Raw)> interpolations)
        {
            if (expression.Green.SlotCount == 1 &&
                SyntaxAccess.GetNode(expression, 0) is { } concatenated)
            {
                foreach (var item in concatenated.ChildNodes())
                {
                    if (item.Kind != SyntaxKind.TStringExpression)
                    {
                        AddError(EmitDiagnosticCode.UnsupportedSyntax, item,
                            "A template string cannot be implicitly concatenated with a non-template literal.");
                        return false;
                    }

                    if (!CollectTemplateParts(item, currentString, strings, interpolations))
                        return false;
                }
                return true;
            }

            var raw = IsRawFormattedString(SyntaxAccess.GetToken(expression, 0).Text);
            var parts = SyntaxAccess.GetNode(expression, 1);
            if (parts is null)
                return true;

            foreach (var part in parts.ChildNodesAndTokens())
            {
                if (part.IsToken)
                {
                    var token = part.AsToken();
                    if (token.Kind != SyntaxKind.TStringMiddleToken)
                        continue;

                    if (!LiteralParser.TryParseFormattedText(token.Text, raw, out var value, out var error))
                    {
                        AddError(EmitDiagnosticCode.InvalidLiteral, token.Span,
                            error ?? "Invalid template-string text.");
                        return false;
                    }
                    currentString.Append(value);
                    continue;
                }

                var node = part.AsNode();
                if (node.Kind == SyntaxKind.Interpolation)
                {
                    if (SyntaxAccess.GetToken(node, 2).Kind == SyntaxKind.EqualToken)
                        currentString.Append(GetDebugInterpolationText(node));
                    strings.Add(currentString.ToString());
                    currentString.Clear();
                    interpolations.Add((node, raw));
                }
                else if (node.Kind is SyntaxKind.ErrorExpression or SyntaxKind.MissingExpression)
                {
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, node,
                        "Cannot emit a malformed template-string part.");
                    return false;
                }
            }

            return true;
        }

        private void EmitTemplateInterpolation(SyntaxNode interpolation, bool raw)
        {
            EmitExpression(SyntaxAccess.GetNode(interpolation, 1));
            EmitLoadConstant(
                new StringConstant(GetInterpolationSource(interpolation)),
                interpolation.Span);

            var operand = 2;
            var formatSpec = SyntaxAccess.GetNode(interpolation, 4);
            if (formatSpec is not null)
            {
                EmitJoinedFormattedParts(
                    SyntaxAccess.GetNode(formatSpec, 1),
                    raw,
                    formatSpec.Span);
                operand++;
            }

            var conversion = GetFormatConversion(SyntaxAccess.GetNode(interpolation, 3));
            if (SyntaxAccess.GetToken(interpolation, 2).Kind == SyntaxKind.EqualToken &&
                conversion == PythonFormatConversion.None &&
                formatSpec is null)
            {
                conversion = PythonFormatConversion.Representation;
            }

            operand |= (int)conversion << 2;
            _bytecode.Emit(PythonOpcode.BuildInterpolation, operand, interpolation.Span);
        }

        private string GetDebugInterpolationText(SyntaxNode interpolation)
        {
            var start = SyntaxAccess.GetToken(interpolation, 0).Span.End;
            var end = GetInterpolationSuffixStart(interpolation);
            if (start < 0 || end < start || end > _syntaxTree.Text.Length)
                return string.Empty;
            return _syntaxTree.Text.Substring(start, end - start);
        }

        private string GetInterpolationSource(SyntaxNode interpolation)
        {
            var start = SyntaxAccess.GetToken(interpolation, 0).Span.End;
            var end = GetInterpolationSuffixStart(interpolation);
            if (start < 0 || end < start || end > _syntaxTree.Text.Length)
                return string.Empty;

            var source = _syntaxTree.Text.Substring(start, end - start);
            var length = source.Length;
            while (length > 0 &&
                   (char.IsWhiteSpace(source[length - 1]) || source[length - 1] == '='))
            {
                length--;
            }
            return source[..length];
        }

        private static int GetInterpolationSuffixStart(SyntaxNode interpolation)
        {
            if (SyntaxAccess.GetNode(interpolation, 3) is { } conversion)
                return conversion.Span.Start;
            if (SyntaxAccess.GetNode(interpolation, 4) is { } formatSpec)
                return formatSpec.Span.Start;

            var rightBrace = SyntaxAccess.GetToken(interpolation, 5);
            return rightBrace.Kind == SyntaxKind.RightBraceToken
                ? rightBrace.Span.Start
                : interpolation.Span.End;
        }

        private static PythonFormatConversion GetFormatConversion(SyntaxNode? conversion)
        {
            if (conversion is null)
                return PythonFormatConversion.None;

            return SyntaxAccess.GetToken(conversion, 1).Text switch
            {
                "s" => PythonFormatConversion.String,
                "r" => PythonFormatConversion.Representation,
                "a" => PythonFormatConversion.Ascii,
                _ => PythonFormatConversion.None,
            };
        }

        private static bool IsRawFormattedString(string startTokenText)
        {
            foreach (var ch in startTokenText)
            {
                if (ch is '\'' or '"')
                    break;
                if (ch is 'r' or 'R')
                    return true;
            }
            return false;
        }

        private void EmitComprehension(SyntaxNode expression, ComprehensionKind kind)
        {
            var table = _rootSymbols.FindTable(expression, SymbolTableKind.Function);
            if (table is null)
            {
                AddError(EmitDiagnosticCode.InternalEmitterError, expression,
                    $"No symbol table was found for {kind} comprehension.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            var clauses = GetComprehensionClauses(expression);
            var outerFor = clauses.FirstOrDefault(static clause =>
                clause.Kind == SyntaxKind.ForComprehensionClause);
            if (outerFor is null)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                    "Comprehension does not contain a for-clause.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (clauses.Any(static clause =>
                    clause.Kind == SyntaxKind.ForComprehensionClause &&
                    SyntaxAccess.GetToken(clause, 0).Kind == SyntaxKind.AsyncKeyword))
            {
                AddError(EmitDiagnosticCode.UnsupportedCoroutine, expression,
                    "Asynchronous comprehensions are not emitted yet.");
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (table.IsComprehensionInlined && kind != ComprehensionKind.Generator)
            {
                EmitInlinedComprehension(expression, kind, table, clauses, outerFor);
                return;
            }

            var qualifiedName = GetComprehensionQualifiedName(kind);
            var nestedCompiler = new CodeUnitCompiler(
                _syntaxTree,
                _options,
                _rootSymbols,
                _diagnostics,
                table);
            var nestedCode = nestedCompiler.CompileComprehension(expression, kind, qualifiedName);
            if (nestedCode is null)
            {
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
                return;
            }

            if (!table.FreeNames.IsEmpty)
            {
                foreach (var freeName in table.FreeNames)
                    EmitClosureCellLoad(freeName, expression.Span);
                _bytecode.Emit(PythonOpcode.BuildTuple, table.FreeNames.Length, expression.Span);
            }

            EmitLoadConstant(new CodeConstant(nestedCode), expression.Span);
            _bytecode.Emit(PythonOpcode.MakeFunction, sourceSpan: expression.Span);
            if (!table.FreeNames.IsEmpty)
            {
                _bytecode.Emit(
                    PythonOpcode.SetFunctionAttribute,
                    FunctionAttributeClosure,
                    expression.Span);
            }

            EmitExpression(SyntaxAccess.GetNode(outerFor, 4));
            _bytecode.Emit(PythonOpcode.GetIterator, sourceSpan: outerFor.Span);
            _bytecode.Emit(PythonOpcode.Call, 0, expression.Span);
        }

        private void EmitInlinedComprehension(
            SyntaxNode expression,
            ComprehensionKind kind,
            SymbolTable table,
            List<SyntaxNode> clauses,
            SyntaxNode outerFor)
        {
            EmitExpression(SyntaxAccess.GetNode(outerFor, 4));
            _bytecode.Emit(PythonOpcode.GetIterator, sourceSpan: outerFor.Span);

            var savedLocals = GetComprehensionLocalsToSave(table, expression);
            foreach (var (name, localIndex, createCell) in savedLocals)
            {
                _bytecode.Emit(PythonOpcode.LoadFastAndClear, localIndex, expression.Span);
                if (createCell)
                    _bytecode.Emit(PythonOpcode.MakeCell, localIndex, expression.Span);
            }
            if (savedLocals.Count != 0)
                _bytecode.Emit(PythonOpcode.Swap, savedLocals.Count + 1, expression.Span);

            var previousSymbols = _nameSymbols;
            var previousInlineState = _inInlinedComprehension;
            _nameSymbols = table;
            _inInlinedComprehension = true;
            try
            {
                if (savedLocals.Count == 0)
                {
                    EmitComprehensionContainer(kind, expression.Span);
                    _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
                    EmitComprehensionClauses(
                        expression,
                        kind,
                        clauses,
                        clauseIndex: 0,
                        iteratorDepth: 0,
                        firstIteratorOnStack: true,
                        firstIteratorFromParameter: false,
                        continueTarget: null);
                    return;
                }

                var protectedStart = _bytecode.DefineLabel();
                var protectedEnd = _bytecode.DefineLabel();
                var handler = _bytecode.DefineLabel();
                var end = _bytecode.DefineLabel();

                _bytecode.MarkLabel(protectedStart);
                EmitComprehensionContainer(kind, expression.Span);
                _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
                EmitComprehensionClauses(
                    expression,
                    kind,
                    clauses,
                    clauseIndex: 0,
                    iteratorDepth: 0,
                    firstIteratorOnStack: true,
                    firstIteratorFromParameter: false,
                    continueTarget: null);
                _bytecode.MarkLabel(protectedEnd);

                RestoreComprehensionLocals(savedLocals, expression.Span);
                _bytecode.EmitJump(end, noInterrupt: true, sourceSpan: expression.Span);

                _bytecode.MarkLabel(handler);
                _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
                RestoreComprehensionLocals(savedLocals, expression.Span);
                _bytecode.Emit(PythonOpcode.Reraise, 0, expression.Span);

                _bytecode.MarkLabel(end);
                _bytecode.AddExceptionRegion(
                    protectedStart,
                    protectedEnd,
                    handler);
            }
            finally
            {
                _nameSymbols = previousSymbols;
                _inInlinedComprehension = previousInlineState;
            }
        }

        private List<(string Name, int LocalIndex, bool CreateCell)> GetComprehensionLocalsToSave(
            SymbolTable table,
            SyntaxNode expression)
        {
            var result = new List<(string Name, int LocalIndex, bool CreateCell)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var includeAll =
                _codeUnitKind == CodeUnitKind.Class &&
                !_inInlinedComprehension;
            foreach (var symbol in table.Symbols)
            {
                if ((!includeAll &&
                     !symbol.IsComprehensionIterationVariable &&
                     (symbol.Flags & SymbolFlags.ComprehensionCell) == 0) ||
                    !seen.Add(symbol.Name))
                {
                    continue;
                }

                if (!TryGetComprehensionLocalIndex(symbol.Name, out var localIndex))
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, expression,
                        $"Inlined comprehension local {symbol.Name} is missing from locals-plus layout.");
                    continue;
                }

                result.Add((
                    symbol.Name,
                    localIndex,
                    symbol.Scope == SymbolScope.Cell ||
                    (symbol.Flags & SymbolFlags.ComprehensionCell) != 0));
            }
            return result;
        }

        private bool TryGetComprehensionLocalIndex(string name, out int localIndex)
        {
            if (_hiddenComprehensionLocalIndices.TryGetValue(name, out localIndex))
                return true;
            return _localIndices.TryGetValue(name, out localIndex);
        }

        private void RestoreComprehensionLocals(
            List<(string Name, int LocalIndex, bool CreateCell)> savedLocals,
            TextSpan span)
        {
            _bytecode.Emit(PythonOpcode.Swap, savedLocals.Count + 1, span);
            for (var index = savedLocals.Count - 1; index >= 0; index--)
                _bytecode.Emit(PythonOpcode.StoreFast, savedLocals[index].LocalIndex, span);
        }

        private void EmitComprehensionBody(
            SyntaxNode expression,
            ComprehensionKind kind,
            bool firstIteratorOnStack,
            bool firstIteratorFromParameter)
        {
            var clauses = GetComprehensionClauses(expression);
            if (clauses.Count == 0)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                    "Comprehension does not contain clauses.");
                if (kind != ComprehensionKind.Generator)
                    EmitComprehensionContainer(kind, expression.Span);
                return;
            }

            if (kind != ComprehensionKind.Generator)
            {
                EmitComprehensionContainer(kind, expression.Span);
                if (firstIteratorOnStack)
                    _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
            }

            EmitComprehensionClauses(
                expression,
                kind,
                clauses,
                clauseIndex: 0,
                iteratorDepth: 0,
                firstIteratorOnStack: firstIteratorOnStack,
                firstIteratorFromParameter: firstIteratorFromParameter,
                continueTarget: null);
        }

        private void EmitComprehensionClauses(
            SyntaxNode expression,
            ComprehensionKind kind,
            List<SyntaxNode> clauses,
            int clauseIndex,
            int iteratorDepth,
            bool firstIteratorOnStack,
            bool firstIteratorFromParameter,
            BytecodeLabel? continueTarget)
        {
            if (clauseIndex >= clauses.Count)
            {
                EmitComprehensionElement(expression, kind, iteratorDepth);
                return;
            }

            var clause = clauses[clauseIndex];
            if (clause.Kind == SyntaxKind.IfComprehensionClause)
            {
                if (continueTarget is null)
                {
                    AddError(EmitDiagnosticCode.InternalEmitterError, clause,
                        "Comprehension if-clause has no enclosing iterator.");
                    return;
                }

                var accepted = _bytecode.DefineLabel();
                EmitExpression(SyntaxAccess.GetNode(clause, 1));
                _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: clause.Span);
                _bytecode.EmitConditionalJump(PythonOpcode.PopJumpIfTrue, accepted, clause.Span);
                _bytecode.EmitJump(continueTarget, noInterrupt: true, sourceSpan: clause.Span);
                _bytecode.MarkLabel(accepted);
                EmitComprehensionClauses(
                    expression,
                    kind,
                    clauses,
                    clauseIndex + 1,
                    iteratorDepth,
                    firstIteratorOnStack: false,
                    firstIteratorFromParameter: false,
                    continueTarget: continueTarget);
                return;
            }

            if (clause.Kind != SyntaxKind.ForComprehensionClause)
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, clause,
                    $"Unsupported comprehension clause {clause.Kind}.");
                return;
            }

            var isFirst = clauseIndex == 0;
            if (isFirst && firstIteratorOnStack)
            {
                // The enclosing code unit already evaluated GET_ITER.
            }
            else if (isFirst && firstIteratorFromParameter)
            {
                // The implicit .0 parameter is the iterator prepared by the caller.
                EmitNameLoad(".0", clause.Span);
            }
            else
            {
                EmitExpression(SyntaxAccess.GetNode(clause, 4));
                _bytecode.Emit(PythonOpcode.GetIterator, sourceSpan: clause.Span);
            }

            var loop = _bytecode.DefineLabel();
            var cleanup = _bytecode.DefineLabel();
            _bytecode.MarkLabel(loop);
            _bytecode.EmitForIterator(cleanup, clause.Span);
            EmitTarget(SyntaxAccess.GetNode(clause, 2), AssignmentTargetMode.Store);

            EmitComprehensionClauses(
                expression,
                kind,
                clauses,
                clauseIndex + 1,
                iteratorDepth + 1,
                firstIteratorOnStack: false,
                firstIteratorFromParameter: false,
                continueTarget: loop);
            _bytecode.EmitJump(loop, noInterrupt: true, sourceSpan: clause.Span);

            _bytecode.MarkLabel(cleanup);
            _bytecode.Emit(PythonOpcode.EndFor, sourceSpan: clause.Span);
            _bytecode.Emit(PythonOpcode.PopIterator, sourceSpan: clause.Span);
        }

        private void EmitComprehensionElement(
            SyntaxNode expression,
            ComprehensionKind kind,
            int iteratorDepth)
        {
            var element = GetComprehensionElement(expression);
            switch (kind)
            {
                case ComprehensionKind.List:
                    EmitExpression(element);
                    _bytecode.Emit(PythonOpcode.ListAppend, iteratorDepth + 1, expression.Span);
                    return;

                case ComprehensionKind.Set:
                    EmitExpression(element);
                    _bytecode.Emit(PythonOpcode.SetAdd, iteratorDepth + 1, expression.Span);
                    return;

                case ComprehensionKind.Dictionary:
                    if (element?.Kind != SyntaxKind.KeyValuePair)
                    {
                        AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                            "Dictionary comprehension element is not a key/value pair.");
                        return;
                    }
                    EmitExpression(SyntaxAccess.GetNode(element, 0));
                    EmitExpression(SyntaxAccess.GetNode(element, 2));
                    _bytecode.Emit(PythonOpcode.MapAdd, iteratorDepth + 1, expression.Span);
                    return;

                case ComprehensionKind.Generator:
                    EmitExpression(element);
                    _bytecode.Emit(PythonOpcode.YieldValue, 0, expression.Span);
                    // The generator-expression body is protected by the implicit
                    // StopIteration handler, so CPython sets the depth-1 bit.
                    _bytecode.Emit(PythonOpcode.Resume, 5, expression.Span);
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
                    return;
            }
        }

        private void EmitComprehensionContainer(ComprehensionKind kind, TextSpan span)
        {
            _bytecode.Emit(kind switch
            {
                ComprehensionKind.List => PythonOpcode.BuildList,
                ComprehensionKind.Set => PythonOpcode.BuildSet,
                ComprehensionKind.Dictionary => PythonOpcode.BuildMap,
                _ => throw new InvalidOperationException("Generator expressions do not build a container."),
            }, 0, span);
        }

        private static SyntaxNode? GetComprehensionElement(SyntaxNode expression)
        {
            var unparenthesizedGenerator =
                expression.Kind == SyntaxKind.GeneratorExpression &&
                SyntaxAccess.GetToken(expression, 0).Kind != SyntaxKind.LeftParenthesisToken;
            return SyntaxAccess.GetNode(expression, unparenthesizedGenerator ? 0 : 1);
        }

        private static List<SyntaxNode> GetComprehensionClauses(SyntaxNode expression)
        {
            var unparenthesizedGenerator =
                expression.Kind == SyntaxKind.GeneratorExpression &&
                SyntaxAccess.GetToken(expression, 0).Kind != SyntaxKind.LeftParenthesisToken;
            var clauses = SyntaxAccess.GetNode(expression, unparenthesizedGenerator ? 1 : 2);
            return clauses?.DescendantNodeChildren()
                .Where(static node => node.Kind is
                    SyntaxKind.ForComprehensionClause or
                    SyntaxKind.IfComprehensionClause)
                .ToList() ?? [];
        }


        private void EmitCollection(SyntaxNode expression, PythonOpcode buildOpcode)
        {
            var separated = expression.ChildNodes()
                .FirstOrDefault(static node => node.Kind == SyntaxKind.SeparatedSyntaxList);
            if (separated is null)
            {
                _bytecode.Emit(buildOpcode, 0, expression.Span);
                return;
            }

            var items = separated.ChildNodes().ToList();
            var hasStarred = items.Any(static item => item.Kind == SyntaxKind.StarredExpression);
            if (!hasStarred)
            {
                foreach (var item in items)
                    EmitExpression(item);
                _bytecode.Emit(buildOpcode, items.Count, expression.Span);
                return;
            }

            if (buildOpcode == PythonOpcode.BuildTuple || buildOpcode == PythonOpcode.BuildList)
            {
                _bytecode.Emit(PythonOpcode.BuildList, 0, expression.Span);
                foreach (var item in items)
                {
                    if (item.Kind == SyntaxKind.StarredExpression)
                    {
                        EmitExpression(SyntaxAccess.GetNode(item, 1));
                        _bytecode.Emit(PythonOpcode.ListExtend, 1, item.Span);
                    }
                    else
                    {
                        EmitExpression(item);
                        _bytecode.Emit(PythonOpcode.ListAppend, 1, item.Span);
                    }
                }

                if (buildOpcode == PythonOpcode.BuildTuple)
                {
                    _bytecode.Emit(
                        PythonOpcode.CallIntrinsic1,
                        (int)PythonIntrinsic1.ListToTuple,
                        expression.Span);
                }
                return;
            }

            if (buildOpcode == PythonOpcode.BuildSet)
            {
                _bytecode.Emit(PythonOpcode.BuildSet, 0, expression.Span);
                foreach (var item in items)
                {
                    if (item.Kind == SyntaxKind.StarredExpression)
                    {
                        EmitExpression(SyntaxAccess.GetNode(item, 1));
                        _bytecode.Emit(PythonOpcode.SetUpdate, 1, item.Span);
                    }
                    else
                    {
                        EmitExpression(item);
                        _bytecode.Emit(PythonOpcode.SetAdd, 1, item.Span);
                    }
                }
            }
        }

        private void EmitDictionary(SyntaxNode expression)
        {
            var separated = expression.ChildNodes()
                .FirstOrDefault(static node => node.Kind == SyntaxKind.SeparatedSyntaxList);
            if (separated is null)
            {
                _bytecode.Emit(PythonOpcode.BuildMap, 0, expression.Span);
                return;
            }

            var items = separated.ChildNodes().ToList();
            var hasExpansion = items.Any(static item => item.Kind == SyntaxKind.DoubleStarredExpression);
            if (!hasExpansion)
            {
                foreach (var pair in items)
                {
                    EmitExpression(SyntaxAccess.GetNode(pair, 0));
                    EmitExpression(SyntaxAccess.GetNode(pair, 2));
                }
                _bytecode.Emit(PythonOpcode.BuildMap, items.Count, expression.Span);
                return;
            }

            _bytecode.Emit(PythonOpcode.BuildMap, 0, expression.Span);
            foreach (var item in items)
            {
                if (item.Kind == SyntaxKind.DoubleStarredExpression)
                {
                    EmitExpression(SyntaxAccess.GetNode(item, 1));
                }
                else
                {
                    EmitExpression(SyntaxAccess.GetNode(item, 0));
                    EmitExpression(SyntaxAccess.GetNode(item, 2));
                    _bytecode.Emit(PythonOpcode.BuildMap, 1, item.Span);
                }
                _bytecode.Emit(PythonOpcode.DictionaryUpdate, 1, item.Span);
            }
        }

        private void EmitUnary(SyntaxNode expression)
        {
            var operatorKind = SyntaxAccess.GetToken(expression, 0).Kind;
            EmitExpression(SyntaxAccess.GetNode(expression, 1));
            switch (operatorKind)
            {
                case SyntaxKind.PlusToken:
                    _bytecode.Emit(
                        PythonOpcode.CallIntrinsic1,
                        (int)PythonIntrinsic1.UnaryPositive,
                        expression.Span);
                    break;
                case SyntaxKind.MinusToken:
                    _bytecode.Emit(PythonOpcode.UnaryNegative, sourceSpan: expression.Span);
                    break;
                case SyntaxKind.TildeToken:
                    _bytecode.Emit(PythonOpcode.UnaryInvert, sourceSpan: expression.Span);
                    break;
                case SyntaxKind.NotKeyword:
                    _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: expression.Span);
                    _bytecode.Emit(PythonOpcode.UnaryNot, sourceSpan: expression.Span);
                    break;
                default:
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                        $"Unary operator {operatorKind} is not emitted.");
                    break;
            }
        }

        private void EmitBinary(SyntaxNode expression)
        {
            var left = SyntaxAccess.GetNode(expression, 0);
            var operatorKind = SyntaxAccess.GetToken(expression, 1).Kind;
            var right = SyntaxAccess.GetNode(expression, 2);

            if (operatorKind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword)
            {
                var end = _bytecode.DefineLabel();
                EmitExpression(left);
                _bytecode.Emit(PythonOpcode.Copy, 1, expression.Span);
                _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: expression.Span);
                _bytecode.EmitConditionalJump(
                    operatorKind == SyntaxKind.AndKeyword
                        ? PythonOpcode.PopJumpIfFalse
                        : PythonOpcode.PopJumpIfTrue,
                    end,
                    expression.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
                EmitExpression(right);
                _bytecode.MarkLabel(end);
                return;
            }

            EmitExpression(left);
            EmitExpression(right);
            if (!TryMapBinaryOperator(operatorKind, inPlace: false, out var operation))
            {
                AddError(EmitDiagnosticCode.UnsupportedSyntax, expression,
                    $"Binary operator {operatorKind} is not emitted.");
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
                return;
            }
            _bytecode.Emit(PythonOpcode.BinaryOperation, (int)operation, expression.Span);
        }

        private void EmitComparison(SyntaxNode expression)
        {
            var parts = expression.ChildNodesAndTokens().ToList();
            if (parts.Count < 3)
            {
                var onlyNode = parts.FirstOrDefault(static part => part.IsNode);
                EmitExpression(onlyNode.IsNode ? onlyNode.AsNode() : null);
                return;
            }

            var operands = new List<SyntaxNode>();
            var operators = new List<(SyntaxKind First, SyntaxKind Second)>();
            var index = 0;
            operands.Add(parts[index++].AsNode());
            while (index < parts.Count)
            {
                var first = parts[index++].Kind;
                var second = SyntaxKind.None;
                if ((first == SyntaxKind.NotKeyword || first == SyntaxKind.IsKeyword) &&
                    index < parts.Count && parts[index].IsToken)
                {
                    second = parts[index++].Kind;
                }
                if (index >= parts.Count || !parts[index].IsNode)
                    break;
                operators.Add((first, second));
                operands.Add(parts[index++].AsNode());
            }

            EmitExpression(operands[0]);
            if (operators.Count == 1)
            {
                EmitExpression(operands[1]);
                EmitComparisonOperator(operators[0], expression.Span);
                return;
            }

            var cleanup = _bytecode.DefineLabel();
            var end = _bytecode.DefineLabel();
            for (var i = 0; i < operators.Count; i++)
            {
                EmitExpression(operands[i + 1]);
                if (i == operators.Count - 1)
                {
                    EmitComparisonOperator(operators[i], expression.Span);
                    _bytecode.EmitJump(end, noInterrupt: true, sourceSpan: expression.Span);
                    break;
                }

                _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
                _bytecode.Emit(PythonOpcode.Copy, 2, expression.Span);
                EmitComparisonOperator(operators[i], expression.Span);
                _bytecode.Emit(PythonOpcode.Copy, 1, expression.Span);
                _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: expression.Span);
                _bytecode.EmitConditionalJump(PythonOpcode.PopJumpIfFalse, cleanup, expression.Span);
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
            }

            _bytecode.MarkLabel(cleanup);
            _bytecode.Emit(PythonOpcode.Swap, 2, expression.Span);
            _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: expression.Span);
            _bytecode.MarkLabel(end);
        }

        private void EmitComparisonOperator(
            (SyntaxKind First, SyntaxKind Second) operation,
            TextSpan span)
        {
            if (operation.First == SyntaxKind.InKeyword)
            {
                _bytecode.Emit(PythonOpcode.ContainsOperation, 0, span);
                return;
            }
            if (operation.First == SyntaxKind.NotKeyword && operation.Second == SyntaxKind.InKeyword)
            {
                _bytecode.Emit(PythonOpcode.ContainsOperation, 1, span);
                return;
            }
            if (operation.First == SyntaxKind.IsKeyword)
            {
                _bytecode.Emit(PythonOpcode.IsOperation,
                    operation.Second == SyntaxKind.NotKeyword ? 1 : 0,
                    span);
                return;
            }

            // CPython 3.14 stores the rich-comparison selector in bits 5..7
            // and the quickening comparison mask in bits 0..3.
            var mask = operation.First switch
            {
                SyntaxKind.LessToken => (0 << 5) | 2,
                SyntaxKind.LessEqualToken => (1 << 5) | 10,
                SyntaxKind.EqualEqualToken => (2 << 5) | 8,
                SyntaxKind.NotEqualToken => (3 << 5) | 7,
                SyntaxKind.GreaterToken => (4 << 5) | 4,
                SyntaxKind.GreaterEqualToken => (5 << 5) | 12,
                _ => -1,
            };
            if (mask < 0)
            {
                _diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.UnsupportedSyntax,
                    EmitDiagnosticSeverity.Error,
                    span,
                    $"Comparison operator {operation.First} is not emitted."));
                return;
            }
            _bytecode.Emit(PythonOpcode.CompareOperation, mask, span);
        }

        private void EmitConditionalExpression(SyntaxNode expression)
        {
            var alternative = _bytecode.DefineLabel();
            var end = _bytecode.DefineLabel();
            EmitConditionJumpFalse(SyntaxAccess.GetNode(expression, 2), alternative, expression.Span);
            EmitExpression(SyntaxAccess.GetNode(expression, 0));
            _bytecode.EmitJump(end, sourceSpan: expression.Span);
            _bytecode.MarkLabel(alternative);
            EmitExpression(SyntaxAccess.GetNode(expression, 4));
            _bytecode.MarkLabel(end);
        }

        private void EmitNamedExpression(SyntaxNode expression)
        {
            var name = SyntaxAccess.GetToken(expression, 0).Text;
            EmitExpression(SyntaxAccess.GetNode(expression, 2));
            _bytecode.Emit(PythonOpcode.Copy, 1, expression.Span);
            EmitNameStore(name, expression.Span);
        }

        private void EmitCall(SyntaxNode expression)
        {
            var callee = SyntaxAccess.GetNode(expression, 0);
            var argumentList = SyntaxAccess.GetNode(expression, 1);
            var separated = argumentList is null ? null : SyntaxAccess.GetNode(argumentList, 1);
            var arguments = separated?.ChildNodes().ToList() ?? [];
            var positionalArguments = arguments
                .Where(static argument => argument.Kind is not (
                    SyntaxKind.KeywordArgument or SyntaxKind.DoubleStarredExpression))
                .ToList();
            var keywordArguments = arguments
                .Where(static argument => argument.Kind is
                    SyntaxKind.KeywordArgument or SyntaxKind.DoubleStarredExpression)
                .ToList();

            ValidateExplicitKeywordArguments(keywordArguments);

            var expanded = positionalArguments.Any(static argument =>
                    argument.Kind == SyntaxKind.StarredExpression) ||
                keywordArguments.Any(static argument =>
                    argument.Kind == SyntaxKind.DoubleStarredExpression);
            if (!expanded)
            {
                EmitSimpleCallable(callee, expression.Span);
                foreach (var argument in positionalArguments)
                    EmitExpression(argument);

                var keywordNames = new List<PythonConstant>(keywordArguments.Count);
                foreach (var argument in keywordArguments)
                {
                    keywordNames.Add(new StringConstant(SyntaxAccess.GetToken(argument, 0).Text));
                    EmitExpression(SyntaxAccess.GetNode(argument, 2));
                }

                var total = positionalArguments.Count + keywordArguments.Count;
                if (keywordNames.Count == 0)
                {
                    _bytecode.Emit(PythonOpcode.Call, total, expression.Span);
                }
                else
                {
                    EmitLoadConstant(new TupleConstant(keywordNames), expression.Span);
                    _bytecode.Emit(PythonOpcode.CallKeyword, total, expression.Span);
                }
                return;
            }

            EmitExpression(callee);
            _bytecode.Emit(PythonOpcode.PushNull, sourceSpan: expression.Span);
            EmitExpandedPositionalArguments(positionalArguments, expression.Span);

            if (keywordArguments.Count == 0)
                _bytecode.Emit(PythonOpcode.PushNull, sourceSpan: expression.Span);
            else
                EmitExpandedKeywordArguments(keywordArguments, expression.Span);

            _bytecode.Emit(PythonOpcode.CallFunctionEx, sourceSpan: expression.Span);
        }

        private void EmitSimpleCallable(SyntaxNode? callee, TextSpan span)
        {
            if (callee?.Kind == SyntaxKind.AttributeExpression)
            {
                EmitExpression(SyntaxAccess.GetNode(callee, 0));
                _bytecode.Emit(
                    PythonOpcode.LoadAttribute,
                    (GetNameIndex(MangleName(SyntaxAccess.GetToken(callee, 2).Text)) << 1) | 1,
                    callee.Span);
                return;
            }

            EmitExpression(callee);
            _bytecode.Emit(PythonOpcode.PushNull, sourceSpan: span);
        }

        private void ValidateExplicitKeywordArguments(List<SyntaxNode> keywordArguments)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var argument in keywordArguments)
            {
                if (argument.Kind != SyntaxKind.KeywordArgument)
                    continue;

                var name = SyntaxAccess.GetToken(argument, 0).Text;
                if (!names.Add(name))
                {
                    AddError(EmitDiagnosticCode.UnsupportedSyntax, argument,
                        $"Keyword argument '{name}' is repeated.");
                }
            }
        }

        private void EmitExpandedPositionalArguments(
            List<SyntaxNode> positionalArguments,
            TextSpan span)
        {
            if (positionalArguments.Count == 1 &&
                positionalArguments[0].Kind == SyntaxKind.StarredExpression)
            {
                EmitExpression(SyntaxAccess.GetNode(positionalArguments[0], 1));
                return;
            }

            if (!positionalArguments.Any(static argument =>
                    argument.Kind == SyntaxKind.StarredExpression))
            {
                foreach (var argument in positionalArguments)
                    EmitExpression(argument);
                _bytecode.Emit(PythonOpcode.BuildTuple, positionalArguments.Count, span);
                return;
            }

            _bytecode.Emit(PythonOpcode.BuildList, 0, span);
            foreach (var argument in positionalArguments)
            {
                if (argument.Kind == SyntaxKind.StarredExpression)
                {
                    EmitExpression(SyntaxAccess.GetNode(argument, 1));
                    _bytecode.Emit(PythonOpcode.ListExtend, 1, argument.Span);
                }
                else
                {
                    EmitExpression(argument);
                    _bytecode.Emit(PythonOpcode.ListAppend, 1, argument.Span);
                }
            }
            _bytecode.Emit(
                PythonOpcode.CallIntrinsic1,
                (int)PythonIntrinsic1.ListToTuple,
                span);
        }

        private void EmitExpandedKeywordArguments(
            List<SyntaxNode> keywordArguments,
            TextSpan span)
        {
            var pendingNamed = new List<SyntaxNode>();
            var haveDictionary = false;

            foreach (var argument in keywordArguments)
            {
                if (argument.Kind == SyntaxKind.KeywordArgument)
                {
                    pendingNamed.Add(argument);
                    continue;
                }

                FlushExpandedNamedKeywords(pendingNamed, ref haveDictionary, span);
                if (!haveDictionary)
                {
                    _bytecode.Emit(PythonOpcode.BuildMap, 0, span);
                    haveDictionary = true;
                }

                EmitExpression(SyntaxAccess.GetNode(argument, 1));
                _bytecode.Emit(PythonOpcode.DictionaryMerge, 1, argument.Span);
            }

            FlushExpandedNamedKeywords(pendingNamed, ref haveDictionary, span);
            if (!haveDictionary)
                _bytecode.Emit(PythonOpcode.BuildMap, 0, span);
        }

        private void FlushExpandedNamedKeywords(
            List<SyntaxNode> pendingNamed,
            ref bool haveDictionary,
            TextSpan span)
        {
            if (pendingNamed.Count == 0)
                return;

            foreach (var argument in pendingNamed)
            {
                EmitLoadConstant(
                    new StringConstant(SyntaxAccess.GetToken(argument, 0).Text),
                    argument.Span);
                EmitExpression(SyntaxAccess.GetNode(argument, 2));
            }
            _bytecode.Emit(PythonOpcode.BuildMap, pendingNamed.Count, span);

            if (haveDictionary)
                _bytecode.Emit(PythonOpcode.DictionaryMerge, 1, span);
            else
                haveDictionary = true;
            pendingNamed.Clear();
        }

        private void EmitSlice(SyntaxNode expression)
        {
            var lower = SyntaxAccess.GetNode(expression, 0);
            var upper = SyntaxAccess.GetNode(expression, 2);
            var step = SyntaxAccess.GetNode(expression, 4);
            if (lower is null)
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
            else
                EmitExpression(lower);
            if (upper is null)
                EmitLoadConstant(PythonNoneConstant.Instance, expression.Span);
            else
                EmitExpression(upper);
            if (step is not null)
            {
                EmitExpression(step);
                _bytecode.Emit(PythonOpcode.BuildSlice, 3, expression.Span);
            }
            else
            {
                _bytecode.Emit(PythonOpcode.BuildSlice, 2, expression.Span);
            }
        }

        private void EmitSubscriptIndex(SyntaxNode? sliceList)
        {
            if (sliceList is null)
            {
                EmitLoadConstant(PythonNoneConstant.Instance, default);
                return;
            }

            var separated = SyntaxAccess.GetNode(sliceList, 0);
            var slices = separated?.ChildNodes().ToList() ?? [];
            if (slices.Count == 1)
            {
                EmitExpression(slices[0]);
                return;
            }

            foreach (var slice in slices)
                EmitExpression(slice);
            _bytecode.Emit(PythonOpcode.BuildTuple, slices.Count, sliceList.Span);
        }

        private void EmitTarget(SyntaxNode? target, AssignmentTargetMode mode)
        {
            if (target is null)
            {
                _diagnostics.Add(new EmitDiagnostic(
                    EmitDiagnosticCode.InvalidAssignmentTarget,
                    EmitDiagnosticSeverity.Error,
                    default,
                    "Missing assignment target."));
                return;
            }

            switch (target.Kind)
            {
                case SyntaxKind.NameExpression:
                    if (mode == AssignmentTargetMode.Store)
                        EmitNameStore(GetName(target), target.Span);
                    else
                        EmitNameDelete(GetName(target), target.Span);
                    return;

                case SyntaxKind.ParenthesizedExpression:
                    EmitTarget(SyntaxAccess.GetNode(target, 1), mode);
                    return;

                case SyntaxKind.TupleExpression:
                case SyntaxKind.ListExpression:
                    EmitSequenceTarget(target, mode);
                    return;

                case SyntaxKind.StarredExpression:
                    EmitTarget(SyntaxAccess.GetNode(target, 1), mode);
                    return;

                case SyntaxKind.AttributeExpression:
                    {
                        var receiver = SyntaxAccess.GetNode(target, 0);
                        var name = MangleName(SyntaxAccess.GetToken(target, 2).Text);
                        EmitExpression(receiver);
                        _bytecode.Emit(
                            mode == AssignmentTargetMode.Store
                                ? PythonOpcode.StoreAttribute
                                : PythonOpcode.DeleteAttribute,
                            GetNameIndex(name),
                            target.Span);
                        return;
                    }

                case SyntaxKind.SubscriptExpression:
                    EmitExpression(SyntaxAccess.GetNode(target, 0));
                    EmitSubscriptIndex(SyntaxAccess.GetNode(target, 2));
                    _bytecode.Emit(
                        mode == AssignmentTargetMode.Store
                            ? PythonOpcode.StoreSubscript
                            : PythonOpcode.DeleteSubscript,
                        sourceSpan: target.Span);
                    return;

                default:
                    AddError(EmitDiagnosticCode.InvalidAssignmentTarget, target,
                        $"Target kind {target.Kind} cannot be emitted.");
                    return;
            }
        }

        private void EmitSequenceTarget(SyntaxNode target, AssignmentTargetMode mode)
        {
            var separated = target.ChildNodes()
                .FirstOrDefault(static node => node.Kind == SyntaxKind.SeparatedSyntaxList);
            var elements = separated?.ChildNodes().ToList() ?? [];
            if (mode == AssignmentTargetMode.Delete)
            {
                foreach (var element in elements)
                    EmitTarget(element, mode);
                return;
            }

            var starred = elements.FindIndex(static element => element.Kind == SyntaxKind.StarredExpression);
            if (starred < 0)
            {
                _bytecode.Emit(PythonOpcode.UnpackSequence, elements.Count, target.Span);
            }
            else
            {
                var after = elements.Count - starred - 1;
                _bytecode.Emit(PythonOpcode.UnpackExtended, starred | (after << 8), target.Span);
            }

            foreach (var element in elements)
                EmitTarget(element, mode);
        }

        private void EmitConditionJumpFalse(SyntaxNode? condition, BytecodeLabel target, TextSpan span)
        {
            EmitExpression(condition);
            _bytecode.Emit(PythonOpcode.ToBoolean, sourceSpan: span);
            _bytecode.EmitConditionalJump(PythonOpcode.PopJumpIfFalse, target, span);
        }

        private void EmitNameLoad(string name, TextSpan span)
        {
            name = MangleName(name);
            var symbol = _nameSymbols.Lookup(name);

            if (_inInlinedComprehension)
            {
                EmitFunctionLikeNameLoad(name, symbol, span);
                return;
            }

            switch (_codeUnitKind)
            {
                case CodeUnitKind.Module:
                    _bytecode.Emit(PythonOpcode.LoadName, GetNameIndex(name), span);
                    return;

                case CodeUnitKind.Class:
                    if (symbol?.Scope == SymbolScope.GlobalExplicit)
                    {
                        _bytecode.Emit(PythonOpcode.LoadGlobal, GetNameIndex(name) << 1, span);
                        return;
                    }

                    if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
                    {
                        if (!TryGetLocalIndex(name, "Class closure", span, out var local))
                        {
                            EmitLoadConstant(PythonNoneConstant.Instance, span);
                            return;
                        }

                        _bytecode.Emit(PythonOpcode.LoadLocals, sourceSpan: span);
                        _bytecode.Emit(PythonOpcode.LoadFromDictionaryOrDereference, local, span);
                        return;
                    }

                    _bytecode.Emit(PythonOpcode.LoadName, GetNameIndex(name), span);
                    return;

                default:
                    EmitFunctionLikeNameLoad(name, symbol, span);
                    return;
            }
        }

        private void EmitFunctionLikeNameLoad(string name, Symbol? symbol, TextSpan span)
        {
            if (symbol?.Scope == SymbolScope.Local)
            {
                if (!TryGetActiveLocalIndex(name, "Local", span, out var local))
                {
                    EmitLoadConstant(PythonNoneConstant.Instance, span);
                    return;
                }

                _bytecode.Emit(PythonOpcode.LoadFastCheck, local, span);
                return;
            }

            if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
            {
                if (!TryGetActiveLocalIndex(name, "Closure", span, out var local))
                {
                    EmitLoadConstant(PythonNoneConstant.Instance, span);
                    return;
                }

                _bytecode.Emit(PythonOpcode.LoadDereference, local, span);
                return;
            }

            _bytecode.Emit(PythonOpcode.LoadGlobal, GetNameIndex(name) << 1, span);
        }

        private void EmitNameStore(string name, TextSpan span)
        {
            name = MangleName(name);
            var symbol = _nameSymbols.Lookup(name);

            if (_inInlinedComprehension)
            {
                EmitFunctionLikeNameStore(name, symbol, span);
                return;
            }

            switch (_codeUnitKind)
            {
                case CodeUnitKind.Module:
                    _bytecode.Emit(PythonOpcode.StoreName, GetNameIndex(name), span);
                    return;

                case CodeUnitKind.Class:
                    if (symbol?.Scope == SymbolScope.GlobalExplicit)
                    {
                        _bytecode.Emit(PythonOpcode.StoreGlobal, GetNameIndex(name), span);
                        return;
                    }

                    if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
                    {
                        if (!TryGetLocalIndex(name, "Class closure", span, out var local))
                        {
                            _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                            return;
                        }

                        _bytecode.Emit(PythonOpcode.StoreDereference, local, span);
                        return;
                    }

                    _bytecode.Emit(PythonOpcode.StoreName, GetNameIndex(name), span);
                    return;

                default:
                    EmitFunctionLikeNameStore(name, symbol, span);
                    return;
            }
        }

        private void EmitFunctionLikeNameStore(string name, Symbol? symbol, TextSpan span)
        {
            if (symbol?.Scope == SymbolScope.Local)
            {
                if (!TryGetActiveLocalIndex(name, "Local", span, out var local))
                {
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                    return;
                }

                _bytecode.Emit(PythonOpcode.StoreFast, local, span);
                return;
            }

            if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
            {
                if (!TryGetActiveLocalIndex(name, "Closure", span, out var local))
                {
                    _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                    return;
                }

                _bytecode.Emit(PythonOpcode.StoreDereference, local, span);
                return;
            }

            _bytecode.Emit(PythonOpcode.StoreGlobal, GetNameIndex(name), span);
        }

        private void EmitNameDelete(string name, TextSpan span)
        {
            name = MangleName(name);
            var symbol = _nameSymbols.Lookup(name);

            if (_inInlinedComprehension)
            {
                EmitFunctionLikeNameDelete(name, symbol, span);
                return;
            }

            switch (_codeUnitKind)
            {
                case CodeUnitKind.Module:
                    _bytecode.Emit(PythonOpcode.DeleteName, GetNameIndex(name), span);
                    return;

                case CodeUnitKind.Class:
                    if (symbol?.Scope == SymbolScope.GlobalExplicit)
                    {
                        _bytecode.Emit(PythonOpcode.DeleteGlobal, GetNameIndex(name), span);
                        return;
                    }

                    if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
                    {
                        if (TryGetLocalIndex(name, "Class closure", span, out var local))
                            _bytecode.Emit(PythonOpcode.DeleteDereference, local, span);
                        return;
                    }

                    _bytecode.Emit(PythonOpcode.DeleteName, GetNameIndex(name), span);
                    return;

                default:
                    EmitFunctionLikeNameDelete(name, symbol, span);
                    return;
            }
        }

        private void EmitFunctionLikeNameDelete(string name, Symbol? symbol, TextSpan span)
        {
            if (symbol?.Scope == SymbolScope.Local)
            {
                if (TryGetActiveLocalIndex(name, "Local", span, out var local))
                    _bytecode.Emit(PythonOpcode.DeleteFast, local, span);
                return;
            }

            if (symbol?.Scope is SymbolScope.Cell or SymbolScope.Free)
            {
                if (TryGetActiveLocalIndex(name, "Closure", span, out var local))
                    _bytecode.Emit(PythonOpcode.DeleteDereference, local, span);
                return;
            }

            _bytecode.Emit(PythonOpcode.DeleteGlobal, GetNameIndex(name), span);
        }

        private bool TryGetActiveLocalIndex(
            string name,
            string category,
            TextSpan span,
            out int localIndex)
        {
            if (_inInlinedComprehension &&
                IsBoundByInlinedComprehension(name) &&
                _hiddenComprehensionLocalIndices.TryGetValue(name, out localIndex))
            {
                return true;
            }

            return TryGetLocalIndex(name, category, span, out localIndex);
        }

        private bool IsBoundByInlinedComprehension(string name)
        {
            for (SymbolTable? table = _nameSymbols;
                 table is not null && table.IsComprehensionInlined;
                 table = table.Parent)
            {
                var symbol = table.Lookup(name);
                if (symbol?.Scope is SymbolScope.Local or SymbolScope.Cell)
                    return true;
            }

            return false;
        }

        private bool TryGetLocalIndex(
            string name,
            string category,
            TextSpan span,
            out int localIndex)
        {
            if (_localIndices.TryGetValue(name, out localIndex))
                return true;

            AddError(EmitDiagnosticCode.InternalEmitterError, span,
                $"{category} name {name} is missing from locals-plus layout.");
            return false;
        }

        private void EmitClassNamespaceStore(string name, TextSpan span)
        {
            _bytecode.Emit(PythonOpcode.StoreName, GetNameIndex(name), span);
        }

        private void EmitDirectDereferenceStore(string name, TextSpan span)
        {
            if (!TryGetLocalIndex(name, "Class cell", span, out var local))
            {
                _bytecode.Emit(PythonOpcode.PopTop, sourceSpan: span);
                return;
            }

            _bytecode.Emit(PythonOpcode.StoreDereference, local, span);
        }

        private void EmitClosureCellLoad(string name, TextSpan span)
        {
            if (!TryGetActiveLocalIndex(name, "Free variable", span, out var local))
            {
                EmitLoadConstant(PythonNoneConstant.Instance, span);
                return;
            }

            var kind = _localsPlusKinds[local];
            if ((kind & (LocalKind.Cell | LocalKind.Free)) == 0)
            {
                AddError(EmitDiagnosticCode.InternalEmitterError, span,
                    $"Free variable {name} does not resolve to a cell in the enclosing code unit.");
                EmitLoadConstant(PythonNoneConstant.Instance, span);
                return;
            }

            // LOAD_CLOSURE is a CPython pseudo-op that lowers to LOAD_FAST in 3.14.
            _bytecode.Emit(PythonOpcode.LoadFast, local, span);
        }

        private void EmitLoadConstant(PythonConstant constant, TextSpan span)
        {
            _bytecode.Emit(PythonOpcode.LoadConstant, GetConstantIndex(constant), span);
        }

        private int GetConstantIndex(PythonConstant constant)
        {
            for (var i = 0; i < _constants.Count; i++)
            {
                if (PythonConstantEquality.Equals(_constants[i], constant))
                    return i;
            }

            var index = _constants.Count;
            _constants.Add(constant);
            return index;
        }

        private void EnsureNoneConstant()
        {
            if (_constants.Count == 0)
                _constants.Add(PythonNoneConstant.Instance);
            else if (_constants[0] is not PythonNoneConstant)
                _constants.Insert(0, PythonNoneConstant.Instance);
        }

        private int GetNameIndex(string name)
        {
            if (_nameIndices.TryGetValue(name, out var index))
                return index;
            index = _names.Count;
            _names.Add(name);
            _nameIndices.Add(name, index);
            return index;
        }

        private string MangleName(string name)
        {
            if (string.IsNullOrEmpty(_privateName) ||
                name.Length < 2 ||
                name[0] != '_' ||
                name[1] != '_' ||
                name.Contains(".", StringComparison.Ordinal) ||
                name.EndsWith("__", StringComparison.Ordinal))
            {
                return name;
            }

            var start = 0;
            while (start < _privateName.Length && _privateName[start] == '_')
                start++;
            if (start == _privateName.Length)
                return name;

            return "_" + _privateName[start..] + name;
        }

        private static string? GetPrivateName(SymbolTable symbols)
        {
            for (SymbolTable? current = symbols; current is not null; current = current.Parent)
            {
                if (current.Kind == SymbolTableKind.Class)
                    return current.Name;
            }
            return null;
        }

        private static string GetName(SyntaxNode expression) =>
            SyntaxAccess.GetToken(expression, 0).Text;

        private static string GetDottedName(SyntaxNode? dotted)
        {
            if (dotted is null)
                return string.Empty;
            return string.Concat(dotted.ChildTokens().Select(static token => token.Text));
        }

        private static bool TryMapBinaryOperator(
            SyntaxKind kind,
            bool inPlace,
            out PythonBinaryOperation operation)
        {
            operation = (kind, inPlace) switch
            {
                (SyntaxKind.PlusToken, false) => PythonBinaryOperation.Add,
                (SyntaxKind.AmpersandToken, false) => PythonBinaryOperation.And,
                (SyntaxKind.DoubleSlashToken, false) => PythonBinaryOperation.FloorDivide,
                (SyntaxKind.LeftShiftToken, false) => PythonBinaryOperation.LeftShift,
                (SyntaxKind.AtToken, false) => PythonBinaryOperation.MatrixMultiply,
                (SyntaxKind.StarToken, false) => PythonBinaryOperation.Multiply,
                (SyntaxKind.PercentToken, false) => PythonBinaryOperation.Remainder,
                (SyntaxKind.PipeToken, false) => PythonBinaryOperation.Or,
                (SyntaxKind.DoubleStarToken, false) => PythonBinaryOperation.Power,
                (SyntaxKind.RightShiftToken, false) => PythonBinaryOperation.RightShift,
                (SyntaxKind.MinusToken, false) => PythonBinaryOperation.Subtract,
                (SyntaxKind.SlashToken, false) => PythonBinaryOperation.TrueDivide,
                (SyntaxKind.CaretToken, false) => PythonBinaryOperation.Xor,

                (SyntaxKind.PlusEqualToken, true) => PythonBinaryOperation.InPlaceAdd,
                (SyntaxKind.AmpersandEqualToken, true) => PythonBinaryOperation.InPlaceAnd,
                (SyntaxKind.DoubleSlashEqualToken, true) => PythonBinaryOperation.InPlaceFloorDivide,
                (SyntaxKind.LeftShiftEqualToken, true) => PythonBinaryOperation.InPlaceLeftShift,
                (SyntaxKind.AtEqualToken, true) => PythonBinaryOperation.InPlaceMatrixMultiply,
                (SyntaxKind.StarEqualToken, true) => PythonBinaryOperation.InPlaceMultiply,
                (SyntaxKind.PercentEqualToken, true) => PythonBinaryOperation.InPlaceRemainder,
                (SyntaxKind.PipeEqualToken, true) => PythonBinaryOperation.InPlaceOr,
                (SyntaxKind.DoubleStarEqualToken, true) => PythonBinaryOperation.InPlacePower,
                (SyntaxKind.RightShiftEqualToken, true) => PythonBinaryOperation.InPlaceRightShift,
                (SyntaxKind.MinusEqualToken, true) => PythonBinaryOperation.InPlaceSubtract,
                (SyntaxKind.SlashEqualToken, true) => PythonBinaryOperation.InPlaceTrueDivide,
                (SyntaxKind.CaretEqualToken, true) => PythonBinaryOperation.InPlaceXor,
                _ => (PythonBinaryOperation)byte.MaxValue,
            };
            return (byte)operation != byte.MaxValue;
        }

        private StringConstant? TryGetDocString(
            SyntaxNode? container,
            out SyntaxNode? expressionStatement)
        {
            expressionStatement = null;
            if (container is null)
                return null;

            var statements = EnumerateStatements(container).ToList();
            if (statements.Count == 0 || statements[0].Kind != SyntaxKind.ExpressionStatement)
                return null;

            var expression = SyntaxAccess.GetNode(statements[0], 0);
            if (expression is null)
                return null;

            PythonConstant? constant = null;
            if (expression.Kind == SyntaxKind.LiteralExpression)
            {
                var token = expression.ChildTokens().FirstOrDefault();
                if (LiteralParser.TryParse(token, out var parsed, out _))
                    constant = parsed;
            }
            else if (expression.Kind == SyntaxKind.StringConcatenationExpression)
            {
                var list = SyntaxAccess.GetNode(expression, 0);
                var values = new List<string>();
                if (list is not null)
                {
                    foreach (var item in list.ChildNodes())
                    {
                        var token = item.ChildTokens().FirstOrDefault();
                        if (!LiteralParser.TryParse(token, out var itemConstant, out _) ||
                            itemConstant is not StringConstant text)
                        {
                            return null;
                        }
                        values.Add(text.Value);
                    }
                }
                constant = new StringConstant(string.Concat(values));
            }

            if (constant is not StringConstant result)
                return null;
            expressionStatement = statements[0];
            return result;
        }

        private static IEnumerable<SyntaxNode> EnumerateStatements(SyntaxNode container)
        {
            switch (container.Kind)
            {
                case SyntaxKind.Suite:
                case SyntaxKind.CompilationUnit:
                    foreach (var child in container.ChildNodes())
                    {
                        foreach (var nested in EnumerateStatements(child))
                            yield return nested;
                    }
                    yield break;

                case SyntaxKind.SyntaxList:
                case SyntaxKind.SimpleStatementList:
                    foreach (var child in container.ChildNodes())
                    {
                        if (child.Kind is SyntaxKind.SyntaxList or SyntaxKind.SimpleStatementList or SyntaxKind.Suite)
                        {
                            foreach (var nested in EnumerateStatements(child))
                                yield return nested;
                        }
                        else
                        {
                            yield return child;
                        }
                    }
                    yield break;

                default:
                    yield return container;
                    yield break;
            }
        }

        private int GetLineNumber(int position)
        {
            var line = Math.Max(1, _options.FirstLineNumber);
            var text = _syntaxTree.Text;
            var limit = Math.Min(position, text.Length);
            for (var i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                    line++;
            }
            return line;
        }

        private void AddError(EmitDiagnosticCode code, SyntaxNode node, string message) =>
            AddError(code, node.Span, message);

        private void AddError(EmitDiagnosticCode code, TextSpan span, string message)
        {
            _diagnostics.Add(new EmitDiagnostic(
                code,
                EmitDiagnosticSeverity.Error,
                span,
                message));
        }

        private bool HasErrors()
        {
            foreach (var diagnostic in _diagnostics)
            {
                if (diagnostic.Severity == EmitDiagnosticSeverity.Error)
                    return true;
            }
            return false;
        }
    }

    internal static class SyntaxAccess
    {
        public static SyntaxNode? GetNode(SyntaxNode node, int slot)
        {
            if ((uint)slot >= (uint)node.Green.SlotCount)
                return null;
            var green = node.Green.GetSlot(slot);
            if (green is null || green is GreenToken)
                return null;
            return new SyntaxNode(
                node.SyntaxTree,
                node,
                green,
                GetSlotPosition(node, slot),
                slot);
        }

        public static SyntaxToken GetToken(SyntaxNode node, int slot)
        {
            if ((uint)slot >= (uint)node.Green.SlotCount)
                return default;
            if (node.Green.GetSlot(slot) is not GreenToken green)
                return default;
            return new SyntaxToken(
                node.SyntaxTree,
                node,
                green,
                GetSlotPosition(node, slot),
                slot);
        }

        public static List<SyntaxNode> GetChildNodes(SyntaxNode node) =>
            node.ChildNodes().ToList();

        private static int GetSlotPosition(SyntaxNode node, int slot)
        {
            var position = node.Position;
            for (var i = 0; i < slot; i++)
            {
                if (node.Green.GetSlot(i) is { } child)
                    position = checked(position + child.FullWidth);
            }
            return position;
        }
    }

    internal static class PythonConstantEquality
    {
        public static bool Equals(PythonConstant left, PythonConstant right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left.Kind != right.Kind)
                return false;

            return (left, right) switch
            {
                (PythonNoneConstant, PythonNoneConstant) => true,
                (EllipsisConstant, EllipsisConstant) => true,
                (BooleanConstant a, BooleanConstant b) => a.Value == b.Value,
                (IntegerConstant a, IntegerConstant b) => a.Value == b.Value,
                (FloatConstant a, FloatConstant b) =>
                    BitConverter.DoubleToInt64Bits(a.Value) == BitConverter.DoubleToInt64Bits(b.Value),
                (ComplexConstant a, ComplexConstant b) =>
                    BitConverter.DoubleToInt64Bits(a.Real) == BitConverter.DoubleToInt64Bits(b.Real) &&
                    BitConverter.DoubleToInt64Bits(a.Imaginary) == BitConverter.DoubleToInt64Bits(b.Imaginary),
                (StringConstant a, StringConstant b) =>
                    string.Equals(a.Value, b.Value, StringComparison.Ordinal),
                (BytesConstant a, BytesConstant b) => a.Value.AsSpan().SequenceEqual(b.Value.AsSpan()),
                (TupleConstant a, TupleConstant b) => SequenceEquals(a.Items, b.Items),
                (FrozenSetConstant a, FrozenSetConstant b) => SequenceEquals(a.Items, b.Items),
                (CodeConstant a, CodeConstant b) => ReferenceEquals(a.Value, b.Value),
                _ => false,
            };
        }

        private static bool SequenceEquals(ImmutableArray<PythonConstant> left, ImmutableArray<PythonConstant> right)
        {
            if (left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (!Equals(left[i], right[i]))
                    return false;
            }
            return true;
        }
    }


    internal static class LiteralParser
    {
        public static bool TryParse(SyntaxToken token, out PythonConstant constant, out string? error)
        {
            switch (token.Kind)
            {
                case SyntaxKind.NoneKeyword:
                    constant = PythonNoneConstant.Instance;
                    error = null;
                    return true;

                case SyntaxKind.TrueKeyword:
                    constant = new BooleanConstant(true);
                    error = null;
                    return true;

                case SyntaxKind.FalseKeyword:
                    constant = new BooleanConstant(false);
                    error = null;
                    return true;

                case SyntaxKind.EllipsisToken:
                    constant = EllipsisConstant.Instance;
                    error = null;
                    return true;

                case SyntaxKind.NumberToken:
                    return TryParseNumber(token.Text, out constant, out error);

                case SyntaxKind.StringToken:
                    return TryParseString(token.Text, out constant, out error);

                default:
                    constant = PythonNoneConstant.Instance;
                    error = $"Token {token.Kind} is not a constant literal.";
                    return false;
            }
        }

        public static bool TryParseFormattedText(
            string text,
            bool raw,
            out string value,
            out string? error)
        {
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if ((ch == '{' || ch == '}') &&
                    i + 1 < text.Length &&
                    text[i + 1] == ch)
                {
                    builder.Append(ch);
                    i++;
                }
                else
                {
                    builder.Append(ch);
                }
            }

            var normalized = builder.ToString();
            if (raw)
            {
                value = normalized;
                error = null;
                return true;
            }

            if (!TryDecodeUnicode(normalized, out var constant, out error) ||
                constant is not StringConstant stringConstant)
            {
                value = string.Empty;
                error ??= "Invalid formatted-string text.";
                return false;
            }

            value = stringConstant.Value;
            return true;
        }

        private static bool TryParseNumber(string text, out PythonConstant constant, out string? error)
        {
            var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal);
            var imaginary = normalized.EndsWith("j", StringComparison.OrdinalIgnoreCase);
            if (imaginary)
                normalized = normalized[..^1];

            if (imaginary || normalized.IndexOfAny(['.', 'e', 'E']) >= 0)
            {
                if (double.TryParse(
                        normalized,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var floating))
                {
                    constant = imaginary
                        ? new ComplexConstant(0, floating)
                        : new FloatConstant(floating);
                    error = null;
                    return true;
                }

                constant = PythonNoneConstant.Instance;
                error = $"Invalid floating-point literal '{text}'.";
                return false;
            }

            var numberBase = 10;
            var digits = normalized;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                numberBase = 16;
                digits = normalized[2..];
            }
            else if (normalized.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                numberBase = 8;
                digits = normalized[2..];
            }
            else if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                numberBase = 2;
                digits = normalized[2..];
            }

            if (!TryParseBigInteger(digits, numberBase, out var integer))
            {
                constant = PythonNoneConstant.Instance;
                error = $"Invalid integer literal '{text}'.";
                return false;
            }

            constant = imaginary
                ? new ComplexConstant(0, (double)integer)
                : new IntegerConstant(integer);
            error = null;
            return true;
        }

        private static bool TryParseBigInteger(string digits, int numberBase, out BigInteger value)
        {
            value = BigInteger.Zero;
            if (digits.Length == 0)
                return false;

            foreach (var ch in digits)
            {
                int digit;
                if (ch is >= '0' and <= '9')
                    digit = ch - '0';
                else if (ch is >= 'a' and <= 'f')
                    digit = ch - 'a' + 10;
                else if (ch is >= 'A' and <= 'F')
                    digit = ch - 'A' + 10;
                else
                    return false;

                if (digit >= numberBase)
                    return false;

                value = value * numberBase + digit;
            }

            return true;
        }

        private static bool TryParseString(string text, out PythonConstant constant, out string? error)
        {
            var quoteIndex = text.IndexOfAny(['\'', '"']);
            if (quoteIndex < 0)
            {
                constant = PythonNoneConstant.Instance;
                error = "String literal does not contain a quote delimiter.";
                return false;
            }

            var prefix = text[..quoteIndex].ToLowerInvariant();
            var isRaw = prefix.Contains('r');
            var isBytes = prefix.Contains('b');
            if (prefix.Contains('f') || prefix.Contains('t'))
            {
                constant = PythonNoneConstant.Instance;
                error = "Formatted and template strings are emitted by a separate expression path.";
                return false;
            }

            var quote = text[quoteIndex];
            var triple = quoteIndex + 2 < text.Length &&
                text[quoteIndex + 1] == quote &&
                text[quoteIndex + 2] == quote;
            var delimiterLength = triple ? 3 : 1;
            var contentStart = quoteIndex + delimiterLength;
            var contentLength = text.Length - contentStart - delimiterLength;
            if (contentLength < 0)
            {
                constant = PythonNoneConstant.Instance;
                error = "Unterminated string literal.";
                return false;
            }

            var content = text.Substring(contentStart, contentLength);
            if (isRaw)
            {
                if (isBytes)
                    return TryEncodeRawBytes(content, out constant, out error);

                constant = new StringConstant(content);
                error = null;
                return true;
            }

            return isBytes
                ? TryDecodeBytes(content, out constant, out error)
                : TryDecodeUnicode(content, out constant, out error);
        }

        private static bool TryEncodeRawBytes(
            string content,
            out PythonConstant constant,
            out string? error)
        {
            var bytes = new List<byte>(content.Length);
            foreach (var ch in content)
            {
                if (ch > 0x7F)
                {
                    constant = PythonNoneConstant.Instance;
                    error = "Bytes literals may contain only ASCII source characters or escapes.";
                    return false;
                }

                bytes.Add((byte)ch);
            }

            constant = new BytesConstant(bytes);
            error = null;
            return true;
        }

        private static bool TryDecodeUnicode(string content, out PythonConstant constant, out string? error)
        {
            var builder = new StringBuilder(content.Length);
            for (var i = 0; i < content.Length; i++)
            {
                var ch = content[i];
                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (++i >= content.Length)
                {
                    constant = PythonNoneConstant.Instance;
                    error = "String literal ends with an incomplete escape sequence.";
                    return false;
                }

                var escaped = content[i];
                if (TryAppendSimpleEscape(builder, escaped))
                    continue;

                if (escaped == '\n')
                    continue;
                if (escaped == '\r')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                        i++;
                    continue;
                }

                if (escaped == 'x')
                {
                    if (!TryReadHex(content, ref i, 2, out var value))
                    {
                        constant = PythonNoneConstant.Instance;
                        error = "Invalid \\x escape in string literal.";
                        return false;
                    }
                    builder.Append((char)value);
                    continue;
                }

                if (escaped == 'u' || escaped == 'U')
                {
                    var width = escaped == 'u' ? 4 : 8;
                    if (!TryReadHex(content, ref i, width, out var value) ||
                        value > 0x10FFFF)
                    {
                        constant = PythonNoneConstant.Instance;
                        error = $"Invalid \\{escaped} escape in string literal.";
                        return false;
                    }

                    if (value <= char.MaxValue)
                        builder.Append((char)value);
                    else
                        builder.Append(char.ConvertFromUtf32(value));
                    continue;
                }

                if (escaped == 'N')
                {
                    constant = PythonNoneConstant.Instance;
                    error = "Named Unicode escapes (\\N{...}) are not implemented by the emitter yet.";
                    return false;
                }

                if (escaped is >= '0' and <= '7')
                {
                    var value = escaped - '0';
                    var consumed = 1;
                    while (consumed < 3 && i + 1 < content.Length && content[i + 1] is >= '0' and <= '7')
                    {
                        value = value * 8 + (content[++i] - '0');
                        consumed++;
                    }
                    builder.Append((char)value);
                    continue;
                }

                // Unknown escapes in strings are preserved
                builder.Append('\\').Append(escaped);
            }

            constant = new StringConstant(builder.ToString());
            error = null;
            return true;
        }

        private static bool TryDecodeBytes(
            string content,
            out PythonConstant constant,
            out string? error)
        {
            var bytes = new List<byte>(content.Length);
            for (var i = 0; i < content.Length; i++)
            {
                var ch = content[i];
                if (ch != '\\')
                {
                    if (ch > 0x7F)
                    {
                        constant = PythonNoneConstant.Instance;
                        error = "Bytes literals may contain only ASCII source characters or escapes.";
                        return false;
                    }
                    bytes.Add((byte)ch);
                    continue;
                }

                if (++i >= content.Length)
                {
                    constant = PythonNoneConstant.Instance;
                    error = "Bytes literal ends with an incomplete escape sequence.";
                    return false;
                }

                var escaped = content[i];
                if (TryGetSimpleEscape(escaped, out var simple))
                {
                    bytes.Add((byte)simple);
                    continue;
                }

                if (escaped == '\n')
                    continue;
                if (escaped == '\r')
                {
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                        i++;
                    continue;
                }

                if (escaped == 'x')
                {
                    if (!TryReadHex(content, ref i, 2, out var value))
                    {
                        constant = PythonNoneConstant.Instance;
                        error = "Invalid \\x escape in bytes literal.";
                        return false;
                    }
                    bytes.Add((byte)value);
                    continue;
                }

                if (escaped is >= '0' and <= '7')
                {
                    var value = escaped - '0';
                    var consumed = 1;
                    while (consumed < 3 && i + 1 < content.Length && content[i + 1] is >= '0' and <= '7')
                    {
                        value = value * 8 + (content[++i] - '0');
                        consumed++;
                    }
                    bytes.Add((byte)value);
                    continue;
                }

                bytes.Add((byte)'\\');
                if (escaped > 0x7F)
                {
                    constant = PythonNoneConstant.Instance;
                    error = "Bytes escape contains a non-ASCII source character.";
                    return false;
                }
                bytes.Add((byte)escaped);
            }

            constant = new BytesConstant(bytes);
            error = null;
            return true;
        }

        private static bool TryAppendSimpleEscape(StringBuilder builder, char escaped)
        {
            if (!TryGetSimpleEscape(escaped, out var value))
                return false;
            builder.Append(value);
            return true;
        }

        private static bool TryGetSimpleEscape(char escaped, out char value)
        {
            value = escaped switch
            {
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'v' => '\v',
                _ => '\0',
            };
            return value != '\0';
        }

        private static bool TryReadHex(string text, ref int escapeCharacterIndex, int digits, out int value)
        {
            value = 0;
            if (escapeCharacterIndex + digits >= text.Length)
                return false;

            for (var j = 0; j < digits; j++)
            {
                var ch = text[++escapeCharacterIndex];
                int digit;
                if (ch is >= '0' and <= '9')
                    digit = ch - '0';
                else if (ch is >= 'a' and <= 'f')
                    digit = ch - 'a' + 10;
                else if (ch is >= 'A' and <= 'F')
                    digit = ch - 'A' + 10;
                else
                    return false;
                value = checked(value * 16 + digit);
            }

            return true;
        }
    }
}
