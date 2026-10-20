using System;

namespace Cnidaria.Rust;

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
    BomTrivia,
    ShebangTrivia,
    SingleLineCommentTrivia,
    MultiLineCommentTrivia,
    OuterLineDocCommentTrivia,
    InnerLineDocCommentTrivia,
    OuterBlockDocCommentTrivia,
    InnerBlockDocCommentTrivia,

    // Identifiers and lifetimes

    IdentifierToken,
    RawIdentifierToken,
    LifetimeToken,
    RawLifetimeToken,

    // Literals

    IntegerLiteralToken,
    FloatingLiteralToken,
    CharacterLiteralToken,
    ByteLiteralToken,
    StringLiteralToken,
    RawStringLiteralToken,
    ByteStringLiteralToken,
    RawByteStringLiteralToken,
    CStringLiteralToken,
    RawCStringLiteralToken,

    // Reserved lexical constructs

    ReservedPrefixToken,
    ReservedLifetimePrefixToken,
    ReservedPoundsToken,
    ReservedGuardedStringToken,

    // Delimiters

    OpenParenToken,                   // (
    CloseParenToken,                  // )

    OpenBracketToken,                 // [
    CloseBracketToken,                // ]

    OpenBraceToken,                   // {
    CloseBraceToken,                  // }

    // Punctuation and operators

    SemicolonToken,                   // ;
    CommaToken,                       // ,
    DotToken,                         // .
    DotDotToken,                      // ..
    DotDotEqualsToken,                // ..=
    DotDotDotToken,                   // ...

    ColonToken,                       // :
    ColonColonToken,                  // ::
    AtToken,                          // @
    HashToken,                        // #
    DollarToken,                      // $
    QuestionToken,                    // ?
    TildeToken,                       // ~

    PlusToken,                        // +
    PlusEqualsToken,                  // +=

    MinusToken,                       // -
    MinusEqualsToken,                 // -=
    MinusGreaterThanToken,            // ->
    LessThanMinusToken,               // <-

    StarToken,                        // *
    StarEqualsToken,                  // *=

    SlashToken,                       // /
    SlashEqualsToken,                 // /=

    PercentToken,                     // %
    PercentEqualsToken,               // %=

    CaretToken,                       // ^
    CaretEqualsToken,                 // ^=

    AmpersandToken,                   // &
    AmpersandEqualsToken,             // &=
    AmpersandAmpersandToken,          // &&

    PipeToken,                        // |
    PipeEqualsToken,                  // |=
    PipePipeToken,                    // ||

    BangToken,                        // !
    BangEqualsToken,                  // !=

    EqualsToken,                      // =
    EqualsEqualsToken,                // ==
    EqualsGreaterThanToken,           // =>

    LessThanToken,                    // <
    LessThanEqualsToken,              // <=
    LessThanLessThanToken,            // <<
    LessThanLessThanEqualsToken,      // <<=

    GreaterThanToken,                 // >
    GreaterThanEqualsToken,           // >=
    GreaterThanGreaterThanToken,      // >>
    GreaterThanGreaterThanEqualsToken, // >>=

    // Strict and reserved keywords

    UnderscoreKeyword,
    AsKeyword,
    AsyncKeyword,
    AwaitKeyword,
    BreakKeyword,
    ConstKeyword,
    ContinueKeyword,
    CrateKeyword,
    DynKeyword,
    ElseKeyword,
    EnumKeyword,
    ExternKeyword,
    FalseKeyword,
    FnKeyword,
    ForKeyword,
    IfKeyword,
    ImplKeyword,
    InKeyword,
    LetKeyword,
    LoopKeyword,
    MatchKeyword,
    ModKeyword,
    MoveKeyword,
    MutKeyword,
    PubKeyword,
    RefKeyword,
    ReturnKeyword,
    SelfValueKeyword,
    SelfTypeKeyword,
    StaticKeyword,
    StructKeyword,
    SuperKeyword,
    TraitKeyword,
    TrueKeyword,
    TypeKeyword,
    UnsafeKeyword,
    UseKeyword,
    WhereKeyword,
    WhileKeyword,
    AbstractKeyword,
    BecomeKeyword,
    BoxKeyword,
    DoKeyword,
    FinalKeyword,
    GenKeyword,
    MacroKeyword,
    OverrideKeyword,
    PrivKeyword,
    TryKeyword,
    TypeofKeyword,
    UnsizedKeyword,
    VirtualKeyword,
    YieldKeyword,

    // Contextual keywords

    UnionKeyword,
    MacroRulesKeyword,
    RawKeyword,
    SafeKeyword,

    // Syntax nodes

    CompilationUnit,
    SkippedSyntax,
    TokenTree,
    Attribute,
    Visibility,
    Path,
    QualifiedPath,
    PathSegment,
    GenericParameterList,
    GenericParameter,
    GenericArgumentList,
    GenericArgument,
    LifetimeBinder,
    WhereClause,
    WherePredicate,
    TypeBoundList,
    TypeBound,
    TypeAnnotation,
    ReturnType,
    EqualsValueClause,
    ParameterList,
    Parameter,
    ParameterTypeList,
    ItemList,
    FieldList,
    Field,
    EnumVariant,
    UseTree,
    ModuleItem,
    UseItem,
    ExternCrateItem,
    FunctionItem,
    StructItem,
    UnionItem,
    EnumItem,
    TraitItem,
    ImplItem,
    ExternBlockItem,
    TypeAliasItem,
    ConstItem,
    StaticItem,
    MacroDefinitionItem,
    MacroInvocationItem,
    MissingItem,
    PathType,
    ReferenceType,
    PointerType,
    TupleType,
    ParenthesizedType,
    SliceType,
    ArrayType,
    FunctionPointerType,
    TraitObjectType,
    ImplTraitType,
    NeverType,
    InferredType,
    MissingType,
    MacroType,
    EmptyStatement,
    ItemStatement,
    LetStatement,
    ExpressionStatement,
    BlockExpression,
    LiteralExpression,
    PathExpression,
    ParenthesizedExpression,
    TupleExpression,
    ArrayExpression,
    RepeatArrayExpression,
    UnaryExpression,
    BinaryExpression,
    AssignmentExpression,
    RangeExpression,
    CastExpression,
    ArgumentList,
    CallExpression,
    MemberAccessExpression,
    IndexExpression,
    TryExpression,
    AwaitExpression,
    StructExpression,
    StructExpressionField,
    IfExpression,
    LetExpression,
    LoopExpression,
    MatchExpression,
    MatchArm,
    ClosureExpression,
    ControlFlowExpression,
    MacroExpression,
    AttributedExpression,
    UnderscoreExpression,
    MissingExpression,
    WildcardPattern,
    RestPattern,
    MissingPattern,
    IdentifierPattern,
    LiteralPattern,
    PathPattern,
    ReferencePattern,
    BoxPattern,
    ParenthesizedPattern,
    TuplePattern,
    SlicePattern,
    TupleStructPattern,
    StructPattern,
    StructPatternField,
    OrPattern,
    RangePattern,
    MacroPattern,
    ConstBlockPattern,
}
