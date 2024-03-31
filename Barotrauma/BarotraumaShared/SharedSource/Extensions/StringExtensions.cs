#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma
{
    static class StringExtensions
    {

        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this ContentPath? p) => p?.IsPathNullOrEmpty() ?? true;
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this ContentPath? p) => p?.IsPathNullOrWhiteSpace() ?? true;

        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this LocalizedString? s) => s is null || string.IsNullOrEmpty(s.Value);
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this LocalizedString? s) => s is null || string.IsNullOrWhiteSpace(s.Value);
        public static bool IsNullOrEmpty([NotNullWhen(returnValue: false)]this RichString? s) => s is null || s.NestedStr.IsNullOrEmpty();
        public static bool IsNullOrWhiteSpace([NotNullWhen(returnValue: false)]this RichString? s) => s is null || s.NestedStr.IsNullOrWhiteSpace();
    }
}