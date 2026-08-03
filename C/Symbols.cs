using System;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    /// <summary>Identifies the semantic category of a symbol</summary>
    public enum SymbolKind : byte
    {
        Error,
        Variable,
        Function,
        Parameter,
        Field,
        TypeAlias,
        Tag,
        Label,
        EnumConstant
    }

    /// <summary>Identifies functions handled by dedicated runtime lowering</summary>
    public enum RuntimeIntrinsicKind : byte
    {
        None,
        BuiltinVaStart,
        BuiltinVaArg,
        CStringWrite,
        Malloc,
        Free,
    }

    /// <summary>Base class for named semantic entities</summary>
    public abstract class Symbol
    {
        public abstract SymbolKind Kind { get; }
        public abstract string Name { get; }

        public override string ToString()
            => Name;
    }

    /// <summary>Base class for symbols that carry a declared type and source origin</summary>
    public abstract class TypedSymbol : Symbol
    {
        public QualifiedType Type { get; }
        /// <summary>Syntax that introduced the symbol or null for synthesized symbols</summary>
        public SyntaxNode? DeclaringSyntax { get; }

        protected TypedSymbol(QualifiedType type, SyntaxNode? declaringSyntax)
        {
            Type = type;
            DeclaringSyntax = declaringSyntax;
        }
    }

    /// <summary>Sentinel used when symbol resolution fails</summary>
    public sealed class ErrorSymbol : Symbol
    {
        public static ErrorSymbol Instance { get; } = new ErrorSymbol();

        private ErrorSymbol()
        {
        }

        public override SymbolKind Kind => SymbolKind.Error;
        public override string Name => "<error-symbol>";
    }

    /// <summary>Represents an object declaration</summary>
    public sealed class VariableSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Variable;
        public override string Name { get; }
        public StorageClass StorageClass { get; }
        /// <summary>Requested physical register name or null when none was specified</summary>
        public string? ExplicitRegisterName { get; }

        public VariableSymbol(
            string name,
            QualifiedType type,
            StorageClass storageClass,
            SyntaxNode? declaringSyntax,
            string? explicitRegisterName = null)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
            StorageClass = storageClass;
            ExplicitRegisterName = string.IsNullOrWhiteSpace(explicitRegisterName) ? null : explicitRegisterName.Trim();
        }
    }

    /// <summary>Represents a function declaration or definition</summary>
    public sealed class FunctionSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Function;
        public override string Name { get; }
        public StorageClass StorageClass { get; }
        public FunctionSpecifiers FunctionSpecifiers { get; }
        /// <summary>Whether the symbol originates from a function definition</summary>
        public bool IsDefinition { get; }
        /// <summary>Dedicated lowering assigned to this function</summary>
        public RuntimeIntrinsicKind IntrinsicKind { get; }
        /// <summary>Whether calls bypass normal function lowering</summary>
        public bool IsIntrinsic => IntrinsicKind != RuntimeIntrinsicKind.None;

        /// <summary>Function type view or null when recovery produced another type</summary>
        public FunctionType? FunctionType => Type.Type as FunctionType;

        public FunctionSymbol(
            string name,
            QualifiedType type,
            StorageClass storageClass,
            FunctionSpecifiers functionSpecifiers,
            bool isDefinition,
            SyntaxNode? declaringSyntax,
            RuntimeIntrinsicKind intrinsicKind = RuntimeIntrinsicKind.None)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
            StorageClass = storageClass;
            FunctionSpecifiers = functionSpecifiers;
            IsDefinition = isDefinition;
            IntrinsicKind = intrinsicKind;
        }
    }

    /// <summary>Represents a function parameter</summary>
    public sealed class ParameterSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Parameter;
        public override string Name { get; }

        public ParameterSymbol(string name, QualifiedType type, SyntaxNode? declaringSyntax)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>Represents a named integer constant introduced by an enum definition</summary>
    public sealed class EnumConstantSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.EnumConstant;
        public override string Name { get; }

        public long Value { get; }

        public EnumConstantSymbol(
            string name,
            QualifiedType type,
            long value,
            SyntaxNode? declaringSyntax)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
            Value = value;
        }
    }

    /// <summary>Represents a named alias for another type</summary>
    public sealed class TypeAliasSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.TypeAlias;
        public override string Name { get; }

        /// <summary>Type denoted by the alias</summary>
        public QualifiedType TargetType => Type;

        public TypeAliasSymbol(string name, QualifiedType targetType, SyntaxNode? declaringSyntax)
            : base(targetType, declaringSyntax)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>Represents a field declared by a tag type</summary>
    public sealed class FieldSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Field;
        public override string Name { get; }

        public TagSymbol ContainingTag { get; }
        /// <summary>Zero-based declaration order within the containing tag</summary>
        public int Ordinal { get; }

        public FieldSymbol(
            string name,
            QualifiedType type,
            TagSymbol containingTag,
            int ordinal,
            SyntaxNode? declaringSyntax)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
            ContainingTag = containingTag ?? throw new ArgumentNullException(nameof(containingTag));
            Ordinal = ordinal < 0 ? 0 : ordinal;
        }
    }

    /// <summary>Represents a structure union or enumeration tag</summary>
    public sealed class TagSymbol : Symbol
    {
        private ImmutableArray<FieldSymbol> _fields;

        public override SymbolKind Kind => SymbolKind.Tag;
        public override string Name { get; }

        public TagKind TagKind { get; }
        /// <summary>Syntax that introduced the symbol or null for synthesized symbols</summary>
        public SyntaxNode? DeclaringSyntax { get; }
        /// <summary>Whether the tag body has been defined</summary>
        public bool IsComplete { get; private set; }

        /// <summary>Fields in declaration order or an empty array when none are available</summary>
        public ImmutableArray<FieldSymbol> Fields
            => _fields.IsDefault ? ImmutableArray<FieldSymbol>.Empty : _fields;

        public TagSymbol(string name, TagKind tagKind, SyntaxNode? declaringSyntax)
        {
            Name = string.IsNullOrEmpty(name) ? "<anonymous>" : name;
            TagKind = tagKind;
            DeclaringSyntax = declaringSyntax;
        }

        /// <summary>Completes the tag exactly once</summary>
        public bool TryDefineFields(ImmutableArray<FieldSymbol> fields)
        {
            if (IsComplete)
                return false;

            _fields = fields.IsDefault
                ? ImmutableArray<FieldSymbol>.Empty
                : fields;
            IsComplete = true;
            return true;
        }

        /// <summary>Finds a directly declared field by name</summary>
        public bool TryGetField(string name, out FieldSymbol? field)
        {
            foreach (var candidate in Fields)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    field = candidate;
                    return true;
                }
            }

            field = null;
            return false;
        }
    }

    /// <summary>Represents a function-scoped statement label</summary>
    public sealed class LabelSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Label;
        public override string Name { get; }

        /// <summary>Syntax that introduced the symbol or null for synthesized symbols</summary>
        public SyntaxNode? DeclaringSyntax { get; }

        public LabelSymbol(string name, SyntaxNode? declaringSyntax)
        {
            Name = name ?? string.Empty;
            DeclaringSyntax = declaringSyntax;
        }
    }
}
