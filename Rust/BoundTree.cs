using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Rust;

public enum BoundNodeKind : byte
{
    CompilationUnit, Function, Global, Error, Literal, Variable, FunctionReference, Tuple, Array, RepeatArray,
    Unary, Binary, Conversion, Assignment, Call, FieldAccess, Index, Construction, Block, If, Loop, For, Match,
    Return, Break, Continue, Range, Let, ExpressionStatement, ItemStatement, Pattern, MatchArm,
    LabelStatement, GotoStatement, ConditionalGotoStatement, Panic, Discriminant
}
public enum ConversionKind : byte { None, Identity, Never, MutableToSharedReference, ArrayToSliceReference, ReferenceToPointer, MutableToConstPointer, FunctionToPointer, NumericCast, PointerCast }
public enum ValueCategory : byte { Value, Place, MutablePlace }
public enum BoundPatternKind : byte { Error, Wildcard, Binding, Literal, Tuple, Aggregate, Reference, Or, Range, Slice, Rest }
public enum BindingMode : byte { Value, Reference, MutableReference }
public readonly record struct ConstantValue(bool HasValue, object? Value)
{
    public static ConstantValue None => default;
    public static ConstantValue From(object? value) => new(true, value);
}

///<summary>Base node produced by semantic binding</summary>
public abstract class BoundNode
{
    public BoundNodeKind Kind { get; }
    public SyntaxNode Syntax { get; }
    public ImmutableArray<BoundNode> Children { get; }
    internal bool OwnErrors;
    public bool HasErrors => OwnErrors || Children.Any(c => c.HasErrors);
    protected BoundNode(BoundNodeKind kind, SyntaxNode syntax, IEnumerable<BoundNode>? children = null, bool hasErrors = false)
    { Kind = kind; Syntax = syntax; Children = children?.ToImmutableArray() ?? []; OwnErrors = hasErrors; }
    public IEnumerable<BoundNode> Descendants()
    {
        var pending = new Stack<BoundNode>(Children.Reverse());
        while (pending.Count != 0)
        {
            BoundNode node = pending.Pop(); yield return node;
            foreach (BoundNode child in node.Children.Reverse()) pending.Push(child);
        }
    }
}

public abstract class BoundExpression : BoundNode
{
    internal TypeSymbol RawType;
    public TypeSymbol Type => RawType.Resolve();
    public ValueCategory ValueCategory { get; }
    public ConstantValue ConstantValue { get; internal set; }
    protected BoundExpression(BoundNodeKind kind, SyntaxNode syntax, TypeSymbol type, IEnumerable<BoundNode>? children = null, ValueCategory category = ValueCategory.Value, bool hasErrors = false)
        : base(kind, syntax, children, hasErrors) { RawType = type; ValueCategory = category; }
}

public abstract class BoundStatement(BoundNodeKind kind, SyntaxNode syntax, IEnumerable<BoundNode>? children = null) : BoundNode(kind, syntax, children);

public sealed class BoundCompilationUnit : BoundNode
{
    public ModuleSymbol Crate { get; }
    public ImmutableArray<BoundFunction> Functions { get; }
    public ImmutableArray<BoundGlobal> Globals { get; }
    internal BoundCompilationUnit(CompilationUnitSyntax syntax, ModuleSymbol crate, ImmutableArray<BoundFunction> functions, ImmutableArray<BoundGlobal> globals, bool errors)
        : base(BoundNodeKind.CompilationUnit, syntax, functions.Cast<BoundNode>().Concat(globals), errors) { Crate = crate; Functions = functions; Globals = globals; }
}

public sealed class BoundFunction : BoundNode
{
    public FunctionSymbol Symbol { get; }
    public ImmutableArray<BoundPattern> Parameters { get; }
    public BoundBlockExpression? Body { get; }
    internal BoundFunction(FunctionItemSyntax syntax, FunctionSymbol symbol, ImmutableArray<BoundPattern> parameters, BoundBlockExpression? body)
        : base(BoundNodeKind.Function, syntax, parameters.Cast<BoundNode>().Concat(body is null ? [] : new[] { body })) { Symbol = symbol; Parameters = parameters; Body = body; }
}

