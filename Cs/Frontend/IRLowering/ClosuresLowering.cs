using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cnidaria.Cs
{
    internal sealed class LambdaClosureRewriter : BoundTreeRewriterWithStackGuard
    {
        private readonly Compilation _compilation;
        private readonly NamedTypeSymbol _objectType;
        private readonly Dictionary<Symbol, BoundExpression> _cellByCapturedSymbol =
            new(ReferenceEqualityComparer<Symbol>.Instance);
        private int _tempId;

        private sealed class ClosureLambdaMethodSymbol : MethodSymbol
        {
            public override string Name { get; }
            public override Symbol? ContainingSymbol { get; }
            public override ImmutableArray<Location> Locations { get; }
            public override TypeSymbol ReturnType { get; }
            private ImmutableArray<ParameterSymbol> _parameters;
            public override ImmutableArray<ParameterSymbol> Parameters => _parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : _parameters;
            public override ImmutableArray<TypeParameterSymbol> TypeParameters => ImmutableArray<TypeParameterSymbol>.Empty;
            public override bool IsStatic => true;
            public override bool IsConstructor => false;
            public override bool IsAsync { get; }

            public ClosureLambdaMethodSymbol(MethodSymbol original)
            {
                Name = original.Name;
                ContainingSymbol = original.ContainingSymbol;
                Locations = original.Locations;
                ReturnType = original.ReturnType;
                _parameters = ImmutableArray<ParameterSymbol>.Empty;
                IsAsync = original.IsAsync;
            }

            public void SetParameters(ImmutableArray<ParameterSymbol> parameters)
                => _parameters = parameters.IsDefault ? ImmutableArray<ParameterSymbol>.Empty : parameters;
        }

        private sealed class LambdaCaptureCollector : BoundTreeRewriter
        {
            private readonly MethodSymbol _lambdaMethod;
            private readonly HashSet<Symbol> _seen = new(ReferenceEqualityComparer<Symbol>.Instance);
            private readonly ImmutableArray<Symbol>.Builder _captures = ImmutableArray.CreateBuilder<Symbol>();

            private LambdaCaptureCollector(MethodSymbol lambdaMethod)
            {
                _lambdaMethod = lambdaMethod;
            }

            public static ImmutableArray<Symbol> Collect(MethodSymbol lambdaMethod, BoundStatement body)
            {
                var c = new LambdaCaptureCollector(lambdaMethod);
                c.RewriteStatement(body);
                return c._captures.ToImmutable();
            }

            protected override BoundExpression RewriteExpression(BoundExpression node)
            {
                switch (node)
                {
                    case BoundLocalExpression local when !ReferenceEquals(local.Local.ContainingSymbol, _lambdaMethod):
                        Add(local.Local);
                        return node;

                    case BoundParameterExpression parameter when !ReferenceEquals(parameter.Parameter.ContainingSymbol, _lambdaMethod):
                        Add(parameter.Parameter);
                        return node;

                    case BoundThisExpression:
                    case BoundBaseExpression:
                        throw new NotSupportedException("Capturing 'this' in lambdas is not implemented by this closure lowering pass yet.");

                    default:
                        return base.RewriteExpression(node);
                }
            }

            private void Add(Symbol symbol)
            {
                if (symbol is LocalSymbol { IsConst: true })
                    return;

                if (_seen.Add(symbol))
                    _captures.Add(symbol);
            }
        }

        private sealed class OwnCapturedSymbolCollector : BoundTreeRewriter
        {
            private readonly MethodSymbol _owner;
            private readonly HashSet<Symbol> _seen = new(ReferenceEqualityComparer<Symbol>.Instance);
            private readonly ImmutableArray<Symbol>.Builder _symbols = ImmutableArray.CreateBuilder<Symbol>();

            private OwnCapturedSymbolCollector(MethodSymbol owner)
            {
                _owner = owner;
            }

            public static ImmutableArray<Symbol> Collect(MethodSymbol owner, BoundStatement body)
            {
                var c = new OwnCapturedSymbolCollector(owner);
                c.RewriteStatement(body);
                return c._symbols.ToImmutable();
            }

            protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
            {
                var captures = LambdaCaptureCollector.Collect(node.Method, node.Body);
                for (int i = 0; i < captures.Length; i++)
                {
                    var s = captures[i];
                    if (ReferenceEquals(s.ContainingSymbol, _owner) && _seen.Add(s))
                        _symbols.Add(s);
                }

                return base.RewriteLambdaExpression(node);
            }
        }

        private readonly struct SavedCell
        {
            public readonly Symbol Symbol;
            public readonly bool HadOld;
            public readonly BoundExpression? Old;

            public SavedCell(Symbol symbol, bool hadOld, BoundExpression? old)
            {
                Symbol = symbol;
                HadOld = hadOld;
                Old = old;
            }
        }

        public LambdaClosureRewriter(Compilation compilation)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            _objectType = (NamedTypeSymbol)_compilation.GetSpecialType(SpecialType.System_Object);
        }

        protected override BoundMethodBody RewriteMethodBody(BoundMethodBody node)
        {
            var body = RewriteMethodLike(node.Syntax, node.Method, node.Body, closureParameterOpt: null, externalCaptures: ImmutableArray<Symbol>.Empty, out _);
            if (!ReferenceEquals(body, node.Body))
                return new BoundMethodBody(node.Syntax, node.Method, body);

            return node;
        }

        protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
        {
            var externalCaptures = LambdaCaptureCollector.Collect(node.Method, node.Body);
            if (node.IsStatic && !externalCaptures.IsDefaultOrEmpty)
                throw new NotSupportedException("Static lambdas cannot capture locals or parameters.");

            ParameterSymbol? closureParameter = null;
            MethodSymbol targetMethod = node.Method;
            BoundExpression? target = node.TargetOpt;

            if (!externalCaptures.IsDefaultOrEmpty)
            {
                var closureMethod = new ClosureLambdaMethodSymbol(node.Method);
                closureParameter = new ParameterSymbol(
                    name: "<>closure",
                    containing: closureMethod,
                    type: _objectType,
                    locations: ImmutableArray<Location>.Empty);

                var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(node.Method.Parameters.Length + 1);
                parameters.Add(closureParameter);
                parameters.AddRange(node.Method.Parameters);
                closureMethod.SetParameters(parameters.ToImmutable());
                targetMethod = closureMethod;

                var cells = ImmutableArray.CreateBuilder<BoundExpression>(externalCaptures.Length);
                for (int i = 0; i < externalCaptures.Length; i++)
                {
                    if (!_cellByCapturedSymbol.TryGetValue(externalCaptures[i], out var cell))
                    {
                        throw new NotSupportedException(
                            $"Cannot build closure for captured symbol '{externalCaptures[i].Name}'. The defining scope has not been closure-lowered.");
                    }
                    cells.Add(cell);
                }

                target = new BoundClosureCreationExpression(node.Syntax, _objectType, cells.ToImmutable());
            }

            var body = RewriteMethodLike(node.Syntax, node.Method, node.Body, closureParameter, externalCaptures, out _);

            if (!ReferenceEquals(body, node.Body) || !ReferenceEquals(targetMethod, node.Method) || !ReferenceEquals(target, node.TargetOpt))
            {
                return new BoundLambdaExpression(
                    (ExpressionSyntax)node.Syntax,
                    (NamedTypeSymbol)node.Type,
                    targetMethod,
                    node.InvokeMethod,
                    body,
                    node.IsStatic,
                    node.IsAsync,
                    target);
            }

            return node;
        }
        protected override BoundStatement RewriteLocalFunctionStatement(BoundLocalFunctionStatement node)
        {
            var body = RewriteMethodLike(
                node.Syntax,
                node.LocalFunction,
                node.Body,
                closureParameterOpt: null,
                externalCaptures: ImmutableArray<Symbol>.Empty,
                out var bodyChanged);

            if (bodyChanged || !ReferenceEquals(body, node.Body))
            {
                return new BoundLocalFunctionStatement(
                    (LocalFunctionStatementSyntax)node.Syntax,
                    node.LocalFunction,
                    body);
            }

            return node;
        }
        protected override BoundStatement RewriteLocalDeclarationStatement(BoundLocalDeclarationStatement node)
        {
            if (_cellByCapturedSymbol.TryGetValue(node.Local, out var cell) && cell is BoundLocalExpression cellLocal)
            {
                var init = node.Initializer is null
                    ? MakeDefaultValue(node.Syntax, node.Local.Type)
                    : RewriteExpression(node.Initializer);

                var cellInit = new BoundClosureCellCreationExpression(node.Syntax, _objectType, node.Local.Type, init);
                return new BoundLocalDeclarationStatement(node.Syntax, cellLocal.Local, cellInit);
            }

            return base.RewriteLocalDeclarationStatement(node);
        }

        protected override BoundExpression RewriteExpression(BoundExpression node)
        {
            switch (node)
            {
                case BoundLocalExpression local when _cellByCapturedSymbol.TryGetValue(local.Local, out var localCell):
                    return new BoundClosureAccessExpression(local.Syntax, local.Local.Type, localCell);

                case BoundParameterExpression parameter when _cellByCapturedSymbol.TryGetValue(parameter.Parameter, out var parameterCell):
                    return new BoundClosureAccessExpression(parameter.Syntax, parameter.Parameter.Type, parameterCell);

                default:
                    return base.RewriteExpression(node);
            }
        }

        private BoundStatement RewriteMethodLike(
            SyntaxNode syntax,
            MethodSymbol method,
            BoundStatement body,
            ParameterSymbol? closureParameterOpt,
            ImmutableArray<Symbol> externalCaptures,
            out bool changed)
        {
            changed = false;
            var saved = new List<SavedCell>();
            var prologue = ImmutableArray.CreateBuilder<BoundStatement>();

            void PushCell(Symbol symbol, BoundExpression cell)
            {
                bool hadOld = _cellByCapturedSymbol.TryGetValue(symbol, out var old);
                saved.Add(new SavedCell(symbol, hadOld, old));
                _cellByCapturedSymbol[symbol] = cell;
            }

            try
            {
                if (closureParameterOpt is not null)
                {
                    var closureExpr = new BoundParameterExpression(syntax, closureParameterOpt);
                    for (int i = 0; i < externalCaptures.Length; i++)
                    {
                        PushCell(
                            externalCaptures[i],
                            new BoundClosureSlotExpression(syntax, _objectType, closureExpr, i));
                    }
                }

                var ownCaptured = OwnCapturedSymbolCollector.Collect(method, body);
                for (int i = 0; i < ownCaptured.Length; i++)
                {
                    var symbol = ownCaptured[i];
                    var cellLocal = new LocalSymbol(
                        name: $"<>cell{_tempId++}_{symbol.Name}",
                        containing: method,
                        type: _objectType,
                        locations: ImmutableArray<Location>.Empty);
                    var cellExpr = new BoundLocalExpression(syntax, cellLocal);
                    PushCell(symbol, cellExpr);

                    if (symbol is ParameterSymbol parameter)
                    {
                        var init = new BoundParameterExpression(syntax, parameter);
                        var cellInit = new BoundClosureCellCreationExpression(syntax, _objectType, parameter.Type, init);
                        prologue.Add(new BoundLocalDeclarationStatement(syntax, cellLocal, cellInit));
                    }
                }

                var rewrittenBody = RewriteStatement(body);

                if (prologue.Count != 0)
                {
                    if (rewrittenBody is BoundBlockStatement block)
                    {
                        var statements = ImmutableArray.CreateBuilder<BoundStatement>(prologue.Count + block.Statements.Length);
                        statements.AddRange(prologue);
                        statements.AddRange(block.Statements);
                        rewrittenBody = new BoundBlockStatement(block.Syntax, statements.ToImmutable());
                    }
                    else
                    {
                        var statements = ImmutableArray.CreateBuilder<BoundStatement>(prologue.Count + 1);
                        statements.AddRange(prologue);
                        statements.Add(rewrittenBody);
                        rewrittenBody = new BoundBlockStatement(syntax, statements.ToImmutable());
                    }
                }

                changed = !ReferenceEquals(rewrittenBody, body) || prologue.Count != 0 || closureParameterOpt is not null;
                return rewrittenBody;
            }
            finally
            {
                for (int i = saved.Count - 1; i >= 0; i--)
                {
                    var item = saved[i];
                    if (item.HadOld)
                        _cellByCapturedSymbol[item.Symbol] = item.Old!;
                    else
                        _cellByCapturedSymbol.Remove(item.Symbol);
                }
            }
        }

        private BoundExpression MakeDefaultValue(SyntaxNode syntax, TypeSymbol type)
        {
            if (type.IsReferenceType || type is PointerTypeSymbol || type is ByRefTypeSymbol)
                return new BoundLiteralExpression(syntax, NullTypeSymbol.Instance, null);

            return type.SpecialType switch
            {
                SpecialType.System_Boolean => new BoundLiteralExpression(syntax, type, false),
                SpecialType.System_Char => new BoundLiteralExpression(syntax, type, '\0'),
                SpecialType.System_Int8 => new BoundLiteralExpression(syntax, type, (sbyte)0),
                SpecialType.System_UInt8 => new BoundLiteralExpression(syntax, type, (byte)0),
                SpecialType.System_Int16 => new BoundLiteralExpression(syntax, type, (short)0),
                SpecialType.System_UInt16 => new BoundLiteralExpression(syntax, type, (ushort)0),
                SpecialType.System_Int32 => new BoundLiteralExpression(syntax, type, 0),
                SpecialType.System_UInt32 => new BoundLiteralExpression(syntax, type, 0u),
                SpecialType.System_Int64 => new BoundLiteralExpression(syntax, type, 0L),
                SpecialType.System_UInt64 => new BoundLiteralExpression(syntax, type, 0UL),
                SpecialType.System_Single => new BoundLiteralExpression(syntax, type, 0f),
                SpecialType.System_Double => new BoundLiteralExpression(syntax, type, 0d),
                _ when type is NamedTypeSymbol named && named.IsValueType
                    => new BoundObjectCreationExpression(syntax, named, constructorOpt: null, ImmutableArray<BoundExpression>.Empty),
                _ => new BoundLiteralExpression(syntax, type, 0),
            };
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

            protected override BoundExpression RewriteLambdaExpression(BoundLambdaExpression node)
            {
                if (node.TargetOpt is not null)
                    RewriteExpression(node.TargetOpt);
                return node;
            }

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

                case BoundLambdaExpression lambda:
                    return RewriteClosedLambdaExpression(lambda);

                default:
                    return base.RewriteExpression(node);
            }
        }

        private BoundExpression RewriteClosedLambdaExpression(BoundLambdaExpression node)
        {
            var target = node.TargetOpt is null ? null : RewriteExpression(node.TargetOpt);
            if (!ReferenceEquals(target, node.TargetOpt))
            {
                return new BoundLambdaExpression(
                    (ExpressionSyntax)node.Syntax,
                    (NamedTypeSymbol)node.Type,
                    node.Method,
                    node.InvokeMethod,
                    node.Body,
                    node.IsStatic,
                    node.IsAsync,
                    target);
            }
            return node;
        }

        private BoundExpression RewriteCallExpressionWithCaptures(BoundCallExpression node)
        {
            var receiver = node.ReceiverOpt is null ? null : RewriteExpression(node.ReceiverOpt);
            var arguments = RewriteExpressions(node.Arguments, out var argsChanged);
            var localFunction = node.Method.OriginalDefinition as LocalFunctionSymbol;
            if (localFunction != null && TryGetCaptureInfo(localFunction, out var info))
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
                var targetMethod = RewriteLocalFunctionCallTarget(node.Method, localFunction, info.Lowered);
                if (!ReferenceEquals(receiver, node.ReceiverOpt) ||
                    argsChanged ||
                    !ReferenceEquals(targetMethod, node.Method) ||
                    rewrittenArguments.Length != node.Arguments.Length)
                {
                    return new BoundCallExpression(node.Syntax, receiver, targetMethod, rewrittenArguments);
                }

                return node;
            }

            if (!ReferenceEquals(receiver, node.ReceiverOpt) || argsChanged)
                return new BoundCallExpression(node.Syntax, receiver, node.Method, arguments);

            return node;
        }
        private MethodSymbol RewriteLocalFunctionCallTarget(
            MethodSymbol method,
            LocalFunctionSymbol original,
            LocalFunctionSymbol lowered)
        {
            if (ReferenceEquals(method, original))
                return lowered;

            if (method is ConstructedMethodSymbol constructed &&
                ReferenceEquals(constructed.OriginalDefinition, original))
            {
                return new ConstructedMethodSymbol(
                    lowered,
                    constructed.TypeArguments,
                    _compilation.TypeManager);
            }

            return lowered;
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
            lowered.SetTypeParameters(original.TypeParameters);

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
}
