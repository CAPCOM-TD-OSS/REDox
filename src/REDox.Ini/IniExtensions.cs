// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace REDox.Ini;

/// <summary>Provides INI parsing and writing extensions for REDox values and objects.</summary>
public static class IniExtensions
{
    extension(DValue doxValue)
    {
        /// <summary>Parses an INI string into a value backed by an independent document snapshot.</summary>
        /// <param name="ini">The INI text to parse.</param>
        /// <param name="settings">
        ///     The serializer settings, or <see langword="null" /> to use
        ///     <see cref="SerializerSettings.Default" />.
        /// </param>
        /// <param name="options">The parsing options.</param>
        /// <returns>The root value containing global keys and section objects.</returns>
        /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
        public static DValue ParseIni(string ini, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(ini, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        /// <summary>Parses UTF-8 INI data into a value backed by an independent document snapshot.</summary>
        /// <param name="ini">The UTF-8 INI data to parse.</param>
        /// <param name="settings">
        ///     The serializer settings, or <see langword="null" /> to use
        ///     <see cref="SerializerSettings.Default" />.
        /// </param>
        /// <param name="options">The parsing options.</param>
        /// <returns>The root value containing global keys and section objects.</returns>
        /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
        public static DValue ParseIni(ReadOnlySpan<byte> ini, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(ini, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        /// <summary>Reads UTF-8 INI data from a stream into a value backed by an independent document snapshot.</summary>
        /// <param name="stream">The stream to read from its current position to the end. The stream remains open.</param>
        /// <param name="settings">
        ///     The serializer settings, or <see langword="null" /> to use
        ///     <see cref="SerializerSettings.Default" />.
        /// </param>
        /// <param name="options">The parsing options.</param>
        /// <returns>The root value containing global keys and section objects.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream" /> is <see langword="null" />.</exception>
        /// <exception cref="DocumentParseException">The input is invalid under the supplied parsing options.</exception>
        public static DValue ParseIni(Stream stream, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        /// <summary>Writes this value as an INI string.</summary>
        /// <param name="options">The output formatting options.</param>
        /// <returns>The encoded INI text, or an empty string if the value is marked as ignored.</returns>
        public string ToIni(IniWriteOptions options = default)
        {
            if (doxValue.GetToken().IsIgnore)
            {
                return string.Empty;
            }

            return IniDocument.EncodeToString(doxValue.AsElement(), options);
        }
    }

    extension(DObject obj)
    {
        /// <summary>Writes this object as an INI string containing global values and section objects.</summary>
        /// <param name="options">The output formatting options.</param>
        /// <returns>The encoded INI text.</returns>
        public string ToIni(IniWriteOptions options = default)
        {
            return IniDocument.EncodeToString(obj, options);
        }
    }

    extension(DMap map)
    {
        /// <summary>Writes this map as an INI string containing global values and section maps.</summary>
        /// <param name="options">The output formatting options.</param>
        /// <returns>The encoded INI text.</returns>
        public string ToIni(IniWriteOptions options = default)
        {
            return IniDocument.EncodeToString(map, options);
        }
    }

    extension(DElement element)
    {
        /// <summary>Writes this element as an INI string.</summary>
        /// <param name="options">The output formatting options.</param>
        /// <returns>The encoded INI text, or an empty string if the element is marked as ignored.</returns>
        public string ToIni(IniWriteOptions options = default)
        {
            if (element.Token.IsIgnore)
            {
                return string.Empty;
            }

            return IniDocument.EncodeToString(element, options);
        }
    }
}