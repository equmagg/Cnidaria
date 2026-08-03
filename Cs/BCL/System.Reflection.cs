namespace System.Reflection
{
    [Flags]
    public enum MemberTypes
    {
        Constructor = 0x01,
        Event = 0x02,
        Field = 0x04,
        Method = 0x08,
        Property = 0x10,
        TypeInfo = 0x20,
        Custom = 0x40,
        NestedType = 0x80,
        All = Constructor | Event | Field | Method | Property | TypeInfo | NestedType,
    }
    [Flags]
    public enum TypeAttributes
    {
        VisibilityMask = 0x00000007,
        NotPublic = 0x00000000,     // Class is not public scope.
        Public = 0x00000001,     // Class is public scope.
        NestedPublic = 0x00000002,     // Class is nested with public visibility.
        NestedPrivate = 0x00000003,     // Class is nested with private visibility.
        NestedFamily = 0x00000004,     // Class is nested with family visibility.
        NestedAssembly = 0x00000005,     // Class is nested with assembly visibility.
        NestedFamANDAssem = 0x00000006,     // Class is nested with family and assembly visibility.
        NestedFamORAssem = 0x00000007,     // Class is nested with family or assembly visibility.

        // Use this mask to retrieve class layout information
        // 0 is AutoLayout, 0x2 is SequentialLayout, 4 is ExplicitLayout
        LayoutMask = 0x00000018,
        AutoLayout = 0x00000000,     // Class fields are auto-laid out
        SequentialLayout = 0x00000008,     // Class fields are laid out sequentially
        ExplicitLayout = 0x00000010,     // Layout is supplied explicitly
                                         // end layout mask

        // Use this mask to distinguish whether a type declaration is an interface.  (Class vs. ValueType done based on whether it subclasses S.ValueType)
        ClassSemanticsMask = 0x00000020,
        Class = 0x00000000,     // Type is a class (or a value type).
        Interface = 0x00000020,     // Type is an interface.

        // Special semantics in addition to class semantics.
        Abstract = 0x00000080,     // Class is abstract
        Sealed = 0x00000100,     // Class is concrete and may not be extended
        SpecialName = 0x00000400,     // Class name is special.  Name describes how.

        // Implementation attributes.
        Import = 0x00001000,     // Class / interface is imported

        // Use tdStringFormatMask to retrieve string information for native interop
        StringFormatMask = 0x00030000,
        AnsiClass = 0x00000000,     // LPTSTR is interpreted as ANSI in this class
        UnicodeClass = 0x00010000,     // LPTSTR is interpreted as UNICODE
        AutoClass = 0x00020000,     // LPTSTR is interpreted automatically
        CustomFormatClass = 0x00030000,     // A non-standard encoding specified by CustomFormatMask
        CustomFormatMask = 0x00C00000,     // Use this mask to retrieve non-standard encoding information for native interop. The meaning of the values of these 2 bits is unspecified.

        // end string format mask

        BeforeFieldInit = 0x00100000,     // Initialize the class any time before first static field access.

        RTSpecialName = 0x00000800,     // Runtime should check name encoding.
        HasSecurity = 0x00040000,     // Class has security associate with it.

        ReservedMask = 0x00040800,
    }
    [Flags]
    public enum BindingFlags
    {
        // a place holder for no flag specified
        Default = 0x00,

        // These flags indicate what to search for when binding
        IgnoreCase = 0x01,          // Ignore the case of Names while searching
        DeclaredOnly = 0x02,        // Only look at the members declared on the Type
        Instance = 0x04,            // Include Instance members in search
        Static = 0x08,              // Include Static members in search
        Public = 0x10,              // Include Public members in search
        NonPublic = 0x20,           // Include Non-Public members in search
        FlattenHierarchy = 0x40,    // Rollup the statics into the class.

        // BindingAccess = 0xFF00;
        InvokeMethod = 0x0100,
        CreateInstance = 0x0200,
        GetField = 0x0400,
        SetField = 0x0800,
        GetProperty = 0x1000,
        SetProperty = 0x2000,

        PutDispProperty = 0x4000,
        PutRefDispProperty = 0x8000,

        ExactBinding = 0x010000,
        SuppressChangeType = 0x020000,

        OptionalParamBinding = 0x040000,

        IgnoreReturn = 0x01000000,
        DoNotWrapExceptions = 0x02000000,
    }
    [Flags]
    public enum CallingConventions
    {
        Standard = 0x0001,
        VarArgs = 0x0002,
        Any = Standard | VarArgs,
        HasThis = 0x0020,
        ExplicitThis = 0x0040,
    }
    public interface IReflect
    {
        Type UnderlyingSystemType { get; }
    }
    public abstract class MemberInfo
    {
        internal virtual bool CacheEquals(object? o) { throw new NotImplementedException(); }
    }
}