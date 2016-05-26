using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace WinGlobWatch
{
    public class ModelState<TPattern> : BindableBase
        where TPattern : class, IPattern
    {
        private readonly ConcurrentDictionary<string, ModelState<TPattern>> _children = new ConcurrentDictionary<string, ModelState<TPattern>>(StringComparer.OrdinalIgnoreCase);
        private readonly Action _deleteAction;
        private readonly ModelState<TPattern> _parentModel;
        private bool _isDirty;
        private bool _isIncluded;
        private TPattern _matchedPattern;

        public ModelState(ModelKind kind, string name, string relPath, string fullPath, ModelState<TPattern> parentModel, Action deleteAction)
        {
            FullPath = fullPath;
            _deleteAction = deleteAction;
            Kind = kind;
            Name = name;
            Path = relPath;
            _parentModel = parentModel;
        }

        public TPattern MatchedPattern
        {
            get { return _matchedPattern; }
            set { Set(ref _matchedPattern, value); }
        }

        public IEnumerable<ModelState<TPattern>> Children => _children.Values.OrderByDescending(x => x.Kind).ThenBy(x => x.Name);

        public string FullPath { get; }

        public bool IsDirty
        {
            get { return _isDirty; }
            internal set { Set(ref _isDirty, value); }
        }

        public bool IsIncluded
        {
            get { return _isIncluded; }
            internal set { Set(ref _isIncluded, value); }
        }

        public ModelKind Kind { get; }

        public string Name { get; }

        public string Path { get; }

        public void AddChild(ModelState<TPattern> model)
        {
            _children[model.Name] = model;
            RaisePropertyChanged(nameof(Children));
        }

        public void Delete()
        {
            _deleteAction();

            _parentModel?.RemoveChild(this);

            foreach (ModelState<TPattern> child in _children.Values)
            {
                child.Delete();
            }
        }

        public void RemoveChild(ModelState<TPattern> model)
        {
            ModelState<TPattern> m;
            _children.TryRemove(model.Name, out m);
            RaisePropertyChanged(nameof(Children));
        }
    }
}