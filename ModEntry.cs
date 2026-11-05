
using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Pathfinding;

namespace PennywiseJumpscare
{
    public sealed class ModEntry : Mod
    {
        private readonly Random random = new Random();

        private int ticksUntilNextEvent;
        private int eventTicksLeft;
        private int repathTicks;
        private int wanderTicks;

        private bool appearing;

        private Vector2 lastPlayerTile = new Vector2(-1000, -1000);

        public override void Entry(IModHelper helper)
        {
            ResetTimer();
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            Monitor.Log("Pennywise AI loaded.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady ||
                Game1.eventUp ||
                Game1.currentMinigame != null)
                return;

            NPC? pennywise =
                Game1.getCharacterFromName("Daniil.PennywiseNPC_Pennywise")
                ?? Game1.getCharacterFromName("Pennywise");

            var location = Game1.currentLocation;

            if (pennywise == null || location == null)
                return;

            if (appearing)
            {
                if (--eventTicksLeft <= 0)
                {
                    appearing = false;
                    ResetTimer();
                }

                return;
            }

            if (--ticksUntilNextEvent <= 0)
            {
                Vector2 playerTile = Game1.player.Tile;

                Vector2 spawnTile = playerTile + new Vector2(
                    random.Next(-5, 6),
                    random.Next(-5, 6)
                );

                if (location.isTileOnMap(spawnTile))
                {
                    pennywise.controller = null;
                    pennywise.currentLocation?.characters.Remove(pennywise);
                    pennywise.currentLocation = location;
                    pennywise.setTileLocation(spawnTile);
                    location.addCharacter(pennywise);

                    appearing = true;
                    eventTicksLeft = 60 * 8;

                    Monitor.Log("Pennywise appeared!", LogLevel.Info);
                }
                else
                {
                    ResetTimer();
                }

                return;
            }

            if (pennywise.currentLocation != location)
                return;

            if (++repathTicks < 45)
                return;

            repathTicks = 0;

            Vector2 playerPosition = Game1.player.Tile;

            float distance = Vector2.Distance(
                pennywise.Tile,
                playerPosition
            );

            // Если игрок далеко — Пеннивайз гуляет.
            if (distance > 8f)
            {
                if (pennywise.controller == null && ++wanderTicks >= 4)
                {
                    wanderTicks = 0;

                    Point current = new Point(
                        (int)pennywise.Tile.X,
                        (int)pennywise.Tile.Y
                    );

                    Point destination = new Point(
                        current.X + random.Next(-5, 6),
                        current.Y + random.Next(-5, 6)
                    );

                    if (location.isTileOnMap(
                        new Vector2(destination.X, destination.Y)))
                    {
                        pennywise.controller = new PathFindController(
                            pennywise,
                            location,
                            destination,
                            -1
                        );
                    }
                }

                return;
            }

            // Если игрок рядом — Пеннивайз преследует его.
            wanderTicks = 0;

            if (distance <= 1.5f)
            {
                pennywise.controller = null;
                pennywise.Halt();
                return;
            }

            bool playerMoved =
                Vector2.DistanceSquared(playerPosition, lastPlayerTile) >= 4f;

            if (pennywise.controller != null && !playerMoved)
                return;

            Point playerPoint = new Point(
                (int)playerPosition.X,
                (int)playerPosition.Y
            );

            Point[] offsets =
            {
                new Point(1, 0),
                new Point(-1, 0),
                new Point(0, 1),
                new Point(0, -1),
                new Point(1, 1),
                new Point(-1, 1),
                new Point(1, -1),
                new Point(-1, -1)
            };

            foreach (Point offset in offsets)
            {
                Point destination = new Point(
                    playerPoint.X + offset.X,
                    playerPoint.Y + offset.Y
                );

                if (!location.isTileOnMap(
                    new Vector2(destination.X, destination.Y)))
                    continue;

                pennywise.controller = new PathFindController(
                    pennywise,
                    location,
                    destination,
                    -1
                );

                lastPlayerTile = playerPosition;
                break;
            }
        }

        private void ResetTimer()
        {
            ticksUntilNextEvent = random.Next(
                60 * 60 * 60,
                60 * 60 * 180
            );
        }
    }
}
