using System;
using System.Collections.Immutable;

namespace Cnidaria.C
{
    public enum SymbolKind : byte
    {
        Error,
        Variable,
        Function,
        Parameter,
        Field,
        TypeAlias,
        Tag,
        Label
    }

    public enum RuntimeIntrinsicKind : byte
    {
        None,
        BuiltinVaStart,
        CStringWrite,
        Malloc,
        Free,
    }

    public abstract class Symbol
    {
        public abstract SymbolKind Kind { get; }
        public abstract string Name { get; }

        public override string ToString()
            => Name;
    }

    public abstract class TypedSymbol : Symbol
    {
        public QualifiedType Type { get; }
        public SyntaxNode? DeclaringSyntax { get; }

        protected TypedSymbol(QualifiedType type, SyntaxNode? declaringSyntax)
        {
            Type = type;
            DeclaringSyntax = declaringSyntax;
        }
    }

    public sealed class ErrorSymbol : Symbol
    {
        public static ErrorSymbol Instance { get; } = new ErrorSymbol();

        private ErrorSymbol()
        {
        }

        public override SymbolKind Kind => SymbolKind.Error;
        public override string Name => "<error-symbol>";
    }

    public sealed class VariableSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Variable;
        public override string Name { get; }
        public StorageClass StorageClass { get; }

        public VariableSymbol(
            string name,
            QualifiedType type,
            StorageClass storageClass,
            SyntaxNode? declaringSyntax)
            : base(type, declaringSyntax)
        {
            Name = name ?? string.Empty;
            StorageClass = storageClass;
        }
    }

    public sealed class FunctionSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Function;
        public override string Name { get; }
        public StorageClass StorageClass { get; }
        public FunctionSpecifiers FunctionSpecifiers { get; }
        public bool IsDefinition { get; }
        public RuntimeIntrinsicKind IntrinsicKind { get; }
        public bool IsIntrinsic => IntrinsicKind != RuntimeIntrinsicKind.None;

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

    public sealed class TypeAliasSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.TypeAlias;
        public override string Name { get; }

        public QualifiedType TargetType => Type;

        public TypeAliasSymbol(string name, QualifiedType targetType, SyntaxNode? declaringSyntax)
            : base(targetType, declaringSyntax)
        {
            Name = name ?? string.Empty;
        }
    }

    public sealed class FieldSymbol : TypedSymbol
    {
        public override SymbolKind Kind => SymbolKind.Field;
        public override string Name { get; }

        public TagSymbol ContainingTag { get; }
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

    public sealed class TagSymbol : Symbol
    {
        private ImmutableArray<FieldSymbol> _fields;

        public override SymbolKind Kind => SymbolKind.Tag;
        public override string Name { get; }

        public TagKind TagKind { get; }
        public SyntaxNode? DeclaringSyntax { get; }
        public bool IsComplete { get; private set; }

        public ImmutableArray<FieldSymbol> Fields
            => _fields.IsDefault ? ImmutableArray<FieldSymbol>.Empty : _fields;

        public TagSymbol(string name, TagKind tagKind, SyntaxNode? declaringSyntax)
        {
            Name = string.IsNullOrEmpty(name) ? "<anonymous>" : name;
            TagKind = tagKind;
            DeclaringSyntax = declaringSyntax;
        }

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

    public sealed class LabelSymbol : Symbol
    {
        public override SymbolKind Kind => SymbolKind.Label;
        public override string Name { get; }

        public SyntaxNode? DeclaringSyntax { get; }

        public LabelSymbol(string name, SyntaxNode? declaringSyntax)
        {
            Name = name ?? string.Empty;
            DeclaringSyntax = declaringSyntax;
        }
    }
}
