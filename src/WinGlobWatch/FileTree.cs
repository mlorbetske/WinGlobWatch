using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinGlobWatch
{
    public class FileTree<TPattern>
        where TPattern : class, IPattern
    {
        private readonly ConcurrentBag<ModelState<TPattern>> _dirtyEntries = new ConcurrentBag<ModelState<TPattern>>();
        private readonly ConcurrentDictionary<string, ModelState<TPattern>> _entries = new ConcurrentDictionary<string, ModelState<TPattern>>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<ICollection<PatternEntry<TPattern>>> _patterns;
        private readonly string _root;
        private int _queryVersion;

        public Task Ready { get; private set; }

        public EventHandler Dirty;

        public EventHandler FilteredEntriesChanged;

        public FileTree(Func<ICollection<PatternEntry<TPattern>>> patterns, string root)
        {
            _patterns = patterns;
            _root = root;

            if (_root[_root.Length - 1] != '\\')
            {
                _root += "\\";
            }
        }

        private ModelState<TPattern> GetOrAddEntry(string relPath, Func<string, ModelState<TPattern>> generator)
        {
            ModelState<TPattern> result;
            if (!_entries.TryGetValue(relPath, out result))
            {
                result = generator(relPath);
                _entries[relPath] = result;
            }

            return result;
        }

        private ModelState<TPattern> Demand(string fullPath)
        {
            if (fullPath.Length < _root.Length - 2)
            {
                return null;
            }

            string relPath = "/" + fullPath.Substring(_root.Length - 1).Replace('\\', '/').TrimStart('/');
            return GetOrAddEntry(relPath, r => ProcessCreateInternal(fullPath));
        }

        public ModelState<TPattern> Root
        {
            get
            {
                ModelState<TPattern> result;

                if (_entries.TryGetValue("/", out result))
                {
                    return result;
                }

                return null;
            }
        }

        public void Clean()
        {
            foreach (ModelState<TPattern> model in _dirtyEntries)
            {
                model.IsDirty = false;
            }

            ModelState<TPattern> m;
            while (_dirtyEntries.TryTake(out m))
            {
            }
        }

        public async Task ScanAsync()
        {
            ModelState<TPattern> m;
            while (_dirtyEntries.TryTake(out m))
            {
            }

            _entries.Clear();
            _entries["/"] = new ModelState<TPattern>(ModelKind.Directory, "", "/", _root, null, () => { });
            await Parallel.ForEach(Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories), ProcessCreate);

            Clean();
        }

        internal void Delete(string fullPath)
        {
            string parent = Path.GetDirectoryName(fullPath).TrimEnd('/', '\\') + '/';
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');

            ModelState<TPattern> m;
            if (_entries.TryGetValue(relPath, out m))
            {
                if (m.IsIncluded)
                {
                    MarkDirty(parent, true);
                }

                m.Delete();
            }
        }

        internal void MarkDirty(string fullPath, bool force = false)
        {
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');
            ModelState<TPattern> m;
            if (_entries.TryGetValue(relPath, out m))
            {
                //Don't dirty files that aren't included, but don't undirty files if they're 
                //  not included anymore but haven't been cleaned yet
                m.IsDirty |= m.IsIncluded || force;
                _dirtyEntries.Add(m);
                Dirty?.Invoke(this, EventArgs.Empty);
            }
        }

        private ModelState<TPattern> ProcessCreateInternal(string fullPath)
        {
            string parent = Path.GetDirectoryName(fullPath);
            string name = Path.GetFileName(fullPath);
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');


            ModelState<TPattern> parentModel = null;
            if (parent != null)
            {
                parentModel = Demand(parent);
            }

            ModelKind kind = File.Exists(fullPath) ? ModelKind.File : ModelKind.Directory;
            ModelState<TPattern> model = new ModelState<TPattern>(kind, name, relPath, fullPath, parentModel, () => RemoveFileInternal(relPath));

            if (CheckPatterns(model))
            {
                FilteredEntriesChanged?.Invoke(this, EventArgs.Empty);
            }

            if (parentModel != null)
            {
                parentModel.AddChild(model);

                if (model.IsIncluded)
                {
                    MarkDirty(parentModel.FullPath, true);
                }
            }

            return model;
        }

        private bool CheckPatterns(ModelState<TPattern> model)
        {
            ICollection<PatternEntry<TPattern>> patterns = _patterns();
            return CheckPatterns(model, patterns);
        }

        private static bool CheckPatterns(ModelState<TPattern> model, ICollection<PatternEntry<TPattern>> patterns)
        {
            bool isIncluded = false;
            bool initial = model.IsIncluded;
            TPattern matched = null;

            foreach (PatternEntry<TPattern> entry in patterns)
            {
                if (((entry.Kind == EntryKind.Include && !isIncluded) || (entry.Kind == EntryKind.Exclude && isIncluded))
                    && entry.Pattern.IsMatch(model.Path))
                {
                    isIncluded = entry.Kind != EntryKind.Exclude;
                    matched = isIncluded ? entry.Pattern : null;
                }
            }

            model.MatchedPattern = matched;
            model.IsIncluded = isIncluded;
            return initial != model.IsIncluded;
        }

        internal void ProcessCreate(string fullPath)
        {
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');
            GetOrAddEntry(relPath, r => ProcessCreateInternal(fullPath));
        }

        private void RemoveFileInternal(string path)
        {
            ModelState<TPattern> model;
            _entries.TryRemove(path, out model);
        }

        public void Empty()
        {
            _entries.Clear();

            try
            {
                ModelState<TPattern> m;
                while (_dirtyEntries.TryTake(out m))
                {
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void UpdateMatches()
        {
            Ready = UpdateMatchesInternal();
        }

        private async Task UpdateMatchesInternal()
        {
            int expect = Interlocked.Increment(ref _queryVersion);
            ICollection<PatternEntry<TPattern>> patterns = _patterns();
            bool changed = false;
            await Parallel.ForEach(_entries.Values, m =>
            {
                if (Volatile.Read(ref _queryVersion) != expect)
                {
                    return;
                }

                if (CheckPatterns(m, patterns))
                {
                    changed = true;
                }
            }, 1);

            if (changed)
            {
                FilteredEntriesChanged?.Invoke(this, EventArgs.Empty);
            }

        }
    }
}