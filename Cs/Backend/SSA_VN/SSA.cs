using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class SsaConfig
    {
        public const int ReservedSsaNumber = 0;
        public const int FirstSsaNumber = 1;
    }
    internal enum SsaSlotKind : byte
    {
        Arg,
        Local,
        Temp,
    }
    internal readonly struct SsaSlot : IEquatable<SsaSlot>, IComparable<SsaSlot>
    {
        public readonly SsaSlotKind Kind;
        public readonly int Index;
        public readonly int LclNum;
        private readonly bool _hasLclNum;

        public SsaSlot(SsaSlotKind kind, int index)
            : this(kind, index, lclNum: -1)
        {
        }

        public SsaSlot(SsaSlotKind kind, int index, int lclNum)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (lclNum < -1) throw new ArgumentOutOfRangeException(nameof(lclNum));
            Kind = kind;
            Index = index;
            LclNum = lclNum;
            _hasLclNum = lclNum >= 0;
        }

        public SsaSlot(GenLocalDescriptor descriptor)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
            Kind = descriptor.Kind switch
            {
                GenLocalKind.Argument => SsaSlotKind.Arg,
                GenLocalKind.Local => SsaSlotKind.Local,
                GenLocalKind.Temporary => SsaSlotKind.Temp,
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor)),
            };
            Index = descriptor.Index;
            LclNum = descriptor.LclNum;
            _hasLclNum = true;
        }

        public bool HasLclNum => _hasLclNum;

        public bool Equals(SsaSlot other)
        {
            if (HasLclNum || other.HasLclNum)
                return HasLclNum && other.HasLclNum && LclNum == other.LclNum;

            return Kind == other.Kind && Index == other.Index;
        }

        public override bool Equals(object? obj) => obj is SsaSlot other && Equals(other);

        public override int GetHashCode()
            => HasLclNum ? LclNum : (((int)Kind * 397) ^ Index);

        public int CompareTo(SsaSlot other)
        {
            if (HasLclNum && other.HasLclNum)
                return LclNum.CompareTo(other.LclNum);
            if (HasLclNum != other.HasLclNum)
                return HasLclNum ? -1 : 1;

            int c = Kind.CompareTo(other.Kind);
            return c != 0 ? c : Index.CompareTo(other.Index);
        }

        public override string ToString()
        {
            if (HasLclNum)
                return "V" + LclNum.ToString();

            char prefix = Kind switch
            {
                SsaSlotKind.Arg => 'a',
                SsaSlotKind.Local => 'l',
                SsaSlotKind.Temp => 't',
                _ => '?',
            };
            return prefix + Index.ToString();
        }
    }
    internal readonly struct SsaValueName : IEquatable<SsaValueName>, IComparable<SsaValueName>
    {
        public readonly SsaSlot Slot;
        public readonly int Version;

        public SsaValueName(SsaSlot slot, int version)
        {
            if (version <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(version));
            if (!slot.HasLclNum)
                throw new ArgumentException("SSA value identity must include a concrete lclNum.", nameof(slot));

            Slot = slot;
            Version = version;
        }

        public bool IsReserved => Version == SsaConfig.ReservedSsaNumber;
        public bool IsValid => Version > SsaConfig.ReservedSsaNumber;

        public bool Equals(SsaValueName other) => Slot.Equals(other.Slot) && Version == other.Version;
        public override bool Equals(object? obj) => obj is SsaValueName other && Equals(other);
        public override int GetHashCode() => (Slot.GetHashCode() * 397) ^ Version;

        public int CompareTo(SsaValueName other)
        {
            int c = Slot.CompareTo(other.Slot);
            return c != 0 ? c : Version.CompareTo(other.Version);
        }

        public override string ToString() => $"{Slot}_{Version}";
    }
    internal readonly struct SsaUseDefLink : IEquatable<SsaUseDefLink>
    {
        public readonly SsaValueName Definition;
        public readonly SsaValueName Use;

        public SsaUseDefLink(SsaValueName definition, SsaValueName use)
        {
            if (!definition.Slot.Equals(use.Slot))
                throw new ArgumentException("SSA use-def link must stay within a single base local.", nameof(use));
            if (definition.Version == use.Version)
                throw new ArgumentException("SSA use-def link cannot refer to the definition itself.", nameof(use));

            Definition = definition;
            Use = use;
        }

        public bool Equals(SsaUseDefLink other) => Definition.Equals(other.Definition) && Use.Equals(other.Use);
        public override bool Equals(object? obj) => obj is SsaUseDefLink other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Definition, Use);
        public override string ToString() => Definition.ToString() + " uses " + Use.ToString();
    }
    internal enum SsaDefinitionKind : byte
    {
        Initial,
        Phi,
        Store,
    }
    internal sealed class SsaDescriptor
    {
        public SsaSlot BaseLocal { get; }
        public int SsaNumber { get; }
        public SsaValueName Name => new SsaValueName(BaseLocal, SsaNumber);
        public SsaDefinitionKind DefinitionKind { get; }
        public int DefBlockId { get; }
        public CfgBlock? DefBlock { get; }
        public int DefStatementIndex { get; }
        public int DefTreeId { get; }
        public GenTree? DefNode { get; }
        public SsaPhi? Phi { get; }
        public RuntimeType? Type { get; }
        public GenStackKind StackKind { get; }
        public int UseDefSsaNumber { get; private set; }
        public int PreviousSsaNumber => UseDefSsaNumber;
        public int UseCount { get; private set; }
        public bool HasPhiUse { get; private set; }
        public bool HasGlobalUse { get; private set; }
        public ValueNumberPair ValueNumbers { get; private set; }

        public bool IsInitial => DefinitionKind == SsaDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaDefinitionKind.Store;
        public bool HasUseDefSsaNum => UseDefSsaNumber != SsaConfig.ReservedSsaNumber;
        public bool IsPartialDefinition => HasUseDefSsaNum;
        public SsaValueName? UseDef => HasUseDefSsaNum ? new SsaValueName(BaseLocal, UseDefSsaNumber) : null;
        public SsaValueName? PreviousDefinition => UseDef;

        public SsaDescriptor(
            SsaSlot baseLocal,
            int ssaNumber,
            SsaDefinitionKind definitionKind,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            GenTree? defNode,
            RuntimeType? type,
            GenStackKind stackKind,
            int useDefSsaNumber = SsaConfig.ReservedSsaNumber,
            CfgBlock? defBlock = null,
            SsaPhi? phiDescriptor = null)
        {
            if (ssaNumber <= SsaConfig.ReservedSsaNumber) throw new ArgumentOutOfRangeException(nameof(ssaNumber));
            if (useDefSsaNumber < SsaConfig.ReservedSsaNumber) throw new ArgumentOutOfRangeException(nameof(useDefSsaNumber));
            if (useDefSsaNumber == ssaNumber) throw new InvalidOperationException("SSA use-def link cannot refer to its own definition.");
            if (!baseLocal.HasLclNum) throw new ArgumentException("SSA descriptor base local must include a concrete lclNum.", nameof(baseLocal));
            BaseLocal = baseLocal;
            SsaNumber = ssaNumber;
            DefinitionKind = definitionKind;
            DefBlockId = defBlockId;
            DefBlock = defBlock;
            DefStatementIndex = defStatementIndex;
            DefTreeId = defTreeId;
            DefNode = defNode;
            Phi = phiDescriptor;
            Type = type;
            StackKind = stackKind;
            UseDefSsaNumber = useDefSsaNumber;
            ValueNumbers = default;
        }

        internal void SetUseDefSsaNum(int useDefSsaNumber)
        {
            if (useDefSsaNumber < SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(useDefSsaNumber));
            if (useDefSsaNumber == SsaNumber)
                throw new InvalidOperationException("SSA use-def link cannot refer to its own definition: " + Name + ".");
            UseDefSsaNumber = useDefSsaNumber;
        }

        internal void SetPreviousDefinition(int previousSsaNumber) => SetUseDefSsaNum(previousSsaNumber);

        internal void AddUse(int useBlockId)
        {
            if (UseCount < ushort.MaxValue)
                UseCount++;
            if (DefBlockId >= 0 && useBlockId >= 0 && useBlockId != DefBlockId)
                HasGlobalUse = true;
        }

        internal void AddPhiUse(int useBlockId)
        {
            HasPhiUse = true;
            AddUse(useBlockId);
        }

        internal void SetValueNumbers(ValueNumberPair valueNumbers)
        {
            ValueNumbers = valueNumbers;
        }

        public override string ToString()
        {
            string def = DefinitionKind switch
            {
                SsaDefinitionKind.Initial => "init",
                SsaDefinitionKind.Phi => "phi",
                SsaDefinitionKind.Store => "store",
                _ => DefinitionKind.ToString(),
            };
            string partial = HasUseDefSsaNum ? " use=" + UseDefSsaNumber.ToString() : string.Empty;
            string uses = UseCount != 0 ? " uses=" + UseCount.ToString() : string.Empty;
            string hints = (HasPhiUse ? " phi-use" : string.Empty) + (HasGlobalUse ? " global-use" : string.Empty);
            string vn = ValueNumbers.Liberal.IsValid || ValueNumbers.Conservative.IsValid ? " vn=" + ValueNumbers.ToString() : string.Empty;
            return BaseLocal.ToString() + "_" + SsaNumber.ToString() + " " + def + partial + uses + hints + vn;
        }
    }
    internal sealed class SsaLocalDescriptor
    {
        public SsaSlot Slot { get; }
        public RuntimeType? Type { get; }
        public GenStackKind StackKind { get; }
        public bool AddressExposed { get; }
        public bool IsSsaPromoted { get; }
        public GenLocalDescriptor? LocalDescriptor { get; }
        public ImmutableArray<SsaDescriptor> PerSsaData { get; }

        public SsaLocalDescriptor(
            SsaSlot slot,
            RuntimeType? type,
            GenStackKind stackKind,
            bool addressExposed,
            bool isSsaPromoted,
            GenLocalDescriptor? localDescriptor,
            ImmutableArray<SsaDescriptor> perSsaData)
        {
            Slot = slot;
            Type = type;
            StackKind = stackKind;
            AddressExposed = addressExposed;
            IsSsaPromoted = isSsaPromoted;
            LocalDescriptor = localDescriptor;
            PerSsaData = perSsaData.IsDefault ? ImmutableArray<SsaDescriptor>.Empty : perSsaData;
        }

        public SsaDescriptor GetSsaDefByNumber(int ssaNumber)
        {
            if (ssaNumber <= SsaConfig.ReservedSsaNumber || (uint)ssaNumber >= (uint)PerSsaData.Length)
                throw new ArgumentOutOfRangeException(nameof(ssaNumber));

            var descriptor = PerSsaData[ssaNumber];
            if (descriptor is null || descriptor.SsaNumber != ssaNumber)
                throw new InvalidOperationException("SSA descriptor table is not dense for " + Slot + ".");
            return descriptor;
        }

        public bool TryGetSsaDefByNumber(int ssaNumber, out SsaDescriptor descriptor)
        {
            if (ssaNumber > SsaConfig.ReservedSsaNumber && (uint)ssaNumber < (uint)PerSsaData.Length)
            {
                descriptor = PerSsaData[ssaNumber];
                if (descriptor is not null && descriptor.SsaNumber == ssaNumber)
                    return true;
            }

            descriptor = null!;
            return false;
        }

        public override string ToString()
        {
            int defCount = PerSsaData.IsDefaultOrEmpty ? 0 : Math.Max(0, PerSsaData.Length - 1);
            return Slot.ToString() + " defs=" + defCount.ToString() + (AddressExposed ? " addr-exposed" : string.Empty);
        }
    }
    internal readonly struct SsaValueDefinition
    {
        public readonly SsaValueName Name;
        public readonly int DefBlockId;
        public readonly int DefStatementIndex;
        public readonly int DefTreeId;
        public readonly bool IsInitial;
        public readonly bool IsPhi;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;
        public readonly GenTree? DefNode;
        public readonly SsaPhi? Phi;
        public readonly int UseDefSsaNumber;
        public int PreviousSsaNumber => UseDefSsaNumber;
        public readonly SsaDescriptor Descriptor;

        public SsaValueDefinition(SsaDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Name = descriptor.Name;
            DefBlockId = descriptor.DefBlockId;
            DefStatementIndex = descriptor.DefStatementIndex;
            DefTreeId = descriptor.DefTreeId;
            IsInitial = descriptor.IsInitial;
            IsPhi = descriptor.IsPhi;
            Type = descriptor.Type;
            StackKind = descriptor.StackKind;
            DefNode = descriptor.DefNode;
            Phi = descriptor.Phi;
            UseDefSsaNumber = descriptor.UseDefSsaNumber;
        }

        public SsaValueDefinition(
            SsaValueName name,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            bool isInitial,
            bool isPhi,
            RuntimeType? type,
            GenStackKind stackKind)
            : this(new SsaDescriptor(
                name.Slot,
                name.Version,
                isInitial ? SsaDefinitionKind.Initial : isPhi ? SsaDefinitionKind.Phi : SsaDefinitionKind.Store,
                defBlockId,
                defStatementIndex,
                defTreeId,
                defNode: null,
                type,
                stackKind))
        {
        }
    }
    internal readonly struct SsaSlotInfo
    {
        public readonly SsaSlot Slot;
        public readonly RuntimeType? Type;
        public readonly GenStackKind StackKind;
        public readonly bool AddressExposed;
        public readonly bool MemoryAliased;
        public readonly GenLocalCategory Category;
        public readonly int LclNum;
        public readonly int VarIndex;
        public readonly bool Tracked;
        public readonly bool InSsa;
        public readonly GenLocalDescriptor? LocalDescriptor;

        public SsaSlotInfo(
            SsaSlot slot,
            RuntimeType? type,
            GenStackKind stackKind,
            bool addressExposed,
            bool memoryAliased = false,
            GenLocalCategory category = GenLocalCategory.Unclassified,
            int lclNum = -1,
            int varIndex = -1,
            bool tracked = false,
            bool inSsa = false,
            GenLocalDescriptor? localDescriptor = null)
        {
            Slot = slot;
            Type = type;
            StackKind = stackKind;
            AddressExposed = addressExposed;
            MemoryAliased = memoryAliased;
            Category = category;
            LclNum = lclNum;
            VarIndex = varIndex;
            Tracked = tracked;
            InSsa = inSsa;
            LocalDescriptor = localDescriptor;
        }

        public bool IsScalarSsaCandidate =>
            InSsa &&
            Tracked &&
            VarIndex >= 0 &&
            !AddressExposed &&
            !MemoryAliased &&
            LocalDescriptor is { CanBeSsaRenamedAsScalar: true };
    }
    internal readonly struct SsaPhiInput
    {
        public readonly int PredecessorBlockId;
        public readonly SsaValueName Value;

        public SsaPhiInput(int predecessorBlockId, SsaValueName value)
        {
            PredecessorBlockId = predecessorBlockId;
            Value = value;
        }
    }
    internal sealed class SsaPhi
    {
        public int BlockId { get; }
        public SsaSlot Slot { get; }
        public SsaValueName Target { get; }
        public ImmutableArray<SsaPhiInput> Inputs { get; }

        public SsaPhi(int blockId, SsaSlot slot, SsaValueName target, ImmutableArray<SsaPhiInput> inputs)
        {
            BlockId = blockId;
            Slot = slot;
            Target = target;
            Inputs = inputs.IsDefault ? ImmutableArray<SsaPhiInput>.Empty : inputs;
        }
    }
    internal enum SsaMemoryKind : byte
    {
        ByrefExposed = 0,
        GcHeap = 1,
    }
    internal enum SsaMemoryKindSet : byte
    {
        None = 0,
        ByrefExposed = 1,
        GcHeap = 2,
        All = ByrefExposed | GcHeap,
    }
    internal static class SsaMemoryKinds
    {
        public static readonly ImmutableArray<SsaMemoryKind> All = ImmutableArray.Create(SsaMemoryKind.ByrefExposed, SsaMemoryKind.GcHeap);

        public static SsaMemoryKindSet SetOf(SsaMemoryKind kind) => (SsaMemoryKindSet)(1 << (int)kind);

        public static bool Contains(this SsaMemoryKindSet set, SsaMemoryKind kind) => (set & SetOf(kind)) != 0;

        public static SsaMemoryKindSet Add(this SsaMemoryKindSet set, SsaMemoryKind kind) => set | SetOf(kind);

        public static SsaMemoryKindSet Remove(this SsaMemoryKindSet set, SsaMemoryKind kind) => set & ~SetOf(kind);

        public static string Name(SsaMemoryKind kind)
            => kind switch
            {
                SsaMemoryKind.ByrefExposed => "ByrefExposed",
                SsaMemoryKind.GcHeap => "GcHeap",
                _ => kind.ToString(),
            };
    }
    internal readonly struct SsaMemoryValueName : IEquatable<SsaMemoryValueName>, IComparable<SsaMemoryValueName>
    {
        public readonly SsaMemoryKind Kind;
        public readonly int Version;

        public SsaMemoryValueName(SsaMemoryKind kind, int version)
        {
            if (version <= SsaConfig.ReservedSsaNumber)
                throw new ArgumentOutOfRangeException(nameof(version));

            Kind = kind;
            Version = version;
        }

        public bool Equals(SsaMemoryValueName other) => Kind == other.Kind && Version == other.Version;
        public override bool Equals(object? obj) => obj is SsaMemoryValueName other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Version;

        public int CompareTo(SsaMemoryValueName other)
        {
            int c = Kind.CompareTo(other.Kind);
            return c != 0 ? c : Version.CompareTo(other.Version);
        }

        public override string ToString()
            => "M" + SsaMemoryKinds.Name(Kind) + "_" + Version.ToString();
    }
    internal readonly struct SsaMemoryPhiInput
    {
        public readonly int PredecessorBlockId;
        public readonly SsaMemoryValueName Value;

        public SsaMemoryPhiInput(int predecessorBlockId, SsaMemoryValueName value)
        {
            PredecessorBlockId = predecessorBlockId;
            Value = value;
        }
    }
    internal sealed class SsaMemoryPhi
    {
        public int BlockId { get; }
        public SsaMemoryKind Kind { get; }
        public SsaMemoryValueName Target { get; }
        public ImmutableArray<SsaMemoryPhiInput> Inputs { get; }

        public SsaMemoryPhi(int blockId, SsaMemoryKind kind, SsaMemoryValueName target, ImmutableArray<SsaMemoryPhiInput> inputs)
        {
            if (target.Kind != kind)
                throw new ArgumentException("Memory phi target kind does not match phi kind.", nameof(target));

            BlockId = blockId;
            Kind = kind;
            Target = target;
            Inputs = inputs.IsDefault ? ImmutableArray<SsaMemoryPhiInput>.Empty : inputs;
        }
    }
    internal enum SsaMemoryDefinitionKind : byte
    {
        Initial,
        Phi,
        Store,
        BlockOut,
    }
    internal sealed class SsaMemoryDescriptor
    {
        public SsaMemoryValueName Name { get; }
        public SsaMemoryDefinitionKind DefinitionKind { get; }
        public int DefBlockId { get; }
        public CfgBlock? DefBlock { get; }
        public int DefStatementIndex { get; }
        public int DefTreeId { get; }
        public GenTree? DefNode { get; }
        public SsaMemoryPhi? Phi { get; }
        public int UseCount { get; private set; }
        public bool HasPhiUse { get; private set; }
        public bool HasGlobalUse { get; private set; }
        public ValueNumber ValueNumber { get; private set; }

        public bool IsInitial => DefinitionKind == SsaMemoryDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaMemoryDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaMemoryDefinitionKind.Store;
        public bool IsBlockOut => DefinitionKind == SsaMemoryDefinitionKind.BlockOut;

        public SsaMemoryDescriptor(
            SsaMemoryValueName name,
            SsaMemoryDefinitionKind definitionKind,
            int defBlockId,
            int defStatementIndex,
            int defTreeId,
            GenTree? defNode,
            CfgBlock? defBlock = null,
            SsaMemoryPhi? phi = null)
        {
            Name = name;
            DefinitionKind = definitionKind;
            DefBlockId = defBlockId;
            DefBlock = defBlock;
            DefStatementIndex = defStatementIndex;
            DefTreeId = defTreeId;
            DefNode = defNode;
            Phi = phi;
        }

        internal void AddUse(int useBlockId)
        {
            if (UseCount < ushort.MaxValue)
                UseCount++;
            if (DefBlockId >= 0 && useBlockId >= 0 && useBlockId != DefBlockId)
                HasGlobalUse = true;
        }

        internal void AddPhiUse(int useBlockId)
        {
            HasPhiUse = true;
            AddUse(useBlockId);
        }

        internal void SetValueNumber(ValueNumber valueNumber)
        {
            ValueNumber = valueNumber;
        }
    }
    internal readonly struct SsaMemoryDefinition
    {
        public readonly SsaMemoryValueName Name;
        public readonly SsaMemoryDefinitionKind DefinitionKind;
        public readonly int DefBlockId;
        public readonly int DefStatementIndex;
        public readonly int DefTreeId;
        public readonly GenTree? DefNode;
        public readonly SsaMemoryPhi? Phi;
        public readonly SsaMemoryDescriptor Descriptor;

        public SsaMemoryDefinition(SsaMemoryDescriptor descriptor)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Name = descriptor.Name;
            DefinitionKind = descriptor.DefinitionKind;
            DefBlockId = descriptor.DefBlockId;
            DefStatementIndex = descriptor.DefStatementIndex;
            DefTreeId = descriptor.DefTreeId;
            DefNode = descriptor.DefNode;
            Phi = descriptor.Phi;
        }

        public bool IsInitial => DefinitionKind == SsaMemoryDefinitionKind.Initial;
        public bool IsPhi => DefinitionKind == SsaMemoryDefinitionKind.Phi;
        public bool IsStore => DefinitionKind == SsaMemoryDefinitionKind.Store;
        public bool IsBlockOut => DefinitionKind == SsaMemoryDefinitionKind.BlockOut;
    }
    internal sealed class SsaTree
    {
        public GenTree Source { get; }
        public GenTreeKind Kind => Source.Kind;
        public ImmutableArray<SsaTree> Operands { get; }
        public SsaValueName? Value { get; }
        public SsaValueName? StoreTarget { get; }
        public SsaValueName? LocalFieldBaseValue { get; }
        public RuntimeField? LocalField { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryUses { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryDefinitions { get; }
        public bool IsPartialDefinition => StoreTarget.HasValue && LocalField is not null;
        public bool IsLocalFieldAccess => (LocalFieldBaseValue.HasValue || IsPartialDefinition) && LocalField is not null;
        public bool HasMemoryEffects => !MemoryUses.IsDefaultOrEmpty || !MemoryDefinitions.IsDefaultOrEmpty;

        public SsaTree(
            GenTree source,
            ImmutableArray<SsaTree> operands,
            SsaValueName? value = null,
            SsaValueName? storeTarget = null,
            SsaValueName? localFieldBaseValue = null,
            RuntimeField? localField = null,
            ImmutableArray<SsaMemoryValueName> memoryUses = default,
            ImmutableArray<SsaMemoryValueName> memoryDefinitions = default)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Operands = operands.IsDefault ? ImmutableArray<SsaTree>.Empty : operands;
            Value = value;
            StoreTarget = storeTarget;
            LocalFieldBaseValue = localFieldBaseValue;
            LocalField = localField;
            MemoryUses = memoryUses.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryUses;
            MemoryDefinitions = memoryDefinitions.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryDefinitions;
        }

        public bool TryGetMemoryUse(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryUses, kind, out value);

        public bool TryGetMemoryDefinition(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryDefinitions, kind, out value);

        private static bool TryGetMemoryValue(ImmutableArray<SsaMemoryValueName> values, SsaMemoryKind kind, out SsaMemoryValueName value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Kind == kind)
                {
                    value = values[i];
                    return true;
                }
            }

            value = default;
            return false;
        }

        public override string ToString() => SsaDumper.FormatTree(this);
    }
    internal readonly struct SsaTreeLinearNode
    {
        public readonly SsaTree Tree;
        public readonly int StatementIndex;
        public readonly int TreeIndex;
        public readonly int BlockOrdinal;

        public SsaTreeLinearNode(SsaTree tree, int statementIndex, int treeIndex, int blockOrdinal)
        {
            Tree = tree ?? throw new ArgumentNullException(nameof(tree));
            StatementIndex = statementIndex;
            TreeIndex = treeIndex;
            BlockOrdinal = blockOrdinal;
        }
    }
    internal static class SsaTreeLinearOrder
    {
        public static ImmutableArray<SsaTree> BuildStatement(SsaTree root)
        {
            if (root is null)
                throw new ArgumentNullException(nameof(root));

            var builder = ImmutableArray.CreateBuilder<SsaTree>();
            var seen = new HashSet<SsaTree>(ReferenceEqualityComparer<SsaTree>.Instance);
            CollectUnique(root, seen, builder);
            builder.Sort(static (left, right) => CompareSourceOrder(left.Source, right.Source));

            var result = builder.ToImmutable();
            ValidateStatementTreeList(root, result);
            return result;
        }

        public static ImmutableArray<ImmutableArray<SsaTree>> BuildStatements(ImmutableArray<SsaTree> statements)
        {
            if (statements.IsDefaultOrEmpty)
                return ImmutableArray<ImmutableArray<SsaTree>>.Empty;

            var builder = ImmutableArray.CreateBuilder<ImmutableArray<SsaTree>>(statements.Length);
            for (int i = 0; i < statements.Length; i++)
                builder.Add(BuildStatement(statements[i]));
            return builder.ToImmutable();
        }

        public static ImmutableArray<SsaTreeLinearNode> BuildBlock(ImmutableArray<ImmutableArray<SsaTree>> statementTreeLists)
        {
            if (statementTreeLists.IsDefaultOrEmpty)
                return ImmutableArray<SsaTreeLinearNode>.Empty;

            int count = 0;
            for (int i = 0; i < statementTreeLists.Length; i++)
                count += statementTreeLists[i].Length;

            var builder = ImmutableArray.CreateBuilder<SsaTreeLinearNode>(count);
            int ordinal = 0;
            for (int s = 0; s < statementTreeLists.Length; s++)
            {
                var list = statementTreeLists[s];
                for (int t = 0; t < list.Length; t++)
                    builder.Add(new SsaTreeLinearNode(list[t], s, t, ordinal++));
            }
            return builder.ToImmutable();
        }

        private static void CollectUnique(SsaTree tree, HashSet<SsaTree> seen, ImmutableArray<SsaTree>.Builder builder)
        {
            if (!seen.Add(tree))
                return;

            builder.Add(tree);
            for (int i = 0; i < tree.Operands.Length; i++)
                CollectUnique(tree.Operands[i], seen, builder);
        }

        private static int CompareSourceOrder(GenTree left, GenTree right)
        {
            int block = left.LinearBlockId.CompareTo(right.LinearBlockId);
            if (block != 0) return block;

            int ordinal = left.LinearOrdinal.CompareTo(right.LinearOrdinal);
            if (ordinal != 0) return ordinal;

            return left.Id.CompareTo(right.Id);
        }

        private static void ValidateStatementTreeList(SsaTree root, ImmutableArray<SsaTree> treeList)
        {
            if (treeList.IsDefaultOrEmpty)
                throw new InvalidOperationException("SSA statement tree-list is empty for root " + root.Source.Id.ToString() + ".");

            if (!ReferenceEquals(treeList[treeList.Length - 1], root))
                throw new InvalidOperationException("SSA statement tree-list root is not last in source execution order for root " + root.Source.Id.ToString() + ".");

            var ordinalByTree = new Dictionary<SsaTree, int>(ReferenceEqualityComparer<SsaTree>.Instance);
            for (int i = 0; i < treeList.Length; i++)
            {
                if (!ordinalByTree.TryAdd(treeList[i], i))
                    throw new InvalidOperationException("SSA statement tree-list contains duplicate source node " + treeList[i].Source.Id.ToString() + ".");
            }

            for (int i = 0; i < treeList.Length; i++)
            {
                var tree = treeList[i];
                for (int op = 0; op < tree.Operands.Length; op++)
                {
                    var operand = tree.Operands[op];
                    if (!ordinalByTree.TryGetValue(operand, out int operandOrdinal))
                        throw new InvalidOperationException("SSA statement tree-list for root " + root.Source.Id.ToString() + " does not contain operand " + operand.Source.Id.ToString() + " of node " + tree.Source.Id.ToString() + ".");
                }
            }
        }
    }
    internal sealed class SsaBlock
    {
        public CfgBlock CfgBlock { get; }
        public ImmutableArray<SsaPhi> Phis { get; }
        public ImmutableArray<SsaMemoryPhi> MemoryPhis { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryIn { get; }
        public ImmutableArray<SsaMemoryValueName> MemoryOut { get; }
        public ImmutableArray<SsaTree> Statements { get; }
        public ImmutableArray<ImmutableArray<SsaTree>> StatementTreeLists { get; }
        public ImmutableArray<SsaTreeLinearNode> TreeList { get; }

        public int Id => CfgBlock.Id;

        public SsaBlock(
            CfgBlock cfgBlock,
            ImmutableArray<SsaPhi> phis,
            ImmutableArray<SsaTree> statements,
            ImmutableArray<SsaMemoryPhi> memoryPhis = default,
            ImmutableArray<SsaMemoryValueName> memoryIn = default,
            ImmutableArray<SsaMemoryValueName> memoryOut = default,
            ImmutableArray<ImmutableArray<SsaTree>> statementTreeLists = default)
        {
            CfgBlock = cfgBlock ?? throw new ArgumentNullException(nameof(cfgBlock));
            Phis = phis.IsDefault ? ImmutableArray<SsaPhi>.Empty : phis;
            MemoryPhis = memoryPhis.IsDefault ? ImmutableArray<SsaMemoryPhi>.Empty : memoryPhis;
            MemoryIn = memoryIn.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryIn;
            MemoryOut = memoryOut.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : memoryOut;
            Statements = statements.IsDefault ? ImmutableArray<SsaTree>.Empty : statements;
            StatementTreeLists = statementTreeLists.IsDefault
                ? SsaTreeLinearOrder.BuildStatements(Statements)
                : ValidateStatementTreeLists(Statements, statementTreeLists);
            TreeList = SsaTreeLinearOrder.BuildBlock(StatementTreeLists);
        }

        private static ImmutableArray<ImmutableArray<SsaTree>> ValidateStatementTreeLists(
            ImmutableArray<SsaTree> statements,
            ImmutableArray<ImmutableArray<SsaTree>> statementTreeLists)
        {
            if (statementTreeLists.Length != statements.Length)
                throw new InvalidOperationException("SSA block statement tree-list count does not match statement count.");

            for (int s = 0; s < statements.Length; s++)
            {
                var list = statementTreeLists[s];
                if (list.IsDefaultOrEmpty)
                    throw new InvalidOperationException("SSA statement tree-list is empty for statement " + s.ToString() + ".");

                if (!ReferenceEquals(list[list.Length - 1], statements[s]))
                    throw new InvalidOperationException("SSA statement tree-list root is not last for statement " + s.ToString() + ".");

                var ordinalByTree = new Dictionary<SsaTree, int>(ReferenceEqualityComparer<SsaTree>.Instance);
                for (int i = 0; i < list.Length; i++)
                {
                    if (!ordinalByTree.TryAdd(list[i], i))
                        throw new InvalidOperationException("SSA statement tree-list contains duplicate node " + list[i].Source.Id.ToString() + ".");
                }

                for (int i = 0; i < list.Length; i++)
                {
                    var tree = list[i];
                    for (int op = 0; op < tree.Operands.Length; op++)
                    {
                        if (!ordinalByTree.TryGetValue(tree.Operands[op], out int operandOrdinal))
                            throw new InvalidOperationException("SSA statement tree-list does not contain operand " + tree.Operands[op].Source.Id.ToString() + " of node " + tree.Source.Id.ToString() + ".");
                    }
                }
            }

            return statementTreeLists;
        }

        public bool TryGetMemoryIn(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryIn, kind, out value);

        public bool TryGetMemoryOut(SsaMemoryKind kind, out SsaMemoryValueName value)
            => TryGetMemoryValue(MemoryOut, kind, out value);

        private static bool TryGetMemoryValue(ImmutableArray<SsaMemoryValueName> values, SsaMemoryKind kind, out SsaMemoryValueName value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Kind == kind)
                {
                    value = values[i];
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
    internal sealed class SsaMethod
    {
        public GenTreeMethod GenTreeMethod { get; }
        public ControlFlowGraph Cfg { get; }
        public ImmutableArray<SsaSlotInfo> Slots { get; }
        public ImmutableArray<SsaLocalDescriptor> SsaLocalDescriptors { get; }
        public ImmutableArray<SsaValueName> InitialValues { get; }
        public ImmutableArray<SsaMemoryValueName> InitialMemoryValues { get; }
        public ImmutableArray<SsaValueDefinition> ValueDefinitions { get; }
        public ImmutableArray<SsaMemoryDefinition> MemoryDefinitions { get; }
        public ImmutableArray<SsaBlock> Blocks { get; }
        public SsaValueNumberingResult? ValueNumbers { get; }

        public SsaMethod(
            GenTreeMethod genTreeMethod,
            ControlFlowGraph cfg,
            ImmutableArray<SsaSlotInfo> slots,
            ImmutableArray<SsaValueName> initialValues,
            ImmutableArray<SsaValueDefinition> valueDefinitions,
            ImmutableArray<SsaBlock> blocks,
            SsaValueNumberingResult? valueNumbers = null,
            ImmutableArray<SsaLocalDescriptor> ssaLocalDescriptors = default,
            ImmutableArray<SsaMemoryValueName> initialMemoryValues = default,
            ImmutableArray<SsaMemoryDefinition> memoryDefinitions = default)
        {
            GenTreeMethod = genTreeMethod ?? throw new ArgumentNullException(nameof(genTreeMethod));
            Cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Slots = slots.IsDefault ? ImmutableArray<SsaSlotInfo>.Empty : slots;
            SsaLocalDescriptors = ssaLocalDescriptors.IsDefault ? ImmutableArray<SsaLocalDescriptor>.Empty : ssaLocalDescriptors;
            InitialValues = initialValues.IsDefault ? ImmutableArray<SsaValueName>.Empty : initialValues;
            InitialMemoryValues = initialMemoryValues.IsDefault ? ImmutableArray<SsaMemoryValueName>.Empty : initialMemoryValues;
            ValueDefinitions = valueDefinitions.IsDefault ? ImmutableArray<SsaValueDefinition>.Empty : valueDefinitions;
            MemoryDefinitions = memoryDefinitions.IsDefault ? ImmutableArray<SsaMemoryDefinition>.Empty : memoryDefinitions;
            Blocks = blocks.IsDefault ? ImmutableArray<SsaBlock>.Empty : blocks;
            ValueNumbers = valueNumbers;
        }

        public bool TryGetSsaLocalDescriptor(SsaSlot slot, out SsaLocalDescriptor descriptor)
        {
            for (int i = 0; i < SsaLocalDescriptors.Length; i++)
            {
                if (SsaLocalDescriptors[i].Slot.Equals(slot))
                {
                    descriptor = SsaLocalDescriptors[i];
                    return true;
                }
            }

            descriptor = null!;
            return false;
        }

        public SsaLocalDescriptor GetSsaLocalDescriptor(SsaSlot slot)
        {
            if (TryGetSsaLocalDescriptor(slot, out var descriptor))
                return descriptor;

            throw new InvalidOperationException("SSA local descriptor is missing for " + slot + ".");
        }

        public bool TryGetSsaDescriptor(SsaValueName name, out SsaDescriptor descriptor)
        {
            if (TryGetSsaLocalDescriptor(name.Slot, out var local))
                return local.TryGetSsaDefByNumber(name.Version, out descriptor);

            descriptor = null!;
            return false;
        }

        public SsaDescriptor GetSsaDescriptor(SsaValueName name)
        {
            if (TryGetSsaDescriptor(name, out var descriptor))
                return descriptor;

            throw new InvalidOperationException("SSA descriptor is missing for " + name + ".");
        }
    }
    internal sealed class SsaProgram
    {
        public ImmutableArray<SsaMethod> Methods { get; }
        public IReadOnlyDictionary<int, SsaMethod> MethodsByRuntimeMethodId { get; }

        public SsaProgram(ImmutableArray<SsaMethod> methods)
        {
            Methods = methods.IsDefault ? ImmutableArray<SsaMethod>.Empty : methods;
            var map = new Dictionary<int, SsaMethod>();
            for (int i = 0; i < Methods.Length; i++)
                map[Methods[i].GenTreeMethod.RuntimeMethod.MethodId] = Methods[i];
            MethodsByRuntimeMethodId = map;
        }
    }
    internal enum SsaLocalAccessKind : byte
    {
        None,
        Use,
        FullDefinition,
        PartialDefinition,
        Address,
    }
    internal readonly struct SsaLocalAccess
    {
        public readonly SsaLocalAccessKind Kind;
        public readonly SsaSlot Slot;
        public readonly SsaSlot BaseSlot;
        public readonly RuntimeField? Field;
        public readonly GenTree? Receiver;
        public readonly int ReceiverOperandIndex;

        public SsaLocalAccess(
            SsaLocalAccessKind kind,
            SsaSlot slot,
            RuntimeField? field = null,
            GenTree? receiver = null,
            int receiverOperandIndex = -1,
            SsaSlot? baseSlot = null)
        {
            Kind = kind;
            Slot = slot;
            BaseSlot = baseSlot ?? slot;
            Field = field;
            Receiver = receiver;
            ReceiverOperandIndex = receiverOperandIndex;
        }

        public bool IsUse => Kind == SsaLocalAccessKind.Use;
        public bool IsFullDefinition => Kind == SsaLocalAccessKind.FullDefinition;
        public bool IsPartialDefinition => Kind == SsaLocalAccessKind.PartialDefinition;
        public bool IsAddress => Kind == SsaLocalAccessKind.Address;
        public bool IsDefinition => IsFullDefinition || IsPartialDefinition;
        public bool IsPromotedFieldAccess => Field is not null && !Slot.Equals(BaseSlot);
    }
    internal static class SsaSlotHelpers
    {
        public static bool TryGetLoadSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (TryGetDirectLoadSlot(node, out slot))
                return true;

            if (TryGetLocalFieldAccess(node, out var access) && access.Kind == SsaLocalAccessKind.Use)
            {
                slot = access.Slot;
                return true;
            }

            slot = default;
            return false;
        }

        public static bool TryGetStoreSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (TryGetDirectStoreSlot(node, out slot))
                return true;

            if (TryGetLocalFieldAccess(node, out var access) && access.IsDefinition)
            {
                slot = access.Slot;
                return true;
            }

            slot = default;
            return false;
        }

        public static bool TryGetDirectLoadSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.Arg:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.Local:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                case GenTreeKind.Temp:
                    return TryMakeSlot(node, SsaSlotKind.Temp, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetDirectStoreSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.StoreArg:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.StoreLocal:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                case GenTreeKind.StoreTemp:
                    return TryMakeSlot(node, SsaSlotKind.Temp, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetAddressExposedSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            switch (node.Kind)
            {
                case GenTreeKind.ArgAddr:
                    return TryMakeSlot(node, SsaSlotKind.Arg, out slot);
                case GenTreeKind.LocalAddr:
                    return TryMakeSlot(node, SsaSlotKind.Local, out slot);
                case GenTreeKind.TempAddr:
                    return TryMakeSlot(node, SsaSlotKind.Temp, out slot);
                default:
                    slot = default;
                    return false;
            }
        }

        public static bool TryGetLocalFieldAccess(GenTree node, out SsaLocalAccess access)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (node.Kind == GenTreeKind.Field && node.Operands.Length != 0)
            {
                var receiver = node.Operands[0];
                if (TryGetContainedLocalAddressSlot(receiver, out var parentSlot))
                {
                    var slot = ResolvePromotedFieldSlot(receiver, parentSlot, node.Field);
                    access = new SsaLocalAccess(SsaLocalAccessKind.Use, slot, node.Field, receiver, 0, parentSlot);
                    return true;
                }
            }

            if (node.Kind == GenTreeKind.StoreField && node.Operands.Length >= 2)
            {
                var receiver = node.Operands[0];
                if (TryGetContainedLocalAddressSlot(receiver, out var parentSlot))
                {
                    var slot = ResolvePromotedFieldSlot(receiver, parentSlot, node.Field);
                    var kind = slot.Equals(parentSlot) ? SsaLocalAccessKind.PartialDefinition : SsaLocalAccessKind.FullDefinition;
                    access = new SsaLocalAccess(kind, slot, node.Field, receiver, 0, parentSlot);
                    return true;
                }
            }

            access = default;
            return false;
        }

        private static SsaSlot ResolvePromotedFieldSlot(GenTree receiver, SsaSlot parentSlot, RuntimeField? field)
        {
            if (field is not null && receiver.LocalDescriptor is not null && receiver.LocalDescriptor.TryGetPromotedField(field, out var fieldDescriptor))
                return new SsaSlot(fieldDescriptor);

            return parentSlot;
        }

        public static bool IsContainedLocalFieldAddressUse(GenTree parent, int operandIndex)
        {
            if (parent is null)
                return false;

            return operandIndex == 0 && parent.Kind is GenTreeKind.Field or GenTreeKind.StoreField;
        }

        private static bool TryGetContainedLocalAddressSlot(GenTree node, out SsaSlot slot)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            if (node.Kind == GenTreeKind.ArgAddr)
                return TryMakeSlot(node, SsaSlotKind.Arg, out slot);

            if (node.Kind == GenTreeKind.LocalAddr)
                return TryMakeSlot(node, SsaSlotKind.Local, out slot);

            if (node.Kind == GenTreeKind.TempAddr)
                return TryMakeSlot(node, SsaSlotKind.Temp, out slot);

            slot = default;
            return false;
        }

        private static bool TryMakeSlot(GenTree node, SsaSlotKind expectedKind, out SsaSlot slot)
        {
            if (node.LocalDescriptor is not null)
            {
                slot = new SsaSlot(node.LocalDescriptor);
                if (slot.Kind != expectedKind)
                    throw new InvalidOperationException("GenTree local descriptor kind does not match node kind: " + node + ".");
                if (node.LocalDescriptor.Index != node.Int32)
                    throw new InvalidOperationException("GenTree local descriptor index does not match node index: " + node + ".");
                return true;
            }

            slot = new SsaSlot(expectedKind, node.Int32);
            return true;
        }
    }
    internal sealed class GenTreeLocalTrackingResult
    {
        public ImmutableArray<SsaSlotInfo> AllSlots { get; }
        public ImmutableArray<SsaSlot> TrackedSlots { get; }
        public ImmutableArray<SsaSlot> SsaCandidateSlots { get; }
        public TrackedLocalTable TrackedLocals { get; }

        public GenTreeLocalTrackingResult(
            ImmutableArray<SsaSlotInfo> allSlots,
            ImmutableArray<SsaSlot> trackedSlots,
            ImmutableArray<SsaSlot> ssaCandidateSlots,
            TrackedLocalTable trackedLocals)
        {
            AllSlots = allSlots.IsDefault ? ImmutableArray<SsaSlotInfo>.Empty : allSlots;
            TrackedSlots = trackedSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : trackedSlots;
            SsaCandidateSlots = ssaCandidateSlots.IsDefault ? ImmutableArray<SsaSlot>.Empty : ssaCandidateSlots;
            TrackedLocals = trackedLocals ?? TrackedLocalTable.Empty;
            if (TrackedLocals.Count != TrackedSlots.Length)
                throw new InvalidOperationException("Tracked local table and tracked local slot list disagree.");
        }
    }
    internal sealed class TrackedLocalTable
    {
        public static readonly TrackedLocalTable Empty = new TrackedLocalTable(ImmutableArray<SsaSlot>.Empty);

        private readonly Dictionary<SsaSlot, int> _varIndexBySlot;

        public ImmutableArray<SsaSlot> Slots { get; }
        public int Count => Slots.Length;

        public TrackedLocalTable(ImmutableArray<SsaSlot> slots)
        {
            Slots = slots.IsDefault ? ImmutableArray<SsaSlot>.Empty : slots;
            _varIndexBySlot = new Dictionary<SsaSlot, int>(Slots.Length);

            for (int i = 0; i < Slots.Length; i++)
            {
                if (!_varIndexBySlot.TryAdd(Slots[i], i))
                    throw new InvalidOperationException("Duplicate tracked local slot " + Slots[i] + ".");
            }
        }

        public bool Contains(SsaSlot slot) => _varIndexBySlot.ContainsKey(slot);

        public bool TryGetVarIndex(SsaSlot slot, out int varIndex) => _varIndexBySlot.TryGetValue(slot, out varIndex);

        public int GetVarIndex(SsaSlot slot)
        {
            if (_varIndexBySlot.TryGetValue(slot, out int varIndex))
                return varIndex;

            throw new InvalidOperationException("Local " + slot + " is not a tracked local.");
        }

        public SsaSlot GetSlot(int varIndex)
        {
            if ((uint)varIndex >= (uint)Slots.Length)
                throw new ArgumentOutOfRangeException(nameof(varIndex));
            return Slots[varIndex];
        }

        public TrackedLocalSet NewEmptySet() => new TrackedLocalSet(this);
    }
    internal sealed class TrackedLocalSet
    {
        private readonly ulong[] _bits;

        public TrackedLocalTable Table { get; }
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _bits.Length; i++)
                    count += PopCount(_bits[i]);
                return count;
            }
        }

        public TrackedLocalSet(TrackedLocalTable table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            _bits = new ulong[(Table.Count + 63) >> 6];
        }

        private TrackedLocalSet(TrackedLocalTable table, ulong[] bits)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            _bits = bits ?? throw new ArgumentNullException(nameof(bits));
        }

        public TrackedLocalSet Clone()
        {
            var copy = new ulong[_bits.Length];
            Array.Copy(_bits, copy, _bits.Length);
            return new TrackedLocalSet(Table, copy);
        }
        public void Clear()
        {
            Array.Clear(_bits, 0, _bits.Length);
        }

        public void CopyFrom(TrackedLocalSet other)
        {
            CheckCompatible(other);
            Array.Copy(other._bits, _bits, _bits.Length);
        }

        public bool CopyFromIfChanged(TrackedLocalSet other)
        {
            CheckCompatible(other);
            bool changed = false;
            for (int i = 0; i < _bits.Length; i++)
            {
                ulong next = other._bits[i];
                changed |= _bits[i] != next;
                _bits[i] = next;
            }
            return changed;
        }

        public bool Add(SsaSlot slot)
        {
            if (!Table.TryGetVarIndex(slot, out int varIndex))
                return false;
            return AddIndex(varIndex);
        }

        public bool AddIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            int word = varIndex >> 6;
            ulong mask = 1UL << (varIndex & 63);
            ulong old = _bits[word];
            _bits[word] = old | mask;
            return (old & mask) == 0;
        }

        public bool Remove(SsaSlot slot)
        {
            if (!Table.TryGetVarIndex(slot, out int varIndex))
                return false;
            return RemoveIndex(varIndex);
        }

        public bool RemoveIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            int word = varIndex >> 6;
            ulong mask = 1UL << (varIndex & 63);
            ulong old = _bits[word];
            _bits[word] = old & ~mask;
            return (old & mask) != 0;
        }

        public bool Contains(SsaSlot slot)
            => Table.TryGetVarIndex(slot, out int varIndex) && ContainsIndex(varIndex);

        public bool ContainsIndex(int varIndex)
        {
            CheckVarIndex(varIndex);
            return (_bits[varIndex >> 6] & (1UL << (varIndex & 63))) != 0;
        }

        public bool UnionWith(TrackedLocalSet other)
        {
            CheckCompatible(other);
            bool changed = false;
            for (int i = 0; i < _bits.Length; i++)
            {
                ulong old = _bits[i];
                ulong next = old | other._bits[i];
                _bits[i] = next;
                changed |= next != old;
            }
            return changed;
        }

        public bool ExceptWith(TrackedLocalSet other)
        {
            CheckCompatible(other);
            bool changed = false;
            for (int i = 0; i < _bits.Length; i++)
            {
                ulong old = _bits[i];
                ulong next = old & ~other._bits[i];
                _bits[i] = next;
                changed |= next != old;
            }
            return changed;
        }

        public bool SetEquals(TrackedLocalSet other)
        {
            CheckCompatible(other);
            for (int i = 0; i < _bits.Length; i++)
            {
                if (_bits[i] != other._bits[i])
                    return false;
            }
            return true;
        }

        public ImmutableArray<SsaSlot> ToImmutableSlots()
        {
            var builder = ImmutableArray.CreateBuilder<SsaSlot>();
            for (int i = 0; i < Table.Count; i++)
            {
                if (ContainsIndex(i))
                    builder.Add(Table.GetSlot(i));
            }
            return builder.ToImmutable();
        }

        private void CheckVarIndex(int varIndex)
        {
            if ((uint)varIndex >= (uint)Table.Count)
                throw new ArgumentOutOfRangeException(nameof(varIndex));
        }

        private void CheckCompatible(TrackedLocalSet other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            if (!ReferenceEquals(Table, other.Table))
                throw new InvalidOperationException("Tracked local bitsets were built from different tracked-local tables.");
        }

        private static int PopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        public override string ToString() => "{" + string.Join(", ", ToImmutableSlots()) + "}";
    }
    internal static class GenTreeLocalTracking
    {
        public static GenTreeLocalTrackingResult AssignTrackedLocals(GenTreeMethod method, ControlFlowGraph cfg)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));
            if (cfg is null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.Blocks.Length != method.Blocks.Length)
                throw new InvalidOperationException("local tracking requires a CFG that matches the method block count.");

            ResetDescriptors(method.ArgDescriptors);
            ResetDescriptors(method.LocalDescriptors);
            ResetDescriptors(method.TempDescriptors);
            method.EnsurePromotedStructFieldLocals();

            var addressExposed = new HashSet<SsaSlot>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    CollectAddressExposed(statements[s], addressExposed);
            }

            var localStorageByRefAliases = BuildLocalStorageByRefAliases(method);
            var structPromotionBlockedParents = BuildStructPromotionBlockedParents(method);
            var weightedUses = ComputeWeightedSlotUses(method, cfg);
            var allSlots = new List<SsaSlotInfo>();
            var trackingCandidates = new List<SsaSlot>();
            var descriptors = new Dictionary<SsaSlot, GenLocalDescriptor>();

            for (int i = 0; i < method.ArgDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.ArgDescriptors[i]), method.ArgDescriptors[i]);

            for (int i = 0; i < method.LocalDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.LocalDescriptors[i]), method.LocalDescriptors[i]);

            for (int i = 0; i < method.TempDescriptors.Length; i++)
                AddDescriptor(new SsaSlot(method.TempDescriptors[i]), method.TempDescriptors[i]);

            trackingCandidates.Sort((a, b) =>
            {
                weightedUses.TryGetValue(a, out int aw);
                weightedUses.TryGetValue(b, out int bw);
                int c = bw.CompareTo(aw);
                return c != 0 ? c : a.CompareTo(b);
            });

            var tracked = new HashSet<SsaSlot>();
            for (int i = 0; i < trackingCandidates.Count; i++)
                tracked.Add(trackingCandidates[i]);

            var trackedList = new List<SsaSlot>(tracked);
            trackedList.Sort();

            bool suppressScalarSsaForEh = cfg.ExceptionRegions.Length != 0 || method.Function.ExceptionHandlers.Length != 0;

            for (int i = 0; i < trackedList.Count; i++)
            {
                var slot = trackedList[i];
                var descriptor = descriptors[slot];

                if (!suppressScalarSsaForEh && CanTrackAsScalar(slot, descriptor))
                {
                    descriptor.MarkRegularPromotedScalar(i);
                }
                else
                {
                    descriptor.MarkTrackedButNotSsa(i, descriptor.Category is GenLocalCategory.Unclassified or GenLocalCategory.UntrackedLocal
                        ? GenLocalCategory.TrackedNonSsaLocal
                        : descriptor.Category);
                }
            }

            foreach (var kv in descriptors)
            {
                if (tracked.Contains(kv.Key))
                    continue;

                var descriptor = kv.Value;
                if (descriptor.AddressExposed)
                    descriptor.MarkAddressExposed();
                else if (descriptor.MemoryAliased)
                    descriptor.MarkMemoryAliased();
                else if (descriptor.IsImplicitByRef || descriptor.Pinned || descriptor.IsRefLike)
                    descriptor.MarkMemoryAliased();
                else if (descriptor.IsCompilerTemp)
                    descriptor.MarkMemoryAliased();
                else if (descriptor.Promoted && descriptor.Category == GenLocalCategory.PromotedStruct)
                    descriptor.MarkPromotedStructParent();
                else
                    descriptor.MarkMemoryAliased();
            }

            allSlots.Clear();
            foreach (var kv in descriptors)
                allSlots.Add(CreateSlotInfo(kv.Key, kv.Value));
            allSlots.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            var ssaCandidates = new List<SsaSlot>();
            for (int i = 0; i < trackedList.Count; i++)
            {
                var slot = trackedList[i];
                if (descriptors[slot].CanBeSsaRenamedAsScalar)
                    ssaCandidates.Add(slot);
            }

            var trackedSlots = trackedList.ToImmutableArray();
            return new GenTreeLocalTrackingResult(
                allSlots.ToImmutableArray(),
                trackedSlots,
                ssaCandidates.ToImmutableArray(),
                new TrackedLocalTable(trackedSlots));

            void AddDescriptor(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                bool exposed = descriptor.AddressExposed || addressExposed.Contains(slot);
                if (exposed)
                    descriptor.MarkAddressExposed();

                if (localStorageByRefAliases.Contains(slot))
                    descriptor.MarkLocalStorageByRefAlias();

                if (descriptor.IsStructField && structPromotionBlockedParents.Contains(descriptor.ParentLclNum))
                    descriptor.MarkMemoryAliased();

                descriptors[slot] = descriptor;

                if (!CanTrackForLiveness(slot, descriptor))
                    return;

                trackingCandidates.Add(slot);
            }

            static SsaSlotInfo CreateSlotInfo(SsaSlot slot, GenLocalDescriptor descriptor)
                => new SsaSlotInfo(
                    slot,
                    descriptor.Type,
                    descriptor.StackKind,
                    descriptor.AddressExposed,
                    descriptor.MemoryAliased,
                    descriptor.Category,
                    descriptor.LclNum,
                    descriptor.VarIndex,
                    descriptor.Tracked,
                    descriptor.SsaPromoted,
                    descriptor);

            static bool CanTrackForLiveness(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                if (descriptor.AddressExposed || descriptor.MemoryAliased)
                    return false;

                if (descriptor.Pinned || descriptor.IsRefLike)
                    return false;

                if (descriptor.Category is GenLocalCategory.AddressExposedLocal or GenLocalCategory.MemoryAliasedLocal or GenLocalCategory.ImplicitByRefPinnedRefLikeLocal)
                    return false;

                if (descriptor.Category == GenLocalCategory.PromotedStruct)
                    return false;

                if (descriptor.StackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                    return false;

                return true;
            }

            static bool CanTrackAsScalar(SsaSlot slot, GenLocalDescriptor descriptor)
            {
                if (!CanTrackForLiveness(slot, descriptor))
                    return false;

                if (descriptor.IsImplicitByRef && descriptor.IsLocalStorageByRefAlias)
                    return false;

                if (descriptor.Category == GenLocalCategory.PromotedStruct)
                    return false;

                if (descriptor.IsStructField && descriptor.Category != GenLocalCategory.PromotedStructField)
                    return false;

                if (!descriptor.IsStructField && descriptor.Category is GenLocalCategory.PromotedStructField or GenLocalCategory.AddressExposedLocal or GenLocalCategory.MemoryAliasedLocal or GenLocalCategory.ImplicitByRefPinnedRefLikeLocal)
                    return false;

                return IsPromotableStorageSlot(descriptor.Type, descriptor.StackKind);
            }
        }

        public static ImmutableArray<SsaSlot> CurrentTrackedSlots(GenTreeMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var result = new List<SsaSlot>();
            AddTracked(method.ArgDescriptors, SsaSlotKind.Arg, result);
            AddTracked(method.LocalDescriptors, SsaSlotKind.Local, result);
            AddTracked(method.TempDescriptors, SsaSlotKind.Temp, result);
            result.Sort();
            return result.ToImmutableArray();
        }

        private static void AddTracked(ImmutableArray<GenLocalDescriptor> descriptors, SsaSlotKind kind, List<SsaSlot> result)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor.IsTrackedForLiveness)
                    result.Add(new SsaSlot(descriptor));
            }
        }

        private static void ResetDescriptors(ImmutableArray<GenLocalDescriptor> descriptors)
        {
            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                descriptor.ResetTrackingAndLivenessState();
            }
        }

        private static void CollectAddressExposed(GenTree node, HashSet<SsaSlot> addressExposed)
        {
            CollectAddressExposed(node, parent: null, operandIndex: -1, addressExposed);
        }

        private static void CollectAddressExposed(GenTree node, GenTree? parent, int operandIndex, HashSet<SsaSlot> addressExposed)
        {
            if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out var slot) &&
                (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
            {
                addressExposed.Add(slot);
                if (node.LocalDescriptor is not null)
                {
                    node.LocalDescriptor.MarkAddressExposed();
                    node.Flags |= GenTreeFlags.AddressExposed;
                }
            }

            for (int i = 0; i < node.Operands.Length; i++)
                CollectAddressExposed(node.Operands[i], node, i, addressExposed);
        }

        private static HashSet<SsaSlot> BuildLocalStorageByRefAliases(GenTreeMethod method)
        {
            var aliases = new HashSet<SsaSlot>();
            bool changed;
            do
            {
                changed = false;
                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var statements = method.Blocks[b].Statements;
                    for (int s = 0; s < statements.Length; s++)
                        VisitStore(statements[s]);
                }
            }
            while (changed);

            return aliases;

            void VisitStore(GenTree node)
            {
                if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot) &&
                    node.LocalDescriptor is { IsImplicitByRef: true } &&
                    node.Operands.Length != 0 &&
                    MayBeLocalStorageByRefValue(node.Operands[0], aliases))
                {
                    if (aliases.Add(storeSlot))
                        changed = true;
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    VisitStore(node.Operands[i]);
            }
        }

        private static bool MayBeLocalStorageByRefValue(GenTree node, HashSet<SsaSlot> localStorageByRefAliases)
        {
            if (node is null)
                return false;

            if (node.Kind is GenTreeKind.ArgAddr or GenTreeKind.LocalAddr or GenTreeKind.TempAddr)
                return true;

            if (node.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp)
                return SsaSlotHelpers.TryGetDirectLoadSlot(node, out var slot) && localStorageByRefAliases.Contains(slot);

            if (node.Kind is GenTreeKind.FieldAddr or GenTreeKind.PointerToByRef or GenTreeKind.PointerElementAddr)
            {
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (MayBeLocalStorageByRefValue(node.Operands[i], localStorageByRefAliases))
                        return true;
                }
            }

            return false;
        }

        private static HashSet<int> BuildStructPromotionBlockedParents(GenTreeMethod method)
        {
            var blocked = new HashSet<int>();

            for (int b = 0; b < method.Blocks.Length; b++)
            {
                var statements = method.Blocks[b].Statements;
                for (int s = 0; s < statements.Length; s++)
                    Visit(statements[s], parent: null, operandIndex: -1);
            }

            return blocked;

            void Visit(GenTree node, GenTree? parent, int operandIndex)
            {
                if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
                {
                    for (int i = 0; i < node.Operands.Length; i++)
                    {
                        if (i == fieldAccess.ReceiverOperandIndex)
                            continue;
                        Visit(node.Operands[i], node, i);
                    }
                    return;
                }

                if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out _) ||
                    SsaSlotHelpers.TryGetDirectStoreSlot(node, out _))
                {
                    if (node.LocalDescriptor is { HasPromotedStructFields: true } descriptor)
                        blocked.Add(descriptor.LclNum);
                }

                if (SsaSlotHelpers.TryGetAddressExposedSlot(node, out _) &&
                    (parent is null || !SsaSlotHelpers.IsContainedLocalFieldAddressUse(parent, operandIndex)))
                {
                    if (node.LocalDescriptor is { HasPromotedStructFields: true } descriptor)
                        blocked.Add(descriptor.LclNum);
                }

                for (int i = 0; i < node.Operands.Length; i++)
                    Visit(node.Operands[i], node, i);
            }
        }

        private static Dictionary<SsaSlot, int> ComputeWeightedSlotUses(GenTreeMethod method, ControlFlowGraph cfg)
        {
            var result = new Dictionary<SsaSlot, int>();
            for (int b = 0; b < method.Blocks.Length; b++)
            {
                int weight = 1 + 8 * LoopDepth(cfg, b);
                var nodes = method.Blocks[b].LinearNodes;
                for (int n = 0; n < nodes.Length; n++)
                    CountSlotUses(nodes[n], result, weight);
            }
            return result;
        }

        private static int LoopDepth(ControlFlowGraph cfg, int blockId)
        {
            int depth = 0;
            for (int i = 0; i < cfg.NaturalLoops.Length; i++)
            {
                if (cfg.NaturalLoops[i].Contains(blockId))
                    depth++;
            }
            return depth;
        }

        private static void CountSlotUses(GenTree node, Dictionary<SsaSlot, int> counts, int weight)
        {
            if (SsaSlotHelpers.TryGetLocalFieldAccess(node, out var fieldAccess))
            {
                if (fieldAccess.Kind == SsaLocalAccessKind.Use)
                {
                    AddUse(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight);
                }
                else if (fieldAccess.Kind == SsaLocalAccessKind.PartialDefinition)
                {
                    AddUse(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight);
                    AddDef(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight, partial: true);
                }
                else if (fieldAccess.Kind == SsaLocalAccessKind.FullDefinition)
                {
                    AddDef(fieldAccess.Slot, DescriptorForFieldAccess(fieldAccess, node), weight, partial: false);
                }

                for (int i = 0; i < node.Operands.Length; i++)
                {
                    if (i == fieldAccess.ReceiverOperandIndex)
                        continue;
                    CountSlotUses(node.Operands[i], counts, weight);
                }
                return;
            }

            if (SsaSlotHelpers.TryGetDirectLoadSlot(node, out var loadSlot))
            {
                AddUse(loadSlot, node.LocalDescriptor, weight);
                return;
            }

            if (SsaSlotHelpers.TryGetDirectStoreSlot(node, out var storeSlot))
            {
                AddDef(storeSlot, node.LocalDescriptor, weight, partial: false);
                return;
            }

            GenLocalDescriptor? DescriptorForFieldAccess(SsaLocalAccess access, GenTree node)
            {
                if (access.Field is not null && access.Receiver?.LocalDescriptor is not null && access.Receiver.LocalDescriptor.TryGetPromotedField(access.Field, out var fieldDescriptor))
                    return fieldDescriptor;
                return node.LocalDescriptor ?? access.Receiver?.LocalDescriptor;
            }

            void AddUse(SsaSlot slot, GenLocalDescriptor? descriptor, int w)
            {
                counts.TryGetValue(slot, out int current);
                counts[slot] = current + Math.Max(1, w);
                descriptor?.AddUse(w);
            }

            void AddDef(SsaSlot slot, GenLocalDescriptor? descriptor, int w, bool partial)
            {
                counts.TryGetValue(slot, out int current);
                counts[slot] = current + Math.Max(1, w);
                if (partial)
                    descriptor?.AddPartialDefinition(w);
                else
                    descriptor?.AddFullDefinition(w);
            }
        }
        private static bool IsPromotableStorageSlot(RuntimeType? type, GenStackKind stackKind)
        {
            if (stackKind is GenStackKind.Void or GenStackKind.Unknown or GenStackKind.Value)
                return false;

            if (stackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                return true;

            if (type is not null)
            {
                if (type.Kind == RuntimeTypeKind.ByRef || type.Kind == RuntimeTypeKind.TypeParam || type.IsReferenceType)
                    return true;

                if (type.IsValueType && type.ContainsGcPointers)
                    return false;
            }

            if (type is null)
                return stackKind is
                    GenStackKind.I4 or
                    GenStackKind.I8 or
                    GenStackKind.R4 or
                    GenStackKind.R8 or
                    GenStackKind.NativeInt or
                    GenStackKind.NativeUInt or
                    GenStackKind.Ptr;

            return MachineAbi.IsPhysicallyPromotableStorage(type, stackKind);
        }

    }
}
