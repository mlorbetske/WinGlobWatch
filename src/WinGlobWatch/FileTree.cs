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
        private readonly ReaderWriterLockSlim _sync = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly ConcurrentBag<ModelState<TPattern>> _dirtyEntries = new ConcurrentBag<ModelState<TPattern>>();
        private readonly Dictionary<string, ModelState<TPattern>> _entries = new Dictionary<string, ModelState<TPattern>>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<ICollection<PatternEntry<TPattern>>> _patterns;
        private readonly string _root;
        private int _queryVersion;

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
            _sync.EnterUpgradeableReadLock();
            if (!_entries.TryGetValue(relPath, out result))
            {
                _sync.EnterWriteLock();
                result = generator(relPath);
                _entries[relPath] = result;
                _sync.ExitWriteLock();
            }

            _sync.ExitUpgradeableReadLock();

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

                _sync.EnterReadLock();
                if (_entries.TryGetValue("/", out result))
                {
                    _sync.ExitReadLock();
                    return result;
                }

                _sync.ExitReadLock();
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

            _sync.EnterWriteLock();
            _entries.Clear();
            _entries["/"] = new ModelState<TPattern>(ModelKind.Directory, "", "/", _root, null, () => { });
            _sync.ExitWriteLock();
            await Parallel.ForEach(Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories), ProcessCreate);

            Clean();
        }

        internal void Delete(string fullPath)
        {
            string parent = Path.GetDirectoryName(fullPath);
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');

            MarkDirty(parent);
            ModelState<TPattern> m;
            _sync.EnterWriteLock();
            if (_entries.TryGetValue(relPath, out m))
            {
                m.Delete();
            }
            _sync.ExitWriteLock();
        }

        internal void MarkDirty(string fullPath)
        {
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');
            ModelState<TPattern> m;
            _sync.EnterReadLock();
            if (_entries.TryGetValue(relPath, out m))
            {
                m.IsDirty = true;
                _dirtyEntries.Add(m);
            }
            _sync.ExitReadLock();
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

            CheckPatterns(model);

            if (parentModel != null)
            {
                parentModel.AddChild(model);
                MarkDirty(parentModel.FullPath);
            }

            return model;
        }

        private void CheckPatterns(ModelState<TPattern> model)
        {
            ICollection<PatternEntry<TPattern>> patterns = _patterns();
            CheckPatterns(model, patterns);
        }

        private static void CheckPatterns(ModelState<TPattern> model, ICollection<PatternEntry<TPattern>> patterns)
        {
            bool isIncluded = false;
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
        }

        internal void ProcessCreate(string fullPath)
        {
            string relPath = fullPath.Substring(_root.Length - 1).Replace('\\', '/');
            GetOrAddEntry(relPath, r => ProcessCreateInternal(fullPath));
        }

        private void RemoveFileInternal(string path)
        {
            _sync.EnterWriteLock();
            _entries.Remove(path);
            _sync.ExitWriteLock();
        }

        public void Empty()
        {
            _sync.EnterWriteLock();
            _entries.Clear();
            _sync.ExitWriteLock();

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

        public async void UpdateMatches()
        {
            int expect = Interlocked.Increment(ref _queryVersion);
            ICollection<PatternEntry<TPattern>> patterns = _patterns();
            await Parallel.ForEach(_entries.Values, m =>
            {
                if (Volatile.Read(ref _queryVersion) != expect)
                {
                    return;
                }

                CheckPatterns(m, patterns);
            }, 1);
        }
    }
}