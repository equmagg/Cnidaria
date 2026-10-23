using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cnidaria.Rust;

internal enum FragmentKind : byte { Expression, Type, Pattern, PatternParameter, Identifier, Lifetime, Literal, TokenTree, Block, Statement, Item, Path, Visibility, Meta }
internal enum BuiltinMacro : byte { None, Assert, Cfg, Column, CompileError, Concat, CorePanic, File, FormatArgs, Line, ModulePath, Stringify, Unreachable }

internal abstract record MacroElement;
internal sealed record MacroToken(SyntaxToken Token) : MacroElement;
internal sealed record MacroGroup(SyntaxToken Open, ImmutableArray<MacroElement> Elements, SyntaxToken Close) : MacroElement;
internal sealed record MacroVariable(SyntaxToken Dollar, SyntaxToken Name, FragmentKind? Fragment) : MacroElement;
internal sealed record MacroRepetition(ImmutableArray<MacroElement> Elements, SyntaxToken? Separator, SyntaxKind Operator) : MacroElement;
internal sealed record MacroRule(ImmutableArray<MacroElement> Matcher, ImmutableArray<MacroElement> Transcriber);
internal sealed record MacroExpansion(MacroDefinition Definition, ImmutableArray<SyntaxToken> Tokens);

internal sealed class MacroDefinition(string name, ImmutableArray<MacroRule> rules, ModuleSymbol crate, BuiltinMacro builtin = BuiltinMacro.None)
{
    internal string Name { get; } = name;
    internal ImmutableArray<MacroRule> Rules { get; } = rules;
    internal ModuleSymbol Crate { get; } = crate;
    internal BuiltinMacro Builtin { get; } = builtin;
}

///<summary>Textually scoped macro_rules definitions visible at one point of the crate</summary>
internal sealed class MacroScope(MacroDefinition definition, MacroScope? parent)
{
    internal MacroDefinition Definition { get; } = definition;
    internal MacroScope? Parent { get; } = parent;
    internal static MacroDefinition? Find(MacroScope? scope, string name)
    {
        for (; scope is not null; scope = scope.Parent)
            if (scope.Definition.Name == name) return scope.Definition;
        return null;
    }
}

internal abstract class MacroBinding;
internal sealed class FragmentBinding(FragmentKind kind, ImmutableArray<SyntaxToken> tokens) : MacroBinding
{
    internal FragmentKind Kind { get; } = kind;
    internal ImmutableArray<SyntaxToken> Tokens { get; } = tokens;
}
internal sealed class SequenceBinding(ImmutableArray<MacroBinding> items) : MacroBinding
{
    internal ImmutableArray<MacroBinding> Items { get; } = items;
}

internal sealed class MacroException(string message) : Exception(message);

///<summary>Token trees of one macro invocation with the matching close index of every open delimiter</summary>
internal sealed class MacroInput
{
    internal ImmutableArray<SyntaxToken> Tokens { get; }
    internal int[] Close { get; }
    internal MacroInput(ImmutableArray<SyntaxToken> tokens)
    {
        Tokens = tokens;
        Close = new int[tokens.Length];
        var open = new Stack<int>();
        for (int i = 0; i < tokens.Length; i++)
        {
            if (IsOpen(tokens[i].Kind)) open.Push(i);
            else if (IsClose(tokens[i].Kind) && open.Count != 0) Close[open.Pop()] = i;
        }
        while (open.Count != 0) Close[open.Pop()] = tokens.Length;
    }
    internal static bool IsOpen(SyntaxKind kind) => kind is SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.OpenBraceToken;
    internal static bool IsClose(SyntaxKind kind) => kind is SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CloseBraceToken;
    internal int TreeEnd(int index) => IsOpen(Tokens[index].Kind) ? Math.Min(Close[index] + 1, Tokens.Length) : index + 1;
    internal ImmutableArray<SyntaxToken> Slice(int start, int end) => ImmutableArray.Create(Tokens, start, end - start);
}

internal sealed record BuiltinContext(SyntaxTree Tree, TextSpan Span, int Mark, string ModulePath, int PointerWidth);

internal sealed class MacroExpander(RustEdition edition)
{
    private readonly RustEdition _edition = edition;

