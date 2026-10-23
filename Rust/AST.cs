using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Rust;

///<summary>Stores one child node or token in source order</summary>
public readonly struct SyntaxNodeOrToken
{
    private readonly SyntaxNode? _node;
    private readonly SyntaxToken _token;
    public bool IsToken { get; }
    public bool IsNode => _node is not null;
    public SyntaxNode Node => _node ?? throw new InvalidOperationException("Not a node.");
    public SyntaxToken Token => IsToken ? _token : throw new InvalidOperationException("Not a token.");
    public TextSpan Span => IsToken ? _token.Span : Node.Span;
    public TextSpan FullSpan => IsToken ? _token.FullSpan : Node.FullSpan;
    public SyntaxNodeOrToken(SyntaxNode node) { _node = node ?? throw new ArgumentNullException(nameof(node)); _token = default; IsToken = false; }
    public SyntaxNodeOrToken(SyntaxToken token) { _node = null; _token = token; IsToken = true; }
    public static implicit operator SyntaxNodeOrToken(SyntaxNode node) => new(node);
    public static implicit operator SyntaxNodeOrToken(SyntaxToken token) => new(token);
}

///<summary>Base class for source-backed syntax</summary>
public abstract class SyntaxNode
{
    public SyntaxKind Kind { get; }
    public ImmutableArray<SyntaxNodeOrToken> Children { get; }
    public TextSpan Span { get; }
    public TextSpan FullSpan { get; }
    protected SyntaxNode(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children)
    {
        Kind = kind;
        Children = children.IsDefault ? ImmutableArray<SyntaxNodeOrToken>.Empty : children;
        if (!Children.IsEmpty)
        {
            Span = new TextSpan(Children[0].Span.Start, Math.Max(0, Children[^1].Span.End - Children[0].Span.Start));
            FullSpan = new TextSpan(Children[0].FullSpan.Start, Math.Max(0, Children[^1].FullSpan.End - Children[0].FullSpan.Start));
        }
    }
    public IEnumerable<SyntaxNode> ChildNodes() => Children.Where(c => c.IsNode).Select(c => c.Node);
    public IEnumerable<SyntaxToken> ChildTokens() => Children.Where(c => c.IsToken).Select(c => c.Token);
    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        var pending = new Stack<SyntaxNode>(ChildNodes().Reverse());
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            yield return node;
            foreach (var child in node.ChildNodes().Reverse()) pending.Push(child);
        }
    }
    public IEnumerable<SyntaxToken> DescendantTokens()
    {
        var pending = new Stack<SyntaxNodeOrToken>(Children.Reverse());
        while (pending.Count != 0)
        {
            var entry = pending.Pop();
            if (entry.IsToken) yield return entry.Token;
            else foreach (var child in entry.Node.Children.Reverse()) pending.Push(child);
        }
    }
    public string GetText(string source) => source.Substring(Span.Start, Span.Length);
    public string GetFullText(string source) => source.Substring(FullSpan.Start, FullSpan.Length);
    protected T? Child<T>(int index = 0) where T : SyntaxNode => ChildNodes().OfType<T>().Skip(index).FirstOrDefault();
    protected IEnumerable<T> Nodes<T>() where T : SyntaxNode => ChildNodes().OfType<T>();
    protected SyntaxToken Token(params SyntaxKind[] kinds) => ChildTokens().FirstOrDefault(t => kinds.Contains(t.Kind));
    public override string ToString() => $"{Kind} {Span}";
}

public abstract class ItemSyntax(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) : SyntaxNode(kind, children)
{
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
    public VisibilitySyntax? Visibility => Child<VisibilitySyntax>();
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.UnderscoreKeyword, SyntaxKind.SelfValueKeyword);
    public GenericParameterListSyntax? GenericParameters => Child<GenericParameterListSyntax>();
    public WhereClauseSyntax? WhereClause => Child<WhereClauseSyntax>();
}
public abstract class ExpressionSyntax(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) : SyntaxNode(kind, children);
public abstract class TypeSyntax(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) : SyntaxNode(kind, children);
public abstract class PatternSyntax(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) : SyntaxNode(kind, children);
public abstract class StatementSyntax(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) : SyntaxNode(kind, children);

///<summary>Stores source text, syntax and diagnostics</summary>
public sealed class SyntaxTree
{
    public string Text { get; }
    public string FilePath { get; }
    public RustEdition Edition { get; }
    public CompilationUnitSyntax Root { get; }
    public ImmutableArray<SyntaxDiagnostic> Diagnostics { get; }
    internal SyntaxTree(string text, string filePath, RustEdition edition, CompilationUnitSyntax root, ImmutableArray<SyntaxDiagnostic> diagnostics)
    { Text = text; FilePath = filePath; Edition = edition; Root = root; Diagnostics = diagnostics; }
    public static SyntaxTree ParseText(string text, ParserOptions? options = null, string filePath = "")
    {
        var parser = new Parser(text, options);
        var root = parser.Parse();
        return new SyntaxTree(text, filePath, parser.Edition, root, parser.LexerDiagnostics.Concat(parser.Diagnostics).ToImmutableArray());
    }
}

// Compilation units, attributes and token trees
public sealed class CompilationUnitSyntax : SyntaxNode
{
    internal CompilationUnitSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.CompilationUnit, children) { }
    public IEnumerable<ItemSyntax> Items => Nodes<ItemSyntax>();
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
    public SyntaxToken EndOfFileToken => Token(SyntaxKind.EndOfFileToken);
}

