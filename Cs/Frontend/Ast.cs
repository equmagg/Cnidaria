using System;
using System.Collections;
using System.Collections.Generic;

namespace Cnidaria.Cs
{

    ///<summary>Builds source spans</summary>
    internal static class NodeSpan
    {
        public static TextSpan Combine(TextSpan first, TextSpan second)
        {
            int start = Math.Min(first.Start, second.Start);
            int end = Math.Max(first.End, second.End);
            return new TextSpan(start, end - start);
        }

        public static TextSpan Combine3(TextSpan a, TextSpan b, TextSpan c)
            => Combine(Combine(a, b), c);

        public static TextSpan From(params TextSpan[] spans)
        {
            if (spans == null || spans.Length == 0) return new TextSpan(0, 0);
            int start = spans[0].Start;
            int end = spans[0].End;
            for (int i = 1; i < spans.Length; i++)
            {
                start = Math.Min(start, spans[i].Start);
                end = Math.Max(end, spans[i].End);
            }
            return new TextSpan(start, end - start);
        }

        public static TextSpan FromNonNull(params TextSpan?[] spans)
        {
            int start = int.MaxValue;
            int end = int.MinValue;
            bool any = false;

            foreach (var s in spans)
            {
                if (s is null) continue;
                any = true;
                start = Math.Min(start, s.Value.Start);
                end = Math.Max(end, s.Value.End);
            }

            return any ? new TextSpan(start, end - start) : new TextSpan(0, 0);
        }
    }

    ///<summary>Base class for source-backed syntax</summary>
    public abstract class SyntaxNode
    {
        public SyntaxKind Kind { get; }
        public TextSpan Span { get; }

        protected SyntaxNode(SyntaxKind kind, TextSpan span)
        {
            Kind = kind;
            Span = span;
        }
    }

    ///<summary>Stores one element of an interleaved node and separator sequence</summary>
    public readonly struct SyntaxNodeOrToken
    {
        private readonly SyntaxNode? _node;
        private readonly SyntaxToken _token;

        public bool IsToken { get; }
        public bool IsNode => !IsToken;

        public SyntaxNode Node => _node ?? throw new InvalidOperationException("Not a node.");
        public SyntaxToken Token => IsToken ? _token : throw new InvalidOperationException("Not a token.");

        public TextSpan Span => IsToken ? _token.Span : _node!.Span;

        public SyntaxNodeOrToken(SyntaxNode node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _token = default;
            IsToken = false;
        }

        public SyntaxNodeOrToken(SyntaxToken token)
        {
            _node = null;
            _token = token;
            IsToken = true;
        }
    }

    ///<summary>Stores an ordered node sequence without separators</summary>
    public readonly struct SyntaxList<T> : IEnumerable<T> where T : SyntaxNode
    {
        private readonly T[] _items;

        public static SyntaxList<T> Empty => new(Array.Empty<T>());

        public SyntaxList(T[] items) => _items = items ?? Array.Empty<T>();

        public int Count => _items.Length;

        public T this[int index] => _items[index];

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public T[] ToArray() => _items;
    }

