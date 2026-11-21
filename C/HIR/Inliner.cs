using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C;

public sealed class InliningOptions
{
    public static InliningOptions Default { get; } = new InliningOptions();

    public bool Enabled { get; }
    public int InlineThreshold { get; }
    public int InlineKeywordThreshold { get; }
    public int AlwaysInlineThreshold { get; }
    public int SingleCallBonus { get; }
    public int MaxCalleeBlocks { get; }
    public int MaxCallerSize { get; }
    public int MaxCallerGrowth { get; }
    public int MaxInlineSitesPerFunction { get; }

    public InliningOptions(
        bool enabled = true,
        int inlineThreshold = 64,
        int inlineKeywordThreshold = 192,
        int alwaysInlineThreshold = 16,
        int singleCallBonus = 24,
        int maxCalleeBlocks = 32,
        int maxCallerSize = 512,
        int maxCallerGrowth = 384,
        int maxInlineSitesPerFunction = 64)
    {
        if (inlineThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(inlineThreshold));
        if (inlineKeywordThreshold < inlineThreshold)
            throw new ArgumentOutOfRangeException(nameof(inlineKeywordThreshold));
        if (alwaysInlineThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(alwaysInlineThreshold));
        if (singleCallBonus < 0)
            throw new ArgumentOutOfRangeException(nameof(singleCallBonus));
        if (maxCalleeBlocks < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCalleeBlocks));
        if (maxCallerSize < 1)
            throw new ArgumentOutOfRangeException(nameof(maxCallerSize));
        if (maxCallerGrowth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCallerGrowth));
        if (maxInlineSitesPerFunction < 1)
            throw new ArgumentOutOfRangeException(nameof(maxInlineSitesPerFunction));

        Enabled = enabled;
        InlineThreshold = inlineThreshold;
        InlineKeywordThreshold = inlineKeywordThreshold;
        AlwaysInlineThreshold = alwaysInlineThreshold;
        SingleCallBonus = singleCallBonus;
        MaxCalleeBlocks = maxCalleeBlocks;
        MaxCallerSize = maxCallerSize;
        MaxCallerGrowth = maxCallerGrowth;
        MaxInlineSitesPerFunction = maxInlineSitesPerFunction;
    }
}

public static class Inliner
{
    private const int CallNodeSize = 6;

    public static GimpleTree Inline(GimpleTree tree, InliningOptions? options = null)
    {
        if (tree is null)
            throw new ArgumentNullException(nameof(tree));

        options ??= InliningOptions.Default;
        if (!options.Enabled || tree.HasInliningApplied)
            return tree;

        var members = tree.Members.ToArray();
        var functions = new List<FunctionEntry>();
        var definitions = new Dictionary<FunctionSymbol, GimpleFunctionDefinition>();
        var definitionsByName = new Dictionary<string, GimpleFunctionDefinition>(StringComparer.Ordinal);

        for (var i = 0; i < members.Length; i++)
        {
            if (members[i] is not GimpleFunctionDefinition function)
                continue;

            functions.Add(new FunctionEntry(i, function));
            if (function.Symbol is not null)
            {
                definitions[function.Symbol] = function;
                definitionsByName[function.Symbol.Name] = function;
            }
        }

        foreach (var member in members)
        {
            if (member is not GimpleGlobalDeclaration global)
                continue;

            foreach (var declaration in global.Declarators)
            {
                if (declaration.Symbol is FunctionSymbol symbol &&
                    definitionsByName.TryGetValue(symbol.Name, out var definition))
                {
                    definitions[symbol] = definition;
                }
            }
        }

        if (functions.Count == 0)
            return tree.WithInliningApplied();

        var callGraph = CallGraphInfo.Build(functions.Select(static entry => entry.Function), definitions);
        var callCounts = CountDirectCalls(functions.Select(static entry => entry.Function), definitions);
        var entriesBySymbol = new Dictionary<FunctionSymbol, FunctionEntry>();
        foreach (var entry in functions)
        {
            if (entry.Function.Symbol is not null)
                entriesBySymbol[entry.Function.Symbol] = entry;
        }

        var analyses = new FunctionAnalyses(definitions);
        var siteOrdinal = 0;

        foreach (var symbol in callGraph.CalleeFirstOrder)
        {
            if (!entriesBySymbol.TryGetValue(symbol, out var entry))
                continue;

            var rewritten = InlineIntoFunction(
                entry.Function,
                definitions,
                analyses,
                callCounts,
                callGraph.RecursiveFunctions,
                options,
                ref siteOrdinal);

            members[entry.MemberIndex] = rewritten;
            UpdateDefinitionMappings(definitions, symbol, rewritten);
            entry.Function = rewritten;
            analyses.Complete(symbol, rewritten);
        }

        foreach (var entry in functions)
        {
            if (entry.Function.Symbol is not null)
                continue;

            var rewritten = InlineIntoFunction(
                entry.Function,
                definitions,
                analyses,
                callCounts,
                callGraph.RecursiveFunctions,
                options,
                ref siteOrdinal);

            members[entry.MemberIndex] = rewritten;
        }

        return new GimpleTree(
            tree.SemanticModel,
            members.ToImmutableArray(),
            tree.Diagnostics,
            hasInliningApplied: true);
    }

    private static void UpdateDefinitionMappings(
        Dictionary<FunctionSymbol, GimpleFunctionDefinition> definitions,
        FunctionSymbol symbol,
        GimpleFunctionDefinition rewritten)
    {
        foreach (var key in definitions.Keys.ToArray())
        {
            var current = definitions[key];
            if (ReferenceEquals(current.Symbol, symbol))
                definitions[key] = rewritten;
        }
    }

