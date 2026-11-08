import Foundation

/// Transmission's JSON varies between releases: a field may be missing on older servers,
/// `wanted` was `0`/`1` before 4.1, and 4.1.0 briefly sent speed limits as doubles.
/// These helpers decode what's there and fall back to a sensible default otherwise,
/// so one odd field never hides a whole torrent list.
protocol LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(from container: KeyedDecodingContainer<Key>, forKey key: Key) -> Self?
}

extension KeyedDecodingContainer {
    func lenient<Value: LenientDecodable>(_ key: Key) -> Value? {
        Value.decodeLeniently(from: self, forKey: key)
    }

    func lenient<Value: LenientDecodable>(_ key: Key, default defaultValue: Value) -> Value {
        Value.decodeLeniently(from: self, forKey: key) ?? defaultValue
    }

    /// A Unix timestamp; Transmission uses `0` for "never".
    func lenientDate(_ key: Key) -> Date? {
        guard let seconds: Int64 = lenient(key), seconds > 0 else { return nil }
        return Date(timeIntervalSince1970: TimeInterval(seconds))
    }

    func lenientArray<Element: Decodable>(_ key: Key, of type: Element.Type = Element.self) -> [Element] {
        (try? decodeIfPresent([Element].self, forKey: key)) ?? []
    }

    func lenientValue<Value: Decodable>(_ key: Key, as type: Value.Type = Value.self) -> Value? {
        try? decodeIfPresent(Value.self, forKey: key)
    }
}

extension Int: LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(from container: KeyedDecodingContainer<Key>, forKey key: Key) -> Int? {
        Int64.decodeLeniently(from: container, forKey: key).map { Int(clamping: $0) }
    }
}

extension Int64: LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(
        from container: KeyedDecodingContainer<Key>,
        forKey key: Key
    ) -> Int64? {
        if let value = try? container.decodeIfPresent(Int64.self, forKey: key) { return value }
        if let value = try? container.decodeIfPresent(Double.self, forKey: key), value.isFinite {
            return Int64(exactly: value.rounded()) ?? 0
        }
        if let value = try? container.decodeIfPresent(Bool.self, forKey: key) { return value ? 1 : 0 }
        return nil
    }
}

extension Double: LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(
        from container: KeyedDecodingContainer<Key>,
        forKey key: Key
    ) -> Double? {
        if let value = try? container.decodeIfPresent(Double.self, forKey: key) { return value }
        if let value = try? container.decodeIfPresent(Int64.self, forKey: key) { return Double(value) }
        return nil
    }
}

extension Bool: LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(from container: KeyedDecodingContainer<Key>, forKey key: Key) -> Bool? {
        if let value = try? container.decodeIfPresent(Bool.self, forKey: key) { return value }
        if let value = try? container.decodeIfPresent(Int64.self, forKey: key) { return value != 0 }
        return nil
    }
}

extension String: LenientDecodable {
    static func decodeLeniently<Key: CodingKey>(
        from container: KeyedDecodingContainer<Key>,
        forKey key: Key
    ) -> String? {
        try? container.decodeIfPresent(String.self, forKey: key)
    }
}
