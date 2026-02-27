using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using OpenCvSharp;

namespace Lensly.Services;

public class PhotoMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime DateTaken { get; set; }
    public DateTime DateModified { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? FocalLength { get; set; }
    public double? Aperture { get; set; }
    public double? ShutterSpeed { get; set; }
    public int? Iso { get; set; }
    public string? Software { get; set; }
}

public static class MetadataService
{
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".ogv", ".flv" };
    
    public static PhotoMetadata GetMetadata(string filePath)
    {
        var metadata = new PhotoMetadata
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            DateModified = File.GetLastWriteTime(filePath)
        };

        try
        {
            var fileInfo = new FileInfo(filePath);
            metadata.FileSize = fileInfo.Length;
            
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isVideo = VideoExtensions.Contains(ext);

            if (isVideo)
            {
                // Parse video with OpenCV
                try
                {
                    using var capture = new VideoCapture(filePath);
                    if (capture.IsOpened())
                    {
                        metadata.Width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                        metadata.Height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                    }
                }
                catch { }
            }
            else
            {
                // Parse image metadata
                var directories = ImageMetadataReader.ReadMetadata(filePath);

            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var ifd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var jpegDirectory = directories.OfType<JpegDirectory>().FirstOrDefault();

            if (subIfdDirectory != null)
            {
                if (subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                {
                    metadata.DateTaken = dateTaken;
                }

                metadata.CameraMake = subIfdDirectory.GetString(ExifDirectoryBase.TagMake);
                metadata.CameraModel = subIfdDirectory.GetString(ExifDirectoryBase.TagModel);
                metadata.LensModel = subIfdDirectory.GetString(ExifDirectoryBase.TagLensModel);
                metadata.Software = subIfdDirectory.GetString(ExifDirectoryBase.TagSoftware);

                if (subIfdDirectory.ContainsTag(ExifDirectoryBase.TagFocalLength))
                {
                    var focalLength = subIfdDirectory.GetRational(ExifDirectoryBase.TagFocalLength);
                    metadata.FocalLength = (double)focalLength.Numerator / focalLength.Denominator;
                }

                if (subIfdDirectory.ContainsTag(ExifDirectoryBase.TagFNumber))
                {
                    var aperture = subIfdDirectory.GetRational(ExifDirectoryBase.TagFNumber);
                    metadata.Aperture = (double)aperture.Numerator / aperture.Denominator;
                }

                var iso = subIfdDirectory.GetInt32(ExifDirectoryBase.TagIsoEquivalent);
                if (iso > 0)
                {
                    metadata.Iso = iso;
                }

                var shutterSpeed = subIfdDirectory.GetString(ExifDirectoryBase.TagExposureTime);
                if (!string.IsNullOrEmpty(shutterSpeed))
                {
                    if (double.TryParse(shutterSpeed.Split('/').FirstOrDefault(), out var num) &&
                        double.TryParse(shutterSpeed.Split('/').LastOrDefault(), out var den) && den > 0)
                    {
                        metadata.ShutterSpeed = num / den;
                    }
                    else if (double.TryParse(shutterSpeed, out var speed))
                    {
                        metadata.ShutterSpeed = speed;
                    }
                }
            }

            if (ifd0Directory != null)
            {
                if (string.IsNullOrEmpty(metadata.CameraMake))
                    metadata.CameraMake = ifd0Directory.GetString(ExifDirectoryBase.TagMake);
                if (string.IsNullOrEmpty(metadata.CameraModel))
                    metadata.CameraModel = ifd0Directory.GetString(ExifDirectoryBase.TagModel);
            }

            if (jpegDirectory != null)
            {
                metadata.Width = jpegDirectory.GetImageWidth();
                metadata.Height = jpegDirectory.GetImageHeight();
            }
            
            // Fallback directories
            if (metadata.Width == 0 || metadata.Height == 0)
            {
                // PNG fallback
                var pngDirectory = directories.OfType<MetadataExtractor.Formats.Png.PngDirectory>().FirstOrDefault();
                if (pngDirectory != null)
                {
                    try
                    {
                        metadata.Width = pngDirectory.GetInt32(MetadataExtractor.Formats.Png.PngDirectory.TagImageWidth);
                        metadata.Height = pngDirectory.GetInt32(MetadataExtractor.Formats.Png.PngDirectory.TagImageHeight);
                    }
                    catch { }
                }
            }
            }
        }
        catch { }
        
        // ImageMagick fallback for dimensions
        if (metadata.Width == 0 || metadata.Height == 0)
        {
            try
            {
                using var image = new ImageMagick.MagickImage(filePath);
                metadata.Width = (int)image.Width;
                metadata.Height = (int)image.Height;
            }
            catch { }
        }

        if (metadata.DateTaken == default)
        {
            metadata.DateTaken = File.GetCreationTime(filePath);
        }

        return metadata;
    }

    public static DateTime GetOriginalDateTaken(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var subIfdDirectory = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            if (subIfdDirectory != null && subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
            {
                return dateTaken;
            }
        }
        catch { }

        return File.GetCreationTime(filePath);
    }

    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    public static string FormatShutterSpeed(double? shutterSpeed)
    {
        if (!shutterSpeed.HasValue) return "—";
        if (shutterSpeed.Value >= 1)
            return $"{shutterSpeed.Value:0.##}s";
        return $"1/{(int)(1 / shutterSpeed.Value)}s";
    }
}
