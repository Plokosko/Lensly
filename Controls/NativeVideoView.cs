using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Lensly.Controls;

public class NativeVideoView : NativeControlHost
{
    [DllImport("libLenslyNative.dylib")]
    private static extern IntPtr Lensly_CreatePlayerView();

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_DestroyPlayerView(IntPtr viewPtr);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_LoadVideo(IntPtr viewPtr, [MarshalAs(UnmanagedType.LPStr)] string path);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_Play(IntPtr viewPtr);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_Pause(IntPtr viewPtr);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_SetVolume(IntPtr viewPtr, float volume);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_Seek(IntPtr viewPtr, double seconds);

    [DllImport("libLenslyNative.dylib")]
    private static extern void Lensly_SeekPrecise(IntPtr viewPtr, double seconds);

    [DllImport("libLenslyNative.dylib")]
    private static extern double Lensly_GetDuration(IntPtr viewPtr);

    [DllImport("libLenslyNative.dylib")]
    private static extern double Lensly_GetCurrentTime(IntPtr viewPtr);

    [DllImport("libLenslyNative.dylib")]
    private static extern bool Lensly_IsPlaying(IntPtr viewPtr);

    private IntPtr _viewPtr = IntPtr.Zero;

    public static readonly DirectProperty<NativeVideoView, string?> FilePathProperty =
        AvaloniaProperty.RegisterDirect<NativeVideoView, string?>(
            nameof(FilePath),
            o => o.FilePath,
            (o, v) => o.FilePath = v);

    private string? _filePath;
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetAndRaise(FilePathProperty, ref _filePath, value))
            {
                if (_viewPtr != IntPtr.Zero && !string.IsNullOrEmpty(value))
                {
                    Lensly_LoadVideo(_viewPtr, value);
                    Lensly_Play(_viewPtr);
                }
            }
        }
    }

    public void Play() { if (_viewPtr != IntPtr.Zero) Lensly_Play(_viewPtr); }
    public void Pause() { if (_viewPtr != IntPtr.Zero) Lensly_Pause(_viewPtr); }
    public void SetVolume(float volume) { if (_viewPtr != IntPtr.Zero) Lensly_SetVolume(_viewPtr, volume); }
    public void Seek(double seconds) { if (_viewPtr != IntPtr.Zero) Lensly_Seek(_viewPtr, seconds); }
    public void SeekPrecise(double seconds) { if (_viewPtr != IntPtr.Zero) Lensly_SeekPrecise(_viewPtr, seconds); }
    public double GetDuration() => _viewPtr != IntPtr.Zero ? Lensly_GetDuration(_viewPtr) : 0;
    public double GetCurrentTime() => _viewPtr != IntPtr.Zero ? Lensly_GetCurrentTime(_viewPtr) : 0;
    public bool IsPlaying() => _viewPtr != IntPtr.Zero && Lensly_IsPlaying(_viewPtr);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _viewPtr = Lensly_CreatePlayerView();
        
        if (!string.IsNullOrEmpty(FilePath))
        {
            Lensly_LoadVideo(_viewPtr, FilePath);
            Lensly_Play(_viewPtr);
        }

        return new PlatformHandle(_viewPtr, "NSView");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_viewPtr != IntPtr.Zero)
        {
            Lensly_DestroyPlayerView(_viewPtr);
            _viewPtr = IntPtr.Zero;
        }
        base.DestroyNativeControlCore(control);
    }
}