    // Expanders that #[rustc_builtin_macro] or #[rustc_builtin_macro(name)] attaches to a declaration
    internal static readonly ImmutableDictionary<string, BuiltinMacro> Builtins = new Dictionary<string, BuiltinMacro>
    {
        ["assert"] = BuiltinMacro.Assert, ["cfg"] = BuiltinMacro.Cfg, ["column"] = BuiltinMacro.Column, ["compile_error"] = BuiltinMacro.CompileError,
        ["concat"] = BuiltinMacro.Concat, ["const_format_args"] = BuiltinMacro.FormatArgs, ["core_panic"] = BuiltinMacro.CorePanic, ["file"] = BuiltinMacro.File,
        ["format_args"] = BuiltinMacro.FormatArgs, ["line"] = BuiltinMacro.Line, ["module_path"] = BuiltinMacro.ModulePath, ["stringify"] = BuiltinMacro.Stringify,
        ["unreachable"] = BuiltinMacro.Unreachable
    }.ToImmutableDictionary(StringComparer.Ordinal);

    internal static SyntaxToken DollarCrate(TextSpan span, int mark) => new(SyntaxKind.CrateKeyword, span, "$crate", null, 0, SyntaxKind.None, [], [], false, mark, "$crate");

    internal static SyntaxToken Own(SyntaxToken token, string text) => token.Text is not null ? token : token.Relocate(token.Span, token.GetText(text), token.Context);
    internal static IEnumerable<SyntaxToken> Tokens(TokenTreeSyntax tree, string text, bool delimiters)
    {
        for (int i = 0; i < tree.Children.Length; i++)
        {
            SyntaxNodeOrToken child = tree.Children[i];
            if (!delimiters && (i == 0 || i == tree.Children.Length - 1) && child.IsToken) continue;
            if (child.IsToken) { if (!child.Token.IsMissing) yield return Own(child.Token, text); }
            else foreach (SyntaxToken token in Tokens((TokenTreeSyntax)child.Node, text, true)) yield return token;
        }
    }

