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
                BoundUsingStatement s => RewriteUsingStatement(s),
                BoundEmptyStatement s => s,
                BoundTryStatement s => RewriteTryStatement(s),
                BoundThrowStatement s => RewriteThrowStatement(s),
                BoundReturnStatement s => RewriteReturnStatement(s),
                BoundYieldStatement s => RewriteYieldStatement(s),
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
                BoundTypeOfExpression e => e,
                BoundSizeOfExpression e => e,
                BoundUnboundLambdaExpression e => e,
                BoundMethodGroupExpression e => e,
                BoundLambdaExpression e => RewriteLambdaExpression(e),
                BoundClosureCellCreationExpression e => RewriteClosureCellCreationExpression(e),
                BoundClosureCreationExpression e => RewriteClosureCreationExpression(e),
                BoundClosureSlotExpression e => RewriteClosureSlotExpression(e),
                BoundClosureAccessExpression e => RewriteClosureAccessExpression(e),


                BoundThrowExpression e => RewriteThrowExpression(e),
                BoundTupleExpression e => RewriteTupleExpression(e),
                BoundArrayInitializerExpression e => RewriteArrayInitializerExpression(e),
                BoundArrayCreationExpression e => RewriteArrayCreationExpression(e),
                BoundArrayElementAccessExpression e => RewriteArrayElementAccessExpression(e),
                BoundInlineArrayElementAccessExpression e => RewriteInlineArrayElementAccessExpression(e),
                BoundStackAllocArrayCreationExpression e => RewriteStackAllocArrayCreationExpression(e),
                BoundStaticDataExpression e => RewriteStaticDataExpression(e),

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
                BoundSpanCollectionExpression e => RewriteSpanCollectionExpression(e),
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
                return new BoundLocalDeclarationStatement(node.Syntax, node.Local, init, node.IsUsing);

            return node;
        }
        protected virtual BoundStatement RewriteUsingStatement(BoundUsingStatement node)
        {
            var declarations = node.Declarations;
            bool declarationsChanged = false;
            if (!declarations.IsDefaultOrEmpty)
            {
                var builder = ImmutableArray.CreateBuilder<BoundLocalDeclarationStatement>(declarations.Length);
                for (int i = 0; i < declarations.Length; i++)
                {
                    var rewritten = (BoundLocalDeclarationStatement)RewriteLocalDeclarationStatement(declarations[i]);
                    if (!ReferenceEquals(rewritten, declarations[i]))
                        declarationsChanged = true;
                    builder.Add(rewritten);
                }
                if (declarationsChanged)
                    declarations = builder.ToImmutable();
            }

            var expression = node.ExpressionOpt is null ? null : RewriteExpression(node.ExpressionOpt);
            var body = RewriteStatement(node.Body);

            if (declarationsChanged || !ReferenceEquals(expression, node.ExpressionOpt) || !ReferenceEquals(body, node.Body))
            {
                return new BoundUsingStatement(
                    (UsingStatementSyntax)node.Syntax,
                    declarations,
                    expression,
                    body);
            }

            return node;
        }
        protected virtual BoundStatement RewriteReturnStatement(BoundReturnStatement node)
        {
            var expr = node.Expression is null ? null : RewriteExpression(node.Expression);
            if (!ReferenceEquals(expr, node.Expression))
                return new BoundReturnStatement(node.Syntax, expr);

            return node;
        }
        protected virtual BoundStatement RewriteYieldStatement(BoundYieldStatement node)
        {
            var expr = node.ExpressionOpt is null ? null : RewriteExpression(node.ExpressionOpt);

            if (!ReferenceEquals(expr, node.ExpressionOpt))
            {
                return new BoundYieldStatement(
                    (YieldStatementSyntax)node.Syntax,
                    node.YieldKind,
                    expr,
                    node.ElementTypeOpt);
            }

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
                return new BoundIfStatement(node.Syntax, condition, thenStmt, elseStmt);
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
        protected virtual BoundExpression RewriteInlineArrayElementAccessExpression(BoundInlineArrayElementAccessExpression node)
        {
            var receiver = RewriteExpression(node.Receiver);
            var index = RewriteExpression(node.Index);
            if (!ReferenceEquals(receiver, node.Receiver) || !ReferenceEquals(index, node.Index))
                return new BoundInlineArrayElementAccessExpression(
                    node.Syntax,
                    receiver,
                    node.ElementField,
                    index,
                    node.Length,
                    node.IsLValue);
            return node;
        }

        protected virtual BoundExpression RewriteSpanCollectionExpression(BoundSpanCollectionExpression node)
        {
            var elements = RewriteExpressions(node.Elements, out var changed);
            if (changed)
            {
                return new BoundSpanCollectionExpression(
                    (CollectionExpressionSyntax)node.Syntax,
                    (NamedTypeSymbol)node.Type,
                    node.ElementType,
                    elements);
            }

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
        protected virtual BoundExpression RewriteStaticDataExpression(BoundStaticDataExpression node)
        {
            var elements = RewriteExpressions(node.Elements, out var changed);

            if (changed)
            {
                return new BoundStaticDataExpression(
                    node.Syntax,
                    (PointerTypeSymbol)node.Type,
                    node.ElementType,
                    elements);
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

        protected virtual BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
        {
            var target = node.TargetOpt is null ? null : RewriteExpression(node.TargetOpt);
            var body = RewriteStatement(node.Body);
            if (!ReferenceEquals(body, node.Body) || !ReferenceEquals(target, node.TargetOpt))
            {
                return new BoundLambdaExpression(
                    (ExpressionSyntax)node.Syntax,
                    (NamedTypeSymbol)node.Type,
                    node.Method,
                    node.InvokeMethod,
                    body,
                    node.IsStatic,
                    node.IsAsync,
                    target);
            }

            return node;
        }

        protected virtual BoundExpression RewriteClosureCellCreationExpression(BoundClosureCellCreationExpression node)
        {
            var initial = RewriteExpression(node.InitialValue);
            if (!ReferenceEquals(initial, node.InitialValue))
                return new BoundClosureCellCreationExpression(node.Syntax, (NamedTypeSymbol)node.Type, node.ValueType, initial);

            return node;
        }

        protected virtual BoundExpression RewriteClosureCreationExpression(BoundClosureCreationExpression node)
        {
            var cells = RewriteExpressions(node.Cells, out var changed);
            if (changed)
                return new BoundClosureCreationExpression(node.Syntax, (NamedTypeSymbol)node.Type, cells);

            return node;
        }

        protected virtual BoundExpression RewriteClosureSlotExpression(BoundClosureSlotExpression node)
        {
            var closure = RewriteExpression(node.Closure);
            if (!ReferenceEquals(closure, node.Closure))
                return new BoundClosureSlotExpression(node.Syntax, (NamedTypeSymbol)node.Type, closure, node.SlotIndex);

            return node;
        }

        protected virtual BoundExpression RewriteClosureAccessExpression(BoundClosureAccessExpression node)
        {
            var cell = RewriteExpression(node.Cell);
            if (!ReferenceEquals(cell, node.Cell))
                return new BoundClosureAccessExpression(node.Syntax, node.Type, cell);

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
                return new BoundConditionalExpression(node.Syntax, node.Type, cond, whenTrue, whenFalse, node.ConstantValueOpt);
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
}
