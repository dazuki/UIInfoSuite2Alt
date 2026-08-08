using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;

namespace UIInfoSuite2Alt.UIElements;

public class ShowBookseller : IDisposable
{
  #region Properties
  private bool _booksellerIsHere;
  private readonly PerScreen<bool> _booksellerIsVisited = new();
  private readonly PerScreen<ClickableTextureComponent> _booksellerIcon = new();

  private bool Enabled { get; set; }
  private bool HideWhenVisited { get; set; }

  private readonly IModHelper _helper;
  #endregion


  #region Lifecycle
  public ShowBookseller(IModHelper helper)
  {
    _helper = helper;
  }

  public void Dispose()
  {
    ToggleOption(false);
  }

  public void ToggleOption(bool showBookseller)
  {
    Enabled = showBookseller;

    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.DayStarted -= OnDayStarted;
    _helper.Events.Display.MenuChanged -= OnMenuChanged;

    if (showBookseller)
    {
      UpdateBookseller();
      _helper.Events.Display.RenderingHud += OnRenderingHud;
      _helper.Events.GameLoop.DayStarted += OnDayStarted;
      _helper.Events.Display.MenuChanged += OnMenuChanged;
    }
  }

  public void ToggleHideWhenVisitedOption(bool hideWhenVisited)
  {
    HideWhenVisited = hideWhenVisited;
    ToggleOption(Enabled);
  }
  #endregion


  #region Event subscriptions
  private void OnDayStarted(object? sender, EventArgs e)
  {
    UpdateBookseller();
  }

  private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
  {
    if (e.NewMenu is ShopMenu menu && menu.ShopId == "Bookseller")
    {
      _booksellerIsVisited.Value = true;
    }
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (UIElementUtils.IsRenderingNormally() && ShouldDrawIcon())
    {
      IconHandler.Handler.EnqueueIcon(
        "Bookseller",
        (batch, pos) =>
        {
          _booksellerIcon.Value = new ClickableTextureComponent(
            new Rectangle(pos.X, pos.Y, 40, 40),
            Game1.mouseCursors_1_6,
            new Rectangle(52, 477, 20, 20),
            2f
          );
          _booksellerIcon.Value.draw(batch);
        },
        batch =>
        {
          if (_booksellerIcon.Value?.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY) ?? false)
          {
            IClickableMenu.drawHoverText(batch, I18n.BooksellerIsInTown(), Game1.smallFont);
          }
        }
      );
    }
  }
  #endregion


  #region Logic
  private void UpdateBookseller()
  {
    var booksellerDays = Utility.getDaysOfBooksellerThisSeason();
    _booksellerIsHere = booksellerDays.Contains(Game1.dayOfMonth);
    _booksellerIsVisited.Value = false;
  }

  private bool ShouldDrawIcon()
  {
    return _booksellerIsHere && (!_booksellerIsVisited.Value || !HideWhenVisited);
  }
  #endregion
}
