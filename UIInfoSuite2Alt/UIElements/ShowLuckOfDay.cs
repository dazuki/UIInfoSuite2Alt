using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using UIInfoSuite2Alt.Infrastructure;
using UIInfoSuite2Alt.Patches;

namespace UIInfoSuite2Alt.UIElements;

internal class ShowLuckOfDay : IDisposable
{
  #region Properties
  private const int CloverFrameSize = 26;
  private const float CloverScale = Game1.pixelZoom / 2.6f;

  private readonly PerScreen<string> _hoverText = new(() => string.Empty);
  private readonly PerScreen<int> _cloverFrame = new(() => 4);
  private readonly PerScreen<Color> _diceColor = new(() => new Color(Color.White.ToVector4()));

  private readonly Texture2D _cloverTexture;
  private readonly Texture2D _tvLuckTexture;

  private readonly PerScreen<ClickableTextureComponent> _icon;

  private readonly IModHelper _helper;

  private const int IconStyleDice = 1;
  private const int IconStyleTvFortune = 2;
  private const int TvFrameSize = 20;
  private const float TvIconScale = Game1.pixelZoom / 2f;

  private readonly PerScreen<int> _tvFrame = new(() => 3);

  private bool Enabled { get; set; }
  private bool ShowExactValue { get; set; }
  private bool RequireTv { get; set; }
  private int IconStyle { get; set; }

  // Classic dice icon colors
  private static readonly Color Luck1Color = new(87, 255, 106, 255);
  private static readonly Color Luck2Color = new(148, 255, 210, 255);
  private static readonly Color Luck3Color = new(246, 255, 145, 255);
  private static readonly Color Luck4Color = new(255, 255, 255, 255);
  private static readonly Color Luck5Color = new(255, 155, 155, 255);
  private static readonly Color Luck6Color = new(165, 165, 165, 204);
  #endregion

  #region Lifecycle
  public ShowLuckOfDay(IModHelper helper)
  {
    _helper = helper;
    _cloverTexture = AssetHelper.TryLoadTextureFromFile(
      Path.Combine(helper.DirectoryPath, "assets", "clover_group.png")
    );
    _tvLuckTexture = AssetHelper.TryLoadTextureFromFile(
      Path.Combine(helper.DirectoryPath, "assets", "tv_group.png")
    );
    _icon = new PerScreen<ClickableTextureComponent>(() => CreateIcon());
  }

  public void Dispose()
  {
    ToggleOption(false);

    if (!AssetHelper.IsFallback(_cloverTexture))
    {
      _cloverTexture.Dispose();
    }

    if (!AssetHelper.IsFallback(_tvLuckTexture))
    {
      _tvLuckTexture.Dispose();
    }
  }

  public void ToggleOption(bool showLuckOfDay)
  {
    Enabled = showLuckOfDay;

    _helper.Events.Player.Warped -= OnWarped;
    _helper.Events.Display.RenderingHud -= OnRenderingHud;
    _helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;

    if (showLuckOfDay)
    {
      AdjustIconXToBlackBorder();
      _helper.Events.Player.Warped += OnWarped;
      _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
      _helper.Events.Display.RenderingHud += OnRenderingHud;
    }
  }

  public void ToggleShowExactValueOption(bool showExactValue)
  {
    ShowExactValue = showExactValue;
    ToggleOption(Enabled);
  }

  public void ToggleRequireTvOption(bool requireTv)
  {
    RequireTv = requireTv;
    ToggleOption(Enabled);
  }

  public void SetIconStyle(int iconStyle)
  {
    IconStyle = iconStyle;
    AdjustIconXToBlackBorder();
  }
  #endregion

