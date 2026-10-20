using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Cnidaria.Cs;

internal sealed class RecordTypeInfo
{
    internal SourceNamedTypeSymbol Type { get; }
    internal SyntaxTree Tree { get; private set; }
    internal RecordDeclarationSyntax Syntax { get; private set; }
    internal SourceMethodSymbol? PrimaryConstructor { get; set; }
    internal List<(SourceMethodSymbol Method, RecordMemberKind Kind, FieldSymbol? Field)> Members { get; } = new();
    internal List<Symbol> PositionalMembers { get; } = new();
    internal ImmutableArray<BoundMethodBody> Bodies { get; set; }
    internal bool MembersDeclared { get; set; }

    internal RecordTypeInfo(SourceNamedTypeSymbol type, SyntaxTree tree, RecordDeclarationSyntax syntax)
    {
        Type = type;
        Tree = tree;
        Syntax = syntax;
    }

    internal void AddDeclaration(SyntaxTree tree, RecordDeclarationSyntax syntax, DiagnosticBag diagnostics)
    {
        if (syntax.ParameterList is null || ReferenceEquals(syntax, Syntax))
            return;
        if (Syntax.ParameterList is not null)
        {
            diagnostics.Add(new Diagnostic("CS8863", DiagnosticSeverity.Error,
                "Only a single partial type declaration may have a parameter list.", new Location(tree, syntax.ParameterList.Span)));
            return;
        }
        Tree = tree;
        Syntax = syntax;
    }
}

internal enum RecordMemberKind
{
    PrimaryConstructor, CopyConstructor, Clone, Get, Set, EqualityContract,
    Equals, ObjectEquals, BaseEquals, Equality, Inequality, GetHashCode, PrintMembers, ToString, Deconstruct
}

internal sealed partial class DeclarationBuilder
{
    private void DeclareRecordConstructors()
    {
        foreach (var type in _typeCache.Values.OfType<SourceNamedTypeSymbol>())
        {
            if (type.RecordInfo is not RecordTypeInfo record || record.Syntax.ParameterList is not ParameterListSyntax list)
                continue;
            var constructor = new SourceMethodSymbol(".ctor", type, _types.GetSpecialType(SpecialType.System_Void),
                ImmutableArray<TypeParameterSymbol>.Empty, false, true, false,
                ImmutableArray.Create(new Location(record.Tree, list.Span)),
                type.IsAbstract ? Accessibility.Protected : Accessibility.Public);
            var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(list.Parameters.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool seenOptional = false;
            foreach (var syntax in list.Parameters)
            {
                if (!names.Add(syntax.Identifier.ValueText ?? ""))
                    _diagnostics.Add(new Diagnostic("CS0100", DiagnosticSeverity.Error,
                        "A parameter name must be unique.", new Location(record.Tree, syntax.Span)));
                var refKind = GetParameterRefKind(syntax);
                if (refKind == ParameterRefKind.Out || refKind == ParameterRefKind.Ref && !IsReadOnlyByRefParameter(syntax) || HasModifier(syntax.Modifiers, SyntaxKind.ThisKeyword))
                    _diagnostics.Add(new Diagnostic("CS0631", DiagnosticSeverity.Error,
                        "Record parameters cannot have 'ref', 'out', or 'this' modifiers.", new Location(record.Tree, syntax.Span)));
                if (IsParamsParameter(syntax) && parameters.Count + 1 != list.Parameters.Count)
                    _diagnostics.Add(new Diagnostic("CS0231", DiagnosticSeverity.Error,
                        "A params parameter must be the last parameter.", new Location(record.Tree, syntax.Span)));
                if (syntax.Default is null && seenOptional && !IsParamsParameter(syntax))
                    _diagnostics.Add(new Diagnostic("CS1737", DiagnosticSeverity.Error,
                        "Optional parameters must appear after all required parameters.", new Location(record.Tree, syntax.Span)));
                seenOptional |= syntax.Default is not null;
                var parameter = new ParameterSymbol(syntax.Identifier.ValueText ?? "", constructor,
                    new ErrorTypeSymbol("unbound-param", null, ImmutableArray<Location>.Empty),
                    ImmutableArray.Create(new Location(record.Tree, syntax.Span)),
                    IsReadOnlyByRefParameter(syntax), GetParameterRefKind(syntax), IsScopedParameter(syntax), IsParamsParameter(syntax));
                parameters.Add(parameter);
                RecordDeclared(record.Tree, syntax, parameter);
            }
            constructor.SetParameters(parameters.ToImmutable());
            constructor.AddDeclaration(new SyntaxReference(record.Tree, list));
            type.AddMember(constructor);
            record.PrimaryConstructor = constructor;
            record.Members.Add((constructor, RecordMemberKind.PrimaryConstructor, null));
            RecordDeclared(record.Tree, list, constructor);
        }
    }
}

internal static partial class RecordBinder
{
    internal static void DeclareMembers(Compilation compilation, DiagnosticBag diagnostics)
    {
        foreach (var record in GetRecords(compilation))
            DeclareMembers(compilation, record, diagnostics);
        foreach (var type in compilation.DeclaredSymbolsByTree.Values.SelectMany(m => m.Values).OfType<NamedTypeSymbol>().Distinct())
        {
            if (type.TypeKind == TypeKind.Class && !type.IsRecord && type.BaseType is NamedTypeSymbol { IsRecord: true })
                diagnostics.Add(new Diagnostic("CS8865", DiagnosticSeverity.Error, "Only records may inherit from records.", type.Locations.FirstOrDefault()));
        }
    }

