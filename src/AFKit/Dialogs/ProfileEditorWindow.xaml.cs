using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFKit.Models;
using AFKit.Services;

namespace AFKit.Dialogs;

public partial class ProfileEditorWindow : Window
{
    public static readonly string[] Palette =
    {
        "#5B8DEF", "#7C5CFA", "#D946A8", "#E5534B", "#F08C2E",
        "#E3B341", "#3FB97A", "#22A3B8", "#8A8F98", "#4B5563"
    };

    private static readonly ProfileGroup NoGroup = new() { Id = "", Name = Loc.T("Prof.NoGroup") };

    private readonly Profile _profile;
    private readonly bool _ready;

    public ProfileEditorWindow(Profile profile, IEnumerable<ProfileGroup> groups, bool isNew)
    {
        InitializeComponent();
        _profile = profile;
        Title = isNew ? Loc.T("Prof.NewTitle") : Loc.T("Prof.EditTitle", profile.Name);

        Swatches.ItemsSource = Palette;
        var groupList = new List<ProfileGroup> { NoGroup };
        groupList.AddRange(groups);
        GroupBox.ItemsSource = groupList;
        GroupBox.SelectedItem = groupList.FirstOrDefault(g => g.Id == (profile.GroupId ?? "")) ?? NoGroup;

        NameBox.Text = profile.Name;
        IconBox.Text = profile.Icon;
        ColorBox.Text = profile.Color;
        StartUrlBox.Text = profile.StartUrl;

        _ready = true;
        UpdatePreview();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnPreviewChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (!_ready) return;
        var temp = new Profile { Name = NameBox.Text, Icon = IconBox.Text };
        PreviewGlyph.Text = temp.Glyph;
        if (TryParseColor(ColorBox.Text, out var brush)) PreviewCircle.Fill = brush;
    }

    private void OnSwatchClicked(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is string color) ColorBox.Text = color;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0) { ShowError(Loc.T("Prof.NameRequired")); return; }
        if (!TryParseColor(ColorBox.Text, out _)) { ShowError(Loc.T("Prof.BadColor")); return; }

        var startUrl = StartUrlBox.Text.Trim();

        _profile.Name = name;
        _profile.Icon = IconBox.Text.Trim();
        _profile.Color = ColorBox.Text.Trim();
        _profile.GroupId = GroupBox.SelectedItem is ProfileGroup g && g.Id != "" ? g.Id : null;
        _profile.StartUrl = startUrl.Length == 0 ? "about:blank" : AFKit.Controls.ProfileView.NormalizeUrl(startUrl);

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    public static bool TryParseColor(string text, out Brush brush)
    {
        brush = Brushes.Transparent;
        try
        {
            if (!text.Trim().StartsWith('#')) return false;
            brush = (Brush)new BrushConverter().ConvertFromString(text.Trim())!;
            return true;
        }
        catch { return false; }
    }
}
