using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        private int appearanceTimer;
        private int encounterTimer;
        private int attackCooldown;
        private int pathTimer;
        private int normalSpeed = -1;

        private int observationTimer;
        private int fleeTimer;
        private int stuckChecks;
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
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            Monitor.Log("Pennywise AI loaded.", LogLevel.Info);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            appearanceTimer = 60 * 50;
            active = false;
            chasing = false;
            observing = false;
            fleeing = false;
            pathTimer = 0;
            Monitor.Log("Save loaded. Pennywise's first appearance is scheduled in 50 seconds.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
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

            // Count the 50-second cycle from one appearance to the next,
            // including the time spent observing, chasing, and fleeing.
            if (appearanceTimer > 0)
                appearanceTimer--;

            if (!active)
            {
                if (appearanceTimer <= 0)
                    TryAppear();

                return;
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

            active = true;
            observing = true;
            chasing = false;
            fleeing = false;
            observationTimer = 60 * 3;
            encounterTimer = 60 * 35;
            appearanceTimer = 60 * 50;
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

        private void EndEncounter()
        {
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

            Monitor.Log("Pennywise fled and disappeared. The 50-second appearance cycle continues.", LogLevel.Info);
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
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
            stuckChecks = 0;

            balloonTexture?.Dispose();
            balloonTexture = null;
        }
    }
}
