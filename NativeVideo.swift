import AppKit
import AVKit
import AVFoundation

@_cdecl("Lensly_CreatePlayerView")
public func Lensly_CreatePlayerView() -> UnsafeMutableRawPointer {
    let view = AVPlayerView()
    view.controlsStyle = .none
    view.layer = CALayer()
    view.wantsLayer = true
    view.layer?.cornerRadius = 20
    view.layer?.masksToBounds = true
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("Lensly_DestroyPlayerView")
public func Lensly_DestroyPlayerView(viewPtr: UnsafeMutableRawPointer) {
    let _ = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeRetainedValue()
}

@_cdecl("Lensly_LoadVideo")
public func Lensly_LoadVideo(viewPtr: UnsafeMutableRawPointer, path: UnsafePointer<Int8>) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    let url = URL(fileURLWithPath: String(cString: path))
    let player = AVPlayer(url: url)
    view.player = player
}

@_cdecl("Lensly_Play")
public func Lensly_Play(viewPtr: UnsafeMutableRawPointer) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    view.player?.play()
}

@_cdecl("Lensly_Pause")
public func Lensly_Pause(viewPtr: UnsafeMutableRawPointer) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    view.player?.pause()
}

@_cdecl("Lensly_SetVolume")
public func Lensly_SetVolume(viewPtr: UnsafeMutableRawPointer, volume: Float) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    view.player?.volume = volume
}

@_cdecl("Lensly_Seek")
public func Lensly_Seek(viewPtr: UnsafeMutableRawPointer, seconds: Double) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    let time = CMTime(seconds: seconds, preferredTimescale: 600)
    // Positive infinity tolerance is much faster for scrubbing (keyframe seeking)
    view.player?.seek(to: time, toleranceBefore: .positiveInfinity, toleranceAfter: .positiveInfinity)
}

@_cdecl("Lensly_SeekPrecise")
public func Lensly_SeekPrecise(viewPtr: UnsafeMutableRawPointer, seconds: Double) {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    let time = CMTime(seconds: seconds, preferredTimescale: 600)
    // Zero tolerance for precise final positioning
    view.player?.seek(to: time, toleranceBefore: .zero, toleranceAfter: .zero)
}

@_cdecl("Lensly_GetDuration")
public func Lensly_GetDuration(viewPtr: UnsafeMutableRawPointer) -> Double {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    return view.player?.currentItem?.duration.seconds ?? 0
}

@_cdecl("Lensly_GetCurrentTime")
public func Lensly_GetCurrentTime(viewPtr: UnsafeMutableRawPointer) -> Double {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    return view.player?.currentTime().seconds ?? 0
}

@_cdecl("Lensly_IsPlaying")
public func Lensly_IsPlaying(viewPtr: UnsafeMutableRawPointer) -> Bool {
    let view = Unmanaged<AVPlayerView>.fromOpaque(viewPtr).takeUnretainedValue()
    return view.player?.timeControlStatus == .playing
}
