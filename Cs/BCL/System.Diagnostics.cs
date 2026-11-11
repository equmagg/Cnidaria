using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Diagnostics
{
    public static class Debugger
    {
        public static bool IsAttached
        {
            get
            {
                // Managed debugger is never attached because we don't have one
                return false;
            }
        }

        public static bool Launch()
        {
            throw new PlatformNotSupportedException();
        }

        public static void NotifyOfCrossThreadDependency()
        {
            // nothing to do...yet
        }
    }

    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class Stopwatch
    {
        private static long GetFrequency()
        {
            const long SecondsToNanoSeconds = 1000000000;
            return SecondsToNanoSeconds;
        }

        public static long GetTimestamp() => Interop.Sys.GetTimestamp();

        private long _elapsed;
        private long _startTimeStamp;
        private bool _isRunning;

        public static readonly long Frequency = GetFrequency();
        public static readonly bool IsHighResolution = true;

        // performance-counter frequency, in counts per ticks.
        // This can speed up conversion to ticks.
        private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Frequency;

        public Stopwatch()
        {
        }

        public void Start()
        {
            // Calling start on a running Stopwatch is a no-op.
            if (!_isRunning)
            {
                _startTimeStamp = GetTimestamp();
                _isRunning = true;
            }
        }

        public static Stopwatch StartNew()
        {
            Stopwatch s = new();
            s.Start();
            return s;
        }

        public void Stop()
        {
            // Calling stop on a stopped Stopwatch is a no-op.
            if (_isRunning)
            {
                _elapsed += GetTimestamp() - _startTimeStamp;
                _isRunning = false;
            }
        }

        public void Reset()
        {
            _elapsed = 0;
            _startTimeStamp = 0;
            _isRunning = false;
        }

        // Convenience method for replacing {sw.Reset(); sw.Start();} with a single sw.Restart()
        public void Restart()
        {
            _elapsed = 0;
            _startTimeStamp = GetTimestamp();
            _isRunning = true;
        }

        /// <summary>
        /// Returns the <see cref="Elapsed"/> time as a string.
        /// </summary>
        /// <returns>
        /// Elapsed time string in the same format used by <see cref="TimeSpan.ToString()"/>.
        /// </returns>
        public override string ToString() => Elapsed.ToString();

        public bool IsRunning => _isRunning;

        public TimeSpan Elapsed => new(ElapsedTimeSpanTicks);

        public long ElapsedMilliseconds => ElapsedTimeSpanTicks / TimeSpan.TicksPerMillisecond;

        public long ElapsedTicks
        {
            get
            {
                long timeElapsed = _elapsed;

                // If the Stopwatch is running, add elapsed time since the Stopwatch is started last time.
                if (_isRunning)
                {
                    timeElapsed += GetTimestamp() - _startTimeStamp;
                }

                return timeElapsed;
            }
        }

        /// <summary>Gets the elapsed time since the <paramref name="startingTimestamp"/> value retrieved using <see cref="GetTimestamp"/>.</summary>
        /// <param name="startingTimestamp">The timestamp marking the beginning of the time period.</param>
        /// <returns>A <see cref="TimeSpan"/> for the elapsed time between the starting timestamp and the time of this call.</returns>
        public static TimeSpan GetElapsedTime(long startingTimestamp) =>
            GetElapsedTime(startingTimestamp, GetTimestamp());

        /// <summary>Gets the elapsed time between two timestamps retrieved using <see cref="GetTimestamp"/>.</summary>
        /// <param name="startingTimestamp">The timestamp marking the beginning of the time period.</param>
        /// <param name="endingTimestamp">The timestamp marking the end of the time period.</param>
        /// <returns>A <see cref="TimeSpan"/> for the elapsed time between the starting and ending timestamps.</returns>
        public static TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            new((long)((endingTimestamp - startingTimestamp) * s_tickFrequency));

        private long ElapsedTimeSpanTicks => (long)(ElapsedTicks * s_tickFrequency);

        private string DebuggerDisplay => $"{Elapsed} (IsRunning = {_isRunning})";
    }

    // Attribute class used by the compiler to mark modules.
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Module, AllowMultiple = false)]
    public sealed class DebuggableAttribute : Attribute
    {
        [Flags]
        public enum DebuggingModes
        {
            None = 0x0,
            Default = 0x1,
            DisableOptimizations = 0x100,
            IgnoreSymbolStoreSequencePoints = 0x2,
            EnableEditAndContinue = 0x4
        }

        public DebuggableAttribute(DebuggingModes modes)
        {
            DebuggingFlags = modes;
        }

        public DebuggingModes DebuggingFlags { get; }
    }

    public enum DebuggerBrowsableState
    {
        Never = 0,
        // Expanded is not supported in this release
        // Expanded = 1,
        Collapsed = 2,
        RootHidden = 3
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class DebuggerBrowsableAttribute : Attribute
    {
        public DebuggerBrowsableAttribute(DebuggerBrowsableState state)
        {
            if (state < DebuggerBrowsableState.Never || state > DebuggerBrowsableState.RootHidden)
                throw new ArgumentOutOfRangeException(nameof(state));

            State = state;
        }
        public DebuggerBrowsableState State { get; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Delegate | AttributeTargets.Enum |
        AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class DebuggerDisplayAttribute : Attribute
    {
        private Type? _target;

        public DebuggerDisplayAttribute(string? value)
        {
            Value = value ?? "";
            Name = "";
            Type = "";
        }

        public string Value { get; }

        public string? Name { get; set; }

        public string? Type { get; set; }

        public Type? Target
        {
            get => _target;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                TargetTypeName = value.AssemblyQualifiedName;
                _target = value;
            }
        }

        public string? TargetTypeName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
    public sealed class DebuggerStepThroughAttribute : Attribute
    {
        public DebuggerStepThroughAttribute() { }
    }
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class FeatureSwitchDefinitionAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FeatureSwitchDefinitionAttribute"/> class
        /// with the specified feature switch name.
        /// </summary>
        /// <param name="switchName">
        /// The name of the feature switch that provides the value for the specified property.
        /// </param>
        public FeatureSwitchDefinitionAttribute(string switchName)
        {
            SwitchName = switchName;
        }

        /// <summary>
        /// The name of the feature switch that provides the value for the specified property.
        /// </summary>
        public string SwitchName { get; }
    }
    /// <summary>
    /// Types and Methods attributed with StackTraceHidden will be omitted from the stack trace text shown in StackTrace.ToString()
    /// and Exception.StackTrace
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Struct, Inherited = false)]
    public sealed class StackTraceHiddenAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StackTraceHiddenAttribute"/> class.
        /// </summary>
        public StackTraceHiddenAttribute() { }
    }
    /// <summary>
    /// Exception thrown when the program executes an instruction that was thought to be unreachable.
    /// </summary>
    public sealed class UnreachableException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnreachableException"/> class with the default error message.
        /// </summary>
        public UnreachableException()
            : base(SR.Arg_UnreachableException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnreachableException"/>
        /// class with a specified error message.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        public UnreachableException(string? message)
            : base(message ?? SR.Arg_UnreachableException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UnreachableException"/>
        /// class with a specified error message and a reference to the inner exception that is the cause of
        /// this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public UnreachableException(string? message, Exception? innerException)
            : base(message ?? SR.Arg_UnreachableException, innerException)
        {
        }
    }
    // There is no debug configuration, so assertions are never checked.
    public static class Debug
    {
        public static void Assert(bool condition) { }
        public static void Assert(bool condition, string? message = null) { }
        public static void Assert(bool condition, string? message, string? detailMessage) { }
        public static void Fail(string? message) { }
        public static void Fail(string? message, string? detailMessage) { }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies the types of members that are dynamically accessed.
    ///
    /// This enumeration has a <see cref="FlagsAttribute"/> attribute that allows a
    /// bitwise combination of its member values.
    /// </summary>
    [Flags]
    internal enum DynamicallyAccessedMemberTypes
    {
        /// <summary>
        /// Specifies no members.
        /// </summary>
        None = 0,

        /// <summary>
        /// Specifies the default, parameterless public constructor.
        /// </summary>
        PublicParameterlessConstructor = 0x0001,

        /// <summary>
        /// Specifies all public constructors.
        /// </summary>
        PublicConstructors = 0x0002 | PublicParameterlessConstructor,

        /// <summary>
        /// Specifies all non-public constructors.
        /// </summary>
        NonPublicConstructors = 0x0004,

        /// <summary>
        /// Specifies all public methods.
        /// </summary>
        PublicMethods = 0x0008,

        /// <summary>
        /// Specifies all non-public methods.
        /// </summary>
        NonPublicMethods = 0x0010,

        /// <summary>
        /// Specifies all public fields.
        /// </summary>
        PublicFields = 0x0020,

        /// <summary>
        /// Specifies all non-public fields.
        /// </summary>
        NonPublicFields = 0x0040,

        /// <summary>
        /// Specifies all public nested types.
        /// </summary>
        PublicNestedTypes = 0x0080,

        /// <summary>
        /// Specifies all non-public nested types.
        /// </summary>
        NonPublicNestedTypes = 0x0100,

        /// <summary>
        /// Specifies all public properties.
        /// </summary>
        PublicProperties = 0x0200,

        /// <summary>
        /// Specifies all non-public properties.
        /// </summary>
        NonPublicProperties = 0x0400,

        /// <summary>
        /// Specifies all public events.
        /// </summary>
        PublicEvents = 0x0800,

        /// <summary>
        /// Specifies all non-public events.
        /// </summary>
        NonPublicEvents = 0x1000,

        /// <summary>
        /// Specifies all interfaces implemented by the type.
        /// </summary>
        Interfaces = 0x2000,

        /// <summary>
        /// Specifies all non-public constructors, including those inherited from base classes.
        /// </summary>
        NonPublicConstructorsWithInherited = NonPublicConstructors | 0x4000,

        /// <summary>
        /// Specifies all non-public methods, including those inherited from base classes.
        /// </summary>
        NonPublicMethodsWithInherited = NonPublicMethods | 0x8000,

        /// <summary>
        /// Specifies all non-public fields, including those inherited from base classes.
        /// </summary>
        NonPublicFieldsWithInherited = NonPublicFields | 0x10000,

        /// <summary>
        /// Specifies all non-public nested types, including those inherited from base classes.
        /// </summary>
        NonPublicNestedTypesWithInherited = NonPublicNestedTypes | 0x20000,

        /// <summary>
        /// Specifies all non-public properties, including those inherited from base classes.
        /// </summary>
        NonPublicPropertiesWithInherited = NonPublicProperties | 0x40000,

        /// <summary>
        /// Specifies all non-public events, including those inherited from base classes.
        /// </summary>
        NonPublicEventsWithInherited = NonPublicEvents | 0x80000,

        /// <summary>
        /// Specifies all public constructors, including those inherited from base classes.
        /// </summary>
        PublicConstructorsWithInherited = PublicConstructors | 0x100000,

        /// <summary>
        /// Specifies all public nested types, including those inherited from base classes.
        /// </summary>
        PublicNestedTypesWithInherited = PublicNestedTypes | 0x200000,

        /// <summary>
        /// Specifies all constructors, including those inherited from base classes.
        /// </summary>
        AllConstructors = PublicConstructorsWithInherited | NonPublicConstructorsWithInherited,

        /// <summary>
        /// Specifies all methods, including those inherited from base classes.
        /// </summary>
        AllMethods = PublicMethods | NonPublicMethodsWithInherited,

        /// <summary>
        /// Specifies all fields, including those inherited from base classes.
        /// </summary>
        AllFields = PublicFields | NonPublicFieldsWithInherited,

        /// <summary>
        /// Specifies all nested types, including those inherited from base classes.
        /// </summary>
        AllNestedTypes = PublicNestedTypesWithInherited | NonPublicNestedTypesWithInherited,

        /// <summary>
        /// Specifies all properties, including those inherited from base classes.
        /// </summary>
        AllProperties = PublicProperties | NonPublicPropertiesWithInherited,

        /// <summary>
        /// Specifies all events, including those inherited from base classes.
        /// </summary>
        AllEvents = PublicEvents | NonPublicEventsWithInherited,

        /// <summary>
        /// Specifies all members.
        /// </summary>
        All = ~None
    }
    /// <summary>
    /// Indicates that certain members on a specified <see cref="Type"/> are accessed dynamically,
    /// for example through <see cref="Reflection"/>.
    /// </summary>
    /// <remarks>
    /// This allows tools to understand which members are being accessed during the execution
    /// of a program.
    ///
    /// This attribute is valid on members whose type is <see cref="Type"/> or <see cref="string"/>.
    ///
    /// When this attribute is applied to a location of type <see cref="string"/>, the assumption is
    /// that the string represents a fully qualified type name.
    ///
    /// When this attribute is applied to a class, interface, or struct, the members specified
    /// can be accessed dynamically on <see cref="Type"/> instances returned from calling
    /// <see cref="object.GetType"/> on instances of that class, interface, or struct.
    ///
    /// If the attribute is applied to a method it's treated as a special case and it implies
    /// the attribute should be applied to the "this" parameter of the method. As such the attribute
    /// should only be used on instance methods of types assignable to System.Type (or string, but no methods
    /// will use it there).
    /// </remarks>
    [CompilerLoweringPreserve]
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter |
        AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Method |
        AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct,
        Inherited = false)]
    internal sealed class DynamicallyAccessedMembersAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DynamicallyAccessedMembersAttribute"/> class
        /// with the specified member types.
        /// </summary>
        /// <param name="memberTypes">The types of members dynamically accessed.</param>
        public DynamicallyAccessedMembersAttribute(DynamicallyAccessedMemberTypes memberTypes)
        {
            MemberTypes = memberTypes;
        }

        /// <summary>
        /// Gets the <see cref="DynamicallyAccessedMemberTypes"/> which specifies the type
        /// of members dynamically accessed.
        /// </summary>
        public DynamicallyAccessedMemberTypes MemberTypes { get; }
    }

    /// <summary>
    /// Indicates that the specified method parameter expects a constant.
    /// </summary>
    /// <remarks>
    /// This can be used to inform tooling that a constant should be used as an argument for the annotated parameter.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class ConstantExpectedAttribute : Attribute
    {
        /// <summary>
        /// Indicates the minimum bound of the expected constant, inclusive.
        /// </summary>
        public object? Min { get; set; }
        /// <summary>
        /// Indicates the maximum bound of the expected constant, inclusive.
        /// </summary>
        public object? Max { get; set; }
    }
    /// <summary>Specifies that null is allowed as an input even if the corresponding type disallows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
    public sealed class AllowNullAttribute : Attribute { }
    /// <summary>Specifies that null is disallowed as an input even if the corresponding type allows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, Inherited = false)]
    public sealed class DisallowNullAttribute : Attribute { }
    /// <summary>Specifies that an output may be null even if the corresponding type disallows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class MaybeNullAttribute : Attribute { }
    /// <summary>Specifies that an output will not be null even if the corresponding type allows it. Specifies that an input argument was not null when the call returns.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    public sealed class NotNullAttribute : Attribute { }
    /// <summary>Specifies that when a method returns <see cref="ReturnValue"/>, the parameter may be null even if the corresponding type disallows it.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class MaybeNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, the associated parameter may be null.
        /// </param>
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        public bool ReturnValue { get; }
    }
    /// <summary>Specifies that when a method returns <see cref="ReturnValue"/>, the parameter will not be null even if the corresponding type allows it.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition.</summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, the associated parameter will not be null.
        /// </param>
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        public bool ReturnValue { get; }
    }
    /// <summary>Specifies that the output will be non-null if the named parameter is non-null.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
    public sealed class NotNullIfNotNullAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the associated parameter name.</summary>
        /// <param name="parameterName">
        /// The associated parameter name.  The output will be non-null if the argument to the parameter specified is non-null.
        /// </param>
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

        /// <summary>Gets the associated parameter name.</summary>
        public string ParameterName { get; }
    }
    /// <summary>Applied to a method that will never return under any circumstance.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class DoesNotReturnAttribute : Attribute { }
    /// <summary>Specifies that the method will not return if the associated Boolean parameter is passed the specified value.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class DoesNotReturnIfAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified parameter value.</summary>
        /// <param name="parameterValue">
        /// The condition parameter value. Code after the method will be considered unreachable by diagnostics if the argument to
        /// the associated parameter matches this value.
        /// </param>
        public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;

        /// <summary>Gets the condition parameter value.</summary>
        public bool ParameterValue { get; }
    }
    /// <summary>Specifies that the method or property will ensure that the listed field and property members have not-null values.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class MemberNotNullAttribute : Attribute
    {
        /// <summary>Initializes the attribute with a field or property member.</summary>
        /// <param name="member">
        /// The field or property member that is promised to be not-null.
        /// </param>
        public MemberNotNullAttribute(string member) => Members = [member];

        /// <summary>Initializes the attribute with the list of field and property members.</summary>
        /// <param name="members">
        /// The list of field and property members that are promised to be not-null.
        /// </param>
        public MemberNotNullAttribute(params string[] members) => Members = members;

        /// <summary>Gets field or property member names.</summary>
        public string[] Members { get; }
    }
    /// <summary>Specifies that the method or property will ensure that the listed field and property members have not-null values when returning with the specified return value condition.</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class MemberNotNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return value condition and a field or property member.</summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, the associated field or property member will not be null.
        /// </param>
        /// <param name="member">
        /// The field or property member that is promised to be not-null.
        /// </param>
        public MemberNotNullWhenAttribute(bool returnValue, string member)
        {
            ReturnValue = returnValue;
            Members = [member];
        }

        /// <summary>Initializes the attribute with the specified return value condition and list of field and property members.</summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, the associated field and property members will not be null.
        /// </param>
        /// <param name="members">
        /// The list of field and property members that are promised to be not-null.
        /// </param>
        public MemberNotNullWhenAttribute(bool returnValue, params string[] members)
        {
            ReturnValue = returnValue;
            Members = members;
        }

        /// <summary>Gets the return value condition.</summary>
        public bool ReturnValue { get; }

        /// <summary>Gets field or property member names.</summary>
        public string[] Members { get; }
    }
    /// <summary>Specifies the syntax used in a string.</summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class StringSyntaxAttribute : Attribute
    {
        /// <summary>Initializes the <see cref="StringSyntaxAttribute"/> with the identifier of the syntax used.</summary>
        /// <param name="syntax">The syntax identifier.</param>
        public StringSyntaxAttribute(string syntax)
        {
            Syntax = syntax;
            Arguments = Array.Empty<object?>();
        }

        /// <summary>Initializes the <see cref="StringSyntaxAttribute"/> with the identifier of the syntax used.</summary>
        /// <param name="syntax">The syntax identifier.</param>
        /// <param name="arguments">Optional arguments associated with the specific syntax employed.</param>
        public StringSyntaxAttribute(string syntax, params object?[] arguments)
        {
            Syntax = syntax;
            Arguments = arguments;
        }

        /// <summary>Gets the identifier of the syntax used.</summary>
        public string Syntax { get; }

        /// <summary>Optional arguments associated with the specific syntax employed.</summary>
        public object?[] Arguments { get; }

        /// <summary>The syntax identifier for strings containing composite formats for string formatting.</summary>
        public const string CompositeFormat = nameof(CompositeFormat);

        /// <summary>The syntax identifier for strings containing date format specifiers.</summary>
        public const string DateOnlyFormat = nameof(DateOnlyFormat);

        /// <summary>The syntax identifier for strings containing date and time format specifiers.</summary>
        public const string DateTimeFormat = nameof(DateTimeFormat);

        /// <summary>The syntax identifier for strings containing <see cref="Enum"/> format specifiers.</summary>
        public const string EnumFormat = nameof(EnumFormat);

        /// <summary>The syntax identifier for strings containing <see cref="Guid"/> format specifiers.</summary>
        public const string GuidFormat = nameof(GuidFormat);

        /// <summary>The syntax identifier for strings containing JavaScript Object Notation (JSON).</summary>
        public const string Json = nameof(Json);

        /// <summary>The syntax identifier for strings containing numeric format specifiers.</summary>
        public const string NumericFormat = nameof(NumericFormat);

        /// <summary>The syntax identifier for strings containing regular expressions.</summary>
        public const string Regex = nameof(Regex);

        /// <summary>The syntax identifier for strings containing time format specifiers.</summary>
        public const string TimeOnlyFormat = nameof(TimeOnlyFormat);

        /// <summary>The syntax identifier for strings containing <see cref="TimeSpan"/> format specifiers.</summary>
        public const string TimeSpanFormat = nameof(TimeSpanFormat);

        /// <summary>The syntax identifier for strings containing URIs.</summary>
        public const string Uri = nameof(Uri);

        /// <summary>The syntax identifier for strings containing XML.</summary>
        public const string Xml = nameof(Xml);
    }
}

