using System.Collections.Generic;

namespace Cnidaria.C;

/// <summary>Names the libgcc routines for the floating formats a target computes in software, softfloat.c defines them under the same conditions</summary>
internal static class SoftFloat
{
    public static bool IsSoftware(TargetInfo target, QualifiedType type)
    {
        if (!GimpleTypes.IsFloating(type))
            return false;

        return target.Architecture switch
        {
            TargetArchitectureKind.RiscV32 or TargetArchitectureKind.RiscV64 => target.SizeOf(type) > RiscVHardwareSize(target),
            TargetArchitectureKind.Arm32 => true,
            _ => false,
        };
    }

    public static bool Involves(TargetInfo target, QualifiedType first, QualifiedType second)
        => IsSoftware(target, first) || IsSoftware(target, second);

    public static bool NeedsRoutine(TargetInfo target, QualifiedType operand, QualifiedType result)
        => Involves(target, operand, result) || ConvertsWideInteger(target, operand, result) || ConvertsWideInteger(target, result, operand);

    public static string? Arithmetic(TargetInfo target, GimpleTreeCode code, QualifiedType type)
    {
        string? operation = code switch
        {
            GimpleTreeCode.PlusExpr => "add",
            GimpleTreeCode.MinusExpr => "sub",
            GimpleTreeCode.MultExpr => "mul",
            GimpleTreeCode.RdivExpr => "div",
            _ => null,
        };
        return operation is null ? null : "__" + operation + Suffix(target, type) + "3";
    }

    public static string? Comparison(TargetInfo target, GimpleTreeCode code, QualifiedType type)
    {
        string? relation = code switch
        {
            GimpleTreeCode.EqExpr => "eq",
            GimpleTreeCode.NeExpr => "ne",
            GimpleTreeCode.LtExpr => "lt",
            GimpleTreeCode.LeExpr => "le",
            GimpleTreeCode.GtExpr => "gt",
            GimpleTreeCode.GeExpr => "ge",
            _ => null,
        };
        return relation is null ? null : "__" + relation + Suffix(target, type) + "2";
    }

    public static string? Conversion(TargetInfo target, QualifiedType source, QualifiedType destination,
        out QualifiedType argument, out QualifiedType result)
    {
        argument = source;
        result = destination;
        bool floatingSource = GimpleTypes.IsFloating(source);
        bool floatingDestination = GimpleTypes.IsFloating(destination);
        if (floatingSource && floatingDestination)
        {
            int sourceSize = target.SizeOf(source);
            int destinationSize = target.SizeOf(destination);
            if (sourceSize == destinationSize)
                return null;
            return sourceSize < destinationSize ? "__extendsfdf2" : "__truncdfsf2";
        }

        if (floatingDestination && GimpleTypes.IsIntegerLike(source))
        {
            bool wide = target.SizeOf(source) > 4;
            bool unsigned = !GimpleTypes.IsSigned(source, target);
            argument = Integer(wide, unsigned);
            return "__float" + (unsigned ? "un" : string.Empty) + (wide ? "di" : "si") + Suffix(target, destination);
        }

        if (floatingSource && GimpleTypes.IsIntegerLike(destination))
        {
            bool wide = target.SizeOf(destination) > 4;
            bool unsigned = !GimpleTypes.IsSigned(destination, target);
            result = Integer(wide, unsigned);
            return "__fix" + (unsigned ? "uns" : string.Empty) + Suffix(target, source) + (wide ? "di" : "si");
        }

        return null;
    }

    // Optimization may still change the operation or the integer type, so a format in use keeps all of its routines
    public static void CollectRoutines(TargetInfo target, GimpleStatement statement, HashSet<string> names)
    {
        switch (statement)
        {
            case GimpleAssignStatement assign when assign.RhsClass is GimpleRhsClass.Unary or GimpleRhsClass.Binary:
                AddFormat(target, assign.Lhs.Type, names);
                foreach (GimpleValue operand in assign.Operands)
                    AddFormat(target, operand.Type, names);
                if (assign.RhsClass == GimpleRhsClass.Unary)
                {
                    AddWideConversions(target, assign.Operands[0].Type, assign.Lhs.Type, names);
                    AddWideConversions(target, assign.Lhs.Type, assign.Operands[0].Type, names);
                }
                break;

            case GimpleCondStatement conditional:
                AddFormat(target, conditional.Lhs.Type, names);
                AddFormat(target, conditional.Rhs.Type, names);
                break;
        }
    }

    private static void AddFormat(TargetInfo target, QualifiedType type, HashSet<string> names)
    {
        if (!IsSoftware(target, type))
            return;

        string suffix = Suffix(target, type);
        foreach (string operation in new[] { "add", "sub", "mul", "div" })
            names.Add("__" + operation + suffix + "3");
        foreach (string relation in new[] { "eq", "ne", "lt", "le", "gt", "ge" })
            names.Add("__" + relation + suffix + "2");
        foreach (string integer in new[] { "si", "unsi", "di", "undi" })
            names.Add("__float" + integer + suffix);
        foreach (string integer in new[] { "si", "di" })
        {
            names.Add("__fix" + suffix + integer);
            names.Add("__fixuns" + suffix + integer);
        }
        names.Add("__extendsfdf2");
        names.Add("__truncdfsf2");
    }

    private static void AddWideConversions(TargetInfo target, QualifiedType integer, QualifiedType floating, HashSet<string> names)
    {
        if (!ConvertsWideInteger(target, integer, floating))
            return;

        string suffix = Suffix(target, floating);
        names.Add("__floatdi" + suffix);
        names.Add("__floatundi" + suffix);
        names.Add("__fix" + suffix + "di");
        names.Add("__fixuns" + suffix + "di");
    }

    private static bool ConvertsWideInteger(TargetInfo target, QualifiedType integer, QualifiedType floating)
        => target.IsRiscV && GimpleTypes.IsFloating(floating) && GimpleTypes.IsIntegerLike(integer) && target.SizeOf(integer) > target.RegisterSize;

    private static QualifiedType Integer(bool wide, bool unsigned)
        => TypeCatalog.Instance.Builtin(wide
            ? unsigned ? BuiltinTypeKind.UnsignedLongLong : BuiltinTypeKind.LongLong
            : unsigned ? BuiltinTypeKind.UnsignedInt : BuiltinTypeKind.Int);

    private static string Suffix(TargetInfo target, QualifiedType type)
        => target.SizeOf(type) == 4 ? "sf" : "df";

    private static int RiscVHardwareSize(TargetInfo target)
        => target.HasFeature(TargetArchitectureFeatures.RiscVD) ? 8
            : target.HasFeature(TargetArchitectureFeatures.RiscVF) ? 4
            : 0;
}