    private static void DeclareMembers(Compilation compilation, RecordTypeInfo record, DiagnosticBag diagnostics)
    {
        if (record.MembersDeclared)
            return;
        record.MembersDeclared = true;
        var type = record.Type;
        if (type.BaseType is NamedTypeSymbol { OriginalDefinition: SourceNamedTypeSymbol { RecordInfo: RecordTypeInfo baseInfo } })
            DeclareMembers(compilation, baseInfo, diagnostics);
        var baseRecord = type.BaseType as NamedTypeSymbol;
        if (type.IsReferenceType && baseRecord is not null && baseRecord.SpecialType != SpecialType.System_Object && !baseRecord.IsRecord)
            Error(record, diagnostics, "CS8864", "Records may only inherit from object or another record.");
        if (baseRecord?.IsRecord != true)
            baseRecord = null;
        if (type.IsRefLikeType)
            Error(record, diagnostics, "CS0106", "A record struct cannot be ref-like.");
        if (record.PrimaryConstructor is null && record.Syntax.BaseList?.Types.Any(t => t is PrimaryConstructorBaseTypeSyntax) == true)
            Error(record, diagnostics, "CS8861", "A record base argument list requires a primary constructor.");
        if (type.IsValueType && !type.GetMembers().OfType<MethodSymbol>().Any(m => m.IsConstructor && !m.IsStatic) &&
            type.GetMembers().Any(m => m is FieldSymbol { IsStatic: false } or PropertySymbol { IsStatic: false } &&
                m.DeclaringSyntaxReferences.Any(r => r.Node is VariableDeclaratorSyntax { Initializer: not null } or PropertyDeclarationSyntax { Initializer: not null })))
            Error(record, diagnostics, "CS8983", "A record struct with field initializers must declare a constructor.");
        foreach (var member in type.GetMembers())
        {
            if (member.Name == "Clone")
                Error(record, diagnostics, "CS8859", "Members named 'Clone' are disallowed in records.");
            if (member is FieldSymbol { IsStatic: false, Type: PointerTypeSymbol or FunctionPointerTypeSymbol or ByRefTypeSymbol })
                Error(record, diagnostics, "CS8908", "A record instance field cannot have a pointer or by-reference type.");
        }

        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        var intType = compilation.GetSpecialType(SpecialType.System_Int32);
        var voidType = compilation.GetSpecialType(SpecialType.System_Void);
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var equatable = FindType(compilation, "System", "IEquatable", 1);
        var runtimeType = FindType(compilation, "System", "Type");
        var builderType = FindType(compilation, "System.Text", "StringBuilder");
        if (equatable is null || runtimeType is null || builderType is null || FindType(compilation, "System.Collections.Generic", "EqualityComparer", 1) is null)
        {
            Error(record, diagnostics, "CS0518", "Record synthesis requires System.Type, IEquatable<T>, EqualityComparer<T>, and StringBuilder.");
            return;
        }
        type.AddSynthesizedInterface(compilation.ConstructNamedType(equatable, ImmutableArray.Create<TypeSymbol>(type)));

        if (record.PrimaryConstructor is SourceMethodSymbol primary)
        {
            if (type.IsReferenceType && IsCopyConstructor(primary))
                Error(record, diagnostics, "CS8910", "A record primary constructor cannot have the same signature as its copy constructor.");
            if (type.GetMembers().OfType<MethodSymbol>().Any(m => !ReferenceEquals(m, primary) && m.IsConstructor && !m.IsStatic &&
                m.Parameters.Length == primary.Parameters.Length && m.Parameters.Select((p, i) => SameType(p.Type, primary.Parameters[i].Type)).All(equal => equal)))
                Error(record, diagnostics, "CS0111", "A constructor with the same parameter types as the primary constructor is already declared.");
            var syntaxParameters = record.Syntax.ParameterList!.Parameters;
            int positionalFieldCount = 0;
            for (int i = 0; i < primary.Parameters.Length; i++)
            {
                var parameter = primary.Parameters[i];
                if (parameter.Name == "Clone")
                    Error(record, diagnostics, "CS8859", "Members named 'Clone' are disallowed in records.");
                var parameterType = parameter.Type is ByRefTypeSymbol byRef ? byRef.ElementType : parameter.Type;
                if (parameterType is PointerTypeSymbol or FunctionPointerTypeSymbol || parameterType.IsRefLikeType)
                    Error(record, diagnostics, "CS8908", "A record positional member cannot have a pointer or ref-like type.");
                var existing = FindMember(type, parameter.Name);
                if (existing is PropertySymbol { IsStatic: false, HasGet: true } property && SameType(property.Type, parameterType) &&
                    (ReferenceEquals(property.ContainingSymbol, type) || property.GetMethod?.DeclaredAccessibility == Accessibility.Public))
                {
                    if (property.GetMethod?.IsAbstract != true)
                    {
                        record.PositionalMembers.Add(property);
                        continue;
                    }
                }
                else if (existing is FieldSymbol { IsStatic: false } field && SameType(field.Type, parameterType) &&
                         (ReferenceEquals(field.ContainingSymbol, type) || field.DeclaredAccessibility == Accessibility.Public))
                {
                    record.PositionalMembers.Add(field);
                    continue;
                }
                else if (existing is not null)
                {
                    Error(record, diagnostics, "CS8866", $"Record member '{parameter.Name}' must be a readable instance property or field of type '{parameterType.Name}'.");
                    record.PositionalMembers.Add(existing);
                    continue;
                }
                var get = AddMethod(record, "get_" + parameter.Name, parameterType, RecordMemberKind.Get);
                var set = AddMethod(record, "set_" + parameter.Name, voidType, RecordMemberKind.Set, ("value", parameterType));
                if (type.IsReferenceType || type.IsReadOnlyStruct)
                    set.MarkInitOnly();
                var synthesizedProperty = new SourcePropertySymbol(parameter.Name, type, syntaxParameters[i].Type!, parameterType,
                    false, Accessibility.Public, true, true, get, set,
                    new Location(record.Tree, syntaxParameters[i].Span), new SyntaxReference(record.Tree, syntaxParameters[i]));
                get.SetAssociatedProperty(synthesizedProperty);
                set.SetAssociatedProperty(synthesizedProperty);
                if (existing is PropertySymbol abstractProperty)
                {
                    Override(get, abstractProperty.GetMethod, record, diagnostics);
                    Override(set, abstractProperty.SetMethod, record, diagnostics);
                }
                type.AddMember(synthesizedProperty);
                var backing = synthesizedProperty.EnsureBackingField(set.IsInitOnly);
                // Positional backing fields precede explicit fields in sequential layout
                type.MoveMember(backing, positionalFieldCount++);
                SetField(record, get, backing);
                SetField(record, set, backing);
                record.PositionalMembers.Add(synthesizedProperty);
            }
            if (primary.Parameters.Length != 0)
            {
                var parameters = primary.Parameters.Select(p => (p.Name, (TypeSymbol)compilation.CreateByRefType(p.Type is ByRefTypeSymbol br ? br.ElementType : p.Type))).ToArray();
                var existing = FindMethod(type, "Deconstruct", parameters.Select(p => p.Item2).ToArray(), declaredOnly: true);
                if (existing is null)
                {
                    var deconstruct = AddMethod(record, "Deconstruct", voidType, RecordMemberKind.Deconstruct, parameters);
                    foreach (var parameter in deconstruct.Parameters)
                        parameter.RefKind = ParameterRefKind.Out;
                }
                else if (existing.IsStatic || existing.DeclaredAccessibility != Accessibility.Public || !SameType(existing.ReturnType, voidType) || existing.Parameters.Any(p => p.RefKind != ParameterRefKind.Out))
                    Error(record, diagnostics, "CS8874", "The record Deconstruct method must be public, return void, and have out parameters.");
            }
        }

        if (type.IsReferenceType)
        {
            var copy = type.GetMembers().OfType<MethodSymbol>().FirstOrDefault(IsCopyConstructor);
            if (copy is null)
            {
                copy = AddMethod(record, ".ctor", voidType, RecordMemberKind.CopyConstructor, ("original", type));
            }
            else if (!type.IsSealed && copy.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected))
                Error(record, diagnostics, "CS8878", "A copy constructor in an unsealed record must be public or protected.");
            if (record.PrimaryConstructor is null && !type.GetMembers().OfType<MethodSymbol>().Any(m => m.IsConstructor && !m.IsStatic && !IsCopyConstructor(m)))
                type.AddMember(new SynthesizedConstructorSymbol(type, voidType, false, ImmutableArray<ParameterSymbol>.Empty));
            var clone = AddMethod(record, "<Clone>$", type, RecordMemberKind.Clone);
            clone.SetDispatchFlags(!type.IsSealed || baseRecord is not null, type.IsAbstract, baseRecord is not null, false);
            if (baseRecord is not null)
                Override(clone, FindMethod(baseRecord, "<Clone>$"), record, diagnostics);
            var contract = type.GetMembers().OfType<PropertySymbol>().FirstOrDefault(p => p.Name == "EqualityContract");
            if (contract is null)
            {
                if (type.GetMembers().Any(m => m.Name == "EqualityContract"))
                    Error(record, diagnostics, "CS0102", "The record already contains a definition for 'EqualityContract'.");
                var get = AddMethod(record, "get_EqualityContract", runtimeType, RecordMemberKind.EqualityContract);
                get.SetDispatchFlags(!type.IsSealed || baseRecord is not null, false, baseRecord is not null, false);
                if (baseRecord is not null)
                    Override(get, FindMethod(baseRecord, "get_EqualityContract"), record, diagnostics);
                contract = new SourcePropertySymbol("EqualityContract", type, new IdentifierNameSyntax(record.Syntax.Identifier), runtimeType,
                    false, get.DeclaredAccessibility, true, false, get, null,
                    new Location(record.Tree, record.Syntax.Span), new SyntaxReference(record.Tree, record.Syntax));
                get.SetAssociatedProperty((SourcePropertySymbol)contract);
                type.AddMember(contract);
            }
            else if (contract.IsStatic || contract.HasSet || !SameType(contract.Type, runtimeType) || contract.GetMethod is null ||
                     contract.DeclaredAccessibility != (type.IsSealed && baseRecord is null ? Accessibility.Private : Accessibility.Protected) ||
                     (baseRecord is not null && !contract.GetMethod.IsOverride) ||
                     (!type.IsSealed && (!(contract.GetMethod.IsVirtual || contract.GetMethod.IsOverride) || contract.GetMethod.IsSealed)))
                Error(record, diagnostics, "CS8875", "EqualityContract has an invalid signature, accessibility, or virtual modifier.");
        }

