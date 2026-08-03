using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cnidaria.Python
{
    public sealed class PythonModule
    {
        public PythonModule(string name, PythonCodeObject codeObject, bool isPackage = false)
        {
            Name = PythonModuleDefinition.ValidateName(name, nameof(name));
            CodeObject = codeObject ?? throw new ArgumentNullException(nameof(codeObject));
            if (codeObject.Version != PythonBytecodeVersion.CPython3_14_6)
            {
                throw new ArgumentException(
                    $"Module '{name}' uses unsupported bytecode version {codeObject.Version}.",
                    nameof(codeObject));
            }

            IsPackage = isPackage;
        }

        public string Name { get; }
        public PythonCodeObject CodeObject { get; }
        public bool IsPackage { get; }
    }

    public sealed class PythonModuleCatalog
    {
        private readonly Dictionary<string, PythonModuleDefinition> _modules;

        public static PythonModuleCatalog Empty { get; } =
            new(Array.Empty<PythonModuleDefinition>());

        public PythonModuleCatalog(IEnumerable<PythonModule> modules)
        {
            ArgumentNullException.ThrowIfNull(modules);
            _modules = new Dictionary<string, PythonModuleDefinition>(StringComparer.Ordinal);
            foreach (var module in modules)
            {
                ArgumentNullException.ThrowIfNull(module);
                AddDefinition(PythonModuleDefinition.FromPython(module));
            }
        }

        private PythonModuleCatalog(IEnumerable<PythonModuleDefinition> modules)
        {
            _modules = new Dictionary<string, PythonModuleDefinition>(StringComparer.Ordinal);
            foreach (var module in modules)
                AddDefinition(module);
        }

        public int Count => _modules.Count;

        public bool Contains(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return _modules.ContainsKey(name);
        }

        public PythonModuleCatalog WithModules(IEnumerable<PythonModule> modules)
        {
            ArgumentNullException.ThrowIfNull(modules);
            var definitions = new Dictionary<string, PythonModuleDefinition>(_modules, StringComparer.Ordinal);
            foreach (var module in modules)
            {
                ArgumentNullException.ThrowIfNull(module);
                definitions[module.Name] = PythonModuleDefinition.FromPython(module);
            }
            return new PythonModuleCatalog(definitions.Values);
        }

        internal bool TryGetModule(string name, out PythonModuleDefinition module)
        {
            return _modules.TryGetValue(name, out module!);
        }

        internal static PythonModuleCatalog CreateStandardLibrary(
            IEnumerable<PythonModuleDefinition> modules)
        {
            return new PythonModuleCatalog(modules);
        }

        private void AddDefinition(PythonModuleDefinition module)
        {
            if (!_modules.TryAdd(module.Name, module))
                throw new ArgumentException($"Duplicate Python module '{module.Name}'.");
        }
    }

    public static class PythonStandardLibrary
    {
        private const string ResourcePrefix = "Cnidaria.Py.stdlib.";

        public static PythonModuleCatalog Default { get; } = CreateDefault();

        private static PythonModuleCatalog CreateDefault()
        {
            return PythonModuleCatalog.CreateStandardLibrary(
            [
                PythonModuleDefinition.FromNative("builtins", PythonNativeModuleKind.Builtins),
                PythonModuleDefinition.FromNative("sys", PythonNativeModuleKind.Sys),
                PythonModuleDefinition.FromNative("math", PythonNativeModuleKind.Math),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("operator")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("itertools")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("keyword")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("bisect")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("functools")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("heapq")),
                PythonModuleDefinition.FromPython(CompileEmbeddedModule("statistics")),
            ]);
        }

        private static PythonModule CompileEmbeddedModule(string name, bool isPackage = false)
        {
            var resourceName = $"{ResourcePrefix}{name}.py";
            using var stream = typeof(PythonStandardLibrary).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded Python module '{resourceName}' was not found.");
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            var source = reader.ReadToEnd();
            var result = PythonEmitter.Emit(
                source,
                new EmitOptions
                {
                    BytecodeVersion = PythonBytecodeVersion.CPython3_14_6,
                    FileName = $"<stdlib>/{name}.py",
                    ModuleName = name,
                    OptimizationLevel = 1,
                    EmitNoMonitoringFlag = true,
                });
            if (!result.Success || result.CodeObject is null)
            {
                var message = new StringBuilder($"Failed to compile embedded Python module '{name}'.");
                foreach (var diagnostic in result.Diagnostics)
                    message.Append(' ').Append(diagnostic.Code).Append(": ").Append(diagnostic.Message);
                throw new InvalidOperationException(message.ToString());
            }

            return new PythonModule(name, result.CodeObject, isPackage);
        }
    }

    internal enum PythonNativeModuleKind : byte
    {
        None = 0,
        Builtins = 1,
        Sys = 2,
        Math = 3,
    }

    internal sealed class PythonModuleDefinition
    {
        private PythonModuleDefinition(
            string name,
            bool isPackage,
            PythonCodeObject? codeObject,
            PythonNativeModuleKind nativeKind)
        {
            Name = ValidateName(name, nameof(name));
            IsPackage = isPackage;
            CodeObject = codeObject;
            NativeKind = nativeKind;
        }

        public string Name { get; }
        public bool IsPackage { get; }
        public PythonCodeObject? CodeObject { get; }
        public PythonNativeModuleKind NativeKind { get; }
        public bool IsNative => NativeKind != PythonNativeModuleKind.None;

        public static PythonModuleDefinition FromPython(PythonModule module)
        {
            return new PythonModuleDefinition(
                module.Name,
                module.IsPackage,
                module.CodeObject,
                PythonNativeModuleKind.None);
        }

        public static PythonModuleDefinition FromNative(string name, PythonNativeModuleKind nativeKind, bool isPackage = false)
        {
            if (nativeKind == PythonNativeModuleKind.None)
                throw new ArgumentOutOfRangeException(nameof(nativeKind));
            return new PythonModuleDefinition(name, isPackage, null, nativeKind);
        }
        public static string ValidateName(string name, string parameterName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, parameterName);
            var segmentStart = 0;
            for (var index = 0; index <= name.Length; index++)
            {
                if (index != name.Length && name[index] != '.')
                    continue;
                if (index == segmentStart)
                    throw new ArgumentException("Python module names cannot contain empty segments.", parameterName);
                if (name[segmentStart] != '_' && !char.IsLetter(name[segmentStart]))
                    throw new ArgumentException($"Invalid Python module name '{name}'.", parameterName);
                for (var part = segmentStart + 1; part < index; part++)
                {
                    if (name[part] != '_' && !char.IsLetterOrDigit(name[part]))
                        throw new ArgumentException($"Invalid Python module name '{name}'.", parameterName);
                }
                segmentStart = index + 1;
            }
            return name;
        }
    }

}
