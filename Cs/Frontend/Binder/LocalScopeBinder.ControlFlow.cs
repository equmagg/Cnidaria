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
        internal void ReportControlFlowDiagnostics(
            BindingContext context,
            DiagnosticBag diagnostics,
            BoundStatement? body = null,
            SyntaxNode? diagnosticNode = null)
        {
            _flow.ReportUndefinedLabels(context, diagnostics);

            if (body is not null)
                ReportMissingReturnIfNeeded(body, diagnosticNode ?? body.Syntax, context, diagnostics);
        }

        private sealed class Completion
        {
            public bool CanCompleteNormally { get; set; }
            public HashSet<LabelSymbol> GotoTargets { get; } = new();
            public HashSet<LabelSymbol> BreakTargets { get; } = new();
            public HashSet<LabelSymbol> ContinueTargets { get; } = new();

            public static Completion None() => new Completion();

            public static Completion Normal()
            {
                var result = new Completion();
                result.CanCompleteNormally = true;
                return result;
            }

            public void Add(Completion other)
            {
                CanCompleteNormally |= other.CanCompleteNormally;
                AddRange(GotoTargets, other.GotoTargets);
                AddRange(BreakTargets, other.BreakTargets);
                AddRange(ContinueTargets, other.ContinueTargets);
            }

            private static void AddRange(HashSet<LabelSymbol> target, HashSet<LabelSymbol> source)
            {
                foreach (var label in source)
                    target.Add(label);
            }
        }

        private static void ReportMissingReturnIfNeeded(
            BoundStatement body,
            SyntaxNode diagnosticNode,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (context.ContainingSymbol is not MethodSymbol method)
                return;

            if (!RequiresValueReturn(method))
                return;

            if (ContainsYieldStatement(body) && TryGetIteratorElementType(context.Compilation, method, out _))
                return;

            var completion = AnalyzeCompletion(body);
            if (!completion.CanCompleteNormally)
                return;

            diagnostics.Add(new Diagnostic(
                "CN_FLOW016",
                DiagnosticSeverity.Error,
                string.Equals(method.Name, "<lambda>", StringComparison.Ordinal)
                    ? "Not all code paths return a value in lambda expression."
                    : $"'{method.Name}': not all code paths return a value.",
                new Location(context.SemanticModel.SyntaxTree, GetMissingReturnDiagnosticSpan(diagnosticNode))));
        }

        private static bool RequiresValueReturn(MethodSymbol method)
        {
            if (method.IsConstructor)
                return false;

            var returnType = method.ReturnType;
            if (returnType.SpecialType == SpecialType.System_Void)
                return false;

            return returnType.Kind != SymbolKind.Error;
        }


        private static TextSpan GetMissingReturnDiagnosticSpan(SyntaxNode node)
        {
            switch (node)
            {
                case MethodDeclarationSyntax method:
                    return method.Identifier.Span;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Identifier.Span;
                case OperatorDeclarationSyntax op:
                    return op.OperatorToken.Span.Length != 0 ? op.OperatorToken.Span : op.OperatorKeyword.Span;
                case ConversionOperatorDeclarationSyntax conversion:
                    return conversion.Type.Span;
                case AccessorDeclarationSyntax accessor:
                    return accessor.Keyword.Span;
                case LambdaExpressionSyntax lambda:
                    return lambda.ArrowToken.Span.Length != 0 ? lambda.ArrowToken.Span : lambda.Span;
                default:
                    return node.Span;
            }
        }

        private static Completion AnalyzeCompletion(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundBadStatement:
                    return Completion.Normal();

                case BoundEmptyStatement:
                case BoundExpressionStatement:
                case BoundLocalDeclarationStatement:
                case BoundLabelStatement:
                case BoundLocalFunctionStatement:
                    return Completion.Normal();

                case BoundReturnStatement:
                case BoundThrowStatement:
                    return Completion.None();

                case BoundYieldStatement yield:
                    return yield.YieldKind == BoundYieldStatementKind.Break
                        ? Completion.None()
                        : Completion.Normal();

                case BoundBreakStatement br:
                    {
                        var result = Completion.None();
                        result.BreakTargets.Add(br.TargetLabel);
                        return result;
                    }

                case BoundContinueStatement cont:
                    {
                        var result = Completion.None();
                        result.ContinueTargets.Add(cont.TargetLabel);
                        return result;
                    }

                case BoundGotoStatement go:
                    {
                        var result = Completion.None();
                        result.GotoTargets.Add(go.TargetLabel);
                        return result;
                    }

                case BoundConditionalGotoStatement conditionalGoto:
                    return AnalyzeConditionalGotoCompletion(conditionalGoto);

                case BoundBlockStatement block:
                    return AnalyzeStatementSequence(FlattenStatementList(block.Statements));

                case BoundStatementList list:
                    return AnalyzeStatementSequence(FlattenStatementList(list.Statements));

                case BoundIfStatement ifStatement:
                    return AnalyzeIfCompletion(ifStatement);

                case BoundWhileStatement whileStatement:
                    return AnalyzeWhileCompletion(whileStatement);

                case BoundDoWhileStatement doWhileStatement:
                    return AnalyzeDoWhileCompletion(doWhileStatement);

                case BoundForStatement forStatement:
                    return AnalyzeForCompletion(forStatement);

                case BoundForEachStatement forEachStatement:
                    return AnalyzeForEachCompletion(forEachStatement);

                case BoundUsingStatement usingStatement:
                    return AnalyzeUsingCompletion(usingStatement);

                case BoundFixedStatement fixedStatement:
                    return AnalyzeCompletion(fixedStatement.Body);

                case BoundTryStatement tryStatement:
                    return AnalyzeTryCompletion(tryStatement);

                case BoundCheckedStatement checkedStatement:
                    return AnalyzeCompletion(checkedStatement.Statement);

                case BoundUncheckedStatement uncheckedStatement:
                    return AnalyzeCompletion(uncheckedStatement.Statement);

                default:
                    return Completion.Normal();
            }
        }

        private static ImmutableArray<BoundStatement> FlattenStatementList(ImmutableArray<BoundStatement> statements)
        {
            var builder = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
            FlattenStatementList(statements, builder);
            return builder.ToImmutable();
        }

        private static void FlattenStatementList(
            ImmutableArray<BoundStatement> statements,
            ImmutableArray<BoundStatement>.Builder builder)
        {
            for (int i = 0; i < statements.Length; i++)
            {
                if (statements[i] is BoundStatementList list)
                    FlattenStatementList(list.Statements, builder);
                else
                    builder.Add(statements[i]);
            }
        }

        private static Completion AnalyzeStatementSequence(ImmutableArray<BoundStatement> statements)
        {
            var result = Completion.None();

            var labelToIndex = new Dictionary<LabelSymbol, int>();
            for (int i = 0; i < statements.Length; i++)
            {
                if (statements[i] is BoundLabelStatement labelStatement && !labelToIndex.ContainsKey(labelStatement.Label))
                    labelToIndex.Add(labelStatement.Label, i);
            }

            var reachable = new bool[statements.Length + 1];
            var work = new Queue<int>();
            Enqueue(0);

            while (work.Count != 0)
            {
                var index = work.Dequeue();
                if (index == statements.Length)
                {
                    result.CanCompleteNormally = true;
                    continue;
                }

                var statement = statements[index];

                if (statement is BoundLabelStatement)
                {
                    Enqueue(index + 1);
                    continue;
                }

                if (statement is BoundGotoStatement gotoStatement)
                {
                    RouteGoto(gotoStatement.TargetLabel);
                    continue;
                }

                if (statement is BoundConditionalGotoStatement conditionalGoto)
                {
                    RouteConditionalGoto(conditionalGoto, index + 1);
                    continue;
                }

                var completion = AnalyzeCompletion(statement);
                if (completion.CanCompleteNormally)
                    Enqueue(index + 1);

                RouteGotos(completion.GotoTargets);
                AddRange(result.BreakTargets, completion.BreakTargets);
                AddRange(result.ContinueTargets, completion.ContinueTargets);
            }

            return result;

            void Enqueue(int index)
            {
                if ((uint)index > (uint)statements.Length)
                    return;

                if (reachable[index])
                    return;

                reachable[index] = true;
                work.Enqueue(index);
            }

            void RouteGoto(LabelSymbol label)
            {
                if (labelToIndex.TryGetValue(label, out var targetIndex))
                    Enqueue(targetIndex);
                else
                    result.GotoTargets.Add(label);
            }

            void RouteGotos(HashSet<LabelSymbol> labels)
            {
                foreach (var label in labels)
                    RouteGoto(label);
            }

            void RouteConditionalGoto(BoundConditionalGotoStatement conditionalGoto, int fallThroughIndex)
            {
                if (TryGetBoolConstant(conditionalGoto.Condition, out var constant))
                {
                    if (constant == conditionalGoto.JumpIfTrue)
                        RouteGoto(conditionalGoto.TargetLabel);
                    else
                        Enqueue(fallThroughIndex);

                    return;
                }

                RouteGoto(conditionalGoto.TargetLabel);
                Enqueue(fallThroughIndex);
            }

            static void AddRange(HashSet<LabelSymbol> target, HashSet<LabelSymbol> source)
            {
                foreach (var label in source)
                    target.Add(label);
            }
        }

        private static Completion AnalyzeConditionalGotoCompletion(BoundConditionalGotoStatement statement)
        {
            var result = Completion.None();
            if (TryGetBoolConstant(statement.Condition, out var constant))
            {
                if (constant == statement.JumpIfTrue)
                    result.GotoTargets.Add(statement.TargetLabel);
                else
                    result.CanCompleteNormally = true;

                return result;
            }

            result.CanCompleteNormally = true;
            result.GotoTargets.Add(statement.TargetLabel);
            return result;
        }

        private static Completion AnalyzeIfCompletion(BoundIfStatement statement)
        {
            if (TryGetBoolConstant(statement.Condition, out var constant))
                return constant
                    ? AnalyzeCompletion(statement.Then)
                    : statement.ElseOpt is null ? Completion.Normal() : AnalyzeCompletion(statement.ElseOpt);

            var result = AnalyzeCompletion(statement.Then);
            if (statement.ElseOpt is null)
                result.CanCompleteNormally = true;
            else
                result.Add(AnalyzeCompletion(statement.ElseOpt));

            return result;
        }

        private static Completion AnalyzeWhileCompletion(BoundWhileStatement statement)
        {
            if (TryGetBoolConstant(statement.Condition, out var constant) && !constant)
                return Completion.Normal();

            var body = AnalyzeCompletion(statement.Body);
            var breaksLoop = body.BreakTargets.Remove(statement.BreakLabel);
            body.ContinueTargets.Remove(statement.ContinueLabel);

            var result = Completion.None();
            result.CanCompleteNormally = breaksLoop || !IsConstantTrue(statement.Condition);
            result.GotoTargets.UnionWith(body.GotoTargets);
            result.BreakTargets.UnionWith(body.BreakTargets);
            result.ContinueTargets.UnionWith(body.ContinueTargets);
            return result;
        }

        private static Completion AnalyzeDoWhileCompletion(BoundDoWhileStatement statement)
        {
            var body = AnalyzeCompletion(statement.Body);
            var breaksLoop = body.BreakTargets.Remove(statement.BreakLabel);
            body.ContinueTargets.Remove(statement.ContinueLabel);

            var result = Completion.None();
            result.CanCompleteNormally = breaksLoop || (body.CanCompleteNormally && !IsConstantTrue(statement.Condition));
            result.GotoTargets.UnionWith(body.GotoTargets);
            result.BreakTargets.UnionWith(body.BreakTargets);
            result.ContinueTargets.UnionWith(body.ContinueTargets);
            return result;
        }

        private static Completion AnalyzeForCompletion(BoundForStatement statement)
        {
            if (statement.ConditionOpt is not null && TryGetBoolConstant(statement.ConditionOpt, out var constant) && !constant)
                return Completion.Normal();

            var body = AnalyzeCompletion(statement.Body);
            var breaksLoop = body.BreakTargets.Remove(statement.BreakLabel);
            body.ContinueTargets.Remove(statement.ContinueLabel);

            var result = Completion.None();
            result.CanCompleteNormally = breaksLoop || !IsConstantTrueOrMissing(statement.ConditionOpt);
            result.GotoTargets.UnionWith(body.GotoTargets);
            result.BreakTargets.UnionWith(body.BreakTargets);
            result.ContinueTargets.UnionWith(body.ContinueTargets);
            return result;
        }

        private static Completion AnalyzeForEachCompletion(BoundForEachStatement statement)
        {
            var body = AnalyzeCompletion(statement.Body);
            body.BreakTargets.Remove(statement.BreakLabel);
            body.ContinueTargets.Remove(statement.ContinueLabel);

            var result = Completion.Normal();
            result.GotoTargets.UnionWith(body.GotoTargets);
            result.BreakTargets.UnionWith(body.BreakTargets);
            result.ContinueTargets.UnionWith(body.ContinueTargets);
            return result;
        }

        private static Completion AnalyzeUsingCompletion(BoundUsingStatement statement)
        {
            var body = AnalyzeCompletion(statement.Body);
            var result = Completion.None();
            result.CanCompleteNormally = body.CanCompleteNormally;
            result.GotoTargets.UnionWith(body.GotoTargets);
            result.BreakTargets.UnionWith(body.BreakTargets);
            result.ContinueTargets.UnionWith(body.ContinueTargets);
            return result;
        }

        private static Completion AnalyzeTryCompletion(BoundTryStatement statement)
        {
            var protectedCompletion = AnalyzeCompletion(statement.TryBlock);
            for (int i = 0; i < statement.CatchBlocks.Length; i++)
            {
                var catchBlock = statement.CatchBlocks[i];
                if (catchBlock.FilterOpt is not null &&
                    TryGetBoolConstant(catchBlock.FilterOpt, out var filterValue) &&
                    !filterValue)
                {
                    continue;
                }

                protectedCompletion.Add(AnalyzeCompletion(catchBlock.Body));
            }

            if (statement.FinallyBlockOpt is null)
                return protectedCompletion;

            var finallyCompletion = AnalyzeCompletion(statement.FinallyBlockOpt);
            if (!finallyCompletion.CanCompleteNormally)
                return finallyCompletion;

            protectedCompletion.GotoTargets.UnionWith(finallyCompletion.GotoTargets);
            protectedCompletion.BreakTargets.UnionWith(finallyCompletion.BreakTargets);
            protectedCompletion.ContinueTargets.UnionWith(finallyCompletion.ContinueTargets);
            return protectedCompletion;
        }

        private static bool IsConstantTrue(BoundExpression expression)
            => TryGetBoolConstant(expression, out var value) && value;

        private static bool IsConstantTrueOrMissing(BoundExpression? expression)
            => expression is null || IsConstantTrue(expression);

        private static bool ContainsYieldStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundYieldStatement:
                    return true;

                case BoundBlockStatement block:
                    return ContainsYieldStatement(block.Statements);

                case BoundStatementList list:
                    return ContainsYieldStatement(list.Statements);

                case BoundIfStatement ifStatement:
                    return ContainsYieldStatement(ifStatement.Then) ||
                           (ifStatement.ElseOpt is not null && ContainsYieldStatement(ifStatement.ElseOpt));

                case BoundWhileStatement whileStatement:
                    return ContainsYieldStatement(whileStatement.Body);

                case BoundDoWhileStatement doWhileStatement:
                    return ContainsYieldStatement(doWhileStatement.Body);

                case BoundForStatement forStatement:
                    return ContainsYieldStatement(forStatement.Body);

                case BoundForEachStatement forEachStatement:
                    return ContainsYieldStatement(forEachStatement.Body);

                case BoundUsingStatement usingStatement:
                    return ContainsYieldStatement(usingStatement.Body);

                case BoundFixedStatement fixedStatement:
                    return ContainsYieldStatement(fixedStatement.Body);

                case BoundTryStatement tryStatement:
                    if (ContainsYieldStatement(tryStatement.TryBlock))
                        return true;

                    for (int i = 0; i < tryStatement.CatchBlocks.Length; i++)
                        if (ContainsYieldStatement(tryStatement.CatchBlocks[i].Body))
                            return true;

                    return tryStatement.FinallyBlockOpt is not null && ContainsYieldStatement(tryStatement.FinallyBlockOpt);

                case BoundCheckedStatement checkedStatement:
                    return ContainsYieldStatement(checkedStatement.Statement);

                case BoundUncheckedStatement uncheckedStatement:
                    return ContainsYieldStatement(uncheckedStatement.Statement);

                default:
                    return false;
            }
        }

        private static bool ContainsYieldStatement(ImmutableArray<BoundStatement> statements)
        {
            for (int i = 0; i < statements.Length; i++)
                if (ContainsYieldStatement(statements[i]))
                    return true;

            return false;
        }

        private static IdentifierNameSyntax MakeIdentifierName(string name, TextSpan span)
            => new IdentifierNameSyntax(
                new SyntaxToken(SyntaxKind.IdentifierToken, span, valueText: name, value: name, leadingTrivia: s_noTrivia, trailingTrivia: s_noTrivia));
        private static SyntaxToken MakeToken(SyntaxKind kind, TextSpan span)
            => new SyntaxToken(kind, span, valueText: null, value: null, leadingTrivia: s_noTrivia, trailingTrivia: s_noTrivia);
        private BoundExpression MakeBoolLiteral(SyntaxNode syntax, BindingContext ctx, bool value)
            => new BoundLiteralExpression(syntax, ctx.Compilation.GetSpecialType(SpecialType.System_Boolean), value);
    }
}
