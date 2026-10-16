using System;

namespace Cnidaria.X86;

internal static partial class X86Forms
{
    private const X86IsaFlags Sse = X86IsaFlags.Sse;
    private const X86IsaFlags Sse2 = X86IsaFlags.Sse2;
    private const X86IsaFlags Sse3 = X86IsaFlags.Sse3;
    private const X86IsaFlags Ssse3 = X86IsaFlags.Ssse3;
    private const X86IsaFlags Sse41 = X86IsaFlags.Sse41;
    private const X86IsaFlags Sse42 = X86IsaFlags.Sse42;
    private const X86IsaFlags Avx = X86IsaFlags.Avx;
    private const X86IsaFlags Avx2 = X86IsaFlags.Avx2;
    private const X86IsaFlags NoYmm = X86IsaFlags.None;
    private const X86IsaFlags F = X86IsaFlags.Avx512F;
    private const X86IsaFlags BW = X86IsaFlags.Avx512BW;
    private const X86IsaFlags DQ = X86IsaFlags.Avx512DQ;
    private const X86IsaFlags CD = X86IsaFlags.Avx512CD;
    private const X86IsaFlags Vbmi = X86IsaFlags.Avx512Vbmi;
    private const X86IsaFlags Vbmi2 = X86IsaFlags.Avx512Vbmi2;
    private const X86IsaFlags Avx10v2 = X86IsaFlags.Avx10v2;
    private const X86IsaFlags Fma = X86IsaFlags.Fma;
    private const X86IsaFlags AvxVnni = X86IsaFlags.AvxVnni;
    private const X86IsaFlags AvxVnniInt8 = X86IsaFlags.AvxVnniInt8;
    private const X86IsaFlags AvxVnniInt16 = X86IsaFlags.AvxVnniInt16;

    private static void Build()
    {
        BuildGeneral();
        BuildScalarSse();
        BuildPackedSse();
        BuildAvx();
    }

    private static void BuildGeneral()
    {
        Def(X86InstrKind.Nop, "90", "");
        Def(X86InstrKind.Pause, "F3 90", "");
        Def(X86InstrKind.Ret, "C3", "");
        Def(X86InstrKind.Leave, "C9", "");
        Def(X86InstrKind.Int3, "CC", "");
        Def(X86InstrKind.Ud2, "0F 0B", "");
        Def(X86InstrKind.Syscall, "0F 05", "");
        Def(X86InstrKind.Cqo, "REX.W 99", "");
        Def(X86InstrKind.Cdq, "99", "");
        Def(X86InstrKind.Cdqe, "REX.W 98", "");
        Def(X86InstrKind.Cbw, "66 98", "");
        Def(X86InstrKind.Cwde, "98", "");
        Def(X86InstrKind.Mfence, "0F AE !F0", "", Sse2);
        Def(X86InstrKind.Sfence, "0F AE !F8", "", Sse);
        Def(X86InstrKind.Lfence, "0F AE !E8", "", Sse2);

        Def(X86InstrKind.Push, "50+r", "ov", flags: X86FormFlags.Default64);
        Def(X86InstrKind.Push, "6A", "ibs");
        Def(X86InstrKind.Push, "68", "id");
        Def(X86InstrKind.Push, "FF /6", "rmv", flags: X86FormFlags.Default64);
        Def(X86InstrKind.Pop, "58+r", "ov", flags: X86FormFlags.Default64);
        Def(X86InstrKind.Pop, "8F /0", "rmv", flags: X86FormFlags.Default64);

        Def(X86InstrKind.Mov, "8A /r", "r8, rm8");
        Def(X86InstrKind.Mov, "8B /r", "rv, rmv");
        Def(X86InstrKind.Mov, "88 /r", "rm8, r8");
        Def(X86InstrKind.Mov, "89 /r", "rmv, rv");
        Def(X86InstrKind.Mov, "REX.W C7 /0", "rm64, ids", flags: X86FormFlags.Mode64);
        Def(X86InstrKind.Mov, "B0+r", "o8, ib");
        Def(X86InstrKind.Mov, "B8+r", "ov, iv");
        Def(X86InstrKind.Mov, "C6 /0", "rm8, ib");
        Def(X86InstrKind.Mov, "C7 /0", "rmv, iz");
        Def(X86InstrKind.Lea, "8D /r", "rv, m");
        Def(X86InstrKind.Movsx, "0F BE /r", "rv, rm8");
        Def(X86InstrKind.Movsx, "0F BF /r", "rv, rm16");
        Def(X86InstrKind.Movzx, "0F B6 /r", "rv, rm8");
        Def(X86InstrKind.Movzx, "0F B7 /r", "rv, rm16");
        Def(X86InstrKind.Movsxd, "REX.W 63 /r", "r64, rm32", flags: X86FormFlags.Mode64);

        var alu = new[] { X86InstrKind.Add, X86InstrKind.Or, X86InstrKind.Adc, X86InstrKind.Sbb, X86InstrKind.And, X86InstrKind.Sub, X86InstrKind.Xor, X86InstrKind.Cmp };
        for (int n = 0; n < alu.Length; n++)
        {
            int b = n * 8;
            Def(alu[n], $"83 /{n}", "rmv, ibs");
            Def(alu[n], $"80 /{n}", "rm8, ib");
            Def(alu[n], $"81 /{n}", "rmv, iz");
            Def(alu[n], $"{b + 2:X2} /r", "r8, rm8");
            Def(alu[n], $"{b + 3:X2} /r", "rv, rmv");
            Def(alu[n], $"{b:X2} /r", "rm8, r8");
            Def(alu[n], $"{b + 1:X2} /r", "rmv, rv");
        }

        Def(X86InstrKind.Test, "F6 /0", "rm8, ib");
        Def(X86InstrKind.Test, "F7 /0", "rmv, iz");
        Def(X86InstrKind.Test, "84 /r", "rm8, r8");
        Def(X86InstrKind.Test, "85 /r", "rmv, rv");
        Def(X86InstrKind.Xchg, "86 /r", "rm8, r8");
        Def(X86InstrKind.Xchg, "87 /r", "rmv, rv");
        Def(X86InstrKind.Cmpxchg, "0F B0 /r", "rm8, r8");
        Def(X86InstrKind.Cmpxchg, "0F B1 /r", "rmv, rv");
        Def(X86InstrKind.Xadd, "0F C0 /r", "rm8, r8");
        Def(X86InstrKind.Xadd, "0F C1 /r", "rmv, rv");

        Def(X86InstrKind.Inc, "FE /0", "rm8");
        Def(X86InstrKind.Inc, "FF /0", "rmv");
        Def(X86InstrKind.Dec, "FE /1", "rm8");
        Def(X86InstrKind.Dec, "FF /1", "rmv");
        var group3 = new[] { (X86InstrKind.Not, 2), (X86InstrKind.Neg, 3), (X86InstrKind.Mul, 4), (X86InstrKind.Imul, 5), (X86InstrKind.Div, 6), (X86InstrKind.Idiv, 7) };
        foreach (var (kind, n) in group3)
        {
            Def(kind, $"F6 /{n}", "rm8");
            Def(kind, $"F7 /{n}", "rmv");
        }
        Def(X86InstrKind.Imul, "0F AF /r", "rv, rmv");
        Def(X86InstrKind.Imul, "6B /r", "rv, rmv, ibs");
        Def(X86InstrKind.Imul, "69 /r", "rv, rmv, iz");

        var shifts = new[] { (X86InstrKind.Rol, 0), (X86InstrKind.Ror, 1), (X86InstrKind.Shl, 4), (X86InstrKind.Shr, 5), (X86InstrKind.Sar, 7) };
        foreach (var (kind, n) in shifts)
        {
            Def(kind, $"D0 /{n}", "rm8, one");
            Def(kind, $"D1 /{n}", "rmv, one");
            Def(kind, $"D2 /{n}", "rm8, cl");
            Def(kind, $"D3 /{n}", "rmv, cl");
            Def(kind, $"C0 /{n}", "rm8, ib");
            Def(kind, $"C1 /{n}", "rmv, ib");
        }

        Def(X86InstrKind.Call, "E8", "rel32");
        Def(X86InstrKind.Call, "FF /2", "rmv", flags: X86FormFlags.Default64);
        Def(X86InstrKind.Jmp, "E9", "rel32");
        Def(X86InstrKind.Jmp, "EB", "rel8");
        Def(X86InstrKind.Jmp, "FF /4", "rmv", flags: X86FormFlags.Default64);
        Def(X86InstrKind.Jcc, "0F 80+cc", "rel32");
        Def(X86InstrKind.Jcc, "70+cc", "rel8");
        Def(X86InstrKind.Setcc, "0F 90+cc /0", "rm8");
        Def(X86InstrKind.Cmovcc, "0F 40+cc /r", "rv, rmv");

        Def(X86InstrKind.Bsf, "0F BC /r", "ry, rmy");
        Def(X86InstrKind.Bsr, "0F BD /r", "ry, rmy");
        Def(X86InstrKind.Lzcnt, "F3 0F BD /r", "ry, rmy", X86IsaFlags.Lzcnt);
        Def(X86InstrKind.Crc32, "F2 0F38 F0 /r", "r32, rm8", Sse42);
        Def(X86InstrKind.Crc32, "F2 REX.W 0F38 F0 /r", "r64, rm8", Sse42, X86FormFlags.Mode64);
        Def(X86InstrKind.Crc32, "66 F2 0F38 F1 /r", "r32, rm16", Sse42);
        Def(X86InstrKind.Crc32, "F2 0F38 F1 /r", "r32, rm32", Sse42);
        Def(X86InstrKind.Crc32, "F2 REX.W 0F38 F1 /r", "r64, rm64", Sse42, X86FormFlags.Mode64);
        Def(X86InstrKind.Prefetchnta, "0F 18 /0", "m8", Sse);
        Def(X86InstrKind.Prefetcht0, "0F 18 /1", "m8", Sse);
        Def(X86InstrKind.Prefetcht1, "0F 18 /2", "m8", Sse);
        Def(X86InstrKind.Prefetcht2, "0F 18 /3", "m8", Sse);
        Def(X86InstrKind.Movnti, "0F C3 /r", "m32, r32", Sse2);
        Def(X86InstrKind.Movnti, "REX.W 0F C3 /r", "m64, r64", Sse2, X86FormFlags.Mode64);
        Def(X86InstrKind.Vzeroupper, "VEX.128.NP.0F.WIG 77", "", Avx);
        Def(X86InstrKind.Vzeroall, "VEX.256.NP.0F.WIG 77", "", Avx);
        Def(X86InstrKind.Serialize, "NP 0F 01 !E8", "", X86IsaFlags.Serialize);

        Def(X86InstrKind.Popcnt, "F3 0F B8 /r", "ry, rmy", X86IsaFlags.Popcnt);
        Def(X86InstrKind.Tzcnt, "F3 0F BC /r", "ry, rmy", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Andn, "VEX.LZ.NP.0F38 F2 /r", "ry, vry, rmy", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Bextr, "VEX.LZ.NP.0F38 F7 /r", "ry, rmy, vry", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Blsr, "VEX.LZ.NP.0F38 F3 /1", "vry, rmy", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Blsmsk, "VEX.LZ.NP.0F38 F3 /2", "vry, rmy", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Blsi, "VEX.LZ.NP.0F38 F3 /3", "vry, rmy", X86IsaFlags.Bmi1);
        Def(X86InstrKind.Bzhi, "VEX.LZ.NP.0F38 F5 /r", "ry, rmy, vry", X86IsaFlags.Bmi2);
        Def(X86InstrKind.Pdep, "VEX.LZ.F2.0F38 F5 /r", "ry, vry, rmy", X86IsaFlags.Bmi2);
        Def(X86InstrKind.Pext, "VEX.LZ.F3.0F38 F5 /r", "ry, vry, rmy", X86IsaFlags.Bmi2);
        Def(X86InstrKind.Mulx, "VEX.LZ.F2.0F38 F6 /r", "ry, vry, rmy", X86IsaFlags.Bmi2);
    }

