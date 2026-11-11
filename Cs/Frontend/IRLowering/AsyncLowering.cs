using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Cnidaria.Cs;

// Suspension belongs to the runtime, so IL only has to keep awaits out of handlers and byrefs off the stack across them
internal static class AsyncLowering
{
    public static BoundMethodBody Rewrite(Compilation compilation, BoundMethodBody body, DiagnosticBag diagnostics)
        => (BoundMethodBody)new FunctionRewriter(compilation, diagnostics).RewriteNode(body);

    private static bool IsAwaitCall(BoundCallExpression call)
        => call.Method.Name is "Await" or "AwaitAwaiter" or "UnsafeAwaitAwaiter" &&
           call.Method.ContainingSymbol is NamedTypeSymbol helpers && helpers.Name == "AsyncHelpers" &&
           helpers.ContainingSymbol is NamespaceSymbol containing && containing.Name == "CompilerServices";

    private static BoundBlockStatement AsBlock(BoundStatement statement)
        => statement as BoundBlockStatement ?? new BoundBlockStatement(statement.Syntax, ImmutableArray.Create(statement));

    private static BoundStatement Assign(SyntaxNode syntax, LocalSymbol local, BoundExpression value)
        => new BoundExpressionStatement(syntax, new BoundAssignmentExpression(syntax, new BoundLocalExpression(syntax, local), value));

    private sealed class FunctionRewriter : BoundTreeRewriterWithStackGuard
    {
        private readonly Compilation _compilation;
        private readonly DiagnosticBag _diagnostics;

        public FunctionRewriter(Compilation compilation, DiagnosticBag diagnostics)
        {
            _compilation = compilation;
            _diagnostics = diagnostics;
        }

        protected override BoundMethodBody RewriteMethodBody(BoundMethodBody node)
        {
            BoundMethodBody rewritten = base.RewriteMethodBody(node);
            if (!node.Method.IsAsync)
                return rewritten;

            return new BoundMethodBody(rewritten.Syntax, rewritten.Method, Lower(rewritten.Method, rewritten.Body));
        }

        protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
        {
            BoundExpression rewritten = base.RewriteLambdaExpression(node);
            if (!node.IsAsync || rewritten is not BoundLambdaExpression lambda)
                return rewritten;

            return new BoundLambdaExpression(
                (ExpressionSyntax)lambda.Syntax,
                (NamedTypeSymbol)lambda.Type,
                lambda.Method,
                lambda.InvokeMethod,
                Lower(lambda.Method, lambda.Body),
                lambda.IsStatic,
                lambda.IsAsync,
                lambda.TargetOpt);
        }

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
        {
            BoundStatement rewritten = base.RewriteLocalFunctionStatement(node);
            if (!node.LocalFunction.IsAsync || rewritten is not BoundLocalFunctionStatement function)
                return rewritten;

            return new BoundLocalFunctionStatement(
                (LocalFunctionStatementSyntax)function.Syntax,
                function.LocalFunction,
                Lower(function.LocalFunction, function.Body));
        }

        private BoundStatement Lower(MethodSymbol method, BoundStatement body)
        {
            var context = new FunctionContext(_compilation, method, _diagnostics);
            body = CopyStructThis(context, method, body);
            body = (BoundStatement)new HandlerRewriter(context).RewriteNode(body);
            body = (BoundStatement)new AwaitSpiller(context).RewriteNode(body);
            ByRefLiveness.Check(context, body);
            return body;
        }

        // The caller's struct cannot be reached through a byref after a suspension
        private static BoundStatement CopyStructThis(FunctionContext context, MethodSymbol method, BoundStatement body)
        {
            if (method.IsStatic || method.ContainingSymbol is not NamedTypeSymbol type || !type.IsValueType)
                return body;

            LocalSymbol copy = context.NewLocal("$this", type);
            var rewritten = (BoundStatement)new StructThisRewriter(copy).RewriteNode(body);
            BoundStatement entry = Assign(body.Syntax, copy, new BoundThisExpression(new ThisExpressionSyntax(default), type));
            return new BoundBlockStatement(body.Syntax, ImmutableArray.Create(entry, rewritten));
        }
    }

    private sealed class FunctionContext
    {
        private readonly Compilation _compilation;
        private readonly MethodSymbol _method;
        private readonly DiagnosticBag _diagnostics;
        private int _nextId;
        private (MethodSymbol Capture, MethodSymbol Throw)? _dispatchInfo;

        public FunctionContext(Compilation compilation, MethodSymbol method, DiagnosticBag diagnostics)
        {
            _compilation = compilation;
            _method = method;
            _diagnostics = diagnostics;
        }

        public MethodSymbol Method => _method;
        public TypeSymbol Int32 => _compilation.GetSpecialType(SpecialType.System_Int32);
        public TypeSymbol Boolean => _compilation.GetSpecialType(SpecialType.System_Boolean);
        public TypeSymbol Object => _compilation.GetSpecialType(SpecialType.System_Object);
        public TypeSymbol ByRef(TypeSymbol type) => _compilation.CreateByRefType(type);

        public LocalSymbol NewLocal(string name, TypeSymbol type)
            => new LocalSymbol(name + _nextId++, _method, type, ImmutableArray<Location>.Empty);

        public LabelSymbol NewLabel(string name) => LabelSymbol.CreateGenerated($"<{name}_{_nextId++}>", _method);

        public BoundExpression Int(SyntaxNode syntax, int value) => new BoundLiteralExpression(syntax, Int32, value);

        public BoundExpression IntEquals(SyntaxNode syntax, LocalSymbol local, int value)
            => new BoundBinaryExpression(syntax, BoundBinaryOperatorKind.Equals, Boolean, new BoundLocalExpression(syntax, local), Int(syntax, value), Optional<object>.None);

