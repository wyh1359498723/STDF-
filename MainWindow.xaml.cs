using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StdfAnalyzer.ViewModels;

namespace StdfAnalyzer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // ListBox drag-reorder state
    private Point _dragStartPoint;
    private int _dragFromIndex = -1;
    private bool _isDraggingInList;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentResult) && _vm.CurrentResult?.IsSuccess == true)
            {
                Dispatcher.Invoke(() =>
                {
                    WaferMap.Parts = _vm.CurrentResult.Parts;
                    WaferMap.InvalidateVisual();
                    EndianText.Text = _vm.CurrentResult.FileInfo.IsLittleEndian ? "Little Endian" : "Big Endian";
                });
            }
        };
    }

    #region Window-level Drop: Add files to queue

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isDraggingInList) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            _vm.AddFilesToQueue(files);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (_isDraggingInList)
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    #endregion

    #region ListBox Drag-Reorder

    private void FileQueue_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(FileQueueList);
        var item = FindListBoxItemUnderMouse(e);
        _dragFromIndex = item != null ? FileQueueList.ItemContainerGenerator.IndexFromContainer(item) : -1;
    }

    private void FileQueue_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragFromIndex < 0)
            return;

        var pos = e.GetPosition(FileQueueList);
        var diff = pos - _dragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Avoid triggering when clicking buttons inside the item
        if (e.OriginalSource is FrameworkElement fe && IsInsideButton(fe))
            return;

        _isDraggingInList = true;
        var data = new DataObject("FileQueueReorder", _dragFromIndex);
        DragDrop.DoDragDrop(FileQueueList, data, DragDropEffects.Move);
        _isDraggingInList = false;
        _dragFromIndex = -1;
    }

    private void FileQueue_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("FileQueueReorder"))
        {
            // External file drop onto the list area — allow it
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void FileQueue_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("FileQueueReorder"))
        {
            int fromIndex = (int)e.Data.GetData("FileQueueReorder")!;
            int toIndex = GetDropIndex(e);

            if (toIndex >= 0)
                _vm.MoveFile(fromIndex, toIndex);

            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            _vm.AddFilesToQueue(files);
            e.Handled = true;
        }
    }

    private int GetDropIndex(DragEventArgs e)
    {
        for (int i = 0; i < FileQueueList.Items.Count; i++)
        {
            var container = FileQueueList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (container == null) continue;

            var pos = e.GetPosition(container);
            if (pos.Y < container.ActualHeight / 2)
                return i;
        }
        return FileQueueList.Items.Count - 1;
    }

    private ListBoxItem? FindListBoxItemUnderMouse(MouseButtonEventArgs e)
    {
        var hit = VisualTreeHelper.HitTest(FileQueueList, e.GetPosition(FileQueueList));
        var dep = hit?.VisualHit;
        while (dep != null && dep is not ListBoxItem)
            dep = VisualTreeHelper.GetParent(dep);
        return dep as ListBoxItem;
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        var dep = element;
        while (dep != null)
        {
            if (dep is Button) return true;
            dep = VisualTreeHelper.GetParent(dep);
        }
        return false;
    }

    #endregion

    #region Toolbar Buttons

    private void BtnAddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "STDF 文件|*.std;*.stdf|所有文件|*.*",
            Title = "选择 STDF 文件（可多选）",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _vm.AddFilesToQueue(dialog.FileNames);
        }
    }

    private void BtnBackToQueue_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearResults();
        WaferMap.Parts = null;
        WaferMap.InvalidateVisual();
    }

    private void BtnClearAll_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearResults();
        _vm.ClearQueue();
        WaferMap.Parts = null;
        WaferMap.InvalidateVisual();
    }

    #endregion
}