    internal static ImmutableArray<MacroRule> ParseRules(TokenTreeSyntax body, string text)
    {
        var rules = ImmutableArray.CreateBuilder<MacroRule>();
        var items = body.Children.Skip(1).SkipLast(1).ToArray();
        for (int i = 0; i < items.Length;)
        {
            if (!items[i].IsNode) throw new MacroException("Expected a delimited macro matcher.");
            var matcher = (TokenTreeSyntax)items[i].Node;
            if (i + 2 >= items.Length || !items[i + 1].IsToken || items[i + 1].Token.Kind != SyntaxKind.EqualsGreaterThanToken || !items[i + 2].IsNode)
                throw new MacroException("Expected '=>' followed by a delimited macro transcriber.");
            var transcriber = (TokenTreeSyntax)items[i + 2].Node;
            rules.Add(new MacroRule(Compile(matcher, text, true), Compile(transcriber, text, false)));
            i += 3;
            if (i < items.Length)
            {
                if (!items[i].IsToken || items[i].Token.Kind != SyntaxKind.SemicolonToken) throw new MacroException("Expected ';' between macro rules.");
                i++;
            }
        }
        if (rules.Count == 0) throw new MacroException("A macro_rules definition requires at least one rule.");
        return rules.ToImmutable();
    }
    private static bool IsName(SyntaxToken token) => token.Kind is SyntaxKind.IdentifierToken or SyntaxKind.RawIdentifierToken || SyntaxFacts.IsKeyword(token.Kind);
    private static ImmutableArray<MacroElement> Compile(TokenTreeSyntax tree, string text, bool matcher)
    {
        var items = tree.Children.Skip(1).SkipLast(1).Where(c => c.IsNode || !c.Token.IsMissing)
            .Select(c => c.IsNode ? (object)(TokenTreeSyntax)c.Node : Own(c.Token, text)).ToList();
        var result = ImmutableArray.CreateBuilder<MacroElement>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is TokenTreeSyntax group) { result.Add(new MacroGroup(Own(group.OpenToken, text), Compile(group, text, matcher), Own(group.CloseToken, text))); continue; }
            var token = (SyntaxToken)items[i];
            if (token.Kind != SyntaxKind.DollarToken || i + 1 == items.Count) { result.Add(new MacroToken(token)); continue; }
            if (items[i + 1] is TokenTreeSyntax { OpenToken.Kind: SyntaxKind.OpenParenToken } repeated)
            {
                ImmutableArray<MacroElement> inner = Compile(repeated, text, matcher);
                i += 2;
                SyntaxToken? separator = null;
                if (i < items.Count && items[i] is SyntaxToken op && op.Kind is SyntaxKind.StarToken or SyntaxKind.PlusToken or SyntaxKind.QuestionToken) { }
                else if (i + 1 < items.Count && items[i] is SyntaxToken sep && items[i + 1] is SyntaxToken next && next.Kind is SyntaxKind.StarToken or SyntaxKind.PlusToken or SyntaxKind.QuestionToken)
                { separator = sep; i++; }
                else throw new MacroException("Expected a repetition operator '*', '+' or '?'.");
                SyntaxKind kind = ((SyntaxToken)items[i]).Kind;
                if (kind == SyntaxKind.QuestionToken && separator is not null) throw new MacroException("The '?' repetition operator does not take a separator.");
                result.Add(new MacroRepetition(inner, separator, kind));
                continue;
            }
            if (items[i + 1] is SyntaxToken name && IsName(name))
            {
                if (!matcher || name.Kind == SyntaxKind.CrateKeyword)
                {
                    result.Add(name.Kind == SyntaxKind.CrateKeyword ? new MacroToken(DollarCrate(name.Span, name.Context)) : new MacroVariable(token, name, null));
                    i++;
                    continue;
                }
                if (i + 3 < items.Count && items[i + 2] is SyntaxToken { Kind: SyntaxKind.ColonToken } && items[i + 3] is SyntaxToken specifier && IsName(specifier))
                {
                    result.Add(new MacroVariable(token, name, Fragment(Binder.Name(specifier))));
                    i += 3;
                    continue;
                }
                throw new MacroException($"Macro variable '${Binder.Name(name)}' is missing a fragment specifier.");
            }
            result.Add(new MacroToken(token));
        }
        return result.ToImmutable();
    }
    private static FragmentKind Fragment(string name) => name switch
    {
        "expr" or "expr_2021" => FragmentKind.Expression, "ty" => FragmentKind.Type, "pat" => FragmentKind.Pattern, "pat_param" => FragmentKind.PatternParameter,
        "ident" => FragmentKind.Identifier, "lifetime" => FragmentKind.Lifetime, "literal" => FragmentKind.Literal, "tt" => FragmentKind.TokenTree,
        "block" => FragmentKind.Block, "stmt" => FragmentKind.Statement, "item" => FragmentKind.Item, "path" => FragmentKind.Path, "vis" => FragmentKind.Visibility,
        "meta" => FragmentKind.Meta,
        _ => throw new MacroException($"Invalid fragment specifier '{name}'.")
    };

    internal static string Key(SyntaxToken token) => token.Context == 0 ? Binder.Name(token) : Binder.Name(token) + "\u0001" + token.Context.ToString(CultureInfo.InvariantCulture);
    private static bool Same(SyntaxToken input, SyntaxToken expected)
    {
        if (input.Kind != expected.Kind) return false;
        if (IsName(input) || input.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken) return Binder.Name(input) == Binder.Name(expected);
        return !SyntaxFacts.IsLiteral(input.Kind) || input.Text == expected.Text;
    }

    internal (MacroRule Rule, ImmutableDictionary<string, MacroBinding> Bindings)? Match(MacroDefinition definition, MacroInput input)
    {
        foreach (MacroRule rule in definition.Rules)
            foreach (var match in Match(input, rule.Matcher, 0, 0, input.Tokens.Length, ImmutableDictionary<string, MacroBinding>.Empty))
                if (match.End == input.Tokens.Length) return (rule, match.Bindings);
        return null;
    }
    private IEnumerable<(int End, ImmutableDictionary<string, MacroBinding> Bindings)> Match(MacroInput input, ImmutableArray<MacroElement> elements, int index, int position, int end,
        ImmutableDictionary<string, MacroBinding> bindings)
    {
        if (index == elements.Length) { yield return (position, bindings); yield break; }
        switch (elements[index])
        {
            case MacroToken expected:
                if (position < end && Same(input.Tokens[position], expected.Token))
                    foreach (var match in Match(input, elements, index + 1, position + 1, end, bindings)) yield return match;
                break;
            case MacroGroup group:
                if (position < end && input.Tokens[position].Kind == group.Open.Kind && input.Close[position] < end)
                {
                    int close = input.Close[position];
                    foreach (var inner in Match(input, group.Elements, 0, position + 1, close, bindings))
                        if (inner.End == close)
                            foreach (var match in Match(input, elements, index + 1, close + 1, end, inner.Bindings)) yield return match;
                }
                break;
            case MacroVariable variable:
            {
                FragmentKind kind = variable.Fragment!.Value;
                int fragmentEnd = ParseFragment(input, kind, position, end);
                if (fragmentEnd < 0) break;
                var binding = new FragmentBinding(kind, input.Slice(position, fragmentEnd));
                foreach (var match in Match(input, elements, index + 1, fragmentEnd, end, bindings.SetItem(Key(variable.Name), binding))) yield return match;
                break;
            }
            case MacroRepetition repetition:
            {
                // Iterations match greedily in a loop; only their count is backtracked, so recursion depth follows the matcher, not the input
                var iterations = new List<ImmutableDictionary<string, MacroBinding>>();
                var positions = new List<int> { position };
                while (repetition.Operator != SyntaxKind.QuestionToken || iterations.Count == 0)
                {
                    int start = positions[^1];
                    if (iterations.Count != 0 && repetition.Separator is SyntaxToken separator)
                    {
                        if (start >= end || !Same(input.Tokens[start], separator)) break;
                        start++;
                    }
                    var iteration = Match(input, repetition.Elements, 0, start, end, ImmutableDictionary<string, MacroBinding>.Empty).FirstOrDefault(m => m.End > positions[^1]);
                    if (iteration.Bindings is null) break;
                    iterations.Add(iteration.Bindings);
                    positions.Add(iteration.End);
                }
                ImmutableArray<string> names = Variables(repetition.Elements).ToImmutableArray();
                for (int count = iterations.Count; count >= (repetition.Operator == SyntaxKind.PlusToken ? 1 : 0); count--)
                {
                    ImmutableDictionary<string, MacroBinding> repeated = bindings;
                    foreach (string name in names)
                        repeated = repeated.SetItem(name, new SequenceBinding(iterations.Take(count).Select(i => i.GetValueOrDefault(name) ?? new SequenceBinding([])).ToImmutableArray()));
                    foreach (var match in Match(input, elements, index + 1, positions[count], end, repeated)) yield return match;
                }
                break;
            }
        }
    }
    private static IEnumerable<string> Variables(ImmutableArray<MacroElement> elements) => elements.SelectMany(e => e switch
    {
        MacroVariable variable => [Key(variable.Name)],
        MacroGroup group => Variables(group.Elements),
        MacroRepetition repetition => Variables(repetition.Elements),
        _ => Enumerable.Empty<string>()
    }).Distinct();

    internal int ParseFragment(MacroInput input, FragmentKind kind, int position, int end)
    {
        if (position >= end) return kind == FragmentKind.Visibility ? position : -1;
        SyntaxToken first = input.Tokens[position];
        switch (kind)
        {
            case FragmentKind.Identifier: return IsName(first) && first.Kind != SyntaxKind.UnderscoreKeyword ? position + 1 : -1;
            case FragmentKind.Lifetime: return first.Kind is SyntaxKind.LifetimeToken or SyntaxKind.RawLifetimeToken ? position + 1 : -1;
            case FragmentKind.TokenTree: return MacroInput.IsClose(first.Kind) ? -1 : input.TreeEnd(position);
            case FragmentKind.Literal:
            {
                int literal = first.Kind == SyntaxKind.MinusToken ? position + 1 : position;
                if (literal >= end) return -1;
                SyntaxKind token = input.Tokens[literal].Kind;
                return SyntaxFacts.IsLiteral(token) || first.Kind != SyntaxKind.MinusToken && token is SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword ? literal + 1 : -1;
            }
        }
        var parser = new Parser(input.Tokens, position, end, _edition);
        _ = kind switch
        {
            FragmentKind.Expression => (SyntaxNode?)parser.ParseExpression(),
            FragmentKind.Type => parser.ParseType(),
            FragmentKind.Pattern => parser.ParsePatternFragment(true),
            FragmentKind.PatternParameter => parser.ParsePatternFragment(false),
            FragmentKind.Block => parser.ParseBlockExpression(),
            FragmentKind.Statement => parser.ParseStatementFragment(),
            FragmentKind.Item => parser.ParseItemFragment(),
            FragmentKind.Path or FragmentKind.Meta => parser.ParsePathFragment(),
            _ => parser.ParseVisibilityFragment()
        };
        int consumed = parser.Consumed;
        if (parser.Diagnostics.Count != 0 || consumed < 0 || consumed == 0 && kind != FragmentKind.Visibility) return -1;
        int result = position + consumed;
        if (kind == FragmentKind.Meta && result < end && MacroInput.IsOpen(input.Tokens[result].Kind)) result = input.TreeEnd(result);
        return result;
    }

    internal static ImmutableArray<SyntaxToken> Transcribe(MacroRule rule, ImmutableDictionary<string, MacroBinding> bindings, TextSpan span, int mark)
    {
        var output = ImmutableArray.CreateBuilder<SyntaxToken>();
        Transcribe(rule.Transcriber, bindings, new List<int>(), output, span, mark);
        return output.ToImmutable();
    }
    private static SyntaxToken Marked(SyntaxToken token, TextSpan span, int mark) => token.Relocate(span, token.Text ?? "", mark);
    private static void Transcribe(ImmutableArray<MacroElement> elements, ImmutableDictionary<string, MacroBinding> bindings, List<int> indices,
        ImmutableArray<SyntaxToken>.Builder output, TextSpan span, int mark)
    {
        foreach (MacroElement element in elements)
        {
            switch (element)
            {
                case MacroToken token: output.Add(Marked(token.Token, span, mark)); break;
                case MacroGroup group:
                    output.Add(Marked(group.Open, span, mark));
                    Transcribe(group.Elements, bindings, indices, output, span, mark);
                    output.Add(Marked(group.Close, span, mark));
                    break;
                case MacroVariable variable:
                {
                    if (!bindings.TryGetValue(Key(variable.Name), out MacroBinding? binding))
                    {
                        output.Add(Marked(variable.Dollar, span, mark));
                        output.Add(Marked(variable.Name, span, mark));
                        break;
                    }
                    if (Select(binding, indices) is not FragmentBinding fragment)
                        throw new MacroException($"Variable '${Binder.Name(variable.Name)}' is still repeating at this depth.");
                    // Expression and type fragments stay one operand when substituted next to operators
                    bool group = fragment.Kind is FragmentKind.Expression or FragmentKind.Type && fragment.Tokens.Length > 1;
                    TextSpan fragmentSpan = fragment.Tokens.IsEmpty ? span : fragment.Tokens[0].Span;
                    if (group) output.Add(Punctuation(SyntaxKind.OpenParenToken, "(", fragmentSpan, mark));
                    output.AddRange(fragment.Tokens);
                    if (group) output.Add(Punctuation(SyntaxKind.CloseParenToken, ")", fragmentSpan, mark));
                    break;
                }
                case MacroRepetition repetition:
                {
                    int count = Count(repetition, bindings, indices);
                    for (int i = 0; i < count; i++)
                    {
                        if (i != 0 && repetition.Separator is SyntaxToken separator) output.Add(Marked(separator, span, mark));
                        indices.Add(i);
                        Transcribe(repetition.Elements, bindings, indices, output, span, mark);
                        indices.RemoveAt(indices.Count - 1);
                    }
                    break;
                }
            }
        }
    }
    private static MacroBinding? Select(MacroBinding binding, List<int> indices, int depth = -1)
    {
        if (depth < 0) depth = indices.Count;
        for (int i = 0; i < depth && binding is SequenceBinding sequence; i++)
        {
            if (indices[i] >= sequence.Items.Length) return null;
            binding = sequence.Items[indices[i]];
        }
        return binding;
    }
    private static int Count(MacroRepetition repetition, ImmutableDictionary<string, MacroBinding> bindings, List<int> indices)
    {
        int count = -1;
        foreach (string name in Variables(repetition.Elements))
        {
            if (!bindings.TryGetValue(name, out MacroBinding? binding) || Select(binding, indices) is not SequenceBinding sequence) continue;
            if (count >= 0 && count != sequence.Items.Length)
                throw new MacroException($"Meta-variables repeat {count} and {sequence.Items.Length} times in the same repetition.");
            count = sequence.Items.Length;
        }
        if (count < 0) throw new MacroException("A repetition in the macro transcriber contains no variables repeating at this depth.");
        return count;
    }

    internal static SyntaxToken Punctuation(SyntaxKind kind, string text, TextSpan span, int mark)
        => new(kind, span, null, null, 0, SyntaxKind.None, [], [], false, mark, text);
    internal ImmutableArray<SyntaxToken> Lex(string text, TextSpan span, int mark)
    {
        var lexer = new Lexer(text, new LexerOptions { Edition = _edition });
        var tokens = ImmutableArray.CreateBuilder<SyntaxToken>();
        for (SyntaxToken token = lexer.NextToken(); token.Kind != SyntaxKind.EndOfFileToken; token = lexer.NextToken())
            tokens.Add(token.Relocate(span, token.GetText(text), mark));
        return tokens.ToImmutable();
    }
    internal static SyntaxToken StringLiteral(string value, TextSpan span, int mark)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value) text.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\0' => "\\0", _ => c.ToString() });
        return new SyntaxToken(SyntaxKind.StringLiteralToken, span, null, value, 0, SyntaxKind.None, [], [], false, mark, text.Append('"').ToString());
    }

    // Splits builtin macro arguments at top-level commas, keeping commas nested in expressions intact
    internal List<(SyntaxToken? Name, ImmutableArray<SyntaxToken> Value)> Arguments(MacroInput input, int start = 0)
    {
        var arguments = new List<(SyntaxToken?, ImmutableArray<SyntaxToken>)>();
        for (int position = start; position < input.Tokens.Length;)
        {
            SyntaxToken? name = null;
            if (position + 1 < input.Tokens.Length && IsName(input.Tokens[position]) && input.Tokens[position + 1].Kind == SyntaxKind.EqualsToken)
            {
                name = input.Tokens[position];
                position += 2;
            }
            int end = ParseFragment(input, FragmentKind.Expression, position, input.Tokens.Length);
            if (end < 0) throw new MacroException("Expected an expression in the macro arguments.");
            arguments.Add((name, input.Slice(position, end)));
            position = end;
            if (position == input.Tokens.Length) break;
            if (input.Tokens[position].Kind != SyntaxKind.CommaToken) throw new MacroException("Expected ',' between macro arguments.");
            position++;
        }
        return arguments;
    }

    internal ImmutableArray<SyntaxToken> ExpandBuiltin(BuiltinMacro builtin, MacroInput input, BuiltinContext context)
    {
        var output = ImmutableArray.CreateBuilder<SyntaxToken>();
        void Text(string text) => output.AddRange(Lex(text, context.Span, context.Mark));
        void Grouped(ImmutableArray<SyntaxToken> tokens) { Text("("); output.AddRange(tokens); Text(")"); }
        void Crate() => output.Add(DollarCrate(context.Span, context.Mark));
        switch (builtin)
        {
            case BuiltinMacro.Assert:
            {
                int end = ParseFragment(input, FragmentKind.Expression, 0, input.Tokens.Length);
                if (end < 0) throw new MacroException("assert! requires a boolean expression.");
                if (end < input.Tokens.Length && input.Tokens[end].Kind != SyntaxKind.CommaToken) throw new MacroException("Expected ',' after the asserted condition.");
                ImmutableArray<SyntaxToken> condition = input.Slice(0, end), message = end + 1 < input.Tokens.Length ? input.Slice(end + 1, input.Tokens.Length) : [];
                Text("if !"); Grouped(condition); Text("{"); Crate();
                if (message.IsEmpty)
                {
                    Text("::panicking::panic(");
                    output.Add(StringLiteral("assertion failed: " + Stringify(condition, context.Tree), context.Span, context.Mark));
                    Text(")");
                }
                else { Text("::panic::panic_2021!"); Grouped(message); }
                Text("}");
                break;
            }
            // Only Rust 2024 is accepted, so the edition dispatch always selects the 2021 forms
            case BuiltinMacro.CorePanic or BuiltinMacro.Unreachable:
                Crate(); Text(builtin == BuiltinMacro.CorePanic ? "::panic::panic_2021!" : "::panic::unreachable_2021!"); Grouped(input.Tokens);
                break;
            case BuiltinMacro.FormatArgs: FormatArguments(input, output, context); break;
            case BuiltinMacro.Stringify: output.Add(StringLiteral(Stringify(input.Tokens, context.Tree), context.Span, context.Mark)); break;
            case BuiltinMacro.Concat:
            {
                var text = new StringBuilder();
                foreach (var (name, value) in Arguments(input)) text.Append(name is null ? LiteralText(value) : throw new MacroException("concat! does not take named arguments."));
                output.Add(StringLiteral(text.ToString(), context.Span, context.Mark));
                break;
            }
            case BuiltinMacro.Line or BuiltinMacro.Column:
            {
                Expect(input, 0);
                string source = context.Tree.Text;
                int start = Math.Min(context.Span.Start, source.Length), lineStart = source.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
                if (start == 0) lineStart = 0;
                ulong value = builtin == BuiltinMacro.Line ? (ulong)source.AsSpan(0, start).Count('\n') + 1 : (ulong)(start - lineStart) + 1;
                string literal = value.ToString(CultureInfo.InvariantCulture) + "u32";
                output.Add(new SyntaxToken(SyntaxKind.IntegerLiteralToken, context.Span, null, value, 3, SyntaxKind.None, [], [], false, context.Mark, literal));
                break;
            }
            case BuiltinMacro.File: Expect(input, 0); output.Add(StringLiteral(context.Tree.FilePath, context.Span, context.Mark)); break;
            case BuiltinMacro.ModulePath: Expect(input, 0); output.Add(StringLiteral(context.ModulePath, context.Span, context.Mark)); break;
            case BuiltinMacro.Cfg:
            {
                int position = 0;
                bool value = Configuration(input, ref position, context);
                if (position < input.Tokens.Length && input.Tokens[position].Kind == SyntaxKind.CommaToken) position++;
                if (position != input.Tokens.Length) throw new MacroException("Expected a single configuration predicate.");
                Text(value ? "true" : "false");
                break;
            }
            case BuiltinMacro.CompileError:
            {
                var arguments = Arguments(input);
                if (arguments.Count != 1 || arguments[0].Value is not [SyntaxToken { Value: string message }]) throw new MacroException("compile_error! takes a string literal.");
                throw new MacroException(message);
            }
        }
        return output.ToImmutable();
    }

    private static void Expect(MacroInput input, int count)
    {
        if (input.Tokens.Length != count) throw new MacroException("This macro takes no arguments.");
    }
    // Arguments are evaluated and borrowed like rustc's expansion; they are not formatted until core::fmt has its traits
    private void FormatArguments(MacroInput input, ImmutableArray<SyntaxToken>.Builder output, BuiltinContext context)
    {
        void Text(string text) => output.AddRange(Lex(text, context.Span, context.Mark));
        var arguments = Arguments(input);
        if (arguments.Count == 0 || arguments[0] is not (null, [SyntaxToken { Kind: SyntaxKind.StringLiteralToken or SyntaxKind.RawStringLiteralToken, Value: string format } literal]))
            throw new MacroException("The format string must be a string literal.");
        var positional = arguments.Skip(1).TakeWhile(a => a.Name is null).Select(a => a.Value).ToList();
        var named = arguments.Skip(1 + positional.Count).ToList();
        if (named.Any(a => a.Name is null)) throw new MacroException("Positional arguments cannot follow named arguments.");
        var used = new bool[positional.Count];
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var captured = new List<ImmutableArray<SyntaxToken>>();
        var pieces = new List<string>();
        foreach (FormatArgument reference in FormatString.Parse(format, pieces))
        {
            if (reference.Name is null)
            {
                if (reference.Position >= positional.Count)
                    throw new MacroException($"Invalid reference to positional argument {reference.Position}; there are {positional.Count} arguments.");
                used[reference.Position] = true;
            }
            else if (named.Any(a => Binder.Name(a.Name!.Value) == reference.Name)) usedNames.Add(reference.Name);
            else if (usedNames.Add("\0" + reference.Name)) captured.Add(Lex(reference.Name, literal.Span, literal.Context));
        }
        if (Array.IndexOf(used, false) is int unused and >= 0) throw new MacroException($"Format argument {unused} is never used.");
        if (named.FirstOrDefault(a => !usedNames.Contains(Binder.Name(a.Name!.Value))) is { Name: SyntaxToken extra }) throw new MacroException($"Named argument '{Binder.Name(extra)}' is never used.");
        var values = positional.Concat(named.Select(a => a.Value)).Concat(captured).ToList();
        if (values.Count != 0)
        {
            Text("match (");
            foreach (ImmutableArray<SyntaxToken> value in values) { Text("&("); output.AddRange(value); Text("),"); }
            Text(") { args => ");
        }
        output.Add(DollarCrate(context.Span, context.Mark));
        Text("::fmt::Arguments::new_const(&[");
        foreach (string piece in pieces) { output.Add(StringLiteral(piece, context.Span, context.Mark)); Text(","); }
        Text("])");
        if (values.Count != 0) Text("}");
    }
    private static string Stringify(ImmutableArray<SyntaxToken> tokens, SyntaxTree tree)
    {
        if (tokens.IsEmpty) return "";
        bool source = tokens.All(t => t.Context == 0) && tokens.Zip(tokens.Skip(1)).All(p => p.First.Span.End <= p.Second.Span.Start) && tokens[^1].Span.End <= tree.Text.Length;
        string text = source ? tree.Text[tokens[0].Span.Start..tokens[^1].Span.End] : string.Join(" ", tokens.Select(t => t.Text));
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
    private static string LiteralText(ImmutableArray<SyntaxToken> value)
    {
        bool negative = value.Length == 2 && value[0].Kind == SyntaxKind.MinusToken;
        if (value.Length != (negative ? 2 : 1)) throw new MacroException("concat! arguments must be literals.");
        SyntaxToken token = value[^1];
        string text = token.Kind switch
        {
            SyntaxKind.StringLiteralToken or SyntaxKind.RawStringLiteralToken or SyntaxKind.CharacterLiteralToken => token.Value?.ToString() ?? "",
            SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword when !negative => Binder.Name(token),
            SyntaxKind.IntegerLiteralToken or SyntaxKind.FloatingLiteralToken => token.Text![..^token.SuffixLength].Replace("_", ""),
            _ => throw new MacroException("concat! arguments must be literals.")
        };
        return negative ? "-" + text : text;
    }
    private static bool Configuration(MacroInput input, ref int position, BuiltinContext context)
    {
        if (position >= input.Tokens.Length || !IsName(input.Tokens[position])) throw new MacroException("Expected a configuration predicate.");
        string name = Binder.Name(input.Tokens[position++]);
        if (name is "all" or "any" or "not" && position < input.Tokens.Length && input.Tokens[position].Kind == SyntaxKind.OpenParenToken)
        {
            int close = input.Close[position++];
            var values = new List<bool>();
            while (position < close)
            {
                values.Add(Configuration(input, ref position, context));
                if (position < close && input.Tokens[position].Kind == SyntaxKind.CommaToken) position++;
                else if (position < close) throw new MacroException("Expected ',' between configuration predicates.");
            }
            position = close + 1;
            if (name == "not" && values.Count != 1) throw new MacroException("not() takes one configuration predicate.");
            return name switch { "all" => values.All(v => v), "any" => values.Any(v => v), _ => !values[0] };
        }
        if (position < input.Tokens.Length && input.Tokens[position].Kind == SyntaxKind.EqualsToken)
        {
            if (position + 1 >= input.Tokens.Length || input.Tokens[position + 1].Value is not string value) throw new MacroException("Expected a string value in the configuration predicate.");
            position += 2;
            return (name, value) switch
            {
                ("target_pointer_width", _) => value == context.PointerWidth.ToString(CultureInfo.InvariantCulture),
                ("target_endian", "little") or ("panic", "abort") => true,
                _ => false
            };
        }
        return name == "debug_assertions";
    }
}

