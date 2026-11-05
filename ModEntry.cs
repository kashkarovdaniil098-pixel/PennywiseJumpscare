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

            // Bring the encounter music up to full volume over 3 seconds.
            if (suspenseMusic != null)
            {
                if (suspenseMusic.State != SoundState.Playing)
                    suspenseMusic.Play();
                if (musicRampTimer < 180)
                    musicRampTimer++;
                float musicBlend = Math.Min(1f, musicRampTimer / 180f);
                suspenseMusic.Volume = 0.35f + musicBlend * 0.65f;

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
            // The glossy blood overlay appears briefly, then fades away.
            observationTimer = 60 * 10;
            effectTimer = 60 * 3;
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
            // Keep the effect vivid at first, then smoothly dissolve it during its final second.
            float bloodOpacity = Math.Min(1f, effectTimer / 60f);
            e.SpriteBatch.Draw(
                bloodDripTexture,
                new Rectangle(0, 0, Game1.viewport.Width, Game1.viewport.Height),
                Color.White * bloodOpacity
            );
        }

        private Texture2D CreateBloodDripTexture(int frame)
        {
            const int width = 512;
            const int height = 512;
            Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            Color[] pixels = new Color[width * height];
            Random textureRandom = new Random(7319);

            // A small number of short, fine trails. Each one ends in a rounded,
            // heavier drop instead of becoming a long, uniform stripe.
            for (int stream = 0; stream < 17; stream++)
            {
                int originX = textureRandom.Next(8, width - 8);
                int length = textureRandom.Next(48, 142);
                float threadWidth = 1.0f + (float)textureRandom.NextDouble() * 1.25f;
                float sway = (float)textureRandom.NextDouble() * 2.4f;
                float phase = (float)textureRandom.NextDouble() * 6.28f;
                int startY = -textureRandom.Next(0, 24);
                int tipY = Math.Min(height - 18, startY + length + (frame % 12) * 1.6f);
                float tipXf = originX + (float)Math.Sin((tipY - startY) * 0.025f + phase) * sway;
                int tipX = Math.Max(10, Math.Min(width - 11, (int)tipXf));

                // Hair-thin glossy thread leading into the main droplet.
                for (int y = Math.Max(0, startY); y < tipY; y++)
                {
                    float progress = (y - startY) / (float)Math.Max(1, tipY - startY);
                    float center = originX
                        + (float)Math.Sin((y - startY) * 0.025f + phase) * sway;
                    float widthAtY = threadWidth * (0.72f + 0.28f * progress);
                    int left = Math.Max(0, (int)Math.Floor(center - widthAtY - 1));
                    int right = Math.Min(width - 1, (int)Math.Ceiling(center + widthAtY + 1));

                    for (int x = left; x <= right; x++)
                    {
                        float across = Math.Abs(x - center) / widthAtY;
                        if (across > 1.35f)
                            continue;

                        float coverage = Math.Max(0f, Math.Min(1f, (1.35f - across) / 0.45f));
                        byte alpha = (byte)(coverage * 218);
                        Color color;
                        if (x < center - widthAtY * 0.25f)
                            color = new Color(255, 92, 98, alpha);
                        else if (across > 0.82f)
                            color = new Color(75, 0, 9, alpha);
                        else
                            color = new Color(174, 3, 20, alpha);

                        pixels[y * width + x] = color;
                    }
                }

                // Rounded teardrop with a dark, refractive edge and bright wet highlight.
                float radiusX = 4.8f + (float)textureRandom.NextDouble() * 3.0f;
                float radiusY = radiusX * (1.15f + (float)textureRandom.NextDouble() * 0.25f);
                int minX = Math.Max(0, (int)(tipX - radiusX - 2));
                int maxX = Math.Min(width - 1, (int)(tipX + radiusX + 2));
                int minY = Math.Max(0, (int)(tipY - radiusY - 2));
                int maxY = Math.Min(height - 1, (int)(tipY + radiusY + 2));

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float nx = (x - tipX) / radiusX;
                        float ny = (y - tipY) / radiusY;
                        float distance = (float)Math.Sqrt(nx * nx + ny * ny);
                        if (distance > 1.18f)
                            continue;

                        float edge = Math.Max(0f, Math.Min(1f, (1.18f - distance) / 0.18f));
                        float centerGlow = Math.Max(0f, 1f - (float)Math.Sqrt(nx * nx * 0.55f + ny * ny * 0.7f));
                        int red = (int)(82 + 105 * centerGlow);
                        int green = (int)(1 + 8 * centerGlow);
                        int blue = (int)(9 + 12 * centerGlow);
                        byte alpha = (byte)(edge * 238);
                        Color dropColor = new Color(red, green, blue, alpha);

                        // Narrow specular streak on the upper-left side of the drop.
                        float highlightX = (x - (tipX - radiusX * 0.34f)) / (radiusX * 0.19f);
                        float highlightY = (y - (tipY - radiusY * 0.34f)) / (radiusY * 0.34f);
                        if (highlightX * highlightX + highlightY * highlightY < 1f)
                            dropColor = new Color(255, 166, 169, (byte)(edge * 225));
                        else if (distance > 0.83f)
                            dropColor = new Color(55, 0, 8, alpha);

                        pixels[y * width + x] = dropColor;
                    }
                }
            }

            texture.SetData(pixels);
            return texture;
        }

        private SoundEffect CreateSuspenseViolinSound()
        {
            const int sampleRate = 44100;
            const int seconds = 12;
            int sampleCount = sampleRate * seconds;
            byte[] pcm = new byte[sampleCount * 2];

            // A continuous bowed-string phrase in the upper register.
            // Phase is accumulated sample-by-sample, so changing notes never resets
            // the waveform and creates the sharp clicks heard in the previous version.
            double[] notes = { 440.00, 523.25, 587.33, 659.25, 587.33, 493.88,
                               440.00, 392.00, 466.16, 523.25, 587.33, 659.25 };
            const double noteSeconds = 1.0;
            const double twoPi = Math.PI * 2.0;
            double phase = 0.0;
            double harmonyPhase = 0.0;

            for (int i = 0; i < sampleCount; i++)
            {
                double t = i / (double)sampleRate;
                int noteIndex = Math.Min(notes.Length - 1, (int)(t / noteSeconds));
                double local = t - noteIndex * noteSeconds;

                // Glide gently into each new note instead of making an abrupt pitch jump.
                double previousNote = noteIndex == 0 ? notes[0] : notes[noteIndex - 1];
                double glide = Math.Min(1.0, local / 0.075);
                glide = glide * glide * (3.0 - 2.0 * glide);
                double baseFrequency = previousNote + (notes[noteIndex] - previousNote) * glide;

                double vibrato = 1.0 + 0.006 * Math.Sin(twoPi * 5.2 * t);
                double slowWaver = 1.0 + 0.0012 * Math.Sin(twoPi * 0.55 * t);
                double frequency = baseFrequency * vibrato * slowWaver;
                phase += twoPi * frequency / sampleRate;
                harmonyPhase += twoPi * (frequency * 1.5) / sampleRate;

                // Smooth start/end for clean looping, with a sustained bow between notes.
                double attack = Math.Min(1.0, t / 0.22);
                double release = Math.Min(1.0, (seconds - t) / 0.30);
                double envelope = Math.Max(0.0, Math.Min(attack, release));
                double tremolo = 0.96 + 0.04 * Math.Sin(twoPi * 4.6 * t);

                // Strong upper harmonics give the sustained tone a violin-like edge.
                double violin =
                    0.31 * Math.Sin(phase) +
                    0.27 * Math.Sin(phase * 2.0 + 0.10) +
                    0.22 * Math.Sin(phase * 3.0 + 0.24) +
                    0.17 * Math.Sin(phase * 4.0 + 0.40) +
                    0.12 * Math.Sin(phase * 5.0 + 0.58) +
                    0.08 * Math.Sin(phase * 6.0 + 0.76) +
                    0.045 * Math.Sin(phase * 7.0 + 0.92);

                double harmony = 0.10 * Math.Sin(harmonyPhase)
                    + 0.035 * Math.Sin(harmonyPhase * 2.0);

                double sample = (violin + harmony) * envelope * tremolo * 0.82;
                sample = Math.Max(-0.94, Math.Min(0.94, sample));
                short value = (short)(sample * 32767.0);
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new SoundEffect(pcm, sampleRate, AudioChannels.Mono);
        }

        private void StartSuspenseMusic()
        {
            try
            {
                if (suspenseMusic == null)
                {
                    // Generate an original ominous violin cue in memory: no external music file required.
                    suspenseSound = CreateSuspenseViolinSound();
                    suspenseMusic = suspenseSound.CreateInstance();
                    // The update loop restarts the track as soon as it finishes.
                    suspenseMusic.Volume = 0.35f;
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