namespace System.Diagnostics.Tracing
{
    /// <summary>
    /// Allows customizing defaults and specifying localization support for the event source class to which it is applied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EventSourceAttribute : Attribute
    {
        /// <summary>
        /// Overrides the ETW name of the event source (which defaults to the class name)
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Overrides the default (calculated) Guid of an EventSource type. Explicitly defining a GUID is discouraged,
        /// except when upgrading existing ETW providers to using event sources.
        /// </summary>
        public string? Guid { get; set; }

        /// <summary>
        /// <para>
        /// EventSources support localization of events. The names used for events, opcodes, tasks, keywords and maps
        /// can be localized to several languages if desired. This works by creating a ResX style string table
        /// (by simply adding a 'Resource File' to your project). This resource file is given a name e.g.
        /// 'DefaultNameSpace.ResourceFileName' which can be passed to the ResourceManager constructor to read the
        /// resources. This name is the value of the LocalizationResources property.
        /// </para><para>
        /// If LocalizationResources property is non-null, then EventSource will look up the localized strings for events by
        /// using the following resource naming scheme
        /// </para>
        ///     <para>* event_EVENTNAME</para>
        ///     <para>* task_TASKNAME</para>
        ///     <para>* keyword_KEYWORDNAME</para>
        ///     <para>* map_MAPNAME</para>
        /// <para>
        /// where the capitalized name is the name of the event, task, keyword, or map value that should be localized.
        /// Note that the localized string for an event corresponds to the Message string, and can have {0} values
        /// which represent the payload values.
        /// </para>
        /// </summary>
        public string? LocalizationResources { get; set; }
    }

