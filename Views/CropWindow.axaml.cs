using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace Lensly.Views;

public partial class CropWindow : Window
{
    private bool _isDragging;
    private bool _isResizing;
    private Point _dragStart;
    private string? _resizeHandle;
    private Rect _cropRect;
    private Rect _imageRect;
    
    public float CropX { get; private set; }
    public float CropY { get; private set; }
    public float CropWidth { get; private set; }
    public float CropHeight { get; private set; }
    public bool WasCropApplied { get; private set; }
    
    public CropWindow()
    {
        InitializeComponent();
        this.Opened += (s, e) =>
        {
            this.Focus();
            CropCanvas.Focus();
        };
    }
    
    public void SetImage(Bitmap bitmap)
    {
        CropImage.Source = bitmap;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var timer = new System.Timers.Timer(100);
            timer.Elapsed += (s, e) =>
            {
                timer.Stop();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    InitializeCropRect();
                });
            };
            timer.Start();
        });
    }
    
    private void InitializeCropRect()
    {
        Console.WriteLine("=== InitializeCropRect called ===");
        var containerBounds = ImageContainer.Bounds;
        var bitmap = CropImage.Source as Bitmap;
        
        Console.WriteLine($"Container bounds: {containerBounds}");
        Console.WriteLine($"Bitmap: {bitmap?.PixelSize}");
        
        if (bitmap == null || containerBounds.Width == 0 || containerBounds.Height == 0) 
        {
            Console.WriteLine("Error: Bitmap or container not ready");
            return;
        }
        var imageAspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
        var containerAspect = containerBounds.Width / containerBounds.Height;
        
        double imageWidth, imageHeight;
        
        if (imageAspect > containerAspect)
        {
            // Image is wider - constrained by width
            imageWidth = containerBounds.Width;
            imageHeight = imageWidth / imageAspect;
        }
        else
        {
            // Image is taller - constrained by height
            imageHeight = containerBounds.Height;
            imageWidth = imageHeight * imageAspect;
        }
        
        // Calculate where the image actually is within the container (it's centered)
        var imageX = (containerBounds.Width - imageWidth) / 2;
        var imageY = (containerBounds.Height - imageHeight) / 2;
        
        _imageRect = new Rect(imageX, imageY, imageWidth, imageHeight);
        
        Console.WriteLine($"Calculated image rect: {_imageRect}");
        var cropWidth = imageWidth * 0.8;
        var cropHeight = imageHeight * 0.8;
        var cropX = imageX + (imageWidth - cropWidth) / 2;
        var cropY = imageY + (imageHeight - cropHeight) / 2;
        
        _cropRect = new Rect(cropX, cropY, cropWidth, cropHeight);
        
        Console.WriteLine($"Initial crop rect: {_cropRect}");
        
        UpdateCropUI();
    }
    
    private void CropCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);
        if (IsNearHandle(pos, HandleTL, out _))
        {
            _isResizing = true;
            _resizeHandle = "TL";
            _dragStart = pos;
            return;
        }
        if (IsNearHandle(pos, HandleTR, out _))
        {
            _isResizing = true;
            _resizeHandle = "TR";
            _dragStart = pos;
            return;
        }
        if (IsNearHandle(pos, HandleBL, out _))
        {
            _isResizing = true;
            _resizeHandle = "BL";
            _dragStart = pos;
            return;
        }
        if (IsNearHandle(pos, HandleBR, out _))
        {
            _isResizing = true;
            _resizeHandle = "BR";
            _dragStart = pos;
            return;
        }
        if (_imageRect.Contains(pos))
        {
            _isDragging = true;
            _dragStart = pos;
            _cropRect = new Rect(pos, new Size(0, 0));
        }
    }
    
    private void CropCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);
        
        if (_isResizing && _resizeHandle != null)
        {
            var delta = pos - _dragStart;
            var newRect = _cropRect;
            
            switch (_resizeHandle)
            {
                case "TL":
                    newRect = new Rect(
                        Math.Max(_imageRect.Left, Math.Min(pos.X, _cropRect.Right - 50)),
                        Math.Max(_imageRect.Top, Math.Min(pos.Y, _cropRect.Bottom - 50)),
                        _cropRect.Width + (_cropRect.Left - Math.Max(_imageRect.Left, Math.Min(pos.X, _cropRect.Right - 50))),
                        _cropRect.Height + (_cropRect.Top - Math.Max(_imageRect.Top, Math.Min(pos.Y, _cropRect.Bottom - 50)))
                    );
                    break;
                case "TR":
                    newRect = new Rect(
                        _cropRect.Left,
                        Math.Max(_imageRect.Top, Math.Min(pos.Y, _cropRect.Bottom - 50)),
                        Math.Min(_imageRect.Right - _cropRect.Left, Math.Max(50, pos.X - _cropRect.Left)),
                        _cropRect.Height + (_cropRect.Top - Math.Max(_imageRect.Top, Math.Min(pos.Y, _cropRect.Bottom - 50)))
                    );
                    break;
                case "BL":
                    newRect = new Rect(
                        Math.Max(_imageRect.Left, Math.Min(pos.X, _cropRect.Right - 50)),
                        _cropRect.Top,
                        _cropRect.Width + (_cropRect.Left - Math.Max(_imageRect.Left, Math.Min(pos.X, _cropRect.Right - 50))),
                        Math.Min(_imageRect.Bottom - _cropRect.Top, Math.Max(50, pos.Y - _cropRect.Top))
                    );
                    break;
                case "BR":
                    newRect = new Rect(
                        _cropRect.Left,
                        _cropRect.Top,
                        Math.Min(_imageRect.Right - _cropRect.Left, Math.Max(50, pos.X - _cropRect.Left)),
                        Math.Min(_imageRect.Bottom - _cropRect.Top, Math.Max(50, pos.Y - _cropRect.Top))
                    );
                    break;
            }
            
            _cropRect = newRect;
            _dragStart = pos;
            UpdateCropUI();
        }
        else if (_isDragging)
        {
            var x = Math.Max(_imageRect.Left, Math.Min(_dragStart.X, pos.X));
            var y = Math.Max(_imageRect.Top, Math.Min(_dragStart.Y, pos.Y));
            var width = Math.Abs(pos.X - _dragStart.X);
            var height = Math.Abs(pos.Y - _dragStart.Y);
            if (pos.X < _dragStart.X) x = Math.Max(_imageRect.Left, pos.X);
            if (pos.Y < _dragStart.Y) y = Math.Max(_imageRect.Top, pos.Y);
            
            width = Math.Min(width, _imageRect.Right - x);
            height = Math.Min(height, _imageRect.Bottom - y);
            
            _cropRect = new Rect(x, y, width, height);
            UpdateCropUI();
        }
    }
    
    private void CropCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _isResizing = false;
        _resizeHandle = null;
    }
    
    private bool IsNearHandle(Point pos, Rectangle handle, out Point handleCenter)
    {
        var left = Canvas.GetLeft(handle);
        var top = Canvas.GetTop(handle);
        handleCenter = new Point(left + 10, top + 10);
        var distance = Math.Sqrt(Math.Pow(pos.X - handleCenter.X, 2) + Math.Pow(pos.Y - handleCenter.Y, 2));
        return distance < 20;
    }
    
    private void UpdateCropUI()
    {
        Canvas.SetLeft(CropRect, _cropRect.Left);
        Canvas.SetTop(CropRect, _cropRect.Top);
        CropRect.Width = _cropRect.Width;
        CropRect.Height = _cropRect.Height;
        Canvas.SetLeft(GridH1, _cropRect.Left);
        Canvas.SetTop(GridH1, _cropRect.Top + _cropRect.Height / 3);
        GridH1.Width = _cropRect.Width;
        
        Canvas.SetLeft(GridH2, _cropRect.Left);
        Canvas.SetTop(GridH2, _cropRect.Top + _cropRect.Height * 2 / 3);
        GridH2.Width = _cropRect.Width;
        
        Canvas.SetLeft(GridV1, _cropRect.Left + _cropRect.Width / 3);
        Canvas.SetTop(GridV1, _cropRect.Top);
        GridV1.Height = _cropRect.Height;
        
        Canvas.SetLeft(GridV2, _cropRect.Left + _cropRect.Width * 2 / 3);
        Canvas.SetTop(GridV2, _cropRect.Top);
        GridV2.Height = _cropRect.Height;
        Canvas.SetLeft(HandleTL, _cropRect.Left - 10);
        Canvas.SetTop(HandleTL, _cropRect.Top - 10);
        
        Canvas.SetLeft(HandleTR, _cropRect.Right - 10);
        Canvas.SetTop(HandleTR, _cropRect.Top - 10);
        
        Canvas.SetLeft(HandleBL, _cropRect.Left - 10);
        Canvas.SetTop(HandleBL, _cropRect.Bottom - 10);
        
        Canvas.SetLeft(HandleBR, _cropRect.Right - 10);
        Canvas.SetTop(HandleBR, _cropRect.Bottom - 10);
        UpdateDarkMask();
    }
    
    private void UpdateDarkMask()
    {
        var canvasBounds = ImageContainer.Bounds;
        var geometry = new CombinedGeometry
        {
            GeometryCombineMode = GeometryCombineMode.Exclude,
            Geometry1 = new RectangleGeometry(new Rect(0, 0, canvasBounds.Width, canvasBounds.Height)),
            Geometry2 = new RectangleGeometry(_cropRect)
        };
        DarkMask.Data = geometry;
    }
    
    private void ApplyCrop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Console.WriteLine($"=== ApplyCrop_Click Debug ===");
        Console.WriteLine($"CropRect: {_cropRect}");
        Console.WriteLine($"ImageRect: {_imageRect}");
        
        if (_cropRect.Width < 10 || _cropRect.Height < 10)
        {
            Console.WriteLine("Error: Crop rect too small");
            Close();
            return;
        }
        
        if (_imageRect.Width <= 0 || _imageRect.Height <= 0)
        {
            Console.WriteLine("Error: Image rect not initialized");
            Close();
            return;
        }
        var relativeX = (_cropRect.Left - _imageRect.Left) / _imageRect.Width;
        var relativeY = (_cropRect.Top - _imageRect.Top) / _imageRect.Height;
        var relativeWidth = _cropRect.Width / _imageRect.Width;
        var relativeHeight = _cropRect.Height / _imageRect.Height;
        
        Console.WriteLine($"Before clamp - X={relativeX}, Y={relativeY}, W={relativeWidth}, H={relativeHeight}");
        relativeX = Math.Max(0, Math.Min(1, relativeX));
        relativeY = Math.Max(0, Math.Min(1, relativeY));
        relativeWidth = Math.Max(0, Math.Min(1 - relativeX, relativeWidth));
        relativeHeight = Math.Max(0, Math.Min(1 - relativeY, relativeHeight));
        
        CropX = (float)relativeX;
        CropY = (float)relativeY;
        CropWidth = (float)relativeWidth;
        CropHeight = (float)relativeHeight;
        
        Console.WriteLine($"Final crop values: X={CropX}, Y={CropY}, W={CropWidth}, H={CropHeight}");
        
        WasCropApplied = true;
        Close();
    }
    
    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WasCropApplied = false;
        Close();
    }
}
