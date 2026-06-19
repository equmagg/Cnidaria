using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace Cnidaria.Cs
{
    internal enum RegisterOperandKind : byte
    {
        None,
        Register,
        SpillSlot,
        IncomingArgumentSlot,
        LocalSlot,
        TempSlot,
        OutgoingArgumentSlot,
        FrameSlot,
    }
    internal enum StackFrameSlotKind : byte
    {
        Invalid,
        Argument,
        Local,
        Temp,
        Spill,
        ParallelCopyScratch,
        ReturnAddress,
        CalleeSavedRegister,
        OutgoingArgument,
    }
    internal enum RegisterFrameBase : byte
    {
        None,
        StackPointer,
        FramePointer,
        IncomingArgumentBase,
    }
    internal readonly struct RegisterOperand : IEquatable<RegisterOperand>
    {
        public readonly RegisterOperandKind Kind;
        public readonly RegisterClass RegisterClass;
        public readonly MachineRegister Register;
        public readonly int SpillSlot;
        public readonly StackFrameSlotKind FrameSlotKind;
        public readonly RegisterFrameBase FrameBase;
        public readonly int FrameSlotIndex;
        public readonly int FrameOffset;
        public readonly int FrameSlotSize;
        public readonly bool IsAddress;

        private RegisterOperand(
            RegisterOperandKind kind,
            RegisterClass registerClass,
            MachineRegister register,
            int spillSlot,
            StackFrameSlotKind frameSlotKind,
            RegisterFrameBase frameBase,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress)
        {
            Kind = kind;
            RegisterClass = registerClass;
            Register = register;
            SpillSlot = spillSlot;
            FrameSlotKind = frameSlotKind;
            FrameBase = frameBase;
            FrameSlotIndex = frameSlotIndex;
            FrameOffset = frameOffset;
            FrameSlotSize = frameSlotSize;
            IsAddress = isAddress;
        }

        public static RegisterOperand None => new RegisterOperand(
            RegisterOperandKind.None,
            RegisterClass.Invalid,
            MachineRegister.Invalid,
            -1,
            StackFrameSlotKind.Invalid,
            RegisterFrameBase.None,
            -1,
            0,
            0,
            isAddress: false);

        public static RegisterOperand ForRegister(MachineRegister register)
        {
            var registerClass = MachineRegisters.GetClass(register);
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(register));
            return new RegisterOperand(
                RegisterOperandKind.Register,
                registerClass,
                register,
                -1,
                StackFrameSlotKind.Invalid,
                RegisterFrameBase.None,
                -1,
                0,
                0,
                isAddress: false);
        }

        public static RegisterOperand ForSpillSlot(RegisterClass registerClass, int slot)
            => ForSpillSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForSpillSlot(RegisterClass registerClass, int slot, int offset, int size)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            return new RegisterOperand(
                RegisterOperandKind.SpillSlot,
                registerClass,
                MachineRegister.Invalid,
                slot,
                StackFrameSlotKind.Invalid,
                RegisterFrameBase.None,
                -1,
                offset,
                size,
                isAddress: false);
        }

        public static RegisterOperand ForIncomingArgumentSlot(RegisterClass registerClass, int slot)
            => ForIncomingArgumentSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForIncomingArgumentSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.IncomingArgumentSlot, registerClass, StackFrameSlotKind.Argument, slot, offset, size);

        public static RegisterOperand ForLocalSlot(RegisterClass registerClass, int slot)
            => ForLocalSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForLocalSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.LocalSlot, registerClass, StackFrameSlotKind.Local, slot, offset, size);

        public static RegisterOperand ForTempSlot(RegisterClass registerClass, int slot)
            => ForTempSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForTempSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.TempSlot, registerClass, StackFrameSlotKind.Temp, slot, offset, size);

        public static RegisterOperand ForOutgoingArgumentSlot(RegisterClass registerClass, int slot)
            => ForOutgoingArgumentSlot(registerClass, slot, offset: 0, size: 0);

        public static RegisterOperand ForOutgoingArgumentSlot(RegisterClass registerClass, int slot, int offset, int size)
            => ForUnresolvedFrameSlot(RegisterOperandKind.OutgoingArgumentSlot, registerClass, StackFrameSlotKind.OutgoingArgument, slot, offset, size);

        private static RegisterOperand ForUnresolvedFrameSlot(
            RegisterOperandKind kind,
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            int slot,
            int offset,
            int size)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            return new RegisterOperand(
                kind,
                registerClass,
                MachineRegister.Invalid,
                -1,
                frameSlotKind,
                RegisterFrameBase.None,
                slot,
                offset,
                size,
                isAddress: false);
        }

        public static RegisterOperand ForFrameSlot(
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress = false)
            => ForFrameSlot(
                registerClass,
                frameSlotKind,
                RegisterFrameBase.FramePointer,
                frameSlotIndex,
                frameOffset,
                frameSlotSize,
                isAddress);

        public static RegisterOperand ForFrameSlot(
            RegisterClass registerClass,
            StackFrameSlotKind frameSlotKind,
            RegisterFrameBase frameBase,
            int frameSlotIndex,
            int frameOffset,
            int frameSlotSize,
            bool isAddress = false)
        {
            if (registerClass == RegisterClass.Invalid)
                throw new ArgumentOutOfRangeException(nameof(registerClass));
            if (frameSlotKind == StackFrameSlotKind.Invalid)
                throw new ArgumentOutOfRangeException(nameof(frameSlotKind));
            if (frameBase is not (RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer or RegisterFrameBase.IncomingArgumentBase))
                throw new ArgumentOutOfRangeException(nameof(frameBase));
            if (frameSlotIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameSlotIndex));
            if (frameOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(frameOffset));
            if (frameSlotSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameSlotSize));

            return new RegisterOperand(
                RegisterOperandKind.FrameSlot,
                registerClass,
                MachineRegister.Invalid,
                -1,
                frameSlotKind,
                frameBase,
                frameSlotIndex,
                frameOffset,
                frameSlotSize,
                isAddress);
        }

        public bool IsNone => Kind == RegisterOperandKind.None;
        public bool IsRegister => Kind == RegisterOperandKind.Register;
        public bool IsSpillSlot => Kind == RegisterOperandKind.SpillSlot;
        public bool IsIncomingArgumentSlot => Kind == RegisterOperandKind.IncomingArgumentSlot;
        public bool IsLocalSlot => Kind == RegisterOperandKind.LocalSlot;
        public bool IsTempSlot => Kind == RegisterOperandKind.TempSlot;
        public bool IsOutgoingArgumentSlot => Kind == RegisterOperandKind.OutgoingArgumentSlot;
        public bool IsUnresolvedFrameSlot => IsIncomingArgumentSlot || IsLocalSlot || IsTempSlot || IsOutgoingArgumentSlot;
        public bool IsFrameSlot => Kind == RegisterOperandKind.FrameSlot;
        public bool IsMemoryOperand => IsSpillSlot || IsUnresolvedFrameSlot || IsFrameSlot;

        public RegisterOperand AsAddress()
        {
            if (IsAddress)
                return this;
            if (!IsMemoryOperand)
                throw new InvalidOperationException("Only stack or frame memory operands can be addressed: " + this + ".");

            return new RegisterOperand(
                Kind,
                RegisterClass.General,
                MachineRegister.Invalid,
                SpillSlot,
                FrameSlotKind,
                FrameBase,
                FrameSlotIndex,
                FrameOffset,
                FrameSlotSize,
                isAddress: true);
        }

        public bool Equals(RegisterOperand other)
            => Kind == other.Kind &&
               RegisterClass == other.RegisterClass &&
               Register == other.Register &&
               SpillSlot == other.SpillSlot &&
               FrameSlotKind == other.FrameSlotKind &&
               FrameBase == other.FrameBase &&
               FrameSlotIndex == other.FrameSlotIndex &&
               FrameOffset == other.FrameOffset &&
               FrameSlotSize == other.FrameSlotSize &&
               IsAddress == other.IsAddress;

        public override bool Equals(object? obj) => obj is RegisterOperand other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (int)RegisterClass;
                hash = (hash * 397) ^ (int)Register;
                hash = (hash * 397) ^ SpillSlot;
                hash = (hash * 397) ^ (int)FrameSlotKind;
                hash = (hash * 397) ^ (int)FrameBase;
                hash = (hash * 397) ^ FrameSlotIndex;
                hash = (hash * 397) ^ FrameOffset;
                hash = (hash * 397) ^ FrameSlotSize;
                hash = (hash * 397) ^ (IsAddress ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
        {
            string text = Kind switch
            {
                RegisterOperandKind.None => "_",
                RegisterOperandKind.Register => MachineRegisters.Format(Register),
                RegisterOperandKind.SpillSlot => $"spill.{MachineRegisters.ClassSuffix(RegisterClass)}[{SpillSlot}]" +
                (FrameOffset != 0 || FrameSlotSize != 0 ? "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString() : string.Empty),
                RegisterOperandKind.IncomingArgumentSlot => FormatUnresolvedFrameSlot("inarg"),
                RegisterOperandKind.LocalSlot => FormatUnresolvedFrameSlot("local"),
                RegisterOperandKind.TempSlot => FormatUnresolvedFrameSlot("temp"),
                RegisterOperandKind.OutgoingArgumentSlot => FormatUnresolvedFrameSlot("outarg"),
                RegisterOperandKind.FrameSlot => "frame." + FrameSlotKind.ToString().ToLowerInvariant() +
                    "[" + FrameSlotIndex.ToString() + "]@" + FrameBaseName(FrameBase) + "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString(),
                _ => "?",
            };

            return IsAddress ? "&" + text : text;
        }

        private string FormatUnresolvedFrameSlot(string prefix)
        {
            var text = prefix + "." + MachineRegisters.ClassSuffix(RegisterClass) + "[" + FrameSlotIndex.ToString() + "]";
            if (FrameOffset != 0 || FrameSlotSize != 0)
                text += "+" + FrameOffset.ToString() + ":" + FrameSlotSize.ToString();
            return text;
        }

        private static string FrameBaseName(RegisterFrameBase frameBase)
        {
            return frameBase switch
            {
                RegisterFrameBase.StackPointer => "sp",
                RegisterFrameBase.FramePointer => "fp",
                RegisterFrameBase.IncomingArgumentBase => "inarg",
                _ => "?",
            };
        }
    }
    internal sealed class StackFrameSlot
    {
        public StackFrameSlotKind Kind { get; }
        public int Index { get; }
        public int Offset { get; }
        public int Size { get; }
        public int Alignment { get; }
        public RegisterClass RegisterClass { get; }
        public RuntimeType? Type { get; }
        public MachineRegister SavedRegister { get; }

        public StackFrameSlot(
            StackFrameSlotKind kind,
            int index,
            int offset,
            int size,
            int alignment,
            RegisterClass registerClass = RegisterClass.Invalid,
            RuntimeType? type = null,
            MachineRegister savedRegister = MachineRegister.Invalid)
        {
            if (kind == StackFrameSlotKind.Invalid)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (alignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(alignment));

            Kind = kind;
            Index = index;
            Offset = offset;
            Size = size;
            Alignment = alignment;
            RegisterClass = registerClass;
            Type = type;
            SavedRegister = savedRegister;
        }

        public int EndOffset => Offset + Size;
        public override string ToString() => Kind + "[" + Index + "] +" + Offset + " size=" + Size;
    }
    internal enum RegisterStackFrameModel : byte
    {
        Leaf,
        RootFrame,
        SharedRootFrameWithFunclets,
    }
    internal sealed class StackFrameLayout
    {
        public static StackFrameLayout Empty { get; } = new StackFrameLayout(
            frameSize: 0,
            frameAlignment: 1,
            calleeSaveAreaOffset: 0,
            calleeSaveAreaSize: 0,
            argumentHomeAreaOffset: 0,
            argumentHomeAreaSize: 0,
            localAreaOffset: 0,
            localAreaSize: 0,
            tempAreaOffset: 0,
            tempAreaSize: 0,
            spillAreaOffset: 0,
            spillAreaSize: 0,
            outgoingArgumentAreaOffset: 0,
            outgoingArgumentAreaSize: 0,
            argumentSlots: ImmutableArray<StackFrameSlot>.Empty,
            localSlots: ImmutableArray<StackFrameSlot>.Empty,
            tempSlots: ImmutableArray<StackFrameSlot>.Empty,
            spillSlots: ImmutableArray<StackFrameSlot>.Empty,
            calleeSavedSlots: ImmutableArray<StackFrameSlot>.Empty,
            outgoingArgumentSlots: ImmutableArray<StackFrameSlot>.Empty,
            usesFramePointer: false,
            frameModel: RegisterStackFrameModel.Leaf);

        public int FrameSize { get; }
        public int FrameAlignment { get; }
        public bool UsesFramePointer { get; }
        public RegisterStackFrameModel FrameModel { get; }
        public int CalleeSaveAreaOffset { get; }
        public int CalleeSaveAreaSize { get; }
        public int ArgumentHomeAreaOffset { get; }
        public int ArgumentHomeAreaSize { get; }
        public int LocalAreaOffset { get; }
        public int LocalAreaSize { get; }
        public int TempAreaOffset { get; }
        public int TempAreaSize { get; }
        public int SpillAreaOffset { get; }
        public int SpillAreaSize { get; }
        public int OutgoingArgumentAreaOffset { get; }
        public int OutgoingArgumentAreaSize { get; }
        public ImmutableArray<StackFrameSlot> ArgumentSlots { get; }
        public ImmutableArray<StackFrameSlot> LocalSlots { get; }
        public ImmutableArray<StackFrameSlot> TempSlots { get; }
        public ImmutableArray<StackFrameSlot> SpillSlots { get; }
        public ImmutableArray<StackFrameSlot> CalleeSavedSlots { get; }
        public ImmutableArray<StackFrameSlot> OutgoingArgumentSlots { get; }
        public IReadOnlyDictionary<int, StackFrameSlot> SpillSlotByIndex { get; }

        public bool IsEmpty => FrameSize == 0 &&
            ArgumentSlots.Length == 0 &&
            LocalSlots.Length == 0 &&
            TempSlots.Length == 0 &&
            SpillSlots.Length == 0 &&
            CalleeSavedSlots.Length == 0 &&
            OutgoingArgumentSlots.Length == 0;

        public StackFrameLayout(
            int frameSize,
            int frameAlignment,
            int calleeSaveAreaOffset,
            int calleeSaveAreaSize,
            int argumentHomeAreaOffset,
            int argumentHomeAreaSize,
            int localAreaOffset,
            int localAreaSize,
            int tempAreaOffset,
            int tempAreaSize,
            int spillAreaOffset,
            int spillAreaSize,
            int outgoingArgumentAreaOffset,
            int outgoingArgumentAreaSize,
            ImmutableArray<StackFrameSlot> argumentSlots,
            ImmutableArray<StackFrameSlot> localSlots,
            ImmutableArray<StackFrameSlot> tempSlots,
            ImmutableArray<StackFrameSlot> spillSlots,
            ImmutableArray<StackFrameSlot> calleeSavedSlots,
            ImmutableArray<StackFrameSlot> outgoingArgumentSlots,
            bool usesFramePointer = false,
            RegisterStackFrameModel frameModel = RegisterStackFrameModel.Leaf)
        {
            if (frameSize < 0)
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (frameAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameAlignment));

            FrameSize = frameSize;
            FrameAlignment = frameAlignment;
            UsesFramePointer = usesFramePointer;
            FrameModel = frameModel;
            CalleeSaveAreaOffset = calleeSaveAreaOffset;
            CalleeSaveAreaSize = calleeSaveAreaSize;
            ArgumentHomeAreaOffset = argumentHomeAreaOffset;
            ArgumentHomeAreaSize = argumentHomeAreaSize;
            LocalAreaOffset = localAreaOffset;
            LocalAreaSize = localAreaSize;
            TempAreaOffset = tempAreaOffset;
            TempAreaSize = tempAreaSize;
            SpillAreaOffset = spillAreaOffset;
            SpillAreaSize = spillAreaSize;
            OutgoingArgumentAreaOffset = outgoingArgumentAreaOffset;
            OutgoingArgumentAreaSize = outgoingArgumentAreaSize;
            ArgumentSlots = argumentSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : argumentSlots;
            LocalSlots = localSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : localSlots;
            TempSlots = tempSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : tempSlots;
            SpillSlots = spillSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : spillSlots;
            CalleeSavedSlots = calleeSavedSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : calleeSavedSlots;
            OutgoingArgumentSlots = outgoingArgumentSlots.IsDefault ? ImmutableArray<StackFrameSlot>.Empty : outgoingArgumentSlots;

            var spillMap = new Dictionary<int, StackFrameSlot>();
            for (int i = 0; i < SpillSlots.Length; i++)
                spillMap[SpillSlots[i].Index] = SpillSlots[i];
            SpillSlotByIndex = spillMap;
        }

        public StackFrameSlot GetSpillSlot(int spillSlot)
        {
            if (!SpillSlotByIndex.TryGetValue(spillSlot, out var slot))
                throw new InvalidOperationException("Missing finalized stack slot for spill slot " + spillSlot + ".");
            return slot;
        }

        public bool TryGetArgumentSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(ArgumentSlots, index, out slot);

        public bool TryGetLocalSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(LocalSlots, index, out slot);

        public bool TryGetTempSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(TempSlots, index, out slot);

        public bool TryGetCalleeSavedSlot(MachineRegister register, out StackFrameSlot slot)
        {
            for (int i = 0; i < CalleeSavedSlots.Length; i++)
            {
                if (CalleeSavedSlots[i].SavedRegister == register)
                {
                    slot = CalleeSavedSlots[i];
                    return true;
                }
            }

            slot = null!;
            return false;
        }

        public bool TryGetOutgoingArgumentSlot(int index, out StackFrameSlot slot)
            => TryGetIndexedSlot(OutgoingArgumentSlots, index, out slot);

        private static bool TryGetIndexedSlot(ImmutableArray<StackFrameSlot> slots, int index, out StackFrameSlot slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Index == index)
                {
                    slot = slots[i];
                    return true;
                }
            }

            slot = null!;
            return false;
        }
    }
    internal sealed class RegisterStackLayoutOptions
    {
        public static RegisterStackLayoutOptions Default => new RegisterStackLayoutOptions();

        public int FrameAlignment { get; set; } = 16;
        public bool AllocateLocalSlots { get; set; } = true;
        public bool AllocateTempSlots { get; set; } = true;
        public bool SaveUsedCalleeSavedRegisters { get; set; } = true;

        public bool SaveFramePointerWhenFrameIsUsed { get; set; } = false;

        public bool UseFramePointerForFunclets { get; set; } = true;

        public bool SaveReturnAddressForNonLeafMethods { get; set; } = true;
        public bool SaveReturnAddressForLeafMethods { get; set; } = false;

        public int OutgoingArgumentSlotCount { get; set; }
    }
}
