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
using Microsoft.Win32;

namespace GuoBuZiLiaoGuanLi;

public partial class TempStorageWindow : Window
{
    private readonly TempStorageService _tempStorage;
    private readonly IScannerService _scannerService;
    private TempStorageFile _selectedFile;
    private bool _isRenaming;

    public TempStorageWindow(TempStorageService tempStorage, IScannerService scannerService)
    {
        InitializeComponent();
        _tempStorage = tempStorage;
        _scannerService = scannerService;
        _tempStorage.FilesChanged += (s, e) => RefreshFileList();
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        FileListPanel.Children.Clear();
        var files = _tempStorage.GetAllFiles().ToList();
        FileCountText.Text = $"共 {files.Count} 个文件";

        foreach (var file in files)
        {
            var btn = new Button
            {
                Content = file.FileName,
                Tag = file,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            btn.Click += FileListItem_Click;
            FileListPanel.Children.Add(btn);
        }

        if (_selectedFile != null)
        {
            SelectFile(_selectedFile.Id);
        }
        else if (files.Count > 0)
        {
            SelectFile(files[0].Id);
        }
        else
        {
            ShowDropHint();
        }
    }

    private void SelectFile(string fileId)
    {
        // 切换文件前，先保存当前文件的修改
        SaveCurrentRemark();

        var files = _tempStorage.GetAllFiles().ToList();
        var file = files.FirstOrDefault(f => f.Id == fileId);
        _selectedFile = file;

        foreach (var child in FileListPanel.Children)
        {
            if (child is Button btn && btn.Tag is TempStorageFile f)
            {
                btn.Background = f.Id == fileId ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : Brushes.Transparent;
                btn.FontWeight = f.Id == fileId ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        if (file != null)
        {
            ShowFilePreview(file);
        }
    }

    private void FileListItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TempStorageFile file)
        {
            SelectFile(file.Id);
        }
    }

    private void ShowDropHint()
    {
        DropHint.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowFilePreview(TempStorageFile file)
    {
        DropHint.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Visible;

        FileNameText.Text = file.FileName;
        
        _isRenaming = true;
        RemarkTextBox.Text = Path.GetFileNameWithoutExtension(file.FileName);
        _isRenaming = false;

        try
        {
            string ext = file.Extension?.ToLower() ?? "";
            if (new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" }.Contains(ext))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(file.FilePath);
                bitmap.DecodePixelWidth = 800;
                bitmap.EndInit();
                bitmap.Freeze();

                var img = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 450,
                    Cursor = Cursors.Hand
                };
                img.MouseLeftButtonUp += (s, args) =>
                {
                    if (ImagePreviewHelper.CanOpen())
                    {
                        ImagePreviewHelper.Show(this, file.FilePath);
                    }
                };

                PreviewContent.Content = img;
            }
            else if (ext == ".pdf")
            {
                var pdfPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                pdfPanel.Children.Add(new TextBlock
                {
                    Text = "📄",
                    FontSize = 80,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                pdfPanel.Children.Add(new TextBlock
                {
                    Text = "PDF 文档\n双击在默认程序中打开",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0)
                });
                pdfPanel.MouseLeftButtonUp += (s, args) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = file.FilePath,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                };
                PreviewContent.Content = pdfPanel;
            }
            else
            {
                PreviewContent.Content = new TextBlock
                {
                    Text = "📁 文件",
                    FontSize = 48,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                };
            }
        }
        catch
        {
            PreviewContent.Content = new TextBlock
            {
                Text = "无法预览",
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192))
            };
        }
    }

    private void DropBorder_PreviewDragOver(object sender, DragEventArgs e)
    {
        bool canAccept = e.Data.GetDataPresent(DataFormats.FileDrop) ||
                         e.Data.GetDataPresent(DataFormats.Bitmap) ||
                         e.Data.GetDataPresent("FileGroupDescriptor") ||
                         e.Data.GetDataPresent("FileGroupDescriptorW");

        e.Effects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (canAccept)
        {
            DropBorder.Background = new SolidColorBrush(Color.FromRgb(219, 234, 254));
        }
    }

    private void DropBorder_PreviewDragLeave(object sender, DragEventArgs e)
    {
        DropBorder.Background = new SolidColorBrush(Color.FromRgb(247, 250, 252));
        e.Handled = true;
    }

    private void DropBorder_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        DropBorder.Background = new SolidColorBrush(Color.FromRgb(247, 250, 252));

        // 拖入新文件前，先保存当前选中文件的修改
        SaveCurrentRemark();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var validFiles = files.Where(f => IsSupportedFile(f)).ToList();
                var added = _tempStorage.AddFiles(validFiles);
                if (added.Count > 0)
                {
                    SelectFile(added[0].Id);
                    System.Media.SystemSounds.Asterisk.Play();
                }
                var invalid = files.Except(validFiles).Select(Path.GetFileName).ToList();
                if (invalid.Count > 0)
                {
                    MessageBox.Show($"部分文件格式不支持:\n{string.Join("\n", invalid.Take(5))}\n\n支持: jpg, jpeg, png, bmp, gif, tiff, pdf",
                        "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private bool IsSupportedFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".pdf" }.Contains(ext);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanButton.IsEnabled = false;
            ScanButton.Content = "扫描中...";

            bool available = await _scannerService.IsScannerAvailableAsync();
            if (!available)
            {
                var result = MessageBox.Show("未检测到扫描仪。是否改为从文件选择？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    AddFilesButton_Click(sender, e);
                }
                return;
            }

            string tempPath = _tempStorage.GetStoragePath();
            string tempName = $"scan_{DateTime.Now:yyyyMMdd_HHmmss}";
            string scannedFile = await _scannerService.ScanToFileAsync(tempPath, tempName);

            if (!string.IsNullOrEmpty(scannedFile) && File.Exists(scannedFile))
            {
                var added = _tempStorage.AddScannedFile(scannedFile, Path.GetFileName(scannedFile));
                SelectFile(added.Id);
                System.Media.SystemSounds.Asterisk.Play();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"扫描出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanButton.Content = "📷 扫描添加";
        }
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片和PDF|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.pdf|所有文件|*.*",
            Title = "选择文件添加到暂存",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            var added = _tempStorage.AddFiles(dialog.FileNames);
            if (added.Count > 0)
            {
                SelectFile(added[0].Id);
                System.Media.SystemSounds.Asterisk.Play();
            }
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string storagePath = _tempStorage.GetStoragePath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = storagePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要清空暂存区所有文件吗？此操作不可恢复。", "确认",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _tempStorage.ClearAll();
            _selectedFile = null;
            ShowDropHint();
        }
    }

    private void SaveCurrentRemark()
    {
        if (_isRenaming || _selectedFile == null)
            return;

        string newName = RemarkTextBox.Text?.Trim();
        string currentNameWithoutExt = Path.GetFileNameWithoutExtension(_selectedFile.FileName);

        if (string.Equals(newName, currentNameWithoutExt, StringComparison.Ordinal))
            return;

        _isRenaming = true;
        _tempStorage.RenameFile(_selectedFile.Id, newName);
        _isRenaming = false;
    }

    private void RemarkTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveCurrentRemark();
    }

    private void RemarkTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveCurrentRemark();
            RemarkTextBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void DeleteFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFile != null)
        {
            _tempStorage.DeleteFile(_selectedFile.Id);
            _selectedFile = null;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
