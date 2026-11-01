// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

namespace ScrubShot.Core.Geometry;

/// <summary>A point in physical pixels. Virtual-screen coordinates can be negative.</summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public static PixelPoint Zero => default;

    public PixelPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A size in physical pixels. Never negative.</summary>
public readonly record struct PixelSize
{
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public bool IsEmpty => Width == 0 || Height == 0;

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// An axis-aligned rectangle in physical pixels, stored as origin plus size. <see cref="Right"/> and
/// <see cref="Bottom"/> are exclusive, matching Win32 <c>RECT</c> semantics.
/// </summary>
public readonly record struct PixelRect
{
    public PixelRect(int left, int top, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public static PixelRect Empty => default;

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public PixelPoint Location => new(Left, Top);

    public PixelSize Size => new(Width, Height);

    public bool IsEmpty => Width == 0 || Height == 0;

    public long Area => (long)Width * Height;

    /// <summary>Builds a rectangle from edges; <paramref name="right"/> and <paramref name="bottom"/> are exclusive.</summary>
    public static PixelRect FromEdges(int left, int top, int right, int bottom)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(right, left);
        ArgumentOutOfRangeException.ThrowIfLessThan(bottom, top);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Builds a rectangle from any two corner points, in any order, so a drag from bottom-right to
    /// top-left yields the same rectangle as the reverse. The far corner is exclusive, so two equal points
    /// give an empty rectangle.
    /// </summary>
    public static PixelRect FromPoints(PixelPoint a, PixelPoint b) =>
        FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public bool Contains(PixelPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Contains(PixelRect other) =>
        other.IsEmpty || (other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom);

    public bool IntersectsWith(PixelRect other) =>
        !IsEmpty && !other.IsEmpty && other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;

    /// <summary>The overlap, or <see cref="Empty"/> when there is none.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        if (!IntersectsWith(other))
        {
            return Empty;
        }

        return FromEdges(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    }

    /// <summary>The smallest rectangle containing both. An empty operand contributes nothing.</summary>
    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public PixelRect Translate(int dx, int dy) => new(Left + dx, Top + dy, Width, Height);

    public PixelRect Translate(PixelPoint delta) => Translate(delta.X, delta.Y);

    /// <summary>Clamps a point into this rectangle (inclusive of the far edge, for cursor positions).</summary>
    public PixelPoint ClampInclusive(PixelPoint point) =>
        new(Math.Clamp(point.X, Left, Math.Max(Left, Right - 1)), Math.Clamp(point.Y, Top, Math.Max(Top, Bottom - 1)));

    public override string ToString() => $"[{Left}, {Top}, {Width}x{Height}]";
}
