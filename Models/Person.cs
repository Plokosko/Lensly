using System;
using System.Collections.Generic;

namespace Lensly.Models;

public class Person
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Unknown Person";
    public List<string> FaceIds { get; set; } = new();
    public string? RepresentativeFaceId { get; set; }
}
