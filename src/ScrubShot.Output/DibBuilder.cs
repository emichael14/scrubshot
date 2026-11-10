// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Buffers.Binary;
using ScrubShot.Capture;

namespace ScrubShot.Output;

/// <summary>
/// Builds the <c>CF_DIB</c> clipboard payload: a 40-byte <c>BITMAPINFOHEADER</c> followed by bottom-up 32-bit BGRA
/// rows. Pure byte work so it can be unit tested without a clipboard.
/// </summary>
public static class DibBuilder
{
    public const int HeaderSize = 40;

    public static byte[] Build(CapturedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var stride = image.Stride;
        var imageSize = checked(stride * image.Height);
        var buffer = new byte[checked(HeaderSize + imageSize)];
        var header = buffer.AsSpan(0, HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(header[0..], HeaderSize);             // biSize
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], image.Width);              // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], image.Height);             // biHeight > 0: bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);                      // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);                     // biBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 0);                      // biCompression = BI_RGB
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)imageSize);        // biSizeImage
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 0);                       // biXPelsPerMeter
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 0);                       // biYPelsPerMeter
        BinaryPrimitives.WriteUInt32LittleEndian(header[32..], 0);                      // biClrUsed
        BinaryPrimitives.WriteUInt32LittleEndian(header[36..], 0);                      // biClrImportant

        // The capture is top-down; a positive-height DIB stores the bottom row first.
        var source = image.Pixels.AsSpan();
        for (var row = 0; row < image.Height; row++)
        {
            var sourceRow = source.Slice(row * stride, stride);
            var targetRow = buffer.AsSpan(HeaderSize + ((image.Height - 1 - row) * stride), stride);
            sourceRow.CopyTo(targetRow);
        }

        return buffer;
    }
}
