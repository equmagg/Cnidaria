using System;
using System.Collections.Generic;

namespace Cnidaria.C;

internal enum ScevKind : byte
{
    Constant,
    Invariant,
    Add,
    Multiply,
    Extend,
    AddRecurrence,
}

internal sealed class Scev
{
    public ScevKind Kind { get; }
    public int Bits { get; }
    public long Value { get; }
    public GimpleName? Name { get; }
    public Scev? Left { get; }
    public Scev? Right { get; }
    public bool SignExtend { get; }
    public bool NoSignedWrap { get; }
    public bool NoUnsignedWrap { get; }

    private Scev(ScevKind kind, int bits, long value, GimpleName? name, Scev? left, Scev? right,
        bool signExtend = false, bool noSignedWrap = false, bool noUnsignedWrap = false)
    {
        Kind = kind;
        Bits = bits;
        Value = value;
        Name = name;
        Left = left;
        Right = right;
        SignExtend = signExtend;
        NoSignedWrap = noSignedWrap;
        NoUnsignedWrap = noUnsignedWrap;
    }

    public Scev Start => Left!;
    public Scev Step => Right!;
    public bool IsInvariant => Kind != ScevKind.AddRecurrence && (Left?.IsInvariant ?? true) && (Right?.IsInvariant ?? true);

    public static Scev Constant(long value, int bits)
        => new(ScevKind.Constant, bits, Wrap(value, bits), null, null, null);

    public static Scev Invariant(GimpleName name, int bits)
        => new(ScevKind.Invariant, bits, 0, name, null, null);

    public static Scev AddRecurrence(Scev start, Scev step, bool noSignedWrap, bool noUnsignedWrap)
        => new(ScevKind.AddRecurrence, start.Bits, 0, null, start, step, noSignedWrap: noSignedWrap, noUnsignedWrap: noUnsignedWrap);

    public bool TryGetConstant(out long value)
    {
        value = Value;
        return Kind == ScevKind.Constant;
    }

    public static Scev? Add(Scev left, Scev right, bool noSignedWrap)
    {
        if (left.Bits != right.Bits)
            return null;
        if (left.TryGetConstant(out var a) && right.TryGetConstant(out var b))
            return Constant(a + b, left.Bits);
        if (left.TryGetConstant(out a) && a == 0)
            return right;
        if (right.TryGetConstant(out b) && b == 0)
            return left;

        if (left.Kind == ScevKind.AddRecurrence && right.Kind == ScevKind.AddRecurrence)
        {
            var start = Add(left.Start, right.Start, noSignedWrap);
            var step = Add(left.Step, right.Step, noSignedWrap);
            return start is null || step is null ? null : AddRecurrence(start, step,
                noSignedWrap && left.NoSignedWrap && right.NoSignedWrap, false);
        }
        if (left.Kind == ScevKind.AddRecurrence || right.Kind == ScevKind.AddRecurrence)
        {
            var recurrence = left.Kind == ScevKind.AddRecurrence ? left : right;
            var other = ReferenceEquals(recurrence, left) ? right : left;
            if (!other.IsInvariant)
                return null;
            var start = Add(recurrence.Start, other, noSignedWrap);
            return start is null ? null : AddRecurrence(start, recurrence.Step, noSignedWrap && recurrence.NoSignedWrap, false);
        }

        if (left.Kind == ScevKind.Constant)
            (left, right) = (right, left);
        if (right.TryGetConstant(out b) && left.Kind == ScevKind.Add && left.Right!.TryGetConstant(out var inner))
            return Add(left.Left!, Constant(inner + b, left.Bits), noSignedWrap);
        return new Scev(ScevKind.Add, left.Bits, 0, null, left, right);
    }

    public static Scev? Multiply(Scev left, Scev right, bool noSignedWrap)
    {
        if (left.Bits != right.Bits)
            return null;
        if (left.TryGetConstant(out var a) && right.TryGetConstant(out var b))
            return Constant(a * b, left.Bits);
        if (left.Kind == ScevKind.Constant)
            (left, right) = (right, left);
        if (right.TryGetConstant(out b))
        {
            if (b == 0)
                return Constant(0, left.Bits);
            if (b == 1)
                return left;
        }

        if (left.Kind == ScevKind.AddRecurrence || right.Kind == ScevKind.AddRecurrence)
        {
            var recurrence = left.Kind == ScevKind.AddRecurrence ? left : right;
            var other = ReferenceEquals(recurrence, left) ? right : left;
            if (!other.IsInvariant)
                return null;
            var start = Multiply(recurrence.Start, other, noSignedWrap);
            var step = Multiply(recurrence.Step, other, noSignedWrap);
            return start is null || step is null ? null : AddRecurrence(start, step, noSignedWrap && recurrence.NoSignedWrap, false);
        }

        return new Scev(ScevKind.Multiply, left.Bits, 0, null, left, right);
    }