    /// <summary>
    /// Any instance methods in a class that subclasses <see cref="EventSource"/> and that return void are
    /// assumed by default to be methods that generate an ETW event. Enough information can be deduced from the
    /// name of the method and its signature to generate basic schema information for the event. The
    /// <see cref="EventAttribute"/> class allows you to specify additional event schema information for an event if
    /// desired.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class EventAttribute : Attribute
    {
        /// <summary>Construct an EventAttribute with specified eventId</summary>
        /// <param name="eventId">ID of the ETW event (an integer between 1 and 65535)</param>
        public EventAttribute(int eventId)
        {
            EventId = eventId;
            Level = EventLevel.Informational;
        }

        /// <summary>Event's ID</summary>
        public int EventId { get; }
        /// <summary>Event's severity level: indicates the severity or verbosity of the event</summary>
        public EventLevel Level { get; set; }
        /// <summary>Event's keywords: allows classification of events by "categories"</summary>
        public EventKeywords Keywords { get; set; }
        /// <summary>Event's operation code: allows defining operations, generally used with Tasks</summary>
        public EventOpcode Opcode
        {
            get => m_opcode;
            set
            {
                m_opcode = value;
                m_opcodeSet = true;
            }
        }

        internal bool IsOpcodeSet => m_opcodeSet;

        /// <summary>Event's task: allows logical grouping of events</summary>
        public EventTask Task { get; set; }

        /// <summary>Event's channel: defines an event log as an additional destination for the event</summary>
        public EventChannel Channel { get; set; }

        /// <summary>Event's version</summary>
        public byte Version { get; set; }

        /// <summary>
        /// This can be specified to enable formatting and localization of the event's payload. You can
        /// use standard .NET substitution operators (eg {1}) in the string and they will be replaced
        /// with the 'ToString()' of the corresponding part of the  event payload.
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// User defined options associated with the event.  These do not have meaning to the EventSource but
        /// are passed through to listeners which given them semantics.
        /// </summary>
        public EventTags Tags { get; set; }

        /// <summary>
        /// Allows fine control over the Activity IDs generated by start and stop events
        /// </summary>
        public EventActivityOptions ActivityOptions { get; set; }

        #region private
        private EventOpcode m_opcode;
        private bool m_opcodeSet;
        #endregion
    }

