using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.C
{
    public sealed class ParseResult
    {
        public TranslationUnitSyntax Root { get; }
        public ImmutableArray<SyntaxDiagnostic> Diagnostics { get; }
        public TypeNameTable TypeNames { get; }

        public ParseResult(
            TranslationUnitSyntax root,
            ImmutableArray<SyntaxDiagnostic> diagnostics,
            TypeNameTable typeNames)
        {
            Root = root;
            Diagnostics = diagnostics;
            TypeNames = typeNames;
        }
    }

    public abstract class SyntaxNode
    {
        public abstract SyntaxKind Kind { get; }
    }

    public sealed class TranslationUnitSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.TranslationUnit;

        public ImmutableArray<SyntaxNode> Members { get; }
        public SyntaxToken EndOfFileToken { get; }

        public TranslationUnitSyntax(
            ImmutableArray<SyntaxNode> members,
            SyntaxToken endOfFileToken)
        {
            Members = members;
            EndOfFileToken = endOfFileToken;
        }
    }

    public sealed class DeclarationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.Declaration;

        public ImmutableArray<SyntaxToken> Specifiers { get; }
        public ImmutableArray<InitDeclaratorSyntax> Declarators { get; }
        public SyntaxToken SemicolonToken { get; }

        public bool IsTypedef => Specifiers.Any(static t => t.Kind == SyntaxKind.TypedefKeyword);

        public DeclarationSyntax(
            ImmutableArray<SyntaxToken> specifiers,
            ImmutableArray<InitDeclaratorSyntax> declarators,
            SyntaxToken semicolonToken)
        {
            Specifiers = specifiers;
            Declarators = declarators;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class FunctionDefinitionSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.FunctionDefinition;

        public ImmutableArray<SyntaxToken> Specifiers { get; }
        public DeclaratorSyntax Declarator { get; }
        public CompoundStatementSyntax Body { get; }

        public FunctionDefinitionSyntax(
            ImmutableArray<SyntaxToken> specifiers,
            DeclaratorSyntax declarator,
            CompoundStatementSyntax body)
        {
            Specifiers = specifiers;
            Declarator = declarator;
            Body = body;
        }
    }

    public sealed class StaticAssertDeclarationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.StaticAssertDeclaration;

        public SyntaxToken StaticAssertKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Condition { get; }
        public SyntaxToken? CommaToken { get; }
        public ExpressionSyntax? Message { get; }
        public SyntaxToken CloseParenToken { get; }
        public SyntaxToken SemicolonToken { get; }

        public StaticAssertDeclarationSyntax(
            SyntaxToken staticAssertKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax condition,
            SyntaxToken? commaToken,
            ExpressionSyntax? message,
            SyntaxToken closeParenToken,
            SyntaxToken semicolonToken)
        {
            StaticAssertKeyword = staticAssertKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CommaToken = commaToken;
            Message = message;
            CloseParenToken = closeParenToken;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class InitDeclaratorSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.InitDeclarator;

        public DeclaratorSyntax Declarator { get; }
        public SyntaxToken? EqualsToken { get; }
        public InitializerSyntax? Initializer { get; }

        public InitDeclaratorSyntax(
            DeclaratorSyntax declarator,
            SyntaxToken? equalsToken,
            InitializerSyntax? initializer)
        {
            Declarator = declarator;
            EqualsToken = equalsToken;
            Initializer = initializer;
        }
    }

    public sealed class DeclaratorSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.Declarator;

        public ImmutableArray<SyntaxToken> Tokens { get; }
        public SyntaxToken? Identifier { get; }

        public DeclaratorSyntax(
            ImmutableArray<SyntaxToken> tokens,
            SyntaxToken? identifier)
        {
            Tokens = tokens;
            Identifier = identifier;
        }
    }

    public abstract class InitializerSyntax : SyntaxNode
    {
    }

    public sealed class ExpressionInitializerSyntax : InitializerSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ExpressionInitializer;

        public ExpressionSyntax Expression { get; }

        public ExpressionInitializerSyntax(ExpressionSyntax expression)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    public sealed class InitializerListSyntax : InitializerSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.InitializerList;

        public SyntaxToken OpenBraceToken { get; }
        public ImmutableArray<InitializerListItemSyntax> Items { get; }
        public SyntaxToken CloseBraceToken { get; }

        public InitializerListSyntax(
            SyntaxToken openBraceToken,
            ImmutableArray<InitializerListItemSyntax> items,
            SyntaxToken closeBraceToken)
        {
            OpenBraceToken = openBraceToken;
            Items = items.IsDefault
                ? ImmutableArray<InitializerListItemSyntax>.Empty
                : items;
            CloseBraceToken = closeBraceToken;
        }
    }

    public sealed class InitializerListItemSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.InitializerListItem;

        public ImmutableArray<DesignatorSyntax> Designators { get; }
        public SyntaxToken? EqualsToken { get; }
        public InitializerSyntax Initializer { get; }
        public SyntaxToken? CommaToken { get; }

        public InitializerListItemSyntax(
            ImmutableArray<DesignatorSyntax> designators,
            SyntaxToken? equalsToken,
            InitializerSyntax initializer,
            SyntaxToken? commaToken)
        {
            Designators = designators.IsDefault
                ? ImmutableArray<DesignatorSyntax>.Empty
                : designators;
            EqualsToken = equalsToken;
            Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            CommaToken = commaToken;
        }
    }

    public abstract class DesignatorSyntax : SyntaxNode
    {
    }

    public sealed class FieldDesignatorSyntax : DesignatorSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.FieldDesignator;

        public SyntaxToken DotToken { get; }
        public SyntaxToken NameToken { get; }

        public FieldDesignatorSyntax(SyntaxToken dotToken, SyntaxToken nameToken)
        {
            DotToken = dotToken;
            NameToken = nameToken;
        }
    }

    public sealed class ArrayDesignatorSyntax : DesignatorSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ArrayDesignator;

        public SyntaxToken OpenBracketToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseBracketToken { get; }

        public ArrayDesignatorSyntax(
            SyntaxToken openBracketToken,
            ExpressionSyntax expression,
            SyntaxToken closeBracketToken)
        {
            OpenBracketToken = openBracketToken;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            CloseBracketToken = closeBracketToken;
        }
    }

    public abstract class StatementSyntax : SyntaxNode
    {

    }

    public sealed class CompoundStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.CompoundStatement;

        public SyntaxToken OpenBraceToken { get; }
        public ImmutableArray<SyntaxNode> Members { get; }
        public SyntaxToken CloseBraceToken { get; }

        public CompoundStatementSyntax(
            SyntaxToken openBraceToken,
            ImmutableArray<SyntaxNode> members,
            SyntaxToken closeBraceToken)
        {
            OpenBraceToken = openBraceToken;
            Members = members;
            CloseBraceToken = closeBraceToken;
        }
    }
    public sealed class IfStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.IfStatement;

        public SyntaxToken IfKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Condition { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax ThenStatement { get; }
        public SyntaxToken? ElseKeyword { get; }
        public StatementSyntax? ElseStatement { get; }

        public IfStatementSyntax(
            SyntaxToken ifKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax condition,
            SyntaxToken closeParenToken,
            StatementSyntax thenStatement,
            SyntaxToken? elseKeyword,
            StatementSyntax? elseStatement)
        {
            IfKeyword = ifKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CloseParenToken = closeParenToken;
            ThenStatement = thenStatement;
            ElseKeyword = elseKeyword;
            ElseStatement = elseStatement;
        }
    }

    public sealed class SwitchStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.SwitchStatement;

        public SyntaxToken SwitchKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public SwitchStatementSyntax(
            SyntaxToken switchKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
        {
            SwitchKeyword = switchKeyword;
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }

    public sealed class WhileStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.WhileStatement;

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
        {
            WhileKeyword = whileKeyword;
            OpenParenToken = openParenToken;
            Condition = condition;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }

    public sealed class DoStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.DoStatement;

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

    public sealed class ForStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ForStatement;

        public SyntaxToken ForKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public SyntaxNode? Initializer { get; }
        public SyntaxToken FirstSemicolonToken { get; }
        public ExpressionSyntax? Condition { get; }
        public SyntaxToken SecondSemicolonToken { get; }
        public ExpressionSyntax? Increment { get; }
        public SyntaxToken CloseParenToken { get; }
        public StatementSyntax Statement { get; }

        public ForStatementSyntax(
            SyntaxToken forKeyword,
            SyntaxToken openParenToken,
            SyntaxNode? initializer,
            SyntaxToken firstSemicolonToken,
            ExpressionSyntax? condition,
            SyntaxToken secondSemicolonToken,
            ExpressionSyntax? increment,
            SyntaxToken closeParenToken,
            StatementSyntax statement)
        {
            ForKeyword = forKeyword;
            OpenParenToken = openParenToken;
            Initializer = initializer;
            FirstSemicolonToken = firstSemicolonToken;
            Condition = condition;
            SecondSemicolonToken = secondSemicolonToken;
            Increment = increment;
            CloseParenToken = closeParenToken;
            Statement = statement;
        }
    }

    public sealed class BreakStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.BreakStatement;

        public SyntaxToken BreakKeyword { get; }
        public SyntaxToken SemicolonToken { get; }

        public BreakStatementSyntax(SyntaxToken breakKeyword, SyntaxToken semicolonToken)
        {
            BreakKeyword = breakKeyword;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class ContinueStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ContinueStatement;

        public SyntaxToken ContinueKeyword { get; }
        public SyntaxToken SemicolonToken { get; }

        public ContinueStatementSyntax(SyntaxToken continueKeyword, SyntaxToken semicolonToken)
        {
            ContinueKeyword = continueKeyword;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class GotoStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.GotoStatement;

        public SyntaxToken GotoKeyword { get; }
        public SyntaxToken IdentifierToken { get; }
        public SyntaxToken SemicolonToken { get; }

        public GotoStatementSyntax(
            SyntaxToken gotoKeyword,
            SyntaxToken identifierToken,
            SyntaxToken semicolonToken)
        {
            GotoKeyword = gotoKeyword;
            IdentifierToken = identifierToken;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class LabelStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.LabelStatement;

        public SyntaxToken IdentifierToken { get; }
        public SyntaxToken ColonToken { get; }
        public StatementSyntax Statement { get; }

        public LabelStatementSyntax(
            SyntaxToken identifierToken,
            SyntaxToken colonToken,
            StatementSyntax statement)
        {
            IdentifierToken = identifierToken;
            ColonToken = colonToken;
            Statement = statement;
        }
    }

    public sealed class CaseStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.CaseStatement;

        public SyntaxToken CaseKeyword { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken ColonToken { get; }
        public StatementSyntax Statement { get; }

        public CaseStatementSyntax(
            SyntaxToken caseKeyword,
            ExpressionSyntax expression,
            SyntaxToken colonToken,
            StatementSyntax statement)
        {
            CaseKeyword = caseKeyword;
            Expression = expression;
            ColonToken = colonToken;
            Statement = statement;
        }
    }

    public sealed class DefaultStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.DefaultStatement;

        public SyntaxToken DefaultKeyword { get; }
        public SyntaxToken ColonToken { get; }
        public StatementSyntax Statement { get; }

        public DefaultStatementSyntax(
            SyntaxToken defaultKeyword,
            SyntaxToken colonToken,
            StatementSyntax statement)
        {
            DefaultKeyword = defaultKeyword;
            ColonToken = colonToken;
            Statement = statement;
        }
    }

    public sealed class ReturnStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ReturnStatement;

        public SyntaxToken ReturnKeyword { get; }
        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public ReturnStatementSyntax(
            SyntaxToken returnKeyword,
            ExpressionSyntax? expression,
            SyntaxToken semicolonToken)
        {
            ReturnKeyword = returnKeyword;
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class ExpressionStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ExpressionStatement;

        public ExpressionSyntax? Expression { get; }
        public SyntaxToken SemicolonToken { get; }

        public ExpressionStatementSyntax(
            ExpressionSyntax? expression,
            SyntaxToken semicolonToken)
        {
            Expression = expression;
            SemicolonToken = semicolonToken;
        }
    }

    public sealed class SkippedExternalDeclarationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.SkippedExternalDeclaration;

        public ImmutableArray<SyntaxToken> Tokens { get; }

        public SkippedExternalDeclarationSyntax(ImmutableArray<SyntaxToken> tokens)
        {
            Tokens = tokens;
        }
    }

    public sealed class SkippedStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.SkippedStatement;

        public ImmutableArray<SyntaxToken> Tokens { get; }

        public SkippedStatementSyntax(ImmutableArray<SyntaxToken> tokens)
        {
            Tokens = tokens;
        }
    }

    public abstract class ExpressionSyntax : SyntaxNode
    {
    }

    public sealed class LiteralExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.LiteralExpression;

        public SyntaxToken LiteralToken { get; }

        public LiteralExpressionSyntax(SyntaxToken literalToken)
        {
            LiteralToken = literalToken;
        }
    }

    public sealed class NameExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.NameExpression;

        public SyntaxToken IdentifierToken { get; }

        public NameExpressionSyntax(SyntaxToken identifierToken)
        {
            IdentifierToken = identifierToken;
        }
    }

    public sealed class UnaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.UnaryExpression;

        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Operand { get; }

        public UnaryExpressionSyntax(
            SyntaxToken operatorToken,
            ExpressionSyntax operand)
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }
    }

    public sealed class BinaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.BinaryExpression;

        public ExpressionSyntax Left { get; }
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Right { get; }

        public BinaryExpressionSyntax(
            ExpressionSyntax left,
            SyntaxToken operatorToken,
            ExpressionSyntax right)
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }

    public sealed class AssignmentExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;

        public ExpressionSyntax Left { get; }
        public SyntaxToken OperatorToken { get; }
        public ExpressionSyntax Right { get; }

        public AssignmentExpressionSyntax(
            ExpressionSyntax left,
            SyntaxToken operatorToken,
            ExpressionSyntax right)
        {
            Left = left;
            OperatorToken = operatorToken;
            Right = right;
        }
    }

    public sealed class ConditionalExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ConditionalExpression;

        public ExpressionSyntax Condition { get; }
        public SyntaxToken QuestionToken { get; }
        public ExpressionSyntax WhenTrue { get; }
        public SyntaxToken ColonToken { get; }
        public ExpressionSyntax WhenFalse { get; }

        public ConditionalExpressionSyntax(
            ExpressionSyntax condition,
            SyntaxToken questionToken,
            ExpressionSyntax whenTrue,
            SyntaxToken colonToken,
            ExpressionSyntax whenFalse)
        {
            Condition = condition;
            QuestionToken = questionToken;
            WhenTrue = whenTrue;
            ColonToken = colonToken;
            WhenFalse = whenFalse;
        }
    }

    public sealed class CastExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.CastExpression;

        public SyntaxToken OpenParenToken { get; }
        public ImmutableArray<SyntaxToken> TypeNameTokens { get; }
        public SyntaxToken CloseParenToken { get; }
        public ExpressionSyntax Expression { get; }

        public CastExpressionSyntax(
            SyntaxToken openParenToken,
            ImmutableArray<SyntaxToken> typeNameTokens,
            SyntaxToken closeParenToken,
            ExpressionSyntax expression)
        {
            OpenParenToken = openParenToken;
            TypeNameTokens = typeNameTokens;
            CloseParenToken = closeParenToken;
            Expression = expression;
        }
    }

    public sealed class SizeofExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.SizeofExpression;

        public SyntaxToken Keyword { get; }
        public SyntaxToken? OpenParenToken { get; }
        public ImmutableArray<SyntaxToken> TypeNameTokens { get; }
        public SyntaxToken? CloseParenToken { get; }
        public ExpressionSyntax? Expression { get; }

        public SizeofExpressionSyntax(
            SyntaxToken keyword,
            SyntaxToken? openParenToken,
            ImmutableArray<SyntaxToken> typeNameTokens,
            SyntaxToken? closeParenToken,
            ExpressionSyntax? expression)
        {
            Keyword = keyword;
            OpenParenToken = openParenToken;
            TypeNameTokens = typeNameTokens;
            CloseParenToken = closeParenToken;
            Expression = expression;
        }
    }

    public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ParenthesizedExpression;

        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }

        public ParenthesizedExpressionSyntax(
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken)
        {
            OpenParenToken = openParenToken;
            Expression = expression;
            CloseParenToken = closeParenToken;
        }
    }

    public sealed class CompoundLiteralExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.CompoundLiteralExpression;

        public SyntaxToken OpenParenToken { get; }
        public ImmutableArray<SyntaxToken> TypeNameTokens { get; }
        public SyntaxToken CloseParenToken { get; }
        public SyntaxToken OpenBraceToken { get; }
        public InitializerListSyntax? InitializerList { get; }

        public SyntaxToken CloseBraceToken { get; }

        public CompoundLiteralExpressionSyntax(
            SyntaxToken openParenToken,
            ImmutableArray<SyntaxToken> typeNameTokens,
            SyntaxToken closeParenToken,
            InitializerListSyntax initializerList)
        {
            OpenParenToken = openParenToken;
            TypeNameTokens = typeNameTokens;
            CloseParenToken = closeParenToken;
            InitializerList = initializerList ?? throw new ArgumentNullException(nameof(initializerList));
            OpenBraceToken = initializerList.OpenBraceToken;
            CloseBraceToken = initializerList.CloseBraceToken;
        }

    }

    public sealed class GenericSelectionExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.GenericSelectionExpression;

        public SyntaxToken GenericKeyword { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax ControlExpression { get; }
        public SyntaxToken CommaToken { get; }
        public ImmutableArray<GenericAssociationSyntax> Associations { get; }
        public SyntaxToken CloseParenToken { get; }

        public GenericSelectionExpressionSyntax(
            SyntaxToken genericKeyword,
            SyntaxToken openParenToken,
            ExpressionSyntax controlExpression,
            SyntaxToken commaToken,
            ImmutableArray<GenericAssociationSyntax> associations,
            SyntaxToken closeParenToken)
        {
            GenericKeyword = genericKeyword;
            OpenParenToken = openParenToken;
            ControlExpression = controlExpression;
            CommaToken = commaToken;
            Associations = associations;
            CloseParenToken = closeParenToken;
        }
    }

    public sealed class GenericAssociationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.GenericAssociation;

        public SyntaxToken? DefaultKeyword { get; }
        public ImmutableArray<SyntaxToken> TypeNameTokens { get; }
        public SyntaxToken ColonToken { get; }
        public ExpressionSyntax Expression { get; }

        public GenericAssociationSyntax(
            SyntaxToken? defaultKeyword,
            ImmutableArray<SyntaxToken> typeNameTokens,
            SyntaxToken colonToken,
            ExpressionSyntax expression)
        {
            DefaultKeyword = defaultKeyword;
            TypeNameTokens = typeNameTokens;
            ColonToken = colonToken;
            Expression = expression;
        }
    }

    public sealed class StatementExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.StatementExpression;

        public SyntaxToken OpenParenToken { get; }
        public CompoundStatementSyntax Statement { get; }
        public SyntaxToken CloseParenToken { get; }

        public StatementExpressionSyntax(
            SyntaxToken openParenToken,
            CompoundStatementSyntax statement,
            SyntaxToken closeParenToken)
        {
            OpenParenToken = openParenToken;
            Statement = statement;
            CloseParenToken = closeParenToken;
        }
    }

    public sealed class CallExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.CallExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OpenParenToken { get; }
        public ImmutableArray<ExpressionSyntax> Arguments { get; }
        public SyntaxToken CloseParenToken { get; }

        public CallExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken openParenToken,
            ImmutableArray<ExpressionSyntax> arguments,
            SyntaxToken closeParenToken)
        {
            Expression = expression;
            OpenParenToken = openParenToken;
            Arguments = arguments;
            CloseParenToken = closeParenToken;
        }
    }

    public sealed class ElementAccessExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ElementAccessExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OpenBracketToken { get; }
        public ExpressionSyntax? Index { get; }
        public SyntaxToken CloseBracketToken { get; }

        public ElementAccessExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken openBracketToken,
            ExpressionSyntax? index,
            SyntaxToken closeBracketToken)
        {
            Expression = expression;
            OpenBracketToken = openBracketToken;
            Index = index;
            CloseBracketToken = closeBracketToken;
        }
    }

    public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.MemberAccessExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; }
        public SyntaxToken NameToken { get; }

        public MemberAccessExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken operatorToken,
            SyntaxToken nameToken)
        {
            Expression = expression;
            OperatorToken = operatorToken;
            NameToken = nameToken;
        }
    }

    public sealed class PostfixUnaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.PostfixUnaryExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; }

        public PostfixUnaryExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken operatorToken)
        {
            Expression = expression;
            OperatorToken = operatorToken;
        }
    }

    public sealed class InvalidExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.InvalidExpression;

        public SyntaxToken Token { get; }

        public InvalidExpressionSyntax(SyntaxToken token)
        {
            Token = token;
        }
    }
}
