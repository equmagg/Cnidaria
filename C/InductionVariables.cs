using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C;

internal static class InductionVariables
{
    private const int MaxPointerWalkGroups = 4;
    private const int MaxIndexedGroups = 2;

    public static GimpleFunctionAnnotations Optimize(GimpleFunctionAnnotations function, TargetInfo target,
        SsaOptimizationOptions options, ValueNumberingOptions valueNumberingOptions, AssertionTable? assertions = null)
    {
        if (!options.EnableInductionVariables || options.MaxLoopAnalysisWork <= 0 || function.Problems.Length != 0)
            return function;
        var analysis = new NaturalLoopAnalysis(function.ControlFlowFunction, options.MaxLoopAnalysisWork);
        if (analysis.Remaining <= 0 || analysis.Loops.Count == 0)
            return function;
        return new Rewriter(function, target, valueNumberingOptions, analysis, assertions).Run();
    }

    private sealed class Shape
    {
        public NaturalLoop Loop = null!;
        public ControlFlowBlock Preheader = null!;
        public ControlFlowBlock Latch = null!;
        public ControlFlowEdge EntryEdge = null!;
        public ControlFlowEdge BackEdge = null!;
        public GimpleStatementAnnotations? ExitTest;
        public bool ExitWhenTrue;
    }

    private sealed class Access
    {
        public GimpleStatementAnnotations Instruction = null!;
        public GimpleOperandInfo Node = null!;
        public GimpleElementAccessExpression Original = null!;
        public Scev Index = null!;
        public Scev? StartRest;
        public long StartOffset;
        public bool NarrowIndex;
    }

    private sealed class Group
    {
        public readonly List<Access> Members = new();
        public Access First => Members[0];
        public GimpleName? Start;
        public GimpleName? Pointer;
    }

    private sealed class Rewriter
    {
        private readonly GimpleFunctionAnnotations _function;
        private readonly TargetInfo _target;
        private readonly ValueNumberingOptions _valueNumberingOptions;
        private readonly NaturalLoopAnalysis _analysis;
        private readonly AssertionTable? _assertions;
        private readonly bool _pointerWalk;
        private readonly int _pointerBits;
        private readonly QualifiedType _indexType;
        private readonly Dictionary<GimpleName, GimpleStatementAnnotations> _definitions = new();
        private readonly Dictionary<GimpleStatement, GimpleStatementAnnotations> _instructions = new();
        private readonly Dictionary<ControlFlowBlock, GimpleBlockAnnotations> _blocks = new();
        private readonly Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> _replacements = new();
        private readonly Dictionary<ControlFlowBlock, List<GimpleStatementAnnotations>> _appended = new();
        private readonly Dictionary<ControlFlowBlock, List<GimplePhi>> _phis = new();
        private readonly Dictionary<GimpleDefinition, GimpleDefinition> _redefinitions = new();
        private readonly List<GimpleVariable> _variables = new();
        private readonly List<GimpleDefinition> _newDefinitions = new();
        private readonly Dictionary<GimpleVariable, GimpleName> _undefined = new();
        private int _nextVariable;
        private int _nextTemporary;
        private int _nextStatement;
        private int _nextPhi;

        public Rewriter(GimpleFunctionAnnotations function, TargetInfo target, ValueNumberingOptions valueNumberingOptions,
            NaturalLoopAnalysis analysis, AssertionTable? assertions)
        {
            _function = function;
            _assertions = assertions;
            _target = target;
            _valueNumberingOptions = valueNumberingOptions;
            _analysis = analysis;
            _pointerWalk = target.IsRiscV || target.IsArm;
            _pointerBits = target.PointerSize * 8;
            _indexType = TypeCatalog.Instance.Builtin(
                _pointerBits == 32 ? BuiltinTypeKind.Int
                : target.SizeOf(TypeCatalog.Instance.Builtin(BuiltinTypeKind.Long)) == 8 ? BuiltinTypeKind.Long
                : BuiltinTypeKind.LongLong);

            foreach (var variable in function.Variables)
            {
                _nextVariable = Math.Max(_nextVariable, variable.Ordinal + 1);
                if (variable.Temporary is not null)
                    _nextTemporary = Math.Max(_nextTemporary, variable.Temporary.Ordinal + 1);
            }
            foreach (var temporary in function.InputFunction.Temporaries)
                _nextTemporary = Math.Max(_nextTemporary, temporary.Ordinal + 1);
            foreach (var block in function.Blocks)
            {
                _blocks[block.ControlFlowBlock] = block;
                foreach (var phi in block.Phis)
                    _nextPhi = Math.Max(_nextPhi, phi.Ordinal + 1);
                foreach (var instruction in block.Statements)
                {
                    _nextStatement = Math.Max(_nextStatement, instruction.Ordinal + 1);
                    _instructions[instruction.Statement] = instruction;
                    foreach (var definition in instruction.Definitions)
                        _definitions[definition.Name] = instruction;
                }
            }
        }

        public GimpleFunctionAnnotations Run()
        {
            foreach (var loop in _analysis.Loops)
            {
                if (_analysis.Remaining <= 0)
                    break;
                if (IsInnermost(loop) && TryGetShape(loop, out var shape) && !TryDeleteDeadLoop(shape))
                    OptimizeLoop(shape);
            }

            return _replacements.Count == 0 && _phis.Count == 0 ? _function : Rebuild();
        }

