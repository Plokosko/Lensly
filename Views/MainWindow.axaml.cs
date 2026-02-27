using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lensly.Services;
using Lensly.ViewModels;

namespace Lensly.Views;

public partial class MainWindow : Window
{
    private bool _isPanning;
    private Point _lastPanPosition;
    private Point _lastPanVelocity;
    private DateTime _lastPanTime;

    private double _zoom = 1.0;
    private double _targetZoom = 1.0;
    private double _panX = 0.0;
    private double _panY = 0.0;
    private double _targetPanX = 0.0;
    private double _targetPanY = 0.0;

    private DispatcherTimer? _zoomAnimationTimer;
    private DispatcherTimer? _panAnimationTimer;
    private DispatcherTimer? _momentumTimer;
    private DispatcherTimer? _videoSyncTimer;
    private DispatcherTimer? _controlsHideTimer;
    
    private Popup? _audioPopup;
    
    private DispatcherTimer _throttleTimer;
    private Dictionary<ItemsControl, Rect> _pendingUpdates = new();
    
    private bool _isCompositorTransitioning;
    private DispatcherTimer _resizeEndTimer;
    private DispatcherTimer _compositorTransitionTimer;

    private DateTime _lastDoubleTap = DateTime.MinValue;
    private const int DoubleTapMs = 300;

    private void NativeOpenFolder_Click(object? sender, EventArgs e) => OpenFolder_Click(sender, null);
    private void NativeToggleFavorite_Click(object? sender, EventArgs e) => ToggleFavorite_Click(sender, new RoutedEventArgs());
    private void NativePlayPause_Click(object? sender, EventArgs e) => PlayPause_Click(sender, new RoutedEventArgs());
    private void NativeGalleryZoomIn_Click(object? sender, EventArgs e) => GalleryZoomIn_Click(sender, new RoutedEventArgs());
    private void NativeGalleryZoomOut_Click(object? sender, EventArgs e) => GalleryZoomOut_Click(sender, new RoutedEventArgs());
    private void NativeInspectorButton_Click(object? sender, EventArgs e) => InspectorButton_Click(sender, new RoutedEventArgs());
    private void NativeNextButton_Click(object? sender, EventArgs e) => NextButton_Click(sender, new RoutedEventArgs());
    private void NativePrevButton_Click(object? sender, EventArgs e) => PrevButton_Click(sender, new RoutedEventArgs());
    private void NativeBackButton_Click(object? sender, EventArgs e) => BackButton_Click(sender, new RoutedEventArgs());
    private void NativePurgeDatabase_Click(object? sender, EventArgs e) => ((MainWindowViewModel)DataContext!).PurgeDatabase();

    public void PlayPause_Requested()
    {
        PlayPause_Click(null, new RoutedEventArgs());
    }

    public void SeekVideo_Requested(double seconds)
    {
        if (NativeVideoPlayer != null)
        {
            NativeVideoPlayer.Seek(seconds);
        }
    }