  #region Event subscriptions
  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    CalculateLuck(e);
  }

  private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
  {
    if (
      UIElementUtils.IsRenderingNormally()
      && (!RequireTv || TvChannelWatcher.HasWatchedFortune.Value)
    )
    {
      switch (IconStyle)
      {
        case IconStyleDice:
          DrawClassicIcon();
          break;
        case IconStyleTvFortune:
          DrawTvIcon();
          break;
        default:
          DrawCloverIcon();
          break;
      }
    }
  }

  private void DrawCloverIcon()
  {
    IconHandler.Handler.EnqueueIcon(
      "Luck",
      (batch, pos) =>
      {
        ClickableTextureComponent icon = _icon.Value;
        icon.bounds.X = pos.X;
        icon.bounds.Y = pos.Y;
        icon.sourceRect = new Rectangle(
          _cloverFrame.Value * CloverFrameSize,
          0,
          CloverFrameSize,
          CloverFrameSize
        );
        _icon.Value = icon;
        _icon.Value.draw(batch, Color.White * 0.9f, 1f);
      },
      batch =>
      {
        if (_icon.Value.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY))
        {
          IClickableMenu.drawHoverText(batch, _hoverText.Value, Game1.smallFont);
        }
      }
    );
  }

  private void DrawTvIcon()
  {
    int scaledSize = (int)(TvFrameSize * TvIconScale);

    IconHandler.Handler.EnqueueIcon(
      "Luck",
      (batch, pos) =>
      {
        var sourceRect = new Rectangle(_tvFrame.Value * TvFrameSize, 0, TvFrameSize, TvFrameSize);
        var destRect = new Rectangle(pos.X, pos.Y, scaledSize, scaledSize);

        batch.Draw(_tvLuckTexture, destRect, sourceRect, Color.White);

        // Update icon bounds for hover detection
        ClickableTextureComponent icon = _icon.Value;
        icon.bounds = destRect;
        _icon.Value = icon;
      },
      batch =>
      {
        if (_icon.Value.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY))
        {
          IClickableMenu.drawHoverText(batch, _hoverText.Value, Game1.smallFont);
        }
      }
    );
  }

  private void DrawClassicIcon()
  {
    IconHandler.Handler.EnqueueIcon(
      "Luck",
      (batch, pos) =>
      {
        ClickableTextureComponent icon = _icon.Value;
        icon.bounds.X = pos.X;
        icon.bounds.Y = pos.Y;
        _icon.Value = icon;
        _icon.Value.draw(batch, _diceColor.Value, 1f);
      },
      batch =>
      {
        if (_icon.Value.containsPoint(AndroidHud.MouseX, AndroidHud.MouseY))
        {
          IClickableMenu.drawHoverText(batch, _hoverText.Value, Game1.smallFont);
        }
      }
    );
  }
  #endregion

  #region Logic
  private void CalculateLuck(UpdateTickedEventArgs e)
  {
    if (e.IsMultipleOf(30)) // half second
    {
      double luck = Game1.player.DailyLuck;
      double sharedLuck = Game1.player.team.sharedDailyLuck.Value;

      // Tiers use DailyLuck (includes Special Charm) with the thresholds the TV fortune teller
      // uses: -0.07, -0.02, +0.02, +0.07. Within the outer tiers the end frames are picked from
      // sharedDailyLuck (base value, before Special Charm), which reaches +-0.12 from the shrine
      // and +-0.1 from the daily roll, so those frames cover both.
      if (luck < -0.07)
      {
        // Very bad luck; end frame for the shrine and the unluckiest rolls
        bool isExtreme = sharedLuck <= -0.09;
        _hoverText.Value = I18n.LuckStatus6();
        _cloverFrame.Value = isExtreme ? 0 : 1;
        _diceColor.Value = Luck6Color;
        _tvFrame.Value = isExtreme ? 0 : 1;
      }
      else if (luck < -0.02)
      {
        // Bad luck
        _hoverText.Value = I18n.LuckStatus5();
        _cloverFrame.Value = 2;
        _diceColor.Value = Luck5Color;
        _tvFrame.Value = 2;
      }
      else if (luck == 0)
      {
        // Absolutely neutral
        _hoverText.Value = I18n.LuckStatus4();
        _cloverFrame.Value = 3;
        _diceColor.Value = Luck4Color;
        _tvFrame.Value = 3;
      }
      else if (luck <= 0.02)
      {
        // Near-neutral (non-zero, between -0.02 and +0.02)
        _hoverText.Value = I18n.LuckStatus3();
        _cloverFrame.Value = 4;
        _diceColor.Value = Luck3Color;
        _tvFrame.Value = 3;
      }
      else if (luck <= 0.07)
      {
        // Good luck
        _hoverText.Value = I18n.LuckStatus2();
        _cloverFrame.Value = 5;
        _diceColor.Value = Luck2Color;
        _tvFrame.Value = 4;
      }
      else
      {
        // Very good luck; end frame for the shrine and the luckiest rolls
        bool isExtreme = sharedLuck >= 0.09;
        _hoverText.Value = I18n.LuckStatus1();
        _cloverFrame.Value = isExtreme ? 7 : 6;
        _diceColor.Value = Luck1Color;
        _tvFrame.Value = isExtreme ? 6 : 5;
      }

      // Rewrite the text, but keep the frame/color
      if (ShowExactValue)
      {
        _hoverText.Value = string.Format(
          I18n.DailyLuckValue(),
          Game1.player.DailyLuck.ToString("N3")
        );
      }
    }
  }

  private void OnWarped(object? sender, WarpedEventArgs e)
  {
    // adjust icon X to black border
    if (e.IsLocalPlayer)
    {
      AdjustIconXToBlackBorder();
    }
  }

  private void AdjustIconXToBlackBorder()
  {
    _icon.Value = CreateIcon();
  }

  private ClickableTextureComponent CreateIcon()
  {
    if (IconStyle == IconStyleDice)
    {
      return new ClickableTextureComponent(
        "",
        new Rectangle(
          Tools.GetWidthInPlayArea() - 134,
          290,
          10 * Game1.pixelZoom,
          10 * Game1.pixelZoom
        ),
        "",
        "",
        Game1.mouseCursors,
        new Rectangle(50, 428, 10, 14),
        Game1.pixelZoom
      );
    }

    if (IconStyle == IconStyleTvFortune)
    {
      int tvScaledSize = (int)(TvFrameSize * TvIconScale);
      return new ClickableTextureComponent(
        "",
        new Rectangle(Tools.GetWidthInPlayArea() - 134, 290, tvScaledSize, tvScaledSize),
        "",
        "",
        _tvLuckTexture,
        new Rectangle(_tvFrame.Value * TvFrameSize, 0, TvFrameSize, TvFrameSize),
        TvIconScale
      );
    }

    int scaledSize = (int)(CloverFrameSize * CloverScale);
    return new ClickableTextureComponent(
      "",
      new Rectangle(Tools.GetWidthInPlayArea() - 134, 290, scaledSize, scaledSize),
      "",
      "",
      _cloverTexture,
      new Rectangle(_cloverFrame.Value * CloverFrameSize, 0, CloverFrameSize, CloverFrameSize),
      CloverScale
    );
  }
  #endregion
}
