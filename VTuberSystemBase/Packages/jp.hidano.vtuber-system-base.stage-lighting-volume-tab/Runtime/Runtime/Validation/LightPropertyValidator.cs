#nullable enable
using VTuberSystemBase.StageLightingVolumeTab.Contracts;

namespace VTuberSystemBase.StageLightingVolumeTab.Validation
{
    /// <summary>
    /// Single source of truth for value-range validation across both Light properties
    /// and Volume Override params. Used by the View layer (inline error display) and
    /// the Volume param factory (initial value sanity). See design.md §Validation
    /// §LightPropertyValidator (Requirements 5.7, 6.7, 9.3).
    /// </summary>
    public static class LightPropertyValidator
    {
        // ---------- Light property validators ----------

        public static ValidationResult ValidateIntensity(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return ValidationResult.Invalid("invalid_number", "intensity must be a finite number");
            if (value < 0f)
                return ValidationResult.Invalid("out_of_range_min", "intensity must be >= 0");
            return ValidationResult.Valid();
        }

        public static ValidationResult ValidateRange(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return ValidationResult.Invalid("invalid_number", "range must be a finite number");
            if (value < 0f)
                return ValidationResult.Invalid("out_of_range_min", "range must be >= 0");
            return ValidationResult.Valid();
        }

        public static ValidationResult ValidateSpotAngle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return ValidationResult.Invalid("invalid_number", "spotAngle must be a finite number");
            if (value < 1f)
                return ValidationResult.Invalid("out_of_range_min", "spotAngle must be >= 1");
            if (value > 179f)
                return ValidationResult.Invalid("out_of_range_max", "spotAngle must be <= 179");
            return ValidationResult.Valid();
        }

        public static ValidationResult ValidateColor(ColorDto color)
        {
            // Per-channel 0..1 nominal range; HDR may exceed but the validator only
            // forbids negative / NaN values. The Color picker UI clamps the visible
            // range; this validator catches programmatic bad input.
            if (HasInvalidFloat(color.R) || HasInvalidFloat(color.G)
                || HasInvalidFloat(color.B) || HasInvalidFloat(color.A))
                return ValidationResult.Invalid("invalid_number", "color channels must be finite");
            if (color.R < 0f || color.G < 0f || color.B < 0f || color.A < 0f)
                return ValidationResult.Invalid("out_of_range_min", "color channels must be >= 0");
            return ValidationResult.Valid();
        }

        // ---------- Volume Override param validator ----------

        public static ValidationResult ValidateVolumeParam(
            VolumeOverrideParamDto schema,
            VolumeOverrideParamValueDto value)
        {
            if (schema.Kind != value.Kind)
                return ValidationResult.Invalid("kind_mismatch",
                    $"expected {schema.Kind}, got {value.Kind}");

            switch (schema.Kind)
            {
                case ParamKind.Bool:
                    if (value.BoolValue is null)
                        return ValidationResult.Invalid("missing_value", "Bool kind requires BoolValue");
                    return ValidationResult.Valid();

                case ParamKind.Int:
                    if (value.IntValue is null)
                        return ValidationResult.Invalid("missing_value", "Int kind requires IntValue");
                    var iv = value.IntValue.Value;
                    if (schema.Range is { } r && r.IntMin is { } imin && iv < imin)
                        return ValidationResult.Invalid("out_of_range_min", $"int < {imin}");
                    if (schema.Range is { } r2 && r2.IntMax is { } imax && iv > imax)
                        return ValidationResult.Invalid("out_of_range_max", $"int > {imax}");
                    return ValidationResult.Valid();

                case ParamKind.Float:
                case ParamKind.ClampedFloat:
                    if (value.FloatValue is null)
                        return ValidationResult.Invalid("missing_value", "Float kind requires FloatValue");
                    var fv = value.FloatValue.Value;
                    if (HasInvalidFloat(fv))
                        return ValidationResult.Invalid("invalid_number", "float must be finite");
                    if (schema.Range is { } fr && fr.FloatMin is { } fmin && fv < fmin)
                        return ValidationResult.Invalid("out_of_range_min", $"float < {fmin}");
                    if (schema.Range is { } fr2 && fr2.FloatMax is { } fmax && fv > fmax)
                        return ValidationResult.Invalid("out_of_range_max", $"float > {fmax}");
                    return ValidationResult.Valid();

                case ParamKind.Color:
                    if (value.ColorValue is null)
                        return ValidationResult.Invalid("missing_value", "Color kind requires ColorValue");
                    return ValidateColor(value.ColorValue.Value);

                case ParamKind.Vector2:
                case ParamKind.Vector3:
                case ParamKind.Vector4:
                    if (value.VectorValue is null)
                        return ValidationResult.Invalid("missing_value", "Vector kind requires VectorValue");
                    var v = value.VectorValue.Value;
                    if (HasInvalidFloat(v.X) || HasInvalidFloat(v.Y)
                        || HasInvalidFloat(v.Z) || HasInvalidFloat(v.W))
                        return ValidationResult.Invalid("invalid_number", "vector components must be finite");
                    return ValidationResult.Valid();

                case ParamKind.Enum:
                    if (value.EnumValue is null)
                        return ValidationResult.Invalid("missing_value", "Enum kind requires EnumValue");
                    if (schema.Range is { } er && er.EnumValues is { } enums)
                    {
                        bool found = false;
                        for (int i = 0; i < enums.Count; i++)
                        {
                            if (string.Equals(enums[i], value.EnumValue, System.StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                            return ValidationResult.Invalid("invalid_enum",
                                $"value '{value.EnumValue}' not in enum");
                    }
                    return ValidationResult.Valid();

                case ParamKind.Unknown:
                    // Unknown kinds are skipped at the UI layer (Req 6.10); not a
                    // validation failure per se but flagged so callers know to skip.
                    return ValidationResult.Invalid("unknown_kind",
                        "kind is Unknown, skip per Req 6.10");

                default:
                    return ValidationResult.Invalid("unsupported_kind",
                        $"unhandled ParamKind: {schema.Kind}");
            }
        }

        private static bool HasInvalidFloat(float f) => float.IsNaN(f) || float.IsInfinity(f);
    }

    public readonly struct ValidationResult
    {
        public bool IsValid { get; init; }
        public string? ErrorCode { get; init; }
        public string? Detail { get; init; }

        public static ValidationResult Valid() => new ValidationResult { IsValid = true };

        public static ValidationResult Invalid(string code, string? detail = null) =>
            new ValidationResult { IsValid = false, ErrorCode = code, Detail = detail };
    }
}
