using System;
using System.Text;

namespace Cnidaria.C;

/// <summary>Identifies the operation carried by a lowered node independently of source syntax</summary>
/// <remarks>Codes mirror the GCC tree codes that survive gimplification</remarks>
public enum GimpleTreeCode : ushort
{
    None = 0,
    ErrorMark,

    // Leaves

    SsaName,
    VarDecl,
    ParmDecl,
    FunctionDecl,
    IntegerCst,
    RealCst,
    StringCst,
    Constructor,

    // References

    MemRef,
    ArrayRef,
    ComponentRef,

    // Unary

    NegateExpr,
    BitNotExpr,
    AbsExpr,
    TruthNotExpr,
    NopExpr,
    ConvertExpr,
    FixTruncExpr,
    FloatExpr,
    ViewConvertExpr,

    // Binary

    PlusExpr,
    MinusExpr,
    MultExpr,
    PointerPlusExpr,
    PointerDiffExpr,
    TruncDivExpr,
    TruncModExpr,
    ExactDivExpr,
    RdivExpr,
    BitAndExpr,
    BitIorExpr,
    BitXorExpr,
    LshiftExpr,
    RshiftExpr,
    LrotateExpr,
    RrotateExpr,
    MinExpr,
    MaxExpr,
    TruthAndExpr,
    TruthOrExpr,
    TruthXorExpr,

    // Comparisons

    LtExpr,
    LeExpr,
    GtExpr,
    GeExpr,
    EqExpr,
    NeExpr,
    UnorderedExpr,
    OrderedExpr,
    UnltExpr,
    UnleExpr,
    UngtExpr,
    UngeExpr,
    UneqExpr,
    LtgtExpr,

    // Expressions

    AddrExpr,
    CondExpr,
    CallExpr,

    Count,
}

/// <summary>Groups tree codes the way GCC groups them through TREE_CODE_CLASS</summary>
public enum GimpleTreeCodeClass : byte
{
    Exceptional,
    Constant,
    Declaration,
    Reference,
    Unary,
    Binary,
    Comparison,
    Expression,
}

/// <summary>Classifies the right-hand side shape of a GIMPLE assignment</summary>
/// <remarks>Matches the GCC gimple_rhs_class partition used to walk assignment operands</remarks>
public enum GimpleRhsClass : byte
{
    Invalid,
    Single,
    Unary,
    Binary,
    Ternary,
}

/// <summary>Provides the static operation tables that drive tree code dispatch</summary>
/// <remarks>Every lookup is an array index so operation tests stay off the string path</remarks>
public static class GimpleOperators
{
    private const int CodeCount = (int)GimpleTreeCode.Count;

    private static readonly GimpleTreeCodeClass[] Classes = BuildClasses();
    private static readonly GimpleRhsClass[] RhsClasses = BuildRhsClasses();
    private static readonly string[] Names = BuildNames();
    private static readonly string[] Spellings = BuildSpellings();
    private static readonly bool[] Commutative = BuildCommutative();

    /// <summary>Gets the GCC-style class of a tree code</summary>
    public static GimpleTreeCodeClass ClassOf(GimpleTreeCode code)
        => (uint)code < CodeCount ? Classes[(int)code] : GimpleTreeCodeClass.Exceptional;

    /// <summary>Gets the assignment right-hand side class produced by a tree code</summary>
    public static GimpleRhsClass RhsClassOf(GimpleTreeCode code)
        => (uint)code < CodeCount ? RhsClasses[(int)code] : GimpleRhsClass.Invalid;

    /// <summary>Gets the number of operands a tree code consumes as an assignment right-hand side</summary>
    public static int Arity(GimpleTreeCode code)
    {
        switch (RhsClassOf(code))
        {
            case GimpleRhsClass.Single:
            case GimpleRhsClass.Unary:
                return 1;
            case GimpleRhsClass.Binary:
                return 2;
            case GimpleRhsClass.Ternary:
                return 3;
            default:
                return 0;
        }
    }

