// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrubShot.Core.Settings;

/// <summary>One serializer configuration for the settings file and the policy merge.</summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Enums are names only. Undefined ordinals in a file are reset by SettingsDocument.Normalize before binding.
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };
}
