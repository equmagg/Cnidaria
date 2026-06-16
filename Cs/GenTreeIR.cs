using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal enum GenStackKind : byte
    {
        Unknown,
        Void,
        I4,
        I8,
        R4,
        R8,
        NativeInt,
        NativeUInt,
        Ref,
        Ptr,
        ByRef,
        Value,
        Null,
    }

    internal enum GenTreeKind : ushort
    {
        Nop,
        Copy,
        Reload,
        Spill,
        GcPoll,
        StackFrameOp,

        ConstI4,
        ConstI8,
        ConstR4Bits,
        ConstR8Bits,
        ConstNull,
        ConstString,
        DefaultValue,
        SizeOf,

        Local,
        LocalAddr,
        Arg,
        ArgAddr,
        Temp,
        TempAddr,
        ExceptionObject,

        Unary,
        Binary,
        Conv,

        Call,
        VirtualCall,
        NewObject,
        NewDelegate,
        DelegateCombine,
        DelegateRemove,
        DelegateInvoke,

        Field,
        FieldAddr,
        StaticField,
        StaticFieldAddr,

        LoadIndirect,
        StoreIndirect,
        StoreLocal,
        StoreArg,
        StoreTemp,
        StoreField,
        StoreStaticField,

        NewArray,
        ArrayElement,
        ArrayElementAddr,
        StoreArrayElement,
        ArrayDataRef,

        StaticData,
        StackAlloc,
        AllocHGlobal,
        FreeHGlobal,
        PointerElementAddr,
        PointerToByRef,
        PointerDiff,

        CastClass,
        IsInst,
        Box,
        UnboxAny,

        Eval,
        Branch,
        BranchTrue,
        BranchFalse,
        Return,
        Throw,
        Rethrow,
        EndFinally,
    }

    internal static class GenTreeLirKinds
    {
        public static bool IsCopyKind(GenTreeKind kind)
            => kind is GenTreeKind.Copy or GenTreeKind.Reload or GenTreeKind.Spill;

        public static bool IsSynthetic(GenTreeKind kind)
            => IsCopyKind(kind) || kind is GenTreeKind.GcPoll or GenTreeKind.StackFrameOp;

        public static bool IsRealTree(GenTree node)
            => node is not null && !IsSynthetic(node.Kind);
    }

    [Flags]
    internal enum GenTreeFlags : uint
    {
        None = 0,

        ContainsCall = 1u << 0,
        CanThrow = 1u << 1,
        SideEffect = 1u << 2,
        MemoryRead = 1u << 3,
        MemoryWrite = 1u << 4,
        LocalUse = 1u << 5,
        LocalDef = 1u << 6,
        AddressExposed = 1u << 7,
        Allocation = 1u << 8,
        ControlFlow = 1u << 9,
        ExceptionFlow = 1u << 10,
        GlobalRef = 1u << 11,
        Indirect = 1u << 12,
        Ordered = 1u << 13,

        VarDef = 1u << 14,
        VarUseAsg = 1u << 15,
        VarDeath = 1u << 16,
    }

    internal enum GenTreeBlockJumpKind : byte
    {
        None,
        FallThrough,
        Always,
        Conditional,
        Return,
        Throw,
        Rethrow,
        EndFinally,
    }

    [Flags]
    internal enum GenTreeBlockFlags : ushort
    {
        None = 0,
        Entry = 1 << 0,
        HasStackEntry = 1 << 1,
        HasStackExit = 1 << 2,
        TryEntry = 1 << 3,
        HandlerEntry = 1 << 4,
        InTryRegion = 1 << 5,
        InHandlerRegion = 1 << 6,
    }



    internal enum GenTreeMethodPhase : byte
    {
        ImportedHir,
        MorphedHir,
        LocalRewrittenHir,
        FlowgraphBuilt,
        HirLiveness,
        Ssa,
        SsaOptimized,
        RationalizedLir,
        LoweredLir,
        RegisterAllocated,
        CodeGenerated,
    }

    internal enum GenTempKind : byte
    {
        StackSpill,
        DupSpill,
        InlineArg,
        InlineLocal,
        InlineReturn,
        StructMaterialization,
    }

    internal readonly struct GenTemp
    {
        public readonly int Index;
        public readonly GenTempKind Kind;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;

        public GenTemp(int index, GenTempKind kind, RuntimeType? type, GenStackKind stackKind)
        {
            Index = index;
            Kind = kind;
            Type = type;
            StackKind = stackKind;
        }
    }


    internal enum GenLocalKind : byte
    {
        Argument,
        Local,
        Temporary,
    }

    internal enum GenLocalCategory : byte
    {
        Unclassified,
        RegularPromotedScalarLocal,
        PromotedStruct,
        PromotedStructField,
        AddressExposedLocal,
        ImplicitByRefPinnedRefLikeLocal,
        CompilerTemp,
        MemoryAliasedLocal,
        TrackedNonSsaLocal,
        UntrackedLocal,
    }
    [Flags]
    internal enum GenLocalFlags : ushort
    {
        None = 0,
        AddressExposed = 1 << 0,
        DoNotEnregister = 1 << 1,
        Promoted = 1 << 2,
        IsStructField = 1 << 3,
        IsCompilerTemp = 1 << 4,
        IsImplicitByRef = 1 << 5,
        IsPinned = 1 << 6,
        IsRefLike = 1 << 7,
        MemoryAliased = 1 << 8,
        InSsa = 1 << 9,
        Tracked = 1 << 10,
        IsStructMaterializationTemp = 1 << 11,
        IsLocalStorageByRefAlias = 1 << 12,
    }

    [Flags]
    internal enum GenTreeLsraFlags : ushort
    {
        None = 0,
        NoRegAtUse = 1 << 0,
        Spill = 1 << 1,
        Reload = 1 << 2,
        LastUse = 1 << 3,
        RegOptional = 1 << 4,
        ContainsInternalRegister = 1 << 5,
        DelayFree = 1 << 6,
        FixedRegister = 1 << 7,
    }

    internal readonly struct GenTreeValueKey : IEquatable<GenTreeValueKey>
    {
        public readonly GenTreeValueOrigin Origin;
        public readonly GenLocalKind LocalKind;
        public readonly int Index;
        public readonly GenTree? Node;
        public readonly bool HasSsaName;
        public readonly SsaSlot SsaSlot;
        public readonly int SsaVersion;

        private GenTreeValueKey(
            GenTreeValueOrigin origin,
            GenLocalKind localKind,
            int index,
            GenTree? node,
            bool hasSsaName = false,
            SsaSlot ssaSlot = default,
            int ssaVersion = -1)
        {
            Origin = origin;
            LocalKind = localKind;
            Index = index;
            Node = node;
            HasSsaName = hasSsaName;
            SsaSlot = ssaSlot;
            SsaVersion = ssaVersion;
        }

        public static GenTreeValueKey ForTree(GenTree node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            return new GenTreeValueKey(GenTreeValueOrigin.TreeNode, default, node.Id, node);
        }

        public static GenTreeValueKey ForLocal(GenLocalKind localKind, int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            GenTreeValueOrigin origin = localKind switch
            {
                GenLocalKind.Argument => GenTreeValueOrigin.Argument,
                GenLocalKind.Local => GenTreeValueOrigin.Local,
                GenLocalKind.Temporary => GenTreeValueOrigin.Temporary,
                _ => throw new ArgumentOutOfRangeException(nameof(localKind)),
            };
            return new GenTreeValueKey(origin, localKind, index, null);
        }

        public static GenTreeValueKey ForSsaValue(SsaValueName name)
        {
            if (name.Version <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(name));
            if (!name.Slot.HasLclNum)
                throw new ArgumentException("SSA value names must be based on a concrete lclNum.", nameof(name));

            GenLocalKind localKind = name.Slot.Kind switch
            {
                SsaSlotKind.Arg => GenLocalKind.Argument,
                SsaSlotKind.Local => GenLocalKind.Local,
                SsaSlotKind.Temp => GenLocalKind.Temporary,
                _ => throw new ArgumentOutOfRangeException(nameof(name)),
            };

            GenTreeValueOrigin origin = localKind switch
            {
                GenLocalKind.Argument => GenTreeValueOrigin.Argument,
                GenLocalKind.Local => GenTreeValueOrigin.Local,
                GenLocalKind.Temporary => GenTreeValueOrigin.Temporary,
                _ => throw new ArgumentOutOfRangeException(nameof(name)),
            };

            return new GenTreeValueKey(origin, localKind, name.Slot.Index, null, true, name.Slot, name.Version);
        }

        public bool IsTreeNode => Origin == GenTreeValueOrigin.TreeNode;
        public bool IsLocalDescriptor => Origin != GenTreeValueOrigin.TreeNode && !HasSsaName;
        public bool IsSsaValue => HasSsaName;

        public bool Equals(GenTreeValueKey other)
        {
            if (HasSsaName || other.HasSsaName)
                return HasSsaName && other.HasSsaName && SsaSlot.Equals(other.SsaSlot) && SsaVersion == other.SsaVersion;

            return Origin == other.Origin &&
                   LocalKind == other.LocalKind &&
                   Index == other.Index &&
                   ReferenceEquals(Node, other.Node);
        }

        public override bool Equals(object? obj) => obj is GenTreeValueKey other && Equals(other);

        public override int GetHashCode()
        {
            if (HasSsaName)
                return HashCode.Combine(SsaSlot, SsaVersion);

            return HashCode.Combine(
                (int)Origin,
                (int)LocalKind,
                Index,
                Node is null ? 0 : RuntimeHelpers.GetHashCode(Node));
        }

        public override string ToString()
        {
            if (Origin == GenTreeValueOrigin.TreeNode)
                return Node is null ? "<tree:null>" : $"t{Node.Id}";
            string prefix = LocalKind switch
            {
                GenLocalKind.Argument => "arg",
                GenLocalKind.Local => "loc",
                GenLocalKind.Temporary => "tmp",
                _ => "local",
            };
            return HasSsaName
                ? $"V{SsaSlot.LclNum}_{SsaVersion}"
                : $"{prefix}{Index}";
        }
    }

    internal class LclVarDsc
    {
        public int LclNum { get; }
        public GenLocalKind Kind { get; }
        public int Index { get; }
        public RuntimeType? Type { get; }
        public GenStackKind StackKind { get; }
        public GenLocalCategory Category { get; internal set; }
        public GenLocalFlags LocalFlags { get; private set; }
        public int ParentLclNum { get; internal set; } = -1;
        public int FieldOrdinal { get; internal set; } = -1;
        public int FieldOffset { get; internal set; } = -1;
        public int FieldSize { get; internal set; } = -1;
        public RuntimeField? PromotedField { get; internal set; }
        private readonly Dictionary<int, GenLocalDescriptor> _promotedFieldsByFieldId = new Dictionary<int, GenLocalDescriptor>();
        private readonly Dictionary<int, GenLocalDescriptor> _promotedFieldsByOffset = new Dictionary<int, GenLocalDescriptor>();
        public bool Tracked { get => (LocalFlags & GenLocalFlags.Tracked) != 0; internal set => SetLocalFlag(GenLocalFlags.Tracked, value); }
        public int VarIndex { get; internal set; } = -1;
        public bool AddressExposed { get => (LocalFlags & GenLocalFlags.AddressExposed) != 0; internal set => SetLocalFlag(GenLocalFlags.AddressExposed, value); }
        public bool Promoted { get => (LocalFlags & GenLocalFlags.Promoted) != 0; internal set => SetLocalFlag(GenLocalFlags.Promoted, value); }
        public bool DoNotEnregister { get => (LocalFlags & GenLocalFlags.DoNotEnregister) != 0; internal set => SetLocalFlag(GenLocalFlags.DoNotEnregister, value); }
        public bool SsaPromoted { get => (LocalFlags & GenLocalFlags.InSsa) != 0; internal set => SetLocalFlag(GenLocalFlags.InSsa, value); }
        public int UseCount { get; internal set; }
        public int DefCount { get; internal set; }
        public int FullDefCount { get; internal set; }
        public int PartialDefCount { get; internal set; }
        public double WeightedUseCount { get; internal set; }
        public double WeightedDefCount { get; internal set; }
        public bool IsCompilerTemp { get => (LocalFlags & GenLocalFlags.IsCompilerTemp) != 0; internal set => SetLocalFlag(GenLocalFlags.IsCompilerTemp, value); }
        public bool IsImplicitByRef { get => (LocalFlags & GenLocalFlags.IsImplicitByRef) != 0; internal set => SetLocalFlag(GenLocalFlags.IsImplicitByRef, value); }
        public bool Pinned { get => (LocalFlags & GenLocalFlags.IsPinned) != 0; internal set => SetLocalFlag(GenLocalFlags.IsPinned, value); }
        public bool IsRefLike { get => (LocalFlags & GenLocalFlags.IsRefLike) != 0; internal set => SetLocalFlag(GenLocalFlags.IsRefLike, value); }
        public bool IsStructField { get => (LocalFlags & GenLocalFlags.IsStructField) != 0; internal set => SetLocalFlag(GenLocalFlags.IsStructField, value); }
        public bool IsStructMaterializationTemp { get => (LocalFlags & GenLocalFlags.IsStructMaterializationTemp) != 0; internal set => SetLocalFlag(GenLocalFlags.IsStructMaterializationTemp, value); }
        public bool IsLocalStorageByRefAlias { get => (LocalFlags & GenLocalFlags.IsLocalStorageByRefAlias) != 0; internal set => SetLocalFlag(GenLocalFlags.IsLocalStorageByRefAlias, value); }
        public bool MemoryAliased { get => (LocalFlags & GenLocalFlags.MemoryAliased) != 0; internal set => SetLocalFlag(GenLocalFlags.MemoryAliased, value); }
        public bool HasMemoryAlias => AddressExposed || MemoryAliased;
        public MachineRegister RegNum { get; internal set; } = MachineRegister.Invalid;
        public bool Register { get; internal set; }
        public bool Spilled { get; internal set; }
        public RegisterOperand FrameHome { get; internal set; } = RegisterOperand.None;
        public bool LRACandidate { get; internal set; }
        public bool IsTrackedForLiveness => Tracked && VarIndex >= 0;
        public bool IsRegisterCandidate => IsTrackedForLiveness && LRACandidate && !DoNotEnregister && !HasMemoryAlias;
        public bool CanBeSsaRenamedAsScalar =>
            SsaPromoted &&
            Tracked &&
            VarIndex >= 0 &&
            !HasMemoryAlias &&
            (
                (!IsStructField && (Category == GenLocalCategory.RegularPromotedScalarLocal || Category == GenLocalCategory.CompilerTemp)) ||
                (IsStructField && Category == GenLocalCategory.PromotedStructField && ParentLclNum >= 0)
            );

        public ImmutableArray<SsaDescriptor> PerSsaData => _ssaDescriptors;

        private readonly Dictionary<int, RegisterAllocationInfo> _ssaAllocations = new Dictionary<int, RegisterAllocationInfo>();
        private ImmutableArray<SsaDescriptor> _ssaDescriptors = ImmutableArray<SsaDescriptor>.Empty;

        public IReadOnlyDictionary<int, RegisterAllocationInfo> SsaAllocations => _ssaAllocations;
        public ImmutableArray<SsaDescriptor> SsaDescriptors => _ssaDescriptors;

        public bool HasSsaAllocations => _ssaAllocations.Count != 0;
        public bool HasSsaDescriptors => !_ssaDescriptors.IsDefaultOrEmpty && _ssaDescriptors.Length > SsaConfig.ReservedSsaNumber + 1;

        internal void ResetTrackingAndLivenessState()
        {
            bool promotedParent = Category == GenLocalCategory.PromotedStruct;
            bool promotedField = Category == GenLocalCategory.PromotedStructField;
            Tracked = false;
            VarIndex = -1;
            Promoted = promotedParent || promotedField;
            SsaPromoted = false;
            LRACandidate = false;
            IsLocalStorageByRefAlias = false;
            DoNotEnregister = promotedParent || AddressExposed || MemoryAliased || IsCompilerTemp || Pinned || IsRefLike;
            if (promotedField && !AddressExposed && !MemoryAliased && !IsImplicitByRef && !Pinned && !IsRefLike)
                DoNotEnregister = false;
            UseCount = 0;
            DefCount = 0;
            FullDefCount = 0;
            PartialDefCount = 0;
            WeightedUseCount = 0;
            WeightedDefCount = 0;
            SetSsaDescriptors(ImmutableArray<SsaDescriptor>.Empty);
            ResetRegisterAllocationState();
        }

        internal void ResetPreSsaClassification()
        {
            bool wasPromotedField = IsStructField && ParentLclNum >= 0 && PromotedField is not null;
            bool wasStructMaterializationTemp = IsStructMaterializationTemp;
            int oldParentLclNum = ParentLclNum;
            int oldFieldOrdinal = FieldOrdinal;
            int oldFieldOffset = FieldOffset;
            int oldFieldSize = FieldSize;
            RuntimeField? oldPromotedField = PromotedField;

            LocalFlags = GenLocalFlags.None;
            Category = GenLocalCategory.Unclassified;
            ParentLclNum = -1;
            FieldOrdinal = -1;
            FieldOffset = -1;
            FieldSize = -1;
            PromotedField = null;
            _promotedFieldsByFieldId.Clear();
            _promotedFieldsByOffset.Clear();

            if (wasStructMaterializationTemp)
                IsStructMaterializationTemp = true;

            if (wasPromotedField)
            {
                MarkPromotedStructField(
                    oldParentLclNum,
                    oldFieldOrdinal,
                    oldFieldOffset,
                    oldFieldSize,
                    oldPromotedField);
            }

            if (Kind == GenLocalKind.Temporary && !wasPromotedField)
            {
                if (wasStructMaterializationTemp && IsPromotableStructMaterializationType(Type))
                {
                    MarkPromotedStructParent();
                }
                else
                {
                    IsCompilerTemp = true;
                    Category = GenLocalCategory.CompilerTemp;
                }
            }
            ClassifySpecialStorage();
            ResetTrackingAndLivenessState();
        }

        internal void ClassifySpecialStorage()
        {
            if (StackKind == GenStackKind.ByRef || Type?.Kind == RuntimeTypeKind.ByRef)
                IsImplicitByRef = true;

            if (IsKnownRefLike(Type))
                IsRefLike = true;

            if (Pinned || IsRefLike)
            {
                Category = GenLocalCategory.ImplicitByRefPinnedRefLikeLocal;
                DoNotEnregister = true;
                SsaPromoted = false;
                LRACandidate = false;
            }
        }

        internal void MarkLocalStorageByRefAlias()
        {
            if (!IsImplicitByRef)
                return;

            IsLocalStorageByRefAlias = true;
            SsaPromoted = false;
            LRACandidate = false;
        }

        internal void MarkAddressExposed()
        {
            AddressExposed = true;
            MemoryAliased = true;
            Category = GenLocalCategory.AddressExposedLocal;
            DoNotEnregister = true;
            SsaPromoted = false;
            Tracked = false;
            LRACandidate = false;
            VarIndex = -1;
        }

        internal void MarkMemoryAliased()
        {
            MemoryAliased = true;
            if (!AddressExposed)
                Category = GenLocalCategory.MemoryAliasedLocal;
            DoNotEnregister = true;
            SsaPromoted = false;
            LRACandidate = false;
        }

        internal void MarkPromotedStructParent()
        {
            if (AddressExposed)
                return;

            Promoted = true;
            MemoryAliased = false;
            IsCompilerTemp = false;
            Category = GenLocalCategory.PromotedStruct;
            DoNotEnregister = true;
            SsaPromoted = false;
            Tracked = false;
            LRACandidate = false;
            VarIndex = -1;
        }

        internal void MarkPromotedStructField(int parentLclNum, int ordinal, int offset, int size, RuntimeField? field = null)
        {
            IsStructField = true;
            Promoted = true;
            IsCompilerTemp = false;
            ParentLclNum = parentLclNum;
            FieldOrdinal = ordinal;
            FieldOffset = offset;
            FieldSize = size;
            PromotedField = field;
            Category = GenLocalCategory.PromotedStructField;
            AddressExposed = false;
            MemoryAliased = false;
            DoNotEnregister = false;
        }

        internal IReadOnlyDictionary<int, GenLocalDescriptor> PromotedFieldsByFieldId => _promotedFieldsByFieldId;

        internal bool HasPromotedStructFields => _promotedFieldsByFieldId.Count != 0;

        internal bool TryGetPromotedField(RuntimeField field, out GenLocalDescriptor descriptor)
        {
            if (field is null)
                throw new ArgumentNullException(nameof(field));
            return _promotedFieldsByFieldId.TryGetValue(field.FieldId, out descriptor!);
        }

        internal void AddPromotedField(RuntimeField field, GenLocalDescriptor descriptor)
        {
            if (field is null)
                throw new ArgumentNullException(nameof(field));
            if (descriptor is null)
                throw new ArgumentNullException(nameof(descriptor));
            if (!ReferenceEquals(descriptor.PromotedField, field))
                throw new InvalidOperationException("Promoted field descriptor is bound to a different RuntimeField.");
            _promotedFieldsByFieldId[field.FieldId] = descriptor;
            _promotedFieldsByOffset[field.Offset] = descriptor;
        }

        internal void MarkRegularPromotedScalar(int denseVarIndex)
        {
            if (HasMemoryAlias || Pinned || IsRefLike)
                throw new InvalidOperationException($"Cannot mark memory-aliased local as an SSA scalar: {this}.");

            if (IsImplicitByRef && IsLocalStorageByRefAlias)
                throw new InvalidOperationException($"Cannot mark local-storage byref alias as an SSA scalar: {this}.");

            if (IsStructField && Category != GenLocalCategory.PromotedStructField)
                throw new InvalidOperationException($"Cannot mark malformed promoted struct field as an SSA scalar: {this}.");

            Tracked = true;
            VarIndex = denseVarIndex;
            SsaPromoted = true;
            Promoted = true;
            LRACandidate = true;
            DoNotEnregister = false;
            if (IsStructField)
            {
                Category = GenLocalCategory.PromotedStructField;
            }
            else if (!IsCompilerTemp)
            {
                Category = GenLocalCategory.RegularPromotedScalarLocal;
            }
            else
            {
                Category = GenLocalCategory.CompilerTemp;
            }
        }

        internal void MarkTrackedButNotSsa(int denseVarIndex, GenLocalCategory category)
        {
            Tracked = true;
            VarIndex = denseVarIndex;
            SsaPromoted = false;
            LRACandidate = false;
            DoNotEnregister = true;
            if (Category == GenLocalCategory.Unclassified || Category == GenLocalCategory.RegularPromotedScalarLocal)
                Category = category;
        }

        internal void MarkUntracked()
        {
            bool promotedParent = Category == GenLocalCategory.PromotedStruct;
            bool promotedField = Category == GenLocalCategory.PromotedStructField;
            Tracked = false;
            VarIndex = -1;
            SsaPromoted = false;
            LRACandidate = false;
            DoNotEnregister = true;
            Promoted = promotedParent || promotedField;
            if (Category == GenLocalCategory.Unclassified)
                Category = GenLocalCategory.UntrackedLocal;
        }

        private void SetLocalFlag(GenLocalFlags flag, bool value)
        {
            LocalFlags = value ? (LocalFlags | flag) : (LocalFlags & ~flag);
        }

        internal static bool IsPromotableStructMaterializationType(RuntimeType? type)
        {
            return type is not null &&
                   type.IsValueType &&
                   type.Kind == RuntimeTypeKind.Struct &&
                   type.InstanceFields.Length != 0;
        }

        private static bool IsKnownRefLike(RuntimeType? type)
        {
            if (type is null)
                return false;

            return type.Name == "Span`1" ||
                   type.Name == "ReadOnlySpan`1" ||
                   type.Name == "Span" ||
                   type.Name == "ReadOnlySpan";
        }

        internal void ResetRegisterAllocationState()
        {
            RegNum = MachineRegister.Invalid;
            Register = false;
            Spilled = false;
            FrameHome = RegisterOperand.None;
            _ssaAllocations.Clear();
        }

        internal void AddUse(double weight)
        {
            UseCount++;
            WeightedUseCount += Math.Max(1.0, weight);
        }

        internal void AddFullDefinition(double weight)
        {
            DefCount++;
            FullDefCount++;
            WeightedDefCount += Math.Max(1.0, weight);
        }

        internal void AddPartialDefinition(double weight)
        {
            DefCount++;
            PartialDefCount++;
            WeightedDefCount += Math.Max(1.0, weight);
        }

        internal void SetSsaAllocation(int ssaVersion, RegisterAllocationInfo allocation)
        {
            if (ssaVersion <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(ssaVersion));
            if (allocation is null)
                throw new ArgumentNullException(nameof(allocation));
            _ssaAllocations[ssaVersion] = allocation;
        }

        internal bool TryGetSsaAllocation(int ssaVersion, out RegisterAllocationInfo? allocation)
            => _ssaAllocations.TryGetValue(ssaVersion, out allocation);

        internal void SetSsaDescriptors(ImmutableArray<SsaDescriptor> descriptors)
        {
            if (descriptors.IsDefaultOrEmpty)
            {
                _ssaDescriptors = ImmutableArray<SsaDescriptor>.Empty;
                return;
            }

            if (descriptors.Length <= SsaConfig.ReservedSsaNumber || descriptors[SsaConfig.ReservedSsaNumber] is not null)
                throw new InvalidOperationException($"SSA descriptor table must reserve index zero for {ValueKey}.");

            for (int i = SsaConfig.FirstSsaNumber; i < descriptors.Length; i++)
            {
                if (descriptors[i] is null || descriptors[i].SsaNumber != i)
                    throw new InvalidOperationException($"SSA descriptor table is not dense for {ValueKey}.");
            }

            _ssaDescriptors = descriptors;
        }

        internal SsaDescriptor GetSsaDescriptor(int ssaVersion)
        {
            if (ssaVersion <= SsaConfig.ReservedSsaNumber || (uint)ssaVersion >= (uint)_ssaDescriptors.Length)
                throw new ArgumentOutOfRangeException(nameof(ssaVersion));
            var descriptor = _ssaDescriptors[ssaVersion];
            if (descriptor is null || descriptor.SsaNumber != ssaVersion)
                throw new InvalidOperationException($"SSA descriptor table is not dense for {ValueKey}.");
            return descriptor;
        }

        internal bool TryGetSsaDescriptor(int ssaVersion, out SsaDescriptor descriptor)
        {
            if (ssaVersion > SsaConfig.ReservedSsaNumber && (uint)ssaVersion < (uint)_ssaDescriptors.Length)
            {
                descriptor = _ssaDescriptors[ssaVersion];
                if (descriptor is not null && descriptor.SsaNumber == ssaVersion)
                    return true;
            }

            descriptor = null!;
            return false;
        }

        public LclVarDsc(int lclNum, GenLocalKind kind, int index, RuntimeType? type, GenStackKind stackKind, GenLocalCategory category = GenLocalCategory.Unclassified)
        {
            if (lclNum < 0) throw new ArgumentOutOfRangeException(nameof(lclNum));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            LclNum = lclNum;
            Kind = kind;
            Index = index;
            Type = type;
            StackKind = stackKind;
            Category = category;
            if (kind == GenLocalKind.Temporary)
                IsCompilerTemp = true;
            ClassifySpecialStorage();
            Tracked = false;
        }

        public GenTreeValueKey ValueKey => GenTreeValueKey.ForLocal(Kind, Index);

        public override string ToString()
        {
            string prefix = Kind switch
            {
                GenLocalKind.Argument => "A",
                GenLocalKind.Local => "V",
                GenLocalKind.Temporary => "T",
                _ => "L",
            };
            return $"{prefix}{Index}#{LclNum} {Category}" + (Tracked ? $" tracked={VarIndex}" : " untracked") +
                   $" uses={UseCount}/{WeightedUseCount:0.##}" +
                   $" defs={DefCount}/{WeightedDefCount:0.##}" +
                   (PartialDefCount != 0 ? $" partial-defs={PartialDefCount}" : string.Empty) +
                   (Register ? $" reg={MachineRegisters.Format(RegNum)}" : $" home={FrameHome}") +
                   (Spilled ? " spilled" : string.Empty) +
                   (AddressExposed ? " addr-exposed" : string.Empty) +
                   (Promoted ? " promoted" : string.Empty) +
                   (IsStructField ? $" parent={ParentLclNum} fld={PromotedField?.Name ?? FieldOrdinal.ToString()}" : string.Empty) +
                   (SsaPromoted ? " ssa" : string.Empty) +
                   (HasSsaDescriptors ? $" ssa-defs={_ssaDescriptors.Length - 1}" : string.Empty) +
                   (HasSsaAllocations ? $" ssa-allocs={_ssaAllocations.Count}" : string.Empty);
        }
    }

    internal sealed class GenLocalDescriptor : LclVarDsc
    {
        public GenLocalDescriptor(int lclNum, GenLocalKind kind, int index, RuntimeType? type, GenStackKind stackKind, GenLocalCategory category = GenLocalCategory.Unclassified)
            : base(lclNum, kind, index, type, stackKind, category)
        {
        }
    }

    internal readonly struct GenTreeInternalRegister
    {
        public readonly MachineRegister Register;
        public readonly RegisterClass RegisterClass;
        public readonly int RefPosition;
        public readonly GenTreeValueKey Owner;

        public GenTreeInternalRegister(MachineRegister register, RegisterClass registerClass, int refPosition, GenTreeValueKey owner)
        {
            Register = register;
            RegisterClass = registerClass;
            RefPosition = refPosition;
            Owner = owner;
        }

        public override string ToString() => $"{MachineRegisters.Format(Register)}@{RefPosition}";
    }

    internal sealed class GenTreeLsraInfo
    {
        public static readonly GenTreeLsraInfo Empty = new GenTreeLsraInfo();

        public MachineRegister GtRegNum { get; internal set; } = MachineRegister.Invalid;
        public GenTreeLsraFlags Flags { get; internal set; }
        public RegisterOperand Home { get; internal set; } = RegisterOperand.None;
        public RegisterValueLocation LocationAtDefinition { get; internal set; }
        public ImmutableArray<RegisterOperand> CodegenResults { get; internal set; } = ImmutableArray<RegisterOperand>.Empty;
        public ImmutableArray<RegisterOperand> CodegenUses { get; internal set; } = ImmutableArray<RegisterOperand>.Empty;
        public ImmutableArray<OperandRole> CodegenUseRoles { get; internal set; } = ImmutableArray<OperandRole>.Empty;
        public ImmutableArray<GenTreeValueKey> CodegenResultValues { get; internal set; } = ImmutableArray<GenTreeValueKey>.Empty;
        public ImmutableArray<GenTreeValueKey> CodegenUseValues { get; internal set; } = ImmutableArray<GenTreeValueKey>.Empty;
        public ImmutableArray<GenTreeInternalRegister> InternalRegisters { get; internal set; } = ImmutableArray<GenTreeInternalRegister>.Empty;
        public MoveFlags MoveFlags { get; internal set; }
        public FrameOperation FrameOperation { get; internal set; }
        public int Immediate { get; internal set; }
        public string? Comment { get; internal set; }

        public bool NoRegAtUse => (Flags & GenTreeLsraFlags.NoRegAtUse) != 0;
        public bool HasRegister => GtRegNum != MachineRegister.Invalid;
        public bool IsMove => MoveFlags != MoveFlags.None || (CodegenResults.Length == 1 && CodegenUses.Length == 1 && FrameOperation == FrameOperation.None && Comment is not null && Comment.Contains("move", StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class GenTree
    {
        public int Id { get; }
        public GenTreeKind Kind { get; internal set; }
        public int Pc { get; }
        public BytecodeOp SourceOp { get; internal set; }
        public RuntimeType? Type { get; internal set; }
        public GenStackKind StackKind { get; internal set; }
        public GenTreeFlags Flags { get; internal set; }
        public ImmutableArray<GenTree> Operands { get; private set; }
        public GenTree? Parent { get; private set; }
        public GenTree? Previous { get; internal set; }
        public GenTree? Next { get; internal set; }
        public int LinearId { get; internal set; } = -1;
        public int LinearBlockId { get; internal set; } = -1;
        public int LinearOrdinal { get; internal set; } = -1;
        public GenTreeLinearKind LinearKind { get; internal set; } = GenTreeLinearKind.Tree;
        public GenTree? RegisterResult { get; internal set; }
        public ImmutableArray<GenTree> RegisterResults { get; internal set; } = ImmutableArray<GenTree>.Empty;
        public ImmutableArray<LirOperandFlags> OperandFlags { get; internal set; } = ImmutableArray<LirOperandFlags>.Empty;
        public ImmutableArray<GenTree> RegisterUses { get; internal set; } = ImmutableArray<GenTree>.Empty;
        public GenTreeLinearLoweringInfo LinearLowering { get; internal set; }
        public LinearMemoryAccess LinearMemoryAccess { get; internal set; } = LinearMemoryAccess.None;
        public RegisterAllocationInfo? RegisterAllocation { get; internal set; }
        public RegisterOperand RegisterHome { get; internal set; } = RegisterOperand.None;
        public RegisterValueLocation RegisterLocationAtDefinition { get; internal set; }
        public int LinearPhiCopyFromBlockId { get; internal set; } = -1;
        public int LinearPhiCopyToBlockId { get; internal set; } = -1;
        public bool IsContainedInLinear { get; internal set; }
        public GenLocalDescriptor? LocalDescriptor { get; internal set; }
        public GenTreeValueKey ValueKey { get; internal set; }
        public SsaValueName? SsaValueName { get; internal set; }
        public SsaValueName? SsaStoreTargetName { get; internal set; }
        public GenTreeLsraInfo LsraInfo { get; private set; } = GenTreeLsraInfo.Empty;
        public ImmutableArray<GenTreeInternalRegister> InternalRegisters => LsraInfo.InternalRegisters;
        public GenTreeLsraFlags LsraFlags => LsraInfo.Flags;

        public int BlockId => LinearBlockId;
        public int Ordinal => LinearOrdinal;
        public int GenTreeLinearId => LinearId;
        public GenTree Source => this;
        public GenTreeKind TreeKind => Kind;
        public ImmutableArray<RegisterOperand> Results => LsraInfo.CodegenResults;
        public ImmutableArray<RegisterOperand> Uses => LsraInfo.CodegenUses;
        public ImmutableArray<OperandRole> UseRoles => LsraInfo.CodegenUseRoles;
        public FrameOperation FrameOperation => LsraInfo.FrameOperation;
        public int Immediate => LsraInfo.Immediate;
        public string? Comment => LsraInfo.Comment;
        public MoveFlags MoveFlags => LsraInfo.MoveFlags;

        public GenTreeValueKey LinearValueKey => ValueKey;
        public bool HasSsaUse => SsaValueName.HasValue;
        public bool HasSsaDefinition => SsaStoreTargetName.HasValue;

        public MoveKind MoveKind
        {
            get
            {
                if (Kind is not (GenTreeKind.Copy or GenTreeKind.Reload or GenTreeKind.Spill) || Results.Length != 1 || Uses.Length != 1)
                    return MoveKind.None;

                var source = Uses[0];
                var destination = Results[0];
                if (source.IsAddress)
                    return destination.IsRegister ? MoveKind.LoadAddress : MoveKind.StoreAddress;
                if (source.IsRegister && destination.IsRegister)
                    return MoveKind.Register;
                if (!source.IsRegister && destination.IsRegister)
                    return MoveKind.Load;
                if (source.IsRegister && !destination.IsRegister)
                    return MoveKind.Store;
                return MoveKind.MemoryToMemory;
            }
        }

        public bool KillsCallerSavedRegisters
        {
            get
            {
                if (Kind is GenTreeKind.GcPoll or GenTreeKind.Copy or GenTreeKind.Reload or GenTreeKind.Spill or GenTreeKind.StackFrameOp)
                    return false;
                return GenTreeLinearLoweringClassifier
                    .Classify(this, RegisterResult, RegisterUses)
                    .HasFlag(GenTreeLinearFlags.CallerSavedKill);
            }
        }

        public bool IsPhiCopy => LinearKind == GenTreeLinearKind.PhiCopy && LinearPhiCopyFromBlockId >= 0 && LinearPhiCopyToBlockId >= 0;

        public bool HasLoweringFlag(GenTreeLinearFlags flag) => LinearLowering.HasFlag(flag);

        public bool ContainsCall => (Flags & GenTreeFlags.ContainsCall) != 0;
        public bool CanThrow => (Flags & GenTreeFlags.CanThrow) != 0;
        public bool HasSideEffect => (Flags & GenTreeFlags.SideEffect) != 0;
        public bool ReadsMemory => (Flags & GenTreeFlags.MemoryRead) != 0;
        public bool WritesMemory => (Flags & GenTreeFlags.MemoryWrite) != 0;

        public int Int32 { get; }
        public long Int64 { get; }
        public string? Text { get; }
        public RuntimeType? RuntimeType { get; }
        public RuntimeField? Field { get; }
        public RuntimeMethod? Method { get; }
        public NumericConvKind ConvKind { get; }
        public NumericConvFlags ConvFlags { get; }
        public int TargetPc { get; }
        public int TargetBlockId { get; }

        public GenTree(
            int id,
            GenTreeKind kind,
            int pc,
            BytecodeOp sourceOp,
            RuntimeType? type,
            GenStackKind stackKind,
            GenTreeFlags flags,
            ImmutableArray<GenTree> operands,
            int int32 = 0,
            long int64 = 0,
            string? text = null,
            RuntimeType? runtimeType = null,
            RuntimeField? field = null,
            RuntimeMethod? method = null,
            NumericConvKind convKind = default,
            NumericConvFlags convFlags = default,
            int targetPc = -1,
            int targetBlockId = -1)
        {
            Id = id;
            Kind = kind;
            Pc = pc;
            SourceOp = sourceOp;
            Type = type;
            StackKind = stackKind;
            Flags = flags;
            SetOperands(operands);
            Int32 = int32;
            Int64 = int64;
            Text = text;
            RuntimeType = runtimeType;
            Field = field;
            Method = method;
            ConvKind = convKind;
            ConvFlags = convFlags;
            TargetPc = targetPc;
            TargetBlockId = targetBlockId;
            ValueKey = GenTreeValueKey.ForTree(this);
        }

        internal void SetOperands(ImmutableArray<GenTree> operands)
        {
            Operands = operands.IsDefault ? ImmutableArray<GenTree>.Empty : operands;
            for (int i = 0; i < Operands.Length; i++)
                Operands[i].Parent = this;
        }

        internal void AttachSsaUse(SsaValueName value)
        {
            SsaValueName = value;
            SsaStoreTargetName = null;
            ValueKey = GenTreeValueKey.ForSsaValue(value);
        }

        internal void AttachSsaDefinition(SsaValueName value, RuntimeType? type, GenStackKind stackKind)
        {
            SsaValueName = null;
            SsaStoreTargetName = value;
            ValueKey = GenTreeValueKey.ForSsaValue(value);
            Type = type;
            StackKind = stackKind;
        }

        internal void ClearSsaAnnotation()
        {
            SsaValueName = null;
            SsaStoreTargetName = null;
            ValueKey = GenTreeValueKey.ForTree(this);
        }

        internal void ResetLinearState()
        {
            Previous = null;
            Next = null;
            LinearId = -1;
            LinearBlockId = -1;
            LinearOrdinal = -1;
            LinearKind = GenTreeLinearKind.Tree;
            RegisterResult = null;
            RegisterResults = ImmutableArray<GenTree>.Empty;
            OperandFlags = ImmutableArray<LirOperandFlags>.Empty;
            RegisterUses = ImmutableArray<GenTree>.Empty;
            LinearLowering = default;
            LinearMemoryAccess = LinearMemoryAccess.None;
            RegisterAllocation = null;
            RegisterHome = RegisterOperand.None;
            RegisterLocationAtDefinition = default;
            LinearPhiCopyFromBlockId = -1;
            LinearPhiCopyToBlockId = -1;
            IsContainedInLinear = false;
            LsraInfo = GenTreeLsraInfo.Empty;
        }

        internal void SetLinearState(
            int linearId,
            int blockId,
            int ordinal,
            GenTreeLinearKind linearKind,
            GenTree? result,
            ImmutableArray<LirOperandFlags> operands,
            ImmutableArray<GenTree> uses,
            GenTreeLinearLoweringInfo lowering,
            LinearMemoryAccess memoryAccess,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            var results = result is null ? ImmutableArray<GenTree>.Empty : ImmutableArray.Create(result);
            SetLinearState(
                linearId,
                blockId,
                ordinal,
                linearKind,
                results,
                operands,
                uses,
                lowering,
                memoryAccess,
                phiCopyFromBlockId,
                phiCopyToBlockId);
        }

        internal void SetLinearState(
            int linearId,
            int blockId,
            int ordinal,
            GenTreeLinearKind linearKind,
            ImmutableArray<GenTree> results,
            ImmutableArray<LirOperandFlags> operands,
            ImmutableArray<GenTree> uses,
            GenTreeLinearLoweringInfo lowering,
            LinearMemoryAccess memoryAccess,
            int phiCopyFromBlockId = -1,
            int phiCopyToBlockId = -1)
        {
            var normalizedResults = results.IsDefault ? ImmutableArray<GenTree>.Empty : results;
            LinearId = linearId;
            LinearBlockId = blockId;
            LinearOrdinal = ordinal;
            LinearKind = linearKind;
            RegisterResults = normalizedResults;
            RegisterResult = normalizedResults.Length == 1 ? normalizedResults[0] : null;
            OperandFlags = operands.IsDefault ? ImmutableArray<LirOperandFlags>.Empty : operands;
            RegisterUses = uses.IsDefault ? ImmutableArray<GenTree>.Empty : uses;
            LinearLowering = lowering;
            LinearMemoryAccess = memoryAccess;
            LinearPhiCopyFromBlockId = phiCopyFromBlockId;
            LinearPhiCopyToBlockId = phiCopyToBlockId;
        }
        internal void AttachRegisterAllocation(RegisterAllocationInfo allocation)
        {
            if (allocation is null)
                throw new ArgumentNullException(nameof(allocation));
            if (!allocation.ValueKey.Equals(LinearValueKey))
                throw new InvalidOperationException("Cannot attach allocation for a different GenTree value.");

            RegisterAllocation = allocation;
            RegisterHome = allocation.Home;
            var abi = MachineAbi.ClassifyStorageValue(Type, StackKind);
            RegisterLocationAtDefinition = allocation.ValueLocationAtDefinition(abi);

            var info = new GenTreeLsraInfo
            {
                GtRegNum = allocation.LocationAtDefinition().IsRegister ? allocation.LocationAtDefinition().Register : MachineRegister.Invalid,
                Home = allocation.Home,
                LocationAtDefinition = RegisterLocationAtDefinition,
                Flags = GenTreeLsraFlags.None
            };
            LsraInfo = info;
        }

        internal void AttachLsraInfo(GenTreeLsraInfo info)
        {
            LsraInfo = info ?? GenTreeLsraInfo.Empty;
        }

        public RegisterValueLocation GetRegisterLocation(int position, bool isReturn = false)
        {
            if (RegisterAllocation is null)
                throw new InvalidOperationException($"GenTree node has no register allocation: {this}.");

            var abi = MachineAbi.ClassifyValue(Type, StackKind, isReturn);
            return RegisterAllocation.ValueLocationAt(position, abi);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            GenTreeDumper.AppendNode(sb, this);
            return sb.ToString();
        }
    }


    internal static class GenTreeTreeOrder
    {
        public static ImmutableArray<GenTree> BuildStatement(GenTree root)
        {
            if (root is null)
                throw new ArgumentNullException(nameof(root));

            var builder = ImmutableArray.CreateBuilder<GenTree>();
            var seen = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            CollectUnique(root, seen, builder);
            builder.Sort(static (left, right) => left.Id.CompareTo(right.Id));

            var result = builder.ToImmutable();
            ValidateStatementTreeList(root, result);
            return result;
        }

        public static ImmutableArray<ImmutableArray<GenTree>> BuildStatements(ImmutableArray<GenTree> statements)
        {
            if (statements.IsDefaultOrEmpty)
                return ImmutableArray<ImmutableArray<GenTree>>.Empty;

            var builder = ImmutableArray.CreateBuilder<ImmutableArray<GenTree>>(statements.Length);
            for (int i = 0; i < statements.Length; i++)
                builder.Add(BuildStatement(statements[i]));
            return builder.ToImmutable();
        }

        public static ImmutableArray<GenTree> Flatten(ImmutableArray<ImmutableArray<GenTree>> statementTreeLists)
        {
            if (statementTreeLists.IsDefaultOrEmpty)
                return ImmutableArray<GenTree>.Empty;

            int count = 0;
            for (int i = 0; i < statementTreeLists.Length; i++)
                count += statementTreeLists[i].Length;

            var builder = ImmutableArray.CreateBuilder<GenTree>(count);
            for (int i = 0; i < statementTreeLists.Length; i++)
                builder.AddRange(statementTreeLists[i]);
            return builder.ToImmutable();
        }

        public static ImmutableArray<GenTree> BuildBlock(ImmutableArray<GenTree> statements)
            => Flatten(BuildStatements(statements));

        private static void CollectUnique(GenTree node, HashSet<GenTree> seen, ImmutableArray<GenTree>.Builder builder)
        {
            if (!seen.Add(node))
                return;

            builder.Add(node);
            for (int i = 0; i < node.Operands.Length; i++)
                CollectUnique(node.Operands[i], seen, builder);
        }

        private static void ValidateStatementTreeList(GenTree root, ImmutableArray<GenTree> treeList)
        {
            if (treeList.IsDefaultOrEmpty)
                throw new InvalidOperationException($"Statement tree-list is empty for root {root.Id}.");

            if (!ReferenceEquals(treeList[treeList.Length - 1], root))
                throw new InvalidOperationException($"Statement tree-list root is not last in execution order for root {root.Id}.");

            var ordinalByNode = new Dictionary<GenTree, int>(ReferenceEqualityComparer<GenTree>.Instance);
            for (int i = 0; i < treeList.Length; i++)
            {
                if (!ordinalByNode.TryAdd(treeList[i], i))
                    throw new InvalidOperationException($"Statement tree-list contains duplicate node {treeList[i].Id}.");
            }

            for (int i = 0; i < treeList.Length; i++)
            {
                var node = treeList[i];
                for (int op = 0; op < node.Operands.Length; op++)
                {
                    var operand = node.Operands[op];
                    if (!ordinalByNode.TryGetValue(operand, out int operandOrdinal))
                        throw new InvalidOperationException($"Statement tree-list for root {root.Id} does not contain operand {operand.Id} of node {node.Id}.");
                }
            }
        }
    }

    internal sealed class GenTreeBlock
    {
        public int Id { get; }
        public int StartPc { get; }
        public int EndPcExclusive { get; }
        public int EntryStackDepth { get; }
        public int ExitStackDepth { get; }
        public GenTreeBlockJumpKind JumpKind { get; }
        public GenTreeBlockFlags Flags { get; }
        public ImmutableArray<GenTree> Statements { get; }
        public ImmutableArray<ImmutableArray<GenTree>> StatementTreeLists { get; }
        public ImmutableArray<GenTree> LinearNodes { get; private set; }
        public GenTree? FirstNode { get; private set; }
        public GenTree? LastNode { get; private set; }
        public ImmutableArray<int> SuccessorBlockIds { get; }
        public ImmutableArray<int> SuccessorPcs { get; }

        public GenTreeBlock(
            int id,
            int startPc,
            int endPcExclusive,
            int entryStackDepth,
            int exitStackDepth,
            GenTreeBlockJumpKind jumpKind,
            GenTreeBlockFlags flags,
            ImmutableArray<GenTree> statements,
            ImmutableArray<int> successorBlockIds,
            ImmutableArray<int> successorPcs)
        {
            Id = id;
            StartPc = startPc;
            EndPcExclusive = endPcExclusive;
            EntryStackDepth = entryStackDepth;
            ExitStackDepth = exitStackDepth;
            JumpKind = jumpKind;
            Flags = flags;
            Statements = statements.IsDefault ? ImmutableArray<GenTree>.Empty : statements;
            StatementTreeLists = GenTreeTreeOrder.BuildStatements(Statements);
            SetLinearNodes(GenTreeTreeOrder.Flatten(StatementTreeLists));
            SuccessorBlockIds = successorBlockIds.IsDefault ? ImmutableArray<int>.Empty : successorBlockIds;
            SuccessorPcs = successorPcs.IsDefault ? ImmutableArray<int>.Empty : successorPcs;
        }

        internal void SetLinearNodes(ImmutableArray<GenTree> nodes)
        {
            LinearNodes = nodes.IsDefault ? ImmutableArray<GenTree>.Empty : nodes;
            for (int i = 0; i < LinearNodes.Length; i++)
            {
                var node = LinearNodes[i];
                node.Previous = i == 0 ? null : LinearNodes[i - 1];
                node.Next = i + 1 == LinearNodes.Length ? null : LinearNodes[i + 1];
                node.LinearBlockId = Id;
                node.LinearOrdinal = i;
            }

            FirstNode = LinearNodes.Length == 0 ? null : LinearNodes[0];
            LastNode = LinearNodes.Length == 0 ? null : LinearNodes[LinearNodes.Length - 1];
        }
    }

    internal sealed class GenTreeMethod
    {
        public RuntimeModule Module { get; }
        public RuntimeMethod RuntimeMethod { get; }
        public BytecodeFunction Function { get; }
        public ImmutableArray<RuntimeType> ArgTypes { get; }
        public ImmutableArray<RuntimeType> LocalTypes { get; }
        public ImmutableArray<GenTemp> Temps { get; }
        public ImmutableArray<GenLocalDescriptor> ArgDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> LocalDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> TempDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> AllLocalDescriptors { get; private set; }
        public ImmutableArray<GenTreeBlock> Blocks { get; }
        public ImmutableArray<RuntimeMethod> DirectDependencies { get; }
        public ImmutableArray<RuntimeMethod> VirtualDependencies { get; }

        private ControlFlowGraph? _cfg;
        private SsaMethod? _ssa;
        private GenTreeLocalLiveness? _hirLiveness;
        private IReadOnlyDictionary<GenTreeValueKey, GenTreeValueInfo> _valueInfoByNode = new Dictionary<GenTreeValueKey, GenTreeValueInfo>();
        private IReadOnlyDictionary<GenTree, GenTreeValueInfo> _valueInfoByRepresentativeNode = new Dictionary<GenTree, GenTreeValueInfo>();
        private IReadOnlyDictionary<GenTree, LinearLiveInterval> _liveIntervalByNode = new Dictionary<GenTree, LinearLiveInterval>();

        public GenTreeMethodPhase Phase { get; private set; } = GenTreeMethodPhase.ImportedHir;
        public ControlFlowGraph Cfg => _cfg ?? throw new InvalidOperationException("GenTree method has no control-flow graph attached.");
        public SsaMethod? Ssa => _ssa;
        public GenTreeLocalLiveness? HirLiveness => _hirLiveness;
        public ImmutableArray<GenTree> LinearNodes { get; private set; } = ImmutableArray<GenTree>.Empty;
        public ImmutableArray<int> LinearBlockOrder { get; private set; } = ImmutableArray<int>.Empty;
        public ImmutableArray<GenTreeValueInfo> Values { get; private set; } = ImmutableArray<GenTreeValueInfo>.Empty;
        public IReadOnlyDictionary<GenTreeValueKey, GenTreeValueInfo> ValueInfoByNode => _valueInfoByNode;
        public ImmutableArray<LinearLiveInterval> LiveIntervals { get; private set; } = ImmutableArray<LinearLiveInterval>.Empty;
        public IReadOnlyDictionary<GenTree, LinearLiveInterval> LiveIntervalByNode => _liveIntervalByNode;
        public ImmutableArray<LinearRefPosition> RefPositions { get; private set; } = ImmutableArray<LinearRefPosition>.Empty;
        public ImmutableArray<RegisterAllocationInfo> RegisterAllocations { get; private set; } = ImmutableArray<RegisterAllocationInfo>.Empty;
        public IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> RegisterAllocationByValue { get; private set; } = new Dictionary<GenTreeValueKey, RegisterAllocationInfo>();
        public int SpillSlotCount { get; private set; }
        public int ParallelCopyScratchSpillSlot { get; private set; } = -1;
        public StackFrameLayout StackFrame { get; private set; } = StackFrameLayout.Empty;
        public bool HasPrologEpilog { get; private set; }
        public ImmutableArray<RegisterUnwindCode> UnwindCodes { get; private set; } = ImmutableArray<RegisterUnwindCode>.Empty;
        public ImmutableArray<RegisterGcLiveRange> GcLiveRanges { get; private set; } = ImmutableArray<RegisterGcLiveRange>.Empty;
        public ImmutableArray<RegisterGcTransition> GcTransitions { get; private set; } = ImmutableArray<RegisterGcTransition>.Empty;
        public ImmutableArray<RegisterGcInterruptibleRange> GcInterruptibleRanges { get; private set; } = ImmutableArray<RegisterGcInterruptibleRange>.Empty;
        public ImmutableArray<RegisterFunclet> Funclets { get; private set; } = ImmutableArray<RegisterFunclet>.Empty;
        public ImmutableArray<RegisterFrameRegion> FrameRegions { get; private set; } = ImmutableArray<RegisterFrameRegion>.Empty;
        public bool GcReportOnlyLeafFunclet { get; private set; }

        public GenTreeMethod(
            RuntimeModule module,
            RuntimeMethod runtimeMethod,
            BytecodeFunction function,
            ImmutableArray<RuntimeType> argTypes,
            ImmutableArray<RuntimeType> localTypes,
            ImmutableArray<GenTemp> temps,
            ImmutableArray<GenTreeBlock> blocks,
            ImmutableArray<RuntimeMethod> directDependencies,
            ImmutableArray<RuntimeMethod> virtualDependencies)
        {
            Module = module;
            RuntimeMethod = runtimeMethod;
            Function = function;
            ArgTypes = argTypes.IsDefault ? ImmutableArray<RuntimeType>.Empty : argTypes;
            LocalTypes = localTypes.IsDefault ? ImmutableArray<RuntimeType>.Empty : localTypes;
            Temps = temps.IsDefault ? ImmutableArray<GenTemp>.Empty : temps;
            ArgDescriptors = BuildArgDescriptors(ArgTypes);
            LocalDescriptors = BuildLocalDescriptors(LocalTypes, ArgDescriptors.Length);
            MarkAddressExposedFromBytecode(Function, ArgDescriptors, LocalDescriptors);
            TempDescriptors = BuildTempDescriptors(Temps, ArgDescriptors.Length + LocalDescriptors.Length);
            AllLocalDescriptors = BuildAllLocalDescriptors(ArgDescriptors, LocalDescriptors, TempDescriptors);
            AttachLocalDescriptorsToTrees(blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks);
            Blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;
            DirectDependencies = directDependencies.IsDefault ? ImmutableArray<RuntimeMethod>.Empty : directDependencies;
            VirtualDependencies = virtualDependencies.IsDefault ? ImmutableArray<RuntimeMethod>.Empty : virtualDependencies;
        }


        internal GenTreeMethod CloneWithBlocks(ImmutableArray<GenTreeBlock> blocks)
        {
            blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;

            var clone = new GenTreeMethod(
                Module,
                RuntimeMethod,
                Function,
                ArgTypes,
                LocalTypes,
                Temps,
                blocks,
                DirectDependencies,
                VirtualDependencies);

            clone.ArgDescriptors = ArgDescriptors;
            clone.LocalDescriptors = LocalDescriptors;
            clone.TempDescriptors = TempDescriptors;
            clone.AllLocalDescriptors = AllLocalDescriptors;
            clone.AttachLocalDescriptorsToTrees(blocks);
            return clone;
        }
        private static void MarkAddressExposedFromBytecode(
            BytecodeFunction function,
            ImmutableArray<GenLocalDescriptor> args,
            ImmutableArray<GenLocalDescriptor> locals)
        {
            var instructions = function.Instructions;
            for (int i = 0; i < instructions.Length; i++)
            {
                var ins = instructions[i];
                if (ins.Op == BytecodeOp.Ldarga)
                {
                    if ((uint)ins.Operand0 < (uint)args.Length && GenTreeMethodBuilder.AddressUseMayEscape(instructions, i))
                        args[ins.Operand0].MarkAddressExposed();
                }
                else if (ins.Op == BytecodeOp.Ldloca)
                {
                    if ((uint)ins.Operand0 < (uint)locals.Length && GenTreeMethodBuilder.AddressUseMayEscape(instructions, i))
                        locals[ins.Operand0].MarkAddressExposed();
                }
            }
        }


        private static ImmutableArray<GenLocalDescriptor> BuildArgDescriptors(ImmutableArray<RuntimeType> args)
        {
            var builder = ImmutableArray.CreateBuilder<GenLocalDescriptor>(args.Length);
            for (int i = 0; i < args.Length; i++)
            {
                var stackKind = StackKindForDescriptor(args[i]);
                var category = IsRefLikeDescriptorType(args[i])
                    ? GenLocalCategory.ImplicitByRefPinnedRefLikeLocal
                    : GenLocalCategory.Unclassified;
                builder.Add(new GenLocalDescriptor(i, GenLocalKind.Argument, i, args[i], stackKind, category));
            }
            return builder.ToImmutable();
        }

        private static ImmutableArray<GenLocalDescriptor> BuildLocalDescriptors(ImmutableArray<RuntimeType> locals, int lclNumBase)
        {
            var builder = ImmutableArray.CreateBuilder<GenLocalDescriptor>(locals.Length);
            for (int i = 0; i < locals.Length; i++)
            {
                var stackKind = StackKindForDescriptor(locals[i]);
                var category = IsRefLikeDescriptorType(locals[i])
                    ? GenLocalCategory.ImplicitByRefPinnedRefLikeLocal
                    : GenLocalCategory.Unclassified;
                builder.Add(new GenLocalDescriptor(lclNumBase + i, GenLocalKind.Local, i, locals[i], stackKind, category));
            }
            return builder.ToImmutable();
        }

        private static ImmutableArray<GenLocalDescriptor> BuildTempDescriptors(ImmutableArray<GenTemp> temps, int lclNumBase)
        {
            var builder = ImmutableArray.CreateBuilder<GenLocalDescriptor>(temps.Length);
            for (int i = 0; i < temps.Length; i++)
            {
                var temp = temps[i];
                bool isStructMaterialization = temp.Kind == GenTempKind.StructMaterialization;
                bool canPromoteTemp = temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineArg or GenTempKind.InlineLocal or GenTempKind.InlineReturn;
                bool promoteParent = canPromoteTemp && LclVarDsc.IsPromotableStructMaterializationType(temp.Type);
                var category = promoteParent ? GenLocalCategory.PromotedStruct : GenLocalCategory.CompilerTemp;
                var descriptor = new GenLocalDescriptor(lclNumBase + i, GenLocalKind.Temporary, temp.Index, temp.Type, temp.StackKind, category);

                if (isStructMaterialization)
                    descriptor.IsStructMaterializationTemp = true;

                if (promoteParent)
                {
                    descriptor.IsCompilerTemp = false;
                    descriptor.MarkPromotedStructParent();
                }
                else
                {
                    descriptor.IsCompilerTemp = true;
                    descriptor.DoNotEnregister = true;
                }

                builder.Add(descriptor);
            }
            return builder.ToImmutable();
        }

        private static ImmutableArray<GenLocalDescriptor> BuildAllLocalDescriptors(
            ImmutableArray<GenLocalDescriptor> args,
            ImmutableArray<GenLocalDescriptor> locals,
            ImmutableArray<GenLocalDescriptor> temps)
        {
            var builder = ImmutableArray.CreateBuilder<GenLocalDescriptor>(args.Length + locals.Length + temps.Length);
            builder.AddRange(args);
            builder.AddRange(locals);
            builder.AddRange(temps);
            builder.Sort(static (left, right) => left.LclNum.CompareTo(right.LclNum));
            for (int i = 0; i < builder.Count; i++)
            {
                if (builder[i].LclNum != i)
                    throw new InvalidOperationException("LclVarDsc table must be dense by lclNum.");
            }
            return builder.ToImmutable();
        }

        private static bool IsRefLikeDescriptorType(RuntimeType? type)
        {
            if (type is null)
                return false;

            return type.Name == "Span`1" ||
                   type.Name == "ReadOnlySpan`1" ||
                   type.Name == "Span" ||
                   type.Name == "ReadOnlySpan";
        }

        private static GenStackKind StackKindForDescriptor(RuntimeType type)
        {
            if (type.IsReferenceType) return GenStackKind.Ref;
            if (type.Kind == RuntimeTypeKind.ByRef) return GenStackKind.ByRef;
            if (type.Kind == RuntimeTypeKind.Pointer) return GenStackKind.Ptr;
            if (type.Name == "Single") return GenStackKind.R4;
            if (type.Name == "Double") return GenStackKind.R8;
            if (type.SizeOf <= 4) return GenStackKind.I4;
            if (type.SizeOf <= 8) return GenStackKind.I8;
            return GenStackKind.Value;
        }

        internal void EnsurePromotedStructFieldLocals()
        {
            if (AllLocalDescriptors.IsDefaultOrEmpty)
                return;

            var args = ArgDescriptors.ToBuilder();
            var locals = LocalDescriptors.ToBuilder();
            var temps = TempDescriptors.ToBuilder();
            int nextLclNum = AllLocalDescriptors.Length;
            bool changed = false;

            PromoteFrom(args, GenLocalKind.Argument, ref nextLclNum, ref changed);
            PromoteFrom(locals, GenLocalKind.Local, ref nextLclNum, ref changed);
            PromoteFrom(temps, GenLocalKind.Temporary, ref nextLclNum, ref changed);

            if (!changed)
                return;

            ArgDescriptors = args.ToImmutable();
            LocalDescriptors = locals.ToImmutable();
            TempDescriptors = temps.ToImmutable();
            AllLocalDescriptors = BuildAllLocalDescriptors(ArgDescriptors, LocalDescriptors, TempDescriptors);

            static void PromoteFrom(ImmutableArray<GenLocalDescriptor>.Builder descriptors, GenLocalKind kind, ref int nextLclNum, ref bool changed)
            {
                int originalCount = descriptors.Count;
                for (int i = 0; i < originalCount; i++)
                {
                    var parent = descriptors[i];
                    if (parent.Category != GenLocalCategory.PromotedStruct)
                        continue;
                    if (parent.AddressExposed || parent.MemoryAliased)
                        continue;
                    if (parent.HasPromotedStructFields)
                        continue;
                    if (!CanPromoteStruct(parent.Type))
                        continue;

                    var fields = parent.Type!.InstanceFields;
                    if (!HasNonOverlappingFields(fields))
                        continue;

                    for (int f = 0; f < fields.Length; f++)
                    {
                        var field = fields[f];
                        if (!CanPromoteField(field))
                            continue;

                        if (!TryFindExistingPromotedFieldDescriptor(descriptors, parent, field, out var fieldDescriptor))
                        {
                            var stackKind = StackKindForDescriptor(field.FieldType);
                            int fieldIndex = descriptors.Count;
                            fieldDescriptor = new GenLocalDescriptor(
                                nextLclNum++,
                                kind,
                                fieldIndex,
                                field.FieldType,
                                stackKind,
                                GenLocalCategory.PromotedStructField);

                            fieldDescriptor.MarkPromotedStructField(
                                parent.LclNum,
                                ordinal: f,
                                offset: field.Offset,
                                size: Math.Max(1, field.FieldType.SizeOf),
                                field: field);

                            descriptors.Add(fieldDescriptor);
                            changed = true;
                        }

                        parent.AddPromotedField(field, fieldDescriptor);
                    }
                }
            }

            static bool TryFindExistingPromotedFieldDescriptor(
                ImmutableArray<GenLocalDescriptor>.Builder descriptors,
                GenLocalDescriptor parent,
                RuntimeField field,
                out GenLocalDescriptor descriptor)
            {
                for (int i = 0; i < descriptors.Count; i++)
                {
                    var candidate = descriptors[i];
                    if (!candidate.IsStructField)
                        continue;
                    if (candidate.ParentLclNum != parent.LclNum)
                        continue;
                    if (!ReferenceEquals(candidate.PromotedField, field))
                        continue;
                    if (candidate.FieldOffset != field.Offset)
                        throw new InvalidOperationException("Promoted field descriptor has stale field offset.");
                    if (candidate.FieldSize != Math.Max(1, field.FieldType.SizeOf))
                        throw new InvalidOperationException("Promoted field descriptor has stale field size.");
                    descriptor = candidate;
                    return true;
                }

                descriptor = null!;
                return false;
            }

            static bool CanPromoteStruct(RuntimeType? type)
            {
                if (type is null || !type.IsValueType || type.Kind != RuntimeTypeKind.Struct)
                    return false;
                if (type.InstanceFields.Length == 0)
                    return false;
                return true;
            }

            static bool CanPromoteField(RuntimeField field)
            {
                if (field.IsStatic)
                    return false;
                var stackKind = StackKindForDescriptor(field.FieldType);
                if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                    return false;
                if (field.FieldType.IsValueType && field.FieldType.ContainsGcPointers)
                    return false;
                return MachineAbi.IsPhysicallyPromotableStorage(field.FieldType, stackKind) ||
                       stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Ptr or GenStackKind.NativeInt or GenStackKind.NativeUInt;
            }

            static bool HasNonOverlappingFields(RuntimeField[] fields)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    int iStart = fields[i].Offset;
                    int iEnd = iStart + Math.Max(1, fields[i].FieldType.SizeOf);
                    for (int j = i + 1; j < fields.Length; j++)
                    {
                        int jStart = fields[j].Offset;
                        int jEnd = jStart + Math.Max(1, fields[j].FieldType.SizeOf);
                        if (iStart < jEnd && jStart < iEnd)
                            return false;
                    }
                }
                return true;
            }
        }

        private void AttachLocalDescriptorsToTrees(ImmutableArray<GenTreeBlock> blocks)
        {
            var tempByIndex = BuildTempDescriptorIndex(TempDescriptors);

            for (int b = 0; b < blocks.Length; b++)
            {
                var statements = blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Attach(statements[s]);
            }

            void Attach(GenTree node)
            {
                switch (node.Kind)
                {
                    case GenTreeKind.Arg:
                    case GenTreeKind.ArgAddr:
                    case GenTreeKind.StoreArg:
                        if ((uint)node.Int32 < (uint)ArgDescriptors.Length)
                            AttachDescriptor(node, ArgDescriptors[node.Int32]);
                        break;

                    case GenTreeKind.Local:
                    case GenTreeKind.LocalAddr:
                    case GenTreeKind.StoreLocal:
                        if ((uint)node.Int32 < (uint)LocalDescriptors.Length)
                            AttachDescriptor(node, LocalDescriptors[node.Int32]);
                        break;

                    case GenTreeKind.Temp:
                    case GenTreeKind.TempAddr:
                    case GenTreeKind.StoreTemp:
                        if ((uint)node.Int32 < (uint)tempByIndex.Length)
                        {
                            var descriptor = tempByIndex[node.Int32];
                            if (descriptor is not null)
                                AttachDescriptor(node, descriptor);
                        }
                        break;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    Attach(node.Operands[i]);
            }

            static void AttachDescriptor(GenTree node, GenLocalDescriptor descriptor)
            {
                node.LocalDescriptor = descriptor;
                node.ValueKey = GenTreeValueKey.ForTree(node);
            }

            static GenLocalDescriptor?[] BuildTempDescriptorIndex(ImmutableArray<GenLocalDescriptor> descriptors)
            {
                if (descriptors.IsDefaultOrEmpty)
                    return Array.Empty<GenLocalDescriptor?>();

                int max = -1;
                for (int i = 0; i < descriptors.Length; i++)
                {
                    if (descriptors[i].Index > max)
                        max = descriptors[i].Index;
                }

                var result = new GenLocalDescriptor?[max + 1];
                for (int i = 0; i < descriptors.Length; i++)
                    result[descriptors[i].Index] = descriptors[i];

                return result;
            }
        }

        internal void SetPhase(GenTreeMethodPhase phase)
        {
            if (phase < Phase)
                throw new InvalidOperationException($"Cannot move GenTree method {RuntimeMethod} phase backwards from {Phase} to {phase}.");
            Phase = phase;
        }

        internal void AttachFlowGraph(ControlFlowGraph cfg, GenTreeMethodPhase phase = GenTreeMethodPhase.FlowgraphBuilt)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            SetPhase(phase);
        }

        internal void AttachHirLiveness(GenTreeLocalLiveness liveness)
        {
            _hirLiveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
            _cfg = liveness.Cfg;
            SetPhase(GenTreeMethodPhase.HirLiveness);
        }

        internal void AttachSsa(SsaMethod ssa, bool optimized)
        {
            _ssa = ssa ?? throw new ArgumentNullException(nameof(ssa));
            _cfg = ssa.Cfg;
            SetPhase(optimized ? GenTreeMethodPhase.SsaOptimized : GenTreeMethodPhase.Ssa);
        }

        internal void AttachLinearBackendState(
            ControlFlowGraph cfg,
            ImmutableArray<GenTree> linearNodes,
            ImmutableArray<GenTreeValueInfo> values,
            IReadOnlyDictionary<GenTreeValueKey, GenTreeValueInfo> valueInfoByNode,
            ImmutableArray<int> linearBlockOrder,
            SsaMethod? ssa = null)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _ssa = ssa;
            LinearNodes = linearNodes.IsDefault ? ImmutableArray<GenTree>.Empty : linearNodes;
            Values = values.IsDefault ? ImmutableArray<GenTreeValueInfo>.Empty : values;
            _valueInfoByNode = valueInfoByNode ?? throw new ArgumentNullException(nameof(valueInfoByNode));
            var representativeMap = new Dictionary<GenTree, GenTreeValueInfo>();
            for (int i = 0; i < Values.Length; i++)
                representativeMap[Values[i].RepresentativeNode] = Values[i];
            _valueInfoByRepresentativeNode = representativeMap;
            LinearBlockOrder = Cnidaria.Cs.LinearBlockOrder.Normalize(Cfg, linearBlockOrder);
            LiveIntervals = ImmutableArray<LinearLiveInterval>.Empty;
            _liveIntervalByNode = new Dictionary<GenTree, LinearLiveInterval>();
            RefPositions = ImmutableArray<LinearRefPosition>.Empty;
            SetPhase(GenTreeMethodPhase.RationalizedLir);
        }

        internal void AttachLiveness(
            ImmutableArray<LinearLiveInterval> liveIntervals,
            IReadOnlyDictionary<GenTree, LinearLiveInterval> liveIntervalByNode,
            ImmutableArray<LinearRefPosition> refPositions)
        {
            if (_cfg is null)
                throw new InvalidOperationException("GenTree method has no linear backend state.");

            LiveIntervals = liveIntervals.IsDefault ? ImmutableArray<LinearLiveInterval>.Empty : liveIntervals;
            _liveIntervalByNode = liveIntervalByNode ?? throw new ArgumentNullException(nameof(liveIntervalByNode));
            RefPositions = refPositions.IsDefault ? ImmutableArray<LinearRefPosition>.Empty : refPositions;
            SetPhase(GenTreeMethodPhase.LoweredLir);
        }

        internal void AttachLsraFinalState(
            ImmutableArray<RegisterAllocationInfo> allocations,
            IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> allocationByValue,
            int spillSlotCount,
            int parallelCopyScratchSpillSlot,
            StackFrameLayout stackFrame,
            bool hasPrologEpilog,
            ImmutableArray<RegisterUnwindCode> unwindCodes,
            ImmutableArray<RegisterGcLiveRange> gcLiveRanges,
            ImmutableArray<RegisterGcTransition> gcTransitions,
            ImmutableArray<RegisterGcInterruptibleRange> gcInterruptibleRanges,
            ImmutableArray<RegisterFunclet> funclets,
            ImmutableArray<RegisterFrameRegion> frameRegions,
            bool gcReportOnlyLeafFunclet)
        {
            RegisterAllocations = allocations.IsDefault ? ImmutableArray<RegisterAllocationInfo>.Empty : allocations;
            RegisterAllocationByValue = allocationByValue ?? throw new ArgumentNullException(nameof(allocationByValue));
            SpillSlotCount = spillSlotCount;
            ParallelCopyScratchSpillSlot = parallelCopyScratchSpillSlot;
            StackFrame = stackFrame;
            HasPrologEpilog = hasPrologEpilog;
            UnwindCodes = unwindCodes.IsDefault ? ImmutableArray<RegisterUnwindCode>.Empty : unwindCodes;
            GcLiveRanges = gcLiveRanges.IsDefault ? ImmutableArray<RegisterGcLiveRange>.Empty : gcLiveRanges;
            GcTransitions = gcTransitions.IsDefault ? ImmutableArray<RegisterGcTransition>.Empty : gcTransitions;
            GcInterruptibleRanges = gcInterruptibleRanges.IsDefault ? ImmutableArray<RegisterGcInterruptibleRange>.Empty : gcInterruptibleRanges;
            Funclets = funclets.IsDefault ? ImmutableArray<RegisterFunclet>.Empty : funclets;
            FrameRegions = frameRegions.IsDefault ? ImmutableArray<RegisterFrameRegion>.Empty : frameRegions;
            GcReportOnlyLeafFunclet = gcReportOnlyLeafFunclet;
            SetPhase(GenTreeMethodPhase.RegisterAllocated);
        }

        public RegisterOperand GetHome(GenTree value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (RegisterAllocationByValue.TryGetValue(value.LinearValueKey, out var allocation))
                return allocation.Home;

            if (value.RegisterAllocation is not null)
                return value.RegisterHome;

            throw new InvalidOperationException($"No LSRA home is attached to GenTree value {value}.");
        }

        public RegisterValueLocation GetValueLocation(GenTree value, int position, bool isReturn = false)
            => value.GetRegisterLocation(position, isReturn);

        public RegisterValueLocation GetValueLocationAtDefinition(GenTree value, bool isReturn = false)
        {
            if (isReturn)
                return value.GetRegisterLocation(value.RegisterAllocation?.DefinitionPosition ?? 0, isReturn: true);
            if (value.RegisterAllocation is null)
                throw new InvalidOperationException($"No LSRA home is attached to GenTree value {value}.");
            return value.RegisterLocationAtDefinition;
        }

        public GenTreeValueInfo GetValueInfo(GenTree value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (_valueInfoByNode.TryGetValue(value.LinearValueKey, out var info))
                return info;
            if (_valueInfoByRepresentativeNode.TryGetValue(value, out info))
                return info;

            throw new InvalidOperationException($"Unknown linear GenTree value {value.LinearValueKey}.");
        }

        public GenTreeValueInfo GetValueInfo(GenTreeValueKey value)
        {
            if (!_valueInfoByNode.TryGetValue(value, out var info))
                throw new InvalidOperationException($"Unknown linear GenTree value {value}.");
            return info;
        }
    }

    internal sealed class GenTreeProgram
    {
        public ImmutableArray<GenTreeMethod> Methods { get; }
        public IReadOnlyDictionary<int, GenTreeMethod> MethodsByRuntimeMethodId { get; }
        public RuntimeTypeSystem? TypeSystem { get; }

        public GenTreeProgram(ImmutableArray<GenTreeMethod> methods)
            : this(null, methods)
        {
        }

        public GenTreeProgram(RuntimeTypeSystem? typeSystem, ImmutableArray<GenTreeMethod> methods)
        {
            TypeSystem = typeSystem;
            Methods = methods.IsDefault ? ImmutableArray<GenTreeMethod>.Empty : methods;
            var map = new Dictionary<int, GenTreeMethod>();
            foreach (var m in Methods)
                map[m.RuntimeMethod.MethodId] = m;
            MethodsByRuntimeMethodId = map;
        }
    }

    internal sealed class GenTreeBuildException : Exception
    {
        public GenTreeBuildException(string message) : base(message) { }
        public GenTreeBuildException(string message, Exception innerException) : base(message, innerException) { }
    }

    internal sealed class GenTreeBuilder
    {
        private readonly IReadOnlyDictionary<string, RuntimeModule> _modules;
        private readonly RuntimeTypeSystem _rts;
        private readonly Dictionary<int, (RuntimeModule module, BytecodeFunction body, RuntimeMethod method)> _bodyByMethodId = new();
        private readonly Dictionary<int, GenTreeMethod> _built = new();
        private readonly Dictionary<int, ImmutableArray<RuntimeMethod>> _virtualTargetCache = new();
        private readonly Dictionary<int, RuntimeType> _liveInstantiatedTypes = new();
        private readonly HashSet<int> _scannedLiveConstructedGenericTypeIds = new();
        private readonly HashSet<int> _scannedLiveConstructedGenericCctorTypeIds = new();
        private int _liveInstantiatedTypeVersion;
        private int _virtualTargetScanVersion;
        public GenTreeBuilder(IReadOnlyDictionary<string, RuntimeModule> modules, RuntimeTypeSystem rts)
        {
            _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            _rts = rts ?? throw new ArgumentNullException(nameof(rts));
            IndexBodies();
        }

        public static GenTreeProgram BuildLinkedProgram(IReadOnlyDictionary<string, RuntimeModule> modules, RuntimeTypeSystem rts)
            => new GenTreeBuilder(modules, rts).BuildAllBodies();

        public static GenTreeProgram BuildReachableProgram(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeTypeSystem rts,
            RuntimeModule entryModule,
            int entryMethodToken)
            => new GenTreeBuilder(modules, rts).BuildReachable(entryModule, ImmutableArray.Create(entryMethodToken));

        public static GenTreeProgram BuildReachableProgram(
            IReadOnlyDictionary<string, RuntimeModule> modules,
            RuntimeTypeSystem rts,
            RuntimeModule entryModule,
            ImmutableArray<int> entryMethodTokens)
            => new GenTreeBuilder(modules, rts).BuildReachable(entryModule, entryMethodTokens);
        public GenTreeProgram BuildAllBodies()
        {
            foreach (var item in _bodyByMethodId.Values)
                BuildOne(item.module, item.body, item.method);

            return new GenTreeProgram(_rts, SortedBuiltMethods());
        }

        public GenTreeProgram BuildReachable(RuntimeModule entryModule, ImmutableArray<int> entryMethodTokens)
        {
            if (entryModule is null)
                throw new ArgumentNullException(nameof(entryModule));

            if (entryMethodTokens.IsDefaultOrEmpty)
                throw new ArgumentException("At least one entry method token is required.", nameof(entryMethodTokens));

            var queue = new Queue<RuntimeMethod>();
            var scheduledOrBuilt = new HashSet<int>();
            var virtualDependencies = new List<RuntimeMethod>();
            var virtualDependencyIds = new HashSet<int>();

            foreach (int entryMethodToken in entryMethodTokens)
            {
                RuntimeMethod entryMethod = _rts.ResolveMethodInMethodContext(
                    entryModule,
                    entryMethodToken,
                    methodContext: null);

                Enqueue(entryMethod);
            }

            for (; ; )
            {
                while (queue.Count != 0)
                {
                    RuntimeMethod scheduled = queue.Dequeue();

                    if (!TryGetBuildableBody(
                            scheduled,
                            out RuntimeModule bodyModule,
                            out BytecodeFunction body,
                            out RuntimeMethod buildMethod))
                    {
                        continue;
                    }

                    GenTreeMethod ir = BuildOne(bodyModule, body, buildMethod);

                    foreach (RuntimeMethod dep in ir.DirectDependencies)
                        Enqueue(dep);

                    foreach (RuntimeMethod declaredVirtual in ir.VirtualDependencies)
                    {
                        if (virtualDependencyIds.Add(declaredVirtual.MethodId))
                            virtualDependencies.Add(declaredVirtual);
                        var targets = GetVirtualTargets(declaredVirtual);
                        for (int t = 0; t < targets.Length; t++)
                            Enqueue(targets[t]);
                    }
                }
                bool added = EnqueueConstructedGenericBodiesDiscoveredDuringImport();

                if (!added)
                    break;
            }

            return new GenTreeProgram(_rts, SortedBuiltMethods());

            bool Enqueue(RuntimeMethod method)
            {
                if (method is null)
                    return false;

                if (!TryGetBuildableBody(method, out _, out _, out RuntimeMethod buildMethod))
                {
                    return false;
                }

                if (!scheduledOrBuilt.Add(buildMethod.MethodId))
                    return false;

                queue.Enqueue(buildMethod);
                return true;
            }

            bool TryGetBuildableBody(
                RuntimeMethod method,
                out RuntimeModule bodyModule,
                out BytecodeFunction body,
                out RuntimeMethod buildMethod)
            {
                buildMethod = method;

                if (method.BodyModule is not null && method.Body is not null)
                {
                    bodyModule = method.BodyModule;
                    body = method.Body;
                    return true;
                }

                if (_bodyByMethodId.TryGetValue(method.MethodId, out var indexedBody))
                {
                    bodyModule = indexedBody.module;
                    body = indexedBody.body;
                    buildMethod = indexedBody.method;
                    return true;
                }

                bodyModule = null!;
                body = null!;
                return false;
            }

            bool EnqueueConstructedGenericBodiesDiscoveredDuringImport()
            {
                bool added = false;

                foreach (RuntimeType type in _liveInstantiatedTypes.Values)
                {
                    if (type.GenericTypeDefinition is null)
                        continue;

                    if (_scannedLiveConstructedGenericTypeIds.Add(type.TypeId))
                        _rts.EnsureConstructedMembers(type);

                    if (_scannedLiveConstructedGenericCctorTypeIds.Add(type.TypeId))
                    {
                        RuntimeMethod? cctor = GenTreeMethodBuilder.FindTypeInitializer(type);
                        if (cctor is not null)
                            added |= Enqueue(cctor);
                    }
                }

                if (_virtualTargetScanVersion != _liveInstantiatedTypeVersion && virtualDependencies.Count != 0)
                {
                    _virtualTargetCache.Clear();
                    _virtualTargetScanVersion = _liveInstantiatedTypeVersion;

                    for (int i = 0; i < virtualDependencies.Count; i++)
                    {
                        RuntimeMethod declaredVirtual = virtualDependencies[i];
                        var targets = GetVirtualTargets(declaredVirtual);
                        for (int t = 0; t < targets.Length; t++)
                            added |= Enqueue(targets[t]);
                    }
                }

                return added;
            }
        }

        private ImmutableArray<GenTreeMethod> SortedBuiltMethods()
        {
            var list = new List<GenTreeMethod>(_built.Values);
            list.Sort(static (a, b) => a.RuntimeMethod.MethodId.CompareTo(b.RuntimeMethod.MethodId));
            return list.ToImmutableArray();
        }

        private void IndexBodies()
        {
            foreach (var module in _modules.Values)
            {
                foreach (var kv in module.MethodsByDefToken)
                {
                    var body = kv.Value;
                    RuntimeMethod method;
                    try
                    {
                        method = _rts.ResolveMethodInMethodContext(module, body.MethodToken, methodContext: null);
                    }
                    catch (Exception ex)
                    {
                        throw new GenTreeBuildException($"Cannot resolve body method {module.Name}:0x{body.MethodToken:X8}.", ex);
                    }

                    _bodyByMethodId[method.MethodId] = (module, body, method);
                }
            }
        }

        private GenTreeMethod BuildOne(RuntimeModule module, BytecodeFunction body, RuntimeMethod method)
        {
            if (_built.TryGetValue(method.MethodId, out var cached))
                return cached;

            var builder = new GenTreeMethodBuilder(_rts, module, body, method);
            var result = builder.Build();

            MarkLiveInstantiatedTypes(builder.InstantiatedTypes);

            _built.Add(method.MethodId, result);
            return result;
        }

        private void MarkLiveInstantiatedTypes(ImmutableArray<RuntimeType> types)
        {
            if (types.IsDefaultOrEmpty)
                return;

            for (int i = 0; i < types.Length; i++)
                MarkLiveInstantiatedType(types[i]);
        }

        private bool MarkLiveInstantiatedType(RuntimeType? type)
        {
            if (!CanHaveRuntimeInstance(type))
                return false;

            if (!_liveInstantiatedTypes.TryAdd(type!.TypeId, type))
                return false;

            _liveInstantiatedTypeVersion++;
            _virtualTargetCache.Clear();
            return true;
        }

        private static bool CanHaveRuntimeInstance(RuntimeType? type)
        {
            if (type is null)
                return false;

            return type.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.Array;
        }
        private ImmutableArray<RuntimeMethod> GetVirtualTargets(RuntimeMethod declared)
        {
            if (_virtualTargetCache.TryGetValue(declared.MethodId, out var cached))
                return cached;

            var result = ImmutableArray.CreateBuilder<RuntimeMethod>();
            var yielded = new HashSet<int>();

            foreach (var target in EnumerateVirtualTargetsCore(declared))
            {
                if (target.BodyModule is null || target.Body is null)
                    continue;

                if (yielded.Add(target.MethodId))
                    result.Add(target);
            }

            var frozen = result.ToImmutable();
            _virtualTargetCache[declared.MethodId] = frozen;
            return frozen;
        }
        private IEnumerable<RuntimeMethod> EnumerateVirtualTargetsCore(RuntimeMethod declared)
        {
            if (declared.BodyModule is not null && declared.Body is not null)
                yield return declared;

            foreach (RuntimeType runtimeType in _liveInstantiatedTypes.Values)
            {
                if (runtimeType.GenericTypeDefinition is not null)
                    _rts.EnsureConstructedMembers(runtimeType);

                if (!CanBeVirtualTarget(runtimeType, declared.DeclaringType))
                    continue;

                for (RuntimeType? owner = runtimeType; owner is not null; owner = owner.BaseType)
                {
                    if (owner.GenericTypeDefinition is not null)
                        _rts.EnsureConstructedMembers(owner);

                    if (declared.DeclaringType.Kind == RuntimeTypeKind.Interface)
                    {
                        foreach (RuntimeMethod explicitTarget in EnumerateExplicitInterfaceTargets(owner, declared))
                            yield return explicitTarget;
                    }

                    var methods = owner.Methods;
                    for (int m = 0; m < methods.Length; m++)
                    {
                        var candidate = methods[m];

                        if (IsVirtualTargetCandidate(candidate, declared))
                            yield return candidate;
                    }
                }
            }
        }
        private IEnumerable<RuntimeMethod> EnumerateExplicitInterfaceTargets(RuntimeType candidateOwner, RuntimeMethod declared)
        {
            var map = candidateOwner.ExplicitInterfaceMethodImpls;

            if (map is null || map.Count == 0)
                yield break;

            if (map.TryGetValue(declared.MethodId, out var exact))
            {
                yield return ProjectRuntimeMethodToOwner(candidateOwner, exact);
            }

            foreach (var kv in map)
            {
                RuntimeMethod ifaceMethod;

                try
                {
                    ifaceMethod = _rts.GetMethodById(kv.Key);
                }
                catch
                {
                    continue;
                }

                if (!SameInterfaceMethodIdentity(ifaceMethod, declared))
                    continue;

                yield return ProjectRuntimeMethodToOwner(candidateOwner, kv.Value);
            }
        }
        private static RuntimeMethod ProjectRuntimeMethodToOwner(RuntimeType candidateOwner, RuntimeMethod method)
        {
            if (method.DeclaringType.TypeId == candidateOwner.TypeId)
                return method;

            var methods = candidateOwner.Methods;

            for (int i = 0; i < methods.Length; i++)
            {
                var candidate = methods[i];

                if (!StringComparer.Ordinal.Equals(candidate.Name, method.Name))
                    continue;

                if (candidate.GenericArity != method.GenericArity)
                    continue;

                if (candidate.IsStatic != method.IsStatic)
                    continue;

                if (candidate.Body is not null &&
                    method.Body is not null &&
                    ReferenceEquals(candidate.Body, method.Body))
                {
                    return candidate;
                }

                if (SameSignature(candidate, method))
                    return candidate;
            }

            return method;
        }
        private static bool IsVirtualTargetCandidate(RuntimeMethod candidate, RuntimeMethod declared)
        {
            if (candidate.MethodId == declared.MethodId)
                return false;

            if (candidate.IsStatic)
                return false;

            if (!StringComparer.Ordinal.Equals(candidate.Name, declared.Name))
                return false;

            if (candidate.GenericArity != declared.GenericArity)
                return false;

            if (!SameSignature(candidate, declared))
                return false;

            if (!CanBeVirtualTarget(candidate.DeclaringType, declared.DeclaringType))
                return false;

            return true;
        }
        private static bool SameSignature(RuntimeMethod a, RuntimeMethod b)
        {
            if (!SameRuntimeType(a.ReturnType, b.ReturnType))
                return false;

            if (a.ParameterTypes.Length != b.ParameterTypes.Length)
                return false;

            for (int i = 0; i < a.ParameterTypes.Length; i++)
            {
                if (!SameRuntimeType(a.ParameterTypes[i], b.ParameterTypes[i]))
                    return false;
            }

            return true;
        }
        private static bool SameInterfaceMethodIdentity(RuntimeMethod ifaceMethod, RuntimeMethod declared)
        {
            if (!StringComparer.Ordinal.Equals(ifaceMethod.Name, declared.Name))
                return false;

            if (ifaceMethod.GenericArity != declared.GenericArity)
                return false;

            if (!SameRuntimeTypeDefinitionOrExact(ifaceMethod.DeclaringType, declared.DeclaringType))
                return false;

            if (ifaceMethod.ParameterTypes.Length != declared.ParameterTypes.Length)
                return false;

            if (!CompatibleInterfaceSignatureType(ifaceMethod.ReturnType, declared.ReturnType))
                return false;

            for (int i = 0; i < ifaceMethod.ParameterTypes.Length; i++)
            {
                if (!CompatibleInterfaceSignatureType(ifaceMethod.ParameterTypes[i], declared.ParameterTypes[i]))
                    return false;
            }

            return true;
        }

        private static bool SameRuntimeType(RuntimeType a, RuntimeType b)
            => a.TypeId == b.TypeId;

        private static bool SameRuntimeTypeDefinitionOrExact(RuntimeType a, RuntimeType b)
        {
            if (a.TypeId == b.TypeId)
                return true;

            RuntimeType ad = a.GenericTypeDefinition ?? a;
            RuntimeType bd = b.GenericTypeDefinition ?? b;

            return ad.TypeId == bd.TypeId;
        }
        private static bool CompatibleInterfaceSignatureType(RuntimeType a, RuntimeType b)
        {
            if (a.TypeId == b.TypeId)
                return true;

            if (a.Kind == RuntimeTypeKind.TypeParam || b.Kind == RuntimeTypeKind.TypeParam)
                return true;

            return SameRuntimeTypeDefinitionOrExact(a, b);
        }
        private static bool CanBeVirtualTarget(RuntimeType candidateOwner, RuntimeType declaredOwner)
        {
            if (ReferenceEquals(candidateOwner, declaredOwner))
                return true;

            if (declaredOwner.Kind == RuntimeTypeKind.Interface)
            {
                for (var t = candidateOwner; t is not null; t = t.BaseType)
                {
                    var interfaces = t.Interfaces;
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        if (SameInterfaceType(interfaces[i], declaredOwner))
                            return true;
                    }
                }
                return false;
            }

            for (var t = candidateOwner.BaseType; t is not null; t = t.BaseType)
            {
                if (ReferenceEquals(t, declaredOwner))
                    return true;
            }

            return false;
        }
        private static bool SameInterfaceType(RuntimeType implemented, RuntimeType declared)
        {
            if (implemented.TypeId == declared.TypeId)
                return true;

            RuntimeType? implementedDef = implemented.GenericTypeDefinition;
            RuntimeType? declaredDef = declared.GenericTypeDefinition;

            if (implementedDef is null || declaredDef is null)
                return false;

            if (implementedDef.TypeId != declaredDef.TypeId)
                return false;

            RuntimeType[] implementedArgs = implemented.GenericTypeArguments;
            RuntimeType[] declaredArgs = declared.GenericTypeArguments;

            if (implementedArgs.Length != declaredArgs.Length)
                return false;

            for (int i = 0; i < implementedArgs.Length; i++)
            {
                if (!CompatibleInterfaceSignatureType(implementedArgs[i], declaredArgs[i]))
                    return false;
            }

            return true;
        }
    }

    internal sealed class GenTreeMethodBuilder
    {
        private readonly RuntimeTypeSystem _rts;
        private readonly RuntimeModule _module;
        private readonly BytecodeFunction _body;
        private readonly RuntimeMethod _method;

        private readonly List<GenTemp> _temps = new();
        private readonly HashSet<int> _materializedImporterTempIds = new();
        private readonly HashSet<int> _structMaterializationTempIds = new();
        private readonly Dictionary<(int StartPc, int Depth), GenTemp> _stackEntryTemps = new();
        private readonly Dictionary<int, GenTemp> _dupTemps = new();
        private readonly HashSet<int> _createdDupTempIds = new();
        private readonly HashSet<int> _directDependencyIds = new();
        private readonly HashSet<int> _virtualDependencyIds = new();
        private readonly List<RuntimeMethod> _directDependencies = new();
        private readonly List<RuntimeMethod> _virtualDependencies = new();
        private readonly Dictionary<int, RuntimeType> _instantiatedTypes = new();
        private readonly HashSet<int> _activeInlineMethods = new();

        private bool[] _addressExposedArgs = Array.Empty<bool>();
        private bool[] _addressExposedLocals = Array.Empty<bool>();

        private const int InlineAlwaysBudget = 24;
        private const int InlineDiscretionaryBudget = 48;
        private const int InlineForceBudget = 128;
        private const int InlineSmallOverBudgetSize = 12;
        private const int InlineMaxDepth = 4;
        private const int InlineMaxForceDepth = 1;
        private const int InlineTotalBudget = 512;
        private int _inlineBudgetRemaining = InlineTotalBudget;

        private RuntimeType[] _argTypes = Array.Empty<RuntimeType>();
        private RuntimeType[] _localTypes = Array.Empty<RuntimeType>();
        private const int UnreachableStackDepth = -1;
        private int[] _stackDepthAtPc = Array.Empty<int>();
        private Dictionary<int, int> _pcToBlockId = new();
        private int _nextNodeId;
        private int _nextTempIndex;

        public ImmutableArray<RuntimeType> InstantiatedTypes
            => _instantiatedTypes.Count == 0
                ? ImmutableArray<RuntimeType>.Empty
                : _instantiatedTypes.Values.ToImmutableArray();

        public GenTreeMethodBuilder(
            RuntimeTypeSystem rts,
            RuntimeModule module,
            BytecodeFunction body,
            RuntimeMethod method)
        {
            _rts = rts;
            _module = module;
            _body = body;
            _method = method;
            _nextTempIndex = 0;
        }

        public GenTreeMethod Build()
        {
            _argTypes = BuildArgTypes(_method);
            _localTypes = BuildLocalTypes();
            ComputeImportAddressExposure();
            _stackDepthAtPc = ComputeStackDepths();

            var leaders = ComputeLeaders(_stackDepthAtPc);
            var blocks = BuildBlocks(leaders);

            return new GenTreeMethod(
                _module,
                _method,
                _body,
                _argTypes.ToImmutableArray(),
                _localTypes.ToImmutableArray(),
                _temps.ToImmutableArray(),
                blocks,
                _directDependencies.ToImmutableArray(),
                _virtualDependencies.ToImmutableArray());
        }

        private RuntimeType[] BuildArgTypes(RuntimeMethod method)
        {
            int count = method.HasThis ? method.ParameterTypes.Length + 1 : method.ParameterTypes.Length;
            var result = new RuntimeType[count];
            for (int i = 0; i < count; i++)
                result[i] = GetArgType(method, i);
            return result;
        }

        private RuntimeType[] BuildLocalTypes()
        {
            var result = new RuntimeType[_body.LocalTypeTokens.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = _rts.ResolveTypeInMethodContext(_module, _body.LocalTypeTokens[i], _method);
            return result;
        }
        private void ComputeImportAddressExposure()
        {
            _addressExposedArgs = new bool[_argTypes.Length];
            _addressExposedLocals = new bool[_localTypes.Length];

            var instructions = _body.Instructions;
            for (int i = 0; i < instructions.Length; i++)
            {
                var ins = instructions[i];
                if (ins.Op == BytecodeOp.Ldarga)
                {
                    if ((uint)ins.Operand0 < (uint)_addressExposedArgs.Length && AddressUseMayEscape(instructions, i))
                        _addressExposedArgs[ins.Operand0] = true;
                }
                else if (ins.Op == BytecodeOp.Ldloca)
                {
                    if ((uint)ins.Operand0 < (uint)_addressExposedLocals.Length && AddressUseMayEscape(instructions, i))
                        _addressExposedLocals[ins.Operand0] = true;
                }
            }
        }
        internal static bool AddressUseMayEscape(ImmutableArray<Instruction> instructions, int addressProducerIndex)
        {
            int positionFromTop = 0;
            for (int i = addressProducerIndex + 1; i < instructions.Length; i++)
            {
                var ins = instructions[i];
                int pop = Math.Max((short)0, ins.Pop);
                if (pop > positionFromTop)
                    return !IsNonEscapingLocalAddressConsumer(ins.Op);

                positionFromTop = positionFromTop - pop + Math.Max((short)0, ins.Push);

                if (ins.Op == BytecodeOp.Dup && positionFromTop == 1)
                    return true;

                if (MayInterruptLinearAddressUseScan(ins.Op))
                    return true;
            }

            return true;
        }
        internal static bool IsNonEscapingLocalAddressConsumer(BytecodeOp op)
            => op is BytecodeOp.Ldfld or BytecodeOp.Stfld;
        internal static bool MayInterruptLinearAddressUseScan(BytecodeOp op)
            => op is BytecodeOp.Br or BytecodeOp.Brtrue or BytecodeOp.Brfalse or
            BytecodeOp.Ret or BytecodeOp.Throw or BytecodeOp.Rethrow or BytecodeOp.Endfinally;
        private RuntimeType GetArgType(RuntimeMethod method, int argIndex)
        {
            if (method.HasThis)
            {
                if (argIndex == 0)
                {
                    if (method.DeclaringType.IsValueType)
                        return _rts.GetByRefType(method.DeclaringType);
                    return method.DeclaringType;
                }
                return method.ParameterTypes[argIndex - 1];
            }
            return method.ParameterTypes[argIndex];
        }

        private ImmutableArray<GenTreeBlock> BuildBlocks(List<int> leaders)
        {
            _pcToBlockId = new Dictionary<int, int>(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
                _pcToBlockId[leaders[i]] = i;

            var blocks = new List<GenTreeBlock>(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
            {
                int startPc = leaders[i];
                int hardEndPc = (i + 1 < leaders.Count) ? leaders[i + 1] : _body.Instructions.Length;
                blocks.Add(BuildBlock(i, startPc, hardEndPc));
            }
            return blocks.ToImmutableArray();
        }

        private GenTreeBlock BuildBlock(int blockId, int startPc, int hardEndPc)
        {
            var statements = new List<GenTree>();
            var stack = CreateEntryStack(startPc);
            int pc = startPc;
            var successorPcs = new List<int>(2);

            while (pc < hardEndPc)
            {
                var ins = _body.Instructions[pc];
                switch (ins.Op)
                {
                    case BytecodeOp.Nop:
                        break;

                    case BytecodeOp.Ldc_I4:
                        Push(stack, Node(GenTreeKind.ConstI4, pc, ins.Op, stackKind: GenStackKind.I4, int32: ins.Operand0));
                        break;

                    case BytecodeOp.Ldc_I8:
                        Push(stack, Node(GenTreeKind.ConstI8, pc, ins.Op, stackKind: GenStackKind.I8, int64: ins.Operand2));
                        break;

                    case BytecodeOp.Ldc_R4:
                        Push(stack, Node(GenTreeKind.ConstR4Bits, pc, ins.Op, stackKind: GenStackKind.R4, int32: ins.Operand0));
                        break;

                    case BytecodeOp.Ldc_R8:
                        Push(stack, Node(GenTreeKind.ConstR8Bits, pc, ins.Op, stackKind: GenStackKind.R8, int64: ins.Operand2));
                        break;

                    case BytecodeOp.Ldnull:
                        Push(stack, Node(GenTreeKind.ConstNull, pc, ins.Op, stackKind: GenStackKind.Null));
                        break;

                    case BytecodeOp.Ldstr:
                        MarkInstantiatedType(_rts.SystemString);
                        Push(stack, Node(GenTreeKind.ConstString, pc, ins.Op, type: _rts.SystemString, stackKind: GenStackKind.Ref,
                            int32: ins.Operand0, text: _module.Md.GetUserString(MetadataToken.Rid(ins.Operand0))));
                        break;

                    case BytecodeOp.DefaultValue:
                        {
                            var t = ResolveType(ins.Operand0);
                            if (t.IsValueType)
                                MarkInstantiatedType(t);
                            Push(stack, Node(GenTreeKind.DefaultValue, pc, ins.Op, type: t, stackKind: StackKindOf(t), runtimeType: t));
                            break;
                        }

                    case BytecodeOp.Sizeof:
                        {
                            var t = ResolveType(ins.Operand0);
                            Push(stack, Node(GenTreeKind.SizeOf, pc, ins.Op, stackKind: GenStackKind.I4, runtimeType: t));
                            break;
                        }

                    case BytecodeOp.TypeIsValueType:
                        {
                            var t = ResolveType(ins.Operand0);
                            Push(stack, Node(GenTreeKind.ConstI4, pc, ins.Op, stackKind: GenStackKind.I4,
                                int32: RuntimeTypeIsValueType(t, pc, ins.Op) ? 1 : 0));
                            break;
                        }

                    case BytecodeOp.Ldloc:
                        {
                            var t = CheckedLocalType(ins.Operand0, pc);
                            Push(stack, Node(GenTreeKind.Local, pc, ins.Op, type: t, stackKind: StackKindOf(t), int32: ins.Operand0));
                            break;
                        }

                    case BytecodeOp.Stloc:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var targetType = CheckedLocalType(ins.Operand0, pc);
                            AppendLocalLikeStore(statements, stack, pc, ins.Op, GenTreeKind.StoreLocal, GenTreeKind.LocalAddr, ins.Operand0, targetType, value.Node);
                            break;
                        }

                    case BytecodeOp.Ldloca:
                        {
                            var t = CheckedLocalType(ins.Operand0, pc);
                            var byRef = _rts.GetByRefType(t);
                            Push(stack, Node(GenTreeKind.LocalAddr, pc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef, int32: ins.Operand0));
                            break;
                        }

                    case BytecodeOp.Ldarg:
                        {
                            var t = CheckedArgType(ins.Operand0, pc);
                            Push(stack, Node(GenTreeKind.Arg, pc, ins.Op, type: t, stackKind: StackKindOf(t), int32: ins.Operand0));
                            break;
                        }

                    case BytecodeOp.Starg:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var targetType = CheckedArgType(ins.Operand0, pc);
                            AppendLocalLikeStore(statements, stack, pc, ins.Op, GenTreeKind.StoreArg, GenTreeKind.ArgAddr, ins.Operand0, targetType, value.Node);
                            break;
                        }

                    case BytecodeOp.Ldarga:
                        {
                            var t = CheckedArgType(ins.Operand0, pc);
                            var byRef = _rts.GetByRefType(t);
                            Push(stack, Node(GenTreeKind.ArgAddr, pc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef, int32: ins.Operand0));
                            break;
                        }

                    case BytecodeOp.Ldthis:
                        {
                            if (_argTypes.Length == 0)
                                throw Fail(pc, ins.Op, "ldthis in a method without implicit this.");
                            var t = _argTypes[0];
                            Push(stack, Node(GenTreeKind.Arg, pc, ins.Op, type: t, stackKind: StackKindOf(t), int32: 0));
                            break;
                        }

                    case BytecodeOp.Pop:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, ins.Op, operands: One(value.Node)));
                            break;
                        }

                    case BytecodeOp.Dup:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var temp = CreateDupTemp(value.Type, value.StackKind);
                            AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreTemp, pc, ins.Op, operands: One(value.Node), int32: temp.Index));
                            var load1 = TempLoad(pc, ins.Op, temp);
                            var load2 = TempLoad(pc, ins.Op, temp);
                            Push(stack, load1);
                            Push(stack, load2);
                            break;
                        }

                    case BytecodeOp.Neg:
                    case BytecodeOp.Not:
                    case BytecodeOp.PtrToByRef:
                    case BytecodeOp.CastClass:
                    case BytecodeOp.Isinst:
                    case BytecodeOp.Box:
                    case BytecodeOp.UnboxAny:
                        EmitUnary(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.Add:
                    case BytecodeOp.Add_Ovf:
                    case BytecodeOp.Add_Ovf_Un:
                    case BytecodeOp.Sub:
                    case BytecodeOp.Sub_Ovf:
                    case BytecodeOp.Sub_Ovf_Un:
                    case BytecodeOp.Mul:
                    case BytecodeOp.Mul_Ovf:
                    case BytecodeOp.Mul_Ovf_Un:
                    case BytecodeOp.Div:
                    case BytecodeOp.Div_Un:
                    case BytecodeOp.Rem:
                    case BytecodeOp.Rem_Un:
                    case BytecodeOp.And:
                    case BytecodeOp.Or:
                    case BytecodeOp.Xor:
                    case BytecodeOp.Shl:
                    case BytecodeOp.Shr:
                    case BytecodeOp.Shr_Un:
                    case BytecodeOp.Ceq:
                    case BytecodeOp.Clt:
                    case BytecodeOp.Clt_Un:
                    case BytecodeOp.Cgt:
                    case BytecodeOp.Cgt_Un:
                    case BytecodeOp.PtrElemAddr:
                    case BytecodeOp.PtrDiff:
                        EmitBinary(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.Conv:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var stackKind = StackKindOf((NumericConvKind)ins.Operand0);
                            PushImportedValue(stack, statements, Node(GenTreeKind.Conv, pc, ins.Op, stackKind: stackKind, operands: One(value.Node),
                                convKind: (NumericConvKind)ins.Operand0, convFlags: (NumericConvFlags)ins.Operand1));
                            break;
                        }

                    case BytecodeOp.Call:
                        EmitCall(stack, statements, pc, ins, isVirtual: false);
                        break;

                    case BytecodeOp.CallVirt:
                        EmitCall(stack, statements, pc, ins, isVirtual: true);
                        break;

                    case BytecodeOp.Newobj:
                        EmitNewObject(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.Ldfld:
                    case BytecodeOp.Ldflda:
                    case BytecodeOp.Stfld:
                    case BytecodeOp.Ldsfld:
                    case BytecodeOp.Ldsflda:
                    case BytecodeOp.Stsfld:
                        EmitField(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.Ldobj:
                        {
                            var address = Pop(stack, pc, ins.Op);
                            var t = ResolveType(ins.Operand0);
                            PushImportedValue(stack, statements, Node(GenTreeKind.LoadIndirect, pc, ins.Op, type: t, stackKind: StackKindOf(t), operands: One(address.Node), runtimeType: t));
                            break;
                        }

                    case BytecodeOp.Stobj:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var address = Pop(stack, pc, ins.Op);
                            var t = ResolveType(ins.Operand0);
                            if (!TryRetargetStructMaterializationToAddress(statements, pc, ins.Op, address.Node, t, value.Node))
                                AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreIndirect, pc, ins.Op, operands: Two(address.Node, value.Node), runtimeType: t));
                            break;
                        }

                    case BytecodeOp.Newarr:
                        {
                            var length = Pop(stack, pc, ins.Op);
                            var elemType = ResolveType(ins.Operand0);
                            var arrayType = _rts.GetArrayType(elemType);
                            MarkInstantiatedType(arrayType);
                            PushImportedValue(stack, statements, Node(GenTreeKind.NewArray, pc, ins.Op, type: arrayType, stackKind: GenStackKind.Ref,
                                operands: One(length.Node), runtimeType: elemType));
                            break;
                        }

                    case BytecodeOp.Ldelem:
                        {
                            var index = Pop(stack, pc, ins.Op);
                            var array = Pop(stack, pc, ins.Op);
                            var elemType = ResolveType(ins.Operand0);
                            PushImportedValue(stack, statements, Node(GenTreeKind.ArrayElement, pc, ins.Op, type: elemType, stackKind: StackKindOf(elemType),
                                operands: Two(array.Node, index.Node), runtimeType: elemType));
                            break;
                        }

                    case BytecodeOp.Ldelema:
                        {
                            var index = Pop(stack, pc, ins.Op);
                            var array = Pop(stack, pc, ins.Op);
                            var elemType = ResolveType(ins.Operand0);
                            var byRef = _rts.GetByRefType(elemType);
                            PushImportedValue(stack, statements, Node(GenTreeKind.ArrayElementAddr, pc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                                operands: Two(array.Node, index.Node), runtimeType: elemType));
                            break;
                        }

                    case BytecodeOp.Stelem:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            var index = Pop(stack, pc, ins.Op);
                            var array = Pop(stack, pc, ins.Op);
                            var elemType = ResolveType(ins.Operand0);
                            AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreArrayElement, pc, ins.Op,
                                operands: ImmutableArray.Create(array.Node, index.Node, value.Node), runtimeType: elemType));
                            break;
                        }

                    case BytecodeOp.LdArrayDataRef:
                        {
                            var array = Pop(stack, pc, ins.Op);
                            PushImportedValue(stack, statements, Node(GenTreeKind.ArrayDataRef, pc, ins.Op, stackKind: GenStackKind.ByRef, operands: One(array.Node)));
                            break;
                        }

                    case BytecodeOp.StaticData:
                        PushImportedValue(stack, statements, Node(GenTreeKind.StaticData, pc, ins.Op, stackKind: GenStackKind.Ptr, int32: ins.Operand0, int64: ins.Operand1));
                        break;

                    case BytecodeOp.StackAlloc:
                        {
                            var count = Pop(stack, pc, ins.Op);
                            PushImportedValue(stack, statements, Node(GenTreeKind.StackAlloc, pc, ins.Op, stackKind: GenStackKind.Ptr, operands: One(count.Node), int32: ins.Operand0));
                            break;
                        }

                    case BytecodeOp.AllocHGlobal:
                        {
                            var byteCount = Pop(stack, pc, ins.Op);
                            PushImportedValue(stack, statements, Node(GenTreeKind.AllocHGlobal, pc, ins.Op, stackKind: GenStackKind.Ptr, operands: One(byteCount.Node)));
                            break;
                        }

                    case BytecodeOp.FreeHGlobal:
                        {
                            var pointer = Pop(stack, pc, ins.Op);
                            AppendImporterStatement(statements, stack, Node(GenTreeKind.FreeHGlobal, pc, ins.Op, stackKind: GenStackKind.Void, operands: One(pointer.Node)));
                            break;
                        }

                    case BytecodeOp.NewClosureCell:
                        EmitNewClosureCell(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.LdClosureCell:
                        EmitLoadClosureCell(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.StClosureCell:
                        EmitStoreClosureCell(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.NewClosure:
                        EmitNewClosure(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.LdClosureSlot:
                        EmitLoadClosureSlot(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.NewDelegate:
                    case BytecodeOp.NewDelegateClosed:
                        EmitNewDelegate(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.DelegateCombine:
                    case BytecodeOp.DelegateRemove:
                        EmitDelegateBinary(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.DelegateInvoke:
                        EmitDelegateInvoke(stack, statements, pc, ins);
                        break;

                    case BytecodeOp.Br:
                        {
                            AddSuccessor(successorPcs, ins.Operand0);

                            SpillStackForBoundaries(statements, stack, successorPcs, pc, ins.Op);

                            statements.Add(Node(
                                GenTreeKind.Branch,
                                pc,
                                ins.Op,
                                targetPc: ins.Operand0,
                                targetBlockId: BlockIdForPc(ins.Operand0)));

                            pc++;
                            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                        }

                    case BytecodeOp.Leave:
                        {
                            AddSuccessor(successorPcs, ins.Operand0);

                            DiscardStackForLeave(statements, stack, pc, ins.Op);

                            statements.Add(Node(
                                GenTreeKind.Branch,
                                pc,
                                ins.Op,
                                targetPc: ins.Operand0,
                                targetBlockId: BlockIdForPc(ins.Operand0)));

                            pc++;
                            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                        }

                    case BytecodeOp.Brtrue:
                    case BytecodeOp.Brfalse:
                        {
                            var cond = Pop(stack, pc, ins.Op);
                            AddSuccessor(successorPcs, ins.Operand0);
                            if (pc + 1 < _body.Instructions.Length)
                                AddSuccessor(successorPcs, pc + 1);
                            SpillStackForBoundaries(statements, stack, successorPcs, pc, ins.Op);
                            statements.Add(Node(ins.Op == BytecodeOp.Brtrue ? GenTreeKind.BranchTrue : GenTreeKind.BranchFalse,
                                pc, ins.Op, operands: One(cond.Node), targetPc: ins.Operand0, targetBlockId: BlockIdForPc(ins.Operand0)));
                            pc++;
                            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                        }

                    case BytecodeOp.Ret:
                        {
                            if (ins.Pop == 1)
                            {
                                var value = Pop(stack, pc, ins.Op);
                                statements.Add(Node(GenTreeKind.Return, pc, ins.Op, operands: One(value.Node)));
                            }
                            else
                            {
                                statements.Add(Node(GenTreeKind.Return, pc, ins.Op));
                            }
                            pc++;
                            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                        }

                    case BytecodeOp.Throw:
                        {
                            var value = Pop(stack, pc, ins.Op);
                            statements.Add(Node(GenTreeKind.Throw, pc, ins.Op, operands: One(value.Node)));
                            pc++;
                            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
                        }

                    case BytecodeOp.Rethrow:
                        statements.Add(Node(GenTreeKind.Rethrow, pc, ins.Op));
                        pc++;
                        return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);

                    case BytecodeOp.Ldexception:
                        Push(stack, Node(GenTreeKind.ExceptionObject, pc, ins.Op, stackKind: GenStackKind.Ref));
                        break;

                    case BytecodeOp.Endfinally:
                        statements.Add(Node(GenTreeKind.EndFinally, pc, ins.Op));
                        pc++;
                        return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);

                    default:
                        throw Fail(pc, ins.Op, $"Unsupported opcode '{ins.Op}'.");
                }

                pc++;
            }

            if (pc < _body.Instructions.Length)
            {
                AddSuccessor(successorPcs, pc);
                SpillStackForBoundaries(statements, stack, successorPcs, pc - 1, BytecodeOp.Nop);
            }

            return CreateBlock(blockId, startPc, pc, statements, successorPcs, stack.Count);
        }

        private GenTreeBlock CreateBlock(int blockId, int startPc, int endPc, List<GenTree> statements, List<int> successorPcs, int exitStackDepth)
        {
            var succBlockIds = new List<int>(successorPcs.Count);
            for (int i = 0; i < successorPcs.Count; i++)
                succBlockIds.Add(BlockIdForPc(successorPcs[i]));

            int entryDepth = TryGetStackDepthAtPc(startPc, out int depth) ? depth : 0;
            var jumpKind = ClassifyBlockJump(statements, successorPcs);
            var flags = ComputeBlockFlags(blockId, startPc, endPc, entryDepth, exitStackDepth, successorPcs.Count);

            return new GenTreeBlock(
                blockId,
                startPc,
                endPc,
                entryDepth,
                exitStackDepth,
                jumpKind,
                flags,
                statements.ToImmutableArray(),
                succBlockIds.ToImmutableArray(),
                successorPcs.ToImmutableArray());
        }

        private GenTreeBlockJumpKind ClassifyBlockJump(List<GenTree> statements, List<int> successorPcs)
        {
            if (statements.Count == 0)
                return successorPcs.Count == 0 ? GenTreeBlockJumpKind.None : GenTreeBlockJumpKind.FallThrough;

            return statements[statements.Count - 1].Kind switch
            {
                GenTreeKind.Branch => GenTreeBlockJumpKind.Always,
                GenTreeKind.BranchTrue or GenTreeKind.BranchFalse => GenTreeBlockJumpKind.Conditional,
                GenTreeKind.Return => GenTreeBlockJumpKind.Return,
                GenTreeKind.Throw => GenTreeBlockJumpKind.Throw,
                GenTreeKind.Rethrow => GenTreeBlockJumpKind.Rethrow,
                GenTreeKind.EndFinally => GenTreeBlockJumpKind.EndFinally,
                _ => successorPcs.Count == 0 ? GenTreeBlockJumpKind.None : GenTreeBlockJumpKind.FallThrough,
            };
        }

        private GenTreeBlockFlags ComputeBlockFlags(int blockId, int startPc, int endPc, int entryStackDepth, int exitStackDepth, int successorCount)
        {
            GenTreeBlockFlags flags = GenTreeBlockFlags.None;
            if (blockId == 0) flags |= GenTreeBlockFlags.Entry;
            if (entryStackDepth != 0) flags |= GenTreeBlockFlags.HasStackEntry;
            if (successorCount != 0 && exitStackDepth != 0) flags |= GenTreeBlockFlags.HasStackExit;

            if (_body.ExceptionHandlers.Length == 0)
                return flags;

            for (int i = 0; i < _body.ExceptionHandlers.Length; i++)
            {
                var h = _body.ExceptionHandlers[i];
                if (h.TryStartPc == startPc) flags |= GenTreeBlockFlags.TryEntry;
                if (h.HandlerStartPc == startPc) flags |= GenTreeBlockFlags.HandlerEntry;
                if (RangesIntersect(startPc, endPc, h.TryStartPc, h.TryEndPc)) flags |= GenTreeBlockFlags.InTryRegion;
                if (RangesIntersect(startPc, endPc, h.HandlerStartPc, h.HandlerEndPc)) flags |= GenTreeBlockFlags.InHandlerRegion;
            }

            return flags;
        }
        private void DiscardStackForLeave(List<GenTree> statements, List<StackValue> stack, int pc, BytecodeOp sourceOp)
        {
            if (stack.Count == 0)
                return;
            for (int i = 0; i < stack.Count; i++)
            {
                GenTree value = stack[i].Node;
                statements.Add(Node(GenTreeKind.Eval, pc, sourceOp, operands: One(value)));
            }
            stack.Clear();
        }
        private static bool RangesIntersect(int aStart, int aEnd, int bStart, int bEnd)
            => aStart < bEnd && bStart < aEnd;
        private bool PcInExceptionRegion(int pc)
        {
            if (_body.ExceptionHandlers.Length == 0)
                return false;

            for (int i = 0; i < _body.ExceptionHandlers.Length; i++)
            {
                var h = _body.ExceptionHandlers[i];
                if ((uint)(pc - h.TryStartPc) < (uint)(h.TryEndPc - h.TryStartPc) ||
                    (uint)(pc - h.HandlerStartPc) < (uint)(h.HandlerEndPc - h.HandlerStartPc))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PcInExceptionHandlerRegion(int pc)
        {
            if (_body.ExceptionHandlers.Length == 0)
                return false;
            for (int i = 0; i < _body.ExceptionHandlers.Length; i++)
            {
                var h = _body.ExceptionHandlers[i];
                if ((uint)(pc - h.HandlerStartPc) < (uint)(h.HandlerEndPc - h.HandlerStartPc))
                    return true;
            }
            return false;
        }
        private bool TryGetStackDepthAtPc(int pc, out int depth)
        {
            if ((uint)pc < (uint)_stackDepthAtPc.Length)
            {
                depth = _stackDepthAtPc[pc];
                return (uint)depth < (uint)_stackDepthAtPc.Length && (_stackDepthAtPc[depth] != UnreachableStackDepth);
            }

            depth = 0;
            return false;
        }
        private List<StackValue> CreateEntryStack(int startPc)
        {
            if (!TryGetStackDepthAtPc(startPc, out int depth))
                throw Fail(startPc, BytecodeOp.Nop, "Missing stack-depth state for block entry.");

            var stack = new List<StackValue>(Math.Max(depth, 4));
            for (int i = 0; i < depth; i++)
            {
                var temp = GetStackEntryTemp(startPc, i, null, GenStackKind.Unknown);
                Push(stack, TempLoad(startPc, BytecodeOp.Nop, temp));
            }
            return stack;
        }

        private void SpillStackForBoundaries(List<GenTree> statements, List<StackValue> stack, IReadOnlyList<int> successorPcs, int pc, BytecodeOp sourceOp)
        {
            if (stack.Count == 0 || successorPcs.Count == 0)
                return;

            var uniqueSuccessors = new List<int>(successorPcs.Count);
            for (int i = 0; i < successorPcs.Count; i++)
            {
                int successorPc = successorPcs[i];
                if (!uniqueSuccessors.Contains(successorPc))
                    uniqueSuccessors.Add(successorPc);
            }

            if (uniqueSuccessors.Count == 1)
            {
                SpillStackForBoundary(statements, stack, uniqueSuccessors[0], pc, sourceOp);
                return;
            }

            for (int i = 0; i < stack.Count; i++)
            {
                var value = stack[i];
                var firstTemp = GetStackEntryTemp(uniqueSuccessors[0], i, value.Type, value.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(value.Node), int32: firstTemp.Index));

                for (int s = 1; s < uniqueSuccessors.Count; s++)
                {
                    var targetTemp = GetStackEntryTemp(uniqueSuccessors[s], i, value.Type, value.StackKind);
                    var reload = TempLoad(pc, sourceOp, firstTemp);
                    statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(reload.Node), int32: targetTemp.Index));
                }
            }
        }

        private void SpillStackForBoundary(List<GenTree> statements, List<StackValue> stack, int targetPc, int pc, BytecodeOp sourceOp)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                var value = stack[i];
                var temp = GetStackEntryTemp(targetPc, i, value.Type, value.StackKind);
                statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(value.Node), int32: temp.Index));
            }
        }

        private GenTemp GetStackEntryTemp(int startPc, int depth, RuntimeType? type, GenStackKind stackKind)
        {
            var key = (StartPc: startPc, Depth: depth);
            if (_stackEntryTemps.TryGetValue(key, out var existing))
            {
                if (IsUnspecifiedStackTempRequest(type, stackKind))
                    return existing;

                GenStackKind mergedKind = MergeStackKind(existing.StackKind, stackKind);
                RuntimeType? mergedType = MergeStackType(existing.Type, existing.StackKind, type, stackKind, mergedKind);

                if (AreIncompatibleStackTempShapes(existing.Type, existing.StackKind, type, stackKind, mergedKind))
                {
                    throw Fail(
                        startPc,
                        BytecodeOp.Nop,
                        $"Incompatible stack temp at block entry pc {startPc}, depth {depth}: " +
                        $"existing {existing.StackKind}/{existing.Type}, incoming {stackKind}/{type}.");
                }

                if (!ReferenceEquals(mergedType, existing.Type) || mergedKind != existing.StackKind)
                {
                    existing = new GenTemp(existing.Index, existing.Kind, mergedType, mergedKind);
                    ReplaceTemp(existing);
                    _stackEntryTemps[key] = existing;
                }
                return existing;
            }

            var temp = new GenTemp(_nextTempIndex++, GenTempKind.StackSpill, type, stackKind);
            _stackEntryTemps.Add(key, temp);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(temp.Index);
            return temp;
        }

        private static bool IsUnspecifiedStackTempRequest(RuntimeType? type, GenStackKind stackKind)
            => type is null && stackKind == GenStackKind.Unknown;

        private static RuntimeType? MergeStackType(
            RuntimeType? leftType,
            GenStackKind leftKind,
            RuntimeType? rightType,
            GenStackKind rightKind,
            GenStackKind mergedKind)
        {
            if (leftType is null) return rightType;
            if (rightType is null) return leftType;
            if (ReferenceEquals(leftType, rightType)) return leftType;

            if (mergedKind == GenStackKind.Ref && IsObjectReferenceStackKind(leftKind) && IsObjectReferenceStackKind(rightKind))
                return null;

            return null;
        }

        private static bool AreIncompatibleStackTempShapes(
            RuntimeType? leftType,
            GenStackKind leftKind,
            RuntimeType? rightType,
            GenStackKind rightKind,
            GenStackKind mergedKind)
        {
            if (leftKind == GenStackKind.Unknown || rightKind == GenStackKind.Unknown)
                return false;

            if (mergedKind == GenStackKind.Unknown)
                return true;

            if (leftType is not null && rightType is not null && !ReferenceEquals(leftType, rightType))
                return !(mergedKind == GenStackKind.Ref && IsObjectReferenceStackKind(leftKind) && IsObjectReferenceStackKind(rightKind));

            return false;
        }

        private static bool IsObjectReferenceStackKind(GenStackKind kind)
            => kind is GenStackKind.Ref or GenStackKind.Null;

        private static GenStackKind MergeStackKind(GenStackKind left, GenStackKind right)
        {
            if (left == right) return left;
            if (left == GenStackKind.Unknown) return right;
            if (right == GenStackKind.Unknown) return left;
            if (left == GenStackKind.Null && right == GenStackKind.Ref) return GenStackKind.Ref;
            if (left == GenStackKind.Ref && right == GenStackKind.Null) return GenStackKind.Ref;
            return GenStackKind.Unknown;
        }

        private void AppendImporterStatement(List<GenTree> statements, List<StackValue> stack, GenTree statement)
        {
            if (RequiresImporterStackBarrier(statement))
                SpillEvaluationStackForImportBarrier(statements, stack, statement.Pc, statement.SourceOp);

            statements.Add(statement);
        }

        private void PushImportedValue(List<StackValue> stack, List<GenTree> statements, GenTree value)
        {
            if (!RequiresImmediateMaterialization(value))
            {
                Push(stack, value);
                return;
            }

            SpillEvaluationStackForImportBarrier(statements, stack, value.Pc, value.SourceOp);

            var temp = CreateImporterSpillTemp(value.Type, value.StackKind);
            statements.Add(Node(GenTreeKind.StoreTemp, value.Pc, value.SourceOp, operands: One(value), int32: temp.Index));
            Push(stack, TempLoad(value.Pc, value.SourceOp, temp));
        }
        private bool IsAlreadyImporterSpillTemp(StackValue value)
        {
            GenTree node = value.Node;
            return node.Kind == GenTreeKind.Temp && _materializedImporterTempIds.Contains(node.Int32);
        }
        private StackValue MaterializeForImporterBarrier(
            List<GenTree> statements,
            StackValue value,
            int pc,
            BytecodeOp sourceOp)
        {
            if (IsAlreadyImporterSpillTemp(value))
                return value;

            var temp = CreateImporterSpillTemp(value.Type, value.StackKind);
            statements.Add(Node(
                GenTreeKind.StoreTemp,
                pc,
                sourceOp,
                operands: One(value.Node),
                int32: temp.Index));

            return TempLoad(pc, sourceOp, temp);
        }
        private void SpillEvaluationStackForImportBarrier(List<GenTree> statements, List<StackValue> stack, int pc, BytecodeOp sourceOp)
        {
            if (stack.Count == 0)
                return;

            for (int i = 0; i < stack.Count; i++)
                stack[i] = MaterializeForImporterBarrier(statements, stack[i], pc, sourceOp);
        }

        private GenTemp CreateImporterSpillTemp(RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.StackSpill, type, stackKind);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(index);
            return temp;
        }

        private static bool RequiresImporterStackBarrier(GenTree statement)
        {
            if (statement is null)
                return false;

            if ((statement.Flags & (GenTreeFlags.LocalDef | GenTreeFlags.MemoryWrite | GenTreeFlags.GlobalRef | GenTreeFlags.ContainsCall | GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow)) != 0)
                return true;

            return statement.Kind is
                GenTreeKind.StoreLocal or
                GenTreeKind.StoreArg or
                GenTreeKind.StoreTemp or
                GenTreeKind.StoreIndirect or
                GenTreeKind.StoreField or
                GenTreeKind.StoreStaticField or
                GenTreeKind.StoreArrayElement or
                GenTreeKind.Eval or
                GenTreeKind.Return or
                GenTreeKind.Throw or
                GenTreeKind.Rethrow or
                GenTreeKind.EndFinally;
        }

        private static bool RequiresImmediateMaterialization(GenTree value)
        {
            if (value is null)
                return false;

            if (value.StackKind == GenStackKind.Void)
                return false;

            if (value.Kind is GenTreeKind.Call or GenTreeKind.VirtualCall or GenTreeKind.DelegateInvoke)
                return false;

            const GenTreeFlags materializeFlags =
                GenTreeFlags.ContainsCall |
                GenTreeFlags.CanThrow |
                GenTreeFlags.SideEffect |
                GenTreeFlags.MemoryRead |
                GenTreeFlags.MemoryWrite |
                GenTreeFlags.GlobalRef |
                GenTreeFlags.Indirect |
                GenTreeFlags.Allocation;

            if ((value.Flags & materializeFlags) != 0)
                return true;

            return value.Kind is
                GenTreeKind.Call or
                GenTreeKind.VirtualCall or
                GenTreeKind.NewObject or
                GenTreeKind.NewArray or
                GenTreeKind.ArrayElement or
                GenTreeKind.ArrayElementAddr or
                GenTreeKind.ArrayDataRef or
                GenTreeKind.Field or
                GenTreeKind.FieldAddr or
                GenTreeKind.StaticField or
                GenTreeKind.StaticFieldAddr or
                GenTreeKind.LoadIndirect or
                GenTreeKind.StaticData or
                GenTreeKind.StackAlloc or
                GenTreeKind.AllocHGlobal or
                GenTreeKind.Box or
                GenTreeKind.UnboxAny or
                GenTreeKind.CastClass;
        }

        private GenTemp CreateDupTemp(RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.DupSpill, type, stackKind);
            _dupTemps.Add(index, temp);
            _createdDupTempIds.Add(index);
            _temps.Add(temp);
            _materializedImporterTempIds.Add(index);
            return temp;
        }

        private void ReplaceTemp(GenTemp temp)
        {
            for (int i = 0; i < _temps.Count; i++)
            {
                if (_temps[i].Index == temp.Index && _temps[i].Kind == temp.Kind)
                {
                    _temps[i] = temp;
                    return;
                }
            }
            _temps.Add(temp);
        }

        private StackValue TempLoad(int pc, BytecodeOp sourceOp, GenTemp temp)
        {
            return new StackValue(Node(GenTreeKind.Temp, pc, sourceOp, type: temp.Type, stackKind: temp.StackKind, int32: temp.Index), temp.Type, temp.StackKind);
        }

        private StackValue TempAddress(int pc, BytecodeOp sourceOp, GenTemp temp)
        {
            var byRefType = temp.Type is null ? null : _rts.GetByRefType(temp.Type);
            return new StackValue(Node(GenTreeKind.TempAddr, pc, sourceOp, type: byRefType, stackKind: GenStackKind.ByRef, int32: temp.Index), byRefType, GenStackKind.ByRef);
        }

        private GenTemp CreateStructMaterializationTemp(RuntimeType type)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, GenTempKind.StructMaterialization, type, StackKindOf(type));
            _temps.Add(temp);
            _structMaterializationTempIds.Add(index);
            return temp;
        }

        private bool TryGetTempByIndex(int index, out GenTemp temp)
        {
            for (int i = 0; i < _temps.Count; i++)
            {
                if (_temps[i].Index == index)
                {
                    temp = _temps[i];
                    return true;
                }
            }

            temp = default;
            return false;
        }

        private void AppendLocalLikeStore(
            List<GenTree> statements,
            List<StackValue> stack,
            int pc,
            BytecodeOp sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType? targetType,
            GenTree value)
        {
            if (targetType is not null &&
                TryRetargetStructMaterializationToLocalLikeStore(statements, pc, sourceOp, storeKind, addressKind, index, targetType, value))
            {
                return;
            }

            if (targetType is not null &&
                CanUseFieldWiseStructStoreForDestination(addressKind, index) &&
                TryAppendFieldWiseStructStore(statements, stack, pc, sourceOp, addressKind, index, targetType, value))
            {
                return;
            }

            AppendImporterStatement(statements, stack, Node(storeKind, pc, sourceOp, operands: One(value), int32: index));
        }
        private bool TryRetargetStructMaterializationToLocalLikeStore(
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType targetType,
            GenTree value)
        {
            if (addressKind == GenTreeKind.TempAddr && value.Kind == GenTreeKind.Temp && value.Int32 == index)
                return false;

            if (PcInExceptionRegion(pc))
                return false;

            if (TryGetTrailingStructMaterialization(statements, value, targetType, out var temp, out var ctorCall, out int rewriteStart))
            {
                if (!CanRetargetStructMaterializationToLocalLikeDestination(addressKind, index, ctorCall))
                    return false;

                statements.RemoveRange(rewriteStart, statements.Count - rewriteStart);
                EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);
                var destinationAddress = CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType);
                AppendRetargetedStructConstructorCall(statements, ctorCall, destinationAddress);
                RemoveEliminatedStructMaterializationTemp(temp);
                return true;
            }

            if (TryGetTrailingInlineStructMaterializationThroughCopyChain(statements, value, targetType, out temp, out var inlinePlan, out var copyTempIds))
            {
                if (!CanRetargetInlineStructMaterializationToLocalLikeDestination(addressKind, index, inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                RemoveEliminatedTempIndexes(copyTempIds);
                return true;
            }

            if (TryGetTrailingInlineStructMaterialization(statements, value, targetType, out temp, out inlinePlan))
            {
                if (!CanRetargetInlineStructMaterializationToLocalLikeDestination(addressKind, index, inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationToLocalLike(statements, pc, sourceOp, storeKind, addressKind, index, targetType);

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, targetType));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                return true;
            }

            return false;
        }

        private bool TryRetargetStructMaterializationToAddress(
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            GenTree destinationAddress,
            RuntimeType targetType,
            GenTree value)
        {
            if (!IsReusableStructDestinationAddress(destinationAddress))
                return false;

            if (PcInExceptionRegion(pc))
                return false;

            if (TryGetTrailingStructMaterialization(statements, value, targetType, out var temp, out var ctorCall, out int rewriteStart))
            {
                if (!CanRetargetStructMaterializationToMemoryDestination(ctorCall))
                    return false;

                statements.RemoveRange(rewriteStart, statements.Count - rewriteStart);
                EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));
                AppendRetargetedStructConstructorCall(statements, ctorCall, CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                return true;
            }

            if (TryGetTrailingInlineStructMaterializationThroughCopyChain(statements, value, targetType, out temp, out var inlinePlan, out var copyTempIds))
            {
                if (!CanRetargetInlineStructMaterializationToMemoryDestination(inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                RemoveEliminatedTempIndexes(copyTempIds);
                return true;
            }

            if (TryGetTrailingInlineStructMaterialization(statements, value, targetType, out temp, out inlinePlan))
            {
                if (!CanRetargetInlineStructMaterializationToMemoryDestination(inlinePlan))
                    return false;

                statements.RemoveRange(inlinePlan.RewriteStart, statements.Count - inlinePlan.RewriteStart);
                if (inlinePlan.NeedsDefaultInitialization)
                    EmitStructDefaultInitializationThroughAddress(statements, pc, sourceOp, targetType, () => CloneAddressNode(destinationAddress));

                AppendRetargetedInlineStructMaterialization(statements, inlinePlan, temp, () => CloneAddressNode(destinationAddress));
                RemoveEliminatedStructMaterializationTemp(temp);
                RemoveEliminatedTempIndexes(inlinePlan.AliasTempIds);
                return true;
            }

            return false;
        }

        private bool CanRetargetStructMaterializationToLocalLikeDestination(GenTreeKind destinationAddressKind, int destinationIndex, GenTree ctorCall)
        {
            for (int i = 1; i < ctorCall.Operands.Length; i++)
            {
                var arg = ctorCall.Operands[i];
                if (HasStructMaterializationOrderingHazard(arg))
                    return false;

                if (ReferencesLocalLikeDestination(arg, destinationAddressKind, destinationIndex))
                    return false;

                if (DestinationMayBeExternallyAliased(destinationAddressKind, destinationIndex) && HasAliasingMemoryAccess(arg))
                    return false;
            }

            return true;
        }

        private static bool CanRetargetStructMaterializationToMemoryDestination(GenTree ctorCall)
        {
            for (int i = 1; i < ctorCall.Operands.Length; i++)
            {
                if (HasStructMaterializationOrderingHazard(ctorCall.Operands[i]) || HasAliasingMemoryAccess(ctorCall.Operands[i]))
                    return false;
            }

            return true;
        }

        private sealed class InlineStructMaterializationPlan
        {
            public int RewriteStart;
            public bool SawDefaultInitialization;
            public bool WritesAllInstanceFields;
            public readonly List<GenTree> FieldStores = new();
            public readonly HashSet<int> AliasTempIds = new();
            public readonly HashSet<RuntimeField> WrittenFields = new();

            public bool NeedsDefaultInitialization => SawDefaultInitialization && !WritesAllInstanceFields;
        }

        private bool CanRetargetInlineStructMaterializationToLocalLikeDestination(
            GenTreeKind destinationAddressKind,
            int destinationIndex,
            InlineStructMaterializationPlan plan)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var value = plan.FieldStores[i].Operands[1];
                if (HasStructMaterializationOrderingHazard(value))
                    return false;

                if (ReferencesLocalLikeDestination(value, destinationAddressKind, destinationIndex))
                    return false;

                if (DestinationMayBeExternallyAliased(destinationAddressKind, destinationIndex) && HasAliasingMemoryAccess(value))
                    return false;
            }

            return true;
        }

        private static bool CanRetargetInlineStructMaterializationToMemoryDestination(InlineStructMaterializationPlan plan)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var value = plan.FieldStores[i].Operands[1];
                if (HasStructMaterializationOrderingHazard(value) || HasAliasingMemoryAccess(value))
                    return false;
            }

            return true;
        }

        private bool TryGetTrailingInlineStructMaterializationThroughCopyChain(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out InlineStructMaterializationPlan plan,
            out HashSet<int> copyTempIds)
        {
            temp = default;
            plan = null!;
            copyTempIds = new HashSet<int>();

            if (value.Kind != GenTreeKind.Temp || statements.Count == 0)
                return false;

            int end = statements.Count;
            var current = value;
            while (end > 0)
            {
                if (current.Kind != GenTreeKind.Temp)
                    return false;

                int copyTempIndex = current.Int32;
                if (!TryGetTempByIndex(copyTempIndex, out var copyTemp) || !IsElidableStructCopyTemp(copyTemp) || !ReferenceEquals(copyTemp.Type, targetType))
                    return false;

                var copy = statements[end - 1];
                if (!TryGetTrailingTempStructCopy(copy, copyTempIndex, targetType, out var source))
                    return false;

                if (HasTempReferenceBefore(statements, end - 1, copyTempIndex))
                    return false;

                copyTempIds.Add(copyTempIndex);
                end--;

                var prefix = end == statements.Count ? statements : statements.GetRange(0, end);
                if (TryGetTrailingInlineStructMaterialization(prefix, source, targetType, out temp, out plan))
                    return true;

                current = source;
            }

            return false;
        }

        private static bool TryGetTrailingTempStructCopy(GenTree statement, int destinationTempIndex, RuntimeType targetType, out GenTree source)
        {
            source = null!;

            if (statement.Kind != GenTreeKind.StoreTemp || statement.Int32 != destinationTempIndex || statement.Operands.Length != 1)
                return false;

            source = statement.Operands[0];
            return source.Kind == GenTreeKind.Temp && ReferenceEquals(source.Type, targetType);
        }

        private static bool IsElidableStructCopyTemp(GenTemp temp)
        {
            return temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineLocal or GenTempKind.InlineReturn or GenTempKind.StackSpill;
        }

        private static bool HasTempReferenceBefore(List<GenTree> statements, int stop, int tempIndex)
        {
            for (int i = 0; i < stop; i++)
            {
                if (ReferencesTempIndex(statements[i], tempIndex))
                    return true;
            }

            return false;
        }

        private static bool ReferencesTempIndex(GenTree tree, int tempIndex)
        {
            if (tree.Int32 == tempIndex && tree.Kind is GenTreeKind.Temp or GenTreeKind.TempAddr or GenTreeKind.StoreTemp)
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesTempIndex(operands[i], tempIndex))
                    return true;
            }

            return false;
        }

        private bool TryGetTrailingInlineStructMaterialization(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out InlineStructMaterializationPlan plan)
        {
            temp = default;
            plan = null!;

            if (value.Kind != GenTreeKind.Temp)
                return false;

            if (!TryGetTempByIndex(value.Int32, out temp) || !IsElidableStructConstructionTemp(temp) || !ReferenceEquals(temp.Type, targetType))
                return false;

            if (!ReferenceEquals(value.Type, targetType) || statements.Count == 0)
                return false;

            for (int start = 0; start < statements.Count; start++)
            {
                if (!TryAnalyzeInlineStructMaterializationSegment(statements, start, temp, targetType, out var candidate))
                    continue;

                if (HasStructMaterializationReferenceBefore(statements, start, temp, candidate.AliasTempIds))
                    continue;

                plan = candidate;
                return true;
            }

            return false;
        }

        private static bool IsElidableStructConstructionTemp(GenTemp temp)
        {
            return temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineLocal or GenTempKind.InlineReturn;
        }

        private bool TryAnalyzeInlineStructMaterializationSegment(
            List<GenTree> statements,
            int start,
            GenTemp temp,
            RuntimeType targetType,
            out InlineStructMaterializationPlan plan)
        {
            plan = new InlineStructMaterializationPlan { RewriteStart = start };
            bool beforeCtorStores = true;
            bool sawMaterializationStatement = false;

            for (int i = start; i < statements.Count; i++)
            {
                var statement = statements[i];

                if (TryGetStructMaterializationAliasDefinition(statement, temp, plan.AliasTempIds, out int aliasTempId))
                {
                    plan.AliasTempIds.Add(aliasTempId);
                    sawMaterializationStatement = true;
                    continue;
                }

                if (beforeCtorStores && IsStructDefaultInitializationForTempOrAlias(statement, temp, targetType, plan.AliasTempIds))
                {
                    plan.SawDefaultInitialization = true;
                    sawMaterializationStatement = true;
                    continue;
                }

                if (TryGetStructMaterializationFieldStore(statement, temp, targetType, plan.AliasTempIds, out var field, out var fieldValue))
                {
                    if (ReferencesStructMaterializationStorage(fieldValue, temp, plan.AliasTempIds))
                        return false;

                    beforeCtorStores = false;
                    sawMaterializationStatement = true;
                    plan.FieldStores.Add(statement);
                    plan.WrittenFields.Add(field);
                    continue;
                }

                return false;
            }

            if (!sawMaterializationStatement)
                return false;

            if (plan.FieldStores.Count == 0 && !plan.SawDefaultInitialization)
                return false;

            plan.WritesAllInstanceFields = plan.FieldStores.Count != 0 && AllInstanceFieldsWritten(targetType, plan.WrittenFields);
            if (!plan.SawDefaultInitialization && !plan.WritesAllInstanceFields)
                return false;

            return true;
        }

        private static bool TryGetStructMaterializationAliasDefinition(
            GenTree statement,
            GenTemp temp,
            HashSet<int> aliasTempIds,
            out int aliasTempId)
        {
            aliasTempId = -1;

            if (statement.Kind != GenTreeKind.StoreTemp || statement.Operands.Length != 1)
                return false;

            if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                return false;

            if (statement.Int32 == temp.Index || aliasTempIds.Contains(statement.Int32))
                return false;

            aliasTempId = statement.Int32;
            return true;
        }

        private static bool IsStructDefaultInitializationForTempOrAlias(
            GenTree statement,
            GenTemp temp,
            RuntimeType valueType,
            HashSet<int> aliasTempIds)
        {
            if (statement.Kind == GenTreeKind.StoreTemp && statement.Int32 == temp.Index)
                return statement.Operands.Length == 1 && IsDefaultValueOfType(statement.Operands[0], valueType);

            if (statement.Kind == GenTreeKind.StoreField && statement.Operands.Length == 2)
            {
                if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                    return false;

                if (statement.Field is null || statement.Field.IsStatic || !ReferenceEquals(statement.Field.DeclaringType, valueType))
                    return false;

                return IsDefaultValue(statement.Operands[1]);
            }

            return false;
        }

        private static bool TryGetStructMaterializationFieldStore(
            GenTree statement,
            GenTemp temp,
            RuntimeType targetType,
            HashSet<int> aliasTempIds,
            out RuntimeField field,
            out GenTree fieldValue)
        {
            field = null!;
            fieldValue = null!;

            if (statement.Kind != GenTreeKind.StoreField || statement.Operands.Length != 2)
                return false;

            if (statement.Field is null || statement.Field.IsStatic || !ReferenceEquals(statement.Field.DeclaringType, targetType))
                return false;

            if (!IsStructMaterializationAddress(statement.Operands[0], temp, aliasTempIds))
                return false;

            field = statement.Field;
            fieldValue = statement.Operands[1];
            return true;
        }

        private static bool IsStructMaterializationAddress(GenTree node, GenTemp temp, HashSet<int> aliasTempIds)
        {
            if (node.Kind == GenTreeKind.TempAddr && node.Int32 == temp.Index)
                return true;

            if (node.Kind == GenTreeKind.Temp && aliasTempIds.Contains(node.Int32))
                return true;

            return false;
        }

        private static bool ReferencesStructMaterializationStorage(GenTree tree, GenTemp temp, HashSet<int> aliasTempIds)
        {
            if (IsStructMaterializationAddress(tree, temp, aliasTempIds))
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesStructMaterializationStorage(operands[i], temp, aliasTempIds))
                    return true;
            }

            return false;
        }

        private static bool HasStructMaterializationReferenceBefore(
            List<GenTree> statements,
            int stop,
            GenTemp temp,
            HashSet<int> aliasTempIds)
        {
            for (int i = 0; i < stop; i++)
            {
                if (ReferencesStructMaterializationStorage(statements[i], temp, aliasTempIds))
                    return true;

                if (statements[i].Kind == GenTreeKind.StoreTemp && aliasTempIds.Contains(statements[i].Int32))
                    return true;
            }

            return false;
        }

        private static bool AllInstanceFieldsWritten(RuntimeType targetType, HashSet<RuntimeField> writtenFields)
        {
            var fields = targetType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!field.IsStatic && !writtenFields.Contains(field))
                    return false;
            }

            return true;
        }

        private void AppendRetargetedInlineStructMaterialization(
            List<GenTree> statements,
            InlineStructMaterializationPlan plan,
            GenTemp temp,
            Func<GenTree> createDestinationAddress)
        {
            for (int i = 0; i < plan.FieldStores.Count; i++)
            {
                var store = plan.FieldStores[i];
                var fieldValue = CloneTreeReplacingStructMaterializationStorage(store.Operands[1], temp, plan.AliasTempIds, createDestinationAddress);
                statements.Add(Node(GenTreeKind.StoreField, store.Pc, store.SourceOp,
                    operands: Two(createDestinationAddress(), fieldValue), field: store.Field, int64: store.Int64, runtimeType: store.RuntimeType));
            }
        }

        private GenTree CloneTreeReplacingStructMaterializationStorage(
            GenTree node,
            GenTemp temp,
            HashSet<int> aliasTempIds,
            Func<GenTree> createDestinationAddress)
        {
            if (IsStructMaterializationAddress(node, temp, aliasTempIds))
                return createDestinationAddress();

            var operands = node.Operands;
            ImmutableArray<GenTree> clonedOperands = ImmutableArray<GenTree>.Empty;
            if (operands.Length != 0)
            {
                var builder = ImmutableArray.CreateBuilder<GenTree>(operands.Length);
                for (int i = 0; i < operands.Length; i++)
                    builder.Add(CloneTreeReplacingStructMaterializationStorage(operands[i], temp, aliasTempIds, createDestinationAddress));
                clonedOperands = builder.ToImmutable();
            }

            return Node(node.Kind, node.Pc, node.SourceOp,
                type: node.Type,
                stackKind: node.StackKind,
                operands: clonedOperands,
                int32: node.Int32,
                int64: node.Int64,
                text: node.Text,
                runtimeType: node.RuntimeType,
                field: node.Field,
                method: node.Method,
                convKind: node.ConvKind,
                convFlags: node.ConvFlags,
                targetPc: node.TargetPc,
                targetBlockId: node.TargetBlockId);
        }

        private bool DestinationMayBeExternallyAliased(GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => (uint)destinationIndex < (uint)_addressExposedLocals.Length && _addressExposedLocals[destinationIndex],
                GenTreeKind.ArgAddr => (uint)destinationIndex < (uint)_addressExposedArgs.Length && _addressExposedArgs[destinationIndex],
                _ => false,
            };
        }

        private static bool ReferencesLocalLikeDestination(GenTree tree, GenTreeKind destinationAddressKind, int destinationIndex)
        {
            if (IsDestinationLocalLikeUse(tree, destinationAddressKind, destinationIndex))
                return true;

            var operands = tree.Operands;
            for (int i = 0; i < operands.Length; i++)
            {
                if (ReferencesLocalLikeDestination(operands[i], destinationAddressKind, destinationIndex))
                    return true;
            }

            return false;
        }

        private static bool IsDestinationLocalLikeUse(GenTree tree, GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Local || tree.Kind == GenTreeKind.LocalAddr || tree.Kind == GenTreeKind.StoreLocal),
                GenTreeKind.ArgAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Arg || tree.Kind == GenTreeKind.ArgAddr || tree.Kind == GenTreeKind.StoreArg),
                GenTreeKind.TempAddr => tree.Int32 == destinationIndex && (tree.Kind == GenTreeKind.Temp || tree.Kind == GenTreeKind.TempAddr || tree.Kind == GenTreeKind.StoreTemp),
                _ => false,
            };
        }

        private static bool HasStructMaterializationOrderingHazard(GenTree tree)
        {
            const GenTreeFlags hazardFlags = GenTreeFlags.ContainsCall |
                                             GenTreeFlags.CanThrow |
                                             GenTreeFlags.SideEffect |
                                             GenTreeFlags.MemoryRead |
                                             GenTreeFlags.MemoryWrite |
                                             GenTreeFlags.GlobalRef |
                                             GenTreeFlags.Indirect |
                                             GenTreeFlags.Allocation |
                                             GenTreeFlags.ControlFlow |
                                             GenTreeFlags.ExceptionFlow;
            return (tree.Flags & hazardFlags) != 0;
        }

        private static bool HasAliasingMemoryAccess(GenTree tree)
        {
            const GenTreeFlags aliasingFlags = GenTreeFlags.ContainsCall |
                                               GenTreeFlags.SideEffect |
                                               GenTreeFlags.MemoryRead |
                                               GenTreeFlags.MemoryWrite |
                                               GenTreeFlags.GlobalRef |
                                               GenTreeFlags.Indirect |
                                               GenTreeFlags.AddressExposed;
            return (tree.Flags & aliasingFlags) != 0;
        }

        private static bool IsReusableStructDestinationAddress(GenTree address)
        {
            return address.Kind == GenTreeKind.LocalAddr ||
                   address.Kind == GenTreeKind.ArgAddr ||
                   address.Kind == GenTreeKind.TempAddr;
        }

        private bool TryGetTrailingStructMaterialization(
            List<GenTree> statements,
            GenTree value,
            RuntimeType targetType,
            out GenTemp temp,
            out GenTree ctorCall,
            out int rewriteStart)
        {
            temp = default;
            ctorCall = null!;
            rewriteStart = -1;

            if (value.Kind != GenTreeKind.Temp || !_structMaterializationTempIds.Contains(value.Int32))
                return false;

            if (!TryGetTempByIndex(value.Int32, out temp) || temp.Kind != GenTempKind.StructMaterialization || !ReferenceEquals(temp.Type, targetType))
                return false;

            if (!ReferenceEquals(value.Type, targetType) || statements.Count == 0)
                return false;

            var trailing = statements[statements.Count - 1];
            if (trailing.Kind != GenTreeKind.Eval || trailing.Operands.Length != 1)
                return false;

            ctorCall = trailing.Operands[0];
            if (ctorCall.Kind != GenTreeKind.Call || ctorCall.StackKind != GenStackKind.Void || ctorCall.Method is null)
                return false;

            if (ctorCall.Operands.Length == 0 || !ReferenceEquals(ctorCall.Method.DeclaringType, targetType))
                return false;

            if (!IsTempAddressFor(ctorCall.Operands[0], temp))
                return false;

            int first = statements.Count - 1;
            bool sawInitialization = false;
            while (first > 0 && IsStructDefaultInitializationForTemp(statements[first - 1], temp, targetType))
            {
                sawInitialization = true;
                first--;
            }

            if (HasInstanceFields(targetType) && !sawInitialization)
                return false;

            rewriteStart = first;
            return true;
        }

        private static bool IsTempAddressFor(GenTree node, GenTemp temp)
        {
            return node.Kind == GenTreeKind.TempAddr && node.Int32 == temp.Index;
        }

        private static bool IsStructDefaultInitializationForTemp(GenTree statement, GenTemp temp, RuntimeType valueType)
        {
            if (statement.Kind == GenTreeKind.StoreTemp && statement.Int32 == temp.Index)
                return IsDefaultValueOfType(statement.Operands[0], valueType);

            if (statement.Kind == GenTreeKind.StoreField && statement.Operands.Length == 2)
            {
                if (!IsTempAddressFor(statement.Operands[0], temp))
                    return false;

                if (statement.Field is null || statement.Field.IsStatic)
                    return false;

                return IsDefaultValue(statement.Operands[1]);
            }

            return false;
        }

        private static bool IsDefaultValueOfType(GenTree node, RuntimeType type)
        {
            return node.Kind == GenTreeKind.DefaultValue && ReferenceEquals(node.RuntimeType, type);
        }

        private static bool IsDefaultValue(GenTree node)
        {
            return node.Kind == GenTreeKind.DefaultValue;
        }

        private static bool HasInstanceFields(RuntimeType type)
        {
            if (type.Kind != RuntimeTypeKind.Struct)
                return false;

            var fields = type.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                if (!fields[i].IsStatic)
                    return true;
            }

            return false;
        }

        private void EmitStructDefaultInitializationToLocalLike(
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            GenTreeKind storeKind,
            GenTreeKind addressKind,
            int index,
            RuntimeType valueType)
        {
            if (EmitStructDefaultInitializationToAddress(statements, pc, sourceOp, valueType,
                    () => CreateLocalLikeAddress(pc, sourceOp, addressKind, index, valueType)))
            {
                return;
            }

            var init = Node(GenTreeKind.DefaultValue, pc, BytecodeOp.DefaultValue, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            statements.Add(Node(storeKind, pc, sourceOp, operands: One(init), int32: index));
        }

        private void EmitStructDefaultInitializationThroughAddress(
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            RuntimeType valueType,
            Func<GenTree> createDestinationAddress)
        {
            if (EmitStructDefaultInitializationToAddress(statements, pc, sourceOp, valueType, createDestinationAddress))
                return;

            var init = Node(GenTreeKind.DefaultValue, pc, BytecodeOp.DefaultValue, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            statements.Add(Node(GenTreeKind.StoreIndirect, pc, sourceOp, operands: Two(createDestinationAddress(), init), runtimeType: valueType));
        }

        private bool EmitStructDefaultInitializationToAddress(
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            RuntimeType valueType,
            Func<GenTree> createDestinationAddress)
        {
            if (valueType.Kind != RuntimeTypeKind.Struct)
                return false;

            if (valueType.InstanceFields.Length == 0)
                return true;

            if (!CanExpandStructFieldWise(valueType))
                return false;

            var fields = valueType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                var fieldDefault = Node(GenTreeKind.DefaultValue, pc, BytecodeOp.DefaultValue, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType);
                statements.Add(Node(GenTreeKind.StoreField, pc, sourceOp,
                    operands: Two(createDestinationAddress(), fieldDefault), field: field, runtimeType: field.FieldType));
            }

            return true;
        }

        private void AppendRetargetedStructConstructorCall(
            List<GenTree> statements,
            GenTree originalCall,
            GenTree destinationAddress)
        {
            var argsBuilder = ImmutableArray.CreateBuilder<GenTree>(originalCall.Operands.Length);
            argsBuilder.Add(destinationAddress);
            for (int i = 1; i < originalCall.Operands.Length; i++)
                argsBuilder.Add(originalCall.Operands[i]);

            var call = Node(GenTreeKind.Call, originalCall.Pc, originalCall.SourceOp, stackKind: GenStackKind.Void,
                operands: argsBuilder.ToImmutable(), int32: originalCall.Int32, int64: originalCall.Int64, method: originalCall.Method);
            statements.Add(Node(GenTreeKind.Eval, originalCall.Pc, originalCall.SourceOp, operands: One(call)));
        }

        private void RemoveEliminatedStructMaterializationTemp(GenTemp temp)
        {
            RemoveEliminatedTempIndex(temp.Index);
        }

        private void RemoveEliminatedTempIndexes(IEnumerable<int> tempIndexes)
        {
            foreach (int tempIndex in tempIndexes)
                RemoveEliminatedTempIndex(tempIndex);
        }

        private void RemoveEliminatedTempIndex(int tempIndex)
        {
            for (int i = _temps.Count - 1; i >= 0; i--)
            {
                if (_temps[i].Index == tempIndex)
                    _temps.RemoveAt(i);
            }

            _structMaterializationTempIds.Remove(tempIndex);
            _materializedImporterTempIds.Remove(tempIndex);
            _dupTemps.Remove(tempIndex);
            _createdDupTempIds.Remove(tempIndex);
        }

        private bool CanUseFieldWiseStructStoreForDestination(GenTreeKind destinationAddressKind, int destinationIndex)
        {
            return destinationAddressKind switch
            {
                GenTreeKind.LocalAddr => (uint)destinationIndex >= (uint)_addressExposedLocals.Length || !_addressExposedLocals[destinationIndex],
                GenTreeKind.ArgAddr => (uint)destinationIndex >= (uint)_addressExposedArgs.Length || !_addressExposedArgs[destinationIndex],
                _ => true,
            };
        }

        private bool TryAppendFieldWiseStructStore(
            List<GenTree> statements,
            List<StackValue> stack,
            int pc,
            BytecodeOp sourceOp,
            GenTreeKind destinationAddressKind,
            int destinationIndex,
            RuntimeType destinationType,
            GenTree value)
        {
            if (!CanExpandStructFieldWise(destinationType))
                return false;

            bool sourceIsDefault = value.Kind == GenTreeKind.DefaultValue && ReferenceEquals(value.RuntimeType, destinationType);
            GenTree? sourceAddress = null;
            if (!sourceIsDefault && !TryCreateAddressForStructValue(pc, sourceOp, value, destinationType, out sourceAddress))
                return false;

            var fields = destinationType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                GenTree fieldValue = sourceIsDefault
                    ? Node(GenTreeKind.DefaultValue, pc, sourceOp, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType)
                    : Node(GenTreeKind.Field, pc, BytecodeOp.Ldfld, type: field.FieldType, stackKind: StackKindOf(field.FieldType), operands: One(CloneAddressNode(sourceAddress!)), field: field, runtimeType: field.FieldType);

                GenTree destinationAddress = CreateLocalLikeAddress(pc, sourceOp, destinationAddressKind, destinationIndex, destinationType);
                AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, pc, sourceOp, operands: Two(destinationAddress, fieldValue), field: field, runtimeType: field.FieldType));
            }

            return true;
        }

        private bool TryCreateAddressForStructValue(int pc, BytecodeOp sourceOp, GenTree value, RuntimeType expectedType, out GenTree address)
        {
            if (!ReferenceEquals(value.Type, expectedType))
            {
                address = null!;
                return false;
            }

            switch (value.Kind)
            {
                case GenTreeKind.Local:
                    address = CreateLocalLikeAddress(pc, sourceOp, GenTreeKind.LocalAddr, value.Int32, expectedType);
                    return true;

                case GenTreeKind.Arg:
                    address = CreateLocalLikeAddress(pc, sourceOp, GenTreeKind.ArgAddr, value.Int32, expectedType);
                    return true;

                case GenTreeKind.Temp:
                    if (!TryGetTempByIndex(value.Int32, out var temp) || !ReferenceEquals(temp.Type, expectedType))
                    {
                        address = null!;
                        return false;
                    }
                    address = TempAddress(pc, sourceOp, temp).Node;
                    return true;
            }

            address = null!;
            return false;
        }

        private GenTree CreateLocalLikeAddress(int pc, BytecodeOp sourceOp, GenTreeKind addressKind, int index, RuntimeType targetType)
        {
            var byRefType = _rts.GetByRefType(targetType);
            return Node(addressKind, pc, sourceOp, type: byRefType, stackKind: GenStackKind.ByRef, int32: index);
        }

        private GenTree CloneAddressNode(GenTree address)
        {
            return Node(address.Kind, address.Pc, address.SourceOp, type: address.Type, stackKind: address.StackKind, int32: address.Int32);
        }

        private static bool CanExpandStructFieldWise(RuntimeType? type)
        {
            if (type is null || !type.IsValueType || type.Kind != RuntimeTypeKind.Struct || type.InstanceFields.Length == 0)
                return false;

            if (StackKindOf(type) != GenStackKind.Value)
                return false;

            return HasNonOverlappingInstanceFields(type.InstanceFields);
        }

        private static bool HasNonOverlappingInstanceFields(RuntimeField[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].IsStatic)
                    continue;

                int iStart = fields[i].Offset;
                int iEnd = iStart + Math.Max(1, fields[i].FieldType.SizeOf);
                for (int j = i + 1; j < fields.Length; j++)
                {
                    if (fields[j].IsStatic)
                        continue;

                    int jStart = fields[j].Offset;
                    int jEnd = jStart + Math.Max(1, fields[j].FieldType.SizeOf);
                    if (iStart < jEnd && jStart < iEnd)
                        return false;
                }
            }

            return true;
        }

        private void EmitUnary(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var value = Pop(stack, pc, ins.Op);
            RuntimeType? type = value.Type;
            GenStackKind stackKind = value.StackKind;
            RuntimeType? operandType = null;
            GenTreeKind kind = ins.Op switch
            {
                BytecodeOp.Neg => GenTreeKind.Unary,
                BytecodeOp.Not => GenTreeKind.Unary,
                BytecodeOp.PtrToByRef => GenTreeKind.PointerToByRef,
                BytecodeOp.CastClass => GenTreeKind.CastClass,
                BytecodeOp.Isinst => GenTreeKind.IsInst,
                BytecodeOp.Box => GenTreeKind.Box,
                BytecodeOp.UnboxAny => GenTreeKind.UnboxAny,
                _ => throw Fail(pc, ins.Op, "Not a unary opcode."),
            };

            switch (ins.Op)
            {
                case BytecodeOp.PtrToByRef:
                    stackKind = GenStackKind.ByRef;
                    type = null;
                    break;

                case BytecodeOp.CastClass:
                case BytecodeOp.Isinst:
                    operandType = ResolveType(ins.Operand0);
                    type = operandType.IsValueType ? _rts.SystemObject : operandType;
                    stackKind = GenStackKind.Ref;
                    break;

                case BytecodeOp.Box:
                    operandType = ResolveType(ins.Operand0);
                    MarkInstantiatedType(operandType);
                    type = _rts.SystemObject;
                    stackKind = GenStackKind.Ref;
                    break;

                case BytecodeOp.UnboxAny:
                    operandType = ResolveType(ins.Operand0);
                    type = operandType;
                    stackKind = StackKindOf(operandType);
                    break;
            }

            PushImportedValue(stack, statements, Node(kind, pc, ins.Op, type: type, stackKind: stackKind, operands: One(value.Node), int32: ins.Operand0, runtimeType: operandType));
        }

        private void EmitBinary(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var right = Pop(stack, pc, ins.Op);
            var left = Pop(stack, pc, ins.Op);

            RuntimeType? type = left.Type;
            GenStackKind stackKind = left.StackKind;
            GenTreeKind kind = GenTreeKind.Binary;
            RuntimeType? runtimeType = null;

            switch (ins.Op)
            {
                case BytecodeOp.Ceq:
                case BytecodeOp.Clt:
                case BytecodeOp.Clt_Un:
                case BytecodeOp.Cgt:
                case BytecodeOp.Cgt_Un:
                    type = null;
                    stackKind = GenStackKind.I4;
                    break;

                case BytecodeOp.PtrElemAddr:
                    kind = GenTreeKind.PointerElementAddr;
                    type = null;
                    stackKind = GenStackKind.Ptr;
                    break;

                case BytecodeOp.PtrDiff:
                    kind = GenTreeKind.PointerDiff;
                    type = null;
                    stackKind = GenStackKind.NativeInt;
                    break;
            }

            PushImportedValue(stack, statements, Node(kind, pc, ins.Op, type: type, stackKind: stackKind, operands: Two(left.Node, right.Node),
                int32: ins.Operand0, runtimeType: runtimeType));
        }

        private RuntimeType ObjectArrayType => _rts.GetArrayType(_rts.SystemObject);

        private void MarkInstantiatedType(RuntimeType? type)
        {
            if (type is null)
                return;

            if (type.Kind is RuntimeTypeKind.Class or RuntimeTypeKind.Struct or RuntimeTypeKind.Enum or RuntimeTypeKind.Array)
                _instantiatedTypes.TryAdd(type.TypeId, type);
        }

        private GenTemp MaterializeImporterValue(List<GenTree> statements, int pc, BytecodeOp sourceOp, GenTree value)
        {
            var temp = CreateImporterSpillTemp(value.Type, value.StackKind);
            statements.Add(Node(GenTreeKind.StoreTemp, pc, sourceOp, operands: One(value), int32: temp.Index));
            return temp;
        }

        private GenTree ConstI4(int pc, BytecodeOp sourceOp, int value)
            => Node(GenTreeKind.ConstI4, pc, sourceOp, stackKind: GenStackKind.I4, int32: value);

        private GenTree BoxIfNeeded(int pc, BytecodeOp sourceOp, RuntimeType valueType, GenTree value)
        {
            if (!valueType.IsValueType)
                return value;

            MarkInstantiatedType(valueType);
            return Node(GenTreeKind.Box, pc, BytecodeOp.Box, type: _rts.SystemObject, stackKind: GenStackKind.Ref,
                operands: One(value), int32: valueType.TypeId, runtimeType: valueType);
        }

        private GenTree UnboxOrCastCellValue(int pc, BytecodeOp sourceOp, RuntimeType valueType, GenTree boxed)
        {
            if (valueType.IsValueType)
            {
                return Node(GenTreeKind.UnboxAny, pc, BytecodeOp.UnboxAny, type: valueType, stackKind: StackKindOf(valueType),
                    operands: One(boxed), int32: valueType.TypeId, runtimeType: valueType);
            }

            if (ReferenceEquals(valueType, _rts.SystemObject))
                return boxed;

            return Node(GenTreeKind.CastClass, pc, BytecodeOp.CastClass, type: valueType, stackKind: GenStackKind.Ref,
                operands: One(boxed), int32: valueType.TypeId, runtimeType: valueType);
        }

        private GenTemp AllocateObjectArrayTemp(List<GenTree> statements, int pc, BytecodeOp sourceOp, int length)
        {
            var len = ConstI4(pc, sourceOp, length);
            var objectArrayType = ObjectArrayType;
            MarkInstantiatedType(objectArrayType);
            var array = Node(GenTreeKind.NewArray, pc, BytecodeOp.Newarr, type: objectArrayType, stackKind: GenStackKind.Ref,
                operands: One(len), runtimeType: _rts.SystemObject);
            return MaterializeImporterValue(statements, pc, sourceOp, array);
        }

        private void StoreObjectArrayElement(List<GenTree> statements, int pc, BytecodeOp sourceOp, GenTree array, int index, GenTree value)
        {
            statements.Add(Node(GenTreeKind.StoreArrayElement, pc, BytecodeOp.Stelem,
                operands: ImmutableArray.Create(array, ConstI4(pc, sourceOp, index), value),
                runtimeType: _rts.SystemObject));
        }

        private GenTree LoadObjectArrayElement(int pc, BytecodeOp sourceOp, GenTree array, int index)
            => Node(GenTreeKind.ArrayElement, pc, BytecodeOp.Ldelem, type: _rts.SystemObject, stackKind: GenStackKind.Ref,
                operands: Two(array, ConstI4(pc, sourceOp, index)), runtimeType: _rts.SystemObject);

        private void EmitNewClosureCell(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var initial = Pop(stack, pc, ins.Op);
            var valueType = ResolveType(ins.Operand0);

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            GenTree stored = BoxIfNeeded(pc, ins.Op, valueType, initial.Node);
            if (stored.Kind == GenTreeKind.Box)
                stored = TempLoad(pc, ins.Op, MaterializeImporterValue(statements, pc, ins.Op, stored)).Node;

            var cellTemp = AllocateObjectArrayTemp(statements, pc, ins.Op, 1);
            StoreObjectArrayElement(statements, pc, ins.Op, TempLoad(pc, ins.Op, cellTemp).Node, 0, stored);

            Push(stack, new StackValue(TempLoad(pc, ins.Op, cellTemp).Node, _rts.SystemObject, GenStackKind.Ref));
        }

        private void EmitLoadClosureCell(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var cell = Pop(stack, pc, ins.Op);
            var valueType = ResolveType(ins.Operand0);
            var boxed = LoadObjectArrayElement(pc, ins.Op, cell.Node, 0);
            PushImportedValue(stack, statements, UnboxOrCastCellValue(pc, ins.Op, valueType, boxed));
        }

        private void EmitStoreClosureCell(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var value = Pop(stack, pc, ins.Op);
            var cell = Pop(stack, pc, ins.Op);
            var valueType = ResolveType(ins.Operand0);

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            GenTree stored = BoxIfNeeded(pc, ins.Op, valueType, value.Node);
            if (stored.Kind == GenTreeKind.Box)
                stored = TempLoad(pc, ins.Op, MaterializeImporterValue(statements, pc, ins.Op, stored)).Node;

            StoreObjectArrayElement(statements, pc, ins.Op, cell.Node, 0, stored);
        }

        private void EmitNewClosure(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            int count = ins.Operand0;
            var cells = PopMany(stack, count, pc, ins.Op);

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            var closureTemp = AllocateObjectArrayTemp(statements, pc, ins.Op, count);
            for (int i = 0; i < cells.Length; i++)
                StoreObjectArrayElement(statements, pc, ins.Op, TempLoad(pc, ins.Op, closureTemp).Node, i, cells[i]);

            Push(stack, new StackValue(TempLoad(pc, ins.Op, closureTemp).Node, _rts.SystemObject, GenStackKind.Ref));
        }

        private void EmitLoadClosureSlot(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var closure = Pop(stack, pc, ins.Op);
            PushImportedValue(stack, statements, LoadObjectArrayElement(pc, ins.Op, closure.Node, ins.Operand0));
        }

        private void EmitNewDelegate(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var delegateType = ResolveType(ins.Operand0);
            MarkInstantiatedType(delegateType);
            var targetMethod = _rts.ResolveMethodInMethodContext(_module, ins.Operand1, _method);
            AddDirectDependency(targetMethod);
            if (targetMethod.IsStatic && !StringComparer.Ordinal.Equals(targetMethod.Name, ".cctor"))
                AddTypeInitializerDependency(targetMethod.DeclaringType);

            ImmutableArray<GenTree> operands;
            if (ins.Op == BytecodeOp.NewDelegateClosed)
                operands = One(Pop(stack, pc, ins.Op).Node);
            else
                operands = ImmutableArray<GenTree>.Empty;

            PushImportedValue(stack, statements, Node(GenTreeKind.NewDelegate, pc, ins.Op, type: delegateType, stackKind: GenStackKind.Ref,
                operands: operands, int64: targetMethod.MethodId, runtimeType: delegateType, method: targetMethod));
        }

        private void EmitDelegateBinary(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var args = PopMany(stack, 2, pc, ins.Op);
            RuntimeType? resultType = args[0].Type ?? args[1].Type;
            PushImportedValue(stack, statements, Node(
                ins.Op == BytecodeOp.DelegateCombine ? GenTreeKind.DelegateCombine : GenTreeKind.DelegateRemove,
                pc,
                ins.Op,
                type: resultType,
                stackKind: GenStackKind.Ref,
                operands: args));
        }

        private void EmitDelegateInvoke(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            int argCount = ins.Operand1;
            var args = PopMany(stack, checked(argCount + 1), pc, ins.Op);
            var invoke = _rts.ResolveMethodInMethodContext(_module, ins.Operand0, _method);

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            bool returnsVoid = IsVoid(invoke.ReturnType);
            var call = Node(GenTreeKind.DelegateInvoke,
                pc,
                ins.Op,
                type: returnsVoid ? null : invoke.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(invoke.ReturnType),
                operands: args,
                int32: args.Length,
                int64: ins.Operand0,
                method: invoke);

            if (returnsVoid)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, ins.Op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);
        }

        private void EmitCall(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins, bool isVirtual)
        {
            int packed = ins.Operand1;
            int argCount = packed & 0x7FFF;
            int hasThis = (packed >> 15) & 1;
            int total = argCount + hasThis;

            var args = PopMany(stack, total, pc, ins.Op);
            var method = _rts.ResolveMethodInMethodContext(_module, ins.Operand0, _method);
            if (isVirtual)
            {
                AddVirtualDependency(method);
            }
            else
            {
                AddDirectDependency(method);
                if (method.IsStatic && !StringComparer.Ordinal.Equals(method.Name, ".cctor"))
                    AddTypeInitializerDependency(method.DeclaringType);
            }

            SpillEvaluationStackForImportBarrier(statements, stack, pc, ins.Op);

            if (!isVirtual && TryInlineCall(method, args, statements, pc, ins.Op, out var inlineResult))
            {
                if (inlineResult is not null)
                    Push(stack, inlineResult);
                return;
            }

            bool returnsVoid = IsVoid(method.ReturnType);
            var call = Node(isVirtual ? GenTreeKind.VirtualCall : GenTreeKind.Call,
                pc,
                ins.Op,
                type: returnsVoid ? null : method.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(method.ReturnType),
                operands: args,
                int32: total,
                int64: ins.Operand0,
                method: method);

            if (returnsVoid)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, ins.Op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);
        }

        private void EmitNewObject(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            int argCount = ins.Operand1;
            var args = PopMany(stack, argCount, pc, ins.Op);
            var ctor = _rts.ResolveMethodInMethodContext(_module, ins.Operand0, _method);
            AddDirectDependency(ctor);
            AddTypeInitializerDependency(ctor.DeclaringType);

            var t = ctor.DeclaringType;
            MarkInstantiatedType(t);

            if (t.IsValueType)
            {
                EmitValueTypeNewObject(stack, statements, pc, ins.Op, ins.Operand0, argCount, args, ctor, t);
                return;
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.NewObject, pc, ins.Op, type: t, stackKind: StackKindOf(t), operands: args,
                int32: argCount, int64: ins.Operand0, method: ctor, runtimeType: t));
        }

        private void EmitValueTypeNewObject(
            List<StackValue> stack,
            List<GenTree> statements,
            int pc,
            BytecodeOp sourceOp,
            int methodToken,
            int userArgCount,
            ImmutableArray<GenTree> userArgs,
            RuntimeMethod ctor,
            RuntimeType valueType)
        {
            if (!valueType.IsValueType)
                throw Fail(pc, sourceOp, "Value-type materialization requires a value-type constructor.");

            SpillEvaluationStackForImportBarrier(statements, stack, pc, sourceOp);

            var temp = CreateStructMaterializationTemp(valueType);
            EmitStructDefaultInitialization(statements, stack, pc, sourceOp, temp, valueType);

            var ctorArgsBuilder = ImmutableArray.CreateBuilder<GenTree>(userArgCount + 1);
            ctorArgsBuilder.Add(TempAddress(pc, sourceOp, temp).Node);
            ctorArgsBuilder.AddRange(userArgs);
            var ctorArgs = ctorArgsBuilder.ToImmutable();

            if (TryInlineCall(ctor, ctorArgs, statements, pc, sourceOp, out var inlineResult))
            {
                if (inlineResult is not null)
                    AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, sourceOp, operands: One(inlineResult)));

                Push(stack, TempLoad(pc, sourceOp, temp));
                return;
            }

            var call = Node(GenTreeKind.Call, pc, sourceOp, stackKind: GenStackKind.Void, operands: ctorArgs,
                int32: userArgCount + 1, int64: methodToken, method: ctor);
            AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, pc, sourceOp, operands: One(call)));
            Push(stack, TempLoad(pc, sourceOp, temp));
        }

        private void EmitStructDefaultInitialization(List<GenTree> statements, List<StackValue> stack, int pc, BytecodeOp sourceOp, GenTemp temp, RuntimeType valueType)
        {
            if (TryEmitFieldWiseStructDefaultInitialization(statements, stack, pc, sourceOp, temp, valueType))
                return;

            var init = Node(GenTreeKind.DefaultValue, pc, BytecodeOp.DefaultValue, type: valueType, stackKind: StackKindOf(valueType), runtimeType: valueType);
            AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreTemp, pc, BytecodeOp.Stloc, operands: One(init), int32: temp.Index));
        }

        private bool TryEmitFieldWiseStructDefaultInitialization(List<GenTree> statements, List<StackValue> stack, int pc, BytecodeOp sourceOp, GenTemp temp, RuntimeType valueType)
        {
            if (valueType.Kind != RuntimeTypeKind.Struct)
                return false;

            if (valueType.InstanceFields.Length == 0)
                return true;

            if (!CanExpandStructFieldWise(valueType))
                return false;

            var fields = valueType.InstanceFields;
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (field.IsStatic)
                    continue;

                var fieldDefault = Node(GenTreeKind.DefaultValue, pc, BytecodeOp.DefaultValue, type: field.FieldType, stackKind: StackKindOf(field.FieldType), runtimeType: field.FieldType);
                AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, pc, sourceOp,
                    operands: Two(TempAddress(pc, sourceOp, temp).Node, fieldDefault), field: field, runtimeType: field.FieldType));
            }

            return true;
        }

        private bool TryInlineCall(
            RuntimeMethod callee,
            ImmutableArray<GenTree> args,
            List<GenTree> statements,
            int callPc,
            BytecodeOp callOp,
            out GenTree? result,
            int inlineDepth = 1)
        {
            result = null;

            var body = callee.Body;
            var bodyModule = callee.BodyModule;
            if (body is null || bodyModule is null)
                return false;

            if (PcInExceptionHandlerRegion(callPc))
                return false;

            if (!CanInline(callee, bodyModule, body, args, inlineDepth, out var inlineInfo))
                return false;

            var calleeArgTypes = BuildArgTypes(callee);
            if (calleeArgTypes.Length != args.Length)
                return false;

            bool registeredActiveInline = _activeInlineMethods.Add(callee.MethodId);
            if (!registeredActiveInline)
                return false;

            _inlineBudgetRemaining = Math.Max(0, _inlineBudgetRemaining - inlineInfo.Cost);

            try
            {
                var argTemps = new GenTemp[calleeArgTypes.Length];
                var argSubstitutions = new StackValue?[calleeArgTypes.Length];
                for (int i = 0; i < argTemps.Length; i++)
                {
                    var t = calleeArgTypes[i];
                    if (inlineInfo.CanSubstituteArgument(i, args[i]))
                    {
                        argSubstitutions[i] = new StackValue(args[i], args[i].Type, args[i].StackKind);
                        continue;
                    }

                    var temp = CreateInlineTemp(GenTempKind.InlineArg, t, StackKindOf(t));
                    argTemps[i] = temp;
                    statements.Add(Node(GenTreeKind.StoreTemp, callPc, callOp, operands: One(args[i]), int32: temp.Index));
                }

                var localTypes = BuildInlineLocalTypes(bodyModule, body, callee);
                var localTemps = new GenTemp[localTypes.Length];
                for (int i = 0; i < localTypes.Length; i++)
                {
                    var t = localTypes[i];
                    var temp = CreateInlineTemp(GenTempKind.InlineLocal, t, StackKindOf(t));
                    localTemps[i] = temp;

                    if (inlineInfo.LocalNeedsInit(i))
                    {
                        var init = Node(GenTreeKind.DefaultValue, callPc, BytecodeOp.DefaultValue, type: t, stackKind: StackKindOf(t), runtimeType: t);
                        statements.Add(Node(GenTreeKind.StoreTemp, callPc, BytecodeOp.Stloc, operands: One(init), int32: temp.Index));
                    }
                }

                var inlineStack = new List<StackValue>(Math.Max(body.MaxStack, 4));
                bool sawReturn = false;

                for (int pc = 0; pc < body.Instructions.Length; pc++)
                {
                    var ins = body.Instructions[pc];
                    if (ins.Op == BytecodeOp.Ret)
                    {
                        if (ins.Pop == 1)
                        {
                            var returnValue = Pop(inlineStack, callPc, ins.Op);
                            result = returnValue.Node;
                        }
                        else
                        {
                            result = null;
                        }

                        sawReturn = true;
                        for (int tail = pc + 1; tail < body.Instructions.Length; tail++)
                        {
                            if (body.Instructions[tail].Op != BytecodeOp.Nop)
                                throw Fail(callPc, body.Instructions[tail].Op, "Unexpected non-nop after inlined return.");
                        }
                        break;
                    }

                    switch (ins.Op)
                    {
                        case BytecodeOp.Nop:
                            break;

                        case BytecodeOp.Ldc_I4:
                            Push(inlineStack, Node(GenTreeKind.ConstI4, callPc, ins.Op, stackKind: GenStackKind.I4, int32: ins.Operand0));
                            break;

                        case BytecodeOp.Ldc_I8:
                            Push(inlineStack, Node(GenTreeKind.ConstI8, callPc, ins.Op, stackKind: GenStackKind.I8, int64: ins.Operand2));
                            break;

                        case BytecodeOp.Ldc_R4:
                            Push(inlineStack, Node(GenTreeKind.ConstR4Bits, callPc, ins.Op, stackKind: GenStackKind.R4, int32: ins.Operand0));
                            break;

                        case BytecodeOp.Ldc_R8:
                            Push(inlineStack, Node(GenTreeKind.ConstR8Bits, callPc, ins.Op, stackKind: GenStackKind.R8, int64: ins.Operand2));
                            break;

                        case BytecodeOp.Ldnull:
                            Push(inlineStack, Node(GenTreeKind.ConstNull, callPc, ins.Op, stackKind: GenStackKind.Null));
                            break;

                        case BytecodeOp.Ldstr:
                            Push(inlineStack, Node(GenTreeKind.ConstString, callPc, ins.Op, type: _rts.SystemString, stackKind: GenStackKind.Ref,
                                int32: ins.Operand0, text: bodyModule.Md.GetUserString(MetadataToken.Rid(ins.Operand0))));
                            break;

                        case BytecodeOp.DefaultValue:
                            {
                                var t = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                Push(inlineStack, Node(GenTreeKind.DefaultValue, callPc, ins.Op, type: t, stackKind: StackKindOf(t), runtimeType: t));
                                break;
                            }

                        case BytecodeOp.Sizeof:
                            {
                                var t = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                Push(inlineStack, Node(GenTreeKind.SizeOf, callPc, ins.Op, stackKind: GenStackKind.I4, runtimeType: t));
                                break;
                            }

                        case BytecodeOp.TypeIsValueType:
                            {
                                var t = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                Push(inlineStack, Node(GenTreeKind.ConstI4, callPc, ins.Op, stackKind: GenStackKind.I4,
                                    int32: RuntimeTypeIsValueType(t, callPc, ins.Op) ? 1 : 0));
                                break;
                            }

                        case BytecodeOp.Ldarg:
                            Push(inlineStack, LoadInlineArg(argTemps, argSubstitutions, ins.Operand0, callPc, ins.Op));
                            break;

                        case BytecodeOp.Ldarga:
                            Push(inlineStack, TempAddress(callPc, ins.Op, CheckedInlineArgTemp(argTemps, ins.Operand0, callPc, ins.Op)));
                            break;

                        case BytecodeOp.Ldthis:
                            Push(inlineStack, LoadInlineArg(argTemps, argSubstitutions, 0, callPc, ins.Op));
                            break;

                        case BytecodeOp.Starg:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var temp = CheckedInlineArgTemp(argTemps, ins.Operand0, callPc, ins.Op);
                                AppendLocalLikeStore(statements, inlineStack, callPc, ins.Op, GenTreeKind.StoreTemp, GenTreeKind.TempAddr, temp.Index, temp.Type, value.Node);
                                break;
                            }

                        case BytecodeOp.Ldloc:
                            Push(inlineStack, TempLoad(callPc, ins.Op, CheckedInlineLocalTemp(localTemps, ins.Operand0, callPc, ins.Op)));
                            break;

                        case BytecodeOp.Ldloca:
                            Push(inlineStack, TempAddress(callPc, ins.Op, CheckedInlineLocalTemp(localTemps, ins.Operand0, callPc, ins.Op)));
                            break;

                        case BytecodeOp.Stloc:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var temp = CheckedInlineLocalTemp(localTemps, ins.Operand0, callPc, ins.Op);
                                AppendLocalLikeStore(statements, inlineStack, callPc, ins.Op, GenTreeKind.StoreTemp, GenTreeKind.TempAddr, temp.Index, temp.Type, value.Node);
                                break;
                            }

                        case BytecodeOp.Pop:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                AppendImporterStatement(statements, inlineStack, Node(GenTreeKind.Eval, callPc, ins.Op, operands: One(value.Node)));
                                break;
                            }

                        case BytecodeOp.Dup:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var temp = CreateDupTemp(value.Type, value.StackKind);
                                AppendImporterStatement(statements, inlineStack, Node(GenTreeKind.StoreTemp, callPc, ins.Op, operands: One(value.Node), int32: temp.Index));
                                Push(inlineStack, TempLoad(callPc, ins.Op, temp));
                                Push(inlineStack, TempLoad(callPc, ins.Op, temp));
                                break;
                            }

                        case BytecodeOp.Neg:
                        case BytecodeOp.Not:
                        case BytecodeOp.PtrToByRef:
                        case BytecodeOp.CastClass:
                        case BytecodeOp.Isinst:
                        case BytecodeOp.Box:
                        case BytecodeOp.UnboxAny:
                            EmitInlineUnary(inlineStack, statements, bodyModule, callee, callPc, ins);
                            break;

                        case BytecodeOp.Add:
                        case BytecodeOp.Add_Ovf:
                        case BytecodeOp.Add_Ovf_Un:
                        case BytecodeOp.Sub:
                        case BytecodeOp.Sub_Ovf:
                        case BytecodeOp.Sub_Ovf_Un:
                        case BytecodeOp.Mul:
                        case BytecodeOp.Mul_Ovf:
                        case BytecodeOp.Mul_Ovf_Un:
                        case BytecodeOp.Div:
                        case BytecodeOp.Div_Un:
                        case BytecodeOp.Rem:
                        case BytecodeOp.Rem_Un:
                        case BytecodeOp.And:
                        case BytecodeOp.Or:
                        case BytecodeOp.Xor:
                        case BytecodeOp.Shl:
                        case BytecodeOp.Shr:
                        case BytecodeOp.Shr_Un:
                        case BytecodeOp.Ceq:
                        case BytecodeOp.Clt:
                        case BytecodeOp.Clt_Un:
                        case BytecodeOp.Cgt:
                        case BytecodeOp.Cgt_Un:
                        case BytecodeOp.PtrElemAddr:
                        case BytecodeOp.PtrDiff:
                            EmitInlineBinary(inlineStack, statements, callPc, ins);
                            break;

                        case BytecodeOp.Conv:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var stackKind = StackKindOf((NumericConvKind)ins.Operand0);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.Conv, callPc, ins.Op, stackKind: stackKind, operands: One(value.Node),
                                    convKind: (NumericConvKind)ins.Operand0, convFlags: (NumericConvFlags)ins.Operand1));
                                break;
                            }

                        case BytecodeOp.Call:
                        case BytecodeOp.CallVirt:
                            EmitInlineCall(inlineStack, statements, bodyModule, callee, callPc, ins, inlineDepth);
                            break;

                        case BytecodeOp.Newobj:
                            EmitInlineNewObject(inlineStack, statements, bodyModule, callee, callPc, ins);
                            break;

                        case BytecodeOp.Ldfld:
                        case BytecodeOp.Ldflda:
                        case BytecodeOp.Stfld:
                        case BytecodeOp.Ldsfld:
                        case BytecodeOp.Ldsflda:
                        case BytecodeOp.Stsfld:
                            EmitInlineField(inlineStack, statements, bodyModule, callee, callPc, ins);
                            break;

                        case BytecodeOp.Ldobj:
                            {
                                var address = Pop(inlineStack, callPc, ins.Op);
                                var t = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.LoadIndirect, callPc, ins.Op, type: t, stackKind: StackKindOf(t), operands: One(address.Node), runtimeType: t));
                                break;
                            }

                        case BytecodeOp.Stobj:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var address = Pop(inlineStack, callPc, ins.Op);
                                var t = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                if (!TryRetargetStructMaterializationToAddress(statements, callPc, ins.Op, address.Node, t, value.Node))
                                    AppendImporterStatement(statements, inlineStack, Node(GenTreeKind.StoreIndirect, callPc, ins.Op, operands: Two(address.Node, value.Node), runtimeType: t));
                                break;
                            }

                        case BytecodeOp.Newarr:
                            {
                                var length = Pop(inlineStack, callPc, ins.Op);
                                var elemType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                var arrayType = _rts.GetArrayType(elemType);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.NewArray, callPc, ins.Op, type: arrayType, stackKind: GenStackKind.Ref,
                                    operands: One(length.Node), runtimeType: elemType));
                                break;
                            }

                        case BytecodeOp.Ldelem:
                            {
                                var index = Pop(inlineStack, callPc, ins.Op);
                                var array = Pop(inlineStack, callPc, ins.Op);
                                var elemType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.ArrayElement, callPc, ins.Op, type: elemType, stackKind: StackKindOf(elemType),
                                    operands: Two(array.Node, index.Node), runtimeType: elemType));
                                break;
                            }

                        case BytecodeOp.Ldelema:
                            {
                                var index = Pop(inlineStack, callPc, ins.Op);
                                var array = Pop(inlineStack, callPc, ins.Op);
                                var elemType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                var byRef = _rts.GetByRefType(elemType);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.ArrayElementAddr, callPc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                                    operands: Two(array.Node, index.Node), runtimeType: elemType));
                                break;
                            }

                        case BytecodeOp.Stelem:
                            {
                                var value = Pop(inlineStack, callPc, ins.Op);
                                var index = Pop(inlineStack, callPc, ins.Op);
                                var array = Pop(inlineStack, callPc, ins.Op);
                                var elemType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                                AppendImporterStatement(statements, inlineStack, Node(GenTreeKind.StoreArrayElement, callPc, ins.Op,
                                    operands: ImmutableArray.Create(array.Node, index.Node, value.Node), runtimeType: elemType));
                                break;
                            }

                        case BytecodeOp.LdArrayDataRef:
                            {
                                var array = Pop(inlineStack, callPc, ins.Op);
                                PushImportedValue(inlineStack, statements, Node(GenTreeKind.ArrayDataRef, callPc, ins.Op, stackKind: GenStackKind.ByRef, operands: One(array.Node)));
                                break;
                            }

                        case BytecodeOp.Br:
                        case BytecodeOp.Leave:
                        case BytecodeOp.Brtrue:
                        case BytecodeOp.Brfalse:
                        case BytecodeOp.Throw:
                        case BytecodeOp.Rethrow:
                        case BytecodeOp.Ldexception:
                        case BytecodeOp.Endfinally:
                        case BytecodeOp.StackAlloc:
                            throw Fail(callPc, ins.Op, "Opcode passed inline screening but has no inline translator.");

                        default:
                            throw Fail(callPc, ins.Op, "Opcode passed inline screening but has no inline translator.");
                    }
                }

                if (!sawReturn)
                    throw Fail(callPc, BytecodeOp.Ret, "Inline candidate has no return.");
                if (!IsVoid(callee.ReturnType) && result is null)
                    throw Fail(callPc, BytecodeOp.Ret, "Inline candidate returned no value for a non-void method.");
                return true;
            }
            finally
            {
                _activeInlineMethods.Remove(callee.MethodId);
            }
        }

        private bool CanInline(
            RuntimeMethod callee,
            RuntimeModule bodyModule,
            BytecodeFunction body,
            ImmutableArray<GenTree> args,
            int inlineDepth,
            out InlineCandidateInfo info)
        {
            info = null!;

            if (callee.MethodId == _method.MethodId)
                return false;
            if (_activeInlineMethods.Contains(callee.MethodId))
                return false;
            if (callee.HasInternalCall || callee.HasNoInlining)
                return false;
            if (body.ExceptionHandlers.Length != 0)
                return false;
            if (args.Length != (callee.HasThis ? callee.ParameterTypes.Length + 1 : callee.ParameterTypes.Length))
                return false;
            if (body.LocalTypeTokens.Length > (callee.HasAggressiveInlining ? 64 : 24))
                return false;
            if (!callee.HasAggressiveInlining && body.MaxStack > 32)
                return false;
            if (inlineDepth > InlineMaxDepth)
                return false;

            if (!AnalyzeInlineCandidate(callee, bodyModule, body, args.Length, out var candidate))
                return false;

            int budget = DetermineInlineBudget(callee, candidate, args, inlineDepth);
            bool forceInline = callee.HasAggressiveInlining;
            bool allowOverBudget = forceInline && inlineDepth <= InlineMaxForceDepth;
            if (!allowOverBudget && candidate.CodeSize <= InlineSmallOverBudgetSize)
                allowOverBudget = true;

            if (candidate.Cost > budget && !allowOverBudget)
                return false;

            if (candidate.Cost > _inlineBudgetRemaining && !allowOverBudget)
                return false;

            info = candidate;
            return true;
        }

        private int DetermineInlineBudget(RuntimeMethod callee, InlineCandidateInfo candidate, ImmutableArray<GenTree> args, int inlineDepth)
        {
            int budget = callee.HasAggressiveInlining
                ? InlineForceBudget
                : candidate.CodeSize <= InlineSmallOverBudgetSize ? InlineAlwaysBudget : InlineDiscretionaryBudget;

            if (candidate.LooksLikeWrapper)
                budget += 32;
            if (candidate.MostlyLoadStore)
                budget += 32;
            if (candidate.HasCall)
                budget += candidate.LooksLikeWrapper ? 16 : 8;

            int substitutableArgs = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (candidate.CanSubstituteArgument(i, args[i]))
                    substitutableArgs++;
            }
            budget += Math.Min(24, substitutableArgs * 4);

            if (inlineDepth > 1)
                budget = Math.Max(InlineAlwaysBudget, budget - ((inlineDepth - 1) * 12));

            return budget;
        }

        private bool AnalyzeInlineCandidate(
            RuntimeMethod callee,
            RuntimeModule bodyModule,
            BytecodeFunction body,
            int argCount,
            out InlineCandidateInfo info)
        {
            info = null!;

            var argLoadCounts = new int[argCount];
            var argStoreCounts = new int[argCount];
            var argAddressCounts = new int[argCount];
            var localAddressCounts = new int[body.LocalTypeTokens.Length];
            var localNeedsInit = new bool[body.LocalTypeTokens.Length];
            var localDefinitelyAssigned = new bool[body.LocalTypeTokens.Length];

            int cost = 0;
            int instructionCount = 0;
            int loadStoreCount = 0;
            int callCount = 0;
            bool sawReturn = false;
            bool returnsValue = false;

            for (int i = 0; i < body.Instructions.Length; i++)
            {
                var ins = body.Instructions[i];
                if (sawReturn)
                {
                    if (ins.Op != BytecodeOp.Nop)
                        return false;
                    continue;
                }

                if (ins.Op == BytecodeOp.Ret)
                {
                    sawReturn = true;
                    returnsValue = ins.Pop == 1;
                    cost++;
                    instructionCount++;
                    continue;
                }

                if (!CanTranslateInlineOpcode(ins.Op))
                    return false;

                if (!NoteInlineOperandUse(ins, argLoadCounts, argStoreCounts, argAddressCounts, localAddressCounts, localNeedsInit, localDefinitelyAssigned))
                    return false;

                instructionCount++;
                if (IsInlineLoadStoreOpcode(ins.Op))
                    loadStoreCount++;
                if (ins.Op is BytecodeOp.Call or BytecodeOp.CallVirt)
                    callCount++;

                cost += InlineOpcodeCost(ins.Op);
                if (ins.Op == BytecodeOp.CallVirt)
                    cost += 4;
            }

            if (!sawReturn)
                return false;

            bool mostlyLoadStore = instructionCount != 0 &&
                ((instructionCount - loadStoreCount) < 4 || (loadStoreCount * 10) >= instructionCount * 9);
            bool looksLikeWrapper = callCount == 1 && instructionCount <= 8;

            info = new InlineCandidateInfo(
                cost,
                body.Instructions.Length,
                argLoadCounts,
                argStoreCounts,
                argAddressCounts,
                localAddressCounts,
                localNeedsInit,
                mostlyLoadStore,
                looksLikeWrapper,
                hasCall: callCount != 0,
                returnsValue: returnsValue);
            return true;
        }

        private static bool NoteInlineOperandUse(
            Instruction ins,
            int[] argLoadCounts,
            int[] argStoreCounts,
            int[] argAddressCounts,
            int[] localAddressCounts,
            bool[] localNeedsInit,
            bool[] localDefinitelyAssigned)
        {
            switch (ins.Op)
            {
                case BytecodeOp.Ldthis:
                    if (argLoadCounts.Length == 0)
                        return false;
                    argLoadCounts[0]++;
                    return true;

                case BytecodeOp.Ldarg:
                    if ((uint)ins.Operand0 >= (uint)argLoadCounts.Length)
                        return false;
                    argLoadCounts[ins.Operand0]++;
                    return true;

                case BytecodeOp.Ldarga:
                    if ((uint)ins.Operand0 >= (uint)argAddressCounts.Length)
                        return false;
                    argAddressCounts[ins.Operand0]++;
                    return true;

                case BytecodeOp.Starg:
                    if ((uint)ins.Operand0 >= (uint)argStoreCounts.Length)
                        return false;
                    argStoreCounts[ins.Operand0]++;
                    return true;

                case BytecodeOp.Ldloc:
                    if ((uint)ins.Operand0 >= (uint)localNeedsInit.Length)
                        return false;
                    if (!localDefinitelyAssigned[ins.Operand0])
                        localNeedsInit[ins.Operand0] = true;
                    return true;

                case BytecodeOp.Ldloca:
                    if ((uint)ins.Operand0 >= (uint)localAddressCounts.Length)
                        return false;
                    localAddressCounts[ins.Operand0]++;
                    if (!localDefinitelyAssigned[ins.Operand0])
                        localNeedsInit[ins.Operand0] = true;
                    return true;

                case BytecodeOp.Stloc:
                    if ((uint)ins.Operand0 >= (uint)localDefinitelyAssigned.Length)
                        return false;
                    localDefinitelyAssigned[ins.Operand0] = true;
                    return true;

                default:
                    return true;
            }
        }

        private StackValue LoadInlineArg(GenTemp[] argTemps, StackValue?[] argSubstitutions, int index, int pc, BytecodeOp op)
        {
            if ((uint)index >= (uint)argTemps.Length)
                throw Fail(pc, op, $"Inline argument index {index} is out of range. Argument count: {argTemps.Length}.");

            if (argSubstitutions[index].HasValue)
                return argSubstitutions[index]!.Value;

            return TempLoad(pc, op, CheckedInlineArgTemp(argTemps, index, pc, op));
        }

        private static bool IsInlineLoadStoreOpcode(BytecodeOp op)
        {
            return op is BytecodeOp.Ldarg or BytecodeOp.Ldarga or BytecodeOp.Ldthis or BytecodeOp.Ldloc or
                         BytecodeOp.Ldc_I8 or BytecodeOp.Ldc_R4 or BytecodeOp.Ldc_R8 or BytecodeOp.Ldnull or
                         BytecodeOp.Ldstr or BytecodeOp.DefaultValue or BytecodeOp.Sizeof or BytecodeOp.TypeIsValueType or
                         BytecodeOp.Starg or BytecodeOp.Stloc or BytecodeOp.Ldfld or BytecodeOp.Ldflda or
                         BytecodeOp.Ldsfld or BytecodeOp.Ldsflda or BytecodeOp.Ldobj or BytecodeOp.Stobj or
                         BytecodeOp.Ldelem or BytecodeOp.Ldelema or BytecodeOp.Stelem or BytecodeOp.Pop or
                         BytecodeOp.Ldloca or BytecodeOp.Ldc_I4;
        }

        private static bool IsPureInlineArgument(GenTree arg)
        {
            const GenTreeFlags badFlags =
                GenTreeFlags.ContainsCall |
                GenTreeFlags.CanThrow |
                GenTreeFlags.SideEffect |
                GenTreeFlags.MemoryRead |
                GenTreeFlags.MemoryWrite |
                GenTreeFlags.GlobalRef |
                GenTreeFlags.Indirect |
                GenTreeFlags.Allocation |
                GenTreeFlags.ControlFlow |
                GenTreeFlags.ExceptionFlow |
                GenTreeFlags.Ordered |
                GenTreeFlags.AddressExposed;

            var disallowed = badFlags;
            if (arg.Kind == GenTreeKind.TempAddr)
                disallowed &= ~GenTreeFlags.AddressExposed;

            if ((arg.Flags & disallowed) != 0)
                return false;

            return arg.Kind is GenTreeKind.ConstI4 or GenTreeKind.ConstI8 or GenTreeKind.ConstR4Bits or GenTreeKind.ConstR8Bits or
                GenTreeKind.ConstNull or GenTreeKind.ConstString or GenTreeKind.Local or GenTreeKind.Arg or GenTreeKind.Temp or GenTreeKind.TempAddr or
                GenTreeKind.DefaultValue or GenTreeKind.SizeOf or GenTreeKind.Unary or GenTreeKind.Binary or GenTreeKind.Conv;
        }

        private sealed class InlineCandidateInfo
        {
            private readonly int[] _argLoadCounts;
            private readonly int[] _argStoreCounts;
            private readonly int[] _argAddressCounts;
            private readonly int[] _localAddressCounts;
            private readonly bool[] _localNeedsInit;

            public int Cost { get; }
            public int CodeSize { get; }
            public bool MostlyLoadStore { get; }
            public bool LooksLikeWrapper { get; }
            public bool HasCall { get; }
            public bool ReturnsValue { get; }

            public InlineCandidateInfo(
                int cost,
                int codeSize,
                int[] argLoadCounts,
                int[] argStoreCounts,
                int[] argAddressCounts,
                int[] localAddressCounts,
                bool[] localNeedsInit,
                bool mostlyLoadStore,
                bool looksLikeWrapper,
                bool hasCall,
                bool returnsValue)
            {
                Cost = cost;
                CodeSize = codeSize;
                _argLoadCounts = argLoadCounts ?? Array.Empty<int>();
                _argStoreCounts = argStoreCounts ?? Array.Empty<int>();
                _argAddressCounts = argAddressCounts ?? Array.Empty<int>();
                _localAddressCounts = localAddressCounts ?? Array.Empty<int>();
                _localNeedsInit = localNeedsInit ?? Array.Empty<bool>();
                MostlyLoadStore = mostlyLoadStore;
                LooksLikeWrapper = looksLikeWrapper;
                HasCall = hasCall;
                ReturnsValue = returnsValue;
            }

            public bool CanSubstituteArgument(int index, GenTree arg)
            {
                if ((uint)index >= (uint)_argLoadCounts.Length ||
                    (uint)index >= (uint)_argStoreCounts.Length ||
                    (uint)index >= (uint)_argAddressCounts.Length)
                {
                    return false;
                }

                return _argStoreCounts[index] == 0 &&
                    _argAddressCounts[index] == 0 &&
                    _argLoadCounts[index] == 1 &&
                    IsPureInlineArgument(arg);
            }

            public bool LocalNeedsInit(int index)
                => (uint)index < (uint)_localNeedsInit.Length && _localNeedsInit[index];
        }

        private static bool CanTranslateInlineOpcode(BytecodeOp op)
        {
            return op switch
            {
                BytecodeOp.Nop or
                BytecodeOp.Pop or
                BytecodeOp.Dup or
                BytecodeOp.Ldnull or
                BytecodeOp.Ldc_I4 or
                BytecodeOp.Ldc_I8 or
                BytecodeOp.Ldc_R4 or
                BytecodeOp.Ldc_R8 or
                BytecodeOp.Ldstr or
                BytecodeOp.DefaultValue or
                BytecodeOp.Sizeof or
                BytecodeOp.TypeIsValueType or
                BytecodeOp.Ldloc or
                BytecodeOp.Stloc or
                BytecodeOp.Ldloca or
                BytecodeOp.Ldarg or
                BytecodeOp.Starg or
                BytecodeOp.Ldarga or
                BytecodeOp.Ldthis or
                BytecodeOp.Add or
                BytecodeOp.Add_Ovf or
                BytecodeOp.Add_Ovf_Un or
                BytecodeOp.Sub or
                BytecodeOp.Sub_Ovf or
                BytecodeOp.Sub_Ovf_Un or
                BytecodeOp.Mul or
                BytecodeOp.Mul_Ovf or
                BytecodeOp.Mul_Ovf_Un or
                BytecodeOp.Div or
                BytecodeOp.Div_Un or
                BytecodeOp.Rem or
                BytecodeOp.Rem_Un or
                BytecodeOp.And or
                BytecodeOp.Or or
                BytecodeOp.Xor or
                BytecodeOp.Shl or
                BytecodeOp.Shr or
                BytecodeOp.Shr_Un or
                BytecodeOp.Neg or
                BytecodeOp.Not or
                BytecodeOp.Ceq or
                BytecodeOp.Clt or
                BytecodeOp.Clt_Un or
                BytecodeOp.Cgt or
                BytecodeOp.Cgt_Un or
                BytecodeOp.Call or
                BytecodeOp.CallVirt or
                BytecodeOp.Newobj or
                BytecodeOp.Ldfld or
                BytecodeOp.Stfld or
                BytecodeOp.Ldsfld or
                BytecodeOp.Stsfld or
                BytecodeOp.Ldflda or
                BytecodeOp.Ldsflda or
                BytecodeOp.Conv or
                BytecodeOp.CastClass or
                BytecodeOp.Box or
                BytecodeOp.UnboxAny or
                BytecodeOp.Ldobj or
                BytecodeOp.Stobj or
                BytecodeOp.Newarr or
                BytecodeOp.Ldelem or
                BytecodeOp.Ldelema or
                BytecodeOp.Stelem or
                BytecodeOp.LdArrayDataRef or
                BytecodeOp.PtrElemAddr or
                BytecodeOp.PtrToByRef or
                BytecodeOp.PtrDiff or
                BytecodeOp.Isinst => true,
                _ => false,
            };
        }

        private static int InlineOpcodeCost(BytecodeOp op)
        {
            return op switch
            {
                BytecodeOp.Nop => 0,
                BytecodeOp.Ldarg or BytecodeOp.Ldthis or BytecodeOp.Ldloc or BytecodeOp.Ldc_I4 or BytecodeOp.Ldc_I8 or BytecodeOp.Ldc_R4 or BytecodeOp.Ldc_R8 or BytecodeOp.Ldnull => 1,
                BytecodeOp.Starg or BytecodeOp.Stloc or BytecodeOp.Dup => 2,
                BytecodeOp.Ldfld or BytecodeOp.Ldflda or BytecodeOp.Ldsfld or BytecodeOp.Ldsflda or BytecodeOp.Ldobj or BytecodeOp.Ldelem or BytecodeOp.Ldelema => 3,
                BytecodeOp.Stfld or BytecodeOp.Stsfld or BytecodeOp.Stobj or BytecodeOp.Stelem => 4,
                BytecodeOp.Newobj or BytecodeOp.Newarr or BytecodeOp.Box => 8,
                BytecodeOp.Call or BytecodeOp.CallVirt => 10,
                BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un => 4,
                _ => 1,
            };
        }

        private RuntimeType[] BuildInlineLocalTypes(RuntimeModule bodyModule, BytecodeFunction body, RuntimeMethod callee)
        {
            var result = new RuntimeType[body.LocalTypeTokens.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = _rts.ResolveTypeInMethodContext(bodyModule, body.LocalTypeTokens[i], callee);
            return result;
        }

        private RuntimeType ResolveTypeIn(RuntimeModule bodyModule, RuntimeMethod methodContext, int typeToken)
            => _rts.ResolveTypeInMethodContext(bodyModule, typeToken, methodContext);

        private GenTemp CreateInlineTemp(GenTempKind kind, RuntimeType? type, GenStackKind stackKind)
        {
            int index = _nextTempIndex++;
            var temp = new GenTemp(index, kind, type, stackKind);
            _temps.Add(temp);
            return temp;
        }

        private GenTemp CheckedInlineArgTemp(GenTemp[] temps, int index, int pc, BytecodeOp op)
        {
            if ((uint)index >= (uint)temps.Length)
                throw Fail(pc, op, $"Inline argument index {index} is out of range. Argument count: {temps.Length}.");
            return temps[index];
        }

        private GenTemp CheckedInlineLocalTemp(GenTemp[] temps, int index, int pc, BytecodeOp op)
        {
            if ((uint)index >= (uint)temps.Length)
                throw Fail(pc, op, $"Inline local index {index} is out of range. Local count: {temps.Length}.");
            return temps[index];
        }

        private void EmitInlineUnary(List<StackValue> stack, List<GenTree> statements, RuntimeModule bodyModule, RuntimeMethod callee, int callPc, Instruction ins)
        {
            var value = Pop(stack, callPc, ins.Op);
            RuntimeType? type = value.Type;
            GenStackKind stackKind = value.StackKind;
            RuntimeType? operandType = null;
            GenTreeKind kind = ins.Op switch
            {
                BytecodeOp.Neg => GenTreeKind.Unary,
                BytecodeOp.Not => GenTreeKind.Unary,
                BytecodeOp.PtrToByRef => GenTreeKind.PointerToByRef,
                BytecodeOp.CastClass => GenTreeKind.CastClass,
                BytecodeOp.Isinst => GenTreeKind.IsInst,
                BytecodeOp.Box => GenTreeKind.Box,
                BytecodeOp.UnboxAny => GenTreeKind.UnboxAny,
                _ => throw Fail(callPc, ins.Op, "Not a unary opcode."),
            };

            switch (ins.Op)
            {
                case BytecodeOp.PtrToByRef:
                    stackKind = GenStackKind.ByRef;
                    type = null;
                    break;

                case BytecodeOp.CastClass:
                case BytecodeOp.Isinst:
                    operandType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                    type = operandType.IsValueType ? _rts.SystemObject : operandType;
                    stackKind = GenStackKind.Ref;
                    break;

                case BytecodeOp.Box:
                    operandType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                    type = _rts.SystemObject;
                    stackKind = GenStackKind.Ref;
                    break;

                case BytecodeOp.UnboxAny:
                    operandType = ResolveTypeIn(bodyModule, callee, ins.Operand0);
                    type = operandType;
                    stackKind = StackKindOf(operandType);
                    break;
            }

            PushImportedValue(stack, statements, Node(kind, callPc, ins.Op, type: type, stackKind: stackKind, operands: One(value.Node), int32: ins.Operand0, runtimeType: operandType));
        }

        private void EmitInlineBinary(List<StackValue> stack, List<GenTree> statements, int callPc, Instruction ins)
        {
            var right = Pop(stack, callPc, ins.Op);
            var left = Pop(stack, callPc, ins.Op);

            RuntimeType? type = left.Type;
            GenStackKind stackKind = left.StackKind;
            GenTreeKind kind = GenTreeKind.Binary;
            RuntimeType? runtimeType = null;

            switch (ins.Op)
            {
                case BytecodeOp.Ceq:
                case BytecodeOp.Clt:
                case BytecodeOp.Clt_Un:
                case BytecodeOp.Cgt:
                case BytecodeOp.Cgt_Un:
                    type = null;
                    stackKind = GenStackKind.I4;
                    break;

                case BytecodeOp.PtrElemAddr:
                    kind = GenTreeKind.PointerElementAddr;
                    type = null;
                    stackKind = GenStackKind.Ptr;
                    break;

                case BytecodeOp.PtrDiff:
                    kind = GenTreeKind.PointerDiff;
                    type = null;
                    stackKind = GenStackKind.NativeInt;
                    break;
            }

            PushImportedValue(stack, statements, Node(kind, callPc, ins.Op, type: type, stackKind: stackKind, operands: Two(left.Node, right.Node),
                int32: ins.Operand0, runtimeType: runtimeType));
        }

        private void EmitInlineCall(
            List<StackValue> stack,
            List<GenTree> statements,
            RuntimeModule bodyModule,
            RuntimeMethod callerContext,
            int callPc,
            Instruction ins,
            int inlineDepth)
        {
            bool isVirtual = ins.Op == BytecodeOp.CallVirt;
            int packed = ins.Operand1;
            int argCount = packed & 0x7FFF;
            int hasThis = (packed >> 15) & 1;
            int total = argCount + hasThis;

            var args = PopMany(stack, total, callPc, ins.Op);
            var method = _rts.ResolveMethodInMethodContext(bodyModule, ins.Operand0, callerContext);

            if (isVirtual)
            {
                AddVirtualDependency(method);
            }
            else
            {
                AddDirectDependency(method);
                if (method.IsStatic && !StringComparer.Ordinal.Equals(method.Name, ".cctor"))
                    AddTypeInitializerDependency(method.DeclaringType);
            }

            SpillEvaluationStackForImportBarrier(statements, stack, callPc, ins.Op);

            if (!isVirtual && TryInlineCall(method, args, statements, callPc, ins.Op, out var inlineResult, inlineDepth + 1))
            {
                if (inlineResult is not null)
                    Push(stack, inlineResult);
                return;
            }

            bool returnsVoid = IsVoid(method.ReturnType);
            var call = Node(isVirtual ? GenTreeKind.VirtualCall : GenTreeKind.Call,
                callPc,
                ins.Op,
                type: returnsVoid ? null : method.ReturnType,
                stackKind: returnsVoid ? GenStackKind.Void : StackKindOf(method.ReturnType),
                operands: args,
                int32: total,
                int64: ins.Operand0,
                method: method);

            if (returnsVoid)
                AppendImporterStatement(statements, stack, Node(GenTreeKind.Eval, callPc, ins.Op, operands: One(call)));
            else
                PushImportedValue(stack, statements, call);
        }

        private void EmitInlineNewObject(List<StackValue> stack, List<GenTree> statements, RuntimeModule bodyModule, RuntimeMethod callee, int callPc, Instruction ins)
        {
            int argCount = ins.Operand1;
            var args = PopMany(stack, argCount, callPc, ins.Op);
            var ctor = _rts.ResolveMethodInMethodContext(bodyModule, ins.Operand0, callee);
            AddDirectDependency(ctor);
            AddTypeInitializerDependency(ctor.DeclaringType);

            var t = ctor.DeclaringType;
            MarkInstantiatedType(t);
            if (t.IsValueType)
            {
                EmitValueTypeNewObject(stack, statements, callPc, ins.Op, ins.Operand0, argCount, args, ctor, t);
                return;
            }

            PushImportedValue(stack, statements, Node(GenTreeKind.NewObject, callPc, ins.Op, type: t, stackKind: StackKindOf(t), operands: args,
                int32: argCount, int64: ins.Operand0, method: ctor, runtimeType: t));
        }

        private void EmitInlineField(
            List<StackValue> stack,
            List<GenTree> statements,
            RuntimeModule bodyModule,
            RuntimeMethod callee,
            int callPc,
            Instruction ins)
        {
            var field = _rts.ResolveFieldInMethodContext(bodyModule, ins.Operand0, callee);
            switch (ins.Op)
            {
                case BytecodeOp.Ldfld:
                    {
                        var receiver = Pop(stack, callPc, ins.Op);
                        PushImportedValue(stack, statements, Node(GenTreeKind.Field, callPc, ins.Op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                            operands: One(receiver.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Ldflda:
                    {
                        var receiver = Pop(stack, callPc, ins.Op);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.FieldAddr, callPc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                            operands: One(receiver.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Stfld:
                    {
                        var value = Pop(stack, callPc, ins.Op);
                        var receiver = Pop(stack, callPc, ins.Op);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, callPc, ins.Op, operands: Two(receiver.Node, value.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Ldsfld:
                    AddTypeInitializerDependency(field.DeclaringType);
                    PushImportedValue(stack, statements, Node(GenTreeKind.StaticField, callPc, ins.Op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                        field: field, int64: ins.Operand0));
                    break;

                case BytecodeOp.Ldsflda:
                    {
                        AddTypeInitializerDependency(field.DeclaringType);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.StaticFieldAddr, callPc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                            field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Stsfld:
                    {
                        AddTypeInitializerDependency(field.DeclaringType);
                        var value = Pop(stack, callPc, ins.Op);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreStaticField, callPc, ins.Op, operands: One(value.Node), field: field, int64: ins.Operand0));
                        break;
                    }
            }
        }

        private void EmitField(List<StackValue> stack, List<GenTree> statements, int pc, Instruction ins)
        {
            var field = _rts.ResolveFieldInMethodContext(_module, ins.Operand0, _method);
            switch (ins.Op)
            {
                case BytecodeOp.Ldfld:
                    {
                        var receiver = Pop(stack, pc, ins.Op);
                        PushImportedValue(stack, statements, Node(GenTreeKind.Field, pc, ins.Op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                            operands: One(receiver.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Ldflda:
                    {
                        var receiver = Pop(stack, pc, ins.Op);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.FieldAddr, pc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                            operands: One(receiver.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Stfld:
                    {
                        var value = Pop(stack, pc, ins.Op);
                        var receiver = Pop(stack, pc, ins.Op);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreField, pc, ins.Op, operands: Two(receiver.Node, value.Node), field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Ldsfld:
                    AddTypeInitializerDependency(field.DeclaringType);
                    PushImportedValue(stack, statements, Node(GenTreeKind.StaticField, pc, ins.Op, type: field.FieldType, stackKind: StackKindOf(field.FieldType),
                        field: field, int64: ins.Operand0));
                    break;

                case BytecodeOp.Ldsflda:
                    {
                        AddTypeInitializerDependency(field.DeclaringType);
                        var byRef = _rts.GetByRefType(field.FieldType);
                        PushImportedValue(stack, statements, Node(GenTreeKind.StaticFieldAddr, pc, ins.Op, type: byRef, stackKind: GenStackKind.ByRef,
                            field: field, int64: ins.Operand0));
                        break;
                    }

                case BytecodeOp.Stsfld:
                    {
                        AddTypeInitializerDependency(field.DeclaringType);
                        var value = Pop(stack, pc, ins.Op);
                        AppendImporterStatement(statements, stack, Node(GenTreeKind.StoreStaticField, pc, ins.Op, operands: One(value.Node), field: field, int64: ins.Operand0));
                        break;
                    }
            }
        }

        private void AddTypeInitializerDependency(RuntimeType type)
        {
            _rts.EnsureConstructedMembers(type);

            RuntimeMethod? cctor = FindTypeInitializer(type);
            if (cctor is not null)
                AddDirectDependency(cctor);
        }

        internal static RuntimeMethod? FindTypeInitializer(RuntimeType type)
        {
            for (int i = 0; i < type.Methods.Length; i++)
            {
                RuntimeMethod method = type.Methods[i];
                if (method.IsStatic && method.ParameterTypes.Length == 0 && StringComparer.Ordinal.Equals(method.Name, ".cctor"))
                    return method;
            }
            return null;
        }

        private void AddDirectDependency(RuntimeMethod method)
        {
            if (method.Body is null)
                return;
            if (_directDependencyIds.Add(method.MethodId))
                _directDependencies.Add(method);
        }

        private void AddVirtualDependency(RuntimeMethod method)
        {
            if (_virtualDependencyIds.Add(method.MethodId))
                _virtualDependencies.Add(method);
        }

        private ImmutableArray<GenTree> PopMany(List<StackValue> stack, int count, int pc, BytecodeOp op)
        {
            if (count < 0)
                throw Fail(pc, op, $"Negative pop count {count}.");
            if (stack.Count < count)
                throw Fail(pc, op, $"Evaluation stack underflow. Need {count}, have {stack.Count}.");

            var result = new GenTree[count];
            for (int i = count - 1; i >= 0; i--)
                result[i] = Pop(stack, pc, op).Node;
            return result.ToImmutableArray();
        }

        private RuntimeType ResolveType(int typeToken)
            => _rts.ResolveTypeInMethodContext(_module, typeToken, _method);

        private RuntimeType CheckedLocalType(int index, int pc)
        {
            if ((uint)index >= (uint)_localTypes.Length)
                throw Fail(pc, BytecodeOp.Ldloc, $"Local index {index} is out of range. Local count: {_localTypes.Length}.");
            return _localTypes[index];
        }

        private bool RuntimeTypeIsValueType(RuntimeType type, int pc, BytecodeOp op)
        {
            if (type.Kind == RuntimeTypeKind.TypeParam)
                throw Fail(pc, op, "TypeIsValueType requires a closed generic context.");

            return type.IsValueType;
        }

        private RuntimeType CheckedArgType(int index, int pc)
        {
            if ((uint)index >= (uint)_argTypes.Length)
                throw Fail(pc, BytecodeOp.Ldarg, $"Argument index {index} is out of range. Argument count: {_argTypes.Length}.");
            return _argTypes[index];
        }

        private GenTree Node(
            GenTreeKind kind,
            int pc,
            BytecodeOp sourceOp,
            RuntimeType? type = null,
            GenStackKind stackKind = GenStackKind.Void,
            ImmutableArray<GenTree> operands = default,
            int int32 = 0,
            long int64 = 0,
            string? text = null,
            RuntimeType? runtimeType = null,
            RuntimeField? field = null,
            RuntimeMethod? method = null,
            NumericConvKind convKind = default,
            NumericConvFlags convFlags = default,
            int targetPc = -1,
            int targetBlockId = -1)
        {
            var actualOperands = operands.IsDefault ? ImmutableArray<GenTree>.Empty : operands;
            var flags = ComputeFlags(kind, sourceOp, actualOperands, convFlags);

            return new GenTree(
                ++_nextNodeId,
                kind,
                pc,
                sourceOp,
                type,
                stackKind,
                flags,
                actualOperands,
                int32,
                int64,
                text,
                runtimeType,
                field,
                method,
                convKind,
                convFlags,
                targetPc,
                targetBlockId);
        }

        private static GenTreeFlags ComputeFlags(GenTreeKind kind, BytecodeOp sourceOp, ImmutableArray<GenTree> operands, NumericConvFlags convFlags)
        {
            GenTreeFlags flags = GenTreeFlags.None;
            for (int i = 0; i < operands.Length; i++)
                flags |= operands[i].Flags;

            switch (kind)
            {
                case GenTreeKind.Local:
                case GenTreeKind.Arg:
                case GenTreeKind.Temp:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.LocalAddr:
                case GenTreeKind.ArgAddr:
                case GenTreeKind.TempAddr:
                    flags |= GenTreeFlags.LocalUse;
                    break;

                case GenTreeKind.StoreLocal:
                case GenTreeKind.StoreArg:
                case GenTreeKind.StoreTemp:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.LocalDef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StaticField:
                case GenTreeKind.StaticFieldAddr:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.GlobalRef;
                    break;

                case GenTreeKind.LoadIndirect:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.Indirect | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreIndirect:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.Indirect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StoreField:
                case GenTreeKind.StoreStaticField:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.GlobalRef | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.NewObject:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.NewDelegate:
                case GenTreeKind.DelegateCombine:
                case GenTreeKind.DelegateRemove:
                case GenTreeKind.NewArray:
                case GenTreeKind.Box:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                case GenTreeKind.ArrayDataRef:
                    flags |= GenTreeFlags.MemoryRead | GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.StoreArrayElement:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.MemoryWrite | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StaticData:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.StackAlloc:
                case GenTreeKind.AllocHGlobal:
                    flags |= GenTreeFlags.Allocation | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.FreeHGlobal:
                    flags |= GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.CastClass:
                case GenTreeKind.UnboxAny:
                    flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Conv:
                    if ((convFlags & NumericConvFlags.Checked) != 0)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Binary:
                    if (sourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un or
                        BytecodeOp.Add_Ovf or BytecodeOp.Add_Ovf_Un or
                        BytecodeOp.Sub_Ovf or BytecodeOp.Sub_Ovf_Un or
                        BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un)
                        flags |= GenTreeFlags.CanThrow;
                    break;

                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    flags |= GenTreeFlags.ContainsCall | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.GlobalRef | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Branch:
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                case GenTreeKind.Return:
                case GenTreeKind.EndFinally:
                    flags |= GenTreeFlags.ControlFlow | GenTreeFlags.Ordered;
                    break;

                case GenTreeKind.Throw:
                case GenTreeKind.Rethrow:
                    flags |= GenTreeFlags.ControlFlow | GenTreeFlags.ExceptionFlow | GenTreeFlags.SideEffect | GenTreeFlags.CanThrow | GenTreeFlags.Ordered;
                    break;
            }

            return flags;
        }

        private static ImmutableArray<GenTree> One(GenTree node) => ImmutableArray.Create(node);
        private static ImmutableArray<GenTree> Two(GenTree left, GenTree right) => ImmutableArray.Create(left, right);

        private static void Push(List<StackValue> stack, StackValue value) => stack.Add(value);
        private static void Push(List<StackValue> stack, GenTree node) => stack.Add(new StackValue(node, node.Type, node.StackKind));

        private static StackValue Pop(List<StackValue> stack, int pc, BytecodeOp op)
        {
            if (stack.Count == 0)
                throw new GenTreeBuildException($"Evaluation stack underflow at pc {pc}, op {op}.");
            int last = stack.Count - 1;
            var value = stack[last];
            stack.RemoveAt(last);
            return value;
        }

        private int BlockIdForPc(int pc)
        {
            if (!_pcToBlockId.TryGetValue(pc, out int id))
                throw Fail(pc, BytecodeOp.Nop, $"No block starts at target pc {pc}.");
            return id;
        }

        private static void AddSuccessor(List<int> successors, int pc)
        {
            if (pc < 0) return;
            for (int i = 0; i < successors.Count; i++)
            {
                if (successors[i] == pc)
                    return;
            }
            successors.Add(pc);
        }

        private int[] ComputeStackDepths()
        {
            var instructions = _body.Instructions;
            var result = new int[instructions.Length];
            Array.Fill(result, UnreachableStackDepth);

            var queue = new Queue<int>();

            AddEntry(0, 0);
            foreach (var h in _body.ExceptionHandlers)
                AddEntry(h.HandlerStartPc, 0);

            while (queue.Count != 0)
            {
                int pc = queue.Dequeue();
                if ((uint)pc >= (uint)instructions.Length)
                    continue;

                int inDepth = result[pc];
                var ins = instructions[pc];

                int outDepth = ins.Op == BytecodeOp.Leave
                    ? 0
                    : checked(inDepth - ins.Pop + ins.Push);

                if (outDepth < 0)
                    throw Fail(pc, ins.Op, $"Negative evaluation stack depth. In={inDepth}, pop={ins.Pop}, push={ins.Push}.");

                if (outDepth > _body.MaxStack)
                    throw Fail(pc, ins.Op, $"Evaluation stack depth {outDepth} exceeds MaxStack {_body.MaxStack}.");

                AddSuccessors(pc, ins, outDepth);
            }

            return result;

            void AddSuccessors(int pc, Instruction ins, int outDepth)
            {
                switch (ins.Op)
                {
                    case BytecodeOp.Br:
                    case BytecodeOp.Leave:
                        AddEntry(ins.Operand0, outDepth);
                        return;

                    case BytecodeOp.Brtrue:
                    case BytecodeOp.Brfalse:
                        AddEntry(ins.Operand0, outDepth);
                        AddEntry(pc + 1, outDepth);
                        return;

                    case BytecodeOp.Ret:
                    case BytecodeOp.Throw:
                    case BytecodeOp.Rethrow:
                    case BytecodeOp.Endfinally:
                        return;

                    default:
                        AddEntry(pc + 1, outDepth);
                        return;
                }
            }

            void AddEntry(int pc, int depth)
            {
                if ((uint)pc >= (uint)instructions.Length)
                    return;

                int existing = result[pc];
                if (existing != UnreachableStackDepth)
                {
                    if (existing != depth)
                        throw Fail(pc, BytecodeOp.Nop, $"Inconsistent stack depth at pc {pc}: existing={existing}, incoming={depth}.");
                    return;
                }

                result[pc] = depth;
                queue.Enqueue(pc);
            }
        }
        private List<int> ComputeLeaders(int[] stackDepthAtPc)
        {
            int instructionCount = _body.Instructions.Length;
            if (instructionCount == 0)
                return new List<int>();

            var isLeader = new bool[instructionCount];
            int leaderCount = 0;

            AddReachableLeader(0);

            for (int pc = 0; pc < instructionCount; pc++)
            {
                if (stackDepthAtPc[pc] == UnreachableStackDepth)
                    continue;

                var ins = _body.Instructions[pc];

                switch (ins.Op)
                {
                    case BytecodeOp.Br:
                    case BytecodeOp.Leave:
                        AddReachableLeader(ins.Operand0);
                        break;

                    case BytecodeOp.Brtrue:
                    case BytecodeOp.Brfalse:
                        AddReachableLeader(ins.Operand0);
                        AddReachableLeader(pc + 1);
                        break;
                }

                if (IsBlockTerminator(ins.Op))
                    AddReachableLeader(pc + 1);
            }

            foreach (var h in _body.ExceptionHandlers)
            {
                AddReachableLeader(h.TryStartPc);
                AddReachableLeader(h.TryEndPc);
                AddReachableLeader(h.HandlerStartPc);
                AddReachableLeader(h.HandlerEndPc);
            }

            var leaders = new List<int>(leaderCount);
            for (int pc = 0; pc < isLeader.Length; pc++)
            {
                if (isLeader[pc])
                    leaders.Add(pc);
            }

            return leaders;

            void AddReachableLeader(int pc)
            {
                if ((uint)pc >= (uint)instructionCount)
                    return;

                if (stackDepthAtPc[pc] == UnreachableStackDepth)
                    return;

                if (isLeader[pc])
                    return;

                isLeader[pc] = true;
                leaderCount++;
            }
        }

        private static bool IsBlockTerminator(BytecodeOp op)
        {
            return op is BytecodeOp.Br or BytecodeOp.Leave or BytecodeOp.Brtrue or BytecodeOp.Brfalse or
                BytecodeOp.Ret or BytecodeOp.Throw or BytecodeOp.Rethrow or BytecodeOp.Endfinally;
        }

        private static bool IsVoid(RuntimeType t)
            => t.Namespace == "System" && t.Name == "Void";

        private static GenStackKind StackKindOf(NumericConvKind kind)
        {
            return kind switch
            {
                NumericConvKind.I8 or NumericConvKind.U8 => GenStackKind.I8,
                NumericConvKind.R4 => GenStackKind.R4,
                NumericConvKind.R8 => GenStackKind.R8,
                NumericConvKind.NativeInt => GenStackKind.NativeInt,
                NumericConvKind.NativeUInt => GenStackKind.NativeUInt,
                _ => GenStackKind.I4,
            };
        }

        private static GenStackKind StackKindOf(RuntimeType? type)
        {
            if (type is null)
                return GenStackKind.Unknown;

            if (IsVoid(type))
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

        private GenTreeBuildException Fail(int pc, BytecodeOp op, string message)
        {
            return new GenTreeBuildException(
                $"GenTree build failed in {_module.Name}:{_method.DeclaringType.Namespace}.{_method.DeclaringType.Name}.{_method.Name} " +
                $"at pc {pc}, op {op}: {message}");
        }

        private readonly struct StackValue
        {
            public readonly GenTree Node;
            public readonly RuntimeType? Type;
            public readonly GenStackKind StackKind;

            public StackValue(GenTree node, RuntimeType? type, GenStackKind stackKind)
            {
                Node = node;
                Type = type;
                StackKind = stackKind;
            }
        }
    }

    internal static class GenTreeDumper
    {
        private static bool TryAppendOperand(StringBuilder sb, GenTree node, int index)
        {
            if ((uint)index < (uint)node.Operands.Length)
            {
                AppendNode(sb, node.Operands[index]);
                return true;
            }

            sb.Append("<missing-op").Append(index).Append('>');
            return false;
        }

        internal static void AppendNode(StringBuilder sb, GenTree node)
        {
            switch (node.Kind)
            {
                case GenTreeKind.ConstI4:
                    sb.Append(node.Int32);
                    return;
                case GenTreeKind.ConstI8:
                    sb.Append(node.Int64).Append('L');
                    return;
                case GenTreeKind.ConstR4Bits:
                    sb.Append(BitConverter.Int32BitsToSingle(node.Int32).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('f');
                    return;
                case GenTreeKind.ConstR8Bits:
                    sb.Append(BitConverter.Int64BitsToDouble(node.Int64).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case GenTreeKind.ConstNull:
                    sb.Append("null");
                    return;
                case GenTreeKind.ConstString:
                    sb.Append('"').Append(Escape(node.Text ?? string.Empty)).Append('"');
                    return;
                case GenTreeKind.Local:
                    sb.Append('l').Append(node.Int32);
                    return;
                case GenTreeKind.LocalAddr:
                    sb.Append("&l").Append(node.Int32);
                    return;
                case GenTreeKind.Arg:
                    sb.Append('a').Append(node.Int32);
                    return;
                case GenTreeKind.ArgAddr:
                    sb.Append("&a").Append(node.Int32);
                    return;
                case GenTreeKind.Temp:
                    sb.Append('t').Append(node.Int32);
                    return;
                case GenTreeKind.TempAddr:
                    sb.Append("&t").Append(node.Int32);
                    return;
                case GenTreeKind.DefaultValue:
                    sb.Append("default(").Append(TypeName(node.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.SizeOf:
                    sb.Append("sizeof(").Append(TypeName(node.RuntimeType)).Append(')');
                    return;
                case GenTreeKind.ExceptionObject:
                    sb.Append("exception");
                    return;
                case GenTreeKind.Unary:
                    sb.Append(node.SourceOp.ToString().ToLowerInvariant()).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Binary:
                    if (node.Operands.Length == 2)
                    {
                        sb.Append('(');
                        TryAppendOperand(sb, node, 0);
                        sb.Append(' ').Append(node.SourceOp).Append(' ');
                        TryAppendOperand(sb, node, 1);
                        sb.Append(')');
                        return;
                    }
                    break;
                case GenTreeKind.Conv:
                    sb.Append("conv.").Append(node.ConvKind);
                    if (node.ConvFlags != NumericConvFlags.None)
                        sb.Append('.').Append(node.ConvFlags);
                    sb.Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Call:
                case GenTreeKind.VirtualCall:
                case GenTreeKind.DelegateInvoke:
                    sb.Append(node.Kind == GenTreeKind.VirtualCall ? "callvirt " : node.Kind == GenTreeKind.DelegateInvoke ? "delegate_invoke " : "call ");
                    sb.Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewObject:
                    sb.Append("newobj ").Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.NewDelegate:
                    sb.Append("newdelegate ").Append(TypeName(node.RuntimeType)).Append(" -> ").Append(MethodName(node.Method)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Field:
                case GenTreeKind.FieldAddr:
                    TryAppendOperand(sb, node, 0);
                    sb.Append(node.Kind == GenTreeKind.FieldAddr ? ".&" : ".");
                    sb.Append(FieldName(node.Field));
                    return;
                case GenTreeKind.StaticField:
                    sb.Append(FieldName(node.Field));
                    return;
                case GenTreeKind.StaticFieldAddr:
                    sb.Append('&').Append(FieldName(node.Field));
                    return;
                case GenTreeKind.LoadIndirect:
                    sb.Append("ldobj ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StoreIndirect:
                    sb.Append("stobj ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StoreLocal:
                    sb.Append('l').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreArg:
                    sb.Append('a').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreTemp:
                    sb.Append('t').Append(node.Int32).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.StoreField:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('.').Append(FieldName(node.Field)).Append(" = ");
                    TryAppendOperand(sb, node, 1);
                    return;
                case GenTreeKind.StoreStaticField:
                    sb.Append(FieldName(node.Field)).Append(" = ");
                    TryAppendOperand(sb, node, 0);
                    return;
                case GenTreeKind.NewArray:
                    sb.Append("newarr ").Append(TypeName(node.RuntimeType)).Append('[');
                    AppendOperands(sb, node);
                    sb.Append(']');
                    return;
                case GenTreeKind.ArrayElement:
                case GenTreeKind.ArrayElementAddr:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('[');
                    TryAppendOperand(sb, node, 1);
                    sb.Append(']');
                    if (node.Kind == GenTreeKind.ArrayElementAddr) sb.Append(".addr");
                    return;
                case GenTreeKind.StoreArrayElement:
                    TryAppendOperand(sb, node, 0);
                    sb.Append('[');
                    TryAppendOperand(sb, node, 1);
                    sb.Append("] = ");
                    TryAppendOperand(sb, node, 2);
                    return;
                case GenTreeKind.ArrayDataRef:
                    sb.Append("arrayDataRef(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.StaticData:
                    sb.Append("staticData(offset=").Append(node.Int32).Append(", length=").Append(node.Int64).Append(')');
                    return;
                case GenTreeKind.StackAlloc:
                    sb.Append("stackalloc(size=").Append(node.Int32).Append(", count=");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.AllocHGlobal:
                    sb.Append("alloc_hglobal(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.FreeHGlobal:
                    sb.Append("free_hglobal(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerElementAddr:
                    sb.Append("ptrElemAddr(size=").Append(node.Int32).Append(", ");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerToByRef:
                    sb.Append("ptrToByRef(");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.PointerDiff:
                    sb.Append("ptrDiff(size=").Append(node.Int32).Append(", ");
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.CastClass:
                    sb.Append("castclass ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.IsInst:
                    sb.Append("isinst ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Box:
                    sb.Append("box ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.UnboxAny:
                    sb.Append("unbox.any ").Append(TypeName(node.RuntimeType)).Append('(');
                    AppendOperands(sb, node);
                    sb.Append(')');
                    return;
                case GenTreeKind.Eval:
                    sb.Append("eval ");
                    AppendOperands(sb, node);
                    return;
                case GenTreeKind.Branch:
                    sb.Append("br B").Append(node.TargetBlockId).Append(" pc ").Append(node.TargetPc);
                    return;
                case GenTreeKind.BranchTrue:
                case GenTreeKind.BranchFalse:
                    sb.Append(node.Kind == GenTreeKind.BranchTrue ? "brtrue " : "brfalse ");
                    TryAppendOperand(sb, node, 0);
                    sb.Append(" -> B").Append(node.TargetBlockId).Append(" pc ").Append(node.TargetPc);
                    return;
                case GenTreeKind.Return:
                    sb.Append("ret");
                    if (node.Operands.Length != 0)
                    {
                        sb.Append(' ');
                        AppendOperands(sb, node);
                    }
                    return;
                case GenTreeKind.Throw:
                    sb.Append("throw ");
                    AppendOperands(sb, node);
                    return;
                case GenTreeKind.Rethrow:
                    sb.Append("rethrow");
                    return;
                case GenTreeKind.EndFinally:
                    sb.Append("endfinally");
                    return;
            }

            sb.Append(node.Kind).Append('(');
            AppendOperands(sb, node);
            sb.Append(')');
        }

        private static void AppendOperands(StringBuilder sb, GenTree node)
        {
            for (int i = 0; i < node.Operands.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                AppendNode(sb, node.Operands[i]);
            }
        }

        private static string TypeName(RuntimeType? type)
        {
            if (type is null) return "?";
            if (string.IsNullOrEmpty(type.Namespace)) return type.Name;
            return $"{type.Namespace}.{type.Name}";
        }

        private static string FieldName(RuntimeField? field)
        {
            if (field is null) return "<field?>";
            return $"{TypeName(field.DeclaringType)}.{field.Name}";
        }

        private static string MethodName(RuntimeMethod? method)
        {
            if (method is null) return "<method?>";
            return $"{TypeName(method.DeclaringType)}.{method.Name}";
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
