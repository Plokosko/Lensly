using OpenCvSharp;

namespace Lensly.Models;

public class FaceDetectionResult
{
    public Rect BoundingBox { get; set; }
    public Point2f? LeftEye { get; set; }
    public Point2f? RightEye { get; set; }
}
