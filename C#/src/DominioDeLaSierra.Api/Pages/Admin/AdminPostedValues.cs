using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace DominioDeLaSierra.Api.Pages.Admin;

internal static class AdminPostedValues
{
    public static bool HasBindingError(ModelStateDictionary modelState, string key) =>
        modelState.TryGetValue(key, out var entry) && entry is { Errors.Count: > 0 };

    public static bool TryReadProductKind(ModelStateDictionary modelState, string key, out ProductKind kind)
    {
        kind = default;
        if (!TryGetFirstValue(modelState, key, out var raw))
        {
            return false;
        }

        return raw is "Standard" or "Wine" or "Pack"
            && Enum.TryParse(raw, ignoreCase: false, out kind)
            && Enum.IsDefined(kind);
    }

    public static bool TryReadRole(ModelStateDictionary modelState, string key, out AdminRole role)
    {
        role = default;
        if (!TryGetFirstValue(modelState, key, out var raw))
        {
            return false;
        }

        return Enum.TryParse(raw, ignoreCase: false, out role) && Enum.IsDefined(role);
    }

    public static bool TryReadBool(ModelStateDictionary modelState, string key, out bool value)
    {
        value = false;
        if (!TryGetFirstValue(modelState, key, out var raw))
        {
            return false;
        }

        return bool.TryParse(raw, out value);
    }

    public static bool TryReadGuid(ModelStateDictionary modelState, string key, Guid boundValue, out Guid value)
    {
        value = Guid.Empty;
        if (HasBindingError(modelState, key))
        {
            return false;
        }

        if (TryGetFirstValue(modelState, key, out var raw))
        {
            return Guid.TryParse(raw, out value) && value != Guid.Empty;
        }

        if (boundValue == Guid.Empty)
        {
            return false;
        }

        value = boundValue;
        return true;
    }

    private static bool TryGetFirstValue(ModelStateDictionary modelState, string key, out string value)
    {
        value = string.Empty;
        if (!modelState.TryGetValue(key, out var entry) || entry is null || entry.Errors.Count > 0)
        {
            return false;
        }

        switch (entry.RawValue)
        {
            case string text when text.Length > 0:
                value = text;
                return true;
            case string[] { Length: > 0 } items when !string.IsNullOrEmpty(items[0]):
                value = items[0];
                return true;
            case StringValues values when values.Count > 0 && !string.IsNullOrEmpty(values[0]):
                value = values[0]!;
                return true;
        }

        if (string.IsNullOrEmpty(entry.AttemptedValue) || entry.AttemptedValue.Contains(','))
        {
            return false;
        }

        value = entry.AttemptedValue;
        return true;
    }
}
