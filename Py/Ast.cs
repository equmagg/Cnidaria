using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Cnidaria.Python
{
    public enum SyntaxDiagnosticCode : byte
    {
        LexicalError,
        UnexpectedToken,
        ExpectedToken,
        ExpectedExpression,
        ExpectedStatement,
        InvalidAssignmentTarget,
        InvalidAugmentedAssignmentTarget,
        ExpectedIndentedBlock,
        ExpectedName,
        ExpectedPattern,
        ExpectedCaseClause,
        InvalidPattern,
        InvalidParameter,
        InvalidExceptClause,
        InvalidStringConcatenation,
        InvalidComprehension,
        InvalidArgument,
        InvalidDictionaryItem,
        InvalidInterpolation,
        InvalidMatchSubject,
        InvalidExpression,
    }

    public readonly struct SyntaxDiagnostic
    {
        public readonly SyntaxDiagnosticCode Code;
        public readonly TextSpan Span;
        public readonly string Message;
        public SyntaxDiagnostic(SyntaxDiagnosticCode code, TextSpan span, string message)
        {
            Code = code;
            Span = span;
            Message = message;
        }
    }

    internal readonly struct GreenDiagnostic
    {
        public readonly SyntaxDiagnosticCode Code;
        public readonly int Offset;
        public readonly int Width;
        public readonly string Message;
        public GreenDiagnostic(SyntaxDiagnosticCode code, int offset, int width, string message)
        {
            Code = code;
            Offset = offset;
            Width = width;
            Message = message;
        }
    }

    internal abstract class GreenNode
    {
        protected GreenNode(
            SyntaxKind kind,
            int fullWidth,
            ImmutableArray<GreenDiagnostic> diagnostics = default)
        {
            Kind = kind;
            FullWidth = fullWidth;
            Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        }

        public SyntaxKind Kind { get; }
        public int FullWidth { get; }
        public ImmutableArray<GreenDiagnostic> Diagnostics { get; }
        public bool ContainsDiagnostics => !Diagnostics.IsEmpty || ContainsDiagnosticsInChildren();

        public abstract int SlotCount { get; }
        public abstract GreenNode? GetSlot(int index);

        public virtual void WriteTo(StringBuilder builder)
        {
            for (var i = 0; i < SlotCount; i++)
                GetSlot(i)?.WriteTo(builder);
        }

        private bool ContainsDiagnosticsInChildren()
        {
            for (var i = 0; i < SlotCount; i++)
            {
                if (GetSlot(i)?.ContainsDiagnostics == true)
                    return true;
            }

            return false;
        }
    }

    internal struct GreenTrivia
    {
        internal GreenTrivia(SyntaxKind kind, string text)
        {
            if (kind is < SyntaxKind.WhitespaceTrivia or > SyntaxKind.SkippedTextTrivia)
                throw new ArgumentOutOfRangeException(nameof(kind));

            Kind = kind;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public SyntaxKind Kind { get; }
        public string Text { get; }
        public int FullWidth => Text.Length;
    }

    internal sealed class GreenToken : GreenNode
    {
        internal GreenToken(
            SyntaxKind kind,
            string text,
            ImmutableArray<GreenTrivia> leadingTrivia = default,
            ImmutableArray<GreenTrivia> trailingTrivia = default,
            ImmutableArray<GreenDiagnostic> diagnostics = default,
            bool isMissing = false)
            : base(
                kind,
                GetTriviaWidth(leadingTrivia) + (text?.Length ?? 0) + GetTriviaWidth(trailingTrivia),
                diagnostics)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            LeadingTrivia = leadingTrivia.IsDefault ? [] : leadingTrivia;
            TrailingTrivia = trailingTrivia.IsDefault ? [] : trailingTrivia;
            IsMissing = isMissing;
        }

        public string Text { get; }
        public ImmutableArray<GreenTrivia> LeadingTrivia { get; }
        public ImmutableArray<GreenTrivia> TrailingTrivia { get; }
        public bool IsMissing { get; }
        public int LeadingWidth => GetTriviaWidth(LeadingTrivia);
        public int Width => Text.Length;
        public int TrailingWidth => GetTriviaWidth(TrailingTrivia);

        public override int SlotCount => 0;

        public override GreenNode? GetSlot(int index) =>
            throw new ArgumentOutOfRangeException(nameof(index));

        public override void WriteTo(StringBuilder builder)
        {
            foreach (var trivia in LeadingTrivia)
                builder.Append(trivia.Text);

            builder.Append(Text);

            foreach (var trivia in TrailingTrivia)
                builder.Append(trivia.Text);
        }

        public static GreenToken Missing(SyntaxKind kind, string message) =>
            new(
                kind,
                string.Empty,
                diagnostics:
                [
                    new GreenDiagnostic(
                    SyntaxDiagnosticCode.ExpectedToken,
                    0,
                    0,
                    message)
                ],
                isMissing: true);

        private static int GetTriviaWidth(ImmutableArray<GreenTrivia> trivia)
        {
            if (trivia.IsDefaultOrEmpty)
                return 0;

            var width = 0;
            foreach (var item in trivia)
                width = checked(width + item.FullWidth);

            return width;
        }
    }

    internal sealed class GreenInternalNode : GreenNode
    {
        private readonly ImmutableArray<GreenNode?> _slots;

        internal GreenInternalNode(
            SyntaxKind kind,
            ImmutableArray<GreenNode?> slots,
            ImmutableArray<GreenDiagnostic> diagnostics = default)
            : base(kind, GetFullWidth(slots), diagnostics)
        {
            _slots = slots.IsDefault ? [] : slots;
        }

        public override int SlotCount => _slots.Length;

        public override GreenNode? GetSlot(int index) => _slots[index];

        private static int GetFullWidth(ImmutableArray<GreenNode?> slots)
        {
            if (slots.IsDefaultOrEmpty)
                return 0;

            var width = 0;
            foreach (var slot in slots)
            {
                if (slot is not null)
                    width = checked(width + slot.FullWidth);
            }

            return width;
        }
    }

    internal static class GreenFactory
    {
        public static GreenInternalNode Node(
            SyntaxKind kind,
            params GreenNode?[] children) =>
            new(kind, ImmutableArray.CreateRange(children));

        public static GreenInternalNode NodeWithDiagnostics(
            SyntaxKind kind,
            ImmutableArray<GreenNode?> children,
            ImmutableArray<GreenDiagnostic> diagnostics) =>
            new(kind, children, diagnostics);

        public static GreenInternalNode List(IEnumerable<GreenNode?> children) =>
            new(SyntaxKind.SyntaxList, [.. children]);

        public static GreenInternalNode SeparatedList(IEnumerable<GreenNode?> children) =>
            new(SyntaxKind.SeparatedSyntaxList, [.. children]);

        public static GreenInternalNode MissingExpression(string message) =>
            new(
                SyntaxKind.MissingExpression,
                [],
                [
                    new GreenDiagnostic(
                    SyntaxDiagnosticCode.ExpectedExpression,
                    0,
                    0,
                    message)
                ]);

        public static GreenInternalNode ErrorExpression(
            GreenNode unexpected,
            string message) =>
            new(
                SyntaxKind.ErrorExpression,
                [unexpected],
                [
                    new GreenDiagnostic(
                        SyntaxDiagnosticCode.UnexpectedToken,
                        0,
                        unexpected.FullWidth,
                        message)
                ]);

        public static GreenInternalNode MissingPattern(string message) =>
            new(
                SyntaxKind.MissingPattern,
                [],
                [
                    new GreenDiagnostic(
                        SyntaxDiagnosticCode.ExpectedPattern,
                        0,
                        0,
                        message)
                ]);

        public static GreenInternalNode ErrorPattern(
            GreenNode unexpected,
            string message) =>
            new(
                SyntaxKind.ErrorPattern,
                [unexpected],
                [
                    new GreenDiagnostic(
                        SyntaxDiagnosticCode.InvalidPattern,
                        0,
                        unexpected.FullWidth,
                        message)
                ]);
    }

    public sealed class SyntaxTree
    {
        private readonly GreenNode _greenRoot;
        private SyntaxNode? _root;

        private SyntaxTree(string text, GreenNode greenRoot)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            _greenRoot = greenRoot ?? throw new ArgumentNullException(nameof(greenRoot));
        }

        public string Text { get; }

        public SyntaxNode GetRoot() =>
            _root ??= new SyntaxNode(this, parent: null, _greenRoot, position: 0, index: 0);

        public ImmutableArray<SyntaxDiagnostic> GetDiagnostics() =>
            GetDiagnostics(_greenRoot, position: 0);

        public SymbolTable GetSymbolTable(SymbolTableOptions? options = null) =>
            SymbolTableBuilder.Build(this, options);

        public string ToFullString()
        {
            var builder = new StringBuilder(_greenRoot.FullWidth);
            _greenRoot.WriteTo(builder);
            return builder.ToString();
        }

        public static SyntaxTree Parse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            var parser = new Parser(text);
            var tree = new SyntaxTree(text, parser.ParseCompilationUnit());

            Debug.Assert(
                string.Equals(tree.ToFullString(), text, StringComparison.Ordinal),
                "The syntax tree must retain every source character exactly once.");

            return tree;
        }

        internal ImmutableArray<SyntaxDiagnostic> GetDiagnostics(
            GreenNode node,
            int position)
        {
            var builder = ImmutableArray.CreateBuilder<SyntaxDiagnostic>();
            CollectDiagnostics(node, position, builder);
            return builder.ToImmutable();
        }

        private static void CollectDiagnostics(GreenNode node, int position, ImmutableArray<SyntaxDiagnostic>.Builder builder)
        {
            foreach (var diagnostic in node.Diagnostics)
            {
                builder.Add(new SyntaxDiagnostic(
                    diagnostic.Code,
                    new TextSpan(
                        checked(position + diagnostic.Offset),
                        diagnostic.Width),
                    diagnostic.Message));
            }

            var childPosition = position;
            for (var i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetSlot(i);
                if (child is null)
                    continue;

                CollectDiagnostics(child, childPosition, builder);
                childPosition = checked(childPosition + child.FullWidth);
            }
        }
    }

    public sealed class SyntaxNode
    {
        internal SyntaxNode(
            SyntaxTree syntaxTree,
            SyntaxNode? parent,
            GreenNode green,
            int position,
            int index)
        {
            SyntaxTree = syntaxTree;
            Parent = parent;
            Green = green;
            Position = position;
            Index = index;
        }

        internal GreenNode Green { get; }
        internal int Index { get; }

        public SyntaxTree SyntaxTree { get; }
        public SyntaxNode? Parent { get; }
        public SyntaxKind Kind => Green.Kind;
        public int Position { get; }
        public TextSpan FullSpan => new(Position, Green.FullWidth);
        public bool ContainsDiagnostics => Green.ContainsDiagnostics;

        public TextSpan Span
        {
            get
            {
                SyntaxToken? first = null;
                SyntaxToken? last = null;
                foreach (var token in DescendantTokens())
                {
                    if (token.IsMissing)
                        continue;

                    first ??= token;
                    last = token;
                }

                return first is null
                    ? new TextSpan(Position, 0)
                    : TextSpan.FromBounds(first.Value.Span.Start, last!.Value.Span.End);
            }
        }

        public IEnumerable<SyntaxNodeOrToken> ChildNodesAndTokens()
        {
            var childPosition = Position;
            for (var i = 0; i < Green.SlotCount; i++)
            {
                var child = Green.GetSlot(i);
                if (child is null)
                    continue;

                if (child is GreenToken token)
                {
                    yield return new SyntaxNodeOrToken(
                        new SyntaxToken(SyntaxTree, this, token, childPosition, i));
                }
                else
                {
                    yield return new SyntaxNodeOrToken(
                        new SyntaxNode(SyntaxTree, this, child, childPosition, i));
                }

                childPosition = checked(childPosition + child.FullWidth);
            }
        }

        public IEnumerable<SyntaxNode> ChildNodes()
        {
            foreach (var child in ChildNodesAndTokens())
            {
                if (child.IsNode)
                    yield return child.AsNode();
            }
        }

        public IEnumerable<SyntaxToken> ChildTokens()
        {
            foreach (var child in ChildNodesAndTokens())
            {
                if (child.IsToken)
                    yield return child.AsToken();
            }
        }

        public IEnumerable<SyntaxToken> DescendantTokens()
        {
            foreach (var child in ChildNodesAndTokens())
            {
                if (child.IsToken)
                {
                    yield return child.AsToken();
                    continue;
                }

                foreach (var token in child.AsNode().DescendantTokens())
                    yield return token;
            }
        }

        public ImmutableArray<SyntaxDiagnostic> GetDiagnostics() =>
            SyntaxTree.GetDiagnostics(Green, Position);

        public string ToFullString()
        {
            var builder = new StringBuilder(Green.FullWidth);
            Green.WriteTo(builder);
            return builder.ToString();
        }

        public override string ToString() => ToFullString();
    }

    public readonly struct SyntaxNodeOrToken
    {
        private readonly SyntaxNode? _node;
        private readonly SyntaxToken _token;

        internal SyntaxNodeOrToken(SyntaxNode node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            _token = default;
        }

        internal SyntaxNodeOrToken(SyntaxToken token)
        {
            _node = null;
            _token = token;
        }

        public bool IsNode => _node is not null;
        public bool IsToken => _node is null;
        public SyntaxKind Kind => IsNode ? _node!.Kind : _token.Kind;
        public TextSpan FullSpan => IsNode ? _node!.FullSpan : _token.FullSpan;

        public SyntaxNode AsNode() =>
            _node ?? throw new InvalidOperationException("The value is a token.");

        public SyntaxToken AsToken() =>
            IsToken ? _token : throw new InvalidOperationException("The value is a node.");
    }

    public readonly struct SyntaxToken
    {
        private readonly SyntaxTree? _syntaxTree;
        private readonly GreenToken? _green;

        internal SyntaxToken(
            SyntaxTree syntaxTree,
            SyntaxNode parent,
            GreenToken green,
            int position,
            int index)
        {
            _syntaxTree = syntaxTree;
            Parent = parent;
            _green = green;
            Position = position;
            Index = index;
        }

        internal int Index { get; }

        public SyntaxNode? Parent { get; }
        public SyntaxKind Kind => _green?.Kind ?? SyntaxKind.None;
        public string Text => _green?.Text ?? string.Empty;
        public int Position { get; }
        public bool IsMissing => _green?.IsMissing == true;
        public TextSpan FullSpan => new(Position, _green?.FullWidth ?? 0);
        public TextSpan Span => new(
            checked(Position + (_green?.LeadingWidth ?? 0)),
            _green?.Width ?? 0);

        public ImmutableArray<SyntaxTrivia> LeadingTrivia =>
            CreateTrivia(_green?.LeadingTrivia ?? [], Position);

        public ImmutableArray<SyntaxTrivia> TrailingTrivia
        {
            get
            {
                var green = _green;
                if (green is null)
                    return [];

                var start = checked(Position + green.LeadingWidth + green.Width);
                return CreateTrivia(green.TrailingTrivia, start);
            }
        }

        public ImmutableArray<SyntaxDiagnostic> GetDiagnostics() =>
            _green is null || _syntaxTree is null
                ? []
                : _syntaxTree.GetDiagnostics(_green, Position);

        public string ToFullString()
        {
            if (_green is null)
                return string.Empty;

            var builder = new StringBuilder(_green.FullWidth);
            _green.WriteTo(builder);
            return builder.ToString();
        }

        public override string ToString() => Text;

        private static ImmutableArray<SyntaxTrivia> CreateTrivia(ImmutableArray<GreenTrivia> trivia, int position)
        {
            if (trivia.IsDefaultOrEmpty)
                return [];

            var builder = ImmutableArray.CreateBuilder<SyntaxTrivia>(trivia.Length);
            foreach (var item in trivia)
            {
                builder.Add(new SyntaxTrivia(item.Kind, item.Text, position));
                position = checked(position + item.FullWidth);
            }

            return builder.MoveToImmutable();
        }
    }

    public readonly struct SyntaxTrivia
    {
        public readonly SyntaxKind Kind;
        public readonly string Text;
        public readonly int Position;
        public SyntaxTrivia(SyntaxKind kind, string text, int position)
        {
            Kind = kind;
            Text = text;
            Position = position;
        }
        public TextSpan FullSpan => new(Position, Text.Length);
    }

}