        var typedEquals = FindMethod(type, "Equals", type, declaredOnly: true);
        if (typedEquals is null)
        {
            typedEquals = AddMethod(record, "Equals", boolType, RecordMemberKind.Equals, ("other", type));
            ((SourceMethodSymbol)typedEquals).SetDispatchFlags(type.IsReferenceType && !type.IsSealed, false, false, false);
        }
        else if (typedEquals.IsStatic || typedEquals.DeclaredAccessibility != Accessibility.Public || !SameType(typedEquals.ReturnType, boolType) ||
                 (type.IsReferenceType && !type.IsSealed && (!typedEquals.IsVirtual || typedEquals.IsSealed)))
            Error(record, diagnostics, "CS8872", "The record Equals method must be public and virtual in an unsealed record class.");
        AddRequired(record, diagnostics, "Equals", boolType, RecordMemberKind.ObjectEquals, ("obj", objectType));
        if (baseRecord is not null)
            AddRequired(record, diagnostics, "Equals", boolType, RecordMemberKind.BaseEquals, ("other", baseRecord));
        AddRequired(record, diagnostics, "op_Equality", boolType, RecordMemberKind.Equality, ("left", type), ("right", type));
        AddRequired(record, diagnostics, "op_Inequality", boolType, RecordMemberKind.Inequality, ("left", type), ("right", type));

