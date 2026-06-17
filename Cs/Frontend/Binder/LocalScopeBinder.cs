using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal sealed partial class LocalScopeBinder : Binder
    {
        private readonly struct LValue
        {
            public BoundExpression Target { get; }
            public BoundExpression Read { get; }
            public LValue(BoundExpression target, BoundExpression read)
            {
                Target = target;
                Read = read;
            }
        }
        private readonly struct SwitchCaseKey : IEquatable<SwitchCaseKey>
        {
            private readonly object? _value;
            private readonly Type? _runtimeType;

            public SwitchCaseKey(object? value)
            {
                _value = value;
                _runtimeType = value?.GetType();
            }

            public bool Equals(SwitchCaseKey other)
                => ReferenceEquals(_runtimeType, other._runtimeType)
                && object.Equals(_value, other._value);

            public override bool Equals(object? obj)
                => obj is SwitchCaseKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _runtimeType is null ? 0 : _runtimeType.GetHashCode();
                    h = (h * 397) ^ (_value is null ? 0 : _value.GetHashCode());
                    return h;
                }
            }
        }
        private sealed class SwitchGotoScope
        {
            private readonly Dictionary<SwitchCaseKey, LabelSymbol> _caseLabels;

            public TypeSymbol GoverningType { get; }
            public LabelSymbol? DefaultLabel { get; }

            public SwitchGotoScope(
                TypeSymbol governingType,
                Dictionary<SwitchCaseKey, LabelSymbol> caseLabels,
                LabelSymbol? defaultLabel)
            {
                GoverningType = governingType;
                _caseLabels = caseLabels;
                DefaultLabel = defaultLabel;
            }

            public bool TryGetCaseLabel(SwitchCaseKey key, out LabelSymbol? label)
                => _caseLabels.TryGetValue(key, out label);
        }
        private sealed class ControlFlowScope
        {
            private enum ExceptionRegionKind : byte
            {
                Try, Catch, Finally
            }
            private readonly struct ExceptionRegionFrame
            {
                public int Id { get; }
                public ExceptionRegionKind Kind { get; }
                public bool TryHasCatch { get; }
                public ExceptionRegionFrame(int id, ExceptionRegionKind kind, bool tryHasCatch)
                {
                    Id = id;
                    Kind = kind;
                    TryHasCatch = tryHasCatch;
                }
            }
            private readonly Symbol _containing;
            private readonly MethodSymbol? _method;

            private readonly Dictionary<string, LabelSymbol> _labelsByName = new(StringComparer.Ordinal);
            private readonly List<(GotoStatementSyntax Syntax, LabelSymbol Label)> _gotos = new();
            private readonly List<ExceptionRegionFrame> _exceptionRegionStack = new();
            private readonly Dictionary<LabelSymbol, ImmutableArray<ExceptionRegionFrame>> _labelRegions = new();
            private readonly List<(GotoStatementSyntax Syntax, LabelSymbol Label,
                ImmutableArray<ExceptionRegionFrame> SourceRegions)> _gotoRegions = new();
            private readonly Stack<LabelSymbol> _breakStack = new();
            private readonly Stack<LabelSymbol> _continueStack = new();
            private readonly Stack<SwitchGotoScope> _switchGotoStack = new();
            private int _nextGeneratedId;
            private bool _diagnosticsEmitted;
            private int _nextExceptionRegionId;
            public ControlFlowScope(Symbol containing)
            {
                _containing = containing;
                _method = containing as MethodSymbol;
            }

            public LabelSymbol NewGeneratedLabel(string prefix)
            {
                var id = ++_nextGeneratedId;
                var m = _method;
                if (m is not null)
                    return LabelSymbol.CreateGenerated($"<{prefix}#{id}>", m);

                return new LabelSymbol($"<{prefix}#{id}>", _containing);
            }

            public LabelSymbol GetOrCreateSourceLabel(string name)
            {
                if (!_labelsByName.TryGetValue(name, out var label))
                {
                    label = new LabelSymbol(name, _containing);
                    _labelsByName.Add(name, label);
                }
                return label;
            }
            public void RegisterGoto(GotoStatementSyntax syntax, LabelSymbol label)
            {
                var snapshot = SnapshotExceptionRegions();
                _gotos.Add((syntax, label));
                _gotoRegions.Add((syntax, label, snapshot));
            }

            public void PushLoop(LabelSymbol breakLabel, LabelSymbol continueLabel)
            {
                _breakStack.Push(breakLabel);
                _continueStack.Push(continueLabel);
            }

            public void PopLoop()
            {
                if (_breakStack.Count != 0)
                    _breakStack.Pop();
                if (_continueStack.Count != 0)
                    _continueStack.Pop();
            }
            public void PushBreak(LabelSymbol breakLabel)
                => _breakStack.Push(breakLabel);
            public void PopBreak()
            {
                if (_breakStack.Count != 0)
                    _breakStack.Pop();
            }
            public void PushTryRegion(bool hasCatch) => PushExceptionRegion(ExceptionRegionKind.Try, hasCatch);
            public void PushCatchRegion() => PushExceptionRegion(ExceptionRegionKind.Catch);
            public void PushFinallyRegion() => PushExceptionRegion(ExceptionRegionKind.Finally);
            public void PopExceptionRegion()
            {
                if (_exceptionRegionStack.Count != 0)
                    _exceptionRegionStack.RemoveAt(_exceptionRegionStack.Count - 1);
            }
            public bool IsInsideTryWithCatchRegion
            {
                get
                {
                    for (int i = _exceptionRegionStack.Count - 1; i >= 0; i--)
                    {
                        var frame = _exceptionRegionStack[i];
                        if (frame.Kind == ExceptionRegionKind.Try && frame.TryHasCatch)
                            return true;
                    }

                    return false;
                }
            }
            public bool IsInsideCatchRegion
            {
                get
                {
                    for (int i = _exceptionRegionStack.Count - 1; i >= 0; i--)
                        if (_exceptionRegionStack[i].Kind == ExceptionRegionKind.Catch)
                            return true;
                    return false;
                }
            }
            public bool IsInsideFinallyRegion
            {
                get
                {
                    for (int i = _exceptionRegionStack.Count - 1; i >= 0; i--)
                        if (_exceptionRegionStack[i].Kind == ExceptionRegionKind.Finally)
                            return true;

                    return false;
                }
            }
            public void RegisterLabelDefinition(LabelSymbol label)
            {
                if (!_labelRegions.ContainsKey(label))
                    _labelRegions[label] = SnapshotExceptionRegions();
            }
            public void ValidateBranchTransfer(
                SyntaxNode syntax,
                LabelSymbol targetLabel,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                var src = SnapshotExceptionRegions();
                if (!_labelRegions.TryGetValue(targetLabel, out var dst))
                    return;
                AddTransferDiagnosticIfInvalid(src, dst, syntax, context, diagnostics);
            }
            public void ValidateMethodExitTransfer(
                SyntaxNode syntax,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                var src = SnapshotExceptionRegions();
                var dst = ImmutableArray<ExceptionRegionFrame>.Empty; // method exit
                AddTransferDiagnosticIfInvalid(src, dst, syntax, context, diagnostics);
            }
            public bool TryGetCurrentBreak(out LabelSymbol breakLabel)
            {
                if (_breakStack.Count == 0)
                {
                    breakLabel = null!;
                    return false;
                }

                breakLabel = _breakStack.Peek();
                return true;
            }
            public bool TryGetCurrentContinue(out LabelSymbol continueLabel)
            {
                if (_continueStack.Count == 0)
                {
                    continueLabel = null!;
                    return false;
                }
                continueLabel = _continueStack.Peek();
                return true;
            }
            public bool TryGetCurrentSwitchGotoScope(out SwitchGotoScope scope)
            {
                if (_switchGotoStack.Count == 0)
                {
                    scope = null!;
                    return false;
                }

                scope = _switchGotoStack.Peek();
                return true;
            }
            public void ReportUndefinedLabels(BindingContext context, DiagnosticBag diagnostics)
            {
                if (_diagnosticsEmitted)
                    return;

                _diagnosticsEmitted = true;

                for (int i = 0; i < _gotos.Count; i++)
                {
                    var (syntax, label) = _gotos[i];
                    if (!label.IsDefined)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_FLOW004",
                            DiagnosticSeverity.Error,
                            $"Label '{label.Name}' does not exist in the current context.",
                            new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                    }
                }
                foreach (var (syntax, label, srcRegions) in _gotoRegions)
                {
                    if (!label.IsDefined)
                        continue;
                    if (!_labelRegions.TryGetValue(label, out var dstRegions))
                        continue;
                    AddTransferDiagnosticIfInvalid(srcRegions, dstRegions, syntax, context, diagnostics);
                }
            }
            private void PushExceptionRegion(ExceptionRegionKind kind, bool tryHasCatch = false)
                => _exceptionRegionStack.Add(new ExceptionRegionFrame(_nextExceptionRegionId++, kind, tryHasCatch));
            public void PushSwitchGotoScope(SwitchGotoScope scope)
                => _switchGotoStack.Push(scope);

            public void PopSwitchGotoScope()
            {
                if (_switchGotoStack.Count != 0)
                    _switchGotoStack.Pop();
            }
            private ImmutableArray<ExceptionRegionFrame> SnapshotExceptionRegions()
            {
                if (_exceptionRegionStack.Count == 0)
                    return ImmutableArray<ExceptionRegionFrame>.Empty;
                return ImmutableArray.CreateRange(_exceptionRegionStack);
            }
            private static void AddTransferDiagnosticIfInvalid(
                ImmutableArray<ExceptionRegionFrame> src,
                ImmutableArray<ExceptionRegionFrame> dst,
                SyntaxNode syntax,
                BindingContext context,
                DiagnosticBag diagnostics)
            {
                if (TryClassifyIllegalTransfer(src, dst, out var id, out var message))
                {
                    diagnostics.Add(new Diagnostic(
                        id,
                        DiagnosticSeverity.Error,
                        message,
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                }
            }
            private static bool TryClassifyIllegalTransfer(
                ImmutableArray<ExceptionRegionFrame> src,
                ImmutableArray<ExceptionRegionFrame> dst,
                out string diagnosticId,
                out string diagnosticMessage)
            {
                int common = 0;
                int max = Math.Min(src.Length, dst.Length);
                while (common < max &&
                    src[common].Id == dst[common].Id &&
                    src[common].Kind == dst[common].Kind)
                {
                    common++;
                }
                if (dst.Length > common)
                {
                    diagnosticId = "CN_FLOW009";
                    diagnosticMessage = "Control cannot enter a try, catch, or finally block.";
                    return true;
                }
                for (int i = common; i < src.Length; i++)
                {
                    if (src[i].Kind == ExceptionRegionKind.Finally)
                    {
                        diagnosticId = "CN_FLOW008";
                        diagnosticMessage = "Control cannot leave a finally clause.";
                        return true;
                    }
                }
                diagnosticId = string.Empty;
                diagnosticMessage = string.Empty;
                return false;
            }
        }
        private sealed class BoundTypeOnlyExpression : BoundExpression
        {
            public override BoundNodeKind Kind => BoundNodeKind.BadExpression;

            public BoundTypeOnlyExpression(ExpressionSyntax syntax, TypeSymbol type)
                : base(syntax)
            {
                Type = type;
                ConstantValueOpt = Optional<object>.None;
            }
        }
        private sealed class LambdaMethodSymbol : MethodSymbol
        {
            public override string Name { get; }
            public override Symbol? ContainingSymbol { get; }
            public override ImmutableArray<Location> Locations { get; }
            public override TypeSymbol ReturnType { get; }
            private ImmutableArray<ParameterSymbol> _parameters;
            public override ImmutableArray<ParameterSymbol> Parameters => _parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : _parameters;
            public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
            public override bool IsStatic { get; }
            public override bool IsConstructor => false;
            public override bool IsAsync { get; }

            public LambdaMethodSymbol(
                string name,
                Symbol containing,
                TypeSymbol returnType,
                ImmutableArray<ParameterSymbol> parameters,
                ImmutableArray<Location> locations,
                bool isStatic,
                bool isAsync)
            {
                Name = name;
                ContainingSymbol = containing;
                ReturnType = returnType;
                _parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
                Locations = locations.IsDefault ? ImmutableArray<Location>.Empty : locations;
                IsStatic = isStatic;
                IsAsync = isAsync;
            }

            public void SetParameters(ImmutableArray<ParameterSymbol> parameters)
                => _parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
        }
        private sealed class BoundOutDiscardExpression : BoundExpression
        {
            public override BoundNodeKind Kind => BoundNodeKind.BadExpression;

            public TypeSymbol? ExplicitElementTypeOpt { get; }

            public BoundOutDiscardExpression(
                ExpressionSyntax syntax,
                TypeSymbol type,
                TypeSymbol? explicitElementTypeOpt)
                : base(syntax)
            {
                Type = type;
                ExplicitElementTypeOpt = explicitElementTypeOpt;
                ConstantValueOpt = Optional<object>.None;
            }
        }
        private sealed class BoundOutVarPendingExpression : BoundExpression
        {
            public override BoundNodeKind Kind => BoundNodeKind.BadExpression;

            public string Name { get; }
            public SingleVariableDesignationSyntax Designation { get; }

            public BoundOutVarPendingExpression(
                DeclarationExpressionSyntax syntax,
                string name,
                SingleVariableDesignationSyntax designation,
                TypeSymbol markerType)
                : base(syntax)
            {
                Name = name;
                Designation = designation;
                Type = markerType;
                ConstantValueOpt = Optional<object>.None;
            }
        }
        private enum BindValueKind : byte
        {
            RValue, LValue
        }
        private sealed class NullBindingRecorder : IBindingRecorder
        {
            public static readonly NullBindingRecorder Instance = new();
            private NullBindingRecorder() { }

            public void RecordDeclared(SyntaxNode syntax, Symbol symbol) { }
            public void RecordBound(SyntaxNode syntax, BoundNode node) { }
        }
        private const string OperatorPrefix = "op_";
        private static BindingContext WithRecorder(BindingContext context, IBindingRecorder recorder)
            => new BindingContext(context.Compilation, context.SemanticModel, context.ContainingSymbol, recorder);
        private static readonly SyntaxTrivia[] s_noTrivia = Array.Empty<SyntaxTrivia>();
        private readonly Symbol _containing;
        private readonly ControlFlowScope _flow;
        private readonly Dictionary<string, LocalSymbol> _locals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ParameterSymbol> _parameters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LocalFunctionSymbol> _localFunctions = new(StringComparer.Ordinal);
        private readonly LocalScopeBinder? _nameConflictStop;

        private int _tempId;
        public LocalScopeBinder(
            Binder? parent,
            BinderFlags flags,
            Symbol containing,
            bool inheritFlowFromParent = true)
            : base(parent, flags)
        {
            _containing = containing;

            _flow =
                inheritFlowFromParent && parent is LocalScopeBinder ls
                ? ls._flow
                : new ControlFlowScope(containing);

            _nameConflictStop = (parent as LocalScopeBinder)?._nameConflictStop;

            if (containing is MethodSymbol m)
            {
                for (int i = 0; i < m.Parameters.Length; i++)
                    _parameters[m.Parameters[i].Name] = m.Parameters[i];
            }
        }
        private LocalScopeBinder(LocalScopeBinder template, BinderFlags flags)
            : base(template.Parent, flags)
        {
            _containing = template._containing;
            _flow = template._flow;
            _locals = template._locals;
            _parameters = template._parameters;
            _localFunctions = template._localFunctions;
            _nameConflictStop = template._nameConflictStop;
        }
        private LocalScopeBinder WithFlags(BinderFlags flags) => new LocalScopeBinder(this, flags);
        private bool IsCheckedOverflowContext
        {
            get
            {
                if ((Flags & BinderFlags.CheckedContext) != 0) return true;
                if ((Flags & BinderFlags.UncheckedContext) != 0) return false;
                return false; // default unchecked
            }
        }
        private static BinderFlags ApplyCheckedContext(BinderFlags flags, bool isChecked)
        {
            flags &= ~(BinderFlags.CheckedContext | BinderFlags.UncheckedContext);
            flags |= isChecked ? BinderFlags.CheckedContext : BinderFlags.UncheckedContext;
            return flags;
        }
        private static bool IsOverflowCheckedUnaryOperator(BoundUnaryOperatorKind op, TypeSymbol type)
        {
            if (op != BoundUnaryOperatorKind.UnaryMinus)
                return false;

            return type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64;
        }
        private static bool IsOverflowCheckedBinaryOperator(BoundBinaryOperatorKind op, TypeSymbol type)
        {
            if (op is not (
                BoundBinaryOperatorKind.Add or
                BoundBinaryOperatorKind.Subtract or
                BoundBinaryOperatorKind.Multiply or
                BoundBinaryOperatorKind.Divide or
                BoundBinaryOperatorKind.Modulo))
            {
                return false;
            }
            return type.SpecialType is
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private void DeclareLocal(LocalSymbol local, SyntaxNode syntax, BindingContext context)
        {
            _locals[local.Name] = local;
            context.Recorder.RecordDeclared(syntax, local);
        }
        private void ImportFlowingLocal(LocalSymbol local)
            => _locals[local.Name] = local;
        public override Symbol? GetDeclaredSymbol(SyntaxNode declaration)
            => Parent?.GetDeclaredSymbol(declaration);

        public override BoundStatement BindStatement(StatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundStatement result;
            switch (node)
            {
                case BlockSyntax block:
                    result = BindBlock(block, context, diagnostics);
                    break;

                case ExpressionStatementSyntax es:
                    {
                        if (TryBindConditionalAccessExpressionStatement(es.Expression, context, diagnostics, out var conditionalStatement))
                        {
                            result = conditionalStatement;
                            break;
                        }

                        BoundExpression expr;
                        if (es.Expression is ImplicitObjectCreationExpressionSyntax ioc)
                            expr = BindImplicitObjectCreation(ioc, context, diagnostics);
                        else
                            expr = BindDiscardedExpression(es.Expression, context, diagnostics);
                        result = new BoundExpressionStatement(es, expr);
                    }
                    break;

                case UnsafeStatementSyntax us:
                    {
                        var unsafeBinder = (Flags & BinderFlags.UnsafeRegion) != 0
                            ? this
                            : WithFlags(Flags | BinderFlags.UnsafeRegion);

                        var inner = unsafeBinder.BindStatement(us.Block, context, diagnostics);

                        if (inner is BoundBlockStatement b)
                            result = new BoundBlockStatement(us, b.Statements);
                        else
                            result = inner;
                    }
                    break;

                case LocalDeclarationStatementSyntax ld:
                    result = BindLocalDeclaration(ld, context, diagnostics);
                    break;

                case UsingStatementSyntax us:
                    result = BindUsingStatement(us, context, diagnostics);
                    break;

                case ReturnStatementSyntax rs:
                    result = BindReturn(rs, context, diagnostics);
                    break;

                case YieldStatementSyntax ys:
                    result = BindYield(ys, context, diagnostics);
                    break;

                case ThrowStatementSyntax th:
                    result = BindThrow(th, context, diagnostics);
                    break;

                case BreakStatementSyntax @break:
                    result = BindBreak(@break, context, diagnostics);
                    break;

                case ContinueStatementSyntax @continue:
                    result = BindContinue(@continue, context, diagnostics);
                    break;

                case EmptyStatementSyntax empty:
                    result = new BoundEmptyStatement(empty);
                    break;
                case IfStatementSyntax @if:
                    result = BindIf(@if, context, diagnostics);
                    break;
                case WhileStatementSyntax @while:
                    result = BindWhile(@while, context, diagnostics);
                    break;
                case DoStatementSyntax @do:
                    result = BindDoWhile(@do, context, diagnostics);
                    break;
                case TryStatementSyntax @try:
                    result = BindTry(@try, context, diagnostics);
                    break;
                case ForStatementSyntax @for:
                    result = BindFor(@for, context, diagnostics);
                    break;
                case ForEachStatementSyntax @foreach:
                    result = BindForEach(@foreach, context, diagnostics);
                    break;
                case ForEachVariableStatementSyntax @foreachVariable:
                    result = BindForEachVariable(@foreachVariable, context, diagnostics);
                    break;
                case GotoStatementSyntax @goto:
                    result = BindGoto(@goto, context, diagnostics);
                    break;
                case LabeledStatementSyntax labeled:
                    result = BindLabeledStatement(labeled, context, diagnostics);
                    break;
                case LocalFunctionStatementSyntax lf:
                    result = BindLocalFunctionStatement(lf, context, diagnostics);
                    break;
                case CheckedStatementSyntax chk:
                    result = BindCheckedStatement(chk, context, diagnostics);
                    break;
                case SwitchStatementSyntax sw:
                    result = BindSwitchStatement(sw, context, diagnostics);
                    break;
                case FixedStatementSyntax fs:
                    result = BindFixedStatement(fs, context, diagnostics);
                    break;

                default:
                    if (Parent != null)
                        result = Parent.BindStatement(node, context, diagnostics);
                    else
                    {
                        diagnostics.Add(new Diagnostic("CN_BIND001", DiagnosticSeverity.Error,
                            $"Statement not supported: {node.Kind}",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        result = new BoundBadStatement(node);
                    }
                    break;
            }

            return Record(node, result, context);
        }
        public override BoundExpression BindExpression(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundExpression result;

            switch (node)
            {
                case LiteralExpressionSyntax lit:
                    result = BindLiteral(lit, context, diagnostics);
                    break;
                case InterpolatedStringExpressionSyntax isx:
                    result = BindInterpolatedString(isx, context, diagnostics);
                    break;
                case IdentifierNameSyntax id:
                    result = BindIdentifier(id, context, diagnostics);
                    break;
                case GenericNameSyntax g:
                    result = BindGenericMethodGroup(g, context, diagnostics);
                    break;
                case MemberAccessExpressionSyntax ma:
                    result = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                    break;
                case CastExpressionSyntax cast:
                    {
                        var targetType = BindType(cast.Type, context, diagnostics);
                        var operand = BindExpression(cast.Expression, context, diagnostics);
                        result = ApplyConversion(
                            exprSyntax: cast,
                            expr: operand,
                            targetType: targetType,
                            diagnosticNode: cast,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: false);

                        break;
                    }
                case TupleExpressionSyntax te:
                    result = BindTupleExpression(te, context, diagnostics);
                    break;
                case ThisExpressionSyntax @this:
                    result = BindThis(@this, context, diagnostics);
                    break;
                case BaseExpressionSyntax @base:
                    result = BindBase(@base, context, diagnostics);
                    break;
                case ParenthesizedExpressionSyntax paren:
                    result = BindExpression(paren.Expression, context, diagnostics);
                    break;
                case PrefixUnaryExpressionSyntax pre:
                    result = BindPrefixUnary(pre, context, diagnostics);
                    break;
                case PostfixUnaryExpressionSyntax post:
                    result = BindPostfixUnary(post, context, diagnostics);
                    break;
                case BinaryExpressionSyntax bin:
                    result = BindBinary(bin, context, diagnostics);
                    break;
                case ConditionalExpressionSyntax cond:
                    result = BindConditional(cond, context, diagnostics);
                    break;
                case ConditionalAccessExpressionSyntax ca:
                    result = BindConditionalAccess(ca, context, diagnostics);
                    break;
                case AssignmentExpressionSyntax assign:
                    result = BindAssignment(assign, context, diagnostics);
                    break;
                case InvocationExpressionSyntax inv:
                    result = BindInvocation(inv, context, diagnostics);
                    break;
                case ImplicitObjectCreationExpressionSyntax ioc:
                    result = BindUnboundImplicitObjectCreation(ioc, context, diagnostics);
                    break;
                case CollectionExpressionSyntax ce:
                    result = BindUnboundCollectionExpression(ce, context, diagnostics);
                    break;
                case ObjectCreationExpressionSyntax oc:
                    result = BindObjectCreation(oc, context, diagnostics);
                    break;
                case ArrayCreationExpressionSyntax ac:
                    result = BindArrayCreation(ac, context, diagnostics);
                    break;
                case ImplicitArrayCreationExpressionSyntax iac:
                    result = BindImplicitArrayCreation(iac, context, diagnostics);
                    break;
                case StackAllocArrayCreationExpressionSyntax sa:
                    result = BindStackAlloc(sa, context, diagnostics);
                    break;
                case ImplicitStackAllocArrayCreationExpressionSyntax isa:
                    result = BindImplicitStackAlloc(isa, context, diagnostics);
                    break;
                case ElementAccessExpressionSyntax ea:
                    result = BindElementAccess(ea, BindValueKind.RValue, context, diagnostics);
                    break;
                case CheckedExpressionSyntax chk:
                    result = BindCheckedExpression(chk, context, diagnostics);
                    break;
                case TypeOfExpressionSyntax tof:
                    result = BindTypeOf(tof, context, diagnostics);
                    break;
                case SizeOfExpressionSyntax sz:
                    result = BindSizeOf(sz, context, diagnostics);
                    break;
                case DefaultExpressionSyntax def:
                    result = BindDefaultExpression(def, context, diagnostics);
                    break;
                case ThrowExpressionSyntax te:
                    result = BindThrowExpression(te, context, diagnostics);
                    break;
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                    result = new BoundUnboundLambdaExpression(node);
                    break;
                case IsPatternExpressionSyntax ip:
                    result = BindIsPatternExpression(ip, context, diagnostics);
                    break;
                case SwitchExpressionSyntax sw:
                    result = BindSwitchExpression(sw, context, diagnostics);
                    break;
                case RefExpressionSyntax re:
                    result = BindRefExpression(re, context, diagnostics);
                    break;


                case RangeExpressionSyntax r:
                    diagnostics.Add(new Diagnostic(
                        "CN_SLICE012",
                        DiagnosticSeverity.Error,
                        "Range expressions are only supported inside element access expressions.",
                        new Location(context.SemanticModel.SyntaxTree, r.Span)));
                    result = new BoundBadExpression(r);
                    break;
                default:
                    if (Parent != null)
                        result = Parent.BindExpression(node, context, diagnostics);
                    else
                    {
                        diagnostics.Add(new Diagnostic("CN_BIND002", DiagnosticSeverity.Error,
                            $"Expression not supported: {node.Kind}",
                            new Location(context.SemanticModel.SyntaxTree, node.Span)));
                        result = new BoundBadExpression(node);
                    }
                    break;
            }

            return Record(node, result, context);
        }

    }
}