public sealed class BoundGlobal : BoundNode
{
    public GlobalSymbol Symbol { get; }
    public BoundExpression? Initializer { get; }
    internal BoundGlobal(ItemSyntax syntax, GlobalSymbol symbol, BoundExpression? initializer)
        : base(BoundNodeKind.Global, syntax, initializer is null ? [] : new[] { initializer }) { Symbol = symbol; Initializer = initializer; }
}

public sealed class BoundErrorExpression : BoundExpression
{
    internal BoundErrorExpression(SyntaxNode syntax, TypeSymbol error, IEnumerable<BoundNode>? children = null)
        : base(BoundNodeKind.Error, syntax, error, children, hasErrors: true) { }
}

public sealed class BoundLiteralExpression : BoundExpression
{
    public object? Value => ConstantValue.Value;
    internal BoundLiteralExpression(SyntaxNode syntax, TypeSymbol type, object? value) : base(BoundNodeKind.Literal, syntax, type) => ConstantValue = ConstantValue.From(value);
}

public sealed class BoundVariableExpression : BoundExpression
{
    public VariableSymbol Variable { get; }
    internal BoundVariableExpression(SyntaxNode syntax, VariableSymbol variable)
        : base(BoundNodeKind.Variable, syntax, variable.Type, category: variable.IsMutable ? ValueCategory.MutablePlace : ValueCategory.Place) => Variable = variable;
}

public sealed class BoundFunctionReference : BoundExpression
{
    public FunctionSymbol Function { get; }
    public ImmutableArray<TypeSymbol> TypeArguments { get; internal set; }
    internal BoundFunctionReference(SyntaxNode syntax, FunctionSymbol function, FunctionTypeSymbol type, ImmutableArray<TypeSymbol> arguments)
        : base(BoundNodeKind.FunctionReference, syntax, type) { Function = function; TypeArguments = arguments; }
}

public sealed class BoundTupleExpression : BoundExpression
{
    public ImmutableArray<BoundExpression> Elements { get; }
    internal BoundTupleExpression(SyntaxNode syntax, TypeSymbol type, ImmutableArray<BoundExpression> elements) : base(BoundNodeKind.Tuple, syntax, type, elements) => Elements = elements;
}

public sealed class BoundArrayExpression : BoundExpression
{
    public ImmutableArray<BoundExpression> Elements { get; }
    public BoundExpression? RepeatCount { get; }
    internal BoundArrayExpression(SyntaxNode syntax, TypeSymbol type, ImmutableArray<BoundExpression> elements, BoundExpression? repeatCount = null)
        : base(repeatCount is null ? BoundNodeKind.Array : BoundNodeKind.RepeatArray, syntax, type, elements.Cast<BoundNode>().Concat(repeatCount is null ? [] : new[] { repeatCount }))
    { Elements = elements; RepeatCount = repeatCount; }
}

public sealed class BoundUnaryExpression : BoundExpression
{
    public SyntaxKind Operator { get; }
    public BoundExpression Operand { get; }
    public bool IsRawBorrow { get; }
    internal BoundUnaryExpression(SyntaxNode syntax, TypeSymbol type, SyntaxKind op, BoundExpression operand, ValueCategory category = ValueCategory.Value, bool rawBorrow = false)
        : base(BoundNodeKind.Unary, syntax, type, new[] { operand }, category) { Operator = op; Operand = operand; IsRawBorrow = rawBorrow; }
}

