# dmgbuild settings for the macOS disk image (docs/Platform.md §3.1): Preferis.app next to a link to /Applications,
# side by side in the Finder window. PackageMacOS (build/Preferis.Build.proj) runs it as
#   pipx run dmgbuild==<version> -s build/dmg/settings.py -D app=<Preferis.app> Preferis <output.dmg>
# By hand, from the repository root, after PublishMacOS:
#   pipx run dmgbuild -s build/dmg/settings.py Preferis "Preferis Installer.dmg"
import os.path

# -D app=... names the bundle; the default is the local build, relative to the repository root.
application = defines.get("app", "artifacts/osx-arm64/Preferis.app").rstrip("/")
appname = os.path.basename(application)

format = defines.get("format", "UDZO")
compression_level = 9
volume_name = "Preferis"

# The arrow between the two icons: background.png, with background@2x.png next to it folded in by dmgbuild for Retina.
# Both are drawn from arrow.png by make-background.py; -D background=... passes the path from MSBuild.
background = defines.get("background", "build/dmg/background.png")

files = [application]
symlinks = {"Applications": "/Applications"}

window_rect = ((200, 120), (640, 320))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
icon_size = 128
icon_locations = {
    appname: (140, 120),
    "Applications": (500, 120),
}
