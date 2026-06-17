using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal sealed class RegisterAllocatorOptions
    {
        public static RegisterAllocatorOptions Default => new RegisterAllocatorOptions();

        public ImmutableArray<MachineRegister> AllocatableGeneralRegisters { get; set; } = MachineRegisters.DefaultAllocatableGprs;
        public ImmutableArray<MachineRegister> AllocatableFloatRegisters { get; set; } = MachineRegisters.DefaultAllocatableFprs;
        public bool PreferCopySourceRegister { get; set; } = true;
        public bool RespectCallClobbers { get; set; } = true;
        public bool FinalizeStackLayout { get; set; } = true;
        public bool GeneratePrologEpilog { get; set; } = true;
        public bool BuildGcInfo { get; set; } = true;

        public bool Validate { get; set; } = true;
        public RegisterStackLayoutOptions StackLayoutOptions { get; set; } = RegisterStackLayoutOptions.Default;

        public MachineRegister ParallelCopyScratchRegister0 { get; set; } = MachineRegisters.ParallelCopyScratch0;
        public MachineRegister ParallelCopyScratchRegister1 { get; set; } = MachineRegisters.ParallelCopyScratch1;
        public MachineRegister ParallelCopyFloatScratchRegister0 { get; set; } = MachineRegisters.FloatParallelCopyScratch0;
        public MachineRegister ParallelCopyFloatScratchRegister1 { get; set; } = MachineRegisters.FloatParallelCopyScratch1;

        public ImmutableArray<MachineRegister> GetAllocatableRegisters(RegisterClass registerClass)
        {
            return registerClass switch
            {
                RegisterClass.General => AllocatableGeneralRegisters,
                RegisterClass.Float => AllocatableFloatRegisters,
                _ => ImmutableArray<MachineRegister>.Empty,
            };
        }
    }
    internal enum OperandRole : byte
    {
        Normal,
        HiddenReturnBuffer,
    }
    internal enum MoveKind : byte
    {
        None,
        Register,
        Load,
        Store,
        MemoryToMemory,
        LoadAddress,
        StoreAddress,
    }
    internal enum MoveFlags : ushort
    {
        None = 0,
        Reload = 1 << 0,
        Spill = 1 << 1,
        Split = 1 << 2,
        ParallelCopy = 1 << 3,
        AbiArgument = 1 << 4,
        AbiReturn = 1 << 5,
        HiddenReturnBuffer = 1 << 6,
        Internal = 1 << 7,
    }
    internal enum FrameOperation : byte
    {
        None,
        AllocateFrame,
        SaveReturnAddress,
        SaveCalleeSavedRegister,
        EstablishFramePointer,
        EnterFuncletFrame,
        LeaveFuncletFrame,
        RestoreStackPointerFromFramePointer,
        RestoreCalleeSavedRegister,
        RestoreReturnAddress,
        FreeFrame,
    }
    internal enum RegisterUnwindCodeKind : byte
    {
        None,
        AllocateStack,
        SaveReturnAddress,
        SaveCalleeSavedRegister,
        SetFramePointer,
    }
    internal readonly struct RegisterUnwindCode
    {
        public readonly int NodeId;
        public readonly int BlockId;
        public readonly int Ordinal;
        public readonly RegisterUnwindCodeKind Kind;
        public readonly MachineRegister Register;
        public readonly int StackOffset;
        public readonly int Size;

        public RegisterUnwindCode(
            int nodeId,
            int blockId,
            int ordinal,
            RegisterUnwindCodeKind kind,
            MachineRegister register,
            int stackOffset,
            int size)
        {
            if (nodeId < 0)
                throw new ArgumentOutOfRangeException(nameof(nodeId));
            if (blockId < 0)
                throw new ArgumentOutOfRangeException(nameof(blockId));
            if (ordinal < 0)
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            if (kind == RegisterUnwindCodeKind.None)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (stackOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(stackOffset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            NodeId = nodeId;
            BlockId = blockId;
            Ordinal = ordinal;
            Kind = kind;
            Register = register;
            StackOffset = stackOffset;
            Size = size;
        }

        public override string ToString()
            => Kind + " id=" + NodeId.ToString() + " B" + BlockId.ToString() + ":" + Ordinal.ToString() +
               (Register == MachineRegister.Invalid ? string.Empty : " " + MachineRegisters.Format(Register)) +
               (Size == 0 ? string.Empty : " stack+" + StackOffset.ToString() + ":" + Size.ToString());
    }
    internal enum RegisterGcRootKind : byte
    {
        ObjectReference,
        ByRef,
        InteriorPointer,
    }
    internal readonly struct RegisterGcLiveRoot : IEquatable<RegisterGcLiveRoot>
    {
        public readonly GenTree Value;
        public readonly RegisterGcRootKind RootKind;
        public readonly RegisterOperand Location;
        public readonly int Offset;
        public readonly RuntimeType? Type;
        public readonly bool RequiresValueInfo;

        public RegisterGcLiveRoot(
            GenTree value,
            RegisterGcRootKind rootKind,
            RegisterOperand location,
            RuntimeType? type,
            int offset = 0,
            bool requiresValueInfo = true)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (location.IsNone)
                throw new ArgumentOutOfRangeException(nameof(location));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            Value = value;
            RootKind = rootKind;
            Location = location;
            Offset = offset;
            Type = type;
            RequiresValueInfo = requiresValueInfo;
        }

        public bool Equals(RegisterGcLiveRoot other)
            => ReferenceEquals(Value, other.Value) &&
               RootKind == other.RootKind &&
               Location.Equals(other.Location) &&
               Offset == other.Offset &&
               RequiresValueInfo == other.RequiresValueInfo;

        public override bool Equals(object? obj) => obj is RegisterGcLiveRoot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Value);
                hash = (hash * 397) ^ (int)RootKind;
                hash = (hash * 397) ^ Location.GetHashCode();
                hash = (hash * 397) ^ Offset;
                hash = (hash * 397) ^ RequiresValueInfo.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => RootKind + " " + Value + " @ " + Location +
               (Offset == 0 ? string.Empty : "+" + Offset.ToString()) +
               (RequiresValueInfo ? string.Empty : " home");
    }
    internal enum RegisterGcLiveRangeFlags : ushort
    {
        None = 0,
        Pinned = 1 << 0,
        ReportOnlyInLeafFunclet = 1 << 1,
        SharedWithParentFrame = 1 << 2,
    }
    internal enum RegisterFuncletKind : byte
    {
        Root,
        Catch,
        Finally,
        Fault,
        Filter,
    }
    internal sealed class RegisterFunclet
    {
        public int Index { get; }
        public RegisterFuncletKind Kind { get; }
        public int ExceptionRegionIndex { get; }
        public int ParentFuncletIndex { get; }
        public int EntryBlockId { get; }
        public ImmutableArray<int> BlockIds { get; }

        public RegisterFunclet(
            int index,
            RegisterFuncletKind kind,
            int exceptionRegionIndex,
            int parentFuncletIndex,
            int entryBlockId,
            ImmutableArray<int> blockIds)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (entryBlockId < 0) throw new ArgumentOutOfRangeException(nameof(entryBlockId));

            Index = index;
            Kind = kind;
            ExceptionRegionIndex = exceptionRegionIndex;
            ParentFuncletIndex = parentFuncletIndex;
            EntryBlockId = entryBlockId;
            BlockIds = blockIds.IsDefault ? ImmutableArray<int>.Empty : blockIds;
        }

        public bool IsRoot => Kind == RegisterFuncletKind.Root;

        public static ImmutableArray<RegisterFunclet> Build(GenTreeMethod method)
        {
            if (method is null) throw new ArgumentNullException(nameof(method));

            var cfg = method.Cfg;
            var result = ImmutableArray.CreateBuilder<RegisterFunclet>();
            var rootBlocks = EhFuncletLayout.BuildRootBlockIds(cfg);

            result.Add(new RegisterFunclet(
                index: 0,
                RegisterFuncletKind.Root,
                exceptionRegionIndex: -1,
                parentFuncletIndex: -1,
                entryBlockId: 0,
                blockIds: rootBlocks));

            var regionToFunclet = new Dictionary<int, int>();
            var regionOrder = EhFuncletLayout.ComputeVmRegionOrder(cfg);
            for (int r = 0; r < regionOrder.Length; r++)
            {
                var region = cfg.ExceptionRegions[regionOrder[r]];
                int index = result.Count;
                regionToFunclet[region.Index] = index;
                var blocks = EhFuncletLayout.BuildFuncletBlockIds(cfg, region);

                result.Add(new RegisterFunclet(
                    index,
                    ToFuncletKind(region.Kind),
                    region.Index,
                    parentFuncletIndex: 0,
                    entryBlockId: region.HandlerStartBlockId,
                    blockIds: blocks));
            }

            if (cfg.ExceptionRegions.Length == 0)
                return result.ToImmutable();

            var fixedResult = ImmutableArray.CreateBuilder<RegisterFunclet>(result.Count);
            fixedResult.Add(result[0]);
            for (int i = 1; i < result.Count; i++)
            {
                var funclet = result[i];
                var region = cfg.ExceptionRegions[funclet.ExceptionRegionIndex];
                int parent = 0;
                if (region.EnclosingHandlerIndex >= 0 && regionToFunclet.TryGetValue(region.EnclosingHandlerIndex, out int mappedParent))
                    parent = mappedParent;

                fixedResult.Add(new RegisterFunclet(
                    funclet.Index,
                    funclet.Kind,
                    funclet.ExceptionRegionIndex,
                    parent,
                    funclet.EntryBlockId,
                    funclet.BlockIds));
            }

            return fixedResult.ToImmutable();
        }

        private static RegisterFuncletKind ToFuncletKind(CfgExceptionRegionKind kind)
        {
            return kind switch
            {
                CfgExceptionRegionKind.Catch => RegisterFuncletKind.Catch,
                CfgExceptionRegionKind.Finally => RegisterFuncletKind.Finally,
                CfgExceptionRegionKind.Fault => RegisterFuncletKind.Fault,
                CfgExceptionRegionKind.Filter => RegisterFuncletKind.Filter,
                _ => RegisterFuncletKind.Catch,
            };
        }
    }
    internal enum RegisterFrameRegionKind : byte
    {
        Prolog,
        Epilog,
    }
    internal sealed class RegisterFrameRegion
    {
        public RegisterFrameRegionKind Kind { get; }
        public int FuncletIndex { get; }
        public int BlockId { get; }
        public int FirstNodeId { get; }
        public int LastNodeId { get; }

        public RegisterFrameRegion(RegisterFrameRegionKind kind, int funcletIndex, int blockId, int firstNodeId, int lastNodeId)
        {
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));
            if (blockId < 0) throw new ArgumentOutOfRangeException(nameof(blockId));
            if (firstNodeId < 0) throw new ArgumentOutOfRangeException(nameof(firstNodeId));
            if (lastNodeId < firstNodeId) throw new ArgumentOutOfRangeException(nameof(lastNodeId));

            Kind = kind;
            FuncletIndex = funcletIndex;
            BlockId = blockId;
            FirstNodeId = firstNodeId;
            LastNodeId = lastNodeId;
        }
    }
    internal enum RegisterGcTransitionKind : byte
    {
        Enter,
        Move,
        Exit,
    }
    internal enum RegisterGcInterruptibleRangeKind : byte
    {
        Call,
        Poll,
        FullyInterruptible,
    }
    internal readonly struct RegisterGcLiveRange
    {
        public readonly RegisterGcLiveRoot Root;
        public readonly int StartPosition;
        public readonly int EndPosition;
        public readonly int FuncletIndex;
        public readonly RegisterGcLiveRangeFlags Flags;

        public RegisterGcLiveRange(
            RegisterGcLiveRoot root,
            int startPosition,
            int endPosition,
            int funcletIndex,
            RegisterGcLiveRangeFlags flags)
        {
            if (startPosition < 0) throw new ArgumentOutOfRangeException(nameof(startPosition));
            if (endPosition < startPosition) throw new ArgumentOutOfRangeException(nameof(endPosition));
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));

            Root = root;
            StartPosition = startPosition;
            EndPosition = endPosition;
            FuncletIndex = funcletIndex;
            Flags = flags;
        }

        public bool IsEmpty => StartPosition == EndPosition;
        public override string ToString()
            => Root + " F" + FuncletIndex.ToString() + " [" + StartPosition.ToString() + ", " + EndPosition.ToString() + ")" +
               (Flags == RegisterGcLiveRangeFlags.None ? string.Empty : " " + Flags.ToString());
    }
    internal readonly struct RegisterGcTransition
    {
        public readonly int Position;
        public readonly RegisterGcTransitionKind Kind;
        public readonly RegisterGcLiveRoot? Before;
        public readonly RegisterGcLiveRoot? After;

        public RegisterGcTransition(int position, RegisterGcTransitionKind kind, RegisterGcLiveRoot? before, RegisterGcLiveRoot? after)
        {
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (kind == RegisterGcTransitionKind.Enter && after is null)
                throw new ArgumentNullException(nameof(after));
            if (kind == RegisterGcTransitionKind.Exit && before is null)
                throw new ArgumentNullException(nameof(before));

            Position = position;
            Kind = kind;
            Before = before;
            After = after;
        }
    }
    internal sealed class RegisterGcInterruptibleRange
    {
        public RegisterGcInterruptibleRangeKind Kind { get; }
        public int StartPosition { get; }
        public int EndPosition { get; }
        public int FuncletIndex { get; }
        public int FirstNodeId { get; }
        public int LastNodeId { get; }

        public RegisterGcInterruptibleRange(
            RegisterGcInterruptibleRangeKind kind,
            int startPosition,
            int endPosition,
            int funcletIndex,
            int firstNodeId,
            int lastNodeId)
        {
            if (startPosition < 0) throw new ArgumentOutOfRangeException(nameof(startPosition));
            if (endPosition <= startPosition) throw new ArgumentOutOfRangeException(nameof(endPosition));
            if (funcletIndex < 0) throw new ArgumentOutOfRangeException(nameof(funcletIndex));
            if (firstNodeId < 0) throw new ArgumentOutOfRangeException(nameof(firstNodeId));
            if (lastNodeId < firstNodeId) throw new ArgumentOutOfRangeException(nameof(lastNodeId));

            Kind = kind;
            StartPosition = startPosition;
            EndPosition = endPosition;
            FuncletIndex = funcletIndex;
            FirstNodeId = firstNodeId;
            LastNodeId = lastNodeId;
        }

        public override string ToString()
            => Kind + " F" + FuncletIndex.ToString() + " [" + StartPosition.ToString() + ", " + EndPosition.ToString() + ")";
    }
    internal sealed class RegisterAllocationSegment
    {
        public int Start { get; }
        public int End { get; }
        public RegisterOperand Location { get; }

        public RegisterAllocationSegment(int start, int end, RegisterOperand location)
        {
            if (end < start)
                throw new ArgumentOutOfRangeException(nameof(end));
            if (location.IsNone && end != start)
                throw new ArgumentOutOfRangeException(nameof(location));

            Start = start;
            End = end;
            Location = location;
        }

        public bool IsEmpty => Start == End;
        public bool Contains(int position) => Start <= position && position < End;
        public bool Intersects(int position, int end) => Start < end && position < End;
        public override string ToString() => "[" + Start + ", " + End + ") " + Location;
    }
    internal sealed class RegisterAllocationFragment
    {
        public int SegmentIndex { get; }
        public AbiRegisterSegment AbiSegment { get; }
        public RegisterOperand Home { get; }
        public ImmutableArray<RegisterAllocationSegment> Segments { get; }

        public RegisterAllocationFragment(
            int segmentIndex,
            AbiRegisterSegment abiSegment,
            RegisterOperand home,
            ImmutableArray<RegisterAllocationSegment> segments)
        {
            if (segmentIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            if (home.IsNone && !segments.IsDefaultOrEmpty)
                throw new ArgumentOutOfRangeException(nameof(home));

            SegmentIndex = segmentIndex;
            AbiSegment = abiSegment;
            Home = home;
            Segments = segments.IsDefault ? ImmutableArray<RegisterAllocationSegment>.Empty : NormalizeSegments(segments);
        }

        public RegisterOperand LocationAt(int position)
        {
            if (Home.IsNone || Segments.Length == 0)
                return Home;

            for (int i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                if (segment.Contains(position))
                    return segment.Location;
            }

            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                if (position >= Segments[i].End)
                    return Segments[i].Location;
            }

            return Segments[0].Location;
        }

        private static ImmutableArray<RegisterAllocationSegment> NormalizeSegments(ImmutableArray<RegisterAllocationSegment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationSegment>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsEmpty)
                    list.Add(source[i]);
            }

            list.Sort(static (a, b) =>
            {
                int c = a.Start.CompareTo(b.Start);
                if (c != 0)
                    return c;
                if (a.Location.IsRegister != b.Location.IsRegister)
                    return a.Location.IsRegister ? -1 : 1;
                c = a.End.CompareTo(b.End);
                if (c != 0)
                    return c;
                return a.Location.ToString().CompareTo(b.Location.ToString());
            });
            if (list.Count == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var merged = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(list.Count);
            var current = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var next = list[i];
                if (current.Location.Equals(next.Location) && next.Start <= current.End)
                {
                    current = new RegisterAllocationSegment(current.Start, Math.Max(current.End, next.End), current.Location);
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            return merged.ToImmutable();
        }
    }
    internal readonly struct RegisterValueLocation
    {
        public readonly GenTree Value;
        public readonly AbiValuePassingKind PassingKind;
        public readonly RegisterOperand Scalar;
        public readonly ImmutableArray<RegisterOperand> Fragments;

        public RegisterValueLocation(
            GenTree value,
            AbiValuePassingKind passingKind,
            RegisterOperand scalar,
            ImmutableArray<RegisterOperand> fragments = default)
        {
            Value = value;
            PassingKind = passingKind;
            Scalar = scalar;
            Fragments = fragments.IsDefault ? ImmutableArray<RegisterOperand>.Empty : fragments;
        }

        public bool IsEmpty => Scalar.IsNone && Fragments.Length == 0;
        public bool IsScalar => !Scalar.IsNone && Fragments.Length == 0;
        public bool IsFragmented => Fragments.Length != 0;
        public int Count => Fragments.Length == 0 ? (Scalar.IsNone ? 0 : 1) : Fragments.Length;

        public RegisterOperand this[int index]
        {
            get
            {
                if (Fragments.Length != 0)
                    return Fragments[index];
                if (index == 0 && !Scalar.IsNone)
                    return Scalar;
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public override string ToString()
        {
            if (IsEmpty)
                return Value + " <none>";
            if (Fragments.Length == 0)
                return Value + " " + Scalar;

            var sb = new StringBuilder();
            sb.Append(Value).Append(" {");
            for (int i = 0; i < Fragments.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                sb.Append(i).Append(':').Append(Fragments[i]);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
    internal sealed class RegisterAllocationInfo
    {
        public GenTree Value { get; }
        public GenTreeValueKey ValueKey { get; }
        public RegisterOperand Home { get; }
        public ImmutableArray<LinearLiveRange> Ranges { get; }
        public ImmutableArray<int> UsePositions { get; }
        public int DefinitionPosition { get; }
        public ImmutableArray<RegisterAllocationSegment> Segments { get; }
        public ImmutableArray<RegisterAllocationFragment> Fragments { get; }

        public RegisterAllocationInfo(
            GenTree value,
            RegisterOperand home,
            ImmutableArray<LinearLiveRange> ranges,
            ImmutableArray<int> usePositions,
            int definitionPosition,
            ImmutableArray<RegisterAllocationSegment> segments = default,
            ImmutableArray<RegisterAllocationFragment> fragments = default)
        {
            Value = value;
            ValueKey = value.LinearValueKey;
            Home = home;
            Ranges = ranges.IsDefault ? ImmutableArray<LinearLiveRange>.Empty : ranges;
            UsePositions = usePositions.IsDefault ? ImmutableArray<int>.Empty : usePositions;
            DefinitionPosition = definitionPosition;
            Segments = segments.IsDefaultOrEmpty ? BuildDefaultSegments(home, Ranges) : NormalizeSegments(segments);
            Fragments = fragments.IsDefaultOrEmpty ? ImmutableArray<RegisterAllocationFragment>.Empty : NormalizeFragments(fragments);
        }

        public RegisterOperand LocationAt(int position)
        {
            if (Home.IsNone || Segments.Length == 0)
                return Home;

            for (int i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                if (segment.Contains(position))
                    return segment.Location;
            }

            if (position == DefinitionPosition)
                return Segments[0].Location;

            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                if (position >= Segments[i].End)
                    return Segments[i].Location;
            }

            return Segments[0].Location;
        }

        public RegisterOperand LocationAtDefinition()
            => LocationAt(DefinitionPosition);

        public RegisterOperand FragmentLocationAt(int position, int abiSegmentIndex)
        {
            if (abiSegmentIndex < 0)
                return LocationAt(position);

            for (int i = 0; i < Fragments.Length; i++)
            {
                if (Fragments[i].SegmentIndex == abiSegmentIndex)
                    return Fragments[i].LocationAt(position);
            }

            return LocationAt(position);
        }

        public RegisterValueLocation ValueLocationAt(int position, AbiValueInfo abi)
        {
            var scalar = LocationAt(position);
            if (scalar.IsNone || abi.PassingKind == AbiValuePassingKind.Void)
                return new RegisterValueLocation(Value, abi.PassingKind, RegisterOperand.None);

            if (abi.PassingKind != AbiValuePassingKind.MultiRegister)
                return new RegisterValueLocation(Value, abi.PassingKind, scalar);

            var abiSegments = MachineAbi.GetRegisterSegments(abi);
            var fragments = ImmutableArray.CreateBuilder<RegisterOperand>(abiSegments.Length);
            for (int i = 0; i < abiSegments.Length; i++)
            {
                if (TryGetAllocatedFragment(i, out var fragment))
                    fragments.Add(fragment.LocationAt(position));
                else
                    fragments.Add(OperandAtOffset(scalar, abiSegments[i]));
            }

            return new RegisterValueLocation(Value, abi.PassingKind, RegisterOperand.None, fragments.ToImmutable());
        }

        public RegisterValueLocation ValueLocationAtDefinition(AbiValueInfo abi)
            => ValueLocationAt(DefinitionPosition, abi);

        private bool TryGetAllocatedFragment(int segmentIndex, out RegisterAllocationFragment fragment)
        {
            for (int i = 0; i < Fragments.Length; i++)
            {
                if (Fragments[i].SegmentIndex == segmentIndex)
                {
                    fragment = Fragments[i];
                    return true;
                }
            }

            fragment = null!;
            return false;
        }

        private static RegisterOperand OperandAtOffset(RegisterOperand operand, AbiRegisterSegment segment)
        {
            if (operand.IsRegister)
            {
                if (segment.Offset != 0)
                    throw new InvalidOperationException("Cannot address a non-zero ABI segment inside register allocation " + operand + ".");
                if (!MachineRegisters.IsRegisterInClass(operand.Register, segment.RegisterClass))
                    throw new InvalidOperationException("Register allocation class does not match ABI segment class: " + operand + " vs " + segment + ".");
                return operand;
            }

            int offset = checked(operand.FrameOffset + segment.Offset);
            int size = segment.Size;

            if (operand.IsSpillSlot)
                return RegisterOperand.ForSpillSlot(segment.RegisterClass, operand.SpillSlot, offset, size);
            if (operand.IsIncomingArgumentSlot)
                return RegisterOperand.ForIncomingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsLocalSlot)
                return RegisterOperand.ForLocalSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsTempSlot)
                return RegisterOperand.ForTempSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsOutgoingArgumentSlot)
                return RegisterOperand.ForOutgoingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
            if (operand.IsFrameSlot)
                return RegisterOperand.ForFrameSlot(segment.RegisterClass, operand.FrameSlotKind, operand.FrameBase, operand.FrameSlotIndex, offset, size, operand.IsAddress);

            throw new InvalidOperationException("Cannot address an ABI segment inside allocation operand: " + operand + ".");
        }

        private static ImmutableArray<RegisterAllocationSegment> BuildDefaultSegments(
            RegisterOperand home,
            ImmutableArray<LinearLiveRange> ranges)
        {
            if (home.IsNone || ranges.Length == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var builder = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(ranges.Length);
            for (int i = 0; i < ranges.Length; i++)
                builder.Add(new RegisterAllocationSegment(ranges[i].Start, ranges[i].End, home));
            return builder.ToImmutable();
        }

        private static ImmutableArray<RegisterAllocationSegment> NormalizeSegments(ImmutableArray<RegisterAllocationSegment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationSegment>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsEmpty)
                    list.Add(source[i]);
            }

            list.Sort(static (a, b) =>
            {
                int c = a.Start.CompareTo(b.Start);
                if (c != 0)
                    return c;
                if (a.Location.IsRegister != b.Location.IsRegister)
                    return a.Location.IsRegister ? -1 : 1;
                c = a.End.CompareTo(b.End);
                if (c != 0)
                    return c;
                return a.Location.ToString().CompareTo(b.Location.ToString());
            });
            if (list.Count == 0)
                return ImmutableArray<RegisterAllocationSegment>.Empty;

            var merged = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(list.Count);
            var current = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                var next = list[i];
                if (current.Location.Equals(next.Location) && next.Start <= current.End)
                {
                    int end = next.End > current.End ? next.End : current.End;
                    current = new RegisterAllocationSegment(current.Start, end, current.Location);
                    continue;
                }

                merged.Add(current);
                current = next;
            }
            merged.Add(current);
            return merged.ToImmutable();
        }

        private static ImmutableArray<RegisterAllocationFragment> NormalizeFragments(ImmutableArray<RegisterAllocationFragment> source)
        {
            if (source.Length <= 1)
                return source;

            var list = new List<RegisterAllocationFragment>(source.Length);
            for (int i = 0; i < source.Length; i++)
                list.Add(source[i]);

            list.Sort(static (a, b) => a.SegmentIndex.CompareTo(b.SegmentIndex));

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i - 1].SegmentIndex == list[i].SegmentIndex)
                    throw new InvalidOperationException("Duplicate register allocation fragment " + list[i].SegmentIndex + ".");
            }

            return list.ToImmutableArray();
        }
    }
    internal sealed class RegisterAllocatedMethod
    {
        public GenTreeMethod GenTreeMethod { get; }
        public ImmutableArray<GenTreeBlock> Blocks { get; }
        public ImmutableArray<GenTree> LinearNodes { get; }
        public ImmutableArray<RegisterAllocationInfo> Allocations { get; }
        public IReadOnlyDictionary<GenTree, RegisterAllocationInfo> AllocationByNode { get; }
        public IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>> InternalRegistersByNodeId { get; }
        public IReadOnlyDictionary<int, int> LsraNodePositions { get; }
        public ImmutableArray<int> LsraBlockStartPositions { get; }
        public ImmutableArray<int> LsraBlockEndPositions { get; }
        public int SpillSlotCount { get; }
        public int ParallelCopyScratchSpillSlot { get; }
        public StackFrameLayout StackFrame { get; }
        public bool HasPrologEpilog { get; }
        public ImmutableArray<RegisterUnwindCode> UnwindCodes { get; }
        public ImmutableArray<RegisterGcLiveRange> GcLiveRanges { get; }
        public ImmutableArray<RegisterGcTransition> GcTransitions { get; }
        public ImmutableArray<RegisterGcInterruptibleRange> GcInterruptibleRanges { get; }
        public ImmutableArray<RegisterFunclet> Funclets { get; }
        public ImmutableArray<RegisterFrameRegion> FrameRegions { get; }
        public bool GcReportOnlyLeafFunclet { get; }

        public RegisterAllocatedMethod(
            GenTreeMethod genTreeMethod,
            ImmutableArray<GenTreeBlock> blocks,
            ImmutableArray<GenTree> nodes,
            ImmutableArray<RegisterAllocationInfo> allocations,
            IReadOnlyDictionary<GenTree, RegisterAllocationInfo> allocationByNode,
            IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>>? internalRegistersByNodeId,
            int spillSlotCount,
            int parallelCopyScratchSpillSlot,
            StackFrameLayout? stackFrame = null,
            bool hasPrologEpilog = false,
            ImmutableArray<RegisterUnwindCode> unwindCodes = default,
            ImmutableArray<RegisterGcLiveRange> gcLiveRanges = default,
            ImmutableArray<RegisterGcTransition> gcTransitions = default,
            ImmutableArray<RegisterGcInterruptibleRange> gcInterruptibleRanges = default,
            ImmutableArray<RegisterFunclet> funclets = default,
            ImmutableArray<RegisterFrameRegion> frameRegions = default,
            bool? gcReportOnlyLeafFunclet = null,
            IReadOnlyDictionary<int, int>? lsraNodePositions = null,
            ImmutableArray<int> lsraBlockStartPositions = default,
            ImmutableArray<int> lsraBlockEndPositions = default)
        {
            GenTreeMethod = genTreeMethod ?? throw new ArgumentNullException(nameof(genTreeMethod));
            Blocks = blocks.IsDefault ? ImmutableArray<GenTreeBlock>.Empty : blocks;
            LinearNodes = nodes.IsDefault ? ImmutableArray<GenTree>.Empty : nodes;
            Allocations = allocations.IsDefault ? ImmutableArray<RegisterAllocationInfo>.Empty : allocations;
            AllocationByNode = allocationByNode ?? throw new ArgumentNullException(nameof(allocationByNode));
            InternalRegistersByNodeId = internalRegistersByNodeId ?? new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>();
            LsraNodePositions = lsraNodePositions is null
                ? new Dictionary<int, int>()
                : new Dictionary<int, int>(lsraNodePositions);
            LsraBlockStartPositions = lsraBlockStartPositions.IsDefault ? ImmutableArray<int>.Empty : lsraBlockStartPositions;
            LsraBlockEndPositions = lsraBlockEndPositions.IsDefault ? ImmutableArray<int>.Empty : lsraBlockEndPositions;
            SpillSlotCount = spillSlotCount;
            ParallelCopyScratchSpillSlot = parallelCopyScratchSpillSlot;
            StackFrame = stackFrame ?? StackFrameLayout.Empty;
            HasPrologEpilog = hasPrologEpilog;
            UnwindCodes = unwindCodes.IsDefault ? ImmutableArray<RegisterUnwindCode>.Empty : unwindCodes;
            GcLiveRanges = gcLiveRanges.IsDefault ? ImmutableArray<RegisterGcLiveRange>.Empty : gcLiveRanges;
            GcTransitions = gcTransitions.IsDefault ? ImmutableArray<RegisterGcTransition>.Empty : gcTransitions;
            GcInterruptibleRanges = gcInterruptibleRanges.IsDefault ? ImmutableArray<RegisterGcInterruptibleRange>.Empty : gcInterruptibleRanges;
            Funclets = funclets.IsDefault ? RegisterFunclet.Build(GenTreeMethod) : funclets;
            FrameRegions = frameRegions.IsDefault ? ImmutableArray<RegisterFrameRegion>.Empty : frameRegions;
            GcReportOnlyLeafFunclet = gcReportOnlyLeafFunclet ?? Funclets.Length > 1;
        }

        public RegisterOperand GetHome(GenTree value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (value.RegisterAllocation is null)
                throw new InvalidOperationException("No register allocation attached to GenTree node " + value + ".");
            return value.RegisterHome;
        }

        public RegisterValueLocation GetValueLocation(GenTree value, int position, bool isReturn = false)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            return value.GetRegisterLocation(position, isReturn);
        }

        public RegisterValueLocation GetValueLocationAtDefinition(GenTree value, bool isReturn = false)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (isReturn)
                return value.GetRegisterLocation(value.RegisterAllocation?.DefinitionPosition ?? 0, isReturn: true);
            if (value.RegisterAllocation is null)
                throw new InvalidOperationException("No register allocation attached to GenTree node " + value + ".");
            return value.RegisterLocationAtDefinition;
        }
    }
    internal static class LinearScanRegisterAllocator
    {
        public static GenTreeProgram AllocateProgram(GenTreeProgram program, RegisterAllocatorOptions? options = null)
        {
            if (program is null)
                throw new ArgumentNullException(nameof(program));

            options ??= RegisterAllocatorOptions.Default;

            var methods = ImmutableArray.CreateBuilder<GenTreeMethod>(program.Methods.Length);
            for (int i = 0; i < program.Methods.Length; i++)
                methods.Add(AllocateMethod(program.Methods[i], options));

            return new GenTreeProgram(program.TypeSystem, methods.ToImmutable());
        }

        public static GenTreeMethod AllocateMethod(GenTreeMethod method, RegisterAllocatorOptions? options = null)
        {
            var registerMethod = AllocateRegisterAllocatedMethod(method, options);
            AttachRegisterAllocatedMethodToLir(registerMethod);
            return registerMethod.GenTreeMethod;
        }

        private static RegisterAllocatedMethod AllocateRegisterAllocatedMethod(GenTreeMethod method, RegisterAllocatorOptions? options = null)
        {
            if (method is null)
                throw new ArgumentNullException(nameof(method));

            options ??= RegisterAllocatorOptions.Default;
            ValidateRegisterSet(options.AllocatableGeneralRegisters, RegisterClass.General);
            ValidateRegisterSet(options.AllocatableFloatRegisters, RegisterClass.Float);

            if (method.Phase < GenTreeMethodPhase.LoweredLir)
            {
                throw new InvalidOperationException(
                    $"LSRA requires lowered LIR for method {method.RuntimeMethod}. " +
                    "Run GenTreeLinearLowerer.LowerMethod before register allocation.");
            }
            if (options.Validate)
                LinearVerifier.VerifyBeforeLsra(method);

            var allocator = new MethodAllocator(method, options);
            var result = allocator.Run();

            if (options.FinalizeStackLayout)
                result = RegisterStackLayoutFinalizer.FinalizeMethod(result, options.StackLayoutOptions);

            if (options.GeneratePrologEpilog)
            {
                if (result.StackFrame.IsEmpty && !options.FinalizeStackLayout)
                    throw new InvalidOperationException("Prolog/epilog generation requires finalized stack layout.");
                result = RegisterPrologEpilogGenerator.GenerateMethod(result);
            }

            if (options.BuildGcInfo)
                result = RegisterGcInfoBuilder.AttachMethod(result);

            if (options.Validate)
                RegisterAllocationVerifier.Verify(result);

            return result;
        }

        private static void AttachRegisterAllocatedMethodToLir(RegisterAllocatedMethod registerMethod)
        {
            var method = registerMethod.GenTreeMethod;
            var allocationByValue = new Dictionary<GenTreeValueKey, RegisterAllocationInfo>();
            foreach (var allocation in registerMethod.Allocations)
            {
                allocationByValue[allocation.ValueKey] = allocation;
            }

            method.AttachLsraFinalState(
                registerMethod.Allocations,
                allocationByValue,
                registerMethod.SpillSlotCount,
                registerMethod.ParallelCopyScratchSpillSlot,
                registerMethod.StackFrame,
                registerMethod.HasPrologEpilog,
                registerMethod.UnwindCodes,
                registerMethod.GcLiveRanges,
                registerMethod.GcTransitions,
                registerMethod.GcInterruptibleRanges,
                registerMethod.Funclets,
                registerMethod.FrameRegions,
                registerMethod.GcReportOnlyLeafFunclet);

            AttachLocalDescriptorAllocationState(method.ArgDescriptors, allocationByValue);
            AttachLocalDescriptorAllocationState(method.LocalDescriptors, allocationByValue);
            AttachLocalDescriptorAllocationState(method.TempDescriptors, allocationByValue);

            for (int b = 0; b < registerMethod.Blocks.Length; b++)
            {
                var block = registerMethod.Blocks[b];
                var nodes = block.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    node.AttachLsraInfo(BuildLsraInfo(registerMethod, node));
                    int phiCopyFromBlockId = node.LinearPhiCopyFromBlockId;
                    int phiCopyToBlockId = node.LinearPhiCopyToBlockId;
                    bool isPhiEdgeMove = phiCopyFromBlockId >= 0 || phiCopyToBlockId >= 0;
                    var linearKind = GenTreeLirKinds.IsCopyKind(node.Kind)
                        ? (isPhiEdgeMove ? GenTreeLinearKind.PhiCopy : GenTreeLinearKind.Copy)
                        : node.Kind == GenTreeKind.GcPoll ? GenTreeLinearKind.GcPoll : GenTreeLinearKind.Tree;
                    node.SetLinearState(
                        node.LinearId >= 0 ? node.LinearId : node.Id,
                        block.Id,
                        i,
                        linearKind,
                        node.RegisterResults,
                        node.OperandFlags,
                        node.RegisterUses,
                        node.LinearLowering,
                        node.LinearMemoryAccess,
                        phiCopyFromBlockId,
                        phiCopyToBlockId);
                }
                block.SetLinearNodes(nodes);
            }

            ValidateSsaDescriptorAllocationState(method, allocationByValue);
        }

        private static void ValidateSsaDescriptorAllocationState(
            GenTreeMethod method,
            IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> allocationByValue)
        {
            foreach (var kv in allocationByValue)
            {
                var key = kv.Key;
                if (!key.IsSsaValue)
                    continue;

                var descriptors = GetDescriptorsForLocalKind(method, key.LocalKind);
                if (!TryGetDescriptorForAllocationKey(descriptors, key, out var descriptor))
                    throw new InvalidOperationException("SSA allocation has no matching local descriptor: " + key + ".");

                if (!descriptor.IsRegisterCandidate || !descriptor.SsaPromoted)
                    throw new InvalidOperationException("SSA allocation was produced for a non-tracked or non-register-candidate local descriptor " + descriptor + ": " + key + ".");

                if (!descriptor.TryGetSsaAllocation(key.SsaVersion, out var mapped) || !ReferenceEquals(mapped, kv.Value))
                    throw new InvalidOperationException("SSA allocation was not attached to descriptor " + descriptor + ": " + key + ".");
            }
        }

        private static ImmutableArray<GenLocalDescriptor> GetDescriptorsForLocalKind(GenTreeMethod method, GenLocalKind kind)
            => kind switch
            {
                GenLocalKind.Argument => method.ArgDescriptors,
                GenLocalKind.Local => method.LocalDescriptors,
                GenLocalKind.Temporary => method.TempDescriptors,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

        private static void AttachLocalDescriptorAllocationState(
            ImmutableArray<GenLocalDescriptor> descriptors,
            IReadOnlyDictionary<GenTreeValueKey, RegisterAllocationInfo> allocationByValue)
        {
            for (int i = 0; i < descriptors.Length; i++)
                descriptors[i].ResetRegisterAllocationState();

            foreach (var allocation in allocationByValue.Values)
            {
                if (!TryGetDescriptorForAllocationKey(descriptors, allocation.ValueKey, out var descriptor))
                    continue;

                if (allocation.ValueKey.IsSsaValue)
                    descriptor.SetSsaAllocation(allocation.ValueKey.SsaVersion, allocation);

                AccumulateDescriptorAllocationState(descriptor, allocation);
            }
        }

        private static bool TryGetDescriptorForAllocationKey(
            ImmutableArray<GenLocalDescriptor> descriptors,
            GenTreeValueKey key,
            out GenLocalDescriptor descriptor)
        {
            if (!key.IsLocalDescriptor && !key.IsSsaValue)
            {
                descriptor = null!;
                return false;
            }

            if (key.IsSsaValue && key.SsaSlot.HasLclNum)
            {
                for (int i = 0; i < descriptors.Length; i++)
                {
                    var candidate = descriptors[i];
                    if (candidate.LclNum == key.SsaSlot.LclNum)
                    {
                        descriptor = candidate;
                        return true;
                    }
                }

                descriptor = null!;
                return false;
            }

            for (int i = 0; i < descriptors.Length; i++)
            {
                var candidate = descriptors[i];
                if (candidate.Kind == key.LocalKind && candidate.Index == key.Index)
                {
                    descriptor = candidate;
                    return true;
                }
            }

            descriptor = null!;
            return false;
        }

        private static void AccumulateDescriptorAllocationState(GenLocalDescriptor descriptor, RegisterAllocationInfo allocation)
        {
            if (descriptor.FrameHome.IsNone && !allocation.Home.IsNone)
                descriptor.FrameHome = allocation.Home;

            MachineRegister register = MachineRegister.Invalid;
            if (allocation.Home.IsRegister)
            {
                register = allocation.Home.Register;
            }
            else
            {
                var definition = allocation.LocationAtDefinition();
                if (definition.IsRegister)
                    register = definition.Register;
            }

            if (register != MachineRegister.Invalid && descriptor.RegNum == MachineRegister.Invalid)
                descriptor.RegNum = register;

            descriptor.Register |= register != MachineRegister.Invalid;
            descriptor.Spilled |= allocation.Home.IsMemoryOperand;

            for (int s = 0; s < allocation.Segments.Length && !descriptor.Spilled; s++)
                descriptor.Spilled = allocation.Segments[s].Location.IsMemoryOperand;

            for (int f = 0; f < allocation.Fragments.Length && !descriptor.Spilled; f++)
            {
                var fragment = allocation.Fragments[f];
                for (int s = 0; s < fragment.Segments.Length; s++)
                {
                    if (fragment.Segments[s].Location.IsMemoryOperand)
                    {
                        descriptor.Spilled = true;
                        break;
                    }
                }
            }
        }

        private static ImmutableArray<GenTreeInternalRegister> GetAssignedInternalRegisters(RegisterAllocatedMethod registerMethod, GenTree node)
        {
            int nodeId = node.GenTreeLinearId >= 0 ? node.GenTreeLinearId : node.Id;
            return registerMethod.InternalRegistersByNodeId.TryGetValue(nodeId, out var registers)
                ? registers
                : ImmutableArray<GenTreeInternalRegister>.Empty;
        }

        private static bool IsRegOptionalValueWithoutRegister(GenTreeMethod method, GenTree valueNode)
        {
            var valueKey = valueNode.LinearValueKey;

            if (!method.RegisterAllocationByValue.TryGetValue(valueKey, out var allocation))
                return false;

            for (int i = 0; i < method.RefPositions.Length; i++)
            {
                var rp = method.RefPositions[i];
                if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                    continue;

                var rpValueKey = rp.Value.LinearValueKey;
                if (!rpValueKey.Equals(valueKey))
                    continue;

                if ((rp.Flags & LinearRefPositionFlags.RegOptional) == 0)
                    continue;

                var location = rp.IsAbiSegment
                    ? allocation.FragmentLocationAt(rp.Position, rp.AbiSegmentIndex)
                    : allocation.LocationAt(rp.Position);

                if (!location.IsRegister)
                    return true;
            }

            return false;
        }


        private static GenTreeLsraInfo BuildLsraInfo(RegisterAllocatedMethod registerMethod, GenTree node)
        {
            var method = registerMethod.GenTreeMethod;
            MachineRegister gtReg = MachineRegister.Invalid;
            if (node.Results.Length == 1 && node.Results[0].IsRegister)
                gtReg = node.Results[0].Register;

            var internalRegisters = GetAssignedInternalRegisters(registerMethod, node);
            RegisterValueLocation locationAtDefinition = default;
            if (node.RegisterResults.Length == 1 &&
                method.RegisterAllocationByValue.TryGetValue(node.RegisterResults[0].LinearValueKey, out var resultAllocation))
            {
                var resultInfo = method.GetValueInfo(node.RegisterResults[0]);
                var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                locationAtDefinition = resultAllocation.ValueLocationAtDefinition(resultAbi);
            }

            GenTreeLsraFlags flags = GenTreeLsraFlags.None;
            if (IsRegOptionalValueWithoutRegister(method, node))
                flags |= GenTreeLsraFlags.NoRegAtUse;
            if ((node.MoveFlags & MoveFlags.Spill) != 0)
                flags |= GenTreeLsraFlags.Spill;
            if ((node.MoveFlags & MoveFlags.Reload) != 0)
                flags |= GenTreeLsraFlags.Reload;
            if (internalRegisters.Length != 0)
                flags |= GenTreeLsraFlags.ContainsInternalRegister;

            var resultValues = ImmutableArray.CreateBuilder<GenTreeValueKey>(node.RegisterResults.Length);
            for (int i = 0; i < node.RegisterResults.Length; i++)
            {
                var value = node.RegisterResults[i];
                resultValues.Add(value.LinearValueKey);
            }

            var useValues = ImmutableArray.CreateBuilder<GenTreeValueKey>(node.RegisterUses.Length);
            for (int i = 0; i < node.RegisterUses.Length; i++)
            {
                var value = node.RegisterUses[i];
                useValues.Add(value.LinearValueKey);
            }

            return new GenTreeLsraInfo
            {
                GtRegNum = gtReg,
                Flags = flags,
                Home = node.Results.Length == 1 ? node.Results[0] : RegisterOperand.None,
                LocationAtDefinition = locationAtDefinition,
                CodegenResults = node.Results,
                CodegenUses = node.Uses,
                CodegenUseRoles = node.UseRoles,
                CodegenResultValues = resultValues.ToImmutable(),
                CodegenUseValues = useValues.ToImmutable(),
                InternalRegisters = internalRegisters,
                MoveFlags = node.MoveFlags,
                FrameOperation = node.FrameOperation,
                Immediate = node.Immediate,
                Comment = node.Comment
            };
        }

        private static void ValidateRegisterSet(ImmutableArray<MachineRegister> registers, RegisterClass expectedClass)
        {
            if (registers.IsDefaultOrEmpty)
                throw new InvalidOperationException("Register allocator needs at least one allocatable " + expectedClass + " register.");

            var seen = new HashSet<MachineRegister>();
            for (int i = 0; i < registers.Length; i++)
            {
                var reg = registers[i];
                if (reg == MachineRegister.Invalid)
                    throw new InvalidOperationException("Invalid allocatable register.");
                if (!MachineRegisters.IsRegisterInClass(reg, expectedClass))
                    throw new InvalidOperationException("Register " + MachineRegisters.Format(reg) + " is not a " + expectedClass + " register.");
                if (MachineRegisters.IsReserved(reg))
                    throw new InvalidOperationException("Register " + MachineRegisters.Format(reg) + " is reserved and cannot be allocatable.");
                if (!seen.Add(reg))
                    throw new InvalidOperationException("Duplicate allocatable register " + MachineRegisters.Format(reg) + ".");
            }
        }

        private sealed class MethodAllocator
        {
            private readonly GenTreeMethod _method;
            private readonly RegisterAllocatorOptions _options;
            private readonly Dictionary<GenTree, List<GenTree>> _phiPreferences;
            private readonly Dictionary<GenTree, List<GenTree>> _preferences;
            private readonly Dictionary<AllocationPreferenceKey, List<MachineRegister>> _registerPreferences;
            private readonly Dictionary<int, int> _nodePositions;
            private readonly ImmutableArray<int> _linearBlockOrder;
            private readonly int[] _blockStartPositions;
            private readonly int[] _blockEndPositions;
            private readonly ImmutableArray<int> _callPositions;
            private readonly ImmutableArray<LinearRefPosition> _killRefPositions;
            private readonly Dictionary<int, ImmutableArray<GenTreeInternalRegister>.Builder> _allocatedInternalRegisters = new();
            private readonly Dictionary<GenTree, List<AllocationInterval>> _intervalsByNode = new();
            private readonly Dictionary<GenTree, RegisterOperand> _aggregateHomes = new();
            private readonly Dictionary<GenTree, RegisterAllocationInfo> _allocations = new();
            private readonly Dictionary<GenTree, ImmutableArray<LinearRefPosition>> _refPositionsByValue;
            private readonly Dictionary<(GenTree value, int abiSegmentIndex), ImmutableArray<LinearRefPosition>> _refPositionsByValueSegment;
            private readonly Dictionary<(int nodeId, int position, RegisterClass registerClass), ImmutableArray<LinearRefPosition>> _hardUseRefPositions;
            private readonly Dictionary<int, GenTree> _nodeByLinearId;
            private readonly ImmutableArray<CfgEdge> _exceptionEdges;
            private readonly List<AllocationInterval> _active = new();
            private readonly List<AllocationInterval> _inactive = new();
            private readonly List<AllocationInterval> _handled = new();
            private int _nextSpillSlot;
            private int _nextNodeId;

            public MethodAllocator(GenTreeMethod method, RegisterAllocatorOptions options)
            {
                _method = method;
                _options = options;
                _linearBlockOrder = method.LinearBlockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : method.LinearBlockOrder;
                _phiPreferences = BuildPhiPreferences(method);
                _preferences = BuildPreferences(method);
                _registerPreferences = BuildRegisterPreferences(method);
                _nodePositions = BuildPositionLayout(method, _linearBlockOrder, out _blockStartPositions, out _blockEndPositions);
                _callPositions = BuildCallPositions(method);
                _killRefPositions = BuildKillRefPositions(method, _nodePositions, _options);
                _refPositionsByValue = BuildRefPositionsByValue(method);
                _refPositionsByValueSegment = BuildRefPositionsByValueSegment(_refPositionsByValue);
                _hardUseRefPositions = BuildHardUseRefPositions(method);
                _nodeByLinearId = BuildNodeByLinearId(method);
                _exceptionEdges = BuildExceptionEdges(method.Cfg);
                _nextNodeId = ComputeNextNodeId(method);
            }
            private static Dictionary<GenTree, ImmutableArray<LinearRefPosition>> BuildRefPositionsByValue(GenTreeMethod method)
            {
                var builders = new Dictionary<GenTree, ImmutableArray<LinearRefPosition>.Builder>(ReferenceEqualityComparer<GenTree>.Instance);

                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Value is null)
                        continue;

                    if (!builders.TryGetValue(rp.Value, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<LinearRefPosition>();
                        builders.Add(rp.Value, builder);
                    }

                    builder.Add(rp);
                }

                var result = new Dictionary<GenTree, ImmutableArray<LinearRefPosition>>(builders.Count, ReferenceEqualityComparer<GenTree>.Instance);
                foreach (var item in builders)
                    result.Add(item.Key, item.Value.ToImmutable());

                return result;
            }

            private static Dictionary<(GenTree value, int abiSegmentIndex), ImmutableArray<LinearRefPosition>> BuildRefPositionsByValueSegment(
                Dictionary<GenTree, ImmutableArray<LinearRefPosition>> refPositionsByValue)
            {
                var builders = new Dictionary<(GenTree value, int abiSegmentIndex), ImmutableArray<LinearRefPosition>.Builder>();

                foreach (var item in refPositionsByValue)
                {
                    var refs = item.Value;
                    for (int i = 0; i < refs.Length; i++)
                    {
                        var rp = refs[i];
                        int segment = rp.IsAbiSegment ? rp.AbiSegmentIndex : -1;
                        var key = (item.Key, segment);

                        if (!builders.TryGetValue(key, out var builder))
                        {
                            builder = ImmutableArray.CreateBuilder<LinearRefPosition>();
                            builders.Add(key, builder);
                        }

                        builder.Add(rp);
                    }
                }

                var result = new Dictionary<(GenTree value, int abiSegmentIndex), ImmutableArray<LinearRefPosition>>(builders.Count);
                foreach (var item in builders)
                    result.Add(item.Key, item.Value.ToImmutable());

                return result;
            }

            private static Dictionary<(int nodeId, int position, RegisterClass registerClass), ImmutableArray<LinearRefPosition>> BuildHardUseRefPositions(GenTreeMethod method)
            {
                var builders = new Dictionary<(int nodeId, int position, RegisterClass registerClass), ImmutableArray<LinearRefPosition>.Builder>();

                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                        continue;

                    var key = (rp.NodeId, rp.Position, rp.RegisterClass);
                    if (!builders.TryGetValue(key, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<LinearRefPosition>();
                        builders.Add(key, builder);
                    }

                    builder.Add(rp);
                }

                var result = new Dictionary<(int nodeId, int position, RegisterClass registerClass), ImmutableArray<LinearRefPosition>>(builders.Count);
                foreach (var item in builders)
                    result.Add(item.Key, item.Value.ToImmutable());

                return result;
            }

            private static Dictionary<int, GenTree> BuildNodeByLinearId(GenTreeMethod method)
            {
                var result = new Dictionary<int, GenTree>(method.LinearNodes.Length);

                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    int linearId = node.LinearId >= 0 ? node.LinearId : node.Id;
                    result[linearId] = node;

                    if (node.Id != linearId)
                        result.TryAdd(node.Id, node);
                }

                return result;
            }

            private static ImmutableArray<CfgEdge> BuildExceptionEdges(ControlFlowGraph cfg)
                => EhFuncletLayout.BuildImplicitExceptionEdges(cfg);

            private ImmutableArray<LinearRefPosition> GetRefPositionsForValueSegment(GenTree value, int abiSegmentIndex)
            {
                return _refPositionsByValueSegment.TryGetValue((value, abiSegmentIndex), out var refs)
                    ? refs
                    : ImmutableArray<LinearRefPosition>.Empty;
            }
            public RegisterAllocatedMethod Run()
            {
                AllocateIntervals();
                AttachAllocationsToGenTrees();

                int copyScratchSlot = -1;

                int GetCopyScratchSlot()
                {
                    if (copyScratchSlot < 0)
                        copyScratchSlot = _nextSpillSlot++;
                    return copyScratchSlot;
                }

                var splitPlan = BuildSplitResolutionPlan();
                var blocks = EmitBlocks(GetCopyScratchSlot, splitPlan, out var nodes);

                var allocationList = new List<RegisterAllocationInfo>(_allocations.Values);
                allocationList.Sort(static (a, b) => a.Value.Id.CompareTo(b.Value.Id));

                return new RegisterAllocatedMethod(
                    _method,
                    blocks,
                    nodes,
                    allocationList.ToImmutableArray(),
                    new Dictionary<GenTree, RegisterAllocationInfo>(_allocations),
                    BuildActualInternalRegisterSideTables(),
                    spillSlotCount: _nextSpillSlot,
                    parallelCopyScratchSpillSlot: copyScratchSlot,
                    lsraNodePositions: new Dictionary<int, int>(_nodePositions),
                    lsraBlockStartPositions: ImmutableArray.CreateRange(_blockStartPositions),
                    lsraBlockEndPositions: ImmutableArray.CreateRange(_blockEndPositions));
            }


            private static int ComputeNextNodeId(GenTreeMethod method)
            {
                int max = -1;

                for (int i = 0; i < method.LinearNodes.Length; i++)
                {
                    var node = method.LinearNodes[i];
                    if (node.Id > max)
                        max = node.Id;
                    if (node.LinearId > max)
                        max = node.LinearId;
                }

                for (int b = 0; b < method.Blocks.Length; b++)
                {
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var node = nodes[i];
                        if (node.Id > max)
                            max = node.Id;
                        if (node.LinearId > max)
                            max = node.LinearId;
                    }
                }

                return checked(max + 1);
            }

            private IReadOnlyDictionary<int, ImmutableArray<GenTreeInternalRegister>> BuildActualInternalRegisterSideTables()
            {
                if (_allocatedInternalRegisters.Count == 0)
                    return new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>();

                var result = new Dictionary<int, ImmutableArray<GenTreeInternalRegister>>(_allocatedInternalRegisters.Count);
                foreach (var pair in _allocatedInternalRegisters)
                    result.Add(pair.Key, pair.Value.ToImmutable());
                return result;
            }


            private void AttachAllocationsToGenTrees()
            {
                foreach (var allocation in _allocations.Values)
                {
                    allocation.Value.AttachRegisterAllocation(allocation);
                }
            }

            private enum AllocationStreamItemKind : byte
            {
                IntervalStart = 0,
                InternalRefPosition = 1,
                KillRefPosition = 2,
            }

            private readonly struct AllocationStreamItem
            {
                private AllocationStreamItem(
                    int position,
                    AllocationStreamItemKind kind,
                    AllocationInterval? interval,
                    LinearRefPosition refPosition)
                {
                    Position = position;
                    Kind = kind;
                    Interval = interval;
                    RefPosition = refPosition;
                }

                public int Position { get; }

                public AllocationStreamItemKind Kind { get; }

                public AllocationInterval? Interval { get; }

                public LinearRefPosition RefPosition { get; }

                public static AllocationStreamItem ForInterval(AllocationInterval interval)
                    => ForInterval(interval, interval.Start);

                public static AllocationStreamItem ForInterval(AllocationInterval interval, int position)
                    => new AllocationStreamItem(position, AllocationStreamItemKind.IntervalStart, interval, default);

                public static AllocationStreamItem ForInternalRefPosition(LinearRefPosition refPosition)
                    => new AllocationStreamItem(refPosition.Position, AllocationStreamItemKind.InternalRefPosition, null, refPosition);

                public static AllocationStreamItem ForKillRefPosition(LinearRefPosition refPosition)
                    => new AllocationStreamItem(refPosition.Position, AllocationStreamItemKind.KillRefPosition, null, refPosition);
            }

            private void AllocateIntervals()
            {
                var intervals = BuildAllocationIntervals();
                var allocationStream = BuildAllocationStream(intervals);

                for (int i = 0; i < allocationStream.Count; i++)
                {
                    var item = allocationStream[i];
                    UpdateActiveAndInactive(item.Position);

                    if (item.Kind == AllocationStreamItemKind.InternalRefPosition)
                    {
                        AllocateInternalRefPosition(item.RefPosition);
                        continue;
                    }

                    if (item.Kind == AllocationStreamItemKind.KillRefPosition)
                    {
                        ProcessKillRefPosition(item.RefPosition, allocationStream, ref i);
                        continue;
                    }

                    var current = item.Interval!;
                    int allocationStart = current.FirstLivePositionAtOrAfter(item.Position);
                    if (allocationStart == int.MaxValue || allocationStart >= current.End)
                    {
                        AssignHome(current);
                        continue;
                    }

                    if (current.HasAssignedRegister && current.AssignedRegisterEnd > allocationStart)
                        continue;

                    if (allocationStart >= current.PermanentSpillStart)
                    {
                        AssignHome(current);
                        continue;
                    }

                    if (current.IsEmpty)
                    {
                        AssignHome(current);
                        continue;
                    }

                    if (current.MustStayInMemory)
                    {
                        Spill(current);
                        continue;
                    }

                    if (TryAllocatePreferredRegister(current, allocationStart, allocationStream, ref i))
                        continue;

                    if (TryAllocateFreeRegister(current, allocationStart, allocationStream, ref i))
                        continue;

                    AllocateBlockedRegister(current, allocationStart, allocationStream, ref i);
                }
            }

            private List<AllocationStreamItem> BuildAllocationStream(List<AllocationInterval> intervals)
            {
                var result = new List<AllocationStreamItem>(intervals.Count + _method.RefPositions.Length);

                for (int i = 0; i < intervals.Count; i++)
                    result.Add(AllocationStreamItem.ForInterval(intervals[i]));

                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var refPosition = _method.RefPositions[i];
                    if (refPosition.Kind == LinearRefPositionKind.Internal)
                        result.Add(AllocationStreamItem.ForInternalRefPosition(refPosition));
                }

                for (int i = 0; i < _killRefPositions.Length; i++)
                    result.Add(AllocationStreamItem.ForKillRefPosition(_killRefPositions[i]));

                result.Sort(CompareAllocationStreamItems);
                return result;
            }

            private static int CompareAllocationStreamItems(AllocationStreamItem a, AllocationStreamItem b)
            {
                int c = a.Position.CompareTo(b.Position);
                if (c != 0)
                    return c;

                c = a.Kind.CompareTo(b.Kind);
                if (c != 0)
                    return c;

                if (a.Kind == AllocationStreamItemKind.IntervalStart)
                {
                    var left = a.Interval!;
                    var right = b.Interval!;
                    c = left.Value.Id.CompareTo(right.Value.Id);
                    if (c != 0)
                        return c;
                    return left.AbiSegmentIndex.CompareTo(right.AbiSegmentIndex);
                }

                c = a.RefPosition.NodeId.CompareTo(b.RefPosition.NodeId);
                if (c != 0)
                    return c;
                c = a.RefPosition.OperandIndex.CompareTo(b.RefPosition.OperandIndex);
                if (c != 0)
                    return c;
                return a.RefPosition.FixedRegister.CompareTo(b.RefPosition.FixedRegister);
            }

            private void InsertAllocationStreamItem(
                List<AllocationStreamItem> stream,
                ref int currentIndex,
                AllocationStreamItem item)
            {
                int insertAt = currentIndex + 1;
                while (insertAt < stream.Count && CompareAllocationStreamItems(stream[insertAt], item) <= 0)
                    insertAt++;

                stream.Insert(insertAt, item);
            }

            private void AllocateInternalRefPosition(LinearRefPosition internalRef)
            {
                if (internalRef.Kind != LinearRefPositionKind.Internal)
                    throw new InvalidOperationException("Expected an internal register ref-position.");

                int count = internalRef.MinimumRegisterCount;
                if (count <= 0)
                    throw new InvalidOperationException($"Invalid internal register count for node {internalRef.NodeId}.");

                ulong alreadySelected = 0;
                for (int i = 0; i < count; i++)
                {
                    var selected = TrySelectFreeInternalRegister(internalRef, alreadySelected);
                    if (selected == MachineRegister.Invalid)
                        selected = SelectInternalRegisterBySpilling(internalRef, alreadySelected);

                    if (selected == MachineRegister.Invalid)
                    {
                        throw new InvalidOperationException(
                            $"Unable to allocate internal {internalRef.RegisterClass} register {i + 1}/{count} for node {internalRef.NodeId}.");
                    }

                    alreadySelected |= MachineRegisters.MaskOf(selected);
                    RecordInternalRegister(internalRef, selected);
                }
            }

            private MachineRegister TrySelectFreeInternalRegister(LinearRefPosition internalRef, ulong alreadySelected)
            {
                ulong forbidden = alreadySelected | HardUseRegisterMaskAt(internalRef.NodeId, internalRef.Position, internalRef.RegisterClass);
                var registers = _options.GetAllocatableRegisters(internalRef.RegisterClass);

                for (int i = 0; i < registers.Length; i++)
                {
                    var register = registers[i];
                    ulong bit = MachineRegisters.MaskOf(register);
                    if ((internalRef.RegisterMask & bit) == 0)
                        continue;
                    if ((forbidden & bit) != 0)
                        continue;
                    if (FindRegisterOwnerAt(register, internalRef.Position) is not null)
                        continue;
                    return register;
                }

                return MachineRegister.Invalid;
            }

            private MachineRegister SelectInternalRegisterBySpilling(LinearRefPosition internalRef, ulong alreadySelected)
            {
                ulong forbidden = alreadySelected | HardUseRegisterMaskAt(internalRef.NodeId, internalRef.Position, internalRef.RegisterClass);
                var registers = _options.GetAllocatableRegisters(internalRef.RegisterClass);

                MachineRegister bestRegister = MachineRegister.Invalid;
                AllocationInterval? bestOwner = null;
                int bestNextUse = -1;

                for (int i = 0; i < registers.Length; i++)
                {
                    var register = registers[i];
                    ulong bit = MachineRegisters.MaskOf(register);
                    if ((internalRef.RegisterMask & bit) == 0)
                        continue;
                    if ((forbidden & bit) != 0)
                        continue;

                    var owner = FindRegisterOwnerAt(register, internalRef.Position);
                    if (owner is null)
                        return register;

                    int nextUse = owner.NextUseAfterOrAt(internalRef.Position);
                    if (nextUse > bestNextUse)
                    {
                        bestRegister = register;
                        bestOwner = owner;
                        bestNextUse = nextUse;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || bestOwner is null)
                    return MachineRegister.Invalid;

                SpillRegisterOwnerAt(bestOwner, internalRef.Position);
                return bestRegister;
            }

            private ulong HardUseRegisterMaskAt(int nodeId, int position, RegisterClass registerClass)
            {
                if (!_hardUseRefPositions.TryGetValue((nodeId, position, registerClass), out var uses))
                    return 0;

                ulong mask = 0;
                for (int i = 0; i < uses.Length; i++)
                {
                    var use = uses[i];
                    if ((use.Flags & LinearRefPositionFlags.RegOptional) != 0 &&
                        (use.Flags & LinearRefPositionFlags.RequiresRegister) == 0)
                    {
                        continue;
                    }
                    if (!TryGetAllocationForValue(use.Value!, out var allocation))
                        continue;

                    var location = use.IsAbiSegment
                        ? allocation.FragmentLocationAt(position, use.AbiSegmentIndex)
                        : allocation.LocationAt(position);
                    if (location.IsRegister)
                        mask |= MachineRegisters.MaskOf(location.Register);
                }

                return mask;
            }

            private bool TryGetAllocationForValue(GenTree value, out RegisterAllocationInfo allocation)
            {
                if (_allocations.TryGetValue(value, out var exactAllocation))
                {
                    allocation = exactAllocation;
                    return true;
                }

                var key = value.LinearValueKey;
                foreach (var candidate in _allocations.Values)
                {
                    if (candidate.ValueKey.Equals(key))
                    {
                        allocation = candidate;
                        return true;
                    }
                }

                allocation = null!;
                return false;
            }

            private AllocationInterval? FindRegisterOwnerAt(MachineRegister register, int position)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register && active.Covers(position))
                        return active;
                }

                return null;
            }

            private void SpillRegisterOwnerAt(AllocationInterval owner, int position)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(_active[i], owner))
                        continue;

                    _active.RemoveAt(i);
                    SpillFrom(owner, position);
                    return;
                }

                throw new InvalidOperationException($"Cannot spill register owner for {MachineRegisters.Format(owner.AssignedRegister)} at {position}.");
            }

            private void RecordInternalRegister(LinearRefPosition internalRef, MachineRegister register)
            {
                if (!_allocatedInternalRegisters.TryGetValue(internalRef.NodeId, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<GenTreeInternalRegister>();
                    _allocatedInternalRegisters.Add(internalRef.NodeId, builder);
                }

                builder.Add(new GenTreeInternalRegister(
                    register,
                    internalRef.RegisterClass,
                    internalRef.Position,
                    GenTreeValueKey.ForTree(NodeForLinearId(internalRef.NodeId))));
            }

            private GenTree NodeForLinearId(int nodeId)
            {
                if (_nodeByLinearId.TryGetValue(nodeId, out var node))
                    return node;

                throw new InvalidOperationException($"Internal register ref-position points at missing node {nodeId}.");
            }

            private List<AllocationInterval> BuildAllocationIntervals()
            {
                var result = new List<AllocationInterval>(_method.LiveIntervals.Length);

                for (int i = 0; i < _method.LiveIntervals.Length; i++)
                {
                    var source = _method.LiveIntervals[i];
                    var valueInfo = _method.GetValueInfo(source.Value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        var fragments = new List<AllocationInterval>(segments.Length);

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var segment = segments[s];
                            var segmentRefs = GetAllocationRefPositionsForValueSegment(source.Value, s, out bool segmentHasRefPositions);
                            var segmentUses = GetAllocationUsePositionsFromRefPositions(segmentRefs, source.UsePositions, segmentHasRefPositions);
                            bool stackOnly = RefPositionsRequireStackHome(source.Value, s);
                            bool exceptionHome = RequiresExceptionEdgeStackHome(source, valueInfo);
                            var interval = new AllocationInterval(
                                source.Value,
                                segment.RegisterClass,
                                source.Ranges,
                                segmentUses,
                                segmentRefs,
                                source.DefinitionPosition,
                                CrossesCall(source.Ranges),
                                requiresSingleLocation: RequiresSingleLocation(source),
                                requiresStackHome: stackOnly || exceptionHome,
                                mustStayInMemory: stackOnly || exceptionHome,
                                stackHomeSize: segment.Size,
                                stackHomeAlignment: abi.Alignment <= 0 ? TargetArchitecture.PointerSize : abi.Alignment,
                                abiSegmentIndex: s,
                                abiSegmentOffset: segment.Offset,
                                abiSegmentSize: segment.Size);
                            fragments.Add(interval);
                            result.Add(interval);
                        }

                        _intervalsByNode[source.Value] = fragments;
                        continue;
                    }
                    {
                        var scalarRefs = GetAllocationRefPositionsForValueSegment(source.Value, -1, out bool scalarHasRefPositions);
                        var scalarUses = GetAllocationUsePositionsFromRefPositions(scalarRefs, source.UsePositions, scalarHasRefPositions);
                        bool abiStackHome = MachineAbi.RequiresStackHome(valueInfo.Type, valueInfo.StackKind);
                        bool stackOnly = RefPositionsRequireStackHome(source.Value, -1);
                        bool exceptionHome = RequiresExceptionEdgeStackHome(source, valueInfo);
                        bool requiresStackHome = abiStackHome || stackOnly || exceptionHome;
                        bool mustStayInMemory = abiStackHome || stackOnly || exceptionHome;
                        var interval = new AllocationInterval(
                            source.Value,
                            valueInfo.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : valueInfo.RegisterClass,
                            source.Ranges,
                            scalarUses,
                            scalarRefs,
                            source.DefinitionPosition,
                            CrossesCall(source.Ranges),
                            requiresSingleLocation: RequiresSingleLocation(source),
                            requiresStackHome,
                            mustStayInMemory,
                            stackHomeSize: abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size,
                            stackHomeAlignment: abi.Alignment <= 0 ? TargetArchitecture.PointerSize : abi.Alignment);
                        _intervalsByNode.Add(interval.Value, new List<AllocationInterval> { interval });
                        result.Add(interval);
                    }

                }

                return result;
            }

            private bool RequiresSingleLocation(LinearLiveInterval interval)
            {
                if (interval.Ranges.Length == 0)
                    return false;

                if (interval.Ranges.Length != 1)
                    return true;

                var range = interval.Ranges[0];
                for (int i = 0; i < _blockStartPositions.Length; i++)
                {
                    int blockStart = _blockStartPositions[i];
                    int blockEndExclusive = _blockEndPositions[i] + 1;
                    if (blockStart <= range.Start && range.End <= blockEndExclusive)
                        return false;
                }

                return true;
            }

            private bool IsLiveAcrossExceptionEdge(LinearLiveInterval interval)
            {
                if (interval.Ranges.Length == 0 || _exceptionEdges.Length == 0)
                    return false;

                for (int i = 0; i < _exceptionEdges.Length; i++)
                {
                    var edge = _exceptionEdges[i];
                    int fromPosition = _blockEndPositions[edge.FromBlockId];
                    int toPosition = _blockStartPositions[edge.ToBlockId];
                    if (IsLiveAt(interval, fromPosition) && IsLiveAt(interval, toPosition))
                        return true;
                }

                return false;
            }

            private bool RequiresExceptionEdgeStackHome(LinearLiveInterval interval, GenTreeValueInfo valueInfo)
            {
                if (!IsLiveAcrossExceptionEdge(interval))
                    return false;

                return IsExceptionVisibleLocalValue(valueInfo);
            }

            private static bool IsExceptionVisibleLocalValue(GenTreeValueInfo valueInfo)
            {
                var value = valueInfo.Value;
                if (value.IsLocalDescriptor)
                    return value.LocalKind != GenLocalKind.Temporary;

                return value.IsSsaValue && value.SsaSlot.HasLclNum && value.SsaSlot.Kind != SsaSlotKind.Temp;
            }

            private static bool NeedsPreciseStackHomeAtNonLocalControlFlow(GenTreeValueInfo valueInfo)
            {
                var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);
                if (abi.ContainsGcPointers)
                    return true;

                if (valueInfo.StackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                    return true;

                var type = valueInfo.Type;
                if (type is null)
                    return false;

                return type.IsReferenceType ||
                       type.ContainsGcPointers ||
                       type.Kind is RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam;
            }

            private static bool IsLiveAt(LinearLiveInterval interval, int position)
                => IsLiveAt(interval.Ranges, position);

            private static bool IsLiveAt(ImmutableArray<LinearLiveRange> ranges, int position)
            {
                if (ranges.IsDefaultOrEmpty)
                    return false;

                for (int i = 0; i < ranges.Length; i++)
                {
                    var range = ranges[i];
                    if (range.Start <= position && position < range.End)
                        return true;
                }

                return false;
            }

            private bool RefPositionsRequireStackHome(GenTree value, int abiSegmentIndex)
            {
                var refs = GetRefPositionsForValueSegment(value, abiSegmentIndex);
                for (int i = 0; i < refs.Length; i++)
                {
                    if ((refs[i].Flags & LinearRefPositionFlags.StackOnly) != 0)
                        return true;
                }

                return false;
            }


            private ImmutableArray<LinearRefPosition> GetAllocationRefPositionsForValueSegment(
                GenTree value,
                int abiSegmentIndex,
                out bool hasAnyRefPositions)
            {
                var refs = GetRefPositionsForValueSegment(value, abiSegmentIndex);
                hasAnyRefPositions = refs.Length != 0;
                if (refs.Length == 0)
                    return ImmutableArray<LinearRefPosition>.Empty;

                var result = ImmutableArray.CreateBuilder<LinearRefPosition>(refs.Length);
                for (int i = 0; i < refs.Length; i++)
                {
                    var rp = refs[i];
                    if (!IsAllocationRequiredRefPosition(rp))
                        continue;

                    result.Add(rp);
                }

                return result.ToImmutable();
            }

            private static ImmutableArray<int> GetAllocationUsePositionsFromRefPositions(
                ImmutableArray<LinearRefPosition> refPositions,
                ImmutableArray<int> fallback,
                bool hasAnyRefPositions)
            {
                if (refPositions.Length == 0)
                    return (hasAnyRefPositions || fallback.IsDefault) ? ImmutableArray<int>.Empty : fallback;

                var positions = ImmutableArray.CreateBuilder<int>(refPositions.Length);
                int last = int.MinValue;

                for (int i = 0; i < refPositions.Length; i++)
                {
                    int position = refPositions[i].Position;
                    if (position == last)
                        continue;

                    positions.Add(position);
                    last = position;
                }

                return positions.ToImmutable();
            }

            private static bool IsAllocationRequiredRefPosition(LinearRefPosition refPosition)
            {
                if (refPosition.Kind != LinearRefPositionKind.Use && refPosition.Kind != LinearRefPositionKind.Def)
                    return false;

                return !IsAllocationOptionalRefPosition(refPosition);
            }

            private static bool IsAllocationOptionalRefPosition(LinearRefPosition refPosition)
            {
                if ((refPosition.Flags & LinearRefPositionFlags.RequiresRegister) != 0)
                    return false;
                if ((refPosition.Flags & LinearRefPositionFlags.FixedRegister) != 0)
                    return false;
                return (refPosition.Flags & LinearRefPositionFlags.RegOptional) != 0;
            }

            private RegisterOperand GetOrCreateAggregateHome(GenTree value, AbiValueInfo abi)
            {
                if (_aggregateHomes.TryGetValue(value, out var home))
                    return home;

                int slot = _nextSpillSlot++;
                home = RegisterOperand.ForSpillSlot(
                    RegisterClass.General,
                    slot,
                    offset: 0,
                    size: abi.Size <= 0 ? TargetArchitecture.PointerSize : abi.Size);
                _aggregateHomes[value] = home;
                return home;
            }

            private void UpdateActiveAndInactive(int position)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var interval = _active[i];
                    if (!interval.HasAssignedRegister || interval.AssignedRegisterEnd <= position)
                    {
                        _active.RemoveAt(i);
                        _handled.Add(interval);
                    }
                    else if (!interval.Covers(position))
                    {
                        _active.RemoveAt(i);
                        _inactive.Add(interval);
                    }
                }

                for (int i = _inactive.Count - 1; i >= 0; i--)
                {
                    var interval = _inactive[i];
                    if (!interval.HasAssignedRegister || interval.AssignedRegisterEnd <= position)
                    {
                        _inactive.RemoveAt(i);
                        _handled.Add(interval);
                    }
                    else if (interval.Covers(position))
                    {
                        _inactive.RemoveAt(i);
                        _active.Add(interval);
                    }
                }
            }

            private bool TryAllocatePreferredRegister(
                AllocationInterval current,
                int allocationStart,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (TryAllocatePreferredMachineRegister(current, allocationStart, stream, ref streamIndex))
                    return true;

                if (TryAllocatePreferredValueRegister(current, allocationStart, _phiPreferences, stream, ref streamIndex))
                    return true;

                if (!_options.PreferCopySourceRegister)
                    return false;

                return TryAllocatePreferredValueRegister(current, allocationStart, _preferences, stream, ref streamIndex);
            }

            private bool TryAllocatePreferredValueRegister(
                AllocationInterval current,
                int allocationStart,
                Dictionary<GenTree, List<GenTree>> preferences,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (!preferences.TryGetValue(current.Value, out var preferredValues))
                    return false;

                for (int i = 0; i < preferredValues.Count; i++)
                {
                    var preferred = preferredValues[i];
                    if (!TryGetAllocationForValue(preferred, out var preferredAllocation))
                        continue;

                    var home = preferredAllocation.LocationAt(allocationStart);
                    if (!home.IsRegister)
                        continue;

                    if (home.RegisterClass != current.RegisterClass)
                        continue;

                    if (!TryAssignPreferredRegister(current, allocationStart, home.Register, stream, ref streamIndex))
                        continue;

                    return true;
                }

                return false;
            }

            private bool TryAllocatePreferredMachineRegister(
                AllocationInterval current,
                int allocationStart,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                return TryAllocatePreferredMachineRegister(current, allocationStart, current.AbiSegmentIndex, stream, ref streamIndex);
            }

            private bool TryAllocatePreferredMachineRegister(
                AllocationInterval current,
                int allocationStart,
                int abiSegmentIndex,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (!_registerPreferences.TryGetValue(new AllocationPreferenceKey(current.Value, abiSegmentIndex), out var preferredRegisters))
                    return false;

                for (int i = 0; i < preferredRegisters.Count; i++)
                {
                    if (TryAssignPreferredRegister(current, allocationStart, preferredRegisters[i], stream, ref streamIndex))
                        return true;
                }

                return false;
            }

            private bool TryAssignPreferredRegister(
                AllocationInterval current,
                int allocationStart,
                MachineRegister register,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (current.MustStayInMemory)
                    return false;

                if (register == MachineRegister.Invalid)
                    return false;

                if (!MachineRegisters.IsRegisterInClass(register, current.RegisterClass))
                    return false;

                if (!IsAllocatable(register))
                    return false;

                if (!RegisterAllowedAtRefPosition(current, register, allocationStart))
                    return false;

                int freeUntil = FirstRegisterConflictPosition(register, current, allocationStart);
                int segmentEnd = ComputeRegisterSegmentEnd(current, allocationStart, register, freeUntil);
                if (segmentEnd <= allocationStart)
                    return false;

                AssignRegisterSegment(current, allocationStart, register, segmentEnd, stream, ref streamIndex);
                return true;
            }

            private bool TryAllocateFreeRegister(
                AllocationInterval current,
                int allocationStart,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                MachineRegister bestRegister = MachineRegister.Invalid;
                int bestSegmentEnd = -1;

                var registers = _options.GetAllocatableRegisters(current.RegisterClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    var reg = registers[i];
                    if (!RegisterAllowedAtRefPosition(current, reg, allocationStart))
                        continue;

                    int freeUntil = FirstRegisterConflictPosition(reg, current, allocationStart);
                    int segmentEnd = ComputeRegisterSegmentEnd(current, allocationStart, reg, freeUntil);
                    if (segmentEnd > bestSegmentEnd)
                    {
                        bestSegmentEnd = segmentEnd;
                        bestRegister = reg;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || bestSegmentEnd <= allocationStart)
                    return false;

                AssignRegisterSegment(current, allocationStart, bestRegister, bestSegmentEnd, stream, ref streamIndex);
                return true;
            }

            private void AllocateBlockedRegister(
                AllocationInterval current,
                int allocationStart,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                int currentNextUse = current.NextUseAfterOrAt(allocationStart);

                MachineRegister bestRegister = MachineRegister.Invalid;
                int bestBlockingNextUse = -1;
                int bestSegmentEnd = -1;

                var registers = _options.GetAllocatableRegisters(current.RegisterClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    var reg = registers[i];
                    if (!RegisterAllowedAtRefPosition(current, reg, allocationStart))
                        continue;

                    int blockingNextUse = NextUseOfBlockingIntervals(reg, current, allocationStart);
                    int conflict = FirstRegisterConflictPosition(reg, current, allocationStart);
                    int segmentEnd = ComputeRegisterSegmentEnd(current, allocationStart, reg, conflict);
                    if (segmentEnd <= allocationStart)
                        continue;

                    if (blockingNextUse > bestBlockingNextUse ||
                        (blockingNextUse == bestBlockingNextUse && segmentEnd > bestSegmentEnd))
                    {
                        bestBlockingNextUse = blockingNextUse;
                        bestSegmentEnd = segmentEnd;
                        bestRegister = reg;
                    }
                }

                if (bestRegister == MachineRegister.Invalid || currentNextUse >= bestBlockingNextUse)
                {
                    SpillFrom(current, allocationStart);
                    return;
                }

                SplitBlockingIntervals(bestRegister, current, allocationStart, bestSegmentEnd, stream, ref streamIndex);
                AssignRegisterSegment(current, allocationStart, bestRegister, bestSegmentEnd, stream, ref streamIndex);
            }

            private bool IsAllocatable(MachineRegister register)
            {
                var registerClass = MachineRegisters.GetClass(register);
                if (registerClass == RegisterClass.Invalid)
                    return false;

                var registers = _options.GetAllocatableRegisters(registerClass);
                for (int i = 0; i < registers.Length; i++)
                {
                    if (registers[i] == register)
                        return true;
                }
                return false;
            }

            private bool IsRegisterClassCompatible(MachineRegister register, AllocationInterval interval)
            {
                return MachineRegisters.IsRegisterInClass(register, interval.RegisterClass);
            }

            private int ComputeRegisterSegmentEnd(AllocationInterval interval, int start, MachineRegister register, int freeUntil)
            {
                if (!IsRegisterClassCompatible(register, interval))
                    return start;

                if (!RegisterAllowedAtRefPosition(interval, register, start))
                    return start;

                int end = freeUntil < interval.End ? freeUntil : interval.End;
                if (end <= start)
                    return start;

                int nextKillLimit = FirstKillLimitForRegister(interval, register, start, end);
                if (nextKillLimit < end)
                    end = nextKillLimit;

                int incompatibleRefPosition = FirstIncompatibleRequiredRefPosition(interval, register, start, end);
                if (incompatibleRefPosition < end)
                    end = incompatibleRefPosition;

                return end;
            }

            private void ProcessKillRefPosition(
                LinearRefPosition kill,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (kill.Kind != LinearRefPositionKind.Kill)
                    return;

                ulong killedRegisters = GetKillRegisterMask(kill);
                if (killedRegisters == 0)
                    return;

                ProcessKillRefPositionInList(_active, kill.Position, killedRegisters, stream, ref streamIndex);
                ProcessKillRefPositionInList(_inactive, kill.Position, killedRegisters, stream, ref streamIndex);
            }

            private void ProcessKillRefPositionInList(
                List<AllocationInterval> intervals,
                int killPosition,
                ulong killedRegisters,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                for (int i = intervals.Count - 1; i >= 0; i--)
                {
                    var interval = intervals[i];
                    var register = interval.AssignedRegister;
                    if (register == MachineRegister.Invalid)
                        continue;
                    if ((killedRegisters & MachineRegisters.MaskOf(register)) == 0)
                        continue;
                    if (!interval.CrossesPosition(killPosition))
                        continue;

                    intervals.RemoveAt(i);
                    FreeKilledInterval(interval, register, killPosition, stream, ref streamIndex);
                    _handled.Add(interval);
                }
            }

            private void FreeKilledInterval(
                AllocationInterval interval,
                MachineRegister killedRegister,
                int killPosition,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                int spillStart = KillSpillSegmentStart(interval, killedRegister, killPosition);
                int nextRefPosition = NextRequiredRefPositionAfter(interval, killPosition);
                int spillEnd = nextRefPosition == int.MaxValue ? interval.End : nextRefPosition;
                if (spillEnd < spillStart)
                    spillEnd = spillStart;

                if (interval.AssignedRegisterEnd > spillStart)
                    interval.SplitAssignedRegisterToSpill(spillStart, spillEnd, GetOrCreateSpillHome(interval));
                else
                {
                    interval.AssignedRegister = MachineRegister.Invalid;
                    interval.AssignedRegisterEnd = spillStart;
                    if (spillEnd > spillStart)
                        interval.AddSegment(spillStart, spillEnd, GetOrCreateSpillHome(interval));
                }

                AssignHome(interval);

                if (nextRefPosition != int.MaxValue && nextRefPosition < interval.End)
                    ScheduleContinuationAt(interval, nextRefPosition, stream, ref streamIndex);
            }

            private int FirstIncompatibleRequiredRefPosition(AllocationInterval interval, MachineRegister register, int start, int end)
            {
                int conflict = FirstConflictingFixedRegisterReservation(interval, register, start, end);
                int lastPosition = int.MinValue;

                for (int i = 0; i < interval.RefPositions.Length; i++)
                {
                    var rp = interval.RefPositions[i];
                    if (rp.Position < start)
                        continue;
                    if (rp.Position >= end)
                        break;
                    if (rp.Position == lastPosition)
                        continue;

                    lastPosition = rp.Position;
                    if (!interval.Covers(rp.Position) && !interval.CrossesPosition(rp.Position))
                        continue;

                    if (!RegisterAllowedAtRefPosition(interval, register, rp.Position) && rp.Position < conflict)
                        conflict = rp.Position;
                }

                return conflict;
            }

            private bool RegisterAllowedAtRefPosition(AllocationInterval interval, MachineRegister register, int position)
            {
                if (register == MachineRegister.Invalid)
                    return false;

                if (!MachineRegisters.IsRegisterInClass(register, interval.RegisterClass))
                    return false;

                ulong requiredMask = RequiredRegisterMaskAt(interval, position);
                return requiredMask == 0 || (requiredMask & MachineRegisters.MaskOf(register)) != 0;
            }

            private static ulong RequiredRegisterMaskAt(AllocationInterval interval, int position)
            {
                ulong requiredMask = 0;
                bool hasRequiredMask = false;

                for (int i = 0; i < interval.RefPositions.Length; i++)
                {
                    var rp = interval.RefPositions[i];
                    if (rp.Position < position)
                        continue;
                    if (rp.Position > position)
                        break;

                    ulong mask = RefPositionRegisterMask(rp);
                    if (!hasRequiredMask)
                    {
                        requiredMask = mask;
                        hasRequiredMask = true;
                    }
                    else
                    {
                        requiredMask &= mask;
                    }
                }

                return hasRequiredMask ? requiredMask : 0;
            }

            private static ulong RefPositionRegisterMask(LinearRefPosition refPosition)
            {
                if (refPosition.FixedRegister != MachineRegister.Invalid)
                    return MachineRegisters.MaskOf(refPosition.FixedRegister);

                if (refPosition.RegisterMask != 0)
                    return refPosition.RegisterMask;

                return MachineRegisters.DefaultMaskForClass(refPosition.RegisterClass);
            }

            private int FirstConflictingFixedRegisterReservation(AllocationInterval interval, MachineRegister register, int start, int end)
            {
                if (register == MachineRegister.Invalid)
                    return int.MaxValue;

                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var rp = _method.RefPositions[i];
                    if (rp.Position < start)
                        continue;
                    if (rp.Position >= end)
                        break;
                    if (rp.FixedRegister != register)
                        continue;
                    if (rp.Kind is not (LinearRefPositionKind.Use or LinearRefPositionKind.Def))
                        continue;
                    if (!interval.CrossesPosition(rp.Position) && !interval.Covers(rp.Position))
                        continue;

                    if (RefPositionBelongsToInterval(rp, interval))
                        continue;

                    return rp.Position;
                }

                return int.MaxValue;
            }

            private static bool RefPositionBelongsToInterval(LinearRefPosition rp, AllocationInterval interval)
            {
                if (rp.Value is null || !rp.Value.Equals(interval.Value))
                    return false;

                if (interval.IsAbiFragment)
                    return rp.AbiSegmentIndex == interval.AbiSegmentIndex;

                return !rp.IsAbiSegment;
            }

            private bool CrossesCall(ImmutableArray<LinearLiveRange> ranges)
            {
                if (!_options.RespectCallClobbers || _callPositions.Length == 0 || ranges.Length == 0)
                    return false;

                int c = 0;
                for (int r = 0; r < ranges.Length; r++)
                {
                    var range = ranges[r];
                    while (c < _callPositions.Length && _callPositions[c] + 1 < range.Start)
                        c++;

                    int scan = c;
                    while (scan < _callPositions.Length && _callPositions[scan] < range.End)
                    {
                        int callPos = _callPositions[scan];
                        if (range.Start <= callPos && callPos + 1 < range.End)
                            return true;
                        scan++;
                    }
                }

                return false;
            }

            private static Dictionary<int, int> BuildPositionLayout(
                GenTreeMethod method,
                ImmutableArray<int> blockOrder,
                out int[] blockStartPositions,
                out int[] blockEndPositions)
            {
                var result = new Dictionary<int, int>();
                blockStartPositions = new int[method.Blocks.Length];
                blockEndPositions = new int[method.Blocks.Length];
                int position = 0;

                blockOrder = blockOrder.IsDefaultOrEmpty
                    ? LinearBlockOrder.Compute(method.Cfg)
                    : LinearBlockOrder.Normalize(method.Cfg, blockOrder);

                for (int o = 0; o < blockOrder.Length; o++)
                {
                    int b = blockOrder[o];
                    blockStartPositions[b] = position;
                    var nodes = method.Blocks[b].LinearNodes;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        var node = nodes[n];
                        if (result.ContainsKey(node.LinearId))
                            throw new InvalidOperationException($"Duplicate GenTree LinearId {node.LinearId} in input LIR stream.");
                        result.Add(node.LinearId, position);

                        if (node.IsPhiCopy)
                        {
                            while (n + 1 < nodes.Length && IsSamePhiCopyGroup(node, nodes[n + 1]))
                            {
                                n++;
                                if (result.ContainsKey(nodes[n].LinearId))
                                    throw new InvalidOperationException($"Duplicate GenTree LinearId {nodes[n].LinearId} in input LIR stream.");
                                result.Add(nodes[n].LinearId, position);
                            }
                        }

                        position += 2;
                    }
                    blockEndPositions[b] = position;
                    position += 2;
                }
                return result;
            }

            private static ImmutableArray<int> BuildCallPositions(GenTreeMethod method)
            {
                var positions = new SortedSet<int>();
                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Kind == LinearRefPositionKind.Kill && GetKillRegisterMask(rp) != 0)
                        positions.Add(rp.Position);
                }


                return positions.ToImmutableArray();
            }

            private static ImmutableArray<LinearRefPosition> BuildKillRefPositions(
                GenTreeMethod method, Dictionary<int, int> nodePositions, RegisterAllocatorOptions options)
            {
                if (!options.RespectCallClobbers)
                    return ImmutableArray<LinearRefPosition>.Empty;

                var kills = ImmutableArray.CreateBuilder<LinearRefPosition>();
                var killPositions = new HashSet<int>();
                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Kind != LinearRefPositionKind.Kill || GetKillRegisterMask(rp) == 0)
                        continue;

                    kills.Add(NormalizeKillRefPosition(rp));
                    killPositions.Add(rp.Position);
                }

                for (int n = 0; n < method.LinearNodes.Length; n++)
                {
                    var node = method.LinearNodes[n];
                    if (!node.HasLoweringFlag(GenTreeLinearFlags.CallerSavedKill))
                        continue;
                    if (!nodePositions.TryGetValue(node.LinearId, out int position))
                        throw new InvalidOperationException("Missing GenTree LIR position for caller-saved kill node " + node.LinearId + ".");
                    if (!killPositions.Contains(position))
                        throw new InvalidOperationException("Caller-saved kill node " + node.LinearId + " has no LSRA kill RefPosition at LIR position " + position + ".");
                }

                kills.Sort(static (a, b) =>
                {
                    int c = a.Position.CompareTo(b.Position);
                    if (c != 0)
                        return c;
                    c = a.NodeId.CompareTo(b.NodeId);
                    if (c != 0)
                        return c;
                    return GetKillRegisterMask(a).CompareTo(GetKillRegisterMask(b));
                });
                return CoalesceKillRefPositions(kills);
            }

            private static ulong GetKillRegisterMask(LinearRefPosition kill)
            {
                if (kill.Kind != LinearRefPositionKind.Kill)
                    return 0;

                ulong mask = kill.RegisterMask;
                if (kill.FixedRegister != MachineRegister.Invalid)
                    mask |= MachineRegisters.MaskOf(kill.FixedRegister);
                return mask;
            }

            private static LinearRefPosition NormalizeKillRefPosition(LinearRefPosition kill)
            {
                ulong mask = GetKillRegisterMask(kill);
                if (mask == 0)
                    throw new InvalidOperationException("Kill ref position has an empty register mask.");

                if (kill.FixedRegister == MachineRegister.Invalid &&
                    kill.RegisterClass == RegisterClass.Invalid &&
                    kill.Value is null &&
                    kill.RegisterMask == mask)
                {
                    return kill;
                }

                return new LinearRefPosition(
                    kill.NodeId,
                    kill.Position,
                    kill.OperandIndex,
                    LinearRefPositionKind.Kill,
                    null,
                    RegisterClass.Invalid,
                    MachineRegister.Invalid,
                    (kill.Flags | LinearRefPositionFlags.Internal) & ~LinearRefPositionFlags.FixedRegister,
                    mask);
            }

            private static ImmutableArray<LinearRefPosition> CoalesceKillRefPositions(
                ImmutableArray<LinearRefPosition>.Builder kills)
            {
                if (kills.Count == 0)
                    return ImmutableArray<LinearRefPosition>.Empty;

                var result = ImmutableArray.CreateBuilder<LinearRefPosition>();
                LinearRefPosition current = kills[0];
                ulong currentMask = GetKillRegisterMask(current);

                for (int i = 1; i < kills.Count; i++)
                {
                    var kill = kills[i];
                    if (kill.Position == current.Position && kill.NodeId == current.NodeId)
                    {
                        currentMask |= GetKillRegisterMask(kill);
                        continue;
                    }

                    result.Add(new LinearRefPosition(
                        current.NodeId,
                        current.Position,
                        -1,
                        LinearRefPositionKind.Kill,
                        null,
                        RegisterClass.Invalid,
                        MachineRegister.Invalid,
                        LinearRefPositionFlags.Internal,
                        currentMask));

                    current = kill;
                    currentMask = GetKillRegisterMask(kill);
                }

                result.Add(new LinearRefPosition(
                    current.NodeId,
                    current.Position,
                    -1,
                    LinearRefPositionKind.Kill,
                    null,
                    RegisterClass.Invalid,
                    MachineRegister.Invalid,
                    LinearRefPositionFlags.Internal,
                    currentMask));

                return result.ToImmutable();
            }

            private int FirstKillLimitForRegister(
                AllocationInterval interval,
                MachineRegister register,
                int start,
                int end)
            {
                if (!_options.RespectCallClobbers || _killRefPositions.Length == 0)
                    return int.MaxValue;

                ulong registerMask = MachineRegisters.MaskOf(register);
                for (int i = 0; i < _killRefPositions.Length; i++)
                {
                    var kill = _killRefPositions[i];
                    if (kill.Position < start)
                        continue;
                    if (kill.Position >= end)
                        break;
                    if ((GetKillRegisterMask(kill) & registerMask) == 0)
                        continue;
                    if (!interval.CrossesPosition(kill.Position))
                        continue;

                    return UseAtPositionNeedsPreKillLocation(interval, kill.Position)
                        ? kill.Position + 1
                        : kill.Position;
                }

                return int.MaxValue;
            }

            private int KillSpillSegmentStart(AllocationInterval interval, MachineRegister register, int killPosition)
                => UseAtPositionNeedsPreKillLocation(interval, killPosition)
                    ? killPosition + 1
                    : killPosition;

            private bool TryGetKillPositionEndingRegisterSegment(
                AllocationInterval interval,
                MachineRegister register,
                int segmentEnd,
                out int killPosition)
            {
                killPosition = -1;
                if (!_options.RespectCallClobbers || _killRefPositions.Length == 0)
                    return false;

                ulong registerMask = MachineRegisters.MaskOf(register);
                for (int i = 0; i < _killRefPositions.Length; i++)
                {
                    var kill = _killRefPositions[i];
                    if (kill.Position > segmentEnd)
                        break;
                    if ((GetKillRegisterMask(kill) & registerMask) == 0)
                        continue;
                    if (!interval.CrossesPosition(kill.Position))
                        continue;

                    int expectedEnd = UseAtPositionNeedsPreKillLocation(interval, kill.Position)
                        ? kill.Position + 1
                        : kill.Position;
                    if (expectedEnd != segmentEnd)
                        continue;

                    killPosition = kill.Position;
                    return true;
                }

                return false;
            }

            private bool IsCallArgumentUse(LinearRefPosition refPosition)
            {
                if (refPosition.Kind != LinearRefPositionKind.Use)
                    return false;

                if (!_nodeByLinearId.TryGetValue(refPosition.NodeId, out var node))
                    return false;

                return IsAbiCall(node);
            }

            private bool UseAtPositionNeedsPreKillLocation(AllocationInterval interval, int position)
            {
                for (int i = 0; i < _method.RefPositions.Length; i++)
                {
                    var rp = _method.RefPositions[i];
                    if (rp.Position < position)
                        continue;
                    if (rp.Position > position)
                        break;
                    if (rp.Kind != LinearRefPositionKind.Use || rp.Value is null)
                        continue;
                    if (!rp.Value.Equals(interval.Value))
                        continue;
                    if (interval.IsAbiFragment)
                    {
                        if (rp.AbiSegmentIndex != interval.AbiSegmentIndex)
                            continue;
                    }
                    else if (rp.IsAbiSegment)
                    {
                        continue;
                    }

                    if ((rp.Flags & LinearRefPositionFlags.StackOnly) != 0)
                        continue;

                    return true;
                }

                return false;
            }

            private bool IsRegisterKilledAtPosition(MachineRegister register, int position)
            {
                if (!_options.RespectCallClobbers || _killRefPositions.Length == 0)
                    return false;

                ulong registerMask = MachineRegisters.MaskOf(register);
                for (int i = 0; i < _killRefPositions.Length; i++)
                {
                    var kill = _killRefPositions[i];
                    if (kill.Position < position)
                        continue;
                    if (kill.Position > position)
                        break;
                    if ((GetKillRegisterMask(kill) & registerMask) != 0)
                        return true;
                }

                return false;
            }

            private static int NextRequiredRefPositionAfter(AllocationInterval interval, int position)
                => interval.NextRequiredRefPositionAfter(position);

            private static int NextRequiredRefPositionAtOrAfter(AllocationInterval interval, int position)
                => interval.NextRequiredRefPositionAtOrAfter(position);

            private static bool IsAbiCall(GenTree node)
                => node.HasLoweringFlag(GenTreeLinearFlags.AbiCall);

            private int FirstRegisterConflictPosition(MachineRegister register, AllocationInterval current, int start)
            {
                int conflict = int.MaxValue;

                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register)
                    {
                        int p = active.FirstRegisterIntersection(current, start, int.MaxValue);
                        if (p < conflict)
                            conflict = p;
                    }
                }

                for (int i = 0; i < _inactive.Count; i++)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register)
                    {
                        int p = inactive.FirstRegisterIntersection(current, start, int.MaxValue);
                        if (p < conflict)
                            conflict = p;
                    }
                }

                return conflict;
            }

            private int NextUseOfBlockingIntervals(MachineRegister register, AllocationInterval current, int start)
            {
                int nextUse = int.MaxValue;

                for (int i = 0; i < _active.Count; i++)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register && active.FirstRegisterIntersection(current, start, int.MaxValue) != int.MaxValue)
                    {
                        int use = active.NextUseAfterOrAt(start);
                        if (use < nextUse)
                            nextUse = use;
                    }
                }

                for (int i = 0; i < _inactive.Count; i++)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register && inactive.FirstRegisterIntersection(current, start, int.MaxValue) != int.MaxValue)
                    {
                        int use = inactive.NextUseAfterOrAt(start);
                        if (use < nextUse)
                            nextUse = use;
                    }
                }

                return nextUse;
            }

            private void SplitBlockingIntervals(
                MachineRegister register,
                AllocationInterval current,
                int currentStart,
                int currentRegisterEnd,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var active = _active[i];
                    if (active.AssignedRegister == register)
                    {
                        int split = active.FirstRegisterIntersection(current, currentStart, currentRegisterEnd);
                        if (split != int.MaxValue)
                        {
                            _active.RemoveAt(i);
                            SplitToSpill(active, split, stream, ref streamIndex);
                            _handled.Add(active);
                        }
                    }
                }

                for (int i = _inactive.Count - 1; i >= 0; i--)
                {
                    var inactive = _inactive[i];
                    if (inactive.AssignedRegister == register)
                    {
                        int split = inactive.FirstRegisterIntersection(current, currentStart, currentRegisterEnd);
                        if (split != int.MaxValue)
                        {
                            _inactive.RemoveAt(i);
                            SplitToSpill(inactive, split, stream, ref streamIndex);
                            _handled.Add(inactive);
                        }
                    }
                }
            }

            private void AssignRegisterSegment(
                AllocationInterval interval,
                int start,
                MachineRegister register,
                int segmentEnd,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (!MachineRegisters.IsRegisterInClass(register, interval.RegisterClass))
                    throw new InvalidOperationException(
                        $"Cannot assign {MachineRegisters.Format(register)} to {interval.RegisterClass} interval {interval.Value}.");
                if (segmentEnd <= start)
                    throw new InvalidOperationException($"Register segment for {interval.Value} is empty.");

                int killPosition = -1;
                bool endedByKill =
                    segmentEnd < interval.End &&
                    TryGetKillPositionEndingRegisterSegment(interval, register, segmentEnd, out killPosition);

                var registerHome = RegisterOperand.ForRegister(register);
                interval.AssignedRegister = register;
                interval.AssignedRegisterEnd = segmentEnd;
                interval.AddSegment(start, segmentEnd, registerHome);

                if (endedByKill)
                {
                    int spillStart = KillSpillSegmentStart(interval, register, killPosition);
                    int nextRefPosition = NextRequiredRefPositionAfter(interval, killPosition);
                    int spillEnd = nextRefPosition == int.MaxValue ? interval.End : nextRefPosition;
                    if (spillEnd < spillStart)
                        spillEnd = spillStart;

                    interval.AddSegment(spillStart, spillEnd, GetOrCreateSpillHome(interval));
                    AssignHome(interval);

                    if (nextRefPosition != int.MaxValue && nextRefPosition < interval.End)
                        ScheduleContinuationAt(interval, nextRefPosition, stream, ref streamIndex);
                }
                else
                {
                    if (segmentEnd < interval.End)
                        AddFutureSplitSpill(interval, segmentEnd, stream, ref streamIndex);
                    else
                        AssignHome(interval);
                }

                if (interval.Covers(start))
                    _active.Add(interval);
                else
                    _inactive.Add(interval);
            }

            private void AddFutureSplitSpill(
                AllocationInterval interval,
                int splitPosition,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                int nextRefPosition = NextRequiredRefPositionAtOrAfter(interval, splitPosition);
                int spillEnd = nextRefPosition == int.MaxValue ? interval.End : nextRefPosition;
                if (spillEnd < splitPosition)
                    spillEnd = splitPosition;

                if (spillEnd == splitPosition)
                    interval.EndAssignedRegisterAt(splitPosition);
                else
                    interval.AddSpillSegmentAfterAssignedRegister(splitPosition, spillEnd, GetOrCreateSpillHome(interval));
                AssignHome(interval);

                if (nextRefPosition != int.MaxValue && nextRefPosition < interval.End)
                    ScheduleContinuationAt(interval, nextRefPosition, stream, ref streamIndex);
            }

            private void ScheduleContinuation(
                AllocationInterval interval,
                int position,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                int next = interval.FirstLivePositionAtOrAfter(position);
                ScheduleContinuationAt(interval, next, stream, ref streamIndex);
            }

            private void ScheduleContinuationAt(
                AllocationInterval interval,
                int next,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                if (next == int.MaxValue || next >= interval.End)
                    return;

                for (int i = streamIndex + 1; i < stream.Count; i++)
                {
                    var item = stream[i];
                    if (item.Position > next)
                        break;
                    if (item.Position == next &&
                        item.Kind == AllocationStreamItemKind.IntervalStart &&
                        ReferenceEquals(item.Interval, interval))
                    {
                        return;
                    }
                }

                InsertAllocationStreamItem(stream, ref streamIndex, AllocationStreamItem.ForInterval(interval, next));
            }

            private void SplitToSpill(
                AllocationInterval interval,
                int splitPosition,
                List<AllocationStreamItem> stream,
                ref int streamIndex)
            {
                int nextRefPosition = NextRequiredRefPositionAtOrAfter(interval, splitPosition);
                int spillEnd = nextRefPosition == int.MaxValue ? interval.End : nextRefPosition;
                if (spillEnd < splitPosition)
                    spillEnd = splitPosition;

                if (spillEnd == splitPosition)
                    interval.EndAssignedRegisterAt(splitPosition);
                else
                    interval.SplitAssignedRegisterToSpill(splitPosition, spillEnd, GetOrCreateSpillHome(interval));
                AssignHome(interval);

                if (nextRefPosition != int.MaxValue && nextRefPosition < interval.End)
                    ScheduleContinuationAt(interval, nextRefPosition, stream, ref streamIndex);
            }

            private void Spill(AllocationInterval interval)
            {
                interval.MarkPermanentlySpilledFrom(interval.Start);
                interval.ReplaceWithSingleSegment(interval.Start, interval.End, GetOrCreateSpillHome(interval));
                AssignHome(interval);
                _handled.Add(interval);
            }

            private void SpillFrom(AllocationInterval interval, int start)
            {
                interval.MarkPermanentlySpilledFrom(start);
                interval.SplitAssignedRegisterToSpill(start, start, GetOrCreateSpillHome(interval));
                AssignHome(interval);
                _handled.Add(interval);
            }

            private RegisterOperand GetOrCreateSpillHome(AllocationInterval interval)
            {
                if (interval.SpillSlot < 0)
                    interval.SpillSlot = _nextSpillSlot++;
                if (interval.IsAbiFragment)
                    return RegisterOperand.ForSpillSlot(interval.RegisterClass, interval.SpillSlot, 0, interval.AbiSegmentSize);
                if (interval.RequiresStackHome && interval.StackHomeSize > 0)
                    return RegisterOperand.ForSpillSlot(interval.RegisterClass == RegisterClass.Invalid ? RegisterClass.General : interval.RegisterClass,
                        interval.SpillSlot, 0, interval.StackHomeSize);
                return RegisterOperand.ForSpillSlot(interval.RegisterClass, interval.SpillSlot);
            }

            private void AssignHome(AllocationInterval interval)
            {
                if (interval.IsAbiFragment)
                {
                    _allocations[interval.Value] = BuildFragmentedAllocation(interval.Value);
                    return;
                }

                RegisterOperand home = interval.PrimaryHome;
                if (interval.MustStayInMemory || interval.HasMemoryLocationSegment)
                    home = GetOrCreateSpillHome(interval);

                _allocations[interval.Value] = new RegisterAllocationInfo(
                    interval.Value,
                    home,
                    interval.Ranges,
                    interval.UsePositions,
                    interval.DefinitionPosition,
                    interval.ToRegisterAllocationSegments());
            }

            private RegisterAllocationInfo BuildFragmentedAllocation(GenTree value)
            {
                if (!_intervalsByNode.TryGetValue(value, out var intervals) || intervals.Count == 0)
                    throw new InvalidOperationException($"Missing ABI fragment intervals for {value}.");

                var valueInfo = _method.GetValueInfo(value);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: false);
                var abiSegments = MachineAbi.GetRegisterSegments(abi);
                var fragments = ImmutableArray.CreateBuilder<RegisterAllocationFragment>(intervals.Count);

                for (int i = 0; i < intervals.Count; i++)
                {
                    var interval = intervals[i];
                    if (!interval.IsAbiFragment)
                        continue;
                    if ((uint)interval.AbiSegmentIndex >= (uint)abiSegments.Length)
                        throw new InvalidOperationException($"Invalid ABI fragment index {interval.AbiSegmentIndex} for {value}.");

                    fragments.Add(new RegisterAllocationFragment(
                        interval.AbiSegmentIndex,
                        abiSegments[interval.AbiSegmentIndex],
                        interval.PrimaryHome,
                        interval.ToRegisterAllocationSegments()));
                }

                RegisterOperand aggregateHome = RegisterOperand.None;
                for (int i = 0; i < intervals.Count; i++)
                {
                    var interval = intervals[i];
                    if (interval.IsAbiFragment && (interval.MustStayInMemory || interval.HasMemoryLocationSegment))
                    {
                        aggregateHome = GetOrCreateAggregateHome(value, abi);
                        break;
                    }
                }

                return new RegisterAllocationInfo(
                    value,
                    aggregateHome,
                    intervals[0].Ranges,
                    intervals[0].UsePositions,
                    intervals[0].DefinitionPosition,
                    segments: ImmutableArray<RegisterAllocationSegment>.Empty,
                    fragments: fragments.ToImmutable());
            }

            private ImmutableArray<GenTreeBlock> EmitBlocks(
                Func<int> getCopyScratchSlot,
                SplitResolutionPlan splitPlan,
                out ImmutableArray<GenTree> allNodes)
            {
                var blockArray = new GenTreeBlock[_method.Blocks.Length];
                var all = ImmutableArray.CreateBuilder<GenTree>();

                for (int orderIndex = 0; orderIndex < _linearBlockOrder.Length; orderIndex++)
                {
                    int b = _linearBlockOrder[orderIndex];
                    var linearBlock = _method.Blocks[b];
                    var nodes = ImmutableArray.CreateBuilder<GenTree>(linearBlock.LinearNodes.Length);
                    var emittedSplitPositions = new HashSet<int>();
                    var pendingPositionSplitMoves = GetPositionSplitMovePositionsForBlock(
                        splitPlan.PositionMoves,
                        _blockStartPositions[b],
                        _blockEndPositions[b]);
                    int pendingPositionSplitMoveIndex = 0;
                    bool emittedExitMoves = false;

                    void EmitPositionSplitMoves(int position)
                    {
                        if (emittedSplitPositions.Add(position))
                            EmitSplitMovesAtPosition(b, position, getCopyScratchSlot, splitPlan.PositionMoves, nodes, all);
                    }

                    void EmitPendingPositionSplitMovesThrough(int position)
                    {
                        while (pendingPositionSplitMoveIndex < pendingPositionSplitMoves.Length &&
                               pendingPositionSplitMoves[pendingPositionSplitMoveIndex] <= position)
                        {
                            EmitPositionSplitMoves(pendingPositionSplitMoves[pendingPositionSplitMoveIndex]);
                            pendingPositionSplitMoveIndex++;
                        }
                    }

                    void EmitExitSplitMoves()
                    {
                        if (emittedExitMoves)
                            return;

                        emittedExitMoves = true;
                        EmitBlockExitSplitMoves(b, getCopyScratchSlot, splitPlan, nodes, all);
                    }

                    EmitBlockEntrySplitMoves(b, getCopyScratchSlot, splitPlan, nodes, all);
                    EmitPendingPositionSplitMovesThrough(_blockStartPositions[b]);

                    for (int n = 0; n < linearBlock.LinearNodes.Length; n++)
                    {
                        var node = linearBlock.LinearNodes[n];
                        int position = GetNodePosition(node);
                        bool isTerminator = IsBlockTerminatorNode(node);

                        if (IsAbiCall(node))
                        {
                            EmitPendingPositionSplitMovesThrough(position);

                            EmitCallLikeTreeNode(
                                node,
                                getCopyScratchSlot,
                                nodes,
                                all);

                            if (!isTerminator)
                                EmitPendingPositionSplitMovesThrough(position + 1);

                            continue;
                        }

                        EmitPendingPositionSplitMovesThrough(position);

                        if (isTerminator)
                        {
                            EmitPendingPositionSplitMovesThrough(_blockEndPositions[b] - 1);
                            EmitExitSplitMoves();
                        }

                        if (node.IsPhiCopy)
                        {
                            bool isPredecessorExitPhiGroup = IsPredecessorExitPhiGroup(node, b);
                            if (isPredecessorExitPhiGroup)
                            {
                                EmitPendingPositionSplitMovesThrough(_blockEndPositions[b] - 1);
                            }

                            n = EmitPhiCopyGroup(
                                linearBlock.LinearNodes,
                                n,
                                b,
                                splitPlan,
                                EmitPositionSplitMoves,
                                emitPostPhiPositionMoves: !isPredecessorExitPhiGroup,
                                getCopyScratchSlot,
                                nodes,
                                all);
                            continue;
                        }
                        else if (node.LinearKind == GenTreeLinearKind.Copy)
                        {
                            EmitCopyNode(node, getCopyScratchSlot, nodes, all);
                        }
                        else if (node.LinearKind == GenTreeLinearKind.GcPoll)
                        {
                            EmitGcPollNode(node, nodes, all);
                        }
                        else
                        {
                            EmitTreeNodeSequence(node, nodes, all);
                        }

                        if (!isTerminator)
                            EmitPendingPositionSplitMovesThrough(position + 1);
                    }

                    EmitPendingPositionSplitMovesThrough(_blockEndPositions[b] - 1);
                    EmitExitSplitMoves();

                    linearBlock.SetLinearNodes(nodes.ToImmutable());
                    blockArray[b] = linearBlock;
                }

                for (int b = 0; b < blockArray.Length; b++)
                {
                    if (blockArray[b] is null)
                    {
                        var emptyBlock = _method.Blocks[b];
                        emptyBlock.SetLinearNodes(ImmutableArray<GenTree>.Empty);
                        blockArray[b] = emptyBlock;
                    }
                }

                allNodes = all.ToImmutable();
                return ImmutableArray.Create(blockArray);
            }

            private static ImmutableArray<int> GetPositionSplitMovePositionsForBlock(
                Dictionary<int, List<RegisterResolvedMove>> positionMoves,
                int blockStart,
                int blockEnd)
            {
                if (positionMoves.Count == 0 || blockEnd <= blockStart)
                    return ImmutableArray<int>.Empty;

                var positions = new List<int>();
                foreach (var kv in positionMoves)
                {
                    if (kv.Value.Count == 0)
                        continue;

                    int position = kv.Key;
                    if (blockStart <= position && position < blockEnd)
                        positions.Add(position);
                }

                if (positions.Count == 0)
                    return ImmutableArray<int>.Empty;

                positions.Sort();
                return positions.ToImmutableArray();
            }

            private readonly struct SplitEdgeKey : IEquatable<SplitEdgeKey>
            {
                public readonly int FromBlockId;
                public readonly int ToBlockId;

                public SplitEdgeKey(int fromBlockId, int toBlockId)
                {
                    FromBlockId = fromBlockId;
                    ToBlockId = toBlockId;
                }

                public bool Equals(SplitEdgeKey other)
                    => FromBlockId == other.FromBlockId && ToBlockId == other.ToBlockId;

                public override bool Equals(object? obj)
                    => obj is SplitEdgeKey other && Equals(other);

                public override int GetHashCode()
                    => HashCode.Combine(FromBlockId, ToBlockId);

                public override string ToString()
                    => $"B{FromBlockId}->B{ToBlockId}";
            }

            private sealed class SplitResolutionPlan
            {
                public readonly Dictionary<int, List<RegisterResolvedMove>> PositionMoves = new();
                public readonly Dictionary<SplitEdgeKey, List<RegisterResolvedMove>> BlockEntryMoves = new();
                public readonly Dictionary<SplitEdgeKey, List<RegisterResolvedMove>> BlockExitMoves = new();
            }

            private SplitResolutionPlan BuildSplitResolutionPlan()
            {
                var plan = new SplitResolutionPlan();

                foreach (var allocation in _allocations.Values)
                {
                    AddSplitTransitionMoves(plan, allocation.Value, allocation.Segments);
                    AddCfgEdgeResolutionMoves(plan, allocation.Value, allocation.Ranges, allocation.Segments);

                    for (int f = 0; f < allocation.Fragments.Length; f++)
                    {
                        AddSplitTransitionMoves(plan, allocation.Value, allocation.Fragments[f].Segments);
                        AddCfgEdgeResolutionMoves(plan, allocation.Value, allocation.Ranges, allocation.Fragments[f].Segments);
                    }
                }

                return plan;
            }

            private void AddSplitTransitionMoves(
                SplitResolutionPlan plan,
                GenTree value,
                ImmutableArray<RegisterAllocationSegment> segments)
            {
                for (int i = 1; i < segments.Length; i++)
                {
                    var previous = segments[i - 1];
                    var current = segments[i];
                    if (previous.Location.Equals(current.Location))
                        continue;

                    var move = new RegisterResolvedMove(
                        previous.Location,
                        current.Location,
                        value,
                        value,
                        MoveFlags.Split);

                    int movePosition = SplitTransitionMovePosition(previous, current);
                    if (movePosition == current.Start && TryGetBlockStartingAtPosition(current.Start, out _))
                        continue;

                    AddMove(plan.PositionMoves, movePosition, move);
                }
            }

            private int SplitTransitionMovePosition(RegisterAllocationSegment previous, RegisterAllocationSegment current)
            {
                if (previous.Location.IsRegister &&
                    current.Location.IsMemoryOperand &&
                    current.Start == previous.End &&
                    current.Start > previous.Start &&
                    IsRegisterKilledAtPosition(previous.Location.Register, current.Start - 1))
                {
                    return current.Start - 1;
                }

                return current.Start;
            }

            private void AddCfgEdgeResolutionMoves(
                SplitResolutionPlan plan,
                GenTree value,
                ImmutableArray<LinearLiveRange> ranges,
                ImmutableArray<RegisterAllocationSegment> segments)
            {
                if (segments.IsDefaultOrEmpty || ranges.IsDefaultOrEmpty)
                    return;

                for (int b = 0; b < _method.Cfg.Blocks.Length; b++)
                {
                    var fromBlock = _method.Cfg.Blocks[b];
                    int fromPosition = _blockEndPositions[b];
                    if (!IsLiveAt(ranges, fromPosition))
                        continue;
                    if (!TryGetLocationAt(segments, fromPosition, out var source))
                        continue;

                    for (int s = 0; s < fromBlock.Successors.Length; s++)
                    {
                        var edge = fromBlock.Successors[s];
                        if ((uint)edge.ToBlockId >= (uint)_method.Cfg.Blocks.Length)
                            throw new InvalidOperationException($"Invalid split target block B{edge.ToBlockId}.");

                        int toPosition = _blockStartPositions[edge.ToBlockId];
                        if (!IsLiveAt(ranges, toPosition))
                            continue;
                        if (!TryGetLocationAt(segments, toPosition, out var destination))
                            continue;

                        if (source.Equals(destination))
                            continue;

                        if (edge.Kind == CfgEdgeKind.Exception)
                        {
                            throw new InvalidOperationException(
                                $"Cannot resolve split of {value} on exception edge {edge}: exception edges cannot execute register moves. " +
                                "The value must remain stack-homed or have the same location at both sides of the edge.");
                        }

                        AddNormalEdgeMove(
                            plan,
                            edge,
                            new RegisterResolvedMove(source, destination, value, value, MoveFlags.Split));
                    }
                }
            }

            private static bool BlockContainsNonSelfPhiDestination(GenTreeBlock block, CfgEdge edge, GenTreeValueKey key)
            {
                var nodes = block.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (!node.IsPhiCopy ||
                        node.LinearPhiCopyFromBlockId != edge.FromBlockId ||
                        node.LinearPhiCopyToBlockId != edge.ToBlockId ||
                        node.RegisterResult is null ||
                        !node.RegisterResult.LinearValueKey.Equals(key))
                    {
                        continue;
                    }

                    if (node.RegisterUses.Length == 0)
                        return true;

                    for (int u = 0; u < node.RegisterUses.Length; u++)
                    {
                        if (!node.RegisterUses[u].LinearValueKey.Equals(key))
                            return true;
                    }
                }

                return false;
            }

            private static bool BlockContainsPhiCopyForEdge(GenTreeBlock block, SplitEdgeKey key)
            {
                var nodes = block.LinearNodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = nodes[i];
                    if (node.IsPhiCopy &&
                        node.LinearPhiCopyFromBlockId == key.FromBlockId &&
                        node.LinearPhiCopyToBlockId == key.ToBlockId)
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool EdgeHasPhiCopyGroup(SplitEdgeKey key)
            {
                if ((uint)key.FromBlockId < (uint)_method.Blocks.Length &&
                    BlockContainsPhiCopyForEdge(_method.Blocks[key.FromBlockId], key))
                {
                    return true;
                }

                if ((uint)key.ToBlockId < (uint)_method.Blocks.Length &&
                    BlockContainsPhiCopyForEdge(_method.Blocks[key.ToBlockId], key))
                {
                    return true;
                }

                return false;
            }

            private static void AddAndRemoveEdgeMoves(
                Dictionary<SplitEdgeKey, List<RegisterResolvedMove>> map,
                SplitEdgeKey key,
                List<RegisterResolvedMove> moves)
            {
                if (!map.TryGetValue(key, out var edgeMoves) || edgeMoves.Count == 0)
                    return;

                if (moves.Count == 0)
                {
                    moves.AddRange(edgeMoves);
                    map.Remove(key);
                    return;
                }

                for (int i = 0; i < edgeMoves.Count; i++)
                {
                    var edgeMove = edgeMoves[i];
                    if (IsCoveredByNonSelfPhiMove(edgeMove, moves))
                        continue;

                    moves.Add(edgeMove);
                }

                map.Remove(key);
            }

            private static bool IsCoveredByNonSelfPhiMove(RegisterResolvedMove edgeMove, List<RegisterResolvedMove> phiMoves)
            {
                var destinationValue = edgeMove.DestinationValue;
                if (destinationValue is null || edgeMove.SourceValue is null)
                    return false;

                if (!edgeMove.SourceValue.LinearValueKey.Equals(destinationValue.LinearValueKey))
                    return false;

                for (int i = 0; i < phiMoves.Count; i++)
                {
                    var phiMove = phiMoves[i];
                    if ((phiMove.MoveFlags & MoveFlags.ParallelCopy) == 0)
                        continue;

                    if (phiMove.DestinationValue is null || phiMove.SourceValue is null)
                        continue;

                    if (!phiMove.DestinationValue.LinearValueKey.Equals(destinationValue.LinearValueKey))
                        continue;

                    if (phiMove.SourceValue.LinearValueKey.Equals(phiMove.DestinationValue.LinearValueKey))
                        continue;

                    if (!phiMove.Destination.Equals(edgeMove.Destination))
                        continue;

                    return true;
                }

                return false;
            }

            private static bool IsPredecessorExitPhiGroup(GenTree groupHead, int blockId)
                => groupHead.IsPhiCopy &&
                   groupHead.LinearBlockId == blockId &&
                   groupHead.LinearPhiCopyFromBlockId == blockId &&
                   groupHead.LinearPhiCopyToBlockId != blockId;

            private void AddMatchingEdgeSplitMovesToPhiGroup(
                SplitResolutionPlan splitPlan,
                GenTree groupHead,
                List<RegisterResolvedMove> moves)
            {
                var key = new SplitEdgeKey(groupHead.LinearPhiCopyFromBlockId, groupHead.LinearPhiCopyToBlockId);

                if (groupHead.LinearBlockId != key.FromBlockId && groupHead.LinearBlockId != key.ToBlockId)
                {
                    throw new InvalidOperationException(
                        $"Phi copy group for edge {key} is placed in unrelated block B{groupHead.LinearBlockId}.");
                }

                AddAndRemoveEdgeMoves(splitPlan.BlockExitMoves, key, moves);
                AddAndRemoveEdgeMoves(splitPlan.BlockEntryMoves, key, moves);
            }


            private void AddNormalEdgeMove(SplitResolutionPlan plan, CfgEdge edge, RegisterResolvedMove move)
            {
                int normalPreds = CountNormalPredecessors(edge.ToBlockId);
                int normalSuccs = CountNormalSuccessors(edge.FromBlockId);
                var key = new SplitEdgeKey(edge.FromBlockId, edge.ToBlockId);

                if (normalPreds == 1)
                {
                    AddMove(plan.BlockEntryMoves, key, move);
                    return;
                }

                if (normalSuccs == 1)
                {
                    AddMove(plan.BlockExitMoves, key, move);
                    return;
                }

                throw new InvalidOperationException(
                    $"Cannot resolve LSRA split move on unsplit critical edge {edge}: " +
                    $"B{edge.FromBlockId} has {normalSuccs} normal successors and B{edge.ToBlockId} has {normalPreds} normal predecessors.");
            }

            private int CountNormalPredecessors(int blockId)
            {
                int count = 0;
                var predecessors = _method.Cfg.Blocks[blockId].Predecessors;
                for (int i = 0; i < predecessors.Length; i++)
                {
                    if (predecessors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private int CountNormalSuccessors(int blockId)
            {
                int count = 0;
                var successors = _method.Cfg.Blocks[blockId].Successors;
                for (int i = 0; i < successors.Length; i++)
                {
                    if (successors[i].Kind != CfgEdgeKind.Exception)
                        count++;
                }
                return count;
            }

            private bool TryGetBlockStartingAtPosition(int position, out int blockId)
            {
                for (int b = 0; b < _blockStartPositions.Length; b++)
                {
                    if (_blockStartPositions[b] == position)
                    {
                        blockId = b;
                        return true;
                    }
                }

                blockId = -1;
                return false;
            }

            private static bool TryGetLocationAt(
                ImmutableArray<RegisterAllocationSegment> segments,
                int position,
                out RegisterOperand location)
            {
                for (int i = 0; i < segments.Length; i++)
                {
                    var segment = segments[i];
                    if (segment.Contains(position))
                    {
                        location = segment.Location;
                        return true;
                    }
                }

                location = RegisterOperand.None;
                return false;
            }

            private static void AddMove<TKey>(
                Dictionary<TKey, List<RegisterResolvedMove>> movesByKey,
                TKey key,
                RegisterResolvedMove move)
                where TKey : notnull
            {
                if (!movesByKey.TryGetValue(key, out var moves))
                {
                    moves = new List<RegisterResolvedMove>();
                    movesByKey.Add(key, moves);
                }

                AddUniqueMove(moves, move);
            }

            private void EmitBlockEntrySplitMoves(
                int blockId,
                Func<int> getCopyScratchSlot,
                SplitResolutionPlan splitPlan,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                EmitEdgeSplitMovesForBlock(
                    blockId,
                    isEntry: true,
                    getCopyScratchSlot,
                    splitPlan,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitBlockExitSplitMoves(
                int blockId,
                Func<int> getCopyScratchSlot,
                SplitResolutionPlan splitPlan,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                EmitEdgeSplitMovesForBlock(
                    blockId,
                    isEntry: false,
                    getCopyScratchSlot,
                    splitPlan,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitEdgeSplitMovesForBlock(
                int blockId,
                bool isEntry,
                Func<int> getCopyScratchSlot,
                SplitResolutionPlan splitPlan,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var map = isEntry ? splitPlan.BlockEntryMoves : splitPlan.BlockExitMoves;
                if (map.Count == 0)
                    return;

                var keys = new List<SplitEdgeKey>();
                foreach (var kv in map)
                {
                    var key = kv.Key;
                    if (isEntry)
                    {
                        if (key.ToBlockId != blockId)
                            continue;
                    }
                    else
                    {
                        if (key.FromBlockId != blockId)
                            continue;
                    }

                    if (EdgeHasPhiCopyGroup(key))
                        continue;

                    keys.Add(key);
                }

                if (keys.Count == 0)
                    return;

                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    if (!map.TryGetValue(key, out var edgeMoves) || edgeMoves.Count == 0)
                        continue;

                    map.Remove(key);
                    EmitResolvedEdgeSplitMoves(
                        blockId,
                        key,
                        getCopyScratchSlot,
                        edgeMoves,
                        blockLinearNodes,
                        allNodes);
                }
            }

            private void EmitResolvedEdgeSplitMoves(
                int blockId,
                SplitEdgeKey key,
                Func<int> getCopyScratchSlot,
                List<RegisterResolvedMove> moves,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (moves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    key.FromBlockId,
                    key.ToBlockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove,
                    phiCopyFromBlockId: key.FromBlockId,
                    phiCopyToBlockId: key.ToBlockId);

                for (int i = 0; i < resolved.Length; i++)
                    AppendMoveNode(blockId, resolved[i], blockLinearNodes, allNodes);
            }

            private void EmitSplitMovesAtPosition(
                int blockId,
                int position,
                Func<int> getCopyScratchSlot,
                Dictionary<int, List<RegisterResolvedMove>> splitMoves,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (!splitMoves.TryGetValue(position, out var moves) || moves.Count == 0)
                    return;

                EmitResolvedSplitMoves(
                    blockId,
                    blockId,
                    getCopyScratchSlot,
                    moves,
                    blockLinearNodes,
                    allNodes);
            }

            private static void AddUniqueMove(List<RegisterResolvedMove> moves, RegisterResolvedMove move)
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    var existing = moves[i];
                    if (!existing.Source.Equals(move.Source) ||
                        !existing.Destination.Equals(move.Destination))
                    {
                        continue;
                    }

                    if (!TryMergeMoveValue(existing.SourceValue, move.SourceValue, out var sourceValue) ||
                        !TryMergeMoveValue(existing.DestinationValue, move.DestinationValue, out var destinationValue))
                    {
                        continue;
                    }

                    moves[i] = new RegisterResolvedMove(
                        existing.Source,
                        existing.Destination,
                        sourceValue,
                        destinationValue,
                        existing.MoveFlags | move.MoveFlags);
                    return;
                }

                moves.Add(move);
            }

            private static bool TryMergeMoveValue(GenTree? left, GenTree? right, out GenTree? merged)
            {
                if (left is null)
                {
                    merged = right;
                    return true;
                }

                if (right is null)
                {
                    merged = left;
                    return true;
                }

                if (SameValue(left, right))
                {
                    merged = left;
                    return true;
                }

                merged = null;
                return false;
            }

            private static bool SameValue(GenTree? left, GenTree? right)
            {
                if (ReferenceEquals(left, right))
                    return true;

                if (left is null || right is null)
                    return false;

                return left.LinearValueKey.Equals(right.LinearValueKey);
            }

            private void AppendMoveNode(
                int blockId,
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (!IsMoveNode(node))
                {
                    var placed = node.WithPlacement(blockId, blockLinearNodes.Count);
                    blockLinearNodes.Add(placed);
                    allNodes.Add(placed);
                    return;
                }

                node = node.WithPlacement(blockId, blockLinearNodes.Count);
                if (IsRedundantMove(node))
                    return;

                if (blockLinearNodes.Count != 0 &&
                    TryFoldMoveWithPrevious(
                        blockLinearNodes[blockLinearNodes.Count - 1],
                        node,
                        blockId,
                        blockLinearNodes.Count - 1,
                        out var previousReplacement,
                        out var currentReplacement))
                {
                    ReplacePreviousNode(previousReplacement, blockLinearNodes, allNodes);

                    if (currentReplacement is null || IsRedundantMove(currentReplacement))
                        return;

                    node = currentReplacement.WithPlacement(blockId, blockLinearNodes.Count);
                }

                if (blockLinearNodes.Count != 0 &&
                    TryRewriteAfterPreviousMove(blockLinearNodes[blockLinearNodes.Count - 1], node, blockId, blockLinearNodes.Count, out var rewritten))
                {
                    if (rewritten is null || IsRedundantMove(rewritten))
                        return;

                    node = rewritten;
                }

                blockLinearNodes.Add(node);
                allNodes.Add(node);
            }

            private static void ReplacePreviousNode(
                GenTree? replacement,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (blockLinearNodes.Count == 0 || allNodes.Count == 0)
                    throw new InvalidOperationException("Cannot replace previous node in an empty LIR builder.");

                if (replacement is null)
                {
                    blockLinearNodes.RemoveAt(blockLinearNodes.Count - 1);
                    allNodes.RemoveAt(allNodes.Count - 1);
                    return;
                }

                blockLinearNodes[blockLinearNodes.Count - 1] = replacement;
                allNodes[allNodes.Count - 1] = replacement;
            }

            private static bool IsMoveNode(GenTree node)
                => (node.Kind == GenTreeKind.Copy || node.Kind == GenTreeKind.Reload || node.Kind == GenTreeKind.Spill) &&
                   node.Results.Length == 1 &&
                   node.Uses.Length == 1;

            private static bool IsRedundantMove(GenTree node)
            {
                if (!IsMoveNode(node) || !node.Results[0].Equals(node.Uses[0]))
                    return false;

                return !IsLogicalValueRename(node);
            }

            private static bool IsLogicalValueRename(GenTree node)
            {
                var resultValue = SingleRegisterResultValue(node);
                var useValue = SingleRegisterUseValue(node);
                return resultValue is not null &&
                       useValue is not null &&
                       !resultValue.LinearValueKey.Equals(useValue.LinearValueKey);
            }

            private static bool TryFoldMoveWithPrevious(
                GenTree previous,
                GenTree current,
                int blockId,
                int previousOrdinal,
                out GenTree? previousReplacement,
                out GenTree? currentReplacement)
            {
                previousReplacement = previous;
                currentReplacement = current;

                if (!IsMoveNode(previous) || !IsMoveNode(current))
                    return false;

                if (previous.IsPhiCopy || current.IsPhiCopy)
                    return false;

                if (previous.Results[0].Equals(current.Results[0]) &&
                    previous.Uses[0].Equals(current.Uses[0]))
                {
                    currentReplacement = null;
                    return true;
                }

                if (!previous.Results[0].IsRegister || !current.Uses[0].IsRegister)
                    return false;

                if (previous.Results[0].Register != current.Uses[0].Register)
                    return false;

                if (!IsScratchRegister(previous.Results[0].Register))
                    return false;

                if (previous.MoveKind == MoveKind.Load && current.MoveKind == MoveKind.Register)
                {
                    previousReplacement = GenTreeLirFactory.Move(
                        previous.LinearId >= 0 ? previous.LinearId : previous.Id,
                        blockId,
                        previousOrdinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current),
                        comment: MergeMoveComments(previous.Comment, current.Comment, "fold scratch reload"),
                        moveFlags: StripReloadSpill(previous.MoveFlags | current.MoveFlags));
                    currentReplacement = null;
                    return true;
                }

                if (previous.MoveKind == MoveKind.LoadAddress && current.MoveKind == MoveKind.Register)
                {
                    previousReplacement = GenTreeLirFactory.Move(
                        previous.LinearId >= 0 ? previous.LinearId : previous.Id,
                        blockId,
                        previousOrdinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current),
                        comment: MergeMoveComments(previous.Comment, current.Comment, "fold scratch address"),
                        moveFlags: StripReloadSpill(previous.MoveFlags | current.MoveFlags));
                    currentReplacement = null;
                    return true;
                }

                if (previous.MoveKind == MoveKind.Register && current.MoveKind == MoveKind.Register)
                {
                    previousReplacement = GenTreeLirFactory.Move(
                        previous.LinearId >= 0 ? previous.LinearId : previous.Id,
                        blockId,
                        previousOrdinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current),
                        comment: MergeMoveComments(previous.Comment, current.Comment, "fold scratch copy"),
                        moveFlags: StripReloadSpill(previous.MoveFlags | current.MoveFlags));
                    currentReplacement = null;
                    return true;
                }

                if (previous.MoveKind == MoveKind.Register && current.MoveKind == MoveKind.Store)
                {
                    previousReplacement = GenTreeLirFactory.Move(
                        previous.LinearId >= 0 ? previous.LinearId : previous.Id,
                        blockId,
                        previousOrdinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current),
                        comment: MergeMoveComments(previous.Comment, current.Comment, "fold scratch store"),
                        moveFlags: current.MoveFlags & ~MoveFlags.Reload);
                    currentReplacement = null;
                    return true;
                }

                return false;
            }

            private static bool IsScratchRegister(MachineRegister register)
            {
                return register == MachineRegisters.BackendScratch ||
                       register == MachineRegisters.TreeScratch3 ||
                       register == MachineRegisters.ParallelCopyScratch0 ||
                       register == MachineRegisters.ParallelCopyScratch1 ||
                       register == MachineRegisters.FloatBackendScratch ||
                       register == MachineRegisters.FloatTreeScratch3 ||
                       register == MachineRegisters.FloatParallelCopyScratch0 ||
                       register == MachineRegisters.FloatParallelCopyScratch1;
            }

            private static string MergeMoveComments(string? left, string? right, string fallback)
            {
                if (string.IsNullOrEmpty(left))
                    return string.IsNullOrEmpty(right) ? fallback : right + "; " + fallback;
                if (string.IsNullOrEmpty(right))
                    return left + "; " + fallback;
                return left + "; " + right + "; " + fallback;
            }

            private static bool TryRewriteAfterPreviousMove(
                GenTree previous,
                GenTree current,
                int blockId,
                int ordinal,
                out GenTree? rewritten)
            {
                rewritten = current;

                if (!IsMoveNode(previous) || !IsMoveNode(current))
                    return false;

                if (previous.IsPhiCopy || current.IsPhiCopy)
                    return false;

                if (previous.MoveKind == MoveKind.Store &&
                    current.MoveKind == MoveKind.Load &&
                    previous.Results[0].Equals(current.Uses[0]) &&
                    previous.Uses[0].IsRegister &&
                    current.Results[0].IsRegister)
                {
                    rewritten = GenTreeLirFactory.Move(
                        current.LinearId >= 0 ? current.LinearId : current.Id,
                        blockId,
                        ordinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current),
                        comment: current.Comment is null ? "forward adjacent store/load" : current.Comment + "; forward adjacent store/load",
                        moveFlags: StripReloadSpill(current.MoveFlags),
                        phiCopyFromBlockId: current.LinearPhiCopyFromBlockId,
                        phiCopyToBlockId: current.LinearPhiCopyToBlockId);
                    return true;
                }

                if (CanForwardAdjacentRegisterCopy(previous, current))
                {
                    var sourceValue = SingleRegisterUseValue(previous) ?? SingleRegisterUseValue(current);
                    rewritten = GenTreeLirFactory.Move(
                        current.LinearId >= 0 ? current.LinearId : current.Id,
                        blockId,
                        ordinal,
                        current.Results[0],
                        previous.Uses[0],
                        SingleRegisterResultValue(current),
                        sourceValue,
                        comment: current.Comment is null ? "forward adjacent register copy" : current.Comment + "; forward adjacent register copy",
                        moveFlags: StripReloadSpill(current.MoveFlags),
                        phiCopyFromBlockId: current.LinearPhiCopyFromBlockId,
                        phiCopyToBlockId: current.LinearPhiCopyToBlockId);
                    return true;
                }

                return false;
            }

            private static bool CanForwardAdjacentRegisterCopy(GenTree previous, GenTree current)
            {
                if (previous.MoveKind != MoveKind.Register || current.MoveKind != MoveKind.Register)
                    return false;

                if (!previous.Results[0].IsRegister || !previous.Uses[0].IsRegister ||
                    !current.Results[0].IsRegister || !current.Uses[0].IsRegister)
                {
                    return false;
                }

                if (!previous.Results[0].Equals(current.Uses[0]))
                    return false;

                if (!IsNonGcBitwiseMove(previous) || !IsNonGcBitwiseMove(current))
                    return false;

                return true;
            }

            private static bool IsNonGcBitwiseMove(GenTree node)
            {
                var resultValue = SingleRegisterResultValue(node);
                var useValue = SingleRegisterUseValue(node);
                return !MayCarryGcManagedPointer(resultValue) && !MayCarryGcManagedPointer(useValue);
            }

            private static bool MayCarryGcManagedPointer(GenTree? value)
            {
                if (value is null)
                    return false;

                if (value.StackKind is GenStackKind.Ref or GenStackKind.ByRef or GenStackKind.Null)
                    return true;

                var type = value.Type;
                if (type is null)
                    return false;

                if (type.Kind == RuntimeTypeKind.ByRef)
                    return true;

                return !type.IsValueType || type.ContainsGcPointers;
            }

            private static MoveFlags StripReloadSpill(MoveFlags flags)
                => flags & ~(MoveFlags.Reload | MoveFlags.Spill);

            private static GenTree? SingleRegisterResultValue(GenTree node)
                => node.RegisterResults.Length == 1 ? node.RegisterResults[0] : node.RegisterResult;

            private static GenTree? SingleRegisterUseValue(GenTree node)
                => node.RegisterUses.Length == 1 ? node.RegisterUses[0] : null;

            private void EmitResolvedSplitMoves(
                int insertionBlockId,
                int toBlockId,
                Func<int> getCopyScratchSlot,
                List<RegisterResolvedMove> moves,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var effectiveMoves = SimplifySameValuePositionSplitMoves(moves);
                effectiveMoves = RetargetSameValuePositionSplitSources(effectiveMoves);
                if (effectiveMoves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    insertionBlockId,
                    toBlockId,
                    effectiveMoves,
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove);

                for (int i = 0; i < resolved.Length; i++)
                    AppendMoveNode(insertionBlockId, resolved[i], blockLinearNodes, allNodes);
            }

            private static List<RegisterResolvedMove> SimplifySameValuePositionSplitMoves(List<RegisterResolvedMove> moves)
            {
                if (moves.Count < 2)
                    return new List<RegisterResolvedMove>(moves);

                var keep = new bool[moves.Count];
                Array.Fill(keep, true);

                for (int i = 0; i < moves.Count; i++)
                {
                    if (!keep[i])
                        continue;

                    var reload = moves[i];
                    if (!IsReloadForStableHomeRoundTrip(reload))
                        continue;

                    for (int j = 0; j < moves.Count; j++)
                    {
                        if (i == j || !keep[j])
                            continue;

                        var spill = moves[j];
                        if (!IsRedundantRoundTripStore(reload, spill))
                            continue;

                        if (HasOtherWriterToLocation(moves, keep, reload.Source, i, j))
                            continue;

                        keep[j] = false;
                        break;
                    }
                }

                var result = new List<RegisterResolvedMove>(moves.Count);
                for (int i = 0; i < moves.Count; i++)
                {
                    if (keep[i])
                        result.Add(moves[i]);
                }

                return result;
            }

            private static List<RegisterResolvedMove> RetargetSameValuePositionSplitSources(List<RegisterResolvedMove> moves)
            {
                if (moves.Count < 2)
                    return moves;

                var result = new List<RegisterResolvedMove>(moves);
                for (int i = 0; i < result.Count; i++)
                {
                    var move = result[i];
                    if (!IsSameValueMove(move))
                        continue;

                    var source = move.Source;
                    var sourceValue = move.SourceValue;
                    var seen = new HashSet<RegisterOperand>();
                    while (TryFindSameValueProducer(result, i, source, sourceValue, out var producer))
                    {
                        if (!seen.Add(source))
                            break;

                        source = producer.Source;
                        sourceValue = producer.SourceValue;
                    }

                    if (!source.Equals(move.Source))
                        result[i] = move.WithSource(source, sourceValue);
                }

                return result;
            }

            private static bool TryFindSameValueProducer(
                List<RegisterResolvedMove> moves,
                int consumerIndex,
                RegisterOperand source,
                GenTree? sourceValue,
                out RegisterResolvedMove producer)
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    if (i == consumerIndex)
                        continue;

                    var candidate = moves[i];
                    if (!candidate.Destination.Equals(source))
                        continue;
                    if (!SameValue(candidate.DestinationValue, sourceValue))
                        continue;
                    if (!IsSameValueMove(candidate))
                        continue;

                    producer = candidate;
                    return true;
                }

                producer = default;
                return false;
            }

            private static bool IsSameValueMove(RegisterResolvedMove move)
                => SameValue(move.SourceValue, move.DestinationValue);

            private static bool IsReloadForStableHomeRoundTrip(RegisterResolvedMove move)
                => move.Source.IsMemoryOperand &&
                   move.Destination.IsRegister &&
                   move.SourceValue is not null &&
                   move.DestinationValue is not null &&
                   move.SourceValue.LinearValueKey.Equals(move.DestinationValue.LinearValueKey);

            private static bool IsRedundantRoundTripStore(RegisterResolvedMove reload, RegisterResolvedMove spill)
                => spill.Source.Equals(reload.Destination) &&
                   spill.Destination.Equals(reload.Source) &&
                   spill.SourceValue is not null &&
                   spill.DestinationValue is not null &&
                   spill.SourceValue.LinearValueKey.Equals(spill.DestinationValue.LinearValueKey) &&
                   reload.SourceValue is not null &&
                   spill.SourceValue.LinearValueKey.Equals(reload.SourceValue.LinearValueKey);

            private static bool HasOtherWriterToLocation(
                List<RegisterResolvedMove> moves,
                bool[] keep,
                RegisterOperand location,
                int firstExcludedIndex,
                int secondExcludedIndex)
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    if (!keep[i] || i == firstExcludedIndex || i == secondExcludedIndex)
                        continue;

                    if (moves[i].Destination.Equals(location))
                        return true;
                }

                return false;
            }

            private static bool IsBlockTerminatorNode(GenTree node)
                => node.Kind is GenTreeKind.Branch or GenTreeKind.BranchTrue or GenTreeKind.BranchFalse or
                   GenTreeKind.Return or GenTreeKind.Throw or GenTreeKind.Rethrow or GenTreeKind.EndFinally;

            private static MachineRegister GetMaybeArgumentRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
            {
                if (registerClass == RegisterClass.Float)
                    return MachineRegisters.GetFloatArgumentRegister(floatIndex++);
                if (registerClass == RegisterClass.General)
                    return MachineRegisters.GetIntegerArgumentRegister(generalIndex++);
                return MachineRegister.Invalid;
            }


            private void EmitGcPollNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var pollNode = GenTreeLirFactory.GcPoll(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node.LinearId,
                    source: node,
                    comment: "loop backedge GC poll");
                blockLinearNodes.Add(pollNode);
                allNodes.Add(pollNode);
            }


            private static bool IsSamePhiCopyGroup(GenTree left, GenTree right)
                => left.IsPhiCopy &&
                   right.IsPhiCopy &&
                   left.LinearPhiCopyFromBlockId == right.LinearPhiCopyFromBlockId &&
                   left.LinearPhiCopyToBlockId == right.LinearPhiCopyToBlockId;

            private readonly struct PendingAggregateHomeStore
            {
                public readonly GenTree Value;
                public readonly int Position;
                public readonly AbiValueInfo Abi;
                public readonly RegisterValueLocation Fragments;

                public PendingAggregateHomeStore(GenTree value, int position, AbiValueInfo abi, RegisterValueLocation fragments)
                {
                    Value = value;
                    Position = position;
                    Abi = abi;
                    Fragments = fragments;
                }
            }

            private int EmitPhiCopyGroup(
                ImmutableArray<GenTree> nodes,
                int firstIndex,
                int blockId,
                SplitResolutionPlan splitPlan,
                Action<int> emitBlockSplitMoves,
                bool emitPostPhiPositionMoves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var groupHead = nodes[firstIndex];
                var moves = new List<RegisterResolvedMove>();
                var aggregateHomeStores = new List<PendingAggregateHomeStore>();
                int lastIndex = firstIndex;

                for (int i = firstIndex; i < nodes.Length && IsSamePhiCopyGroup(groupHead, nodes[i]); i++)
                {
                    var node = nodes[i];
                    int position = GetNodePosition(node);
                    emitBlockSplitMoves(position);

                    if (!TryBuildPhiCopyMoves(node, position, moves, aggregateHomeStores))
                    {
                        EmitResolvedPhiCopyMoves(
                            blockId,
                            groupHead.LinearPhiCopyFromBlockId,
                            groupHead.LinearPhiCopyToBlockId,
                            moves,
                            getCopyScratchSlot,
                            blockLinearNodes,
                            allNodes);
                        moves.Clear();
                        EmitPendingAggregateHomeStores(blockId, aggregateHomeStores, blockLinearNodes, allNodes);
                        aggregateHomeStores.Clear();
                        EmitCopyNode(node, getCopyScratchSlot, blockLinearNodes, allNodes);
                    }

                    lastIndex = i;
                }

                AddMatchingEdgeSplitMovesToPhiGroup(splitPlan, groupHead, moves);

                EmitResolvedPhiCopyMoves(
                    blockId,
                    groupHead.LinearPhiCopyFromBlockId,
                    groupHead.LinearPhiCopyToBlockId,
                    moves,
                    getCopyScratchSlot,
                    blockLinearNodes,
                    allNodes);
                EmitPendingAggregateHomeStores(blockId, aggregateHomeStores, blockLinearNodes, allNodes);

                if (emitPostPhiPositionMoves)
                {
                    for (int i = firstIndex; i <= lastIndex; i++)
                    {
                        int position = GetNodePosition(nodes[i]);
                        emitBlockSplitMoves(position + 1);
                    }
                }

                return lastIndex;
            }

            private void EmitPendingAggregateHomeStores(
                int blockId,
                List<PendingAggregateHomeStore> stores,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                for (int i = 0; i < stores.Count; i++)
                {
                    var store = stores[i];
                    EmitAggregateHomeStores(
                        blockId,
                        store.Value,
                        store.Position,
                        store.Abi,
                        store.Fragments,
                        "phi aggregate home",
                        blockLinearNodes,
                        allNodes);
                }
            }

            private void EmitResolvedPhiCopyMoves(
                int blockId,
                int fromBlockId,
                int toBlockId,
                List<RegisterResolvedMove> moves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (moves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    fromBlockId,
                    toBlockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove,
                    phiCopyFromBlockId: fromBlockId,
                    phiCopyToBlockId: toBlockId,
                    preserveIdentityMoves: true);

                for (int i = 0; i < resolved.Length; i++)
                    AppendMoveNode(blockId, resolved[i], blockLinearNodes, allNodes);
            }

            private void EmitResolvedCopyMoves(
                int blockId,
                List<RegisterResolvedMove> moves,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (moves.Count == 0)
                    return;

                var resolved = RegisterParallelCopyResolver.Resolve(
                    blockId,
                    blockId,
                    new List<RegisterResolvedMove>(moves),
                    getCopyScratchSlot,
                    _options.ParallelCopyScratchRegister0,
                    _options.ParallelCopyFloatScratchRegister0,
                    ref _nextNodeId,
                    CanEmitDirectMemoryMove);

                for (int i = 0; i < resolved.Length; i++)
                    AppendMoveNode(blockId, resolved[i], blockLinearNodes, allNodes);
            }

            private bool TryBuildPhiCopyMoves(
                GenTree node,
                int position,
                List<RegisterResolvedMove> moves,
                List<PendingAggregateHomeStore> aggregateHomeStores)
            {
                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                var destinationValue = node.RegisterResult;
                var sourceValue = node.RegisterUses[0];
                var destinationInfo = _method.GetValueInfo(destinationValue);
                var sourceInfo = _method.GetValueInfo(sourceValue);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                var sourceAbi = MachineAbi.ClassifyStorageValue(sourceInfo.Type, sourceInfo.StackKind);

                if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister ||
                    sourceAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    if (destinationAbi.PassingKind != AbiValuePassingKind.MultiRegister ||
                        sourceAbi.PassingKind != AbiValuePassingKind.MultiRegister)
                    {
                        throw new InvalidOperationException(
                            $"Cannot lower scalar/aggregate phi copy as a parallel copy: {sourceValue} -> {destinationValue}.");
                    }

                    int destinationPosition = PhiDestinationPosition(node);
                    int sourcePosition = PhiSourcePosition(node);
                    var destinationLocation = ValueLocationForDefinition(destinationValue, destinationPosition, destinationAbi);
                    var sourceLocation = ValueLocationForUse(sourceValue, sourcePosition, sourceAbi);
                    if (destinationLocation.Count != sourceLocation.Count)
                    {
                        throw new InvalidOperationException(
                            $"Cannot lower multi-register phi copy with different ABI segment counts: {sourceValue} -> {destinationValue}.");
                    }

                    for (int i = 0; i < destinationLocation.Count; i++)
                    {
                        var destination = destinationLocation[i];
                        var source = sourceLocation[i];
                        if (destination.IsNone)
                            continue;
                        if (source.IsNone)
                        {
                            throw new InvalidOperationException(
                                $"Cannot resolve phi copy {sourceValue} -> {destinationValue}: source ABI segment {i} has no physical home at B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                        }

                        moves.Add(new RegisterResolvedMove(
                            source,
                            destination,
                            sourceValue,
                            destinationValue,
                            MoveFlags.ParallelCopy));
                    }

                    aggregateHomeStores.Add(new PendingAggregateHomeStore(
                        destinationValue,
                        destinationPosition,
                        destinationAbi,
                        destinationLocation));
                    return true;
                }

                var scalarDestination = HomeForDefinition(destinationValue, PhiDestinationPosition(node));
                if (scalarDestination.IsNone)
                    return true;

                var scalarSource = HomeForUse(sourceValue, PhiSourcePosition(node));
                if (scalarSource.IsNone)
                {
                    throw new InvalidOperationException(
                        $"Cannot resolve phi copy {sourceValue} -> {destinationValue}: source value has no physical home at B{node.LinearPhiCopyFromBlockId}->B{node.LinearPhiCopyToBlockId}.");
                }

                moves.Add(new RegisterResolvedMove(
                    scalarSource,
                    scalarDestination,
                    sourceValue,
                    destinationValue,
                    MoveFlags.ParallelCopy));

                return true;
            }

            private int PhiSourcePosition(GenTree node)
            {
                if ((uint)node.LinearPhiCopyFromBlockId >= (uint)_blockEndPositions.Length)
                    throw new InvalidOperationException($"Invalid phi-copy source block B{node.LinearPhiCopyFromBlockId} for {node}.");

                int blockEnd = _blockEndPositions[node.LinearPhiCopyFromBlockId];
                int blockStart = _blockStartPositions[node.LinearPhiCopyFromBlockId];
                return blockEnd > blockStart ? blockEnd - 1 : blockStart;
            }

            private int PhiDestinationPosition(GenTree node)
            {
                if ((uint)node.LinearPhiCopyToBlockId >= (uint)_blockStartPositions.Length)
                    throw new InvalidOperationException($"Invalid phi-copy destination block B{node.LinearPhiCopyToBlockId} for {node}.");

                return _blockStartPositions[node.LinearPhiCopyToBlockId];
            }

            private void EmitCopyNode(
                GenTree node,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return;

                int position = GetNodePosition(node);
                EmitValueCopy(
                    node.LinearBlockId,
                    node.RegisterResult,
                    position + 1,
                    node.RegisterUses[0],
                    position,
                    "linear copy",
                    getCopyScratchSlot,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitValueCopy(
                int blockId,
                GenTree destinationValue,
                int destinationPosition,
                GenTree sourceValue,
                int sourcePosition,
                string comment,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                var destinationInfo = _method.GetValueInfo(destinationValue);
                var sourceInfo = _method.GetValueInfo(sourceValue);
                var destinationAbi = MachineAbi.ClassifyStorageValue(destinationInfo.Type, destinationInfo.StackKind);
                var sourceAbi = MachineAbi.ClassifyStorageValue(sourceInfo.Type, sourceInfo.StackKind);

                if (destinationAbi.PassingKind == AbiValuePassingKind.MultiRegister &&
                    sourceAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var destinationLocation = ValueLocationForDefinition(destinationValue, destinationPosition, destinationAbi);
                    var sourceLocation = ValueLocationForUse(sourceValue, sourcePosition, sourceAbi);
                    if (destinationLocation.Count != sourceLocation.Count)
                        throw new InvalidOperationException(
                            $"Cannot copy multi-register values with different ABI segment counts: {sourceValue} -> {destinationValue}.");

                    var moves = new List<RegisterResolvedMove>();
                    for (int i = 0; i < destinationLocation.Count; i++)
                    {
                        var destination = destinationLocation[i];
                        var source = sourceLocation[i];
                        if (destination.IsNone || source.IsNone || source.Equals(destination))
                            continue;

                        moves.Add(new RegisterResolvedMove(
                            source,
                            destination,
                            sourceValue,
                            destinationValue,
                            MoveFlags.ParallelCopy));
                    }

                    EmitResolvedCopyMoves(blockId, moves, getCopyScratchSlot, blockLinearNodes, allNodes);

                    EmitAggregateHomeStores(
                        blockId,
                        destinationValue,
                        destinationPosition,
                        destinationAbi,
                        destinationLocation,
                        comment + " aggregate home",
                        blockLinearNodes,
                        allNodes);
                    return;
                }

                var scalarDestination = HomeForDefinition(destinationValue, destinationPosition);
                if (scalarDestination.IsNone)
                    return;

                var scalarSource = HomeForUse(sourceValue, sourcePosition);
                if (scalarSource.Equals(scalarDestination))
                    return;

                EmitMoveSequence(
                    blockId,
                    scalarDestination,
                    scalarSource,
                    destinationValue,
                    sourceValue,
                    comment,
                    blockLinearNodes,
                    allNodes);
            }

            private void EmitMoveSequence(
                int blockId,
                RegisterOperand destination,
                RegisterOperand source,
                GenTree? destinationValue,
                GenTree? sourceValue,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes,
                MoveFlags moveFlags = MoveFlags.None)
            {
                if (destination.IsNone || source.IsNone)
                    return;

                if (!RequiresScratchForMove(source, destination) || CanEmitDirectMemoryMove(destination, source, destinationValue, sourceValue))
                {
                    var direct = GenTreeLirFactory.Move(
                        _nextNodeId++,
                        blockId,
                        blockLinearNodes.Count,
                        destination,
                        source,
                        destinationValue,
                        sourceValue,
                        comment,
                        moveFlags);
                    AppendMoveNode(blockId, direct, blockLinearNodes, allNodes);
                    return;
                }

                var scratchClass = RegisterClassForMove(source, destination);
                var scratch = RegisterOperand.ForRegister(GetScratchRegisterForClass(scratchClass));
                var load = GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    blockLinearNodes.Count,
                    scratch,
                    source,
                    destinationValue: null,
                    sourceValue: sourceValue,
                    comment: comment + " reload",
                    moveFlags: moveFlags | MoveFlags.Reload);
                AppendMoveNode(blockId, load, blockLinearNodes, allNodes);

                var store = GenTreeLirFactory.Move(
                    _nextNodeId++,
                    blockId,
                    blockLinearNodes.Count,
                    destination,
                    scratch,
                    destinationValue,
                    sourceValue: null,
                    comment: comment + " store",
                    moveFlags: moveFlags | MoveFlags.Spill);
                AppendMoveNode(blockId, store, blockLinearNodes, allNodes);
            }

            private void EmitAggregateHomeStores(
                int blockId,
                GenTree value,
                int position,
                AbiValueInfo abi,
                RegisterValueLocation fragments,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (abi.PassingKind != AbiValuePassingKind.MultiRegister || !fragments.IsFragmented)
                    return;

                var aggregate = HomeForDefinition(value, position);
                if (aggregate.IsNone)
                    return;

                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    var destination = OperandAtOffset(aggregate, segments[i]);
                    var source = fragments[i];
                    if (source.IsNone || destination.IsNone || source.Equals(destination))
                        continue;

                    EmitMoveSequence(
                        blockId,
                        destination,
                        source,
                        value,
                        value,
                        $"{comment} fragment {i}",
                        blockLinearNodes,
                        allNodes);
                }
            }

            private void EmitFragmentReloadsFromAggregateHome(
                int blockId,
                GenTree value,
                int position,
                AbiValueInfo abi,
                RegisterValueLocation fragments,
                string comment,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (abi.PassingKind != AbiValuePassingKind.MultiRegister || !fragments.IsFragmented)
                    return;

                var aggregate = HomeForDefinition(value, position);
                if (aggregate.IsNone)
                    return;

                var segments = MachineAbi.GetRegisterSegments(abi);
                for (int i = 0; i < segments.Length; i++)
                {
                    var destination = fragments[i];
                    var source = OperandAtOffset(aggregate, segments[i]);
                    if (destination.IsNone || source.Equals(destination))
                        continue;

                    EmitMoveSequence(
                        blockId,
                        destination,
                        source,
                        value,
                        value,
                        $"{comment} fragment {i}",
                        blockLinearNodes,
                        allNodes);
                }
            }


            private bool CanEmitDirectMemoryMove(
                RegisterOperand destination,
                RegisterOperand source,
                GenTree? destinationValue,
                GenTree? sourceValue)
            {
                if (!destination.IsMemoryOperand || !source.IsMemoryOperand)
                    return false;

                if (!IsWideMemoryOperand(destination) && !IsWideMemoryOperand(source))
                    return false;

                if (destinationValue is not null && IsBlockCopyValue(destinationValue))
                    return true;
                if (sourceValue is not null && IsBlockCopyValue(sourceValue))
                    return true;
                return false;
            }

            private static bool IsWideMemoryOperand(RegisterOperand operand)
                => operand.IsMemoryOperand && operand.FrameSlotSize > MachineAbi.GeneralRegisterSlotSize;

            private bool IsBlockCopyValue(GenTree value)
            {
                var valueInfo = _method.GetValueInfo(value);
                return MachineAbi.IsBlockCopyValue(valueInfo.Type, valueInfo.StackKind);
            }

            private MachineRegister GetScratchRegisterForClass(RegisterClass registerClass)
            {
                if (registerClass is not (RegisterClass.General or RegisterClass.Float))
                    throw new InvalidOperationException($"Cannot select a scratch register for {registerClass} move.");

                var scratch = registerClass == RegisterClass.Float
                    ? _options.ParallelCopyFloatScratchRegister0
                    : _options.ParallelCopyScratchRegister0;

                ValidateReservedScratch(scratch, registerClass);
                return scratch;
            }

            private MachineRegister GetTreeScratchRegister(RegisterClass registerClass, int index)
            {
                var scratchPool = registerClass switch
                {
                    RegisterClass.General => MachineRegisters.TreeScratchGprs,
                    RegisterClass.Float => MachineRegisters.TreeScratchFprs,
                    _ => ImmutableArray<MachineRegister>.Empty,
                };

                var scratch = (uint)index < (uint)scratchPool.Length
                    ? scratchPool[index]
                    : MachineRegister.Invalid;

                if (scratch == MachineRegister.Invalid)
                    throw new InvalidOperationException($"Not enough reserved scratch registers to normalize a {registerClass} tree node.");

                ValidateReservedScratch(scratch, registerClass);
                return scratch;
            }

            private void ValidateReservedScratch(MachineRegister scratch, RegisterClass registerClass)
            {
                if (!MachineRegisters.IsRegisterInClass(scratch, registerClass))
                    throw new InvalidOperationException($"Invalid scratch register {scratch} for {registerClass} move.");
                if (IsAllocatable(scratch))
                    throw new InvalidOperationException($"Scratch register {MachineRegisters.Format(scratch)} must not be allocatable.");
            }

            private static RegisterClass RegisterClassForMove(RegisterOperand source, RegisterOperand destination)
            {
                if (destination.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return destination.RegisterClass;
                if (source.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return source.RegisterClass;

                throw new InvalidOperationException($"Invalid move without a concrete register class: {source} -> {destination}.");
            }

            private static bool RequiresScratchForMove(RegisterOperand source, RegisterOperand destination)
                => !source.IsNone && !destination.IsNone && !source.IsRegister && !destination.IsRegister;

            private void EmitTreeNodeSequence(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (false)
                    throw new InvalidOperationException("GenTree LIR tree node has no GenTree source.");

                if (IsAbiCall(node))
                {
                    EmitCallLikeTreeNode(node, () => _nextSpillSlot++, blockLinearNodes, allNodes);
                    return;
                }

                if (node.Kind == GenTreeKind.Return)
                {
                    EmitReturnTreeNode(node, blockLinearNodes, allNodes);
                    return;
                }

                if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                    EmitRegisterOnlyTreeNode(node, blockLinearNodes, allNodes);
                else
                {
                    EmitMemoryConstrainedTreeNode(node, blockLinearNodes, allNodes);

                    if (node.RegisterResult is not null)
                    {
                        int definitionPosition = GetNodePosition(node) + 1;
                        var resultValue = node.RegisterResult;
                        var resultInfo = _method.GetValueInfo(resultValue);
                        var abi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                        if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                        {
                            EmitFragmentReloadsFromAggregateHome(
                                node.LinearBlockId,
                                resultValue,
                                definitionPosition,
                                abi,
                                ValueLocationForDefinition(resultValue, definitionPosition, abi),
                                "tree multi-register result reload",
                                blockLinearNodes,
                                allNodes);
                        }
                    }
                }
            }

            private void EmitReturnTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (node.RegisterUses.Length == 0)
                {
                    var voidReturn = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        ImmutableArray<RegisterOperand>.Empty,
                        (GenTree?)null,
                        linearUses: ImmutableArray<GenTree>.Empty,
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(voidReturn);
                    allNodes.Add(voidReturn);
                    return;
                }

                if (node.RegisterUses.Length != 1)
                    throw new InvalidOperationException("Return node must have zero or one value use.");

                int position = GetNodePosition(node);
                var value = node.RegisterUses[0];
                var valueInfo = _method.GetValueInfo(value);
                var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
                var sourceLocation = ValueLocationForUse(value, position, abi);
                var sourceOperand = sourceLocation.IsScalar ? sourceLocation.Scalar : HomeForUse(value, position);

                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    var returnOperand = RegisterOperand.ForRegister(GetReturnRegister(abi.RegisterClass));

                    if (!sourceOperand.Equals(returnOperand))
                    {
                        EmitMoveSequence(
                            node.LinearBlockId,
                            returnOperand,
                            sourceOperand,
                            destinationValue: null,
                            sourceValue: value,
                            comment: "return value to ABI register",
                            blockLinearNodes,
                            allNodes);
                    }

                    var returnNode = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        ImmutableArray.Create(returnOperand),
                        (GenTree?)null,
                        linearUses: node.RegisterUses,
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(returnNode);
                    allNodes.Add(returnNode);
                    return;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var returnOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                    var returnUses = ImmutableArray.CreateBuilder<GenTree>();
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    int generalReturnIndex = 0;
                    int floatReturnIndex = 0;

                    for (int i = 0; i < segments.Length; i++)
                    {
                        var segment = segments[i];
                        var target = RegisterOperand.ForRegister(GetReturnRegister(segment.RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                        var source = sourceLocation.IsFragmented ? sourceLocation[i] : OperandAtOffset(sourceOperand, segment);

                        if (!source.Equals(target))
                        {
                            EmitMoveSequence(
                                node.LinearBlockId,
                                target,
                                source,
                                destinationValue: null,
                                sourceValue: value,
                                comment: "return struct fragment to ABI register",
                                blockLinearNodes,
                                allNodes);
                        }

                        returnOperands.Add(target);
                        returnUses.Add(value);
                    }

                    var returnNode = GenTreeLirFactory.Tree(
                        _nextNodeId++,
                        node.LinearBlockId,
                        blockLinearNodes.Count,
                        node,
                        RegisterOperand.None,
                        returnOperands.ToImmutable(),
                        (GenTree?)null,
                        linearUses: returnUses.ToImmutable(),
                        linearId: node.LinearId,
                        linearOperands: node.OperandFlags);
                    blockLinearNodes.Add(returnNode);
                    allNodes.Add(returnNode);
                    return;
                }

                var indirectReturnNode = GenTreeLirFactory.Tree(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    RegisterOperand.None,
                    ImmutableArray.Create(sourceOperand),
                    (GenTree?)null,
                    linearUses: node.RegisterUses,
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(indirectReturnNode);
                allNodes.Add(indirectReturnNode);
            }

            private void EmitRegisterOnlyTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var useOperands = ImmutableArray.CreateBuilder<RegisterOperand>(node.RegisterUses.Length);
                var expandedRegisterUses = ImmutableArray.CreateBuilder<GenTree>(node.RegisterUses.Length);
                var resultOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                var resultGenTrees = ImmutableArray.CreateBuilder<GenTree>();
                var postTreeStores = new List<RegisterResolvedMove>();
                var scratchUseCounts = new Dictionary<RegisterClass, int>();

                ReserveInternalScratchRegisters(node, scratchUseCounts);

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var value = node.RegisterUses[i];
                    var valueInfo = _method.GetValueInfo(value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var location = ValueLocationForUse(value, position, abi);
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        if (location.Count != segments.Length)
                            throw new InvalidOperationException($"Multi-register use location count does not match ABI segment count for {value}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var home = location[s];
                            if (home.IsRegister)
                            {
                                useOperands.Add(home);
                                expandedRegisterUses.Add(value);
                                continue;
                            }

                            int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                            var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                            EmitMoveSequence(
                                node.LinearBlockId,
                                scratch,
                                home,
                                destinationValue: null,
                                sourceValue: value,
                                "tree multi-register operand reload",
                                blockLinearNodes,
                                allNodes);
                            useOperands.Add(scratch);
                            expandedRegisterUses.Add(value);
                        }
                        continue;
                    }

                    var scalarHome = HomeForUse(value, position);
                    if (scalarHome.IsRegister)
                    {
                        useOperands.Add(scalarHome);
                        expandedRegisterUses.Add(value);
                        continue;
                    }

                    int scalarScratchIndex = NextScratchIndex(scratchUseCounts, scalarHome.RegisterClass);
                    var scalarScratch = RegisterOperand.ForRegister(GetTreeScratchRegister(scalarHome.RegisterClass, scalarScratchIndex));
                    EmitMoveSequence(
                        node.LinearBlockId,
                        scalarScratch,
                        scalarHome,
                        destinationValue: null,
                        sourceValue: value,
                        "tree operand reload",
                        blockLinearNodes,
                        allNodes);
                    useOperands.Add(scalarScratch);
                    expandedRegisterUses.Add(value);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);

                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var finalLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        if (finalLocation.Count == 0 && RequiresCodegenResultOperand(node, resultAbi))
                        {
                            for (int s = 0; s < segments.Length; s++)
                            {
                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                resultOperands.Add(scratch);
                                resultGenTrees.Add(resultValue);
                            }
                        }
                        else
                        {
                            if (finalLocation.Count != segments.Length)
                                throw new InvalidOperationException(
                                    $"Multi-register result location count does not match ABI segment count for {resultValue}.");

                            for (int s = 0; s < segments.Length; s++)
                            {
                                var finalFragment = finalLocation[s];
                                if (finalFragment.IsRegister)
                                {
                                    resultOperands.Add(finalFragment);
                                    resultGenTrees.Add(resultValue);
                                    continue;
                                }

                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                resultOperands.Add(scratch);
                                resultGenTrees.Add(resultValue);
                                if (!finalFragment.IsNone)
                                    postTreeStores.Add(new RegisterResolvedMove(scratch, finalFragment, resultValue, resultValue));
                            }
                        }
                    }
                    else
                    {
                        RegisterOperand finalResult = HomeForDefinition(resultValue, definitionPosition);
                        RegisterOperand nodeResult;
                        if (finalResult.IsRegister)
                        {
                            nodeResult = finalResult;
                        }
                        else if (finalResult.IsNone)
                        {
                            if (RequiresCodegenResultOperand(node, resultAbi))
                            {
                                var resultClass = RegisterClassForReload(resultInfo, resultAbi, finalResult);
                                int scratchIndex = NextScratchIndex(scratchUseCounts, resultClass);
                                nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(resultClass, scratchIndex));
                            }
                            else
                            {
                                nodeResult = RegisterOperand.None;
                            }
                        }
                        else
                        {
                            int scratchIndex = NextScratchIndex(scratchUseCounts, finalResult.RegisterClass);
                            nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(finalResult.RegisterClass, scratchIndex));
                            postTreeStores.Add(new RegisterResolvedMove(nodeResult, finalResult, resultValue, resultValue));
                        }

                        if (!nodeResult.IsNone)
                        {
                            resultOperands.Add(nodeResult);
                            resultGenTrees.Add(resultValue);
                        }
                    }
                }

                var treeNode = GenTreeLirFactory.TreeMulti(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    resultOperands.ToImmutable(),
                    useOperands.ToImmutable(),
                    resultGenTrees.ToImmutable(),
                    expandedRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(treeNode);
                allNodes.Add(treeNode);

                for (int i = 0; i < postTreeStores.Count; i++)
                {
                    var store = postTreeStores[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        store.Destination,
                        store.Source,
                        store.DestinationValue,
                        sourceValue: store.SourceValue,
                        "tree result spill",
                        blockLinearNodes,
                        allNodes);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        EmitAggregateHomeStores(
                            node.LinearBlockId,
                            resultValue,
                            definitionPosition,
                            resultAbi,
                            ValueLocationForDefinition(resultValue, definitionPosition, resultAbi),
                            "tree multi-register result aggregate home",
                            blockLinearNodes,
                            allNodes);
                    }
                }
            }

            private void EmitCallLikeTreeNode(
                GenTree node,
                Func<int> getCopyScratchSlot,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (node.Kind == GenTreeKind.NewObject && node.Method?.DeclaringType.IsValueType == true)
                {
                    throw new InvalidOperationException(
                        "Value-type newobj must be lowered into a struct materialization temp and a void constructor call before register allocation.");
                }

                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var targets = ImmutableArray.CreateBuilder<RegisterOperand>();
                var callRegisterUses = ImmutableArray.CreateBuilder<GenTree>();
                var callUseRoles = ImmutableArray.CreateBuilder<OperandRole>();
                var registerMoves = new List<RegisterResolvedMove>();
                var stackMoves = new List<RegisterResolvedMove>();
                bool referenceTypeNewObject = node.Kind == GenTreeKind.NewObject;
                var descriptor = MachineAbi.BuildCallDescriptor(node.RegisterUses, _method.GetValueInfo, node.RegisterResult, node.Method, referenceTypeNewObject);

                RegisterOperand finalResult = RegisterOperand.None;
                RegisterOperand callResult = RegisterOperand.None;
                GenTree? nodeResultValue = null;
                AbiValueInfo resultAbi = descriptor.ReturnAbi;
                GenTree? resultValueOpt = null;

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    resultValueOpt = resultValue;
                    finalResult = HomeForDefinition(resultValue, definitionPosition);
                    if (finalResult.IsNone &&
                        (resultAbi.PassingKind == AbiValuePassingKind.Indirect || referenceTypeNewObject))
                    {
                        int homeSize = referenceTypeNewObject
                            ? TargetArchitecture.PointerSize
                            : Math.Max(MachineAbi.StackArgumentSlotSize, Math.Max(1, resultAbi.Size));
                        finalResult = RegisterOperand.ForSpillSlot(
                            RegisterClass.General,
                            _nextSpillSlot++,
                            0,
                            homeSize);
                    }

                    if (!finalResult.IsNone)
                    {
                        if (referenceTypeNewObject)
                        {
                            callResult = finalResult.IsFrameSlot
                                ? finalResult
                                : RegisterOperand.ForSpillSlot(RegisterClass.General, _nextSpillSlot++, 0, TargetArchitecture.PointerSize);
                            nodeResultValue = resultValue;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.ScalarRegister)
                        {
                            callResult = RegisterOperand.ForRegister(GetReturnRegister(resultAbi.RegisterClass));
                            nodeResultValue = resultValue;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                        {
                            callResult = RegisterOperand.None;
                            nodeResultValue = null;
                        }
                        else if (resultAbi.PassingKind == AbiValuePassingKind.Indirect)
                        {
                            callResult = RegisterOperand.None;
                            nodeResultValue = null;
                        }
                        else
                        {
                            callResult = finalResult;
                            nodeResultValue = resultValue;
                        }
                    }
                }

                for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
                {
                    var segment = descriptor.ArgumentSegments[i];
                    var target = GetCallArgumentOperand(segment.Location);
                    RegisterOperand source;
                    MoveFlags moveFlags = MoveFlags.AbiArgument;
                    OperandRole role;
                    GenTree? moveSourceValue;
                    GenTree? moveDestinationValue;

                    if (segment.IsHiddenReturnBuffer)
                    {
                        if (node.RegisterResult is null || finalResult.IsNone)
                            throw new InvalidOperationException("ABI call result has no addressable hidden return buffer home.");

                        source = finalResult.AsAddress();
                        role = OperandRole.HiddenReturnBuffer;
                        moveFlags |= MoveFlags.HiddenReturnBuffer;
                        moveSourceValue = null;
                        moveDestinationValue = null;
                    }
                    else
                    {
                        var sourceLocation = ValueLocationForUse(segment.Value, position, segment.ValueAbi);
                        source = GetSourceOperandForCallSegment(sourceLocation, segment);
                        role = OperandRole.Normal;
                        moveSourceValue = segment.Value;
                        moveDestinationValue = segment.Value;
                    }

                    targets.Add(target);
                    callRegisterUses.Add(segment.Value);
                    callUseRoles.Add(role);

                    if (source.Equals(target))
                        continue;

                    var move = new RegisterResolvedMove(
                        source,
                        target,
                        moveSourceValue,
                        moveDestinationValue,
                        moveFlags);
                    if (target.IsOutgoingArgumentSlot)
                        stackMoves.Add(move);
                    else
                        registerMoves.Add(move);
                }

                for (int i = 0; i < stackMoves.Count; i++)
                {
                    var move = stackMoves[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        move.Destination,
                        move.Source,
                        move.DestinationValue,
                        move.SourceValue,
                        "call stack argument home",
                        blockLinearNodes,
                        allNodes);
                }

                int scratchSlot = -1;
                int GetScratchSlot()
                {
                    if (scratchSlot < 0)
                        scratchSlot = getCopyScratchSlot();
                    return scratchSlot;
                }

                if (registerMoves.Count != 0)
                {
                    var setup = RegisterParallelCopyResolver.Resolve(
                        node.LinearBlockId,
                        node.LinearBlockId,
                        registerMoves,
                        GetScratchSlot,
                        _options.ParallelCopyScratchRegister0,
                        _options.ParallelCopyFloatScratchRegister0,
                        ref _nextNodeId,
                        CanEmitDirectMemoryMove);

                    for (int i = 0; i < setup.Length; i++)
                        AppendMoveNode(node.LinearBlockId, setup[i], blockLinearNodes, allNodes);
                }

                var callNode = GenTreeLirFactory.Tree(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    callResult,
                    targets.ToImmutable(),
                    nodeResultValue,
                    callRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    useRoles: callUseRoles.ToImmutable(),
                    linearOperands: node.OperandFlags);
                blockLinearNodes.Add(callNode);
                allNodes.Add(callNode);

                if (resultValueOpt is not null && !finalResult.IsNone)
                {
                    var resultValue = resultValueOpt;
                    if (resultAbi.PassingKind == AbiValuePassingKind.ScalarRegister && !finalResult.Equals(callResult))
                    {
                        EmitMoveSequence(
                            node.LinearBlockId,
                            finalResult,
                            callResult,
                            resultValue,
                            sourceValue: null,
                            "call result from ABI register",
                            blockLinearNodes,
                            allNodes);
                    }
                    else if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        var destinationLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        int generalReturnIndex = 0;
                        int floatReturnIndex = 0;
                        for (int i = 0; i < segments.Length; i++)
                        {
                            var segment = segments[i];
                            var source = RegisterOperand.ForRegister(GetReturnRegister(segment.RegisterClass, ref generalReturnIndex, ref floatReturnIndex));
                            var destination = destinationLocation.IsFragmented ? destinationLocation[i] : OperandAtOffset(finalResult, segment);
                            EmitMoveSequence(
                                node.LinearBlockId,
                                destination,
                                source,
                                destinationValue: resultValue,
                                sourceValue: null,
                                comment: "call struct result from ABI register",
                                blockLinearNodes,
                                allNodes);
                        }

                        EmitAggregateHomeStores(
                            node.LinearBlockId,
                            resultValue,
                            definitionPosition,
                            resultAbi,
                            destinationLocation,
                            "call struct result aggregate home",
                            blockLinearNodes,
                            allNodes);
                    }
                }
            }

            private static RegisterOperand GetCallArgumentOperand(AbiArgumentLocation location)
            {
                if (location.IsRegister)
                    return RegisterOperand.ForRegister(location.Register);

                return RegisterOperand.ForOutgoingArgumentSlot(
                    location.RegisterClass,
                    location.StackSlotIndex,
                    location.StackOffset,
                    location.Size);
            }

            private static RegisterOperand GetSourceOperandForCallSegment(RegisterValueLocation sourceLocation, AbiCallSegment segment)
            {
                if (segment.IsAbiSegment)
                    return sourceLocation.IsFragmented
                        ? sourceLocation[segment.SegmentIndex]
                        : OperandAtOffset(sourceLocation.Scalar, segment.ToRegisterSegment());

                if (sourceLocation.IsScalar)
                    return sourceLocation.Scalar;

                if (sourceLocation.Count == 1)
                    return sourceLocation[0];

                throw new InvalidOperationException($"Non-fragment ABI argument cannot be read from fragmented source location: {sourceLocation}.");
            }

            private static MachineRegister GetReturnRegister(RegisterClass registerClass, ref int generalIndex, ref int floatIndex)
            {
                int index;
                if (registerClass == RegisterClass.Float)
                {
                    index = floatIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.FloatReturnValue0,
                        1 => MachineRegisters.FloatReturnValue1,
                        _ => throw new InvalidOperationException("Not enough float return registers for multi-register return."),
                    };
                }

                if (registerClass == RegisterClass.General)
                {
                    index = generalIndex++;
                    return index switch
                    {
                        0 => MachineRegisters.ReturnValue0,
                        1 => MachineRegisters.ReturnValue1,
                        _ => throw new InvalidOperationException("Not enough integer return registers for multi-register return."),
                    };
                }

                throw new InvalidOperationException("Invalid return register class " + registerClass + ".");
            }

            private static RegisterOperand OperandAtOffset(RegisterOperand operand, AbiRegisterSegment segment)
            {
                if (operand.IsRegister)
                {
                    if (segment.Offset != 0)
                        throw new InvalidOperationException($"Cannot address a non-zero struct fragment inside a scalar register operand: {operand}.");
                    if (!MachineRegisters.IsRegisterInClass(operand.Register, segment.RegisterClass))
                        throw new InvalidOperationException($"Register operand class does not match ABI segment class: {operand} vs {segment}.");
                    return operand;
                }

                int offset = checked(operand.FrameOffset + segment.Offset);
                int size = segment.Size;

                if (operand.IsSpillSlot)
                    return RegisterOperand.ForSpillSlot(segment.RegisterClass, operand.SpillSlot, offset, size);
                if (operand.IsIncomingArgumentSlot)
                    return RegisterOperand.ForIncomingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsLocalSlot)
                    return RegisterOperand.ForLocalSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsTempSlot)
                    return RegisterOperand.ForTempSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsOutgoingArgumentSlot)
                    return RegisterOperand.ForOutgoingArgumentSlot(segment.RegisterClass, operand.FrameSlotIndex, offset, size);
                if (operand.IsFrameSlot)
                    return RegisterOperand.ForFrameSlot(segment.RegisterClass, operand.FrameSlotKind, operand.FrameBase, operand.FrameSlotIndex, offset, size, operand.IsAddress);

                throw new InvalidOperationException($"Cannot address an ABI fragment inside operand: {operand}.");
            }

            private static void ReserveInternalScratchRegisters(GenTree node, Dictionary<RegisterClass, int> scratchUseCounts)
            {
                if (node.LinearLowering.InternalGeneralRegisters != 0)
                    scratchUseCounts[RegisterClass.General] = node.LinearLowering.InternalGeneralRegisters;

                if (node.LinearLowering.InternalFloatRegisters != 0)
                    scratchUseCounts[RegisterClass.Float] = node.LinearLowering.InternalFloatRegisters;
            }

            private static int NextScratchIndex(Dictionary<RegisterClass, int> scratchUseCounts, RegisterClass registerClass)
            {
                scratchUseCounts.TryGetValue(registerClass, out int index);
                scratchUseCounts[registerClass] = index + 1;
                return index;
            }

            private void EmitMemoryConstrainedTreeNode(
                GenTree node,
                ImmutableArray<GenTree>.Builder blockLinearNodes,
                ImmutableArray<GenTree>.Builder allNodes)
            {
                if (false)
                    throw new InvalidOperationException("GenTree LIR tree node has no GenTree source.");

                int position = GetNodePosition(node);
                int definitionPosition = position + 1;
                var useOperands = ImmutableArray.CreateBuilder<RegisterOperand>(node.RegisterUses.Length);
                var expandedRegisterUses = ImmutableArray.CreateBuilder<GenTree>(node.RegisterUses.Length);
                var resultOperands = ImmutableArray.CreateBuilder<RegisterOperand>();
                var resultGenTrees = ImmutableArray.CreateBuilder<GenTree>();
                var postTreeStores = new List<RegisterResolvedMove>();
                var scratchUseCounts = new Dictionary<RegisterClass, int>();

                ReserveInternalScratchRegisters(node, scratchUseCounts);

                for (int i = 0; i < node.RegisterUses.Length; i++)
                {
                    var value = node.RegisterUses[i];
                    var valueInfo = _method.GetValueInfo(value);
                    var abi = MachineAbi.ClassifyStorageValue(valueInfo.Type, valueInfo.StackKind);
                    int operandIndex = GetOperandIndexForRegisterUse(node, i);
                    var operandFlags = GetOperandFlagsForRegisterUse(node, i);
                    bool requiresRegister = RequiresCodegenRegisterUse(node, operandIndex, abi, operandFlags);

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        if (!requiresRegister && CanPassMemoryValueUseByAggregateHome(node, operandIndex, value, position, out var aggregateHome))
                        {
                            useOperands.Add(aggregateHome);
                            expandedRegisterUses.Add(value);
                            continue;
                        }

                        var location = ValueLocationForUse(value, position, abi);
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        if (location.Count != segments.Length)
                            throw new InvalidOperationException($"Multi-register use location count does not match ABI segment count for {value}.");

                        for (int s = 0; s < segments.Length; s++)
                        {
                            var home = location[s];
                            if (requiresRegister && !home.IsRegister)
                            {
                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                EmitMoveSequence(
                                    node.LinearBlockId,
                                    scratch,
                                    home,
                                    destinationValue: null,
                                    sourceValue: value,
                                    "tree memory-operand fragment reload",
                                    blockLinearNodes,
                                    allNodes);
                                home = scratch;
                            }

                            useOperands.Add(home);
                            expandedRegisterUses.Add(value);
                        }
                        continue;
                    }

                    var scalarHome = HomeForUse(value, position);
                    if (requiresRegister && !scalarHome.IsRegister)
                    {
                        var reloadClass = RegisterClassForReload(valueInfo, abi, scalarHome);
                        int scratchIndex = NextScratchIndex(scratchUseCounts, reloadClass);
                        var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(reloadClass, scratchIndex));
                        EmitMoveSequence(
                            node.LinearBlockId,
                            scratch,
                            scalarHome,
                            destinationValue: null,
                            sourceValue: value,
                            "tree memory-operand reload",
                            blockLinearNodes,
                            allNodes);
                        scalarHome = scratch;
                    }

                    useOperands.Add(scalarHome);
                    expandedRegisterUses.Add(value);
                }

                if (node.RegisterResult is not null)
                {
                    var resultValue = node.RegisterResult;
                    var resultInfo = _method.GetValueInfo(resultValue);
                    var resultAbi = MachineAbi.ClassifyStorageValue(resultInfo.Type, resultInfo.StackKind);
                    if (resultAbi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var finalLocation = ValueLocationForDefinition(resultValue, definitionPosition, resultAbi);
                        var segments = MachineAbi.GetRegisterSegments(resultAbi);
                        if (finalLocation.Count == 0 && RequiresCodegenResultOperand(node, resultAbi))
                        {
                            for (int s = 0; s < segments.Length; s++)
                            {
                                int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                var scratch = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                resultOperands.Add(scratch);
                                resultGenTrees.Add(resultValue);
                            }
                        }
                        else
                        {
                            if (finalLocation.Count != segments.Length)
                                throw new InvalidOperationException($"Multi-register result location count does not match ABI segment count for {resultValue}.");

                            for (int s = 0; s < segments.Length; s++)
                            {
                                var finalFragment = finalLocation[s];
                                RegisterOperand nodeFragment = finalFragment;
                                if (finalFragment.IsNone)
                                {
                                    if (RequiresCodegenResultOperand(node, resultAbi))
                                    {
                                        int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                        nodeFragment = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                    }
                                }
                                else if (RequiresCodegenRegisterDefinition(node, resultAbi) && !finalFragment.IsRegister)
                                {
                                    int scratchIndex = NextScratchIndex(scratchUseCounts, segments[s].RegisterClass);
                                    nodeFragment = RegisterOperand.ForRegister(GetTreeScratchRegister(segments[s].RegisterClass, scratchIndex));
                                    postTreeStores.Add(new RegisterResolvedMove(nodeFragment, finalFragment, resultValue, resultValue));
                                }

                                if (!nodeFragment.IsNone)
                                {
                                    resultOperands.Add(nodeFragment);
                                    resultGenTrees.Add(resultValue);
                                }
                            }
                        }
                    }
                    else
                    {
                        var finalResult = HomeForDefinition(resultValue, definitionPosition);
                        RegisterOperand nodeResult = finalResult;
                        if (finalResult.IsNone)
                        {
                            if (RequiresCodegenResultOperand(node, resultAbi))
                            {
                                var resultClass = RegisterClassForReload(resultInfo, resultAbi, finalResult);
                                int scratchIndex = NextScratchIndex(scratchUseCounts, resultClass);
                                nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(resultClass, scratchIndex));
                            }
                        }
                        else if (RequiresCodegenRegisterDefinition(node, resultAbi) && !finalResult.IsRegister)
                        {
                            var resultClass = RegisterClassForReload(resultInfo, resultAbi, finalResult);
                            int scratchIndex = NextScratchIndex(scratchUseCounts, resultClass);
                            nodeResult = RegisterOperand.ForRegister(GetTreeScratchRegister(resultClass, scratchIndex));
                            postTreeStores.Add(new RegisterResolvedMove(nodeResult, finalResult, resultValue, resultValue));
                        }

                        if (!nodeResult.IsNone)
                        {
                            resultOperands.Add(nodeResult);
                            resultGenTrees.Add(resultValue);
                        }
                    }
                }

                var treeNode = GenTreeLirFactory.TreeMulti(
                    _nextNodeId++,
                    node.LinearBlockId,
                    blockLinearNodes.Count,
                    node,
                    resultOperands.ToImmutable(),
                    useOperands.ToImmutable(),
                    resultGenTrees.ToImmutable(),
                    expandedRegisterUses.ToImmutable(),
                    linearId: node.LinearId,
                    linearOperands: node.OperandFlags);

                blockLinearNodes.Add(treeNode);
                allNodes.Add(treeNode);

                for (int i = 0; i < postTreeStores.Count; i++)
                {
                    var store = postTreeStores[i];
                    EmitMoveSequence(
                        node.LinearBlockId,
                        store.Destination,
                        store.Source,
                        store.DestinationValue,
                        sourceValue: store.SourceValue,
                        "tree memory-node result spill",
                        blockLinearNodes,
                        allNodes);
                }

            }

            private bool CanPassMemoryValueUseByAggregateHome(
                GenTree node,
                int operandIndex,
                GenTree value,
                int position,
                out RegisterOperand aggregateHome)
            {
                aggregateHome = RegisterOperand.None;

                if (!node.LinearMemoryAccess.HasValueOperand(operandIndex))
                    return false;

                if (!node.LinearMemoryAccess.IsBlockCopy)
                    return false;

                var home = HomeForUse(value, position);
                if (!home.IsFrameSlot)
                    return false;

                aggregateHome = home;
                return true;
            }

            private static LirOperandFlags GetOperandFlagsForRegisterUse(GenTree node, int registerUseIndex)
            {
                if (node.OperandFlags.IsDefaultOrEmpty || node.Operands.IsDefaultOrEmpty)
                    return LirOperandFlags.None;

                int seenRegisterUses = 0;
                int limit = Math.Min(node.Operands.Length, node.OperandFlags.Length);
                for (int i = 0; i < limit; i++)
                {
                    var flags = node.OperandFlags[i];
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    if (seenRegisterUses == registerUseIndex)
                        return flags;

                    seenRegisterUses++;
                }

                return LirOperandFlags.None;
            }

            private static bool RequiresCodegenRegisterUse(
                GenTree node,
                int operandIndex,
                AbiValueInfo abi,
                LirOperandFlags operandFlags)
            {
                if ((operandFlags & LirOperandFlags.RegOptional) != 0)
                    return false;

                if (node.HasLoweringFlag(GenTreeLinearFlags.RequiresRegisterOperands))
                    return true;

                var memory = node.LinearMemoryAccess;
                if (!memory.IsNone)
                {
                    if (memory.HasAddressOperand(operandIndex))
                        return true;

                    if (memory.HasValueOperand(operandIndex))
                    {
                        if (memory.IsBlockCopy)
                            return false;

                        return abi.PassingKind == AbiValuePassingKind.ScalarRegister;
                    }
                }

                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister)
                    return false;

                return true;
            }

            private static bool RequiresCodegenResultOperand(GenTree node, AbiValueInfo abi)
            {
                if (node.RegisterResult is null)
                    return false;

                if (abi.PassingKind == AbiValuePassingKind.Void)
                    return false;

                return node.Kind switch
                {
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.StoreField => false,
                    GenTreeKind.StoreStaticField => false,
                    GenTreeKind.StoreIndirect => false,
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


            private static bool RequiresCodegenRegisterDefinition(GenTree node, AbiValueInfo abi)
            {
                if (abi.PassingKind is AbiValuePassingKind.Stack or AbiValuePassingKind.Indirect or AbiValuePassingKind.MultiRegister)
                    return false;

                if (node.RegisterResult is not null && node.RegisterResult.LinearValueKey.IsSsaValue)
                    return true;

                return node.Kind switch
                {
                    GenTreeKind.Local => false,
                    GenTreeKind.Arg => false,
                    GenTreeKind.Temp => false,
                    GenTreeKind.TempAddr => false,
                    GenTreeKind.StoreLocal => false,
                    GenTreeKind.StoreArg => false,
                    GenTreeKind.StoreTemp => false,
                    GenTreeKind.DefaultValue => false,
                    _ => true,
                };
            }

            private static RegisterClass RegisterClassForReload(GenTreeValueInfo info, AbiValueInfo abi, RegisterOperand home)
            {
                if (home.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return home.RegisterClass;

                if (info.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return info.RegisterClass;

                if (abi.RegisterClass is RegisterClass.General or RegisterClass.Float)
                    return abi.RegisterClass;

                return RegisterClass.General;
            }

            private static int GetOperandIndexForRegisterUse(GenTree node, int registerUseIndex)
            {
                if (node.Operands.IsDefaultOrEmpty)
                    return registerUseIndex;

                int seenRegisterUses = 0;
                int flagCount = node.OperandFlags.IsDefaultOrEmpty ? 0 : node.OperandFlags.Length;
                for (int i = 0; i < node.Operands.Length; i++)
                {
                    var flags = i < flagCount ? node.OperandFlags[i] : LirOperandFlags.None;
                    if ((flags & LirOperandFlags.Contained) != 0)
                        continue;

                    if (seenRegisterUses == registerUseIndex)
                        return i;

                    seenRegisterUses++;
                }

                return registerUseIndex;
            }

            private int GetNodePosition(GenTree node)
            {
                if (!_nodePositions.TryGetValue(node.LinearId, out int position))
                    throw new InvalidOperationException($"Missing GenTree LIR position for node {node.LinearId}.");
                return position;
            }

            private RegisterOperand HomeForUse(GenTree value, int position)
            {
                var home = HomeOrNone(value, position);
                if (home.IsNone)
                    throw new InvalidOperationException($"GenTree value {value} is used but has no assigned register or spill slot at position {position}.");
                return home;
            }

            private RegisterValueLocation ValueLocationForUse(GenTree value, int position, AbiValueInfo abi)
            {
                var location = ValueLocationOrNone(value, position, abi);
                if (location.IsEmpty)
                    throw new InvalidOperationException($"GenTree value {value} is used but has no assigned location at position {position}.");

                if (location.IsFragmented)
                {
                    for (int i = 0; i < location.Count; i++)
                    {
                        if (location[i].IsNone)
                            throw new InvalidOperationException($"GenTree value {value} ABI fragment {i} is used but has no assigned location at position {position}.");
                    }
                }

                return location;
            }

            private RegisterOperand HomeForDefinition(GenTree value)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.LocationAtDefinition();
            }

            private RegisterOperand HomeForDefinition(GenTree value, int position)
                => HomeOrNone(value, position);

            private RegisterValueLocation ValueLocationForDefinition(GenTree value, int position, AbiValueInfo abi)
                => ValueLocationOrNone(value, position, abi);

            private RegisterOperand HomeOrNone(GenTree value, int position)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.LocationAt(position);
            }

            private RegisterValueLocation ValueLocationOrNone(GenTree value, int position, AbiValueInfo abi)
            {
                if (!_allocations.TryGetValue(value, out var allocation))
                    throw new InvalidOperationException($"Missing allocation for {value}.");
                return allocation.ValueLocationAt(position, abi);
            }

            private readonly struct AllocationPreferenceKey : IEquatable<AllocationPreferenceKey>
            {
                public readonly GenTree Value;
                public readonly int AbiSegmentIndex;

                public AllocationPreferenceKey(GenTree value, int abiSegmentIndex)
                {
                    Value = value;
                    AbiSegmentIndex = abiSegmentIndex;
                }

                public bool Equals(AllocationPreferenceKey other)
                    => Value.Equals(other.Value) && AbiSegmentIndex == other.AbiSegmentIndex;

                public override bool Equals(object? obj)
                    => obj is AllocationPreferenceKey other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return (Value.GetHashCode() * 397) ^ AbiSegmentIndex;
                    }
                }
            }

            private static Dictionary<AllocationPreferenceKey, List<MachineRegister>> BuildRegisterPreferences(GenTreeMethod method)
            {
                var result = new Dictionary<AllocationPreferenceKey, List<MachineRegister>>();

                for (int i = 0; i < method.RefPositions.Length; i++)
                {
                    var rp = method.RefPositions[i];
                    if (rp.Value is null || rp.FixedRegister == MachineRegister.Invalid)
                        continue;
                    if (rp.Kind is not (LinearRefPositionKind.Use or LinearRefPositionKind.Def))
                        continue;

                    AddRegisterPreference(result, rp.Value, rp.IsAbiSegment ? rp.AbiSegmentIndex : -1, rp.FixedRegister);
                }

                for (int i = 0; i < method.Values.Length; i++)
                {
                    var info = method.Values[i];
                    if (!IsInitialSsaArgumentValue(info))
                        continue;

                    if ((uint)info.Value.SsaSlot.Index < (uint)method.ArgTypes.Length)
                        AddIncomingArgumentRegisterPreferences(result, method, info, info.Value.SsaSlot.Index);
                }

                foreach (var node in method.LinearNodes)
                {
                    if (IsAbiCall(node))
                    {
                        var descriptor = MachineAbi.BuildCallDescriptor(node.RegisterUses, method.GetValueInfo, node.RegisterResult, node.Method, node.Kind == GenTreeKind.NewObject);
                        for (int i = 0; i < descriptor.ArgumentSegments.Length; i++)
                        {
                            var segment = descriptor.ArgumentSegments[i];
                            if (segment.IsHiddenReturnBuffer || !segment.IsRegister)
                                continue;

                            AddRegisterPreference(
                                result,
                                segment.Value,
                                segment.IsAbiSegment ? segment.SegmentIndex : -1,
                                segment.Location.Register);
                        }

                        if (node.RegisterResult is not null)
                            AddReturnRegisterPreferences(result, node.RegisterResult, descriptor.ReturnAbi);

                        continue;
                    }

                    if (node.Kind == GenTreeKind.Return && node.RegisterUses.Length == 1)
                    {
                        var value = node.RegisterUses[0];
                        var valueInfo = method.GetValueInfo(value);
                        var abi = MachineAbi.ClassifyValue(valueInfo.Type, valueInfo.StackKind, isReturn: true);
                        AddReturnRegisterPreferences(result, value, abi);
                    }
                }

                return result;
            }

            private static bool IsInitialSsaArgumentValue(GenTreeValueInfo info)
                => info.Value.IsSsaValue &&
                   info.Value.SsaSlot.Kind == SsaSlotKind.Arg &&
                   info.DefinitionBlockId < 0 &&
                   info.DefinitionNodeId < 0;

            private static void AddIncomingArgumentRegisterPreferences(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> result,
                GenTreeMethod method,
                GenTreeValueInfo info,
                int argumentIndex)
            {
                int general = 0;
                int floating = 0;
                int hiddenReturnBufferInsertionIndex = MachineAbi.HiddenReturnBufferInsertionIndex(
                    method.RuntimeMethod,
                    method.ArgTypes.Length);

                for (int i = 0; i <= argumentIndex; i++)
                {
                    if (hiddenReturnBufferInsertionIndex == i)
                        _ = GetMaybeArgumentRegister(RegisterClass.General, ref general, ref floating);

                    RuntimeType currentType = method.ArgTypes[i];
                    GenStackKind currentStackKind = i == argumentIndex ? info.StackKind : MachineAbi.StackKindForType(currentType);
                    var abi = i == argumentIndex
                        ? MachineAbi.ClassifyValue(info.Type, currentStackKind, isReturn: false)
                        : MachineAbi.ClassifyValue(currentType, currentStackKind, isReturn: false);

                    if (abi.PassingKind == AbiValuePassingKind.Void)
                        continue;

                    if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                    {
                        var register = GetMaybeArgumentRegister(abi.RegisterClass, ref general, ref floating);
                        if (i == argumentIndex && register != MachineRegister.Invalid)
                            AddRegisterPreference(result, info.RepresentativeNode, -1, register);
                        continue;
                    }

                    if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                    {
                        var segments = MachineAbi.GetRegisterSegments(abi);
                        for (int s = 0; s < segments.Length; s++)
                        {
                            var register = GetMaybeArgumentRegister(segments[s].RegisterClass, ref general, ref floating);
                            if (i == argumentIndex && register != MachineRegister.Invalid)
                                AddRegisterPreference(result, info.RepresentativeNode, s, register);
                        }
                    }
                }
            }

            private static void AddReturnRegisterPreferences(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> result,
                GenTree value,
                AbiValueInfo abi)
            {
                if (abi.PassingKind == AbiValuePassingKind.ScalarRegister)
                {
                    AddRegisterPreference(result, value, -1, GetReturnRegister(abi.RegisterClass));
                    return;
                }

                if (abi.PassingKind == AbiValuePassingKind.MultiRegister)
                {
                    var segments = MachineAbi.GetRegisterSegments(abi);
                    int general = 0;
                    int floating = 0;
                    for (int s = 0; s < segments.Length; s++)
                        AddRegisterPreference(result, value, s, GetReturnRegister(segments[s].RegisterClass, ref general, ref floating));
                }
            }

            private static MachineRegister GetReturnRegister(RegisterClass registerClass)
            {
                return registerClass switch
                {
                    RegisterClass.Float => MachineRegisters.FloatReturnValue0,
                    RegisterClass.General => MachineRegisters.ReturnValue0,
                    _ => MachineRegister.Invalid,
                };
            }

            private static void AddRegisterPreference(
                Dictionary<AllocationPreferenceKey, List<MachineRegister>> map,
                GenTree value,
                int abiSegmentIndex,
                MachineRegister register)
            {
                if (register == MachineRegister.Invalid)
                    return;

                var key = new AllocationPreferenceKey(value, abiSegmentIndex);
                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<MachineRegister>();
                    map.Add(key, list);
                }

                if (!list.Contains(register))
                    list.Add(register);
            }

            private static Dictionary<GenTree, List<GenTree>> BuildPhiPreferences(GenTreeMethod method)
            {
                var result = new Dictionary<GenTree, List<GenTree>>();

                foreach (var node in method.LinearNodes)
                {
                    if (!node.IsPhiCopy || node.RegisterResult is null || node.RegisterUses.Length != 1)
                        continue;

                    AddClassCompatiblePreference(method, result, node.RegisterResult, node.RegisterUses[0]);
                }

                return result;
            }

            private static Dictionary<GenTree, List<GenTree>> BuildPreferences(GenTreeMethod method)
            {
                var result = new Dictionary<GenTree, List<GenTree>>();

                foreach (var node in method.LinearNodes)
                {
                    if (node.RegisterResult is null || node.RegisterUses.Length != 1)
                        continue;

                    if (node.LinearKind == GenTreeLinearKind.Copy || IsPromotedStoreDef(node) || IsPromotedLoadUse(node))
                        AddClassCompatiblePreference(method, result, node.RegisterResult, node.RegisterUses[0]);
                }

                return result;
            }

            private static bool IsPromotedStoreDef(GenTree node)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree || node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                return node.Kind is GenTreeKind.StoreArg or GenTreeKind.StoreLocal or GenTreeKind.StoreTemp;
            }

            private static bool IsPromotedLoadUse(GenTree node)
            {
                if (node.LinearKind != GenTreeLinearKind.Tree || node.RegisterResult is null || node.RegisterUses.Length != 1)
                    return false;

                return node.Kind is GenTreeKind.Arg or GenTreeKind.Local or GenTreeKind.Temp;
            }

            private static void AddClassCompatiblePreference(
                GenTreeMethod method,
                Dictionary<GenTree, List<GenTree>> map,
                GenTree left,
                GenTree right)
            {
                var leftClass = method.GetValueInfo(left).RegisterClass;
                var rightClass = method.GetValueInfo(right).RegisterClass;
                if (leftClass != rightClass)
                    return;

                AddPreference(map, left, right);
                AddPreference(map, right, left);
            }

            private static void AddPreference(Dictionary<GenTree, List<GenTree>> map, GenTree value, GenTree preferred)
            {
                if (!map.TryGetValue(value, out var list))
                {
                    list = new List<GenTree>();
                    map.Add(value, list);
                }

                if (!list.Contains(preferred))
                    list.Add(preferred);
            }
        }

        private sealed class AllocationInterval
        {
            private sealed class AllocationSegment
            {
                public int Start;
                public int End;
                public RegisterOperand Location;

                public AllocationSegment(int start, int end, RegisterOperand location)
                {
                    Start = start;
                    End = end;
                    Location = location;
                }
            }

            public GenTree Value { get; }
            public RegisterClass RegisterClass { get; }
            public ImmutableArray<LinearLiveRange> Ranges { get; }
            public ImmutableArray<int> UsePositions { get; }
            public ImmutableArray<LinearRefPosition> RefPositions { get; }
            public int DefinitionPosition { get; }
            public bool CrossesCall { get; }
            public bool RequiresSingleLocation { get; }
            public bool RequiresStackHome { get; }
            public bool MustStayInMemory { get; }
            public int StackHomeSize { get; }
            public int StackHomeAlignment { get; }
            public int AbiSegmentIndex { get; }
            public int AbiSegmentOffset { get; }
            public int AbiSegmentSize { get; }
            public int Start { get; }
            public int End { get; }
            public MachineRegister AssignedRegister { get; set; } = MachineRegister.Invalid;
            public int AssignedRegisterEnd { get; set; }
            public int SpillSlot { get; set; } = -1;
            public int PermanentSpillStart { get; private set; } = int.MaxValue;

            private readonly List<AllocationSegment> _segments = new();

            public bool IsEmpty => Ranges.Length == 0;
            public bool HasAssignedRegister => AssignedRegister != MachineRegister.Invalid && AssignedRegisterEnd > Start;
            public bool IsAbiFragment => AbiSegmentIndex >= 0;

            public RegisterOperand PrimaryHome
            {
                get
                {
                    if (_segments.Count == 0)
                        return RegisterOperand.None;
                    return _segments[0].Location;
                }
            }

            public bool HasMemoryLocationSegment
            {
                get
                {
                    for (int i = 0; i < _segments.Count; i++)
                    {
                        if (_segments[i].Location.IsMemoryOperand)
                            return true;
                    }

                    return false;
                }
            }

            public AllocationInterval(
                GenTree value,
                RegisterClass registerClass,
                ImmutableArray<LinearLiveRange> ranges,
                ImmutableArray<int> usePositions,
                ImmutableArray<LinearRefPosition> refPositions,
                int definitionPosition,
                bool crossesCall,
                bool requiresSingleLocation,
                bool requiresStackHome,
                bool mustStayInMemory,
                int stackHomeSize = 0,
                int stackHomeAlignment = 1,
                int abiSegmentIndex = -1,
                int abiSegmentOffset = 0,
                int abiSegmentSize = 0)
            {
                if (stackHomeSize < 0)
                    throw new ArgumentOutOfRangeException(nameof(stackHomeSize));
                if (stackHomeAlignment <= 0)
                    throw new ArgumentOutOfRangeException(nameof(stackHomeAlignment));
                if (abiSegmentIndex < -1)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentIndex));
                if (abiSegmentOffset < 0)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentOffset));
                if (abiSegmentSize < 0)
                    throw new ArgumentOutOfRangeException(nameof(abiSegmentSize));

                Value = value;
                RegisterClass = registerClass;
                Ranges = ranges.IsDefault ? ImmutableArray<LinearLiveRange>.Empty : ranges;
                UsePositions = usePositions.IsDefault ? ImmutableArray<int>.Empty : usePositions;
                RefPositions = refPositions.IsDefault ? ImmutableArray<LinearRefPosition>.Empty : refPositions;
                DefinitionPosition = definitionPosition;
                CrossesCall = crossesCall;
                RequiresSingleLocation = requiresSingleLocation;
                RequiresStackHome = requiresStackHome;
                MustStayInMemory = mustStayInMemory;
                StackHomeSize = stackHomeSize;
                StackHomeAlignment = stackHomeAlignment;
                AbiSegmentIndex = abiSegmentIndex;
                AbiSegmentOffset = abiSegmentOffset;
                AbiSegmentSize = abiSegmentSize;

                if (Ranges.Length == 0)
                {
                    Start = definitionPosition;
                    End = definitionPosition;
                }
                else
                {
                    Start = Ranges[0].Start;
                    End = Ranges[0].End;
                    for (int i = 1; i < Ranges.Length; i++)
                    {
                        if (Ranges[i].Start < Start)
                            Start = Ranges[i].Start;
                        if (Ranges[i].End > End)
                            End = Ranges[i].End;
                    }
                }

                AssignedRegisterEnd = Start;
            }

            public bool Covers(int position)
            {
                for (int i = 0; i < Ranges.Length; i++)
                {
                    var range = Ranges[i];
                    if (range.Start <= position && position < range.End)
                        return true;
                }
                return false;
            }

            public bool CrossesPosition(int position)
            {
                for (int i = 0; i < Ranges.Length; i++)
                {
                    var range = Ranges[i];
                    if (range.Start <= position && position + 1 < range.End)
                        return true;
                }
                return false;
            }

            public bool Intersects(AllocationInterval other)
            {
                int i = 0;
                int j = 0;
                while (i < Ranges.Length && j < other.Ranges.Length)
                {
                    var a = Ranges[i];
                    var b = other.Ranges[j];
                    if (a.Start < b.End && b.Start < a.End)
                        return true;
                    if (a.End <= b.Start)
                        i++;
                    else
                        j++;
                }
                return false;
            }

            public int FirstIntersection(AllocationInterval other)
                => FirstIntersection(other, int.MinValue, int.MaxValue);

            public int FirstIntersection(AllocationInterval other, int minPosition, int maxPosition)
            {
                int best = int.MaxValue;
                int i = 0;
                int j = 0;
                while (i < Ranges.Length && j < other.Ranges.Length)
                {
                    var a = Ranges[i];
                    var b = other.Ranges[j];
                    int start = Math.Max(Math.Max(a.Start, b.Start), minPosition);
                    int end = Math.Min(Math.Min(a.End, b.End), maxPosition);
                    if (start < end && start < best)
                        best = start;

                    if (a.End <= b.End)
                        i++;
                    else
                        j++;
                }
                return best;
            }

            public int FirstRegisterIntersection(AllocationInterval other)
                => FirstRegisterIntersection(other, int.MinValue, int.MaxValue);

            public int FirstRegisterIntersection(AllocationInterval other, int minPosition, int maxPosition)
            {
                if (!HasAssignedRegister)
                    return int.MaxValue;

                int best = int.MaxValue;
                for (int s = 0; s < _segments.Count; s++)
                {
                    var segment = _segments[s];
                    if (!segment.Location.IsRegister || segment.Location.Register != AssignedRegister)
                        continue;

                    int p = FirstIntersection(other, Math.Max(minPosition, segment.Start), Math.Min(maxPosition, segment.End));
                    if (p < best)
                        best = p;
                }
                return best;
            }

            public int NextUseAfterOrAt(int position)
            {
                for (int i = 0; i < UsePositions.Length; i++)
                {
                    if (UsePositions[i] >= position)
                        return UsePositions[i];
                }
                return int.MaxValue;
            }

            public int NextRequiredRefPositionAfter(int position)
            {
                for (int i = 0; i < RefPositions.Length; i++)
                {
                    int refPosition = RefPositions[i].Position;
                    if (refPosition > position)
                        return refPosition;
                }

                for (int i = 0; i < UsePositions.Length; i++)
                {
                    int usePosition = UsePositions[i];
                    if (usePosition > position)
                        return usePosition;
                }

                return int.MaxValue;
            }

            public int NextRequiredRefPositionAtOrAfter(int position)
            {
                for (int i = 0; i < RefPositions.Length; i++)
                {
                    int refPosition = RefPositions[i].Position;
                    if (refPosition >= position)
                        return refPosition;
                }

                for (int i = 0; i < UsePositions.Length; i++)
                {
                    int usePosition = UsePositions[i];
                    if (usePosition >= position)
                        return usePosition;
                }

                return int.MaxValue;
            }

            public void MarkPermanentlySpilledFrom(int position)
            {
                if (position < PermanentSpillStart)
                    PermanentSpillStart = position;
            }

            public int FirstLivePositionAtOrAfter(int position)
            {
                for (int i = 0; i < Ranges.Length; i++)
                {
                    var range = Ranges[i];
                    if (position < range.Start)
                        return range.Start;
                    if (position < range.End)
                        return position;
                }

                return int.MaxValue;
            }

            public void AddSegment(int start, int end, RegisterOperand location)
            {
                if (end <= start || location.IsNone)
                    return;

                _segments.Add(new AllocationSegment(start, end, location));
                NormalizeSegments();
            }

            public void ReplaceWithSingleSegment(int start, int end, RegisterOperand location)
            {
                if (location.IsNone)
                    throw new ArgumentOutOfRangeException(nameof(location));

                _segments.Clear();
                AssignedRegister = location.IsRegister ? location.Register : MachineRegister.Invalid;
                AssignedRegisterEnd = location.IsRegister ? end : start;
                AddSegment(start, end, location);
            }

            public void EndAssignedRegisterAt(int splitPosition)
            {
                if (!HasAssignedRegister)
                    return;

                MachineRegister splitRegister = AssignedRegister;
                TrimAssignedRegisterAt(splitRegister, splitPosition);
                if (AssignedRegisterEnd > splitPosition)
                    AssignedRegisterEnd = splitPosition;
                NormalizeSegments();
            }

            public void SplitAssignedRegisterToSpill(int splitPosition, int spillEnd, RegisterOperand spillHome)
            {
                if (spillHome.IsNone)
                    throw new ArgumentOutOfRangeException(nameof(spillHome));
                if (spillEnd < splitPosition)
                    throw new ArgumentOutOfRangeException(nameof(spillEnd));

                int spillSegmentEnd = spillEnd == splitPosition ? End : spillEnd;

                if (!HasAssignedRegister)
                {
                    RemoveSegmentsOverlapping(splitPosition, spillSegmentEnd);
                    AddSegment(splitPosition, spillSegmentEnd, spillHome);
                    return;
                }

                MachineRegister splitRegister = AssignedRegister;
                TrimAssignedRegisterAt(splitRegister, splitPosition);
                RemoveSegmentsOverlapping(splitPosition, spillSegmentEnd);

                AssignedRegister = MachineRegister.Invalid;
                AssignedRegisterEnd = splitPosition;
                AddSegment(splitPosition, spillSegmentEnd, spillHome);
                NormalizeSegments();
            }

            public void AddSpillSegmentAfterAssignedRegister(int splitPosition, int spillEnd, RegisterOperand spillHome)
            {
                if (spillHome.IsNone)
                    throw new ArgumentOutOfRangeException(nameof(spillHome));
                if (spillEnd < splitPosition)
                    throw new ArgumentOutOfRangeException(nameof(spillEnd));

                int spillSegmentEnd = spillEnd == splitPosition ? End : spillEnd;

                if (HasAssignedRegister)
                {
                    MachineRegister splitRegister = AssignedRegister;
                    TrimAssignedRegisterAt(splitRegister, splitPosition);
                    if (AssignedRegisterEnd > splitPosition)
                        AssignedRegisterEnd = splitPosition;
                }

                RemoveSegmentsOverlapping(splitPosition, spillSegmentEnd);
                AddSegment(splitPosition, spillSegmentEnd, spillHome);
                NormalizeSegments();
            }

            private void TrimAssignedRegisterAt(MachineRegister register, int splitPosition)
            {
                if (register == MachineRegister.Invalid)
                    return;

                for (int i = _segments.Count - 1; i >= 0; i--)
                {
                    var segment = _segments[i];
                    if (!segment.Location.IsRegister || segment.Location.Register != register)
                        continue;
                    if (splitPosition <= segment.Start)
                    {
                        _segments.RemoveAt(i);
                    }
                    else if (splitPosition < segment.End)
                    {
                        segment.End = splitPosition;
                    }
                }
            }

            private void RemoveSegmentsOverlapping(int start, int end)
            {
                if (end <= start)
                    return;

                for (int i = _segments.Count - 1; i >= 0; i--)
                {
                    var segment = _segments[i];
                    if (segment.End <= start || segment.Start >= end)
                        continue;

                    if (segment.Start < start && segment.End > end)
                    {
                        _segments.Add(new AllocationSegment(end, segment.End, segment.Location));
                        segment.End = start;
                    }
                    else if (segment.Start < start)
                    {
                        segment.End = start;
                    }
                    else if (segment.End > end)
                    {
                        segment.Start = end;
                    }
                    else
                    {
                        _segments.RemoveAt(i);
                    }
                }
            }

            public ImmutableArray<RegisterAllocationSegment> ToRegisterAllocationSegments()
            {
                NormalizeSegments();
                var builder = ImmutableArray.CreateBuilder<RegisterAllocationSegment>(_segments.Count);
                for (int i = 0; i < _segments.Count; i++)
                {
                    var segment = _segments[i];
                    if (segment.End > segment.Start)
                        builder.Add(new RegisterAllocationSegment(segment.Start, segment.End, segment.Location));
                }
                return builder.ToImmutable();
            }

            private void NormalizeSegments()
            {
                if (_segments.Count <= 1)
                    return;

                _segments.Sort(static (a, b) =>
                {
                    int c = a.Start.CompareTo(b.Start);
                    if (c != 0)
                        return c;
                    if (a.Location.IsRegister != b.Location.IsRegister)
                        return a.Location.IsRegister ? -1 : 1;
                    c = a.End.CompareTo(b.End);
                    if (c != 0)
                        return c;
                    return a.Location.ToString().CompareTo(b.Location.ToString());
                });

                int w = 0;
                for (int r = 0; r < _segments.Count; r++)
                {
                    var next = _segments[r];
                    if (next.End <= next.Start)
                        continue;

                    if (w != 0)
                    {
                        var prev = _segments[w - 1];
                        if (prev.Location.Equals(next.Location) && next.Start <= prev.End)
                        {
                            if (next.End > prev.End)
                                prev.End = next.End;
                            continue;
                        }
                    }

                    _segments[w++] = next;
                }

                if (w < _segments.Count)
                    _segments.RemoveRange(w, _segments.Count - w);
            }
        }
    }
}
