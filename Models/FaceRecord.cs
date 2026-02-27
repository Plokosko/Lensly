using System;
using OpenCvSharp;

namespace Lensly.Models;

public class FaceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FaceImagePath { get; set; } = string.Empty;
    public string SourcePhotoPath { get; set; } = string.Empty;
    public Rect BoundingBox { get; set; }
    public float[]? Embedding { get; set; }
}
