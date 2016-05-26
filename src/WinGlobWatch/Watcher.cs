using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WinGlobWatch
{
    public class Watcher<TPattern> : BindableBase, IDisposable
        where TPattern : class, IPattern
    {
        private static readonly ConcurrentDictionary<string, Task<Watcher<TPattern>>> Watchers = new ConcurrentDictionary<string, Task<Watcher<TPattern>>>(StringComparer.OrdinalIgnoreCase);
        private readonly FileTree<TPattern> _fileTree;
        private List<PatternEntry<TPattern>> _patterns;
        private readonly string _root;
        private readonly FileSystemWatcher _watcher;

        private Watcher(string root, FileSystemWatcher watcher)
        {
            _root = root;
            _watcher = watcher;
            _patterns = new List<PatternEntry<TPattern>>();
            _fileTree = new FileTree<TPattern>(() => _patterns, root);
        }

        public ModelState<TPattern> Root => _fileTree.Root;

        public void Clean()
        {
            _fileTree.Clean();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public static async Task<Watcher<TPattern>> For(string dir)
        {
            return await Watchers.GetOrAdd(dir, async d =>
            {
                FileSystemWatcher watcher = new FileSystemWatcher(d)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = true
                };

                Watcher<TPattern> result = new Watcher<TPattern>(dir, watcher);
                watcher.Renamed += result.EntryRenamed;
                watcher.Created += result.EntryCreated;
                watcher.Changed += result.EntryChanged;
                watcher.Deleted += result.EntryDeleted;
                watcher.EnableRaisingEvents = true;
                await result._fileTree.ScanAsync();
                result.RaisePropertyChanged(nameof(Root));
                return result;
            });
        }

        public PatternEntry<TPattern> AddPattern(EntryKind kind, TPattern pattern)
        {
            PatternEntry<TPattern> p = new PatternEntry<TPattern>(kind, pattern);
            AddPattern(p);
            return p;
        }

        public void AddPattern(PatternEntry<TPattern> pattern)
        {
            List<PatternEntry<TPattern>> p = new List<PatternEntry<TPattern>>(_patterns) {pattern};
            _patterns = p;
            _fileTree.UpdateMatches();
        }

        protected virtual void Dispose(bool isDisposing)
        {
            Task<Watcher<TPattern>> watcher;
            Watchers.TryRemove(_root, out watcher);
            _fileTree.Empty();
            _watcher.EnableRaisingEvents = true;
            _watcher.Renamed -= EntryRenamed;
            _watcher.Created -= EntryCreated;
            _watcher.Changed -= EntryChanged;
            _watcher.Deleted -= EntryDeleted;
            _watcher.Dispose();
        }

        private void EntryChanged(object sender, FileSystemEventArgs e)
        {
            _fileTree.MarkDirty(e.FullPath);
        }

        private void EntryCreated(object sender, FileSystemEventArgs e)
        {
            _fileTree.ProcessCreate(e.FullPath);
        }

        private void EntryDeleted(object sender, FileSystemEventArgs e)
        {
            _fileTree.Delete(e.FullPath);
        }

        private void EntryRenamed(object sender, RenamedEventArgs e)
        {
            _fileTree.Delete(e.OldFullPath);
            _fileTree.ProcessCreate(e.FullPath);
        }

        ~Watcher()
        {
            Dispose(false);
        }

        public void RemovePattern(PatternEntry<TPattern> pattern)
        {
            List<PatternEntry<TPattern>> p = new List<PatternEntry<TPattern>>(_patterns);
            p.Remove(pattern);
            _patterns = p;
            _fileTree.UpdateMatches();
        }
    }
}