    ///<summary>Stores nodes and separator tokens in source order</summary>
    public readonly struct SeparatedSyntaxList<T> : IEnumerable<T> where T : SyntaxNode
    {
        private readonly SyntaxNodeOrToken[] _nodesAndSeparators;

        public SeparatedSyntaxList(SyntaxNodeOrToken[] nodesAndSeparators)
            => _nodesAndSeparators = nodesAndSeparators ?? Array.Empty<SyntaxNodeOrToken>();
        public static SeparatedSyntaxList<T> Empty => new(Array.Empty<SyntaxNodeOrToken>());
        private SyntaxNodeOrToken[] Items => _nodesAndSeparators ?? Array.Empty<SyntaxNodeOrToken>();
        public int Count
        {
            get
            {
                var items = Items;
                int len = items.Length;
                if (len == 0) return 0;

                if (items[len - 1].IsToken)
                    return len / 2;

                return (len + 1) / 2;
            }
        }

        public int SeparatorCount => Items.Length / 2;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return (T)Items[index * 2].Node;
            }
        }

        public SyntaxToken GetSeparator(int index)
        {
            if ((uint)index >= (uint)SeparatorCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return Items[index * 2 + 1].Token;
        }

        public SyntaxNodeOrToken[] GetWithSeparators() => Items;

        public IEnumerator<T> GetEnumerator()
        {
            var items = Items;
            for (int i = 0; i < items.Length; i += 2)
            {
                if (items[i].IsNode)
                    yield return (T)items[i].Node;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    ///<summary>Stores an ordered token sequence</summary>
    public readonly struct SyntaxTokenList : IEnumerable<SyntaxToken>
    {
        private readonly SyntaxToken[] _tokens;

        public static SyntaxTokenList Empty => new(Array.Empty<SyntaxToken>());

        public SyntaxTokenList(SyntaxToken[] tokens) => _tokens = tokens ?? Array.Empty<SyntaxToken>();

        public int Count => _tokens.Length;
        public SyntaxToken this[int index] => _tokens[index];

        public IEnumerator<SyntaxToken> GetEnumerator() => ((IEnumerable<SyntaxToken>)_tokens).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _tokens.GetEnumerator();

        public SyntaxToken[] ToArray() => _tokens;
    }

    // abstract nodes
    ///<summary>Base class for syntax that can be bound as an expression</summary>
    public abstract class ExpressionSyntax : SyntaxNode
    {
        protected ExpressionSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Base class for executable statement syntax</summary>
    public abstract class StatementSyntax : SyntaxNode
    {
        protected StatementSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Base class for text and interpolation segments inside an interpolated string</summary>
    public abstract class InterpolatedStringContentSyntax : SyntaxNode
    {
        protected InterpolatedStringContentSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    ///<summary>Base class for declarations allowed in a compilation unit, namespace or type</summary>
    public abstract class MemberDeclarationSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }

        protected MemberDeclarationSyntax(SyntaxKind kind, SyntaxList<AttributeListSyntax> attributeLists, TextSpan span)
            : base(kind, span)
        {
            AttributeLists = attributeLists;
        }
    }
    ///<summary>Base class for expression and spread elements in a collection expression</summary>
    public abstract class CollectionElementSyntax : SyntaxNode
    {
        protected CollectionElementSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    ///<summary>Base class for entries in a type base list</summary>
    public abstract class BaseTypeSyntax : SyntaxNode
    {
        public abstract TypeSyntax Type { get; }
        protected BaseTypeSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    ///<summary>Represents a base type without primary constructor arguments</summary>
    public sealed class SimpleBaseTypeSyntax : BaseTypeSyntax
    {
        public override TypeSyntax Type { get; }

        public SimpleBaseTypeSyntax(TypeSyntax type)
            : base(SyntaxKind.SimpleBaseType, type.Span)
        {
            Type = type;
        }
    }
    ///<summary>Represents the colon-prefixed base type and interface list</summary>
    public sealed class BaseListSyntax : SyntaxNode
    {
        public SyntaxToken ColonToken { get; }
        public SeparatedSyntaxList<BaseTypeSyntax> Types { get; }

        public BaseListSyntax(SyntaxToken colonToken, SeparatedSyntaxList<BaseTypeSyntax> types)
            : base(SyntaxKind.BaseList, types.Count > 0
                ? NodeSpan.From(colonToken.Span, types[types.Count - 1].Span)
                : colonToken.Span)
        {
            ColonToken = colonToken;
            Types = types;
        }
    }
    ///<summary>Represents an expression body introduced by '=>'</summary>
    public sealed class ArrowExpressionClauseSyntax : SyntaxNode
    {
        public SyntaxToken ArrowToken { get; }
        public ExpressionSyntax Expression { get; }

        public ArrowExpressionClauseSyntax(SyntaxToken arrowToken, ExpressionSyntax expression)
            : base(SyntaxKind.ArrowExpressionClause, NodeSpan.From(arrowToken.Span, expression.Span))
        {
            ArrowToken = arrowToken;
            Expression = expression;
        }
    }
    ///<summary>Base class for syntax interpreted as a type</summary>
    public abstract class TypeSyntax : ExpressionSyntax
    {
        protected TypeSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Base class for qualified and simple names</summary>
    public abstract class NameSyntax : TypeSyntax
    {
        protected NameSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Base class for identifier and generic names</summary>
    public abstract class SimpleNameSyntax : NameSyntax
    {
        protected SimpleNameSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    // base nodes
    ///<summary>Represents the complete source file including directives, members and end-of-file token</summary>
    public sealed class CompilationUnitSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxList<ExternAliasDirectiveSyntax> Externs { get; }
        public SyntaxList<UsingDirectiveSyntax> Usings { get; }
        public SyntaxList<MemberDeclarationSyntax> Members { get; }
        public SyntaxToken EndOfFileToken { get; }

        public CompilationUnitSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxList<ExternAliasDirectiveSyntax> externs,
            SyntaxList<UsingDirectiveSyntax> usings,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken endOfFileToken)
            : base(
                SyntaxKind.CompilationUnit,
                NodeSpan.FromNonNull(
                    externs.Count > 0 ? externs[0].Span : (TextSpan?)null,
                    usings.Count > 0 ? usings[0].Span : (TextSpan?)null,
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    members.Count > 0 ? members[0].Span : (TextSpan?)null,
                    endOfFileToken.Span))
        {
            AttributeLists = attributeLists;
            Externs = externs;
            Usings = usings;
            Members = members;
            EndOfFileToken = endOfFileToken;
        }
    }
    ///<summary>Represents an 'extern alias name;' directive</summary>
    public sealed class ExternAliasDirectiveSyntax : SyntaxNode
    {
        public SyntaxToken ExternKeyword { get; }
        public SyntaxToken AliasKeyword { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken SemicolonToken { get; }

        public ExternAliasDirectiveSyntax(SyntaxToken externKeyword, SyntaxToken aliasKeyword, SyntaxToken identifier, SyntaxToken semicolonToken)
            : base(SyntaxKind.ExternAliasDirective, NodeSpan.From(externKeyword.Span, semicolonToken.Span))
        {
            ExternKeyword = externKeyword;
            AliasKeyword = aliasKeyword;
            Identifier = identifier;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents the 'name =' prefix used by aliases and named attribute arguments</summary>
    public sealed class NameEqualsSyntax : SyntaxNode
    {
        public IdentifierNameSyntax Name { get; }
        public SyntaxToken EqualsToken { get; }

        public NameEqualsSyntax(IdentifierNameSyntax name, SyntaxToken equalsToken)
            : base(SyntaxKind.NameEquals, NodeSpan.From(name.Span, equalsToken.Span))
        {
            Name = name;
            EqualsToken = equalsToken;
        }
    }
    ///<summary>Represents a using directive with optional 'global', 'static', 'unsafe' or alias syntax</summary>
    public sealed class UsingDirectiveSyntax : SyntaxNode
    {
        public SyntaxToken GlobalKeyword { get; } // optional
        public SyntaxToken UsingKeyword { get; }
        public SyntaxToken StaticKeyword { get; } // optional
        public SyntaxToken UnsafeKeyword { get; } // optional
        public NameEqualsSyntax? Alias { get; }   // optional
        public TypeSyntax NamespaceOrType { get; }
        public NameSyntax? Name => NamespaceOrType as NameSyntax;
        public SyntaxToken SemicolonToken { get; }

        public UsingDirectiveSyntax(
            SyntaxToken globalKeyword,
            SyntaxToken usingKeyword,
            SyntaxToken staticKeyword,
            SyntaxToken unsafeKeyword,
            NameEqualsSyntax? alias,
            TypeSyntax namespaceOrType,
            SyntaxToken semicolonToken)
            : base(SyntaxKind.UsingDirective,
                   NodeSpan.FromNonNull(
                       globalKeyword.Span.Length != 0 ? globalKeyword.Span : (TextSpan?)null,
                       usingKeyword.Span,
                       semicolonToken.Span))
        {
            GlobalKeyword = globalKeyword;
            UsingKeyword = usingKeyword;
            StaticKeyword = staticKeyword;
            UnsafeKeyword = unsafeKeyword;
            Alias = alias;
            NamespaceOrType = namespaceOrType;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Wraps a top-level statement as a compilation unit member</summary>
    public sealed class GlobalStatementSyntax : MemberDeclarationSyntax
    {
        public StatementSyntax Statement { get; }

        public GlobalStatementSyntax(SyntaxList<AttributeListSyntax> attributeLists, StatementSyntax statement)
            : base(
                SyntaxKind.GlobalStatement,
                attributeLists,
                NodeSpan.FromNonNull(attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null, statement.Span))
        {
            Statement = statement;
        }
    }

    ///<summary>Represents a brace-delimited statement list</summary>
    public sealed class BlockSyntax : StatementSyntax
    {
        public SyntaxToken OpenBraceToken { get; }
        public SyntaxList<StatementSyntax> Statements { get; }
        public SyntaxToken CloseBraceToken { get; }

        public BlockSyntax(SyntaxToken openBraceToken, SyntaxList<StatementSyntax> statements, SyntaxToken closeBraceToken)
            : base(SyntaxKind.Block, NodeSpan.From(openBraceToken.Span, closeBraceToken.Span))
        {
            OpenBraceToken = openBraceToken;
            Statements = statements;
            CloseBraceToken = closeBraceToken;
        }
    }

    ///<summary>Represents an expression followed by ';'</summary>
    public sealed class ExpressionStatementSyntax : StatementSyntax
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public ExpressionStatementSyntax(ExpressionSyntax expression, SyntaxToken semicolonToken)
            : base(SyntaxKind.ExpressionStatement, NodeSpan.From(expression.Span, semicolonToken.Span))
        {
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }


    // type nodes
    ///<summary>Represents a single identifier used as a name</summary>
    public sealed class IdentifierNameSyntax : SimpleNameSyntax
    {
        public SyntaxToken Identifier { get; }

        public IdentifierNameSyntax(SyntaxToken identifier)
            : base(SyntaxKind.IdentifierName, identifier.Span)
        {
            Identifier = identifier;
        }
    }

    ///<summary>Represents a dotted name such as 'A.B'</summary>
    public sealed class QualifiedNameSyntax : NameSyntax
    {
        public NameSyntax Left { get; }
        public SyntaxToken DotToken { get; }
        public SimpleNameSyntax Right { get; }

        public QualifiedNameSyntax(NameSyntax left, SyntaxToken dotToken, SimpleNameSyntax right)
            : base(SyntaxKind.QualifiedName, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            DotToken = dotToken;
            Right = right;
        }
    }
    ///<summary>Represents an alias-qualified name such as 'global::System'</summary>
    public sealed class AliasQualifiedNameSyntax : NameSyntax
    {
        public IdentifierNameSyntax Alias { get; }
        public SyntaxToken ColonColonToken { get; }
        public SimpleNameSyntax Name { get; }

        public AliasQualifiedNameSyntax(
            IdentifierNameSyntax alias,
            SyntaxToken colonColonToken,
            SimpleNameSyntax name)
            : base(SyntaxKind.AliasQualifiedName, NodeSpan.From(alias.Span, name.Span))
        {
            Alias = alias;
            ColonColonToken = colonColonToken;
            Name = name;
        }
    }
    ///<summary>Represents the interface name and trailing dot in an explicit member declaration</summary>
    public sealed class ExplicitInterfaceSpecifierSyntax : SyntaxNode
    {
        public NameSyntax Name { get; }
        public SyntaxToken DotToken { get; }

        public ExplicitInterfaceSpecifierSyntax(NameSyntax name, SyntaxToken dotToken)
            : base(SyntaxKind.ExplicitInterfaceSpecifier, NodeSpan.From(name.Span, dotToken.Span))
        {
            Name = name;
            DotToken = dotToken;
        }
    }
    ///<summary>Represents a type keyword such as 'int', 'string' or 'void'</summary>
    public sealed class PredefinedTypeSyntax : TypeSyntax
    {
        public SyntaxToken Keyword { get; }

        public PredefinedTypeSyntax(SyntaxToken keyword)
            : base(SyntaxKind.PredefinedType, keyword.Span)
        {
            Keyword = keyword;
        }
    }
    ///<summary>Represents 'ref T' or 'ref readonly T'</summary>
    public sealed class RefTypeSyntax : TypeSyntax
    {
        public SyntaxToken RefKeyword { get; }
        public SyntaxToken ReadOnlyKeyword { get; } // optional
        public TypeSyntax Type { get; }

        public RefTypeSyntax(SyntaxToken refKeyword, SyntaxToken readOnlyKeyword, TypeSyntax type)
            : base(SyntaxKind.RefType, NodeSpan.From(refKeyword.Span, type.Span))
        {
            RefKeyword = refKeyword;
            ReadOnlyKeyword = readOnlyKeyword;
            Type = type;
        }
    }
    ///<summary>Represents 'scoped T'</summary>
    public sealed class ScopedTypeSyntax : TypeSyntax
    {
        public SyntaxToken ScopedKeyword { get; }
        public TypeSyntax Type { get; }

        public ScopedTypeSyntax(SyntaxToken scopedKeyword, TypeSyntax type)
            : base(SyntaxKind.ScopedType, NodeSpan.From(scopedKeyword.Span, type.Span))
        {
            ScopedKeyword = scopedKeyword;
            Type = type;
        }
    }
    ///<summary>Represents the pointer type 'T*'</summary>
    public sealed class PointerTypeSyntax : TypeSyntax
    {
        public TypeSyntax ElementType { get; }
        public SyntaxToken AsteriskToken { get; }

        public PointerTypeSyntax(TypeSyntax elementType, SyntaxToken asteriskToken)
            : base(SyntaxKind.PointerType, NodeSpan.From(elementType.Span, asteriskToken.Span))
        {
            ElementType = elementType;
            AsteriskToken = asteriskToken;
        }
    }
    ///<summary>Represents a function pointer type beginning with delegate and an asterisk</summary>
    public sealed class FunctionPointerTypeSyntax : TypeSyntax
    {
        public SyntaxToken DelegateKeyword { get; }
        public SyntaxToken AsteriskToken { get; }
        public FunctionPointerCallingConventionSyntax? CallingConvention { get; }
        public FunctionPointerParameterListSyntax ParameterList { get; }

        public FunctionPointerTypeSyntax(
            SyntaxToken delegateKeyword,
            SyntaxToken asteriskToken,
            FunctionPointerCallingConventionSyntax? callingConvention,
            FunctionPointerParameterListSyntax parameterList)
            : base(
                SyntaxKind.FunctionPointerType,
                NodeSpan.FromNonNull(delegateKeyword.Span, asteriskToken.Span, callingConvention?.Span, parameterList.Span))
        {
            DelegateKeyword = delegateKeyword;
            AsteriskToken = asteriskToken;
            CallingConvention = callingConvention;
            ParameterList = parameterList;
        }
    }
    ///<summary>Represents the angle-bracketed parameter types and final return type of a function pointer</summary>
    public sealed class FunctionPointerParameterListSyntax : SyntaxNode
    {
        public SyntaxToken LessThanToken { get; }
        public SeparatedSyntaxList<FunctionPointerParameterSyntax> Parameters { get; }
        public SyntaxToken GreaterThanToken { get; }

        public FunctionPointerParameterListSyntax(
            SyntaxToken lessThanToken,
            SeparatedSyntaxList<FunctionPointerParameterSyntax> parameters,
            SyntaxToken greaterThanToken)
            : base(SyntaxKind.FunctionPointerParameterList, NodeSpan.From(lessThanToken.Span, greaterThanToken.Span))
        {
            LessThanToken = lessThanToken;
            Parameters = parameters;
            GreaterThanToken = greaterThanToken;
        }
    }
    ///<summary>Represents one function pointer parameter or its final return type</summary>
    public sealed class FunctionPointerParameterSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax Type { get; }

        public FunctionPointerParameterSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax type)
            : base(
                SyntaxKind.FunctionPointerParameter,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    type.Span))
        {
            AttributeLists = attributeLists;
            Modifiers = modifiers;
            Type = type;
        }
    }
    ///<summary>Represents the optional 'managed' or 'unmanaged[...]' calling convention</summary>
    public sealed class FunctionPointerCallingConventionSyntax : SyntaxNode
    {
        public SyntaxToken ManagedOrUnmanagedKeyword { get; }
        public FunctionPointerUnmanagedCallingConventionListSyntax? UnmanagedCallingConventionList { get; }

        public FunctionPointerCallingConventionSyntax(
            SyntaxToken managedOrUnmanagedKeyword,
            FunctionPointerUnmanagedCallingConventionListSyntax? unmanagedCallingConventionList)
            : base(
                SyntaxKind.FunctionPointerCallingConvention,
                NodeSpan.FromNonNull(managedOrUnmanagedKeyword.Span, unmanagedCallingConventionList?.Span))
        {
            ManagedOrUnmanagedKeyword = managedOrUnmanagedKeyword;
            UnmanagedCallingConventionList = unmanagedCallingConventionList;
        }
    }
    ///<summary>Represents the bracketed unmanaged calling convention identifiers</summary>
    public sealed class FunctionPointerUnmanagedCallingConventionListSyntax : SyntaxNode
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<FunctionPointerUnmanagedCallingConventionSyntax> CallingConventions { get; }
        public SyntaxToken CloseBracketToken { get; }

        public FunctionPointerUnmanagedCallingConventionListSyntax(
            SyntaxToken openBracketToken,
            SeparatedSyntaxList<FunctionPointerUnmanagedCallingConventionSyntax> callingConventions,
            SyntaxToken closeBracketToken)
            : base(SyntaxKind.FunctionPointerUnmanagedCallingConventionList, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            CallingConventions = callingConventions;
            CloseBracketToken = closeBracketToken;
        }
    }
    ///<summary>Represents one unmanaged calling convention identifier</summary>
    public sealed class FunctionPointerUnmanagedCallingConventionSyntax : SyntaxNode
    {
        public SyntaxToken Name { get; }

        public FunctionPointerUnmanagedCallingConventionSyntax(SyntaxToken name)
            : base(SyntaxKind.FunctionPointerUnmanagedCallingConvention, name.Span)
        {
            Name = name;
        }
    }
    ///<summary>Represents 'T?' when parsed as a type</summary>
    public sealed class NullableTypeSyntax : TypeSyntax
    {
        public TypeSyntax ElementType { get; }
        public SyntaxToken QuestionToken { get; }

        public NullableTypeSyntax(TypeSyntax elementType, SyntaxToken questionToken)
            : base(SyntaxKind.NullableType, NodeSpan.From(elementType.Span, questionToken.Span))
        {
            ElementType = elementType;
            QuestionToken = questionToken;
        }
    }
    ///<summary>Represents a parenthesized tuple type</summary>
    public sealed class TupleTypeSyntax : TypeSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<TupleElementSyntax> Elements { get; }
        public SyntaxToken CloseParenToken { get; }

        public TupleTypeSyntax(
            SyntaxToken openParenToken,
            SeparatedSyntaxList<TupleElementSyntax> elements,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.TupleType, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Elements = elements;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents one tuple type element with an optional name</summary>
    public sealed class TupleElementSyntax : SyntaxNode
    {
        public TypeSyntax Type { get; }
        public SyntaxToken Identifier { get; } // optional

        public TupleElementSyntax(TypeSyntax type, SyntaxToken identifier)
            : base(
                  SyntaxKind.TupleElement,
                  identifier.Span.Length != 0
                      ? NodeSpan.From(type.Span, identifier.Span)
                      : type.Span)
        {
            Type = type;
            Identifier = identifier;
        }
    }
    // =pattern nodes=
    ///<summary>Base class for syntax accepted in pattern positions</summary>
    public abstract class PatternSyntax : SyntaxNode
    {
        protected PatternSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    ///<summary>Represents an expression matched by value</summary>
    public sealed class ConstantPatternSyntax : PatternSyntax
    {
        public ExpressionSyntax Expression { get; }

        public ConstantPatternSyntax(ExpressionSyntax expression)
            : base(SyntaxKind.ConstantPattern, expression.Span)
        {
            Expression = expression;
        }
    }
    ///<summary>Represents a type test with a variable designation</summary>
    public sealed class DeclarationPatternSyntax : PatternSyntax
    {
        public TypeSyntax Type { get; }
        public VariableDesignationSyntax Designation { get; }

        public DeclarationPatternSyntax(TypeSyntax type, VariableDesignationSyntax designation)
            : base(SyntaxKind.DeclarationPattern, NodeSpan.From(type.Span, designation.Span))
        {
            Type = type;
            Designation = designation;
        }
    }
    ///<summary>Represents 'var' followed by a variable designation</summary>
    public sealed class VarPatternSyntax : PatternSyntax
    {
        public SyntaxToken VarKeyword { get; }
        public VariableDesignationSyntax Designation { get; }

        public VarPatternSyntax(SyntaxToken varKeyword, VariableDesignationSyntax designation)
            : base(SyntaxKind.VarPattern, NodeSpan.From(varKeyword.Span, designation.Span))
        {
            VarKeyword = varKeyword;
            Designation = designation;
        }
    }
    ///<summary>Represents a pattern that tests only the input type</summary>
    public sealed class TypePatternSyntax : PatternSyntax
    {
        public TypeSyntax Type { get; }

        public TypePatternSyntax(TypeSyntax type)
            : base(SyntaxKind.TypePattern, type.Span)
        {
            Type = type;
        }
    }
    ///<summary>Represents a relational operator followed by a constant expression</summary>
    public sealed class RelationalPatternSyntax : PatternSyntax
    {
        public SyntaxToken OperatorToken { get; } // '<', '<=', '>' or '>='
        public ExpressionSyntax Expression { get; }

        public RelationalPatternSyntax(SyntaxToken operatorToken, ExpressionSyntax expression)
            : base(SyntaxKind.RelationalPattern, NodeSpan.From(operatorToken.Span, expression.Span))
        {
            OperatorToken = operatorToken;
            Expression = expression;
        }
    }
    ///<summary>Represents two patterns joined by 'and' or 'or'</summary>
    public sealed class BinaryPatternSyntax : PatternSyntax
    {
        public PatternSyntax Left { get; }
        public SyntaxToken OperatorToken { get; } // 'and' or 'or'
        public PatternSyntax Right { get; }

        public BinaryPatternSyntax(SyntaxKind kind, PatternSyntax left, SyntaxToken operatorToken, PatternSyntax right)
            : base(kind, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }
    ///<summary>Represents a pattern prefixed by 'not'</summary>
    public sealed class UnaryPatternSyntax : PatternSyntax
    {
        public SyntaxToken OperatorToken { get; } // 'not'
        public PatternSyntax Pattern { get; }

        public UnaryPatternSyntax(SyntaxKind kind, SyntaxToken operatorToken, PatternSyntax pattern)
            : base(kind, NodeSpan.From(operatorToken.Span, pattern.Span))
        {
            OperatorToken = operatorToken;
            Pattern = pattern;
        }
    }
    ///<summary>Represents a bracket-delimited list pattern with an optional designation</summary>
    public sealed class ListPatternSyntax : PatternSyntax
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<PatternSyntax> Patterns { get; }
        public SyntaxToken CloseBracketToken { get; }
        public VariableDesignationSyntax? Designation { get; }

        public ListPatternSyntax(
            SyntaxToken openBracketToken,
            SeparatedSyntaxList<PatternSyntax> patterns,
            SyntaxToken closeBracketToken,
            VariableDesignationSyntax? designation)
            : base(
                  SyntaxKind.ListPattern,
                  NodeSpan.FromNonNull(
                      openBracketToken.Span,
                      closeBracketToken.Span,
                      designation?.Span))
        {
            OpenBracketToken = openBracketToken;
            Patterns = patterns;
            CloseBracketToken = closeBracketToken;
            Designation = designation;
        }
    }
    ///<summary>Represents a slice pattern beginning with '..'</summary>
    public sealed class SlicePatternSyntax : PatternSyntax
    {
        public SyntaxToken DotDotToken { get; }
        public PatternSyntax? Pattern { get; }

        public SlicePatternSyntax(SyntaxToken dotDotToken, PatternSyntax? pattern)
            : base(
                  SyntaxKind.SlicePattern,
                  pattern is null
                      ? dotDotToken.Span
                      : NodeSpan.From(dotDotToken.Span, pattern.Span))
        {
            DotDotToken = dotDotToken;
            Pattern = pattern;
        }
    }
    ///<summary>Represents a parenthesized pattern</summary>
    public sealed class ParenthesizedPatternSyntax : PatternSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public PatternSyntax Pattern { get; }
        public SyntaxToken CloseParenToken { get; }

        public ParenthesizedPatternSyntax(SyntaxToken openParenToken, PatternSyntax pattern, SyntaxToken closeParenToken)
            : base(SyntaxKind.ParenthesizedPattern, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Pattern = pattern;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents the discard pattern '_'</summary>
    public sealed class DiscardPatternSyntax : PatternSyntax
    {
        public SyntaxToken UnderscoreToken { get; }

        public DiscardPatternSyntax(SyntaxToken underscoreToken)
            : base(SyntaxKind.DiscardPattern, underscoreToken.Span)
        {
            UnderscoreToken = underscoreToken;
        }
    }
    ///<summary>Represents a recursive pattern with optional type, positional clause, property clause and designation</summary>
    public sealed class RecursivePatternSyntax : PatternSyntax
    {
        public TypeSyntax? Type { get; }
        public PositionalPatternClauseSyntax? PositionalPatternClause { get; }
        public PropertyPatternClauseSyntax? PropertyPatternClause { get; }
        public VariableDesignationSyntax? Designation { get; }

        public RecursivePatternSyntax(
            TypeSyntax? type,
            PositionalPatternClauseSyntax? positionalPatternClause,
            PropertyPatternClauseSyntax? propertyPatternClause,
            VariableDesignationSyntax? designation)
            : base(
                  SyntaxKind.RecursivePattern,
                  NodeSpan.FromNonNull(
                      type?.Span,
                      positionalPatternClause?.Span,
                      propertyPatternClause?.Span,
                      designation?.Span))
        {
            Type = type;
            PositionalPatternClause = positionalPatternClause;
            PropertyPatternClause = propertyPatternClause;
            Designation = designation;
        }
    }
    ///<summary>Represents a parenthesized positional subpattern list</summary>
    public sealed class PositionalPatternClauseSyntax : SyntaxNode
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<SubpatternSyntax> Subpatterns { get; }
        public SyntaxToken CloseParenToken { get; }

        public PositionalPatternClauseSyntax(
            SyntaxToken openParenToken,
            SeparatedSyntaxList<SubpatternSyntax> subpatterns,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.PositionalPatternClause, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Subpatterns = subpatterns;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a brace-delimited property subpattern list</summary>
    public sealed class PropertyPatternClauseSyntax : SyntaxNode
    {
        public SyntaxToken OpenBraceToken { get; }
        public SeparatedSyntaxList<SubpatternSyntax> Subpatterns { get; }
        public SyntaxToken CloseBraceToken { get; }

        public PropertyPatternClauseSyntax(
            SyntaxToken openBraceToken,
            SeparatedSyntaxList<SubpatternSyntax> subpatterns,
            SyntaxToken closeBraceToken)
            : base(SyntaxKind.PropertyPatternClause, NodeSpan.From(openBraceToken.Span, closeBraceToken.Span))
        {
            OpenBraceToken = openBraceToken;
            Subpatterns = subpatterns;
            CloseBraceToken = closeBraceToken;
        }
    }
    ///<summary>Represents one positional or property subpattern</summary>
    public sealed class SubpatternSyntax : SyntaxNode
    {
        public BaseExpressionColonSyntax? ExpressionColon { get; }
        public NameColonSyntax? NameColon => ExpressionColon as NameColonSyntax;
        public PatternSyntax Pattern { get; }

        public SubpatternSyntax(BaseExpressionColonSyntax? expressionColon, PatternSyntax pattern)
            : base(SyntaxKind.Subpattern, NodeSpan.FromNonNull(expressionColon?.Span, pattern.Span))
        {
            ExpressionColon = expressionColon;
            Pattern = pattern;
        }
    }
    ///<summary>Represents a 'when' guard attached to a switch label or arm</summary>
    public sealed class WhenClauseSyntax : SyntaxNode
    {
        public SyntaxToken WhenKeyword { get; }
        public ExpressionSyntax Condition { get; }

        public WhenClauseSyntax(SyntaxToken whenKeyword, ExpressionSyntax condition)
            : base(SyntaxKind.WhenClause, NodeSpan.From(whenKeyword.Span, condition.Span))
        {
            WhenKeyword = whenKeyword;
            Condition = condition;
        }
    }
    // =statement nodes=
    ///<summary>Represents a bracketed attribute list with an optional target</summary>
    public sealed class AttributeListSyntax : SyntaxNode
    {
        public SyntaxToken OpenBracketToken { get; }
        public AttributeTargetSpecifierSyntax? Target { get; }
        public SeparatedSyntaxList<AttributeSyntax> Attributes { get; }
        public SyntaxToken CloseBracketToken { get; }

        public AttributeListSyntax(
            SyntaxToken openBracketToken,
            AttributeTargetSpecifierSyntax? target,
            SeparatedSyntaxList<AttributeSyntax> attributes,
            SyntaxToken closeBracketToken)
            : base(SyntaxKind.AttributeList, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            Target = target;
            Attributes = attributes;
            CloseBracketToken = closeBracketToken;
        }
    }
    ///<summary>Represents an attribute target such as 'assembly:' or 'return:'</summary>
    public sealed class AttributeTargetSpecifierSyntax : SyntaxNode
    {
        public SyntaxToken Identifier { get; }
        public SyntaxToken ColonToken { get; }

        public AttributeTargetSpecifierSyntax(SyntaxToken identifier, SyntaxToken colonToken)
            : base(SyntaxKind.AttributeTargetSpecifier, NodeSpan.From(identifier.Span, colonToken.Span))
        {
            Identifier = identifier;
            ColonToken = colonToken;
        }
    }
    ///<summary>Represents an attribute name with optional arguments</summary>
    public sealed class AttributeSyntax : SyntaxNode
    {
        public NameSyntax Name { get; }
        public AttributeArgumentListSyntax? ArgumentList { get; }

        public AttributeSyntax(NameSyntax name, AttributeArgumentListSyntax? argumentList)
            : base(SyntaxKind.Attribute, NodeSpan.FromNonNull(name.Span, argumentList?.Span))
        {
            Name = name;
            ArgumentList = argumentList;
        }
    }
    ///<summary>Represents the parenthesized argument list of an attribute</summary>
    public sealed class AttributeArgumentListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<AttributeArgumentSyntax> Arguments { get; }
        public SyntaxToken CloseParenToken { get; }

        public AttributeArgumentListSyntax(
            SyntaxToken openParenToken,
            SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.AttributeArgumentList, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Arguments = arguments;
            CloseParenToken = closeParenToken;
        }
    }

    ///<summary>Represents an attribute argument with an optional 'name =' or 'name:' prefix</summary>
    public sealed class AttributeArgumentSyntax : SyntaxNode
    {
        public NameEqualsSyntax? NameEquals { get; }
        public NameColonSyntax? NameColon { get; }
        public ExpressionSyntax Expression { get; }

        public AttributeArgumentSyntax(NameEqualsSyntax? nameEquals, NameColonSyntax? nameColon, ExpressionSyntax expression)
            : base(SyntaxKind.AttributeArgument, NodeSpan.FromNonNull(nameEquals?.Span, nameColon?.Span, expression.Span))
        {
            NameEquals = nameEquals;
            NameColon = nameColon;
            Expression = expression;
        }
    }
    ///<summary>Represents a switch statement with brace-delimited sections</summary>
    public sealed class SwitchStatementSyntax : StatementSyntax
    {
        public SyntaxToken SwitchKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }

        public SyntaxToken OpenBraceToken { get; }
        public SyntaxList<SwitchSectionSyntax> Sections { get; }
        public SyntaxToken CloseBraceToken { get; }

        public SwitchStatementSyntax(
            SyntaxToken switchKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken,
            SyntaxToken openBraceToken,
            SyntaxList<SwitchSectionSyntax> sections,
            SyntaxToken closeBraceToken)
            : base(SyntaxKind.SwitchStatement, NodeSpan.From(switchKeyword.Span, closeBraceToken.Span))
        {
            SwitchKeyword = switchKeyword;
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
            OpenBraceToken = openBraceToken;
            Sections = sections;
            CloseBraceToken = closeBraceToken;
        }
    }
    ///<summary>Groups one or more switch labels with their statements</summary>
    public sealed class SwitchSectionSyntax : SyntaxNode
    {
        public SyntaxList<SwitchLabelSyntax> Labels { get; }
        public SyntaxList<StatementSyntax> Statements { get; }

        public SwitchSectionSyntax(SyntaxList<SwitchLabelSyntax> labels, SyntaxList<StatementSyntax> statements)
            : base(
                SyntaxKind.SwitchSection,
                ComputeSpan(labels, statements))
        {
            Labels = labels;
            Statements = statements;
        }
        private static TextSpan ComputeSpan(SyntaxList<SwitchLabelSyntax> labels, SyntaxList<StatementSyntax> statements)
        {
            if (labels.Count > 0)
            {
                var start = labels[0].Span;
                var end = statements.Count > 0 ? statements[statements.Count - 1].Span : labels[labels.Count - 1].Span;
                return NodeSpan.From(start, end);
            }

            if (statements.Count > 0)
                return NodeSpan.From(statements[0].Span, statements[statements.Count - 1].Span);

            return new TextSpan(0, 0);
        }
    }
    ///<summary>Base class for case, pattern case and default labels</summary>
    public abstract class SwitchLabelSyntax : SyntaxNode
    {
        protected SwitchLabelSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Represents a 'case expression:' label</summary>
    public sealed class CaseSwitchLabelSyntax : SwitchLabelSyntax
    {
        public SyntaxToken CaseKeyword { get; }
        public ExpressionSyntax Value { get; }
        public SyntaxToken ColonToken { get; }

        public CaseSwitchLabelSyntax(SyntaxToken caseKeyword, ExpressionSyntax value, SyntaxToken colonToken)
            : base(SyntaxKind.CaseSwitchLabel, NodeSpan.From(caseKeyword.Span, colonToken.Span))
        {
            CaseKeyword = caseKeyword;
            Value = value;
            ColonToken = colonToken;
        }
    }
    ///<summary>Represents a 'case pattern' label with an optional 'when' guard</summary>
    public sealed class CasePatternSwitchLabelSyntax : SwitchLabelSyntax
    {
        public SyntaxToken CaseKeyword { get; }
        public PatternSyntax Pattern { get; }
        public WhenClauseSyntax? WhenClause { get; }
        public SyntaxToken ColonToken { get; }

        public CasePatternSwitchLabelSyntax(
            SyntaxToken caseKeyword,
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SyntaxToken colonToken)
            : base(SyntaxKind.CasePatternSwitchLabel, NodeSpan.From(caseKeyword.Span, colonToken.Span))
        {
            CaseKeyword = caseKeyword;
            Pattern = pattern;
            WhenClause = whenClause;
            ColonToken = colonToken;
        }
    }
    ///<summary>Represents a 'default:' switch label</summary>
    public sealed class DefaultSwitchLabelSyntax : SwitchLabelSyntax
    {
        public SyntaxToken DefaultKeyword { get; }
        public SyntaxToken ColonToken { get; }

        public DefaultSwitchLabelSyntax(SyntaxToken defaultKeyword, SyntaxToken colonToken)
            : base(SyntaxKind.DefaultSwitchLabel, NodeSpan.From(defaultKeyword.Span, colonToken.Span))
        {
            DefaultKeyword = defaultKeyword;
            ColonToken = colonToken;
        }
    }
    ///<summary>Represents a switch expression applied to a governing expression</summary>
    public sealed class SwitchExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax GoverningExpression { get; }
        public SyntaxToken SwitchKeyword { get; }
        public SyntaxToken OpenBraceToken { get; }
        public SeparatedSyntaxList<SwitchExpressionArmSyntax> Arms { get; }
        public SyntaxToken CloseBraceToken { get; }

        public SwitchExpressionSyntax(
            ExpressionSyntax governingExpression,
            SyntaxToken switchKeyword,
            SyntaxToken openBraceToken,
            SeparatedSyntaxList<SwitchExpressionArmSyntax> arms,
            SyntaxToken closeBraceToken)
            : base(SyntaxKind.SwitchExpression, NodeSpan.From(governingExpression.Span, closeBraceToken.Span))
        {
            GoverningExpression = governingExpression;
            SwitchKeyword = switchKeyword;
            OpenBraceToken = openBraceToken;
            Arms = arms;
            CloseBraceToken = closeBraceToken;
        }
    }

    ///<summary>Represents a non-destructive mutation expression introduced by contextual 'with'</summary>
    public sealed class WithExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken WithKeyword { get; }
        public InitializerExpressionSyntax Initializer { get; }

        public WithExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken withKeyword,
            InitializerExpressionSyntax initializer)
            : base(SyntaxKind.WithExpression, NodeSpan.From(expression.Span, initializer.Span))
        {
            Expression = expression;
            WithKeyword = withKeyword;
            Initializer = initializer;
        }
    }
    ///<summary>Represents one switch expression arm with a pattern, optional guard and '=>' result</summary>
    public sealed class SwitchExpressionArmSyntax : SyntaxNode
    {
        public PatternSyntax Pattern { get; }
        public WhenClauseSyntax? WhenClause { get; }
        public SyntaxToken EqualsGreaterThanToken { get; }
        public ExpressionSyntax Expression { get; }
        public SwitchExpressionArmSyntax(
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SyntaxToken equalsGreaterThanToken,
            ExpressionSyntax expression)
            : base(
                SyntaxKind.SwitchExpressionArm,
                NodeSpan.FromNonNull(pattern.Span, whenClause?.Span, equalsGreaterThanToken.Span, expression.Span))
        {
            Pattern = pattern;
            WhenClause = whenClause;
            EqualsGreaterThanToken = equalsGreaterThanToken;
            Expression = expression;
        }
    }
    ///<summary>Represents a method-like declaration scoped to a block</summary>
    public sealed class LocalFunctionStatementSyntax : StatementSyntax
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax ReturnType { get; }
        public SyntaxToken Identifier { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public LocalFunctionStatementSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax parameterList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.LocalFunctionStatement,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    returnType.Span,
                    identifier.Span,
                    body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span)))
        {
            AttributeLists = attributeLists;
            Modifiers = modifiers;
            ReturnType = returnType;
            Identifier = identifier;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents the empty statement ';'</summary>
    public sealed class EmptyStatementSyntax : StatementSyntax
    {
        public SyntaxToken SemicolonToken { get; }

        public EmptyStatementSyntax(SyntaxToken semicolonToken)
            : base(SyntaxKind.EmptyStatement, semicolonToken.Span)
        {
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a 'try' block followed by catch clauses or a finally clause</summary>
    public sealed class TryStatementSyntax : StatementSyntax
    {
        public SyntaxToken TryKeyword { get; }
        public BlockSyntax Block { get; }
        public SyntaxList<CatchClauseSyntax> Catches { get; }
        public FinallyClauseSyntax? Finally { get; }

        public TryStatementSyntax(
            SyntaxToken tryKeyword,
            BlockSyntax block,
            SyntaxList<CatchClauseSyntax> catches,
            FinallyClauseSyntax? @finally)
            : base(
                SyntaxKind.TryStatement,
                NodeSpan.From(
                    tryKeyword.Span,
                    @finally?.Span ?? (catches.Count > 0 ? catches[catches.Count - 1].Span : block.Span)))
        {
            TryKeyword = tryKeyword;
            Block = block;
            Catches = catches;
            Finally = @finally;
        }
    }

    ///<summary>Represents a catch clause with optional declaration and filter</summary>
    public sealed class CatchClauseSyntax : SyntaxNode
    {
        public SyntaxToken CatchKeyword { get; }
        public CatchDeclarationSyntax? Declaration { get; }
        public CatchFilterClauseSyntax? Filter { get; }
        public BlockSyntax Block { get; }

        public CatchClauseSyntax(
            SyntaxToken catchKeyword,
            CatchDeclarationSyntax? declaration,
            CatchFilterClauseSyntax? filter,
            BlockSyntax block)
            : base(SyntaxKind.CatchClause, NodeSpan.From(catchKeyword.Span, block.Span))
        {
            CatchKeyword = catchKeyword;
            Declaration = declaration;
            Filter = filter;
            Block = block;
        }
    }

    ///<summary>Represents the exception type and optional identifier in a catch clause</summary>
    public sealed class CatchDeclarationSyntax : SyntaxNode
    {
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken Identifier { get; } // optional
        public SyntaxToken CloseParenToken { get; }

        public CatchDeclarationSyntax(
            SyntaxToken openParenToken,
            TypeSyntax type,
            SyntaxToken identifier,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.CatchDeclaration, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Type = type;
            Identifier = identifier;
            CloseParenToken = closeParenToken;
        }
    }

    ///<summary>Represents a parenthesized 'when' filter on a catch clause</summary>
    public sealed class CatchFilterClauseSyntax : SyntaxNode
    {
        public SyntaxToken WhenKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax FilterExpression { get; }
        public SyntaxToken CloseParenToken { get; }

        public CatchFilterClauseSyntax(
            SyntaxToken whenKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax filterExpression,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.CatchFilterClause, NodeSpan.From(whenKeyword.Span, closeParenToken.Span))
        {
            WhenKeyword = whenKeyword;
            OpenParenToken = openParenToken;
            FilterExpression = filterExpression;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a 'finally' block</summary>
    public sealed class FinallyClauseSyntax : SyntaxNode
    {
        public SyntaxToken FinallyKeyword { get; }
        public BlockSyntax Block { get; }

        public FinallyClauseSyntax(SyntaxToken finallyKeyword, BlockSyntax block)
            : base(SyntaxKind.FinallyClause, NodeSpan.From(finallyKeyword.Span, block.Span))
        {
            FinallyKeyword = finallyKeyword;
            Block = block;
        }
    }
    ///<summary>Represents 'goto label', 'goto case' or 'goto default'</summary>
    public sealed class GotoStatementSyntax : StatementSyntax
    {
        public SyntaxToken GotoKeyword { get; }
        public SyntaxToken CaseOrDefaultKeyword { get; } // 'case', 'default' or absent
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public GotoStatementSyntax(
            SyntaxKind kind,
            SyntaxToken gotoKeyword,
            SyntaxToken caseOrDefaultKeyword,
            ExpressionSyntax? expression,
            SyntaxToken semicolonToken)
            : base(kind, NodeSpan.From(gotoKeyword.Span, semicolonToken.Span))
        {
            GotoKeyword = gotoKeyword;
            CaseOrDefaultKeyword = caseOrDefaultKeyword;
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }

    ///<summary>Represents an identifier label followed by a statement</summary>
    public sealed class LabeledStatementSyntax : StatementSyntax
    {
        public SyntaxToken Identifier { get; }
        public SyntaxToken ColonToken { get; }
        public StatementSyntax Statement { get; }

        public LabeledStatementSyntax(SyntaxToken identifier, SyntaxToken colonToken, StatementSyntax statement)
            : base(SyntaxKind.LabeledStatement, NodeSpan.From(identifier.Span, statement.Span))
        {
            Identifier = identifier;
            ColonToken = colonToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a return statement with an optional value</summary>
    public sealed class ReturnStatementSyntax : StatementSyntax
    {
        public SyntaxToken ReturnKeyword { get; }
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public ReturnStatementSyntax(SyntaxToken returnKeyword, ExpressionSyntax? expression, SyntaxToken semicolonToken)
            : base(SyntaxKind.ReturnStatement, NodeSpan.FromNonNull(returnKeyword.Span, expression?.Span, semicolonToken.Span))
        {
            ReturnKeyword = returnKeyword;
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a 'break;' statement</summary>
    public sealed class BreakStatementSyntax : StatementSyntax
    {
        public SyntaxToken BreakKeyword { get; }
        public SyntaxToken SemicolonToken { get; }

        public BreakStatementSyntax(SyntaxToken breakKeyword, SyntaxToken semicolonToken)
            : base(SyntaxKind.BreakStatement, NodeSpan.From(breakKeyword.Span, semicolonToken.Span))
        {
            BreakKeyword = breakKeyword;
            SemicolonToken = semicolonToken;
        }
    }

    ///<summary>Represents a 'continue;' statement</summary>
    public sealed class ContinueStatementSyntax : StatementSyntax
    {
        public SyntaxToken ContinueKeyword { get; }
        public SyntaxToken SemicolonToken { get; }

        public ContinueStatementSyntax(SyntaxToken continueKeyword, SyntaxToken semicolonToken)
            : base(SyntaxKind.ContinueStatement, NodeSpan.From(continueKeyword.Span, semicolonToken.Span))
        {
            ContinueKeyword = continueKeyword;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a throw statement or expressionless rethrow</summary>
    public sealed class ThrowStatementSyntax : StatementSyntax
    {
        public SyntaxToken ThrowKeyword { get; }
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public ThrowStatementSyntax(SyntaxToken throwKeyword, ExpressionSyntax? expression, SyntaxToken semicolonToken)
            : base(SyntaxKind.ThrowStatement, NodeSpan.FromNonNull(throwKeyword.Span, expression?.Span, semicolonToken.Span))
        {
            ThrowKeyword = throwKeyword;
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a post-tested do-while loop</summary>
    public sealed class DoStatementSyntax : StatementSyntax
    {
        public SyntaxToken DoKeyword { get; }
        public StatementSyntax Statement { get; }
        public SyntaxToken WhileKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Condition { get; }
        public SyntaxToken CloseParenToken { get; }
        public SyntaxToken SemicolonToken { get; }

        public DoStatementSyntax(
            SyntaxToken doKeyword,
            StatementSyntax statement,
            SyntaxToken whileKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax condition,
            SyntaxToken closeParenToken,
            SyntaxToken semicolonToken)
            : base(SyntaxKind.DoStatement, NodeSpan.From(doKeyword.Span, semicolonToken.Span))
        {
            DoKeyword = doKeyword;
            Statement = statement;
            WhileKeyword = whileKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CloseParenToken = closeParenToken;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a pre-tested while loop</summary>
    public sealed class WhileStatementSyntax : StatementSyntax
    {
        public SyntaxToken WhileKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Condition { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public WhileStatementSyntax(
            SyntaxToken whileKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax condition,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.WhileStatement, NodeSpan.From(whileKeyword.Span, statement.Span))
        {
            WhileKeyword = whileKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }

    ///<summary>Represents a for loop with declaration or expression initializers, condition and incrementors</summary>
    public sealed class ForStatementSyntax : StatementSyntax
    {
        public SyntaxToken ForKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public VariableDeclarationSyntax? Declaration { get; }
        public SeparatedSyntaxList<ExpressionSyntax> Initializers { get; }

        public SyntaxToken FirstSemicolonToken { get; }
        public ExpressionSyntax? Condition { get; }
        public SyntaxToken SecondSemicolonToken { get; }
        public SeparatedSyntaxList<ExpressionSyntax> Incrementors { get; }

        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public ForStatementSyntax(
            SyntaxToken forKeyword,
            SyntaxToken openParenToken,
            VariableDeclarationSyntax? declaration,
            SeparatedSyntaxList<ExpressionSyntax> initializers,
            SyntaxToken firstSemicolonToken,
            ExpressionSyntax? condition,
            SyntaxToken secondSemicolonToken,
            SeparatedSyntaxList<ExpressionSyntax> incrementors,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.ForStatement, NodeSpan.From(forKeyword.Span, statement.Span))
        {
            ForKeyword = forKeyword;
            OpenParenToken = openParenToken;
            Declaration = declaration;
            Initializers = initializers;
            FirstSemicolonToken = firstSemicolonToken;
            Condition = condition;
            SecondSemicolonToken = secondSemicolonToken;
            Incrementors = incrementors;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a foreach loop with a typed identifier iteration variable</summary>
    public sealed class ForEachStatementSyntax : StatementSyntax
    {
        public SyntaxToken AwaitKeyword { get; } // optional
        public SyntaxToken ForEachKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken InKeyword { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public ForEachStatementSyntax(
            SyntaxToken awaitKeyword,
            SyntaxToken forEachKeyword,
            SyntaxToken openParenToken,
            TypeSyntax type,
            SyntaxToken identifier,
            SyntaxToken inKeyword,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.ForEachStatement, NodeSpan.FromNonNull(
                awaitKeyword.Span.Length != 0 ? awaitKeyword.Span : (TextSpan?)null,
                forEachKeyword.Span,
                statement.Span))
        {
            AwaitKeyword = awaitKeyword;
            ForEachKeyword = forEachKeyword;
            OpenParenToken = openParenToken;
            Type = type;
            Identifier = identifier;
            InKeyword = inKeyword;
            Expression = expression;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a foreach loop with a deconstruction or expression iteration variable</summary>
    public sealed class ForEachVariableStatementSyntax : StatementSyntax
    {
        public SyntaxToken AwaitKeyword { get; } // optional
        public SyntaxToken ForEachKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Variable { get; }
        public SyntaxToken InKeyword { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public ForEachVariableStatementSyntax(
            SyntaxToken awaitKeyword,
            SyntaxToken forEachKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax variable,
            SyntaxToken inKeyword,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.ForEachVariableStatement, NodeSpan.FromNonNull(
                awaitKeyword.Span.Length != 0 ? awaitKeyword.Span : (TextSpan?)null,
                forEachKeyword.Span,
                statement.Span))
        {
            AwaitKeyword = awaitKeyword;
            ForEachKeyword = forEachKeyword;
            OpenParenToken = openParenToken;
            Variable = variable;
            InKeyword = inKeyword;
            Expression = expression;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a resource scope using either a declaration or expression</summary>
    public sealed class UsingStatementSyntax : StatementSyntax
    {
        public SyntaxToken AwaitKeyword { get; } // optional
        public SyntaxToken UsingKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public VariableDeclarationSyntax? Declaration { get; }
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public UsingStatementSyntax(
            SyntaxToken awaitKeyword,
            SyntaxToken usingKeyword,
            SyntaxToken openParenToken,
            VariableDeclarationSyntax? declaration,
            ExpressionSyntax? expression,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.UsingStatement, NodeSpan.FromNonNull(
                awaitKeyword.Span.Length != 0 ? awaitKeyword.Span : (TextSpan?)null,
                usingKeyword.Span,
                statement.Span))
        {
            AwaitKeyword = awaitKeyword;
            UsingKeyword = usingKeyword;
            OpenParenToken = openParenToken;
            Declaration = declaration;
            Expression = expression;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents an 'unsafe' block</summary>
    public sealed class UnsafeStatementSyntax : StatementSyntax
    {
        public SyntaxToken UnsafeKeyword { get; }
        public BlockSyntax Block { get; }

        public UnsafeStatementSyntax(SyntaxToken unsafeKeyword, BlockSyntax block)
            : base(SyntaxKind.UnsafeStatement, NodeSpan.From(unsafeKeyword.Span, block.Span))
        {
            UnsafeKeyword = unsafeKeyword;
            Block = block;
        }
    }

    ///<summary>Represents a 'fixed' declaration and its embedded statement</summary>
    public sealed class FixedStatementSyntax : StatementSyntax
    {
        public SyntaxToken FixedKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public VariableDeclarationSyntax Declaration { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public FixedStatementSyntax(
            SyntaxToken fixedKeyword,
            SyntaxToken openParenToken,
            VariableDeclarationSyntax declaration,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.FixedStatement, NodeSpan.From(fixedKeyword.Span, statement.Span))
        {
            FixedKeyword = fixedKeyword;
            OpenParenToken = openParenToken;
            Declaration = declaration;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a lock statement</summary>
    public sealed class LockStatementSyntax : StatementSyntax
    {
        public SyntaxToken LockKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public LockStatementSyntax(
            SyntaxToken lockKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
            : base(SyntaxKind.LockStatement, NodeSpan.From(lockKeyword.Span, statement.Span))
        {
            LockKeyword = lockKeyword;
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }
    ///<summary>Represents a 'checked' or 'unchecked' block</summary>
    public sealed class CheckedStatementSyntax : StatementSyntax
    {
        public SyntaxToken Keyword { get; } // 'checked' or 'unchecked'
        public BlockSyntax Block { get; }

        public CheckedStatementSyntax(SyntaxKind kind, SyntaxToken keyword, BlockSyntax block)
            : base(kind, NodeSpan.From(keyword.Span, block.Span))
        {
            Keyword = keyword;
            Block = block;
        }
    }
    ///<summary>Represents a local declaration including 'using' and 'await using' forms</summary>
    public sealed class LocalDeclarationStatementSyntax : StatementSyntax
    {
        public SyntaxToken AwaitKeyword { get; } // optional
        public SyntaxToken UsingKeyword { get; } // optional
        public SyntaxTokenList Modifiers { get; }
        public VariableDeclarationSyntax Declaration { get; }
        public SyntaxToken SemicolonToken { get; }

        public LocalDeclarationStatementSyntax(
            SyntaxToken awaitKeyword,
            SyntaxToken usingKeyword,
            SyntaxTokenList modifiers,
            VariableDeclarationSyntax declaration,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.LocalDeclarationStatement,
                NodeSpan.FromNonNull(
                    awaitKeyword.Span.Length != 0 ? awaitKeyword.Span : (TextSpan?)null,
                    usingKeyword.Span.Length != 0 ? usingKeyword.Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    declaration.Span,
                    semicolonToken.Span))
        {
            AwaitKeyword = awaitKeyword;
            UsingKeyword = usingKeyword;
            Modifiers = modifiers;
            Declaration = declaration;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents 'yield return' or 'yield break'</summary>
    public sealed class YieldStatementSyntax : StatementSyntax
    {
        public SyntaxToken YieldKeyword { get; }
        public SyntaxToken ReturnOrBreakKeyword { get; } // 'return' or 'break'
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public YieldStatementSyntax(
            SyntaxKind kind,
            SyntaxToken yieldKeyword,
            SyntaxToken returnOrBreakKeyword,
            ExpressionSyntax? expression,
            SyntaxToken semicolonToken)
            : base(kind, NodeSpan.From(yieldKeyword.Span, semicolonToken.Span))
        {
            YieldKeyword = yieldKeyword;
            ReturnOrBreakKeyword = returnOrBreakKeyword;
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }
    // variable declaration
    ///<summary>Represents an initializer introduced by '='</summary>
    public sealed class EqualsValueClauseSyntax : SyntaxNode
    {
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Value { get; }

        public EqualsValueClauseSyntax(SyntaxToken equalsToken, ExpressionSyntax value)
            : base(SyntaxKind.EqualsValueClause, NodeSpan.From(equalsToken.Span, value.Span))
        {
            EqualsToken = equalsToken;
            Value = value;
        }
    }

    ///<summary>Represents one declared variable with optional brackets and initializer</summary>
    public sealed class VariableDeclaratorSyntax : SyntaxNode
    {
        public SyntaxToken Identifier { get; }
        public BracketedArgumentListSyntax? ArgumentList { get; }
        public EqualsValueClauseSyntax? Initializer { get; }

        public VariableDeclaratorSyntax(SyntaxToken identifier, BracketedArgumentListSyntax? argumentList, EqualsValueClauseSyntax? initializer)
            : base(SyntaxKind.VariableDeclarator,
                  NodeSpan.FromNonNull(identifier.Span, argumentList?.Span, initializer?.Span))
        {
            Identifier = identifier;
            ArgumentList = argumentList;
            Initializer = initializer;
        }
    }

    ///<summary>Represents a type followed by one or more variable declarators</summary>
    public sealed class VariableDeclarationSyntax : SyntaxNode
    {
        public TypeSyntax Type { get; }
        public SeparatedSyntaxList<VariableDeclaratorSyntax> Variables { get; }

        public VariableDeclarationSyntax(TypeSyntax type, SeparatedSyntaxList<VariableDeclaratorSyntax> variables)
            : base(SyntaxKind.VariableDeclaration,
                  variables.Count > 0
                      ? NodeSpan.From(type.Span, variables[variables.Count - 1].Span)
                      : type.Span)
        {
            Type = type;
            Variables = variables;
        }
    }
    // members
    ///<summary>Base class for block-scoped and file-scoped namespace declarations</summary>
    public abstract class BaseNamespaceDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxToken NamespaceKeyword { get; }
        public NameSyntax Name { get; }
        public SyntaxList<ExternAliasDirectiveSyntax> Externs { get; }
        public SyntaxList<UsingDirectiveSyntax> Usings { get; }
        public SyntaxList<MemberDeclarationSyntax> Members { get; }

        protected BaseNamespaceDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxKind kind,
            SyntaxToken namespaceKeyword,
            NameSyntax name,
            SyntaxList<ExternAliasDirectiveSyntax> externs,
            SyntaxList<UsingDirectiveSyntax> usings,
            SyntaxList<MemberDeclarationSyntax> members,
            TextSpan span)
            : base(kind, attributeLists, span)
        {
            NamespaceKeyword = namespaceKeyword;
            Name = name;
            Externs = externs;
            Usings = usings;
            Members = members;
        }
    }
    ///<summary>Represents a namespace declaration terminated by ';'</summary>
    public sealed class FileScopedNamespaceDeclarationSyntax : BaseNamespaceDeclarationSyntax
    {
        public SyntaxToken SemicolonToken { get; }

        public FileScopedNamespaceDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxToken namespaceKeyword,
            NameSyntax name,
            SyntaxToken semicolonToken,
            SyntaxList<ExternAliasDirectiveSyntax> externs,
            SyntaxList<UsingDirectiveSyntax> usings,
            SyntaxList<MemberDeclarationSyntax> members)
            : base(
                attributeLists,
                SyntaxKind.FileScopedNamespaceDeclaration,
                namespaceKeyword,
                name,
                externs,
                usings,
                members,
                NodeSpan.From(namespaceKeyword.Span, semicolonToken.Span))
        {
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a brace-delimited namespace declaration</summary>
    public sealed class NamespaceDeclarationSyntax : BaseNamespaceDeclarationSyntax
    {
        public SyntaxToken OpenBraceToken { get; }
        public SyntaxToken CloseBraceToken { get; }
        public SyntaxToken SemicolonToken { get; } // exists for recovery

        public NamespaceDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxToken namespaceKeyword,
            NameSyntax name,
            SyntaxToken openBraceToken,
            SyntaxList<ExternAliasDirectiveSyntax> externs,
            SyntaxList<UsingDirectiveSyntax> usings,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                SyntaxKind.NamespaceDeclaration,
                namespaceKeyword,
                name,
                externs,
                usings,
                members,
                NodeSpan.From(namespaceKeyword.Span, (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            OpenBraceToken = openBraceToken;
            CloseBraceToken = closeBraceToken;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a comma-separated type argument list in angle brackets</summary>
    public sealed class TypeArgumentListSyntax : SyntaxNode
    {
        public SyntaxToken LessThanToken { get; }
        public SeparatedSyntaxList<TypeSyntax> Arguments { get; }
        public SyntaxToken GreaterThanToken { get; }

        public TypeArgumentListSyntax(SyntaxToken lessThanToken, SeparatedSyntaxList<TypeSyntax> arguments, SyntaxToken greaterThanToken)
            : base(SyntaxKind.TypeArgumentList, NodeSpan.From(lessThanToken.Span, greaterThanToken.Span))
        {
            LessThanToken = lessThanToken;
            Arguments = arguments;
            GreaterThanToken = greaterThanToken;
        }
    }
    ///<summary>Represents an omitted argument in an unbound generic name</summary>
    public sealed class OmittedTypeArgumentSyntax : TypeSyntax
    {
        public SyntaxToken OmittedTypeArgumentToken { get; }

        public OmittedTypeArgumentSyntax(SyntaxToken omittedTypeArgumentToken)
            : base(SyntaxKind.OmittedTypeArgument, omittedTypeArgumentToken.Span)
        {
            OmittedTypeArgumentToken = omittedTypeArgumentToken;
        }
    }
    ///<summary>Represents an identifier followed by type arguments</summary>
    public sealed class GenericNameSyntax : SimpleNameSyntax
    {
        public SyntaxToken Identifier { get; }
        public TypeArgumentListSyntax TypeArgumentList { get; }
        public bool IsUnboundGenericName
        {
            get
            {
                var arguments = TypeArgumentList.Arguments;
                if (arguments.Count == 0)
                    return false;

                for (int i = 0; i < arguments.Count; i++)
                {
                    if (arguments[i] is not OmittedTypeArgumentSyntax)
                        return false;
                }

                return true;
            }
        }

        public GenericNameSyntax(SyntaxToken identifier, TypeArgumentListSyntax typeArgumentList)
            : base(SyntaxKind.GenericName, NodeSpan.From(identifier.Span, typeArgumentList.Span))
        {
            Identifier = identifier;
            TypeArgumentList = typeArgumentList;
        }
    }
    ///<summary>Base class for type declarations with a brace-delimited body</summary>
    public abstract class BaseTypeDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken OpenBraceToken { get; }
        public SyntaxList<MemberDeclarationSyntax> Members { get; }
        public SyntaxToken CloseBraceToken { get; }
        public SyntaxToken SemicolonToken { get; } // optional

        protected BaseTypeDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxKind kind,
            SyntaxTokenList modifiers,
            SyntaxToken identifier,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken,
            TextSpan span)
            : base(kind, attributeLists, span)
        {
            Modifiers = modifiers;
            Identifier = identifier;
            OpenBraceToken = openBraceToken;
            Members = members;
            CloseBraceToken = closeBraceToken;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a delegate type declaration</summary>
    public sealed class DelegateDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken DelegateKeyword { get; }
        public TypeSyntax ReturnType { get; }
        public SyntaxToken Identifier { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public ParameterListSyntax ParameterList { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public SyntaxToken SemicolonToken { get; }

        public DelegateDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken delegateKeyword,
            TypeSyntax returnType,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax parameterList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.DelegateDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    delegateKeyword.Span,
                    returnType.Span,
                    identifier.Span,
                    parameterList.Span,
                    semicolonToken.Span))
        {
            Modifiers = modifiers;
            DelegateKeyword = delegateKeyword;
            ReturnType = returnType;
            Identifier = identifier;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            ConstraintClauses = constraintClauses;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Base class for lambda expressions and anonymous methods</summary>
    public abstract class AnonymousFunctionExpressionSyntax : ExpressionSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public abstract BlockSyntax? Block { get; }
        public abstract ExpressionSyntax? ExpressionBody { get; }
        public SyntaxNode Body => (SyntaxNode?)Block ?? ExpressionBody!;

        public SyntaxToken AsyncKeyword
        {
            get
            {
                for (int i = 0; i < Modifiers.Count; i++)
                {
                    var modifier = Modifiers[i];
                    if (modifier.Kind == SyntaxKind.AsyncKeyword ||
                        (modifier.Kind == SyntaxKind.IdentifierToken && modifier.ContextualKind == SyntaxKind.AsyncKeyword))
                    {
                        return modifier;
                    }
                }

                return default;
            }
        }

        protected AnonymousFunctionExpressionSyntax(
            SyntaxKind kind,
            SyntaxTokenList modifiers,
            TextSpan span)
            : base(kind, span)
        {
            Modifiers = modifiers;
        }
    }
    ///<summary>Represents an anonymous method introduced by 'delegate'</summary>
    public sealed class AnonymousMethodExpressionSyntax : AnonymousFunctionExpressionSyntax
    {
        public SyntaxToken DelegateKeyword { get; }
        public ParameterListSyntax? ParameterList { get; }
        public override BlockSyntax Block { get; }
        public override ExpressionSyntax? ExpressionBody => null;

        public AnonymousMethodExpressionSyntax(
            SyntaxTokenList modifiers,
            SyntaxToken delegateKeyword,
            ParameterListSyntax? parameterList,
            BlockSyntax block)
            : base(
                SyntaxKind.AnonymousMethodExpression,
                modifiers,
                NodeSpan.FromNonNull(
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    delegateKeyword.Span,
                    parameterList?.Span,
                    block.Span))
        {
            DelegateKeyword = delegateKeyword;
            ParameterList = parameterList;
            Block = block;
        }
    }
    ///<summary>Base class for class, struct, interface, record and extension declarations</summary>
    public abstract class TypeDeclarationSyntax : BaseTypeDeclarationSyntax
    {
        public abstract SyntaxToken Keyword { get; }
        public abstract TypeParameterListSyntax? TypeParameterList { get; }
        public abstract ParameterListSyntax? ParameterList { get; }
        public abstract SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }

        protected TypeDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxKind kind,
            SyntaxTokenList modifiers,
            SyntaxToken identifier,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken,
            TextSpan span)
            : base(attributeLists, kind, modifiers, identifier, openBraceToken, members, closeBraceToken, semicolonToken, span)
        {
        }
    }
    ///<summary>Represents an extension block declaration</summary>
    public sealed class ExtensionBlockDeclarationSyntax : TypeDeclarationSyntax
    {
        public override SyntaxToken Keyword { get; }
        public override TypeParameterListSyntax? TypeParameterList { get; }
        public override ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList => null;
        public override SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }

        public ExtensionBlockDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken extensionKeyword,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax? parameterList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                SyntaxKind.ExtensionBlockDeclaration,
                modifiers,
                identifier: default,
                openBraceToken,
                members,
                closeBraceToken,
                semicolonToken,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    extensionKeyword.Span,
                    typeParameterList?.Span,
                    parameterList?.Span,
                    constraintClauses.Count > 0 ? constraintClauses[constraintClauses.Count - 1].Span : (TextSpan?)null,
                    openBraceToken.Span.Length != 0 ? openBraceToken.Span : (TextSpan?)null,
                    semicolonToken.Span.Length != 0
                        ? semicolonToken.Span
                        : closeBraceToken.Span.Length != 0 ? closeBraceToken.Span : (TextSpan?)null))
        {
            Keyword = extensionKeyword;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents a 'where T:' constraint clause</summary>
    public sealed class TypeParameterConstraintClauseSyntax : SyntaxNode
    {
        public SyntaxToken WhereKeyword { get; }
        public SyntaxToken Name { get; }
        public SyntaxToken ColonToken { get; }
        public SeparatedSyntaxList<TypeParameterConstraintSyntax> Constraints { get; }

        public TypeParameterConstraintClauseSyntax(
            SyntaxToken whereKeyword,
            SyntaxToken name,
            SyntaxToken colonToken,
            SeparatedSyntaxList<TypeParameterConstraintSyntax> constraints)
            : base(
                SyntaxKind.TypeParameterConstraintClause,
                constraints.Count > 0
                    ? NodeSpan.From(whereKeyword.Span, constraints[constraints.Count - 1].Span)
                    : NodeSpan.From(whereKeyword.Span, colonToken.Span))
        {
            WhereKeyword = whereKeyword;
            Name = name;
            ColonToken = colonToken;
            Constraints = constraints;
        }
    }
    ///<summary>Base class for constraints on a type parameter</summary>
    public abstract class TypeParameterConstraintSyntax : SyntaxNode
    {
        protected TypeParameterConstraintSyntax(SyntaxKind kind, TextSpan span)
            : base(kind, span)
        {
        }
    }
    ///<summary>Represents a 'class', 'class?' or 'struct' constraint</summary>
    public sealed class ClassOrStructConstraintSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken ClassOrStructKeyword { get; } // 'class' or 'struct'
        public SyntaxToken QuestionToken { get; } // optional

        public ClassOrStructConstraintSyntax(
            SyntaxKind kind,
            SyntaxToken classOrStructKeyword,
            SyntaxToken questionToken)
            : base(
                kind,
                questionToken.Span.Length != 0
                    ? NodeSpan.From(classOrStructKeyword.Span, questionToken.Span)
                    : classOrStructKeyword.Span)
        {
            ClassOrStructKeyword = classOrStructKeyword;
            QuestionToken = questionToken;
        }
    }

    ///<summary>Represents an 'allows' anti-constraint clause</summary>
    public sealed class AllowsConstraintClauseSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken AllowsKeyword { get; }
        public SeparatedSyntaxList<AllowsConstraintSyntax> Constraints { get; }

        public AllowsConstraintClauseSyntax(
            SyntaxToken allowsKeyword,
            SeparatedSyntaxList<AllowsConstraintSyntax> constraints)
            : base(
                SyntaxKind.AllowsConstraintClause,
                constraints.Count > 0
                    ? NodeSpan.From(allowsKeyword.Span, constraints[constraints.Count - 1].Span)
                    : allowsKeyword.Span)
        {
            AllowsKeyword = allowsKeyword;
            Constraints = constraints;
        }
    }

    ///<summary>Base class for constraints following 'allows'</summary>
    public abstract class AllowsConstraintSyntax : SyntaxNode
    {
        protected AllowsConstraintSyntax(SyntaxKind kind, TextSpan span)
            : base(kind, span)
        {
        }
    }

    ///<summary>Represents the 'ref struct' anti-constraint</summary>
    public sealed class RefStructConstraintSyntax : AllowsConstraintSyntax
    {
        public SyntaxToken RefKeyword { get; }
        public SyntaxToken StructKeyword { get; }

        public RefStructConstraintSyntax(
            SyntaxToken refKeyword,
            SyntaxToken structKeyword)
            : base(SyntaxKind.RefStructConstraint, NodeSpan.From(refKeyword.Span, structKeyword.Span))
        {
            RefKeyword = refKeyword;
            StructKeyword = structKeyword;
        }
    }
    ///<summary>Represents a type used as a generic parameter constraint</summary>
    public sealed class TypeConstraintSyntax : TypeParameterConstraintSyntax
    {
        public TypeSyntax Type { get; }

        public TypeConstraintSyntax(TypeSyntax type)
            : base(SyntaxKind.TypeConstraint, type.Span)
        {
            Type = type;
        }
    }
    ///<summary>Represents the 'new()' generic parameter constraint</summary>
    public sealed class ConstructorConstraintSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public SyntaxToken CloseParenToken { get; }

        public ConstructorConstraintSyntax(
            SyntaxToken newKeyword,
            SyntaxToken openParenToken,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.ConstructorConstraint, NodeSpan.From(newKeyword.Span, closeParenToken.Span))
        {
            NewKeyword = newKeyword;
            OpenParenToken = openParenToken;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents the 'default' constraint on an override or explicit implementation</summary>
    public sealed class DefaultConstraintSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken DefaultKeyword { get; }

        public DefaultConstraintSyntax(SyntaxToken defaultKeyword)
            : base(SyntaxKind.DefaultConstraint, defaultKeyword.Span)
        {
            DefaultKeyword = defaultKeyword;
        }
    }
    ///<summary>Represents a class header, optional bases and constraints, and member body</summary>
    public sealed class ClassDeclarationSyntax : TypeDeclarationSyntax
    {
        public override SyntaxToken Keyword { get; }
        public override TypeParameterListSyntax? TypeParameterList { get; }
        public override ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public override SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public ClassDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken classKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax? parameterList,
            BaseListSyntax? baseList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                SyntaxKind.ClassDeclaration,
                modifiers,
                identifier,
                openBraceToken,
                members,
                closeBraceToken,
                semicolonToken,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    classKeyword.Span,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            Keyword = classKeyword;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents a struct header, optional interfaces and constraints, and member body</summary>
    public sealed class StructDeclarationSyntax : TypeDeclarationSyntax
    {
        public override SyntaxToken Keyword { get; }
        public override TypeParameterListSyntax? TypeParameterList { get; }
        public override ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public override SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public StructDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken structKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax? parameterList,
            BaseListSyntax? baseList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                SyntaxKind.StructDeclaration,
                modifiers,
                identifier,
                openBraceToken,
                members,
                closeBraceToken,
                semicolonToken,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    structKeyword.Span,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            Keyword = structKeyword;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents an interface header, optional bases and constraints, and member body</summary>
    public sealed class InterfaceDeclarationSyntax : TypeDeclarationSyntax
    {
        public override SyntaxToken Keyword { get; }
        public override TypeParameterListSyntax? TypeParameterList { get; }
        public override ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public override SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public InterfaceDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken interfaceKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax? parameterList,
            BaseListSyntax? baseList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                SyntaxKind.InterfaceDeclaration,
                modifiers,
                identifier,
                openBraceToken,
                members,
                closeBraceToken,
                semicolonToken,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    interfaceKeyword.Span,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            Keyword = interfaceKeyword;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents a record header with optional kind, primary parameters, bases, constraints and body</summary>
    public sealed class RecordDeclarationSyntax : TypeDeclarationSyntax
    {
        public override SyntaxToken Keyword { get; } // 'record'
        public SyntaxToken ClassOrStructKeyword { get; } // 'class', 'struct' or absent
        public override TypeParameterListSyntax? TypeParameterList { get; }
        public override ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public override SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }

        public RecordDeclarationSyntax(
            SyntaxKind kind,
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken recordKeyword,
            SyntaxToken classOrStructKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax? parameterList,
            BaseListSyntax? baseList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            SyntaxToken openBraceToken,
            SyntaxList<MemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                attributeLists,
                kind,
                modifiers,
                identifier,
                openBraceToken,
                members,
                closeBraceToken,
                semicolonToken,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    recordKeyword.Span,
                    (classOrStructKeyword.Span.Length != 0 ? classOrStructKeyword.Span : (TextSpan?)null),
                    identifier.Span,
                    typeParameterList?.Span,
                    parameterList?.Span,
                    baseList?.Span,
                    constraintClauses.Count > 0 ? constraintClauses[constraintClauses.Count - 1].Span : (TextSpan?)null,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            Keyword = recordKeyword;
            ClassOrStructKeyword = classOrStructKeyword;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents one enum member with an optional constant value</summary>
    public sealed class EnumMemberDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxToken Identifier { get; }
        public EqualsValueClauseSyntax? EqualsValue { get; }

        public EnumMemberDeclarationSyntax(SyntaxList<AttributeListSyntax> attributeLists, SyntaxToken identifier, EqualsValueClauseSyntax? equalsValue)
            : base(SyntaxKind.EnumMemberDeclaration,
                  attributeLists,
                  NodeSpan.FromNonNull(attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null, identifier.Span, equalsValue?.Span))
        {
            Identifier = identifier;
            EqualsValue = equalsValue;
        }
    }
    ///<summary>Represents an enum declaration and its comma-separated members</summary>
    public sealed class EnumDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken EnumKeyword { get; }
        public SyntaxToken Identifier { get; }
        public BaseListSyntax? BaseList { get; }

        public SyntaxToken OpenBraceToken { get; }
        public SeparatedSyntaxList<EnumMemberDeclarationSyntax> Members { get; }
        public SyntaxToken CloseBraceToken { get; }
        public SyntaxToken SemicolonToken { get; } // optional
        public EnumDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken enumKeyword,
            SyntaxToken identifier,
            BaseListSyntax? baseList,
            SyntaxToken openBraceToken,
            SeparatedSyntaxList<EnumMemberDeclarationSyntax> members,
            SyntaxToken closeBraceToken,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.EnumDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    enumKeyword.Span,
                    identifier.Span,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : closeBraceToken.Span)))
        {
            Modifiers = modifiers;
            EnumKeyword = enumKeyword;
            Identifier = identifier;
            BaseList = baseList;
            OpenBraceToken = openBraceToken;
            Members = members;
            CloseBraceToken = closeBraceToken;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a comma-separated type parameter list in angle brackets</summary>
    public sealed class TypeParameterListSyntax : SyntaxNode
    {
        public SyntaxToken LessThanToken { get; }
        public SeparatedSyntaxList<TypeParameterSyntax> Parameters { get; }
        public SyntaxToken GreaterThanToken { get; }

        public TypeParameterListSyntax(
            SyntaxToken lessThanToken,
            SeparatedSyntaxList<TypeParameterSyntax> parameters,
            SyntaxToken greaterThanToken)
            : base(SyntaxKind.TypeParameterList, NodeSpan.From(lessThanToken.Span, greaterThanToken.Span))
        {
            LessThanToken = lessThanToken;
            Parameters = parameters;
            GreaterThanToken = greaterThanToken;
        }
    }
    ///<summary>Represents one type parameter with optional attributes and variance</summary>
    public sealed class TypeParameterSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxToken VarianceKeyword { get; } // 'in', 'out' or absent
        public SyntaxToken Identifier { get; }

        public TypeParameterSyntax(SyntaxList<AttributeListSyntax> attributeLists, SyntaxToken varianceKeyword, SyntaxToken identifier)
            : base(SyntaxKind.TypeParameter, NodeSpan.FromNonNull(
                attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                varianceKeyword.Span.Length != 0 ? varianceKeyword.Span : (TextSpan?)null,
                identifier.Span))
        {
            AttributeLists = attributeLists;
            VarianceKeyword = varianceKeyword;
            Identifier = identifier;
        }
    }
    ///<summary>Represents one parameter with optional attributes, modifiers, type and default value</summary>
    public sealed class ParameterSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax? Type { get; }
        public SyntaxToken Identifier { get; }
        public EqualsValueClauseSyntax? Default { get; }

        public ParameterSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax? type,
            SyntaxToken identifier,
            EqualsValueClauseSyntax? @default)
            : base(
                SyntaxKind.Parameter,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    type?.Span,
                    identifier.Kind != SyntaxKind.None ? identifier.Span : (TextSpan?)null,
                    @default?.Span))
        {
            AttributeLists = attributeLists;
            Modifiers = modifiers;
            Type = type;
            Identifier = identifier;
            Default = @default;
        }
    }

    ///<summary>Represents a comma-separated parameter list in parentheses</summary>
    public sealed class ParameterListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }
        public SyntaxToken CloseParenToken { get; }

        public ParameterListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<ParameterSyntax> parameters, SyntaxToken closeParenToken)
            : base(SyntaxKind.ParameterList, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Parameters = parameters;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents an indexer parameter list in brackets</summary>
    public sealed class BracketedParameterListSyntax : SyntaxNode
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<ParameterSyntax> Parameters { get; }
        public SyntaxToken CloseBracketToken { get; }

        public BracketedParameterListSyntax(
            SyntaxToken openBracketToken,
            SeparatedSyntaxList<ParameterSyntax> parameters,
            SyntaxToken closeBracketToken)
            : base(SyntaxKind.BracketedParameterList, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            Parameters = parameters;
            CloseBracketToken = closeBracketToken;
        }
    }
    ///<summary>Represents a method declaration with block, expression or semicolon body</summary>
    public sealed class MethodDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax ReturnType { get; }
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken Identifier { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public MethodDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
            ParameterListSyntax parameterList,
            SyntaxList<TypeParameterConstraintClauseSyntax> constraintClauses,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.MethodDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    returnType.Span,
                    explicitInterfaceSpecifier?.Span,
                    identifier.Span,
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            ReturnType = returnType;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            Identifier = identifier;
            TypeParameterList = typeParameterList;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
            ConstraintClauses = constraintClauses;
        }
    }
    ///<summary>Represents an overloaded operator declaration</summary>
    public sealed class OperatorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax ReturnType { get; }
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken OperatorKeyword { get; }
        public SyntaxToken CheckedKeyword { get; } // optional
        // '+', '-', '!', '~', '++', '--', 'true', 'false', '*', '/', '%', '&', '|', '^'
        // '<<', '>>', '>>>', '==', '!=', '<', '>', '<=', '>='
        // '+=', '-=', '*=', '/=', '%=', '&=', '|=', '^=', '<<=', '>>=' or '>>>='
        public SyntaxToken OperatorToken { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public OperatorDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken operatorKeyword,
            SyntaxToken checkedKeyword,
            SyntaxToken operatorToken,
            ParameterListSyntax parameterList,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.OperatorDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    returnType.Span,
                    explicitInterfaceSpecifier?.Span,
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            ReturnType = returnType;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            OperatorKeyword = operatorKeyword;
            CheckedKeyword = checkedKeyword;
            OperatorToken = operatorToken;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents an implicit or explicit conversion operator declaration</summary>
    public sealed class ConversionOperatorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken ImplicitOrExplicitKeyword { get; } // 'implicit' or 'explicit'
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken OperatorKeyword { get; }
        public SyntaxToken CheckedKeyword { get; } // optional
        public TypeSyntax Type { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public ConversionOperatorDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken implicitOrExplicitKeyword,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken operatorKeyword,
            SyntaxToken checkedKeyword,
            TypeSyntax type,
            ParameterListSyntax parameterList,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.ConversionOperatorDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    implicitOrExplicitKeyword.Span,
                    explicitInterfaceSpecifier?.Span,
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            ImplicitOrExplicitKeyword = implicitOrExplicitKeyword;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            OperatorKeyword = operatorKeyword;
            CheckedKeyword = checkedKeyword;
            Type = type;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a constructor initializer targeting 'this' or 'base'</summary>
    public sealed class ConstructorInitializerSyntax : SyntaxNode
    {
        public SyntaxToken ColonToken { get; }
        public SyntaxToken ThisOrBaseKeyword { get; } // 'this' or 'base'
        public ArgumentListSyntax ArgumentList { get; }

        public ConstructorInitializerSyntax(
            SyntaxKind kind,
            SyntaxToken colonToken,
            SyntaxToken thisOrBaseKeyword,
            ArgumentListSyntax argumentList)
            : base(kind, NodeSpan.From(colonToken.Span, argumentList.Span))
        {
            ColonToken = colonToken;
            ThisOrBaseKeyword = thisOrBaseKeyword;
            ArgumentList = argumentList;
        }
    }
    ///<summary>Represents an instance or static constructor declaration</summary>
    public sealed class ConstructorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken Identifier { get; }
        public ParameterListSyntax ParameterList { get; }
        public ConstructorInitializerSyntax? Initializer { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public ConstructorDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken identifier,
            ParameterListSyntax parameterList,
            ConstructorInitializerSyntax? initializer,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.ConstructorDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    identifier.Span,
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            Identifier = identifier;
            ParameterList = parameterList;
            Initializer = initializer;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a destructor declaration</summary>
    public sealed class DestructorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken TildeToken { get; }
        public SyntaxToken Identifier { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public DestructorDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken tildeToken,
            SyntaxToken identifier,
            ParameterListSyntax parameterList,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.DestructorDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    tildeToken.Span,
                    identifier.Span,
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            TildeToken = tildeToken;
            Identifier = identifier;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a base type followed by primary constructor arguments</summary>
    public sealed class PrimaryConstructorBaseTypeSyntax : BaseTypeSyntax
    {
        public override TypeSyntax Type { get; }
        public ArgumentListSyntax ArgumentList { get; }

        public PrimaryConstructorBaseTypeSyntax(TypeSyntax type, ArgumentListSyntax argumentList)
            : base(SyntaxKind.PrimaryConstructorBaseType, NodeSpan.From(type.Span, argumentList.Span))
        {
            Type = type;
            ArgumentList = argumentList;
        }
    }
    ///<summary>Represents field modifiers, variable declaration and terminating semicolon</summary>
    public sealed class FieldDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public VariableDeclarationSyntax Declaration { get; }
        public SyntaxToken SemicolonToken { get; }

        public FieldDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            VariableDeclarationSyntax declaration,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.FieldDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    declaration.Span,
                    semicolonToken.Span))
        {
            Modifiers = modifiers;
            Declaration = declaration;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a field-like event declaration</summary>
    public sealed class EventFieldDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken EventKeyword { get; }
        public VariableDeclarationSyntax Declaration { get; }
        public SyntaxToken SemicolonToken { get; }

        public EventFieldDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken eventKeyword,
            VariableDeclarationSyntax declaration,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.EventFieldDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    eventKeyword.Span,
                    declaration.Span,
                    semicolonToken.Span))
        {
            Modifiers = modifiers;
            EventKeyword = eventKeyword;
            Declaration = declaration;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents an if statement with an optional else clause</summary>
    public sealed class IfStatementSyntax : StatementSyntax
    {
        public SyntaxToken IfKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Condition { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }
        public ElseClauseSyntax? Else { get; }

        public IfStatementSyntax(
        SyntaxToken ifKeyword,
        SyntaxToken openParenToken,
        ExpressionSyntax condition,
        SyntaxToken closeParenToken,
        StatementSyntax statement,
        ElseClauseSyntax? @else)
        : base(
            SyntaxKind.IfStatement,
            NodeSpan.From(ifKeyword.Span, (@else != null ? @else.Span : statement.Span)))
        {
            IfKeyword = ifKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CloseParenToken = closeParenToken;
            Statement = statement;
            Else = @else;
        }
    }

    ///<summary>Represents the 'else' branch of an if statement</summary>
    public sealed class ElseClauseSyntax : SyntaxNode
    {
        public SyntaxToken ElseKeyword { get; }
        public StatementSyntax Statement { get; }

        public ElseClauseSyntax(SyntaxToken elseKeyword, StatementSyntax statement)
            : base(SyntaxKind.ElseClause, NodeSpan.From(elseKeyword.Span, statement.Span))
        {
            ElseKeyword = elseKeyword;
            Statement = statement;
        }
    }

    // =expression nodes=
    ///<summary>Base class for clauses in a query body</summary>
    public abstract class QueryClauseSyntax : SyntaxNode
    {
        protected QueryClauseSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Base class for the terminal select or group clause of a query body</summary>
    public abstract class SelectOrGroupClauseSyntax : SyntaxNode
    {
        protected SelectOrGroupClauseSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Represents a query expression beginning with a from clause</summary>
    public sealed class QueryExpressionSyntax : ExpressionSyntax
    {
        public FromClauseSyntax FromClause { get; }
        public QueryBodySyntax Body { get; }

        public QueryExpressionSyntax(FromClauseSyntax fromClause, QueryBodySyntax body)
            : base(SyntaxKind.QueryExpression, NodeSpan.From(fromClause.Span, body.Span))
        {
            FromClause = fromClause;
            Body = body;
        }
    }

    ///<summary>Represents the clauses, terminal clause and optional continuation of a query</summary>
    public sealed class QueryBodySyntax : SyntaxNode
    {
        public SyntaxList<QueryClauseSyntax> Clauses { get; }
        public SelectOrGroupClauseSyntax SelectOrGroup { get; }
        public QueryContinuationSyntax? Continuation { get; }

        public QueryBodySyntax(
            SyntaxList<QueryClauseSyntax> clauses,
            SelectOrGroupClauseSyntax selectOrGroup,
            QueryContinuationSyntax? continuation)
            : base(
                SyntaxKind.QueryBody,
                NodeSpan.From(
                    clauses.Count > 0 ? clauses[0].Span : selectOrGroup.Span,
                    continuation?.Span ?? selectOrGroup.Span))
        {
            Clauses = clauses;
            SelectOrGroup = selectOrGroup;
            Continuation = continuation;
        }
    }

    ///<summary>Represents a from clause</summary>
    public sealed class FromClauseSyntax : QueryClauseSyntax
    {
        public SyntaxToken FromKeyword { get; }
        public TypeSyntax? Type { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken InKeyword { get; }
        public ExpressionSyntax Expression { get; }

        public FromClauseSyntax(
            SyntaxToken fromKeyword,
            TypeSyntax? type,
            SyntaxToken identifier,
            SyntaxToken inKeyword,
            ExpressionSyntax expression)
            : base(SyntaxKind.FromClause, NodeSpan.From(fromKeyword.Span, expression.Span))
        {
            FromKeyword = fromKeyword;
            Type = type;
            Identifier = identifier;
            InKeyword = inKeyword;
            Expression = expression;
        }
    }

    ///<summary>Represents a let clause</summary>
    public sealed class LetClauseSyntax : QueryClauseSyntax
    {
        public SyntaxToken LetKeyword { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken EqualsToken { get; }
        public ExpressionSyntax Expression { get; }

        public LetClauseSyntax(
            SyntaxToken letKeyword,
            SyntaxToken identifier,
            SyntaxToken equalsToken,
            ExpressionSyntax expression)
            : base(SyntaxKind.LetClause, NodeSpan.From(letKeyword.Span, expression.Span))
        {
            LetKeyword = letKeyword;
            Identifier = identifier;
            EqualsToken = equalsToken;
            Expression = expression;
        }
    }

    ///<summary>Represents a join clause</summary>
    public sealed class JoinClauseSyntax : QueryClauseSyntax
    {
        public SyntaxToken JoinKeyword { get; }
        public TypeSyntax? Type { get; }
        public SyntaxToken Identifier { get; }
        public SyntaxToken InKeyword { get; }
        public ExpressionSyntax InExpression { get; }
        public SyntaxToken OnKeyword { get; }
        public ExpressionSyntax LeftExpression { get; }
        public SyntaxToken EqualsKeyword { get; }
        public ExpressionSyntax RightExpression { get; }
        public JoinIntoClauseSyntax? Into { get; }

        public JoinClauseSyntax(
            SyntaxToken joinKeyword,
            TypeSyntax? type,
            SyntaxToken identifier,
            SyntaxToken inKeyword,
            ExpressionSyntax inExpression,
            SyntaxToken onKeyword,
            ExpressionSyntax leftExpression,
            SyntaxToken equalsKeyword,
            ExpressionSyntax rightExpression,
            JoinIntoClauseSyntax? into)
            : base(
                SyntaxKind.JoinClause,
                NodeSpan.From(joinKeyword.Span, into?.Span ?? rightExpression.Span))
        {
            JoinKeyword = joinKeyword;
            Type = type;
            Identifier = identifier;
            InKeyword = inKeyword;
            InExpression = inExpression;
            OnKeyword = onKeyword;
            LeftExpression = leftExpression;
            EqualsKeyword = equalsKeyword;
            RightExpression = rightExpression;
            Into = into;
        }
    }

    ///<summary>Represents the optional into part of a join clause</summary>
    public sealed class JoinIntoClauseSyntax : SyntaxNode
    {
        public SyntaxToken IntoKeyword { get; }
        public SyntaxToken Identifier { get; }

        public JoinIntoClauseSyntax(SyntaxToken intoKeyword, SyntaxToken identifier)
            : base(SyntaxKind.JoinIntoClause, NodeSpan.From(intoKeyword.Span, identifier.Span))
        {
            IntoKeyword = intoKeyword;
            Identifier = identifier;
        }
    }

    ///<summary>Represents a where clause</summary>
    public sealed class WhereClauseSyntax : QueryClauseSyntax
    {
        public SyntaxToken WhereKeyword { get; }
        public ExpressionSyntax Condition { get; }

        public WhereClauseSyntax(SyntaxToken whereKeyword, ExpressionSyntax condition)
            : base(SyntaxKind.WhereClause, NodeSpan.From(whereKeyword.Span, condition.Span))
        {
            WhereKeyword = whereKeyword;
            Condition = condition;
        }
    }

    ///<summary>Represents an orderby clause</summary>
    public sealed class OrderByClauseSyntax : QueryClauseSyntax
    {
        public SyntaxToken OrderByKeyword { get; }
        public SeparatedSyntaxList<OrderingSyntax> Orderings { get; }

        public OrderByClauseSyntax(
            SyntaxToken orderByKeyword,
            SeparatedSyntaxList<OrderingSyntax> orderings)
            : base(
                SyntaxKind.OrderByClause,
                orderings.Count > 0
                    ? NodeSpan.From(orderByKeyword.Span, orderings[orderings.Count - 1].Span)
                    : orderByKeyword.Span)
        {
            OrderByKeyword = orderByKeyword;
            Orderings = orderings;
        }
    }

    ///<summary>Represents one ordering expression and its optional direction</summary>
    public sealed class OrderingSyntax : SyntaxNode
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken AscendingOrDescendingKeyword { get; }

        public OrderingSyntax(
            SyntaxKind kind,
            ExpressionSyntax expression,
            SyntaxToken ascendingOrDescendingKeyword)
            : base(
                kind,
                ascendingOrDescendingKeyword.Kind != SyntaxKind.None
                    ? NodeSpan.From(expression.Span, ascendingOrDescendingKeyword.Span)
                    : expression.Span)
        {
            Expression = expression;
            AscendingOrDescendingKeyword = ascendingOrDescendingKeyword;
        }
    }

    ///<summary>Represents a select clause</summary>
    public sealed class SelectClauseSyntax : SelectOrGroupClauseSyntax
    {
        public SyntaxToken SelectKeyword { get; }
        public ExpressionSyntax Expression { get; }

        public SelectClauseSyntax(SyntaxToken selectKeyword, ExpressionSyntax expression)
            : base(SyntaxKind.SelectClause, NodeSpan.From(selectKeyword.Span, expression.Span))
        {
            SelectKeyword = selectKeyword;
            Expression = expression;
        }
    }

    ///<summary>Represents a group clause</summary>
    public sealed class GroupClauseSyntax : SelectOrGroupClauseSyntax
    {
        public SyntaxToken GroupKeyword { get; }
        public ExpressionSyntax GroupExpression { get; }
        public SyntaxToken ByKeyword { get; }
        public ExpressionSyntax ByExpression { get; }

        public GroupClauseSyntax(
            SyntaxToken groupKeyword,
            ExpressionSyntax groupExpression,
            SyntaxToken byKeyword,
            ExpressionSyntax byExpression)
            : base(SyntaxKind.GroupClause, NodeSpan.From(groupKeyword.Span, byExpression.Span))
        {
            GroupKeyword = groupKeyword;
            GroupExpression = groupExpression;
            ByKeyword = byKeyword;
            ByExpression = byExpression;
        }
    }

    ///<summary>Represents an into continuation and its following query body</summary>
    public sealed class QueryContinuationSyntax : SyntaxNode
    {
        public SyntaxToken IntoKeyword { get; }
        public SyntaxToken Identifier { get; }
        public QueryBodySyntax Body { get; }

        public QueryContinuationSyntax(
            SyntaxToken intoKeyword,
            SyntaxToken identifier,
            QueryBodySyntax body)
            : base(SyntaxKind.QueryContinuation, NodeSpan.From(intoKeyword.Span, body.Span))
        {
            IntoKeyword = intoKeyword;
            Identifier = identifier;
            Body = body;
        }
    }

    ///<summary>Represents an interpolated string and its ordered content segments</summary>
    public sealed class InterpolatedStringExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken StringStartToken { get; }
        public SyntaxList<InterpolatedStringContentSyntax> Contents { get; }
        public SyntaxToken StringEndToken { get; }

        public InterpolatedStringExpressionSyntax(
            SyntaxToken stringStartToken,
            SyntaxList<InterpolatedStringContentSyntax> contents,
            SyntaxToken stringEndToken)
            : base(SyntaxKind.InterpolatedStringExpression, NodeSpan.From(stringStartToken.Span, stringEndToken.Span))
        {
            StringStartToken = stringStartToken;
            Contents = contents;
            StringEndToken = stringEndToken;
        }
    }
    ///<summary>Represents a raw text segment inside an interpolated string</summary>
    public sealed class InterpolatedStringTextSyntax : InterpolatedStringContentSyntax
    {
        public SyntaxToken TextToken { get; }

        public InterpolatedStringTextSyntax(SyntaxToken textToken)
            : base(SyntaxKind.InterpolatedStringText, textToken.Span)
        {
            TextToken = textToken;
        }
    }
    ///<summary>Represents an interpolation with optional alignment and format clauses</summary>
    public sealed class InterpolationSyntax : InterpolatedStringContentSyntax
    {
        public SyntaxToken OpenBraceToken { get; }
        public ExpressionSyntax Expression { get; }
        public InterpolationAlignmentClauseSyntax? AlignmentClause { get; }
        public InterpolationFormatClauseSyntax? FormatClause { get; }
        public SyntaxToken CloseBraceToken { get; }

        public InterpolationSyntax(
            SyntaxToken openBraceToken,
            ExpressionSyntax expression,
            InterpolationAlignmentClauseSyntax? alignmentClause,
            InterpolationFormatClauseSyntax? formatClause,
            SyntaxToken closeBraceToken)
            : base(SyntaxKind.Interpolation, NodeSpan.From(openBraceToken.Span, closeBraceToken.Span))
        {
            OpenBraceToken = openBraceToken;
            Expression = expression;
            AlignmentClause = alignmentClause;
            FormatClause = formatClause;
            CloseBraceToken = closeBraceToken;
        }
    }
    ///<summary>Represents the comma-prefixed alignment expression of an interpolation</summary>
    public sealed class InterpolationAlignmentClauseSyntax : SyntaxNode
    {
        public SyntaxToken CommaToken { get; }
        public ExpressionSyntax Value { get; }

        public InterpolationAlignmentClauseSyntax(SyntaxToken commaToken, ExpressionSyntax value)
            : base(SyntaxKind.InterpolationAlignmentClause, NodeSpan.From(commaToken.Span, value.Span))
        {
            CommaToken = commaToken;
            Value = value;
        }
    }
    ///<summary>Represents the colon-prefixed format text of an interpolation</summary>
    public sealed class InterpolationFormatClauseSyntax : SyntaxNode
    {
        public SyntaxToken ColonToken { get; }
        public SyntaxToken FormatStringToken { get; } // InterpolatedStringTextToken

        public InterpolationFormatClauseSyntax(SyntaxToken colonToken, SyntaxToken formatStringToken)
            : base(SyntaxKind.InterpolationFormatClause, NodeSpan.From(colonToken.Span, formatStringToken.Span))
        {
            ColonToken = colonToken;
            FormatStringToken = formatStringToken;
        }
    }
    ///<summary>Represents an expression enclosed in parentheses</summary>
    public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }

        public ParenthesizedExpressionSyntax(SyntaxToken openParenToken, ExpressionSyntax expression, SyntaxToken closeParenToken)
            : base(SyntaxKind.ParenthesizedExpression, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a tuple expression as a parenthesized argument sequence</summary>
    public sealed class TupleExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<ArgumentSyntax> Arguments { get; }
        public SyntaxToken CloseParenToken { get; }

        public TupleExpressionSyntax(
            SyntaxToken openParenToken,
            SeparatedSyntaxList<ArgumentSyntax> arguments,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.TupleExpression, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Arguments = arguments;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Wraps a regular expression element in a collection expression</summary>
    public sealed class ExpressionElementSyntax : CollectionElementSyntax
    {
        public ExpressionSyntax Expression { get; }

        public ExpressionElementSyntax(ExpressionSyntax expression)
            : base(SyntaxKind.ExpressionElement, expression.Span)
        {
            Expression = expression;
        }
    }
    ///<summary>Represents a collection element introduced by '..'</summary>
    public sealed class SpreadElementSyntax : CollectionElementSyntax
    {
        public SyntaxToken DotDotToken { get; }
        public ExpressionSyntax Expression { get; }

        public SpreadElementSyntax(SyntaxToken dotDotToken, ExpressionSyntax expression)
            : base(SyntaxKind.SpreadElement, NodeSpan.From(dotDotToken.Span, expression.Span))
        {
            DotDotToken = dotDotToken;
            Expression = expression;
        }
    }
    ///<summary>Represents a bracketed collection expression</summary>
    public sealed class CollectionExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<CollectionElementSyntax> Elements { get; }
        public SyntaxToken CloseBracketToken { get; }

        public CollectionExpressionSyntax(
            SyntaxToken openBracketToken,
            SeparatedSyntaxList<CollectionElementSyntax> elements,
            SyntaxToken closeBracketToken)
            : base(SyntaxKind.CollectionExpression, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            Elements = elements;
            CloseBracketToken = closeBracketToken;
        }
    }
    ///<summary>Represents a 'typeof(T)' expression</summary>
    public sealed class TypeOfExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken TypeOfKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken CloseParenToken { get; }

        public TypeOfExpressionSyntax(
            SyntaxToken typeOfKeyword,
            SyntaxToken openParenToken,
            TypeSyntax type,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.TypeOfExpression, NodeSpan.From(typeOfKeyword.Span, closeParenToken.Span))
        {
            TypeOfKeyword = typeOfKeyword;
            OpenParenToken = openParenToken;
            Type = type;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a 'sizeof(T)' expression</summary>
    public sealed class SizeOfExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken SizeOfKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken CloseParenToken { get; }

        public SizeOfExpressionSyntax(
            SyntaxToken sizeOfKeyword,
            SyntaxToken openParenToken,
            TypeSyntax type,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.SizeOfExpression, NodeSpan.From(sizeOfKeyword.Span, closeParenToken.Span))
        {
            SizeOfKeyword = sizeOfKeyword;
            OpenParenToken = openParenToken;
            Type = type;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a typed 'default(T)' expression</summary>
    public sealed class DefaultExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken DefaultKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken CloseParenToken { get; }

        public DefaultExpressionSyntax(
            SyntaxToken defaultKeyword,
            SyntaxToken openParenToken,
            TypeSyntax type,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.DefaultExpression, NodeSpan.From(defaultKeyword.Span, closeParenToken.Span))
        {
            DefaultKeyword = defaultKeyword;
            OpenParenToken = openParenToken;
            Type = type;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a 'checked' or 'unchecked' expression</summary>
    public sealed class CheckedExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Keyword { get; } // 'checked' or 'unchecked'
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }

        public CheckedExpressionSyntax(
            SyntaxKind kind,
            SyntaxToken keyword,
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken)
            : base(kind, NodeSpan.From(keyword.Span, closeParenToken.Span))
        {
            Keyword = keyword;
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a prefix operator applied before its operand</summary>
    public sealed class PrefixUnaryExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OperatorToken { get; } // '+', '-', '!', '~', '++', '--', '^', '&' or '*'
        public ExpressionSyntax Operand { get; }

        public PrefixUnaryExpressionSyntax(SyntaxKind kind, SyntaxToken operatorToken, ExpressionSyntax operand)
            : base(kind, NodeSpan.From(operatorToken.Span, operand.Span))
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }
    }
    ///<summary>Represents a parenthesized type cast followed by its operand</summary>
    public sealed class CastExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public TypeSyntax Type { get; }
        public SyntaxToken CloseParenToken { get; }
        public ExpressionSyntax Expression { get; }

        public CastExpressionSyntax(SyntaxToken openParenToken, TypeSyntax type, SyntaxToken closeParenToken, ExpressionSyntax expression)
            : base(SyntaxKind.CastExpression, NodeSpan.From(openParenToken.Span, expression.Span))
        {
            OpenParenToken = openParenToken;
            Type = type;
            CloseParenToken = closeParenToken;
            Expression = expression;
        }
    }
    ///<summary>Base class for variable names, discards and nested deconstruction designations</summary>
    public abstract class VariableDesignationSyntax : SyntaxNode
    {
        protected VariableDesignationSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    ///<summary>Represents one identifier introduced by a pattern or declaration expression</summary>
    public sealed class SingleVariableDesignationSyntax : VariableDesignationSyntax
    {
        public SyntaxToken Identifier { get; }

        public SingleVariableDesignationSyntax(SyntaxToken identifier)
            : base(SyntaxKind.SingleVariableDesignation, identifier.Span)
        {
            Identifier = identifier;
        }
    }
    ///<summary>Represents the 'this' expression</summary>
    public sealed class ThisExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken ThisKeyword { get; }

        public ThisExpressionSyntax(SyntaxToken thisKeyword)
            : base(SyntaxKind.ThisExpression, thisKeyword.Span)
        {
            ThisKeyword = thisKeyword;
        }
    }
    ///<summary>Represents the discard designation '_'</summary>
    public sealed class DiscardDesignationSyntax : VariableDesignationSyntax
    {
        public SyntaxToken UnderscoreToken { get; }

        public DiscardDesignationSyntax(SyntaxToken underscoreToken)
            : base(SyntaxKind.DiscardDesignation, underscoreToken.Span)
        {
            UnderscoreToken = underscoreToken;
        }
    }
    ///<summary>Represents a nested comma-separated deconstruction designation</summary>
    public sealed class ParenthesizedVariableDesignationSyntax : VariableDesignationSyntax
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<VariableDesignationSyntax> Variables { get; }
        public SyntaxToken CloseParenToken { get; }
        public ParenthesizedVariableDesignationSyntax(
            SyntaxToken openParenToken,
            SeparatedSyntaxList<VariableDesignationSyntax> variables,
            SyntaxToken closeParenToken)
            : base(SyntaxKind.ParenthesizedVariableDesignation, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Variables = variables;
            CloseParenToken = closeParenToken;
        }
    }
    ///<summary>Represents a type and variable designation used in an expression position</summary>
    public sealed class DeclarationExpressionSyntax : ExpressionSyntax
    {
        public TypeSyntax Type { get; }
        public VariableDesignationSyntax Designation { get; }

        public DeclarationExpressionSyntax(TypeSyntax type, VariableDesignationSyntax designation)
            : base(SyntaxKind.DeclarationExpression, NodeSpan.From(type.Span, designation.Span))
        {
            Type = type;
            Designation = designation;
        }
    }
    ///<summary>Represents the 'base' expression</summary>
    public sealed class BaseExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken BaseKeyword { get; }

        public BaseExpressionSyntax(SyntaxToken baseKeyword)
            : base(SyntaxKind.BaseExpression, baseKeyword.Span)
        {
            BaseKeyword = baseKeyword;
        }
    }

    ///<summary>Represents a literal, null, true, false or default value</summary>
    public sealed class LiteralExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken LiteralToken { get; }

        public LiteralExpressionSyntax(SyntaxKind kind, SyntaxToken literalToken)
            : base(kind, literalToken.Span)
        {
            LiteralToken = literalToken;
        }
    }

    ///<summary>Represents the contextual 'field' expression</summary>
    public sealed class FieldExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Token { get; }

        public FieldExpressionSyntax(SyntaxToken token)
            : base(SyntaxKind.FieldExpression, token.Span)
        {
            Token = token;
        }
    }

    ///<summary>Represents an expression prefixed by contextual 'await'</summary>
    public sealed class AwaitExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken AwaitKeyword { get; } // contextual token
        public ExpressionSyntax Expression { get; }

        public AwaitExpressionSyntax(SyntaxToken awaitKeyword, ExpressionSyntax expression)
            : base(SyntaxKind.AwaitExpression, NodeSpan.From(awaitKeyword.Span, expression.Span))
        {
            AwaitKeyword = awaitKeyword;
            Expression = expression;
        }
    }
    ///<summary>Represents an expression prefixed by 'throw'</summary>
    public sealed class ThrowExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken ThrowKeyword { get; }
        public ExpressionSyntax Expression { get; }

        public ThrowExpressionSyntax(SyntaxToken throwKeyword, ExpressionSyntax expression)
            : base(SyntaxKind.ThrowExpression, NodeSpan.From(throwKeyword.Span, expression.Span))
        {
            ThrowKeyword = throwKeyword;
            Expression = expression;
        }
    }
    ///<summary>Represents a postfix operator applied after its operand</summary>
    public sealed class PostfixUnaryExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Operand { get; }
        public SyntaxToken OperatorToken { get; } // '++' or '--'

        public PostfixUnaryExpressionSyntax(SyntaxKind kind, ExpressionSyntax operand, SyntaxToken operatorToken)
            : base(kind, NodeSpan.From(operand.Span, operatorToken.Span))
        {
            Operand = operand;
            OperatorToken = operatorToken;
        }
    }

    ///<summary>Represents a binary operator and its left and right operands</summary>
    public sealed class BinaryExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Left { get; }
        // '*', '/', '%', '+', '-', '<<', '>>', '>>>', '<', '<=', '>', '>='
        // 'is', 'as', '==', '!=', '&', '^', '|', '&&', '||' or '??'
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Right { get; }

        public BinaryExpressionSyntax(SyntaxKind kind, ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
            : base(kind, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }
    ///<summary>Represents an expression matched by 'is pattern'</summary>
    public sealed class IsPatternExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken IsKeyword { get; }
        public PatternSyntax Pattern { get; }

        public IsPatternExpressionSyntax(ExpressionSyntax expression, SyntaxToken isKeyword, PatternSyntax pattern)
            : base(SyntaxKind.IsPatternExpression, NodeSpan.From(expression.Span, pattern.Span))
        {
            Expression = expression;
            IsKeyword = isKeyword;
            Pattern = pattern;
        }
    }
    ///<summary>Represents an assignment operator and its left and right operands</summary>
    public sealed class AssignmentExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Left { get; }
        // '=', '+=', '-=', '*=', '/=', '%=', '&=', '|=', '^=', '<<=', '>>=', '>>>=' or '??='
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Right { get; }

        public AssignmentExpressionSyntax(SyntaxKind kind, ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
            : base(kind, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }

    ///<summary>Represents the ternary conditional expression</summary>
    public sealed class ConditionalExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Condition { get; }
        public SyntaxToken QuestionToken { get; }
        public ExpressionSyntax WhenTrue { get; }
        public SyntaxToken ColonToken { get; }
        public ExpressionSyntax WhenFalse { get; }

        public ConditionalExpressionSyntax(
            ExpressionSyntax condition, SyntaxToken questionToken, ExpressionSyntax whenTrue, SyntaxToken colonToken, ExpressionSyntax whenFalse)
            : base(SyntaxKind.ConditionalExpression, NodeSpan.From(condition.Span, whenFalse.Span))
        {
            Condition = condition;
            QuestionToken = questionToken;
            WhenTrue = whenTrue;
            ColonToken = colonToken;
            WhenFalse = whenFalse;
        }
    }

    ///<summary>Represents a '..' range with either operand optionally omitted</summary>
    public sealed class RangeExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax? LeftOperand { get; }
        public SyntaxToken OperatorToken { get; } // '..'
        public ExpressionSyntax? RightOperand { get; }

        public RangeExpressionSyntax(ExpressionSyntax? left, SyntaxToken operatorToken, ExpressionSyntax? right)
            : base(SyntaxKind.RangeExpression,
                  NodeSpan.FromNonNull(left?.Span, operatorToken.Span, right?.Span))
        {
            LeftOperand = left;
            OperatorToken = operatorToken;
            RightOperand = right;
        }
    }

    ///<summary>Represents member selection through '.' or '->'</summary>
    public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; } // '.' or '->'
        public SimpleNameSyntax Name { get; }

        public MemberAccessExpressionSyntax(SyntaxKind kind, ExpressionSyntax expression, SyntaxToken operatorToken, SimpleNameSyntax name)
            : base(kind, NodeSpan.From(expression.Span, name.Span))
        {
            Expression = expression;
            OperatorToken = operatorToken;
            Name = name;
        }
    }

    ///<summary>Represents conditional access introduced by '?' before a binding expression</summary>
    public sealed class ConditionalAccessExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; } // '?'
        public ExpressionSyntax WhenNotNull { get; }

        public ConditionalAccessExpressionSyntax(ExpressionSyntax expression, SyntaxToken operatorToken, ExpressionSyntax whenNotNull)
            : base(SyntaxKind.ConditionalAccessExpression, NodeSpan.From(expression.Span, whenNotNull.Span))
        {
            Expression = expression;
            OperatorToken = operatorToken;
            WhenNotNull = whenNotNull;
        }
    }

    ///<summary>Represents the '.name' binding used after conditional access</summary>
    public sealed class MemberBindingExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OperatorToken { get; } // '.'
        public SimpleNameSyntax Name { get; }

        public MemberBindingExpressionSyntax(SyntaxToken operatorToken, SimpleNameSyntax name)
            : base(SyntaxKind.MemberBindingExpression, NodeSpan.From(operatorToken.Span, name.Span))
        {
            OperatorToken = operatorToken;
            Name = name;
        }
    }

    ///<summary>Base class for an expression followed by ':'</summary>
    public abstract class BaseExpressionColonSyntax : SyntaxNode
    {
        public abstract ExpressionSyntax Expression { get; }
        public SyntaxToken ColonToken { get; }

        protected BaseExpressionColonSyntax(SyntaxKind kind, SyntaxToken colonToken, TextSpan span)
            : base(kind, span)
        {
            ColonToken = colonToken;
        }
    }

    ///<summary>Represents the 'name:' prefix of a named argument or subpattern</summary>
    public sealed class NameColonSyntax : BaseExpressionColonSyntax
    {
        public IdentifierNameSyntax Name { get; }
        public override ExpressionSyntax Expression => Name;

        public NameColonSyntax(IdentifierNameSyntax name, SyntaxToken colonToken)
            : base(SyntaxKind.NameColon, colonToken, NodeSpan.From(name.Span, colonToken.Span))
        {
            Name = name;
        }
    }

    ///<summary>Represents an expression followed by ':' in an extended property pattern</summary>
    public sealed class ExpressionColonSyntax : BaseExpressionColonSyntax
    {
        public override ExpressionSyntax Expression { get; }

        public ExpressionColonSyntax(ExpressionSyntax expression, SyntaxToken colonToken)
            : base(SyntaxKind.ExpressionColon, colonToken, NodeSpan.From(expression.Span, colonToken.Span))
        {
            Expression = expression;
        }
    }

    ///<summary>Represents an argument with optional name and ref-kind modifier</summary>
    public sealed class ArgumentSyntax : SyntaxNode
    {
        public NameColonSyntax? NameColon { get; }
        public SyntaxToken? RefKindKeyword { get; } // 'ref', 'out', 'in' or absent
        public ExpressionSyntax Expression { get; }

        public ArgumentSyntax(NameColonSyntax? nameColon, SyntaxToken? refKindKeyword, ExpressionSyntax expression)
        : base(SyntaxKind.Argument, NodeSpan.FromNonNull(nameColon?.Span, refKindKeyword?.Span, expression.Span))
        {
            NameColon = nameColon;
            RefKindKeyword = refKindKeyword;
            Expression = expression;
        }
    }

    ///<summary>Represents a comma-separated argument list in parentheses</summary>
    public sealed class ArgumentListSyntax : SyntaxNode
    {
        public SyntaxToken OpenParenToken { get; }
        public SeparatedSyntaxList<ArgumentSyntax> Arguments { get; }
        public SyntaxToken CloseParenToken { get; }

        public ArgumentListSyntax(SyntaxToken openParenToken, SeparatedSyntaxList<ArgumentSyntax> arguments, SyntaxToken closeParenToken)
            : base(SyntaxKind.ArgumentList, NodeSpan.From(openParenToken.Span, closeParenToken.Span))
        {
            OpenParenToken = openParenToken;
            Arguments = arguments;
            CloseParenToken = closeParenToken;
        }
    }

    ///<summary>Represents a comma-separated argument list in brackets</summary>
    public sealed class BracketedArgumentListSyntax : SyntaxNode
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<ArgumentSyntax> Arguments { get; }
        public SyntaxToken CloseBracketToken { get; }

        public BracketedArgumentListSyntax(SyntaxToken openBracketToken, SeparatedSyntaxList<ArgumentSyntax> arguments, SyntaxToken closeBracketToken)
            : base(SyntaxKind.BracketedArgumentList, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            Arguments = arguments;
            CloseBracketToken = closeBracketToken;
        }
    }

    ///<summary>Represents invocation of an expression with arguments</summary>
    public sealed class InvocationExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public ArgumentListSyntax ArgumentList { get; }

        public InvocationExpressionSyntax(ExpressionSyntax expression, ArgumentListSyntax argumentList)
            : base(SyntaxKind.InvocationExpression, NodeSpan.From(expression.Span, argumentList.Span))
        {
            Expression = expression;
            ArgumentList = argumentList;
        }
    }

    ///<summary>Represents indexed access on an expression</summary>
    public sealed class ElementAccessExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Expression { get; }
        public BracketedArgumentListSyntax ArgumentList { get; }

        public ElementAccessExpressionSyntax(ExpressionSyntax expression, BracketedArgumentListSyntax argumentList)
            : base(SyntaxKind.ElementAccessExpression, NodeSpan.From(expression.Span, argumentList.Span))
        {
            Expression = expression;
            ArgumentList = argumentList;
        }
    }

    ///<summary>Represents bracketed element access without a receiver in an initializer</summary>
    public sealed class ImplicitElementAccessSyntax : ExpressionSyntax
    {
        public BracketedArgumentListSyntax ArgumentList { get; }

        public ImplicitElementAccessSyntax(BracketedArgumentListSyntax argumentList)
            : base(SyntaxKind.ImplicitElementAccess, argumentList.Span)
        {
            ArgumentList = argumentList;
        }
    }
    ///<summary>Represents bracketed element binding after conditional access</summary>
    public sealed class ElementBindingExpressionSyntax : ExpressionSyntax
    {
        public BracketedArgumentListSyntax ArgumentList { get; }

        public ElementBindingExpressionSyntax(BracketedArgumentListSyntax argumentList)
            : base(SyntaxKind.ElementBindingExpression, argumentList.Span)
        {
            ArgumentList = argumentList;
        }
    }
    ///<summary>Represents an omitted array dimension between separators</summary>
    public sealed class OmittedArraySizeExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OmittedArraySizeExpressionToken { get; }

        public OmittedArraySizeExpressionSyntax(SyntaxToken token)
            : base(SyntaxKind.OmittedArraySizeExpression, token.Span)
        {
            OmittedArraySizeExpressionToken = token;
        }
    }

    ///<summary>Represents one bracketed array rank and its optional dimension sizes</summary>
    public sealed class ArrayRankSpecifierSyntax : SyntaxNode
    {
        public SyntaxToken OpenBracketToken { get; }
        public SeparatedSyntaxList<ExpressionSyntax> Sizes { get; }
        public SyntaxToken CloseBracketToken { get; }

        public ArrayRankSpecifierSyntax(SyntaxToken openBracketToken, SeparatedSyntaxList<ExpressionSyntax> sizes, SyntaxToken closeBracketToken)
            : base(SyntaxKind.ArrayRankSpecifier, NodeSpan.From(openBracketToken.Span, closeBracketToken.Span))
        {
            OpenBracketToken = openBracketToken;
            Sizes = sizes;
            CloseBracketToken = closeBracketToken;
        }
    }

    ///<summary>Represents an element type followed by one or more array rank specifiers</summary>
    public sealed class ArrayTypeSyntax : TypeSyntax
    {
        public TypeSyntax ElementType { get; }
        public SyntaxList<ArrayRankSpecifierSyntax> RankSpecifiers { get; }

        public ArrayTypeSyntax(TypeSyntax elementType, SyntaxList<ArrayRankSpecifierSyntax> rankSpecifiers)
            : base(SyntaxKind.ArrayType,
                   rankSpecifiers.Count > 0
                       ? NodeSpan.From(elementType.Span, rankSpecifiers[rankSpecifiers.Count - 1].Span)
                       : elementType.Span)
        {
            ElementType = elementType;
            RankSpecifiers = rankSpecifiers;
        }
    }
    ///<summary>Represents a brace-delimited object, collection, array or complex element initializer</summary>
    public sealed class InitializerExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OpenBraceToken { get; }
        public SeparatedSyntaxList<ExpressionSyntax> Expressions { get; }
        public SyntaxToken CloseBraceToken { get; }

        public InitializerExpressionSyntax(SyntaxKind kind, SyntaxToken openBraceToken, SeparatedSyntaxList<ExpressionSyntax> expressions, SyntaxToken closeBraceToken)
            : base(kind, NodeSpan.From(openBraceToken.Span, closeBraceToken.Span))
        {
            OpenBraceToken = openBraceToken;
            Expressions = expressions;
            CloseBraceToken = closeBraceToken;
        }
    }

    ///<summary>Represents one member declarator in an anonymous object creation expression</summary>
    public sealed class AnonymousObjectMemberDeclaratorSyntax : SyntaxNode
    {
        public NameEqualsSyntax? NameEquals { get; }
        public ExpressionSyntax Expression { get; }

        public AnonymousObjectMemberDeclaratorSyntax(NameEqualsSyntax? nameEquals, ExpressionSyntax expression)
            : base(
                SyntaxKind.AnonymousObjectMemberDeclarator,
                NodeSpan.FromNonNull(nameEquals?.Span, expression.Span))
        {
            NameEquals = nameEquals;
            Expression = expression;
        }
    }

    ///<summary>Represents anonymous object creation introduced by 'new'</summary>
    public sealed class AnonymousObjectCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public SyntaxToken OpenBraceToken { get; }
        public SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax> Initializers { get; }
        public SyntaxToken CloseBraceToken { get; }

        public AnonymousObjectCreationExpressionSyntax(
            SyntaxToken newKeyword,
            SyntaxToken openBraceToken,
            SeparatedSyntaxList<AnonymousObjectMemberDeclaratorSyntax> initializers,
            SyntaxToken closeBraceToken)
            : base(SyntaxKind.AnonymousObjectCreationExpression, NodeSpan.From(newKeyword.Span, closeBraceToken.Span))
        {
            NewKeyword = newKeyword;
            OpenBraceToken = openBraceToken;
            Initializers = initializers;
            CloseBraceToken = closeBraceToken;
        }
    }
    ///<summary>Represents explicit array creation with optional initializer</summary>
    public sealed class ArrayCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public ArrayTypeSyntax Type { get; }
        public InitializerExpressionSyntax? Initializer { get; }

        public ArrayCreationExpressionSyntax(
            SyntaxToken newKeyword,
            ArrayTypeSyntax type,
            InitializerExpressionSyntax? initializer)
            : base(
                SyntaxKind.ArrayCreationExpression,
                NodeSpan.FromNonNull(newKeyword.Span, type.Span, initializer?.Span))
        {
            NewKeyword = newKeyword;
            Type = type;
            Initializer = initializer;
        }
    }
    ///<summary>Represents implicitly typed array creation with a required initializer</summary>
    public sealed class ImplicitArrayCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public SyntaxToken OpenBracketToken { get; }
        public SyntaxTokenList Commas { get; }
        public SyntaxToken CloseBracketToken { get; }
        public InitializerExpressionSyntax Initializer { get; }

        public ImplicitArrayCreationExpressionSyntax(
            SyntaxToken newKeyword,
            SyntaxToken openBracketToken,
            SyntaxTokenList commas,
            SyntaxToken closeBracketToken,
            InitializerExpressionSyntax initializer)
            : base(
                SyntaxKind.ImplicitArrayCreationExpression,
                NodeSpan.From(newKeyword.Span, initializer.Span))
        {
            NewKeyword = newKeyword;
            OpenBracketToken = openBracketToken;
            Commas = commas;
            CloseBracketToken = closeBracketToken;
            Initializer = initializer;
        }
    }
    ///<summary>Represents target-typed object creation with optional initializer</summary>
    public sealed class ImplicitObjectCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public ArgumentListSyntax ArgumentList { get; }
        public InitializerExpressionSyntax? Initializer { get; }
        public ImplicitObjectCreationExpressionSyntax(
            SyntaxToken newKeyword,
            ArgumentListSyntax argumentList,
            InitializerExpressionSyntax? initializer)
            : base(
                  SyntaxKind.ImplicitObjectCreationExpression,
                  NodeSpan.FromNonNull(newKeyword.Span, argumentList.Span, initializer?.Span))
        {
            NewKeyword = newKeyword;
            ArgumentList = argumentList;
            Initializer = initializer;
        }
    }
    ///<summary>Represents stack allocation with an explicit element or array type</summary>
    public sealed class StackAllocArrayCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken StackAllocKeyword { get; }
        public TypeSyntax Type { get; }
        public InitializerExpressionSyntax? Initializer { get; }

        public StackAllocArrayCreationExpressionSyntax(
            SyntaxToken stackAllocKeyword,
            TypeSyntax type,
            InitializerExpressionSyntax? initializer)
            : base(
                SyntaxKind.StackAllocArrayCreationExpression,
                NodeSpan.FromNonNull(stackAllocKeyword.Span, type.Span, initializer?.Span))
        {
            StackAllocKeyword = stackAllocKeyword;
            Type = type;
            Initializer = initializer;
        }
    }

    ///<summary>Represents implicitly typed stack allocation with an initializer</summary>
    public sealed class ImplicitStackAllocArrayCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken StackAllocKeyword { get; }
        public SyntaxToken OpenBracketToken { get; }
        public SyntaxToken CloseBracketToken { get; }
        public InitializerExpressionSyntax Initializer { get; }

        public ImplicitStackAllocArrayCreationExpressionSyntax(
            SyntaxToken stackAllocKeyword,
            SyntaxToken openBracketToken,
            SyntaxToken closeBracketToken,
            InitializerExpressionSyntax initializer)
            : base(
                SyntaxKind.ImplicitStackAllocArrayCreationExpression,
                NodeSpan.From(stackAllocKeyword.Span, initializer.Span))
        {
            StackAllocKeyword = stackAllocKeyword;
            OpenBracketToken = openBracketToken;
            CloseBracketToken = closeBracketToken;
            Initializer = initializer;
        }
    }
    ///<summary>Represents object creation with an explicit type and optional arguments or initializer</summary>
    public sealed class ObjectCreationExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken NewKeyword { get; }
        public TypeSyntax Type { get; }
        public ArgumentListSyntax? ArgumentList { get; }
        public InitializerExpressionSyntax? Initializer { get; }

        public ObjectCreationExpressionSyntax(
            SyntaxToken newKeyword,
            TypeSyntax type,
            ArgumentListSyntax? argumentList,
            InitializerExpressionSyntax? initializer)
            : base(SyntaxKind.ObjectCreationExpression,
                   NodeSpan.FromNonNull(newKeyword.Span, type.Span, (initializer?.Span ?? argumentList?.Span)))
        {
            NewKeyword = newKeyword;
            Type = type;
            ArgumentList = argumentList;
            Initializer = initializer;
        }
    }
    ///<summary>Represents the brace-delimited accessor list of a property, event or indexer</summary>
    public sealed class AccessorListSyntax : SyntaxNode
    {
        public SyntaxToken OpenBraceToken { get; }
        public SyntaxList<AccessorDeclarationSyntax> Accessors { get; }
        public SyntaxToken CloseBraceToken { get; }

        public AccessorListSyntax(SyntaxToken openBraceToken, SyntaxList<AccessorDeclarationSyntax> accessors, SyntaxToken closeBraceToken)
            : base(SyntaxKind.AccessorList, NodeSpan.From(openBraceToken.Span, closeBraceToken.Span))
        {
            OpenBraceToken = openBraceToken;
            Accessors = accessors;
            CloseBraceToken = closeBraceToken;
        }
    }
    ///<summary>Represents one get, set, init, add or remove accessor</summary>
    public sealed class AccessorDeclarationSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken Keyword { get; } // 'get', 'set', 'init', 'add' or 'remove'
        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public AccessorDeclarationSyntax(
            SyntaxKind kind,
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken keyword,
            BlockSyntax? body,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken)
            : base(kind,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    keyword.Span,
                    body?.Span,
                    expressionBody?.Span,
                    semicolonToken.Span))
        {
            AttributeLists = attributeLists;
            Modifiers = modifiers;
            Keyword = keyword;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents a property with accessors or an expression body and optional initializer</summary>
    public sealed class PropertyDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax Type { get; }
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken Identifier { get; }

        public AccessorListSyntax? AccessorList { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public EqualsValueClauseSyntax? Initializer { get; }
        public SyntaxToken SemicolonToken { get; }

        public PropertyDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax type,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken identifier,
            AccessorListSyntax? accessorList,
            ArrowExpressionClauseSyntax? expressionBody,
            EqualsValueClauseSyntax? initializer,
            SyntaxToken semicolonToken)
            : base(
                SyntaxKind.PropertyDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    type.Span,
                    explicitInterfaceSpecifier?.Span,
                    identifier.Span,
                    (semicolonToken.Span.Length != 0 ? semicolonToken.Span : (accessorList?.Span ?? expressionBody?.Span))))
        {
            Modifiers = modifiers;
            Type = type;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            Identifier = identifier;
            AccessorList = accessorList;
            ExpressionBody = expressionBody;
            Initializer = initializer;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Represents an event declaration with explicit accessors</summary>
    public sealed class EventDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken EventKeyword { get; }
        public TypeSyntax Type { get; }
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken Identifier { get; }
        public AccessorListSyntax AccessorList { get; }

        public EventDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken eventKeyword,
            TypeSyntax type,
            ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
            SyntaxToken identifier,
            AccessorListSyntax accessorList)
            : base(
                SyntaxKind.EventDeclaration,
                attributeLists,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    eventKeyword.Span,
                    type.Span,
                    explicitInterfaceSpecifier?.Span,
                    identifier.Span,
                    accessorList.Span))
        {
            Modifiers = modifiers;
            EventKeyword = eventKeyword;
            Type = type;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            Identifier = identifier;
            AccessorList = accessorList;
        }
    }
    ///<summary>Represents an indexer with accessors or an expression body</summary>
    public sealed class IndexerDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax Type { get; }
        public ExplicitInterfaceSpecifierSyntax? ExplicitInterfaceSpecifier { get; }
        public SyntaxToken ThisKeyword { get; }
        public BracketedParameterListSyntax ParameterList { get; }

        public AccessorListSyntax? AccessorList { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }
        public IndexerDeclarationSyntax(
        SyntaxList<AttributeListSyntax> attributeLists,
        SyntaxTokenList modifiers,
        TypeSyntax type,
        ExplicitInterfaceSpecifierSyntax? explicitInterfaceSpecifier,
        SyntaxToken thisKeyword,
        BracketedParameterListSyntax parameterList,
        AccessorListSyntax? accessorList,
        ArrowExpressionClauseSyntax? expressionBody,
        SyntaxToken semicolonToken)
        : base(
            SyntaxKind.IndexerDeclaration,
            attributeLists,
            NodeSpan.FromNonNull(
                attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                type.Span,
                explicitInterfaceSpecifier?.Span,
                thisKeyword.Span,
                (semicolonToken.Span.Length != 0 ? semicolonToken.Span : (accessorList?.Span ?? expressionBody?.Span))))
        {
            Modifiers = modifiers;
            Type = type;
            ExplicitInterfaceSpecifier = explicitInterfaceSpecifier;
            ThisKeyword = thisKeyword;
            ParameterList = parameterList;
            AccessorList = accessorList;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    ///<summary>Base class for simple and parenthesized lambda expressions</summary>
    public abstract class LambdaExpressionSyntax : AnonymousFunctionExpressionSyntax
    {
        private readonly SyntaxNode _body;

        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxToken ArrowToken { get; }
        public override BlockSyntax? Block => _body as BlockSyntax;
        public override ExpressionSyntax? ExpressionBody => _body as ExpressionSyntax;

        protected LambdaExpressionSyntax(
            SyntaxKind kind,
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken arrowToken,
            SyntaxNode body,
            TextSpan span)
            : base(kind, modifiers, span)
        {
            AttributeLists = attributeLists;
            ArrowToken = arrowToken;
            _body = body;
        }
    }

    ///<summary>Represents a lambda with one unparenthesized parameter</summary>
    public sealed class SimpleLambdaExpressionSyntax : LambdaExpressionSyntax
    {
        public ParameterSyntax Parameter { get; }

        public SimpleLambdaExpressionSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            ParameterSyntax parameter,
            SyntaxToken arrowToken,
            SyntaxNode body)
            : base(
                SyntaxKind.SimpleLambdaExpression,
                attributeLists,
                modifiers,
                arrowToken,
                body,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    parameter.Span,
                    arrowToken.Span,
                    body.Span))
        {
            Parameter = parameter;
        }
    }
    ///<summary>Represents a lambda with a parenthesized parameter list</summary>
    public sealed class ParenthesizedLambdaExpressionSyntax : LambdaExpressionSyntax
    {
        public TypeSyntax? ReturnType { get; }
        public ParameterListSyntax ParameterList { get; }

        public ParenthesizedLambdaExpressionSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax? returnType,
            ParameterListSyntax parameterList,
            SyntaxToken arrowToken,
            SyntaxNode body)
            : base(
                SyntaxKind.ParenthesizedLambdaExpression,
                attributeLists,
                modifiers,
                arrowToken,
                body,
                NodeSpan.FromNonNull(
                    attributeLists.Count > 0 ? attributeLists[0].Span : (TextSpan?)null,
                    modifiers.Count > 0 ? modifiers[0].Span : (TextSpan?)null,
                    returnType?.Span,
                    parameterList.Span,
                    arrowToken.Span,
                    body.Span))
        {
            ReturnType = returnType;
            ParameterList = parameterList;
        }
    }
    ///<summary>Represents an expression prefixed by 'ref'</summary>
    public sealed class RefExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken RefKeyword { get; }
        public ExpressionSyntax Expression { get; }

        public RefExpressionSyntax(SyntaxToken refKeyword, ExpressionSyntax expression)
            : base(SyntaxKind.RefExpression, NodeSpan.From(refKeyword.Span, expression.Span))
        {
            RefKeyword = refKeyword;
            Expression = expression;
        }
    }
}
