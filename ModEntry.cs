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
        private Texture2D? jumpscareTexture;
        private SoundEffect? suspenseSound;
        private SoundEffectInstance? suspenseMusic;

        private int appearanceTimer;
        private int balloonSpawnTimer;
        private Vector2 balloonTile;
        private GameLocation? balloonLocation;
        private bool balloonVisible;
        private int encounterTimer;
        private int attackCooldown;
        private int pathTimer;
        private int normalSpeed = -1;

        private int observationTimer;
        private int fleeTimer;
        private int stuckChecks;
        private int musicRampTimer;
        private float originalMusicVolume = -1f;
        private int silhouetteTimer;
        private int jumpscareFaceTimer;
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
            balloonSpawnTimer = 0;
            balloonVisible = false;
            balloonLocation = null;
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
                if (balloonSpawnTimer > 0)
                {
                    balloonSpawnTimer--;
                    if (silhouetteTimer > 0)
                    {
                        silhouetteTimer--;
                        if (silhouetteTimer == 0)
                            jumpscareFaceTimer = 180;
                    }
                    else if (jumpscareFaceTimer > 0)
                    {
                        jumpscareFaceTimer--;
                        if (Game1.ticks % 3 == 0)
                        {
                            screenShakeX = random.Next(-5, 6);
                            screenShakeY = random.Next(-4, 5);
                            Game1.viewport.X += screenShakeX;
                            Game1.viewport.Y += screenShakeY;
                        }
                    }
                    if (balloonSpawnTimer <= 0)
                        TryAppear();
                    return;
                }

                if (appearanceTimer > 0)
                    appearanceTimer--;

                if (appearanceTimer <= 0)
                    PrepareBalloonAppearance();

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

        private void PrepareBalloonAppearance()
        {
            GameLocation location = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;
            Vector2? spawn = FindBalloonSpawn(location, playerTile);

            if (spawn == null)
            {
                appearanceTimer = 60;
                Monitor.Log("Couldn't find a passable tile about 10 tiles away for the balloon. Retrying.", LogLevel.Warn);
                return;
            }

            balloonTile = spawn.Value;
            balloonLocation = location;
            balloonVisible = true;
            balloonSpawnTimer = 60 * 10;
            LoadJumpscarePortrait();
            silhouetteTimer = 45;
            jumpscareFaceTimer = 0;
            Monitor.Log("A red balloon appeared about 10 tiles from the player. Pennywise will arrive in 10 seconds.", LogLevel.Info);
        }

        private Vector2? FindBalloonSpawn(GameLocation location, Vector2 playerTile)
        {
            Vector2? best = null;
            float bestDifference = float.MaxValue;

            // Search a ring around the player and choose the closest passable tile to 10 tiles.
            for (int x = -12; x <= 12; x++)
            for (int y = -12; y <= 12; y++)
            {
                float distance = (float)Math.Sqrt(x * x + y * y);
                if (distance < 8.5f || distance > 11.5f)
                    continue;

                Vector2 candidate = playerTile + new Vector2(x, y);
                if (!IsPassable(location, candidate))
                    continue;

                float difference = Math.Abs(distance - 10f);
                if (difference < bestDifference)
                {
                    bestDifference = difference;
                    best = candidate;
                }
            }

            return best;
        }

        private void TryAppear()
        {
            GameLocation location = Game1.currentLocation;
            Vector2? spawn = balloonVisible && balloonLocation == location && IsPassable(location, balloonTile)
                ? balloonTile
                : FindBalloonSpawn(location, Game1.player.Tile);

            if (spawn == null)
            {
                balloonSpawnTimer = 0;
                appearanceTimer = 60;
                Monitor.Log("No suitable Pennywise spawn tile found. Retrying.", LogLevel.Warn);
                return;
            }

            balloonTile = spawn.Value;
            balloonLocation = location;
            // Hide the warning balloon at the exact moment Pennywise appears.
            balloonVisible = false;
            pennywise!.controller = null;
            pennywise.currentLocation?.characters.Remove(pennywise);
            pennywise.currentLocation = location;
            pennywise.setTileLocation(spawn.Value);
            location.addCharacter(pennywise);
            pennywise.speed = Math.Max(1, normalSpeed * 3);

            originalMusicVolume = Game1.musicPlayerVolume;
            active = true;
            musicRampTimer = 0;
            StartSuspenseMusic();
            // The balloon is the warning: Pennywise appears at its tile after 10 seconds
            // and immediately begins chasing instead of standing and watching.
            observing = false;
            chasing = true;
            fleeing = false;
            observationTimer = 0;
            // The portrait has already played during the balloon warning, before this spawn.
            silhouetteTimer = 0;
            jumpscareFaceTimer = 0;
            encounterTimer = 60 * 30;
            attackCooldown = 0;
            pathTimer = 59; // Recalculate the chase path on the very next update tick.
            stuckChecks = 0;
            lastPathPosition = pennywise.Position;

            Monitor.Log("Pennywise appeared beside the red balloon and immediately started chasing.", LogLevel.Info);
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
                    pennywise.speed = Math.Max(1, (int)Math.Ceiling(normalSpeed * 3.0));
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
            pennywise.speed = Math.Max(1, (int)Math.Ceiling(normalSpeed * 3.0));
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
            if (!balloonVisible || balloonLocation != Game1.currentLocation)
                return;

            balloonTexture ??= CreateBalloonTexture();
            Vector2 worldPosition = new Vector2(
                balloonTile.X * Game1.tileSize + 4,
                balloonTile.Y * Game1.tileSize - 58
            );
            Vector2 position = Game1.GlobalToLocal(Game1.viewport, worldPosition);
            e.SpriteBatch.Draw(balloonTexture, position, Color.White);
        }

        private Texture2D CreateBalloonTexture()
        {
            const int width = 64;
            const int height = 92;
            Texture2D texture = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            Color[] pixels = new Color[width * height];

            // Large glossy red balloon with a visible knot and a thin curved string.
            for (int y = 3; y < 70; y++)
            for (int x = 7; x < 57; x++)
            {
                float dx = (x - 32f) / 24f;
                float dy = (y - 31f) / 34f;
                float d = dx * dx + dy * dy;
                if (d <= 1f)
                {
                    float shine = Math.Max(0f, 1f - (float)Math.Sqrt(
                        ((x - 22f) / 12f) * ((x - 22f) / 12f) +
                        ((y - 19f) / 19f) * ((y - 19f) / 19f)));
                    pixels[y * width + x] = new Color(
                        155 + (int)(shine * 100),
                        0 + (int)(shine * 24),
                        8 + (int)(shine * 22),
                        255
                    );
                }
            }

            // Knot at the bottom of the balloon.
            for (int y = 66; y <= 73; y++)
            for (int x = 27; x <= 37; x++)
                if (Math.Abs(x - 32) + Math.Abs(y - 69) <= 7)
                    pixels[y * width + x] = new Color(120, 0, 12, 255);

            // Fine grey string with a slight waviness.
            for (int y = 73; y < 92; y++)
            {
                int x = 32 + (int)Math.Round(Math.Sin(y * 0.22) * 2.0);
                pixels[y * width + x] = new Color(215, 215, 215, 255);
                if (x + 1 < width)
                    pixels[y * width + x + 1] = new Color(110, 110, 110, 230);
            }

            texture.SetData(pixels);
            return texture;
        }

        private void LoadJumpscarePortrait()
        {
            if (jumpscareTexture != null)
                return;

            try
            {
                string imagePath = Path.Combine(Helper.DirectoryPath, "jumpscare.png");
                if (!File.Exists(imagePath))
                {
                    Monitor.Log("jumpscare.png is missing from the mod folder: " + imagePath, LogLevel.Error);
                    return;
                }

                using (FileStream stream = File.OpenRead(imagePath))
                    jumpscareTexture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);

                Monitor.Log("Loaded full-screen Pennywise portrait: " + jumpscareTexture.Width + "x" + jumpscareTexture.Height, LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log("Couldn't load jumpscare.png: " + ex, LogLevel.Error);
                jumpscareTexture = null;
            }
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if ((!active && silhouetteTimer <= 0 && jumpscareFaceTimer <= 0) || !Context.IsWorldReady)
                return;

            // Draw the full-screen portrait during the warning, before Pennywise appears.
            if (silhouetteTimer > 0 || jumpscareFaceTimer > 0)
            {
                LoadJumpscarePortrait();
                if (jumpscareTexture == null)
                    return;

                int screenW = Game1.uiViewport.Width;
                int screenH = Game1.uiViewport.Height;
                float scale = Math.Max(
                    (float)screenW / jumpscareTexture.Width,
                    (float)screenH / jumpscareTexture.Height
                );
                int drawW = (int)(jumpscareTexture.Width * scale);
                int drawH = (int)(jumpscareTexture.Height * scale);
                Rectangle faceRect = new Rectangle(
                    (screenW - drawW) / 2,
                    (screenH - drawH) / 2,
                    drawW,
                    drawH
                );

                if (silhouetteTimer > 0)
                {
                    // Nearly black tint turns the portrait into a looming silhouette.
                    e.SpriteBatch.Draw(jumpscareTexture, faceRect, new Color(7, 0, 8, 255));
                }
                else
                {
                    float faceOpacity = Math.Min(1f, (192f - jumpscareFaceTimer) / 12f);
                    e.SpriteBatch.Draw(jumpscareTexture, faceRect, Color.White * faceOpacity);
                }
            }
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
                    string musicPath = Path.Combine(Helper.DirectoryPath, "suspense.wav");
                    if (!File.Exists(musicPath))
                        throw new FileNotFoundException("suspense.wav is missing from the mod folder.", musicPath);
                    using (FileStream stream = File.OpenRead(musicPath))
                        suspenseSound = SoundEffect.FromStream(stream);
                    suspenseMusic = suspenseSound.CreateInstance();
                    // SMAPI's MonoGame reference doesn't expose IsLooped here.
                    // OnUpdateTicked restarts the instance when playback ends.
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

            // Leave the balloon at the exact place Pennywise disappears.
            Vector2 finalBalloonTile = pennywise?.Tile ?? balloonTile;
            GameLocation? finalBalloonLocation = pennywise?.currentLocation ?? balloonLocation;

            if (pennywise != null)
            {
                pennywise.controller = null;
                pennywise.Halt();
                pennywise.speed = normalSpeed;
                pennywise.currentLocation?.characters.Remove(pennywise);
                pennywise.currentLocation = null;
            }

            balloonTile = finalBalloonTile;
            balloonLocation = finalBalloonLocation;
            balloonVisible = finalBalloonLocation != null;

            active = false;
            chasing = false;
            observing = false;
            fleeing = false;
            pathTimer = 0;
            stuckChecks = 0;
            silhouetteTimer = 0;
            jumpscareFaceTimer = 0;

            balloonSpawnTimer = 0;
            appearanceTimer = 60 * 30;
            Monitor.Log("Pennywise fled and disappeared. The red balloon remains behind.", LogLevel.Info);
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
            balloonSpawnTimer = 0;
            balloonVisible = false;
            balloonLocation = null;
            pathTimer = 0;
            observationTimer = 0;
            fleeTimer = 0;
            screenShakeX = 0;
            screenShakeY = 0;
            stuckChecks = 0;

            balloonTexture?.Dispose();
            balloonTexture = null;
            jumpscareTexture?.Dispose();
            jumpscareTexture = null;
            silhouetteTimer = 0;
            jumpscareFaceTimer = 0;
            screenShakeX = 0;
            screenShakeY = 0;
        }
    }
}
