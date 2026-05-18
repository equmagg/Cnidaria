using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cnidaria.Cs
{
    internal readonly struct ValueNumber : IEquatable<ValueNumber>, IComparable<ValueNumber>
    {
        public readonly int Id;

        public ValueNumber(int id)
        {
            Id = id;
        }

        public bool IsValid => Id > 0;
        public bool Equals(ValueNumber other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is ValueNumber other && Equals(other);
        public override int GetHashCode() => Id;
        public int CompareTo(ValueNumber other) => Id.CompareTo(other.Id);
        public override string ToString() => IsValid ? "$" + Id.ToString("x") : "$NoVN";

        public static bool operator ==(ValueNumber left, ValueNumber right) => left.Id == right.Id;
        public static bool operator !=(ValueNumber left, ValueNumber right) => left.Id != right.Id;
    }

    internal readonly struct ValueNumberPair : IEquatable<ValueNumberPair>
    {
        public readonly ValueNumber Liberal;
        public readonly ValueNumber Conservative;

        public ValueNumberPair(ValueNumber liberal, ValueNumber conservative)
        {
            Liberal = liberal;
            Conservative = conservative;
        }

        public static ValueNumberPair Same(ValueNumber value) => new ValueNumberPair(value, value);
        public bool BothEqual => Liberal == Conservative;
        public ValueNumber this[ValueNumberCategory category] => category == ValueNumberCategory.Liberal ? Liberal : Conservative;
        public bool Equals(ValueNumberPair other) => Liberal == other.Liberal && Conservative == other.Conservative;
        public override bool Equals(object? obj) => obj is ValueNumberPair other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Liberal, Conservative);
        public override string ToString() => BothEqual ? Liberal.ToString() : Liberal.ToString() + "/" + Conservative.ToString();
    }

    internal enum ValueNumberCategory : byte
    {
        Liberal,
        Conservative,
    }

    internal readonly struct ValueNumberType : IEquatable<ValueNumberType>
    {
        public readonly GenStackKind StackKind;
        public readonly int RuntimeTypeId;
        public readonly RuntimeType? RuntimeType;

        private const int CanonicalValueTypeTag = unchecked((int)0x80000000);

        private ValueNumberType(GenStackKind stackKind, RuntimeType? runtimeType)
        {
            StackKind = stackKind;
            RuntimeType = runtimeType;
            RuntimeTypeId = CanonicalRuntimeTypeId(runtimeType);
        }

        public static ValueNumberType For(GenStackKind stackKind, RuntimeType? runtimeType)
            => new ValueNumberType(stackKind, runtimeType);

        private static int CanonicalRuntimeTypeId(RuntimeType? runtimeType)
        {
            if (runtimeType is null)
                return 0;

            if (runtimeType.IsValueType)
                return CanonicalValueTypeTag ^ Math.Max(1, runtimeType.SizeOf);

            return runtimeType.TypeId;
        }

        public bool Equals(ValueNumberType other)
            => StackKind == other.StackKind && RuntimeTypeId == other.RuntimeTypeId;

        public override bool Equals(object? obj) => obj is ValueNumberType other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((int)StackKind, RuntimeTypeId);

        public override string ToString()
        {
            if (RuntimeType is null)
                return StackKind.ToString();
            if (RuntimeType.IsValueType)
                return StackKind.ToString() + ":struct-by-size(" + Math.Max(1, RuntimeType.SizeOf).ToString() + ")";
            return StackKind.ToString() + ":" + RuntimeType.Name;
        }
    }

    internal enum ValueNumberKind : byte
    {
        Invalid,
        Constant,
        Function,
        Phi,
        MemoryPhi,
        Unique,
    }

    internal enum ValueNumberConstantKind : byte
    {
        Int32,
        Int64,
        Float32Bits,
        Float64Bits,
        Null,
        Void,
        EmptyExceptionSet,
        String,
        TypeHandle,
        CanonicalTypeHandle,
        FieldHandle,
        FieldSequence,
        MethodHandle,
        SsaSlot,
        PhysicalSelector,
        ArrayElementClass,
        MethodBody,
        Block,
        Heap,
    }

    internal enum ValueNumberFunction : ushort
    {
        None = 0,

        InitVal,
        PhiDef,
        PhiMemoryDef,
        MemOpaque,
        MapSelect,
        MapStore,
        MapPhysicalStore,
        BitCast,
        ZeroObj,
        PtrToLoc,
        PtrToArrElem,
        PtrToStatic,
        MDArrLength,
        MDArrLowerBound,
        Cast,
        CastOvf,
        CastClass,
        IsInstanceOf,
        LdElemA,
        ByrefExposedLoad,
        ValWithExc,
        ExcSetCons,
        NullPtrExc,
        ArithmeticExc,
        OverflowExc,
        ConvOverflowExc,
        DivideByZeroExc,
        IndexOutOfRangeExc,
        InvalidCastExc,
        NewArrOverflowExc,
        HelperOpaqueExc,

        Add,
        Sub,
        Mul,
        Div,
        DivUn,
        Rem,
        RemUn,
        And,
        Or,
        Xor,
        Shl,
        Shr,
        ShrUn,
        Neg,
        Not,
        Ceq,
        Clt,
        CltUn,
        Cgt,
        CgtUn,
        Conv,
        SizeOf,
        FieldAddr,
        StaticFieldAddr,
        ArrayLength,
        ArrayElementAddr,
        ArrayDataRef,
        StackAlloc,
        PointerElementAddr,
        PointerToByRef,
        PointerDiff,
        Box,
        UnboxAny,
        NewObject,
        NewArray,
        Call,
        VirtualCall,
        ExceptionObject,
        DefaultValue,
    }

    internal enum ValueNumberFieldSequenceKind : byte
    {
        Instance,
        Static,
    }

    internal readonly struct ValueNumberFieldSegment : IEquatable<ValueNumberFieldSegment>
    {
        public readonly RuntimeField Field;
        public readonly int Offset;
        public readonly int Size;

        public ValueNumberFieldSegment(RuntimeField field)
            : this(field, field?.Offset ?? 0, field is null ? 0 : Math.Max(1, field.FieldType.SizeOf))
        {
        }

        public ValueNumberFieldSegment(RuntimeField field, int offset, int size)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Offset = offset;
            Size = Math.Max(1, size);
        }

        public bool Equals(ValueNumberFieldSegment other)
            => Field.FieldId == other.Field.FieldId && Offset == other.Offset && Size == other.Size;

        public override bool Equals(object? obj) => obj is ValueNumberFieldSegment other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Field.FieldId, Offset, Size);

        public override string ToString()
            => Field.DeclaringType.Name + "." + Field.Name + "+" + Offset.ToString() + ":" + Size.ToString();
    }

    internal sealed class ValueNumberFieldSequence : IEquatable<ValueNumberFieldSequence>
    {
        public ValueNumberFieldSequenceKind Kind { get; }
        public ImmutableArray<ValueNumberFieldSegment> Segments { get; }
        private readonly int _hashCode;

        private ValueNumberFieldSequence(ValueNumberFieldSequenceKind kind, ImmutableArray<ValueNumberFieldSegment> segments)
        {
            if (segments.IsDefaultOrEmpty)
                throw new ArgumentException("A field sequence must contain at least one field.", nameof(segments));

            Kind = kind;
            Segments = segments;
            _hashCode = ComputeHashCode(kind, segments);
        }

        public static ValueNumberFieldSequence Create(RuntimeField field, ValueNumberFieldSequenceKind kind)
            => new ValueNumberFieldSequence(kind, ImmutableArray.Create(new ValueNumberFieldSegment(field)));

        public ValueNumberFieldSequence Append(RuntimeField field)
        {
            if (field is null)
                throw new ArgumentNullException(nameof(field));

            var builder = Segments.ToBuilder();
            builder.Add(new ValueNumberFieldSegment(field));
            return new ValueNumberFieldSequence(Kind, builder.ToImmutable());
        }

        public ValueNumberFieldSequence Primary()
            => Segments.Length == 1
                ? this
                : new ValueNumberFieldSequence(Kind, ImmutableArray.Create(Segments[0]));

        public RuntimeField FirstField => Segments[0].Field;
        public RuntimeField LastField => Segments[Segments.Length - 1].Field;
        public int StableHashCode => _hashCode;

        public int OffsetWithinPrimary
        {
            get
            {
                int offset = 0;
                for (int i = 1; i < Segments.Length; i++)
                    offset = checked(offset + Segments[i].Offset);
                return offset;
            }
        }

        public int TotalOffset
        {
            get
            {
                int offset = 0;
                for (int i = 0; i < Segments.Length; i++)
                    offset = checked(offset + Segments[i].Offset);
                return offset;
            }
        }

        public bool Equals(ValueNumberFieldSequence? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || Kind != other.Kind || Segments.Length != other.Segments.Length) return false;
            for (int i = 0; i < Segments.Length; i++)
            {
                if (!Segments[i].Equals(other.Segments[i]))
                    return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is ValueNumberFieldSequence other && Equals(other);

        public override int GetHashCode() => _hashCode;

        private static int ComputeHashCode(ValueNumberFieldSequenceKind kind, ImmutableArray<ValueNumberFieldSegment> segments)
        {
            unchecked
            {
                int hash = (int)kind;
                for (int i = 0; i < segments.Length; i++)
                    hash = (hash * 397) ^ segments[i].GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Kind == ValueNumberFieldSequenceKind.Static ? "static:" : "inst:");
            for (int i = 0; i < Segments.Length; i++)
            {
                if (i != 0)
                    sb.Append('.');
                sb.Append(Segments[i].Field.Name);
                sb.Append('@').Append(Segments[i].Offset);
            }
            return sb.ToString();
        }
    }

    internal readonly struct ValueNumberConstantKey : IEquatable<ValueNumberConstantKey>, IComparable<ValueNumberConstantKey>
    {
        public readonly ValueNumberConstantKind Kind;
        public readonly GenStackKind StackKind;
        public readonly RuntimeType? RuntimeType;
        public readonly long A;
        public readonly long B;
        public readonly object? Object;

        private ValueNumberConstantKey(ValueNumberConstantKind kind, GenStackKind stackKind, RuntimeType? type, long a, long b, object? obj)
        {
            Kind = kind;
            StackKind = stackKind;
            RuntimeType = type;
            A = a;
            B = b;
            Object = obj;
        }

        public static ValueNumberConstantKey Int32(int value) => new ValueNumberConstantKey(ValueNumberConstantKind.Int32, GenStackKind.I4, null, value, 0, null);
        public static ValueNumberConstantKey Int64(long value) => new ValueNumberConstantKey(ValueNumberConstantKind.Int64, GenStackKind.I8, null, value, 0, null);
        public static ValueNumberConstantKey Float32Bits(int bits) => new ValueNumberConstantKey(ValueNumberConstantKind.Float32Bits, GenStackKind.R4, null, bits, 0, null);
        public static ValueNumberConstantKey Float64Bits(long bits) => new ValueNumberConstantKey(ValueNumberConstantKind.Float64Bits, GenStackKind.R8, null, bits, 0, null);
        public static ValueNumberConstantKey Null(RuntimeType? type) => new ValueNumberConstantKey(ValueNumberConstantKind.Null, GenStackKind.Null, type, type?.TypeId ?? 0, 0, null);
        public static ValueNumberConstantKey Void() => new ValueNumberConstantKey(ValueNumberConstantKind.Void, GenStackKind.Void, null, 0, 0, null);
        public static ValueNumberConstantKey EmptyExceptionSet() => new ValueNumberConstantKey(ValueNumberConstantKind.EmptyExceptionSet, GenStackKind.Unknown, null, 0, 0, null);
        public static ValueNumberConstantKey String(string? value) => new ValueNumberConstantKey(ValueNumberConstantKind.String, GenStackKind.Ref, null, 0, 0, value ?? string.Empty);
        public static ValueNumberConstantKey TypeHandle(RuntimeType? type) => new ValueNumberConstantKey(ValueNumberConstantKind.TypeHandle, GenStackKind.NativeInt, type, type?.TypeId ?? 0, 0, null);
        public static ValueNumberConstantKey CanonicalType(RuntimeType? type)
        {
            if (type is null)
                return new ValueNumberConstantKey(ValueNumberConstantKind.CanonicalTypeHandle, GenStackKind.NativeInt, null, 0, 0, null);
            if (type.IsValueType)
                return new ValueNumberConstantKey(ValueNumberConstantKind.CanonicalTypeHandle, GenStackKind.NativeInt, null, Math.Max(1, type.SizeOf), 0, null);
            return new ValueNumberConstantKey(ValueNumberConstantKind.CanonicalTypeHandle, GenStackKind.NativeInt, type, type.TypeId, 0, null);
        }

        public static ValueNumberConstantKey Field(RuntimeField field) => new ValueNumberConstantKey(ValueNumberConstantKind.FieldHandle, GenStackKind.NativeInt, field.DeclaringType, field.FieldId, field.Offset, field);
        public static ValueNumberConstantKey FieldSequence(ValueNumberFieldSequence sequence)
        {
            if (sequence is null) throw new ArgumentNullException(nameof(sequence));
            return new ValueNumberConstantKey(
                ValueNumberConstantKind.FieldSequence,
                GenStackKind.NativeInt,
                sequence.FirstField.DeclaringType,
                sequence.FirstField.FieldId,
                ((long)(byte)sequence.Kind << 56) | (uint)sequence.StableHashCode,
                sequence);
        }
        public static ValueNumberConstantKey Method(RuntimeMethod method) => new ValueNumberConstantKey(ValueNumberConstantKind.MethodHandle, GenStackKind.NativeInt, method.DeclaringType, method.MethodId, 0, method);
        public static ValueNumberConstantKey Slot(SsaSlot slot)
            => new ValueNumberConstantKey(
                ValueNumberConstantKind.SsaSlot,
                GenStackKind.NativeInt,
                null,
                slot.HasLclNum ? slot.LclNum : (int)slot.Kind,
                slot.HasLclNum ? 0 : slot.Index,
                null);
        public static ValueNumberConstantKey PhysicalSelector(int offset, int size) => new ValueNumberConstantKey(ValueNumberConstantKind.PhysicalSelector, GenStackKind.NativeInt, null, offset, size, null);
        public static ValueNumberConstantKey ArrayElementClass(int equivalenceClass, int size, int exactTypeId)
            => new ValueNumberConstantKey(
                ValueNumberConstantKind.ArrayElementClass,
                GenStackKind.NativeInt,
                null,
                equivalenceClass,
                ((long)Math.Max(0, Math.Min(size, 0x7fffffff)) << 32) | (uint)exactTypeId,
                null);
        public static ValueNumberConstantKey MethodBody(RuntimeMethod method) => new ValueNumberConstantKey(ValueNumberConstantKind.MethodBody, GenStackKind.NativeInt, method.DeclaringType, method.MethodId, method.Body?.GetHashCode() ?? 0, method);
        public static ValueNumberConstantKey Block(int blockId) => new ValueNumberConstantKey(ValueNumberConstantKind.Block, GenStackKind.I4, null, blockId, 0, null);
        public static ValueNumberConstantKey Heap(RuntimeMethod method) => new ValueNumberConstantKey(ValueNumberConstantKind.Heap, GenStackKind.Unknown, method.DeclaringType, method.MethodId, 0, method);

        public bool Equals(ValueNumberConstantKey other)
        {
            return Kind == other.Kind &&
                   StackKind == other.StackKind &&
                   SameRuntimeType(RuntimeType, other.RuntimeType) &&
                   A == other.A &&
                   B == other.B &&
                   Equals(Object, other.Object);
        }

        public override bool Equals(object? obj) => obj is ValueNumberConstantKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (int)StackKind;
                hash = (hash * 397) ^ (RuntimeType?.TypeId ?? 0);
                hash = (hash * 397) ^ A.GetHashCode();
                hash = (hash * 397) ^ B.GetHashCode();
                hash = (hash * 397) ^ (Object?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public int CompareTo(ValueNumberConstantKey other)
        {
            int c = Kind.CompareTo(other.Kind);
            if (c != 0) return c;
            c = StackKind.CompareTo(other.StackKind);
            if (c != 0) return c;
            c = (RuntimeType?.TypeId ?? 0).CompareTo(other.RuntimeType?.TypeId ?? 0);
            if (c != 0) return c;
            c = A.CompareTo(other.A);
            if (c != 0) return c;
            return B.CompareTo(other.B);
        }

        private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.TypeId == right.TypeId;
        }

        public override string ToString()
        {
            return Kind switch
            {
                ValueNumberConstantKind.Int32 => A.ToString(),
                ValueNumberConstantKind.Int64 => A.ToString() + "L",
                ValueNumberConstantKind.Float64Bits => "r8bits(" + A.ToString("x") + ")",
                ValueNumberConstantKind.Null => "null",
                ValueNumberConstantKind.Void => "void",
                ValueNumberConstantKind.EmptyExceptionSet => "empty-exc-set",
                ValueNumberConstantKind.String => "str(" + Object + ")",
                ValueNumberConstantKind.TypeHandle => "type(" + (RuntimeType?.Name ?? A.ToString()) + ")",
                ValueNumberConstantKind.CanonicalTypeHandle => RuntimeType is null ? "vntype(size=" + A.ToString() + ")" : "vntype(" + RuntimeType.Name + ")",
                ValueNumberConstantKind.FieldHandle => "field(" + (Object is RuntimeField f ? f.Name : A.ToString()) + ")",
                ValueNumberConstantKind.FieldSequence => "fieldseq(" + (Object is ValueNumberFieldSequence fs ? fs.ToString() : A.ToString()) + ")",
                ValueNumberConstantKind.MethodHandle => "method(" + (Object is RuntimeMethod m ? m.Name : A.ToString()) + ")",
                ValueNumberConstantKind.SsaSlot => "slot(" + ((SsaSlotKind)A).ToString() + ":" + B.ToString() + ")",
                ValueNumberConstantKind.PhysicalSelector => "phys(" + A.ToString() + ":" + B.ToString() + ")",
                ValueNumberConstantKind.ArrayElementClass => "arrclass(" + A.ToString() + ":" + (B >> 32).ToString() + ":" + ((int)B).ToString() + ")",
                ValueNumberConstantKind.Block => "B" + A.ToString(),
                ValueNumberConstantKind.Heap => "heap(" + A.ToString() + ")",
                _ => Kind.ToString() + "(" + A.ToString() + "," + B.ToString() + ")",
            };
        }
    }

    internal sealed class ValueNumberEntry
    {
        public ValueNumberKind Kind { get; }
        public GenStackKind StackKind { get; }
        public RuntimeType? Type { get; }
        public ValueNumberType TypeKey { get; }
        public ValueNumberConstantKey Constant { get; }
        public ValueNumberFunction Function { get; }
        public ImmutableArray<ValueNumber> Args { get; private set; }
        public int StableId { get; }

        public ValueNumberEntry(ValueNumberKind kind, GenStackKind stackKind, RuntimeType? type, ValueNumberConstantKey constant, ValueNumberFunction function, ImmutableArray<ValueNumber> args, int stableId)
        {
            Kind = kind;
            StackKind = stackKind;
            Type = type;
            TypeKey = ValueNumberType.For(stackKind, type);
            Constant = constant;
            Function = function;
            Args = args.IsDefault ? ImmutableArray<ValueNumber>.Empty : args;
            StableId = stableId;
        }

        internal void SetArgs(ImmutableArray<ValueNumber> args)
        {
            Args = args.IsDefault ? ImmutableArray<ValueNumber>.Empty : args;
        }
    }

    internal readonly struct ValueNumberFuncKey : IEquatable<ValueNumberFuncKey>
    {
        public readonly ValueNumberFunction Function;
        public readonly ValueNumberType TypeKey;
        public readonly ImmutableArray<ValueNumber> Args;

        public ValueNumberFuncKey(ValueNumberFunction function, GenStackKind stackKind, RuntimeType? type, ImmutableArray<ValueNumber> args)
        {
            Function = function;
            TypeKey = ValueNumberType.For(stackKind, type);
            Args = args.IsDefault ? ImmutableArray<ValueNumber>.Empty : args;
        }

        public bool Equals(ValueNumberFuncKey other)
        {
            if (Function != other.Function || !TypeKey.Equals(other.TypeKey) || Args.Length != other.Args.Length)
                return false;
            for (int i = 0; i < Args.Length; i++)
            {
                if (Args[i] != other.Args[i]) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is ValueNumberFuncKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Function;
                hash = (hash * 397) ^ TypeKey.GetHashCode();
                for (int i = 0; i < Args.Length; i++)
                    hash = (hash * 397) ^ Args[i].Id;
                return hash;
            }
        }

        private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.IsValueType || right.IsValueType)
                return left.IsValueType && right.IsValueType && Math.Max(1, left.SizeOf) == Math.Max(1, right.SizeOf);
            return left.TypeId == right.TypeId;
        }
    }


    internal readonly struct ValueNumberStableUniqueKey : IEquatable<ValueNumberStableUniqueKey>
    {
        public readonly int StableId;
        public readonly ValueNumberFunction Function;
        public readonly ValueNumberType TypeKey;
        public readonly ImmutableArray<ValueNumber> Args;

        public ValueNumberStableUniqueKey(int stableId, ValueNumberFunction function, GenStackKind stackKind, RuntimeType? type, ImmutableArray<ValueNumber> args)
        {
            StableId = stableId;
            Function = function;
            TypeKey = ValueNumberType.For(stackKind, type);
            Args = args.IsDefault ? ImmutableArray<ValueNumber>.Empty : args;
        }

        public bool Equals(ValueNumberStableUniqueKey other)
        {
            if (StableId != other.StableId || Function != other.Function || !TypeKey.Equals(other.TypeKey) || Args.Length != other.Args.Length)
                return false;

            for (int i = 0; i < Args.Length; i++)
            {
                if (Args[i] != other.Args[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ValueNumberStableUniqueKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StableId;
                hash = (hash * 397) ^ (int)Function;
                hash = (hash * 397) ^ TypeKey.GetHashCode();
                for (int i = 0; i < Args.Length; i++)
                    hash = (hash * 397) ^ Args[i].Id;
                return hash;
            }
        }

        private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.IsValueType || right.IsValueType)
                return left.IsValueType && right.IsValueType && Math.Max(1, left.SizeOf) == Math.Max(1, right.SizeOf);
            return left.TypeId == right.TypeId;
        }
    }

    internal sealed class ValueNumberStore
    {
        public static readonly ValueNumber NoVN = new ValueNumber(0);
        public static readonly ValueNumber RecursiveVN = new ValueNumber(-1);
        public const int DefaultMapSelectBudget = 100;

        private readonly Dictionary<ValueNumberConstantKey, ValueNumber> _constants = new();
        private readonly Dictionary<ValueNumberFuncKey, ValueNumber> _functions = new();
        private readonly Dictionary<ValueNumberStableUniqueKey, ValueNumber> _stableUniqueFunctions = new();
        private readonly Dictionary<(SsaValueName target, ValueNumberCategory category, ValueNumberFunction function), ValueNumber> _stableSsaPhis = new();
        private readonly Dictionary<(int blockId, int memoryKind, ValueNumberFunction function), ValueNumber> _stableMemoryPhis = new();
        private readonly Dictionary<int, ValueNumberEntry> _entries = new();
        private readonly Dictionary<ValueNumberFuncKey, MapSelectWorkCacheEntry> _mapSelectCache = new();
        private int _nextId = 1;

        public ValueNumber InitialHeap(RuntimeMethod method)
            => VNForConstant(ValueNumberConstantKey.Heap(method));

        public ValueNumber InitialByrefExposed(RuntimeMethod method)
            => VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.InitVal, VNForMethod(method), VNForInt32(-3));

        public ValueNumber VNForConstant(ValueNumberConstantKey key)
        {
            if (_constants.TryGetValue(key, out var existing))
                return existing;

            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Constant, key.StackKind, key.RuntimeType, key, ValueNumberFunction.None, ImmutableArray<ValueNumber>.Empty, stableId: 0));
            _constants.Add(key, vn);
            return vn;
        }

        public ValueNumber VNForInt32(int value) => VNForConstant(ValueNumberConstantKey.Int32(value));
        public ValueNumber VNForInt64(long value) => VNForConstant(ValueNumberConstantKey.Int64(value));
        public ValueNumber VNForFloat32Bits(int bits) => VNForConstant(ValueNumberConstantKey.Float32Bits(bits));
        public ValueNumber VNForFloat64Bits(long bits) => VNForConstant(ValueNumberConstantKey.Float64Bits(bits));
        public ValueNumber VNForNull(RuntimeType? type) => VNForConstant(ValueNumberConstantKey.Null(type));
        public ValueNumber VNForVoid() => VNForConstant(ValueNumberConstantKey.Void());
        public ValueNumber VNForEmptyExcSet() => VNForConstant(ValueNumberConstantKey.EmptyExceptionSet());
        public ValueNumber VNForType(RuntimeType? type) => VNForConstant(ValueNumberConstantKey.TypeHandle(type));
        public ValueNumber VNForCanonicalType(RuntimeType? type) => VNForConstant(ValueNumberConstantKey.CanonicalType(type));
        public ValueNumber VNForField(RuntimeField field) => VNForConstant(ValueNumberConstantKey.Field(field));
        public ValueNumber VNForFieldSequence(ValueNumberFieldSequence sequence) => VNForConstant(ValueNumberConstantKey.FieldSequence(sequence));
        public ValueNumber VNForMethod(RuntimeMethod method) => VNForConstant(ValueNumberConstantKey.Method(method));
        public ValueNumber VNForSlot(SsaSlot slot) => VNForConstant(ValueNumberConstantKey.Slot(slot));
        public ValueNumber VNForBlock(int blockId) => VNForConstant(ValueNumberConstantKey.Block(blockId));
        public ValueNumber VNForPhysicalSelector(int offset, int size) => VNForConstant(ValueNumberConstantKey.PhysicalSelector(offset, size));
        public ValueNumber VNForArrayElementClass(int equivalenceClass, int size, int exactTypeId)
            => VNForConstant(ValueNumberConstantKey.ArrayElementClass(equivalenceClass, size, exactTypeId));

        private static void ValidateFunctionArity(ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            int expected = function switch
            {
                ValueNumberFunction.MemOpaque => 1,
                ValueNumberFunction.MapStore => 4,
                ValueNumberFunction.MapPhysicalStore => 3,
                ValueNumberFunction.ValWithExc => 2,
                ValueNumberFunction.ExcSetCons => 2,
                ValueNumberFunction.NullPtrExc => 1,
                ValueNumberFunction.OverflowExc => 1,
                ValueNumberFunction.DivideByZeroExc => 1,
                ValueNumberFunction.NewArrOverflowExc => 1,
                ValueNumberFunction.HelperOpaqueExc => 1,
                ValueNumberFunction.ArithmeticExc => 2,
                ValueNumberFunction.ConvOverflowExc => 2,
                ValueNumberFunction.IndexOutOfRangeExc => 2,
                ValueNumberFunction.InvalidCastExc => 2,
                _ => -1,
            };

            if (expected >= 0 && args.Length != expected)
                throw new ArgumentException(function.ToString() + " requires exactly " + expected.ToString() + " argument(s).", nameof(args));
        }

        public ValueNumber VNForUnique(GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefault)
                args = ImmutableArray<ValueNumber>.Empty;
            ValidateFunctionArity(function, args);
            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Unique, stackKind, type, default, function, args, stableId: 0));
            return vn;
        }

        public ValueNumber VNForStableUnique(int stableId, GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefault)
                args = ImmutableArray<ValueNumber>.Empty;
            ValidateFunctionArity(function, args);

            var key = new ValueNumberStableUniqueKey(stableId, function, stackKind, type, args);
            if (_stableUniqueFunctions.TryGetValue(key, out var existing))
                return existing;

            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Unique, stackKind, type, default, function, args, stableId));
            _stableUniqueFunctions.Add(key, vn);
            return vn;
        }

        public ValueNumber VNForSsaPhi(SsaValueName target, ImmutableArray<ValueNumber> inputVNs, GenStackKind stackKind, RuntimeType? type)
            => VNForSsaPhi(target, ValueNumberCategory.Liberal, inputVNs, stackKind, type);

        public ValueNumber VNForSsaPhi(SsaValueName target, ValueNumberCategory category, ImmutableArray<ValueNumber> inputVNs, GenStackKind stackKind, RuntimeType? type)
        {
            ValueNumber reduced = TryReducePhi(inputVNs);
            if (reduced.IsValid)
                return reduced;

            var key = (target, category, ValueNumberFunction.PhiDef);
            if (!_stableSsaPhis.TryGetValue(key, out var vn))
            {
                int stableId = HashCode.Combine(StableIdFor(target), (int)category);
                vn = Allocate(new ValueNumberEntry(ValueNumberKind.Phi, stackKind, type, default, ValueNumberFunction.PhiDef, NormalizePhiArgs(inputVNs), stableId));
                _stableSsaPhis.Add(key, vn);
            }
            else
            {
                _entries[vn.Id].SetArgs(NormalizePhiArgs(inputVNs));
            }
            return vn;
        }

        public ValueNumber VNForMemoryPhi(int blockId, ImmutableArray<ValueNumber> inputVNs)
            => VNForMemoryPhi(blockId, 0, inputVNs);

        public ValueNumber VNForMemoryPhi(int blockId, int memoryKind, ImmutableArray<ValueNumber> inputVNs)
        {
            ValueNumber reduced = TryReducePhi(inputVNs);
            if (reduced.IsValid)
                return reduced;

            var key = (blockId, memoryKind, ValueNumberFunction.PhiMemoryDef);
            if (!_stableMemoryPhis.TryGetValue(key, out var vn))
            {
                int stableId = HashCode.Combine(blockId, memoryKind);
                vn = Allocate(new ValueNumberEntry(ValueNumberKind.MemoryPhi, GenStackKind.Unknown, null, default, ValueNumberFunction.PhiMemoryDef, NormalizePhiArgs(inputVNs), stableId));
                _stableMemoryPhis.Add(key, vn);
            }
            else
            {
                _entries[vn.Id].SetArgs(NormalizePhiArgs(inputVNs));
            }
            return vn;
        }

        public ValueNumber VNForFunc(GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, params ValueNumber[] args)
            => VNForFunc(stackKind, type, function, args is null || args.Length == 0 ? ImmutableArray<ValueNumber>.Empty : ImmutableArray.Create(args));

        public ValueNumber VNForFunc(GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefault)
                args = ImmutableArray<ValueNumber>.Empty;

            ValidateFunctionArity(function, args);

            if (function == ValueNumberFunction.MapSelect && args.Length == 2)
                return VNForMapSelect(stackKind, type, args[0], args[1]);

            if (function == ValueNumberFunction.BitCast && args.Length == 2)
            {
                var argEntry = GetEntryOrNull(args[0]);
                if (argEntry is not null && argEntry.StackKind == stackKind && SameRuntimeType(argEntry.Type, type))
                    return args[0];
            }

            if (IsCommutative(function) && args.Length == 2 && args[1].CompareTo(args[0]) < 0)
                args = ImmutableArray.Create(args[1], args[0]);

            if (TryFold(function, stackKind, type, args, out var folded))
                return folded;

            var key = new ValueNumberFuncKey(function, stackKind, type, args);
            if (_functions.TryGetValue(key, out var existing))
                return existing;

            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Function, stackKind, type, default, function, args, stableId: 0));
            _functions.Add(key, vn);
            return vn;
        }

        public bool TryGetEntry(ValueNumber vn, out ValueNumberEntry entry)
        {
            if (vn.IsValid && _entries.TryGetValue(vn.Id, out entry!))
                return true;
            entry = null!;
            return false;
        }

        public ValueNumber VNForException(ValueNumberFunction function, params ValueNumber[] args)
            => VNForException(function, args is null || args.Length == 0 ? ImmutableArray<ValueNumber>.Empty : ImmutableArray.Create(args));

        public ValueNumber VNForException(ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            if (!IsExceptionFunction(function))
                throw new ArgumentException(function.ToString() + " is not an exception value-number function.", nameof(function));

            return VNForFunc(GenStackKind.Unknown, null, function, args);
        }

        public ValueNumber VNExcSetSingleton(ValueNumber exception)
        {
            if (!exception.IsValid)
                return VNForEmptyExcSet();

            return VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.ExcSetCons, exception, VNForEmptyExcSet());
        }

        public ValueNumber VNExcSetUnion(ValueNumber left, ValueNumber right)
        {
            if (IsEmptyExcSet(left))
                return IsExceptionSet(right) ? right : VNExcSetSingleton(right);
            if (IsEmptyExcSet(right))
                return IsExceptionSet(left) ? left : VNExcSetSingleton(left);

            var items = new List<ValueNumber>();
            CollectExceptionSet(left, items);
            CollectExceptionSet(right, items);
            if (items.Count == 0)
                return VNForEmptyExcSet();

            items.Sort();
            ValueNumber tail = VNForEmptyExcSet();
            ValueNumber previous = NoVN;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                ValueNumber item = items[i];
                if (!item.IsValid || item == previous)
                    continue;
                tail = VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.ExcSetCons, item, tail);
                previous = item;
            }

            return tail;
        }

        public ValueNumber VNWithExc(ValueNumber value, ValueNumber exceptionSet)
        {
            if (!value.IsValid || IsEmptyExcSet(exceptionSet))
                return value;

            VNUnpackExc(value, out var normal, out var oldExceptionSet);
            ValueNumber combined = VNExcSetUnion(oldExceptionSet, exceptionSet);
            if (IsEmptyExcSet(combined))
                return normal;

            var normalEntry = GetEntryOrNull(normal);
            GenStackKind stackKind = normalEntry?.StackKind ?? GenStackKind.Unknown;
            RuntimeType? type = normalEntry?.Type;
            return VNForFunc(stackKind, type, ValueNumberFunction.ValWithExc, normal, combined);
        }

        public ValueNumber VNNormalValue(ValueNumber value)
        {
            VNUnpackExc(value, out var normal, out _);
            return normal;
        }

        public ValueNumber VNExceptionSet(ValueNumber value)
        {
            VNUnpackExc(value, out _, out var exceptionSet);
            return exceptionSet;
        }

        public void VNUnpackExc(ValueNumber value, out ValueNumber normal, out ValueNumber exceptionSet)
        {
            if (TryGetEntry(value, out var entry) &&
                entry.Function == ValueNumberFunction.ValWithExc &&
                entry.Args.Length == 2)
            {
                normal = entry.Args[0];
                exceptionSet = entry.Args[1];
                return;
            }

            normal = value;
            exceptionSet = VNForEmptyExcSet();
        }
        public bool IsValueWithExc(ValueNumber value)
            => TryGetEntry(value, out var entry) &&
            entry.Function == ValueNumberFunction.ValWithExc &&
            entry.Args.Length == 2;
        public bool IsEmptyExcSet(ValueNumber value)
            => TryGetConstant(value, out var constant) && constant.Kind == ValueNumberConstantKind.EmptyExceptionSet;

        public bool IsExceptionSet(ValueNumber value)
        {
            if (IsEmptyExcSet(value))
                return true;
            return TryGetEntry(value, out var entry) &&
                   entry.Function == ValueNumberFunction.ExcSetCons &&
                   entry.Args.Length == 2 &&
                   IsExceptionSet(entry.Args[1]);
        }

        private void CollectExceptionSet(ValueNumber value, List<ValueNumber> items)
        {
            if (!value.IsValid || IsEmptyExcSet(value))
                return;

            if (TryGetEntry(value, out var entry) &&
                entry.Function == ValueNumberFunction.ExcSetCons &&
                entry.Args.Length == 2)
            {
                items.Add(entry.Args[0]);
                CollectExceptionSet(entry.Args[1], items);
                return;
            }

            items.Add(value);
        }

        private static bool IsExceptionFunction(ValueNumberFunction function)
            => function is ValueNumberFunction.NullPtrExc
                or ValueNumberFunction.ArithmeticExc
                or ValueNumberFunction.OverflowExc
                or ValueNumberFunction.ConvOverflowExc
                or ValueNumberFunction.DivideByZeroExc
                or ValueNumberFunction.IndexOutOfRangeExc
                or ValueNumberFunction.InvalidCastExc
                or ValueNumberFunction.NewArrOverflowExc
                or ValueNumberFunction.HelperOpaqueExc;

        public bool IsConstant(ValueNumber vn) => TryGetEntry(vn, out var entry) && entry.Kind == ValueNumberKind.Constant;

        public bool TryGetConstant(ValueNumber vn, out ValueNumberConstantKey constant)
        {
            if (TryGetEntry(vn, out var entry) && entry.Kind == ValueNumberKind.Constant)
            {
                constant = entry.Constant;
                return true;
            }
            constant = default;
            return false;
        }

        public bool TryGetFieldSequence(ValueNumber vn, out ValueNumberFieldSequence sequence)
        {
            if (TryGetConstant(vn, out var constant) &&
                constant.Kind == ValueNumberConstantKind.FieldSequence &&
                constant.Object is ValueNumberFieldSequence fs)
            {
                sequence = fs;
                return true;
            }

            sequence = null!;
            return false;
        }

        public string Dump(ValueNumber vn)
        {
            if (!vn.IsValid)
                return vn.ToString();
            if (!_entries.TryGetValue(vn.Id, out var entry))
                return vn.ToString();

            switch (entry.Kind)
            {
                case ValueNumberKind.Constant:
                    return vn + "=" + entry.Constant;
                case ValueNumberKind.Phi:
                case ValueNumberKind.MemoryPhi:
                case ValueNumberKind.Function:
                case ValueNumberKind.Unique:
                    return vn + "=" + entry.Function + "(" + Join(entry.Args) + ")";
                default:
                    return vn.ToString();
            }
        }

        public ValueNumber VNForMapSelectWithDependencies(
            GenStackKind stackKind,
            RuntimeType? type,
            ValueNumber map,
            ValueNumber selector,
            ISet<ValueNumber>? memoryDependencies)
        {
            if (!map.IsValid || !selector.IsValid)
                return VNForUnique(stackKind, type, ValueNumberFunction.MapSelect, ImmutableArray.Create(map, selector));

            int budget = DefaultMapSelectBudget;
            bool usedRecursiveVN = false;
            var active = new HashSet<ValueNumberFuncKey>();
            var dependencies = new HashSet<ValueNumber>();
            ValueNumber result = VNForMapSelectWork(stackKind, type, map, selector, ref budget, active, dependencies, ref usedRecursiveVN);

            if (memoryDependencies is not null)
            {
                foreach (ValueNumber dependency in dependencies)
                    memoryDependencies.Add(dependency);
            }

            return result == RecursiveVN ? CreateMapSelect(stackKind, type, map, selector) : result;
        }

        private ValueNumber VNForMapSelect(GenStackKind stackKind, RuntimeType? type, ValueNumber map, ValueNumber selector)
            => VNForMapSelectWithDependencies(stackKind, type, map, selector, memoryDependencies: null);

        private readonly struct MapSelectWorkCacheEntry
        {
            public readonly ValueNumber Result;
            public readonly ImmutableArray<ValueNumber> MemoryDependencies;

            public MapSelectWorkCacheEntry(ValueNumber result, ImmutableArray<ValueNumber> memoryDependencies)
            {
                Result = result;
                MemoryDependencies = memoryDependencies.IsDefault ? ImmutableArray<ValueNumber>.Empty : memoryDependencies;
            }

            public void AddDependenciesTo(ISet<ValueNumber> dependencies)
            {
                for (int i = 0; i < MemoryDependencies.Length; i++)
                    dependencies.Add(MemoryDependencies[i]);
            }
        }

        private ValueNumber VNForMapSelectWork(
            GenStackKind stackKind,
            RuntimeType? type,
            ValueNumber map,
            ValueNumber selector,
            ref int budget,
            HashSet<ValueNumberFuncKey> active,
            HashSet<ValueNumber> memoryDependencies,
            ref bool usedRecursiveVN)
        {
            var args = ImmutableArray.Create(map, selector);
            var key = new ValueNumberFuncKey(ValueNumberFunction.MapSelect, stackKind, type, args);

            if (_mapSelectCache.TryGetValue(key, out var cached))
            {
                cached.AddDependenciesTo(memoryDependencies);
                return cached.Result;
            }

            if (budget <= 0)
                return CreateMapSelect(stackKind, type, map, selector);

            budget--;

            if (!active.Add(key))
            {
                usedRecursiveVN = true;
                return RecursiveVN;
            }

            try
            {
                if (TryGetEntry(map, out var entry))
                {
                    if (entry.Function == ValueNumberFunction.MapStore && entry.Args.Length == 4)
                    {
                        var previousMap = entry.Args[0];
                        var storedSelector = entry.Args[1];
                        var storedValue = entry.Args[2];

                        if (storedSelector == selector)
                        {
                            memoryDependencies.Add(previousMap);
                            return NormalizeLoad(storedValue, stackKind, type);
                        }

                        if (SelectorsKnownDistinct(storedSelector, selector))
                        {
                            return VNForMapSelectWork(
                                stackKind,
                                type,
                                previousMap,
                                selector,
                                ref budget,
                                active,
                                memoryDependencies,
                                ref usedRecursiveVN);
                        }
                    }

                    if (entry.Function == ValueNumberFunction.MapPhysicalStore && entry.Args.Length == 3)
                    {
                        var previousMap = entry.Args[0];
                        var storedSelector = entry.Args[1];
                        var storedValue = entry.Args[2];

                        if (PhysicalSelectorsEqual(storedSelector, selector))
                            return NormalizeLoad(storedValue, stackKind, type);

                        if (PhysicalSelectorContains(storedSelector, selector, out int offsetDelta, out int selectSize))
                        {
                            ValueNumber innerSelector = VNForPhysicalSelector(offsetDelta, selectSize);
                            return VNForMapSelectWork(
                                stackKind,
                                type,
                                storedValue,
                                innerSelector,
                                ref budget,
                                active,
                                memoryDependencies,
                                ref usedRecursiveVN);
                        }

                        if (PhysicalSelectorsDoNotOverlap(storedSelector, selector))
                        {
                            return VNForMapSelectWork(
                                stackKind,
                                type,
                                previousMap,
                                selector,
                                ref budget,
                                active,
                                memoryDependencies,
                                ref usedRecursiveVN);
                        }
                    }

                    if (entry.Function == ValueNumberFunction.BitCast && entry.Args.Length >= 1)
                    {
                        return VNForMapSelectWork(
                            stackKind,
                            type,
                            entry.Args[0],
                            selector,
                            ref budget,
                            active,
                            memoryDependencies,
                            ref usedRecursiveVN);
                    }

                    if ((entry.Kind == ValueNumberKind.Phi || entry.Kind == ValueNumberKind.MemoryPhi) && entry.Args.Length != 0)
                    {
                        var recursiveDependencies = new HashSet<ValueNumber>();
                        ValueNumber sameSelectedResult = RecursiveVN;
                        bool allSame = true;

                        for (int i = 0; i < entry.Args.Length; i++)
                        {
                            if (budget <= 0 || !entry.Args[i].IsValid)
                            {
                                allSame = false;
                                break;
                            }

                            bool branchUsedRecursiveVN = false;
                            ValueNumber currentResult = VNForMapSelectWork(
                                stackKind,
                                type,
                                entry.Args[i],
                                selector,
                                ref budget,
                                active,
                                recursiveDependencies,
                                ref branchUsedRecursiveVN);
                            usedRecursiveVN |= branchUsedRecursiveVN;

                            if (sameSelectedResult == RecursiveVN)
                                sameSelectedResult = currentResult;

                            if (currentResult != RecursiveVN && currentResult != sameSelectedResult)
                            {
                                allSame = false;
                                break;
                            }
                        }

                        if (allSame && sameSelectedResult != RecursiveVN)
                        {
                            if (!usedRecursiveVN)
                                _mapSelectCache[key] = new MapSelectWorkCacheEntry(sameSelectedResult, FreezeDependencies(recursiveDependencies));
                            AddAll(memoryDependencies, recursiveDependencies);
                            return sameSelectedResult;
                        }

                        AddAll(memoryDependencies, recursiveDependencies);
                    }
                }

                ValueNumber result = CreateMapSelect(stackKind, type, map, selector);
                _mapSelectCache[key] = new MapSelectWorkCacheEntry(result, FreezeDependencies(memoryDependencies));
                return result;
            }
            finally
            {
                active.Remove(key);
            }
        }

        private static ImmutableArray<ValueNumber> FreezeDependencies(HashSet<ValueNumber> dependencies)
        {
            if (dependencies.Count == 0)
                return ImmutableArray<ValueNumber>.Empty;

            var list = new List<ValueNumber>(dependencies);
            list.Sort();
            var builder = ImmutableArray.CreateBuilder<ValueNumber>(list.Count);
            for (int i = 0; i < list.Count; i++)
                builder.Add(list[i]);
            return builder.ToImmutable();
        }

        private static void AddAll(HashSet<ValueNumber> destination, HashSet<ValueNumber> source)
        {
            foreach (ValueNumber value in source)
                destination.Add(value);
        }

        private ValueNumber CreateMapSelect(GenStackKind stackKind, RuntimeType? type, ValueNumber map, ValueNumber selector)
        {
            var args = ImmutableArray.Create(map, selector);
            var key = new ValueNumberFuncKey(ValueNumberFunction.MapSelect, stackKind, type, args);
            if (_functions.TryGetValue(key, out var existing))
                return existing;

            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Function, stackKind, type, default, ValueNumberFunction.MapSelect, args, stableId: 0));
            _functions.Add(key, vn);
            return vn;
        }

        private ValueNumber NormalizeLoad(ValueNumber value, GenStackKind stackKind, RuntimeType? type)
        {
            var entry = GetEntryOrNull(value);
            if (entry is null)
                return value;
            if (entry.StackKind == stackKind && SameRuntimeType(entry.Type, type))
                return value;
            return VNForFunc(stackKind, type, ValueNumberFunction.BitCast, value, VNForCanonicalType(type));
        }


        private ValueNumber TryReducePhi(ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefaultOrEmpty)
                return NoVN;

            ValueNumber first = NoVN;
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.IsValid)
                    return NoVN;
                if (!first.IsValid)
                {
                    first = arg;
                    continue;
                }
                if (arg != first)
                    return NoVN;
            }
            return first;
        }

        private static ImmutableArray<ValueNumber> NormalizePhiArgs(ImmutableArray<ValueNumber> inputVNs)
        {
            if (inputVNs.IsDefault)
                return ImmutableArray<ValueNumber>.Empty;
            return inputVNs;
        }

        private bool TryFold(ValueNumberFunction function, GenStackKind stackKind, RuntimeType? type, ImmutableArray<ValueNumber> args, out ValueNumber folded)
        {
            folded = NoVN;

            if (args.Length == 1 && TryGetConstant(args[0], out var c0))
            {
                switch (function)
                {
                    case ValueNumberFunction.Neg:
                        if (c0.Kind == ValueNumberConstantKind.Int32) { folded = VNForInt32(unchecked(-(int)c0.A)); return true; }
                        if (c0.Kind == ValueNumberConstantKind.Int64) { folded = VNForInt64(unchecked(-c0.A)); return true; }
                        break;
                    case ValueNumberFunction.Not:
                        if (c0.Kind == ValueNumberConstantKind.Int32) { folded = VNForInt32(~(int)c0.A); return true; }
                        if (c0.Kind == ValueNumberConstantKind.Int64) { folded = VNForInt64(~c0.A); return true; }
                        break;
                    case ValueNumberFunction.Conv:
                        folded = FoldUncheckedIntegerConversion(stackKind, type, c0);
                        return folded.IsValid;
                }
            }

            if (args.Length == 2 && TryGetConstant(args[0], out c0) && TryGetConstant(args[1], out var c1))
            {
                if (TryFoldBinary(function, stackKind, c0, c1, out folded))
                    return true;
            }

            if (args.Length == 2)
            {
                switch (function)
                {
                    case ValueNumberFunction.Add:
                    case ValueNumberFunction.Or:
                    case ValueNumberFunction.Xor:
                        if (IsZero(args[0])) { folded = args[1]; return true; }
                        if (IsZero(args[1])) { folded = args[0]; return true; }
                        break;
                    case ValueNumberFunction.Sub:
                    case ValueNumberFunction.Shl:
                    case ValueNumberFunction.Shr:
                    case ValueNumberFunction.ShrUn:
                        if (IsZero(args[1])) { folded = args[0]; return true; }
                        break;
                    case ValueNumberFunction.Mul:
                        if (IsOne(args[0])) { folded = args[1]; return true; }
                        if (IsOne(args[1])) { folded = args[0]; return true; }
                        if (IsZero(args[0]) || IsZero(args[1])) { folded = stackKind == GenStackKind.I8 ? VNForInt64(0) : VNForInt32(0); return true; }
                        break;
                    case ValueNumberFunction.And:
                        if (IsAllBitsSet(args[0])) { folded = args[1]; return true; }
                        if (IsAllBitsSet(args[1])) { folded = args[0]; return true; }
                        if (IsZero(args[0]) || IsZero(args[1])) { folded = stackKind == GenStackKind.I8 ? VNForInt64(0) : VNForInt32(0); return true; }
                        break;
                    case ValueNumberFunction.Ceq:
                        if (args[0] == args[1]) { folded = VNForInt32(1); return true; }
                        break;
                    case ValueNumberFunction.Clt:
                    case ValueNumberFunction.CltUn:
                    case ValueNumberFunction.Cgt:
                    case ValueNumberFunction.CgtUn:
                        if (args[0] == args[1]) { folded = VNForInt32(0); return true; }
                        break;
                }
            }

            return false;
        }

        private bool TryFoldBinary(ValueNumberFunction function, GenStackKind stackKind, ValueNumberConstantKey c0, ValueNumberConstantKey c1, out ValueNumber folded)
        {
            folded = NoVN;
            if (!IsIntegerConstant(c0) || !IsIntegerConstant(c1))
                return false;

            bool wide = c0.Kind == ValueNumberConstantKind.Int64 || c1.Kind == ValueNumberConstantKind.Int64 || stackKind == GenStackKind.I8;
            long a = c0.A;
            long b = c1.A;

            try
            {
                if (wide)
                {
                    folded = function switch
                    {
                        ValueNumberFunction.Add => VNForInt64(unchecked(a + b)),
                        ValueNumberFunction.Sub => VNForInt64(unchecked(a - b)),
                        ValueNumberFunction.Mul => VNForInt64(unchecked(a * b)),
                        ValueNumberFunction.And => VNForInt64(a & b),
                        ValueNumberFunction.Or => VNForInt64(a | b),
                        ValueNumberFunction.Xor => VNForInt64(a ^ b),
                        ValueNumberFunction.Shl => VNForInt64(unchecked(a << (int)(b & 0x3f))),
                        ValueNumberFunction.Shr => VNForInt64(a >> (int)(b & 0x3f)),
                        ValueNumberFunction.ShrUn => VNForInt64(unchecked((long)((ulong)a >> (int)(b & 0x3f)))),
                        ValueNumberFunction.Ceq => VNForInt32(a == b ? 1 : 0),
                        ValueNumberFunction.Clt => VNForInt32(a < b ? 1 : 0),
                        ValueNumberFunction.Cgt => VNForInt32(a > b ? 1 : 0),
                        ValueNumberFunction.CltUn => VNForInt32((ulong)a < (ulong)b ? 1 : 0),
                        ValueNumberFunction.CgtUn => VNForInt32((ulong)a > (ulong)b ? 1 : 0),
                        ValueNumberFunction.Div when b != 0 && !(a == long.MinValue && b == -1) => VNForInt64(a / b),
                        ValueNumberFunction.DivUn when b != 0 => VNForInt64(unchecked((long)((ulong)a / (ulong)b))),
                        ValueNumberFunction.Rem when b != 0 && !(a == long.MinValue && b == -1) => VNForInt64(a % b),
                        ValueNumberFunction.RemUn when b != 0 => VNForInt64(unchecked((long)((ulong)a % (ulong)b))),
                        _ => NoVN,
                    };
                }
                else
                {
                    int x = (int)a;
                    int y = (int)b;
                    folded = function switch
                    {
                        ValueNumberFunction.Add => VNForInt32(unchecked(x + y)),
                        ValueNumberFunction.Sub => VNForInt32(unchecked(x - y)),
                        ValueNumberFunction.Mul => VNForInt32(unchecked(x * y)),
                        ValueNumberFunction.And => VNForInt32(x & y),
                        ValueNumberFunction.Or => VNForInt32(x | y),
                        ValueNumberFunction.Xor => VNForInt32(x ^ y),
                        ValueNumberFunction.Shl => VNForInt32(unchecked(x << (y & 0x1f))),
                        ValueNumberFunction.Shr => VNForInt32(x >> (y & 0x1f)),
                        ValueNumberFunction.ShrUn => VNForInt32(unchecked((int)((uint)x >> (y & 0x1f)))),
                        ValueNumberFunction.Ceq => VNForInt32(x == y ? 1 : 0),
                        ValueNumberFunction.Clt => VNForInt32(x < y ? 1 : 0),
                        ValueNumberFunction.Cgt => VNForInt32(x > y ? 1 : 0),
                        ValueNumberFunction.CltUn => VNForInt32((uint)x < (uint)y ? 1 : 0),
                        ValueNumberFunction.CgtUn => VNForInt32((uint)x > (uint)y ? 1 : 0),
                        ValueNumberFunction.Div when y != 0 && !(x == int.MinValue && y == -1) => VNForInt32(x / y),
                        ValueNumberFunction.DivUn when y != 0 => VNForInt32(unchecked((int)((uint)x / (uint)y))),
                        ValueNumberFunction.Rem when y != 0 && !(x == int.MinValue && y == -1) => VNForInt32(x % y),
                        ValueNumberFunction.RemUn when y != 0 => VNForInt32(unchecked((int)((uint)x % (uint)y))),
                        _ => NoVN,
                    };
                }
            }
            catch (DivideByZeroException)
            {
                folded = NoVN;
            }

            return folded.IsValid;
        }

        private ValueNumber FoldUncheckedIntegerConversion(GenStackKind targetStackKind, RuntimeType? targetType, ValueNumberConstantKey c)
        {
            if (!IsIntegerConstant(c))
                return NoVN;

            long v = c.A;
            string? name = targetType?.Name;
            return name switch
            {
                "SByte" => VNForInt32(unchecked((sbyte)v)),
                "Byte" => VNForInt32(unchecked((byte)v)),
                "Int16" => VNForInt32(unchecked((short)v)),
                "UInt16" => VNForInt32(unchecked((ushort)v)),
                "Char" => VNForInt32(unchecked((char)v)),
                "Boolean" => VNForInt32(v == 0 ? 0 : 1),
                "Int32" => VNForInt32(unchecked((int)v)),
                "UInt32" => VNForInt32(unchecked((int)(uint)v)),
                "Int64" => VNForInt64(v),
                "UInt64" => VNForInt64(v),
                "IntPtr" => TargetArchitecture.PointerSize == 4 ? VNForInt32(unchecked((int)v)) : VNForInt64(v),
                "UIntPtr" => TargetArchitecture.PointerSize == 4 ? VNForInt32(unchecked((int)(uint)v)) : VNForInt64(v),
                _ => targetStackKind == GenStackKind.I8 ? VNForInt64(v) : targetStackKind == GenStackKind.I4 ? VNForInt32(unchecked((int)v)) : NoVN,
            };
        }

        private bool IsZero(ValueNumber vn)
        {
            return TryGetConstant(vn, out var c) &&
                   (c.Kind == ValueNumberConstantKind.Null ||
                    c.Kind == ValueNumberConstantKind.Int32 && c.A == 0 ||
                    c.Kind == ValueNumberConstantKind.Int64 && c.A == 0);
        }

        private bool IsOne(ValueNumber vn)
        {
            return TryGetConstant(vn, out var c) &&
                   (c.Kind == ValueNumberConstantKind.Int32 && c.A == 1 ||
                    c.Kind == ValueNumberConstantKind.Int64 && c.A == 1);
        }

        private bool IsAllBitsSet(ValueNumber vn)
        {
            return TryGetConstant(vn, out var c) &&
                   (c.Kind == ValueNumberConstantKind.Int32 && (int)c.A == -1 ||
                    c.Kind == ValueNumberConstantKind.Int64 && c.A == -1);
        }

        private static bool IsIntegerConstant(ValueNumberConstantKey c)
            => c.Kind == ValueNumberConstantKind.Int32 || c.Kind == ValueNumberConstantKind.Int64;

        private bool SelectorsKnownDistinct(ValueNumber left, ValueNumber right)
        {
            if (left == right)
                return false;

            if (!TryGetConstant(left, out var l) || !TryGetConstant(right, out var r))
                return false;

            if (l.Kind != r.Kind)
                return false;

            return l.Kind switch
            {
                ValueNumberConstantKind.FieldHandle => l.A != r.A,
                ValueNumberConstantKind.FieldSequence => FieldSequencesKnownDistinct(l.Object as ValueNumberFieldSequence, r.Object as ValueNumberFieldSequence),
                ValueNumberConstantKind.TypeHandle => l.A != r.A,
                ValueNumberConstantKind.CanonicalTypeHandle => l.A != r.A || l.B != r.B,
                ValueNumberConstantKind.SsaSlot => l.A != r.A || l.B != r.B,
                ValueNumberConstantKind.PhysicalSelector => PhysicalSelectorsDoNotOverlap(left, right),
                ValueNumberConstantKind.ArrayElementClass => l.A != r.A || l.B != r.B,
                ValueNumberConstantKind.Int32 => !l.Equals(r),
                ValueNumberConstantKind.Int64 => !l.Equals(r),
                ValueNumberConstantKind.String => !l.Equals(r),
                ValueNumberConstantKind.MethodHandle => !l.Equals(r),
                _ => false,
            };
        }

        private static bool FieldSequencesKnownDistinct(ValueNumberFieldSequence? left, ValueNumberFieldSequence? right)
        {
            if (left is null || right is null)
                return false;
            if (left.Kind != right.Kind)
                return true;
            return left.FirstField.FieldId != right.FirstField.FieldId;
        }

        private bool PhysicalSelectorsEqual(ValueNumber left, ValueNumber right)
        {
            return TryGetConstant(left, out var l) && TryGetConstant(right, out var r) &&
                   l.Kind == ValueNumberConstantKind.PhysicalSelector &&
                   r.Kind == ValueNumberConstantKind.PhysicalSelector &&
                   l.A == r.A &&
                   l.B == r.B;
        }

        private bool PhysicalSelectorsDoNotOverlap(ValueNumber left, ValueNumber right)
        {
            if (!TryGetConstant(left, out var l) || !TryGetConstant(right, out var r))
                return false;
            if (l.Kind != ValueNumberConstantKind.PhysicalSelector || r.Kind != ValueNumberConstantKind.PhysicalSelector)
                return false;
            long lStart = l.A;
            long lEnd = l.A + Math.Max(0, l.B);
            long rStart = r.A;
            long rEnd = r.A + Math.Max(0, r.B);
            return lEnd <= rStart || rEnd <= lStart;
        }

        private bool PhysicalSelectorContains(ValueNumber container, ValueNumber contained, out int offsetDelta, out int containedSize)
        {
            offsetDelta = 0;
            containedSize = 0;

            if (!TryGetConstant(container, out var outer) || !TryGetConstant(contained, out var inner))
                return false;
            if (outer.Kind != ValueNumberConstantKind.PhysicalSelector || inner.Kind != ValueNumberConstantKind.PhysicalSelector)
                return false;

            long outerStart = outer.A;
            long outerSize = Math.Max(0, outer.B);
            long outerEnd = outerStart + outerSize;
            long innerStart = inner.A;
            long innerSize = Math.Max(0, inner.B);
            long innerEnd = innerStart + innerSize;

            if (innerSize <= 0)
                return false;
            if (outerStart > innerStart || innerEnd > outerEnd)
                return false;

            long delta = innerStart - outerStart;
            if (delta > int.MaxValue || innerSize > int.MaxValue)
                return false;

            offsetDelta = (int)delta;
            containedSize = (int)innerSize;
            return true;
        }

        private ValueNumberEntry? GetEntryOrNull(ValueNumber vn)
        {
            if (!vn.IsValid) return null;
            _entries.TryGetValue(vn.Id, out var entry);
            return entry;
        }

        private ValueNumber Allocate(ValueNumberEntry entry)
        {
            var vn = new ValueNumber(_nextId++);
            _entries.Add(vn.Id, entry);
            return vn;
        }

        private static int StableIdFor(SsaValueName name)
            => HashCode.Combine(name.Slot.Kind, name.Slot.Index, name.Version);

        private static bool IsCommutative(ValueNumberFunction function)
            => function == ValueNumberFunction.Add ||
               function == ValueNumberFunction.Mul ||
               function == ValueNumberFunction.And ||
               function == ValueNumberFunction.Or ||
               function == ValueNumberFunction.Xor ||
               function == ValueNumberFunction.Ceq;

        private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.IsValueType || right.IsValueType)
                return left.IsValueType && right.IsValueType && Math.Max(1, left.SizeOf) == Math.Max(1, right.SizeOf);
            return left.TypeId == right.TypeId;
        }

        private static string Join(ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefaultOrEmpty)
                return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append(args[i]);
            }
            return sb.ToString();
        }
    }

    internal readonly struct SsaLoopMemoryDependency : IEquatable<SsaLoopMemoryDependency>
    {
        public readonly int LoopIndex;
        public readonly int BlockId;
        public readonly int StatementIndex;
        public readonly int TreeId;
        public readonly ValueNumber Memory;

        public SsaLoopMemoryDependency(int loopIndex, int blockId, int statementIndex, int treeId, ValueNumber memory)
        {
            LoopIndex = loopIndex;
            BlockId = blockId;
            StatementIndex = statementIndex;
            TreeId = treeId;
            Memory = memory;
        }

        public bool Equals(SsaLoopMemoryDependency other)
            => LoopIndex == other.LoopIndex &&
               BlockId == other.BlockId &&
               StatementIndex == other.StatementIndex &&
               TreeId == other.TreeId &&
               Memory == other.Memory;

        public override bool Equals(object? obj) => obj is SsaLoopMemoryDependency other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(LoopIndex, BlockId, StatementIndex, TreeId, Memory.Id);

        public override string ToString()
            => "L" + LoopIndex.ToString() + ":B" + BlockId.ToString() + ":S" + StatementIndex.ToString() + ":T" + TreeId.ToString() + " -> " + Memory.ToString();
    }

    internal sealed class SsaValueNumberingResult
    {
        public ValueNumberStore Store { get; }
        public IReadOnlyDictionary<SsaValueName, ValueNumberPair> SsaValues { get; }
        public IReadOnlyDictionary<SsaMemoryValueName, ValueNumber> MemoryValues { get; }
        public IReadOnlyDictionary<GenTree, ValueNumberPair> TreeValues { get; }
        public IReadOnlyDictionary<int, ValueNumber> HeapIn { get; }
        public IReadOnlyDictionary<int, ValueNumber> HeapOut { get; }
        public IReadOnlyDictionary<int, ValueNumber> ByrefExposedIn { get; }
        public IReadOnlyDictionary<int, ValueNumber> ByrefExposedOut { get; }
        public ImmutableArray<SsaLoopMemoryDependency> LoopMemoryDependencies { get; }

        public SsaValueNumberingResult(
            ValueNumberStore store,
            IReadOnlyDictionary<SsaValueName, ValueNumberPair> ssaValues,
            IReadOnlyDictionary<GenTree, ValueNumberPair> treeValues,
            IReadOnlyDictionary<SsaMemoryValueName, ValueNumber> memoryValues,
            IReadOnlyDictionary<int, ValueNumber> heapIn,
            IReadOnlyDictionary<int, ValueNumber> heapOut,
            IReadOnlyDictionary<int, ValueNumber>? byrefExposedIn = null,
            IReadOnlyDictionary<int, ValueNumber>? byrefExposedOut = null,
            ImmutableArray<SsaLoopMemoryDependency> loopMemoryDependencies = default)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            SsaValues = ssaValues ?? throw new ArgumentNullException(nameof(ssaValues));
            MemoryValues = memoryValues ?? throw new ArgumentNullException(nameof(memoryValues));
            TreeValues = treeValues ?? throw new ArgumentNullException(nameof(treeValues));
            HeapIn = heapIn ?? throw new ArgumentNullException(nameof(heapIn));
            HeapOut = heapOut ?? throw new ArgumentNullException(nameof(heapOut));
            ByrefExposedIn = byrefExposedIn ?? new Dictionary<int, ValueNumber>();
            ByrefExposedOut = byrefExposedOut ?? new Dictionary<int, ValueNumber>();
            LoopMemoryDependencies = loopMemoryDependencies.IsDefault ? ImmutableArray<SsaLoopMemoryDependency>.Empty : loopMemoryDependencies;
        }

        public bool TryGetSsaValue(SsaValueName name, out ValueNumberPair value) => SsaValues.TryGetValue(name, out value);
        public bool TryGetMemoryValue(SsaMemoryValueName name, out ValueNumber value) => MemoryValues.TryGetValue(name, out value);
        public bool TryGetTreeValue(GenTree tree, out ValueNumberPair value) => TreeValues.TryGetValue(tree, out value);
    }

    internal static class SsaValueNumbering
    {
        public static SsaMethod BuildMethod(SsaMethod method, bool validate = true)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            var result = new Builder(method).Run();
            StampSsaDescriptorValueNumbers(method, result);
            var numbered = new SsaMethod(
                method.GenTreeMethod,
                method.Cfg,
                method.Slots,
                method.InitialValues,
                method.ValueDefinitions,
                method.Blocks,
                result,
                method.SsaLocalDescriptors,
                method.InitialMemoryValues,
                method.MemoryDefinitions);
            SsaSourceAnnotations.Attach(numbered);
            if (validate)
                SsaVerifier.Verify(numbered);
            return numbered;
        }


        private static void StampSsaDescriptorValueNumbers(SsaMethod method, SsaValueNumberingResult result)
        {
            for (int i = 0; i < method.ValueDefinitions.Length; i++)
            {
                var definition = method.ValueDefinitions[i];
                if (!result.TryGetSsaValue(definition.Name, out var valueNumbers))
                    throw new InvalidOperationException("Value numbering did not assign an SSA descriptor value number for " + definition.Name + ".");

                definition.Descriptor.SetValueNumbers(valueNumbers);
            }

            for (int i = 0; i < method.MemoryDefinitions.Length; i++)
            {
                var definition = method.MemoryDefinitions[i];
                if (!result.TryGetMemoryValue(definition.Name, out var valueNumber))
                    throw new InvalidOperationException("Value numbering did not assign a memory SSA descriptor value number for " + definition.Name + ".");

                definition.Descriptor.SetValueNumber(valueNumber);
            }
        }

        private sealed class Builder
        {
            private readonly SsaMethod _method;
            private readonly ValueNumberStore _store = new();
            private readonly Dictionary<SsaValueName, ValueNumberPair> _ssaValues = new();
            private readonly Dictionary<SsaMemoryValueName, ValueNumber> _memoryValues = new();
            private readonly Dictionary<GenTree, ValueNumberPair> _treeValues = new(ReferenceEqualityComparer<GenTree>.Instance);
            private readonly Dictionary<int, ValueNumber> _heapIn = new();
            private readonly Dictionary<int, ValueNumber> _heapOut = new();
            private readonly Dictionary<int, ValueNumber> _byrefExposedIn = new();
            private readonly Dictionary<int, ValueNumber> _byrefExposedOut = new();
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _slotInfos = new();
            private readonly Dictionary<SsaValueName, SsaValueDefinition> _definitions = new();
            private readonly Dictionary<SsaMemoryValueName, SsaMemoryDefinition> _memoryDefinitions = new();
            private readonly Dictionary<SsaValueName, SsaDescriptor> _ssaDescriptors = new();
            private readonly Dictionary<(int loopIndex, int blockId, int statementIndex, int treeId), HashSet<ValueNumber>> _loopMemoryDependencies = new();
            private readonly SsaBlock[] _blockById;
            private readonly int[] _loopNumberByBlock;
            private bool _changedThisPass;

            public Builder(SsaMethod method)
            {
                _method = method;
                for (int i = 0; i < method.Slots.Length; i++)
                    _slotInfos[method.Slots[i].Slot] = method.Slots[i];
                for (int i = 0; i < method.ValueDefinitions.Length; i++)
                {
                    _definitions[method.ValueDefinitions[i].Name] = method.ValueDefinitions[i];
                    _ssaDescriptors[method.ValueDefinitions[i].Name] = method.ValueDefinitions[i].Descriptor;
                }
                for (int i = 0; i < method.MemoryDefinitions.Length; i++)
                    _memoryDefinitions[method.MemoryDefinitions[i].Name] = method.MemoryDefinitions[i];
                _blockById = new SsaBlock[method.Blocks.Length];
                for (int i = 0; i < method.Blocks.Length; i++)
                    _blockById[method.Blocks[i].Id] = method.Blocks[i];
                _loopNumberByBlock = BuildLoopNumberByBlock(method.Cfg);
            }

            public SsaValueNumberingResult Run()
            {
                SeedInitialValues();

                var order = _method.Cfg.ReversePostOrder.IsDefaultOrEmpty
                    ? BuildNaturalBlockOrder()
                    : _method.Cfg.ReversePostOrder;

                int passCount = Math.Max(2, Math.Min(32, _method.Blocks.Length + 2));
                for (int pass = 0; pass < passCount; pass++)
                {
                    _changedThisPass = false;
                    _loopMemoryDependencies.Clear();

                    for (int i = 0; i < order.Length; i++)
                    {
                        int blockId = order[i];
                        if ((uint)blockId >= (uint)_blockById.Length || _blockById[blockId] is null)
                            continue;
                        NumberBlock(_blockById[blockId]);
                    }

                    if (pass != 0 && !_changedThisPass)
                        break;
                }

                for (int i = 0; i < _method.Blocks.Length; i++)
                {
                    int blockId = _method.Blocks[i].Id;
                    if (!_heapIn.ContainsKey(blockId))
                        _heapIn[blockId] = OpaqueMemory(blockId);
                    if (!_heapOut.ContainsKey(blockId))
                        _heapOut[blockId] = _heapIn[blockId];
                    if (!_byrefExposedIn.ContainsKey(blockId))
                        _byrefExposedIn[blockId] = OpaqueMemory(blockId);
                    if (!_byrefExposedOut.ContainsKey(blockId))
                        _byrefExposedOut[blockId] = _byrefExposedIn[blockId];
                }

                return new SsaValueNumberingResult(
                    _store,
                    new Dictionary<SsaValueName, ValueNumberPair>(_ssaValues),
                    new Dictionary<GenTree, ValueNumberPair>(_treeValues, ReferenceEqualityComparer<GenTree>.Instance),
                    new Dictionary<SsaMemoryValueName, ValueNumber>(_memoryValues),
                    new Dictionary<int, ValueNumber>(_heapIn),
                    new Dictionary<int, ValueNumber>(_heapOut),
                    new Dictionary<int, ValueNumber>(_byrefExposedIn),
                    new Dictionary<int, ValueNumber>(_byrefExposedOut),
                    FreezeLoopMemoryDependencies());
            }

            private ImmutableArray<int> BuildNaturalBlockOrder()
            {
                var builder = ImmutableArray.CreateBuilder<int>(_method.Blocks.Length);
                for (int i = 0; i < _method.Blocks.Length; i++)
                    builder.Add(_method.Blocks[i].Id);
                return builder.ToImmutable();
            }

            private ImmutableArray<SsaLoopMemoryDependency> FreezeLoopMemoryDependencies()
            {
                if (_loopMemoryDependencies.Count == 0)
                    return ImmutableArray<SsaLoopMemoryDependency>.Empty;

                var entries = new List<SsaLoopMemoryDependency>();
                foreach (var pair in _loopMemoryDependencies)
                {
                    var memories = new List<ValueNumber>(pair.Value);
                    memories.Sort();
                    for (int i = 0; i < memories.Count; i++)
                    {
                        entries.Add(new SsaLoopMemoryDependency(
                            pair.Key.loopIndex,
                            pair.Key.blockId,
                            pair.Key.statementIndex,
                            pair.Key.treeId,
                            memories[i]));
                    }
                }

                entries.Sort(static (left, right) =>
                {
                    int c = left.LoopIndex.CompareTo(right.LoopIndex);
                    if (c != 0) return c;
                    c = left.BlockId.CompareTo(right.BlockId);
                    if (c != 0) return c;
                    c = left.StatementIndex.CompareTo(right.StatementIndex);
                    if (c != 0) return c;
                    c = left.TreeId.CompareTo(right.TreeId);
                    if (c != 0) return c;
                    return left.Memory.CompareTo(right.Memory);
                });

                return entries.ToImmutableArray();
            }

            private ValueNumber SelectMemoryMap(
                GenTree node,
                int blockId,
                int statementIndex,
                GenStackKind stackKind,
                RuntimeType? type,
                ValueNumber map,
                ValueNumber selector)
            {
                var dependencies = new HashSet<ValueNumber>();
                ValueNumber value = _store.VNForMapSelectWithDependencies(stackKind, type, map, selector, dependencies);
                if (dependencies.Count != 0)
                    RecordLoopMemoryDependencies(node, blockId, statementIndex, dependencies);
                return value;
            }

            private void RecordLoopMemoryDependencies(GenTree node, int blockId, int statementIndex, HashSet<ValueNumber> dependencies)
            {
                if ((uint)blockId >= (uint)_loopNumberByBlock.Length)
                    return;

                int loopIndex = _loopNumberByBlock[blockId];
                if (loopIndex < 0)
                    return;

                var key = (loopIndex, blockId, statementIndex, node.Id);
                if (!_loopMemoryDependencies.TryGetValue(key, out var existing))
                {
                    existing = new HashSet<ValueNumber>();
                    _loopMemoryDependencies.Add(key, existing);
                }

                foreach (ValueNumber dependency in dependencies)
                    existing.Add(dependency);
            }

            private void SeedInitialValues()
            {
                for (int i = 0; i < _method.InitialValues.Length; i++)
                {
                    var name = _method.InitialValues[i];
                    var info = GetSlotInfo(name.Slot);
                    var slotVN = _store.VNForSlot(name.Slot);
                    var initVN = _store.VNForFunc(info.StackKind, info.Type, ValueNumberFunction.InitVal, slotVN);
                    SetSsaValue(name, ValueNumberPair.Same(initVN));
                }

                for (int i = 0; i < _method.InitialMemoryValues.Length; i++)
                {
                    var name = _method.InitialMemoryValues[i];
                    SetMemoryValue(name, InitialMemoryValue(name.Kind));
                }
            }

            private void NumberBlock(SsaBlock block)
            {
                ValueNumber heap = GetBlockMemoryIn(block, SsaMemoryKind.GcHeap, _store.InitialHeap(_method.GenTreeMethod.RuntimeMethod));
                ValueNumber byrefExposed = GetBlockMemoryIn(block, SsaMemoryKind.ByrefExposed, _store.InitialByrefExposed(_method.GenTreeMethod.RuntimeMethod));
                SetHeapIn(block.Id, heap);
                SetByrefExposedIn(block.Id, byrefExposed);

                for (int p = 0; p < block.MemoryPhis.Length; p++)
                    NumberMemoryPhi(block.MemoryPhis[p]);

                if (block.TryGetMemoryIn(SsaMemoryKind.GcHeap, out var heapInName))
                    heap = GetMemoryValue(heapInName);
                if (block.TryGetMemoryIn(SsaMemoryKind.ByrefExposed, out var byrefInName))
                    byrefExposed = GetMemoryValue(byrefInName);

                for (int p = 0; p < block.Phis.Length; p++)
                    NumberPhi(block.Phis[p]);

                for (int s = 0; s < block.Statements.Length; s++)
                    NumberStatement(block.Statements[s], block.StatementTreeLists[s], block.Id, s, ref heap, ref byrefExposed);

                PublishBlockMemoryOut(block, ref heap, ref byrefExposed);

                SetHeapOut(block.Id, heap);
                SetByrefExposedOut(block.Id, byrefExposed);
            }


            private void NumberPhi(SsaPhi phi)
            {
                var liberalInputs = ImmutableArray.CreateBuilder<ValueNumber>(phi.Inputs.Length);
                var conservativeInputs = ImmutableArray.CreateBuilder<ValueNumber>(phi.Inputs.Length);

                for (int i = 0; i < phi.Inputs.Length; i++)
                {
                    ValueNumberPair inputPair = _ssaValues.TryGetValue(phi.Inputs[i].Value, out var existing)
                        ? existing
                        : InitialValueFor(phi.Inputs[i].Value);

                    liberalInputs.Add(_store.VNNormalValue(inputPair.Liberal));
                    conservativeInputs.Add(_store.VNNormalValue(inputPair.Conservative));
                }

                var info = GetSlotInfo(phi.Slot);
                var liberal = _store.VNForSsaPhi(phi.Target, ValueNumberCategory.Liberal, liberalInputs.ToImmutable(), info.StackKind, info.Type);
                var conservative = _store.VNForSsaPhi(phi.Target, ValueNumberCategory.Conservative, conservativeInputs.ToImmutable(), info.StackKind, info.Type);
                SetSsaValue(phi.Target, new ValueNumberPair(liberal, conservative));
            }
            private ValueNumber GetBlockMemoryIn(SsaBlock block, SsaMemoryKind kind, ValueNumber fallback)
            {
                if (block.TryGetMemoryIn(kind, out var name))
                    return GetMemoryValue(name);
                return fallback;
            }

            private void NumberMemoryPhi(SsaMemoryPhi phi)
            {
                var inputs = ImmutableArray.CreateBuilder<ValueNumber>(phi.Inputs.Length);
                for (int i = 0; i < phi.Inputs.Length; i++)
                    inputs.Add(GetMemoryValue(phi.Inputs[i].Value));

                SetMemoryValue(phi.Target, _store.VNForMemoryPhi(phi.BlockId, MemoryKindStableId(phi.Kind), inputs.ToImmutable()));
            }

            private static int MemoryKindStableId(SsaMemoryKind kind)
                => kind switch
                {
                    SsaMemoryKind.GcHeap => 0,
                    SsaMemoryKind.ByrefExposed => 1,
                    _ => 100 + (int)kind,
                };

            private ValueNumber InitialMemoryValue(SsaMemoryKind kind)
                => kind switch
                {
                    SsaMemoryKind.GcHeap => _store.InitialHeap(_method.GenTreeMethod.RuntimeMethod),
                    SsaMemoryKind.ByrefExposed => _store.InitialByrefExposed(_method.GenTreeMethod.RuntimeMethod),
                    _ => _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.InitVal, _store.VNForMethod(_method.GenTreeMethod.RuntimeMethod), _store.VNForInt32(100 + (int)kind)),
                };

            private void ApplyMemoryUses(SsaTree tree, ref ValueNumber heap, ref ValueNumber byrefExposed)
            {
                if (tree.TryGetMemoryUse(SsaMemoryKind.GcHeap, out var heapUse))
                    heap = GetMemoryValue(heapUse);
                if (tree.TryGetMemoryUse(SsaMemoryKind.ByrefExposed, out var byrefUse))
                    byrefExposed = GetMemoryValue(byrefUse);
            }

            private void PublishMemoryDefinitions(
                SsaTree tree,
                ImmutableArray<ValueNumberPair> operands,
                int blockId,
                int statementIndex,
                ValueNumber oldHeap,
                ValueNumber oldByrefExposed,
                ref ValueNumber heap,
                ref ValueNumber byrefExposed)
            {
                for (int i = 0; i < tree.MemoryDefinitions.Length; i++)
                {
                    var name = tree.MemoryDefinitions[i];
                    switch (name.Kind)
                    {
                        case SsaMemoryKind.GcHeap:
                            if (heap == oldHeap)
                                heap = OpaqueMemory(blockId);
                            SetMemoryValue(name, heap);
                            break;

                        case SsaMemoryKind.ByrefExposed:
                            if (byrefExposed == oldByrefExposed)
                                byrefExposed = OpaqueMemory(blockId);
                            SetMemoryValue(name, byrefExposed);
                            break;

                        default:
                            SetMemoryValue(name, OpaqueMemory(blockId));
                            break;
                    }
                }
            }

            private void PublishBlockMemoryOut(SsaBlock block, ref ValueNumber heap, ref ValueNumber byrefExposed)
            {
                for (int i = 0; i < block.MemoryOut.Length; i++)
                {
                    var name = block.MemoryOut[i];
                    switch (name.Kind)
                    {
                        case SsaMemoryKind.GcHeap:
                            SetMemoryValue(name, heap);
                            heap = GetMemoryValue(name);
                            break;
                        case SsaMemoryKind.ByrefExposed:
                            SetMemoryValue(name, byrefExposed);
                            byrefExposed = GetMemoryValue(name);
                            break;
                        default:
                            if (!_memoryValues.ContainsKey(name))
                                SetMemoryValue(name, InitialMemoryValue(name.Kind));
                            break;
                    }
                }
            }


            private ValueNumberPair NumberStatement(SsaTree root, ImmutableArray<SsaTree> treeList, int blockId, int statementIndex, ref ValueNumber heap, ref ValueNumber byrefExposed)
            {
                if (treeList.IsDefaultOrEmpty)
                    throw new InvalidOperationException("SSA statement root has no linear tree-list node " + root.Source.Id.ToString() + ".");

                var valuesByTree = new Dictionary<SsaTree, ValueNumberPair>(ReferenceEqualityComparer<SsaTree>.Instance);
                for (int i = 0; i < treeList.Length; i++)
                {
                    var tree = treeList[i];
                    var value = NumberLinearTree(tree, valuesByTree, blockId, statementIndex, ref heap, ref byrefExposed);
                    valuesByTree[tree] = value;
                }

                if (!valuesByTree.TryGetValue(root, out var result))
                    throw new InvalidOperationException("SSA statement root was not present in linear tree-list node " + root.Source.Id.ToString() + ".");

                return result;
            }

            private ValueNumberPair NumberLinearTree(
                SsaTree tree,
                Dictionary<SsaTree, ValueNumberPair> valuesByTree,
                int blockId,
                int statementIndex,
                ref ValueNumber heap,
                ref ValueNumber byrefExposed)
            {
                if (tree.Value.HasValue)
                {
                    var value = GetSsaValue(tree.Value.Value);
                    Remember(tree.Source, value);
                    return value;
                }

                var operands = GetNumberedOperands(tree, valuesByTree);

                ApplyMemoryUses(tree, ref heap, ref byrefExposed);
                ValueNumber oldHeap = heap;
                ValueNumber oldByrefExposed = byrefExposed;

                if (tree.StoreTarget.HasValue)
                {
                    ValueNumberPair rhs = operands.Length == 0
                        ? ValueNumberPair.Same(_store.VNForStableUnique(StableSyntheticId(blockId, statementIndex, ValueNumberFunction.InitVal), tree.Source.StackKind, tree.Source.Type, ValueNumberFunction.InitVal, ImmutableArray.Create(_store.VNForBlock(blockId), _store.VNForInt32(statementIndex))))
                        : operands[operands.Length - 1];

                    var target = tree.StoreTarget.Value;
                    var info = GetSlotInfo(target.Slot);
                    ValueNumberPair normalized;
                    if (tree.IsPartialDefinition)
                    {
                        if (tree.LocalFieldBaseValue.HasValue)
                            throw new InvalidOperationException("Partial SSA definition still carries node-level use-def metadata at node " + tree.Source.Id.ToString() + ".");
                        if (!_ssaDescriptors.TryGetValue(target, out var descriptor) || !descriptor.HasUseDefSsaNum)
                            throw new InvalidOperationException("Partial SSA definition " + target + " has no descriptor use-def SSA number.");

                        var useName = new SsaValueName(target.Slot, descriptor.UseDefSsaNumber);
                        var oldLocal = GetSsaValue(useName);
                        normalized = NumberPartialLocalDefinition(tree.Source, oldLocal, rhs, info.StackKind, info.Type, tree.LocalField!);
                    }
                    else
                    {
                        if (tree.LocalFieldBaseValue.HasValue || tree.LocalField is not null)
                            throw new InvalidOperationException("Malformed SSA local-field store metadata at node " + tree.Source.Id.ToString() + ".");

                        normalized = NormalizeStore(rhs, info.StackKind, info.Type);
                    }
                    SetSsaValue(target, normalized);
                    PublishMemoryDefinitions(tree, operands, blockId, statementIndex, oldHeap, oldByrefExposed, ref heap, ref byrefExposed);
                    var treeResult = ApplyExceptionSet(normalized, ExceptionSetFromOperands(operands));
                    Remember(tree.Source, treeResult);
                    return treeResult;
                }

                if (tree.LocalFieldBaseValue.HasValue && tree.LocalField is not null)
                {
                    var baseValue = GetSsaValue(tree.LocalFieldBaseValue.Value);
                    var result = NumberLocalFieldLoad(tree.Source, baseValue, tree.LocalField);
                    result = ApplyExceptionSet(result, _store.VNExceptionSet(baseValue.Conservative));
                    PublishMemoryDefinitions(tree, operands, blockId, statementIndex, oldHeap, oldByrefExposed, ref heap, ref byrefExposed);
                    Remember(tree.Source, result);
                    return result;
                }

                if (tree.LocalFieldBaseValue.HasValue || tree.LocalField is not null)
                    throw new InvalidOperationException("Malformed SSA local-field load metadata at node " + tree.Source.Id.ToString() + ".");

                var nonStoreResult = NumberNonStoreTree(tree.Source, operands, blockId, statementIndex, ref heap, ref byrefExposed);
                nonStoreResult = AttachTreeExceptions(tree.Source, nonStoreResult, operands);
                PublishMemoryDefinitions(tree, operands, blockId, statementIndex, oldHeap, oldByrefExposed, ref heap, ref byrefExposed);
                Remember(tree.Source, nonStoreResult);
                return nonStoreResult;
            }

            private static ImmutableArray<ValueNumberPair> GetNumberedOperands(SsaTree tree, Dictionary<SsaTree, ValueNumberPair> valuesByTree)
            {
                if (tree.Operands.Length == 0)
                    return ImmutableArray<ValueNumberPair>.Empty;

                var operands = ImmutableArray.CreateBuilder<ValueNumberPair>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                {
                    var operand = tree.Operands[i];
                    if (!valuesByTree.TryGetValue(operand, out var value))
                        throw new InvalidOperationException("SSA tree-list is not in execution order: node " + tree.Source.Id.ToString() + " operand " + operand.Source.Id.ToString() + " has not been numbered yet.");
                    operands.Add(value);
                }

                return operands.ToImmutable();
            }

            private ValueNumberPair NumberNonStoreTree(GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId, int statementIndex, ref ValueNumber heap, ref ValueNumber byrefExposed)
            {
                switch (node.Kind)
                {
                    case GenTreeKind.ConstI4:
                        return ValueNumberPair.Same(_store.VNForInt32(node.Int32));
                    case GenTreeKind.ConstI8:
                        return ValueNumberPair.Same(_store.VNForInt64(node.Int64));
                    case GenTreeKind.ConstR4Bits:
                        return ValueNumberPair.Same(_store.VNForFloat32Bits(node.Int32));
                    case GenTreeKind.ConstR8Bits:
                        return ValueNumberPair.Same(_store.VNForFloat64Bits(node.Int64));
                    case GenTreeKind.ConstNull:
                        return ValueNumberPair.Same(_store.VNForNull(node.Type));
                    case GenTreeKind.ConstString:
                        return ValueNumberPair.Same(_store.VNForConstant(ValueNumberConstantKey.String(node.Text)));
                    case GenTreeKind.SizeOf:
                        return ValueNumberPair.Same(_store.VNForInt32(node.RuntimeType?.SizeOf ?? node.Type?.SizeOf ?? node.Int32));
                    case GenTreeKind.DefaultValue:
                        return ValueNumberPair.Same(DefaultValue(node.Type, node.StackKind));
                    case GenTreeKind.Local:
                    case GenTreeKind.Arg:
                    case GenTreeKind.Temp:
                        return NumberUnpromotedLocalLoad(node, byrefExposed, blockId, statementIndex);
                    case GenTreeKind.Unary:
                        return Unary(node, operands, blockId);
                    case GenTreeKind.Binary:
                        return Binary(node, operands, blockId);
                    case GenTreeKind.Conv:
                        return Conv(node, operands);
                    case GenTreeKind.Field:
                        return LoadField(node, operands, heap, byrefExposed, blockId, statementIndex);
                    case GenTreeKind.StaticField:
                        return LoadStaticField(node, heap, blockId, statementIndex);
                    case GenTreeKind.LoadIndirect:
                        return LoadIndirect(node, operands, heap, byrefExposed, blockId, statementIndex);
                    case GenTreeKind.StoreField:
                        return StoreField(node, operands, ref heap, ref byrefExposed, blockId, statementIndex);
                    case GenTreeKind.StoreStaticField:
                        return StoreStaticField(node, operands, ref heap, blockId, statementIndex);
                    case GenTreeKind.StoreIndirect:
                        return StoreIndirect(node, operands, ref heap, ref byrefExposed, blockId, statementIndex);
                    case GenTreeKind.StoreLocal:
                    case GenTreeKind.StoreArg:
                    case GenTreeKind.StoreTemp:
                        return StoreUnpromotedLocal(node, operands, ref byrefExposed, blockId, statementIndex);
                    case GenTreeKind.ArrayElement:
                        return LoadArrayElement(node, operands, heap, blockId, statementIndex);
                    case GenTreeKind.StoreArrayElement:
                        return StoreArrayElement(node, operands, ref heap, blockId, statementIndex);
                    case GenTreeKind.ArrayElementAddr:
                        return ArrayElementAddress(node, operands);
                    case GenTreeKind.ArrayDataRef:
                        return Func(node, ValueNumberFunction.ArrayDataRef, operands);
                    case GenTreeKind.NewArray:
                        return NewArray(node, operands, ref heap, blockId);
                    case GenTreeKind.NewObject:
                    case GenTreeKind.NewDelegate:
                    case GenTreeKind.DelegateCombine:
                    case GenTreeKind.DelegateRemove:
                        return NewObject(node, operands, ref heap, blockId);
                    case GenTreeKind.Call:
                    case GenTreeKind.VirtualCall:
                    case GenTreeKind.DelegateInvoke:
                        return Call(node, operands, ref heap, ref byrefExposed, blockId);
                    case GenTreeKind.CastClass:
                        return FuncWithException(node, ValueNumberFunction.CastClass, operands);
                    case GenTreeKind.IsInst:
                        return FuncWithException(node, ValueNumberFunction.IsInstanceOf, operands);
                    case GenTreeKind.Box:
                        return FuncWithException(node, ValueNumberFunction.Box, operands);
                    case GenTreeKind.UnboxAny:
                        return FuncWithException(node, ValueNumberFunction.UnboxAny, operands);
                    case GenTreeKind.LocalAddr:
                    case GenTreeKind.ArgAddr:
                        return LocalAddress(node, blockId);
                    case GenTreeKind.FieldAddr:
                        return FieldAddress(node, operands, blockId);
                    case GenTreeKind.StaticFieldAddr:
                        return StaticFieldAddress(node, blockId);
                    case GenTreeKind.StackAlloc:
                        return FuncWithException(node, ValueNumberFunction.StackAlloc, operands, _store.VNForInt32(node.Int32));
                    case GenTreeKind.PointerElementAddr:
                        return Func(node, ValueNumberFunction.PointerElementAddr, operands, _store.VNForInt32(node.Int32));
                    case GenTreeKind.PointerToByRef:
                        return Func(node, ValueNumberFunction.PointerToByRef, operands);
                    case GenTreeKind.PointerDiff:
                        return Func(node, ValueNumberFunction.PointerDiff, operands, _store.VNForInt32(node.Int32));
                    case GenTreeKind.ExceptionObject:
                        return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.ExceptionObject, ImmutableArray.Create(_store.VNForBlock(blockId))));
                    default:
                        return Opaque(node, operands, ref heap, ref byrefExposed, blockId, statementIndex);
                }
            }

            private ValueNumberPair NumberLocalFieldLoad(GenTree node, ValueNumberPair baseValue, RuntimeField field)
            {
                ValueNumber selector = _store.VNForPhysicalSelector(field.Offset, Math.Max(1, StorageSize(field.FieldType, StackKindOf(field.FieldType))));
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, _store.VNNormalValue(baseValue.Liberal), selector);
                ValueNumber conservative = baseValue.BothEqual
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, _store.VNNormalValue(baseValue.Conservative), selector);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair NumberPartialLocalDefinition(
                GenTree node,
                ValueNumberPair oldLocal,
                ValueNumberPair fieldValue,
                GenStackKind localStackKind,
                RuntimeType? localType,
                RuntimeField field)
            {
                GenStackKind fieldStackKind = StackKindOf(field.FieldType);
                ValueNumber selector = _store.VNForPhysicalSelector(field.Offset, Math.Max(1, StorageSize(field.FieldType, fieldStackKind)));
                ValueNumber storedLiberal = Normalize(fieldValue.Liberal, fieldStackKind, field.FieldType);
                ValueNumber storedConservative = fieldValue.BothEqual
                    ? storedLiberal
                    : Normalize(fieldValue.Conservative, fieldStackKind, field.FieldType);
                ValueNumber liberalMap = _store.VNForFunc(
                    GenStackKind.Unknown,
                    null,
                    ValueNumberFunction.MapPhysicalStore,
                    _store.VNNormalValue(oldLocal.Liberal),
                    selector,
                    storedLiberal);
                ValueNumber liberal = Normalize(liberalMap, localStackKind, localType);
                ValueNumber conservative = oldLocal.BothEqual && storedLiberal == storedConservative
                    ? liberal
                    : Normalize(
                        _store.VNForFunc(
                            GenStackKind.Unknown,
                            null,
                            ValueNumberFunction.MapPhysicalStore,
                            _store.VNNormalValue(oldLocal.Conservative),
                            selector,
                            storedConservative),
                        localStackKind,
                        localType);
                return liberal == conservative ? ValueNumberPair.Same(liberal) : new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair NumberUnpromotedLocalLoad(GenTree node, ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                if (!SsaSlotHelpers.TryGetDirectLoadSlot(node, out var slot))
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                var info = GetSlotInfo(slot);
                ValueNumber memory = byrefExposed;
                ValueNumber localMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, memory, _store.VNForSlot(slot));
                ValueNumber selector = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(info.Type, info.StackKind)));
                ValueNumber value = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, localMap, selector);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreUnpromotedLocal(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                if (!SsaSlotHelpers.TryGetStoreSlot(node, out var slot) || operands.Length == 0)
                    return OpaqueByrefExposedStore(node, operands, ref byrefExposed, blockId);

                var info = GetSlotInfo(slot);
                ValueNumberPair value = NormalizeStore(operands[operands.Length - 1], info.StackKind, info.Type);
                ValueNumber slotSelector = _store.VNForSlot(slot);
                ValueNumber memory = byrefExposed;
                ValueNumber localMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, memory, slotSelector);
                ValueNumber physical = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(info.Type, info.StackKind)));
                ValueNumber newLocalMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, localMap, physical, _store.VNNormalValue(value.Conservative));
                ValueNumber updated = MapStore(memory, slotSelector, newLocalMap, blockId);
                byrefExposed = updated;
                return value;
            }

            private ValueNumberPair Unary(GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId)
            {
                var func = node.SourceOp switch
                {
                    BytecodeOp.Neg => ValueNumberFunction.Neg,
                    BytecodeOp.Not => ValueNumberFunction.Not,
                    _ => ValueNumberFunction.None,
                };
                if (func == ValueNumberFunction.None)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                ValueNumber liberalArg = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                ValueNumber conservativeArg = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Conservative);
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, func, liberalArg);
                ValueNumber conservative = liberalArg == conservativeArg
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, func, conservativeArg);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair Binary(GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId)
            {
                var func = BinaryFunction(node.SourceOp);
                if (func == ValueNumberFunction.None)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind,
                        node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                if (IsCheckedOverflowBinaryOp(node.SourceOp))
                {
                    ValueNumber leftLiberalChecked = operands.Length > 0 ? OperandNormal(operands, 0, ValueNumberCategory.Liberal) : ValueNumberStore.NoVN;
                    ValueNumber rightLiberalChecked = operands.Length > 1 ? OperandNormal(operands, 1, ValueNumberCategory.Liberal) : ValueNumberStore.NoVN;
                    ValueNumber leftConservativeChecked = operands.Length > 0 ? OperandNormal(operands, 0, ValueNumberCategory.Conservative) : ValueNumberStore.NoVN;
                    ValueNumber rightConservativeChecked = operands.Length > 1 ? OperandNormal(operands, 1, ValueNumberCategory.Conservative) : ValueNumberStore.NoVN;

                    ValueNumber liberalChecked = _store.VNForFunc(node.StackKind, node.Type, func, leftLiberalChecked, rightLiberalChecked);
                    ValueNumber conservativeChecked = leftLiberalChecked == leftConservativeChecked && rightLiberalChecked == rightConservativeChecked
                        ? liberalChecked
                        : _store.VNForFunc(node.StackKind, node.Type, func, leftConservativeChecked, rightConservativeChecked);
                    return WithException(node, liberalChecked, conservativeChecked);
                }
                {
                    ValueNumber leftLiberal = operands.Length > 0 ? OperandNormal(operands, 0, ValueNumberCategory.Liberal) : ValueNumberStore.NoVN;
                    ValueNumber rightLiberal = operands.Length > 1 ? OperandNormal(operands, 1, ValueNumberCategory.Liberal) : ValueNumberStore.NoVN;
                    ValueNumber leftConservative = operands.Length > 0 ? OperandNormal(operands, 0, ValueNumberCategory.Conservative) : ValueNumberStore.NoVN;
                    ValueNumber rightConservative = operands.Length > 1 ? OperandNormal(operands, 1, ValueNumberCategory.Conservative) : ValueNumberStore.NoVN;

                    ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, func, leftLiberal, rightLiberal);
                    ValueNumber conservative = leftLiberal == leftConservative && rightLiberal == rightConservative
                        ? liberal
                        : _store.VNForFunc(node.StackKind, node.Type, func, leftConservative, rightConservative);

                    if (node.CanThrow)
                        return WithException(node, liberal, conservative);
                    return new ValueNumberPair(liberal, conservative);
                }
            }

            private ValueNumberPair Conv(GenTree node, ImmutableArray<ValueNumberPair> operands)
            {
                ValueNumber argLiberal = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                ValueNumber argConservative = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Conservative);
                ValueNumber conv = _store.VNForInt32(((int)node.ConvKind << 8) | ((int)node.ConvFlags & 0xff));
                var func = (node.ConvFlags & NumericConvFlags.Checked) != 0 ? ValueNumberFunction.CastOvf : ValueNumberFunction.Conv;
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, func, argLiberal, conv);
                ValueNumber conservative = argLiberal == argConservative
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, func, argConservative, conv);
                return node.CanThrow ? WithException(node, liberal, conservative) : new ValueNumberPair(liberal, conservative);
            }

            private readonly struct FieldAddressInfo
            {
                public readonly bool IsStatic;
                public readonly ValueNumber BaseAddress;
                public readonly ValueNumberFieldSequence Sequence;
                public readonly int Offset;

                public FieldAddressInfo(bool isStatic, ValueNumber baseAddress, ValueNumberFieldSequence sequence, int offset)
                {
                    IsStatic = isStatic;
                    BaseAddress = baseAddress;
                    Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
                    Offset = offset;
                }
            }

            private readonly struct ArrayElementAddressInfo
            {
                public readonly ValueNumber ElementClass;
                public readonly ValueNumber Array;
                public readonly ValueNumber Index;
                public readonly int Offset;

                public ArrayElementAddressInfo(ValueNumber elementClass, ValueNumber array, ValueNumber index, int offset)
                {
                    ElementClass = elementClass;
                    Array = array;
                    Index = index;
                    Offset = offset;
                }
            }

            private ValueNumberPair LoadField(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap, ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                if (node.Field is null || operands.Length == 0)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ArgsFromPairs(operands).Add(heap)));

                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localAccess) && localAccess.Kind == SsaLocalAccessKind.Use)
                {
                    if (localAccess.IsPromotedFieldAccess)
                    {
                        ValueNumber promotedMemory = byrefExposed;
                        ValueNumber promotedSelector = _store.VNForSlot(localAccess.Slot);
                        ValueNumber promotedValue = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, promotedMemory, promotedSelector);
                        return ValueNumberPair.Same(promotedValue);
                    }

                    GenStackKind fieldStackKind = StackKindOf(node.Field.FieldType);
                    ValueNumber memory = byrefExposed;
                    ValueNumber slotSelector = _store.VNForSlot(localAccess.BaseSlot);
                    ValueNumber localMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, memory, slotSelector);
                    ValueNumber physical = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, fieldStackKind)));
                    ValueNumber value = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, localMap, physical);
                    return ValueNumberPair.Same(value);
                }

                ValueNumber receiver = OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                if (TryDecodeFieldAddress(receiver, out var receiverAddress))
                {
                    if (IsLocalAddress(receiverAddress.BaseAddress))
                    {
                        ValueNumber type = _store.VNForType(node.Type ?? node.RuntimeType ?? node.Field.FieldType);
                        ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.ByrefExposedLoad, type, receiver, byrefExposed);
                        return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
                    }

                    var nested = receiverAddress.Sequence.Append(node.Field);
                    return receiverAddress.IsStatic
                        ? LoadStaticFieldBySequence(node, nested, receiverAddress.Offset, heap, blockId, statementIndex)
                        : LoadInstanceFieldBySequence(node, receiverAddress.BaseAddress, nested, receiverAddress.Offset, heap, blockId, statementIndex);
                }

                return LoadInstanceFieldBySequence(
                    node,
                    receiver,
                    ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Instance),
                    0,
                    heap,
                    blockId,
                    statementIndex);
            }

            private ValueNumberPair LoadStaticField(GenTree node, ValueNumber heap, int blockId, int statementIndex)
            {
                if (node.Field is null)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ImmutableArray.Create(heap)));

                return LoadStaticFieldBySequence(
                    node,
                    ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Static),
                    0,
                    heap,
                    blockId,
                    statementIndex);
            }

            private ValueNumberPair LoadIndirect(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap, ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                ValueNumber addr = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Liberal);

                if (TryDecodeFieldAddress(addr, out var fieldAddress) && !IsLocalAddress(fieldAddress.BaseAddress))
                {
                    return fieldAddress.IsStatic
                        ? LoadStaticFieldBySequence(node, fieldAddress.Sequence, fieldAddress.Offset, heap, blockId, statementIndex)
                        : LoadInstanceFieldBySequence(node, fieldAddress.BaseAddress, fieldAddress.Sequence, fieldAddress.Offset, heap, blockId, statementIndex);
                }

                if (TryDecodeArrayElementAddress(addr, out var arrayAddress))
                    return LoadArrayElementBySelector(node, arrayAddress.ElementClass, arrayAddress.Array, arrayAddress.Index, arrayAddress.Offset, heap, blockId, statementIndex);

                ValueNumber memory = IsLocalAddress(addr) || (TryDecodeFieldAddress(addr, out var localFieldAddress) && IsLocalAddress(localFieldAddress.BaseAddress))
                    ? byrefExposed
                    : OpaqueMemory(blockId);
                ValueNumber type = _store.VNForType(node.Type ?? node.RuntimeType);
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.ByrefExposedLoad, type, addr, memory);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreField(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                if (node.Field is null || operands.Length < 2)
                    return OpaqueStore(node, operands, ref heap, blockId);

                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localAccess) && localAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    GenStackKind fieldStackKind = StackKindOf(node.Field.FieldType);
                    ValueNumber value = Normalize(OperandNormal(operands, operands.Length - 1, ValueNumberCategory.Liberal), fieldStackKind, node.Field.FieldType);
                    ValueNumber slotSelector = _store.VNForSlot(localAccess.BaseSlot);
                    ValueNumber memory = byrefExposed;
                    ValueNumber localMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, memory, slotSelector);
                    ValueNumber physical = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, fieldStackKind)));
                    ValueNumber newLocalMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, localMap, physical, value);
                    ValueNumber updated = MapStore(memory, slotSelector, newLocalMap, blockId);
                    byrefExposed = updated;
                    return ValueNumberPair.Same(value);
                }

                ValueNumber receiver = OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                ValueNumber stored = OperandNormal(operands, 1, ValueNumberCategory.Liberal);
                if (TryDecodeFieldAddress(receiver, out var receiverAddress))
                {
                    if (IsLocalAddress(receiverAddress.BaseAddress))
                    {
                        byrefExposed = OpaqueMemory(blockId);
                        return ValueNumberPair.Same(stored);
                    }

                    var nested = receiverAddress.Sequence.Append(node.Field);
                    if (receiverAddress.IsStatic)
                        StoreStaticFieldBySequence(node, nested, receiverAddress.Offset, stored, ref heap, blockId, statementIndex);
                    else
                        StoreInstanceFieldBySequence(node, receiverAddress.BaseAddress, nested, receiverAddress.Offset, stored, ref heap, blockId, statementIndex);
                    return ValueNumberPair.Same(stored);
                }

                StoreInstanceFieldBySequence(
                    node,
                    receiver,
                    ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Instance),
                    0,
                    stored,
                    ref heap,
                    blockId,
                    statementIndex);
                return ValueNumberPair.Same(stored);
            }

            private ValueNumberPair StoreStaticField(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, int blockId, int statementIndex)
            {
                if (node.Field is null || operands.Length == 0)
                    return OpaqueStore(node, operands, ref heap, blockId);

                ValueNumber value = OperandNormal(operands, operands.Length - 1, ValueNumberCategory.Liberal);
                StoreStaticFieldBySequence(
                    node,
                    ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Static),
                    0,
                    value,
                    ref heap,
                    blockId,
                    statementIndex);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreIndirect(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                ValueNumber result = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, operands.Length - 1, ValueNumberCategory.Liberal);
                ValueNumber addr = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, 0, ValueNumberCategory.Liberal);

                if (TryDecodeFieldAddress(addr, out var fieldAddress))
                {
                    if (IsLocalAddress(fieldAddress.BaseAddress))
                    {
                        byrefExposed = OpaqueMemory(blockId);
                        return ValueNumberPair.Same(result);
                    }

                    if (fieldAddress.IsStatic)
                        StoreStaticFieldBySequence(node, fieldAddress.Sequence, fieldAddress.Offset, result, ref heap, blockId, statementIndex);
                    else
                        StoreInstanceFieldBySequence(node, fieldAddress.BaseAddress, fieldAddress.Sequence, fieldAddress.Offset, result, ref heap, blockId, statementIndex);
                    return ValueNumberPair.Same(result);
                }

                if (TryDecodeArrayElementAddress(addr, out var arrayAddress))
                {
                    StoreArrayElementBySelector(node, arrayAddress.ElementClass, arrayAddress.Array, arrayAddress.Index, arrayAddress.Offset, result, ref heap, blockId, statementIndex);
                    return ValueNumberPair.Same(result);
                }

                if (IsLocalAddress(addr))
                    byrefExposed = OpaqueMemory(blockId);
                else
                {
                    heap = OpaqueMemory(blockId);
                    byrefExposed = OpaqueMemory(blockId);
                }
                return ValueNumberPair.Same(result);
            }

            private ValueNumberPair ArrayElementAddress(GenTree node, ImmutableArray<ValueNumberPair> operands)
            {
                if (operands.Length < 2)
                    return Func(node, ValueNumberFunction.LdElemA, operands);

                var args = ImmutableArray.Create(
                    ArrayElementAliasSelector(node),
                    OperandNormal(operands, 0, ValueNumberCategory.Liberal),
                    OperandNormal(operands, 1, ValueNumberCategory.Liberal),
                    _store.VNForInt32(0));
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.PtrToArrElem, args);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private ValueNumberPair LoadArrayElement(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap, int blockId, int statementIndex)
            {
                if (operands.Length < 2)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ArgsFromPairs(operands).Add(heap)));

                return LoadArrayElementBySelector(node, ArrayElementAliasSelector(node), OperandNormal(operands, 0, ValueNumberCategory.Liberal), OperandNormal(operands, 1, ValueNumberCategory.Liberal), 0, heap, blockId, statementIndex);
            }

            private ValueNumberPair StoreArrayElement(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, int blockId, int statementIndex)
            {
                if (operands.Length < 3)
                    return OpaqueStore(node, operands, ref heap, blockId);

                ValueNumber typeSelector = ArrayElementAliasSelector(node);
                ValueNumber array = OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                ValueNumber index = OperandNormal(operands, 1, ValueNumberCategory.Liberal);
                ValueNumber value = OperandNormal(operands, 2, ValueNumberCategory.Liberal);
                StoreArrayElementBySelector(node, typeSelector, array, index, 0, value, ref heap, blockId, statementIndex);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair LoadInstanceFieldBySequence(GenTree node, ValueNumber obj, ValueNumberFieldSequence sequence, int extraOffset, ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber fieldSelector = _store.VNForFieldSequence(sequence.Primary());
                ValueNumber fieldMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, fieldSelector);
                ValueNumber objMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, fieldMap, obj);
                ValueNumber physical = _store.VNForPhysicalSelector(checked(sequence.OffsetWithinPrimary + extraOffset), AccessSize(node, sequence.LastField.FieldType));
                ValueNumber value = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, objMap, physical);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private ValueNumberPair LoadStaticFieldBySequence(GenTree node, ValueNumberFieldSequence sequence, int extraOffset, ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber fieldSelector = _store.VNForFieldSequence(sequence.Primary());
                ValueNumber fieldMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, fieldSelector);
                ValueNumber physical = _store.VNForPhysicalSelector(checked(sequence.OffsetWithinPrimary + extraOffset), AccessSize(node, sequence.LastField.FieldType));
                ValueNumber value = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, fieldMap, physical);
                return ValueNumberPair.Same(value);
            }

            private void StoreInstanceFieldBySequence(GenTree node, ValueNumber obj, ValueNumberFieldSequence sequence, int extraOffset, ValueNumber value, ref ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber fieldSelector = _store.VNForFieldSequence(sequence.Primary());
                ValueNumber fieldMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, fieldSelector);
                ValueNumber objMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, fieldMap, obj);
                ValueNumber physical = _store.VNForPhysicalSelector(checked(sequence.OffsetWithinPrimary + extraOffset), AccessSize(node, sequence.LastField.FieldType));
                ValueNumber normalized = Normalize(value, AccessStackKind(node, sequence.LastField.FieldType), AccessRuntimeType(node, sequence.LastField.FieldType));
                ValueNumber newObjMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, objMap, physical, normalized);
                ValueNumber newFieldMap = MapStore(fieldMap, obj, newObjMap, blockId);
                heap = MapStore(heap, fieldSelector, newFieldMap, blockId);
            }

            private void StoreStaticFieldBySequence(GenTree node, ValueNumberFieldSequence sequence, int extraOffset, ValueNumber value, ref ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber fieldSelector = _store.VNForFieldSequence(sequence.Primary());
                ValueNumber fieldMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, fieldSelector);
                ValueNumber physical = _store.VNForPhysicalSelector(checked(sequence.OffsetWithinPrimary + extraOffset), AccessSize(node, sequence.LastField.FieldType));
                ValueNumber normalized = Normalize(value, AccessStackKind(node, sequence.LastField.FieldType), AccessRuntimeType(node, sequence.LastField.FieldType));
                ValueNumber newFieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, fieldMap, physical, normalized);
                heap = MapStore(heap, fieldSelector, newFieldMap, blockId);
            }

            private ValueNumberPair LoadArrayElementBySelector(GenTree node, ValueNumber typeSelector, ValueNumber array, ValueNumber index, int extraOffset, ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber arrayMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, typeSelector);
                ValueNumber objectMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, arrayMap, array);
                ValueNumber indexMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, objectMap, index);
                ValueNumber selector = _store.VNForPhysicalSelector(extraOffset, AccessSize(node, node.Type ?? node.RuntimeType));
                ValueNumber value = SelectMemoryMap(node, blockId, statementIndex, node.StackKind, node.Type, indexMap, selector);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private void StoreArrayElementBySelector(GenTree node, ValueNumber typeSelector, ValueNumber array, ValueNumber index, int extraOffset, ValueNumber value, ref ValueNumber heap, int blockId, int statementIndex)
            {
                ValueNumber arrayMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, heap, typeSelector);
                ValueNumber objectMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, arrayMap, array);
                ValueNumber indexMap = SelectMemoryMap(node, blockId, statementIndex, GenStackKind.Unknown, null, objectMap, index);
                ValueNumber selector = _store.VNForPhysicalSelector(extraOffset, AccessSize(node, node.Type ?? node.RuntimeType));
                ValueNumber normalized = Normalize(value, AccessStackKind(node, node.Type ?? node.RuntimeType), AccessRuntimeType(node, node.Type ?? node.RuntimeType));
                ValueNumber newIndexMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, indexMap, selector, normalized);
                ValueNumber newObjectMap = MapStore(objectMap, index, newIndexMap, blockId);
                ValueNumber newArrayMap = MapStore(arrayMap, array, newObjectMap, blockId);
                heap = MapStore(heap, typeSelector, newArrayMap, blockId);
            }

            private ValueNumberPair FieldAddress(GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId)
            {
                if (node.Field is null || operands.Length == 0)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                ValueNumber receiver = OperandNormal(operands, 0, ValueNumberCategory.Liberal);
                if (TryDecodeFieldAddress(receiver, out var receiverAddress))
                {
                    var nested = receiverAddress.Sequence.Append(node.Field);
                    ValueNumber sequence = _store.VNForFieldSequence(nested);
                    ValueNumber offset = _store.VNForInt32(receiverAddress.Offset);
                    ValueNumber vn = receiverAddress.IsStatic
                        ? _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.PtrToStatic, receiverAddress.BaseAddress, sequence, offset)
                        : _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.FieldAddr, receiverAddress.BaseAddress, sequence, offset);
                    return node.CanThrow ? WithException(node, vn) : ValueNumberPair.Same(vn);
                }

                var fieldSequence = ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Instance);
                ValueNumber value = _store.VNForFunc(
                    node.StackKind,
                    node.Type,
                    ValueNumberFunction.FieldAddr,
                    receiver,
                    _store.VNForFieldSequence(fieldSequence),
                    _store.VNForInt32(0));
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private bool TryDecodeFieldAddress(ValueNumber value, out FieldAddressInfo info)
            {
                if (_store.TryGetEntry(value, out var entry))
                {
                    bool isFieldAddr = entry.Function == ValueNumberFunction.FieldAddr && entry.Args.Length >= 3;
                    bool isStaticAddr = entry.Function == ValueNumberFunction.PtrToStatic && entry.Args.Length >= 3;
                    if ((isFieldAddr || isStaticAddr) &&
                        _store.TryGetFieldSequence(entry.Args[1], out var sequence) &&
                        TryGetInt32(entry.Args[2], out int offset))
                    {
                        info = new FieldAddressInfo(isStaticAddr, entry.Args[0], sequence, offset);
                        return true;
                    }
                }

                info = default;
                return false;
            }

            private bool TryDecodeArrayElementAddress(ValueNumber value, out ArrayElementAddressInfo info)
            {
                if (_store.TryGetEntry(value, out var entry) &&
                    entry.Function == ValueNumberFunction.PtrToArrElem &&
                    entry.Args.Length >= 4 &&
                    TryGetInt32(entry.Args[3], out int offset))
                {
                    info = new ArrayElementAddressInfo(entry.Args[0], entry.Args[1], entry.Args[2], offset);
                    return true;
                }

                info = default;
                return false;
            }

            private bool TryGetInt32(ValueNumber vn, out int value)
            {
                if (_store.TryGetConstant(vn, out var constant) && constant.Kind == ValueNumberConstantKind.Int32)
                {
                    value = checked((int)constant.A);
                    return true;
                }

                value = 0;
                return false;
            }

            private static int AccessSize(GenTree node, RuntimeType? fallbackType)
                => Math.Max(1, StorageSize(AccessRuntimeType(node, fallbackType), AccessStackKind(node, fallbackType)));

            private static RuntimeType? AccessRuntimeType(GenTree node, RuntimeType? fallbackType)
                => node.Type ?? node.RuntimeType ?? fallbackType;

            private static GenStackKind AccessStackKind(GenTree node, RuntimeType? fallbackType)
                => node.StackKind == GenStackKind.Void || node.StackKind == GenStackKind.Unknown
                    ? StackKindOf(AccessRuntimeType(node, fallbackType))
                    : node.StackKind;

            private ValueNumberPair NewArray(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, int blockId)
            {
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.NewArray, ArgsFromPairs(operands).Add(_store.VNForType(node.RuntimeType ?? node.Type)));
                heap = OpaqueMemory(blockId);
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair NewObject(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, int blockId)
            {
                var newObjArgs = ArgsFromPairs(operands);
                if (node.Method is not null)
                    newObjArgs = newObjArgs.Add(_store.VNForMethod(node.Method));
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.NewObject, newObjArgs);
                heap = OpaqueMemory(blockId);
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair Call(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, int blockId)
            {
                var args = ArgsFromPairs(operands);
                if (node.Method is not null)
                    args = args.Add(_store.VNForMethod(node.Method));
                args = args.Add(heap).Add(byrefExposed);
                ValueNumberFunction func = node.Kind == GenTreeKind.VirtualCall ? ValueNumberFunction.VirtualCall : ValueNumberFunction.Call;
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, func, args);
                heap = OpaqueMemory(blockId);
                byrefExposed = OpaqueMemory(blockId);
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair LocalAddress(GenTree node, int blockId)
            {
                if (!SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot))
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                ValueNumber vn = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.PtrToLoc, _store.VNForSlot(slot), _store.VNForInt32(0));
                return ValueNumberPair.Same(vn);
            }

            private ValueNumberPair StaticFieldAddress(GenTree node, int blockId)
            {
                if (node.Field is null)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId)));

                var sequence = ValueNumberFieldSequence.Create(node.Field, ValueNumberFieldSequenceKind.Static);
                ValueNumber vn = _store.VNForFunc(
                    node.StackKind,
                    node.Type,
                    ValueNumberFunction.PtrToStatic,
                    _store.VNForInt32(0),
                    _store.VNForFieldSequence(sequence),
                    _store.VNForInt32(0));
                return ValueNumberPair.Same(vn);
            }

            private ValueNumberPair Func(GenTree node, ValueNumberFunction function, ImmutableArray<ValueNumberPair> operands)
                => ValueNumberPair.Same(_store.VNForFunc(node.StackKind, node.Type, function, ArgsFromPairs(operands)));

            private ValueNumberPair Func(GenTree node, ValueNumberFunction function, ImmutableArray<ValueNumberPair> operands, ValueNumber extra)
                => ValueNumberPair.Same(_store.VNForFunc(node.StackKind, node.Type, function, ArgsFromPairs(operands).Add(extra)));

            private ValueNumberPair FuncWithException(GenTree node, ValueNumberFunction function, ImmutableArray<ValueNumberPair> operands)
            {
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, function, ArgsFromPairs(operands).Add(_store.VNForType(node.RuntimeType ?? node.Type)));
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair FuncWithException(GenTree node, ValueNumberFunction function, ImmutableArray<ValueNumberPair> operands, ValueNumber extra)
            {
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, function, ArgsFromPairs(operands).Add(extra));
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair Opaque(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                ValueNumber liberal = ProducesValue(node)
                    ? _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, OpaqueArgs(blockId))
                    : ValueNumberStore.NoVN;

                if (node.WritesMemory || node.ContainsCall || node.HasSideEffect)
                {
                    heap = OpaqueMemory(blockId);
                    byrefExposed = OpaqueMemory(blockId);
                }

                return node.CanThrow && liberal.IsValid ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair OpaqueByrefExposedStore(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber byrefExposed, int blockId)
            {
                ValueNumber value = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, operands.Length - 1, ValueNumberCategory.Liberal);
                byrefExposed = OpaqueMemory(blockId);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair OpaqueStore(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, int blockId)
            {
                ValueNumber value = operands.Length == 0 ? ValueNumberStore.NoVN : OperandNormal(operands, operands.Length - 1, ValueNumberCategory.Liberal);
                heap = OpaqueMemory(blockId);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair AttachTreeExceptions(GenTree node, ValueNumberPair value, ImmutableArray<ValueNumberPair> operands)
            {
                ValueNumber exceptionSet = ExceptionSetFromOperands(operands);
                exceptionSet = _store.VNExcSetUnion(exceptionSet, _store.VNExceptionSet(value.Liberal));
                exceptionSet = _store.VNExcSetUnion(exceptionSet, _store.VNExceptionSet(value.Conservative));
                exceptionSet = _store.VNExcSetUnion(exceptionSet, OwnExceptionSet(node, operands, value));
                return ApplyExceptionSet(value, exceptionSet);
            }

            private ValueNumberPair ApplyExceptionSet(ValueNumberPair value, ValueNumber exceptionSet)
            {
                ValueNumber liberalNormal = _store.VNNormalValue(value.Liberal);
                ValueNumber conservativeNormal = _store.VNNormalValue(value.Conservative);

                if (!liberalNormal.IsValid && !conservativeNormal.IsValid)
                {
                    if (_store.IsEmptyExcSet(exceptionSet))
                        return ValueNumberPair.Same(ValueNumberStore.NoVN);

                    liberalNormal = _store.VNForVoid();
                    conservativeNormal = liberalNormal;
                }

                ValueNumber conservative = _store.VNWithExc(conservativeNormal, exceptionSet);
                return liberalNormal == conservative
                    ? ValueNumberPair.Same(liberalNormal)
                    : new ValueNumberPair(liberalNormal, conservative);
            }

            private ValueNumber ExceptionSetFromOperands(ImmutableArray<ValueNumberPair> operands)
            {
                ValueNumber result = _store.VNForEmptyExcSet();
                if (operands.IsDefaultOrEmpty)
                    return result;

                for (int i = 0; i < operands.Length; i++)
                {
                    result = _store.VNExcSetUnion(result, _store.VNExceptionSet(operands[i].Liberal));
                    result = _store.VNExcSetUnion(result, _store.VNExceptionSet(operands[i].Conservative));
                }

                return result;
            }

            private ValueNumber OwnExceptionSet(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumberPair value)
            {
                if (!node.CanThrow && node.Kind != GenTreeKind.Throw && node.Kind != GenTreeKind.Rethrow)
                    return _store.VNForEmptyExcSet();

                ValueNumber result = _store.VNForEmptyExcSet();
                ValueNumber normalValue = _store.VNNormalValue(value.Conservative.IsValid ? value.Conservative : value.Liberal);
                if (!normalValue.IsValid)
                    normalValue = _store.VNForVoid();

                switch (node.Kind)
                {
                    case GenTreeKind.Binary:
                        if (IsCheckedOverflowBinaryOp(node.SourceOp))
                            result = AddException(result, ValueNumberFunction.OverflowExc, normalValue);

                        if (node.SourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                        {
                            result = AddException(result, ValueNumberFunction.DivideByZeroExc, OperandNormal(operands, 1));
                            if (node.SourceOp is BytecodeOp.Div or BytecodeOp.Rem)
                                result = AddException(result, ValueNumberFunction.ArithmeticExc, normalValue, _store.VNForInt32((int)node.SourceOp));
                        }
                        break;

                    case GenTreeKind.Conv:
                        if ((node.ConvFlags & NumericConvFlags.Checked) != 0)
                        {
                            ValueNumber convTarget = _store.VNForInt32(((int)node.ConvKind << 8) | ((int)node.ConvFlags & 0xff));
                            result = AddException(result, ValueNumberFunction.ConvOverflowExc, OperandNormal(operands, 0), convTarget);
                        }
                        break;

                    case GenTreeKind.Field:
                    case GenTreeKind.FieldAddr:
                    case GenTreeKind.StoreField:
                        if (operands.Length != 0)
                            result = AddException(result, ValueNumberFunction.NullPtrExc, OperandNormal(operands, 0));
                        break;

                    case GenTreeKind.LoadIndirect:
                    case GenTreeKind.StoreIndirect:
                        if (operands.Length != 0)
                            result = AddException(result, ValueNumberFunction.NullPtrExc, OperandNormal(operands, 0));
                        break;

                    case GenTreeKind.ArrayElement:
                    case GenTreeKind.ArrayElementAddr:
                    case GenTreeKind.StoreArrayElement:
                        if (operands.Length >= 2)
                        {
                            ValueNumber array = OperandNormal(operands, 0);
                            ValueNumber index = OperandNormal(operands, 1);
                            ValueNumber length = _store.VNForFunc(GenStackKind.I4, null, ValueNumberFunction.ArrayLength, array);
                            result = AddException(result, ValueNumberFunction.NullPtrExc, array);
                            result = AddException(result, ValueNumberFunction.IndexOutOfRangeExc, length, index);
                        }
                        break;

                    case GenTreeKind.ArrayDataRef:
                        if (operands.Length != 0)
                            result = AddException(result, ValueNumberFunction.NullPtrExc, OperandNormal(operands, 0));
                        break;

                    case GenTreeKind.NewArray:
                        result = AddException(result, ValueNumberFunction.NewArrOverflowExc, OperandNormal(operands, 0));
                        result = AddHelperOpaqueException(result, node);
                        break;

                    case GenTreeKind.CastClass:
                        result = AddException(result, ValueNumberFunction.InvalidCastExc, OperandNormal(operands, 0), _store.VNForType(node.RuntimeType ?? node.Type));
                        break;

                    case GenTreeKind.UnboxAny:
                        result = AddException(result, ValueNumberFunction.NullPtrExc, OperandNormal(operands, 0));
                        result = AddException(result, ValueNumberFunction.InvalidCastExc, OperandNormal(operands, 0), _store.VNForType(node.RuntimeType ?? node.Type));
                        break;

                    case GenTreeKind.VirtualCall:
                    case GenTreeKind.DelegateInvoke:
                        if (operands.Length != 0)
                            result = AddException(result, ValueNumberFunction.NullPtrExc, OperandNormal(operands, 0));
                        result = AddHelperOpaqueException(result, node);
                        break;

                    case GenTreeKind.Call:
                    case GenTreeKind.NewObject:
                    case GenTreeKind.NewDelegate:
                    case GenTreeKind.DelegateCombine:
                    case GenTreeKind.DelegateRemove:
                    case GenTreeKind.Box:
                    case GenTreeKind.StackAlloc:
                    case GenTreeKind.StaticField:
                    case GenTreeKind.StaticFieldAddr:
                    case GenTreeKind.StoreStaticField:
                    case GenTreeKind.Throw:
                    case GenTreeKind.Rethrow:
                        result = AddHelperOpaqueException(result, node);
                        break;
                }

                return result;
            }

            private ValueNumber OperandNormal(ImmutableArray<ValueNumberPair> operands, int index)
                => OperandNormal(operands, index, ValueNumberCategory.Conservative);

            private ValueNumber OperandNormal(ImmutableArray<ValueNumberPair> operands, int index, ValueNumberCategory category)
            {
                if ((uint)index >= (uint)operands.Length)
                    return ValueNumberStore.NoVN;
                return _store.VNNormalValue(operands[index][category]);
            }

            private ValueNumber AddException(ValueNumber set, ValueNumberFunction function, params ValueNumber[] args)
            {
                ValueNumber exception = _store.VNForException(function, args);
                return _store.VNExcSetUnion(set, _store.VNExcSetSingleton(exception));
            }

            private ValueNumber AddHelperOpaqueException(ValueNumber set, GenTree node)
            {
                ValueNumber key;
                if (node.Method is not null)
                    key = _store.VNForMethod(node.Method);
                else if (node.Field is not null)
                    key = _store.VNForField(node.Field);
                else if (node.RuntimeType is not null || node.Type is not null)
                    key = _store.VNForType(node.RuntimeType ?? node.Type);
                else
                    key = _store.VNForInt32(node.Id);

                return AddException(set, ValueNumberFunction.HelperOpaqueExc, key);
            }

            private ValueNumberPair WithException(GenTree node, ValueNumber liberal)
                => WithException(node, liberal, liberal);

            private ValueNumberPair WithException(GenTree node, ValueNumber liberal, ValueNumber conservativeValue)
            {
                return liberal == conservativeValue
                    ? ValueNumberPair.Same(liberal)
                    : new ValueNumberPair(liberal, conservativeValue);
            }

            private ValueNumberPair NormalizeStore(ValueNumberPair value, GenStackKind stackKind, RuntimeType? type)
            {
                ValueNumber liberal = Normalize(value.Liberal, stackKind, type);
                ValueNumber conservative = value.BothEqual ? liberal : Normalize(value.Conservative, stackKind, type);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumber Normalize(ValueNumber value, GenStackKind stackKind, RuntimeType? type)
            {
                ValueNumber normal = _store.VNNormalValue(value);

                if (!_store.TryGetEntry(normal, out var entry))
                {
                    return normal;
                }
                if (entry.StackKind == stackKind && SameRuntimeType(entry.Type, type))
                {
                    return normal;
                }

                return _store.VNForFunc(stackKind, type, ValueNumberFunction.BitCast, normal, _store.VNForCanonicalType(type));
            }

            private ValueNumberPair InitialValueFor(SsaValueName name)
            {
                if (_ssaValues.TryGetValue(name, out var value))
                    return value;

                var info = GetSlotInfo(name.Slot);
                ValueNumber init = _store.VNForFunc(info.StackKind, info.Type, ValueNumberFunction.InitVal, _store.VNForSlot(name.Slot), _store.VNForInt32(name.Version));
                value = ValueNumberPair.Same(init);
                SetSsaValue(name, value);
                return value;
            }

            private ValueNumberPair GetSsaValue(SsaValueName name)
            {
                if (_ssaValues.TryGetValue(name, out var value))
                    return value;
                return InitialValueFor(name);
            }

            private ValueNumber GetMemoryValue(SsaMemoryValueName name)
            {
                if (_memoryValues.TryGetValue(name, out var value))
                    return value;

                value = InitialMemoryValue(name.Kind);
                SetMemoryValue(name, value);
                return value;
            }

            private void SetMemoryValue(SsaMemoryValueName name, ValueNumber value)
            {
                if (_memoryValues.TryGetValue(name, out var existing) && existing == value)
                    return;

                _memoryValues[name] = value;
                if (_memoryDefinitions.TryGetValue(name, out var definition))
                    definition.Descriptor.SetValueNumber(value);
                _changedThisPass = true;
            }

            private void SetSsaValue(SsaValueName name, ValueNumberPair value)
            {
                var normalValue = new ValueNumberPair(
                    _store.VNNormalValue(value.Liberal),
                    _store.VNNormalValue(value.Conservative));
                if (_ssaValues.TryGetValue(name, out var existing) && existing.Equals(normalValue))
                    return;

                _ssaValues[name] = normalValue;
                if (_ssaDescriptors.TryGetValue(name, out var descriptor))
                    descriptor.SetValueNumbers(normalValue);
                _changedThisPass = true;
            }

            private void SetHeapIn(int blockId, ValueNumber value)
            {
                if (_heapIn.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _heapIn[blockId] = value;
                _changedThisPass = true;
            }

            private void SetHeapOut(int blockId, ValueNumber value)
            {
                if (_heapOut.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _heapOut[blockId] = value;
                _changedThisPass = true;
            }

            private void SetByrefExposedIn(int blockId, ValueNumber value)
            {
                if (_byrefExposedIn.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _byrefExposedIn[blockId] = value;
                _changedThisPass = true;
            }

            private void SetByrefExposedOut(int blockId, ValueNumber value)
            {
                if (_byrefExposedOut.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _byrefExposedOut[blockId] = value;
                _changedThisPass = true;
            }

            private void Remember(GenTree node, ValueNumberPair value)
            {
                if (node is not null && value.Liberal.IsValid)
                    _treeValues[node] = value;
            }

            private SsaSlotInfo GetSlotInfo(SsaSlot slot)
            {
                if (_slotInfos.TryGetValue(slot, out var info))
                    return info;
                return new SsaSlotInfo(slot, null, GenStackKind.Unknown, addressExposed: true, memoryAliased: true, category: GenLocalCategory.AddressExposedLocal);
            }

            private bool IsLocalAddress(ValueNumber value)
            {
                if (!_store.TryGetEntry(value, out var entry))
                    return false;
                return entry.Function == ValueNumberFunction.PtrToLoc && entry.Args.Length != 0;
            }

            private ValueNumber DefaultValue(RuntimeType? type, GenStackKind stackKind)
            {
                if (stackKind == GenStackKind.Ref || stackKind == GenStackKind.ByRef || stackKind == GenStackKind.Null)
                    return _store.VNForNull(type);
                if (stackKind == GenStackKind.I8)
                    return _store.VNForInt64(0);
                if (stackKind == GenStackKind.R4)
                    return _store.VNForFloat32Bits(0);
                if (stackKind == GenStackKind.R8)
                    return _store.VNForFloat64Bits(0);
                return _store.VNForInt32(0);
            }

            private ValueNumber ExtraFieldArg(GenTree node)
                => node.Field is null ? _store.VNForInt32(node.Int32) : _store.VNForField(node.Field);

            private static int StableSyntheticId(int blockId, int statementIndex, ValueNumberFunction function)
                => HashCode.Combine(blockId, statementIndex, (int)function);

            private ValueNumber OpaqueMemory(int blockId)
                => _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, LoopNumberVNForBlock(blockId));

            private ImmutableArray<ValueNumber> OpaqueArgs(int blockId)
                => ImmutableArray.Create(LoopNumberVNForBlock(blockId));

            private ValueNumber LoopNumberVNForBlock(int blockId)
                => _store.VNForInt32(LoopNumberForBlock(blockId));

            private ImmutableArray<ValueNumber> ArgsFromPairs(ImmutableArray<ValueNumberPair> operands)
                => ArgsFromPairs(operands, ValueNumberCategory.Liberal);

            private ImmutableArray<ValueNumber> ArgsFromPairs(ImmutableArray<ValueNumberPair> operands, ValueNumberCategory category)
            {
                if (operands.IsDefaultOrEmpty)
                    return ImmutableArray<ValueNumber>.Empty;
                var builder = ImmutableArray.CreateBuilder<ValueNumber>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    builder.Add(_store.VNNormalValue(operands[i][category]));
                return builder.ToImmutable();
            }

            private ValueNumber MapStore(ValueNumber map, ValueNumber selector, ValueNumber value, int blockId)
                => _store.VNForFunc(
                    GenStackKind.Unknown,
                    null,
                    ValueNumberFunction.MapStore,
                    map,
                    selector,
                    value,
                    _store.VNForInt32(LoopNumberForBlock(blockId)));

            private int LoopNumberForBlock(int blockId)
                => (uint)blockId < (uint)_loopNumberByBlock.Length ? _loopNumberByBlock[blockId] : -1;

            private static int[] BuildLoopNumberByBlock(ControlFlowGraph cfg)
            {
                var result = new int[cfg.Blocks.Length];
                var bestSize = new int[cfg.Blocks.Length];
                var bestDepth = new int[cfg.Blocks.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = -1;
                    bestSize[i] = int.MaxValue;
                    bestDepth[i] = -1;
                }

                if (cfg.NaturalLoops.IsDefaultOrEmpty)
                    return result;

                for (int i = 0; i < cfg.NaturalLoops.Length; i++)
                {
                    var loop = cfg.NaturalLoops[i];
                    int size = loop.Blocks.IsDefaultOrEmpty ? int.MaxValue - 1 : loop.Blocks.Length;
                    for (int j = 0; j < loop.Blocks.Length; j++)
                    {
                        int blockId = loop.Blocks[j];
                        if ((uint)blockId >= (uint)result.Length)
                            continue;
                        if (loop.Depth > bestDepth[blockId] || (loop.Depth == bestDepth[blockId] && size <= bestSize[blockId]))
                        {
                            result[blockId] = loop.Index;
                            bestDepth[blockId] = loop.Depth;
                            bestSize[blockId] = size;
                        }
                    }
                }

                return result;
            }

            private static bool IsCheckedOverflowBinaryOp(BytecodeOp op)
                => op is BytecodeOp.Add_Ovf or BytecodeOp.Add_Ovf_Un
                    or BytecodeOp.Sub_Ovf or BytecodeOp.Sub_Ovf_Un
                    or BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un;
            private static ValueNumberFunction BinaryFunction(BytecodeOp op)
            {
                return op switch
                {
                    BytecodeOp.Add or BytecodeOp.Add_Ovf or BytecodeOp.Add_Ovf_Un => ValueNumberFunction.Add,
                    BytecodeOp.Sub or BytecodeOp.Sub_Ovf or BytecodeOp.Sub_Ovf_Un => ValueNumberFunction.Sub,
                    BytecodeOp.Mul or BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un => ValueNumberFunction.Mul,
                    BytecodeOp.Div => ValueNumberFunction.Div,
                    BytecodeOp.Div_Un => ValueNumberFunction.DivUn,
                    BytecodeOp.Rem => ValueNumberFunction.Rem,
                    BytecodeOp.Rem_Un => ValueNumberFunction.RemUn,
                    BytecodeOp.And => ValueNumberFunction.And,
                    BytecodeOp.Or => ValueNumberFunction.Or,
                    BytecodeOp.Xor => ValueNumberFunction.Xor,
                    BytecodeOp.Shl => ValueNumberFunction.Shl,
                    BytecodeOp.Shr => ValueNumberFunction.Shr,
                    BytecodeOp.Shr_Un => ValueNumberFunction.ShrUn,
                    BytecodeOp.Ceq => ValueNumberFunction.Ceq,
                    BytecodeOp.Clt => ValueNumberFunction.Clt,
                    BytecodeOp.Clt_Un => ValueNumberFunction.CltUn,
                    BytecodeOp.Cgt => ValueNumberFunction.Cgt,
                    BytecodeOp.Cgt_Un => ValueNumberFunction.CgtUn,
                    _ => ValueNumberFunction.None,
                };
            }

            private static GenStackKind StackKindOf(RuntimeType? type)
            {
                if (type is null)
                    return GenStackKind.Unknown;

                if (type.Namespace == "System" && type.Name == "Void")
                    return GenStackKind.Void;

                if (type.IsReferenceType)
                    return GenStackKind.Ref;

                if (type.Kind == RuntimeTypeKind.Pointer)
                    return GenStackKind.Ptr;

                if (type.Kind == RuntimeTypeKind.ByRef)
                    return GenStackKind.ByRef;

                if (type.Kind == RuntimeTypeKind.TypeParam)
                    return GenStackKind.Value;

                if (type.Kind == RuntimeTypeKind.Enum)
                    return type.SizeOf <= 4 ? GenStackKind.I4 : GenStackKind.I8;

                switch (type.PrimitiveKind)
                {
                    case RuntimePrimitiveKind.Void:
                        return GenStackKind.Void;
                    case RuntimePrimitiveKind.Boolean:
                    case RuntimePrimitiveKind.Char:
                    case RuntimePrimitiveKind.Int8:
                    case RuntimePrimitiveKind.UInt8:
                    case RuntimePrimitiveKind.Int16:
                    case RuntimePrimitiveKind.UInt16:
                    case RuntimePrimitiveKind.Int32:
                    case RuntimePrimitiveKind.UInt32:
                        return GenStackKind.I4;
                    case RuntimePrimitiveKind.Int64:
                    case RuntimePrimitiveKind.UInt64:
                        return GenStackKind.I8;
                    case RuntimePrimitiveKind.Single:
                        return GenStackKind.R4;
                    case RuntimePrimitiveKind.Double:
                        return GenStackKind.R8;
                    case RuntimePrimitiveKind.NativeInt:
                        return GenStackKind.NativeInt;
                    case RuntimePrimitiveKind.NativeUInt:
                        return GenStackKind.NativeUInt;
                }

                if (type.Namespace == "System")
                {
                    switch (type.Name)
                    {
                        case "Boolean":
                        case "Char":
                        case "SByte":
                        case "Byte":
                        case "Int16":
                        case "UInt16":
                        case "Int32":
                        case "UInt32":
                            return GenStackKind.I4;
                        case "Int64":
                        case "UInt64":
                            return GenStackKind.I8;
                        case "Single":
                            return GenStackKind.R4;
                        case "Double":
                            return GenStackKind.R8;
                        case "IntPtr":
                            return GenStackKind.NativeInt;
                        case "UIntPtr":
                            return GenStackKind.NativeUInt;
                    }
                }

                return GenStackKind.Value;
            }

            private static int StorageSize(RuntimeType? type, GenStackKind stackKind)
            {
                if (type is not null && type.SizeOf > 0)
                    return type.SizeOf;
                switch (stackKind)
                {
                    case GenStackKind.I8:
                    case GenStackKind.R8:
                        return 8;
                    case GenStackKind.R4:
                        return 4;
                    case GenStackKind.NativeInt:
                    case GenStackKind.NativeUInt:
                    case GenStackKind.Ptr:
                    case GenStackKind.Ref:
                    case GenStackKind.ByRef:
                        return TargetArchitecture.PointerSize;
                    case GenStackKind.Value:
                        return Math.Max(1, type?.SizeOf ?? TargetArchitecture.PointerSize);
                    default:
                        return 4;
                }
            }

            private const int ArrayAliasUnknown = 0;
            private const int ArrayAliasReference = 1;
            private const int ArrayAliasInt8 = 2;
            private const int ArrayAliasInt16 = 3;
            private const int ArrayAliasInt32 = 4;
            private const int ArrayAliasInt64 = 5;
            private const int ArrayAliasNativeInt = 6;
            private const int ArrayAliasFloat32 = 7;
            private const int ArrayAliasFloat64 = 8;
            private const int ArrayAliasExactStruct = 9;
            private const int ArrayAliasBoolean = 10;

            private readonly struct ArrayElementAliasClass
            {
                public readonly int EquivalenceClass;
                public readonly int Size;
                public readonly int ExactTypeId;

                public ArrayElementAliasClass(int equivalenceClass, int size, int exactTypeId)
                {
                    EquivalenceClass = equivalenceClass;
                    Size = Math.Max(0, size);
                    ExactTypeId = exactTypeId;
                }
            }

            private ValueNumber ArrayElementAliasSelector(GenTree node)
            {
                RuntimeType? type = node.RuntimeType ?? node.Type;
                GenStackKind fallbackKind = node.Kind == GenTreeKind.ArrayElementAddr ? GenStackKind.Unknown : node.StackKind;
                var aliasClass = GetArrayElementAliasClass(type, fallbackKind);
                return _store.VNForArrayElementClass(aliasClass.EquivalenceClass, aliasClass.Size, aliasClass.ExactTypeId);
            }

            private static ArrayElementAliasClass GetArrayElementAliasClass(RuntimeType? type, GenStackKind fallbackKind)
            {
                if (type is null)
                    return new ArrayElementAliasClass(ArrayAliasUnknown, StorageSize(null, fallbackKind), 0);

                RuntimeType elementType = type.Kind == RuntimeTypeKind.Array && type.ElementType is not null ? type.ElementType : type;
                GenStackKind kind = fallbackKind;

                if (elementType.IsReferenceType)
                    return new ArrayElementAliasClass(ArrayAliasReference, TargetArchitecture.PointerSize, RuntimeArrayElementTypeEquivalenceId(elementType));
                if (kind is GenStackKind.Ref or GenStackKind.Null)
                    return new ArrayElementAliasClass(ArrayAliasReference, TargetArchitecture.PointerSize, 0);

                int size = Math.Max(1, StorageSize(elementType, kind));

                switch (elementType.PrimitiveKind)
                {
                    case RuntimePrimitiveKind.Boolean:
                        return new ArrayElementAliasClass(ArrayAliasBoolean, 1, 0);
                    case RuntimePrimitiveKind.Int8:
                    case RuntimePrimitiveKind.UInt8:
                        return new ArrayElementAliasClass(ArrayAliasInt8, 1, 0);
                    case RuntimePrimitiveKind.Char:
                    case RuntimePrimitiveKind.Int16:
                    case RuntimePrimitiveKind.UInt16:
                        return new ArrayElementAliasClass(ArrayAliasInt16, 2, 0);
                    case RuntimePrimitiveKind.Int32:
                    case RuntimePrimitiveKind.UInt32:
                        return new ArrayElementAliasClass(ArrayAliasInt32, 4, 0);
                    case RuntimePrimitiveKind.Int64:
                    case RuntimePrimitiveKind.UInt64:
                        return new ArrayElementAliasClass(ArrayAliasInt64, 8, 0);
                    case RuntimePrimitiveKind.NativeInt:
                    case RuntimePrimitiveKind.NativeUInt:
                        return new ArrayElementAliasClass(ArrayAliasNativeInt, TargetArchitecture.PointerSize, 0);
                    case RuntimePrimitiveKind.Single:
                        return new ArrayElementAliasClass(ArrayAliasFloat32, 4, 0);
                    case RuntimePrimitiveKind.Double:
                        return new ArrayElementAliasClass(ArrayAliasFloat64, 8, 0);
                }

                if (elementType.Kind == RuntimeTypeKind.Enum)
                    return IntegralArrayAliasClassForSize(size);

                if (kind == GenStackKind.R4)
                    return new ArrayElementAliasClass(ArrayAliasFloat32, 4, 0);
                if (kind == GenStackKind.R8)
                    return new ArrayElementAliasClass(ArrayAliasFloat64, 8, 0);
                if (kind is GenStackKind.NativeInt or GenStackKind.NativeUInt or GenStackKind.Ptr)
                    return new ArrayElementAliasClass(ArrayAliasNativeInt, TargetArchitecture.PointerSize, 0);
                if (kind is GenStackKind.I4 or GenStackKind.I8)
                    return IntegralArrayAliasClassForSize(size);

                if (elementType.IsValueType)
                    return new ArrayElementAliasClass(ArrayAliasExactStruct, size, RuntimeArrayElementTypeEquivalenceId(elementType));

                return new ArrayElementAliasClass(ArrayAliasUnknown, size, 0);
            }

            private static ArrayElementAliasClass IntegralArrayAliasClassForSize(int size)
            {
                return size switch
                {
                    1 => new ArrayElementAliasClass(ArrayAliasInt8, 1, 0),
                    2 => new ArrayElementAliasClass(ArrayAliasInt16, 2, 0),
                    4 => new ArrayElementAliasClass(ArrayAliasInt32, 4, 0),
                    8 => new ArrayElementAliasClass(ArrayAliasInt64, 8, 0),
                    _ => new ArrayElementAliasClass(ArrayAliasExactStruct, Math.Max(1, size), 0),
                };
            }

            private static int RuntimeArrayElementTypeEquivalenceId(RuntimeType type)
            {
                if (type is null)
                    return 0;

                int hash = RuntimeArrayElementTypeEquivalenceHash(type, new HashSet<int>());
                if (hash == int.MinValue)
                    return int.MaxValue;
                hash = Math.Abs(hash);
                return hash == 0 ? 1 : hash;
            }

            private static int RuntimeArrayElementTypeEquivalenceHash(RuntimeType type, HashSet<int> visiting)
            {
                unchecked
                {
                    int hash = Mix(0x51ed71a5, (int)type.Kind);
                    hash = Mix(hash, (int)type.PrimitiveKind);
                    hash = Mix(hash, Math.Max(1, type.SizeOf));
                    hash = Mix(hash, Math.Max(1, type.AlignOf));
                    hash = Mix(hash, type.ContainsGcPointers ? 1 : 0);

                    if (type.Kind == RuntimeTypeKind.Array)
                    {
                        hash = Mix(hash, type.ArrayRank);
                        hash = Mix(hash, type.ElementType is null ? 0 : RuntimeArrayElementTypeEquivalenceHash(type.ElementType, visiting));
                        return hash;
                    }

                    if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                    {
                        hash = Mix(hash, type.TypeId);
                        return hash;
                    }

                    if (!type.IsValueType)
                        return hash;

                    if (!visiting.Add(type.TypeId))
                        return Mix(hash, 0x421);

                    try
                    {
                        var fields = (RuntimeField[])(type.InstanceFields?.Clone() ?? Array.Empty<RuntimeField>());
                        Array.Sort(fields, static (left, right) =>
                        {
                            int c = left.Offset.CompareTo(right.Offset);
                            if (c != 0) return c;
                            c = Math.Max(1, left.FieldType.SizeOf).CompareTo(Math.Max(1, right.FieldType.SizeOf));
                            if (c != 0) return c;
                            return ((int)left.FieldType.Kind).CompareTo((int)right.FieldType.Kind);
                        });

                        hash = Mix(hash, fields.Length);
                        for (int i = 0; i < fields.Length; i++)
                        {
                            RuntimeField field = fields[i];
                            hash = Mix(hash, field.Offset);
                            hash = Mix(hash, Math.Max(1, field.FieldType.SizeOf));
                            hash = Mix(hash, field.FieldType.ContainsGcPointers ? 1 : 0);
                            hash = Mix(hash, RuntimeArrayElementTypeEquivalenceHash(field.FieldType, visiting));
                        }
                    }
                    finally
                    {
                        visiting.Remove(type.TypeId);
                    }

                    return hash;
                }
            }

            private static int Mix(int hash, int value)
            {
                unchecked
                {
                    hash ^= value + unchecked((int)0x9e3779b9) + (hash << 6) + (hash >> 2);
                    return hash;
                }
            }

            private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null) return false;
                if (left.IsValueType || right.IsValueType)
                    return left.IsValueType && right.IsValueType && Math.Max(1, left.SizeOf) == Math.Max(1, right.SizeOf);
                return left.TypeId == right.TypeId;
            }

            private static bool ProducesValue(GenTree node)
            {
                if (node.StackKind == GenStackKind.Void)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.Nop => false,
                    GenTreeKind.StoreIndirect => false,
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.StoreField => false,
                    GenTreeKind.StoreStaticField => false,
                    GenTreeKind.StoreArrayElement => false,
                    GenTreeKind.Eval => false,
                    GenTreeKind.Branch => false,
                    GenTreeKind.BranchTrue => false,
                    GenTreeKind.BranchFalse => false,
                    GenTreeKind.Return => false,
                    GenTreeKind.Throw => false,
                    GenTreeKind.Rethrow => false,
                    GenTreeKind.EndFinally => false,
                    _ => true,
                };
            }
        }
    }


}
