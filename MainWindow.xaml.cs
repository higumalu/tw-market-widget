using System.Windows;
using System.Windows.Input;
using TwMarketWidget.ViewModels;

namespace TwMarketWidget;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    /// <summary>完整模式的視窗尺寸，切到精簡模式時先記起來，切回去才回得去。</summary>
    private double _fullWidth;
    private double _fullHeight;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _fullWidth = _viewModel.WindowWidth;
        _fullHeight = _viewModel.WindowHeight;
        Width = _fullWidth;
        Height = _fullHeight;
        if (_viewModel.WindowLeft is { } left && _viewModel.WindowTop is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }

        ApplyMode();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CompactMode))
            {
                ApplyMode();
            }
        };

        Loaded += (_, _) => _viewModel.Start();
        Closing += (_, _) => SaveBounds();
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// 精簡模式高度跟著列數自動縮，寬度沿用上次調整過的值；
    /// 切回完整模式時還原原本的視窗尺寸。
    /// </summary>
    private void ApplyMode()
    {
        if (_viewModel.CompactMode)
        {
            if (SizeToContent == SizeToContent.Manual)
            {
                _fullWidth = Width;
                _fullHeight = Height;
            }

            ResizeMode = ResizeMode.CanResize;
            SizeToContent = SizeToContent.Height;
            MinWidth = 320;
            Width = _viewModel.CompactWidth;
        }
        else
        {
            if (SizeToContent == SizeToContent.Height)
            {
                _viewModel.CompactWidth = Width;
            }

            SizeToContent = SizeToContent.Manual;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 620;
            Width = _fullWidth;
            Height = _fullHeight;
        }
    }

    private void SaveBounds()
    {
        if (_viewModel.CompactMode)
        {
            _viewModel.CompactWidth = Width;
            _viewModel.UpdateWindowBounds(Left, Top, _fullWidth, _fullHeight);
        }
        else
        {
            _viewModel.UpdateWindowBounds(Left, Top, Width, Height);
        }
    }

    /// <summary>沒有系統標題列，改由這條列負責拖曳整個視窗。</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
