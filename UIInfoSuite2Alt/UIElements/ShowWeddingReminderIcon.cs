using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowWeddingReminderIcon : IDisposable
{
  #region Properties
  private const string MermaidsPendantId = "(O)460";
  private const float IconScale = 40 / 16f;
  private const float ExclamationScale = 1.6f;

  private readonly PerScreen<string?> _spouseDisplayName = new();

  private readonly PerScreen<ClickableTextureComponent> _icon = new(() =>
    new ClickableTextureComponent(new Rectangle(0, 0, 40, 40), null, Rectangle.Empty, IconScale)
  );

  private readonly IModHelper _helper;
  #endregion


  #region Life cycle
  public ShowWeddingReminderIcon(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool enabled)
  {
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
    _helper.Events.Display.RenderingHud -= OnRenderingHud;

    if (enabled)
    {
      CheckForWedding();
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
    }
    else
    {
      _spouseDisplayName.Value = null;
    }
  }
  #endregion


  #region Event subscriptions
  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    CheckForWedding();
  }

  // The ceremony fires after DayStarted, so re-check to clear the icon once married.
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (e.IsOneSecond)
    {
      CheckForWedding();
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (_spouseDisplayName.Value != null && UIElementUtils.IsRenderingNormally())
    {
      EnqueueIcon(_spouseDisplayName.Value);
    }
  }
  #endregion


  #region Logic
  private void CheckForWedding()
  {
    _spouseDisplayName.Value = null;

    if (!Context.IsWorldReady)
    {
      return;
    }

    Farmer player = Game1.player;
    Friendship? friendship = null;
    string? displayName = null;

    if (
      player.spouse != null
      && player.isEngaged()
      && player.friendshipData.TryGetValue(player.spouse, out Friendship npcFriendship)
    )
    {
      friendship = npcFriendship;
      displayName = Game1.getCharacterFromName(player.spouse)?.displayName ?? player.spouse;
    }
    else if (player.team.IsEngaged(player.UniqueMultiplayerID))
    {
      long? spouseId = player.team.GetSpouse(player.UniqueMultiplayerID);
      if (spouseId.HasValue)
      {
        friendship = player.team.GetFriendship(player.UniqueMultiplayerID, spouseId.Value);
        displayName = Game1.GetPlayer(spouseId.Value)?.displayName;
      }
    }

    // Roommate agreements never produce a ceremony (getAvailableWeddingEvent returns null),
    // so there is nothing to dress up for.
    if (friendship == null || displayName == null || friendship.RoommateMarriage)
    {
      return;
    }

    // CountdownToWedding clamps to 0 once the date passes. Still engaged at 0 means today was
    // ineligible and the ceremony slipped, so the next eligible day is the wedding.
    var tomorrow = new WorldDate(Game1.Date) { TotalDays = Game1.Date.TotalDays + 1 };
    if (
      friendship.CountdownToWedding <= 1
      && Game1.canHaveWeddingOnDay(tomorrow.DayOfMonth, tomorrow.Season)
    )
    {
      _spouseDisplayName.Value = displayName;
    }
  }

  private void EnqueueIcon(string spouseDisplayName)
  {
    // Resolved per frame: textures from game content get disposed when a mod invalidates assets.
    ParsedItemData pendant = ItemRegistry.GetDataOrErrorItem(MermaidsPendantId);
    ClickableTextureComponent icon = _icon.Value;
    icon.texture = pendant.GetTexture();
    icon.sourceRect = pendant.GetSourceRect();

    // Shares the birthday icon's order slot; a once-per-save event does not need its own.
    IconHandler.Handler.EnqueueIcon(
      "Birthday",
      (batch, pos) =>
      {
        icon.bounds.X = pos.X;
        icon.bounds.Y = pos.Y;
        icon.draw(batch);

        Tools.DrawPulsingExclamation(
          batch,
          new Vector2(pos.X + 30 + 2.5f * ExclamationScale, pos.Y + 16 + 7f * ExclamationScale),
          ExclamationScale,
          Tools.ExclamationOrigin
        );
      },
      batch =>
      {
        if (AndroidHud.IsHovered(icon))
        {
          IClickableMenu.drawHoverText(
            batch,
            I18n.WeddingTomorrow(name: spouseDisplayName),
            Game1.smallFont
          );
        }
      }
    );
  }
  #endregion
}