public sealed class BoundBinaryExpression : BoundExpression
{
    public SyntaxKind Operator { get; }
    public BoundExpression Left { get; }
    public BoundExpression Right { get; }
    internal BoundBinaryExpression(SyntaxNode syntax, TypeSymbol type, SyntaxKind op, BoundExpression left, BoundExpression right)
        : base(BoundNodeKind.Binary, syntax, type, new[] { left, right }) { Operator = op; Left = left; Right = right; }
}

public sealed class BoundConversionExpression : BoundExpression
{
    public ConversionKind Conversion { get; }
    public BoundExpression Operand { get; }
    internal BoundConversionExpression(SyntaxNode syntax, TypeSymbol type, ConversionKind conversion, BoundExpression operand)
        : base(BoundNodeKind.Conversion, syntax, type, new[] { operand }) { Conversion = conversion; Operand = operand; }
}

public sealed class BoundAssignmentExpression : BoundExpression
{
    public SyntaxKind Operator { get; }
    public BoundExpression Target { get; }
    public BoundExpression Value { get; }
    internal BoundAssignmentExpression(SyntaxNode syntax, TypeSymbol unit, SyntaxKind op, BoundExpression target, BoundExpression value)
        : base(BoundNodeKind.Assignment, syntax, unit, new[] { target, value }) { Operator = op; Target = target; Value = value; }
}

public sealed class BoundCallExpression : BoundExpression
{
    public BoundExpression Target { get; }
    public ImmutableArray<BoundExpression> Arguments { get; }
    internal BoundCallExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression target, ImmutableArray<BoundExpression> arguments)
        : base(BoundNodeKind.Call, syntax, type, new[] { target }.Concat(arguments)) { Target = target; Arguments = arguments; }
}

public sealed class BoundFieldExpression : BoundExpression
{
    public BoundExpression Receiver { get; }
    public FieldSymbol? Field { get; }
    public int TupleIndex { get; }
    public string Name => Field?.Name ?? TupleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal BoundFieldExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression receiver, FieldSymbol? field, int index, ValueCategory category)
        : base(BoundNodeKind.FieldAccess, syntax, type, new[] { receiver }, category) { Receiver = receiver; Field = field; TupleIndex = index; }
}

public sealed class BoundIndexExpression : BoundExpression
{
    public BoundExpression Receiver { get; }
    public BoundExpression Index { get; }
    internal BoundIndexExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression receiver, BoundExpression index, ValueCategory category)
        : base(BoundNodeKind.Index, syntax, type, new[] { receiver, index }, category) { Receiver = receiver; Index = index; }
}

public readonly record struct BoundFieldInitializer(FieldSymbol Field, BoundExpression Value);
public sealed class BoundConstructionExpression : BoundExpression
{
    public VariantSymbol? Variant { get; }
    public ImmutableArray<BoundFieldInitializer> Fields { get; }
    public BoundExpression? Base { get; }
    internal BoundConstructionExpression(SyntaxNode syntax, NamedTypeSymbol type, VariantSymbol? variant, ImmutableArray<BoundFieldInitializer> fields, BoundExpression? baseValue = null)
        : base(BoundNodeKind.Construction, syntax, type, fields.Select(f => f.Value).Concat(baseValue is null ? [] : new[] { baseValue })) { Variant = variant; Fields = fields; Base = baseValue; }
}

public sealed class BoundBlockExpression : BoundExpression
{
    public ImmutableArray<BoundStatement> Statements { get; }
    public BoundExpression? Tail { get; }
    public ImmutableArray<LocalSymbol> Locals { get; }
    public LabelSymbol? Label { get; }
    internal BoundBlockExpression(SyntaxNode syntax, TypeSymbol type, ImmutableArray<BoundStatement> statements, BoundExpression? tail, ImmutableArray<LocalSymbol> locals, LabelSymbol? label = null)
        : base(BoundNodeKind.Block, syntax, type, statements.Cast<BoundNode>().Concat(tail is null ? [] : new[] { tail })) { Statements = statements; Tail = tail; Locals = locals; Label = label; }
}

