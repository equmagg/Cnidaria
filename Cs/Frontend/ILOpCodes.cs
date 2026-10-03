using System;

namespace Cnidaria.Cs
{
    public enum ILOpCode : ushort
    {
        Nop = 0x00,
        Break = 0x01,
        Ldarg_0 = 0x02,
        Ldarg_1 = 0x03,
        Ldarg_2 = 0x04,
        Ldarg_3 = 0x05,
        Ldloc_0 = 0x06,
        Ldloc_1 = 0x07,
        Ldloc_2 = 0x08,
        Ldloc_3 = 0x09,
        Stloc_0 = 0x0A,
        Stloc_1 = 0x0B,
        Stloc_2 = 0x0C,
        Stloc_3 = 0x0D,
        Ldarg_S = 0x0E,
        Ldarga_S = 0x0F,
        Starg_S = 0x10,
        Ldloc_S = 0x11,
        Ldloca_S = 0x12,
        Stloc_S = 0x13,
        Ldnull = 0x14,
        Ldc_I4_M1 = 0x15,
        Ldc_I4_0 = 0x16,
        Ldc_I4_1 = 0x17,
        Ldc_I4_2 = 0x18,
        Ldc_I4_3 = 0x19,
        Ldc_I4_4 = 0x1A,
        Ldc_I4_5 = 0x1B,
        Ldc_I4_6 = 0x1C,
        Ldc_I4_7 = 0x1D,
        Ldc_I4_8 = 0x1E,
        Ldc_I4_S = 0x1F,
        Ldc_I4 = 0x20,
        Ldc_I8 = 0x21,
        Ldc_R4 = 0x22,
        Ldc_R8 = 0x23,
        Dup = 0x25,
        Pop = 0x26,
        Jmp = 0x27,
        Call = 0x28,
        Calli = 0x29,
        Ret = 0x2A,
        Br_S = 0x2B,
        Brfalse_S = 0x2C,
        Brtrue_S = 0x2D,
        Beq_S = 0x2E,
        Bge_S = 0x2F,
        Bgt_S = 0x30,
        Ble_S = 0x31,
        Blt_S = 0x32,
        Bne_Un_S = 0x33,
        Bge_Un_S = 0x34,
        Bgt_Un_S = 0x35,
        Ble_Un_S = 0x36,
        Blt_Un_S = 0x37,
        Br = 0x38,
        Brfalse = 0x39,
        Brtrue = 0x3A,
        Beq = 0x3B,
        Bge = 0x3C,
        Bgt = 0x3D,
        Ble = 0x3E,
        Blt = 0x3F,
        Bne_Un = 0x40,
        Bge_Un = 0x41,
        Bgt_Un = 0x42,
        Ble_Un = 0x43,
        Blt_Un = 0x44,
        Switch = 0x45,
        Ldind_I1 = 0x46,
        Ldind_U1 = 0x47,
        Ldind_I2 = 0x48,
        Ldind_U2 = 0x49,
        Ldind_I4 = 0x4A,
        Ldind_U4 = 0x4B,
        Ldind_I8 = 0x4C,
        Ldind_I = 0x4D,
        Ldind_R4 = 0x4E,
        Ldind_R8 = 0x4F,
        Ldind_Ref = 0x50,
        Stind_Ref = 0x51,
        Stind_I1 = 0x52,
        Stind_I2 = 0x53,
        Stind_I4 = 0x54,
        Stind_I8 = 0x55,
        Stind_R4 = 0x56,
        Stind_R8 = 0x57,
        Add = 0x58,
        Sub = 0x59,
        Mul = 0x5A,
        Div = 0x5B,
        Div_Un = 0x5C,
        Rem = 0x5D,
        Rem_Un = 0x5E,
        And = 0x5F,
        Or = 0x60,
        Xor = 0x61,
        Shl = 0x62,
        Shr = 0x63,
        Shr_Un = 0x64,
        Neg = 0x65,
        Not = 0x66,
        Conv_I1 = 0x67,
        Conv_I2 = 0x68,
        Conv_I4 = 0x69,
        Conv_I8 = 0x6A,
        Conv_R4 = 0x6B,
        Conv_R8 = 0x6C,
        Conv_U4 = 0x6D,
        Conv_U8 = 0x6E,
        Callvirt = 0x6F,
        Cpobj = 0x70,
        Ldobj = 0x71,
        Ldstr = 0x72,
        Newobj = 0x73,
        Castclass = 0x74,
        Isinst = 0x75,
        Conv_R_Un = 0x76,
        Unbox = 0x79,
        Throw = 0x7A,
        Ldfld = 0x7B,
        Ldflda = 0x7C,
        Stfld = 0x7D,
        Ldsfld = 0x7E,
        Ldsflda = 0x7F,
        Stsfld = 0x80,
        Stobj = 0x81,
        Conv_Ovf_I1_Un = 0x82,
        Conv_Ovf_I2_Un = 0x83,
        Conv_Ovf_I4_Un = 0x84,
        Conv_Ovf_I8_Un = 0x85,
        Conv_Ovf_U1_Un = 0x86,
        Conv_Ovf_U2_Un = 0x87,
        Conv_Ovf_U4_Un = 0x88,
        Conv_Ovf_U8_Un = 0x89,
        Conv_Ovf_I_Un = 0x8A,
        Conv_Ovf_U_Un = 0x8B,
        Box = 0x8C,
        Newarr = 0x8D,
        Ldlen = 0x8E,
        Ldelema = 0x8F,
        Ldelem_I1 = 0x90,
        Ldelem_U1 = 0x91,
        Ldelem_I2 = 0x92,
        Ldelem_U2 = 0x93,
        Ldelem_I4 = 0x94,
        Ldelem_U4 = 0x95,
        Ldelem_I8 = 0x96,
        Ldelem_I = 0x97,
        Ldelem_R4 = 0x98,
        Ldelem_R8 = 0x99,
        Ldelem_Ref = 0x9A,
        Stelem_I = 0x9B,
        Stelem_I1 = 0x9C,
        Stelem_I2 = 0x9D,
        Stelem_I4 = 0x9E,
        Stelem_I8 = 0x9F,
        Stelem_R4 = 0xA0,
        Stelem_R8 = 0xA1,
        Stelem_Ref = 0xA2,
        Ldelem = 0xA3,
        Stelem = 0xA4,
        Unbox_Any = 0xA5,
        Conv_Ovf_I1 = 0xB3,
        Conv_Ovf_U1 = 0xB4,
        Conv_Ovf_I2 = 0xB5,
        Conv_Ovf_U2 = 0xB6,
        Conv_Ovf_I4 = 0xB7,
        Conv_Ovf_U4 = 0xB8,
        Conv_Ovf_I8 = 0xB9,
        Conv_Ovf_U8 = 0xBA,
        Refanyval = 0xC2,
        Ckfinite = 0xC3,
        Mkrefany = 0xC6,
        Ldtoken = 0xD0,
        Conv_U2 = 0xD1,
        Conv_U1 = 0xD2,
        Conv_I = 0xD3,
        Conv_Ovf_I = 0xD4,
        Conv_Ovf_U = 0xD5,
        Add_Ovf = 0xD6,
        Add_Ovf_Un = 0xD7,
        Mul_Ovf = 0xD8,
        Mul_Ovf_Un = 0xD9,
        Sub_Ovf = 0xDA,
        Sub_Ovf_Un = 0xDB,
        Endfinally = 0xDC,
        Leave = 0xDD,
        Leave_S = 0xDE,
        Stind_I = 0xDF,
        Conv_U = 0xE0,
        Arglist = 0xFE00,
        Ceq = 0xFE01,
        Cgt = 0xFE02,
        Cgt_Un = 0xFE03,
        Clt = 0xFE04,
        Clt_Un = 0xFE05,
        Ldftn = 0xFE06,
        Ldvirtftn = 0xFE07,
        Ldarg = 0xFE09,
        Ldarga = 0xFE0A,
        Starg = 0xFE0B,
        Ldloc = 0xFE0C,
        Ldloca = 0xFE0D,
        Stloc = 0xFE0E,
        Localloc = 0xFE0F,
        Endfilter = 0xFE11,
        Unaligned = 0xFE12,
        Volatile = 0xFE13,
        Tail = 0xFE14,
        Initobj = 0xFE15,
        Constrained = 0xFE16,
        Cpblk = 0xFE17,
        Initblk = 0xFE18,
        No = 0xFE19,
        Rethrow = 0xFE1A,
        Sizeof = 0xFE1C,
        Refanytype = 0xFE1D,
        Readonly = 0xFE1E,
    }
    public enum ILOperandKind : byte
    {
        None,
        Int8,
        UInt8,
        UInt16,
        Int32,
        Int64,
        Float32,
        Float64,
        ShortBranch,
        Branch,
        Switch,
        Token,
    }
    public static class ILOpCodes
    {
        private static readonly ILOperandKind[] s_oneByte = new ILOperandKind[256];
        private static readonly ILOperandKind[] s_twoByte = new ILOperandKind[32];
        private static readonly bool[] s_validOneByte = new bool[256];
        private static readonly bool[] s_validTwoByte = new bool[32];