public sealed class SkippedSyntax : SyntaxNode
{
    internal SkippedSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.SkippedSyntax, children) { }
}

public sealed class TokenTreeSyntax : SyntaxNode
{
    internal TokenTreeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TokenTree, children) { }
    public SyntaxToken OpenToken => ChildTokens().First();
    public SyntaxToken CloseToken => ChildTokens().Last();
}

public sealed class AttributeSyntax : SyntaxNode
{
    internal AttributeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.Attribute, children) { }
    public bool IsInner => ChildTokens().Any(t => t.Kind == SyntaxKind.BangToken);
    public bool IsUnsafe => ChildTokens().Any(t => t.Kind == SyntaxKind.UnsafeKeyword);
    public PathSyntax? Path => Child<PathSyntax>();
    public TokenTreeSyntax? Input => Child<TokenTreeSyntax>();
    public EqualsValueClauseSyntax? Value => Child<EqualsValueClauseSyntax>();
}

public sealed class VisibilitySyntax : SyntaxNode
{
    internal VisibilitySyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.Visibility, children) { }
    public PathSyntax? Restriction => Child<PathSyntax>();
}

// Paths and generic constraints
public sealed class PathSyntax : SyntaxNode
{
    internal PathSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.Path, children) { }
    public IEnumerable<PathSegmentSyntax> Segments => Nodes<PathSegmentSyntax>();
    public QualifiedPathSyntax? Qualification => Child<QualifiedPathSyntax>();
}

public sealed class QualifiedPathSyntax : SyntaxNode
{
    internal QualifiedPathSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.QualifiedPath, children) { }
    public TypeSyntax? Type => Child<TypeSyntax>();
    public PathSyntax? Trait => Child<PathSyntax>();
}

public sealed class PathSegmentSyntax : SyntaxNode
{
    internal PathSegmentSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.PathSegment, children) { }
    public SyntaxToken Identifier => ChildTokens().FirstOrDefault();
    public GenericArgumentListSyntax? Arguments => Child<GenericArgumentListSyntax>();
    public ParameterTypeListSyntax? FunctionParameters => Child<ParameterTypeListSyntax>();
    public ReturnTypeSyntax? ReturnType => Child<ReturnTypeSyntax>();
}

public sealed class GenericParameterListSyntax : SyntaxNode
{
    internal GenericParameterListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.GenericParameterList, children) { }
    public IEnumerable<GenericParameterSyntax> Parameters => Nodes<GenericParameterSyntax>();
}

public sealed class GenericParameterSyntax : SyntaxNode
{
    internal GenericParameterSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.GenericParameter, children) { }
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.LifetimeToken, SyntaxKind.RawLifetimeToken);
    public bool IsConst => ChildTokens().Any(t => t.Kind == SyntaxKind.ConstKeyword);
    public TypeAnnotationSyntax? TypeAnnotation => Child<TypeAnnotationSyntax>();
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
    public EqualsValueClauseSyntax? Default => Child<EqualsValueClauseSyntax>();
}

public sealed class GenericArgumentListSyntax : SyntaxNode
{
    internal GenericArgumentListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.GenericArgumentList, children) { }
    public IEnumerable<GenericArgumentSyntax> Arguments => Nodes<GenericArgumentSyntax>();
}

public sealed class GenericArgumentSyntax : SyntaxNode
{
    internal GenericArgumentSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.GenericArgument, children) { }
    public SyntaxNode? Value => ChildNodes().FirstOrDefault();
    public EqualsValueClauseSyntax? Binding => Child<EqualsValueClauseSyntax>();
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
}

public sealed class LifetimeBinderSyntax : SyntaxNode
{
    internal LifetimeBinderSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LifetimeBinder, children) { }
    public GenericParameterListSyntax? Parameters => Child<GenericParameterListSyntax>();
}

public sealed class WhereClauseSyntax : SyntaxNode
{
    internal WhereClauseSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.WhereClause, children) { }
    public IEnumerable<WherePredicateSyntax> Predicates => Nodes<WherePredicateSyntax>();
}

public sealed class WherePredicateSyntax : SyntaxNode
{
    internal WherePredicateSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.WherePredicate, children) { }
    public LifetimeBinderSyntax? Binder => Child<LifetimeBinderSyntax>();
    public TypeSyntax? Type => Child<TypeSyntax>();
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
}

public sealed class TypeBoundListSyntax : SyntaxNode
{
    internal TypeBoundListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TypeBoundList, children) { }
    public IEnumerable<TypeBoundSyntax> Bounds => Nodes<TypeBoundSyntax>();
}

public sealed class TypeBoundSyntax : SyntaxNode
{
    internal TypeBoundSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TypeBound, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public LifetimeBinderSyntax? Binder => Child<LifetimeBinderSyntax>();
    public TypeSyntax? Type => Child<TypeSyntax>();
    public TypeBoundSyntax? Inner => Child<TypeBoundSyntax>();
    public SyntaxToken Lifetime => Token(SyntaxKind.LifetimeToken, SyntaxKind.RawLifetimeToken);
}

public sealed class TypeAnnotationSyntax : SyntaxNode
{
    internal TypeAnnotationSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TypeAnnotation, children) { }
    public TypeSyntax? Type => Child<TypeSyntax>();
}

