import Foundation

/// The speeds offered in speed-limit menus, like Transmission Remote GUI's "Speed limit menu items".
public enum SpeedLimitPresets {
    /// Transmission Remote GUI's default list, in kB/s. 0 stands for unlimited.
    public static let defaultList = "0,10,25,50,100,250,500,750,1000,2500,5000,7000"

    /// The speeds in a comma-separated list, smallest first. Duplicates and anything that isn't
    /// a whole, non-negative number are dropped, so a typo never breaks the menu.
    public static func parse(_ text: String) -> [Int] {
        let speeds = text
            .split { $0 == "," || $0 == ";" || $0.isWhitespace }
            .compactMap { Int($0) }
            .filter { $0 >= 0 }
        return Set(speeds).sorted()
    }
}