    private static void BuildScalarSse()
    {
        Def(X86InstrKind.Movss, "F3 0F 10 /r", "x, xm32", Sse);
        Def(X86InstrKind.Movss, "F3 0F 11 /r", "m32, x", Sse);
        Def(X86InstrKind.Movss, "VEX.LIG.F3.0F.WIG 10 /r", "x, m32", Avx);
        Def(X86InstrKind.Movss, "VEX.LIG.F3.0F.WIG 11 /r", "m32, x", Avx);
        Def(X86InstrKind.Movss, "VEX.LIG.F3.0F.WIG 10 /r", "x, vx, xr", Avx);
        Def(X86InstrKind.Movsd, "F2 0F 10 /r", "x, xm64", Sse2);
        Def(X86InstrKind.Movsd, "F2 0F 11 /r", "m64, x", Sse2);
        Def(X86InstrKind.Movsd, "VEX.LIG.F2.0F.WIG 10 /r", "x, m64", Avx);
        Def(X86InstrKind.Movsd, "VEX.LIG.F2.0F.WIG 11 /r", "m64, x", Avx);
        Def(X86InstrKind.Movsd, "VEX.LIG.F2.0F.WIG 10 /r", "x, vx, xr", Avx);

        foreach (var (single, dbl, op, rounding) in new[]
        {
            (X86InstrKind.Addss, X86InstrKind.Addsd, "58", true), (X86InstrKind.Subss, X86InstrKind.Subsd, "5C", true),
            (X86InstrKind.Mulss, X86InstrKind.Mulsd, "59", true), (X86InstrKind.Divss, X86InstrKind.Divsd, "5E", true),
            (X86InstrKind.Sqrtss, X86InstrKind.Sqrtsd, "51", true), (X86InstrKind.Minss, X86InstrKind.Minsd, "5D", false),
            (X86InstrKind.Maxss, X86InstrKind.Maxsd, "5F", false),
        })
        {
            ScalarBinary(single, "F3 0F " + op, Sse, 32, evex: rounding ? new Evex("W0", F, 4, Flags: Rounded) : null);
            ScalarBinary(dbl, "F2 0F " + op, Sse2, 64, evex: rounding ? new Evex("W1", F, 8, Flags: Rounded) : null);
        }
        ScalarBinary(X86InstrKind.Rcpss, "F3 0F 53", Sse, 32);
        ScalarBinary(X86InstrKind.Rsqrtss, "F3 0F 52", Sse, 32);
        ScalarBinary(X86InstrKind.Cvtss2sd, "F3 0F 5A", Sse2, 32);
        ScalarBinary(X86InstrKind.Cvtsd2ss, "F2 0F 5A", Sse2, 64, evex: new Evex("W1", F, 8, Flags: Rounded));
        ScalarBinary(X86InstrKind.Cmpss, "F3 0F C2", Sse, 32, ", ib");
        ScalarBinary(X86InstrKind.Cmpsd, "F2 0F C2", Sse2, 64, ", ib");
        ScalarBinary(X86InstrKind.Roundss, "66 0F3A 0A", Sse41, 32, ", ib");
        ScalarBinary(X86InstrKind.Roundsd, "66 0F3A 0B", Sse41, 64, ", ib");

        foreach (var (kind, op, isa, bits) in new[]
        {
            (X86InstrKind.Ucomiss, "NP 0F 2E", Sse, 32), (X86InstrKind.Ucomisd, "66 0F 2E", Sse2, 64),
            (X86InstrKind.Comiss, "NP 0F 2F", Sse, 32), (X86InstrKind.Comisd, "66 0F 2F", Sse2, 64),
        })
        {
            Def(kind, op + " /r", $"x, xm{bits}", isa);
            Def(kind, Vex("LIG", op) + " /r", $"x, xm{bits}", Avx);
        }

        Def(X86InstrKind.Cvtsi2ss, "F3 0F 2A /r", "x, rmy", Sse);
        Def(X86InstrKind.Cvtsi2ss, "VEX.LIG.F3.0F 2A /r", "x, vx, rmy", Avx);
        Def(X86InstrKind.Cvtsi2ss, "EVEX.LIG.F3.0F.W0 2A /r", "x, vx, rm32", F, X86FormFlags.Rounding, X86Tuple.Scalar, 4);
        Def(X86InstrKind.Cvtsi2ss, "EVEX.LIG.F3.0F.W1 2A /r", "x, vx, rm64", F, X86FormFlags.Rounding | X86FormFlags.Mode64, X86Tuple.Scalar, 8);
        Def(X86InstrKind.Cvtsi2sd, "F2 0F 2A /r", "x, rmy", Sse2);
        Def(X86InstrKind.Cvtsi2sd, "VEX.LIG.F2.0F 2A /r", "x, vx, rmy", Avx);
        Def(X86InstrKind.Cvtsi2sd, "EVEX.LIG.F2.0F.W1 2A /r", "x, vx, rm64", F, X86FormFlags.Rounding | X86FormFlags.Mode64, X86Tuple.Scalar, 8);
        foreach (var (kind, op, isa, bits) in new[]
        {
            (X86InstrKind.Cvttss2si, "F3 0F 2C", Sse, 32), (X86InstrKind.Cvttsd2si, "F2 0F 2C", Sse2, 64),
            (X86InstrKind.Cvtss2si, "F3 0F 2D", Sse, 32), (X86InstrKind.Cvtsd2si, "F2 0F 2D", Sse2, 64),
        })
        {
            Def(kind, op + " /r", $"ry, xm{bits}", isa);
            Def(kind, Vex("LIG", op) + " /r", $"ry, xm{bits}", Avx);
            if (kind is X86InstrKind.Cvtss2si or X86InstrKind.Cvtsd2si)
            {
                var (pp, map, opcode) = SplitLegacy(op);
                Def(kind, $"EVEX.LIG.{pp}.{map}.W0 {opcode} /r", $"r32, xm{bits}", F, X86FormFlags.Rounding, X86Tuple.Scalar, bits / 8);
                Def(kind, $"EVEX.LIG.{pp}.{map}.W1 {opcode} /r", $"r64, xm{bits}", F, X86FormFlags.Rounding | X86FormFlags.Mode64, X86Tuple.Scalar, bits / 8);
            }
        }

        Def(X86InstrKind.Movd, "66 0F 6E /r", "x, rm32", Sse2);
        Def(X86InstrKind.Movd, "66 0F 7E /r", "rm32, x", Sse2);
        Def(X86InstrKind.Movd, "VEX.128.66.0F.W0 6E /r", "x, rm32", Avx);
        Def(X86InstrKind.Movd, "VEX.128.66.0F.W0 7E /r", "rm32, x", Avx);
        Def(X86InstrKind.Movq, "F3 0F 7E /r", "x, xm64", Sse2);
        Def(X86InstrKind.Movq, "66 REX.W 0F 6E /r", "x, rm64", Sse2, X86FormFlags.Mode64);
        Def(X86InstrKind.Movq, "66 REX.W 0F 7E /r", "rm64, x", Sse2, X86FormFlags.Mode64);
        Def(X86InstrKind.Movq, "66 0F D6 /r", "xm64, x", Sse2);
        Def(X86InstrKind.Movq, "VEX.128.F3.0F.WIG 7E /r", "x, xm64", Avx);
        Def(X86InstrKind.Movq, "VEX.128.66.0F.W1 6E /r", "x, rm64", Avx, X86FormFlags.Mode64);
        Def(X86InstrKind.Movq, "VEX.128.66.0F.W1 7E /r", "rm64, x", Avx, X86FormFlags.Mode64);
        Def(X86InstrKind.Movq, "VEX.128.66.0F.WIG D6 /r", "xm64, x", Avx);
    }

