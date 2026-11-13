using System;

namespace Cnidaria.Cs
{
    internal enum RuntimeIntrinsicId : ushort
    {
        None,
        InterlockedCompareExchange,
        InterlockedExchangeAdd,
        InterlockedExchange,
        InterlockedOr,
        InterlockedAnd,
        MemoryBarrier,
        VolatileRead,
        VolatileWrite,
        InteropSys,
        AsyncCallContinuation,
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

    internal readonly struct InterlockedExchangeIntrinsic
    {
        public RuntimeType ValueType { get; }
        public int Size { get; }
        public bool IsReference { get; }
        public bool IsSigned { get; }

        public InterlockedExchangeIntrinsic(RuntimeType valueType, int size, bool isReference, bool isSigned)
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

    internal readonly struct InterlockedBitwiseIntrinsic
    {
        public RuntimeType ValueType { get; }
        public int Size { get; }
        public bool IsSigned { get; }

        public InterlockedBitwiseIntrinsic(RuntimeType valueType, int size, bool isSigned)
        {
            ValueType = valueType;
            Size = size;
            IsSigned = isSigned;
        }
    }

    internal readonly struct RuntimeIntrinsicInfo
    {
        public RuntimeIntrinsicId Id { get; }
        public RuntimeIntrinsicFlags Flags { get; }
        public InterlockedCompareExchangeIntrinsic CompareExchange { get; }
        public InterlockedExchangeAddIntrinsic ExchangeAdd { get; }
        public InterlockedExchangeIntrinsic Exchange { get; }
        public InterlockedBitwiseIntrinsic Bitwise { get; }

        public bool IsSpecialImport => (Flags & RuntimeIntrinsicFlags.SpecialImport) != 0;
        public bool IsNoInline => (Flags & RuntimeIntrinsicFlags.NoInline) != 0;
        public bool IsNoGcSafePoint => (Flags & RuntimeIntrinsicFlags.NoGcSafePoint) != 0;
        public bool IsAtomicMemory => (Flags & RuntimeIntrinsicFlags.AtomicMemory) != 0;

        public RuntimeIntrinsicInfo(
            RuntimeIntrinsicId id,
            RuntimeIntrinsicFlags flags,
            InterlockedCompareExchangeIntrinsic compareExchange = default,
            InterlockedExchangeAddIntrinsic exchangeAdd = default,
            InterlockedExchangeIntrinsic exchange = default,
            InterlockedBitwiseIntrinsic bitwise = default)
        {
            Id = id;
            Flags = flags;
            CompareExchange = compareExchange;
            ExchangeAdd = exchangeAdd;
            Exchange = exchange;
            Bitwise = bitwise;
        }
    }

    // GC services the BCL reaches through RuntimeImports and InternalCalls, each exported under its own name
    internal static class RuntimeGcImports
    {
        public const string FinalizerDrainSymbol = "RhpFinalizerDrain";

        private static bool IsRuntimeImport(RuntimeMethod method)
            => method.HasInternalCall && method.IsStatic && method.DeclaringType.Namespace == "System.Runtime" &&
               method.DeclaringType.Name is "RuntimeImports" or "InternalCalls";

        public static string? Symbol(RuntimeMethod method)
            => IsRuntimeImport(method) && method.Name is
                "RhpHandleAlloc" or
                "RhpHandleAllocDependent" or
                "RhHandleFree" or
                "RhHandleGet" or
                "RhHandleSet" or
                "RhHandleGetDependent" or
                "RhHandleSetDependentSecondary" or
                "RhSuppressFinalize" or
                "RhReRegisterForFinalize" or
                "RhpGetNextFinalizableObject" or
                "RhCollect" or
                "RhpFallbackFailFast" or
                "RhpMonitorEnter" or
                "RhpMonitorTryEnter" or
                "RhpMonitorExit" or
                "RhpMonitorIsEntered" or
                "RhpMonitorWait" or
                "RhpMonitorPulse" or
                "RhYield" or
                "RhSpinWait" or
                "RhpClassInitEnter" or
                "RhpClassInitComplete"
                ? method.Name
                : null;

        public static bool IsGcSafePoint(RuntimeMethod method)
            => IsRuntimeImport(method) && method.Name is "RhCollect" or "RhpMonitorEnter" or "RhpMonitorTryEnter" or "RhpMonitorWait" or "RhpClassInitEnter";

        // The shims of this library are part of the runtime every native image links
        public static bool IsRuntimeLibrary(DllImportData import)
            => import.ModuleName == "libSystem.Native";

        public static bool IsFinalizerDrain(RuntimeMethod method)
            => method.IsStatic &&
               method.Name == "DrainQueueOnCurrentThread" &&
               method.DeclaringType.Namespace == "System.Runtime" &&
               method.DeclaringType.Name == "__Finalizer";
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

        // A barrier has no operands and no result, so it needs neither the call ABI nor a null check
        private const RuntimeIntrinsicFlags MemoryBarrierFlags =
            RuntimeIntrinsicFlags.SpecialImport |
            RuntimeIntrinsicFlags.NoInline |
            RuntimeIntrinsicFlags.NoGcSafePoint |
            RuntimeIntrinsicFlags.AtomicMemory |
            RuntimeIntrinsicFlags.SideEffect |
            RuntimeIntrinsicFlags.MemoryRead |
            RuntimeIntrinsicFlags.MemoryWrite |
            RuntimeIntrinsicFlags.GlobalRef |
            RuntimeIntrinsicFlags.Ordered;

        // Reads the continuation register the async call just before it returned in, no instruction may come between them
        private const RuntimeIntrinsicFlags AsyncCallContinuationFlags =
            RuntimeIntrinsicFlags.SpecialImport |
            RuntimeIntrinsicFlags.NoInline |
            RuntimeIntrinsicFlags.NoGcSafePoint |
            RuntimeIntrinsicFlags.SideEffect |
            RuntimeIntrinsicFlags.Ordered;

        private const RuntimeIntrinsicFlags SystemCallFlags =
            RuntimeIntrinsicFlags.SpecialImport |
            RuntimeIntrinsicFlags.NoInline |
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

            if (!method.HasThis && method.IsStatic && method.DeclaringType.AssemblyName == "std" &&
                method.DeclaringType.Namespace.Length == 0 && method.DeclaringType.Name == "Interop+Sys")
            {
                return RuntimeIntrinsicId.InteropSys;
            }

            if (method.IsStatic &&
                method.ParameterTypes.Length == 0 &&
                method.Name == "AsyncCallContinuation" &&
                method.DeclaringType.AssemblyName == "std" &&
                method.DeclaringType.Namespace == "System.Runtime.CompilerServices" &&
                method.DeclaringType.Name == "AsyncHelpers")
            {
                return RuntimeIntrinsicId.AsyncCallContinuation;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 3 &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                method.Name == "CompareExchange")
            {
                return RuntimeIntrinsicId.InterlockedCompareExchange;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 2 &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                method.Name == "ExchangeAdd")
            {
                return RuntimeIntrinsicId.InterlockedExchangeAdd;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 2 &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                method.Name == "Exchange")
            {
                return RuntimeIntrinsicId.InterlockedExchange;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 2 &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                method.Name == "Or")
            {
                return RuntimeIntrinsicId.InterlockedOr;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 2 &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                method.Name == "And")
            {
                return RuntimeIntrinsicId.InterlockedAnd;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length == 0 &&
                method.ReturnType.PrimitiveKind == RuntimePrimitiveKind.Void &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Interlocked" &&
                (method.Name == "MemoryBarrier" ||
                 method.Name == "ReadMemoryBarrier"))
            {
                return RuntimeIntrinsicId.MemoryBarrier;
            }

            if (!method.HasThis &&
                method.IsStatic &&
                method.ParameterTypes.Length is 1 or 2 &&
                method.ParameterTypes[0].Kind == RuntimeTypeKind.ByRef &&
                method.DeclaringType.Namespace == "System.Threading" &&
                method.DeclaringType.Name == "Volatile")
            {
                if (method.ParameterTypes.Length == 1 && method.Name == "Read")
                    return RuntimeIntrinsicId.VolatileRead;
                if (method.ParameterTypes.Length == 2 && method.Name == "Write")
                    return RuntimeIntrinsicId.VolatileWrite;
            }

            return RuntimeIntrinsicId.None;
        }

        public static RuntimeIntrinsicFlags GetFlags(RuntimeIntrinsicId id)
            => id switch
            {
                RuntimeIntrinsicId.InterlockedCompareExchange => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.InterlockedExchangeAdd => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.InterlockedExchange => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.InterlockedOr => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.InterlockedAnd => AtomicReadModifyWriteFlags,
                RuntimeIntrinsicId.MemoryBarrier or RuntimeIntrinsicId.VolatileRead or RuntimeIntrinsicId.VolatileWrite => MemoryBarrierFlags,
                RuntimeIntrinsicId.InteropSys => SystemCallFlags,
                RuntimeIntrinsicId.AsyncCallContinuation => AsyncCallContinuationFlags,
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
                case RuntimeIntrinsicId.InteropSys:
                    intrinsic = new RuntimeIntrinsicInfo(id, SystemCallFlags);
                    return true;
                case RuntimeIntrinsicId.AsyncCallContinuation:
                    intrinsic = new RuntimeIntrinsicInfo(id, AsyncCallContinuationFlags);
                    return true;
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
                case RuntimeIntrinsicId.InterlockedExchange:
                    if (TryGetInterlockedExchange(method, target.PointerSize, out var exchange))
                    {
                        intrinsic = new RuntimeIntrinsicInfo(id, AtomicReadModifyWriteFlags, exchange: exchange);
                        return true;
                    }
                    break;
                case RuntimeIntrinsicId.InterlockedOr:
                case RuntimeIntrinsicId.InterlockedAnd:
                    if (TryGetInterlockedBitwise(method, target, out InterlockedBitwiseIntrinsic bitwise))
                    {
                        intrinsic = new RuntimeIntrinsicInfo(id, AtomicReadModifyWriteFlags, bitwise: bitwise);
                        return true;
                    }
                    break;
                case RuntimeIntrinsicId.MemoryBarrier:
                case RuntimeIntrinsicId.VolatileRead:
                case RuntimeIntrinsicId.VolatileWrite:
                    intrinsic = new RuntimeIntrinsicInfo(id, MemoryBarrierFlags);
                    return true;
            }

            intrinsic = default;
            return false;
        }