    public void SeekVideoPrecise_Requested(double seconds)
    {
        if (NativeVideoPlayer != null)
        {
            NativeVideoPlayer.SeekPrecise(seconds);
        }
    }

    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsEditing = !vm.IsEditing;
            if (vm.IsEditing)
            {
                vm.EditBrightness = 0;
                vm.EditContrast = 1;
                vm.EditRotation = 0;
                vm.SelectedFilter = "None";
            }
        }
    }

    private void CancelEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsEditing = false;
        }
    }

    private async void SaveEdit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ApplyEditAsync();
        }
    }

    private void RotateLeft_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // rotate left
            vm.EditRotation = (float)((vm.EditRotation - 90 + 360) % 360);
        }
    }

    private void RotateRight_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // rotate right
            vm.EditRotation = (float)((vm.EditRotation + 90) % 360);
        }
    }

    private async void CropToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedPhoto == null) return;
        var bitmap = vm.SelectedPhoto.FullImage;
        if (bitmap == null) return;
        
        // Temporarily close the DetailPopup to prevent it from capturing input
        if (DetailPopup != null)
        {
            DetailPopup.IsOpen = false;
        }
        
        // Open crop window as modal dialog
        var cropWindow = new CropWindow();
        cropWindow.SetImage(bitmap);
        
        await cropWindow.ShowDialog(this);
        
        // Restore the DetailPopup
        if (DetailPopup != null && vm.IsDetailView)
        {
            DetailPopup.IsOpen = true;
        }
        
        // If crop was applied, process it
        if (cropWindow.WasCropApplied)
        {
            await vm.CropAndResizeAsync(
                cropWindow.CropX,
                cropWindow.CropY,
                cropWindow.CropWidth,
                cropWindow.CropHeight
            );
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                var state = (WindowState)e.NewValue!;
                if (state == WindowState.Maximized || state == WindowState.FullScreen)
                {
                    Dispatcher.UIThread.Post(() => WindowState = WindowState.Normal);
                }
            }
        };
        
        _throttleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _throttleTimer.Tick += ThrottleTimer_Tick;

        _videoSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _videoSyncTimer.Tick += VideoSyncTimer_Tick;
        _videoSyncTimer.Start();

        _controlsHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _controlsHideTimer.Tick += (s, e) => 
        {
            if (DataContext is MainWindowViewModel vm && vm.IsDetailView)
            {
                vm.AreVideoControlsVisible = false;
            }
            _controlsHideTimer.Stop();
        };

        this.PointerMoved += (s, e) => 
        {
            ResetControlsTimer();
        };

        _resizeEndTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _resizeEndTimer.Tick += (s, e) =>
        {
            _resizeEndTimer.Stop();
            // After resize ends, ensure layout is correct
            ResetZoomState();
        };

        _compositorTransitionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800) // Longer for macOS fullscreen animation
        };
        _compositorTransitionTimer.Tick += (s, e) =>
        {
            _compositorTransitionTimer.Stop();
            _isCompositorTransitioning = false;
            OnPropertyChanged(new AvaloniaPropertyChangedEventArgs<bool>(
                this, IsCompositorTransitioningProperty, false, true, BindingPriority.LocalValue));
        };

        this.SizeChanged += (s, e) =>
        {
            _resizeEndTimer.Stop();
            _resizeEndTimer.Start();
        };

        _zoomAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _zoomAnimationTimer.Tick += ZoomAnimation_Tick;
        
        _panAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _panAnimationTimer.Tick += PanAnimation_Tick;
        
        _momentumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _momentumTimer.Tick += Momentum_Tick;
    }

    private void ResetControlsTimer()
    {
        if (DataContext is MainWindowViewModel vm && vm.IsDetailView)
        {
            vm.AreVideoControlsVisible = true;
            _controlsHideTimer?.Stop();
            _controlsHideTimer?.Start();
        }
    }

    private void Popup_PointerMoved(object? sender, PointerEventArgs e)
    {
        ResetControlsTimer();
    }

    private void DetailPanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        ResetControlsTimer();
    }

    private void ControlBar_PointerEntered(object? sender, PointerEventArgs e)
    {
        _controlsHideTimer?.Stop();
    }

    private void ControlBar_PointerExited(object? sender, PointerEventArgs e)
    {
        ResetControlsTimer();
    }

    private double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    private double EaseOutExpo(double t) => t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);

    private void ZoomAnimation_Tick(object? sender, EventArgs e)
    {
        const double zoomSpeed = 0.12;
        var diff = _targetZoom - _zoom;
        
        if (Math.Abs(diff) < 0.001)
        {
            _zoom = _targetZoom;
            _zoomAnimationTimer?.Stop();
        }
        else
        {
            _zoom += diff * zoomSpeed;
        }
        
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
        {
            var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
            if (zc != null)
            {
                vm.SelectedPhoto.ZoomLevel = _zoom;
                ClampPan(zc, vm.SelectedPhoto);
                ApplyMatrix(zc);
            }
        }
    }

    private void PanAnimation_Tick(object? sender, EventArgs e)
    {
        const double panSpeed = 0.15;
        var dx = _targetPanX - _panX;
        var dy = _targetPanY - _panY;
        
        if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1)
        {
            _panX = _targetPanX;
            _panY = _targetPanY;
            _panAnimationTimer?.Stop();
        }
        else
        {
            _panX += dx * panSpeed;
            _panY += dy * panSpeed;
        }
        
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
        {
            var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
            if (zc != null) ApplyMatrix(zc);
        }
    }

    private void Momentum_Tick(object? sender, EventArgs e)
    {
        var friction = 0.92;
        
        _panX += _lastPanVelocity.X;
        _panY += _lastPanVelocity.Y;
        
        _lastPanVelocity = new Point(_lastPanVelocity.X * friction, _lastPanVelocity.Y * friction);
        
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
        {
            var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
            if (zc != null)
            {
                ClampPan(zc, vm.SelectedPhoto);
                ApplyMatrix(zc);
            }
        }
        
        if (Math.Abs(_lastPanVelocity.X) < 0.5 && Math.Abs(_lastPanVelocity.Y) < 0.5)
        {
            _momentumTimer?.Stop();
        }
    }

    public static readonly DirectProperty<MainWindow, bool> IsCompositorTransitioningProperty =
        AvaloniaProperty.RegisterDirect<MainWindow, bool>(
            nameof(IsCompositorTransitioning),
            o => o.IsCompositorTransitioning);

    public bool IsCompositorTransitioning => _isCompositorTransitioning;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            // Fullscreen or Maximize transitions on macOS are heavy for the compositor
            _isCompositorTransitioning = true;
            _compositorTransitionTimer.Stop();
            _compositorTransitionTimer.Start();
            
            OnPropertyChanged(new AvaloniaPropertyChangedEventArgs<bool>(
                this, IsCompositorTransitioningProperty, false, true, BindingPriority.LocalValue));
        }
    }

    private void ThrottleTimer_Tick(object? sender, EventArgs e)
    {
        _throttleTimer.Stop();
        
        var updates = _pendingUpdates.ToList();
        _pendingUpdates.Clear();

        foreach (var kvp in updates)
        {
            RunViewportCheck(kvp.Key, kvp.Value);
        }
    }

    public void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 1)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Photos.CollectionChanged -= Photos_CollectionChanged;
            vm.Photos.CollectionChanged += Photos_CollectionChanged;

            vm.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(MainWindowViewModel.IsDetailView) && vm.IsDetailView)
                {
                    // Popups on macOS are separate windows, so we must find the slider in the Popup's own visual tree
                    Dispatcher.UIThread.Post(() => {
                        if (DetailPopup?.Child is Visual popupContent)
                        {
                            var slider = popupContent.FindDescendantOfType<Slider>(s => s?.Name == "VideoScrubSlider");
                            if (slider != null)
                            {
                                slider.RemoveHandler(PointerPressedEvent, VideoSeek_PointerPressed);
                                slider.RemoveHandler(PointerReleasedEvent, VideoSeek_PointerReleased);
                                
                                slider.AddHandler(PointerPressedEvent, VideoSeek_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
                                slider.AddHandler(PointerReleasedEvent, VideoSeek_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
                            }
                        }
                    }, DispatcherPriority.Loaded);
                }
            };
        }
    }

    private void Photos_CollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    { }

    private void RunViewportCheck(ItemsControl itemsControl, Rect visibleViewport)
    {
        var containers = itemsControl.GetRealizedContainers().ToList();
        if (containers.Count == 0) return;

        var buffer = Math.Max(visibleViewport.Height, 200);
        var loadRect = new Rect(visibleViewport.X, visibleViewport.Y - buffer,
                                visibleViewport.Width, visibleViewport.Height + buffer * 2);

        foreach (var container in containers)
        {
            if (container.DataContext is PhotoItem photo)
            {
                var topLeft = container.TranslatePoint(new Point(0, 0), itemsControl);
                var bottomRight = container.TranslatePoint(
                    new Point(container.Bounds.Width, container.Bounds.Height), itemsControl);

                if (topLeft.HasValue && bottomRight.HasValue)
                {
                    var itemRect = new Rect(topLeft.Value, bottomRight.Value);
                    if (itemRect.Intersects(loadRect))
                        _ = photo.LoadImageAsync((MainWindowViewModel)DataContext!);
                    else
                        photo.UnloadThumbnail();
                }
            }
        }
    }

    public void PhotoListBox_ViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is ItemsControl ic && ic.DataContext is PhotoGroup)
        {
            _pendingUpdates[ic] = e.EffectiveViewport;
            _throttleTimer.Stop();
            _throttleTimer.Start();
        }
    }

    public void PhotoGrid_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PhotoGridWidth = e.NewSize.Width;
        }
    }

    public void ZoomContainer_ViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.IsDetailView && _zoom <= 1.0)
        {
            ResetZoomState();
        }
    }

    public async void OpenFolder_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Select Photos", AllowMultiple = false });

            if (folders.Count >= 1 && folders[0].TryGetLocalPath() is { } path)
                _ = ((MainWindowViewModel)DataContext!).LoadPhotosFromFolderAsync(path);
        }
        catch (Exception ex) { Debug.WriteLine($"Folder picker: {ex.Message}"); }
    }

    public void BackButton_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            ResetZoomState();
            ((MainWindowViewModel)DataContext!).ClearSelection();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BackButton_Click failed: {ex.Message}");
        }
    }

    public void GalleryBack_Click(object? s, RoutedEventArgs e)
    {
        ((MainWindowViewModel)DataContext!).GoBack();
    }

    public void NextButton_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            ((MainWindowViewModel)DataContext!).SelectNextPhoto();
            Dispatcher.UIThread.Post(() => ResetZoomState(), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NextButton_Click failed: {ex.Message}");
        }
    }

    public void PrevButton_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            ((MainWindowViewModel)DataContext!).SelectPreviousPhoto();
            Dispatcher.UIThread.Post(() => ResetZoomState(), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PrevButton_Click failed: {ex.Message}");
        }
    }

    public void InspectorButton_Click(object? s, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
            vm.SelectedPhoto.ShowInspector = !vm.SelectedPhoto.ShowInspector;
    }

    public void GalleryZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ZoomIn();
    }

    public void GalleryZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.ZoomOut();
    }

    public void ZoomIn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
        {
            var container = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
            if (container == null) return;
            
            var cx = container.Bounds.Width / 2.0;
            var cy = container.Bounds.Height / 2.0;
            
            if (Math.Abs(_zoom - _targetZoom) < 0.1)
            {
                var newZoom = Math.Min(_zoom * 1.5, 10.0);
                SmoothZoomAt(cx, cy, newZoom, container, vm.SelectedPhoto);
            }
        }
    }

    public void ZoomOut_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
        {
            var container = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
            if (container == null) return;
            
            var cx = container.Bounds.Width / 2.0;
            var cy = container.Bounds.Height / 2.0;
            
            if (Math.Abs(_zoom - _targetZoom) < 0.1)
            {
                var newZoom = Math.Max(_zoom / 1.5, 0.5);
                SmoothZoomAt(cx, cy, newZoom, container, vm.SelectedPhoto);
            }
        }
    }

    public void Detail_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedPhoto == null) return;
        if (sender is not Control control) return;

        var mousePos = e.GetPosition(control);
        
        var factor = e.Delta.Y > 0 ? 1.0 + Math.Abs(e.Delta.Y) * 0.15 : 1.0 / (1.0 + Math.Abs(e.Delta.Y) * 0.15);
        var newZoom = Math.Clamp(_zoom * factor, 0.5, 10.0);
        
        SmoothZoomAt(mousePos.X, mousePos.Y, newZoom, control, vm.SelectedPhoto);
        e.Handled = true;
    }

    public void Detail_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        
        _momentumTimer?.Stop();
        
        var now = DateTime.Now;
        if ((now - _lastDoubleTap).TotalMilliseconds < DoubleTapMs)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
            {
                var container = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
                if (container != null)
                {
                    var pos = e.GetPosition(container);
                    if (_zoom > 1.5)
                    {
                        SmoothZoomAt(pos.X, pos.Y, 1.0, container, vm.SelectedPhoto);
                    }
                    else
                    {
                        SmoothZoomAt(pos.X, pos.Y, 3.0, container, vm.SelectedPhoto);
                    }
                }
            }
            _lastDoubleTap = DateTime.MinValue;
            _isPanning = false;
            return;
        }
        
        _lastDoubleTap = now;
        
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPanPosition = e.GetPosition(this);
            _lastPanVelocity = new Point(0, 0);
            _lastPanTime = DateTime.Now;
            if (sender is Control c) c.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
    }

    public void Detail_PointerMoved(object? sender, PointerEventArgs e)
    {
        ResetControlsTimer();
        
        if (!_isPanning) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (DataContext is not MainWindowViewModel vm || vm.SelectedPhoto == null) return;
        if (sender is not Control control) return;

        var now = DateTime.Now;
        var pos = e.GetPosition(this);
        var delta = pos - _lastPanPosition;
        var dt = (now - _lastPanTime).TotalSeconds;
        
        if (dt > 0)
        {
            _lastPanVelocity = new Point(delta.X / dt * 0.1, delta.Y / dt * 0.1);
        }
        
        _lastPanPosition = pos;
        _lastPanTime = now;

        _panX += delta.X;
        _panY += delta.Y;

        ClampPan(control, vm.SelectedPhoto);
        ApplyMatrix(control);
    }

    public void Detail_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPanning && _zoom > 1.0)
        {
            var speed = Math.Sqrt(_lastPanVelocity.X * _lastPanVelocity.X + _lastPanVelocity.Y * _lastPanVelocity.Y);
            if (speed > 2)
            {
                _momentumTimer?.Start();
            }
        }
        
        _isPanning = false;
        if (sender is Control c) c.Cursor = Cursor.Default;
    }

    private void ApplyMatrix(Control? zoomContainer)
    {
        if (zoomContainer == null) return;
        
        var imageBorder = zoomContainer.FindDescendantOfType<Border>(b => b?.Name == "ImageBorder");
        var videoBorder = zoomContainer.FindDescendantOfType<Border>(b => b?.Name == "VideoBorder");
        
        ApplyTransformToBorder(imageBorder);
        ApplyTransformToBorder(videoBorder);
    }
    
    private void ApplyTransformToBorder(Border? border)
    {
        if (border?.RenderTransform is TransformGroup group)
        {
            foreach (var t in group.Children)
            {
                if (t is ScaleTransform st)
                {
                    st.ScaleX = _zoom;
                    st.ScaleY = _zoom;
                }
                else if (t is TranslateTransform tt)
                {
                    tt.X = _panX;
                    tt.Y = _panY;
                }
            }
        }
    }

    private void ZoomAt(double cx, double cy, double newZoom, Control container, PhotoItem photo)
    {
        var vpW = container.Bounds.Width;
        var vpH = container.Bounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        var imgW = photo.FullImage?.Size.Width ?? 1920;
        var imgH = photo.FullImage?.Size.Height ?? 1080;
        
        var fitScale = Math.Min(vpW / imgW, vpH / imgH);
        var imgDispW = imgW * fitScale;
        var imgDispH = imgH * fitScale;

        var worldX = (cx - _panX) / _zoom;
        var worldY = (cy - _panY) / _zoom;

        _zoom = newZoom;

        _panX = cx - _zoom * worldX;
        _panY = cy - _zoom * worldY;

        photo.ZoomLevel = _zoom;

        ClampPan(container, photo);
        ApplyMatrix(container);
    }

    private void SmoothZoomAt(double cx, double cy, double newZoom, Control container, PhotoItem photo)
    {
        var vpW = container.Bounds.Width;
        var vpH = container.Bounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        var imgW = photo.FullImage?.Size.Width ?? 1920;
        var imgH = photo.FullImage?.Size.Height ?? 1080;
        
        if (imgW <= 0) imgW = 1920;
        if (imgH <= 0) imgH = 1080;
        
        var fitScale = Math.Min(vpW / imgW, vpH / imgH);
        var imgDispW = imgW * fitScale;
        var imgDispH = imgH * fitScale;

        var worldX = (cx - _panX) / _zoom;
        var worldY = (cy - _panY) / _zoom;

        _targetZoom = newZoom;

        _targetPanX = cx - _targetZoom * worldX;
        _targetPanY = cy - _targetZoom * worldY;

        ClampPanTarget(container, photo);
        
        _zoomAnimationTimer?.Stop();
        _zoomAnimationTimer?.Start();
        
        _zoom = _targetZoom;
        _panX = _targetPanX;
        _panY = _targetPanY;
    }

    private void ClampPanTarget(Control zoomContainer, PhotoItem photo)
    {
        var vpW = zoomContainer.Bounds.Width;
        var vpH = zoomContainer.Bounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        var imgW = photo.FullImage?.Size.Width ?? 1920;
        var imgH = photo.FullImage?.Size.Height ?? 1080;
        
        if (imgW <= 0) imgW = 1920;
        if (imgH <= 0) imgH = 1080;

        var fitScale = Math.Min(vpW / imgW, vpH / imgH);
        var imgDispW = imgW * fitScale;
        var imgDispH = imgH * fitScale;

        var scaledW = imgDispW * _targetZoom;
        var scaledH = imgDispH * _targetZoom;

        var naturalCenterX = (vpW - scaledW) / 2.0;
        var naturalCenterY = (vpH - scaledH) / 2.0;

        if (scaledW <= vpW)
        {
            _targetPanX = naturalCenterX;
        }
        else
        {
            var minPanX = -(scaledW - vpW);
            var maxPanX = 0;
            _targetPanX = Math.Clamp(_targetPanX, minPanX, maxPanX);
        }

        if (scaledH <= vpH)
        {
            _targetPanY = naturalCenterY;
        }
        else
        {
            var minPanY = -(scaledH - vpH);
            var maxPanY = 0;
            _targetPanY = Math.Clamp(_targetPanY, minPanY, maxPanY);
        }
    }

    private void ClampPan(Control zoomContainer, PhotoItem photo)
    {
        var vpW = zoomContainer.Bounds.Width;
        var vpH = zoomContainer.Bounds.Height;
        if (vpW <= 0 || vpH <= 0) return;

        var imgW = photo.FullImage?.Size.Width ?? 1920;
        var imgH = photo.FullImage?.Size.Height ?? 1080;
        
        if (imgW <= 0) imgW = 1920;
        if (imgH <= 0) imgH = 1080;

        var fitScale = Math.Min(vpW / imgW, vpH / imgH);
        var imgDispW = imgW * fitScale;
        var imgDispH = imgH * fitScale;

        var scaledW = imgDispW * _zoom;
        var scaledH = imgDispH * _zoom;

        var naturalCenterX = (vpW - scaledW) / 2.0;
        var naturalCenterY = (vpH - scaledH) / 2.0;

        if (scaledW <= vpW)
        {
            _panX = naturalCenterX;
        }
        else
        {
            var minPanX = -(scaledW - vpW);
            var maxPanX = 0;
            _panX = Math.Clamp(_panX, minPanX, maxPanX);
        }

        if (scaledH <= vpH)
        {
            _panY = naturalCenterY;
        }
        else
        {
            var minPanY = -(scaledH - vpH);
            var maxPanY = 0;
            _panY = Math.Clamp(_panY, minPanY, maxPanY);
        }
    }

    private void ResetZoomState()
    {
        _momentumTimer?.Stop();
        _zoomAnimationTimer?.Stop();
        try
        {
            if (NativeVideoPlayer != null)
            {
                NativeVideoPlayer.Pause();
            }
        }
        catch { }
        
        _zoom = 1.0;
        _targetZoom = 1.0;
        _panX = 0.0;
        _panY = 0.0;
        _targetPanX = 0.0;
        _targetPanY = 0.0;
        
        try
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
            {
                var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
                if (zc == null) return;
                
                var vpW = zc.Bounds.Width;
                var vpH = zc.Bounds.Height;
                    
                if (vpW <= 0 || vpH <= 0)
                {
                    vpW = this.Bounds.Width - 240;
                    vpH = this.Bounds.Height;
                }

                if (vpW <= 0 || vpH <= 0) return;

                double imgW, imgH;
                if (vm.SelectedPhoto.IsVideo)
                {
                    imgW = 1920;
                    imgH = 1080;

                    // Only reload if path is different to avoid constant restarts
                    if (NativeVideoPlayer != null && NativeVideoPlayer.FilePath != vm.SelectedPhoto.FilePath)
                    {
                        NativeVideoPlayer.FilePath = vm.SelectedPhoto.FilePath;
                    }
                }
                else
                {
                    imgW = vm.SelectedPhoto.FullImage?.Size.Width ?? 1920;
                    imgH = vm.SelectedPhoto.FullImage?.Size.Height ?? 1080;
                }
                
                if (imgW <= 0 || imgH <= 0)
                {
                    imgW = 1920;
                    imgH = 1080;
                }
                
                var fitScale = Math.Min(vpW / imgW, vpH / imgH);
                var imgDispW = imgW * fitScale;
                var imgDispH = imgH * fitScale;

                // Images can size naturally with MaxWidth/MaxHeight bindings
                // But native video needs explicit sizing
                var vb = zc.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "VideoBorder");
                if (vb != null) 
                { 
                    vb.Width = imgDispW; 
                    vb.Height = imgDispH; 
                }
                
                _zoom = 1.0;
                _targetZoom = 1.0;
                _panX = 0;
                _panY = 0;
                _targetPanX = 0;
                _targetPanY = 0;
                
                ApplyMatrix(zc);
                vm.SelectedPhoto.ZoomLevel = _zoom;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ResetZoomState failed: {ex.Message}");
        }
    }

    private void VideoSyncTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.IsVideoSelected && NativeVideoPlayer != null)
        {
            vm.VideoDurationSeconds = NativeVideoPlayer.GetDuration();
            vm.SetPositionInternal(NativeVideoPlayer.GetCurrentTime());
            
            bool isPlaying = NativeVideoPlayer.IsPlaying();
            vm.IsPlaying = isPlaying;

            // If VM says it should be playing but native says it's not (and we aren't at the end), try to play
            if (!isPlaying && vm.IsVideoSelected && vm.VideoDurationSeconds > 0 && vm.VideoPositionSeconds < vm.VideoDurationSeconds - 0.1)
            {
                // We don't force play here to avoid fighting with manual pauses, 
                // but this is where we'd sync if needed.
            }
        }
    }

    public void PlayPause_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (NativeVideoPlayer != null)
            {
                if (NativeVideoPlayer.IsPlaying()) NativeVideoPlayer.Pause();
                else NativeVideoPlayer.Play();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PlayPause_Click failed: {ex.Message}");
        }
    }

    public void VideoSeek_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsUserSeeking = true;
            // Pause the video while scrubbing for better control
            if (NativeVideoPlayer != null && NativeVideoPlayer.IsPlaying())
            {
                vm._wasPlayingBeforeSeeking = true;
                NativeVideoPlayer.Pause();
            }
            else
            {
                vm._wasPlayingBeforeSeeking = false;
            }
        }
    }

    public void VideoSeek_PointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel vm && vm.IsUserSeeking && sender is Slider slider)
            {
                if (NativeVideoPlayer != null)
                {
                    NativeVideoPlayer.Seek(slider.Value);
                }
                vm.UpdatePositionForScrubbing(slider.Value);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VideoSeek_PointerMoved failed: {ex.Message}");
        }
    }

    public void VideoSeek_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel vm && sender is Slider slider)
            {
                vm.IsUserSeeking = false;
                
                // Precise seek when user releases
                if (NativeVideoPlayer != null)
                {
                    NativeVideoPlayer.SeekPrecise(slider.Value);
                }
                if (vm._wasPlayingBeforeSeeking && NativeVideoPlayer != null)
                {
                    NativeVideoPlayer.Play();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VideoSeek_PointerReleased failed: {ex.Message}");
        }
    }

    public void VideoSeek_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel vm && !vm.IsUserSeeking && sender is Slider slider)
            {
                if (NativeVideoPlayer != null && Math.Abs(e.NewValue - vm.VideoPositionSeconds) > 0.5)
                {
                    NativeVideoPlayer.SeekPrecise(e.NewValue);
                    vm._ignorePlaybackUpdatesUntil = DateTime.Now.AddMilliseconds(300);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VideoSeek_ValueChanged failed: {ex.Message}");
        }
    }

    public void VideoVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (NativeVideoPlayer != null)
        {
            NativeVideoPlayer.SetVolume((float)e.NewValue);
        }
    }

    public void AudioOutputBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        if (_audioPopup == null)
            _audioPopup = BuildAudioOutputPopup();

        _audioPopup.PlacementTarget = btn;
        _audioPopup.IsOpen = !_audioPopup.IsOpen;
    }

    private Popup BuildAudioOutputPopup()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock
        {
            Text = "Audio Output",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = Brushes.White
        });

        panel.Children.Add(new Rectangle
        {
            Height = 1,
            Fill = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            Margin = new Thickness(0, 2)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Volume",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#99FFFFFF"))
        });

        var volSlider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center
        };

        volSlider.ValueChanged += (_, ev) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.VideoVolume = ev.NewValue;
        };

        panel.Children.Add(volSlider);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E6000000")),
            CornerRadius = new CornerRadius(12),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #50000000"),
            Child = panel,
            MinWidth = 210
        };

        return new Popup
        {
            Child = border,
            Placement = PlacementMode.Top,
            IsLightDismissEnabled = true
        };
    }

    public async void CopyPath_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            if (s is MenuItem { DataContext: PhotoItem p })
            {
                var tl = TopLevel.GetTopLevel(this);
                if (tl?.Clipboard != null) await tl.Clipboard.SetTextAsync(p.FilePath);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Clipboard: {ex.Message}"); }
    }

    public void RevealInOS_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            if (s is MenuItem { DataContext: PhotoItem p })
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start("explorer.exe", $"/select,\"{p.FilePath}\"");
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", $"-R \"{p.FilePath}\"");
            }
        }
        catch (Exception ex) { Debug.WriteLine($"OS reveal: {ex.Message}"); }
    }

    public void DeletePhoto_Click(object? s, RoutedEventArgs e)
    {
        if (s is MenuItem { DataContext: PhotoItem p })
            ((MainWindowViewModel)DataContext!).DeletePhoto(p);
    }

    public void ToggleFavorite_Click(object? s, RoutedEventArgs e)
    {
        if (s is MenuItem { DataContext: PhotoItem p })
            ((MainWindowViewModel)DataContext!).ToggleFavorite(p);
        else if (DataContext is MainWindowViewModel vm && vm.SelectedPhoto != null)
            vm.ToggleFavorite(vm.SelectedPhoto);
    }

    public void Edit_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            if (s is MenuItem { DataContext: PhotoItem p })
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo(p.FilePath) { UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", $"\"{p.FilePath}\"");
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Edit: {ex.Message}"); }
    }

    public void Share_Click(object? s, RoutedEventArgs e)
    {
        // Simple "Share" for now is revealing it to the OS so user can share from there
        RevealInOS_Click(s, e);
    }

    public async void Duplicate_Click(object? s, RoutedEventArgs e)
    {
        try
        {
            if (s is MenuItem { DataContext: PhotoItem p })
            {
                var dir = System.IO.Path.GetDirectoryName(p.FilePath);
                var ext = System.IO.Path.GetExtension(p.FilePath);
                var name = System.IO.Path.GetFileNameWithoutExtension(p.FilePath);
                var newPath = System.IO.Path.Combine(dir!, $"{name} copy{ext}");
                
                int i = 1;
                while (File.Exists(newPath))
                {
                    newPath = System.IO.Path.Combine(dir!, $"{name} copy {++i}{ext}");
                }
                
                File.Copy(p.FilePath, newPath);
                
                // Reload folder to show duplicate
                if (DataContext is MainWindowViewModel vm)
                    await vm.LoadPhotosFromFolderAsync(dir!);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Duplicate: {ex.Message}"); }
    }

    public void CollectionCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectedSidebarCategory = category;
        }
    }

    public void PersonCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MainWindowViewModel.PersonItemViewModel p } && DataContext is MainWindowViewModel vm)
        {
            vm.ShowPersonPhotos(p.Id);
        }
    }

    public void PersonName_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: MainWindowViewModel.PersonItemViewModel p } textBox && DataContext is MainWindowViewModel vm)
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
                vm.RenamePerson(p.Id, textBox.Text);
        }
    }

    public void PersonName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            // Clear focus to trigger LostFocus which calls RenamePerson
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            e.Handled = true;
        }
    }

    public void PeopleView_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
    }

    public void RenamePerson_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MainWindowViewModel.PersonItemViewModel p })
        {
            // Focus the textbox for renaming
            var stack = this.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(s => s.DataContext == p);
            var tb = stack?.FindDescendantOfType<TextBox>();
            tb?.Focus();
        }
    }

    public void DeletePerson_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: MainWindowViewModel.PersonItemViewModel p } && DataContext is MainWindowViewModel vm)
        {
            vm.DeletePerson(p.Id);
        }
    }

    public void MergePerson_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is MainWindowViewModel.PersonItemViewModel source && DataContext is MainWindowViewModel vm)
        {
            var flyout = new MenuFlyout();
            foreach (var target in vm.People.Where(x => x.Id != source.Id))
            {
                var item = new MenuItem { Header = $"Merge into {target.Name}" };
                item.Click += (s, ev) => vm.MergePerson(source.Id, target.Id);
                flyout.Items.Add(item);
            }
            
            if (flyout.Items.Count > 0)
                flyout.ShowAt(menuItem, true);
        }
    }

    public async void Thumbnail_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        try
        {
            if (sender is Border { DataContext: PhotoItem p })
            {
                var vm = (MainWindowViewModel)DataContext!;

                if (vm.SelectedTimeView == "Years")
                {
                    vm.SelectedTimeView = "Months";
                    
                    // Give the UI a moment to switch and render
                    await Task.Delay(100);
                    
                    var sv = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(x => x.FindDescendantOfType<ItemsControl>()?.Name == "PhotoGrid");
                    var ic = sv?.FindDescendantOfType<ItemsControl>();
                    if (sv != null && ic != null)
                    {
                        // Target the earliest month of that year
                        var targetGroup = vm.GroupedPhotos.LastOrDefault(g => g.Date.Year == p.DateTaken.Year);
                        if (targetGroup != null)
                        {
                            var container = ic.ContainerFromItem(targetGroup) as Control;
                            if (container != null)
                            {
                                var pos = container.TranslatePoint(new Point(0, 0), ic);
                                if (pos.HasValue)
                                {
                                    sv.Offset = new Vector(sv.Offset.X, Math.Max(0, pos.Value.Y - 50)); // scroll with a bit of padding
                                }
                            }
                        }
                    }
                    return;
                }

                vm.SelectedPhoto = p;
                _ = p.LoadFullResolutionAsync();
                
                // Give the UI a moment to switch to Detail View and layout
                await Task.Delay(50);
                
                ResetZoomState();
                for (int i = 0; i < 10; i++)
                {
                    if (p.FullImage != null || p.IsVideo) break;
                    await Task.Delay(30);
                }

                ResetZoomState();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Thumbnail_Tapped failed: {ex.Message}");
        }
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsDetailView)
        {
            // Show controls on any key press in detail view
            ResetControlsTimer();
            
            var isCmd = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta);
            var isCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            
            switch (e.Key)
            {
                case Avalonia.Input.Key.Right:
                case Avalonia.Input.Key.Down:
                    NextButton_Click(null, null!);
                    e.Handled = true;
                    break;

                case Avalonia.Input.Key.Left:
                case Avalonia.Input.Key.Up:
                    PrevButton_Click(null, null!);
                    e.Handled = true;
                    break;

                case Avalonia.Input.Key.Escape:
                    BackButton_Click(null, null!);
                    e.Handled = true;
                    break;

                case Avalonia.Input.Key.Space:
                    if (vm.SelectedPhoto != null)
                    {
                        if (vm.SelectedPhoto.IsVideo)
                            vm.TogglePlayPause();
                        else
                        {
                            var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
                            if (zc != null)
                            {
                                var newZoom = _zoom > 1.5 ? 1.0 : 3.0;
                                SmoothZoomAt(zc.Bounds.Width / 2, zc.Bounds.Height / 2,
                                       newZoom, zc, vm.SelectedPhoto);
                            }
                        }
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.D0 when isCmd || isCtrl:
                    if (vm.SelectedPhoto != null)
                    {
                        ResetZoomState();
                        e.Handled = true;
                    }
                    break;
                    
                case Avalonia.Input.Key.D1 when isCmd || isCtrl:
                    if (vm.SelectedPhoto != null)
                    {
                        var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
                        if (zc != null)
                        {
                            SmoothZoomAt(zc.Bounds.Width / 2, zc.Bounds.Height / 2, 1.0, zc, vm.SelectedPhoto);
                        }
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.F:
                    if (vm.SelectedPhoto != null)
                    {
                        ResetZoomState();
                        var zc = this.FindDescendantOfType<Grid>(g => g?.Name == "ZoomContainer");
                        if (zc != null) ApplyMatrix(zc);
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.I:
                    if (vm.SelectedPhoto != null)
                    {
                        vm.SelectedPhoto.ShowInspector = !vm.SelectedPhoto.ShowInspector;
                        e.Handled = true;
                    }
                    break;

                case Avalonia.Input.Key.Delete:
                    if (vm.SelectedPhoto != null)
                    {
                        vm.DeletePhoto(vm.SelectedPhoto);
                        e.Handled = true;
                    }
                    break;
            }
        }
        else
        {
            if (e.Key == Avalonia.Input.Key.O &&
                (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta) ||
                 e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)))
            {
                OpenFolder_Click(null, null);
                e.Handled = true;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}

internal static class VisualExtensions
{
    public static T? FindDescendantOfType<T>(this Visual root, Func<T?, bool>? predicate = null)
        where T : Visual
    {
        foreach (var v in root.GetVisualDescendants())
        {
            if (v is T match && (predicate == null || predicate(match)))
                return match;
        }
        return null;
    }
}