        private bool IsInnermost(NaturalLoop loop)
        {
            foreach (var other in _analysis.Loops)
            {
                if (!ReferenceEquals(other, loop) && loop.Blocks.Contains(other.Header))
                    return false;
            }
            return true;
        }

        private bool TryGetShape(NaturalLoop loop, out Shape shape)
        {
            shape = new Shape { Loop = loop };
            if (loop.Preheader is not { } preheader || !_blocks.ContainsKey(preheader) || !_blocks.TryGetValue(loop.Header, out var header))
                return false;
            if (loop.Header.UniquePredecessors.Length != 2 || loop.Header.Predecessors.Length != 2)
                return false;
            foreach (var edge in loop.Header.Predecessors)
            {
                if (ReferenceEquals(edge.Source, preheader))
                    shape.EntryEdge = edge;
                else if (loop.Blocks.Contains(edge.Source))
                    shape.BackEdge = edge;
            }
            if (shape.EntryEdge is null || shape.BackEdge is null)
                return false;
            shape.Preheader = preheader;
            shape.Latch = shape.BackEdge.Source;

            if (Terminator(preheader) is not GimpleGotoStatement entry || !Targets(entry.Target, loop.Header))
                return false;
            switch (Terminator(shape.Latch))
            {
                case GimpleGotoStatement jump when Targets(jump.Target, loop.Header):
                case GimpleCondStatement branch when Targets(branch.WhenTrue, loop.Header) != Targets(branch.WhenFalse, loop.Header):
                    break;
                default:
                    return false;
            }

            foreach (var block in loop.Blocks)
            {
                if (!_blocks.TryGetValue(block, out var annotations))
                    return false;
                foreach (var instruction in annotations.Statements)
                {
                    if (instruction.Statement is GimpleAsmStatement)
                        return false;
                }
            }

            if (header.Statements.Length != 0 && header.Statements[^1] is { Statement: GimpleCondStatement test } exitTest)
            {
                var trueInside = TargetsLoop(test.WhenTrue, loop);
                if (trueInside != TargetsLoop(test.WhenFalse, loop))
                {
                    shape.ExitTest = exitTest;
                    shape.ExitWhenTrue = !trueInside;
                }
            }
            return true;
        }

        private GimpleStatement? Terminator(ControlFlowBlock block)
            => _blocks.TryGetValue(block, out var annotations) && annotations.Statements.Length != 0
                ? annotations.Statements[^1].Statement
                : null;

        private bool Targets(GimpleLabel label, ControlFlowBlock block)
            => _function.ControlFlowFunction.TryGetBlock(label, out var target) && ReferenceEquals(target, block);

        private bool TargetsLoop(GimpleLabel label, NaturalLoop loop)
            => _function.ControlFlowFunction.TryGetBlock(label, out var target) && target is not null && loop.Blocks.Contains(target);

        // Deciding the exit test leaves the body, the counter and their phis to dead code elimination
        private bool TryDeleteDeadLoop(Shape shape)
        {
            if (shape.ExitTest?.Statement is not GimpleCondStatement test ||
                !_function.ControlFlowFunction.TryGetBlock(shape.ExitWhenTrue ? test.WhenTrue : test.WhenFalse, out var exit) || exit is null)
                return false;
            foreach (var block in shape.Loop.Blocks)
            {
                foreach (var successor in block.UniqueSuccessors)
                {
                    if (!shape.Loop.Blocks.Contains(successor) && !ReferenceEquals(successor, exit))
                        return false;
                }
                foreach (var instruction in _blocks[block].Statements)
                {
                    if (!_analysis.Spend() || !IsSideEffectFree(instruction))
                        return false;
                }
            }
            foreach (var use in _function.Uses)
            {
                if (use.Kind != GimpleUseKind.Memory && use.Name.Definition?.Block is { } block &&
                    shape.Loop.Blocks.Contains(block) && !shape.Loop.Blocks.Contains(use.Block))
                    return false;
            }

            var evolution = new ScalarEvolution(shape.Loop, shape.Preheader, shape.Latch, _target, _definitions, test, shape.ExitWhenTrue);
            var ivOnLeft = IsRecurrence(test.Lhs, evolution) && IsInvariantOperand(test.Rhs, evolution);
            if (!ivOnLeft && !(IsRecurrence(test.Rhs, evolution) && IsInvariantOperand(test.Lhs, evolution)))
                return false;
            var recurrence = evolution.Analyze((GimpleName)(ivOnLeft ? test.Lhs : test.Rhs))!;
            if (!recurrence.Step.TryGetConstant(out var stride) || stride == 0)
                return false;
            var code = ivOnLeft ? test.Code : Swap(test.Code);
            var continueCode = shape.ExitWhenTrue ? ScalarEvolution.Invert(code) : code;
            var finite = recurrence.NoSignedWrap
                ? continueCode switch
                {
                    GimpleTreeCode.LtExpr or GimpleTreeCode.LeExpr => stride > 0,
                    GimpleTreeCode.GtExpr or GimpleTreeCode.GeExpr => stride < 0,
                    GimpleTreeCode.NeExpr => stride is 1 or -1,
                    _ => false,
                }
                : continueCode switch
                {
                    GimpleTreeCode.LtExpr => stride == 1,
                    GimpleTreeCode.GtExpr => stride == -1,
                    GimpleTreeCode.NeExpr => stride is 1 or -1,
                    _ => false,
                };
            if (!finite)
                return false;

            var jump = new GimpleGotoStatement(shape.ExitWhenTrue ? test.WhenTrue : test.WhenFalse, test.Syntax);
            _replacements[shape.ExitTest] = new GimpleStatementAnnotations(shape.ExitTest.Ordinal, shape.ExitTest.Block, jump, jump,
                ImmutableArray<GimpleOperandInfo>.Empty, ImmutableArray<GimpleUse>.Empty, ImmutableArray<GimpleDefinition>.Empty, null, null, GimpleStatementFlags.None);
            return true;
        }

