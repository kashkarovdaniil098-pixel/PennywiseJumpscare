using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
using System.IO;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Pathfinding;

namespace PennywiseJumpscare
{
    public sealed class ModEntry : Mod
    {
        private readonly Random random = new Random();

        private NPC? pennywise;
        private Texture2D? balloonTexture;
        private Texture2D? bloodDripTexture;
        private SoundEffect? suspenseSound;
        private SoundEffectInstance? suspenseMusic;

        private int appearanceTimer;
        private int encounterTimer;
        private int attackCooldown;
        private int pathTimer;
        private int normalSpeed = -1;

        private int observationTimer;
        private int fleeTimer;
        private int stuckChecks;
        private int effectTimer;
        private int musicRampTimer;
        private float originalMusicVolume = -1f;
        private int bloodFrame;
        private int bloodRefreshTimer;
        private int screenShakeX;
        private int screenShakeY;
        private Vector2 lastPathPosition;

        private bool active;
        private bool chasing;
        private bool observing;
        private bool fleeing;
        private bool missingNpcLogged;

        public override void Entry(IModHelper helper)
        {
            appearanceTimer = 0;

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.Display.RenderedHud += OnRenderedHud;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            Monitor.Log("Pennywise AI loaded.", LogLevel.Info);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            appearanceTimer = 60 * 30;
            active = false;
            chasing = false;
            observing = false;
            fleeing = false;
            pathTimer = 0;
            Monitor.Log("Save loaded. Pennywise's first appearance is scheduled in 30 seconds.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // Undo the previous tiny camera offset before applying the next one.
            if (screenShakeX != 0 || screenShakeY != 0)
            {
                Game1.viewport.X -= screenShakeX;
                Game1.viewport.Y -= screenShakeY;
                screenShakeX = 0;
                screenShakeY = 0;
            }

            if (!Context.IsWorldReady || Game1.eventUp || Game1.currentMinigame != null)
                return;

            if (pennywise == null)
            {
                pennywise = Game1.getCharacterFromName("Daniil.PennywiseNPC_Pennywise")
                    ?? Game1.getCharacterFromName("Pennywise");

                if (pennywise == null)
                {
                    if (!missingNpcLogged)
                    {
                        Monitor.Log("Pennywise NPC instance not found. Check the Pennywise NPC content pack.", LogLevel.Warn);
                        missingNpcLogged = true;
                    }
                    return;
                }

                normalSpeed = Math.Max(1, pennywise.speed);
                missingNpcLogged = false;
                Monitor.Log("Pennywise NPC found.", LogLevel.Info);
            }

            // Wait 30 seconds after the previous scene has fully ended.
            if (!active)
            {
                if (appearanceTimer > 0)
                    appearanceTimer--;

                if (appearanceTimer <= 0)
                    TryAppear();

                return;
            }

            // Start quietly and raise the encounter music smoothly to full volume over 10 seconds.
            if (suspenseMusic != null)
            {
                if (suspenseMusic.State != SoundState.Playing)
                    suspenseMusic.Play();
                if (musicRampTimer < 600)
                    musicRampTimer++;
                float musicBlend = Math.Min(1f, musicRampTimer / 600f);
                suspenseMusic.Volume = 0.02f + musicBlend * 0.98f;

                // Fade the game's current music down as Pennywise's track rises.
                if (originalMusicVolume >= 0f)
                {
                    float gameMusicVolume = originalMusicVolume * (1f - musicBlend);
                    Game1.musicPlayerVolume = gameMusicVolume;
                    Game1.musicCategory.SetVolume(gameMusicVolume);
                }
            }

            if (pennywise.currentLocation != Game1.currentLocation)
                return;

            if (attackCooldown > 0)
                attackCooldown--;

            if (observing)
            {
                pennywise.controller = null;
                pennywise.Halt();
                observationTimer--;

                if (effectTimer > 0)
                {
                    effectTimer--;
                    if (Game1.ticks % 4 == 0)
                    {
                        screenShakeX = random.Next(-2, 3);
                        screenShakeY = random.Next(-1, 2);
                        Game1.viewport.X += screenShakeX;
                        Game1.viewport.Y += screenShakeY;
                    }
                }

                if (observationTimer <= 0)
                {
                    observing = false;
                    chasing = true;
                    pathTimer = 60;
                    lastPathPosition = pennywise.Position;
                    stuckChecks = 0;
                    Monitor.Log("Pennywise has left cover and started chasing.", LogLevel.Info);
                }
                return;
            }

            if (fleeing)
            {
                fleeTimer--;
                if (fleeTimer <= 0)
                    EndEncounter();
                return;
            }

            encounterTimer--;
            if (encounterTimer <= 0)
            {
                StartFleeing();
                return;
            }

            float distance = Vector2.Distance(pennywise.Tile, Game1.player.Tile);

            if (distance <= 1.5f)
            {
                pennywise.controller = null;
                pennywise.Halt();

                if (attackCooldown <= 0)
                {
                    Game1.player.takeDamage(10, true, null);
                    attackCooldown = 120;
                    Monitor.Log("Pennywise attacked the player for 10 HP.", LogLevel.Info);
                }
                return;
            }

            if (chasing && ++pathTimer >= 60)
            {
                pathTimer = 0;
                if (Vector2.Distance(pennywise.Position, lastPathPosition) < 4f && pennywise.controller != null)
                    stuckChecks++;
                else
                    stuckChecks = 0;

                lastPathPosition = pennywise.Position;

                // Don't replace an active path every few ticks. That caused the NPC
                // to repeatedly restart its route and shuffle back and forth.
                if (pennywise.controller == null || stuckChecks >= 2)
                {
                    pennywise.controller = null;
                    stuckChecks = 0;
                    FollowPlayer();
                }
            }
        }

        private bool IsPassable(GameLocation location, Vector2 tile)
        {
            return location.isTileOnMap(tile) && location.isTilePassable(tile);
        }

        private void TryAppear()
        {
            GameLocation location = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            Vector2? spawn = FindCoveredSpawn(location, playerTile);

            if (spawn == null)
                spawn = FindFallbackSpawn(location, playerTile);

            if (spawn == null)
            {
                appearanceTimer = 60;
                Monitor.Log("No suitable spawn tile found. Retrying.", LogLevel.Warn);
                return;
            }

            pennywise!.controller = null;
            pennywise.currentLocation?.characters.Remove(pennywise);
            pennywise.currentLocation = location;
            pennywise.setTileLocation(spawn.Value);
            location.addCharacter(pennywise);
            pennywise.speed = Math.Max(1, normalSpeed);

            originalMusicVolume = Game1.musicPlayerVolume;
            active = true;
            musicRampTimer = 0;
            bloodFrame = 0;
            bloodRefreshTimer = 0;
            StartSuspenseMusic();
            observing = true;
            chasing = false;
            fleeing = false;
            // The screen effect lasts for the full 10-second observation.
            observationTimer = 60 * 10;
            effectTimer = 60 * 10;
            encounterTimer = 60 * 30;
            attackCooldown = 0;
            pathTimer = 0;
            stuckChecks = 0;
            lastPathPosition = pennywise.Position;

            Monitor.Log("Pennywise appeared behind nearby cover and is watching the player.", LogLevel.Info);
        }

        private Vector2? FindCoveredSpawn(GameLocation location, Vector2 playerTile)
        {
            // Look for a passable tile on the far side of an impassable cover tile.
            // This works with map obstacles such as trees, bushes, and fences.
            for (int i = 0; i < 160; i++)
            {
                int dx = random.Next(-8, 9);
                int dy = random.Next(-8, 9);
                if (Math.Abs(dx) + Math.Abs(dy) < 4)
                    continue;

                Vector2 candidate = playerTile + new Vector2(dx, dy);
                if (!IsPassable(location, candidate))
                    continue;

                Vector2 direction = playerTile - candidate;
                float length = direction.Length();
                if (length < 0.001f)
                    continue;

                direction.Normalize();
                Vector2 coverTile = candidate + new Vector2(
                    (float)Math.Round(direction.X),
                    (float)Math.Round(direction.Y)
                );

                if (location.isTileOnMap(coverTile) && !location.isTilePassable(coverTile))
                    return candidate;
            }

            return null;
        }

        private Vector2? FindFallbackSpawn(GameLocation location, Vector2 playerTile)
        {
            for (int i = 0; i < 120; i++)
            {
                Vector2 candidate = playerTile + new Vector2(random.Next(-8, 9), random.Next(-8, 9));
                if (Math.Abs(candidate.X - playerTile.X) + Math.Abs(candidate.Y - playerTile.Y) < 4)
                    continue;
                if (IsPassable(location, candidate))
                    return candidate;
            }

            return null;
        }

        private void FollowPlayer()
        {
            if (pennywise == null)
                return;

            GameLocation location = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            Point playerPoint = new Point((int)playerTile.X, (int)playerTile.Y);

            Point[] offsets =
            {
                new Point(1, 0), new Point(-1, 0),
                new Point(0, 1), new Point(0, -1),
                new Point(1, 1), new Point(-1, 1),
                new Point(1, -1), new Point(-1, -1)
            };

            // Prefer a nearby reachable tile around the player, but don't constantly
            // cancel the NPC's current path while it is moving.
            foreach (Point offset in offsets)
            {
                Point destination = new Point(playerPoint.X + offset.X, playerPoint.Y + offset.Y);
                Vector2 tile = new Vector2(destination.X, destination.Y);
                if (!IsPassable(location, tile))
                    continue;

                try
                {
                    pennywise.speed = Math.Max(1, (int)Math.Ceiling(normalSpeed * 1.2));
                    var controller = new PathFindController(pennywise, location, destination, -1);
                    if (controller != null)
                    {
                        pennywise.controller = controller;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Monitor.Log("Pathfinding error: " + ex.Message, LogLevel.Warn);
                }
            }

            Monitor.Log("Pennywise couldn't find a path; will retry shortly.", LogLevel.Trace);
        }

        private void StartFleeing()
        {
            if (pennywise == null)
            {
                EndEncounter();
                return;
            }

            fleeing = true;
            chasing = false;
            observing = false;
            fleeTimer = 60 * 3;
            pennywise.controller = null;
            pennywise.speed = Math.Max(1, (int)Math.Ceiling(normalSpeed * 1.5));
            SendPennywiseAway();
            Monitor.Log("Pennywise is fleeing before disappearing.", LogLevel.Info);
        }

        private void SendPennywiseAway()
        {
            if (pennywise == null)
                return;

            GameLocation location = Game1.currentLocation;
            Vector2 fromPlayer = pennywise.Tile - Game1.player.Tile;
            if (fromPlayer.LengthSquared() < 0.01f)
                fromPlayer = new Vector2(1, 0);
            fromPlayer.Normalize();

            Vector2 best = pennywise.Tile;
            float bestDistance = Vector2.Distance(best, Game1.player.Tile);

            for (int i = 0; i < 24; i++)
            {
                float angle = (float)(i * Math.PI / 12.0);
                Vector2 direction = new Vector2(
                    (float)Math.Cos(angle) * fromPlayer.X - (float)Math.Sin(angle) * fromPlayer.Y,
                    (float)Math.Sin(angle) * fromPlayer.X + (float)Math.Cos(angle) * fromPlayer.Y
                );
                Vector2 candidate = pennywise.Tile + direction * 7f;
                if (!IsPassable(location, candidate))
                    continue;

                float distance = Vector2.Distance(candidate, Game1.player.Tile);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            Point target = new Point((int)best.X, (int)best.Y);
            try
            {
                pennywise.controller = new PathFindController(pennywise, location, target, -1);
            }
            catch (Exception ex)
            {
                Monitor.Log("Flee pathfinding error: " + ex.Message, LogLevel.Warn);
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!active || pennywise == null || pennywise.currentLocation != Game1.currentLocation)
                return;

            balloonTexture ??= CreateBalloonTexture();
            Vector2 position = Game1.GlobalToLocal(
                Game1.viewport,
                pennywise.Position + new Vector2(34, 0)
            );

            e.SpriteBatch.Draw(balloonTexture, position, Color.White);
        }

        private Texture2D CreateBalloonTexture()
        {
            const int width = 28;
            const int height = 38;
            Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = (x - 14f) / 13f;
                    float dy = (y - 13f) / 16f;
                    bool inside = dx * dx + dy * dy <= 1f;
                    pixels[y * width + x] = inside ? Color.Red : Color.Transparent;
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!active || effectTimer <= 0 || !Context.IsWorldReady)
                return;

            if (bloodDripTexture == null || ++bloodRefreshTimer >= 4)
            {
                bloodDripTexture?.Dispose();
                bloodDripTexture = CreateBloodDripTexture(bloodFrame++);
                bloodRefreshTimer = 0;
            }
            e.SpriteBatch.Draw(
                bloodDripTexture,
                new Rectangle(0, 0, Game1.viewport.Width, 235),
                Color.White
            );
        }

        private Texture2D CreateBloodDripTexture(int frame)
        {
            const int width = 512;
            const int height = 235;
            Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            Color[] pixels = new Color[width * height];
            Random textureRandom = new Random(7319);

            // A glossy, uneven upper pool with a dark underside and tiny surface highlights.
            for (int x = 0; x < width; x++)
            {
                int edge = 19 + (int)(3 * Math.Sin(x * 0.071)) + textureRandom.Next(0, 5);
                for (int y = 0; y <= edge; y++)
                {
                    float shade = y / (float)Math.Max(1, edge);
                    int red = 105 + (int)(75 * (1f - shade));
                    int green = (int)(5 + 5 * (1f - shade));
                    int blue = 12 + (int)(10 * (1f - shade));
                    byte alpha = (byte)(238 - shade * 18);
                    if (y == edge - 2 && x % 17 < 5)
                        pixels[y * width + x] = new Color(235, 65, 72, 220);
                    else
                        pixels[y * width + x] = new Color(red, green, blue, alpha);
                }
            }

            // Rounded, irregular streams. Their starting points advance each frame,
            // making the glossy drips visibly travel down instead of remaining flat.
            for (int stream = 0; stream < 39; stream++)
            {
                int originX = textureRandom.Next(0, width);
                int maxLength = textureRandom.Next(62, 225);
                int phase = textureRandom.Next(0, 90);
                int startY = -((frame * 2 + phase) % 72);
                float baseWidth = textureRandom.Next(2, 8);
                float wave = (float)textureRandom.NextDouble() * 6.28f;
                float drift = 0.7f + (float)textureRandom.NextDouble() * 1.8f;
                int endY = Math.Min(height - 4, startY + maxLength);

                for (int y = Math.Max(0, startY); y <= endY; y++)
                {
                    float localY = y - startY;
                    float taper = 0.78f + 0.22f * (float)Math.Sin(Math.Min(1f, localY / maxLength) * Math.PI);
                    float center = originX
                        + (float)Math.Sin((localY + frame * 0.8f) * 0.045f + wave) * drift
                        + (float)Math.Sin(localY * 0.018f + wave) * 1.5f;
                    int half = Math.Max(2, (int)(baseWidth * taper));
                    int cx = (int)center;

                    for (int x = Math.Max(0, cx - half - 1); x <= Math.Min(width - 1, cx + half + 1); x++)
                    {
                        int dx = x - cx;
                        float across = Math.Abs(dx) / (float)(half + 1);
                        if (across > 1f)
                            continue;

                        // Dark rim, saturated center, and a narrow wet highlight create depth.
                        float edgeShade = 1f - across * 0.62f;
                        float grain = 0.94f + 0.06f * (float)Math.Sin(x * 0.37 + y * 0.21 + stream);
                        int red = (int)(150 * edgeShade * grain + 35);
                        int green = dx == -half / 2 ? 26 : 2;
                        int blue = (int)(18 * edgeShade);
                        byte alpha = (byte)(218 - across * 24);

                        if (dx == -Math.Max(1, half / 2))
                        {
                            red = 238;
                            green = 70;
                            blue = 78;
                            alpha = 205;
                        }
                        else if (Math.Abs(dx) == half)
                        {
                            red = 70;
                            green = 0;
                            blue = 8;
                            alpha = 235;
                        }

                        pixels[y * width + x] = new Color(red, green, blue, alpha);
                    }
                }

                // Bulb-shaped drop at the moving tip, with a bright upper glint.
                int tipX = Math.Max(3, Math.Min(width - 4, originX));
                int tipY = Math.Max(2, Math.Min(height - 3, endY));
                int radius = Math.Max(2, (int)(baseWidth * 0.85f));
                for (int y = tipY - radius; y <= tipY + radius; y++)
                {
                    for (int x = tipX - radius; x <= tipX + radius; x++)
                    {
                        if (x < 0 || x >= width || y < 0 || y >= height)
                            continue;
                        float d = (float)Math.Sqrt((x - tipX) * (x - tipX) + (y - tipY) * (y - tipY) * 0.7f);
                        if (d > radius)
                            continue;
                        Color drop = d < radius * 0.35f
                            ? new Color(235, 55, 65, 230)
                            : new Color(125, 0, 12, 238);
                        pixels[y * width + x] = drop;
                    }
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private void StartSuspenseMusic()
        {
            try
            {
                if (suspenseMusic == null)
                {
                    string path = Path.Combine(Helper.DirectoryPath, "suspense.wav");
                    if (!File.Exists(path))
                    {
                        Monitor.Log("Suspense music file is missing; continuing without music.", LogLevel.Warn);
                        return;
                    }

                    using (FileStream stream = File.OpenRead(path))
                        suspenseSound = SoundEffect.FromStream(stream);
                    suspenseMusic = suspenseSound.CreateInstance();
                    suspenseMusic.IsLooped = true;
                    suspenseMusic.Volume = 0.02f;
                }

                if (suspenseMusic.State != SoundState.Playing)
                    suspenseMusic.Play();
            }
            catch (Exception ex)
            {
                Monitor.Log("Couldn't start suspense music: " + ex.Message, LogLevel.Warn);
            }
        }

        private void StopSuspenseMusic()
        {
            if (suspenseMusic != null)
                suspenseMusic.Stop();
        }

        private void RestoreGameMusic()
        {
            if (originalMusicVolume >= 0f)
            {
                Game1.musicPlayerVolume = originalMusicVolume;
                Game1.musicCategory.SetVolume(originalMusicVolume);
                originalMusicVolume = -1f;
            }
        }

        private void EndEncounter()
        {
            StopSuspenseMusic();
            RestoreGameMusic();
            musicRampTimer = 0;
            if (pennywise != null)
            {
                pennywise.controller = null;
                pennywise.Halt();
                pennywise.speed = normalSpeed;
                pennywise.currentLocation?.characters.Remove(pennywise);
                pennywise.currentLocation = null;
            }

            active = false;
            chasing = false;
            observing = false;
            fleeing = false;
            pathTimer = 0;
            stuckChecks = 0;
            effectTimer = 0;

            appearanceTimer = 60 * 30;
            Monitor.Log("Pennywise fled and disappeared. Next appearance in 30 seconds.", LogLevel.Info);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            StopSuspenseMusic();
            RestoreGameMusic();
            musicRampTimer = 0;
            if (pennywise != null)
            {
                pennywise.controller = null;
                pennywise.currentLocation?.characters.Remove(pennywise);
            }

            active = false;
            chasing = false;
            observing = false;
            fleeing = false;
            pennywise = null;
            normalSpeed = -1;
            appearanceTimer = 0;
            pathTimer = 0;
            observationTimer = 0;
            fleeTimer = 0;
            effectTimer = 0;
            screenShakeX = 0;
            screenShakeY = 0;
            stuckChecks = 0;

            balloonTexture?.Dispose();
            balloonTexture = null;
            bloodDripTexture?.Dispose();
            bloodDripTexture = null;
            effectTimer = 0;
            screenShakeX = 0;
            screenShakeY = 0;
        }
    }
}
