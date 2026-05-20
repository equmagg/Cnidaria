namespace Cnidaria.C
{
    public enum SyntaxKind : ushort
    {
        None = 0,

        // Special tokens

        BadToken,
        MissingToken,
        EndOfFileToken,

        // Trivia

        WhitespaceTrivia,
        EndOfLineTrivia,
        SingleLineCommentTrivia,
        MultiLineCommentTrivia,
        LineContinuationTrivia,
        DirectiveTrivia,
        DisabledTextTrivia,
        BomTrivia,

        // Identifiers

        IdentifierToken,
        TypedefNameToken,

        // Literals

        IntegerLiteralToken,
        FloatingLiteralToken,

        CharacterLiteralToken,
        WideCharacterLiteralToken,       // L'x'
        Utf8CharacterLiteralToken,       // u8'x' in C23
        Utf16CharacterLiteralToken,      // u'x'
        Utf32CharacterLiteralToken,      // U'x'

        StringLiteralToken,
        WideStringLiteralToken,          // L"..."
        Utf8StringLiteralToken,          // u8"..."
        Utf16StringLiteralToken,         // u"..."
        Utf32StringLiteralToken,         // U"..."

        // Punctuators / operators

        OpenParenToken,                  // (
        CloseParenToken,                 // )

        OpenBraceToken,                  // {
        CloseBraceToken,                 // }

        OpenBracketToken,                // [
        CloseBracketToken,               // ]

        SemicolonToken,                  // ;
        ColonToken,                      // :
        CommaToken,                      // ,
        DotToken,                        // .
        ArrowToken,                      // ->
        QuestionToken,                   // ?
        EllipsisToken,                   // ...

        PlusToken,                       // +
        PlusPlusToken,                   // ++
        PlusEqualsToken,                 // +=

        MinusToken,                      // -
        MinusMinusToken,                 // --
        MinusEqualsToken,                // -=

        StarToken,                       // *
        StarEqualsToken,                 // *=

        SlashToken,                      // /
        SlashEqualsToken,                // /=

        PercentToken,                    // %
        PercentEqualsToken,              // %=

        AmpersandToken,                  // &
        AmpersandAmpersandToken,         // &&
        AmpersandEqualsToken,            // &=

        PipeToken,                       // |
        PipePipeToken,                   // ||
        PipeEqualsToken,                 // |=

        HatToken,                        // ^
        HatEqualsToken,                  // ^=

        TildeToken,                      // ~

        BangToken,                       // !
        BangEqualsToken,                 // !=

        EqualsToken,                     // =
        EqualsEqualsToken,               // ==

        LessThanToken,                   // <
        LessThanEqualsToken,             // <=
        LessThanLessThanToken,           // <<
        LessThanLessThanEqualsToken,     // <<=

        GreaterThanToken,                // >
        GreaterThanEqualsToken,          // >=
        GreaterThanGreaterThanToken,     // >>
        GreaterThanGreaterThanEqualsToken, // >>=

        HashToken,                       // #
        HashHashToken,                   // ##

        BackslashToken,                  // \\

        // Digraph punctuators

        OpenBracketDigraphToken,         // <:
        CloseBracketDigraphToken,        // :>
        OpenBraceDigraphToken,           // <%
        CloseBraceDigraphToken,          // %>
        HashDigraphToken,                // %:
        HashHashDigraphToken,            // %:%:

        // ISO C90 / C95 keywords

        AutoKeyword,
        BreakKeyword,
        CaseKeyword,
        CharKeyword,
        ConstKeyword,
        ContinueKeyword,
        DefaultKeyword,
        DoKeyword,
        DoubleKeyword,
        ElseKeyword,
        EnumKeyword,
        ExternKeyword,
        FloatKeyword,
        ForKeyword,
        GotoKeyword,
        IfKeyword,
        IntKeyword,
        LongKeyword,
        RegisterKeyword,
        ReturnKeyword,
        ShortKeyword,
        SignedKeyword,
        SizeofKeyword,
        StaticKeyword,
        StructKeyword,
        SwitchKeyword,
        TypedefKeyword,
        UnionKeyword,
        UnsignedKeyword,
        VoidKeyword,
        VolatileKeyword,
        WhileKeyword,
        AtomicKeyword,

        // C99 keywords

        InlineKeyword,
        RestrictKeyword,

        UnderscoreBoolKeyword,           // _Bool
        UnderscoreComplexKeyword,        // _Complex
        UnderscoreImaginaryKeyword,      // _Imaginary
        UnderscorePragmaKeyword,         // _Pragma

        // C11 keywords

        UnderscoreAlignasKeyword,        // _Alignas
        UnderscoreAlignofKeyword,        // _Alignof
        UnderscoreAtomicKeyword,         // _Atomic
        UnderscoreGenericKeyword,        // _Generic
        UnderscoreNoreturnKeyword,       // _Noreturn
        UnderscoreStaticAssertKeyword,   // _Static_assert
        UnderscoreThreadLocalKeyword,    // _Thread_local

        // C23 keywords and keyword spellings

        AlignasKeyword,                  // alignas
        AlignofKeyword,                  // alignof
        BoolKeyword,                     // bool
        ConstexprKeyword,                // constexpr
        FalseKeyword,                    // false
        NullptrKeyword,                  // nullptr
        StaticAssertKeyword,             // static_assert
        ThreadLocalKeyword,              // thread_local
        TrueKeyword,                     // true
        TypeofKeyword,                   // typeof
        TypeofUnqualKeyword,             // typeof_unqual

        UnderscoreBitIntKeyword,         // _BitInt

        // Conditionally-supported keywords

        AsmKeyword,
        FortranKeyword,

        UnderscoreDecimal32Keyword,      // _Decimal32
        UnderscoreDecimal64Keyword,      // _Decimal64
        UnderscoreDecimal128Keyword,     // _Decimal128

        // Common extension keywords

        ExtensionKeyword,                // __extension__
        AttributeKeyword,                // __attribute__
        DeclspecKeyword,                 // __declspec

        BuiltinVaArgKeyword,             // __builtin_va_arg
        BuiltinOffsetofKeyword,          // __builtin_offsetof
        BuiltinTypesCompatiblePKeyword,  // __builtin_types_compatible_p
        BuiltinChooseExprKeyword,        // __builtin_choose_expr

        AsmExtensionKeyword,             // __asm, __asm__
        InlineExtensionKeyword,          // __inline, __inline__
        RestrictExtensionKeyword,        // __restrict, __restrict__
        TypeofExtensionKeyword,          // __typeof, __typeof__
        VolatileExtensionKeyword,        // __volatile, __volatile__
        ConstExtensionKeyword,           // __const, __const__

        // Syntax nodes

        TranslationUnit,
        FunctionDefinition,
        Declaration,
        StaticAssertDeclaration,
        InitDeclarator,
        Declarator,
        ExpressionInitializer,
        InitializerList,
        InitializerListItem,
        FieldDesignator,
        ArrayDesignator,

        CompoundStatement,
        IfStatement,
        SwitchStatement,
        WhileStatement,
        DoStatement,
        ForStatement,
        BreakStatement,
        ContinueStatement,
        GotoStatement,
        LabelStatement,
        CaseStatement,
        DefaultStatement,
        ReturnStatement,
        ExpressionStatement,
        SkippedExternalDeclaration,
        SkippedStatement,

        LiteralExpression,
        NameExpression,
        UnaryExpression,
        BinaryExpression,
        AssignmentExpression,
        ConditionalExpression,
        CastExpression,
        SizeofExpression,
        ParenthesizedExpression,
        CompoundLiteralExpression,
        GenericSelectionExpression,
        GenericAssociation,
        StatementExpression,
        CallExpression,
        ElementAccessExpression,
        MemberAccessExpression,
        PostfixUnaryExpression,
        InvalidExpression,
    }
}
