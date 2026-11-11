using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Cnidaria.Cs
{
    // Anonymous function and method group conversion
    internal sealed partial class LocalScopeBinder : Binder
    {
        private static bool TryGetAnonymousFunctionParts(
            ExpressionSyntax syntax,
            out ImmutableArray<ParameterSyntax> parameters,
            out bool hasExplicitParameterList,
            out SyntaxNode body,
            out TypeSyntax? explicitReturnType,
            out bool isStatic,
            out bool isAsync)
        {
            switch (syntax)
            {
                case SimpleLambdaExpressionSyntax simple:
                    parameters = ImmutableArray.Create(simple.Parameter);
                    hasExplicitParameterList = true;
                    body = simple.Body;
                    explicitReturnType = null;
                    isStatic = HasAnonymousFunctionModifier(simple.Modifiers, SyntaxKind.StaticKeyword);
                    isAsync = HasAnonymousFunctionModifier(simple.Modifiers, SyntaxKind.AsyncKeyword);
                    return true;

                case ParenthesizedLambdaExpressionSyntax parenthesized:
                    parameters = parenthesized.ParameterList.Parameters.ToImmutableArray();
                    hasExplicitParameterList = true;
                    body = parenthesized.Body;
                    explicitReturnType = parenthesized.ReturnType;
                    isStatic = HasAnonymousFunctionModifier(parenthesized.Modifiers, SyntaxKind.StaticKeyword);
                    isAsync = HasAnonymousFunctionModifier(parenthesized.Modifiers, SyntaxKind.AsyncKeyword);
                    return true;

                case AnonymousMethodExpressionSyntax anonymous:
                    parameters = anonymous.ParameterList?.Parameters.ToImmutableArray() ?? ImmutableArray<ParameterSyntax>.Empty;
                    hasExplicitParameterList = anonymous.ParameterList is not null;
                    body = anonymous.Block;
                    explicitReturnType = null;
                    isStatic = HasAnonymousFunctionModifier(anonymous.Modifiers, SyntaxKind.StaticKeyword);
                    isAsync = HasAnonymousFunctionModifier(anonymous.Modifiers, SyntaxKind.AsyncKeyword);
                    return true;

                default:
                    parameters = ImmutableArray<ParameterSyntax>.Empty;
                    hasExplicitParameterList = false;
                    body = syntax;
                    explicitReturnType = null;
                    isStatic = false;
                    isAsync = false;
                    return false;
            }
        }

        private static bool HasAnonymousFunctionModifier(SyntaxTokenList modifiers, SyntaxKind kind)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                if (modifier.Kind == kind ||
                    (modifier.Kind == SyntaxKind.IdentifierToken && modifier.ContextualKind == kind))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetDelegateInvokeMethod(TypeSymbol targetType, out NamedTypeSymbol delegateType, out MethodSymbol invoke)
        {
            if (targetType is NamedTypeSymbol named && named.TypeKind == TypeKind.Delegate)
            {
                var methods = LookupMethods(named, "Invoke");
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!m.IsStatic && m.Name == "Invoke")
                    {
                        delegateType = named;
                        invoke = m;
                        return true;
                    }
                }
            }

            delegateType = null!;
            invoke = null!;
            return false;
        }

        // A void delegate takes a statement expression or no 'return x', any other delegate needs a value (C# 10.7)
        private bool LambdaBodyFitsDelegateReturn(
            BoundUnboundLambdaExpression lambda,
            SyntaxNode bodySyntax,
            MethodSymbol invoke,
            bool isAsync,
            BindingContext context)
        {
            bool targetIsVoid = invoke.ReturnType.SpecialType == SpecialType.System_Void ||
                (isAsync && TryGetTaskResultType(invoke.ReturnType, out TypeSymbol? taskResult) && taskResult is null);
            if (bodySyntax is ExpressionSyntax expression)
            {
                if (targetIsVoid)
                {
                    return expression is InvocationExpressionSyntax or AssignmentExpressionSyntax or ObjectCreationExpressionSyntax
                        or ConditionalAccessExpressionSyntax or AwaitExpressionSyntax or PrefixUnaryExpressionSyntax
                        or PostfixUnaryExpressionSyntax or ThrowExpressionSyntax;
                }

                if (expression is not InvocationExpressionSyntax and not ConditionalAccessExpressionSyntax)
                    return true;
            }

            var parameterTypes = ImmutableArray.CreateBuilder<TypeSymbol>(invoke.Parameters.Length);
            foreach (ParameterSymbol parameter in invoke.Parameters)
                parameterTypes.Add(parameter.Type);
            if (!TryProbeLambdaBody(lambda, parameterTypes.MoveToImmutable(), context, out TypeSymbol? bodyType, out bool returnsValue, out bool returnsNothing))
                return true;

            if (bodySyntax is ExpressionSyntax)
                return bodyType?.SpecialType != SpecialType.System_Void;
            if (targetIsVoid)
                return !returnsValue;
            return !returnsNothing && (returnsValue || BlockEndsInThrow((BlockSyntax)bodySyntax));
        }

        private static bool BlockEndsInThrow(BlockSyntax block)
            => block.Statements.Count != 0 && block.Statements[block.Statements.Count - 1] is ThrowStatementSyntax or WhileStatementSyntax or ForStatementSyntax;

        // Speculative lambda binding suppresses diagnostics and semantic records
        private bool CanConvertLambdaToDelegate(
            BoundUnboundLambdaExpression lambda,
            TypeSymbol targetType,
            BindingContext context)
        {
            if (!TryGetDelegateInvokeMethod(targetType, out _, out var invoke))
                return false;

            if (!TryGetAnonymousFunctionParts(
                    (ExpressionSyntax)lambda.Syntax,
                    out var parameterSyntaxes,
                    out var hasExplicitParameterList,
                    out SyntaxNode bodySyntax,
                    out var explicitReturnType,
                    out _,
                    out bool isAsync))
            {
                return false;
            }

            if (hasExplicitParameterList && parameterSyntaxes.Length != invoke.Parameters.Length)
                return false;

            if (isAsync && !TryGetTaskResultType(invoke.ReturnType, out _) && invoke.ReturnType.SpecialType != SpecialType.System_Void)
                return false;

            if (!LambdaBodyFitsDelegateReturn(lambda, bodySyntax, invoke, isAsync, context))
                return false;

            if (explicitReturnType is not null)
            {
                if (IsVar(explicitReturnType))
                    return false;

                var returnDiagnostics = new DiagnosticBag();
                var returnType = BindType(explicitReturnType, context, returnDiagnostics);
                if (returnType.Kind == SymbolKind.Error || !AreSameType(returnType, invoke.ReturnType))
                    return false;
            }

            if (!hasExplicitParameterList)
                return true;

            for (int i = 0; i < parameterSyntaxes.Length; i++)
            {
                var sourceParameter = parameterSyntaxes[i];
                var targetParameter = invoke.Parameters[i];

                if (DeclarationBuilder.GetParameterRefKind(sourceParameter) != targetParameter.RefKind)
                    return false;

                if (sourceParameter.Type is not null && !IsVar(sourceParameter.Type))
                {
                    var probeDiagnostics = new DiagnosticBag();
                    var sourceType = BindType(sourceParameter.Type, context, probeDiagnostics);
                    if (!AreSameType(sourceType, targetParameter.Type))
                        return false;
                }
            }

            return true;
        }

        // The type a lambda body produces once its parameters have these types, for output type inference (C# 12.6.3.7)
        private TypeSymbol? InferLambdaReturnType(BoundUnboundLambdaExpression lambda, ImmutableArray<TypeSymbol> parameterTypes, BindingContext context)
        {
            var syntax = (ExpressionSyntax)lambda.Syntax;
            if (!TryGetAnonymousFunctionParts(syntax, out ImmutableArray<ParameterSyntax> parameterSyntaxes, out bool hasExplicitParameterList, out _, out TypeSyntax? explicitReturnType, out _, out bool isAsync) ||
                (hasExplicitParameterList && parameterSyntaxes.Length != parameterTypes.Length))
            {
                return null;
            }

            if (explicitReturnType is not null)
                return IsVar(explicitReturnType) ? null : BindType(explicitReturnType, WithRecorder(context, NullBindingRecorder.Instance), new DiagnosticBag());

            TryProbeLambdaBody(lambda, parameterTypes, context, out TypeSymbol? result, out _, out _);
            if (result is null or NullTypeSymbol or DefaultLiteralTypeSymbol or ErrorTypeSymbol || result.SpecialType == SpecialType.System_Void)
                result = null;
            if (!isAsync)
                return result;

            // An async lambda produces a task of what its body returns
            NamedTypeSymbol? taskDefinition = GetWellKnownType(context.Compilation, new[] { "System", "Threading", "Tasks" }, "Task", result is null ? 0 : 1);
            if (taskDefinition is null)
                return null;
            return result is null ? taskDefinition : context.Compilation.ConstructNamedType(taskDefinition, ImmutableArray.Create(result));
        }

        // Binds the body with these parameter types, discarding diagnostics and records
        private bool TryProbeLambdaBody(
            BoundUnboundLambdaExpression lambda,
            ImmutableArray<TypeSymbol> parameterTypes,
            BindingContext context,
            out TypeSymbol? bodyType,
            out bool returnsValue,
            out bool returnsNothing)
        {
            bodyType = null;
            returnsValue = false;
            returnsNothing = false;
            var syntax = (ExpressionSyntax)lambda.Syntax;
            if (!TryGetAnonymousFunctionParts(syntax, out ImmutableArray<ParameterSyntax> parameterSyntaxes, out bool hasExplicitParameterList, out SyntaxNode bodySyntax, out _, out bool isStatic, out bool isAsync) ||
                (hasExplicitParameterList && parameterSyntaxes.Length != parameterTypes.Length))
            {
                return false;
            }

            var diagnostics = new DiagnosticBag();
            var probeContext = WithRecorder(context, NullBindingRecorder.Instance);

            var lambdaMethod = new LambdaMethodSymbol(
                name: "<lambda>",
                containing: context.ContainingSymbol,
                returnType: context.Compilation.GetSpecialType(SpecialType.System_Object),
                parameters: ImmutableArray<ParameterSymbol>.Empty,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                isStatic: true,
                isAsync: isAsync,
                returnsByRefReadonly: false,
                isDeclaredStatic: isStatic);
            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(parameterTypes.Length);
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                string name = hasExplicitParameterList ? parameterSyntaxes[i].Identifier.ValueText ?? "" : "arg" + i.ToString();
                var refKind = hasExplicitParameterList ? DeclarationBuilder.GetParameterRefKind(parameterSyntaxes[i]) : ParameterRefKind.None;
                parameters.Add(new ParameterSymbol(name, lambdaMethod, parameterTypes[i], ImmutableArray<Location>.Empty, refKind: refKind));
            }
            lambdaMethod.SetParameters(parameters.ToImmutable());

            var bodyBinder = new LocalScopeBinder(parent: this, flags: Flags | BinderFlags.InLambda, containing: lambdaMethod, inheritFlowFromParent: false);
            var bodyContext = new BindingContext(context.Compilation, context.SemanticModel, lambdaMethod, NullBindingRecorder.Instance);
            TypeSymbol? result = null;
            if (bodySyntax is ExpressionSyntax expressionBody)
            {
                result = bodyBinder.BindExpression(expressionBody, bodyContext, diagnostics).Type;
            }
            else if (bodySyntax is BlockSyntax block)
            {
                // Each return converts to the placeholder object return type; the types under those conversions are the candidates
                var returns = new ReturnTypeCollector();
                returns.RewriteNode(bodyBinder.BindStatement(block, bodyContext, diagnostics));
                result = returns.Single;
                returnsValue = returns.ReturnsValue;
                returnsNothing = returns.ReturnsNothing;
            }
            bodyType = result;
            return true;
        }

        private sealed class ReturnTypeCollector : BoundTreeRewriter
        {
            private bool _conflict;
            public TypeSymbol? Single { get; private set; }
            public bool ReturnsValue { get; private set; }
            public bool ReturnsNothing { get; private set; }

            protected override BoundStatement RewriteReturnStatement(BoundReturnStatement node)
            {
                ReturnsValue |= node.Expression is not null;
                ReturnsNothing |= node.Expression is null;
                var value = node.Expression is BoundConversionExpression conversion ? conversion.Operand : node.Expression;
                if (value?.Type is TypeSymbol type && !_conflict)
                {
                    if (Single is null)
                        Single = type;
                    else if (!AreSameType(Single, type))
                    {
                        _conflict = true;
                        Single = null;
                    }
                }
                return node;
            }

            protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node) => node;
        }

        // Delegate parameter types establish the lambda signature before body binding
        private BoundExpression BindLambdaConversion(
            BoundUnboundLambdaExpression lambda,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var syntax = (ExpressionSyntax)lambda.Syntax;

            if (!TryGetDelegateInvokeMethod(targetType, out var delegateType, out var invoke))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LAMBDA001",
                    DiagnosticSeverity.Error,
                    $"Cannot convert lambda expression to non-delegate type '{targetType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            if (!TryGetAnonymousFunctionParts(
                    syntax,
                    out var parameterSyntaxes,
                    out var hasExplicitParameterList,
                    out var bodySyntax,
                    out var explicitReturnType,
                    out var isStatic,
                    out var isAsync))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            if (isAsync)
            {
                if (!TryGetTaskResultType(invoke.ReturnType, out _))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_ASYNC001",
                        DiagnosticSeverity.Error,
                        "An async lambda must convert to a delegate returning Task, Task<T>, ValueTask or ValueTask<T>.",
                        new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                }
            }

            if (hasExplicitParameterList && parameterSyntaxes.Length != invoke.Parameters.Length)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LAMBDA003",
                    DiagnosticSeverity.Error,
                    $"Delegate '{delegateType.Name}' expects {invoke.Parameters.Length} parameter(s), but the lambda has {parameterSyntaxes.Length}.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));

                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            TypeSymbol lambdaReturnType = invoke.ReturnType;
            if (explicitReturnType is not null)
            {
                if (IsVar(explicitReturnType))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_LAMBDA009",
                        DiagnosticSeverity.Error,
                        "An explicit lambda return type cannot be 'var'.",
                        new Location(context.SemanticModel.SyntaxTree, explicitReturnType.Span)));
                }
                else
                {
                    var boundReturnType = BindType(explicitReturnType, context, diagnostics);
                    if (boundReturnType.Kind != SymbolKind.Error)
                    {
                        lambdaReturnType = boundReturnType;
                        bool explicitReturnsByRefReadonly =
                            explicitReturnType is RefTypeSyntax explicitRefReturn &&
                            explicitRefReturn.ReadOnlyKeyword.Kind == SyntaxKind.ReadOnlyKeyword;
                        if (!AreSameType(boundReturnType, invoke.ReturnType) ||
                            explicitReturnsByRefReadonly != invoke.ReturnsByRefReadonly)
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_LAMBDA008",
                                DiagnosticSeverity.Error,
                                $"Explicit lambda return type '{boundReturnType.Name}' does not match delegate return type '{invoke.ReturnType.Name}'.",
                                new Location(context.SemanticModel.SyntaxTree, explicitReturnType.Span)));
                        }
                    }
                }
            }

            var lambdaMethod = new LambdaMethodSymbol(
                name: "<lambda>",
                containing: context.ContainingSymbol,
                returnType: lambdaReturnType,
                parameters: ImmutableArray<ParameterSymbol>.Empty,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                isStatic: true,
                isAsync: isAsync,
                returnsByRefReadonly: invoke.ReturnsByRefReadonly,
                isDeclaredStatic: isStatic);

            if (syntax is LambdaExpressionSyntax lambdaSyntax)
            {
                if (lambdaSyntax is SimpleLambdaExpressionSyntax && lambdaSyntax.AttributeLists.Count > 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_LAMBDA010",
                        DiagnosticSeverity.Error,
                        "Attributes on lambda expressions require a parenthesized parameter list.",
                        new Location(context.SemanticModel.SyntaxTree, lambdaSyntax.AttributeLists[0].Span)));
                }

                AttributeBinder.BindAnonymousFunctionAttributes(
                    lambdaSyntax,
                    lambdaMethod,
                    this,
                    context,
                    diagnostics,
                    lambdaMethod.AddAttribute);
            }

            var parameterBuilder = ImmutableArray.CreateBuilder<ParameterSymbol>(invoke.Parameters.Length);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < invoke.Parameters.Length; i++)
            {
                ParameterSyntax? sourceParameter = hasExplicitParameterList ? parameterSyntaxes[i] : null;
                var targetParameter = invoke.Parameters[i];
                string parameterName = sourceParameter?.Identifier.ValueText ?? targetParameter.Name;
                if (string.IsNullOrEmpty(parameterName))
                    parameterName = "arg" + i.ToString();

                if (sourceParameter is not null)
                {
                    ReportFieldKeywordDeclaration(sourceParameter.Identifier, context, diagnostics);

                    if (!seenNames.Add(parameterName))
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_LAMBDA004",
                            DiagnosticSeverity.Error,
                            $"Duplicate lambda parameter name '{parameterName}'.",
                            new Location(context.SemanticModel.SyntaxTree, sourceParameter.Identifier.Span)));
                    }

                    var sourceRefKind = DeclarationBuilder.GetParameterRefKind(sourceParameter);
                    if (sourceRefKind != targetParameter.RefKind)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_LAMBDA005",
                            DiagnosticSeverity.Error,
                            $"Lambda parameter '{parameterName}' ref kind does not match delegate parameter ref kind.",
                            new Location(context.SemanticModel.SyntaxTree, sourceParameter.Span)));
                    }

                    if (sourceParameter.Type is not null && !IsVar(sourceParameter.Type))
                    {
                        var sourceType = BindType(sourceParameter.Type, context, diagnostics);
                        if (!AreSameType(sourceType, targetParameter.Type))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_LAMBDA006",
                                DiagnosticSeverity.Error,
                                $"Lambda parameter '{parameterName}' type '{sourceType.Name}' does not match delegate parameter type '{targetParameter.Type.Name}'.",
                                new Location(context.SemanticModel.SyntaxTree, sourceParameter.Type.Span)));
                        }
                    }
                }

                parameterBuilder.Add(new ParameterSymbol(
                    parameterName,
                    containing: lambdaMethod,
                    type: targetParameter.Type,
                    locations: sourceParameter is null
                        ? ImmutableArray<Location>.Empty
                        : ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, sourceParameter.Identifier.Span)),
                    isReadOnlyRef: targetParameter.IsReadOnlyRef,
                    refKind: targetParameter.RefKind,
                    isScoped: targetParameter.IsScoped,
                    isParams: targetParameter.IsParams,
                    hasExplicitDefault: targetParameter.HasExplicitDefault,
                    defaultValueOpt: targetParameter.DefaultValueOpt));
            }

            lambdaMethod.SetParameters(parameterBuilder.ToImmutable());

            for (int i = 0; i < parameterSyntaxes.Length && i < lambdaMethod.Parameters.Length; i++)
                context.Recorder.RecordDeclared(parameterSyntaxes[i], lambdaMethod.Parameters[i]);

            var bodyBinder = new LocalScopeBinder(
                parent: this,
                flags: Flags | BinderFlags.InLambda,
                containing: lambdaMethod,
                inheritFlowFromParent: false);

            var bodyContext = new BindingContext(context.Compilation, context.SemanticModel, lambdaMethod, context.Recorder);
            BoundStatement body;

            if (bodySyntax is BlockSyntax block)
            {
                body = bodyBinder.BindStatement(block, bodyContext, diagnostics);
            }
            else if (bodySyntax is ExpressionSyntax expressionBody)
            {
                TypeSymbol bodyReturnType = isAsync && TryGetTaskResultType(lambdaReturnType, out TypeSymbol? taskResult)
                    ? taskResult ?? context.Compilation.GetSpecialType(SpecialType.System_Void)
                    : lambdaReturnType;
                if (bodyReturnType.SpecialType == SpecialType.System_Void)
                {
                    body = new BoundBlockStatement(
                        syntax,
                        ImmutableArray.Create(bodyBinder.BindVoidExpressionBody(expressionBody, expressionBody, bodyContext, diagnostics)));
                }
                else
                {
                    BoundExpression expression = bodyBinder.BindExpression(expressionBody, bodyContext, diagnostics);
                    expression = bodyBinder.ApplyConversion(
                        exprSyntax: expressionBody,
                        expr: expression,
                        targetType: bodyReturnType,
                        diagnosticNode: expressionBody,
                        context: bodyContext,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    body = new BoundBlockStatement(
                        syntax,
                        ImmutableArray.Create<BoundStatement>(new BoundReturnStatement(expressionBody, expression)));
                }
            }
            else
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LAMBDA007",
                    DiagnosticSeverity.Error,
                    "Unsupported lambda body.",
                    new Location(context.SemanticModel.SyntaxTree, bodySyntax.Span)));

                body = new BoundBadStatement(bodySyntax as StatementSyntax ?? new EmptyStatementSyntax(default));
            }

            bodyBinder.ReportControlFlowDiagnostics(bodyContext, diagnostics, body, syntax);
            StaticFunctionChecker.Check(lambdaMethod, body, context, diagnostics);

            return new BoundLambdaExpression(
                syntax,
                delegateType,
                lambdaMethod,
                invoke,
                body,
                isStatic,
                isAsync);
        }

        // A static function may refer to nothing around it but static local functions
        private sealed class StaticFunctionChecker : BoundTreeRewriter
        {
            private readonly MethodSymbol _function;
            private readonly BindingContext _context;
            private readonly DiagnosticBag _diagnostics;

            private StaticFunctionChecker(MethodSymbol function, BindingContext context, DiagnosticBag diagnostics)
            {
                _function = function;
                _context = context;
                _diagnostics = diagnostics;
            }

            public static void Check(MethodSymbol function, BoundStatement body, BindingContext context, DiagnosticBag diagnostics)
            {
                if (IsDeclaredStatic(function))
                    new StaticFunctionChecker(function, context, diagnostics).RewriteStatement(body);
            }

            protected override BoundExpression RewriteExpressionCore(BoundExpression node)
            {
                switch (node)
                {
                    case BoundLocalExpression local:
                        if (!local.Local.IsConst)
                            ReportOuterSymbol(node, local.Local);
                        return node;
                    case BoundParameterExpression parameter:
                        ReportOuterSymbol(node, parameter.Parameter);
                        return node;
                    case BoundThisExpression:
                    case BoundBaseExpression:
                        ReportThis(node);
                        return node;
                    // Left behind by errors reported already
                    case BoundUnboundConditionalExpression:
                    case BoundUnboundSwitchExpression:
                        return node;
                    default:
                        return base.RewriteExpressionCore(node);
                }
            }

            protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
            {
                if (node.Method.OriginalDefinition is LocalFunctionSymbol function)
                {
                    if (!IsDeclaredStatic(function))
                        ReportOuterSymbol(node, function);
                }
                else if (node.ReceiverOpt is null && !node.Method.IsStatic)
                {
                    ReportThis(node);
                }

                return base.RewriteCallExpression(node);
            }

            protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
                => IsDeclaredStatic(node.Method) ? node : base.RewriteLambdaExpression(node);

            protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
                => IsDeclaredStatic(node.LocalFunction) ? node : base.RewriteLocalFunctionStatement(node);

            private void ReportOuterSymbol(BoundExpression node, Symbol symbol)
            {
                for (Symbol? owner = symbol.ContainingSymbol; owner is MethodSymbol; owner = owner.ContainingSymbol)
                {
                    if (ReferenceEquals(owner, _function))
                        return;
                }

                Report(node, _function is LambdaMethodSymbol
                    ? ("CN_STATIC001", $"A static anonymous function cannot contain a reference to '{symbol.Name}'.")
                    : ("CN_STATIC003", $"A static local function cannot contain a reference to '{symbol.Name}'."));
            }

            private void ReportThis(BoundExpression node)
            {
                Report(node, _function is LambdaMethodSymbol
                    ? ("CN_STATIC002", "A static anonymous function cannot contain a reference to 'this' or 'base'.")
                    : ("CN_STATIC004", "A static local function cannot contain a reference to 'this' or 'base'."));
            }

            private void Report(BoundExpression node, (string Id, string Message) error)
            {
                _diagnostics.Add(new Diagnostic(error.Id, DiagnosticSeverity.Error, error.Message, new Location(_context.SemanticModel.SyntaxTree, node.Syntax.Span)));
            }
        }

        private bool CanConvertMethodGroupToDelegate(
            BoundMethodGroupExpression methodGroup,
            TypeSymbol targetType,
            BindingContext context)
        {
            return TryResolveMethodGroupConversion(
                methodGroup,
                targetType,
                context,
                diagnostics: new DiagnosticBag(),
                diagnosticNode: methodGroup.Syntax,
                reportDiagnostics: false,
                out _,
                out _,
                out _);
        }

        // Method group conversion uses the delegate Invoke signature as synthetic arguments
        private bool TryResolveMethodGroupConversion(
            BoundMethodGroupExpression methodGroup,
            TypeSymbol targetType,
            BindingContext context,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            bool reportDiagnostics,
            [NotNullWhen(true)] out NamedTypeSymbol? delegateType,
            [NotNullWhen(true)] out MethodSymbol? invoke,
            [NotNullWhen(true)] out MethodSymbol? chosen)
        {
            delegateType = null;
            invoke = null;
            chosen = null;

            if (!TryGetDelegateInvokeMethod(targetType, out var resolvedDelegateType, out var resolvedInvoke))
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV001",
                        DiagnosticSeverity.Error,
                        $"Cannot convert method group to non-delegate type '{targetType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            delegateType = resolvedDelegateType;
            invoke = resolvedInvoke;

            var invokeForResolution = resolvedInvoke;
            var arityCandidates = methodGroup.Methods
                .Where(m => m.Parameters.Length == invokeForResolution.Parameters.Length)
                .ToImmutableArray();

            var syntax = (ExpressionSyntax)methodGroup.Syntax;
            var argsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(invoke.Parameters.Length);
            for (int i = 0; i < invoke.Parameters.Length; i++)
                argsBuilder.Add(new BoundTypeOnlyExpression(syntax, invoke.Parameters[i].Type));

            var overloadContext = WithRecorder(context, NullBindingRecorder.Instance);
            var overloadDiagnostics = new DiagnosticBag();
            MethodSymbol? resolvedMethod = null;
            if (!arityCandidates.IsDefaultOrEmpty)
            {
                TryResolveOverload(
                    candidates: arityCandidates,
                    args: argsBuilder.ToImmutable(),
                    getArgExprSyntax: _ => syntax,
                    getArgRefKindKeyword: i => MakeRefKindToken(invokeForResolution.Parameters[i], syntax.Span),
                    getArgName: null,
                    chosen: out resolvedMethod,
                    convertedArgs: out _,
                    context: overloadContext,
                    diagnostics: overloadDiagnostics,
                    diagnosticNode: diagnosticNode);
            }

            // Extension methods apply when no instance method does, the receiver then becomes their first argument
            if (resolvedMethod is null && methodGroup.ReceiverOpt is BoundExpression extensionReceiver)
            {
                ImmutableArray<MethodSymbol> extensions = LookupExtensionMethods(methodGroup.Name, extensionReceiver, context)
                    .Where(m => m.Parameters.Length == invokeForResolution.Parameters.Length + 1)
                    .ToImmutableArray();
                if (!extensions.IsDefaultOrEmpty)
                {
                    argsBuilder.Insert(0, new BoundTypeOnlyExpression(syntax, extensionReceiver.Type));
                    TryResolveOverload(
                        candidates: extensions,
                        args: argsBuilder.ToImmutable(),
                        getArgExprSyntax: _ => syntax,
                        getArgRefKindKeyword: i => i == 0 ? null : MakeRefKindToken(invokeForResolution.Parameters[i - 1], syntax.Span),
                        getArgName: null,
                        chosen: out resolvedMethod,
                        convertedArgs: out _,
                        context: overloadContext,
                        diagnostics: new DiagnosticBag(),
                        diagnosticNode: diagnosticNode);
                }
            }

            if (resolvedMethod is null)
            {
                if (reportDiagnostics)
                {
                    if (arityCandidates.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MG_CONV002",
                            DiagnosticSeverity.Error,
                            $"No overload of '{methodGroup.Name}' is compatible with delegate '{delegateType.Name}'.",
                            new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                    }
                    else
                    {
                        foreach (Diagnostic diagnostic in overloadDiagnostics.ToImmutable())
                            diagnostics.Add(diagnostic);
                    }
                }

                return false;
            }

            if (!AreMethodGroupParametersCompatible(resolvedMethod, invoke, syntax, context))
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV005",
                        DiagnosticSeverity.Error,
                        $"No overload for '{resolvedMethod.Name}' matches delegate '{delegateType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            if (!IsMethodGroupReturnCompatible(resolvedMethod, invoke, syntax, context))
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV003",
                        DiagnosticSeverity.Error,
                        $"Return type of method '{resolvedMethod.Name}' is not compatible with delegate '{delegateType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            if (IsClosedExtensionTarget(resolvedMethod, invoke) && (resolvedMethod.Parameters[0].Type.IsValueType || resolvedMethod.Parameters[0].Type is ByRefTypeSymbol or TypeParameterSymbol))
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV006",
                        DiagnosticSeverity.Error,
                        $"Extension method '{resolvedMethod.Name}' defined on a value type cannot be used to create delegates.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            // A local function gets its receiver from the closure the conversion creates
            if (!resolvedMethod.IsStatic && methodGroup.ReceiverOpt is null && resolvedMethod.OriginalDefinition is not LocalFunctionSymbol)
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV004",
                        DiagnosticSeverity.Error,
                        "An object reference is required for the non-static method group.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            chosen = resolvedMethod;
            return true;
        }

        private static SyntaxToken? MakeRefKindToken(ParameterSymbol parameter, TextSpan span)
        {
            var refKind = parameter.RefKind;
            if (refKind == ParameterRefKind.None && parameter.Type is ByRefTypeSymbol)
                refKind = parameter.IsReadOnlyRef ? ParameterRefKind.In : ParameterRefKind.Ref;

            return refKind switch
            {
                ParameterRefKind.Ref => MakeToken(SyntaxKind.RefKeyword, span),
                ParameterRefKind.Out => MakeToken(SyntaxKind.OutKeyword, span),
                ParameterRefKind.In => MakeToken(SyntaxKind.InKeyword, span),
                _ => null
            };
        }

        private static bool IsClosedExtensionTarget(MethodSymbol method, MethodSymbol invoke)
            => method.IsStatic && method.Parameters.Length == invoke.Parameters.Length + 1;

        // A delegate reaches the method with the argument bits it was given, so only conversions that keep them apply
        private bool AreMethodGroupParametersCompatible(MethodSymbol method, MethodSymbol invoke, ExpressionSyntax syntax, BindingContext context)
        {
            int offset = method.Parameters.Length - invoke.Parameters.Length;
            for (int i = 0; i < invoke.Parameters.Length; i++)
            {
                TypeSymbol from = invoke.Parameters[i].Type;
                TypeSymbol to = method.Parameters[i + offset].Type;
                if (from is ByRefTypeSymbol || to is ByRefTypeSymbol)
                {
                    if (!AreSameType(from, to))
                        return false;

                    continue;
                }

                ConversionKind kind = ClassifyConversion(new BoundTypeOnlyExpression(syntax, from), to, context).Kind;
                if (kind is not (ConversionKind.Identity or ConversionKind.ImplicitReference))
                    return false;
            }

            return true;
        }

        private bool IsMethodGroupReturnCompatible(
            MethodSymbol method,
            MethodSymbol invoke,
            ExpressionSyntax syntax,
            BindingContext context)
        {
            if (invoke.ReturnType.SpecialType == SpecialType.System_Void)
                return method.ReturnType.SpecialType == SpecialType.System_Void;

            if (method.ReturnType.SpecialType == SpecialType.System_Void)
                return false;

            if (method.ReturnType is ByRefTypeSymbol || invoke.ReturnType is ByRefTypeSymbol)
                return AreSameType(method.ReturnType, invoke.ReturnType) &&
                       method.ReturnsByRefReadonly == invoke.ReturnsByRefReadonly;

            var methodReturn = new BoundTypeOnlyExpression(syntax, method.ReturnType);
            var conversion = ClassifyConversion(methodReturn, invoke.ReturnType, context);
            return conversion.Kind == ConversionKind.Identity ||
                   conversion.Kind == ConversionKind.ImplicitReference;
        }

        private BoundExpression BindMethodGroupConversion(
            BoundMethodGroupExpression methodGroup,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var syntax = (ExpressionSyntax)methodGroup.Syntax;

            if (!TryResolveMethodGroupConversion(
                    methodGroup,
                    targetType,
                    context,
                    diagnostics,
                    diagnosticNode,
                    reportDiagnostics: true,
                    out NamedTypeSymbol? delegateType,
                    out MethodSymbol? invoke,
                    out MethodSymbol? chosen))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            if (IsClosedExtensionTarget(chosen, invoke))
                return new BoundDelegateCreationExpression(syntax, delegateType, chosen, methodGroup.ReceiverOpt, isVirtual: false);

            BoundExpression? target = chosen.IsStatic ? null : methodGroup.ReceiverOpt;
            if (!NeedsMethodGroupWrapper(chosen, target))
            {
                bool isVirtual = target is not null && target is not BoundBaseExpression && (chosen.IsVirtual || chosen.IsAbstract);
                MethodSymbol method = chosen;
                while (isVirtual && method.OverriddenMethod is MethodSymbol overridden)
                    method = overridden;

                if (target is not null && target.Type is TypeParameterSymbol)
                    target = new BoundConversionExpression(syntax, context.Compilation.GetSpecialType(SpecialType.System_Object), target, new Conversion(ConversionKind.Boxing), isChecked: false);

                return new BoundDelegateCreationExpression(syntax, delegateType, method, target, isVirtual);
            }

            TypeSymbol? valueReceiverType = null;
            if (target is not null && target.Type.IsValueType)
            {
                // A private element preserves the captured struct and mutations without aliasing the source variable.
                valueReceiverType = target.Type;
                TypeSymbol intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                target = new BoundArrayCreationExpression(syntax, context.Compilation.CreateArrayType(valueReceiverType, rank: 1), valueReceiverType,
                    new BoundLiteralExpression(syntax, intType, 1),
                    new BoundArrayInitializerExpression(syntax, valueReceiverType, ImmutableArray.Create(target)));
            }

            MethodGroupWrapper wrapper = GetMethodGroupWrapper(chosen, delegateType, invoke, target, valueReceiverType, syntax, diagnosticNode, context, diagnostics);
            var lambda = new BoundLambdaExpression(
                syntax,
                delegateType,
                wrapper.Method,
                invoke,
                wrapper.Body,
                isStatic: true,
                isAsync: false,
                targetOpt: target);
            return chosen.OriginalDefinition is LocalFunctionSymbol localFunction
                ? ShareLocalFunctionDelegate(syntax, localFunction, chosen, delegateType, lambda, context)
                : lambda;
        }

        // A struct receiver needs a private copy to run on and these others have no body of their own for a delegate to point at
        private static bool NeedsMethodGroupWrapper(MethodSymbol chosen, BoundExpression? target)
            => chosen.OriginalDefinition is LocalFunctionSymbol ||
               chosen.IsExtern ||
               MethodAttributeFacts.HasIntrinsic(chosen) ||
               chosen.GetDllImportData() is not null ||
               (chosen.IsStatic && (chosen.IsAbstract || chosen.IsVirtual)) ||
               (chosen.ContainingSymbol is NamedTypeSymbol owner && owner.TypeKind == TypeKind.Delegate) ||
               (target is not null && target.Type is not TypeParameterSymbol && target.Type.IsValueType);

        // Each conversion would build its own closure, so a capturing local function shares one delegate per activation of its scope and a handler can remove itself
        private BoundExpression ShareLocalFunctionDelegate(
            ExpressionSyntax syntax,
            LocalFunctionSymbol localFunction,
            MethodSymbol chosen,
            NamedTypeSymbol delegateType,
            BoundLambdaExpression lambda,
            BindingContext context)
        {
            if (IsDeclaredStatic(localFunction))
                return lambda;

            LocalScopeBinder? scope = null;
            for (Binder? binder = this; scope is null && binder is LocalScopeBinder candidate; binder = candidate.Parent)
            {
                if (candidate._localFunctions.TryGetValue(localFunction.Name, out LocalFunctionSymbol? found) && ReferenceEquals(found, localFunction))
                    scope = candidate;
            }

            if (scope is null)
                return lambda;

            // A static function around the conversion cannot capture anything, then the local function does not either and has no closure to share
            for (Symbol? symbol = context.ContainingSymbol; symbol is not null && !ReferenceEquals(symbol, scope._containing); symbol = symbol.ContainingSymbol)
            {
                if (IsDeclaredStatic(symbol))
                    return lambda;
            }

            LocalSymbol? shared = null;
            foreach ((MethodSymbol target, NamedTypeSymbol type, LocalSymbol existing) in scope._localFunctionDelegates)
            {
                if (ReferenceEquals(target.OriginalDefinition, localFunction) && AreSameType(type, delegateType) && HaveSameTypeArguments(target, chosen))
                    shared = existing;
            }

            if (shared is null)
            {
                shared = new LocalSymbol("<delegate>", scope._containing, delegateType, ImmutableArray<Location>.Empty);
                scope._localFunctionDelegates.Add((chosen, delegateType, shared));
            }

            var assignment = new AssignmentExpressionSyntax(SyntaxKind.CoalesceAssignmentExpression, syntax, MakeToken(SyntaxKind.QuestionQuestionEqualsToken, syntax.Span), syntax);
            return new BoundNullCoalescingAssignmentExpression(assignment, new BoundLocalExpression(syntax, shared), lambda);
        }

        internal void DeclareLocalFunctionDelegates(ImmutableArray<BoundStatement>.Builder statements, SyntaxNode syntax)
        {
            int index = 0;
            foreach ((MethodSymbol _, NamedTypeSymbol _, LocalSymbol shared) in _localFunctionDelegates)
            {
                var none = new BoundConversionExpression(syntax, shared.Type, new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null), new Conversion(ConversionKind.NullLiteral), isChecked: false);
                statements.Insert(index++, new BoundLocalDeclarationStatement(syntax, shared, none));
            }
        }

        // A wrapper belongs to the function it is converted in, its parameters and type parameters come from there
        private MethodGroupWrapper GetMethodGroupWrapper(
            MethodSymbol chosen,
            NamedTypeSymbol delegateType,
            MethodSymbol invoke,
            BoundExpression? target,
            TypeSymbol? valueReceiverType,
            ExpressionSyntax syntax,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            // Every conversion of a local function shares the place it is declared in, so a static one stays equal across them
            Symbol owner = chosen.OriginalDefinition is LocalFunctionSymbol localFunction && localFunction.ContainingSymbol is Symbol declaredIn
                ? declaredIn
                : context.ContainingSymbol;
            Dictionary<MethodSymbol, List<MethodGroupWrapper>> known = context.Compilation.MethodGroupWrappers;
            if (!known.TryGetValue(chosen.OriginalDefinition, out List<MethodGroupWrapper>? wrappers))
            {
                wrappers = new List<MethodGroupWrapper>();
                known.Add(chosen.OriginalDefinition, wrappers);
            }

            foreach (MethodGroupWrapper existing in wrappers)
            {
                if (ReferenceEquals(existing.Owner, owner) &&
                    AreSameType(existing.DelegateType, delegateType) &&
                    AreSameType(existing.ValueReceiverType, valueReceiverType) &&
                    AreSameType(existing.Target.ContainingSymbol as NamedTypeSymbol, chosen.ContainingSymbol as NamedTypeSymbol) &&
                    HaveSameTypeArguments(existing.Target, chosen))
                {
                    return existing;
                }
            }

            MethodGroupWrapper wrapper = CreateMethodGroupWrapper(chosen, delegateType, invoke, target, valueReceiverType, owner, syntax, diagnosticNode, context, diagnostics);
            wrappers.Add(wrapper);
            return wrapper;
        }

        private static bool HaveSameTypeArguments(MethodSymbol left, MethodSymbol right)
        {
            if (left.TypeArguments.Length != right.TypeArguments.Length)
                return false;

            for (int i = 0; i < left.TypeArguments.Length; i++)
            {
                if (!AreSameType(left.TypeArguments[i], right.TypeArguments[i]))
                    return false;
            }

            return true;
        }

        private MethodGroupWrapper CreateMethodGroupWrapper(
            MethodSymbol chosen,
            NamedTypeSymbol delegateType,
            MethodSymbol invoke,
            BoundExpression? target,
            TypeSymbol? valueReceiverType,
            Symbol owner,
            ExpressionSyntax syntax,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var lambdaMethod = new LambdaMethodSymbol(
                name: "<methodgroup>",
                containing: owner,
                returnType: invoke.ReturnType,
                parameters: ImmutableArray<ParameterSymbol>.Empty,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                isStatic: true,
                isAsync: false,
                returnsByRefReadonly: invoke.ReturnsByRefReadonly,
                isDeclaredStatic: chosen.OriginalDefinition is LocalFunctionSymbol);

            BoundExpression? receiver = null;
            ImmutableArray<ParameterSymbol>.Builder parameterBuilder = ImmutableArray.CreateBuilder<ParameterSymbol>(invoke.Parameters.Length + (target is null ? 0 : 1));
            if (target is not null)
            {
                TypeSymbol receiverType = target.Type;
                if (valueReceiverType is null && chosen.ContainingSymbol is NamedTypeSymbol declaringType)
                    receiverType = declaringType;

                var receiverParameter = new ParameterSymbol("<>receiver", lambdaMethod, receiverType, ImmutableArray<Location>.Empty);
                parameterBuilder.Add(receiverParameter);
                receiver = new BoundParameterExpression(syntax, receiverParameter);
                if (valueReceiverType is not null)
                {
                    receiver = new BoundArrayElementAccessExpression(syntax, valueReceiverType, receiver,
                        new BoundLiteralExpression(syntax, context.Compilation.GetSpecialType(SpecialType.System_Int32), 0));
                }
            }
            int receiverCount = parameterBuilder.Count;
            for (int i = 0; i < invoke.Parameters.Length; i++)
            {
                var delegateParameter = invoke.Parameters[i];
                var parameterName = string.IsNullOrEmpty(delegateParameter.Name)
                    ? "arg" + i.ToString()
                    : delegateParameter.Name;

                parameterBuilder.Add(new ParameterSymbol(
                    parameterName,
                    containing: lambdaMethod,
                    type: delegateParameter.Type,
                    locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                    isReadOnlyRef: delegateParameter.IsReadOnlyRef,
                    refKind: delegateParameter.RefKind,
                    isScoped: delegateParameter.IsScoped,
                    isParams: false,
                    hasExplicitDefault: false));
            }

            lambdaMethod.SetParameters(parameterBuilder.ToImmutable());

            var callArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(invoke.Parameters.Length);
            for (int i = 0; i < invoke.Parameters.Length; i++)
            {
                BoundExpression arg = new BoundParameterExpression(syntax, lambdaMethod.Parameters[i + receiverCount]);
                ParameterSymbol targetParameter = chosen.Parameters[i];
                var refKind = targetParameter.RefKind;
                if (refKind == ParameterRefKind.None && targetParameter.Type is ByRefTypeSymbol)
                    refKind = targetParameter.IsReadOnlyRef ? ParameterRefKind.In : ParameterRefKind.Ref;

                if (refKind != ParameterRefKind.None)
                {
                    var byRefType = targetParameter.Type is ByRefTypeSymbol
                        ? targetParameter.Type
                        : context.Compilation.CreateByRefType(arg.Type);
                    arg = new BoundRefExpression(syntax, byRefType, arg);
                }

                callArgsBuilder.Add(arg);
            }

            var call = new BoundCallExpression(syntax, receiver, chosen, callArgsBuilder.ToImmutable());

            BoundStatement body;
            if (invoke.ReturnType.SpecialType == SpecialType.System_Void)
            {
                body = new BoundBlockStatement(
                    syntax,
                    ImmutableArray.Create<BoundStatement>(new BoundExpressionStatement(syntax, call)));
            }
            else
            {
                BoundExpression returnValue = call;
                if (!AreSameType(returnValue.Type, invoke.ReturnType))
                {
                    returnValue = ApplyConversion(
                        exprSyntax: syntax,
                        expr: returnValue,
                        targetType: invoke.ReturnType,
                        diagnosticNode: diagnosticNode,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);
                }

                body = new BoundBlockStatement(
                    syntax,
                    ImmutableArray.Create<BoundStatement>(new BoundReturnStatement(syntax, returnValue)));
            }

            return new MethodGroupWrapper(chosen, delegateType, valueReceiverType, owner, lambdaMethod, body);
        }

        private BoundExpression BindDefaultExpression(
            DefaultExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var type = BindType(node.Type, context, diagnostics);
            if (type is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            if (type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEF001",
                    DiagnosticSeverity.Error,
                    "The default value of 'void' is not valid.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            if (type is ByRefTypeSymbol)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DEF002",
                    DiagnosticSeverity.Error,
                    "The default value of a byref type is not valid.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                return new BoundBadExpression(node);
            }

            return MakeDefaultValue(node, type);
        }
    }
}
