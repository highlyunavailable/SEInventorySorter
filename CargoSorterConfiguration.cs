using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Utils;

namespace InventorySorter
{
    public class CargoSorterConfiguration
    {
        private const string ConfigFileName = "CargoSort.xml";

        private const string DefaultSpecialContainerKeyword = "Special";
        private const string DefaultLimitedContainerKeyword = "Limited";
        private const string DefaultOreContainerKeyword = "Ores";
        private const string DefaultIngotContainerKeyword = "Ingots";
        private const string DefaultComponentContainerKeyword = "Components";
        private const string DefaultToolContainerKeyword = "Tools";
        private const string DefaultAmmoContainerKeyword = "Ammo";
        private const string DefaultBottleContainerKeyword = "Bottles";
        private const string DefaultConsumablesContainerKeyword = "Consumables";
        private const string DefaultIngredientsContainerKeyword = "Ingredients";
        private const string DefaultAnyContainerKeyword = "Any Item";
        private const string DefaultQuotaContainerKeyword = "Cargo";
        private static readonly string[] DefaultLockedContainerKeywords = { "Locked", "Hidden", "!manual" };

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
            SpecialContainerKeyword = CurrentOrDefault(SpecialContainerKeyword, DefaultSpecialContainerKeyword);
            LimitedContainerKeyword = CurrentOrDefault(LimitedContainerKeyword, DefaultLimitedContainerKeyword);
            OreContainerKeyword = CurrentOrDefault(OreContainerKeyword, DefaultOreContainerKeyword);
            IngotContainerKeyword = CurrentOrDefault(IngotContainerKeyword, DefaultIngotContainerKeyword);
            AmmoContainerKeyword = CurrentOrDefault(DefaultAmmoContainerKeyword, AmmoContainerKeyword);
            ComponentContainerKeyword = CurrentOrDefault(ComponentContainerKeyword, DefaultComponentContainerKeyword);
            ToolContainerKeyword = CurrentOrDefault(ToolContainerKeyword, DefaultToolContainerKeyword);
            BottleContainerKeyword = CurrentOrDefault(BottleContainerKeyword, DefaultBottleContainerKeyword);
            ConsumablesContainerKeyword = CurrentOrDefault(ConsumablesContainerKeyword, DefaultConsumablesContainerKeyword);
            IngredientsContainerKeyword = CurrentOrDefault(IngredientsContainerKeyword, DefaultIngredientsContainerKeyword);
            AnyContainerKeyword = CurrentOrDefault(AnyContainerKeyword, DefaultAnyContainerKeyword);
            QuotaContainerKeyword = CurrentOrDefault(QuotaContainerKeyword, DefaultQuotaContainerKeyword);
            AutoSortFrequencySeconds = AutoSortFrequencySeconds > 0 ? AutoSortFrequencySeconds : 10;
        }

        private void SetDefaults()
        {
            SpecialContainerKeyword = DefaultSpecialContainerKeyword;
            LimitedContainerKeyword = DefaultLimitedContainerKeyword;
            OreContainerKeyword = DefaultOreContainerKeyword;
            IngotContainerKeyword = DefaultIngotContainerKeyword;
            AmmoContainerKeyword = DefaultAmmoContainerKeyword;
            ComponentContainerKeyword = DefaultComponentContainerKeyword;
            ToolContainerKeyword = DefaultToolContainerKeyword;
            BottleContainerKeyword = DefaultBottleContainerKeyword;
            ConsumablesContainerKeyword = DefaultConsumablesContainerKeyword;
            IngredientsContainerKeyword = DefaultIngredientsContainerKeyword;
            AnyContainerKeyword = DefaultAnyContainerKeyword;
            QuotaContainerKeyword = DefaultQuotaContainerKeyword;
            LockedContainerKeywords = new List<string>(DefaultLockedContainerKeywords);
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