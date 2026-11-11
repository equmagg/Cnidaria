using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    // Runtime async: an async method keeps its Task signature, returns the unwrapped result and awaits through AsyncHelpers
    internal sealed partial class LocalScopeBinder : Binder
    {
        private static readonly string[] CompilerServicesNamespace = { "System", "Runtime", "CompilerServices" };

        // A null result type stands for the non-generic Task and ValueTask
        internal static bool TryGetTaskResultType(TypeSymbol type, out TypeSymbol? resultType)
        {
            resultType = null;
            if (type is not NamedTypeSymbol named || named.OriginalDefinition.Name is not ("Task" or "ValueTask"))
                return false;
            if (named.ContainingSymbol is not NamespaceSymbol tasks || tasks.Name != "Tasks" ||
                tasks.ContainingSymbol is not NamespaceSymbol threading || threading.Name != "Threading" ||
                threading.ContainingSymbol is not NamespaceSymbol system || system.Name != "System")
            {
                return false;
            }

            if (named.Arity == 1)
                resultType = named.TypeArguments[0];
            return named.Arity <= 1;
        }

        // What a 'return' produces in the body of this method
        internal static TypeSymbol GetBodyReturnType(MethodSymbol method, Compilation compilation)
        {
            if (!method.IsAsync || !TryGetTaskResultType(method.ReturnType, out TypeSymbol? resultType))
                return method.ReturnType;

            return resultType ?? compilation.GetSpecialType(SpecialType.System_Void);
        }

        // Runtime async has no form for these, Roslyn lowers them to state machines
        internal static void ValidateAsyncSignature(MethodSymbol method, SyntaxNode syntax, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!method.IsAsync)
                return;

            if (!TryGetTaskResultType(method.ReturnType, out _))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASYNC001",
                    DiagnosticSeverity.Error,
                    "An async method must return Task, Task<T>, ValueTask or ValueTask<T>.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
            }

            foreach (ParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind != ParameterRefKind.None || parameter.Type is ByRefTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ASYNC002",
                        DiagnosticSeverity.Error,
                        "Async methods cannot have ref, in or out parameters.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                }
                else if (parameter.Type.IsRefLikeType)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ASYNC003",
                        DiagnosticSeverity.Error,
                        $"Parameters of an async method cannot have the by-reference-like type '{parameter.Type.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                }
            }
        }

        private BoundExpression BindAwait(AwaitExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundExpression operand = BindExpression(node.Expression, context, diagnostics);
            if (operand.HasErrors)
                return new BoundBadExpression(node);

            if (!IsAwaitAllowed(node, context, diagnostics))
                return new BoundBadExpression(node);

            return BindAwaitOf(node, node.Expression, operand, context, diagnostics);
        }

        private static void MarkTopLevelAsyncIfNeeded(BindingContext context)
        {
            if (context.ContainingSymbol is not SourceMethodSymbol entry || entry.IsAsync || !ReferenceEquals(entry, context.Compilation.EntryPoint))
                return;

            bool returnsValue = entry.ReturnType.SpecialType != SpecialType.System_Void;
            NamedTypeSymbol? task = GetWellKnownType(context.Compilation, new[] { "System", "Threading", "Tasks" }, "Task", returnsValue ? 1 : 0);
            if (task is not null)
                entry.MarkTopLevelAsync(returnsValue ? context.Compilation.ConstructNamedType(task, ImmutableArray.Create(entry.ReturnType)) : task);
        }

        private bool IsAwaitAllowed(SyntaxNode node, BindingContext context, DiagnosticBag diagnostics)
        {
            MarkTopLevelAsyncIfNeeded(context);
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsAsync)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AWAIT001",
                    DiagnosticSeverity.Error,
                    "The 'await' operator can only be used within an async method.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return false;
            }

            for (Binder? binder = this; binder is LocalScopeBinder scope && ReferenceEquals(scope._containing, method); binder = scope.Parent)
            {
                if (scope._inCatchFilter)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_AWAIT003",
                        DiagnosticSeverity.Error,
                        "Cannot await in the filter expression of a catch clause.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return false;
                }

                if (scope._inLockBody)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_AWAIT004",
                        DiagnosticSeverity.Error,
                        "Cannot await in the body of a lock statement.",
                        new Location(context.SemanticModel.SyntaxTree, node.Span)));
                    return false;
                }
            }

            return true;
        }

        private BoundExpression BindAwaitOf(
            SyntaxNode node,
            ExpressionSyntax operandSyntax,
            BoundExpression operand,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            NamedTypeSymbol? helpers = GetWellKnownType(context.Compilation, CompilerServicesNamespace, "AsyncHelpers", 0);
            if (helpers is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AWAIT000",
                    DiagnosticSeverity.Error,
                    "Required type 'System.Runtime.CompilerServices.AsyncHelpers' was not found.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            // Task, ValueTask and their configured awaitables have a helper of their own
            if (FindAwaitHelper(helpers, operand.Type, context) is MethodSymbol helper)
                return new BoundCallExpression(node, receiverOpt: null, helper, ImmutableArray.Create(operand));

            return BindAwaiterPattern(node, operandSyntax, operand, helpers, context, diagnostics);
        }

        private static MethodSymbol? FindAwaitHelper(NamedTypeSymbol helpers, TypeSymbol operandType, BindingContext context)
        {
            foreach (Symbol member in helpers.GetMembers())
            {
                if (member is not MethodSymbol method || method.Name != "Await" || method.Parameters.Length != 1)
                    continue;

                TypeSymbol parameterType = method.Parameters[0].Type;
                if (method.TypeParameters.IsDefaultOrEmpty)
                {
                    if (AreSameType(parameterType, operandType))
                        return method;
                    continue;
                }

                if (method.TypeParameters.Length == 1 &&
                    operandType is NamedTypeSymbol operand && operand.Arity == 1 &&
                    parameterType is NamedTypeSymbol parameter &&
                    ReferenceEquals(operand.OriginalDefinition, parameter.OriginalDefinition))
                {
                    return new ConstructedMethodSymbol(method, operand.TypeArguments, context.Compilation.TypeManager);
                }
            }

            return null;
        }

        // awaiter = e.GetAwaiter(); if (!awaiter.IsCompleted) AsyncHelpers.[Unsafe]AwaitAwaiter(awaiter); awaiter.GetResult()
        private BoundExpression BindAwaiterPattern(
            SyntaxNode node,
            ExpressionSyntax operandSyntax,
            BoundExpression operand,
            NamedTypeSymbol helpers,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            TextSpan span = operandSyntax.Span;
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            BindingContext probeContext = WithRecorder(context, NullBindingRecorder.Instance);

            LocalSymbol awaitable = NewTemp("$awaitable", operand.Type);
            scope.ImportFlowingLocal(awaitable);
            var getAwaiterDiagnostics = new DiagnosticBag();
            BoundExpression getAwaiter = scope.BindExpression(
                MakeMemberCallSyntax(MakeIdentifierName(awaitable.Name, span), "GetAwaiter", span),
                probeContext,
                getAwaiterDiagnostics);
            if (getAwaiter.HasErrors || getAwaiter.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AWAIT002",
                    DiagnosticSeverity.Error,
                    $"'{operand.Type.Name}' does not contain a suitable definition for 'GetAwaiter'.",
                    new Location(context.SemanticModel.SyntaxTree, span)));
                return new BoundBadExpression(node);
            }

            LocalSymbol awaiter = NewTemp("$awaiter", getAwaiter.Type);
            scope.ImportFlowingLocal(awaiter);
            IdentifierNameSyntax awaiterName = MakeIdentifierName(awaiter.Name, span);
            var awaiterDiagnostics = new DiagnosticBag();
            BoundExpression isCompleted = scope.BindExpression(MakeMemberSyntax(awaiterName, "IsCompleted", span), probeContext, awaiterDiagnostics);
            BoundExpression getResult = scope.BindExpression(MakeMemberCallSyntax(awaiterName, "GetResult", span), probeContext, awaiterDiagnostics);

            NamedTypeSymbol? notifyCompletion = GetWellKnownType(context.Compilation, CompilerServicesNamespace, "INotifyCompletion", 0);
            NamedTypeSymbol? criticalNotifyCompletion = GetWellKnownType(context.Compilation, CompilerServicesNamespace, "ICriticalNotifyCompletion", 0);
            bool isCritical = criticalNotifyCompletion is not null && IsOrImplements(getAwaiter.Type, criticalNotifyCompletion);
            if (isCompleted.HasErrors || getResult.HasErrors ||
                isCompleted.Type.SpecialType != SpecialType.System_Boolean ||
                notifyCompletion is null || !IsOrImplements(getAwaiter.Type, notifyCompletion))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AWAIT005",
                    DiagnosticSeverity.Error,
                    $"'{getAwaiter.Type.Name}' is not an awaiter: it needs IsCompleted, GetResult() and INotifyCompletion.",
                    new Location(context.SemanticModel.SyntaxTree, span)));
                return new BoundBadExpression(node);
            }

            MethodSymbol? suspend = null;
            string suspendName = isCritical ? "UnsafeAwaitAwaiter" : "AwaitAwaiter";
            foreach (Symbol member in helpers.GetMembers())
            {
                if (member is MethodSymbol method && method.Name == suspendName && method.TypeParameters.Length == 1)
                    suspend = new ConstructedMethodSymbol(method, ImmutableArray.Create(getAwaiter.Type), context.Compilation.TypeManager);
            }
            if (suspend is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_AWAIT000",
                    DiagnosticSeverity.Error,
                    $"Required method 'AsyncHelpers.{suspendName}' was not found.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            TypeSymbol boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            var notCompleted = new BoundUnaryExpression(node, BoundUnaryOperatorKind.LogicalNot, boolType, isCompleted, Optional<object>.None);
            var suspendCall = new BoundCallExpression(node, receiverOpt: null, suspend, ImmutableArray.Create<BoundExpression>(new BoundLocalExpression(node, awaiter)));

            return new BoundSequenceExpression(
                node,
                locals: ImmutableArray.Create(awaitable, awaiter),
                sideEffects: ImmutableArray.Create<BoundStatement>(
                    new BoundLocalDeclarationStatement(node, awaitable, operand),
                    new BoundLocalDeclarationStatement(node, awaiter, getAwaiter),
                    new BoundIfStatement(node, notCompleted, new BoundExpressionStatement(node, suspendCall), elseOpt: null)),
                value: getResult);
        }

        private static bool IsOrImplements(TypeSymbol type, NamedTypeSymbol contract)
            => AreSameType(type, contract) || ImplementsInterface(type, contract) ||
               (type is TypeParameterSymbol parameter && GenericConstraintFacts.SatisfiesTypeConstraint(parameter, contract));

        private static MemberAccessExpressionSyntax MakeMemberSyntax(ExpressionSyntax receiver, string name, TextSpan span)
            => new MemberAccessExpressionSyntax(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                MakeToken(SyntaxKind.DotToken, span),
                MakeIdentifierName(name, span));

        private static InvocationExpressionSyntax MakeMemberCallSyntax(ExpressionSyntax receiver, string name, TextSpan span)
            => new InvocationExpressionSyntax(
                MakeMemberSyntax(receiver, name, span),
                new ArgumentListSyntax(
                    MakeToken(SyntaxKind.OpenParenToken, span),
                    SeparatedSyntaxList<ArgumentSyntax>.Empty,
                    MakeToken(SyntaxKind.CloseParenToken, span)));

        // await resource.DisposeAsync(), for 'await using' and 'await foreach'
        private BoundExpression BindDisposeAsync(
            SyntaxNode node,
            LocalSymbol resource,
            TextSpan span,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            scope.ImportFlowingLocal(resource);
            InvocationExpressionSyntax callSyntax = MakeMemberCallSyntax(MakeIdentifierName(resource.Name, span), "DisposeAsync", span);
            BindingContext probeContext = WithRecorder(context, NullBindingRecorder.Instance);
            BoundExpression dispose = scope.BindExpression(callSyntax, probeContext, new DiagnosticBag());
            if (dispose.HasErrors &&
                GetWellKnownType(context.Compilation, new[] { "System" }, "IAsyncDisposable", 0) is NamedTypeSymbol asyncDisposable)
            {
                BoundExpression receiver = scope.ApplyConversion(
                    MakeIdentifierName(resource.Name, span),
                    new BoundLocalExpression(node, resource),
                    asyncDisposable,
                    node,
                    probeContext,
                    new DiagnosticBag(),
                    requireImplicit: true);
                if (!receiver.HasErrors)
                {
                    foreach (Symbol member in asyncDisposable.GetMembers())
                    {
                        if (member is MethodSymbol method && method.Name == "DisposeAsync")
                            dispose = new BoundCallExpression(node, receiver, method, ImmutableArray<BoundExpression>.Empty);
                    }
                }
            }

            if (dispose.HasErrors)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING_AWAIT001",
                    DiagnosticSeverity.Error,
                    $"'{resource.Type.Name}': type used in an asynchronous using statement must implement 'System.IAsyncDisposable' or have a suitable 'DisposeAsync' method.",
                    new Location(context.SemanticModel.SyntaxTree, span)));
                return new BoundBadExpression(node);
            }

            return scope.BindAwaitOf(node, callSyntax, dispose, context, diagnostics);
        }

        // try { body } finally { if (resource != null) await resource.DisposeAsync(); }
        private BoundStatement MakeAwaitedDisposal(
            SyntaxNode node,
            LocalSymbol resource,
            BoundStatement body,
            TextSpan span,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            BoundExpression dispose = BindDisposeAsync(node, resource, span, context, diagnostics);
            BoundStatement disposeStatement = new BoundExpressionStatement(node, dispose);
            if (resource.Type.IsReferenceType)
            {
                var notNull = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.NotEquals,
                    context.Compilation.GetSpecialType(SpecialType.System_Boolean),
                    new BoundLocalExpression(node, resource),
                    new BoundLiteralExpression(node, NullTypeSymbol.Instance, null),
                    Optional<object>.None);
                disposeStatement = new BoundIfStatement(node, notNull, disposeStatement, elseOpt: null);
            }

            BoundBlockStatement tryBlock = body as BoundBlockStatement ?? new BoundBlockStatement(body.Syntax, ImmutableArray.Create(body));
            return new BoundTryStatement(
                CreateSyntheticTryStatementSyntax(),
                tryBlock,
                ImmutableArray<BoundCatchBlock>.Empty,
                new BoundBlockStatement(node, ImmutableArray.Create(disposeStatement)));
        }

        private BoundStatement BindAwaitUsingStatement(UsingStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!IsAwaitAllowed(node, context, diagnostics))
                return new BoundBadStatement(node);

            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing) { _awaitUsingScope = true };
            if (node.Declaration is not null)
            {
                ImmutableArray<BoundLocalDeclarationStatement> declarations = scope.BindUsingResourceDeclaration(node, node.Declaration, context, diagnostics);
                BoundStatement current = scope.BindStatement(node.Statement, context, diagnostics);
                for (int i = declarations.Length - 1; i >= 0; i--)
                    current = scope.WrapAwaitedDeclaration(node, declarations[i], current, node.Declaration.Span, context, diagnostics);
                return current;
            }

            if (node.Expression is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_USING000",
                    DiagnosticSeverity.Error,
                    "A using statement requires a resource expression or resource declaration.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            BoundExpression expression = BindExpression(node.Expression, context, diagnostics);
            if (expression.HasErrors)
                return new BoundBadStatement(node);

            BoundStatement body = scope.BindStatement(node.Statement, context, diagnostics);
            LocalSymbol resource = NewTemp("$asyncResource", expression.Type);
            return new BoundBlockStatement(
                node,
                ImmutableArray.Create<BoundStatement>(
                    new BoundLocalDeclarationStatement(node, resource, expression),
                    MakeAwaitedDisposal(node, resource, body, node.Expression.Span, context, diagnostics)));
        }

        private BoundStatement WrapAwaitedDeclaration(
            SyntaxNode node,
            BoundLocalDeclarationStatement declaration,
            BoundStatement body,
            TextSpan span,
            BindingContext context,
            DiagnosticBag diagnostics)
            => new BoundBlockStatement(
                node,
                ImmutableArray.Create<BoundStatement>(
                    new BoundLocalDeclarationStatement(declaration.Syntax, declaration.Local, declaration.Initializer),
                    MakeAwaitedDisposal(node, declaration.Local, body, span, context, diagnostics)));

        // 'await using var x = e;' protects the rest of its block
        private BoundStatement BindAwaitUsingDeclaration(
            LocalDeclarationStatementSyntax node,
            ImmutableArray<StatementSyntax> following,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (!IsAwaitAllowed(node, context, diagnostics))
                return new BoundBadStatement(node);

            _awaitUsingScope = true;
            BoundStatement bound = BindLocalDeclaration(node, context, diagnostics);
            _awaitUsingScope = false;

            var statements = ImmutableArray.CreateBuilder<BoundStatement>(following.Length);
            LocalScopeBinder current = this;
            foreach (StatementSyntax statement in following)
            {
                BoundStatement boundStatement = current.BindStatement(statement, context, diagnostics);
                statements.Add(boundStatement);
                current = current.GetFlowScopeBinderForFollowingStatements(boundStatement);
            }

            BoundStatement body = new BoundBlockStatement(node, statements.ToImmutable());
            TextSpan span = node.Declaration.Span;
            if (bound is BoundLocalDeclarationStatement single)
                return WrapAwaitedDeclaration(node, single, body, span, context, diagnostics);
            if (bound is BoundStatementList list)
            {
                for (int i = list.Statements.Length - 1; i >= 0; i--)
                    body = WrapAwaitedDeclaration(node, (BoundLocalDeclarationStatement)list.Statements[i], body, span, context, diagnostics);
                return body;
            }

            return bound;
        }

        internal static TryStatementSyntax CreateSyntheticTryStatementSyntax()
        {
            var emptyBlock = new BlockSyntax(default, SyntaxList<StatementSyntax>.Empty, default);
            return new TryStatementSyntax(default, emptyBlock, SyntaxList<CatchClauseSyntax>.Empty, new FinallyClauseSyntax(default, emptyBlock));
        }

        private BoundStatement BindAwaitForEach(ForEachStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!IsAwaitAllowed(node, context, diagnostics))
                return new BoundBadStatement(node);

            BoundExpression collection = BindExpression(node.Expression, context, diagnostics);
            if (collection.HasErrors)
                return new BoundBadStatement(node);

            TextSpan span = node.Expression.Span;
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            BindingContext probeContext = WithRecorder(context, NullBindingRecorder.Instance);

            LocalSymbol source = NewTemp("$asyncSource", collection.Type);
            scope.ImportFlowingLocal(source);
            BoundExpression getEnumerator = scope.BindExpression(
                MakeMemberCallSyntax(MakeIdentifierName(source.Name, span), "GetAsyncEnumerator", span),
                probeContext,
                new DiagnosticBag());
            if (getEnumerator.HasErrors || getEnumerator.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH_AWAIT002",
                    DiagnosticSeverity.Error,
                    $"Asynchronous foreach statement cannot operate on variables of type '{collection.Type.Name}' because it does not contain a suitable public instance definition for 'GetAsyncEnumerator'.",
                    new Location(context.SemanticModel.SyntaxTree, span)));
                return new BoundBadStatement(node);
            }

            LocalSymbol enumerator = NewTemp("$asyncEnumerator", getEnumerator.Type);
            scope.ImportFlowingLocal(enumerator);
            IdentifierNameSyntax enumeratorName = MakeIdentifierName(enumerator.Name, span);
            InvocationExpressionSyntax moveNextSyntax = MakeMemberCallSyntax(enumeratorName, "MoveNextAsync", span);
            BoundExpression moveNext = scope.BindExpression(moveNextSyntax, probeContext, new DiagnosticBag());
            BoundExpression current = scope.BindExpression(MakeMemberSyntax(enumeratorName, "Current", span), probeContext, new DiagnosticBag());
            if (moveNext.HasErrors || current.HasErrors)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FOREACH_AWAIT003",
                    DiagnosticSeverity.Error,
                    $"'{enumerator.Type.Name}' needs a 'MoveNextAsync' method and a 'Current' property to be an asynchronous enumerator.",
                    new Location(context.SemanticModel.SyntaxTree, span)));
                return new BoundBadStatement(node);
            }

            TypeSymbol boolType = context.Compilation.GetSpecialType(SpecialType.System_Boolean);
            BoundExpression condition = scope.ApplyConversion(
                moveNextSyntax,
                scope.BindAwaitOf(node, moveNextSyntax, moveNext, context, diagnostics),
                boolType,
                node,
                context,
                diagnostics,
                requireImplicit: true);

            string name = node.Identifier.ValueText ?? string.Empty;
            var iterationScope = new LocalScopeBinder(parent: scope, flags: Flags, containing: _containing);
            if (iterationScope.IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LOCAL001",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)));
            }

            TypeSymbol iterationType = IsVar(node.Type) ? current.Type : iterationScope.BindType(node.Type, context, diagnostics);
            var iterationLocal = new LocalSymbol(
                name: name,
                containing: _containing,
                type: iterationType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)),
                isReadOnly: true);
            iterationScope.DeclareLocal(iterationLocal, node, context);
            BoundExpression initial = iterationScope.ApplyConversion(node.Expression, current, iterationType, node, context, diagnostics, requireImplicit: false);

            LabelSymbol breakLabel = _flow.NewGeneratedLabel("break");
            LabelSymbol continueLabel = _flow.NewGeneratedLabel("continue");
            _flow.PushLoop(breakLabel, continueLabel);
            BoundStatement body;
            try
            {
                body = iterationScope.BindStatement(node.Statement, context, diagnostics);
            }
            finally
            {
                _flow.PopLoop();
            }

            // continue: if (!await e.MoveNextAsync()) goto break; x = e.Current; body; goto continue; break:
            var notMoved = new BoundConditionalGotoStatement(node, condition, breakLabel, jumpIfTrue: false);
            var loop = new BoundBlockStatement(
                node,
                ImmutableArray.Create<BoundStatement>(
                    new BoundLabelStatement(node, continueLabel),
                    notMoved,
                    new BoundBlockStatement(
                        node,
                        ImmutableArray.Create<BoundStatement>(new BoundLocalDeclarationStatement(node, iterationLocal, initial), body)),
                    new BoundGotoStatement(node, continueLabel),
                    new BoundLabelStatement(node, breakLabel)));

            BoundStatement protectedLoop = MakeAwaitedDisposal(node, enumerator, loop, span, context, diagnostics);
            return new BoundBlockStatement(
                node,
                ImmutableArray.Create<BoundStatement>(
                    new BoundLocalDeclarationStatement(node, source, collection),
                    new BoundLocalDeclarationStatement(node, enumerator, getEnumerator),
                    protectedLoop));
        }
    }
}
