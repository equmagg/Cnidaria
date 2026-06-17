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
        private BoundStatement BindGoto(GotoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.CaseOrDefaultKeyword.Kind == SyntaxKind.CaseKeyword)
                return BindGotoCase(node, context, diagnostics);

            if (node.CaseOrDefaultKeyword.Kind == SyntaxKind.DefaultKeyword)
                return BindGotoDefault(node, context, diagnostics);

            if (node.Kind == SyntaxKind.GotoStatement)
                return BindGotoIdentifier(node, context, diagnostics);

            diagnostics.Add(new Diagnostic(
                "CN_FLOW005",
                DiagnosticSeverity.Error,
                $"Unsupported goto statement kind: {node.Kind}.",
                new Location(context.SemanticModel.SyntaxTree, node.Span)));
            return new BoundBadStatement(node);
        }
        private BoundStatement BindGotoIdentifier(GotoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (node.Expression is not IdentifierNameSyntax id)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW006",
                    DiagnosticSeverity.Error,
                    "Expected an identifier after 'goto'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var name = id.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW006",
                    DiagnosticSeverity.Error,
                    "Expected an identifier after 'goto'.",
                    new Location(context.SemanticModel.SyntaxTree, id.Span)));
                return new BoundBadStatement(node);
            }

            var label = _flow.GetOrCreateSourceLabel(name);
            _flow.RegisterGoto(node, label);

            Record(id, new BoundLabelExpression(id, label), context);

            return new BoundGotoStatement(node, label);
        }
        private BoundStatement BindGotoCase(GotoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentSwitchGotoScope(out var switchScope))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW010",
                    DiagnosticSeverity.Error,
                    "The 'goto case' statement is not within a switch statement.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            if (node.Expression is null)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW011",
                    DiagnosticSeverity.Error,
                    "Expected a constant expression after 'goto case'.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var value = BindExpression(node.Expression, context, diagnostics);
            value = ApplyConversion(
                exprSyntax: node.Expression,
                expr: value,
                targetType: switchScope.GoverningType,
                diagnosticNode: node,
                context: context,
                diagnostics: diagnostics,
                requireImplicit: true);

            if (value.HasErrors)
                return new BoundBadStatement(node);

            if (!value.ConstantValueOpt.HasValue)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW012",
                    DiagnosticSeverity.Error,
                    "The 'goto case' expression must be a constant expression.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadStatement(node);
            }

            var key = new SwitchCaseKey(value.ConstantValueOpt.Value);
            if (!switchScope.TryGetCaseLabel(key, out var label))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW013",
                    DiagnosticSeverity.Error,
                    "No matching case label exists in the current switch statement.",
                    new Location(context.SemanticModel.SyntaxTree, node.Expression.Span)));
                return new BoundBadStatement(node);
            }

            _flow.RegisterGoto(node, label!);
            return new BoundGotoStatement(node, label!);
        }
        private BoundStatement BindGotoDefault(GotoStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            if (!_flow.TryGetCurrentSwitchGotoScope(out var switchScope))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW014",
                    DiagnosticSeverity.Error,
                    "The 'goto default' statement is not within a switch statement.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            if (switchScope.DefaultLabel is not LabelSymbol label)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW015",
                    DiagnosticSeverity.Error,
                    "No default label exists in the current switch statement.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            _flow.RegisterGoto(node, label);
            return new BoundGotoStatement(node, label);
        }
        private BoundStatement BindLabeledStatement(LabeledStatementSyntax node, BindingContext context, DiagnosticBag diagnostics)
        {
            var name = node.Identifier.ValueText ?? string.Empty;
            if (name.Length == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW007",
                    DiagnosticSeverity.Error,
                    "Invalid label name.",
                    new Location(context.SemanticModel.SyntaxTree, node.Span)));
                return new BoundBadStatement(node);
            }

            var label = _flow.GetOrCreateSourceLabel(name);
            if (!label.TryDefine(context.SemanticModel.SyntaxTree, node))
            {
                diagnostics.Add(new Diagnostic(
                    "CN_FLOW003",
                    DiagnosticSeverity.Error,
                    $"Label '{name}' is a duplicate.",
                    new Location(context.SemanticModel.SyntaxTree, node.Identifier.Span)));
            }
            else
            {
                _flow.RegisterLabelDefinition(label);
            }

            context.Recorder.RecordDeclared(node, label);

            var inner = BindStatement(node.Statement, context, diagnostics);
            var list = ImmutableArray.Create<BoundStatement>(
                new BoundLabelStatement(node, label),
                inner);

            return new BoundStatementList(node, list);
        }
        private ImmutableArray<BoundStatement> BindForVariableDeclaration(
            SyntaxNode ownerSyntax,
            VariableDeclarationSyntax decl,
            BindingContext context,
            DiagnosticBag diagnostics)
        {
            if (decl.Variables.Count == 0)
                return ImmutableArray<BoundStatement>.Empty;

            var isVar = IsVar(decl.Type);

            TypeSymbol? explicitType = null;
            if (!isVar)
                explicitType = BindType(decl.Type, context, diagnostics);

            if (decl.Variables.Count == 1)
            {
                var s = BindSingleDeclarator(
                    ownerSyntax, decl.Variables[0], isVar, explicitType, isRefLocal: false, isConst: false, isUsing: false, context, diagnostics);
                return ImmutableArray.Create(s);
            }

            var list = ImmutableArray.CreateBuilder<BoundStatement>(decl.Variables.Count);
            for (int i = 0; i < decl.Variables.Count; i++)
                list.Add(BindSingleDeclarator(
                    ownerSyntax, decl.Variables[i], isVar, explicitType, isRefLocal: false, isConst: false, isUsing: false, context, diagnostics));

            return list.ToImmutable();
        }
        }
}