        private static bool IsSideEffectFree(GimpleStatementAnnotations instruction)
        {
            switch (instruction.Statement)
            {
                case GimpleGotoStatement or GimpleCondStatement or GimpleDeclarationStatement or GimpleNopStatement:
                    return true;
                case GimpleAssignStatement:
                    if ((instruction.Flags & (GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) != 0 ||
                        instruction.MemoryInput is not null || instruction.MemoryOutput is not null)
                        return false;
                    foreach (var definition in instruction.Definitions)
                    {
                        if (definition.Name.Variable.Kind == GimpleVariableKind.Memory)
                            return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        private void OptimizeLoop(Shape shape)
        {
            var test = shape.ExitTest?.Statement as GimpleCondStatement;
            var evolution = new ScalarEvolution(shape.Loop, shape.Preheader, shape.Latch, _target, _definitions, test, shape.ExitWhenTrue);
            var accesses = new List<Access>();
            foreach (var block in shape.Loop.Blocks)
            {
                foreach (var instruction in _blocks[block].Statements)
                {
                    if (instruction.Statement is not (GimpleAssignStatement or GimpleCallStatement))
                        continue;
                    foreach (var operand in instruction.Operands)
                        CollectAccesses(instruction, operand, shape.Loop, evolution, accesses);
                }
            }
            if (accesses.Count == 0)
                return;

            var groups = new List<Group>();
            foreach (var access in accesses)
            {
                var group = groups.FirstOrDefault(candidate => SameGroup(candidate.First, access));
                if (group is null)
                {
                    group = new Group();
                    groups.Add(group);
                }
                group.Members.Add(access);
            }

            var replacements = new Dictionary<GimpleOperandInfo, GimpleOperandInfo>();
            var preheader = new List<GimpleStatementAnnotations>();
            var latch = new List<GimpleStatementAnnotations>();
            var reduced = new List<Group>();
            var widened = new Dictionary<long, GimpleName>();
            foreach (var group in groups.OrderByDescending(static group => group.Members.Count))
            {
                if (_pointerWalk ? reduced.Count < MaxPointerWalkGroups : !IsIndexable(group) && reduced.Count < MaxIndexedGroups)
                {
                    StrengthReduce(shape, group, preheader, latch, replacements);
                    reduced.Add(group);
                }
                else if (!_pointerWalk && _pointerBits == 64)
                {
                    Widen(shape, group, preheader, latch, replacements, widened);
                }
            }
            if (replacements.Count == 0)
                return;

            var rewritten = new Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations>();
            foreach (var access in accesses)
            {
                if (replacements.ContainsKey(access.Node) && !rewritten.ContainsKey(access.Instruction))
                    rewritten.Add(access.Instruction, Rewrite(access.Instruction, replacements));
            }

            var exit = RewriteExitTest(shape, evolution, reduced, widened, preheader, rewritten);
            foreach (var pair in rewritten)
                _replacements[pair.Key] = pair.Value;
            if (exit is not null)
                _replacements[shape.ExitTest!] = exit;
            Append(shape.Preheader, preheader);
            Append(shape.Latch, latch);
        }

        private void CollectAccesses(GimpleStatementAnnotations instruction, GimpleOperandInfo node, NaturalLoop loop,
            ScalarEvolution evolution, List<Access> accesses)
        {
            if (!_analysis.Spend())
                return;
            if (node.Original is GimpleElementAccessExpression { Index: not null } original && node.Children.Length == 2 &&
                TryDescribe(instruction, node, original, loop, evolution, out var access))
                accesses.Add(access);
            foreach (var child in node.Children)
                CollectAccesses(instruction, child, loop, evolution, accesses);
        }

        private bool TryDescribe(GimpleStatementAnnotations instruction, GimpleOperandInfo node, GimpleElementAccessExpression original,
            NaturalLoop loop, ScalarEvolution evolution, out Access access)
        {
            access = null!;
            var size = _target.SizeOf(original.Type);
            if (size <= 0 || GimpleTypes.IsVolatileOrAtomic(original.Type))
                return false;

            var baseNode = node.Children[0];
            var invariantBase = original.Expression.Type.Type is PointerType
                ? baseNode.Name is { } pointer && !baseNode.IsAddress && evolution.IsLoopInvariant(pointer)
                : IsInvariantPlace(baseNode, evolution);
            if (!invariantBase)
                return false;

            var indexNode = node.Children[1];
            if (indexNode.Name is not { } indexName || evolution.Analyze(indexName) is not { Kind: ScevKind.AddRecurrence } recurrence)
                return false;
            var indexBits = evolution.IntegerBits(indexName.Type);
            if (indexBits > _pointerBits || Scev.Extend(recurrence, _pointerBits, GimpleTypes.IsSigned(indexName.Type)) is not { Kind: ScevKind.AddRecurrence } index)
                return false;
            if (index.Step.TryGetConstant(out var step) && step == 0)
                return false;

            index.Start.SplitConstant(out var rest, out var offset);
            access = new Access
            {
                Instruction = instruction,
                Node = node,
                Original = original,
                Index = index,
                StartRest = rest,
                StartOffset = offset,
                NarrowIndex = indexBits < _pointerBits,
            };
            return true;
        }

        private static bool IsInvariantPlace(GimpleOperandInfo node, ScalarEvolution evolution)
        {
            if (node.Name is not null)
                return false;
            switch (node.Original)
            {
                case GimpleSymbolValue:
                case GimpleTemporaryValue:
                    return node.Children.Length == 0;
                case GimpleElementAccessExpression access when node.Children.Length == 2:
                    return IsInvariantValue(node.Children[1], evolution) &&
                        (access.Expression.Type.Type is PointerType
                            ? IsInvariantValue(node.Children[0], evolution)
                            : IsInvariantPlace(node.Children[0], evolution));
                case GimpleMemberAccessExpression member when node.Children.Length == 1:
                    return member.ThroughPointer ? IsInvariantValue(node.Children[0], evolution) : IsInvariantPlace(node.Children[0], evolution);
                case GimpleIndirectExpression when node.Children.Length == 1:
                    return IsInvariantValue(node.Children[0], evolution);
                default:
                    return false;
            }
        }

        private static bool IsInvariantValue(GimpleOperandInfo node, ScalarEvolution evolution)
            => node.Original is GimpleConstantValue && node.Children.Length == 0 ||
               node.Name is { } name && !node.IsAddress && evolution.IsLoopInvariant(name);

        private static bool SameGroup(Access left, Access right)
            => SameType(left.Original.Type, right.Original.Type) &&
               left.Index.Step.IsEquivalentTo(right.Index.Step) &&
               (left.StartRest is null ? right.StartRest is null : right.StartRest is not null && left.StartRest.IsEquivalentTo(right.StartRest)) &&
               SameOperand(left.Node.Children[0], right.Node.Children[0]);

        private static bool SameType(QualifiedType left, QualifiedType right)
            => ReferenceEquals(left.Type, right.Type) && left.Qualifiers == right.Qualifiers;

        private static bool SameOperand(GimpleOperandInfo left, GimpleOperandInfo right)
        {
            if (left.Name is not null || right.Name is not null)
                return ReferenceEquals(left.Name, right.Name) && left.Role == right.Role;
            if (left.Children.Length != right.Children.Length)
                return false;
            var same = (left.Original, right.Original) switch
            {
                (GimpleConstantValue a, GimpleConstantValue b) => Equals(a.Value, b.Value) && SameType(a.Type, b.Type),
                (GimpleSymbolValue a, GimpleSymbolValue b) => ReferenceEquals(a.Symbol, b.Symbol),
                (GimpleTemporaryValue a, GimpleTemporaryValue b) => a.Ordinal == b.Ordinal,
                (GimpleMemberAccessExpression a, GimpleMemberAccessExpression b) => ReferenceEquals(a.Field, b.Field) && a.ThroughPointer == b.ThroughPointer,
                (GimpleElementAccessExpression, GimpleElementAccessExpression) => true,
                (GimpleIndirectExpression, GimpleIndirectExpression) => true,
                _ => false,
            };
            if (!same)
                return false;
            for (var i = 0; i < left.Children.Length; i++)
            {
                if (!SameOperand(left.Children[i], right.Children[i]))
                    return false;
            }
            return true;
        }

        // x86 and the bytecode address base + index * 1, 2, 4 or 8 directly
        private bool IsIndexable(Group group)
        {
            var size = _target.SizeOf(group.First.Original.Type);
            return size is 1 or 2 or 4 or 8 && group.First.Index.Step.TryGetConstant(out _);
        }

        private void StrengthReduce(Shape shape, Group group, List<GimpleStatementAnnotations> preheader,
            List<GimpleStatementAnnotations> latch, Dictionary<GimpleOperandInfo, GimpleOperandInfo> replacements)
        {
            var first = group.First;
            var elementType = first.Original.Type;
            var pointerType = new QualifiedType(TypeCatalog.Instance.PointerTo(elementType));
            var baseValue = GimpleNameMaterializer.MaterializeValue(first.Node.Children[0]);
            var origin = first.StartOffset;
            var startIndex = first.StartRest is null
                ? Constant(origin)
                : Materialize(Scev.Add(first.StartRest, Scev.Constant(origin, _pointerBits), false)!, shape.Preheader, preheader);
            var start = EmitAddressOf(shape.Preheader, preheader, pointerType,
                new GimpleElementAccessExpression(baseValue, startIndex, elementType, first.Original.Syntax));
            var step = Materialize(first.Index.Step, shape.Preheader, preheader);

            var variable = NewVariable(pointerType);
            var current = NewName(variable);
            var next = NewName(variable);
            AddPhi(shape, variable, current, start, next);
            if (step is GimpleConstantValue)
            {
                latch.Add(NewAssign(shape.Latch, next, GimpleTreeCode.PointerPlusExpr, current, step));
            }
            else
            {
                var size = _target.SizeOf(elementType);
                var bytes = size == 1 ? step : Emit(shape.Preheader, preheader, GimpleTreeCode.MultExpr, _indexType, step, Constant(size));
                var bytePointer = new QualifiedType(TypeCatalog.Instance.PointerTo(TypeCatalog.Instance.Builtin(BuiltinTypeKind.UnsignedChar)));
                var raw = Emit(shape.Latch, latch, GimpleTreeCode.NopExpr, bytePointer, current);
                var advanced = Emit(shape.Latch, latch, GimpleTreeCode.PointerPlusExpr, bytePointer, raw, bytes);
                latch.Add(NewAssign(shape.Latch, next, GimpleTreeCode.NopExpr, advanced));
            }
            group.Start = start;
            group.Pointer = current;

            foreach (var member in group.Members)
            {
                var displacement = member.StartOffset - origin;
                GimpleValue replacement = displacement == 0
                    ? new GimpleIndirectExpression(current, elementType, member.Original.Syntax)
                    : new GimpleElementAccessExpression(current, Constant(displacement), elementType, member.Original.Syntax);
                replacements[member.Node] = SsaRewriting.Build(replacement, member.Node.Role, member.Node.ReadsMemory);
            }
        }

        private void Widen(Shape shape, Group group, List<GimpleStatementAnnotations> preheader,
            List<GimpleStatementAnnotations> latch, Dictionary<GimpleOperandInfo, GimpleOperandInfo> replacements,
            Dictionary<long, GimpleName> widened)
        {
            var first = group.First;
            if (!first.NarrowIndex || !first.Index.Step.TryGetConstant(out var stride))
                return;
            if (!widened.TryGetValue(stride, out var counter))
            {
                var variable = NewVariable(_indexType);
                counter = NewName(variable);
                var next = NewName(variable);
                AddPhi(shape, variable, counter, Emit(shape.Preheader, preheader, GimpleTreeCode.IntegerCst, _indexType, Constant(0)), next);
                latch.Add(NewAssign(shape.Latch, next, GimpleTreeCode.PlusExpr, counter, Constant(stride)));
                widened.Add(stride, counter);
            }

            foreach (var member in group.Members)
            {
                GimpleValue baseValue = GimpleNameMaterializer.MaterializeValue(member.Node.Children[0]);
                if (member.StartRest is not null || member.StartOffset != 0)
                {
                    var start = member.StartRest is null
                        ? Constant(member.StartOffset)
                        : Materialize(Scev.Add(member.StartRest, Scev.Constant(member.StartOffset, _pointerBits), false)!, shape.Preheader, preheader);
                    baseValue = EmitAddressOf(shape.Preheader, preheader, new QualifiedType(TypeCatalog.Instance.PointerTo(member.Original.Type)),
                        new GimpleElementAccessExpression(baseValue, start, member.Original.Type, member.Original.Syntax));
                }
                replacements[member.Node] = SsaRewriting.Build(new GimpleElementAccessExpression(baseValue, counter, member.Original.Type, member.Original.Syntax),
                    member.Node.Role, member.Node.ReadsMemory);
            }
        }

        private GimpleStatementAnnotations? RewriteExitTest(Shape shape, ScalarEvolution evolution, List<Group> reduced,
            Dictionary<long, GimpleName> widened, List<GimpleStatementAnnotations> preheader,
            Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> rewritten)
        {
            if (shape.ExitTest?.Statement is not GimpleCondStatement test || _pointerBits != 64)
                return null;
            var ivOnLeft = IsRecurrence(test.Lhs, evolution) && IsInvariantOperand(test.Rhs, evolution);
            if (!ivOnLeft && !(IsRecurrence(test.Rhs, evolution) && IsInvariantOperand(test.Lhs, evolution)))
                return null;
            var variable = (GimpleName)(ivOnLeft ? test.Lhs : test.Rhs);
            var bound = ivOnLeft ? test.Rhs : test.Lhs;
            if (evolution.IntegerBits(variable.Type) != 32 || evolution.IntegerBits(bound.Type) != 32 ||
                evolution.Analyze(variable) is not { Kind: ScevKind.AddRecurrence } recurrence ||
                !recurrence.Step.TryGetConstant(out var stride) ||
                Scev.Extend(recurrence, 64, GimpleTypes.IsSigned(variable.Type)) is not { Kind: ScevKind.AddRecurrence } wide ||
                !IsDeadAfterRewrite(variable, shape.ExitTest, rewritten, new HashSet<GimpleName>()))
                return null;

            var signed = GimpleTypes.IsSigned(variable.Type);
            var code = ivOnLeft ? test.Code : Swap(test.Code);
            var continueCode = shape.ExitWhenTrue ? ScalarEvolution.Invert(code) : code;

            if (!_pointerWalk)
            {
                if (!widened.TryGetValue(stride, out var counter))
                    return null;
                var start = Materialize(wide.Start, shape.Preheader, preheader);
                var limit = Emit(shape.Preheader, preheader, GimpleTreeCode.MinusExpr, _indexType, Extend(bound, signed, shape.Preheader, preheader), start);
                var widenedTest = ivOnLeft ? test.WithOperands(test.Code, counter, limit) : test.WithOperands(test.Code, limit, counter);
                return NewCondition(shape.ExitTest, widenedTest);
            }

            var group = reduced.FirstOrDefault(candidate => candidate.First.Index.Step.TryGetConstant(out var step) && step == stride);
            if (group?.Start is null || group.Pointer is null || stride is not (1 or -1))
                return null;
            var extra = continueCode switch
            {
                GimpleTreeCode.LtExpr when stride == 1 => 0,
                GimpleTreeCode.LeExpr when stride == 1 => 1,
                GimpleTreeCode.GtExpr when stride == -1 => 0,
                GimpleTreeCode.GeExpr when stride == -1 => 1,
                GimpleTreeCode.NeExpr when signed => 0,
                _ => -1,
            };
            if (extra < 0)
                return null;

            // != stops only on the exact trip count, which a reversed range would step past
            GimpleValue distance;
            if (wide.Start.TryGetConstant(out var first) && bound is GimpleConstantValue { Value: var last } && ScalarEvolution.TryGetInteger(last, out var limitValue))
            {
                var limit = signed ? (long)(int)limitValue : (long)(uint)limitValue;
                var trips = Math.Max(0, (stride == 1 ? limit - first : first - limit) + extra);
                distance = Constant(stride == 1 ? trips : -trips);
            }
            else
            {
                var forward = continueCode == GimpleTreeCode.NeExpr ||
                    stride == 1 && wide.Start.TryGetConstant(out first) && first <= 0 && !GimpleTypes.IsSigned(bound.Type) ||
                    IsKnownForward(shape, recurrence.Start, variable.Type, bound, stride);
                var from = Materialize(wide.Start, shape.Preheader, preheader);
                var to = Extend(bound, signed, shape.Preheader, preheader);
                distance = stride == 1
                    ? Emit(shape.Preheader, preheader, GimpleTreeCode.MinusExpr, _indexType, to, from)
                    : Emit(shape.Preheader, preheader, GimpleTreeCode.MinusExpr, _indexType, from, to);
                if (extra != 0)
                    distance = Emit(shape.Preheader, preheader, GimpleTreeCode.PlusExpr, _indexType, distance, Constant(1));
                if (!forward)
                {
                    // A range that is empty from the start has to leave on the first test
                    var sign = Emit(shape.Preheader, preheader, GimpleTreeCode.RshiftExpr, _indexType, distance, Constant(63));
                    var negative = Emit(shape.Preheader, preheader, GimpleTreeCode.BitAndExpr, _indexType, distance, sign);
                    distance = Emit(shape.Preheader, preheader, GimpleTreeCode.MinusExpr, _indexType, distance, negative);
                }
                if (stride == -1)
                    distance = Emit(shape.Preheader, preheader, GimpleTreeCode.NegateExpr, _indexType, distance);
            }
            var end = Emit(shape.Preheader, preheader, GimpleTreeCode.PointerPlusExpr, group.Start.Type, group.Start, distance);
            var inside = shape.ExitWhenTrue ? test.WhenFalse : test.WhenTrue;
            var outside = shape.ExitWhenTrue ? test.WhenTrue : test.WhenFalse;
            return NewCondition(shape.ExitTest, new GimpleCondStatement(GimpleTreeCode.NeExpr, group.Pointer, end, inside, outside, test.Syntax));
        }

        private bool IsKnownForward(Shape shape, Scev start, QualifiedType type, GimpleValue bound, long stride)
        {
            GimpleValue? first = start.Kind switch
            {
                ScevKind.Invariant => start.Name,
                ScevKind.Constant => new GimpleConstantValue(GimpleTypes.IsSigned(type) ? (object)(int)start.Value : (uint)start.Value, type),
                _ => null,
            };
            if (_assertions is null || first is null)
                return false;
            return stride == 1
                ? _assertions.Proves(shape.Preheader, bound, GimpleTreeCode.GeExpr, first)
                : _assertions.Proves(shape.Preheader, first, GimpleTreeCode.GeExpr, bound);
        }

        private static bool IsRecurrence(GimpleValue value, ScalarEvolution evolution)
            => value is GimpleName name && evolution.Analyze(name) is { Kind: ScevKind.AddRecurrence };

        private static bool IsInvariantOperand(GimpleValue value, ScalarEvolution evolution)
            => value is GimpleConstantValue || value is GimpleName name && evolution.IsLoopInvariant(name);

        private static GimpleTreeCode Swap(GimpleTreeCode code) => code switch
        {
            GimpleTreeCode.LtExpr => GimpleTreeCode.GtExpr,
            GimpleTreeCode.LeExpr => GimpleTreeCode.GeExpr,
            GimpleTreeCode.GtExpr => GimpleTreeCode.LtExpr,
            GimpleTreeCode.GeExpr => GimpleTreeCode.LeExpr,
            _ => code,
        };

        private bool IsDeadAfterRewrite(GimpleName name, GimpleStatementAnnotations? test,
            Dictionary<GimpleStatementAnnotations, GimpleStatementAnnotations> rewritten, HashSet<GimpleName> visiting)
        {
            if (!visiting.Add(name))
                return true;
            foreach (var use in _function.GetImmediateUses(name))
            {
                if (!_analysis.Spend())
                    return false;
                if (use.Kind == GimpleUseKind.Phi)
                {
                    if (use.Statement is not GimplePhi phi || !IsDeadAfterRewrite(phi.Result, test, rewritten, visiting))
                        return false;
                    continue;
                }
                if (use.Statement is null || !_instructions.TryGetValue(use.Statement, out var instruction))
                    return false;
                if (ReferenceEquals(instruction, test))
                    continue;
                if (rewritten.TryGetValue(instruction, out var replacement))
                {
                    if (replacement.Uses.Any(other => ReferenceEquals(other.Name, name)))
                        return false;
                    continue;
                }
                if (instruction.Statement is not GimpleAssignStatement ||
                    (instruction.Flags & (GimpleStatementFlags.ReadsMemory | GimpleStatementFlags.WritesMemory | GimpleStatementFlags.ContainsCall)) != 0 ||
                    instruction.Definitions.Length != 1 ||
                    !IsDeadAfterRewrite(instruction.Definitions[0].Name, test, rewritten, visiting))
                    return false;
            }
            return true;
        }

        private GimpleValue Extend(GimpleValue value, bool signed, ControlFlowBlock block, List<GimpleStatementAnnotations> sink)
        {
            if (value is GimpleConstantValue constant && ScalarEvolution.TryGetInteger(constant.Value, out var number))
                return Constant(signed ? (long)(int)number : (long)(uint)number);
            return Emit(block, sink, GimpleTreeCode.NopExpr, _indexType, value);
        }

        private GimpleValue Materialize(Scev value, ControlFlowBlock block, List<GimpleStatementAnnotations> sink)
        {
            var type = value.Bits == _pointerBits ? _indexType : TypeCatalog.Instance.Builtin(BuiltinTypeKind.Int);
            switch (value.Kind)
            {
                case ScevKind.Constant:
                    return new GimpleConstantValue(value.Bits == 64 ? value.Value : (object)(int)value.Value, type);
                case ScevKind.Invariant:
                    return value.Name!;
                case ScevKind.Extend:
                    {
                        var operand = Materialize(value.Left!, block, sink);
                        if (GimpleTypes.IsSigned(operand.Type) != value.SignExtend)
                        {
                            var sameWidth = TypeCatalog.Instance.Builtin(value.SignExtend ? BuiltinTypeKind.Int : BuiltinTypeKind.UnsignedInt);
                            operand = Emit(block, sink, GimpleTreeCode.NopExpr, sameWidth, operand);
                        }
                        return Emit(block, sink, GimpleTreeCode.NopExpr, type, operand);
                    }
                case ScevKind.Add:
                    return Emit(block, sink, GimpleTreeCode.PlusExpr, type, Materialize(value.Left!, block, sink), Materialize(value.Right!, block, sink));
                case ScevKind.Multiply:
                    return Emit(block, sink, GimpleTreeCode.MultExpr, type, Materialize(value.Left!, block, sink), Materialize(value.Right!, block, sink));
                default:
                    throw new InvalidOperationException("A recurrence has no value outside its loop.");
            }
        }

        private GimpleConstantValue Constant(long value)
            => new(_pointerBits == 64 ? value : (object)(int)value, _indexType);

        private GimpleName EmitAddressOf(ControlFlowBlock block, List<GimpleStatementAnnotations> sink, QualifiedType type, GimplePlace place)
            => Emit(block, sink, GimpleTreeCode.AddrExpr, type, new GimpleAddressOfExpression(place, type, place.Syntax));

        private GimpleName Emit(ControlFlowBlock block, List<GimpleStatementAnnotations> sink, GimpleTreeCode code, QualifiedType type, params GimpleValue[] operands)
        {
            var name = NewName(NewVariable(type));
            sink.Add(NewAssign(block, name, code, operands));
            return name;
        }

        private GimpleStatementAnnotations NewAssign(ControlFlowBlock block, GimpleName result, GimpleTreeCode code, params GimpleValue[] operands)
        {
            var statement = operands.Length == 1 && code is GimpleTreeCode.AddrExpr or GimpleTreeCode.IntegerCst or GimpleTreeCode.SsaName
                ? GimpleAssignStatement.Single(result, operands[0])
                : new GimpleAssignStatement(result, code, operands.ToImmutableArray());
            var expressions = operands.Select(operand => SsaRewriting.Build(operand, GimpleOperandRole.Value, readsMemory: false)).ToImmutableArray();
            var definition = new GimpleDefinition(result, GimpleDefinitionKind.Statement, block, statement, result, parameter: null);
            _newDefinitions.Add(definition);
            return new GimpleStatementAnnotations(_nextStatement++, block, statement, statement, expressions,
                SsaRewriting.CollectUses(expressions, block, statement), ImmutableArray.Create(definition), null, null, GimpleStatementFlags.None);
        }

        private GimpleStatementAnnotations NewCondition(GimpleStatementAnnotations previous, GimpleCondStatement statement)
        {
            var expressions = ImmutableArray.Create(
                SsaRewriting.Build(statement.Lhs, GimpleOperandRole.Value, readsMemory: false),
                SsaRewriting.Build(statement.Rhs, GimpleOperandRole.Value, readsMemory: false));
            return new GimpleStatementAnnotations(previous.Ordinal, previous.Block, statement, statement, expressions,
                SsaRewriting.CollectUses(expressions, previous.Block, statement), ImmutableArray<GimpleDefinition>.Empty, null, null, GimpleStatementFlags.None);
        }

        private GimpleStatementAnnotations Rewrite(GimpleStatementAnnotations instruction, Dictionary<GimpleOperandInfo, GimpleOperandInfo> replacements)
            => SsaRewriting.Rewrite(instruction, node => replacements.TryGetValue(node, out var replacement) ? replacement : null, _redefinitions);

        private GimpleVariable NewVariable(QualifiedType type)
        {
            var temporary = new GimpleTemporaryValue(_nextTemporary++, type);
            var variable = new GimpleVariable(_nextVariable++, GimpleVariableKind.Temporary, null, temporary, type, temporary.Name);
            _variables.Add(variable);
            var undefined = new GimpleName(variable, 0, isUndefined: true);
            _undefined.Add(variable, undefined);
            _newDefinitions.Add(new GimpleDefinition(undefined, GimpleDefinitionKind.Undefined, block: null, statement: null, target: null, parameter: null));
            return variable;
        }

        private readonly Dictionary<GimpleVariable, int> _versions = new();

        private GimpleName NewName(GimpleVariable variable)
        {
            _versions.TryGetValue(variable, out var version);
            _versions[variable] = ++version;
            return new GimpleName(variable, version, isUndefined: false);
        }

        private void AddPhi(Shape shape, GimpleVariable variable, GimpleName result, GimpleName initial, GimpleName next)
        {
            var definition = new GimpleDefinition(result, GimpleDefinitionKind.Phi, shape.Loop.Header, statement: null, target: result, parameter: null);
            _newDefinitions.Add(definition);
            var phi = new GimplePhi(_nextPhi++, shape.Loop.Header, variable, result,
                ImmutableArray.Create(new GimplePhiOperand(shape.EntryEdge, initial), new GimplePhiOperand(shape.BackEdge, next)));
            if (!_phis.TryGetValue(shape.Loop.Header, out var list))
                _phis.Add(shape.Loop.Header, list = new List<GimplePhi>());
            list.Add(phi);
        }

        private void Append(ControlFlowBlock block, List<GimpleStatementAnnotations> statements)
        {
            if (statements.Count == 0)
                return;
            if (!_appended.TryGetValue(block, out var list))
                _appended.Add(block, list = new List<GimpleStatementAnnotations>());
            list.AddRange(statements);
        }

        private GimpleFunctionAnnotations Rebuild()
        {
            var blocks = ImmutableArray.CreateBuilder<GimpleBlockAnnotations>(_function.Blocks.Length);
            var uses = ImmutableArray.CreateBuilder<GimpleUse>();
            foreach (var block in _function.Blocks)
            {
                var statements = new List<GimpleStatementAnnotations>(block.Statements.Length);
                foreach (var instruction in block.Statements)
                    statements.Add(_replacements.TryGetValue(instruction, out var replacement) ? replacement : instruction);
                if (_appended.TryGetValue(block.ControlFlowBlock, out var appended))
                {
                    var at = statements.Count != 0 && statements[^1].Statement.IsTerminator ? statements.Count - 1 : statements.Count;
                    statements.InsertRange(at, appended);
                }
                foreach (var instruction in statements)
                    uses.AddRange(instruction.Uses);
                var phis = _phis.TryGetValue(block.ControlFlowBlock, out var added) ? block.Phis.AddRange(added) : block.Phis;
                blocks.Add(new GimpleBlockAnnotations(block.ControlFlowBlock, phis, statements.ToImmutableArray()));
            }

            var definitions = ImmutableArray.CreateBuilder<GimpleDefinition>(_function.Definitions.Length + _newDefinitions.Count);
            foreach (var definition in _function.Definitions)
                definitions.Add(_redefinitions.TryGetValue(definition, out var redefined) ? redefined : definition);
            definitions.AddRange(_newDefinitions);

            var undefined = new Dictionary<GimpleVariable, GimpleName>(_undefined);
            foreach (var variable in _function.Variables)
                undefined.Add(variable, _function.GetUndefinedName(variable));

            return new GimpleFunctionAnnotations(_function.ControlFlowFunction, _function.MemoryVariable,
                _function.Variables.AddRange(_variables), blocks.ToImmutable(), definitions.ToImmutable(), uses.ToImmutable(),
                _function.Problems, undefined, _valueNumberingOptions, _function.Target);
        }
    }
}