public sealed class ReturnTypeSyntax : SyntaxNode
{
    internal ReturnTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ReturnType, children) { }
    public TypeSyntax? Type => Child<TypeSyntax>();
}

public sealed class EqualsValueClauseSyntax : SyntaxNode
{
    internal EqualsValueClauseSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.EqualsValueClause, children) { }
    public SyntaxNode? Value => ChildNodes().FirstOrDefault();
}

public sealed class ParameterListSyntax : SyntaxNode
{
    internal ParameterListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ParameterList, children) { }
    public IEnumerable<ParameterSyntax> Parameters => Nodes<ParameterSyntax>();
}

public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.Parameter, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
    public TypeSyntax? Type => Child<TypeAnnotationSyntax>()?.Type ?? Child<TypeSyntax>();
    public bool IsSelf => ChildTokens().Any(t => t.Kind == SyntaxKind.SelfValueKeyword);
    public bool IsVariadic => ChildTokens().Any(t => t.Kind == SyntaxKind.DotDotDotToken);
}

public sealed class ParameterTypeListSyntax : SyntaxNode
{
    internal ParameterTypeListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ParameterTypeList, children) { }
    public IEnumerable<ParameterSyntax> Parameters => Nodes<ParameterSyntax>();
    public IEnumerable<TypeSyntax> Types => Parameters.Select(p => p.Type).OfType<TypeSyntax>();
}

public sealed class ItemListSyntax : SyntaxNode
{
    internal ItemListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ItemList, children) { }
    public IEnumerable<ItemSyntax> Items => Nodes<ItemSyntax>();
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
}

public sealed class FieldListSyntax : SyntaxNode
{
    internal FieldListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.FieldList, children) { }
    public IEnumerable<FieldSyntax> Fields => Nodes<FieldSyntax>();
}

public sealed class FieldSyntax : SyntaxNode
{
    internal FieldSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.Field, children) { }
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken);
    public TypeSyntax? Type => Child<TypeAnnotationSyntax>()?.Type ?? Child<TypeSyntax>();
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
    public VisibilitySyntax? Visibility => Child<VisibilitySyntax>();
}

public sealed class EnumVariantSyntax : SyntaxNode
{
    internal EnumVariantSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.EnumVariant, children) { }
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken);
    public FieldListSyntax? Fields => Child<FieldListSyntax>();
    public EqualsValueClauseSyntax? Discriminant => Child<EqualsValueClauseSyntax>();
}

public sealed class UseTreeSyntax : SyntaxNode
{
    internal UseTreeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.UseTree, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public IEnumerable<UseTreeSyntax> ChildrenTrees => Nodes<UseTreeSyntax>();
    public SyntaxToken Alias => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.UnderscoreKeyword);
    public bool IsGlob => ChildTokens().Any(t => t.Kind == SyntaxKind.StarToken);
}

// Items
public sealed class ModuleItemSyntax : ItemSyntax
{
    internal ModuleItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ModuleItem, children) { }
    public ItemListSyntax? Body => Child<ItemListSyntax>();
}

public sealed class UseItemSyntax : ItemSyntax
{
    internal UseItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.UseItem, children) { }
    public UseTreeSyntax? Tree => Child<UseTreeSyntax>();
}

public sealed class ExternCrateItemSyntax : ItemSyntax
{
    internal ExternCrateItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ExternCrateItem, children) { }
    public SyntaxToken Alias => ChildTokens().SkipWhile(t => t.Kind != SyntaxKind.AsKeyword).Skip(1).FirstOrDefault();
}

public sealed class FunctionItemSyntax : ItemSyntax
{
    internal FunctionItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.FunctionItem, children) { }
    public ParameterListSyntax? Parameters => Child<ParameterListSyntax>();
    public ReturnTypeSyntax? ReturnType => Child<ReturnTypeSyntax>();
    public BlockExpressionSyntax? Body => Child<BlockExpressionSyntax>();
}

public sealed class StructItemSyntax : ItemSyntax
{
    internal StructItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StructItem, children) { }
    public FieldListSyntax? Fields => Child<FieldListSyntax>();
}

public sealed class UnionItemSyntax : ItemSyntax
{
    internal UnionItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.UnionItem, children) { }
    public FieldListSyntax? Fields => Child<FieldListSyntax>();
}

public sealed class EnumItemSyntax : ItemSyntax
{
    internal EnumItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.EnumItem, children) { }
    public IEnumerable<EnumVariantSyntax> Variants => Nodes<EnumVariantSyntax>();
}

public sealed class TraitItemSyntax : ItemSyntax
{
    internal TraitItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TraitItem, children) { }
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
    public ItemListSyntax? Body => Child<ItemListSyntax>();
}

public sealed class ImplItemSyntax : ItemSyntax
{
    internal ImplItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ImplItem, children) { }
    public TypeSyntax? SelfType => Nodes<TypeSyntax>().LastOrDefault();
    public TypeSyntax? Trait => ChildTokens().Any(t => t.Kind == SyntaxKind.ForKeyword) ? Child<TypeSyntax>() : null;
    public ItemListSyntax? Body => Child<ItemListSyntax>();
}

public sealed class ExternBlockItemSyntax : ItemSyntax
{
    internal ExternBlockItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ExternBlockItem, children) { }
    public ItemListSyntax? Body => Child<ItemListSyntax>();
}

