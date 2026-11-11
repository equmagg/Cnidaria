using System;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Cs
{
    // Type syntax, blocks, constructor initializers, and conversion application
    internal sealed partial class LocalScopeBinder : Binder
    {
        private BoundExpression BindThis(ThisExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
            {
                diagnostics.Add(new Diagnostic("CN_THIS000", DiagnosticSeverity.Error,
                    "The 'this' keyword is only valid in instance members.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (IsInStaticMember(method))
            {
                diagnostics.Add(new Diagnostic("CN_THIS001", DiagnosticSeverity.Error,
                    "Cannot use 'this' in a static method.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (GetEnclosingType(method, out _) is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic("CN_THIS002", DiagnosticSeverity.Error,
                    "Cannot resolve containing type for 'this'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (ReportThisInStructFunction(node, context, diagnostics))
                return new BoundBadExpression(node);

            return new BoundThisExpression(node, containingType);
        }

        // Lambdas and local functions see the 'this' of the member around them
        private static bool IsInStaticMember(Symbol? symbol)
        {
            while (symbol is LambdaMethodSymbol or LocalFunctionSymbol)
                symbol = symbol.ContainingSymbol;

            return symbol switch
            {
                MethodSymbol method => method.IsStatic,
                FieldSymbol field => field.IsStatic,
                PropertySymbol property => property.IsStatic,
                _ => false,
            };
        }

        private static bool IsDeclaredStatic(Symbol symbol) => symbol switch
        {
            LambdaMethodSymbol lambda => lambda.IsDeclaredStatic,
            LocalFunctionSymbol function => HasModifier(function.Declaration.Modifiers, SyntaxKind.StaticKeyword),
            _ => false,
        };

        // A static function on the way cuts 'this' off, which StaticFunctionChecker reports if it is used anyway
        private static bool HasNoThis(Symbol? symbol)
        {
            for (; symbol is LambdaMethodSymbol or LocalFunctionSymbol; symbol = symbol.ContainingSymbol)
            {
                if (IsDeclaredStatic(symbol))
                    return true;
            }

            return IsInStaticMember(symbol);
        }

        private static bool ReportThisInStructFunction(SyntaxNode node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not (LambdaMethodSymbol or LocalFunctionSymbol) ||
                HasNoThis(context.ContainingSymbol) ||
                GetEnclosingType(context.ContainingSymbol, out _) is not NamedTypeSymbol enclosingType ||
                !enclosingType.IsValueType)
            {
                return false;
            }

            diagnostics.Add(new Diagnostic(
                "CN_THIS003",
                DiagnosticSeverity.Error,
                "Anonymous methods, lambda expressions, query expressions, and local functions inside structs cannot access instance members of 'this'. " +
                "Consider copying 'this' to a local variable outside the anonymous method, lambda expression, query expression, or local function and using the local instead.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            return true;
        }

        private BoundExpression BindBase(
            BaseExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BASE001",
                    DiagnosticSeverity.Error,
                    "The 'base' keyword is only valid in instance members.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (IsInStaticMember(method))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BASE002",
                    DiagnosticSeverity.Error,
                    "Cannot use 'base' in a static method.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (GetEnclosingType(method, out _) is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BASE003",
                    DiagnosticSeverity.Error,
                    "Cannot resolve containing type for 'base'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (containingType.TypeKind != TypeKind.Class)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BASE004",
                    DiagnosticSeverity.Error,
                    "The 'base' keyword is only valid in classes.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (containingType.BaseType is not NamedTypeSymbol baseType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BASE005",
                    DiagnosticSeverity.Error,
                    $"Type '{containingType.Name}' does not have a base type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            return new BoundBaseExpression(node, containingType, baseType);
        }
        /// <summary>Binds type syntax through the nearest type binder</summary>
        public override TypeSymbol BindType(TypeSyntax syntax, BindingContext context, DiagnosticBag diagnostics)
        {
            if (syntax is PointerTypeSyntax p)
            {
                if (!IsUnsafeContext(context.ContainingSymbol, Flags))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_UNSAFE_TYPE001",
                        DiagnosticSeverity.Warning,
                        "Pointer types may only be used in an unsafe context.",
                        new Location(context.SemanticModel.SyntaxTree, p.Span)));
                }

                var elem = BindType(p.ElementType, context, diagnostics);

                if (elem.IsReferenceType || elem is ArrayTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_PTRTYPE001",
                        DiagnosticSeverity.Warning,
                        $"This takes the address of, gets the size of, or declares a pointer to a managed type '{elem.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, p.ElementType.Span)));
                }

                return context.Compilation.CreatePointerType(elem);
            }

            if (Parent != null)
                return Parent.BindType(syntax, context, diagnostics);

            diagnostics.Add(new Diagnostic("CN_TYPE000", DiagnosticSeverity.Error,
                $"No parent binder to bind type: {syntax.Kind}",
                new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

            return new ErrorTypeSymbol("type", containing: null, ImmutableArray<Location>.Empty);
        }
        private BoundStatement BindBlock(BlockSyntax block, BindingContext context, DiagnosticBag diagnostics)
        {
            var scope = new LocalScopeBinder(parent: this, flags: Flags, containing: _containing);
            scope.PredeclareLocalFunctionsInBlock(block, context, diagnostics);

            var statements = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Count);
            LocalScopeBinder current = scope;

            for (int i = 0; i < block.Statements.Count; i++)
            {
                StatementSyntax statement = block.Statements[i];
                if (statement is LocalDeclarationStatementSyntax declaration && declaration.AwaitKeyword.Span.Length != 0)
                {
                    var following = ImmutableArray.CreateBuilder<StatementSyntax>(block.Statements.Count - i - 1);
                    for (int f = i + 1; f < block.Statements.Count; f++)
                        following.Add(block.Statements[f]);
                    statements.Add(current.BindAwaitUsingDeclaration(declaration, following.MoveToImmutable(), context, diagnostics));
                    break;
                }

                var bound = current.BindStatement(statement, context, diagnostics);
                statements.Add(bound);
                current = current.GetFlowScopeBinderForFollowingStatements(bound);
            }

            scope.DeclareLocalFunctionDelegates(statements, block);
            return new BoundBlockStatement(block, statements.ToImmutable());
        }
        /// <summary>Binds an explicit or implicit base or this constructor initializer</summary>
        internal BoundStatement? BindConstructorInitializer(
            ConstructorDeclarationSyntax ctorSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
            => BindConstructorInitializer(ctorSyntax.Initializer, context, diagnostics);

        internal BoundStatement? BindConstructorInitializer(
            ConstructorInitializerSyntax? initSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (initSyntax is null)
                return null;
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT000",
                    DiagnosticSeverity.Error,
                    "Constructor initializer can only appear in a constructor.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (method.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT001",
                    DiagnosticSeverity.Error,
                    "Static constructors cannot have a constructor initializer.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (method.ContainingSymbol is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT002",
                    DiagnosticSeverity.Error,
                    "Cannot resolve containing type for constructor initializer.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            bool isThisInitializer = initSyntax.ThisOrBaseKeyword.Kind == SyntaxKind.ThisKeyword;
            bool isBaseInitializer = initSyntax.ThisOrBaseKeyword.Kind == SyntaxKind.BaseKeyword;
            if (!isThisInitializer && !isBaseInitializer)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT003",
                    DiagnosticSeverity.Error,
                    "Constructor initializer must be this or base.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            NamedTypeSymbol targetType;
            if (isThisInitializer)
            {
                targetType = containingType;
            }
            else
            {
                if (containingType.TypeKind != TypeKind.Class)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CTORINIT004",
                        DiagnosticSeverity.Error,
                        "Only class constructors can use base constructor inializers.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                    var empty = new BoundEmptyStatement(initSyntax);
                    context.Recorder.RecordBound(initSyntax, empty);
                    return empty;
                }
                var baseType = containingType.BaseType as NamedTypeSymbol;
                if (baseType is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CTORINIT004",
                        DiagnosticSeverity.Error,
                        "Only class constructors can use base constructor inializers.",
                        new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                    var empty = new BoundEmptyStatement(initSyntax);
                    context.Recorder.RecordBound(initSyntax, empty);
                    return empty;
                }
                targetType = baseType;
            }
            var argSyntaxes = initSyntax.ArgumentList.Arguments;
            var argsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                argsBuilder.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            var boundArgs = argsBuilder.ToImmutable();
            var ctorCandidates = LookupConstructors(targetType)
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();
            // A struct has an implicit parameterless constructor that zeroes it
            if (isThisInitializer && containingType.TypeKind == TypeKind.Struct && boundArgs.IsEmpty &&
                !ctorCandidates.Any(c => c.Parameters.IsEmpty))
            {
                var thisTarget = new BoundThisExpression(new ThisExpressionSyntax(initSyntax.ThisOrBaseKeyword), containingType);
                var zero = new BoundObjectCreationExpression(initSyntax, containingType, constructorOpt: null, arguments: boundArgs);
                var zeroThis = new BoundExpressionStatement(initSyntax, new BoundAssignmentExpression(initSyntax, thisTarget, zero));
                context.Recorder.RecordBound(initSyntax, zeroThis);
                return zeroThis;
            }
            if (ctorCandidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT005",
                    DiagnosticSeverity.Error,
                    $"No accessible constructor found for '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }
            if (!TryResolveOverload(
                candidates: ctorCandidates,
                args: boundArgs,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: initSyntax))
            {
                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            if (isThisInitializer && ReferenceEquals(chosen, method))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CTORINIT006",
                    DiagnosticSeverity.Error,
                    "Constructor cannot delegate to itself.",
                    new Location(context.SemanticModel.SyntaxTree, initSyntax.Span)));

                var empty = new BoundEmptyStatement(initSyntax);
                context.Recorder.RecordBound(initSyntax, empty);
                return empty;
            }

            ExpressionSyntax receiverSyntax = isBaseInitializer
                ? new BaseExpressionSyntax(initSyntax.ThisOrBaseKeyword)
                : new ThisExpressionSyntax(initSyntax.ThisOrBaseKeyword);

            BoundExpression receiver = isBaseInitializer
                ? new BoundBaseExpression(receiverSyntax, containingType, targetType)
                : new BoundThisExpression(receiverSyntax, containingType);

            var call = new BoundCallExpression(initSyntax, receiver, chosen!, convertedArgs);
            var stmt = new BoundExpressionStatement(initSyntax, call);

            context.Recorder.RecordBound(initSyntax, stmt);
            return stmt;
        }
        /// <summary>Applies a classified conversion and lowers special target-typed forms</summary>
        internal BoundExpression ApplyConversion(
            ExpressionSyntax exprSyntax,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit)
        {
            if (IsTrueErrorType(targetType))
            {
                var converted = CreateErrorConversion(exprSyntax, expr, targetType);
                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            if (expr is BoundOutVarPendingExpression outVar)
            {
                var converted = MaterializeOutVarPending(
                    exprSyntax,
                    outVar,
                    targetType,
                    diagnosticNode,
                    context,
                    diagnostics);

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            if (expr is BoundOutDiscardExpression discard)
            {
                var converted = MaterializeOutDiscard(
                    exprSyntax,
                    discard,
                    targetType,
                    diagnosticNode,
                    context,
                    diagnostics);

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            // Target-typed method group
            if (expr is BoundMethodGroupExpression methodGroup)
            {
                var bound = BindMethodGroupConversion(methodGroup, targetType, diagnosticNode, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            if (expr is BoundFunctionPointerMethodGroupExpression functionPointerMethodGroup)
            {
                if (targetType is FunctionPointerTypeSymbol functionPointerType &&
                    TryResolveFunctionPointerMethodGroup(functionPointerMethodGroup, functionPointerType, context, out var method))
                {
                    var bound = new BoundFunctionPointerLoadExpression(
                        (PrefixUnaryExpressionSyntax)functionPointerMethodGroup.Syntax,
                        functionPointerType,
                        method!);
                    context.Recorder.RecordBound(exprSyntax, bound);
                    return bound;
                }

                diagnostics.Add(new Diagnostic(
                    "CN_FNPTR_CONV001",
                    DiagnosticSeverity.Error,
                    $"No overload is compatible with function pointer type '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);
                context.Recorder.RecordBound(exprSyntax, bad);
                return bad;
            }
            // Target-typed conditional
            if (expr is BoundUnboundConditionalExpression unboundConditional)
            {
                var conditionalSyntax = (ConditionalExpressionSyntax)unboundConditional.Syntax;
                var whenTrue = ApplyConversion(conditionalSyntax.WhenTrue, unboundConditional.WhenTrue, targetType, diagnosticNode, context, diagnostics, requireImplicit: true);
                var whenFalse = ApplyConversion(conditionalSyntax.WhenFalse, unboundConditional.WhenFalse, targetType, diagnosticNode, context, diagnostics, requireImplicit: true);
                BoundExpression bound = whenTrue.HasErrors || whenFalse.HasErrors
                    ? new BoundBadExpression(conditionalSyntax)
                    : new BoundConditionalExpression(conditionalSyntax, targetType, unboundConditional.Condition, whenTrue, whenFalse,
                        FoldConditionalConstant(unboundConditional.Condition, whenTrue, whenFalse));
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target-typed switch expression
            if (expr is BoundUnboundSwitchExpression unboundSwitch)
            {
                var bound = BindSwitchExpressionWithType(unboundSwitch, targetType, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target-typed anonymous function
            if (expr is BoundUnboundLambdaExpression unboundLambda)
            {
                var bound = BindLambdaConversion(unboundLambda, targetType, diagnosticNode, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target-typed object creation
            if (exprSyntax is ImplicitObjectCreationExpressionSyntax ioc)
            {
                BoundExpression bound;
                if (expr is BoundUnboundImplicitObjectCreationExpression unbound)
                    bound = BindImplicitObjectCreation(ioc, targetType, unbound.Arguments, context, diagnostics);
                else
                    bound = BindImplicitObjectCreation(ioc, targetType, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target-typed collection expression
            if (expr is BoundUnboundCollectionExpression unboundCollection && unboundCollection.Syntax is CollectionExpressionSyntax collectionSyntax)
            {
                var bound = BindCollectionExpression(collectionSyntax, targetType, unboundCollection, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, bound);
                return bound;
            }
            // Target-typed default literal
            if (expr.Type is DefaultLiteralTypeSymbol)
            {
                if (targetType.SpecialType == SpecialType.System_Void || targetType is ByRefTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CONV001",
                        DiagnosticSeverity.Error,
                        $"No {(requireImplicit ? "implicit " : "")}conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);
                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }

                var lowered = MakeDefaultValue(exprSyntax, targetType);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }

            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            if (expr is BoundThrowExpression te)
            {
                te.SetType(targetType);
                return te;
            }

            if (ShouldSuppressCascade(expr))
            {
                var converted = CreateErrorConversion(exprSyntax, expr, targetType);
                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            var conv = ClassifyConversion(expr, targetType, context);

            if (expr is BoundStackAllocArrayCreationExpression && conv.Kind != ConversionKind.ImplicitStackAlloc)
                EnsureUnsafe(exprSyntax, context, diagnostics);

            if (conv.Kind == ConversionKind.ImplicitStackAlloc)
            {
                if (expr is not BoundStackAllocArrayCreationExpression sa)
                    throw new InvalidOperationException("ImplicitStackAlloc conversion requires a stackalloc expression.");
                if (!TryGetSpanLikeElementType(targetType, out var spanLikeType, out var spanElemType) ||
                    !ReferenceEquals(sa.ElementType, spanElemType))
                {
                    diagnostics.Add(new Diagnostic(
                    "CN_CONV001",
                    DiagnosticSeverity.Error,
                    requireImplicit
                        ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                        : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(exprSyntax);
                    bad.SetType(targetType);

                    context.Recorder.RecordBound(exprSyntax, bad);
                    return bad;
                }
                var lowered = LowerStackAllocToSpanCreation(exprSyntax, sa, spanLikeType, diagnosticNode, context, diagnostics);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }
            if (!conv.Exists || (requireImplicit && !conv.IsImplicit))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CONV001",
                    DiagnosticSeverity.Error,
                    requireImplicit
                        ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                        : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);

                context.Recorder.RecordBound(exprSyntax, bad);
                return bad;
            }
            if (conv.Kind is ConversionKind.ImplicitNullable or ConversionKind.ExplicitNullable)
            {
                var lowered = LowerNullableConversion(
                    exprSyntax, expr, targetType, diagnosticNode, context, diagnostics, requireImplicit);
                context.Recorder.RecordBound(exprSyntax, lowered);
                return lowered;
            }
            if (conv.Kind == ConversionKind.UserDefined && conv.UserDefinedMethod is MethodSymbol userConv)
            {
                BoundExpression arg = expr;
                var paramType = userConv.Parameters[0].Type;

                if (!ReferenceEquals(arg.Type, paramType))
                {
                    var pre = ClassifyConversion(arg, paramType); // Exclude recursive user-defined lookup
                    if (!pre.Exists || (requireImplicit && !pre.IsImplicit))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CONV001",
                            DiagnosticSeverity.Error,
                            requireImplicit
                                ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                                : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                        var bad = new BoundBadExpression(exprSyntax);
                        bad.SetType(targetType);
                        context.Recorder.RecordBound(exprSyntax, bad);
                        return bad;
                    }

                    bool preChecked = IsCheckedOverflowContext && pre.Kind == ConversionKind.ExplicitNumeric;
                    arg = new BoundConversionExpression(exprSyntax, paramType, arg, pre, preChecked);
                }
                BoundExpression converted = new BoundCallExpression(
                    exprSyntax,
                    receiverOpt: null,
                    userConv,
                    ImmutableArray.Create(arg),
                    constrainedToTypeOpt: FindOperatorConstraintOwner(userConv, expr.Type, targetType));

                if (!ReferenceEquals(converted.Type, targetType))
                {
                    var post = ClassifyConversion(converted, targetType); // Exclude recursive user-defined lookup
                    if (!post.Exists || (requireImplicit && !post.IsImplicit))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CONV001",
                            DiagnosticSeverity.Error,
                            requireImplicit
                                ? $"No implicit conversion from '{expr.Type.Name}' to '{targetType.Name}'."
                                : $"No conversion from '{expr.Type.Name}' to '{targetType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                        var bad = new BoundBadExpression(exprSyntax);
                        bad.SetType(targetType);
                        context.Recorder.RecordBound(exprSyntax, bad);
                        return bad;
                    }

                    bool postChecked = IsCheckedOverflowContext && post.Kind == ConversionKind.ExplicitNumeric;
                    converted = new BoundConversionExpression(exprSyntax, targetType, converted, post, postChecked);
                }

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
            {
                bool isChecked = IsCheckedOverflowContext && conv.Kind == ConversionKind.ExplicitNumeric;
                var converted = new BoundConversionExpression(exprSyntax, targetType, expr, conv, isChecked);

                context.Recorder.RecordBound(exprSyntax, converted);
                return converted;
            }
        }
        private BoundExpression MaterializeOutVarPending(
            ExpressionSyntax exprSyntax,
            BoundOutVarPendingExpression outVar,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (targetType is not ByRefTypeSymbol targetByRef)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTVAR001",
                    DiagnosticSeverity.Error,
                    "An out variable declaration can only be converted to a by-ref parameter type.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);
                return bad;
            }

            if (IsNameDeclaredInEnclosingScopes(outVar.Name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL002",
                    DiagnosticSeverity.Error,
                    $"A local named '{outVar.Name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, outVar.Designation.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);
                return bad;
            }

            var elementType = targetByRef.ElementType;

            if (outVar.IsScoped && !elementType.IsRefLikeType && elementType is not ErrorTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_SCOPED001",
                    DiagnosticSeverity.Error,
                    "The scoped modifier can only be used with ref locals or ref-like value types.",
                    new Location(context.SemanticModel.SyntaxTree, outVar.Syntax.Span)));
            }

            var local = new LocalSymbol(
                name: outVar.Name,
                containing: _containing,
                type: elementType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, outVar.Designation.Span)),
                isConst: false,
                constantValueOpt: Optional<object>.None,
                isByRef: false,
                isScoped: outVar.IsScoped);

            ExpressionVariableScope()._locals[outVar.Name] = local;
            context.Recorder.RecordDeclared(outVar.Designation, local);

            return new BoundRefExpression(
                exprSyntax,
                targetType,
                new BoundLocalExpression(exprSyntax, local));
        }
        private BoundExpression MaterializeOutDiscard(
            ExpressionSyntax exprSyntax,
            BoundOutDiscardExpression discard,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (targetType is not ByRefTypeSymbol targetByRef)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDISCARD001",
                    DiagnosticSeverity.Error,
                    "An out discard can only be converted to a by-ref parameter type.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);
                return bad;
            }

            var elementType = targetByRef.ElementType;

            if (discard.ExplicitElementTypeOpt is not null &&
                !AreSameType(discard.ExplicitElementTypeOpt, elementType))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDISCARD002",
                    DiagnosticSeverity.Error,
                    $"Cannot pass discard of type '{discard.ExplicitElementTypeOpt.Name}' to an out parameter of type '{elementType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, exprSyntax.Span)));

                var bad = new BoundBadExpression(exprSyntax);
                bad.SetType(targetType);
                return bad;
            }

            var temp = NewTemp("$out_discard", elementType);
            var tempRef = new BoundRefExpression(
                exprSyntax,
                targetType,
                new BoundLocalExpression(exprSyntax, temp));

            return new BoundSequenceExpression(
                exprSyntax,
                locals: ImmutableArray.Create(temp),
                sideEffects: ImmutableArray<BoundStatement>.Empty,
                value: tempRef);
        }
        // Nullable conversion lowering preserves source evaluation and HasValue branching
        private BoundExpression LowerNullableConversion(
            ExpressionSyntax exprSyntax,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics,
            bool requireImplicit)
        {
            if (!TryGetSystemNullableInfo(targetType, out var targetNullable, out var targetUnderlying))
                return new BoundBadExpression(exprSyntax);

            // Nullable<S> to Nullable<T>
            if (TryGetSystemNullableInfo(expr.Type, out var fromNullable, out var fromUnderlying))
            {
                var hasValueGet = FindNullableHasValueGetter(fromNullable);
                var gv = FindNullableGetValueOrDefault(fromNullable, fromUnderlying);

                if (hasValueGet is null || gv is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_NULLCONV000",
                        DiagnosticSeverity.Error,
                        "Missing Nullable<T> members (HasValue / GetValueOrDefault).",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                    return new BoundBadExpression(exprSyntax);
                }

                var srcTmp = NewNullableTemp(expr.Type);
                var srcDecl = new BoundLocalDeclarationStatement(diagnosticNode, srcTmp, expr);
                var src = new BoundLocalExpression(diagnosticNode, srcTmp);

                var cond = new BoundCallExpression(diagnosticNode, src, hasValueGet, ImmutableArray<BoundExpression>.Empty);

                var srcVal = new BoundCallExpression(diagnosticNode, src, gv, ImmutableArray<BoundExpression>.Empty);

                // Convert the underlying value
                var convertedUnderlying = ApplyConversion(
                    exprSyntax: exprSyntax,
                    expr: srcVal,
                    targetType: targetUnderlying,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: requireImplicit);

                var whenTrue = WrapIntoNullable(diagnosticNode, targetNullable, targetUnderlying, convertedUnderlying);
                var whenFalse = MakeDefaultValue(diagnosticNode, targetType);

                var condSyntax = new ConditionalExpressionSyntax(
                    condition: exprSyntax,
                    questionToken: default,
                    whenTrue: exprSyntax,
                    colonToken: default,
                    whenFalse: exprSyntax);

                var value = new BoundConditionalExpression(condSyntax, targetType, cond, whenTrue, whenFalse, Optional<object>.None);

                return new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(srcTmp),
                    sideEffects: ImmutableArray.Create<BoundStatement>(srcDecl),
                    value: value);
            }

            // S to Nullable<T>
            {
                var srcTmp = NewNullableTemp(expr.Type);
                var srcDecl = new BoundLocalDeclarationStatement(diagnosticNode, srcTmp, expr);
                var src = new BoundLocalExpression(diagnosticNode, srcTmp);

                var convertedUnderlying = ApplyConversion(
                    exprSyntax: exprSyntax,
                    expr: src,
                    targetType: targetUnderlying,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: requireImplicit);

                var wrapped = WrapIntoNullable(diagnosticNode, targetNullable, targetUnderlying, convertedUnderlying);

                return new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(srcTmp),
                    sideEffects: ImmutableArray.Create<BoundStatement>(srcDecl),
                    value: wrapped);
            }
        }
        private BoundExpression WrapIntoNullable(SyntaxNode syntax, NamedTypeSymbol nullableType, TypeSymbol underlying, BoundExpression value)
        {
            var ctor = FindNullableCtor(nullableType, underlying);
            if (ctor is null)
                return new BoundBadExpression(syntax);

            var tmp = NewNullableTemp(nullableType);
            var decl = new BoundLocalDeclarationStatement(syntax, tmp, initializer: null);

            var recv = new BoundLocalExpression(syntax, tmp);
            var call = new BoundCallExpression(syntax, recv, ctor, ImmutableArray.Create(value));
            var callStmt = new BoundExpressionStatement(syntax, call);

            return new BoundSequenceExpression(
                syntax,
                locals: ImmutableArray.Create(tmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(decl, callStmt),
                value: new BoundLocalExpression(syntax, tmp));
        }
        private static MethodSymbol? FindNullableHasValueGetter(NamedTypeSymbol nullableType)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is PropertySymbol ps && ps.Name == "HasValue")
                    return ps.GetMethod;
            }
            return null;
        }
        private BoundExpression MakeDefaultValue(SyntaxNode syntax, TypeSymbol type)
        {
            var tmp = NewNullableTemp(type);
            var decl = new BoundLocalDeclarationStatement(syntax, tmp, initializer: null);
            var value = new BoundLocalExpression(syntax, tmp);
            return new BoundSequenceExpression(
                syntax,
                locals: ImmutableArray.Create(tmp),
                sideEffects: ImmutableArray.Create<BoundStatement>(decl),
                value: value);
        }
        private LocalSymbol NewNullableTemp(TypeSymbol type)
        {
            var name = $"$nullable_tmp{_tempId++}";
            return new LocalSymbol(name, _containing, type, ImmutableArray<Location>.Empty);
        }

        private static MethodSymbol? FindNullableCtor(NamedTypeSymbol nullableType, TypeSymbol underlying)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic && ms.Parameters.Length == 1)
                {
                    if (ReferenceEquals(ms.Parameters[0].Type, underlying))
                        return ms;
                }
            }
            return null;
        }
        private static MethodSymbol? FindNullableGetValueOrDefault(NamedTypeSymbol nullableType, TypeSymbol underlying)
        {
            foreach (var m in nullableType.GetMembers())
            {
                if (m is MethodSymbol ms && !ms.IsStatic
                    && ms.Name == "GetValueOrDefault"
                    && ms.Parameters.Length == 0
                    && ReferenceEquals(ms.ReturnType, underlying))
                {
                    return ms;
                }
            }
            return null;
        }
    }
}
