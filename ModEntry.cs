
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
        private int stalkingTimer;
        private int attackCooldown;
        private int pathTimer;
        private int normalSpeed = -1;

        private bool active;
        private bool chasing;

        public override void Entry(IModHelper helper)
        {
            ResetAppearanceTimer();

            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            Monitor.Log("Pennywise AI loaded.", LogLevel.Info);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || Game1.eventUp ||
                Game1.currentMinigame != null)
                return;

            pennywise ??=
                Game1.getCharacterFromName("Daniil.PennywiseNPC_Pennywise")
                ?? Game1.getCharacterFromName("Pennywise");

            if (pennywise == null)
                return;

            if (normalSpeed < 0)
                normalSpeed = Math.Max(1, pennywise.speed);

            if (appearanceTimer > 0)
                appearanceTimer--;

            if (attackCooldown > 0)
                attackCooldown--;

            if (!active)
            {
                if (appearanceTimer <= 0)
                    TryAppear();

                return;
            }

            if (pennywise.currentLocation != Game1.currentLocation)
                return;

            encounterTimer--;

            if (encounterTimer <= 0)
            {
                EndEncounter();
                return;
            }

            float distance = Vector2.Distance(
                pennywise.Tile,
                Game1.player.Tile
            );

            if (!chasing)
            {
                pennywise.speed = normalSpeed;
                stalkingTimer++;

                if (stalkingTimer >= 180)
                {
                    chasing = true;
                    pathTimer = 15;
                }

                return;
            }

            if (distance <= 1.5f)
            {
                pennywise.controller = null;
                pennywise.Halt();

                if (attackCooldown <= 0)
                {
                    Game1.player.takeDamage(10, true, null);
                    attackCooldown = 120;
                }

                return;
            }

            if (++pathTimer >= 15)
            {
                pathTimer = 0;
                FollowPlayer();
            }
        }

        private bool IsPassable(GameLocation location, Vector2 tile)
        {
            return location.isTileOnMap(tile) &&
                location.isTilePassable(tile);
        }

        private void TryAppear()
        {
            GameLocation location = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;

            Vector2? coveredPosition = null;
            Vector2? fallbackPosition = null;

            for (int i = 0; i < 80; i++)
            {
                int dx = random.Next(-8, 9);
                int dy = random.Next(-8, 9);

                if (Math.Abs(dx) + Math.Abs(dy) < 4)
                    continue;

                Vector2 tile = playerTile + new Vector2(dx, dy);

                if (!IsPassable(location, tile))
                    continue;

                if (fallbackPosition == null)
                    fallbackPosition = tile;

                if (HasNearbyCover(location, tile))
                {
                    coveredPosition = tile;
                    break;
                }
            }

            Vector2? spawn = coveredPosition ?? fallbackPosition;

            if (spawn == null)
            {
                ResetAppearanceTimer();
                return;
            }

            pennywise!.controller = null;
            pennywise.currentLocation?.characters.Remove(pennywise);
            pennywise.currentLocation = location;
            pennywise.setTileLocation(spawn.Value);
            location.addCharacter(pennywise);

            pennywise.speed = normalSpeed;

            active = true;
            chasing = false;
            stalkingTimer = 0;
            encounterTimer = 60 * 35;
            pathTimer = 0;

            Monitor.Log("Pennywise appeared.", LogLevel.Info);
        }

        private bool HasNearbyCover(GameLocation location, Vector2 tile)
        {
            Vector2[] nearby =
            {
                tile + new Vector2(1, 0),
                tile + new Vector2(-1, 0),
                tile + new Vector2(0, 1),
                tile + new Vector2(0, -1)
            };

            foreach (Vector2 check in nearby)
            {
                if (location.terrainFeatures.ContainsKey(check) ||
                    location.objects.ContainsKey(check))
                    return true;
            }

            return false;
        }

        private void FollowPlayer()
        {
            if (pennywise == null)
                return;

            GameLocation location = Game1.currentLocation;
            Vector2 playerTile = Game1.player.Tile;

            Point playerPoint = new Point(
                (int)playerTile.X,
                (int)playerTile.Y
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

                Vector2 tile = new Vector2(
                    destination.X,
                    destination.Y
                );

                if (!IsPassable(location, tile))
                    continue;

                pennywise.speed = Math.Max(
                    1,
                    (int)Math.Ceiling(normalSpeed * 1.2)
                );

                pennywise.controller = new PathFindController(
                    pennywise,
                    location,
                    destination,
                    -1
                );

                return;
            }
        }

        private void OnRenderedWorld(
            object? sender,
            RenderedWorldEventArgs e)
        {
            if (!active || pennywise == null ||
                pennywise.currentLocation != Game1.currentLocation)
                return;

            balloonTexture ??= CreateBalloonTexture();

            Vector2 position = Game1.GlobalToLocal(
                Game1.viewport,
                pennywise.Position + new Vector2(38, 8)
            );

            e.SpriteBatch.Draw(
                balloonTexture,
                position,
                Color.White
            );

            e.SpriteBatch.Draw(
                Game1.staminaRect,
                new Microsoft.Xna.Framework.Rectangle(
                    (int)position.X + 7,
                    (int)position.Y + 15,
                    1,
                    12
                ),
                Color.White
            );
        }

        private Texture2D CreateBalloonTexture()
        {
            Texture2D texture = new Texture2D(
                Game1.graphics.GraphicsDevice,
                16,
                22
            );

            Color[] pixels = new Color[16 * 22];

            for (int y = 0; y < 22; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float dx = (x - 8f) / 7f;
                    float dy = (y - 8f) / 9f;

                    pixels[y * 16 + x] =
                        dx * dx + dy * dy <= 1f
                        ? Color.Red
                        : Color.Transparent;
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

                if (normalSpeed > 0)
                    pennywise.speed = normalSpeed;
            }

            active = false;
            chasing = false;

            ResetAppearanceTimer();
        }

        private void ResetAppearanceTimer()
        {
            appearanceTimer = random.Next(
                60 * 60,
                60 * 60 * 3
            );
        }

        private void OnReturnedToTitle(
            object? sender,
            ReturnedToTitleEventArgs e)
        {
            active = false;
            chasing = false;
            pennywise = null;
            normalSpeed = -1;

            balloonTexture?.Dispose();
            balloonTexture = null;

            ResetAppearanceTimer();
        }
    }
}