        static ILOpCodes()
        {
            foreach (ILOpCode op in Enum.GetValues<ILOpCode>())
            {
                if ((ushort)op >= 0xFE00)
                    s_validTwoByte[(ushort)op & 0xFF] = true;
                else
                    s_validOneByte[(ushort)op] = true;
            }
            Set(ILOperandKind.UInt8, ILOpCode.Ldarg_S, ILOpCode.Ldarga_S, ILOpCode.Starg_S, ILOpCode.Ldloc_S, ILOpCode.Ldloca_S, ILOpCode.Stloc_S, ILOpCode.Unaligned);
            Set(ILOperandKind.Int8, ILOpCode.Ldc_I4_S);
            Set(ILOperandKind.UInt16, ILOpCode.Ldarg, ILOpCode.Ldarga, ILOpCode.Starg, ILOpCode.Ldloc, ILOpCode.Ldloca, ILOpCode.Stloc);
            Set(ILOperandKind.Int32, ILOpCode.Ldc_I4);
            Set(ILOperandKind.Int64, ILOpCode.Ldc_I8);
            Set(ILOperandKind.Float32, ILOpCode.Ldc_R4);
            Set(ILOperandKind.Float64, ILOpCode.Ldc_R8);
            Set(ILOperandKind.Switch, ILOpCode.Switch);
            for (ushort op = (ushort)ILOpCode.Br_S; op <= (ushort)ILOpCode.Blt_Un_S; op++)
                s_oneByte[op] = ILOperandKind.ShortBranch;
            for (ushort op = (ushort)ILOpCode.Br; op <= (ushort)ILOpCode.Blt_Un; op++)
                s_oneByte[op] = ILOperandKind.Branch;
            Set(ILOperandKind.ShortBranch, ILOpCode.Leave_S);
            Set(ILOperandKind.Branch, ILOpCode.Leave);
            Set(ILOperandKind.Token,
                ILOpCode.Jmp, ILOpCode.Call, ILOpCode.Calli, ILOpCode.Callvirt, ILOpCode.Cpobj, ILOpCode.Ldobj, ILOpCode.Ldstr,
                ILOpCode.Newobj, ILOpCode.Castclass, ILOpCode.Isinst, ILOpCode.Unbox, ILOpCode.Ldfld, ILOpCode.Ldflda, ILOpCode.Stfld,
                ILOpCode.Ldsfld, ILOpCode.Ldsflda, ILOpCode.Stsfld, ILOpCode.Stobj, ILOpCode.Box, ILOpCode.Newarr, ILOpCode.Ldelema,
                ILOpCode.Ldelem, ILOpCode.Stelem, ILOpCode.Unbox_Any, ILOpCode.Refanyval, ILOpCode.Mkrefany, ILOpCode.Ldtoken,
                ILOpCode.Ldftn, ILOpCode.Ldvirtftn, ILOpCode.Initobj, ILOpCode.Constrained, ILOpCode.Sizeof);
        }
        private static void Set(ILOperandKind kind, params ILOpCode[] ops)
        {
            foreach (var op in ops)
            {
                if ((ushort)op >= 0xFE00)
                    s_twoByte[(ushort)op & 0xFF] = kind;
                else
                    s_oneByte[(ushort)op] = kind;
            }
        }
        public static ILOperandKind OperandKind(ILOpCode op)
            => (ushort)op >= 0xFE00 ? s_twoByte[(ushort)op & 0xFF] : s_oneByte[(ushort)op];
        public static int OpCodeSize(ILOpCode op) => (ushort)op >= 0xFE00 ? 2 : 1;
        public static int OperandSize(ILOperandKind kind) => kind switch
        {
            ILOperandKind.None => 0,
            ILOperandKind.Int8 or ILOperandKind.UInt8 or ILOperandKind.ShortBranch => 1,
            ILOperandKind.UInt16 => 2,
            ILOperandKind.Int32 or ILOperandKind.Float32 or ILOperandKind.Branch or ILOperandKind.Token => 4,
            ILOperandKind.Int64 or ILOperandKind.Float64 => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        public static bool TryDecode(ReadOnlySpan<byte> code, int offset, out ILOpCode op)
        {
            byte b = code[offset];
            if (b == 0xFE)
            {
                if (offset + 1 >= code.Length)
                {
                    op = default;
                    return false;
                }
                byte second = code[offset + 1];
                op = (ILOpCode)(0xFE00 | second);
                return second < s_validTwoByte.Length && s_validTwoByte[second];
            }
            op = (ILOpCode)b;
            return s_validOneByte[b];
        }
        public static ILOpCode ToLongBranch(ILOpCode op) => op switch
        {
            ILOpCode.Leave_S => ILOpCode.Leave,
            >= ILOpCode.Br_S and <= ILOpCode.Blt_Un_S => (ILOpCode)((ushort)op + (ILOpCode.Br - ILOpCode.Br_S)),
            _ => op,
        };
        public static ILOpCode ToShortBranch(ILOpCode op) => op switch
        {
            ILOpCode.Leave => ILOpCode.Leave_S,
            >= ILOpCode.Br and <= ILOpCode.Blt_Un => (ILOpCode)((ushort)op - (ILOpCode.Br - ILOpCode.Br_S)),
            _ => op,
        };
    }
}
