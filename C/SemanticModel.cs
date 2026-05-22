using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C
{
    public readonly struct SemanticDiagnostic : IDiagnostic
    {
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }
        public TextSpan Position { get; }

        public SemanticDiagnostic(DiagnosticSeverity severity, string? message, TextSpan position)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Position = position;
        }

        public static SemanticDiagnostic Error(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Error, message, position);

        public static SemanticDiagnostic Warning(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Warning, message, position);

        public static SemanticDiagnostic MessageInfo(string message, TextSpan position)
            => new SemanticDiagnostic(DiagnosticSeverity.Message, message, position);
    }

    public sealed class SyntaxTree
    {
        public string Text { get; }
        public string? FilePath { get; }
        public PreprocessorOptions PreprocessorOptions { get; }
        public ParseResult ParseResult { get; }

        public TranslationUnitSyntax Root => ParseResult.Root;
        public ImmutableArray<SyntaxDiagnostic> Diagnostics => ParseResult.Diagnostics;

        private SyntaxTree(string text, PreprocessorOptions options, ParseResult parseResult)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            PreprocessorOptions = options ?? throw new ArgumentNullException(nameof(options));
            FilePath = options.FilePath;
            ParseResult = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
        }

        public static SyntaxTree ParseText(string text, PreprocessorOptions? options = null)
        {
            var effectiveOptions = options ?? PreprocessorOptions.CreateDefault();
            var parseResult = Parser.Parse(text, effectiveOptions);
            return new SyntaxTree(text, effectiveOptions, parseResult);
        }

        public static SyntaxTree ParseSource(
            string text,
            string? filePath = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            PreprocessorEnvironment? environment = null,
            bool includeStandardHeaders = true)
        {
            var effectiveOptions = PreprocessorOptions.CreateForInMemoryFiles(
                filePath: filePath,
                includeFiles: includeFiles,
                includeSearchPaths: includeSearchPaths,
                includeResolver: includeResolver,
                predefinedMacros: predefinedMacros,
                environment: environment,
                includeStandardHeaders: includeStandardHeaders);

            return ParseText(text, effectiveOptions);
        }
    }

    public sealed class CompilationOptions
    {
        public TargetInfo Target { get; }

        public CompilationOptions(
            TargetInfo? target = null)
        {
            Target = target ?? TargetInfo.Default;
        }
    }

    public sealed class Compilation
    {
        private readonly Lazy<SemanticState> _semanticState;

        public string? AssemblyName { get; }
        public ImmutableArray<SyntaxTree> SyntaxTrees { get; }
        public CompilationOptions Options { get; }

        private Compilation(
            string? assemblyName,
            ImmutableArray<SyntaxTree> syntaxTrees,
            CompilationOptions options)
        {
            AssemblyName = assemblyName;
            SyntaxTrees = syntaxTrees;
            Options = options ?? new CompilationOptions();

            _semanticState = new Lazy<SemanticState>(
                () => DeclarationCollector.Collect(this),
                isThreadSafe: true);
        }

        internal SemanticState SemanticState => _semanticState.Value;

        public Scope GlobalScope => SemanticState.GlobalScope;

        public ImmutableArray<SemanticDiagnostic> SemanticDiagnostics
            => SemanticState.Diagnostics;

        public static Compilation Create(
            string text,
            string? assemblyName = null,
            CompilationOptions? options = null,
            PreprocessorOptions? preprocessorOptions = null)
        {
            return Create(
                new[] { SyntaxTree.ParseText(text, preprocessorOptions) },
                assemblyName,
                options);
        }

        public static Compilation CreateFromSource(
            string text,
            string? filePath = null,
            IEnumerable<IncludeFile>? includeFiles = null,
            IEnumerable<string>? includeSearchPaths = null,
            IIncludeResolver? includeResolver = null,
            IReadOnlyDictionary<string, string>? predefinedMacros = null,
            PreprocessorEnvironment? environment = null,
            bool includeStandardHeaders = true,
            string? assemblyName = null,
            CompilationOptions? options = null)
        {
            return Create(
                new[]
                {
                    SyntaxTree.ParseSource(
                        text,
                        filePath,
                        includeFiles,
                        includeSearchPaths,
                        includeResolver,
                        predefinedMacros,
                        environment,
                        includeStandardHeaders)
                },
                assemblyName,
                options);
        }

        public static Compilation Create(
            IEnumerable<SyntaxTree> syntaxTrees,
            string? assemblyName = null,
            CompilationOptions? options = null)
        {
            if (syntaxTrees is null)
                throw new ArgumentNullException(nameof(syntaxTrees));

            return new Compilation(
                assemblyName,
                syntaxTrees.ToImmutableArray(),
                options ?? new CompilationOptions());
        }

        public Compilation AddSyntaxTrees(params SyntaxTree[] syntaxTrees)
        {
            if (syntaxTrees is null)
                throw new ArgumentNullException(nameof(syntaxTrees));

            return new Compilation(
                AssemblyName,
                SyntaxTrees.AddRange(syntaxTrees),
                Options);
        }

        public SemanticModel GetSemanticModel(SyntaxTree syntaxTree)
        {
            if (syntaxTree is null)
                throw new ArgumentNullException(nameof(syntaxTree));

            if (!SyntaxTrees.Contains(syntaxTree))
                throw new ArgumentException("The syntax tree is not part of this compilation.", nameof(syntaxTree));

            return new SemanticModel(this, syntaxTree);
        }

        public ImmutableArray<IDiagnostic> GetDiagnostics()
        {
            var builder = ImmutableArray.CreateBuilder<IDiagnostic>();

            foreach (var tree in SyntaxTrees)
            {
                foreach (var diagnostic in tree.Diagnostics)
                    builder.Add(diagnostic);
            }

            foreach (var diagnostic in SemanticDiagnostics)
                builder.Add(diagnostic);

            return builder.ToImmutable();
        }
    }

    public sealed class SemanticModel
    {
        private readonly Compilation _compilation;
        private readonly SyntaxTree _syntaxTree;

        internal SemanticModel(Compilation compilation, SyntaxTree syntaxTree)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _syntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        }

        public Compilation Compilation => _compilation;
        public SyntaxTree SyntaxTree => _syntaxTree;
        public TranslationUnitSyntax Root => _syntaxTree.Root;
        public BoundTree GetBoundTree() => Binder.BindTree(this);
        public GimpleTree GetGimpleTree() => GimpleTree.Lower(this);
        public Symbol? GetDeclaredSymbol(SyntaxNode node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            return _compilation.SemanticState.GetDeclaredSymbol(node);
        }

        public Symbol? GetSymbolInfo(ExpressionSyntax expression)
        {
            if (expression is null)
                throw new ArgumentNullException(nameof(expression));

            return _compilation.SemanticState.GetReferencedSymbol(expression);
        }

        public Scope? GetScope(SyntaxNode node)
        {
            if (node is null)
                throw new ArgumentNullException(nameof(node));

            return _compilation.SemanticState.GetScope(node);
        }

        public Symbol? LookupOrdinaryName(string name, SyntaxNode context)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));
            if (context is null)
                throw new ArgumentNullException(nameof(context));

            return GetScope(context)?.LookupOrdinary(name);
        }

        public TagSymbol? LookupTag(string name, SyntaxNode context)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));
            if (context is null)
                throw new ArgumentNullException(nameof(context));

            return GetScope(context)?.LookupTag(name);
        }
    }

    internal sealed class SemanticState
    {
        private readonly Dictionary<SyntaxNode, Symbol> _declaredSymbols = new();
        private readonly Dictionary<ExpressionSyntax, Symbol> _referencedSymbols = new();
        private readonly Dictionary<SyntaxNode, Scope> _scopes = new();

        public Scope GlobalScope { get; }
        public ImmutableArray<SemanticDiagnostic> Diagnostics { get; }

        public SemanticState(
            Scope globalScope,
            Dictionary<SyntaxNode, Symbol> declaredSymbols,
            Dictionary<ExpressionSyntax, Symbol> referencedSymbols,
            Dictionary<SyntaxNode, Scope> scopes,
            ImmutableArray<SemanticDiagnostic> diagnostics)
        {
            GlobalScope = globalScope ?? throw new ArgumentNullException(nameof(globalScope));

            foreach (var pair in declaredSymbols)
                _declaredSymbols[pair.Key] = pair.Value;

            foreach (var pair in referencedSymbols)
                _referencedSymbols[pair.Key] = pair.Value;

            foreach (var pair in scopes)
                _scopes[pair.Key] = pair.Value;

            Diagnostics = diagnostics;
        }

        public Symbol? GetDeclaredSymbol(SyntaxNode node)
            => _declaredSymbols.TryGetValue(node, out var symbol) ? symbol : null;

        public Symbol? GetReferencedSymbol(ExpressionSyntax expression)
            => _referencedSymbols.TryGetValue(expression, out var symbol) ? symbol : null;

        public Scope? GetScope(SyntaxNode node)
            => _scopes.TryGetValue(node, out var scope) ? scope : null;
    }

    public enum Endianness : byte { Little, Big }

    public enum CharSignedness : byte { Signed, Unsigned, ImplementationDefined }

    public readonly struct PrimitiveLayout
    {
        public int Size { get; }
        public int Alignment { get; }

        public PrimitiveLayout(int size, int alignment)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (alignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(alignment));

            Size = size;
            Alignment = alignment;
        }
    }

    public sealed class TargetInfo
    {
        public static TargetInfo Default { get; } = new TargetInfo(
            pointerSize: 4,
            pointerAlignment: 4,
            registerSize: 8,
            registerAlignment: 8,
            charLayout: new PrimitiveLayout(1, 1),
            shortLayout: new PrimitiveLayout(2, 2),
            intLayout: new PrimitiveLayout(4, 4),
            longLayout: new PrimitiveLayout(8, 8),
            longLongLayout: new PrimitiveLayout(8, 8),
            floatLayout: new PrimitiveLayout(4, 4),
            doubleLayout: new PrimitiveLayout(8, 8),
            longDoubleLayout: new PrimitiveLayout(16, 16),
            boolLayout: new PrimitiveLayout(1, 1),
            endianness: Endianness.Little,
            charSignedness: CharSignedness.ImplementationDefined);

        public int PointerSize { get; }
        public int PointerAlignment { get; }
        public int RegisterSize { get; }
        public int RegisterAlignment { get; }

        public PrimitiveLayout CharLayout { get; }
        public PrimitiveLayout ShortLayout { get; }
        public PrimitiveLayout IntLayout { get; }
        public PrimitiveLayout LongLayout { get; }
        public PrimitiveLayout LongLongLayout { get; }
        public PrimitiveLayout FloatLayout { get; }
        public PrimitiveLayout DoubleLayout { get; }
        public PrimitiveLayout LongDoubleLayout { get; }
        public PrimitiveLayout BoolLayout { get; }

        public Endianness Endianness { get; }
        public CharSignedness CharSignedness { get; }

        public TargetInfo(
            int pointerSize,
            int pointerAlignment,
            int registerSize,
            int registerAlignment,
            PrimitiveLayout charLayout,
            PrimitiveLayout shortLayout,
            PrimitiveLayout intLayout,
            PrimitiveLayout longLayout,
            PrimitiveLayout longLongLayout,
            PrimitiveLayout floatLayout,
            PrimitiveLayout doubleLayout,
            PrimitiveLayout longDoubleLayout,
            PrimitiveLayout boolLayout,
            Endianness endianness,
            CharSignedness charSignedness)
        {
            if (pointerSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pointerSize));
            if (pointerAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(pointerAlignment));
            if (registerSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerSize));
            if (registerAlignment <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerAlignment));

            PointerSize = pointerSize;
            PointerAlignment = pointerAlignment;
            RegisterSize = registerSize;
            RegisterAlignment = registerAlignment;
            CharLayout = charLayout;
            ShortLayout = shortLayout;
            IntLayout = intLayout;
            LongLayout = longLayout;
            LongLongLayout = longLongLayout;
            FloatLayout = floatLayout;
            DoubleLayout = doubleLayout;
            LongDoubleLayout = longDoubleLayout;
            BoolLayout = boolLayout;
            Endianness = endianness;
            CharSignedness = charSignedness;
        }

        public int SizeOf(QualifiedType type)
            => SizeOf(type.Type);

        public int AlignOf(QualifiedType type)
            => AlignOf(type.Type);

        public int SizeOf(CType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            switch (type)
            {
                case BuiltinType builtin:
                    return GetPrimitiveLayout(builtin.BuiltinKind).Size;

                case PointerType:
                    return PointerSize;

                case ArrayType array when array.Length.HasValue:
                    return checked(SizeOf(array.ElementType) * (int)array.Length.Value);

                case ArrayType:
                    return 0;

                case EnumType:
                    return IntLayout.Size;

                case FunctionType:
                    return 0;

                case TagType tag:
                    return SizeOfTag(tag.Symbol);

                case CErrorType:
                    return 0;

                default:
                    return 0;
            }
        }

        public int AlignOf(CType type)
        {
            if (type is null)
                throw new ArgumentNullException(nameof(type));

            switch (type)
            {
                case BuiltinType builtin:
                    return GetPrimitiveLayout(builtin.BuiltinKind).Alignment;

                case PointerType:
                    return PointerAlignment;

                case ArrayType array:
                    return AlignOf(array.ElementType);

                case EnumType:
                    return IntLayout.Alignment;

                case FunctionType:
                    return 1;

                case TagType tag:
                    return AlignOfTag(tag.Symbol);

                case CErrorType:
                    return 1;

                default:
                    return 1;
            }
        }

        private int SizeOfTag(TagSymbol symbol)
        {
            if (symbol is null || !symbol.IsComplete)
                return 0;

            if (symbol.TagKind == TagKind.Union)
            {
                var unionSize = 0;
                var unionAlignment = 1;

                foreach (var field in symbol.Fields)
                {
                    unionSize = Math.Max(unionSize, SizeOf(field.Type));
                    unionAlignment = Math.Max(unionAlignment, AlignOf(field.Type));
                }

                return AlignTo(unionSize, unionAlignment);
            }

            var offset = 0;
            var structAlignment = 1;

            foreach (var field in symbol.Fields)
            {
                var fieldAlignment = AlignOf(field.Type);
                offset = AlignTo(offset, fieldAlignment);
                offset += SizeOf(field.Type);
                structAlignment = Math.Max(structAlignment, fieldAlignment);
            }

            return AlignTo(offset, structAlignment);
        }

        private int AlignOfTag(TagSymbol symbol)
        {
            if (symbol is null || !symbol.IsComplete)
                return 1;

            var alignment = 1;
            foreach (var field in symbol.Fields)
                alignment = Math.Max(alignment, AlignOf(field.Type));

            return alignment;
        }

        private static int AlignTo(int value, int alignment)
        {
            if (alignment <= 1)
                return value;

            var remainder = value % alignment;
            return remainder == 0 ? value : checked(value + alignment - remainder);
        }

        private PrimitiveLayout GetPrimitiveLayout(BuiltinTypeKind kind)
        {
            switch (kind)
            {
                case BuiltinTypeKind.Void:
                    return new PrimitiveLayout(1, 1);
                case BuiltinTypeKind.Bool:
                    return BoolLayout;
                case BuiltinTypeKind.Char:
                case BuiltinTypeKind.SignedChar:
                case BuiltinTypeKind.UnsignedChar:
                    return CharLayout;
                case BuiltinTypeKind.Short:
                case BuiltinTypeKind.UnsignedShort:
                    return ShortLayout;
                case BuiltinTypeKind.Int:
                case BuiltinTypeKind.UnsignedInt:
                    return IntLayout;
                case BuiltinTypeKind.Long:
                case BuiltinTypeKind.UnsignedLong:
                    return LongLayout;
                case BuiltinTypeKind.LongLong:
                case BuiltinTypeKind.UnsignedLongLong:
                    return LongLongLayout;
                case BuiltinTypeKind.Float:
                    return FloatLayout;
                case BuiltinTypeKind.Double:
                    return DoubleLayout;
                case BuiltinTypeKind.LongDouble:
                    return LongDoubleLayout;
                default:
                    return IntLayout;
            }
        }
    }

    [Flags]
    public enum TypeQualifiers : byte
    {
        None = 0,
        Const = 1,
        Volatile = 2,
        Restrict = 4,
        Atomic = 8
    }

    public enum TypeKind : byte
    {
        Error,
        Builtin,
        Pointer,
        Array,
        Function,
        Struct,
        Union,
        Enum
    }

    public enum BuiltinTypeKind : byte
    {
        Void,
        Bool,
        Char,
        SignedChar,
        UnsignedChar,
        Short,
        UnsignedShort,
        Int,
        UnsignedInt,
        Long,
        UnsignedLong,
        LongLong,
        UnsignedLongLong,
        Float,
        Double,
        LongDouble
    }

    public readonly struct QualifiedType
    {
        public CType Type { get; }
        public TypeQualifiers Qualifiers { get; }

        public QualifiedType(CType type, TypeQualifiers qualifiers = TypeQualifiers.None)
        {
            Type = type ?? CErrorType.Instance;
            Qualifiers = qualifiers;
        }

        public bool IsError => Type is CErrorType;

        public string ToDisplayString()
        {
            if (Qualifiers == TypeQualifiers.None)
                return Type.ToDisplayString();

            return Type.ToDisplayString() + " " + Qualifiers.ToString().ToLowerInvariant();
        }

        public override string ToString()
            => ToDisplayString();
    }

    public abstract class CType
    {
        public abstract TypeKind Kind { get; }
        public abstract string ToDisplayString();

        public override string ToString()
            => ToDisplayString();
    }

    public sealed class CErrorType : CType
    {
        public static CErrorType Instance { get; } = new CErrorType();

        private CErrorType()
        {
        }

        public override TypeKind Kind => TypeKind.Error;

        public override string ToDisplayString()
            => "<error-type>";
    }

    public sealed class BuiltinType : CType
    {
        public BuiltinTypeKind BuiltinKind { get; }

        public override TypeKind Kind => TypeKind.Builtin;

        public BuiltinType(BuiltinTypeKind kind)
        {
            BuiltinKind = kind;
        }

        public override string ToDisplayString()
        {
            switch (BuiltinKind)
            {
                case BuiltinTypeKind.Void:
                    return "void";
                case BuiltinTypeKind.Bool:
                    return "_Bool";
                case BuiltinTypeKind.Char:
                    return "char";
                case BuiltinTypeKind.SignedChar:
                    return "signed char";
                case BuiltinTypeKind.UnsignedChar:
                    return "unsigned char";
                case BuiltinTypeKind.Short:
                    return "short";
                case BuiltinTypeKind.UnsignedShort:
                    return "unsigned short";
                case BuiltinTypeKind.Int:
                    return "int";
                case BuiltinTypeKind.UnsignedInt:
                    return "unsigned int";
                case BuiltinTypeKind.Long:
                    return "long";
                case BuiltinTypeKind.UnsignedLong:
                    return "unsigned long";
                case BuiltinTypeKind.LongLong:
                    return "long long";
                case BuiltinTypeKind.UnsignedLongLong:
                    return "unsigned long long";
                case BuiltinTypeKind.Float:
                    return "float";
                case BuiltinTypeKind.Double:
                    return "double";
                case BuiltinTypeKind.LongDouble:
                    return "long double";
                default:
                    return "<builtin>";
            }
        }
    }

    public sealed class PointerType : CType
    {
        public QualifiedType PointeeType { get; }

        public PointerType(QualifiedType pointeeType)
        {
            PointeeType = pointeeType;
        }

        public override TypeKind Kind => TypeKind.Pointer;

        public override string ToDisplayString()
            => PointeeType.ToDisplayString() + "*";
    }

    public sealed class ArrayType : CType
    {
        public QualifiedType ElementType { get; }
        public long? Length { get; }

        public ArrayType(QualifiedType elementType, long? length)
        {
            ElementType = elementType;
            Length = length;
        }

        public override TypeKind Kind => TypeKind.Array;

        public override string ToDisplayString()
            => ElementType.ToDisplayString() + "[" + (Length.HasValue ? Length.Value.ToString() : string.Empty) + "]";
    }

    public sealed class FunctionType : CType
    {
        public QualifiedType ReturnType { get; }
        public ImmutableArray<ParameterSymbol> Parameters { get; }
        public bool HasPrototype { get; }
        public bool IsVariadic { get; }

        public FunctionType(
            QualifiedType returnType,
            ImmutableArray<ParameterSymbol> parameters,
            bool hasPrototype,
            bool isVariadic)
        {
            ReturnType = returnType;
            Parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
            HasPrototype = hasPrototype;
            IsVariadic = isVariadic;
        }

        public override TypeKind Kind => TypeKind.Function;

        public override string ToDisplayString()
        {
            var parameters = HasPrototype
                ? string.Join(", ", Parameters.Select(static p => p.Type.ToDisplayString()))
                : string.Empty;

            if (IsVariadic)
                parameters = parameters.Length == 0 ? "..." : parameters + ", ...";

            return ReturnType.ToDisplayString() + " (" + parameters + ")";
        }
    }

    public enum TagKind : byte { Struct, Union, Enum }

    public sealed class TagType : CType
    {
        public TagSymbol Symbol { get; }

        public TagType(TagSymbol symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public override TypeKind Kind
        {
            get
            {
                switch (Symbol.TagKind)
                {
                    case TagKind.Struct:
                        return TypeKind.Struct;
                    case TagKind.Union:
                        return TypeKind.Union;
                    default:
                        return TypeKind.Enum;
                }
            }
        }

        public override string ToDisplayString()
            => Symbol.TagKind.ToString().ToLowerInvariant() + " " + Symbol.Name;
    }

    public sealed class EnumType : CType
    {
        public TagSymbol Symbol { get; }

        public EnumType(TagSymbol symbol)
        {
            Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        }

        public override TypeKind Kind => TypeKind.Enum;

        public override string ToDisplayString()
            => "enum " + Symbol.Name;
    }

    public sealed class TypeCatalog
    {
        public static TypeCatalog Instance { get; } = new TypeCatalog();

        public BuiltinType Void { get; } = new BuiltinType(BuiltinTypeKind.Void);
        public BuiltinType Bool { get; } = new BuiltinType(BuiltinTypeKind.Bool);
        public BuiltinType Char { get; } = new BuiltinType(BuiltinTypeKind.Char);
        public BuiltinType SignedChar { get; } = new BuiltinType(BuiltinTypeKind.SignedChar);
        public BuiltinType UnsignedChar { get; } = new BuiltinType(BuiltinTypeKind.UnsignedChar);
        public BuiltinType Short { get; } = new BuiltinType(BuiltinTypeKind.Short);
        public BuiltinType UnsignedShort { get; } = new BuiltinType(BuiltinTypeKind.UnsignedShort);
        public BuiltinType Int { get; } = new BuiltinType(BuiltinTypeKind.Int);
        public BuiltinType UnsignedInt { get; } = new BuiltinType(BuiltinTypeKind.UnsignedInt);
        public BuiltinType Long { get; } = new BuiltinType(BuiltinTypeKind.Long);
        public BuiltinType UnsignedLong { get; } = new BuiltinType(BuiltinTypeKind.UnsignedLong);
        public BuiltinType LongLong { get; } = new BuiltinType(BuiltinTypeKind.LongLong);
        public BuiltinType UnsignedLongLong { get; } = new BuiltinType(BuiltinTypeKind.UnsignedLongLong);
        public BuiltinType Float { get; } = new BuiltinType(BuiltinTypeKind.Float);
        public BuiltinType Double { get; } = new BuiltinType(BuiltinTypeKind.Double);
        public BuiltinType LongDouble { get; } = new BuiltinType(BuiltinTypeKind.LongDouble);

        private TypeCatalog() { }

        public PointerType PointerTo(QualifiedType pointee)
            => new PointerType(pointee);

        public ArrayType ArrayOf(QualifiedType elementType, long? length)
            => new ArrayType(elementType, length);

        public FunctionType FunctionReturning(
            QualifiedType returnType,
            ImmutableArray<ParameterSymbol> parameters,
            bool hasPrototype,
            bool isVariadic)
        {
            return new FunctionType(returnType, parameters, hasPrototype, isVariadic);
        }

        public QualifiedType Builtin(BuiltinTypeKind kind, TypeQualifiers qualifiers = TypeQualifiers.None)
        {
            switch (kind)
            {
                case BuiltinTypeKind.Void:
                    return new QualifiedType(Void, qualifiers);
                case BuiltinTypeKind.Bool:
                    return new QualifiedType(Bool, qualifiers);
                case BuiltinTypeKind.Char:
                    return new QualifiedType(Char, qualifiers);
                case BuiltinTypeKind.SignedChar:
                    return new QualifiedType(SignedChar, qualifiers);
                case BuiltinTypeKind.UnsignedChar:
                    return new QualifiedType(UnsignedChar, qualifiers);
                case BuiltinTypeKind.Short:
                    return new QualifiedType(Short, qualifiers);
                case BuiltinTypeKind.UnsignedShort:
                    return new QualifiedType(UnsignedShort, qualifiers);
                case BuiltinTypeKind.Int:
                    return new QualifiedType(Int, qualifiers);
                case BuiltinTypeKind.UnsignedInt:
                    return new QualifiedType(UnsignedInt, qualifiers);
                case BuiltinTypeKind.Long:
                    return new QualifiedType(Long, qualifiers);
                case BuiltinTypeKind.UnsignedLong:
                    return new QualifiedType(UnsignedLong, qualifiers);
                case BuiltinTypeKind.LongLong:
                    return new QualifiedType(LongLong, qualifiers);
                case BuiltinTypeKind.UnsignedLongLong:
                    return new QualifiedType(UnsignedLongLong, qualifiers);
                case BuiltinTypeKind.Float:
                    return new QualifiedType(Float, qualifiers);
                case BuiltinTypeKind.Double:
                    return new QualifiedType(Double, qualifiers);
                case BuiltinTypeKind.LongDouble:
                    return new QualifiedType(LongDouble, qualifiers);
                default:
                    return new QualifiedType(CErrorType.Instance, qualifiers);
            }
        }
    }


    public sealed class Scope
    {
        private readonly Dictionary<string, Symbol> _ordinarySymbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TagSymbol> _tagSymbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LabelSymbol> _labelSymbols = new(StringComparer.Ordinal);

        public Scope? Parent { get; }
        public SyntaxNode? DeclaringSyntax { get; }

        public Scope(Scope? parent, SyntaxNode? declaringSyntax)
        {
            Parent = parent;
            DeclaringSyntax = declaringSyntax;
        }

        public IEnumerable<Symbol> OrdinarySymbols => _ordinarySymbols.Values;
        public IEnumerable<TagSymbol> Tags => _tagSymbols.Values;
        public IEnumerable<LabelSymbol> Labels => _labelSymbols.Values;

        public bool TryDeclareOrdinary(Symbol symbol, out Symbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_ordinarySymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _ordinarySymbols.Add(symbol.Name, symbol);
            return true;
        }

        public void ReplaceOrdinary(Symbol symbol)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            _ordinarySymbols[symbol.Name] = symbol;
        }

        public bool TryDeclareTag(TagSymbol symbol, out TagSymbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_tagSymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _tagSymbols.Add(symbol.Name, symbol);
            return true;
        }

        public bool TryDeclareLabel(LabelSymbol symbol, out LabelSymbol? existing)
        {
            if (symbol is null)
                throw new ArgumentNullException(nameof(symbol));

            if (_labelSymbols.TryGetValue(symbol.Name, out existing))
                return false;

            _labelSymbols.Add(symbol.Name, symbol);
            return true;
        }

        public Symbol? LookupOrdinary(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._ordinarySymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }

        public TagSymbol? LookupTag(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._tagSymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }

        public LabelSymbol? LookupLabel(string name)
        {
            for (var scope = this; scope is not null; scope = scope.Parent)
            {
                if (scope._labelSymbols.TryGetValue(name, out var symbol))
                    return symbol;
            }

            return null;
        }
    }



}
