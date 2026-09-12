using System;
using System.Collections.Generic;

namespace Cnidaria.Cs
{
    // The embedded runtime carries a helper for every service the language can ask for, and a
    // program asks for a handful. Compiling it is expensive and cached per target, so the helpers
    // nothing reachable names come off the compiled object instead
    internal sealed class ObjectTrimmer
    {
        private readonly List<SectionInfo> _sections = new List<SectionInfo>();
        private readonly Dictionary<string, int> _sectionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Definition> _definitions = new List<Definition>();
        private readonly Dictionary<string, List<int>> _definitionsByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private readonly List<List<int>> _referencesByDefinition = new List<List<int>>();
        private readonly List<string> _targetNames = new List<string>();
        private readonly List<string> _unownedReferences = new List<string>();
        private bool _indexed;

        public void AddSection(string name, int size, int alignment)
        {
            if (string.IsNullOrEmpty(name) || size < 0 || _sectionIndex.ContainsKey(name))
                return;

            _sectionIndex.Add(name, _sections.Count);
            _sections.Add(new SectionInfo(name, size, Math.Max(1, alignment)));
        }

        public void AddDefinition(string name, string section, int offset, int size)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(section) || offset < 0 || size <= 0 ||
                !_sectionIndex.TryGetValue(section, out int sectionIndex))
            {
                return;
            }

