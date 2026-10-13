using System.Windows.Controls;

namespace AFKit.Controls;

/// <summary>
/// V2 — strip below the battle in the window (DataContext = <see cref="AFKit.Models.TaskbarCell"/>): session
/// summary (Time, XP/h, Gold/h, Profit/h), the session record and the 5 latest captures, as in the grid.
/// Only shows up after the page has sent something (<see cref="AFKit.Models.TaskbarCell.HasInfo"/>).
/// </summary>
public partial class BattleInfo : UserControl
{
    public BattleInfo()
    {
        InitializeComponent();
    }
}
