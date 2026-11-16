using FlyyLink.Core;
using FlyyLink.Icons;
using FlyyLink.Model;
using FlyyLink.Shell;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using static FlyyLink.Shell.NativeMethods;

namespace FlyyLink.Views;

/// The pages of the Settings window, in sidebar order.
public enum SettingsSection { General, Notifications, Sharing, Devices, About }

/// FlyyLink's settings: a sidebar of sections and the chosen section's controls. Mirrors SettingsWindow.swift.
public sealed class SettingsWindow : Window
{
    private static readonly (SettingsSection Section, string Title, Lucide Icon)[] Sections =
    [
        (SettingsSection.General, "General", Lucide.Settings),
        (SettingsSection.Notifications, "Notifications", Lucide.Bell),
        (SettingsSection.Sharing, "Sharing", Lucide.Send),
        (SettingsSection.Devices, "Devices", Lucide.Smartphone),
        (SettingsSection.About, "About", Lucide.CircleCheck),
    ];

    private readonly AppModel model;
    private readonly StackPanel sidebar = new() { Spacing = 2, Padding = new Thickness(10, 18, 10, 18) };
    private readonly StackPanel page = new() { Spacing = 18, Padding = new Thickness(26, 22, 26, 26) };
    private SettingsSection section = SettingsSection.General;
    private bool shown;

    public SettingsWindow(AppModel model)
    {
        this.model = model;
        Title = "FlyyLink Settings";
        SystemBackdrop = new MicaBackdrop();

        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 640;
            presenter.PreferredMinimumHeight = 460;
        }

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var content = new Border { Child = new ScrollViewer { Content = page } };
        content.SetValue(Border.BackgroundProperty, Resource("FlyyLinkPanelBrush"));
        content.SetValue(Border.BorderBrushProperty, Resource("FlyyLinkBorderBrush"));
        content.BorderThickness = new Thickness(1, 0, 0, 0);
        Grid.SetColumn(content, 1);

