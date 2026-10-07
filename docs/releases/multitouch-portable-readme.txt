GestureSign V2 18.3.2-multitouch.1 - touchscreen drawing test build (x64)

Extract the complete ZIP to a fresh writable folder. Exit the old settings
window and tray service, then start GestureSign.WinUI.exe.

Changes:
- Gestures -> Draw Gesture / Retrain captures a separate stroke per finger.
- Two fingers can draw opposite directions. Actual captured strokes determine
  finger count; a single stroke is no longer duplicated into parallel fingers.
- A stationary finger is retained when another finger draws a stroke.
- Drawing dialogs temporarily pause enabled recognition and restore it on close.
- Existing two-finger right-click actions keep their original settings: the mouse
  moves to the FirstDown position before sending the right mouse button action.
- Includes the issue #9 UI localization fixes.

Suggested device checks:
1. With the existing two-finger right-click action enabled, place the first finger
   to the RIGHT of the second finger. Check that the mouse/right-click targets
   the first finger's position, as before.
2. Open Gestures -> Draw Gesture. Touch with two fingers at the same time and
   move them in opposite directions. Both colored strokes should remain visible.
3. Save and check that the gesture preview contains both independent strokes.
4. Close/cancel the editor, then repeat the original two-finger right-click test.
5. Keep one finger stationary while drawing with the other, then save/retrain.

Back up your configuration before testing. Hardware multitouch verification
still requires the touchscreen device; automated checks do not emulate its driver.
Optional Kando and intent learning components remain separate downloads.