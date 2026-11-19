import Foundation
import FlyyLinkKit

signal(SIGPIPE, SIG_IGN)

let relay = BrowserHostRelay(input: .standardInput, output: .standardOutput, endpoint: BrowserEndpoint.load)
relay.launch = {
    let open = Process()
    open.executableURL = URL(fileURLWithPath: "/usr/bin/open")
    open.arguments = ["-g", "-b", "com.toneflix.flyylink"]
    try? open.run()
}
relay.run()