    /// <summary>
    /// By default all instance methods in a class that subclasses code:EventSource that and return
    /// void are assumed to be methods that generate an event. This default can be overridden by specifying
    /// the code:NonEventAttribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NonEventAttribute : Attribute
    {
        /// <summary>
        /// Constructs a default NonEventAttribute
        /// </summary>
        public NonEventAttribute() { }
    }

    /// <summary>
    /// EventChannelAttribute allows customizing channels supported by an EventSource. This attribute must be
    /// applied to an member of type EventChannel defined in a Channels class nested in the EventSource class:
    /// <code>
    ///     public static class Channels
    ///     {
    ///         [Channel(Enabled = true, EventChannelType = EventChannelType.Admin)]
    ///         public const EventChannel Admin = (EventChannel)16;
    ///
    ///         [Channel(Enabled = false, EventChannelType = EventChannelType.Operational)]
    ///         public const EventChannel Operational = (EventChannel)17;
    ///     }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class EventChannelAttribute : Attribute
    {
        /// <summary>
        /// Specified whether the channel is enabled by default
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Legal values are in EventChannelType
        /// </summary>
        public EventChannelType EventChannelType { get; set; }

        // TODO: there is a convention that the name is the Provider/Type   Should we provide an override?
        // public string Name { get; set; }
    }