public sealed class BoundIfExpression : BoundExpression
{
    public BoundExpression Condition { get; }
    public BoundBlockExpression Then { get; }
    public BoundExpression? Else { get; }
    internal BoundIfExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression condition, BoundBlockExpression then, BoundExpression? otherwise)
        : base(BoundNodeKind.If, syntax, type, new BoundNode[] { condition, then }.Concat(otherwise is null ? [] : new[] { otherwise })) { Condition = condition; Then = then; Else = otherwise; }
}

public sealed class BoundLoopExpression : BoundExpression
{
    public LabelSymbol Label { get; }
    public BoundExpression? Condition { get; }
    public BoundBlockExpression Body { get; }
    internal BoundLoopExpression(SyntaxNode syntax, TypeSymbol type, LabelSymbol label, BoundExpression? condition, BoundBlockExpression body)
        : base(BoundNodeKind.Loop, syntax, type, (condition is null ? Array.Empty<BoundNode>() : new BoundNode[] { condition }).Append(body)) { Label = label; Condition = condition; Body = body; }
}

public sealed class BoundForExpression : BoundExpression
{
    public LabelSymbol Label { get; }
    public BoundPattern Pattern { get; }
    public BoundExpression Iterator { get; }
    public BoundBlockExpression Body { get; }
    internal BoundForExpression(SyntaxNode syntax, TypeSymbol unit, LabelSymbol label, BoundPattern pattern, BoundExpression iterator, BoundBlockExpression body)
        : base(BoundNodeKind.For, syntax, unit, new BoundNode[] { pattern, iterator, body }) { Label = label; Pattern = pattern; Iterator = iterator; Body = body; }
}

public sealed class BoundControlFlowExpression : BoundExpression
{
    public LabelSymbol? Target { get; }
    public BoundExpression? Value { get; }
    internal BoundControlFlowExpression(BoundNodeKind kind, SyntaxNode syntax, TypeSymbol never, LabelSymbol? target, BoundExpression? value)
        : base(kind, syntax, never, value is null ? [] : new[] { value }) { Target = target; Value = value; }
}

public sealed class BoundRangeExpression : BoundExpression
{
    public BoundExpression? Start { get; }
    public BoundExpression? End { get; }
    public bool Inclusive { get; }
    internal BoundRangeExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression? start, BoundExpression? end, bool inclusive)
        : base(BoundNodeKind.Range, syntax, type, new[] { start, end }.OfType<BoundNode>()) { Start = start; End = end; Inclusive = inclusive; }
}

public sealed class BoundLetStatement : BoundStatement
{
    public BoundPattern Pattern { get; }
    public BoundExpression? Initializer { get; }
    public BoundBlockExpression? Else { get; }
    internal BoundLetStatement(SyntaxNode syntax, BoundPattern pattern, BoundExpression? initializer, BoundBlockExpression? otherwise)
        : base(BoundNodeKind.Let, syntax, new BoundNode?[] { pattern, initializer, otherwise }.OfType<BoundNode>()) { Pattern = pattern; Initializer = initializer; Else = otherwise; }
}

public sealed class BoundPatternTestExpression : BoundExpression
{
    public BoundPattern Pattern { get; }
    public BoundExpression Value { get; }
    internal BoundPatternTestExpression(SyntaxNode syntax, TypeSymbol boolean, BoundPattern pattern, BoundExpression value)
        : base(BoundNodeKind.Let, syntax, boolean, new BoundNode[] { pattern, value }) { Pattern = pattern; Value = value; }
}

public sealed class BoundExpressionStatement : BoundStatement
{
    public BoundExpression? Expression { get; }
    internal BoundExpressionStatement(SyntaxNode syntax, BoundExpression? expression)
        : base(BoundNodeKind.ExpressionStatement, syntax, expression is null ? [] : new[] { expression }) => Expression = expression;
}

