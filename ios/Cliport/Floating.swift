// Cliport v2.0 (iOS) - floating window (Picture in Picture).
// While its small window floats above other apps, iOS treats Cliport as on-screen: it keeps running and may use the
// pasteboard, so copying syncs in both directions without opening the app. The window can be pushed to the screen edge.
import AVKit
import AVFoundation
import CoreMedia
import CoreVideo
import SwiftUI
import UIKit

final class Floating: NSObject, AVPictureInPictureControllerDelegate, AVPictureInPictureSampleBufferPlaybackDelegate {
    static let shared = Floating()
    let layer = AVSampleBufferDisplayLayer()
    private var controller: AVPictureInPictureController?
    private var timer: Timer?
    private var drawn = ""
    private(set) var active = false                  // the window is floating right now
    var text = "Cliport"
    var supported: Bool { AVPictureInPictureController.isPictureInPictureSupported() }
    var enabled: Bool { controller != nil }

    func enable() {
        guard supported, controller == nil else { return }
        do {
            let s = AVAudioSession.sharedInstance()
            try s.setCategory(.playback, mode: .default, options: [.mixWithOthers])
            try s.setActive(true)
        } catch { Log.write("floating: audio session \(error.localizedDescription)") }
        layer.videoGravity = .resizeAspect
        let c = AVPictureInPictureController(contentSource: .init(sampleBufferDisplayLayer: layer, playbackDelegate: self))
        c.delegate = self
        c.canStartPictureInPictureAutomaticallyFromInline = true      // starts by itself when the user leaves the app
        c.requiresLinearPlayback = true                               // no skip buttons
        controller = c
        drawn = ""
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.frame() }
        frame()
        Log.write("floating window enabled")
    }
    func disable() {
        guard controller != nil else { return }
        timer?.invalidate(); timer = nil
        controller?.stopPictureInPicture()
        controller = nil; active = false
        layer.flushAndRemoveImage()
        Log.write("floating window disabled")
    }
    func startNow() { if let c = controller, !c.isPictureInPictureActive { c.startPictureInPicture() } }

    /// One still picture per second keeps the "video" alive; it is redrawn only when the text changes.
    private var lastBuffer: CVPixelBuffer?
    private func frame() {
        let w = 480, h = 120
        if drawn != text || lastBuffer == nil {
            var pb: CVPixelBuffer?
            let attrs: [CFString: Any] = [kCVPixelBufferCGImageCompatibilityKey: true, kCVPixelBufferCGBitmapContextCompatibilityKey: true,
                                          kCVPixelBufferIOSurfacePropertiesKey: [:] as [String: Any]]
            guard CVPixelBufferCreate(kCFAllocatorDefault, w, h, kCVPixelFormatType_32BGRA, attrs as CFDictionary, &pb) == kCVReturnSuccess, let buf = pb else { return }
            CVPixelBufferLockBaseAddress(buf, [])
            if let ctx = CGContext(data: CVPixelBufferGetBaseAddress(buf), width: w, height: h, bitsPerComponent: 8, bytesPerRow: CVPixelBufferGetBytesPerRow(buf),
                                   space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue) {
                ctx.translateBy(x: 0, y: CGFloat(h)); ctx.scaleBy(x: 1, y: -1)
                UIGraphicsPushContext(ctx)
                UIColor(red: 0.31, green: 0.27, blue: 0.90, alpha: 1).setFill()
                ctx.fill(CGRect(x: 0, y: 0, width: w, height: h))
                let style = NSMutableParagraphStyle(); style.alignment = .center
                (text as NSString).draw(in: CGRect(x: 8, y: 38, width: w - 16, height: 50),
                                        withAttributes: [.font: UIFont.boldSystemFont(ofSize: 34), .foregroundColor: UIColor.white, .paragraphStyle: style])
                UIGraphicsPopContext()
            }
            CVPixelBufferUnlockBaseAddress(buf, [])
            lastBuffer = buf; drawn = text
        }
        guard let buf = lastBuffer else { return }
        var fmt: CMVideoFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buf, formatDescriptionOut: &fmt)
        guard let f = fmt else { return }
        var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 1), presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()), decodeTimeStamp: .invalid)
        var sb: CMSampleBuffer?
        CMSampleBufferCreateReadyWithImageBuffer(allocator: kCFAllocatorDefault, imageBuffer: buf, formatDescription: f, sampleTiming: &timing, sampleBufferOut: &sb)
        guard let s = sb else { return }
        if let arr = CMSampleBufferGetSampleAttachmentsArray(s, createIfNecessary: true), CFArrayGetCount(arr) > 0 {
            let d = unsafeBitCast(CFArrayGetValueAtIndex(arr, 0), to: CFMutableDictionary.self)
            CFDictionarySetValue(d, Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(), Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }
        if layer.status == .failed { layer.flush() }
        layer.enqueue(s)
    }

    // ---------- playback delegate: a live, endless, always-playing stream ----------
    func pictureInPictureController(_ pictureInPictureController: AVPictureInPictureController, setPlaying playing: Bool) { }
    func pictureInPictureControllerTimeRangeForPlayback(_ pictureInPictureController: AVPictureInPictureController) -> CMTimeRange {
        CMTimeRange(start: .negativeInfinity, duration: .positiveInfinity)
    }
    func pictureInPictureControllerIsPlaybackPaused(_ pictureInPictureController: AVPictureInPictureController) -> Bool { false }
    func pictureInPictureController(_ pictureInPictureController: AVPictureInPictureController, didTransitionToRenderSize newRenderSize: CMVideoDimensions) { }
    func pictureInPictureController(_ pictureInPictureController: AVPictureInPictureController, skipByInterval skipInterval: CMTime, completion completionHandler: @escaping () -> Void) {
        completionHandler()
    }

    // ---------- controller delegate ----------
    func pictureInPictureControllerDidStartPictureInPicture(_ pictureInPictureController: AVPictureInPictureController) {
        active = true; Log.write("floating window started")
    }
    func pictureInPictureControllerDidStopPictureInPicture(_ pictureInPictureController: AVPictureInPictureController) {
        active = false; Log.write("floating window stopped")
    }
    func pictureInPictureController(_ pictureInPictureController: AVPictureInPictureController, failedToStartPictureInPictureWithError error: Error) {
        active = false; Log.write("floating window failed to start: \(error.localizedDescription)")
    }
}

/// The on-screen strip the floating window grows out of. It has to be visible when the user leaves the app.
struct FloatingStrip: UIViewRepresentable {
    final class Host: UIView {
        override func layoutSubviews() {
            super.layoutSubviews()
            CATransaction.begin(); CATransaction.setDisableActions(true)
            Floating.shared.layer.frame = bounds
            CATransaction.commit()
        }
    }
    func makeUIView(context: Context) -> Host {
        let v = Host()
        v.backgroundColor = UIColor(red: 0.31, green: 0.27, blue: 0.90, alpha: 1)
        v.layer.addSublayer(Floating.shared.layer)
        return v
    }
    func updateUIView(_ uiView: Host, context: Context) { }
}
