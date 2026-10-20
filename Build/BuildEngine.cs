using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Cnidaria;

internal sealed class BuildEngine
{
    private readonly string _projectPath;
    private readonly IFileSystem _fileSystem;
    private readonly BuildOptions _options;
    private readonly BuildLimits _limits;
    private readonly Dictionary<string, string> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _global = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CompileItem> _items = new();
    private readonly Dictionary<string, BuildTarget> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeImports = new(StringComparer.Ordinal);
    private readonly HashSet<string> _projectFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _compiledModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeModules = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Dependency, string For)> _nativeReferences = new();
    private readonly List<BuildArtifact> _artifacts = new();
    private readonly List<string> _diagnostics = new();
    private int _importCount, _xmlNodes, _itemCount, _taskCount;
    private long _readBytes, _outputBytes;
    private bool _failed;
    private string? _defaultTarget;

    private sealed record CompileItem(string Path, string Module, string? Compiler, BuildOutputKind Kind);
    private enum TaskKind : byte { Compile, Copy }
    private sealed record TaskStep(TaskKind Kind, string? Module, string? Source, string? To);
    private sealed record BuildTarget(string[] Dependencies, List<TaskStep> Tasks);

    internal BuildEngine(string projectPath, IFileSystem fileSystem, BuildOptions options)
    {
        _projectPath = projectPath;
        _fileSystem = fileSystem;
        _options = options;
        _limits = options.Limits;
        var name = Path.GetFileNameWithoutExtension(projectPath);
        _properties.Add("ProjectName", name);
        _properties.Add("ProjectDir", BuildPath.Directory(projectPath));
        _properties.Add("Configuration", "Debug");
        _properties.Add("TargetArchitecture", "rbc32");
        _properties.Add("TargetOS", "None");
        _properties.Add("TargetFeatures", "None");
        _properties.Add("OutputType", "Executable");
        _properties.Add("OutputPath", "bin/$(Configuration)");
        foreach (var pair in options.Properties)
        {
            CheckPropertyName(pair.Key);
            if (pair.Value is null || pair.Value.Length > _limits.MaxExpansionCharacters)
                throw new BuildException("Global property exceeds expansion budget.");
            _properties[pair.Key] = pair.Value;
            _global.Add(pair.Key);
        }
        if (_properties.Count > _limits.MaxProperties) throw new BuildException("Property budget exceeded.");
    }

    internal BuildResult Run()
    {
        try
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            LoadProject(_projectPath, 1);
            if (!_targets.ContainsKey("Build"))
                _targets.Add("Build", new BuildTarget(Array.Empty<string>(), new List<TaskStep> { new(TaskKind.Compile, null, null, null) }));
            if (_targets.Count > _limits.MaxTargets)
                throw new BuildException("Target budget exceeded.");
            var target = _options.BuildTarget.Equals("Build", StringComparison.OrdinalIgnoreCase) &&
                _defaultTarget is not null ? _defaultTarget : _options.BuildTarget;
            if ((target.Equals("Aot", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("Rebuild", StringComparison.OrdinalIgnoreCase)) && !_targets.ContainsKey(target))
                target = "Build";
            RunTarget(target, 0);
        }
        catch (OperationCanceledException) { throw; }
        catch (BuildException e) { _diagnostics.Add(e.Message); _failed = true; }
        catch (IOException e) { _diagnostics.Add($"Filesystem error: {e.Message}"); _failed = true; }
        catch (UnauthorizedAccessException e) { _diagnostics.Add($"Filesystem access denied: {e.Message}"); _failed = true; }
        catch (RegexMatchTimeoutException) { _diagnostics.Add("Glob matching time budget exceeded."); _failed = true; }
        return new BuildResult(_properties, _artifacts, _diagnostics, !_failed);
    }

    private void LoadProject(string path, int depth)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (depth > _limits.MaxImportDepth)
            throw new BuildException("Maximum import depth exceeded.");
        if (!_activeImports.Add(path))
            throw new BuildException($"Import cycle at '{path}'.");

        _projectFiles.Add(path);
        try
        {
            var bytes = Read(path, _limits.MaxProjectBytes);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _limits.MaxProjectBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            while (Next(reader) && reader.NodeType is XmlNodeType.XmlDeclaration or XmlNodeType.Whitespace)
            {
            }

            if (reader.NodeType != XmlNodeType.Element)
                throw new BuildException($"Missing Project root in '{path}'.");
            CheckElement(reader, "Project", "DefaultTargets");
            if (depth == 1 && Get(reader, "DefaultTargets") is { Length: > 0 } defaultTarget)
            {
                ValidateIdentifier(defaultTarget);
                _defaultTarget = defaultTarget;
            }

            var directory = BuildPath.Directory(path);
            ReadChildren(reader, child =>
            {
                switch (child.LocalName)
                {
                    case "PropertyGroup":
                        ReadProperties(child);
                        break;
                    case "ItemGroup":
                        ReadItems(child, directory);
                        break;
                    case "Import":
                        CheckElement(child, "Import", "Project", "Condition");
                        var enabled = Condition(child);
                        var import = enabled ? Required(child, "Project") : null;
                        ReadEmpty(child);
                        if (import is not null)
                        {
                            if (HasGlob(import) || import.Contains(';'))
                                throw new BuildException("Import must name one explicit file.");
                            if (++_importCount > _limits.MaxImports)
                                throw new BuildException("Import count budget exceeded.");
                            LoadProject(BuildPath.Normalize(import, directory), depth + 1);
                        }
                        break;
                    case "Target":
                        ReadTarget(child, directory);
                        break;
                    default:
                        throw new BuildException($"Unsupported project element '{child.LocalName}'.");
                }
            });

            while (Next(reader))
            {
                if (reader.NodeType != XmlNodeType.Whitespace)
                    throw new BuildException("Unexpected content after Project root.");
            }
        }
        catch (XmlException ex)
        {
            throw new BuildException($"Invalid XML in '{path}': {ex.Message}", ex);
        }
        finally
        {
            _activeImports.Remove(path);
        }
    }

    private bool Next(XmlReader reader)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (!reader.Read())
            return false;

        if (reader.Depth >= _limits.MaxXmlDepth)
            throw new BuildException("XML depth budget exceeded.");
        if (reader.NodeType == XmlNodeType.Element)
        {
            if (_xmlNodes > _limits.MaxXmlNodes - 1 - reader.AttributeCount)
                throw new BuildException("XML node/attribute budget exceeded.");
            _xmlNodes += 1 + reader.AttributeCount;
        }
        return true;
    }

    private void ReadChildren(XmlReader reader, Action<XmlReader> readChild)
    {
        if (reader.IsEmptyElement)
            return;

        var depth = reader.Depth;
        while (Next(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1)
            {
                readChild(reader);
                continue;
            }
            if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                continue;
            throw new BuildException($"Unexpected content inside '{reader.Name}'.");
        }
        throw new BuildException("Unexpected end of project XML.");
    }

    private void DiscardElement(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;

        var depth = reader.Depth;
        while (Next(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
        }
        throw new BuildException("Unexpected end of project XML.");
    }

    private string ReadScalar(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return string.Empty;

        var depth = reader.Depth;
        var value = new StringBuilder();
        while (Next(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return value.ToString();
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or
                XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                throw new BuildException("Nested property values are not supported.");
            if (reader.Value.Length > _limits.MaxExpansionCharacters - value.Length)
                throw new BuildException("Expansion budget exceeded.");
            value.Append(reader.Value);
        }
        throw new BuildException("Unexpected end of project XML.");
    }

    private void ReadEmpty(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return;

        var name = reader.LocalName;
        var depth = reader.Depth;
        while (Next(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                return;
            if (reader.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                continue;
            if ((reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) &&
                string.IsNullOrWhiteSpace(reader.Value))
                continue;
            throw new BuildException($"'{name}' cannot contain child nodes or text.");
        }
        throw new BuildException("Unexpected end of project XML.");
    }

    private void ReadProperties(XmlReader group)
    {
        CheckElement(group, "PropertyGroup", "Condition");
        if (!Condition(group))
        {
            DiscardElement(group);
            return;
        }

        ReadChildren(group, prop =>
        {
            CheckNamespace(prop);
            var name = prop.LocalName;
            CheckPropertyName(name);
            CheckAttributes(prop, "Condition");
            var enabled = Condition(prop);
            var text = ReadScalar(prop);
            if (!enabled || _global.Contains(name))
                return;

            var value = Expand(text);
            if (!_properties.ContainsKey(name) && _properties.Count >= _limits.MaxProperties)
                throw new BuildException("Property budget exceeded.");
            _properties[name] = value;
        });
    }

    private void ReadItems(XmlReader group, string baseDirectory)
    {
        CheckElement(group, "ItemGroup", "Condition");
        if (!Condition(group))
        {
            DiscardElement(group);
            return;
        }

        ReadChildren(group, element =>
        {
            if (element.LocalName == "NativeReference")
            {
                CheckElement(element, "NativeReference", "Include", "For", "Condition");
                if (Condition(element))
                {
                    var dependency = Required(element, "Include");
                    var forModule = Get(element, "For") ?? GetProperty("ProjectName");
                    ValidateModule(dependency);
                    ValidateModule(forModule);
                    if (++_itemCount > _limits.MaxItems)
                        throw new BuildException("Item budget exceeded.");
                    _nativeReferences.Add((dependency, forModule));
                }
                ReadEmpty(element);
                return;
            }

            CheckElement(element, "Compile", "Include", "Remove", "Exclude", "Compiler", "Module", "OutputType", "Condition");
            if (!Condition(element))
            {
                ReadEmpty(element);
                return;
            }

            var remove = Get(element, "Remove");
            var include = Get(element, "Include");
            if (string.IsNullOrWhiteSpace(remove) == string.IsNullOrWhiteSpace(include))
                throw new BuildException("Compile requires either Include or Remove.");
            if (remove is not null)
            {
                ReadEmpty(element);
                var patterns = Split(remove).Select(s => CreateGlob(BuildPath.Normalize(s, baseDirectory))).ToArray();
                _items.RemoveAll(item => patterns.Any(pattern => pattern.IsMatch(item.Path)));
                return;
            }

            var module = Get(element, "Module") ?? GetProperty("ProjectName");
            var compiler = Get(element, "Compiler");
            var output = Get(element, "OutputType") ?? GetProperty("OutputType");
            var exclude = Get(element, "Exclude") ?? string.Empty;
            ReadEmpty(element);
            ValidateModule(module);
            if (output.Equals("Exe", StringComparison.OrdinalIgnoreCase))
                output = "Executable";
            var kind = ParseEnum<BuildOutputKind>(output, "OutputType");
            var excludes = Split(exclude).Select(s => CreateGlob(BuildPath.Normalize(s, baseDirectory))).ToArray();
            foreach (var pattern in Split(include!))
            {
                var absolute = BuildPath.Normalize(pattern, baseDirectory);
                foreach (var matched in MatchFiles(absolute))
                {
                    _options.CancellationToken.ThrowIfCancellationRequested();
                    if (excludes.Any(x => x.IsMatch(matched)))
                        continue;
                    if (_items.Any(i => i.Path == matched && i.Module == module))
                        continue;
                    if (++_itemCount > _limits.MaxItems)
                        throw new BuildException("Compile item budget exceeded.");
                    _items.Add(new CompileItem(matched, module, compiler, kind));
                }
            }
        });
    }

    private void ReadTarget(XmlReader element, string baseDirectory)
    {
        CheckElement(element, "Target", "Name", "DependsOnTargets", "Condition");
        if (!Condition(element))
        {
            DiscardElement(element);
            return;
        }

        var name = Required(element, "Name");
        ValidateIdentifier(name);
        if (_targets.ContainsKey(name))
            throw new BuildException($"Duplicate target '{name}'.");
        if (_targets.Count >= _limits.MaxTargets)
            throw new BuildException("Target budget exceeded.");
        var deps = Split(Get(element, "DependsOnTargets") ?? string.Empty).ToArray();
        foreach (var dep in deps)
            ValidateIdentifier(dep);

        var tasks = new List<TaskStep>();
        ReadChildren(element, task =>
        {
            if (++_taskCount > _limits.MaxTasks)
                throw new BuildException("Task budget exceeded.");
            switch (task.LocalName)
            {
                case "Compile":
                    CheckElement(task, "Compile", "Module", "Condition");
                    if (Condition(task))
                    {
                        var module = Get(task, "Module");
                        if (module is not null)
                            ValidateModule(module);
                        tasks.Add(new TaskStep(TaskKind.Compile, module, null, null));
                    }
                    ReadEmpty(task);
                    break;
                case "Copy":
                    CheckElement(task, "Copy", "Source", "To", "Condition");
                    if (Condition(task))
                        tasks.Add(new TaskStep(TaskKind.Copy, null,
                            BuildPath.Normalize(Required(task, "Source"), baseDirectory),
                            BuildPath.Normalize(Required(task, "To"), baseDirectory)));
                    ReadEmpty(task);
                    break;
                default:
                    throw new BuildException($"Unsupported task '{task.LocalName}'.");
            }
        });
        _targets.Add(name, new BuildTarget(deps, tasks));
    }

    private void RunTarget(string name, int depth)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (depth >= _limits.MaxTargets) throw new BuildException("Target dependency depth exceeded.");
        if (_completedTargets.Contains(name)) return;
        if (!_targets.TryGetValue(name, out var target)) throw new BuildException($"Unknown target '{name}'.");
        if (!_activeTargets.Add(name)) throw new BuildException($"Target dependency cycle at '{name}'.");
        try
        {
            foreach (var dep in target.Dependencies) RunTarget(dep, depth + 1);
            foreach (var task in target.Tasks)
            {
                _options.CancellationToken.ThrowIfCancellationRequested();
                if (task.Kind == TaskKind.Compile) Compile(task.Module);
                else Copy(task.Source!, task.To!);
            }
            _completedTargets.Add(name);
        }
        finally { _activeTargets.Remove(name); }
    }

    private void Compile(string? selectedModule)
    {
        foreach (var module in _items.Select(x => x.Module).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (selectedModule is null || module.Equals(selectedModule, StringComparison.OrdinalIgnoreCase))
                CompileModule(module, 0);
        }
        if (selectedModule is not null && !_items.Any(x => x.Module.Equals(selectedModule, StringComparison.OrdinalIgnoreCase)))
            throw new BuildException($"Unknown compile module '{selectedModule}'.");
    }

    private void CompileModule(string module, int depth)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (depth >= _limits.MaxTargets) throw new BuildException("Native reference dependency depth exceeded.");
        if (_compiledModules.Contains(module)) return;
        if (!_activeModules.Add(module)) throw new BuildException($"Native reference cycle at '{module}'.");
        try
        {
            var items = _items.Where(x => x.Module.Equals(module, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (items.Length == 0) throw new BuildException($"Missing module '{module}'.");
            var dependencies = _nativeReferences.Where(x => x.For.Equals(module, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Dependency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var dependency in dependencies) CompileModule(dependency, depth + 1);
            var first = items[0];
            var language = BuildCompilation.Resolve(first.Path, first.Compiler);
            var backend = BuildCompilation.Backend(language);
            foreach (var item in items)
            {
                if (BuildCompilation.Resolve(item.Path, item.Compiler) != language || item.Kind != first.Kind)
                    throw new BuildException($"Module '{module}' mixes compilers or output types. Split it into modules.");
            }
            if (dependencies.Length > 0 && backend != BuildBackend.Managed)
                throw new BuildException("NativeReference can only be attached to a managed module.");
            var nativeLibraries = new List<string>();
            foreach (var dep in dependencies)
            {
                var artifact = _artifacts.SingleOrDefault(a => a.Module.Equals(dep, StringComparison.OrdinalIgnoreCase));
                if (artifact.Backend != BuildBackend.Unmanaged || artifact.Kind != BuildOutputKind.SharedLibrary)
                    throw new BuildException($"NativeReference '{dep}' must point to a built unmanaged shared library.");
                nativeLibraries.Add(artifact.Path.Substring(artifact.Path.LastIndexOf('/') + 1));
            }
            var sources = new List<BuildSource>(items.Length);
            foreach (var item in items) sources.Add(new BuildSource(item.Path, Read(item.Path, _limits.MaxFileBytes)));
            var arch = ParseArchitecture(GetProperty("TargetArchitecture"));
            var os = ParseEnum<OperatingSystemKind>(GetProperty("TargetOS"), "TargetOS");
            var features = ParseEnum<TargetArchitectureFeatures>(GetProperty("TargetFeatures"), "TargetFeatures");
            var config = GetProperty("Configuration");
            var request = new BuildCompileRequest(module, first.Kind, arch, os, features,
                config, sources, nativeLibraries, _properties,
                path => Read(path, _limits.MaxFileBytes), _options.CancellationToken);
            var output = BuildCompilation.Compile(language, request);
            if (output.Diagnostics.Count > _limits.MaxTasks) throw new BuildException("Compiler diagnostic count budget exceeded.");
            foreach (var diag in output.Diagnostics)
            {
                if (diag is null || diag.Length > _limits.MaxExpansionCharacters)
                    throw new BuildException("Compiler diagnostic length budget exceeded.");
                _diagnostics.Add($"[{module}] {diag}");
            }
            if (output.Bytes is null)
            {
                _failed = true;
                throw new BuildException($"Compilation of '{module}' failed.");
            }
            var filename = module + Extension(first.Kind, arch, os);
            if (first.Kind == BuildOutputKind.SharedLibrary && os == OperatingSystemKind.Linux)
                filename = "lib" + filename;
            var outputDirectory = BuildPath.Normalize(GetProperty("OutputPath"), GetProperty("ProjectDir"));
            var path = BuildPath.Normalize(filename, outputDirectory);
            WriteOutput(path, output.Bytes);
            _artifacts.Add(new BuildArtifact(module, backend, first.Kind, path, output.Bytes.LongLength));
            _compiledModules.Add(module);
        }
        finally { _activeModules.Remove(module); }
    }

    private static string Extension(BuildOutputKind kind, TargetArchitectureKind architecture, OperatingSystemKind os)
    {
        if (kind == BuildOutputKind.Library) return ".dll";
        if (kind == BuildOutputKind.SharedLibrary) return os switch
        {
            OperatingSystemKind.Linux => ".so",
            OperatingSystemKind.Windows => ".dll",
            OperatingSystemKind.MacOs => ".dylib",
            _ => ".so"
        };
        if (architecture is TargetArchitectureKind.RegisterBytecode or TargetArchitectureKind.RegisterBytecode64) return ".rbc";
        return os == OperatingSystemKind.Windows ? ".exe" : ".elf";
    }

    private void Copy(string from, string to) => WriteOutput(to, Read(from, _limits.MaxFileBytes));
    private byte[] Read(string path, int perFileMaximum)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        var allowance = (int)Math.Min(perFileMaximum, Math.Min(int.MaxValue, _limits.MaxBuildReadBytes - _readBytes));
        if (allowance < 0) throw new BuildException("Build read budget exceeded.");
        var content = _fileSystem.Read(path, allowance);
        if (content.LongLength > _limits.MaxBuildReadBytes - _readBytes) throw new BuildException("Build read budget exceeded.");
        _readBytes += content.LongLength;
        return content;
    }
    private void WriteOutput(string path, ReadOnlySpan<byte> bytes)
    {
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > _limits.MaxBuildOutputBytes - _outputBytes)
            throw new BuildException("Build output byte budget exceeded.");
        if (_projectFiles.Contains(path) || _items.Any(x => x.Path == path))
            throw new BuildException($"Output '{path}' would overwrite a project or a source file.");
        _fileSystem.Write(path, bytes);
        _outputBytes += bytes.Length;
    }

    private IEnumerable<string> MatchFiles(string pattern)
    {
        if (!HasGlob(pattern)) { yield return pattern; yield break; }
        int wildcard = pattern.IndexOfAny(new[] { '*', '?' });
        var slash = pattern.LastIndexOf('/', wildcard);
        var root = slash <= 0 ? "/" : pattern.Substring(0, slash);
        var matcher = CreateGlob(pattern);
        var files = new List<string>();
        foreach (var path in _fileSystem.EnumerateFiles(root, _limits.MaxItems))
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            var normalized = BuildPath.Normalize(path);
            if (matcher.IsMatch(normalized)) files.Add(normalized);
            if (files.Count > _limits.MaxItems) throw new BuildException("Glob match budget exceeded.");
        }
        files.Sort(StringComparer.Ordinal);
        foreach (var file in files) yield return file;
    }
    private static bool HasGlob(string p) => p.IndexOfAny(new[] { '*', '?' }) >= 0;
    private static Regex CreateGlob(string pattern)
    {
        var b = new StringBuilder("\\A");
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/') { b.Append("(?:.*/)?"); i++; }
                else b.Append(".*");
            }
            else if (pattern[i] == '*') b.Append("[^/]*");
            else if (pattern[i] == '?') b.Append("[^/]");
            else b.Append(Regex.Escape(pattern[i].ToString()));
        }
        b.Append("\\z");
        return new Regex(b.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(200));
    }

    private string GetProperty(string name) => Expand(_properties.TryGetValue(name, out var value) ? value : string.Empty);
    private string Expand(string value) => Expand(value, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private string Expand(string value, HashSet<string> expanding)
    {
        if (value.Length > _limits.MaxExpansionCharacters)
            throw new BuildException("Expansion budget exceeded.");
        if (!value.Contains("$(", StringComparison.Ordinal))
            return value;

        var b = new StringBuilder(Math.Min(value.Length, 256));
        for (int i = 0; i < value.Length;)
        {
            if (value[i] == '$' && i + 1 < value.Length && value[i + 1] == '(')
            {
                int end = value.IndexOf(')', i + 2);
                if (end < 0) throw new BuildException("Unterminated property reference.");
                var property = value.Substring(i + 2, end - i - 2);
                CheckPropertyName(property);
                if (_properties.TryGetValue(property, out var replacement))
                {
                    if (!expanding.Add(property))
                        throw new BuildException($"Property expansion cycle at '{property}'.");
                    b.Append(Expand(replacement, expanding));
                    expanding.Remove(property);
                }
                i = end + 1;
            }
            else b.Append(value[i++]);
            if (b.Length > _limits.MaxExpansionCharacters) throw new BuildException("Expansion budget exceeded.");
        }
        return b.ToString();
    }
    private bool Condition(XmlReader element)
    {
        var cond = Get(element, "Condition");
        if (string.IsNullOrWhiteSpace(cond)) return true;
        var at = cond.IndexOf("==", StringComparison.Ordinal);
        bool equal = true;
        if (at < 0) { at = cond.IndexOf("!=", StringComparison.Ordinal); equal = false; }
        if (at < 0) throw new BuildException("Condition only supports quoted == and != comparisons.");
        var left = Unquote(cond.Substring(0, at).Trim());
        var right = Unquote(cond.Substring(at + 2).Trim());
        var result = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        return equal ? result : !result;
    }
    private static string Unquote(string value)
    {
        if (value.Length < 2 || (value[0] != '\'' && value[0] != '"') || value[^1] != value[0])
            throw new BuildException("Condition operands must be quoted strings.");
        return value.Substring(1, value.Length - 2);
    }
    private static IEnumerable<string> Split(string text) => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private string? Get(XmlReader reader, string attribute)
    {
        var value = reader.GetAttribute(attribute);
        return value is null ? null : Expand(value);
    }

    private string Required(XmlReader reader, string name) => Get(reader, name) is { Length: > 0 } value ? value :
        throw new BuildException($"Missing '{name}' on '{reader.LocalName}'.");

    private static void CheckNamespace(XmlReader reader)
    {
        if (reader.NamespaceURI is not "")
            throw new BuildException($"Unrecognized namespace '{reader.NamespaceURI}'.");
    }

    private static void CheckAttributes(XmlReader reader, params string[] allowed)
    {
        var elementName = reader.LocalName;
        if (!reader.MoveToFirstAttribute())
            return;
        do
        {
            if (reader.Prefix == "xmlns" || reader.Name == "xmlns")
                continue;
            if (reader.NamespaceURI.Length != 0 || !allowed.Contains(reader.LocalName, StringComparer.Ordinal))
                throw new BuildException($"Unknown attribute '{reader.Name}' on '{elementName}'.");
        }
        while (reader.MoveToNextAttribute());
        reader.MoveToElement();
    }

    private static void CheckElement(XmlReader reader, string name, params string[] attributes)
    {
        CheckNamespace(reader);
        if (reader.LocalName != name)
            throw new BuildException($"Expected '{name}', got '{reader.LocalName}'.");
        CheckAttributes(reader, attributes);
    }

    private static void ValidateIdentifier(string id)
    {
        if (id.Length is < 1 or > 80 || !char.IsLetter(id[0]) ||
            id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.'))
            throw new BuildException($"Invalid target/property name '{id}'.");
    }
    private static void CheckPropertyName(string name) => ValidateIdentifier(name);
    private static void ValidateModule(string module)
    {
        if (module.Length is < 1 or > 80 || module.Any(c =>
                !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-' && c != '.'))
            throw new BuildException("Invalid module name.");
        if (module is "." or "..") throw new BuildException("Invalid module name.");
    }
    private static T ParseEnum<T>(string value, string name) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result) ||
            (!Enum.IsDefined(result) && typeof(T) != typeof(TargetArchitectureFeatures)))
            throw new BuildException($"Invalid {name} '{value}'.");
        return result;
    }
    private static TargetArchitectureKind ParseArchitecture(string architecture) => architecture.ToLowerInvariant() switch
    {
        "x86" => TargetArchitectureKind.I386,
        "x64" or "amd64" => TargetArchitectureKind.X86_64,
        "riscv32" or "rv32" => TargetArchitectureKind.RiscV32,
        "riscv64" or "rv64" => TargetArchitectureKind.RiscV64,
        "arm32" => TargetArchitectureKind.Arm32,
        "arm64" => TargetArchitectureKind.Arm64,
        "rbc32" => TargetArchitectureKind.RegisterBytecode,
        "rbc64" => TargetArchitectureKind.RegisterBytecode64,
        "wasm32" => TargetArchitectureKind.Wasm32,
        "wasm64" => TargetArchitectureKind.Wasm64,
        _ => ParseEnum<TargetArchitectureKind>(architecture, "TargetArchitecture")
    };
}