    private static GimpleFunctionDefinition InlineIntoFunction(
        GimpleFunctionDefinition caller,
        Dictionary<FunctionSymbol, GimpleFunctionDefinition> definitions,
        FunctionAnalyses analyses,
        Dictionary<FunctionSymbol, int> callCounts,
        HashSet<FunctionSymbol> recursiveFunctions,
        InliningOptions options,
        ref int siteOrdinal)
    {
        var current = caller;
        var initialAnalysis = analyses.Get(current);
        if (initialAnalysis.HasControlFlowProblems)
            return current;
        if (initialAnalysis.MarksNoReturn)
            current = MarkNoReturnCalls(current, analyses, ref siteOrdinal);

        var initialSize = initialAnalysis.EstimatedSize;
        var growth = 0;
        var inlinedSites = 0;

        while (inlinedSites < options.MaxInlineSitesPerFunction)
        {
            InlineSite? selectedSite = null;
            GimpleFunctionDefinition? selectedCallee = null;
            var selectedGrowth = int.MaxValue;

            foreach (var site in EnumerateInlineSites(current))
            {
                if (!TryResolveDirectCallee(site.Call.Function, out var calleeSymbol))
                    continue;
                if (!definitions.TryGetValue(calleeSymbol, out var callee))
                    continue;

                var analysis = analyses.Get(callee);
                var siteGrowth = analysis.EstimatedSize - CallNodeSize;
                if (siteGrowth >= selectedGrowth)
                    continue;
                if (!CanInline(
                    current,
                    site,
                    callee,
                    analysis,
                    siteGrowth,
                    callCounts,
                    recursiveFunctions,
                    options,
                    initialSize,
                    growth))
                {
                    continue;
                }

                selectedSite = site;
                selectedCallee = callee;
                selectedGrowth = siteGrowth;
            }

            if (selectedSite is null || selectedCallee is null)
                break;

            current = InlineCallSite(current, selectedCallee, selectedSite.Value, siteOrdinal++);
            growth += selectedGrowth;
            inlinedSites++;
        }

        return current;
    }

    private static bool CanInline(
        GimpleFunctionDefinition caller,
        InlineSite site,
        GimpleFunctionDefinition callee,
        FunctionAnalysis analysis,
        int siteGrowth,
        Dictionary<FunctionSymbol, int> callCounts,
        HashSet<FunctionSymbol> recursiveFunctions,
        InliningOptions options,
        int initialCallerSize,
        int currentGrowth)
    {
        var calleeSymbol = callee.Symbol;
        if (calleeSymbol is null)
            return false;
        if (ReferenceEquals(caller.Symbol, calleeSymbol))
            return false;
        if (calleeSymbol.IsIntrinsic)
            return false;
        if (recursiveFunctions.Contains(calleeSymbol))
            return false;
        if ((calleeSymbol.FunctionSpecifiers & FunctionSpecifiers.NoReturn) != 0 || !analysis.CanReturn)
            return false;
        if ((calleeSymbol.FunctionSpecifiers & FunctionSpecifiers.NoInline) != 0)
            return false;

        var functionType = calleeSymbol.FunctionType;
        var callType = site.Call.FunctionType;
        if (functionType is null || !functionType.HasPrototype || functionType.IsVariadic)
            return false;
        if (callType is null || !callType.HasPrototype || callType.IsVariadic)
            return false;
        if (site.Call.Arguments.Length != functionType.Parameters.Length ||
            callType.Parameters.Length != functionType.Parameters.Length)
        {
            return false;
        }

        if (analysis.HasControlFlowProblems || analysis.HasAsmGoto)
            return false;
        if (analysis.ReachableBlocks == 0 || analysis.ReachableBlocks > options.MaxCalleeBlocks)
            return false;

        if (siteGrowth > 0 &&
            currentGrowth + siteGrowth > Math.Max(options.MaxCallerSize - initialCallerSize, options.MaxCallerGrowth))
        {
            return false;
        }

        var threshold = (calleeSymbol.FunctionSpecifiers & FunctionSpecifiers.Inline) != 0
            ? options.InlineKeywordThreshold
            : options.InlineThreshold;

        if (callCounts.TryGetValue(calleeSymbol, out var count) && count == 1)
            threshold += options.SingleCallBonus;
        if (analysis.ReachableBlocks == 1)
            threshold += 8;

        return analysis.EstimatedSize <= options.AlwaysInlineThreshold || analysis.InlineCost <= threshold;
    }

    private static GimpleFunctionDefinition InlineCallSite(
        GimpleFunctionDefinition caller,
        GimpleFunctionDefinition callee,
        InlineSite site,
        int siteOrdinal)
    {
        var callerBlocks = caller.Blocks.ToList();
        var callerTemporaries = caller.Temporaries.ToList();
        var continuationLabel = new GimpleLabel(
            "inline_cont_" + siteOrdinal.ToString(CultureInfo.InvariantCulture),
            syntax: site.Statement.Syntax);

        var nextTemporaryOrdinal = callerTemporaries.Count == 0
            ? 0
            : callerTemporaries.Max(static temporary => temporary.Ordinal) + 1;

        var cloner = new InlineCloner(
            callee,
            site.Call,
            site.ResultTarget,
            continuationLabel,
            siteOrdinal,
            nextTemporaryOrdinal,
            callerTemporaries);

        var clonedBlocks = cloner.CloneReachableBlocks();
        var originalBlock = callerBlocks[site.BlockIndex];
        var before = originalBlock.Statements.Take(site.StatementIndex).ToList();
        var after = originalBlock.Statements.Skip(site.StatementIndex + 1).ToImmutableArray();

        before.Add(new GimpleGotoStatement(cloner.EntryLabel, site.Statement.Syntax));
        var replacement = new GimpleBasicBlock(originalBlock.Label, before.ToImmutableArray());
        var continuation = new GimpleBasicBlock(continuationLabel, after);

        callerBlocks.RemoveAt(site.BlockIndex);
        callerBlocks.Insert(site.BlockIndex, replacement);
        callerBlocks.InsertRange(site.BlockIndex + 1, clonedBlocks);
        callerBlocks.Insert(site.BlockIndex + 1 + clonedBlocks.Count, continuation);

        return new GimpleFunctionDefinition(
            caller.Syntax,
            caller.Symbol,
            callerTemporaries.ToImmutableArray(),
            callerBlocks.ToImmutableArray(),
            caller.EntryLabel);
    }

