using System;
using System.Collections.Generic;

namespace Cnidaria.Rust;

/// <summary>
/// Classifies Rust token kinds and resolves edition-dependent keywords.
/// </summary>
public static class SyntaxFacts
{
    private static readonly Dictionary<string, SyntaxKind> Keywords = new(StringComparer.Ordinal)
    {
        ["_"] = SyntaxKind.UnderscoreKeyword,
        ["as"] = SyntaxKind.AsKeyword,
        ["async"] = SyntaxKind.AsyncKeyword,
        ["await"] = SyntaxKind.AwaitKeyword,
        ["break"] = SyntaxKind.BreakKeyword,
        ["const"] = SyntaxKind.ConstKeyword,
        ["continue"] = SyntaxKind.ContinueKeyword,
        ["crate"] = SyntaxKind.CrateKeyword,
        ["dyn"] = SyntaxKind.DynKeyword,
        ["else"] = SyntaxKind.ElseKeyword,
        ["enum"] = SyntaxKind.EnumKeyword,
        ["extern"] = SyntaxKind.ExternKeyword,
        ["false"] = SyntaxKind.FalseKeyword,
        ["fn"] = SyntaxKind.FnKeyword,
        ["for"] = SyntaxKind.ForKeyword,
        ["if"] = SyntaxKind.IfKeyword,
        ["impl"] = SyntaxKind.ImplKeyword,
        ["in"] = SyntaxKind.InKeyword,
        ["let"] = SyntaxKind.LetKeyword,
        ["loop"] = SyntaxKind.LoopKeyword,
        ["match"] = SyntaxKind.MatchKeyword,
        ["mod"] = SyntaxKind.ModKeyword,
        ["move"] = SyntaxKind.MoveKeyword,
        ["mut"] = SyntaxKind.MutKeyword,
        ["pub"] = SyntaxKind.PubKeyword,
        ["ref"] = SyntaxKind.RefKeyword,
        ["return"] = SyntaxKind.ReturnKeyword,
        ["self"] = SyntaxKind.SelfValueKeyword,
        ["Self"] = SyntaxKind.SelfTypeKeyword,
        ["static"] = SyntaxKind.StaticKeyword,
        ["struct"] = SyntaxKind.StructKeyword,
        ["super"] = SyntaxKind.SuperKeyword,
        ["trait"] = SyntaxKind.TraitKeyword,
        ["true"] = SyntaxKind.TrueKeyword,
        ["type"] = SyntaxKind.TypeKeyword,
        ["unsafe"] = SyntaxKind.UnsafeKeyword,
        ["use"] = SyntaxKind.UseKeyword,
        ["where"] = SyntaxKind.WhereKeyword,
        ["while"] = SyntaxKind.WhileKeyword,
        ["abstract"] = SyntaxKind.AbstractKeyword,
        ["become"] = SyntaxKind.BecomeKeyword,
        ["box"] = SyntaxKind.BoxKeyword,
        ["do"] = SyntaxKind.DoKeyword,
        ["final"] = SyntaxKind.FinalKeyword,
        ["gen"] = SyntaxKind.GenKeyword,
        ["macro"] = SyntaxKind.MacroKeyword,
        ["override"] = SyntaxKind.OverrideKeyword,
        ["priv"] = SyntaxKind.PrivKeyword,
        ["try"] = SyntaxKind.TryKeyword,
        ["typeof"] = SyntaxKind.TypeofKeyword,
        ["unsized"] = SyntaxKind.UnsizedKeyword,
        ["virtual"] = SyntaxKind.VirtualKeyword,
        ["yield"] = SyntaxKind.YieldKeyword,
    };

    private static readonly Dictionary<string, SyntaxKind> WeakKeywords = new(StringComparer.Ordinal)
    {
        ["union"] = SyntaxKind.UnionKeyword,
        ["macro_rules"] = SyntaxKind.MacroRulesKeyword,
        ["raw"] = SyntaxKind.RawKeyword,
        ["safe"] = SyntaxKind.SafeKeyword,
    };

