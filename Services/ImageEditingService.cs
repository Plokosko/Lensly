using System;
using System.Runtime.InteropServices;

namespace Lensly.Services;

public class ImageEditingService
{
    [DllImport("libLenslyNative.dylib")]
    private static extern bool Lensly_ApplyEdit(
        [MarshalAs(UnmanagedType.LPStr)] string inputPath,
        [MarshalAs(UnmanagedType.LPStr)] string outputPath,
        float brightness,
        float contrast,
        float rotation,
        float scaleX,
        float scaleY,
        float cropX,
        float cropY,
        float cropW,
        float cropH,
        [MarshalAs(UnmanagedType.LPStr)] string? filterName,
        float maxDimension
    );

    public bool ApplyEdit(
        string inputPath,
        string outputPath,
        float brightness = 0,
        float contrast = 1,
        float rotation = 0,
        float scaleX = 1,
        float scaleY = 1,
        float cropX = 0,
        float cropY = 0,
        float cropW = 0,
        float cropH = 0,
        string? filterName = null,
        float maxDimension = 0)
    {
        try
        {
            return Lensly_ApplyEdit(
                inputPath,
                outputPath,
                brightness,
                contrast,
                rotation,
                scaleX,
                scaleY,
                cropX,
                cropY,
                cropW,
                cropH,
                filterName,
                maxDimension
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying edit: {ex.Message}");
            return false;
        }
    }
}