public sealed class BoundLabelStatement : BoundStatement
{
    public LabelSymbol Label { get; }
    internal BoundLabelStatement(SyntaxNode syntax, LabelSymbol label) : base(BoundNodeKind.LabelStatement, syntax) => Label = label;
}

public sealed class BoundGotoStatement : BoundStatement
{
    public LabelSymbol Label { get; }
    public BoundExpression? Condition { get; }
    public bool JumpIfTrue { get; }
    internal BoundGotoStatement(SyntaxNode syntax, LabelSymbol label, BoundExpression? condition = null, bool jumpIfTrue = true)
        : base(condition is null ? BoundNodeKind.GotoStatement : BoundNodeKind.ConditionalGotoStatement, syntax, condition is null ? [] : new[] { condition })
    { Label = label; Condition = condition; JumpIfTrue = jumpIfTrue; }
}

public sealed class BoundPanicExpression : BoundExpression
{
    internal BoundPanicExpression(SyntaxNode syntax, TypeSymbol never) : base(BoundNodeKind.Panic, syntax, never) { }
}

public sealed class BoundDiscriminantExpression : BoundExpression
{
    public BoundExpression Operand { get; }
    internal BoundDiscriminantExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression operand)
        : base(BoundNodeKind.Discriminant, syntax, type, new[] { operand }) => Operand = operand;
}

public sealed class BoundPattern : BoundNode
{
    private readonly TypeSymbol _inputType;
    public BoundPatternKind PatternKind { get; }
    public TypeSymbol InputType => _inputType.Resolve();
    public int InputIndex { get; internal set; } = -1;
    public VariableSymbol? Variable { get; }
    public Symbol? MatchedSymbol { get; }
    public BindingMode BindingMode { get; }
    public ImmutableArray<BoundPattern> Subpatterns { get; }
    public ImmutableArray<FieldSymbol> Fields { get; }
    public BoundExpression? Value { get; }
    public bool IsIrrefutable { get; }
    internal BoundPattern(SyntaxNode syntax, TypeSymbol type, BoundPatternKind kind, ImmutableArray<BoundPattern> patterns = default,
        VariableSymbol? variable = null, Symbol? matched = null, BindingMode mode = BindingMode.Value, ImmutableArray<FieldSymbol> fields = default, BoundExpression? value = null, bool irrefutable = false)
        : base(BoundNodeKind.Pattern, syntax, (patterns.IsDefault ? ImmutableArray<BoundPattern>.Empty : patterns).Cast<BoundNode>().Concat(value is null ? [] : new[] { value }), kind == BoundPatternKind.Error)
    { _inputType = type; PatternKind = kind; Subpatterns = patterns.IsDefault ? [] : patterns; Variable = variable; MatchedSymbol = matched; BindingMode = mode; Fields = fields.IsDefault ? [] : fields; Value = value; IsIrrefutable = irrefutable; }
}

public sealed class BoundMatchArm : BoundNode
{
    public BoundPattern Pattern { get; }
    public BoundExpression? Guard { get; }
    public BoundExpression Value { get; }
    internal BoundMatchArm(SyntaxNode syntax, BoundPattern pattern, BoundExpression? guard, BoundExpression value)
        : base(BoundNodeKind.MatchArm, syntax, new BoundNode?[] { pattern, guard, value }.OfType<BoundNode>()) { Pattern = pattern; Guard = guard; Value = value; }
}

public sealed class BoundMatchExpression : BoundExpression
{
    public BoundExpression Scrutinee { get; }
    public ImmutableArray<BoundMatchArm> Arms { get; }
    internal BoundMatchExpression(SyntaxNode syntax, TypeSymbol type, BoundExpression scrutinee, ImmutableArray<BoundMatchArm> arms)
        : base(BoundNodeKind.Match, syntax, type, new BoundNode[] { scrutinee }.Concat(arms)) { Scrutinee = scrutinee; Arms = arms; }
}
