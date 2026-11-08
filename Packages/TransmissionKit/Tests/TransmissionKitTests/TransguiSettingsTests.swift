import Foundation
import Testing
@testable import TransmissionKit

@Suite("Importing servers from transgui.ini")
struct TransguiSettingsTests {
    /// A made-up transgui.ini in the shape transgui 5.18 writes. The passwords are fake:
    /// "c2VjcmV0" is base64 for "secret", "cHJveHlwYXNz" for "proxypass".
    private let ini = """
        [Hosts]
        CurHost=Home NAS
        Count=2
        Host1=Home NAS
        Host2=Seedbox

        [Interface]
        MaxFoldersHistory=30

        [Connection.Home NAS]
        Host=192.168.1.20
        UseSSL=0
        Autoreconnect=1
        Port=9091
        UserName=admin
        Password=c2VjcmV0
        UseProxy=0
        PathMap=/mnt/HD/HD_a2/Torrents=/Volumes/Torrents|/mnt/HD/HD_a2/Movies=/Volumes/Movies
        DownSpeeds=0,50,100,500,1000
        UpSpeeds=0,10,50,100

        [Connection.Seedbox]
        Host=seedbox.example.com
        UseSSL=1
        Port=443
        RpcPath=/torrents/rpc
        UserName=me
        Password=-
        CertFile=/Users/me/client.pem
        UseProxy=1
        UseSockProxy=1
        ProxyHost=proxy.example.com
        ProxyPort=1080
        ProxyUser=proxyuser
        ProxyPass=cHJveHlwYXNz

        [AddTorrent.Home NAS]
        FolderCount=2
        Folder0=/mnt/HD/HD_a2/Torrents/Movies
        FolHit0=12
        FolExt0=
        LastDt0=03.11.2026
        Folder1=/mnt/HD/HD_a2/Torrents/Linux
        FolHit1=3
        LastDt1=garbage
        """

    private var settings: TransguiSettings { TransguiSettings(iniText: ini) }

    @Test func readsTheConnectionList() {
        #expect(settings.connections.map(\.name) == ["Home NAS", "Seedbox"])
        #expect(settings.activeConnectionName == "Home NAS")
        #expect(settings.folderHistoryLimit == 30)
    }

    @Test func readsAPlainHTTPServer() throws {
        let nas = try #require(settings.connections.first)
        #expect(nas.host == "192.168.1.20")
        #expect(nas.port == 9091)
        #expect(nas.rpcPath == "/transmission/rpc")
        #expect(!nas.usesTLS)
        #expect(nas.username == "admin")
        #expect(nas.password == "secret")
        #expect(nas.proxy == nil)
        #expect(nas.downloadSpeeds == "0,50,100,500,1000")
        #expect(nas.pathMappings == [
            .init(remotePath: "/mnt/HD/HD_a2/Torrents", localPath: "/Volumes/Torrents"),
            .init(remotePath: "/mnt/HD/HD_a2/Movies", localPath: "/Volumes/Movies")
        ])
    }

    @Test func readsAnHTTPSServerBehindAProxy() throws {
        let seedbox = try #require(settings.connections.last)
        #expect(seedbox.usesTLS)
        #expect(seedbox.port == 443)
        #expect(seedbox.rpcPath == "/torrents/rpc")
        #expect(seedbox.password == nil, "\"-\" means transgui asks every time")
        #expect(seedbox.hasClientCertificate)
        #expect(seedbox.proxy == .init(
            host: "proxy.example.com", port: 1080, usesSOCKS5: true, username: "proxyuser", password: "proxypass"
        ))
    }

    @Test func readsTheFolderHistory() throws {
        let folders = try #require(settings.connections.first?.folders)
        #expect(folders.map(\.path) == ["/mnt/HD/HD_a2/Torrents/Movies", "/mnt/HD/HD_a2/Torrents/Linux"])
        #expect(folders.map(\.useCount) == [12, 3])
        let lastUsed = try #require(folders.first?.lastUsed)
        let day = Calendar(identifier: .gregorian).dateComponents([.year, .month, .day], from: lastUsed)
        #expect(day == DateComponents(year: 2026, month: 11, day: 3))
        #expect(folders.last?.lastUsed == nil)
    }

    @Test func readsTheOldSingleConnectionFormat() {
        let old = TransguiSettings(iniText: "[Connection]\nHost=nas.local\nPort=9092\n")
        #expect(old.connections.map(\.host) == ["nas.local"])
        #expect(old.connections.first?.port == 9092)
    }

    @Test func ignoresConnectionsWithoutAHost() {
        let empty = TransguiSettings(iniText: "[Hosts]\nCount=1\nHost1=Broken\n[Connection.Broken]\nPort=9091\n")
        #expect(empty.connections.isEmpty)
    }
}
