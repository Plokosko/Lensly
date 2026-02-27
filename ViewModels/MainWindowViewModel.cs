using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lensly.Services;
using Lensly.Models;
using Lensly.Views;

namespace Lensly.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly List<PhotoItem> _allPhotos = new();
    private string _currentFolderPath = string.Empty;
    private string _selectedSidebarCategory = "Library";
    private PhotoItem? _selectedPhoto;
    private object _currentPage;
    private bool _groupByDate = true;
    private string _searchText = "";
    private string _selectedTimeView = "All Photos";

    public string SearchText
    {
        get => _searchText;
        set
        {
            SetProperty(ref _searchText, value);
            ApplyFilter();
        }
    }

    public ObservableCollection<string> TimeViews { get; } = new() { "Years", "Months", "All Photos" };

    public string SelectedTimeView
    {
        get => _selectedTimeView;
        set
        {
            SetProperty(ref _selectedTimeView, value);
            OnPropertyChanged(nameof(CurrentThumbnailSize));
            ApplyFilter();
        }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseIcon));
            }
        }
    }

    private bool _isUpdatingFromPlayback;
    private double _videoPositionSeconds;
    private double _videoDurationSeconds;
    private double _videoVolume = 0.8;

    public bool IsDetailView => SelectedPhoto != null;
    public bool IsCollectionsView => SelectedSidebarCategory == "Collections" || SelectedSidebarCategory == "All Albums";
    public bool IsGalleryView => !IsCollectionsView && !IsDetailView && (!IsPeopleView || _selectedPersonId != null);
    public bool IsEmpty => Photos.Count == 0 && !IsDetailView && GroupedPhotos.Count == 0 && !IsCollectionsView && (!IsPeopleView || _selectedPersonId != null);
    public string PhotoCount => IsPeopleView ? $"{People.Count} people" : (Photos.Count == 0 ? "" : $"{Photos.Count} items");

    public string CurrentCategoryName
    {
        get
        {
            if (SelectedSidebarCategory == "People" && _selectedPersonId != null)
            {
                var person = FaceManagementService.GetPeople().FirstOrDefault(p => p.Id == _selectedPersonId);
                return person?.Name ?? "People";
            }
            return SelectedSidebarCategory;
        }
    }
    
    public bool IsWhatsAppFolder => !string.IsNullOrEmpty(_currentFolderPath) && 
                                     (_currentFolderPath.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
                                      _currentFolderPath.Contains("WA", StringComparison.OrdinalIgnoreCase));

    private readonly ImageEditingService _editingService = new();

    private bool _isEditing;
    public bool IsEditing 
    { 
        get => _isEditing; 
        set 
        {
            if (SetProperty(ref _isEditing, value))
            {
                if (!value) 
                {
                    PreviewImage = null;
                    IsCropping = false;
                }
                else UpdatePreviewAsync();
            }
        }
    }

    private bool _isCropping;
    public bool IsCropping
    {
        get => _isCropping;
        set => SetProperty(ref _isCropping, value);
    }

    // Normalized crop (0-1)
    private double _cropX = 0.1;
    public double CropX { get => _cropX; set => SetProperty(ref _cropX, value); }
    private double _cropY = 0.1;
    public double CropY { get => _cropY; set => SetProperty(ref _cropY, value); }
    private double _cropW = 0.8;
    public double CropW { get => _cropW; set => SetProperty(ref _cropW, value); }
    private double _cropH = 0.8;
    public double CropH { get => _cropH; set => SetProperty(ref _cropH, value); }

    private float _editBrightness = 0;
    public float EditBrightness { get => _editBrightness; set { if (SetProperty(ref _editBrightness, value)) OnEditPropertyChanged(); } }

    private float _editContrast = 1;
    public float EditContrast { get => _editContrast; set { if (SetProperty(ref _editContrast, value)) OnEditPropertyChanged(); } }

    private float _editRotation = 0;
    public float EditRotation { get => _editRotation; set { if (SetProperty(ref _editRotation, value)) OnEditPropertyChanged(); } }

    private string? _selectedFilter;
    public string? SelectedFilter { get => _selectedFilter; set { if (SetProperty(ref _selectedFilter, value)) OnEditPropertyChanged(); } }

    private Bitmap? _previewImage;
    public Bitmap? PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }

    private CancellationTokenSource? _previewCts;

    private void OnEditPropertyChanged()
    {
        if (!IsEditing) return;
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;
        Task.Delay(30, token).ContinueWith(t => 
        {
            if (!t.IsCanceled) Dispatcher.UIThread.Post(() => UpdatePreviewAsync());
        }, token);
    }

    private async void UpdatePreviewAsync()
    {
        if (SelectedPhoto == null) return;

        var inputPath = SelectedPhoto.FilePath;
        var tempPath = Path.Combine(Path.GetTempPath(), "lensly_preview.jpg");

        // High-res thumbnails for zoomed views
        int itemsPerRow = _zoomLevels[_zoomLevelIndex];
        float previewDimension = (itemsPerRow <= 7) ? 2400f : 1200f;

        await Task.Run(() =>
        {
            FilterMapping.TryGetValue(SelectedFilter ?? "None", out var ciFilterName);

            _editingService.ApplyEdit(
                inputPath,
                tempPath,
                brightness: EditBrightness,
                contrast: EditContrast,
                rotation: EditRotation,
                filterName: ciFilterName,
                maxDimension: previewDimension
            );
        });

        if (File.Exists(tempPath))
        {
            try
            {
                using var stream = File.OpenRead(tempPath);
                PreviewImage = new Bitmap(stream);
            }
            catch { }
        }
    }

        private static readonly Dictionary<string, string?> FilterMapping = new()
        {
            { "None", null },
            { "Chrome", "CIPhotoEffectChrome" },
            { "Fade", "CIPhotoEffectFade" },
            { "Instant", "CIPhotoEffectInstant" },
            { "Mono", "CIPhotoEffectMono" },
            { "Noir", "CIPhotoEffectNoir" },
            { "Process", "CIPhotoEffectProcess" },
            { "Tonal", "CIPhotoEffectTonal" },
            { "Transfer", "CIPhotoEffectTransfer" },
            { "Sepia", "CISepiaTone" },
            { "Vignette", "CIVignette" }
        };
    
        public ObservableCollection<string> AvailableFilters { get; } = new(FilterMapping.Keys);
    
        public async Task ApplyEditAsync()
        {
            if (SelectedPhoto == null) return;
    
            var inputPath = SelectedPhoto.FilePath;
            var dir = Path.GetDirectoryName(inputPath) ?? "";
            var ext = Path.GetExtension(inputPath);
            var name = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(dir, $"{name}_edited_{DateTime.Now:yyyyMMddHHmmss}{ext}");
    
            FilterMapping.TryGetValue(SelectedFilter ?? "None", out var ciFilterName);
    
            var success = _editingService.ApplyEdit(
                inputPath, 
                outputPath, 
                brightness: EditBrightness,
                contrast: EditContrast,
                rotation: EditRotation,
                filterName: ciFilterName);

        if (success && File.Exists(outputPath))
        {
            var newPhoto = new PhotoItem
            {
                FilePath = outputPath,
                Title = Path.GetFileName(outputPath),
                DateTaken = DateTime.Now,
                IsFavorite = false
            };

            _allPhotos.Add(newPhoto);
            
            if (SelectedPhoto != null)
            {
                int index = Photos.IndexOf(SelectedPhoto);
                if (index >= 0) Photos.Insert(index + 1, newPhoto);
                else Photos.Add(newPhoto);
            }
            else
            {
                Photos.Add(newPhoto);
            }

            UpdateGroupedPhotos(new List<PhotoItem> { newPhoto });
            OnPropertyChanged(nameof(PhotoCount));

            IsEditing = false;
            SelectedPhoto = newPhoto;
            ShowNotification("Edit saved successfully!");
        }
        else
        {
            ShowNotification("Failed to save edit.");
        }
    }

    public async Task CropAndResizeAsync(float x, float y, float width, float height, float scale = 1.0f)
    {
        if (SelectedPhoto == null) return;

        Console.WriteLine($"=== CropAndResizeAsync ===");
        Console.WriteLine($"Normalized input: X={x}, Y={y}, W={width}, H={height}, Scale={scale}");

        var inputPath = SelectedPhoto.FilePath;
        var dir = Path.GetDirectoryName(inputPath) ?? "";
        var ext = Path.GetExtension(inputPath);
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var outputPath = Path.Combine(dir, $"{name}_cropped_{DateTime.Now:yyyyMMddHHmmss}{ext}");

        Console.WriteLine($"Input path: {inputPath}");
        Console.WriteLine($"Output path: {outputPath}");

        // Convert normalized to pixels
        var metadata = MetadataService.GetMetadata(inputPath);
        var imageWidth = metadata?.Width ?? 0;
        var imageHeight = metadata?.Height ?? 0;
        
        Console.WriteLine($"Image dimensions: {imageWidth}x{imageHeight}");
        
        if (imageWidth == 0 || imageHeight == 0)
        {
            ShowNotification("Failed to get image dimensions.");
            return;
        }
        
        // Normalized to pixels
        var pixelX = x * imageWidth;
        var pixelY = y * imageHeight;
        var pixelW = width * imageWidth;
        var pixelH = height * imageHeight;
        
        Console.WriteLine($"Pixel coordinates: X={pixelX}, Y={pixelY}, W={pixelW}, H={pixelH}");

        var success = _editingService.ApplyEdit(
            inputPath,
            outputPath,
            scaleX: scale,
            scaleY: scale,
            cropX: pixelX,
            cropY: pixelY,
            cropW: pixelW,
            cropH: pixelH
        );

        Console.WriteLine($"ApplyEdit result: {success}");

        if (success)
        {
            ShowNotification("Crop applied and saved.");
            
            // Add to current view
            var dateTaken = MetadataService.GetOriginalDateTaken(outputPath);
            var newPhoto = new PhotoItem
            {
                FilePath = outputPath,
                Title = Path.GetFileName(outputPath),
                DateTaken = dateTaken,
                IsFavorite = false
            };
            
            _allPhotos.Add(newPhoto);
            Photos.Add(newPhoto);
            UpdateGroupedPhotos(new List<PhotoItem> { newPhoto });
            OnPropertyChanged(nameof(PhotoCount));
            
            // Select cropped photo
            SelectedPhoto = newPhoto;
        }
        else
        {
            ShowNotification("Failed to crop/resize.");
        }
    }

    public void ToggleFavorite(PhotoItem photo)
    {
        if (photo == null) return;
        FaceManagementService.ToggleFavorite(photo.FilePath);
        photo.IsFavorite = FaceManagementService.IsFavorite(photo.FilePath);
        
        if (SelectedSidebarCategory == "Favorites")
            ApplyFilter();
    }

    public bool GroupByDate
    {
        get => _groupByDate;
        set
        {
            SetProperty(ref _groupByDate, value);
            ApplyFilter();
        }
    }

    private int _zoomLevelIndex = 0; // 3 items/row
    private readonly int[] _zoomLevels = { 3, 5, 7, 9, 15 }; // Mini view
    private double _photoGridWidth;

    public double PhotoGridWidth
    {
        get => _photoGridWidth;
        set
        {
            if (SetProperty(ref _photoGridWidth, value))
            {
                OnPropertyChanged(nameof(CurrentThumbnailSize));
            }
        }
    }

    public double CurrentThumbnailSize
    {
        get
        {
            if (_photoGridWidth <= 0) return 160;

            int itemsPerRow = _zoomLevels[_zoomLevelIndex];
            // Account for margins
            double availableWidth = _photoGridWidth - 48;
            
            // Subtract a tiny safety margin (0.5px total) to ensure layout rounding 
            // doesn't push the last item to a new row.
            return Math.Max(40, (availableWidth - 0.5) / itemsPerRow);
        }
    }

    public int ThumbnailWidth
    {
        get
        {
            // Width based on columns
            int itemsPerRow = _zoomLevels[_zoomLevelIndex];
            if (itemsPerRow <= 3) return 800;
            if (itemsPerRow <= 5) return 600;
            if (itemsPerRow <= 7) return 450;
            return 300;
        }
    }

    public void ZoomIn()
    {
        if (_zoomLevelIndex > 0)
        {
            _zoomLevelIndex--;
            OnPropertyChanged(nameof(CurrentThumbnailSize));
            OnPropertyChanged(nameof(ThumbnailWidth));
            ClearThumbnails();
            ApplyFilter();
        }
    }

    public void ZoomOut()
    {
        if (_zoomLevelIndex < _zoomLevels.Length - 1)
        {
            _zoomLevelIndex++;
            OnPropertyChanged(nameof(CurrentThumbnailSize));
            OnPropertyChanged(nameof(ThumbnailWidth));
            ClearThumbnails();
            ApplyFilter();
        }
    }

    private void ClearThumbnails()
    {
        foreach (var photo in _allPhotos)
        {
            photo.UnloadThumbnail();
        }
    }

    public ObservableCollection<string> LibraryItems { get; } = new() { "Library", "Collections" };
    public ObservableCollection<string> PinnedItems { get; } = new()
    {
        "Favorites", "Recently Saved", "Videos", "Screenshots", "People"
    };
    public ObservableCollection<string> AlbumItems { get; } = new() { "All Albums" };

    private Dictionary<string, string> _albumPaths = new();

    public ObservableCollection<PhotoItem> Photos { get; } = new();
    public ObservableCollection<PhotoGroup> GroupedPhotos { get; } = new();

    public ObservableCollection<CollectionItem> Collections { get; } = new();

    public class CollectionItem : ViewModelBase
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string AccentColor { get; set; } = "#007AFF";
        public List<PhotoItem> Photos { get; set; } = new();
        public int PhotoCount => Photos.Count;
        
        private Bitmap? _preview;
        public Bitmap? Preview 
        { 
            get => _preview; 
            set 
            {
                var old = _preview;
                if (SetProperty(ref _preview, value) && old != value)
                {
                    old?.Dispose();
                }
            }
        }
    }

    private int _itemsToProcess;
    private int _itemsProcessed;
    private bool _isAnalyzing;
    private string _currentStatusText = string.Empty;

    public int ItemsToProcess { get => _itemsToProcess; set => SetProperty(ref _itemsToProcess, value); }
    public int ItemsProcessed { get => _itemsProcessed; set => SetProperty(ref _itemsProcessed, value); }
    public bool IsAnalyzing { get => _isAnalyzing; set => SetProperty(ref _isAnalyzing, value); }
    public string CurrentStatusText { get => _currentStatusText; set => SetProperty(ref _currentStatusText, value); }
    public double AnalysisProgress => ItemsToProcess == 0 ? 0 : (double)ItemsProcessed / ItemsToProcess * 100.0;

    public void ReportProgress(bool isStart, string? fileName = null)
    {
        if (isStart)
        {
            ItemsToProcess++;
            IsAnalyzing = true;
            if (fileName != null) CurrentStatusText = $"Analyzing {fileName}...";
        }
        else
        {
            ItemsProcessed++;
            if (ItemsProcessed >= ItemsToProcess)
            {
                CurrentStatusText = "Clustering faces...";
                ItemsProcessed = 0;
                ItemsToProcess = 0;
                IsAnalyzing = false;
                FaceManagementService.ClusterPeople();
                if (IsPeopleView) _ = LoadPeopleAsync();
                CurrentStatusText = "";
            }
        }
        OnPropertyChanged(nameof(AnalysisProgress));
    }

    public void RenamePerson(string personId, string newName)
    {
        FaceManagementService.RenamePerson(personId, newName);
        var p = People.FirstOrDefault(x => x.Id == personId);
        if (p != null) p.Name = newName;
    }

    public void MergePerson(string sourceId, string targetId)
    {
        FaceManagementService.MergePeople(sourceId, targetId);
        _ = LoadPeopleAsync();
    }

    public void DeletePerson(string personId)
    {
        FaceManagementService.DeletePerson(personId);
        _ = LoadPeopleAsync();
    }

    private string _notificationMessage = string.Empty;
    public string NotificationMessage
    {
        get => _notificationMessage;
        set => SetProperty(ref _notificationMessage, value);
    }

    private bool _isNotificationVisible;
    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        set => SetProperty(ref _isNotificationVisible, value);
    }

    public void ShowNotification(string message)
    {
        NotificationMessage = message;
        IsNotificationVisible = true;
        Task.Delay(3000).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() => IsNotificationVisible = false);
        });
    }

    public void PurgeDatabase()
    {
        FaceManagementService.PurgeDatabase();
        _ = LoadPeopleAsync();
        ShowNotification("Face database purged. Rescanning library...");
    }

    public ObservableCollection<PersonItemViewModel> People { get; } = new();

    public class PersonItemViewModel : ViewModelBase
    {
        public string Id { get; set; } = string.Empty;
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
        private Bitmap? _representativeFace;
        public Bitmap? RepresentativeFace
        {
            get => _representativeFace;
            set => SetProperty(ref _representativeFace, value);
        }
        public int FaceCount { get; set; }
    }

    public object CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public PhotoItem? SelectedPhoto
    {
        get => _selectedPhoto;
        set
        {
            if (_selectedPhoto != value)
            {
                var previousPhoto = _selectedPhoto;
                
                if (previousPhoto != null)
                {
                    previousPhoto.CleanupVideo();
                    previousPhoto.ClearFullImage();
                }
                
                value?.ResetZoom();
                SetProperty(ref _selectedPhoto, value);
                CurrentPage = value ?? (object)this;
                OnPropertyChanged(nameof(IsDetailView));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(IsVideoSelected));
                
                if (value != null)
                {
                    _ = value.LoadFullResolutionAsync();
                    
                    if (value.IsVideo)
                    {
                        IsPlaying = false;
                        VideoDurationSeconds = 0;
                        SetPositionInternal(0);
                    }
                }
            }
        }
    }

    public bool IsVideoSelected => SelectedPhoto?.IsVideo ?? false;
    public bool IsPeopleView => SelectedSidebarCategory == "People" && _selectedPersonId == null;

    private bool _areVideoControlsVisible = true;
    public bool AreVideoControlsVisible
    {
        get => _areVideoControlsVisible;
        set => SetProperty(ref _areVideoControlsVisible, value);
    }

    public string PlayPauseIcon => _isPlaying ? "⏸" : "▶";

    public double VideoPositionSeconds
    {
        get => _videoPositionSeconds;
        set
        {
            // Ignore sync updates
            if (_isUpdatingFromPlayback) return;
            
            // Prevent seek feedback
            if (IsUserSeeking) return;
            
            _videoPositionSeconds = value;
            OnPropertyChanged(nameof(VideoPositionSeconds));
            OnPropertyChanged(nameof(VideoTimeDisplay));
        }
    }

    public double VideoDurationSeconds
    {
        get => _videoDurationSeconds;
        set => SetProperty(ref _videoDurationSeconds, value);
    }

    public double VideoVolume
    {
        get => _videoVolume;
        set => SetProperty(ref _videoVolume, value);
    }

    public string VideoTimeDisplay
    {
        get
        {
            var pos = TimeSpan.FromSeconds(_videoPositionSeconds);
            var dur = TimeSpan.FromSeconds(_videoDurationSeconds);
            return $"{pos:m\\:ss} / {dur:m\\:ss}";
        }
    }

    public bool IsUserSeeking { get; set; } = false;
    public bool _wasPlayingBeforeSeeking = false;

    public string SelectedSidebarCategory
    {
        get => _selectedSidebarCategory;
        set
        {
            if (value == null) return;
            
            if (SetProperty(ref _selectedSidebarCategory, value))
            {
                ClearSelection(); // Exit detail view
                _selectedPersonId = null;
                ApplyFilter();
            }
        }
    }

    public MainWindowViewModel()
    {
        _currentPage = this;
        LoadLastFolderAsync();
    }

    private async void LoadLastFolderAsync()
    {
        try
        {
            var settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(settingsFile))
            {
                var path = await File.ReadAllTextAsync(settingsFile);
                if (Directory.Exists(path))
                {
                    _ = LoadPhotosFromFolderAsync(path);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load last folder: {ex.Message}");
        }
    }

    private async void SaveLastFolder(string path)
    {
        try
        {
            var settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
            await File.WriteAllTextAsync(settingsFile, path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save last folder: {ex.Message}");
        }
    }

    private void OnVideoEndReached()
    {
        IsPlaying = false;
        SetPositionInternal(0);
    }

    public void TogglePlayPause()
    {
        if (_currentPage is MainWindow window)
        {
            window.PlayPause_Requested();
        }
    }

    public void PlayVideo()
    {
    }

    public void PauseVideo()
    {
    }

    public DateTime _ignorePlaybackUpdatesUntil = DateTime.MinValue;

    public void SetPositionInternal(double seconds)
    {
        // Prevent seek jumping
        if (IsUserSeeking) return;

        _isUpdatingFromPlayback = true;
        _videoPositionSeconds = seconds;
        OnPropertyChanged(nameof(VideoPositionSeconds));
        OnPropertyChanged(nameof(VideoTimeDisplay));
        _isUpdatingFromPlayback = false;
    }

    public void SeekVideo(double seconds, bool isPrecise = false)
    {
        if (_currentPage is MainWindow window)
        {
            if (isPrecise)
            {
                // Precise seek
                _ignorePlaybackUpdatesUntil = DateTime.Now.AddMilliseconds(300);
                window.SeekVideoPrecise_Requested(seconds);
            }
            else
            {
                // Fast seek
                window.SeekVideo_Requested(seconds);
            }
        }
    }

    public void UpdatePositionForScrubbing(double seconds)
    {
        // Silent position update
        _videoPositionSeconds = seconds;
        OnPropertyChanged(nameof(VideoTimeDisplay));
    }

    public void CleanupVideo()
    {
        IsPlaying = false;
        VideoDurationSeconds = 0;
        SetPositionInternal(0);
    }

    public void ClearSelection()
    {
        if (SelectedPhoto != null)
        {
            var photoToClear = SelectedPhoto;
            SelectedPhoto = null;
            photoToClear.CleanupVideo();
            photoToClear.ClearFullImage();
            CleanupVideo();
            OnPropertyChanged(nameof(IsEmpty));

            // Rescan images
            _ = RescanCurrentFolderAsync();
        }
    }

    public async Task RescanCurrentFolderAsync()
    {
        if (string.IsNullOrEmpty(_currentFolderPath) || !Directory.Exists(_currentFolderPath)) return;

        var supported = new[] { ".jpg", ".jpeg", ".png", ".heic", ".webp", ".mp4", ".webm", ".mov", ".mkv", ".avi", ".ogv", ".flv" };
        var existingPaths = _allPhotos.Select(p => p.FilePath).ToHashSet();

        await Task.Run(() =>
        {
            var newFiles = Directory.EnumerateFiles(_currentFolderPath, "*", SearchOption.AllDirectories)
                .Where(f => supported.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !existingPaths.Contains(f))
                .ToList();

            if (newFiles.Count == 0) return;

            var batch = new List<PhotoItem>();
            foreach (var file in newFiles)
            {
                var dateTaken = MetadataService.GetOriginalDateTaken(file);
                var photo = new PhotoItem
                {
                    FilePath = file,
                    Title = Path.GetFileName(file),
                    DateTaken = dateTaken,
                    IsFavorite = FaceManagementService.IsFavorite(file)
                };

                _allPhotos.Add(photo);
                batch.Add(photo);

                if (batch.Count >= 50)
                {
                    var toAdd = batch.ToList();
                    batch.Clear();
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var p in toAdd)
                        {
                            if (SelectedSidebarCategory == "Library")
                                Photos.Add(p);
                        }
                        UpdateGroupedPhotos(toAdd);
                        OnPropertyChanged(nameof(PhotoCount));
                        OnPropertyChanged(nameof(IsEmpty));
                    });
                }
            }

            if (batch.Count > 0)
            {
                var toAdd = batch.ToList();
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var p in toAdd)
                    {
                        if (SelectedSidebarCategory == "Library")
                            Photos.Add(p);
                    }
                    UpdateGroupedPhotos(toAdd);
                    OnPropertyChanged(nameof(PhotoCount));
                    OnPropertyChanged(nameof(IsEmpty));
                });
            }
        });
    }

    public void SelectNextPhoto()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        int index = Photos.IndexOf(SelectedPhoto);
        if (index < Photos.Count - 1)
        {
            SelectedPhoto = Photos[index + 1];
        }
    }

    public void SelectPreviousPhoto()
    {
        if (SelectedPhoto == null || Photos.Count == 0) return;
        int index = Photos.IndexOf(SelectedPhoto);
        if (index > 0)
        {
            SelectedPhoto = Photos[index - 1];
        }
    }

    public void DeletePhoto(PhotoItem photo)
    {
        if (photo == null) return;

        int index = Photos.IndexOf(photo);
        bool wasSelected = SelectedPhoto == photo;

        TrashService.MoveToTrash(photo.FilePath);
        _allPhotos.Remove(photo);
        Photos.Remove(photo);

        foreach (var group in GroupedPhotos)
        {
            if (group.Photos.Remove(photo))
            {
                if (group.Photos.Count == 0)
                {
                    GroupedPhotos.Remove(group);
                }
                break;
            }
        }

        if (wasSelected)
        {
            if (Photos.Count > 0)
            {
                // Select next/prev
                int nextIndex = Math.Min(index, Photos.Count - 1);
                SelectedPhoto = Photos[nextIndex];
            }
            else
            {
                ClearSelection();
            }
        }

        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task LoadPhotosFromFolderAsync(string folderPath)
    {
        _currentFolderPath = folderPath;
        OnPropertyChanged(nameof(IsWhatsAppFolder));
        
        foreach (var p in _allPhotos)
        {
            p.UnloadThumbnail();
            p.ClearFullImage();
        }

        Photos.Clear();
        _allPhotos.Clear();
        GroupedPhotos.Clear();
        
        _albumPaths.Clear();
        AlbumItems.Clear();
        AlbumItems.Add("All Albums");

        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(IsEmpty));

        SaveLastFolder(folderPath);

        var supported = new[] { ".jpg", ".jpeg", ".png", ".heic", ".webp", ".mp4", ".webm", ".mov", ".mkv", ".avi", ".ogv", ".flv" };

        await Task.Run(() =>
        {
            // Find media subfolders
            var subfolders = Directory.GetDirectories(folderPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var sub in subfolders)
            {
                try
                {
                    bool hasMedia = Directory.EnumerateFiles(sub)
                        .Any(f => supported.Contains(Path.GetExtension(f).ToLowerInvariant()));
                    
                    if (hasMedia)
                    {
                        var name = Path.GetFileName(sub);
                        // Skip screenshots in Albums
                        if (name.Equals("Screenshots", StringComparison.OrdinalIgnoreCase)) continue;

                        _albumPaths[name] = sub;
                        Dispatcher.UIThread.Post(() => AlbumItems.Add(name));
                    }
                }
                catch { }
            }

            var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => supported.Contains(Path.GetExtension(f).ToLowerInvariant()));

            var batch = new List<PhotoItem>();
            foreach (var file in files)
            {
                var dateTaken = MetadataService.GetOriginalDateTaken(file);
                var photo = new PhotoItem
                {
                    FilePath = file,
                    Title = Path.GetFileName(file),
                    DateTaken = dateTaken,
                    IsFavorite = FaceManagementService.IsFavorite(file)
                };

                _allPhotos.Add(photo);
                batch.Add(photo);

                if (batch.Count >= 50)
                {
                    var toAdd = batch.ToList();
                    batch.Clear();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (SelectedSidebarCategory == "Library")
                        {
                            foreach (var p in toAdd) Photos.Add(p);
                            UpdateGroupedPhotos(toAdd);
                            OnPropertyChanged(nameof(PhotoCount));
                            OnPropertyChanged(nameof(IsEmpty));
                        }
                    });
                }
            }

            if (batch.Count > 0)
            {
                var toAdd = batch.ToList();
                Dispatcher.UIThread.Post(() =>
                {
                    if (SelectedSidebarCategory == "Library")
                    {
                        foreach (var p in toAdd) Photos.Add(p);
                        UpdateGroupedPhotos(toAdd);
                        OnPropertyChanged(nameof(PhotoCount));
                        OnPropertyChanged(nameof(IsEmpty));
                    }
                });
            }
        });
        Dispatcher.UIThread.Post(ApplyFilter);
    }

    private void UpdateGroupedPhotos(List<PhotoItem> newPhotos)
    {
        if (SelectedTimeView == "All Photos")
        {
            var allPhotosGroup = GroupedPhotos.FirstOrDefault();
            if (allPhotosGroup == null)
            {
                allPhotosGroup = new PhotoGroup { Date = DateTime.MinValue, Header = "All Photos" };
                GroupedPhotos.Add(allPhotosGroup);
            }
            foreach (var photo in newPhotos)
            {
                photo.YearText = "";
                allPhotosGroup.Photos.Add(photo);
            }
            return;
        }

        if (SelectedTimeView == "Years")
        {
            var yearsGroup = GroupedPhotos.FirstOrDefault();
            if (yearsGroup == null)
            {
                yearsGroup = new PhotoGroup { Date = DateTime.MinValue, Header = "Years" };
                GroupedPhotos.Add(yearsGroup);
            }
            foreach (var photo in newPhotos)
            {
                var yearString = photo.DateTaken.ToString("yyyy");
                if (!yearsGroup.Photos.Any(p => p.YearText == yearString))
                {
                    photo.YearText = yearString;
                    yearsGroup.Photos.Add(photo);
                }
                else
                {
                    photo.YearText = "";
                }
            }

            // Sort newest first
            var sortedYears = yearsGroup.Photos.OrderByDescending(p => p.DateTaken).ToList();
            yearsGroup.Photos.Clear();
            foreach (var p in sortedYears) yearsGroup.Photos.Add(p);

            return;
        }

        foreach (var photo in newPhotos)
        {
            photo.YearText = "";
            DateTime date;
            string header;

            // Months
            date = new DateTime(photo.DateTaken.Year, photo.DateTaken.Month, 1);
            header = photo.DateTaken.ToString("MMMM yyyy");

            var group = GroupedPhotos.FirstOrDefault(g => g.Date == date);

            if (group == null)
            {
                group = new PhotoGroup
                {
                    Date = date,
                    Header = header
                };

                var insertIndex = 0;
                for (int i = 0; i < GroupedPhotos.Count; i++)
                {
                    if (GroupedPhotos[i].Date > date)
                    {
                        insertIndex = i + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                GroupedPhotos.Insert(insertIndex, group);
            }

            group.Photos.Add(photo);
        }
    }

    private CancellationTokenSource? _filterCts;

    private async void ApplyFilter()
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        OnPropertyChanged(nameof(IsCollectionsView));
        OnPropertyChanged(nameof(IsPeopleView));
        OnPropertyChanged(nameof(IsGalleryView));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(CurrentCategoryName));

        if (IsPeopleView)
        {
            _ = LoadPeopleAsync();
            return;
        }

        var category = SelectedSidebarCategory;
        var personId = _selectedPersonId;
        var searchText = SearchText;
        var isCollectionsOrMap = category == "Collections" || category == "Map";
        var isAllAlbums = category == "All Albums";
        var isCollections = category == "Collections";

        try
        {
            await Task.Run(() =>
            {
                IEnumerable<PhotoItem> filtered;
                if (category == "People" && personId != null)
                {
                    var faces = FaceManagementService.GetFacesForPerson(personId);
                    var paths = faces.Select(f => f.SourcePhotoPath).ToHashSet();
                    filtered = _allPhotos.Where(p => paths.Contains(p.FilePath));
                }
                else if (isAllAlbums)
                {
                    filtered = Array.Empty<PhotoItem>();
                }
                else if (category != null && _albumPaths.ContainsKey(category))
                {
                    var albumPath = _albumPaths[category];
                    filtered = _allPhotos.Where(p => p.FilePath.StartsWith(albumPath, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    switch (category)
                    {
                        case "People": filtered = _allPhotos.Where(p => p.HasFaces); break;
                        case "Videos": filtered = _allPhotos.Where(p => p.IsVideo); break;
                        case "Favorites": filtered = _allPhotos.Where(p => p.IsFavorite); break;
                        case "Recently Saved": filtered = _allPhotos.OrderByDescending(p => p.DateTaken).Take(50); break;
                        case "Screenshots": filtered = _allPhotos.Where(p => p.FilePath.Contains("Screenshot", StringComparison.OrdinalIgnoreCase) || p.FilePath.Contains("Screen Shot", StringComparison.OrdinalIgnoreCase)); break;
                        case "Collections":
                            filtered = Array.Empty<PhotoItem>();
                            break;
                        default: filtered = _allPhotos; break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    filtered = filtered.Where(p => p.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                }

                // Sort newest first
                var filteredList = filtered.OrderByDescending(p => p.DateTaken).ToList();

                List<CollectionItem>? newCollections = null;
                if (isAllAlbums)
                {
                    newCollections = new List<CollectionItem>();
                    foreach (var kvp in _albumPaths)
                    {
                        var albumPhotos = _allPhotos.Where(p => p.FilePath.StartsWith(kvp.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                        newCollections.Add(new CollectionItem
                        {
                            Name = kvp.Key,
                            Icon = "All Albums",
                            AccentColor = "#8E8E93",
                            Photos = albumPhotos
                        });
                    }
                }
                else if (isCollections)
                {
                    newCollections = new List<CollectionItem>();
                    newCollections.Add(new CollectionItem
                    {
                        Name = "Favorites",
                        Icon = "Favorites",
                        AccentColor = "#FF3B30",
                        Photos = _allPhotos.Where(p => p.IsFavorite).ToList()
                    });
                    newCollections.Add(new CollectionItem
                    {
                        Name = "Recently Saved",
                        Icon = "Recently Saved",
                        AccentColor = "#007AFF",
                        Photos = _allPhotos.OrderByDescending(p => p.DateTaken).Take(50).ToList()
                    });
                    newCollections.Add(new CollectionItem
                    {
                        Name = "Videos",
                        Icon = "Videos",
                        AccentColor = "#5856D6",
                        Photos = _allPhotos.Where(p => p.IsVideo).ToList()
                    });
                    newCollections.Add(new CollectionItem
                    {
                        Name = "Screenshots",
                        Icon = "Screenshots",
                        AccentColor = "#34C759",
                        Photos = _allPhotos.Where(p => p.FilePath.Contains("Screenshot", StringComparison.OrdinalIgnoreCase) || p.FilePath.Contains("Screen Shot", StringComparison.OrdinalIgnoreCase)).ToList()
                    });
                    newCollections.Add(new CollectionItem
                    {
                        Name = "People",
                        Icon = "People",
                        AccentColor = "#FF9500",
                        Photos = _allPhotos.Where(p => p.HasFaces).ToList()
                    });
                }

                token.ThrowIfCancellationRequested();

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;

                    if (isCollectionsOrMap)
                    {
                        foreach (var p in _allPhotos)
                        {
                            p.UnloadThumbnail();
                        }
                    }
                    else
                    {
                        var filteredSet = filteredList.ToHashSet();
                        foreach (var p in _allPhotos.Where(p => !filteredSet.Contains(p)))
                        {
                            p.UnloadThumbnail();
                        }
                    }

                    if (newCollections != null)
                    {
                        Collections.Clear();
                        foreach (var c in newCollections)
                        {
                            Collections.Add(c);
                            // Async preview
                            var firstPhoto = c.Photos.FirstOrDefault();
                            if (firstPhoto != null)
                            {
                                _ = Task.Run(() => 
                                {
                                    var thumb = ImageLoaderService.LoadThumbnail(firstPhoto.FilePath, 400, CancellationToken.None);
                                    if (thumb != null)
                                    {
                                        Dispatcher.UIThread.Post(() => c.Preview = thumb);
                                    }
                                });
                            }
                        }
                        OnPropertyChanged(nameof(Collections));
                    }

                    Photos.Clear();
                    GroupedPhotos.Clear();

                    foreach (var p in filteredList) Photos.Add(p);
                    UpdateGroupedPhotos(filteredList);

                    OnPropertyChanged(nameof(PhotoCount));
                    OnPropertyChanged(nameof(IsEmpty));
                });
            }, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadPeopleAsync()
    {
        People.Clear();
        Photos.Clear();
        GroupedPhotos.Clear();
        
        var peopleData = FaceManagementService.GetPeople();
        foreach (var p in peopleData)
        {
            var vm = new PersonItemViewModel
            {
                Id = p.Id,
                Name = p.Name,
                FaceCount = p.FaceIds.Count
            };
            
            if (p.RepresentativeFaceId != null)
            {
                var face = FaceManagementService.GetFaceById(p.RepresentativeFaceId);
                if (face != null && File.Exists(face.FaceImagePath))
                {
                    vm.RepresentativeFace = await Task.Run(() => new Bitmap(face.FaceImagePath));
                }
            }
            People.Add(vm);
        }
        
        OnPropertyChanged(nameof(PhotoCount));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string? _selectedPersonId;

    public void ShowPersonPhotos(string personId)
    {
        _selectedPersonId = personId;
        ApplyFilter();
    }

    public bool IsFiltered => _selectedPersonId != null;

    public void GoBack()
    {
        if (_selectedPersonId != null)
        {
            _selectedPersonId = null;
            ApplyFilter();
        }
    }
}

public class PhotoItem : ViewModelBase
{
    private static readonly SemaphoreSlim _sem = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private static readonly string[] VideoExts =
        { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".ogv", ".flv" };

    private Bitmap? _thumb;
    private Bitmap? _full;
    private bool _faces;
    private bool _facesProcessed;
    private CancellationTokenSource? _loadCts;

    private double _zoomLevel = 1.0;

    private PhotoMetadata? _metadata;
    private bool _showInspector;

    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    
    private string _yearText = string.Empty;
    public string YearText { get => _yearText; set { SetProperty(ref _yearText, value); OnPropertyChanged(nameof(IsYearPreview)); } }
    public bool IsYearPreview => !string.IsNullOrEmpty(YearText);

    public DateTime DateTaken { get; set; }

    public PhotoMetadata? Metadata
    {
        get => _metadata;
        private set => SetProperty(ref _metadata, value);
    }

    public bool ShowInspector
    {
        get => _showInspector;
        set => SetProperty(ref _showInspector, value);
    }

    public bool HasCameraInfo => Metadata?.CameraMake != null || Metadata?.CameraModel != null;
    public bool HasExifInfo => Metadata?.Aperture != null || Metadata?.Iso != null;
    public string FileSizeFormatted => Metadata != null ? MetadataService.FormatFileSize(Metadata.FileSize) : "—";
    public string ShutterSpeedFormatted => MetadataService.FormatShutterSpeed(Metadata?.ShutterSpeed);
    public string DimensionsFormatted => Metadata != null ? $"{Metadata.Width} × {Metadata.Height}" : "—";

    public Bitmap? Thumbnail 
    { 
        get => _thumb; 
        set 
        {
            var old = _thumb;
            if (SetProperty(ref _thumb, value) && old != value)
            {
                old?.Dispose();
            }
        } 
    }

    public Bitmap? FullImage 
    { 
        get => _full; 
        set 
        {
            var old = _full;
            if (SetProperty(ref _full, value) && old != value)
            {
                old?.Dispose();
            }
        } 
    }
    public bool HasFaces { get => _faces; set => SetProperty(ref _faces, value); }
    private bool _isFavorite;
    public bool IsFavorite { get => _isFavorite; set => SetProperty(ref _isFavorite, value); }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetProperty(ref _zoomLevel, value);
    }

    public bool IsVideo => VideoExts.Contains(
        Path.GetExtension(FilePath).ToLowerInvariant());

    public void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    public void ZoomIn() => ZoomLevel = Math.Clamp(ZoomLevel * 1.15, 0.1, 5.0);
    public void ZoomOut() => ZoomLevel = Math.Clamp(ZoomLevel / 1.15, 0.1, 5.0);

    public async Task LoadImageAsync(MainWindowViewModel parentVm)
    {
        if (Thumbnail != null) return;

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            await _sem.WaitAsync(token);
            try
            {
                parentVm.ReportProgress(true, Title);
                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var thumb = ImageLoaderService.LoadThumbnail(FilePath, parentVm.ThumbnailWidth, token);
                    if (thumb != null)
                        Dispatcher.UIThread.Post(() => Thumbnail = thumb);

                    if (_facesProcessed || IsVideo) 
                    {
                        _facesProcessed = true;
                        return;
                    }

                    token.ThrowIfCancellationRequested();
                    
                    // Skip if in DB
                    if (FaceManagementService.HasProcessedImage(FilePath))
                    {
                        _facesProcessed = true;
                        var facesFound = FaceManagementService.HasFacesInImage(FilePath);
                        Dispatcher.UIThread.Post(() => HasFaces = facesFound);
                        return;
                    }

                    parentVm.ReportProgress(true, Title);
                    var detections = FaceDetectionService.DetectFaces(FilePath, token);
                    Dispatcher.UIThread.Post(() => HasFaces = detections.Length > 0);

                    _ = FaceManagementService.ProcessImageAsync(FilePath, token);
                    _facesProcessed = true;
                }, token);
                parentVm.ReportProgress(false);
            }
            catch { parentVm.ReportProgress(false); }
            finally { _sem.Release(); }
        }
        catch (OperationCanceledException) { }
    }

    public void CancelLoading() => _loadCts?.Cancel();

    public async Task LoadFullResolutionAsync()
    {
        if (FullImage != null) return;
        await _sem.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                var img = ImageLoaderService.LoadFullImage(FilePath, 1920);
                Dispatcher.UIThread.Post(() => FullImage = img);

                var meta = MetadataService.GetMetadata(FilePath);
                Dispatcher.UIThread.Post(() =>
                {
                    Metadata = meta;
                    OnPropertyChanged(nameof(HasCameraInfo));
                    OnPropertyChanged(nameof(HasExifInfo));
                    OnPropertyChanged(nameof(FileSizeFormatted));
                    OnPropertyChanged(nameof(ShutterSpeedFormatted));
                    OnPropertyChanged(nameof(DimensionsFormatted));
                });
            });
        }
        finally { _sem.Release(); }
    }

    public void ClearFullImage()
    {
        var old = FullImage;
        FullImage = null;
        old?.Dispose();
    }

    public void UnloadThumbnail()
    {
        _loadCts?.Cancel();
        var old = _thumb;
        Thumbnail = null;
        old?.Dispose();
    }

    public void CleanupVideo()
    {
    }
}
