using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Cnidaria.Cs
{

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

    public readonly struct SeparatedSyntaxList<T> : IEnumerable<T> where T : SyntaxNode
    {
        private readonly SyntaxNodeOrToken[] _nodesAndSeparators;

        public SeparatedSyntaxList(SyntaxNodeOrToken[] nodesAndSeparators)
            => _nodesAndSeparators = nodesAndSeparators ?? Array.Empty<SyntaxNodeOrToken>();
        public static SeparatedSyntaxList<T> Empty => new(Array.Empty<SyntaxNodeOrToken>());
        public int Count
        {
            get
            {
                int len = _nodesAndSeparators.Length;
                if (len == 0) return 0;

                if (_nodesAndSeparators[len - 1].IsToken)
                    return len / 2;

                return (len + 1) / 2;
            }
        }

        public int SeparatorCount => _nodesAndSeparators.Length / 2;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                int i = index * 2;
                return (T)_nodesAndSeparators[i].Node;
            }
        }

        public SyntaxToken GetSeparator(int index)
        {
            if ((uint)index >= (uint)SeparatorCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            int i = index * 2 + 1;
            return _nodesAndSeparators[i].Token;
        }

        public SyntaxNodeOrToken[] GetWithSeparators() => _nodesAndSeparators;

        public IEnumerator<T> GetEnumerator()
        {
            // Only nodes are at even indices
            for (int i = 0; i < _nodesAndSeparators.Length; i += 2)
            {
                if (_nodesAndSeparators[i].IsNode)
                    yield return (T)_nodesAndSeparators[i].Node;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
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
    public abstract class ExpressionSyntax : SyntaxNode
    {
        protected ExpressionSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    public abstract class StatementSyntax : SyntaxNode
    {
        protected StatementSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    public abstract class InterpolatedStringContentSyntax : SyntaxNode
    {
        protected InterpolatedStringContentSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    public abstract class MemberDeclarationSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }

        protected MemberDeclarationSyntax(SyntaxKind kind, SyntaxList<AttributeListSyntax> attributeLists, TextSpan span)
            : base(kind, span)
        {
            AttributeLists = attributeLists;
        }
    }
    public abstract class CollectionElementSyntax : SyntaxNode
    {
        protected CollectionElementSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    public abstract class BaseTypeSyntax : SyntaxNode
    {
        public abstract TypeSyntax Type { get; }
        protected BaseTypeSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    public sealed class SimpleBaseTypeSyntax : BaseTypeSyntax
    {
        public override TypeSyntax Type { get; }

        public SimpleBaseTypeSyntax(TypeSyntax type)
            : base(SyntaxKind.SimpleBaseType, type.Span)
        {
            Type = type;
        }
    }
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
    public abstract class TypeSyntax : ExpressionSyntax
    {
        protected TypeSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    public abstract class NameSyntax : TypeSyntax
    {
        protected NameSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    public abstract class SimpleNameSyntax : NameSyntax
    {
        protected SimpleNameSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    // base nodes
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
    public sealed class UsingDirectiveSyntax : SyntaxNode
    {
        public SyntaxToken GlobalKeyword { get; } // optional
        public SyntaxToken UsingKeyword { get; }
        public SyntaxToken StaticKeyword { get; } // optional
        public NameEqualsSyntax? Alias { get; }   // optional
        public NameSyntax Name { get; }
        public SyntaxToken SemicolonToken { get; }

        public UsingDirectiveSyntax(
            SyntaxToken globalKeyword,
            SyntaxToken usingKeyword,
            SyntaxToken staticKeyword,
            NameEqualsSyntax? alias,
            NameSyntax name,
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
            Alias = alias;
            Name = name;
            SemicolonToken = semicolonToken;
        }
    }
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
    public sealed class IdentifierNameSyntax : SimpleNameSyntax
    {
        public SyntaxToken Identifier { get; }

        public IdentifierNameSyntax(SyntaxToken identifier)
            : base(SyntaxKind.IdentifierName, identifier.Span)
        {
            Identifier = identifier;
        }
    }

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
    public sealed class PredefinedTypeSyntax : TypeSyntax
    {
        public SyntaxToken Keyword { get; }

        public PredefinedTypeSyntax(SyntaxToken keyword)
            : base(SyntaxKind.PredefinedType, keyword.Span)
        {
            Keyword = keyword;
        }
    }
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
    public sealed class FunctionPointerUnmanagedCallingConventionSyntax : SyntaxNode
    {
        public SyntaxToken Name { get; }

        public FunctionPointerUnmanagedCallingConventionSyntax(SyntaxToken name)
            : base(SyntaxKind.FunctionPointerUnmanagedCallingConvention, name.Span)
        {
            Name = name;
        }
    }
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
    public abstract class PatternSyntax : SyntaxNode
    {
        protected PatternSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }
    public sealed class ConstantPatternSyntax : PatternSyntax
    {
        public ExpressionSyntax Expression { get; }

        public ConstantPatternSyntax(ExpressionSyntax expression)
            : base(SyntaxKind.ConstantPattern, expression.Span)
        {
            Expression = expression;
        }
    }
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
    public sealed class TypePatternSyntax : PatternSyntax
    {
        public TypeSyntax Type { get; }

        public TypePatternSyntax(TypeSyntax type)
            : base(SyntaxKind.TypePattern, type.Span)
        {
            Type = type;
        }
    }
    public sealed class RelationalPatternSyntax : PatternSyntax
    {
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Expression { get; }

        public RelationalPatternSyntax(SyntaxToken operatorToken, ExpressionSyntax expression)
            : base(SyntaxKind.RelationalPattern, NodeSpan.From(operatorToken.Span, expression.Span))
        {
            OperatorToken = operatorToken;
            Expression = expression;
        }
    }
    public sealed class BinaryPatternSyntax : PatternSyntax
    {
        public PatternSyntax Left { get; }
        public SyntaxToken OperatorToken { get; }
        public PatternSyntax Right { get; }

        public BinaryPatternSyntax(SyntaxKind kind, PatternSyntax left, SyntaxToken operatorToken, PatternSyntax right)
            : base(kind, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }
    public sealed class UnaryPatternSyntax : PatternSyntax
    {
        public SyntaxToken OperatorToken { get; }
        public PatternSyntax Pattern { get; }

        public UnaryPatternSyntax(SyntaxKind kind, SyntaxToken operatorToken, PatternSyntax pattern)
            : base(kind, NodeSpan.From(operatorToken.Span, pattern.Span))
        {
            OperatorToken = operatorToken;
            Pattern = pattern;
        }
    }
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
    public sealed class DiscardPatternSyntax : PatternSyntax
    {
        public SyntaxToken UnderscoreToken { get; }

        public DiscardPatternSyntax(SyntaxToken underscoreToken)
            : base(SyntaxKind.DiscardPattern, underscoreToken.Span)
        {
            UnderscoreToken = underscoreToken;
        }
    }
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
    public abstract class SwitchLabelSyntax : SyntaxNode
    {
        protected SwitchLabelSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

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
    public sealed class EmptyStatementSyntax : StatementSyntax
    {
        public SyntaxToken SemicolonToken { get; }

        public EmptyStatementSyntax(SyntaxToken semicolonToken)
            : base(SyntaxKind.EmptyStatement, semicolonToken.Span)
        {
            SemicolonToken = semicolonToken;
        }
    }
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
    public sealed class GotoStatementSyntax : StatementSyntax
    {
        public SyntaxToken GotoKeyword { get; }
        public SyntaxToken CaseOrDefaultKeyword { get; } // optional
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
    public sealed class CheckedStatementSyntax : StatementSyntax
    {
        public SyntaxToken Keyword { get; }
        public BlockSyntax Block { get; }

        public CheckedStatementSyntax(SyntaxKind kind, SyntaxToken keyword, BlockSyntax block)
            : base(kind, NodeSpan.From(keyword.Span, block.Span))
        {
            Keyword = keyword;
            Block = block;
        }
    }
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
    public sealed class YieldStatementSyntax : StatementSyntax
    {
        public SyntaxToken YieldKeyword { get; }
        public SyntaxToken ReturnOrBreakKeyword { get; }
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
    public sealed class GenericNameSyntax : SimpleNameSyntax
    {
        public SyntaxToken Identifier { get; }
        public TypeArgumentListSyntax TypeArgumentList { get; }

        public GenericNameSyntax(SyntaxToken identifier, TypeArgumentListSyntax typeArgumentList)
            : base(SyntaxKind.GenericName, NodeSpan.From(identifier.Span, typeArgumentList.Span))
        {
            Identifier = identifier;
            TypeArgumentList = typeArgumentList;
        }
    }
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
    public sealed class AnonymousMethodExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken AsyncKeyword { get; } // optional
        public SyntaxToken DelegateKeyword { get; }
        public ParameterListSyntax? ParameterList { get; }
        public BlockSyntax Block { get; }

        public AnonymousMethodExpressionSyntax(
            SyntaxToken asyncKeyword,
            SyntaxToken delegateKeyword,
            ParameterListSyntax? parameterList,
            BlockSyntax block)
            : base(
                SyntaxKind.AnonymousMethodExpression,
                NodeSpan.FromNonNull(
                    asyncKeyword.Span.Length != 0 ? asyncKeyword.Span : (TextSpan?)null,
                    delegateKeyword.Span,
                    parameterList?.Span,
                    block.Span))
        {
            AsyncKeyword = asyncKeyword;
            DelegateKeyword = delegateKeyword;
            ParameterList = parameterList;
            Block = block;
        }
    }
    public abstract class TypeDeclarationSyntax : BaseTypeDeclarationSyntax
    {
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
    public abstract class TypeParameterConstraintSyntax : SyntaxNode
    {
        protected TypeParameterConstraintSyntax(SyntaxKind kind, TextSpan span)
            : base(kind, span)
        {
        }
    }
    public sealed class ClassOrStructConstraintSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken ClassOrStructKeyword { get; }
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

    public abstract class AllowsConstraintSyntax : SyntaxNode
    {
        protected AllowsConstraintSyntax(SyntaxKind kind, TextSpan span)
            : base(kind, span)
        {
        }
    }

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
    public sealed class TypeConstraintSyntax : TypeParameterConstraintSyntax
    {
        public TypeSyntax Type { get; }

        public TypeConstraintSyntax(TypeSyntax type)
            : base(SyntaxKind.TypeConstraint, type.Span)
        {
            Type = type;
        }
    }
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
    public sealed class DefaultConstraintSyntax : TypeParameterConstraintSyntax
    {
        public SyntaxToken DefaultKeyword { get; }

        public DefaultConstraintSyntax(SyntaxToken defaultKeyword)
            : base(SyntaxKind.DefaultConstraint, defaultKeyword.Span)
        {
            DefaultKeyword = defaultKeyword;
        }
    }
    public sealed class ClassDeclarationSyntax : TypeDeclarationSyntax
    {
        public SyntaxToken Keyword { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public ClassDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken classKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
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
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    public sealed class StructDeclarationSyntax : TypeDeclarationSyntax
    {
        public SyntaxToken Keyword { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public StructDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken structKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
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
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    public sealed class InterfaceDeclarationSyntax : TypeDeclarationSyntax
    {
        public SyntaxToken Keyword { get; }
        public TypeParameterListSyntax? TypeParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }
        public InterfaceDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            SyntaxToken interfaceKeyword,
            SyntaxToken identifier,
            TypeParameterListSyntax? typeParameterList,
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
            BaseList = baseList;
            ConstraintClauses = constraintClauses;
        }
    }
    public sealed class RecordDeclarationSyntax : TypeDeclarationSyntax
    {
        public SyntaxToken Keyword { get; }
        public SyntaxToken ClassOrStructKeyword { get; } // optional
        public TypeParameterListSyntax? TypeParameterList { get; }
        public ParameterListSyntax? ParameterList { get; }
        public BaseListSyntax? BaseList { get; }
        public SyntaxList<TypeParameterConstraintClauseSyntax> ConstraintClauses { get; }

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
    public sealed class TypeParameterSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxToken VarianceKeyword { get; } // optional
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
                    identifier.Span,
                    @default?.Span))
        {
            AttributeLists = attributeLists;
            Modifiers = modifiers;
            Type = type;
            Identifier = identifier;
            Default = @default;
        }
    }

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
    public sealed class OperatorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public TypeSyntax ReturnType { get; }
        public SyntaxToken OperatorKeyword { get; }
        public SyntaxToken CheckedKeyword { get; } // optional
        public SyntaxToken OperatorToken { get; }
        public ParameterListSyntax ParameterList { get; }

        public BlockSyntax? Body { get; }
        public ArrowExpressionClauseSyntax? ExpressionBody { get; }
        public SyntaxToken SemicolonToken { get; }

        public OperatorDeclarationSyntax(
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxTokenList modifiers,
            TypeSyntax returnType,
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
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            ReturnType = returnType;
            OperatorKeyword = operatorKeyword;
            CheckedKeyword = checkedKeyword;
            OperatorToken = operatorToken;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    public sealed class ConversionOperatorDeclarationSyntax : MemberDeclarationSyntax
    {
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken ImplicitOrExplicitKeyword { get; }
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
                    (body != null ? body.Span : (expressionBody != null ? expressionBody.Span : semicolonToken.Span))))
        {
            Modifiers = modifiers;
            ImplicitOrExplicitKeyword = implicitOrExplicitKeyword;
            OperatorKeyword = operatorKeyword;
            CheckedKeyword = checkedKeyword;
            Type = type;
            ParameterList = parameterList;
            Body = body;
            ExpressionBody = expressionBody;
            SemicolonToken = semicolonToken;
        }
    }
    public sealed class ConstructorInitializerSyntax : SyntaxNode
    {
        public SyntaxToken ColonToken { get; }
        public SyntaxToken ThisOrBaseKeyword { get; }
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
    public sealed class InterpolatedStringTextSyntax : InterpolatedStringContentSyntax
    {
        public SyntaxToken TextToken { get; }

        public InterpolatedStringTextSyntax(SyntaxToken textToken)
            : base(SyntaxKind.InterpolatedStringText, textToken.Span)
        {
            TextToken = textToken;
        }
    }
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
    public sealed class ExpressionElementSyntax : CollectionElementSyntax
    {
        public ExpressionSyntax Expression { get; }

        public ExpressionElementSyntax(ExpressionSyntax expression)
            : base(SyntaxKind.ExpressionElement, expression.Span)
        {
            Expression = expression;
        }
    }
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
    public sealed class CheckedExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken Keyword { get; }
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
    public sealed class PrefixUnaryExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Operand { get; }

        public PrefixUnaryExpressionSyntax(SyntaxKind kind, SyntaxToken operatorToken, ExpressionSyntax operand)
            : base(kind, NodeSpan.From(operatorToken.Span, operand.Span))
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }
    }
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
    public abstract class VariableDesignationSyntax : SyntaxNode
    {
        protected VariableDesignationSyntax(SyntaxKind kind, TextSpan span) : base(kind, span) { }
    }

    public sealed class SingleVariableDesignationSyntax : VariableDesignationSyntax
    {
        public SyntaxToken Identifier { get; }

        public SingleVariableDesignationSyntax(SyntaxToken identifier)
            : base(SyntaxKind.SingleVariableDesignation, identifier.Span)
        {
            Identifier = identifier;
        }
    }
    public sealed class ThisExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken ThisKeyword { get; }

        public ThisExpressionSyntax(SyntaxToken thisKeyword)
            : base(SyntaxKind.ThisExpression, thisKeyword.Span)
        {
            ThisKeyword = thisKeyword;
        }
    }
    public sealed class DiscardDesignationSyntax : VariableDesignationSyntax
    {
        public SyntaxToken UnderscoreToken { get; }

        public DiscardDesignationSyntax(SyntaxToken underscoreToken)
            : base(SyntaxKind.DiscardDesignation, underscoreToken.Span)
        {
            UnderscoreToken = underscoreToken;
        }
    }
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
    public sealed class BaseExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken BaseKeyword { get; }

        public BaseExpressionSyntax(SyntaxToken baseKeyword)
            : base(SyntaxKind.BaseExpression, baseKeyword.Span)
        {
            BaseKeyword = baseKeyword;
        }
    }

    public sealed class LiteralExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken LiteralToken { get; }

        public LiteralExpressionSyntax(SyntaxKind kind, SyntaxToken literalToken)
            : base(kind, literalToken.Span)
        {
            LiteralToken = literalToken;
        }
    }

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
    public sealed class PostfixUnaryExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Operand { get; }
        public SyntaxToken OperatorToken { get; }

        public PostfixUnaryExpressionSyntax(SyntaxKind kind, ExpressionSyntax operand, SyntaxToken operatorToken)
            : base(kind, NodeSpan.From(operand.Span, operatorToken.Span))
        {
            Operand = operand;
            OperatorToken = operatorToken;
        }
    }

    public sealed class BinaryExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Left { get; }
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
    public sealed class AssignmentExpressionSyntax : ExpressionSyntax
    {
        public ExpressionSyntax Left { get; }
        public SyntaxToken OperatorToken { get; } // '='
        public ExpressionSyntax Right { get; }

        public AssignmentExpressionSyntax(SyntaxKind kind, ExpressionSyntax left, SyntaxToken operatorToken, ExpressionSyntax right)
            : base(kind, NodeSpan.From(left.Span, right.Span))
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }

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

    public sealed class NameColonSyntax : SyntaxNode
    {
        public IdentifierNameSyntax Name { get; }
        public SyntaxToken ColonToken { get; }

        public NameColonSyntax(IdentifierNameSyntax name, SyntaxToken colonToken)
            : base(SyntaxKind.NameColon, NodeSpan.From(name.Span, colonToken.Span))
        {
            Name = name;
            ColonToken = colonToken;
        }
    }

    public sealed class ArgumentSyntax : SyntaxNode
    {
        public NameColonSyntax? NameColon { get; }
        public SyntaxToken? RefKindKeyword { get; }
        public ExpressionSyntax Expression { get; }

        public ArgumentSyntax(NameColonSyntax? nameColon, SyntaxToken? refKindKeyword, ExpressionSyntax expression)
        : base(SyntaxKind.Argument, NodeSpan.FromNonNull(nameColon?.Span, refKindKeyword?.Span, expression.Span))
        {
            NameColon = nameColon;
            RefKindKeyword = refKindKeyword;
            Expression = expression;
        }
    }

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
    public sealed class OmittedArraySizeExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken OmittedArraySizeExpressionToken { get; }

        public OmittedArraySizeExpressionSyntax(SyntaxToken token)
            : base(SyntaxKind.OmittedArraySizeExpression, token.Span)
        {
            OmittedArraySizeExpressionToken = token;
        }
    }

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
    public sealed class AccessorDeclarationSyntax : SyntaxNode
    {
        public SyntaxList<AttributeListSyntax> AttributeLists { get; }
        public SyntaxTokenList Modifiers { get; }
        public SyntaxToken Keyword { get; }
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
    public abstract class LambdaExpressionSyntax : ExpressionSyntax
    {
        public SyntaxToken StaticKeyword { get; } // optional
        public SyntaxToken AsyncKeyword { get; } // optional
        public SyntaxToken ArrowToken { get; }
        public SyntaxNode Body { get; } // BlockSyntax or ExpressionSyntax

        protected LambdaExpressionSyntax(
            SyntaxKind kind,
            SyntaxToken staticKeyword,
            SyntaxToken asyncKeyword,
            SyntaxToken arrowToken,
            SyntaxNode body,
            TextSpan span)
            : base(kind, span)
        {
            StaticKeyword = staticKeyword;
            AsyncKeyword = asyncKeyword;
            ArrowToken = arrowToken;
            Body = body;
        }
    }

    public sealed class SimpleLambdaExpressionSyntax : LambdaExpressionSyntax
    {
        public ParameterSyntax Parameter { get; }

        public SimpleLambdaExpressionSyntax(
            SyntaxToken staticKeyword,
            SyntaxToken asyncKeyword,
            ParameterSyntax parameter,
            SyntaxToken arrowToken,
            SyntaxNode body)
            : base(
                SyntaxKind.SimpleLambdaExpression,
                staticKeyword,
                asyncKeyword,
                arrowToken,
                body,
                NodeSpan.FromNonNull(
                    asyncKeyword.Span.Length != 0 ? asyncKeyword.Span : (TextSpan?)null,
                    parameter.Span,
                    arrowToken.Span,
                    body.Span))
        {
            Parameter = parameter;
        }
    }
    public sealed class ParenthesizedLambdaExpressionSyntax : LambdaExpressionSyntax
    {
        public ParameterListSyntax ParameterList { get; }

        public ParenthesizedLambdaExpressionSyntax(
            SyntaxToken staticKeyword,
            SyntaxToken asyncKeyword,
            ParameterListSyntax parameterList,
            SyntaxToken arrowToken,
            SyntaxNode body)
            : base(
                SyntaxKind.ParenthesizedLambdaExpression,
                staticKeyword,
                asyncKeyword,
                arrowToken,
                body,
                NodeSpan.FromNonNull(
                    asyncKeyword.Span.Length != 0 ? asyncKeyword.Span : (TextSpan?)null,
                    parameterList.Span,
                    arrowToken.Span,
                    body.Span))
        {
            ParameterList = parameterList;
        }
    }
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