        public static bool Supports(RuntimeIntrinsicId id, TargetInfo target)
            => id switch
            {
                RuntimeIntrinsicId.None => false,
                RuntimeIntrinsicId.InteropSys => target.IsRegisterBytecode,
                _ => target.Architecture is
                    Cnidaria.TargetArchitectureKind.RegisterBytecode or
                    Cnidaria.TargetArchitectureKind.RegisterBytecode64 or
                    Cnidaria.TargetArchitectureKind.I386 or
                    Cnidaria.TargetArchitectureKind.X86_64 or
                    Cnidaria.TargetArchitectureKind.RiscV32 or
                    Cnidaria.TargetArchitectureKind.RiscV64 or
                    Cnidaria.TargetArchitectureKind.Arm64,
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

            if (!TryClassifyAtomicValue(signatureValueType, method, pointerSize, out RuntimeType classifiedType, out int classifiedSize, out bool classifiedReference, out bool classifiedSigned))
                return false;

            intrinsic = new InterlockedCompareExchangeIntrinsic(classifiedType, classifiedSize, classifiedReference, classifiedSigned);
            return true;
        }

        /// <summary>Resolves the storage shape an atomic read-modify-write operates on</summary>
        private static bool TryClassifyAtomicValue(
            RuntimeType signatureValueType,
            RuntimeMethod method,
            int pointerSize,
            out RuntimeType valueType,
            out int size,
            out bool isReference,
            out bool isSigned)
        {
            size = 0;
            isReference = false;
            isSigned = false;

            valueType = signatureValueType;
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
                size = pointerSize;
                isReference = true;
                return true;
            }

