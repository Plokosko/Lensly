import Foundation
import Vision
import ImageIO

if CommandLine.arguments.count < 2 {
    print("[]")
    exit(0)
}

let imagePath = CommandLine.arguments[1]
let fileURL = URL(fileURLWithPath: imagePath)

let options: [CFString: Any] = [
    kCGImageSourceShouldCache: false,
    kCGImageSourceCreateThumbnailFromImageAlways: true,
    kCGImageSourceCreateThumbnailWithTransform: true,
    kCGImageSourceThumbnailMaxPixelSize: 1280
]

guard let imageSource = CGImageSourceCreateWithURL(fileURL as CFURL, nil),
      let cgImage = CGImageSourceCreateThumbnailAtIndex(imageSource, 0, options as CFDictionary),
      let properties = CGImageSourceCopyPropertiesAtIndex(imageSource, 0, nil) as? [CFString: Any] else {
    print("[]")
    exit(0)
}

var pixelWidth = properties[kCGImagePropertyPixelWidth] as? CGFloat ?? 0
var pixelHeight = properties[kCGImagePropertyPixelHeight] as? CGFloat ?? 0

if let orientation = properties[kCGImagePropertyOrientation] as? UInt32, orientation >= 5 && orientation <= 8 {
    let temp = pixelWidth
    pixelWidth = pixelHeight
    pixelHeight = temp
}

let handler = VNImageRequestHandler(cgImage: cgImage, options: [:])
let request = VNDetectFaceLandmarksRequest()

do {
    try handler.perform([request])
    guard let observations = request.results else {
        print("[]")
        exit(0)
    }
    
    var results: [[String: Any]] = []
    
    for face in observations {
        let bbox = face.boundingBox
        
        let x = bbox.origin.x * pixelWidth
        let width = bbox.size.width * pixelWidth
        let height = bbox.size.height * pixelHeight
        let y = pixelHeight - (bbox.origin.y * pixelHeight) - height
        
        var faceDict: [String: Any] = [
            "x": x,
            "y": y,
            "width": width,
            "height": height
        ]
        
        if let landmarks = face.landmarks {
            if let leftEyePts = landmarks.leftEye?.normalizedPoints, !leftEyePts.isEmpty,
               let rightEyePts = landmarks.rightEye?.normalizedPoints, !rightEyePts.isEmpty {
                
                var leX: CGFloat = 0, leY: CGFloat = 0
                for p in leftEyePts { leX += p.x; leY += p.y }
                leX /= CGFloat(leftEyePts.count)
                leY /= CGFloat(leftEyePts.count)
                
                var reX: CGFloat = 0, reY: CGFloat = 0
                for p in rightEyePts { reX += p.x; reY += p.y }
                reX /= CGFloat(rightEyePts.count)
                reY /= CGFloat(rightEyePts.count)
                
                let leftEyePixelX = (bbox.origin.x + leX * bbox.size.width) * pixelWidth
                let leftEyePixelY = pixelHeight - ((bbox.origin.y + leY * bbox.size.height) * pixelHeight)
                
                let rightEyePixelX = (bbox.origin.x + reX * bbox.size.width) * pixelWidth
                let rightEyePixelY = pixelHeight - ((bbox.origin.y + reY * bbox.size.height) * pixelHeight)
                
                faceDict["leftEyeX"] = leftEyePixelX
                faceDict["leftEyeY"] = leftEyePixelY
                faceDict["rightEyeX"] = rightEyePixelX
                faceDict["rightEyeY"] = rightEyePixelY
            }
        }
        results.append(faceDict)
    }
    
    let jsonData = try JSONSerialization.data(withJSONObject: results, options: [])
    if let jsonString = String(data: jsonData, encoding: .utf8) {
        print(jsonString)
    }
} catch {
    print("[]")
}
