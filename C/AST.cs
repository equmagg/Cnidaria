using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Cnidaria.C
{
    ///<summary>Contains the translation unit, syntax diagnostics and final typedef state</summary>
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

    ///<summary>Base class for parsed source constructs</summary>
    public abstract class SyntaxNode
    {
        public abstract SyntaxKind Kind { get; }
    }

    ///<summary>Represents the complete source file</summary>
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

    ///<summary>Represents declaration specifiers followed by init declarators</summary>
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

    ///<summary>Represents declaration specifiers, a function declarator and its body</summary>
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

    ///<summary>Represents a static assertion with an optional message expression</summary>
    public sealed class StaticAssertDeclarationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.StaticAssertDeclaration;

        public SyntaxToken StaticAssertKeyword { get; } // '_Static_assert' or 'static_assert'
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

    ///<summary>Represents a declarator with optional register binding and initializer</summary>
    public sealed class InitDeclaratorSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.InitDeclarator;

        public DeclaratorSyntax Declarator { get; }
        public SyntaxToken? AsmKeyword { get; }
        public SyntaxToken? AsmOpenParenToken { get; }
        public ImmutableArray<SyntaxToken> AsmRegisterNameTokens { get; }
        public SyntaxToken? AsmCloseParenToken { get; }
        public SyntaxToken? EqualsToken { get; }
        public InitializerSyntax? Initializer { get; }

        public InitDeclaratorSyntax(
            DeclaratorSyntax declarator,
            SyntaxToken? asmKeyword,
            SyntaxToken? asmOpenParenToken,
            ImmutableArray<SyntaxToken> asmRegisterNameTokens,
            SyntaxToken? asmCloseParenToken,
            SyntaxToken? equalsToken,
            InitializerSyntax? initializer)
        {
            Declarator = declarator;
            AsmKeyword = asmKeyword;
            AsmOpenParenToken = asmOpenParenToken;
            AsmRegisterNameTokens = asmRegisterNameTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : asmRegisterNameTokens;
            AsmCloseParenToken = asmCloseParenToken;
            EqualsToken = equalsToken;
            Initializer = initializer;
        }

        public string? ExplicitRegisterName => AsmRegisterNameTokens.Length == 0
            ? null
            : AsmSyntaxHelpers.ConcatenateStringLiterals(AsmRegisterNameTokens);
    }

    ///<summary>Preserves the full declarator token sequence and its declared identifier</summary>
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

    ///<summary>Base class for expression and brace-delimited initializers</summary>
    public abstract class InitializerSyntax : SyntaxNode
    {
    }

    ///<summary>Wraps an assignment expression used as an initializer</summary>
    public sealed class ExpressionInitializerSyntax : InitializerSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.ExpressionInitializer;

        public ExpressionSyntax Expression { get; }

        public ExpressionInitializerSyntax(ExpressionSyntax expression)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }
    }

    ///<summary>Represents a brace-delimited initializer list</summary>
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

    ///<summary>Represents one initializer with optional designators, equals token and trailing comma</summary>
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

    ///<summary>Base class for field and array designators in an initializer list</summary>
    public abstract class DesignatorSyntax : SyntaxNode
    {
    }

    ///<summary>Represents a '.field' initializer designator</summary>
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

    ///<summary>Represents a bracketed index initializer designator</summary>
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

    ///<summary>Base class for statement syntax</summary>
    public abstract class StatementSyntax : SyntaxNode
    {

    }

    ///<summary>Represents a brace-delimited sequence of declarations and statements</summary>
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
    ///<summary>Represents an if statement with an optional else branch</summary>
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

    ///<summary>Represents a switch controlling expression and embedded statement</summary>
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

    ///<summary>Represents a pre-tested while loop</summary>
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

    ///<summary>Represents a post-tested do-while loop</summary>
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

    ///<summary>Represents a for loop with declaration or expression initializer, condition and increment</summary>
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

    ///<summary>Represents a 'break;' statement</summary>
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

    ///<summary>Represents a 'continue;' statement</summary>
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

    ///<summary>Represents a goto statement targeting an identifier label</summary>
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

    ///<summary>Represents an identifier label followed by a statement</summary>
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

    ///<summary>Represents a case label and the following statement</summary>
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

    ///<summary>Represents a default label and the following statement</summary>
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

    ///<summary>Represents a return statement with an optional expression</summary>
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

    ///<summary>Represents an optional expression followed by a semicolon</summary>
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

    ///<summary>Represents one named or unnamed assembly operand with constraint and expression</summary>
    public sealed class AsmOperandSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.AsmOperand;

        public SyntaxToken? OpenBracketToken { get; }
        public SyntaxToken? NameToken { get; }
        public SyntaxToken? CloseBracketToken { get; }
        public ImmutableArray<SyntaxToken> ConstraintLiteralTokens { get; }
        public SyntaxToken OpenParenToken { get; }
        public ExpressionSyntax Expression { get; }
        public SyntaxToken CloseParenToken { get; }

        public AsmOperandSyntax(
            SyntaxToken? openBracketToken,
            SyntaxToken? nameToken,
            SyntaxToken? closeBracketToken,
            ImmutableArray<SyntaxToken> constraintLiteralTokens,
            SyntaxToken openParenToken,
            ExpressionSyntax expression,
            SyntaxToken closeParenToken)
        {
            OpenBracketToken = openBracketToken;
            NameToken = nameToken;
            CloseBracketToken = closeBracketToken;
            ConstraintLiteralTokens = constraintLiteralTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : constraintLiteralTokens;
            OpenParenToken = openParenToken;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            CloseParenToken = closeParenToken;
        }

        public string? Name => NameToken?.Text;
        public string Constraint => AsmSyntaxHelpers.ConcatenateStringLiterals(ConstraintLiteralTokens);
    }

    ///<summary>Represents one assembly clobber name assembled from adjacent string literals</summary>
    public sealed class AsmClobberSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.AsmClobber;

        public ImmutableArray<SyntaxToken> StringLiteralTokens { get; }

        public AsmClobberSyntax(ImmutableArray<SyntaxToken> stringLiteralTokens)
        {
            StringLiteralTokens = stringLiteralTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : stringLiteralTokens;
        }

        public string Text => AsmSyntaxHelpers.ConcatenateStringLiterals(StringLiteralTokens);
    }

    ///<summary>Represents an extended assembly statement including operands, clobbers and goto labels</summary>
    public sealed class AsmStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.AsmStatement;

        public SyntaxToken AsmKeyword { get; }
        public ImmutableArray<SyntaxToken> QualifierTokens { get; }
        public SyntaxToken OpenParenToken { get; }
        public ImmutableArray<SyntaxToken> StringLiteralTokens { get; }
        public ImmutableArray<AsmOperandSyntax> OutputOperands { get; }
        public ImmutableArray<AsmOperandSyntax> InputOperands { get; }
        public ImmutableArray<AsmClobberSyntax> Clobbers { get; }
        public ImmutableArray<SyntaxToken> GotoLabelTokens { get; }
        public SyntaxToken CloseParenToken { get; }
        public SyntaxToken SemicolonToken { get; }

        public AsmStatementSyntax(
            SyntaxToken asmKeyword,
            ImmutableArray<SyntaxToken> qualifierTokens,
            SyntaxToken openParenToken,
            ImmutableArray<SyntaxToken> stringLiteralTokens,
            ImmutableArray<AsmOperandSyntax> outputOperands,
            ImmutableArray<AsmOperandSyntax> inputOperands,
            ImmutableArray<AsmClobberSyntax> clobbers,
            ImmutableArray<SyntaxToken> gotoLabelTokens,
            SyntaxToken closeParenToken,
            SyntaxToken semicolonToken)
        {
            AsmKeyword = asmKeyword;
            QualifierTokens = qualifierTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : qualifierTokens;
            OpenParenToken = openParenToken;
            StringLiteralTokens = stringLiteralTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : stringLiteralTokens;
            OutputOperands = outputOperands.IsDefault
                ? ImmutableArray<AsmOperandSyntax>.Empty
                : outputOperands;
            InputOperands = inputOperands.IsDefault
                ? ImmutableArray<AsmOperandSyntax>.Empty
                : inputOperands;
            Clobbers = clobbers.IsDefault
                ? ImmutableArray<AsmClobberSyntax>.Empty
                : clobbers;
            GotoLabelTokens = gotoLabelTokens.IsDefault
                ? ImmutableArray<SyntaxToken>.Empty
                : gotoLabelTokens;
            CloseParenToken = closeParenToken;
            SemicolonToken = semicolonToken;
        }

        public string Text => AsmSyntaxHelpers.ConcatenateStringLiterals(StringLiteralTokens);

        public bool IsVolatile => QualifierTokens.Any(static token => IsVolatileQualifier(token));
        public bool IsInline => QualifierTokens.Any(static token => IsInlineQualifier(token));
        public bool IsGoto => GotoLabelTokens.Length != 0 || QualifierTokens.Any(static token => IsGotoQualifier(token));

        private static bool IsVolatileQualifier(SyntaxToken token)
            => token.Kind is SyntaxKind.VolatileKeyword or SyntaxKind.VolatileExtensionKeyword;

        private static bool IsInlineQualifier(SyntaxToken token)
            => token.Kind is SyntaxKind.InlineKeyword or SyntaxKind.InlineExtensionKeyword;

        private static bool IsGotoQualifier(SyntaxToken token)
            => token.Kind == SyntaxKind.GotoKeyword || string.Equals(token.Text, "__goto__", StringComparison.Ordinal);
    }

    ///<summary>Decodes and concatenates adjacent string literal tokens used by assembly syntax</summary>
    internal static class AsmSyntaxHelpers
    {
        public static string ConcatenateStringLiterals(ImmutableArray<SyntaxToken> tokens)
        {
            var builder = new StringBuilder();
            foreach (var token in tokens)
                builder.Append(token.Value as string ?? string.Empty);
            return builder.ToString();
        }
    }

    ///<summary>Preserves tokens skipped while recovering at file scope</summary>
    public sealed class SkippedExternalDeclarationSyntax : SyntaxNode
    {
        public override SyntaxKind Kind => SyntaxKind.SkippedExternalDeclaration;

        public ImmutableArray<SyntaxToken> Tokens { get; }

        public SkippedExternalDeclarationSyntax(ImmutableArray<SyntaxToken> tokens)
        {
            Tokens = tokens;
        }
    }

    ///<summary>Preserves tokens skipped while recovering inside a statement</summary>
    public sealed class SkippedStatementSyntax : StatementSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.SkippedStatement;

        public ImmutableArray<SyntaxToken> Tokens { get; }

        public SkippedStatementSyntax(ImmutableArray<SyntaxToken> tokens)
        {
            Tokens = tokens;
        }
    }

    ///<summary>Base class for expression syntax</summary>
    public abstract class ExpressionSyntax : SyntaxNode
    {
    }

    ///<summary>Represents one literal token</summary>
    public sealed class LiteralExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.LiteralExpression;

        public SyntaxToken LiteralToken { get; }

        public LiteralExpressionSyntax(SyntaxToken literalToken)
        {
            LiteralToken = literalToken;
        }
    }

    ///<summary>Represents an identifier used as an expression</summary>
    public sealed class NameExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.NameExpression;

        public SyntaxToken IdentifierToken { get; }

        public NameExpressionSyntax(SyntaxToken identifierToken)
        {
            IdentifierToken = identifierToken;
        }
    }

    ///<summary>Represents a prefix unary operator and its operand</summary>
    public sealed class UnaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.UnaryExpression;

        public SyntaxToken OperatorToken { get; } // '+', '-', '!', '~', '*', '&', '++' or '--'
        public ExpressionSyntax Operand { get; }

        public UnaryExpressionSyntax(
            SyntaxToken operatorToken,
            ExpressionSyntax operand)
        {
            OperatorToken = operatorToken;
            Operand = operand;
        }
    }

    ///<summary>Represents a binary operator and its left and right operands</summary>
    public sealed class BinaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.BinaryExpression;

        public ExpressionSyntax Left { get; }
        // ',', '||', '&&', '|', '^', '&', '==', '!=', '<', '<=', '>', '>='
        // '<<', '>>', '+', '-', '*', '/' or '%'
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

    ///<summary>Represents an assignment operator and its left and right operands</summary>
    public sealed class AssignmentExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;

        public ExpressionSyntax Left { get; }
        // '=', '+=', '-=', '*=', '/=', '%=', '&=', '|=', '^=', '<<=' or '>>='
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

    ///<summary>Represents the ternary conditional expression</summary>
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

    ///<summary>Represents a parenthesized type name followed by the expression being cast</summary>
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

    ///<summary>Represents a size or alignment query applied to a type name or expression</summary>
    public sealed class SizeofExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.SizeofExpression;

        public SyntaxToken Keyword { get; } // 'sizeof', 'alignof' or '_Alignof'
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

    ///<summary>Represents an expression enclosed in parentheses</summary>
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

    ///<summary>Represents a parenthesized type name followed by a brace initializer</summary>
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

    ///<summary>Represents a generic selection with its controlling expression and associations</summary>
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

    ///<summary>Represents one type-name or default association in a generic selection</summary>
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

    ///<summary>Represents a compound statement used as an expression</summary>
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

    ///<summary>Represents invocation of an expression with arguments</summary>
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

    ///<summary>Represents bracketed indexing on an expression</summary>
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

    ///<summary>Represents member selection through '.' or '->'</summary>
    public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.MemberAccessExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; } // '.' or '->'
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

    ///<summary>Represents a postfix increment or decrement expression</summary>
    public sealed class PostfixUnaryExpressionSyntax : ExpressionSyntax
    {
        public override SyntaxKind Kind => SyntaxKind.PostfixUnaryExpression;

        public ExpressionSyntax Expression { get; }
        public SyntaxToken OperatorToken { get; } // '++' or '--'

        public PostfixUnaryExpressionSyntax(
            ExpressionSyntax expression,
            SyntaxToken operatorToken)
        {
            Expression = expression;
            OperatorToken = operatorToken;
        }
    }

    ///<summary>Preserves the token that produced an expression recovery node</summary>
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
