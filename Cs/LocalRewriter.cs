using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal sealed class StackGuard
    {
        private const int MaxUncheckedRecursionDepth = 128;
        private int _counter = MaxUncheckedRecursionDepth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureSufficientExecutionStack()
        {
            if (--_counter > 0)
                return;

            _counter = MaxUncheckedRecursionDepth;
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        public T RunOnEmptyStack<T>(Func<T> action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));

            T? result = default;
            Exception? captured = null;

            var thread = new Thread(() =>
            {
                try { result = action(); }
                catch (Exception ex) { captured = ex; }
            }, 1 << 20);

            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            if (captured is not null)
                throw captured;

            return result!;
        }
    }

    internal abstract class BoundTreeRewriterWithStackGuard : BoundTreeRewriter
    {
        private readonly StackGuard _stackGuard = new StackGuard();

        protected sealed override BoundStatement RewriteStatement(BoundStatement node)
        {
            try
            {
                _stackGuard.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                return _stackGuard.RunOnEmptyStack(() => base.RewriteStatement(node));
            }

            return base.RewriteStatement(node);
        }

        protected sealed override BoundExpression RewriteExpression(BoundExpression node)
        {
            try
            {
                _stackGuard.EnsureSufficientExecutionStack();
            }
            catch (InsufficientExecutionStackException)
            {
                return _stackGuard.RunOnEmptyStack(() => base.RewriteExpression(node));
            }

            return base.RewriteExpression(node);
        }
    }

    internal abstract class BoundTreeRewriter
    {
        public virtual BoundNode RewriteNode(BoundNode node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));

            return node switch
            {
                BoundStatement s => RewriteStatement(s),
                BoundExpression e => RewriteExpression(e),
                BoundCompilationUnit u => RewriteCompilationUnit(u),
                BoundMethodBody b => RewriteMethodBody(b),
                _ => node
            };
        }
        protected virtual BoundMethodBody RewriteMethodBody(BoundMethodBody node)
        {
            var body = RewriteStatement(node.Body);
            if (!ReferenceEquals(body, node.Body))
                return new BoundMethodBody(node.Syntax, node.Method, body);

            return node;
        }
        protected virtual BoundCompilationUnit RewriteCompilationUnit(BoundCompilationUnit node)
        {
            var statements = RewriteStatements(node.Statements, out var changed);
            var top = node.TopLevelMethodBodyOpt is null ? null : RewriteMethodBody(node.TopLevelMethodBodyOpt);

            if (changed || !ReferenceEquals(top, node.TopLevelMethodBodyOpt))
                return new BoundCompilationUnit((CompilationUnitSyntax)node.Syntax, statements, top);

            return node;
        }
        protected virtual BoundStatement RewriteStatement(BoundStatement node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));

            return node switch
            {
                BoundBadStatement s => s,
                BoundStatementList s => RewriteStatementList(s),
                BoundBlockStatement s => RewriteBlockStatement(s),
                BoundExpressionStatement s => RewriteExpressionStatement(s),
                BoundLocalDeclarationStatement s => RewriteLocalDeclarationStatement(s),
                BoundEmptyStatement s => s,
                BoundTryStatement s => RewriteTryStatement(s),
                BoundThrowStatement s => RewriteThrowStatement(s),
                BoundReturnStatement s => RewriteReturnStatement(s),
                BoundIfStatement s => RewriteIfStatement(s),
                BoundWhileStatement s => RewriteWhileStatement(s),
                BoundDoWhileStatement s => RewriteDoWhileStatement(s),
                BoundForStatement s => RewriteForStatement(s),
                BoundLabelStatement s => s,
                BoundGotoStatement s => s,
                BoundConditionalGotoStatement s => RewriteConditionalGotoStatement(s),
                BoundBreakStatement s => RewriteBreakStatement(s),
                BoundContinueStatement s => RewriteContinueStatement(s),
                BoundCheckedStatement s => RewriteCheckedStatement(s),
                BoundUncheckedStatement s => RewriteUncheckedStatement(s),
                BoundLocalFunctionStatement s => RewriteLocalFunctionStatement(s),
                _ => throw new NotSupportedException($"Unexpected bound statement: {node.GetType().Name}")
            };
        }

        protected virtual BoundExpression RewriteExpression(BoundExpression node)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));

            return node switch
            {
                BoundBadExpression e => e,
                BoundLiteralExpression e => e,
                BoundLocalExpression e => e,
                BoundParameterExpression e => e,
                BoundThisExpression e => e,
                BoundLabelExpression e => e,
                BoundSizeOfExpression e => e,

                BoundTupleExpression e => RewriteTupleExpression(e),
                BoundArrayInitializerExpression e => RewriteArrayInitializerExpression(e),
                BoundArrayCreationExpression e => RewriteArrayCreationExpression(e),
                BoundArrayElementAccessExpression e => RewriteArrayElementAccessExpression(e),
                BoundStackAllocArrayCreationExpression e => RewriteStackAllocArrayCreationExpression(e),

                BoundRefExpression e => RewriteRefExpression(e),
                BoundAddressOfExpression e => RewriteAddressOfExpression(e),
                BoundPointerIndirectionExpression e => RewritePointerIndirectionExpression(e),
                BoundPointerElementAccessExpression e => RewritePointerElementAccessExpression(e),

                BoundConversionExpression e => RewriteConversionExpression(e),
                BoundAsExpression e => RewriteAsExpression(e),
                BoundUnaryExpression e => RewriteUnaryExpression(e),
                BoundBinaryExpression e => RewriteBinaryExpression(e),
                BoundConditionalExpression e => RewriteConditionalExpression(e),
                BoundAssignmentExpression e => RewriteAssignmentExpression(e),
                BoundCompoundAssignmentExpression e => RewriteCompoundAssignmentExpression(e),
                BoundNullCoalescingAssignmentExpression e => RewriteNullCoalescingAssignmentExpression(e),
                BoundIncrementDecrementExpression e => RewriteIncrementDecrementExpression(e),
                BoundCallExpression e => RewriteCallExpression(e),
                BoundObjectCreationExpression e => RewriteObjectCreationExpression(e),
                BoundUnboundImplicitObjectCreationExpression e => RewriteUnboundImplicitObjectCreationExpression(e),
                BoundSequenceExpression e => RewriteSequenceExpression(e),
                BoundIndexerAccessExpression e => RewriteIndexerAccessExpression(e),
                BoundMemberAccessExpression e => RewriteMemberAccessExpression(e),
                BoundCheckedExpression e => RewriteCheckedExpression(e),
                BoundUncheckedExpression e => RewriteUncheckedExpression(e),

                _ => throw new NotSupportedException($"Unexpected bound expression: {node.GetType().Name}")
            };
        }

        protected virtual BoundStatement RewriteStatementList(BoundStatementList node)
        {
            var statements = RewriteStatements(node.Statements, out var changed);
            if (changed)
                return new BoundStatementList(node.Syntax, statements);

            return node;
        }

        protected virtual BoundStatement RewriteBlockStatement(BoundBlockStatement node)
        {
            var statements = RewriteStatements(node.Statements, out var changed);
            if (changed)
                return new BoundBlockStatement(node.Syntax, statements);

            return node;
        }
        protected virtual BoundStatement RewriteExpressionStatement(BoundExpressionStatement node)
        {
            var expr = RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
                return new BoundExpressionStatement(node.Syntax, expr);

            return node;
        }
        protected virtual BoundStatement RewriteTryStatement(BoundTryStatement node)
        {
            var tryBlock = (BoundBlockStatement)RewriteStatement(node.TryBlock);

            bool catchesChanged = false;
            var catches = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.CatchBlocks.Length);
            for (int i = 0; i < node.CatchBlocks.Length; i++)
            {
                var c = RewriteCatchBlock(node.CatchBlocks[i]);
                if (!ReferenceEquals(c, node.CatchBlocks[i]))
                    catchesChanged = true;
                catches.Add(c);
            }

            var finallyBlock = node.FinallyBlockOpt is null
                ? null
                : (BoundBlockStatement)RewriteStatement(node.FinallyBlockOpt);

            if (!ReferenceEquals(tryBlock, node.TryBlock) ||
                catchesChanged ||
                !ReferenceEquals(finallyBlock, node.FinallyBlockOpt))
            {
                return new BoundTryStatement(
                    (TryStatementSyntax)node.Syntax,
                    tryBlock,
                    catches.ToImmutable(),
                    finallyBlock);
            }

            return node;
        }

        protected virtual BoundCatchBlock RewriteCatchBlock(BoundCatchBlock node)
        {
            var filter = node.FilterOpt is null ? null : RewriteExpression(node.FilterOpt);
            var body = (BoundBlockStatement)RewriteStatement(node.Body);

            if (!ReferenceEquals(filter, node.FilterOpt) || !ReferenceEquals(body, node.Body))
            {
                return new BoundCatchBlock(
                    (CatchClauseSyntax)node.Syntax,
                    node.ExceptionType,
                    node.ExceptionLocalOpt,
                    filter,
                    body);
            }

            return node;
        }
        protected virtual BoundExpression RewriteIndexerAccessExpression(BoundIndexerAccessExpression node)
        {
            var receiver = RewriteExpression(node.Receiver);
            var args = RewriteExpressions(node.Arguments, out var argsChanged);

            if (!ReferenceEquals(receiver, node.Receiver) || argsChanged)
                return new BoundIndexerAccessExpression(
                    (ExpressionSyntax)node.Syntax, receiver, node.Indexer, args, node.IsLValue, node.HasErrors);

            return node;
        }
        protected virtual BoundExpression RewriteMemberAccessExpression(BoundMemberAccessExpression node)
        {
            var recv = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
            if (!ReferenceEquals(recv, node.ReceiverOpt))
                return new BoundMemberAccessExpression(
                    (ExpressionSyntax)node.Syntax, recv, node.Member, node.Type, node.IsLValue, node.ConstantValueOpt, node.HasErrors);
            return node;
        }
        protected virtual BoundStatement RewriteCheckedStatement(BoundCheckedStatement node)
        {
            var statement = RewriteStatement(node.Statement);
            if (!ReferenceEquals(statement, node.Statement))
                return new BoundCheckedStatement((CheckedStatementSyntax)node.Syntax, statement);
            return node;
        }

        protected virtual BoundStatement RewriteUncheckedStatement(BoundUncheckedStatement node)
        {
            var statement = RewriteStatement(node.Statement);
            if (!ReferenceEquals(statement, node.Statement))
                return new BoundUncheckedStatement((CheckedStatementSyntax)node.Syntax, statement);
            return node;
        }
        protected virtual BoundExpression RewriteCheckedExpression(BoundCheckedExpression node)
        {
            var expr = RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
                return new BoundCheckedExpression((CheckedExpressionSyntax)node.Syntax, expr);
            return node;
        }

        protected virtual BoundExpression RewriteUncheckedExpression(BoundUncheckedExpression node)
        {
            var expr = RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
                return new BoundUncheckedExpression((CheckedExpressionSyntax)node.Syntax, expr);
            return node;
        }
        protected virtual BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
        {
            var body = RewriteStatement(node.Body);
            if (!ReferenceEquals(body, node.Body))
                return new BoundLocalFunctionStatement((LocalFunctionStatementSyntax)node.Syntax, node.LocalFunction, body);
            return node;
        }
        protected virtual BoundStatement RewriteLocalDeclarationStatement(BoundLocalDeclarationStatement node)
        {
            var init = node.Initializer is null ? null : RewriteExpression(node.Initializer);
            if (!ReferenceEquals(init, node.Initializer))
                return new BoundLocalDeclarationStatement(node.Syntax, node.Local, init);

            return node;
        }

        protected virtual BoundStatement RewriteReturnStatement(BoundReturnStatement node)
        {
            var expr = node.Expression is null ? null : RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
                return new BoundReturnStatement(node.Syntax, expr);

            return node;
        }
        protected virtual BoundStatement RewriteThrowStatement(BoundThrowStatement node)
        {
            var expr = node.ExpressionOpt is null ? null : RewriteExpression(node.ExpressionOpt);
            if (!ReferenceEquals(expr, node.ExpressionOpt))
                return new BoundThrowStatement(node.Syntax, expr);
            return node;
        }
        protected virtual BoundStatement RewriteIfStatement(BoundIfStatement node)
        {
            var condition = RewriteExpression(node.Condition);
            var thenStmt = RewriteStatement(node.Then);
            var elseStmt = node.ElseOpt is null ? null : RewriteStatement(node.ElseOpt);

            if (!ReferenceEquals(condition, node.Condition) ||
                !ReferenceEquals(thenStmt, node.Then) ||
                !ReferenceEquals(elseStmt, node.ElseOpt))
            {
                return new BoundIfStatement((IfStatementSyntax)node.Syntax, condition, thenStmt, elseStmt);
            }

            return node;
        }
        protected virtual BoundStatement RewriteWhileStatement(BoundWhileStatement node)
        {
            var condition = RewriteExpression(node.Condition);
            var body = RewriteStatement(node.Body);

            if (!ReferenceEquals(condition, node.Condition) || !ReferenceEquals(body, node.Body))
                return new BoundWhileStatement((WhileStatementSyntax)node.Syntax, condition, body, node.BreakLabel, node.ContinueLabel);

            return node;
        }

        protected virtual BoundStatement RewriteDoWhileStatement(BoundDoWhileStatement node)
        {
            var condition = RewriteExpression(node.Condition);
            var body = RewriteStatement(node.Body);

            if (!ReferenceEquals(condition, node.Condition) || !ReferenceEquals(body, node.Body))
                return new BoundDoWhileStatement((DoStatementSyntax)node.Syntax, body, condition, node.BreakLabel, node.ContinueLabel);

            return node;
        }

        protected virtual BoundStatement RewriteForStatement(BoundForStatement node)
        {
            var inits = RewriteStatements(node.Initializers, out var initsChanged);
            var incs = RewriteStatements(node.Incrementors, out var incsChanged);
            var cond = node.ConditionOpt is null ? null : RewriteExpression(node.ConditionOpt);
            var body = RewriteStatement(node.Body);

            if (initsChanged || incsChanged || !ReferenceEquals(cond, node.ConditionOpt) || !ReferenceEquals(body, node.Body))
            {
                return new BoundForStatement(
                    (ForStatementSyntax)node.Syntax,
                    inits,
                    cond,
                    incs,
                    body,
                    node.BreakLabel,
                    node.ContinueLabel);
            }

            return node;
        }
        protected virtual BoundStatement RewriteConditionalGotoStatement(BoundConditionalGotoStatement node)
        {
            var condition = RewriteExpression(node.Condition);
            if (!ReferenceEquals(condition, node.Condition))
                return new BoundConditionalGotoStatement(node.Syntax, condition, node.TargetLabel, node.JumpIfTrue);

            return node;
        }

        protected virtual BoundStatement RewriteBreakStatement(BoundBreakStatement node) => node;
        protected virtual BoundStatement RewriteContinueStatement(BoundContinueStatement node) => node;

        protected virtual BoundExpression RewriteTupleExpression(BoundTupleExpression node)
        {
            var elements = RewriteExpressions(node.Elements, out var changed);
            if (changed)
                return new BoundTupleExpression((TupleExpressionSyntax)node.Syntax, (TupleTypeSymbol)node.Type, elements, node.ElementNames, node.HasErrors);

            return node;
        }

        protected virtual BoundExpression RewriteArrayInitializerExpression(BoundArrayInitializerExpression node)
        {
            var elems = RewriteExpressions(node.Elements, out var changed);
            if (changed)
                return new BoundArrayInitializerExpression((InitializerExpressionSyntax)node.Syntax, node.Type, elems);

            return node;
        }
        protected virtual BoundExpression RewriteArrayCreationExpression(BoundArrayCreationExpression node)
        {
            var dimensions = RewriteExpressions(node.DimensionSizes, out var dimsChanged);
            var init = node.InitializerOpt is null
                ? null
                : (BoundArrayInitializerExpression)RewriteExpression(node.InitializerOpt);

            if (dimsChanged || !ReferenceEquals(init, node.InitializerOpt))
            {
                return new BoundArrayCreationExpression(
                    node.Syntax,
                    (ArrayTypeSymbol)node.Type,
                    node.ElementType,
                    dimensions,
                    init);
            }

            return node;
        }
        protected virtual BoundExpression RewriteArrayElementAccessExpression(BoundArrayElementAccessExpression node)
        {
            var expr = RewriteExpression(node.Expression);
            var indices = RewriteExpressions(node.Indices, out var indicesChanged);
            if (!ReferenceEquals(expr, node.Expression) || indicesChanged)
                return new BoundArrayElementAccessExpression(node.Syntax, node.Type, expr, indices);
            return node;
        }
        protected virtual BoundExpression RewriteStackAllocArrayCreationExpression(BoundStackAllocArrayCreationExpression node)
        {
            var count = RewriteExpression(node.Count);
            var init = node.InitializerOpt is null ? null : (BoundArrayInitializerExpression)RewriteExpression(node.InitializerOpt);

            if (!ReferenceEquals(count, node.Count) || !ReferenceEquals(init, node.InitializerOpt))
            {
                return new BoundStackAllocArrayCreationExpression(
                    node.Syntax,
                    (PointerTypeSymbol)node.Type,
                    node.ElementType,
                    count,
                    init);
            }

            return node;
        }
        protected virtual BoundExpression RewriteRefExpression(BoundRefExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundRefExpression(node.Syntax, node.Type, operand);

            return node;
        }
        protected virtual BoundExpression RewriteAddressOfExpression(BoundAddressOfExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundAddressOfExpression((PrefixUnaryExpressionSyntax)node.Syntax, (PointerTypeSymbol)node.Type, operand);

            return node;
        }

        protected virtual BoundExpression RewritePointerIndirectionExpression(BoundPointerIndirectionExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundPointerIndirectionExpression(node.Syntax, node.Type, operand);

            return node;
        }

        protected virtual BoundExpression RewritePointerElementAccessExpression(BoundPointerElementAccessExpression node)
        {
            var expr = RewriteExpression(node.Expression);
            var idx = RewriteExpression(node.Index);

            if (!ReferenceEquals(expr, node.Expression) || !ReferenceEquals(idx, node.Index))
                return new BoundPointerElementAccessExpression(node.Syntax, node.Type, expr, idx);

            return node;
        }

        protected virtual BoundExpression RewriteConversionExpression(BoundConversionExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundConversionExpression(node.Syntax, node.Type, operand, node.Conversion, node.IsChecked);

            return node;
        }

        protected virtual BoundExpression RewriteAsExpression(BoundAsExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundAsExpression(node.Syntax, node.Type, operand, node.Conversion);

            return node;
        }

        protected virtual BoundExpression RewriteUnaryExpression(BoundUnaryExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            if (!ReferenceEquals(operand, node.Operand))
                return new BoundUnaryExpression(node.Syntax, node.OperatorKind, node.Type, operand, node.ConstantValueOpt, node.IsChecked);

            return node;
        }

        protected virtual BoundExpression RewriteBinaryExpression(BoundBinaryExpression node)
        {
            var left = RewriteExpression(node.Left);
            var right = RewriteExpression(node.Right);

            if (!ReferenceEquals(left, node.Left) || !ReferenceEquals(right, node.Right))
                return new BoundBinaryExpression(node.Syntax, node.OperatorKind, node.Type, left, right, node.ConstantValueOpt, node.IsChecked);

            return node;
        }

        protected virtual BoundExpression RewriteConditionalExpression(BoundConditionalExpression node)
        {
            var cond = RewriteExpression(node.Condition);
            var whenTrue = RewriteExpression(node.WhenTrue);
            var whenFalse = RewriteExpression(node.WhenFalse);

            if (!ReferenceEquals(cond, node.Condition) ||
                !ReferenceEquals(whenTrue, node.WhenTrue) ||
                !ReferenceEquals(whenFalse, node.WhenFalse))
            {
                return new BoundConditionalExpression((ConditionalExpressionSyntax)node.Syntax, node.Type, cond, whenTrue, whenFalse, node.ConstantValueOpt);
            }

            return node;
        }
        protected virtual BoundExpression RewriteAssignmentExpression(BoundAssignmentExpression node)
        {
            var left = RewriteExpression(node.Left);
            var right = RewriteExpression(node.Right);

            if (!ReferenceEquals(left, node.Left) || !ReferenceEquals(right, node.Right))
                return new BoundAssignmentExpression(node.Syntax, left, right);

            return node;
        }
        protected virtual BoundExpression RewriteCompoundAssignmentExpression(BoundCompoundAssignmentExpression node)
        {
            var left = RewriteExpression(node.Left);
            var value = RewriteExpression(node.Value);

            if (!ReferenceEquals(left, node.Left) || !ReferenceEquals(value, node.Value))
                return new BoundCompoundAssignmentExpression(node.Syntax, left, node.OperatorKind, value, node.IsChecked);

            return node;
        }
        protected virtual BoundExpression RewriteNullCoalescingAssignmentExpression(BoundNullCoalescingAssignmentExpression node)
        {
            var left = RewriteExpression(node.Left);
            var value = RewriteExpression(node.Value);

            if (!ReferenceEquals(left, node.Left) || !ReferenceEquals(value, node.Value))
                return new BoundNullCoalescingAssignmentExpression(node.Syntax, left, value);

            return node;
        }
        protected virtual BoundExpression RewriteIncrementDecrementExpression(BoundIncrementDecrementExpression node)
        {
            var target = RewriteExpression(node.Target);
            var read = RewriteExpression(node.Read);
            var value = RewriteExpression(node.Value);

            if (!ReferenceEquals(target, node.Target) ||
                !ReferenceEquals(read, node.Read) ||
                !ReferenceEquals(value, node.Value))
            {
                return new BoundIncrementDecrementExpression(
                    node.Syntax,
                    target,
                    read,
                    value,
                    node.IsIncrement,
                    node.IsPostfix,
                    node.IsChecked);
            }

            return node;
        }
        protected virtual BoundExpression RewriteCallExpression(BoundCallExpression node)
        {
            var receiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
            var args = RewriteExpressions(node.Arguments, out var argsChanged);

            if (!ReferenceEquals(receiver, node.ReceiverOpt) || argsChanged)
                return new BoundCallExpression(node.Syntax, receiver, node.Method, args);

            return node;
        }
        protected virtual BoundExpression RewriteObjectCreationExpression(BoundObjectCreationExpression node)
        {
            var args = RewriteExpressions(node.Arguments, out var argsChanged);
            if (argsChanged)
                return new BoundObjectCreationExpression(
                     (ObjectCreationExpressionSyntax)node.Syntax,
                     (NamedTypeSymbol)node.Type,
                     node.ConstructorOpt,
                     args,
                     hasErrors: node.HasErrors);
            return node;
        }
        protected virtual BoundExpression RewriteUnboundImplicitObjectCreationExpression(BoundUnboundImplicitObjectCreationExpression node)
        {
            var args = RewriteExpressions(node.Arguments, out var changed);
            if (changed)
                return new BoundUnboundImplicitObjectCreationExpression((ImplicitObjectCreationExpressionSyntax)node.Syntax, args);
            return node;
        }
        protected virtual BoundExpression RewriteSequenceExpression(BoundSequenceExpression node)
        {
            var sideEffects = RewriteStatements(node.SideEffects, out var changed);
            var value = RewriteExpression(node.Value);

            if (changed || !ReferenceEquals(value, node.Value))
                return new BoundSequenceExpression(node.Syntax, node.Locals, sideEffects, value);

            return node;
        }

        protected ImmutableArray<BoundStatement> RewriteStatements(ImmutableArray<BoundStatement> statements, out bool changed)
        {
            changed = false;
            if (statements.IsDefaultOrEmpty)
                return statements;

            var builder = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
            for (int i = 0; i < statements.Length; i++)
            {
                var s = statements[i];
                var r = RewriteStatement(s);
                if (!ReferenceEquals(r, s))
                    changed = true;
                builder.Add(r);
            }
            return changed ? builder.ToImmutable() : statements;
        }

        protected ImmutableArray<BoundExpression> RewriteExpressions(ImmutableArray<BoundExpression> expressions, out bool changed)
        {
            changed = false;
            if (expressions.IsDefaultOrEmpty)
                return expressions;

            var builder = ImmutableArray.CreateBuilder<BoundExpression>(expressions.Length);
            for (int i = 0; i < expressions.Length; i++)
            {
                var e = expressions[i];
                var r = RewriteExpression(e);
                if (!ReferenceEquals(r, e))
                    changed = true;
                builder.Add(r);
            }
            return changed ? builder.ToImmutable() : expressions;
        }

    }


    internal static class IRLowering
    {
        public static BoundMethodBody Rewrite(Compilation compilation, BoundMethodBody methodBody)
        {
            if (compilation is null) throw new ArgumentNullException(nameof(compilation));
            if (methodBody is null) throw new ArgumentNullException(nameof(methodBody));

            var rewriter = new LocalRewriter(compilation, methodBody.Method);
            var loweredBody = rewriter.RewriteNode(methodBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected rewrite result.");

            loweredBody = EnsureBlockBody(loweredBody);

            var cleaner = new CleanupRewriter();
            loweredBody = cleaner.RewriteNode(loweredBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected cleanup result.");

            loweredBody = EnsureBlockBody(loweredBody);
            return loweredBody;
        }

        private static BoundMethodBody EnsureBlockBody(BoundMethodBody body)
        {
            if (body.Body is BoundBlockStatement)
                return body;

            if (body.Body is BoundStatementList list)
            {
                return new BoundMethodBody(
                    body.Syntax,
                    body.Method,
                    new BoundBlockStatement(list.Syntax, list.Statements));
            }

            return new BoundMethodBody(
                body.Syntax,
                body.Method,
                new BoundBlockStatement(body.Body.Syntax, ImmutableArray.Create<BoundStatement>(body.Body)));
        }

        private sealed class LocalRewriter : BoundTreeRewriterWithStackGuard
        {
            private readonly Compilation _compilation;
            private readonly MethodSymbol _method;
            private int _labelId;
            private int _tempId;
            private bool? _checkedContextOverride;
            private IDisposable PushCheckedContext(bool value)
            {
                var previous = _checkedContextOverride;
                _checkedContextOverride = value;
                return new CheckedContextScope(this, previous);
            }

            private bool GetEffectiveIsChecked(bool nodeValue) => _checkedContextOverride ?? nodeValue;

            private sealed class CheckedContextScope : IDisposable
            {
                private readonly LocalRewriter _owner;
                private readonly bool? _previous;

                public CheckedContextScope(LocalRewriter owner, bool? previous)
                {
                    _owner = owner;
                    _previous = previous;
                }

                public void Dispose() => _owner._checkedContextOverride = _previous;
            }
            public LocalRewriter(Compilation compilation, MethodSymbol method)
            {
                _compilation = compilation;
                _method = method;
            }

            private LabelSymbol GenerateLabel(string debugName)
                => LabelSymbol.CreateGenerated($"<{debugName}_{_labelId++}>", _method);

            private LocalSymbol CreateTempLocal(TypeSymbol type)
                => new LocalSymbol($"$temp{_tempId++}", _method, type, ImmutableArray<Location>.Empty);

            private BoundStatement MakeConditionalGoto(SyntaxNode syntax, BoundExpression condition, LabelSymbol target, bool jumpIfTrue)
            {
                condition = SimplifyConditionForBranch(condition, ref jumpIfTrue);

                if (TryGetBoolConstant(condition, out var b))
                {
                    return b == jumpIfTrue
                        ? new BoundGotoStatement(syntax, target)
                        : new BoundEmptyStatement(syntax);
                }

                return new BoundConditionalGotoStatement(syntax, condition, target, jumpIfTrue);
            }

            private static BoundExpression SimplifyConditionForBranch(BoundExpression condition, ref bool jumpIfTrue)
            {
                while (true)
                {
                    // Drop identity conversions early
                    if (condition is BoundConversionExpression c &&
                        c.Conversion.Kind == ConversionKind.Identity &&
                        !c.HasErrors &&
                        c.Operand.Type == c.Type)
                    {
                        condition = c.Operand;
                        continue;
                    }

                    // !cond => invert branch
                    if (condition is BoundUnaryExpression u &&
                        u.OperatorKind == BoundUnaryOperatorKind.LogicalNot)
                    {
                        condition = u.Operand;
                        jumpIfTrue = !jumpIfTrue;
                        continue;
                    }

                    return condition;
                }
            }
            private static bool TryGetAutoPropertyBackingField(PropertySymbol prop, out FieldSymbol backingField)
            {
                if (prop is SourcePropertySymbol sp && sp.BackingFieldOpt is FieldSymbol bf)
                {
                    backingField = bf;
                    return true;
                }

                backingField = null!;
                return false;
            }
            private static bool TryGetBoolConstant(BoundExpression expr, out bool value)
            {
                if (expr.ConstantValueOpt.HasValue && expr.ConstantValueOpt.Value is bool b)
                {
                    value = b;
                    return true;
                }

                value = default;
                return false;
            }

            private static bool IsNoOpStatement(BoundStatement statement)
            {
                if (statement is BoundEmptyStatement)
                    return true;

                return statement is BoundBlockStatement b && b.Statements.IsDefaultOrEmpty;
            }
            protected override BoundStatement RewriteStatementList(BoundStatementList node)
            {
                var statements = RewriteStatements(node.Statements, out _);
                return new BoundBlockStatement(node.Syntax, statements);
            }
            protected override BoundMethodBody RewriteMethodBody(BoundMethodBody node)
            {
                var rewritten = base.RewriteMethodBody(node);
                if (!_method.IsConstructor || _method.IsStatic)
                    return rewritten;
                if (_method.ContainingSymbol is not NamedTypeSymbol containingType)
                    return rewritten;
                if (containingType.TypeKind != TypeKind.Class)
                    return rewritten;
                var baseType = containingType.BaseType as NamedTypeSymbol;
                if (baseType is null)
                    return rewritten;
                if (BodyStartsWithConstructorInitializer(rewritten.Body))
                    return rewritten;
                var baseCtor = FindParameterlessInstanceConstructor(baseType);
                if (baseCtor is null)
                    return rewritten;
                var thisExpr = new BoundThisExpression(new ThisExpressionSyntax(default), containingType);
                var call = new BoundCallExpression(node.Syntax, thisExpr, baseCtor, ImmutableArray<BoundExpression>.Empty);
                var stmt = new BoundExpressionStatement(node.Syntax, call);
                var newBody = PrependStatement(rewritten.Body, stmt);
                if (!ReferenceEquals(newBody, rewritten.Body))
                    return new BoundMethodBody(rewritten.Syntax, rewritten.Method, newBody);
                return rewritten;
            }
            private static BoundStatement PrependStatement(BoundStatement body, BoundStatement statement)
            {
                if (body is BoundBlockStatement block)
                {
                    var b = ImmutableArray.CreateBuilder<BoundStatement>(block.Statements.Length + 1);
                    b.Add(statement);
                    b.AddRange(block.Statements);
                    return new BoundBlockStatement(block.Syntax, b.ToImmutable());
                }
                return new BoundBlockStatement(body.Syntax, ImmutableArray.Create(statement, body));
            }
            private static bool BodyStartsWithConstructorInitializer(BoundStatement body)
            {
                if (body is BoundBlockStatement block && block.Statements.Length > 0)
                {
                    if (block.Statements[0] is BoundExpressionStatement es &&
                         es.Expression is BoundCallExpression call &&
                         call.Method.IsConstructor &&
                         call.ReceiverOpt is BoundThisExpression)
                    {
                        return true;
                    }
                }
                return false;
            }
            private static MethodSymbol? FindParameterlessInstanceConstructor(NamedTypeSymbol type)
            {
                var members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol m &&
                         m.IsConstructor &&
                         !m.IsStatic &&
                         m.Parameters.Length == 0)
                    {
                        return m;
                    }
                }
                return null;
            }
            protected override BoundStatement RewriteIfStatement(BoundIfStatement node)
            {
                var condition = RewriteExpression(node.Condition);

                if (TryGetBoolConstant(condition, out var constCond))
                {
                    if (constCond)
                        return RewriteStatement(node.Then);

                    return node.ElseOpt is null ? new BoundEmptyStatement(node.Syntax) : RewriteStatement(node.ElseOpt);
                }

                var thenStmt = RewriteStatement(node.Then);
                var elseStmt = node.ElseOpt is null ? null : RewriteStatement(node.ElseOpt);

                var thenNoOp = IsNoOpStatement(thenStmt);
                var elseNoOp = elseStmt is not null && IsNoOpStatement(elseStmt);

                // if (cond) { }  => evaluate cond for side-effects only
                if (elseStmt is null && thenNoOp)
                    return new BoundExpressionStatement(node.Syntax, condition);

                // if (cond) S else { }  => if (cond) S
                if (elseNoOp)
                    elseStmt = null;

                if (elseStmt is null)
                {
                    var endLabel = GenerateLabel("if_end");

                    var builder = ImmutableArray.CreateBuilder<BoundStatement>(3);
                    var branch = MakeConditionalGoto(node.Syntax, condition, endLabel, jumpIfTrue: false);
                    if (branch is not BoundEmptyStatement)
                        builder.Add(branch);
                    builder.Add(thenStmt);
                    builder.Add(new BoundLabelStatement(node.Syntax, endLabel));
                    return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
                }

                // if (cond) { } else S  => if (cond) goto end; S; end:
                if (thenNoOp)
                {
                    var endLabel = GenerateLabel("if_end");

                    var builder = ImmutableArray.CreateBuilder<BoundStatement>(3);
                    var branch = MakeConditionalGoto(node.Syntax, condition, endLabel, jumpIfTrue: true);
                    if (branch is not BoundEmptyStatement)
                        builder.Add(branch);
                    builder.Add(elseStmt);
                    builder.Add(new BoundLabelStatement(node.Syntax, endLabel));
                    return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
                }

                {
                    var elseLabel = GenerateLabel("if_else");
                    var endLabel = GenerateLabel("if_end");

                    var builder = ImmutableArray.CreateBuilder<BoundStatement>(6);
                    var branch = MakeConditionalGoto(node.Syntax, condition, elseLabel, jumpIfTrue: false);
                    if (branch is not BoundEmptyStatement)
                        builder.Add(branch);
                    builder.Add(thenStmt);
                    builder.Add(new BoundGotoStatement(node.Syntax, endLabel));
                    builder.Add(new BoundLabelStatement(node.Syntax, elseLabel));
                    builder.Add(elseStmt);
                    builder.Add(new BoundLabelStatement(node.Syntax, endLabel));
                    return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
                }
            }

            protected override BoundStatement RewriteWhileStatement(BoundWhileStatement node)
            {
                var condition = RewriteExpression(node.Condition);
                var body = RewriteStatement(node.Body);

                if (TryGetBoolConstant(condition, out var constCond) && !constCond)
                    return new BoundEmptyStatement(node.Syntax);

                var builder = ImmutableArray.CreateBuilder<BoundStatement>(5);

                builder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                var branch = MakeConditionalGoto(node.Syntax, condition, node.BreakLabel, jumpIfTrue: false);
                if (branch is not BoundEmptyStatement)
                    builder.Add(branch);
                builder.Add(body);
                builder.Add(new BoundGotoStatement(node.Syntax, node.ContinueLabel));
                builder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }

            protected override BoundStatement RewriteDoWhileStatement(BoundDoWhileStatement node)
            {
                var condition = RewriteExpression(node.Condition);
                var body = RewriteStatement(node.Body);

                var startLabel = GenerateLabel("do_start");
                var backEdge = MakeConditionalGoto(node.Syntax, condition, startLabel, jumpIfTrue: true);

                // do { body } while (false)  => body; (keep break/continue targets)
                if (backEdge is BoundEmptyStatement)
                {
                    var builderOnce = ImmutableArray.CreateBuilder<BoundStatement>(3);
                    builderOnce.Add(body);
                    builderOnce.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                    builderOnce.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));
                    return new BoundBlockStatement(node.Syntax, builderOnce.ToImmutable());
                }

                var builder = ImmutableArray.CreateBuilder<BoundStatement>(5);
                builder.Add(new BoundLabelStatement(node.Syntax, startLabel));
                builder.Add(body);
                builder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                builder.Add(backEdge);
                builder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }

            protected override BoundStatement RewriteForStatement(BoundForStatement node)
            {
                var inits = RewriteStatements(node.Initializers, out _);
                var incs = RewriteStatements(node.Incrementors, out _);
                var body = RewriteStatement(node.Body);

                var checkLabel = GenerateLabel("for_check");
                var condition = node.ConditionOpt is null ? null : RewriteExpression(node.ConditionOpt);

                if (condition is not null && TryGetBoolConstant(condition, out var constCond) && !constCond)
                {
                    if (inits.IsDefaultOrEmpty)
                        return new BoundEmptyStatement(node.Syntax);

                    return new BoundBlockStatement(node.Syntax, inits);
                }

                var builder = ImmutableArray.CreateBuilder<BoundStatement>();

                for (int i = 0; i < inits.Length; i++)
                    builder.Add(inits[i]);

                builder.Add(new BoundLabelStatement(node.Syntax, checkLabel));
                if (condition is not null)
                {
                    var branch = MakeConditionalGoto(node.Syntax, condition, node.BreakLabel, jumpIfTrue: false);
                    if (branch is not BoundEmptyStatement)
                        builder.Add(branch);
                }

                builder.Add(body);

                builder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                for (int i = 0; i < incs.Length; i++)
                    builder.Add(incs[i]);

                builder.Add(new BoundGotoStatement(node.Syntax, checkLabel));
                builder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }

            protected override BoundStatement RewriteBreakStatement(BoundBreakStatement node)
            {
                return new BoundGotoStatement(node.Syntax, node.TargetLabel);
            }

            protected override BoundStatement RewriteContinueStatement(BoundContinueStatement node)
            {
                return new BoundGotoStatement(node.Syntax, node.TargetLabel);
            }
            protected override BoundStatement RewriteCheckedStatement(BoundCheckedStatement node)
            {
                using (PushCheckedContext(true))
                    return RewriteStatement(node.Statement);
            }

            protected override BoundStatement RewriteUncheckedStatement(BoundUncheckedStatement node)
            {
                using (PushCheckedContext(false))
                    return RewriteStatement(node.Statement);
            }

            protected override BoundExpression RewriteCheckedExpression(BoundCheckedExpression node)
            {
                using (PushCheckedContext(true))
                    return RewriteExpression(node.Expression);
            }

            protected override BoundExpression RewriteUncheckedExpression(BoundUncheckedExpression node)
            {
                using (PushCheckedContext(false))
                    return RewriteExpression(node.Expression);
            }
            protected override BoundExpression RewriteConversionExpression(BoundConversionExpression node)
            {
                var operand = RewriteExpression(node.Operand);
                var isChecked = GetEffectiveIsChecked(node.IsChecked);

                if (!ReferenceEquals(operand, node.Operand) || isChecked != node.IsChecked)
                    return new BoundConversionExpression(node.Syntax, node.Type, operand, node.Conversion, isChecked);

                return node;
            }
            protected override BoundStatement RewriteExpressionStatement(BoundExpressionStatement node)
            {
                if (node.Expression is BoundIncrementDecrementExpression ide && ide.IsPostfix)
                {
                    var noResult = new BoundIncrementDecrementExpression(
                        ide.Syntax,
                        ide.Target,
                        ide.Read,
                        ide.Value,
                        ide.IsIncrement,
                        isPostfix: false,
                        ide.IsChecked);

                    var rewritten = RewriteExpression(noResult);
                    return new BoundExpressionStatement(node.Syntax, rewritten);
                }

                return base.RewriteExpressionStatement(node);
            }
            protected override BoundExpression RewriteUnaryExpression(BoundUnaryExpression node)
            {
                var operand = RewriteExpression(node.Operand);
                var isChecked = GetEffectiveIsChecked(node.IsChecked);
                if (!ReferenceEquals(operand, node.Operand) || isChecked != node.IsChecked)
                    return new BoundUnaryExpression(node.Syntax, node.OperatorKind, node.Type, operand, node.ConstantValueOpt, isChecked);
                return node;
            }
            protected override BoundExpression RewriteBinaryExpression(BoundBinaryExpression node)
            {
                if (node.OperatorKind == BoundBinaryOperatorKind.StringConcatenation)
                    return LowerStringConcatenation(node);

                if (node.OperatorKind == BoundBinaryOperatorKind.Add)
                    return RewriteBinaryOperatorChain(node);

                var left = RewriteExpression(node.Left);
                var right = RewriteExpression(node.Right);
                var isChecked = GetEffectiveIsChecked(node.IsChecked);
                if (!ReferenceEquals(left, node.Left) || !ReferenceEquals(right, node.Right) || isChecked != node.IsChecked)
                    return new BoundBinaryExpression(node.Syntax, node.OperatorKind, node.Type, left, right, node.ConstantValueOpt, isChecked);

                return node;
            }
            private BoundExpression LowerStringConcatenation(BoundBinaryExpression node)
            {
                // Flatten left associated chain
                var nodes = new List<BoundBinaryExpression>(8);
                var rightsReversed = new List<BoundExpression>(8);

                BoundExpression current = node;
                while (current is BoundBinaryExpression b && b.OperatorKind == BoundBinaryOperatorKind.StringConcatenation)
                {
                    nodes.Add(b);
                    rightsReversed.Add(b.Right);
                    current = b.Left;
                }

                int operandCount = 1 + rightsReversed.Count;
                var operands = new BoundExpression[operandCount];
                operands[0] = current;
                for (int i = 0; i < rightsReversed.Count; i++)
                    operands[i + 1] = rightsReversed[rightsReversed.Count - 1 - i];

                // Rewrite operands in eval order
                for (int i = 0; i < operands.Length; i++)
                    operands[i] = RewriteExpression(operands[i]);

                var stringType = _compilation.GetSpecialType(SpecialType.System_String);
                var objectType = _compilation.GetSpecialType(SpecialType.System_Object);
                var int32Type = _compilation.GetSpecialType(SpecialType.System_Int32);

                MethodSymbol FindConcat(params TypeSymbol[] ps)
                {
                    if (stringType is not NamedTypeSymbol nt)
                        throw new InvalidOperationException("System.String is not a named type.");

                    var members = nt.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is not MethodSymbol m) continue;
                        if (!m.IsStatic) continue;
                        if (!string.Equals(m.Name, "Concat", StringComparison.Ordinal)) continue;
                        if (!ReferenceEquals(m.ReturnType, stringType)) continue;

                        var mp = m.Parameters;
                        if (mp.Length != ps.Length) continue;

                        bool ok = true;
                        for (int k = 0; k < ps.Length; k++)
                        {
                            if (!ReferenceEquals(mp[k].Type, ps[k]))
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (ok) return m;
                    }
                    throw new MissingMethodException("System.String.Concat overload not found.");
                }

                BoundExpression ToObject(BoundExpression e)
                {
                    if (ReferenceEquals(e.Type, objectType))
                        return e;

                    var kind =
                        e.Type is NullTypeSymbol ? ConversionKind.NullLiteral :
                        e.Type.IsValueType ? ConversionKind.Boxing :
                        ConversionKind.ImplicitReference;

                    return new BoundConversionExpression(
                        e.Syntax,
                        objectType,
                        e,
                        new Conversion(kind),
                        isChecked: false);
                }

                // Prefer overloads without array for up to 4 operands
                if (operands.Length == 2)
                {
                    var m = FindConcat(objectType, objectType);
                    return new BoundCallExpression(node.Syntax, receiverOpt: null, m,
                        ImmutableArray.Create(ToObject(operands[0]), ToObject(operands[1])));
                }
                if (operands.Length == 3)
                {
                    var m = FindConcat(objectType, objectType, objectType);
                    return new BoundCallExpression(node.Syntax, receiverOpt: null, m,
                        ImmutableArray.Create(ToObject(operands[0]), ToObject(operands[1]), ToObject(operands[2])));
                }
                if (operands.Length == 4)
                {
                    var m = FindConcat(objectType, objectType, objectType, objectType);
                    return new BoundCallExpression(node.Syntax, receiverOpt: null, m,
                        ImmutableArray.Create(ToObject(operands[0]), ToObject(operands[1]), ToObject(operands[2]), ToObject(operands[3])));
                }

                // object[] path for longer chains
                var objArrayType = _compilation.CreateArrayType(objectType, rank: 1);
                var arrLocal = CreateTempLocal(objArrayType);

                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(2 + operands.Length);

                // arr = new object[operandCount]
                var countLit = new BoundLiteralExpression(node.Syntax, int32Type, operands.Length);
                var newArr = new BoundArrayCreationExpression(node.Syntax, objArrayType, objectType, countLit, initializerOpt: null);

                sideEffects.Add(new BoundExpressionStatement(
                    node.Syntax,
                    new BoundAssignmentExpression(
                        node.Syntax,
                        new BoundLocalExpression(node.Syntax, arrLocal),
                        newArr)));

                // arr[i] = (object)operand[i]
                for (int i = 0; i < operands.Length; i++)
                {
                    var idxLit = new BoundLiteralExpression(node.Syntax, int32Type, i);
                    var elem = new BoundArrayElementAccessExpression(
                        node.Syntax,
                        elementType: objectType,
                        expression: new BoundLocalExpression(node.Syntax, arrLocal),
                        index: idxLit);

                    sideEffects.Add(new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(node.Syntax, elem, ToObject(operands[i]))));
                }

                var concatArr = FindConcat(objArrayType);
                var call = new BoundCallExpression(
                    node.Syntax,
                    receiverOpt: null,
                    concatArr,
                    ImmutableArray.Create<BoundExpression>(new BoundLocalExpression(node.Syntax, arrLocal)));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals: ImmutableArray.Create(arrLocal),
                    sideEffects: sideEffects.ToImmutable(),
                    value: call);
            }
            private BoundExpression RewriteBinaryOperatorChain(BoundBinaryExpression node)
            {
                var op = node.OperatorKind;

                // Collect left nested nodes
                var nodes = new List<BoundBinaryExpression>(capacity: 8);
                var rightsReversed = new List<BoundExpression>(capacity: 8);

                BoundExpression current = node;
                while (current is BoundBinaryExpression b && b.OperatorKind == op)
                {
                    nodes.Add(b);
                    rightsReversed.Add(b.Right);
                    current = b.Left;
                }

                if (nodes.Count == 0)
                    return base.RewriteBinaryExpression(node);

                var forcedChecked = _checkedContextOverride;
                var anyCheckedDiff = forcedChecked.HasValue && nodes.Exists(n => n.IsChecked != forcedChecked.Value);

                // Rewrite operands in evaluation order
                var operandCount = 1 + rightsReversed.Count;
                var operands = new BoundExpression[operandCount];

                operands[0] = current;
                for (int i = 0; i < rightsReversed.Count; i++)
                    operands[i + 1] = rightsReversed[rightsReversed.Count - 1 - i];

                var rewrittenOperands = new BoundExpression[operandCount];
                bool anyChanged = false;

                for (int i = 0; i < operandCount; i++)
                {
                    var original = operands[i];
                    var rewritten = RewriteExpression(original);
                    rewrittenOperands[i] = rewritten;

                    if (!ReferenceEquals(original, rewritten))
                        anyChanged = true;
                }

                if (!anyChanged && !anyCheckedDiff)
                    return node;

                BoundExpression acc = rewrittenOperands[0];

                for (int k = 0; k < rightsReversed.Count; k++)
                {
                    var n = nodes[nodes.Count - 1 - k];
                    var right = rewrittenOperands[k + 1];

                    var isChecked = forcedChecked ?? n.IsChecked;

                    if (ReferenceEquals(acc, n.Left) && ReferenceEquals(right, n.Right) && isChecked == n.IsChecked)
                    {
                        acc = n;
                    }
                    else
                    {
                        acc = new BoundBinaryExpression(
                            n.Syntax,
                            n.OperatorKind,
                            n.Type,
                            acc,
                            right,
                            n.ConstantValueOpt,
                            isChecked);
                    }
                }

                return acc;
            }
            protected override BoundExpression RewriteAssignmentExpression(BoundAssignmentExpression node)
            {
                if (node.Left is BoundIndexerAccessExpression leftIndexer &&
                    leftIndexer.Indexer.SetMethod is MethodSymbol indexerSetMethod)
                {
                    return LowerIndexerAssignment(node, leftIndexer, indexerSetMethod);
                }

                if (node.Left is BoundMemberAccessExpression { Member: PropertySymbol prop } left)
                {
                    // Auto property
                    if (TryGetAutoPropertyBackingField(prop, out _))
                    {
                        var rewrittenLeft = RewriteExpression(node.Left);
                        var rewrittenRight = RewriteExpression(node.Right);
                        return new BoundAssignmentExpression(node.Syntax, rewrittenLeft, rewrittenRight);
                    }

                    // Regular property
                    if (prop.SetMethod is MethodSymbol setMethod)
                        return LowerPropertyAssignment(node, left, setMethod);
                }

                return base.RewriteAssignmentExpression(node);
            }
            protected override BoundExpression RewriteMemberAccessExpression(BoundMemberAccessExpression node)
            {
                // Auto property
                if (node.Member is PropertySymbol prop && TryGetAutoPropertyBackingField(prop, out var backingField))
                {
                    var rewrittenReceiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);

                    return new BoundMemberAccessExpression(
                        (ExpressionSyntax)node.Syntax,
                        rewrittenReceiver,
                        backingField,
                        backingField.Type,
                        isLValue: node.IsLValue,
                        constantValueOpt: Optional<object>.None);
                }

                // Regular property read
                if (node.Member is PropertySymbol p && p.GetMethod is MethodSymbol getMethod)
                {
                    var receiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
                    return new BoundCallExpression(node.Syntax, receiver, getMethod, ImmutableArray<BoundExpression>.Empty);
                }

                return base.RewriteMemberAccessExpression(node);
            }
            protected override BoundExpression RewriteIndexerAccessExpression(BoundIndexerAccessExpression node)
            {
                if (node.Indexer.GetMethod is MethodSymbol getMethod)
                {
                    var receiver = RewriteExpression(node.Receiver);
                    var args = RewriteExpressions(node.Arguments, out var argsChanged);

                    if (ReferenceEquals(receiver, node.Receiver) && !argsChanged)
                        return new BoundCallExpression(node.Syntax, receiver, getMethod, node.Arguments);

                    return new BoundCallExpression(node.Syntax, receiver, getMethod, args);
                }

                return base.RewriteIndexerAccessExpression(node);
            }
            private BoundExpression LowerPropertyAssignment(
                BoundAssignmentExpression node,
                BoundMemberAccessExpression left,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>(initialCapacity: 2);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(initialCapacity: 3);

                BoundExpression? receiver = left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt);

                // Ensure receiver is evaluated once
                if (receiver is not null && !IsSimpleReceiver(receiver))
                {
                    var receiverTemp = CreateTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(
                                node.Syntax,
                                new BoundLocalExpression(node.Syntax, receiverTemp),
                                receiver)));

                    receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                }
                // Ensure assigned value is evaluated once and is the result of the assignment expression
                var rewrittenRight = RewriteExpression(node.Right);

                var valueTemp = CreateTempLocal(node.Type);
                locals.Add(valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(
                            node.Syntax,
                            new BoundLocalExpression(node.Syntax, valueTemp),
                            rewrittenRight)));

                var valueExpr = new BoundLocalExpression(node.Syntax, valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            ImmutableArray.Create<BoundExpression>(valueExpr))));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    valueExpr);
            }
            private static bool IsSimpleReceiver(BoundExpression expr)
                => expr is BoundLocalExpression or BoundParameterExpression or BoundThisExpression;
            private BoundExpression LowerIncrementDecrementWithSpill(
                BoundIncrementDecrementExpression node,
                BoundExpression rewrittenTarget)
            {
                var localsBuilder = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffectsBuilder = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression lvalue;

                switch (rewrittenTarget)
                {
                    case BoundPointerIndirectionExpression ind:
                        {
                            var ptrTemp = CreateTempLocal(ind.Operand.Type);
                            localsBuilder.Add(ptrTemp);

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, ptrTemp),
                                        ind.Operand)));

                            lvalue = new BoundPointerIndirectionExpression(
                                node.Syntax,
                                ind.Type,
                                new BoundLocalExpression(node.Syntax, ptrTemp));

                            break;
                        }

                    case BoundPointerElementAccessExpression pea:
                        {
                            var ptrTemp = CreateTempLocal(pea.Expression.Type);
                            var idxTemp = CreateTempLocal(pea.Index.Type);

                            localsBuilder.Add(ptrTemp);
                            localsBuilder.Add(idxTemp);

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, ptrTemp),
                                        pea.Expression)));

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, idxTemp),
                                        pea.Index)));

                            lvalue = new BoundPointerElementAccessExpression(
                                node.Syntax,
                                pea.Type,
                                new BoundLocalExpression(node.Syntax, ptrTemp),
                                new BoundLocalExpression(node.Syntax, idxTemp));

                            break;
                        }

                    case BoundMemberAccessExpression ma when ma.Member is FieldSymbol fs:
                        {
                            BoundExpression? receiver = ma.ReceiverOpt;

                            if (receiver is not null && !IsSimpleReceiver(receiver))
                            {
                                var receiverTemp = CreateTempLocal(receiver.Type);
                                localsBuilder.Add(receiverTemp);

                                sideEffectsBuilder.Add(
                                    new BoundExpressionStatement(
                                        node.Syntax,
                                        new BoundAssignmentExpression(
                                            node.Syntax,
                                            new BoundLocalExpression(node.Syntax, receiverTemp),
                                            receiver)));

                                receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                            }

                            lvalue = new BoundMemberAccessExpression(
                                (ExpressionSyntax)node.Syntax,
                                receiver,
                                fs,
                                fs.Type,
                                isLValue: true,
                                constantValueOpt: Optional<object>.None);

                            break;
                        }
                    default:
                        throw new NotSupportedException(
                            $"Increment/decrement lowering for lvalue '{rewrittenTarget.GetType().Name}' is not implemented.");
                }
                BoundLocalExpression? oldValueExpr = null;
                if (node.IsPostfix)
                {
                    var oldTemp = CreateTempLocal(node.Type);
                    localsBuilder.Add(oldTemp);
                    oldValueExpr = new BoundLocalExpression(node.Syntax, oldTemp);

                    sideEffectsBuilder.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(node.Syntax, oldValueExpr, lvalue)));
                }

                var readForValue = (BoundExpression?)oldValueExpr ?? lvalue;
                var valueWithSpilledLValue = ReplaceExpressionByReference(node.Value, node.Read, readForValue);
                var rewrittenValueWithSpilledLValue = RewriteExpression(valueWithSpilledLValue);

                var assignment = new BoundAssignmentExpression(node.Syntax, lvalue, rewrittenValueWithSpilledLValue);

                if (!node.IsPostfix)
                {
                    if (localsBuilder.Count == 0 && sideEffectsBuilder.Count == 0)
                        return assignment;

                    return new BoundSequenceExpression(
                        node.Syntax,
                        localsBuilder.ToImmutable(),
                        sideEffectsBuilder.ToImmutable(),
                        assignment);
                }

                sideEffectsBuilder.Add(new BoundExpressionStatement(node.Syntax, assignment));

                return new BoundSequenceExpression(
                    node.Syntax,
                    localsBuilder.ToImmutable(),
                    sideEffectsBuilder.ToImmutable(),
                    oldValueExpr!);
            }
            private BoundExpression LowerPropertyIncrementDecrement(
                BoundIncrementDecrementExpression node,
                BoundMemberAccessExpression left,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>(initialCapacity: node.IsPostfix ? 3 : 2);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(initialCapacity: node.IsPostfix ? 4 : 3);

                BoundExpression? receiver = left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt);

                if (receiver is not null && !IsSimpleReceiver(receiver))
                {
                    var receiverTemp = CreateTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(
                                node.Syntax,
                                new BoundLocalExpression(node.Syntax, receiverTemp),
                                receiver)));

                    receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                }

                var getCall = new BoundCallExpression(node.Syntax, receiver, getMethod, ImmutableArray<BoundExpression>.Empty);

                BoundExpression readExprForValue = getCall;
                BoundLocalExpression? oldValueExpr = null;

                if (node.IsPostfix)
                {
                    var oldTemp = CreateTempLocal(node.Type);
                    locals.Add(oldTemp);
                    oldValueExpr = new BoundLocalExpression(node.Syntax, oldTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(node.Syntax, oldValueExpr, getCall)));

                    readExprForValue = oldValueExpr;
                }

                var valueWithGet = ReplaceExpressionByReference(node.Value, node.Read, readExprForValue);
                var rewrittenValue = RewriteExpression(valueWithGet);

                var assignedTemp = CreateTempLocal(node.Type);
                locals.Add(assignedTemp);
                var assignedValueExpr = new BoundLocalExpression(node.Syntax, assignedTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(node.Syntax, assignedValueExpr, rewrittenValue)));

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            ImmutableArray.Create<BoundExpression>(assignedValueExpr))));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    node.IsPostfix ? oldValueExpr! : assignedValueExpr);
            }
            private BoundExpression LowerSimpleIncrementDecrement(BoundIncrementDecrementExpression node, BoundExpression rewrittenTarget)
            {
                var valueWithRewrittenTarget = ReplaceExpressionByReference(node.Value, node.Read, rewrittenTarget);
                var rewrittenValue = RewriteExpression(valueWithRewrittenTarget);
                var assignment = new BoundAssignmentExpression(node.Syntax, rewrittenTarget, rewrittenValue);

                if (!node.IsPostfix)
                    return assignment;

                var oldTemp = CreateTempLocal(node.Type);
                var oldTempExpr = new BoundLocalExpression(node.Syntax, oldTemp);

                return new BoundSequenceExpression(
                    node.Syntax,
                    ImmutableArray.Create(oldTemp),
                    ImmutableArray.Create<BoundStatement>(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(node.Syntax, oldTempExpr, rewrittenTarget)),
                        new BoundExpressionStatement(node.Syntax, assignment)),
                    oldTempExpr);
            }
            protected override BoundExpression RewriteIncrementDecrementExpression(BoundIncrementDecrementExpression node)
            {
                if (node.Target is BoundIndexerAccessExpression leftIndexer &&
                    leftIndexer.Indexer.GetMethod is MethodSymbol indexerGetMethod &&
                    leftIndexer.Indexer.SetMethod is MethodSymbol indexerSetMethod)
                {
                    return LowerIndexerIncrementDecrement(node, leftIndexer, indexerGetMethod, indexerSetMethod);
                }

                if (node.Target is BoundMemberAccessExpression { Member: PropertySymbol prop } leftProp &&
                    !TryGetAutoPropertyBackingField(prop, out _) &&
                    prop.GetMethod is MethodSymbol getMethod &&
                    prop.SetMethod is MethodSymbol setMethod)
                {
                    return LowerPropertyIncrementDecrement(node, leftProp, getMethod, setMethod);
                }

                var rewrittenTarget = RewriteExpression(node.Target);

                if (rewrittenTarget is BoundLocalExpression || rewrittenTarget is BoundParameterExpression)
                    return LowerSimpleIncrementDecrement(node, rewrittenTarget);

                return LowerIncrementDecrementWithSpill(node, rewrittenTarget);
            }
            protected override BoundExpression RewriteCompoundAssignmentExpression(BoundCompoundAssignmentExpression node)
            {
                if (node.Left is BoundIndexerAccessExpression leftIndexer &&
                    leftIndexer.Indexer.GetMethod is MethodSymbol indexerGetMethod &&
                    leftIndexer.Indexer.SetMethod is MethodSymbol indexerSetMethod)
                {
                    return LowerIndexerCompoundAssignment(node, leftIndexer, indexerGetMethod, indexerSetMethod);
                }

                // Regular property compound assignment
                if (node.Left is BoundMemberAccessExpression { Member: PropertySymbol prop } leftProp &&
                    !TryGetAutoPropertyBackingField(prop, out _) &&
                    prop.GetMethod is MethodSymbol getMethod &&
                    prop.SetMethod is MethodSymbol setMethod)
                {
                    return LowerPropertyCompoundAssignment(node, leftProp, getMethod, setMethod);
                }

                var left = RewriteExpression(node.Left);

                if (left is BoundLocalExpression || left is BoundParameterExpression)
                {
                    var value = RewriteExpression(node.Value);
                    return new BoundAssignmentExpression(node.Syntax, left, value);
                }

                // Fields
                return LowerCompoundAssignmentWithSpill(node, left);
            }
            protected override BoundExpression RewriteNullCoalescingAssignmentExpression(BoundNullCoalescingAssignmentExpression node)
            {
                // indexer
                if (node.Left is BoundIndexerAccessExpression leftIndexer &&
                    leftIndexer.Indexer.GetMethod is MethodSymbol indexerGet &&
                    leftIndexer.Indexer.SetMethod is MethodSymbol indexerSet)
                {
                    return LowerIndexerNullCoalescingAssignment(node, leftIndexer, indexerGet, indexerSet);
                }

                // Property
                if (node.Left is BoundMemberAccessExpression { Member: PropertySymbol prop } leftProp)
                {
                    // Auto property => lower as backing field access (field-like)
                    if (TryGetAutoPropertyBackingField(prop, out var backingField))
                    {
                        return LowerAutoPropertyNullCoalescingAssignment(node, leftProp, backingField);
                    }

                    if (prop.GetMethod is MethodSymbol getMethod && prop.SetMethod is MethodSymbol setMethod)
                    {
                        return LowerPropertyNullCoalescingAssignment(node, leftProp, prop, getMethod, setMethod);
                    }
                }

                var rewrittenLeft = RewriteExpression(node.Left);

                if (rewrittenLeft is BoundLocalExpression || rewrittenLeft is BoundParameterExpression)
                {
                    var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                    var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();
                    return LowerNullCoalescingAssignmentCore(node, locals, sideEffects, readExpr: rewrittenLeft, lvalueForSet: rewrittenLeft);
                }

                return LowerNullCoalescingAssignmentWithSpill(node, rewrittenLeft);
            }
            private BoundExpression LowerPropertyNullCoalescingAssignment(
                BoundNullCoalescingAssignmentExpression node,
                BoundMemberAccessExpression left,
                PropertySymbol prop,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression? receiver = left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt);

                if (receiver is not null && !IsSimpleReceiver(receiver))
                {
                    var receiverTemp = CreateTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(
                                node.Syntax,
                                new BoundLocalExpression(node.Syntax, receiverTemp),
                                receiver)));

                    receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                }

                var getCall = new BoundCallExpression(node.Syntax, receiver, getMethod, ImmutableArray<BoundExpression>.Empty);

                var lvalueForSet = new BoundMemberAccessExpression(
                    (ExpressionSyntax)node.Syntax,
                    receiver,
                    prop,
                    prop.Type,
                    isLValue: true,
                    constantValueOpt: Optional<object>.None);

                return LowerNullCoalescingAssignmentCore(node, locals, sideEffects, readExpr: getCall, lvalueForSet: lvalueForSet);
            }
            private BoundExpression LowerAutoPropertyNullCoalescingAssignment(
                BoundNullCoalescingAssignmentExpression node,
                BoundMemberAccessExpression left,
                FieldSymbol backingField)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression? receiver = left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt);

                if (receiver is not null && !IsSimpleReceiver(receiver))
                {
                    var receiverTemp = CreateTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(
                                node.Syntax,
                                new BoundLocalExpression(node.Syntax, receiverTemp),
                                receiver)));

                    receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                }

                var fieldLValue = new BoundMemberAccessExpression(
                    (ExpressionSyntax)node.Syntax,
                    receiver,
                    backingField,
                    backingField.Type,
                    isLValue: true,
                    constantValueOpt: Optional<object>.None);

                return LowerNullCoalescingAssignmentCore(node, locals, sideEffects, readExpr: fieldLValue, lvalueForSet: fieldLValue);
            }

            private BoundExpression LowerIndexerNullCoalescingAssignment(
                BoundNullCoalescingAssignmentExpression node,
                BoundIndexerAccessExpression left,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                SpillIndexerReceiverAndArguments(
                    node.Syntax,
                    left.Receiver,
                    left.Arguments,
                    locals,
                    sideEffects,
                    out var receiver,
                    out var indexArgs);

                var getCall = new BoundCallExpression(node.Syntax, receiver!, getMethod, indexArgs);

                var lvalueForSet = new BoundIndexerAccessExpression(
                    (ExpressionSyntax)node.Syntax,
                    receiver!,
                    left.Indexer,
                    indexArgs,
                    isLValue: true);

                return LowerNullCoalescingAssignmentCore(node, locals, sideEffects, readExpr: getCall, lvalueForSet: lvalueForSet);
            }

            private BoundExpression LowerNullCoalescingAssignmentWithSpill(
                BoundNullCoalescingAssignmentExpression node,
                BoundExpression rewrittenLeft)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression lvalue;

                switch (rewrittenLeft)
                {
                    case BoundMemberAccessExpression ma when ma.Member is FieldSymbol fs:
                        {
                            BoundExpression? receiver = ma.ReceiverOpt;

                            if (receiver is not null && !IsSimpleReceiver(receiver))
                            {
                                var receiverTemp = CreateTempLocal(receiver.Type);
                                locals.Add(receiverTemp);

                                sideEffects.Add(
                                    new BoundExpressionStatement(
                                        node.Syntax,
                                        new BoundAssignmentExpression(
                                            node.Syntax,
                                            new BoundLocalExpression(node.Syntax, receiverTemp),
                                            receiver)));

                                receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                            }

                            lvalue = new BoundMemberAccessExpression(
                                (ExpressionSyntax)node.Syntax,
                                receiver,
                                fs,
                                fs.Type,
                                isLValue: true,
                                constantValueOpt: Optional<object>.None);

                            break;
                        }

                    default:
                        throw new NotSupportedException(
                            $"Null-coalescing assignment lowering for lvalue '{rewrittenLeft.GetType().Name}' is not implemented.");
                }

                return LowerNullCoalescingAssignmentCore(node, locals, sideEffects, readExpr: lvalue, lvalueForSet: lvalue);
            }
            private BoundExpression LowerNullCoalescingAssignmentCore(
                BoundNullCoalescingAssignmentExpression node,
                ImmutableArray<LocalSymbol>.Builder locals,
                ImmutableArray<BoundStatement>.Builder sideEffects,
                BoundExpression readExpr,
                BoundExpression lvalueForSet)
            {
                // tmp = readExpr
                var tmp = CreateTempLocal(node.Type);
                locals.Add(tmp);

                var tmpExpr = new BoundLocalExpression(node.Syntax, tmp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(node.Syntax, tmpExpr, readExpr)));

                var cond = MakeNullCoalescingAssignedCondition(node.Syntax, tmpExpr, node.Type);

                var whenFalse = new BoundAssignmentExpression(
                    node.Syntax,
                    lvalueForSet,
                    RewriteExpression(node.Value)); // RHS executed only on false branch

                var s = (AssignmentExpressionSyntax)node.Syntax;
                var condSyntax = new ConditionalExpressionSyntax(
                    condition: s.Left,
                    questionToken: default,
                    whenTrue: s.Left,
                    colonToken: default,
                    whenFalse: s.Right);

                var value = new BoundConditionalExpression(
                    condSyntax,
                    node.Type,
                    cond,
                    tmpExpr,
                    whenFalse,
                    Optional<object>.None);

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    value);
            }
            private BoundExpression MakeNullCoalescingAssignedCondition(SyntaxNode syntax, BoundExpression tmpExpr, TypeSymbol type)
            {
                var boolType = _compilation.GetSpecialType(SpecialType.System_Boolean);

                if (TryGetSystemNullableInfo(type, out var nullableType, out _))
                {
                    var hasValueGet = FindNullableHasValueGetter(nullableType)
                        ?? throw new InvalidOperationException("Nullable<T>.HasValue getter not found.");

                    return new BoundCallExpression(syntax, tmpExpr, hasValueGet, ImmutableArray<BoundExpression>.Empty);
                }

                if (!type.IsReferenceType && type is not NullTypeSymbol)
                    throw new InvalidOperationException("Invalid target type for ??= lowering.");

                var nullLit = new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null);

                return new BoundBinaryExpression(
                    syntax,
                    BoundBinaryOperatorKind.NotEquals,
                    boolType,
                    tmpExpr,
                    nullLit,
                    constantValueOpt: Optional<object>.None);
            }
            private static bool TryGetSystemNullableInfo(TypeSymbol t, out NamedTypeSymbol nullableType, out TypeSymbol underlying)
            {
                if (t is NamedTypeSymbol nt && nt.IsValueType)
                {
                    var def = nt.OriginalDefinition;
                    if (def.Arity == 1
                        && string.Equals(def.Name, "Nullable", StringComparison.Ordinal)
                        && def.ContainingSymbol is NamespaceSymbol ns
                        && string.Equals(ns.Name, "System", StringComparison.Ordinal))
                    {
                        nullableType = nt;
                        underlying = nt.TypeArguments[0];
                        return true;
                    }
                }

                nullableType = null!;
                underlying = null!;
                return false;
            }
            private static MethodSymbol? FindNullableHasValueGetter(NamedTypeSymbol nullableType)
            {
                foreach (var m in nullableType.GetMembers())
                {
                    if (m is PropertySymbol ps && string.Equals(ps.Name, "HasValue", StringComparison.Ordinal))
                        return ps.GetMethod;
                }
                return null;
            }
            private BoundExpression LowerPropertyCompoundAssignment(
                BoundCompoundAssignmentExpression node,
                BoundMemberAccessExpression left,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression? receiver = left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt);

                if (receiver is not null && !IsSimpleReceiver(receiver))
                {
                    var receiverTemp = CreateTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(
                                node.Syntax,
                                new BoundLocalExpression(node.Syntax, receiverTemp),
                                receiver)));

                    receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                }

                var getCall = new BoundCallExpression(node.Syntax, receiver, getMethod, ImmutableArray<BoundExpression>.Empty);

                var valueWithGet = ReplaceExpressionByReference(node.Value, node.Left, getCall);
                var rewrittenValue = RewriteExpression(valueWithGet);

                var valueTemp = CreateTempLocal(node.Type);
                locals.Add(valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(
                            node.Syntax,
                            new BoundLocalExpression(node.Syntax, valueTemp),
                            rewrittenValue)));

                var valueExpr = new BoundLocalExpression(node.Syntax, valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            ImmutableArray.Create<BoundExpression>(valueExpr))));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    valueExpr);
            }
            private BoundExpression LowerCompoundAssignmentWithSpill(
                BoundCompoundAssignmentExpression node,
                BoundExpression rewrittenLeft)
            {
                var localsBuilder = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffectsBuilder = ImmutableArray.CreateBuilder<BoundStatement>();

                BoundExpression lvalue;

                switch (rewrittenLeft)
                {
                    case BoundPointerIndirectionExpression ind:
                        {
                            var ptrTemp = CreateTempLocal(ind.Operand.Type);
                            localsBuilder.Add(ptrTemp);

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, ptrTemp),
                                        ind.Operand)));

                            lvalue = new BoundPointerIndirectionExpression(
                                node.Syntax,
                                ind.Type,
                                new BoundLocalExpression(node.Syntax, ptrTemp));

                            break;
                        }

                    case BoundPointerElementAccessExpression pea:
                        {
                            var ptrTemp = CreateTempLocal(pea.Expression.Type);
                            var idxTemp = CreateTempLocal(pea.Index.Type);

                            localsBuilder.Add(ptrTemp);
                            localsBuilder.Add(idxTemp);

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, ptrTemp),
                                        pea.Expression)));

                            sideEffectsBuilder.Add(
                                new BoundExpressionStatement(
                                    node.Syntax,
                                    new BoundAssignmentExpression(
                                        node.Syntax,
                                        new BoundLocalExpression(node.Syntax, idxTemp),
                                        pea.Index)));

                            lvalue = new BoundPointerElementAccessExpression(
                                node.Syntax,
                                pea.Type,
                                new BoundLocalExpression(node.Syntax, ptrTemp),
                                new BoundLocalExpression(node.Syntax, idxTemp));

                            break;
                        }
                    case BoundMemberAccessExpression ma when ma.Member is FieldSymbol fs:
                        {
                            BoundExpression? receiver = ma.ReceiverOpt;

                            if (receiver is not null && !IsSimpleReceiver(receiver))
                            {
                                var receiverTemp = CreateTempLocal(receiver.Type);
                                localsBuilder.Add(receiverTemp);

                                sideEffectsBuilder.Add(
                                    new BoundExpressionStatement(
                                        node.Syntax,
                                        new BoundAssignmentExpression(
                                            node.Syntax,
                                            new BoundLocalExpression(node.Syntax, receiverTemp),
                                            receiver)));

                                receiver = new BoundLocalExpression(node.Syntax, receiverTemp);
                            }

                            lvalue = new BoundMemberAccessExpression(
                                (ExpressionSyntax)node.Syntax,
                                receiver,
                                fs,
                                fs.Type,
                                isLValue: true,
                                constantValueOpt: Optional<object>.None);

                            break;
                        }


                    default:
                        throw new NotSupportedException(
                            $"Compound assignment lowering for lvalue '{rewrittenLeft.GetType().Name}' is not implemented.");
                }

                var valueWithSpilledLValue = ReplaceExpressionByReference(node.Value, node.Left, lvalue);
                var rewrittenValueWithSpilledLValue = RewriteExpression(valueWithSpilledLValue);

                var assignment = new BoundAssignmentExpression(node.Syntax, lvalue, rewrittenValueWithSpilledLValue);

                return new BoundSequenceExpression(
                    node.Syntax,
                    localsBuilder.ToImmutable(),
                    sideEffectsBuilder.ToImmutable(),
                    assignment);
            }

            private BoundExpression LowerIndexerAssignment(
                BoundAssignmentExpression node,
                BoundIndexerAccessExpression left,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                SpillIndexerReceiverAndArguments(
                    node.Syntax,
                    left.Receiver,
                    left.Arguments,
                    locals,
                    sideEffects,
                    out var receiver,
                    out var indexArgs);

                var rewrittenRight = RewriteExpression(node.Right);

                var valueTemp = CreateTempLocal(node.Type);
                locals.Add(valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(
                            node.Syntax,
                            new BoundLocalExpression(node.Syntax, valueTemp),
                            rewrittenRight)));

                var valueExpr = new BoundLocalExpression(node.Syntax, valueTemp);

                var setArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(indexArgs.Length + 1);
                for (int i = 0; i < indexArgs.Length; i++)
                    setArgsBuilder.Add(indexArgs[i]);
                setArgsBuilder.Add(valueExpr);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            setArgsBuilder.ToImmutable())));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    valueExpr);
            }
            private BoundExpression LowerIndexerCompoundAssignment(
                BoundCompoundAssignmentExpression node,
                BoundIndexerAccessExpression left,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>();
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>();

                
                SpillIndexerReceiverAndArguments(
                    node.Syntax,
                    left.Receiver,
                    left.Arguments,
                    locals,
                    sideEffects,
                    out var receiver,
                    out var indexArgs);

                var getCall = new BoundCallExpression(node.Syntax, receiver, getMethod, indexArgs);

                var valueWithGet = ReplaceExpressionByReference(node.Value, node.Left, getCall);
                var rewrittenValue = RewriteExpression(valueWithGet);

                var valueTemp = CreateTempLocal(node.Type);
                locals.Add(valueTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(
                            node.Syntax,
                            new BoundLocalExpression(node.Syntax, valueTemp),
                            rewrittenValue)));

                var valueExpr = new BoundLocalExpression(node.Syntax, valueTemp);

                var setArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(indexArgs.Length + 1);
                for (int i = 0; i < indexArgs.Length; i++)
                    setArgsBuilder.Add(indexArgs[i]);
                setArgsBuilder.Add(valueExpr);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            setArgsBuilder.ToImmutable())));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    valueExpr);
            }
            private BoundExpression LowerIndexerIncrementDecrement(
                BoundIncrementDecrementExpression node,
                BoundIndexerAccessExpression left,
                MethodSymbol getMethod,
                MethodSymbol setMethod)
            {
                var locals = ImmutableArray.CreateBuilder<LocalSymbol>(initialCapacity: node.IsPostfix ? 4 : 3);
                var sideEffects = ImmutableArray.CreateBuilder<BoundStatement>(initialCapacity: node.IsPostfix ? 5 : 4);

                SpillIndexerReceiverAndArguments(
                    node.Syntax,
                    left.Receiver,
                    left.Arguments,
                    locals,
                    sideEffects,
                    out var receiver,
                    out var indexArgs);

                var getCall = new BoundCallExpression(node.Syntax, receiver, getMethod, indexArgs);

                BoundExpression readExprForValue = getCall;
                BoundLocalExpression? oldValueExpr = null;

                if (node.IsPostfix)
                {
                    var oldTemp = CreateTempLocal(node.Type);
                    locals.Add(oldTemp);
                    oldValueExpr = new BoundLocalExpression(node.Syntax, oldTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            node.Syntax,
                            new BoundAssignmentExpression(node.Syntax, oldValueExpr, getCall)));

                    readExprForValue = oldValueExpr;
                }

                var valueWithGet = ReplaceExpressionByReference(node.Value, node.Read, readExprForValue);
                var rewrittenValue = RewriteExpression(valueWithGet);

                var assignedTemp = CreateTempLocal(node.Type);
                locals.Add(assignedTemp);
                var assignedValueExpr = new BoundLocalExpression(node.Syntax, assignedTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundAssignmentExpression(node.Syntax, assignedValueExpr, rewrittenValue)));

                var setArgsBuilder = ImmutableArray.CreateBuilder<BoundExpression>(indexArgs.Length + 1);
                for (int i = 0; i < indexArgs.Length; i++)
                    setArgsBuilder.Add(indexArgs[i]);
                setArgsBuilder.Add(assignedValueExpr);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        node.Syntax,
                        new BoundCallExpression(
                            node.Syntax,
                            receiver,
                            setMethod,
                            setArgsBuilder.ToImmutable())));

                return new BoundSequenceExpression(
                    node.Syntax,
                    locals.ToImmutable(),
                    sideEffects.ToImmutable(),
                    node.IsPostfix ? oldValueExpr! : assignedValueExpr);
            }
            private void SpillIndexerReceiverAndArguments(
                SyntaxNode syntax,
                BoundExpression? receiver,
                ImmutableArray<BoundExpression> arguments,
                ImmutableArray<LocalSymbol>.Builder locals,
                ImmutableArray<BoundStatement>.Builder sideEffects,
                out BoundExpression? spilledReceiver,
                out ImmutableArray<BoundExpression> spilledArguments)
            {
                spilledReceiver = receiver is null ? null : RewriteExpression(receiver);

                if (spilledReceiver is not null && !IsSimpleReceiver(spilledReceiver))
                {
                    var receiverTemp = CreateTempLocal(spilledReceiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            syntax,
                            new BoundAssignmentExpression(
                                syntax,
                                new BoundLocalExpression(syntax, receiverTemp),
                                spilledReceiver)));

                    spilledReceiver = new BoundLocalExpression(syntax, receiverTemp);
                }

                var argBuilder = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length);
                for (int i = 0; i < arguments.Length; i++)
                {
                    var rewrittenArg = RewriteExpression(arguments[i]);

                    if (IsSimpleSpillExpression(rewrittenArg))
                    {
                        argBuilder.Add(rewrittenArg);
                        continue;
                    }

                    var argTemp = CreateTempLocal(rewrittenArg.Type);
                    locals.Add(argTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            syntax,
                            new BoundAssignmentExpression(
                                syntax,
                                new BoundLocalExpression(syntax, argTemp),
                                rewrittenArg)));

                    argBuilder.Add(new BoundLocalExpression(syntax, argTemp));
                }

                spilledArguments = argBuilder.ToImmutable();
            }
            private static bool IsSimpleSpillExpression(BoundExpression expr)
                => expr is BoundLocalExpression
                        or BoundParameterExpression
                        or BoundThisExpression
                        or BoundLiteralExpression;
            private static BoundExpression ReplaceExpressionByReference(
                BoundExpression root, BoundExpression from, BoundExpression to)
            {
                if (ReferenceEquals(root, from))
                    return to;

                var replacer = new ReferenceReplacingRewriter(from, to);
                return (BoundExpression)replacer.RewriteNode(root);
            }
            private sealed class ReferenceReplacingRewriter : BoundTreeRewriter
            {
                private readonly BoundExpression _from;
                private readonly BoundExpression _to;

                public ReferenceReplacingRewriter(BoundExpression from, BoundExpression to)
                {
                    _from = from;
                    _to = to;
                }
                protected override BoundExpression RewriteExpression(BoundExpression node)
                {
                    if (ReferenceEquals(node, _from))
                        return _to;

                    return base.RewriteExpression(node);
                }
            }
        }
        private sealed class CleanupRewriter : BoundTreeRewriterWithStackGuard
        {
            protected override BoundStatement RewriteStatementList(BoundStatementList node)
            {
                // just in case
                return RewriteBlockStatement(new BoundBlockStatement(node.Syntax, node.Statements));
            }

            protected override BoundStatement RewriteBlockStatement(BoundBlockStatement node)
            {
                if (node.Statements.IsDefaultOrEmpty)
                    return new BoundEmptyStatement(node.Syntax);

                var builder = ImmutableArray.CreateBuilder<BoundStatement>(node.Statements.Length);
                bool changed = false;

                for (int i = 0; i < node.Statements.Length; i++)
                {
                    var s = node.Statements[i];
                    var r = RewriteStatement(s);

                    if (r is BoundEmptyStatement)
                    {
                        changed = true;
                        continue;
                    }

                    // BoundBlockStatement does not carry locals; should be safe to flatten
                    if (r is BoundBlockStatement block)
                    {
                        changed = true;
                        builder.AddRange(block.Statements);
                        continue;
                    }

                    if (!ReferenceEquals(r, s))
                        changed = true;

                    builder.Add(r);
                }

                // Remove jumps to the immediately following location
                for (int i = 0; i < builder.Count - 1; i++)
                {
                    if (builder[i] is BoundGotoStatement g)
                    {
                        if (IsTargetInFollowingLabelRun(builder, i + 1, g.TargetLabel))
                        {
                            builder.RemoveAt(i);
                            changed = true;
                            i--;
                        }

                        continue;
                    }

                    if (builder[i] is BoundConditionalGotoStatement cg)
                    {
                        if (IsTargetInFollowingLabelRun(builder, i + 1, cg.TargetLabel))
                        {
                            builder[i] = new BoundExpressionStatement(cg.Syntax, cg.Condition);
                            changed = true;
                        }
                    }
                }

                // Drop unreferenced generated labels
                var usedLabels = new HashSet<LabelSymbol>();
                for (int i = 0; i < builder.Count; i++)
                {
                    switch (builder[i])
                    {
                        case BoundGotoStatement g:
                            usedLabels.Add(g.TargetLabel);
                            break;
                        case BoundConditionalGotoStatement cg:
                            usedLabels.Add(cg.TargetLabel);
                            break;
                    }
                }

                for (int i = 0; i < builder.Count; i++)
                {
                    if (builder[i] is BoundLabelStatement l &&
                        !usedLabels.Contains(l.Label) &&
                        IsGeneratedLabel(l.Label))
                    {
                        builder.RemoveAt(i);
                        changed = true;
                        i--;
                    }
                }

                if (!changed)
                    return node;

                if (builder.Count == 0)
                    return new BoundEmptyStatement(node.Syntax);

                if (builder.Count == 1)
                    return builder[0];

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }

            private static bool IsGeneratedLabel(LabelSymbol label)
                => label.Locations.IsDefaultOrEmpty && label.DeclaringSyntaxReferences.IsDefaultOrEmpty;

            private static bool IsTargetInFollowingLabelRun(
                ImmutableArray<BoundStatement>.Builder statements,
                int startIndex,
                LabelSymbol target)
            {
                for (int i = startIndex; i < statements.Count; i++)
                {
                    if (statements[i] is BoundLabelStatement l)
                    {
                        if (ReferenceEquals(l.Label, target))
                            return true;

                        continue;
                    }

                    break;
                }

                return false;
            }

            protected override BoundExpression RewriteConversionExpression(BoundConversionExpression node)
            {
                var operand = RewriteExpression(node.Operand);

                if (node.Conversion.Kind == ConversionKind.Identity &&
                    !operand.HasErrors &&
                    operand.Type == node.Type)
                {
                    return operand;
                }

                if (!ReferenceEquals(operand, node.Operand))
                    return new BoundConversionExpression(node.Syntax, node.Type, operand, node.Conversion, node.IsChecked);

                return node;
            }

            protected override BoundExpression RewriteSequenceExpression(BoundSequenceExpression node)
            {
                var locals = node.Locals;

                var sideEffects = RewriteStatements(node.SideEffects, out var sideEffectsChanged);
                sideEffects = FilterEmptyAndFlattenBlocks(sideEffects, ref sideEffectsChanged);

                var value = RewriteExpression(node.Value);

                // Merge nested sequences
                if (value is BoundSequenceExpression inner)
                {
                    var mergedLocals = Concat(locals, inner.Locals);
                    var mergedSideEffects = Concat(sideEffects, inner.SideEffects);

                    if (mergedLocals.IsDefaultOrEmpty && mergedSideEffects.IsDefaultOrEmpty)
                        return inner.Value;

                    return new BoundSequenceExpression(node.Syntax, mergedLocals, mergedSideEffects, inner.Value);
                }

                if (locals.IsDefaultOrEmpty && sideEffects.IsDefaultOrEmpty)
                    return value;

                if (sideEffectsChanged || !ReferenceEquals(value, node.Value))
                    return new BoundSequenceExpression(node.Syntax, locals, sideEffects, value);

                return node;
            }

            private static ImmutableArray<BoundStatement> FilterEmptyAndFlattenBlocks(
                ImmutableArray<BoundStatement> statements,
                ref bool changed)
            {
                if (statements.IsDefaultOrEmpty)
                    return statements;

                ImmutableArray<BoundStatement>.Builder? builder = null;

                for (int i = 0; i < statements.Length; i++)
                {
                    var s = statements[i];

                    if (s is BoundEmptyStatement)
                    {
                        changed = true;

                        if (builder is null)
                        {
                            builder = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
                            for (int j = 0; j < i; j++)
                                builder.Add(statements[j]);
                        }

                        continue;
                    }

                    if (s is BoundBlockStatement b)
                    {
                        changed = true;

                        if (builder is null)
                        {
                            builder = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length + b.Statements.Length);
                            for (int j = 0; j < i; j++)
                                builder.Add(statements[j]);
                        }

                        builder.AddRange(b.Statements);
                        continue;
                    }

                    builder?.Add(s);
                }

                return builder is null ? statements : builder.ToImmutable();
            }

            private static ImmutableArray<T> Concat<T>(ImmutableArray<T> first, ImmutableArray<T> second)
            {
                if (first.IsDefaultOrEmpty)
                    return second;
                if (second.IsDefaultOrEmpty)
                    return first;

                var builder = ImmutableArray.CreateBuilder<T>(first.Length + second.Length);
                builder.AddRange(first);
                builder.AddRange(second);
                return builder.ToImmutable();
            }
        }


    }
}
