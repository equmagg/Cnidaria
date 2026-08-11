using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    public sealed class TrimmingOptions
    {
        public static TrimmingOptions Default { get; } = new TrimmingOptions();

        public bool Enabled { get; }
        public bool PreserveExternallyVisibleSymbols { get; }
        public ImmutableHashSet<string> RootSymbols { get; }

        public TrimmingOptions(
            bool enabled = true,
            bool preserveExternallyVisibleSymbols = true,
            IEnumerable<string>? rootSymbols = null)
        {
            Enabled = enabled;
            PreserveExternallyVisibleSymbols = preserveExternallyVisibleSymbols;
            RootSymbols = rootSymbols is null
                ? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal)
                : rootSymbols.ToImmutableHashSet(StringComparer.Ordinal);
        }
    }

    internal readonly struct TrimResult
    {
        public ControlFlowGraph ControlFlowGraph { get; }
        public ImmutableArray<SsaFunction> Functions { get; }

        public TrimResult(
            ControlFlowGraph controlFlowGraph,
            ImmutableArray<SsaFunction> functions)
        {
            ControlFlowGraph = controlFlowGraph;
            Functions = functions;
        }
    }

    internal static class Trimmer
    {
        public static TrimResult Trim(
            ControlFlowGraph controlFlowGraph,
            ImmutableArray<SsaFunction> functions,
            TrimmingOptions options)
        {
            if (controlFlowGraph is null)
                throw new ArgumentNullException(nameof(controlFlowGraph));
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            if (!options.Enabled)
                return new TrimResult(controlFlowGraph, functions);

            return new Pass(controlFlowGraph, functions, options).Run();
        }

        private sealed class Pass
        {
            private readonly ControlFlowGraph _controlFlowGraph;
            private readonly GimpleTree _tree;
            private readonly FileScopeLinkageMap _fileScopeLinkage;
            private readonly ImmutableArray<SsaFunction> _ssaFunctions;
            private readonly TrimmingOptions _options;
            private readonly Dictionary<FunctionSymbol, List<GimpleFunctionDefinition>> _functionsBySymbol = new();
            private readonly Dictionary<string, List<GimpleFunctionDefinition>> _functionsByName = new(StringComparer.Ordinal);
            private readonly Dictionary<Symbol, List<GimpleVariableDeclaration>> _globalsBySymbol = new();
            private readonly Dictionary<string, List<GimpleVariableDeclaration>> _globalsByName = new(StringComparer.Ordinal);
            private readonly Dictionary<GimpleFunctionDefinition, SsaFunction> _ssaByFunction = new();
            private readonly HashSet<GimpleFunctionDefinition> _liveFunctions = new();
            private readonly HashSet<GimpleVariableDeclaration> _liveGlobals = new();
            private readonly Queue<GimpleFunctionDefinition> _functionWorkList = new();
            private readonly Queue<GimpleVariableDeclaration> _globalWorkList = new();

            public Pass(
                ControlFlowGraph controlFlowGraph,
                ImmutableArray<SsaFunction> ssaFunctions,
                TrimmingOptions options)
            {
                _controlFlowGraph = controlFlowGraph;
                _tree = controlFlowGraph.GimpleTree;
                _fileScopeLinkage = FileScopeLinkageMap.Create(_tree.SemanticModel);
                _ssaFunctions = ssaFunctions.IsDefault ? ImmutableArray<SsaFunction>.Empty : ssaFunctions;
                _options = options;
            }

            public TrimResult Run()
            {
                IndexMembers();
                MarkRoots();
                ProcessWorkLists();
                return Rewrite();
            }

            private void IndexMembers()
            {
                foreach (var function in _ssaFunctions)
                    _ssaByFunction[function.Function] = function;

                foreach (var member in _tree.Members)
                {
                    if (member is GimpleFunctionDefinition function)
                    {
                        if (function.Symbol is not null)
                        {
                            Add(_functionsBySymbol, function.Symbol, function);
                            Add(_functionsByName, function.Symbol.Name, function);
                        }

                        continue;
                    }

                    if (member is not GimpleGlobalDeclaration global)
                        continue;

                    foreach (var declaration in global.Declarators)
                    {
                        if (!IsObjectDeclaration(declaration) || declaration.Symbol is null)
                            continue;

                        Add(_globalsBySymbol, declaration.Symbol, declaration);
                        Add(_globalsByName, declaration.Symbol.Name, declaration);
                    }
                }
            }

            private void MarkRoots()
            {
                foreach (var member in _tree.Members)
                {
                    if (member is GimpleFunctionDefinition function)
                    {
                        if (function.Symbol is null ||
                            _options.RootSymbols.Contains(function.Symbol.Name) ||
                            (_options.PreserveExternallyVisibleSymbols && !_fileScopeLinkage.IsInternal(function.Symbol)))
                        {
                            MarkFunction(function);
                        }

                        continue;
                    }

                    if (member is not GimpleGlobalDeclaration global)
                        continue;

                    foreach (var declaration in global.Declarators)
                    {
                        if (!IsObjectDeclaration(declaration))
                            continue;

                        if (declaration.Symbol is null)
                        {
                            MarkGlobal(declaration);
                        }
                        else if (_options.RootSymbols.Contains(declaration.Symbol.Name) ||
                            (_options.PreserveExternallyVisibleSymbols && !_fileScopeLinkage.IsInternal(declaration.Symbol)))
                        {
                            MarkGlobalsByName(declaration.Symbol.Name);
                        }
                    }
                }

                foreach (var root in _options.RootSymbols)
                    MarkByName(root);
            }

            private void ProcessWorkLists()
            {
                var references = new HashSet<Symbol>();
                var names = new HashSet<string>(StringComparer.Ordinal);

                while (_functionWorkList.Count != 0 || _globalWorkList.Count != 0)
                {
                    while (_functionWorkList.Count != 0)
                    {
                        var function = _functionWorkList.Dequeue();
                        references.Clear();
                        names.Clear();
                        CollectFunctionReferences(function, references, names);
                        MarkReferences(references, names);
                    }

                    while (_globalWorkList.Count != 0)
                    {
                        var global = _globalWorkList.Dequeue();
                        references.Clear();
                        names.Clear();
                        if (global.Initializer is not null)
                            SymbolCollector.Collect(global.Initializer, references, names);
                        MarkReferences(references, names);
                    }
                }
            }

            private void CollectFunctionReferences(
                GimpleFunctionDefinition function,
                HashSet<Symbol> references,
                HashSet<string> names)
            {
                if (_ssaByFunction.TryGetValue(function, out var ssaFunction))
                {
                    foreach (var block in EnumerateReachableBlocks(ssaFunction))
                    {
                        foreach (var instruction in block.Instructions)
                            SymbolCollector.Collect(instruction.Statement, references, names);
                    }

                    return;
                }

                foreach (var block in function.Blocks)
                {
                    foreach (var statement in block.Statements)
                        SymbolCollector.Collect(statement, references, names);
                }
            }

            private static IEnumerable<SsaBlock> EnumerateReachableBlocks(SsaFunction function)
            {
                if (function.Blocks.Length == 0)
                    yield break;

                var byControlFlowBlock = new Dictionary<ControlFlowBlock, SsaBlock>();
                foreach (var block in function.Blocks)
                    byControlFlowBlock[block.ControlFlowBlock] = block;

                if (!byControlFlowBlock.TryGetValue(function.ControlFlowFunction.Entry, out var entry))
                    entry = function.Blocks[0];

                var visited = new HashSet<ControlFlowBlock>();
                var stack = new Stack<SsaBlock>();
                visited.Add(entry.ControlFlowBlock);
                stack.Push(entry);

                while (stack.Count != 0)
                {
                    var block = stack.Pop();
                    yield return block;

                    foreach (var successor in EnumerateOptimizedSuccessors(function.ControlFlowFunction, block))
                    {
                        if (successor.IsExit || !visited.Add(successor))
                            continue;

                        if (byControlFlowBlock.TryGetValue(successor, out var successorBlock))
                            stack.Push(successorBlock);
                    }
                }
            }

            private static IEnumerable<ControlFlowBlock> EnumerateOptimizedSuccessors(
                ControlFlowFunction function,
                SsaBlock block)
            {
                var terminator = block.Instructions.Length == 0 ? null : block.Instructions[^1].Statement;
                switch (terminator)
                {
                    case GimpleGotoStatement gotoStatement:
                        if (function.TryGetBlock(gotoStatement.Target, out var gotoTarget) && gotoTarget is not null)
                            yield return gotoTarget;
                        yield break;

                    case GimpleConditionalGotoStatement conditional:
                        if (function.TryGetBlock(conditional.WhenTrue, out var trueTarget) && trueTarget is not null)
                            yield return trueTarget;
                        if (function.TryGetBlock(conditional.WhenFalse, out var falseTarget) && falseTarget is not null && !ReferenceEquals(falseTarget, trueTarget))
                            yield return falseTarget;
                        yield break;

                    case GimpleSwitchStatement switchStatement:
                        var seen = new HashSet<ControlFlowBlock>();
                        foreach (var switchCase in switchStatement.Cases)
                        {
                            if (function.TryGetBlock(switchCase.Target, out var caseTarget) && caseTarget is not null && seen.Add(caseTarget))
                                yield return caseTarget;
                        }

                        if (function.TryGetBlock(switchStatement.DefaultLabel, out var defaultTarget) && defaultTarget is not null && seen.Add(defaultTarget))
                            yield return defaultTarget;
                        yield break;

                    case GimpleReturnStatement:
                        yield break;
                }

                foreach (var edge in block.ControlFlowBlock.Successors)
                {
                    if (!edge.Target.IsExit)
                        yield return edge.Target;
                }
            }

            private void MarkReferences(HashSet<Symbol> references, HashSet<string> names)
            {
                foreach (var symbol in references)
                    MarkSymbol(symbol);
                foreach (var name in names)
                    MarkByName(name);
            }

            private void MarkSymbol(Symbol symbol)
            {
                if (symbol is FunctionSymbol function)
                {
                    if (_functionsBySymbol.TryGetValue(function, out var definitions))
                    {
                        foreach (var definition in definitions)
                            MarkFunction(definition);
                    }
                    else
                    {
                        MarkByName(function.Name);
                    }

                    return;
                }

                if (_globalsBySymbol.TryGetValue(symbol, out var globals))
                {
                    foreach (var global in globals)
                        MarkGlobal(global);
                    MarkGlobalsByName(symbol.Name);
                    return;
                }

                if (symbol is VariableSymbol { StorageClass: StorageClass.Extern })
                    MarkGlobalsByName(symbol.Name);
            }

            private void MarkByName(string name)
            {
                if (_functionsByName.TryGetValue(name, out var functions))
                {
                    foreach (var function in functions)
                        MarkFunction(function);
                }

                MarkGlobalsByName(name);
            }

            private void MarkGlobalsByName(string name)
            {
                if (!_globalsByName.TryGetValue(name, out var globals))
                    return;

                foreach (var global in globals)
                    MarkGlobal(global);
            }

            private void MarkFunction(GimpleFunctionDefinition function)
            {
                if (_liveFunctions.Add(function))
                    _functionWorkList.Enqueue(function);
            }

            private void MarkGlobal(GimpleVariableDeclaration global)
            {
                if (_liveGlobals.Add(global))
                    _globalWorkList.Enqueue(global);
            }

            private TrimResult Rewrite()
            {
                var members = ImmutableArray.CreateBuilder<GimpleNode>(_tree.Members.Length);
                foreach (var member in _tree.Members)
                {
                    switch (member)
                    {
                        case GimpleFunctionDefinition function when _liveFunctions.Contains(function):
                            members.Add(function);
                            break;

                        case GimpleFunctionDefinition:
                            break;

                        case GimpleGlobalDeclaration global:
                            var declarators = ImmutableArray.CreateBuilder<GimpleVariableDeclaration>(global.Declarators.Length);
                            foreach (var declaration in global.Declarators)
                            {
                                if (!IsObjectDeclaration(declaration) ||
                                    _liveGlobals.Contains(declaration))
                                {
                                    declarators.Add(declaration);
                                }
                            }

                            if (declarators.Count != 0)
                            {
                                members.Add(declarators.Count == global.Declarators.Length
                                    ? global
                                    : new GimpleGlobalDeclaration(global.Syntax, global.StorageClass, declarators.ToImmutable()));
                            }
                            break;

                        default:
                            members.Add(member);
                            break;
                    }
                }

                var functions = ImmutableArray.CreateBuilder<SsaFunction>(_ssaFunctions.Length);
                foreach (var function in _ssaFunctions)
                {
                    if (_liveFunctions.Contains(function.Function))
                        functions.Add(function);
                }

                var tree = new GimpleTree(
                    _tree.SemanticModel,
                    members.ToImmutable(),
                    _tree.Diagnostics,
                    _tree.HasInliningApplied);
                var functionArray = functions.ToImmutable();
                var controlFlowFunctions = ImmutableArray.CreateBuilder<ControlFlowFunction>(functionArray.Length);
                foreach (var function in functionArray)
                    controlFlowFunctions.Add(function.ControlFlowFunction);
                var controlFlowGraph = _controlFlowGraph.WithTrimmedMembers(tree, controlFlowFunctions.ToImmutable());

                return new TrimResult(controlFlowGraph, functionArray);
            }

            private static bool IsObjectDeclaration(GimpleVariableDeclaration declaration)
                => declaration.StorageClass != StorageClass.Typedef &&
                   declaration.Symbol is not TypeAliasSymbol &&
                   declaration.Symbol is not FunctionSymbol &&
                   declaration.Type.Type is not FunctionType;

            private static void Add<TKey, TValue>(
                Dictionary<TKey, List<TValue>> dictionary,
                TKey key,
                TValue value)
                where TKey : notnull
            {
                if (!dictionary.TryGetValue(key, out var values))
                {
                    values = new List<TValue>();
                    dictionary.Add(key, values);
                }

                values.Add(value);
            }
        }
    }

    internal static class SymbolCollector
    {
        public static void Collect(
            GimpleStatement statement,
            HashSet<Symbol> symbols,
            HashSet<string>? names = null)
        {
            switch (statement)
            {
                case GimpleDeclarationStatement declaration when declaration.Declaration.Initializer is not null:
                    Collect(declaration.Declaration.Initializer, symbols, names);
                    break;

                case GimpleAssignmentStatement assignment:
                    Collect(assignment.Target, symbols, names);
                    Collect(assignment.Value, symbols, names);
                    break;

                case GimpleZeroInitializeStatement zeroInitialize:
                    Collect(zeroInitialize.Target, symbols, names);
                    break;

                case GimpleExpressionStatement expressionStatement:
                    Collect(expressionStatement.Expression, symbols, names);
                    break;

                case GimpleConditionalGotoStatement conditional:
                    Collect(conditional.Condition, symbols, names);
                    break;

                case GimpleSwitchStatement switchStatement:
                    Collect(switchStatement.Expression, symbols, names);
                    break;

                case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                    Collect(returnStatement.Expression, symbols, names);
                    break;

                case GimpleAsmStatement asmStatement:
                    foreach (var output in asmStatement.Outputs)
                    {
                        if (output.Target is not null)
                            Collect(output.Target, symbols, names);
                        if (output.Value is not null)
                            Collect(output.Value, symbols, names);
                    }

                    foreach (var input in asmStatement.Inputs)
                    {
                        if (input.Target is not null)
                            Collect(input.Target, symbols, names);
                        if (input.Value is not null)
                            Collect(input.Value, symbols, names);
                    }

                    if (names is not null)
                        CollectIdentifiers(asmStatement.Text, names);
                    break;
            }
        }

        public static void Collect(
            GimpleInitializer initializer,
            HashSet<Symbol> symbols,
            HashSet<string>? names = null)
        {
            switch (initializer)
            {
                case GimpleExpressionInitializer expression:
                    Collect(expression.Expression, symbols, names);
                    break;

                case GimpleInitializerList list:
                    foreach (var item in list.Items)
                        Collect(item.Initializer, symbols, names);
                    break;
            }
        }

        public static void Collect(
            GimpleValue value,
            HashSet<Symbol> symbols,
            HashSet<string>? names = null)
        {
            switch (value)
            {
                case GimpleSymbolValue symbolValue:
                    symbols.Add(symbolValue.Symbol);
                    break;

                case GimpleUnaryExpression unary:
                    Collect(unary.Operand, symbols, names);
                    break;

                case GimpleBinaryExpression binary:
                    Collect(binary.Left, symbols, names);
                    Collect(binary.Right, symbols, names);
                    break;

                case GimpleConversionExpression conversion:
                    Collect(conversion.Operand, symbols, names);
                    break;

                case GimpleCastExpression cast:
                    Collect(cast.Operand, symbols, names);
                    break;

                case GimpleAddressOfExpression addressOf:
                    Collect(addressOf.Target, symbols, names);
                    break;

                case GimpleIndirectExpression indirect:
                    Collect(indirect.Address, symbols, names);
                    break;

                case GimpleElementAccessExpression elementAccess:
                    Collect(elementAccess.Expression, symbols, names);
                    if (elementAccess.Index is not null)
                        Collect(elementAccess.Index, symbols, names);
                    break;

                case GimpleMemberAccessExpression memberAccess:
                    Collect(memberAccess.Expression, symbols, names);
                    break;

                case GimpleCallExpression call:
                    Collect(call.Callee, symbols, names);
                    foreach (var argument in call.Arguments)
                        Collect(argument, symbols, names);
                    break;
            }
        }

        private static void CollectIdentifiers(string text, HashSet<string> names)
        {
            var index = 0;
            while (index < text.Length)
            {
                if (text[index] != '_' && !char.IsLetter(text[index]))
                {
                    index++;
                    continue;
                }

                var start = index++;
                while (index < text.Length && (text[index] == '_' || char.IsLetterOrDigit(text[index])))
                    index++;
                names.Add(text.Substring(start, index - start));
            }
        }
    }
}