    public static Scev? Negate(Scev value, bool noSignedWrap)
        => Multiply(value, Constant(-1, value.Bits), noSignedWrap);

    public static Scev? Extend(Scev value, int bits, bool signExtend)
    {
        if (value.Bits == bits)
            return value;
        if (value.Bits > bits)
            return null;

        switch (value.Kind)
        {
            case ScevKind.Constant:
                return Constant(signExtend ? value.Value : (long)((ulong)value.Value & Mask(value.Bits)), bits);

            case ScevKind.AddRecurrence:
                {
                    // Only a sequence that never wraps in the narrow type reads the same once widened
                    if (signExtend ? !value.NoSignedWrap : !value.NoUnsignedWrap || !value.Step.TryGetConstant(out var step) || step < 0)
                        return null;
                    var start = Extend(value.Start, bits, signExtend);
                    var stride = Extend(value.Step, bits, signExtend);
                    return start is null || stride is null ? null : AddRecurrence(start, stride, value.NoSignedWrap, value.NoUnsignedWrap);
                }

            case ScevKind.Extend when value.SignExtend == signExtend:
                return Extend(value.Left!, bits, signExtend);

            default:
                return value.IsInvariant ? new Scev(ScevKind.Extend, bits, 0, null, value, null, signExtend) : null;
        }
    }

    public bool Contains(GimpleName name)
        => Kind == ScevKind.Invariant ? ReferenceEquals(Name, name) : (Left?.Contains(name) ?? false) || (Right?.Contains(name) ?? false);

    public bool IsEquivalentTo(Scev other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (Kind != other.Kind || Bits != other.Bits || Value != other.Value || !ReferenceEquals(Name, other.Name) || SignExtend != other.SignExtend)
            return false;
        return (Left is null ? other.Left is null : other.Left is not null && Left.IsEquivalentTo(other.Left)) &&
               (Right is null ? other.Right is null : other.Right is not null && Right.IsEquivalentTo(other.Right));
    }

    public void SplitConstant(out Scev? rest, out long constant)
    {
        if (TryGetConstant(out constant))
        {
            rest = null;
            return;
        }
        if (Kind == ScevKind.Add && Right!.TryGetConstant(out constant))
        {
            rest = Left;
            return;
        }
        rest = this;
        constant = 0;
    }

    public static ulong Mask(int bits) => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;

    private static long Wrap(long value, int bits)
        => bits >= 64 ? value : (long)((ulong)value << (64 - bits)) >> (64 - bits);
}

/// <summary>Describes integer values of one natural loop as recurrences over its iterations</summary>
internal sealed class ScalarEvolution
{
    private readonly NaturalLoop _loop;
    private readonly ControlFlowBlock _preheader;
    private readonly ControlFlowBlock _latch;
    private readonly TargetInfo _target;
    private readonly IReadOnlyDictionary<GimpleName, GimpleStatementAnnotations> _definitions;
    private readonly Dictionary<GimpleName, Scev?> _cache = new();
    private readonly HashSet<GimpleName> _pending = new();
    private GimpleCondStatement? _exitTest;
    private bool _exitWhenTrue;
    private bool _unsignedStep;

    public ScalarEvolution(NaturalLoop loop, ControlFlowBlock preheader, ControlFlowBlock latch, TargetInfo target,
        IReadOnlyDictionary<GimpleName, GimpleStatementAnnotations> definitions, GimpleCondStatement? exitTest, bool exitWhenTrue)
    {
        _loop = loop;
        _preheader = preheader;
        _latch = latch;
        _target = target;
        _definitions = definitions;
        _exitTest = exitTest;
        _exitWhenTrue = exitWhenTrue;
    }

