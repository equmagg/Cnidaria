using System;

namespace Cnidaria.Cs
{
    internal enum RuntimeIntrinsicId : ushort
    {
        None,
        InterlockedCompareExchange,
        InterlockedExchangeAdd,
    }

    [Flags]
    internal enum RuntimeIntrinsicFlags : ushort
    {
        None = 0,
        SpecialImport = 1 << 0,
        NoInline = 1 << 1,
        NoGcSafePoint = 1 << 2,
        AtomicMemory = 1 << 3,
        UsesCallAbi = 1 << 4,
        SideEffect = 1 << 5,
        CanThrow = 1 << 6,
        MemoryRead = 1 << 7,
        MemoryWrite = 1 << 8,
        GlobalRef = 1 << 9,
        Indirect = 1 << 10,
        Ordered = 1 << 11,
    }

    internal readonly struct InterlockedCompareExchangeIntrinsic
    {
        public RuntimeType ValueType { get; }
        public int Size { get; }
        public bool IsReference { get; }
        public bool IsSigned { get; }

        public InterlockedCompareExchangeIntrinsic(RuntimeType valueType, int size, bool isReference, bool isSigned)
        {
            ValueType = valueType;
            Size = size;
            IsReference = isReference;
            IsSigned = isSigned;
        }
    }

    internal readonly struct InterlockedExchangeAddIntrinsic
    {
        public RuntimeType ValueType { get; }
        public int Size { get; }

        public InterlockedExchangeAddIntrinsic(RuntimeType valueType, int size)
        {
            ValueType = valueType;
            Size = size;
        }
    }

    internal readonly struct RuntimeIntrinsicInfo
    {
        public RuntimeIntrinsicId Id { get; }
        public RuntimeIntrinsicFlags Flags { get; }
        public InterlockedCompareExchangeIntrinsic CompareExchange { get; }
        public InterlockedExchangeAddIntrinsic ExchangeAdd { get; }

        public bool IsSpecialImport => (Flags & RuntimeIntrinsicFlags.SpecialImport) != 0;
        public bool IsNoInline => (Flags & RuntimeIntrinsicFlags.NoInline) != 0;
        public bool IsNoGcSafePoint => (Flags & RuntimeIntrinsicFlags.NoGcSafePoint) != 0;
        public bool IsAtomicMemory => (Flags & RuntimeIntrinsicFlags.AtomicMemory) != 0;

        public RuntimeIntrinsicInfo(
            RuntimeIntrinsicId id,
            RuntimeIntrinsicFlags flags,
            InterlockedCompareExchangeIntrinsic compareExchange = default,
            InterlockedExchangeAddIntrinsic exchangeAdd = default)
        {
            Id = id;
            Flags = flags;
            CompareExchange = compareExchange;
            ExchangeAdd = exchangeAdd;
        }
    }

    internal static class RuntimeIntrinsics
    {
        private const RuntimeIntrinsicFlags AtomicReadModifyWriteFlags =
            RuntimeIntrinsicFlags.SpecialImport |
            RuntimeIntrinsicFlags.NoInline |
            RuntimeIntrinsicFlags.NoGcSafePoint |
            RuntimeIntrinsicFlags.AtomicMemory |
            RuntimeIntrinsicFlags.UsesCallAbi |
            RuntimeIntrinsicFlags.SideEffect |
            RuntimeIntrinsicFlags.CanThrow |
            RuntimeIntrinsicFlags.MemoryRead |
            RuntimeIntrinsicFlags.MemoryWrite |
            RuntimeIntrinsicFlags.GlobalRef |
            RuntimeIntrinsicFlags.Indirect |
            RuntimeIntrinsicFlags.Ordered;