    private static IEnumerable<InlineSite> EnumerateInlineSites(GimpleFunctionDefinition function)
    {
        var cfg = ControlFlowFunction.Build(function);
        for (var blockIndex = 0; blockIndex < function.Blocks.Length; blockIndex++)
        {
            if (!cfg.RealBlocks[blockIndex].IsReachable)
                continue;

            var statements = function.Blocks[blockIndex].Statements;
            for (var statementIndex = 0; statementIndex < statements.Length; statementIndex++)
            {
                if (statements[statementIndex] is GimpleCallStatement call)
                    yield return new InlineSite(blockIndex, statementIndex, call);
            }
        }
    }

    private static Dictionary<FunctionSymbol, int> CountDirectCalls(
        IEnumerable<GimpleFunctionDefinition> functions,
        Dictionary<FunctionSymbol, GimpleFunctionDefinition> definitions)
    {
        var result = new Dictionary<FunctionSymbol, int>();
        foreach (var function in functions)
        {
            foreach (var call in EnumerateCalls(function))
            {
                if (!TryResolveDirectCallee(call.Function, out var symbol) || !definitions.ContainsKey(symbol))
                    continue;

                var canonical = definitions[symbol].Symbol;
                if (canonical is null)
                    continue;

                result.TryGetValue(canonical, out var count);
                result[canonical] = count + 1;
            }
        }

        return result;
    }

    private static IEnumerable<GimpleCallStatement> EnumerateCalls(GimpleFunctionDefinition function)
    {
        var cfg = ControlFlowFunction.Build(function);
        foreach (var block in cfg.RealBlocks)
        {
            if (!block.IsReachable)
                continue;

            foreach (var statement in block.Statements)
            {
                if (statement is GimpleCallStatement call)
                    yield return call;
            }
        }
    }

    private static bool TryResolveDirectCallee(GimpleValue value, out FunctionSymbol symbol)
    {
        while (true)
        {
            switch (value)
            {
                case GimpleSymbolValue { Symbol: FunctionSymbol function }:
                    symbol = function;
                    return true;

                case GimpleConversionExpression conversion
                    when conversion.ConversionKind is GimpleConversionKind.Identity or GimpleConversionKind.FunctionToPointer:
                    value = conversion.Operand;
                    continue;

                case GimpleCastExpression cast:
                    value = cast.Operand;
                    continue;

                case GimpleAddressOfExpression { Target: GimpleSymbolValue { Symbol: FunctionSymbol function } }:
                    symbol = function;
                    return true;

                case GimpleIndirectExpression indirect:
                    value = indirect.Address;
                    continue;

                default:
                    symbol = null!;
                    return false;
            }
        }
    }

    private static FunctionAnalysis Analyze(GimpleFunctionDefinition function, FunctionAnalyses analyses)
    {
        var cfg = ControlFlowFunction.Build(function);
        var estimatedSize = 0;
        var inlineCost = 0;
        var reachableBlocks = 0;
        var hasAsmGoto = false;
        var marksNoReturn = false;
        var callsNoReturn = new bool[cfg.Blocks.Length];
        var locals = new HashSet<Symbol>();

        foreach (var block in cfg.RealBlocks)
        {
            if (!block.IsReachable)
                continue;

            reachableBlocks++;
            estimatedSize += 2;
            inlineCost += 2;

            foreach (var statement in block.Statements)
            {
                if (statement is GimpleDeclarationStatement declaration && declaration.Symbol is VariableSymbol variable &&
                    variable.ExplicitRegisterName is null &&
                    variable.StorageClass is not (StorageClass.Static or StorageClass.Extern or StorageClass.ThreadLocal))
                {
                    locals.Add(variable);
                }

                NodeCost cost = StatementCost(statement, locals);
                estimatedSize += cost.Size;
                inlineCost += cost.Cost;
                if (statement is GimpleAsmStatement { IsGoto: true })
                    hasAsmGoto = true;
                if (statement is GimpleCallStatement call && analyses.IsNoReturnCall(call))
                {
                    callsNoReturn[block.Ordinal] = true;
                    marksNoReturn |= !call.IsNoReturn;
                }
            }

            foreach (var successor in block.UniqueSuccessors)
            {
                if (!successor.IsExit && successor.Dominates(block))
                    inlineCost += 12;
            }
        }

        return new FunctionAnalysis(
            estimatedSize,
            inlineCost,
            reachableBlocks,
            hasAsmGoto,
            cfg.Problems.Length != 0,
            ReachesExit(cfg, callsNoReturn),
            marksNoReturn);
    }

