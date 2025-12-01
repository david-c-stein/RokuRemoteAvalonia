using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RokuRemoteAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace RokuRemoteAvalonia.Controls
{
    public partial class ActionReviewDialog : UserControl, INotifyPropertyChanged
    {
        private ObservableCollection<QuickLaunchActionViewModel> _actions = new();
        private string _title = "Review Actions";
        private QuickLaunchSequence? _originalSequence;

        public ActionReviewDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        public ObservableCollection<QuickLaunchActionViewModel> Actions
        {
            get => _actions;
            set
            {
                _actions = value;
                OnPropertyChanged(nameof(Actions));
                OnPropertyChanged(nameof(HasActions));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ActionsSummary));
                UpdateStepNumbers();
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        public bool HasActions => Actions.Count > 0;
        public bool IsEmpty => Actions.Count == 0;
        
        public string ActionsSummary
        {
            get
            {
                if (Actions.Count == 0) return "No actions configured";
                if (Actions.Count == 1) return "1 action in sequence";
                return $"{Actions.Count} actions in sequence";
            }
        }

        public event EventHandler? SaveChanges;
        public event EventHandler? CancelChanges;
        public event EventHandler? TestSequence;
        public event EventHandler? ClearAll;

        public void LoadSequence(QuickLaunchSequence sequence)
        {
            _originalSequence = sequence;
            Title = $"Review Actions - {sequence.Name}";
            
            Actions.Clear();
            for (int i = 0; i < sequence.Actions.Count; i++)
            {
                var action = sequence.Actions[i];
                var viewModel = new QuickLaunchActionViewModel(action, i + 1)
                {
                    CanMoveUp = i > 0,
                    CanMoveDown = i < sequence.Actions.Count - 1
                };
                Actions.Add(viewModel);
            }
        }

        public List<QuickLaunchAction> GetModifiedActions()
        {
            return Actions.Select(vm => vm.Action).ToList();
        }

        private void UpdateStepNumbers()
        {
            for (int i = 0; i < Actions.Count; i++)
            {
                Actions[i].StepNumber = i + 1;
                Actions[i].CanMoveUp = i > 0;
                Actions[i].CanMoveDown = i < Actions.Count - 1;
            }
            OnPropertyChanged(nameof(ActionsSummary));
        }

        private void OnMoveUpAction(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is QuickLaunchActionViewModel action)
            {
                var index = Actions.IndexOf(action);
                if (index > 0)
                {
                    Actions.Move(index, index - 1);
                    UpdateStepNumbers();
                }
            }
        }

        private void OnMoveDownAction(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is QuickLaunchActionViewModel action)
            {
                var index = Actions.IndexOf(action);
                if (index < Actions.Count - 1)
                {
                    Actions.Move(index, index + 1);
                    UpdateStepNumbers();
                }
            }
        }

        private void OnDeleteAction(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is QuickLaunchActionViewModel action)
            {
                Actions.Remove(action);
                UpdateStepNumbers();
            }
        }

        private void OnClearAllActions(object? sender, RoutedEventArgs e)
        {
            Actions.Clear();
            UpdateStepNumbers();
            ClearAll?.Invoke(this, EventArgs.Empty);
        }

        private void OnTestSequence(object? sender, RoutedEventArgs e)
        {
            TestSequence?.Invoke(this, EventArgs.Empty);
        }

        private void OnSaveChanges(object? sender, RoutedEventArgs e)
        {
            SaveChanges?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancelChanges(object? sender, RoutedEventArgs e)
        {
            CancelChanges?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class QuickLaunchActionViewModel : INotifyPropertyChanged
    {
        private int _stepNumber;
        private bool _canMoveUp;
        private bool _canMoveDown;

        public QuickLaunchActionViewModel(QuickLaunchAction action, int stepNumber)
        {
            Action = action;
            StepNumber = stepNumber;
        }

        public QuickLaunchAction Action { get; }

        public int StepNumber
        {
            get => _stepNumber;
            set
            {
                _stepNumber = value;
                OnPropertyChanged(nameof(StepNumber));
            }
        }

        public bool CanMoveUp
        {
            get => _canMoveUp;
            set
            {
                _canMoveUp = value;
                OnPropertyChanged(nameof(CanMoveUp));
            }
        }

        public bool CanMoveDown
        {
            get => _canMoveDown;
            set
            {
                _canMoveDown = value;
                OnPropertyChanged(nameof(CanMoveDown));
            }
        }

        public string DisplayName => Action.DisplayName;
        
        public string TypeIcon => Action.Type switch
        {
            ActionType.KeyPress => "🎮",
            ActionType.AppLaunch => "📱",
            ActionType.Wait => "⏱",
            _ => "❓"
        };

        public string ActionDetails => Action.Type switch
        {
            ActionType.KeyPress => $"Key: {Action.Command}",
            ActionType.AppLaunch => $"App ID: {Action.Parameter}",
            ActionType.Wait => $"Wait: {Action.DelayMs}ms",
            _ => ""
        };

        public bool HasDetails => !string.IsNullOrEmpty(ActionDetails);

        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
