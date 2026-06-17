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
        private static bool TryGetAnonymousFunctionParts(
            ExpressionSyntax syntax,
            out ImmutableArray<ParameterSyntax> parameters,
            out bool hasExplicitParameterList,
            out SyntaxNode body,
            out bool isStatic,
            out bool isAsync)
        {
            switch (syntax)
            {
                case SimpleLambdaExpressionSyntax simple:
                    parameters = ImmutableArray.Create(simple.Parameter);
                    hasExplicitParameterList = true;
                    body = simple.Body;
                    isStatic = simple.StaticKeyword.Span.Length != 0;
                    isAsync = simple.AsyncKeyword.Span.Length != 0;
                    return true;

                case ParenthesizedLambdaExpressionSyntax parenthesized:
                    parameters = parenthesized.ParameterList.Parameters.ToImmutableArray();
                    hasExplicitParameterList = true;
                    body = parenthesized.Body;
                    isStatic = parenthesized.StaticKeyword.Span.Length != 0;
                    isAsync = parenthesized.AsyncKeyword.Span.Length != 0;
                    return true;

                case AnonymousMethodExpressionSyntax anonymous:
                    parameters = anonymous.ParameterList?.Parameters.ToImmutableArray() ?? ImmutableArray<ParameterSyntax>.Empty;
                    hasExplicitParameterList = anonymous.ParameterList is not null;
                    body = anonymous.Block;
                    isStatic = false;
                    isAsync = anonymous.AsyncKeyword.Span.Length != 0;
                    return true;

                default:
                    parameters = ImmutableArray<ParameterSyntax>.Empty;
                    hasExplicitParameterList = false;
                    body = syntax;
                    isStatic = false;
                    isAsync = false;
                    return false;
            }
        }

        private static bool TryGetDelegateInvokeMethod(TypeSymbol targetType, out NamedTypeSymbol delegateType, out MethodSymbol invoke)
        {
            if (targetType is NamedTypeSymbol named && named.TypeKind == TypeKind.Delegate)
            {
                var methods = LookupMethods(named, "Invoke");
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!m.IsStatic && StringComparer.Ordinal.Equals(m.Name, "Invoke"))
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
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            if (hasExplicitParameterList && parameterSyntaxes.Length != invoke.Parameters.Length)
                return false;

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
                    out var isStatic,
                    out var isAsync))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            if (isAsync)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_LAMBDA002",
                    DiagnosticSeverity.Error,
                    "Async lambdas are not implemented.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
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

            var lambdaMethod = new LambdaMethodSymbol(
                name: "<lambda>",
                containing: context.ContainingSymbol,
                returnType: invoke.ReturnType,
                parameters: ImmutableArray<ParameterSymbol>.Empty,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                isStatic: true,
                isAsync: isAsync);

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
                var expression = bodyBinder.BindExpression(expressionBody, bodyContext, diagnostics);
                if (invoke.ReturnType.SpecialType == SpecialType.System_Void)
                {
                    body = new BoundBlockStatement(
                        syntax,
                        ImmutableArray.Create<BoundStatement>(new BoundExpressionStatement(expressionBody, expression)));
                }
                else
                {
                    expression = bodyBinder.ApplyConversion(
                        exprSyntax: expressionBody,
                        expr: expression,
                        targetType: invoke.ReturnType,
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

            return new BoundLambdaExpression(
                syntax,
                delegateType,
                lambdaMethod,
                invoke,
                body,
                isStatic,
                isAsync);
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

        private bool TryResolveMethodGroupConversion(
            BoundMethodGroupExpression methodGroup,
            TypeSymbol targetType,
            BindingContext context,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            bool reportDiagnostics,
            out NamedTypeSymbol? delegateType,
            out MethodSymbol? invoke,
            out MethodSymbol? chosen)
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

            if (arityCandidates.IsDefaultOrEmpty)
            {
                if (reportDiagnostics)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MG_CONV002",
                        DiagnosticSeverity.Error,
                        $"No overload of '{methodGroup.Name}' is compatible with delegate '{delegateType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                }

                return false;
            }

            var syntax = (ExpressionSyntax)methodGroup.Syntax;
            var argsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(invoke.Parameters.Length);
            for (int i = 0; i < invoke.Parameters.Length; i++)
                argsBuilder.Add(new BoundTypeOnlyExpression(syntax, invoke.Parameters[i].Type));

            var overloadDiagnostics = reportDiagnostics ? diagnostics : new DiagnosticBag();
            var overloadContext = WithRecorder(context, NullBindingRecorder.Instance);

            if (!TryResolveOverload(
                candidates: arityCandidates,
                args: argsBuilder.ToImmutable(),
                getArgExprSyntax: _ => syntax,
                getArgRefKindKeyword: i => MakeRefKindToken(invokeForResolution.Parameters[i], syntax.Span),
                getArgName: null,
                chosen: out var resolvedMethod,
                convertedArgs: out _,
                context: overloadContext,
                diagnostics: overloadDiagnostics,
                diagnosticNode: diagnosticNode))
            {
                return false;
            }

            if (resolvedMethod is null)
                return false;

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

            if (!resolvedMethod.IsStatic && methodGroup.ReceiverOpt is null)
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
                return AreSameType(method.ReturnType, invoke.ReturnType);

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
                    out var delegateType,
                    out var invoke,
                    out var chosen))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(targetType);
                return bad;
            }

            var lambdaMethod = new LambdaMethodSymbol(
                name: "<methodgroup>",
                containing: context.ContainingSymbol,
                returnType: invoke!.ReturnType,
                parameters: ImmutableArray<ParameterSymbol>.Empty,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, syntax.Span)),
                isStatic: true,
                isAsync: false);

            var parameterBuilder = ImmutableArray.CreateBuilder<ParameterSymbol>(invoke.Parameters.Length);
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

            var callArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(lambdaMethod.Parameters.Length);
            for (int i = 0; i < lambdaMethod.Parameters.Length; i++)
            {
                BoundExpression arg = new BoundParameterExpression(syntax, lambdaMethod.Parameters[i]);
                var targetParameter = chosen!.Parameters[i];
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

            BoundExpression? receiver = chosen!.IsStatic ? null : methodGroup.ReceiverOpt;
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

            return new BoundLambdaExpression(
                syntax,
                delegateType!,
                lambdaMethod,
                invoke,
                body,
                isStatic: true,
                isAsync: false);
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