    private static GimpleFunctionDefinition MarkNoReturnCalls(GimpleFunctionDefinition function, FunctionAnalyses analyses, ref int siteOrdinal)
    {
        var blocks = ImmutableArray.CreateBuilder<GimpleBasicBlock>(function.Blocks.Length);
        foreach (GimpleBasicBlock block in function.Blocks)
        {
            ImmutableArray<GimpleStatement> statements = block.Statements;
            GimpleLabel label = block.Label;
            int start = 0;
            for (var i = 0; i < statements.Length; i++)
            {
                if (statements[i] is not GimpleCallStatement call || call.IsNoReturn || !analyses.IsNoReturnCall(call))
                    continue;
                var marked = new GimpleCallStatement(call.Lhs, call.Function, call.Arguments, call.FunctionType, call.Type, call.Syntax,
                    call.IsTailCall, isNoReturn: true);
                blocks.Add(new GimpleBasicBlock(label, ImmutableArray.Create(statements, start, i - start).Add(marked)));
                label = new GimpleLabel("inline_" + siteOrdinal++.ToString(CultureInfo.InvariantCulture) + "_unreachable", syntax: call.Syntax);
                start = i + 1;
            }

            if (start == 0)
                blocks.Add(block);
            else if (start < statements.Length)
                blocks.Add(new GimpleBasicBlock(label, ImmutableArray.Create(statements, start, statements.Length - start)));
        }

        return new GimpleFunctionDefinition(function.Syntax, function.Symbol, function.Temporaries, blocks.ToImmutable(), function.EntryLabel);
    }

    private static bool ReachesExit(ControlFlowFunction cfg, bool[] callsNoReturn)
    {
        var visited = new bool[cfg.Blocks.Length];
        var pending = new Stack<ControlFlowBlock>();
        visited[cfg.Entry.Ordinal] = true;
        pending.Push(cfg.Entry);

        while (pending.Count != 0)
        {
            var block = pending.Pop();
            if (block.IsExit)
                return true;
            if (callsNoReturn[block.Ordinal])
                continue;

            foreach (var successor in block.UniqueSuccessors)
            {
                if (!visited[successor.Ordinal])
                {
                    visited[successor.Ordinal] = true;
                    pending.Push(successor);
                }
            }
        }

        return false;
    }

    private static NodeCost StatementCost(GimpleStatement statement, HashSet<Symbol> locals)
    {
        return statement switch
        {
            GimpleDeclarationStatement => new NodeCost(0, 0),
            GimpleNopStatement => new NodeCost(0, 0),
            GimpleAssignStatement assign => AssignCost(assign, locals),
            GimpleCallStatement call => CallCost(call),
            GimpleGotoStatement => new NodeCost(0, 0),
            GimpleCondStatement conditional => ValueCost(conditional.Lhs) + ValueCost(conditional.Rhs) + new NodeCost(1, 2),
            GimpleSwitchStatement switchStatement => ValueCost(switchStatement.Expression) + new NodeCost(2, 3 + switchStatement.Cases.Length),
            GimpleReturnStatement returnStatement => returnStatement.Expression is null ? new NodeCost(0, 0) : ValueCost(returnStatement.Expression),
            GimpleAsmStatement asmStatement => new NodeCost(8, 16 + asmStatement.Inputs.Length + asmStatement.Outputs.Length),
            _ => new NodeCost(1, 2),
        };
    }

    private static NodeCost AssignCost(GimpleAssignStatement assign, HashSet<Symbol> locals)
    {
        if (IsRegisterMove(assign, locals))
            return new NodeCost(0, 0);

        var cost = ValueCost(assign.Lhs) + new NodeCost(1, assign.IsConstructor ? 2 : 1);
        foreach (var operand in assign.Operands)
            cost += ValueCost(operand);

        return cost;
    }

    private static bool IsRegisterMove(GimpleAssignStatement assign, HashSet<Symbol> locals)
    {
        if (assign.Op1 is not GimpleValue source || !IsRegister(assign.Lhs, locals) ||
            source is not GimpleConstantValue && !IsRegister(source, locals))
            return false;

        return assign.RhsClass == GimpleRhsClass.Single ||
               assign.Subcode is GimpleTreeCode.NopExpr or GimpleTreeCode.ConvertExpr && IsIntegral(source.Type) && IsIntegral(assign.Lhs.Type);
    }

    private static bool IsRegister(GimpleValue value, HashSet<Symbol> locals)
        => value.Type.Type.Kind is TypeKind.Builtin or TypeKind.Enum or TypeKind.Pointer && !GimpleTypes.IsVoid(value.Type) &&
           !GimpleTypes.IsVolatileOrAtomic(value.Type) &&
           (value is GimpleTemporaryValue || value is GimpleSymbolValue symbol && (symbol.Symbol is ParameterSymbol || locals.Contains(symbol.Symbol)));

    private static bool IsIntegral(QualifiedType type)
        => GimpleTypes.IsIntegerLike(type) || type.Type.Kind == TypeKind.Pointer;

    private static NodeCost CallCost(GimpleCallStatement call)
    {
        var cost = ValueCost(call.Function) + new NodeCost(CallNodeSize, 12);
        if (call.Lhs is not null)
            cost += ValueCost(call.Lhs);

        foreach (var argument in call.Arguments)
            cost += ValueCost(argument);

        return cost;
    }

    private static NodeCost ValueCost(GimpleValue value)
    {
        return value switch
        {
            GimpleSymbolValue => new NodeCost(0, 0),
            GimpleTemporaryValue => new NodeCost(0, 0),
            GimpleConstantValue => new NodeCost(0, 0),
            GimpleErrorValue => new NodeCost(0, 0),
            GimpleUnaryExpression unary => ValueCost(unary.Operand) + new NodeCost(1, 1),
            GimpleBinaryExpression binary => ValueCost(binary.Left) + ValueCost(binary.Right) + new NodeCost(1, 1),
            GimpleConversionExpression conversion => ValueCost(conversion.Operand) + new NodeCost(1, 1),
            GimpleCastExpression cast => ValueCost(cast.Operand) + new NodeCost(1, 1),
            GimpleAddressOfExpression addressOf => ValueCost(addressOf.Target) + new NodeCost(1, 1),
            GimpleIndirectExpression indirect => ValueCost(indirect.Address) + new NodeCost(1, 2),
            GimpleElementAccessExpression elementAccess =>
                ValueCost(elementAccess.Expression) +
                (elementAccess.Index is null ? new NodeCost(0, 0) : ValueCost(elementAccess.Index)) +
                new NodeCost(1, 2),
            GimpleMemberAccessExpression memberAccess => ValueCost(memberAccess.Expression) + new NodeCost(1, 1),
            _ => new NodeCost(1, 1),
        };
    }

