using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Cnidaria.Python
{
    public enum PythonBytecodeVersion : byte
    {
        CPython3_14_6,
    }

    [Flags]
    public enum CodeFlags : int
    {
        None = 0,
        Optimized = 0x0001,
        NewLocals = 0x0002,
        VarArgs = 0x0004,
        VarKeywords = 0x0008,
        Nested = 0x0010,
        Generator = 0x0020,
        Coroutine = 0x0080,
        AsyncGenerator = 0x0200,
        FutureAnnotations = 0x01000000,
        NoMonitoring = 0x02000000,
        HasDocString = 0x04000000,
        Method = 0x08000000,
    }

    [Flags]
    public enum LocalKind : byte
    {
        None = 0,
        PositionalArgument = 0x02,
        KeywordArgument = 0x04,
        VariadicArgument = 0x08,
        Hidden = 0x10,
        Local = 0x20,
        Cell = 0x40,
        Free = 0x80,
    }

    public enum ConstantKind : byte
    {
        None,
        Boolean,
        Integer,
        Float,
        Complex,
        String,
        Bytes,
        Tuple,
        FrozenSet,
        Code,
        Ellipsis,
    }

    public abstract class PythonConstant
    {
        private protected PythonConstant(ConstantKind kind)
        {
            Kind = kind;
        }

        public ConstantKind Kind { get; }
    }

    public sealed class PythonNoneConstant : PythonConstant
    {
        private PythonNoneConstant() : base(ConstantKind.None) { }
        public static PythonNoneConstant Instance { get; } = new();
        public override string ToString() => "None";
    }

    public sealed class EllipsisConstant : PythonConstant
    {
        private EllipsisConstant() : base(ConstantKind.Ellipsis) { }
        public static EllipsisConstant Instance { get; } = new();
        public override string ToString() => "Ellipsis";
    }

    public sealed class BooleanConstant : PythonConstant
    {
        public BooleanConstant(bool value) : base(ConstantKind.Boolean)
        {
            Value = value;
        }

        public bool Value { get; }
        public override string ToString() => Value ? "True" : "False";
    }

    public sealed class IntegerConstant : PythonConstant
    {
        public IntegerConstant(BigInteger value) : base(ConstantKind.Integer)
        {
            Value = value;
        }

        public BigInteger Value { get; }
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class FloatConstant : PythonConstant
    {
        public FloatConstant(double value) : base(ConstantKind.Float)
        {
            Value = value;
        }

        public double Value { get; }
        public override string ToString() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class ComplexConstant : PythonConstant
    {
        public ComplexConstant(double real, double imaginary) : base(ConstantKind.Complex)
        {
            Real = real;
            Imaginary = imaginary;
        }

        public double Real { get; }
        public double Imaginary { get; }
        public override string ToString() => $"({Real:R}{(Imaginary < 0 ? string.Empty : "+")}{Imaginary:R}j)";
    }

    public sealed class StringConstant : PythonConstant
    {
        public StringConstant(string value) : base(ConstantKind.String)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Value { get; }
        public override string ToString() => Value;
    }

    public sealed class BytesConstant : PythonConstant
    {
        public BytesConstant(IEnumerable<byte> value) : base(ConstantKind.Bytes)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = [.. value];
        }

        public ImmutableArray<byte> Value { get; }
        public override string ToString() => $"bytes[{Value.Length}]";
    }

    public sealed class TupleConstant : PythonConstant
    {
        public TupleConstant(IEnumerable<PythonConstant> items) : base(ConstantKind.Tuple)
        {
            ArgumentNullException.ThrowIfNull(items);
            Items = [.. items];
        }

        public ImmutableArray<PythonConstant> Items { get; }
        public override string ToString() => $"tuple[{Items.Length}]";
    }

    public sealed class FrozenSetConstant : PythonConstant
    {
        public FrozenSetConstant(IEnumerable<PythonConstant> items) : base(ConstantKind.FrozenSet)
        {
            ArgumentNullException.ThrowIfNull(items);
            Items = [.. items];
        }

        public ImmutableArray<PythonConstant> Items { get; }
        public override string ToString() => $"frozenset[{Items.Length}]";
    }

    public sealed class CodeConstant : PythonConstant
    {
        public CodeConstant(PythonCodeObject value) : base(ConstantKind.Code)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public PythonCodeObject Value { get; }
        public override string ToString() => $"<code object {Value.Name}>";
    }

    public sealed class PythonCodeObject
    {
        internal PythonCodeObject(
            PythonBytecodeVersion version,
            int argumentCount,
            int positionalOnlyArgumentCount,
            int keywordOnlyArgumentCount,
            int stackSize,
            CodeFlags flags,
            ImmutableArray<byte> bytecode,
            ImmutableArray<PythonConstant> constants,
            ImmutableArray<string> names,
            ImmutableArray<string> localsPlusNames,
            ImmutableArray<LocalKind> localsPlusKinds,
            string fileName,
            string name,
            string qualifiedName,
            int firstLineNumber,
            ImmutableArray<byte> lineTable,
            ImmutableArray<byte> exceptionTable)
        {
            if (argumentCount < 0)
                throw new ArgumentOutOfRangeException(nameof(argumentCount));
            if (positionalOnlyArgumentCount < 0 || positionalOnlyArgumentCount > argumentCount)
                throw new ArgumentOutOfRangeException(nameof(positionalOnlyArgumentCount));
            if (keywordOnlyArgumentCount < 0)
                throw new ArgumentOutOfRangeException(nameof(keywordOnlyArgumentCount));
            if (stackSize < 0)
                throw new ArgumentOutOfRangeException(nameof(stackSize));
            if (localsPlusNames.Length != localsPlusKinds.Length)
                throw new ArgumentException("Locals-plus names and kinds must have equal lengths.");
            if ((bytecode.Length & 1) != 0)
                throw new ArgumentException("CPython wordcode must contain complete two-byte code units.", nameof(bytecode));

            Version = version;
            ArgumentCount = argumentCount;
            PositionalOnlyArgumentCount = positionalOnlyArgumentCount;
            KeywordOnlyArgumentCount = keywordOnlyArgumentCount;
            StackSize = stackSize;
            Flags = flags;
            Bytecode = bytecode.IsDefault ? [] : bytecode;
            Constants = constants.IsDefault ? [] : constants;
            Names = names.IsDefault ? [] : names;
            LocalsPlusNames = localsPlusNames.IsDefault ? [] : localsPlusNames;
            LocalsPlusKinds = localsPlusKinds.IsDefault ? [] : localsPlusKinds;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            QualifiedName = qualifiedName ?? throw new ArgumentNullException(nameof(qualifiedName));
            FirstLineNumber = firstLineNumber;
            LineTable = lineTable.IsDefault ? [] : lineTable;
            ExceptionTable = exceptionTable.IsDefault ? [] : exceptionTable;
        }

        public PythonBytecodeVersion Version { get; }
        public int ArgumentCount { get; }
        public int PositionalOnlyArgumentCount { get; }
        public int KeywordOnlyArgumentCount { get; }
        public int StackSize { get; }
        public CodeFlags Flags { get; }
        public ImmutableArray<byte> Bytecode { get; }
        public ImmutableArray<PythonConstant> Constants { get; }
        public ImmutableArray<string> Names { get; }
        public ImmutableArray<string> LocalsPlusNames { get; }
        public ImmutableArray<LocalKind> LocalsPlusKinds { get; }
        public string FileName { get; }
        public string Name { get; }
        public string QualifiedName { get; }
        public int FirstLineNumber { get; }
        public ImmutableArray<byte> LineTable { get; }
        public ImmutableArray<byte> ExceptionTable { get; }
    }

    public enum EmitDiagnosticSeverity : byte
    {
        Info,
        Warning,
        Error,
    }

    public enum EmitDiagnosticCode : ushort
    {
        SyntaxTreeContainsErrors,
        SymbolTableContainsErrors,
        UnsupportedSyntax,
        UnsupportedClosure,
        UnsupportedGenerator,
        UnsupportedCoroutine,
        UnsupportedLiteral,
        InvalidLiteral,
        InvalidAssignmentTarget,
        InvalidControlFlow,
        InvalidBytecode,
        OperandOutOfRange,
        InvalidOptions,
        InternalEmitterError,
    }

    public readonly struct EmitDiagnostic
    {
        public EmitDiagnostic(
            EmitDiagnosticCode code,
            EmitDiagnosticSeverity severity,
            TextSpan span,
            string message)
        {
            Code = code;
            Severity = severity;
            Span = span;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public EmitDiagnosticCode Code { get; }
        public EmitDiagnosticSeverity Severity { get; }
        public TextSpan Span { get; }
        public string Message { get; }
    }

    public sealed class EmitOptions
    {
        public PythonBytecodeVersion BytecodeVersion { get; init; } = PythonBytecodeVersion.CPython3_14_6;
        public string FileName { get; init; } = "<module>";
        public string ModuleName { get; init; } = "<module>";
        public int FirstLineNumber { get; init; } = 1;
        public int OptimizationLevel { get; init; }
        public bool EmitNoMonitoringFlag { get; init; }
    }

    public sealed class EmitResult
    {
        internal EmitResult(
            PythonCodeObject? codeObject,
            ImmutableArray<EmitDiagnostic> diagnostics)
        {
            CodeObject = codeObject;
            Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        }

        public PythonCodeObject? CodeObject { get; }
        public ImmutableArray<EmitDiagnostic> Diagnostics { get; }
        public bool Success
        {
            get
            {
                if (CodeObject is null)
                    return false;

                foreach (var diagnostic in Diagnostics)
                {
                    if (diagnostic.Severity == EmitDiagnosticSeverity.Error)
                        return false;
                }

                return true;
            }
        }
    }
}