        var hash = FindMethod(type, "GetHashCode", declaredOnly: true);
        if (hash is null)
            hash = AddMethod(record, "GetHashCode", intType, RecordMemberKind.GetHashCode);
        else if (!hash.IsOverride || hash.IsStatic || !SameType(hash.ReturnType, intType))
            Error(record, diagnostics, "CS8869", "A record GetHashCode method must override object.GetHashCode.");
        bool customEquals = !record.Members.Any(m => ReferenceEquals(m.Method, typedEquals));
        bool customHash = !record.Members.Any(m => ReferenceEquals(m.Method, hash));
        if (customEquals != customHash)
            diagnostics.Add(new Diagnostic("CS8851", DiagnosticSeverity.Warning, "A record should define both Equals and GetHashCode together.", new Location(record.Tree, record.Syntax.Span)));

        var print = FindMethod(type, "PrintMembers", builderType, declaredOnly: true);
        if (print is null)
        {
            print = AddMethod(record, "PrintMembers", boolType, RecordMemberKind.PrintMembers, ("builder", builderType));
            ((SourceMethodSymbol)print).SetDispatchFlags(type.IsReferenceType && (!type.IsSealed || baseRecord is not null), false, baseRecord is not null, false);
            if (baseRecord is not null)
                Override((SourceMethodSymbol)print, FindMethod(baseRecord, "PrintMembers", builderType), record, diagnostics);
        }
        else if (print.IsStatic || !SameType(print.ReturnType, boolType) ||
                 print.DeclaredAccessibility != (type.IsValueType || type.IsSealed && baseRecord is null ? Accessibility.Private : Accessibility.Protected) ||
                 (baseRecord is not null && !print.IsOverride) ||
                 (type.IsReferenceType && !type.IsSealed && (!(print.IsVirtual || print.IsOverride) || print.IsSealed)))
            Error(record, diagnostics, "CS8874", "PrintMembers has an invalid signature, accessibility, or virtual modifier.");

