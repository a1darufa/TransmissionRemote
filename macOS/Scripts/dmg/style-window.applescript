-- Lays out the mounted disk image's Finder window: background picture, icon size, and the
-- app and Applications icons on either side of the arrow. Finder saves this in the image's
-- .DS_Store, so everyone who opens the image sees the same window.
--
-- Usage: osascript style-window.applescript <volume name> <app file name>
-- Positions must match make-background.swift.
on run argv
	set volumeName to item 1 of argv
	set appName to item 2 of argv

	tell application "Finder"
		tell disk volumeName
			open
			set current view of container window to icon view
			set toolbar visible of container window to false
			set statusbar visible of container window to false
			-- 640 × 400 of content, plus the title bar.
			set bounds of container window to {200, 120, 840, 548}

			set viewOptions to icon view options of container window
			set arrangement of viewOptions to not arranged
			set icon size of viewOptions to 128
			set text size of viewOptions to 13
			set background picture of viewOptions to file ".background:background.tiff"

			set position of item appName of container window to {160, 190}
			set position of item "Applications" of container window to {480, 190}

			-- Closing and reopening makes Finder write the .DS_Store now rather than later.
			close
			open
			update without registering applications
			delay 1
			close
		end tell
	end tell
end run