public sealed class TypeAliasItemSyntax : ItemSyntax
{
    internal TypeAliasItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TypeAliasItem, children) { }
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
    public EqualsValueClauseSyntax? Value => Child<EqualsValueClauseSyntax>();
}

public sealed class ConstItemSyntax : ItemSyntax
{
    internal ConstItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ConstItem, children) { }
    public TypeSyntax? Type => Child<TypeAnnotationSyntax>()?.Type;
    public EqualsValueClauseSyntax? Value => Child<EqualsValueClauseSyntax>();
}

public sealed class StaticItemSyntax : ItemSyntax
{
    internal StaticItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StaticItem, children) { }
    public TypeSyntax? Type => Child<TypeAnnotationSyntax>()?.Type;
    public EqualsValueClauseSyntax? Value => Child<EqualsValueClauseSyntax>();
}

public sealed class MacroDefinitionItemSyntax : ItemSyntax
{
    internal MacroDefinitionItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MacroDefinitionItem, children) { }
    public TokenTreeSyntax? Body => Child<TokenTreeSyntax>();
}

public sealed class MacroInvocationItemSyntax : ItemSyntax
{
    internal MacroInvocationItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MacroInvocationItem, children) { }
    public MacroExpressionSyntax? Invocation => Child<MacroExpressionSyntax>();
}

public sealed class MissingItemSyntax : ItemSyntax
{
    internal MissingItemSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MissingItem, children) { }
}

// Types
public sealed class PathTypeSyntax : TypeSyntax
{
    internal PathTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.PathType, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
}

public sealed class ReferenceTypeSyntax : TypeSyntax
{
    internal ReferenceTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ReferenceType, children) { }
    public TypeSyntax? ElementType => Child<TypeSyntax>();
    public SyntaxToken Lifetime => Token(SyntaxKind.LifetimeToken, SyntaxKind.RawLifetimeToken);
    public bool IsMutable => ChildTokens().Any(t => t.Kind == SyntaxKind.MutKeyword);
}

public sealed class PointerTypeSyntax : TypeSyntax
{
    internal PointerTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.PointerType, children) { }
    public TypeSyntax? ElementType => Child<TypeSyntax>();
    public bool IsMutable => ChildTokens().Any(t => t.Kind == SyntaxKind.MutKeyword);
}

public sealed class TupleTypeSyntax : TypeSyntax
{
    internal TupleTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TupleType, children) { }
    public IEnumerable<TypeSyntax> Elements => Nodes<TypeSyntax>();
}

public sealed class ParenthesizedTypeSyntax : TypeSyntax
{
    internal ParenthesizedTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ParenthesizedType, children) { }
    public TypeSyntax? Type => Child<TypeSyntax>();
}

public sealed class SliceTypeSyntax : TypeSyntax
{
    internal SliceTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.SliceType, children) { }
    public TypeSyntax? ElementType => Child<TypeSyntax>();
}

public sealed class ArrayTypeSyntax : TypeSyntax
{
    internal ArrayTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ArrayType, children) { }
    public TypeSyntax? ElementType => Child<TypeSyntax>();
    public ExpressionSyntax? Length => Child<ExpressionSyntax>();
}

public sealed class FunctionPointerTypeSyntax : TypeSyntax
{
    internal FunctionPointerTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.FunctionPointerType, children) { }
    public LifetimeBinderSyntax? Binder => Child<LifetimeBinderSyntax>();
    public ParameterTypeListSyntax? Parameters => Child<ParameterTypeListSyntax>();
    public ReturnTypeSyntax? ReturnType => Child<ReturnTypeSyntax>();
}

public sealed class TraitObjectTypeSyntax : TypeSyntax
{
    internal TraitObjectTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TraitObjectType, children) { }
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
}

public sealed class ImplTraitTypeSyntax : TypeSyntax
{
    internal ImplTraitTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ImplTraitType, children) { }
    public TypeBoundListSyntax? Bounds => Child<TypeBoundListSyntax>();
}

public sealed class NeverTypeSyntax : TypeSyntax
{
    internal NeverTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.NeverType, children) { }
}

public sealed class InferredTypeSyntax : TypeSyntax
{
    internal InferredTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.InferredType, children) { }
}

public sealed class MissingTypeSyntax : TypeSyntax
{
    internal MissingTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MissingType, children) { }
}

public sealed class MacroTypeSyntax : TypeSyntax
{
    internal MacroTypeSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MacroType, children) { }
    public MacroExpressionSyntax? Invocation => Child<MacroExpressionSyntax>();
}

// Statements and expressions
public sealed class EmptyStatementSyntax : StatementSyntax
{
    internal EmptyStatementSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.EmptyStatement, children) { }
}

public sealed class ItemStatementSyntax : StatementSyntax
{
    internal ItemStatementSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ItemStatement, children) { }
    public ItemSyntax? Item => Child<ItemSyntax>();
}

public sealed class LetStatementSyntax : StatementSyntax
{
    internal LetStatementSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LetStatement, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
    public TypeSyntax? Type => Child<TypeAnnotationSyntax>()?.Type;
    public EqualsValueClauseSyntax? Initializer => Child<EqualsValueClauseSyntax>();
    public BlockExpressionSyntax? Else => Child<BlockExpressionSyntax>();
}

public sealed class ExpressionStatementSyntax : StatementSyntax
{
    internal ExpressionStatementSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ExpressionStatement, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
    public SyntaxToken Semicolon => Token(SyntaxKind.SemicolonToken);
}

