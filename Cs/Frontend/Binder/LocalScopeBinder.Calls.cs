using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Cs
{
    // Invocation, overload resolution, argument binding, and object creation
    internal sealed partial class LocalScopeBinder : Binder
    {
        // Bind arguments before dispatching by callable expression shape
        private BoundExpression BindInvocation(InvocationExpressionSyntax inv, BindingContext context, DiagnosticBag diagnostics)
        {
            if (inv.Expression is IdentifierNameSyntax intrinsicId &&
                string.Equals(intrinsicId.Identifier.ValueText, "nameof", StringComparison.Ordinal))
            {
                return BindNameOf(inv, context, diagnostics);
            }

            var argSyntaxes = inv.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));

            var boundArgs = args.ToImmutable();

            return inv.Expression switch
            {
                MemberAccessExpressionSyntax ma => BindMemberAccessInvocation(inv, ma, argSyntaxes, boundArgs, context, diagnostics),
                IdentifierNameSyntax id => BindSimpleInvocation(inv, id, argSyntaxes, boundArgs, context, diagnostics),
                GenericNameSyntax g => BindGenericSimpleInvocation(inv, g, argSyntaxes, boundArgs, context, diagnostics),
                _ => BindDelegateOrUnsupportedInvocation(inv, argSyntaxes, boundArgs, context, diagnostics)
            };
        }
        private BoundExpression BindNameOf(InvocationExpressionSyntax inv, BindingContext context, DiagnosticBag diagnostics)
        {
            var args = inv.ArgumentList.Arguments;
            if (args.Count != 1 || args[0].NameColon is not null || args[0].RefKindKeyword is not null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_NAMEOF001",
                    DiagnosticSeverity.Error,
                    "nameof requires exactly one unmodified argument.",
                    new Location(context.SemanticModel.SyntaxTree, inv.Span)));
                return new BoundBadExpression(inv);
            }

            if (!TryGetNameOfValue(args[0].Expression, out var name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_NAMEOF002",
                    DiagnosticSeverity.Error,
                    "Invalid expression in nameof.",
                    new Location(context.SemanticModel.SyntaxTree, args[0].Expression.Span)));
                return new BoundBadExpression(inv);
            }

            return new BoundLiteralExpression(
                inv,
                context.Compilation.GetSpecialType(SpecialType.System_String),
                name);
        }

        private static bool TryGetNameOfValue(ExpressionSyntax expression, out string name)
        {
            switch (expression)
            {
                case IdentifierNameSyntax id:
                    name = id.Identifier.ValueText ?? string.Empty;
                    return name.Length != 0;

                case GenericNameSyntax generic:
                    name = generic.Identifier.ValueText ?? string.Empty;
                    return name.Length != 0;

                case MemberAccessExpressionSyntax memberAccess:
                    name = GetSimpleName(memberAccess.Name);
                    return name.Length != 0;

                case ParenthesizedExpressionSyntax parenthesized:
                    return TryGetNameOfValue(parenthesized.Expression, out name);

                default:
                    name = string.Empty;
                    return false;
            }
        }

        private BoundExpression BindCallArgument(ArgumentSyntax argSyntax, BindingContext context, DiagnosticBag diagnostics)
        {
            if (argSyntax.RefKindKeyword is null)
                return BindExpression(argSyntax.Expression, context, diagnostics);

            var rk = argSyntax.RefKindKeyword.Value;

            if (rk.Kind == SyntaxKind.IdentifierToken && rk.ContextualKind == SyntaxKind.ScopedKeyword)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ARGMOD001",
                    DiagnosticSeverity.Error,
                    "The 'scoped' modifier is not valid on an argument.",
                    new Location(context.SemanticModel.SyntaxTree, rk.Span)));

                return BindExpression(argSyntax.Expression, context, diagnostics);
            }

            if (rk.Kind is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword or SyntaxKind.InKeyword)
            {
                return BindByRefCallArgument(
                    argSyntax,
                    argRefKind: rk.Kind switch
                    {
                        SyntaxKind.RefKeyword => ParameterRefKind.Ref,
                        SyntaxKind.OutKeyword => ParameterRefKind.Out,
                        SyntaxKind.InKeyword => ParameterRefKind.In,
                        _ => ParameterRefKind.None
                    },
                    context,
                    diagnostics);
            }


            return BindExpression(argSyntax.Expression, context, diagnostics);
        }

        private BoundExpression BindByRefCallArgument(
            ArgumentSyntax argSyntax,
            ParameterRefKind argRefKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            bool isOutArgument = argRefKind == ParameterRefKind.Out;
            bool isReadOnlyPass = argRefKind == ParameterRefKind.In;

            if (!isOutArgument && argSyntax.Expression is DeclarationExpressionSyntax)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL004",
                    DiagnosticSeverity.Error,
                    "A declaration expression is only valid as an out argument.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));

                return new BoundBadExpression(argSyntax);
            }

            if (isOutArgument && argSyntax.Expression is IdentifierNameSyntax id
                && string.Equals(id.Identifier.ValueText, "_", StringComparison.Ordinal))
            {
                if (!IsNameDeclaredInEnclosingScopes("_"))
                {
                    return new BoundOutDiscardExpression(
                        argSyntax.Expression,
                        context.Compilation.GetSpecialType(SpecialType.System_Void),
                        explicitElementTypeOpt: null);
                }
            }

            var operand = BindAssignableValue(argSyntax.Expression, context, diagnostics);
            if (operand.HasErrors)
                return operand;
            if (operand is BoundOutVarPendingExpression)
                return operand;
            if (operand is BoundOutDiscardExpression)
                return operand;

            if (operand is BoundLocalExpression bl && bl.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF001",
                    DiagnosticSeverity.Error,
                    "Cannot create a ref to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (IsNonRefReturningPropertyTarget(operand))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF002",
                    DiagnosticSeverity.Error,
                    "A property cannot be used as a ref value here.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (!isReadOnlyPass && IsReadOnlyLocalTarget(operand))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF_READONLYLOCAL",
                    DiagnosticSeverity.Error,
                    "Cannot use a readonly local as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (!isReadOnlyPass && IsReadOnlyRefReturnTarget(operand))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REFRET_READONLY002",
                    DiagnosticSeverity.Error,
                    "Cannot pass a 'ref readonly' return value as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            if (!isReadOnlyPass &&
                operand is BoundParameterExpression pe &&
                pe.Parameter.IsReadOnlyRef &&
                !IsInsideIntrinsicMethod(context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_REF003",
                    DiagnosticSeverity.Error,
                    "Cannot use a readonly by-ref parameter as a writable ref.",
                    new Location(context.SemanticModel.SyntaxTree, argSyntax.Expression.Span)));
                return new BoundBadExpression(argSyntax);
            }

            var refElementType = operand.Type is ByRefTypeSymbol br ? br.ElementType : operand.Type;
            var byRefType = context.Compilation.CreateByRefType(refElementType);
            return new BoundRefExpression(argSyntax, byRefType, operand);
        }

        private static ParameterRefKind GetArgRefKind(SyntaxToken? tok)
        {
            if (tok is null)
                return ParameterRefKind.None;

            var t = tok.Value;
            return t.Kind switch
            {
                SyntaxKind.RefKeyword => ParameterRefKind.Ref,
                SyntaxKind.OutKeyword => ParameterRefKind.Out,
                SyntaxKind.InKeyword => ParameterRefKind.In,
                _ => ParameterRefKind.None
            };
        }

        private static bool ArgumentRefKindMatchesParameter(SyntaxToken? argRefKindKeyword, ParameterSymbol parameter)
        {
            var argKind = GetArgRefKind(argRefKindKeyword);

            var paramKind = parameter.RefKind;
            if (paramKind == ParameterRefKind.None && parameter.Type is ByRefTypeSymbol)
                paramKind = parameter.IsReadOnlyRef ? ParameterRefKind.In : ParameterRefKind.Ref;

            if (argKind == ParameterRefKind.Ref && paramKind == ParameterRefKind.In)
                return true;
            if (argKind == ParameterRefKind.In && paramKind == ParameterRefKind.Ref && parameter.IsReadOnlyRef)
                return true;
            return argKind == paramKind;
        }
        private static bool IsInterpolatedStringHandlerParameter(ParameterSymbol parameter)
        {
            var type = parameter.Type is ByRefTypeSymbol byRef ? byRef.ElementType : parameter.Type;
            if (type is not NamedTypeSymbol handlerType)
                return false;

            var attributes = handlerType.OriginalDefinition.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (IsAttributeByMetadataName(
                    attributes[i],
                    "System.Runtime.CompilerServices",
                    "InterpolatedStringHandlerAttribute"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAttributeByMetadataName(AttributeData attribute, string namespaceName, string typeName)
        {
            var type = attribute.AttributeClass;
            if (!string.Equals(type.Name, typeName, StringComparison.Ordinal))
                return false;

            var parts = new Stack<string>();
            Symbol? current = type.ContainingSymbol;
            while (current is NamespaceSymbol ns && !ns.IsGlobalNamespace)
            {
                parts.Push(ns.Name);
                current = ns.ContainingSymbol;
            }

            return string.Equals(string.Join(".", parts), namespaceName, StringComparison.Ordinal);
        }

        private static ImmutableArray<string> GetInterpolatedStringHandlerArgumentNames(ParameterSymbol parameter)
        {
            var attributes = parameter.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                var attribute = attributes[i];
                if (!IsAttributeByMetadataName(
                    attribute,
                    "System.Runtime.CompilerServices",
                    "InterpolatedStringHandlerArgumentAttribute"))
                {
                    continue;
                }

                var names = ImmutableArray.CreateBuilder<string>();
                for (int a = 0; a < attribute.ConstructorArguments.Length; a++)
                {
                    var value = attribute.ConstructorArguments[a].Value;
                    if (value is string name)
                    {
                        names.Add(name);
                    }
                    else if (value is ImmutableArray<TypedConstant> array)
                    {
                        for (int e = 0; e < array.Length; e++)
                        {
                            if (array[e].Value is string elementName)
                                names.Add(elementName);
                        }
                    }
                }

                return names.ToImmutable();
            }

            return ImmutableArray<string>.Empty;
        }

        private BoundExpression BindInterpolatedStringHandlerArgument(
            InterpolatedStringExpressionSyntax interpolation,
            MethodSymbol outerMethod,
            int handlerParameterIndex,
            ImmutableArray<BoundExpression> outerArguments,
            int[] outerArgumentToParameterMap,
            BoundExpression[] convertedOuterArguments,
            Func<int, ExpressionSyntax> getOuterArgumentSyntax,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var handlerParameter = outerMethod.Parameters[handlerParameterIndex];
            var handlerParameterType = handlerParameter.Type;
            var handlerValueType = handlerParameterType is ByRefTypeSymbol byRef
                ? byRef.ElementType
                : handlerParameterType;

            if (handlerValueType is not NamedTypeSymbol handlerType)
            {
                var bad = new BoundBadExpression(interpolation);
                bad.SetType(handlerParameterType);
                return bad;
            }

            int literalLength = 0;
            int formattedCount = 0;
            for (int i = 0; i < interpolation.Contents.Count; i++)
            {
                if (interpolation.Contents[i] is InterpolatedStringTextSyntax text)
                {
                    var value = text.TextToken.Value as string ?? text.TextToken.ValueText ?? string.Empty;
                    literalLength += value.Length;
                }
                else if (interpolation.Contents[i] is InterpolationSyntax)
                {
                    formattedCount++;
                }
            }

            var intType = context.Compilation.GetSpecialType(SpecialType.System_Int32);
            var ctorArguments = ImmutableArray.CreateBuilder<BoundExpression>();
            var ctorArgumentSyntaxes = new List<ExpressionSyntax>();
            ctorArguments.Add(new BoundLiteralExpression(interpolation, intType, literalLength));
            ctorArgumentSyntaxes.Add(interpolation);
            ctorArguments.Add(new BoundLiteralExpression(interpolation, intType, formattedCount));
            ctorArgumentSyntaxes.Add(interpolation);

            var forwardedNames = GetInterpolatedStringHandlerArgumentNames(handlerParameter);
            for (int i = 0; i < forwardedNames.Length; i++)
            {
                string name = forwardedNames[i];
                if (name.Length == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INTERP_HANDLER001",
                        DiagnosticSeverity.Error,
                        "Interpolated string handler receiver arguments are not supported for this invocation form.",
                        new Location(context.SemanticModel.SyntaxTree, interpolation.Span)));
                    var bad = new BoundBadExpression(interpolation);
                    bad.SetType(handlerParameterType);
                    return bad;
                }

                int forwardedParameterIndex = -1;
                for (int p = 0; p < outerMethod.Parameters.Length; p++)
                {
                    if (string.Equals(outerMethod.Parameters[p].Name, name, StringComparison.Ordinal))
                    {
                        forwardedParameterIndex = p;
                        break;
                    }
                }

                if (forwardedParameterIndex < 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INTERP_HANDLER002",
                        DiagnosticSeverity.Error,
                        $"Interpolated string handler argument '{name}' does not name a parameter of '{outerMethod.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, interpolation.Span)));
                    var bad = new BoundBadExpression(interpolation);
                    bad.SetType(handlerParameterType);
                    return bad;
                }

                int forwardedArgumentIndex = -1;
                for (int a = 0; a < outerArgumentToParameterMap.Length; a++)
                {
                    if (outerArgumentToParameterMap[a] == forwardedParameterIndex)
                    {
                        forwardedArgumentIndex = a;
                        break;
                    }
                }

                BoundExpression forwarded;
                ExpressionSyntax forwardedSyntax;
                if (forwardedArgumentIndex >= 0)
                {
                    forwardedSyntax = getOuterArgumentSyntax(forwardedArgumentIndex);
                    forwarded = convertedOuterArguments[forwardedParameterIndex] ?? ApplyConversion(
                        forwardedSyntax,
                        outerArguments[forwardedArgumentIndex],
                        outerMethod.Parameters[forwardedParameterIndex].Type,
                        interpolation,
                        context,
                        diagnostics,
                        requireImplicit: true);
                }
                else if (outerMethod.Parameters[forwardedParameterIndex].HasExplicitDefault &&
                    outerMethod.Parameters[forwardedParameterIndex].DefaultValueOpt.HasValue)
                {
                    forwardedSyntax = interpolation;
                    var forwardedParameter = outerMethod.Parameters[forwardedParameterIndex];
                    forwarded = forwardedParameter.DefaultValueOpt.Value is null && forwardedParameter.Type.IsValueType
                        ? MakeDefaultValue(interpolation, forwardedParameter.Type)
                        : new BoundLiteralExpression(interpolation, forwardedParameter.Type, forwardedParameter.DefaultValueOpt.Value);
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INTERP_HANDLER003",
                        DiagnosticSeverity.Error,
                        $"Interpolated string handler argument '{name}' has no supplied value.",
                        new Location(context.SemanticModel.SyntaxTree, interpolation.Span)));
                    var bad = new BoundBadExpression(interpolation);
                    bad.SetType(handlerParameterType);
                    return bad;
                }

                ctorArguments.Add(forwarded);
                ctorArgumentSyntaxes.Add(forwardedSyntax);
            }

            var constructors = LookupConstructors(handlerType)
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();
            if (constructors.IsDefaultOrEmpty ||
                !TryResolveOverload(
                    constructors,
                    ctorArguments.ToImmutable(),
                    i => ctorArgumentSyntaxes[i],
                    out var constructor,
                    out var convertedCtorArguments,
                    context,
                    diagnostics,
                    interpolation,
                    getArgRefKindKeyword: null,
                    getArgName: null,
                    allowParamsExpansion: true))
            {
                if (constructors.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_INTERP_HANDLER004",
                        DiagnosticSeverity.Error,
                        $"Interpolated string handler type '{handlerType.Name}' has no accessible constructor.",
                        new Location(context.SemanticModel.SyntaxTree, interpolation.Span)));
                }

                var bad = new BoundBadExpression(interpolation);
                bad.SetType(handlerParameterType);
                return bad;
            }

            var handlerLocal = NewTemp("<handler$>", handlerType);
            var handlerLocalExpression = new BoundLocalExpression(interpolation, handlerLocal);
            var creation = new BoundObjectCreationExpression(
                interpolation,
                handlerType,
                constructor!,
                convertedCtorArguments);
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
            sideEffects.Add(new BoundExpressionStatement(
                interpolation,
                new BoundAssignmentExpression(interpolation, handlerLocalExpression, creation)));

            for (int i = 0; i < interpolation.Contents.Count; i++)
            {
                var content = interpolation.Contents[i];
                if (content is InterpolatedStringTextSyntax text)
                {
                    string textValue = text.TextToken.Value as string ?? text.TextToken.ValueText ?? string.Empty;
                    if (textValue.Length == 0)
                        continue;

                    var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);
                    var appendArguments = ImmutableArray.Create<BoundExpression>(
                        new BoundLiteralExpression(interpolation, stringType, textValue));
                    if (!TryBindInterpolatedStringHandlerAppendCall(
                        interpolation,
                        handlerType,
                        handlerLocal,
                        "AppendLiteral",
                        appendArguments,
                        ImmutableArray.Create<ExpressionSyntax>(interpolation),
                        context,
                        diagnostics,
                        out var appendCall))
                    {
                        var bad = new BoundBadExpression(interpolation);
                        bad.SetType(handlerParameterType);
                        return bad;
                    }

                    sideEffects.Add(new BoundExpressionStatement(content, appendCall));
                    continue;
                }

                if (content is not InterpolationSyntax formatted)
                    continue;

                var appendArgs = ImmutableArray.CreateBuilder<BoundExpression>();
                var appendSyntaxes = ImmutableArray.CreateBuilder<ExpressionSyntax>();
                var value = BindExpression(formatted.Expression, context, diagnostics);
                appendArgs.Add(value);
                appendSyntaxes.Add(formatted.Expression);

                if (formatted.AlignmentClause is not null)
                {
                    var alignment = BindExpression(formatted.AlignmentClause.Value, context, diagnostics);
                    appendArgs.Add(alignment);
                    appendSyntaxes.Add(formatted.AlignmentClause.Value);
                }

                if (formatted.FormatClause is not null)
                {
                    var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);
                    string format = formatted.FormatClause.FormatStringToken.Value as string
                        ?? formatted.FormatClause.FormatStringToken.ValueText
                        ?? string.Empty;
                    appendArgs.Add(new BoundLiteralExpression(interpolation, stringType, format));
                    appendSyntaxes.Add(interpolation);
                }

                if (!TryBindInterpolatedStringHandlerAppendCall(
                    formatted,
                    handlerType,
                    handlerLocal,
                    "AppendFormatted",
                    appendArgs.ToImmutable(),
                    appendSyntaxes.ToImmutable(),
                    context,
                    diagnostics,
                    out var formattedCall))
                {
                    var bad = new BoundBadExpression(interpolation);
                    bad.SetType(handlerParameterType);
                    return bad;
                }

                sideEffects.Add(new BoundExpressionStatement(formatted, formattedCall));
            }

            BoundExpression result = new BoundLocalExpression(interpolation, handlerLocal);
            if (handlerParameterType is ByRefTypeSymbol)
                result = new BoundRefExpression(interpolation, handlerParameterType, result);

            return new BoundSequenceExpression(
                interpolation,
                ImmutableArray.Create(handlerLocal),
                sideEffects.ToImmutable(),
                result);
        }

        private bool TryBindInterpolatedStringHandlerAppendCall(
            SyntaxNode syntax,
            NamedTypeSymbol handlerType,
            LocalSymbol handlerLocal,
            string methodName,
            ImmutableArray<BoundExpression> arguments,
            ImmutableArray<ExpressionSyntax> argumentSyntaxes,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression call)
        {
            call = null!;
            var candidates = LookupMethods(handlerType, methodName)
                .Where(m => !m.IsStatic && AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();
            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INTERP_HANDLER005",
                    DiagnosticSeverity.Error,
                    $"Interpolated string handler type '{handlerType.Name}' has no accessible instance method '{methodName}'.",
                    new Location(context.SemanticModel.SyntaxTree, syntax.Span)));
                return false;
            }

            if (!TryResolveOverload(
                candidates,
                arguments,
                i => argumentSyntaxes[i],
                out var chosen,
                out var convertedArguments,
                context,
                diagnostics,
                syntax,
                getArgRefKindKeyword: null,
                getArgName: null,
                allowParamsExpansion: true))
            {
                return false;
            }

            var receiver = PrepareReceiverForResolvedMemberCall(
                syntax,
                new BoundLocalExpression(syntax, handlerLocal),
                chosen!,
                context);
            call = new BoundCallExpression(syntax, receiver, chosen!, convertedArguments);
            return true;
        }

        private BoundExpression BindDelegateOrUnsupportedInvocation(
            InvocationExpressionSyntax inv,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiver = BindExpression(inv.Expression, context, diagnostics);
            if (!receiver.HasErrors && receiver.Type is FunctionPointerTypeSymbol functionPointerType)
                return BindFunctionPointerInvocation(inv, receiver, functionPointerType, argSyntaxes, args, context, diagnostics);
            if (!receiver.HasErrors && TryGetDelegateInvokeMethod(receiver.Type, out _, out _))
                return BindDelegateInvocation(inv, receiver, argSyntaxes, args, context, diagnostics);

            return BindUnsupportedInvocation(inv, context, diagnostics);
        }

        // Function pointer calls validate exact reference kinds before conversions
        private BoundExpression BindFunctionPointerInvocation(
            InvocationExpressionSyntax inv,
            BoundExpression receiver,
            FunctionPointerTypeSymbol functionPointerType,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            EnsureUnsafe(inv, context, diagnostics);

            if (args.Length != functionPointerType.Parameters.Length)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FNPTR_CALL001",
                    DiagnosticSeverity.Error,
                    $"Function pointer requires {functionPointerType.Parameters.Length} argument(s), but {args.Length} were supplied.",
                    new Location(context.SemanticModel.SyntaxTree, inv.ArgumentList.Span)));
                return new BoundBadExpression(inv);
            }

            var converted = ImmutableArray.CreateBuilder<BoundExpression>(args.Length);
            bool hasErrors = false;
            for (int i = 0; i < args.Length; i++)
            {
                var argumentSyntax = argSyntaxes[i];
                var parameter = functionPointerType.Parameters[i];
                if (argumentSyntax.NameColon is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_CALL002",
                        DiagnosticSeverity.Error,
                        "Function pointer arguments cannot be named.",
                        new Location(context.SemanticModel.SyntaxTree, argumentSyntax.NameColon.Span)));
                    hasErrors = true;
                }

                var actualRefKind = GetArgRefKind(argumentSyntax.RefKindKeyword);
                var expectedRefKind = parameter.RefKind switch
                {
                    FunctionPointerRefKind.Ref => ParameterRefKind.Ref,
                    FunctionPointerRefKind.Out => ParameterRefKind.Out,
                    FunctionPointerRefKind.In => ParameterRefKind.In,
                    FunctionPointerRefKind.RefReadOnly => ParameterRefKind.In,
                    _ => ParameterRefKind.None
                };

                bool isInputParameter = parameter.RefKind is FunctionPointerRefKind.In or FunctionPointerRefKind.RefReadOnly;
                bool refKindMatches = actualRefKind == expectedRefKind ||
                    isInputParameter && (actualRefKind == ParameterRefKind.None || actualRefKind == ParameterRefKind.Ref);
                if (!refKindMatches)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_CALL003",
                        DiagnosticSeverity.Error,
                        $"Argument {i + 1} must be passed with '{FormatFunctionPointerRefKind(parameter.RefKind)}'.",
                        new Location(context.SemanticModel.SyntaxTree, argumentSyntax.Span)));
                    converted.Add(args[i]);
                    hasErrors = true;
                    continue;
                }

                if (parameter.RefKind == FunctionPointerRefKind.In && actualRefKind == ParameterRefKind.Ref)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_CALL005",
                        DiagnosticSeverity.Warning,
                        $"Argument {i + 1} is passed with 'ref' to an 'in' function pointer parameter.",
                        new Location(context.SemanticModel.SyntaxTree, argumentSyntax.Span)));
                }
                else if (parameter.RefKind == FunctionPointerRefKind.RefReadOnly && actualRefKind == ParameterRefKind.None)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_CALL006",
                        DiagnosticSeverity.Warning,
                        $"Argument {i + 1} should be passed with 'in' or 'ref' to a 'ref readonly' function pointer parameter.",
                        new Location(context.SemanticModel.SyntaxTree, argumentSyntax.Span)));
                }

                if (parameter.RefKind == FunctionPointerRefKind.None)
                {
                    var value = ApplyConversion(
                        argumentSyntax.Expression,
                        args[i],
                        parameter.Type,
                        argumentSyntax.Expression,
                        context,
                        diagnostics,
                        requireImplicit: true);
                    converted.Add(value);
                    hasErrors |= value.HasErrors;
                    continue;
                }

                if (isInputParameter && actualRefKind == ParameterRefKind.None)
                {
                    var value = ApplyConversion(
                        argumentSyntax.Expression,
                        args[i],
                        parameter.Type,
                        argumentSyntax.Expression,
                        context,
                        diagnostics,
                        requireImplicit: true);
                    if (value.HasErrors)
                    {
                        converted.Add(value);
                        hasErrors = true;
                        continue;
                    }

                    var temp = NewTemp("<fnptr-in$>", parameter.Type);
                    var local = new BoundLocalExpression(argumentSyntax.Expression, temp);
                    var assignment = new BoundAssignmentExpression(argumentSyntax.Expression, local, value);
                    var reference = new BoundRefExpression(
                        argumentSyntax.Expression,
                        context.Compilation.CreateByRefType(parameter.Type),
                        new BoundLocalExpression(argumentSyntax.Expression, temp));
                    converted.Add(new BoundSequenceExpression(
                        argumentSyntax.Expression,
                        ImmutableArray.Create(temp),
                        ImmutableArray.Create<BoundStatement>(
                            new BoundExpressionStatement(argumentSyntax.Expression, assignment)),
                        reference));
                    continue;
                }

                if (args[i].Type is not ByRefTypeSymbol byRef || !AreSameType(byRef.ElementType, parameter.Type))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_CALL004",
                        DiagnosticSeverity.Error,
                        $"Argument {i + 1} must be a variable of type '{parameter.Type.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, argumentSyntax.Expression.Span)));
                    hasErrors = true;
                }
                converted.Add(args[i]);
            }

            if (hasErrors)
                return new BoundBadExpression(inv);

            return new BoundFunctionPointerInvocationExpression(inv, receiver, functionPointerType, converted.ToImmutable());
        }

        private static string FormatFunctionPointerRefKind(FunctionPointerRefKind refKind)
            => refKind switch
            {
                FunctionPointerRefKind.Ref => "ref",
                FunctionPointerRefKind.Out => "out",
                FunctionPointerRefKind.In => "in",
                FunctionPointerRefKind.RefReadOnly => "in",
                _ => "value"
            };

        private BoundExpression BindDelegateInvocation(
            InvocationExpressionSyntax inv,
            BoundExpression receiver,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (!TryGetDelegateInvokeMethod(receiver.Type, out var delegateType, out var invoke))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALL_DELEGATE001",
                    DiagnosticSeverity.Error,
                    $"Expression of type '{receiver.Type.Name}' is not invocable.",
                    new Location(context.SemanticModel.SyntaxTree, inv.Expression.Span)));
                return new BoundBadExpression(inv);
            }

            if (!TryResolveOverload(
                candidates: ImmutableArray.Create(invoke),
                args: args,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: inv))
            {
                return new BoundBadExpression(inv);
            }

            return new BoundCallExpression(inv, receiver, chosen!, convertedArgs);
        }

        private BoundExpression BindUnsupportedInvocation(InvocationExpressionSyntax inv, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_CALL000", DiagnosticSeverity.Error,
                $"Invocation target not supported: {inv.Expression.Kind}",
                new Location(context.SemanticModel.SyntaxTree, inv.Expression.Span)));
            return new BoundBadExpression(inv);
        }

        private BoundExpression BindSimpleInvocation(
            InvocationExpressionSyntax inv,
            IdentifierNameSyntax id,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = id.Identifier.ValueText ?? "";

            if (TryBindSimpleValue(id, out var simpleValue))
            {
                if (!simpleValue.HasErrors && simpleValue.Type is FunctionPointerTypeSymbol functionPointerType)
                    return BindFunctionPointerInvocation(inv, simpleValue, functionPointerType, argSyntaxes, args, context, diagnostics);
                if (!simpleValue.HasErrors && TryGetDelegateInvokeMethod(simpleValue.Type, out _, out _))
                    return BindDelegateInvocation(inv, simpleValue, argSyntaxes, args, context, diagnostics);

                diagnostics.Add(new Diagnostic("CN_CALL010", DiagnosticSeverity.Error,
                    $"Expression of type '{simpleValue.Type.Name}' is not invocable.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadExpression(inv);
            }

            if (!string.IsNullOrEmpty(name) &&
                TryBindUnqualifiedMember(id, name, BindValueKind.RValue, context, diagnostics, out var memberValue))
            {
                if (memberValue.HasErrors)
                    return new BoundBadExpression(inv);

                if (memberValue.Type is FunctionPointerTypeSymbol functionPointerType)
                    return BindFunctionPointerInvocation(inv, memberValue, functionPointerType, argSyntaxes, args, context, diagnostics);

                if (TryGetDelegateInvokeMethod(memberValue.Type, out _, out _))
                    return BindDelegateInvocation(inv, memberValue, argSyntaxes, args, context, diagnostics);

                diagnostics.Add(new Diagnostic("CN_CALL010", DiagnosticSeverity.Error,
                    $"Expression of type '{memberValue.Type.Name}' is not invocable.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadExpression(inv);
            }

            if (!string.IsNullOrEmpty(name) &&
                TryBindImportedStaticMember(id, name, BindValueKind.RValue, context, diagnostics, out var importedStaticMemberValue))
            {
                if (importedStaticMemberValue.HasErrors)
                    return new BoundBadExpression(inv);

                if (importedStaticMemberValue.Type is FunctionPointerTypeSymbol functionPointerType)
                    return BindFunctionPointerInvocation(inv, importedStaticMemberValue, functionPointerType, argSyntaxes, args, context, diagnostics);

                if (TryGetDelegateInvokeMethod(importedStaticMemberValue.Type, out _, out _))
                    return BindDelegateInvocation(inv, importedStaticMemberValue, argSyntaxes, args, context, diagnostics);

                diagnostics.Add(new Diagnostic("CN_CALL010", DiagnosticSeverity.Error,
                    $"Expression of type '{importedStaticMemberValue.Type.Name}' is not invocable.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadExpression(inv);
            }

            // Local function invocation
            if (TryGetLocalFunctionFromEnclosingScopes(name, out var localFunc) && localFunc != null)
            {
                var candidates = ImmutableArray.Create<MethodSymbol>(localFunc);
                if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }

                return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
            }
            {
                var methodCtx = context.ContainingSymbol as MethodSymbol;
                var containingType = methodCtx?.ContainingSymbol as NamedTypeSymbol;
                if (containingType is null)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL011", DiagnosticSeverity.Error,
                        "Cannot bind an unqualified invocation without an enclosing type.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }

                var candidates = LookupMethods(containingType, name);
                if (methodCtx != null && methodCtx.IsStatic)
                    candidates = candidates.Where(m => m.IsStatic).ToImmutableArray();
                bool fromUsingStatic = false;
                if (candidates.IsDefaultOrEmpty)
                {
                    candidates = LookupImportedStaticMethods(name, context);
                    fromUsingStatic = !candidates.IsDefaultOrEmpty;
                }
                if (candidates.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL012", DiagnosticSeverity.Error,
                        $"No method '{name}' found in type '{containingType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }
                if (!fromUsingStatic)
                {
                    candidates = candidates
                    .Where(m => AccessibilityHelper.IsAccessible(m, context))
                    .ToImmutableArray();
                }
                if (candidates.IsDefaultOrEmpty)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CALL_ACC001",
                        DiagnosticSeverity.Error,
                        $"No accessible overload of '{name}' found in type '{containingType.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }
                if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }

                if (methodCtx != null && methodCtx.IsStatic && chosen is { IsStatic: false })
                {
                    diagnostics.Add(new Diagnostic("CN_CALL013", DiagnosticSeverity.Error,
                        "An object reference is required for the non-static method.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));
                    return new BoundBadExpression(inv);
                }

                // A null receiver represents implicit this for instance methods
                return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
            }
        }
        private BoundExpression BindMemberAccessInvocation(
            InvocationExpressionSyntax inv,
            MemberAccessExpressionSyntax ma,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(ma.Name);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_CALL020", DiagnosticSeverity.Error,
                    "Invalid member name in invocation.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(inv);
            }

            bool isPointerAccess = ma.Kind == SyntaxKind.PointerMemberAccessExpression;

            BoundExpression? receiverValue;
            NamedTypeSymbol? receiverType;
            TypeParameterSymbol? receiverTypeParameter = null;

            if (!TryBindReceiverForMemberAccess(ma.Expression, isPointerAccess, out receiverValue, out receiverType, context, diagnostics))
            {
                var probeContext = WithRecorder(context, NullBindingRecorder.Instance);
                var probeSymbol = BindNamespaceOrType(ma.Expression, probeContext, new DiagnosticBag());
                if (probeSymbol is null)
                {
                    if (ma.Expression is IdentifierNameSyntax or GenericNameSyntax)
                        BindExpression(ma.Expression, context, diagnostics);
                    else
                        BindNamespaceOrType(ma.Expression, context, diagnostics);
                    return new BoundBadExpression(inv);
                }

                var sym = BindNamespaceOrType(ma.Expression, context, diagnostics);
                receiverType = sym as NamedTypeSymbol;
                receiverTypeParameter = sym as TypeParameterSymbol;
                receiverValue = null;
            }

            if (receiverValue?.Type is TypeParameterSymbol valueTypeParameter)
                receiverTypeParameter = valueTypeParameter;

            bool receiverMayAlsoBeIdenticalType =
                ReceiverMayAlsoBeIdenticalTypeName(ma.Expression, receiverValue, context);

            if (receiverType is null && receiverTypeParameter is null)
            {
                diagnostics.Add(new Diagnostic("CN_CALL021", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                return new BoundBadExpression(inv);
            }

            var candidates = receiverTypeParameter is not null
                ? receiverValue is null
                    ? LookupMethods(receiverTypeParameter, name)
                    : LookupInstanceMethods(receiverTypeParameter, name, context)
                : LookupMethods(receiverType!, name);
            if (candidates.IsDefaultOrEmpty && receiverTypeParameter is null && receiverValue is not null && receiverType!.TypeKind == TypeKind.Interface)
            {
                var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object) as NamedTypeSymbol;
                if (objectType is not null)
                    candidates = LookupMethods(objectType, name);
            }
            if (receiverValue is null)
            {
                candidates = candidates.Where(m => m.IsStatic).ToImmutableArray();
            }
            else if (!receiverMayAlsoBeIdenticalType)
            {
                candidates = candidates.Where(m => !m.IsStatic).ToImmutableArray();
            }

            bool methodFound = !candidates.IsDefaultOrEmpty;

            candidates = candidates
                .Where(m => AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (methodFound && candidates.IsDefaultOrEmpty) // A matching method exists but is inaccessible
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALL_ACC002",
                    DiagnosticSeverity.Error,
                    $"No accessible overload of '{name}' found on type '{receiverTypeParameter?.Name ?? receiverType!.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(inv);
            }
            if (!candidates.IsDefaultOrEmpty)
            {
                if (ma.Name is GenericNameSyntax gName)
                {
                    var explicitTypeArgs = BindTypeArguments(gName.TypeArgumentList.Arguments, context, diagnostics);
                    var arity = explicitTypeArgs.Length;

                    var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
                    if (arityMatches.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_CALLG010",
                            DiagnosticSeverity.Error,
                            $"No overload of '{name}' has {arity} type parameter(s).",
                            new Location(context.SemanticModel.SyntaxTree, gName.Span)));
                        return new BoundBadExpression(inv);
                    }

                    var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
                    for (int i = 0; i < arityMatches.Length; i++)
                    {
                        var def = arityMatches[i];
                        if (!GenericConstraintChecker.CheckMethodInstantiation(
                            methodDefinition: def,
                            typeArguments: explicitTypeArgs,
                            getArgSpan: a => gName.TypeArgumentList.Arguments[a].Span,
                            context: context,
                            diagnostics: diagnostics))
                        {
                            continue;
                        }

                        constructed.Add(new ConstructedMethodSymbol(def, explicitTypeArgs, context.Compilation.TypeManager));
                    }

                    candidates = constructed.ToImmutable();
                }
                else
                {
                    // Generic methods without explicit type arguments are handled by overload resolution
                }
                if (TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    var selectedReceiver = chosen!.IsStatic && receiverMayAlsoBeIdenticalType
                        ? null
                        : receiverValue;
                    var callReceiver = PrepareReceiverForResolvedMemberCall(
                        ma.Expression,
                        selectedReceiver,
                        chosen,
                        context);
                    return new BoundCallExpression(inv, receiverOpt: callReceiver, method: chosen!, arguments: convertedArgs);
                }
                return new BoundBadExpression(inv);
            }
            // Extension methods
            if (receiverValue is not null)
            {
                var extensionCandidates = LookupExtensionMethods(name, receiverValue, context);

                if (!extensionCandidates.IsDefaultOrEmpty)
                {
                    var extArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(args.Length + 1);
                    extArgsBuilder.Add(receiverValue);
                    extArgsBuilder.AddRange(args);
                    var extArgs = extArgsBuilder.ToImmutable();

                    if (TryResolveOverload(
                        candidates: extensionCandidates,
                        args: extArgs,
                        getArgExprSyntax: i => i == 0 ? ma.Expression : argSyntaxes[i - 1].Expression,
                        getArgRefKindKeyword: i => i == 0 ? null : argSyntaxes[i - 1].RefKindKeyword,
                        getArgName: i => i == 0 ? null : argSyntaxes[i - 1].NameColon?.Name.Identifier.ValueText,
                        chosen: out var chosen,
                        convertedArgs: out var convertedArgs,
                        context: context,
                        diagnostics: diagnostics,
                        diagnosticNode: inv))
                    {
                        return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
                    }

                    return new BoundBadExpression(inv);
                }
            }

            {
                var probeDiagnostics = new DiagnosticBag();
                var probeContext = WithRecorder(context, NullBindingRecorder.Instance);
                var probeValue = BindMemberAccess(ma, BindValueKind.RValue, probeContext, probeDiagnostics);
                if (!probeValue.HasErrors && probeValue.Type is FunctionPointerTypeSymbol functionPointerType)
                {
                    var functionPointerValue = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                    return BindFunctionPointerInvocation(inv, functionPointerValue, functionPointerType, argSyntaxes, args, context, diagnostics);
                }
                if (!probeValue.HasErrors && TryGetDelegateInvokeMethod(probeValue.Type, out _, out _))
                {
                    var delegateValue = BindMemberAccess(ma, BindValueKind.RValue, context, diagnostics);
                    return BindDelegateInvocation(inv, delegateValue, argSyntaxes, args, context, diagnostics);
                }
            }

            diagnostics.Add(new Diagnostic("CN_CALL022", DiagnosticSeverity.Error,
                $"No method '{name}' found on type '{receiverTypeParameter?.Name ?? receiverType!.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));

            return new BoundBadExpression(inv);
        }
        // Normalize static and instance receivers after a method candidate is selected
        private BoundExpression? PrepareReceiverForResolvedMemberCall(
            SyntaxNode syntax, BoundExpression? receiver, MethodSymbol method, BindingContext context)
        {
            if (receiver is null)
                return null;

            if (receiver.Type is not TypeParameterSymbol tp ||
                (tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) != 0)
            {
                return receiver;
            }

            if (method.ContainingSymbol is not NamedTypeSymbol owner ||
                owner.SpecialType != SpecialType.System_Object)
            {
                return receiver;
            }

            var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object);
            return new BoundConversionExpression(
                syntax,
                objectType,
                receiver,
                new Conversion(ConversionKind.Boxing),
                isChecked: false);
        }
        private BoundExpression BindGenericSimpleInvocation(
            InvocationExpressionSyntax inv,
            GenericNameSyntax nameSyntax,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = nameSyntax.Identifier.ValueText ?? "";
            var explicitTypeArgs = BindTypeArguments(nameSyntax.TypeArgumentList.Arguments, context, diagnostics);

            if (TryGetLocalFunctionFromEnclosingScopes(name, out var localFunc) && localFunc != null)
            {
                var localArity = explicitTypeArgs.Length;
                if (localFunc.TypeParameters.Length != localArity)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_CALLG001",
                        DiagnosticSeverity.Error,
                        $"Local function '{name}' has {localFunc.TypeParameters.Length} type parameter(s), but {localArity} type argument(s) were supplied.",
                        new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                    return new BoundBadExpression(inv);
                }

                if (!GenericConstraintChecker.CheckMethodInstantiation(
                    methodDefinition: localFunc,
                    typeArguments: explicitTypeArgs,
                    getArgSpan: a => nameSyntax.TypeArgumentList.Arguments[a].Span,
                    context: context,
                    diagnostics: diagnostics))
                {
                    return new BoundBadExpression(inv);
                }

                var constructedLocal = new ConstructedMethodSymbol(
                    localFunc,
                    explicitTypeArgs,
                    context.Compilation.TypeManager);

                if (!TryResolveOverload(
                    candidates: ImmutableArray.Create<MethodSymbol>(constructedLocal),
                    args: args,
                    getArgExprSyntax: i => argSyntaxes[i].Expression,
                    getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                    getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                    chosen: out var localChosen,
                    convertedArgs: out var localConvertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: inv))
                {
                    return new BoundBadExpression(inv);
                }

                return new BoundCallExpression(inv, receiverOpt: null, method: localChosen!, arguments: localConvertedArgs);
            }

            var methodCtx = context.ContainingSymbol as MethodSymbol;
            var containingType = methodCtx?.ContainingSymbol as NamedTypeSymbol;
            if (containingType is null)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG002", DiagnosticSeverity.Error,
                    "Cannot bind an unqualified invocation without an enclosing type.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            var candidates = LookupMethods(containingType, name);
            if (methodCtx != null && methodCtx.IsStatic)
                candidates = candidates.Where(m => m.IsStatic).ToImmutableArray();
            bool fromUsingStatic = false;
            if (candidates.IsDefaultOrEmpty)
            {
                candidates = LookupImportedStaticMethods(name, context);
                fromUsingStatic = !candidates.IsDefaultOrEmpty;
            }
            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG003", DiagnosticSeverity.Error,
                    $"No method '{name}' found in type '{containingType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }
            if (!fromUsingStatic)
            {
                candidates = candidates
                    .Where(m => AccessibilityHelper.IsAccessible(m, context))
                    .ToImmutableArray();
            }

            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CALL_ACC003",
                    DiagnosticSeverity.Error,
                    $"No accessible overload of '{name}' found in type '{containingType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }
            var arity = explicitTypeArgs.Length;
            var arityMatches = candidates.Where(m => m.TypeParameters.Length == arity).ToImmutableArray();
            if (arityMatches.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_CALLG004", DiagnosticSeverity.Error,
                    $"No overload of '{name}' has {arity} type parameter(s).",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            var constructed = ImmutableArray.CreateBuilder<MethodSymbol>(arityMatches.Length);
            for (int i = 0; i < arityMatches.Length; i++)
                constructed.Add(new ConstructedMethodSymbol(arityMatches[i], explicitTypeArgs, context.Compilation.TypeManager));

            if (!TryResolveOverload(
                candidates: constructed.ToImmutable(),
                args: args,
                getArgExprSyntax: i => argSyntaxes[i].Expression,
                getArgRefKindKeyword: i => argSyntaxes[i].RefKindKeyword,
                getArgName: i => argSyntaxes[i].NameColon?.Name.Identifier.ValueText,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: inv))
            {
                return new BoundBadExpression(inv);
            }

            if (methodCtx != null && methodCtx.IsStatic && chosen is { IsStatic: false })
            {
                diagnostics.Add(new Diagnostic("CN_CALLG005", DiagnosticSeverity.Error,
                    "An object reference is required for the non-static method.",
                    new Location(context.SemanticModel.SyntaxTree, nameSyntax.Span)));
                return new BoundBadExpression(inv);
            }

            // A null receiver represents implicit this for instance methods
            return new BoundCallExpression(inv, receiverOpt: null, method: chosen!, arguments: convertedArgs);
        }
        private bool ReceiverMayAlsoBeIdenticalTypeName(
            ExpressionSyntax receiverSyntax,
            BoundExpression? receiverValue,
            BindingContext context)
        {
            if (receiverValue is null || receiverSyntax is not IdentifierNameSyntax)
                return false;

            var probeContext = WithRecorder(context, NullBindingRecorder.Instance);
            var typeMeaning = BindNamespaceOrType(receiverSyntax, probeContext, new DiagnosticBag()) as TypeSymbol;
            return typeMeaning is not null && AreSameType(receiverValue.Type, typeMeaning);
        }

        private bool TryBindReceiverForMemberAccess(
            ExpressionSyntax receiverSyntax,
            bool isPointerAccess,
            out BoundExpression? receiverValue,
            out NamedTypeSymbol? receiverType,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            receiverValue = null;
            receiverType = null;

            if (isPointerAccess)
            {
                EnsureUnsafe(receiverSyntax, context, diagnostics);

                // Pointer member access is always a value receiver
                var ptrValue = receiverSyntax is IdentifierNameSyntax id && TryBindSimpleValue(id, out var v)
                    ? v
                    : BindExpression(receiverSyntax, context, diagnostics);

                if (ptrValue.Type is not PointerTypeSymbol pt)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL023", DiagnosticSeverity.Error,
                        "The receiver of '->' must be a pointer.",
                        new Location(context.SemanticModel.SyntaxTree, receiverSyntax.Span)));
                    receiverValue = new BoundBadExpression(receiverSyntax);
                    return true;
                }

                var pointedAt = pt.PointedAtType;
                receiverType = GetReceiverTypeForMemberLookup(pointedAt, context);
                if (receiverType is null)
                {
                    diagnostics.Add(new Diagnostic("CN_CALL024", DiagnosticSeverity.Error,
                        $"The receiver type '{pointedAt.Name}' does not support member lookup.",
                        new Location(context.SemanticModel.SyntaxTree, receiverSyntax.Span)));
                    receiverValue = new BoundBadExpression(receiverSyntax);
                    return true;
                }

                receiverValue = new BoundPointerIndirectionExpression(receiverSyntax, pointedAt, ptrValue);
                return true;
            }
            if (receiverSyntax is IdentifierNameSyntax rid)
            {
                if (TryBindSimpleValue(rid, out var simple))
                {
                    receiverValue = simple;
                    receiverType = GetReceiverTypeForMemberLookup(simple.Type, context);
                    return true;
                }
                var name = rid.Identifier.ValueText ?? "";
                if (!string.IsNullOrEmpty(name) &&
                    TryBindUnqualifiedMember(rid, name, BindValueKind.RValue, context, diagnostics, out var memberExpr))
                {
                    receiverValue = memberExpr;
                    receiverType = GetReceiverTypeForMemberLookup(memberExpr.Type, context);
                    return true;
                }
                if (!string.IsNullOrEmpty(name) &&
                    TryBindImportedStaticMember(rid, name, BindValueKind.RValue, context, diagnostics, out var staticMemberExpr))
                {
                    receiverValue = staticMemberExpr;
                    receiverType = GetReceiverTypeForMemberLookup(staticMemberExpr.Type, context);
                    return true;
                }
                return false;
            }
            if (receiverSyntax is GenericNameSyntax)
                return false;
            if (receiverSyntax is MemberAccessExpressionSyntax)
            {
                var tmpDiagnostics = new DiagnosticBag();
                var tmpContext = WithRecorder(context, NullBindingRecorder.Instance);

                var tmpValue = BindExpression(receiverSyntax, tmpContext, tmpDiagnostics);
                if (!tmpValue.HasErrors)
                {
                    var tmpReceiverType = GetReceiverTypeForMemberLookup(tmpValue.Type, context);
                    if (tmpReceiverType is not null)
                    {
                        receiverValue = tmpValue;
                        receiverType = tmpReceiverType;
                        return true;
                    }
                }
                return false;
            }
            if (receiverSyntax is PredefinedTypeSyntax pts)
            {
                receiverValue = null;
                receiverType = BindType(pts, context, diagnostics) as NamedTypeSymbol;
                return true;
            }
            if (receiverSyntax is BaseExpressionSyntax bs)
            {
                receiverValue = BindBase(bs, context, diagnostics);

                if (receiverValue is BoundBaseExpression bb)
                {
                    receiverType = bb.BaseType;
                    return true;
                }

                receiverType = GetReceiverTypeForMemberLookup(receiverValue.Type, context);
                return true;
            }

            receiverValue = BindExpression(receiverSyntax, context, diagnostics);
            receiverType = GetReceiverTypeForMemberLookup(receiverValue.Type, context);
            return true;
        }

        private BoundExpression BindMemberAccess(
            MemberAccessExpressionSyntax ma,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var name = GetSimpleName(ma.Name);
            if (string.IsNullOrEmpty(name))
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC000", DiagnosticSeverity.Error,
                    "Invalid member name in member access.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            bool isPointerAccess = ma.Kind == SyntaxKind.PointerMemberAccessExpression;
            BoundExpression? receiverValue;
            NamedTypeSymbol? receiverType;
            if (!TryBindReceiverForMemberAccess(
                ma.Expression,
                isPointerAccess,
                out receiverValue,
                out receiverType,
                context,
                diagnostics))
            {
                var probeContext = WithRecorder(context, NullBindingRecorder.Instance);
                var probeSymbol = BindNamespaceOrType(ma.Expression, probeContext, new DiagnosticBag());
                if (probeSymbol is null)
                {
                    if (ma.Expression is IdentifierNameSyntax or GenericNameSyntax)
                        BindExpression(ma.Expression, context, diagnostics);
                    else
                        BindNamespaceOrType(ma.Expression, context, diagnostics);
                    return new BoundBadExpression(ma);
                }

                var sym = BindNamespaceOrType(ma.Expression, context, diagnostics);
                receiverType = sym as NamedTypeSymbol;
                receiverValue = null;
            }
            bool receiverMayAlsoBeIdenticalType =
                ReceiverMayAlsoBeIdenticalTypeName(ma.Expression, receiverValue, context);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC001", DiagnosticSeverity.Error,
                    "Receiver is not a type or a value with members.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                return new BoundBadExpression(ma);
            }
            var members = LookupMembers(receiverType, name);
            if (members.IsDefaultOrEmpty && receiverValue is not null && receiverType.TypeKind == TypeKind.Interface)
            {
                var objectType = context.Compilation.GetSpecialType(SpecialType.System_Object) as NamedTypeSymbol;
                if (objectType is not null)
                    members = LookupMembers(objectType, name);
            }
            if (members.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC002", DiagnosticSeverity.Error,
                    $"No member '{name}' found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            var accessibleMembers = FilterAccessibleMembers(members, context);
            if (accessibleMembers.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ACC002",
                    DiagnosticSeverity.Error,
                    $"Member '{name}' is inaccessible due to its protection level.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));

                return new BoundBadExpression(ma);
            }

            members = accessibleMembers;
            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool hasMethod = false;
            bool hasType = false;
            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol f:
                        field ??= f;
                        break;
                    case PropertySymbol p:
                        prop ??= p;
                        break;
                    case MethodSymbol:
                        hasMethod = true;
                        break;
                    case NamedTypeSymbol:
                        hasType = true;
                        break;
                }
            }

            if (field is null && prop is null)
            {
                if (hasMethod)
                {
                    var methodBuilder = ImmutableArray.CreateBuilder<MethodSymbol>();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is not MethodSymbol method)
                            continue;

                        if (receiverValue is null)
                        {
                            if (method.IsStatic)
                                methodBuilder.Add(method);
                        }
                        else
                        {
                            if (!method.IsStatic)
                                methodBuilder.Add(method);
                        }
                    }

                    var methods = ApplyExplicitTypeArgumentsToMethodGroup(
                        ma.Name,
                        methodBuilder.ToImmutable(),
                        context,
                        diagnostics,
                        out bool hadArityMatch);

                    if (methods.IsDefaultOrEmpty)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_MEMACC_MG001",
                            DiagnosticSeverity.Error,
                            hadArityMatch
                                ? $"No overload of '{name}' satisfies the supplied type arguments or receiver kind."
                                : $"No overload of '{name}' has the supplied number of type arguments.",
                            new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));

                        return new BoundBadExpression(ma);
                    }

                    return new BoundMethodGroupExpression(ma, name, receiverValue, methods);
                }
                if (hasType)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC004", DiagnosticSeverity.Error,
                        "A type name is not a value.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                diagnostics.Add(new Diagnostic("CN_MEMACC005", DiagnosticSeverity.Error,
                    "Member access does not resolve to a value member.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            if (field is not null && prop is not null)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC006", DiagnosticSeverity.Error,
                    $"Member name '{name}' is ambiguous between a field and a property.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Name.Span)));
                return new BoundBadExpression(ma);
            }
            // Bind the selected field
            if (field is not null)
            {
                if (valueKind == BindValueKind.LValue && field.IsConst)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC010", DiagnosticSeverity.Error,
                        "Cannot assign to a const field.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool isStatic = field.IsStatic;
                if (receiverValue is null)
                {
                    if (!isStatic)
                    {
                        diagnostics.Add(new Diagnostic("CN_MEMACC011", DiagnosticSeverity.Error,
                            "An object reference is required for the non-static field.",
                            new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                        return new BoundBadExpression(ma);
                    }
                }
                else
                {
                    if (isStatic)
                    {
                        if (!receiverMayAlsoBeIdenticalType)
                        {
                            diagnostics.Add(new Diagnostic("CN_MEMACC012", DiagnosticSeverity.Error,
                                "A static field cannot be accessed with an instance reference.",
                                new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                        }
                        receiverValue = null;
                    }
                }
                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = GetFieldValueType(field);

                if (valueKind == BindValueKind.LValue &&
                    !field.IsStatic &&
                    IsReadOnlyValueReceiver(receiverValue, context) &&
                    !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiverValue, context);

                if (valueKind == BindValueKind.LValue && field.IsReadOnly && !allowCtorReadonlyWrite && !isRefField)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC013", DiagnosticSeverity.Error,
                        "Cannot assign to a readonly field except in a constructor of the same type.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);

                var cv = field.IsConst ? field.ConstantValueOpt : Optional<object>.None;
                return new BoundMemberAccessExpression(
                    ma,
                    receiverValue,
                    field,
                    fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: cv);
            }
            // Bind the selected property
            if (prop is null)
                return new BoundBadExpression(ma);
            bool canReadProperty =
                prop.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);
            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC020", DiagnosticSeverity.Error,
                    "Property has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, receiverValue, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic("CN_MEMACC021", DiagnosticSeverity.Error,
                    "Property has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            bool propIsStatic = prop.IsStatic;
            if (receiverValue is null)
            {
                if (!propIsStatic)
                {
                    diagnostics.Add(new Diagnostic("CN_MEMACC022", DiagnosticSeverity.Error,
                        "An object reference is required for the non-static property.",
                        new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                    return new BoundBadExpression(ma);
                }
            }
            else
            {
                if (propIsStatic)
                {
                    if (!receiverMayAlsoBeIdenticalType)
                    {
                        diagnostics.Add(new Diagnostic("CN_MEMACC023", DiagnosticSeverity.Error,
                            "A static property cannot be accessed with an instance reference.",
                            new Location(context.SemanticModel.SyntaxTree, ma.Expression.Span)));
                    }
                    receiverValue = null;
                }
            }
            if (valueKind == BindValueKind.LValue &&
                !prop.IsStatic &&
                IsReadOnlyValueReceiver(receiverValue, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, ma.Span)));
                return new BoundBadExpression(ma);
            }
            return new BoundMemberAccessExpression(
                ma,
                receiverValue,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);
        }
        private TypeSymbol GetFieldValueType(FieldSymbol field)
        {
            if ((Flags & BinderFlags.EnumMemberInitializer) != 0 &&
                field.IsConst &&
                field.ContainingSymbol is NamedTypeSymbol enumType &&
                enumType.TypeKind == TypeKind.Enum)
            {
                return enumType.EnumUnderlyingType ?? field.Type;
            }

            return field.Type is ByRefTypeSymbol byRef ? byRef.ElementType : field.Type;
        }

        private static NamedTypeSymbol? GetReceiverTypeForMemberLookup(TypeSymbol type)
        {
            if (type is NamedTypeSymbol nt)
                return nt;

            if (type is ArrayTypeSymbol at)
                return at.BaseType as NamedTypeSymbol;

            return null;
        }
        private static NamedTypeSymbol? GetReceiverTypeForMemberLookup(TypeSymbol type, BindingContext context)
        {
            var receiverType = GetReceiverTypeForMemberLookup(type);
            if (receiverType is not null)
                return receiverType;

            if (type is TypeParameterSymbol tp &&
                (tp.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) == 0)
            {
                return context.Compilation.GetSpecialType(SpecialType.System_Object);
            }

            return null;
        }
        private static ImmutableArray<Symbol> FilterAccessibleMembers(
            ImmutableArray<Symbol> members, BindingContext context)
        {
            if (members.IsDefaultOrEmpty)
                return members;

            var b = ImmutableArray.CreateBuilder<Symbol>(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (AccessibilityHelper.IsAccessible(m, context))
                    b.Add(m);
            }

            return b.Count == members.Length ? members : b.ToImmutable();
        }
        private IEnumerable<NamedTypeSymbol> EnumerateExtensionContainerTypes(BindingContext context)
        {
            var imports = GetImports(context);
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);

            // Search statically imported types
            for (int i = 0; i < imports.StaticTypes.Length; i++)
            {
                var t = imports.StaticTypes[i];
                if (seen.Add(t))
                    yield return t;
            }

            // Search imported namespace and type containers
            for (int i = 0; i < imports.Containers.Length; i++)
            {
                switch (imports.Containers[i])
                {
                    case NamedTypeSymbol nt:
                        if (seen.Add(nt))
                            yield return nt;
                        break;

                    case NamespaceSymbol ns:
                        {
                            var types = ns.GetTypeMembers();
                            for (int j = 0; j < types.Length; j++)
                                if (seen.Add(types[j]))
                                    yield return types[j];
                            break;
                        }
                }
            }

            // Search the enclosing type namespace
            for (Symbol? s = context.ContainingSymbol; s is not null; s = s.ContainingSymbol)
            {
                if (s is NamespaceSymbol curNs)
                {
                    var types = curNs.GetTypeMembers();
                    for (int j = 0; j < types.Length; j++)
                        if (seen.Add(types[j]))
                            yield return types[j];
                    break;
                }
            }
        }
        // Extension lookup preserves import scope order before overload resolution
        private ImmutableArray<MethodSymbol> LookupExtensionMethods(
            string name,
            BoundExpression receiver,
            BindingContext context)
        {
            var b = ImmutableArray.CreateBuilder<MethodSymbol>();

            foreach (var containerType in EnumerateExtensionContainerTypes(context))
            {
                var methods = LookupMethods(containerType, name);
                if (methods.IsDefaultOrEmpty)
                    continue;

                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (!IsExtensionMethod(m))
                        continue;

                    if (!AccessibilityHelper.IsAccessible(m, context))
                        continue;

                    if (m.Parameters.Length == 0)
                        continue;

                    if (!m.TypeParameters.IsDefaultOrEmpty)
                    {
                        b.Add(m);
                        continue;
                    }

                    var firstParamType = m.Parameters[0].Type;
                    var conv = ClassifyConversion(receiver, firstParamType, context);
                    if (!conv.Exists || !conv.IsImplicit)
                        continue;

                    b.Add(m);
                }
            }

            return b.Count == 0 ? ImmutableArray<MethodSymbol>.Empty : b.ToImmutable();
        }
        private static ImmutableArray<Symbol> LookupMembers(NamedTypeSymbol type, string name)
        {
            var b = ImmutableArray.CreateBuilder<Symbol>();

            if (type.TypeKind == TypeKind.Interface)
            {
                foreach (var t in EnumerateInterfaceClosure(type))
                {
                    var all = t.GetMembers();
                    for (int i = 0; i < all.Length; i++)
                    {
                        var m = all[i];
                        if (!StringComparer.Ordinal.Equals(m.Name, name))
                            continue;

                        if (m is MethodSymbol ms && ms.ExplicitInterfaceImplementation is not null)
                            continue;
                        if (m is PropertySymbol ps && ps.ExplicitInterfaceImplementation is not null)
                            continue;

                        b.Add(m);
                    }
                }

                return b.ToImmutable();
            }

            for (NamedTypeSymbol? t = type; t != null; t = t.BaseType as NamedTypeSymbol)
            {
                var all = t.GetMembers();
                for (int i = 0; i < all.Length; i++)
                {
                    var m = all[i];
                    if (!StringComparer.Ordinal.Equals(m.Name, name))
                        continue;

                    if (m is MethodSymbol ms && ms.ExplicitInterfaceImplementation is not null)
                        continue;
                    if (m is PropertySymbol ps && ps.ExplicitInterfaceImplementation is not null)
                        continue;

                    b.Add(m);
                }

                if (b.Count != 0)
                    return b.ToImmutable();
            }

            return ImmutableArray<Symbol>.Empty;
        }
        private static IEnumerable<NamedTypeSymbol> EnumerateInterfaceClosure(NamedTypeSymbol root)
        {
            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var queue = new Queue<NamedTypeSymbol>();
            queue.Enqueue(root);

            while (queue.Count != 0)
            {
                var cur = queue.Dequeue();
                if (!seen.Add(cur))
                    continue;

                yield return cur;

                var ifaces = cur.Interfaces;
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i] is NamedTypeSymbol nt && nt.TypeKind == TypeKind.Interface)
                        queue.Enqueue(nt);
                }
            }
        }
        private static ImmutableArray<MethodSymbol> LookupInstanceMethods(
            TypeParameterSymbol typeParameter,
            string name,
            BindingContext context)
        {
            var builder = ImmutableArray.CreateBuilder<MethodSymbol>();
            var constrainedMethods = LookupMethods(typeParameter, name);
            for (int i = 0; i < constrainedMethods.Length; i++)
            {
                var method = constrainedMethods[i];
                if (method.IsStatic)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < builder.Count; j++)
                {
                    if (SameSignature(builder[j], method))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    builder.Add(method);
            }

            if ((typeParameter.GenericConstraint & GenericConstraintsFlags.AllowsRefStruct) == 0 &&
                context.Compilation.GetSpecialType(SpecialType.System_Object) is NamedTypeSymbol objectType)
            {
                var objectMethods = LookupMethods(objectType, name);
                for (int i = 0; i < objectMethods.Length; i++)
                {
                    var method = objectMethods[i];
                    if (method.IsStatic)
                        continue;

                    bool duplicate = false;
                    for (int j = 0; j < builder.Count; j++)
                    {
                        if (SameSignature(builder[j], method))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        builder.Add(method);
                }
            }

            return builder.ToImmutable();
        }
        private static ImmutableArray<MethodSymbol> LookupMethods(TypeParameterSymbol typeParameter, string name)
        {
            var builder = ImmutableArray.CreateBuilder<MethodSymbol>();
            var visited = new HashSet<TypeParameterSymbol>(ReferenceEqualityComparer<TypeParameterSymbol>.Instance);
            AddConstraintMethods(typeParameter, name, builder, visited);
            return builder.ToImmutable();
        }
        private static void AddConstraintMethods(
            TypeParameterSymbol typeParameter,
            string name,
            ImmutableArray<MethodSymbol>.Builder builder,
            HashSet<TypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
                return;

            var constraints = typeParameter.ConstraintTypes;
            for (int i = 0; i < constraints.Length; i++)
            {
                ImmutableArray<MethodSymbol> methods;
                if (constraints[i] is NamedTypeSymbol constraintType)
                {
                    methods = LookupMethods(constraintType, name);
                }
                else if (constraints[i] is TypeParameterSymbol constrainedTypeParameter)
                {
                    AddConstraintMethods(constrainedTypeParameter, name, builder, visited);
                    continue;
                }
                else
                {
                    continue;
                }

                for (int m = 0; m < methods.Length; m++)
                {
                    bool duplicate = false;
                    for (int j = 0; j < builder.Count; j++)
                    {
                        if (SameSignature(builder[j], methods[m]))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        builder.Add(methods[m]);
                }
            }
        }
        private static ImmutableArray<MethodSymbol> LookupMethods(NamedTypeSymbol type, string name)
        {
            var b = ImmutableArray.CreateBuilder<MethodSymbol>();

            if (type.TypeKind == TypeKind.Interface)
            {
                foreach (var t in EnumerateInterfaceClosure(type))
                {
                    var members = t.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is MethodSymbol ms &&
                            !ms.IsConstructor &&
                            ms.ExplicitInterfaceImplementation is null &&
                            StringComparer.Ordinal.Equals(ms.Name, name))
                        {
                            bool dup = false;
                            for (int j = 0; j < b.Count; j++)
                            {
                                if (SameSignature(b[j], ms))
                                {
                                    dup = true;
                                    break;
                                }
                            }

                            if (!dup)
                                b.Add(ms);
                        }
                    }
                }

                return b.ToImmutable();
            }

            for (NamedTypeSymbol? t = type; t != null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol ms &&
                        !ms.IsConstructor &&
                        ms.ExplicitInterfaceImplementation is null &&
                        StringComparer.Ordinal.Equals(ms.Name, name))
                    {
                        bool dup = false;
                        for (int j = 0; j < b.Count; j++)
                        {
                            if (SameSignature(b[j], ms))
                            {
                                dup = true;
                                break;
                            }
                        }

                        if (!dup)
                            b.Add(ms);
                    }
                }
            }

            return b.ToImmutable();
        }
        private static bool SameSignature(MethodSymbol a, MethodSymbol b)
        {
            if (a.IsStatic != b.IsStatic) return false;
            if (a.TypeParameters.Length != b.TypeParameters.Length) return false;

            var ap = a.Parameters;
            var bp = b.Parameters;
            if (ap.Length != bp.Length) return false;

            for (int i = 0; i < ap.Length; i++)
            {
                if (ap[i].RefKind != bp[i].RefKind) return false;
                if (ap[i].IsReadOnlyRef != bp[i].IsReadOnlyRef) return false;
                if (!ReferenceEquals(ap[i].Type, bp[i].Type)) return false;
            }
            return true;
        }
        private static ImmutableArray<PropertySymbol> LookupIndexers(NamedTypeSymbol type)
        {
            var builder = ImmutableArray.CreateBuilder<PropertySymbol>();

            for (NamedTypeSymbol? t = type; t is not null; t = t.BaseType as NamedTypeSymbol)
            {
                var members = t.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol p &&
                        p.ExplicitInterfaceImplementation is null
                        && p.Parameters.Length != 0)
                        builder.Add(p);
                }

                if (builder.Count != 0)
                    return builder.ToImmutable();
            }

            return ImmutableArray<PropertySymbol>.Empty;
        }
        private static ImmutableArray<MethodSymbol> LookupConstructors(NamedTypeSymbol type)
        {
            if (type is null)
                return ImmutableArray<MethodSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<MethodSymbol>();
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms && ms.IsConstructor && !ms.IsStatic)
                    b.Add(ms);
            }
            return b.ToImmutable();
        }
        private ImmutableArray<TypeSymbol> BindTypeArguments(
            SeparatedSyntaxList<TypeSyntax> args,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var b = ImmutableArray.CreateBuilder<TypeSymbol>(args.Count);
            for (int i = 0; i < args.Count; i++)
            {
                var typeArgument = BindType(args[i], context, diagnostics);
                if (typeArgument is FunctionPointerTypeSymbol)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_FNPTR_GENERIC001",
                        DiagnosticSeverity.Error,
                        $"The type '{typeArgument.Name}' may not be used as a type argument.",
                        new Location(context.SemanticModel.SyntaxTree, args[i].Span)));
                }
                b.Add(typeArgument);
            }
            return b.ToImmutable();
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic("CN_NEW003", DiagnosticSeverity.Error,
                "Cannot infer the type of a target-typed object creation expression. Provide an explicit target type.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            var bad = new BoundBadExpression(node);
            bad.SetType(new ErrorTypeSymbol("new", containing: null, ImmutableArray<Location>.Empty));
            return bad;
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node, TypeSymbol targetType, BindingContext context, DiagnosticBag diagnostics)
        {
            var argSyntaxes = node.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            return BindImplicitObjectCreation(node, targetType, args.ToImmutable(), context, diagnostics);
        }
        private BoundExpression BindImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node,
            TypeSymbol targetType,
            ImmutableArray<BoundExpression> boundArgs,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (targetType is not NamedTypeSymbol nt)
            {
                diagnostics.Add(new Diagnostic("CN_NEW001", DiagnosticSeverity.Error,
                    $"'{targetType.Name}' is not a constructible type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(targetType);
                return bad;
            }
            var created = BindObjectCreationCoreFromBoundArgs(
                syntax: node,
                type: nt,
                argSyntaxes: node.ArgumentList.Arguments,
                boundArgs: boundArgs,
                diagnosticSpan: node.Span,
                context: context,
                diagnostics: diagnostics);

            return BindObjectCreationInitializer(node, created, node.Initializer, context, diagnostics);
        }
        private BoundExpression BindUnboundImplicitObjectCreation(
            ImplicitObjectCreationExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var argSyntaxes = node.ArgumentList.Arguments;
            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));
            return new BoundUnboundImplicitObjectCreationExpression(node, args.ToImmutable());
        }
        private BoundExpression BindUnboundCollectionExpression(
            CollectionExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var elements = ImmutableArray.CreateBuilder<BoundCollectionElement>(node.Elements.Count);
            for (int i = 0; i < node.Elements.Count; i++)
            {
                var element = node.Elements[i];
                switch (element)
                {
                    case ExpressionElementSyntax expressionElement:
                        {
                            var bound = BindExpression(expressionElement.Expression, context, diagnostics);
                            elements.Add(new BoundCollectionElement(
                                BoundCollectionElementKind.Expression,
                                expressionElement,
                                bound));
                            break;
                        }
                    case SpreadElementSyntax spreadElement:
                        {
                            var bound = BindExpression(spreadElement.Expression, context, diagnostics);
                            elements.Add(new BoundCollectionElement(
                                BoundCollectionElementKind.Spread,
                                spreadElement,
                                bound));
                            break;
                        }
                    default:
                        diagnostics.Add(new Diagnostic(
                            "CN_COLL000",
                            DiagnosticSeverity.Error,
                            "Unsupported collection expression element.",
                            new Location(context.SemanticModel.SyntaxTree, element.Span)));
                        elements.Add(new BoundCollectionElement(
                            BoundCollectionElementKind.Expression,
                            element,
                            new BoundBadExpression(element)));
                        break;
                }
            }

            return new BoundUnboundCollectionExpression(node, elements.ToImmutable());
        }
        private BoundExpression BindObjectCreation(ObjectCreationExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var createdType = BindType(node.Type, context, diagnostics);
            if (createdType is not NamedTypeSymbol nt)
            {
                diagnostics.Add(new Diagnostic("CN_NEW001", DiagnosticSeverity.Error,
                    $"'{createdType.Name}' is not a constructible type.",
                    new Location(context.SemanticModel.SyntaxTree, node.Type.Span)));
                var bad = new BoundBadExpression(node);
                bad.SetType(createdType);
                return bad;
            }
            var argSyntaxes = node.ArgumentList?.Arguments ?? SeparatedSyntaxList<ArgumentSyntax>.Empty;

            var args = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Count);
            for (int i = 0; i < argSyntaxes.Count; i++)
                args.Add(BindCallArgument(argSyntaxes[i], context, diagnostics));

            BoundExpression created;

            if (nt.TypeKind == TypeKind.Struct && node.ArgumentList is null)
            {
                created = new BoundObjectCreationExpression(
                    syntax: node,
                    type: nt,
                    constructorOpt: null,
                    arguments: ImmutableArray<BoundExpression>.Empty);
            }
            else
            {
                created = BindObjectCreationCoreFromBoundArgs(
                syntax: node,
                type: nt,
                argSyntaxes: argSyntaxes,
                boundArgs: args.ToImmutable(),
                diagnosticSpan: node.Type.Span,
                context: context,
                diagnostics: diagnostics);
            }

            return BindObjectCreationInitializer(node, created, node.Initializer, context, diagnostics);
        }

        private BoundExpression BindObjectCreationCoreFromBoundArgs(
            SyntaxNode syntax,
            NamedTypeSymbol type,
            SeparatedSyntaxList<ArgumentSyntax> argSyntaxes,
            ImmutableArray<BoundExpression> boundArgs,
            TextSpan diagnosticSpan,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var allCtorCandidates = LookupConstructors(type);
            var ctorCandidates = allCtorCandidates
                .Where(c => AccessibilityHelper.IsAccessible(c, context))
                .ToImmutableArray();

            if (type.TypeKind == TypeKind.Struct && boundArgs.Length == 0)
            {
                bool hasAccessibleParameterlessCtor = false;
                for (int i = 0; i < ctorCandidates.Length; i++)
                {
                    if (ctorCandidates[i].Parameters.Length == 0)
                    {
                        hasAccessibleParameterlessCtor = true;
                        break;
                    }
                }

                if (!hasAccessibleParameterlessCtor)
                    return new BoundObjectCreationExpression(syntax, type, constructorOpt: null, arguments: boundArgs);
            }
            if (ctorCandidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic("CN_NEW002", DiagnosticSeverity.Error,
                    $"No accessible constructor found for '{type.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticSpan)));
                var bad = new BoundBadExpression(syntax);
                bad.SetType(type);
                return bad;
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
                diagnosticNode: syntax))
            {
                var bad = new BoundBadExpression(syntax);
                bad.SetType(type);
                return bad;
            }
            return new BoundObjectCreationExpression(syntax, type, chosen!, convertedArgs);
        }
        // Stabilize the created receiver before emitting initializer side effects
        private BoundExpression BindObjectCreationInitializer(
            ExpressionSyntax syntax,
            BoundExpression created,
            InitializerExpressionSyntax? initializer,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (initializer is null || created.HasErrors)
                return created;

            var temp = NewTemp("$objinit", created.Type);
            var tempExpr = new BoundLocalExpression(syntax, temp);

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

            locals.Add(temp);
            sideEffects.Add(new BoundLocalDeclarationStatement(syntax, temp, created));

            switch (initializer.Kind)
            {
                case SyntaxKind.ObjectInitializerExpression:
                    AppendObjectInitializerEffects(
                        initializer,
                        tempExpr,
                        locals,
                        sideEffects,
                        context,
                        diagnostics);
                    break;
                case SyntaxKind.CollectionInitializerExpression:
                    AppendCollectionInitializerEffects(
                        initializer,
                        tempExpr,
                        locals,
                        sideEffects,
                        context,
                        diagnostics);
                    break;
                default:
                    diagnostics.Add(new Diagnostic(
                        "CN_NEW000",
                        DiagnosticSeverity.Error,
                        "Unsupported object or collection initializer.",
                        new Location(context.SemanticModel.SyntaxTree, initializer.Span)));

                    var bad = new BoundBadExpression(syntax);
                    bad.SetType(created.Type);
                    return bad;
            }


            var seq = new BoundSequenceExpression(
                syntax,
                locals.ToImmutable(),
                sideEffects.ToImmutable(),
                tempExpr);

            context.Recorder.RecordBound(initializer, seq);
            return seq;
        }
        private void AppendObjectInitializerEffects(
            InitializerExpressionSyntax initializer,
            BoundExpression receiver,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var stableReceiver = StabilizeObjectInitializerReceiver(
                initializer, receiver, locals, sideEffects, context, diagnostics);

            if (stableReceiver is null)
                return;

            for (int i = 0; i < initializer.Expressions.Count; i++)
            {
                var element = initializer.Expressions[i];

                if (element is not AssignmentExpressionSyntax assign ||
                    assign.Kind != SyntaxKind.SimpleAssignmentExpression)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_OBJINIT001",
                        DiagnosticSeverity.Error,
                        "Object initializer elements must be simple assignments.",
                        new Location(context.SemanticModel.SyntaxTree, element.Span)));
                    continue;
                }
                if (!TryGetObjectInitializerMemberName(assign.Left, out var memberName))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_OBJINIT002",
                        DiagnosticSeverity.Error,
                        "Object initializer member must be a simple identifier.",
                        new Location(context.SemanticModel.SyntaxTree, assign.Left.Span)));
                    continue;
                }
                // Nested object initializer
                if (assign.Right is InitializerExpressionSyntax nestedInit &&
                    nestedInit.Kind == SyntaxKind.ObjectInitializerExpression)
                {
                    var nestedReceiver = BindObjectInitializerMemberAccess(
                        memberSyntax: assign.Left,
                        receiver: stableReceiver,
                        memberName: memberName,
                        valueKind: BindValueKind.RValue,
                        context: context,
                        diagnostics: diagnostics);

                    if (nestedReceiver.HasErrors)
                        continue;

                    AppendObjectInitializerEffects(
                        nestedInit,
                        nestedReceiver,
                        locals,
                        sideEffects,
                        context,
                        diagnostics);

                    context.Recorder.RecordBound(assign, nestedReceiver);
                    context.Recorder.RecordBound(nestedInit, nestedReceiver);
                    continue;
                }
                if (assign.Right is InitializerExpressionSyntax unsupportedInit &&
                    unsupportedInit.Kind == SyntaxKind.CollectionInitializerExpression)
                {
                    var nestedReceiver = BindObjectInitializerMemberAccess(
                        memberSyntax: assign.Left,
                        receiver: stableReceiver,
                        memberName: memberName,
                        valueKind: BindValueKind.RValue,
                        context: context,
                        diagnostics: diagnostics);

                    if (nestedReceiver.HasErrors)
                        continue;

                    AppendCollectionInitializerEffects(
                        unsupportedInit,
                        nestedReceiver,
                        locals,
                        sideEffects,
                        context,
                        diagnostics);

                    context.Recorder.RecordBound(assign, nestedReceiver);
                    context.Recorder.RecordBound(unsupportedInit, nestedReceiver);
                    continue;
                }

                var left = BindObjectInitializerMemberAccess(
                    memberSyntax: assign.Left,
                    receiver: stableReceiver,
                    memberName: memberName,
                    valueKind: BindValueKind.LValue,
                    context: context,
                    diagnostics: diagnostics);

                if (left.HasErrors)
                    continue;

                var right = BindExpressionWithTargetType(
                    exprSyntax: assign.Right,
                    targetType: left.Type,
                    diagnosticNode: assign,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                var boundAssign = new BoundAssignmentExpression(assign, left, right);
                if (left.HasErrors || right.HasErrors)
                    boundAssign.SetHasErrors();

                context.Recorder.RecordBound(assign, boundAssign);
                sideEffects.Add(new BoundExpressionStatement(assign, boundAssign));
            }
        }
        private void AppendCollectionInitializerEffects(
            InitializerExpressionSyntax initializer,
            BoundExpression receiver,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var stableReceiver = StabilizeObjectInitializerReceiver(
                initializer, receiver, locals, sideEffects, context, diagnostics);

            if (stableReceiver is null)
                return;

            for (int i = 0; i < initializer.Expressions.Count; i++)
            {
                var element = initializer.Expressions[i];

                var addCall = BindCollectionInitializerAddCall(
                    elementSyntax: element,
                    receiver: stableReceiver,
                    context: context,
                    diagnostics: diagnostics);

                context.Recorder.RecordBound(element, addCall);

                if (addCall.HasErrors)
                    continue;

                sideEffects.Add(new BoundExpressionStatement(element, addCall));
            }
        }
        private BoundExpression BindCollectionInitializerAddCall(
            ExpressionSyntax elementSyntax,
            BoundExpression receiver,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_COLINIT001",
                    DiagnosticSeverity.Error,
                    "Collection initializer receiver has no bindable members.",
                    new Location(context.SemanticModel.SyntaxTree, elementSyntax.Span)));
                return new BoundBadExpression(elementSyntax);
            }

            var argSyntaxesBuilder = ImmutableArray.CreateBuilder<ExpressionSyntax>();
            if (elementSyntax is InitializerExpressionSyntax nestedArgs)
            {
                for (int i = 0; i < nestedArgs.Expressions.Count; i++)
                    argSyntaxesBuilder.Add(nestedArgs.Expressions[i]);
            }
            else
            {
                argSyntaxesBuilder.Add(elementSyntax);
            }

            var argSyntaxes = argSyntaxesBuilder.ToImmutable();

            var boundArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(argSyntaxes.Length);
            for (int i = 0; i < argSyntaxes.Length; i++)
                boundArgsBuilder.Add(BindExpression(argSyntaxes[i], context, diagnostics));

            var boundArgs = boundArgsBuilder.ToImmutable();

            var instanceCandidates = LookupMethods(receiverType, "Add")
                .OfType<MethodSymbol>()
                .Where(m => !m.IsStatic)
                .Where(m => AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (!instanceCandidates.IsDefaultOrEmpty)
            {
                if (TryResolveOverload(
                    candidates: instanceCandidates,
                    args: boundArgs,
                    getArgExprSyntax: i => argSyntaxes[i],
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: elementSyntax))
                {
                    return new BoundCallExpression(elementSyntax, receiver, chosen!, convertedArgs);
                }

                return new BoundBadExpression(elementSyntax);
            }

            var extensionCandidates = LookupExtensionMethods("Add", receiver, context);
            if (!extensionCandidates.IsDefaultOrEmpty)
            {
                var extArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(boundArgs.Length + 1);
                extArgsBuilder.Add(receiver);
                extArgsBuilder.AddRange(boundArgs);
                var extArgs = extArgsBuilder.ToImmutable();

                var receiverArgSyntax = receiver.Syntax as ExpressionSyntax ?? elementSyntax;

                if (TryResolveOverload(
                    candidates: extensionCandidates,
                    args: extArgs,
                    getArgExprSyntax: i => i == 0 ? receiverArgSyntax : argSyntaxes[i - 1],
                    getArgRefKindKeyword: i => null,
                    getArgName: i => null,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: elementSyntax))
                {
                    return new BoundCallExpression(
                        elementSyntax,
                        receiverOpt: null,
                        method: chosen!,
                        arguments: convertedArgs);
                }

                return new BoundBadExpression(elementSyntax);
            }

            diagnostics.Add(new Diagnostic(
                "CN_COLINIT002",
                DiagnosticSeverity.Error,
                $"No accessible 'Add' method found on type '{receiverType.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, elementSyntax.Span)));

            return new BoundBadExpression(elementSyntax);
        }
        private BoundExpression? StabilizeObjectInitializerReceiver(
            SyntaxNode syntax,
            BoundExpression receiver,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (receiver.Type.IsValueType)
            {
                if (receiver.IsLValue || receiver is BoundThisExpression)
                    return receiver;

                diagnostics.Add(new Diagnostic(
                    "CN_OBJINIT003",
                    DiagnosticSeverity.Error,
                    "Nested object initializers cannot target a non-variable value-type receiver.",
                    new Location(context.SemanticModel.SyntaxTree, receiver.Syntax.Span)));
                return null;
            }
            if (receiver is BoundLocalExpression or BoundParameterExpression or BoundThisExpression or BoundBaseExpression)
                return receiver;

            var temp = NewTemp("$objinit_recv", receiver.Type);
            var tempExpr = new BoundLocalExpression(syntax, temp);

            locals.Add(temp);
            sideEffects.Add(new BoundLocalDeclarationStatement(syntax, temp, receiver));
            return tempExpr;
        }
        private static bool TryGetObjectInitializerMemberName(ExpressionSyntax syntax, out string memberName)
        {
            if (syntax is IdentifierNameSyntax id)
            {
                memberName = id.Identifier.ValueText ?? string.Empty;
                return memberName.Length != 0;
            }
            memberName = string.Empty;
            return false;
        }
        private BoundExpression BindObjectInitializerMemberAccess(
            ExpressionSyntax memberSyntax,
            BoundExpression receiver,
            string memberName,
            BindValueKind valueKind,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var receiverType = GetReceiverTypeForMemberLookup(receiver.Type);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OBJINIT004",
                    DiagnosticSeverity.Error,
                    "Object initializer receiver has no bindable members.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            var members = LookupMembers(receiverType, memberName);
            if (members.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC002",
                    DiagnosticSeverity.Error,
                    $"No member '{memberName}' found on type '{receiverType.Name}'.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            members = FilterAccessibleMembers(members, context);
            if (members.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ACC002",
                    DiagnosticSeverity.Error,
                    $"Member '{memberName}' is inaccessible due to its protection level.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            FieldSymbol? field = null;
            PropertySymbol? prop = null;
            bool hasMethod = false;
            bool hasType = false;

            for (int i = 0; i < members.Length; i++)
            {
                switch (members[i])
                {
                    case FieldSymbol f: field ??= f; break;
                    case PropertySymbol p: prop ??= p; break;
                    case MethodSymbol: hasMethod = true; break;
                    case NamedTypeSymbol: hasType = true; break;
                }
            }

            if (field is null && prop is null)
            {
                diagnostics.Add(new Diagnostic(
                    hasMethod ? "CN_MEMACC003" :
                    hasType ? "CN_MEMACC004" :
                                "CN_MEMACC005",
                    DiagnosticSeverity.Error,
                    hasMethod ? "Method groups are not supported." :
                    hasType ? "A type name is not a value." :
                                "Member access does not resolve to a value member.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            if (field is not null && prop is not null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC006",
                    DiagnosticSeverity.Error,
                    $"Member name '{memberName}' is ambiguous between a field and a property.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            if (field is not null)
            {
                if (field.IsStatic)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_OBJINIT005",
                        DiagnosticSeverity.Error,
                        "Object initializers cannot target static fields.",
                        new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                    return new BoundBadExpression(memberSyntax);
                }
                if (valueKind == BindValueKind.LValue && field.IsConst)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MEMACC010",
                        DiagnosticSeverity.Error,
                        "Cannot assign to a const field.",
                        new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                    return new BoundBadExpression(memberSyntax);
                }

                bool isRefField = field.Type is ByRefTypeSymbol;
                TypeSymbol fieldValueType = isRefField ? ((ByRefTypeSymbol)field.Type).ElementType : field.Type;

                if (valueKind == BindValueKind.LValue &&
                    IsReadOnlyValueReceiver(receiver, context) &&
                    !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_READONLY_THIS001",
                        DiagnosticSeverity.Error,
                        "Cannot assign to instance members of 'this' in a readonly struct instance member.",
                        new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                    return new BoundBadExpression(memberSyntax);
                }

                bool allowCtorReadonlyWrite =
                    valueKind == BindValueKind.LValue &&
                    field.IsReadOnly &&
                    CanAssignReadOnlyFieldInConstructor(field, receiver, context);

                if (valueKind == BindValueKind.LValue && field.IsReadOnly && !allowCtorReadonlyWrite && !isRefField)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_MEMACC013",
                        DiagnosticSeverity.Error,
                        "Cannot assign to a readonly field except in a constructor of the same type.",
                        new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                    return new BoundBadExpression(memberSyntax);
                }

                bool canWriteField = !field.IsConst && (isRefField || !field.IsReadOnly || allowCtorReadonlyWrite);

                return new BoundMemberAccessExpression(
                    memberSyntax,
                    receiver,
                    field,
                    fieldValueType,
                    isLValue: canWriteField,
                    constantValueOpt: field.IsConst ? field.ConstantValueOpt : Optional<object>.None);
            }

            if (prop!.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OBJINIT006",
                    DiagnosticSeverity.Error,
                    "Object initializers cannot target static properties.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }
            bool canReadProperty =
                prop.GetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.GetMethod, context);

            bool canWriteProperty =
                prop.SetMethod is not null &&
                AccessibilityHelper.IsAccessible(prop.SetMethod, context);

            if (valueKind == BindValueKind.RValue && !canReadProperty)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC020",
                    DiagnosticSeverity.Error,
                    "Property has no accessible getter.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            bool allowCtorAutoPropWrite =
                valueKind == BindValueKind.LValue &&
                !canWriteProperty &&
                CanAssignReadOnlyAutoPropertyInConstructor(prop, receiver, context);

            if (valueKind == BindValueKind.LValue && !canWriteProperty && !allowCtorAutoPropWrite)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_MEMACC021",
                    DiagnosticSeverity.Error,
                    "Property has no accessible setter.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            if (valueKind == BindValueKind.LValue && IsReadOnlyValueReceiver(receiver, context))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_READONLY_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to instance properties of 'this' in a readonly struct instance member.",
                    new Location(context.SemanticModel.SyntaxTree, memberSyntax.Span)));
                return new BoundBadExpression(memberSyntax);
            }

            return new BoundMemberAccessExpression(
                memberSyntax,
                receiver,
                prop,
                prop.Type,
                isLValue: canWriteProperty || allowCtorAutoPropWrite);
        }
        // Infer method type arguments from the selected argument-to-parameter mapping
        private bool TryInferAndConstructGenericMethodCandidate(
            MethodSymbol candidate,
            ImmutableArray<BoundExpression> args,
            int[] argToParamMap,
            bool usesParamsExpansion,
            int[]? paramsElementArgIndices,
            BindingContext context,
            SyntaxNode diagnosticNode,
            out MethodSymbol constructed)
        {
            constructed = candidate;

            var typeParameters = candidate.TypeParameters;
            if (typeParameters.IsDefaultOrEmpty)
                return true;

            if (candidate is ConstructedMethodSymbol)
            {
                return GenericConstraintChecker.CheckMethodInstantiation(
                    methodDefinition: candidate.OriginalDefinition,
                    typeArguments: candidate.TypeArguments,
                    getArgSpan: _ => diagnosticNode.Span,
                    context: context,
                    diagnostics: new DiagnosticBag());
            }

            var existingTypeArguments = candidate.TypeArguments;
            if (existingTypeArguments.Length == typeParameters.Length)
            {
                bool alreadyConstructed = false;
                for (int i = 0; i < typeParameters.Length; i++)
                {
                    if (!ReferenceEquals(existingTypeArguments[i], typeParameters[i]))
                    {
                        alreadyConstructed = true;
                        break;
                    }
                }

                if (alreadyConstructed)
                {
                    return GenericConstraintChecker.CheckMethodInstantiation(
                        methodDefinition: candidate.OriginalDefinition,
                        typeArguments: existingTypeArguments,
                        getArgSpan: _ => diagnosticNode.Span,
                        context: context,
                        diagnostics: new DiagnosticBag());
                }
            }

            var inferences = new TypeSymbol?[typeParameters.Length];
            var exactInferences = new bool[typeParameters.Length];
            var parameters = candidate.Parameters;
            int paramsIndex = usesParamsExpansion ? parameters.Length - 1 : -1;
            var expandedParamsArgs = paramsElementArgIndices is null
                ? null
                : new HashSet<int>(paramsElementArgIndices);

            for (int a = 0; a < args.Length; a++)
            {
                if (ShouldSuppressCascade(args[a]))
                    continue;

                int p = argToParamMap[a];
                if ((uint)p >= (uint)parameters.Length)
                    return false;

                var parameterType = parameters[p].Type;

                if (usesParamsExpansion && p == paramsIndex &&
                    expandedParamsArgs is not null && expandedParamsArgs.Contains(a))
                {
                    if (parameterType is not ArrayTypeSymbol paramsArray || paramsArray.Rank != 1 || !paramsArray.IsSZArray)
                        return false;

                    parameterType = paramsArray.ElementType;
                }

                InferMethodTypeArgumentsFromParameter(
                    parameterType,
                    args[a],
                    typeParameters,
                    inferences,
                    exactInferences);
            }

            var typeArguments = ImmutableArray.CreateBuilder<TypeSymbol>(typeParameters.Length);
            for (int i = 0; i < typeParameters.Length; i++)
            {
                var inferred = inferences[i];
                if (inferred is null || inferred is DefaultLiteralTypeSymbol)
                    return false;

                typeArguments.Add(inferred);
            }

            var inferredTypeArguments = typeArguments.ToImmutable();

            // Constraint failures make the candidate inapplicable
            if (!GenericConstraintChecker.CheckMethodInstantiation(
                methodDefinition: candidate,
                typeArguments: inferredTypeArguments,
                getArgSpan: _ => diagnosticNode.Span,
                context: context,
                diagnostics: new DiagnosticBag()))
            {
                return false;
            }

            constructed = new ConstructedMethodSymbol(
                candidate,
                inferredTypeArguments,
                context.Compilation.TypeManager);

            return true;
        }

        private static void InferMethodTypeArgumentsFromParameter(
            TypeSymbol parameterType,
            BoundExpression argument,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            TypeSymbol?[] inferences,
            bool[] exactInferences)
        {
            if (argument.Type is NullTypeSymbol or DefaultLiteralTypeSymbol or ThrowTypeSymbol)
                return;

            if (IsTrueErrorType(argument.Type))
                return;

            InferMethodTypeArgumentsFromTypes(
                parameterType,
                argument.Type,
                typeParameters,
                inferences,
                exactInferences,
                exactInference: false);
        }

        private static void InferMethodTypeArgumentsFromTypes(
            TypeSymbol parameterType,
            TypeSymbol argumentType,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            TypeSymbol?[] inferences,
            bool[] exactInferences,
            bool exactInference)
        {
            if (TryGetMethodTypeParameterOrdinal(parameterType, typeParameters, out int ordinal))
            {
                AddMethodTypeInference(ordinal, argumentType, inferences, exactInferences, exactInference);
                return;
            }

            switch (parameterType)
            {
                case ByRefTypeSymbol parameterByRef:
                    if (argumentType is ByRefTypeSymbol argumentByRef)
                        InferMethodTypeArgumentsFromTypes(
                            parameterByRef.ElementType,
                            argumentByRef.ElementType,
                            typeParameters,
                            inferences,
                            exactInferences,
                            exactInference: true);
                    return;

                case ArrayTypeSymbol parameterArray:
                    if (argumentType is ArrayTypeSymbol argumentArray &&
                        parameterArray.Rank == argumentArray.Rank &&
                        parameterArray.IsSZArray == argumentArray.IsSZArray)
                        InferMethodTypeArgumentsFromTypes(
                            parameterArray.ElementType,
                            argumentArray.ElementType,
                            typeParameters,
                            inferences,
                            exactInferences,
                            exactInference);
                    return;

                case PointerTypeSymbol parameterPointer:
                    if (argumentType is PointerTypeSymbol argumentPointer)
                        InferMethodTypeArgumentsFromTypes(
                            parameterPointer.PointedAtType,
                            argumentPointer.PointedAtType,
                            typeParameters,
                            inferences,
                            exactInferences,
                            exactInference);
                    return;

                case FunctionPointerTypeSymbol parameterFunctionPointer:
                    if (argumentType is FunctionPointerTypeSymbol argumentFunctionPointer &&
                        parameterFunctionPointer.CallingConvention == argumentFunctionPointer.CallingConvention &&
                        parameterFunctionPointer.ReturnRefKind == argumentFunctionPointer.ReturnRefKind &&
                        parameterFunctionPointer.Parameters.Length == argumentFunctionPointer.Parameters.Length)
                    {
                        InferMethodTypeArgumentsFromTypes(
                            parameterFunctionPointer.ReturnType,
                            argumentFunctionPointer.ReturnType,
                            typeParameters,
                            inferences,
                            exactInferences,
                            exactInference);
                        for (int i = 0; i < parameterFunctionPointer.Parameters.Length; i++)
                        {
                            if (parameterFunctionPointer.Parameters[i].RefKind != argumentFunctionPointer.Parameters[i].RefKind)
                                return;
                            InferMethodTypeArgumentsFromTypes(
                                parameterFunctionPointer.Parameters[i].Type,
                                argumentFunctionPointer.Parameters[i].Type,
                                typeParameters,
                                inferences,
                                exactInferences,
                                exactInference);
                        }
                    }
                    return;

                case TupleTypeSymbol parameterTuple:
                    if (argumentType is TupleTypeSymbol argumentTuple &&
                        parameterTuple.ElementTypes.Length == argumentTuple.ElementTypes.Length)
                    {
                        for (int i = 0; i < parameterTuple.ElementTypes.Length; i++)
                        {
                            InferMethodTypeArgumentsFromTypes(
                                parameterTuple.ElementTypes[i],
                                argumentTuple.ElementTypes[i],
                                typeParameters,
                                inferences,
                                exactInferences,
                                exactInference);
                        }
                    }
                    return;

                case NamedTypeSymbol parameterNamed:
                    if (!ContainsAnyMethodTypeParameter(parameterNamed, typeParameters))
                        return;

                    if (TryFindMatchingInferenceNamedType(argumentType, parameterNamed.OriginalDefinition, out var matchingArgumentNamed))
                    {
                        var parameterArgs = parameterNamed.TypeArguments;
                        var argumentArgs = matchingArgumentNamed.TypeArguments;

                        int n = Math.Min(parameterArgs.Length, argumentArgs.Length);
                        for (int i = 0; i < n; i++)
                        {
                            InferMethodTypeArgumentsFromTypes(
                                parameterArgs[i],
                                argumentArgs[i],
                                typeParameters,
                                inferences,
                                exactInferences,
                                exactInference);
                        }
                    }
                    return;
            }
        }

        private static bool TryGetMethodTypeParameterOrdinal(
            TypeSymbol type,
            ImmutableArray<TypeParameterSymbol> typeParameters,
            out int ordinal)
        {
            if (type is TypeParameterSymbol tp)
            {
                for (int i = 0; i < typeParameters.Length; i++)
                {
                    if (ReferenceEquals(tp, typeParameters[i]))
                    {
                        ordinal = i;
                        return true;
                    }
                }
            }

            ordinal = -1;
            return false;
        }

        private static void AddMethodTypeInference(
            int ordinal,
            TypeSymbol inferredType,
            TypeSymbol?[] inferences,
            bool[] exactInferences,
            bool exactInference)
        {
            var existing = inferences[ordinal];
            if (existing is null)
            {
                inferences[ordinal] = inferredType;
                exactInferences[ordinal] = exactInference;
                return;
            }

            if (AreSameType(existing, inferredType))
            {
                exactInferences[ordinal] |= exactInference;
                return;
            }

            bool existingExact = exactInferences[ordinal];
            if (existingExact && !exactInference)
                return;

            if (!existingExact && exactInference)
            {
                inferences[ordinal] = inferredType;
                exactInferences[ordinal] = true;
                return;
            }

            inferences[ordinal] = DefaultLiteralTypeSymbol.Instance;
        }

        private static bool ContainsAnyMethodTypeParameter(
            TypeSymbol type,
            ImmutableArray<TypeParameterSymbol> typeParameters)
        {
            if (TryGetMethodTypeParameterOrdinal(type, typeParameters, out _))
                return true;

            switch (type)
            {
                case ByRefTypeSymbol br:
                    return ContainsAnyMethodTypeParameter(br.ElementType, typeParameters);

                case ArrayTypeSymbol at:
                    return ContainsAnyMethodTypeParameter(at.ElementType, typeParameters);

                case PointerTypeSymbol pt:
                    return ContainsAnyMethodTypeParameter(pt.PointedAtType, typeParameters);

                case FunctionPointerTypeSymbol functionPointer:
                    if (ContainsAnyMethodTypeParameter(functionPointer.ReturnType, typeParameters))
                        return true;
                    for (int i = 0; i < functionPointer.Parameters.Length; i++)
                        if (ContainsAnyMethodTypeParameter(functionPointer.Parameters[i].Type, typeParameters))
                            return true;
                    return false;

                case TupleTypeSymbol tt:
                    for (int i = 0; i < tt.ElementTypes.Length; i++)
                    {
                        if (ContainsAnyMethodTypeParameter(tt.ElementTypes[i], typeParameters))
                            return true;
                    }
                    return false;

                case NamedTypeSymbol nt:
                    var args = nt.TypeArguments;
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (ContainsAnyMethodTypeParameter(args[i], typeParameters))
                            return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private static bool TryFindMatchingInferenceNamedType(
            TypeSymbol argumentType,
            NamedTypeSymbol parameterDefinition,
            out NamedTypeSymbol matchingArgumentType)
        {
            matchingArgumentType = null!;

            var seen = new HashSet<NamedTypeSymbol>(ReferenceEqualityComparer<NamedTypeSymbol>.Instance);
            var queue = new Queue<NamedTypeSymbol>();

            if (argumentType is NamedTypeSymbol argumentNamed)
                queue.Enqueue(argumentNamed);

            var directInterfaces = argumentType.Interfaces;
            for (int i = 0; i < directInterfaces.Length; i++)
            {
                if (directInterfaces[i] is NamedTypeSymbol iface)
                    queue.Enqueue(iface);
            }

            while (queue.Count != 0)
            {
                var current = queue.Dequeue();
                if (!seen.Add(current))
                    continue;

                if (ReferenceEquals(current.OriginalDefinition, parameterDefinition))
                {
                    matchingArgumentType = current;
                    return true;
                }

                var interfaces = current.Interfaces;
                for (int i = 0; i < interfaces.Length; i++)
                {
                    if (interfaces[i] is NamedTypeSymbol iface)
                        queue.Enqueue(iface);
                }

                if (current.BaseType is NamedTypeSymbol baseType)
                    queue.Enqueue(baseType);
            }

            return false;
        }

        // Rank applicable candidates after argument mapping, inference, and conversion analysis
        private bool TryResolveOverload(
            ImmutableArray<MethodSymbol> candidates,
            ImmutableArray<BoundExpression> args,
            Func<int, ExpressionSyntax> getArgExprSyntax,
            out MethodSymbol? chosen,
            out ImmutableArray<BoundExpression> convertedArgs,
            BindingContext context,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            Func<int, SyntaxToken?>? getArgRefKindKeyword = null,
            Func<int, string?>? getArgName = null,
            bool allowParamsExpansion = true)
        {
            chosen = null;
            convertedArgs = default;

            if (getArgName is not null)
            {
                bool sawNamed = false;
                var seenNames = new HashSet<string>(StringComparer.Ordinal);

                for (int i = 0; i < args.Length; i++)
                {
                    var n = getArgName(i);
                    if (!string.IsNullOrEmpty(n))
                    {
                        sawNamed = true;
                        if (!seenNames.Add(n!))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_NAMEDARG002",
                                DiagnosticSeverity.Error,
                                $"Named argument '{n}' is specified multiple times.",
                                new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                            return false;
                        }
                    }
                    else if (sawNamed)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_NAMEDARG001",
                            DiagnosticSeverity.Error,
                            "Positional arguments cannot appear after named arguments.",
                            new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                        return false;
                    }
                }

                foreach (var name in seenNames)
                {
                    bool found = false;
                    for (int c = 0; c < candidates.Length && !found; c++)
                    {
                        var ps = candidates[c].Parameters;
                        for (int p = 0; p < ps.Length; p++)
                        {
                            if (string.Equals(ps[p].Name, name, StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        // Pick the first occurrence for location
                        for (int i = 0; i < args.Length; i++)
                        {
                            var n = getArgName(i);
                            if (string.Equals(n, name, StringComparison.Ordinal))
                            {
                                diagnostics.Add(new Diagnostic(
                                    "CN_NAMEDARG003",
                                    DiagnosticSeverity.Error,
                                    $"No parameter named '{name}' exists in any candidate overload.",
                                    new Location(context.SemanticModel.SyntaxTree, getArgExprSyntax(i).Span)));
                                break;
                            }
                        }
                        return false;
                    }
                }
            }

            MethodSymbol? best = null;
            bool bestUsesParamsExpansion = false;
            int bestScore = int.MaxValue;
            int[]? bestArgToParamMap = null;
            int[]? bestParamsElementArgIndices = null;
            bool ambiguous = false;

            bool allowErrorRecovery = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (ShouldSuppressCascade(args[i]))
                {
                    allowErrorRecovery = true;
                    break;
                }
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                var m = candidates[i];
                var ps = m.Parameters;

                // Fixed-arity form
                if (args.Length <= ps.Length)
                {
                    if (TryScoreRegular(m, args, getArgExprSyntax, getArgRefKindKeyword, getArgName, context, out var scoredMethod, out int score, out var map))
                        ConsiderCandidate(scoredMethod, usesParamsExpansion: false, score, map, paramsElementArgIndices: null);
                }

                // Expanded params form
                if (allowParamsExpansion && ps.Length > 0 && ps[^1].IsParams && ps[^1].Type is ArrayTypeSymbol at && at.Rank == 1 && at.IsSZArray)
                {
                    int fixedCount = ps.Length - 1;

                    if (TryScoreParamsExpanded(m, args, fixedCount, getArgRefKindKeyword, getArgName,
                        context, out var scoredMethod, out int score, out var map, out var paramsElementArgIndices))
                    {
                        // Penalize params expansion so non-expanded matches win ties
                        score += 5;
                        ConsiderCandidate(scoredMethod, usesParamsExpansion: true, score, map, paramsElementArgIndices);
                    }
                }
            }

            if (best is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL001",
                    DiagnosticSeverity.Error,
                    "No overload matches the argument list.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL002",
                    DiagnosticSeverity.Error,
                    "Overload resolution is ambiguous.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            var chosenMap = bestArgToParamMap;
            if (chosenMap is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OVL_INTERNAL001",
                    DiagnosticSeverity.Error,
                    "Internal error: overload resolution map is missing.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                return false;
            }

            var converted = new BoundExpression[best.Parameters.Length];
            if (!bestUsesParamsExpansion)
            {
                var ps = best.Parameters;
                var assigned = new bool[ps.Length];

                for (int a = 0; a < args.Length; a++)
                {
                    int p = chosenMap[a];
                    assigned[p] = true;

                    if (getArgExprSyntax(a) is InterpolatedStringExpressionSyntax interpolated &&
                        IsInterpolatedStringHandlerParameter(ps[p]))
                    {
                        converted[p] = BindInterpolatedStringHandlerArgument(
                            interpolated,
                            best,
                            p,
                            args,
                            chosenMap,
                            converted,
                            getArgExprSyntax,
                            context,
                            diagnostics);
                    }
                    else
                    {
                        converted[p] = ApplyConversion(
                            exprSyntax: getArgExprSyntax(a),
                            expr: args[a],
                            targetType: ps[p].Type,
                            diagnosticNode: diagnosticNode,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: true);
                    }
                }

                for (int p = 0; p < ps.Length; p++)
                    if (!assigned[p])
                        converted[p] = CreateOmittedArgument(ps[p]);
            }
            else
            {
                var ps = best.Parameters;
                int fixedCount = ps.Length - 1;
                int paramsIndex = ps.Length - 1;

                var paramsParam = ps[paramsIndex];
                var paramsArrayType = (ArrayTypeSymbol)paramsParam.Type;
                var elementType = paramsArrayType.ElementType;

                // Map arguments to fixed parameters
                var fixedArgIndex = new int[fixedCount];
                for (int p = 0; p < fixedCount; p++)
                    fixedArgIndex[p] = -1;

                for (int a = 0; a < args.Length; a++)
                {
                    int p = chosenMap[a];
                    if ((uint)p < (uint)fixedCount)
                        fixedArgIndex[p] = a;
                }

                // Convert fixed arguments
                for (int p = 0; p < fixedCount; p++)
                {
                    int a = fixedArgIndex[p];
                    if (a >= 0)
                    {
                        converted[p] = ApplyConversion(
                            exprSyntax: getArgExprSyntax(a),
                            expr: args[a],
                            targetType: ps[p].Type,
                            diagnosticNode: diagnosticNode,
                            context: context,
                            diagnostics: diagnostics,
                            requireImplicit: true);
                    }
                    else
                    {
                        converted[p] = CreateOmittedArgument(ps[p]);
                    }
                }

                var paramElems = bestParamsElementArgIndices ?? Array.Empty<int>();
                int elemCount = paramElems.Length;
                var int32Type = context.Compilation.GetSpecialType(SpecialType.System_Int32);

                var arrLocal = NewTemp("<params$>", paramsArrayType);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2 + elemCount);

                // Allocate the expanded params array
                var countLit = new BoundLiteralExpression(diagnosticNode, int32Type, elemCount);
                var newArr = new BoundArrayCreationExpression(diagnosticNode, paramsArrayType, elementType, countLit, initializerOpt: null);

                sideEffects.Add(new BoundExpressionStatement(
                    diagnosticNode,
                    new BoundAssignmentExpression(
                        diagnosticNode,
                        new BoundLocalExpression(diagnosticNode, arrLocal),
                        newArr)));

                // Store each expanded argument
                for (int i = 0; i < elemCount; i++)
                {
                    int argIndex = paramElems[i];

                    var idxLit = new BoundLiteralExpression(diagnosticNode, int32Type, i);
                    var elemAccess = new BoundArrayElementAccessExpression(
                        diagnosticNode,
                        elementType,
                        new BoundLocalExpression(diagnosticNode, arrLocal),
                        idxLit);

                    var elemValue = ApplyConversion(
                        exprSyntax: getArgExprSyntax(argIndex),
                        expr: args[argIndex],
                        targetType: elementType,
                        diagnosticNode: diagnosticNode,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    sideEffects.Add(new BoundExpressionStatement(
                        diagnosticNode,
                        new BoundAssignmentExpression(diagnosticNode, elemAccess, elemValue)));
                }

                var packedArray = new BoundSequenceExpression(
                    diagnosticNode,
                    locals: ImmutableArray.Create(arrLocal),
                    sideEffects: sideEffects.ToImmutable(),
                    value: new BoundLocalExpression(diagnosticNode, arrLocal));

                converted[paramsIndex] = packedArray;
            }

            chosen = best;
            convertedArgs = ImmutableArray.Create(converted);
            return true;

            void ConsiderCandidate(MethodSymbol m, bool usesParamsExpansion, int score, int[] argToParamMap, int[]? paramsElementArgIndices)
            {
                var currentBest = best;
                var currentBestArgToParamMap = bestArgToParamMap;

                if (currentBest is null || currentBestArgToParamMap is null || score < bestScore)
                {
                    bestScore = score;
                    best = m;
                    bestUsesParamsExpansion = usesParamsExpansion;
                    bestArgToParamMap = argToParamMap;
                    bestParamsElementArgIndices = paramsElementArgIndices;
                    ambiguous = false;
                    return;
                }

                if (score != bestScore)
                    return;

                if (ReferenceEquals(currentBest, m))
                {
                    if (bestUsesParamsExpansion && !usesParamsExpansion)
                    {
                        bestUsesParamsExpansion = false;
                        bestArgToParamMap = argToParamMap;
                        bestParamsElementArgIndices = paramsElementArgIndices;
                        ambiguous = false;
                    }
                    return;
                }

                var conversionTieBreak = CompareArgumentConversions(
                    m,
                    usesParamsExpansion,
                    argToParamMap,
                    paramsElementArgIndices,
                    currentBest,
                    bestUsesParamsExpansion,
                    currentBestArgToParamMap,
                    bestParamsElementArgIndices);

                if (conversionTieBreak < 0)
                {
                    best = m;
                    bestUsesParamsExpansion = usesParamsExpansion;
                    bestArgToParamMap = argToParamMap;
                    bestParamsElementArgIndices = paramsElementArgIndices;
                    ambiguous = false;
                    return;
                }

                if (conversionTieBreak > 0)
                    return;

                var tieBreak = CompareOverloadTieBreak(
                    m,
                    argToParamMap,
                    currentBest,
                    currentBestArgToParamMap);
                if (tieBreak < 0)
                {
                    best = m;
                    bestUsesParamsExpansion = usesParamsExpansion;
                    bestArgToParamMap = argToParamMap;
                    bestParamsElementArgIndices = paramsElementArgIndices;
                    ambiguous = false;
                    return;
                }

                if (tieBreak > 0)
                    return;

                ambiguous = true;
            }

            int CompareArgumentConversions(
                MethodSymbol left,
                bool leftUsesParamsExpansion,
                int[] leftMap,
                int[]? leftParamsElementArgIndices,
                MethodSymbol right,
                bool rightUsesParamsExpansion,
                int[] rightMap,
                int[]? rightParamsElementArgIndices)
            {
                bool leftBetter = false;
                bool rightBetter = false;

                for (int a = 0; a < args.Length; a++)
                {
                    if (allowErrorRecovery && ShouldSuppressCascade(args[a]))
                        continue;

                    var leftTarget = GetEffectiveParameterType(
                        left,
                        leftUsesParamsExpansion,
                        leftMap[a],
                        a,
                        leftParamsElementArgIndices);
                    var rightTarget = GetEffectiveParameterType(
                        right,
                        rightUsesParamsExpansion,
                        rightMap[a],
                        a,
                        rightParamsElementArgIndices);

                    if (AreSameType(leftTarget, rightTarget))
                        continue;

                    int better = BetterConversionFromExpression(args[a], leftTarget, rightTarget, getArgExprSyntax(a));
                    if (better < 0)
                        leftBetter = true;
                    else if (better > 0)
                        rightBetter = true;

                    if (leftBetter && rightBetter)
                        return 0;
                }

                if (leftBetter == rightBetter)
                    return 0;

                return leftBetter ? -1 : 1;
            }

            static TypeSymbol GetEffectiveParameterType(
                MethodSymbol method,
                bool usesParamsExpansion,
                int parameterIndex,
                int argumentIndex,
                int[]? paramsElementArgIndices)
            {
                var parameterType = method.Parameters[parameterIndex].Type;
                if (!usesParamsExpansion ||
                    parameterIndex != method.Parameters.Length - 1 ||
                    parameterType is not ArrayTypeSymbol arrayType ||
                    paramsElementArgIndices is null)
                {
                    return parameterType;
                }

                for (int i = 0; i < paramsElementArgIndices.Length; i++)
                {
                    if (paramsElementArgIndices[i] == argumentIndex)
                        return arrayType.ElementType;
                }

                return parameterType;
            }

            int BetterConversionFromExpression(
                BoundExpression expression,
                TypeSymbol leftTarget,
                TypeSymbol rightTarget,
                ExpressionSyntax expressionSyntax)
            {
                bool exactLeft = AreSameType(expression.Type, leftTarget);
                bool exactRight = AreSameType(expression.Type, rightTarget);

                if (exactLeft != exactRight)
                    return exactLeft ? -1 : 1;

                var leftToRight = ClassifyConversion(
                    new BoundTypeOnlyExpression(expressionSyntax, leftTarget),
                    rightTarget,
                    context.Compilation.Target);
                var rightToLeft = ClassifyConversion(
                    new BoundTypeOnlyExpression(expressionSyntax, rightTarget),
                    leftTarget,
                    context.Compilation.Target);

                bool leftImplicitToRight = leftToRight.Exists && leftToRight.IsImplicit;
                bool rightImplicitToLeft = rightToLeft.Exists && rightToLeft.IsImplicit;

                if (leftImplicitToRight != rightImplicitToLeft)
                    return leftImplicitToRight ? -1 : 1;

                if (IsBetterSignedIntegralTarget(leftTarget.SpecialType, rightTarget.SpecialType))
                    return -1;
                if (IsBetterSignedIntegralTarget(rightTarget.SpecialType, leftTarget.SpecialType))
                    return 1;

                return 0;
            }

            static bool IsBetterSignedIntegralTarget(SpecialType signed, SpecialType unsigned)
            {
                return signed switch
                {
                    SpecialType.System_Int8 => unsigned is SpecialType.System_UInt8 or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UIntPtr or SpecialType.System_UInt64,
                    SpecialType.System_Int16 => unsigned is SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UIntPtr or SpecialType.System_UInt64,
                    SpecialType.System_Int32 => unsigned is SpecialType.System_UInt32 or SpecialType.System_UIntPtr or SpecialType.System_UInt64,
                    SpecialType.System_IntPtr => unsigned is SpecialType.System_UIntPtr or SpecialType.System_UInt64,
                    SpecialType.System_Int64 => unsigned is SpecialType.System_UIntPtr or SpecialType.System_UInt64,
                    _ => false
                };
            }

            static int CompareOverloadTieBreak(
                MethodSymbol left,
                int[] leftArgToParamMap,
                MethodSymbol? right,
                int[]? rightArgToParamMap)
            {
                if (right is null || rightArgToParamMap is null)
                    return -1;

                bool leftGeneric = !left.TypeParameters.IsDefaultOrEmpty;
                bool rightGeneric = !right.TypeParameters.IsDefaultOrEmpty;

                if (leftGeneric != rightGeneric)
                    return leftGeneric ? 1 : -1;

                bool leftAllParametersSupplied = AllParametersHaveCorrespondingArguments(left, leftArgToParamMap);
                bool rightAllParametersSupplied = AllParametersHaveCorrespondingArguments(right, rightArgToParamMap);
                bool leftUsesOptionalDefaults = UsesOptionalDefaults(left, leftArgToParamMap);
                bool rightUsesOptionalDefaults = UsesOptionalDefaults(right, rightArgToParamMap);

                if (leftAllParametersSupplied && rightUsesOptionalDefaults)
                    return -1;
                if (rightAllParametersSupplied && leftUsesOptionalDefaults)
                    return 1;

                return 0;

                static bool AllParametersHaveCorrespondingArguments(MethodSymbol method, int[] argToParamMap)
                {
                    for (int p = 0; p < method.Parameters.Length; p++)
                    {
                        bool found = false;
                        for (int a = 0; a < argToParamMap.Length; a++)
                        {
                            if (argToParamMap[a] == p)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                            return false;
                    }

                    return true;
                }

                static bool UsesOptionalDefaults(MethodSymbol method, int[] argToParamMap)
                {
                    for (int p = 0; p < method.Parameters.Length; p++)
                    {
                        if (!method.Parameters[p].HasExplicitDefault)
                            continue;

                        bool found = false;
                        for (int a = 0; a < argToParamMap.Length; a++)
                        {
                            if (argToParamMap[a] == p)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                            return true;
                    }

                    return false;
                }
            }

            bool TryScoreRegular(
                MethodSymbol m,
                ImmutableArray<BoundExpression> args,
                Func<int, ExpressionSyntax> getArgExprSyntax,
                Func<int, SyntaxToken?>? getArgRefKindKeyword,
                Func<int, string?>? getArgName,
                BindingContext context,
                out MethodSymbol scoredMethod,
                out int score,
                out int[] argToParamMap)
            {
                scoredMethod = m;
                score = 0;
                argToParamMap = Array.Empty<int>();

                var ps = m.Parameters;
                if (!TryBuildRegularArgMap(ps, args.Length, getArgName, out var map, out var assigned))
                    return false;

                for (int p = 0; p < ps.Length; p++)
                {
                    if (!assigned[p] && !IsOmittable(ps[p]))
                        return false;
                }

                if (!TryInferAndConstructGenericMethodCandidate(
                    m,
                    args,
                    map,
                    usesParamsExpansion: false,
                    paramsElementArgIndices: null,
                    context,
                    diagnosticNode,
                    out scoredMethod))
                {
                    return false;
                }

                ps = scoredMethod.Parameters;

                for (int a = 0; a < args.Length; a++)
                {
                    if (allowErrorRecovery && ShouldSuppressCascade(args[a]))
                        continue;

                    int p = map[a];

                    bool handlerConversion =
                        getArgExprSyntax(a) is InterpolatedStringExpressionSyntax &&
                        IsInterpolatedStringHandlerParameter(ps[p]) &&
                        (getArgRefKindKeyword is null || GetArgRefKind(getArgRefKindKeyword(a)) == ParameterRefKind.None);

                    if (getArgRefKindKeyword is not null &&
                        !ArgumentRefKindMatchesParameter(getArgRefKindKeyword(a), ps[p]) &&
                        !handlerConversion)
                    {
                        return false;
                    }

                    if (handlerConversion)
                    {
                        score += 1;
                        continue;
                    }

                    var conv = ClassifyConversion(args[a], ps[p].Type, context);
                    if (!conv.Exists || !conv.IsImplicit)
                        return false;

                    score += ConversionScore(conv.Kind);
                }

                argToParamMap = map;
                return true;
            }
            bool TryScoreParamsExpanded(
                MethodSymbol m,
                ImmutableArray<BoundExpression> args,
                int fixedCount,
                Func<int, SyntaxToken?>? getArgRefKindKeyword,
                Func<int, string?>? getArgName,
                BindingContext context,
                out MethodSymbol scoredMethod,
                out int score,
                out int[] argToParamMap,
                out int[] paramsElementArgIndices)
            {
                scoredMethod = m;
                score = 0;
                argToParamMap = Array.Empty<int>();
                paramsElementArgIndices = Array.Empty<int>();

                var ps = m.Parameters;
                if (ps.Length == 0)
                    return false;

                if (!TryBuildParamsExpandedArgMap(ps, args.Length, fixedCount, getArgName,
                    out var map, out var fixedAssigned, out var elems))
                {
                    return false;
                }

                // Any fixed parameter not assigned by an argument must have a default
                for (int p = 0; p < fixedCount; p++)
                {
                    if (!fixedAssigned[p] && !ps[p].HasExplicitDefault)
                        return false;
                }

                if (!TryInferAndConstructGenericMethodCandidate(
                    m,
                    args,
                    map,
                    usesParamsExpansion: true,
                    paramsElementArgIndices: elems,
                    context,
                    diagnosticNode,
                    out scoredMethod))
                {
                    return false;
                }

                ps = scoredMethod.Parameters;
                int paramsIndex = ps.Length - 1;

                if (ps[paramsIndex].Type is not ArrayTypeSymbol constructedParamsArray || constructedParamsArray.Rank != 1 || !constructedParamsArray.IsSZArray)
                    return false;

                var elementType = constructedParamsArray.ElementType;

                for (int a = 0; a < args.Length; a++)
                {
                    if (allowErrorRecovery && ShouldSuppressCascade(args[a]))
                        continue;

                    int p = map[a];

                    if (p != paramsIndex)
                    {
                        if (getArgRefKindKeyword is not null &&
                            !ArgumentRefKindMatchesParameter(getArgRefKindKeyword(a), ps[p]))
                        {
                            return false;
                        }

                        var conv = ClassifyConversion(args[a], ps[p].Type, context);
                        if (!conv.Exists || !conv.IsImplicit)
                            return false;

                        score += ConversionScore(conv.Kind);
                    }
                    else
                    {
                        if (getArgRefKindKeyword is not null &&
                            GetArgRefKind(getArgRefKindKeyword(a)) != ParameterRefKind.None)
                        {
                            return false;
                        }

                        var conv = ClassifyConversion(args[a], elementType, context);
                        if (!conv.Exists || !conv.IsImplicit)
                            return false;

                        score += ConversionScore(conv.Kind);
                    }
                }

                argToParamMap = map;
                paramsElementArgIndices = elems;
                return true;
            }

            static bool TryBuildRegularArgMap(
                ImmutableArray<ParameterSymbol> parameters,
                int argCount,
                Func<int, string?>? getArgName,
                out int[] map,
                out bool[] assigned)
            {
                map = new int[argCount];
                assigned = new bool[parameters.Length];

                // Positional arguments need no remapping
                if (getArgName is null)
                {
                    for (int i = 0; i < argCount; i++)
                    {
                        map[i] = i;
                        if ((uint)i < (uint)assigned.Length)
                            assigned[i] = true;
                    }
                    return true;
                }

                int nextPositional = 0;

                for (int a = 0; a < argCount; a++)
                {
                    var name = getArgName(a);
                    if (!string.IsNullOrEmpty(name))
                    {
                        int p = IndexOfParameter(parameters, name!);
                        if (p < 0) return false;
                        if (assigned[p]) return false;

                        assigned[p] = true;
                        map[a] = p;
                        continue;
                    }

                    while (nextPositional < parameters.Length && assigned[nextPositional])
                        nextPositional++;

                    if (nextPositional >= parameters.Length)
                        return false;

                    assigned[nextPositional] = true;
                    map[a] = nextPositional;
                    nextPositional++;
                }

                return true;
            }

            static bool TryBuildParamsExpandedArgMap(
                ImmutableArray<ParameterSymbol> parameters,
                int argCount,
                int fixedCount,
                Func<int, string?>? getArgName,
                out int[] map,
                out bool[] fixedAssigned,
                out int[] paramsElementArgIndices)
            {
                map = new int[argCount];
                fixedAssigned = new bool[Math.Max(0, fixedCount)];
                paramsElementArgIndices = Array.Empty<int>();
                int paramsIndex = parameters.Length - 1;

                // Positional arguments need no remapping
                if (getArgName is null)
                {
                    var elems = new int[Math.Max(0, argCount - fixedCount)];
                    int e = 0;

                    for (int a = 0; a < argCount; a++)
                    {
                        if (a < fixedCount)
                        {
                            map[a] = a;
                            fixedAssigned[a] = true;
                        }
                        else
                        {
                            map[a] = paramsIndex;
                            elems[e++] = a;
                        }
                    }

                    paramsElementArgIndices = elems;
                    return true;
                }

                var elemList = new List<int>();
                int nextPositional = 0;

                for (int a = 0; a < argCount; a++)
                {
                    var name = getArgName(a);
                    if (!string.IsNullOrEmpty(name))
                    {
                        int p = IndexOfParameter(parameters, name!);
                        if (p < 0) return false;

                        if (p != paramsIndex)
                        {
                            if ((uint)p >= (uint)fixedCount) return false;
                            if (fixedAssigned[p]) return false;

                            fixedAssigned[p] = true;
                            map[a] = p;
                        }
                        else
                        {
                            map[a] = paramsIndex;
                            elemList.Add(a);
                        }

                        continue;
                    }

                    while (nextPositional < fixedCount && fixedAssigned[nextPositional])
                        nextPositional++;

                    if (nextPositional < fixedCount)
                    {
                        fixedAssigned[nextPositional] = true;
                        map[a] = nextPositional;
                        nextPositional++;
                    }
                    else
                    {
                        map[a] = paramsIndex;
                        elemList.Add(a);
                    }
                }

                paramsElementArgIndices = elemList.Count == 0 ? Array.Empty<int>() : elemList.ToArray();
                return true;
            }

            static int IndexOfParameter(ImmutableArray<ParameterSymbol> parameters, string name)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
                        return i;
                }
                return -1;
            }

            static bool IsOmittable(ParameterSymbol p)
                => p.IsParams || p.HasExplicitDefault;

            BoundExpression CreateOmittedArgument(ParameterSymbol p)
            {
                if (p.IsParams)
                {
                    if (p.Type is not ArrayTypeSymbol at || at.Rank != 1 || !at.IsSZArray)
                    {
                        var bad = new BoundBadExpression(diagnosticNode);
                        bad.SetType(p.Type);
                        return bad;
                    }

                    var int32Type = context.Compilation.GetSpecialType(SpecialType.System_Int32);
                    var zero = new BoundLiteralExpression(diagnosticNode, int32Type, 0);
                    return new BoundArrayCreationExpression(diagnosticNode, at, at.ElementType, zero, initializerOpt: null);
                }

                if (!p.HasExplicitDefault || !p.DefaultValueOpt.HasValue)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_OVL003",
                        DiagnosticSeverity.Error,
                        $"Missing argument for parameter '{p.Name}'.",
                        new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                    var bad = new BoundBadExpression(diagnosticNode);
                    bad.SetType(p.Type);
                    return bad;
                }

                if (p.DefaultValueOpt.Value is null && p.Type.IsValueType)
                    return MakeDefaultValue(diagnosticNode, p.Type);

                return new BoundLiteralExpression(diagnosticNode, p.Type, p.DefaultValueOpt.Value);
            }
            static int ConversionScore(ConversionKind k) => k switch
            {
                ConversionKind.Identity => 0,
                ConversionKind.ImplicitStackAlloc => 1,
                ConversionKind.ImplicitNumeric => 1,
                ConversionKind.ImplicitConstant => 1,
                ConversionKind.ImplicitReference => 1,
                ConversionKind.ImplicitTuple => 1,
                ConversionKind.ImplicitNullable => 1,
                ConversionKind.NullLiteral => 1,
                ConversionKind.Boxing => 2,
                ConversionKind.UserDefined => 3,
                _ => 10
            };
        }
        // Operator overload resolution reuses the general candidate ranking pipeline
        private bool TryBindUserDefinedUnaryOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            BoundUnaryOperatorKind op,
            BoundExpression operand,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = GetUnaryOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: operand.Type,
                rightType: null,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(operand);
            if (!TryResolveOverload(
                    candidates: candidates,
                    args: args,
                    getArgExprSyntax: _ => operandSyntax,
                    chosen: out var chosen,
                    convertedArgs: out var convertedArgs,
                    context: context,
                    diagnostics: diagnostics,
                    diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_UOP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindUserDefinedBinaryOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax leftSyntax,
            BoundExpression left,
            ExpressionSyntax rightSyntax,
            BoundExpression right,
            BoundBinaryOperatorKind op,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            bool requireBooleanReturn = false)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = GetBinaryOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: left.Type,
                rightType: right.Type,
                metadataNames: names,
                parameterCount: 2,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(left, right);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: i => i == 0 ? leftSyntax : rightSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BOP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            if (requireBooleanReturn && chosen.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_BOP002",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' must return 'bool' for this operator.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindDirectCompoundAssignmentOperator(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression leftTarget,
            BoundExpression right,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            out MethodSymbol? method)
        {
            result = new BoundBadExpression(node);
            method = null;

            var names = GetCompoundAssignmentOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupInstanceUserDefinedOperatorMethods(
                receiverType: leftTarget.Type,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var overloadDiags = new DiagnosticBag();
            if (!TryResolveOverload(
                candidates: candidates,
                args: ImmutableArray.Create(right),
                getArgExprSyntax: _ => node.Right,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: overloadDiags,
                diagnosticNode: node))
            {
                if (IsOnlyNoApplicableOverload(overloadDiags))
                    return false;

                foreach (var d in overloadDiags.ToImmutable())
                    diagnostics.Add(d);
                return true;
            }

            if (chosen!.ReturnType.SpecialType != SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG_OP002",
                    DiagnosticSeverity.Error,
                    $"Direct compound operator '{chosen.Name}' must return void.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return true;
            }

            method = chosen;
            result = new BoundCallExpression(node, receiverOpt: leftTarget, chosen, convertedArgs);
            return true;

        }
        private static bool IsOnlyNoApplicableOverload(DiagnosticBag diagnostics)
        {
            var items = diagnostics.ToImmutable();
            if (items.IsDefaultOrEmpty)
                return false;

            for (int i = 0; i < items.Length; i++)
                if (!string.Equals(items[i].Id, "CN_OVL001", StringComparison.Ordinal))
                    return false;

            return true;
        }
        private bool TryBindUserDefinedCompoundAssignmentOperator(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(node);

            var names = GetCompoundAssignmentOperatorMetadataNames(op, IsCheckedOverflowContext);
            if (names.IsDefaultOrEmpty)
                return false;

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: left.Type,
                rightType: right.Type,
                metadataNames: names,
                parameterCount: 2,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(left, right);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: i => i == 0 ? node.Left : node.Right,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: node))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG_OP001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void in compound assignment.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return true;
            }

            result = new BoundCallExpression(node, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private bool TryBindDirectIncrementDecrementOperator(
            ExpressionSyntax operatorSyntax,
            BoundExpression target,
            bool isIncrement,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result,
            out MethodSymbol? method)
        {
            result = new BoundBadExpression(operatorSyntax);
            method = null;

            var names = IsCheckedOverflowContext
                ? ImmutableArray.Create(
                    isIncrement ? "op_CheckedIncrementAssignment" : "op_CheckedDecrementAssignment",
                    isIncrement ? "op_IncrementAssignment" : "op_DecrementAssignment")
                : ImmutableArray.Create(
                    isIncrement ? "op_IncrementAssignment" : "op_DecrementAssignment");

            var candidates = LookupInstanceUserDefinedOperatorMethods(
                receiverType: target.Type,
                metadataNames: names,
                parameterCount: 0,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var overloadDiags = new DiagnosticBag();
            if (!TryResolveOverload(
                candidates: candidates,
                args: ImmutableArray<BoundExpression>.Empty,
                getArgExprSyntax: _ => operatorSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: overloadDiags,
                diagnosticNode: operatorSyntax))
            {
                if (IsOnlyNoApplicableOverload(overloadDiags))
                    return false;

                foreach (var d in overloadDiags.ToImmutable())
                    diagnostics.Add(d);
                return true;
            }

            if (chosen!.ReturnType.SpecialType != SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INCDEC002",
                    DiagnosticSeverity.Error,
                    $"Direct increment/decrement operator '{chosen.Name}' must return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            method = chosen;
            result = new BoundCallExpression(operatorSyntax, receiverOpt: target, chosen, convertedArgs);
            return true;
        }
        private bool TryBindUserDefinedIncrementDecrementOperator(
            ExpressionSyntax operatorSyntax,
            ExpressionSyntax operandSyntax,
            BoundExpression operand,
            bool isIncrement,
            BindingContext context,
            DiagnosticBag diagnostics,
            out BoundExpression result)
        {
            result = new BoundBadExpression(operatorSyntax);

            var names = IsCheckedOverflowContext
                ? ImmutableArray.Create(
                    isIncrement ? "op_CheckedIncrement" : "op_CheckedDecrement",
                    isIncrement ? "op_Increment" : "op_Decrement")
                : ImmutableArray.Create(
                    isIncrement ? "op_Increment" : "op_Decrement");

            var candidates = LookupUserDefinedOperatorMethods(
                leftType: operand.Type,
                rightType: null,
                metadataNames: names,
                parameterCount: 1,
                context: context);

            if (candidates.IsDefaultOrEmpty)
                return false;

            var args = ImmutableArray.Create(operand);
            if (!TryResolveOverload(
                candidates: candidates,
                args: args,
                getArgExprSyntax: _ => operandSyntax,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: operatorSyntax))
            {
                return true;
            }

            if (chosen!.ReturnType.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_INCDEC001",
                    DiagnosticSeverity.Error,
                    $"Operator method '{chosen.Name}' cannot return void.",
                    new Location(context.SemanticModel.SyntaxTree, operatorSyntax.Span)));
                return true;
            }

            result = new BoundCallExpression(operatorSyntax, receiverOpt: null, chosen, convertedArgs);
            return true;
        }
        private ImmutableArray<MethodSymbol> LookupInstanceUserDefinedOperatorMethods(
    TypeSymbol receiverType,
    ImmutableArray<string> metadataNames,
    int parameterCount,
    BindingContext context)
        {
            var types = new List<NamedTypeSymbol>();
            var seenTypes = new HashSet<NamedTypeSymbol>();

            if (receiverType is NamedTypeSymbol nt)
            {
                for (NamedTypeSymbol? cur = nt; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
                {
                    if (seenTypes.Add(cur))
                        types.Add(cur);
                }
            }

            var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var t in types)
            {
                foreach (var m in t.GetMembers())
                {
                    if (m is not MethodSymbol ms)
                        continue;
                    if (ms.IsStatic || ms.IsConstructor)
                        continue;
                    if (ms.Parameters.Length != parameterCount)
                        continue;
                    if (!ContainsName(ms.Name, metadataNames))
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;

                    methods.Add(ms);
                }
            }

            return methods.ToImmutable();

            static bool ContainsName(string name, ImmutableArray<string> names)
            {
                for (int i = 0; i < names.Length; i++)
                    if (string.Equals(name, names[i], StringComparison.Ordinal))
                        return true;
                return false;
            }
        }
        private ImmutableArray<MethodSymbol> LookupUserDefinedOperatorMethods(
            TypeSymbol leftType,
            TypeSymbol? rightType,
            ImmutableArray<string> metadataNames,
            int parameterCount,
            BindingContext context)
        {
            var types = new List<NamedTypeSymbol>();
            var seenTypes = new HashSet<NamedTypeSymbol>();

            AddTypeAndBases(leftType);
            if (rightType is not null)
                AddTypeAndBases(rightType);

            var methods = ImmutableArray.CreateBuilder<MethodSymbol>();
            foreach (var t in types)
            {
                foreach (var m in t.GetMembers())
                {
                    if (m is not MethodSymbol ms)
                        continue;
                    if (!ms.IsStatic || ms.IsConstructor)
                        continue;
                    if (ms.Parameters.Length != parameterCount)
                        continue;
                    if (!ContainsName(ms.Name, metadataNames))
                        continue;
                    if (!AccessibilityHelper.IsAccessible(ms, context))
                        continue;

                    methods.Add(ms);
                }
            }

            return methods.ToImmutable();

            void AddTypeAndBases(TypeSymbol type)
            {
                if (type is not NamedTypeSymbol nt)
                    return;

                for (NamedTypeSymbol? cur = nt; cur is not null; cur = cur.BaseType as NamedTypeSymbol)
                {
                    if (seenTypes.Add(cur))
                        types.Add(cur);
                }
            }

            static bool ContainsName(string name, ImmutableArray<string> names)
            {
                for (int i = 0; i < names.Length; i++)
                    if (string.Equals(name, names[i], StringComparison.Ordinal))
                        return true;
                return false;
            }
        }

    }
}
