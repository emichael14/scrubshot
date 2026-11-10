// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Capture;
using ScrubShot.Core.Settings;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ScrubShot.Output;

/// <summary>Encodes a <see cref="CapturedImage"/> to PNG or JPEG bytes in memory with the in-box Windows Imaging Component.</summary>
public static class ImageEncoder
{
    /// <summary>PNG, lossless, alpha ignored (captures are opaque).</summary>
    public static Task<byte[]> EncodePngAsync(CapturedImage image, CancellationToken cancellationToken = default) =>
        EncodeAsync(image, BitmapEncoder.PngEncoderId, null, cancellationToken);

    /// <summary>JPEG at <paramref name="quality"/> from 1 to 100 (clamped). Alpha is dropped by the format.</summary>
    public static Task<byte[]> EncodeJpgAsync(CapturedImage image, int quality, CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(quality, 1, 100);
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(clamped / 100f, PropertyType.Single),
        };
        return EncodeAsync(image, BitmapEncoder.JpegEncoderId, options, cancellationToken);
    }

    public static Task<byte[]> EncodeAsync(CapturedImage image, ImageFormat format, int jpgQuality, CancellationToken cancellationToken = default) =>
        format == ImageFormat.Jpg ? EncodeJpgAsync(image, jpgQuality, cancellationToken) : EncodePngAsync(image, cancellationToken);

    public static string ExtensionFor(ImageFormat format) => format == ImageFormat.Jpg ? ".jpg" : ".png";

    private static async Task<byte[]> EncodeAsync(CapturedImage image, Guid encoderId, BitmapPropertySet? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new ArgumentException("The image is empty.", nameof(image));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = options is null
            ? await BitmapEncoder.CreateAsync(encoderId, stream).AsTask(cancellationToken).ConfigureAwait(false)
            : await BitmapEncoder.CreateAsync(encoderId, stream, options).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)image.Width, (uint)image.Height, 96, 96, image.Pixels);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        var length = checked((int)stream.Size);
        var bytes = new byte[length];
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)length).AsTask(cancellationToken).ConfigureAwait(false);
        if (loaded != length)
        {
            throw new IOException("The encoder produced fewer bytes than it reported.");
        }

        reader.ReadBytes(bytes);
        return bytes;
    }
}