public sealed class BlockExpressionSyntax : ExpressionSyntax
{
    internal BlockExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.BlockExpression, children) { }
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
    public IEnumerable<StatementSyntax> Statements => Nodes<StatementSyntax>();
    public ExpressionSyntax? TailExpression => Child<ExpressionSyntax>();
}

public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LiteralExpression, children) { }
    public SyntaxToken Literal => ChildTokens().Last();
}

public sealed class PathExpressionSyntax : ExpressionSyntax
{
    internal PathExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.PathExpression, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
}

public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    internal ParenthesizedExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ParenthesizedExpression, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
}

public sealed class TupleExpressionSyntax : ExpressionSyntax
{
    internal TupleExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TupleExpression, children) { }
    public IEnumerable<ExpressionSyntax> Elements => Nodes<ExpressionSyntax>();
}

public sealed class ArrayExpressionSyntax : ExpressionSyntax
{
    internal ArrayExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ArrayExpression, children) { }
    public IEnumerable<ExpressionSyntax> Elements => Nodes<ExpressionSyntax>();
}

public sealed class RepeatArrayExpressionSyntax : ExpressionSyntax
{
    internal RepeatArrayExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.RepeatArrayExpression, children) { }
    public ExpressionSyntax? Element => Child<ExpressionSyntax>();
    public ExpressionSyntax? Length => Child<ExpressionSyntax>(1);
}

public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.UnaryExpression, children) { }
    public SyntaxToken OperatorToken => ChildTokens().First();
    public ExpressionSyntax? Operand => Child<ExpressionSyntax>();
}

public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    internal BinaryExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.BinaryExpression, children) { }
    public ExpressionSyntax? Left => Child<ExpressionSyntax>();
    public ExpressionSyntax? Right => Child<ExpressionSyntax>(1);
    public SyntaxToken OperatorToken => ChildTokens().First();
}

public sealed class AssignmentExpressionSyntax : ExpressionSyntax
{
    internal AssignmentExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.AssignmentExpression, children) { }
    public ExpressionSyntax? Left => Child<ExpressionSyntax>();
    public ExpressionSyntax? Right => Child<ExpressionSyntax>(1);
    public SyntaxToken OperatorToken => ChildTokens().First();
}

public sealed class RangeExpressionSyntax : ExpressionSyntax
{
    internal RangeExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.RangeExpression, children) { }
    public ExpressionSyntax? Start => Children[0].IsNode ? Children[0].Node as ExpressionSyntax : null;
    public ExpressionSyntax? End => Children[^1].IsNode ? Children[^1].Node as ExpressionSyntax : null;
    public SyntaxToken OperatorToken => Token(SyntaxKind.DotDotToken, SyntaxKind.DotDotEqualsToken);
}

public sealed class CastExpressionSyntax : ExpressionSyntax
{
    internal CastExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.CastExpression, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
    public TypeSyntax? Type => Child<TypeSyntax>();
}

public sealed class ArgumentListSyntax : SyntaxNode
{
    internal ArgumentListSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ArgumentList, children) { }
    public IEnumerable<ExpressionSyntax> Arguments => Nodes<ExpressionSyntax>();
}

public sealed class CallExpressionSyntax : ExpressionSyntax
{
    internal CallExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.CallExpression, children) { }
    public ExpressionSyntax? Target => Child<ExpressionSyntax>();
    public ArgumentListSyntax? Arguments => Child<ArgumentListSyntax>();
}

public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
{
    internal MemberAccessExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MemberAccessExpression, children) { }
    public ExpressionSyntax? Receiver => Child<ExpressionSyntax>();
    public SyntaxToken Member => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.IntegerLiteralToken);
    public GenericArgumentListSyntax? GenericArguments => Child<GenericArgumentListSyntax>();
}

public sealed class IndexExpressionSyntax : ExpressionSyntax
{
    internal IndexExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.IndexExpression, children) { }
    public ExpressionSyntax? Receiver => Child<ExpressionSyntax>();
    public ExpressionSyntax? Index => Child<ExpressionSyntax>(1);
}

public sealed class TryExpressionSyntax : ExpressionSyntax
{
    internal TryExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TryExpression, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
}

public sealed class AwaitExpressionSyntax : ExpressionSyntax
{
    internal AwaitExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.AwaitExpression, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
}

public sealed class StructExpressionSyntax : ExpressionSyntax
{
    internal StructExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StructExpression, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public IEnumerable<StructExpressionFieldSyntax> Fields => Nodes<StructExpressionFieldSyntax>();
    public ExpressionSyntax? Base => Child<ExpressionSyntax>();
}

public sealed class StructExpressionFieldSyntax : SyntaxNode
{
    internal StructExpressionFieldSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StructExpressionField, children) { }
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.IntegerLiteralToken);
    public ExpressionSyntax? Value => Child<ExpressionSyntax>();
}

public sealed class IfExpressionSyntax : ExpressionSyntax
{
    internal IfExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.IfExpression, children) { }
    public ExpressionSyntax? Condition => Child<ExpressionSyntax>();
    public BlockExpressionSyntax? Then => Child<ExpressionSyntax>(1) as BlockExpressionSyntax;
    public ExpressionSyntax? Else => Child<ExpressionSyntax>(2);
}

