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

        protected override BoundExpression RewriteExpression(BoundExpression node)
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
                BoundForEachStatement s => RewriteForEachStatement(s),
                BoundLabelStatement s => s,
                BoundGotoStatement s => s,
                BoundConditionalGotoStatement s => RewriteConditionalGotoStatement(s),
                BoundBreakStatement s => RewriteBreakStatement(s),
                BoundContinueStatement s => RewriteContinueStatement(s),
                BoundCheckedStatement s => RewriteCheckedStatement(s),
                BoundUncheckedStatement s => RewriteUncheckedStatement(s),
                BoundLocalFunctionStatement s => RewriteLocalFunctionStatement(s),
                BoundFixedStatement s => RewriteFixedStatement(s),
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
                BoundBaseExpression e => e,
                BoundLabelExpression e => e,
                BoundSizeOfExpression e => e,


                BoundThrowExpression e => RewriteThrowExpression(e),
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
                BoundUnboundCollectionExpression e => e,
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
                BoundFixedInitializerExpression e => RewriteFixedInitializerExpression(e),
                BoundIsPatternExpression e => RewriteIsPatternExpression(e),

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
            var rewrittenTry = RewriteStatement(node.TryBlock);
            var tryBlock = EnsureBlockStatement(node.TryBlock, rewrittenTry);

            bool catchesChanged = false;
            var catches = ImmutableArray.CreateBuilder<BoundCatchBlock>(node.CatchBlocks.Length);
            for (int i = 0; i < node.CatchBlocks.Length; i++)
            {
                var c = RewriteCatchBlock(node.CatchBlocks[i]);
                if (!ReferenceEquals(c, node.CatchBlocks[i]))
                    catchesChanged = true;
                catches.Add(c);
            }

            BoundBlockStatement? finallyBlock = null;
            if (node.FinallyBlockOpt is not null)
            {
                var rewrittenFinally = RewriteStatement(node.FinallyBlockOpt);
                finallyBlock = EnsureBlockStatement(node.FinallyBlockOpt, rewrittenFinally);
            }

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

            var rewrittenBody = RewriteStatement(node.Body);
            var body = EnsureBlockStatement(node.Body, rewrittenBody);

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
        private static BoundBlockStatement EnsureBlockStatement(BoundBlockStatement original, BoundStatement rewritten)
        {
            if (rewritten is BoundBlockStatement block)
                return block;

            var statements = rewritten is BoundEmptyStatement
                ? ImmutableArray<BoundStatement>.Empty
                : ImmutableArray.Create(rewritten);

            var wrapped = new BoundBlockStatement(original.Syntax, statements);

            if (rewritten.HasErrors)
                wrapped.SetHasErrors();

            return wrapped;
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
        protected virtual BoundStatement RewriteFixedStatement(BoundFixedStatement node)
        {
            var changed = false;
            var decls = ImmutableArray.CreateBuilder<BoundLocalDeclarationStatement>(node.Declarations.Length);

            for (int i = 0; i < node.Declarations.Length; i++)
            {
                var d = (BoundLocalDeclarationStatement)RewriteLocalDeclarationStatement(node.Declarations[i]);
                if (!ReferenceEquals(d, node.Declarations[i]))
                    changed = true;
                decls.Add(d);
            }

            var body = RewriteStatement(node.Body);
            if (!ReferenceEquals(body, node.Body))
                changed = true;

            return changed
                ? new BoundFixedStatement((FixedStatementSyntax)node.Syntax, decls.ToImmutable(), body)
                : node;
        }
        protected virtual BoundExpression RewriteFixedInitializerExpression(BoundFixedInitializerExpression node)
        {
            var expr = RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
            {
                return new BoundFixedInitializerExpression(
                    syntax: node.Syntax,
                    declaredPointerType: (PointerTypeSymbol)node.Type,
                    initializerKind: node.InitializerKind,
                    expression: expr,
                    elementType: node.ElementType,
                    elementPointerConversion: node.ElementPointerConversion,
                    getPinnableReferenceMethodOpt: node.GetPinnableReferenceMethodOpt);
            }

            return node;
        }
        protected virtual BoundExpression RewriteIsPatternExpression(BoundIsPatternExpression node)
        {
            var operand = RewriteExpression(node.Operand);
            var constant = node.ConstantOpt is null ? null : RewriteExpression(node.ConstantOpt);

            if (!ReferenceEquals(operand, node.Operand) ||
                !ReferenceEquals(constant, node.ConstantOpt))
            {
                return new BoundIsPatternExpression(
                    syntax: node.Syntax,
                    operand: operand,
                    boolType: node.Type,
                    patternKind: node.PatternKind,
                    patternTypeOpt: node.PatternTypeOpt,
                    constantOpt: constant,
                    comparisonTypeOpt: node.ComparisonTypeOpt,
                    declaredLocalOpt: node.DeclaredLocalOpt,
                    isDiscard: node.IsDiscard,
                    isNegated: node.IsNegated);
            }

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
        protected virtual BoundStatement RewriteForEachStatement(BoundForEachStatement node)
        {
            var collection = RewriteExpression(node.Collection);
            var body = RewriteStatement(node.Body);

            if (!ReferenceEquals(collection, node.Collection) ||
                !ReferenceEquals(body, node.Body))
            {
                return new BoundForEachStatement(
                    (ForEachStatementSyntax)node.Syntax,
                    node.EnumeratorKind,
                    node.IterationVariable,
                    collection,
                    node.CollectionType,
                    node.EnumeratorType,
                    node.ElementType,
                    node.CollectionConversion,
                    node.GetEnumeratorMethodOpt,
                    node.GetEnumeratorIsExtensionMethod,
                    node.CurrentPropertyOpt,
                    node.MoveNextMethodOpt,
                    node.IterationConversion,
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
        protected virtual BoundExpression RewriteThrowExpression(BoundThrowExpression node)
        {
            var ex = RewriteExpression(node.Exception);
            if (!ReferenceEquals(ex, node.Exception))
            {
                var rewritten = new BoundThrowExpression((ThrowExpressionSyntax)node.Syntax, ex);
                rewritten.SetType(node.Type);
                return rewritten;
            }
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
                return new BoundCompoundAssignmentExpression(node.Syntax, left, node.OperatorKind, value,
                    node.OperatorMethodOpt, node.UsesDirectOperator, node.IsChecked);

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
                    node.OperatorMethodOpt,
                    node.UsesDirectOperator,
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
                     node.Syntax,
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


    internal sealed class LocalFunctionClosureRewriter : BoundTreeRewriterWithStackGuard
    {
        private readonly Compilation _compilation;
        private readonly List<Dictionary<LocalFunctionSymbol, CaptureInfo>> _localFunctionScopes = new();
        private readonly Dictionary<Symbol, ParameterSymbol> _captureParameterMap =
            new(ReferenceEqualityComparer<Symbol>.Instance);

        private readonly struct CaptureInfo
        {
            public readonly LocalFunctionSymbol Original;
            public readonly LocalFunctionSymbol Lowered;
            public readonly ImmutableArray<Symbol> CapturedSymbols;
            public readonly ImmutableArray<ParameterSymbol> HiddenParameters;

            public CaptureInfo(
                LocalFunctionSymbol original,
                LocalFunctionSymbol lowered,
                ImmutableArray<Symbol> capturedSymbols,
                ImmutableArray<ParameterSymbol> hiddenParameters)
            {
                Original = original;
                Lowered = lowered;
                CapturedSymbols = capturedSymbols.IsDefault ? ImmutableArray<Symbol>.Empty : capturedSymbols;
                HiddenParameters = hiddenParameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : hiddenParameters;
            }
        }

        private readonly struct SavedCaptureParameter
        {
            public readonly Symbol Symbol;
            public readonly bool HadOldValue;
            public readonly ParameterSymbol OldValue;

            public SavedCaptureParameter(Symbol symbol, bool hadOldValue, ParameterSymbol oldValue)
            {
                Symbol = symbol;
                HadOldValue = hadOldValue;
                OldValue = oldValue;
            }
        }

        private sealed class CaptureCollector : BoundTreeRewriter
        {
            private readonly MethodSymbol _owner;
            private readonly HashSet<Symbol> _seen = new(ReferenceEqualityComparer<Symbol>.Instance);
            private readonly ImmutableArray<Symbol>.Builder _captures = ImmutableArray.CreateBuilder<Symbol>();

            private CaptureCollector(MethodSymbol owner)
            {
                _owner = owner;
            }

            public static ImmutableArray<Symbol> Collect(MethodSymbol owner, BoundStatement body)
            {
                var collector = new CaptureCollector(owner);
                collector.RewriteStatement(body);
                return collector._captures.ToImmutable();
            }

            protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
                => node;

            protected override BoundExpression RewriteExpression(BoundExpression node)
            {
                switch (node)
                {
                    case BoundLocalExpression local when !ReferenceEquals(local.Local.ContainingSymbol, _owner):
                        Add(local.Local);
                        return node;

                    case BoundParameterExpression parameter when !ReferenceEquals(parameter.Parameter.ContainingSymbol, _owner):
                        Add(parameter.Parameter);
                        return node;

                    default:
                        return base.RewriteExpression(node);
                }
            }

            private void Add(Symbol symbol)
            {
                if (_seen.Add(symbol))
                    _captures.Add(symbol);
            }
        }

        private sealed class NestedLocalFunctionCollector : BoundTreeRewriter
        {
            private readonly ImmutableArray<BoundLocalFunctionStatement>.Builder _localFunctions =
                ImmutableArray.CreateBuilder<BoundLocalFunctionStatement>();

            private NestedLocalFunctionCollector()
            {
            }

            public static ImmutableArray<BoundLocalFunctionStatement> Collect(BoundStatement body)
            {
                var collector = new NestedLocalFunctionCollector();
                collector.RewriteStatement(body);
                return collector._localFunctions.ToImmutable();
            }

            protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
            {
                _localFunctions.Add(node);
                return node;
            }
        }

        public LocalFunctionClosureRewriter(Compilation compilation)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        }

        protected override BoundStatement RewriteBlockStatement(BoundBlockStatement node)
        {
            var statements = RewriteScopedStatements(node.Statements, out var changed);
            if (changed)
                return new BoundBlockStatement(node.Syntax, statements);

            return node;
        }

        protected override BoundStatement RewriteStatementList(BoundStatementList node)
        {
            var statements = RewriteScopedStatements(node.Statements, out var changed);
            if (changed)
                return new BoundStatementList(node.Syntax, statements);

            return node;
        }

        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
        {
            if (!TryGetCaptureInfo(node.LocalFunction, out var info))
                return base.RewriteLocalFunctionStatement(node);

            var saved = PushCaptureParameters(node.Syntax, info);
            try
            {
                var body = RewriteStatement(node.Body);
                if (!ReferenceEquals(body, node.Body) || !ReferenceEquals(info.Lowered, node.LocalFunction))
                {
                    return new BoundLocalFunctionStatement(
                        (LocalFunctionStatementSyntax)node.Syntax,
                        info.Lowered,
                        body);
                }

                return node;
            }
            finally
            {
                PopCaptureParameters(saved);
            }
        }

        protected override BoundExpression RewriteExpression(BoundExpression node)
        {
            switch (node)
            {
                case BoundLocalExpression local when _captureParameterMap.TryGetValue(local.Local, out var localCapture):
                    return new BoundParameterExpression(local.Syntax, localCapture);

                case BoundParameterExpression parameter when _captureParameterMap.TryGetValue(parameter.Parameter, out var parameterCapture):
                    return new BoundParameterExpression(parameter.Syntax, parameterCapture);

                case BoundCallExpression call:
                    return RewriteCallExpressionWithCaptures(call);

                default:
                    return base.RewriteExpression(node);
            }
        }

        private BoundExpression RewriteCallExpressionWithCaptures(BoundCallExpression node)
        {
            var receiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
            var arguments = RewriteExpressions(node.Arguments, out var argsChanged);

            if (node.Method is LocalFunctionSymbol localFunction && TryGetCaptureInfo(localFunction, out var info))
            {
                ImmutableArray<BoundExpression> rewrittenArguments = arguments;

                if (!info.HiddenParameters.IsDefaultOrEmpty)
                {
                    var builder = ImmutableArray.CreateBuilder<BoundExpression>(arguments.Length + info.HiddenParameters.Length);
                    builder.AddRange(arguments);

                    for (int i = 0; i < info.HiddenParameters.Length; i++)
                    {
                        var hiddenParameter = info.HiddenParameters[i];
                        var capturedValue = MakeCurrentCaptureValueExpression(node.Syntax, info.CapturedSymbols[i]);

                        builder.Add(new BoundRefExpression(
                            node.Syntax,
                            hiddenParameter.Type,
                            capturedValue));
                    }

                    rewrittenArguments = builder.ToImmutable();
                }

                if (!ReferenceEquals(receiver, node.ReceiverOpt) ||
                    argsChanged ||
                    !ReferenceEquals(info.Lowered, node.Method) ||
                    rewrittenArguments.Length != node.Arguments.Length)
                {
                    return new BoundCallExpression(node.Syntax, receiver, info.Lowered, rewrittenArguments);
                }

                return node;
            }

            if (!ReferenceEquals(receiver, node.ReceiverOpt) || argsChanged)
                return new BoundCallExpression(node.Syntax, receiver, node.Method, arguments);

            return node;
        }

        private ImmutableArray<BoundStatement> RewriteScopedStatements(
            ImmutableArray<BoundStatement> statements,
            out bool changed)
        {
            changed = false;
            if (statements.IsDefaultOrEmpty)
                return statements;

            var localFunctions = CollectCaptureInfos(statements);
            _localFunctionScopes.Add(localFunctions);

            try
            {
                var builder = ImmutableArray.CreateBuilder<BoundStatement>(statements.Length);
                for (int i = 0; i < statements.Length; i++)
                {
                    var statement = statements[i];
                    var rewritten = RewriteStatement(statement);
                    if (!ReferenceEquals(rewritten, statement))
                        changed = true;
                    builder.Add(rewritten);
                }

                return changed ? builder.ToImmutable() : statements;
            }
            finally
            {
                _localFunctionScopes.RemoveAt(_localFunctionScopes.Count - 1);
            }
        }

        private Dictionary<LocalFunctionSymbol, CaptureInfo> CollectCaptureInfos(ImmutableArray<BoundStatement> statements)
        {
            var map = new Dictionary<LocalFunctionSymbol, CaptureInfo>(ReferenceEqualityComparer<LocalFunctionSymbol>.Instance);

            for (int i = 0; i < statements.Length; i++)
            {
                if (statements[i] is not BoundLocalFunctionStatement localFunction)
                    continue;

                map[localFunction.LocalFunction] = CreateCaptureInfo(localFunction);
            }

            return map;
        }

        private ImmutableArray<Symbol> CollectRequiredCaptures(MethodSymbol owner, BoundStatement body)
        {
            var captures = ImmutableArray.CreateBuilder<Symbol>();
            var seen = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

            void Add(Symbol symbol)
            {
                if (seen.Add(symbol))
                    captures.Add(symbol);
            }

            var directCaptures = CaptureCollector.Collect(owner, body);
            for (int i = 0; i < directCaptures.Length; i++)
                Add(directCaptures[i]);

            var nestedLocalFunctions = NestedLocalFunctionCollector.Collect(body);
            for (int i = 0; i < nestedLocalFunctions.Length; i++)
            {
                var nested = nestedLocalFunctions[i];
                var nestedCaptures = CollectRequiredCaptures(nested.LocalFunction, nested.Body);
                for (int j = 0; j < nestedCaptures.Length; j++)
                {
                    var symbol = nestedCaptures[j];
                    if (!ReferenceEquals(symbol.ContainingSymbol, owner))
                        Add(symbol);
                }
            }

            return captures.ToImmutable();
        }

        private CaptureInfo CreateCaptureInfo(BoundLocalFunctionStatement statement)
        {
            var original = statement.LocalFunction;
            var capturedSymbols = CollectRequiredCaptures(original, statement.Body);

            if (capturedSymbols.IsDefaultOrEmpty)
                return new CaptureInfo(original, original, capturedSymbols, ImmutableArray<ParameterSymbol>.Empty);

            var declaration = original.Declaration;
            var tree = original.DeclaringSyntaxReferences.Length != 0
                ? original.DeclaringSyntaxReferences[0].SyntaxTree
                : throw new InvalidOperationException("Local function syntax reference is missing.");

            var lowered = new LocalFunctionSymbol(
                original.Name,
                original.ContainingSymbol ?? throw new InvalidOperationException("Local function containing symbol is missing."),
                declaration,
                tree,
                original.Locations,
                original.IsStatic,
                original.IsAsync);

            var allParameters = ImmutableArray.CreateBuilder<ParameterSymbol>(original.Parameters.Length + capturedSymbols.Length);
            allParameters.AddRange(original.Parameters);

            var hiddenParameters = ImmutableArray.CreateBuilder<ParameterSymbol>(capturedSymbols.Length);
            for (int i = 0; i < capturedSymbols.Length; i++)
            {
                var symbol = capturedSymbols[i];
                var hiddenType = GetHiddenCaptureParameterType(symbol);
                var hiddenParameter = new ParameterSymbol(
                    name: $"<>capture{i}_{symbol.Name}",
                    containing: lowered,
                    type: hiddenType,
                    locations: ImmutableArray<Location>.Empty);

                allParameters.Add(hiddenParameter);
                hiddenParameters.Add(hiddenParameter);
            }

            lowered.SetSignature(original.ReturnType, allParameters.ToImmutable());

            return new CaptureInfo(
                original,
                lowered,
                capturedSymbols,
                hiddenParameters.ToImmutable());
        }

        private TypeSymbol GetHiddenCaptureParameterType(Symbol symbol)
        {
            return symbol switch
            {
                LocalSymbol local => _compilation.CreateByRefType(local.Type),
                ParameterSymbol parameter when parameter.Type is ByRefTypeSymbol => parameter.Type,
                ParameterSymbol parameter => _compilation.CreateByRefType(parameter.Type),
                _ => throw new NotSupportedException($"Unsupported captured symbol '{symbol.Kind}'.")
            };
        }

        private BoundExpression MakeCurrentCaptureValueExpression(SyntaxNode syntax, Symbol capturedSymbol)
        {
            if (_captureParameterMap.TryGetValue(capturedSymbol, out var parameter))
                return new BoundParameterExpression(syntax, parameter);

            return capturedSymbol switch
            {
                LocalSymbol local => new BoundLocalExpression(syntax, local),
                ParameterSymbol originalParameter => new BoundParameterExpression(syntax, originalParameter),
                _ => throw new NotSupportedException($"Unsupported captured symbol '{capturedSymbol.Kind}'.")
            };
        }

        private SavedCaptureParameter[] PushCaptureParameters(SyntaxNode syntax, CaptureInfo info)
        {
            if (info.HiddenParameters.IsDefaultOrEmpty)
                return Array.Empty<SavedCaptureParameter>();

            var saved = new SavedCaptureParameter[info.HiddenParameters.Length];
            for (int i = 0; i < info.HiddenParameters.Length; i++)
            {
                var symbol = info.CapturedSymbols[i];
                bool hadOldValue = _captureParameterMap.TryGetValue(symbol, out var oldValue);
                saved[i] = new SavedCaptureParameter(symbol, hadOldValue, oldValue!);
                _captureParameterMap[symbol] = info.HiddenParameters[i];
            }

            return saved;
        }

        private void PopCaptureParameters(SavedCaptureParameter[] saved)
        {
            for (int i = saved.Length - 1; i >= 0; i--)
            {
                var item = saved[i];
                if (item.HadOldValue)
                    _captureParameterMap[item.Symbol] = item.OldValue;
                else
                    _captureParameterMap.Remove(item.Symbol);
            }
        }

        private bool TryGetCaptureInfo(LocalFunctionSymbol symbol, out CaptureInfo info)
        {
            for (int i = _localFunctionScopes.Count - 1; i >= 0; i--)
            {
                if (_localFunctionScopes[i].TryGetValue(symbol, out info))
                    return true;
            }

            info = default;
            return false;
        }
    }


    internal static class IRLowering
    {
        public static BoundMethodBody Rewrite(
            Compilation compilation,
            BoundMethodBody methodBody)
        {
            if (compilation is null) throw new ArgumentNullException(nameof(compilation));
            if (methodBody is null) throw new ArgumentNullException(nameof(methodBody));

            var closureRewriter = new LocalFunctionClosureRewriter(compilation);
            var closureLoweredBody = closureRewriter.RewriteNode(methodBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected closure conversion result.");

            var rewriter = new LocalRewriter(compilation, closureLoweredBody.Method);
            var loweredBody = rewriter.RewriteNode(closureLoweredBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected rewrite result.");

            loweredBody = EnsureBlockBody(loweredBody);

            var cleaner = new CleanupRewriter();
            loweredBody = cleaner.RewriteNode(loweredBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected cleanup result.");

            loweredBody = EnsureBlockBody(loweredBody);

            var localOptimizer = new LocalFlowOptimizer();
            loweredBody = localOptimizer.RewriteNode(loweredBody) as BoundMethodBody
                ?? throw new InvalidOperationException("Unexpected local optimization result.");

            loweredBody = EnsureBlockBody(loweredBody);

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
            private NamespaceSymbol? _systemNsCache;
            private readonly Dictionary<int, NamedTypeSymbol> _valueTupleDefCache = new();
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
            private NamespaceSymbol GetSystemNamespaceOrThrow()
            {
                if (_systemNsCache != null) return _systemNsCache;

                var g = _compilation.GlobalNamespace;
                var nss = g.GetNamespaceMembers();
                for (int i = 0; i < nss.Length; i++)
                {
                    if (string.Equals(nss[i].Name, "System", StringComparison.Ordinal))
                        return _systemNsCache = nss[i];
                }

                throw new InvalidOperationException("Tuples require namespace 'System' with ValueTuple definitions.");
            }
            private LabelSymbol GenerateLabel(string debugName)
                => LabelSymbol.CreateGenerated($"<{debugName}_{_labelId++}>", _method);

            private LocalSymbol CreateTempLocal(TypeSymbol type)
                => new LocalSymbol($"$temp{_tempId++}", _method, type, ImmutableArray<Location>.Empty);

            private LocalSymbol CreateRefTempLocal(TypeSymbol type)
                => new LocalSymbol($"$temp{_tempId++}", _method, type, ImmutableArray<Location>.Empty, isByRef: true);
            private NamedTypeSymbol GetValueTupleDef(int arity)
            {
                if (_valueTupleDefCache.TryGetValue(arity, out var t))
                    return t;

                var sys = GetSystemNamespaceOrThrow();
                var cands = sys.GetTypeMembers("ValueTuple", arity);
                if (cands.IsDefaultOrEmpty)
                    throw new InvalidOperationException($"Tuples require 'System.ValueTuple' with arity {arity}.");

                t = cands[0];
                _valueTupleDefCache[arity] = t;
                return t;
            }

            private TypeSymbol MapTupleElementType(TypeSymbol t)
            {
                if (t is TupleTypeSymbol tt)
                    return GetValueTupleTypeForElements(tt.ElementTypes);
                return t;
            }
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
            private NamedTypeSymbol GetValueTupleTypeForElements(ImmutableArray<TypeSymbol> elems)
            {
                if (elems.Length == 0)
                    return GetValueTupleDef(0);

                if (elems.Length <= 7)
                {
                    var def = GetValueTupleDef(elems.Length);
                    var b = ImmutableArray.CreateBuilder<TypeSymbol>(elems.Length);
                    for (int i = 0; i < elems.Length; i++)
                        b.Add(MapTupleElementType(elems[i]));
                    return _compilation.ConstructNamedType(def, b.ToImmutable());
                }
                else
                {
                    var def8 = GetValueTupleDef(8);

                    var b = ImmutableArray.CreateBuilder<TypeSymbol>(8);
                    for (int i = 0; i < 7; i++)
                        b.Add(MapTupleElementType(elems[i]));

                    var restElems = SliceTypes(elems, 7, elems.Length - 7);
                    var restType = GetValueTupleTypeForElements(restElems);
                    b.Add(restType);

                    return _compilation.ConstructNamedType(def8, b.ToImmutable());
                }
            }

            private static ImmutableArray<TypeSymbol> SliceTypes(ImmutableArray<TypeSymbol> src, int start, int count)
            {
                var b = ImmutableArray.CreateBuilder<TypeSymbol>(count);
                for (int i = 0; i < count; i++)
                    b.Add(src[start + i]);
                return b.ToImmutable();
            }

            private static ImmutableArray<BoundExpression> SliceExprs(ImmutableArray<BoundExpression> src, int start, int count)
            {
                var b = ImmutableArray.CreateBuilder<BoundExpression>(count);
                for (int i = 0; i < count; i++)
                    b.Add(src[start + i]);
                return b.ToImmutable();
            }
            private BoundExpression CreateValueTupleValue(
                SyntaxNode syntax,
                ImmutableArray<TypeSymbol> tupleElemTypes,
                ImmutableArray<BoundExpression> values)
            {
                if (tupleElemTypes.Length != values.Length)
                    throw new InvalidOperationException("Tuple element type/value length mismatch.");

                int n = tupleElemTypes.Length;

                // () => default(ValueTuple)
                if (n == 0)
                {
                    var vt0 = GetValueTupleDef(0);
                    return new BoundObjectCreationExpression(syntax, vt0, constructorOpt: null, ImmutableArray<BoundExpression>.Empty);
                }

                if (n <= 7)
                {
                    var vt = GetValueTupleTypeForElements(tupleElemTypes);
                    var ctor = FindStructCtorByParamCount(vt, n);
                    return new BoundObjectCreationExpression(syntax, vt, ctor, values);
                }
                else
                {
                    // Build Rest recursively
                    var firstTypes = SliceTypes(tupleElemTypes, 0, 7);
                    var restTypes = SliceTypes(tupleElemTypes, 7, n - 7);

                    var firstVals = SliceExprs(values, 0, 7);
                    var restVals = SliceExprs(values, 7, n - 7);

                    var restExpr = CreateValueTupleValue(syntax, restTypes, restVals);

                    var vt8 = GetValueTupleTypeForElements(tupleElemTypes); // ValueTuple`8<...>
                    var ctor8 = FindStructCtorByParamCount(vt8, 8);

                    var args = ImmutableArray.CreateBuilder<BoundExpression>(8);
                    args.AddRange(firstVals);
                    args.Add(restExpr);

                    return new BoundObjectCreationExpression(syntax, vt8, ctor8, args.ToImmutable());
                }
            }
            private BoundExpression ReadValueTupleElement(
                ExpressionSyntax syntax,
                BoundExpression receiver,
                NamedTypeSymbol receiverVtType,
                TupleTypeSymbol semanticTuple,
                int elementIndex)
            {
                BoundExpression cur = receiver;
                NamedTypeSymbol curType = receiverVtType;

                int i = elementIndex;

                while (curType.Arity == 8 && i >= 7)
                {
                    var restField = GetFieldOrThrow(curType, "Rest");
                    cur = new BoundMemberAccessExpression(
                        syntax, cur, restField, restField.Type,
                        isLValue: (cur.IsLValue || cur is BoundThisExpression));
                    curType = (NamedTypeSymbol)restField.Type;
                    i -= 7;
                }

                string itemName = "Item" + (i + 1).ToString();
                var itemField = GetFieldOrThrow(curType, itemName);

                var viewType = semanticTuple.ElementTypes[elementIndex];
                return new BoundMemberAccessExpression(
                    syntax, cur, itemField, viewType,
                    isLValue: (cur.IsLValue || cur is BoundThisExpression));
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
            protected override BoundExpression RewriteTupleExpression(BoundTupleExpression node)
            {
                // Rewrite elements first
                var elems = RewriteExpressions(node.Elements, out _);

                var tupleType = (TupleTypeSymbol)node.Type;
                return CreateValueTupleValue(node.Syntax, tupleType.ElementTypes, elems);
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

                // if (cond) { }  => evaluate cond for side effects only
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

            protected override BoundStatement RewriteForEachStatement(BoundForEachStatement node)
            {
                var collection = RewriteExpression(node.Collection);
                var body = RewriteStatement(node.Body);

                return node.EnumeratorKind switch
                {
                    BoundForEachEnumeratorKind.Array => LowerArrayForEach(node, collection, body),
                    BoundForEachEnumeratorKind.String => LowerStringForEach(node, collection, body),
                    BoundForEachEnumeratorKind.Pattern or BoundForEachEnumeratorKind.Interface => LowerEnumeratorForEach(node, collection, body),
                    _ => throw new NotSupportedException($"Unexpected foreach enumerator kind: {node.EnumeratorKind}")
                };
            }

            private BoundStatement LowerArrayForEach(
                BoundForEachStatement node,
                BoundExpression collection,
                BoundStatement body)
            {
                if (collection.Type is not ArrayTypeSymbol arrayType || arrayType.Rank != 1)
                    throw new NotSupportedException("foreach lowering for multidimensional arrays is not implemented.");

                var intType = _compilation.GetSpecialType(SpecialType.System_Int32);
                var boolType = _compilation.GetSpecialType(SpecialType.System_Boolean);

                var collectionTemp = CreateTempLocal(collection.Type);
                var indexTemp = CreateTempLocal(intType);

                var collectionExpr = new BoundLocalExpression(node.Syntax, collectionTemp);
                var indexExpr = new BoundLocalExpression(node.Syntax, indexTemp);
                var zero = new BoundLiteralExpression(node.Syntax, intType, 0);
                var one = new BoundLiteralExpression(node.Syntax, intType, 1);

                var lengthExpr = RewriteExpression(MakeArrayLengthRead(node.Syntax, collectionExpr));
                var condition = new BoundBinaryExpression(
                    node.Syntax,
                    BoundBinaryOperatorKind.LessThan,
                    boolType,
                    indexExpr,
                    lengthExpr,
                    Optional<object>.None);

                BoundExpression current = new BoundArrayElementAccessExpression(
                    node.Syntax,
                    node.ElementType,
                    collectionExpr,
                    indexExpr);
                current = ApplyConversionIfNeeded(node.Syntax, current, node.IterationVariable.Type, node.IterationConversion);
                current = RewriteExpression(current);

                var increment = new BoundAssignmentExpression(
                    node.Syntax,
                    indexExpr,
                    new BoundBinaryExpression(
                        node.Syntax,
                        BoundBinaryOperatorKind.Add,
                        intType,
                        indexExpr,
                        one,
                        Optional<object>.None,
                        isChecked: GetEffectiveIsChecked(false)));

                var builder = ImmutableArray.CreateBuilder<BoundStatement>();
                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, collectionTemp, collection));
                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, indexTemp, zero));
                builder.Add(new BoundLabelStatement(node.Syntax, GenerateLabel("foreach_array_check")));

                var checkLabel = ((BoundLabelStatement)builder[2]).Label;
                var branch = MakeConditionalGoto(node.Syntax, condition, node.BreakLabel, jumpIfTrue: false);
                if (branch is not BoundEmptyStatement)
                    builder.Add(branch);

                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, node.IterationVariable, current));
                builder.Add(body);
                builder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                builder.Add(new BoundExpressionStatement(node.Syntax, RewriteExpression(increment)));
                builder.Add(new BoundGotoStatement(node.Syntax, checkLabel));
                builder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }
            private BoundStatement LowerStringForEach(
                BoundForEachStatement node,
                BoundExpression collection,
                BoundStatement body)
            {
                if (collection.Type.SpecialType != SpecialType.System_String)
                    throw new InvalidOperationException("Expected string expression for foreach string lowering.");

                var intType = _compilation.GetSpecialType(SpecialType.System_Int32);
                var boolType = _compilation.GetSpecialType(SpecialType.System_Boolean);

                var collectionTemp = CreateTempLocal(collection.Type);
                var indexTemp = CreateTempLocal(intType);

                var collectionExpr = new BoundLocalExpression(node.Syntax, collectionTemp);
                var indexExpr = new BoundLocalExpression(node.Syntax, indexTemp);
                var zero = new BoundLiteralExpression(node.Syntax, intType, 0);
                var one = new BoundLiteralExpression(node.Syntax, intType, 1);

                var lengthExpr = RewriteExpression(MakeStringLengthRead(node.Syntax, collectionExpr));
                var condition = new BoundBinaryExpression(
                    node.Syntax,
                    BoundBinaryOperatorKind.LessThan,
                    boolType,
                    indexExpr,
                    lengthExpr,
                    Optional<object>.None);

                BoundExpression current = MakeStringCharRead(node.Syntax, collectionExpr, indexExpr);
                current = ApplyConversionIfNeeded(node.Syntax, current, node.IterationVariable.Type, node.IterationConversion);
                current = RewriteExpression(current);

                var increment = new BoundAssignmentExpression(
                    node.Syntax,
                    indexExpr,
                    new BoundBinaryExpression(
                        node.Syntax,
                        BoundBinaryOperatorKind.Add,
                        intType,
                        indexExpr,
                        one,
                        Optional<object>.None,
                        isChecked: GetEffectiveIsChecked(false)));

                var builder = ImmutableArray.CreateBuilder<BoundStatement>();
                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, collectionTemp, collection));
                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, indexTemp, zero));
                builder.Add(new BoundLabelStatement(node.Syntax, GenerateLabel("foreach_string_check")));

                var checkLabel = ((BoundLabelStatement)builder[2]).Label;
                var branch = MakeConditionalGoto(node.Syntax, condition, node.BreakLabel, jumpIfTrue: false);
                if (branch is not BoundEmptyStatement)
                    builder.Add(branch);

                builder.Add(new BoundLocalDeclarationStatement(node.Syntax, node.IterationVariable, current));
                builder.Add(body);
                builder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));
                builder.Add(new BoundExpressionStatement(node.Syntax, RewriteExpression(increment)));
                builder.Add(new BoundGotoStatement(node.Syntax, checkLabel));
                builder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                return new BoundBlockStatement(node.Syntax, builder.ToImmutable());
            }
            private BoundStatement LowerEnumeratorForEach(
                BoundForEachStatement node,
                BoundExpression collection,
                BoundStatement body)
            {
                var getEnumerator = node.GetEnumeratorMethodOpt
                    ?? throw new InvalidOperationException("foreach enumerator GetEnumerator method was not captured by binder.");
                var currentProperty = node.CurrentPropertyOpt
                    ?? throw new InvalidOperationException("foreach enumerator Current property was not captured by binder.");
                var moveNextMethod = node.MoveNextMethodOpt
                    ?? throw new InvalidOperationException("foreach enumerator MoveNext method was not captured by binder.");

                var collectionTemp = CreateTempLocal(collection.Type);
                var enumeratorTemp = CreateTempLocal(node.EnumeratorType);

                var collectionExpr = new BoundLocalExpression(node.Syntax, collectionTemp);
                var enumeratorExpr = new BoundLocalExpression(node.Syntax, enumeratorTemp);

                BoundExpression receiver = ApplyConversionIfNeeded(node.Syntax, collectionExpr, node.CollectionType, node.CollectionConversion);
                receiver = RewriteExpression(receiver);

                BoundExpression getEnumeratorCall;
                if (node.GetEnumeratorIsExtensionMethod)
                {
                    getEnumeratorCall = new BoundCallExpression(
                        node.Syntax,
                        receiverOpt: null,
                        method: getEnumerator,
                        arguments: ImmutableArray.Create(receiver));
                }
                else
                {
                    getEnumeratorCall = new BoundCallExpression(
                        node.Syntax,
                        receiverOpt: receiver,
                        method: getEnumerator,
                        arguments: ImmutableArray<BoundExpression>.Empty);
                }
                getEnumeratorCall = RewriteExpression(getEnumeratorCall);

                var moveNextCall = RewriteExpression(new BoundCallExpression(
                    node.Syntax,
                    enumeratorExpr,
                    moveNextMethod,
                    ImmutableArray<BoundExpression>.Empty));

                BoundExpression currentRead;
                if (currentProperty.GetMethod is MethodSymbol currentGetter)
                {
                    currentRead = new BoundCallExpression(
                        node.Syntax,
                        enumeratorExpr,
                        currentGetter,
                        ImmutableArray<BoundExpression>.Empty);
                }
                else
                {
                    throw new InvalidOperationException("foreach enumerator Current getter was not captured by binder.");
                }

                currentRead = ApplyConversionIfNeeded(node.Syntax, currentRead, node.IterationVariable.Type, node.IterationConversion);
                currentRead = RewriteExpression(currentRead);

                var loopBuilder = ImmutableArray.CreateBuilder<BoundStatement>();
                loopBuilder.Add(new BoundLabelStatement(node.Syntax, node.ContinueLabel));

                var branch = MakeConditionalGoto(node.Syntax, moveNextCall, node.BreakLabel, jumpIfTrue: false);
                if (branch is not BoundEmptyStatement)
                    loopBuilder.Add(branch);

                loopBuilder.Add(new BoundLocalDeclarationStatement(node.Syntax, node.IterationVariable, currentRead));
                loopBuilder.Add(body);
                loopBuilder.Add(new BoundGotoStatement(node.Syntax, node.ContinueLabel));
                loopBuilder.Add(new BoundLabelStatement(node.Syntax, node.BreakLabel));

                BoundStatement loopBody = new BoundBlockStatement(node.Syntax, loopBuilder.ToImmutable());
                if (TryCreateForEachFinally(node.Syntax, enumeratorTemp, out var finallyBlock))
                {
                    loopBody = new BoundTryStatement(
                        CreateSyntheticTryStatementSyntax(),
                        new BoundBlockStatement(node.Syntax, ImmutableArray.Create(loopBody)),
                        ImmutableArray<BoundCatchBlock>.Empty,
                        finallyBlock);
                }

                return new BoundBlockStatement(
                    node.Syntax,
                    ImmutableArray.Create<BoundStatement>(
                        new BoundLocalDeclarationStatement(node.Syntax, collectionTemp, collection),
                        new BoundLocalDeclarationStatement(node.Syntax, enumeratorTemp, getEnumeratorCall),
                        loopBody));
            }

            private BoundExpression ApplyConversionIfNeeded(
                SyntaxNode syntax,
                BoundExpression expression,
                TypeSymbol targetType,
                Conversion conversion)
            {
                if (!conversion.Exists)
                    throw new InvalidOperationException($"Missing conversion from '{expression.Type.Name}' to '{targetType.Name}'.");

                if (conversion.Kind == ConversionKind.Identity && ReferenceEquals(expression.Type, targetType))
                    return expression;

                return new BoundConversionExpression(
                    syntax,
                    targetType,
                    expression,
                    conversion,
                    isChecked: GetEffectiveIsChecked(false));
            }

            private BoundExpression MakeArrayLengthRead(SyntaxNode syntax, BoundExpression arrayExpr)
            {
                if (arrayExpr.Type is not ArrayTypeSymbol arrayType || arrayType.BaseType is not NamedTypeSymbol arrayBase)
                    throw new InvalidOperationException("Expected array expression for Length lowering.");

                var members = arrayBase.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol prop &&
                        string.Equals(prop.Name, "Length", StringComparison.Ordinal) &&
                        prop.GetMethod is MethodSymbol getter)
                    {
                        return new BoundCallExpression(syntax, arrayExpr, getter, ImmutableArray<BoundExpression>.Empty);
                    }
                }

                throw new InvalidOperationException("System.Array.Length getter not found.");
            }
            private BoundExpression MakeStringLengthRead(SyntaxNode syntax, BoundExpression stringExpr)
            {
                if (stringExpr.Type.SpecialType != SpecialType.System_String)
                    throw new InvalidOperationException("Expected string expression for Length lowering.");

                var stringType = _compilation.GetSpecialType(SpecialType.System_String);
                var intType = _compilation.GetSpecialType(SpecialType.System_Int32);

                var members = stringType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol prop &&
                        !prop.IsStatic &&
                        string.Equals(prop.Name, "Length", StringComparison.Ordinal) &&
                        ReferenceEquals(prop.Type, intType) &&
                        prop.GetMethod is MethodSymbol getter)
                    {
                        return new BoundCallExpression(syntax, stringExpr, getter, ImmutableArray<BoundExpression>.Empty);
                    }
                }

                throw new InvalidOperationException("System.String.Length getter not found.");
            }
            private BoundExpression MakeStringCharRead(SyntaxNode syntax, BoundExpression stringExpr, BoundExpression indexExpr)
            {
                if (stringExpr.Type.SpecialType != SpecialType.System_String)
                    throw new InvalidOperationException("Expected string expression for indexer lowering.");

                var stringType = _compilation.GetSpecialType(SpecialType.System_String);
                var intType = _compilation.GetSpecialType(SpecialType.System_Int32);
                var charType = _compilation.GetSpecialType(SpecialType.System_Char);

                var members = stringType.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is PropertySymbol prop &&
                        !prop.IsStatic &&
                        ReferenceEquals(prop.Type, charType) &&
                        prop.Parameters.Length == 1 &&
                        ReferenceEquals(prop.Parameters[0].Type, intType) &&
                        prop.GetMethod is MethodSymbol getter)
                    {
                        return new BoundCallExpression(
                            syntax,
                            stringExpr,
                            getter,
                            ImmutableArray.Create(indexExpr));
                    }
                }

                throw new InvalidOperationException("System.String indexer getter not found.");
            }
            private bool TryCreateForEachFinally(
                SyntaxNode syntax,
                LocalSymbol enumeratorLocal,
                out BoundBlockStatement finallyBlock)
            {
                var enumeratorExpr = new BoundLocalExpression(syntax, enumeratorLocal);
                var enumeratorType = enumeratorLocal.Type;
                var nullLiteral = new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null);

                var directDispose = FindAccessibleDisposeMethod(enumeratorType);
                if (directDispose is not null)
                {
                    var call = RewriteExpression(new BoundCallExpression(
                        syntax,
                        enumeratorExpr,
                        directDispose,
                        ImmutableArray<BoundExpression>.Empty));

                    if (enumeratorType.IsValueType)
                    {
                        finallyBlock = new BoundBlockStatement(
                            syntax,
                            ImmutableArray.Create<BoundStatement>(new BoundExpressionStatement(syntax, call)));
                        return true;
                    }

                    var notNull = new BoundBinaryExpression(
                        syntax,
                        BoundBinaryOperatorKind.NotEquals,
                        _compilation.GetSpecialType(SpecialType.System_Boolean),
                        enumeratorExpr,
                        nullLiteral,
                        Optional<object>.None);

                    var endLabel = LabelSymbol.CreateGenerated("<foreach_dispose_end>", _method);
                    var statements = ImmutableArray.CreateBuilder<BoundStatement>(3);
                    var skip = MakeConditionalGoto(syntax, notNull, endLabel, jumpIfTrue: false);
                    if (skip is not BoundEmptyStatement)
                        statements.Add(skip);
                    statements.Add(new BoundExpressionStatement(syntax, call));
                    statements.Add(new BoundLabelStatement(syntax, endLabel));

                    finallyBlock = new BoundBlockStatement(syntax, statements.ToImmutable());
                    return true;
                }

                var disposableType = GetWellKnownType(_compilation, new[] { "System" }, "IDisposable", 0);
                if (disposableType is null)
                {
                    finallyBlock = null!;
                    return false;
                }

                var disposeMethod = FindParameterlessInstanceMethod(disposableType, "Dispose", _compilation.GetSpecialType(SpecialType.System_Void));
                if (disposeMethod is null)
                {
                    finallyBlock = null!;
                    return false;
                }

                var conv = LocalScopeBinder.ClassifyConversion(enumeratorExpr, disposableType);
                if (!conv.Exists || !conv.IsImplicit)
                {
                    finallyBlock = null!;
                    return false;
                }

                if (enumeratorType.IsValueType)
                {
                    var boxed = RewriteExpression(new BoundConversionExpression(
                        syntax,
                        disposableType,
                        enumeratorExpr,
                        conv,
                        isChecked: false));
                    var call = RewriteExpression(new BoundCallExpression(
                        syntax,
                        boxed,
                        disposeMethod,
                        ImmutableArray<BoundExpression>.Empty));

                    finallyBlock = new BoundBlockStatement(
                        syntax,
                        ImmutableArray.Create<BoundStatement>(new BoundExpressionStatement(syntax, call)));
                    return true;
                }

                {
                    var disposableTemp = CreateTempLocal(disposableType);
                    var disposableExpr = new BoundLocalExpression(syntax, disposableTemp);
                    var notNull = new BoundBinaryExpression(
                        syntax,
                        BoundBinaryOperatorKind.NotEquals,
                        _compilation.GetSpecialType(SpecialType.System_Boolean),
                        disposableExpr,
                        nullLiteral,
                        Optional<object>.None);

                    var asExpr = RewriteExpression(new BoundAsExpression(syntax, disposableType, enumeratorExpr, conv));
                    var disposeCall = RewriteExpression(new BoundCallExpression(
                        syntax,
                        disposableExpr,
                        disposeMethod,
                        ImmutableArray<BoundExpression>.Empty));

                    var endLabel = LabelSymbol.CreateGenerated("<foreach_dispose_end>", _method);
                    var statements = ImmutableArray.CreateBuilder<BoundStatement>();
                    statements.Add(new BoundLocalDeclarationStatement(syntax, disposableTemp, asExpr));
                    var skip = MakeConditionalGoto(syntax, notNull, endLabel, jumpIfTrue: false);
                    if (skip is not BoundEmptyStatement)
                        statements.Add(skip);
                    statements.Add(new BoundExpressionStatement(syntax, disposeCall));
                    statements.Add(new BoundLabelStatement(syntax, endLabel));

                    finallyBlock = new BoundBlockStatement(syntax, statements.ToImmutable());
                    return true;
                }

            }

            private static MethodSymbol? FindAccessibleDisposeMethod(TypeSymbol type)
            {
                var method = FindParameterlessInstanceMethod(type, "Dispose", null);
                return method is not null && method.ReturnType.SpecialType == SpecialType.System_Void
                    ? method
                    : null;
            }

            private static MethodSymbol? FindParameterlessInstanceMethod(TypeSymbol type, string name, TypeSymbol? returnType)
            {
                var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer<TypeSymbol>.Instance);

                MethodSymbol? Visit(TypeSymbol current)
                {
                    if (!seen.Add(current))
                        return null;

                    if (current is not NamedTypeSymbol nt)
                        return null;

                    var members = nt.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is not MethodSymbol method)
                            continue;
                        if (method.IsStatic)
                            continue;
                        if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                            continue;
                        if (method.TypeParameters.Length != 0)
                            continue;
                        if (method.Parameters.Length != 0)
                            continue;
                        if (returnType is not null && !ReferenceEquals(method.ReturnType, returnType))
                            continue;
                        return method;
                    }

                    var interfaces = nt.Interfaces;
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        var m = Visit(interfaces[i]);
                        if (m is not null)
                            return m;
                    }

                    if (nt.BaseType is TypeSymbol baseType)
                        return Visit(baseType);

                    return null;
                }

                return Visit(type);
            }

            private static NamedTypeSymbol? GetWellKnownType(
                Compilation compilation,
                string[] namespaceParts,
                string typeName,
                int arity)
            {
                NamespaceSymbol current = compilation.GlobalNamespace;

                for (int p = 0; p < namespaceParts.Length; p++)
                {
                    var next = current.GetNamespaceMembers();
                    NamespaceSymbol? found = null;

                    for (int i = 0; i < next.Length; i++)
                    {
                        if (string.Equals(next[i].Name, namespaceParts[p], StringComparison.Ordinal))
                        {
                            found = next[i];
                            break;
                        }
                    }

                    if (found is null)
                        return null;

                    current = found;
                }

                var types = current.GetTypeMembers(typeName, arity);
                if (types.IsDefaultOrEmpty)
                    return null;

                return types[0].OriginalDefinition;
            }

            private static TryStatementSyntax CreateSyntheticTryStatementSyntax()
            {
                var emptyBlock = new BlockSyntax(default, SyntaxList<StatementSyntax>.Empty, default);
                var finallyClause = new FinallyClauseSyntax(default, emptyBlock);
                return new TryStatementSyntax(default, emptyBlock, SyntaxList<CatchClauseSyntax>.Empty, finallyClause);
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
            protected override BoundStatement RewriteFixedStatement(BoundFixedStatement node)
            {
                var body = RewriteStatement(node.Body);
                var statements = ImmutableArray.CreateBuilder<BoundStatement>(node.Declarations.Length * 3 + 1);

                for (int i = 0; i < node.Declarations.Length; i++)
                    LowerFixedDeclaration(node.Declarations[i], statements);

                statements.Add(body);
                return new BoundBlockStatement(node.Syntax, statements.ToImmutable());
            }
            private void LowerFixedDeclaration(BoundLocalDeclarationStatement decl, ImmutableArray<BoundStatement>.Builder statements)
            {
                if (decl.Initializer is not BoundFixedInitializerExpression fixedInit)
                {
                    statements.Add((BoundLocalDeclarationStatement)RewriteLocalDeclarationStatement(decl));
                    return;
                }

                fixedInit = (BoundFixedInitializerExpression)RewriteFixedInitializerExpression(fixedInit);

                var ptrType = (PointerTypeSymbol)decl.Local.Type;
                BoundExpression loweredInit = fixedInit.InitializerKind switch
                {
                    BoundFixedInitializerKind.Array => LowerFixedArrayInitializer(fixedInit.Syntax, ptrType, fixedInit, statements),
                    BoundFixedInitializerKind.String => LowerFixedStringInitializer(fixedInit.Syntax, ptrType, fixedInit, statements),
                    BoundFixedInitializerKind.GetPinnableReference => LowerFixedGetPinnableInitializer(fixedInit.Syntax, ptrType, fixedInit, statements),
                    BoundFixedInitializerKind.AddressOf => LowerFixedAddressOfInitializer(fixedInit.Syntax, ptrType, fixedInit, statements),
                    _ => throw new NotSupportedException($"Unexpected fixed initializer kind: {fixedInit.InitializerKind}")
                };

                statements.Add(new BoundLocalDeclarationStatement(decl.Syntax, decl.Local, loweredInit));
            }
            private BoundExpression LowerFixedArrayInitializer(
                SyntaxNode syntax,
                PointerTypeSymbol declaredPtrType,
                BoundFixedInitializerExpression fixedInit,
                ImmutableArray<BoundStatement>.Builder statements)
            {
                var arrTemp = CreateTempLocal(fixedInit.Expression.Type);
                var arrExpr = new BoundLocalExpression(syntax, arrTemp);

                statements.Add(new BoundLocalDeclarationStatement(syntax, arrTemp, fixedInit.Expression));

                var int32Type = _compilation.GetSpecialType(SpecialType.System_Int32);
                var zeroInt = new BoundLiteralExpression(syntax, int32Type, 0);
                var nullPtr = new BoundConversionExpression(
                    syntax: syntax,
                    type: declaredPtrType,
                    operand: new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                    conversion: new Conversion(ConversionKind.NullLiteral),
                    isChecked: false);

                var notNull = new BoundBinaryExpression(
                    syntax,
                    BoundBinaryOperatorKind.NotEquals,
                    _compilation.GetSpecialType(SpecialType.System_Boolean),
                    arrExpr,
                    new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                    Optional<object>.None);

                var len = MakeArrayLengthRead(syntax, arrExpr);
                var nonEmpty = new BoundBinaryExpression(
                    syntax,
                    BoundBinaryOperatorKind.NotEquals,
                    _compilation.GetSpecialType(SpecialType.System_Boolean),
                    len,
                    zeroInt,
                    Optional<object>.None);

                var firstElem = new BoundArrayElementAccessExpression(
                    syntax,
                    fixedInit.ElementType,
                    arrExpr,
                    zeroInt);

                var addrSyntax = new PrefixUnaryExpressionSyntax(
                    SyntaxKind.AddressOfExpression,
                    default,
                    (ExpressionSyntax)firstElem.Syntax);

                BoundExpression ptrValue = new BoundAddressOfExpression(
                    addrSyntax,
                    _compilation.CreatePointerType(fixedInit.ElementType),
                    firstElem);

                if (fixedInit.ElementPointerConversion.Kind != ConversionKind.Identity || !ReferenceEquals(ptrValue.Type, declaredPtrType))
                {
                    ptrValue = new BoundConversionExpression(syntax, declaredPtrType, ptrValue, fixedInit.ElementPointerConversion, isChecked: false);
                }

                // arr != null ? (arr.Length != 0 ? &arr[0] : (T*)null) : (T*)null
                var innerSyntax = new ConditionalExpressionSyntax(
                    (ExpressionSyntax)nonEmpty.Syntax, default, (ExpressionSyntax)ptrValue.Syntax, default, (ExpressionSyntax)nullPtr.Syntax);

                var inner = new BoundConditionalExpression(
                    innerSyntax,
                    declaredPtrType,
                    nonEmpty,
                    ptrValue,
                    nullPtr,
                    Optional<object>.None);

                var outerSyntax = new ConditionalExpressionSyntax(
                    (ExpressionSyntax)notNull.Syntax, default, (ExpressionSyntax)inner.Syntax, default, (ExpressionSyntax)nullPtr.Syntax);

                return new BoundConditionalExpression(
                    outerSyntax,
                    declaredPtrType,
                    notNull,
                    inner,
                    nullPtr,
                    Optional<object>.None);

                BoundExpression MakeArrayLengthRead(SyntaxNode syntax, BoundExpression arrayExpr)
                {
                    if (arrayExpr.Type is not ArrayTypeSymbol arrayType || arrayType.BaseType is not NamedTypeSymbol arrayBase)
                        throw new InvalidOperationException("Expected array expression in fixed initializer lowering.");

                    var members = arrayBase.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is PropertySymbol prop &&
                            string.Equals(prop.Name, "Length", StringComparison.Ordinal) &&
                            prop.GetMethod is MethodSymbol getter)
                        {
                            return new BoundCallExpression(syntax, arrayExpr, getter, ImmutableArray<BoundExpression>.Empty);
                        }
                    }

                    throw new InvalidOperationException("System.Array.Length getter not found.");
                }

            }
            private BoundExpression LowerFixedStringInitializer(
                SyntaxNode syntax,
                PointerTypeSymbol declaredPtrType,
                BoundFixedInitializerExpression fixedInit,
                ImmutableArray<BoundStatement>.Builder statements)
            {
                var strTemp = CreateTempLocal(fixedInit.Expression.Type);
                var strExpr = new BoundLocalExpression(syntax, strTemp);
                statements.Add(new BoundLocalDeclarationStatement(syntax, strTemp, fixedInit.Expression));

                var nullPtr = new BoundConversionExpression(
                    syntax: syntax,
                    type: declaredPtrType,
                    operand: new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                    conversion: new Conversion(ConversionKind.NullLiteral),
                    isChecked: false);

                var notNull = new BoundBinaryExpression(
                    syntax,
                    BoundBinaryOperatorKind.NotEquals,
                    _compilation.GetSpecialType(SpecialType.System_Boolean),
                    strExpr,
                    new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                    Optional<object>.None);

                var getPinnable = FindStringGetPinnableReference()
                    ?? throw new InvalidOperationException("System.String.GetPinnableReference not found.");

                var byrefCall = new BoundCallExpression(
                    syntax,
                    strExpr,
                    getPinnable,
                    ImmutableArray<BoundExpression>.Empty);

                var addrSyntax = new PrefixUnaryExpressionSyntax(
                    SyntaxKind.AddressOfExpression,
                    default,
                    (ExpressionSyntax)byrefCall.Syntax);

                BoundExpression ptrValue = new BoundAddressOfExpression(
                    addrSyntax,
                    _compilation.CreatePointerType(fixedInit.ElementType),
                    byrefCall);

                if (fixedInit.ElementPointerConversion.Kind != ConversionKind.Identity || !ReferenceEquals(ptrValue.Type, declaredPtrType))
                {
                    ptrValue = new BoundConversionExpression(syntax, declaredPtrType, ptrValue, fixedInit.ElementPointerConversion, isChecked: false);
                }

                var condSyntax = new ConditionalExpressionSyntax(
                    (ExpressionSyntax)notNull.Syntax, default, (ExpressionSyntax)ptrValue.Syntax, default, (ExpressionSyntax)nullPtr.Syntax);

                return new BoundConditionalExpression(
                    condSyntax,
                    declaredPtrType,
                    notNull,
                    ptrValue,
                    nullPtr,
                    Optional<object>.None);

                MethodSymbol? FindStringGetPinnableReference()
                {
                    var stringType = _compilation.GetSpecialType(SpecialType.System_String);
                    var charType = _compilation.GetSpecialType(SpecialType.System_Char);

                    var members = stringType.GetMembers();
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i] is MethodSymbol method &&
                            string.Equals(method.Name, "GetPinnableReference", StringComparison.Ordinal) &&
                            !method.IsStatic &&
                            method.TypeParameters.Length == 0 &&
                            method.Parameters.Length == 0 &&
                            method.ReturnType is ByRefTypeSymbol byRef &&
                            ReferenceEquals(byRef.ElementType, charType))
                        {
                            return method;
                        }
                    }

                    return null;
                }
            }
            private BoundExpression LowerFixedGetPinnableInitializer(
                SyntaxNode syntax,
                PointerTypeSymbol declaredPtrType,
                BoundFixedInitializerExpression fixedInit,
                ImmutableArray<BoundStatement>.Builder statements)
            {
                var method = fixedInit.GetPinnableReferenceMethodOpt
                    ?? throw new InvalidOperationException("GetPinnableReference method was not captured by binder.");

                var receiverTemp = CreateTempLocal(fixedInit.Expression.Type);
                var receiverExpr = new BoundLocalExpression(syntax, receiverTemp);
                statements.Add(new BoundLocalDeclarationStatement(syntax, receiverTemp, fixedInit.Expression));

                BoundExpression call;
                if (method.IsExtensionMethod)
                {
                    BoundExpression receiverArg = receiverExpr;

                    var firstParamType = method.Parameters[0].Type;
                    var receiverConv = LocalScopeBinder.ClassifyConversion(receiverArg, firstParamType);
                    if (!receiverConv.Exists || !receiverConv.IsImplicit)
                        throw new InvalidOperationException("Bound fixed extension receiver is no longer applicable.");

                    if (receiverConv.Kind != ConversionKind.Identity || !ReferenceEquals(receiverArg.Type, firstParamType))
                    {
                        receiverArg = new BoundConversionExpression(
                            syntax,
                            firstParamType,
                            receiverArg,
                            receiverConv,
                            isChecked: false);
                    }

                    call = new BoundCallExpression(
                        syntax,
                        receiverOpt: null,
                        method,
                        ImmutableArray.Create<BoundExpression>(receiverArg));
                }
                else
                {
                    call = new BoundCallExpression(
                        syntax,
                        receiverExpr,
                        method,
                        ImmutableArray<BoundExpression>.Empty);
                }

                var addrSyntax = new PrefixUnaryExpressionSyntax(
                    SyntaxKind.AddressOfExpression,
                    default,
                    (ExpressionSyntax)call.Syntax);

                BoundExpression ptrValue = new BoundAddressOfExpression(
                    addrSyntax,
                    _compilation.CreatePointerType(fixedInit.ElementType),
                    call);

                if (fixedInit.ElementPointerConversion.Kind != ConversionKind.Identity ||
                    !ReferenceEquals(ptrValue.Type, declaredPtrType))
                {
                    ptrValue = new BoundConversionExpression(
                        syntax,
                        declaredPtrType,
                        ptrValue,
                        fixedInit.ElementPointerConversion,
                        isChecked: false);
                }

                if (fixedInit.Expression.Type.IsReferenceType)
                {
                    var nullPtr = new BoundConversionExpression(
                        syntax: syntax,
                        type: declaredPtrType,
                        operand: new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                        conversion: new Conversion(ConversionKind.NullLiteral),
                        isChecked: false);

                    var notNull = new BoundBinaryExpression(
                        syntax,
                        BoundBinaryOperatorKind.NotEquals,
                        _compilation.GetSpecialType(SpecialType.System_Boolean),
                        receiverExpr,
                        new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null),
                        Optional<object>.None);

                    var condSyntax = new ConditionalExpressionSyntax(
                        (ExpressionSyntax)notNull.Syntax,
                        default,
                        (ExpressionSyntax)ptrValue.Syntax,
                        default,
                        (ExpressionSyntax)nullPtr.Syntax);

                    return new BoundConditionalExpression(
                        condSyntax,
                        declaredPtrType,
                        notNull,
                        ptrValue,
                        nullPtr,
                        Optional<object>.None);
                }

                return ptrValue;
            }
            private BoundExpression LowerFixedAddressOfInitializer(
                SyntaxNode syntax,
                PointerTypeSymbol declaredPtrType,
                BoundFixedInitializerExpression fixedInit,
                ImmutableArray<BoundStatement>.Builder statements)
            {
                BoundExpression ptrValue = fixedInit.Expression;

                if (fixedInit.ElementPointerConversion.Kind != ConversionKind.Identity || !ReferenceEquals(ptrValue.Type, declaredPtrType))
                    ptrValue = new BoundConversionExpression(
                        syntax,
                        declaredPtrType,
                        ptrValue,
                        fixedInit.ElementPointerConversion,
                        isChecked: false);

                return ptrValue;
            }

            protected override BoundExpression RewriteConversionExpression(BoundConversionExpression node)
            {
                var effectiveChecked = GetEffectiveIsChecked(node.IsChecked);

                // Lower tuple conversions
                if ((node.Conversion.Kind == ConversionKind.ImplicitTuple || node.Conversion.Kind == ConversionKind.ExplicitTuple) &&
                    node.Type is TupleTypeSymbol toTuple &&
                    node.Operand.Type is TupleTypeSymbol fromTuple)
                {
                    int n = toTuple.ElementTypes.Length;
                    if (n != fromTuple.ElementTypes.Length)
                        throw new InvalidOperationException("Tuple conversion arity mismatch.");

                    var exprSyntax = node.Syntax as ExpressionSyntax
                        ?? node.Operand.Syntax as ExpressionSyntax
                        ?? throw new InvalidOperationException("Expected ExpressionSyntax for tuple conversion.");

                    // Fast path
                    if (node.Operand is BoundTupleExpression lit)
                    {
                        var converted = ImmutableArray.CreateBuilder<BoundExpression>(n);

                        for (int i = 0; i < n; i++)
                        {
                            var src = RewriteExpression(lit.Elements[i]);
                            var targetType = toTuple.ElementTypes[i];

                            var conv = LocalScopeBinder.ClassifyConversion(src, targetType);
                            if (!conv.Exists)
                                throw new InvalidOperationException($"No element conversion for tuple element {i}.");

                            BoundExpression e = conv.Kind == ConversionKind.Identity
                                ? src
                                : new BoundConversionExpression(exprSyntax, targetType, src, conv, effectiveChecked);

                            // allow nested tuple conversions to lower
                            e = RewriteExpression(e);
                            converted.Add(e);
                        }

                        return CreateValueTupleValue(node.Syntax, toTuple.ElementTypes, converted.ToImmutable());
                    }

                    // General path
                    var operandLowered = RewriteExpression(node.Operand);

                    var srcVtType = GetValueTupleTypeForElements(fromTuple.ElementTypes);
                    var tmp = CreateTempLocal(srcVtType);
                    var tmpExpr = new BoundLocalExpression(exprSyntax, tmp);

                    var assign = new BoundAssignmentExpression(exprSyntax, tmpExpr, operandLowered);
                    var side = new BoundExpressionStatement(exprSyntax, assign);

                    var converted2 = ImmutableArray.CreateBuilder<BoundExpression>(n);

                    for (int i = 0; i < n; i++)
                    {
                        var read = ReadValueTupleElement(exprSyntax, tmpExpr, srcVtType, fromTuple, i);
                        var targetType = toTuple.ElementTypes[i];

                        var conv = LocalScopeBinder.ClassifyConversion(read, targetType);
                        if (!conv.Exists)
                            throw new InvalidOperationException($"No element conversion for tuple element {i}.");

                        BoundExpression e = conv.Kind == ConversionKind.Identity
                            ? read
                            : new BoundConversionExpression(exprSyntax, targetType, read, conv, effectiveChecked);

                        e = RewriteExpression(e);
                        converted2.Add(e);
                    }

                    var value = CreateValueTupleValue(node.Syntax, toTuple.ElementTypes, converted2.ToImmutable());

                    return new BoundSequenceExpression(
                        syntax: exprSyntax,
                        locals: ImmutableArray.Create(tmp),
                        sideEffects: ImmutableArray.Create<BoundStatement>(side),
                        value: value);
                }

                // default behavior
                var operand = RewriteExpression(node.Operand);
                if (!ReferenceEquals(operand, node.Operand) || effectiveChecked != node.IsChecked)
                    return new BoundConversionExpression(node.Syntax, node.Type, operand, node.Conversion, effectiveChecked);

                return node;
            }
            private static MethodSymbol? FindStructCtorByParamCount(NamedTypeSymbol type, int paramCount)
            {
                if (paramCount == 0)
                    return null;

                var members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is MethodSymbol m &&
                        m.IsConstructor &&
                        !m.IsStatic &&
                        m.Parameters.Length == paramCount)
                    {
                        return m;
                    }
                }
                throw new InvalidOperationException($"No ctor found for '{type.Name}' with {paramCount} parameters.");
            }
            private static FieldSymbol GetFieldOrThrow(NamedTypeSymbol type, string fieldName)
            {
                var members = type.GetMembers();
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] is FieldSymbol f && string.Equals(f.Name, fieldName, StringComparison.Ordinal))
                        return f;
                }
                throw new InvalidOperationException($"Field '{fieldName}' not found in '{type.Name}'.");
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
                        ide.OperatorMethodOpt,
                        ide.UsesDirectOperator,
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
                // Tuple element access
                if (node.Member is TupleElementFieldSymbol tef &&
                    node.ReceiverOpt is not null &&
                    node.ReceiverOpt.Type is TupleTypeSymbol tuple)
                {
                    var exprSyntax = node.Syntax as ExpressionSyntax
                        ?? node.ReceiverOpt.Syntax as ExpressionSyntax
                        ?? throw new InvalidOperationException("Expected ExpressionSyntax for tuple member access.");

                    var receiver = RewriteExpression(node.ReceiverOpt);
                    var vtType = GetValueTupleTypeForElements(tuple.ElementTypes);
                    return ReadValueTupleElement(exprSyntax, receiver, vtType, tuple, tef.ElementIndex);
                }

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
            protected override BoundExpression RewriteCallExpression(BoundCallExpression node)
                => RewriteCallOperands(node);
            private BoundCallExpression RewriteCallOperands(BoundCallExpression node)
            {
                var receiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
                var args = RewriteExpressions(node.Arguments, out var argsChanged);
                if (ReferenceEquals(receiver, node.ReceiverOpt) && !argsChanged)
                    return node;
                return new BoundCallExpression(node.Syntax, receiver, node.Method, args);
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

                BoundExpression? receiver = SpillReceiverForLValueAccess(
                    node.Syntax,
                    left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt),
                    locals,
                    sideEffects);
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
                => expr is BoundLocalExpression
                    or BoundParameterExpression
                    or BoundThisExpression
                    or BoundBaseExpression;

            private BoundExpression? SpillReceiverForLValueAccess(
                SyntaxNode syntax,
                BoundExpression? receiver,
                ImmutableArray<LocalSymbol>.Builder locals,
                ImmutableArray<BoundStatement>.Builder sideEffects)
            {
                if (receiver is null || IsSimpleReceiver(receiver))
                    return receiver;

                if (receiver.Type.IsValueType)
                {
                    if (!receiver.IsLValue)
                        throw new InvalidOperationException(
                            "Cannot mutate a field of a non-lvalue value-type receiver.");

                    var receiverTemp = CreateRefTempLocal(receiver.Type);
                    locals.Add(receiverTemp);

                    sideEffects.Add(
                        new BoundLocalDeclarationStatement(
                            syntax,
                            receiverTemp,
                            new BoundRefExpression(
                                syntax,
                                _compilation.CreateByRefType(receiver.Type),
                                receiver)));

                    return new BoundLocalExpression(syntax, receiverTemp);
                }

                var temp = CreateTempLocal(receiver.Type);
                locals.Add(temp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        syntax,
                        new BoundAssignmentExpression(
                            syntax,
                            new BoundLocalExpression(syntax, temp),
                            receiver)));

                return new BoundLocalExpression(syntax, temp);
            }
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
                    case BoundArrayElementAccessExpression aea:
                        {
                            lvalue = SpillArrayElementAccess(
                                node.Syntax,
                                aea,
                                localsBuilder,
                                sideEffectsBuilder);

                            break;
                        }
                    case BoundMemberAccessExpression ma when ma.Member is FieldSymbol fs:
                        {
                            BoundExpression? receiver = SpillReceiverForLValueAccess(
                                node.Syntax,
                                ma.ReceiverOpt,
                                localsBuilder,
                                sideEffectsBuilder);

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

                BoundExpression? receiver = SpillReceiverForLValueAccess(
                    node.Syntax,
                    left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt),
                    locals,
                    sideEffects);

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
                if (node.UsesDirectOperator)
                {
                    var rewrittenTarget = RewriteExpression(node.Target);

                    if (rewrittenTarget is BoundLocalExpression || rewrittenTarget is BoundParameterExpression)
                        return LowerSimpleDirectIncrementDecrement(node, rewrittenTarget);

                    return LowerDirectIncrementDecrementWithSpill(node, rewrittenTarget);
                }
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

                {
                    var rewrittenTarget = RewriteExpression(node.Target);

                    if (rewrittenTarget is BoundLocalExpression || rewrittenTarget is BoundParameterExpression)
                        return LowerSimpleIncrementDecrement(node, rewrittenTarget);

                    return LowerIncrementDecrementWithSpill(node, rewrittenTarget);
                }

            }
            private BoundExpression LowerSimpleDirectIncrementDecrement(
                BoundIncrementDecrementExpression node,
                BoundExpression rewrittenTarget)
            {
                if (node.IsPostfix)
                    throw new NotSupportedException("Postfix direct increment/decrement should have been rewritten to discarded form.");

                var directCall = RewriteExpression(ReplaceExpressionByReference(node.Value, node.Target, rewrittenTarget));

                return new BoundSequenceExpression(
                    node.Syntax,
                    ImmutableArray<LocalSymbol>.Empty,
                    ImmutableArray.Create<BoundStatement>(
                        new BoundExpressionStatement(node.Syntax, directCall)),
                    rewrittenTarget);
            }
            private BoundExpression LowerDirectIncrementDecrementWithSpill(
                BoundIncrementDecrementExpression node,
                BoundExpression rewrittenTarget)
            {
                if (node.IsPostfix)
                    throw new NotSupportedException("Postfix direct increment/decrement should have been rewritten to discarded form.");

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
                            BoundExpression? receiver = SpillReceiverForLValueAccess(
                                node.Syntax,
                                ma.ReceiverOpt,
                                localsBuilder,
                                sideEffectsBuilder);

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
                            $"Direct increment/decrement lowering for lvalue '{rewrittenTarget.GetType().Name}' is not implemented.");
                }

                var directCall = RewriteExpression(ReplaceExpressionByReference(node.Value, node.Target, lvalue));
                sideEffectsBuilder.Add(new BoundExpressionStatement(node.Syntax, directCall));

                return new BoundSequenceExpression(
                    node.Syntax,
                    localsBuilder.ToImmutable(),
                    sideEffectsBuilder.ToImmutable(),
                    lvalue);
            }
            protected override BoundExpression RewriteCompoundAssignmentExpression(BoundCompoundAssignmentExpression node)
            {
                if (node.UsesDirectOperator)
                {
                    var rewrittenLeft = RewriteExpression(node.Left);

                    if (rewrittenLeft is BoundLocalExpression || rewrittenLeft is BoundParameterExpression)
                        return LowerSimpleDirectCompoundAssignment(node, rewrittenLeft);

                    return LowerDirectCompoundAssignmentWithSpill(node, rewrittenLeft);
                }

                if (node.Left is BoundIndexerAccessExpression leftIndexer &&
                    leftIndexer.Indexer.GetMethod is MethodSymbol indexerGetMethod &&
                    leftIndexer.Indexer.SetMethod is MethodSymbol indexerSetMethod)
                {
                    return LowerIndexerCompoundAssignment(node, leftIndexer, indexerGetMethod, indexerSetMethod);
                }

                if (node.Left is BoundMemberAccessExpression { Member: PropertySymbol prop } leftProp &&
                    !TryGetAutoPropertyBackingField(prop, out _) &&
                    prop.GetMethod is MethodSymbol getMethod &&
                    prop.SetMethod is MethodSymbol setMethod)
                {
                    return LowerPropertyCompoundAssignment(node, leftProp, getMethod, setMethod);
                }

                var rewrittenLeft2 = RewriteExpression(node.Left);

                if (rewrittenLeft2 is BoundLocalExpression || rewrittenLeft2 is BoundParameterExpression)
                {
                    var valueWithRewrittenLeft = ReplaceExpressionByReference(node.Value, node.Left, rewrittenLeft2);
                    var rewrittenValue = RewriteExpression(valueWithRewrittenLeft);
                    return new BoundAssignmentExpression(node.Syntax, rewrittenLeft2, rewrittenValue);
                }

                //Fields
                return LowerCompoundAssignmentWithSpill(node, rewrittenLeft2);
            }
            private BoundExpression LowerSimpleDirectCompoundAssignment(
                BoundCompoundAssignmentExpression node,
                BoundExpression rewrittenLeft)
            {
                var directCall = RewriteExpression(ReplaceExpressionByReference(node.Value, node.Left, rewrittenLeft));

                return new BoundSequenceExpression(
                    node.Syntax,
                    ImmutableArray<LocalSymbol>.Empty,
                    ImmutableArray.Create<BoundStatement>(
                        new BoundExpressionStatement(node.Syntax, directCall)),
                    rewrittenLeft);
            }

            private BoundExpression LowerDirectCompoundAssignmentWithSpill(
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
                            BoundExpression? receiver = SpillReceiverForLValueAccess(
                                node.Syntax,
                                ma.ReceiverOpt,
                                localsBuilder,
                                sideEffectsBuilder);

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
                            $"Direct compound assignment lowering for lvalue '{rewrittenLeft.GetType().Name}' is not implemented.");
                }

                var directCall = RewriteExpression(ReplaceExpressionByReference(node.Value, node.Left, lvalue));

                sideEffectsBuilder.Add(new BoundExpressionStatement(node.Syntax, directCall));

                return new BoundSequenceExpression(
                    node.Syntax,
                    localsBuilder.ToImmutable(),
                    sideEffectsBuilder.ToImmutable(),
                    lvalue);
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

                BoundExpression? receiver = SpillReceiverForLValueAccess(
                    node.Syntax,
                    left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt),
                    locals,
                    sideEffects);

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

                BoundExpression? receiver = SpillReceiverForLValueAccess(
                    node.Syntax,
                    left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt),
                    locals,
                    sideEffects);

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
                            BoundExpression? receiver = SpillReceiverForLValueAccess(
                                node.Syntax,
                                ma.ReceiverOpt,
                                locals,
                                sideEffects);

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

                BoundExpression? receiver = SpillReceiverForLValueAccess(
                    node.Syntax,
                    left.ReceiverOpt is null ? null : RewriteExpression(left.ReceiverOpt),
                    locals,
                    sideEffects);

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
                    case BoundArrayElementAccessExpression aea:
                        {
                            lvalue = SpillArrayElementAccess(
                                node.Syntax,
                                aea,
                                localsBuilder,
                                sideEffectsBuilder);

                            break;
                        }
                    case BoundMemberAccessExpression ma when ma.Member is FieldSymbol fs:
                        {
                            BoundExpression? receiver = SpillReceiverForLValueAccess(
                                node.Syntax,
                                ma.ReceiverOpt,
                                localsBuilder,
                                sideEffectsBuilder);

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
            private BoundArrayElementAccessExpression SpillArrayElementAccess(
                SyntaxNode syntax,
                BoundArrayElementAccessExpression arrayElement,
                ImmutableArray<LocalSymbol>.Builder locals,
                ImmutableArray<BoundStatement>.Builder sideEffects)
            {
                var arrayTemp = CreateTempLocal(arrayElement.Expression.Type);
                locals.Add(arrayTemp);

                sideEffects.Add(
                    new BoundExpressionStatement(
                        syntax,
                        new BoundAssignmentExpression(
                            syntax,
                            new BoundLocalExpression(syntax, arrayTemp),
                            arrayElement.Expression)));

                var rewrittenIndices = ImmutableArray.CreateBuilder<BoundExpression>(arrayElement.Indices.Length);

                for (int i = 0; i < arrayElement.Indices.Length; i++)
                {
                    var index = arrayElement.Indices[i];
                    var indexTemp = CreateTempLocal(index.Type);
                    locals.Add(indexTemp);

                    sideEffects.Add(
                        new BoundExpressionStatement(
                            syntax,
                            new BoundAssignmentExpression(
                                syntax,
                                new BoundLocalExpression(syntax, indexTemp),
                                index)));

                    rewrittenIndices.Add(new BoundLocalExpression(syntax, indexTemp));
                }

                return new BoundArrayElementAccessExpression(
                    syntax,
                    arrayElement.Type,
                    new BoundLocalExpression(syntax, arrayTemp),
                    rewrittenIndices.ToImmutable());
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

        private sealed class LocalFlowOptimizer : BoundTreeRewriterWithStackGuard
        {
            private int _exceptionRegionDepth;
            protected override BoundStatement RewriteTryStatement(BoundTryStatement node)
            {
                _exceptionRegionDepth++;
                try
                {
                    return base.RewriteTryStatement(node);
                }
                finally
                {
                    _exceptionRegionDepth--;
                }
            }
            protected override BoundStatement RewriteBlockStatement(BoundBlockStatement node)
            {
                if (node.Statements.IsDefaultOrEmpty)
                    return node;

                var rewritten = RewriteStatements(node.Statements, out var changed);
                if (_exceptionRegionDepth != 0)
                {
                    if (changed)
                        return new BoundBlockStatement(node.Syntax, rewritten);

                    return node;
                }
                if (CanOptimizeStraightLineStatements(rewritten))
                {
                    var optimized = OptimizeStraightLineStatements(rewritten, out var optimizedChanged);
                    if (optimizedChanged || changed)
                    {
                        if (optimized.IsDefaultOrEmpty)
                            return new BoundEmptyStatement(node.Syntax);

                        if (optimized.Length == 1)
                            return optimized[0];

                        return new BoundBlockStatement(node.Syntax, optimized);
                    }
                }

                if (changed)
                    return new BoundBlockStatement(node.Syntax, rewritten);

                return node;
            }

            protected override BoundExpression RewriteSequenceExpression(BoundSequenceExpression node)
            {
                var sideEffects = RewriteStatements(node.SideEffects, out var sideEffectsChanged);
                var value = RewriteExpression(node.Value);
                var locals = node.Locals;
                if (_exceptionRegionDepth != 0)
                {
                    if (sideEffectsChanged || !ReferenceEquals(value, node.Value))
                        return new BoundSequenceExpression(node.Syntax, locals, sideEffects, value);

                    return node;
                }
                if (!CanOptimizeStraightLineStatements(sideEffects))
                {
                    var prunedLocals = PruneUnusedSequenceLocals(locals, sideEffects, value, out var localsChanged);

                    if (sideEffectsChanged || localsChanged || !ReferenceEquals(value, node.Value))
                        return new BoundSequenceExpression(node.Syntax, prunedLocals, sideEffects, value);

                    return node;
                }

                var optimized = OptimizeSequence(locals, sideEffects, value, out var optimizedChanged);
                if (optimizedChanged || sideEffectsChanged || !ReferenceEquals(value, node.Value))
                    return optimized;

                return node;
            }

            private static bool CanOptimizeStraightLineStatements(ImmutableArray<BoundStatement> statements)
            {
                for (int i = 0; i < statements.Length; i++)
                {
                    switch (statements[i])
                    {
                        case BoundEmptyStatement:
                        case BoundExpressionStatement:
                        case BoundLocalDeclarationStatement:
                        case BoundReturnStatement:
                        case BoundThrowStatement:
                            continue;
                        default:
                            return false;
                    }
                }

                return true;
            }

            private static ImmutableArray<BoundStatement> OptimizeStraightLineStatements(
                ImmutableArray<BoundStatement> statements,
                out bool changed)
            {
                changed = false;
                if (statements.IsDefaultOrEmpty)
                    return statements;

                var optimized = EliminateDeadLocalStores(statements, terminalValue: null, out var optimizedChanged);

                changed = optimizedChanged;
                return optimized;
            }

            private static BoundSequenceExpression OptimizeSequence(
                ImmutableArray<LocalSymbol> locals,
                ImmutableArray<BoundStatement> sideEffects,
                BoundExpression value,
                out bool changed)
            {
                changed = false;

                var removableLocals = CreateLocalSet(locals);
                sideEffects = EliminateDeadLocalStores(sideEffects, value, out var storesChanged, removableLocals);

                var prunedLocals = PruneUnusedSequenceLocals(locals, sideEffects, value, out var localsChanged);
                changed = storesChanged || localsChanged;

                return new BoundSequenceExpression(value.Syntax, prunedLocals, sideEffects, value);
            }

            private static HashSet<LocalSymbol> CreateLocalSet(ImmutableArray<LocalSymbol> locals)
            {
                var result = new HashSet<LocalSymbol>(ReferenceEqualityComparer<LocalSymbol>.Instance);
                for (int i = 0; i < locals.Length; i++)
                    result.Add(locals[i]);
                return result;
            }

            private static ImmutableArray<LocalSymbol> PruneUnusedSequenceLocals(
                ImmutableArray<LocalSymbol> locals,
                ImmutableArray<BoundStatement> sideEffects,
                BoundExpression value,
                out bool changed)
            {
                changed = false;
                if (locals.IsDefaultOrEmpty)
                    return locals;

                var mentioned = new HashSet<LocalSymbol>();
                for (int i = 0; i < sideEffects.Length; i++)
                    CollectMentionedLocals(sideEffects[i], mentioned);
                CollectMentionedLocals(value, mentioned);

                ImmutableArray<LocalSymbol>.Builder? builder = null;
                for (int i = 0; i < locals.Length; i++)
                {
                    var local = locals[i];
                    if (mentioned.Contains(local))
                    {
                        builder?.Add(local);
                        continue;
                    }

                    changed = true;
                    if (builder is null)
                    {
                        builder = ImmutableArray.CreateBuilder<LocalSymbol>(locals.Length - 1);
                        for (int j = 0; j < i; j++)
                            builder.Add(locals[j]);
                    }
                }

                return builder is null ? locals : builder.ToImmutable();
            }

            private static ImmutableArray<BoundStatement> EliminateDeadLocalStores(
                ImmutableArray<BoundStatement> statements,
                BoundExpression? terminalValue,
                out bool changed,
                HashSet<LocalSymbol>? removableLocals = null)
            {
                changed = false;
                if (statements.IsDefaultOrEmpty)
                    return statements;

                var live = new HashSet<LocalSymbol>();
                if (terminalValue is not null)
                    CollectReadLocals(terminalValue, live);

                var kept = new List<BoundStatement>(statements.Length);
                for (int i = statements.Length - 1; i >= 0; i--)
                {
                    var statement = statements[i];
                    if (statement is BoundEmptyStatement)
                    {
                        changed = true;
                        continue;
                    }

                    switch (statement)
                    {
                        case BoundLocalDeclarationStatement localDeclaration:
                            {
                                bool isDead = CanEliminateLocalStore(localDeclaration.Local, live, removableLocals);
                                live.Remove(localDeclaration.Local);
                                if (localDeclaration.Initializer is not null)
                                    CollectReadLocals(localDeclaration.Initializer, live);

                                if (!isDead)
                                {
                                    kept.Add(statement);
                                    break;
                                }

                                changed = true;
                                if (localDeclaration.Initializer is not null &&
                                    !CanDiscardExpressionCompletely(localDeclaration.Initializer))
                                {
                                    kept.Add(new BoundExpressionStatement(localDeclaration.Syntax, localDeclaration.Initializer));
                                }
                                break;
                            }

                        case BoundExpressionStatement expressionStatement:
                            {
                                if (TryGetTopLevelLocalStore(expressionStatement.Expression, out var storedLocal, out var storedValue))
                                {
                                    bool isDead = CanEliminateLocalStore(storedLocal, live, removableLocals);
                                    live.Remove(storedLocal);
                                    CollectReadLocals(storedValue, live);

                                    if (!isDead)
                                    {
                                        kept.Add(statement);
                                        break;
                                    }

                                    changed = true;
                                    if (!CanDiscardExpressionCompletely(storedValue))
                                        kept.Add(new BoundExpressionStatement(expressionStatement.Syntax, storedValue));
                                    break;
                                }

                                CollectReadLocals(expressionStatement.Expression, live);
                                kept.Add(statement);
                                break;
                            }

                        case BoundReturnStatement returnStatement:
                            if (returnStatement.Expression is not null)
                                CollectReadLocals(returnStatement.Expression, live);
                            kept.Add(statement);
                            break;

                        case BoundThrowStatement throwStatement:
                            if (throwStatement.ExpressionOpt is not null)
                                CollectReadLocals(throwStatement.ExpressionOpt, live);
                            kept.Add(statement);
                            break;

                        default:
                            kept.Add(statement);
                            break;
                    }
                }

                kept.Reverse();
                if (!changed)
                    return statements;

                return kept.Count == 0
                    ? ImmutableArray<BoundStatement>.Empty
                    : ImmutableArray.CreateRange(kept);
            }

            private static bool CanEliminateLocalStore(
                LocalSymbol local,
                HashSet<LocalSymbol> live,
                HashSet<LocalSymbol>? removableLocals)
            {
                if (local.IsByRef)
                    return false;
                if (removableLocals is not null && !removableLocals.Contains(local))
                    return false;

                return !live.Contains(local);
            }

            private static bool TryGetTopLevelLocalStore(
                BoundExpression expression,
                out LocalSymbol local,
                out BoundExpression value)
            {
                local = null!;
                value = null!;

                if (expression is BoundAssignmentExpression assignment &&
                    assignment.Left is BoundLocalExpression localExpression)
                {
                    local = localExpression.Local;
                    value = assignment.Right;
                    return true;
                }

                return false;
            }

            private static bool CanDiscardExpressionCompletely(BoundExpression expression)
            {
                switch (expression)
                {
                    case BoundLiteralExpression:
                    case BoundLocalExpression:
                    case BoundParameterExpression:
                    case BoundThisExpression:
                    case BoundSizeOfExpression:
                        return true;

                    case BoundCheckedExpression checkedExpression:
                        return CanDiscardExpressionCompletely(checkedExpression.Expression);

                    case BoundUncheckedExpression uncheckedExpression:
                        return CanDiscardExpressionCompletely(uncheckedExpression.Expression);

                    default:
                        return false;
                }
            }

            private static void CollectMentionedLocals(BoundStatement statement, HashSet<LocalSymbol> locals)
            {
                CollectReadLocals(statement, locals);
                CollectWrittenLocals(statement, locals);
                CollectAddressExposedLocals(statement, locals);
            }

            private static void CollectMentionedLocals(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                CollectReadLocals(expression, locals);
                CollectWrittenLocals(expression, locals);
                CollectAddressExposedLocals(expression, locals);
            }

            private static void CollectReadLocals(BoundStatement statement, HashSet<LocalSymbol> locals)
            {
                switch (statement)
                {
                    case BoundExpressionStatement expressionStatement:
                        CollectReadLocals(expressionStatement.Expression, locals);
                        break;
                    case BoundLocalDeclarationStatement localDeclaration when localDeclaration.Initializer is not null:
                        CollectReadLocals(localDeclaration.Initializer, locals);
                        break;
                    case BoundReturnStatement returnStatement when returnStatement.Expression is not null:
                        CollectReadLocals(returnStatement.Expression, locals);
                        break;
                    case BoundThrowStatement throwStatement when throwStatement.ExpressionOpt is not null:
                        CollectReadLocals(throwStatement.ExpressionOpt, locals);
                        break;
                }
            }

            private static void CollectReadLocals(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                switch (expression)
                {
                    case BoundBadExpression:
                    case BoundLiteralExpression:
                    case BoundParameterExpression:
                    case BoundThisExpression:
                    case BoundLabelExpression:
                    case BoundSizeOfExpression:
                        return;

                    case BoundLocalExpression local:
                        locals.Add(local.Local);
                        return;

                    case BoundThrowExpression throwExpression:
                        CollectReadLocals(throwExpression.Exception, locals);
                        return;

                    case BoundTupleExpression tuple:
                        for (int i = 0; i < tuple.Elements.Length; i++)
                            CollectReadLocals(tuple.Elements[i], locals);
                        return;

                    case BoundArrayInitializerExpression arrayInitializer:
                        for (int i = 0; i < arrayInitializer.Elements.Length; i++)
                            CollectReadLocals(arrayInitializer.Elements[i], locals);
                        return;

                    case BoundArrayCreationExpression arrayCreation:
                        for (int i = 0; i < arrayCreation.DimensionSizes.Length; i++)
                            CollectReadLocals(arrayCreation.DimensionSizes[i], locals);
                        if (arrayCreation.InitializerOpt is not null)
                            CollectReadLocals(arrayCreation.InitializerOpt, locals);
                        return;

                    case BoundArrayElementAccessExpression arrayElementAccess:
                        CollectReadLocals(arrayElementAccess.Expression, locals);
                        for (int i = 0; i < arrayElementAccess.Indices.Length; i++)
                            CollectReadLocals(arrayElementAccess.Indices[i], locals);
                        return;

                    case BoundStackAllocArrayCreationExpression stackAlloc:
                        CollectReadLocals(stackAlloc.Count, locals);
                        if (stackAlloc.InitializerOpt is not null)
                            CollectReadLocals(stackAlloc.InitializerOpt, locals);
                        return;

                    case BoundRefExpression refExpression:
                        CollectReadsFromAddressOperand(refExpression.Operand, locals);
                        return;

                    case BoundAddressOfExpression addressOf:
                        CollectReadsFromAddressOperand(addressOf.Operand, locals);
                        return;

                    case BoundPointerIndirectionExpression pointerIndirection:
                        CollectReadLocals(pointerIndirection.Operand, locals);
                        return;

                    case BoundPointerElementAccessExpression pointerElementAccess:
                        CollectReadLocals(pointerElementAccess.Expression, locals);
                        CollectReadLocals(pointerElementAccess.Index, locals);
                        return;

                    case BoundConversionExpression conversion:
                        CollectReadLocals(conversion.Operand, locals);
                        return;

                    case BoundAsExpression asExpression:
                        CollectReadLocals(asExpression.Operand, locals);
                        return;

                    case BoundUnaryExpression unary:
                        CollectReadLocals(unary.Operand, locals);
                        return;

                    case BoundBinaryExpression binary:
                        CollectReadLocals(binary.Left, locals);
                        CollectReadLocals(binary.Right, locals);
                        return;

                    case BoundConditionalExpression conditional:
                        CollectReadLocals(conditional.Condition, locals);
                        CollectReadLocals(conditional.WhenTrue, locals);
                        CollectReadLocals(conditional.WhenFalse, locals);
                        return;

                    case BoundAssignmentExpression assignment:
                        CollectReadsFromAssignmentTarget(assignment.Left, locals);
                        CollectReadLocals(assignment.Right, locals);
                        return;

                    case BoundCompoundAssignmentExpression compoundAssignment:
                        CollectReadsFromAssignmentTarget(compoundAssignment.Left, locals, includeLocalTargetRead: true);
                        CollectReadLocals(compoundAssignment.Value, locals);
                        return;

                    case BoundNullCoalescingAssignmentExpression nullCoalescingAssignment:
                        CollectReadsFromAssignmentTarget(nullCoalescingAssignment.Left, locals, includeLocalTargetRead: true);
                        CollectReadLocals(nullCoalescingAssignment.Value, locals);
                        return;

                    case BoundIncrementDecrementExpression incrementDecrement:
                        CollectReadLocals(incrementDecrement.Target, locals);
                        CollectReadLocals(incrementDecrement.Read, locals);
                        CollectReadLocals(incrementDecrement.Value, locals);
                        return;

                    case BoundCallExpression call:
                        if (call.ReceiverOpt is not null)
                            CollectReadLocals(call.ReceiverOpt, locals);
                        for (int i = 0; i < call.Arguments.Length; i++)
                            CollectReadLocals(call.Arguments[i], locals);
                        return;

                    case BoundObjectCreationExpression objectCreation:
                        for (int i = 0; i < objectCreation.Arguments.Length; i++)
                            CollectReadLocals(objectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundImplicitObjectCreationExpression implicitObjectCreation:
                        for (int i = 0; i < implicitObjectCreation.Arguments.Length; i++)
                            CollectReadLocals(implicitObjectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundCollectionExpression collectionExpression:
                        for (int i = 0; i < collectionExpression.Elements.Length; i++)
                            CollectReadLocals(collectionExpression.Elements[i].Expression, locals);
                        return;

                    case BoundSequenceExpression sequence:
                        for (int i = 0; i < sequence.SideEffects.Length; i++)
                            CollectReadLocals(sequence.SideEffects[i], locals);
                        CollectReadLocals(sequence.Value, locals);
                        return;

                    case BoundIndexerAccessExpression indexerAccess:
                        CollectReadLocals(indexerAccess.Receiver, locals);
                        for (int i = 0; i < indexerAccess.Arguments.Length; i++)
                            CollectReadLocals(indexerAccess.Arguments[i], locals);
                        return;

                    case BoundMemberAccessExpression memberAccess:
                        if (memberAccess.ReceiverOpt is not null)
                            CollectReadLocals(memberAccess.ReceiverOpt, locals);
                        return;

                    case BoundCheckedExpression checkedExpression:
                        CollectReadLocals(checkedExpression.Expression, locals);
                        return;

                    case BoundUncheckedExpression uncheckedExpression:
                        CollectReadLocals(uncheckedExpression.Expression, locals);
                        return;

                    case BoundIsPatternExpression isPattern:
                        CollectReadLocals(isPattern.Operand, locals);
                        return;
                }
            }

            private static void CollectReadsFromAssignmentTarget(
                BoundExpression expression,
                HashSet<LocalSymbol> locals,
                bool includeLocalTargetRead = false)
            {
                switch (expression)
                {
                    case BoundLocalExpression local when includeLocalTargetRead:
                        locals.Add(local.Local);
                        return;
                    case BoundLocalExpression:
                    case BoundParameterExpression:
                    case BoundThisExpression:
                        return;
                    case BoundMemberAccessExpression memberAccess:
                        if (memberAccess.ReceiverOpt is not null)
                            CollectReadLocals(memberAccess.ReceiverOpt, locals);
                        return;
                    case BoundIndexerAccessExpression indexerAccess:
                        CollectReadLocals(indexerAccess.Receiver, locals);
                        for (int i = 0; i < indexerAccess.Arguments.Length; i++)
                            CollectReadLocals(indexerAccess.Arguments[i], locals);
                        return;
                    case BoundArrayElementAccessExpression arrayElementAccess:
                        CollectReadLocals(arrayElementAccess.Expression, locals);
                        for (int i = 0; i < arrayElementAccess.Indices.Length; i++)
                            CollectReadLocals(arrayElementAccess.Indices[i], locals);
                        return;
                    case BoundPointerIndirectionExpression pointerIndirection:
                        CollectReadLocals(pointerIndirection.Operand, locals);
                        return;
                    case BoundPointerElementAccessExpression pointerElementAccess:
                        CollectReadLocals(pointerElementAccess.Expression, locals);
                        CollectReadLocals(pointerElementAccess.Index, locals);
                        return;
                    default:
                        CollectReadLocals(expression, locals);
                        return;
                }
            }

            private static void CollectReadsFromAddressOperand(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                switch (expression)
                {
                    case BoundLocalExpression:
                    case BoundParameterExpression:
                    case BoundThisExpression:
                        return;
                    default:
                        CollectReadsFromAssignmentTarget(expression, locals);
                        return;
                }
            }

            private static void CollectWrittenLocals(BoundStatement statement, HashSet<LocalSymbol> locals)
            {
                switch (statement)
                {
                    case BoundExpressionStatement expressionStatement:
                        CollectWrittenLocals(expressionStatement.Expression, locals);
                        break;
                    case BoundLocalDeclarationStatement localDeclaration:
                        locals.Add(localDeclaration.Local);
                        if (localDeclaration.Initializer is not null)
                            CollectWrittenLocals(localDeclaration.Initializer, locals);
                        break;
                    case BoundReturnStatement returnStatement when returnStatement.Expression is not null:
                        CollectWrittenLocals(returnStatement.Expression, locals);
                        break;
                    case BoundThrowStatement throwStatement when throwStatement.ExpressionOpt is not null:
                        CollectWrittenLocals(throwStatement.ExpressionOpt, locals);
                        break;
                }
            }

            private static void CollectWrittenLocals(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                switch (expression)
                {
                    case BoundAssignmentExpression assignment:
                        if (assignment.Left is BoundLocalExpression local)
                            locals.Add(local.Local);
                        CollectWrittenLocals(assignment.Left, locals);
                        CollectWrittenLocals(assignment.Right, locals);
                        return;

                    case BoundCompoundAssignmentExpression compoundAssignment:
                        if (compoundAssignment.Left is BoundLocalExpression compoundLocal)
                            locals.Add(compoundLocal.Local);
                        CollectWrittenLocals(compoundAssignment.Left, locals);
                        CollectWrittenLocals(compoundAssignment.Value, locals);
                        return;

                    case BoundNullCoalescingAssignmentExpression nullCoalescingAssignment:
                        if (nullCoalescingAssignment.Left is BoundLocalExpression nullCoalescingLocal)
                            locals.Add(nullCoalescingLocal.Local);
                        CollectWrittenLocals(nullCoalescingAssignment.Left, locals);
                        CollectWrittenLocals(nullCoalescingAssignment.Value, locals);
                        return;

                    case BoundIncrementDecrementExpression incrementDecrement:
                        if (incrementDecrement.Target is BoundLocalExpression incrementDecrementLocal)
                            locals.Add(incrementDecrementLocal.Local);
                        CollectWrittenLocals(incrementDecrement.Target, locals);
                        CollectWrittenLocals(incrementDecrement.Read, locals);
                        CollectWrittenLocals(incrementDecrement.Value, locals);
                        return;

                    case BoundSequenceExpression sequence:
                        for (int i = 0; i < sequence.SideEffects.Length; i++)
                            CollectWrittenLocals(sequence.SideEffects[i], locals);
                        CollectWrittenLocals(sequence.Value, locals);
                        return;

                    case BoundTupleExpression tuple:
                        for (int i = 0; i < tuple.Elements.Length; i++)
                            CollectWrittenLocals(tuple.Elements[i], locals);
                        return;

                    case BoundArrayInitializerExpression arrayInitializer:
                        for (int i = 0; i < arrayInitializer.Elements.Length; i++)
                            CollectWrittenLocals(arrayInitializer.Elements[i], locals);
                        return;

                    case BoundArrayCreationExpression arrayCreation:
                        for (int i = 0; i < arrayCreation.DimensionSizes.Length; i++)
                            CollectWrittenLocals(arrayCreation.DimensionSizes[i], locals);
                        if (arrayCreation.InitializerOpt is not null)
                            CollectWrittenLocals(arrayCreation.InitializerOpt, locals);
                        return;

                    case BoundArrayElementAccessExpression arrayElementAccess:
                        CollectWrittenLocals(arrayElementAccess.Expression, locals);
                        for (int i = 0; i < arrayElementAccess.Indices.Length; i++)
                            CollectWrittenLocals(arrayElementAccess.Indices[i], locals);
                        return;

                    case BoundStackAllocArrayCreationExpression stackAlloc:
                        CollectWrittenLocals(stackAlloc.Count, locals);
                        if (stackAlloc.InitializerOpt is not null)
                            CollectWrittenLocals(stackAlloc.InitializerOpt, locals);
                        return;

                    case BoundRefExpression refExpression:
                        CollectWrittenLocals(refExpression.Operand, locals);
                        return;

                    case BoundAddressOfExpression addressOf:
                        CollectWrittenLocals(addressOf.Operand, locals);
                        return;

                    case BoundPointerIndirectionExpression pointerIndirection:
                        CollectWrittenLocals(pointerIndirection.Operand, locals);
                        return;

                    case BoundPointerElementAccessExpression pointerElementAccess:
                        CollectWrittenLocals(pointerElementAccess.Expression, locals);
                        CollectWrittenLocals(pointerElementAccess.Index, locals);
                        return;

                    case BoundConversionExpression conversion:
                        CollectWrittenLocals(conversion.Operand, locals);
                        return;

                    case BoundAsExpression asExpression:
                        CollectWrittenLocals(asExpression.Operand, locals);
                        return;

                    case BoundUnaryExpression unary:
                        CollectWrittenLocals(unary.Operand, locals);
                        return;

                    case BoundBinaryExpression binary:
                        CollectWrittenLocals(binary.Left, locals);
                        CollectWrittenLocals(binary.Right, locals);
                        return;

                    case BoundConditionalExpression conditional:
                        CollectWrittenLocals(conditional.Condition, locals);
                        CollectWrittenLocals(conditional.WhenTrue, locals);
                        CollectWrittenLocals(conditional.WhenFalse, locals);
                        return;

                    case BoundCallExpression call:
                        if (call.ReceiverOpt is not null)
                            CollectWrittenLocals(call.ReceiverOpt, locals);
                        for (int i = 0; i < call.Arguments.Length; i++)
                            CollectWrittenLocals(call.Arguments[i], locals);
                        return;

                    case BoundObjectCreationExpression objectCreation:
                        for (int i = 0; i < objectCreation.Arguments.Length; i++)
                            CollectWrittenLocals(objectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundImplicitObjectCreationExpression implicitObjectCreation:
                        for (int i = 0; i < implicitObjectCreation.Arguments.Length; i++)
                            CollectWrittenLocals(implicitObjectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundCollectionExpression collectionExpression:
                        for (int i = 0; i < collectionExpression.Elements.Length; i++)
                            CollectWrittenLocals(collectionExpression.Elements[i].Expression, locals);
                        return;

                    case BoundIndexerAccessExpression indexerAccess:
                        CollectWrittenLocals(indexerAccess.Receiver, locals);
                        for (int i = 0; i < indexerAccess.Arguments.Length; i++)
                            CollectWrittenLocals(indexerAccess.Arguments[i], locals);
                        return;

                    case BoundMemberAccessExpression memberAccess:
                        if (memberAccess.ReceiverOpt is not null)
                            CollectWrittenLocals(memberAccess.ReceiverOpt, locals);
                        return;

                    case BoundCheckedExpression checkedExpression:
                        CollectWrittenLocals(checkedExpression.Expression, locals);
                        return;

                    case BoundUncheckedExpression uncheckedExpression:
                        CollectWrittenLocals(uncheckedExpression.Expression, locals);
                        return;

                    case BoundIsPatternExpression isPattern:
                        if (isPattern.DeclaredLocalOpt is not null)
                            locals.Add(isPattern.DeclaredLocalOpt);
                        CollectWrittenLocals(isPattern.Operand, locals);
                        return;

                    default:
                        return;
                }
            }

            private static void CollectAddressExposedLocals(BoundStatement statement, HashSet<LocalSymbol> locals)
            {
                switch (statement)
                {
                    case BoundExpressionStatement expressionStatement:
                        CollectAddressExposedLocals(expressionStatement.Expression, locals);
                        break;
                    case BoundLocalDeclarationStatement localDeclaration when localDeclaration.Initializer is not null:
                        CollectAddressExposedLocals(localDeclaration.Initializer, locals);
                        break;
                    case BoundReturnStatement returnStatement when returnStatement.Expression is not null:
                        CollectAddressExposedLocals(returnStatement.Expression, locals);
                        break;
                    case BoundThrowStatement throwStatement when throwStatement.ExpressionOpt is not null:
                        CollectAddressExposedLocals(throwStatement.ExpressionOpt, locals);
                        break;
                }
            }

            private static void CollectAddressExposedLocals(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                switch (expression)
                {
                    case BoundRefExpression refExpression:
                        CollectAddressLocalsFromOperand(refExpression.Operand, locals);
                        return;

                    case BoundAddressOfExpression addressOf:
                        CollectAddressLocalsFromOperand(addressOf.Operand, locals);
                        return;

                    case BoundTupleExpression tuple:
                        for (int i = 0; i < tuple.Elements.Length; i++)
                            CollectAddressExposedLocals(tuple.Elements[i], locals);
                        return;

                    case BoundArrayInitializerExpression arrayInitializer:
                        for (int i = 0; i < arrayInitializer.Elements.Length; i++)
                            CollectAddressExposedLocals(arrayInitializer.Elements[i], locals);
                        return;

                    case BoundArrayCreationExpression arrayCreation:
                        for (int i = 0; i < arrayCreation.DimensionSizes.Length; i++)
                            CollectAddressExposedLocals(arrayCreation.DimensionSizes[i], locals);
                        if (arrayCreation.InitializerOpt is not null)
                            CollectAddressExposedLocals(arrayCreation.InitializerOpt, locals);
                        return;

                    case BoundArrayElementAccessExpression arrayElementAccess:
                        CollectAddressExposedLocals(arrayElementAccess.Expression, locals);
                        for (int i = 0; i < arrayElementAccess.Indices.Length; i++)
                            CollectAddressExposedLocals(arrayElementAccess.Indices[i], locals);
                        return;

                    case BoundStackAllocArrayCreationExpression stackAlloc:
                        CollectAddressExposedLocals(stackAlloc.Count, locals);
                        if (stackAlloc.InitializerOpt is not null)
                            CollectAddressExposedLocals(stackAlloc.InitializerOpt, locals);
                        return;

                    case BoundPointerIndirectionExpression pointerIndirection:
                        CollectAddressExposedLocals(pointerIndirection.Operand, locals);
                        return;

                    case BoundPointerElementAccessExpression pointerElementAccess:
                        CollectAddressExposedLocals(pointerElementAccess.Expression, locals);
                        CollectAddressExposedLocals(pointerElementAccess.Index, locals);
                        return;

                    case BoundConversionExpression conversion:
                        CollectAddressExposedLocals(conversion.Operand, locals);
                        return;

                    case BoundAsExpression asExpression:
                        CollectAddressExposedLocals(asExpression.Operand, locals);
                        return;

                    case BoundUnaryExpression unary:
                        CollectAddressExposedLocals(unary.Operand, locals);
                        return;

                    case BoundBinaryExpression binary:
                        CollectAddressExposedLocals(binary.Left, locals);
                        CollectAddressExposedLocals(binary.Right, locals);
                        return;

                    case BoundConditionalExpression conditional:
                        CollectAddressExposedLocals(conditional.Condition, locals);
                        CollectAddressExposedLocals(conditional.WhenTrue, locals);
                        CollectAddressExposedLocals(conditional.WhenFalse, locals);
                        return;

                    case BoundAssignmentExpression assignment:
                        CollectAddressExposedLocals(assignment.Left, locals);
                        CollectAddressExposedLocals(assignment.Right, locals);
                        return;

                    case BoundCompoundAssignmentExpression compoundAssignment:
                        CollectAddressExposedLocals(compoundAssignment.Left, locals);
                        CollectAddressExposedLocals(compoundAssignment.Value, locals);
                        return;

                    case BoundNullCoalescingAssignmentExpression nullCoalescingAssignment:
                        CollectAddressExposedLocals(nullCoalescingAssignment.Left, locals);
                        CollectAddressExposedLocals(nullCoalescingAssignment.Value, locals);
                        return;

                    case BoundIncrementDecrementExpression incrementDecrement:
                        CollectAddressExposedLocals(incrementDecrement.Target, locals);
                        CollectAddressExposedLocals(incrementDecrement.Read, locals);
                        CollectAddressExposedLocals(incrementDecrement.Value, locals);
                        return;

                    case BoundCallExpression call:
                        if (call.ReceiverOpt is not null)
                            CollectAddressExposedLocals(call.ReceiverOpt, locals);
                        for (int i = 0; i < call.Arguments.Length; i++)
                            CollectAddressExposedLocals(call.Arguments[i], locals);
                        return;

                    case BoundObjectCreationExpression objectCreation:
                        for (int i = 0; i < objectCreation.Arguments.Length; i++)
                            CollectAddressExposedLocals(objectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundImplicitObjectCreationExpression implicitObjectCreation:
                        for (int i = 0; i < implicitObjectCreation.Arguments.Length; i++)
                            CollectAddressExposedLocals(implicitObjectCreation.Arguments[i], locals);
                        return;

                    case BoundUnboundCollectionExpression collectionExpression:
                        for (int i = 0; i < collectionExpression.Elements.Length; i++)
                            CollectAddressExposedLocals(collectionExpression.Elements[i].Expression, locals);
                        return;

                    case BoundSequenceExpression sequence:
                        for (int i = 0; i < sequence.SideEffects.Length; i++)
                            CollectAddressExposedLocals(sequence.SideEffects[i], locals);
                        CollectAddressExposedLocals(sequence.Value, locals);
                        return;

                    case BoundIndexerAccessExpression indexerAccess:
                        CollectAddressExposedLocals(indexerAccess.Receiver, locals);
                        for (int i = 0; i < indexerAccess.Arguments.Length; i++)
                            CollectAddressExposedLocals(indexerAccess.Arguments[i], locals);
                        return;

                    case BoundMemberAccessExpression memberAccess:
                        if (memberAccess.ReceiverOpt is not null)
                            CollectAddressExposedLocals(memberAccess.ReceiverOpt, locals);
                        return;

                    case BoundCheckedExpression checkedExpression:
                        CollectAddressExposedLocals(checkedExpression.Expression, locals);
                        return;

                    case BoundUncheckedExpression uncheckedExpression:
                        CollectAddressExposedLocals(uncheckedExpression.Expression, locals);
                        return;

                    case BoundIsPatternExpression isPattern:
                        CollectAddressExposedLocals(isPattern.Operand, locals);
                        return;

                    default:
                        return;
                }
            }

            private static void CollectAddressLocalsFromOperand(BoundExpression expression, HashSet<LocalSymbol> locals)
            {
                switch (expression)
                {
                    case BoundLocalExpression local:
                        locals.Add(local.Local);
                        return;
                    case BoundMemberAccessExpression memberAccess:
                        if (memberAccess.ReceiverOpt is not null)
                            CollectAddressExposedLocals(memberAccess.ReceiverOpt, locals);
                        return;
                    case BoundIndexerAccessExpression indexerAccess:
                        CollectAddressExposedLocals(indexerAccess.Receiver, locals);
                        for (int i = 0; i < indexerAccess.Arguments.Length; i++)
                            CollectAddressExposedLocals(indexerAccess.Arguments[i], locals);
                        return;
                    case BoundArrayElementAccessExpression arrayElementAccess:
                        CollectAddressExposedLocals(arrayElementAccess.Expression, locals);
                        for (int i = 0; i < arrayElementAccess.Indices.Length; i++)
                            CollectAddressExposedLocals(arrayElementAccess.Indices[i], locals);
                        return;
                    case BoundPointerIndirectionExpression pointerIndirection:
                        CollectAddressExposedLocals(pointerIndirection.Operand, locals);
                        return;
                    case BoundPointerElementAccessExpression pointerElementAccess:
                        CollectAddressExposedLocals(pointerElementAccess.Expression, locals);
                        CollectAddressExposedLocals(pointerElementAccess.Index, locals);
                        return;
                }
            }

        }


    }
}
