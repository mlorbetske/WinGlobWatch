using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinGlobWatch.WpfApp
{
    public class MainWindowViewModel : BindableBase
    {
        private string _currentRootDir;
        private string _rootDir;
        private Watcher<GlobPatternWithMetadata> _watcher;
        private string _patternKind;
        private string _currentPattern;

        public MainWindowViewModel()
        {
            Patterns = new ObservableCollection<PatternModel>();
            UpdateWatcherCommand = new ActionCommand(UpdateWatcher, RootDirExists);
            SetCleanCommand = new ActionCommand(Clean, WatcherExists);
            AddPatternCommand = new ActionCommand(AddPattern);
        }

        private void AddPattern()
        {
            PatternModel model = new PatternModel(PatternKind == "Include" ? EntryKind.Include : EntryKind.Exclude, CurrentPattern, Patterns.Count.ToString(), Patterns, () => _watcher);
            model.Pattern = _watcher?.AddPattern(model.Kind, model.RawPattern);
            Patterns.Add(model);
        }

        public ObservableCollection<PatternModel> Patterns { get; set; }

        public string CurrentRootDir
        {
            get { return _currentRootDir; }
            set { Set(ref _currentRootDir, value, StringComparer.OrdinalIgnoreCase); }
        }

        public Watcher<GlobPatternWithMetadata> CurrentWatcher
        {
            get { return _watcher; }
            set { Set(ref _watcher, value); }
        }

        public string RootDir
        {
            get { return _rootDir; }
            set
            {
                if (Set(ref _rootDir, value, StringComparer.OrdinalIgnoreCase))
                {
                    UpdateWatcherCommand.CanExecute(null);
                }
            }
        }

        public ICommand SetCleanCommand { get; }

        public ICommand UpdateWatcherCommand { get; }

        public string PatternKind
        {
            get { return _patternKind; }
            set { Set(ref _patternKind, value); }
        }

        public string CurrentPattern
        {
            get { return _currentPattern; }
            set { Set(ref _currentPattern, value); }
        }

        public ICommand AddPatternCommand { get; }

        private void Clean()
        {
            CurrentWatcher.Clean();
        }

        private bool RootDirExists()
        {
            return Directory.Exists(RootDir);
        }

        private async void UpdateWatcher()
        {
            CurrentWatcher?.Dispose();
            CurrentRootDir = RootDir;
            Watcher<GlobPatternWithMetadata> w = await Watcher<GlobPatternWithMetadata>.For(CurrentRootDir);

            foreach (PatternModel model in Patterns)
            {
                model.Pattern = w.AddPattern(model.Kind, model.RawPattern);
            }

            CurrentWatcher = w;
            SetCleanCommand.CanExecute(null);
        }

        private bool WatcherExists()
        {
            return _watcher != null;
        }
    }

    public class PatternModel
    {
        private readonly string _metadata;
        private readonly string _currentPattern;
        private readonly ObservableCollection<PatternModel> _owner;
        private readonly Func<Watcher<GlobPatternWithMetadata>> _watcher;

        public PatternModel(EntryKind entryKind, string currentPattern, string metadata, ObservableCollection<PatternModel> owner, Func<Watcher<GlobPatternWithMetadata>> watcher)
        {
            _watcher = watcher;
            _owner = owner;
            Kind = entryKind;
            _currentPattern = currentPattern;
            _metadata = metadata;
            RawPattern = new GlobPatternWithMetadata(currentPattern, metadata);
            RemovePatternCommand = new ActionCommand(() =>
            {
                _owner.Remove(this);
                _watcher()?.RemovePattern(Pattern);
            });
        }

        public PatternEntry<GlobPatternWithMetadata> Pattern { get; set; }


        public ICommand RemovePatternCommand { get; }

        public string Text => $"{Kind} - {_currentPattern} ({_metadata})";

        public EntryKind Kind { get; }

        public GlobPatternWithMetadata RawPattern { get; }
    }
}