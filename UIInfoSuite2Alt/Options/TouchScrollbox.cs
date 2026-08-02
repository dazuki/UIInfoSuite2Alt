using System;
using Microsoft.Xna.Framework;

namespace UIInfoSuite2Alt.Options;

/// <summary>
///   Touch drag-scrolling with fling momentum, ported from the Android game's
///   <c>StardewValley.Menus.MobileScrollbox</c>, which only exists in the Android game assembly.
///   <see cref="YOffsetForScroll" /> is zero at the top and negative as content scrolls up.
/// </summary>
internal class TouchScrollbox
{
  private const float MinSpeed = 1f;
  private const float DampingFactor = 1.05f;

  /// <summary>Drag distance before a press counts as a scroll rather than a tap.</summary>
  private const int YChangeToRegisterScroll = 12;

  private readonly float[] _speedMeasure = new float[8];
  private int _currentSpeedMeasure;
  private float _speed;
  private int _oldYDiff;
  private int _panelScrollStartY;
  private int _yOffsetAtStartOfPanelScroll;
  private int _lastYValue;

  public Rectangle Bounds;

  private bool PanelScrolling { get; set; }

  /// <summary>The press moved far enough to be a drag, so it no longer counts as a tap.</summary>
  public bool HavePanelScrolled { get; private set; }

  public bool ScrollingWithMomentum { get; private set; }

  private int MaxYOffset { get; set; } = 1;

  public int YOffsetForScroll { get; private set; }

  public void SetMaxYOffset(int offset)
  {
    MaxYOffset = offset == 0 ? 1 : offset;
  }

  public void SetYOffsetForScroll(int offset)
  {
    YOffsetForScroll = offset;
  }

  public void StopMomentum()
  {
    _speed = 0f;
    ScrollingWithMomentum = false;
  }

  /// <summary>Advances fling momentum. Returns true while the offset is still changing.</summary>
  public bool Update()
  {
    if (!ScrollingWithMomentum)
    {
      return false;
    }

    if (Math.Abs(_speed) <= MinSpeed)
    {
      ScrollingWithMomentum = false;
      return false;
    }

    YOffsetForScroll += (int)_speed;

    if (YOffsetForScroll > 0)
    {
      YOffsetForScroll = 0;
      StopMomentum();
      return true;
    }

    if (YOffsetForScroll < -MaxYOffset)
    {
      YOffsetForScroll = -MaxYOffset;
      StopMomentum();
      return true;
    }

    // Decay faster near either end so the fling settles instead of hitting the stop hard
    float extraDamping = 1f;
    if (_speed < 0f)
    {
      float progress = YOffsetForScroll / (float)-MaxYOffset;
      if (progress > 0.9f)
      {
        extraDamping = Math.Max(1f, (progress - 0.9f) * 20f);
      }
    }
    else
    {
      float progress = (MaxYOffset + YOffsetForScroll) / (float)MaxYOffset;
      if (progress > 0.9f)
      {
        extraDamping = Math.Max(1f, (progress - 0.9f) * 20f);
      }
    }

    _speed /= DampingFactor * extraDamping;
    return true;
  }

  public void ReceiveLeftClick(int x, int y)
  {
    _speed = 0f;
    ScrollingWithMomentum = false;

    if (!Bounds.Contains(x, y))
    {
      return;
    }

    PanelScrolling = true;
    HavePanelScrolled = false;
    _panelScrollStartY = y;
    _yOffsetAtStartOfPanelScroll = YOffsetForScroll;
    Array.Clear(_speedMeasure, 0, _speedMeasure.Length);
    _currentSpeedMeasure = 0;
  }

  /// <summary>Applies drag movement. Returns true if <see cref="YOffsetForScroll" /> changed.</summary>
  public bool LeftClickHeld(int x, int y)
  {
    if (IsPullingPastEnd(y))
    {
      _panelScrollStartY = y;
      return false;
    }

    if (PanelScrolling && !HavePanelScrolled)
    {
      if (
        y > _panelScrollStartY + YChangeToRegisterScroll
        || y < _panelScrollStartY - YChangeToRegisterScroll
      )
      {
        HavePanelScrolled = true;
        _lastYValue = y;
      }
    }

    if (!HavePanelScrolled)
    {
      return false;
    }

    int diff = y - _lastYValue;
    int offsetBefore = YOffsetForScroll;

    if (diff > 0)
    {
      // Direction reversal: re-anchor so the content does not jump
      if (_oldYDiff <= 0)
      {
        _panelScrollStartY = y;
        _yOffsetAtStartOfPanelScroll = YOffsetForScroll;
        _oldYDiff = diff;
        _lastYValue = y;
        return false;
      }

      YOffsetForScroll = Math.Min(0, _yOffsetAtStartOfPanelScroll + y - _panelScrollStartY);
    }
    else if (diff < 0)
    {
      if (_oldYDiff >= 0)
      {
        _panelScrollStartY = y;
        _yOffsetAtStartOfPanelScroll = YOffsetForScroll;
        _oldYDiff = diff;
        _lastYValue = y;
        return false;
      }

      YOffsetForScroll = Math.Max(
        -MaxYOffset,
        _yOffsetAtStartOfPanelScroll + y - _panelScrollStartY
      );
    }

    _oldYDiff = diff;
    _speedMeasure[_currentSpeedMeasure] = diff;
    _lastYValue = y;

    _currentSpeedMeasure++;
    if (_currentSpeedMeasure >= _speedMeasure.Length)
    {
      _currentSpeedMeasure = 0;
    }

    return YOffsetForScroll != offsetBefore;
  }

  public void ReleaseLeftClick()
  {
    if (HavePanelScrolled)
    {
      _speed = 0f;
      foreach (float measure in _speedMeasure)
      {
        _speed += measure;
      }

      _speed /= _speedMeasure.Length;
      ScrollingWithMomentum = true;
    }

    PanelScrolling = false;
    HavePanelScrolled = false;
  }

  private bool IsPullingPastEnd(int y)
  {
    return (y >= _panelScrollStartY && _yOffsetAtStartOfPanelScroll >= 0)
      || (y <= _panelScrollStartY && _yOffsetAtStartOfPanelScroll <= -MaxYOffset);
  }
}
