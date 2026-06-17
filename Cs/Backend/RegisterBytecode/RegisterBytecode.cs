using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Cnidaria.Cs
{
    internal static class RegisterVmIsa
    {
        public const int InstructionSize = 16;
        public const int HeaderSize = 88;
        public const byte InvalidRegister = 255;
        public const byte ZeroRegister = (byte)MachineRegister.X0;
        public const byte StackPointerRegister = (byte)MachineRegister.X2;
        public const byte FramePointerRegister = (byte)MachineRegister.X8;
        public const byte ReturnAddressRegister = (byte)MachineRegister.X1;
        public const byte IntegerReturnRegister0 = (byte)MachineRegister.X10;
        public const byte IntegerReturnRegister1 = (byte)MachineRegister.X11;
        public const byte FloatReturnRegister0 = (byte)MachineRegister.F10;
        public const byte FloatReturnRegister1 = (byte)MachineRegister.F11;

        public static bool IsIntegerRegister(byte register)
            => register <= (byte)MachineRegister.X31;

        public static bool IsFloatRegister(byte register)
            => register >= (byte)MachineRegister.F0 && register <= (byte)MachineRegister.F31;

        public static byte EncodeRegister(MachineRegister register)
        {
            if (register == MachineRegister.Invalid)
                return InvalidRegister;
            if (!IsIntegerRegister((byte)register) && !IsFloatRegister((byte)register))
                throw new ArgumentOutOfRangeException(nameof(register));
            return (byte)register;
        }

        public static MachineRegister DecodeRegister(byte register)
        {
            if (register == InvalidRegister)
                return MachineRegister.Invalid;
            if (!IsIntegerRegister(register) && !IsFloatRegister(register))
                throw new ArgumentOutOfRangeException(nameof(register));
            return (MachineRegister)register;
        }
    }

    [Flags]
    internal enum ImageFlags : byte
    {
        None = 0,
        LittleEndian = 1 << 0,
        Target32 = 1 << 1, /*overwise Target64*/
        UsesFramePointer = 1 << 2,
        HasEh = 1 << 3,
        HasGcInfo = 1 << 4,
        HasUnwindInfo = 1 << 5,
        HasReflectionMetadata = 1 << 6,
    }

    [Flags]
    internal enum InstructionFlags : ushort
    {
        None = 0,
        Volatile = 1 << 0,
        Unaligned = 1 << 1,
        NoNullCheck = 1 << 2,
        NoBoundsCheck = 1 << 3,
        WriteBarrier = 1 << 4,
        TailCall = 1 << 5,
        GcSafePoint = 1 << 6,
        MayThrow = 1 << 7,
        PreserveException = 1 << 8,
    }

    [Flags]
    internal enum CallFlags : ushort
    {
        None = 0,
        Tail = 1 << 0,
        InternalCall = 1 << 3,
        HiddenReturnBuffer = 1 << 4,
        GcSafePoint = 1 << 5,
        MayThrow = 1 << 6,
    }

    internal enum MemoryBase : byte
    {
        Register = 0,
        StackPointer = 1,
        FramePointer = 2,
        ThreadPointer = 3,
        GlobalPointer = 4,
    }

    [Flags]
    internal enum MemoryFlags : byte
    {
        None = 0,
        Volatile = 1 << 0,
        Unaligned = 1 << 1,
        NoNullCheck = 1 << 2,
        NoBoundsCheck = 1 << 3,
    }

    [Flags]
    internal enum FieldAccessFlags : byte
    {
        None = 0,
        DeclaringTypeIsValueType = 1 << 0,
        FieldTypeContainsGcPointers = 1 << 1,
    }

    internal static class Aux
    {
        private const int MemoryScaleShift = 0;
        private const int MemoryAlignmentShift = 4;
        private const int MemoryBaseShift = 8;
        private const int MemoryFlagsShift = 12;
        private const ushort MemoryNibbleMask = 0xF;

        public static ushort Memory(byte scaleLog2 = 0, byte alignmentLog2 = 0,
            MemoryBase memoryBase = Cs.MemoryBase.Register, InstructionFlags flags = InstructionFlags.None)
            => Memory(scaleLog2, alignmentLog2, memoryBase, ToMemoryFlags(flags));

        public static ushort Memory(byte scaleLog2, byte alignmentLog2, MemoryBase memoryBase, MemoryFlags flags)
        {
            if (scaleLog2 > 15) throw new ArgumentOutOfRangeException(nameof(scaleLog2));
            if (alignmentLog2 > 15) throw new ArgumentOutOfRangeException(nameof(alignmentLog2));
            if ((uint)memoryBase > 15) throw new ArgumentOutOfRangeException(nameof(memoryBase));
            if (((ushort)flags & ~MemoryNibbleMask) != 0) throw new ArgumentOutOfRangeException(nameof(flags));

            return (ushort)(
                ((ushort)scaleLog2 << MemoryScaleShift) |
                ((ushort)alignmentLog2 << MemoryAlignmentShift) |
                ((ushort)(byte)memoryBase << MemoryBaseShift) |
                ((ushort)flags << MemoryFlagsShift));
        }

        public static ushort MemoryWithFlags(ushort aux, MemoryFlags flags)
        {
            if (((ushort)flags & ~MemoryNibbleMask) != 0) throw new ArgumentOutOfRangeException(nameof(flags));
            return (ushort)((aux & 0x0FFF) | ((ushort)flags << MemoryFlagsShift));
        }

        public static ushort MemoryWithFlags(ushort aux, InstructionFlags flags)
            => MemoryWithFlags(aux, ToMemoryFlags(flags));

        public static ushort Call(CallFlags flags)
            => (ushort)flags;

        public static ushort Instruction(InstructionFlags flags)
            => (ushort)flags;

        public static ushort Field(InstructionFlags instructionFlags, FieldAccessFlags fieldFlags)
        {
            if (((ushort)instructionFlags & 0xFF00) != 0)
                throw new ArgumentOutOfRangeException(nameof(instructionFlags), "Field aux reserves the high byte for field access flags.");
            return (ushort)(((ushort)instructionFlags & 0x00FF) | ((ushort)(byte)fieldFlags << 8));
        }

        public static InstructionFlags FieldInstructionFlags(ushort aux)
            => (InstructionFlags)(aux & 0x00FF);

        public static FieldAccessFlags FieldFlags(ushort aux)
            => (FieldAccessFlags)(byte)(aux >> 8);

        public static int MemoryScaleLog2(ushort aux) => aux & MemoryNibbleMask;
        public static int MemoryAlignmentLog2(ushort aux) => (aux >> MemoryAlignmentShift) & MemoryNibbleMask;
        public static MemoryBase MemoryBase(ushort aux) => (MemoryBase)((aux >> MemoryBaseShift) & MemoryNibbleMask);
        public static MemoryFlags MemoryFlags(ushort aux) => (MemoryFlags)((aux >> MemoryFlagsShift) & MemoryNibbleMask);

        public static MemoryFlags ToMemoryFlags(InstructionFlags flags)
        {
            const InstructionFlags supported =
                InstructionFlags.Volatile |
                InstructionFlags.Unaligned |
                InstructionFlags.NoNullCheck |
                InstructionFlags.NoBoundsCheck;

            if ((flags & ~supported) != 0)
                throw new ArgumentOutOfRangeException(nameof(flags), "Unsupported memory aux flag combination.");

            MemoryFlags result = Cs.MemoryFlags.None;
            if ((flags & InstructionFlags.Volatile) != 0) result |= Cs.MemoryFlags.Volatile;
            if ((flags & InstructionFlags.Unaligned) != 0) result |= Cs.MemoryFlags.Unaligned;
            if ((flags & InstructionFlags.NoNullCheck) != 0) result |= Cs.MemoryFlags.NoNullCheck;
            if ((flags & InstructionFlags.NoBoundsCheck) != 0) result |= Cs.MemoryFlags.NoBoundsCheck;
            return result;
        }

        public static InstructionFlags ToInstructionFlags(MemoryFlags flags)
        {
            InstructionFlags result = InstructionFlags.None;
            if ((flags & Cs.MemoryFlags.Volatile) != 0) result |= InstructionFlags.Volatile;
            if ((flags & Cs.MemoryFlags.Unaligned) != 0) result |= InstructionFlags.Unaligned;
            if ((flags & Cs.MemoryFlags.NoNullCheck) != 0) result |= InstructionFlags.NoNullCheck;
            if ((flags & Cs.MemoryFlags.NoBoundsCheck) != 0) result |= InstructionFlags.NoBoundsCheck;
            return result;
        }
    }

    internal enum Op : ushort
    {
        Invalid = 0,
        Nop = 1,
        Break = 2,
        Trap = 3,
        GcPoll = 4,

        RetVoid = 16,
        RetI = 17,
        RetF = 18,
        RetRef = 19,
        RetValue = 20,
        J = 21,
        Leave = 22,
        EndFinally = 23,
        Throw = 24,
        Rethrow = 25,
        LdExceptionRef = 26,
        SwitchI32 = 27,
        SwitchI64 = 28,

        BrTrueI32 = 32,
        BrFalseI32 = 33,
        BrTrueI64 = 34,
        BrFalseI64 = 35,
        BrTrueRef = 36,
        BrFalseRef = 37,
        BrI32Eq = 38,
        BrI32Ne = 39,
        BrI32Lt = 40,
        BrI32Le = 41,
        BrI32Gt = 42,
        BrI32Ge = 43,
        BrU32Lt = 44,
        BrU32Le = 45,
        BrU32Gt = 46,
        BrU32Ge = 47,
        BrI64Eq = 48,
        BrI64Ne = 49,
        BrI64Lt = 50,
        BrI64Le = 51,
        BrI64Gt = 52,
        BrI64Ge = 53,
        BrU64Lt = 54,
        BrU64Le = 55,
        BrU64Gt = 56,
        BrU64Ge = 57,
        BrRefEq = 58,
        BrRefNe = 59,
        BrF32Eq = 60,
        BrF32Ne = 61,
        BrF32Lt = 62,
        BrF32Le = 63,
        BrF32Gt = 64,
        BrF32Ge = 65,
        BrF64Eq = 66,
        BrF64Ne = 67,
        BrF64Lt = 68,
        BrF64Le = 69,
        BrF64Gt = 70,
        BrF64Ge = 71,

        MovI = 96,
        MovF = 97,
        MovRef = 98,
        MovPtr = 99,
        LiI32 = 100,
        LiI64 = 101,
        LiF32Bits = 102,
        LiF64Bits = 103,
        LiNull = 104,
        LiString = 105,
        LiTypeHandle = 106,
        LiMethodHandle = 107,
        LiFieldHandle = 108,
        LiStaticBase = 109,

        I32Add = 128,
        I32Sub = 129,
        I32Mul = 130,
        I32Div = 131,
        I32Rem = 132,
        U32Div = 133,
        U32Rem = 134,
        I32Neg = 135,
        I32AddOvf = 136,
        I32SubOvf = 137,
        I32MulOvf = 138,
        U32AddOvf = 139,
        U32SubOvf = 140,
        U32MulOvf = 141,
        I32And = 142,
        I32Or = 143,
        I32Xor = 144,
        I32Not = 145,
        I32Shl = 146,
        I32Shr = 147,
        U32Shr = 148,
        I32Rol = 149,
        I32Ror = 150,
        I32Eq = 151,
        I32Ne = 152,
        I32Lt = 153,
        I32Le = 154,
        I32Gt = 155,
        I32Ge = 156,
        U32Lt = 157,
        U32Le = 158,
        U32Gt = 159,
        U32Ge = 160,
        I32Min = 161,
        I32Max = 162,
        U32Min = 163,
        U32Max = 164,
        I32AddImm = 176,
        I32SubImm = 177,
        I32MulImm = 178,
        I32AndImm = 179,
        I32OrImm = 180,
        I32XorImm = 181,
        I32ShlImm = 182,
        I32ShrImm = 183,
        U32ShrImm = 184,
        I32EqImm = 185,
        I32NeImm = 186,
        I32LtImm = 187,
        I32LeImm = 188,
        I32GtImm = 189,
        I32GeImm = 190,
        U32LtImm = 191,

        I64Add = 192,
        I64Sub = 193,
        I64Mul = 194,
        I64Div = 195,
        I64Rem = 196,
        U64Div = 197,
        U64Rem = 198,
        I64Neg = 199,
        I64AddOvf = 200,
        I64SubOvf = 201,
        I64MulOvf = 202,
        U64AddOvf = 203,
        U64SubOvf = 204,
        U64MulOvf = 205,
        I64And = 206,
        I64Or = 207,
        I64Xor = 208,
        I64Not = 209,
        I64Shl = 210,
        I64Shr = 211,
        U64Shr = 212,
        I64Rol = 213,
        I64Ror = 214,
        I64Eq = 215,
        I64Ne = 216,
        I64Lt = 217,
        I64Le = 218,
        I64Gt = 219,
        I64Ge = 220,
        U64Lt = 221,
        U64Le = 222,
        U64Gt = 223,
        U64Ge = 224,
        I64Min = 225,
        I64Max = 226,
        U64Min = 227,
        U64Max = 228,
        I64AddImm = 240,
        I64SubImm = 241,
        I64MulImm = 242,
        I64AndImm = 243,
        I64OrImm = 244,
        I64XorImm = 245,
        I64ShlImm = 246,
        I64ShrImm = 247,
        U64ShrImm = 248,
        I64EqImm = 249,
        I64NeImm = 250,
        I64LtImm = 251,
        I64LeImm = 252,
        I64GtImm = 253,
        I64GeImm = 254,
        U64LtImm = 255,

        F32Add = 256,
        F32Sub = 257,
        F32Mul = 258,
        F32Div = 259,
        F32Rem = 260,
        F32Neg = 261,
        F32Abs = 262,
        F32Eq = 263,
        F32Ne = 264,
        F32Lt = 265,
        F32Le = 266,
        F32Gt = 267,
        F32Ge = 268,
        F32Min = 269,
        F32Max = 270,
        F32IsNaN = 271,
        F32IsFinite = 272,

        F64Add = 288,
        F64Sub = 289,
        F64Mul = 290,
        F64Div = 291,
        F64Rem = 292,
        F64Neg = 293,
        F64Abs = 294,
        F64Eq = 295,
        F64Ne = 296,
        F64Lt = 297,
        F64Le = 298,
        F64Gt = 299,
        F64Ge = 300,
        F64Min = 301,
        F64Max = 302,
        F64IsNaN = 303,
        F64IsFinite = 304,

        I32ToI64 = 320,
        U32ToI64 = 321,
        I64ToI32 = 322,
        I64ToI32Ovf = 323,
        U64ToI32Ovf = 324,
        I32ToF32 = 325,
        I32ToF64 = 326,
        U32ToF32 = 327,
        U32ToF64 = 328,
        I64ToF32 = 329,
        I64ToF64 = 330,
        U64ToF32 = 331,
        U64ToF64 = 332,
        F32ToF64 = 333,
        F64ToF32 = 334,
        F32ToI32 = 335,
        F32ToI64 = 336,
        F64ToI32 = 337,
        F64ToI64 = 338,
        F32ToI32Ovf = 339,
        F32ToI64Ovf = 340,
        F64ToI32Ovf = 341,
        F64ToI64Ovf = 342,
        BitcastI32F32 = 343,
        BitcastF32I32 = 344,
        BitcastI64F64 = 345,
        BitcastF64I64 = 346,
        SignExtendI8ToI32 = 347,
        SignExtendI16ToI32 = 348,
        ZeroExtendI8ToI32 = 349,
        ZeroExtendI16ToI32 = 350,
        TruncI32ToI8 = 351,
        TruncI32ToI16 = 352,
        I32ToI8Ovf = 353,
        U32ToI8Ovf = 354,
        I32ToU8Ovf = 355,
        U32ToU8Ovf = 356,
        I32ToI16Ovf = 357,
        U32ToI16Ovf = 358,
        I32ToU16Ovf = 359,
        U32ToU16Ovf = 360,
        I64ToU32Ovf = 361,
        U64ToU32Ovf = 362,

        LdI1 = 384,
        LdU1 = 385,
        LdI2 = 386,
        LdU2 = 387,
        LdI4 = 388,
        LdU4 = 389,
        LdI8 = 390,
        LdN = 391,
        LdF32 = 392,
        LdF64 = 393,
        LdRef = 394,
        LdPtr = 395,
        LdAddr = 396,
        LdObj = 397,
        StI1 = 398,
        StI2 = 399,
        StI4 = 400,
        StI8 = 401,
        StN = 402,
        StF32 = 403,
        StF64 = 404,
        StRef = 405,
        StPtr = 406,
        StObj = 407,
        CpObj = 408,
        CpBlk = 409,
        InitBlk = 410,
        NullCheck = 411,
        BoundsCheck = 412,
        WriteBarrier = 413,

        LdFldI1 = 448,
        LdFldU1 = 449,
        LdFldI2 = 450,
        LdFldU2 = 451,
        LdFldI4 = 452,
        LdFldU4 = 453,
        LdFldI8 = 454,
        LdFldN = 455,
        LdFldF32 = 456,
        LdFldF64 = 457,
        LdFldRef = 458,
        LdFldPtr = 459,
        LdFldAddr = 460,
        LdFldObj = 461,
        StFldI1 = 462,
        StFldI2 = 463,
        StFldI4 = 464,
        StFldI8 = 465,
        StFldN = 466,
        StFldF32 = 467,
        StFldF64 = 468,
        StFldRef = 469,
        StFldPtr = 470,
        StFldObj = 471,


        LdLen = 576,
        LdElemI1 = 577,
        LdElemU1 = 578,
        LdElemI2 = 579,
        LdElemU2 = 580,
        LdElemI4 = 581,
        LdElemU4 = 582,
        LdElemI8 = 583,
        LdElemN = 584,
        LdElemF32 = 585,
        LdElemF64 = 586,
        LdElemRef = 587,
        LdElemPtr = 588,
        LdElemAddr = 589,
        LdElemObj = 590,
        StElemI1 = 591,
        StElemI2 = 592,
        StElemI4 = 593,
        StElemI8 = 594,
        StElemN = 595,
        StElemF32 = 596,
        StElemF64 = 597,
        StElemRef = 598,
        StElemPtr = 599,
        StElemObj = 600,
        LdArrayDataAddr = 601,

        AllocObj = 640,
        NewArr = 641,
        NewSZArray = 642,
        FastAllocateString = 643,
        Box = 644,
        UnboxAny = 645,
        UnboxAddr = 646,
        CastClass = 647,
        IsInst = 648,
        SizeOf = 649,
        InitObj = 650,
        DefaultValue = 651,
        RefEq = 652,
        RefNe = 653,
        RuntimeTypeEquals = 654,
        LdVTableEntry = 655,
        NewDelegate = 656,
        NewDelegateClosed = 657,
        DelegateCombine = 658,
        DelegateRemove = 659,

        CallVoid = 704,
        CallI = 705,
        CallF = 706,
        CallRef = 707,
        CallValue = 708,
        CallInternalVoid = 719,
        CallInternalI = 720,
        CallInternalF = 721,
        CallInternalRef = 722,
        CallInternalValue = 723,
        CallIndirectVoid = 724,
        CallIndirectI = 725,
        CallIndirectF = 726,
        CallIndirectRef = 727,
        CallIndirectValue = 728,
        StackAlloc = 768,
        PtrAddI32 = 769,
        PtrAddI64 = 770,
        PtrSub = 771,
        PtrDiff = 772,
        ByRefAddI32 = 773,
        ByRefAddI64 = 774,
        ByRefToPtr = 775,
        PtrToByRef = 776,
        StaticData = 777,
        AllocHGlobal = 778,
        FreeHGlobal = 779,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = RegisterVmIsa.InstructionSize)]
    internal readonly struct InstrDesc : IEquatable<InstrDesc>
    {
        public readonly Op Op;
        public readonly byte Rd;
        public readonly byte Rs1;
        public readonly byte Rs2;
        public readonly byte Rs3;
        public readonly ushort Aux;
        public readonly long Imm;

        public InstrDesc(Op op,
            byte rd = RegisterVmIsa.InvalidRegister,
            byte rs1 = RegisterVmIsa.InvalidRegister,
            byte rs2 = RegisterVmIsa.InvalidRegister,
            byte rs3 = RegisterVmIsa.InvalidRegister,
            ushort aux = 0, long imm = 0)
        {
            if (op == Op.Invalid) throw new ArgumentOutOfRangeException(nameof(op));
            ValidateRegister(rd, nameof(rd));
            ValidateRegister(rs1, nameof(rs1));
            ValidateRegister(rs2, nameof(rs2));
            ValidateRegister(rs3, nameof(rs3));
            Op = op;
            Rd = rd;
            Rs1 = rs1;
            Rs2 = rs2;
            Rs3 = rs3;
            Aux = aux;
            Imm = imm;
        }

        private static void ValidateRegister(byte register, string name)
        {
            if (register == RegisterVmIsa.InvalidRegister)
                return;
            if (!RegisterVmIsa.IsIntegerRegister(register) && !RegisterVmIsa.IsFloatRegister(register))
                throw new ArgumentOutOfRangeException(name);
        }

        public static InstrDesc Op0(Op op, ushort aux = 0, long imm = 0)
            => new InstrDesc(op, aux: aux, imm: imm);

        public static InstrDesc R(Op op, MachineRegister rd, MachineRegister rs1, MachineRegister rs2, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs1), RegisterVmIsa.EncodeRegister(rs2), aux: aux);

        public static InstrDesc R3(Op op, MachineRegister rd, MachineRegister rs1, MachineRegister rs2, MachineRegister rs3, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs1), RegisterVmIsa.EncodeRegister(rs2), RegisterVmIsa.EncodeRegister(rs3), aux);

        public static InstrDesc I(Op op, MachineRegister rd, MachineRegister rs1, long imm, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs1), aux: aux, imm: imm);

        public static InstrDesc Li(Op op, MachineRegister rd, long imm, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rd), aux: aux, imm: imm);

        public static InstrDesc J(int targetPc)
            => new InstrDesc(Op.J, imm: targetPc);

        public static InstrDesc Br1(Op op, MachineRegister rs1, int targetPc, ushort aux = 0)
            => new InstrDesc(op, rs1: RegisterVmIsa.EncodeRegister(rs1), aux: aux, imm: targetPc);

        public static InstrDesc Br2(Op op, MachineRegister rs1, MachineRegister rs2, int targetPc, ushort aux = 0)
            => new InstrDesc(op, rs1: RegisterVmIsa.EncodeRegister(rs1), rs2: RegisterVmIsa.EncodeRegister(rs2), aux: aux, imm: targetPc);

        public static InstrDesc Mem(Op op, MachineRegister rdOrValue, MachineRegister address, long offset, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rdOrValue), RegisterVmIsa.EncodeRegister(address), RegisterVmIsa.EncodeRegister(index), aux: aux, imm: offset);

        public static InstrDesc Field(Op op, MachineRegister rdOrValue, MachineRegister instance, int fieldOffset, int fieldSize, FieldAccessFlags fieldFlags, ushort aux)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rdOrValue), RegisterVmIsa.EncodeRegister(instance), aux: Cs.Aux.Field((InstructionFlags)(aux & 0x00FF), fieldFlags), imm: PackFieldOperand(fieldOffset, fieldSize));

        public static long PackFieldOperand(int fieldOffset, int fieldSize)
        {
            if (fieldOffset < 0) throw new ArgumentOutOfRangeException(nameof(fieldOffset));
            if (fieldSize < 0) throw new ArgumentOutOfRangeException(nameof(fieldSize));
            return ((long)(uint)fieldSize << 32) | (uint)fieldOffset;
        }

        public static int FieldOffset(long operand)
            => unchecked((int)(uint)operand);

        public static int FieldSize(long operand)
            => checked((int)(uint)(operand >> 32));

        public static InstrDesc Array(Op op, MachineRegister rdOrValue, MachineRegister array, MachineRegister index, int elementTypeLayoutIndex = -1, ushort aux = 0)
            => new InstrDesc(op, RegisterVmIsa.EncodeRegister(rdOrValue), RegisterVmIsa.EncodeRegister(array), RegisterVmIsa.EncodeRegister(index), aux: aux, imm: elementTypeLayoutIndex);

        public static InstrDesc Call(Op op, int targetOperand, CallFlags flags = CallFlags.None)
            => new InstrDesc(op, aux: Cs.Aux.Call(flags), imm: targetOperand);

        public static InstrDesc Switch(Op op, MachineRegister key, int tableStartIndex, int tableCount)
        {
            if (op != Op.SwitchI32 && op != Op.SwitchI64)
                throw new ArgumentOutOfRangeException(nameof(op));
            if (tableStartIndex < 0) throw new ArgumentOutOfRangeException(nameof(tableStartIndex));
            if ((uint)tableCount > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(tableCount));
            return new InstrDesc(op, rs1: RegisterVmIsa.EncodeRegister(key), aux: (ushort)tableCount, imm: tableStartIndex);
        }

        public InstrDesc WithTargetPc(int targetPc)
            => new InstrDesc(Op, Rd, Rs1, Rs2, Rs3, Aux, targetPc);

        public InstrDesc WithAux(ushort aux)
            => new InstrDesc(Op, Rd, Rs1, Rs2, Rs3, aux, Imm);

        public bool Equals(InstrDesc other)
            => Op == other.Op && Rd == other.Rd && Rs1 == other.Rs1 && Rs2 == other.Rs2 && Rs3 == other.Rs3 && Aux == other.Aux && Imm == other.Imm;

        public override bool Equals(object? obj) => obj is InstrDesc other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Op;
                hash = (hash * 397) ^ Rd;
                hash = (hash * 397) ^ Rs1;
                hash = (hash * 397) ^ Rs2;
                hash = (hash * 397) ^ Rs3;
                hash = (hash * 397) ^ Aux;
                hash = (hash * 397) ^ Imm.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
            => $"{Op} rd={FormatReg(Rd)} rs1={FormatReg(Rs1)} rs2={FormatReg(Rs2)} rs3={FormatReg(Rs3)} aux={Aux} imm={Imm}";

        private static string FormatReg(byte register)
            => register == RegisterVmIsa.InvalidRegister ? "-" : MachineRegisters.Format((MachineRegister)register);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct CodeImageHeader
    {
        public readonly ushort HeaderSize;
        public readonly ushort InstructionSize;
        public readonly uint Flags;
        public readonly int CodeOffset;
        public readonly int CodeCount;
        public readonly int MethodOffset;
        public readonly int MethodCount;
        public readonly int EhOffset;
        public readonly int EhCount;
        public readonly int GcSafePointOffset;
        public readonly int GcSafePointCount;
        public readonly int GcRootOffset;
        public readonly int GcRootCount;
        public readonly int UnwindOffset;
        public readonly int UnwindCount;
        public readonly int SwitchTableOffset;
        public readonly int SwitchTableCount;
        public readonly int TypeLayoutOffset;
        public readonly int TypeLayoutCount;
        public readonly int VTableOffset;
        public readonly int VTableCount;
        public readonly int BlobOffset;
        public readonly int BlobLength;

        public CodeImageHeader(
            ImageFlags flags,
            int codeOffset,
            int codeCount,
            int methodOffset,
            int methodCount,
            int ehOffset,
            int ehCount,
            int gcSafePointOffset,
            int gcSafePointCount,
            int gcRootOffset,
            int gcRootCount,
            int unwindOffset,
            int unwindCount,
            int switchTableOffset,
            int switchTableCount,
            int typeLayoutOffset,
            int typeLayoutCount,
            int vTableOffset,
            int vTableCount,
            int blobOffset,
            int blobLength)
        {
            HeaderSize = RegisterVmIsa.HeaderSize;
            InstructionSize = RegisterVmIsa.InstructionSize;
            Flags = (uint)flags;
            CodeOffset = codeOffset;
            CodeCount = codeCount;
            MethodOffset = methodOffset;
            MethodCount = methodCount;
            EhOffset = ehOffset;
            EhCount = ehCount;
            GcSafePointOffset = gcSafePointOffset;
            GcSafePointCount = gcSafePointCount;
            GcRootOffset = gcRootOffset;
            GcRootCount = gcRootCount;
            UnwindOffset = unwindOffset;
            UnwindCount = unwindCount;
            SwitchTableOffset = switchTableOffset;
            SwitchTableCount = switchTableCount;
            TypeLayoutOffset = typeLayoutOffset;
            TypeLayoutCount = typeLayoutCount;
            VTableOffset = vTableOffset;
            VTableCount = vTableCount;
            BlobOffset = blobOffset;
            BlobLength = blobLength;
        }
    }


    [Flags]
    internal enum MethodFlags : ushort
    {
        None = 0,
        UsesFramePointer = 1 << 0,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct MethodRecord
    {
        public readonly int RuntimeMethodId;
        public readonly int StaticConstructorTypeLayoutIndex;
        public readonly int EntryPc;
        public readonly int FrameSize;
        public readonly int OutgoingArgumentAreaOffset;
        public readonly int EhStartIndex;
        public readonly int EhCount;
        public readonly int GcSafePointStartIndex;
        public readonly int GcSafePointCount;
        public readonly int UnwindStartIndex;
        public readonly int UnwindCount;
        public readonly ushort Flags;
        public readonly ushort Reserved;

        public const int NoStaticConstructorTypeLayout = -1;

        public MethodRecord(
            int runtimeMethodId,
            int staticConstructorTypeLayoutIndex,
            int entryPc,
            int frameSize,
            int outgoingArgumentAreaOffset,
            int ehStartIndex,
            int ehCount,
            int gcSafePointStartIndex,
            int gcSafePointCount,
            int unwindStartIndex,
            int unwindCount,
            ushort flags = 0)
        {
            if (runtimeMethodId < -1) throw new ArgumentOutOfRangeException(nameof(runtimeMethodId));
            if (staticConstructorTypeLayoutIndex < -1) throw new ArgumentOutOfRangeException(nameof(staticConstructorTypeLayoutIndex));
            if (entryPc < 0) throw new ArgumentOutOfRangeException(nameof(entryPc));
            if (frameSize < 0) throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (outgoingArgumentAreaOffset < 0) throw new ArgumentOutOfRangeException(nameof(outgoingArgumentAreaOffset));
            RuntimeMethodId = runtimeMethodId;
            StaticConstructorTypeLayoutIndex = staticConstructorTypeLayoutIndex;
            EntryPc = entryPc;
            FrameSize = frameSize;
            OutgoingArgumentAreaOffset = outgoingArgumentAreaOffset;
            EhStartIndex = ehStartIndex;
            EhCount = ehCount;
            GcSafePointStartIndex = gcSafePointStartIndex;
            GcSafePointCount = gcSafePointCount;
            UnwindStartIndex = unwindStartIndex;
            UnwindCount = unwindCount;
            Flags = flags;
            Reserved = 0;
        }
    }


    internal enum EhRegionKind : byte
    {
        Catch = 1,
        CatchAll = 2,
        Finally = 3,
        Fault = 4,
        Filter = 5,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct EhRegionRecord
    {
        public readonly int TryStartPc;
        public readonly int TryEndPc;
        public readonly int HandlerStartPc;
        public readonly int HandlerEndPc;
        public readonly int FilterStartPc;
        public readonly int CatchTypeId;
        public readonly int ParentRegionIndex;
        public readonly int SourceTryStartPc;
        public readonly int SourceTryEndPc;
        public readonly int SourceHandlerStartPc;
        public readonly int SourceHandlerIndex;
        public readonly byte Kind;
        public readonly byte Reserved0;
        public readonly ushort Reserved1;

        public EhRegionRecord(
            EhRegionKind kind,
            int tryStartPc,
            int tryEndPc,
            int handlerStartPc,
            int handlerEndPc,
            int filterStartPc = -1,
            int catchTypeId = -1,
            int parentRegionIndex = -1,
            int sourceTryStartPc = -1,
            int sourceTryEndPc = -1,
            int sourceHandlerStartPc = -1,
            int sourceHandlerIndex = -1)
        {
            if (tryStartPc < 0 || tryEndPc < tryStartPc) throw new ArgumentOutOfRangeException(nameof(tryStartPc));
            if (handlerStartPc < 0 || handlerEndPc < handlerStartPc) throw new ArgumentOutOfRangeException(nameof(handlerStartPc));
            if (sourceTryStartPc >= 0 && sourceTryEndPc < sourceTryStartPc) throw new ArgumentOutOfRangeException(nameof(sourceTryEndPc));
            Kind = (byte)kind;
            TryStartPc = tryStartPc;
            TryEndPc = tryEndPc;
            HandlerStartPc = handlerStartPc;
            HandlerEndPc = handlerEndPc;
            FilterStartPc = filterStartPc;
            CatchTypeId = catchTypeId;
            ParentRegionIndex = parentRegionIndex;
            SourceTryStartPc = sourceTryStartPc >= 0 ? sourceTryStartPc : tryStartPc;
            SourceTryEndPc = sourceTryEndPc >= 0 ? sourceTryEndPc : tryEndPc;
            SourceHandlerStartPc = sourceHandlerStartPc >= 0 ? sourceHandlerStartPc : handlerStartPc;
            SourceHandlerIndex = sourceHandlerIndex;
            Reserved0 = 0;
            Reserved1 = 0;
        }
    }

    internal enum GcRootKind : byte
    {
        RegisterRef = 1,
        RegisterByRef = 2,
        FrameRef = 3,
        FrameByRef = 4,
        InteriorRegister = 5,
        InteriorFrame = 6,
    }

    [Flags]
    internal enum GcRootFlags : ushort
    {
        None = 0,
        Pinned = 1 << 0,
        ReportOnlyInLeafFunclet = 1 << 1,
        SharedWithParentFrame = 1 << 2,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct GcSafePointRecord
    {
        public readonly int Pc;
        public readonly int RootStartIndex;
        public readonly int RootCount;
        public readonly ushort Flags;
        public readonly ushort Reserved;

        public GcSafePointRecord(int pc, int rootStartIndex, int rootCount, ushort flags = 0)
        {
            if (pc < 0) throw new ArgumentOutOfRangeException(nameof(pc));
            if (rootStartIndex < 0) throw new ArgumentOutOfRangeException(nameof(rootStartIndex));
            if (rootCount < 0) throw new ArgumentOutOfRangeException(nameof(rootCount));
            Pc = pc;
            RootStartIndex = rootStartIndex;
            RootCount = rootCount;
            Flags = flags;
            Reserved = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct GcRootRecord
    {
        public readonly byte Kind;
        public readonly byte Register;
        public readonly ushort Flags;
        public readonly int FrameOffset;
        public readonly int Size;
        public readonly int RuntimeTypeId;
        public readonly int CellOffset;
        public readonly byte FrameBase;
        public readonly byte Reserved0;
        public readonly ushort Reserved1;

        public GcRootRecord(
            GcRootKind kind,
            MachineRegister register,
            int frameOffset,
            int size,
            int runtimeTypeId,
            int cellOffset,
            GcRootFlags flags,
            RegisterFrameBase frameBase)
        {
            if (frameOffset < -1) throw new ArgumentOutOfRangeException(nameof(frameOffset));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (cellOffset < 0) throw new ArgumentOutOfRangeException(nameof(cellOffset));
            bool frameRoot = kind is GcRootKind.FrameRef or GcRootKind.FrameByRef or GcRootKind.InteriorFrame;
            if (frameRoot && frameBase == RegisterFrameBase.None)
                frameBase = RegisterFrameBase.FramePointer;
            Kind = (byte)kind;
            Register = RegisterVmIsa.EncodeRegister(register);
            Flags = (ushort)flags;
            FrameOffset = frameOffset;
            Size = size;
            RuntimeTypeId = runtimeTypeId;
            CellOffset = cellOffset;
            FrameBase = (byte)frameBase;
            Reserved0 = 0;
            Reserved1 = 0;
        }
    }

    internal enum UnwindCodeKind : byte
    {
        AllocateStack = 1,
        SaveReturnAddress = 2,
        SaveCalleeSavedRegister = 3,
        SetFramePointer = 4,
        RestoreCalleeSavedRegister = 5,
        RestoreReturnAddress = 6,
        FreeStack = 7,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct UnwindRecord
    {
        public readonly int Pc;
        public readonly byte Kind;
        public readonly byte Register;
        public readonly ushort Reserved;
        public readonly int StackOffset;
        public readonly int Size;

        public UnwindRecord(int pc, UnwindCodeKind kind, MachineRegister register, int stackOffset, int size)
        {
            if (pc < 0) throw new ArgumentOutOfRangeException(nameof(pc));
            if (stackOffset < 0) throw new ArgumentOutOfRangeException(nameof(stackOffset));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            Pc = pc;
            Kind = (byte)kind;
            Register = RegisterVmIsa.EncodeRegister(register);
            Reserved = 0;
            StackOffset = stackOffset;
            Size = size;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct SwitchTableRecord
    {
        public readonly long Key;
        public readonly int TargetPc;
        public readonly int Reserved;

        public SwitchTableRecord(long key, int targetPc)
        {
            if (targetPc < 0) throw new ArgumentOutOfRangeException(nameof(targetPc));
            Key = key;
            TargetPc = targetPc;
            Reserved = 0;
        }
    }

    [Flags]
    internal enum TypeLayoutFlags : ushort
    {
        None = 0,
        ValueType = 1 << 0,
        ReferenceType = 1 << 1,
        Array = 1 << 2,
        PointerLike = 1 << 3,
        NativeInt = 1 << 4,
        UnsignedSmall = 1 << 5,
        Char = 1 << 6,
        ContainsGcPointers = 1 << 7,
        DelegateLike = 1 << 8,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct VTableSlotRecord
    {
        public readonly int TargetPc;
        public readonly int Reserved;

        public VTableSlotRecord(int targetPc)
        {
            if (targetPc < -1) throw new ArgumentOutOfRangeException(nameof(targetPc));
            TargetPc = targetPc;
            Reserved = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct TypeLayoutRecord
    {
        public readonly int RuntimeTypeId;
        public readonly int ElementTypeLayoutIndex;
        public readonly int Size;
        public readonly int Align;
        public readonly int InstanceSize;
        public readonly int StaticSize;
        public readonly int StaticAlign;
        public readonly int VTableStartIndex;
        public readonly int VTableCount;
        public readonly int DelegateArrayTypeLayoutIndex;
        public readonly int DelegateTargetOffset;
        public readonly int DelegateMethodPtrOffset;
        public readonly int DelegateInvocationListOffset;
        public readonly int DelegateInvocationCountOffset;
        public readonly ushort Flags;
        public readonly ushort Reserved;

        public TypeLayoutRecord(
            int runtimeTypeId,
            int elementTypeLayoutIndex,
            int size,
            int align,
            int instanceSize,
            int staticSize,
            int staticAlign,
            TypeLayoutFlags flags,
            int vTableStartIndex = -1,
            int vTableCount = 0,
            int delegateArrayTypeLayoutIndex = -1,
            int delegateTargetOffset = -1,
            int delegateMethodPtrOffset = -1,
            int delegateInvocationListOffset = -1,
            int delegateInvocationCountOffset = -1)
        {
            if (runtimeTypeId < 0) throw new ArgumentOutOfRangeException(nameof(runtimeTypeId));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (align <= 0) throw new ArgumentOutOfRangeException(nameof(align));
            if (instanceSize < 0) throw new ArgumentOutOfRangeException(nameof(instanceSize));
            if (staticSize < 0) throw new ArgumentOutOfRangeException(nameof(staticSize));
            if (staticAlign <= 0) throw new ArgumentOutOfRangeException(nameof(staticAlign));
            if (vTableStartIndex < -1) throw new ArgumentOutOfRangeException(nameof(vTableStartIndex));
            if (vTableCount < 0) throw new ArgumentOutOfRangeException(nameof(vTableCount));
            if (delegateArrayTypeLayoutIndex < -1) throw new ArgumentOutOfRangeException(nameof(delegateArrayTypeLayoutIndex));
            if (delegateTargetOffset < -1) throw new ArgumentOutOfRangeException(nameof(delegateTargetOffset));
            if (delegateMethodPtrOffset < -1) throw new ArgumentOutOfRangeException(nameof(delegateMethodPtrOffset));
            if (delegateInvocationListOffset < -1) throw new ArgumentOutOfRangeException(nameof(delegateInvocationListOffset));
            if (delegateInvocationCountOffset < -1) throw new ArgumentOutOfRangeException(nameof(delegateInvocationCountOffset));
            RuntimeTypeId = runtimeTypeId;
            ElementTypeLayoutIndex = elementTypeLayoutIndex;
            Size = size;
            Align = align;
            InstanceSize = instanceSize;
            StaticSize = staticSize;
            StaticAlign = staticAlign;
            VTableStartIndex = vTableStartIndex;
            VTableCount = vTableCount;
            DelegateArrayTypeLayoutIndex = delegateArrayTypeLayoutIndex;
            DelegateTargetOffset = delegateTargetOffset;
            DelegateMethodPtrOffset = delegateMethodPtrOffset;
            DelegateInvocationListOffset = delegateInvocationListOffset;
            DelegateInvocationCountOffset = delegateInvocationCountOffset;
            Flags = (ushort)flags;
            Reserved = 0;
        }

        public TypeLayoutFlags LayoutFlags => (TypeLayoutFlags)Flags;
        public bool IsValueType => (LayoutFlags & TypeLayoutFlags.ValueType) != 0;
        public bool IsReferenceType => (LayoutFlags & TypeLayoutFlags.ReferenceType) != 0;
        public bool IsArray => (LayoutFlags & TypeLayoutFlags.Array) != 0;
        public bool IsPointerLike => (LayoutFlags & TypeLayoutFlags.PointerLike) != 0;
        public bool IsNativeInt => (LayoutFlags & TypeLayoutFlags.NativeInt) != 0;
        public bool IsUnsignedSmall => (LayoutFlags & TypeLayoutFlags.UnsignedSmall) != 0;
        public bool IsChar => (LayoutFlags & TypeLayoutFlags.Char) != 0;
        public bool ContainsGcPointers => (LayoutFlags & TypeLayoutFlags.ContainsGcPointers) != 0;
        public bool IsDelegateLike => (LayoutFlags & TypeLayoutFlags.DelegateLike) != 0;
    }

    public sealed class CodeImage
    {
        internal ImageFlags Flags { get; }
        internal ImmutableArray<InstrDesc> Code { get; }
        internal ImmutableArray<MethodRecord> Methods { get; }
        internal ImmutableArray<EhRegionRecord> EhRegions { get; }
        internal ImmutableArray<GcSafePointRecord> GcSafePoints { get; }
        internal ImmutableArray<GcRootRecord> GcRoots { get; }
        internal ImmutableArray<UnwindRecord> Unwind { get; }
        internal ImmutableArray<SwitchTableRecord> SwitchTable { get; }
        internal ImmutableArray<TypeLayoutRecord> TypeLayouts { get; }
        internal ImmutableArray<VTableSlotRecord> VTables { get; }
        internal ImmutableArray<byte> Blob { get; }
        internal IReadOnlyDictionary<int, int> MethodIndexByRuntimeMethodId { get; }
        internal IReadOnlyDictionary<int, int> MethodIndexByEntryPc { get; }
        internal IReadOnlyDictionary<int, int> TypeLayoutIndexByRuntimeTypeId { get; }

        internal CodeImage(
            ImageFlags flags,
            ImmutableArray<InstrDesc> code,
            ImmutableArray<MethodRecord> methods,
            ImmutableArray<EhRegionRecord> ehRegions = default,
            ImmutableArray<GcSafePointRecord> gcSafePoints = default,
            ImmutableArray<GcRootRecord> gcRoots = default,
            ImmutableArray<UnwindRecord> unwind = default,
            ImmutableArray<SwitchTableRecord> switchTable = default,
            ImmutableArray<TypeLayoutRecord> typeLayouts = default,
            ImmutableArray<VTableSlotRecord> vTables = default,
            ImmutableArray<byte> blob = default,
            bool validate = true)
        {
            Flags = flags;
            Code = code.IsDefault ? ImmutableArray<InstrDesc>.Empty : code;
            Methods = methods.IsDefault ? ImmutableArray<MethodRecord>.Empty : methods;
            EhRegions = ehRegions.IsDefault ? ImmutableArray<EhRegionRecord>.Empty : ehRegions;
            GcSafePoints = gcSafePoints.IsDefault ? ImmutableArray<GcSafePointRecord>.Empty : gcSafePoints;
            GcRoots = gcRoots.IsDefault ? ImmutableArray<GcRootRecord>.Empty : gcRoots;
            Unwind = unwind.IsDefault ? ImmutableArray<UnwindRecord>.Empty : unwind;
            SwitchTable = switchTable.IsDefault ? ImmutableArray<SwitchTableRecord>.Empty : switchTable;
            TypeLayouts = typeLayouts.IsDefault ? ImmutableArray<TypeLayoutRecord>.Empty : typeLayouts;
            VTables = vTables.IsDefault ? ImmutableArray<VTableSlotRecord>.Empty : vTables;
            Blob = blob.IsDefault ? ImmutableArray<byte>.Empty : blob;

            var typeMap = new Dictionary<int, int>(TypeLayouts.Length);
            for (int i = 0; i < TypeLayouts.Length; i++)
            {
                if (!typeMap.TryAdd(TypeLayouts[i].RuntimeTypeId, i))
                    throw new InvalidOperationException($"Duplicate runtime type id in RVM layout table: {TypeLayouts[i].RuntimeTypeId}");
            }
            TypeLayoutIndexByRuntimeTypeId = typeMap;

            var entryPcMap = new Dictionary<int, int>(Methods.Length);
            for (int i = 0; i < Methods.Length; i++)
            {
                if (!entryPcMap.TryAdd(Methods[i].EntryPc, i))
                    throw new InvalidOperationException($"Duplicate method entry PC in RVM image: {Methods[i].EntryPc}");
            }
            MethodIndexByEntryPc = entryPcMap;


            var map = new Dictionary<int, int>(Methods.Length);
            for (int i = 0; i < Methods.Length; i++)
            {
                int runtimeMethodId = Methods[i].RuntimeMethodId;
                if (runtimeMethodId < 0)
                    continue;
                if (!map.TryAdd(runtimeMethodId, i))
                    throw new InvalidOperationException($"Duplicate runtime method id in RVM method body map: {runtimeMethodId}");
            }
            MethodIndexByRuntimeMethodId = map;

            if (validate)
                Validate();
        }

        internal MethodRecord GetMethod(int runtimeMethodId)
        {
            if (!MethodIndexByRuntimeMethodId.TryGetValue(runtimeMethodId, out int index))
                throw new KeyNotFoundException($"Runtime method id was not found in RVM image: {runtimeMethodId}");
            return Methods[index];
        }

        internal MethodRecord GetMethodByEntryPc(int entryPc)
        {
            if (!MethodIndexByEntryPc.TryGetValue(entryPc, out int index))
                throw new KeyNotFoundException($"Method entry PC was not found in RVM image: {entryPc}");
            return Methods[index];
        }

        internal bool TryGetTypeLayoutIndexByRuntimeTypeId(int runtimeTypeId, out int index)
            => TypeLayoutIndexByRuntimeTypeId.TryGetValue(runtimeTypeId, out index);

        internal int GetMethodEndPc(int methodIndex)
        {
            if ((uint)methodIndex >= (uint)Methods.Length)
                throw new ArgumentOutOfRangeException(nameof(methodIndex));

            int entryPc = Methods[methodIndex].EntryPc;
            int endPc = methodIndex + 1 < Methods.Length ? Methods[methodIndex + 1].EntryPc : Code.Length;
            if (endPc < entryPc)
                throw new InvalidOperationException("RVM method records are not sorted by entry PC.");
            return endPc;
        }

        internal int GetMethodRuntimeMethodId(int methodIndex)
        {
            if ((uint)methodIndex >= (uint)Methods.Length)
                throw new ArgumentOutOfRangeException(nameof(methodIndex));
            return Methods[methodIndex].RuntimeMethodId;
        }

        internal void Validate()
        {
            int[] methodByPc = new int[Code.Length];
            for (int i = 0; i < methodByPc.Length; i++)
                methodByPc[i] = -1;

            for (int i = 0; i < Methods.Length; i++)
            {
                var m = Methods[i];
                int endPc = GetMethodEndPc(i);
                int runtimeMethodId = GetMethodRuntimeMethodId(i);
                if (m.EntryPc < 0 || endPc <= m.EntryPc || m.EntryPc > Code.Length || endPc > Code.Length)
                    throw new InvalidOperationException($"Method has invalid code range: {runtimeMethodId}");
                if (m.FrameSize < 0)
                    throw new InvalidOperationException($"Method has invalid frame size: {runtimeMethodId}");
                if (m.RuntimeMethodId < -1)
                    throw new InvalidOperationException($"Method has invalid runtime method id: {m.RuntimeMethodId}");
                if (m.StaticConstructorTypeLayoutIndex >= 0)
                    CheckRange(m.StaticConstructorTypeLayoutIndex, 1, TypeLayouts.Length, "static constructor type layout marker");
                CheckRange(m.EhStartIndex, m.EhCount, EhRegions.Length, "method EH range");
                CheckRange(m.GcSafePointStartIndex, m.GcSafePointCount, GcSafePoints.Length, "method GC safepoint range");
                CheckRange(m.UnwindStartIndex, m.UnwindCount, Unwind.Length, "method unwind range");

                for (int pc = m.EntryPc; pc < endPc; pc++)
                {
                    if (methodByPc[pc] >= 0)
                        throw new InvalidOperationException($"RVM method code ranges overlap at PC {pc}");
                    methodByPc[pc] = i;
                }
            }

            for (int pc = 0; pc < Code.Length; pc++)
            {
                int methodIndex = methodByPc[pc];
                if (methodIndex < 0)
                    throw new InvalidOperationException($"Instruction is outside every method at PC {pc}");

                var inst = Code[pc];
                ValidateInstructionOperands(pc, inst);
                ValidateInstructionLayoutOperand(pc, inst);

                if (IsPcTargetInstruction(inst.Op))
                {
                    int targetPc = CheckedPc(inst.Imm, pc, "branch target");
                    ValidateSameMethodTarget(pc, targetPc, methodIndex, methodByPc);
                }
                else if (IsSwitchInstruction(inst.Op))
                {
                    ValidateSwitch(pc, inst, methodIndex, methodByPc);
                }
            }

            ValidateExecutionLayoutTables();


            for (int i = 0; i < VTables.Length; i++)
            {
                int targetPc = VTables[i].TargetPc;
                if (targetPc >= 0 && !MethodIndexByEntryPc.ContainsKey(targetPc))
                    throw new InvalidOperationException($"VTable entry #{i} target is not a method entry PC: {targetPc}");
            }

            ValidateMethodSideTables(methodByPc);
        }


        private void ValidateExecutionLayoutTables()
        {
            for (int i = 0; i < TypeLayouts.Length; i++)
            {
                var t = TypeLayouts[i];
                if (t.RuntimeTypeId < 0)
                    throw new InvalidOperationException("Type layout has invalid runtime type id.");
                if (t.Size < 0 || t.Align <= 0 || (t.Align & (t.Align - 1)) != 0)
                    throw new InvalidOperationException("Type layout has invalid size or alignment.");
                if (t.StaticSize < 0 || t.StaticAlign <= 0 || (t.StaticAlign & (t.StaticAlign - 1)) != 0)
                    throw new InvalidOperationException("Type layout has invalid static size or alignment.");
                if (t.ElementTypeLayoutIndex >= 0)
                    CheckRange(t.ElementTypeLayoutIndex, 1, TypeLayouts.Length, "type layout element range");
                if (t.VTableStartIndex >= 0 || t.VTableCount != 0)
                    CheckRange(t.VTableStartIndex, t.VTableCount, VTables.Length, "type layout vtable range");
            }
        }

        private void ValidateInstructionLayoutOperand(int pc, InstrDesc inst)
        {
            if (IsInstanceFieldMemoryInstruction(inst.Op))
            {
                ValidateFieldImmediate(pc, inst);
                return;
            }


            if (inst.Op == Op.LdVTableEntry)
            {
                if (inst.Imm < 0 || inst.Imm > int.MaxValue)
                    throw new InvalidOperationException($"Invalid vtable slot at PC {pc}");
                return;
            }

            if (IsTypeLayoutInstruction(inst.Op))
            {
                ValidateTableIndex(inst.Imm, TypeLayouts.Length, pc, "type layout");
                return;
            }

            if (IsArrayElementLayoutInstruction(inst.Op))
            {
                ValidateTableIndex(inst.Imm, TypeLayouts.Length, pc, "array element type layout");
                return;
            }

            if (IsNonInternalDirectCallInstruction(inst.Op))
            {
                int targetPc = CheckedPc(inst.Imm, pc, "direct call target");
                if (!MethodIndexByEntryPc.ContainsKey(targetPc))
                    throw new InvalidOperationException($"Direct call target is not a method entry PC at PC {pc}");
                return;
            }

            if (IsInternalDirectCall(inst.Op))
            {
                if (inst.Imm < 0 || inst.Imm > int.MaxValue)
                    throw new InvalidOperationException($"Invalid internal-call runtime method id at PC {pc}");
                _ = checked((int)inst.Imm);
                return;
            }

            if (inst.Op == Op.AllocObj)
            {
                ValidateTableIndex(inst.Imm, TypeLayouts.Length, pc, "object allocation type layout");
                return;
            }

        }

        private static void ValidateTableIndex(long value, int length, int pc, string name)
        {
            if (value < 0 || value > int.MaxValue || (int)value >= length)
                throw new InvalidOperationException($"Invalid {name} index at PC {pc}");
        }

        private static bool IsInstanceFieldMemoryInstruction(Op op)
            => IsInstanceFieldInstruction(op);

        private static void ValidateFieldImmediate(int pc, InstrDesc inst)
        {
            int offset = InstrDesc.FieldOffset(inst.Imm);
            int size = InstrDesc.FieldSize(inst.Imm);
            if (offset < 0)
                throw new InvalidOperationException($"Invalid field offset at PC {pc}");
            if (size < 0)
                throw new InvalidOperationException($"Invalid field size at PC {pc}");
            if ((Aux.FieldInstructionFlags(inst.Aux) & ~(InstructionFlags.MayThrow | InstructionFlags.WriteBarrier | InstructionFlags.Volatile | InstructionFlags.Unaligned | InstructionFlags.NoNullCheck)) != 0)
                throw new InvalidOperationException($"Invalid field instruction flags at PC {pc}");
        }

        private static bool IsTypeLayoutInstruction(Op op)
            => op is Op.LiStaticBase or Op.CpObj or Op.NewArr or Op.NewSZArray or Op.Box or Op.UnboxAny or Op.UnboxAddr
                or Op.SizeOf or Op.InitObj or Op.DefaultValue;

        private static bool IsArrayElementLayoutInstruction(Op op)
            => IsArrayInstruction(op) && op is not (Op.LdLen or Op.LdArrayDataAddr);

        private static bool IsNonInternalDirectCallInstruction(Op op)
            => op is Op.CallVoid or Op.CallI or Op.CallF or Op.CallRef or Op.CallValue;

        private static bool IsInternalDirectCall(Op op)
            => IsOpInRange(op, Op.CallInternalVoid, Op.CallInternalValue);


        private void ValidateMethodSideTables(int[] methodByPc)
        {
            for (int methodIndex = 0; methodIndex < Methods.Length; methodIndex++)
            {
                var method = Methods[methodIndex];
                int methodEndPc = GetMethodEndPc(methodIndex);

                for (int i = 0; i < method.EhCount; i++)
                {
                    int regionIndex = method.EhStartIndex + i;
                    var region = EhRegions[regionIndex];
                    ValidatePcRangeInMethod(region.TryStartPc, region.TryEndPc, method.EntryPc, methodEndPc, "EH try range");
                    ValidatePcRangeInMethod(region.HandlerStartPc, region.HandlerEndPc, method.EntryPc, methodEndPc, "EH handler range");
                    if (region.FilterStartPc >= 0 && (region.FilterStartPc < method.EntryPc || region.FilterStartPc >= methodEndPc))
                        throw new InvalidOperationException("EH filter starts outside its method.");
                    if (region.ParentRegionIndex >= 0 && region.ParentRegionIndex >= method.EhCount)
                        throw new InvalidOperationException("EH region has invalid method-local parent region index.");
                }

                for (int i = 0; i < method.GcSafePointCount; i++)
                {
                    int safePointIndex = method.GcSafePointStartIndex + i;
                    var safePoint = GcSafePoints[safePointIndex];
                    if (safePoint.Pc < method.EntryPc || safePoint.Pc >= methodEndPc || methodByPc[safePoint.Pc] != methodIndex)
                        throw new InvalidOperationException("GC safepoint is outside its method.");
                    CheckRange(safePoint.RootStartIndex, safePoint.RootCount, GcRoots.Length, "GC root range");
                    for (int rootIndex = 0; rootIndex < safePoint.RootCount; rootIndex++)
                        ValidateGcRoot(GcRoots[safePoint.RootStartIndex + rootIndex], method);
                }

                for (int i = 0; i < method.UnwindCount; i++)
                {
                    var unwind = Unwind[method.UnwindStartIndex + i];
                    if (unwind.Pc < method.EntryPc || unwind.Pc >= methodEndPc || methodByPc[unwind.Pc] != methodIndex)
                        throw new InvalidOperationException("Unwind record is outside its method.");
                    if (unwind.StackOffset + unwind.Size > method.FrameSize)
                        throw new InvalidOperationException("Unwind record exceeds method frame size.");
                }
            }
        }

        private static void ValidateGcRoot(GcRootRecord root, MethodRecord method)
        {
            var kind = (GcRootKind)root.Kind;
            bool registerRoot = kind == GcRootKind.RegisterRef || kind == GcRootKind.RegisterByRef || kind == GcRootKind.InteriorRegister;
            bool frameRoot = kind == GcRootKind.FrameRef || kind == GcRootKind.FrameByRef || kind == GcRootKind.InteriorFrame;

            if (!registerRoot && !frameRoot)
                throw new InvalidOperationException("Invalid GC root kind.");

            if (registerRoot)
            {
                if (root.Register == RegisterVmIsa.InvalidRegister || !RegisterVmIsa.IsIntegerRegister(root.Register))
                    throw new InvalidOperationException("GC register root must use a GPR.");
                if (root.CellOffset != 0)
                    throw new InvalidOperationException("GC register root must not use a frame-cell offset.");
            }
            else
            {
                if (root.Register != RegisterVmIsa.InvalidRegister)
                    throw new InvalidOperationException("GC frame root must not name a register.");
                var frameBase = (RegisterFrameBase)root.FrameBase;
                if (frameBase is not (RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer or RegisterFrameBase.IncomingArgumentBase))
                    throw new InvalidOperationException("GC frame root has invalid frame base.");
                if (root.FrameOffset < 0)
                    throw new InvalidOperationException("GC frame root has negative offset.");
                if (root.CellOffset < 0)
                    throw new InvalidOperationException("GC frame root has negative cell offset.");
                if (frameBase is RegisterFrameBase.StackPointer or RegisterFrameBase.FramePointer)
                {
                    if (root.FrameOffset + root.CellOffset + root.Size > method.FrameSize)
                        throw new InvalidOperationException("GC frame root exceeds method frame size.");
                }
            }
        }

        private void ValidateSwitch(int pc, InstrDesc inst, int methodIndex, int[] methodByPc)
        {
            int tableStart = CheckedPc(inst.Imm, pc, "switch table start");
            int tableCount = inst.Aux;
            CheckRange(tableStart, tableCount, SwitchTable.Length, "switch table range");
            for (int i = 0; i < tableCount; i++)
                ValidateSameMethodTarget(pc, SwitchTable[tableStart + i].TargetPc, methodIndex, methodByPc);
        }

        private static void ValidateSameMethodTarget(int sourcePc, int targetPc, int sourceMethodIndex, int[] methodByPc)
        {
            if (targetPc < 0 || targetPc >= methodByPc.Length)
                throw new InvalidOperationException($"Instruction has invalid target PC at {sourcePc}");
            if (methodByPc[targetPc] != sourceMethodIndex)
                throw new InvalidOperationException($"Instruction target leaves method without a call/return/EH transfer at PC {sourcePc}");
        }

        private static void ValidatePcRangeInMethod(int startPc, int endPc, int methodStartPc, int methodEndPc, string name)
        {
            if (startPc < methodStartPc || startPc > methodEndPc || endPc < startPc || endPc > methodEndPc)
                throw new InvalidOperationException("Invalid " + name + ".");
        }

        private static void CheckRange(int start, int count, int length, string name)
        {
            if (start < 0 || count < 0 || start > length || count > length - start)
                throw new InvalidOperationException("Invalid " + name + ".");
        }

        private static int CheckedPc(long value, int sourcePc, string name)
        {
            if (value < 0 || value > int.MaxValue)
                throw new InvalidOperationException($"Invalid {name} at PC {sourcePc}");
            return (int)value;
        }

        private static void ValidateInstructionOperands(int pc, InstrDesc inst)
        {
            if (!Enum.IsDefined(typeof(Op), inst.Op))
                throw new InvalidOperationException($"Invalid RVM opcode at PC {pc}");

            switch (inst.Op)
            {
                case Op.RetF:
                    RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                    break;
                case Op.RetI:
                case Op.RetRef:
                case Op.RetValue:
                case Op.SwitchI32:
                case Op.SwitchI64:
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    break;
            }

            if (IsMoveInstruction(inst.Op))
            {
                ValidateMoveInstruction(pc, inst);
            }
            else if (IsLoadImmediateInstruction(inst.Op))
            {
                ValidateLoadImmediateInstruction(pc, inst);
            }
            else if (IsConversionInstruction(inst.Op))
            {
                ValidateConversionInstruction(pc, inst);
            }
            else if (IsIntegerImmediate(inst.Op))
            {
                RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
            }
            else if (IsIntegerBinaryOrCompare(inst.Op))
            {
                RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                if (!IsIntegerUnary(inst.Op)) RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsFloatArithmetic(inst.Op))
            {
                RequireFpr(inst.Rd, pc, nameof(inst.Rd));
                RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                if (!IsFloatUnary(inst.Op)) RequireFpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsFloatCompare(inst.Op))
            {
                RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                RequireFpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsFloatPredicate(inst.Op))
            {
                RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
            }
            else if (IsAddressMemoryInstruction(inst.Op))
            {
                ValidateAddressMemoryInstruction(pc, inst);
            }
            else if (IsInstanceFieldInstruction(inst.Op))
            {
                RequireValueRegister(inst.Op, inst.Rd, pc, nameof(inst.Rd));
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
            }
            else if (IsArrayInstruction(inst.Op))
            {
                RequireValueRegister(inst.Op, inst.Rd, pc, nameof(inst.Rd));
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                if (inst.Op != Op.LdLen && inst.Op != Op.LdArrayDataAddr)
                    RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsIntegerBranch(inst.Op) || IsReferenceBranch(inst.Op))
            {
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                if (IsTwoOperandBranch(inst.Op)) RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsFloatBranch(inst.Op))
            {
                RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                RequireFpr(inst.Rs2, pc, nameof(inst.Rs2));
            }
            else if (IsRuntimeObjectInstruction(inst.Op))
            {
                ValidateRuntimeObjectInstruction(pc, inst);
            }
            else if (IsPointerInstruction(inst.Op))
            {
                ValidatePointerInstruction(pc, inst);
            }
            else if (IsIndirectCall(inst.Op))
            {
                RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
            }
        }

        private static void ValidateMoveInstruction(int pc, InstrDesc inst)
        {
            if (inst.Op == Op.MovF)
            {
                RequireFpr(inst.Rd, pc, nameof(inst.Rd));
                RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                return;
            }

            RequireGpr(inst.Rd, pc, nameof(inst.Rd));
            RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
        }

        private static void ValidateLoadImmediateInstruction(int pc, InstrDesc inst)
        {
            if (inst.Op is Op.LiF32Bits or Op.LiF64Bits)
                RequireFpr(inst.Rd, pc, nameof(inst.Rd));
            else
                RequireGpr(inst.Rd, pc, nameof(inst.Rd));
        }

        private static void ValidateConversionInstruction(int pc, InstrDesc inst)
        {
            switch (inst.Op)
            {
                case Op.I32ToF32:
                case Op.I32ToF64:
                case Op.U32ToF32:
                case Op.U32ToF64:
                case Op.I64ToF32:
                case Op.I64ToF64:
                case Op.U64ToF32:
                case Op.U64ToF64:
                case Op.BitcastI32F32:
                case Op.BitcastI64F64:
                    RequireFpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.F32ToF64:
                case Op.F64ToF32:
                    RequireFpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.F32ToI32:
                case Op.F32ToI64:
                case Op.F64ToI32:
                case Op.F64ToI64:
                case Op.F32ToI32Ovf:
                case Op.F32ToI64Ovf:
                case Op.F64ToI32Ovf:
                case Op.F64ToI64Ovf:
                case Op.BitcastF32I32:
                case Op.BitcastF64I64:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireFpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                default:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;
            }
        }

        private static void ValidateRuntimeObjectInstruction(int pc, InstrDesc inst)
        {
            switch (inst.Op)
            {
                case Op.AllocObj:
                case Op.NewDelegate:
                case Op.SizeOf:
                case Op.DefaultValue:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    return;

                case Op.NewDelegateClosed:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.DelegateCombine:
                case Op.DelegateRemove:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
                    return;

                case Op.LdVTableEntry:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.NewArr:
                case Op.NewSZArray:
                case Op.FastAllocateString:
                case Op.CastClass:
                case Op.IsInst:
                case Op.UnboxAddr:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.Box:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireAnyRegister(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.UnboxAny:
                    RequireAnyRegister(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.InitObj:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    return;

                case Op.RefEq:
                case Op.RefNe:
                case Op.RuntimeTypeEquals:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
                    return;

                default:
                    return;
            }
        }

        private static void ValidatePointerInstruction(int pc, InstrDesc inst)
        {
            switch (inst.Op)
            {
                case Op.StaticData:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    return;
                case Op.FreeHGlobal:
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;
                case Op.StackAlloc:
                case Op.AllocHGlobal:
                case Op.ByRefToPtr:
                case Op.PtrToByRef:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.PtrAddI32:
                case Op.PtrAddI64:
                case Op.PtrSub:
                case Op.PtrDiff:
                case Op.ByRefAddI32:
                case Op.ByRefAddI64:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
                    return;

                default:
                    throw new InvalidOperationException($"Invalid pointer opcode at PC {pc}");
            }
        }

        private static void ValidateAddressMemoryInstruction(int pc, InstrDesc inst)
        {
            switch (inst.Op)
            {
                case Op.CpObj:
                case Op.CpBlk:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.InitObj:
                case Op.InitBlk:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    return;

                case Op.NullCheck:
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                case Op.BoundsCheck:
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
                    return;

                case Op.WriteBarrier:
                    RequireGpr(inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    return;

                default:
                    RequireValueRegister(inst.Op, inst.Rd, pc, nameof(inst.Rd));
                    RequireGpr(inst.Rs1, pc, nameof(inst.Rs1));
                    if (inst.Rs2 != RegisterVmIsa.InvalidRegister) RequireGpr(inst.Rs2, pc, nameof(inst.Rs2));
                    return;
            }
        }

        private static void RequireValueRegister(Op op, byte register, int pc, string operand)
        {
            if (IsFloatValueInstruction(op))
                RequireFpr(register, pc, operand);
            else
                RequireGpr(register, pc, operand);
        }

        private static void RequireGpr(byte register, int pc, string operand)
        {
            if (!RegisterVmIsa.IsIntegerRegister(register))
                throw new InvalidOperationException($"Expected GPR for {operand} at PC {pc}");
        }

        private static void RequireFpr(byte register, int pc, string operand)
        {
            if (!RegisterVmIsa.IsFloatRegister(register))
                throw new InvalidOperationException($"Expected FPR for {operand} at PC {pc}");
        }

        private static void RequireAnyRegister(byte register, int pc, string operand)
        {
            if (!RegisterVmIsa.IsIntegerRegister(register) && !RegisterVmIsa.IsFloatRegister(register))
                throw new InvalidOperationException($"Expected register for {operand} at PC {pc}");
        }

        private static bool IsPcTargetInstruction(Op op)
            => op is Op.J or Op.Leave or Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64 or Op.BrTrueRef or Op.BrFalseRef
                or Op.BrI32Eq or Op.BrI32Ne or Op.BrI32Lt or Op.BrI32Le or Op.BrI32Gt or Op.BrI32Ge
                or Op.BrU32Lt or Op.BrU32Le or Op.BrU32Gt or Op.BrU32Ge
                or Op.BrI64Eq or Op.BrI64Ne or Op.BrI64Lt or Op.BrI64Le or Op.BrI64Gt or Op.BrI64Ge
                or Op.BrU64Lt or Op.BrU64Le or Op.BrU64Gt or Op.BrU64Ge
                or Op.BrRefEq or Op.BrRefNe
                or Op.BrF32Eq or Op.BrF32Ne or Op.BrF32Lt or Op.BrF32Le or Op.BrF32Gt or Op.BrF32Ge
                or Op.BrF64Eq or Op.BrF64Ne or Op.BrF64Lt or Op.BrF64Le or Op.BrF64Gt or Op.BrF64Ge;

        private static bool IsCallInstruction(Op op)
            => IsOpInRange(op, Op.CallVoid, Op.CallIndirectValue);

        private static bool IsSwitchInstruction(Op op)
            => op is Op.SwitchI32 or Op.SwitchI64;

        private static bool IsMoveInstruction(Op op)
            => op is Op.MovI or Op.MovF or Op.MovRef or Op.MovPtr;

        private static bool IsLoadImmediateInstruction(Op op)
            => op is Op.LiI32 or Op.LiI64 or Op.LiF32Bits or Op.LiF64Bits or Op.LiNull or Op.LiString
                or Op.LiTypeHandle or Op.LiMethodHandle or Op.LiFieldHandle or Op.LiStaticBase;

        private static bool IsConversionInstruction(Op op)
            => IsOpInRange(op, Op.I32ToI64, Op.U64ToU32Ovf);

        private static bool IsRuntimeObjectInstruction(Op op)
            => IsOpInRange(op, Op.AllocObj, Op.DelegateRemove);

        private static bool IsPointerInstruction(Op op)
            => IsOpInRange(op, Op.StackAlloc, Op.PtrToByRef);

        private static bool IsIndirectCall(Op op)
            => IsOpInRange(op, Op.CallIndirectVoid, Op.CallIndirectValue);

        private static bool IsIntegerImmediate(Op op)
            => op is Op.I32AddImm or Op.I32SubImm or Op.I32MulImm or Op.I32AndImm or Op.I32OrImm or Op.I32XorImm
                or Op.I32ShlImm or Op.I32ShrImm or Op.U32ShrImm or Op.I32EqImm or Op.I32NeImm or Op.I32LtImm
                or Op.I32LeImm or Op.I32GtImm or Op.I32GeImm or Op.U32LtImm
                or Op.I64AddImm or Op.I64SubImm or Op.I64MulImm or Op.I64AndImm or Op.I64OrImm or Op.I64XorImm
                or Op.I64ShlImm or Op.I64ShrImm or Op.U64ShrImm or Op.I64EqImm or Op.I64NeImm or Op.I64LtImm
                or Op.I64LeImm or Op.I64GtImm or Op.I64GeImm or Op.U64LtImm;

        private static bool IsIntegerBinaryOrCompare(Op op)
            => IsOpInRange(op, Op.I32Add, Op.U32Max) || IsOpInRange(op, Op.I64Add, Op.U64Max);

        private static bool IsIntegerUnary(Op op)
            => op is Op.I32Neg or Op.I32Not or Op.I64Neg or Op.I64Not;

        private static bool IsFloatArithmetic(Op op)
            => op is Op.F32Add or Op.F32Sub or Op.F32Mul or Op.F32Div or Op.F32Rem or Op.F32Neg or Op.F32Abs or Op.F32Min or Op.F32Max
                or Op.F64Add or Op.F64Sub or Op.F64Mul or Op.F64Div or Op.F64Rem or Op.F64Neg or Op.F64Abs or Op.F64Min or Op.F64Max;

        private static bool IsFloatUnary(Op op)
            => op is Op.F32Neg or Op.F32Abs or Op.F64Neg or Op.F64Abs;

        private static bool IsFloatCompare(Op op)
            => op is Op.F32Eq or Op.F32Ne or Op.F32Lt or Op.F32Le or Op.F32Gt or Op.F32Ge
                or Op.F64Eq or Op.F64Ne or Op.F64Lt or Op.F64Le or Op.F64Gt or Op.F64Ge;

        private static bool IsFloatPredicate(Op op)
            => op is Op.F32IsNaN or Op.F32IsFinite or Op.F64IsNaN or Op.F64IsFinite;

        private static bool IsFloatValueInstruction(Op op)
            => op is Op.LdF32 or Op.LdF64 or Op.StF32 or Op.StF64
                or Op.LdFldF32 or Op.LdFldF64 or Op.StFldF32 or Op.StFldF64
                or Op.LdElemF32 or Op.LdElemF64 or Op.StElemF32 or Op.StElemF64;

        private static bool IsAddressMemoryInstruction(Op op)
            => IsOpInRange(op, Op.LdI1, Op.WriteBarrier);

        private static bool IsInstanceFieldInstruction(Op op)
            => IsOpInRange(op, Op.LdFldI1, Op.StFldObj);


        private static bool IsArrayInstruction(Op op)
            => IsOpInRange(op, Op.LdLen, Op.LdArrayDataAddr);

        private static bool IsOpInRange(Op op, Op first, Op last)
            => (ushort)op >= (ushort)first && (ushort)op <= (ushort)last;

        private static bool IsIntegerBranch(Op op)
            => op is Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64
                or Op.BrI32Eq or Op.BrI32Ne or Op.BrI32Lt or Op.BrI32Le or Op.BrI32Gt or Op.BrI32Ge
                or Op.BrU32Lt or Op.BrU32Le or Op.BrU32Gt or Op.BrU32Ge
                or Op.BrI64Eq or Op.BrI64Ne or Op.BrI64Lt or Op.BrI64Le or Op.BrI64Gt or Op.BrI64Ge
                or Op.BrU64Lt or Op.BrU64Le or Op.BrU64Gt or Op.BrU64Ge;

        private static bool IsReferenceBranch(Op op)
            => op is Op.BrTrueRef or Op.BrFalseRef or Op.BrRefEq or Op.BrRefNe;

        private static bool IsFloatBranch(Op op)
            => op is Op.BrF32Eq or Op.BrF32Ne or Op.BrF32Lt or Op.BrF32Le or Op.BrF32Gt or Op.BrF32Ge
                or Op.BrF64Eq or Op.BrF64Ne or Op.BrF64Lt or Op.BrF64Le or Op.BrF64Gt or Op.BrF64Ge;

        private static bool IsTwoOperandBranch(Op op)
            => op is not (Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64 or Op.BrTrueRef or Op.BrFalseRef);
    }

    internal readonly struct Label : IEquatable<Label>
    {
        public readonly int Id;
        internal Label(int id) { Id = id; }
        public bool Equals(Label other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is Label other && Equals(other);
        public override int GetHashCode() => Id;
        public override string ToString() => $"L{Id}";
    }

    internal sealed class Assembler
    {
        private readonly List<InstrDesc> _code = new List<InstrDesc>();
        private readonly List<MethodDraft> _methods = new List<MethodDraft>();
        private readonly List<EhRegionRecord> _eh = new List<EhRegionRecord>();
        private readonly List<GcSafePointRecord> _gcSafePoints = new List<GcSafePointRecord>();
        private readonly List<GcRootRecord> _gcRoots = new List<GcRootRecord>();
        private readonly List<UnwindRecord> _unwind = new List<UnwindRecord>();
        private readonly List<SwitchTableRecord> _switchTable = new List<SwitchTableRecord>();
        private readonly List<TypeLayoutRecord> _typeLayouts = new List<TypeLayoutRecord>();
        private readonly List<RuntimeType> _typeLayoutTypes = new List<RuntimeType>();
        private readonly List<VTableSlotRecord> _vTables = new List<VTableSlotRecord>();
        private readonly List<RuntimeMethod> _interfaceDispatchMethods = new List<RuntimeMethod>();
        private readonly Dictionary<int, int> _typeLayoutByRuntimeTypeId = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _interfaceDispatchSlotByMethodId = new Dictionary<int, int>();
        private readonly List<byte> _blob = new List<byte>();
        private readonly List<int> _labelPc = new List<int>();
        private readonly List<Fixup> _fixups = new List<Fixup>();
        private MethodDraft? _currentMethod;
        private readonly RuntimeTypeSystem? _rts;

        public Assembler(RuntimeTypeSystem? rts)
        {
            _rts = rts;
        }

        public int Pc => _code.Count;

        public Label CreateLabel()
        {
            int id = _labelPc.Count;
            _labelPc.Add(-1);
            return new Label(id);
        }

        public void Bind(Label label)
        {
            ValidateLabel(label);
            if (_labelPc[label.Id] >= 0)
                throw new InvalidOperationException($"Label is already bound: {label}");
            _labelPc[label.Id] = Pc;
        }

        public void BeginMethod(int runtimeMethodId, int staticConstructorTypeLayoutIndex = -1, StackFrameLayout? frame = null, ushort flags = 0)
        {
            if (_currentMethod is not null)
                throw new InvalidOperationException("Previous RVM method is not ended.");
            if (runtimeMethodId < -1)
                throw new ArgumentOutOfRangeException(nameof(runtimeMethodId));
            if (staticConstructorTypeLayoutIndex < -1)
                throw new ArgumentOutOfRangeException(nameof(staticConstructorTypeLayoutIndex));
            frame ??= StackFrameLayout.Empty;
            _currentMethod = new MethodDraft(runtimeMethodId, staticConstructorTypeLayoutIndex, Pc, frame, flags, _eh.Count, _gcSafePoints.Count, _unwind.Count);
        }

        public void BeginMethod(int runtimeMethodId, Label entryLabel, int staticConstructorTypeLayoutIndex = -1, StackFrameLayout? frame = null, ushort flags = 0)
        {
            ValidateLabel(entryLabel);
            if (_labelPc[entryLabel.Id] >= 0)
                throw new InvalidOperationException($"Method entry label is already bound: {entryLabel}");

            Bind(entryLabel);
            BeginMethod(runtimeMethodId, staticConstructorTypeLayoutIndex, frame, flags);
        }

        public void EndMethod()
        {
            if (_currentMethod is null)
                throw new InvalidOperationException("No active RVM method.");
            _currentMethod.EndPc = Pc;
            _currentMethod.EhEndIndex = _eh.Count;
            _currentMethod.GcSafePointEndIndex = _gcSafePoints.Count;
            _currentMethod.UnwindEndIndex = _unwind.Count;
            _methods.Add(_currentMethod);
            _currentMethod = null;
        }

        public int InternTypeLayout(RuntimeType type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            _rts?.EnsureRuntimeTypeReady(type);
            if (_typeLayoutByRuntimeTypeId.TryGetValue(type.TypeId, out int existing))
                return existing;

            int elementIndex = -1;
            if (type.ElementType is not null)
                elementIndex = InternTypeLayout(type.ElementType);

            TypeLayoutFlags flags = TypeLayoutFlags.None;
            if (type.IsValueType) flags |= TypeLayoutFlags.ValueType;
            if (type.IsReferenceType) flags |= TypeLayoutFlags.ReferenceType;
            if (type.Kind == RuntimeTypeKind.Array) flags |= TypeLayoutFlags.Array;
            if (type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam) flags |= TypeLayoutFlags.PointerLike;
            if (type.PrimitiveKind is RuntimePrimitiveKind.NativeInt or RuntimePrimitiveKind.NativeUInt) flags |= TypeLayoutFlags.NativeInt;
            if (type.PrimitiveKind is RuntimePrimitiveKind.UInt8 or RuntimePrimitiveKind.UInt16 or RuntimePrimitiveKind.UInt32 or RuntimePrimitiveKind.UInt64 or RuntimePrimitiveKind.NativeUInt) flags |= TypeLayoutFlags.UnsignedSmall;
            if (type.PrimitiveKind == RuntimePrimitiveKind.Char) flags |= TypeLayoutFlags.Char;
            if (type.ContainsGcPointers) flags |= TypeLayoutFlags.ContainsGcPointers;

            bool isDelegateLike = IsDelegateLikeRuntimeType(type);
            if (isDelegateLike)
                flags |= TypeLayoutFlags.DelegateLike;

            int delegateTargetOffset = -1;
            int delegateMethodPtrOffset = -1;
            int delegateInvocationListOffset = -1;
            int delegateInvocationCountOffset = -1;
            if (isDelegateLike)
            {
                delegateTargetOffset = FindInstanceFieldOffsetInHierarchy(type, "_target");
                delegateMethodPtrOffset = FindInstanceFieldOffsetInHierarchy(type, "_methodPtr");
                delegateInvocationListOffset = FindInstanceFieldOffsetInHierarchy(type, "_invocationList");
                delegateInvocationCountOffset = FindInstanceFieldOffsetInHierarchy(type, "_invocationCount");
            }

            int size = StorageSizeOfForLayout(type);
            int align = StorageAlignOfForLayout(type);
            int instanceSize = InstanceSizeOfForLayout(
                type,
                isDelegateLike,
                delegateTargetOffset,
                delegateMethodPtrOffset,
                delegateInvocationListOffset,
                delegateInvocationCountOffset);
            int index = _typeLayouts.Count;
            _typeLayoutByRuntimeTypeId.Add(type.TypeId, index);
            _typeLayoutTypes.Add(type);
            _typeLayouts.Add(new TypeLayoutRecord(
                type.TypeId,
                elementIndex,
                size,
                align,
                instanceSize,
                Math.Max(0, type.StaticSize),
                Math.Max(1, type.StaticAlign),
                flags,
                -1,
                0,
                -1,
                delegateTargetOffset,
                delegateMethodPtrOffset,
                delegateInvocationListOffset,
                delegateInvocationCountOffset));

            if (isDelegateLike && _rts is not null)
            {
                RuntimeType delegateArrayType = _rts.GetArrayType(type);
                int delegateArrayLayoutIndex = InternTypeLayout(delegateArrayType);
                _typeLayouts[index] = new TypeLayoutRecord(
                    type.TypeId,
                    elementIndex,
                    size,
                    align,
                    instanceSize,
                    Math.Max(0, type.StaticSize),
                    Math.Max(1, type.StaticAlign),
                    flags,
                    -1,
                    0,
                    delegateArrayLayoutIndex,
                    delegateTargetOffset,
                    delegateMethodPtrOffset,
                    delegateInvocationListOffset,
                    delegateInvocationCountOffset);
            }

            return index;
        }

        public TypeLayoutRecord GetTypeLayoutRecord(int typeLayoutIndex)
        {
            if ((uint)typeLayoutIndex >= (uint)_typeLayouts.Count)
                throw new ArgumentOutOfRangeException(nameof(typeLayoutIndex));
            return _typeLayouts[typeLayoutIndex];
        }

        public void LdVTableEntry(MachineRegister rd, MachineRegister receiver, RuntimeMethod declaredMethod)
        {
            if (declaredMethod is null) throw new ArgumentNullException(nameof(declaredMethod));
            if (!declaredMethod.HasThis)
                throw new InvalidOperationException("Virtual dispatch requires an instance method.");

            bool isInterfaceDispatch = declaredMethod.DeclaringType.Kind == RuntimeTypeKind.Interface;
            int slot;

            if (isInterfaceDispatch)
            {
                if (!_interfaceDispatchSlotByMethodId.ContainsKey(declaredMethod.MethodId))
                {
                    _interfaceDispatchSlotByMethodId.Add(declaredMethod.MethodId, -1);
                    _interfaceDispatchMethods.Add(declaredMethod);
                }

                slot = 0;
            }
            else
            {
                if (declaredMethod.VTableSlot < 0)
                    throw new InvalidOperationException($"Method M{declaredMethod.MethodId} has no class vtable slot and cannot be lowered as virtual dispatch.");

                slot = declaredMethod.VTableSlot;
            }

            int pc = Emit(new InstrDesc(
                Op.LdVTableEntry,
                RegisterVmIsa.EncodeRegister(rd),
                RegisterVmIsa.EncodeRegister(receiver),
                aux: Aux.Instruction(InstructionFlags.MayThrow),
                imm: slot));

            if (isInterfaceDispatch)
                _fixups.Add(new Fixup(pc, declaredMethod.MethodId, FixupKind.VirtualSlotImmediate, -1));
        }

        private void PrepareVirtualTables()
        {
            if (_vTables.Count != 0)
                return;

            int classSlotCount = 0;
            for (int i = 0; i < _typeLayoutTypes.Count; i++)
                classSlotCount = Math.Max(classSlotCount, Math.Max(0, _typeLayoutTypes[i].VTable.Length));

            for (int i = 0; i < _interfaceDispatchMethods.Count; i++)
            {
                RuntimeMethod method = _interfaceDispatchMethods[i];
                _interfaceDispatchSlotByMethodId[method.MethodId] = checked(classSlotCount + i);
            }

            var methodEntryPcById = new Dictionary<int, int>(_methods.Count);
            for (int i = 0; i < _methods.Count; i++)
            {
                int runtimeMethodId = _methods[i].RuntimeMethodId;
                if (runtimeMethodId >= 0)
                    methodEntryPcById[runtimeMethodId] = _methods[i].EntryPc;
            }

            int totalSlotCount = checked(classSlotCount + _interfaceDispatchMethods.Count);
            if (totalSlotCount == 0)
                return;

            for (int typeIndex = 0; typeIndex < _typeLayoutTypes.Count; typeIndex++)
            {
                RuntimeType type = _typeLayoutTypes[typeIndex];
                int start = _vTables.Count;

                for (int slot = 0; slot < totalSlotCount; slot++)
                {
                    RuntimeMethod? target = null;
                    if (slot < classSlotCount)
                    {
                        if ((uint)slot < (uint)type.VTable.Length)
                            target = type.VTable[slot];
                    }
                    else
                    {
                        RuntimeMethod declared = _interfaceDispatchMethods[slot - classSlotCount];
                        target = ResolveInterfaceDispatchTarget(type, declared);
                    }

                    int targetPc = -1;
                    if (target is not null)
                    {
                        if (methodEntryPcById.TryGetValue(target.MethodId, out int pc))
                        {
                            targetPc = pc;
                        }
                        else
                        {
                            RuntimeMethod projected = ProjectRuntimeMethodToOwner(type, target);
                            if (methodEntryPcById.TryGetValue(projected.MethodId, out pc))
                                targetPc = pc;
                        }
                    }

                    _vTables.Add(new VTableSlotRecord(targetPc));
                }

                TypeLayoutRecord old = _typeLayouts[typeIndex];
                _typeLayouts[typeIndex] = new TypeLayoutRecord(
                    old.RuntimeTypeId,
                    old.ElementTypeLayoutIndex,
                    old.Size,
                    old.Align,
                    old.InstanceSize,
                    old.StaticSize,
                    old.StaticAlign,
                    old.LayoutFlags,
                    start,
                    totalSlotCount,
                    old.DelegateArrayTypeLayoutIndex,
                    old.DelegateTargetOffset,
                    old.DelegateMethodPtrOffset,
                    old.DelegateInvocationListOffset,
                    old.DelegateInvocationCountOffset);
            }
        }

        private int VirtualDispatchSlotForMethodId(int declaredMethodId)
        {
            if (_interfaceDispatchSlotByMethodId.TryGetValue(declaredMethodId, out int slot) && slot >= 0)
                return slot;
            throw new InvalidOperationException($"Unresolved interface dispatch slot for M{declaredMethodId}.");
        }

        private RuntimeMethod? ResolveInterfaceDispatchTarget(RuntimeType actual, RuntimeMethod declared)
        {
            if (actual is null || declared is null)
                return null;

            if (!CanBeVirtualTarget(actual, declared.DeclaringType))
                return null;

            for (RuntimeType? t = actual; t is not null; t = t.BaseType)
            {
                RuntimeMethod? explicitImpl = TryResolveExplicitInterfaceImpl(t, declared);
                if (explicitImpl is not null)
                    return explicitImpl;
            }

            RuntimeMethod? implicitImpl = FindMostDerivedMethodByNameAndSig(actual, declared);
            if (implicitImpl is not null)
                return implicitImpl;

            return null;
        }

        private RuntimeMethod? TryResolveExplicitInterfaceImpl(RuntimeType implementationType, RuntimeMethod declared)
        {
            var map = implementationType.ExplicitInterfaceMethodImpls;
            if (map is null || map.Count == 0)
                return null;

            if (map.TryGetValue(declared.MethodId, out RuntimeMethod? exact))
                return ProjectRuntimeMethodToOwner(implementationType, exact);

            foreach (var kv in map)
            {
                RuntimeMethod ifaceMethod;
                try
                {
                    if (_rts is null)
                        return null;
                    ifaceMethod = _rts.GetMethodById(kv.Key);
                }
                catch
                {
                    continue;
                }

                if (SameInterfaceMethodIdentity(ifaceMethod, declared))
                    return ProjectRuntimeMethodToOwner(implementationType, kv.Value);
            }

            return null;
        }

        private RuntimeMethod ProjectRuntimeMethodToOwner(RuntimeType owner, RuntimeMethod method)
        {
            if (method.DeclaringType.TypeId == owner.TypeId)
                return method;

            _rts?.EnsureConstructedMembers(owner);

            RuntimeMethod[] methods = owner.Methods;
            for (int i = 0; i < methods.Length; i++)
            {
                RuntimeMethod candidate = methods[i];
                if (!StringComparer.Ordinal.Equals(candidate.Name, method.Name))
                    continue;
                if (candidate.GenericArity != method.GenericArity)
                    continue;
                if (candidate.IsStatic != method.IsStatic)
                    continue;
                if (candidate.Body is not null && method.Body is not null && ReferenceEquals(candidate.Body, method.Body))
                    return candidate;
                if (SameSigRuntime(candidate, method))
                    return candidate;
            }

            return method;
        }

        private static bool CanBeVirtualTarget(RuntimeType candidateOwner, RuntimeType declaredOwner)
        {
            if (ReferenceEquals(candidateOwner, declaredOwner) || candidateOwner.TypeId == declaredOwner.TypeId)
                return true;

            if (declaredOwner.Kind == RuntimeTypeKind.Interface)
            {
                for (RuntimeType? t = candidateOwner; t is not null; t = t.BaseType)
                {
                    RuntimeType[] interfaces = t.Interfaces;
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        if (SameInterfaceType(interfaces[i], declaredOwner))
                            return true;
                    }
                }
                return false;
            }

            for (RuntimeType? t = candidateOwner.BaseType; t is not null; t = t.BaseType)
            {
                if (ReferenceEquals(t, declaredOwner) || t.TypeId == declaredOwner.TypeId)
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

        private static RuntimeMethod? FindMostDerivedMethodByNameAndSig(RuntimeType actual, RuntimeMethod declared)
        {
            for (RuntimeType? cur = actual; cur is not null; cur = cur.BaseType)
            {
                RuntimeMethod[] methods = cur.Methods;
                for (int i = 0; i < methods.Length; i++)
                {
                    RuntimeMethod m = methods[i];
                    if (m.IsStatic)
                        continue;
                    if (m.IsPrivate)
                        continue;
                    if (!StringComparer.Ordinal.Equals(m.Name, declared.Name))
                        continue;
                    if (SameSigRuntime(m, declared))
                        return m;
                }
            }

            return null;
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

        private static bool SameSigRuntime(RuntimeMethod a, RuntimeMethod b)
        {
            if (a.GenericArity != b.GenericArity)
                return false;
            if (a.ParameterTypes.Length != b.ParameterTypes.Length)
                return false;
            if (a.ReturnType.TypeId != b.ReturnType.TypeId)
                return false;
            for (int i = 0; i < a.ParameterTypes.Length; i++)
            {
                if (a.ParameterTypes[i].TypeId != b.ParameterTypes[i].TypeId)
                    return false;
            }
            return true;
        }

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

        private RuntimeType GetLogicalArgumentTypeForAbi(RuntimeMethod method, int logicalIndex)
        {
            if (method.HasThis)
            {
                if (logicalIndex == 0)
                {
                    if (!method.DeclaringType.IsValueType)
                        return method.DeclaringType;
                    if (_rts is null)
                        throw new InvalidOperationException("ABI argument layout emission for value-type instance methods requires a runtime type system to materialize byref this.");
                    return _rts.GetByRefType(method.DeclaringType);
                }
                logicalIndex--;
            }
            if ((uint)logicalIndex >= (uint)method.ParameterTypes.Length)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex));
            return method.ParameterTypes[logicalIndex];
        }


        private static bool IsSystemMulticastDelegateBase(RuntimeType type)
            => StringComparer.Ordinal.Equals(type.Namespace, "System") &&
               StringComparer.Ordinal.Equals(type.Name, "MulticastDelegate");

        private static bool IsDelegateLikeRuntimeType(RuntimeType type)
        {
            for (RuntimeType? cur = type; cur is not null; cur = cur.BaseType)
            {
                if (IsSystemMulticastDelegateBase(cur))
                    return true;
            }
            return false;
        }

        private static int FindInstanceFieldOffsetInHierarchy(RuntimeType type, string name)
        {
            for (RuntimeType? cur = type; cur is not null; cur = cur.BaseType)
            {
                for (int i = 0; i < cur.InstanceFields.Length; i++)
                {
                    RuntimeField field = cur.InstanceFields[i];
                    if (!field.IsStatic && StringComparer.Ordinal.Equals(field.Name, name))
                        return field.Offset;
                }
            }
            throw new MissingFieldException(type.Name, name);
        }

        private static int StorageSizeOfForLayout(RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                return TargetArchitecture.PointerSize;
            return Math.Max(1, type.SizeOf);
        }

        private static int StorageAlignOfForLayout(RuntimeType type)
        {
            if (type.IsReferenceType || type.Kind is RuntimeTypeKind.Pointer or RuntimeTypeKind.ByRef or RuntimeTypeKind.TypeParam)
                return TargetArchitecture.PointerSize;
            return Math.Max(1, type.AlignOf);
        }

        private static int InstanceSizeOfForLayout(
            RuntimeType type,
            bool isDelegateLike,
            int delegateTargetOffset,
            int delegateMethodPtrOffset,
            int delegateInvocationListOffset,
            int delegateInvocationCountOffset)
        {
            int instanceSize = Math.Max(0, type.InstanceSize);

            if (!isDelegateLike)
                return instanceSize;

            int required = TargetArchitecture.PointerSize * 2;
            IncludeDelegateSlot(delegateTargetOffset, ref required);
            IncludeDelegateSlot(delegateMethodPtrOffset, ref required);
            IncludeDelegateSlot(delegateInvocationListOffset, ref required);
            IncludeDelegateSlot(delegateInvocationCountOffset, ref required);
            return Math.Max(instanceSize, AlignUpForDelegateLayout(required, TargetArchitecture.PointerSize));
        }
        private static int AlignUpForDelegateLayout(int value, int alignment)
        {
            if (alignment <= 1)
                return value;

            int mask = alignment - 1;
            return checked((value + mask) & ~mask);
        }
        private static void IncludeDelegateSlot(int offset, ref int required)
        {
            if (offset < 0)
                return;
            required = Math.Max(required, checked(offset + TargetArchitecture.PointerSize));
        }

        public int AddBlob(ReadOnlySpan<byte> bytes)
        {
            int offset = _blob.Count;
            for (int i = 0; i < bytes.Length; i++)
                _blob.Add(bytes[i]);
            return offset;
        }

        public int AddSwitchEntry(int key, Label target)
            => AddSwitchEntry((long)key, target);

        public int AddSwitchEntry(long key, Label target)
        {
            ValidateLabel(target);
            int index = _switchTable.Count;
            _switchTable.Add(new SwitchTableRecord(key, 0));
            _fixups.Add(new Fixup(-1, target.Id, FixupKind.SwitchEntry, index));
            return index;
        }

        public void SwitchI32(MachineRegister key, int tableStartIndex, int tableCount)
            => Emit(InstrDesc.Switch(Op.SwitchI32, key, tableStartIndex, tableCount));

        public void SwitchI64(MachineRegister key, int tableStartIndex, int tableCount)
            => Emit(InstrDesc.Switch(Op.SwitchI64, key, tableStartIndex, tableCount));


        public void AddExceptionRegion(EhRegionRecord region) => _eh.Add(region);
        public void AddGcSafePoint(GcSafePointRecord safePoint) => _gcSafePoints.Add(safePoint);
        public void AddGcRoot(GcRootRecord root) => _gcRoots.Add(root);
        public void AddUnwind(UnwindRecord unwind) => _unwind.Add(unwind);

        public int Emit(InstrDesc instruction)
        {
            int pc = Pc;
            _code.Add(instruction);
            return pc;
        }

        public void Nop() => Emit(InstrDesc.Op0(Op.Nop));
        public void Trap(int reason) => Emit(InstrDesc.Op0(Op.Trap, imm: reason));
        public void GcPoll() => Emit(InstrDesc.Op0(Op.GcPoll, Aux.Instruction(InstructionFlags.GcSafePoint)));

        public void RetVoid() => Emit(InstrDesc.Op0(Op.RetVoid));
        public void RetI(MachineRegister rs) => Emit(new InstrDesc(Op.RetI, rs1: RegisterVmIsa.EncodeRegister(rs)));
        public void RetF(MachineRegister rs) => Emit(new InstrDesc(Op.RetF, rs1: RegisterVmIsa.EncodeRegister(rs)));
        public void RetRef(MachineRegister rs) => Emit(new InstrDesc(Op.RetRef, rs1: RegisterVmIsa.EncodeRegister(rs)));
        public void RetValue(MachineRegister address, int size) => Emit(new InstrDesc(Op.RetValue, rs1: RegisterVmIsa.EncodeRegister(address), imm: size));

        public void J(Label target) => Branch(Op.J, target);
        public void Leave(Label target) => Branch(Op.Leave, target);
        public void BrTrueI32(MachineRegister rs, Label target) => Branch(Op.BrTrueI32, rs, target);
        public void BrFalseI32(MachineRegister rs, Label target) => Branch(Op.BrFalseI32, rs, target);
        public void BrTrueI64(MachineRegister rs, Label target) => Branch(Op.BrTrueI64, rs, target);
        public void BrFalseI64(MachineRegister rs, Label target) => Branch(Op.BrFalseI64, rs, target);
        public void BrTrueRef(MachineRegister rs, Label target) => Branch(Op.BrTrueRef, rs, target);
        public void BrFalseRef(MachineRegister rs, Label target) => Branch(Op.BrFalseRef, rs, target);
        public void BrI32Eq(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Eq, a, b, target);
        public void BrI32Ne(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Ne, a, b, target);
        public void BrI32Lt(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Lt, a, b, target);
        public void BrI32Le(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Le, a, b, target);
        public void BrI32Gt(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Gt, a, b, target);
        public void BrI32Ge(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI32Ge, a, b, target);
        public void BrI64Eq(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI64Eq, a, b, target);
        public void BrI64Ne(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrI64Ne, a, b, target);
        public void BrRefEq(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrRefEq, a, b, target);
        public void BrRefNe(MachineRegister a, MachineRegister b, Label target) => Branch(Op.BrRefNe, a, b, target);

        public void Branch(Op op, Label target)
        {
            if (op is not (Op.J or Op.Leave))
                throw new ArgumentOutOfRangeException(nameof(op));
            EmitTarget(InstrDesc.Op0(op), target);
        }

        public void Branch(Op op, MachineRegister rs, Label target)
        {
            if (op is not (Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64 or Op.BrTrueRef or Op.BrFalseRef))
                throw new ArgumentOutOfRangeException(nameof(op));
            EmitTarget(InstrDesc.Br1(op, rs, -1), target);
        }

        public void Branch(Op op, MachineRegister a, MachineRegister b, Label target)
        {
            if (!IsTwoRegisterBranchOp(op))
                throw new ArgumentOutOfRangeException(nameof(op));
            EmitTarget(InstrDesc.Br2(op, a, b, -1), target);
        }

        private static bool IsTwoRegisterBranchOp(Op op)
            => op is Op.BrI32Eq or Op.BrI32Ne or Op.BrI32Lt or Op.BrI32Le or Op.BrI32Gt or Op.BrI32Ge
                or Op.BrU32Lt or Op.BrU32Le or Op.BrU32Gt or Op.BrU32Ge
                or Op.BrI64Eq or Op.BrI64Ne or Op.BrI64Lt or Op.BrI64Le or Op.BrI64Gt or Op.BrI64Ge
                or Op.BrU64Lt or Op.BrU64Le or Op.BrU64Gt or Op.BrU64Ge
                or Op.BrRefEq or Op.BrRefNe
                or Op.BrF32Eq or Op.BrF32Ne or Op.BrF32Lt or Op.BrF32Le or Op.BrF32Gt or Op.BrF32Ge
                or Op.BrF64Eq or Op.BrF64Ne or Op.BrF64Lt or Op.BrF64Le or Op.BrF64Gt or Op.BrF64Ge;

        public void MovI(MachineRegister rd, MachineRegister rs) => Emit(new InstrDesc(Op.MovI, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs)));
        public void MovF(MachineRegister rd, MachineRegister rs) => Emit(new InstrDesc(Op.MovF, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs)));
        public void MovRef(MachineRegister rd, MachineRegister rs) => Emit(new InstrDesc(Op.MovRef, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs)));
        public void MovPtr(MachineRegister rd, MachineRegister rs) => Emit(new InstrDesc(Op.MovPtr, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(rs)));
        public void LiI32(MachineRegister rd, int value) => Emit(InstrDesc.Li(Op.LiI32, rd, value));
        public void LiI64(MachineRegister rd, long value) => Emit(InstrDesc.Li(Op.LiI64, rd, value));
        public void LiF32Bits(MachineRegister rd, int bits) => Emit(InstrDesc.Li(Op.LiF32Bits, rd, bits));
        public void LiF64Bits(MachineRegister rd, long bits) => Emit(InstrDesc.Li(Op.LiF64Bits, rd, bits));
        public void LiNull(MachineRegister rd) => Emit(InstrDesc.Li(Op.LiNull, rd, 0));
        public void LiString(MachineRegister rd, int userStringRid) => Emit(InstrDesc.Li(Op.LiString, rd, userStringRid, Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow)));
        public void LiTypeHandle(MachineRegister rd, int runtimeTypeId) => Emit(InstrDesc.Li(Op.LiTypeHandle, rd, runtimeTypeId));

        public void I32Add(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I32Add, rd, a, b));
        public void I32Sub(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I32Sub, rd, a, b));
        public void I32Mul(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I32Mul, rd, a, b));
        public void I32Div(MachineRegister rd, MachineRegister a, MachineRegister b)
            => Emit(InstrDesc.R(Op.I32Div, rd, a, b, Aux.Instruction(InstructionFlags.MayThrow)));
        public void U32Div(MachineRegister rd, MachineRegister a, MachineRegister b)
            => Emit(InstrDesc.R(Op.U32Div, rd, a, b, Aux.Instruction(InstructionFlags.MayThrow)));
        public void I32AddImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32AddImm, rd, a, value));
        public void I32SubImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32SubImm, rd, a, value));
        public void I32MulImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32MulImm, rd, a, value));
        public void I32AndImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32AndImm, rd, a, value));
        public void I32OrImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32OrImm, rd, a, value));
        public void I32XorImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32XorImm, rd, a, value));
        public void I32ShlImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32ShlImm, rd, a, value));
        public void I32ShrImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32ShrImm, rd, a, value));
        public void U32ShrImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.U32ShrImm, rd, a, value));
        public void I32EqImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32EqImm, rd, a, value));
        public void I32NeImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32NeImm, rd, a, value));
        public void I32LtImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32LtImm, rd, a, value));
        public void I32LeImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32LeImm, rd, a, value));
        public void I32GtImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32GtImm, rd, a, value));
        public void I32GeImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I32GeImm, rd, a, value));
        public void U32LtImm(MachineRegister rd, MachineRegister a, uint value) => Emit(InstrDesc.I(Op.U32LtImm, rd, a, unchecked((int)value)));
        public void I64Add(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I64Add, rd, a, b));
        public void I64Sub(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I64Sub, rd, a, b));
        public void I64Mul(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.I64Mul, rd, a, b));
        public void I64Div(MachineRegister rd, MachineRegister a, MachineRegister b)
            => Emit(InstrDesc.R(Op.I64Div, rd, a, b, Aux.Instruction(InstructionFlags.MayThrow)));
        public void U64Div(MachineRegister rd, MachineRegister a, MachineRegister b)
            => Emit(InstrDesc.R(Op.U64Div, rd, a, b, Aux.Instruction(InstructionFlags.MayThrow)));
        public void I64AddImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64AddImm, rd, a, value));
        public void I64SubImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64SubImm, rd, a, value));
        public void I64MulImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64MulImm, rd, a, value));
        public void I64AndImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64AndImm, rd, a, value));
        public void I64OrImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64OrImm, rd, a, value));
        public void I64XorImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64XorImm, rd, a, value));
        public void I64ShlImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I64ShlImm, rd, a, value));
        public void I64ShrImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.I64ShrImm, rd, a, value));
        public void U64ShrImm(MachineRegister rd, MachineRegister a, int value) => Emit(InstrDesc.I(Op.U64ShrImm, rd, a, value));
        public void I64EqImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64EqImm, rd, a, value));
        public void I64NeImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64NeImm, rd, a, value));
        public void I64LtImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64LtImm, rd, a, value));
        public void I64LeImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64LeImm, rd, a, value));
        public void I64GtImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64GtImm, rd, a, value));
        public void I64GeImm(MachineRegister rd, MachineRegister a, long value) => Emit(InstrDesc.I(Op.I64GeImm, rd, a, value));
        public void U64LtImm(MachineRegister rd, MachineRegister a, ulong value) => Emit(InstrDesc.I(Op.U64LtImm, rd, a, unchecked((long)value)));
        public void F32Add(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F32Add, rd, a, b));
        public void F32Sub(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F32Sub, rd, a, b));
        public void F32Mul(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F32Mul, rd, a, b));
        public void F32Div(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F32Div, rd, a, b));
        public void F64Add(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F64Add, rd, a, b));
        public void F64Sub(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F64Sub, rd, a, b));
        public void F64Mul(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F64Mul, rd, a, b));
        public void F64Div(MachineRegister rd, MachineRegister a, MachineRegister b) => Emit(InstrDesc.R(Op.F64Div, rd, a, b));

        public void LdI4(MachineRegister rd, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.LdI4, rd, address, offset, index, aux));
        public void LdI8(MachineRegister rd, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.LdI8, rd, address, offset, index, aux));
        public void LdF32(MachineRegister rd, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.LdF32, rd, address, offset, index, aux));
        public void LdF64(MachineRegister rd, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.LdF64, rd, address, offset, index, aux));
        public void LdRef(MachineRegister rd, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.LdRef, rd, address, offset, index, aux));
        public void StI4(MachineRegister value, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.StI4, value, address, offset, index, aux));
        public void StI8(MachineRegister value, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.StI8, value, address, offset, index, aux));
        public void StF32(MachineRegister value, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.StF32, value, address, offset, index, aux));
        public void StF64(MachineRegister value, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.StF64, value, address, offset, index, aux));
        public void StRef(MachineRegister value, MachineRegister address, int offset = 0, MachineRegister index = MachineRegister.Invalid, ushort aux = 0)
            => Emit(InstrDesc.Mem(Op.StRef, value, address, offset, index, aux));

        public void LdFldI4(MachineRegister rd, MachineRegister instance, int fieldOffset, int fieldSize = 4, FieldAccessFlags fieldFlags = FieldAccessFlags.None)
            => Emit(InstrDesc.Field(Op.LdFldI4, rd, instance, fieldOffset, fieldSize, fieldFlags, Aux.Instruction(InstructionFlags.MayThrow)));
        public void StFldI4(MachineRegister value, MachineRegister instance, int fieldOffset, int fieldSize = 4, FieldAccessFlags fieldFlags = FieldAccessFlags.None)
            => Emit(InstrDesc.Field(Op.StFldI4, value, instance, fieldOffset, fieldSize, fieldFlags, Aux.Instruction(InstructionFlags.MayThrow)));
        public void LdFldRef(MachineRegister rd, MachineRegister instance, int fieldOffset, int fieldSize = TargetArchitecture.PointerSize, FieldAccessFlags fieldFlags = FieldAccessFlags.FieldTypeContainsGcPointers)
            => Emit(InstrDesc.Field(Op.LdFldRef, rd, instance, fieldOffset, fieldSize, fieldFlags, Aux.Instruction(InstructionFlags.MayThrow)));
        public void StFldRef(MachineRegister value, MachineRegister instance, int fieldOffset, int fieldSize = TargetArchitecture.PointerSize, FieldAccessFlags fieldFlags = FieldAccessFlags.FieldTypeContainsGcPointers)
            => Emit(InstrDesc.Field(Op.StFldRef, value, instance, fieldOffset, fieldSize, fieldFlags, Aux.Instruction(InstructionFlags.MayThrow | InstructionFlags.WriteBarrier)));

        public void LdElemI4(MachineRegister rd, MachineRegister array, MachineRegister index, int elementTypeLayoutIndex = -1)
            => Emit(InstrDesc.Array(Op.LdElemI4, rd, array, index, elementTypeLayoutIndex));
        public void StElemI4(MachineRegister value, MachineRegister array, MachineRegister index, int elementTypeLayoutIndex = -1)
            => Emit(InstrDesc.Array(Op.StElemI4, value, array, index, elementTypeLayoutIndex));
        public void LdElemRef(MachineRegister rd, MachineRegister array, MachineRegister index, int elementTypeLayoutIndex = -1)
            => Emit(InstrDesc.Array(Op.LdElemRef, rd, array, index, elementTypeLayoutIndex));
        public void StElemRef(MachineRegister value, MachineRegister array, MachineRegister index, int elementTypeLayoutIndex = -1)
            => Emit(InstrDesc.Array(Op.StElemRef, value, array, index, elementTypeLayoutIndex, Aux.Instruction(InstructionFlags.WriteBarrier)));
        public void LdLen(MachineRegister rd, MachineRegister array) => Emit(new InstrDesc(Op.LdLen, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(array)));

        public void AllocObj(MachineRegister rd, RuntimeType type)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            int typeLayoutIndex = InternTypeLayout(type);
            Emit(InstrDesc.Li(Op.AllocObj, rd, typeLayoutIndex, Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow | InstructionFlags.WriteBarrier)));
        }
        public void NewSZArray(MachineRegister rd, MachineRegister length, int arrayTypeLayoutIndex)
            => Emit(new InstrDesc(Op.NewSZArray, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(length),
                aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow), imm: arrayTypeLayoutIndex));

        public void NewDelegate(MachineRegister rd, int delegateTypeLayoutIndex, Label targetEntry)
        {
            ValidateLabel(targetEntry);
            int pc = Emit(new InstrDesc(Op.NewDelegate, RegisterVmIsa.EncodeRegister(rd),
                aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow),
                imm: PackDelegateDescriptor(delegateTypeLayoutIndex, 0)));
            _fixups.Add(new Fixup(pc, targetEntry.Id, FixupKind.DelegateDescriptorTargetPc, -1));
        }

        public void NewDelegateClosed(MachineRegister rd, MachineRegister target, int delegateTypeLayoutIndex, Label targetEntry)
        {
            ValidateLabel(targetEntry);
            int pc = Emit(new InstrDesc(Op.NewDelegateClosed, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(target),
                aux: Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow),
                imm: PackDelegateDescriptor(delegateTypeLayoutIndex, 0)));
            _fixups.Add(new Fixup(pc, targetEntry.Id, FixupKind.DelegateDescriptorTargetPc, -1));
        }

        public void DelegateCombine(MachineRegister rd, MachineRegister left, MachineRegister right)
            => Emit(InstrDesc.R(Op.DelegateCombine, rd, left, right,
                Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow | InstructionFlags.WriteBarrier)));

        public void DelegateRemove(MachineRegister rd, MachineRegister source, MachineRegister value)
            => Emit(InstrDesc.R(Op.DelegateRemove, rd, source, value,
                Aux.Instruction(InstructionFlags.GcSafePoint | InstructionFlags.MayThrow | InstructionFlags.WriteBarrier)));


        private static long PackDelegateDescriptor(int delegateTypeLayoutIndex, int targetCodePointer)
            => ((long)(uint)delegateTypeLayoutIndex << 32) | (uint)targetCodePointer;
        public void CastClass(MachineRegister rd, MachineRegister source, int runtimeTypeId)
            => Emit(new InstrDesc(Op.CastClass, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(source),
                aux: Aux.Instruction(InstructionFlags.MayThrow), imm: runtimeTypeId));
        public void IsInst(MachineRegister rd, MachineRegister source, int runtimeTypeId)
            => Emit(new InstrDesc(Op.IsInst, RegisterVmIsa.EncodeRegister(rd), RegisterVmIsa.EncodeRegister(source), imm: runtimeTypeId));

        public void CallDirect(Op op, Label target, CallFlags flags = CallFlags.None)
        {
            if (!IsDirectManagedCallOp(op))
                throw new ArgumentOutOfRangeException(nameof(op));
            EmitTarget(InstrDesc.Call(op, -1, flags), target);
        }

        public void CallVoid(Label target, CallFlags flags = CallFlags.None) => CallDirect(Op.CallVoid, target, flags);
        public void CallI(Label target, CallFlags flags = CallFlags.None) => CallDirect(Op.CallI, target, flags);
        public void CallF(Label target, CallFlags flags = CallFlags.None) => CallDirect(Op.CallF, target, flags);
        public void CallRef(Label target, CallFlags flags = CallFlags.None) => CallDirect(Op.CallRef, target, flags);
        public void CallValue(Label target, CallFlags flags = CallFlags.HiddenReturnBuffer) => CallDirect(Op.CallValue, target, flags);

        private static bool IsDirectManagedCallOp(Op op)
            => op is Op.CallVoid or Op.CallI or Op.CallF or Op.CallRef or Op.CallValue;

        private void EmitTarget(InstrDesc instruction, Label target)
        {
            ValidateLabel(target);
            int pc = Emit(instruction);
            _fixups.Add(new Fixup(pc, target.Id, FixupKind.InstructionImmediate, -1));
        }

        public CodeImage Build(ImageFlags flags, bool validate = true)
        {
            if (_currentMethod is not null)
                throw new InvalidOperationException("Active RVM method is not ended.");

            PrepareVirtualTables();
            ResolveFixups();

            var methods = ImmutableArray.CreateBuilder<MethodRecord>(_methods.Count);
            for (int i = 0; i < _methods.Count; i++)
            {
                var m = _methods[i];
                methods.Add(new MethodRecord(
                    m.RuntimeMethodId,
                    m.StaticConstructorTypeLayoutIndex,
                    m.EntryPc,
                    m.Frame.FrameSize,
                    m.Frame.OutgoingArgumentAreaOffset,
                    m.EhStartIndex,
                    m.EhEndIndex - m.EhStartIndex,
                    m.GcSafePointStartIndex,
                    m.GcSafePointEndIndex - m.GcSafePointStartIndex,
                    m.UnwindStartIndex,
                    m.UnwindEndIndex - m.UnwindStartIndex,
                    m.Flags));
            }

            flags |= ImageFlags.LittleEndian;
            if (_eh.Count != 0) flags |= ImageFlags.HasEh;
            if (_gcSafePoints.Count != 0 || _gcRoots.Count != 0) flags |= ImageFlags.HasGcInfo;
            if (_unwind.Count != 0) flags |= ImageFlags.HasUnwindInfo;

            return new CodeImage(
                flags,
                _code.ToImmutableArray(),
                methods.ToImmutable(),
                _eh.ToImmutableArray(),
                _gcSafePoints.ToImmutableArray(),
                _gcRoots.ToImmutableArray(),
                _unwind.ToImmutableArray(),
                _switchTable.ToImmutableArray(),
                _typeLayouts.ToImmutableArray(),
                _vTables.ToImmutableArray(),
                _blob.ToImmutableArray(),
                validate: validate);
        }

        private void PatchImmediate(int pc, long immediate)
        {
            if ((uint)pc >= (uint)_code.Count)
                throw new InvalidOperationException($"Invalid RVM patch PC {pc}");
            InstrDesc old = _code[pc];
            _code[pc] = new InstrDesc(old.Op, old.Rd, old.Rs1, old.Rs2, old.Rs3, old.Aux, immediate);
        }

        private void ResolveFixups()
        {
            for (int i = 0; i < _fixups.Count; i++)
            {
                var fixup = _fixups[i];
                int targetPc = -1;
                if (fixup.Kind != FixupKind.VirtualSlotImmediate)
                {
                    targetPc = _labelPc[fixup.LabelId];
                    if (targetPc < 0)
                        throw new InvalidOperationException($"Unbound RVM label: L{fixup.LabelId}");
                }

                if (fixup.Kind == FixupKind.InstructionImmediate)
                {
                    _code[fixup.Pc] = _code[fixup.Pc].WithTargetPc(targetPc);
                }
                else if (fixup.Kind == FixupKind.SwitchEntry)
                {
                    var old = _switchTable[fixup.TableIndex];
                    _switchTable[fixup.TableIndex] = new SwitchTableRecord(old.Key, targetPc);
                }
                else if (fixup.Kind == FixupKind.DelegateDescriptorTargetPc)
                {
                    InstrDesc old = _code[fixup.Pc];
                    long descriptor = (old.Imm & unchecked((long)0xFFFFFFFF00000000)) | (uint)targetPc;
                    _code[fixup.Pc] = new InstrDesc(old.Op, old.Rd, old.Rs1, old.Rs2, old.Rs3, old.Aux, descriptor);
                }
                else if (fixup.Kind == FixupKind.VirtualSlotImmediate)
                {
                    InstrDesc old = _code[fixup.Pc];
                    int slot = VirtualDispatchSlotForMethodId(fixup.LabelId);
                    _code[fixup.Pc] = new InstrDesc(old.Op, old.Rd, old.Rs1, old.Rs2, old.Rs3, old.Aux, slot);
                }
            }
        }

        private void ValidateLabel(Label label)
        {
            if (label.Id < 0 || label.Id >= _labelPc.Count)
                throw new ArgumentOutOfRangeException(nameof(label));
        }

        private sealed class MethodDraft
        {
            public readonly int RuntimeMethodId;
            public readonly int StaticConstructorTypeLayoutIndex;
            public readonly int EntryPc;
            public int EndPc;
            public readonly StackFrameLayout Frame;
            public readonly ushort Flags;
            public readonly int EhStartIndex;
            public int EhEndIndex;
            public readonly int GcSafePointStartIndex;
            public int GcSafePointEndIndex;
            public readonly int UnwindStartIndex;
            public int UnwindEndIndex;

            public MethodDraft(int runtimeMethodId, int staticConstructorTypeLayoutIndex, int entryPc, StackFrameLayout frame, ushort flags, int ehStartIndex, int gcSafePointStartIndex, int unwindStartIndex)
            {
                RuntimeMethodId = runtimeMethodId;
                StaticConstructorTypeLayoutIndex = staticConstructorTypeLayoutIndex;
                EntryPc = entryPc;
                EndPc = entryPc;
                Frame = frame;
                Flags = flags;
                EhStartIndex = ehStartIndex;
                EhEndIndex = ehStartIndex;
                GcSafePointStartIndex = gcSafePointStartIndex;
                GcSafePointEndIndex = gcSafePointStartIndex;
                UnwindStartIndex = unwindStartIndex;
                UnwindEndIndex = unwindStartIndex;
            }
        }

        private readonly struct Fixup
        {
            public readonly int Pc;
            public readonly int LabelId;
            public readonly FixupKind Kind;
            public readonly int TableIndex;

            public Fixup(int pc, int labelId, FixupKind kind, int tableIndex)
            {
                Pc = pc;
                LabelId = labelId;
                Kind = kind;
                TableIndex = tableIndex;
            }
        }

        private enum FixupKind
        {
            InstructionImmediate,
            SwitchEntry,
            DelegateDescriptorTargetPc,
            VirtualSlotImmediate,
        }
    }

    internal static class ImageSerializer
    {
        public static byte[] ToBytes(CodeImage image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("Image serialization is little-endian only.");
            EnsureStructSizes();

            int offset = RegisterVmIsa.HeaderSize;
            int codeOffset = Align(offset, 8);
            offset = codeOffset + image.Code.Length * RegisterVmIsa.InstructionSize;
            int methodOffset = Align(offset, 8);
            offset = methodOffset + image.Methods.Length * Marshal.SizeOf<MethodRecord>();
            int ehOffset = Align(offset, 8);
            offset = ehOffset + image.EhRegions.Length * Marshal.SizeOf<EhRegionRecord>();
            int gcSafePointOffset = Align(offset, 8);
            offset = gcSafePointOffset + image.GcSafePoints.Length * Marshal.SizeOf<GcSafePointRecord>();
            int gcRootOffset = Align(offset, 8);
            offset = gcRootOffset + image.GcRoots.Length * Marshal.SizeOf<GcRootRecord>();
            int unwindOffset = Align(offset, 8);
            offset = unwindOffset + image.Unwind.Length * Marshal.SizeOf<UnwindRecord>();
            int switchOffset = Align(offset, 8);
            offset = switchOffset + image.SwitchTable.Length * Marshal.SizeOf<SwitchTableRecord>();
            int typeLayoutOffset = Align(offset, 8);
            offset = typeLayoutOffset + image.TypeLayouts.Length * Marshal.SizeOf<TypeLayoutRecord>();
            int vTableOffset = Align(offset, 8);
            offset = vTableOffset + image.VTables.Length * Marshal.SizeOf<VTableSlotRecord>();
            int blobOffset = Align(offset, 8);
            offset = blobOffset + image.Blob.Length;

            byte[] bytes = new byte[offset];
            var header = new CodeImageHeader(
                image.Flags,
                codeOffset,
                image.Code.Length,
                methodOffset,
                image.Methods.Length,
                ehOffset,
                image.EhRegions.Length,
                gcSafePointOffset,
                image.GcSafePoints.Length,
                gcRootOffset,
                image.GcRoots.Length,
                unwindOffset,
                image.Unwind.Length,
                switchOffset,
                image.SwitchTable.Length,
                typeLayoutOffset,
                image.TypeLayouts.Length,
                vTableOffset,
                image.VTables.Length,
                blobOffset,
                image.Blob.Length);

            WriteOne(bytes, 0, header);
            WriteMany(bytes, codeOffset, image.Code.AsSpan());
            WriteMany(bytes, methodOffset, image.Methods.AsSpan());
            WriteMany(bytes, ehOffset, image.EhRegions.AsSpan());
            WriteMany(bytes, gcSafePointOffset, image.GcSafePoints.AsSpan());
            WriteMany(bytes, gcRootOffset, image.GcRoots.AsSpan());
            WriteMany(bytes, unwindOffset, image.Unwind.AsSpan());
            WriteMany(bytes, switchOffset, image.SwitchTable.AsSpan());
            WriteMany(bytes, typeLayoutOffset, image.TypeLayouts.AsSpan());
            WriteMany(bytes, vTableOffset, image.VTables.AsSpan());
            image.Blob.AsSpan().CopyTo(bytes.AsSpan(blobOffset));
            return bytes;
        }

        public static CodeImage FromBytes(ReadOnlySpan<byte> data)
        {
            if (!BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("Image serialization is little-endian only.");
            EnsureStructSizes();
            if (data.Length < RegisterVmIsa.HeaderSize)
                throw new InvalidDataException("Register image is shorter than the fixed header.");

            CodeImageHeader header = MemoryMarshal.Read<CodeImageHeader>(data);
            if (header.HeaderSize != RegisterVmIsa.HeaderSize)
                throw new InvalidDataException("Invalid register image header size.");
            if (header.InstructionSize != RegisterVmIsa.InstructionSize)
                throw new InvalidDataException("Invalid register image instruction size.");

            int end = 0;
            ValidateSection(data.Length, header.CodeOffset, header.CodeCount, RegisterVmIsa.InstructionSize, "code", ref end);
            ValidateSection(data.Length, header.MethodOffset, header.MethodCount, Marshal.SizeOf<MethodRecord>(), "method", ref end);
            ValidateSection(data.Length, header.EhOffset, header.EhCount, Marshal.SizeOf<EhRegionRecord>(), "EH", ref end);
            ValidateSection(data.Length, header.GcSafePointOffset, header.GcSafePointCount, Marshal.SizeOf<GcSafePointRecord>(), "GC safepoint", ref end);
            ValidateSection(data.Length, header.GcRootOffset, header.GcRootCount, Marshal.SizeOf<GcRootRecord>(), "GC root", ref end);
            ValidateSection(data.Length, header.UnwindOffset, header.UnwindCount, Marshal.SizeOf<UnwindRecord>(), "unwind", ref end);
            ValidateSection(data.Length, header.SwitchTableOffset, header.SwitchTableCount, Marshal.SizeOf<SwitchTableRecord>(), "switch", ref end);
            ValidateSection(data.Length, header.TypeLayoutOffset, header.TypeLayoutCount, Marshal.SizeOf<TypeLayoutRecord>(), "type-layout", ref end);
            ValidateSection(data.Length, header.VTableOffset, header.VTableCount, Marshal.SizeOf<VTableSlotRecord>(), "vtable", ref end);
            ValidateSection(data.Length, header.BlobOffset, header.BlobLength, 1, "blob", ref end);
            if (end != data.Length)
                throw new InvalidDataException("Trailing bytes found in register image.");

            return new CodeImage(
                (ImageFlags)header.Flags,
                ReadMany<InstrDesc>(data, header.CodeOffset, header.CodeCount),
                ReadMany<MethodRecord>(data, header.MethodOffset, header.MethodCount),
                ReadMany<EhRegionRecord>(data, header.EhOffset, header.EhCount),
                ReadMany<GcSafePointRecord>(data, header.GcSafePointOffset, header.GcSafePointCount),
                ReadMany<GcRootRecord>(data, header.GcRootOffset, header.GcRootCount),
                ReadMany<UnwindRecord>(data, header.UnwindOffset, header.UnwindCount),
                ReadMany<SwitchTableRecord>(data, header.SwitchTableOffset, header.SwitchTableCount),
                ReadMany<TypeLayoutRecord>(data, header.TypeLayoutOffset, header.TypeLayoutCount),
                ReadMany<VTableSlotRecord>(data, header.VTableOffset, header.VTableCount),
                data.Slice(header.BlobOffset, header.BlobLength).ToArray().ToImmutableArray());
        }

        private static void ValidateSection(int imageLength, int offset, int count, int elementSize, string name, ref int end)
        {
            if (offset < 0 || count < 0)
                throw new InvalidDataException($"Negative {name} section offset or count.");
            if (elementSize <= 0)
                throw new InvalidDataException($"Invalid {name} section element size.");
            long byteLength = (long)count * elementSize;
            long sectionEnd = (long)offset + byteLength;
            if (sectionEnd > imageLength)
                throw new InvalidDataException($"Register image {name} section is outside the image.");
            if (count != 0 && offset < RegisterVmIsa.HeaderSize)
                throw new InvalidDataException($"Register image {name} section overlaps the fixed header.");
            if (sectionEnd > end)
                end = checked((int)sectionEnd);
        }

        private static ImmutableArray<T> ReadMany<T>(ReadOnlySpan<byte> source, int offset, int count)
            where T : struct
        {
            if (count == 0)
                return ImmutableArray<T>.Empty;

            int size = Marshal.SizeOf<T>();
            var result = new T[count];
            for (int i = 0; i < count; i++)
                result[i] = MemoryMarshal.Read<T>(source.Slice(offset + i * size, size));
            return ImmutableArray.Create(result);
        }

        private static void WriteOne<T>(byte[] destination, int offset, T value)
            where T : struct
        {
            ref byte destRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(destination), offset);
            Unsafe.WriteUnaligned(ref destRef, value);
        }

        private static void WriteMany<T>(byte[] destination, int offset, ReadOnlySpan<T> values)
            where T : struct
        {
            if (values.Length == 0)
                return;
            MemoryMarshal.AsBytes(values).CopyTo(destination.AsSpan(offset));
        }

        private static int Align(int value, int alignment)
            => (value + alignment - 1) & ~(alignment - 1);

        private static void EnsureStructSizes()
        {
            if (Marshal.SizeOf<InstrDesc>() != RegisterVmIsa.InstructionSize)
                throw new InvalidOperationException("Unexpected instruction size.");
            if (Marshal.SizeOf<CodeImageHeader>() > RegisterVmIsa.HeaderSize)
                throw new InvalidOperationException("Unexpected header size.");
        }
    }

    internal sealed class ImagePrintOptions
    {
        public static ImagePrintOptions Default { get; } = new ImagePrintOptions();

        public bool IncludeHeader { get; set; } = true;
        public bool IncludeSideTables { get; set; } = true;
        public bool IncludeRawOperands { get; set; }
        public bool UseLabels { get; set; } = true;
        public int MaxBlobBytes { get; set; } = 128;
    }

    internal static class ImagePrinter
    {
        public static string Print(CodeImage image, ImagePrintOptions? options = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));

            var writer = new StringWriter();
            WriteTo(writer, image, options);
            return writer.ToString();
        }
        public static void WriteTo(TextWriter writer, CodeImage image, ImagePrintOptions? options = null)
        {
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (image is null) throw new ArgumentNullException(nameof(image));
            options ??= ImagePrintOptions.Default;

            var labels = BuildLabels(image);

            if (options.IncludeHeader)
                WriteHeader(writer, image);

            for (int m = 0; m < image.Methods.Length; m++)
            {
                var method = image.Methods[m];
                int endPc = image.GetMethodEndPc(m);

                writer.WriteLine();
                writer.Write("method M");
                writer.Write(image.GetMethodRuntimeMethodId(m));
                if (method.StaticConstructorTypeLayoutIndex >= 0)
                {
                    writer.Write(" abi=");
                    writer.Write(method.StaticConstructorTypeLayoutIndex);
                }
                writer.Write(" pc=[");
                writer.Write(method.EntryPc);
                writer.Write("..");
                writer.Write(endPc);
                writer.Write(") frame=");
                writer.Write(method.FrameSize);
                writer.Write(" outArgsBase=");
                writer.Write(method.OutgoingArgumentAreaOffset);
                if (method.Flags != 0)
                {
                    writer.Write(" flags=0x");
                    writer.Write(method.Flags.ToString("X4"));
                }
                if (method.EhCount != 0)
                {
                    writer.Write(" eh=");
                    writer.Write(method.EhStartIndex);
                    writer.Write("+");
                    writer.Write(method.EhCount);
                }
                if (method.GcSafePointCount != 0)
                {
                    writer.Write(" gcsp=");
                    writer.Write(method.GcSafePointStartIndex);
                    writer.Write("+");
                    writer.Write(method.GcSafePointCount);
                }
                if (method.UnwindCount != 0)
                {
                    writer.Write(" unwind=");
                    writer.Write(method.UnwindStartIndex);
                    writer.Write("+");
                    writer.Write(method.UnwindCount);
                }
                writer.WriteLine();

                for (int pc = method.EntryPc; pc < endPc; pc++)
                {
                    if (options.UseLabels && labels.TryGetValue(pc, out string? label))
                    {
                        writer.Write(label);
                        writer.WriteLine(":");
                    }

                    writer.Write("  ");
                    writer.Write(pc.ToString("D4"));
                    writer.Write(": ");
                    writer.WriteLine(FormatInstruction(image, pc, image.Code[pc], labels, options));
                }
            }

            if (options.IncludeSideTables)
                WriteSideTables(writer, image, labels, options);

            writer.Write($"bytecode size: {image.Code.Length} instructions");
        }

        private static void WriteHeader(TextWriter writer, CodeImage image)
        {
            writer.WriteLine($"register bytecode image");
            writer.Write("flags=");
            writer.Write(image.Flags);
            writer.Write(" code=");
            writer.Write(image.Code.Length);
            writer.Write(" methods=");
            writer.Write(image.Methods.Length);
            writer.Write(" eh=");
            writer.Write(image.EhRegions.Length);
            writer.Write(" gcsp=");
            writer.Write(image.GcSafePoints.Length);
            writer.Write(" roots=");
            writer.Write(image.GcRoots.Length);
            writer.Write(" unwind=");
            writer.Write(image.Unwind.Length);
            writer.Write(" switch=");
            writer.Write(image.SwitchTable.Length);
            writer.Write(" typelayout=");
            writer.Write(image.TypeLayouts.Length);
            writer.Write(" vtable=");
            writer.Write(image.VTables.Length);
            writer.Write(" blob=");
            writer.Write(image.Blob.Length);
            writer.WriteLine();
        }

        private static void WriteSideTables(TextWriter writer, CodeImage image, Dictionary<int, string> labels, ImagePrintOptions options)
        {
            writer.WriteLine();
            writer.WriteLine("side tables");


            if (image.EhRegions.Length != 0)
            {
                writer.WriteLine("  eh:");
                for (int i = 0; i < image.EhRegions.Length; i++)
                {
                    var e = image.EhRegions[i];
                    writer.Write("    #");
                    writer.Write(i);
                    writer.Write(' ');
                    writer.Write((EhRegionKind)e.Kind);
                    writer.Write(" try=");
                    writer.Write(FormatPcRange(e.TryStartPc, e.TryEndPc, labels, options));
                    writer.Write(" handler=");
                    writer.Write(FormatPcRange(e.HandlerStartPc, e.HandlerEndPc, labels, options));
                    if (e.FilterStartPc >= 0)
                    {
                        writer.Write(" filter=");
                        writer.Write(FormatTarget(e.FilterStartPc, labels, options));
                    }
                    if (e.CatchTypeId >= 0)
                    {
                        writer.Write(" catch=T");
                        writer.Write(e.CatchTypeId);
                    }
                    if (e.ParentRegionIndex >= 0)
                    {
                        writer.Write(" parent=#");
                        writer.Write(e.ParentRegionIndex);
                    }
                    writer.WriteLine();
                }
            }

            if (image.GcSafePoints.Length != 0)
            {
                writer.WriteLine("  gc safepoints:");
                for (int i = 0; i < image.GcSafePoints.Length; i++)
                {
                    var sp = image.GcSafePoints[i];
                    writer.Write("    #");
                    writer.Write(i);
                    writer.Write(" pc=");
                    writer.Write(FormatTarget(sp.Pc, labels, options));
                    writer.Write(" roots=");
                    writer.Write(sp.RootStartIndex);
                    writer.Write("+");
                    writer.Write(sp.RootCount);
                    if (sp.Flags != 0)
                    {
                        writer.Write(" flags=0x");
                        writer.Write(sp.Flags.ToString("X4"));
                    }
                    writer.WriteLine();

                    for (int r = 0; r < sp.RootCount; r++)
                    {
                        int rootIndex = sp.RootStartIndex + r;
                        if ((uint)rootIndex >= (uint)image.GcRoots.Length)
                            continue;

                        writer.Write("      ");
                        writer.Write(FormatGcRoot(rootIndex, image.GcRoots[rootIndex]));
                        writer.WriteLine();
                    }
                }
            }

            if (image.Unwind.Length != 0)
            {
                writer.WriteLine("  unwind:");
                for (int i = 0; i < image.Unwind.Length; i++)
                {
                    var u = image.Unwind[i];
                    writer.Write("    #");
                    writer.Write(i);
                    writer.Write(" pc=");
                    writer.Write(FormatTarget(u.Pc, labels, options));
                    writer.Write(' ');
                    writer.Write((UnwindCodeKind)u.Kind);
                    writer.Write(" reg=");
                    writer.Write(FormatRegister(u.Register));
                    writer.Write(" stack+");
                    writer.Write(u.StackOffset);
                    writer.Write(" size=");
                    writer.Write(u.Size);
                    writer.WriteLine();
                }
            }

            if (image.SwitchTable.Length != 0)
            {
                writer.WriteLine("  switch:");
                for (int i = 0; i < image.SwitchTable.Length; i++)
                {
                    var s = image.SwitchTable[i];
                    writer.Write("    #");
                    writer.Write(i);
                    writer.Write(" key=");
                    writer.Write(s.Key);
                    writer.Write(" target=");
                    writer.Write(FormatTarget(s.TargetPc, labels, options));
                    writer.WriteLine();
                }
            }

            if (image.TypeLayouts.Length != 0)
            {
                writer.WriteLine("  type-layouts:");
                for (int i = 0; i < image.TypeLayouts.Length; i++)
                {
                    var t = image.TypeLayouts[i];
                    writer.Write("    TL");
                    writer.Write(i);
                    writer.Write(" T");
                    writer.Write(t.RuntimeTypeId);
                    writer.Write(" size=");
                    writer.Write(t.Size);
                    writer.Write(" align=");
                    writer.Write(t.Align);
                    writer.Write(" instance=");
                    writer.Write(t.InstanceSize);
                    writer.Write(" static=");
                    writer.Write(t.StaticSize);
                    writer.Write("/");
                    writer.Write(t.StaticAlign);
                    if (t.ElementTypeLayoutIndex >= 0)
                    {
                        writer.Write(" elem=TL");
                        writer.Write(t.ElementTypeLayoutIndex);
                    }
                    if (t.VTableCount != 0)
                    {
                        writer.Write(" vtable=");
                        writer.Write(t.VTableStartIndex);
                        writer.Write("+");
                        writer.Write(t.VTableCount);
                    }
                    if (t.Flags != 0)
                    {
                        writer.Write(" flags=");
                        writer.Write((TypeLayoutFlags)t.Flags);
                    }
                    writer.WriteLine();
                }
            }

            if (image.VTables.Length != 0)
            {
                writer.WriteLine("  vtables:");
                for (int i = 0; i < image.VTables.Length; i++)
                {
                    var v = image.VTables[i];
                    writer.Write("    VT");
                    writer.Write(i);
                    writer.Write(" target=");
                    writer.Write(v.TargetPc < 0 ? "null" : FormatTarget(v.TargetPc, labels, options));
                    writer.WriteLine();
                }
            }

            if (image.Blob.Length != 0)
            {
                writer.Write("  blob: ");
                int count = Math.Min(image.Blob.Length, Math.Max(0, options.MaxBlobBytes));
                for (int i = 0; i < count; i++)
                {
                    if (i != 0)
                        writer.Write(' ');
                    writer.Write(image.Blob[i].ToString("X2"));
                }
                if (count < image.Blob.Length)
                {
                    writer.Write(" ... +");
                    writer.Write(image.Blob.Length - count);
                    writer.Write(" bytes");
                }
                writer.WriteLine();
            }
        }

        private static Dictionary<int, string> BuildLabels(CodeImage image)
        {
            var targets = new SortedSet<int>();

            for (int i = 0; i < image.Methods.Length; i++)
                targets.Add(image.Methods[i].EntryPc);

            for (int pc = 0; pc < image.Code.Length; pc++)
            {
                var inst = image.Code[pc];
                if (IsPcTargetInstruction(inst.Op))
                    AddTarget(targets, inst.Imm, image.Code.Length);
                else if (IsSwitchInstruction(inst.Op))
                {
                    int start = CheckedIndex(inst.Imm, image.SwitchTable.Length);
                    int count = inst.Aux;
                    for (int i = 0; i < count && start + i < image.SwitchTable.Length; i++)
                        targets.Add(image.SwitchTable[start + i].TargetPc);
                }
            }

            for (int i = 0; i < image.EhRegions.Length; i++)
            {
                var e = image.EhRegions[i];
                targets.Add(e.TryStartPc);
                targets.Add(e.TryEndPc);
                targets.Add(e.HandlerStartPc);
                targets.Add(e.HandlerEndPc);
                if (e.FilterStartPc >= 0)
                    targets.Add(e.FilterStartPc);
            }

            var labels = new Dictionary<int, string>();
            foreach (int pc in targets)
            {
                if ((uint)pc <= (uint)image.Code.Length)
                    labels[pc] = $"L{pc:D4}";
            }
            return labels;
        }

        private static void AddTarget(SortedSet<int> targets, long pc, int codeLength)
        {
            if (pc >= 0 && pc <= codeLength)
                targets.Add((int)pc);
        }

        private static int CheckedIndex(long value, int length)
        {
            if (value < 0 || value > int.MaxValue)
                return length;
            return (int)value;
        }

        private static string FormatInstruction(CodeImage image, int pc, InstrDesc inst, Dictionary<int, string> labels, ImagePrintOptions options)
        {
            var sb = new StringBuilder();
            sb.Append(inst.Op);

            string operands = FormatOperands(image, inst, labels, options);
            if (operands.Length != 0)
            {
                sb.Append(' ');
                sb.Append(operands);
            }

            string annotation = FormatAnnotation(inst);
            if (annotation.Length != 0)
            {
                sb.Append(" ; ");
                sb.Append(annotation);
            }

            if (options.IncludeRawOperands)
            {
                sb.Append(" ; raw rd=");
                sb.Append(FormatRegister(inst.Rd));
                sb.Append(" rs1=");
                sb.Append(FormatRegister(inst.Rs1));
                sb.Append(" rs2=");
                sb.Append(FormatRegister(inst.Rs2));
                sb.Append(" rs3=");
                sb.Append(FormatRegister(inst.Rs3));
                sb.Append(" aux=0x");
                sb.Append(inst.Aux.ToString("X4"));
                sb.Append(" imm=");
                sb.Append(inst.Imm);
            }

            return sb.ToString();
        }

        private static string FormatOperands(CodeImage image, InstrDesc inst, Dictionary<int, string> labels, ImagePrintOptions options)
        {
            if (inst.Op == Op.Nop || inst.Op == Op.Break || inst.Op == Op.GcPoll || inst.Op == Op.RetVoid || inst.Op == Op.EndFinally || inst.Op == Op.Rethrow)
                return string.Empty;

            if (inst.Op == Op.Trap)
                return FormatImmediate(inst.Imm);

            if (inst.Op is Op.RetI or Op.RetF or Op.RetRef)
                return FormatRegister(inst.Rs1);

            if (inst.Op == Op.RetValue)
                return $"{FormatRegister(inst.Rs1)}, size={inst.Imm}";

            if (inst.Op == Op.Throw)
                return FormatRegister(inst.Rs1);

            if (inst.Op == Op.LdExceptionRef)
                return FormatRegister(inst.Rd);

            if (IsPcTargetInstruction(inst.Op))
            {
                if (inst.Op is Op.J or Op.Leave)
                    return FormatTarget(inst.Imm, labels, options);
                if (IsTwoOperandBranch(inst.Op))
                    return FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2) + ", " + FormatTarget(inst.Imm, labels, options);
                return FormatRegister(inst.Rs1) + ", " + FormatTarget(inst.Imm, labels, options);
            }

            if (IsSwitchInstruction(inst.Op))
                return $"{FormatRegister(inst.Rs1)}, switchTable#{inst.Imm}+{inst.Aux}";

            if (IsMove(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);

            if (IsLoadImmediate(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatLoadImmediate(inst);

            if (IsIntegerImmediate(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatImmediate(inst.Imm);

            if (IsIntegerBinaryOrCompare(inst.Op))
            {
                if (IsIntegerUnary(inst.Op))
                    return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2);
            }

            if (IsFloatArithmetic(inst.Op))
            {
                if (IsFloatUnary(inst.Op))
                    return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2);
            }

            if (IsFloatCompare(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2);

            if (IsFloatPredicate(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);

            if (IsConversion(inst.Op))
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);

            if (IsAddressMemoryInstruction(inst.Op))
                return FormatAddressMemoryOperands(inst);

            if (IsInstanceFieldInstruction(inst.Op))
                return $"{FormatRegister(inst.Rd)}, [{FormatRegister(inst.Rs1)}+{InstrDesc.FieldOffset(inst.Imm)}] size={InstrDesc.FieldSize(inst.Imm)}{FormatFieldFlags(inst.Aux)}";


            if (IsArrayInstruction(inst.Op))
            {
                if (inst.Op == Op.LdLen)
                    return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);
                if (inst.Op == Op.LdArrayDataAddr)
                    return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + "[" + FormatRegister(inst.Rs2) + "]" + FormatTypeLayoutSuffix(inst.Imm);
            }

            if (inst.Op == Op.AllocObj)
                return $"{FormatRegister(inst.Rd)}, layout=TL{inst.Imm}";

            if (inst.Op is Op.NewArr or Op.NewSZArray)
                return $"{FormatRegister(inst.Rd)}, len={FormatRegister(inst.Rs1)}, layout=TL{inst.Imm}";

            if (inst.Op is Op.CastClass or Op.IsInst)
                return $"{FormatRegister(inst.Rd)}, {FormatRegister(inst.Rs1)}, T{inst.Imm}";

            if (inst.Op is Op.Box or Op.UnboxAny or Op.UnboxAddr or Op.InitObj or Op.DefaultValue or Op.SizeOf)
                return $"{FormatRegister(inst.Rd)}, {FormatRegister(inst.Rs1)}, TL{inst.Imm}";

            if (inst.Op == Op.LdVTableEntry)
                return $"{FormatRegister(inst.Rd)}, [{FormatRegister(inst.Rs1)}.vtable+{inst.Imm}]";

            if (inst.Op is Op.RefEq or Op.RefNe or Op.RuntimeTypeEquals)
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2);

            if (IsNonInternalDirectCallInstruction(inst.Op))
                return FormatTarget(inst.Imm, labels, options) + FormatCallFlags(inst.Aux);

            if (IsInternalDirectCall(inst.Op))
                return $"internal M{inst.Imm}" + FormatCallFlags(inst.Aux);

            if (IsIndirectCall(inst.Op))
                return "*" + FormatRegister(inst.Rs1) + FormatCallFlags(inst.Aux);


            if (inst.Op == Op.StaticData)
                return $"{FormatRegister(inst.Rd)}, blobOffset={(int)(inst.Imm >> 32)}, length={unchecked((int)(uint)inst.Imm)}";

            if (inst.Op == Op.StackAlloc)
                return $"{FormatRegister(inst.Rd)}, count={FormatRegister(inst.Rs1)}, elemSize={inst.Imm}";

            if (inst.Op == Op.AllocHGlobal)
                return FormatRegister(inst.Rd) + ", byteCount=" + FormatRegister(inst.Rs1);

            if (inst.Op == Op.FreeHGlobal)
                return FormatRegister(inst.Rs1);

            if (IsPointerOp(inst.Op))
                return FormatPointerOperands(inst);

            return FormatGenericOperands(inst);
        }
        private static string FormatAnnotation(InstrDesc inst)
        {
            if (inst.Aux == 0)
                return string.Empty;

            if (IsAddressMemoryInstruction(inst.Op))
                return FormatMemoryAux(inst.Aux);

            if (IsDirectCall(inst.Op) || IsIndirectCall(inst.Op))
                return string.Empty;

            var flags = (InstructionFlags)inst.Aux;
            return flags == InstructionFlags.None ? $"aux=0x{inst.Aux:X4}" : $"flags={flags}";
        }

        private static string FormatAddressMemoryOperands(InstrDesc inst)
        {
            return inst.Op switch
            {
                Op.CpObj => $"{FormatRegister(inst.Rd)}, {FormatRegister(inst.Rs1)}, TL{inst.Imm}",
                Op.CpBlk => $"{FormatRegister(inst.Rd)}, {FormatRegister(inst.Rs1)}, size={inst.Imm}",
                Op.InitBlk => $"{FormatRegister(inst.Rd)}, size={inst.Imm}",
                Op.NullCheck => FormatRegister(inst.Rs1),
                Op.BoundsCheck => FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2),
                Op.WriteBarrier => FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1),
                _ => FormatRegister(inst.Rd) + ", " + FormatMemory(inst),
            };
        }

        private static string FormatGenericOperands(InstrDesc inst)
        {
            var parts = new List<string>(5);
            if (inst.Rd != RegisterVmIsa.InvalidRegister) parts.Add(FormatRegister(inst.Rd));
            if (inst.Rs1 != RegisterVmIsa.InvalidRegister) parts.Add(FormatRegister(inst.Rs1));
            if (inst.Rs2 != RegisterVmIsa.InvalidRegister) parts.Add(FormatRegister(inst.Rs2));
            if (inst.Rs3 != RegisterVmIsa.InvalidRegister) parts.Add(FormatRegister(inst.Rs3));
            if (inst.Imm != 0) parts.Add(FormatImmediate(inst.Imm));
            return string.Join(", ", parts);
        }

        private static string FormatLoadImmediate(InstrDesc inst)
        {
            if (inst.Op == Op.LiNull)
                return "null";
            if (inst.Op == Op.LiString)
                return $"string#{inst.Imm}";
            if (inst.Op == Op.LiTypeHandle)
                return $"typeHandle T{inst.Imm}";
            if (inst.Op == Op.LiMethodHandle)
                return $"methodHandle M{inst.Imm}";
            if (inst.Op == Op.LiFieldHandle)
                return $"fieldHandle F{inst.Imm}";
            if (inst.Op == Op.LiStaticBase)
                return $"staticBase T{inst.Imm}";
            if (inst.Op == Op.LiF32Bits)
                return $"bits=0x{unchecked((uint)inst.Imm):X8}";
            if (inst.Op == Op.LiF64Bits)
                return $"bits=0x{unchecked((ulong)inst.Imm):X16}";
            return FormatImmediate(inst.Imm);
        }

        private static string FormatFieldFlags(ushort aux)
        {
            FieldAccessFlags flags = Aux.FieldFlags(aux);
            return flags == FieldAccessFlags.None ? string.Empty : $" fieldFlags={flags}";
        }

        private static string FormatCallFlags(ushort aux)
        {
            var flags = (CallFlags)aux;
            return flags == CallFlags.None ? string.Empty : $" flags={flags}";
        }

        private static string FormatPointerOperands(InstrDesc inst)
        {
            if (inst.Op == Op.PtrSub || inst.Op == Op.PtrDiff)
                return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1) + ", " + FormatRegister(inst.Rs2);
            if (inst.Op is Op.PtrAddI32 or Op.PtrAddI64 or Op.ByRefAddI32 or Op.ByRefAddI64)
                return $"{FormatRegister(inst.Rd)}, {FormatRegister(inst.Rs1)}, {FormatRegister(inst.Rs2)}, elemSize={inst.Imm}";
            return FormatRegister(inst.Rd) + ", " + FormatRegister(inst.Rs1);
        }

        private static string FormatMemory(InstrDesc inst)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            sb.Append(FormatMemoryBase(inst));

            if (inst.Rs2 != RegisterVmIsa.InvalidRegister)
            {
                sb.Append(" + ");
                sb.Append(FormatRegister(inst.Rs2));
                int scale = Aux.MemoryScaleLog2(inst.Aux);
                if (scale != 0)
                {
                    sb.Append(" << ");
                    sb.Append(scale);
                }
            }

            if (inst.Imm > 0)
            {
                sb.Append(" + ");
                sb.Append(inst.Imm);
            }
            else if (inst.Imm < 0)
            {
                sb.Append(" - ");
                sb.Append(unchecked((ulong)(-inst.Imm)));
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatMemoryBase(InstrDesc inst)
        {
            var memoryBase = Aux.MemoryBase(inst.Aux);
            return memoryBase switch
            {
                MemoryBase.StackPointer => "sp",
                MemoryBase.FramePointer => "fp",
                MemoryBase.ThreadPointer => "tp",
                MemoryBase.GlobalPointer => "gp",
                _ => FormatRegister(inst.Rs1),
            };
        }

        private static string FormatMemoryAux(ushort aux)
        {
            int scale = Aux.MemoryScaleLog2(aux);
            int align = Aux.MemoryAlignmentLog2(aux);
            var memoryBase = Aux.MemoryBase(aux);
            var flags = Aux.MemoryFlags(aux);

            var sb = new StringBuilder();
            sb.Append("mem base=");
            sb.Append(memoryBase);
            if (scale != 0)
            {
                sb.Append(" scale=2^");
                sb.Append(scale);
            }
            if (align != 0)
            {
                sb.Append(" align=2^");
                sb.Append(align);
            }
            if (flags != MemoryFlags.None)
            {
                sb.Append(" flags=");
                sb.Append(flags);
            }
            return sb.ToString();
        }

        private static string FormatGcRoot(int index, GcRootRecord root)
        {
            var sb = new StringBuilder();
            sb.Append('#');
            sb.Append(index);
            sb.Append(' ');
            sb.Append((GcRootKind)root.Kind);
            if (root.Register != RegisterVmIsa.InvalidRegister)
            {
                sb.Append(" reg=");
                sb.Append(FormatRegister(root.Register));
            }
            if (root.FrameOffset >= 0)
            {
                sb.Append(' ');
                sb.Append((RegisterFrameBase)root.FrameBase);
                sb.Append('+');
                sb.Append(root.FrameOffset);
            }
            sb.Append(" size=");
            sb.Append(root.Size);
            if (root.RuntimeTypeId >= 0)
            {
                sb.Append(" type=T");
                sb.Append(root.RuntimeTypeId);
            }
            if (root.CellOffset != 0)
            {
                sb.Append(" cell+");
                sb.Append(root.CellOffset);
            }
            AppendFlags(sb, " flags", (GcRootFlags)root.Flags);
            return sb.ToString();
        }

        private static string FormatTarget(long target, Dictionary<int, string> labels, ImagePrintOptions options)
        {
            if (target >= 0 && target <= int.MaxValue)
            {
                int pc = (int)target;
                if (options.UseLabels && labels.TryGetValue(pc, out string? label))
                    return label;
                return pc.ToString();
            }
            return target.ToString();
        }
        private static string FormatPcRange(int startPc, int endPc, Dictionary<int, string> labels, ImagePrintOptions options)
            => $"[{FormatTarget(startPc, labels, options)}..{FormatTarget(endPc, labels, options)})";

        private static string FormatRegister(byte register)
            => register == RegisterVmIsa.InvalidRegister ? "-" : MachineRegisters.Format((MachineRegister)register);

        private static string FormatImmediate(long value)
        {
            if (value >= -16 && value <= 16)
                return value.ToString();
            return $"{value} / 0x{unchecked((ulong)value):X}";
        }

        private static string FormatTypeLayoutSuffix(long typeLayoutIndex)
            => typeLayoutIndex < 0 ? string.Empty : $" type=TL{typeLayoutIndex}";

        private static void AppendFlags(TextWriter writer, string name, CallFlags flags)
        {
            if (flags == CallFlags.None)
                return;
            writer.Write(name);
            writer.Write('=');
            writer.Write(flags);
        }

        private static void AppendFlags(StringBuilder sb, string name, GcRootFlags flags)
        {
            if (flags == GcRootFlags.None)
                return;
            sb.Append(name);
            sb.Append('=');
            sb.Append(flags);
        }

        private static bool IsMove(Op op)
            => op is Op.MovI or Op.MovF or Op.MovRef or Op.MovPtr;

        private static bool IsLoadImmediate(Op op)
            => op is Op.LiI32 or Op.LiI64 or Op.LiF32Bits or Op.LiF64Bits or Op.LiNull or Op.LiString
                or Op.LiTypeHandle or Op.LiMethodHandle or Op.LiFieldHandle or Op.LiStaticBase;

        private static bool IsPcTargetInstruction(Op op)
            => op is Op.J or Op.Leave or Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64 or Op.BrTrueRef or Op.BrFalseRef
                or Op.BrI32Eq or Op.BrI32Ne or Op.BrI32Lt or Op.BrI32Le or Op.BrI32Gt or Op.BrI32Ge
                or Op.BrU32Lt or Op.BrU32Le or Op.BrU32Gt or Op.BrU32Ge
                or Op.BrI64Eq or Op.BrI64Ne or Op.BrI64Lt or Op.BrI64Le or Op.BrI64Gt or Op.BrI64Ge
                or Op.BrU64Lt or Op.BrU64Le or Op.BrU64Gt or Op.BrU64Ge
                or Op.BrRefEq or Op.BrRefNe
                or Op.BrF32Eq or Op.BrF32Ne or Op.BrF32Lt or Op.BrF32Le or Op.BrF32Gt or Op.BrF32Ge
                or Op.BrF64Eq or Op.BrF64Ne or Op.BrF64Lt or Op.BrF64Le or Op.BrF64Gt or Op.BrF64Ge;

        private static bool IsSwitchInstruction(Op op)
            => op is Op.SwitchI32 or Op.SwitchI64;

        private static bool IsIntegerImmediate(Op op)
            => op is Op.I32AddImm or Op.I32SubImm or Op.I32MulImm or Op.I32AndImm or Op.I32OrImm or Op.I32XorImm
                or Op.I32ShlImm or Op.I32ShrImm or Op.U32ShrImm or Op.I32EqImm or Op.I32NeImm or Op.I32LtImm
                or Op.I32LeImm or Op.I32GtImm or Op.I32GeImm or Op.U32LtImm
                or Op.I64AddImm or Op.I64SubImm or Op.I64MulImm or Op.I64AndImm or Op.I64OrImm or Op.I64XorImm
                or Op.I64ShlImm or Op.I64ShrImm or Op.U64ShrImm or Op.I64EqImm or Op.I64NeImm or Op.I64LtImm
                or Op.I64LeImm or Op.I64GtImm or Op.I64GeImm or Op.U64LtImm;

        private static bool IsIntegerBinaryOrCompare(Op op)
            => IsOpInRange(op, Op.I32Add, Op.U32Max) || IsOpInRange(op, Op.I64Add, Op.U64Max);

        private static bool IsIntegerUnary(Op op)
            => op is Op.I32Neg or Op.I32Not or Op.I64Neg or Op.I64Not;

        private static bool IsFloatArithmetic(Op op)
            => op is Op.F32Add or Op.F32Sub or Op.F32Mul or Op.F32Div or Op.F32Rem or Op.F32Neg or Op.F32Abs or Op.F32Min or Op.F32Max
                or Op.F64Add or Op.F64Sub or Op.F64Mul or Op.F64Div or Op.F64Rem or Op.F64Neg or Op.F64Abs or Op.F64Min or Op.F64Max;

        private static bool IsFloatUnary(Op op)
            => op is Op.F32Neg or Op.F32Abs or Op.F64Neg or Op.F64Abs;

        private static bool IsFloatCompare(Op op)
            => op is Op.F32Eq or Op.F32Ne or Op.F32Lt or Op.F32Le or Op.F32Gt or Op.F32Ge
                or Op.F64Eq or Op.F64Ne or Op.F64Lt or Op.F64Le or Op.F64Gt or Op.F64Ge;

        private static bool IsFloatPredicate(Op op)
            => op is Op.F32IsNaN or Op.F32IsFinite or Op.F64IsNaN or Op.F64IsFinite;

        private static bool IsConversion(Op op)
            => IsOpInRange(op, Op.I32ToI64, Op.U64ToU32Ovf);

        private static bool IsAddressMemoryInstruction(Op op)
            => IsOpInRange(op, Op.LdI1, Op.WriteBarrier);

        private static bool IsInstanceFieldInstruction(Op op)
            => IsOpInRange(op, Op.LdFldI1, Op.StFldObj);


        private static bool IsArrayInstruction(Op op)
            => IsOpInRange(op, Op.LdLen, Op.LdArrayDataAddr);

        private static bool IsDirectCall(Op op)
            => IsNonInternalDirectCallInstruction(op) || IsInternalDirectCall(op);

        private static bool IsNonInternalDirectCallInstruction(Op op)
            => op is Op.CallVoid or Op.CallI or Op.CallF or Op.CallRef or Op.CallValue;

        private static bool IsInternalDirectCall(Op op)
            => IsOpInRange(op, Op.CallInternalVoid, Op.CallInternalValue);


        private static bool IsIndirectCall(Op op)
            => IsOpInRange(op, Op.CallIndirectVoid, Op.CallIndirectValue);

        private static bool IsPointerOp(Op op)
            => IsOpInRange(op, Op.StackAlloc, Op.FreeHGlobal);

        private static bool IsTwoOperandBranch(Op op)
            => op is not (Op.BrTrueI32 or Op.BrFalseI32 or Op.BrTrueI64 or Op.BrFalseI64 or Op.BrTrueRef or Op.BrFalseRef);

        private static bool IsOpInRange(Op op, Op first, Op last)
            => (ushort)op >= (ushort)first && (ushort)op <= (ushort)last;
    }
}
