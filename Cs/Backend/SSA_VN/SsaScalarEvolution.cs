using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    internal enum ScevOper : byte
    {
        Constant,
        Local,
        ZeroExtend,
        SignExtend,
        Add,
        Mul,
        Lsh,
        AddRec,
    }

    internal enum ScevType : byte
    {
        Int32,
        Int64,
        NativeInt,
        Ref,
        ByRef,
    }

    internal abstract class Scev
    {
        public ScevOper Oper { get; }
        public ScevType Type { get; }
        public RuntimeType? RuntimeType { get; }
        public GenStackKind StackKind { get; }

        protected Scev(ScevOper oper, ScevType type, RuntimeType? runtimeType, GenStackKind stackKind)
        {
            Oper = oper;
            Type = type;
            RuntimeType = runtimeType;
            StackKind = stackKind;
        }

        public bool IsInvariant()
        {
            return this switch
            {
                ScevConstant => true,
                ScevLocal => true,
                ScevUnary unary => unary.Operand.IsInvariant(),
                ScevBinary binary => binary.Left.IsInvariant() && binary.Right.IsInvariant(),
                ScevAddRec => false,
                _ => false,
            };
        }

        public Scev PeelAdditions(out long offset)
        {
            offset = 0;
            Scev current = this;
            while (current is ScevBinary { Oper: ScevOper.Add } add)
            {
                if (add.Left is ScevConstant leftConstant)
                {
                    offset = unchecked(offset + leftConstant.Value);
                    current = add.Right;
                    continue;
                }
                if (add.Right is ScevConstant rightConstant)
                {
                    offset = unchecked(offset + rightConstant.Value);
                    current = add.Left;
                    continue;
                }
                break;
            }
            return current;
        }

        public static bool StructuralEquals(Scev? left, Scev? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;
            if (left.Oper != right.Oper || left.Type != right.Type)
                return false;

            return (left, right) switch
            {
                (ScevConstant l, ScevConstant r) => l.Value == r.Value,
                (ScevLocal l, ScevLocal r) => l.Value.Equals(r.Value),
                (ScevUnary l, ScevUnary r) => StructuralEquals(l.Operand, r.Operand),
                (ScevBinary l, ScevBinary r) => StructuralEquals(l.Left, r.Left) && StructuralEquals(l.Right, r.Right),
                (ScevAddRec l, ScevAddRec r) => StructuralEquals(l.Start, r.Start) && StructuralEquals(l.Step, r.Step),
                _ => false,
            };
        }
    }

    internal sealed class ScevConstant : Scev
    {
        public long Value { get; }

        public ScevConstant(ScevType type, RuntimeType? runtimeType, GenStackKind stackKind, long value)
            : base(ScevOper.Constant, type, runtimeType, stackKind)
        {
            Value = value;
        }
    }

    internal sealed class ScevLocal : Scev
    {
        public SsaValueName Value { get; }

        public ScevLocal(ScevType type, RuntimeType? runtimeType, GenStackKind stackKind, SsaValueName value)
            : base(ScevOper.Local, type, runtimeType, stackKind)
        {
            Value = value;
        }
    }

    internal sealed class ScevUnary : Scev
    {
        public Scev Operand { get; }

        public ScevUnary(ScevOper oper, ScevType type, RuntimeType? runtimeType, GenStackKind stackKind, Scev operand)
            : base(oper, type, runtimeType, stackKind)
        {
            if (oper is not (ScevOper.ZeroExtend or ScevOper.SignExtend))
                throw new ArgumentOutOfRangeException(nameof(oper));
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }
    }

    internal sealed class ScevBinary : Scev
    {
        public Scev Left { get; }
        public Scev Right { get; }

        public ScevBinary(ScevOper oper, ScevType type, RuntimeType? runtimeType, GenStackKind stackKind, Scev left, Scev right)
            : base(oper, type, runtimeType, stackKind)
        {
            if (oper is not (ScevOper.Add or ScevOper.Mul or ScevOper.Lsh))
                throw new ArgumentOutOfRangeException(nameof(oper));
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }
    }

    internal sealed class ScevAddRec : Scev
    {
        public int LoopIndex { get; }
        public Scev Start { get; }
        public Scev Step { get; }

        public ScevAddRec(ScevType type, RuntimeType? runtimeType, GenStackKind stackKind, int loopIndex, Scev start, Scev step)
            : base(ScevOper.AddRec, type, runtimeType, stackKind)
        {
            if (loopIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(loopIndex));
            LoopIndex = loopIndex;
            Start = start ?? throw new ArgumentNullException(nameof(start));
            Step = step ?? throw new ArgumentNullException(nameof(step));
        }
    }

    internal readonly struct ScevSimplificationAssumptions
    {
        public static readonly ScevSimplificationAssumptions None = new(ImmutableArray<Scev>.Empty);

        public ImmutableArray<Scev> BackEdgeTakenBounds { get; }

        public ScevSimplificationAssumptions(ImmutableArray<Scev> backEdgeTakenBounds)
        {
            BackEdgeTakenBounds = backEdgeTakenBounds.IsDefault ? ImmutableArray<Scev>.Empty : backEdgeTakenBounds;
        }
    }

    internal sealed class ScalarEvolutionContext
    {
        private const int AnalysisMaximumDepth = 64;

        private readonly SsaMethod _method;
        private readonly TargetInfo _target;
        private readonly Dictionary<GenTree, Scev?> _treeCache = new(ReferenceEqualityComparer<GenTree>.Instance);
        private readonly Dictionary<GenTree, Scev?> _ephemeralTreeCache = new(ReferenceEqualityComparer<GenTree>.Instance);
        private readonly Dictionary<SsaValueName, Scev?> _valueCache = new();
        private readonly Dictionary<SsaValueName, Scev?> _ephemeralValueCache = new();
        private CfgLoop _loop;
        private bool _usingEphemeralCache;
        private bool _hasLoop;

        public ScalarEvolutionContext(SsaMethod method)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            _target = method.GenTreeMethod.Target;
        }

        public CfgLoop Loop => _hasLoop ? _loop : throw new InvalidOperationException("Scalar evolution context has no active loop.");

        public void ResetForLoop(CfgLoop loop)
        {
            _loop = loop;
            _hasLoop = true;
            _treeCache.Clear();
            _ephemeralTreeCache.Clear();
            _valueCache.Clear();
            _ephemeralValueCache.Clear();
            _usingEphemeralCache = false;
        }

        public Scev? Analyze(int blockId, GenTree tree)
        {
            if (!_hasLoop)
                throw new InvalidOperationException("Scalar evolution context has no active loop.");
            if (tree is null)
                throw new ArgumentNullException(nameof(tree));
            return AnalyzeTree(blockId, tree, 0);
        }

        public Scev? Analyze(SsaValueName value)
        {
            if (!_hasLoop)
                throw new InvalidOperationException("Scalar evolution context has no active loop.");
            return AnalyzeValue(value, 0);
        }

        public Scev Simplify(Scev scev)
            => Simplify(scev, ScevSimplificationAssumptions.None);

        public Scev Simplify(Scev scev, in ScevSimplificationAssumptions assumptions)
        {
            if (scev is null)
                throw new ArgumentNullException(nameof(scev));

            switch (scev)
            {
                case ScevConstant:
                    return scev;

                case ScevLocal local:
                    return TryGetConstantValue(local, out var localConstant)
                        ? NewConstant(local.RuntimeType, local.StackKind, localConstant)
                        : local;

                case ScevUnary unary:
                    {
                        var operand = Simplify(unary.Operand, assumptions);
                        if (unary.Type == operand.Type)
                            return operand;
                        if (operand is ScevConstant constant)
                        {
                            long value = unary.Oper == ScevOper.ZeroExtend
                                ? unchecked((long)(uint)constant.Value)
                                : unchecked((long)(int)constant.Value);
                            return NewConstant(unary.RuntimeType, unary.StackKind, value);
                        }
                        if (operand is ScevAddRec addRec &&
                            !AddRecMayOverflow(addRec, unary.Oper == ScevOper.SignExtend, assumptions))
                        {
                            var start = Simplify(NewExtension(unary.Oper, unary.RuntimeType, unary.StackKind, addRec.Start), assumptions);
                            var step = Simplify(NewExtension(unary.Oper, unary.RuntimeType, unary.StackKind, addRec.Step), assumptions);
                            return NewAddRec(start, step);
                        }
                        return ReferenceEquals(operand, unary.Operand)
                            ? unary
                            : NewExtension(unary.Oper, unary.RuntimeType, unary.StackKind, operand);
                    }

                case ScevBinary binary:
                    return SimplifyBinary(binary, assumptions);

                case ScevAddRec addRec:
                    {
                        var start = Simplify(addRec.Start, assumptions);
                        var step = Simplify(addRec.Step, assumptions);
                        return ReferenceEquals(start, addRec.Start) && ReferenceEquals(step, addRec.Step)
                            ? addRec
                            : NewAddRec(start, step);
                    }

                default:
                    return scev;
            }
        }

        private Scev SimplifyBinary(ScevBinary binary, in ScevSimplificationAssumptions assumptions)
        {
            var left = Simplify(binary.Left, assumptions);
            var right = Simplify(binary.Right, assumptions);

            if (binary.Oper is ScevOper.Add or ScevOper.Mul)
            {
                if (right is ScevAddRec && left is not ScevAddRec)
                    (left, right) = (right, left);
                if (left is ScevConstant && right is not ScevConstant)
                    (left, right) = (right, left);
            }

            if (left is ScevAddRec addRec)
            {
                var start = Simplify(NewBinary(binary.Oper, addRec.Start, right), assumptions);
                var step = binary.Oper is ScevOper.Mul or ScevOper.Lsh
                    ? Simplify(NewBinary(binary.Oper, addRec.Step, right), assumptions)
                    : addRec.Step;
                return NewAddRec(start, step);
            }

            if (left is ScevConstant leftConstant && right is ScevConstant rightConstant)
            {
                long value = binary.Oper switch
                {
                    ScevOper.Add => unchecked(leftConstant.Value + rightConstant.Value),
                    ScevOper.Mul => unchecked(leftConstant.Value * rightConstant.Value),
                    ScevOper.Lsh => unchecked(leftConstant.Value << (int)(rightConstant.Value & (IntegralBits(binary.Type) - 1))),
                    _ => throw new InvalidOperationException(),
                };
                if (IntegralBits(binary.Type) == 32)
                    value = unchecked((int)value);
                return NewConstant(binary.RuntimeType, binary.StackKind, value);
            }

            if (right is ScevConstant rightCns)
            {
                if (binary.Oper is ScevOper.Add or ScevOper.Lsh && rightCns.Value == 0)
                    return left;

                if (binary.Oper == ScevOper.Add &&
                    left is ScevBinary { Oper: ScevOper.Add, Right: ScevConstant } leftAdd)
                {
                    var newRight = NewBinary(ScevOper.Add, leftAdd.Right, right);
                    return Simplify(NewBinary(ScevOper.Add, leftAdd.Left, newRight), assumptions);
                }

                if (binary.Oper == ScevOper.Mul)
                {
                    if (rightCns.Value == 0)
                        return right;
                    if (rightCns.Value == 1)
                        return left;
                    if (left is ScevBinary { Oper: ScevOper.Mul, Right: ScevConstant } leftMul)
                    {
                        var newRight = NewBinary(ScevOper.Mul, leftMul.Right, right);
                        return Simplify(NewBinary(ScevOper.Mul, leftMul.Left, newRight), assumptions);
                    }
                }
            }
            else if (left is ScevConstant leftCns && binary.Oper == ScevOper.Lsh && leftCns.Value == 0)
            {
                return left;
            }

            if (binary.Oper == ScevOper.Add &&
                left is ScevBinary { Oper: ScevOper.Add, Right: ScevConstant } leftAddWithConstant &&
                right is ScevBinary { Oper: ScevOper.Add, Right: ScevConstant } rightAddWithConstant)
            {
                var newLeft = NewBinary(ScevOper.Add, leftAddWithConstant.Left, rightAddWithConstant.Left);
                var newRight = NewBinary(ScevOper.Add, leftAddWithConstant.Right, rightAddWithConstant.Right);
                return Simplify(NewBinary(ScevOper.Add, newLeft, newRight), assumptions);
            }

            return ReferenceEquals(left, binary.Left) && ReferenceEquals(right, binary.Right)
                ? binary
                : new ScevBinary(binary.Oper, binary.Type, binary.RuntimeType, binary.StackKind, left, right);
        }

        private bool AddRecMayOverflow(
            ScevAddRec addRec,
            bool signedBound,
            in ScevSimplificationAssumptions assumptions)
        {
            if (assumptions.BackEdgeTakenBounds.IsDefaultOrEmpty || addRec.Type != ScevType.Int32 || signedBound)
                return true;
            if (!TryGetConstant(addRec.Start, out var start) || unchecked((int)start) != 0)
                return true;
            if (!TryGetConstant(addRec.Step, out var step) || unchecked((int)step) != 1)
                return true;

            for (int i = 0; i < assumptions.BackEdgeTakenBounds.Length; i++)
            {
                if (assumptions.BackEdgeTakenBounds[i].Type == ScevType.Int32)
                    return false;
            }

            return true;
        }

        public Scev? ComputeExitNotTakenCount(int exitingBlockId)
        {
            if (!_hasLoop || (uint)exitingBlockId >= (uint)_method.Blocks.Length || !_loop.Contains(exitingBlockId))
                return null;
            if (!TryGetExitingCondition(exitingBlockId, out var condition, out bool exitWhenConditionTrue))
                return null;
            if (condition.Kind != GenTreeKind.Binary || condition.Operands.Length != 2)
                return null;

            ExitComparison comparison;
            bool unsigned;
            switch (condition.SourceOp)
            {
                case BytecodeOp.Clt:
                    comparison = ExitComparison.LessThan;
                    unsigned = false;
                    break;
                case BytecodeOp.Clt_Un:
                    comparison = ExitComparison.LessThan;
                    unsigned = true;
                    break;
                case BytecodeOp.Cgt:
                    comparison = ExitComparison.GreaterThan;
                    unsigned = false;
                    break;
                case BytecodeOp.Cgt_Un:
                    comparison = ExitComparison.GreaterThan;
                    unsigned = true;
                    break;
                default:
                    return null;
            }

            if (!exitWhenConditionTrue)
                comparison = ReverseComparison(comparison);

            var lhs = Analyze(exitingBlockId, condition.Operands[0]);
            var rhs = Analyze(exitingBlockId, condition.Operands[1]);
            if (lhs is null || rhs is null || lhs.Type is ScevType.Ref or ScevType.ByRef || rhs.Type is ScevType.Ref or ScevType.ByRef)
                return null;

            lhs = Simplify(lhs);
            rhs = Simplify(rhs);
            bool lhsInvariant = lhs.IsInvariant();
            bool rhsInvariant = rhs.IsInvariant();
            if (lhsInvariant == rhsInvariant)
                return null;
            if (lhsInvariant)
            {
                (lhs, rhs) = (rhs, lhs);
                comparison = SwapComparison(comparison);
            }

            if (lhs is not ScevAddRec addRec ||
                addRec.LoopIndex != _loop.Index ||
                addRec.Type != rhs.Type ||
                !TryGetConstant(addRec.Step, out var rawStep))
            {
                return null;
            }

            long step = addRec.Type == ScevType.Int32 ? unchecked((int)rawStep) : rawStep;
            if (((comparison is ExitComparison.GreaterThan or ExitComparison.GreaterThanOrEqual) && step != 1) ||
                ((comparison is ExitComparison.LessThan or ExitComparison.LessThanOrEqual) && step != -1) ||
                MayOverflowBeforeExit(addRec, rhs, comparison, unsigned))
            {
                return null;
            }

            Scev lowerBound;
            Scev upperBound;
            var one = NewConstant(rhs.RuntimeType, rhs.StackKind, 1);
            var minusOne = NewConstant(rhs.RuntimeType, rhs.StackKind, -1);
            switch (comparison)
            {
                case ExitComparison.GreaterThanOrEqual:
                    lowerBound = addRec.Start;
                    upperBound = NewBinary(ScevOper.Add, rhs, NewBinary(ScevOper.Add, addRec.Step, minusOne));
                    break;
                case ExitComparison.GreaterThan:
                    lowerBound = addRec.Start;
                    upperBound = NewBinary(ScevOper.Add, rhs, addRec.Step);
                    break;
                case ExitComparison.LessThanOrEqual:
                    lowerBound = NewBinary(ScevOper.Add, rhs, NewBinary(ScevOper.Add, addRec.Step, one));
                    upperBound = addRec.Start;
                    break;
                case ExitComparison.LessThan:
                    lowerBound = NewBinary(ScevOper.Add, rhs, addRec.Step);
                    upperBound = addRec.Start;
                    break;
                default:
                    return null;
            }

            lowerBound = Simplify(lowerBound);
            upperBound = Simplify(upperBound);
            if (!CanProveLessOrEqual(lowerBound, upperBound, unsigned))
                return null;

            var negLower = NewBinary(ScevOper.Mul, lowerBound, NewConstant(lowerBound.RuntimeType, lowerBound.StackKind, -1));
            return Simplify(NewBinary(ScevOper.Add, upperBound, negLower));
        }


        private bool MayOverflowBeforeExit(ScevAddRec addRec, Scev rhs, ExitComparison comparison, bool unsigned)
        {
            if (!TryGetConstant(addRec.Step, out var rawStep))
                return true;

            long step = addRec.Type == ScevType.Int32 ? unchecked((int)rawStep) : rawStep;
            switch (comparison)
            {
                case ExitComparison.GreaterThan:
                    if (step < 0)
                        return true;
                    if (step == 1)
                        return !CanProveStrictStepWithoutOverflow(rhs, increment: true, unsigned);
                    return true;

                case ExitComparison.GreaterThanOrEqual:
                    return step != 1;

                case ExitComparison.LessThan:
                    if (step > 0)
                        return true;
                    if (step == -1)
                        return !CanProveStrictStepWithoutOverflow(rhs, increment: false, unsigned);
                    return true;

                case ExitComparison.LessThanOrEqual:
                    return step != -1;

                default:
                    return true;
            }
        }

        private bool CanProveStrictStepWithoutOverflow(Scev rhs, bool increment, bool unsigned)
        {
            if (!TryGetConstant(rhs, out var value))
                return false;

            switch (rhs.Type)
            {
                case ScevType.Int32:
                    return increment
                        ? (unsigned ? unchecked((uint)value) != uint.MaxValue : unchecked((int)value) != int.MaxValue)
                        : (unsigned ? unchecked((uint)value) != 0 : unchecked((int)value) != int.MinValue);

                case ScevType.Int64:
                    return increment
                        ? (unsigned ? unchecked((ulong)value) != ulong.MaxValue : value != long.MaxValue)
                        : (unsigned ? unchecked((ulong)value) != 0 : value != long.MinValue);

                case ScevType.NativeInt:
                    if (_target.PointerSize == 4)
                    {
                        return increment
                            ? (unsigned ? unchecked((uint)value) != uint.MaxValue : unchecked((int)value) != int.MaxValue)
                            : (unsigned ? unchecked((uint)value) != 0 : unchecked((int)value) != int.MinValue);
                    }
                    return increment
                        ? (unsigned ? unchecked((ulong)value) != ulong.MaxValue : value != long.MaxValue)
                        : (unsigned ? unchecked((ulong)value) != 0 : value != long.MinValue);

                default:
                    return false;
            }
        }

        private bool TryGetExitingCondition(int blockId, out GenTree condition, out bool exitWhenConditionTrue)
        {
            condition = null!;
            exitWhenConditionTrue = false;
            var block = _method.Blocks[blockId];
            if (block.Statements.IsDefaultOrEmpty)
                return false;

            SsaTree? terminator = null;
            for (int i = block.Statements.Length - 1; i >= 0; i--)
            {
                var candidate = block.Statements[i];
                if (candidate.Kind is GenTreeKind.BranchTrue or GenTreeKind.BranchFalse)
                {
                    terminator = candidate;
                    break;
                }
                if (candidate.Kind != GenTreeKind.Branch)
                    break;
            }

            if (terminator is null || terminator.Operands.Length != 1)
                return false;

            int inLoopNormal = 0;
            int outLoopNormal = 0;
            CfgEdge exitEdge = default;
            var successors = _method.Cfg.Blocks[blockId].Successors;
            for (int i = 0; i < successors.Length; i++)
            {
                var edge = successors[i];
                if (edge.Kind == CfgEdgeKind.Exception)
                    continue;
                if (_loop.Contains(edge.ToBlockId))
                    inLoopNormal++;
                else
                {
                    outLoopNormal++;
                    exitEdge = edge;
                }
            }

            if (inLoopNormal != 1 || outLoopNormal != 1)
                return false;

            bool branchTakenExits = exitEdge.Kind is CfgEdgeKind.BranchTrue or CfgEdgeKind.BranchFalse;
            exitWhenConditionTrue = terminator.Kind == GenTreeKind.BranchTrue ? branchTakenExits : !branchTakenExits;
            condition = terminator.Operands[0].Source;
            return true;
        }

        private bool CanProveLessOrEqual(Scev lower, Scev upper, bool unsigned)
        {
            if (lower.Type != upper.Type)
                return false;
            if (Scev.StructuralEquals(lower, upper))
                return true;
            if (TryGetConstant(lower, out var lowerValue) && TryGetConstant(upper, out var upperValue))
            {
                if (lower.Type == ScevType.Int32)
                {
                    return unsigned
                        ? unchecked((uint)lowerValue) <= unchecked((uint)upperValue)
                        : unchecked((int)lowerValue) <= unchecked((int)upperValue);
                }
                if (lower.Type == ScevType.Int64)
                {
                    return unsigned
                        ? unchecked((ulong)lowerValue) <= unchecked((ulong)upperValue)
                        : lowerValue <= upperValue;
                }
                if (lower.Type == ScevType.NativeInt)
                {
                    if (_target.PointerSize == 4)
                    {
                        return unsigned
                            ? unchecked((uint)lowerValue) <= unchecked((uint)upperValue)
                            : unchecked((int)lowerValue) <= unchecked((int)upperValue);
                    }
                    return unsigned
                        ? unchecked((ulong)lowerValue) <= unchecked((ulong)upperValue)
                        : lowerValue <= upperValue;
                }
                return false;
            }

            if (unsigned && TryGetConstant(lower, out lowerValue) && lowerValue == 0)
                return true;

            return false;
        }

        private static ExitComparison ReverseComparison(ExitComparison comparison)
            => comparison switch
            {
                ExitComparison.LessThan => ExitComparison.GreaterThanOrEqual,
                ExitComparison.LessThanOrEqual => ExitComparison.GreaterThan,
                ExitComparison.GreaterThan => ExitComparison.LessThanOrEqual,
                ExitComparison.GreaterThanOrEqual => ExitComparison.LessThan,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
            };

        private static ExitComparison SwapComparison(ExitComparison comparison)
            => comparison switch
            {
                ExitComparison.LessThan => ExitComparison.GreaterThan,
                ExitComparison.LessThanOrEqual => ExitComparison.GreaterThanOrEqual,
                ExitComparison.GreaterThan => ExitComparison.LessThan,
                ExitComparison.GreaterThanOrEqual => ExitComparison.LessThanOrEqual,
                _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
            };

        private enum ExitComparison : byte
        {
            LessThan,
            LessThanOrEqual,
            GreaterThan,
            GreaterThanOrEqual,
        }

        private bool TryGetConstant(Scev scev, out long value)
        {
            if (scev is ScevConstant constant)
            {
                value = constant.Value;
                return true;
            }
            if (scev is ScevLocal local)
                return TryGetConstantValue(local, out value);

            value = 0;
            return false;
        }

        private Scev? AnalyzeTree(int blockId, GenTree tree, int depth)
        {
            if (_treeCache.TryGetValue(tree, out var cached) ||
                (_usingEphemeralCache && _ephemeralTreeCache.TryGetValue(tree, out cached)))
            {
                return cached;
            }
            if (depth >= AnalysisMaximumDepth)
                return null;

            Scev? result = AnalyzeTreeNew(blockId, tree, depth);
            if (_usingEphemeralCache)
                _ephemeralTreeCache[tree] = result;
            else
                _treeCache[tree] = result;
            return result;
        }

        private Scev? AnalyzeTreeNew(int blockId, GenTree tree, int depth)
        {
            if (!IsScevType(tree.StackKind))
                return null;

            switch (tree.Kind)
            {
                case GenTreeKind.ConstI4:
                    return tree.StackKind == GenStackKind.I4
                        ? NewConstant(tree.Type, tree.StackKind, tree.Int32)
                        : null;

                case GenTreeKind.ConstI8:
                    return tree.StackKind == GenStackKind.I8
                        ? NewConstant(tree.Type, tree.StackKind, tree.Int64)
                        : null;

                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    {
                        if (!tree.SsaValueName.HasValue || !_method.TryGetSsaDescriptor(tree.SsaValueName.Value, out var descriptor))
                            return null;
                        if (tree.StackKind != descriptor.StackKind || !ReferenceEquals(tree.Type, descriptor.Type) || IsSmallIntegral(tree))
                            return null;
                        return AnalyzeValue(tree.SsaValueName.Value, depth + 1);
                    }

                case GenTreeKind.Conv:
                    {
                        if (tree.Operands.Length != 1 || tree.ConvKind is not (NumericConvKind.I8 or NumericConvKind.U8))
                            return null;
                        var operand = AnalyzeTree(blockId, tree.Operands[0], depth + 1);
                        if (operand is null || operand.Type != ScevType.Int32)
                            return null;
                        return NewExtension(
                            (tree.ConvFlags & NumericConvFlags.SourceUnsigned) != 0 ? ScevOper.ZeroExtend : ScevOper.SignExtend,
                            tree.Type,
                            tree.StackKind,
                            operand);
                    }

                case GenTreeKind.Binary:
                    {
                        if (tree.Operands.Length != 2)
                            return null;
                        if (tree.SourceOp is not (BytecodeOp.Add or BytecodeOp.Sub or BytecodeOp.Mul or BytecodeOp.Shl))
                            return null;

                        var left = AnalyzeTree(blockId, tree.Operands[0], depth + 1);
                        var right = AnalyzeTree(blockId, tree.Operands[1], depth + 1);
                        if (left is null || right is null)
                            return null;

                        if (tree.SourceOp == BytecodeOp.Sub)
                        {
                            if (right.StackKind == GenStackKind.ByRef)
                                return null;
                            var minusOne = NewConstant(right.RuntimeType, right.StackKind, -1);
                            right = NewBinary(ScevOper.Mul, right, minusOne);
                        }

                        var oper = tree.SourceOp switch
                        {
                            BytecodeOp.Add or BytecodeOp.Sub => ScevOper.Add,
                            BytecodeOp.Mul => ScevOper.Mul,
                            BytecodeOp.Shl => ScevOper.Lsh,
                            _ => throw new InvalidOperationException(),
                        };
                        return NewBinaryWithType(oper, left, right, tree.Type, tree.StackKind);
                    }

                default:
                    return null;
            }
        }

        private Scev? AnalyzeValue(SsaValueName value, int depth)
        {
            if (_valueCache.TryGetValue(value, out var cached) ||
                (_usingEphemeralCache && _ephemeralValueCache.TryGetValue(value, out cached)))
            {
                return cached;
            }
            if (depth >= AnalysisMaximumDepth)
                return null;
            if (!_method.TryGetSsaDescriptor(value, out var descriptor))
                return null;
            if (!IsScevType(descriptor.StackKind) || IsSmallIntegral(descriptor))
                return null;

            Scev? result;
            if (descriptor.IsInitial || descriptor.DefBlockId < 0 || !_loop.Contains(descriptor.DefBlockId))
            {
                result = NewLocal(descriptor, value);
            }
            else if (descriptor.IsPhi)
            {
                result = AnalyzePhi(descriptor, depth);
            }
            else if (descriptor.IsStore)
            {
                result = AnalyzeStore(descriptor, depth);
            }
            else
            {
                result = null;
            }

            if (_usingEphemeralCache)
                _ephemeralValueCache[value] = result;
            else
                _valueCache[value] = result;
            return result;
        }

        private Scev? AnalyzeStore(SsaDescriptor descriptor, int depth)
        {
            if (descriptor.IsPartialDefinition)
                return null;
            var data = GetDefinitionData(descriptor);
            return data is null ? null : AnalyzeTree(descriptor.DefBlockId, data, depth + 1);
        }

        private Scev? AnalyzePhi(SsaDescriptor descriptor, int depth)
        {
            if (descriptor.DefBlockId != _loop.Header || descriptor.Phi is null)
                return null;

            SsaValueName? enter = null;
            SsaValueName? backedge = null;
            for (int i = 0; i < descriptor.Phi.Inputs.Length; i++)
            {
                var input = descriptor.Phi.Inputs[i];
                if (_loop.Contains(input.PredecessorBlockId))
                {
                    if (!backedge.HasValue)
                        backedge = input.Value;
                    else if (!backedge.Value.Equals(input.Value))
                        return null;
                }
                else
                {
                    if (!enter.HasValue)
                        enter = input.Value;
                    else if (!enter.Value.Equals(input.Value))
                        return null;
                }
            }

            if (!enter.HasValue || !backedge.HasValue)
                return null;

            var startDescriptor = _method.GetSsaDescriptor(enter.Value);
            var start = NewLocal(startDescriptor, enter.Value);

            var simple = CreateSimpleAddRec(descriptor.Phi.Target, start, backedge.Value);
            if (simple is not null)
                return simple;

            var symbolic = NewConstant(descriptor.Type, descriptor.StackKind, 0xdeadbeef);
            _ephemeralValueCache[descriptor.Phi.Target] = symbolic;

            Scev? recursive;
            if (_usingEphemeralCache)
            {
                recursive = AnalyzeValue(backedge.Value, depth + 1);
            }
            else
            {
                _usingEphemeralCache = true;
                try
                {
                    recursive = AnalyzeValue(backedge.Value, depth + 1);
                }
                finally
                {
                    _usingEphemeralCache = false;
                    _ephemeralTreeCache.Clear();
                    _ephemeralValueCache.Clear();
                }
            }

            if (recursive is null)
                return null;

            return MakeAddRecFromRecursiveScev(start, recursive, symbolic);
        }

        private Scev? CreateSimpleAddRec(SsaValueName phiTarget, Scev start, SsaValueName backedgeValue)
        {
            if (!_method.TryGetSsaDescriptor(backedgeValue, out var backedgeDescriptor) || !backedgeDescriptor.IsStore)
                return null;

            var data = GetDefinitionData(backedgeDescriptor);
            if (data is null)
                return null;
            if (data.Kind != GenTreeKind.Binary || data.SourceOp != BytecodeOp.Add || data.Operands.Length != 2)
                return null;

            GenTree? stepTree = null;
            if (IsUseOf(data.Operands[0], phiTarget))
                stepTree = data.Operands[1];
            else if (IsUseOf(data.Operands[1], phiTarget))
                stepTree = data.Operands[0];

            if (stepTree is null)
                return null;

            var step = CreateSimpleInvariantScev(stepTree);
            return step is null
                ? null
                : NewAddRec(start, step);
        }

        private Scev? CreateSimpleInvariantScev(GenTree tree)
        {
            if (tree.Kind == GenTreeKind.ConstI4 && tree.StackKind == GenStackKind.I4)
                return NewConstant(tree.Type, tree.StackKind, tree.Int32);
            if (tree.Kind == GenTreeKind.ConstI8 && tree.StackKind == GenStackKind.I8)
                return NewConstant(tree.Type, tree.StackKind, tree.Int64);

            if ((tree.Kind is GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp) && tree.SsaValueName.HasValue)
            {
                var value = tree.SsaValueName.Value;
                if (_method.TryGetSsaDescriptor(value, out var descriptor) &&
                    (descriptor.DefBlockId < 0 || !_loop.Contains(descriptor.DefBlockId)))
                {
                    return NewLocal(descriptor, value);
                }
            }

            return null;
        }

        private Scev? MakeAddRecFromRecursiveScev(Scev start, Scev recursive, Scev symbolic)
        {
            if (recursive is not ScevBinary { Oper: ScevOper.Add })
                return null;

            var operands = new List<Scev>();
            ExtractAddOperands(recursive, operands);
            int topLevelAppearances = 0;
            for (int i = 0; i < operands.Count; i++)
            {
                if (ReferenceEquals(operands[i], symbolic))
                {
                    topLevelAppearances++;
                    continue;
                }
                if (ContainsReference(operands[i], symbolic))
                    return null;
            }

            if (topLevelAppearances != 1)
                return null;

            Scev? step = null;
            for (int i = 0; i < operands.Count; i++)
            {
                if (ReferenceEquals(operands[i], symbolic))
                    continue;
                step = step is null ? operands[i] : NewBinary(ScevOper.Add, step, operands[i]);
            }

            if (step is null)
                return null;

            return NewAddRec(start, step);
        }

        private static void ExtractAddOperands(Scev scev, List<Scev> operands)
        {
            if (scev is ScevBinary { Oper: ScevOper.Add } add)
            {
                ExtractAddOperands(add.Left, operands);
                ExtractAddOperands(add.Right, operands);
                return;
            }
            operands.Add(scev);
        }

        private static bool ContainsReference(Scev scev, Scev target)
        {
            if (ReferenceEquals(scev, target))
                return true;
            return scev switch
            {
                ScevUnary unary => ContainsReference(unary.Operand, target),
                ScevBinary binary => ContainsReference(binary.Left, target) || ContainsReference(binary.Right, target),
                ScevAddRec addRec => ContainsReference(addRec.Start, target) || ContainsReference(addRec.Step, target),
                _ => false,
            };
        }

        private bool TryGetConstantValue(ScevLocal local, out long value)
        {
            if (_method.TryGetSsaDescriptor(local.Value, out var descriptor) && !descriptor.IsPartialDefinition)
            {
                var data = GetDefinitionData(descriptor);
                if (data is not null && data.Kind == GenTreeKind.ConstI4)
                {
                    value = data.Int32;
                    return true;
                }
                if (data is not null && data.Kind == GenTreeKind.ConstI8)
                {
                    value = data.Int64;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static GenTree? GetDefinitionData(SsaDescriptor descriptor)
        {
            var defNode = descriptor.DefNode;
            if (defNode is null)
                return null;

            if (SsaSlotHelpers.TryGetDirectStoreSlot(defNode, out var directStoreSlot))
            {
                return directStoreSlot.Equals(descriptor.BaseLocal) && defNode.Operands.Length == 1
                    ? defNode.Operands[0]
                    : null;
            }

            if (SsaSlotHelpers.TryGetLocalFieldAccess(defNode, out var fieldAccess) &&
                fieldAccess.IsFullDefinition &&
                fieldAccess.IsPromotedFieldAccess &&
                fieldAccess.Slot.Equals(descriptor.BaseLocal) &&
                defNode.Kind == GenTreeKind.StoreField &&
                defNode.Operands.Length >= 2)
            {
                return defNode.Operands[1];
            }

            return null;
        }

        private static bool IsSmallIntegral(GenTree tree)
            => tree.StackKind == GenStackKind.I4 && tree.Type is not null && tree.Type.SizeOf > 0 && tree.Type.SizeOf < 4;

        private static bool IsSmallIntegral(SsaDescriptor descriptor)
            => descriptor.StackKind == GenStackKind.I4 && descriptor.Type is not null && descriptor.Type.SizeOf > 0 && descriptor.Type.SizeOf < 4;

        public ScevConstant NewConstant(RuntimeType? type, GenStackKind stackKind, long value)
        {
            if (IntegralBits(GetScevType(stackKind)) == 32)
                value = unchecked((int)value);
            return new ScevConstant(GetScevType(stackKind), type, stackKind, value);
        }

        public ScevUnary NewExtension(ScevOper oper, RuntimeType? type, GenStackKind stackKind, Scev operand)
            => new ScevUnary(oper, GetScevType(stackKind), type, stackKind, operand);

        public ScevAddRec NewAddRec(Scev start, Scev step)
            => new ScevAddRec(start.Type, start.RuntimeType, start.StackKind, Loop.Index, start, step);

        private ScevLocal NewLocal(SsaDescriptor descriptor, SsaValueName value)
            => new ScevLocal(GetScevType(descriptor.StackKind), descriptor.Type, descriptor.StackKind, value);

        private static bool IsUseOf(GenTree tree, SsaValueName value)
            => tree.SsaValueName.HasValue && tree.SsaValueName.Value.Equals(value);

        private static bool IsScevType(GenStackKind stackKind)
            => stackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr;

        private int IntegralBits(ScevType type)
            => type switch
            {
                ScevType.Int64 => 64,
                ScevType.NativeInt or ScevType.Ref or ScevType.ByRef => _target.PointerSize * 8,
                _ => 32,
            };

        private static ScevType GetScevType(GenStackKind stackKind)
            => stackKind switch
            {
                GenStackKind.I4 => ScevType.Int32,
                GenStackKind.I8 => ScevType.Int64,
                GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr => ScevType.NativeInt,
                GenStackKind.Ref => ScevType.Ref,
                GenStackKind.ByRef => ScevType.ByRef,
                _ => throw new ArgumentOutOfRangeException(nameof(stackKind)),
            };

        public ScevBinary NewBinary(ScevOper oper, Scev left, Scev right)
        {
            if (oper == ScevOper.Add)
            {
                if (left.Type is ScevType.Ref or ScevType.ByRef)
                    return new ScevBinary(oper, ScevType.ByRef, null, GenStackKind.ByRef, left, right);
                if (right.Type is ScevType.Ref or ScevType.ByRef)
                    return new ScevBinary(oper, ScevType.ByRef, null, GenStackKind.ByRef, left, right);
            }
            return NewBinaryWithType(oper, left, right, left.RuntimeType, left.StackKind);
        }

        private static ScevBinary NewBinaryWithType(ScevOper oper, Scev left, Scev right, RuntimeType? runtimeType, GenStackKind stackKind)
            => new ScevBinary(oper, GetScevType(stackKind), runtimeType, stackKind, left, right);
    }
}