            RuntimeType scalarType = valueType;
            if (scalarType.Kind == RuntimeTypeKind.Enum && scalarType.ElementType is not null)
                scalarType = scalarType.ElementType;

            RuntimePrimitiveKind primitive = scalarType.PrimitiveKind;
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

            isSigned = signed;
            return true;
        }

        private static bool TryGetInterlockedExchange(
            RuntimeMethod method,
            int pointerSize,
            out InterlockedExchangeIntrinsic intrinsic)
        {
            intrinsic = default;

            if (GetIntrinsicId(method) != RuntimeIntrinsicId.InterlockedExchange)
                return false;

            RuntimeType locationType = method.ParameterTypes[0];
            if (locationType.Kind != RuntimeTypeKind.ByRef || locationType.ElementType is null)
                return false;

            RuntimeType signatureValueType = locationType.ElementType;
            if (!SameType(signatureValueType, method.ParameterTypes[1]) ||
                !SameType(signatureValueType, method.ReturnType))
            {
                return false;
            }

            if (!TryClassifyAtomicValue(signatureValueType, method, pointerSize, out RuntimeType valueType, out int size, out bool isReference, out bool isSigned))
                return false;

            intrinsic = new InterlockedExchangeIntrinsic(valueType, size, isReference, isSigned);
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

        // Values narrower than a word and the targets without a scratch register keep the managed compare-exchange loop
        private static bool TryGetInterlockedBitwise(RuntimeMethod method, TargetInfo target, out InterlockedBitwiseIntrinsic intrinsic)
        {
            intrinsic = default;

            if (target.Architecture == Cnidaria.TargetArchitectureKind.I386)
                return false;

            RuntimeType locationType = method.ParameterTypes[0];
            if (locationType.Kind != RuntimeTypeKind.ByRef || locationType.ElementType is null)
                return false;

            RuntimeType signatureValueType = locationType.ElementType;
            if (!SameType(signatureValueType, method.ParameterTypes[1]) || !SameType(signatureValueType, method.ReturnType))
                return false;

            if (!TryClassifyAtomicValue(signatureValueType, method, target.PointerSize, out RuntimeType valueType, out int size, out bool isReference, out bool isSigned) ||
                isReference ||
                size < 4)
            {
                return false;
            }

            if (size == 8 && target.PointerSize == 4 && target.Architecture is not
                (Cnidaria.TargetArchitectureKind.RegisterBytecode or Cnidaria.TargetArchitectureKind.RegisterBytecode64))
            {
                return false;
            }

            intrinsic = new InterlockedBitwiseIntrinsic(valueType, size, isSigned);
            return true;
        }

        private static bool SameType(RuntimeType left, RuntimeType right)
            => ReferenceEquals(left, right) || left.TypeId == right.TypeId;
    }
}
