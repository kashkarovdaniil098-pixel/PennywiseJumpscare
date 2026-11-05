
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

        private bool appearing;
        private Vector2 lastPlayerTile = new Vector2(-1000, -1000);

        public override void Entry(IModHelper helper)
        {
            ResetTimer();

            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            Monitor.Log("Pennywise Jumpscare loaded.", LogLevel.Info);
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

            if (pennywise == null || Game1.currentLocation == null)
                return;

            var location = Game1.currentLocation;

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

                Vector2 destination = playerTile + new Vector2(
                    random.Next(-5, 6),
                    random.Next(-5, 6)
                );

                if (location.isTileOnMap(destination))
                {
                    pennywise.controller = null;
                    pennywise.currentLocation?.characters.Remove(pennywise);
                    pennywise.currentLocation = location;
                    pennywise.setTileLocation(destination);
                    location.addCharacter(pennywise);

                    appearing = true;
                    eventTicksLeft = 60 * 8;

                    Monitor.Log("Pennywise appeared nearby!", LogLevel.Info);
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

            Vector2 currentPlayerTile = Game1.player.Tile;

            float distance = Vector2.Distance(
                pennywise.Tile,
                currentPlayerTile
            );

            if (distance <= 1.5f)
            {
                pennywise.controller = null;
                pennywise.Halt();
                return;
            }

            bool playerMoved =
                Vector2.DistanceSquared(currentPlayerTile, lastPlayerTile) >= 4f;

            if (pennywise.controller != null && !playerMoved)
                return;

            Point playerPoint = new Point(
                (int)currentPlayerTile.X,
                (int)currentPlayerTile.Y
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

            Point destinationPoint = Point.Zero;
            bool foundDestination = false;

            foreach (Point offset in offsets)
            {
                Point candidate = new Point(
                    playerPoint.X + offset.X,
                    playerPoint.Y + offset.Y
                );

                if (location.isTileOnMap(
                    new Vector2(candidate.X, candidate.Y)))
                {
                    destinationPoint = candidate;
                    foundDestination = true;
                    break;
                }
            }

            if (!foundDestination)
                return;

            pennywise.controller = new PathFindController(
                pennywise,
                location,
                destinationPoint,
                -1
            );

            lastPlayerTile = currentPlayerTile;
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
