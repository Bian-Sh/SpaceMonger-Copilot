using System.Windows;
using SpaceMonger.App.Converters;
using SpaceMonger.Core.Enums;

namespace SpaceMonger.App.Views;

public partial class CleanupConfirmDialog : Window
{
    /// <summary>
    /// Gets the total number of items to be cleaned up.
    /// </summary>
    public int TotalItems { get; private set; }

    /// <summary>
    /// Gets the formatted total space to be freed.
    /// </summary>
    public string TotalSpace { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the deletion mode selected by the user.
    /// </summary>
    public DeletionMode SelectedMode { get; private set; } = DeletionMode.MoveToRecycleBin;

    public CleanupConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures the dialog with cleanup summary information before showing it.
    /// </summary>
    /// <param name="totalItems">The number of items to be cleaned up.</param>
    /// <param name="totalSizeBytes">The total size in bytes to be freed.</param>
    public void SetCleanupInfo(int totalItems, long totalSizeBytes)
    {
        TotalItems = totalItems;
        TotalSpace = FileSizeConverter.FormatSize(totalSizeBytes);

        ItemCount.Text = $"Items to remove: {TotalItems}";
        SpaceToFree.Text = $"Space to free: {TotalSpace}";
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DeletionModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (PermanentDeleteRadio == null || PermanentDeleteWarning == null)
        {
            return;
        }

        if (PermanentDeleteRadio.IsChecked == true)
        {
            SelectedMode = DeletionMode.PermanentDelete;
            PermanentDeleteWarning.Visibility = Visibility.Visible;
        }
        else
        {
            SelectedMode = DeletionMode.MoveToRecycleBin;
            PermanentDeleteWarning.Visibility = Visibility.Collapsed;
        }
    }
}