    private sealed class InlineCloner
    {
        private readonly GimpleFunctionDefinition _callee;
        private readonly GimpleCallStatement _call;
        private readonly GimplePlace? _resultTarget;
        private readonly GimpleLabel _continuationLabel;
        private readonly int _siteOrdinal;
        private readonly List<GimpleTemporaryValue> _callerTemporaries;
        private readonly Dictionary<GimpleLabel, GimpleLabel> _labels = new();
        private readonly Dictionary<GimpleTemporaryValue, GimpleTemporaryValue> _temporaries = new();
        private readonly Dictionary<Symbol, Symbol> _symbols = new();
        private readonly HashSet<GimpleBasicBlock> _reachableBlocks = new();
        private int _nextTemporaryOrdinal;

        public GimpleLabel EntryLabel => _labels[_callee.EntryLabel];

        public InlineCloner(
            GimpleFunctionDefinition callee,
            GimpleCallStatement call,
            GimplePlace? resultTarget,
            GimpleLabel continuationLabel,
            int siteOrdinal,
            int nextTemporaryOrdinal,
            List<GimpleTemporaryValue> callerTemporaries)
        {
            _callee = callee;
            _call = call;
            _resultTarget = resultTarget;
            _continuationLabel = continuationLabel;
            _siteOrdinal = siteOrdinal;
            _nextTemporaryOrdinal = nextTemporaryOrdinal;
            _callerTemporaries = callerTemporaries;

            PrepareMaps();
        }

        public List<GimpleBasicBlock> CloneReachableBlocks()
        {
            var result = new List<GimpleBasicBlock>();
            foreach (var block in _callee.Blocks)
            {
                if (!_reachableBlocks.Contains(block))
                    continue;

                var statements = ImmutableArray.CreateBuilder<GimpleStatement>();
                if (ReferenceEquals(block.Label, _callee.EntryLabel))
                    AppendParameterBindings(statements);

                foreach (var statement in block.Statements)
                {
                    if (statement is GimpleReturnStatement returnStatement)
                    {
                        if (returnStatement.Expression is not null && _resultTarget is not null)
                        {
                            statements.Add(GimpleAssignStatement.Single(
                                _resultTarget,
                                CloneValue(returnStatement.Expression),
                                returnStatement.Syntax));
                        }

                        statements.Add(new GimpleGotoStatement(_continuationLabel, returnStatement.Syntax));
                        continue;
                    }

                    statements.Add(CloneStatement(statement));
                }

                if (statements.Count == 0 || !statements[^1].IsTerminator)
                {
                    var isLastReachable = !HasReachableFallthroughSuccessor(block);
                    if (isLastReachable)
                        statements.Add(new GimpleGotoStatement(_continuationLabel, block.Syntax));
                }

                result.Add(new GimpleBasicBlock(_labels[block.Label], statements.ToImmutable()));
            }

            return result;
        }

        private void PrepareMaps()
        {
            var cfg = ControlFlowFunction.Build(_callee);
            foreach (var block in cfg.RealBlocks)
            {
                if (block.IsReachable && block.GimpleBlock is not null)
                    _reachableBlocks.Add(block.GimpleBlock);
            }

            foreach (var block in _callee.Blocks)
            {
                if (!_reachableBlocks.Contains(block))
                    continue;

                _labels[block.Label] = new GimpleLabel(
                    "inline_" + _siteOrdinal.ToString(CultureInfo.InvariantCulture) + "_" + block.Label.Name,
                    syntax: block.Label.Syntax);
            }

            foreach (var temporary in _callee.Temporaries)
                MapTemporary(temporary);

            var functionType = _callee.Symbol?.FunctionType;
            if (functionType is not null)
            {
                foreach (var parameter in functionType.Parameters)
                {
                    _symbols[parameter] = new VariableSymbol(
                        parameter.Name + ".inl" + _siteOrdinal.ToString(CultureInfo.InvariantCulture),
                        parameter.Type,
                        StorageClass.Auto,
                        parameter.DeclaringSyntax);
                }
            }

            foreach (var block in _callee.Blocks)
            {
                if (!_reachableBlocks.Contains(block))
                    continue;

                foreach (var statement in block.Statements)
                {
                    if (statement is not GimpleDeclarationStatement { Symbol: VariableSymbol variable })
                        continue;
                    if (variable.StorageClass is not (StorageClass.None or StorageClass.Auto or StorageClass.Register))
                        continue;
                    if (_symbols.ContainsKey(variable))
                        continue;

                    _symbols[variable] = new VariableSymbol(
                        variable.Name + ".inl" + _siteOrdinal.ToString(CultureInfo.InvariantCulture),
                        variable.Type,
                        variable.StorageClass,
                        variable.DeclaringSyntax,
                        variable.ExplicitRegisterName);
                }
            }
        }

        private bool HasReachableFallthroughSuccessor(GimpleBasicBlock block)
        {
            var index = _callee.Blocks.IndexOf(block);
            if (index < 0 || index + 1 >= _callee.Blocks.Length)
                return false;
            if (block.HasTerminator)
                return false;
            return _reachableBlocks.Contains(_callee.Blocks[index + 1]);
        }