        public static RuntimeIntrinsicId GetIntrinsicId(RuntimeMethod? method)
        {
            if (method is null)
                return RuntimeIntrinsicId.None;

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 3 &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Threading") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Interlocked") &&
                StringComparer.Ordinal.Equals(method.Name, "CompareExchange"))
            {
                return RuntimeIntrinsicId.InterlockedCompareExchange;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 2 &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Namespace, "System.Threading") &&
                StringComparer.Ordinal.Equals(method.DeclaringType.Name, "Interlocked") &&
                StringComparer.Ordinal.Equals(method.Name, "ExchangeAdd"))
            {
                return RuntimeIntrinsicId.InterlockedExchangeAdd;
            }

            return RuntimeIntrinsicId.None;
        }

        public static RuntimeIntrinsicFlags GetFlags(RuntimeIntrinsicId id)
            => id switch
            {
                RuntimeIntrinsicId.InterlockedCompareExchange => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.InterlockedExchangeAdd => AtomicReadModifyWriteFlags,
                _ => RuntimeIntrinsicFlags.None,
            };

        public static bool IsNoGcSafePoint(RuntimeIntrinsicId id)
            => (GetFlags(id) & RuntimeIntrinsicFlags.NoGcSafePoint) != 0;

        public static bool TryResolve(RuntimeMethod method, TargetInfo target, out RuntimeIntrinsicInfo intrinsic)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));

            RuntimeIntrinsicId id = GetIntrinsicId(method);
            if (!Supports(id, target))
            {
                intrinsic = default;
                return false;
            }

            switch (id)
            {
                case RuntimeIntrinsicId.InterlockedCompareExchange:
                    if (TryGetInterlockedCompareExchange(method, target.PointerSize, out var compareExchange))
                    {
                        intrinsic = new RuntimeIntrinsicInfo(id, AtomicReadModifyWriteFlags, compareExchange: compareExchange);
                        return true;
                    }
                    break;
                case RuntimeIntrinsicId.InterlockedExchangeAdd:
                    if (TryGetInterlockedExchangeAdd(method, target, out var exchangeAdd))
                    {
                        intrinsic = new RuntimeIntrinsicInfo(id, AtomicReadModifyWriteFlags, exchangeAdd: exchangeAdd);
                        return true;
                    }
                    break;
            }

            intrinsic = default;
            return false;
        }

        public static bool Supports(RuntimeIntrinsicId id, TargetInfo target)
            => id switch
            {
                RuntimeIntrinsicId.InterlockedCompareExchange => target.Architecture is
                    Cnidaria.TargetArchitectureKind.RegisterBytecode or
                    Cnidaria.TargetArchitectureKind.RegisterBytecode64 or
                    Cnidaria.TargetArchitectureKind.I386 or
                    Cnidaria.TargetArchitectureKind.X86_64 or
                    Cnidaria.TargetArchitectureKind.RiscV32 or
                    Cnidaria.TargetArchitectureKind.RiscV64,
                RuntimeIntrinsicId.InterlockedExchangeAdd => target.Architecture is
                    Cnidaria.TargetArchitectureKind.RegisterBytecode or
                    Cnidaria.TargetArchitectureKind.RegisterBytecode64 or
                    Cnidaria.TargetArchitectureKind.I386 or
                    Cnidaria.TargetArchitectureKind.X86_64 or
                    Cnidaria.TargetArchitectureKind.RiscV32 or
                    Cnidaria.TargetArchitectureKind.RiscV64,
                _ => false,
            };

        private static bool TryGetInterlockedCompareExchange(
            RuntimeMethod method,
            int pointerSize,
            out InterlockedCompareExchangeIntrinsic intrinsic)
        {
            intrinsic = default;

            if (GetIntrinsicId(method) != RuntimeIntrinsicId.InterlockedCompareExchange)
                return false;

            RuntimeType locationType = method.ParameterTypes[0];
            if (locationType.Kind != RuntimeTypeKind.ByRef || locationType.ElementType is null)
                return false;

            RuntimeType signatureValueType = locationType.ElementType;
            if (!SameType(signatureValueType, method.ParameterTypes[1]) ||
                !SameType(signatureValueType, method.ParameterTypes[2]) ||
                !SameType(signatureValueType, method.ReturnType))
            {
                return false;
            }

            RuntimeType valueType = signatureValueType;
            if (valueType.Kind == RuntimeTypeKind.TypeParam)
            {
                if (!valueType.IsMethodGenericParameter ||
                    (uint)valueType.GenericParameterOrdinal >= (uint)method.MethodGenericArguments.Length)
                {
                    return false;
                }
                valueType = method.MethodGenericArguments[valueType.GenericParameterOrdinal];
            }

            if (valueType.IsReferenceType)
            {
                intrinsic = new InterlockedCompareExchangeIntrinsic(valueType, pointerSize, isReference: true, isSigned: false);
                return true;
            }

            RuntimeType scalarType = valueType;
            if (scalarType.Kind == RuntimeTypeKind.Enum && scalarType.ElementType is not null)
                scalarType = scalarType.ElementType;

            RuntimePrimitiveKind primitive = scalarType.PrimitiveKind;
            int size;
            bool signed;
            switch (primitive)
            {
                case RuntimePrimitiveKind.Int8:
                    size = 1;
                    signed = true;
                    break;
                case RuntimePrimitiveKind.UInt8:
                case RuntimePrimitiveKind.Boolean:
                    size = 1;
                    signed = false;
                    break;
                case RuntimePrimitiveKind.Int16:
                    size = 2;
                    signed = true;
                    break;
                case RuntimePrimitiveKind.UInt16:
                case RuntimePrimitiveKind.Char:
                    size = 2;
                    signed = false;
                    break;
                case RuntimePrimitiveKind.Int32:
                    size = 4;
                    signed = true;
                    break;
                case RuntimePrimitiveKind.UInt32:
                    size = 4;
                    signed = false;
                    break;
                case RuntimePrimitiveKind.Int64:
                    size = 8;
                    signed = true;
                    break;
                case RuntimePrimitiveKind.UInt64:
                    size = 8;
                    signed = false;
                    break;
                case RuntimePrimitiveKind.NativeInt:
                    size = pointerSize;
                    signed = true;
                    break;
                case RuntimePrimitiveKind.NativeUInt:
                    size = pointerSize;
                    signed = false;
                    break;
                default:
                    if (valueType.Kind != RuntimeTypeKind.Enum || valueType.SizeOf is not (1 or 2 or 4 or 8))
                        return false;
                    size = valueType.SizeOf;
                    signed = false;
                    break;
            }

            intrinsic = new InterlockedCompareExchangeIntrinsic(valueType, size, isReference: false, isSigned: signed);
            return true;
        }

        private static bool TryGetInterlockedExchangeAdd(
            RuntimeMethod method,
            TargetInfo target,
            out InterlockedExchangeAddIntrinsic intrinsic)
        {
            intrinsic = default;

            if (GetIntrinsicId(method) != RuntimeIntrinsicId.InterlockedExchangeAdd)
                return false;

            RuntimeType locationType = method.ParameterTypes[0];
            if (locationType.Kind != RuntimeTypeKind.ByRef || locationType.ElementType is null)
                return false;

            RuntimeType valueType = locationType.ElementType;
            if (!SameType(valueType, method.ParameterTypes[1]) || !SameType(valueType, method.ReturnType))
                return false;

            int size = valueType.PrimitiveKind switch
            {
                RuntimePrimitiveKind.Int32 => 4,
                RuntimePrimitiveKind.Int64 => 8,
                _ => 0,
            };
            if (size == 0)
                return false;

            if (size == 8 && target.PointerSize == 4 && target.Architecture is not
                (Cnidaria.TargetArchitectureKind.RegisterBytecode or Cnidaria.TargetArchitectureKind.RegisterBytecode64))
            {
                return false;
            }

            intrinsic = new InterlockedExchangeAddIntrinsic(valueType, size);
            return true;
        }

        private static bool SameType(RuntimeType left, RuntimeType right)
            => ReferenceEquals(left, right) || left.TypeId == right.TypeId;
    }
}
