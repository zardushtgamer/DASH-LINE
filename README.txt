DASHLINE - Unity port
=====================
1. Create (or open) any Unity project. 2D, 3D, URP, Built-in all work. Unity 2021.3+ recommended.
2. Copy the folder  Assets/Scripts/Dashline  into your project's Assets folder.
3. Open ANY scene (even an empty one) and press Play. No GameObjects, prefabs or sprites to set up.
   The game builds its own camera, art and sound in code.

Controls: Arrows/WASD move, Z/Space/C jump, X/Shift dash, V/E/Ctrl PARRY, R retry, P/Esc pause.
PARRY: press just before (or just after) hitting a wall to bounce off it. Fast hits give a PERFECT parry
(bigger bounce, dash refill, lots of style). Chain tricks to climb the style rank D > C > B > A > S > SS > SSS.
Input works with both the old Input Manager and the new Input System package.

Files:
  DashlineData.cs  - themes, stage list, level chunks (edit the Stages array to reorder / add stages)
  DashlineArt.cs   - all procedural pixel art (tiles, sky, mountains, sprites)
  DashlineGame.cs  - physics, player, camera, particles, audio, UI