    /// <summary>
    /// Allowed channel types
    /// </summary>
    internal enum EventChannelType
    {
        /// <summary>The admin channel</summary>
        Admin = 1,
        /// <summary>The operational channel</summary>
        Operational,
        /// <summary>The Analytic channel</summary>
        Analytic,
        /// <summary>The debug channel</summary>
        Debug,
    }

    /// <summary>
    /// Describes the pre-defined command (EventCommandEventArgs.Command property) that is passed to the OnEventCommand callback.
    /// </summary>
    public enum EventCommand
    {
        /// <summary>
        /// Update EventSource state
        /// </summary>
        Update = 0,
        /// <summary>
        /// Request EventSource to generate and send its manifest
        /// </summary>
        SendManifest = -1,
        /// <summary>
        /// Enable event
        /// </summary>
        Enable = -2,
        /// <summary>
        /// Disable event
        /// </summary>
        Disable = -3
    }

    #region private classes

    // holds a bitfield representing a session mask
    /// <summary>
    /// A SessionMask represents a set of (at most MAX) sessions as a bit mask. The perEventSourceSessionId
    /// is the index in the SessionMask of the bit that will be set. These can translate to
    /// EventSource's reserved keywords bits using the provided ToEventKeywords() and
    /// FromEventKeywords() methods.
    /// </summary>
    internal struct SessionMask
    {
        public SessionMask(SessionMask m)
        { m_mask = m.m_mask; }