        root.Children.Add(sidebar);
        root.Children.Add(content);
        Content = root;

        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AppModel.Devices) && section == SettingsSection.Devices) Render();
        };
        model.Licence.PropertyChanged += (_, _) =>
        {
            if (section == SettingsSection.About) Render();
        };

        Render();
    }

    /// The licence card in the panel was asked for.
    public event Action? PanelRequested;

    public void Present()
    {
        if (!shown)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(720 * scale), (int)(520 * scale)));
            shown = true;
        }

        Render();
        AppWindow.Show(true);
        Activate();
    }

    private void Render()
    {
        RenderSidebar();

        page.Children.Clear();
        page.Children.Add(new TextBlock { Text = Sections.First(s => s.Section == section).Title, FontSize = 24, FontWeight = FontWeights.Bold });

        foreach (var block in section switch
        {
            SettingsSection.General => General(),
            SettingsSection.Notifications => Notifications(),
            SettingsSection.Sharing => Sharing(),
            SettingsSection.Devices => Devices(),
            _ => About(),
        })
        {
            page.Children.Add(block);
        }
    }

    private void RenderSidebar()
    {
        sidebar.Children.Clear();

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 14) };
        brand.Children.Add(new AppGlyph(22));
        brand.Children.Add(new TextBlock { Text = "FlyyLink", FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        sidebar.Children.Add(brand);

        foreach (var (item, title, icon) in Sections) sidebar.Children.Add(SidebarRow(item, title, icon));
    }

    private Button SidebarRow(SettingsSection item, string title, Lucide icon)
    {
        var selected = item == section;
        var glyph = new LucideIcon { Icon = icon, Size = 12 };
        glyph.SetValue(LucideIcon.ForegroundProperty, Resource(selected ? "FlyyLinkOnAccentBrush" : "FlyyLinkAccentBrush"));
        var tile = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Child = glyph };
        tile.SetValue(Border.BackgroundProperty, Resource(selected ? "FlyyLinkAccentBrush" : "FlyyLinkTintBrush"));

        var row = new Button
        {
            Style = (Style)Resource("FlyyLinkPlainButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    tile,
                    new TextBlock { Text = title, FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        if (selected) row.SetValue(Control.BackgroundProperty, Resource("FlyyLinkTintBrush"));
        row.Click += (_, _) =>
        {
            section = item;
            Render();
        };

        return row;
    }

    // Pages

    private IEnumerable<UIElement> General()
    {
        var switcher = new ComboBox { MinWidth = 150 };
        switcher.Items.Add("Card carousel");
        switcher.Items.Add("Dropdown");
        switcher.SelectedIndex = model.SwitcherStyle == PhoneSwitcherStyle.Carousel ? 0 : 1;
        switcher.SelectionChanged += (_, _) => model.SwitcherStyle = switcher.SelectedIndex == 0 ? PhoneSwitcherStyle.Carousel : PhoneSwitcherStyle.Menu;

        yield return Group(null,
            Row(Lucide.Zap, "Start with Windows", null, Switch(model.StartsWithWindows, on => model.StartsWithWindows = on)),
            Row(Lucide.Images, "Show phone photos in Explorer", null, Switch(model.ShowsPhotosInExplorer, on => model.ShowsPhotosInExplorer = on)),
            Row(Lucide.FolderOpen, "Show phone files in Explorer", null, Switch(model.ShowsPhoneInExplorer, on => model.ShowsPhoneInExplorer = on)),
            Row(Lucide.Smartphone, "Switch devices with", null, switcher));

        var keys = new List<UIElement>();
        var list = new StackPanel { Opacity = model.UsesShortcuts ? 1 : 0.45 };
        keys.Add(Row(Lucide.Keyboard, "Use shortcuts", null, Switch(model.UsesShortcuts, on =>
        {
            model.UsesShortcuts = on;
            list.Opacity = on ? 1 : 0.45;
        })));
        foreach (var shortcut in GlobalShortcuts.All) list.Children.Add(ShortcutRow(shortcut.Title, shortcut.Name));
        keys.Add(list);

        yield return Group("Keyboard shortcuts", [.. keys]);
    }

    private IEnumerable<UIElement> Notifications()
    {
        var rows = new List<UIElement>
        {
            Row(Lucide.Bell, "Show phone notifications", null, Switch(model.MirrorsNotifications, on => model.MirrorsNotifications = on)),
        };
        if (model.CanShareNotifications)
        {
            rows.Add(Row(Lucide.Monitor, "Share this PC's notifications", null, Switch(model.SharesNotifications, on => model.SharesNotifications = on)));
        }
        rows.Add(Row(Lucide.BellRing, "Share phone notifications and calls with my computers", null, Switch(model.RelaysPhones, on => model.RelaysPhones = on)));

        yield return Group(null, [.. rows]);
    }

    private IEnumerable<UIElement> Sharing()
    {
        var choices = new StackPanel();
        foreach (var (mode, label) in AppModel.FlyyDropChoices)
        {
            choices.Children.Add(ChoiceRow(label, model.FlyyDropReceiving == mode, () =>
            {
                model.FlyyDropReceiving = mode;
                Render();
            }));
        }

        yield return Group("FlyyDrop", choices);

        var open = new Button { Content = "Open" };
        open.Click += (_, _) => Explorer.Open(AppPaths.Downloads);

        yield return Group(null,
            Row(Lucide.MousePointer2, "Let my devices control this PC", null, Switch(model.AllowsRemoteInput, on => model.AllowsRemoteInput = on)),
            Row(Lucide.Download, "Received files", "Downloads", open));
    }

    private IEnumerable<UIElement> Devices()
    {
        var paired = model.Devices.Where(d => d.IsPaired).ToList();

        if (paired.Count == 0)
        {
            var radar = new LucideIcon { Icon = Lucide.Radar, Size = 30, HorizontalAlignment = HorizontalAlignment.Center };
            radar.SetValue(LucideIcon.ForegroundProperty, Resource("FlyyLinkAccentBrush"));
            yield return new Border
            {
                Style = (Style)Resource("FlyyLinkCard"),
                Padding = new Thickness(18, 40, 18, 40),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        radar,
                        new TextBlock { Text = "No paired devices", FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
            };
            yield break;
        }

        yield return Group(null, [.. paired.Select(device =>
        {
            var unpair = new Button { Content = "Unpair" };
            unpair.Click += (_, _) => model.Unpair(device.Id);

            return Row(Icon(device), device.Name, Kind(device), unpair);
        })]);
    }

    private IEnumerable<UIElement> About()
    {
        var updates = new Button { Content = "Check for updates", VerticalAlignment = VerticalAlignment.Center };
        updates.Click += (_, _) => model.CheckForUpdates();

        var hero = new Grid { ColumnSpacing = 14 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "FlyyLink", FontSize = 17, FontWeight = FontWeights.Bold },
                Caption($"Version {model.VersionText}"),
            },
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(updates, 2);
        hero.Children.Add(new AppGlyph(52));
        hero.Children.Add(name);
        hero.Children.Add(updates);

        yield return new Border { Style = (Style)Resource("FlyyLinkCard"), Padding = new Thickness(16), Child = hero };

        var rows = new List<UIElement>();
        if (model.Licence.LicensedTo is string owner)
        {
            var manage = new Button { Content = "Manage" };
            manage.Click += (_, _) => LicenceModel.ManageComputers();
            rows.Add(Row(Lucide.CircleCheck, "Licensed to", owner, manage));
        }
        else if (model.Licence.CanSignIn)
        {
            var signIn = new Button { Content = "Sign in" };
            signIn.Click += (_, _) =>
            {
                model.Licence.OpenCard();
                PanelRequested?.Invoke();
            };
            rows.Add(Row(Lucide.Lock, "Licence", null, signIn));
        }
        rows.Add(Row(Lucide.Laptop, "This PC", model.Identity.DeviceName, null));

        yield return Group(null, [.. rows]);
    }

    // Building blocks

    private static object Resource(string key) => Application.Current.Resources[key];

    /// A card of rows under an optional title, with hairlines between the rows.
    private static StackPanel Group(string? title, params UIElement[] rows)
    {
        var card = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) card.Children.Add(Hairline());
            card.Children.Add(rows[i]);
        }

        var group = new StackPanel { Spacing = 8 };
        if (title is not null)
        {
            group.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Style = (Style)Resource("FlyyLinkSectionHeader"), Margin = new Thickness(4, 0, 0, 0) });
        }
        group.Children.Add(new Border { Style = (Style)Resource("FlyyLinkCard"), Padding = new Thickness(0), Child = card });

        return group;
    }

    private static Border Hairline()
    {
        var line = new Border { Height = 1, Margin = new Thickness(56, 0, 0, 0) };
        line.SetValue(Border.BackgroundProperty, Resource("FlyyLinkBorderBrush"));

        return line;
    }

    /// One setting: an icon tile, its name, an optional value under it and its control.
    private static Grid Row(Lucide icon, string title, string? detail, FrameworkElement? control)
    {
        var glyph = new LucideIcon { Icon = icon, Size = 14 };
        glyph.SetValue(LucideIcon.ForegroundProperty, Resource("FlyyLinkAccentBrush"));
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center, Child = glyph };
        tile.SetValue(Border.BackgroundProperty, Resource("FlyyLinkTintBrush"));

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
        if (detail is not null) text.Children.Add(Caption(detail));

        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 10, 14, 10), MinHeight = 52 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        row.Children.Add(tile);
        row.Children.Add(text);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            row.Children.Add(control);
        }

        return row;
    }

    /// One option of a single-choice list, ticked when chosen.
    private static Button ChoiceRow(string title, bool selected, Action choose)
    {
        var tick = new LucideIcon { Icon = Lucide.Check, Size = 13, Visibility = selected ? Visibility.Visible : Visibility.Collapsed };
        tick.SetValue(LucideIcon.ForegroundProperty, Resource("FlyyLinkAccentBrush"));

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(tick, 1);
        content.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(tick);

        var row = new Button
        {
            Style = (Style)Resource("FlyyLinkPlainButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 11, 14, 11),
            CornerRadius = new CornerRadius(0),
            Content = content,
        };
        row.Click += (_, _) => choose();

        return row;
    }

    private static ToggleSwitch Switch(bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);

        return toggle;
    }

    /// A shortcut's name with its keys, each on its own small key.
    private static Grid ShortcutRow(string title, string keys)
    {
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var key in keys.Split('+'))
        {
            var cap = new Border
            {
                MinWidth = 22,
                Height = 22,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock { Text = key, FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
            };
            cap.SetValue(Border.BackgroundProperty, Resource("FlyyLinkThumbBrush"));
            caps.Children.Add(cap);
        }

        var row = new Grid { Padding = new Thickness(14, 7, 14, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(caps, 1);
        row.Children.Add(new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(caps);

        return row;
    }

    private static TextBlock Caption(string text) => new() { Text = text, Style = (Style)Resource("FlyyLinkCaption") };

    private static Lucide Icon(DeviceState device) => device.IsPhone ? Lucide.Smartphone : device.IsTv ? Lucide.Tv : Lucide.Laptop;

    private static string Kind(DeviceState device) => device.IsPhone ? "Phone" : device.IsTv ? "TV" : "Computer";
}
