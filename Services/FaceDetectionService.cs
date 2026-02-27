using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Lensly.Models;
using OpenCvSharp;

namespace Lensly.Services;

public static class FaceDetectionService
{
    private static readonly string AppleVisionPath = Path.Combine(AppContext.BaseDirectory, "AppleVision");

    public static FaceDetectionResult[] DetectFaces(string filePath, CancellationToken token = default)
    {
        if (!File.Exists(filePath) || !File.Exists(AppleVisionPath)) 
            return Array.Empty<FaceDetectionResult>();

        try
        {
            token.ThrowIfCancellationRequested();
            
            var psi = new ProcessStartInfo
            {
                FileName = AppleVisionPath,
                Arguments = $"\"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return Array.Empty<FaceDetectionResult>();
            
            // 3s timeout
            // Check cancellation
            bool exited = false;
            using (var registration = token.Register(() => { try { process.Kill(); } catch { } }))
            {
                exited = process.WaitForExit(3000); // 3s timeout
            }

            if (!exited)
            {
                try { process.Kill(); } catch { }
                System.Diagnostics.Debug.WriteLine($"FaceDetectionService: Timeout detecting faces in {Path.GetFileName(filePath)}");
                return Array.Empty<FaceDetectionResult>();
            }

            if (token.IsCancellationRequested) return Array.Empty<FaceDetectionResult>();
            
            string output = process.StandardOutput.ReadToEnd();
            
            var json = JsonDocument.Parse(output);
            var results = new List<FaceDetectionResult>();
            
            foreach (var element in json.RootElement.EnumerateArray())
            {
                double x = element.GetProperty("x").GetDouble();
                double y = element.GetProperty("y").GetDouble();
                double width = element.GetProperty("width").GetDouble();
                double height = element.GetProperty("height").GetDouble();
                
                var result = new FaceDetectionResult
                {
                    BoundingBox = new Rect((int)x, (int)y, (int)width, (int)height)
                };
                
                if (element.TryGetProperty("leftEyeX", out var leX) &&
                    element.TryGetProperty("leftEyeY", out var leY) &&
                    element.TryGetProperty("rightEyeX", out var reX) &&
                    element.TryGetProperty("rightEyeY", out var reY))
                {
                    result.LeftEye = new Point2f((float)leX.GetDouble(), (float)leY.GetDouble());
                    result.RightEye = new Point2f((float)reX.GetDouble(), (float)reY.GetDouble());
                }
                
                results.Add(result);
            }
            
            return results.ToArray();
        }
        catch
        {
            return Array.Empty<FaceDetectionResult>(); 
        }
    }

    public static bool ImageHasFaces(string filePath, CancellationToken token = default)
    {
        return DetectFaces(filePath, token).Length > 0;
    }
}