        public SessionMask(uint mask = 0)
        { m_mask = mask & MASK; }

        public bool IsEqualOrSupersetOf(SessionMask m)
        {
            return (this.m_mask | m.m_mask) == this.m_mask;
        }

        public static SessionMask All => new SessionMask(MASK);

        public static SessionMask FromId(int perEventSourceSessionId)
        {
            return new SessionMask((uint)1 << perEventSourceSessionId);
        }

        public ulong ToEventKeywords()
        {
            return (ulong)m_mask << SHIFT_SESSION_TO_KEYWORD;
        }

        public static SessionMask FromEventKeywords(ulong m)
        {
            return new SessionMask((uint)(m >> SHIFT_SESSION_TO_KEYWORD));
        }

        public bool this[int perEventSourceSessionId]
        {
            get
            {
                return (m_mask & (1 << perEventSourceSessionId)) != 0;
            }
            set
            {
                if (value) m_mask |= ((uint)1 << perEventSourceSessionId);
                else m_mask &= ~((uint)1 << perEventSourceSessionId);
            }
        }

        public static SessionMask operator |(SessionMask m1, SessionMask m2) =>
            new SessionMask(m1.m_mask | m2.m_mask);

        public static SessionMask operator &(SessionMask m1, SessionMask m2) =>
            new SessionMask(m1.m_mask & m2.m_mask);

        public static SessionMask operator ^(SessionMask m1, SessionMask m2) =>
            new SessionMask(m1.m_mask ^ m2.m_mask);

        public static SessionMask operator ~(SessionMask m) =>
            new SessionMask(MASK & ~(m.m_mask));

        public static explicit operator ulong(SessionMask m) => m.m_mask;

        public static explicit operator uint(SessionMask m) => m.m_mask;

        private uint m_mask;

        internal const int SHIFT_SESSION_TO_KEYWORD = 44;         // bits 44-47 inclusive are reserved
        internal const uint MASK = 0x0fU;                         // the mask of 4 reserved bits
        internal const uint MAX = 4;                              // maximum number of simultaneous ETW sessions supported
    }

    /// <summary>
    /// code:EventDispatchers are a simple 'helper' structure that holds the filtering state
    /// (m_EventEnabled) for a particular EventSource X EventListener tuple
    ///
    /// Thus a single EventListener may have many EventDispatchers (one for every EventSource
    /// that EventListener has activate) and a Single EventSource may also have many
    /// event Dispatchers (one for every EventListener that has activated it).
    ///
    /// Logically a particular EventDispatcher belongs to exactly one EventSource and exactly
    /// one EventListener (although EventDispatcher does not 'remember' the EventSource it is
    /// associated with.
    /// </summary>
    internal sealed class EventDispatcher
    {
        internal EventDispatcher(EventDispatcher? next, Dictionary<int, bool>? eventEnabled, EventListener listener)
        {
            m_Next = next;
            m_EventEnabled = eventEnabled;
            m_Listener = listener;
        }

        // Instance fields
        internal readonly EventListener m_Listener;   // The dispatcher this entry is for
        internal Dictionary<int, bool>? m_EventEnabled;              // For every event in a the eventSource, is it enabled?

        // Only guaranteed to exist after a EnsureInit()
        internal EventDispatcher? m_Next;              // These form a linked list in code:EventSource.m_Dispatchers
        // Of all listeners for that eventSource.
    }

    /// <summary>
    /// Flags that can be used with EventSource.GenerateManifest to control how the ETW manifest for the EventSource is
    /// generated.
    /// </summary>
    [Flags]
    public enum EventManifestOptions
    {
        /// <summary>
        /// Only the resources associated with current UI culture are included in the  manifest
        /// </summary>
        None = 0x0,
        /// <summary>
        /// Throw exceptions for any inconsistency encountered
        /// </summary>
        Strict = 0x1,
        /// <summary>
        /// Generate a "resources" node under "localization" for every satellite assembly provided
        /// </summary>
        AllCultures = 0x2,
        /// <summary>
        /// Generate the manifest only if the event source needs to be registered on the machine,
        /// otherwise return null (but still perform validation if Strict is specified)
        /// </summary>
        OnlyIfNeededForRegistration = 0x4,
        /// <summary>
        /// When generating the manifest do *not* enforce the rule that the current EventSource class
        /// must be the base class for the user-defined type passed in. This allows validation of .net
        /// event sources using the new validation code
        /// </summary>
        AllowEventSourceOverride = 0x8,
    }

    /// <summary>
    /// Used to send the m_rawManifest into the event dispatcher as a series of events.
    /// </summary>
    internal struct ManifestEnvelope
    {
        public const int MaxChunkSize = 0xF700;
        public enum ManifestFormats : byte
        {
            SimpleXmlFormat = 1,          // simply dump the XML manifest as UTF8
        }

        public ManifestFormats Format;
        public byte MajorVersion;
        public byte MinorVersion;
        public byte Magic;
        public ushort TotalChunks;
        public ushort ChunkNumber;
    }

    #endregion