            int index = _definitions.Count;
            _definitions.Add(new Definition(name, sectionIndex, offset, checked(offset + size)));
            _referencesByDefinition.Add(new List<int>());
            if (!_definitionsByName.TryGetValue(name, out var list))
            {
                list = new List<int>(1);
                _definitionsByName.Add(name, list);
            }
            list.Add(index);
        }

        public void AddReference(string section, int offset, string targetName)
        {
            if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(section) || offset < 0 ||
                !_sectionIndex.TryGetValue(section, out int sectionIndex))
            {
                return;
            }

            EnsureIndexed();
            SectionInfo info = _sections[sectionIndex];
            var ordered = info.OrderedDefinitions;
            int targetIndex = _targetNames.Count;
            bool owned = false;
            for (int i = UpperBound(ordered, offset) - 1; i >= 0; i--)
            {
                Definition definition = _definitions[ordered[i]];
                if (offset - definition.Start >= info.WidestDefinition)
                    break;
                if (offset < definition.End)
                {
                    _referencesByDefinition[ordered[i]].Add(targetIndex);
                    owned = true;
                }
            }

            _targetNames.Add(targetName);

            // A reference that sits outside every definition belongs to no one that can be dropped,
            // so whatever it names has to stay
            if (!owned)
                _unownedReferences.Add(targetName);
        }

        public ObjectTrimLayout Trim(IEnumerable<string> rootNames)
        {
            if (rootNames is null)
                throw new ArgumentNullException(nameof(rootNames));

            EnsureIndexed();
            var live = new bool[_definitions.Count];
            var pending = new Queue<int>();
            var visitedNames = new HashSet<string>(StringComparer.Ordinal);

            void MarkName(string name)
            {
                if (!visitedNames.Add(name) || !_definitionsByName.TryGetValue(name, out var list))
                    return;

                for (int i = 0; i < list.Count; i++)
                {
                    int index = list[i];
                    if (live[index])
                        continue;
                    live[index] = true;
                    pending.Enqueue(index);
                }
            }

            foreach (string root in rootNames)
            {
                if (!string.IsNullOrEmpty(root))
                    MarkName(root);
            }

            for (int i = 0; i < _unownedReferences.Count; i++)
                MarkName(_unownedReferences[i]);

            while (pending.Count != 0)
            {
                var outgoing = _referencesByDefinition[pending.Dequeue()];
                for (int i = 0; i < outgoing.Count; i++)
                    MarkName(_targetNames[outgoing[i]]);
            }

            var layouts = new Dictionary<string, SectionLayout>(StringComparer.Ordinal);
            bool removedAnything = false;
            for (int s = 0; s < _sections.Count; s++)
            {
                SectionInfo info = _sections[s];
                SectionLayout layout = SectionLayout.Build(info, _definitions, live);
                layouts.Add(info.Name, layout);
                removedAnything |= layout.Size != info.Size;
            }

            var liveNames = new SortedSet<string>(StringComparer.Ordinal);
            for (int d = 0; d < _definitions.Count; d++)
            {
                if (live[d])
                    liveNames.Add(_definitions[d].Name);
            }

            return new ObjectTrimLayout(layouts, liveNames, removedAnything);
        }

        private void EnsureIndexed()
        {
            if (_indexed)
                return;

            _indexed = true;
            for (int s = 0; s < _sections.Count; s++)
            {
                var ordered = new List<int>();
                int widest = 1;
                for (int d = 0; d < _definitions.Count; d++)
                {
                    if (_definitions[d].SectionIndex != s)
                        continue;
                    ordered.Add(d);
                    widest = Math.Max(widest, _definitions[d].End - _definitions[d].Start);
                }

                ordered.Sort((left, right) => _definitions[left].Start.CompareTo(_definitions[right].Start));
                _sections[s].OrderedDefinitions = ordered;
                _sections[s].WidestDefinition = widest;
            }
        }

        private int UpperBound(List<int> ordered, int offset)
        {
            int low = 0;
            int high = ordered.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (_definitions[ordered[mid]].Start <= offset)
                    low = mid + 1;
                else
                    high = mid;
            }
            return low;
        }

        internal readonly struct Definition
        {
            public readonly string Name;
            public readonly int SectionIndex;
            public readonly int Start;
            public readonly int End;

            public Definition(string name, int sectionIndex, int start, int end)
            {
                Name = name;
                SectionIndex = sectionIndex;
                Start = start;
                End = end;
            }
        }

        internal sealed class SectionInfo
        {
            public string Name { get; }
            public int Size { get; }
            public int Alignment { get; }
            public List<int> OrderedDefinitions { get; set; } = new List<int>();
            public int WidestDefinition { get; set; } = 1;

            public SectionInfo(string name, int size, int alignment)
            {
                Name = name;
                Size = size;
                Alignment = alignment;
            }
        }

        // A hole is a whole number of section alignment units, which is what keeps the definitions
        // after it sitting where their own alignment needs them
        internal sealed class SectionLayout
        {
            private readonly int[] _holeStarts;
            private readonly int[] _holeEnds;

            public int Size { get; }

            private SectionLayout(int[] holeStarts, int[] holeEnds, int size)
            {
                _holeStarts = holeStarts;
                _holeEnds = holeEnds;
                Size = size;
            }

            internal static SectionLayout Build(SectionInfo info, List<Definition> definitions, bool[] live)
            {
                var ordered = info.OrderedDefinitions;
                var holeStarts = new List<int>();
                var holeEnds = new List<int>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (live[ordered[i]])
                        continue;

                    Definition definition = definitions[ordered[i]];
                    int start = definition.Start;
                    int end = Math.Min(info.Size, definition.End);

                    // Anything a surviving definition also covers stays, overlap and all
                    for (int j = 0; j < ordered.Count && end > start; j++)
                    {
                        if (!live[ordered[j]])
                            continue;
                        Definition other = definitions[ordered[j]];
                        if (other.End <= start || other.Start >= end)
                            continue;
                        if (other.Start <= start)
                            start = Math.Max(start, other.End);
                        else
                            end = Math.Min(end, other.Start);
                    }

                    start = AlignUp(start, info.Alignment);
                    end = AlignDown(end, info.Alignment);
                    if (end <= start)
                        continue;

                    if (holeEnds.Count != 0 && holeEnds[^1] >= start)
                        holeEnds[^1] = Math.Max(holeEnds[^1], end);
                    else
                    {
                        holeStarts.Add(start);
                        holeEnds.Add(end);
                    }
                }

                int removed = 0;
                for (int i = 0; i < holeStarts.Count; i++)
                    removed += holeEnds[i] - holeStarts[i];

                return new SectionLayout(holeStarts.ToArray(), holeEnds.ToArray(), checked(info.Size - removed));
            }

            public bool IsLive(int offset)
            {
                int low = 0;
                int high = _holeStarts.Length - 1;
                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    if (offset < _holeStarts[mid])
                        high = mid - 1;
                    else if (offset >= _holeEnds[mid])
                        low = mid + 1;
                    else
                        return false;
                }
                return true;
            }

            public int Map(int offset)
            {
                int removed = 0;
                for (int i = 0; i < _holeStarts.Length; i++)
                {
                    if (_holeStarts[i] >= offset)
                        break;
                    removed += Math.Min(offset, _holeEnds[i]) - _holeStarts[i];
                }
                return Math.Max(0, offset - removed);
            }

            private static int AlignUp(int value, int alignment)
            {
                int remainder = value % alignment;
                return remainder == 0 ? value : checked(value + alignment - remainder);
            }

            private static int AlignDown(int value, int alignment)
                => value - (value % alignment);
        }
    }

    internal sealed class ObjectTrimLayout
    {
        private readonly Dictionary<string, ObjectTrimmer.SectionLayout> _sections;
        private readonly SortedSet<string> _liveNames;

        public bool RemovedAnything { get; }

        // Two programs that reach the same helpers share one trimmed object
        public string LiveKey { get; }

        public ObjectTrimLayout(
            Dictionary<string, ObjectTrimmer.SectionLayout> sections,
            SortedSet<string> liveNames,
            bool removedAnything)
        {
            _sections = sections;
            _liveNames = liveNames;
            RemovedAnything = removedAnything;
            LiveKey = string.Join(",", liveNames);
        }

        public bool IsDefinitionLive(string name)
            => _liveNames.Contains(name);

        public bool IsLive(string section, int offset)
            => !_sections.TryGetValue(section, out var layout) || layout.IsLive(offset);

        public int Map(string section, int offset)
            => _sections.TryGetValue(section, out var layout) ? layout.Map(offset) : offset;

        public int SizeOf(string section, int originalSize)
            => _sections.TryGetValue(section, out var layout) ? layout.Size : originalSize;
    }
}
