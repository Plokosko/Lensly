import Foundation
import CoreImage
import AppKit

@_cdecl("Lensly_ApplyEdit")
public func Lensly_ApplyEdit(
    inputPath: UnsafePointer<Int8>,
    outputPath: UnsafePointer<Int8>,
    brightness: Float,
    contrast: Float,
    rotation: Float,
    scaleX: Float,
    scaleY: Float,
    cropX: Float,
    cropY: Float,
    cropW: Float,
    cropH: Float,
    filterName: UnsafePointer<Int8>?,
    maxDimension: Float
) -> Bool {
    let inputStr = String(cString: inputPath)
    let outputStr = String(cString: outputPath)
    
    guard let inputURL = URL(string: "file://" + inputStr),
          let ciImage = CIImage(contentsOf: inputURL) else {
        return false
    }
    
    var resultImage = ciImage
    
    // downscale preview
    if maxDimension > 0 {
        let extent = resultImage.extent
        let scale = CGFloat(maxDimension) / max(extent.width, extent.height)
        if scale < 1.0 {
            resultImage = resultImage.transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        }
    }
    
    // brightness/contrast
    if let filter = CIFilter(name: "CIColorControls") {
        filter.setValue(resultImage, forKey: kCIInputImageKey)
        filter.setValue(brightness, forKey: kCIInputBrightnessKey)
        filter.setValue(contrast, forKey: kCIInputContrastKey)
        if let output = filter.outputImage {
            resultImage = output
        }
    }
    
    // filters
    if let filterNamePtr = filterName {
        let name = String(cString: filterNamePtr)
        if !name.isEmpty, let filter = CIFilter(name: name) {
            filter.setValue(resultImage, forKey: kCIInputImageKey)
            if let output = filter.outputImage {
                resultImage = output
            }
        }
    }
    
    // crop
    if cropW > 0 && cropH > 0 {
        let cropRect = CGRect(x: CGFloat(cropX), y: CGFloat(cropY), width: CGFloat(cropW), height: CGFloat(cropH))
        resultImage = resultImage.cropped(to: cropRect)
        resultImage = resultImage.transformed(by: CGAffineTransform(translationX: -CGFloat(cropX), y: -CGFloat(cropY)))
    }
    
    // rotate
    if rotation != 0 {
        let angle = CGFloat(rotation) * .pi / 180.0
        let extent = resultImage.extent
        let transform = CGAffineTransform(translationX: extent.midX, y: extent.midY)
            .rotated(by: angle)
            .translatedBy(x: -extent.midX, y: -extent.midY)
        resultImage = resultImage.transformed(by: transform)
    }
    
    // scale
    if (scaleX != 1.0 || scaleY != 1.0) && scaleX > 0 && scaleY > 0 {
        resultImage = resultImage.transformed(by: CGAffineTransform(scaleX: CGFloat(scaleX), y: CGFloat(scaleY)))
    }
    
    let context = CIContext(options: nil)
    guard let cgImage = context.createCGImage(resultImage, from: resultImage.extent) else {
        return false
    }
    
    let nsImage = NSImage(cgImage: cgImage, size: NSSize(width: cgImage.width, height: cgImage.height))
    guard let tiffData = nsImage.tiffRepresentation,
          let bitmapImage = NSBitmapImageRep(data: tiffData),
          let jpegData = bitmapImage.representation(using: .jpeg, properties: [.compressionFactor: 0.8]) else {
        return false
    }
    
    do {
        try jpegData.write(to: URL(fileURLWithPath: outputStr))
        return true
    } catch {
        return false
    }
}