    public int IntegerBits(QualifiedType type)
    {
        if (!GimpleTypes.IsIntegerLike(type) || type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Bool })
            return 0;
        var bits = _target.SizeOf(type) * 8;
        return bits is 32 or 64 ? bits : 0;
    }

    public bool IsLoopInvariant(GimpleName name)
        => name.Definition is { } definition && !name.IsUndefined &&
           (definition.Kind == GimpleDefinitionKind.Entry || definition.Block is not null && !_loop.Blocks.Contains(definition.Block));

    public Scev? Analyze(GimpleValue value)
    {
        switch (value)
        {
            case GimpleConstantValue constant:
                var bits = IntegerBits(constant.Type);
                return bits != 0 && TryGetInteger(constant.Value, out var number) ? Scev.Constant(number, bits) : null;
            case GimpleName name:
                return Analyze(name);
            default:
                return null;
        }
    }

    public Scev? Analyze(GimpleName name)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;
        if (_pending.Contains(name))
            return null;

        _pending.Add(name);
        var result = Compute(name);
        _pending.Remove(name);
        _cache[name] = result;
        return result;
    }

    private Scev? Compute(GimpleName name)
    {
        var bits = IntegerBits(name.Type);
        if (bits == 0 || name.IsUndefined || name.Definition is not { } definition)
            return null;

        if (IsLoopInvariant(name))
        {
            if (definition.Kind == GimpleDefinitionKind.Statement &&
                definition.Statement is GimpleAssignStatement { RhsClass: GimpleRhsClass.Single, Operands: [GimpleConstantValue constant] } &&
                TryGetInteger(constant.Value, out var number))
                return Scev.Constant(number, bits);
            return Scev.Invariant(name, bits);
        }

        if (definition.Kind == GimpleDefinitionKind.Phi)
            return ReferenceEquals(definition.Block, _loop.Header) && definition.Statement is GimplePhi phi ? AnalyzePhi(name, phi, bits) : null;

        if (definition.Kind != GimpleDefinitionKind.Statement || definition.Statement is not GimpleAssignStatement assign ||
            !_definitions.TryGetValue(name, out var instruction) || (instruction.Flags & GimpleStatementFlags.ReadsMemory) != 0)
            return null;

        var signed = GimpleTypes.IsSigned(name.Type);
        switch (assign.Subcode)
        {
            case GimpleTreeCode.SsaName:
            case GimpleTreeCode.IntegerCst:
                return assign.Operands.Length == 1 ? Analyze(assign.Operands[0]) : null;

            case GimpleTreeCode.PlusExpr:
            case GimpleTreeCode.MinusExpr:
            case GimpleTreeCode.MultExpr:
                {
                    if (assign.Operands.Length != 2 || Analyze(assign.Operands[0]) is not { } left || Analyze(assign.Operands[1]) is not { } right)
                        return null;
                    if (!signed)
                        _unsignedStep = true;
                    return assign.Subcode switch
                    {
                        GimpleTreeCode.PlusExpr => Scev.Add(left, right, signed),
                        GimpleTreeCode.MinusExpr => Scev.Negate(right, signed) is { } negated ? Scev.Add(left, negated, signed) : null,
                        _ => Scev.Multiply(left, right, signed),
                    };
                }

            case GimpleTreeCode.LshiftExpr:
                {
                    if (assign.Operands.Length != 2 || Analyze(assign.Operands[0]) is not { } left ||
                        assign.Operands[1] is not GimpleConstantValue { Value: var amount } || !TryGetInteger(amount, out var shift) ||
                        shift < 0 || shift >= bits - 1)
                        return null;
                    if (!signed)
                        _unsignedStep = true;
                    return Scev.Multiply(left, Scev.Constant(1L << (int)shift, bits), signed);
                }

            case GimpleTreeCode.NegateExpr:
                return assign.Operands.Length == 1 && Analyze(assign.Operands[0]) is { } operandValue ? Scev.Negate(operandValue, signed) : null;

            case GimpleTreeCode.NopExpr:
            case GimpleTreeCode.ConvertExpr:
                {
                    if (assign.Operands.Length != 1 || Analyze(assign.Operands[0]) is not { } operand)
                        return null;
                    var sourceBits = IntegerBits(assign.Operands[0].Type);
                    if (sourceBits == bits)
                        return operand;
                    return sourceBits < bits ? Scev.Extend(operand, bits, GimpleTypes.IsSigned(assign.Operands[0].Type)) : null;
                }

            default:
                return null;
        }
    }

    private Scev? AnalyzePhi(GimpleName name, GimplePhi phi, int bits)
    {
        if (phi.Operands.Length != 2)
            return null;

        GimpleName? initial = null;
        GimpleName? next = null;
        foreach (var operand in phi.Operands)
        {
            if (ReferenceEquals(operand.Predecessor, _preheader))
                initial = operand.Value;
            else if (ReferenceEquals(operand.Predecessor, _latch))
                next = operand.Value;
        }
        if (initial is null || next is null || Analyze(initial) is not { IsInvariant: true } start)
            return null;

        // Descriptions made against the placeholder are provisional and must not outlive it
        var placeholder = Scev.Invariant(name, bits);
        var saved = new Dictionary<GimpleName, Scev?>(_cache);
        _cache[name] = placeholder;
        _unsignedStep = false;
        var advanced = Analyze(next);
        var unsignedStep = _unsignedStep;
        _cache.Clear();
        foreach (var pair in saved)
            _cache[pair.Key] = pair.Value;

        if (advanced is null || !TryExtractStep(advanced, name, out var step))
            return null;

        var noSignedWrap = GimpleTypes.IsSigned(name.Type) && !unsignedStep;
        var noUnsignedWrap = !GimpleTypes.IsSigned(name.Type) && step.TryGetConstant(out var stride) && stride == 1 && IsBoundedAbove(name);
        return Scev.AddRecurrence(start, step, noSignedWrap, noUnsignedWrap);
    }

    private static bool TryExtractStep(Scev advanced, GimpleName phi, out Scev step)
    {
        step = null!;
        var terms = new List<Scev>();
        Flatten(advanced, terms);
        Scev? rest = null;
        var found = false;
        foreach (var term in terms)
        {
            if (term.Kind == ScevKind.Invariant && ReferenceEquals(term.Name, phi))
            {
                if (found)
                    return false;
                found = true;
                continue;
            }
            if (!term.IsInvariant || term.Contains(phi))
                return false;
            rest = rest is null ? term : Scev.Add(rest, term, false);
            if (rest is null)
                return false;
        }

        if (!found || rest is null)
            return false;
        step = rest;
        return true;

        static void Flatten(Scev value, List<Scev> terms)
        {
            if (value.Kind == ScevKind.Add)
            {
                Flatten(value.Left!, terms);
                Flatten(value.Right!, terms);
                return;
            }
            terms.Add(value);
        }
    }

    // i < n with a unit step stops at n, before i can wrap
    private bool IsBoundedAbove(GimpleName phi)
    {
        if (_exitTest is null)
            return false;
        var code = _exitWhenTrue ? Invert(_exitTest.Code) : _exitTest.Code;
        return code == GimpleTreeCode.LtExpr && ReferenceEquals(_exitTest.Lhs, phi) && IsInvariantOperand(_exitTest.Rhs) ||
               code == GimpleTreeCode.GtExpr && ReferenceEquals(_exitTest.Rhs, phi) && IsInvariantOperand(_exitTest.Lhs);
    }

    private bool IsInvariantOperand(GimpleValue value)
        => value is GimpleConstantValue || value is GimpleName name && IsLoopInvariant(name);

    public static GimpleTreeCode Invert(GimpleTreeCode code) => code switch
    {
        GimpleTreeCode.LtExpr => GimpleTreeCode.GeExpr,
        GimpleTreeCode.LeExpr => GimpleTreeCode.GtExpr,
        GimpleTreeCode.GtExpr => GimpleTreeCode.LeExpr,
        GimpleTreeCode.GeExpr => GimpleTreeCode.LtExpr,
        GimpleTreeCode.EqExpr => GimpleTreeCode.NeExpr,
        GimpleTreeCode.NeExpr => GimpleTreeCode.EqExpr,
        _ => GimpleTreeCode.None,
    };

    public static bool TryGetInteger(object? value, out long number)
    {
        switch (value)
        {
            case int v: number = v; return true;
            case long v: number = v; return true;
            case short v: number = v; return true;
            case sbyte v: number = v; return true;
            case byte v: number = v; return true;
            case ushort v: number = v; return true;
            case uint v: number = v; return true;
            case ulong v: number = unchecked((long)v); return true;
            case char v: number = v; return true;
            default: number = 0; return false;
        }
    }
}