public sealed class LetExpressionSyntax : ExpressionSyntax
{
    internal LetExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LetExpression, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
    public ExpressionSyntax? Value => Child<ExpressionSyntax>();
}

public sealed class LoopExpressionSyntax : ExpressionSyntax
{
    internal LoopExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LoopExpression, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
    public ExpressionSyntax? ConditionOrIterator => Nodes<ExpressionSyntax>().SkipLast(1).FirstOrDefault();
    public BlockExpressionSyntax? Body => Nodes<ExpressionSyntax>().LastOrDefault() as BlockExpressionSyntax;
    public SyntaxToken Label => Token(SyntaxKind.LifetimeToken, SyntaxKind.RawLifetimeToken);
}

public sealed class MatchExpressionSyntax : ExpressionSyntax
{
    internal MatchExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MatchExpression, children) { }
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
    public IEnumerable<MatchArmSyntax> Arms => Nodes<MatchArmSyntax>();
}

public sealed class MatchArmSyntax : SyntaxNode
{
    internal MatchArmSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MatchArm, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
    public ExpressionSyntax? Guard => ChildTokens().Any(t => t.Kind == SyntaxKind.IfKeyword) ? Child<ExpressionSyntax>() : null;
    public ExpressionSyntax? Expression => Nodes<ExpressionSyntax>().LastOrDefault();
}

public sealed class ClosureExpressionSyntax : ExpressionSyntax
{
    internal ClosureExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ClosureExpression, children) { }
    public LifetimeBinderSyntax? Binder => Child<LifetimeBinderSyntax>();
    public ParameterListSyntax? Parameters => Child<ParameterListSyntax>();
    public ReturnTypeSyntax? ReturnType => Child<ReturnTypeSyntax>();
    public ExpressionSyntax? Body => Child<ExpressionSyntax>();
}

public sealed class ControlFlowExpressionSyntax : ExpressionSyntax
{
    internal ControlFlowExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ControlFlowExpression, children) { }
    public SyntaxToken Keyword => ChildTokens().First();
    public SyntaxToken Label => Token(SyntaxKind.LifetimeToken, SyntaxKind.RawLifetimeToken);
    public ExpressionSyntax? Value => Child<ExpressionSyntax>();
}

public sealed class MacroExpressionSyntax : ExpressionSyntax
{
    internal MacroExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MacroExpression, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public TokenTreeSyntax? Body => Child<TokenTreeSyntax>();
}

public sealed class AttributedExpressionSyntax : ExpressionSyntax
{
    internal AttributedExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.AttributedExpression, children) { }
    public IEnumerable<AttributeSyntax> Attributes => Nodes<AttributeSyntax>();
    public ExpressionSyntax? Expression => Child<ExpressionSyntax>();
}

public sealed class UnderscoreExpressionSyntax : ExpressionSyntax
{
    internal UnderscoreExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.UnderscoreExpression, children) { }
}

public sealed class MissingExpressionSyntax : ExpressionSyntax
{
    internal MissingExpressionSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MissingExpression, children) { }
}

// Patterns
public sealed class WildcardPatternSyntax : PatternSyntax
{
    internal WildcardPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.WildcardPattern, children) { }
}

public sealed class RestPatternSyntax : PatternSyntax
{
    internal RestPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.RestPattern, children) { }
}

public sealed class MissingPatternSyntax : PatternSyntax
{
    internal MissingPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MissingPattern, children) { }
}

public sealed class IdentifierPatternSyntax : PatternSyntax
{
    internal IdentifierPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.IdentifierPattern, children) { }
    public SyntaxToken Identifier => Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.SelfValueKeyword);
    public PatternSyntax? Subpattern => Child<PatternSyntax>();
}

public sealed class LiteralPatternSyntax : PatternSyntax
{
    internal LiteralPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.LiteralPattern, children) { }
    public SyntaxToken Literal => ChildTokens().Last();
}

public sealed class PathPatternSyntax : PatternSyntax
{
    internal PathPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.PathPattern, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
}

public sealed class ReferencePatternSyntax : PatternSyntax
{
    internal ReferencePatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ReferencePattern, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
}

public sealed class BoxPatternSyntax : PatternSyntax
{
    internal BoxPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.BoxPattern, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
}

public sealed class ParenthesizedPatternSyntax : PatternSyntax
{
    internal ParenthesizedPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ParenthesizedPattern, children) { }
    public PatternSyntax? Pattern => Child<PatternSyntax>();
}

public sealed class TuplePatternSyntax : PatternSyntax
{
    internal TuplePatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TuplePattern, children) { }
    public IEnumerable<PatternSyntax> Elements => Nodes<PatternSyntax>();
}

public sealed class SlicePatternSyntax : PatternSyntax
{
    internal SlicePatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.SlicePattern, children) { }
    public IEnumerable<PatternSyntax> Elements => Nodes<PatternSyntax>();
}

public sealed class TupleStructPatternSyntax : PatternSyntax
{
    internal TupleStructPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.TupleStructPattern, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public IEnumerable<PatternSyntax> Elements => Nodes<PatternSyntax>();
}

public sealed class StructPatternSyntax : PatternSyntax
{
    internal StructPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StructPattern, children) { }
    public PathSyntax? Path => Child<PathSyntax>();
    public IEnumerable<StructPatternFieldSyntax> Fields => Nodes<StructPatternFieldSyntax>();
}

