using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public bool DisableShowItemName { get; set; }

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

        public static void ShowHelp()
        {
            var activeConfig = CargoSorterSessionComponent.Instance.Config;
            var sb = new StringBuilder();
            sb.AppendLine("Format:")
                .AppendLine("OptionName: Current Value (default)")
                .AppendLine("Description: A description of the parameter")
                .AppendLine("Use '/configuresort OptionName \"New Value\"' to change options")
                .AppendLine()
                .AppendLine("Type Container Keywords")
                .AppendLine()
                .Append("SpecialContainerKeyword: ").Append(activeConfig.SpecialContainerKeyword).Append(" (").Append(DefaultSpecialContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container to mark it as Special.")
                .AppendLine()
                .Append("LimitedContainerKeyword: ").Append(activeConfig.LimitedContainerKeyword).Append(" (").Append(DefaultLimitedContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container to mark it as Limited.")
                .AppendLine()
                .Append("OreContainerKeyword: ").Append(activeConfig.OreContainerKeyword).Append(" (").Append(DefaultOreContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Ore.")
                .AppendLine()
                .Append("IngotContainerKeyword: ").Append(activeConfig.IngotContainerKeyword).Append(" (").Append(DefaultIngotContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Ingot.")
                .AppendLine()
                .Append("ComponentContainerKeyword: ").Append(activeConfig.ComponentContainerKeyword).Append(" (").Append(DefaultComponentContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Component.")
                .AppendLine()
                .Append("AmmoContainerKeyword: ").Append(activeConfig.AmmoContainerKeyword).Append(" (").Append(DefaultAmmoContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Ammo.")
                .AppendLine()
                .Append("ToolContainerKeyword: ").Append(activeConfig.ToolContainerKeyword).Append(" (").Append(DefaultToolContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Tools.")
                .AppendLine()
                .Append("BottleContainerKeyword: ").Append(activeConfig.BottleContainerKeyword).Append(" (").Append(DefaultBottleContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Bottles.")
                .AppendLine()
                .Append("ConsumablesContainerKeyword: ").Append(activeConfig.ConsumablesContainerKeyword).Append(" (").Append(DefaultConsumablesContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Consumables (cooked food, medicine).")
                .AppendLine()
                .Append("IngredientsContainerKeyword: ").Append(activeConfig.IngredientsContainerKeyword).Append(" (").Append(DefaultIngredientsContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any Ingredients (raw food, seeds).")
                .AppendLine()
                .Append("AnyContainerKeyword: ").Append(activeConfig.AnyContainerKeyword).Append(" (").Append(DefaultAnyContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that should contain any type of item.")
                .AppendLine()
                .Append("QuotaContainerKeyword: ").Append(activeConfig.QuotaContainerKeyword).Append(" (").Append(DefaultQuotaContainerKeyword).AppendLine(")")
                .AppendLine("Keyword to put in the name of a container that allows the contents to count toward fulfilling production quotas.")
                .AppendLine(activeConfig.QuotaContainerKeyword)
                .AppendLine()
                .Append("LockedContainerKeywords: ").Append(string.Join(", ", activeConfig.LockedContainerKeywords)).Append(" (").Append(string.Join(", ", DefaultLockedContainerKeywords)).AppendLine(")")
                .AppendLine("Keywords to put in a block's name to make the sorter ignore it")
                .AppendLine();

            sb.AppendLine()
                .AppendLine("Feature Settings:")
                .AppendLine()
                .Append("GasGeneratorFillPercent: ").Append(activeConfig.GasGeneratorFillPercent * 100).Append(" (").Append(80).AppendLine(")")
                .AppendLine("Fill gas generators to the configured volume percentage with ice (0 to disable).")
                .AppendLine()
                .Append("ExpectedLargeGridReactorFuel: ").Append(activeConfig.ExpectedLargeGridReactorFuel).Append(" (").Append(100).AppendLine(")")
                .AppendLine("Fill large grid reactors with this many units of fuel (0 to disable).")
                .AppendLine()
                .Append("ExpectedLargeGridReactorFuel: ").Append(activeConfig.ExpectedSmallGridReactorFuel).Append(" (").Append(25).AppendLine(")")
                .AppendLine("Fill small grid reactors with this many units of fuel (0 to disable).")
                .AppendLine()
                .Append("AllowSpecialSteal: ").Append(activeConfig.AllowSpecialSteal).Append(" (").Append(true).AppendLine(")")
                .AppendLine("Allow higher priority Special containers to steal from lower priority Special containers.")
                .AppendLine()
                .Append("ShowProgressNotifications: ").Append(activeConfig.ShowProgressNotifications).Append(" (").Append(true).AppendLine(")")
                .AppendLine("Show sorter results in the in-game chat when using /sort.")
                .AppendLine()
                .Append("ShowMissingItems: ").Append(activeConfig.ShowMissingItems).Append(" (").Append(true).AppendLine(")")
                .AppendLine("Show missing item messages in the in-game chat when using /sort.")
                .AppendLine()
                .Append("CopyResultsToClipboard: ").Append(activeConfig.CopyResultsToClipboard).Append(" (").Append(false).AppendLine(")")
                .AppendLine("Allow copying results from popups to the system clipboard.")
                .AppendLine()
                .Append("AutoSortFrequencySeconds: ").Append(activeConfig.AutoSortFrequencySeconds).Append(" (").Append(10).AppendLine(")")
                .AppendLine("WIP: Grid-local auto sorting frequency in seconds, minimum 5.")
                .AppendLine()
                .Append("DisableShowItemName: ").Append(activeConfig.DisableShowItemName).Append(" (").Append(false).AppendLine(")")
                .AppendLine("Disable showing the display name of the item (ex. Steel Plate) alongside the definition ID (Component/SteelPlate).");
            MyAPIGateway.Utilities.ShowMissionScreen("Inventory Sorter", string.Empty, "Settings",
                sb.ToString());
        }


        public static void ChangeSettingsFromChat(string option, string value)
        {
            var newConfig = LoadSettings();

            if (string.Equals(option, "SpecialContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.SpecialContainerKeyword = value;
            }
            else if (string.Equals(option, "LimitedContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.LimitedContainerKeyword = value;
            }
            else if (string.Equals(option, "OreContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.OreContainerKeyword = value;
            }
            else if (string.Equals(option, "IngotContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.IngotContainerKeyword = value;
            }
            else if (string.Equals(option, "ComponentContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.ComponentContainerKeyword = value;
            }
            else if (string.Equals(option, "ToolContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.ToolContainerKeyword = value;
            }
            else if (string.Equals(option, "AmmoContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.AmmoContainerKeyword = value;
            }
            else if (string.Equals(option, "BottleContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.BottleContainerKeyword = value;
            }
            else if (string.Equals(option, "ConsumablesContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.ConsumablesContainerKeyword = value;
            }
            else if (string.Equals(option, "IngredientsContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.IngredientsContainerKeyword = value;
            }
            else if (string.Equals(option, "AnyContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.AnyContainerKeyword = value;
            }
            else if (string.Equals(option, "QuotaContainerKeyword", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.QuotaContainerKeyword = value;
            }
            else if (string.Equals(option, "LockedContainerKeywords", StringComparison.OrdinalIgnoreCase))
            {
                newConfig.LockedContainerKeywords = value.Split(',').Select(v => v.Trim()).ToList();
            }
            else if (string.Equals(option, "GasGeneratorFillPercent", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, out parsedValue) && parsedValue >= 0 && parsedValue <= 100)
                {
                    newConfig.GasGeneratorFillPercent = parsedValue / 100f;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as an integer or valid value. Value must be 0-100 inclusive.");
                    return;
                }
            }
            else if (string.Equals(option, "ExpectedLargeGridReactorFuel", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, out parsedValue))
                {
                    newConfig.ExpectedLargeGridReactorFuel = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as an integer.");
                    return;
                }
            }
            else if (string.Equals(option, "ExpectedSmallGridReactorFuel", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, out parsedValue))
                {
                    newConfig.ExpectedSmallGridReactorFuel = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as an integer.");
                    return;
                }
            }
            else if (string.Equals(option, "AllowSpecialSteal", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    newConfig.AllowSpecialSteal = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as a boolean.");
                    return;
                }
            }
            else if (string.Equals(option, "ShowProgressNotifications", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    newConfig.ShowProgressNotifications = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as a boolean.");
                    return;
                }
            }
            else if (string.Equals(option, "ShowMissingItems", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    newConfig.ShowMissingItems = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as a boolean.");
                    return;
                }
            }
            else if (string.Equals(option, "CopyResultsToClipboard", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    newConfig.CopyResultsToClipboard = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as a boolean.");
                    return;
                }
            }
            else if (string.Equals(option, "AutoSortFrequencySeconds", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, out parsedValue) && parsedValue >= 5)
                {
                    newConfig.AutoSortFrequencySeconds = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"CargoSort: Failed to parse {value} as an integer or valid value. Value must be >= 5.");
                    return;
                }
            }
            else if (string.Equals(option, "DisableShowItemName", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    newConfig.DisableShowItemName = parsedValue;
                }
                else
                {
                    MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to parse {value} as a boolean.");
                    return;
                }
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Unknown config option {value}. Run '/configuresort help' to see the list.");
                return;
            }

            if (newConfig.Validate())
            {
                SaveSettings(newConfig);
                CargoSorterSessionComponent.Instance.LoadSettings();
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Saved and applied new settings!");
            }
            else
            {
                MyAPIGateway.Utilities.ShowMessage("Sorter", $"Failed to validate new settings!");
            }
        }
    }
}