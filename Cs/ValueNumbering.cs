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

        private ValueNumberType(GenStackKind stackKind, RuntimeType? runtimeType)
        {
            StackKind = stackKind;
            RuntimeType = runtimeType;
            RuntimeTypeId = runtimeType?.TypeId ?? 0;
        }

        public static ValueNumberType For(GenStackKind stackKind, RuntimeType? runtimeType)
            => new ValueNumberType(stackKind, runtimeType);

        public bool Equals(ValueNumberType other)
            => StackKind == other.StackKind && RuntimeTypeId == other.RuntimeTypeId;

        public override bool Equals(object? obj) => obj is ValueNumberType other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine((int)StackKind, RuntimeTypeId);

        public override string ToString()
            => RuntimeType is null ? StackKind.ToString() : StackKind.ToString() + ":" + RuntimeType.Name;
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
        String,
        TypeHandle,
        FieldHandle,
        MethodHandle,
        SsaSlot,
        PhysicalSelector,
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
        LocalFieldSelect,
        LocalFieldStore,
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
        public static ValueNumberConstantKey String(string? value) => new ValueNumberConstantKey(ValueNumberConstantKind.String, GenStackKind.Ref, null, 0, 0, value ?? string.Empty);
        public static ValueNumberConstantKey TypeHandle(RuntimeType? type) => new ValueNumberConstantKey(ValueNumberConstantKind.TypeHandle, GenStackKind.NativeInt, type, type?.TypeId ?? 0, 0, null);
        public static ValueNumberConstantKey Field(RuntimeField field) => new ValueNumberConstantKey(ValueNumberConstantKind.FieldHandle, GenStackKind.NativeInt, field.DeclaringType, field.FieldId, field.Offset, field);
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
                ValueNumberConstantKind.String => "str(" + Object + ")",
                ValueNumberConstantKind.TypeHandle => "type(" + (RuntimeType?.Name ?? A.ToString()) + ")",
                ValueNumberConstantKind.FieldHandle => "field(" + (Object is RuntimeField f ? f.Name : A.ToString()) + ")",
                ValueNumberConstantKind.MethodHandle => "method(" + (Object is RuntimeMethod m ? m.Name : A.ToString()) + ")",
                ValueNumberConstantKind.SsaSlot => "slot(" + ((SsaSlotKind)A).ToString() + ":" + B.ToString() + ")",
                ValueNumberConstantKind.PhysicalSelector => "phys(" + A.ToString() + ":" + B.ToString() + ")",
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
        private int _nextId = 1;
        private int _mapSelectBudget = DefaultMapSelectBudget;

        public ValueNumber InitialHeap(RuntimeMethod method)
            => VNForConstant(ValueNumberConstantKey.Heap(method));

        public ValueNumber InitialStack(RuntimeMethod method)
            => VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.InitVal, VNForMethod(method), VNForInt32(-2));

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
        public ValueNumber VNForType(RuntimeType? type) => VNForConstant(ValueNumberConstantKey.TypeHandle(type));
        public ValueNumber VNForField(RuntimeField field) => VNForConstant(ValueNumberConstantKey.Field(field));
        public ValueNumber VNForMethod(RuntimeMethod method) => VNForConstant(ValueNumberConstantKey.Method(method));
        public ValueNumber VNForSlot(SsaSlot slot) => VNForConstant(ValueNumberConstantKey.Slot(slot));
        public ValueNumber VNForBlock(int blockId) => VNForConstant(ValueNumberConstantKey.Block(blockId));
        public ValueNumber VNForPhysicalSelector(int offset, int size) => VNForConstant(ValueNumberConstantKey.PhysicalSelector(offset, size));

        public ValueNumber VNForUnique(GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            var vn = Allocate(new ValueNumberEntry(ValueNumberKind.Unique, stackKind, type, default, function, args, stableId: 0));
            return vn;
        }

        public ValueNumber VNForStableUnique(int stableId, GenStackKind stackKind, RuntimeType? type, ValueNumberFunction function, ImmutableArray<ValueNumber> args)
        {
            if (args.IsDefault)
                args = ImmutableArray<ValueNumber>.Empty;

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

        private ValueNumber VNForMapSelect(GenStackKind stackKind, RuntimeType? type, ValueNumber map, ValueNumber selector)
        {
            if (!map.IsValid || !selector.IsValid)
                return VNForUnique(stackKind, type, ValueNumberFunction.MapSelect, ImmutableArray.Create(map, selector));

            _mapSelectBudget = DefaultMapSelectBudget;
            var result = VNForMapSelectWork(stackKind, type, map, selector);
            _mapSelectBudget = DefaultMapSelectBudget;
            return result;
        }

        private ValueNumber VNForMapSelectWork(GenStackKind stackKind, RuntimeType? type, ValueNumber map, ValueNumber selector)
        {
            if (--_mapSelectBudget <= 0)
                return VNForUnique(stackKind, type, ValueNumberFunction.MapSelect, ImmutableArray.Create(map, selector));

            if (TryGetEntry(map, out var entry))
            {
                if (entry.Function == ValueNumberFunction.MapStore && entry.Args.Length == 4)
                {
                    var previousMap = entry.Args[0];
                    var storedSelector = entry.Args[1];
                    var storedValue = entry.Args[2];

                    if (storedSelector == selector)
                        return NormalizeLoad(storedValue, stackKind, type);

                    if (SelectorsKnownDistinct(storedSelector, selector))
                        return VNForMapSelectWork(stackKind, type, previousMap, selector);
                }

                if (entry.Function == ValueNumberFunction.MapPhysicalStore && entry.Args.Length == 3)
                {
                    var previousMap = entry.Args[0];
                    var storedSelector = entry.Args[1];
                    var storedValue = entry.Args[2];

                    if (PhysicalSelectorsEqual(storedSelector, selector))
                        return NormalizeLoad(storedValue, stackKind, type);

                    if (PhysicalSelectorsDoNotOverlap(storedSelector, selector))
                        return VNForMapSelectWork(stackKind, type, previousMap, selector);
                }

                if (entry.Function == ValueNumberFunction.BitCast && entry.Args.Length >= 1)
                    return VNForMapSelectWork(stackKind, type, entry.Args[0], selector);

                if ((entry.Kind == ValueNumberKind.Phi || entry.Kind == ValueNumberKind.MemoryPhi) && entry.Args.Length != 0)
                {
                    var selected = ImmutableArray.CreateBuilder<ValueNumber>(entry.Args.Length);
                    for (int i = 0; i < entry.Args.Length; i++)
                    {
                        if (!entry.Args[i].IsValid)
                            return CreateMapSelect(stackKind, type, map, selector);
                        selected.Add(VNForMapSelectWork(stackKind, type, entry.Args[i], selector));
                    }

                    var reduced = TryReducePhi(selected.ToImmutable());
                    if (reduced.IsValid)
                        return reduced;
                }
            }

            return CreateMapSelect(stackKind, type, map, selector);
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
            return VNForFunc(stackKind, type, ValueNumberFunction.BitCast, value, VNForType(type));
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

            if (function == ValueNumberFunction.LocalFieldSelect && args.Length == 3)
            {
                var baseEntry = GetEntryOrNull(args[0]);
                if (baseEntry is not null &&
                    baseEntry.Function == ValueNumberFunction.LocalFieldStore &&
                    baseEntry.Args.Length == 5)
                {
                    var previousLocal = baseEntry.Args[0];
                    var storedSelector = baseEntry.Args[1];
                    var storedValue = baseEntry.Args[2];
                    var storedField = baseEntry.Args[3];
                    var selector = args[1];
                    var field = args[2];

                    if (storedField == field && PhysicalSelectorsEqual(storedSelector, selector))
                    {
                        folded = NormalizeLoad(storedValue, stackKind, type);
                        return true;
                    }

                    if (PhysicalSelectorsDoNotOverlap(storedSelector, selector))
                    {
                        folded = VNForFunc(stackKind, type, ValueNumberFunction.LocalFieldSelect, previousLocal, selector, field);
                        return true;
                    }
                }
            }

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
                ValueNumberConstantKind.TypeHandle => l.A != r.A,
                ValueNumberConstantKind.SsaSlot => l.A != r.A || l.B != r.B,
                ValueNumberConstantKind.PhysicalSelector => PhysicalSelectorsDoNotOverlap(left, right),
                ValueNumberConstantKind.Int32 => !l.Equals(r),
                ValueNumberConstantKind.Int64 => !l.Equals(r),
                ValueNumberConstantKind.String => !l.Equals(r),
                ValueNumberConstantKind.MethodHandle => !l.Equals(r),
                _ => false,
            };
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

    internal sealed class SsaValueNumberingResult
    {
        public ValueNumberStore Store { get; }
        public IReadOnlyDictionary<SsaValueName, ValueNumberPair> SsaValues { get; }
        public IReadOnlyDictionary<SsaMemoryValueName, ValueNumber> MemoryValues { get; }
        public IReadOnlyDictionary<GenTree, ValueNumberPair> TreeValues { get; }
        public IReadOnlyDictionary<int, ValueNumber> HeapIn { get; }
        public IReadOnlyDictionary<int, ValueNumber> HeapOut { get; }
        public IReadOnlyDictionary<int, ValueNumber> StackIn { get; }
        public IReadOnlyDictionary<int, ValueNumber> StackOut { get; }
        public IReadOnlyDictionary<int, ValueNumber> ByrefExposedIn { get; }
        public IReadOnlyDictionary<int, ValueNumber> ByrefExposedOut { get; }

        public SsaValueNumberingResult(
            ValueNumberStore store,
            IReadOnlyDictionary<SsaValueName, ValueNumberPair> ssaValues,
            IReadOnlyDictionary<GenTree, ValueNumberPair> treeValues,
            IReadOnlyDictionary<SsaMemoryValueName, ValueNumber> memoryValues,
            IReadOnlyDictionary<int, ValueNumber> heapIn,
            IReadOnlyDictionary<int, ValueNumber> heapOut,
            IReadOnlyDictionary<int, ValueNumber>? stackIn = null,
            IReadOnlyDictionary<int, ValueNumber>? stackOut = null,
            IReadOnlyDictionary<int, ValueNumber>? byrefExposedIn = null,
            IReadOnlyDictionary<int, ValueNumber>? byrefExposedOut = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            SsaValues = ssaValues ?? throw new ArgumentNullException(nameof(ssaValues));
            MemoryValues = memoryValues ?? throw new ArgumentNullException(nameof(memoryValues));
            TreeValues = treeValues ?? throw new ArgumentNullException(nameof(treeValues));
            HeapIn = heapIn ?? throw new ArgumentNullException(nameof(heapIn));
            HeapOut = heapOut ?? throw new ArgumentNullException(nameof(heapOut));
            StackIn = stackIn ?? new Dictionary<int, ValueNumber>();
            StackOut = stackOut ?? new Dictionary<int, ValueNumber>();
            ByrefExposedIn = byrefExposedIn ?? new Dictionary<int, ValueNumber>();
            ByrefExposedOut = byrefExposedOut ?? new Dictionary<int, ValueNumber>();
        }

        public bool TryGetSsaValue(SsaValueName name, out ValueNumberPair value) => SsaValues.TryGetValue(name, out value);
        public bool TryGetMemoryValue(SsaMemoryValueName name, out ValueNumber value) => MemoryValues.TryGetValue(name, out value);
        public bool TryGetTreeValue(GenTree tree, out ValueNumberPair value) => TreeValues.TryGetValue(tree, out value);
    }

    internal static class SsaValueNumbering
    {
        public static SsaMethod BuildMethod(SsaMethod method)
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
            private readonly Dictionary<int, ValueNumber> _stackIn = new();
            private readonly Dictionary<int, ValueNumber> _stackOut = new();
            private readonly Dictionary<int, ValueNumber> _byrefExposedIn = new();
            private readonly Dictionary<int, ValueNumber> _byrefExposedOut = new();
            private readonly Dictionary<SsaSlot, SsaSlotInfo> _slotInfos = new();
            private readonly Dictionary<SsaValueName, SsaValueDefinition> _definitions = new();
            private readonly Dictionary<SsaMemoryValueName, SsaMemoryDefinition> _memoryDefinitions = new();
            private readonly Dictionary<SsaValueName, SsaDescriptor> _ssaDescriptors = new();
            private readonly SsaBlock[] _blockById;
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
                        _heapIn[blockId] = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, _store.VNForBlock(blockId));
                    if (!_heapOut.ContainsKey(blockId))
                        _heapOut[blockId] = _heapIn[blockId];
                    if (!_stackIn.ContainsKey(blockId))
                        _stackIn[blockId] = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, _store.VNForBlock(blockId), _store.VNForInt32(-2));
                    if (!_stackOut.ContainsKey(blockId))
                        _stackOut[blockId] = _stackIn[blockId];
                    if (!_byrefExposedIn.ContainsKey(blockId))
                        _byrefExposedIn[blockId] = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, _store.VNForBlock(blockId), _store.VNForInt32(-3));
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
                    new Dictionary<int, ValueNumber>(_stackIn),
                    new Dictionary<int, ValueNumber>(_stackOut),
                    new Dictionary<int, ValueNumber>(_byrefExposedIn),
                    new Dictionary<int, ValueNumber>(_byrefExposedOut));
            }

            private ImmutableArray<int> BuildNaturalBlockOrder()
            {
                var builder = ImmutableArray.CreateBuilder<int>(_method.Blocks.Length);
                for (int i = 0; i < _method.Blocks.Length; i++)
                    builder.Add(_method.Blocks[i].Id);
                return builder.ToImmutable();
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
                ValueNumber stack = ComputeStackIn(block);
                ValueNumber byrefExposed = GetBlockMemoryIn(block, SsaMemoryKind.ByrefExposed, _store.InitialByrefExposed(_method.GenTreeMethod.RuntimeMethod));
                SetHeapIn(block.Id, heap);
                SetStackIn(block.Id, stack);
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
                    NumberTree(block.Statements[s], block.Id, s, ref heap, ref byrefExposed, ref stack);

                PublishBlockMemoryOut(block, ref heap, ref byrefExposed);

                SetHeapOut(block.Id, heap);
                SetStackOut(block.Id, stack);
                SetByrefExposedOut(block.Id, byrefExposed);
            }

            private ValueNumber ComputeHeapIn(SsaBlock block)
            {
                var preds = block.CfgBlock.Predecessors;
                if (preds.Length == 0)
                    return _store.InitialHeap(_method.GenTreeMethod.RuntimeMethod);

                if (preds.Length == 1 && _heapOut.TryGetValue(preds[0].FromBlockId, out var single))
                    return single;

                var inputs = ImmutableArray.CreateBuilder<ValueNumber>(preds.Length);
                for (int i = 0; i < preds.Length; i++)
                {
                    if (_heapOut.TryGetValue(preds[i].FromBlockId, out var predHeap))
                        inputs.Add(predHeap);
                    else
                        inputs.Add(_store.VNForMemoryPhi(block.Id, ImmutableArray<ValueNumber>.Empty));
                }

                return _store.VNForMemoryPhi(block.Id, inputs.ToImmutable());
            }

            private ValueNumber ComputeStackIn(SsaBlock block)
            {
                var preds = block.CfgBlock.Predecessors;
                if (preds.Length == 0)
                    return _store.InitialStack(_method.GenTreeMethod.RuntimeMethod);

                if (preds.Length == 1 && _stackOut.TryGetValue(preds[0].FromBlockId, out var single))
                    return single;

                var inputs = ImmutableArray.CreateBuilder<ValueNumber>(preds.Length);
                for (int i = 0; i < preds.Length; i++)
                {
                    if (_stackOut.TryGetValue(preds[i].FromBlockId, out var predStack))
                        inputs.Add(predStack);
                    else
                        inputs.Add(_store.VNForMemoryPhi(block.Id, 1, ImmutableArray<ValueNumber>.Empty));
                }

                return _store.VNForMemoryPhi(block.Id, 1, inputs.ToImmutable());
            }

            private ValueNumber ComputeByrefExposedIn(SsaBlock block)
            {
                var preds = block.CfgBlock.Predecessors;
                if (preds.Length == 0)
                    return _store.InitialByrefExposed(_method.GenTreeMethod.RuntimeMethod);

                if (preds.Length == 1 && _byrefExposedOut.TryGetValue(preds[0].FromBlockId, out var single))
                    return single;

                var inputs = ImmutableArray.CreateBuilder<ValueNumber>(preds.Length);
                for (int i = 0; i < preds.Length; i++)
                {
                    if (_byrefExposedOut.TryGetValue(preds[i].FromBlockId, out var predMemory))
                        inputs.Add(predMemory);
                    else
                        inputs.Add(_store.VNForMemoryPhi(block.Id, 2, ImmutableArray<ValueNumber>.Empty));
                }

                return _store.VNForMemoryPhi(block.Id, 2, inputs.ToImmutable());
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
                    liberalInputs.Add(inputPair.Liberal);
                    conservativeInputs.Add(inputPair.Conservative);
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
                    SsaMemoryKind.ByrefExposed => 2,
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
                                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(oldHeap, tree.Source, operands, blockId, statementIndex));
                            SetMemoryValue(name, heap);
                            break;

                        case SsaMemoryKind.ByrefExposed:
                            if (byrefExposed == oldByrefExposed)
                                byrefExposed = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(oldByrefExposed, tree.Source, operands, blockId, statementIndex));
                            SetMemoryValue(name, byrefExposed);
                            break;

                        default:
                            SetMemoryValue(name, _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(InitialMemoryValue(name.Kind), tree.Source, operands, blockId, statementIndex)));
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


            private ValueNumberPair NumberTree(SsaTree tree, int blockId, int statementIndex, ref ValueNumber heap, ref ValueNumber byrefExposed, ref ValueNumber stack)
            {
                if (tree.Value.HasValue)
                {
                    var value = GetSsaValue(tree.Value.Value);
                    Remember(tree.Source, value);
                    return value;
                }

                var operandPairs = ImmutableArray.CreateBuilder<ValueNumberPair>(tree.Operands.Length);
                for (int i = 0; i < tree.Operands.Length; i++)
                    operandPairs.Add(NumberTree(tree.Operands[i], blockId, statementIndex, ref heap, ref byrefExposed, ref stack));
                var operands = operandPairs.ToImmutable();

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
                    Remember(tree.Source, normalized);
                    return normalized;
                }

                if (tree.LocalFieldBaseValue.HasValue && tree.LocalField is not null)
                {
                    var baseValue = GetSsaValue(tree.LocalFieldBaseValue.Value);
                    var result = NumberLocalFieldLoad(tree.Source, baseValue, tree.LocalField);
                    PublishMemoryDefinitions(tree, operands, blockId, statementIndex, oldHeap, oldByrefExposed, ref heap, ref byrefExposed);
                    Remember(tree.Source, result);
                    return result;
                }

                if (tree.LocalFieldBaseValue.HasValue || tree.LocalField is not null)
                    throw new InvalidOperationException("Malformed SSA local-field load metadata at node " + tree.Source.Id.ToString() + ".");
                {
                    var result = NumberNonStoreTree(tree.Source, operands, blockId, statementIndex, ref heap, ref byrefExposed, ref stack);
                    PublishMemoryDefinitions(tree, operands, blockId, statementIndex, oldHeap, oldByrefExposed, ref heap, ref byrefExposed);
                    Remember(tree.Source, result);
                    return result;
                }
            }

            private ValueNumberPair NumberNonStoreTree(GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId, int statementIndex, ref ValueNumber heap, ref ValueNumber byrefExposed, ref ValueNumber stack)
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
                        return NumberUnpromotedLocalLoad(node, heap, byrefExposed, stack);
                    case GenTreeKind.Unary:
                        return Unary(node, operands);
                    case GenTreeKind.Binary:
                        return Binary(node, operands);
                    case GenTreeKind.Conv:
                        return Conv(node, operands);
                    case GenTreeKind.Field:
                        return LoadField(node, operands, heap, byrefExposed, stack);
                    case GenTreeKind.StaticField:
                        return LoadStaticField(node, heap);
                    case GenTreeKind.LoadIndirect:
                        return LoadIndirect(node, operands, heap, byrefExposed);
                    case GenTreeKind.StoreField:
                        return StoreField(node, operands, ref heap, ref byrefExposed, ref stack);
                    case GenTreeKind.StoreStaticField:
                        return StoreStaticField(node, operands, ref heap);
                    case GenTreeKind.StoreIndirect:
                        return StoreIndirect(node, operands, ref heap, ref byrefExposed, blockId, statementIndex);
                    case GenTreeKind.StoreLocal:
                    case GenTreeKind.StoreArg:
                    case GenTreeKind.StoreTemp:
                        return StoreUnpromotedLocal(node, operands, ref heap, ref byrefExposed, ref stack);
                    case GenTreeKind.ArrayElement:
                        return LoadArrayElement(node, operands, heap);
                    case GenTreeKind.StoreArrayElement:
                        return StoreArrayElement(node, operands, ref heap);
                    case GenTreeKind.ArrayElementAddr:
                        return Func(node, ValueNumberFunction.LdElemA, operands);
                    case GenTreeKind.ArrayDataRef:
                        return Func(node, ValueNumberFunction.ArrayDataRef, operands);
                    case GenTreeKind.NewArray:
                        return NewArray(node, operands, ref heap);
                    case GenTreeKind.NewObject:
                        return NewObject(node, operands, ref heap);
                    case GenTreeKind.Call:
                    case GenTreeKind.VirtualCall:
                        return Call(node, operands, ref heap, ref byrefExposed);
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
                        return LocalAddress(node);
                    case GenTreeKind.FieldAddr:
                        return Func(node, ValueNumberFunction.FieldAddr, operands, ExtraFieldArg(node));
                    case GenTreeKind.StaticFieldAddr:
                        return StaticFieldAddress(node);
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
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.LocalFieldSelect, baseValue.Liberal, selector, _store.VNForField(field));
                ValueNumber conservative = baseValue.BothEqual
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.LocalFieldSelect, baseValue.Conservative, selector, _store.VNForField(field));
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
                ValueNumber selector = _store.VNForPhysicalSelector(field.Offset, Math.Max(1, StorageSize(field.FieldType, StackKindOf(field.FieldType))));
                ValueNumber fieldToken = _store.VNForField(field);
                ValueNumber liberal = _store.VNForFunc(
                    localStackKind,
                    localType,
                    ValueNumberFunction.LocalFieldStore,
                    oldLocal.Liberal,
                    selector,
                    fieldValue.Liberal,
                    fieldToken,
                    _store.VNForInt32(node.Id));
                ValueNumber conservative = oldLocal.BothEqual && fieldValue.BothEqual
                    ? liberal
                    : _store.VNForFunc(
                        localStackKind,
                        localType,
                        ValueNumberFunction.LocalFieldStore,
                        oldLocal.Conservative,
                        selector,
                        fieldValue.Conservative,
                        fieldToken,
                        _store.VNForInt32(node.Id));
                return NormalizeStore(new ValueNumberPair(liberal, conservative), localStackKind, localType);
            }

            private ValueNumberPair NumberUnpromotedLocalLoad(GenTree node, ValueNumber heap, ValueNumber byrefExposed, ValueNumber stack)
            {
                if (!SsaSlotHelpers.TryGetDirectLoadSlot(node, out var slot))
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, ImmutableArray<ValueNumber>.Empty));

                var info = GetSlotInfo(slot);
                ValueNumber memory = (info.AddressExposed || info.MemoryAliased) ? byrefExposed : stack;
                ValueNumber localMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, memory, _store.VNForSlot(slot));
                ValueNumber selector = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(info.Type, info.StackKind)));
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, localMap, selector);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreUnpromotedLocal(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, ref ValueNumber stack)
            {
                if (!SsaSlotHelpers.TryGetStoreSlot(node, out var slot) || operands.Length == 0)
                    return OpaqueStore(node, operands, ref heap);

                var info = GetSlotInfo(slot);
                ValueNumberPair value = operands[operands.Length - 1];
                ValueNumber slotSelector = _store.VNForSlot(slot);
                ValueNumber memory = (info.AddressExposed || info.MemoryAliased) ? byrefExposed : stack;
                ValueNumber localMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, memory, slotSelector);
                ValueNumber physical = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(info.Type, info.StackKind)));
                ValueNumber newLocalMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, localMap, physical, value.Conservative);
                ValueNumber updated = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, memory, slotSelector, newLocalMap, _store.VNForInt32(node.Id));
                if (info.AddressExposed || info.MemoryAliased)
                    byrefExposed = updated;
                else
                    stack = updated;
                return NormalizeStore(value, info.StackKind, info.Type);
            }

            private ValueNumberPair Unary(GenTree node, ImmutableArray<ValueNumberPair> operands)
            {
                var func = node.SourceOp switch
                {
                    BytecodeOp.Neg => ValueNumberFunction.Neg,
                    BytecodeOp.Not => ValueNumberFunction.Not,
                    _ => ValueNumberFunction.None,
                };
                if (func == ValueNumberFunction.None)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, ArgsFromPairs(operands, ValueNumberCategory.Liberal)));

                ValueNumber liberalArg = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Liberal;
                ValueNumber conservativeArg = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Conservative;
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, func, liberalArg);
                ValueNumber conservative = liberalArg == conservativeArg
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, func, conservativeArg);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair Binary(GenTree node, ImmutableArray<ValueNumberPair> operands)
            {
                var func = BinaryFunction(node.SourceOp);
                if (func == ValueNumberFunction.None)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind,
                        node.Type, ValueNumberFunction.MemOpaque, ArgsFromPairs(operands, ValueNumberCategory.Liberal)));

                if (IsCheckedOverflowBinaryOp(node.SourceOp))
                {
                    var args = ArgsFromPairs(operands, ValueNumberCategory.Liberal)
                        .Add(_store.VNForInt32((int)node.SourceOp));
                    ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, func, args);
                    return WithException(node, liberal);
                }
                {
                    ValueNumber leftLiberal = operands.Length > 0 ? operands[0].Liberal : ValueNumberStore.NoVN;
                    ValueNumber rightLiberal = operands.Length > 1 ? operands[1].Liberal : ValueNumberStore.NoVN;
                    ValueNumber leftConservative = operands.Length > 0 ? operands[0].Conservative : ValueNumberStore.NoVN;
                    ValueNumber rightConservative = operands.Length > 1 ? operands[1].Conservative : ValueNumberStore.NoVN;

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
                ValueNumber argLiberal = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Liberal;
                ValueNumber argConservative = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Conservative;
                ValueNumber conv = _store.VNForInt32(((int)node.ConvKind << 8) | ((int)node.ConvFlags & 0xff));
                var func = (node.ConvFlags & NumericConvFlags.Checked) != 0 ? ValueNumberFunction.CastOvf : ValueNumberFunction.Conv;
                ValueNumber liberal = _store.VNForFunc(node.StackKind, node.Type, func, argLiberal, conv);
                ValueNumber conservative = argLiberal == argConservative
                    ? liberal
                    : _store.VNForFunc(node.StackKind, node.Type, func, argConservative, conv);
                return node.CanThrow ? WithException(node, liberal, conservative) : new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair LoadField(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap, ValueNumber byrefExposed, ValueNumber stack)
            {
                if (node.Field is null || operands.Length == 0)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ArgsFromPairs(operands).Add(heap)));

                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localAccess) && localAccess.Kind == SsaLocalAccessKind.Use)
                {
                    if (localAccess.IsPromotedFieldAccess)
                    {
                        var promotedInfo = GetSlotInfo(localAccess.Slot);
                        ValueNumber promotedMemory = (promotedInfo.AddressExposed || promotedInfo.MemoryAliased) ? byrefExposed : stack;
                        ValueNumber promotedSelector = _store.VNForSlot(localAccess.Slot);
                        ValueNumber promotedValue = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, promotedMemory, promotedSelector);
                        return ValueNumberPair.Same(promotedValue);
                    }

                    var info = GetSlotInfo(localAccess.BaseSlot);
                    ValueNumber memory = (info.AddressExposed || info.MemoryAliased) ? byrefExposed : stack;
                    ValueNumber slotSelector = _store.VNForSlot(localAccess.BaseSlot);
                    ValueNumber localMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, memory, slotSelector);
                    ValueNumber physical = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                    ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, localMap, physical);
                    return ValueNumberPair.Same(value);
                }
                {
                    ValueNumber obj = operands[0].Liberal;
                    ValueNumber fieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, _store.VNForField(node.Field));
                    ValueNumber objMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, fieldMap, obj);
                    ValueNumber selector = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                    ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, objMap, selector);
                    return ValueNumberPair.Same(value);
                }
            }

            private ValueNumberPair LoadStaticField(GenTree node, ValueNumber heap)
            {
                if (node.Field is null)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ImmutableArray.Create(heap)));

                ValueNumber fieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, _store.VNForField(node.Field));
                ValueNumber selector = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, fieldMap, selector);
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair LoadIndirect(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap, ValueNumber byrefExposed)
            {
                ValueNumber addr = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Liberal;
                ValueNumber memory = IsLocalAddress(addr) ? byrefExposed : heap;
                ValueNumber type = _store.VNForType(node.Type ?? node.RuntimeType);
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.ByrefExposedLoad, type, addr, memory);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreField(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, ref ValueNumber stack)
            {
                if (node.Field is null || operands.Length < 2)
                    return OpaqueStore(node, operands, ref heap);

                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var localAccess) && localAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    var info = GetSlotInfo(localAccess.BaseSlot);
                    ValueNumber value = operands[operands.Length - 1].Liberal;
                    ValueNumber slotSelector = _store.VNForSlot(localAccess.BaseSlot);
                    ValueNumber memory = (info.AddressExposed || info.MemoryAliased) ? byrefExposed : stack;
                    ValueNumber localMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, memory, slotSelector);
                    ValueNumber physical = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                    ValueNumber newLocalMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, localMap, physical, value);
                    ValueNumber updated = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, memory, slotSelector, newLocalMap, _store.VNForInt32(node.Id));
                    if (info.AddressExposed || info.MemoryAliased)
                        byrefExposed = updated;
                    else
                        stack = updated;
                    return ValueNumberPair.Same(value);
                }

                ValueNumber obj = operands[0].Liberal;
                ValueNumber heapValue = operands[1].Liberal;
                ValueNumber fieldSelector = _store.VNForField(node.Field);
                ValueNumber fieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, fieldSelector);
                ValueNumber objMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, fieldMap, obj);
                ValueNumber heapPhysical = _store.VNForPhysicalSelector(node.Field.Offset, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                ValueNumber newObjMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, objMap, heapPhysical, heapValue);
                ValueNumber newFieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, fieldMap, obj, newObjMap, _store.VNForInt32(node.Id));
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, heap, fieldSelector, newFieldMap, _store.VNForInt32(node.Id));
                return ValueNumberPair.Same(heapValue);
            }

            private ValueNumberPair StoreStaticField(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap)
            {
                if (node.Field is null || operands.Length == 0)
                    return OpaqueStore(node, operands, ref heap);

                ValueNumber value = operands[operands.Length - 1].Liberal;
                ValueNumber fieldSelector = _store.VNForField(node.Field);
                ValueNumber fieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, fieldSelector);
                ValueNumber physical = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(node.Field.FieldType, StackKindOf(node.Field.FieldType))));
                ValueNumber newFieldMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, fieldMap, physical, value);
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, heap, fieldSelector, newFieldMap, _store.VNForInt32(node.Id));
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreIndirect(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed, int blockId, int statementIndex)
            {
                ValueNumber result = operands.Length == 0 ? ValueNumberStore.NoVN : operands[operands.Length - 1].Liberal;
                ValueNumber addr = operands.Length == 0 ? ValueNumberStore.NoVN : operands[0].Liberal;
                if (IsLocalAddress(addr))
                    byrefExposed = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(byrefExposed, node, operands, blockId, statementIndex));
                else
                    heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(heap, node, operands, blockId, statementIndex));
                return ValueNumberPair.Same(result);
            }

            private ValueNumberPair LoadArrayElement(GenTree node, ImmutableArray<ValueNumberPair> operands, ValueNumber heap)
            {
                if (operands.Length < 2)
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MapSelect, ArgsFromPairs(operands).Add(heap)));

                ValueNumber typeSelector = _store.VNForType(node.RuntimeType ?? node.Type);
                ValueNumber arrayMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, typeSelector);
                ValueNumber objectMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, arrayMap, operands[0].Liberal);
                ValueNumber indexMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, objectMap, operands[1].Liberal);
                ValueNumber selector = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(node.Type ?? node.RuntimeType, node.StackKind)));
                ValueNumber value = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.MapSelect, indexMap, selector);
                return node.CanThrow ? WithException(node, value) : ValueNumberPair.Same(value);
            }

            private ValueNumberPair StoreArrayElement(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap)
            {
                if (operands.Length < 3)
                    return OpaqueStore(node, operands, ref heap);

                ValueNumber typeSelector = _store.VNForType(node.RuntimeType ?? node.Type);
                ValueNumber array = operands[0].Liberal;
                ValueNumber index = operands[1].Liberal;
                ValueNumber value = operands[2].Liberal;
                ValueNumber arrayMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, heap, typeSelector);
                ValueNumber objectMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, arrayMap, array);
                ValueNumber indexMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapSelect, objectMap, index);
                ValueNumber selector = _store.VNForPhysicalSelector(0, Math.Max(1, StorageSize(node.Type ?? node.RuntimeType, node.StackKind)));
                ValueNumber newIndexMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapPhysicalStore, indexMap, selector, value);
                ValueNumber newObjectMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, objectMap, index, newIndexMap, _store.VNForInt32(node.Id));
                ValueNumber newArrayMap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, arrayMap, array, newObjectMap, _store.VNForInt32(node.Id));
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MapStore, heap, typeSelector, newArrayMap, _store.VNForInt32(node.Id));
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair NewArray(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap)
            {
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.NewArray, ArgsFromPairs(operands).Add(_store.VNForType(node.RuntimeType ?? node.Type)));
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, heap, liberal);
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair NewObject(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap)
            {
                var newObjArgs = ArgsFromPairs(operands);
                if (node.Method is not null)
                    newObjArgs = newObjArgs.Add(_store.VNForMethod(node.Method));
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.NewObject, newObjArgs);
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, heap, liberal);
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair Call(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap, ref ValueNumber byrefExposed)
            {
                var args = ArgsFromPairs(operands);
                if (node.Method is not null)
                    args = args.Add(_store.VNForMethod(node.Method));
                args = args.Add(heap).Add(byrefExposed);
                ValueNumberFunction func = node.Kind == GenTreeKind.VirtualCall ? ValueNumberFunction.VirtualCall : ValueNumberFunction.Call;
                ValueNumber liberal = _store.VNForStableUnique(node.Id, node.StackKind, node.Type, func, args);
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, heap, liberal);
                byrefExposed = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, byrefExposed, liberal, _store.VNForInt32(node.Id));
                return node.CanThrow ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair LocalAddress(GenTree node)
            {
                if (!SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot))
                    return ValueNumberPair.Same(_store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, ImmutableArray<ValueNumber>.Empty));

                ValueNumber vn = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.PtrToLoc, _store.VNForSlot(slot), _store.VNForInt32(0));
                return ValueNumberPair.Same(vn);
            }

            private ValueNumberPair StaticFieldAddress(GenTree node)
            {
                ValueNumber field = node.Field is not null ? _store.VNForField(node.Field) : _store.VNForInt32(node.Int32);
                ValueNumber vn = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.PtrToStatic, field, _store.VNForInt32(0), _store.VNForInt32(0));
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
                    ? _store.VNForStableUnique(node.Id, node.StackKind, node.Type, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(heap, node, operands, blockId, statementIndex))
                    : ValueNumberStore.NoVN;

                if (node.WritesMemory || node.ContainsCall || node.HasSideEffect)
                {
                    heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(heap, node, operands, blockId, statementIndex));
                    byrefExposed = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, BuildOpaqueArgs(byrefExposed, node, operands, blockId, statementIndex));
                }

                return node.CanThrow && liberal.IsValid ? WithException(node, liberal) : ValueNumberPair.Same(liberal);
            }

            private ValueNumberPair OpaqueStore(GenTree node, ImmutableArray<ValueNumberPair> operands, ref ValueNumber heap)
            {
                ValueNumber value = operands.Length == 0 ? ValueNumberStore.NoVN : operands[operands.Length - 1].Liberal;
                heap = _store.VNForFunc(GenStackKind.Unknown, null, ValueNumberFunction.MemOpaque, ArgsFromPairs(operands).Add(heap).Add(_store.VNForInt32(node.Id)));
                return ValueNumberPair.Same(value);
            }

            private ValueNumberPair WithException(GenTree node, ValueNumber liberal)
                => WithException(node, liberal, liberal);

            private ValueNumberPair WithException(GenTree node, ValueNumber liberal, ValueNumber conservativeValue)
            {
                ValueNumber exception = _store.VNForStableUnique(node.Id, GenStackKind.Unknown, null, ValueNumberFunction.ExcSetCons, ImmutableArray.Create(_store.VNForInt32(node.Id)));
                ValueNumber conservative = _store.VNForFunc(node.StackKind, node.Type, ValueNumberFunction.ValWithExc, conservativeValue, exception);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumberPair NormalizeStore(ValueNumberPair value, GenStackKind stackKind, RuntimeType? type)
            {
                ValueNumber liberal = Normalize(value.Liberal, stackKind, type);
                ValueNumber conservative = value.BothEqual ? liberal : Normalize(value.Conservative, stackKind, type);
                return new ValueNumberPair(liberal, conservative);
            }

            private ValueNumber Normalize(ValueNumber value, GenStackKind stackKind, RuntimeType? type)
            {
                if (!_store.TryGetEntry(value, out var entry))
                    return value;
                if (entry.StackKind == stackKind && SameRuntimeType(entry.Type, type))
                    return value;
                return _store.VNForFunc(stackKind, type, ValueNumberFunction.BitCast, value, _store.VNForType(type));
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
                if (_ssaValues.TryGetValue(name, out var existing) && existing.Equals(value))
                    return;

                _ssaValues[name] = value;
                if (_ssaDescriptors.TryGetValue(name, out var descriptor))
                    descriptor.SetValueNumbers(value);
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

            private void SetStackIn(int blockId, ValueNumber value)
            {
                if (_stackIn.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _stackIn[blockId] = value;
                _changedThisPass = true;
            }

            private void SetStackOut(int blockId, ValueNumber value)
            {
                if (_stackOut.TryGetValue(blockId, out var existing) && existing == value)
                    return;

                _stackOut[blockId] = value;
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

            private ImmutableArray<ValueNumber> BuildOpaqueArgs(ValueNumber heap, GenTree node, ImmutableArray<ValueNumberPair> operands, int blockId, int statementIndex)
                => ArgsFromPairs(operands).Add(heap).Add(_store.VNForInt32(node.Id)).Add(_store.VNForBlock(blockId)).Add(_store.VNForInt32(statementIndex));

            private ImmutableArray<ValueNumber> ArgsFromPairs(ImmutableArray<ValueNumberPair> operands)
                => ArgsFromPairs(operands, ValueNumberCategory.Liberal);

            private ImmutableArray<ValueNumber> ArgsFromPairs(ImmutableArray<ValueNumberPair> operands, ValueNumberCategory category)
            {
                if (operands.IsDefaultOrEmpty)
                    return ImmutableArray<ValueNumber>.Empty;
                var builder = ImmutableArray.CreateBuilder<ValueNumber>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    builder.Add(operands[i][category]);
                return builder.ToImmutable();
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
                if (type is null) return GenStackKind.Unknown;
                if (type.IsReferenceType) return GenStackKind.Ref;
                if (type.Kind == RuntimeTypeKind.ByRef) return GenStackKind.ByRef;
                if (type.Kind == RuntimeTypeKind.Pointer) return GenStackKind.Ptr;
                if (type.Name == "Single") return GenStackKind.R4;
                if (type.Name == "Double") return GenStackKind.R8;
                if (type.SizeOf <= 4) return GenStackKind.I4;
                if (type.SizeOf <= 8) return GenStackKind.I8;
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

            private static bool SameRuntimeType(RuntimeType? left, RuntimeType? right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null) return false;
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
