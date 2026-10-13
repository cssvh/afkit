using System.Windows.Controls;

namespace AFKit.Controls;

/// <summary>
/// V2 — a battle cell in the Game Boy Advance style (DataContext = <see cref="AFKit.Models.TaskbarCell"/>):
/// enemy at the top right (front view) with its box at the top left; your Pokémon at the bottom left
/// (back view) with its box at the bottom right. Scales to whatever size it gets.
/// </summary>
public partial class BattleCell : UserControl
{
    public BattleCell()
    {
        InitializeComponent();
    }
}