internal readonly record struct FormatArgument(int Position, string? Name);

///<summary>Validates format strings, splits their literal pieces and lists the arguments they reference</summary>
internal static class FormatString
{
    internal static List<FormatArgument> Parse(string format, List<string> pieces)
    {
        var references = new List<FormatArgument>();
        var piece = new StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '}')
            {
                if (i + 1 < format.Length && format[i + 1] == '}') { piece.Append('}'); i++; continue; }
                throw new MacroException("Invalid format string: unmatched '}'.");
            }
            if (c != '{') { piece.Append(c); continue; }
            if (i + 1 < format.Length && format[i + 1] == '{') { piece.Append('{'); i++; continue; }
            pieces.Add(piece.ToString());
            piece.Clear();
            int close = format.IndexOf('}', i + 1);
            if (close < 0) throw new MacroException("Invalid format string: expected '}'.");
            string content = format[(i + 1)..close];
            int colon = content.IndexOf(':');
            string specification = colon < 0 ? "" : content[(colon + 1)..];
            if (specification.Contains(".*", StringComparison.Ordinal)) references.Add(new FormatArgument(next++, null));
            references.Add(Reference((colon < 0 ? content : content[..colon]).Trim(), ref next));
            for (int dollar = specification.IndexOf('$'); dollar >= 0; dollar = specification.IndexOf('$', dollar + 1))
            {
                int start = dollar;
                while (start > 0 && (char.IsLetterOrDigit(specification[start - 1]) || specification[start - 1] == '_')) start--;
                if (start < dollar) references.Add(Reference(specification[start..dollar], ref next));
            }
            i = close;
        }
        if (piece.Length != 0) pieces.Add(piece.ToString());
        return references;
    }
    private static FormatArgument Reference(string argument, ref int next)
    {
        if (argument.Length == 0) return new FormatArgument(next++, null);
        if (argument.All(char.IsAsciiDigit)) return new FormatArgument(int.Parse(argument, CultureInfo.InvariantCulture), null);
        if (!(char.IsLetter(argument[0]) || argument[0] == '_') || !argument.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new MacroException($"Invalid format string: invalid argument name '{argument}'.");
        return new FormatArgument(-1, argument);
    }
}
