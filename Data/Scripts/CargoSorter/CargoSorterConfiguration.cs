using System;
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
        public static readonly string[] defaultLockedContainerKeywords = { "Lock", "Seat", "Control Station", "Hidden", "!manual" };

        public string SpecialContainerKeyword { get; set; }
        public string LimitedContainerKeyword { get; set; }
        public string OreContainerKeyword { get; set; }
        public string IngotContainerKeyword { get; set; }
        public string ComponentContainerKeyword { get; set; }
        public string ToolContainerKeyword { get; set; }
        public string AmmoContainerKeyword { get; set; }
        public string BottleContainerKeyword { get; set; }
        public List<string> LockedContainerKeywords { get; set; }
        public float GasGeneratorFillPercent { get; set; }
        public int ExpectedLargeGridReactorFuel { get; set; }
        public int ExpectedSmallGridReactorFuel { get; set; }
        public bool AllowSpecialSteal { get; set; }
        public bool ShowProgressNotifications { get; set; }
        public bool ShowMissingItems { get; set; }
        public bool CopyResultsToClipboard { get; set; }

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

                    if (loadedSettings == null || !loadedSettings.Validate())
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
                LockedContainerKeywords.All(k => !string.IsNullOrWhiteSpace(k)) &&
                GasGeneratorFillPercent >= 0f && GasGeneratorFillPercent <= 1f &&
                ExpectedLargeGridReactorFuel >= 0 &&
                ExpectedSmallGridReactorFuel >= 0;
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
            LockedContainerKeywords = new List<string>(defaultLockedContainerKeywords);
            GasGeneratorFillPercent = 0.8f;
            ExpectedLargeGridReactorFuel = 100;
            ExpectedSmallGridReactorFuel = 25;
            AllowSpecialSteal = true;
            ShowProgressNotifications = true;
            ShowMissingItems = true;
        }
    }
}