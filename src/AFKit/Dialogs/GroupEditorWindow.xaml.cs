using System.Windows;
using AFKit.Services;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AFKit.Models;

namespace AFKit.Dialogs;

public partial class GroupEditorWindow : Window
{
    private readonly ProfileGroup _group;
    private string _color;
    private Border? _selectedSwatch;

    public GroupEditorWindow(ProfileGroup group, bool isNew)
    {
        InitializeComponent();
        _group = group;
        _color = group.Color;
        Title = Loc.T(isNew ? "Group.New" : "Group.Edit");
        NameBox.Text = isNew ? "" : group.Name;
        Swatches.ItemsSource = ProfileEditorWindow.Palette;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnSwatchClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string color) return;
        if (_selectedSwatch != null) _selectedSwatch.BorderBrush = Brushes.Transparent;
        border.BorderBrush = (Brush)FindResource("Fg");
        _selectedSwatch = border;
        _color = color;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = Loc.T("Group.NameRequired");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        _group.Name = name;
        _group.Color = _color;
        DialogResult = true;
    }
}
