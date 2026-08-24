
// GaGa for macOS.
// Draws the application icon and builds AppIcon.icns next to this file.
//
//   swift Icon/make-icon.swift

import AppKit

let sizes = [16, 32, 64, 128, 256, 512, 1024]

/// The antenna from the menu bar, so that the two read as the same app.
let symbolName = "antenna.radiowaves.left.and.right"

func gradient() -> NSGradient {
    NSGradient(colors: [
        NSColor(srgbRed: 0.45, green: 0.36, blue: 0.95, alpha: 1),
        NSColor(srgbRed: 0.16, green: 0.11, blue: 0.55, alpha: 1),
    ])!
}

func draw(size: Int) -> NSImage {
    let side = CGFloat(size)
    let image = NSImage(size: NSSize(width: side, height: side))

    image.lockFocus()
    NSGraphicsContext.current?.imageInterpolation = .high

    // the rounded square, inset the way macOS app icons are
    let inset = side * 0.09
    let rect = NSRect(x: inset, y: inset, width: side - inset * 2, height: side - inset * 2)
    let radius = rect.width * 0.225
    let squircle = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)

    gradient().draw(in: squircle, angle: -90)

    // a soft highlight across the top, so it does not look flat
    squircle.setClip()
    NSGradient(colors: [
        NSColor(white: 1, alpha: 0.22),
        NSColor(white: 1, alpha: 0),
    ])!.draw(in: NSRect(x: rect.minX, y: rect.midY, width: rect.width, height: rect.height / 2),
             angle: -90)

    // the antenna, tinted white
    let glyphSide = side * 0.52
    let configuration = NSImage.SymbolConfiguration(pointSize: glyphSide, weight: .medium)

    if let symbol = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)?
        .withSymbolConfiguration(configuration) {

        let white = NSImage(size: symbol.size, flipped: false) { bounds in
            symbol.draw(in: bounds)
            NSColor.white.set()
            bounds.fill(using: .sourceAtop)
            return true
        }

        let target = NSRect(
            x: (side - white.size.width) / 2,
            y: (side - white.size.height) / 2,
            width: white.size.width,
            height: white.size.height
        )

        white.draw(in: target)
    }

    image.unlockFocus()
    return image
}

func png(_ image: NSImage, side: Int) -> Data {
    let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!

    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    image.draw(in: NSRect(x: 0, y: 0, width: side, height: side))
    NSGraphicsContext.restoreGraphicsState()

    return rep.representation(using: .png, properties: [:])!
}

let here = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
let iconset = here.appendingPathComponent("AppIcon.iconset")

try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

// each size is drawn at its own resolution, so small ones stay crisp
var rendered: [Int: Data] = [:]
for size in sizes {
    rendered[size] = png(draw(size: size), side: size)
}

let names: [(String, Int)] = [
    ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024),
]

for (name, size) in names {
    try rendered[size]!.write(to: iconset.appendingPathComponent(name))
}

let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path,
                      "-o", here.appendingPathComponent("AppIcon.icns").path]
try iconutil.run()
iconutil.waitUntilExit()

try? FileManager.default.removeItem(at: iconset)
print(iconutil.terminationStatus == 0 ? "wrote AppIcon.icns" : "iconutil failed")