    /// <summary>Gets the diagnostic name of a tree code</summary>
    public static string Name(GimpleTreeCode code)
        => (uint)code < CodeCount ? Names[(int)code] : "<unknown>";

    /// <summary>Gets the C operator text that renders a tree code for the low-level pipeline</summary>
    /// <remarks>Codes with no C spelling fall back to their diagnostic name</remarks>
    public static string Spelling(GimpleTreeCode code)
        => (uint)code < CodeCount ? Spellings[(int)code] : "<unknown>";

    public static bool IsComparison(GimpleTreeCode code)
        => ClassOf(code) == GimpleTreeCodeClass.Comparison;

    /// <summary>Gets whether operand order carries no meaning for a tree code</summary>
    public static bool IsCommutative(GimpleTreeCode code)
        => (uint)code < CodeCount && Commutative[(int)code];

    /// <summary>Gets whether a tree code reinterprets or resizes a single operand</summary>
    public static bool IsConversion(GimpleTreeCode code)
        => code is GimpleTreeCode.NopExpr or GimpleTreeCode.ConvertExpr or GimpleTreeCode.FixTruncExpr
            or GimpleTreeCode.FloatExpr or GimpleTreeCode.ViewConvertExpr;

    /// <summary>Maps a C unary operator onto the tree code that implements it</summary>
    public static GimpleTreeCode FromUnaryOperator(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PlusToken: return GimpleTreeCode.NopExpr;
            case SyntaxKind.MinusToken: return GimpleTreeCode.NegateExpr;
            case SyntaxKind.TildeToken: return GimpleTreeCode.BitNotExpr;
            case SyntaxKind.BangToken: return GimpleTreeCode.TruthNotExpr;
            default: return GimpleTreeCode.None;
        }
    }

    /// <summary>Maps a C binary operator onto the tree code that implements it for the given operand types</summary>
    /// <remarks>Pointer arithmetic and floating-point division select the dedicated codes GCC uses</remarks>
    public static GimpleTreeCode FromBinaryOperator(SyntaxKind kind, QualifiedType left, QualifiedType right)
    {
        switch (kind)
        {
            case SyntaxKind.PlusToken:
                if (GimpleTypes.IsPointerLike(left) && GimpleTypes.IsIntegerLike(right))
                    return GimpleTreeCode.PointerPlusExpr;
                if (GimpleTypes.IsIntegerLike(left) && GimpleTypes.IsPointerLike(right))
                    return GimpleTreeCode.PointerPlusExpr;
                return GimpleTreeCode.PlusExpr;

            case SyntaxKind.MinusToken:
                if (GimpleTypes.IsPointerLike(left) && GimpleTypes.IsPointerLike(right))
                    return GimpleTreeCode.PointerDiffExpr;
                if (GimpleTypes.IsPointerLike(left) && GimpleTypes.IsIntegerLike(right))
                    return GimpleTreeCode.PointerPlusExpr;
                return GimpleTreeCode.MinusExpr;


            case SyntaxKind.StarToken:
                return GimpleTreeCode.MultExpr;

            case SyntaxKind.SlashToken:
                return GimpleTypes.IsFloating(left) || GimpleTypes.IsFloating(right)
                    ? GimpleTreeCode.RdivExpr
                    : GimpleTreeCode.TruncDivExpr;

            case SyntaxKind.PercentToken: return GimpleTreeCode.TruncModExpr;
            case SyntaxKind.AmpersandToken: return GimpleTreeCode.BitAndExpr;
            case SyntaxKind.PipeToken: return GimpleTreeCode.BitIorExpr;
            case SyntaxKind.HatToken: return GimpleTreeCode.BitXorExpr;
            case SyntaxKind.LessThanLessThanToken: return GimpleTreeCode.LshiftExpr;
            case SyntaxKind.GreaterThanGreaterThanToken: return GimpleTreeCode.RshiftExpr;
            case SyntaxKind.LessThanToken: return GimpleTreeCode.LtExpr;
            case SyntaxKind.LessThanEqualsToken: return GimpleTreeCode.LeExpr;
            case SyntaxKind.GreaterThanToken: return GimpleTreeCode.GtExpr;
            case SyntaxKind.GreaterThanEqualsToken: return GimpleTreeCode.GeExpr;
            case SyntaxKind.EqualsEqualsToken: return GimpleTreeCode.EqExpr;
            case SyntaxKind.BangEqualsToken: return GimpleTreeCode.NeExpr;
            case SyntaxKind.AmpersandAmpersandToken: return GimpleTreeCode.TruthAndExpr;
            case SyntaxKind.PipePipeToken: return GimpleTreeCode.TruthOrExpr;
            default: return GimpleTreeCode.None;
        }
    }

    /// <summary>Gets whether an operator steps a pointer backwards and needs its offset negated</summary>
    /// <remarks>A pointer subtraction lowers to a pointer addition, so the offset carries the sign</remarks>
    public static bool NegatesPointerOffset(SyntaxKind kind, QualifiedType left, QualifiedType right)
        => kind == SyntaxKind.MinusToken &&
           GimpleTypes.IsPointerLike(left) &&
           GimpleTypes.IsIntegerLike(right);

    /// <summary>Maps a compound assignment operator onto the plain operator it embeds</summary>
    public static SyntaxKind CompoundAssignmentOperator(SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.PlusEqualsToken: return SyntaxKind.PlusToken;
            case SyntaxKind.MinusEqualsToken: return SyntaxKind.MinusToken;
            case SyntaxKind.StarEqualsToken: return SyntaxKind.StarToken;
            case SyntaxKind.SlashEqualsToken: return SyntaxKind.SlashToken;
            case SyntaxKind.PercentEqualsToken: return SyntaxKind.PercentToken;
            case SyntaxKind.AmpersandEqualsToken: return SyntaxKind.AmpersandToken;
            case SyntaxKind.PipeEqualsToken: return SyntaxKind.PipeToken;
            case SyntaxKind.HatEqualsToken: return SyntaxKind.HatToken;
            case SyntaxKind.LessThanLessThanEqualsToken: return SyntaxKind.LessThanLessThanToken;
            case SyntaxKind.GreaterThanGreaterThanEqualsToken: return SyntaxKind.GreaterThanGreaterThanToken;
            default: return SyntaxKind.None;
        }
    }

    /// <summary>Gets the tree code that identifies a lowered value</summary>
    /// <remarks>This is the subcode a single right-hand side assignment carries for the value</remarks>
    public static GimpleTreeCode CodeOf(GimpleValue value)
    {
        switch (value)
        {
            case GimpleName:
                return GimpleTreeCode.SsaName;

            case GimpleSymbolValue symbolValue:
                return symbolValue.Symbol switch
                {
                    FunctionSymbol => GimpleTreeCode.FunctionDecl,
                    ParameterSymbol => GimpleTreeCode.ParmDecl,
                    _ => GimpleTreeCode.VarDecl,
                };

            case GimpleTemporaryValue:
                return GimpleTreeCode.VarDecl;

            case GimpleConstantValue constant:
                return constant.Value switch
                {
                    string => GimpleTreeCode.StringCst,
                    float or double or decimal => GimpleTreeCode.RealCst,
                    _ => GimpleTypes.IsFloating(constant.Type) ? GimpleTreeCode.RealCst : GimpleTreeCode.IntegerCst,
                };

            case GimpleIndirectExpression:
                return GimpleTreeCode.MemRef;

            case GimpleElementAccessExpression:
                return GimpleTreeCode.ArrayRef;

            case GimpleMemberAccessExpression:
                return GimpleTreeCode.ComponentRef;

            case GimpleAddressOfExpression:
                return GimpleTreeCode.AddrExpr;

            case GimpleUnaryExpression unary:
                return unary.Code;

            case GimpleBinaryExpression binary:
                return binary.Code;

            case GimpleConversionExpression conversion:
                return conversion.Code;

            case GimpleCastExpression cast:
                return ConversionCode(cast.Operand.Type, cast.Type);

            default:
                return GimpleTreeCode.ErrorMark;
        }
    }

    /// <summary>Selects the conversion tree code that turns a source type into a destination type</summary>
    public static GimpleTreeCode ConversionCode(QualifiedType source, QualifiedType destination)
    {
        var sourceFloating = GimpleTypes.IsFloating(source);
        var destinationFloating = GimpleTypes.IsFloating(destination);

        if (sourceFloating && !destinationFloating)
            return GimpleTypes.IsIntegerLike(destination) ? GimpleTreeCode.FixTruncExpr : GimpleTreeCode.ViewConvertExpr;

        if (!sourceFloating && destinationFloating)
            return GimpleTypes.IsIntegerLike(source) ? GimpleTreeCode.FloatExpr : GimpleTreeCode.ViewConvertExpr;

        if (sourceFloating)
            return GimpleTreeCode.ConvertExpr;

        var sourceScalar = GimpleTypes.IsIntegerLike(source) || GimpleTypes.IsPointerLike(source);
        var destinationScalar = GimpleTypes.IsIntegerLike(destination) || GimpleTypes.IsPointerLike(destination);
        if (sourceScalar && destinationScalar)
            return GimpleTreeCode.NopExpr;

        return GimpleTreeCode.ViewConvertExpr;
    }

    private static GimpleTreeCodeClass[] BuildClasses()
    {
        var classes = new GimpleTreeCodeClass[CodeCount];

        classes[(int)GimpleTreeCode.SsaName] = GimpleTreeCodeClass.Declaration;
        classes[(int)GimpleTreeCode.VarDecl] = GimpleTreeCodeClass.Declaration;
        classes[(int)GimpleTreeCode.ParmDecl] = GimpleTreeCodeClass.Declaration;
        classes[(int)GimpleTreeCode.FunctionDecl] = GimpleTreeCodeClass.Declaration;

        classes[(int)GimpleTreeCode.IntegerCst] = GimpleTreeCodeClass.Constant;
        classes[(int)GimpleTreeCode.RealCst] = GimpleTreeCodeClass.Constant;
        classes[(int)GimpleTreeCode.StringCst] = GimpleTreeCodeClass.Constant;

        classes[(int)GimpleTreeCode.MemRef] = GimpleTreeCodeClass.Reference;
        classes[(int)GimpleTreeCode.ArrayRef] = GimpleTreeCodeClass.Reference;
        classes[(int)GimpleTreeCode.ComponentRef] = GimpleTreeCodeClass.Reference;

        classes[(int)GimpleTreeCode.AddrExpr] = GimpleTreeCodeClass.Expression;
        classes[(int)GimpleTreeCode.CondExpr] = GimpleTreeCodeClass.Expression;
        classes[(int)GimpleTreeCode.CallExpr] = GimpleTreeCodeClass.Expression;

        classes[(int)GimpleTreeCode.NegateExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.BitNotExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.AbsExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.TruthNotExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.NopExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.ConvertExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.FixTruncExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.FloatExpr] = GimpleTreeCodeClass.Unary;
        classes[(int)GimpleTreeCode.ViewConvertExpr] = GimpleTreeCodeClass.Unary;

        classes[(int)GimpleTreeCode.PlusExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.MinusExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.MultExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.PointerPlusExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.PointerDiffExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.TruncDivExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.TruncModExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.ExactDivExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.RdivExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.BitAndExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.BitIorExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.BitXorExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.LshiftExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.RshiftExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.LrotateExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.RrotateExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.MinExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.MaxExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.TruthAndExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.TruthOrExpr] = GimpleTreeCodeClass.Binary;
        classes[(int)GimpleTreeCode.TruthXorExpr] = GimpleTreeCodeClass.Binary;

        classes[(int)GimpleTreeCode.LtExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.LeExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.GtExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.GeExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.EqExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.NeExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UnorderedExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.OrderedExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UnltExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UnleExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UngtExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UngeExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.UneqExpr] = GimpleTreeCodeClass.Comparison;
        classes[(int)GimpleTreeCode.LtgtExpr] = GimpleTreeCodeClass.Comparison;

        return classes;
    }

    private static GimpleRhsClass[] BuildRhsClasses()
    {
        var rhsClasses = new GimpleRhsClass[CodeCount];
        for (var code = 0; code < CodeCount; code++)
        {
            switch (Classes[code])
            {
                case GimpleTreeCodeClass.Unary:
                    rhsClasses[code] = GimpleRhsClass.Unary;
                    break;
                case GimpleTreeCodeClass.Binary:
                case GimpleTreeCodeClass.Comparison:
                    rhsClasses[code] = GimpleRhsClass.Binary;
                    break;
                case GimpleTreeCodeClass.Constant:
                case GimpleTreeCodeClass.Declaration:
                case GimpleTreeCodeClass.Reference:
                    rhsClasses[code] = GimpleRhsClass.Single;
                    break;
                default:
                    rhsClasses[code] = GimpleRhsClass.Invalid;
                    break;
            }
        }

        rhsClasses[(int)GimpleTreeCode.AddrExpr] = GimpleRhsClass.Single;
        rhsClasses[(int)GimpleTreeCode.Constructor] = GimpleRhsClass.Single;
        rhsClasses[(int)GimpleTreeCode.ErrorMark] = GimpleRhsClass.Single;
        rhsClasses[(int)GimpleTreeCode.CondExpr] = GimpleRhsClass.Ternary;
        return rhsClasses;
    }

    private static string[] BuildNames()
    {
        var names = new string[CodeCount];
        for (var code = 0; code < CodeCount; code++)
            names[code] = ToSnakeCase(((GimpleTreeCode)code).ToString());
        return names;
    }

    // Spellings feed the low-level pipeline, whose operator dispatch is textual
    private static string[] BuildSpellings()
    {
        var spellings = new string[CodeCount];
        for (var code = 0; code < CodeCount; code++)
            spellings[code] = Names[code];

        spellings[(int)GimpleTreeCode.PlusExpr] = "+";
        spellings[(int)GimpleTreeCode.PointerPlusExpr] = "+";
        spellings[(int)GimpleTreeCode.MinusExpr] = "-";
        spellings[(int)GimpleTreeCode.PointerDiffExpr] = "-";
        spellings[(int)GimpleTreeCode.MultExpr] = "*";
        spellings[(int)GimpleTreeCode.TruncDivExpr] = "/";
        spellings[(int)GimpleTreeCode.ExactDivExpr] = "/";
        spellings[(int)GimpleTreeCode.RdivExpr] = "/";
        spellings[(int)GimpleTreeCode.TruncModExpr] = "%";
        spellings[(int)GimpleTreeCode.BitAndExpr] = "&";
        spellings[(int)GimpleTreeCode.BitIorExpr] = "|";
        spellings[(int)GimpleTreeCode.BitXorExpr] = "^";
        spellings[(int)GimpleTreeCode.LshiftExpr] = "<<";
        spellings[(int)GimpleTreeCode.RshiftExpr] = ">>";
        spellings[(int)GimpleTreeCode.TruthAndExpr] = "&&";
        spellings[(int)GimpleTreeCode.TruthOrExpr] = "||";
        spellings[(int)GimpleTreeCode.LtExpr] = "<";
        spellings[(int)GimpleTreeCode.LeExpr] = "<=";
        spellings[(int)GimpleTreeCode.GtExpr] = ">";
        spellings[(int)GimpleTreeCode.GeExpr] = ">=";
        spellings[(int)GimpleTreeCode.EqExpr] = "==";
        spellings[(int)GimpleTreeCode.NeExpr] = "!=";
        spellings[(int)GimpleTreeCode.NegateExpr] = "-";
        spellings[(int)GimpleTreeCode.BitNotExpr] = "~";
        spellings[(int)GimpleTreeCode.TruthNotExpr] = "!";
        spellings[(int)GimpleTreeCode.NopExpr] = "+";
        return spellings;
    }

    private static bool[] BuildCommutative()
    {
        var commutative = new bool[CodeCount];
        commutative[(int)GimpleTreeCode.PlusExpr] = true;
        commutative[(int)GimpleTreeCode.MultExpr] = true;
        commutative[(int)GimpleTreeCode.BitAndExpr] = true;
        commutative[(int)GimpleTreeCode.BitIorExpr] = true;
        commutative[(int)GimpleTreeCode.BitXorExpr] = true;
        commutative[(int)GimpleTreeCode.MinExpr] = true;
        commutative[(int)GimpleTreeCode.MaxExpr] = true;
        commutative[(int)GimpleTreeCode.TruthXorExpr] = true;
        commutative[(int)GimpleTreeCode.EqExpr] = true;
        commutative[(int)GimpleTreeCode.NeExpr] = true;
        commutative[(int)GimpleTreeCode.UnorderedExpr] = true;
        commutative[(int)GimpleTreeCode.OrderedExpr] = true;
        commutative[(int)GimpleTreeCode.UneqExpr] = true;
        commutative[(int)GimpleTreeCode.LtgtExpr] = true;
        return commutative;
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (char.IsUpper(character))
            {
                if (i != 0)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

/// <summary>Provides the type predicates the lowered pipeline tests on operands</summary>
public static class GimpleTypes
{
    public static bool IsVoid(QualifiedType type)
        => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Void };

    public static bool IsFloating(QualifiedType type)
        => type.Type is BuiltinType { BuiltinKind: BuiltinTypeKind.Float or BuiltinTypeKind.Double or BuiltinTypeKind.LongDouble };

    public static bool IsPointerLike(QualifiedType type)
        => type.Type.Kind is TypeKind.Pointer or TypeKind.Array or TypeKind.Function;

    public static bool IsIntegerLike(QualifiedType type)
    {
        if (type.Type.Kind == TypeKind.Enum)
            return true;

        if (type.Type is not BuiltinType builtin)
            return false;

        return builtin.BuiltinKind is
            BuiltinTypeKind.Bool or
            BuiltinTypeKind.Char or
            BuiltinTypeKind.SignedChar or
            BuiltinTypeKind.UnsignedChar or
            BuiltinTypeKind.Short or
            BuiltinTypeKind.UnsignedShort or
            BuiltinTypeKind.Int or
            BuiltinTypeKind.UnsignedInt or
            BuiltinTypeKind.Long or
            BuiltinTypeKind.UnsignedLong or
            BuiltinTypeKind.LongLong or
            BuiltinTypeKind.UnsignedLongLong;
    }

    public static bool IsAggregate(QualifiedType type)
        => type.Type.Kind is TypeKind.Struct or TypeKind.Union or TypeKind.Array;

    public static bool IsVolatileOrAtomic(QualifiedType type)
        => (type.Qualifiers & (TypeQualifiers.Volatile | TypeQualifiers.Atomic)) != 0;

    /// <summary>Gets whether an arithmetic type carries a sign</summary>
    public static bool IsSigned(QualifiedType type)
    {
        if (type.Type.Kind == TypeKind.Enum)
            return true;

        if (type.Type is not BuiltinType builtin)
            return false;

        return builtin.BuiltinKind is
            BuiltinTypeKind.Char or
            BuiltinTypeKind.SignedChar or
            BuiltinTypeKind.Short or
            BuiltinTypeKind.Int or
            BuiltinTypeKind.Long or
            BuiltinTypeKind.LongLong or
            BuiltinTypeKind.Float or
            BuiltinTypeKind.Double or
            BuiltinTypeKind.LongDouble;
    }
}