        var toString = FindMethod(type, "ToString", declaredOnly: true);
        if (toString is null && FindMethod(baseRecord, "ToString") is not { IsSealed: true })
            AddMethod(record, "ToString", stringType, RecordMemberKind.ToString);
        else if (toString is not null && (!toString.IsOverride || toString.IsStatic || !SameType(toString.ReturnType, stringType)))
            Error(record, diagnostics, "CS8869", "A record ToString method must override object.ToString.");

        foreach (var (method, kind, _) in record.Members)
        {
            if (type.GetMembers().Any(m => m is not MethodSymbol && m.Name == method.Name))
                Error(record, diagnostics, "CS0102", $"The record already contains a definition for '{method.Name}'.");
            if (kind is RecordMemberKind.ObjectEquals or RecordMemberKind.BaseEquals or RecordMemberKind.GetHashCode or RecordMemberKind.ToString)
            {
                method.SetDispatchFlags(true, false, true, kind == RecordMemberKind.BaseEquals);
                Override(method, FindMethod(type.BaseType as NamedTypeSymbol, method.Name, method.Parameters.Select(p => p.Type).ToArray()), record, diagnostics);
            }
        }
    }

    private static void SetField(RecordTypeInfo record, SourceMethodSymbol method, FieldSymbol field)
    {
        int index = record.Members.FindIndex(m => ReferenceEquals(m.Method, method));
        record.Members[index] = (method, record.Members[index].Kind, field);
    }

