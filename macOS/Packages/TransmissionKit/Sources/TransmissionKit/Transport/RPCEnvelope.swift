import Foundation

/// A string-backed coding key used to rename keys on the fly.
struct AnyCodingKey: CodingKey, ExpressibleByStringLiteral {
    let stringValue: String
    let intValue: Int?

    init(_ string: String) {
        stringValue = string
        intValue = nil
    }

    init(stringLiteral value: String) { self.init(value) }
    init?(stringValue: String) { self.init(stringValue) }

    init?(intValue: Int) {
        stringValue = String(intValue)
        self.intValue = intValue
    }
}

extension CodingUserInfoKey {
    /// Lets individual values (e.g. the encryption mode) pick a dialect-specific spelling.
    static let rpcDialect: CodingUserInfoKey = {
        guard let key = CodingUserInfoKey(rawValue: "TransmissionKit.rpcDialect") else {
            preconditionFailure("CodingUserInfoKey accepts any non-empty raw value")
        }
        return key
    }()
}

/// Wraps request parameters in the envelope the chosen dialect expects.
///
/// JSON-RPC: `{"jsonrpc": "2.0", "method": "...", "params": {...}, "id": 1}`
/// Legacy:   `{"method": "...", "arguments": {...}, "tag": 1}`
struct RequestEnvelope<Params: Encodable>: Encodable {
    let dialect: RPCDialect
    let method: RPCMethod
    let params: Params
    let tag: Int

    func encode(to encoder: any Encoder) throws {
        var container = encoder.container(keyedBy: AnyCodingKey.self)
        try container.encode(dialect.wireName(for: method), forKey: "method")
        switch dialect {
        case .jsonRPC:
            try container.encode("2.0", forKey: "jsonrpc")
            try container.encode(params, forKey: "params")
            try container.encode(tag, forKey: "id")
        case .legacy:
            try container.encode(params, forKey: "arguments")
            try container.encode(tag, forKey: "tag")
        }
    }
}

/// Placeholder for requests without parameters and responses without a body.
public struct Empty: Codable, Sendable, Equatable {
    public init() {}
}

struct JSONRPCResponse<Result: Decodable>: Decodable {
    let result: Result?
    let error: JSONRPCError?
}

struct JSONRPCError: Decodable {
    /// Transmission puts its own, more specific message here.
    struct Details: Decodable {
        let errorString: String?
    }

    let code: Int
    let message: String
    let data: Details?
}

struct LegacyResponse<Result: Decodable>: Decodable {
    /// `"success"` or a human-readable error message.
    let result: String
    let arguments: Result?
}

/// Builds encoders/decoders that translate between our camelCase models and the wire format.
enum RPCCoding {
    static func encoder(for dialect: RPCDialect, scope: KeyScope) -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.userInfo[.rpcDialect] = dialect
        encoder.keyEncodingStrategy = .custom { codingPath in
            let key = codingPath.last?.stringValue ?? ""
            // Depth 1 is the envelope itself (method/params/id), which is spelled the same everywhere.
            guard codingPath.count > 1 else { return AnyCodingKey(key) }
            return AnyCodingKey(dialect.wireName(for: key, scope: scope))
        }
        return encoder
    }

    static func decoder(for dialect: RPCDialect) -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.userInfo[.rpcDialect] = dialect
        decoder.keyDecodingStrategy = .custom { codingPath in
            AnyCodingKey(KeyCase.camelCase(codingPath.last?.stringValue ?? ""))
        }
        return decoder
    }
}
