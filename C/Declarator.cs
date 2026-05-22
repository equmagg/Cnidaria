using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.C
{

    public enum StorageClass : byte
    {
        None,
        Typedef,
        Extern,
        Static,
        Auto,
        Register,
        ThreadLocal
    }

    [Flags]
    public enum FunctionSpecifiers : byte
    {
        None = 0,
        Inline = 1,
        NoReturn = 2
    }




    internal readonly struct DeclarationSpecifiers
    {
        public QualifiedType BaseType { get; }
        public StorageClass StorageClass { get; }
        public TypeQualifiers Qualifiers { get; }
        public FunctionSpecifiers FunctionSpecifiers { get; }

        public bool IsTypedef => StorageClass == StorageClass.Typedef;

        public DeclarationSpecifiers(
            QualifiedType baseType,
            StorageClass storageClass,
            TypeQualifiers qualifiers,
            FunctionSpecifiers functionSpecifiers)
        {
            BaseType = baseType;
            StorageClass = storageClass;
            Qualifiers = qualifiers;
            FunctionSpecifiers = functionSpecifiers;
        }
    }
    internal sealed class DeclarationCollector
    {
        private readonly Compilation _compilation;
        private readonly TypeCatalog _types = TypeCatalog.Instance;
        private readonly List<SemanticDiagnostic> _diagnostics = new();
        private readonly Dictionary<SyntaxNode, Symbol> _declaredSymbols = new();
        private readonly Dictionary<ExpressionSyntax, Symbol> _referencedSymbols = new();
        private readonly Dictionary<SyntaxNode, Scope> _scopes = new();

        private DeclarationCollector(Compilation compilation)
        {
            _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        }

        public static SemanticState Collect(Compilation compilation)
        {
            var collector = new DeclarationCollector(compilation);
            var globalScope = new Scope(parent: null, declaringSyntax: null);

            foreach (var tree in compilation.SyntaxTrees)
                collector.CollectTranslationUnit(tree.Root, globalScope);

            return new SemanticState(
                globalScope,
                collector._declaredSymbols,
                collector._referencedSymbols,
                collector._scopes,
                collector._diagnostics.ToImmutableArray());
        }

        private void CollectTranslationUnit(TranslationUnitSyntax root, Scope globalScope)
        {
            _scopes[root] = globalScope;

            foreach (var member in root.Members)
            {
                switch (member)
                {
                    case DeclarationSyntax declaration:
                        CollectDeclaration(declaration, globalScope);
                        break;

                    case FunctionDefinitionSyntax functionDefinition:
                        CollectFunctionDefinition(functionDefinition, globalScope);
                        break;

                    case StaticAssertDeclarationSyntax staticAssert:
                        _scopes[staticAssert] = globalScope;
                        VisitExpression(staticAssert.Condition, globalScope);
                        if (staticAssert.Message is not null)
                            VisitExpression(staticAssert.Message, globalScope);
                        break;

                    default:
                        _scopes[member] = globalScope;
                        break;
                }
            }
        }

        private void CollectFunctionDefinition(FunctionDefinitionSyntax functionDefinition, Scope scope)
        {
            _scopes[functionDefinition] = scope;

            var specifiers = DeclarationTypeParser.ParseSpecifiers(functionDefinition.Specifiers, scope, _types);
            var type = DeclaratorTypeBuilder.Build(functionDefinition.Declarator, specifiers.BaseType, _types, scope);

            if (functionDefinition.Declarator.Identifier is null)
                return;

            var name = functionDefinition.Declarator.Identifier.Value.Text;
            var symbol = new FunctionSymbol(
                name,
                EnsureFunctionType(type),
                specifiers.StorageClass,
                specifiers.FunctionSpecifiers,
                isDefinition: true,
                functionDefinition,
                GetRuntimeIntrinsicKind(name));

            DeclareOrdinary(scope, symbol);
            _declaredSymbols[functionDefinition] = symbol;
            _declaredSymbols[functionDefinition.Declarator] = symbol;

            var functionScope = new Scope(scope, functionDefinition);
            DeclareParameters(symbol.FunctionType, functionScope);
            _scopes[functionDefinition.Body] = functionScope;
            CollectCompoundStatement(functionDefinition.Body, functionScope);
        }

        private void DeclareParameters(FunctionType? functionType, Scope functionScope)
        {
            if (functionType is null)
                return;

            foreach (var parameter in functionType.Parameters)
            {
                if (string.IsNullOrEmpty(parameter.Name))
                    continue;

                DeclareOrdinary(functionScope, parameter);
            }
        }

        private QualifiedType EnsureFunctionType(QualifiedType type)
        {
            if (type.Type is FunctionType)
                return type;

            return new QualifiedType(
                _types.FunctionReturning(
                    type,
                    ImmutableArray<ParameterSymbol>.Empty,
                    hasPrototype: false,
                    isVariadic: false));
        }
        private static RuntimeIntrinsicKind GetRuntimeIntrinsicKind(string name)
        {
            if (string.Equals(name, StandardHeaders.PrintfIntrinsicName, StringComparison.Ordinal))
                return RuntimeIntrinsicKind.CStringWrite;

            if (string.Equals(name, StandardHeaders.MallocIntrinsicName, StringComparison.Ordinal))
                return RuntimeIntrinsicKind.Malloc;

            if (string.Equals(name, StandardHeaders.FreeIntrinsicName, StringComparison.Ordinal))
                return RuntimeIntrinsicKind.Free;

            if (string.Equals(name, StandardHeaders.BuiltinVaStartName, StringComparison.Ordinal))
                return RuntimeIntrinsicKind.BuiltinVaStart;

            return RuntimeIntrinsicKind.None;
        }
        private void CollectDeclaration(DeclarationSyntax declaration, Scope scope)
        {
            _scopes[declaration] = scope;

            var specifiers = DeclarationTypeParser.ParseSpecifiers(declaration.Specifiers, scope, _types);

            if (declaration.Declarators.Length == 0)
                return;

            foreach (var initDeclarator in declaration.Declarators)
                CollectInitDeclarator(initDeclarator, declaration, specifiers, scope);

            if (declaration.Declarators.Length == 1 &&
                _declaredSymbols.TryGetValue(declaration.Declarators[0], out var singleSymbol))
            {
                _declaredSymbols[declaration] = singleSymbol;
            }
        }

        private void CollectInitDeclarator(
            InitDeclaratorSyntax initDeclarator,
            DeclarationSyntax declaration,
            DeclarationSpecifiers specifiers,
            Scope scope)
        {
            _scopes[initDeclarator] = scope;
            _scopes[initDeclarator.Declarator] = scope;

            var identifier = initDeclarator.Declarator.Identifier;
            if (identifier is null)
                return;

            var name = identifier.Value.Text;
            var declaredType = DeclaratorTypeBuilder.Build(initDeclarator.Declarator, specifiers.BaseType, _types, scope);

            Symbol symbol;
            if (specifiers.IsTypedef)
            {
                symbol = new TypeAliasSymbol(name, declaredType, initDeclarator);
            }
            else if (declaredType.Type is FunctionType)
            {
                symbol = new FunctionSymbol(
                    name,
                    declaredType,
                    specifiers.StorageClass,
                    specifiers.FunctionSpecifiers,
                    isDefinition: false,
                    initDeclarator,
                    GetRuntimeIntrinsicKind(name));
            }
            else
            {
                symbol = new VariableSymbol(
                    name,
                    declaredType,
                    specifiers.StorageClass,
                    initDeclarator);
            }

            DeclareOrdinary(scope, symbol);
            _declaredSymbols[initDeclarator] = symbol;
            _declaredSymbols[initDeclarator.Declarator] = symbol;

            if (initDeclarator.Initializer is not null)
                VisitInitializer(initDeclarator.Initializer, scope);
        }

        private void CollectCompoundStatement(CompoundStatementSyntax statement, Scope scope)
        {
            _scopes[statement] = scope;

            foreach (var member in statement.Members)
            {
                switch (member)
                {
                    case DeclarationSyntax declaration:
                        CollectDeclaration(declaration, scope);
                        break;

                    case StaticAssertDeclarationSyntax staticAssert:
                        _scopes[staticAssert] = scope;
                        VisitExpression(staticAssert.Condition, scope);
                        if (staticAssert.Message is not null)
                            VisitExpression(staticAssert.Message, scope);
                        break;

                    case StatementSyntax childStatement:
                        CollectStatement(childStatement, scope);
                        break;

                    default:
                        _scopes[member] = scope;
                        break;
                }
            }
        }

        private void CollectStatement(StatementSyntax statement, Scope scope)
        {
            _scopes[statement] = scope;

            switch (statement)
            {
                case CompoundStatementSyntax compound:
                    CollectCompoundStatement(compound, new Scope(scope, compound));
                    break;

                case IfStatementSyntax ifStatement:
                    VisitExpression(ifStatement.Condition, scope);
                    CollectStatement(ifStatement.ThenStatement, scope);
                    if (ifStatement.ElseStatement is not null)
                        CollectStatement(ifStatement.ElseStatement, scope);
                    break;

                case SwitchStatementSyntax switchStatement:
                    VisitExpression(switchStatement.Expression, scope);
                    CollectStatement(switchStatement.Statement, scope);
                    break;

                case WhileStatementSyntax whileStatement:
                    VisitExpression(whileStatement.Condition, scope);
                    CollectStatement(whileStatement.Statement, scope);
                    break;

                case DoStatementSyntax doStatement:
                    CollectStatement(doStatement.Statement, scope);
                    VisitExpression(doStatement.Condition, scope);
                    break;

                case ForStatementSyntax forStatement:
                    CollectForStatement(forStatement, scope);
                    break;

                case LabelStatementSyntax labelStatement:
                    DeclareLabel(scope, new LabelSymbol(labelStatement.IdentifierToken.Text, labelStatement));
                    CollectStatement(labelStatement.Statement, scope);
                    break;

                case CaseStatementSyntax caseStatement:
                    VisitExpression(caseStatement.Expression, scope);
                    CollectStatement(caseStatement.Statement, scope);
                    break;

                case DefaultStatementSyntax defaultStatement:
                    CollectStatement(defaultStatement.Statement, scope);
                    break;

                case ReturnStatementSyntax returnStatement:
                    if (returnStatement.Expression is not null)
                        VisitExpression(returnStatement.Expression, scope);
                    break;

                case ExpressionStatementSyntax expressionStatement:
                    if (expressionStatement.Expression is not null)
                        VisitExpression(expressionStatement.Expression, scope);
                    break;
            }
        }

        private void CollectForStatement(ForStatementSyntax forStatement, Scope parentScope)
        {
            var forScope = new Scope(parentScope, forStatement);
            _scopes[forStatement] = forScope;

            if (forStatement.Initializer is DeclarationSyntax declaration)
                CollectDeclaration(declaration, forScope);
            else if (forStatement.Initializer is ExpressionSyntax initializerExpression)
                VisitExpression(initializerExpression, forScope);

            if (forStatement.Condition is not null)
                VisitExpression(forStatement.Condition, forScope);

            if (forStatement.Increment is not null)
                VisitExpression(forStatement.Increment, forScope);

            CollectStatement(forStatement.Statement, forScope);
        }

        private void VisitExpression(ExpressionSyntax expression, Scope scope)
        {
            _scopes[expression] = scope;

            switch (expression)
            {
                case NameExpressionSyntax nameExpression:
                    {
                        var symbol = scope.LookupOrdinary(nameExpression.IdentifierToken.Text);
                        if (symbol is not null)
                            _referencedSymbols[nameExpression] = symbol;
                        break;
                    }

                case UnaryExpressionSyntax unary:
                    VisitExpression(unary.Operand, scope);
                    break;

                case BinaryExpressionSyntax binary:
                    VisitExpression(binary.Left, scope);
                    VisitExpression(binary.Right, scope);
                    break;

                case AssignmentExpressionSyntax assignment:
                    VisitExpression(assignment.Left, scope);
                    VisitExpression(assignment.Right, scope);
                    break;

                case ConditionalExpressionSyntax conditional:
                    VisitExpression(conditional.Condition, scope);
                    VisitExpression(conditional.WhenTrue, scope);
                    VisitExpression(conditional.WhenFalse, scope);
                    break;

                case CastExpressionSyntax cast:
                    VisitExpression(cast.Expression, scope);
                    break;

                case SizeofExpressionSyntax sizeofExpression:
                    if (sizeofExpression.Expression is not null)
                        VisitExpression(sizeofExpression.Expression, scope);
                    break;

                case ParenthesizedExpressionSyntax parenthesized:
                    VisitExpression(parenthesized.Expression, scope);
                    break;

                case CompoundLiteralExpressionSyntax compoundLiteral:
                    if (compoundLiteral.InitializerList is not null)
                        VisitInitializer(compoundLiteral.InitializerList, scope);
                    break;

                case GenericSelectionExpressionSyntax generic:
                    VisitExpression(generic.ControlExpression, scope);
                    foreach (var association in generic.Associations)
                        VisitExpression(association.Expression, scope);
                    break;

                case StatementExpressionSyntax statementExpression:
                    CollectCompoundStatement(statementExpression.Statement, new Scope(scope, statementExpression));
                    break;

                case CallExpressionSyntax call:
                    VisitExpression(call.Expression, scope);
                    foreach (var argument in call.Arguments)
                        VisitExpression(argument, scope);
                    break;

                case ElementAccessExpressionSyntax elementAccess:
                    VisitExpression(elementAccess.Expression, scope);
                    if (elementAccess.Index is not null)
                        VisitExpression(elementAccess.Index, scope);
                    break;

                case MemberAccessExpressionSyntax memberAccess:
                    VisitExpression(memberAccess.Expression, scope);
                    break;

                case PostfixUnaryExpressionSyntax postfix:
                    VisitExpression(postfix.Expression, scope);
                    break;
            }
        }

        private void VisitInitializer(InitializerSyntax initializer, Scope scope)
        {
            _scopes[initializer] = scope;

            switch (initializer)
            {
                case ExpressionInitializerSyntax expressionInitializer:
                    VisitExpression(expressionInitializer.Expression, scope);
                    break;

                case InitializerListSyntax initializerList:
                    foreach (var item in initializerList.Items)
                    {
                        _scopes[item] = scope;

                        foreach (var designator in item.Designators)
                        {
                            _scopes[designator] = scope;

                            if (designator is ArrayDesignatorSyntax arrayDesignator)
                                VisitExpression(arrayDesignator.Expression, scope);
                        }

                        VisitInitializer(item.Initializer, scope);
                    }
                    break;
            }
        }

        private void DeclareOrdinary(Scope scope, Symbol symbol)
        {
            if (!scope.TryDeclareOrdinary(symbol, out var existing))
            {
                if (existing is FunctionSymbol existingFunction &&
                    existingFunction.IsIntrinsic &&
                    symbol is FunctionSymbol)
                {
                    scope.ReplaceOrdinary(symbol);
                }
            }
        }

        private void DeclareLabel(Scope scope, LabelSymbol symbol)
        {
            if (!scope.TryDeclareLabel(symbol, out _))
            {
                _diagnostics.Add(SemanticDiagnostic.Error(
                    "Duplicate label '" + symbol.Name + "'.",
                    GetDeclarationSpan(symbol.DeclaringSyntax)));
            }

            if (symbol.DeclaringSyntax is not null)
                _declaredSymbols[symbol.DeclaringSyntax] = symbol;
        }

        private static TextSpan GetDeclarationSpan(SyntaxNode? syntax)
        {
            switch (syntax)
            {
                case LabelStatementSyntax label:
                    return label.IdentifierToken.Span;
                case InitDeclaratorSyntax init when init.Declarator.Identifier.HasValue:
                    return init.Declarator.Identifier.Value.Span;
                case FunctionDefinitionSyntax function when function.Declarator.Identifier.HasValue:
                    return function.Declarator.Identifier.Value.Span;
                default:
                    return new TextSpan(0, 0);
            }
        }
    }
    internal sealed class DeclarationTypeParser
    {
        private readonly ImmutableArray<SyntaxToken> _tokens;
        private readonly Scope _scope;
        private readonly TypeCatalog _types;

        private DeclarationTypeParser(
            ImmutableArray<SyntaxToken> tokens,
            Scope scope,
            TypeCatalog types)
        {
            _tokens = tokens;
            _scope = scope;
            _types = types;
        }

        public static DeclarationSpecifiers ParseSpecifiers(
            ImmutableArray<SyntaxToken> tokens,
            Scope scope,
            TypeCatalog types)
        {
            var parser = new DeclarationTypeParser(tokens, scope, types);
            return parser.Parse();
        }

        private DeclarationSpecifiers Parse()
        {
            var storageClass = StorageClass.None;
            var qualifiers = TypeQualifiers.None;
            var functionSpecifiers = FunctionSpecifiers.None;

            var sawVoid = false;
            var sawBool = false;
            var sawChar = false;
            var sawShort = false;
            var longCount = 0;
            var sawSigned = false;
            var sawUnsigned = false;
            var sawInt = false;
            var sawFloat = false;
            var sawDouble = false;
            QualifiedType? namedBaseType = null;

            for (var i = 0; i < _tokens.Length; i++)
            {
                var token = _tokens[i];

                switch (token.Kind)
                {
                    case SyntaxKind.TypedefKeyword:
                        storageClass = StorageClass.Typedef;
                        break;

                    case SyntaxKind.ExternKeyword:
                        storageClass = StorageClass.Extern;
                        break;

                    case SyntaxKind.StaticKeyword:
                        storageClass = StorageClass.Static;
                        break;

                    case SyntaxKind.AutoKeyword:
                        storageClass = StorageClass.Auto;
                        break;

                    case SyntaxKind.RegisterKeyword:
                        storageClass = StorageClass.Register;
                        break;

                    case SyntaxKind.ThreadLocalKeyword:
                    case SyntaxKind.UnderscoreThreadLocalKeyword:
                        storageClass = StorageClass.ThreadLocal;
                        break;

                    case SyntaxKind.ConstKeyword:
                    case SyntaxKind.ConstExtensionKeyword:
                        qualifiers |= TypeQualifiers.Const;
                        break;

                    case SyntaxKind.VolatileKeyword:
                    case SyntaxKind.VolatileExtensionKeyword:
                        qualifiers |= TypeQualifiers.Volatile;
                        break;

                    case SyntaxKind.RestrictKeyword:
                    case SyntaxKind.RestrictExtensionKeyword:
                        qualifiers |= TypeQualifiers.Restrict;
                        break;

                    case SyntaxKind.AtomicKeyword:
                    case SyntaxKind.UnderscoreAtomicKeyword:
                        qualifiers |= TypeQualifiers.Atomic;
                        break;

                    case SyntaxKind.InlineKeyword:
                    case SyntaxKind.InlineExtensionKeyword:
                        functionSpecifiers |= FunctionSpecifiers.Inline;
                        break;

                    case SyntaxKind.UnderscoreNoreturnKeyword:
                        functionSpecifiers |= FunctionSpecifiers.NoReturn;
                        break;

                    case SyntaxKind.VoidKeyword:
                        sawVoid = true;
                        break;

                    case SyntaxKind.BoolKeyword:
                    case SyntaxKind.UnderscoreBoolKeyword:
                        sawBool = true;
                        break;

                    case SyntaxKind.CharKeyword:
                        sawChar = true;
                        break;

                    case SyntaxKind.ShortKeyword:
                        sawShort = true;
                        break;

                    case SyntaxKind.LongKeyword:
                        longCount++;
                        break;

                    case SyntaxKind.SignedKeyword:
                        sawSigned = true;
                        break;

                    case SyntaxKind.UnsignedKeyword:
                        sawUnsigned = true;
                        break;

                    case SyntaxKind.IntKeyword:
                        sawInt = true;
                        break;

                    case SyntaxKind.FloatKeyword:
                        sawFloat = true;
                        break;

                    case SyntaxKind.DoubleKeyword:
                        sawDouble = true;
                        break;

                    case SyntaxKind.TypedefNameToken:
                        if (namedBaseType is null)
                            namedBaseType = ResolveTypedefName(token);
                        break;

                    case SyntaxKind.StructKeyword:
                    case SyntaxKind.UnionKeyword:
                    case SyntaxKind.EnumKeyword:
                        if (namedBaseType is null)
                            namedBaseType = ResolveTagType(token.Kind, i);
                        break;
                }
            }

            if (namedBaseType.HasValue)
            {
                var named = namedBaseType.Value;
                return new DeclarationSpecifiers(
                    new QualifiedType(named.Type, named.Qualifiers | qualifiers),
                    storageClass,
                    qualifiers,
                    functionSpecifiers);
            }

            var builtinKind = SelectBuiltinType(
                sawVoid,
                sawBool,
                sawChar,
                sawShort,
                longCount,
                sawSigned,
                sawUnsigned,
                sawInt,
                sawFloat,
                sawDouble);

            return new DeclarationSpecifiers(
                _types.Builtin(builtinKind, qualifiers),
                storageClass,
                qualifiers,
                functionSpecifiers);
        }

        private QualifiedType ResolveTypedefName(SyntaxToken token)
        {
            if (_scope.LookupOrdinary(token.Text) is TypeAliasSymbol alias)
                return alias.TargetType;

            return new QualifiedType(CErrorType.Instance);
        }

        private QualifiedType ResolveTagType(SyntaxKind keywordKind, int keywordIndex)
        {
            var tagKind = keywordKind == SyntaxKind.StructKeyword
                ? TagKind.Struct
                : keywordKind == SyntaxKind.UnionKeyword
                    ? TagKind.Union
                    : TagKind.Enum;

            var name = FindTagName(keywordIndex);
            if (string.IsNullOrEmpty(name))
                name = "<anonymous@" + _tokens[keywordIndex].Position.ToString() + ">";

            var existing = _scope.LookupTag(name);
            if (existing is null || existing.TagKind != tagKind)
            {
                existing = new TagSymbol(name, tagKind, declaringSyntax: null);
                _scope.TryDeclareTag(existing, out _);
            }

            if (tagKind != TagKind.Enum && TryFindTagBody(keywordIndex, out var bodyTokens))
            {
                var fields = StructUnionFieldParser.ParseFields(
                    bodyTokens,
                    _scope,
                    _types,
                    existing);

                existing.TryDefineFields(fields);
            }

            if (tagKind == TagKind.Enum)
                return new QualifiedType(new EnumType(existing));

            return new QualifiedType(new TagType(existing));
        }

        private string? FindTagName(int keywordIndex)
        {
            for (var i = keywordIndex + 1; i < _tokens.Length; i++)
            {
                var token = _tokens[i];

                if (token.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    return token.Text;

                if (token.Kind == SyntaxKind.OpenBraceToken)
                    return null;
            }

            return null;
        }

        private bool TryFindTagBody(int keywordIndex, out ImmutableArray<SyntaxToken> bodyTokens)
        {
            for (var i = keywordIndex + 1; i < _tokens.Length; i++)
            {
                if (_tokens[i].Kind == SyntaxKind.OpenBraceToken)
                    return TryReadBalancedContent(i, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken, out bodyTokens);

                if (_tokens[i].Kind == SyntaxKind.SemicolonToken ||
                    _tokens[i].Kind == SyntaxKind.CommaToken)
                    break;
            }

            bodyTokens = ImmutableArray<SyntaxToken>.Empty;
            return false;
        }

        private bool TryReadBalancedContent(
            int openIndex,
            SyntaxKind openKind,
            SyntaxKind closeKind,
            out ImmutableArray<SyntaxToken> content)
        {
            var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
            var depth = 0;

            for (var i = openIndex + 1; i < _tokens.Length; i++)
            {
                var token = _tokens[i];

                if (token.Kind == closeKind && depth == 0)
                {
                    content = builder.ToImmutable();
                    return true;
                }

                builder.Add(token);

                if (token.Kind == openKind)
                    depth++;
                else if (token.Kind == closeKind && depth > 0)
                    depth--;
            }

            content = ImmutableArray<SyntaxToken>.Empty;
            return false;
        }

        private static BuiltinTypeKind SelectBuiltinType(
            bool sawVoid,
            bool sawBool,
            bool sawChar,
            bool sawShort,
            int longCount,
            bool sawSigned,
            bool sawUnsigned,
            bool sawInt,
            bool sawFloat,
            bool sawDouble)
        {
            if (sawVoid)
                return BuiltinTypeKind.Void;

            if (sawBool)
                return BuiltinTypeKind.Bool;

            if (sawChar)
            {
                if (sawUnsigned)
                    return BuiltinTypeKind.UnsignedChar;
                if (sawSigned)
                    return BuiltinTypeKind.SignedChar;
                return BuiltinTypeKind.Char;
            }

            if (sawFloat)
                return BuiltinTypeKind.Float;

            if (sawDouble)
                return longCount > 0 ? BuiltinTypeKind.LongDouble : BuiltinTypeKind.Double;

            if (sawShort)
                return sawUnsigned ? BuiltinTypeKind.UnsignedShort : BuiltinTypeKind.Short;

            if (longCount >= 2)
                return sawUnsigned ? BuiltinTypeKind.UnsignedLongLong : BuiltinTypeKind.LongLong;

            if (longCount == 1)
                return sawUnsigned ? BuiltinTypeKind.UnsignedLong : BuiltinTypeKind.Long;

            if (sawUnsigned)
                return BuiltinTypeKind.UnsignedInt;

            return BuiltinTypeKind.Int;
        }
    }
    internal static class StructUnionFieldParser
    {
        public static ImmutableArray<FieldSymbol> ParseFields(
            ImmutableArray<SyntaxToken> bodyTokens,
            Scope scope,
            TypeCatalog types,
            TagSymbol containingTag)
        {
            if (bodyTokens.IsDefaultOrEmpty)
                return ImmutableArray<FieldSymbol>.Empty;

            var fields = ImmutableArray.CreateBuilder<FieldSymbol>();

            foreach (var declarationTokens in SplitTopLevel(bodyTokens, SyntaxKind.SemicolonToken))
                ParseFieldDeclaration(declarationTokens, scope, types, containingTag, fields);

            return fields.ToImmutable();
        }

        private static void ParseFieldDeclaration(
            ImmutableArray<SyntaxToken> declarationTokens,
            Scope scope,
            TypeCatalog types,
            TagSymbol containingTag,
            ImmutableArray<FieldSymbol>.Builder fields)
        {
            if (declarationTokens.IsDefaultOrEmpty)
                return;

            var specifierTokens = ReadSpecifierTokens(declarationTokens, out var declaratorStart);
            if (specifierTokens.Length == 0)
                return;

            var specifiers = DeclarationTypeParser.ParseSpecifiers(specifierTokens, scope, types);
            var declaratorTokens = declarationTokens.Skip(declaratorStart).ToImmutableArray();

            if (declaratorTokens.Length == 0)
            {
                AddAnonymousAggregateField(specifiers.BaseType, containingTag, fields);
                return;
            }

            foreach (var rawDeclarator in SplitTopLevel(declaratorTokens, SyntaxKind.CommaToken))
            {
                var cleanDeclarator = StripFieldSuffix(rawDeclarator);
                var identifier = FindDeclaratorIdentifier(cleanDeclarator);

                if (!identifier.HasValue)
                    continue;

                var type = DeclaratorTypeBuilder.Build(
                    new DeclaratorSyntax(cleanDeclarator, identifier),
                    specifiers.BaseType,
                    types,
                    scope);

                fields.Add(new FieldSymbol(
                    identifier.Value.Text,
                    type,
                    containingTag,
                    fields.Count,
                    declaringSyntax: null));
            }
        }

        private static void AddAnonymousAggregateField(
            QualifiedType type,
            TagSymbol containingTag,
            ImmutableArray<FieldSymbol>.Builder fields)
        {
            if (type.Type.Kind is not TypeKind.Struct and not TypeKind.Union)
                return;

            fields.Add(new FieldSymbol(
                string.Empty,
                type,
                containingTag,
                fields.Count,
                declaringSyntax: null));
        }

        private static ImmutableArray<SyntaxToken> ReadSpecifierTokens(
            ImmutableArray<SyntaxToken> tokens,
            out int nextIndex)
        {
            var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
            var index = 0;

            while (index < tokens.Length && IsDeclarationSpecifierStart(tokens[index].Kind))
            {
                var token = tokens[index];
                builder.Add(token);
                index++;

                if (token.Kind is SyntaxKind.StructKeyword or SyntaxKind.UnionKeyword or SyntaxKind.EnumKeyword)
                {
                    if (index < tokens.Length &&
                        tokens[index].Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    {
                        builder.Add(tokens[index]);
                        index++;
                    }

                    if (index < tokens.Length && tokens[index].Kind == SyntaxKind.OpenBraceToken)
                        ReadBalancedTokenSequence(tokens, builder, ref index, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken);

                    continue;
                }

                if (IsParenthesizedDeclarationSpecifier(token.Kind) &&
                    index < tokens.Length &&
                    tokens[index].Kind == SyntaxKind.OpenParenToken)
                {
                    ReadBalancedTokenSequence(tokens, builder, ref index, SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken);
                }
            }

            nextIndex = index;
            return builder.ToImmutable();
        }

        private static ImmutableArray<ImmutableArray<SyntaxToken>> SplitTopLevel(
            ImmutableArray<SyntaxToken> tokens,
            SyntaxKind separator)
        {
            var result = ImmutableArray.CreateBuilder<ImmutableArray<SyntaxToken>>();
            var current = ImmutableArray.CreateBuilder<SyntaxToken>();
            var parenDepth = 0;
            var bracketDepth = 0;
            var braceDepth = 0;

            foreach (var token in tokens)
            {
                if (token.Kind == separator && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    result.Add(current.ToImmutable());
                    current.Clear();
                    continue;
                }

                current.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;
                    case SyntaxKind.CloseParenToken when parenDepth > 0:
                        parenDepth--;
                        break;
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.OpenBracketDigraphToken:
                        bracketDepth++;
                        break;
                    case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    case SyntaxKind.CloseBracketDigraphToken when bracketDepth > 0:
                        bracketDepth--;
                        break;
                    case SyntaxKind.OpenBraceToken:
                    case SyntaxKind.OpenBraceDigraphToken:
                        braceDepth++;
                        break;
                    case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    case SyntaxKind.CloseBraceDigraphToken when braceDepth > 0:
                        braceDepth--;
                        break;
                }
            }

            if (current.Count > 0)
                result.Add(current.ToImmutable());

            return result.ToImmutable();
        }

        private static ImmutableArray<SyntaxToken> StripFieldSuffix(ImmutableArray<SyntaxToken> tokens)
        {
            var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
            var parenDepth = 0;
            var bracketDepth = 0;
            var braceDepth = 0;

            foreach (var token in tokens)
            {
                if ((token.Kind == SyntaxKind.ColonToken || token.Kind == SyntaxKind.EqualsToken) &&
                    parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    break;
                }

                builder.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;
                    case SyntaxKind.CloseParenToken when parenDepth > 0:
                        parenDepth--;
                        break;
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.OpenBracketDigraphToken:
                        bracketDepth++;
                        break;
                    case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    case SyntaxKind.CloseBracketDigraphToken when bracketDepth > 0:
                        bracketDepth--;
                        break;
                    case SyntaxKind.OpenBraceToken:
                    case SyntaxKind.OpenBraceDigraphToken:
                        braceDepth++;
                        break;
                    case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    case SyntaxKind.CloseBraceDigraphToken when braceDepth > 0:
                        braceDepth--;
                        break;
                }
            }

            return builder.ToImmutable();
        }

        private static SyntaxToken? FindDeclaratorIdentifier(ImmutableArray<SyntaxToken> tokens)
        {
            foreach (var token in tokens)
            {
                if (token.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    return token;
            }

            return null;
        }

        private static void ReadBalancedTokenSequence(
            ImmutableArray<SyntaxToken> tokens,
            ImmutableArray<SyntaxToken>.Builder builder,
            ref int index,
            SyntaxKind openKind,
            SyntaxKind closeKind)
        {
            if (index >= tokens.Length || tokens[index].Kind != openKind)
                return;

            var depth = 0;

            while (index < tokens.Length)
            {
                var token = tokens[index];
                builder.Add(token);
                index++;

                if (token.Kind == openKind)
                    depth++;
                else if (token.Kind == closeKind)
                {
                    depth--;
                    if (depth == 0)
                        return;
                }
            }
        }

        private static bool IsDeclarationSpecifierStart(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.TypedefKeyword or
                SyntaxKind.ExternKeyword or
                SyntaxKind.StaticKeyword or
                SyntaxKind.AutoKeyword or
                SyntaxKind.RegisterKeyword or
                SyntaxKind.ThreadLocalKeyword or
                SyntaxKind.UnderscoreThreadLocalKeyword or

                SyntaxKind.VoidKeyword or
                SyntaxKind.CharKeyword or
                SyntaxKind.ShortKeyword or
                SyntaxKind.IntKeyword or
                SyntaxKind.LongKeyword or
                SyntaxKind.FloatKeyword or
                SyntaxKind.DoubleKeyword or
                SyntaxKind.SignedKeyword or
                SyntaxKind.UnsignedKeyword or
                SyntaxKind.BoolKeyword or
                SyntaxKind.UnderscoreBoolKeyword or
                SyntaxKind.UnderscoreComplexKeyword or
                SyntaxKind.UnderscoreImaginaryKeyword or
                SyntaxKind.UnderscoreBitIntKeyword or
                SyntaxKind.UnderscoreDecimal32Keyword or
                SyntaxKind.UnderscoreDecimal64Keyword or
                SyntaxKind.UnderscoreDecimal128Keyword or

                SyntaxKind.StructKeyword or
                SyntaxKind.UnionKeyword or
                SyntaxKind.EnumKeyword or
                SyntaxKind.TypeofKeyword or
                SyntaxKind.TypeofUnqualKeyword or
                SyntaxKind.TypeofExtensionKeyword or

                SyntaxKind.ConstKeyword or
                SyntaxKind.VolatileKeyword or
                SyntaxKind.RestrictKeyword or
                SyntaxKind.InlineKeyword or
                SyntaxKind.UnderscoreNoreturnKeyword or
                SyntaxKind.ConstexprKeyword or

                SyntaxKind.AlignasKeyword or
                SyntaxKind.UnderscoreAlignasKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or

                SyntaxKind.ConstExtensionKeyword or
                SyntaxKind.VolatileExtensionKeyword or
                SyntaxKind.RestrictExtensionKeyword or
                SyntaxKind.InlineExtensionKeyword or
                SyntaxKind.ExtensionKeyword or
                SyntaxKind.AttributeKeyword or
                SyntaxKind.DeclspecKeyword or

                SyntaxKind.TypedefNameToken => true,

                _ => false,
            };
        }

        private static bool IsParenthesizedDeclarationSpecifier(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.AlignasKeyword or
                SyntaxKind.UnderscoreAlignasKeyword or
                SyntaxKind.AtomicKeyword or
                SyntaxKind.UnderscoreAtomicKeyword or
                SyntaxKind.TypeofKeyword or
                SyntaxKind.TypeofUnqualKeyword or
                SyntaxKind.TypeofExtensionKeyword or
                SyntaxKind.UnderscoreBitIntKeyword or
                SyntaxKind.AttributeKeyword or
                SyntaxKind.DeclspecKeyword => true,

                _ => false,
            };
        }
    }

    internal static class DeclaratorTypeBuilder
    {
        public static QualifiedType Build(
            DeclaratorSyntax declarator,
            QualifiedType baseType,
            TypeCatalog types,
            Scope? scope = null)
        {
            if (declarator is null)
                throw new ArgumentNullException(nameof(declarator));
            if (types is null)
                throw new ArgumentNullException(nameof(types));

            var parser = new DeclaratorParser(declarator.Tokens, types, scope ?? new Scope(parent: null, declaringSyntax: null));
            var node = parser.ParseDeclarator();
            return node.Apply(baseType, types);
        }
    }

    internal sealed class DeclaratorParser
    {
        private readonly ImmutableArray<SyntaxToken> _tokens;
        private readonly TypeCatalog _types;
        private readonly Scope _scope;
        private int _position;

        public DeclaratorParser(ImmutableArray<SyntaxToken> tokens, TypeCatalog types, Scope scope)
        {
            _tokens = tokens;
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        public DeclaratorNode ParseDeclarator()
        {
            if (Current.Kind == SyntaxKind.StarToken)
            {
                NextToken();

                var qualifiers = TypeQualifiers.None;
                while (IsTypeQualifier(Current.Kind))
                {
                    qualifiers |= ToQualifier(Current.Kind);
                    NextToken();
                }

                var inner = ParseDeclarator();
                return new PointerDeclaratorNode(inner, qualifiers);
            }

            return ParseDirectDeclarator();
        }

        private DeclaratorNode ParseDirectDeclarator()
        {
            DeclaratorNode node;

            if (Current.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
            {
                node = new IdentifierDeclaratorNode(NextToken());
            }
            else if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                NextToken();
                node = ParseDeclarator();

                if (Current.Kind == SyntaxKind.CloseParenToken)
                    NextToken();
            }
            else
            {
                node = new MissingDeclaratorNode();
            }

            while (true)
            {
                if (Current.Kind == SyntaxKind.OpenBracketToken)
                {
                    var content = ReadBalancedContent(SyntaxKind.OpenBracketToken, SyntaxKind.CloseBracketToken);
                    node = new ArrayDeclaratorNode(node, TryReadArrayLength(content));
                    continue;
                }

                if (Current.Kind == SyntaxKind.OpenParenToken)
                {
                    var content = ReadBalancedContent(SyntaxKind.OpenParenToken, SyntaxKind.CloseParenToken);
                    var parameters = ParseParameterList(content, out var hasPrototype, out var isVariadic);
                    node = new FunctionDeclaratorNode(
                        node,
                        parameters,
                        hasPrototype,
                        isVariadic);
                    continue;
                }

                return node;
            }
        }

        private ImmutableArray<SyntaxToken> ReadBalancedContent(SyntaxKind openKind, SyntaxKind closeKind)
        {
            if (Current.Kind != openKind)
                return ImmutableArray<SyntaxToken>.Empty;

            NextToken();
            var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
            var depth = 0;

            while (Current.Kind != SyntaxKind.EndOfFileToken &&
                   Current.Kind != SyntaxKind.MissingToken)
            {
                if (Current.Kind == closeKind && depth == 0)
                {
                    NextToken();
                    break;
                }

                var token = NextToken();
                builder.Add(token);

                if (token.Kind == openKind)
                    depth++;
                else if (token.Kind == closeKind && depth > 0)
                    depth--;
            }

            return builder.ToImmutable();
        }

        private long? TryReadArrayLength(ImmutableArray<SyntaxToken> tokens)
        {
            if (tokens.Length != 1)
                return null;

            var token = tokens[0];
            if (token.Kind != SyntaxKind.IntegerLiteralToken)
                return null;

            var text = token.Text.Replace("'", string.Empty);
            var suffixStart = text.Length;
            while (suffixStart > 0 && text[suffixStart - 1] is 'u' or 'U' or 'l' or 'L')
                suffixStart--;

            text = text[..suffixStart];

            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return Convert.ToInt64(text[2..], 16);

                if (text.Length > 1 && text[0] == '0')
                    return Convert.ToInt64(text[1..], 8);

                return Convert.ToInt64(text, 10);
            }
            catch
            {
                return null;
            }
        }

        private ImmutableArray<ParameterSymbol> ParseParameterList(
            ImmutableArray<SyntaxToken> tokens,
            out bool hasPrototype,
            out bool isVariadic)
        {
            hasPrototype = LooksLikePrototype(tokens);
            isVariadic = tokens.Any(static t => t.Kind == SyntaxKind.EllipsisToken);

            if (!hasPrototype || tokens.IsDefaultOrEmpty)
                return ImmutableArray<ParameterSymbol>.Empty;

            if (tokens.Length == 1 && tokens[0].Kind == SyntaxKind.VoidKeyword)
                return ImmutableArray<ParameterSymbol>.Empty;

            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>();
            foreach (var parameterTokens in SplitTopLevel(tokens, SyntaxKind.CommaToken))
            {
                if (parameterTokens.Length == 0 || parameterTokens.Any(static t => t.Kind == SyntaxKind.EllipsisToken))
                    continue;

                var specifierTokens = ReadParameterSpecifierTokens(parameterTokens, out var declaratorStart);
                if (specifierTokens.Length == 0)
                    continue;

                if (specifierTokens.Length == 1 &&
                    specifierTokens[0].Kind == SyntaxKind.VoidKeyword &&
                    declaratorStart >= parameterTokens.Length)
                {
                    continue;
                }

                var specifiers = DeclarationTypeParser.ParseSpecifiers(specifierTokens, _scope, _types);
                var declaratorTokens = parameterTokens.Skip(declaratorStart).ToImmutableArray();
                var identifier = FindDeclaratorIdentifier(declaratorTokens);
                var parameterDeclarator = new DeclaratorSyntax(declaratorTokens, identifier);
                var type = DeclaratorTypeBuilder.Build(parameterDeclarator, specifiers.BaseType, _types, _scope);

                parameters.Add(new ParameterSymbol(
                    identifier?.Text ?? string.Empty,
                    AdjustParameterType(type),
                    declaringSyntax: null));
            }

            return parameters.ToImmutable();
        }

        private QualifiedType AdjustParameterType(QualifiedType type)
        {
            if (type.Type is ArrayType array)
                return new QualifiedType(_types.PointerTo(array.ElementType), type.Qualifiers);

            if (type.Type is FunctionType)
                return new QualifiedType(_types.PointerTo(type), type.Qualifiers);

            return type;
        }

        private static ImmutableArray<SyntaxToken> ReadParameterSpecifierTokens(
            ImmutableArray<SyntaxToken> tokens,
            out int nextIndex)
        {
            var builder = ImmutableArray.CreateBuilder<SyntaxToken>();
            var index = 0;

            while (index < tokens.Length && IsDeclarationSpecifierToken(tokens[index].Kind))
            {
                var token = tokens[index];
                builder.Add(token);
                index++;

                if (token.Kind is SyntaxKind.StructKeyword or SyntaxKind.UnionKeyword or SyntaxKind.EnumKeyword)
                {
                    if (index < tokens.Length &&
                        tokens[index].Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    {
                        builder.Add(tokens[index]);
                        index++;
                    }

                    if (index < tokens.Length && tokens[index].Kind == SyntaxKind.OpenBraceToken)
                        ReadBalancedTokenSequence(tokens, builder, ref index, SyntaxKind.OpenBraceToken, SyntaxKind.CloseBraceToken);
                }
            }

            nextIndex = index;
            return builder.ToImmutable();
        }

        private static ImmutableArray<ImmutableArray<SyntaxToken>> SplitTopLevel(
            ImmutableArray<SyntaxToken> tokens,
            SyntaxKind separator)
        {
            var result = ImmutableArray.CreateBuilder<ImmutableArray<SyntaxToken>>();
            var current = ImmutableArray.CreateBuilder<SyntaxToken>();
            var parenDepth = 0;
            var bracketDepth = 0;
            var braceDepth = 0;

            foreach (var token in tokens)
            {
                if (token.Kind == separator && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    result.Add(current.ToImmutable());
                    current.Clear();
                    continue;
                }

                current.Add(token);

                switch (token.Kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;
                    case SyntaxKind.CloseParenToken when parenDepth > 0:
                        parenDepth--;
                        break;
                    case SyntaxKind.OpenBracketToken:
                    case SyntaxKind.OpenBracketDigraphToken:
                        bracketDepth++;
                        break;
                    case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    case SyntaxKind.CloseBracketDigraphToken when bracketDepth > 0:
                        bracketDepth--;
                        break;
                    case SyntaxKind.OpenBraceToken:
                    case SyntaxKind.OpenBraceDigraphToken:
                        braceDepth++;
                        break;
                    case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    case SyntaxKind.CloseBraceDigraphToken when braceDepth > 0:
                        braceDepth--;
                        break;
                }
            }

            if (current.Count != 0)
                result.Add(current.ToImmutable());

            return result.ToImmutable();
        }

        private static void ReadBalancedTokenSequence(
            ImmutableArray<SyntaxToken> tokens,
            ImmutableArray<SyntaxToken>.Builder builder,
            ref int index,
            SyntaxKind openKind,
            SyntaxKind closeKind)
        {
            if (index >= tokens.Length || tokens[index].Kind != openKind)
                return;

            var depth = 0;
            while (index < tokens.Length)
            {
                var token = tokens[index];
                builder.Add(token);
                index++;

                if (token.Kind == openKind)
                    depth++;
                else if (token.Kind == closeKind)
                {
                    depth--;
                    if (depth == 0)
                        return;
                }
            }
        }

        private static SyntaxToken? FindDeclaratorIdentifier(ImmutableArray<SyntaxToken> tokens)
        {
            foreach (var token in tokens)
            {
                if (token.Kind is SyntaxKind.IdentifierToken or SyntaxKind.TypedefNameToken)
                    return token;
            }

            return null;
        }

        private static bool LooksLikePrototype(ImmutableArray<SyntaxToken> tokens)
        {
            if (tokens.Length == 0)
                return false;

            if (tokens.Length == 1 && tokens[0].Kind == SyntaxKind.VoidKeyword)
                return true;

            return tokens.Any(static t => IsDeclarationSpecifierToken(t.Kind));
        }

        private static bool IsDeclarationSpecifierToken(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.VoidKeyword:
                case SyntaxKind.CharKeyword:
                case SyntaxKind.ShortKeyword:
                case SyntaxKind.IntKeyword:
                case SyntaxKind.LongKeyword:
                case SyntaxKind.FloatKeyword:
                case SyntaxKind.DoubleKeyword:
                case SyntaxKind.SignedKeyword:
                case SyntaxKind.UnsignedKeyword:
                case SyntaxKind.StructKeyword:
                case SyntaxKind.UnionKeyword:
                case SyntaxKind.EnumKeyword:
                case SyntaxKind.TypedefNameToken:
                case SyntaxKind.BoolKeyword:
                case SyntaxKind.UnderscoreBoolKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.ConstExtensionKeyword:
                case SyntaxKind.VolatileKeyword:
                case SyntaxKind.VolatileExtensionKeyword:
                case SyntaxKind.RestrictKeyword:
                case SyntaxKind.RestrictExtensionKeyword:
                case SyntaxKind.AtomicKeyword:
                case SyntaxKind.UnderscoreAtomicKeyword:
                    return true;

                default:
                    return false;
            }
        }

        private SyntaxToken Current
            => _position < _tokens.Length
                ? _tokens[_position]
                : new SyntaxToken(
                    SyntaxKind.EndOfFileToken,
                    0,
                    string.Empty,
                    null,
                    ImmutableArray<SyntaxTrivia>.Empty,
                    ImmutableArray<SyntaxTrivia>.Empty);

        private SyntaxToken NextToken()
        {
            var current = Current;
            if (_position < _tokens.Length)
                _position++;
            return current;
        }

        private static bool IsTypeQualifier(SyntaxKind kind)
            => kind is SyntaxKind.ConstKeyword
                or SyntaxKind.ConstExtensionKeyword
                or SyntaxKind.VolatileKeyword
                or SyntaxKind.VolatileExtensionKeyword
                or SyntaxKind.RestrictKeyword
                or SyntaxKind.RestrictExtensionKeyword
                or SyntaxKind.AtomicKeyword
                or SyntaxKind.UnderscoreAtomicKeyword;

        private static TypeQualifiers ToQualifier(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.ConstExtensionKeyword:
                    return TypeQualifiers.Const;

                case SyntaxKind.VolatileKeyword:
                case SyntaxKind.VolatileExtensionKeyword:
                    return TypeQualifiers.Volatile;

                case SyntaxKind.RestrictKeyword:
                case SyntaxKind.RestrictExtensionKeyword:
                    return TypeQualifiers.Restrict;

                case SyntaxKind.AtomicKeyword:
                case SyntaxKind.UnderscoreAtomicKeyword:
                    return TypeQualifiers.Atomic;

                default:
                    return TypeQualifiers.None;
            }
        }
    }

    internal abstract class DeclaratorNode
    {
        public abstract QualifiedType Apply(QualifiedType baseType, TypeCatalog types);
    }

    internal sealed class IdentifierDeclaratorNode : DeclaratorNode
    {
        public SyntaxToken Identifier { get; }

        public IdentifierDeclaratorNode(SyntaxToken identifier)
        {
            Identifier = identifier;
        }

        public override QualifiedType Apply(QualifiedType baseType, TypeCatalog types)
            => baseType;
    }

    internal sealed class MissingDeclaratorNode : DeclaratorNode
    {
        public override QualifiedType Apply(QualifiedType baseType, TypeCatalog types)
            => baseType;
    }

    internal sealed class PointerDeclaratorNode : DeclaratorNode
    {
        private readonly DeclaratorNode _inner;
        private readonly TypeQualifiers _qualifiers;

        public PointerDeclaratorNode(DeclaratorNode inner, TypeQualifiers qualifiers)
        {
            _inner = inner ?? new MissingDeclaratorNode();
            _qualifiers = qualifiers;
        }

        public override QualifiedType Apply(QualifiedType baseType, TypeCatalog types)
        {
            var pointer = new QualifiedType(types.PointerTo(baseType), _qualifiers);
            return _inner.Apply(pointer, types);
        }
    }

    internal sealed class ArrayDeclaratorNode : DeclaratorNode
    {
        private readonly DeclaratorNode _inner;
        private readonly long? _length;

        public ArrayDeclaratorNode(DeclaratorNode inner, long? length)
        {
            _inner = inner ?? new MissingDeclaratorNode();
            _length = length;
        }

        public override QualifiedType Apply(QualifiedType baseType, TypeCatalog types)
        {
            var array = new QualifiedType(types.ArrayOf(baseType, _length));
            return _inner.Apply(array, types);
        }
    }

    internal sealed class FunctionDeclaratorNode : DeclaratorNode
    {
        private readonly DeclaratorNode _inner;
        private readonly ImmutableArray<ParameterSymbol> _parameters;
        private readonly bool _hasPrototype;
        private readonly bool _isVariadic;

        public FunctionDeclaratorNode(
            DeclaratorNode inner,
            ImmutableArray<ParameterSymbol> parameters,
            bool hasPrototype,
            bool isVariadic)
        {
            _inner = inner ?? new MissingDeclaratorNode();
            _parameters = parameters.IsDefault
                ? ImmutableArray<ParameterSymbol>.Empty
                : parameters;
            _hasPrototype = hasPrototype;
            _isVariadic = isVariadic;
        }

        public override QualifiedType Apply(QualifiedType baseType, TypeCatalog types)
        {
            var function = new QualifiedType(types.FunctionReturning(
                baseType,
                _parameters,
                _hasPrototype,
                _isVariadic));

            return _inner.Apply(function, types);
        }
    }
}