public sealed class StructPatternFieldSyntax : SyntaxNode
{
    internal StructPatternFieldSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.StructPatternField, children) { }
    public SyntaxToken Identifier => ChildTokens().All(t => t.Kind != SyntaxKind.ColonToken) && Child<IdentifierPatternSyntax>() is IdentifierPatternSyntax shorthand
        ? shorthand.Identifier : Token(SyntaxKind.IdentifierToken, SyntaxKind.RawIdentifierToken, SyntaxKind.IntegerLiteralToken);
    public PatternSyntax? Pattern => Child<PatternSyntax>();
}

public sealed class OrPatternSyntax : PatternSyntax
{
    internal OrPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.OrPattern, children) { }
    public IEnumerable<PatternSyntax> Alternatives => Nodes<PatternSyntax>();
}

public sealed class RangePatternSyntax : PatternSyntax
{
    internal RangePatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.RangePattern, children) { }
    public PatternSyntax? Start => Children[0].IsNode ? Children[0].Node as PatternSyntax : null;
    public PatternSyntax? End => Children[^1].IsNode ? Children[^1].Node as PatternSyntax : null;
    public SyntaxToken OperatorToken => Token(SyntaxKind.DotDotToken, SyntaxKind.DotDotEqualsToken, SyntaxKind.DotDotDotToken);
}

public sealed class MacroPatternSyntax : PatternSyntax
{
    internal MacroPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.MacroPattern, children) { }
    public MacroExpressionSyntax? Invocation => Child<MacroExpressionSyntax>();
}

public sealed class ConstBlockPatternSyntax : PatternSyntax
{
    internal ConstBlockPatternSyntax(ImmutableArray<SyntaxNodeOrToken> children) : base(SyntaxKind.ConstBlockPattern, children) { }
    public BlockExpressionSyntax? Block => Child<BlockExpressionSyntax>();
}