        private void AppendParameterBindings(ImmutableArray<GimpleStatement>.Builder statements)
        {
            var functionType = _callee.Symbol?.FunctionType;
            if (functionType is null)
                return;

            for (var i = 0; i < functionType.Parameters.Length; i++)
            {
                var parameter = functionType.Parameters[i];
                if (!_symbols.TryGetValue(parameter, out var mapped) || mapped is not VariableSymbol variable)
                    continue;

                statements.Add(new GimpleDeclarationStatement(
                    new GimpleVariableDeclaration(
                        variable,
                        variable.Type,
                        StorageClass.Auto,
                        syntax: parameter.DeclaringSyntax)));
                statements.Add(GimpleAssignStatement.Single(
                    new GimpleSymbolValue(variable, variable.Type, parameter.DeclaringSyntax),
                    _call.Arguments[i],
                    _call.Syntax));
            }
        }

        private GimpleStatement CloneStatement(GimpleStatement statement)
        {
            return statement switch
            {
                GimpleDeclarationStatement declaration => new GimpleDeclarationStatement(CloneDeclaration(declaration.Declaration)),
                GimpleAssignStatement assign => new GimpleAssignStatement(
                    ClonePlace(assign.Lhs),
                    assign.Subcode,
                    assign.Operands.Select(CloneValue).ToImmutableArray(),
                    assign.Syntax),
                GimpleCallStatement call => new GimpleCallStatement(
                    call.Lhs is null ? null : ClonePlace(call.Lhs),
                    CloneValue(call.Function),
                    call.Arguments.Select(CloneValue).ToImmutableArray(),
                    call.FunctionType,
                    call.Type,
                    call.Syntax,
                    call.IsTailCall,
                    call.IsNoReturn),
                GimpleGotoStatement gotoStatement => new GimpleGotoStatement(CloneLabel(gotoStatement.Target), gotoStatement.Syntax),
                GimpleCondStatement conditional => new GimpleCondStatement(
                    conditional.Code,
                    CloneValue(conditional.Lhs),
                    CloneValue(conditional.Rhs),
                    CloneLabel(conditional.WhenTrue),
                    CloneLabel(conditional.WhenFalse),
                    conditional.Syntax),
                GimpleSwitchStatement switchStatement => new GimpleSwitchStatement(
                    CloneValue(switchStatement.Expression),
                    switchStatement.Cases.Select(CloneSwitchCase).ToImmutableArray(),
                    CloneLabel(switchStatement.DefaultLabel),
                    switchStatement.Syntax),
                GimpleAsmStatement asmStatement => CloneAsmStatement(asmStatement),
                GimpleNopStatement nop => new GimpleNopStatement(nop.Syntax),
                GimpleReturnStatement => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException("Unsupported GIMPLE statement kind: " + statement.Kind),
            };
        }

        private GimpleAsmStatement CloneAsmStatement(GimpleAsmStatement statement)
        {
            return new GimpleAsmStatement(
                statement.Text,
                statement.IsVolatile,
                statement.IsInline,
                statement.IsGoto,
                statement.Outputs.Select(CloneAsmOperand).ToImmutableArray(),
                statement.Inputs.Select(CloneAsmOperand).ToImmutableArray(),
                statement.Clobbers,
                statement.GotoLabels.Select(CloneLabel).ToImmutableArray(),
                statement.Syntax);
        }

        private GimpleAsmOperand CloneAsmOperand(GimpleAsmOperand operand)
        {
            return new GimpleAsmOperand(
                operand.Name,
                operand.Constraint,
                operand.Target is null ? null : ClonePlace(operand.Target),
                operand.Value is null ? null : CloneValue(operand.Value),
                operand.IsOutput,
                operand.IsReadWrite,
                operand.Syntax);
        }

        private GimpleSwitchCase CloneSwitchCase(GimpleSwitchCase item)
            => new GimpleSwitchCase((GimpleConstantValue)CloneValue(item.Value), CloneLabel(item.Target));

        private GimpleVariableDeclaration CloneDeclaration(GimpleVariableDeclaration declaration)
        {
            var symbol = declaration.Symbol;
            if (symbol is not null && _symbols.TryGetValue(symbol, out var mapped))
                symbol = mapped;

            return new GimpleVariableDeclaration(
                symbol,
                declaration.Type,
                declaration.StorageClass,
                declaration.Initializer is null ? null : CloneInitializer(declaration.Initializer),
                declaration.Syntax);
        }

        private GimpleInitializer CloneInitializer(GimpleInitializer initializer)
        {
            return initializer switch
            {
                GimpleExpressionInitializer expressionInitializer => new GimpleExpressionInitializer(
                    expressionInitializer.Syntax,
                    expressionInitializer.TargetType,
                    CloneValue(expressionInitializer.Expression)),
                GimpleInitializerList initializerList => new GimpleInitializerList(
                    initializerList.Syntax,
                    initializerList.TargetType,
                    initializerList.Items.Select(CloneInitializerItem).ToImmutableArray(),
                    initializerList.IsByteImage),
                _ => throw new InvalidOperationException("Unsupported GIMPLE initializer."),
            };
        }

        private GimpleInitializerListItem CloneInitializerItem(GimpleInitializerListItem item)
            => new GimpleInitializerListItem(item.Syntax, item.Designators, CloneInitializer(item.Initializer), item.ElementIndex);

        private GimpleLabel CloneLabel(GimpleLabel label)
        {
            if (_labels.TryGetValue(label, out var mapped))
                return mapped;

            mapped = new GimpleLabel(
                "inline_" + _siteOrdinal.ToString(CultureInfo.InvariantCulture) + "_" + label.Name,
                syntax: label.Syntax);
            _labels[label] = mapped;
            return mapped;
        }

        private GimplePlace ClonePlace(GimplePlace place)
            => (GimplePlace)CloneValue(place);