    private static void BuildPackedSse()
    {
        Move(X86InstrKind.Movaps, "NP 0F 28", "NP 0F 29", Sse, new Evex("W0", F, 4));
        Move(X86InstrKind.Movups, "NP 0F 10", "NP 0F 11", Sse, new Evex("W0", F, 4));
        Move(X86InstrKind.Movapd, "66 0F 28", "66 0F 29", Sse2, new Evex("W1", F, 8));
        Move(X86InstrKind.Movupd, "66 0F 10", "66 0F 11", Sse2, new Evex("W1", F, 8));
        Move(X86InstrKind.Movdqa, "66 0F 6F", "66 0F 7F", Sse2);
        Move(X86InstrKind.Movdqu, "F3 0F 6F", "F3 0F 7F", Sse2);

        foreach (var (kind, load, store, isa) in new[]
        {
            (X86InstrKind.Movlps, "NP 0F 12", "NP 0F 13", Sse), (X86InstrKind.Movhps, "NP 0F 16", "NP 0F 17", Sse),
            (X86InstrKind.Movlpd, "66 0F 12", "66 0F 13", Sse2), (X86InstrKind.Movhpd, "66 0F 16", "66 0F 17", Sse2),
        })
        {
            Def(kind, load + " /r", "x, m64", isa);
            Def(kind, store + " /r", "m64, x", isa);
            Def(kind, Vex("128", load) + " /r", "x, vx, m64", Avx);
            Def(kind, Vex("128", store) + " /r", "m64, x", Avx);
        }
        Def(X86InstrKind.Movhlps, "NP 0F 12 /r", "x, xr", Sse);
        Def(X86InstrKind.Movhlps, "VEX.128.NP.0F.WIG 12 /r", "x, vx, xr", Avx);
        Def(X86InstrKind.Movlhps, "NP 0F 16 /r", "x, xr", Sse);
        Def(X86InstrKind.Movlhps, "VEX.128.NP.0F.WIG 16 /r", "x, vx, xr", Avx);

        foreach (var (kind, op, isa, ymm, w) in new[]
        {
            (X86InstrKind.Movntps, "NP 0F 2B", Sse, Avx, "W0"), (X86InstrKind.Movntpd, "66 0F 2B", Sse2, Avx, "W1"), (X86InstrKind.Movntdq, "66 0F E7", Sse2, Avx, "W0"),
        })
        {
            Def(kind, op + " /r", "m128, x", isa);
            Def(kind, Vex("128", op) + " /r", "m128, x", Avx);
            Def(kind, Vex("256", op) + " /r", "m256, y", ymm);
            Def(kind, EvexOpcode("512", op + " /r", w), "m512, z", F, X86FormFlags.None, X86Tuple.FullMemory);
        }
        Def(X86InstrKind.Movntdqa, "66 0F38 2A /r", "x, m128", Sse41);
        Def(X86InstrKind.Movntdqa, "VEX.128.66.0F38.WIG 2A /r", "x, m128", Avx);
        Def(X86InstrKind.Movntdqa, "VEX.256.66.0F38.WIG 2A /r", "y, m256", Avx2);
        Def(X86InstrKind.Movntdqa, "EVEX.512.66.0F38.W0 2A /r", "z, m512", F, X86FormFlags.None, X86Tuple.FullMemory);
        Def(X86InstrKind.Lddqu, "F2 0F F0 /r", "x, m128", Sse3);
        Def(X86InstrKind.Lddqu, "VEX.128.F2.0F.WIG F0 /r", "x, m128", Avx);
        Def(X86InstrKind.Lddqu, "VEX.256.F2.0F.WIG F0 /r", "y, m256", Avx);
        Def(X86InstrKind.Maskmovdqu, "66 0F F7 /r", "x, xr", Sse2);
        Def(X86InstrKind.Maskmovdqu, "VEX.128.66.0F.WIG F7 /r", "x, xr", Avx);

        foreach (var (kind, op, evex) in new[]
        {
            (X86InstrKind.Addps, "58", new Evex("W0", F, 4, Flags: Rounded)), (X86InstrKind.Subps, "5C", new Evex("W0", F, 4, Flags: Rounded)),
            (X86InstrKind.Mulps, "59", new Evex("W0", F, 4, Flags: Rounded)), (X86InstrKind.Divps, "5E", new Evex("W0", F, 4, Flags: Rounded)),
            (X86InstrKind.Andps, "54", new Evex("W0", DQ, 4)), (X86InstrKind.Orps, "56", new Evex("W0", DQ, 4)),
            (X86InstrKind.Xorps, "57", new Evex("W0", DQ, 4)), (X86InstrKind.Andnps, "55", new Evex("W0", DQ, 4)),
            (X86InstrKind.Minps, "5D", new Evex("W0", F, 4)), (X86InstrKind.Maxps, "5F", new Evex("W0", F, 4)),
            (X86InstrKind.Unpcklps, "14", new Evex("W0", F, 4)), (X86InstrKind.Unpckhps, "15", new Evex("W0", F, 4)),
        })
        {
            Binary(kind, "NP 0F " + op, Sse, Avx, evex: evex);
        }
        foreach (var (kind, op, evex) in new[]
        {
            (X86InstrKind.Addpd, "58", new Evex("W1", F, 8, Flags: Rounded)), (X86InstrKind.Subpd, "5C", new Evex("W1", F, 8, Flags: Rounded)),
            (X86InstrKind.Mulpd, "59", new Evex("W1", F, 8, Flags: Rounded)), (X86InstrKind.Divpd, "5E", new Evex("W1", F, 8, Flags: Rounded)),
            (X86InstrKind.Andpd, "54", new Evex("W1", DQ, 8)), (X86InstrKind.Orpd, "56", new Evex("W1", DQ, 8)),
            (X86InstrKind.Xorpd, "57", new Evex("W1", DQ, 8)), (X86InstrKind.Andnpd, "55", new Evex("W1", DQ, 8)),
            (X86InstrKind.Minpd, "5D", new Evex("W1", F, 8)), (X86InstrKind.Maxpd, "5F", new Evex("W1", F, 8)),
            (X86InstrKind.Unpcklpd, "14", new Evex("W1", F, 8)), (X86InstrKind.Unpckhpd, "15", new Evex("W1", F, 8)),
        })
        {
            Binary(kind, "66 0F " + op, Sse2, Avx, evex: evex);
        }
        Binary(X86InstrKind.Shufps, "NP 0F C6", Sse, Avx, ", ib", new Evex("W0", F, 4));
        Binary(X86InstrKind.Shufpd, "66 0F C6", Sse2, Avx, ", ib", new Evex("W1", F, 8));
        Binary(X86InstrKind.Cmpps, "NP 0F C2", Sse, Avx, ", ib", new Evex("W0", F, 4, MaskResult: true));
        Binary(X86InstrKind.Cmppd, "66 0F C2", Sse2, Avx, ", ib", new Evex("W1", F, 8, MaskResult: true));

        foreach (var (kind, op, evex) in new[]
        {
            (X86InstrKind.Paddb, "FC", Bytes), (X86InstrKind.Paddw, "FD", Bytes),
            (X86InstrKind.Paddd, "FE", new Evex("W0", F, 4)), (X86InstrKind.Paddq, "D4", new Evex("W1", F, 8)),
            (X86InstrKind.Psubb, "F8", Bytes), (X86InstrKind.Psubw, "F9", Bytes),
            (X86InstrKind.Psubd, "FA", new Evex("W0", F, 4)), (X86InstrKind.Psubq, "FB", new Evex("W1", F, 8)),
            (X86InstrKind.Paddsb, "EC", Bytes), (X86InstrKind.Paddsw, "ED", Bytes), (X86InstrKind.Paddusb, "DC", Bytes), (X86InstrKind.Paddusw, "DD", Bytes),
            (X86InstrKind.Psubsb, "E8", Bytes), (X86InstrKind.Psubsw, "E9", Bytes), (X86InstrKind.Psubusb, "D8", Bytes), (X86InstrKind.Psubusw, "D9", Bytes),
            (X86InstrKind.Pmullw, "D5", Bytes), (X86InstrKind.Pmulhw, "E5", Bytes), (X86InstrKind.Pmulhuw, "E4", Bytes),
            (X86InstrKind.Pmuludq, "F4", new Evex("W1", F, 8)),
            (X86InstrKind.Pand, "DB", new Evex("W0", F, 4, "vpandd")), (X86InstrKind.Pandn, "DF", new Evex("W0", F, 4, "vpandnd")),
            (X86InstrKind.Por, "EB", new Evex("W0", F, 4, "vpord")), (X86InstrKind.Pxor, "EF", new Evex("W0", F, 4, "vpxord")),
            (X86InstrKind.Pcmpeqb, "74", Bytes with { MaskResult = true }), (X86InstrKind.Pcmpeqw, "75", Bytes with { MaskResult = true }),
            (X86InstrKind.Pcmpeqd, "76", new Evex("W0", F, 4, MaskResult: true)),
            (X86InstrKind.Pcmpgtb, "64", Bytes with { MaskResult = true }), (X86InstrKind.Pcmpgtw, "65", Bytes with { MaskResult = true }),
            (X86InstrKind.Pcmpgtd, "66", new Evex("W0", F, 4, MaskResult: true)),
            (X86InstrKind.Pminub, "DA", Bytes), (X86InstrKind.Pmaxub, "DE", Bytes), (X86InstrKind.Pminsw, "EA", Bytes), (X86InstrKind.Pmaxsw, "EE", Bytes),
            (X86InstrKind.Psadbw, "F6", Bytes with { Flags = X86FormFlags.None }),
            (X86InstrKind.Packsswb, "63", Bytes), (X86InstrKind.Packssdw, "6B", new Evex("W0", BW, 4)), (X86InstrKind.Packuswb, "67", Bytes),
            (X86InstrKind.Punpcklbw, "60", Bytes), (X86InstrKind.Punpcklwd, "61", Bytes),
            (X86InstrKind.Punpckldq, "62", new Evex("W0", F, 4)), (X86InstrKind.Punpcklqdq, "6C", new Evex("W1", F, 8)),
            (X86InstrKind.Punpckhbw, "68", Bytes), (X86InstrKind.Punpckhwd, "69", Bytes),
            (X86InstrKind.Punpckhdq, "6A", new Evex("W0", F, 4)), (X86InstrKind.Punpckhqdq, "6D", new Evex("W1", F, 8)),
            (X86InstrKind.Pavgb, "E0", Bytes), (X86InstrKind.Pavgw, "E3", Bytes), (X86InstrKind.Pmaddwd, "F5", Bytes),
        })
        {
            Binary(kind, "66 0F " + op, Sse2, Avx2, evex: evex);
        }

        Binary(X86InstrKind.Haddps, "F2 0F 7C", Sse3, Avx);
        Binary(X86InstrKind.Haddpd, "66 0F 7C", Sse3, Avx);
        Binary(X86InstrKind.Hsubps, "F2 0F 7D", Sse3, Avx);
        Binary(X86InstrKind.Hsubpd, "66 0F 7D", Sse3, Avx);
        Binary(X86InstrKind.Addsubps, "F2 0F D0", Sse3, Avx);
        Binary(X86InstrKind.Addsubpd, "66 0F D0", Sse3, Avx);

        foreach (var (kind, op, evex) in new[]
        {
            (X86InstrKind.Pshufb, "00", (Evex?)Bytes), (X86InstrKind.Phaddw, "01", null), (X86InstrKind.Phaddd, "02", null), (X86InstrKind.Phaddsw, "03", null),
            (X86InstrKind.Pmaddubsw, "04", Bytes), (X86InstrKind.Phsubw, "05", null), (X86InstrKind.Phsubd, "06", null), (X86InstrKind.Phsubsw, "07", null),
            (X86InstrKind.Psignb, "08", null), (X86InstrKind.Psignw, "09", null), (X86InstrKind.Psignd, "0A", null), (X86InstrKind.Pmulhrsw, "0B", Bytes),
        })
        {
            Binary(kind, "66 0F38 " + op, Ssse3, Avx2, evex: evex);
        }
        Binary(X86InstrKind.Palignr, "66 0F3A 0F", Ssse3, Avx2, ", ib", Bytes);

        foreach (var (kind, op, evex) in new[]
        {
            (X86InstrKind.Pmuldq, "28", new Evex("W1", F, 8)), (X86InstrKind.Pcmpeqq, "29", new Evex("W1", F, 8, MaskResult: true)),
            (X86InstrKind.Packusdw, "2B", new Evex("W0", BW, 4)),
            (X86InstrKind.Pminsb, "38", Bytes), (X86InstrKind.Pminsd, "39", new Evex("W0", F, 4)), (X86InstrKind.Pminuw, "3A", Bytes), (X86InstrKind.Pminud, "3B", new Evex("W0", F, 4)),
            (X86InstrKind.Pmaxsb, "3C", Bytes), (X86InstrKind.Pmaxsd, "3D", new Evex("W0", F, 4)), (X86InstrKind.Pmaxuw, "3E", Bytes), (X86InstrKind.Pmaxud, "3F", new Evex("W0", F, 4)),
            (X86InstrKind.Pmulld, "40", new Evex("W0", F, 4)),
        })
        {
            Binary(kind, "66 0F38 " + op, Sse41, Avx2, evex: evex);
        }
        Binary(X86InstrKind.Pcmpgtq, "66 0F38 37", Sse42, Avx2, evex: new Evex("W1", F, 8, MaskResult: true));
        Binary(X86InstrKind.Blendps, "66 0F3A 0C", Sse41, Avx, ", ib");
        Binary(X86InstrKind.Blendpd, "66 0F3A 0D", Sse41, Avx, ", ib");
        Binary(X86InstrKind.Pblendw, "66 0F3A 0E", Sse41, Avx2, ", ib");
        Binary(X86InstrKind.Dpps, "66 0F3A 40", Sse41, Avx, ", ib");
        Binary(X86InstrKind.Dppd, "66 0F3A 41", Sse41, NoYmm, ", ib");
        Binary(X86InstrKind.Mpsadbw, "66 0F3A 42", Sse41, Avx2, ", ib");
        foreach (var (kind, op) in new[] { (X86InstrKind.Aesenc, "DC"), (X86InstrKind.Aesenclast, "DD"), (X86InstrKind.Aesdec, "DE"), (X86InstrKind.Aesdeclast, "DF") })
        {
            Def(kind, $"66 0F38 {op} /r", "x, xm", X86IsaFlags.Aes);
            Def(kind, $"VEX.128.66.0F38.WIG {op} /r", "x, vx, xm", X86IsaFlags.Aes | Avx);
        }
        Def(X86InstrKind.Aesimc, "66 0F38 DB /r", "x, xm", X86IsaFlags.Aes);
        Def(X86InstrKind.Aesimc, "VEX.128.66.0F38.WIG DB /r", "x, xm", X86IsaFlags.Aes | Avx);
        Def(X86InstrKind.Aeskeygenassist, "66 0F3A DF /r", "x, xm, ib", X86IsaFlags.Aes);
        Def(X86InstrKind.Aeskeygenassist, "VEX.128.66.0F3A.WIG DF /r", "x, xm, ib", X86IsaFlags.Aes | Avx);
        Def(X86InstrKind.Pclmulqdq, "66 0F3A 44 /r", "x, xm, ib", X86IsaFlags.Pclmulqdq);
        Def(X86InstrKind.Pclmulqdq, "VEX.128.66.0F3A.WIG 44 /r", "x, vx, xm, ib", X86IsaFlags.Pclmulqdq | Avx);
        Def(X86InstrKind.Pclmulqdq, "VEX.256.66.0F3A.WIG 44 /r", "y, vy, ym, ib", X86IsaFlags.Vpclmulqdq | Avx);
        Vl(X86InstrKind.Pclmulqdq, "EVEX.L.66.0F3A.WIG 44 /r", "V, vV, VM, ib", X86IsaFlags.Vpclmulqdq | F, X86Tuple.FullMemory, flags: X86FormFlags.None);
        Def(X86InstrKind.Gf2p8mulb, "66 0F38 CF /r", "x, xm", X86IsaFlags.Gfni);
        Def(X86InstrKind.Gf2p8mulb, "VEX.128.66.0F38.W0 CF /r", "x, vx, xm", X86IsaFlags.Gfni | Avx);
        Def(X86InstrKind.Gf2p8mulb, "VEX.256.66.0F38.W0 CF /r", "y, vy, ym", X86IsaFlags.Gfni | Avx);
        Vl(X86InstrKind.Gf2p8mulb, "EVEX.L.66.0F38.W0 CF /r", "V, vV, VM", X86IsaFlags.Gfni | F, X86Tuple.FullMemory);
        foreach (var (kind, op) in new[] { (X86InstrKind.Gf2p8affineqb, "CE"), (X86InstrKind.Gf2p8affineinvqb, "CF") })
        {
            Def(kind, $"66 0F3A {op} /r", "x, xm, ib", X86IsaFlags.Gfni);
            Def(kind, $"VEX.128.66.0F3A.W1 {op} /r", "x, vx, xm, ib", X86IsaFlags.Gfni | Avx);
            Def(kind, $"VEX.256.66.0F3A.W1 {op} /r", "y, vy, ym, ib", X86IsaFlags.Gfni | Avx);
            Vl(kind, $"EVEX.L.66.0F3A.W1 {op} /r", "V, vV, VM/b8, ib", X86IsaFlags.Gfni | F, X86Tuple.Full, 8);
        }
        Def(X86InstrKind.Insertps, "66 0F3A 21 /r", "x, xm32, ib", Sse41);
        Def(X86InstrKind.Insertps, "VEX.128.66.0F3A.WIG 21 /r", "x, vx, xm32, ib", Avx);

        foreach (var (kind, legacy, vex, ymm) in new[]
        {
            (X86InstrKind.Blendvps, "66 0F38 14", "4A", Avx), (X86InstrKind.Blendvpd, "66 0F38 15", "4B", Avx), (X86InstrKind.Pblendvb, "66 0F38 10", "4C", Avx2),
        })
        {
            Def(kind, legacy + " /r", "x, xm, xmm0", Sse41);
            Def(kind, $"VEX.128.66.0F3A.W0 {vex} /r", "x, vx, xm, is4x", Avx, X86FormFlags.LegacyXmm0);
            Def(kind, $"VEX.256.66.0F3A.W0 {vex} /r", "y, vy, ym, is4y", ymm);
        }

        Unary(X86InstrKind.Sqrtps, "NP 0F 51", Sse, Avx, evex: new Evex("W0", F, 4, Flags: Rounded));
        Unary(X86InstrKind.Rcpps, "NP 0F 53", Sse, Avx);
        Unary(X86InstrKind.Rsqrtps, "NP 0F 52", Sse, Avx);
        Unary(X86InstrKind.Cvtdq2ps, "NP 0F 5B", Sse2, Avx, evex: new Evex("W0", F, 4, Flags: Rounded));
        Unary(X86InstrKind.Cvttps2dq, "F3 0F 5B", Sse2, Avx, evex: new Evex("W0", F, 4));
        Unary(X86InstrKind.Sqrtpd, "66 0F 51", Sse2, Avx, evex: new Evex("W1", F, 8, Flags: Rounded));
        Unary(X86InstrKind.Cvtps2dq, "66 0F 5B", Sse2, Avx, evex: new Evex("W0", F, 4, Flags: Rounded));
        Unary(X86InstrKind.Cvtps2pd, "NP 0F 5A", Sse2, Avx, "xm64", "y, xm128", evex: new Evex("W0", F, 4, Operands: "z, ym/b4", Tuple: X86Tuple.Half));
        Unary(X86InstrKind.Cvtdq2pd, "F3 0F E6", Sse2, Avx, "xm64", "y, xm128", evex: new Evex("W0", F, 4, Operands: "z, ym/b4", Tuple: X86Tuple.Half));
        Unary(X86InstrKind.Cvtpd2ps, "66 0F 5A", Sse2, Avx, "xm", "x, ym", evex: new Evex("W1", F, 8, Flags: Rounded, Operands: "y, zm/b8"));
        Unary(X86InstrKind.Cvttpd2dq, "66 0F E6", Sse2, Avx, "xm", "x, ym", evex: new Evex("W1", F, 8, Operands: "y, zm/b8"));
        Unary(X86InstrKind.Cvtpd2dq, "F2 0F E6", Sse2, Avx, "xm", "x, ym", evex: new Evex("W1", F, 8, Flags: Rounded, Operands: "y, zm/b8"));
        Unary(X86InstrKind.Movddup, "F2 0F 12", Sse3, Avx, "xm64", evex: new Evex("W1", F, Tuple: X86Tuple.MoveDup));
        Unary(X86InstrKind.Movshdup, "F3 0F 16", Sse3, Avx, evex: new Evex("W0", F));
        Unary(X86InstrKind.Movsldup, "F3 0F 12", Sse3, Avx, evex: new Evex("W0", F));
        Unary(X86InstrKind.Pabsb, "66 0F38 1C", Ssse3, Avx2, evex: Bytes);
        Unary(X86InstrKind.Pabsw, "66 0F38 1D", Ssse3, Avx2, evex: Bytes);
        Unary(X86InstrKind.Pabsd, "66 0F38 1E", Ssse3, Avx2, evex: new Evex("W0", F, 4));
        foreach (var (kind, op, bits, w, isa) in new[]
        {
            (X86InstrKind.Pmovsxbw, "20", 64, "WIG", BW), (X86InstrKind.Pmovsxbd, "21", 32, "WIG", F), (X86InstrKind.Pmovsxbq, "22", 16, "WIG", F),
            (X86InstrKind.Pmovsxwd, "23", 64, "WIG", F), (X86InstrKind.Pmovsxwq, "24", 32, "WIG", F), (X86InstrKind.Pmovsxdq, "25", 64, "W0", F),
            (X86InstrKind.Pmovzxbw, "30", 64, "WIG", BW), (X86InstrKind.Pmovzxbd, "31", 32, "WIG", F), (X86InstrKind.Pmovzxbq, "32", 16, "WIG", F),
            (X86InstrKind.Pmovzxwd, "33", 64, "WIG", F), (X86InstrKind.Pmovzxwq, "34", 32, "WIG", F), (X86InstrKind.Pmovzxdq, "35", 64, "W0", F),
        })
        {
            string source = bits switch { 64 => "ym", 32 => "xm", _ => "xm64" };
            var tuple = bits switch { 64 => X86Tuple.HalfMemory, 32 => X86Tuple.QuarterMemory, _ => X86Tuple.EighthMemory };
            Unary(kind, "66 0F38 " + op, Sse41, Avx2, $"xm{bits}", $"y, xm{bits * 2}", evex: new Evex(w, isa, Operands: "z, " + source, Tuple: tuple));
        }
        Unary(X86InstrKind.Phminposuw, "66 0F38 41", Sse41, NoYmm);
        Unary(X86InstrKind.Ptest, "66 0F38 17", Sse41, Avx);
        Unary(X86InstrKind.Roundps, "66 0F3A 08", Sse41, Avx, "xm", null, ", ib");
        Unary(X86InstrKind.Roundpd, "66 0F3A 09", Sse41, Avx, "xm", null, ", ib");
        Unary(X86InstrKind.Pshufd, "66 0F 70", Sse2, Avx2, "xm", null, ", ib", new Evex("W0", F, 4));
        Unary(X86InstrKind.Pshuflw, "F2 0F 70", Sse2, Avx2, "xm", null, ", ib", Bytes);
        Unary(X86InstrKind.Pshufhw, "F3 0F 70", Sse2, Avx2, "xm", null, ", ib", Bytes);

        foreach (var (kind, op, isa, ymm) in new[] { (X86InstrKind.Movmskps, "NP 0F 50", Sse, Avx), (X86InstrKind.Movmskpd, "66 0F 50", Sse2, Avx), (X86InstrKind.Pmovmskb, "66 0F D7", Sse2, Avx2) })
        {
            Def(kind, op + " /r", "r32, xr", isa);
            Def(kind, Vex("128", op) + " /r", "r32, xr", Avx);
            Def(kind, Vex("256", op) + " /r", "r32, yr", ymm);
        }
        Def(X86InstrKind.Pextrw, "66 0F C5 /r", "r32, xr, ib", Sse2);
        Def(X86InstrKind.Pextrw, "VEX.128.66.0F.W0 C5 /r", "r32, xr, ib", Avx);
        Def(X86InstrKind.Pinsrw, "66 0F C4 /r", "x, rm32/m16, ib", Sse2);
        Def(X86InstrKind.Pinsrw, "VEX.128.66.0F.W0 C4 /r", "x, vx, rm32/m16, ib", Avx);
        foreach (var (kind, op, operands, flags) in new[]
        {
            (X86InstrKind.Pextrb, "66 0F3A 14", "rm32/m8, x, ib", X86FormFlags.None), (X86InstrKind.Pextrd, "66 0F3A 16", "rm32, x, ib", X86FormFlags.None),
            (X86InstrKind.Pextrq, "66 REX.W 0F3A 16", "rm64, x, ib", X86FormFlags.Mode64), (X86InstrKind.Extractps, "66 0F3A 17", "rm32, x, ib", X86FormFlags.None),
            (X86InstrKind.Pinsrb, "66 0F3A 20", "x, rm32/m8, ib", X86FormFlags.None), (X86InstrKind.Pinsrd, "66 0F3A 22", "x, rm32, ib", X86FormFlags.None),
            (X86InstrKind.Pinsrq, "66 REX.W 0F3A 22", "x, rm64, ib", X86FormFlags.Mode64),
        })
        {
            Def(kind, op + " /r", operands, Sse41, flags);
            bool insert = operands[0] == 'x';
            string vexOperands = insert ? "x, vx" + operands[1..] : operands;
            string vex = $"VEX.128.66.0F3A.{(kind is X86InstrKind.Pextrq or X86InstrKind.Pinsrq ? "W1" : "W0")} {op[^2..]}";
            Def(kind, vex + " /r", vexOperands, Avx, flags);
        }

        foreach (var (kind, count, immediate, n, evex) in new[]
        {
            (X86InstrKind.Psllw, "F1", "71", 6, Bytes), (X86InstrKind.Pslld, "F2", "72", 6, new Evex("W0", F, 4)), (X86InstrKind.Psllq, "F3", "73", 6, new Evex("W1", F, 8)),
            (X86InstrKind.Psrlw, "D1", "71", 2, Bytes), (X86InstrKind.Psrld, "D2", "72", 2, new Evex("W0", F, 4)), (X86InstrKind.Psrlq, "D3", "73", 2, new Evex("W1", F, 8)),
            (X86InstrKind.Psraw, "E1", "71", 4, Bytes), (X86InstrKind.Psrad, "E2", "72", 4, new Evex("W0", F, 4)),
        })
        {
            Def(kind, $"66 0F {count} /r", "x, xm", Sse2);
            Def(kind, $"66 0F {immediate} /{n}", "xr, ib", Sse2);
            Def(kind, $"VEX.128.66.0F.WIG {count} /r", "x, vx, xm", Avx);
            Def(kind, $"VEX.128.66.0F.WIG {immediate} /{n}", "vx, xr, ib", Avx);
            Def(kind, $"VEX.256.66.0F.WIG {count} /r", "y, vy, xm", Avx2);
            Def(kind, $"VEX.256.66.0F.WIG {immediate} /{n}", "vy, yr, ib", Avx2);
            Def(kind, EvexOpcode("512", $"66 0F {count} /r", evex.W), "z, vz, xm", evex.Isa, X86FormFlags.Masking, X86Tuple.Mem128);
            Def(kind, EvexOpcode("512", $"66 0F {immediate} /{n}", evex.W), "vz, " + Wide(evex) + ", ib", evex.Isa, X86FormFlags.Masking, TupleOf(evex), evex.Element);
        }
        foreach (var (kind, n) in new[] { (X86InstrKind.Pslldq, 7), (X86InstrKind.Psrldq, 3) })
        {
            Def(kind, $"66 0F 73 /{n}", "xr, ib", Sse2);
            Def(kind, $"VEX.128.66.0F.WIG 73 /{n}", "vx, xr, ib", Avx);
            Def(kind, $"VEX.256.66.0F.WIG 73 /{n}", "vy, yr, ib", Avx2);
            Def(kind, $"EVEX.512.66.0F.WIG 73 /{n}", "vz, zm, ib", BW, X86FormFlags.None, X86Tuple.FullMemory);
        }
    }