    private static SourceMethodSymbol AddMethod(RecordTypeInfo record, string name, TypeSymbol result, RecordMemberKind kind,
        params (string Name, TypeSymbol Type)[] parameters)
    {
        bool isConstructor = kind == RecordMemberKind.CopyConstructor;
        bool isStatic = kind is RecordMemberKind.Equality or RecordMemberKind.Inequality;
        bool hasRecordBase = record.Type.BaseType is NamedTypeSymbol { IsRecord: true };
        var accessibility = kind switch
        {
            RecordMemberKind.CopyConstructor => record.Type.IsSealed ? Accessibility.Private : Accessibility.Protected,
            RecordMemberKind.EqualityContract or RecordMemberKind.PrintMembers => record.Type.IsValueType || record.Type.IsSealed && !hasRecordBase ? Accessibility.Private : Accessibility.Protected,
            _ => Accessibility.Public
        };
        var method = new SourceMethodSymbol(name, record.Type, result, ImmutableArray<TypeParameterSymbol>.Empty,
            isStatic, isConstructor, false, ImmutableArray<Location>.Empty, accessibility);
        method.SetParameters(parameters.Select(p => new ParameterSymbol(p.Name, method, p.Type, ImmutableArray<Location>.Empty)).ToImmutableArray());
        record.Type.AddMember(method);
        record.Members.Add((method, kind, null));
        return method;
    }

    private static void AddRequired(RecordTypeInfo record, DiagnosticBag diagnostics, string name, TypeSymbol result, RecordMemberKind kind,
        params (string Name, TypeSymbol Type)[] parameters)
    {
        if (FindMethod(record.Type, name, parameters.Select(p => p.Type).ToArray(), true) is not null)
            Error(record, diagnostics, "CS0111", $"Record member '{name}' is reserved for compiler synthesis.");
        else
            AddMethod(record, name, result, kind, parameters);
    }

    private static void Override(SourceMethodSymbol method, MethodSymbol? overridden, RecordTypeInfo record, DiagnosticBag diagnostics)
    {
        if (overridden is null || !(overridden.IsVirtual || overridden.IsOverride || overridden.IsAbstract) || overridden.IsSealed)
        {
            Error(record, diagnostics, "CS8869", $"Record member '{method.Name}' has no suitable overridable base member.");
            return;
        }
        method.SetDispatchFlags(true, method.IsAbstract, true, method.IsSealed);
        method.SetOverriddenMethod(overridden);
    }

    private static void Error(RecordTypeInfo record, DiagnosticBag diagnostics, string id, string message)
        => diagnostics.Add(new Diagnostic(id, DiagnosticSeverity.Error, message, new Location(record.Tree, record.Syntax.Span)));