        private GimpleValue CloneValue(GimpleValue value)
        {
            return value switch
            {
                GimpleSymbolValue symbolValue => CloneSymbolValue(symbolValue),
                GimpleTemporaryValue temporary => MapTemporary(temporary),
                GimpleConstantValue constant => new GimpleConstantValue(constant.Value, constant.Type, constant.Syntax),
                GimpleUnaryExpression unary => new GimpleUnaryExpression(
                    unary.Code,
                    CloneValue(unary.Operand),
                    unary.Type,
                    unary.Syntax),
                GimpleBinaryExpression binary => new GimpleBinaryExpression(
                    CloneValue(binary.Left),
                    binary.Code,
                    CloneValue(binary.Right),
                    binary.Type,
                    binary.Syntax),
                GimpleConversionExpression conversion => new GimpleConversionExpression(
                    CloneValue(conversion.Operand),
                    conversion.Type,
                    conversion.ConversionKind,
                    conversion.Syntax),
                GimpleCastExpression cast => new GimpleCastExpression(
                    CloneValue(cast.Operand),
                    cast.Type,
                    cast.Syntax),
                GimpleAddressOfExpression addressOf => new GimpleAddressOfExpression(
                    ClonePlace(addressOf.Target),
                    addressOf.Type,
                    addressOf.Syntax),
                GimpleIndirectExpression indirect => new GimpleIndirectExpression(
                    CloneValue(indirect.Address),
                    indirect.Type,
                    indirect.Syntax),
                GimpleElementAccessExpression elementAccess => new GimpleElementAccessExpression(
                    CloneValue(elementAccess.Expression),
                    elementAccess.Index is null ? null : CloneValue(elementAccess.Index),
                    elementAccess.Type,
                    elementAccess.Syntax),
                GimpleMemberAccessExpression memberAccess => new GimpleMemberAccessExpression(
                    CloneValue(memberAccess.Expression),
                    memberAccess.ThroughPointer,
                    memberAccess.NameToken,
                    memberAccess.Field,
                    memberAccess.Type,
                    memberAccess.Syntax),
                GimpleErrorValue error => new GimpleErrorValue(error.Syntax),
                _ => throw new InvalidOperationException("Unsupported GIMPLE value kind: " + value.Kind),
            };
        }

        private GimpleSymbolValue CloneSymbolValue(GimpleSymbolValue value)
        {
            var symbol = value.Symbol;
            if (_symbols.TryGetValue(symbol, out var mapped))
                symbol = mapped;
            return new GimpleSymbolValue(symbol, value.Type, value.Syntax);
        }

        private GimpleTemporaryValue MapTemporary(GimpleTemporaryValue temporary)
        {
            if (_temporaries.TryGetValue(temporary, out var mapped))
                return mapped;

            mapped = new GimpleTemporaryValue(_nextTemporaryOrdinal++, temporary.Type, temporary.Syntax);
            _temporaries[temporary] = mapped;
            _callerTemporaries.Add(mapped);
            return mapped;
        }
    }

    private sealed class CallGraphInfo
    {
        public ImmutableArray<FunctionSymbol> CalleeFirstOrder { get; }
        public HashSet<FunctionSymbol> RecursiveFunctions { get; }

        private CallGraphInfo(
            ImmutableArray<FunctionSymbol> calleeFirstOrder,
            HashSet<FunctionSymbol> recursiveFunctions)
        {
            CalleeFirstOrder = calleeFirstOrder;
            RecursiveFunctions = recursiveFunctions;
        }

        public static CallGraphInfo Build(
            IEnumerable<GimpleFunctionDefinition> functions,
            Dictionary<FunctionSymbol, GimpleFunctionDefinition> definitions)
        {
            var orderedFunctions = functions
                .Where(static function => function.Symbol is not null)
                .Select(static function => function.Symbol!)
                .Distinct()
                .ToImmutableArray();

            var edges = new Dictionary<FunctionSymbol, HashSet<FunctionSymbol>>();
            foreach (var symbol in orderedFunctions)
                edges[symbol] = new HashSet<FunctionSymbol>();

            foreach (var function in functions)
            {
                if (function.Symbol is null || !edges.TryGetValue(function.Symbol, out var targets))
                    continue;

                foreach (var call in EnumerateCalls(function))
                {
                    if (TryResolveDirectCallee(call.Function, out var callee) &&
                        definitions.TryGetValue(callee, out var definition) &&
                        definition.Symbol is not null)
                    {
                        targets.Add(definition.Symbol);
                    }
                }
            }

            var tarjan = new Tarjan(edges, orderedFunctions);
            var components = tarjan.Run();
            var componentBySymbol = new Dictionary<FunctionSymbol, int>();
            var recursive = new HashSet<FunctionSymbol>();

            for (var i = 0; i < components.Count; i++)
            {
                foreach (var symbol in components[i])
                    componentBySymbol[symbol] = i;

                if (components[i].Count > 1)
                {
                    recursive.UnionWith(components[i]);
                }
                else
                {
                    var symbol = components[i][0];
                    if (edges[symbol].Contains(symbol))
                        recursive.Add(symbol);
                }
            }

            var componentEdges = new Dictionary<int, HashSet<int>>();
            for (var i = 0; i < components.Count; i++)
                componentEdges[i] = new HashSet<int>();

            foreach (var pair in edges)
            {
                var source = componentBySymbol[pair.Key];
                foreach (var targetSymbol in pair.Value)
                {
                    var target = componentBySymbol[targetSymbol];
                    if (source != target)
                        componentEdges[source].Add(target);
                }
            }

            var componentOrder = new List<int>();
            var visited = new HashSet<int>();
            for (var i = 0; i < components.Count; i++)
                VisitComponent(i, componentEdges, visited, componentOrder);

            var symbolOrder = ImmutableArray.CreateBuilder<FunctionSymbol>();
            foreach (var component in componentOrder)
            {
                foreach (var symbol in orderedFunctions)
                {
                    if (componentBySymbol[symbol] == component)
                        symbolOrder.Add(symbol);
                }
            }

            return new CallGraphInfo(symbolOrder.ToImmutable(), recursive);
        }

