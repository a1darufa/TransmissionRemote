// Draws the background of the disk image window: an arrow from the app to the Applications
// folder, and a note on approving the first launch, since releases aren't notarized.
//
// Usage: swift make-background.swift <output.png> <scale>
// The layout must match the icon positions in style-window.applescript.
import AppKit

let windowSize = CGSize(width: 640, height: 400)
/// Icon centres in Finder's coordinates (origin at the top left).
let appIconCenter = CGPoint(x: 160, y: 190)
let applicationsIconCenter = CGPoint(x: 480, y: 190)

let arguments = CommandLine.arguments
guard arguments.count == 3, let scale = Double(arguments[2]), scale > 0 else {
    FileHandle.standardError.write(Data("Usage: swift make-background.swift <output.png> <scale>\n".utf8))
    exit(1)
}
let outputURL = URL(fileURLWithPath: arguments[1])

/// AppKit draws from the bottom left; Finder places icons from the top left.
func flipped(_ point: CGPoint) -> CGPoint {
    CGPoint(x: point.x, y: windowSize.height - point.y)
}

func drawBackground() {
    let gradient = NSGradient(
        starting: NSColor(white: 1, alpha: 1),
        ending: NSColor(srgbRed: 0.91, green: 0.92, blue: 0.94, alpha: 1)
    )
    gradient?.draw(in: CGRect(origin: .zero, size: windowSize), angle: -90)
}

func drawArrow() {
    let start = flipped(CGPoint(x: appIconCenter.x + 95, y: appIconCenter.y))
    let end = flipped(CGPoint(x: applicationsIconCenter.x - 95, y: applicationsIconCenter.y))
    let headLength = 22.0

    let arrow = NSBezierPath()
    arrow.move(to: start)
    arrow.line(to: CGPoint(x: end.x - 4, y: end.y))
    arrow.move(to: CGPoint(x: end.x - headLength, y: end.y + headLength))
    arrow.line(to: end)
    arrow.line(to: CGPoint(x: end.x - headLength, y: end.y - headLength))
    arrow.lineWidth = 9
    arrow.lineCapStyle = .round
    arrow.lineJoinStyle = .round
    NSColor(white: 0.55, alpha: 1).setStroke()
    arrow.stroke()
}

func drawText(
    _ text: String, size: CGFloat, weight: NSFont.Weight, color: NSColor, centeredAt finderY: CGFloat
) {
    let paragraph = NSMutableParagraphStyle()
    paragraph.alignment = .center
    let attributed = NSAttributedString(string: text, attributes: [
        .font: NSFont.systemFont(ofSize: size, weight: weight),
        .foregroundColor: color,
        .paragraphStyle: paragraph
    ])
    let height = attributed.boundingRect(
        with: CGSize(width: windowSize.width - 60, height: .greatestFiniteMagnitude),
        options: [.usesLineFragmentOrigin]
    ).height
    let origin = flipped(CGPoint(x: 30, y: finderY + height / 2))
    attributed.draw(in: CGRect(x: origin.x, y: origin.y, width: windowSize.width - 60, height: height))
}

let pixelsWide = Int(windowSize.width * scale)
let pixelsHigh = Int(windowSize.height * scale)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: pixelsWide, pixelsHigh: pixelsHigh,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fatalError("Couldn't create a \(pixelsWide)×\(pixelsHigh) bitmap")
}
bitmap.size = windowSize // Marks the 2x image as Retina, so Finder doesn't show it twice as large.

NSGraphicsContext.current = context
context.cgContext.scaleBy(x: scale, y: scale)
drawBackground()
drawArrow()
drawText(
    "Drag Transmission Remote into Applications",
    size: 17, weight: .semibold, color: NSColor(white: 0.2, alpha: 1), centeredAt: 52
)
drawText(
    """
    This free app isn't notarized by Apple. The first time you open it, go to
    System Settings → Privacy & Security and click Open Anyway.
    """,
    size: 12, weight: .regular, color: NSColor(white: 0.4, alpha: 1), centeredAt: 345
)
NSGraphicsContext.current = nil

guard let png = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("Couldn't encode the background as PNG")
}
try png.write(to: outputURL)