    internal static NamedTypeSymbol? FindType(Compilation compilation, string ns, string name, int arity = 0)
    {
        NamespaceSymbol? current = compilation.GlobalNamespace;
        foreach (string part in ns.Split('.'))
            current = current?.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
        return current?.GetTypeMembers(name, arity).FirstOrDefault();
    }

    private static Symbol? FindMember(NamedTypeSymbol? type, string name)
    {
        for (; type is not null; type = type.BaseType as NamedTypeSymbol)
        {
            var member = type.GetMembers().FirstOrDefault(m => m.Name == name);
            if (member is not null)
                return member;
        }
        return null;
    }

    internal static MethodSymbol? FindMethod(NamedTypeSymbol? type, string name, TypeSymbol? parameter = null, bool declaredOnly = false)
        => FindMethod(type, name, parameter is null ? Array.Empty<TypeSymbol>() : new[] { parameter }, declaredOnly);

    private static MethodSymbol? FindMethod(NamedTypeSymbol? type, string name, TypeSymbol[] parameters, bool declaredOnly = false)
    {
        for (; type is not null; type = declaredOnly ? null : type.BaseType as NamedTypeSymbol)
        {
            foreach (var method in type.GetMembers().OfType<MethodSymbol>())
            {
                if (method.Name == name && method.TypeParameters.Length == 0 && method.Parameters.Length == parameters.Length &&
                    method.Parameters.Select((p, i) => SameType(p.Type, parameters[i])).All(equal => equal))
                    return method;
            }
        }
        return null;
    }

    internal static IEnumerable<RecordTypeInfo> GetRecords(Compilation compilation)
        => compilation.DeclaredSymbolsByTree.Values.SelectMany(m => m.Values).OfType<SourceNamedTypeSymbol>()
            .Distinct().Select(t => t.RecordInfo).OfType<RecordTypeInfo>();

    internal static bool IsCopyConstructor(MethodSymbol method)
        => method.IsConstructor && !method.IsStatic && method.ContainingSymbol is NamedTypeSymbol { IsRecord: true, IsReferenceType: true } type &&
           method.Parameters.Length == 1 && SameType(method.Parameters[0].Type, type);

    internal static void ValidateConstructor(ConstructorDeclarationSyntax syntax, MethodSymbol method, BoundStatement? initializer,
        SyntaxTree tree, DiagnosticBag diagnostics)
    {
        if (method.IsStatic || method.ContainingSymbol is not SourceNamedTypeSymbol { RecordInfo: RecordTypeInfo record } type)
            return;
        if (IsCopyConstructor(method))
        {
            bool valid = type.BaseType?.SpecialType == SpecialType.System_Object
                ? syntax.Initializer is null || initializer is BoundExpressionStatement { Expression: BoundCallExpression { Method.IsConstructor: true, Method.Parameters.Length: 0, Method.ContainingSymbol: TypeSymbol { SpecialType: SpecialType.System_Object } } }
                : initializer is BoundExpressionStatement { Expression: BoundCallExpression call } && IsCopyConstructor(call.Method) &&
                    call.ReceiverOpt is BoundBaseExpression;
            if (!valid)
                diagnostics.Add(new Diagnostic("CS8868", DiagnosticSeverity.Error,
                    "A record copy constructor must call the base copy constructor or the parameterless object constructor.", new Location(tree, syntax.Span)));
        }
        else if (record.PrimaryConstructor is not null && syntax.Initializer?.ThisOrBaseKeyword.Kind != SyntaxKind.ThisKeyword)
            diagnostics.Add(new Diagnostic("CS8862", DiagnosticSeverity.Error,
                "A constructor in a record with a primary constructor must delegate using 'this'.", new Location(tree, syntax.Span)));
    }

    private static bool SameType(TypeSymbol left, TypeSymbol right) => LocalScopeBinder.AreSameType(left, right);
}
