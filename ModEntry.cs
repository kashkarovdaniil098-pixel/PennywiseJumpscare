
using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PennywiseJumpscare
{
    public sealed class ModEntry : Mod
    {
        private int repathTicks;
        private Vector2 lastPlayerTile = new Vector2(-1000, -1000);

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            Monitor.Log("Pennywise follow AI loaded.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.eventUp || Game1.currentMinigame != null)
                return;

            if (++repathTicks < 45)
                return;

            repathTicks = 0;

            NPC? pennywise =
                Game1.getCharacterFromName("Daniil.PennywiseNPC_Pennywise")
                ?? Game1.getCharacterFromName("Pennywise");

            var location = Game1.currentLocation;
            if (pennywise == null || location == null || pennywise.currentLocation != location)
                return;

            Vector2 playerTile = Game1.player.Tile;
            float distance = Vector2.Distance(pennywise.Tile, playerTile);

            if (distance <= 1.5f)
            {
                if (pennywise.controller != null)
                {
                    pennywise.controller = null;
                    pennywise.Halt();
                }

                lastPlayerTile = playerTile;
                return;
            }

            bool playerMoved = Vector2.DistanceSquared(playerTile, lastPlayerTile) >= 4f;
            if (pennywise.controller != null && !playerMoved)
                return;

            Point[] offsets =
            {
                new Point(1, 0), new Point(-1, 0),
                new Point(0, 1), new Point(0, -1),
                new Point(1, 1), new Point(-1, 1),
                new Point(1, -1), new Point(-1, -1)
            };

            Point playerPoint = new Point((int)playerTile.X, (int)playerTile.Y);
            Point destination = Point.Zero;
            bool foundDestination = false;

            foreach (Point offset in offsets)
            {
                Point candidate = new Point(playerPoint.X + offset.X, playerPoint.Y + offset.Y);
                if (location.isTileOnMap(new Vector2(candidate.X, candidate.Y)))
                {
                    destination = candidate;
                    foundDestination = true;
                    break;
                }
            }

            if (!foundDestination)
                return;

            pennywise.controller = new PathFindController(
                pennywise,
                location,
                destination,
                -1
            );

            lastPlayerTile = playerTile;
        }
    }
}
