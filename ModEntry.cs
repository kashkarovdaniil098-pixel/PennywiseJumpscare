using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PennywiseJumpscare
{
    public sealed class ModEntry : Mod
    {
        private readonly Random random = new Random();
        private int ticksUntilNextEvent;
        private int eventTicksLeft;
        private bool hiding;

        public override void Entry(IModHelper helper)
        {
            ResetTimer();
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            Monitor.Log("Pennywise Jumpscare loaded.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.eventUp || Game1.currentMinigame != null)
                return;

            NPC? pennywise = Game1.getCharacterFromName("Daniil.PennywiseNPC_Pennywise");
            if (pennywise == null)
                pennywise = Game1.getCharacterFromName("Pennywise");

            if (hiding)
            {
                if (--eventTicksLeft <= 0)
                {
                    hiding = false;
                    pennywise?.setTileLocation(new Vector2(47, 87));
                    ResetTimer();
                }
                return;
            }

            if (--ticksUntilNextEvent > 0)
                return;

            if (pennywise == null || Game1.currentLocation == null)
            {
                ResetTimer();
                return;
            }

            // Use the player's current map. The destination is a few tiles away,
            // avoiding the player's exact tile.
            Vector2 playerTile = Game1.player.Tile;
            Vector2 destination = playerTile + new Vector2(
                random.Next(-5, 6), random.Next(-5, 6));

            if (!Game1.currentLocation.isTileOnMap(new xTile.Dimensions.Location(
                    (int)destination.X, (int)destination.Y)))
            {
                ResetTimer();
                return;
            }

            pennywise.currentLocation?.characters.Remove(pennywise);
            pennywise.currentLocation = Game1.currentLocation;
            pennywise.setTileLocation(destination);
            Game1.currentLocation.addCharacter(pennywise);
            hiding = true;
            eventTicksLeft = 60 * 8;
            Monitor.Log("Pennywise appeared nearby.", LogLevel.Info);
        }

        private void ResetTimer()
        {
            // Random interval: about 1–3 in-game hours, measured in update ticks.
            ticksUntilNextEvent = random.Next(60 * 60 * 60, 60 * 60 * 180);
        }
    }
}
