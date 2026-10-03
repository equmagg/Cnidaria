using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Cnidaria.Cs
{
    /// <summary>Contains the source namespace graph and syntax-to-symbol maps</summary>
    internal sealed class DeclarationResult
    {
        public NamespaceSymbol GlobalNamespace { get; }
        public ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> DeclaredSymbolsByTree { get; }

        public DeclarationResult(
            NamespaceSymbol globalNamespace,
            ImmutableDictionary<SyntaxTree, ImmutableDictionary<SyntaxNode, Symbol>> declaredSymbolsByTree)
        {
            GlobalNamespace = globalNamespace;
            DeclaredSymbolsByTree = declaredSymbolsByTree;
        }
    }
    /// <summary>Builds source declarations and required synthesized members</summary>
    internal sealed class DeclarationBuilder
    {
        private const string OpetatorPrefix = "op_";
        private readonly DiagnosticBag _diagnostics;
        private readonly TypeManager _types;
        private readonly bool _isCoreLibrary;
        private readonly SourceNamespaceSymbol _global;
        private readonly Dictionary<SyntaxTree, Dictionary<SyntaxNode, Symbol>> _declByTree = new();

        private readonly Dictionary<(Symbol container, string name, int arity), NamedTypeSymbol> _typeCache = new();
        private readonly Dictionary<NamedTypeSymbol, int> _extensionBlockCounts = new();

        private SyntaxTree? _topLevelTree;
        private SourceNamedTypeSymbol? _topLevelProgram;
        private MethodSymbol? _topLevelMain;

        /// <summary>Gets the synthesized entry point for top-level statements</summary>
        public MethodSymbol? SynthesizedEntryPoint => _topLevelMain;

        public DeclarationBuilder(DiagnosticBag diagnostics, TypeManager types, bool isCoreLibrary)
        {
            _diagnostics = diagnostics;
            _types = types;
            _isCoreLibrary = isCoreLibrary;
            _global = new SourceNamespaceSymbol(name: "", containing: null, isGlobal: true);
        }
        /// <summary>Declares all source symbols and finalizes synthesized members</summary>
        /// <remarks>Delegate bases are completed before constructors and type initializers are synthesized</remarks>
        public DeclarationResult Build(ImmutableArray<SyntaxTree> trees)
        {
            foreach (var tree in trees)
            {
                _declByTree[tree] = new Dictionary<SyntaxNode, Symbol>();
                DeclareCompilationUnit(tree, tree.Root, _global);
            }
            CompleteDelegateBaseTypes();
            SynthesizeDefaultConstructors();
            SynthesizeTypeInitializers();
            var immutable = _declByTree.ToImmutableDictionary(
                kv => kv.Key,
                kv => kv.Value.ToImmutableDictionary());

            return new DeclarationResult(_global, immutable);
        }
        private void SynthesizeDefaultConstructors()
        {
            foreach (var kv in _typeCache)
                if (kv.Value is SourceNamedTypeSymbol s)
                    EnsureDefaultConstructor(s);
        }
        private void SynthesizeTypeInitializers()
        {
            foreach (var kv in _typeCache)
                if (kv.Value is SourceNamedTypeSymbol s)
                    EnsureTypeInitializer(s);
        }
        // A static field initializer requires a type initializer symbol even without explicit syntax
        private void EnsureTypeInitializer(SourceNamedTypeSymbol type)
        {
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol ms &&
                    ms.IsStatic &&
                    ms.Parameters.Length == 0 &&
                    StringComparer.Ordinal.Equals(ms.Name, ".cctor"))
                {
                    return;
                }
            }

            bool needsCctor = false;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not (SourceFieldSymbol { IsStatic: true, IsConst: false } or SourcePropertySymbol { IsStatic: true }))
                    continue;

                var declRefs = members[i].DeclaringSyntaxReferences;
                if (declRefs.IsDefaultOrEmpty)
                    continue;

                if (declRefs[0].Node is VariableDeclaratorSyntax { Initializer: not null } or PropertyDeclarationSyntax { Initializer: not null })
                {
                    needsCctor = true;
                    break;
                }
            }

            if (!needsCctor)
                return;

            var voidType = _types.GetSpecialType(SpecialType.System_Void);

            var cctor = new SourceMethodSymbol(
                name: ".cctor",
                containing: type,
                returnType: voidType,
                typeParameters: default,
                isStatic: true,
                isConstructor: true,
                isAsync: false,
                locations: ImmutableArray<Location>.Empty,
                declaredAccessibility: Accessibility.Private);

            cctor.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            cctor.SetParameters(ImmutableArray<ParameterSymbol>.Empty);

            type.AddMember(cctor);
        }
        // Constructor synthesis depends on type kind and existing instance constructors
        private void EnsureDefaultConstructor(SourceNamedTypeSymbol type)
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
                return;
            bool hasAnyInstanceCtor = false;
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol { IsConstructor: true, IsStatic: false })
                    hasAnyInstanceCtor = true;
            }
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            if (type.TypeKind == TypeKind.Class)
            {
                if (!hasAnyInstanceCtor)
                    type.AddMember(new SynthesizedConstructorSymbol(
                        containing: type,
                        voidType: voidType,
                        isStatic: false,
                        parameters: ImmutableArray<ParameterSymbol>.Empty));
                return;
            }
            if (hasAnyInstanceCtor)
                return;
            // A struct has no implicit constructor to run field initializers, so one must be declared (CS8983).
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is not (SourceFieldSymbol { IsStatic: false } or SourcePropertySymbol { IsStatic: false }))
                    continue;
                var declRefs = members[i].DeclaringSyntaxReferences;
                if (!declRefs.IsDefaultOrEmpty &&
                    declRefs[0].Node is VariableDeclaratorSyntax { Initializer: not null } or PropertyDeclarationSyntax { Initializer: not null })
                {
                    _diagnostics.Add(new Diagnostic(
                        "CN_STRUCTINIT001",
                        DiagnosticSeverity.Error,
                        "A 'struct' with field initializers must include an explicitly declared constructor.",
                        new Location(declRefs[0].SyntaxTree, declRefs[0].Node.Span)));
                }
            }
        }

        private static void AddMemberToType(NamedTypeSymbol containingType, Symbol member)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s: s.AddMember(member); return;
                case SpecialNamedTypeSymbol sp: sp.AddMember(member); return;
                default: throw new InvalidOperationException("Type must be mutable.");
            }
        }
        private static void AddNestedTypeToType(NamedTypeSymbol containingType, NamedTypeSymbol nested)
        {
            switch (containingType)
            {
                case SourceNamedTypeSymbol s: s.AddNestedType(nested); return;
                case SpecialNamedTypeSymbol sp: sp.AddNestedType(nested); return;
                default: throw new InvalidOperationException("Type must be mutable.");
            }
        }
        private static string GetFullNamespaceName(SourceNamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace) return "";
            var parts = new Stack<string>();
            Symbol? cur = ns;
            while (cur is SourceNamespaceSymbol sns && !sns.IsGlobalNamespace)
            {
                parts.Push(sns.Name);
                cur = sns.ContainingSymbol;
            }
            return string.Join(".", parts);
        }
        // Reuse predefined symbols while declaring core library types
        private bool TryMapSpecialType(Symbol container, string name, int arity, TypeKind declaredKind, Location location, out NamedTypeSymbol special)
        {
            special = null!;
            if (!_isCoreLibrary) return false;
            if (arity != 0) return false;
            if (container is not SourceNamespaceSymbol ns) return false;
            if (!StringComparer.Ordinal.Equals(GetFullNamespaceName(ns), "System")) return false;

            var st = name switch
            {
                "Object" => SpecialType.System_Object,
                "Void" => SpecialType.System_Void,
                "ValueType" => SpecialType.System_ValueType,
                "Enum" => SpecialType.System_Enum,
                "Array" => SpecialType.System_Array,
                "String" => SpecialType.System_String,
                "Exception" => SpecialType.System_Exception,

                "Boolean" => SpecialType.System_Boolean,
                "Char" => SpecialType.System_Char,
                "SByte" => SpecialType.System_Int8,
                "Byte" => SpecialType.System_UInt8,
                "Int16" => SpecialType.System_Int16,
                "UInt16" => SpecialType.System_UInt16,
                "Int32" => SpecialType.System_Int32,
                "UInt32" => SpecialType.System_UInt32,
                "Int64" => SpecialType.System_Int64,
                "UInt64" => SpecialType.System_UInt64,
                "Single" => SpecialType.System_Single,
                "Double" => SpecialType.System_Double,
                "Decimal" => SpecialType.System_Decimal,
                "IntPtr" => SpecialType.System_IntPtr,
                "UIntPtr" => SpecialType.System_UIntPtr,
                _ => SpecialType.None
            };
            if (st == SpecialType.None) return false;
            special = _types.GetSpecialType(st);
            if (special.TypeKind != declaredKind)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_CORELIB_0001",
                    severity: DiagnosticSeverity.Error,
                    message: $"Special type System.{name} must be declared as {special.TypeKind}, not {declaredKind}.",
                    location: location));
            }
            return true;
        }
        private void RecordDeclared(SyntaxTree tree, SyntaxNode node, Symbol symbol)
            => _declByTree[tree][node] = symbol;
        private void DeclareCompilationUnit(SyntaxTree tree, CompilationUnitSyntax root, SourceNamespaceSymbol global)
        {
            foreach (var member in root.Members)
                DeclareMember(tree, member, global);
        }
        private TypeSymbol? GetDefaultBaseType(TypeKind kind)
        {
            return kind switch
            {
                TypeKind.Class => _types.GetSpecialType(SpecialType.System_Object),
                TypeKind.Struct => _types.GetSpecialType(SpecialType.System_ValueType),
                TypeKind.Enum => _types.GetSpecialType(SpecialType.System_Enum),
                TypeKind.Delegate => GetDelegateBaseType(),
                _ => null
            };
        }
        private void DeclareMember(SyntaxTree tree, MemberDeclarationSyntax member, Symbol container)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    DeclareNamespaceDeclaration(tree, ns, (SourceNamespaceSymbol)container);
                    return;

                case ClassDeclarationSyntax c:
                    DeclareTypeDeclaration(tree, c, container, TypeKind.Class, c.TypeParameterList);
                    return;

                case StructDeclarationSyntax s:
                    DeclareTypeDeclaration(tree, s, container, TypeKind.Struct, s.TypeParameterList);
                    return;

                case InterfaceDeclarationSyntax i:
                    DeclareTypeDeclaration(tree, i, container, TypeKind.Interface, i.TypeParameterList);
                    return;

                case EnumDeclarationSyntax e:
                    DeclareEnumDeclaration(tree, e, container);
                    return;

                case DelegateDeclarationSyntax d:
                    DeclareDelegateDeclaration(tree, d, container);
                    return;

                case GlobalStatementSyntax gs:
                    DeclareTopLevelStatement(tree, gs, container);
                    return;

                default:
                    // Ignore unsupported member syntax
                    return;
            }
        }
        // Top-level statements share one synthesized program type and entry point
        private void DeclareTopLevelStatement(SyntaxTree tree, GlobalStatementSyntax gs, Symbol container)
        {
            if (container is not SourceNamespaceSymbol global || !global.IsGlobalNamespace)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_TL001",
                    severity: DiagnosticSeverity.Error,
                    message: "Top-level statements must be in the global namespace.",
                    location: new Location(tree, gs.Span)));
                return;
            }

            if (_topLevelTree is null)
                _topLevelTree = tree;
            else if (!ReferenceEquals(_topLevelTree, tree))
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_TL002",
                    severity: DiagnosticSeverity.Error,
                    message: "Top-level statements are only allowed in one source file.",
                    location: new Location(tree, gs.Span)));
                return;
            }

            // Create the shared top-level container on first use
            _topLevelProgram ??= GetOrCreateProgramType(tree, global, gs);

            // Create the synthesized entry point on first use
            _topLevelMain ??= GetOrCreateTopLevelMain(tree, _topLevelProgram, gs);
        }
        /// <summary>Returns the shared synthesized container for top-level statements</summary>
        private SourceNamedTypeSymbol GetOrCreateProgramType(
            SyntaxTree tree, SourceNamespaceSymbol global, SyntaxNode locationNode)
        {
            var key = ((Symbol)global, "Program", 0);

            if (!_typeCache.TryGetValue(key, out var existing))
            {
                var type = new SourceNamedTypeSymbol("Program", global, TypeKind.Class, arity: 0, Accessibility.Internal);
                type.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Class));
                type.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
                global.AddType(type);

                type.AddDeclaration(tree, locationNode);

                _typeCache.Add(key, type);
                return type;
            }

            if (existing is SourceNamedTypeSymbol s)
                return s;

            throw new InvalidOperationException("Cached Program type is not a SourceNamedTypeSymbol.");
        }
        /// <summary>Returns the synthesized entry point for top-level statements</summary>
        private MethodSymbol GetOrCreateTopLevelMain(
            SyntaxTree tree, SourceNamedTypeSymbol programType, SyntaxNode locationNode)
        {
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            var stringType = _types.GetSpecialType(SpecialType.System_String);


            var main = new SourceMethodSymbol(
                name: "<Main>$",
                containing: programType,
                returnType: voidType,
                typeParameters: default,
                isStatic: true,
                isConstructor: false,
                isAsync: false,
                locations: ImmutableArray.Create(new Location(tree, locationNode.Span)),
                declaredAccessibility: Accessibility.Private);
            main.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var argsType = _types.GetArrayType(stringType, rank: 1);
            var argsParam = new ParameterSymbol(
                name: "args",
                containing: main,
                type: argsType,
                locations: ImmutableArray.Create(new Location(tree, locationNode.Span)));

            main.SetParameters(ImmutableArray.Create(argsParam));
            programType.AddMember(main);
            return main;
        }
        private void DeclareNamespaceDeclaration(SyntaxTree tree, BaseNamespaceDeclarationSyntax syntax, SourceNamespaceSymbol container)
        {
            var ns = GetOrCreateNamespacePath(tree, container, syntax.Name, syntax);
            RecordDeclared(tree, syntax, ns);

            // Declare members under the containing namespace
            foreach (var member in syntax.Members)
                DeclareMember(tree, member, ns);
        }
        private SourceNamespaceSymbol GetOrCreateNamespacePath(
            SyntaxTree tree,
            SourceNamespaceSymbol container,
            NameSyntax name,
            SyntaxNode ownerForDiagnostics)
        {
            var current = container;
            foreach (var part in EnumerateDottedNameParts(name))
            {
                if (part.Length == 0)
                    continue;

                current = current.GetOrAddNamespace(
                    part,
                    () => new SourceNamespaceSymbol(part, current, isGlobal: false));

            }

            current.AddDeclaration(tree, ownerForDiagnostics);
            return current;
        }
        private void DeclareTypeDeclaration(
            SyntaxTree tree,
            TypeDeclarationSyntax syntax,
            Symbol container,
            TypeKind kind,
            TypeParameterListSyntax? typeParameterList)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var arity = typeParameterList?.Parameters.Count ?? 0;
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            var isReadOnlyStruct = HasModifier(syntax.Modifiers, SyntaxKind.ReadOnlyKeyword);
            var isRefStruct = HasModifier(syntax.Modifiers, SyntaxKind.RefKeyword);
            var isUnsafe = HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword) || Binder.IsUnsafeContext(container);
            var isSealed = HasModifier(syntax.Modifiers, SyntaxKind.SealedKeyword);
            var isAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var key = (container, name, arity);
            if (kind != TypeKind.Struct && (isReadOnlyStruct || isRefStruct))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_TYPEMOD001",
                    DiagnosticSeverity.Error,
                    "Only structs may be declared with 'readonly' or 'ref'.",
                    new Location(tree, syntax.Span)));
            }
            if (isSealed && kind != TypeKind.Class)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_TYPEMOD002",
                    DiagnosticSeverity.Error,
                    "Only classes may be declared with 'sealed'.",
                    new Location(tree, syntax.Span)));
            }
            if (isSealed && isAbstract)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_TYPEMOD003",
                    DiagnosticSeverity.Error,
                    "A class cannot be both abstract and sealed.",
                    new Location(tree, syntax.Span)));
            }
            if (!_typeCache.TryGetValue(key, out var type))
            {
                if (TryMapSpecialType(container, name, arity, kind, new Location(tree, syntax.Span), out var special))
                {
                    type = special;
                    _typeCache.Add(key, type);
                }
                else
                {
                    var s = new SourceNamedTypeSymbol(
                        name,
                        container,
                        kind,
                        arity,
                        declaredAcc,
                        isFromMetadata: false,
                        isReadOnlyStruct: kind == TypeKind.Struct && isReadOnlyStruct,
                        isRefLikeType: kind == TypeKind.Struct && isRefStruct,
                        isSealed: kind == TypeKind.Class && isSealed,
                        isUnsafe: isUnsafe);
                    s.SetDefaultBaseType(GetDefaultBaseType(kind));
                    s.SetTypeParameters(DeclareTypeParameters(tree, s, typeParameterList));
                    type = s;
                    _typeCache.Add(key, type);
                    if (container is SourceNamespaceSymbol ns) ns.AddType(type);
                    else if (container is NamedTypeSymbol nt) AddNestedTypeToType(nt, type);
                    else throw new InvalidOperationException($"Invalid container for type: {container.Kind}");
                }
            }
            else
            {
                if (type.TypeKind != kind)
                {
                    _diagnostics.Add(new Diagnostic(
                        id: "CN0001",
                        severity: DiagnosticSeverity.Error,
                        message: $"Partial type '{name}' has conflicting kinds ({type.TypeKind} vs {kind}).",
                        location: new Location(tree, syntax.Span)));
                }
                else if (type is SourceNamedTypeSymbol srcExisting && kind == TypeKind.Struct)
                {
                    if (srcExisting.IsReadOnlyStruct != isReadOnlyStruct || srcExisting.IsRefLikeType != isRefStruct)
                    {
                        _diagnostics.Add(new Diagnostic(
                            "CN_TYPEMOD002",
                            DiagnosticSeverity.Error,
                            $"Partial struct '{name}' has conflicting 'readonly'/'ref' modifiers.",
                            new Location(tree, syntax.Span)));
                    }
                }

            }
            if (isUnsafe && type is SourceNamedTypeSymbol unsafeSourceType)
                unsafeSourceType.MarkUnsafe();
            if (type is SpecialNamedTypeSymbol declaredSpecial)
                declaredSpecial.SetDeclaredAccessibility(declaredAcc);
            if (kind == TypeKind.Class && isSealed)
            {
                switch (type)
                {
                    case SourceNamedTypeSymbol src:
                        src.MarkSealed();
                        break;

                    case SpecialNamedTypeSymbol special:
                        special.MarkSealed();
                        break;
                }
            }
            if (kind == TypeKind.Class && isAbstract && type is SourceNamedTypeSymbol abstractType)
                abstractType.MarkAbstract();
            if (type is SourceNamedTypeSymbol srcType)
                srcType.AddDeclaration(tree, syntax);
            RecordDeclared(tree, syntax, type);

            // Type parameters participate in declaration lookup
            if (typeParameterList != null)
            {
                for (int i = 0; i < typeParameterList.Parameters.Count; i++)
                {
                    var tpSyntax = typeParameterList.Parameters[i];
                }
            }

            // Declare nested types inside this type
            foreach (var m in syntax.Members)
            {
                switch (m)
                {
                    case ClassDeclarationSyntax c:
                        DeclareTypeDeclaration(tree, c, type, TypeKind.Class, c.TypeParameterList);
                        break;
                    case StructDeclarationSyntax s:
                        DeclareTypeDeclaration(tree, s, type, TypeKind.Struct, s.TypeParameterList);
                        break;
                    case InterfaceDeclarationSyntax i:
                        DeclareTypeDeclaration(tree, i, type, TypeKind.Interface, i.TypeParameterList);
                        break;
                    case EnumDeclarationSyntax e:
                        DeclareEnumDeclaration(tree, e, type);
                        break;
                    case DelegateDeclarationSyntax d:
                        DeclareDelegateDeclaration(tree, d, type);
                        break;
                    case MethodDeclarationSyntax md:
                        DeclareMethodDeclaration(tree, md, type);
                        break;
                    case FieldDeclarationSyntax fd:
                        DeclareFieldDeclaration(tree, fd, type);
                        break;
                    case PropertyDeclarationSyntax pd:
                        DeclarePropertyDeclaration(tree, pd, type);
                        break;
                    case IndexerDeclarationSyntax id:
                        DeclareIndexerDeclaration(tree, id, type);
                        break;
                    case ConstructorDeclarationSyntax cd:
                        DeclareConstructorDeclaration(tree, cd, type);
                        break;
                    case OperatorDeclarationSyntax ods:
                        DeclareOperatorDeclaration(tree, ods, type);
                        break;

                    case ConversionOperatorDeclarationSyntax cods:
                        DeclareConversionOperatorDeclaration(tree, cods, type);
                        break;
                    case ExtensionBlockDeclarationSyntax extensionBlock when type is NamedTypeSymbol extendingType:
                        DeclareExtensionBlock(tree, extensionBlock, syntax, extendingType);
                        break;
                    default:

                        break;
                }
            }
        }
        // Delegate base types are assigned after all source types are declared
        private void CompleteDelegateBaseTypes()
        {
            var delegateBase = GetDelegateBaseType();
            foreach (var kv in _typeCache)
            {
                if (kv.Value is not SourceNamedTypeSymbol type || type.TypeKind != TypeKind.Delegate)
                    continue;

                if (delegateBase is null || ReferenceEquals(type.BaseType, delegateBase))
                    continue;

                if (ReferenceEquals(type, delegateBase))
                    continue;

                type.SetDeclaredBaseType(delegateBase);
            }
        }

        private NamedTypeSymbol GetDelegateBaseType()
        {
            if (TryFindSystemType(_global, "MulticastDelegate", arity: 0, out var multicastDelegate))
                return multicastDelegate;

            if (TryFindSystemType(_types.CoreGlobalNamespace, "MulticastDelegate", arity: 0, out multicastDelegate))
                return multicastDelegate;

            if (TryFindSystemType(_global, "Delegate", arity: 0, out var delegateType))
                return delegateType;

            if (TryFindSystemType(_types.CoreGlobalNamespace, "Delegate", arity: 0, out delegateType))
                return delegateType;

            return _types.GetSpecialType(SpecialType.System_Object);
        }

        /// <summary>Finds a top-level System type by name and arity</summary>
        internal static bool TryFindSystemType(NamespaceSymbol global, string name, int arity, out NamedTypeSymbol type)
        {
            type = null!;
            NamespaceSymbol? systemNs = null;
            var namespaces = global.GetNamespaceMembers();
            for (int i = 0; i < namespaces.Length; i++)
            {
                if (StringComparer.Ordinal.Equals(namespaces[i].Name, "System"))
                {
                    systemNs = namespaces[i];
                    break;
                }
            }

            if (systemNs is null)
                return false;

            var candidates = systemNs.GetTypeMembers(name, arity);
            if (candidates.IsDefaultOrEmpty)
                return false;

            type = candidates[0];
            return true;
        }

        private void DeclareDelegateDeclaration(SyntaxTree tree, DelegateDeclarationSyntax syntax, Symbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var arity = syntax.TypeParameterList?.Parameters.Count ?? 0;
            var key = (container, name, arity);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var isUnsafe = HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword) || Binder.IsUnsafeContext(container);
            NamedTypeSymbol type;
            if (!_typeCache.TryGetValue(key, out var existing))
            {
                var sourceType = new SourceNamedTypeSymbol(
                    name,
                    container,
                    TypeKind.Delegate,
                    arity,
                    declaredAcc,
                    isFromMetadata: false,
                    isUnsafe: isUnsafe);

                sourceType.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Delegate));
                sourceType.SetTypeParameters(DeclareTypeParameters(tree, sourceType, syntax.TypeParameterList));

                type = sourceType;
                _typeCache.Add(key, type);

                if (container is SourceNamespaceSymbol ns)
                    ns.AddType(type);
                else if (container is NamedTypeSymbol nt)
                    AddNestedTypeToType(nt, type);
                else
                    throw new InvalidOperationException($"Invalid container for delegate: {container.Kind}");
            }
            else
            {
                type = existing;
                if (type.TypeKind != TypeKind.Delegate)
                {
                    _diagnostics.Add(new Diagnostic(
                        id: "CN0003",
                        severity: DiagnosticSeverity.Error,
                        message: $"Type '{name}' conflicts with delegate declaration.",
                        location: new Location(tree, syntax.Span)));
                }
            }

            if (type is SourceNamedTypeSymbol srcType)
            {
                srcType.AddDeclaration(tree, syntax);

                if (!HasDelegateInvokeMethod(srcType))
                    AddSynthesizedDelegateMembers(tree, syntax, srcType);
            }

            RecordDeclared(tree, syntax, type);
        }

        private static bool HasDelegateInvokeMethod(NamedTypeSymbol type)
        {
            var members = type.GetMembers();
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] is MethodSymbol m && StringComparer.Ordinal.Equals(m.Name, "Invoke"))
                    return true;
            }

            return false;
        }

        // Delegate declarations receive constructor and Invoke symbols
        private void AddSynthesizedDelegateMembers(SyntaxTree tree, DelegateDeclarationSyntax syntax, SourceNamedTypeSymbol delegateType)
        {
            var voidType = _types.GetSpecialType(SpecialType.System_Void);
            var objectType = _types.GetSpecialType(SpecialType.System_Object);
            var intPtrType = _types.GetSpecialType(SpecialType.System_IntPtr);
            var placeholderReturn = new ErrorTypeSymbol("unbound-delegate-return", containing: null, ImmutableArray<Location>.Empty);

            var ctor = new SourceMethodSymbol(
                name: ".ctor",
                containing: delegateType,
                returnType: voidType,
                typeParameters: default,
                isStatic: false,
                isConstructor: true,
                isAsync: false,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: Accessibility.Public);
            ctor.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            ctor.SetParameters(ImmutableArray.Create(
                new ParameterSymbol("target", ctor, objectType, ImmutableArray<Location>.Empty),
                new ParameterSymbol("method", ctor, intPtrType, ImmutableArray<Location>.Empty)));
            ctor.AddDeclaration(new SyntaxReference(tree, syntax));
            AddMemberToType(delegateType, ctor);

            var invoke = new SourceMethodSymbol(
                name: "Invoke",
                containing: delegateType,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: false,
                isConstructor: false,
                isAsync: false,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: Accessibility.Public);
            invoke.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            invoke.SetDispatchFlags(isVirtual: true, isAbstract: false, isOverride: false, isSealed: false);

            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-delegate-param", containing: null, ImmutableArray<Location>.Empty);
                var parameter = new ParameterSymbol(
                    name: pName,
                    containing: invoke,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: IsParamsParameter(p));

                parameters.Add(parameter);
                RecordDeclared(tree, p, parameter);
            }

            invoke.SetParameters(parameters.ToImmutable());
            invoke.AddDeclaration(new SyntaxReference(tree, syntax));
            AddMemberToType(delegateType, invoke);
        }

        private static bool HasModifier(SyntaxTokenList mods, SyntaxKind kind)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.Kind == kind)
                    return true;
                if (m.Kind == SyntaxKind.IdentifierToken && m.ContextualKind == kind)
                    return true;
            }
            return false;
        }
        /// <summary>Decodes accessibility modifiers and reports invalid combinations</summary>
        private Accessibility DecodeDeclaredAccessibility(
            SyntaxTokenList mods,
            Accessibility @default,
            bool allowProtected,
            bool allowInternal,
            Location diagLocation)
        {
            bool hasPublic = HasModifier(mods, SyntaxKind.PublicKeyword);
            bool hasPrivate = HasModifier(mods, SyntaxKind.PrivateKeyword);
            bool hasProtected = HasModifier(mods, SyntaxKind.ProtectedKeyword);
            bool hasInternal = HasModifier(mods, SyntaxKind.InternalKeyword);

            int cnt = (hasPublic ? 1 : 0) + (hasPrivate ? 1 : 0) + (hasProtected ? 1 : 0) + (hasInternal ? 1 : 0);
            if (cnt == 0)
                return @default;

            if (hasPublic && cnt > 1)
            {
                _diagnostics.Add(new Diagnostic("CN_ACC001", DiagnosticSeverity.Error,
                    "Invalid access modifier combination.", diagLocation));
                return @default;
            }

            if (hasPrivate && hasInternal && !hasProtected)
            {
                _diagnostics.Add(new Diagnostic("CN_ACC001", DiagnosticSeverity.Error,
                    "Invalid access modifier combination.", diagLocation));
                return @default;
            }

            if (hasProtected && hasInternal)
            {
                if (!allowProtected || !allowInternal)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Protected/internal is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.ProtectedOrInternal;
            }

            if (hasPrivate && hasProtected)
            {
                if (!allowProtected)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Private protected is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.ProtectedAndInternal;
            }

            if (hasPublic) return Accessibility.Public;
            if (hasPrivate) return Accessibility.Private;

            if (hasProtected)
            {
                if (!allowProtected)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Protected is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.Protected;
            }

            if (hasInternal)
            {
                if (!allowInternal)
                {
                    _diagnostics.Add(new Diagnostic("CN_ACC002", DiagnosticSeverity.Error,
                        "Internal is not valid here.", diagLocation));
                    return @default;
                }
                return Accessibility.Internal;
            }

            return @default;
        }
        private static bool HasAccessibilityModifier(SyntaxTokenList modifiers)
            => HasModifier(modifiers, SyntaxKind.PublicKeyword) ||
               HasModifier(modifiers, SyntaxKind.PrivateKeyword) ||
               HasModifier(modifiers, SyntaxKind.ProtectedKeyword) ||
               HasModifier(modifiers, SyntaxKind.InternalKeyword);

        private Accessibility DecodeAccessorAccessibility(
            SyntaxTree tree,
            AccessorDeclarationSyntax accessor,
            Accessibility propertyAccessibility,
            bool hasGetAndSet,
            ref bool hasAccessorAccessibility)
        {
            if (!HasAccessibilityModifier(accessor.Modifiers))
                return propertyAccessibility;

            var location = new Location(tree, accessor.Span);
            var accessibility = DecodeDeclaredAccessibility(
                accessor.Modifiers,
                propertyAccessibility,
                allowProtected: true,
                allowInternal: true,
                diagLocation: location);

            if (!hasGetAndSet)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_ACC_ACCESSOR001",
                    DiagnosticSeverity.Error,
                    "An accessor modifier is only valid when the property or indexer has both accessors.",
                    location));
            }

            if (hasAccessorAccessibility)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_ACC_ACCESSOR002",
                    DiagnosticSeverity.Error,
                    "Only one accessor may declare an accessibility modifier.",
                    location));
            }
            hasAccessorAccessibility = true;

            if (!IsMoreRestrictiveAccessibility(accessibility, propertyAccessibility))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_ACC_ACCESSOR003",
                    DiagnosticSeverity.Error,
                    "The accessor accessibility must be more restrictive than the property or indexer accessibility.",
                    location));
            }

            return accessibility;
        }

        private static bool IsMoreRestrictiveAccessibility(Accessibility accessor, Accessibility property)
        {
            if (accessor == property || accessor == Accessibility.NotApplicable)
                return false;

            return property switch
            {
                Accessibility.Public => accessor is Accessibility.Private or Accessibility.Protected or
                    Accessibility.Internal or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal,
                Accessibility.ProtectedOrInternal => accessor is Accessibility.Private or Accessibility.Protected or
                    Accessibility.Internal or Accessibility.ProtectedAndInternal,
                Accessibility.Protected => accessor is Accessibility.Private or Accessibility.ProtectedAndInternal,
                Accessibility.Internal => accessor is Accessibility.Private or Accessibility.ProtectedAndInternal,
                Accessibility.ProtectedAndInternal => accessor == Accessibility.Private,
                _ => false,
            };
        }
        private static Accessibility GetDefaultTypeMemberAccessibility(NamedTypeSymbol container)
            => container.TypeKind == TypeKind.Interface
                ? Accessibility.Public
                : Accessibility.Private;
        private void ValidateSealedMethodModifier(
            SyntaxTree tree,
            SyntaxNode syntax,
            bool isStatic,
            bool isOverride,
            bool isAbstract,
            bool isVirtualKeyword,
            bool isSealed,
            string memberKind)
        {
            if (!isSealed)
                return;

            if (!isOverride)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_SEALED_MEMBER001",
                    DiagnosticSeverity.Error,
                    $"The 'sealed' modifier is only valid on an override {memberKind}.",
                    new Location(tree, syntax.Span)));
            }

            if (isAbstract)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_SEALED_MEMBER002",
                    DiagnosticSeverity.Error,
                    $"A sealed {memberKind} cannot be abstract.",
                    new Location(tree, syntax.Span)));
            }

            if (isVirtualKeyword)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_SEALED_MEMBER003",
                    DiagnosticSeverity.Error,
                    $"A sealed {memberKind} cannot also be virtual.",
                    new Location(tree, syntax.Span)));
            }

            if (isStatic)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_SEALED_MEMBER004",
                    DiagnosticSeverity.Error,
                    $"A static {memberKind} cannot be sealed.",
                    new Location(tree, syntax.Span)));
            }
        }
        // Accessors carry the property's dispatch modifiers; instance interface accessors without a body are abstract.
        private void SetAccessorDispatchFlags(
            SyntaxTree tree,
            SyntaxNode syntax,
            SyntaxTokenList modifiers,
            NamedTypeSymbol container,
            bool isStatic,
            Accessibility declaredAcc,
            bool hasExpressionBody,
            SourceMethodSymbol? getMethod,
            AccessorDeclarationSyntax? getSyntax,
            SourceMethodSymbol? setMethod,
            AccessorDeclarationSyntax? setSyntax)
        {
            var isOverride = HasModifier(modifiers, SyntaxKind.OverrideKeyword);
            var isAbstract = HasModifier(modifiers, SyntaxKind.AbstractKeyword);
            var isVirtualKeyword = HasModifier(modifiers, SyntaxKind.VirtualKeyword);
            var isSealed = HasModifier(modifiers, SyntaxKind.SealedKeyword);
            ValidateSealedMethodModifier(tree, syntax, isStatic, isOverride, isAbstract, isVirtualKeyword, isSealed, "property");

            bool isVirtual = isVirtualKeyword || isOverride || isAbstract;
            bool isInterfaceInstance = container.TypeKind == TypeKind.Interface && !isStatic;
            bool nonVirtual = declaredAcc == Accessibility.Private || isSealed;
            Apply(getMethod, hasExpressionBody || HasBody(getSyntax));
            Apply(setMethod, HasBody(setSyntax));

            void Apply(SourceMethodSymbol? accessor, bool hasBody)
            {
                if (accessor is null)
                    return;
                if (isInterfaceInstance)
                    accessor.SetDispatchFlags(isVirtual: !hasBody || !nonVirtual, isAbstract: !hasBody, isOverride: false, isSealed: false);
                else
                    accessor.SetDispatchFlags(isVirtual, isAbstract, isOverride, isSealed);
            }
            static bool HasBody(AccessorDeclarationSyntax? accessor)
                => accessor is not null && (accessor.Body is not null || accessor.ExpressionBody is not null);
        }
        // Extension block members compile to static methods of the enclosing class; the nested '<G>$N' type keeps the block for metadata readers
        private void DeclareExtensionBlock(SyntaxTree tree, ExtensionBlockDeclarationSyntax syntax, TypeDeclarationSyntax ownerSyntax, NamedTypeSymbol container)
        {
            if (container.ContainingSymbol is not NamespaceSymbol || container.Arity != 0 ||
                !HasModifier(ownerSyntax.Modifiers, SyntaxKind.StaticKeyword))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_EXT001",
                    DiagnosticSeverity.Error,
                    "Extension blocks must be declared in a non-generic, non-nested static class.",
                    new Location(tree, syntax.Span)));
                return;
            }
            if (syntax.ParameterList is not { Parameters.Count: 1 } parameterList)
                return;

            int ordinal = _extensionBlockCounts.TryGetValue(container, out int count) ? count : 0;
            _extensionBlockCounts[container] = ordinal + 1;
            string groupingName = $"<G>${ordinal}";
            var receiverSyntax = parameterList.Parameters[0];
            string receiverName = receiverSyntax.Identifier.ValueText ?? "";
            var location = new Location(tree, syntax.Span);
            var placeholder = new ErrorTypeSymbol("unbound-receiver", containing: null, ImmutableArray<Location>.Empty);

            var grouping = new SourceNamedTypeSymbol(
                groupingName, container, TypeKind.Class, syntax.TypeParameterList?.Parameters.Count ?? 0, Accessibility.Public);
            grouping.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Class));
            grouping.SetTypeParameters(DeclareTypeParameters(tree, grouping, syntax.TypeParameterList));
            grouping.MarkAbstract();
            grouping.AddDeclaration(tree, syntax);
            AddNestedTypeToType(container, grouping);
            RecordDeclared(tree, syntax, grouping);

            var marker = new SourceMethodSymbol(
                name: "<Extension>$",
                containing: grouping,
                returnType: _types.GetSpecialType(SpecialType.System_Void),
                typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                isStatic: false,
                isConstructor: false,
                isAsync: false,
                locations: ImmutableArray.Create(location),
                declaredAccessibility: Accessibility.Public);
            marker.SetDispatchFlags(isVirtual: true, isAbstract: true, isOverride: false, isSealed: false);
            marker.SetParameters(ImmutableArray.Create(CreateExtensionReceiver(tree, receiverSyntax, marker, placeholder)));
            AddMemberToType(grouping, marker);

            var block = new ExtensionBlockDeclaration(syntax, receiverSyntax, receiverName.Length != 0, groupingName, new SyntaxReference(tree, syntax));
            foreach (var member in syntax.Members)
            {
                switch (member)
                {
                    case MethodDeclarationSyntax method:
                        DeclareExtensionMethod(tree, method, container, block);
                        break;
                    case PropertyDeclarationSyntax property:
                        DeclareExtensionProperty(tree, property, container, block);
                        break;
                    case OperatorDeclarationSyntax op:
                        DeclareExtensionOperator(tree, op, container, block);
                        break;
                    default:
                        _diagnostics.Add(new Diagnostic(
                            "CN_EXT002",
                            DiagnosticSeverity.Error,
                            "Extension blocks may only declare methods, properties, and operators.",
                            new Location(tree, member.Span)));
                        break;
                }
            }
        }

        private sealed record ExtensionBlockDeclaration(
            ExtensionBlockDeclarationSyntax Syntax,
            ParameterSyntax ReceiverSyntax,
            bool HasReceiverName,
            string GroupingName,
            SyntaxReference Reference);

        private static ParameterSymbol CreateExtensionReceiver(SyntaxTree tree, ParameterSyntax receiverSyntax, Symbol owner, TypeSymbol placeholder)
            => new ParameterSymbol(
                name: receiverSyntax.Identifier.ValueText ?? "",
                containing: owner,
                type: placeholder,
                locations: ImmutableArray.Create(new Location(tree, receiverSyntax.Span)),
                isReadOnlyRef: IsReadOnlyByRefParameter(receiverSyntax),
                refKind: GetParameterRefKind(receiverSyntax),
                isScoped: IsScopedParameter(receiverSyntax));

        private bool CheckExtensionMemberModifiers(SyntaxTree tree, SyntaxNode member, SyntaxTokenList modifiers, ExtensionBlockDeclaration block, bool isStatic)
        {
            if (HasModifier(modifiers, SyntaxKind.VirtualKeyword) || HasModifier(modifiers, SyntaxKind.AbstractKeyword) ||
                HasModifier(modifiers, SyntaxKind.OverrideKeyword) || HasModifier(modifiers, SyntaxKind.SealedKeyword) ||
                HasModifier(modifiers, SyntaxKind.ProtectedKeyword))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_EXT003",
                    DiagnosticSeverity.Error,
                    "Extension members cannot be virtual, abstract, override, sealed, or protected.",
                    new Location(tree, member.Span)));
                return false;
            }
            if (!isStatic && !block.HasReceiverName)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_EXT004",
                    DiagnosticSeverity.Error,
                    "An instance extension member requires a named receiver parameter.",
                    new Location(tree, member.Span)));
                return false;
            }
            return true;
        }

        private ImmutableArray<TypeParameterSymbol> DeclareExtensionTypeParameters(
            SyntaxTree tree, Symbol owner, ExtensionBlockDeclaration block, TypeParameterListSyntax? ownList)
        {
            var blockList = block.Syntax.TypeParameterList;
            int blockCount = blockList?.Parameters.Count ?? 0;
            int ownCount = ownList?.Parameters.Count ?? 0;
            var builder = ImmutableArray.CreateBuilder<TypeParameterSymbol>(blockCount + ownCount);
            for (int i = 0; i < blockCount; i++)
            {
                var p = blockList!.Parameters[i];
                builder.Add(new TypeParameterSymbol(p.Identifier.ValueText ?? "", owner, i, ImmutableArray.Create(new Location(tree, p.Span))));
            }
            for (int i = 0; i < ownCount; i++)
            {
                var p = ownList!.Parameters[i];
                var tp = new TypeParameterSymbol(p.Identifier.ValueText ?? "", owner, blockCount + i, ImmutableArray.Create(new Location(tree, p.Span)));
                builder.Add(tp);
                RecordDeclared(tree, p, tp);
            }
            return builder.ToImmutable();
        }

        private SourceMethodSymbol CreateExtensionImplementation(
            SyntaxTree tree,
            string name,
            NamedTypeSymbol container,
            ExtensionBlockDeclaration block,
            ExtensionMemberInfo info,
            TypeSymbol returnType,
            Accessibility accessibility,
            SyntaxNode declaration,
            TypeParameterListSyntax? ownTypeParameters,
            bool isAsync,
            bool isUnsafe,
            bool isExtern)
        {
            var method = new SourceMethodSymbol(
                name: name,
                containing: container,
                returnType: returnType,
                typeParameters: default,
                isStatic: true,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, declaration.Span)),
                declaredAccessibility: accessibility,
                isExtensionMethod: info.Kind == ExtensionMemberKind.Method && !info.IsStatic,
                isUnsafe: isUnsafe || Binder.IsUnsafeContext(container),
                isExtern: isExtern);
            method.SetTypeParameters(DeclareExtensionTypeParameters(tree, method, block, ownTypeParameters));
            method.SetExtensionMember(info);
            method.AddDeclaration(new SyntaxReference(tree, declaration));
            AddMemberToType(container, method);
            return method;
        }

        private void DeclareExtensionMethod(SyntaxTree tree, MethodDeclarationSyntax syntax, NamedTypeSymbol container, ExtensionBlockDeclaration block)
        {
            var name = syntax.Identifier.ValueText ?? "";
            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            if (!CheckExtensionMemberModifiers(tree, syntax, syntax.Modifiers, block, isStatic))
                return;

            var accessibility = DecodeDeclaredAccessibility(
                syntax.Modifiers, GetDefaultTypeMemberAccessibility(container), allowProtected: false, allowInternal: true, diagLocation: new Location(tree, syntax.Span));
            var info = new ExtensionMemberInfo(
                ExtensionMemberKind.Method, name, isStatic, isSetter: false, block.Syntax.TypeParameterList?.Parameters.Count ?? 0, block.GroupingName, block.Reference);
            var method = CreateExtensionImplementation(
                tree, name, container, block, info,
                new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty),
                accessibility, syntax, syntax.TypeParameterList,
                isAsync: HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword),
                isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword),
                isExtern: HasModifier(syntax.Modifiers, SyntaxKind.ExternKeyword));

            var placeholder = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);
            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count + 1);
            if (!isStatic)
                parameters.Add(CreateExtensionReceiver(tree, block.ReceiverSyntax, method, placeholder));
            foreach (var p in syntax.ParameterList.Parameters)
            {
                var parameter = new ParameterSymbol(
                    name: p.Identifier.ValueText ?? "",
                    containing: method,
                    type: placeholder,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: IsParamsParameter(p));
                parameters.Add(parameter);
                RecordDeclared(tree, p, parameter);
            }
            method.SetParameters(parameters.ToImmutable());
            RecordDeclared(tree, syntax, method);
        }

        private void DeclareExtensionProperty(SyntaxTree tree, PropertyDeclarationSyntax syntax, NamedTypeSymbol container, ExtensionBlockDeclaration block)
        {
            var name = syntax.Identifier.ValueText ?? "";
            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            if (name.Length == 0 || !CheckExtensionMemberModifiers(tree, syntax, syntax.Modifiers, block, isStatic))
                return;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers, GetDefaultTypeMemberAccessibility(container), allowProtected: false, allowInternal: true, diagLocation: new Location(tree, syntax.Span));
            AccessorDeclarationSyntax? getSyntax = null, setSyntax = null;
            bool hasGet = syntax.ExpressionBody is not null;
            bool hasSet = false;
            bool hasAutoAccessor = false;
            if (syntax.AccessorList is not null)
            {
                foreach (var accessor in syntax.AccessorList.Accessors)
                {
                    if (accessor.Kind == SyntaxKind.GetAccessorDeclaration)
                    {
                        hasGet = true;
                        getSyntax ??= accessor;
                    }
                    else if (accessor.Kind is SyntaxKind.SetAccessorDeclaration or SyntaxKind.InitAccessorDeclaration)
                    {
                        hasSet = true;
                        setSyntax ??= accessor;
                    }
                    hasAutoAccessor |= accessor.Body is null && accessor.ExpressionBody is null;
                }
            }
            if (hasAutoAccessor || (!hasGet && !hasSet))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_EXT005",
                    DiagnosticSeverity.Error,
                    "Extension properties must declare accessors with bodies; they have no storage of their own.",
                    new Location(tree, syntax.Span)));
                return;
            }

            int blockArity = block.Syntax.TypeParameterList?.Parameters.Count ?? 0;
            bool unused = false;
            var placeholder = new ErrorTypeSymbol("unbound-property", containing: null, ImmutableArray<Location>.Empty);
            SourceMethodSymbol? getMethod = null, setMethod = null;
            if (hasGet)
            {
                var accessibility = getSyntax is null ? declaredAcc : DecodeAccessorAccessibility(tree, getSyntax, declaredAcc, hasGet && hasSet, ref unused);
                var info = new ExtensionMemberInfo(ExtensionMemberKind.Property, name, isStatic, isSetter: false, blockArity, block.GroupingName, block.Reference);
                getMethod = CreateExtensionImplementation(
                    tree, $"get_{name}", container, block, info, placeholder, accessibility, (SyntaxNode?)getSyntax ?? syntax, null,
                    isAsync: false, isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword), isExtern: false);
                getMethod.SetParameters(isStatic
                    ? ImmutableArray<ParameterSymbol>.Empty
                    : ImmutableArray.Create(CreateExtensionReceiver(tree, block.ReceiverSyntax, getMethod, placeholder)));
                if (getSyntax is not null)
                    RecordDeclared(tree, getSyntax, getMethod);
            }
            if (hasSet)
            {
                var accessibility = setSyntax is null ? declaredAcc : DecodeAccessorAccessibility(tree, setSyntax, declaredAcc, hasGet && hasSet, ref unused);
                var info = new ExtensionMemberInfo(ExtensionMemberKind.Property, name, isStatic, isSetter: true, blockArity, block.GroupingName, block.Reference);
                setMethod = CreateExtensionImplementation(
                    tree, $"set_{name}", container, block, info, _types.GetSpecialType(SpecialType.System_Void), accessibility, setSyntax!, null,
                    isAsync: false, isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword), isExtern: false);
                var value = new ParameterSymbol("value", setMethod, placeholder, ImmutableArray.Create(new Location(tree, setSyntax!.Span)));
                setMethod.SetParameters(isStatic
                    ? ImmutableArray.Create(value)
                    : ImmutableArray.Create(CreateExtensionReceiver(tree, block.ReceiverSyntax, setMethod, placeholder), value));
                RecordDeclared(tree, setSyntax, setMethod);
            }

            var property = new SourcePropertySymbol(
                name: name,
                containing: container,
                declaredTypeSyntax: syntax.Type,
                placeholderType: placeholder,
                isStatic: isStatic,
                declaredAccessibility: declaredAcc,
                hasGet: hasGet,
                hasSet: hasSet,
                getMethod: getMethod,
                setMethod: setMethod,
                location: new Location(tree, syntax.Span),
                declarationRef: new SyntaxReference(tree, syntax));
            getMethod?.SetAssociatedProperty(property);
            setMethod?.SetAssociatedProperty(property);
            // Only the accessors are members; the property is found through them
            RecordDeclared(tree, syntax, property);
        }

        private void DeclareExtensionOperator(SyntaxTree tree, OperatorDeclarationSyntax syntax, NamedTypeSymbol container, ExtensionBlockDeclaration block)
        {
            if (!TryGetOperatorMetadataName(syntax, out var metadataName) || !HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_EXT006",
                    DiagnosticSeverity.Error,
                    "An extension operator must be a static operator.",
                    new Location(tree, syntax.Span)));
                return;
            }
            if (!CheckExtensionMemberModifiers(tree, syntax, syntax.Modifiers, block, isStatic: true))
                return;

            var accessibility = DecodeDeclaredAccessibility(
                syntax.Modifiers, GetDefaultTypeMemberAccessibility(container), allowProtected: false, allowInternal: true, diagLocation: new Location(tree, syntax.Span));
            var info = new ExtensionMemberInfo(
                ExtensionMemberKind.Operator, metadataName, isStatic: true, isSetter: false, block.Syntax.TypeParameterList?.Parameters.Count ?? 0, block.GroupingName, block.Reference);
            var method = CreateExtensionImplementation(
                tree, metadataName, container, block, info,
                new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty),
                accessibility, syntax, null, isAsync: false, isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword), isExtern: false);

            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            foreach (var p in syntax.ParameterList.Parameters)
            {
                var parameter = new ParameterSymbol(
                    name: p.Identifier.ValueText ?? "",
                    containing: method,
                    type: new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty),
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));
                parameters.Add(parameter);
                RecordDeclared(tree, p, parameter);
            }
            method.SetParameters(parameters.ToImmutable());
            RecordDeclared(tree, syntax, method);
        }

        private void DeclareMethodDeclaration(SyntaxTree tree, MethodDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var isExtern = HasModifier(syntax.Modifiers, SyntaxKind.ExternKeyword);
            var isOverride = HasModifier(syntax.Modifiers, SyntaxKind.OverrideKeyword);
            var isAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            var isVirtualKeyword = HasModifier(syntax.Modifiers, SyntaxKind.VirtualKeyword);
            var isSealed = HasModifier(syntax.Modifiers, SyntaxKind.SealedKeyword);

            ValidateSealedMethodModifier(tree, syntax, isStatic, isOverride, isAbstract, isVirtualKeyword, isSealed, "method");

            var typeDefaultAcc = GetDefaultTypeMemberAccessibility(container);
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            bool isVirtual = isVirtualKeyword || isOverride || isAbstract;
            if (container.TypeKind == TypeKind.Interface && !isStatic)
            {
                var hasBody = syntax.Body is not null || syntax.ExpressionBody is not null;
                var nonVirtual = declaredAcc == Accessibility.Private || isSealed;
                if (!hasBody && nonVirtual)
                {
                    _diagnostics.Add(new Diagnostic(
                        "CN_IFACE_DEFAULT001",
                        DiagnosticSeverity.Error,
                        "A private or sealed interface method must have a body.",
                        new Location(tree, syntax.Span)));
                }

                isAbstract = !hasBody;
                isVirtual = !hasBody || !nonVirtual;
                isOverride = false;
                isSealed = false;
            }
            // Signature types are bound in a later pass
            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: name,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc,
                isExtensionMethod: syntax.ParameterList.Parameters.Count != 0
                    && HasModifier(syntax.ParameterList.Parameters[0].Modifiers, SyntaxKind.ThisKeyword),
                isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword) || Binder.IsUnsafeContext(container),
                isExtern: isExtern);

            if (syntax.ExplicitInterfaceSpecifier is not null)
                method.MarkExplicitInterfaceDeclaration();
            method.SetDispatchFlags(isVirtual, isAbstract, isOverride, isSealed);
            var tps = DeclareTypeParameters(tree, method, syntax.TypeParameterList);
            method.SetTypeParameters(tps);
            // Parameter types are bound in a later pass
            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);
                bool isParams = IsParamsParameter(p);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: isParams);

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        internal static bool IsParamsParameter(ParameterSyntax p)
            => HasModifier(p.Modifiers, SyntaxKind.ParamsKeyword);
        internal static bool IsReadOnlyByRefParameter(ParameterSyntax p)
        {
            return HasModifier(p.Modifiers, SyntaxKind.InKeyword)
                || (HasModifier(p.Modifiers, SyntaxKind.RefKeyword)
                && HasModifier(p.Modifiers, SyntaxKind.ReadOnlyKeyword));
        }
        /// <summary>Maps parameter modifiers to their semantic reference kind</summary>
        internal static ParameterRefKind GetParameterRefKind(ParameterSyntax p)
        {
            if (HasModifier(p.Modifiers, SyntaxKind.RefKeyword)) return ParameterRefKind.Ref;
            if (HasModifier(p.Modifiers, SyntaxKind.OutKeyword)) return ParameterRefKind.Out;
            if (HasModifier(p.Modifiers, SyntaxKind.InKeyword)) return ParameterRefKind.In;
            return ParameterRefKind.None;
        }
        internal static bool IsScopedParameter(ParameterSyntax p)
            => HasModifier(p.Modifiers, SyntaxKind.ScopedKeyword);
        private void DeclareOperatorDeclaration(SyntaxTree tree, OperatorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            if (!TryGetOperatorMetadataName(syntax, out var metadataName))
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_OPDECL001",
                    DiagnosticSeverity.Error,
                    $"Unsupported operator declaration '{metadataName}'.",
                    new Location(tree, syntax.Span)));
                return;
            }

            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var isOverride = HasModifier(syntax.Modifiers, SyntaxKind.OverrideKeyword);
            var isAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            var isVirtualKeyword = HasModifier(syntax.Modifiers, SyntaxKind.VirtualKeyword);
            var isSealed = HasModifier(syntax.Modifiers, SyntaxKind.SealedKeyword);

            ValidateSealedMethodModifier(tree, syntax, isStatic, isOverride, isAbstract, isVirtualKeyword, isSealed, "operator");
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                GetDefaultTypeMemberAccessibility(container),
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            bool isVirtual = isVirtualKeyword || isOverride || isAbstract;

            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: metadataName,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc);

            if (syntax.ExplicitInterfaceSpecifier is not null)
                method.MarkExplicitInterfaceDeclaration();
            method.SetDispatchFlags(isVirtual, isAbstract, isOverride, isSealed);
            method.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        private void DeclareConversionOperatorDeclaration(
            SyntaxTree tree, ConversionOperatorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            bool isExplicit = syntax.ImplicitOrExplicitKeyword.Kind == SyntaxKind.ExplicitKeyword;
            bool isChecked = syntax.CheckedKeyword.Span.Length != 0;

            if (isChecked && !isExplicit)
            {
                _diagnostics.Add(new Diagnostic(
                    "CN_OPDECL002",
                    DiagnosticSeverity.Error,
                    "Checked conversion operator can only be explicit.",
                    new Location(tree, syntax.Span)));
            }

            string metadataName = isChecked
                ? $"{OpetatorPrefix}CheckedExplicit"
                : (isExplicit ? $"{OpetatorPrefix}Explicit" : $"{OpetatorPrefix}Implicit");

            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                GetDefaultTypeMemberAccessibility(container),
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            var placeholderReturn = new ErrorTypeSymbol("unbound-return", containing: null, ImmutableArray<Location>.Empty);

            var method = new SourceMethodSymbol(
                name: metadataName,
                containing: container,
                returnType: placeholderReturn,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: false,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc);

            if (syntax.ExplicitInterfaceSpecifier is not null)
                method.MarkExplicitInterfaceDeclaration();
            bool isConversionAbstract = HasModifier(syntax.Modifiers, SyntaxKind.AbstractKeyword);
            bool isConversionVirtual = isConversionAbstract || HasModifier(syntax.Modifiers, SyntaxKind.VirtualKeyword);
            method.SetDispatchFlags(isVirtual: isConversionVirtual, isAbstract: isConversionAbstract, isOverride: false, isSealed: false);
            method.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: method,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            method.SetParameters(ps.ToImmutable());
            method.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, method);
            RecordDeclared(tree, syntax, method);
        }
        private static bool TryGetOperatorMetadataName(OperatorDeclarationSyntax syntax, out string name)
        {
            bool isChecked = syntax.CheckedKeyword.Span.Length != 0;
            string op = syntax.OperatorToken.ValueText ?? string.Empty;
            int arity = syntax.ParameterList.Parameters.Count;

            switch (op)
            {
                case "+":
                    if (arity == 1) { name = $"{OpetatorPrefix}UnaryPlus"; return true; }
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedAddition" : $"{OpetatorPrefix}Addition"; return true;
                    }
                    break;

                case "-":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedUnaryNegation" : $"{OpetatorPrefix}UnaryNegation"; return true;
                    }
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedSubtraction" : $"{OpetatorPrefix}Subtraction"; return true;
                    }
                    break;

                case "*":
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedMultiply" : $"{OpetatorPrefix}Multiply"; return true;
                    }
                    break;
                case "/":
                    if (arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDivision" : $"{OpetatorPrefix}Division"; return true;
                    }
                    break;
                case "%":
                    if (arity == 2) { name = $"{OpetatorPrefix}Modulus"; return true; }
                    break;

                case "&":
                    if (arity == 2) { name = $"{OpetatorPrefix}BitwiseAnd"; return true; }
                    break;
                case "|":
                    if (arity == 2) { name = $"{OpetatorPrefix}BitwiseOr"; return true; }
                    break;
                case "^":
                    if (arity == 2) { name = $"{OpetatorPrefix}ExclusiveOr"; return true; }
                    break;

                case "<<":
                    if (arity == 2) { name = $"{OpetatorPrefix}LeftShift"; return true; }
                    break;
                case ">>":
                    if (arity == 2) { name = $"{OpetatorPrefix}RightShift"; return true; }
                    break;
                case ">>>":
                    if (arity == 2) { name = $"{OpetatorPrefix}UnsignedRightShift"; return true; }
                    break;

                case "==":
                    if (arity == 2) { name = $"{OpetatorPrefix}Equality"; return true; }
                    break;
                case "!=":
                    if (arity == 2) { name = $"{OpetatorPrefix}Inequality"; return true; }
                    break;
                case "<":
                    if (arity == 2) { name = $"{OpetatorPrefix}LessThan"; return true; }
                    break;
                case "<=":
                    if (arity == 2) { name = $"{OpetatorPrefix}LessThanOrEqual"; return true; }
                    break;
                case ">":
                    if (arity == 2) { name = $"{OpetatorPrefix}GreaterThan"; return true; }
                    break;
                case ">=":
                    if (arity == 2) { name = $"{OpetatorPrefix}GreaterThanOrEqual"; return true; }
                    break;

                case "!":
                    if (arity == 1) { name = $"{OpetatorPrefix}LogicalNot"; return true; }
                    break;
                case "~":
                    if (arity == 1) { name = $"{OpetatorPrefix}OnesComplement"; return true; }
                    break;

                case "++":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedIncrement"
                            : $"{OpetatorPrefix}Increment";
                        return true;
                    }
                    if (arity == 0)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedIncrementAssignment"
                            : $"{OpetatorPrefix}IncrementAssignment";
                        return true;
                    }
                    break;

                case "--":
                    if (arity == 1)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDecrement"
                            : $"{OpetatorPrefix}Decrement";
                        return true;
                    }
                    if (arity == 0)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDecrementAssignment"
                            : $"{OpetatorPrefix}DecrementAssignment";
                        return true;
                    }
                    break;

                case "true":
                    if (arity == 1) { name = $"{OpetatorPrefix}True"; return true; }
                    break;
                case "false":
                    if (arity == 1) { name = $"{OpetatorPrefix}False"; return true; }
                    break;

                case "+=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedAdditionAssignment" : $"{OpetatorPrefix}AdditionAssignment"; return true;
                    }
                    break;
                case "-=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedSubtractionAssignment" : $"{OpetatorPrefix}SubtractionAssignment"; return true;
                    }
                    break;
                case "*=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedMultiplyAssignment" : $"{OpetatorPrefix}MultiplyAssignment"; return true;
                    }
                    break;
                case "/=":
                    if (arity == 1 || arity == 2)
                    {
                        name = isChecked
                            ? $"{OpetatorPrefix}CheckedDivisionAssignment" : $"{OpetatorPrefix}DivisionAssignment"; return true;
                    }
                    break;

                case "%=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}ModulusAssignment"; return true; }
                    break;
                case "&=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}BitwiseAndAssignment"; return true; }
                    break;
                case "|=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}BitwiseOrAssignment"; return true; }
                    break;
                case "^=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}ExclusiveOrAssignment"; return true; }
                    break;
                case "<<=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}LeftShiftAssignment"; return true; }
                    break;
                case ">>=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}RightShiftAssignment"; return true; }
                    break;
                case ">>>=":
                    if (arity == 1 || arity == 2) { name = $"{OpetatorPrefix}UnsignedRightShiftAssignment"; return true; }
                    break;
            }

            name = op;
            return false;
        }
        private void DeclareConstructorDeclaration(SyntaxTree tree, ConstructorDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var isAsync = HasModifier(syntax.Modifiers, SyntaxKind.AsyncKeyword);
            var name = isStatic ? ".cctor" : (syntax.Identifier.ValueText ?? container.Name);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var voidType = _types.GetSpecialType(SpecialType.System_Void);

            var ctor = new SourceMethodSymbol(
                name: name,
                containing: container,
                returnType: voidType,
                typeParameters: default,
                isStatic: isStatic,
                isConstructor: true,
                isAsync: isAsync,
                locations: ImmutableArray.Create(new Location(tree, syntax.Span)),
                declaredAccessibility: declaredAcc,
                isUnsafe: HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword) || Binder.IsUnsafeContext(container));
            ctor.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);
            var ps = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);
                bool isParams = IsParamsParameter(p);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: ctor,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p),
                    isParams: isParams);

                ps.Add(param);
                RecordDeclared(tree, p, param);
            }

            ctor.SetParameters(ps.ToImmutable());
            ctor.AddDeclaration(new SyntaxReference(tree, syntax));

            AddMemberToType(container, ctor);
            RecordDeclared(tree, syntax, ctor);
        }
        private void DeclareIndexerDeclaration(SyntaxTree tree, IndexerDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            const string metadataName = "Item";

            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var typeDefaultAcc = GetDefaultTypeMemberAccessibility(container);

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));

            bool hasGet = false;
            bool hasSet = false;
            AccessorDeclarationSyntax? getAccessorSyntax = null;
            AccessorDeclarationSyntax? setAccessorSyntax = null;

            if (syntax.ExpressionBody is not null)
            {
                hasGet = true;
            }
            else if (syntax.AccessorList is not null)
            {
                var accessors = syntax.AccessorList.Accessors;
                for (int i = 0; i < accessors.Count; i++)
                {
                    var a = accessors[i];
                    switch (a.Kind)
                    {
                        case SyntaxKind.GetAccessorDeclaration:
                            hasGet = true;
                            getAccessorSyntax ??= a;
                            break;
                        case SyntaxKind.SetAccessorDeclaration:
                        case SyntaxKind.InitAccessorDeclaration:
                            hasSet = true;
                            setAccessorSyntax ??= a;
                            break;
                    }
                }
            }

            if (!hasGet && !hasSet)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_IDX000",
                    severity: DiagnosticSeverity.Error,
                    message: "Indexer must declare at least a get or set accessor.",
                    location: new Location(tree, syntax.Span)));
                return;
            }

            bool hasAccessorAccessibility = false;
            var getAccessibility = getAccessorSyntax is null
                ? declaredAcc
                : DecodeAccessorAccessibility(tree, getAccessorSyntax, declaredAcc, hasGet && hasSet, ref hasAccessorAccessibility);
            var setAccessibility = setAccessorSyntax is null
                ? declaredAcc
                : DecodeAccessorAccessibility(tree, setAccessorSyntax, declaredAcc, hasGet && hasSet, ref hasAccessorAccessibility);

            var placeholderType = new ErrorTypeSymbol("unbound-indexer", containing: null, ImmutableArray<Location>.Empty);

            SourceMethodSymbol? getMethod = null;
            SourceMethodSymbol? setMethod = null;

            if (hasGet)
            {
                var loc = new Location(tree, (getAccessorSyntax ?? (SyntaxNode)syntax).Span);
                getMethod = new SourceMethodSymbol(
                    name: $"get_{metadataName}",
                    containing: container,
                    returnType: placeholderType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: getAccessibility);

                getMethod.AddDeclaration(new SyntaxReference(tree, (getAccessorSyntax ?? (SyntaxNode)syntax)));

                AddMemberToType(container, getMethod);
                if (getAccessorSyntax is not null)
                    RecordDeclared(tree, getAccessorSyntax, getMethod);
            }

            if (hasSet)
            {
                var voidType = _types.GetSpecialType(SpecialType.System_Void);
                var loc = new Location(tree, (setAccessorSyntax ?? (SyntaxNode)syntax).Span);

                setMethod = new SourceMethodSymbol(
                    name: $"set_{metadataName}",
                    containing: container,
                    returnType: voidType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: setAccessibility);

                setMethod.AddDeclaration(new SyntaxReference(tree, (setAccessorSyntax ?? (SyntaxNode)syntax)));

                AddMemberToType(container, setMethod);
                if (setAccessorSyntax is not null)
                    RecordDeclared(tree, setAccessorSyntax, setMethod);
            }

            var prop = new SourcePropertySymbol(
                name: metadataName,
                containing: container,
                declaredTypeSyntax: syntax.Type,
                placeholderType: placeholderType,
                isStatic: isStatic,
                declaredAccessibility: declaredAcc,
                hasGet: hasGet,
                hasSet: hasSet,
                getMethod: getMethod,
                setMethod: setMethod,
                location: new Location(tree, syntax.Span),
                declarationRef: new SyntaxReference(tree, syntax));

            if (syntax.ExplicitInterfaceSpecifier is not null)
            {
                prop.MarkExplicitInterfaceDeclaration();
                getMethod?.MarkExplicitInterfaceDeclaration();
                setMethod?.MarkExplicitInterfaceDeclaration();
            }
            SetAccessorDispatchFlags(tree, syntax, syntax.Modifiers, container, isStatic, declaredAcc, syntax.ExpressionBody is not null,
                getMethod, getAccessorSyntax, setMethod, setAccessorSyntax);
            getMethod?.SetAssociatedProperty(prop);
            setMethod?.SetAssociatedProperty(prop);

            // Property parameters
            var propParams = ImmutableArray.CreateBuilder<ParameterSymbol>(syntax.ParameterList.Parameters.Count);
            for (int i = 0; i < syntax.ParameterList.Parameters.Count; i++)
            {
                var p = syntax.ParameterList.Parameters[i];
                var pName = p.Identifier.ValueText ?? "";
                var pType = new ErrorTypeSymbol("unbound-param", containing: null, ImmutableArray<Location>.Empty);

                var param = new ParameterSymbol(
                    name: pName,
                    containing: prop,
                    type: pType,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)),
                    isReadOnlyRef: IsReadOnlyByRefParameter(p),
                    refKind: GetParameterRefKind(p),
                    isScoped: IsScopedParameter(p));

                propParams.Add(param);
                RecordDeclared(tree, p, param);
            }

            prop.SetParameters(propParams.ToImmutable());

            // Getter parameters mirror indexer parameters
            if (getMethod is not null)
            {
                var getParams = ImmutableArray.CreateBuilder<ParameterSymbol>(propParams.Count);
                for (int i = 0; i < propParams.Count; i++)
                {
                    var src = propParams[i];
                    getParams.Add(new ParameterSymbol(
                        name: src.Name,
                        containing: getMethod,
                        type: src.Type,
                        locations: src.Locations,
                        isReadOnlyRef: src.IsReadOnlyRef));
                }
                getMethod.SetParameters(getParams.ToImmutable());
            }

            // Setter parameters append the implicit value parameter to indexer parameters
            if (setMethod is not null)
            {
                var setParams = ImmutableArray.CreateBuilder<ParameterSymbol>(propParams.Count + 1);
                for (int i = 0; i < propParams.Count; i++)
                {
                    var src = propParams[i];
                    setParams.Add(new ParameterSymbol(
                        name: src.Name,
                        containing: setMethod,
                        type: src.Type,
                        locations: src.Locations,
                        isReadOnlyRef: src.IsReadOnlyRef));
                }

                setParams.Add(new ParameterSymbol(
                    name: "value",
                    containing: setMethod,
                    type: placeholderType,
                    locations: ImmutableArray.Create(new Location(tree, syntax.Span))));

                setMethod.SetParameters(setParams.ToImmutable());
            }

            AddMemberToType(container, prop);
            RecordDeclared(tree, syntax, prop);
        }
        private void DeclarePropertyDeclaration(SyntaxTree tree, PropertyDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            if (name.Length == 0)
                return;

            bool isStatic = HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            var typeDefaultAcc = GetDefaultTypeMemberAccessibility(container);

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: true,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            bool hasGet = false;
            bool hasSet = false;

            AccessorDeclarationSyntax? getAccessorSyntax = null;
            AccessorDeclarationSyntax? setAccessorSyntax = null;

            if (syntax.ExpressionBody is not null)
            {
                // Expression-bodied property
                hasGet = true;
            }
            else if (syntax.AccessorList is not null)
            {
                var accessors = syntax.AccessorList.Accessors;
                for (int i = 0; i < accessors.Count; i++)
                {
                    var a = accessors[i];
                    switch (a.Kind)
                    {
                        case SyntaxKind.GetAccessorDeclaration:
                            hasGet = true;
                            getAccessorSyntax ??= a;
                            break;
                        case SyntaxKind.SetAccessorDeclaration:
                        case SyntaxKind.InitAccessorDeclaration:
                            hasSet = true;
                            setAccessorSyntax ??= a;
                            break;
                    }
                }
            }
            if (!hasGet && !hasSet)
            {
                _diagnostics.Add(new Diagnostic(
                    id: "CN_PROP000",
                    severity: DiagnosticSeverity.Error,
                    message: $"Property '{name}' must declare at least a get or set accessor.",
                    location: new Location(tree, syntax.Span)));
                return;
            }
            bool hasAccessorAccessibility = false;
            var getAccessibility = getAccessorSyntax is null
                ? declaredAcc
                : DecodeAccessorAccessibility(tree, getAccessorSyntax, declaredAcc, hasGet && hasSet, ref hasAccessorAccessibility);
            var setAccessibility = setAccessorSyntax is null
                ? declaredAcc
                : DecodeAccessorAccessibility(tree, setAccessorSyntax, declaredAcc, hasGet && hasSet, ref hasAccessorAccessibility);

            var placeholderType = new ErrorTypeSymbol("unbound-property", containing: null, ImmutableArray<Location>.Empty);

            SourceMethodSymbol? getMethod = null;
            SourceMethodSymbol? setMethod = null;

            if (hasGet)
            {
                var loc = new Location(tree, (getAccessorSyntax ?? syntax as SyntaxNode).Span);
                getMethod = new SourceMethodSymbol(
                    name: $"get_{name}",
                    containing: container,
                    returnType: placeholderType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: getAccessibility);

                getMethod.SetParameters(ImmutableArray<ParameterSymbol>.Empty);
                getMethod.AddDeclaration(new SyntaxReference(tree, (getAccessorSyntax ?? syntax as SyntaxNode)));

                AddMemberToType(container, getMethod);
                if (getAccessorSyntax is not null)
                    RecordDeclared(tree, getAccessorSyntax, getMethod);
            }

            if (hasSet)
            {
                var voidType = _types.GetSpecialType(SpecialType.System_Void);
                var loc = new Location(tree, (setAccessorSyntax ?? syntax as SyntaxNode).Span);

                setMethod = new SourceMethodSymbol(
                    name: $"set_{name}",
                    containing: container,
                    returnType: voidType,
                    typeParameters: ImmutableArray<TypeParameterSymbol>.Empty,
                    isStatic: isStatic,
                    isConstructor: false,
                    isAsync: false,
                    locations: ImmutableArray.Create(loc),
                    declaredAccessibility: setAccessibility);

                // Implicit value parameter
                var valueParam = new ParameterSymbol(
                    name: "value",
                    containing: setMethod,
                    type: placeholderType,
                    locations: ImmutableArray.Create(loc));

                setMethod.SetParameters(ImmutableArray.Create(valueParam));
                setMethod.AddDeclaration(new SyntaxReference(tree, (setAccessorSyntax ?? syntax as SyntaxNode)));

                AddMemberToType(container, setMethod);
                if (setAccessorSyntax is not null)
                    RecordDeclared(tree, setAccessorSyntax, setMethod);
            }
            var prop = new SourcePropertySymbol(
                name: name,
                containing: container,
                declaredTypeSyntax: syntax.Type,
                placeholderType: placeholderType,
                isStatic: isStatic,
                declaredAccessibility: declaredAcc,
                hasGet: hasGet,
                hasSet: hasSet,
                getMethod: getMethod,
                setMethod: setMethod,
                location: new Location(tree, syntax.Span),
                declarationRef: new SyntaxReference(tree, syntax));

            if (syntax.ExplicitInterfaceSpecifier is not null)
            {
                prop.MarkExplicitInterfaceDeclaration();
                getMethod?.MarkExplicitInterfaceDeclaration();
                setMethod?.MarkExplicitInterfaceDeclaration();
            }
            SetAccessorDispatchFlags(tree, syntax, syntax.Modifiers, container, isStatic, declaredAcc, syntax.ExpressionBody is not null,
                getMethod, getAccessorSyntax, setMethod, setAccessorSyntax);
            getMethod?.SetAssociatedProperty(prop);
            setMethod?.SetAssociatedProperty(prop);

            AddMemberToType(container, prop);
            RecordDeclared(tree, syntax, prop);
        }
        private void DeclareFieldDeclaration(SyntaxTree tree, FieldDeclarationSyntax syntax, NamedTypeSymbol container)
        {
            bool isConst = HasModifier(syntax.Modifiers, SyntaxKind.ConstKeyword);
            bool isStatic = isConst || HasModifier(syntax.Modifiers, SyntaxKind.StaticKeyword);
            bool isReadOnly = !isConst && HasModifier(syntax.Modifiers, SyntaxKind.ReadOnlyKeyword);
            bool isUnsafe = HasModifier(syntax.Modifiers, SyntaxKind.UnsafeKeyword) || Binder.IsUnsafeContext(container);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;

            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            var placeholderType = new ErrorTypeSymbol("unbound-field", containing: null, ImmutableArray<Location>.Empty);
            var decl = syntax.Declaration;

            for (int i = 0; i < decl.Variables.Count; i++)
            {
                var v = decl.Variables[i];
                var name = v.Identifier.ValueText ?? "";
                if (name.Length == 0)
                    continue;

                var field = new SourceFieldSymbol(
                    name: name,
                    containing: container,
                    declaredTypeSyntax: decl.Type,
                    placeholderType: placeholderType,
                    isStatic: isStatic,
                    isConst: isConst,
                    isReadOnly: isReadOnly,
                    isUnsafe: isUnsafe,
                    declaredAccessibility: declaredAcc,
                    location: new Location(tree, v.Span),
                    declarationRef: new SyntaxReference(tree, v),
                    attributeOwnerDeclarationRef: new SyntaxReference(tree, syntax));

                AddMemberToType(container, field);

                RecordDeclared(tree, v, field);
            }
        }
        private void DeclareEnumDeclaration(SyntaxTree tree, EnumDeclarationSyntax syntax, Symbol container)
        {
            var name = syntax.Identifier.ValueText ?? "";
            var key = (container, name, arity: 0);
            var typeDefaultAcc = container is NamedTypeSymbol
                ? Accessibility.Private
                : Accessibility.Internal;
            NamedTypeSymbol type;
            var declaredAcc = DecodeDeclaredAccessibility(
                syntax.Modifiers,
                typeDefaultAcc,
                allowProtected: container is NamedTypeSymbol,
                allowInternal: true,
                diagLocation: new Location(tree, syntax.Span));
            if (!_typeCache.TryGetValue(key, out var existing))
            {
                var s = new SourceNamedTypeSymbol(name, container, TypeKind.Enum, arity: 0, declaredAcc);
                s.SetDefaultBaseType(GetDefaultBaseType(TypeKind.Enum));
                s.SetTypeParameters(ImmutableArray<TypeParameterSymbol>.Empty);

                type = s;
                _typeCache.Add(key, type);

                if (container is SourceNamespaceSymbol ns)
                    ns.AddType(type);
                else if (container is NamedTypeSymbol nt)
                    AddNestedTypeToType(nt, type);
                else
                    throw new InvalidOperationException($"Invalid container for enum: {container.Kind}");
            }
            else
            {
                type = existing;

                if (type.TypeKind != TypeKind.Enum)
                {
                    _diagnostics.Add(new Diagnostic(
                        id: "CN0002",
                        severity: DiagnosticSeverity.Error,
                        message: $"Type '{name}' conflicts with enum declaration.",
                        location: new Location(tree, syntax.Span)));
                }
            }

            if (type is SourceNamedTypeSymbol src)
                src.AddDeclaration(tree, syntax);

            RecordDeclared(tree, syntax, type);

            if (type.TypeKind != TypeKind.Enum || type is not NamedTypeSymbol enumType)
                return;

            for (int i = 0; i < syntax.Members.Count; i++)
            {
                var em = syntax.Members[i];
                var memberName = em.Identifier.ValueText ?? "";
                if (memberName.Length == 0)
                    continue;

                var field = new SourceFieldSymbol(
                    name: memberName,
                    containing: enumType,
                    declaredTypeSyntax: null,
                    placeholderType: enumType,
                    isStatic: true,
                    isConst: true,
                    isReadOnly: false,
                    isUnsafe: false,
                    declaredAccessibility: Accessibility.Public,
                    location: new Location(tree, em.Span),
                    declarationRef: new SyntaxReference(tree, em),
                    attributeOwnerDeclarationRef: new SyntaxReference(tree, em));

                AddMemberToType(enumType, field);
                RecordDeclared(tree, em, field);
            }
        }
        private ImmutableArray<TypeParameterSymbol> DeclareTypeParameters(
            SyntaxTree tree, Symbol owner, TypeParameterListSyntax? list)
        {
            if (list == null || list.Parameters.Count == 0)
                return ImmutableArray<TypeParameterSymbol>.Empty;

            var b = ImmutableArray.CreateBuilder<TypeParameterSymbol>(list.Parameters.Count);
            for (int i = 0; i < list.Parameters.Count; i++)
            {
                var p = list.Parameters[i];
                var name = p.Identifier.ValueText ?? "";
                var tp = new TypeParameterSymbol(
                    name: name,
                    containing: owner,
                    ordinal: i,
                    locations: ImmutableArray.Create(new Location(tree, p.Span)));

                b.Add(tp);
                RecordDeclared(tree, p, tp);
            }
            return b.ToImmutable();
        }
        private static IEnumerable<string> EnumerateDottedNameParts(NameSyntax name)
        {
            switch (name)
            {
                case IdentifierNameSyntax id:
                    yield return id.Identifier.ValueText ?? "";
                    yield break;

                case QualifiedNameSyntax q:
                    foreach (var left in EnumerateDottedNameParts(q.Left))
                        yield return left;

                    yield return q.Right switch
                    {
                        IdentifierNameSyntax rid => rid.Identifier.ValueText ?? "",
                        GenericNameSyntax rg => rg.Identifier.ValueText ?? "",
                        _ => ""
                    };
                    yield break;

                default:
                    yield return "";
                    yield break;
            }
        }
    }
}
