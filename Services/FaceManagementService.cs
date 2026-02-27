using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lensly.Models;
using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lensly.Services;

public class FaceManagementService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lensly");
    
    private static readonly string FacesDir = Path.Combine(DataDir, "Faces");
    private static readonly string PeopleFile = Path.Combine(DataDir, "people.json");
    private static readonly string FacesFile = Path.Combine(DataDir, "faces.json");
    private static readonly string AnalyzedFile = Path.Combine(DataDir, "analyzed.json");
    private static readonly string FavoritesFile = Path.Combine(DataDir, "favorites.json");

    private static List<Person> _people = new();
    private static List<FaceRecord> _faces = new();
    private static HashSet<string> _analyzedPaths = new();
    private static HashSet<string> _favorites = new();
    private static readonly object _lock = new();
    
    private static InferenceSession? _session;

    static FaceManagementService()
    {
        if (!Directory.Exists(FacesDir)) Directory.CreateDirectory(FacesDir);
        LoadData();
        
        try
        {
            var modelPath = Path.Combine(AppContext.BaseDirectory, "Models", "arcface_r50.onnx");
            if (File.Exists(modelPath))
            {
                var options = new SessionOptions();
                // Use CPU for ArcFace compatibility
                _session = new InferenceSession(modelPath, options);
                System.Diagnostics.Debug.WriteLine("FaceManagementService: ArcFace model loaded successfully.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FaceManagementService ERROR: Model not found at {modelPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FaceManagementService ERROR: Failed to load model: {ex.Message}");
        }
    }

    private static void LoadData()
    {
        lock (_lock)
        {
            if (File.Exists(PeopleFile))
            {
                try { _people = JsonSerializer.Deserialize<List<Person>>(File.ReadAllText(PeopleFile)) ?? new(); }
                catch { _people = new(); }
            }
            if (File.Exists(FacesFile))
            {
                try { _faces = JsonSerializer.Deserialize<List<FaceRecord>>(File.ReadAllText(FacesFile)) ?? new(); }
                catch { _faces = new(); }
            }
            if (File.Exists(AnalyzedFile))
            {
                try { _analyzedPaths = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(AnalyzedFile)) ?? new(); }
                catch { _analyzedPaths = new(); }
            }
            if (File.Exists(FavoritesFile))
            {
                try { _favorites = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FavoritesFile)) ?? new(); }
                catch { _favorites = new(); }
            }
        }
    }

    private static void SaveData()
    {
        lock (_lock)
        {
            File.WriteAllText(PeopleFile, JsonSerializer.Serialize(_people));
            File.WriteAllText(FacesFile, JsonSerializer.Serialize(_faces));
            File.WriteAllText(AnalyzedFile, JsonSerializer.Serialize(_analyzedPaths));
            File.WriteAllText(FavoritesFile, JsonSerializer.Serialize(_favorites));
        }
    }

    public static bool IsFavorite(string filePath)
    {
        lock (_lock)
        {
            return _favorites.Contains(filePath);
        }
    }

    public static void ToggleFavorite(string filePath)
    {
        lock (_lock)
        {
            if (_favorites.Contains(filePath)) _favorites.Remove(filePath);
            else _favorites.Add(filePath);
            SaveData();
        }
    }

    public static void RenamePerson(string personId, string newName)
    {
        lock (_lock)
        {
            var person = _people.FirstOrDefault(p => p.Id == personId);
            if (person != null)
            {
                person.Name = newName;
                SaveData();
            }
        }
    }

    public static List<Person> GetPeople() => _people.ToList();
    public static List<FaceRecord> GetFacesForPerson(string personId)
    {
        var person = _people.FirstOrDefault(p => p.Id == personId);
        if (person == null) return new();
        return _faces.Where(f => person.FaceIds.Contains(f.Id)).ToList();
    }

    public static FaceRecord? GetFaceById(string faceId) => _faces.FirstOrDefault(f => f.Id == faceId);

    public static bool HasProcessedImage(string filePath)
    {
        lock (_lock)
        {
            return _analyzedPaths.Contains(filePath);
        }
    }

    public static bool HasFacesInImage(string filePath)
    {
        lock (_lock)
        {
            return _faces.Any(f => f.SourcePhotoPath == filePath);
        }
    }

    public static async Task ProcessImageAsync(string filePath, CancellationToken token = default)
    {
        if (HasProcessedImage(filePath)) return;

        var detections = FaceDetectionService.DetectFaces(filePath, token);
        
        lock (_lock)
        {
            _analyzedPaths.Add(filePath);
        }

        if (detections.Length == 0)
        {
            SaveData();
            return;
        }

        System.Diagnostics.Debug.WriteLine($"FaceManagementService: Found {detections.Length} faces in {Path.GetFileName(filePath)}");

        // Downscale for faster cropping
        using var tempSrc = new Mat(filePath, ImreadModes.Color);
        if (tempSrc.Empty()) return;

        double scale = 1.0;
        int maxDim = 1600;
        using var src = new Mat();
        if (tempSrc.Width > maxDim || tempSrc.Height > maxDim)
        {
            scale = Math.Min((double)maxDim / tempSrc.Width, (double)maxDim / tempSrc.Height);
            Cv2.Resize(tempSrc, src, new Size(0, 0), scale, scale);
        }
        else
        {
            tempSrc.CopyTo(src);
        }

        foreach (var detection in detections)
        {
            token.ThrowIfCancellationRequested();
            
            // Map to scaled coords
            var rect = new Rect(
                (int)(detection.BoundingBox.X * scale),
                (int)(detection.BoundingBox.Y * scale),
                (int)(detection.BoundingBox.Width * scale),
                (int)(detection.BoundingBox.Height * scale)
            );

            var scaledDetection = new FaceDetectionResult
            {
                BoundingBox = rect
            };
            if (detection.LeftEye.HasValue) 
                scaledDetection.LeftEye = new Point2f(detection.LeftEye.Value.X * (float)scale, detection.LeftEye.Value.Y * (float)scale);
            if (detection.RightEye.HasValue)
                scaledDetection.RightEye = new Point2f(detection.RightEye.Value.X * (float)scale, detection.RightEye.Value.Y * (float)scale);

            // Square bbox for thumbnails
            int centerX = rect.X + rect.Width / 2;
            int centerY = rect.Y + rect.Height / 2;
            int size = (int)(Math.Max(rect.Width, rect.Height) * 1.4); 
            int halfSize = size / 2;
            
            halfSize = Math.Min(halfSize, centerX);
            halfSize = Math.Min(halfSize, centerY);
            halfSize = Math.Min(halfSize, src.Width - centerX);
            halfSize = Math.Min(halfSize, src.Height - centerY);
            
            if (halfSize <= 0) continue;

            var cropRect = new Rect(centerX - halfSize, centerY - halfSize, halfSize * 2, halfSize * 2);

            // ArcFace alignment
            using var alignedFace = AlignFace(src, scaledDetection);
            
            float[]? embedding = null;
            if (_session != null && !alignedFace.Empty())
            {
                embedding = ExtractEmbedding(alignedFace);
            }

            using var uiThumbMat = new Mat(src, cropRect);
            using var normalizedFace = new Mat();
            Cv2.Resize(uiThumbMat, normalizedFace, new Size(128, 128));

            var faceId = Guid.NewGuid().ToString();
            var facePath = Path.Combine(FacesDir, $"{faceId}.jpg");
            normalizedFace.SaveImage(facePath);

            var faceRecord = new FaceRecord
            {
                Id = faceId,
                FaceImagePath = facePath,
                SourcePhotoPath = filePath,
                BoundingBox = detection.BoundingBox, // Keep original bbox
                Embedding = embedding
            };

            lock (_lock)
            {
                _faces.Add(faceRecord);
                
                var matchingPerson = FindMatch(embedding);
                if (matchingPerson != null)
                {
                    matchingPerson.FaceIds.Add(faceId);
                }
                else
                {
                    var person = new Person
                    {
                        Name = "Unnamed Person",
                        FaceIds = new List<string> { faceId },
                        RepresentativeFaceId = faceId
                    };
                    _people.Add(person);
                }
            }
        }

        SaveData();
    }

    private static float[]? ExtractEmbedding(Mat face)
    {
        if (_session == null) return null;

        // Input: [1, 3, 112, 112]
        using var resized = new Mat();
        Cv2.Resize(face, resized, new Size(112, 112));
        
        var tensor = new DenseTensor<float>(new[] { 1, 3, 112, 112 });
        for (int y = 0; y < 112; y++)
        {
            for (int x = 0; x < 112; x++)
            {
                var color = resized.At<Vec3b>(y, x);
                // Normalize pixels
                tensor[0, 0, y, x] = (color.Item2 - 127.5f) / 128.0f; // R
                tensor[0, 1, y, x] = (color.Item1 - 127.5f) / 128.0f; // G
                tensor[0, 2, y, x] = (color.Item0 - 127.5f) / 128.0f; // B
            }
        }

        try
        {
            // InsightFace input names
            string inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "data";
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
            
            using var results = _session.Run(inputs);
            var output = results.First().AsEnumerable<float>().ToArray();

            // L2 normalize
            double norm = 0;
            foreach (var val in output) norm += val * val;
            norm = Math.Sqrt(norm);
            if (norm > 0)
            {
                for (int i = 0; i < output.Length; i++) output[i] = (float)(output[i] / norm);
            }

            return output;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FaceManagementService ERROR: Inference failed: {ex.Message}");
            return null;
        }
    }

    private static Mat AlignFace(Mat src, FaceDetectionResult detection)
    {
        if (detection.LeftEye == null || detection.RightEye == null)
        {
            var rect = detection.BoundingBox;
            int aiPadding = (int)(rect.Width * 0.2);
            var aiCropRect = new Rect(
                Math.Max(0, rect.X - aiPadding),
                Math.Max(0, rect.Y - aiPadding),
                Math.Min(src.Width - rect.X, rect.Width + 2 * aiPadding),
                Math.Min(src.Height - rect.Y, rect.Height + 2 * aiPadding)
            );
            return new Mat(src, aiCropRect);
        }

        Point2f leftEye = detection.LeftEye.Value;
        Point2f rightEye = detection.RightEye.Value;
        Point2f eyeCenter = new Point2f((leftEye.X + rightEye.X) / 2.0f, (leftEye.Y + rightEye.Y) / 2.0f);
        
        double dy = rightEye.Y - leftEye.Y;
        double dx = rightEye.X - leftEye.X;
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        int outputSize = 112;
        double desiredLeftEyeX = 0.35;
        double desiredRightEyeX = 0.65; 
        double desiredEyeY = 0.40;

        double dist = Math.Sqrt(dx * dx + dy * dy);
        double desiredDist = (desiredRightEyeX - desiredLeftEyeX) * outputSize;
        double scale = desiredDist / dist;

        Mat rotMat = Cv2.GetRotationMatrix2D(eyeCenter, angle, scale);
        rotMat.Set<double>(0, 2, rotMat.At<double>(0, 2) + (outputSize * 0.5 - eyeCenter.X));
        rotMat.Set<double>(1, 2, rotMat.At<double>(1, 2) + (outputSize * desiredEyeY - eyeCenter.Y));

        Mat alignedFace = new Mat();
        Cv2.WarpAffine(src, alignedFace, rotMat, new Size(outputSize, outputSize), InterpolationFlags.Cubic);
        return alignedFace;
    }

    public static void DeletePerson(string personId)
    {
        lock (_lock)
        {
            var person = _people.FirstOrDefault(p => p.Id == personId);
            if (person != null)
            {
                _people.Remove(person);
                SaveData();
            }
        }
    }

    private static Person? FindMatch(float[]? newEmbedding)
    {
        if (newEmbedding == null || newEmbedding.Length == 0) return null;

        Person? bestMatch = null;
        // ArcFace 512D Cosine Similarity threshold. 
        // With perfect alignment, 0.45-0.5 is very strict.
        double bestSimilarity = 0.45; 

        foreach (var person in _people)
        {
            if (person.FaceIds.Count == 0) continue;

            var facesToCheck = person.FaceIds.Skip(Math.Max(0, person.FaceIds.Count - 25)).ToList();
            if (person.RepresentativeFaceId != null && !facesToCheck.Contains(person.RepresentativeFaceId))
            {
                facesToCheck.Add(person.RepresentativeFaceId);
            }

            var embeddings = new List<float[]>();
            foreach (var fId in facesToCheck)
            {
                var faceRecord = _faces.FirstOrDefault(f => f.Id == fId);
                if (faceRecord?.Embedding != null && faceRecord.Embedding.Length == newEmbedding.Length)
                {
                    embeddings.Add(faceRecord.Embedding);
                }
            }

            if (embeddings.Count == 0) continue;

            float[] centroid = new float[newEmbedding.Length];
            for (int i = 0; i < centroid.Length; i++)
            {
                float sum = 0;
                foreach (var emb in embeddings) sum += emb[i];
                centroid[i] = sum / embeddings.Count;
            }

            double norm = 0;
            foreach (var val in centroid) norm += val * val;
            norm = Math.Sqrt(norm);
            if (norm > 0)
            {
                for (int i = 0; i < centroid.Length; i++) centroid[i] = (float)(centroid[i] / norm);
            }

            double similarity = 0.0;
            for (int i = 0; i < newEmbedding.Length; i++)
            {
                similarity += newEmbedding[i] * centroid[i];
            }
            
            if (similarity > bestSimilarity) 
            {
                bestSimilarity = similarity;
                bestMatch = person;
            }
        }
        return bestMatch;
    }

    public static void MergePeople(string sourcePersonId, string targetPersonId)
    {
        lock (_lock)
        {
            var source = _people.FirstOrDefault(p => p.Id == sourcePersonId);
            var target = _people.FirstOrDefault(p => p.Id == targetPersonId);
            
            if (source != null && target != null && source != target)
            {
                target.FaceIds.AddRange(source.FaceIds);
                _people.Remove(source);
                SaveData();
            }
        }
    }

    public static void PurgeDatabase()
    {
        lock (_lock)
        {
            _people.Clear();
            _faces.Clear();
            _analyzedPaths.Clear();
            _favorites.Clear();

            try
            {
                if (Directory.Exists(FacesDir))
                {
                    foreach (var file in Directory.GetFiles(FacesDir))
                    {
                        File.Delete(file);
                    }
                }
                if (File.Exists(PeopleFile)) File.Delete(PeopleFile);
                if (File.Exists(FacesFile)) File.Delete(FacesFile);
                if (File.Exists(AnalyzedFile)) File.Delete(AnalyzedFile);
                if (File.Exists(FavoritesFile)) File.Delete(FavoritesFile);
            }
            catch { }
            
            SaveData();
        }
    }

    public static void ClusterPeople()
    {
        lock (_lock)
        {
            if (_faces.Count == 0) 
            {
                System.Diagnostics.Debug.WriteLine("FaceManagementService: No faces to cluster.");
                return;
            }

            var validFaces = _faces.Where(f => f.Embedding != null).ToList();
            System.Diagnostics.Debug.WriteLine($"FaceManagementService: Clustering {validFaces.Count} faces with DBSCAN...");
            
            if (validFaces.Count == 0) return;

            // DBSCAN distance threshold
            double eps = 0.65; 
            int minPts = 1;

            int[] labels = new int[validFaces.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;

            int clusterId = 0;
            for (int i = 0; i < validFaces.Count; i++)
            {
                if (labels[i] != -1) continue;

                var neighbors = GetNeighbors(i, validFaces, eps);
                if (neighbors.Count < minPts)
                {
                    labels[i] = -2; // Noise
                    continue;
                }

                labels[i] = clusterId;
                var queue = new Queue<int>(neighbors);
                while (queue.Count > 0)
                {
                    int next = queue.Dequeue();
                    if (labels[next] == -2) labels[next] = clusterId;
                    if (labels[next] != -1) continue;

                    labels[next] = clusterId;
                    var nextNeighbors = GetNeighbors(next, validFaces, eps);
                    if (nextNeighbors.Count >= minPts)
                    {
                        foreach (var n in nextNeighbors) queue.Enqueue(n);
                    }
                }
                clusterId++;
            }

            var newPeople = new List<Person>();
            var clusterGroups = labels.Select((label, index) => new { label, index })
                                      .Where(x => x.label >= 0)
                                      .GroupBy(x => x.label);

            foreach (var group in clusterGroups)
            {
                var faceIds = group.Select(x => validFaces[x.index].Id).ToList();
                // Persist names
                var existingNames = _people.Where(p => p.FaceIds.Intersect(faceIds).Any())
                                          .Select(p => p.Name)
                                          .Where(n => n != "Unnamed Person")
                                          .Distinct()
                                          .ToList();

                var person = new Person
                {
                    Name = existingNames.FirstOrDefault() ?? "Unnamed Person",
                    FaceIds = faceIds,
                    RepresentativeFaceId = faceIds.First()
                };
                newPeople.Add(person);
            }

            System.Diagnostics.Debug.WriteLine($"FaceManagementService: DBSCAN finished. Created {newPeople.Count} people.");
            _people = newPeople;
            SaveData();
        }
    }

    private static List<int> GetNeighbors(int index, List<FaceRecord> faces, double eps)
    {
        var neighbors = new List<int>();
        var emb1 = faces[index].Embedding!;
        for (int i = 0; i < faces.Count; i++)
        {
            var emb2 = faces[i].Embedding!;
            double similarity = 0;
            for (int k = 0; k < emb1.Length; k++) similarity += emb1[k] * emb2[k];
            if (1.0 - similarity <= eps) neighbors.Add(i);
        }
        return neighbors;
    }
}
