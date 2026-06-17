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
        private BoundExpression BindAssignment(AssignmentExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Left is ConditionalAccessExpressionSyntax conditionalLeft)
                return BindConditionalAssignment(node, conditionalLeft, context, diagnostics);

            if (node.Kind == SyntaxKind.SimpleAssignmentExpression && IsDeconstructionTargetSyntax(node.Left))
                return BindDeconstructionAssignment(node, context, diagnostics);

            var lv = BindLValue(node.Left, context, diagnostics, requireReadable: node.Kind != SyntaxKind.SimpleAssignmentExpression);
            var leftTarget = lv.Target;

            var right = BindExpression(node.Right, context, diagnostics);

            return BindAssignmentToLValue(node, lv, right, context, diagnostics);
        }
        private BoundExpression BindAssignmentToLValue(
            AssignmentExpressionSyntax node,
            LValue lv,
            BoundExpression right,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var leftTarget = lv.Target;
            bool hasErrors = leftTarget.HasErrors || right.HasErrors;

            // const local assignment check
            if (!leftTarget.HasErrors &&
                leftTarget is BoundLocalExpression le &&
                le.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic("CN_ASG001", DiagnosticSeverity.Error,
                    "Cannot assign to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                hasErrors = true;
            }
            if (!leftTarget.HasErrors && IsReadOnlyLocalTarget(leftTarget))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYLOCAL",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a readonly local.",
                    new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                hasErrors = true;
            }
            if (!leftTarget.HasErrors && IsReadOnlyByRefParameterTarget(leftTarget))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYREF001",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a readonly by-ref parameter.",
                    new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                hasErrors = true;
            }
            // Simple assignment
            if (node.Kind == SyntaxKind.SimpleAssignmentExpression)
            {
                if (!leftTarget.HasErrors)
                {
                    TypeSymbol assignmentTargetType = leftTarget.Type;
                    // ref reassignment for ref
                    if (right is BoundRefExpression)
                    {
                        if (leftTarget is BoundMemberAccessExpression { Member: FieldSymbol field } &&
                            field.Type is ByRefTypeSymbol refFieldType)
                        {
                            assignmentTargetType = refFieldType;
                        }
                        else if (leftTarget is BoundLocalExpression refLocalExpr && refLocalExpr.Local.IsByRef)
                        {
                            assignmentTargetType = context.Compilation.CreateByRefType(refLocalExpr.Local.Type);
                        }
                    }
                    else
                    {
                        // value assignment through ref
                        if (leftTarget is BoundMemberAccessExpression { Member: FieldSymbol field } &&
                            field.Type is ByRefTypeSymbol refFieldType)
                        {
                            assignmentTargetType = refFieldType.ElementType;
                        }
                    }

                    right = ApplyConversion(
                        exprSyntax: node.Right,
                        expr: right,
                        targetType: assignmentTargetType,
                        diagnosticNode: node,
                        context: context,
                        diagnostics: diagnostics,
                        requireImplicit: true);

                    hasErrors |= right.HasErrors;
                }

                var asg = new BoundAssignmentExpression(node, leftTarget, right);
                if (hasErrors) asg.SetHasErrors();
                return asg;
            }
            // ??=
            if (node.Kind == SyntaxKind.CoalesceAssignmentExpression)
            {
                if (leftTarget.HasErrors || right.HasErrors)
                    return new BoundBadExpression(node);

                if (!leftTarget.Type.IsReferenceType && !TryGetSystemNullableInfo(leftTarget.Type, out _, out _))
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_COALESCEASG000",
                        DiagnosticSeverity.Error,
                        "The lhs of '??=' must be a reference type or a nullable value type.",
                        new Location(context.SemanticModel.SyntaxTree, node.Left.Span)));
                    return new BoundBadExpression(node);
                }
                // Convert rhs to lhs type
                var converted = ApplyConversion(
                    exprSyntax: node.Right,
                    expr: right,
                    targetType: leftTarget.Type,
                    diagnosticNode: node,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

                if (converted.HasErrors)
                    return new BoundBadExpression(node);

                var asg = new BoundNullCoalescingAssignmentExpression(node, leftTarget, converted);
                if (hasErrors) asg.SetHasErrors();
                return asg;
            }
            // Compound assignment
            if (!TryMapCompoundAssignmentOperator(node.Kind, out var opKind))
            {
                diagnostics.Add(new Diagnostic("CN_ASG000", DiagnosticSeverity.Error,
                    $"Assignment form not supported: {node.Kind}",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            if (leftTarget.HasErrors || right.HasErrors)
                return new BoundBadExpression(node);

            var leftRead = lv.Read;

            if (IsDirectOperatorTarget(leftTarget) &&
                TryBindDirectCompoundAssignmentOperator(
                    node,
                    opKind,
                    leftTarget,
                    right,
                    context,
                    diagnostics,
                    out var directCall,
                    out var directMethod))
            {
                if (directCall.HasErrors)
                    return new BoundBadExpression(node);

                return new BoundCompoundAssignmentExpression(
                    node,
                    leftTarget,
                    opKind,
                    directCall,
                    operatorMethodOpt: directMethod,
                    usesDirectOperator: true,
                    isChecked: directMethod is not null &&
                               directMethod.Name.StartsWith("op_Checked", StringComparison.Ordinal));
            }

            var opResult = BindCompoundOperatorBinary(node, opKind, leftRead, right, context, diagnostics);
            if (opResult.HasErrors)
                return new BoundBadExpression(node);

            var valueToAssign = ApplyCompoundAssignmentConversion(
                syntaxForBoundNode: node,
                expr: opResult,
                targetType: leftTarget.Type,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics);

            bool isChecked = opResult is BoundBinaryExpression compoundOp && compoundOp.IsChecked;
            return new BoundCompoundAssignmentExpression(
                node,
                leftTarget,
                opKind,
                valueToAssign,
                operatorMethodOpt: null,
                usesDirectOperator: false,
                isChecked: isChecked);
        }
        private BoundExpression BindDeconstructionAssignment(
            AssignmentExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var right = BindExpression(node.Right, context, diagnostics);
            if (right.HasErrors)
                return new BoundBadExpression(node);

            var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
            var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

            var rhsTemp = NewTemp("$deconstruct_tmp", right.Type);
            locals.Add(rhsTemp);
            sideEffects.Add(new BoundLocalDeclarationStatement(node, rhsTemp, right));

            var rhsTempExpr = new BoundLocalExpression(node, rhsTemp);
            BindDeconstructionTarget(
                targetSyntax: node.Left,
                source: rhsTempExpr,
                sourceType: right.Type,
                context: context,
                diagnostics: diagnostics,
                locals: locals,
                sideEffects: sideEffects);

            return new BoundSequenceExpression(
                node,
                locals.ToImmutable(),
                sideEffects.ToImmutable(),
                rhsTempExpr);
        }
        private void BindDeconstructionTarget(
            ExpressionSyntax targetSyntax,
            BoundExpression source,
            TypeSymbol sourceType,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            switch (targetSyntax)
            {
                case ParenthesizedExpressionSyntax paren:
                    BindDeconstructionTarget(paren.Expression, source, sourceType, context, diagnostics, locals, sideEffects);
                    return;

                case TupleExpressionSyntax tuple:
                    BindTupleDeconstructionTarget(tuple, source, sourceType, context, diagnostics, locals, sideEffects);
                    return;

                case DeclarationExpressionSyntax declaration:
                    BindDeclarationDeconstructionTarget(declaration, source, sourceType, context, diagnostics, locals, sideEffects);
                    return;

                default:
                    BindLeafDeconstructionAssignment(targetSyntax, source, context, diagnostics, sideEffects);
                    return;
            }
        }

        private void BindTupleDeconstructionTarget(
            TupleExpressionSyntax tuple,
            BoundExpression source,
            TypeSymbol sourceType,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            var expectedTypes = ImmutableArray.CreateBuilder<TypeSymbol?>(tuple.Arguments.Count);
            for (int i = 0; i < tuple.Arguments.Count; i++)
            {
                var arg = tuple.Arguments[i];
                if (arg.NameColon is not null || arg.RefKindKeyword is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        "CN_DECONSTR003",
                        DiagnosticSeverity.Error,
                        "Named and ref tuple elements are not supported on the left-hand side of deconstruction.",
                        new Location(context.SemanticModel.SyntaxTree, arg.Span)));
                    expectedTypes.Add(null);
                    continue;
                }

                expectedTypes.Add(GetExpectedDeconstructionTargetType(arg.Expression, context));
            }

            if (!TryPrepareDeconstructionSource(
                diagnosticNode: tuple,
                source: source,
                sourceType: sourceType,
                targetArity: tuple.Arguments.Count,
                expectedTypes: expectedTypes.ToImmutable(),
                context: context,
                diagnostics: diagnostics,
                locals: locals,
                sideEffects: sideEffects,
                sourceElements: out var sourceElements,
                sourceElementTypes: out var sourceElementTypes))
            {
                return;
            }

            if (tuple.Arguments.Count != sourceElementTypes.Length)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DECONSTR002",
                    DiagnosticSeverity.Error,
                    $"Tuple deconstruction expects {tuple.Arguments.Count} element(s), but source has {sourceElementTypes.Length}.",
                    new Location(context.SemanticModel.SyntaxTree, tuple.Span)));
            }

            int n = Math.Min(tuple.Arguments.Count, sourceElementTypes.Length);
            for (int i = 0; i < n; i++)
            {
                var arg = tuple.Arguments[i];
                if (arg.NameColon is not null || arg.RefKindKeyword is not null)
                    continue;

                BindDeconstructionTarget(arg.Expression, sourceElements[i], sourceElementTypes[i], context, diagnostics, locals, sideEffects);
            }
        }
        private void BindDeclarationDeconstructionTarget(
            DeclarationExpressionSyntax declaration,
            BoundExpression source,
            TypeSymbol sourceType,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            if (declaration.Designation is SingleVariableDesignationSyntax single)
            {
                TypeSymbol localType;
                if (IsVar(declaration.Type))
                {
                    if (sourceType is NullTypeSymbol ||
                        sourceType.SpecialType == SpecialType.System_Void ||
                        sourceType is ErrorTypeSymbol)
                    {
                        diagnostics.Add(new Diagnostic(
                            "CN_DECONSTR004",
                            DiagnosticSeverity.Error,
                            "Cannot infer the type of a deconstruction variable from this source element.",
                            new Location(context.SemanticModel.SyntaxTree, declaration.Span)));
                        localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                    }
                    else
                    {
                        localType = sourceType;
                    }
                }
                else
                {
                    localType = BindType(declaration.Type, context, diagnostics);
                }

                BindSingleDeconstructionLocal(single, declaration, localType, source, context, diagnostics, locals, sideEffects);
                return;
            }

            if (declaration.Designation is ParenthesizedVariableDesignationSyntax paren)
            {
                TypeSymbol? explicitTargetType = IsVar(declaration.Type)
                    ? null
                    : BindType(declaration.Type, context, diagnostics);

                BindDeconstructionDesignation(
                    paren,
                    explicitTargetType,
                    source,
                    sourceType,
                    context,
                    diagnostics,
                    locals,
                    sideEffects);
                return;
            }

            if (declaration.Designation is DiscardDesignationSyntax)
                return;

            diagnostics.Add(new Diagnostic(
                "CN_DECONSTR005",
                DiagnosticSeverity.Error,
                "Unsupported declaration form in deconstruction target.",
                new Location(context.SemanticModel.SyntaxTree, declaration.Span)));
        }
        private void BindDeconstructionDesignation(
            VariableDesignationSyntax designation,
            TypeSymbol? explicitTargetType,
            BoundExpression source,
            TypeSymbol sourceType,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            switch (designation)
            {
                case DiscardDesignationSyntax:
                    return;

                case SingleVariableDesignationSyntax single:
                    {
                        var localType = explicitTargetType;
                        if (localType is null)
                        {
                            if (sourceType is NullTypeSymbol ||
                                sourceType.SpecialType == SpecialType.System_Void ||
                                sourceType is ErrorTypeSymbol)
                            {
                                diagnostics.Add(new Diagnostic(
                                    "CN_DECONSTR004",
                                    DiagnosticSeverity.Error,
                                    "Cannot infer the type of a deconstruction variable from this source element.",
                                    new Location(context.SemanticModel.SyntaxTree, designation.Span)));
                                localType = new ErrorTypeSymbol("var", containing: null, ImmutableArray<Location>.Empty);
                            }
                            else
                            {
                                localType = sourceType;
                            }
                        }

                        BindSingleDeconstructionLocal(single, designation, localType, source, context, diagnostics, locals, sideEffects);
                        return;
                    }

                case ParenthesizedVariableDesignationSyntax paren:
                    {
                        ImmutableArray<TypeSymbol?> expectedTypes;
                        if (explicitTargetType is null)
                        {
                            var unknown = ImmutableArray.CreateBuilder<TypeSymbol?>(paren.Variables.Count);
                            for (int i = 0; i < paren.Variables.Count; i++)
                                unknown.Add(null);
                            expectedTypes = unknown.ToImmutable();
                        }
                        else
                        {
                            if (!TryGetTupleElementTypes(explicitTargetType, out var targetElements))
                            {
                                diagnostics.Add(new Diagnostic(
                                    "CN_DECONSTR006",
                                    DiagnosticSeverity.Error,
                                    $"Type '{explicitTargetType.Name}' is not a tuple type and cannot be used with a parenthesized designation.",
                                    new Location(context.SemanticModel.SyntaxTree, paren.Span)));
                                return;
                            }

                            var targets = ImmutableArray.CreateBuilder<TypeSymbol?>(targetElements.Length);
                            for (int i = 0; i < targetElements.Length; i++)
                                targets.Add(targetElements[i]);
                            expectedTypes = targets.ToImmutable();
                        }

                        if (!TryPrepareDeconstructionSource(
                            diagnosticNode: paren,
                            source: source,
                            sourceType: sourceType,
                            targetArity: paren.Variables.Count,
                            expectedTypes: expectedTypes,
                            context: context,
                            diagnostics: diagnostics,
                            locals: locals,
                            sideEffects: sideEffects,
                            sourceElements: out var sourceElements,
                            sourceElementTypes: out var sourceElementTypes))
                        {
                            return;
                        }

                        if (paren.Variables.Count != sourceElementTypes.Length ||
                            (explicitTargetType is not null && expectedTypes.Length != sourceElementTypes.Length))
                        {
                            diagnostics.Add(new Diagnostic(
                                "CN_DECONSTR008",
                                DiagnosticSeverity.Error,
                                "Tuple deconstruction arity does not match the target designation.",
                                new Location(context.SemanticModel.SyntaxTree, paren.Span)));
                        }

                        int n = Math.Min(paren.Variables.Count, sourceElementTypes.Length);
                        if (explicitTargetType is not null)
                            n = Math.Min(n, expectedTypes.Length);

                        for (int i = 0; i < n; i++)
                        {
                            BindDeconstructionDesignation(
                                paren.Variables[i],
                                i < expectedTypes.Length ? expectedTypes[i] : null,
                                sourceElements[i],
                                sourceElementTypes[i],
                                context,
                                diagnostics,
                                locals,
                                sideEffects);
                        }
                        return;
                    }

                default:
                    diagnostics.Add(new Diagnostic(
                        "CN_DECONSTR009",
                        DiagnosticSeverity.Error,
                        "Unsupported variable designation in deconstruction.",
                        new Location(context.SemanticModel.SyntaxTree, designation.Span)));
                    return;
            }
        }
        private bool TryPrepareDeconstructionSource(
            SyntaxNode diagnosticNode,
            BoundExpression source,
            TypeSymbol sourceType,
            int targetArity,
            ImmutableArray<TypeSymbol?> expectedTypes,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects,
            out ImmutableArray<BoundExpression> sourceElements,
            out ImmutableArray<TypeSymbol> sourceElementTypes)
        {
            if (TryGetTupleElementTypes(sourceType, out var tupleElementTypes))
            {
                var values = ImmutableArray.CreateBuilder<BoundExpression>(tupleElementTypes.Length);
                for (int i = 0; i < tupleElementTypes.Length; i++)
                {
                    values.Add(MakeTupleElementRead(
                        SyntheticExpression(diagnosticNode),
                        source,
                        sourceType,
                        i,
                        tupleElementTypes[i]));
                }

                sourceElements = values.ToImmutable();
                sourceElementTypes = tupleElementTypes;
                return true;
            }

            if (TryPrepareUserDefinedDeconstructionSource(
                diagnosticNode,
                source,
                sourceType,
                targetArity,
                expectedTypes,
                context,
                diagnostics,
                locals,
                sideEffects,
                out sourceElements,
                out sourceElementTypes,
                out var userDefinedDiagnosticReported))
            {
                return true;
            }

            if (userDefinedDiagnosticReported)
                return false;

            diagnostics.Add(new Diagnostic(
                "CN_DECONSTR001",
                DiagnosticSeverity.Error,
                $"Cannot deconstruct source of type '{sourceType.Name}'.",
                new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

            sourceElements = ImmutableArray<BoundExpression>.Empty;
            sourceElementTypes = ImmutableArray<TypeSymbol>.Empty;
            return false;
        }
        private bool TryPrepareUserDefinedDeconstructionSource(
            SyntaxNode diagnosticNode,
            BoundExpression source,
            TypeSymbol sourceType,
            int targetArity,
            ImmutableArray<TypeSymbol?> expectedTypes,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects,
            out ImmutableArray<BoundExpression> sourceElements,
            out ImmutableArray<TypeSymbol> sourceElementTypes,
            out bool reportedDiagnostic)
        {
            sourceElements = ImmutableArray<BoundExpression>.Empty;
            sourceElementTypes = ImmutableArray<TypeSymbol>.Empty;
            reportedDiagnostic = false;

            if (sourceType is not NamedTypeSymbol receiverType)
                return false;

            if (!TryResolveDeconstructMethod(
                receiver: source,
                receiverType: receiverType,
                targetArity: targetArity,
                expectedTypes: expectedTypes,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: diagnosticNode,
                chosenMethod: out var chosenMethod,
                useExtensionForm: out var useExtensionForm,
                reportedDiagnostic: out reportedDiagnostic))
            {
                return false;
            }

            var callArgs = ImmutableArray.CreateBuilder<BoundExpression>();
            if (useExtensionForm)
            {
                var receiverParamType = chosenMethod!.Parameters[0].Type;
                var convertedReceiver = ApplyConversion(
                    exprSyntax: source.Syntax as ExpressionSyntax ?? SyntheticExpression(diagnosticNode),
                    expr: source,
                    targetType: receiverParamType,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);
                callArgs.Add(convertedReceiver);
            }

            var elementTypes = ImmutableArray.CreateBuilder<TypeSymbol>(targetArity);
            var elementValues = ImmutableArray.CreateBuilder<BoundExpression>(targetArity);
            int baseParamIndex = useExtensionForm ? 1 : 0;

            for (int i = 0; i < targetArity; i++)
            {
                var param = chosenMethod!.Parameters[baseParamIndex + i];
                var elementType = GetDeconstructOutElementType(param);

                var temp = NewTemp("$deconstruct_out", elementType);
                locals.Add(temp);

                var tempExpr = new BoundLocalExpression(diagnosticNode, temp);
                var tempRefType = context.Compilation.CreateByRefType(elementType);
                callArgs.Add(new BoundRefExpression(diagnosticNode, tempRefType, tempExpr));

                elementTypes.Add(elementType);
                elementValues.Add(tempExpr);
            }

            var call = new BoundCallExpression(
                diagnosticNode,
                receiverOpt: useExtensionForm ? null : source,
                method: chosenMethod!,
                arguments: callArgs.ToImmutable());

            sideEffects.Add(new BoundExpressionStatement(diagnosticNode, call));

            sourceElements = elementValues.ToImmutable();
            sourceElementTypes = elementTypes.ToImmutable();
            return true;
        }
        private bool TryResolveDeconstructMethod(
            BoundExpression receiver,
            NamedTypeSymbol receiverType,
            int targetArity,
            ImmutableArray<TypeSymbol?> expectedTypes,
            BindingContext context,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            out MethodSymbol? chosenMethod,
            out bool useExtensionForm,
            out bool reportedDiagnostic)
        {
            chosenMethod = null;
            useExtensionForm = false;
            reportedDiagnostic = false;

            var instanceCandidates = LookupMethods(receiverType, "Deconstruct")
                .Where(m => !m.IsStatic && AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (TryChooseBestDeconstructCandidate(
                candidates: instanceCandidates,
                receiver: receiver,
                targetArity: targetArity,
                expectedTypes: expectedTypes,
                context: context,
                useExtensionForm: false,
                diagnostics: diagnostics,
                diagnosticNode: diagnosticNode,
                chosenMethod: out chosenMethod,
                reportedDiagnostic: out reportedDiagnostic))
            {
                return true;
            }

            if (reportedDiagnostic)
                return false;

            var extensionCandidates = LookupExtensionMethods("Deconstruct", receiver, context)
                .Where(m => AccessibilityHelper.IsAccessible(m, context))
                .ToImmutableArray();

            if (TryChooseBestDeconstructCandidate(
                candidates: extensionCandidates,
                receiver: receiver,
                targetArity: targetArity,
                expectedTypes: expectedTypes,
                context: context,
                useExtensionForm: true,
                diagnostics: diagnostics,
                diagnosticNode: diagnosticNode,
                chosenMethod: out chosenMethod,
                reportedDiagnostic: out reportedDiagnostic))
            {
                useExtensionForm = true;
                return true;
            }

            return false;
        }
        private bool TryChooseBestDeconstructCandidate(
            ImmutableArray<MethodSymbol> candidates,
            BoundExpression receiver,
            int targetArity,
            ImmutableArray<TypeSymbol?> expectedTypes,
            BindingContext context,
            bool useExtensionForm,
            DiagnosticBag diagnostics,
            SyntaxNode diagnosticNode,
            out MethodSymbol? chosenMethod,
            out bool reportedDiagnostic)
        {
            chosenMethod = null;
            reportedDiagnostic = false;
            int bestScore = int.MaxValue;
            bool ambiguous = false;
            bool sawNameMatch = false;
            bool sawArityMatch = false;

            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!StringComparer.Ordinal.Equals(candidate.Name, "Deconstruct"))
                    continue;

                sawNameMatch = true;

                if (!IsValidDeconstructMethodShape(candidate, targetArity, useExtensionForm))
                    continue;

                sawArityMatch = true;

                int score = useExtensionForm ? 10 : 0;
                int baseParamIndex = useExtensionForm ? 1 : 0;

                if (useExtensionForm)
                {
                    var receiverConv = ClassifyConversion(receiver, candidate.Parameters[0].Type, context);
                    if (!receiverConv.Exists || !receiverConv.IsImplicit)
                        continue;

                    score += GetDeconstructConversionScore(receiverConv.Kind);
                }

                bool applicable = true;
                for (int p = 0; p < targetArity; p++)
                {
                    var expectedType = p < expectedTypes.Length ? expectedTypes[p] : null;
                    if (expectedType is null)
                        continue;

                    var elementType = GetDeconstructOutElementType(candidate.Parameters[baseParamIndex + p]);
                    if (!AreSameType(expectedType, elementType))
                    {
                        applicable = false;
                        break;
                    }
                }

                if (!applicable)
                    continue;

                if (score < bestScore)
                {
                    bestScore = score;
                    chosenMethod = candidate;
                    ambiguous = false;
                }
                else if (score == bestScore)
                {
                    ambiguous = true;
                }
            }

            if (chosenMethod is not null && !ambiguous)
                return true;

            if (ambiguous)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DECONSTR011",
                    DiagnosticSeverity.Error,
                    "Deconstruction is ambiguous between multiple 'Deconstruct' overloads.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                reportedDiagnostic = true;
                return false;
            }

            if (sawNameMatch && !sawArityMatch)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DECONSTR012",
                    DiagnosticSeverity.Error,
                    $"No 'Deconstruct' overload on type '{receiver.Type.Name}' accepts {targetArity} out parameter(s).",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));
                reportedDiagnostic = true;
                return false;
            }

            return false;
        }

        private static bool IsValidDeconstructMethodShape(MethodSymbol method, int targetArity, bool useExtensionForm)
        {
            if (method is null)
                return false;

            if (method.TypeParameters.Length != 0)
                return false;

            if (method.ReturnType.SpecialType != SpecialType.System_Void)
                return false;

            if (useExtensionForm)
            {
                if (!IsExtensionMethod(method))
                    return false;
                if (method.Parameters.Length != targetArity + 1)
                    return false;
            }
            else
            {
                if (method.IsStatic)
                    return false;
                if (method.Parameters.Length != targetArity)
                    return false;
            }

            int baseParamIndex = useExtensionForm ? 1 : 0;
            for (int i = baseParamIndex; i < method.Parameters.Length; i++)
            {
                var p = method.Parameters[i];
                if (p.RefKind != ParameterRefKind.Out)
                    return false;
                if (p.Type is not ByRefTypeSymbol)
                    return false;
            }

            return true;
        }

        private static TypeSymbol GetDeconstructOutElementType(ParameterSymbol parameter)
        {
            return parameter.Type is ByRefTypeSymbol br ? br.ElementType : parameter.Type;
        }

        private static int GetDeconstructConversionScore(ConversionKind kind)
        {
            return kind switch
            {
                ConversionKind.Identity => 0,
                ConversionKind.ImplicitNumeric => 1,
                ConversionKind.ImplicitConstant => 1,
                ConversionKind.ImplicitReference => 1,
                ConversionKind.Boxing => 2,
                ConversionKind.ImplicitNullable => 2,
                ConversionKind.NullLiteral => 2,
                ConversionKind.ExplicitNumeric => 4,
                ConversionKind.ExplicitReference => 4,
                ConversionKind.Unboxing => 4,
                _ => 10
            };
        }

        private TypeSymbol? GetExpectedDeconstructionTargetType(
            ExpressionSyntax targetSyntax,
            BindingContext context)
        {
            switch (targetSyntax)
            {
                case ParenthesizedExpressionSyntax paren:
                    return GetExpectedDeconstructionTargetType(paren.Expression, context);

                case DeclarationExpressionSyntax declaration:
                    if (declaration.Designation is SingleVariableDesignationSyntax)
                        return IsVar(declaration.Type) ? null : BindType(declaration.Type, context, new DiagnosticBag());
                    return null;

                case TupleExpressionSyntax:
                    return null;

                default:
                    var probeDiagnostics = new DiagnosticBag();
                    var lv = BindLValue(targetSyntax, context, probeDiagnostics, requireReadable: false);
                    return lv.Target.HasErrors ? null : lv.Target.Type;
            }
        }
        private void BindSingleDeconstructionLocal(
            SingleVariableDesignationSyntax single,
            SyntaxNode ownerSyntax,
            TypeSymbol localType,
            BoundExpression source,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<LocalSymbol>.Builder locals,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            var name = single.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
                return;

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DECONSTR010",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, single.Span)));
            }

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: localType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, single.Span)),
                isConst: false,
                constantValueOpt: Optional<object>.None,
                isByRef: false);

            _locals[name] = local;

            context.Recorder.RecordDeclared(single, local);

            var initializer = ApplyConversion(
                exprSyntax: source.Syntax as ExpressionSyntax ?? SyntheticExpression(ownerSyntax),
                expr: source,
                targetType: localType,
                diagnosticNode: ownerSyntax,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            sideEffects.Add(new BoundLocalDeclarationStatement(ownerSyntax, local, initializer));
        }

        private void BindLeafDeconstructionAssignment(
            ExpressionSyntax targetSyntax,
            BoundExpression source,
            BindingContext context,
            DiagnosticBag diagnostics,
            ImmutableArray<BoundStatement>.Builder sideEffects)
        {
            if (IsDeconstructionDiscardTarget(targetSyntax))
                return;

            var lv = BindLValue(targetSyntax, context, diagnostics, requireReadable: false);
            var target = lv.Target;
            bool hasErrors = target.HasErrors || source.HasErrors;

            if (!target.HasErrors && target is BoundLocalExpression le && le.Local.IsConst)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG001",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a const local.",
                    new Location(context.SemanticModel.SyntaxTree, targetSyntax.Span)));
                hasErrors = true;
            }
            if (!target.HasErrors && IsReadOnlyLocalTarget(target))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYLOCAL",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a readonly local.",
                    new Location(context.SemanticModel.SyntaxTree, targetSyntax.Span)));
                hasErrors = true;
            }
            if (!target.HasErrors && IsReadOnlyByRefParameterTarget(target))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_READONLYREF001",
                    DiagnosticSeverity.Error,
                    "Cannot assign to a readonly by-ref parameter.",
                    new Location(context.SemanticModel.SyntaxTree, targetSyntax.Span)));
                hasErrors = true;
            }

            var converted = target.HasErrors
                ? source
                : ApplyConversion(
                    exprSyntax: source.Syntax as ExpressionSyntax ?? SyntheticExpression(targetSyntax),
                    expr: source,
                    targetType: target.Type,
                    diagnosticNode: targetSyntax,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: true);

            hasErrors |= converted.HasErrors;

            var assignment = new BoundAssignmentExpression(targetSyntax, target, converted);
            if (hasErrors)
                assignment.SetHasErrors();

            sideEffects.Add(new BoundExpressionStatement(targetSyntax, assignment));
        }
        private bool IsDeconstructionDiscardTarget(ExpressionSyntax targetSyntax)
        {
            if (targetSyntax is ParenthesizedExpressionSyntax paren)
                return IsDeconstructionDiscardTarget(paren.Expression);

            return targetSyntax is IdentifierNameSyntax id &&
                StringComparer.Ordinal.Equals(id.Identifier.ValueText, "_") &&
                !IsNameDeclaredInEnclosingScopes("_");
        }
        private BoundExpression MakeTupleElementRead(
            ExpressionSyntax syntax,
            BoundExpression receiver,
            TypeSymbol receiverType,
            int elementIndex,
            TypeSymbol elementType)
        {
            if (receiverType is TupleTypeSymbol tuple)
            {
                var field = new TupleElementFieldSymbol("Item" + (elementIndex + 1).ToString(), tuple, elementIndex, elementType);
                return new BoundMemberAccessExpression(syntax, receiver, field, elementType, isLValue: false);
            }

            if (receiverType is NamedTypeSymbol valueTupleType && IsSystemValueTupleType(valueTupleType))
            {
                BoundExpression current = receiver;
                NamedTypeSymbol currentType = valueTupleType;
                int index = elementIndex;

                while (currentType.Arity == 8 && index >= 7)
                {
                    var restField = FindInstanceField(currentType, "Rest");
                    if (restField is null || restField.Type is not NamedTypeSymbol nextType)
                        return new BoundBadExpression(syntax);

                    current = new BoundMemberAccessExpression(syntax, current, restField, restField.Type, isLValue: false);
                    currentType = nextType;
                    index -= 7;
                }

                var itemField = FindInstanceField(currentType, "Item" + (index + 1).ToString());
                if (itemField is null)
                    return new BoundBadExpression(syntax);

                return new BoundMemberAccessExpression(syntax, current, itemField, elementType, isLValue: false);
            }

            return new BoundBadExpression(syntax);
        }

        private static bool TryGetTupleElementTypes(TypeSymbol type, out ImmutableArray<TypeSymbol> elementTypes)
        {
            if (type is TupleTypeSymbol tuple)
            {
                elementTypes = tuple.ElementTypes;
                return true;
            }

            if (type is NamedTypeSymbol named && IsSystemValueTupleType(named))
            {
                var builder = ImmutableArray.CreateBuilder<TypeSymbol>();
                if (TryAppendValueTupleElementTypes(named, builder))
                {
                    elementTypes = builder.ToImmutable();
                    return true;
                }
            }

            elementTypes = ImmutableArray<TypeSymbol>.Empty;
            return false;
        }

        private static bool TryAppendValueTupleElementTypes(NamedTypeSymbol named, ImmutableArray<TypeSymbol>.Builder builder)
        {
            if (!IsSystemValueTupleType(named))
                return false;

            var args = named.TypeArguments;
            int arity = args.Length;

            if (arity <= 7)
            {
                for (int i = 0; i < arity; i++)
                    builder.Add(args[i]);
                return true;
            }

            if (arity != 8)
                return false;

            for (int i = 0; i < 7; i++)
                builder.Add(args[i]);

            return args[7] is NamedTypeSymbol rest && TryAppendValueTupleElementTypes(rest, builder);
        }

        private static bool IsSystemValueTupleType(NamedTypeSymbol named)
        {
            var def = named.OriginalDefinition;
            return StringComparer.Ordinal.Equals(def.Name, "ValueTuple") &&
                   IsNamespaceFullName(def.ContainingSymbol, "System");
        }
        private static bool IsNamespaceFullName(Symbol? symbol, string fullName)
        {
            if (symbol is not NamespaceSymbol ns)
                return false;

            if (ns.IsGlobalNamespace)
                return fullName.Length == 0;

            var parts = new Stack<string>();
            for (Symbol? cur = ns; cur is NamespaceSymbol n && !n.IsGlobalNamespace; cur = n.ContainingSymbol)
                parts.Push(n.Name);

            var sb = new StringBuilder();
            bool first = true;
            foreach (var part in parts)
            {
                if (!first)
                    sb.Append('.');
                sb.Append(part);
                first = false;
            }

            return StringComparer.Ordinal.Equals(sb.ToString(), fullName);
        }
        private static FieldSymbol? FindInstanceField(NamedTypeSymbol type, string name)
        {
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is FieldSymbol field &&
                    !field.IsStatic &&
                    StringComparer.Ordinal.Equals(field.Name, name))
                {
                    return field;
                }
            }

            return null;
        }
        private static ExpressionSyntax SyntheticExpression(SyntaxNode syntax)
            => syntax as ExpressionSyntax ?? new IdentifierNameSyntax(default);
        private static bool IsDeconstructionTargetSyntax(ExpressionSyntax syntax)
        {
            if (syntax is null)
                return false;

            return syntax switch
            {
                TupleExpressionSyntax => true,
                DeclarationExpressionSyntax de when de.Designation is ParenthesizedVariableDesignationSyntax => true,
                ParenthesizedExpressionSyntax p => IsDeconstructionTargetSyntax(p.Expression),
                _ => false
            };
        }
        private static bool IsDirectOperatorTarget(BoundExpression target)
        {
            return target switch
            {
                BoundLocalExpression => true,
                BoundParameterExpression => true,
                BoundArrayElementAccessExpression => true,
                BoundInlineArrayElementAccessExpression => true,
                BoundPointerIndirectionExpression => true,
                BoundPointerElementAccessExpression => true,
                BoundMemberAccessExpression { Member: FieldSymbol } => true,
                BoundCallExpression call when call.IsLValue => true,
                _ => false
            };
        }
        private BoundExpression BindInterpolatedString(
            InterpolatedStringExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);
            bool canBeConst = true;
            var constBuilder = new StringBuilder();

            var parts = new List<BoundExpression>(capacity: Math.Max(1, node.Contents.Count));

            for (int i = 0; i < node.Contents.Count; i++)
            {
                var c = node.Contents[i];

                if (c is InterpolatedStringTextSyntax text)
                {
                    var tok = text.TextToken;
                    var s = tok.Value as string ?? tok.ValueText ?? string.Empty;

                    if (canBeConst)
                        constBuilder.Append(s);

                    if (s.Length != 0)
                        parts.Add(new BoundLiteralExpression(text, stringType, s));

                    continue;
                }

                if (c is InterpolationSyntax interp)
                {
                    if (interp.AlignmentClause is not null)
                    {
                        diagnostics.Add(new Diagnostic(
                            id: "CN_INTERP_ALIGN000",
                            severity: DiagnosticSeverity.Error,
                            message: "Interpolation alignment is not supported.",
                            location: new Location(context.SemanticModel.SyntaxTree, interp.AlignmentClause.Span)));
                    }

                    var expr = BindExpression(interp.Expression, context, diagnostics);

                    if (expr.Type.SpecialType == SpecialType.System_Void)
                    {
                        diagnostics.Add(new Diagnostic(
                            id: "CN_INTERP_VOID000",
                            severity: DiagnosticSeverity.Error,
                            message: "Cannot use an expression of type 'void' in an interpolated string.",
                            location: new Location(context.SemanticModel.SyntaxTree, interp.Expression.Span)));

                        expr = new BoundBadExpression(interp);
                    }

                    if (interp.FormatClause is { } formatClause && !expr.HasErrors)
                        expr = BindInterpolationFormatSpecifier(interp, formatClause, expr, context, diagnostics);

                    parts.Add(expr);

                    if (canBeConst)
                    {
                        if (interp.AlignmentClause is not null || interp.FormatClause is not null)
                        {
                            canBeConst = false;
                        }
                        else if (!expr.ConstantValueOpt.HasValue || expr.Type.SpecialType != SpecialType.System_String)
                        {
                            canBeConst = false;
                        }
                        else
                        {
                            constBuilder.Append((string?)expr.ConstantValueOpt.Value ?? string.Empty);
                        }
                    }

                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    id: "CN_INTERP_UNK000",
                    severity: DiagnosticSeverity.Error,
                    message: $"Unsupported interpolated string content: {c.Kind}",
                    location: new Location(context.SemanticModel.SyntaxTree, c.Span)));
            }

            if (canBeConst)
                return new BoundLiteralExpression(node, stringType, constBuilder.ToString());

            if (parts.Count == 0)
                return new BoundLiteralExpression(node, stringType, string.Empty);

            BoundExpression acc = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                acc = new BoundBinaryExpression(
                    node,
                    BoundBinaryOperatorKind.StringConcatenation,
                    stringType,
                    acc,
                    parts[i],
                    Optional<object>.None);
            }

            return acc;
        }
        private BoundExpression BindInterpolationFormatSpecifier(
            InterpolationSyntax interpolation,
            InterpolationFormatClauseSyntax formatClause,
            BoundExpression value,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);

            var formatText = formatClause.FormatStringToken.Value as string
                ?? formatClause.FormatStringToken.ValueText
                ?? string.Empty;

            var receiverType = GetReceiverTypeForMemberLookup(value.Type);
            if (receiverType is null)
            {
                diagnostics.Add(new Diagnostic(
                    id: "CN_INTERP_FMT001",
                    severity: DiagnosticSeverity.Error,
                    message: $"Type '{value.Type.Name}' cannot be used with an interpolation format specifier.",
                    location: new Location(context.SemanticModel.SyntaxTree, formatClause.Span)));

                return new BoundBadExpression(interpolation);
            }

            var candidates = LookupMethods(receiverType, "ToString")
                .Where(m =>
                    !m.IsStatic &&
                    AccessibilityHelper.IsAccessible(m, context) &&
                    m.TypeParameters.IsDefaultOrEmpty &&
                    m.ReturnType.SpecialType == SpecialType.System_String &&
                    m.Parameters.Length == 1 &&
                    m.Parameters[0].RefKind == ParameterRefKind.None &&
                    m.Parameters[0].Type.SpecialType == SpecialType.System_String)
                .ToImmutableArray();

            if (candidates.IsDefaultOrEmpty)
            {
                diagnostics.Add(new Diagnostic(
                    id: "CN_INTERP_FMT002",
                    severity: DiagnosticSeverity.Error,
                    message: $"Interpolation format specifier ':{formatText}' cannot be applied to expression of type '{value.Type.Name}': " +
                    $"no accessible instance method was found.",
                    location: new Location(context.SemanticModel.SyntaxTree, formatClause.Span)));

                return new BoundBadExpression(interpolation);
            }

            var formatArg = new BoundLiteralExpression(formatClause, stringType, formatText);

            if (!TryResolveOverload(
                candidates: candidates,
                args: ImmutableArray.Create<BoundExpression>(formatArg),
                getArgExprSyntax: _ => interpolation.Expression,
                chosen: out var chosen,
                convertedArgs: out var convertedArgs,
                context: context,
                diagnostics: diagnostics,
                diagnosticNode: formatClause))
            {
                return new BoundBadExpression(interpolation);
            }

            return new BoundCallExpression(
                interpolation,
                receiverOpt: value,
                method: chosen!,
                arguments: convertedArgs);
        }
        private BoundExpression BindStringConcatenation(
            ExpressionSyntax syntax,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            var stringType = ctx.Compilation.GetSpecialType(SpecialType.System_String);

            if (left.Type.SpecialType == SpecialType.System_Void || right.Type.SpecialType == SpecialType.System_Void)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_STRCONCAT001",
                    DiagnosticSeverity.Error,
                    "Operator '+' cannot be applied to operands of type 'void'.",
                    new Location(ctx.SemanticModel.SyntaxTree, syntax.Span)));
                return new BoundBadExpression(syntax);
            }

            if (left.Type is NullTypeSymbol || (left.ConstantValueOpt.HasValue && left.ConstantValueOpt.Value is null))
                left = ApplyConversion((ExpressionSyntax)left.Syntax, left, stringType, syntax, ctx, diagnostics, requireImplicit: true);

            if (right.Type is NullTypeSymbol || (right.ConstantValueOpt.HasValue && right.ConstantValueOpt.Value is null))
                right = ApplyConversion((ExpressionSyntax)right.Syntax, right, stringType, syntax, ctx, diagnostics, requireImplicit: true);

            if (left.HasErrors || right.HasErrors)
                return new BoundBadExpression(syntax);

            var cv = FoldStringConcatConstant(left, right);

            return new BoundBinaryExpression(
                syntax,
                BoundBinaryOperatorKind.StringConcatenation,
                stringType,
                left,
                right,
                cv);
        }
        private static Optional<object> FoldStringConcatConstant(BoundExpression left, BoundExpression right)
        {
            if (!left.ConstantValueOpt.HasValue || !right.ConstantValueOpt.HasValue)
                return Optional<object>.None;

            if (!TryConstToString(left, out var ls) || !TryConstToString(right, out var rs))
                return Optional<object>.None;

            return new Optional<object>(string.Concat(ls, rs));
            static bool TryConstToString(BoundExpression e, out string s)
            {
                var v = e.ConstantValueOpt.Value;

                if (v is null)
                {
                    s = "";
                    return true;
                }
                if (v is string str)
                {
                    s = str;
                    return true;
                }

                s = "";
                return false;
            }
        }
        private static bool TryMapCompoundAssignmentOperator(SyntaxKind assignmentKind, out BoundBinaryOperatorKind opKind)
        {
            switch (assignmentKind)
            {
                case SyntaxKind.AddAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Add; return true;
                case SyntaxKind.SubtractAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Subtract; return true;
                case SyntaxKind.MultiplyAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Multiply; return true;
                case SyntaxKind.DivideAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Divide; return true;
                case SyntaxKind.ModuloAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.Modulo; return true;

                case SyntaxKind.AndAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.BitwiseAnd; return true;
                case SyntaxKind.OrAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.BitwiseOr; return true;
                case SyntaxKind.ExclusiveOrAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.ExclusiveOr; return true;

                case SyntaxKind.LeftShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.LeftShift; return true;
                case SyntaxKind.RightShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.RightShift; return true;

                case SyntaxKind.UnsignedRightShiftAssignmentExpression:
                    opKind = BoundBinaryOperatorKind.UnsignedRightShift; return true;

                default:
                    opKind = default;
                    return false;
            }
        }
        private BoundExpression BindCompoundOperatorBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if ((op == BoundBinaryOperatorKind.Add || op == BoundBinaryOperatorKind.Subtract) &&
                left.Type is NamedTypeSymbol { TypeKind: TypeKind.Delegate } &&
                ClassifyConversion(right, left.Type, ctx) is { Exists: true, IsImplicit: true })
            {
                return BindDelegateCompoundAssignment(node, op, left, right, ctx, diagnostics);
            }

            if (TryBindUserDefinedCompoundAssignmentOperator(
                node: node,
                op: op,
                left: left,
                right: right,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            switch (op)
            {
                case BoundBinaryOperatorKind.Add:
                case BoundBinaryOperatorKind.Subtract:
                case BoundBinaryOperatorKind.Multiply:
                case BoundBinaryOperatorKind.Divide:
                case BoundBinaryOperatorKind.Modulo:
                    return BindCompoundNumericBinary(node, op, left, right, ctx, diagnostics);

                case BoundBinaryOperatorKind.BitwiseAnd:
                case BoundBinaryOperatorKind.BitwiseOr:
                case BoundBinaryOperatorKind.ExclusiveOr:
                    return BindCompoundBitwiseBinary(node, op, left, right, ctx, diagnostics);

                case BoundBinaryOperatorKind.LeftShift:
                case BoundBinaryOperatorKind.RightShift:
                case BoundBinaryOperatorKind.UnsignedRightShift:
                    return BindCompoundShiftBinary(node, op, left, right, ctx, diagnostics);

                default:
                    diagnostics.Add(new Diagnostic("CN_ASG_OP000", DiagnosticSeverity.Error,
                        $"Compound assignment operator not supported: {op}",
                        new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                    return new BoundBadExpression(node);
            }
        }
        private BoundExpression BindDelegateCompoundAssignment(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            string methodName = op == BoundBinaryOperatorKind.Add ? "Combine" : "Remove";

            if (!TryFindSystemDelegateMethod(ctx.Compilation, methodName, out var method))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_DELEGATE_COMBINE000",
                    DiagnosticSeverity.Error,
                    $"System.Delegate.{methodName}(Delegate, Delegate) is required for delegate compound assignment.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var delegateParamType = method.Parameters[0].Type;

            var leftArg = ApplyConversion(
                exprSyntax: node.Left,
                expr: left,
                targetType: delegateParamType,
                diagnosticNode: node,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            var rightAsLeftType = ApplyConversion(
                exprSyntax: node.Right,
                expr: right,
                targetType: left.Type,
                diagnosticNode: node,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            var rightArg = ApplyConversion(
                exprSyntax: node.Right,
                expr: rightAsLeftType,
                targetType: delegateParamType,
                diagnosticNode: node,
                context: ctx,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (leftArg.HasErrors || rightArg.HasErrors)
                return new BoundBadExpression(node);

            return new BoundCallExpression(
                node,
                receiverOpt: null,
                method: method,
                arguments: ImmutableArray.Create(leftArg, rightArg));
        }

        private static bool TryFindSystemDelegateMethod(Compilation compilation, string name, out MethodSymbol method)
        {
            method = null!;

            if (!DeclarationBuilder.TryFindSystemType(compilation.GlobalNamespace, "Delegate", arity: 0, out var delegateType))
                return false;

            var methods = LookupMethods(delegateType, name);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (!m.IsStatic)
                    continue;
                if (m.Parameters.Length != 2)
                    continue;
                if (!StringComparer.Ordinal.Equals(m.Name, name))
                    continue;
                if (ReferenceEquals(m.Parameters[0].Type, delegateType) &&
                    ReferenceEquals(m.Parameters[1].Type, delegateType))
                {
                    method = m;
                    return true;
                }
            }

            return false;
        }

        private BoundExpression BindCompoundNumericBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            if (op == BoundBinaryOperatorKind.Add &&
                (left.Type.SpecialType == SpecialType.System_String
                || right.Type.SpecialType == SpecialType.System_String))
            {
                return BindStringConcatenation(node, left, right, ctx, diagnostics);
            }
            if (TryBindPointerArithmeticBinary(
                diagnosticNode: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                ctx: ctx,
                diagnostics: diagnostics,
                out var ptrArith))
            {
                return ptrArith;
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, node, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, promoted, node, ctx, diagnostics, requireImplicit: true);
            var rightConv = ApplyConversion(node.Right, right, promoted, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            bool isChecked = IsCheckedOverflowContext && IsOverflowCheckedBinaryOperator(op, promoted);
            var constValue = FoldBinaryConstant(op, promoted, leftConv, rightConv, isChecked);
            return new BoundBinaryExpression(node, op, promoted, leftConv, rightConv, constValue, isChecked);
        }
        private BoundExpression BindCompoundBitwiseBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }

            var boolType = ctx.Compilation.GetSpecialType(SpecialType.System_Boolean);

            // bool
            if (left.Type.SpecialType == SpecialType.System_Boolean &&
                right.Type.SpecialType == SpecialType.System_Boolean)
            {
                return new BoundBinaryExpression(node, op, boolType, left, right, Optional<object>.None);
            }
            // enum op enum
            if (IsEnumType(left.Type) && ReferenceEquals(left.Type, right.Type))
            {
                var underlying = GetEnumUnderlyingTypeOrDefault(ctx.Compilation, left.Type);
                var constValue = FoldBitwiseConstant(op, underlying.SpecialType, left, right);
                return new BoundBinaryExpression(node, op, left.Type, left, right, constValue);
            }
            // integral only
            if (!IsIntegral(left.Type.SpecialType) || !IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_BIT000", DiagnosticSeverity.Error,
                    $"Bitwise operator requires integral operands (or bool/bool), got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var promoted = GetBinaryNumericPromotionType(ctx.Compilation, ctx.SemanticModel.SyntaxTree, left, right, node, diagnostics);
            if (promoted is null || promoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, promoted, node, ctx, diagnostics, requireImplicit: true);
            var rightConv = ApplyConversion(node.Right, right, promoted, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            return new BoundBinaryExpression(node, op, promoted, leftConv, rightConv, Optional<object>.None);
        }

        private BoundExpression BindCompoundShiftBinary(
            AssignmentExpressionSyntax node,
            BoundBinaryOperatorKind op,
            BoundExpression left,
            BoundExpression right,
            BindingContext ctx,
            DiagnosticBag diagnostics)
        {
            if (TryBindUserDefinedBinaryOperator(
                operatorSyntax: node,
                leftSyntax: node.Left,
                left: left,
                rightSyntax: node.Right,
                right: right,
                op: op,
                context: ctx,
                diagnostics: diagnostics,
                out var userDefined))
            {
                return userDefined;
            }
            if (!IsIntegral(left.Type.SpecialType) || !IsIntegral(right.Type.SpecialType))
            {
                diagnostics.Add(new Diagnostic("CN_SHIFT000", DiagnosticSeverity.Error,
                    $"Shift operator requires integral operands, got '{left.Type.Name}' and '{right.Type.Name}'.",
                    new Location(ctx.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadExpression(node);
            }

            var leftPromoted = GetUnaryPromotionType(
                ctx.Compilation,
                ctx.SemanticModel.SyntaxTree,
                BoundUnaryOperatorKind.UnaryPlus,
                left.Type,
                node,
                diagnostics);

            if (leftPromoted is null || leftPromoted is ErrorTypeSymbol)
                return new BoundBadExpression(node);

            var leftConv = ApplyConversion(node.Left, left, leftPromoted, node, ctx, diagnostics, requireImplicit: true);

            var intType = ctx.Compilation.GetSpecialType(SpecialType.System_Int32);
            var rightConv = ApplyConversion(node.Right, right, intType, node, ctx, diagnostics, requireImplicit: true);

            if (leftConv.HasErrors || rightConv.HasErrors)
                return new BoundBadExpression(node);

            // shift result type is leftPromoted
            return new BoundBinaryExpression(node, op, leftPromoted, leftConv, rightConv, Optional<object>.None);
        }
        private BoundExpression ApplyCompoundAssignmentConversion(
            ExpressionSyntax syntaxForBoundNode,
            BoundExpression expr,
            TypeSymbol targetType,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (ReferenceEquals(expr.Type, targetType))
                return expr;

            var conv = ClassifyConversion(expr, targetType, context);

            bool ok =
                conv.Exists &&
                (conv.IsImplicit ||
                conv.Kind
                is ConversionKind.ExplicitNumeric
                or ConversionKind.ExplicitReference
                or ConversionKind.Unboxing
                or ConversionKind.UserDefined);

            if (!ok)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_CASG001",
                    DiagnosticSeverity.Error,
                    $"No conversion from '{expr.Type.Name}' to '{targetType.Name}' in compound assignment.",
                    new Location(context.SemanticModel.SyntaxTree, diagnosticNode.Span)));

                var bad = new BoundBadExpression(syntaxForBoundNode);
                bad.SetType(targetType);
                return bad;
            }
            if (conv.Kind == ConversionKind.UserDefined)
            {
                return ApplyConversion(
                    exprSyntax: syntaxForBoundNode,
                    expr: expr,
                    targetType: targetType,
                    diagnosticNode: diagnosticNode,
                    context: context,
                    diagnostics: diagnostics,
                    requireImplicit: false);
            }
            bool isChecked = IsCheckedOverflowContext && conv.Kind == ConversionKind.ExplicitNumeric;
            return new BoundConversionExpression(syntaxForBoundNode, targetType, expr, conv, isChecked);
        }
        private BoundExpression BindAssignableValue(ExpressionSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            BoundExpression expr;

            if (node is DeclarationExpressionSyntax de)
            {
                expr = BindOutDeclarationExpressionAsLValue(de, context, diagnostics);
                expr = Record(de, expr, context);
            }
            else if (node is ThisExpressionSyntax @this)
            {
                expr = BindThisAsLValue(@this, context, diagnostics);
                expr = Record(@this, expr, context);
            }
            else if (node is MemberAccessExpressionSyntax ma)
            {
                expr = BindMemberAccess(ma, BindValueKind.LValue, context, diagnostics);
                expr = Record(ma, expr, context);
            }
            else if (node is ElementAccessExpressionSyntax ea)
            {
                expr = BindElementAccess(ea, BindValueKind.LValue, context, diagnostics);
                expr = Record(ea, expr, context);
            }
            else if (node is IdentifierNameSyntax id)
            {
                var name = id.Identifier.ValueText ?? "";
                if (TryGetLocalFromEnclosingScopes(name, out var local))
                {
                    expr = new BoundLocalExpression(id, local!);
                    expr = Record(id, expr, context);
                }
                else if (TryGetParameterFromEnclosingScopes(name, out var param))
                {
                    expr = new BoundParameterExpression(id, param!);
                    expr = Record(id, expr, context);
                }
                else if (TryBindUnqualifiedMember(id, name, BindValueKind.LValue, context, diagnostics, out var memberExpr))
                {
                    expr = Record(id, memberExpr, context);
                }
                else if (TryBindImportedStaticMember(id, name, BindValueKind.LValue, context, diagnostics, out var staticMemberExpr))
                {
                    expr = Record(id, staticMemberExpr, context);
                }
                else
                {
                    diagnostics.Add(new Diagnostic("CN_BIND003", DiagnosticSeverity.Error,
                        $"Use of undeclared identifier '{name}' during assignment.",
                        new Location(context.SemanticModel.SyntaxTree, id.Span)));

                    expr = new BoundBadExpression(id);
                    expr = Record(id, expr, context);
                }
            }
            else
            {
                expr = BindExpression(node, context, diagnostics);
            }

            if (expr.HasErrors)
                return expr;

            if (expr is BoundOutVarPendingExpression)
                return expr;

            if (expr is BoundOutDiscardExpression)
                return expr;

            if (expr.IsLValue)
                return expr;


            diagnostics.Add(new Diagnostic("CN_ASG002", DiagnosticSeverity.Error,
                "The left-hand side of an assignment must be a variable, property or indexer.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));

            return new BoundBadExpression(node);
        }
        private BoundExpression BindThisAsLValue(
            ThisExpressionSyntax node,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_THIS000",
                    DiagnosticSeverity.Error,
                    "The 'this' keyword is only valid in instance members.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadExpression(node);
            }

            if (!method.IsConstructor || method.IsStatic)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_THIS001",
                    DiagnosticSeverity.Error,
                    "Cannot assign to 'this' outside an instance constructor.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadExpression(node);
            }

            if (method.ContainingSymbol is not NamedTypeSymbol containingType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot resolve containing type for 'this'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadExpression(node);
            }

            if (!containingType.IsValueType)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_ASG_THIS002",
                    DiagnosticSeverity.Error,
                    "Cannot assign to 'this' in a reference type constructor.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));

                return new BoundBadExpression(node);
            }

            return new BoundThisExpression(node, containingType, isLValue: true);
        }
        private BoundExpression BindOutDeclarationExpressionAsLValue(
            DeclarationExpressionSyntax de,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (de.Designation is DiscardDesignationSyntax)
            {
                if (IsVar(de.Type))
                {
                    return new BoundOutDiscardExpression(
                        de,
                        context.Compilation.GetSpecialType(SpecialType.System_Void),
                        explicitElementTypeOpt: null);
                }

                var explicitElementType = BindType(de.Type, context, diagnostics);
                var explicitByRefType = context.Compilation.CreateByRefType(explicitElementType);

                return new BoundOutDiscardExpression(
                    de,
                    explicitByRefType,
                    explicitElementTypeOpt: explicitElementType);
            }

            if (de.Designation is not SingleVariableDesignationSyntax sv)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL001",
                    DiagnosticSeverity.Error,
                    "Unsupported variable designation in out argument.",
                    new Location(context.SemanticModel.SyntaxTree, de.Span)));

                return new BoundBadExpression(de);
            }

            var name = sv.Identifier.ValueText ?? "";

            if (IsNameDeclaredInEnclosingScopes(name))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_OUTDECL002",
                    DiagnosticSeverity.Error,
                    $"A local named '{name}' is already declared in this scope.",
                    new Location(context.SemanticModel.SyntaxTree, sv.Span)));
            }

            var isVar = IsVar(de.Type);

            if (isVar)
            {
                return new BoundOutVarPendingExpression(
                    de,
                    name,
                    sv,
                    context.Compilation.GetSpecialType(SpecialType.System_Void));
            }

            TypeSymbol localType = BindType(de.Type, context, diagnostics);

            var local = new LocalSymbol(
                name: name,
                containing: _containing,
                type: localType,
                locations: ImmutableArray.Create(new Location(context.SemanticModel.SyntaxTree, sv.Span)),
                isConst: false,
                constantValueOpt: Optional<object>.None,
                isByRef: false);

            _locals[name] = local;
            context.Recorder.RecordDeclared(sv, local);

            var idSyntax = new IdentifierNameSyntax(sv.Identifier);
            return new BoundLocalExpression(idSyntax, local);
        }
        private static bool IsReadOnlyValueReceiver(BoundExpression? receiver, BindingContext context)
        {
            if (IsReadOnlyStructThisReceiver(receiver, context))
                return true;

            if (receiver is BoundParameterExpression p &&
                p.Parameter.IsReadOnlyRef &&
                p.Parameter.Type.IsValueType)
            {
                return true;
            }

            return false;
        }
        private static bool IsReadOnlyStructThisReceiver(BoundExpression? receiver, BindingContext context)
        {
            if (receiver is not BoundThisExpression)
                return false;

            if (context.ContainingSymbol is not MethodSymbol method)
                return false;

            // Constructors may assign fields
            if (method.IsConstructor || method.IsStatic)
                return false;

            return method.ContainingSymbol is NamedTypeSymbol nt && nt.IsReadOnlyStruct;
        }
        private static bool IsReadOnlyByRefParameterTarget(BoundExpression expr)
        {
            return expr is BoundParameterExpression p && p.Parameter.IsReadOnlyRef;
        }
        private static bool IsReadOnlyLocalTarget(BoundExpression expr)
            => expr is BoundLocalExpression l && l.Local.IsReadOnly;

        private static bool IsReadOnlyTarget(BoundExpression expr)
            => IsReadOnlyLocalTarget(expr) || IsReadOnlyByRefParameterTarget(expr);
        private static bool IsInsideIntrinsicMethod(BindingContext context)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
                return false;

            var attrs = method.GetAttributes();
            for (int i = 0; i < attrs.Length; i++)
            {
                var attrClass = attrs[i].AttributeClass;
                if (attrClass is not null &&
                    string.Equals(attrClass.Name, "IntrinsicAttribute", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        private static bool CanAssignReadOnlyFieldInConstructor(
            FieldSymbol field,
            BoundExpression? receiver,
            BindingContext context)
        {
            if (!field.IsReadOnly)
                return false;

            // Must be inside a constructor of the same containing type
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
                return false;

            if (!ReferenceEquals(method.ContainingSymbol, field.ContainingSymbol))
                return false;

            // Static/instance must match
            if (method.IsStatic != field.IsStatic)
                return false;

            if (!field.IsStatic)
                return receiver is null || receiver is BoundThisExpression;

            // Static readonly field write in static ctor
            return receiver is null;
        }
        private static bool CanAssignReadOnlyAutoPropertyInConstructor(
            PropertySymbol prop,
            BoundExpression? receiver,
            BindingContext context)
        {
            // Only source auto properties with synthesized backing field
            if (prop is not SourcePropertySymbol sp || sp.BackingFieldOpt is null)
                return false;

            // Property already has a setter
            if (prop.HasSet)
                return false;

            // Must be inside a constructor of the same containing type
            if (context.ContainingSymbol is not MethodSymbol method || !method.IsConstructor)
                return false;

            if (!ReferenceEquals(method.ContainingSymbol, prop.ContainingSymbol))
                return false;

            // Static/instance must match
            if (method.IsStatic != prop.IsStatic)
                return false;

            // For instance property only 'this' is allowed
            if (!prop.IsStatic)
            {
                return receiver is null || receiver is BoundThisExpression;
            }

            // Static property write in static ctor
            return receiver is null;
        }
    }
}