    private static readonly Dictionary<string, SyntaxKind> Punctuation = new(StringComparer.Ordinal)
    {
        ["("] = SyntaxKind.OpenParenToken,
        [")"] = SyntaxKind.CloseParenToken,
        ["["] = SyntaxKind.OpenBracketToken,
        ["]"] = SyntaxKind.CloseBracketToken,
        ["{"] = SyntaxKind.OpenBraceToken,
        ["}"] = SyntaxKind.CloseBraceToken,
        [";"] = SyntaxKind.SemicolonToken,
        [","] = SyntaxKind.CommaToken,
        ["."] = SyntaxKind.DotToken,
        [".."] = SyntaxKind.DotDotToken,
        ["..="] = SyntaxKind.DotDotEqualsToken,
        ["..."] = SyntaxKind.DotDotDotToken,
        [":"] = SyntaxKind.ColonToken,
        ["::"] = SyntaxKind.ColonColonToken,
        ["@"] = SyntaxKind.AtToken,
        ["#"] = SyntaxKind.HashToken,
        ["$"] = SyntaxKind.DollarToken,
        ["?"] = SyntaxKind.QuestionToken,
        ["~"] = SyntaxKind.TildeToken,
        ["+"] = SyntaxKind.PlusToken,
        ["+="] = SyntaxKind.PlusEqualsToken,
        ["-"] = SyntaxKind.MinusToken,
        ["-="] = SyntaxKind.MinusEqualsToken,
        ["->"] = SyntaxKind.MinusGreaterThanToken,
        ["<-"] = SyntaxKind.LessThanMinusToken,
        ["*"] = SyntaxKind.StarToken,
        ["*="] = SyntaxKind.StarEqualsToken,
        ["/"] = SyntaxKind.SlashToken,
        ["/="] = SyntaxKind.SlashEqualsToken,
        ["%"] = SyntaxKind.PercentToken,
        ["%="] = SyntaxKind.PercentEqualsToken,
        ["^"] = SyntaxKind.CaretToken,
        ["^="] = SyntaxKind.CaretEqualsToken,
        ["&"] = SyntaxKind.AmpersandToken,
        ["&="] = SyntaxKind.AmpersandEqualsToken,
        ["&&"] = SyntaxKind.AmpersandAmpersandToken,
        ["|"] = SyntaxKind.PipeToken,
        ["|="] = SyntaxKind.PipeEqualsToken,
        ["||"] = SyntaxKind.PipePipeToken,
        ["!"] = SyntaxKind.BangToken,
        ["!="] = SyntaxKind.BangEqualsToken,
        ["="] = SyntaxKind.EqualsToken,
        ["=="] = SyntaxKind.EqualsEqualsToken,
        ["=>"] = SyntaxKind.EqualsGreaterThanToken,
        ["<"] = SyntaxKind.LessThanToken,
        ["<="] = SyntaxKind.LessThanEqualsToken,
        ["<<"] = SyntaxKind.LessThanLessThanToken,
        ["<<="] = SyntaxKind.LessThanLessThanEqualsToken,
        [">"] = SyntaxKind.GreaterThanToken,
        [">="] = SyntaxKind.GreaterThanEqualsToken,
        [">>"] = SyntaxKind.GreaterThanGreaterThanToken,
        [">>="] = SyntaxKind.GreaterThanGreaterThanEqualsToken,
    };

    public static SyntaxKind GetKeywordKind(string text, RustEdition edition = RustEdition.Rust2024)
    {
        if (!Keywords.TryGetValue(text, out var kind))
            return SyntaxKind.IdentifierToken;

        if (edition == RustEdition.Rust2015 && kind is
            SyntaxKind.AsyncKeyword or SyntaxKind.AwaitKeyword or SyntaxKind.DynKeyword or SyntaxKind.TryKeyword)
        {
            return SyntaxKind.IdentifierToken;
        }

        if (edition < RustEdition.Rust2024 && kind == SyntaxKind.GenKeyword)
            return SyntaxKind.IdentifierToken;

        return kind;
    }

    public static SyntaxKind GetContextualKeywordKind(string text)
        => WeakKeywords.TryGetValue(text, out var kind) ? kind : SyntaxKind.None;

    public static bool IsKeyword(SyntaxKind kind)
        => kind >= SyntaxKind.UnderscoreKeyword && kind <= SyntaxKind.YieldKeyword;

    public static bool IsTrivia(SyntaxKind kind)
        => kind >= SyntaxKind.WhitespaceTrivia && kind <= SyntaxKind.InnerBlockDocCommentTrivia;

    public static bool IsLiteral(SyntaxKind kind)
        => kind >= SyntaxKind.IntegerLiteralToken && kind <= SyntaxKind.RawCStringLiteralToken;

    public static bool IsDocComment(SyntaxKind kind)
        => kind is SyntaxKind.OuterLineDocCommentTrivia or SyntaxKind.InnerLineDocCommentTrivia
            or SyntaxKind.OuterBlockDocCommentTrivia or SyntaxKind.InnerBlockDocCommentTrivia;

    public static bool IsContextualKeyword(SyntaxKind kind)
        => kind is SyntaxKind.UnionKeyword or SyntaxKind.MacroRulesKeyword
            or SyntaxKind.RawKeyword or SyntaxKind.SafeKeyword;

    /// <summary>Returns the fixed spelling of keywords and punctuation, if one exists.</summary>
    public static string? GetText(SyntaxKind kind)
    {
        foreach (var pair in Punctuation)
        {
            if (pair.Value == kind)
                return pair.Key;
        }

        foreach (var pair in Keywords)
        {
            if (pair.Value == kind)
                return pair.Key;
        }

        foreach (var pair in WeakKeywords)
        {
            if (pair.Value == kind)
                return pair.Key;
        }

        return null;
    }

    public static bool IsPunctuation(SyntaxKind kind)
        => kind >= SyntaxKind.OpenParenToken && kind <= SyntaxKind.GreaterThanGreaterThanEqualsToken;

    public static bool TryGetPunctuation(ReadOnlySpan<char> text, out SyntaxKind kind)
        => Punctuation.TryGetValue(text.ToString(), out kind);

    /// <summary>Raw identifiers and raw lifetimes cannot spell these special names.</summary>
    public static bool IsForbiddenRawIdentifier(string normalized)
        => normalized is "_" or "crate" or "self" or "Self" or "super";
}