        // ExceptionDispatchInfo keeps the original stack trace, which a plain throw of the caught object would lose
        public BoundStatement MakeRethrow(SyntaxNode syntax, LocalSymbol caught)
        {
            NamedTypeSymbol exceptionType = LookupExceptionType();
            if (DerivesFromException(caught.Type, exceptionType))
                return MakeCaptureThrow(syntax, new BoundLocalExpression(syntax, caught));

            LocalSymbol exception = NewLocal("$exception", exceptionType);
            LabelSymbol isException = NewLabel("rethrow");
            var conversion = new Conversion(ConversionKind.ExplicitReference);
            return new BoundBlockStatement(syntax, ImmutableArray.Create<BoundStatement>(
                Assign(syntax, exception, new BoundAsExpression(syntax, exceptionType, new BoundLocalExpression(syntax, caught), conversion)),
                new BoundConditionalGotoStatement(syntax, new BoundLocalExpression(syntax, exception), isException, jumpIfTrue: true),
                new BoundThrowStatement(syntax, new BoundLocalExpression(syntax, caught)),
                new BoundLabelStatement(syntax, isException),
                MakeCaptureThrow(syntax, new BoundLocalExpression(syntax, exception))));
        }

        private BoundStatement MakeCaptureThrow(SyntaxNode syntax, BoundExpression exception)
        {
            (MethodSymbol capture, MethodSymbol rethrow) = _dispatchInfo ??= LookupDispatchInfo();
            var captured = new BoundCallExpression(syntax, receiverOpt: null, capture, ImmutableArray.Create(exception));
            return new BoundExpressionStatement(syntax, new BoundCallExpression(syntax, captured, rethrow, ImmutableArray<BoundExpression>.Empty));
        }

        private (MethodSymbol Capture, MethodSymbol Throw) LookupDispatchInfo()
        {
            NamedTypeSymbol type = LocalScopeBinder.GetWellKnownType(_compilation, new[] { "System", "Runtime", "ExceptionServices" }, "ExceptionDispatchInfo", 0)
                ?? throw new InvalidOperationException("Required type 'System.Runtime.ExceptionServices.ExceptionDispatchInfo' was not found.");
            MethodSymbol[] methods = type.GetMembers().OfType<MethodSymbol>().ToArray();
            return (
                methods.First(m => m.Name == "Capture" && m.IsStatic && m.Parameters.Length == 1),
                methods.First(m => m.Name == "Throw" && !m.IsStatic && m.Parameters.Length == 0));
        }

        private NamedTypeSymbol LookupExceptionType()
            => LocalScopeBinder.GetWellKnownType(_compilation, new[] { "System" }, "Exception", 0)
                ?? throw new InvalidOperationException("Required type 'System.Exception' was not found.");

        private static bool DerivesFromException(TypeSymbol type, NamedTypeSymbol exception)
        {
            for (var current = type as NamedTypeSymbol; current is not null; current = current.BaseType as NamedTypeSymbol)
            {
                if (ReferenceEquals(current, exception))
                    return true;
            }

            return false;
        }

        public void ReportLiveAcrossAwait(LocalSymbol local)
            => Report("CN_ASYNC004", "A local of by-reference or by-reference-like type cannot be preserved across 'await'.",
                local.Locations.IsDefaultOrEmpty ? MethodLocation() : local.Locations[0]);

        public void ReportReferenceAcrossAwait(SyntaxNode syntax)
        {
            Location location = MethodLocation();
            Report("CN_ASYNC005", "A reference returned by a call cannot be preserved across 'await'.", new Location(location.SyntaxTree, syntax.Span));
        }

