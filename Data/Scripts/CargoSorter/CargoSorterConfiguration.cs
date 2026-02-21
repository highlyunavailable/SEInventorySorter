using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Utils;

namespace CargoSorter
{
    public class CargoSorterConfiguration
    {
        private const string ConfigFileName = "CargoSort.xml";

        public const string defaultSpecialContainerKeyword = "Special";
        public const string defaultLimitedContainerKeyword = "Limited";
        public const string defaultOreContainerKeyword = "Ores";
        public const string defaultIngotContainerKeyword = "Ingots";
        public const string defaultComponentContainerKeyword = "Components";
        public const string defaultToolContainerKeyword = "Tools";
        public const string defaultAmmoContainerKeyword = "Ammo";
        public const string defaultBottleContainerKeyword = "Bottles";
        public const string defaultConsumablesContainerKeyword = "Consumables";
        public const string defaultIngredientsContainerKeyword = "Ingredients";
        public const string defaultAnyContainerKeyword = "Any Item";
        public const string defaultQuotaContainerKeyword = "Cargo";
        public static readonly string[] defaultLockedContainerKeywords = { "Locked", "Hidden", "!manual" };

        public string SpecialContainerKeyword { get; set; }
        public string LimitedContainerKeyword { get; set; }
        public string OreContainerKeyword { get; set; }
        public string IngotContainerKeyword { get; set; }
        public string ComponentContainerKeyword { get; set; }
        public string ToolContainerKeyword { get; set; }
        public string AmmoContainerKeyword { get; set; }
        public string BottleContainerKeyword { get; set; }
        public string ConsumablesContainerKeyword { get; set; }
        public string IngredientsContainerKeyword { get; set; }
        public string AnyContainerKeyword { get; set; }
        public string QuotaContainerKeyword { get; set; }
        public List<string> LockedContainerKeywords { get; set; }
        public float GasGeneratorFillPercent { get; set; }
        public int ExpectedLargeGridReactorFuel { get; set; }
        public int ExpectedSmallGridReactorFuel { get; set; }
        public bool AllowSpecialSteal { get; set; }
        public bool ShowProgressNotifications { get; set; }
        public bool ShowMissingItems { get; set; }
        public bool CopyResultsToClipboard { get; set; }
        public int AutoSortFrequencySeconds { get; set; }

        public static CargoSorterConfiguration LoadSettings()
        {
            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(ConfigFileName, typeof(CargoSorterConfiguration)))
            {
                try
                {
                    CargoSorterConfiguration loadedSettings;
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ConfigFileName, typeof(CargoSorterConfiguration)))
                    {
                        loadedSettings = MyAPIGateway.Utilities.SerializeFromXML<CargoSorterConfiguration>(reader.ReadToEnd());
                    }

                    if (loadedSettings == null)
                    {
                        throw new Exception("CargoSort: Invalid mod configuration, resetting settings");
                    }

                    if (!loadedSettings.Validate())
                    {
                        loadedSettings.Upgrade();
                    }

                    if (!loadedSettings.Validate())
                    {
                        throw new Exception("CargoSort: Invalid mod configuration, resetting settings");
                    }

