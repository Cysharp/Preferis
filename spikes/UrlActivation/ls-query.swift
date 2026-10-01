// Spike 4 helper: ask Launch Services which apps claim https URLs and public.html documents,
// to see whether the spike bundle is listed alongside the real browsers.
import AppKit
import UniformTypeIdentifiers

let ws = NSWorkspace.shared
print("== apps that open https URLs ==")
for u in ws.urlsForApplications(toOpen: URL(string: "https://example.com/")!) { print("  " + u.path) }
print("== apps that open public.html ==")
for u in ws.urlsForApplications(toOpen: UTType.html) { print("  " + u.path) }
print("== current default for https ==")
print("  " + (ws.urlForApplication(toOpen: URL(string: "https://example.com/")!)?.path ?? "none"))
