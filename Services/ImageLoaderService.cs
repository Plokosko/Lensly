using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Media.Imaging;
using ImageMagick;
using OpenCvSharp;

namespace Lensly.Services;

public static class ImageLoaderService
{
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi", ".ogv", ".flv" };

    public static Bitmap? LoadThumbnail(string filePath, int targetWidth, CancellationToken token)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            token.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (VideoExtensions.Contains(ext))
            {
                using var capture = new VideoCapture(filePath);
                if (!capture.IsOpened())
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open video: {filePath}");
                    return null;
                }

                var totalFrames = capture.Get(VideoCaptureProperties.FrameCount);
                if (totalFrames > 0 && totalFrames > 30)
                {
                    capture.Set(VideoCaptureProperties.PosFrames, Math.Min(30, totalFrames / 2));
                }

                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read frame from video: {filePath}");
                    return null;
                }

                token.ThrowIfCancellationRequested();

                // Validate dimensions
                if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 10000 || frame.Height > 10000)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid frame dimensions: {frame.Width}x{frame.Height}");
                    return null;
                }

                var ratio = (double)targetWidth / frame.Width;
                var targetHeight = (int)(frame.Height * ratio);

                using var resized = new Mat();
                Cv2.Resize(frame, resized, new Size(targetWidth, targetHeight));

                Cv2.ImEncode(".jpg", resized, out byte[] buf);
                if (buf == null || buf.Length == 0) return null;
                
                using var ms = new MemoryStream(buf);
                return new Bitmap(ms);
            }

            if (ext == ".heic")
            {
                // Decode at target size
                var settings = new MagickReadSettings { Width = (uint)targetWidth, Height = (uint)targetWidth };
                using var image = new MagickImage(filePath, settings);

                token.ThrowIfCancellationRequested();

                var ratio = (double)targetWidth / image.Width;
                // Cast to satisfy Magick.NET
                image.Resize((uint)targetWidth, (uint)(image.Height * ratio));
                image.Format = MagickFormat.Jpg;

                using var ms = new MemoryStream();
                image.Write(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }

            using var stream = File.OpenRead(filePath);
            return Bitmap.DecodeToWidth(stream, targetWidth);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadThumbnail failed for {filePath}: {ex.Message}");
            return null;
        }
    }

    public static Bitmap? LoadFullImage(string filePath, int maxDimension = 1920)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (VideoExtensions.Contains(ext))
            {
                using var capture = new VideoCapture(filePath);
                if (!capture.IsOpened())
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open video: {filePath}");
                    return null;
                }

                var totalFrames = capture.Get(VideoCaptureProperties.FrameCount);
                if (totalFrames > 0 && totalFrames > 30)
                {
                    capture.Set(VideoCaptureProperties.PosFrames, Math.Min(30, totalFrames / 2));
                }

                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read frame from video: {filePath}");
                    return null;
                }

                // Validate dimensions
                if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 10000 || frame.Height > 10000)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid frame dimensions: {frame.Width}x{frame.Height}");
                    return null;
                }

                if (frame.Width > maxDimension || frame.Height > maxDimension)
                {
                    var isLandscape = frame.Width > frame.Height;
                    var tWidth = isLandscape ? maxDimension : (int)(frame.Width * ((double)maxDimension / frame.Height));
                    var tHeight = isLandscape ? (int)(frame.Height * ((double)maxDimension / frame.Width)) : maxDimension;

                    using var resized = new Mat();
                    Cv2.Resize(frame, resized, new Size(tWidth, tHeight));
                    Cv2.ImEncode(".jpg", resized, out byte[] buf);
                    if (buf == null || buf.Length == 0) return null;
                    
                    using var ms = new MemoryStream(buf);
                    return new Bitmap(ms);
                }
                else
                {
                    Cv2.ImEncode(".jpg", frame, out byte[] buf);
                    if (buf == null || buf.Length == 0) return null;
                    
                    using var ms = new MemoryStream(buf);
                    return new Bitmap(ms);
                }
            }

            if (ext == ".heic")
            {
                using var image = new MagickImage(filePath);
                if (image.Width > maxDimension || image.Height > maxDimension)
                {
                    var isLandscape = image.Width > image.Height;
                    var tWidth = isLandscape ? (uint)maxDimension : (uint)(image.Width * ((double)maxDimension / image.Height));
                    var tHeight = isLandscape ? (uint)(image.Height * ((double)maxDimension / image.Width)) : (uint)maxDimension;
                    image.Resize(tWidth, tHeight);
                }
                image.Format = MagickFormat.Jpg;
                using var ms = new MemoryStream();
                image.Write(ms);
                ms.Position = 0;
                return new Bitmap(ms);
            }

            using var stream = File.OpenRead(filePath);
            return Bitmap.DecodeToWidth(stream, maxDimension);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadFullImage failed for {filePath}: {ex.Message}");
            return null;
        }
    }
}