                    SaveSettings(loadedSettings);
                    return loadedSettings;
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLineAndConsole($"CargoSort: Failed to load mod settings: {e.Message}\n{e.StackTrace}");
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(ConfigFileName, typeof(CargoSorterConfiguration)))
                    {
                        var data = reader.ReadToEnd();
                        if (data.Length > 0)
                        {
                            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ConfigFileName + ".old", typeof(CargoSorterConfiguration)))
                            {
                                writer.Write(data);
                            }
                        }
                    }
                }
            }

            var settings = new CargoSorterConfiguration();
            settings.SetDefaults();
            SaveSettings(settings);
            return settings;
        }

        private static void SaveSettings(CargoSorterConfiguration settings)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(ConfigFileName, typeof(CargoSorterConfiguration)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"CargoSort: Failed to save mod settings: {e.Message}\n{e.StackTrace}");
            }
        }

        private bool Validate()
        {
            return !string.IsNullOrWhiteSpace(SpecialContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(LimitedContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(OreContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(IngotContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(ComponentContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(AmmoContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(ToolContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(BottleContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(ConsumablesContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(IngredientsContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(AnyContainerKeyword) &&
                   !string.IsNullOrWhiteSpace(QuotaContainerKeyword) &&
                   LockedContainerKeywords.All(k => !string.IsNullOrWhiteSpace(k)) &&
                   GasGeneratorFillPercent >= 0f && GasGeneratorFillPercent <= 1f &&
                   ExpectedLargeGridReactorFuel >= 0 &&
                   ExpectedSmallGridReactorFuel >= 0 &&
                   AutoSortFrequencySeconds > 0;
        }

        private void Upgrade()
        {
            SpecialContainerKeyword = CurrentOrDefault(SpecialContainerKeyword, defaultSpecialContainerKeyword);
            LimitedContainerKeyword = CurrentOrDefault(LimitedContainerKeyword, defaultLimitedContainerKeyword);
            OreContainerKeyword = CurrentOrDefault(OreContainerKeyword, defaultOreContainerKeyword);
            IngotContainerKeyword = CurrentOrDefault(IngotContainerKeyword, defaultIngotContainerKeyword);
            AmmoContainerKeyword = CurrentOrDefault(defaultAmmoContainerKeyword, AmmoContainerKeyword);
            ComponentContainerKeyword = CurrentOrDefault(ComponentContainerKeyword, defaultComponentContainerKeyword);
            ToolContainerKeyword = CurrentOrDefault(ToolContainerKeyword, defaultToolContainerKeyword);
            BottleContainerKeyword = CurrentOrDefault(BottleContainerKeyword, defaultBottleContainerKeyword);
            ConsumablesContainerKeyword = CurrentOrDefault(ConsumablesContainerKeyword, defaultConsumablesContainerKeyword);
            IngredientsContainerKeyword = CurrentOrDefault(IngredientsContainerKeyword, defaultIngredientsContainerKeyword);
            AnyContainerKeyword = CurrentOrDefault(AnyContainerKeyword, defaultAnyContainerKeyword);
            QuotaContainerKeyword = CurrentOrDefault(QuotaContainerKeyword, defaultQuotaContainerKeyword);
            AutoSortFrequencySeconds = AutoSortFrequencySeconds > 0 ? AutoSortFrequencySeconds : 10;
        }

        private void SetDefaults()
        {
            SpecialContainerKeyword = defaultSpecialContainerKeyword;
            LimitedContainerKeyword = defaultLimitedContainerKeyword;
            OreContainerKeyword = defaultOreContainerKeyword;
            IngotContainerKeyword = defaultIngotContainerKeyword;
            AmmoContainerKeyword = defaultAmmoContainerKeyword;
            ComponentContainerKeyword = defaultComponentContainerKeyword;
            ToolContainerKeyword = defaultToolContainerKeyword;
            BottleContainerKeyword = defaultBottleContainerKeyword;
            ConsumablesContainerKeyword = defaultConsumablesContainerKeyword;
            IngredientsContainerKeyword = defaultIngredientsContainerKeyword;
            AnyContainerKeyword = defaultAnyContainerKeyword;
            QuotaContainerKeyword = defaultQuotaContainerKeyword;
            LockedContainerKeywords = new List<string>(defaultLockedContainerKeywords);
            GasGeneratorFillPercent = 0.8f;
            ExpectedLargeGridReactorFuel = 100;
            ExpectedSmallGridReactorFuel = 25;
            AllowSpecialSteal = true;
            ShowProgressNotifications = true;
            ShowMissingItems = true;
            AutoSortFrequencySeconds = 10;
        }

        private string CurrentOrDefault(string str, string defaultStr)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return defaultStr;
            }

            return str;
        }
    }
}