    /// <summary>
    /// This class is meant to be inherited by a user-defined event source in order to define a managed
    /// ETW provider.   Please See DESIGN NOTES above for the internal architecture.
    /// The minimal definition of an EventSource simply specifies a number of ETW event methods that
    /// call one of the EventSource.WriteEvent overloads, <see cref="WriteEventCore"/>,
    /// or <see cref="WriteEventWithRelatedActivityIdCore"/> to log them. This functionality
    /// is sufficient for many users.
    /// <para>
    /// To achieve more control over the ETW provider manifest exposed by the event source type, the
    /// [<see cref="EventAttribute"/>] attributes can be specified for the ETW event methods.
    /// </para><para>
    /// For very advanced EventSources, it is possible to intercept the commands being given to the
    /// eventSource and change what filtering is done (see EventListener.EnableEvents and
    /// <see cref="EventListener.DisableEvents"/>) or cause actions to be performed by the eventSource,
    /// e.g. dumping a data structure (see EventSource.SendCommand and
    /// <see cref="OnEventCommand"/>).
    /// </para><para>
    /// The eventSources can be turned on with Windows ETW controllers (e.g. logman), immediately.
    /// It is also possible to control and intercept the data dispatcher programmatically.  See
    /// <see cref="EventListener"/> for more.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This is a minimal definition for a custom event source:
    /// <code>
    /// [EventSource(Name="Samples.Demos.Minimal")]
    /// sealed class MinimalEventSource : EventSource
    /// {
    ///     public static readonly MinimalEventSource Log = new MinimalEventSource();
    ///     public void Load(long ImageBase, string Name) { WriteEvent(1, ImageBase, Name); }
    ///     public void Unload(long ImageBase) { WriteEvent(2, ImageBase); }
    ///     private MinimalEventSource() {}
    /// }
    /// </code>
    /// </remarks>
    // The EnsureDescriptorsInitialized() method might need to access EventSource and its derived type
    // members and the trimmer ensures that these members are preserved.
    [DynamicallyAccessedMembers(ManifestMemberTypes)]
    public class EventSource : IDisposable
    {
        // private instance state
        private string m_name = null!;                  // My friendly name (privided in ctor)
        internal int m_id;                              // A small integer that is unique to this instance.
        private Guid m_guid;                            // GUID representing the ETW eventSource to the OS.
        internal volatile Dictionary<int, EventMetadata>? m_eventData; // None per-event data
        private volatile byte[]? m_rawManifest;          // Bytes to send out representing the event schema

        internal const DynamicallyAccessedMemberTypes ManifestMemberTypes = DynamicallyAccessedMemberTypes.All;

        internal static bool IsSupported => false;

        public string Name => m_name;

        public Guid Guid => m_guid;

        protected EventSource()
        {
        }

        internal EventSource(Guid eventSourceGuid, string eventSourceName)
        {
            m_guid = eventSourceGuid;
            m_name = eventSourceName;
        }

        internal EventSource(string eventSourceName, Guid eventSourceGuid, params object?[] options)
            : this(eventSourceGuid, eventSourceName)
        {
        }

        public bool IsEnabled() => false;

        public bool IsEnabled(EventLevel level, EventKeywords keywords) => false;

        public static void SetCurrentThreadActivityId(Guid activityId)
        {
        }

        public static void SetCurrentThreadActivityId(Guid activityId, out Guid oldActivityThatWillContinue)
        {
            oldActivityThatWillContinue = default;
        }

        internal static nint ObjectIDForEvents(object? o) => 0;

        protected virtual void OnEventCommand(EventCommandEventArgs command)
        {
        }

        protected void WriteEvent(int eventId)
        {
        }

        protected void WriteEvent(int eventId, params object?[] args)
        {
        }

        protected void WriteEvent(int eventId, int arg1)
        {
        }

        protected void WriteEvent(int eventId, int arg1, int arg2)
        {
        }

        protected void WriteEvent(int eventId, int arg1, int arg2, int arg3)
        {
        }

        protected void WriteEvent(int eventId, long arg1)
        {
        }

        protected void WriteEvent(int eventId, string? arg1)
        {
        }

        protected void WriteEvent(int eventId, string? arg1, long arg2)
        {
        }

        protected void WriteEvent(int eventId, string? arg1, string? arg2)
        {
        }

        protected void WriteEvent(int eventId, int arg1, long arg2)
        {
        }

        protected void WriteEvent(int eventId, int arg1, int arg2, long arg3)
        {
        }

        protected unsafe void WriteEventCore(int eventId, int eventDataCount, EventData* data)
        {
        }

        protected unsafe void WriteEventWithRelatedActivityIdCore(int eventId, Guid* relatedActivityId, int eventDataCount, EventData* data)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        protected internal struct EventData
        {
            internal ulong m_Ptr;
            internal int m_Size;
            internal int m_Reserved;

            public unsafe nint DataPointer
            {
                get => (nint)(void*)m_Ptr;
                set => m_Ptr = (ulong)(void*)value;
            }

            public int Size
            {
                get => m_Size;
                set => m_Size = value;
            }

            internal int Reserved
            {
                get => m_Reserved;
                set => m_Reserved = value;
            }
        }
    }

    public class EventCommandEventArgs : EventArgs
    {
        public EventCommand Command { get; internal set; }

        public IDictionary<string, string?>? Arguments { get; internal set; }
    }

    public abstract class EventListener : IDisposable
    {
        public virtual void Dispose()
        {
        }
    }

    internal struct EventMetadata
    {
    }