        private void Report(string id, string message, Location location)
            => _diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, location));

        private Location MethodLocation()
        {
            for (Symbol? symbol = _method; symbol is not null; symbol = symbol.ContainingSymbol)
            {
                if (!symbol.Locations.IsDefaultOrEmpty)
                    return symbol.Locations[0];
            }

            throw new InvalidOperationException($"Async function '{_method.Name}' has no source location.");
        }
    }

    private sealed class AwaitFinder : BoundTreeRewriterWithStackGuard
    {
        private bool _found;

        public static bool Contains(BoundNode node)
        {
            var finder = new AwaitFinder();
            finder.RewriteNode(node);
            return finder._found;
        }

        protected override BoundExpression RewriteExpressionCore(BoundExpression node)
            => _found ? node : base.RewriteExpressionCore(node);

        protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
        {
            if (IsAwaitCall(node))
            {
                _found = true;
                return node;
            }

            return base.RewriteCallExpression(node);
        }

        protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node) => node;

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;
    }

    private sealed class StructThisRewriter : BoundTreeRewriterWithStackGuard
    {
        private readonly LocalSymbol _copy;

        public StructThisRewriter(LocalSymbol copy)
        {
            _copy = copy;
        }

        protected override BoundExpression RewriteExpressionCore(BoundExpression node)
            => node is BoundThisExpression ? new BoundLocalExpression(node.Syntax, _copy) : base.RewriteExpressionCore(node);

        protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
        {
            BoundExpression rewritten = base.RewriteCallExpression(node);
            if (rewritten is not BoundCallExpression call || call.ReceiverOpt is not null || call.Method.IsStatic)
                return rewritten;

            return new BoundCallExpression(call.Syntax, new BoundLocalExpression(call.Syntax, _copy), call.Method, call.Arguments, call.ConstrainedToTypeOpt);
        }

        protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node) => node;

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;
    }

    // A handler cannot suspend, so a catch or finally that awaits runs after its try, driven by what the try recorded
    private sealed class HandlerRewriter : BoundTreeRewriterWithStackGuard
    {
        private readonly FunctionContext _context;
        private LocalSymbol? _returnValue;

        public HandlerRewriter(FunctionContext context)
        {
            _context = context;
        }

        protected override BoundExpression RewriteExpressionCore(BoundExpression node) => node;

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;

        protected override BoundStatement RewriteTryStatement(BoundTryStatement node)
        {
            var rewritten = (BoundTryStatement)base.RewriteTryStatement(node);
            bool catchAwaits = rewritten.CatchBlocks.Any(c => AwaitFinder.Contains(c.Body));
            bool finallyAwaits = rewritten.FinallyBlockOpt is not null && AwaitFinder.Contains(rewritten.FinallyBlockOpt);
            if (!catchAwaits && !finallyAwaits)
                return rewritten;

            var syntax = (TryStatementSyntax)rewritten.Syntax;
            BoundStatement guarded = rewritten.TryBlock;
            if (!rewritten.CatchBlocks.IsEmpty)
            {
                var withCatches = new BoundTryStatement(syntax, rewritten.TryBlock, rewritten.CatchBlocks, finallyBlockOpt: null);
                guarded = catchAwaits ? LowerCatches(withCatches) : withCatches;
            }

            if (rewritten.FinallyBlockOpt is null)
                return guarded;

            return finallyAwaits
                ? LowerFinally(syntax, guarded, rewritten.FinallyBlockOpt)
                : new BoundTryStatement(syntax, AsBlock(guarded), ImmutableArray<BoundCatchBlock>.Empty, rewritten.FinallyBlockOpt);
        }

        private BoundStatement LowerCatches(BoundTryStatement node)
        {
            SyntaxNode syntax = node.Syntax;
            LocalSymbol pendingCatch = _context.NewLocal("$pendingCatch", _context.Int32);
            var catches = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.CatchBlocks.Length);
            var moved = new List<BoundBlockStatement>();

            foreach (BoundCatchBlock handler in node.CatchBlocks)
            {
                if (!AwaitFinder.Contains(handler.Body))
                {
                    catches.Add(handler);
                    continue;
                }

                moved.Add(handler.Body);
                var body = new BoundBlockStatement(handler.Body.Syntax, ImmutableArray.Create(
                    Assign(handler.Body.Syntax, pendingCatch, _context.Int(handler.Body.Syntax, moved.Count))));

                LocalSymbol caught = handler.ExceptionLocalOpt ?? _context.NewLocal("$caught", handler.ExceptionType);
                var rethrows = new RethrowRewriter(_context, caught);
                moved[moved.Count - 1] = AsBlock((BoundStatement)rethrows.RewriteNode(handler.Body));

                catches.Add(new BoundCatchBlock(
                    (CatchClauseSyntax)handler.Syntax,
                    handler.ExceptionType,
                    handler.ExceptionLocalOpt ?? (rethrows.Rewrote ? caught : null),
                    handler.FilterOpt,
                    body));
            }

            var statements = ImmutableArray.CreateBuilder<BoundStatement>();
            statements.Add(Assign(syntax, pendingCatch, _context.Int(syntax, 0)));
            statements.Add(new BoundTryStatement((TryStatementSyntax)syntax, node.TryBlock, catches.ToImmutable(), finallyBlockOpt: null));

            LabelSymbol end = _context.NewLabel("catch_end");
            for (int i = 0; i < moved.Count; i++)
            {
                bool last = i == moved.Count - 1;
                LabelSymbol next = last ? end : _context.NewLabel("catch_next");
                statements.Add(new BoundConditionalGotoStatement(syntax, _context.IntEquals(syntax, pendingCatch, i + 1), next, jumpIfTrue: false));
                statements.Add(moved[i]);
                if (!last)
                {
                    statements.Add(new BoundGotoStatement(syntax, end));
                    statements.Add(new BoundLabelStatement(syntax, next));
                }
            }

            statements.Add(new BoundLabelStatement(syntax, end));
            return new BoundBlockStatement(syntax, statements.ToImmutable());
        }

        private BoundStatement LowerFinally(TryStatementSyntax syntax, BoundStatement guarded, BoundBlockStatement finallyBlock)
        {
            LocalSymbol pending = _context.NewLocal("$pendingException", _context.Object);
            LocalSymbol pendingBranch = _context.NewLocal("$pendingBranch", _context.Int32);
            LabelSymbol finallyLabel = _context.NewLabel("finally");
            var exits = new ExitRewriter(this, guarded, pendingBranch, finallyLabel);
            BoundBlockStatement body = AsBlock((BoundStatement)exits.RewriteNode(guarded));

            var statements = ImmutableArray.CreateBuilder<BoundStatement>();
            statements.Add(Assign(syntax, pending, new BoundLiteralExpression(syntax, _context.Object, null)));
            if (exits.Targets.Count != 0)
                statements.Add(Assign(syntax, pendingBranch, _context.Int(syntax, 0)));

            var catchAll = new BoundCatchBlock(
                new CatchClauseSyntax(default, null, null, new BlockSyntax(default, SyntaxList<StatementSyntax>.Empty, default)),
                _context.Object,
                pending,
                filterOpt: null,
                new BoundBlockStatement(syntax, ImmutableArray<BoundStatement>.Empty));
            statements.Add(new BoundTryStatement(syntax, body, ImmutableArray.Create(catchAll), finallyBlockOpt: null));

            statements.Add(new BoundLabelStatement(syntax, finallyLabel));
            statements.Add(finallyBlock);

            LabelSymbol noException = _context.NewLabel("no_exception");
            statements.Add(new BoundConditionalGotoStatement(syntax, new BoundLocalExpression(syntax, pending), noException, jumpIfTrue: false));
            statements.Add(_context.MakeRethrow(syntax, pending));
            statements.Add(new BoundLabelStatement(syntax, noException));

            for (int i = 0; i < exits.Targets.Count; i++)
            {
                int kind = i + 1;
                if (exits.Targets[i] is LabelSymbol target)
                {
                    statements.Add(new BoundConditionalGotoStatement(syntax, _context.IntEquals(syntax, pendingBranch, kind), target, jumpIfTrue: true));
                    continue;
                }

                LabelSymbol skip = _context.NewLabel("no_return");
                BoundExpression? value = _returnValue is null ? null : new BoundLocalExpression(syntax, _returnValue);
                statements.Add(new BoundConditionalGotoStatement(syntax, _context.IntEquals(syntax, pendingBranch, kind), skip, jumpIfTrue: false));
                statements.Add(new BoundReturnStatement(syntax, value));
                statements.Add(new BoundLabelStatement(syntax, skip));
            }

            return new BoundBlockStatement(syntax, statements.ToImmutable());
        }

        private LocalSymbol GetReturnValueLocal(TypeSymbol type) => _returnValue ??= _context.NewLocal("$returnValue", type);

        private sealed class RethrowRewriter : BoundTreeRewriterWithStackGuard
        {
            private readonly FunctionContext _context;
            private readonly LocalSymbol _caught;

            public RethrowRewriter(FunctionContext context, LocalSymbol caught)
            {
                _context = context;
                _caught = caught;
            }

            public bool Rewrote { get; private set; }

            protected override BoundExpression RewriteExpressionCore(BoundExpression node) => node;

            protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;

            protected override BoundStatement RewriteThrowStatement(BoundThrowStatement node)
            {
                if (node.ExpressionOpt is not null)
                    return node;

                Rewrote = true;
                return _context.MakeRethrow(node.Syntax, _caught);
            }

            // A rethrow in a nested catch belongs to that catch
            protected override BoundStatement RewriteTryStatement(BoundTryStatement node)
            {
                BoundBlockStatement tryBlock = AsBlock(RewriteStatement(node.TryBlock));
                return ReferenceEquals(tryBlock, node.TryBlock)
                    ? node
                    : new BoundTryStatement((TryStatementSyntax)node.Syntax, tryBlock, node.CatchBlocks, node.FinallyBlockOpt);
            }
        }

        // Jumps out of the protected code record their target and go through the moved finally first
        private sealed class ExitRewriter : BoundTreeRewriter
        {
            private readonly HandlerRewriter _owner;
            private readonly LocalSymbol _pendingBranch;
            private readonly LabelSymbol _finallyLabel;
            private readonly HashSet<LabelSymbol> _inner = new(ReferenceEqualityComparer<LabelSymbol>.Instance);
            private static readonly object ReturnTarget = new();

            public ExitRewriter(HandlerRewriter owner, BoundStatement guarded, LocalSymbol pendingBranch, LabelSymbol finallyLabel)
            {
                _owner = owner;
                _pendingBranch = pendingBranch;
                _finallyLabel = finallyLabel;
                CollectLabels(guarded);
            }

            public List<object> Targets { get; } = new();

            private void CollectLabels(BoundStatement statement)
            {
                switch (statement)
                {
                    case BoundBlockStatement block:
                        foreach (BoundStatement child in block.Statements)
                            CollectLabels(child);
                        break;
                    case BoundLabelStatement label:
                        _inner.Add(label.Label);
                        break;
                    case BoundTryStatement @try:
                        CollectLabels(@try.TryBlock);
                        foreach (BoundCatchBlock handler in @try.CatchBlocks)
                            CollectLabels(handler.Body);
                        if (@try.FinallyBlockOpt is not null)
                            CollectLabels(@try.FinallyBlockOpt);
                        break;
                }
            }

            protected override BoundExpression RewriteExpressionCore(BoundExpression node) => node;

            protected override BoundStatement RewriteStatement(BoundStatement node)
            {
                switch (node)
                {
                    case BoundGotoStatement jump when !_inner.Contains(jump.TargetLabel):
                        return Exit(jump.Syntax, jump.TargetLabel, ImmutableArray<BoundStatement>.Empty);
                    case BoundConditionalGotoStatement jump when !_inner.Contains(jump.TargetLabel):
                        {
                            LabelSymbol skip = _owner._context.NewLabel("no_exit");
                            return new BoundBlockStatement(jump.Syntax, ImmutableArray.Create<BoundStatement>(
                                new BoundConditionalGotoStatement(jump.Syntax, jump.Condition, skip, !jump.JumpIfTrue),
                                Exit(jump.Syntax, jump.TargetLabel, ImmutableArray<BoundStatement>.Empty),
                                new BoundLabelStatement(jump.Syntax, skip)));
                        }
                    case BoundReturnStatement ret:
                        return Exit(ret.Syntax, ReturnTarget, StoreReturnValue(ret));
                    case BoundLocalFunctionStatement:
                        return node;
                    default:
                        return base.RewriteStatement(node);
                }
            }

            private ImmutableArray<BoundStatement> StoreReturnValue(BoundReturnStatement ret)
            {
                if (ret.Expression is null)
                    return ImmutableArray<BoundStatement>.Empty;

                // The dispatch of a nested moved finally already reads the shared local
                LocalSymbol local = _owner.GetReturnValueLocal(ret.Expression.Type);
                return ret.Expression is BoundLocalExpression current && ReferenceEquals(current.Local, local)
                    ? ImmutableArray<BoundStatement>.Empty
                    : ImmutableArray.Create(Assign(ret.Syntax, local, ret.Expression));
            }

            private BoundStatement Exit(SyntaxNode syntax, object target, ImmutableArray<BoundStatement> before)
            {
                int index = Targets.IndexOf(target);
                if (index < 0)
                {
                    index = Targets.Count;
                    Targets.Add(target);
                }

                var statements = ImmutableArray.CreateBuilder<BoundStatement>(before.Length + 2);
                statements.AddRange(before);
                statements.Add(Assign(syntax, _pendingBranch, _owner._context.Int(syntax, index + 1)));
                statements.Add(new BoundGotoStatement(syntax, _finallyLabel));
                return new BoundBlockStatement(syntax, statements.ToImmutable());
            }

            // Control cannot leave a finally, so only try and catch bodies have exits
            protected override BoundStatement RewriteTryStatement(BoundTryStatement node)
            {
                BoundBlockStatement tryBlock = AsBlock(RewriteStatement(node.TryBlock));
                var catches = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.CatchBlocks.Length);
                foreach (BoundCatchBlock handler in node.CatchBlocks)
                {
                    catches.Add(new BoundCatchBlock(
                        (CatchClauseSyntax)handler.Syntax,
                        handler.ExceptionType,
                        handler.ExceptionLocalOpt,
                        handler.FilterOpt,
                        AsBlock(RewriteStatement(handler.Body))));
                }

                return new BoundTryStatement((TryStatementSyntax)node.Syntax, tryBlock, catches.ToImmutable(), node.FinallyBlockOpt);
            }
        }
    }

    // A value may wait on the stack during an await, an address may not: what follows it is hoisted and the address recomputed
    private sealed class AwaitSpiller : BoundTreeRewriterWithStackGuard
    {
        private readonly FunctionContext _context;
        private readonly HashSet<BoundExpression> _stable = new(ReferenceEqualityComparer<BoundExpression>.Instance);

        public AwaitSpiller(FunctionContext context)
        {
            _context = context;
        }

        protected override BoundExpression RewriteExpressionCore(BoundExpression node)
            => AwaitFinder.Contains(node) ? base.RewriteExpressionCore(node) : node;

        protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node) => node;

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;

        protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
        {
            BoundExpression rewritten = base.RewriteCallExpression(node);
            if (rewritten is not BoundCallExpression call)
                return rewritten;

            int last = LastAwait(call.Arguments);
            if (last < 0)
                return call;

            bool receiverByRef = call.ReceiverOpt is not null && IsReceiverPassedByRef(call, call.ReceiverOpt);
            if (!receiverByRef && !HasByRefBefore(call.Arguments, last))
                return call;

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var effects = ImmutableArray.CreateBuilder<BoundStatement>();
            BoundExpression? receiver = call.ReceiverOpt;
            if (receiver is not null)
                receiver = receiverByRef ? Stabilize(receiver, locals, effects) : SpillOperand(receiver, locals, effects);
            ImmutableArray<BoundExpression> arguments = SpillArguments(call.Arguments, last, locals, effects);
            return new BoundSequenceExpression(
                call.Syntax,
                locals.ToImmutable(),
                effects.ToImmutable(),
                new BoundCallExpression(call.Syntax, receiver, call.Method, arguments, call.ConstrainedToTypeOpt));
        }

        protected override BoundExpression RewriteObjectCreationExpression(BoundObjectCreationExpression node)
        {
            BoundExpression rewritten = base.RewriteObjectCreationExpression(node);
            if (rewritten is not BoundObjectCreationExpression creation)
                return rewritten;

            int last = LastAwait(creation.Arguments);
            if (last < 0 || !HasByRefBefore(creation.Arguments, last))
                return creation;

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var effects = ImmutableArray.CreateBuilder<BoundStatement>();
            ImmutableArray<BoundExpression> arguments = SpillArguments(creation.Arguments, last, locals, effects);
            return new BoundSequenceExpression(
                creation.Syntax,
                locals.ToImmutable(),
                effects.ToImmutable(),
                new BoundObjectCreationExpression(creation.Syntax, (NamedTypeSymbol)creation.Type, creation.ConstructorOpt, arguments));
        }

        protected override BoundExpression RewriteAssignmentExpression(BoundAssignmentExpression node)
        {
            BoundExpression rewritten = base.RewriteAssignmentExpression(node);
            if (rewritten is not BoundAssignmentExpression assignment ||
                assignment.Right is BoundRefExpression ||
                !AwaitFinder.Contains(assignment.Right) ||
                !StoresThroughAddress(assignment.Left))
            {
                return rewritten;
            }

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var effects = ImmutableArray.CreateBuilder<BoundStatement>();
            BoundExpression left = Stabilize(assignment.Left, locals, effects);
            BoundExpression value = SpillToTemp(assignment.Right, locals, effects);
            return new BoundSequenceExpression(
                assignment.Syntax,
                locals.ToImmutable(),
                effects.ToImmutable(),
                new BoundAssignmentExpression(assignment.Syntax, left, value));
        }

        // Lowering stabilizes a mutated struct through a ref temp, which an await in the mutation would outlive
        protected override BoundExpression RewriteSequenceExpression(BoundSequenceExpression node)
        {
            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var effects = ImmutableArray.CreateBuilder<BoundStatement>();
            var temps = new Dictionary<LocalSymbol, BoundExpression>(ReferenceEqualityComparer<LocalSymbol>.Instance);

            for (int i = 0; i < node.SideEffects.Length; i++)
            {
                BoundStatement statement = temps.Count == 0 ? node.SideEffects[i] : (BoundStatement)new RefTempSubstituter(temps).RewriteNode(node.SideEffects[i]);
                if (TryGetRefTempDefinition(statement, out LocalSymbol? temp, out BoundExpression? referent) && AwaitsAfter(node, i))
                {
                    temps[temp] = Stabilize(RewriteExpression(referent), locals, effects);
                    continue;
                }

                effects.Add(RewriteStatement(statement));
            }

            BoundExpression value = temps.Count == 0 ? node.Value : (BoundExpression)new RefTempSubstituter(temps).RewriteNode(node.Value);
            value = RewriteExpression(value);

            foreach (LocalSymbol local in node.Locals)
            {
                if (!temps.ContainsKey(local))
                    locals.Add(local);
            }

            return new BoundSequenceExpression(node.Syntax, locals.ToImmutable(), effects.ToImmutable(), value);
        }

        private static bool TryGetRefTempDefinition(BoundStatement statement, [NotNullWhen(true)] out LocalSymbol? temp, [NotNullWhen(true)] out BoundExpression? referent)
        {
            switch (statement)
            {
                case BoundLocalDeclarationStatement declaration when declaration.Local.IsByRef && declaration.Initializer is BoundRefExpression initializer:
                    temp = declaration.Local;
                    referent = initializer.Operand;
                    return true;
                case BoundExpressionStatement expression when
                    expression.Expression is BoundAssignmentExpression assignment &&
                    assignment.Left is BoundLocalExpression left && left.Local.IsByRef &&
                    assignment.Right is BoundRefExpression assigned:
                    temp = left.Local;
                    referent = assigned.Operand;
                    return true;
                default:
                    temp = null;
                    referent = null;
                    return false;
            }
        }

        private static bool AwaitsAfter(BoundSequenceExpression sequence, int index)
        {
            for (int i = index + 1; i < sequence.SideEffects.Length; i++)
            {
                if (AwaitFinder.Contains(sequence.SideEffects[i]))
                    return true;
            }

            return AwaitFinder.Contains(sequence.Value);
        }

        private static int LastAwait(ImmutableArray<BoundExpression> arguments)
        {
            for (int i = arguments.Length - 1; i >= 0; i--)
            {
                if (AwaitFinder.Contains(arguments[i]))
                    return i;
            }

            return -1;
        }

        private static bool HasByRefBefore(ImmutableArray<BoundExpression> arguments, int end)
        {
            for (int i = 0; i < end; i++)
            {
                if (IsByRefOperand(arguments[i]))
                    return true;
            }

            return false;
        }

        private static bool IsByRefOperand(BoundExpression operand)
            => operand is BoundRefExpression || (operand is BoundSequenceExpression sequence && IsByRefOperand(sequence.Value));

        private static bool IsReceiverPassedByRef(BoundCallExpression call, BoundExpression receiver)
        {
            if (call.Method.IsStatic)
                return call.Method.ExtensionMember is ExtensionMemberInfo extension && !extension.IsStatic && call.Method.Parameters[0].Type is ByRefTypeSymbol;

            TypeSymbol type = receiver is BoundConversionExpression conversion && conversion.Conversion.Kind == ConversionKind.Boxing ? conversion.Operand.Type : receiver.Type;
            return type is TypeParameterSymbol || type.IsValueType;
        }

        // Mirrors which stores the emitter starts by pushing an address for
        private static bool StoresThroughAddress(BoundExpression left)
        {
            switch (left)
            {
                case BoundLocalExpression local:
                    return local.Local.IsByRef;
                case BoundParameterExpression:
                case BoundClosureAccessExpression:
                    return false;
                case BoundArrayElementAccessExpression element:
                    return element.Indices.Length == 1 && element.Expression.Type is ArrayTypeSymbol array && array.Rank != 1;
                case BoundMemberAccessExpression member when member.Member is FieldSymbol field:
                    return field.Type is ByRefTypeSymbol ||
                        (!field.IsStatic && member.ReceiverOpt is not null && member.ReceiverOpt.Type.IsValueType);
                default:
                    return true;
            }
        }

        private ImmutableArray<BoundExpression> SpillArguments(
            ImmutableArray<BoundExpression> arguments,
            int last,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder effects)
        {
            var result = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
            for (int i = 0; i < arguments.Length; i++)
                result.Add(i <= last ? SpillOperand(arguments[i], locals, effects) : arguments[i]);
            return result.MoveToImmutable();
        }

        private BoundExpression SpillOperand(
            BoundExpression operand,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder effects)
        {
            switch (operand)
            {
                case BoundLiteralExpression:
                case BoundThisExpression:
                case BoundBaseExpression:
                    return operand;
                case BoundRefExpression reference:
                    return new BoundRefExpression(reference.Syntax, reference.Type, Stabilize(reference.Operand, locals, effects));
                case BoundSequenceExpression sequence:
                    locals.AddRange(sequence.Locals);
                    effects.AddRange(sequence.SideEffects);
                    return SpillOperand(sequence.Value, locals, effects);
                default:
                    return SpillToTemp(operand, locals, effects);
            }
        }

        private BoundExpression SpillToTemp(
            BoundExpression value,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder effects)
        {
            LocalSymbol temp = _context.NewLocal("$spill", value.Type);
            locals.Add(temp);
            effects.Add(Assign(value.Syntax, temp, value));
            return new BoundLocalExpression(value.Syntax, temp);
        }

        // Evaluates everything an lvalue depends on, faults included, and returns a form that is free to evaluate later
        private BoundExpression Stabilize(
            BoundExpression lvalue,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder effects)
        {
            if (_stable.Contains(lvalue))
                return lvalue;

            BoundExpression stable;
            switch (lvalue)
            {
                case BoundLocalExpression:
                case BoundParameterExpression:
                case BoundThisExpression:
                case BoundBaseExpression:
                case BoundClosureAccessExpression:
                    stable = lvalue;
                    break;

                case BoundSequenceExpression sequence:
                    locals.AddRange(sequence.Locals);
                    effects.AddRange(sequence.SideEffects);
                    stable = Stabilize(sequence.Value, locals, effects);
                    break;

                case BoundConversionExpression boxing when boxing.Conversion.Kind == ConversionKind.Boxing:
                    stable = new BoundConversionExpression(
                        boxing.Syntax,
                        boxing.Type,
                        Stabilize(boxing.Operand, locals, effects),
                        boxing.Conversion,
                        boxing.IsChecked);
                    break;

                case BoundMemberAccessExpression member when member.Member is FieldSymbol field && !field.IsStatic && member.ReceiverOpt is BoundExpression receiver:
                    if (receiver.Type.IsValueType)
                    {
                        stable = RebuildField(member, Stabilize(receiver, locals, effects));
                        break;
                    }

                    stable = RebuildField(member, SpillOperand(receiver, locals, effects));
                    if (receiver is not (BoundThisExpression or BoundBaseExpression))
                        effects.Add(new BoundExpressionStatement(member.Syntax, stable));
                    break;

                case BoundMemberAccessExpression staticField when staticField.Member is FieldSymbol:
                    stable = lvalue;
                    break;

                case BoundArrayElementAccessExpression element:
                    {
                        BoundExpression array = SpillOperand(element.Expression, locals, effects);
                        var indices = ImmutableArray.CreateBuilder<BoundExpression>(element.Indices.Length);
                        foreach (BoundExpression index in element.Indices)
                            indices.Add(SpillOperand(index, locals, effects));
                        stable = new BoundArrayElementAccessExpression(element.Syntax, element.Type, array, indices.MoveToImmutable());
                        effects.Add(new BoundExpressionStatement(element.Syntax, new BoundRefExpression(element.Syntax, _context.ByRef(element.Type), stable)));
                        break;
                    }

                case BoundPointerIndirectionExpression indirection:
                    stable = new BoundPointerIndirectionExpression(indirection.Syntax, indirection.Type, SpillOperand(indirection.Operand, locals, effects));
                    break;

                case BoundPointerElementAccessExpression pointerElement:
                    stable = new BoundPointerElementAccessExpression(
                        pointerElement.Syntax,
                        pointerElement.Type,
                        SpillOperand(pointerElement.Expression, locals, effects),
                        SpillOperand(pointerElement.Index, locals, effects));
                    break;

                default:
                    if (lvalue.IsLValue)
                    {
                        _context.ReportReferenceAcrossAwait(lvalue.Syntax);
                        return lvalue;
                    }

                    stable = SpillToTemp(lvalue, locals, effects);
                    break;
            }

            _stable.Add(stable);
            return stable;
        }

        private static BoundExpression RebuildField(BoundMemberAccessExpression member, BoundExpression receiver)
            => new BoundMemberAccessExpression(
                (ExpressionSyntax)member.Syntax,
                receiver,
                member.Member,
                member.Type,
                member.IsLValue,
                member.ConstantValueOpt,
                member.HasErrors,
                member.ConstrainedToTypeOpt);

        private sealed class RefTempSubstituter : BoundTreeRewriterWithStackGuard
        {
            private readonly Dictionary<LocalSymbol, BoundExpression> _temps;

            public RefTempSubstituter(Dictionary<LocalSymbol, BoundExpression> temps)
            {
                _temps = temps;
            }

            protected override BoundExpression RewriteExpressionCore(BoundExpression node)
                => node is BoundLocalExpression local && _temps.TryGetValue(local.Local, out BoundExpression? stable)
                    ? stable
                    : base.RewriteExpressionCore(node);

            protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node) => node;

            protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node) => node;
        }
    }

    // Reports ref and ref-like locals still in use after an await, by liveness over the flattened body
    private sealed class ByRefLiveness : BoundTreeRewriter
    {
        private enum StepKind : byte
        {
            None,
            Use,
            Define,
            Await,
            Jump,
            ConditionalJump,
            End,
        }

        private readonly record struct Step(StepKind Kind, LocalSymbol? Local, LabelSymbol? Label);

        private readonly MethodSymbol _method;
        private readonly List<Step> _steps = new();
        private readonly Dictionary<LabelSymbol, int> _labels = new(ReferenceEqualityComparer<LabelSymbol>.Instance);
        private readonly List<(int Start, int End, int Handler)> _handlers = new();
        private readonly Dictionary<LocalSymbol, int> _tracked = new(ReferenceEqualityComparer<LocalSymbol>.Instance);
        private bool _hasAwait;
        private int _nextLabel;

        private ByRefLiveness(MethodSymbol method)
        {
            _method = method;
        }

        public static void Check(FunctionContext context, BoundStatement body)
        {
            var walker = new ByRefLiveness(context.Method);
            walker.RewriteNode(body);
            if (walker._hasAwait && walker._tracked.Count != 0)
                walker.Analyze(context);
        }

        private static bool IsTracked(LocalSymbol local) => local.IsByRef || local.Type.IsRefLikeType;

        private void AddLocalStep(StepKind kind, LocalSymbol local)
        {
            if (!IsTracked(local))
                return;
            if (!_tracked.ContainsKey(local))
                _tracked.Add(local, _tracked.Count);
            _steps.Add(new Step(kind, local, null));
        }

        private LabelSymbol NewLabel() => LabelSymbol.CreateGenerated($"<liveness_{_nextLabel++}>", _method);

        private void MarkLabel(LabelSymbol label)
        {
            _labels[label] = _steps.Count;
            _steps.Add(default);
        }

        protected override BoundStatement RewriteStatement(BoundStatement node)
        {
            switch (node)
            {
                case BoundLabelStatement label:
                    MarkLabel(label.Label);
                    return node;
                case BoundGotoStatement jump:
                    _steps.Add(new Step(StepKind.Jump, null, jump.TargetLabel));
                    return node;
                case BoundConditionalGotoStatement jump:
                    RewriteExpression(jump.Condition);
                    _steps.Add(new Step(StepKind.ConditionalJump, null, jump.TargetLabel));
                    return node;
                case BoundReturnStatement ret:
                    if (ret.Expression is not null)
                        RewriteExpression(ret.Expression);
                    _steps.Add(new Step(StepKind.End, null, null));
                    return node;
                case BoundThrowStatement thrown:
                    if (thrown.ExpressionOpt is not null)
                        RewriteExpression(thrown.ExpressionOpt);
                    _steps.Add(new Step(StepKind.End, null, null));
                    return node;
                case BoundLocalDeclarationStatement declaration:
                    if (declaration.Initializer is not null)
                        RewriteExpression(declaration.Initializer);
                    AddLocalStep(StepKind.Define, declaration.Local);
                    return node;
                case BoundLocalFunctionStatement:
                    return node;
                case BoundTryStatement @try:
                    WalkTry(@try);
                    return node;
                default:
                    return base.RewriteStatement(node);
            }
        }

        private void WalkTry(BoundTryStatement node)
        {
            LabelSymbol after = NewLabel();
            int start = _steps.Count;
            RewriteStatement(node.TryBlock);
            int end = _steps.Count;
            _steps.Add(new Step(StepKind.Jump, null, after));

            foreach (BoundCatchBlock handler in node.CatchBlocks)
            {
                _handlers.Add((start, end, _steps.Count));
                _steps.Add(default);
                if (handler.ExceptionLocalOpt is not null)
                    AddLocalStep(StepKind.Define, handler.ExceptionLocalOpt);
                if (handler.FilterOpt is not null)
                    RewriteExpression(handler.FilterOpt);
                RewriteStatement(handler.Body);
                _steps.Add(new Step(StepKind.Jump, null, after));
            }

            MarkLabel(after);
            if (node.FinallyBlockOpt is not null)
            {
                _handlers.Add((start, _steps.Count, _steps.Count));
                RewriteStatement(node.FinallyBlockOpt);
            }
        }

        protected override BoundExpression RewriteExpressionCore(BoundExpression node)
        {
            switch (node)
            {
                case BoundLocalExpression local:
                    AddLocalStep(StepKind.Use, local.Local);
                    return node;
                case BoundAssignmentExpression assignment when assignment.Left is BoundLocalExpression left:
                    RewriteExpression(assignment.Right);
                    AddLocalStep(assignment.Right is BoundRefExpression || !left.Local.IsByRef ? StepKind.Define : StepKind.Use, left.Local);
                    return node;
                case BoundLambdaExpression:
                    return node;
                default:
                    return base.RewriteExpressionCore(node);
            }
        }

        protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
        {
            BoundExpression result = base.RewriteCallExpression(node);
            if (IsAwaitCall(node))
            {
                _hasAwait = true;
                _steps.Add(new Step(StepKind.Await, null, null));
            }

            return result;
        }

        private void Analyze(FunctionContext context)
        {
            var extraSuccessors = new Dictionary<int, List<int>>();
            foreach ((int start, int end, int handler) in _handlers)
            {
                for (int i = start; i < end; i++)
                {
                    if (!extraSuccessors.TryGetValue(i, out List<int>? list))
                        extraSuccessors.Add(i, list = new List<int>());
                    list.Add(handler);
                }
            }

            int count = _steps.Count;
            var liveIn = new BitArray[count + 1];
            for (int i = 0; i <= count; i++)
                liveIn[i] = new BitArray(_tracked.Count);

            var liveOut = new BitArray(_tracked.Count);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = count - 1; i >= 0; i--)
                {
                    ComputeLiveOut(i, liveIn, extraSuccessors, liveOut);
                    Step step = _steps[i];
                    if (step.Kind == StepKind.Define)
                        liveOut[_tracked[step.Local!]] = false;
                    else if (step.Kind == StepKind.Use)
                        liveOut[_tracked[step.Local!]] = true;

                    if (!SameBits(liveIn[i], liveOut))
                    {
                        liveIn[i] = new BitArray(liveOut);
                        changed = true;
                    }
                }
            }

            var reported = new bool[_tracked.Count];
            for (int i = 0; i < count; i++)
            {
                if (_steps[i].Kind != StepKind.Await)
                    continue;

                ComputeLiveOut(i, liveIn, extraSuccessors, liveOut);
                foreach ((LocalSymbol local, int index) in _tracked)
                {
                    if (liveOut[index] && !reported[index])
                    {
                        reported[index] = true;
                        context.ReportLiveAcrossAwait(local);
                    }
                }
            }
        }

        private void ComputeLiveOut(int index, BitArray[] liveIn, Dictionary<int, List<int>> extra, BitArray liveOut)
        {
            liveOut.SetAll(false);
            Step step = _steps[index];
            if (step.Kind != StepKind.Jump && step.Kind != StepKind.End)
                liveOut.Or(liveIn[index + 1]);
            if (step.Label is LabelSymbol label && _labels.TryGetValue(label, out int target))
                liveOut.Or(liveIn[target]);
            if (extra.TryGetValue(index, out List<int>? handlers))
            {
                foreach (int handler in handlers)
                    liveOut.Or(liveIn[handler]);
            }
        }

        private static bool SameBits(BitArray left, BitArray right)
        {
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }
    }
}
