using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace RokuRemoteAvalonia.Controls
{
    public partial class RokuKeyboard : UserControl
    {
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<RokuKeyboard, string>(nameof(Text), "");

        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<RokuKeyboard, string>(nameof(Title), "Enter Text");

        public static readonly StyledProperty<bool> ShowOkCancelProperty =
            AvaloniaProperty.Register<RokuKeyboard, bool>(nameof(ShowOkCancel), true);

        public static readonly StyledProperty<bool> IsShiftActiveProperty =
            AvaloniaProperty.Register<RokuKeyboard, bool>(nameof(IsShiftActive), false);

        public static readonly StyledProperty<bool> IsCapsLockActiveProperty =
            AvaloniaProperty.Register<RokuKeyboard, bool>(nameof(IsCapsLockActive), false);

        public string Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public bool ShowOkCancel
        {
            get => GetValue(ShowOkCancelProperty);
            set => SetValue(ShowOkCancelProperty, value);
        }

        public bool IsShiftActive
        {
            get => GetValue(IsShiftActiveProperty);
            set => SetValue(IsShiftActiveProperty, value);
        }

        public bool IsCapsLockActive
        {
            get => GetValue(IsCapsLockActiveProperty);
            set => SetValue(IsCapsLockActiveProperty, value);
        }

        public string ShiftButtonText => IsCapsLockActive ? "⇪ LOCK" : IsShiftActive ? "⇧ ON" : "⇧";

        public event EventHandler<string>? TextChanged;
        public event EventHandler<string>? KeyPressed;
        public event EventHandler? OkClicked;
        public event EventHandler? CancelClicked;

        public RokuKeyboard()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void OnKeyClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Content is string key)
            {
                string actualKey = ProcessKey(key);
                
                if (ShowOkCancel)
                {
                    // For dialog mode, accumulate text
                    Text += actualKey;
                    TextChanged?.Invoke(this, Text);
                }
                else
                {
                    // For direct input mode, send individual keypresses
                    KeyPressed?.Invoke(this, actualKey);
                }

                // Reset shift after key press (unless caps lock is active)
                if (IsShiftActive && !IsCapsLockActive)
                {
                    IsShiftActive = false;
                    OnPropertyChanged(nameof(IsShiftActive));
                    OnPropertyChanged(nameof(ShiftButtonText));
                }
            }
        }

        private string ProcessKey(string key)
        {
            // Handle shift/caps lock for letters
            if (key.Length == 1 && char.IsLetter(key[0]))
            {
                return (IsShiftActive || IsCapsLockActive) ? key.ToUpper() : key.ToLower();
            }

            // Handle shift for numbers/symbols
            if (IsShiftActive || IsCapsLockActive)
            {
                return key switch
                {
                    "1" => "!",
                    "2" => "@",
                    "3" => "#",
                    "4" => "$",
                    "5" => "%",
                    "6" => "^",
                    "7" => "&",
                    "8" => "*",
                    "9" => "(",
                    "0" => ")",
                    "-" => "_",
                    "=" => "+",
                    "[" => "{",
                    "]" => "}",
                    "\\" => "|",
                    ";" => ":",
                    "'" => "\"",
                    "," => "<",
                    "." => ">",
                    "/" => "?",
                    "`" => "~",
                    _ => key
                };
            }

            return key;
        }

        private void OnSpaceClick(object? sender, RoutedEventArgs e)
        {
            if (ShowOkCancel)
            {
                Text += " ";
                TextChanged?.Invoke(this, Text);
            }
            else
            {
                KeyPressed?.Invoke(this, " ");
            }
        }

        private void OnBackspaceClick(object? sender, RoutedEventArgs e)
        {
            if (ShowOkCancel)
            {
                if (Text.Length > 0)
                {
                    Text = Text[..^1];
                    TextChanged?.Invoke(this, Text);
                }
            }
            else
            {
                KeyPressed?.Invoke(this, "Backspace");
            }
        }

        private void OnClearClick(object? sender, RoutedEventArgs e)
        {
            if (ShowOkCancel)
            {
                Text = "";
                TextChanged?.Invoke(this, Text);
            }
            else
            {
                KeyPressed?.Invoke(this, "Clear");
            }
        }

        private void OnEnterClick(object? sender, RoutedEventArgs e)
        {
            if (ShowOkCancel)
            {
                Text += "\n";
                TextChanged?.Invoke(this, Text);
            }
            else
            {
                KeyPressed?.Invoke(this, "Enter");
            }
        }

        private void OnShiftClick(object? sender, RoutedEventArgs e)
        {
            if (IsCapsLockActive)
            {
                // Turn off caps lock
                IsCapsLockActive = false;
                IsShiftActive = false;
            }
            else if (IsShiftActive)
            {
                // Turn on caps lock
                IsCapsLockActive = true;
                IsShiftActive = false;
            }
            else
            {
                // Turn on shift
                IsShiftActive = true;
            }

            OnPropertyChanged(nameof(IsShiftActive));
            OnPropertyChanged(nameof(IsCapsLockActive));
            OnPropertyChanged(nameof(ShiftButtonText));
        }

        private void OnTabClick(object? sender, RoutedEventArgs e)
        {
            if (ShowOkCancel)
            {
                Text += "\t";
                TextChanged?.Invoke(this, Text);
            }
            else
            {
                KeyPressed?.Invoke(this, "\t");
            }
        }

        private void OnOkClick(object? sender, RoutedEventArgs e)
        {
            OkClicked?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            CancelClicked?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            // Trigger UI updates for shift state changes
        }
    }
}