    /// <summary>
    /// Tags are flags that are not interpreted by EventSource but are passed along
    /// to the EventListener. The EventListener determines the semantics of the flags.
    /// </summary>
    [Flags]
    public enum EventTags
    {
        /// <summary>
        /// No special traits are added to the event.
        /// </summary>
        None = 0,
    }

    /// <summary>
    /// EventActivityOptions flags allow to specify different activity related characteristics.
    /// </summary>
    [Flags]
    public enum EventActivityOptions
    {
        /// <summary>
        /// No special options are added to the event.
        /// </summary>
        None = 0,

        /// <summary>
        /// Disable Implicit Activity Tracking
        /// </summary>
        Disable = 0x2,

        /// <summary>
        /// Allow activity event to call itself (directly or indirectly)
        /// </summary>
        Recursive = 0x4,

        /// <summary>
        /// Allows event activity to live beyond its parent.
        /// </summary>
        Detachable = 0x8
    }

    /// <summary>
    /// Contains an event level that is defined in an event provider. The level signifies the severity of the event.
    /// Custom values must be in the range from 16 through 255.
    /// </summary>
    public enum EventLevel
    {
        /// <summary>
        /// Log always
        /// </summary>
        LogAlways = 0,
        /// <summary>
        /// Only critical errors
        /// </summary>
        Critical,
        /// <summary>
        /// All errors, including previous levels
        /// </summary>
        Error,
        /// <summary>
        /// All warnings, including previous levels
        /// </summary>
        Warning,
        /// <summary>
        /// All informational events, including previous levels
        /// </summary>
        Informational,
        /// <summary>
        /// All events, including previous levels
        /// </summary>
        Verbose
    }

    /// <summary>
    /// Contains an event task that is defined in an event provider. The task identifies a portion of an application or a component that publishes an event. A task is a 16-bit value with 16 top values reserved.
    /// Custom values must be in the range from 1 through 65534.
    /// </summary>
    public enum EventTask
    {
        /// <summary>
        /// Undefined task
        /// </summary>
        None = 0
    }

    /// <summary>
    /// Contains an event opcode that is defined in an event provider. An opcode defines a numeric value that identifies the activity or a point within an activity that the application was performing when it raised the event.
    /// Custom values must be in the range from 11 through 239.
    /// </summary>
    public enum EventOpcode
    {
        /// <summary>
        /// An informational event
        /// </summary>
        Info = 0,
        /// <summary>
        /// An activity start event
        /// </summary>
        Start,
        /// <summary>
        /// An activity end event
        /// </summary>
        Stop,
        /// <summary>
        /// A trace collection start event
        /// </summary>
        DataCollectionStart,
        /// <summary>
        /// A trace collection end event
        /// </summary>
        DataCollectionStop,
        /// <summary>
        /// An extensional event
        /// </summary>
        Extension,
        /// <summary>
        /// A reply event
        /// </summary>
        Reply,
        /// <summary>
        /// An event representing the activity resuming from the suspension
        /// </summary>
        Resume,
        /// <summary>
        /// An event representing the activity is suspended, pending another activity's completion
        /// </summary>
        Suspend,
        /// <summary>
        /// An event representing the activity is transferred to another component, and can continue to work
        /// </summary>
        Send,
        /// <summary>
        /// An event representing receiving an activity transfer from another component
        /// </summary>
        Receive = 240
    }

    /// <summary>
    /// Specifies the event log channel for the event.
    /// </summary>
    public enum EventChannel : byte
    {
        /// <summary>
        /// No channel
        /// </summary>
        None = 0,
        // Channels 1 - 15 are reserved...
        /// <summary>The admin channel</summary>
        Admin = 16,
        /// <summary>The operational channel</summary>
        Operational = 17,
        /// <summary>The analytic channel</summary>
        Analytic = 18,
        /// <summary>The debug channel</summary>
        Debug = 19,
    }

    /// <summary>
    /// Defines the standard keywords that apply to events.
    /// </summary>
    [Flags]
    public enum EventKeywords : long
    {
        /// <summary>
        /// No events.
        /// </summary>
        None = 0x0,
        /// <summary>
        /// All Events
        /// </summary>
        All = ~0,
        /// <summary>
        /// Telemetry events
        /// </summary>
        MicrosoftTelemetry = 0x02000000000000,
        /// <summary>
        /// WDI context events
        /// </summary>
        WdiContext = 0x02000000000000,
        /// <summary>
        /// WDI diagnostic events
        /// </summary>
        WdiDiagnostic = 0x04000000000000,
        /// <summary>
        /// SQM events
        /// </summary>
        Sqm = 0x08000000000000,
        /// <summary>
        /// Failed security audits
        /// </summary>
        AuditFailure = 0x10000000000000,
        /// <summary>
        /// Successful security audits
        /// </summary>
        AuditSuccess = 0x20000000000000,
        /// <summary>
        /// Transfer events where the related Activity ID is a computed value and not a GUID
        /// N.B. The correct value for this field is 0x40000000000000.
        /// </summary>
        CorrelationHint = 0x10000000000000,
        /// <summary>
        /// Events raised using classic eventlog API
        /// </summary>
        EventLogClassic = 0x80000000000000
    }

    internal sealed class FrameworkEventSource : EventSource
    {
        public static readonly FrameworkEventSource Log = new FrameworkEventSource();

        public void ThreadTransferSendObj(object id, int kind, string info, bool multiDequeues, int intInfo1, int intInfo2)
        {
        }

        public void ThreadTransferReceiveObj(object id, int kind, string info)
        {
        }
    }
}
