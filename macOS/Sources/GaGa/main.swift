
// GaGa for macOS.
// A minimal radio player for the macOS menu bar.

import AppKit

let application = NSApplication.shared
let controller = AppController()

// a regular app with a window and a dock icon, that also lives
// in the menu bar:
application.setActivationPolicy(.regular)
application.delegate = controller
application.run()
