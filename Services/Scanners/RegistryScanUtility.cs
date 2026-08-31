using System;
using System.Collections.Generic;
using System.Linq;
using GameLauncher.Models;
using Microsoft.Win32;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Liest Registry-Schlüssel über alle relevanten Standorte hinweg (HKLM 64/32 Bit
    /// sowie HKCU). Launcher und ihre Spiele tragen sich je nach Bitness und
    /// Installationsart unterschiedlich ein; ein einzelner Standort übersieht daher
    /// regelmäßig installierte Spiele.
    /// </summary>
    internal static class RegistryScanUtility
    {
        private static readonly (RegistryHive Hive, RegistryView View)[] Locations =
        [
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32)
        ];

        /// <summary>
        /// Ruft <paramref name="visit"/> für jeden vorhandenen Schlüssel auf.
        /// Nicht vorhandene oder gesperrte Standorte werden übersprungen.
        /// </summary>
        public static void ForEachKey(string subKeyPath, Action<RegistryKey> visit)
        {
            foreach (var (hive, view) in Locations)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var subKey = baseKey.OpenSubKey(subKeyPath);
                    if (subKey != null)
                    {
                        visit(subKey);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($@"Registry-Zugriff auf {hive}\{subKeyPath} ({view}) fehlgeschlagen", ex);
                }
            }
        }

        /// <summary>
        /// Durchläuft die Unterschlüssel von <paramref name="subKeyPath"/> über alle Standorte
        /// hinweg. Ein Unterschlüsselname wird nur einmal ausgewertet, auch wenn er in
        /// mehreren Registry-Sichten vorhanden ist.
        /// </summary>
        public static void ForEachSubKey(string subKeyPath, Action<string, RegistryKey> visit)
        {
            var visitedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ForEachKey(subKeyPath, parentKey =>
            {
                foreach (string subKeyName in parentKey.GetSubKeyNames())
                {
                    if (!visitedNames.Add(subKeyName))
                    {
                        continue;
                    }

                    try
                    {
                        using var subKey = parentKey.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            visit(subKeyName, subKey);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($@"Registry-Unterschlüssel {subKeyPath}\{subKeyName} konnte nicht gelesen werden", ex);
                    }
                }
            });
        }

        /// <summary>
        /// Liest einen Zeichenkettenwert aus allen Standorten und liefert die eindeutigen Treffer.
        /// </summary>
        public static List<string> ReadStrings(string subKeyPath, string valueName)
        {
            var values = new List<string>();

            ForEachKey(subKeyPath, key =>
            {
                if (key.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            });

            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