    // Instructions that exist only in VEX or EVEX form
    private static void BuildAvx()
    {
        const X86FormFlags Masked = X86FormFlags.Masking, Plain = X86FormFlags.None;
        const X86Tuple Full = X86Tuple.Full, FullMemory = X86Tuple.FullMemory, Scalar = X86Tuple.Scalar;

        Def(X86InstrKind.Vbroadcastss, "VEX.128.66.0F38.W0 18 /r", "x, m32", Avx);
        Def(X86InstrKind.Vbroadcastss, "VEX.256.66.0F38.W0 18 /r", "y, m32", Avx);
        Def(X86InstrKind.Vbroadcastss, "VEX.128.66.0F38.W0 18 /r", "x, xr", Avx2);
        Def(X86InstrKind.Vbroadcastss, "VEX.256.66.0F38.W0 18 /r", "y, xr", Avx2);
        Vl(X86InstrKind.Vbroadcastss, "EVEX.L.66.0F38.W0 18 /r", "V, xm32", F, Scalar, 4);
        Def(X86InstrKind.Vbroadcastsd, "VEX.256.66.0F38.W0 19 /r", "y, m64", Avx);
        Def(X86InstrKind.Vbroadcastsd, "VEX.256.66.0F38.W0 19 /r", "y, xr", Avx2);
        Vl(X86InstrKind.Vbroadcastsd, "EVEX.L.66.0F38.W1 19 /r", "V, xm64", F, Scalar, 8, minimum: 256);
        foreach (var (kind, op, gpr, bits, w, isa) in new[]
        {
            (X86InstrKind.Vpbroadcastb, "78", "7A", 8, "W0", BW), (X86InstrKind.Vpbroadcastw, "79", "7B", 16, "W0", BW),
            (X86InstrKind.Vpbroadcastd, "58", "7C", 32, "W0", F), (X86InstrKind.Vpbroadcastq, "59", "7C", 64, "W1", F),
        })
        {
            Def(kind, $"VEX.128.66.0F38.W0 {op} /r", $"x, xm{bits}", Avx2);
            Def(kind, $"VEX.256.66.0F38.W0 {op} /r", $"y, xm{bits}", Avx2);
            Vl(kind, $"EVEX.L.66.0F38.{w} {op} /r", $"V, xm{bits}", isa, Scalar, bits / 8);
            Vl(kind, $"EVEX.L.66.0F38.{w} {gpr} /r", bits == 64 ? "V, rr64" : "V, rr32", isa, X86Tuple.None, flags: bits == 64 ? Masked | X86FormFlags.Mode64 : Masked);
        }
        Def(X86InstrKind.Vbroadcastf128, "VEX.256.66.0F38.W0 1A /r", "y, m128", Avx);
        Def(X86InstrKind.Vbroadcasti128, "VEX.256.66.0F38.W0 5A /r", "y, m128", Avx2);
        foreach (var (kind, op, w, isa, tuple, element, minimum, operands) in new[]
        {
            (X86InstrKind.Vbroadcastf32x2, "19", "W0", DQ, X86Tuple.Tuple2, 4, 256, "V, xm64"),
            (X86InstrKind.Vbroadcasti32x2, "59", "W0", DQ, X86Tuple.Tuple2, 4, 128, "V, xm64"),
            (X86InstrKind.Vbroadcastf32x4, "1A", "W0", F, X86Tuple.Tuple4, 4, 256, "V, m128"),
            (X86InstrKind.Vbroadcasti32x4, "5A", "W0", F, X86Tuple.Tuple4, 4, 256, "V, m128"),
            (X86InstrKind.Vbroadcastf64x2, "1A", "W1", DQ, X86Tuple.Tuple2, 8, 256, "V, m128"),
            (X86InstrKind.Vbroadcasti64x2, "5A", "W1", DQ, X86Tuple.Tuple2, 8, 256, "V, m128"),
            (X86InstrKind.Vbroadcastf32x8, "1B", "W0", DQ, X86Tuple.Tuple8, 4, 512, "V, m256"),
            (X86InstrKind.Vbroadcasti32x8, "5B", "W0", DQ, X86Tuple.Tuple8, 4, 512, "V, m256"),
            (X86InstrKind.Vbroadcastf64x4, "1B", "W1", F, X86Tuple.Tuple4, 8, 512, "V, m256"),
            (X86InstrKind.Vbroadcasti64x4, "5B", "W1", F, X86Tuple.Tuple4, 8, 512, "V, m256"),
        })
        {
            Vl(kind, $"EVEX.L.66.0F38.{w} {op} /r", operands, isa, tuple, element, minimum: minimum);
        }

        Def(X86InstrKind.Vinsertf128, "VEX.256.66.0F3A.W0 18 /r", "y, vy, xm, ib", Avx);
        Def(X86InstrKind.Vinserti128, "VEX.256.66.0F3A.W0 38 /r", "y, vy, xm, ib", Avx2);
        Def(X86InstrKind.Vextractf128, "VEX.256.66.0F3A.W0 19 /r", "xm, y, ib", Avx);
        Def(X86InstrKind.Vextracti128, "VEX.256.66.0F3A.W0 39 /r", "xm, y, ib", Avx2);
        foreach (var (insert, extract, op, w, isa, tuple, element, minimum, part) in new[]
        {
            (X86InstrKind.Vinsertf32x4, X86InstrKind.Vextractf32x4, 0x18, "W0", F, X86Tuple.Tuple4, 4, 256, "x"),
            (X86InstrKind.Vinserti32x4, X86InstrKind.Vextracti32x4, 0x38, "W0", F, X86Tuple.Tuple4, 4, 256, "x"),
            (X86InstrKind.Vinsertf64x2, X86InstrKind.Vextractf64x2, 0x18, "W1", DQ, X86Tuple.Tuple2, 8, 256, "x"),
            (X86InstrKind.Vinserti64x2, X86InstrKind.Vextracti64x2, 0x38, "W1", DQ, X86Tuple.Tuple2, 8, 256, "x"),
            (X86InstrKind.Vinsertf32x8, X86InstrKind.Vextractf32x8, 0x1A, "W0", DQ, X86Tuple.Tuple8, 4, 512, "y"),
            (X86InstrKind.Vinserti32x8, X86InstrKind.Vextracti32x8, 0x3A, "W0", DQ, X86Tuple.Tuple8, 4, 512, "y"),
            (X86InstrKind.Vinsertf64x4, X86InstrKind.Vextractf64x4, 0x1A, "W1", F, X86Tuple.Tuple4, 8, 512, "y"),
            (X86InstrKind.Vinserti64x4, X86InstrKind.Vextracti64x4, 0x3A, "W1", F, X86Tuple.Tuple4, 8, 512, "y"),
        })
        {
            Vl(insert, $"EVEX.L.66.0F3A.{w} {op:X2} /r", $"V, vV, {part}m, ib", isa, tuple, element, minimum: minimum);
            Vl(extract, $"EVEX.L.66.0F3A.{w} {op + 1:X2} /r", $"{part}m, V, ib", isa, tuple, element, minimum: minimum);
        }

        Def(X86InstrKind.Vperm2f128, "VEX.256.66.0F3A.W0 06 /r", "y, vy, ym, ib", Avx);
        Def(X86InstrKind.Vperm2i128, "VEX.256.66.0F3A.W0 46 /r", "y, vy, ym, ib", Avx2);
        foreach (var (kind, variable, immediate, w, element) in new[] { (X86InstrKind.Vpermilps, "0C", "04", "W0", 4), (X86InstrKind.Vpermilpd, "0D", "05", "W1", 8) })
        {
            Def(kind, $"VEX.128.66.0F38.W0 {variable} /r", "x, vx, xm", Avx);
            Def(kind, $"VEX.256.66.0F38.W0 {variable} /r", "y, vy, ym", Avx);
            Def(kind, $"VEX.128.66.0F3A.W0 {immediate} /r", "x, xm, ib", Avx);
            Def(kind, $"VEX.256.66.0F3A.W0 {immediate} /r", "y, ym, ib", Avx);
            Vl(kind, $"EVEX.L.66.0F38.{w} {variable} /r", $"V, vV, VM/b{element}", F, Full, element);
            Vl(kind, $"EVEX.L.66.0F3A.{w} {immediate} /r", $"V, VM/b{element}, ib", F, Full, element);
        }
        foreach (var (kind, variable, immediate, w, element) in new[] { (X86InstrKind.Vpermq, "36", "00", "W1", 8), (X86InstrKind.Vpermpd, "16", "01", "W1", 8) })
        {
            Def(kind, $"VEX.256.66.0F3A.W1 {immediate} /r", "y, ym, ib", Avx2);
            Vl(kind, $"EVEX.L.66.0F3A.{w} {immediate} /r", $"V, VM/b{element}, ib", F, Full, element, minimum: 256);
            Vl(kind, $"EVEX.L.66.0F38.{w} {variable} /r", $"V, vV, VM/b{element}", F, Full, element, minimum: 256);
        }
        foreach (var (kind, op) in new[] { (X86InstrKind.Vpermd, "36"), (X86InstrKind.Vpermps, "16") })
        {
            Def(kind, $"VEX.256.66.0F38.W0 {op} /r", "y, vy, ym", Avx2);
            Vl(kind, $"EVEX.L.66.0F38.W0 {op} /r", "V, vV, VM/b4", F, Full, 4, minimum: 256);
        }
        Vl(X86InstrKind.Vpermb, "EVEX.L.66.0F38.W0 8D /r", "V, vV, VM", Vbmi, FullMemory);
        Vl(X86InstrKind.Vpermw, "EVEX.L.66.0F38.W1 8D /r", "V, vV, VM", BW, FullMemory);
        foreach (var (overwriteTable, overwriteIndex, op, w, isa, element) in new[]
        {
            (X86InstrKind.Vpermt2b, X86InstrKind.Vpermi2b, 0x7D, "W0", Vbmi, 0), (X86InstrKind.Vpermt2w, X86InstrKind.Vpermi2w, 0x7D, "W1", BW, 0),
            (X86InstrKind.Vpermt2d, X86InstrKind.Vpermi2d, 0x7E, "W0", F, 4), (X86InstrKind.Vpermt2q, X86InstrKind.Vpermi2q, 0x7E, "W1", F, 8),
            (X86InstrKind.Vpermt2ps, X86InstrKind.Vpermi2ps, 0x7F, "W0", F, 4), (X86InstrKind.Vpermt2pd, X86InstrKind.Vpermi2pd, 0x7F, "W1", F, 8),
        })
        {
            string source = element == 0 ? "V, vV, VM" : $"V, vV, VM/b{element}";
            Vl(overwriteTable, $"EVEX.L.66.0F38.{w} {op:X2} /r", source, isa, element == 0 ? FullMemory : Full, element);
            Vl(overwriteIndex, $"EVEX.L.66.0F38.{w} {op - 8:X2} /r", source, isa, element == 0 ? FullMemory : Full, element);
        }
        Vl(X86InstrKind.Vshuff32x4, "EVEX.L.66.0F3A.W0 23 /r", "V, vV, VM/b4, ib", F, Full, 4, minimum: 256);
        Vl(X86InstrKind.Vshuff64x2, "EVEX.L.66.0F3A.W1 23 /r", "V, vV, VM/b8, ib", F, Full, 8, minimum: 256);
        Vl(X86InstrKind.Vshufi32x4, "EVEX.L.66.0F3A.W0 43 /r", "V, vV, VM/b4, ib", F, Full, 4, minimum: 256);
        Vl(X86InstrKind.Vshufi64x2, "EVEX.L.66.0F3A.W1 43 /r", "V, vV, VM/b8, ib", F, Full, 8, minimum: 256);
        Vl(X86InstrKind.Valignd, "EVEX.L.66.0F3A.W0 03 /r", "V, vV, VM/b4, ib", F, Full, 4);
        Vl(X86InstrKind.Valignq, "EVEX.L.66.0F3A.W1 03 /r", "V, vV, VM/b8, ib", F, Full, 8);
        Vl(X86InstrKind.Vdbpsadbw, "EVEX.L.66.0F3A.W0 42 /r", "V, vV, VM, ib", BW, FullMemory);
        Vl(X86InstrKind.Vpmultishiftqb, "EVEX.L.66.0F38.W1 83 /r", "V, vV, VM/b8", Vbmi, Full, 8);

        Def(X86InstrKind.Vpblendd, "VEX.128.66.0F3A.W0 02 /r", "x, vx, xm, ib", Avx2);
        Def(X86InstrKind.Vpblendd, "VEX.256.66.0F3A.W0 02 /r", "y, vy, ym, ib", Avx2);
        foreach (var (kind, op, w, isa, element) in new[]
        {
            (X86InstrKind.Vpblendmb, "66", "W0", BW, 0), (X86InstrKind.Vpblendmw, "66", "W1", BW, 0),
            (X86InstrKind.Vpblendmd, "64", "W0", F, 4), (X86InstrKind.Vpblendmq, "64", "W1", F, 8),
            (X86InstrKind.Vblendmps, "65", "W0", F, 4), (X86InstrKind.Vblendmpd, "65", "W1", F, 8),
        })
        {
            Vl(kind, $"EVEX.L.66.0F38.{w} {op} /r", element == 0 ? "V, vV, VM" : $"V, vV, VM/b{element}", isa, element == 0 ? FullMemory : Full, element);
        }
        foreach (var (kind, load, store, w, isa) in new[]
        {
            (X86InstrKind.Vmaskmovps, "2C", "2E", "W0", Avx), (X86InstrKind.Vmaskmovpd, "2D", "2F", "W0", Avx),
            (X86InstrKind.Vpmaskmovd, "8C", "8E", "W0", Avx2), (X86InstrKind.Vpmaskmovq, "8C", "8E", "W1", Avx2),
        })
        {
            Def(kind, $"VEX.128.66.0F38.{w} {load} /r", "x, vx, m128", isa);
            Def(kind, $"VEX.256.66.0F38.{w} {load} /r", "y, vy, m256", isa);
            Def(kind, $"VEX.128.66.0F38.{w} {store} /r", "m128, vx, x", isa);
            Def(kind, $"VEX.256.66.0F38.{w} {store} /r", "m256, vy, y", isa);
        }
        foreach (var (kind, pp, w, isa) in new[]
        {
            (X86InstrKind.Vmovdqa32, "66", "W0", F), (X86InstrKind.Vmovdqa64, "66", "W1", F), (X86InstrKind.Vmovdqu8, "F2", "W0", BW),
            (X86InstrKind.Vmovdqu16, "F2", "W1", BW), (X86InstrKind.Vmovdqu32, "F3", "W0", F), (X86InstrKind.Vmovdqu64, "F3", "W1", F),
        })
        {
            Vl(kind, $"EVEX.L.{pp}.0F.{w} 6F /r", "V, VM", isa, FullMemory);
            Vl(kind, $"EVEX.L.{pp}.0F.{w} 7F /r", "VM, V", isa, FullMemory);
        }
        foreach (var (compress, expand, compressOp, expandOp, w, isa, element) in new[]
        {
            (X86InstrKind.Vcompressps, X86InstrKind.Vexpandps, "8A", "88", "W0", F, 4), (X86InstrKind.Vcompresspd, X86InstrKind.Vexpandpd, "8A", "88", "W1", F, 8),
            (X86InstrKind.Vpcompressd, X86InstrKind.Vpexpandd, "8B", "89", "W0", F, 4), (X86InstrKind.Vpcompressq, X86InstrKind.Vpexpandq, "8B", "89", "W1", F, 8),
            (X86InstrKind.Vpcompressb, X86InstrKind.Vpexpandb, "63", "62", "W0", Vbmi2, 1), (X86InstrKind.Vpcompressw, X86InstrKind.Vpexpandw, "63", "62", "W1", Vbmi2, 2),
        })
        {
            Vl(compress, $"EVEX.L.66.0F38.{w} {compressOp} /r", "VM, V", isa, Scalar, element);
            Vl(expand, $"EVEX.L.66.0F38.{w} {expandOp} /r", "V, VM", isa, Scalar, element);
        }
        foreach (var (kind, op, w, index128, index256) in new[]
        {
            (X86InstrKind.Vpgatherdd, "90", "W0", "x, vm32x, vx", "y, vm32y, vy"), (X86InstrKind.Vpgatherdq, "90", "W1", "x, vm32x, vx", "y, vm32x, vy"),
            (X86InstrKind.Vpgatherqd, "91", "W0", "x, vm64x, vx", "x, vm64y, vx"), (X86InstrKind.Vpgatherqq, "91", "W1", "x, vm64x, vx", "y, vm64y, vy"),
            (X86InstrKind.Vgatherdps, "92", "W0", "x, vm32x, vx", "y, vm32y, vy"), (X86InstrKind.Vgatherdpd, "92", "W1", "x, vm32x, vx", "y, vm32x, vy"),
            (X86InstrKind.Vgatherqps, "93", "W0", "x, vm64x, vx", "x, vm64y, vx"), (X86InstrKind.Vgatherqpd, "93", "W1", "x, vm64x, vx", "y, vm64y, vy"),
        })
        {
            Def(kind, $"VEX.128.66.0F38.{w} {op} /r", index128, Avx2);
            Def(kind, $"VEX.256.66.0F38.{w} {op} /r", index256, Avx2);
        }

        foreach (var (kind, op, w, vex) in new[]
        {
            (X86InstrKind.Vpsllvd, "47", "W0", true), (X86InstrKind.Vpsllvq, "47", "W1", true),
            (X86InstrKind.Vpsrlvd, "45", "W0", true), (X86InstrKind.Vpsrlvq, "45", "W1", true),
            (X86InstrKind.Vpsravd, "46", "W0", true), (X86InstrKind.Vpsravq, "46", "W1", false),
        })
        {
            if (vex)
            {
                Def(kind, $"VEX.128.66.0F38.{w} {op} /r", "x, vx, xm", Avx2);
                Def(kind, $"VEX.256.66.0F38.{w} {op} /r", "y, vy, ym", Avx2);
            }
            int element = w == "W1" ? 8 : 4;
            Vl(kind, $"EVEX.L.66.0F38.{w} {op} /r", $"V, vV, VM/b{element}", F, Full, element);
        }
        Vl(X86InstrKind.Vpsllvw, "EVEX.L.66.0F38.W1 12 /r", "V, vV, VM", BW, FullMemory);
        Vl(X86InstrKind.Vpsrlvw, "EVEX.L.66.0F38.W1 10 /r", "V, vV, VM", BW, FullMemory);
        Vl(X86InstrKind.Vpsravw, "EVEX.L.66.0F38.W1 11 /r", "V, vV, VM", BW, FullMemory);
        foreach (var (kind, w, extension, element) in new[]
        {
            (X86InstrKind.Vprold, "W0", 1, 4), (X86InstrKind.Vprolq, "W1", 1, 8), (X86InstrKind.Vprord, "W0", 0, 4), (X86InstrKind.Vprorq, "W1", 0, 8),
        })
        {
            Vl(kind, $"EVEX.L.66.0F.{w} 72 /{extension}", $"vV, VM/b{element}, ib", F, Full, element);
        }
        foreach (var (kind, op, w, element) in new[]
        {
            (X86InstrKind.Vprolvd, "15", "W0", 4), (X86InstrKind.Vprolvq, "15", "W1", 8), (X86InstrKind.Vprorvd, "14", "W0", 4), (X86InstrKind.Vprorvq, "14", "W1", 8),
        })
        {
            Vl(kind, $"EVEX.L.66.0F38.{w} {op} /r", $"V, vV, VM/b{element}", F, Full, element);
        }

        Vl(X86InstrKind.Vpternlogd, "EVEX.L.66.0F3A.W0 25 /r", "V, vV, VM/b4, ib", F, Full, 4);
        Vl(X86InstrKind.Vpternlogq, "EVEX.L.66.0F3A.W1 25 /r", "V, vV, VM/b8, ib", F, Full, 8);
        foreach (var (kind, op, w, isa, element) in new[]
        {
            (X86InstrKind.Vpcmpb, "3F", "W0", BW, 1), (X86InstrKind.Vpcmpub, "3E", "W0", BW, 1),
            (X86InstrKind.Vpcmpw, "3F", "W1", BW, 2), (X86InstrKind.Vpcmpuw, "3E", "W1", BW, 2),
            (X86InstrKind.Vpcmpd, "1F", "W0", F, 4), (X86InstrKind.Vpcmpud, "1E", "W0", F, 4),
            (X86InstrKind.Vpcmpq, "1F", "W1", F, 8), (X86InstrKind.Vpcmpuq, "1E", "W1", F, 8),
        })
        {
            bool full = element >= 4;
            Vl(kind, $"EVEX.L.66.0F3A.{w} {op} /r", full ? $"k, vV, VM/b{element}, ib" : "k, vV, VM, ib", isa, full ? Full : FullMemory, element);
        }
        foreach (var (kind, pp, w, element) in new[] { (X86InstrKind.Cmpps, "NP", "W0", 4), (X86InstrKind.Cmppd, "66", "W1", 8) })
        {
            foreach (int bits in new[] { 128, 256 })
                Def(kind, $"EVEX.{bits}.{pp}.0F.{w} C2 /r", Resize($"k, vV, VM/b{element}, ib", bits), F, X86FormFlags.Masking, Full, element);
        }
        foreach (var (test, testNot, op, w, isa, element) in new[]
        {
            (X86InstrKind.Vptestmb, X86InstrKind.Vptestnmb, "26", "W0", BW, 0), (X86InstrKind.Vptestmw, X86InstrKind.Vptestnmw, "26", "W1", BW, 0),
            (X86InstrKind.Vptestmd, X86InstrKind.Vptestnmd, "27", "W0", F, 4), (X86InstrKind.Vptestmq, X86InstrKind.Vptestnmq, "27", "W1", F, 8),
        })
        {
            string operands = element == 0 ? "k, vV, VM" : $"k, vV, VM/b{element}";
            Vl(test, $"EVEX.L.66.0F38.{w} {op} /r", operands, isa, element == 0 ? FullMemory : Full, element);
            Vl(testNot, $"EVEX.L.F3.0F38.{w} {op} /r", operands, isa, element == 0 ? FullMemory : Full, element);
        }
        foreach (var (toVector, toMask, op, w, isa) in new[]
        {
            (X86InstrKind.Vpmovm2b, X86InstrKind.Vpmovb2m, "28", "W0", BW), (X86InstrKind.Vpmovm2w, X86InstrKind.Vpmovw2m, "28", "W1", BW),
            (X86InstrKind.Vpmovm2d, X86InstrKind.Vpmovd2m, "38", "W0", DQ), (X86InstrKind.Vpmovm2q, X86InstrKind.Vpmovq2m, "38", "W1", DQ),
        })
        {
            Vl(toVector, $"EVEX.L.F3.0F38.{w} {op} /r", "V, kr", isa, X86Tuple.None, flags: Plain);
            Vl(toMask, $"EVEX.L.F3.0F38.{w} {(op == "28" ? "29" : "39")} /r", "k, VR", isa, X86Tuple.None, flags: Plain);
        }
        foreach (var (kind, pp, w, isa) in new[] { (X86InstrKind.Kmovb, "66", "W0", DQ), (X86InstrKind.Kmovw, "NP", "W0", F), (X86InstrKind.Kmovd, "F2", "W0", BW), (X86InstrKind.Kmovq, "F2", "W1", BW) })
        {
            string gpr = kind == X86InstrKind.Kmovq ? "64" : "32";
            var flags = kind == X86InstrKind.Kmovq ? X86FormFlags.Mode64 : X86FormFlags.None;
            Def(kind, $"VEX.L0.{pp}.0F.{w} 92 /r", "k, rr" + gpr, isa, flags);
            Def(kind, $"VEX.L0.{pp}.0F.{w} 93 /r", $"r{gpr}, kr", isa, flags);
        }
        Def(X86InstrKind.Kmovw, "VEX.L0.NP.0F.W0 90 /r", "k, kr", F);
        Def(X86InstrKind.Kortestb, "VEX.L0.66.0F.W0 98 /r", "k, kr", DQ);
        Def(X86InstrKind.Kortestw, "VEX.L0.0F.W0 98 /r", "k, kr", F);
        Def(X86InstrKind.Kortestd, "VEX.L0.66.0F.W1 98 /r", "k, kr", BW);
        Def(X86InstrKind.Kortestq, "VEX.L0.0F.W1 98 /r", "k, kr", BW);
        foreach (var (plain, signed, unsigned, op, isa, operands, tuple) in new[]
        {
            (X86InstrKind.Vpmovwb, X86InstrKind.Vpmovswb, X86InstrKind.Vpmovuswb, 0x30, BW, "HM, V", X86Tuple.HalfMemory),
            (X86InstrKind.Vpmovdb, X86InstrKind.Vpmovsdb, X86InstrKind.Vpmovusdb, 0x31, F, "QM, V", X86Tuple.QuarterMemory),
            (X86InstrKind.Vpmovqb, X86InstrKind.Vpmovsqb, X86InstrKind.Vpmovusqb, 0x32, F, "EM, V", X86Tuple.EighthMemory),
            (X86InstrKind.Vpmovdw, X86InstrKind.Vpmovsdw, X86InstrKind.Vpmovusdw, 0x33, F, "HM, V", X86Tuple.HalfMemory),
            (X86InstrKind.Vpmovqw, X86InstrKind.Vpmovsqw, X86InstrKind.Vpmovusqw, 0x34, F, "QM, V", X86Tuple.QuarterMemory),
            (X86InstrKind.Vpmovqd, X86InstrKind.Vpmovsqd, X86InstrKind.Vpmovusqd, 0x35, F, "HM, V", X86Tuple.HalfMemory),
        })
        {
            Vl(plain, $"EVEX.L.F3.0F38.W0 {op:X2} /r", operands, isa, tuple);
            Vl(signed, $"EVEX.L.F3.0F38.W0 {op - 0x10:X2} /r", operands, isa, tuple);
            Vl(unsigned, $"EVEX.L.F3.0F38.W0 {op - 0x20:X2} /r", operands, isa, tuple);
        }

        Vl(X86InstrKind.Vpabsq, "EVEX.L.66.0F38.W1 1F /r", "V, VM/b8", F, Full, 8);
        foreach (var (kind, op) in new[] { (X86InstrKind.Vpmaxsq, "3D"), (X86InstrKind.Vpminsq, "39"), (X86InstrKind.Vpmaxuq, "3F"), (X86InstrKind.Vpminuq, "3B") })
            Vl(kind, $"EVEX.L.66.0F38.W1 {op} /r", "V, vV, VM/b8", F, Full, 8);
        Vl(X86InstrKind.Vpsraq, "EVEX.L.66.0F.W1 E2 /r", "V, vV, xm", F, X86Tuple.Mem128, 8);
        Vl(X86InstrKind.Vpsraq, "EVEX.L.66.0F.W1 72 /4", "vV, VM/b8, ib", F, Full, 8);
        Vl(X86InstrKind.Vpmullq, "EVEX.L.66.0F38.W1 40 /r", "V, vV, VM/b8", DQ, Full, 8);
        foreach (var (kind, op) in new[] { (X86InstrKind.Vpandq, "DB"), (X86InstrKind.Vpandnq, "DF"), (X86InstrKind.Vporq, "EB"), (X86InstrKind.Vpxorq, "EF") })
            Vl(kind, $"EVEX.L.66.0F.W1 {op} /r", "V, vV, VM/b8", F, Full, 8);
        Vl(X86InstrKind.Vpconflictd, "EVEX.L.66.0F38.W0 C4 /r", "V, VM/b4", CD, Full, 4);
        Vl(X86InstrKind.Vpconflictq, "EVEX.L.66.0F38.W1 C4 /r", "V, VM/b8", CD, Full, 8);
        Vl(X86InstrKind.Vplzcntd, "EVEX.L.66.0F38.W0 44 /r", "V, VM/b4", CD, Full, 4);
        Vl(X86InstrKind.Vplzcntq, "EVEX.L.66.0F38.W1 44 /r", "V, VM/b8", CD, Full, 8);

        foreach (var (single, dbl, scalarSingle, scalarDouble, op, scalarOp, flags) in new[]
        {
            (X86InstrKind.Vgetexpps, X86InstrKind.Vgetexppd, X86InstrKind.Vgetexpss, X86InstrKind.Vgetexpsd, "42", "43", Masked),
            (X86InstrKind.Vrcp14ps, X86InstrKind.Vrcp14pd, X86InstrKind.Vrcp14ss, X86InstrKind.Vrcp14sd, "4C", "4D", Masked),
            (X86InstrKind.Vrsqrt14ps, X86InstrKind.Vrsqrt14pd, X86InstrKind.Vrsqrt14ss, X86InstrKind.Vrsqrt14sd, "4E", "4F", Masked),
            (X86InstrKind.Vscalefps, X86InstrKind.Vscalefpd, X86InstrKind.Vscalefss, X86InstrKind.Vscalefsd, "2C", "2D", Rounded),
        })
        {
            string vector = single == X86InstrKind.Vscalefps ? "V, vV, " : "V, ";
            Vl(single, $"EVEX.L.66.0F38.W0 {op} /r", vector + "VM/b4", F, Full, 4, flags: flags);
            Vl(dbl, $"EVEX.L.66.0F38.W1 {op} /r", vector + "VM/b8", F, Full, 8, flags: flags);
            Def(scalarSingle, $"EVEX.LIG.66.0F38.W0 {scalarOp} /r", "x, vx, xm32", F, flags, Scalar, 4);
            Def(scalarDouble, $"EVEX.LIG.66.0F38.W1 {scalarOp} /r", "x, vx, xm64", F, flags, Scalar, 8);
        }
        foreach (var (single, dbl, scalarSingle, scalarDouble, op, scalarOp, isa, binary) in new[]
        {
            (X86InstrKind.Vgetmantps, X86InstrKind.Vgetmantpd, X86InstrKind.Vgetmantss, X86InstrKind.Vgetmantsd, 0x26, 0x27, F, false),
            (X86InstrKind.Vreduceps, X86InstrKind.Vreducepd, X86InstrKind.Vreducess, X86InstrKind.Vreducesd, 0x56, 0x57, DQ, false),
            (X86InstrKind.Vrangeps, X86InstrKind.Vrangepd, X86InstrKind.Vrangess, X86InstrKind.Vrangesd, 0x50, 0x51, DQ, true),
            (X86InstrKind.Vfixupimmps, X86InstrKind.Vfixupimmpd, X86InstrKind.Vfixupimmss, X86InstrKind.Vfixupimmsd, 0x54, 0x55, F, true),
            (X86InstrKind.Vrndscaleps, X86InstrKind.Vrndscalepd, X86InstrKind.Vrndscaless, X86InstrKind.Vrndscalesd, 0x08, 0x0A, F, false),
        })
        {
            // vrndscalepd and vrndscalesd have opcodes of their own
            int doubleStep = single == X86InstrKind.Vrndscaleps ? 1 : 0;
            string vector = binary ? "V, vV, " : "V, ";
            Vl(single, $"EVEX.L.66.0F3A.W0 {op:X2} /r", vector + "VM/b4, ib", isa, Full, 4);
            Vl(dbl, $"EVEX.L.66.0F3A.W1 {op + doubleStep:X2} /r", vector + "VM/b8, ib", isa, Full, 8);
            Def(scalarSingle, $"EVEX.LIG.66.0F3A.W0 {scalarOp:X2} /r", "x, vx, xm32, ib", isa, Masked, Scalar, 4);
            Def(scalarDouble, $"EVEX.LIG.66.0F3A.W1 {scalarOp + doubleStep:X2} /r", "x, vx, xm64, ib", isa, Masked, Scalar, 8);
        }
        Vl(X86InstrKind.Vfpclassps, "EVEX.L.66.0F3A.W0 66 /r", "k, VM/b4, ib", DQ, Full, 4);
        Vl(X86InstrKind.Vfpclasspd, "EVEX.L.66.0F3A.W1 66 /r", "k, VM/b8, ib", DQ, Full, 8);
        Def(X86InstrKind.Vfpclassss, "EVEX.LIG.66.0F3A.W0 67 /r", "k, xm32, ib", DQ, Masked, Scalar, 4);
        Def(X86InstrKind.Vfpclasssd, "EVEX.LIG.66.0F3A.W1 67 /r", "k, xm64, ib", DQ, Masked, Scalar, 8);

        foreach (var (kind, pp, w, op, isa, operands, tuple, element, rounding) in new[]
        {
            (X86InstrKind.Vcvtps2udq, "NP", "W0", "79", F, "V, VM/b4", Full, 4, true),
            (X86InstrKind.Vcvttps2udq, "NP", "W0", "78", F, "V, VM/b4", Full, 4, false),
            (X86InstrKind.Vcvtudq2ps, "F2", "W0", "7A", F, "V, VM/b4", Full, 4, true),
            (X86InstrKind.Vcvtpd2udq, "NP", "W1", "79", F, "H, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvttpd2udq, "NP", "W1", "78", F, "H, VM/b8", Full, 8, false),
            (X86InstrKind.Vcvtudq2pd, "F3", "W0", "7A", F, "V, HM/b4", X86Tuple.Half, 4, false),
            (X86InstrKind.Vcvtpd2qq, "66", "W1", "7B", DQ, "V, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvttpd2qq, "66", "W1", "7A", DQ, "V, VM/b8", Full, 8, false),
            (X86InstrKind.Vcvtpd2uqq, "66", "W1", "79", DQ, "V, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvttpd2uqq, "66", "W1", "78", DQ, "V, VM/b8", Full, 8, false),
            (X86InstrKind.Vcvtps2qq, "66", "W0", "7B", DQ, "V, HM/b4", X86Tuple.Half, 4, true),
            (X86InstrKind.Vcvttps2qq, "66", "W0", "7A", DQ, "V, HM/b4", X86Tuple.Half, 4, false),
            (X86InstrKind.Vcvtps2uqq, "66", "W0", "79", DQ, "V, HM/b4", X86Tuple.Half, 4, true),
            (X86InstrKind.Vcvttps2uqq, "66", "W0", "78", DQ, "V, HM/b4", X86Tuple.Half, 4, false),
            (X86InstrKind.Vcvtqq2pd, "F3", "W1", "E6", DQ, "V, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvtuqq2pd, "F3", "W1", "7A", DQ, "V, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvtqq2ps, "NP", "W1", "5B", DQ, "H, VM/b8", Full, 8, true),
            (X86InstrKind.Vcvtuqq2ps, "F2", "W1", "7A", DQ, "H, VM/b8", Full, 8, true),
        })
        {
            Vl(kind, $"EVEX.L.{pp}.0F.{w} {op} /r", operands, isa, tuple, element, flags: rounding ? Rounded : Masked);
        }
        foreach (var (kind, pp, op, bits, rounding) in new[]
        {
            (X86InstrKind.Vcvtss2usi, "F3", "79", 32, true), (X86InstrKind.Vcvtsd2usi, "F2", "79", 64, true),
            (X86InstrKind.Vcvttss2usi, "F3", "78", 32, false), (X86InstrKind.Vcvttsd2usi, "F2", "78", 64, false),
        })
        {
            var flags = rounding ? X86FormFlags.Rounding : X86FormFlags.None;
            Def(kind, $"EVEX.LIG.{pp}.0F.W0 {op} /r", $"r32, xm{bits}", F, flags, Scalar, bits / 8);
            Def(kind, $"EVEX.LIG.{pp}.0F.W1 {op} /r", $"r64, xm{bits}", F, flags | X86FormFlags.Mode64, Scalar, bits / 8);
        }
        Def(X86InstrKind.Vcvtusi2ss, "EVEX.LIG.F3.0F.W0 7B /r", "x, vx, rm32", F, X86FormFlags.Rounding, Scalar, 4);
        Def(X86InstrKind.Vcvtusi2ss, "EVEX.LIG.F3.0F.W1 7B /r", "x, vx, rm64", F, X86FormFlags.Rounding | X86FormFlags.Mode64, Scalar, 8);
        Def(X86InstrKind.Vcvtusi2sd, "EVEX.LIG.F2.0F.W0 7B /r", "x, vx, rm32", F, X86FormFlags.None, Scalar, 4);
        Def(X86InstrKind.Vcvtusi2sd, "EVEX.LIG.F2.0F.W1 7B /r", "x, vx, rm64", F, X86FormFlags.Rounding | X86FormFlags.Mode64, Scalar, 8);

        foreach (var (kind, op, scalarOp) in new[] { ("fmadd", 0x98, 0x99), ("fmsub", 0x9A, 0x9B), ("fnmadd", 0x9C, 0x9D), ("fnmsub", 0x9E, 0x9F), ("fmaddsub", 0x96, 0), ("fmsubadd", 0x97, 0) })
        {
            for (int order = 0; order < 3; order++)
            {
                string name = "V" + kind + (order switch { 0 => "132", 1 => "213", _ => "231" });
                int step = order * 0x10;
                foreach (var (suffix, w, element) in new[] { ("ps", "W0", 4), ("pd", "W1", 8) })
                {
                    var packed = Enum.Parse<X86InstrKind>(name + suffix);
                    Def(packed, $"VEX.128.66.0F38.{w} {op + step:X2} /r", "x, vx, xm", Fma);
                    Def(packed, $"VEX.256.66.0F38.{w} {op + step:X2} /r", "y, vy, ym", Fma);
                    Vl(packed, $"EVEX.L.66.0F38.{w} {op + step:X2} /r", $"V, vV, VM/b{element}", F, Full, element, flags: Rounded);
                }
                if (scalarOp == 0)
                    continue;
                foreach (var (suffix, w, bits) in new[] { ("ss", "W0", 32), ("sd", "W1", 64) })
                {
                    var scalar = Enum.Parse<X86InstrKind>(name + suffix);
                    Def(scalar, $"VEX.LIG.66.0F38.{w} {scalarOp + step:X2} /r", $"x, vx, xm{bits}", Fma);
                    Def(scalar, $"EVEX.LIG.66.0F38.{w} {scalarOp + step:X2} /r", $"x, vx, xm{bits}", F, Rounded, Scalar, bits / 8);
                }
            }
        }

        Def(X86InstrKind.Vtestps, "VEX.128.66.0F38.W0 0E /r", "x, xm", Avx);
        Def(X86InstrKind.Vtestps, "VEX.256.66.0F38.W0 0E /r", "y, ym", Avx);
        Def(X86InstrKind.Vtestpd, "VEX.128.66.0F38.W0 0F /r", "x, xm", Avx);
        Def(X86InstrKind.Vtestpd, "VEX.256.66.0F38.W0 0F /r", "y, ym", Avx);

        foreach (var (kind, pp, op, isa) in new[]
        {
            (X86InstrKind.Vpdpbusd, "66", "50", AvxVnni), (X86InstrKind.Vpdpbusds, "66", "51", AvxVnni),
            (X86InstrKind.Vpdpwssd, "66", "52", AvxVnni), (X86InstrKind.Vpdpwssds, "66", "53", AvxVnni),
            (X86InstrKind.Vpdpbssd, "F2", "50", AvxVnniInt8), (X86InstrKind.Vpdpbssds, "F2", "51", AvxVnniInt8),
            (X86InstrKind.Vpdpbsud, "F3", "50", AvxVnniInt8), (X86InstrKind.Vpdpbsuds, "F3", "51", AvxVnniInt8),
            (X86InstrKind.Vpdpbuud, "NP", "50", AvxVnniInt8), (X86InstrKind.Vpdpbuuds, "NP", "51", AvxVnniInt8),
            (X86InstrKind.Vpdpwsud, "F3", "D2", AvxVnniInt16), (X86InstrKind.Vpdpwsuds, "F3", "D3", AvxVnniInt16),
            (X86InstrKind.Vpdpwusd, "66", "D2", AvxVnniInt16), (X86InstrKind.Vpdpwusds, "66", "D3", AvxVnniInt16),
            (X86InstrKind.Vpdpwuud, "NP", "D2", AvxVnniInt16), (X86InstrKind.Vpdpwuuds, "NP", "D3", AvxVnniInt16),
        })
        {
            Def(kind, $"VEX.128.{pp}.0F38.W0 {op} /r", "x, vx, xm", isa);
            Def(kind, $"VEX.256.{pp}.0F38.W0 {op} /r", "y, vy, ym", isa);
            if (isa != AvxVnni)
                Def(kind, $"EVEX.512.{pp}.0F38.W0 {op} /r", "z, vz, zm/b4", Avx10v2, Masked, Full, 4);
        }

        Vl(X86InstrKind.Vminmaxps, "EVEX.L.66.0F3A.W0 52 /r", "V, vV, VM/b4, ib", Avx10v2, Full, 4);
        Vl(X86InstrKind.Vminmaxpd, "EVEX.L.66.0F3A.W1 52 /r", "V, vV, VM/b8, ib", Avx10v2, Full, 8);
        Def(X86InstrKind.Vminmaxss, "EVEX.LIG.66.0F3A.W0 53 /r", "x, vx, xm32, ib", Avx10v2, Masked, Scalar, 4);
        Def(X86InstrKind.Vminmaxsd, "EVEX.LIG.66.0F3A.W1 53 /r", "x, vx, xm64, ib", Avx10v2, Masked, Scalar, 8);
        foreach (var (kind, pp, w, op, operands, tuple, element, rounding) in new[]
        {
            (X86InstrKind.Vcvttps2dqs, "NP", "W0", "6D", "V, VM/b4", Full, 4, false), (X86InstrKind.Vcvttps2udqs, "NP", "W0", "6C", "V, VM/b4", Full, 4, false),
            (X86InstrKind.Vcvttpd2dqs, "NP", "W1", "6D", "H, VM/b8", Full, 8, false), (X86InstrKind.Vcvttpd2udqs, "NP", "W1", "6C", "H, VM/b8", Full, 8, false),
            (X86InstrKind.Vcvttps2qqs, "66", "W0", "6D", "V, HM/b4", X86Tuple.Half, 4, false), (X86InstrKind.Vcvttps2uqqs, "66", "W0", "6C", "V, HM/b4", X86Tuple.Half, 4, false),
            (X86InstrKind.Vcvttpd2qqs, "66", "W1", "6D", "V, VM/b8", Full, 8, false), (X86InstrKind.Vcvttpd2uqqs, "66", "W1", "6C", "V, VM/b8", Full, 8, false),
            (X86InstrKind.Vcvtps2ibs, "66", "W0", "69", "V, VM/b4", Full, 4, true), (X86InstrKind.Vcvtps2iubs, "66", "W0", "6B", "V, VM/b4", Full, 4, true),
            (X86InstrKind.Vcvttps2ibs, "66", "W0", "68", "V, VM/b4", Full, 4, false), (X86InstrKind.Vcvttps2iubs, "66", "W0", "6A", "V, VM/b4", Full, 4, false),
        })
        {
            Vl(kind, $"EVEX.L.{pp}.MAP5.{w} {op} /r", operands, Avx10v2, tuple, element, flags: rounding ? Rounded : Masked);
        }
        Def(X86InstrKind.Movd, "EVEX.128.F3.0F.W0 7E /r", "x, xm32", Avx10v2, Plain, Scalar, 4);
        Def(X86InstrKind.Movd, "EVEX.128.66.0F.W0 D6 /r", "xm32, x", Avx10v2, Plain, Scalar, 4);
        Def(X86InstrKind.Vmovw, "EVEX.128.F3.MAP5.W0 6E /r", "x, xm16", Avx10v2, Plain, Scalar, 2);
        Def(X86InstrKind.Vmovw, "EVEX.128.F3.MAP5.W0 7E /r", "xm16, x", Avx10v2, Plain, Scalar, 2);
        Vl(X86InstrKind.Mpsadbw, "EVEX.L.F3.0F3A.W0 42 /r", "V, vV, VM, ib", Avx10v2, FullMemory);
    }

    // EVEX forms at every length: V, vV, VM and VR take the length, H and HM half of it, QM a quarter and EM an eighth
    private static void Vl(X86InstrKind kind, string opcode, string operands, X86IsaFlags isa, X86Tuple tuple, int element = 0,
        X86FormFlags flags = X86FormFlags.Masking, int minimum = 128, string? mnemonic = null)
    {
        for (int bits = minimum; bits <= 512; bits *= 2)
        {
            Def(kind, opcode.Replace("EVEX.L.", $"EVEX.{bits}."), Resize(operands, bits), isa,
                bits == 512 ? flags : flags & ~X86FormFlags.Rounding, tuple, element, mnemonic);
        }
    }

    private static string Resize(string operands, int bits)
    {
        string full = bits switch { 128 => "x", 256 => "y", _ => "z" };
        var parts = operands.Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            int slash = parts[i].IndexOf('/');
            string token = slash < 0 ? parts[i] : parts[i][..slash];
            string suffix = slash < 0 ? "" : parts[i][slash..];
            token = token switch
            {
                "V" => full,
                "vV" => "v" + full,
                "VM" => full + "m",
                "VR" => full + "r",
                "H" => bits == 512 ? "y" : "x",
                "HM" => bits switch { 128 => "xm64", 256 => "xm", _ => "ym" },
                "QM" => bits switch { 128 => "xm32", 256 => "xm64", _ => "xm" },
                "EM" => bits switch { 128 => "xm16", 256 => "xm32", _ => "xm64" },
                _ => token,
            };
            parts[i] = token + suffix;
        }
        return string.Join(", ", parts);
    }

    // Element is the broadcast size; byte and word operations have none
    private readonly record struct Evex(string W, X86IsaFlags Isa, int Element = 0, string? Mnemonic = null, bool MaskResult = false,
        X86FormFlags Flags = X86FormFlags.Masking, string? Operands = null, X86Tuple Tuple = X86Tuple.None);

    private static readonly Evex Bytes = new Evex("WIG", BW);

    private const X86FormFlags Rounded = X86FormFlags.Masking | X86FormFlags.Rounding;

    private static string Wide(Evex evex) => evex.Element == 0 ? "zm" : $"zm/b{evex.Element}";

    private static X86Tuple TupleOf(Evex evex)
        => evex.Tuple != X86Tuple.None ? evex.Tuple : evex.Element == 0 ? X86Tuple.FullMemory : X86Tuple.Full;

    // "NP 0F 58" -> "VEX.<length>.NP.0F.WIG 58"
    private static string Vex(string length, string legacy)
    {
        var (pp, map, opcode) = SplitLegacy(legacy);
        return $"VEX.{length}.{pp}.{map}.WIG {opcode}";
    }

    // "66 0F 58" -> "EVEX.<length>.66.0F.<w> 58"
    private static string EvexOpcode(string length, string legacy, string w)
    {
        var (pp, map, opcode) = SplitLegacy(legacy);
        return $"EVEX.{length}.{pp}.{map}.{w} {opcode}";
    }

    private static (string Pp, string Map, string Opcode) SplitLegacy(string legacy)
    {
        var tokens = legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string pp = "NP";
        string map = "0F";
        int i = 0;
        if (tokens[i] is "NP" or "66" or "F2" or "F3")
            pp = tokens[i++];
        if (tokens[i] == "0F")
            i++;
        else if (tokens[i] is "0F38" or "0F3A")
            map = tokens[i++];
        return (pp, map, string.Join(' ', tokens[i..]));
    }

    private static void Move(X86InstrKind kind, string load, string store, X86IsaFlags isa, Evex? evex = null)
    {
        Def(kind, load + " /r", "x, xm", isa);
        Def(kind, store + " /r", "xm, x", isa);
        Def(kind, Vex("128", load) + " /r", "x, xm", Avx);
        Def(kind, Vex("128", store) + " /r", "xm, x", Avx);
        Def(kind, Vex("256", load) + " /r", "y, ym", Avx);
        Def(kind, Vex("256", store) + " /r", "ym, y", Avx);
        if (evex is Evex e)
        {
            Vl(kind, EvexOpcode("L", load + " /r", e.W), "V, VM", e.Isa, X86Tuple.FullMemory, e.Element, e.Flags);
            Vl(kind, EvexOpcode("L", store + " /r", e.W), "VM, V", e.Isa, X86Tuple.FullMemory, e.Element, e.Flags);
        }
    }

    private static void Binary(X86InstrKind kind, string legacy, X86IsaFlags isa, X86IsaFlags ymm, string immediate = "", Evex? evex = null)
    {
        Def(kind, legacy + " /r", "x, xm" + immediate, isa);
        Def(kind, Vex("128", legacy) + " /r", "x, vx, xm" + immediate, Avx);
        if (ymm != NoYmm)
            Def(kind, Vex("256", legacy) + " /r", "y, vy, ym" + immediate, ymm);
        if (evex is Evex e)
        {
            string operands = (e.MaskResult ? "k, vz, " : "z, vz, ") + Wide(e) + immediate;
            Def(kind, EvexOpcode("512", legacy + " /r", e.W), operands, e.Isa, e.Flags, TupleOf(e), e.Element, e.Mnemonic);
        }
    }

    private static void ScalarBinary(X86InstrKind kind, string legacy, X86IsaFlags isa, int bits, string immediate = "", Evex? evex = null)
    {
        Def(kind, legacy + " /r", $"x, xm{bits}" + immediate, isa);
        Def(kind, Vex("LIG", legacy) + " /r", $"x, vx, xm{bits}" + immediate, Avx);
        if (evex is Evex e)
            Def(kind, EvexOpcode("LIG", legacy + " /r", e.W), $"x, vx, xm{bits}" + immediate, e.Isa, e.Flags, X86Tuple.Scalar, bits / 8);
    }

    private static void Unary(X86InstrKind kind, string legacy, X86IsaFlags isa, X86IsaFlags ymm, string source = "xm", string? ymmOperands = null, string immediate = "", Evex? evex = null)
    {
        Def(kind, legacy + " /r", "x, " + source + immediate, isa);
        Def(kind, Vex("128", legacy) + " /r", "x, " + source + immediate, Avx);
        if (ymm != NoYmm)
            Def(kind, Vex("256", legacy) + " /r", (ymmOperands ?? "y, ym") + immediate, ymm);
        if (evex is Evex e)
            Def(kind, EvexOpcode("512", legacy + " /r", e.W), (e.Operands ?? "z, " + Wide(e)) + immediate, e.Isa, e.Flags, TupleOf(e), e.Element, e.Mnemonic);
    }
}
