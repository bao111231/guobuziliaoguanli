using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GuoBuZiLiaoGuanLi.Models;
using GuoBuZiLiaoGuanLi.Services;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace GuoBuZiLiaoGuanLi;

public partial class MainWindow : Window
{
    private readonly IScannerService _scannerService;
    private readonly TempStorageService _tempStorage;
    private HttpServerService _httpServer;
    private string _rootDirectory;
    private List<CustomerFolder> _allCustomerFolders;
    private CustomerFolder _selectedFolder;
    private DocumentItem _selectedDocument;
    private FolderStatus? _currentFilter;
    private string _searchText;
    private bool _isInitialized;
    private int _lastPendingCount;
    private bool _wasDeactivated;
    private bool _isFirstLoad = true;
    private bool _isScanning = false;
    private readonly object _scanLock = new object();

    private static readonly string ADriveExePath = @"C:\Users\bao\AppData\Local\Programs\aDrive\aDrive.exe";
    private Process _aDriveProcess;
    private DispatcherTimer _aDriveTimer;
    private const int ADriveTimeoutMinutes = 3;

    private static readonly string ConfigFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuoBuZiLiaoGuanLi",
        "config.txt");

    private static readonly string DebugLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "dragdrop_debug.log");

    private static void DebugLog(string msg)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}";
            File.AppendAllText(DebugLogPath, line);
        }
        catch { }
    }

    public MainWindow()
    {
        InitializeComponent();
        _scannerService = new WiaScannerService();
        _tempStorage = new TempStorageService();
        _allCustomerFolders = new List<CustomerFolder>();
        _currentFilter = null;
        _searchText = "";
        _isInitialized = true;

        _aDriveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(ADriveTimeoutMinutes) };
        _aDriveTimer.Tick += ADriveTimer_Tick;

        try { File.WriteAllText(DebugLogPath, $"==== 程序启动 {DateTime.Now} ===={Environment.NewLine}"); } catch { }

        InitializeDragDrop();
        LoadSavedRootDirectory();

        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
    }

    private void ADriveTimer_Tick(object sender, EventArgs e)
    {
        _aDriveTimer.Stop();
        KillADriveProcess();
    }

    private void TriggerADrive()
    {
        try
        {
            if (!File.Exists(ADriveExePath))
            {
                return;
            }

            if (_aDriveProcess == null || _aDriveProcess.HasExited)
            {
                // 使用 --openAtLogin 参数伪装成开机自启动，让aDrive静默启动到托盘
                _aDriveProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = ADriveExePath,
                    Arguments = "--openAtLogin",
                    UseShellExecute = true
                });
            }

            _aDriveTimer.Stop();
            _aDriveTimer.Start();
        }
        catch { }
    }

    private void KillADriveProcess()
    {
        try
        {
            if (_aDriveProcess != null && !_aDriveProcess.HasExited)
            {
                _aDriveProcess.Kill(entireProcessTree: true);
                _aDriveProcess.WaitForExit(5000);
            }
        }
        catch { }
        finally
        {
            _aDriveProcess = null;
        }
    }

    private void MainWindow_Deactivated(object sender, EventArgs e)
    {
        _wasDeactivated = true;
    }

    private void MainWindow_Activated(object sender, EventArgs e)
    {
        // 只有曾经失去过焦点后才触发刷新，避免程序启动时多刷新一次
        if (!_wasDeactivated || !_isInitialized)
        {
            return;
        }
        _wasDeactivated = false;

        // 没有设置根目录则无需刷新
        if (string.IsNullOrEmpty(_rootDirectory) || !Directory.Exists(_rootDirectory))
        {
            return;
        }

        // 记录当前选中的文件夹和资料项，刷新后尝试恢复
        string selectedFolderName = _selectedFolder?.OriginalFolderName;
        DocumentType? selectedDocType = _selectedDocument?.DocumentType;

        LoadCustomerFolders();

        // 恢复文件夹选中状态
        if (!string.IsNullOrEmpty(selectedFolderName))
        {
            var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == selectedFolderName);
            if (matchingFolder != null)
            {
                SelectFolder(matchingFolder);

                // 恢复资料项选中状态
                if (selectedDocType.HasValue && matchingFolder.Documents.TryGetValue(selectedDocType.Value, out var doc))
                {
                    SelectDocument(doc);
                }
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text ?? "";
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
        
        if (_isInitialized)
        {
            ApplyFilter();
            UpdateStatusSummary();
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void UpdateClearButtonVisibility()
    {
        ClearSearchButton.Visibility = !string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSavedRootDirectory()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string savedPath = File.ReadAllText(ConfigFilePath).Trim();
                if (Directory.Exists(savedPath))
                {
                    _rootDirectory = savedPath;
                    _tempStorage.SetStorageRoot(_rootDirectory);
                    LoadCustomerFolders();
                    StartHttpServer();
                }
            }
        }
        catch { }
    }

    private void SaveRootDirectory(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(ConfigFilePath, path);
        }
        catch { }
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含客户资料的根目录"
        };

        if (!string.IsNullOrEmpty(_rootDirectory) && Directory.Exists(_rootDirectory))
        {
            dialog.InitialDirectory = _rootDirectory;
        }

        if (dialog.ShowDialog() == true)
        {
            _rootDirectory = dialog.FolderName;
            SaveRootDirectory(_rootDirectory);
            _tempStorage.SetStorageRoot(_rootDirectory);
            LoadCustomerFolders();
            StartHttpServer();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_rootDirectory))
        {
            LoadCustomerFolders();
        }
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || FolderListPanel == null)
            return;

        if (sender is RadioButton rb)
        {
            if (rb == FilterAll)
                _currentFilter = null;
            else if (rb == FilterMissing)
                _currentFilter = FolderStatus.Missing;
            else if (rb == FilterPending)
                _currentFilter = FolderStatus.Pending;
            else if (rb == FilterNotUploaded)
                _currentFilter = FolderStatus.NotUploaded;
            else if (rb == FilterUploadedUnconfirmed)
                _currentFilter = FolderStatus.UploadedUnconfirmed;
            else if (rb == FilterUploaded)
                _currentFilter = FolderStatus.Uploaded;

            ApplyFilter();
        }
    }

    private void LoadCustomerFolders()
    {
        // 记录加载前已存在的文件夹名称，用于检测是否有新文件夹出现
        var oldFolderNames = new HashSet<string>(
            _allCustomerFolders.Select(f => f.OriginalFolderName),
            StringComparer.OrdinalIgnoreCase);

        _allCustomerFolders.Clear();
        FolderListPanel.Children.Clear();

        try
        {
            var directories = Directory.GetDirectories(_rootDirectory);

            foreach (var dir in directories)
            {
                if (string.Equals(Path.GetFileName(dir), "暂存文件", StringComparison.OrdinalIgnoreCase))
                    continue;

                var folder = new CustomerFolder(dir);
                _allCustomerFolders.Add(folder);
            }

            _allCustomerFolders = _allCustomerFolders.OrderByDescending(f => f.CreationTime).ToList();

            ApplyFilter();
            UpdateStatusSummary();

            WelcomePanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;

            // 非首次加载时，检测是否有新文件夹出现，有则触发aDrive
            if (!_isFirstLoad)
            {
                bool hasNewFolder = _allCustomerFolders.Any(f => !oldFolderNames.Contains(f.OriginalFolderName));
                if (hasNewFolder)
                {
                    TriggerADrive();
                }
            }
            _isFirstLoad = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        FolderListPanel.Children.Clear();

        IEnumerable<CustomerFolder> foldersToShow = _allCustomerFolders;

        if (_currentFilter.HasValue)
        {
            foldersToShow = foldersToShow.Where(f => f.Status == _currentFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string search = _searchText.Trim().ToLower();
            foldersToShow = foldersToShow.Where(f => 
                f.DisplayName.ToLower().Contains(search) || 
                f.OriginalFolderName.ToLower().Contains(search) ||
                (f.InvoiceMMDD != null && f.InvoiceMMDD.Contains(search)) ||
                (f.CustomerName != null && f.CustomerName.ToLower().Contains(search)));
        }

        foreach (var folder in foldersToShow)
        {
            CreateFolderCard(folder);
        }

        UpdateClearButtonVisibility();
    }

    private void CreateFolderCard(CustomerFolder folder)
    {
        var border = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(2),
            BorderBrush = folder.Status.GetStatusBrush(),
            Tag = folder,
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel();

        var nameText = new TextBlock
        {
            Text = folder.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        textStack.Children.Add(nameText);

        var timeText = new TextBlock
        {
            Text = folder.HasInvoiceDate
                ? $"📅 {folder.InvoiceMMDD}  |  创建: {folder.CreationTime:MM-dd HH:mm}"
                : $"创建: {folder.CreationTime:yyyy-MM-dd HH:mm}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 174, 192)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        textStack.Children.Add(timeText);

        // 状态圆球 + 文字
        var statusStack = new StackPanel { Orientation = Orientation.Horizontal };
        statusStack.VerticalAlignment = VerticalAlignment.Center;
        statusStack.HorizontalAlignment = HorizontalAlignment.Right;

        var statusCircle = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = folder.Status.GetStatusBrush(),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusText = new TextBlock
        {
            Text = folder.Status.GetDisplayName(),
            Foreground = folder.Status.GetStatusBrush(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        statusStack.Children.Add(statusCircle);
        statusStack.Children.Add(statusText);

        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(statusStack, 1);
        grid.Children.Add(textStack);
        grid.Children.Add(statusStack);

        border.Child = grid;

        border.MouseLeftButtonUp += FolderCard_Click;
        border.MouseEnter += FolderCard_MouseEnter;
        border.MouseLeave += FolderCard_MouseLeave;

        FolderListPanel.Children.Add(border);
    }

    private void FolderCard_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 0.9;
            border.BorderThickness = new Thickness(3);
        }
    }

    private void FolderCard_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
            if (border.Tag == _selectedFolder)
                border.BorderThickness = new Thickness(3);
            else
                border.BorderThickness = new Thickness(2);
        }
    }

    private void FolderCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is CustomerFolder folder)
        {
            SelectFolder(folder);
        }
    }

    private void SelectFolder(CustomerFolder folder)
    {
        _selectedFolder = folder;
        folder.Refresh();
        UpdateFolderSelectionVisual();
        ShowFolderDetails(folder);
    }

    private void UpdateFolderSelectionVisual()
    {
        foreach (var child in FolderListPanel.Children)
        {
            if (child is Border border)
            {
                if (border.Tag == _selectedFolder)
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(235, 245, 255));
                    border.BorderThickness = new Thickness(3);
                }
                else
                {
                    border.Background = Brushes.White;
                    border.BorderThickness = new Thickness(2);
                }
            }
        }
    }

    private void ShowFolderDetails(CustomerFolder folder)
    {
        DetailPanel.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Collapsed;

        FolderNameText.Text = folder.DisplayName;
        if (folder.HasInvoiceDate)
        {
            FolderDateText.Text = $"📅 {folder.InvoiceMMDD}";
            FolderDateText.Visibility = Visibility.Visible;
        }
        else
        {
            FolderDateText.Visibility = Visibility.Collapsed;
        }
        CreationTimeText.Text = $"创建时间: {folder.CreationTime:yyyy-MM-dd HH:mm}";
        UpdateStatusDisplay();

        _selectedDocument = null;
        PreviewDropGrid.AllowDrop = false;
        RenderDocumentItems(folder);
        UpdateActionButtonsVisibility();
        UpdatePreviewPanel(null);
    }

    private void UpdateStatusDisplay()
    {
        if (_selectedFolder != null)
        {
            StatusBadge.Background = _selectedFolder.Status.GetStatusBrush();
            StatusText.Text = _selectedFolder.Status.GetDisplayName();
        }
    }

    private void UpdateActionButtonsVisibility()
    {
        if (_selectedFolder == null) return;

        bool hasAllDocuments = _selectedFolder.Documents.Values.All(d => d.Exists);
        bool hasMissingDocuments = !hasAllDocuments;

        MarkPendingButton.Visibility = Visibility.Collapsed;
        UnmarkPendingButton.Visibility = Visibility.Collapsed;
        MarkCompleteButton.Visibility = Visibility.Collapsed;
        UnmarkCompleteButton.Visibility = Visibility.Collapsed;
        MarkUploadedButton.Visibility = Visibility.Collapsed;
        UnmarkUploadedButton.Visibility = Visibility.Collapsed;

        if (hasMissingDocuments)
        {
            if (_selectedFolder.IsManuallyComplete)
            {
                UnmarkCompleteButton.Visibility = Visibility.Visible;

                if (_selectedFolder.IsUploaded)
                {
                    UnmarkUploadedButton.Visibility = Visibility.Visible;
                }
                else
                {
                    MarkUploadedButton.Visibility = Visibility.Visible;
                    UnmarkUploadedButton.Visibility = Visibility.Collapsed;

                    if (_selectedFolder.IsUploadedUnconfirmed)
                    {
                        MarkUploadedButton.Content = "✓ 确认已上传";
                    }
                    else
                    {
                        MarkUploadedButton.Content = "✓ 标记已上传";
                    }
                }
            }
            else
            {
                MarkCompleteButton.Visibility = Visibility.Visible;

                if (_selectedFolder.IsPending)
                {
                    UnmarkPendingButton.Visibility = Visibility.Visible;
                }
                else
                {
                    MarkPendingButton.Visibility = Visibility.Visible;
                }
            }
        }
        else
        {
            if (_selectedFolder.IsUploaded)
            {
                MarkUploadedButton.Visibility = Visibility.Collapsed;
                UnmarkUploadedButton.Visibility = Visibility.Visible;
            }
            else
            {
                MarkUploadedButton.Visibility = Visibility.Visible;
                UnmarkUploadedButton.Visibility = Visibility.Collapsed;

                if (_selectedFolder.IsUploadedUnconfirmed)
                {
                    MarkUploadedButton.Content = "✓ 确认已上传";
                }
                else
                {
                    MarkUploadedButton.Content = "✓ 标记已上传";
                }
            }
        }
    }

    private void MarkUploadedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            string originalName = _selectedFolder.OriginalFolderName;
            bool isUnconfirmed = _selectedFolder.IsUploadedUnconfirmed;
            
            string confirmMessage = isUnconfirmed 
                ? "此文件夹已被其他程序标记为已上传，是否在本系统中确认？" 
                : "确定要标记此文件夹为已上传吗？";
            
            var result = MessageBox.Show(confirmMessage, "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _selectedFolder.MarkAsUploaded();
                LoadCustomerFolders();
                
                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == originalName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
                
                string successMessage = isUnconfirmed 
                    ? "已确认上传完成！" 
                    : "已标记为上传完成！文件夹名称已添加\"已上传\"标记。";
                MessageBox.Show(successMessage, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void UnmarkUploadedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            string originalName = _selectedFolder.OriginalFolderName;
            var result = MessageBox.Show("确定要取消已上传标记吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _selectedFolder.UnmarkAsUploaded();
                LoadCustomerFolders();
                
                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == originalName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
            }
        }
    }

    private void MarkPendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            var result = MessageBox.Show("确定要标记此文件夹为暂滞状态吗？\n暂滞表示资料暂时不全，后续补充后会自动取消。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _selectedFolder.MarkAsPending();
                LoadCustomerFolders();
                
                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == _selectedFolder.OriginalFolderName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
            }
        }
    }

    private void UnmarkPendingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            var result = MessageBox.Show("确定要取消暂滞状态吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _selectedFolder.UnmarkAsPending();
                LoadCustomerFolders();
                
                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == _selectedFolder.OriginalFolderName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
            }
        }
    }

    private void MarkCompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            var result = MessageBox.Show("此文件夹资料实际不全，确定要手动标记为资料齐全吗？\n标记后文件夹将显示为蓝色（未上传）状态。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                string originalName = _selectedFolder.OriginalFolderName;
                _selectedFolder.MarkAsComplete();
                LoadCustomerFolders();

                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == originalName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
            }
        }
    }

    private void UnmarkCompleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null)
        {
            var result = MessageBox.Show("确定要取消资料齐全标记吗？\n取消后文件夹将恢复为缺资料（红色）状态。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                string originalName = _selectedFolder.OriginalFolderName;
                _selectedFolder.UnmarkAsComplete();
                LoadCustomerFolders();

                var matchingFolder = _allCustomerFolders.FirstOrDefault(f => f.OriginalFolderName == originalName);
                if (matchingFolder != null)
                {
                    SelectFolder(matchingFolder);
                }
            }
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder != null && Directory.Exists(_selectedFolder.FolderPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _selectedFolder.FolderPath,
                UseShellExecute = true
            });
        }
    }

    private void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder == null || !Directory.Exists(_selectedFolder.FolderPath))
        {
            return;
        }

        string folderName = _selectedFolder.OriginalFolderName;
        var result = MessageBox.Show(
            $"确定要删除此文件夹吗？此操作不可恢复！\n\n" +
            $"文件夹: {_selectedFolder.DisplayName}\n" +
            $"路径: {_selectedFolder.FolderPath}",
            "确认删除文件夹",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Directory.Delete(_selectedFolder.FolderPath, recursive: true);
            _selectedFolder = null;
            _selectedDocument = null;
            LoadCustomerFolders();
            DetailPanel.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Collapsed;
            MessageBox.Show($"文件夹 \"{folderName}\" 已删除。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RenderDocumentItems(CustomerFolder folder)
    {
        DocumentItemsPanel.Children.Clear();

        foreach (var docType in folder.Documents.Keys)
        {
            var document = folder.Documents[docType];
            CreateDocumentItem(document);
        }
    }

    private void CreateDocumentItem(DocumentItem document)
    {
        var docBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 252)),
            Tag = document,
            Cursor = Cursors.Hand
        };

        var grid = new Grid
        {
            Background = Brushes.Transparent
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusEllipse = new Ellipse
        {
            Width = 28,
            Height = 28,
            Fill = document.Exists ? new SolidColorBrush(Color.FromRgb(40, 167, 69)) : new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var checkText = new TextBlock
        {
            Text = document.Exists ? "✓" : "✗",
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(checkText, 0);

        var nameStack = new StackPanel();
        Grid.SetColumn(nameStack, 1);
        nameStack.VerticalAlignment = VerticalAlignment.Center;

        string countText = document.FileCount > 1 ? $" ({document.FileCount}张)" : "";
        var docNameText = new TextBlock
        {
            Text = document.DisplayName + countText + $" [{document.FileNamePrefix}*]",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
        };
        nameStack.Children.Add(docNameText);

        if (document.Exists && document.AllFiles.Count > 0)
        {
            var fileNameText = new TextBlock
            {
                Text = string.Join(", ", document.AllFiles.Take(2).Select(f => Path.GetFileName(f))) +
                       (document.AllFiles.Count > 2 ? " ..." : ""),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(113, 128, 150)),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nameStack.Children.Add(fileNameText);
        }

        var buttonStack = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        Grid.SetColumn(buttonStack, 2);

        var scanButton = new Button
        {
            Content = "📷 扫描",
            Background = new SolidColorBrush(Color.FromRgb(40, 167, 69)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = document
        };
        scanButton.Click += ScanDocument_Click;

        var importButton = new Button
        {
            Content = "📦 从暂存导入",
            Background = new SolidColorBrush(Color.FromRgb(23, 162, 184)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = document
        };
        importButton.Click += ImportDocument_Click;

        var moveToTempButton = new Button
        {
            Content = "📦 移到暂存",
            Background = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = document,
            ToolTip = "将文件移动到暂存区",
            Visibility = document.Exists ? Visibility.Visible : Visibility.Collapsed
        };
        moveToTempButton.Click += MoveToTempStorage_Click;

        var deleteButton = new Button
        {
            Content = "🗑️",
            Background = new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 12,
            Tag = document,
            ToolTip = "删除"
        };
        deleteButton.Click += DeleteDocument_Click;

        buttonStack.Children.Add(scanButton);
        buttonStack.Children.Add(importButton);
        buttonStack.Children.Add(moveToTempButton);
        buttonStack.Children.Add(deleteButton);

        grid.Children.Add(statusEllipse);
        Grid.SetColumn(statusEllipse, 0);
        grid.Children.Add(checkText);
        grid.Children.Add(nameStack);
        grid.Children.Add(buttonStack);

        docBorder.Child = grid;
        docBorder.MouseLeftButtonUp += DocumentItem_Click;
        docBorder.MouseEnter += DocItem_MouseEnter;
        docBorder.MouseLeave += DocItem_MouseLeave;

        DocumentItemsPanel.Children.Add(docBorder);
    }

    private void InitializeDragDrop()
    {
        // 默认不允许拖入，只有在 SelectDocument 中才会启用 AllowDrop
        PreviewDropGrid.AllowDrop = false;
    }

    private void PreviewDropGrid_PreviewDragEnter(object sender, DragEventArgs e)
    {
        LogDragData("PreviewDragEnter", e);
        PreviewDropGrid_PreviewDragOver(sender, e);
    }

    private void PreviewDropGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        // 微信拖拽的图片可能不是 FileDrop 格式，需要识别多种格式
        bool canAccept = _selectedDocument != null && (
            e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetDataPresent(DataFormats.Bitmap) ||
            e.Data.GetDataPresent("FileGroupDescriptor") ||
            e.Data.GetDataPresent("FileGroupDescriptorW") ||
            e.Data.GetDataPresent("CF_DIB") ||
            e.Data.GetDataPresent(DataFormats.Dib)
        );

        if (canAccept)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void PreviewDropGrid_PreviewDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// 列出拖拽时 DataObject 中所有可用的数据格式，方便调试微信等来源的拖拽数据
    /// </summary>
    private void LogDragData(string stage, DragEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"---- {stage} ----");
            sb.AppendLine($"  SelectedDocument = {_selectedDocument?.DocumentType}");

            string[] formats = e.Data.GetFormats();
            sb.AppendLine($"  GetFormats() 共 {formats.Length} 个:");
            foreach (var fmt in formats)
            {
                bool present = e.Data.GetDataPresent(fmt);
                object data = null;
                try { data = e.Data.GetData(fmt); } catch (Exception ex) { data = $"[GetData异常: {ex.Message}]"; }

                string typeDesc = data?.GetType().FullName ?? "null";
                string valueDesc;
                if (data == null) valueDesc = "null";
                else if (data is string[] arr) valueDesc = $"string[{arr.Length}] = [{string.Join(", ", arr.Take(3))}{(arr.Length > 3 ? ", ..." : "")}]";
                else if (data is string s) valueDesc = $"string = \"{s}\"";
                else if (data is System.IO.MemoryStream ms)
                {
                    valueDesc = $"MemoryStream, 长度={ms.Length}, 头16字节={BitConverter.ToString(ms.ToArray().Take(16).ToArray())}";
                }
                else if (data is System.Windows.Media.Imaging.BitmapSource bs) valueDesc = $"BitmapSource {bs.PixelWidth}x{bs.PixelHeight}";
                else valueDesc = data.ToString();

                sb.AppendLine($"    [{fmt}] present={present} type={typeDesc} value={valueDesc}");
            }
            DebugLog(sb.ToString());
        }
        catch (Exception ex)
        {
            DebugLog($"LogDragData 异常: {ex}");
        }
    }

    private void PreviewDropGrid_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        LogDragData("PreviewDrop", e);

        if (_selectedDocument == null)
        {
            MessageBox.Show("请先在上方点击选择要导入的资料类型（如：发票、SN码等），再拖拽图片！",
                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1. 优先处理标准 FileDrop（资源管理器拖文件）
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                ImportFilesFromDragDrop(files);
                return;
            }
        }

        // 2. 处理 FileGroupDescriptor（微信、QQ、Outlook 等拖出虚拟文件时常用）
        if (TryExtractFilesFromDescriptor(e, out var extractedFiles) && extractedFiles.Count > 0)
        {
            ImportFilesFromDragDrop(extractedFiles.ToArray());
            return;
        }

        // 3. 兜底：尝试从 Bitmap 提取图片
        if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            try
            {
                var bmp = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (bmp != null)
                {
                    SaveBitmapToFile(bmp);
                    return;
                }
            }
            catch (Exception ex) { DebugLog($"Bitmap 兜底异常: {ex.Message}"); }
        }

        MessageBox.Show("无法识别此拖拽内容。\n支持：图片文件（jpg/png/bmp等）、微信/资源管理器拖拽的图片。",
            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// 处理 FileGroupDescriptor/FileGroupDescriptorW：微信等应用拖拽图片时，
    /// 文件内容通过 FileContents 格式逐个获取，文件名通过 Descriptor 获取。
    /// </summary>
    private bool TryExtractFilesFromDescriptor(DragEventArgs e, out List<string> savedFiles)
    {
        savedFiles = new List<string>();
        try
        {
            // 微信的 FileGroupDescriptor 通常是内存流
            object descriptor = e.Data.GetData("FileGroupDescriptorW")
                                ?? e.Data.GetData("FileGroupDescriptor")
                                ?? e.Data.GetData("FileGroupDescriptor");

            if (descriptor == null)
            {
                DebugLog("TryExtract: 没有 FileGroupDescriptor 数据");
                return false;
            }

            DebugLog($"TryExtract: descriptor 类型 = {descriptor.GetType().FullName}");

            // 简化处理：直接尝试获取 FileContents（单文件情况，微信拖单张常用此方式）
            object contents = e.Data.GetData("FileContents")
                              ?? e.Data.GetData("FileContents");

            if (contents is System.IO.MemoryStream ms)
            {
                DebugLog($"TryExtract: FileContents 是 MemoryStream, 长度={ms.Length}");
                string savedPath = SaveStreamToImageFile(ms);
                if (!string.IsNullOrEmpty(savedPath)) savedFiles.Add(savedPath);
                return savedFiles.Count > 0;
            }
            else if (contents is System.IO.FileStream fs)
            {
                DebugLog($"TryExtract: FileContents 是 FileStream, 路径={fs.Name}");
                savedFiles.Add(fs.Name);
                return true;
            }
            else if (contents != null)
            {
                DebugLog($"TryExtract: FileContents 类型未知 = {contents.GetType().FullName}");
            }
            else
            {
                DebugLog("TryExtract: 没有 FileContents 数据");
            }
        }
        catch (Exception ex)
        {
            DebugLog($"TryExtract 异常: {ex}");
        }
        return false;
    }

    private string SaveStreamToImageFile(System.IO.MemoryStream ms)
    {
        try
        {
            // 识别图片格式（看头几个字节）
            byte[] data = ms.ToArray();
            string ext = DetectImageExtension(data);
            if (string.IsNullOrEmpty(ext))
            {
                DebugLog($"SaveStream: 无法识别图片格式，前16字节={BitConverter.ToString(data.Take(16).ToArray())}");
                return null;
            }

            string nextFileName = _selectedFolder.GetNextFileName(_selectedDocument.DocumentType);
            string targetPath = Path.Combine(_selectedFolder.FolderPath, nextFileName + ext);
            int counter = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(_selectedFolder.FolderPath, $"{nextFileName}_{counter}{ext}");
                counter++;
            }
            File.WriteAllBytes(targetPath, data);
            DebugLog($"SaveStream: 保存成功 -> {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            DebugLog($"SaveStream 异常: {ex}");
            return null;
        }
    }

    private void SaveBitmapToFile(BitmapSource bmp)
    {
        try
        {
            string nextFileName = _selectedFolder.GetNextFileName(_selectedDocument.DocumentType);
            string targetPath = Path.Combine(_selectedFolder.FolderPath, nextFileName + ".png");
            int counter = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(_selectedFolder.FolderPath, $"{nextFileName}_{counter}.png");
                counter++;
            }

            using var fs = new FileStream(targetPath, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
            DebugLog($"SaveBitmap: 保存成功 -> {targetPath}");

            _selectedFolder.Refresh();
            RenderDocumentItems(_selectedFolder);
            UpdateFolderCard(_selectedFolder);
            UpdateStatusSummary();
            UpdateStatusDisplay();
            UpdateActionButtonsVisibility();
            ApplyFilter();
            foreach (var child in DocumentItemsPanel.Children)
            {
                if (child is Border b && b.Tag is DocumentItem d && d.DocumentType == _selectedDocument.DocumentType)
                {
                    SelectDocument(d);
                    break;
                }
            }
            SystemSounds.Asterisk.Play();
            TriggerADrive();
        }
        catch (Exception ex)
        {
            DebugLog($"SaveBitmap 异常: {ex}");
            MessageBox.Show($"保存图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 根据文件头字节判断图片扩展名
    /// </summary>
    private static string DetectImageExtension(byte[] data)
    {
        if (data == null || data.Length < 4) return null;
        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        // PNG: 89 50 4E 47
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return ".png";
        // GIF: 47 49 46 38
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return ".gif";
        // BMP: 42 4D
        if (data[0] == 0x42 && data[1] == 0x4D) return ".bmp";
        // TIFF: 49 49 2A 00 或 4D 4D 00 2A
        if ((data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) ||
            (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A)) return ".tiff";
        // PDF: 25 50 44 46
        if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return ".pdf";
        return null;
    }

    /// <summary>
    /// 共享的拖拽导入逻辑：被 WPF OLE 拖拽（PreviewDrop）和 Win32 WM_DROPFILES 共同复用。
    /// </summary>
    private void ImportFilesFromDragDrop(string[] files)
    {
        if (_selectedDocument == null || _selectedFolder == null || files == null || files.Length == 0)
        {
            MessageBox.Show("请先在上方点击选择要导入的资料类型（如：发票、SN码等），再拖拽图片！",
                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        List<string> invalidFiles = new List<string>();
        int successCount = 0;

        foreach (var file in files)
        {
            if (IsImageFile(file))
            {
                try
                {
                    string nextFileName = _selectedFolder.GetNextFileName(_selectedDocument.DocumentType);
                    string extension = Path.GetExtension(file);
                    string targetPath = Path.Combine(_selectedFolder.FolderPath, nextFileName + extension);

                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        targetPath = Path.Combine(_selectedFolder.FolderPath, $"{nextFileName}_{counter}{extension}");
                        counter++;
                    }

                    File.Copy(file, targetPath, true);
                    successCount++;
                }
                catch
                {
                    invalidFiles.Add(Path.GetFileName(file));
                }
            }
            else
            {
                invalidFiles.Add(Path.GetFileName(file));
            }
        }

        if (successCount > 0)
        {
            _selectedFolder.Refresh();
            RenderDocumentItems(_selectedFolder);
            UpdateFolderCard(_selectedFolder);
            UpdateStatusSummary();
            UpdateStatusDisplay();
            UpdateActionButtonsVisibility();
            ApplyFilter();

            foreach (var child in DocumentItemsPanel.Children)
            {
                if (child is Border b && b.Tag is DocumentItem d && d.DocumentType == _selectedDocument.DocumentType)
                {
                    SelectDocument(d);
                    break;
                }
            }

            SystemSounds.Asterisk.Play();
            TriggerADrive();
        }

        if (invalidFiles.Count > 0)
        {
            MessageBox.Show($"部分文件无法导入：\n{string.Join("\n", invalidFiles.Take(5))}" +
                            (invalidFiles.Count > 5 ? "\n..." : "") +
                            $"\n\n支持格式: jpg, jpeg, png, bmp, gif, tiff, pdf",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool IsImageFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".pdf" }.Contains(ext);
    }

    private void DocItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            if (border.Tag != _selectedDocument)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(237, 242, 247));
            }
        }
    }

    private void DocItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            if (border.Tag == _selectedDocument)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(219, 234, 254));
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromRgb(247, 250, 252));
            }
        }
    }

    private void DocumentItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button || e.OriginalSource is TextBlock textBlock && textBlock.Parent is Button)
            return;

        if (sender is Border border && border.Tag is DocumentItem doc)
        {
            SelectDocument(doc);
        }
    }

    private void SelectDocument(DocumentItem doc)
    {
        _selectedDocument = doc;

        foreach (var child in DocumentItemsPanel.Children)
        {
            if (child is Border border)
            {
                if (border.Tag == _selectedDocument)
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(219, 234, 254));
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(247, 250, 252));
                    border.BorderBrush = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);
                }
            }
        }

        // 选中资料项后，预览区允许拖入（显示复制光标，而不是禁止符号）
        PreviewDropGrid.AllowDrop = true;

        ShowPreview(doc);
    }

    private async void ScanDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DocumentItem doc)
        {
            lock (_scanLock)
            {
                if (_isScanning)
                {
                    SystemSounds.Beep.Play();
                    return;
                }
                _isScanning = true;
            }

            HashSet<string> existingFiles = null;
            string outputPath = null;
            string nextFileName = null;

            try
            {
                SetAllScanButtonsEnabled(false, doc);

                outputPath = _selectedFolder.FolderPath;
                nextFileName = _selectedFolder.GetNextFileName(doc.DocumentType);

                existingFiles = new HashSet<string>(Directory.GetFiles(outputPath).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

                bool scannerAvailable = await _scannerService.IsScannerAvailableAsync();

                if (!scannerAvailable)
                {
                    var result = MessageBox.Show(
                        "未检测到扫描仪。\n\n是否改为从文件选择图片？",
                        "扫描仪未检测到",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        ImportImageFileDirect(doc);
                    }
                    return;
                }

                string scannedFile = await _scannerService.ScanToFileAsync(outputPath, nextFileName);

                if (!string.IsNullOrEmpty(scannedFile) && File.Exists(scannedFile))
                {
                    ProcessSuccessfulScan(doc);
                }
                else
                {
                    await Task.Delay(500);

                    var currentFiles = Directory.GetFiles(outputPath)
                        .Where(f => IsImageFile(f))
                        .ToList();

                    var newFiles = currentFiles
                        .Where(f => !existingFiles.Contains(Path.GetFileName(f)))
                        .OrderByDescending(f => File.GetCreationTime(f))
                        .ToList();

                    if (newFiles.Count > 0)
                    {
                        foreach (var newFile in newFiles)
                        {
                            string ext = Path.GetExtension(newFile);
                            string targetPath;
                            if (newFiles.Count == 1)
                            {
                                targetPath = Path.Combine(outputPath, nextFileName + ext);
                            }
                            else
                            {
                                string numStr = newFiles.IndexOf(newFile) == 0 ? "" : $"_{newFiles.IndexOf(newFile)}";
                                targetPath = Path.Combine(outputPath, nextFileName + numStr + ext);
                            }
                            
                            int counter = 1;
                            while (File.Exists(targetPath))
                            {
                                targetPath = Path.Combine(outputPath, $"{nextFileName}_{counter}{ext}");
                                counter++;
                            }
                            File.Move(newFile, targetPath);
                        }

                        ProcessSuccessfulScan(doc);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"扫描出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetAllScanButtonsEnabled(true);
                lock (_scanLock)
                {
                    _isScanning = false;
                }
            }
        }
    }

    private void ProcessSuccessfulScan(DocumentItem doc)
    {
        _selectedFolder.Refresh();
        RenderDocumentItems(_selectedFolder);
        UpdateFolderCard(_selectedFolder);
        UpdateStatusSummary();
        UpdateStatusDisplay();
        UpdateActionButtonsVisibility();
        ApplyFilter();

        foreach (var child in DocumentItemsPanel.Children)
        {
            if (child is Border b && b.Tag is DocumentItem d && d.DocumentType == doc.DocumentType)
            {
                SelectDocument(d);
                break;
            }
        }

        SystemSounds.Asterisk.Play();
        TriggerADrive();
    }

    private void SetAllScanButtonsEnabled(bool enabled, DocumentItem scanningDoc = null)
    {
        foreach (var child in DocumentItemsPanel.Children)
        {
            if (child is Border border)
            {
                var scanButton = FindVisualChild<Button>(border);
                if (scanButton != null && scanButton.Tag is DocumentItem docItem)
                {
                    if (enabled)
                    {
                        scanButton.Content = "📷 扫描";
                        scanButton.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                        scanButton.Opacity = 1.0;
                    }
                    else if (scanningDoc != null && docItem.DocumentType == scanningDoc.DocumentType)
                    {
                        // 当前正在扫描的按钮：深橙色 + 完整文案
                        scanButton.Content = $"⏳ 扫描{docItem.DisplayName}中...";
                        scanButton.Background = new SolidColorBrush(Color.FromRgb(230, 110, 0));
                        scanButton.Opacity = 1.0;
                    }
                    else
                    {
                        // 其他被锁定的按钮：灰色 + 半透明 + 简短文案
                        scanButton.Content = "⏸ 等待";
                        scanButton.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));
                        scanButton.Opacity = 0.55;
                    }
                }
            }
        }
    }

    private static T FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate = null) where T : DependencyObject
    {
        if (parent == null) return null;

        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && (predicate == null || predicate(typedChild)))
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child, predicate);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void ImportDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DocumentItem doc)
        {
            ImportImageFile(doc);
        }
    }

    private void ImportImageFile(DocumentItem doc)
    {
        var picker = new TempFilePickerWindow(_tempStorage)
        {
            Owner = this
        };

        if (picker.ShowDialog() == true && picker.SelectedFiles.Count > 0)
        {
            var paths = picker.SelectedFiles.Select(f => f.FilePath).ToList();
            var idsToDelete = new List<string>();
            
            var successPaths = ImportFilesToDocument(doc, paths);
            
            foreach (var file in picker.SelectedFiles)
            {
                if (successPaths.Contains(file.FilePath))
                {
                    idsToDelete.Add(file.Id);
                }
            }
            
            if (idsToDelete.Count > 0)
            {
                _tempStorage.DeleteFiles(idsToDelete);
            }
        }
    }

    private void ImportImageFileDirect(DocumentItem doc)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.pdf|所有文件|*.*",
            Title = $"选择{doc.DisplayName}图片",
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() == true)
        {
            ImportFilesToDocument(doc, openFileDialog.FileNames.ToList());
        }
    }

    private List<string> ImportFilesToDocument(DocumentItem doc, List<string> sourceFiles)
    {
        var successPaths = new List<string>();
        if (sourceFiles == null || sourceFiles.Count == 0) return successPaths;

        int successCount = 0;
        List<string> invalidFiles = new List<string>();

        foreach (var sourceFile in sourceFiles)
        {
            if (!IsImageFile(sourceFile) || !File.Exists(sourceFile))
            {
                invalidFiles.Add(Path.GetFileName(sourceFile));
                continue;
            }

            try
            {
                string nextFileName = _selectedFolder.GetNextFileName(doc.DocumentType);
                string extension = Path.GetExtension(sourceFile);
                string targetPath = Path.Combine(_selectedFolder.FolderPath, nextFileName + extension);

                int counter = 1;
                while (File.Exists(targetPath))
                {
                    targetPath = Path.Combine(_selectedFolder.FolderPath, $"{nextFileName}_{counter}{extension}");
                    counter++;
                }

                File.Copy(sourceFile, targetPath, true);
                successCount++;
                successPaths.Add(sourceFile);
            }
            catch
            {
                invalidFiles.Add(Path.GetFileName(sourceFile));
            }
        }

        if (successCount > 0)
        {
            _selectedFolder.Refresh();
            RenderDocumentItems(_selectedFolder);
            UpdateFolderCard(_selectedFolder);
            UpdateStatusSummary();
            UpdateStatusDisplay();
            UpdateActionButtonsVisibility();
            ApplyFilter();

            foreach (var child in DocumentItemsPanel.Children)
            {
                if (child is Border b && b.Tag is DocumentItem d && d.DocumentType == doc.DocumentType)
                {
                    SelectDocument(d);
                    break;
                }
            }

            TriggerADrive();
        }

        if (invalidFiles.Count > 0)
        {
            MessageBox.Show($"部分文件无法导入：\n{string.Join("\n", invalidFiles.Take(5))}" +
                            (invalidFiles.Count > 5 ? "\n..." : "") +
                            $"\n\n支持格式: jpg, jpeg, png, bmp, gif, tiff, pdf",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (successCount > 0)
        {
            MessageBox.Show($"成功导入 {successCount} 个文件！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return successPaths;
    }

    private void MoveToTempStorage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DocumentItem doc)
        {
            if (!doc.Exists || doc.AllFiles.Count == 0)
            {
                return;
            }

            string fileList = string.Join("\n", doc.AllFiles.Select(f => Path.GetFileName(f)));
            var result = MessageBox.Show(
                $"确定要将{doc.DisplayName}的所有文件移动到暂存区吗？\n\n共 {doc.AllFiles.Count} 个文件：\n{fileList}",
                "确认移动到暂存",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    int movedCount = 0;
                    foreach (var file in doc.AllFiles)
                    {
                        if (File.Exists(file))
                        {
                            _tempStorage.AddFile(file);
                            File.Delete(file);
                            movedCount++;
                        }
                    }

                    _selectedFolder.Refresh();
                    RenderDocumentItems(_selectedFolder);
                    UpdateFolderCard(_selectedFolder);
                    UpdateStatusSummary();
                    UpdateStatusDisplay();
                    UpdateActionButtonsVisibility();
                    ApplyFilter();
                    UpdatePreviewPanel(null);

                    SystemSounds.Asterisk.Play();
                    MessageBox.Show($"成功移动 {movedCount} 个文件到暂存区！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"移动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void DeleteDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DocumentItem doc)
        {
            if (!doc.Exists || doc.AllFiles.Count == 0)
            {
                return;
            }

            string fileList = string.Join("\n", doc.AllFiles.Select(f => Path.GetFileName(f)));
            var result = MessageBox.Show(
                $"确定要删除{doc.DisplayName}的所有文件吗？\n\n共 {doc.AllFiles.Count} 个文件：\n{fileList}",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    foreach (var file in doc.AllFiles)
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }

                    _selectedFolder.Refresh();
                    RenderDocumentItems(_selectedFolder);
                    UpdateFolderCard(_selectedFolder);
                    UpdateStatusSummary();
                    UpdateStatusDisplay();
                    UpdateActionButtonsVisibility();
                    ApplyFilter();
                    UpdatePreviewPanel(null);

                    MessageBox.Show("删除成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void UpdateFolderCard(CustomerFolder folder)
    {
        foreach (var child in FolderListPanel.Children)
        {
            if (child is Border border && border.Tag == folder)
            {
                border.BorderBrush = folder.Status.GetStatusBrush();
                if (border.Child is Grid grid)
                {
                    foreach (var gridChild in grid.Children)
                    {
                        if (gridChild is StackPanel textStack && textStack.Orientation == Orientation.Vertical)
                        {
                            foreach (var stackChild in textStack.Children)
                            {
                                if (stackChild is TextBlock nameText && nameText.FontWeight == FontWeights.SemiBold)
                                {
                                    nameText.Text = folder.DisplayName;
                                }
                            }
                        }
                        else if (gridChild is StackPanel statusStack && statusStack.Orientation == Orientation.Horizontal)
                        {
                            foreach (var stackChild in statusStack.Children)
                            {
                                if (stackChild is Ellipse circle)
                                {
                                    circle.Fill = folder.Status.GetStatusBrush();
                                }
                                else if (stackChild is TextBlock tb)
                                {
                                    tb.Text = folder.Status.GetDisplayName();
                                    tb.Foreground = folder.Status.GetStatusBrush();
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void UpdateStatusSummary()
    {
        int missing = _allCustomerFolders.Count(f => f.Status == FolderStatus.Missing);
        int pending = _allCustomerFolders.Count(f => f.Status == FolderStatus.Pending);
        int notUploaded = _allCustomerFolders.Count(f => f.Status == FolderStatus.NotUploaded);
        int unconfirmed = _allCustomerFolders.Count(f => f.Status == FolderStatus.UploadedUnconfirmed);
        int uploaded = _allCustomerFolders.Count(f => f.Status == FolderStatus.Uploaded);

        string extraInfo = "";
        int shown = FolderListPanel.Children.Count;
        bool hasFilter = _currentFilter.HasValue;
        bool hasSearch = !string.IsNullOrWhiteSpace(_searchText);

        if (hasFilter || hasSearch)
        {
            extraInfo = $" (显示 {shown} 个";
            if (hasSearch)
            {
                extraInfo += $"，搜索: \"{_searchText.Trim()}\"";
            }
            extraInfo += ")";
        }

        StatusSummaryText.Text = $"共 {_allCustomerFolders.Count} 个文件夹 | " +
                                 $"🔴 缺资料: {missing} | " +
                                 $"🟣 暂滞: {pending} | " +
                                 $"🔵 未上传: {notUploaded} | " +
                                 $"🟠 未确认: {unconfirmed} | " +
                                 $"🟢 已上传: {uploaded}{extraInfo}";
    }

    private void ShowPreview(DocumentItem doc)
    {
        PreviewPanel.Children.Clear();

        if (doc != null && doc.Exists && doc.AllFiles.Count > 0)
        {
            var imageFiles = doc.AllFiles.Where(f => !f.ToLower().EndsWith(".pdf")).ToList();

            if (imageFiles.Count == 0)
            {
                NoPreviewText.Visibility = Visibility.Visible;
                NoPreviewTextBlock.Text = "PDF文件无法直接预览，请双击打开";
                return;
            }

            try
            {
                NoPreviewText.Visibility = Visibility.Collapsed;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                int totalRows = (imageFiles.Count + 1) / 2;
                for (int r = 0; r < totalRows; r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var imageFile = imageFiles[i];
                    int col = (i % 2) * 2;
                    int row = i / 2;

                    var border = new Border
                    {
                        Margin = new Thickness(0, 0, 0, 12),
                        Cursor = Cursors.Hand,
                        Tag = imageFile
                    };

                    var img = new Image
                    {
                        Source = LoadImage(imageFile),
                        Stretch = Stretch.Uniform,
                        MaxHeight = 300,
                        VerticalAlignment = VerticalAlignment.Top
                    };

                    border.Child = img;
                    border.MouseLeftButtonUp += PreviewImage_Click;
                    Grid.SetColumn(border, col);
                    Grid.SetRow(border, row);
                    grid.Children.Add(border);
                }

                PreviewPanel.Children.Add(grid);
            }
            catch
            {
                PreviewPanel.Children.Clear();
                NoPreviewText.Visibility = Visibility.Visible;
                NoPreviewTextBlock.Text = "无法预览此图片格式";
            }
        }
        else
        {
            UpdatePreviewPanel(doc);
        }
    }

    private void PreviewImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (!ImagePreviewHelper.CanOpen())
            return;

        string imagePath = null;
        if (sender is Border border && border.Tag is string borderPath)
        {
            imagePath = borderPath;
        }
        else if (sender is Image img && img.Tag is string imgPath)
        {
            imagePath = imgPath;
        }

        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            ImagePreviewHelper.Show(this, imagePath);
        }
    }

    private BitmapImage LoadImage(string path)
    {
        return ImagePreviewHelper.LoadImage(path);
    }

    private void UpdatePreviewPanel(DocumentItem doc)
    {
        PreviewPanel.Children.Clear();
        NoPreviewText.Visibility = Visibility.Visible;

        if (doc != null && !doc.Exists)
        {
            NoPreviewTextBlock.Text = $"{doc.DisplayName}尚未扫描或导入\n可点击扫描/导入按钮，或直接拖拽图片到预览区域";
        }
        else
        {
            NoPreviewTextBlock.Text = "请先点击选择左侧资料项，然后拖拽图片文件到这里导入";
        }
    }

    private void StartHttpServer()
    {
        if (string.IsNullOrEmpty(_rootDirectory) || !Directory.Exists(_rootDirectory))
            return;

        try
        {
            if (_httpServer == null)
            {
                _httpServer = new HttpServerService();
                _httpServer.OnFoldersChanged += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        LoadCustomerFolders();
                        _lastPendingCount = CountUnconfirmedFolders();
                    });
                };
            }

            if (_httpServer.IsRunning)
            {
                _httpServer.Stop();
            }

            bool started = _httpServer.TryStart(_rootDirectory, 8888);
            if (started)
            {
                _lastPendingCount = CountUnconfirmedFolders();
            }
        }
        catch { }
    }

    private void PhoneTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rootDirectory) || !Directory.Exists(_rootDirectory))
        {
            MessageBox.Show("请先选择根目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (_httpServer == null || !_httpServer.IsRunning)
            {
                StartHttpServer();
            }

            if (_httpServer == null || !_httpServer.IsRunning)
            {
                MessageBox.Show("无法启动传输服务，所有尝试的端口都被占用。\n请关闭一些程序后重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var qrWindow = new QrCodeWindow(_httpServer.ServerUrl, _httpServer.DiagnosticMessages)
            {
                Owner = this
            };
            qrWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"显示二维码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TempStorageButton_Click(object sender, RoutedEventArgs e)
    {
        var tempStorageWindow = new TempStorageWindow(_tempStorage, _scannerService)
        {
            Owner = this
        };
        tempStorageWindow.ShowDialog();
    }

    private void StatisticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rootDirectory) || !Directory.Exists(_rootDirectory))
        {
            MessageBox.Show("请先选择根目录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var statsWindow = new StatisticsWindow(_rootDirectory)
        {
            Owner = this
        };
        statsWindow.ShowDialog();
    }

    private int CountUnconfirmedFolders()
    {
        int count = 0;
        try
        {
            if (Directory.Exists(_rootDirectory))
            {
                foreach (var dir in Directory.GetDirectories(_rootDirectory))
                {
                    var folder = new CustomerFolder(dir);
                    if (folder.Status == FolderStatus.UploadedUnconfirmed)
                    {
                        count++;
                    }
                }
            }
        }
        catch { }
        return count;
    }

    protected override void OnClosed(EventArgs e)
    {
        _aDriveTimer?.Stop();
        KillADriveProcess();
        _httpServer?.Stop();
        base.OnClosed(e);
    }
}
