using FlyyLink.Model;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Text;

namespace FlyyLink.Views;

/// The phone's text conversations, in their own window. Closing it only hides it.
public sealed partial class MessagesWindow : Window
{
    private readonly AppModel model;
    private bool selecting;
    private long? lastBubble;

    public MessagesWindow(AppModel model)
    {
        this.model = model;
        Store = model.Messages;
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.SetIcon(AppPaths.WindowIcon);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(980, 680));
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            AppWindow.Hide();
        };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 480;
        }

        Store.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MessagesStore.Shown):
                    Reselect();
                    break;
                case nameof(MessagesStore.Thread):
                    DispatcherQueue.TryEnqueue(ScrollToNewest);
                    break;
            }
        };
    }

    public MessagesStore Store { get; }

    /// Brings the window forward, starting a text to `number` when one is given, or opening the dialer.
    public void Present(string? number = null, bool dial = false)
    {
        AppWindow.Show(true);
        Activate();

        if (Store.IsEmpty) Store.Refresh();
        if (number is not null) WhenLoaded(() => _ = ComposeAsync(number));
        else if (dial) WhenLoaded(() => _ = DialAsync());
    }

    /// Dialogs need the window's content on screen, which a new window doesn't have yet.
    private void WhenLoaded(Action action)
    {
        var root = (FrameworkElement)Content;
        if (root.XamlRoot is not null && root.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(() => action());
            return;
        }

        void Loaded(object sender, RoutedEventArgs e)
        {
            root.Loaded -= Loaded;
            action();
        }

        root.Loaded += Loaded;
    }

    // Bound helpers

    /// Function bindings can't turn a bool into Visibility themselves, so this returns it directly.
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static FontWeight Weight(bool unread) => unread ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold;

    public static HorizontalAlignment Side(bool mine) => mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public static double Fade(bool pending) => pending ? 0.6 : 1;

    public static Brush BubbleBrush(bool mine) => (Brush)Application.Current.Resources[mine ? "FlyyLinkAccentBrush" : "FlyyLinkThumbBrush"];

    public static Brush BubbleText(bool mine) => (Brush)Application.Current.Resources[mine ? "FlyyLinkOnAccentBrush" : "FlyyLinkTextBrush"];

    public static ImageSource? Picture(string? base64) => Pictures.FromBase64(base64);

    public string SelectedTitle(Conversation? conversation) => conversation?.Title ?? "";

    public string SelectedNumbers(Conversation? conversation) => conversation is null || conversation.Numbers == conversation.Title ? "" : conversation.Numbers;

    // Conversations

    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selecting || ConversationList.SelectedItem is not Conversation conversation) return;

        lastBubble = null;
        Store.Open(conversation.Id);
        Reply.Focus(FocusState.Programmatic);
    }

    /// The list gets a new set of items whenever a text arrives, so select the open conversation again.
    private void Reselect()
    {
        selecting = true;
        ConversationList.SelectedItem = Store.Shown.FirstOrDefault(c => c.Id == Store.Selected?.Id);
        selecting = false;
    }

    private void ScrollToNewest()
    {
        var thread = Store.Thread;
        if (thread.Count == 0 || thread[^1].Key == lastBubble) return;

        lastBubble = thread[^1].Key;
        ThreadList.UpdateLayout();
        ThreadList.ScrollIntoView(thread[^1], ScrollIntoViewAlignment.Leading);
    }

    private void LoadEarlier_Click(object sender, RoutedEventArgs e) => Store.LoadEarlier();

    // Sending

    private void Send_Click(object sender, RoutedEventArgs e) => SendReply();

    private void Reply_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        e.Handled = true;
        SendReply();
    }

    private void SendReply()
    {
        if (Reply.Text.Trim().Length == 0) return;

        Store.Reply(Reply.Text);
        Reply.Text = "";
    }

    private void Compose_Click(object sender, RoutedEventArgs e) => _ = ComposeAsync(null);

    private async Task ComposeAsync(string? number)
    {
        var to = new ContactPicker(Store.Contacts, "To", number ?? "");
        var body = new TextBox { Header = "Message", PlaceholderText = "Text message", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
        var dialog = Dialog("New message", "Send", new StackPanel { Spacing = 12, Width = 320, Children = { to.Box, body } });
        dialog.Opened += (_, _) => (number is null ? (Control)to.Box : body).Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !Store.Compose(to.Number, body.Text))
            await Dialog("Not sent", "OK", new TextBlock { Text = "Check the number, message and phone connection.", TextWrapping = TextWrapping.Wrap }, withCancel: false).ShowAsync();
    }

    // Calling

    private void Dial_Click(object sender, RoutedEventArgs e) => _ = DialAsync();

    private void CallSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Store.Selected is { Addresses.Count: > 0 } conversation) model.Dial(conversation.Addresses[0].Address, conversation.Addresses[0].Name);
    }

    private async Task DialAsync()
    {
        var number = new ContactPicker(Store.Contacts, fontSize: 18);
        var dialog = Dialog("Call from your phone", "Call", new StackPanel { Spacing = 8, Width = 300, Children = { number.Box } });
        dialog.Opened += (_, _) => number.Box.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && number.Number.Length > 0) model.Dial(number.Number, number.Name);
    }

    private ContentDialog Dialog(string title, string action, UIElement content, bool withCancel = true) => new()
    {
        XamlRoot = Content.XamlRoot,
        Title = title,
        Content = content,
        PrimaryButtonText = action,
        CloseButtonText = withCancel ? "Cancel" : "",
        DefaultButton = ContentDialogButton.Primary,
        RequestedTheme = ((FrameworkElement)Content).ActualTheme,
    };
}
