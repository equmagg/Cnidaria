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
        PhysicalPromotedHir,
        GlobalMorphedHir,
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
        CommonSubexpression,
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
            bool wasPromotedParent = Category == GenLocalCategory.PromotedStruct && HasPromotedStructFields;
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
            else if (wasPromotedParent)
            {
                MarkPromotedStructParent();
            }

            if (Kind == GenLocalKind.Temporary && !wasPromotedField && !wasPromotedParent)
            {
                if (wasStructMaterializationTemp && IsPromotableStructMaterializationType(Type) && !IsRefLikeStorageType(Type))
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

        internal static bool IsRefLikeStorageType(RuntimeType? type)
        {
            return type is not null &&
                   (type.Kind == RuntimeTypeKind.ByRef || IsKnownRefLike(type));
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
        public SsaValueName? SsaLocalFieldBaseValue { get; internal set; }
        public RuntimeField? SsaLocalField { get; internal set; }
        public ImmutableArray<SsaMemoryValueName> SsaMemoryUses { get; internal set; } = ImmutableArray<SsaMemoryValueName>.Empty;
        public ImmutableArray<SsaMemoryValueName> SsaMemoryDefinitions { get; internal set; } = ImmutableArray<SsaMemoryValueName>.Empty;
        public GenTreeLsraInfo LsraInfo { get; private set; } = GenTreeLsraInfo.Empty;
        public int CseNumber { get; internal set; }
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
        public bool HasSsaLocalFieldBaseUse => SsaLocalFieldBaseValue.HasValue;
        public bool HasSsaMemoryEffects => !SsaMemoryUses.IsDefaultOrEmpty || !SsaMemoryDefinitions.IsDefaultOrEmpty;

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

        internal void SetParent(GenTree? parent)
            => Parent = parent;

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

        internal void AttachSsaLocalFieldBaseUse(SsaValueName value, RuntimeField? field)
        {
            SsaLocalFieldBaseValue = value;
            SsaLocalField = field;
        }

        internal void AttachSsaLocalField(RuntimeField? field)
        {
            SsaLocalField = field;
        }

        internal void AttachSsaMemory(ImmutableArray<SsaMemoryValueName> uses, ImmutableArray<SsaMemoryValueName> definitions)
        {
            SsaMemoryUses = uses.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : uses;
            SsaMemoryDefinitions = definitions.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : definitions;
        }

        internal void ClearSsaAnnotation()
        {
            SsaValueName = null;
            SsaStoreTargetName = null;
            SsaLocalFieldBaseValue = null;
            SsaLocalField = null;
            SsaMemoryUses = ImmutableArray<SsaMemoryValueName>.Empty;
            SsaMemoryDefinitions = ImmutableArray<SsaMemoryValueName>.Empty;
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
    internal static class GenTreeArithmeticSemantics
    {
        public static bool BinaryOperationCanThrow(BytecodeOp sourceOp, RuntimeType? type, GenStackKind stackKind, ImmutableArray<GenTree> operands)
        {
            if (sourceOp is BytecodeOp.Add_Ovf or BytecodeOp.Add_Ovf_Un
                or BytecodeOp.Sub_Ovf or BytecodeOp.Sub_Ovf_Un
                or BytecodeOp.Mul_Ovf or BytecodeOp.Mul_Ovf_Un)
                return true;

            if (sourceOp is BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un)
                return DivRemCanThrow(sourceOp, type, stackKind, operands);

            return false;
        }

        public static bool DivRemCanThrow(GenTree node)
        {
            if (node.Kind != GenTreeKind.Binary)
                return false;
            return DivRemCanThrow(node.SourceOp, node.Type, node.StackKind, node.Operands);
        }

        public static bool DivRemCanThrow(BytecodeOp sourceOp, RuntimeType? type, GenStackKind stackKind, ImmutableArray<GenTree> operands)
        {
            if (sourceOp is not (BytecodeOp.Div or BytecodeOp.Div_Un or BytecodeOp.Rem or BytecodeOp.Rem_Un))
                return false;

            if (!IsIntegralArithmeticType(type, stackKind))
                return false;

            if (operands.Length < 2)
                return true;

            int bits = IntegralBits(type, stackKind);
            if (!TryGetIntegralConstant(operands[1], bits, out long signedDivisor, out ulong unsignedDivisor))
                return true;

            if (unsignedDivisor == 0)
                return true;

            if (sourceOp is BytecodeOp.Div_Un or BytecodeOp.Rem_Un)
                return false;

            if (signedDivisor != -1)
                return false;

            if (operands.Length >= 1 && TryGetIntegralConstant(operands[0], bits, out long signedDividend, out _))
                return IsSignedMinValue(signedDividend, bits);

            return true;
        }

        public static bool IsIntegralArithmeticType(RuntimeType? type, GenStackKind stackKind)
        {
            if (type is not null)
            {
                if (type.PrimitiveKind is RuntimePrimitiveKind.Single or RuntimePrimitiveKind.Double or RuntimePrimitiveKind.Decimal or RuntimePrimitiveKind.Void)
                    return false;

                if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef)
                    return false;

                if (type.PrimitiveKind is RuntimePrimitiveKind.Boolean or RuntimePrimitiveKind.Char or
                    RuntimePrimitiveKind.Int8 or RuntimePrimitiveKind.UInt8 or
                    RuntimePrimitiveKind.Int16 or RuntimePrimitiveKind.UInt16 or
                    RuntimePrimitiveKind.Int32 or RuntimePrimitiveKind.UInt32 or
                    RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64 or
                    RuntimePrimitiveKind.IntPtr or RuntimePrimitiveKind.UIntPtr)
                    return true;
            }

            return stackKind is GenStackKind.I4 or GenStackKind.I8 or GenStackKind.NativeInt or GenStackKind.NativeUInt;
        }

        public static bool Is64BitIntegral(RuntimeType? type, GenStackKind stackKind)
            => IntegralBits(type, stackKind) == 64;

        public static int IntegralBits(RuntimeType? type, GenStackKind stackKind)
        {
            if (stackKind == GenStackKind.I8)
                return 64;
            if (stackKind is GenStackKind.NativeInt or GenStackKind.NativeUInt)
                return TargetArchitecture.PointerSize == 8 ? 64 : 32;

            if (type is not null)
            {
                if (type.PrimitiveKind is RuntimePrimitiveKind.Int64 or RuntimePrimitiveKind.UInt64)
                    return 64;
                if (type.PrimitiveKind is RuntimePrimitiveKind.IntPtr or RuntimePrimitiveKind.UIntPtr)
                    return TargetArchitecture.PointerSize == 8 ? 64 : 32;
            }

            return 32;
        }

        public static bool TryGetIntegralConstant(GenTree node, int bits, out long signedValue, out ulong unsignedValue)
        {
            switch (node.Kind)
            {
                case GenTreeKind.ConstI4:
                    signedValue = node.Int32;
                    unsignedValue = unchecked((uint)node.Int32);
                    return true;
                case GenTreeKind.ConstI8:
                    if (bits <= 32)
                    {
                        int narrowed = unchecked((int)node.Int64);
                        signedValue = narrowed;
                        unsignedValue = unchecked((uint)narrowed);
                    }
                    else
                    {
                        signedValue = node.Int64;
                        unsignedValue = unchecked((ulong)node.Int64);
                    }
                    return true;
                default:
                    signedValue = 0;
                    unsignedValue = 0;
                    return false;
            }
        }

        public static bool IsSignedMinValue(long value, int bits)
            => bits <= 32 ? value == int.MinValue : value == long.MinValue;

        public static bool TryGetUnsignedPowerOfTwoDivisor(ulong divisor, int bits, out int shift)
        {
            shift = 0;
            ulong mask = bits <= 32 ? uint.MaxValue : ulong.MaxValue;
            divisor &= mask;
            if (divisor <= 1 || (divisor & (divisor - 1)) != 0)
                return false;
            shift = Log2(divisor);
            return true;
        }

        private static int Log2(ulong value)
        {
            int result = 0;
            while ((value >>= 1) != 0)
                result++;
            return result;
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
            CollectPostOrder(root, seen, builder);

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

        private static void CollectPostOrder(GenTree node, HashSet<GenTree> seen, ImmutableArray<GenTree>.Builder builder)
        {
            if (!seen.Add(node))
                return;

            for (int i = 0; i < node.Operands.Length; i++)
                CollectPostOrder(node.Operands[i], seen, builder);
            builder.Add(node);
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
            NormalizeParents(Statements);
            StatementTreeLists = GenTreeTreeOrder.BuildStatements(Statements);
            SetLinearNodes(GenTreeTreeOrder.Flatten(StatementTreeLists));
            SuccessorBlockIds = successorBlockIds.IsDefault ? ImmutableArray<int>.Empty : successorBlockIds;
            SuccessorPcs = successorPcs.IsDefault ? ImmutableArray<int>.Empty : successorPcs;
        }


        private static void NormalizeParents(ImmutableArray<GenTree> statements)
        {
            if (statements.IsDefaultOrEmpty)
                return;

            var seen = new HashSet<GenTree>(ReferenceEqualityComparer<GenTree>.Instance);
            for (int i = 0; i < statements.Length; i++)
                NormalizeParent(statements[i], null, seen);
        }

        private static void NormalizeParent(GenTree node, GenTree? parent, HashSet<GenTree> seen)
        {
            if (!seen.Add(node))
                throw new InvalidOperationException($"GenTree node {node.Id} is shared between statement trees.");

            node.SetParent(parent);
            for (int i = 0; i < node.Operands.Length; i++)
                NormalizeParent(node.Operands[i], node, seen);
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
        public ImmutableArray<GenTemp> Temps { get; private set; }
        public ImmutableArray<GenLocalDescriptor> ArgDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> LocalDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> TempDescriptors { get; private set; }
        public ImmutableArray<GenLocalDescriptor> AllLocalDescriptors { get; private set; }
        public ImmutableArray<GenTreeBlock> Blocks { get; private set; }
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

        internal void ReplaceBlocksPreservingFlow(ImmutableArray<GenTreeBlock> blocks)
        {
            Blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;
            AttachLocalDescriptorsToTrees(Blocks);
        }

        internal GenLocalDescriptor AppendCompilerTemp(GenTempKind kind, RuntimeType? type, GenStackKind stackKind)
        {
            if (kind == GenTempKind.StructMaterialization)
                throw new ArgumentException("Struct materialization temps must be created before local classification.", nameof(kind));

            int tempIndex = NextTempIndex();
            int lclNum = AllLocalDescriptors.Length;
            var temp = new GenTemp(tempIndex, kind, type, stackKind);
            var descriptor = new GenLocalDescriptor(lclNum, GenLocalKind.Temporary, tempIndex, type, stackKind, GenLocalCategory.CompilerTemp);
            descriptor.IsCompilerTemp = true;
            descriptor.DoNotEnregister = kind != GenTempKind.CommonSubexpression;

            Temps = Temps.Add(temp);
            TempDescriptors = TempDescriptors.Add(descriptor);
            AllLocalDescriptors = AllLocalDescriptors.Add(descriptor);
            return descriptor;

            int NextTempIndex()
            {
                int max = -1;
                for (int i = 0; i < Temps.Length; i++)
                {
                    if (Temps[i].Index > max)
                        max = Temps[i].Index;
                }
                return max + 1;
            }
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
                bool isCommonSubexpression = temp.Kind == GenTempKind.CommonSubexpression;
                bool canPromoteTemp = temp.Kind is GenTempKind.StructMaterialization or GenTempKind.InlineArg or GenTempKind.InlineLocal or GenTempKind.InlineReturn;
                bool promoteParent = canPromoteTemp &&
                    LclVarDsc.IsPromotableStructMaterializationType(temp.Type) &&
                    !LclVarDsc.IsRefLikeStorageType(temp.Type);
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
                    descriptor.DoNotEnregister = !isCommonSubexpression;
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
                    if (parent.IsRefLike || parent.IsImplicitByRef)
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
                if (LclVarDsc.IsRefLikeStorageType(type))
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
                if (field.FieldType.Kind == RuntimeTypeKind.ByRef || stackKind is GenStackKind.ByRef)
                    return false;
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
                if (node.SsaValueName.HasValue)
                {
                    if (SsaSlotMatchesDescriptor(node.SsaValueName.Value.Slot, descriptor))
                        node.LocalDescriptor = descriptor;
                    return;
                }

                if (node.SsaStoreTargetName.HasValue)
                {
                    if (SsaSlotMatchesDescriptor(node.SsaStoreTargetName.Value.Slot, descriptor))
                        node.LocalDescriptor = descriptor;
                    return;
                }

                if (node.SsaLocalFieldBaseValue.HasValue)
                {
                    if (SsaSlotMatchesDescriptor(node.SsaLocalFieldBaseValue.Value.Slot, descriptor))
                        node.LocalDescriptor = descriptor;
                    return;
                }

                node.LocalDescriptor = descriptor;
                node.ValueKey = GenTreeValueKey.ForTree(node);
            }

            static bool SsaSlotMatchesDescriptor(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                if (slot.HasLclNum)
                    return slot.LclNum == descriptor.LclNum;

                return descriptor.Kind switch
                {
                    GenLocalKind.Argument => slot.Kind == SsaSlotKind.Arg && slot.Index == descriptor.Index,
                    GenLocalKind.Local => slot.Kind == SsaSlotKind.Local && slot.Index == descriptor.Index,
                    GenLocalKind.Temporary => slot.Kind == SsaSlotKind.Temp && slot.Index == descriptor.Index,
                    _ => false,
                };
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
}
