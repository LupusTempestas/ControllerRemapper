using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace WolverineRemapper.Services
{
    /// <summary>One selectable UI language.</summary>
    public sealed record Language(string Code, string NativeName)
    {
        public override string ToString() => NativeName;
    }

    /// <summary>
    /// Runtime-switchable UI strings. Catalogues are embedded JSON files
    /// (Localization/{code}.json, flat key → text). XAML binds through the
    /// indexer: <c>{Binding [key], Source={x:Static svc:L10n.I}}</c>; changing
    /// <see cref="CurrentLanguage"/> raises <c>Item[]</c> so every binding
    /// refreshes without a restart. Missing keys fall back to English, then to
    /// the key itself so a gap is visible rather than blank.
    /// </summary>
    public sealed class L10n : INotifyPropertyChanged
    {
        // Static initializers run in textual order: Available must exist
        // before the singleton's constructor reads Available[0].
        public static readonly IReadOnlyList<Language> Available = new[]
        {
            new Language("en", "English"),
            new Language("fr", "Français"),
            new Language("de", "Deutsch"),
            new Language("es", "Español"),
            new Language("nl", "Nederlands"),
        };

        public static readonly L10n I = new();

        private readonly Dictionary<string, Dictionary<string, string>> _catalogues = new();
        private Dictionary<string, string> _current;
        private readonly Dictionary<string, string> _english;
        private Language _language = Available[0];

        private L10n()
        {
            _english = Load("en");
            _current = _english;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Called after the language changes (menus that are rebuilt by code hook this).</summary>
        public event Action? LanguageChanged;

        public Language CurrentLanguage
        {
            get => _language;
            set
            {
                if (value == null || value.Code == _language.Code) return;
                _language = value;
                _current = Load(value.Code);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                LanguageChanged?.Invoke();
            }
        }

        /// <summary>Indexer for XAML bindings.</summary>
        public string this[string key] => T(key);

        /// <summary>Look up a string.</summary>
        public string T(string key)
        {
            if (_current.TryGetValue(key, out var s) && !string.IsNullOrEmpty(s)) return s;
            if (_english.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e)) return e;
            return key;
        }

        /// <summary>Look up a format string and apply arguments.</summary>
        public string F(string key, params object?[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, T(key), args); }
            catch (FormatException) { return T(key); }
        }

        /// <summary>Best match for the OS language, or English.</summary>
        public static Language Detect()
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Available.FirstOrDefault(l => l.Code.Equals(two, StringComparison.OrdinalIgnoreCase)) ?? Available[0];
        }

        public static Language FromCode(string? code) =>
            Available.FirstOrDefault(l => l.Code.Equals(code ?? "", StringComparison.OrdinalIgnoreCase)) ?? Detect();

        private Dictionary<string, string> Load(string code)
        {
            if (_catalogues.TryGetValue(code, out var cached)) return cached;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string? name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith($".Localization.{code}.json", StringComparison.OrdinalIgnoreCase));
                if (name != null)
                {
                    using var stream = asm.GetManifestResourceStream(name)!;
                    using var reader = new StreamReader(stream);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                    if (parsed != null) dict = parsed;
                }
            }
            catch
            {
                // A broken catalogue must never take the UI down: fall back to English.
            }
            _catalogues[code] = dict;
            return dict;
        }
    }
}
