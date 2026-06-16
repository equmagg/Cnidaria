using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Cnidaria.C
{
    internal static class SsaOptimizer
    {
        public static SsaFunction Optimize(
            SsaFunction function,
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
                !options.EnableDeadCodeElimination)
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

        private sealed class Pass
        {
            private readonly SsaFunction _function;
            private readonly TargetInfo _target;
            private readonly SsaOptimizationOptions _options;
            private readonly ValueNumberingOptions _valueNumberingOptions;
            private readonly Dictionary<SsaName, SsaName> _copies = new();
            private readonly Dictionary<SsaName, GimpleConstantValue> _constants = new();
            private readonly Dictionary<int, List<SsaName>> _representativesByValueNumber = new();
            private readonly Dictionary<SsaDefinition, SsaDefinition> _definitionMap = new();
            private readonly HashSet<SsaDefinition> _removedDefinitions = new();
            private readonly List<SsaUse> _uses = new();

            public bool Changed { get; private set; }

            public Pass(
                SsaFunction function,
                TargetInfo target,
                SsaOptimizationOptions options,
                ValueNumberingOptions valueNumberingOptions)
            {
                _function = function;
                _target = target;
                _options = options;
                _valueNumberingOptions = valueNumberingOptions;
            }

            public SsaFunction Run()
            {
                var blocksByControlFlowBlock = new Dictionary<ControlFlowBlock, SsaBlock>();

                foreach (var controlFlowBlock in _function.ControlFlowFunction.ReversePostOrder)
                {
                    if (controlFlowBlock.IsExit || !controlFlowBlock.IsReachable)
                        continue;

                    if (!_function.TryGetBlock(controlFlowBlock, out var block) || block is null)
                        continue;

                    var rewrittenPhis = RewritePhis(block);
                    var rewrittenInstructions = RewriteInstructions(block);
                    blocksByControlFlowBlock[controlFlowBlock] = new SsaBlock(controlFlowBlock, rewrittenPhis, rewrittenInstructions);
                }

                var blocks = ImmutableArray.CreateBuilder<SsaBlock>();
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

                return new SsaFunction(
                    _function.ControlFlowFunction,
                    _function.MemoryVariable,
                    _function.Variables,
                    blockArray,
                    definitions,
                    _uses.ToImmutableArray(),
                    _function.Problems,
                    CreateUndefinedNameMap(),
                    _valueNumberingOptions);
            }

            private ImmutableArray<SsaDefinition> RewriteDefinitionList()
            {
                var definitions = ImmutableArray.CreateBuilder<SsaDefinition>(_function.Definitions.Length);
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

            private void RebuildUsesFromBlocks(ImmutableArray<SsaBlock> blocks)
            {
                _uses.Clear();
                foreach (var block in blocks)
                {
                    foreach (var instruction in block.Instructions)
                    {
                        foreach (var use in instruction.Uses)
                            _uses.Add(use);
                    }
                }
            }

            private ImmutableArray<SsaBlock> EliminateDeadCode(ImmutableArray<SsaBlock> blocks)
            {
                var instructionByDefinition = new Dictionary<SsaName, SsaInstruction>();
                var phiByResult = new Dictionary<SsaName, SsaPhi>();

                foreach (var block in blocks)
                {
                    foreach (var phi in block.Phis)
                        phiByResult[phi.Result] = phi;

                    foreach (var instruction in block.Instructions)
                    {
                        foreach (var definition in instruction.Definitions)
                            instructionByDefinition[definition.Name] = instruction;
                    }
                }

                var liveInstructions = new HashSet<SsaInstruction>();
                var livePhis = new HashSet<SsaPhi>();
                var workList = new Queue<SsaName>();

                foreach (var block in blocks)
                {
                    foreach (var phi in block.Phis)
                    {
                        if (phi.Result.Variable.Kind == SsaVariableKind.Memory)
                            MarkPhiLive(phi, livePhis, workList);
                    }

                    foreach (var instruction in block.Instructions)
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

                var result = ImmutableArray.CreateBuilder<SsaBlock>(blocks.Length);
                foreach (var block in blocks)
                {
                    var phisChanged = false;
                    var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                    foreach (var phi in block.Phis)
                    {
                        if (phi.Result.Variable.Kind == SsaVariableKind.Memory || livePhis.Contains(phi))
                        {
                            phis.Add(phi);
                            continue;
                        }

                        RemovePhiDefinition(phi);
                        phisChanged = true;
                    }

                    var instructionsChanged = false;
                    var instructions = ImmutableArray.CreateBuilder<SsaInstruction>(block.Instructions.Length);
                    foreach (var instruction in block.Instructions)
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
                        result.Add(new SsaBlock(block.ControlFlowBlock, phis.ToImmutable(), instructions.ToImmutable()));
                    }
                    else
                    {
                        result.Add(block);
                    }
                }

                return result.ToImmutable();
            }

            private static bool IsRemovableInstruction(SsaInstruction instruction)
            {
                if (instruction.Statement.IsTerminator)
                    return false;

                if ((instruction.Flags & (SsaInstructionFlags.ReadsMemory | SsaInstructionFlags.WritesMemory | SsaInstructionFlags.ContainsCall)) != 0)
                    return false;

                if (instruction.Definitions.Length == 0 && instruction.Statement is not GimpleExpressionStatement)
                    return false;

                if (instruction.MemoryInput is not null || instruction.MemoryOutput is not null)
                    return false;

                foreach (var definition in instruction.Definitions)
                {
                    if (definition.Name.Variable.Kind == SsaVariableKind.Memory)
                        return false;
                }

                return true;
            }

            private static void MarkInstructionLive(
                SsaInstruction instruction,
                HashSet<SsaInstruction> liveInstructions,
                Queue<SsaName> workList)
            {
                if (!liveInstructions.Add(instruction))
                    return;

                foreach (var use in instruction.Uses)
                {
                    if (use.Kind != SsaUseKind.Memory && use.Name.Variable.Kind != SsaVariableKind.Memory)
                        workList.Enqueue(use.Name);
                }
            }

            private static void MarkPhiLive(
                SsaPhi phi,
                HashSet<SsaPhi> livePhis,
                Queue<SsaName> workList)
            {
                if (!livePhis.Add(phi))
                    return;

                foreach (var operand in phi.Operands)
                {
                    if (operand.Value.Variable.Kind != SsaVariableKind.Memory)
                        workList.Enqueue(operand.Value);
                }
            }

            private void RemoveInstructionDefinitions(SsaInstruction instruction)
            {
                foreach (var definition in instruction.Definitions)
                    _removedDefinitions.Add(definition);
            }

            private void RemovePhiDefinition(SsaPhi phi)
            {
                if (_function.TryGetDefinition(phi.Result, out var definition) && definition is not null)
                    _removedDefinitions.Add(definition);
            }

            private static ImmutableArray<SsaExpression> PruneExpressionsForStatement(
                GimpleStatement statement,
                ImmutableArray<SsaExpression> expressions)
            {
                return statement switch
                {
                    GimpleGotoStatement => ImmutableArray<SsaExpression>.Empty,
                    GimpleReturnStatement { Expression: null } => ImmutableArray<SsaExpression>.Empty,
                    _ => expressions,
                };
            }

            private Dictionary<SsaVariable, SsaName> CreateUndefinedNameMap()
            {
                var result = new Dictionary<SsaVariable, SsaName>();
                foreach (var variable in _function.Variables)
                    result[variable] = _function.GetUndefinedName(variable);
                return result;
            }

            private ImmutableArray<SsaPhi> RewritePhis(SsaBlock block)
            {
                if (block.Phis.Length == 0)
                    return ImmutableArray<SsaPhi>.Empty;

                var phis = ImmutableArray.CreateBuilder<SsaPhi>(block.Phis.Length);
                foreach (var phi in block.Phis)
                {
                    var operands = ImmutableArray.CreateBuilder<SsaPhiOperand>(phi.Operands.Length);
                    var changed = false;
                    foreach (var operand in phi.Operands)
                    {
                        var value = _options.EnableCopyPropagation
                            ? ResolveCopyForBlock(operand.Value, operand.Predecessor)
                            : operand.Value;

                        if (!ReferenceEquals(value, operand.Value))
                            changed = true;

                        operands.Add(new SsaPhiOperand(operand.Predecessor, value));
                    }

                    var rewritten = changed
                        ? new SsaPhi(phi.Ordinal, phi.Block, phi.Variable, phi.Result, operands.ToImmutable())
                        : phi;

                    if (changed)
                        Changed = true;

                    phis.Add(rewritten);

                    if (_options.EnableCopyPropagation && TryGetTrivialPhiCopy(rewritten, out var copy))
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

            private ImmutableArray<SsaInstruction> RewriteInstructions(SsaBlock block)
            {
                if (block.Instructions.Length == 0)
                    return ImmutableArray<SsaInstruction>.Empty;

                var instructions = ImmutableArray.CreateBuilder<SsaInstruction>(block.Instructions.Length);
                foreach (var instruction in block.Instructions)
                {
                    var rewritten = RewriteInstruction(instruction);
                    instructions.Add(rewritten);
                    AnalyzeInstructionDefinition(rewritten);
                }

                return instructions.ToImmutable();
            }

            private SsaInstruction RewriteInstruction(SsaInstruction instruction)
            {
                var expressions = ImmutableArray.CreateBuilder<SsaExpression>(instruction.Expressions.Length);
                foreach (var expression in instruction.Expressions)
                    expressions.Add(RewriteExpression(expression, instruction.Block, instruction.Statement));

                var newStatement = MaterializeStatement(instruction.Statement, instruction, expressions.ToImmutable());
                var expressionArray = PruneExpressionsForStatement(newStatement, expressions.ToImmutable());
                var definitions = ImmutableArray.CreateBuilder<SsaDefinition>(instruction.Definitions.Length);
                foreach (var definition in instruction.Definitions)
                {
                    var rewrittenDefinition = ReferenceEquals(newStatement, definition.Statement)
                        ? definition
                        : new SsaDefinition(
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
                    flags |= SsaInstructionFlags.WritesMemory;
                if ((instruction.Flags & SsaInstructionFlags.ContainsCall) != 0)
                    flags |= SsaInstructionFlags.ContainsCall;

                var uses = ImmutableArray.CreateBuilder<SsaUse>();
                CollectUses(expressionArray, instruction.Block, newStatement, uses);

                var memoryInput = instruction.MemoryInput;
                if (memoryInput is not null && (flags & (SsaInstructionFlags.ReadsMemory | SsaInstructionFlags.WritesMemory)) != 0)
                {
                    var memoryUse = new SsaUse(memoryInput, SsaUseKind.Memory, instruction.Block, newStatement, value: null);
                    uses.Add(memoryUse);
                    _uses.Add(memoryUse);
                }
                else if (memoryInput is not null)
                {
                    Changed = true;
                    memoryInput = null;
                }

                var memoryOutput = (flags & SsaInstructionFlags.WritesMemory) != 0 ? instruction.MemoryOutput : null;
                if (!ReferenceEquals(memoryOutput, instruction.MemoryOutput))
                    Changed = true;

                var rewritten = new SsaInstruction(
                    instruction.Ordinal,
                    instruction.Block,
                    newStatement,
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
                    !SameExpressions(instruction.Expressions, expressionArray))
                {
                    Changed = true;
                }

                return rewritten;
            }

            private SsaExpression RewriteExpression(
                SsaExpression expression,
                ControlFlowBlock useBlock,
                GimpleStatement statement)
            {
                if (expression.Name is not null)
                {
                    if (expression.IsAddress)
                        return expression;

                    var name = expression.Name;
                    if (_options.EnableCopyPropagation)
                        name = ResolveCopyForBlock(name, useBlock);

                    if (_options.EnableConstantFolding && _constants.TryGetValue(name, out var constant))
                    {
                        Changed = true;
                        return CreateConstantExpression(CloneConstant(constant, expression.Original.Syntax));
                    }

                    if (_options.EnableCopyPropagation && TryGetValueNumberRepresentative(name, useBlock, out var representative))
                        name = representative;

                    if (!ReferenceEquals(name, expression.Name))
                    {
                        Changed = true;
                        return CreateNameExpression(MaterializeNameValue(name, expression.Original), name, expression.Role);
                    }

                    return expression;
                }

                if (expression.Children.Length == 0)
                    return expression;

                var children = ImmutableArray.CreateBuilder<SsaExpression>(expression.Children.Length);
                var changed = false;
                foreach (var child in expression.Children)
                {
                    var rewrittenChild = RewriteExpression(child, useBlock, statement);
                    if (!ReferenceEquals(rewrittenChild, child))
                        changed = true;
                    children.Add(rewrittenChild);
                }

                var childArray = children.ToImmutable();
                if (_options.EnableConstantFolding &&
                    !expression.ContainsCall &&
                    !expression.WritesMemory &&
                    TryFoldExpression(expression, childArray, out var folded))
                {
                    Changed = true;
                    return folded;
                }

                if (!changed)
                    return expression;

                Changed = true;
                return CreateCompositeExpression(expression.Original, childArray, expression.Role);
            }

            private GimpleStatement MaterializeStatement(
                GimpleStatement statement,
                SsaInstruction instruction,
                ImmutableArray<SsaExpression> expressions)
            {
                switch (statement)
                {
                    case GimpleAssignmentStatement assignment:
                        if (TryGetAssignmentValueExpression(instruction, expressions, out var valueExpression) &&
                            TryMaterializeValue(valueExpression, out var value))
                        {
                            return ReferenceEquals(value, assignment.Value)
                                ? statement
                                : new GimpleAssignmentStatement(assignment.Target, value, statement.Syntax);
                        }
                        break;

                    case GimpleExpressionStatement expressionStatement:
                        if (expressions.Length >= 1 && TryMaterializeValue(expressions[0], out var expression))
                        {
                            return ReferenceEquals(expression, expressionStatement.Expression)
                                ? statement
                                : new GimpleExpressionStatement(expression, statement.Syntax);
                        }
                        break;

                    case GimpleConditionalGotoStatement conditional:
                        if (_options.EnableBranchFolding && ReferenceEquals(conditional.WhenTrue, conditional.WhenFalse))
                            return new GimpleGotoStatement(conditional.WhenTrue, statement.Syntax);

                        if (expressions.Length >= 1 && TryMaterializeValue(expressions[0], out var condition))
                        {
                            if (_options.EnableBranchFolding &&
                                TryGetConstant(expressions[0], out var conditionConstant) &&
                                TryGetBranchTruth(conditionConstant, out var truth))
                            {
                                return new GimpleGotoStatement(truth ? conditional.WhenTrue : conditional.WhenFalse, statement.Syntax);
                            }

                            return ReferenceEquals(condition, conditional.Condition)
                                ? statement
                                : new GimpleConditionalGotoStatement(condition, conditional.WhenTrue, conditional.WhenFalse, statement.Syntax);
                        }
                        break;

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

            private void AnalyzeInstructionDefinition(SsaInstruction instruction)
            {
                var definition = GetPrimaryDefinition(instruction);
                if (definition is null)
                    return;

                if (instruction.Statement is GimpleZeroInitializeStatement && IsScalarZeroFoldable(definition.Name.Type))
                {
                    AddConstant(definition.Name, CreateZeroConstant(definition.Name.Type, instruction.Statement.Syntax));
                    return;
                }

                if ((instruction.Flags & (SsaInstructionFlags.WritesMemory | SsaInstructionFlags.ContainsCall)) != 0)
                    return;

                if (instruction.Statement is not GimpleAssignmentStatement)
                {
                    if (_options.EnableCopyPropagation)
                        TryAddValueNumberCopy(definition.Name, instruction.Block);
                    return;
                }

                if (!TryGetAssignmentValueExpression(instruction, instruction.Expressions, out var valueExpression))
                    return;

                if (_options.EnableConstantFolding && TryGetConstant(valueExpression, out var constant))
                {
                    AddConstant(definition.Name, CloneConstant(constant, instruction.Statement.Syntax));
                    return;
                }

                if (_options.EnableCopyPropagation &&
                    valueExpression.Name is not null &&
                    !valueExpression.Name.IsUndefined &&
                    valueExpression.Name.Variable.Kind != SsaVariableKind.Memory &&
                    SameType(valueExpression.Name.Type, definition.Name.Type))
                {
                    AddCopy(definition.Name, valueExpression.Name);
                    return;
                }

                if (_options.EnableCopyPropagation)
                    TryAddValueNumberCopy(definition.Name, instruction.Block);
            }

            private void TryAddValueNumberCopy(SsaName name, ControlFlowBlock useBlock)
            {
                if (!_options.EnableCopyPropagation)
                    return;

                if (name.IsUndefined || name.Variable.Kind == SsaVariableKind.Memory)
                    return;

                if (!_function.ValueNumbering.TryGetValueNumber(name, out var valueNumber) || valueNumber is null)
                    return;

                if (!CanUseValueNumberForCopy(valueNumber))
                    return;

                if (TryGetValueNumberRepresentative(valueNumber, name.Type, useBlock, out var representative) &&
                    !ReferenceEquals(representative, name))
                {
                    AddCopy(name, representative);
                    return;
                }

                if (!_representativesByValueNumber.TryGetValue(valueNumber.Id, out var list))
                {
                    list = new List<SsaName>();
                    _representativesByValueNumber.Add(valueNumber.Id, list);
                }

                list.Add(name);
            }

            private bool TryGetValueNumberRepresentative(SsaName name, ControlFlowBlock useBlock, out SsaName representative)
            {
                representative = null!;

                if (!_function.ValueNumbering.TryGetValueNumber(name, out var valueNumber) || valueNumber is null)
                    return false;

                if (!CanUseValueNumberForCopy(valueNumber))
                    return false;

                return TryGetValueNumberRepresentative(valueNumber, name.Type, useBlock, out representative);
            }

            private bool TryGetValueNumberRepresentative(
                ValueNumber valueNumber,
                QualifiedType type,
                ControlFlowBlock useBlock,
                out SsaName representative)
            {
                representative = null!;

                if (!_representativesByValueNumber.TryGetValue(valueNumber.Id, out var candidates))
                    return false;

                for (var i = 0; i < candidates.Count; i++)
                {
                    var candidate = ResolveCopyForBlock(candidates[i], useBlock);
                    if (candidate.IsUndefined || candidate.Variable.Kind == SsaVariableKind.Memory)
                        continue;

                    if (!SameType(candidate.Type, type))
                        continue;

                    if (CanUseNameAtBlock(candidate, useBlock))
                    {
                        representative = candidate;
                        return true;
                    }
                }

                return false;
            }

            private static bool CanUseValueNumberForCopy(ValueNumber valueNumber)
            {
                if (valueNumber.IsUnique || valueNumber.IsMemoryDependent)
                    return false;

                return valueNumber.Kind is ValueNumberKind.Entry or ValueNumberKind.Constant or ValueNumberKind.Expression or ValueNumberKind.Phi;
            }

            private SsaName ResolveCopyForBlock(SsaName name, ControlFlowBlock useBlock)
            {
                var current = name;
                var seen = new HashSet<SsaName>();

                while (_copies.TryGetValue(current, out var next))
                {
                    if (!seen.Add(current) || !CanUseNameAtBlock(next, useBlock))
                        break;

                    current = next;
                }

                return current;
            }

            private void AddCopy(SsaName destination, SsaName source)
            {
                if (destination.IsUndefined || source.IsUndefined)
                    return;
                if (destination.Variable.Kind == SsaVariableKind.Memory || source.Variable.Kind == SsaVariableKind.Memory)
                    return;
                if (!SameType(destination.Type, source.Type))
                    return;
                if (ReferenceEquals(destination, source))
                    return;

                _copies[destination] = source;
            }

            private void AddConstant(SsaName destination, GimpleConstantValue constant)
            {
                if (destination.IsUndefined || destination.Variable.Kind == SsaVariableKind.Memory)
                    return;
                if (!SameType(destination.Type, constant.Type))
                    return;

                _constants[destination] = constant;
            }

            private bool CanUseNameAtBlock(SsaName name, ControlFlowBlock useBlock)
            {
                if (!_function.TryGetDefinition(name, out var definition) || definition is null)
                    return false;

                if (definition.Kind == SsaDefinitionKind.Undefined)
                    return false;

                if (definition.Block is null)
                    return false;

                return definition.Block.Dominates(useBlock);
            }

            private static bool TryGetTrivialPhiCopy(SsaPhi phi, out SsaName copy)
            {
                copy = null!;
                if (phi.Operands.Length == 0)
                    return false;

                var first = phi.Operands[0].Value;
                if (first.IsUndefined || first.Variable.Kind == SsaVariableKind.Memory)
                    return false;

                for (var i = 1; i < phi.Operands.Length; i++)
                {
                    if (!ReferenceEquals(first, phi.Operands[i].Value))
                        return false;
                }

                copy = first;
                return true;
            }

            private static SsaDefinition? GetPrimaryDefinition(SsaInstruction instruction)
            {
                foreach (var definition in instruction.Definitions)
                {
                    if (definition.Name.Variable.Kind != SsaVariableKind.Memory)
                        return definition;
                }

                return null;
            }

            private static bool TryGetAssignmentValueExpression(
                SsaInstruction instruction,
                ImmutableArray<SsaExpression> expressions,
                out SsaExpression expression)
            {
                if (GetPrimaryDefinition(instruction) is not null && expressions.Length >= 1)
                {
                    expression = expressions[0];
                    return true;
                }

                if (GetPrimaryDefinition(instruction) is null && expressions.Length >= 2)
                {
                    expression = expressions[1];
                    return true;
                }

                expression = null!;
                return false;
            }

            private static void CollectExpressionUses(
                SsaExpression expression,
                ControlFlowBlock block,
                GimpleStatement statement,
                ImmutableArray<SsaUse>.Builder uses)
            {
                if (expression.Name is not null)
                {
                    var kind = expression.IsAddress ? SsaUseKind.Address : SsaUseKind.Value;
                    uses.Add(new SsaUse(expression.Name, kind, block, statement, expression.Original));
                    return;
                }

                foreach (var child in expression.Children)
                    CollectExpressionUses(child, block, statement, uses);
            }

            private void CollectUses(ImmutableArray<SsaExpression> expressions, ControlFlowBlock block, GimpleStatement statement, ImmutableArray<SsaUse>.Builder uses)
            {
                foreach (var expression in expressions)
                {
                    CollectExpressionUses(expression, block, statement, uses);
                }

                foreach (var use in uses)
                    _uses.Add(use);
            }

            private static SsaInstructionFlags TranslateFlags(ImmutableArray<SsaExpression> expressions)
            {
                var flags = SsaInstructionFlags.None;
                foreach (var expression in expressions)
                {
                    if (expression.ReadsMemory)
                        flags |= SsaInstructionFlags.ReadsMemory;
                    if (expression.WritesMemory)
                        flags |= SsaInstructionFlags.WritesMemory;
                    if (expression.ContainsCall)
                        flags |= SsaInstructionFlags.ContainsCall;
                }

                return flags;
            }

            private static bool SameExpressions(ImmutableArray<SsaExpression> left, ImmutableArray<SsaExpression> right)
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

            private static SsaExpression CreateNameExpression(GimpleValue original, SsaName name, SsaExpressionRole role)
                => new SsaExpression(original, name, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false, role);

            private static SsaExpression CreateConstantExpression(GimpleConstantValue constant)
                => new SsaExpression(constant, name: null, ImmutableArray<SsaExpression>.Empty, readsMemory: false, writesMemory: false, containsCall: false);

            private static SsaExpression CreateCompositeExpression(GimpleValue original, ImmutableArray<SsaExpression> children, SsaExpressionRole role)
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
                        if (role == SsaExpressionRole.Value)
                            readsMemory = true;
                        break;

                    case GimpleCallExpression:
                        readsMemory = true;
                        writesMemory = true;
                        containsCall = true;
                        break;
                }

                return new SsaExpression(original, name: null, children, readsMemory, writesMemory, containsCall, role);
            }

            private static GimpleValue MaterializeNameValue(SsaName name, GimpleValue fallback)
            {
                if (name.Variable.Symbol is TypedSymbol typedSymbol)
                    return new GimpleSymbolValue(typedSymbol, name.Type, fallback.Syntax);

                if (name.Variable.Temporary is not null)
                    return name.Variable.Temporary;

                return fallback;
            }

            private static bool TryMaterializeValue(SsaExpression expression, out GimpleValue value)
            {
                if (expression.Name is not null && !expression.IsAddress)
                {
                    value = MaterializeNameValue(expression.Name, expression.Original);
                    return true;
                }

                if (expression.Original is GimpleConstantValue constant && expression.Children.Length == 0)
                {
                    value = constant;
                    return true;
                }

                if (expression.Children.Length == 0)
                {
                    value = expression.Original;
                    return false;
                }

                var children = ImmutableArray.CreateBuilder<GimpleValue>(expression.Children.Length);
                foreach (var child in expression.Children)
                {
                    if (!TryMaterializeValue(child, out var childValue))
                    {
                        value = expression.Original;
                        return false;
                    }

                    children.Add(childValue);
                }

                switch (expression.Original)
                {
                    case GimpleUnaryExpression unary when children.Count == 1:
                        value = new GimpleUnaryExpression(unary.OperatorToken, children[0], unary.Type, unary.Syntax);
                        return true;

                    case GimpleBinaryExpression binary when children.Count == 2:
                        value = new GimpleBinaryExpression(children[0], binary.OperatorToken, children[1], binary.Type, binary.Syntax);
                        return true;

                    case GimpleConversionExpression conversion when children.Count == 1:
                        value = new GimpleConversionExpression(children[0], conversion.Type, conversion.ConversionKind, conversion.Syntax);
                        return true;

                    case GimpleCastExpression cast when children.Count == 1:
                        value = new GimpleCastExpression(children[0], cast.Type, cast.Syntax);
                        return true;

                    case GimpleCallExpression call when children.Count == call.Arguments.Length + 1:
                        value = new GimpleCallExpression(
                            children[0],
                            children.Skip(1).ToImmutableArray(),
                            call.FunctionType,
                            call.Type,
                            call.Syntax);
                        return true;
                }

                value = expression.Original;
                return false;
            }

            private bool TryFoldExpression(
                SsaExpression expression,
                ImmutableArray<SsaExpression> children,
                out SsaExpression folded)
            {
                switch (expression.Original)
                {
                    case GimpleUnaryExpression unary when children.Length == 1:
                        return TryFoldUnary(unary, children[0], out folded);

                    case GimpleBinaryExpression binary when children.Length == 2:
                        return TryFoldBinary(binary, children[0], children[1], out folded);

                    case GimpleConversionExpression conversion when children.Length == 1:
                        return TryFoldConversion(conversion, children[0], out folded);

                    case GimpleCastExpression cast when children.Length == 1:
                        return TryFoldCast(cast, children[0], out folded);
                }

                folded = null!;
                return false;
            }

            private bool TryFoldUnary(GimpleUnaryExpression unary, SsaExpression operand, out SsaExpression folded)
            {
                var kind = unary.OperatorToken.Kind;

                if (kind == SyntaxKind.PlusToken && SameType(operand.Original.Type, unary.Type))
                {
                    folded = operand;
                    return true;
                }

                if (!TryGetConstant(operand, out var constant))
                {
                    folded = null!;
                    return false;
                }

                if (kind == SyntaxKind.BangToken && TryGetIntegerConstant(constant, out var truthValue, out _))
                {
                    folded = CreateConstantExpression(CreateIntegerConstant(truthValue == 0 ? 1UL : 0UL, unary.Type, unary.Syntax));
                    return true;
                }

                if (!TryGetIntegerConstant(constant, out var value, out var info))
                {
                    folded = null!;
                    return false;
                }

                switch (kind)
                {
                    case SyntaxKind.PlusToken:
                        if (TryCastInteger(value, unary.Type, unary.Syntax, out var plus))
                        {
                            folded = CreateConstantExpression(plus);
                            return true;
                        }
                        break;

                    case SyntaxKind.MinusToken:
                        if (TryNegateInteger(value, info, unary.Type, unary.Syntax, out var negated))
                        {
                            folded = CreateConstantExpression(negated);
                            return true;
                        }
                        break;

                    case SyntaxKind.TildeToken:
                        if (TryCastInteger(~value, unary.Type, unary.Syntax, out var complemented))
                        {
                            folded = CreateConstantExpression(complemented);
                            return true;
                        }
                        break;
                }

                folded = null!;
                return false;
            }

            private bool TryFoldBinary(GimpleBinaryExpression binary, SsaExpression left, SsaExpression right, out SsaExpression folded)
            {
                if (TryFoldBinaryConstants(binary, left, right, out folded))
                    return true;

                if (TryFoldBinaryIdentity(binary, left, right, out folded))
                    return true;

                if (TryFoldSameNameComparison(binary, left, right, out folded))
                    return true;

                folded = null!;
                return false;
            }

            private bool TryFoldBinaryConstants(GimpleBinaryExpression binary, SsaExpression left, SsaExpression right, out SsaExpression folded)
            {
                folded = null!;
                if (!TryGetConstant(left, out var leftConstant) || !TryGetConstant(right, out var rightConstant))
                    return false;

                var kind = binary.OperatorToken.Kind;

                if ((kind is SyntaxKind.AmpersandAmpersandToken or SyntaxKind.PipePipeToken) &&
                    TryGetIntegerConstant(leftConstant, out var leftTruth, out _) &&
                    TryGetIntegerConstant(rightConstant, out var rightTruth, out _))
                {
                    var result = kind == SyntaxKind.AmpersandAmpersandToken
                        ? (leftTruth != 0 && rightTruth != 0)
                        : (leftTruth != 0 || rightTruth != 0);
                    folded = CreateConstantExpression(CreateIntegerConstant(result ? 1UL : 0UL, binary.Type, binary.Syntax));
                    return true;
                }

                if (!TryGetIntegerConstant(leftConstant, out var leftValue, out var leftInfo) ||
                    !TryGetIntegerConstant(rightConstant, out var rightValue, out var rightInfo))
                {
                    return false;
                }

                if (!SameType(leftConstant.Type, rightConstant.Type))
                    return false;

                if (kind is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or
                    SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or
                    SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken)
                {
                    if (TryCompareIntegers(leftValue, rightValue, leftInfo, kind, out var comparison))
                    {
                        folded = CreateConstantExpression(CreateIntegerConstant(comparison ? 1UL : 0UL, binary.Type, binary.Syntax));
                        return true;
                    }

                    return false;
                }

                if (!SameType(leftConstant.Type, binary.Type))
                    return false;

                {
                    if (TryEvaluateIntegerBinary(leftValue, rightValue, leftInfo, kind, binary.Type, binary.Syntax, out var result))
                    {
                        folded = CreateConstantExpression(result);
                        return true;
                    }
                }
                

                return false;
            }

            private bool TryFoldBinaryIdentity(GimpleBinaryExpression binary, SsaExpression left, SsaExpression right, out SsaExpression folded)
            {
                folded = null!;
                var kind = binary.OperatorToken.Kind;

                if (IsZero(right) && SameType(left.Original.Type, binary.Type))
                {
                    switch (kind)
                    {
                        case SyntaxKind.PlusToken:
                        case SyntaxKind.MinusToken:
                        case SyntaxKind.PipeToken:
                        case SyntaxKind.HatToken:
                            folded = left;
                            return true;
                    }
                }

                if (IsZero(left) && SameType(right.Original.Type, binary.Type))
                {
                    switch (kind)
                    {
                        case SyntaxKind.PlusToken:
                        case SyntaxKind.PipeToken:
                        case SyntaxKind.HatToken:
                            folded = right;
                            return true;
                    }
                }

                if (IsOne(right) && SameType(left.Original.Type, binary.Type))
                {
                    switch (kind)
                    {
                        case SyntaxKind.StarToken:
                        case SyntaxKind.SlashToken:
                            folded = left;
                            return true;
                    }
                }

                if (IsOne(left) && SameType(right.Original.Type, binary.Type) && kind == SyntaxKind.StarToken)
                {
                    folded = right;
                    return true;
                }

                return false;
            }

            private bool TryFoldSameNameComparison(GimpleBinaryExpression binary, SsaExpression left, SsaExpression right, out SsaExpression folded)
            {
                folded = null!;
                if (left.Name is null || right.Name is null || !ReferenceEquals(left.Name, right.Name))
                    return false;

                if (!IsIntegerLike(left.Name.Type) && !IsPointerLike(left.Name.Type))
                    return false;

                switch (binary.OperatorToken.Kind)
                {
                    case SyntaxKind.EqualsEqualsToken:
                        folded = CreateConstantExpression(CreateIntegerConstant(1UL, binary.Type, binary.Syntax));
                        return true;

                    case SyntaxKind.BangEqualsToken:
                        folded = CreateConstantExpression(CreateIntegerConstant(0UL, binary.Type, binary.Syntax));
                        return true;
                }

                return false;
            }

            private bool TryFoldConversion(GimpleConversionExpression conversion, SsaExpression operand, out SsaExpression folded)
            {
                if (conversion.ConversionKind == GimpleConversionKind.Identity && SameType(operand.Original.Type, conversion.Type))
                {
                    folded = operand;
                    return true;
                }

                if (TryGetConstant(operand, out var constant) && TryCastIntegerConstant(constant, conversion.Type, conversion.Syntax, out var casted))
                {
                    folded = CreateConstantExpression(casted);
                    return true;
                }

                folded = null!;
                return false;
            }

            private bool TryFoldCast(GimpleCastExpression cast, SsaExpression operand, out SsaExpression folded)
            {
                if (TryGetConstant(operand, out var constant) && TryCastIntegerConstant(constant, cast.Type, cast.Syntax, out var casted))
                {
                    folded = CreateConstantExpression(casted);
                    return true;
                }

                folded = null!;
                return false;
            }

            private bool TryEvaluateIntegerBinary(
                ulong left,
                ulong right,
                IntegerInfo info,
                SyntaxKind kind,
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
                            switch (kind)
                            {
                                case SyntaxKind.PlusToken:
                                    signedResult = leftSigned + rightSigned;
                                    break;
                                case SyntaxKind.MinusToken:
                                    signedResult = leftSigned - rightSigned;
                                    break;
                                case SyntaxKind.StarToken:
                                    signedResult = leftSigned * rightSigned;
                                    break;
                                case SyntaxKind.SlashToken:
                                    if (rightSigned == 0 || (leftSigned == MinSigned(info.Bits) && rightSigned == -1))
                                        return false;
                                    signedResult = leftSigned / rightSigned;
                                    break;
                                case SyntaxKind.PercentToken:
                                    if (rightSigned == 0 || (leftSigned == MinSigned(info.Bits) && rightSigned == -1))
                                        return false;
                                    signedResult = leftSigned % rightSigned;
                                    break;
                                case SyntaxKind.AmpersandToken:
                                    result = CreateIntegerConstant(left & right, resultType, syntax);
                                    return true;
                                case SyntaxKind.PipeToken:
                                    result = CreateIntegerConstant(left | right, resultType, syntax);
                                    return true;
                                case SyntaxKind.HatToken:
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
                switch (kind)
                {
                    case SyntaxKind.PlusToken:
                        unsignedResult = (left + right) & mask;
                        break;
                    case SyntaxKind.MinusToken:
                        unsignedResult = (left - right) & mask;
                        break;
                    case SyntaxKind.StarToken:
                        unsignedResult = (left * right) & mask;
                        break;
                    case SyntaxKind.SlashToken:
                        if (right == 0)
                            return false;
                        unsignedResult = left / right;
                        break;
                    case SyntaxKind.PercentToken:
                        if (right == 0)
                            return false;
                        unsignedResult = left % right;
                        break;
                    case SyntaxKind.AmpersandToken:
                        unsignedResult = left & right;
                        break;
                    case SyntaxKind.PipeToken:
                        unsignedResult = left | right;
                        break;
                    case SyntaxKind.HatToken:
                        unsignedResult = left ^ right;
                        break;
                    default:
                        return false;
                }

                result = CreateIntegerConstant(unsignedResult, resultType, syntax);
                return true;
            }

            private bool TryCompareIntegers(ulong left, ulong right, IntegerInfo info, SyntaxKind kind, out bool result)
            {
                if (info.IsSigned)
                {
                    var leftSigned = ToSigned(left, info.Bits);
                    var rightSigned = ToSigned(right, info.Bits);
                    result = kind switch
                    {
                        SyntaxKind.EqualsEqualsToken => leftSigned == rightSigned,
                        SyntaxKind.BangEqualsToken => leftSigned != rightSigned,
                        SyntaxKind.LessThanToken => leftSigned < rightSigned,
                        SyntaxKind.LessThanEqualsToken => leftSigned <= rightSigned,
                        SyntaxKind.GreaterThanToken => leftSigned > rightSigned,
                        SyntaxKind.GreaterThanEqualsToken => leftSigned >= rightSigned,
                        _ => false,
                    };
                    return kind is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or
                        SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or
                        SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken;
                }

                result = kind switch
                {
                    SyntaxKind.EqualsEqualsToken => left == right,
                    SyntaxKind.BangEqualsToken => left != right,
                    SyntaxKind.LessThanToken => left < right,
                    SyntaxKind.LessThanEqualsToken => left <= right,
                    SyntaxKind.GreaterThanToken => left > right,
                    SyntaxKind.GreaterThanEqualsToken => left >= right,
                    _ => false,
                };
                return kind is SyntaxKind.EqualsEqualsToken or SyntaxKind.BangEqualsToken or
                    SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or
                    SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken;
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
                if (!TryGetIntegerConstant(constant, out var value, out _))
                    return false;

                return TryCastInteger(value, targetType, syntax, out casted);
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

            private static bool TryGetConstant(SsaExpression expression, out GimpleConstantValue constant)
            {
                if (expression.Name is null && expression.Children.Length == 0 && expression.Original is GimpleConstantValue value)
                {
                    constant = value;
                    return true;
                }

                constant = null!;
                return false;
            }

            private bool IsZero(SsaExpression expression)
                => TryGetConstant(expression, out var constant) && TryGetIntegerConstant(constant, out var value, out _) && value == 0;

            private bool IsOne(SsaExpression expression)
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