        private static void VisitComponent(
            int component,
            Dictionary<int, HashSet<int>> edges,
            HashSet<int> visited,
            List<int> order)
        {
            if (!visited.Add(component))
                return;

            foreach (var target in edges[component].OrderBy(static value => value))
                VisitComponent(target, edges, visited, order);

            order.Add(component);
        }
    }

    private sealed class Tarjan
    {
        private readonly Dictionary<FunctionSymbol, HashSet<FunctionSymbol>> _edges;
        private readonly ImmutableArray<FunctionSymbol> _order;
        private readonly Dictionary<FunctionSymbol, int> _indices = new();
        private readonly Dictionary<FunctionSymbol, int> _lowLinks = new();
        private readonly Stack<FunctionSymbol> _stack = new();
        private readonly HashSet<FunctionSymbol> _onStack = new();
        private readonly List<List<FunctionSymbol>> _components = new();
        private int _index;

        public Tarjan(
            Dictionary<FunctionSymbol, HashSet<FunctionSymbol>> edges,
            ImmutableArray<FunctionSymbol> order)
        {
            _edges = edges;
            _order = order;
        }

        public List<List<FunctionSymbol>> Run()
        {
            foreach (var symbol in _order)
            {
                if (!_indices.ContainsKey(symbol))
                    Connect(symbol);
            }

            return _components;
        }

        private void Connect(FunctionSymbol symbol)
        {
            _indices[symbol] = _index;
            _lowLinks[symbol] = _index;
            _index++;
            _stack.Push(symbol);
            _onStack.Add(symbol);

            foreach (var target in _edges[symbol])
            {
                if (!_indices.ContainsKey(target))
                {
                    Connect(target);
                    _lowLinks[symbol] = Math.Min(_lowLinks[symbol], _lowLinks[target]);
                }
                else if (_onStack.Contains(target))
                {
                    _lowLinks[symbol] = Math.Min(_lowLinks[symbol], _indices[target]);
                }
            }

            if (_lowLinks[symbol] != _indices[symbol])
                return;

            var component = new List<FunctionSymbol>();
            while (_stack.Count != 0)
            {
                var current = _stack.Pop();
                _onStack.Remove(current);
                component.Add(current);
                if (ReferenceEquals(current, symbol))
                    break;
            }

            _components.Add(component);
        }
    }

    private sealed class FunctionEntry
    {
        public int MemberIndex { get; }
        public GimpleFunctionDefinition Function { get; set; }

        public FunctionEntry(int memberIndex, GimpleFunctionDefinition function)
        {
            MemberIndex = memberIndex;
            Function = function;
        }
    }

    private readonly struct InlineSite
    {
        public int BlockIndex { get; }
        public int StatementIndex { get; }
        public GimpleCallStatement Call { get; }
        public GimpleStatement Statement => Call;
        public GimplePlace? ResultTarget => Call.Lhs;

        public InlineSite(int blockIndex, int statementIndex, GimpleCallStatement call)
        {
            BlockIndex = blockIndex;
            StatementIndex = statementIndex;
            Call = call;
        }
    }

    private sealed class FunctionAnalyses
    {
        private readonly Dictionary<FunctionSymbol, GimpleFunctionDefinition> _definitions;
        private readonly Dictionary<GimpleFunctionDefinition, FunctionAnalysis> _analyses = new();
        private readonly HashSet<FunctionSymbol> _noReturn = new();

        public FunctionAnalyses(Dictionary<FunctionSymbol, GimpleFunctionDefinition> definitions)
            => _definitions = definitions;

        public FunctionAnalysis Get(GimpleFunctionDefinition function)
        {
            if (!_analyses.TryGetValue(function, out var analysis))
            {
                analysis = Analyze(function, this);
                _analyses.Add(function, analysis);
            }

            return analysis;
        }

        public void Complete(FunctionSymbol symbol, GimpleFunctionDefinition function)
        {
            if (!Get(function).CanReturn)
                _noReturn.Add(symbol);
        }

        public bool IsNoReturnCall(GimpleCallStatement call)
        {
            if (!TryResolveDirectCallee(call.Function, out var symbol))
                return false;
            if ((symbol.FunctionSpecifiers & FunctionSpecifiers.NoReturn) != 0)
                return true;

            return _definitions.TryGetValue(symbol, out var definition) &&
                   definition.Symbol is not null &&
                   _noReturn.Contains(definition.Symbol);
        }
    }

    private readonly struct FunctionAnalysis
    {
        public int EstimatedSize { get; }
        public int InlineCost { get; }
        public int ReachableBlocks { get; }
        public bool HasAsmGoto { get; }
        public bool HasControlFlowProblems { get; }
        public bool CanReturn { get; }
        public bool MarksNoReturn { get; }

        public FunctionAnalysis(
            int estimatedSize,
            int inlineCost,
            int reachableBlocks,
            bool hasAsmGoto,
            bool hasControlFlowProblems,
            bool canReturn,
            bool marksNoReturn)
        {
            EstimatedSize = estimatedSize;
            InlineCost = inlineCost;
            ReachableBlocks = reachableBlocks;
            HasAsmGoto = hasAsmGoto;
            HasControlFlowProblems = hasControlFlowProblems;
            CanReturn = canReturn;
            MarksNoReturn = marksNoReturn;
        }
    }

    private readonly struct NodeCost
    {
        public int Size { get; }
        public int Cost { get; }

        public NodeCost(int size, int cost)
        {
            Size = size;
            Cost = cost;
        }

        public static NodeCost operator +(NodeCost left, NodeCost right)
            => new NodeCost(left.Size + right.Size, left.Cost + right.Cost);
    }
}
