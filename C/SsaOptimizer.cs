using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    internal static partial class SsaOptimizer
    {
        public static GimpleFunctionAnnotations Optimize(
            GimpleFunctionAnnotations function,
            TargetInfo target,
            SsaOptimizationOptions options,
            ValueNumberingOptions valueNumberingOptions)
        {
            if (function is null)
                throw new ArgumentNullException(nameof(function));
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (valueNumberingOptions is null)
                throw new ArgumentNullException(nameof(valueNumberingOptions));

            if (!options.EnableConstantFolding &&
                !options.EnableCopyPropagation &&
                !options.EnableBranchFolding &&
                !options.EnableDeadCodeElimination &&
                !options.EnableCommonSubexpressionElimination)
            {
                return function;
            }

            var current = function;
            for (var i = 0; i < options.MaxIterations; i++)
            {
                var pass = new Pass(current, target, options, valueNumberingOptions);
                var next = pass.Run();
                if (!pass.Changed)
                    return current;

                current = next;
            }

            return current;
        }

        private sealed partial class Pass
        {
            private readonly GimpleFunctionAnnotations _function;
            private readonly TargetInfo _target;
            private readonly SsaOptimizationOptions _options;
            private readonly ValueNumberingOptions _valueNumberingOptions;
            private readonly Dictionary<GimpleName, GimpleName> _copies = new();
            private readonly Dictionary<GimpleName, GimpleConstantValue> _constants = new();
            private readonly Dictionary<int, List<GimpleName>> _representativesByValueNumber = new();
            private readonly Dictionary<GimpleName, int> _useCounts = new();
            private readonly Dictionary<GimpleName, int> _definitionOrder = new();
            private readonly Dictionary<ControlFlowBlock, int> _dominatorDepths = new();
            private readonly Dictionary<GimpleDefinition, GimpleDefinition> _definitionMap = new();
            private readonly HashSet<GimpleDefinition> _removedDefinitions = new();
            private readonly List<GimpleUse> _uses = new();

            public bool Changed { get; private set; }

            public Pass(
                GimpleFunctionAnnotations function,
                TargetInfo target,
                SsaOptimizationOptions options,
                ValueNumberingOptions valueNumberingOptions)
            {
                _function = function;
                _target = target;
                _options = options;
                _valueNumberingOptions = valueNumberingOptions;
                IndexUseCounts();
                IndexDefinitionOrder();
                IndexDominatorDepths();
                if (_options.EnableCommonSubexpressionElimination)
                    FindCommonSubexpressions();
            }

            private void IndexUseCounts()
            {
                foreach (var use in _function.Uses)
                {
                    if (use.Name.Variable.Kind == GimpleVariableKind.Memory)
                        continue;

                    _useCounts.TryGetValue(use.Name, out var count);
                    _useCounts[use.Name] = count + 1;
                }

                foreach (var block in _function.Blocks)
                {
                    foreach (var phi in block.Phis)
                    {
                        foreach (var operand in phi.Operands)
                        {
                            if (operand.Value.Variable.Kind == GimpleVariableKind.Memory)
                                continue;

                            _useCounts.TryGetValue(operand.Value, out var count);
                            _useCounts[operand.Value] = count + 1;
                        }
                    }
                }
            }

            private void IndexDefinitionOrder()
            {
                foreach (var definition in _function.Definitions)
                {
                    if (definition.Kind == GimpleDefinitionKind.Entry)
                        _definitionOrder[definition.Name] = -2;
                    else if (definition.Kind == GimpleDefinitionKind.Phi)
                        _definitionOrder[definition.Name] = -1;
                }

                foreach (var block in _function.Blocks)
                {
                    for (var instructionIndex = 0; instructionIndex < block.Statements.Length; instructionIndex++)
                    {
                        foreach (var definition in block.Statements[instructionIndex].Definitions)
                            _definitionOrder[definition.Name] = instructionIndex;
                    }
                }
            }

            private void IndexDominatorDepths()
            {
                foreach (var block in _function.ControlFlowFunction.ReversePostOrder)
                {
                    if (!block.IsReachable || block.IsExit)
                        continue;

                    _dominatorDepths[block] = block.ImmediateDominator is null || ReferenceEquals(block.ImmediateDominator, block)
                        ? 0
                        : (_dominatorDepths.TryGetValue(block.ImmediateDominator, out var parentDepth) ? parentDepth + 1 : 0);
                }
            }

            public GimpleFunctionAnnotations Run()
            {
                var blocksByControlFlowBlock = new Dictionary<ControlFlowBlock, GimpleBlockAnnotations>();

                foreach (var controlFlowBlock in _function.ControlFlowFunction.ReversePostOrder)
                {
                    if (controlFlowBlock.IsExit || !controlFlowBlock.IsReachable)
                        continue;

                    if (!_function.TryGetBlock(controlFlowBlock, out var block) || block is null)
                        continue;

                    var rewrittenPhis = RewritePhis(block);
                    var rewrittenInstructions = RewriteInstructions(block);
                    blocksByControlFlowBlock[controlFlowBlock] = new GimpleBlockAnnotations(controlFlowBlock, rewrittenPhis, rewrittenInstructions);
                }

                var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>();
                foreach (var block in _function.Blocks)
                {
                    if (blocksByControlFlowBlock.TryGetValue(block.ControlFlowBlock, out var rewritten))
                    {
                        blocks.Add(rewritten);
                        continue;
                    }

                    blocks.Add(block);
                }

                var blockArray = blocks.ToImmutable();
                if (_options.EnableDeadCodeElimination)
                    blockArray = EliminateDeadCode(blockArray);

                if (!Changed)
                    return _function;

                RebuildUsesFromBlocks(blockArray);
                var definitions = RewriteDefinitionList();

                return new GimpleFunctionAnnotations(
                    _function.ControlFlowFunction,
                    _function.MemoryVariable,
                    _function.Variables,
                    blockArray,
                    definitions,
                    _uses.ToImmutableArray(),
                    _function.Problems,
                    CreateUndefinedNameMap(),
                    _valueNumberingOptions,
                    _target);
            }

            private ImmutableArray<GimpleDefinition> RewriteDefinitionList()
            {
                var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length);
                foreach (var definition in _function.Definitions)
                {
                    if (_removedDefinitions.Contains(definition))
                        continue;

                    if (_definitionMap.TryGetValue(definition, out var replacement))
                    {
                        if (!_removedDefinitions.Contains(replacement))
                            definitions.Add(replacement);
                    }
                    else
                    {
                        definitions.Add(definition);
                    }
                }

                return definitions.ToImmutable();
            }

            private void RebuildUsesFromBlocks(ImmutableArray<GimpleBlockAnnotations> blocks)
            {
                _uses.Clear();
                foreach (var block in blocks)
                {
                    foreach (var instruction in block.Statements)
                    {
                        foreach (var use in instruction.Uses)
                            _uses.Add(use);
                    }
                }
            }

            private ImmutableArray<GimpleBlockAnnotations> EliminateDeadCode(ImmutableArray<GimpleBlockAnnotations> blocks)
            {
                var instructionByDefinition = new Dictionary<GimpleName, GimpleStatementAnnotations>();
                var phiByResult = new Dictionary<GimpleName, GimplePhi>();
                var declarationsBySymbol = new Dictionary<Symbol, List<GimpleStatementAnnotations>>();

                foreach (var block in blocks)
                {
                    foreach (var phi in block.Phis)
                        phiByResult[phi.Result] = phi;

                    foreach (var instruction in block.Statements)
                    {
                        foreach (var definition in instruction.Definitions)
                            instructionByDefinition[definition.Name] = instruction;

                        if (instruction.Statement is GimpleDeclarationStatement { Symbol: not null } declaration)
                        {
                            if (!declarationsBySymbol.TryGetValue(declaration.Symbol, out var declarations))
                            {
                                declarations = new List<GimpleStatementAnnotations>();
                                declarationsBySymbol.Add(declaration.Symbol, declarations);
                            }

                            declarations.Add(instruction);
                        }
                    }
                }

                var liveInstructions = new HashSet<GimpleStatementAnnotations>();
                var livePhis = new HashSet<GimplePhi>();
                var workList = new Queue<GimpleName>();

                foreach (var block in blocks)
                {
                    foreach (var phi in block.Phis)
                    {
                        if (phi.Result.Variable.Kind == GimpleVariableKind.Memory)
                            MarkPhiLive(phi, livePhis, workList);
                    }

                    foreach (var instruction in block.Statements)
                    {
                        if (!IsRemovableInstruction(instruction))
                            MarkInstructionLive(instruction, liveInstructions, workList);
                    }
                }

                while (workList.Count != 0)
                {
                    var name = workList.Dequeue();
                    if (name.IsUndefined)
                        continue;

                    if (phiByResult.TryGetValue(name, out var phi))
                    {
                        MarkPhiLive(phi, livePhis, workList);
                        continue;
                    }

                    if (instructionByDefinition.TryGetValue(name, out var instruction))
                        MarkInstructionLive(instruction, liveInstructions, workList);
                }

                var referencedSymbols = new HashSet<Symbol>();
                foreach (var instruction in liveInstructions)
                    SymbolCollector.Collect(instruction.Statement, referencedSymbols);

                foreach (var symbol in referencedSymbols)
                {
                    if (!declarationsBySymbol.TryGetValue(symbol, out var declarations))
                        continue;

                    foreach (var declaration in declarations)
                        MarkInstructionLive(declaration, liveInstructions, workList);
                }

                var result = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(blocks.Length);
                foreach (var block in blocks)
                {
                    var phisChanged = false;
                    var phis = ImmutableArray.CreateBuilder<GimplePhi>(block.Phis.Length);
                    foreach (var phi in block.Phis)
                    {
                        if (phi.Result.Variable.Kind == GimpleVariableKind.Memory || livePhis.Contains(phi))
                        {
                            phis.Add(phi);
                            continue;
                        }

                        RemovePhiDefinition(phi);
                        phisChanged = true;
                    }

                    var instructionsChanged = false;
                    var instructions = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                    foreach (var instruction in block.Statements)
                    {
                        if (!IsRemovableInstruction(instruction) || liveInstructions.Contains(instruction))
                        {
                            instructions.Add(instruction);
                            continue;
                        }

                        RemoveInstructionDefinitions(instruction);
                        instructionsChanged = true;
                    }

                    if (phisChanged || instructionsChanged)
                    {
                        Changed = true;
                        result.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, phis.ToImmutable(), instructions.ToImmutable()));
                    }
                    else
                    {
                        result.Add(block);
                    }
                }

                return result.ToImmutable();
            }

            private static bool IsRemovableInstruction(GimpleStatementAnnotations instruction)
            {
                if (instruction.Statement.IsTerminator)
                    return false;

                if ((instruction.Flags & (GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) != 0)
                    return false;

                if (instruction.Statement is GimpleDeclarationStatement or GimpleNopStatement)
                    return true;

                if (instruction.Definitions.Length == 0)
                    return false;

                if (instruction.MemoryInput is not null || instruction.MemoryOutput is not null)
                    return false;

                foreach (var definition in instruction.Definitions)
                {
                    if (definition.Name.Variable.Kind == GimpleVariableKind.Memory)
                        return false;
                }

                return true;
            }

            private static void MarkInstructionLive(
                GimpleStatementAnnotations instruction,
                HashSet<GimpleStatementAnnotations> liveInstructions,
                Queue<GimpleName> workList)
            {
                if (!liveInstructions.Add(instruction))
                    return;

                foreach (var use in instruction.Uses)
                {
                    if (use.Kind != GimpleUseKind.Memory && use.Name.Variable.Kind != GimpleVariableKind.Memory)
                        workList.Enqueue(use.Name);
                }
            }

            private static void MarkPhiLive(
                GimplePhi phi,
                HashSet<GimplePhi> livePhis,
                Queue<GimpleName> workList)
            {
                if (!livePhis.Add(phi))
                    return;

                foreach (var operand in phi.Operands)
                {
                    if (operand.Value.Variable.Kind != GimpleVariableKind.Memory)
                        workList.Enqueue(operand.Value);
                }
            }

            private void RemoveInstructionDefinitions(GimpleStatementAnnotations instruction)
            {
                foreach (var definition in instruction.Definitions)
                    _removedDefinitions.Add(definition);
            }

            private void RemovePhiDefinition(GimplePhi phi)
            {
                if (_function.TryGetDefinition(phi.Result, out var definition) && definition is not null)
                    _removedDefinitions.Add(definition);
            }

            private static ImmutableArray<GimpleOperandInfo> PruneExpressionsForStatement(
                GimpleStatement statement,
                ImmutableArray<GimpleOperandInfo> expressions)
            {
                return statement switch
                {
                    GimpleGotoStatement => ImmutableArray<GimpleOperandInfo>.Empty,
                    GimpleReturnStatement { Expression: null } => ImmutableArray<GimpleOperandInfo>.Empty,
                    _ => expressions,
                };
            }

            private Dictionary<GimpleVariable, GimpleName> CreateUndefinedNameMap()
            {
                var result = new Dictionary<GimpleVariable, GimpleName>();
                foreach (var variable in _function.Variables)
                    result[variable] = _function.GetUndefinedName(variable);
                return result;
            }

            private ImmutableArray<GimplePhi> RewritePhis(GimpleBlockAnnotations block)
            {
                if (block.Phis.Length == 0)
                    return ImmutableArray<GimplePhi>.Empty;

                var phis = ImmutableArray.CreateBuilder<GimplePhi>(block.Phis.Length);
                foreach (var phi in block.Phis)
                {
                    var operands = ImmutableArray.CreateBuilder<GimplePhiOperand>(phi.Operands.Length);
                    var changed = false;
                    foreach (var operand in phi.Operands)
                    {
                        var value = _options.EnableCopyPropagation
                            ? ResolveCopyForBlock(operand.Value, operand.Predecessor)
                            : operand.Value;

                        if (_options.EnableCopyPropagation &&
                            TryGetValueNumberRepresentative(value, operand.Predecessor, out var representative))
                        {
                            value = representative;
                        }

                        if (!ReferenceEquals(value, operand.Value))
                            changed = true;

                        operands.Add(new GimplePhiOperand(operand.Edge, value));
                    }

                    var rewritten = changed
                        ? new GimplePhi(phi.Ordinal, phi.Block, phi.Variable, phi.Result, operands.ToImmutable())
                        : phi;

                    if (changed)
                        Changed = true;

                    phis.Add(rewritten);

                    if (_options.EnableConstantFolding && TryGetPhiConstant(rewritten, out var phiConstant))
                        AddConstant(phi.Result, phiConstant);

                    if (_options.EnableCopyPropagation &&
                        (TryGetTrivialPhiCopy(rewritten, out var copy) || TryGetValueNumberPhiCopy(rewritten, out copy)))
                    {
                        AddCopy(phi.Result, copy);
                    }
                    else if (_options.EnableCopyPropagation)
                    {
                        TryAddValueNumberCopy(phi.Result, block.ControlFlowBlock);
                    }
                }

                return phis.ToImmutable();
            }

            private ImmutableArray<GimpleStatementAnnotations> RewriteInstructions(GimpleBlockAnnotations block)
            {
                if (block.Statements.Length == 0)
                    return ImmutableArray<GimpleStatementAnnotations>.Empty;

                var instructions = ImmutableArray.CreateBuilder<GimpleStatementAnnotations>(block.Statements.Length);
                foreach (var instruction in block.Statements)
                {
                    var rewritten = RewriteInstruction(instruction);
                    instructions.Add(rewritten);
                    AnalyzeInstructionDefinition(rewritten);
                }

                return instructions.ToImmutable();
            }

            private GimpleStatementAnnotations RewriteInstruction(GimpleStatementAnnotations instruction)
            {
                var expressions = ImmutableArray.CreateBuilder<GimpleOperandInfo>(instruction.Operands.Length);
                foreach (var expression in instruction.Operands)
                    expressions.Add(RewriteExpression(expression, instruction.Block, instruction.Statement));

                var rewrittenExpressions = expressions.ToImmutable();
                GimpleStatement newStatement;
                if (_commonAssignments.TryGetValue(instruction, out var replacement) && instruction.Statement is GimpleAssignStatement assign)
                {
                    rewrittenExpressions = ImmutableArray.Create(RewriteExpression(replacement, instruction.Block, instruction.Statement));
                    newStatement = GimpleAssignStatement.Single(assign.Lhs, GimpleNameMaterializer.MaterializeValue(rewrittenExpressions[0]), assign.Syntax);
                }
                else
                {
                    newStatement = MaterializeStatement(instruction.Statement, instruction, ref rewrittenExpressions);
                }
                var expressionArray = PruneExpressionsForStatement(newStatement, rewrittenExpressions);
                var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(instruction.Definitions.Length);
                foreach (var definition in instruction.Definitions)
                {
                    var rewrittenDefinition = ReferenceEquals(newStatement, definition.Statement)
                        ? definition
                        : new GimpleDefinition(
                            definition.Name,
                            definition.Kind,
                            definition.Block,
                            newStatement,
                            definition.Target,
                            definition.Parameter);

                    definitions.Add(rewrittenDefinition);
                    if (!ReferenceEquals(rewrittenDefinition, definition))
                    {
                        _definitionMap[definition] = rewrittenDefinition;
                        Changed = true;
                    }
                }

                var flags = TranslateFlags(expressionArray);
                if (instruction.MemoryOutput is not null)
                    flags |= GimpleStatementFlags.WritesMemory;
                if ((instruction.Flags & GimpleStatementFlags.ContainsCall) != 0)
                    flags |= GimpleStatementFlags.ContainsCall;
                if (newStatement is GimpleAsmStatement)
                    flags |= GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall;

                var uses = ImmutableArray.CreateBuilder<GimpleUse>();
                CollectUses(expressionArray, instruction.Block, newStatement, uses);

                var memoryInput = instruction.MemoryInput;
                if (memoryInput is not null && (flags & (GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory)) != 0)
                {
                    var memoryUse = new GimpleUse(memoryInput, GimpleUseKind.Memory, instruction.Block, newStatement, value: null);
                    uses.Add(memoryUse);
                    _uses.Add(memoryUse);
                }
                else if (memoryInput is not null)
                {
                    Changed = true;
                    memoryInput = null;
                }

                var memoryOutput = (flags & GimpleStatementFlags.WritesMemory) != 0 ? instruction.MemoryOutput : null;
                if (!ReferenceEquals(memoryOutput, instruction.MemoryOutput))
                    Changed = true;

                var rewritten = new GimpleStatementAnnotations(
                    instruction.Ordinal,
                    instruction.Block,
                    newStatement,
                    instruction.InputStatement,
                    expressionArray,
                    uses.ToImmutable(),
                    definitions.ToImmutable(),
                    memoryInput,
                    memoryOutput,
                    flags);

                if (!ReferenceEquals(newStatement, instruction.Statement) ||
                    !ReferenceEquals(memoryInput, instruction.MemoryInput) ||
                    !ReferenceEquals(memoryOutput, instruction.MemoryOutput) ||
                    flags != instruction.Flags ||
                    !SameExpressions(instruction.Operands, expressionArray))
                {
                    Changed = true;
                }

                return rewritten;
            }

            private GimpleOperandInfo RewriteExpression(
                GimpleOperandInfo expression,
                ControlFlowBlock useBlock,
                GimpleStatement statement)
            {
                if (_commonOperands.TryGetValue(expression, out var common))
                {
                    Changed = true;
                    expression = common;
                }
                if (expression.Name is not null)
                {
                    if (expression.IsAddress)
                        return expression;

                    var name = expression.Name;
                    if (_options.EnableCopyPropagation)
                        name = ResolveCopyForBlock(name, useBlock);

                    if (_options.EnableConstantFolding &&
                        CanReplaceNameWithConstant(name) &&
                        _constants.TryGetValue(name, out var constant))
                    {
                        Changed = true;
                        return CreateConstantExpression(CloneConstant(constant, expression.Original.Syntax));
                    }

                    if (_options.EnableConstantFolding &&
                        CanReplaceNameWithConstant(name) &&
                        _function.ValueNumbering.TryGetConstantValue(name, out var numberedConstant))
                    {
                        Changed = true;
                        return CreateConstantExpression(new GimpleConstantValue(numberedConstant, name.Type, expression.Original.Syntax));
                    }

                    if (_options.EnableCopyPropagation && TryGetValueNumberRepresentative(name, useBlock, out var representative))
                        name = representative;

                    if (!ReferenceEquals(name, expression.Name))
                    {
                        Changed = true;
                        return CreateNameExpression(name, name, expression.Role);
                    }

                    return expression;
                }

                if (expression.Children.Length == 0)
                    return expression;

                var children = ImmutableArray.CreateBuilder<GimpleOperandInfo>(expression.Children.Length);
                var changed = false;
                foreach (var child in expression.Children)
                {
                    var rewrittenChild = RewriteExpression(child, useBlock, statement);
                    if (!ReferenceEquals(rewrittenChild, child))
                        changed = true;
                    children.Add(rewrittenChild);
                }

                var childArray = children.ToImmutable();
                if (!changed)
                    return expression;

                Changed = true;
                return CreateCompositeExpression(expression.Original, childArray, expression.Role);
            }

            private GimpleStatement MaterializeStatement(
                GimpleStatement statement,
                GimpleStatementAnnotations instruction,
                ref ImmutableArray<GimpleOperandInfo> expressions)
            {
                switch (statement)
                {
                    case GimpleAssignStatement assign:
                        return MaterializeAssign(assign, instruction, ref expressions);

                    case GimpleCallStatement call:
                        return MaterializeCall(call, instruction, expressions);

                    case GimpleCondStatement conditional:
                        return MaterializeCond(conditional, expressions);

                    case GimpleSwitchStatement switchStatement:
                        if (expressions.Length >= 1 && TryMaterializeValue(expressions[0], out var switchValue))
                        {
                            if (_options.EnableBranchFolding &&
                                TryGetConstant(expressions[0], out var switchConstant) &&
                                TryGetSwitchTarget(switchStatement, switchConstant, out var target))
                            {
                                return new GimpleGotoStatement(target, statement.Syntax);
                            }

                            return ReferenceEquals(switchValue, switchStatement.Expression)
                                ? statement
                                : new GimpleSwitchStatement(switchValue, switchStatement.Cases, switchStatement.DefaultLabel, statement.Syntax);
                        }
                        break;

                    case GimpleReturnStatement returnStatement when returnStatement.Expression is not null:
                        if (expressions.Length >= 1 && TryMaterializeValue(expressions[0], out var returnValue))
                        {
                            return ReferenceEquals(returnValue, returnStatement.Expression)
                                ? statement
                                : new GimpleReturnStatement(returnStatement.Function, returnValue, statement.Syntax);
                        }
                        break;
                }

                return statement;
            }

            /// <summary>Rebuilds an assignment from rewritten operands, folding the computation when possible</summary>
            private GimpleStatement MaterializeAssign(
                GimpleAssignStatement assign,
                GimpleStatementAnnotations instruction,
                ref ImmutableArray<GimpleOperandInfo> expressions)
            {
                var start = GetRhsOperandStart(instruction);
                if (assign.Operands.Length == 0 || start + assign.Operands.Length > expressions.Length)
                    return assign;

                var operands = ImmutableArray.CreateBuilder<GimpleOperandInfo>(assign.Operands.Length);
                for (var i = 0; i < assign.Operands.Length; i++)
                    operands.Add(expressions[start + i]);

                var operandArray = operands.ToImmutable();
                if (_options.EnableConstantFolding && TryFoldAssign(assign, operandArray, out var folded))
                {
                    Changed = true;
                    expressions = ReplaceRhsExpressions(expressions, start, assign.Operands.Length, ImmutableArray.Create(folded));
                    return GimpleAssignStatement.Single(assign.Lhs, GimpleNameMaterializer.MaterializeValue(folded), assign.Syntax);
                }

                var values = ImmutableArray.CreateBuilder<GimpleValue>(operandArray.Length);
                var changed = false;
                for (var i = 0; i < operandArray.Length; i++)
                {
                    var value = GimpleNameMaterializer.MaterializeValue(operandArray[i]);
                    changed |= !ReferenceEquals(value, assign.Operands[i]);
                    values.Add(value);
                }

                return changed ? assign.WithOperands(assign.Lhs, values.ToImmutable()) : assign;
            }

            private static GimpleStatement MaterializeCall(
                GimpleCallStatement call,
                GimpleStatementAnnotations instruction,
                ImmutableArray<GimpleOperandInfo> expressions)
            {
                var hasLhsAddress = call.Lhs is not null && GetPrimaryDefinition(instruction) is null;
                var start = hasLhsAddress ? 1 : 0;
                if (start + call.Arguments.Length + 1 > expressions.Length)
                    return call;

                var function = GimpleNameMaterializer.MaterializeValue(expressions[start]);
                var arguments = ImmutableArray.CreateBuilder<GimpleValue>(call.Arguments.Length);
                var changed = !ReferenceEquals(function, call.Function);
                for (var i = 0; i < call.Arguments.Length; i++)
                {
                    var argument = GimpleNameMaterializer.MaterializeValue(expressions[start + 1 + i]);
                    changed |= !ReferenceEquals(argument, call.Arguments[i]);
                    arguments.Add(argument);
                }

                return changed ? call.WithOperands(call.Lhs, function, arguments.ToImmutable()) : call;
            }

            /// <summary>Rebuilds a branch, resolving it to a jump when the comparison is decided</summary>
            private GimpleStatement MaterializeCond(GimpleCondStatement conditional, ImmutableArray<GimpleOperandInfo> expressions)
            {
                if (_options.EnableBranchFolding && ReferenceEquals(conditional.WhenTrue, conditional.WhenFalse))
                    return new GimpleGotoStatement(conditional.WhenTrue, conditional.Syntax);

                if (expressions.Length < 2)
                    return conditional;

                if (_options.EnableBranchFolding &&
                    TryGetConstant(expressions[0], out var leftConstant) &&
                    TryGetConstant(expressions[1], out var rightConstant) &&
                    TryGetIntegerConstant(leftConstant, out var leftValue, out var leftInfo) &&
                    TryGetIntegerConstant(rightConstant, out var rightValue, out _) &&
                    TryCompareIntegers(leftValue, rightValue, leftInfo, conditional.Code, out var truth))
                {
                    return new GimpleGotoStatement(truth ? conditional.WhenTrue : conditional.WhenFalse, conditional.Syntax);
                }

                var left = GimpleNameMaterializer.MaterializeValue(expressions[0]);
                var right = GimpleNameMaterializer.MaterializeValue(expressions[1]);
                return ReferenceEquals(left, conditional.Lhs) && ReferenceEquals(right, conditional.Rhs)
                    ? conditional
                    : conditional.WithOperands(conditional.Code, left, right);
            }

            private static ImmutableArray<GimpleOperandInfo> ReplaceRhsExpressions(
                ImmutableArray<GimpleOperandInfo> expressions,
                int start,
                int length,
                ImmutableArray<GimpleOperandInfo> replacement)
            {
                var builder = ImmutableArray.CreateBuilder<GimpleOperandInfo>(start + replacement.Length);
                for (var i = 0; i < start; i++)
                    builder.Add(expressions[i]);

                builder.AddRange(replacement);
                for (var i = start + length; i < expressions.Length; i++)
                    builder.Add(expressions[i]);

                return builder.ToImmutable();
            }

            private void AnalyzeInstructionDefinition(GimpleStatementAnnotations instruction)
            {
                var definition = GetPrimaryDefinition(instruction);
                if (definition is null)
                    return;

                if (instruction.Statement is GimpleAssignStatement { IsConstructor: true } && IsScalarZeroFoldable(definition.Name.Type))
                {
                    AddConstant(definition.Name, CreateZeroConstant(definition.Name.Type, instruction.Statement.Syntax));
                    return;
                }

                if (_options.EnableConstantFolding &&
                    _function.ValueNumbering.TryGetConstantValue(definition.Name, out var numberedConstant))
                {
                    AddConstant(definition.Name, new GimpleConstantValue(numberedConstant, definition.Name.Type, instruction.Statement.Syntax));
                    return;
                }

                if ((instruction.Flags & (GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) != 0)
                    return;

                if (instruction.Statement is not GimpleAssignStatement { RhsClass: GimpleRhsClass.Single })
                {
                    if (_options.EnableCopyPropagation)
                        TryAddValueNumberCopy(definition.Name, instruction.Block);
                    return;
                }

                if (!TryGetSingleRhsExpression(instruction, instruction.Operands, out var valueExpression))
                    return;

                if (_options.EnableConstantFolding && TryGetConstant(valueExpression, out var constant))
                {
                    AddConstant(definition.Name, CloneConstant(constant, instruction.Statement.Syntax));
                    return;
                }

                if (_options.EnableCopyPropagation &&
                    valueExpression.Name is not null &&
                    !valueExpression.Name.IsUndefined &&
                    valueExpression.Name.Variable.Kind != GimpleVariableKind.Memory &&
                    SameType(valueExpression.Name.Type, definition.Name.Type))
                {
                    AddCopy(definition.Name, valueExpression.Name);
                    return;
                }

                if (_options.EnableCopyPropagation)
                    TryAddValueNumberCopy(definition.Name, instruction.Block);
            }

            private void TryAddValueNumberCopy(GimpleName name, ControlFlowBlock useBlock)
            {
                if (!_options.EnableCopyPropagation)
                    return;

                if (name.IsUndefined || name.Variable.Kind == GimpleVariableKind.Memory)
                    return;

                if (!_function.ValueNumbering.TryGetValueNumber(name, out var valueNumber) || valueNumber is null)
                    return;

                if (!CanUseValueNumberForCopy(valueNumber))
                    return;

                if (TryGetValueNumberRepresentative(valueNumber, name, useBlock, out var representative) &&
                    !ReferenceEquals(representative, name))
                {
                    AddCopy(name, representative);
                    return;
                }

                if (!_representativesByValueNumber.TryGetValue(valueNumber.Id, out var list))
                {
                    list = new List<GimpleName>();
                    _representativesByValueNumber.Add(valueNumber.Id, list);
                }

                if (!list.Contains(name))
                    list.Add(name);
            }

            private bool TryGetValueNumberRepresentative(GimpleName name, ControlFlowBlock useBlock, out GimpleName representative)
            {
                representative = null!;

                if (!_function.ValueNumbering.TryGetValueNumber(name, out var valueNumber) || valueNumber is null)
                    return false;

                if (!CanUseValueNumberForCopy(valueNumber))
                    return false;

                return TryGetValueNumberRepresentative(valueNumber, name, useBlock, out representative);
            }

            private bool TryGetValueNumberRepresentative(
                ValueNumber valueNumber,
                GimpleName useName,
                ControlFlowBlock useBlock,
                out GimpleName representative)
            {
                representative = null!;

                if (!_representativesByValueNumber.TryGetValue(valueNumber.Id, out var candidates))
                    return false;

                var bestScore = int.MinValue;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = ResolveCopyForBlock(candidates[i], useBlock);
                    if (candidate.IsUndefined || candidate.Variable.Kind == GimpleVariableKind.Memory)
                        continue;

                    if (ReferenceEquals(candidate, useName))
                        continue;

                    if (!CanSubstituteName(useName, candidate))
                        continue;

                    if (!CanUseNameAtBlock(candidate, useBlock))
                        continue;

                    var score = ScoreCopyCandidate(useName, candidate, useBlock);
                    if (score <= 0 || score <= bestScore)
                        continue;

                    bestScore = score;
                    representative = candidate;
                }

                return representative is not null;
            }

            private static bool CanUseValueNumberForCopy(ValueNumber valueNumber)
            {
                if (valueNumber.IsUnique || valueNumber.IsMemoryDependent)
                    return false;

                return valueNumber.Kind is ValueNumberKind.Entry or ValueNumberKind.Constant or ValueNumberKind.Expression or ValueNumberKind.Phi;
            }

            private int ScoreCopyCandidate(GimpleName useName, GimpleName candidate, ControlFlowBlock useBlock)
            {
                var score = 1;

                if (useName.Variable.Kind == GimpleVariableKind.Temporary)
                    score += 8;
                if (candidate.Variable.Kind == GimpleVariableKind.Temporary)
                    score -= 8;

                if (ReferenceEquals(useName.Variable, candidate.Variable))
                    score += 2;

                if (_useCounts.TryGetValue(candidate, out var useCount))
                    score += Math.Min(useCount, 8);

                if (_function.TryGetDefinition(candidate, out var definition) && definition?.Block is not null)
                {
                    if (ReferenceEquals(definition.Block, useBlock))
                        score += 8;

                    if (_dominatorDepths.TryGetValue(definition.Block, out var depth))
                        score += Math.Min(depth, 16);
                }

                return score;
            }

            private GimpleName ResolveCopyForBlock(GimpleName name, ControlFlowBlock useBlock)
            {
                var current = name;
                var remaining = _copies.Count + 1;

                while (remaining-- > 0 && _copies.TryGetValue(current, out var next))
                {
                    if (!CanUseNameAtBlock(next, useBlock))
                        break;

                    current = next;
                }

                return current;
            }

            private void AddCopy(GimpleName destination, GimpleName source)
            {
                if (destination.IsUndefined || source.IsUndefined)
                    return;
                if (destination.Variable.Kind == GimpleVariableKind.Memory || source.Variable.Kind == GimpleVariableKind.Memory)
                    return;
                if (!CanSubstituteName(destination, source))
                    return;
                if (ReferenceEquals(destination, source))
                    return;

                if (_function.TryGetDefinition(destination, out var destinationDefinition) &&
                    destinationDefinition is not null &&
                    !CanUseNameAtDefinition(source, destinationDefinition))
                {
                    return;
                }

                _copies[destination] = source;
            }

            private static bool CanSubstituteName(GimpleName destination, GimpleName source)
            {
                if (!SameType(destination.Type, source.Type))
                    return false;

                if (IsVolatileOrAtomic(destination.Type) || IsVolatileOrAtomic(source.Type))
                    return false;

                if (ReferenceEquals(destination.Variable, source.Variable))
                    return true;

                return !HasExplicitRegister(destination) && !HasExplicitRegister(source);
            }

            private static bool HasExplicitRegister(GimpleName name)
                => name.Variable.Symbol is VariableSymbol { ExplicitRegisterName: not null };

            private static bool CanReplaceNameWithConstant(GimpleName name)
                => !IsVolatileOrAtomic(name.Type) && !HasExplicitRegister(name);

            private void AddConstant(GimpleName destination, GimpleConstantValue constant)
            {
                if (destination.IsUndefined || destination.Variable.Kind == GimpleVariableKind.Memory)
                    return;
                if (!CanReplaceNameWithConstant(destination) || !SameType(destination.Type, constant.Type))
                    return;

                _constants[destination] = constant;
            }

            private bool CanUseNameAtDefinition(GimpleName name, GimpleDefinition destination)
            {
                if (!_function.TryGetDefinition(name, out var source) || source is null)
                    return false;

                if (source.Kind == GimpleDefinitionKind.Undefined || source.Block is null || destination.Block is null)
                    return false;

                if (!ReferenceEquals(source.Block, destination.Block))
                    return source.Block.Dominates(destination.Block);

                if (!_definitionOrder.TryGetValue(name, out var sourceOrder) ||
                    !_definitionOrder.TryGetValue(destination.Name, out var destinationOrder))
                {
                    return false;
                }

                if (sourceOrder < 0 && destinationOrder < 0)
                    return sourceOrder <= destinationOrder;

                return sourceOrder < destinationOrder;
            }

            private bool CanUseNameAtBlock(GimpleName name, ControlFlowBlock useBlock)
            {
                if (!_function.TryGetDefinition(name, out var definition) || definition is null)
                    return false;

                if (definition.Kind == GimpleDefinitionKind.Undefined)
                    return false;

                if (definition.Block is null)
                    return false;

                return definition.Block.Dominates(useBlock);
            }

            private static bool TryGetTrivialPhiCopy(GimplePhi phi, out GimpleName copy)
            {
                copy = null!;
                if (phi.Operands.Length == 0)
                    return false;

                var first = phi.Operands[0].Value;
                if (first.IsUndefined || first.Variable.Kind == GimpleVariableKind.Memory)
                    return false;

                for (var i = 1; i < phi.Operands.Length; i++)
                {
                    if (!ReferenceEquals(first, phi.Operands[i].Value))
                        return false;
                }

                copy = first;
                return true;
            }

            private bool TryGetValueNumberPhiCopy(GimplePhi phi, out GimpleName copy)
            {
                copy = null!;
                if (phi.Operands.Length == 0 || phi.Result.Variable.Kind == GimpleVariableKind.Memory)
                    return false;

                if (!_function.ValueNumbering.TryGetValueNumber(phi.Operands[0].Value, out var valueNumber) ||
                    valueNumber is null ||
                    !CanUseValueNumberForCopy(valueNumber))
                {
                    return false;
                }

                for (var i = 1; i < phi.Operands.Length; i++)
                {
                    if (!_function.ValueNumbering.TryGetValueNumber(phi.Operands[i].Value, out var operandNumber) ||
                        !ReferenceEquals(valueNumber, operandNumber))
                    {
                        return false;
                    }
                }

                if (!_function.TryGetDefinition(phi.Result, out var phiDefinition) || phiDefinition is null)
                    return false;

                var bestScore = int.MinValue;
                foreach (var operand in phi.Operands)
                {
                    var candidate = ResolveCopyForBlock(operand.Value, operand.Predecessor);
                    if (ReferenceEquals(candidate, phi.Result) || !CanSubstituteName(phi.Result, candidate))
                        continue;
                    if (!CanUseNameAtDefinition(candidate, phiDefinition))
                        continue;

                    var score = ScoreCopyCandidate(phi.Result, candidate, phi.Block);
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    copy = candidate;
                }

                return copy is not null;
            }

            private bool TryGetPhiConstant(GimplePhi phi, out GimpleConstantValue constant)
            {
                constant = null!;
                if (phi.Operands.Length == 0 || !CanReplaceNameWithConstant(phi.Result))
                    return false;

                if (!_function.ValueNumbering.TryGetValueNumber(phi.Operands[0].Value, out var valueNumber) ||
                    valueNumber is null ||
                    !valueNumber.HasConstantValue)
                {
                    return false;
                }

                for (var i = 1; i < phi.Operands.Length; i++)
                {
                    if (!_function.ValueNumbering.TryGetValueNumber(phi.Operands[i].Value, out var operandNumber) ||
                        !ReferenceEquals(valueNumber, operandNumber))
                    {
                        return false;
                    }
                }

                constant = new GimpleConstantValue(valueNumber.ConstantValue, phi.Result.Type);
                return true;
            }

            private static GimpleDefinition? GetPrimaryDefinition(GimpleStatementAnnotations instruction)
            {
                foreach (var definition in instruction.Definitions)
                {
                    if (definition.Name.Variable.Kind != GimpleVariableKind.Memory)
                        return definition;
                }

                return null;
            }

            /// <summary>Gets the operand index at which an assignment right-hand side starts</summary>
            /// <remarks>A memory target contributes its address ahead of the right-hand side operands</remarks>
            private static int GetRhsOperandStart(GimpleStatementAnnotations instruction)
                => GetPrimaryDefinition(instruction) is null ? 1 : 0;

            private static bool TryGetSingleRhsExpression(
                GimpleStatementAnnotations instruction,
                ImmutableArray<GimpleOperandInfo> expressions,
                out GimpleOperandInfo expression)
            {
                var index = GetRhsOperandStart(instruction);
                if (index < expressions.Length)
                {
                    expression = expressions[index];
                    return true;
                }

                expression = null!;
                return false;
            }

            private static void CollectExpressionUses(
                GimpleOperandInfo expression,
                ControlFlowBlock block,
                GimpleStatement statement,
                ImmutableArray<GimpleUse>.Builder uses)
            {
                if (expression.Name is not null)
                {
                    var kind = expression.IsAddress ? GimpleUseKind.Address : GimpleUseKind.Value;
                    uses.Add(new GimpleUse(expression.Name, kind, block, statement, expression.Original));
                    return;
                }

                foreach (var child in expression.Children)
                    CollectExpressionUses(child, block, statement, uses);
            }

            private void CollectUses(ImmutableArray<GimpleOperandInfo> expressions, ControlFlowBlock block, GimpleStatement statement, ImmutableArray<GimpleUse>.Builder uses)
            {
                foreach (var expression in expressions)
                {
                    CollectExpressionUses(expression, block, statement, uses);
                }

                foreach (var use in uses)
                    _uses.Add(use);
            }

            private static GimpleStatementFlags TranslateFlags(ImmutableArray<GimpleOperandInfo> expressions)
            {
                var flags = GimpleStatementFlags.None;
                foreach (var expression in expressions)
                {
                    if (expression.ReadsMemory)
                        flags |= GimpleStatementFlags.ReadsMemory;
                    if (expression.WritesMemory)
                        flags |= GimpleStatementFlags.WritesMemory;
                    if (expression.ContainsCall)
                        flags |= GimpleStatementFlags.ContainsCall;
                }

                return flags;
            }

            private static bool SameExpressions(ImmutableArray<GimpleOperandInfo> left, ImmutableArray<GimpleOperandInfo> right)
            {
                if (left.Length != right.Length)
                    return false;

                for (var i = 0; i < left.Length; i++)
                {
                    if (!ReferenceEquals(left[i], right[i]))
                        return false;
                }

                return true;
            }

            private static GimpleOperandInfo CreateNameExpression(GimpleValue original, GimpleName name, GimpleOperandRole role)
                => new GimpleOperandInfo(original, name, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role);

            private static GimpleOperandInfo CreateConstantExpression(GimpleConstantValue constant)
                => new GimpleOperandInfo(constant, name: null, ImmutableArray<GimpleOperandInfo>.Empty, readsMemory: false, writesMemory: false, containsCall: false);

            private static GimpleOperandInfo CreateCompositeExpression(GimpleValue original, ImmutableArray<GimpleOperandInfo> children, GimpleOperandRole role)
            {
                var readsMemory = false;
                var writesMemory = false;
                var containsCall = false;
                foreach (var child in children)
                {
                    readsMemory |= child.ReadsMemory;
                    writesMemory |= child.WritesMemory;
                    containsCall |= child.ContainsCall;
                }

                switch (original)
                {
                    case GimpleSymbolValue:
                    case GimpleTemporaryValue:
                    case GimpleIndirectExpression:
                    case GimpleElementAccessExpression:
                    case GimpleMemberAccessExpression:
                        if (role == GimpleOperandRole.Value)
                            readsMemory = true;
                        break;

                }

                return new GimpleOperandInfo(original, name: null, children, readsMemory, writesMemory, containsCall, role);
            }

            private static bool TryMaterializeValue(GimpleOperandInfo expression, out GimpleValue value)
            {
                value = GimpleNameMaterializer.MaterializeValue(expression);
                return true;
            }

            /// <summary>Folds an assignment right-hand side into a single value when its operands allow it</summary>
            /// <remarks>This is the statement-level counterpart of the GCC fold_stmt pass</remarks>
            private bool TryFoldAssign(
                GimpleAssignStatement assign,
                ImmutableArray<GimpleOperandInfo> operands,
                out GimpleOperandInfo folded)
            {
                switch (assign.RhsClass)
                {
                    case GimpleRhsClass.Unary when operands.Length == 1:
                        return TryFoldUnary(assign.Subcode, assign.Lhs.Type, assign.Syntax, operands[0], out folded);

                    case GimpleRhsClass.Binary when operands.Length == 2:
                        return TryFoldBinary(assign.Subcode, assign.Lhs.Type, assign.Syntax, operands[0], operands[1], out folded);
                }

                folded = null!;
                return false;
            }

            private bool TryFoldUnary(
                GimpleTreeCode code,
                QualifiedType type,
                SyntaxNode? syntax,
                GimpleOperandInfo operand,
                out GimpleOperandInfo folded)
            {
                if (GimpleOperators.IsConversion(code))
                    return TryFoldConversion(code, type, syntax, operand, out folded);

                if (!TryGetConstant(operand, out var constant))
                {
                    folded = null!;
                    return false;
                }

                if (code == GimpleTreeCode.TruthNotExpr && TryGetIntegerConstant(constant, out var truthValue, out _))
                {
                    folded = CreateConstantExpression(CreateIntegerConstant(truthValue == 0 ? 1UL : 0UL, type, syntax));
                    return true;
                }

                if (!TryGetIntegerConstant(constant, out var value, out var info))
                {
                    folded = null!;
                    return false;
                }

                switch (code)
                {
                    case GimpleTreeCode.NegateExpr:
                        if (TryNegateInteger(value, info, type, syntax, out var negated))
                        {
                            folded = CreateConstantExpression(negated);
                            return true;
                        }
                        break;

                    case GimpleTreeCode.BitNotExpr:
                        if (TryCastInteger(~value, type, syntax, out var complemented))
                        {
                            folded = CreateConstantExpression(complemented);
                            return true;
                        }
                        break;
                }

                folded = null!;
                return false;
            }

            private bool TryFoldConversion(
                GimpleTreeCode code,
                QualifiedType type,
                SyntaxNode? syntax,
                GimpleOperandInfo operand,
                out GimpleOperandInfo folded)
            {
                if (code == GimpleTreeCode.NopExpr && SameType(operand.Original.Type, type))
                {
                    folded = operand;
                    return true;
                }

                if (TryGetConstant(operand, out var constant) && TryCastIntegerConstant(constant, type, syntax, out var casted))
                {
                    folded = CreateConstantExpression(casted);
                    return true;
                }

                folded = null!;
                return false;
            }

            private bool TryFoldBinary(
                GimpleTreeCode code,
                QualifiedType type,
                SyntaxNode? syntax,
                GimpleOperandInfo left,
                GimpleOperandInfo right,
                out GimpleOperandInfo folded)
            {
                if (TryFoldBinaryConstants(code, type, syntax, left, right, out folded))
                    return true;

                if (TryFoldBinaryIdentity(code, type, left, right, out folded))
                    return true;

                if (TryFoldSameNameComparison(code, type, syntax, left, right, out folded))
                    return true;

                folded = null!;
                return false;
            }

            private bool TryFoldBinaryConstants(
                GimpleTreeCode code,
                QualifiedType type,
                SyntaxNode? syntax,
                GimpleOperandInfo left,
                GimpleOperandInfo right,
                out GimpleOperandInfo folded)
            {
                folded = null!;
                if (!TryGetConstant(left, out var leftConstant) || !TryGetConstant(right, out var rightConstant))
                    return false;

                if (code is GimpleTreeCode.TruthAndExpr or GimpleTreeCode.TruthOrExpr &&
                    TryGetIntegerConstant(leftConstant, out var leftTruth, out _) &&
                    TryGetIntegerConstant(rightConstant, out var rightTruth, out _))
                {
                    var truth = code == GimpleTreeCode.TruthAndExpr
                        ? (leftTruth != 0 && rightTruth != 0)
                        : (leftTruth != 0 || rightTruth != 0);
                    folded = CreateConstantExpression(CreateIntegerConstant(truth ? 1UL : 0UL, type, syntax));
                    return true;
                }

                if (!TryGetIntegerConstant(leftConstant, out var leftValue, out var leftInfo) ||
                    !TryGetIntegerConstant(rightConstant, out var rightValue, out _))
                {
                    return false;
                }

                if (!SameType(leftConstant.Type, rightConstant.Type))
                    return false;

                if (GimpleOperators.IsComparison(code))
                {
                    if (TryCompareIntegers(leftValue, rightValue, leftInfo, code, out var comparison))
                    {
                        folded = CreateConstantExpression(CreateIntegerConstant(comparison ? 1UL : 0UL, type, syntax));
                        return true;
                    }

                    return false;
                }

                if (!SameType(leftConstant.Type, type))
                    return false;

                if (TryEvaluateIntegerBinary(leftValue, rightValue, leftInfo, code, type, syntax, out var result))
                {
                    folded = CreateConstantExpression(result);
                    return true;
                }

                return false;
            }

            private bool TryFoldBinaryIdentity(
                GimpleTreeCode code,
                QualifiedType type,
                GimpleOperandInfo left,
                GimpleOperandInfo right,
                out GimpleOperandInfo folded)
            {
                folded = null!;

                if (IsZero(right) && SameType(left.Original.Type, type))
                {
                    switch (code)
                    {
                        case GimpleTreeCode.PlusExpr:
                        case GimpleTreeCode.PointerPlusExpr:
                        case GimpleTreeCode.MinusExpr:
                        case GimpleTreeCode.BitIorExpr:
                        case GimpleTreeCode.BitXorExpr:
                        case GimpleTreeCode.LshiftExpr:
                        case GimpleTreeCode.RshiftExpr:
                            folded = left;
                            return true;
                    }
                }

                if (IsZero(left) && SameType(right.Original.Type, type))
                {
                    switch (code)
                    {
                        case GimpleTreeCode.PlusExpr:
                        case GimpleTreeCode.BitIorExpr:
                        case GimpleTreeCode.BitXorExpr:
                            folded = right;
                            return true;
                    }
                }

                if (IsOne(right) && SameType(left.Original.Type, type))
                {
                    switch (code)
                    {
                        case GimpleTreeCode.MultExpr:
                        case GimpleTreeCode.TruncDivExpr:
                        case GimpleTreeCode.ExactDivExpr:
                        case GimpleTreeCode.RdivExpr:
                            folded = left;
                            return true;
                    }
                }

                if (IsOne(left) && SameType(right.Original.Type, type) && code == GimpleTreeCode.MultExpr)
                {
                    folded = right;
                    return true;
                }

                return false;
            }

            private bool TryFoldSameNameComparison(
                GimpleTreeCode code,
                QualifiedType type,
                SyntaxNode? syntax,
                GimpleOperandInfo left,
                GimpleOperandInfo right,
                out GimpleOperandInfo folded)
            {
                folded = null!;
                if (left.Name is null || right.Name is null || !ReferenceEquals(left.Name, right.Name))
                    return false;

                if (!IsIntegerLike(left.Name.Type) && !IsPointerLike(left.Name.Type))
                    return false;

                switch (code)
                {
                    case GimpleTreeCode.EqExpr:
                        folded = CreateConstantExpression(CreateIntegerConstant(1UL, type, syntax));
                        return true;

                    case GimpleTreeCode.NeExpr:
                        folded = CreateConstantExpression(CreateIntegerConstant(0UL, type, syntax));
                        return true;
                }

                return false;
            }

            private bool TryEvaluateIntegerBinary(
                ulong left,
                ulong right,
                IntegerInfo info,
                GimpleTreeCode code,
                QualifiedType resultType,
                SyntaxNode? syntax,
                out GimpleConstantValue result)
            {
                result = null!;

                if (info.IsSigned)
                {
                    var leftSigned = ToSigned(left, info.Bits);
                    var rightSigned = ToSigned(right, info.Bits);
                    long signedResult;
                    try
                    {
                        checked
                        {
                            switch (code)
                            {
                                case GimpleTreeCode.PlusExpr:
                                    signedResult = leftSigned + rightSigned;
                                    break;
                                case GimpleTreeCode.MinusExpr:
                                    signedResult = leftSigned - rightSigned;
                                    break;
                                case GimpleTreeCode.MultExpr:
                                    signedResult = leftSigned * rightSigned;
                                    break;
                                case GimpleTreeCode.TruncDivExpr:
                                case GimpleTreeCode.ExactDivExpr:
                                    if (rightSigned == 0 || (leftSigned == MinSigned(info.Bits) && rightSigned == -1))
                                        return false;
                                    signedResult = leftSigned / rightSigned;
                                    break;
                                case GimpleTreeCode.TruncModExpr:
                                    if (rightSigned == 0 || (leftSigned == MinSigned(info.Bits) && rightSigned == -1))
                                        return false;
                                    signedResult = leftSigned % rightSigned;
                                    break;
                                case GimpleTreeCode.BitAndExpr:
                                    result = CreateIntegerConstant(left & right, resultType, syntax);
                                    return true;
                                case GimpleTreeCode.BitIorExpr:
                                    result = CreateIntegerConstant(left | right, resultType, syntax);
                                    return true;
                                case GimpleTreeCode.BitXorExpr:
                                    result = CreateIntegerConstant(left ^ right, resultType, syntax);
                                    return true;
                                default:
                                    return false;
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }

                    if (signedResult < MinSigned(info.Bits) || signedResult > MaxSigned(info.Bits))
                        return false;

                    result = CreateIntegerConstant(unchecked((ulong)signedResult), resultType, syntax);
                    return true;
                }

                var mask = Mask(info.Bits);
                ulong unsignedResult;
                switch (code)
                {
                    case GimpleTreeCode.PlusExpr:
                        unsignedResult = (left + right) & mask;
                        break;
                    case GimpleTreeCode.MinusExpr:
                        unsignedResult = (left - right) & mask;
                        break;
                    case GimpleTreeCode.MultExpr:
                        unsignedResult = (left * right) & mask;
                        break;
                    case GimpleTreeCode.TruncDivExpr:
                    case GimpleTreeCode.ExactDivExpr:
                        if (right == 0)
                            return false;
                        unsignedResult = left / right;
                        break;
                    case GimpleTreeCode.TruncModExpr:
                        if (right == 0)
                            return false;
                        unsignedResult = left % right;
                        break;
                    case GimpleTreeCode.BitAndExpr:
                        unsignedResult = left & right;
                        break;
                    case GimpleTreeCode.BitIorExpr:
                        unsignedResult = left | right;
                        break;
                    case GimpleTreeCode.BitXorExpr:
                        unsignedResult = left ^ right;
                        break;
                    default:
                        return false;
                }

                result = CreateIntegerConstant(unsignedResult, resultType, syntax);
                return true;
            }

            private bool TryCompareIntegers(ulong left, ulong right, IntegerInfo info, GimpleTreeCode code, out bool result)
            {
                var supported = code is GimpleTreeCode.EqExpr or GimpleTreeCode.NeExpr or
                    GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr or
                    GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr;

                if (info.IsSigned)
                {
                    var leftSigned = ToSigned(left, info.Bits);
                    var rightSigned = ToSigned(right, info.Bits);
                    result = code switch
                    {
                        GimpleTreeCode.EqExpr => leftSigned == rightSigned,
                        GimpleTreeCode.NeExpr => leftSigned != rightSigned,
                        GimpleTreeCode.LtExpr => leftSigned < rightSigned,
                        GimpleTreeCode.LeExpr => leftSigned <= rightSigned,
                        GimpleTreeCode.GtExpr => leftSigned > rightSigned,
                        GimpleTreeCode.GeExpr => leftSigned >= rightSigned,
                        _ => false,
                    };
                    return supported;
                }

                result = code switch
                {
                    GimpleTreeCode.EqExpr => left == right,
                    GimpleTreeCode.NeExpr => left != right,
                    GimpleTreeCode.LtExpr => left < right,
                    GimpleTreeCode.LeExpr => left <= right,
                    GimpleTreeCode.GtExpr => left > right,
                    GimpleTreeCode.GeExpr => left >= right,
                    _ => false,
                };
                return supported;
            }

            private bool TryNegateInteger(ulong value, IntegerInfo info, QualifiedType resultType, SyntaxNode? syntax, out GimpleConstantValue result)
            {
                result = null!;
                if (info.IsSigned)
                {
                    var signed = ToSigned(value, info.Bits);
                    if (signed == MinSigned(info.Bits))
                        return false;

                    result = CreateIntegerConstant(unchecked((ulong)(-signed)), resultType, syntax);
                    return true;
                }

                result = CreateIntegerConstant(unchecked(0UL - value), resultType, syntax);
                return true;
            }

            private bool TryCastIntegerConstant(GimpleConstantValue constant, QualifiedType targetType, SyntaxNode? syntax, out GimpleConstantValue casted)
            {
                casted = null!;
                if (!TryGetIntegerConstant(constant, out var value, out var sourceInfo))
                    return false;

                if (!TryGetIntegerInfo(targetType, out _))
                    return false;

                var targetValue = sourceInfo.IsSigned
                    ? unchecked((ulong)ToSigned(value, sourceInfo.Bits))
                    : value;

                casted = CreateIntegerConstant(targetValue, targetType, syntax);
                return true;
            }

            private bool TryCastInteger(ulong value, QualifiedType targetType, SyntaxNode? syntax, out GimpleConstantValue casted)
            {
                casted = null!;
                if (!TryGetIntegerInfo(targetType, out _))
                    return false;

                casted = CreateIntegerConstant(value, targetType, syntax);
                return true;
            }

            private bool TryGetIntegerConstant(GimpleConstantValue constant, out ulong value, out IntegerInfo info)
            {
                if (!TryGetIntegerInfo(constant.Type, out info))
                {
                    value = 0;
                    return false;
                }

                if (!TryConvertIntegerObject(constant.Value, out var raw))
                {
                    value = 0;
                    return false;
                }

                value = raw & Mask(info.Bits);
                return true;
            }

            private bool TryGetIntegerInfo(QualifiedType type, out IntegerInfo info)
            {
                var normalized = GimpleTypeHelpers.Normalize(type);
                if (normalized.Type.Kind == TypeKind.Enum)
                {
                    info = new IntegerInfo(_target.SizeOf(normalized) * 8, isSigned: true);
                    return true;
                }

                if (normalized.Type is not BuiltinType builtin)
                {
                    info = default;
                    return false;
                }

                var bits = Math.Max(1, _target.SizeOf(normalized) * 8);
                switch (builtin.BuiltinKind)
                {
                    case BuiltinTypeKind.Bool:
                        info = new IntegerInfo(bits, isSigned: false);
                        return true;
                    case BuiltinTypeKind.Char:
                        info = new IntegerInfo(bits, _target.CharSignedness != CharSignedness.Unsigned);
                        return true;
                    case BuiltinTypeKind.SignedChar:
                    case BuiltinTypeKind.Short:
                    case BuiltinTypeKind.Int:
                    case BuiltinTypeKind.Long:
                    case BuiltinTypeKind.LongLong:
                        info = new IntegerInfo(bits, isSigned: true);
                        return true;
                    case BuiltinTypeKind.UnsignedChar:
                    case BuiltinTypeKind.UnsignedShort:
                    case BuiltinTypeKind.UnsignedInt:
                    case BuiltinTypeKind.UnsignedLong:
                    case BuiltinTypeKind.UnsignedLongLong:
                        info = new IntegerInfo(bits, isSigned: false);
                        return true;
                    default:
                        info = default;
                        return false;
                }
            }

            private static bool TryConvertIntegerObject(object? constant, out ulong value)
            {
                switch (constant)
                {
                    case null:
                        value = 0;
                        return true;
                    case bool b:
                        value = b ? 1UL : 0UL;
                        return true;
                    case char c:
                        value = c;
                        return true;
                    case byte b:
                        value = b;
                        return true;
                    case sbyte sb:
                        value = unchecked((ulong)sb);
                        return true;
                    case short s:
                        value = unchecked((ulong)s);
                        return true;
                    case ushort us:
                        value = us;
                        return true;
                    case int i:
                        value = unchecked((ulong)i);
                        return true;
                    case uint ui:
                        value = ui;
                        return true;
                    case long l:
                        value = unchecked((ulong)l);
                        return true;
                    case ulong ul:
                        value = ul;
                        return true;
                    default:
                        value = 0;
                        return false;
                }
            }

            private GimpleConstantValue CreateIntegerConstant(ulong value, QualifiedType type, SyntaxNode? syntax)
            {
                if (!TryGetIntegerInfo(type, out var info))
                    return new GimpleConstantValue(value, type, syntax);

                value &= Mask(info.Bits);
                object boxed;
                if (info.IsSigned)
                {
                    var signed = ToSigned(value, info.Bits);
                    boxed = info.Bits <= 32 ? unchecked((int)signed) : signed;
                }
                else
                {
                    boxed = info.Bits <= 32 ? unchecked((uint)value) : value;
                }

                if (type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
                    boxed = value == 0 ? 0 : 1;

                return new GimpleConstantValue(boxed, type, syntax);
            }

            private GimpleConstantValue CreateZeroConstant(QualifiedType type, SyntaxNode? syntax)
                => CreateIntegerConstant(0UL, type, syntax);

            private bool IsScalarZeroFoldable(QualifiedType type)
                => TryGetIntegerInfo(type, out _);

            private bool TryGetConstant(GimpleOperandInfo expression, out GimpleConstantValue constant)
            {
                if (expression.Name is null && expression.Children.Length == 0 && expression.Original is GimpleConstantValue value)
                {
                    constant = value;
                    return true;
                }

                if (expression.Name is not null &&
                    CanReplaceNameWithConstant(expression.Name) &&
                    _constants.TryGetValue(expression.Name, out var propagated))
                {
                    constant = propagated;
                    return true;
                }

                if (expression.Name is not null &&
                    CanReplaceNameWithConstant(expression.Name) &&
                    _function.ValueNumbering.TryGetConstantValue(expression.Name, out var numberedConstant))
                {
                    constant = new GimpleConstantValue(numberedConstant, expression.Name.Type, expression.Original.Syntax);
                    return true;
                }

                constant = null!;
                return false;
            }

            private bool IsZero(GimpleOperandInfo expression)
                => TryGetConstant(expression, out var constant) && TryGetIntegerConstant(constant, out var value, out _) && value == 0;

            private bool IsOne(GimpleOperandInfo expression)
                => TryGetConstant(expression, out var constant) && TryGetIntegerConstant(constant, out var value, out _) && value == 1;

            private bool TryGetBranchTruth(GimpleConstantValue constant, out bool truth)
            {
                if (TryGetIntegerConstant(constant, out var value, out _))
                {
                    truth = value != 0;
                    return true;
                }

                truth = false;
                return false;
            }

            private bool TryGetSwitchTarget(
                GimpleSwitchStatement switchStatement,
                GimpleConstantValue switchConstant,
                out GimpleLabel target)
            {
                if (!TryGetIntegerConstant(switchConstant, out var switchValue, out _))
                {
                    target = null!;
                    return false;
                }

                foreach (var @case in switchStatement.Cases)
                {
                    if (TryGetIntegerConstant(@case.Value, out var caseValue, out _) && switchValue == caseValue)
                    {
                        target = @case.Target;
                        return true;
                    }
                }

                target = switchStatement.DefaultLabel;
                return true;
            }

            private static GimpleConstantValue CloneConstant(GimpleConstantValue constant, SyntaxNode? syntax)
                => syntax is null || ReferenceEquals(syntax, constant.Syntax)
                    ? constant
                    : new GimpleConstantValue(constant.Value, constant.Type, syntax);

            private static ulong Mask(int bits)
                => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1UL;

            private static long ToSigned(ulong value, int bits)
            {
                value &= Mask(bits);
                if (bits >= 64)
                    return unchecked((long)value);

                var signBit = 1UL << (bits - 1);
                return (value & signBit) == 0
                    ? (long)value
                    : unchecked((long)(value | ~Mask(bits)));
            }

            private static long MinSigned(int bits)
                => bits >= 64 ? long.MinValue : -(1L << (bits - 1));

            private static long MaxSigned(int bits)
                => bits >= 64 ? long.MaxValue : (1L << (bits - 1)) - 1L;

            private static bool SameType(QualifiedType left, QualifiedType right)
                => string.Equals(
                    GimpleTypeHelpers.Normalize(left).ToDisplayString(),
                    GimpleTypeHelpers.Normalize(right).ToDisplayString(),
                    StringComparison.Ordinal);

            private static bool IsVolatileOrAtomic(QualifiedType type)
                => (GimpleTypeHelpers.Normalize(type).Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;

            private static bool IsPointerLike(QualifiedType type)
                => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

            private static bool IsIntegerLike(QualifiedType type)
            {
                if (type.Type.Kind == TypeKind.Enum)
                    return true;

                if (type.Type is not BuiltinType builtin)
                    return false;

                return builtin.BuiltinKind is
                    BuiltinTypeKind.Bool or
                    BuiltinTypeKind.Char or
                    BuiltinTypeKind.SignedChar or
                    BuiltinTypeKind.UnsignedChar or
                    BuiltinTypeKind.Short or
                    BuiltinTypeKind.UnsignedShort or
                    BuiltinTypeKind.Int or
                    BuiltinTypeKind.UnsignedInt or
                    BuiltinTypeKind.Long or
                    BuiltinTypeKind.UnsignedLong or
                    BuiltinTypeKind.LongLong or
                    BuiltinTypeKind.UnsignedLongLong;
            }

            private readonly struct IntegerInfo
            {
                public int Bits { get; }
                public bool IsSigned { get; }

                public IntegerInfo(int bits, bool isSigned)
                {
                    Bits = bits <= 0 ? 1 : Math.Min(64, bits);
                    IsSigned = isSigned;
                }
            }
        }
    }
}
