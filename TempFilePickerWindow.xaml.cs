using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuoBuZiLiaoGuanLi.Services;

namespace GuoBuZiLiaoGuanLi;

public partial class TempFilePickerWindow : Window
{
    private readonly TempStorageService _tempStorage;
    private List<TempStorageFile> _allFiles = new();
    private List<TempStorageFile> _filteredFiles = new();
    private HashSet<string> _selectedIds = new();
    private TempStorageFile _lastSelectedFile;
    public List<TempStorageFile> SelectedFiles { get; private set; } = new();

    public TempFilePickerWindow(TempStorageService tempStorage)
    {
        InitializeComponent();
        _tempStorage = tempStorage;
        LoadFiles();
    }

    private void LoadFiles()
    {
        _allFiles = _tempStorage.GetAllFiles().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string search = SearchBox.Text?.ToLower()?.Trim() ?? "";
        if (string.IsNullOrEmpty(search))
        {
            _filteredFiles = _allFiles.ToList();
        }
        else
        {
            _filteredFiles = _allFiles.Where(f =>
                f.FileName.ToLower().Contains(search)
            ).ToList();
        }

        RenderFiles();
        UpdateSelectionInfo();
        TotalInfo.Text = $"共 {_allFiles.Count} 个文件" + (search != "" ? $"，匹配 {_filteredFiles.Count} 个" : "");
    }

    private void RenderFiles()
    {
        FilesGrid.Children.Clear();

        foreach (var file in _filteredFiles)
        {
            var card = CreateFileCard(file);
            FilesGrid.Children.Add(card);
        }

        if (_filteredFiles.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "暂存区暂无文件\n\n请先到资料暂存中添加文件",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(40)
            };
            Grid.SetColumnSpan(empty, 4);
            FilesGrid.Children.Add(empty);
        }
    }

    private Border CreateFileCard(TempStorageFile file)
    {
        bool isSelected = _selectedIds.Contains(file.Id);

        var border = new Border
        {
            Background = isSelected ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : Brushes.White,
            BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = isSelected ? new Thickness(3) : new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            Cursor = Cursors.Hand,
            Tag = file
        };

        var stack = new StackPanel();

        var imageBorder = new Border
        {
            Height = 140,
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 252)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = GetPreviewForFile(file)
        };
        stack.Children.Add(imageBorder);

        var nameText = new TextBlock
        {
            Text = file.FileName,
            FontSize = 12,
            FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32,
            TextWrapping = TextWrapping.NoWrap
        };
        stack.Children.Add(nameText);

        var timeText = new TextBlock
        {
            Text = file.AddedTime.ToString("MM-dd HH:mm"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(timeText);

        border.Child = stack;

        border.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                PreviewFile(file);
                e.Handled = true;
                return;
            }
            HandleFileClick(file, Keyboard.Modifiers);
            e.Handled = true;
        };

        border.PreviewMouseRightButtonDown += (s, e) =>
        {
            if (!_selectedIds.Contains(file.Id))
            {
                _selectedIds.Clear();
                _selectedIds.Add(file.Id);
                UpdateSelectionVisual();
                UpdateSelectionInfo();
            }
            e.Handled = true;
        };

        return border;
    }

    private UIElement GetPreviewForFile(TempStorageFile file)
    {
        try
        {
            string ext = file.Extension?.ToLower() ?? "";
            if (new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" }.Contains(ext))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(file.FilePath);
                bitmap.DecodePixelWidth = 200;
                bitmap.EndInit();
                bitmap.Freeze();

                return new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4)
                };
            }
            else if (ext == ".pdf")
            {
                return new TextBlock
                {
                    Text = "📄 PDF",
                    FontSize = 28,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        catch { }

        return new TextBlock
        {
            Text = "📁",
            FontSize = 36,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void HandleFileClick(TempStorageFile file, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;

        if (shift && _lastSelectedFile != null)
        {
            int lastIdx = _filteredFiles.IndexOf(_lastSelectedFile);
            int curIdx = _filteredFiles.IndexOf(file);
            if (lastIdx >= 0 && curIdx >= 0)
            {
                int start = Math.Min(lastIdx, curIdx);
                int end = Math.Max(lastIdx, curIdx);
                for (int i = start; i <= end; i++)
                {
                    _selectedIds.Add(_filteredFiles[i].Id);
                }
            }
        }
        else if (ctrl)
        {
            if (_selectedIds.Contains(file.Id))
                _selectedIds.Remove(file.Id);
            else
                _selectedIds.Add(file.Id);
        }
        else
        {
            _selectedIds.Clear();
            _selectedIds.Add(file.Id);
        }

        _lastSelectedFile = file;
        UpdateSelectionVisual();
        UpdateSelectionInfo();
    }

    private void UpdateSelectionVisual()
    {
        foreach (var child in FilesGrid.Children)
        {
            if (child is Border border && border.Tag is TempStorageFile file)
            {
                bool isSelected = _selectedIds.Contains(file.Id);
                border.Background = isSelected ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : Brushes.White;
                border.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : new SolidColorBrush(Color.FromRgb(226, 232, 240));
                border.BorderThickness = isSelected ? new Thickness(3) : new Thickness(1);

                if (border.Child is StackPanel sp)
                {
                    var nameText = sp.Children.OfType<TextBlock>().FirstOrDefault();
                    if (nameText != null)
                        nameText.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }
    }

    private void UpdateSelectionInfo()
    {
        SelectionInfo.Text = $"已选择 {_selectedIds.Count} 个文件";
        ImportButton.IsEnabled = _selectedIds.Count > 0;
    }

    private void PreviewFile(TempStorageFile file)
    {
        if (ImagePreviewHelper.CanOpen())
        {
            ImagePreviewHelper.Show(this, file.FilePath);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = SearchBox.Text ?? "";
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (IsLoaded) ApplyFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds = new HashSet<string>(_filteredFiles.Select(f => f.Id));
        UpdateSelectionVisual();
        UpdateSelectionInfo();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds.Clear();
        UpdateSelectionVisual();
        UpdateSelectionInfo();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedFiles = _filteredFiles
            .Where(f => _selectedIds.Contains(f.Id))
            .ToList();
        DialogResult = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SelectAllButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
