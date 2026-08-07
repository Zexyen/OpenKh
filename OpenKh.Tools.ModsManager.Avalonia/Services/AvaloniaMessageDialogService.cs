using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using OpenKh.Tools.ModsManager.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed record NativeMessageButton(string Caption, MessageDialogResult Result, bool IsDefault, bool IsCancel);

    public static class AvaloniaMessageDialogMapping
    {
        public static IReadOnlyList<NativeMessageButton> Buttons(MessageDialogButtons buttons) => buttons switch
        {
            MessageDialogButtons.Ok => new[] { new NativeMessageButton("OK", MessageDialogResult.Ok, true, true) },
            MessageDialogButtons.OkCancel => new[]
            {
                new NativeMessageButton("OK", MessageDialogResult.Ok, true, false),
                new NativeMessageButton("Cancel", MessageDialogResult.Cancel, false, true),
            },
            MessageDialogButtons.YesNo => new[]
            {
                new NativeMessageButton("Yes", MessageDialogResult.Yes, true, false),
                new NativeMessageButton("No", MessageDialogResult.No, false, true),
            },
            MessageDialogButtons.YesNoCancel => new[]
            {
                new NativeMessageButton("Yes", MessageDialogResult.Yes, true, false),
                new NativeMessageButton("No", MessageDialogResult.No, false, false),
                new NativeMessageButton("Cancel", MessageDialogResult.Cancel, false, true),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(buttons)),
        };

        public static MessageDialogResult CloseResult(MessageDialogButtons buttons) =>
            Buttons(buttons).First(x => x.IsCancel).Result;

        public static string KindGlyph(MessageDialogKind kind) => kind switch
        {
            MessageDialogKind.Information => "ℹ",
            MessageDialogKind.Warning => "⚠",
            MessageDialogKind.Error => "⛔",
            MessageDialogKind.Question => "?",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public sealed class AvaloniaMessageDialogService : IMessageDialogService
    {
        private readonly Func<Window> _owner;

        public AvaloniaMessageDialogService(Func<Window> owner = null) =>
            _owner = owner ?? ActiveWindow;

        public async Task<MessageDialogResult> ShowAsync(
            MessageDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            var window = CreateWindow(request);
            using var registration = cancellationToken.Register(() =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => window.Close(AvaloniaMessageDialogMapping.CloseResult(request.Buttons))));

            var owner = _owner() ?? throw new InvalidOperationException(
                "A visible Avalonia window is required to show a modal message dialog.");
            return await window.ShowDialog<MessageDialogResult>(owner);
        }

        internal static Window CreateWindow(MessageDialogRequest request)
        {
            var buttons = AvaloniaMessageDialogMapping.Buttons(request.Buttons);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var window = new Window
            {
                Title = request.Title ?? string.Empty,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            foreach (var definition in buttons)
            {
                var button = new Button
                {
                    Content = definition.Caption,
                    MinWidth = 82,
                    IsDefault = definition.IsDefault,
                    IsCancel = definition.IsCancel,
                };
                button.Click += (_, _) => window.Close(definition.Result);
                panel.Children.Add(button);
            }

            window.Closing += (_, e) =>
            {
                if (!e.IsProgrammatic)
                {
                    e.Cancel = true;
                    window.Close(AvaloniaMessageDialogMapping.CloseResult(request.Buttons));
                }
            };
            window.Content = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("*,Auto"),
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                RowSpacing = 20,
                ColumnSpacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = AvaloniaMessageDialogMapping.KindGlyph(request.Kind),
                        FontSize = 28,
                        FontWeight = FontWeight.Bold,
                        VerticalAlignment = VerticalAlignment.Top,
                    },
                    new TextBlock
                    {
                        Text = request.Message ?? string.Empty,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 370,
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 1,
                    },
                    panel.WithGridPosition(1, 0, 1, 2),
                },
            };
            return window;
        }

        private static Window ActiveWindow() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(x => x.IsActive)
            ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    internal static class MessageDialogGridExtensions
    {
        public static T WithGridPosition<T>(this T control, int row, int column, int rowSpan, int columnSpan) where T : Control
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetRowSpan(control, rowSpan);
            Grid.SetColumnSpan(control, columnSpan);
            return control;
        }
    }
}
