using FlyyLink.Core;
using Microsoft.UI.Xaml.Controls;

namespace FlyyLink.Views;

/// A number box that suggests the phone's contacts as you type a name or number.
internal sealed class ContactPicker
{
    private sealed record Suggestion(string Name, string Number)
    {
        public override string ToString() => $"{Name}  {Number}";
    }

    private readonly IReadOnlyList<PhoneContact> contacts;

    public ContactPicker(IReadOnlyList<PhoneContact> contacts, string? header = null, string text = "", double fontSize = 14)
    {
        this.contacts = contacts;

        Box = new AutoSuggestBox
        {
            Header = header,
            PlaceholderText = contacts.Count == 0 ? "Phone number" : "Name or number",
            Text = text,
            FontSize = fontSize,
            UpdateTextOnSelect = false,
        };
        Box.TextChanged += TextChanged;
        Box.SuggestionChosen += (_, args) => Pick((Suggestion)args.SelectedItem);
    }

    public AutoSuggestBox Box { get; }

    public string Number => Box.Text.Trim();

    /// The contact's name when the number was picked from the suggestions.
    public string? Name { get; private set; }

    private void TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        Name = null;

        var query = sender.Text.Trim();
        sender.ItemsSource = query.Length == 0 ? null : PhoneContacts.Search(contacts, query)
            .SelectMany(c => c.Numbers.Select(n => new Suggestion(c.Name, n)))
            .Take(6)
            .ToList();
    }

    private void Pick(Suggestion suggestion)
    {
        Box.Text = suggestion.Number;
        Name = suggestion.Name;
        Box.ItemsSource = null;
    }
}