internal static class SyntaxFactory
{
    internal static SyntaxNode Create(SyntaxKind kind, ImmutableArray<SyntaxNodeOrToken> children) => kind switch
    {
        SyntaxKind.CompilationUnit => new CompilationUnitSyntax(children),
        SyntaxKind.SkippedSyntax => new SkippedSyntax(children),
        SyntaxKind.TokenTree => new TokenTreeSyntax(children),
        SyntaxKind.Attribute => new AttributeSyntax(children),
        SyntaxKind.Visibility => new VisibilitySyntax(children),
        SyntaxKind.Path => new PathSyntax(children),
        SyntaxKind.QualifiedPath => new QualifiedPathSyntax(children),
        SyntaxKind.PathSegment => new PathSegmentSyntax(children),
        SyntaxKind.GenericParameterList => new GenericParameterListSyntax(children),
        SyntaxKind.GenericParameter => new GenericParameterSyntax(children),
        SyntaxKind.GenericArgumentList => new GenericArgumentListSyntax(children),
        SyntaxKind.GenericArgument => new GenericArgumentSyntax(children),
        SyntaxKind.LifetimeBinder => new LifetimeBinderSyntax(children),
        SyntaxKind.WhereClause => new WhereClauseSyntax(children),
        SyntaxKind.WherePredicate => new WherePredicateSyntax(children),
        SyntaxKind.TypeBoundList => new TypeBoundListSyntax(children),
        SyntaxKind.TypeBound => new TypeBoundSyntax(children),
        SyntaxKind.TypeAnnotation => new TypeAnnotationSyntax(children),
        SyntaxKind.ReturnType => new ReturnTypeSyntax(children),
        SyntaxKind.EqualsValueClause => new EqualsValueClauseSyntax(children),
        SyntaxKind.ParameterList => new ParameterListSyntax(children),
        SyntaxKind.Parameter => new ParameterSyntax(children),
        SyntaxKind.ParameterTypeList => new ParameterTypeListSyntax(children),
        SyntaxKind.ItemList => new ItemListSyntax(children),
        SyntaxKind.FieldList => new FieldListSyntax(children),
        SyntaxKind.Field => new FieldSyntax(children),
        SyntaxKind.EnumVariant => new EnumVariantSyntax(children),
        SyntaxKind.UseTree => new UseTreeSyntax(children),
        SyntaxKind.ModuleItem => new ModuleItemSyntax(children),
        SyntaxKind.UseItem => new UseItemSyntax(children),
        SyntaxKind.ExternCrateItem => new ExternCrateItemSyntax(children),
        SyntaxKind.FunctionItem => new FunctionItemSyntax(children),
        SyntaxKind.StructItem => new StructItemSyntax(children),
        SyntaxKind.UnionItem => new UnionItemSyntax(children),
        SyntaxKind.EnumItem => new EnumItemSyntax(children),
        SyntaxKind.TraitItem => new TraitItemSyntax(children),
        SyntaxKind.ImplItem => new ImplItemSyntax(children),
        SyntaxKind.ExternBlockItem => new ExternBlockItemSyntax(children),
        SyntaxKind.TypeAliasItem => new TypeAliasItemSyntax(children),
        SyntaxKind.ConstItem => new ConstItemSyntax(children),
        SyntaxKind.StaticItem => new StaticItemSyntax(children),
        SyntaxKind.MacroDefinitionItem => new MacroDefinitionItemSyntax(children),
        SyntaxKind.MacroInvocationItem => new MacroInvocationItemSyntax(children),
        SyntaxKind.MissingItem => new MissingItemSyntax(children),
        SyntaxKind.PathType => new PathTypeSyntax(children),
        SyntaxKind.ReferenceType => new ReferenceTypeSyntax(children),
        SyntaxKind.PointerType => new PointerTypeSyntax(children),
        SyntaxKind.TupleType => new TupleTypeSyntax(children),
        SyntaxKind.ParenthesizedType => new ParenthesizedTypeSyntax(children),
        SyntaxKind.SliceType => new SliceTypeSyntax(children),
        SyntaxKind.ArrayType => new ArrayTypeSyntax(children),
        SyntaxKind.FunctionPointerType => new FunctionPointerTypeSyntax(children),
        SyntaxKind.TraitObjectType => new TraitObjectTypeSyntax(children),
        SyntaxKind.ImplTraitType => new ImplTraitTypeSyntax(children),
        SyntaxKind.NeverType => new NeverTypeSyntax(children),
        SyntaxKind.InferredType => new InferredTypeSyntax(children),
        SyntaxKind.MissingType => new MissingTypeSyntax(children),
        SyntaxKind.MacroType => new MacroTypeSyntax(children),
        SyntaxKind.EmptyStatement => new EmptyStatementSyntax(children),
        SyntaxKind.ItemStatement => new ItemStatementSyntax(children),
        SyntaxKind.LetStatement => new LetStatementSyntax(children),
        SyntaxKind.ExpressionStatement => new ExpressionStatementSyntax(children),
        SyntaxKind.BlockExpression => new BlockExpressionSyntax(children),
        SyntaxKind.LiteralExpression => new LiteralExpressionSyntax(children),
        SyntaxKind.PathExpression => new PathExpressionSyntax(children),
        SyntaxKind.ParenthesizedExpression => new ParenthesizedExpressionSyntax(children),
        SyntaxKind.TupleExpression => new TupleExpressionSyntax(children),
        SyntaxKind.ArrayExpression => new ArrayExpressionSyntax(children),
        SyntaxKind.RepeatArrayExpression => new RepeatArrayExpressionSyntax(children),
        SyntaxKind.UnaryExpression => new UnaryExpressionSyntax(children),
        SyntaxKind.BinaryExpression => new BinaryExpressionSyntax(children),
        SyntaxKind.AssignmentExpression => new AssignmentExpressionSyntax(children),
        SyntaxKind.RangeExpression => new RangeExpressionSyntax(children),
        SyntaxKind.CastExpression => new CastExpressionSyntax(children),
        SyntaxKind.ArgumentList => new ArgumentListSyntax(children),
        SyntaxKind.CallExpression => new CallExpressionSyntax(children),
        SyntaxKind.MemberAccessExpression => new MemberAccessExpressionSyntax(children),
        SyntaxKind.IndexExpression => new IndexExpressionSyntax(children),
        SyntaxKind.TryExpression => new TryExpressionSyntax(children),
        SyntaxKind.AwaitExpression => new AwaitExpressionSyntax(children),
        SyntaxKind.StructExpression => new StructExpressionSyntax(children),
        SyntaxKind.StructExpressionField => new StructExpressionFieldSyntax(children),
        SyntaxKind.IfExpression => new IfExpressionSyntax(children),
        SyntaxKind.LetExpression => new LetExpressionSyntax(children),
        SyntaxKind.LoopExpression => new LoopExpressionSyntax(children),
        SyntaxKind.MatchExpression => new MatchExpressionSyntax(children),
        SyntaxKind.MatchArm => new MatchArmSyntax(children),
        SyntaxKind.ClosureExpression => new ClosureExpressionSyntax(children),
        SyntaxKind.ControlFlowExpression => new ControlFlowExpressionSyntax(children),
        SyntaxKind.MacroExpression => new MacroExpressionSyntax(children),
        SyntaxKind.AttributedExpression => new AttributedExpressionSyntax(children),
        SyntaxKind.UnderscoreExpression => new UnderscoreExpressionSyntax(children),
        SyntaxKind.MissingExpression => new MissingExpressionSyntax(children),
        SyntaxKind.WildcardPattern => new WildcardPatternSyntax(children),
        SyntaxKind.RestPattern => new RestPatternSyntax(children),
        SyntaxKind.MissingPattern => new MissingPatternSyntax(children),
        SyntaxKind.IdentifierPattern => new IdentifierPatternSyntax(children),
        SyntaxKind.LiteralPattern => new LiteralPatternSyntax(children),
        SyntaxKind.PathPattern => new PathPatternSyntax(children),
        SyntaxKind.ReferencePattern => new ReferencePatternSyntax(children),
        SyntaxKind.BoxPattern => new BoxPatternSyntax(children),
        SyntaxKind.ParenthesizedPattern => new ParenthesizedPatternSyntax(children),
        SyntaxKind.TuplePattern => new TuplePatternSyntax(children),
        SyntaxKind.SlicePattern => new SlicePatternSyntax(children),
        SyntaxKind.TupleStructPattern => new TupleStructPatternSyntax(children),
        SyntaxKind.StructPattern => new StructPatternSyntax(children),
        SyntaxKind.StructPatternField => new StructPatternFieldSyntax(children),
        SyntaxKind.OrPattern => new OrPatternSyntax(children),
        SyntaxKind.RangePattern => new RangePatternSyntax(children),
        SyntaxKind.MacroPattern => new MacroPatternSyntax(children),
        SyntaxKind.ConstBlockPattern => new ConstBlockPatternSyntax(children),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
