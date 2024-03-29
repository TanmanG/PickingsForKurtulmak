﻿
using Pickings_For_Kurtulmak;
using ScottPlot;
using Syncfusion.Windows.Forms.Tools;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Syncfusion.WinForms.Controls;
using static DamageCalculatorGUI.DamageCalculator;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using Syncfusion.Windows.Forms;
using System.Drawing.Text;
using ScottPlot.Plottable;
using ScottPlot.Renderable;
using ScottPlot.Drawing.Colormaps;
using System.Text.Json.Serialization;
using static System.Windows.Forms.AxHost;

namespace DamageCalculatorGUI
{
    public partial class CalculatorWindow : SfForm
    {
        // BASE LOAD FUNCTIONS
        //
        public CalculatorWindow()
        {
            InitializeComponent();
        }
        private void CalculatorWindowLoad(object sender, EventArgs e)
        {
            // Load Color Palettes
            if (!ReadColorPalettes())
            { // Store the a default color palette
                if (!colorPalettes.ContainsKey("pf2e"))
                {
                    colorPalettes.Add(key: "pf2e", value: PFKColorPalette.GetDefaultPalette("pf2e"));
                    WriteColorPalette(colorPalettes["pf2e"], "pf2e");
                }
                if (!colorPalettes.ContainsKey("light"))
                {
                    colorPalettes.TryAdd(key: "light", value: PFKColorPalette.GetDefaultPalette("light"));
                    WriteColorPalette(colorPalettes["light"], "light");
                }
                if (!colorPalettes.ContainsKey("dark"))
                {
                    colorPalettes.TryAdd(key: "dark", value: PFKColorPalette.GetDefaultPalette("dark"));
                    WriteColorPalette(colorPalettes["dark"], "dark");
                }
            }

            // Load Settings
            if (!ReadSettings("settings.json"))
            { // Generate default settings if none found/couldn't read properly
                PFKSettings = new()
                {
                    CurrentColorPallete = colorPalettes.ElementAt(0).Value
                };
                WriteSettings();
            }
            else
            { // Double-check loaded settings for erroneous data
                if (PFKSettings.CurrentColorPallete is null)
                { // Catch bad palette data and load default (top of the list)
                    LoadColorPallete(colorPalettes.OrderBy(paletteKVP => paletteKVP.Key)
                                                  .First().Value);
                }
                // Else: Nothing to do, as ReadSettings() succeeding is a win.
            }

            // Set graph appearances to be something normal
            InitializeGraphStartAppearance();

            // Generate and store label hashes for the help mode
            StoreLabelHelpHashes();

            // Generate and store entry hashes for batch entry mode & entry locking
            StoreEntryBoxParentBatchHashes();

            // Generate and store Setting hashes and vice versa
            StoreSettingToControl();
            StoreControlToSetting();
            StoreSettingToCheckbox();
            StoreTickboxToTextboxes();

            // Default Settings
            currEncounterSettings.ResetSettings();
            ReloadVisuals(currEncounterSettings);

            // Lock Window Dimensions
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Enable Carat Hiding
            AddCaretHidingEvents();


            // Populate the tracked-controls menu
            allWindowControls = new();
            HelperFunctions.GetAllControls(this, allWindowControls);
            allWindowControls.Add(CalculatorMiscStatisticsCalculateStatsProgressBars);
            allWindowControls.AddRange(MainTabControl.TabPages.Cast<TabPageAdv>());

            // Reset the UI
            currEncounterSettings.ResetSettings();
            ApplyCurrentColorPallete();
            ReloadVisuals(currEncounterSettings);

            // Update the graphs to fix some scuffed rendering problems
            CalculatorBatchComputeScottPlot.Refresh();
            CalculatorDamageDistributionScottPlot.Refresh();

            // Load to the Settings UI
            UpdateSettingsThemesListbox();
        }
        private void CalculatorWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteSettings();
        }

        // Current UI Settings
        public List<Control> allWindowControls;

        public PFKSettings PFKSettings = new();

        // Damage Stats Struct
        public DamageStats damageStats = new();
        public BatchResults damageStats_BATCH = new();
        public List<int> batch_view_selected_steps = new() { 0 };

        // Already Running a Sim
        private static bool computing_damage = false;

        // Batch Compute Variable
        private static bool hovering_entrybox = false;
        private static bool batch_mode_enabled = false;
        private static Control batch_mode_selected = null;
        private readonly Dictionary<int, GroupBox> entrybox_groupBox_hashes = new();
        private readonly Dictionary<int, EncounterSetting> control_hash_to_setting = new();
        private readonly Dictionary<EncounterSetting, Dictionary<int, int>> batched_variables_last_value = new();
        private readonly Dictionary<EncounterSetting, Dictionary<int, BatchModeSettings>> batched_variables = new();
        private readonly Dictionary<EncounterSetting, Control> setting_to_control = new();
        private readonly Dictionary<int, List<TextBox>> tickbox_to_textboxes = new();
        private static readonly Dictionary<EncounterSetting, CheckBox> setting_to_checkbox = new();
        private static readonly Dictionary<EncounterSetting, Tuple<DieField, bool, bool>> setting_to_info = new() // Conversions to get die type, i.e. EncounterSetting to DieField, Bleed, and Crit respectively
        {
            { EncounterSetting.damage_dice_count, new (DieField.count, false, false) },
            { EncounterSetting.damage_dice_size, new (DieField.size, false, false) },
            { EncounterSetting.damage_dice_bonus, new (DieField.bonus, false, false) },

            { EncounterSetting.damage_dice_count_critical, new (DieField.count, false, true) },
            { EncounterSetting.damage_dice_size_critical, new (DieField.size, false, true) },
            { EncounterSetting.damage_dice_bonus_critical, new (DieField.bonus, false, true) },

            { EncounterSetting.damage_dice_DOT_count, new (DieField.count, true, false) },
            { EncounterSetting.damage_dice_DOT_size, new (DieField.size, true, false) },
            { EncounterSetting.damage_dice_DOT_bonus, new (DieField.bonus, true, false) },

            { EncounterSetting.damage_dice_DOT_count_critical, new (DieField.count, true, true) },
            { EncounterSetting.damage_dice_DOT_size_critical, new (DieField.size, true, true) },
            { EncounterSetting.damage_dice_DOT_bonus_critical, new (DieField.bonus, true, true) },
        };
        private static readonly Dictionary<EncounterSetting, string> setting_to_string = new()
        {
            { EncounterSetting.number_of_encounters, "Encounters" },
            { EncounterSetting.rounds_per_encounter, "Rounds" },
            { EncounterSetting.actions_per_round_any, "Actions" },
            { EncounterSetting.actions_per_round_strike, "Extra Actions (Strike)" },
            { EncounterSetting.actions_per_round_draw, "Extra Actions (Draw)" },
            { EncounterSetting.actions_per_round_stride, "Extra Actions (Stride)" },
            { EncounterSetting.actions_per_round_reload, "Extra Actions (Reload)" },
            { EncounterSetting.actions_per_round_long_reload, "Extra Actions (Long Reload)" },
            { EncounterSetting.magazine_size, "Magazine Size" },
            { EncounterSetting.reload, "Reload" },
            { EncounterSetting.long_reload, "Long Reload" },
            { EncounterSetting.draw, "Draw" },
            { EncounterSetting.damage_dice_count, "Die Count" },
            { EncounterSetting.damage_dice_size, "Die Size" },
            { EncounterSetting.damage_dice_bonus, "Die Bonus" },
            { EncounterSetting.damage_dice_count_critical, "Die Count (🗲)" },
            { EncounterSetting.damage_dice_size_critical, "Die Size (🗲)" },
            { EncounterSetting.damage_dice_bonus_critical, "Die Bonus (🗲)" },
            { EncounterSetting.bonus_to_hit, "Bonus To Hit" },
            { EncounterSetting.AC, "AC" },
            { EncounterSetting.crit_threshhold, "Critical Hit Minimum" },
            { EncounterSetting.MAP_modifier, "MAP Modifier" },
            { EncounterSetting.engagement_range, "Engagement Start Range" },
            { EncounterSetting.move_speed, "Movement Speed" },
            { EncounterSetting.range, "Range Increment" },
            { EncounterSetting.volley, "Volley Increment" },
            { EncounterSetting.damage_dice_DOT_count, "Die Count (🗡)" },
            { EncounterSetting.damage_dice_DOT_size, "Die Size (🗡)" },
            { EncounterSetting.damage_dice_DOT_bonus, "Die Bonus (🗡)" },
            { EncounterSetting.damage_dice_DOT_count_critical, "Die Count (🗡🗲)" },
            { EncounterSetting.damage_dice_DOT_size_critical, "Die Size (🗡🗲)" },
            { EncounterSetting.damage_dice_DOT_bonus_critical, "Die Bonus (🗡🗲)" }
        };
        private static double computationStepsTotal = 0;
        private static double computationStepsCurrent = 0;

        public enum EncounterSetting
        {
            number_of_encounters,
            rounds_per_encounter,
            actions_per_round_any,
            actions_per_round_strike,
            actions_per_round_draw,
            actions_per_round_stride,
            actions_per_round_reload,
            actions_per_round_long_reload,
            magazine_size,
            reload,
            long_reload,
            draw,
            bonus_to_hit,
            AC,
            crit_threshhold,
            MAP_modifier,
            engagement_range,
            move_speed,
            seek_favorable_range,
            range,
            volley,
            damage_dice_count,
            damage_dice_size,
            damage_dice_bonus,
            damage_dice_count_critical,
            damage_dice_size_critical,
            damage_dice_bonus_critical,
            damage_dice_DOT_count,
            damage_dice_DOT_size,
            damage_dice_DOT_bonus,
            damage_dice_DOT_count_critical,
            damage_dice_DOT_size_critical,
            damage_dice_DOT_bonus_critical,
        }

        // Batch Setting, represents the batch configuration for the given value.
        public struct BatchModeSettings
        {
            public EncounterSetting encounterSetting;
            public int layer; // What layer to scale on. Equal means both iterate simultaneously, below means will iterate once every other layer finishes.
            public int start; // Initial value
            public int end; // End value 
            public List<int> steps; // Pattern of steps to be taken in total. Repeating list.
            public int number_of_steps; // Number of times to be stepped in total.
            public int step_direction; // Overall direction of steps
            public Dictionary<int, int> value_at_step; // Number of steps taken to reach the given value
            public bool initialized;

            public int index;
            public DieField die_field;
            public bool bleed;
            public bool critical;

            public BatchModeSettings(int layer, int start, int end, List<int> steps, bool bleed = false, int index = -1, DieField die_field = DieField.count, bool critical = false, EncounterSetting encounterSetting = default)
            {
                this.encounterSetting = encounterSetting;
                this.layer = layer;
                this.start = start;
                this.end = end;
                this.steps = steps;
                this.index = index;
                this.bleed = bleed;
                this.die_field = die_field;
                this.critical = critical;

                value_at_step = new();
                initialized = false;
                number_of_steps = 0;

                step_direction = Math.Sign(steps.Sum()); // Compute overall direction of the steps list


                // Check it's safe to compute
                CheckBatchLogicSafety(this);

                // Compute the base step direction.
                GetValueAtStep();
            }

            public int GetValueAtStep(int target_step = -1)
            {
                if (value_at_step.TryGetValue(target_step, out int value))
                    return value;
                // To-do: Maybe optimize this to get the key closest to the target step as the initial values?

                // Store the base value
                int valueAtTargetStep = start;

                // Store the current step
                int currentStep = 0;

                if (end > start)
                {
                    // Step until the target_step is reached
                    while ((target_step != -1 && currentStep != target_step)  // The target step is reached if provided
                            || (target_step == -1 && valueAtTargetStep < end))// End has been reached if provided
                    { // Iterate steps until the target step is reached

                        // Cache the current step value
                        value_at_step.TryAdd(currentStep, valueAtTargetStep);

                        // Increase the current value by the step, clamped to not overflow the index 
                        valueAtTargetStep = Math.Clamp(value: valueAtTargetStep + steps[HelperFunctions.Mod(currentStep, steps.Count)],
                                                        min: int.MinValue, max: end);

                        // Iterate the current step
                        currentStep++;
                    }
                }
                else
                {
                    // Step until the target_step is reached
                    while ((target_step != -1 && currentStep != target_step)  // The target step is reached if provided
                            || (target_step == -1 && valueAtTargetStep > end))// End has been reached if provided
                    { // Iterate steps until the target step is reached

                        // Cache the current step value
                        value_at_step.TryAdd(currentStep, valueAtTargetStep);

                        // Increase the current value by the step, clamped to not overflow the index 
                        valueAtTargetStep = Math.Clamp(value: valueAtTargetStep + steps[HelperFunctions.Mod(currentStep, steps.Count)],
                                                        min: end, max: int.MaxValue);

                        // Iterate the current step
                        currentStep++;
                    }
                }


                if (target_step == -1)
                    number_of_steps = currentStep;

                return valueAtTargetStep;
            }
        }
        // Help Mode Variable
        public static bool help_mode_enabled;
        Dictionary<int, string> label_hashes_help = new();
        EncounterSettings currEncounterSettings = new();

        public Dictionary<int, List<BatchModeSettings>> layerViewControlEncounterSettings = new();

        public bool isBatchGraphSmall = false;
        private async void CalculateDamageStatsButton_MouseClick(object sender, MouseEventArgs e)
        {

            if (!HelperFunctions.IsControlDown())
            {
                if (computing_damage) // Prevent double-computing
                    return;

                // Update Data
                try
                { // Check for exception
                    CheckComputeSafety();

                    // Safe!
                    currEncounterSettings = GetDamageSimulationVariables(currEncounterSettings);

                    // Compute Damage Stats
                    if (batch_mode_enabled)
                    {
                        var binned_compute_layers = ComputeBinnedLayersForBatch(batched_variables);
                        int[] maxSteps = ComputeMaxStepsArrayForBatch(binned_compute_layers);

                        // Track Progress Bar
                        Progress<int> progress = new();
                        progress.ProgressChanged += SetProgressBar;

                        // Compute all variables
                        damageStats_BATCH = await ComputeAverageDamage_BATCH(encounter_settings: currEncounterSettings,
                                                                            binned_compute_layers: binned_compute_layers,
                                                                            maxSteps: maxSteps,
                                                                            batched_variables: batched_variables,
                                                                            progress: progress);

                        int numberOfLayers = batched_variables
                                        .SelectMany(encounterSetting => encounterSetting.Value)
                                        .GroupBy(blindEncounterSetting => blindEncounterSetting.Value.layer)
                                        .Distinct()
                                        .Count();

                        // Reset the viewed index list
                        batch_view_selected_steps.Clear();
                        batch_view_selected_steps.AddRange(Enumerable.Repeat(element: 0, numberOfLayers));

                        // Adjust the graph size
                        isBatchGraphSmall = numberOfLayers > 1;
                        SetBatchGraphSizeSmall(isBatchGraphSmall);
                        SetBatchLayerViewControlVisibility(isBatchGraphSmall);

                        // Update the graph
                        UpdateBatchGraph(damageStats_BATCH);
                    }
                    else
                    {
                        damageStats = await ComputeAverageDamage_SINGLE(number_of_encounters: currEncounterSettings.number_of_encounters,
                                                        rounds_per_encounter: currEncounterSettings.rounds_per_encounter,
                                                        actions_per_round: currEncounterSettings.actions_per_round,
                                                        reload_size: currEncounterSettings.magazine_size,
                                                        reload: currEncounterSettings.reload,
                                                        long_reload: currEncounterSettings.long_reload,
                                                        draw: currEncounterSettings.draw,
                                                        damage_dice: currEncounterSettings.damage_dice,
                                                        bonus_to_hit: currEncounterSettings.bonus_to_hit,
                                                        AC: currEncounterSettings.AC,
                                                        crit_threshhold: currEncounterSettings.crit_threshhold,
                                                        MAP_modifier: currEncounterSettings.MAP_modifier,
                                                        engagement_range: currEncounterSettings.engagement_range,
                                                        move_speed: currEncounterSettings.move_speed,
                                                        seek_favorable_range: currEncounterSettings.seek_favorable_range,
                                                        range: currEncounterSettings.range,
                                                        volley: currEncounterSettings.volley,
                                                        damage_dice_DOT: currEncounterSettings.damage_dice_DOT);

                        // Compute 25th, 50th, and 75th Percentiles
                        Tuple<int, int, int> percentiles = HelperFunctions.ComputePercentiles(damageStats.damage_bins);
                        /// Update the GUI with the percentiles
                        UpdateStatisticsGUI(percentiles);

                        // Update Graph
                        UpdateBaseGraph(percentiles);
                    }

                    // Visually Update Graphs
                    CalculatorBatchComputeScottPlot.Refresh();
                    CalculatorDamageDistributionScottPlot.Refresh();
                }
                catch (Exception ex)
                { // Exception caught!
                    computing_damage = false;
                    PushErrorCalculatorVariableErrorMessages(ex);
                    return;
                }
            }
        }

        private void AddCaretHidingEvents()
        {
            CalculatorEncounterStatisticsMeanTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorEncounterStatisticsMedianTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorEncounterStatisticsUpperQuartileTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorEncounterStatisticsLowerQuartileBoxTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorMiscStatisticsRoundDamageMeanTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorMiscStatisticsAttackDamageMeanTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
            CalculatorMiscStatisticsAccuracyMeanTextBox.GotFocus += new EventHandler(TextBox_GotFocusDisableCarat);
        }

        // POPULATE DICTIONARIES/LISTS
        //
        private static void CollectionAddFromControlHash<T>(Dictionary<int, T> dictionary, Control control, T element)
        {
            dictionary.Add(control.GetHashCode(), element);
        }
        private void StoreLabelHelpHashes()
        {
            // Attack
            CollectionAddFromControlHash(label_hashes_help, CalculatorAttackBonusToHitLabel, "The total bonus to-hit. Around ~12 for a 4th level Gunslinger.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAttackCriticalHitMinimumLabel, "Minimum die roll to-hit in order to critically strike. Only way to crit other than getting 10 over the AC.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAttackACLabel, "Armor Class of the target to be tested against. Typically ~21 for a 4th level enemy.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAttackMAPModifierLabel, "The Multiple-Attack-Penalty. Typically 5 unless you are using an Agile weapon or have Agile Grace, in which this is equal to 4 and 3 respectively.");
            // Ammunition
            CollectionAddFromControlHash(label_hashes_help, CalculatorAmmunitionReloadLabel, "The number of Interact actions required to re-chamber a weapon after firing. I.e. a 3 action round would compose of Strike, Reload 1, Strike or Strike, Reload 2.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAmmunitionMagazineSizeLabel, "The number of Strike actions that can be done before requiring a Long Reload.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAmmunitionLongReloadLabel, "The number of Interact actions required to replenish the weapon's Magazine Size to max. Includes one complementary Reload. I.e. two 3 action rounds would compose of Strike, Reload 1, Strike then Long Reload 2, Strike");
            CollectionAddFromControlHash(label_hashes_help, CalculatorAmmunitionDrawLengthLabel, "The number of Interact actions required to draw the weapon. I.e. a 3 action round would compose of Draw 1, Strike, Reload 1.");
            // Damage
            CollectionAddFromControlHash(label_hashes_help, CalculatorDamageDieSizeLabel, "The base damage of this weapon on hit. Represented by [X]d[Y]+[Z], such that a Y sided die will be rolled X times with a flat Z added (or subtracted if negative).");
            CollectionAddFromControlHash(label_hashes_help, CalculatorDamageCriticalDieLabel, "The upgraded die quantity, size, and/or flat bonus from Critical Strikes. Represented in vanilla Pathfinder 2E as Brutal Critical.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorDamageBleedDieLabel, "The amount of Persistent Damage dealt on-hit by this weapon. Each applied instance of this is checked against a flat DC15 save every round to simulate a save.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorDamageCriticalBleedDieLabel, "The upgraded die quantity, size, and/or flat bonus to Persistent damage when Critically Striking.");
            // Reach
            CollectionAddFromControlHash(label_hashes_help, CalculatorReachRangeIncrementLabel, "The Range Increment of the weapon. Attacks attacks against targets beyond the range increment take a stacking -2 penalty to-hit for each increment away. I.e. Range Increment of 30ft will take a -4 at 70ft.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorReachVolleyIncrementLabel, "The Volley Increment of the weapon. Attacks made within this distance suffer a flat -2 to-hit.");
            // Encounter
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterNumberOfEncountersLabel, "How many combat encounters to simulate. Default is around 100,000.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterRoundsPerEncounterLabel, "How many rounds to simulate per encounter. Higher simulates more drawn out encounters, shorter simulates more brief encounters.");
            // Actions
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionActionsPerRoundLabel, "How many actions granted each round. Typically 3 unless affected by an effect such as Haste.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsLabel, "These actions can only be used for the specified action and are granted each round.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsDrawLabel, "How many Draw-only actions to be granted each round.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsReloadLabel, "How many Reload-only actions to be granted each round.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsStrideLabel, "How many Stride-only actions to be granted each round.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsStrikeLabel, "How many Strike-only actions to be granted each round.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorActionExtraLimitedActionsLongReloadLabel, "How many Long Reload-only actions to be granted each round.");
            // Distance
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterEngagementRangeLabel, "Distance to begin the encounter at. Defaults to 30 if unchecked.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorReachMovementSpeedLabel, "Amount of distance covered with one Stride action. The simulated player will Stride into the ideal (within range and out of volley) firing position before firing.");
            // Encounter Damage Statistics
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterStatisticsMeanLabel, "The 'true average' damage across all encounters. I.e. The sum of all encounters divded by the number of encounters.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterStatisticsUpperQuartileLabel, "The 75th percentile damage across all encounters. I.e. The average high-end/lucky damage performance of the weapon.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterStatisticsMedianLabel, "The 50th percentile damage across all encounters. I.e. The average center-most value and generally typical performance of the weapon.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorEncounterStatisticsLowerQuartileLabel, "The 25th percentile damage across all encounters. I.e. The aveerage lower-end/unlucky damage performance of the weapon.");
            // Misc Statistics
            CollectionAddFromControlHash(label_hashes_help, CalculatorMiscStatisticsRoundDamageMeanLabel, "The 'true average' round damage for each round of combat. Best captures rapid-fire weapons.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorMiscStatisticsAttackDamageMeanLabel, "The 'true average' attack damage for each attack landed. Best captures the typical per-hit performance of weapon.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorMiscStatisticsAccuracyMeanLabel, "The average accuracy of the weapon. I.e. What percentage of attacks hit.");
            // Buttons
            CollectionAddFromControlHash(label_hashes_help, CalculatorCalculateDamageStatsButton, "CTRL + Left-Click to copy a comma separated list of the data below.");
            // Batch Compute
            CollectionAddFromControlHash(label_hashes_help, CalculatorBatchComputeButton, "Enable to swap cursor to batch compute mode, allowing you to compute variables at different values rather than just one. Select a entry box with the crosshair to open the batch compute settings popup.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorBatchComputePopupStartLabel, "The value to start scaling this variable from for the given variable. This will be the first value of the selected variable, which will be increased by the value of \"step\" each computation iteration. I.e. Start 0, End 3, Step 1 will simulate the values: 0, 1, 2, 3.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorBatchComputePopupEndValueLabel, "The value to end scaling this variable up to for the given variable. The variable will be iterated by Step until it reaches or exceeds this value, in the latter case will just round down to this. I.e. Start 2, End 7, Step 2 will simulate the values: 2, 4, 6, 7.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorBatchComputePopupStepPatternLabel, "The value to increase the variable by each iteration. This will start at Start, then end rounding down to End. Can be negative. I.e. Start 5, End 0, Step -2 will simulate the values: 5, 3, 1, 0.");
            CollectionAddFromControlHash(label_hashes_help, CalculatorBatchComputePopupLayerLabel, "Lower values will be iterated after all layers above it have fully iterated from start to finish. Warning: This is extremely expensive to use, as computation increases exponentially with each new layer!");
        }
        private void StoreEntryBoxParentBatchHashes()
        {
            // Attack
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAttackBonusToHitTextBox, CalculatorAttackGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAttackCriticalHitMinimumTextBox, CalculatorAttackGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAttackACTextBox, CalculatorAttackGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAttackMAPModifierTextBox, CalculatorAttackGroupBox);

            // Ammunition
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAmmunitionReloadTextBox, CalculatorAmmunitionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAmmunitionMagazineSizeCheckBox, CalculatorAmmunitionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAmmunitionMagazineSizeTextBox, CalculatorAmmunitionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAmmunitionLongReloadTextBox, CalculatorAmmunitionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorAmmunitionDrawLengthTextBox, CalculatorAmmunitionGroupBox);

            // Damage
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageSaveButton, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageAddNewButton, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageDeleteButton, CalculatorDamageGroupBox);


            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageDieCountTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageDieSizeTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageDieBonusTextBox, CalculatorDamageGroupBox);

            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalDieCheckBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalDieCountTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalDieSizeTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalDieBonusTextBox, CalculatorDamageGroupBox);

            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageBleedDieCheckBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageBleedDieCountTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageBleedDieSizeTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageBleedDieBonusTextBox, CalculatorDamageGroupBox);

            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalBleedDieCheckBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalBleedDieCountTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalBleedDieSizeTextBox, CalculatorDamageGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorDamageCriticalBleedDieBonusTextBox, CalculatorDamageGroupBox);

            // Reach
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachRangeIncrementCheckBox, CalculatorReachGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachRangeIncrementTextBox, CalculatorReachGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachVolleyIncrementCheckBox, CalculatorReachGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachVolleyIncrementTextBox, CalculatorReachGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachMovementSpeedCheckBox, CalculatorReachGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorReachMovementSpeedTextBox, CalculatorReachGroupBox);

            // Encounter
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorEncounterNumberOfEncountersTextBox, CalculatorEncounterGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorEncounterRoundsPerEncounterTextBox, CalculatorEncounterGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorEncounterEngagementRangeCheckBox, CalculatorEncounterGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorEncounterEngagementRangeTextBox, CalculatorEncounterGroupBox);

            // Action
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionActionsPerRoundTextBox, CalculatorActionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionExtraLimitedActionsStrikeNumericUpDown, CalculatorActionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionExtraLimitedActionsDrawNumericUpDown, CalculatorActionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionExtraLimitedActionsStrideNumericUpDown, CalculatorActionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionExtraLimitedActionsReloadNumericUpDown, CalculatorActionGroupBox);
            CollectionAddFromControlHash(entrybox_groupBox_hashes, CalculatorActionExtraLimitedActionsLongReloadNumericUpDown, CalculatorActionGroupBox);
        }
        private void StoreControlToSetting()
        {
            // Attack
            control_hash_to_setting.Add(CalculatorAttackBonusToHitTextBox.GetHashCode(), EncounterSetting.bonus_to_hit);
            control_hash_to_setting.Add(CalculatorAttackCriticalHitMinimumTextBox.GetHashCode(), EncounterSetting.crit_threshhold);
            control_hash_to_setting.Add(CalculatorAttackACTextBox.GetHashCode(), EncounterSetting.AC);
            control_hash_to_setting.Add(CalculatorAttackMAPModifierTextBox.GetHashCode(), EncounterSetting.MAP_modifier);

            // Ammunition
            control_hash_to_setting.Add(CalculatorAmmunitionReloadTextBox.GetHashCode(), EncounterSetting.reload);
            control_hash_to_setting.Add(CalculatorAmmunitionMagazineSizeTextBox.GetHashCode(), EncounterSetting.magazine_size);
            control_hash_to_setting.Add(CalculatorAmmunitionLongReloadTextBox.GetHashCode(), EncounterSetting.long_reload);
            control_hash_to_setting.Add(CalculatorAmmunitionDrawLengthTextBox.GetHashCode(), EncounterSetting.draw);

            // Damage Die
            // Base
            control_hash_to_setting.Add(CalculatorDamageDieCountTextBox.GetHashCode(), EncounterSetting.damage_dice_count);
            control_hash_to_setting.Add(CalculatorDamageDieSizeTextBox.GetHashCode(), EncounterSetting.damage_dice_size);
            control_hash_to_setting.Add(CalculatorDamageDieBonusTextBox.GetHashCode(), EncounterSetting.damage_dice_bonus);
            // Critical
            control_hash_to_setting.Add(CalculatorDamageCriticalDieCountTextBox.GetHashCode(), EncounterSetting.damage_dice_count_critical);
            control_hash_to_setting.Add(CalculatorDamageCriticalDieSizeTextBox.GetHashCode(), EncounterSetting.damage_dice_size_critical);
            control_hash_to_setting.Add(CalculatorDamageCriticalDieBonusTextBox.GetHashCode(), EncounterSetting.damage_dice_bonus_critical);
            // Bleed
            control_hash_to_setting.Add(CalculatorDamageBleedDieCountTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_count);
            control_hash_to_setting.Add(CalculatorDamageBleedDieSizeTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_size);
            control_hash_to_setting.Add(CalculatorDamageBleedDieBonusTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_bonus);
            // Critical Bleed
            control_hash_to_setting.Add(CalculatorDamageCriticalBleedDieCountTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_count_critical);
            control_hash_to_setting.Add(CalculatorDamageCriticalBleedDieSizeTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_size_critical);
            control_hash_to_setting.Add(CalculatorDamageCriticalBleedDieBonusTextBox.GetHashCode(), EncounterSetting.damage_dice_DOT_bonus_critical);

            // Reach
            control_hash_to_setting.Add(CalculatorReachRangeIncrementTextBox.GetHashCode(), EncounterSetting.range);
            control_hash_to_setting.Add(CalculatorReachVolleyIncrementTextBox.GetHashCode(), EncounterSetting.volley);
            control_hash_to_setting.Add(CalculatorReachMovementSpeedTextBox.GetHashCode(), EncounterSetting.move_speed);

            // Reach
            control_hash_to_setting.Add(CalculatorEncounterNumberOfEncountersTextBox.GetHashCode(), EncounterSetting.number_of_encounters);
            control_hash_to_setting.Add(CalculatorEncounterRoundsPerEncounterTextBox.GetHashCode(), EncounterSetting.rounds_per_encounter);
            control_hash_to_setting.Add(CalculatorEncounterEngagementRangeTextBox.GetHashCode(), EncounterSetting.engagement_range);

            // Action
            control_hash_to_setting.Add(CalculatorActionActionsPerRoundTextBox.GetHashCode(), EncounterSetting.actions_per_round_any);
            control_hash_to_setting.Add(CalculatorActionExtraLimitedActionsStrikeNumericUpDown.GetHashCode(), EncounterSetting.actions_per_round_strike);
            control_hash_to_setting.Add(CalculatorActionExtraLimitedActionsDrawNumericUpDown.GetHashCode(), EncounterSetting.actions_per_round_draw);
            control_hash_to_setting.Add(CalculatorActionExtraLimitedActionsStrideNumericUpDown.GetHashCode(), EncounterSetting.actions_per_round_stride);
            control_hash_to_setting.Add(CalculatorActionExtraLimitedActionsReloadNumericUpDown.GetHashCode(), EncounterSetting.actions_per_round_reload);
            control_hash_to_setting.Add(CalculatorActionExtraLimitedActionsLongReloadNumericUpDown.GetHashCode(), EncounterSetting.actions_per_round_long_reload);
        }
        private void StoreSettingToControl()
        {
            // Attack
            setting_to_control.Add(EncounterSetting.bonus_to_hit, CalculatorAttackBonusToHitTextBox);
            setting_to_control.Add(EncounterSetting.crit_threshhold, CalculatorAttackCriticalHitMinimumTextBox);
            setting_to_control.Add(EncounterSetting.AC, CalculatorAttackACTextBox);
            setting_to_control.Add(EncounterSetting.MAP_modifier, CalculatorAttackMAPModifierTextBox);

            // Ammunition
            setting_to_control.Add(EncounterSetting.reload, CalculatorAmmunitionReloadTextBox);
            setting_to_control.Add(EncounterSetting.magazine_size, CalculatorAmmunitionMagazineSizeTextBox);
            setting_to_control.Add(EncounterSetting.long_reload, CalculatorAmmunitionLongReloadTextBox);
            setting_to_control.Add(EncounterSetting.draw, CalculatorAmmunitionDrawLengthTextBox);

            // Damage Die
            // Base
            setting_to_control.Add(EncounterSetting.damage_dice_count, CalculatorDamageDieCountTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_size, CalculatorDamageDieSizeTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_bonus, CalculatorDamageDieBonusTextBox);
            // Critical
            setting_to_control.Add(EncounterSetting.damage_dice_count_critical, CalculatorDamageCriticalDieCountTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_size_critical, CalculatorDamageCriticalDieSizeTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_bonus_critical, CalculatorDamageCriticalDieBonusTextBox);
            // Bleed
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_count, CalculatorDamageBleedDieCountTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_size, CalculatorDamageBleedDieSizeTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_bonus, CalculatorDamageBleedDieBonusTextBox);
            // Critical Bleed
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_count_critical, CalculatorDamageCriticalBleedDieCountTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_size_critical, CalculatorDamageCriticalBleedDieSizeTextBox);
            setting_to_control.Add(EncounterSetting.damage_dice_DOT_bonus_critical, CalculatorDamageCriticalBleedDieBonusTextBox);

            // Reach
            setting_to_control.Add(EncounterSetting.range, CalculatorReachRangeIncrementTextBox);
            setting_to_control.Add(EncounterSetting.volley, CalculatorReachVolleyIncrementTextBox);
            setting_to_control.Add(EncounterSetting.move_speed, CalculatorReachMovementSpeedTextBox);

            // Reach
            setting_to_control.Add(EncounterSetting.number_of_encounters, CalculatorEncounterNumberOfEncountersTextBox);
            setting_to_control.Add(EncounterSetting.rounds_per_encounter, CalculatorEncounterRoundsPerEncounterTextBox);
            setting_to_control.Add(EncounterSetting.engagement_range, CalculatorEncounterEngagementRangeTextBox);

            // Action
            setting_to_control.Add(EncounterSetting.actions_per_round_any, CalculatorActionActionsPerRoundTextBox);
            setting_to_control.Add(EncounterSetting.actions_per_round_strike, CalculatorActionExtraLimitedActionsStrikeNumericUpDown);
            setting_to_control.Add(EncounterSetting.actions_per_round_draw, CalculatorActionExtraLimitedActionsDrawNumericUpDown);
            setting_to_control.Add(EncounterSetting.actions_per_round_stride, CalculatorActionExtraLimitedActionsStrideNumericUpDown);
            setting_to_control.Add(EncounterSetting.actions_per_round_reload, CalculatorActionExtraLimitedActionsReloadNumericUpDown);
            setting_to_control.Add(EncounterSetting.actions_per_round_long_reload, CalculatorActionExtraLimitedActionsLongReloadNumericUpDown);
        }
        private void StoreSettingToCheckbox()
        {
            setting_to_checkbox.Add(EncounterSetting.damage_dice_count_critical, CalculatorDamageCriticalDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_size_critical, CalculatorDamageCriticalDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_bonus_critical, CalculatorDamageCriticalDieCheckBox);

            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_count, CalculatorDamageBleedDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_size, CalculatorDamageBleedDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_bonus, CalculatorDamageBleedDieCheckBox);

            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_count_critical, CalculatorDamageCriticalBleedDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_size_critical, CalculatorDamageCriticalBleedDieCheckBox);
            setting_to_checkbox.Add(EncounterSetting.damage_dice_DOT_bonus_critical, CalculatorDamageCriticalBleedDieCheckBox);

            setting_to_checkbox.Add(EncounterSetting.magazine_size, CalculatorAmmunitionMagazineSizeCheckBox);
            setting_to_checkbox.Add(EncounterSetting.long_reload, CalculatorAmmunitionMagazineSizeCheckBox);

            setting_to_checkbox.Add(EncounterSetting.range, CalculatorReachRangeIncrementCheckBox);
            setting_to_checkbox.Add(EncounterSetting.volley, CalculatorReachVolleyIncrementCheckBox);
            setting_to_checkbox.Add(EncounterSetting.move_speed, CalculatorReachMovementSpeedCheckBox);
            setting_to_checkbox.Add(EncounterSetting.engagement_range, CalculatorEncounterEngagementRangeCheckBox);
        }
        private void StoreTickboxToTextboxes()
        {
            tickbox_to_textboxes.Clear();
            tickbox_to_textboxes.Add(key: CalculatorAmmunitionMagazineSizeCheckBox.GetHashCode(),
                                     value: new() { CalculatorAmmunitionMagazineSizeTextBox,
                                                    CalculatorAmmunitionLongReloadTextBox });

            tickbox_to_textboxes.Add(key: CalculatorDamageCriticalDieCheckBox.GetHashCode(),
                                     value: new() { CalculatorDamageCriticalDieCountTextBox,
                                                        CalculatorDamageCriticalDieSizeTextBox,
                                                        CalculatorDamageCriticalDieBonusTextBox});

            tickbox_to_textboxes.Add(key: CalculatorDamageBleedDieCheckBox.GetHashCode(),
                                     value: new() { CalculatorDamageBleedDieCountTextBox,
                                                        CalculatorDamageBleedDieSizeTextBox,
                                                        CalculatorDamageBleedDieBonusTextBox});

            tickbox_to_textboxes.Add(key: CalculatorDamageCriticalBleedDieCheckBox.GetHashCode(),
                                     value: new() { CalculatorDamageCriticalBleedDieCountTextBox,
                                                        CalculatorDamageCriticalBleedDieSizeTextBox,
                                                        CalculatorDamageCriticalBleedDieBonusTextBox});

            tickbox_to_textboxes.Add(key: CalculatorReachRangeIncrementCheckBox.GetHashCode(),
                                     value: new() { CalculatorReachRangeIncrementTextBox });

            tickbox_to_textboxes.Add(key: CalculatorReachVolleyIncrementCheckBox.GetHashCode(),
                                     value: new() { CalculatorReachVolleyIncrementTextBox });

            tickbox_to_textboxes.Add(key: CalculatorReachMovementSpeedCheckBox.GetHashCode(),
                                     value: new() { CalculatorReachMovementSpeedTextBox });

            tickbox_to_textboxes.Add(key: CalculatorEncounterEngagementRangeCheckBox.GetHashCode(),
                                     value: new() { CalculatorEncounterEngagementRangeTextBox });
        }

        // SETTINGS
        // Set the given setting
        /// <summary>
        /// Sets the value of the variable corresponding to the given control and index.
        /// </summary>
        /// <param name="control">Control to set the value of.</param>
        /// <param name="value">Value to set the respective variable to.</param>
        /// <param name="index">Index to set, if using a list-type variable.</param>
        private void SetValueBySetting(EncounterSetting encounterSetting, int value, int index = 0)
        {
            switch (encounterSetting)
            {
                // Attack
                case EncounterSetting.bonus_to_hit:
                    currEncounterSettings.bonus_to_hit = value;
                    break;
                case EncounterSetting.crit_threshhold:
                    currEncounterSettings.crit_threshhold = value;
                    break;
                case EncounterSetting.AC:
                    currEncounterSettings.AC = value;
                    break;
                case EncounterSetting.MAP_modifier:
                    currEncounterSettings.MAP_modifier = value;
                    break;
                // Ammunition
                case EncounterSetting.reload:
                    currEncounterSettings.reload = value;
                    break;
                case EncounterSetting.magazine_size:
                    currEncounterSettings.magazine_size = value;
                    break;
                case EncounterSetting.long_reload:
                    currEncounterSettings.long_reload = value;
                    break;
                case EncounterSetting.draw:
                    currEncounterSettings.draw = value;
                    break;
                // Damage
                // Base
                case EncounterSetting.damage_dice_count:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.count, value, index);
                    break;
                case EncounterSetting.damage_dice_size:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.size, value, index);
                    break;
                case EncounterSetting.damage_dice_bonus:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.bonus, value, index);
                    break;
                // Critical
                case EncounterSetting.damage_dice_count_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.count, value, index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_size_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.size, value, index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_bonus_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.bonus, value, index, edit_critical: true);
                    break;
                // Bleed
                case EncounterSetting.damage_dice_DOT_count:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.count, value, index);
                    break;
                case EncounterSetting.damage_dice_DOT_size:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.size, value, index);
                    break;
                case EncounterSetting.damage_dice_DOT_bonus:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.bonus, value, index);
                    break;
                // Critical
                case EncounterSetting.damage_dice_DOT_count_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.count, value, index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_DOT_size_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.size, value, index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_DOT_bonus_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.bonus, value, index, edit_critical: true);
                    break;

                // Reach
                case EncounterSetting.range:
                    currEncounterSettings.range = value;
                    break;
                case EncounterSetting.volley:
                    currEncounterSettings.volley = value;
                    break;
                case EncounterSetting.move_speed:
                    currEncounterSettings.move_speed = value;
                    break;
                // Encounter
                case EncounterSetting.number_of_encounters:
                    currEncounterSettings.number_of_encounters = value;
                    break;
                case EncounterSetting.rounds_per_encounter:
                    currEncounterSettings.rounds_per_encounter = value;
                    break;
                case EncounterSetting.engagement_range:
                    currEncounterSettings.engagement_range = value;
                    break;
                // Action
                case EncounterSetting.actions_per_round_any:
                    currEncounterSettings.actions_per_round.any = value;
                    break;
                case EncounterSetting.actions_per_round_strike:
                    currEncounterSettings.actions_per_round.strike = value;
                    break;
                case EncounterSetting.actions_per_round_draw:
                    currEncounterSettings.actions_per_round.draw = value;
                    break;
                case EncounterSetting.actions_per_round_stride:
                    currEncounterSettings.actions_per_round.stride = value;
                    break;
                case EncounterSetting.actions_per_round_reload:
                    currEncounterSettings.actions_per_round.reload = value;
                    break;
                case EncounterSetting.actions_per_round_long_reload:
                    currEncounterSettings.actions_per_round.long_reload = value;
                    break;
            }
        }
        private int GetValueBySetting(EncounterSetting encounterSetting, int index = 0)
        {
            switch (encounterSetting)
            {
                // Attack
                case EncounterSetting.bonus_to_hit:
                    return currEncounterSettings.bonus_to_hit;
                case EncounterSetting.crit_threshhold:
                    return currEncounterSettings.crit_threshhold;
                case EncounterSetting.AC:
                    return currEncounterSettings.AC;
                case EncounterSetting.MAP_modifier:
                    return currEncounterSettings.MAP_modifier;
                // Ammunition
                case EncounterSetting.reload:
                    return currEncounterSettings.reload;
                case EncounterSetting.magazine_size:
                    return currEncounterSettings.magazine_size;
                case EncounterSetting.long_reload:
                    return currEncounterSettings.long_reload;
                case EncounterSetting.draw:
                    return currEncounterSettings.draw;
                // Damage
                // Base
                case EncounterSetting.damage_dice_count:
                    return currEncounterSettings.damage_dice[index].Item1.Item1;
                case EncounterSetting.damage_dice_size:
                    return currEncounterSettings.damage_dice[index].Item1.Item2;
                case EncounterSetting.damage_dice_bonus:
                    return currEncounterSettings.damage_dice[index].Item1.Item3;
                // Critical
                case EncounterSetting.damage_dice_count_critical:
                    return currEncounterSettings.damage_dice[index].Item2.Item1;
                case EncounterSetting.damage_dice_size_critical:
                    return currEncounterSettings.damage_dice[index].Item2.Item2;
                case EncounterSetting.damage_dice_bonus_critical:
                    return currEncounterSettings.damage_dice[index].Item2.Item3;
                // Bleed
                case EncounterSetting.damage_dice_DOT_count:
                    return currEncounterSettings.damage_dice_DOT[index].Item1.Item1;
                case EncounterSetting.damage_dice_DOT_size:
                    return currEncounterSettings.damage_dice_DOT[index].Item1.Item2;
                case EncounterSetting.damage_dice_DOT_bonus:
                    return currEncounterSettings.damage_dice_DOT[index].Item1.Item3;
                // Critical
                case EncounterSetting.damage_dice_DOT_count_critical:
                    return currEncounterSettings.damage_dice_DOT[index].Item2.Item1;
                case EncounterSetting.damage_dice_DOT_size_critical:
                    return currEncounterSettings.damage_dice_DOT[index].Item2.Item2;
                case EncounterSetting.damage_dice_DOT_bonus_critical:
                    return currEncounterSettings.damage_dice_DOT[index].Item2.Item3;

                // Reach
                case EncounterSetting.range:
                    return currEncounterSettings.range;
                case EncounterSetting.volley:
                    return currEncounterSettings.volley;
                case EncounterSetting.move_speed:
                    return currEncounterSettings.move_speed;
                // Encounter
                case EncounterSetting.number_of_encounters:
                    return currEncounterSettings.number_of_encounters;
                case EncounterSetting.rounds_per_encounter:
                    return currEncounterSettings.rounds_per_encounter;
                case EncounterSetting.engagement_range:
                    return currEncounterSettings.engagement_range;
                // Action
                case EncounterSetting.actions_per_round_any:
                    return currEncounterSettings.actions_per_round.any;
                case EncounterSetting.actions_per_round_strike:
                    return currEncounterSettings.actions_per_round.strike;
                case EncounterSetting.actions_per_round_draw:
                    return currEncounterSettings.actions_per_round.draw;
                case EncounterSetting.actions_per_round_stride:
                    return currEncounterSettings.actions_per_round.stride;
                case EncounterSetting.actions_per_round_reload:
                    return currEncounterSettings.actions_per_round.reload;
                case EncounterSetting.actions_per_round_long_reload:
                    return currEncounterSettings.actions_per_round.long_reload;
            }
            throw new Exception("No encounter setting given!");
        }
        private void ResetSettingByControl(Control control, int index = 0)
        {
            switch (control_hash_to_setting[control.GetHashCode()])
            {
                // Attack
                case EncounterSetting.bonus_to_hit:
                    currEncounterSettings.bonus_to_hit = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.crit_threshhold:
                    currEncounterSettings.crit_threshhold = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.AC:
                    currEncounterSettings.AC = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.MAP_modifier:
                    currEncounterSettings.MAP_modifier = GetDefaultSettingByControl(control);
                    break;
                // Ammunition
                case EncounterSetting.reload:
                    currEncounterSettings.reload = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.magazine_size:
                    currEncounterSettings.magazine_size = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.long_reload:
                    currEncounterSettings.long_reload = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.draw:
                    currEncounterSettings.draw = GetDefaultSettingByControl(control);
                    break;
                // Damage
                // Base
                case EncounterSetting.damage_dice_count:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.count, GetDefaultSettingByControl(control), index);
                    break;
                case EncounterSetting.damage_dice_size:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.size, GetDefaultSettingByControl(control), index);
                    break;
                case EncounterSetting.damage_dice_bonus:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.bonus, GetDefaultSettingByControl(control), index);
                    break;
                // Critical
                case EncounterSetting.damage_dice_count_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.count, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_size_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.size, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_bonus_critical:
                    EditDamageDie(currEncounterSettings.damage_dice, DieField.bonus, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;
                // Bleed
                case EncounterSetting.damage_dice_DOT_count:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.count, GetDefaultSettingByControl(control), index);
                    break;
                case EncounterSetting.damage_dice_DOT_size:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.size, GetDefaultSettingByControl(control), index);
                    break;
                case EncounterSetting.damage_dice_DOT_bonus:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.bonus, GetDefaultSettingByControl(control), index);
                    break;
                // Critical
                case EncounterSetting.damage_dice_DOT_count_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.count, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_DOT_size_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.size, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;
                case EncounterSetting.damage_dice_DOT_bonus_critical:
                    EditDamageDie(currEncounterSettings.damage_dice_DOT, DieField.bonus, GetDefaultSettingByControl(control), index, edit_critical: true);
                    break;

                // Reach
                case EncounterSetting.range:
                    currEncounterSettings.range = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.volley:
                    currEncounterSettings.volley = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.move_speed:
                    currEncounterSettings.move_speed = GetDefaultSettingByControl(control);
                    break;
                // Encounter
                case EncounterSetting.number_of_encounters:
                    currEncounterSettings.number_of_encounters = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.rounds_per_encounter:
                    currEncounterSettings.rounds_per_encounter = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.engagement_range:
                    currEncounterSettings.engagement_range = GetDefaultSettingByControl(control);
                    break;
                // Action
                case EncounterSetting.actions_per_round_any:
                    currEncounterSettings.actions_per_round.any = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.actions_per_round_strike:
                    currEncounterSettings.actions_per_round.strike = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.actions_per_round_draw:
                    currEncounterSettings.actions_per_round.draw = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.actions_per_round_stride:
                    currEncounterSettings.actions_per_round.stride = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.actions_per_round_reload:
                    currEncounterSettings.actions_per_round.reload = GetDefaultSettingByControl(control);
                    break;
                case EncounterSetting.actions_per_round_long_reload:
                    currEncounterSettings.actions_per_round.long_reload = GetDefaultSettingByControl(control);
                    break;
            }
        }
        private int GetDefaultSettingByControl(Control control)
        {
            return control_hash_to_setting[control.GetHashCode()] switch
            {
                // Attack
                EncounterSetting.bonus_to_hit => 10,
                EncounterSetting.crit_threshhold => 20,
                EncounterSetting.AC => 21,
                EncounterSetting.MAP_modifier => 0,
                // Ammunition
                EncounterSetting.reload => 1,
                EncounterSetting.magazine_size => 0,
                EncounterSetting.long_reload => 0,
                EncounterSetting.draw => 1,
                // Damage
                // Base
                EncounterSetting.damage_dice_count => 1,
                EncounterSetting.damage_dice_size => 6,
                EncounterSetting.damage_dice_bonus => 0,
                // Critical
                EncounterSetting.damage_dice_count_critical => GetValueFromSetting(EncounterSetting.damage_dice_count),
                EncounterSetting.damage_dice_size_critical => GetValueFromSetting(EncounterSetting.damage_dice_size),
                EncounterSetting.damage_dice_bonus_critical => GetValueFromSetting(EncounterSetting.damage_dice_bonus),
                // Bleed
                EncounterSetting.damage_dice_DOT_count => 0,
                EncounterSetting.damage_dice_DOT_size => 0,
                EncounterSetting.damage_dice_DOT_bonus => 0,
                // Critical
                EncounterSetting.damage_dice_DOT_count_critical => setting_to_checkbox[EncounterSetting.damage_dice_DOT_count].Checked
                                                                    ? GetValueFromSetting(EncounterSetting.damage_dice_DOT_count)
                                                                    : 0,
                EncounterSetting.damage_dice_DOT_size_critical => setting_to_checkbox[EncounterSetting.damage_dice_DOT_count].Checked
                                                                    ? GetValueFromSetting(EncounterSetting.damage_dice_DOT_size)
                                                                    : 0,
                EncounterSetting.damage_dice_DOT_bonus_critical => setting_to_checkbox[EncounterSetting.damage_dice_DOT_count].Checked
                                                                    ? GetValueFromSetting(EncounterSetting.damage_dice_DOT_bonus)
                                                                    : 0,
                // Reach
                EncounterSetting.range => 100,
                EncounterSetting.volley => 0,
                EncounterSetting.move_speed => 25,
                // Encounter
                EncounterSetting.number_of_encounters => 10000,
                EncounterSetting.rounds_per_encounter => 6,
                EncounterSetting.engagement_range => 30,
                // Action
                EncounterSetting.actions_per_round_any => 3,
                EncounterSetting.actions_per_round_strike => 0,
                EncounterSetting.actions_per_round_draw => 0,
                EncounterSetting.actions_per_round_stride => 0,
                EncounterSetting.actions_per_round_reload => 0,
                EncounterSetting.actions_per_round_long_reload => 0,
                _ => 0,
            };
        }
        private int GetValueFromSetting(EncounterSetting setting, int index = 0)
        {
            return GetValueFromControl(setting_to_control[setting], index);
        }
        private int GetValueFromControl(Control control, int index = 0)
        {
            switch (control)
            {
                case TextBox textBox:
                    return int.TryParse(textBox.Text, out int result) ? result : GetDefaultSettingByControl(control);
                case NumericUpDown numericUpDown:
                    return (int)numericUpDown.Value;
                case ListBox listBox: // To-do: Investigate if this is actually being ran on ListBoxes
                    return listBox.Items.Count > index ? int.Parse((string)listBox.Items[index]) : GetDefaultSettingByControl(control);
                case CheckBox checkBox:
                    return checkBox.Checked ? 1 : 0;
            }

            Exception ex = new();
            ex.Data.Add(1001, "Given field cannot be parsed");
            throw ex;
        }

        // SETTING HELPERS
        //
        /// <summary>
        /// Edit the given damage die in the given damage_dice variable.
        /// </summary>
        /// <param name="setting">Which field to edit.</param>
        /// <param name="value">What to set the value of the damage die to.</param>
        /// <param name="index">What index to change the value of.</param>
        private static void EditDamageDie(List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> damage_dice, DieField setting, int value, int index, bool edit_critical = false)
        {// Store the relevant damage die
            Tuple<Tuple<int, int, int>, Tuple<int, int, int>> edited_damage_die = damage_dice[index];

            // Figure out whether edit_critical or base damage is being modified.
            Tuple<int, int, int> edited_die =
                (edit_critical)
                ? edited_damage_die.Item2 // Edit the edit_critical die
                : edited_damage_die.Item1;// Edit the base die

            Tuple<int, int, int> unedited_die =
                (edit_critical)
                ? edited_damage_die.Item1 // Storing the base die
                : edited_damage_die.Item2;// Storing the edit_critical die

            // Remove the old entry
            damage_dice.RemoveAt(index);

            switch (setting)
            {
                case DieField.count:
                    // Replace it with the new value
                    edited_die = new(value, edited_die.Item2, edited_die.Item3);
                    break;
                case DieField.size:
                    edited_die = new(edited_die.Item1, value, edited_die.Item3);
                    break;
                case DieField.bonus:
                    edited_die = new(edited_die.Item1, edited_die.Item2, value);
                    break;
            }

            damage_dice.Insert(index, new(edit_critical
                                                ? unedited_die
                                                : edited_die,
                                            edit_critical
                                                ? edited_die
                                                : unedited_die));
        }
        public enum DieField
        {
            count,
            size,
            bonus
        }

        // VISUAL UPDATES
        //
        private void InitializeGraphStartAppearance()
        {
            // Update the Batch Graph
            CalculatorBatchComputeScottPlot.Plot.SetAxisLimits(xMin: 0, xMax: 100, yMin: 0, yMax: 10);
            CalculatorBatchComputeScottPlot.Plot.SetOuterViewLimits(xMin: 0, xMax: 100, yMin: 0, yMax: 10);

            // Update the Base Graph
            CalculatorDamageDistributionScottPlot.Plot.SetAxisLimits(xMin: 0, xMax: 100, yMin: 0, yMax: 1000);
            CalculatorDamageDistributionScottPlot.Plot.SetOuterViewLimits(xMin: 0, xMax: 100, yMin: 0, yMax: 1000);
        }
        private void ReloadControl(Control control, string settingValue)
        {
            EncounterSetting encounterSetting = control_hash_to_setting[control.GetHashCode()];

            if (setting_to_checkbox.TryGetValue(encounterSetting, out var checkbox) && !checkbox.Checked) // Check if control is unticked
            { // Reload non-batched, checkbox regulated control appearance
                control.Text = string.Empty;
                control.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
            }
            else if (batch_mode_enabled && batched_variables.ContainsKey(encounterSetting)) // Check if control is batched
            {// Reload batched, regulated control appearance
                control.Text = "BATCHED";
                control.BackColor = PFKSettings.CurrentColorPallete.EntryBatched;
            }
            else // Default to normal
            {
                control.Text = settingValue;
                control.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
            }
        }
        private void ReloadControl(NumericUpDown control, int settingValue)
        {
            EncounterSetting encounterSetting = control_hash_to_setting[control.GetHashCode()];

            if (setting_to_checkbox.TryGetValue(encounterSetting, out var checkbox) && !checkbox.Checked) // Check if control is unticked
            { // Reload non-batched, checkbox regulated control appearance
                control.Value = settingValue;
                control.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
            }
            else if (batch_mode_enabled && batched_variables.ContainsKey(encounterSetting)) // Check if control is batched
            {// Reload batched, regulated control appearance
                control.Value = 0;
                control.BackColor = PFKSettings.CurrentColorPallete.EntryBatched;
            }
            else // Default to normal
            {
                control.Value = settingValue;
                control.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
            }
        }
        private void ReloadVisuals(EncounterSettings encounterSettings)
        {
            // Reload the ScottPlots
            CalculatorBatchComputeScottPlot.Refresh();
            CalculatorDamageDistributionScottPlot.Refresh();
            SettingsThemeMockupScottPlot.Refresh();

            // Reload the Encounter GroupBox
            ReloadControl(CalculatorEncounterNumberOfEncountersTextBox, encounterSettings.number_of_encounters.ToString());
            ReloadControl(CalculatorEncounterRoundsPerEncounterTextBox, encounterSettings.rounds_per_encounter.ToString());
            CalculatorEncounterEngagementRangeCheckBox.Checked = encounterSettings.engagement_range != GetDefaultSettingByControl(CalculatorEncounterEngagementRangeTextBox);
            ReloadControl(CalculatorEncounterEngagementRangeTextBox, encounterSettings.engagement_range.ToString());

            // Reload the Action GroupBox
            ReloadControl(CalculatorActionActionsPerRoundTextBox, encounterSettings.actions_per_round.any.ToString());
            ReloadControl(CalculatorActionExtraLimitedActionsStrikeNumericUpDown, encounterSettings.actions_per_round.strike);
            ReloadControl(CalculatorActionExtraLimitedActionsDrawNumericUpDown, encounterSettings.actions_per_round.draw);
            ReloadControl(CalculatorActionExtraLimitedActionsStrideNumericUpDown, encounterSettings.actions_per_round.stride);
            ReloadControl(CalculatorActionExtraLimitedActionsReloadNumericUpDown, encounterSettings.actions_per_round.reload);
            ReloadControl(CalculatorActionExtraLimitedActionsLongReloadNumericUpDown, encounterSettings.actions_per_round.long_reload);

            // Reload the Ammunition GroupBox
            ReloadControl(CalculatorAmmunitionReloadTextBox, encounterSettings.reload.ToString());
            CalculatorAmmunitionMagazineSizeCheckBox.Checked = encounterSettings.magazine_size != GetDefaultSettingByControl(CalculatorAmmunitionMagazineSizeTextBox);
            ReloadControl(CalculatorAmmunitionMagazineSizeTextBox, encounterSettings.magazine_size.ToString());
            ReloadControl(CalculatorAmmunitionLongReloadTextBox, encounterSettings.long_reload.ToString());
            ReloadControl(CalculatorAmmunitionDrawLengthTextBox, encounterSettings.draw.ToString());

            // Reload the Damage GroupBox
            CalculatorDamageListBox.Items.Clear();
            for (int dieIndex = 0; dieIndex < encounterSettings.damage_dice.Count; dieIndex++)
                CalculatorDamageListBox.Items.Add(CreateDamageListBoxString(encounterSettings.damage_dice[dieIndex], encounterSettings.damage_dice_DOT[dieIndex], index: dieIndex));
            SelectCheckBoxForToggle(CalculatorDamageBleedDieCheckBox);
            SelectCheckBoxForToggle(CalculatorDamageCriticalBleedDieCheckBox);
            SelectCheckBoxForToggle(CalculatorDamageCriticalDieCheckBox);

            // Reload the Attack GroupBox
            ReloadControl(CalculatorAttackBonusToHitTextBox, encounterSettings.bonus_to_hit.ToString());
            ReloadControl(CalculatorAttackCriticalHitMinimumTextBox, encounterSettings.crit_threshhold.ToString());
            ReloadControl(CalculatorAttackACTextBox, encounterSettings.AC.ToString());
            ReloadControl(CalculatorAttackMAPModifierTextBox, encounterSettings.MAP_modifier.ToString());

            // Reload the Reach GroupBox
            CalculatorReachRangeIncrementCheckBox.Checked = encounterSettings.range != GetDefaultSettingByControl(CalculatorReachRangeIncrementTextBox);
            ReloadControl(CalculatorReachRangeIncrementTextBox, encounterSettings.range.ToString());
            CalculatorReachMovementSpeedCheckBox.Checked = encounterSettings.seek_favorable_range;
            ReloadControl(CalculatorReachMovementSpeedTextBox, encounterSettings.move_speed.ToString());
            CalculatorReachVolleyIncrementCheckBox.Checked = encounterSettings.volley != GetDefaultSettingByControl(CalculatorReachVolleyIncrementTextBox);
            ReloadControl(CalculatorReachVolleyIncrementTextBox, encounterSettings.volley.ToString());

            ActiveControl = null;
        }
        private void UpdateStatisticsGUI(Tuple<int, int, int> percentiles)
        {
            // Update Encounter Statistics GUI
            CalculatorEncounterStatisticsMeanTextBox.Text = Math.Round(damageStats.average_encounter_damage, 2).ToString();
            CalculatorEncounterStatisticsMedianTextBox.Text = percentiles.Item2.ToString();
            CalculatorEncounterStatisticsUpperQuartileTextBox.Text = percentiles.Item3.ToString();
            CalculatorEncounterStatisticsLowerQuartileBoxTextBox.Text = percentiles.Item1.ToString();

            // Update Misc Statistics GUI
            CalculatorMiscStatisticsRoundDamageMeanTextBox.Text = Math.Round(damageStats.average_round_damage, 2).ToString();
            CalculatorMiscStatisticsAttackDamageMeanTextBox.Text = Math.Round(damageStats.average_hit_damage, 2).ToString();
            CalculatorMiscStatisticsAccuracyMeanTextBox.Text = (Math.Round(damageStats.average_accuracy * 100, 2)).ToString() + "%";
        }

        BarPlot CalculatorDamageDistributionScottPlotBarPlot = null;
        Legend CalculatorDamageDistributionScottPlotLegend = null;
        private void UpdateBaseGraph(Tuple<int, int, int> percentiles_int)
        {
            // Clear the Plot
            CalculatorDamageDistributionScottPlot.Plot.Clear();
            CalculatorDamageDistributionScottPlot.Plot.Style(grid: PFKSettings.CurrentColorPallete.Border,
                                                             axisLabel: PFKSettings.CurrentColorPallete.Text,
                                                             dataBackground: PFKSettings.CurrentColorPallete.GraphBackground);

            // Get the maximum value in the list
            int maxKey = damageStats.damage_bins.Keys.Max();
            // Convert the damage bins into an array
            double[] graphBins = Enumerable.Range(0, maxKey + 1)
                .Select(i => damageStats.damage_bins.TryGetValue(i, out int value) ? value : 0)
                .Select(i => (double)i)
                .ToArray();
            // Generate and store the edges of each bin
            double[] binEdges = Enumerable.Range(0, graphBins.Length).Select(x => (double)x).ToArray();

            // Render the Plot
            CalculatorDamageDistributionScottPlotBarPlot = CalculatorDamageDistributionScottPlot.Plot.AddBar(values: graphBins,
                                                                                                 positions: binEdges,
                                                                                                 color: PFKSettings.CurrentColorPallete.GraphBars);

            // Configure the Plot
            CalculatorDamageDistributionScottPlotBarPlot.BarWidth = 1;
            CalculatorDamageDistributionScottPlot.Plot.YAxis.Label("Occurances");
            CalculatorDamageDistributionScottPlot.Plot.YAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorDamageDistributionScottPlot.Plot.YAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorDamageDistributionScottPlot.Plot.YAxis.Color(PFKSettings.CurrentColorPallete.Text);

            CalculatorDamageDistributionScottPlot.Plot.XAxis.Label("Average Encounter Damage");
            CalculatorDamageDistributionScottPlot.Plot.XAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorDamageDistributionScottPlot.Plot.XAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorDamageDistributionScottPlot.Plot.XAxis.Color(PFKSettings.CurrentColorPallete.Text);

            CalculatorDamageDistributionScottPlot.Plot.SetAxisLimits(xMin: 0, yMin: 0,
                                                                    xMax: maxKey, yMax: damageStats.damage_bins.Values.Max() * 1.2);
            CalculatorDamageDistributionScottPlot.Plot.SetOuterViewLimits(yMin: 0, xMin: 0,
                                                                    xMax: maxKey, yMax: damageStats.damage_bins.Values.Max() * 1.2);

            CalculatorDamageDistributionScottPlot.Plot.Legend(location: ScottPlot.Alignment.UpperLeft);

            Tuple<double, double, double> percentiles = new(percentiles_int.Item1 + ((percentiles_int.Item1 == 0) ? 0.25 : 0),
                                                            percentiles_int.Item2 + ((percentiles_int.Item2 == 0) ? 0.25 : 0),
                                                            percentiles_int.Item3 + ((percentiles_int.Item3 == 0) ? 0.25 : 0));

            if (percentiles.Item1 == percentiles.Item2 && percentiles.Item2 == percentiles.Item3)
            { // Catch when all three are the same
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item1,
                                                                           color: Color.Red,
                                                                           label: "Q1, Q2, Q3");
            }
            else if (percentiles.Item1 == percentiles.Item2)
            { // Catch when lower two percentiles are the same
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item1,
                                                                           color: Color.Red,
                                                                           label: "Q1, Q2");
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item3,
                                                                           color: Color.Orange,
                                                                           label: "Q3");
            }
            else if (percentiles.Item2 == percentiles.Item3)
            { // Catch when upper two percentiles are the same
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item1, color: Color.Orange, label: "Q1");
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item2, color: Color.Red, label: "Q2, Q3");
            }
            else
            { // Default to three separate lines
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item1, color: Color.Orange, label: "Q1");
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item2, color: Color.Red, label: "Q2");
                CalculatorDamageDistributionScottPlot.Plot.AddVerticalLine(x: percentiles.Item3, color: Color.Orange, label: "Q3");
            }
            CalculatorDamageDistributionScottPlotLegend = CalculatorDamageDistributionScottPlot.Plot.Legend(location: ScottPlot.Alignment.UpperRight);
            CalculatorDamageDistributionScottPlotLegend.FillColor = PFKSettings.CurrentColorPallete.Background;
            CalculatorDamageDistributionScottPlotLegend.FontColor = PFKSettings.CurrentColorPallete.Text;
        }
        private void UpdateBatchGraph(BatchResults batch_results)
        {
            // Clear encounter tracking
            batch_results.highest_encounter_count = 0;

            // Clear the Plot
            CalculatorBatchComputeScottPlot.Plot.Clear();

            batch_results.processed_data = UpdateBatchGraph_PROCESS_DATA_ARR(batch_results, batch_view_selected_steps.ToArray());
            UpdateBatchGraph_RENDER_GRAPH(batch_results, batch_view_selected_steps.ToArray());
        }
        private static double[,] UpdateBatchGraph_PROCESS_DATA_ARR(BatchResults batch_results, int[] batch_selected)
        {
            // Extract a 2D array to use to populate the graph. Probably one of the most technically complex functions I've ever written tbh
            Array pulledLayer = HelperFunctions.GetSubArray(original_array: batch_results.raw_data, index_extracted: batch_selected);
            // Create the surface graph array as wide as the highest damage and as tall as the number of scaling variables
            double[,] processed_data = new double[batch_results.raw_data.GetLength(batch_results.raw_data.Rank - 1), batch_results.max_width];

            int currIndex;

            // Iterate to convert each horizonal entry
            for (int currRow = 0; currRow < processed_data.GetLength(0); currRow++)
            {
                // Set the current data column
                currIndex = currRow;
                // Store a copy of the cu
                // Store a reference to the current row being expanded
                Dictionary<int, int> currentStatsBlock = ((DamageStats)pulledLayer.GetValue(currIndex)).damage_bins;
                // Populate each row
                for (int currCol = 0; currCol < processed_data.GetLength(1); currCol++)
                {
                    // Store the current value, or a zero if it's not present
                    processed_data[currRow, currCol] = currentStatsBlock.TryGetValue(key: currCol, out int value)
                                                    ? value
                                                    : 0;

                    if (batch_results.highest_encounter_count < ((DamageStats)pulledLayer.GetValue(currIndex)).encounters_simulated)
                    {
                        batch_results.highest_encounter_count = ((DamageStats)pulledLayer.GetValue(currIndex)).encounters_simulated;
                    }
                }
            }

            return processed_data;
        }

        Colorbar CalculatorBatchComputeScottPlotColorbar = null;
        private void UpdateBatchGraph_RENDER_GRAPH(BatchResults batch_results, int[] batch_selected)
        {
            // Render the Plot
            var addedHeatmap = CalculatorBatchComputeScottPlot.Plot.AddHeatmap(batch_results.processed_data, lockScales: false);

            // Configure the Plot
            addedHeatmap.FlipVertically = true;

            // To-do: Add a timer hook to iterate the graph if it's selected

            // Generate a list of incrementing X ticks
            double[] xTicks = Enumerable.Range(0, batch_results.max_width)
                                        .Where(x => x % (batch_results.max_width / 8) == 0)
                                        .Select(Convert.ToDouble)
                                        .ToArray();
            CalculatorBatchComputeScottPlot.Plot.XTicks(positions: xTicks.Select(x => x + 0.5).ToArray(),
                                                        labels: xTicks.Select(x => x.ToString()).ToArray());

            // Label the X and Y Axes
            int highest_layer = batched_variables.Select(batchedSetting => batchedSetting.Value.Max(settingDict => settingDict.Value.layer)).Max();

            // Extract the dictionary of the currently selected layer
            List<Dictionary<EncounterSetting, Dictionary<int, int>>> tick_values =
                                batch_results.tick_values.Rank > 1
                                ? HelperFunctions.GetSubArray(original_array: batch_results.tick_values,
                                                            index_extracted: batch_selected)
                                    .Cast<Dictionary<EncounterSetting, Dictionary<int, int>>>()
                                    .ToList()
                                : batch_results.tick_values
                                    .Cast<Dictionary<EncounterSetting, Dictionary<int, int>>>()
                                    .ToList();

            // FILTER OUT Y-AXIS STATIC VARIABLES
            tick_values.First().ToList().ForEach(baseKVP =>
            {
                baseKVP.Value.ToList().ForEach(indexedKVP =>
                {
                    // Check each tick variable in an arbitrary tick (as all ticks will have every variable in theory)
                    if (tick_values.All(tickValueDict => tickValueDict[baseKVP.Key][indexedKVP.Key] == indexedKVP.Value))
                    { // If every dictionary has the same value for the checked tick variable, remove it from all dictionaries
                        tick_values.ForEach(iteratedKVP =>
                        {
                            // Check if we're going to have to snipe a single variable out
                            if (iteratedKVP[baseKVP.Key].Count > 1)
                            { // Remove only the targeted indexed value
                                iteratedKVP[baseKVP.Key].Remove(indexedKVP.Key);
                            }
                            else
                            { // Delete the entire encounterSetting dict
                                iteratedKVP.Remove(baseKVP.Key);
                            }
                        });
                    }
                });
            });

            // COMBINE SHORT ENOUGH Y-LABELS
            // Combine each batched variable into the Y-axis label
            int maxLength = 26; // set the maximum length here
            int numberOfLabelLines = 1; // Track number of lines being made
            int currentLineLength = 0;
            string labelStringSeparator = ", "; // Create a separator between each variable
            string labelString = ""; // Create an initial label string to concatenate to

            // Get a single string of each variable
            foreach (var encounterDictionaries in tick_values.First())
            {
                foreach (var encounterTickPair in encounterDictionaries.Value)
                {
                    string settingString = setting_to_string[encounterDictionaries.Key]
                                            + (encounterTickPair.Key != -1
                                                ? " (#" + (encounterTickPair.Key + 1).ToString() + ")"
                                                : "");
                    if (currentLineLength + settingString.Length > maxLength)
                    { // Current Line is too long, wrapping!
                        labelString += "\n"; // Wrap the string around
                        numberOfLabelLines++; // Increase number of lines
                        currentLineLength = settingString.Length; // Reset the current line length
                    }
                    else
                    { // Current Line isn't long enough to reset!
                        currentLineLength += settingString.Length; // Increase the current line length by the new string
                    }
                    // Store the current string.
                    labelString += settingString + labelStringSeparator;
                }
            }
            // Remove the trailing separator
            if (labelString.EndsWith(labelStringSeparator))
            {
                labelString = labelString[..^labelStringSeparator.Length];
            }

            // Apply the label
            CalculatorBatchComputeScottPlot.Plot.YAxis.Label(labelString);
            // Style size down, with a floor size of 8
            CalculatorBatchComputeScottPlot.Plot.YAxis.LabelStyle(fontSize: 16 / numberOfLabelLines + 6);
            CalculatorBatchComputeScottPlot.Plot.YAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorBatchComputeScottPlot.Plot.YAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorBatchComputeScottPlot.Plot.YAxis.Color(PFKSettings.CurrentColorPallete.Text);

            // Concatenate and assign y-tick values
            CalculatorBatchComputeScottPlot.Plot.YTicks(positions: Enumerable.Range(0, batch_results.dimensions[^1]).Select(x => x + 0.5).ToArray(),
                                                        labels: tick_values
                                                                    .Select(tickList => string.Join(values: tickList.Values
                                                                                                .SelectMany(encounterSettingTickDict => encounterSettingTickDict.Values)
                                                                                                .Select(encounterSettingValue => encounterSettingValue.ToString()),
                                                                                             separator: ", ")).ToArray());
            // Label the x-axis
            CalculatorBatchComputeScottPlot.Plot.XAxis.Label("Encounter Damage");
            CalculatorBatchComputeScottPlot.Plot.XAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorBatchComputeScottPlot.Plot.XAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
            CalculatorBatchComputeScottPlot.Plot.XAxis.Color(PFKSettings.CurrentColorPallete.Text);

            // Set the camera/axis limits
            CalculatorBatchComputeScottPlot.Plot.SetAxisLimits(xMin: 0, xMax: batch_results.max_width,
                                                               yMin: 0, yMax: batch_results.dimensions[^1]);
            CalculatorBatchComputeScottPlot.Plot.SetOuterViewLimits(xMin: 0, xMax: batch_results.max_width,
                                                                    yMin: 0, yMax: batch_results.dimensions[^1]);

            // Add & Configure the color bar
            CalculatorBatchComputeScottPlotColorbar = CalculatorBatchComputeScottPlot.Plot.AddColorbar(addedHeatmap);
            CalculatorBatchComputeScottPlotColorbar.AutomaticTicks(formatter: ConvertDoubleToPercentageString);
            CalculatorBatchComputeScottPlotColorbar.TickMarkColor = PFKSettings.CurrentColorPallete.Text;
            CalculatorBatchComputeScottPlotColorbar.TickLabelFont.Color = PFKSettings.CurrentColorPallete.Text;
        }
        private string ConvertDoubleToPercentageString(double value)
        {
            return Math.Round(value * 100 / damageStats_BATCH.highest_encounter_count, 2).ToString() + "%";
        }

        // SAFETY
        //
        private void CheckComputeSafety()
        {
            Exception ex = new();

            // Empty Data Exceptions
            // Attack Category
            if (CalculatorAttackBonusToHitTextBox.Text.Length == 0)
            {
                ex.Data.Add(201, "Missing Data Exception: Missing bonus to hit");
            }
            if (CalculatorAttackCriticalHitMinimumTextBox.Text.Length == 0)
            {
                ex.Data.Add(202, "Missing Data Exception: Missing crit threshhold");
            }
            if (CalculatorAttackACTextBox.Text.Length == 0)
            {
                ex.Data.Add(203, "Missing Data Exception: Missing AC");
            }
            if (CalculatorAttackMAPModifierTextBox.Text.Length == 0)
            {
                ex.Data.Add(204, "Missing Data Exception: Missing MAP modifier");
            }

            // Ammunition Category
            if (CalculatorAmmunitionReloadTextBox.Text.Length == 0)
            {
                ex.Data.Add(205, "Missing Data Exception: Missing reload");
            }
            if (CalculatorAmmunitionMagazineSizeCheckBox.Checked && CalculatorAmmunitionMagazineSizeTextBox.Text.Length == 0)
            {
                ex.Data.Add(206, "Missing Data Exception: Missing magazine size");
            }
            if (CalculatorAmmunitionMagazineSizeCheckBox.Checked && CalculatorAmmunitionDrawLengthTextBox.Text.Length == 0)
            {
                ex.Data.Add(207, "Missing Data Exception: Missing long reload");
            }
            if (CalculatorAmmunitionDrawLengthTextBox.Text.Length == 0)
            {
                ex.Data.Add(208, "Missing Data Exception: Missing draw length");
            }

            // Reach
            if (CalculatorReachRangeIncrementCheckBox.Checked && CalculatorReachRangeIncrementTextBox.Text.Length == 0)
            {
                ex.Data.Add(209, "Missing Data Exception: Missing range increment");
            }
            if (CalculatorReachVolleyIncrementCheckBox.Checked && CalculatorReachVolleyIncrementTextBox.Text.Length == 0)
            {
                ex.Data.Add(210, "Missing Data Exception: Missing volley increment");
            }

            // Encounter
            if (CalculatorEncounterNumberOfEncountersTextBox.Text.Length == 0)
            {
                ex.Data.Add(211, "Missing Data Exception: Missing number of encounters");
            }
            if (CalculatorEncounterRoundsPerEncounterTextBox.Text.Length == 0)
            {
                ex.Data.Add(212, "Missing Data Exception: Missing rounds per encounter");
            }
            if (CalculatorActionActionsPerRoundTextBox.Text.Length == 0)
            {
                ex.Data.Add(213, "Missing Data Exception: Missing actions per round");
            }

            // Distance
            if (CalculatorEncounterEngagementRangeCheckBox.Checked && CalculatorEncounterEngagementRangeTextBox.Text.Length == 0)
            {
                ex.Data.Add(214, "Missing Data Exception: Missing engagement range");
            }
            if (CalculatorReachMovementSpeedCheckBox.Checked && CalculatorReachMovementSpeedTextBox.Text.Length == 0)
            {
                ex.Data.Add(215, "Missing Data Exception: Missing movement speed");
            }

            // Action
            if (CalculatorActionExtraLimitedActionsDrawNumericUpDown.Text.Length == 0)
            {
                ex.Data.Add(216, "Missing Data Exception: Missing limited action draw");
            }
            if (CalculatorActionExtraLimitedActionsStrideNumericUpDown.Text.Length == 0)
            {
                ex.Data.Add(217, "Missing Data Exception: Missing limited action stride");
            }
            if (CalculatorActionExtraLimitedActionsReloadNumericUpDown.Text.Length == 0)
            {
                ex.Data.Add(218, "Missing Data Exception: Missing limited action reload");
            }
            if (CalculatorActionExtraLimitedActionsLongReloadNumericUpDown.Text.Length == 0)
            {
                ex.Data.Add(219, "Missing Data Exception: Missing limited action long reload");
            }
            if (CalculatorActionExtraLimitedActionsStrikeNumericUpDown.Text.Length == 0)
            {
                ex.Data.Add(220, "Missing Data Exception: Missing limited action strike");
            }


            // Bad Data Exceptions
            if (currEncounterSettings.number_of_encounters <= 0)
            { // Throw bad encounter data exception
                ex.Data.Add(101, "Bad Data Exception: Zero or negative encounter count");
            }
            if (currEncounterSettings.move_speed <= 0)
            { // Throw bad movement speed data exception
                ex.Data.Add(102, "Bad Data Exception: Zero or negative movement speed");
            }
            if (currEncounterSettings.rounds_per_encounter < 0)
            { // Throw bad rounds/encounter data exception
                ex.Data.Add(103, "Bad Data Exception: Negative rounds per encounter");
            }
            if (currEncounterSettings.actions_per_round.Total < 0)
            { // Throw bad actions/round data exception
                ex.Data.Add(104, "Bad Data Exception: Negative actions per round");
            }
            if (currEncounterSettings.range < 0)
            { // Throw bad range data exception
                ex.Data.Add(105, "Bad Data Exception: Negative range");
            }
            if (currEncounterSettings.engagement_range < 0)
            { // Throw bad encounter range data exception
                ex.Data.Add(106, "Bad Data Exception: Negative encounter range");
            }
            if (currEncounterSettings.damage_dice.Count <= 0 || currEncounterSettings.damage_dice_DOT.Count <= 0)
            { //  Throw no damage dice exception
                ex.Data.Add(51, "Damage Count Exception: No damage dice");
            }
            if (currEncounterSettings.damage_dice.Count != currEncounterSettings.damage_dice_DOT.Count)
            { // Throw imbalanced damage die exception
                ex.Data.Add(52, "Damage Count Exception: Imbalanced damage/edit_bleed dice");
            }

            // Batch Mode Checks
            if (batch_mode_enabled)
            {
                if (batched_variables.Count == 0)
                {
                    ex.Data.Add(701, "Missing Data Exception: No variables batched");
                }
            }

            // Throw exception on problems
            if (ex.Data.Count > 0)
                throw ex;
        }
        private void PushErrorCalculatorVariableErrorMessages(Exception ex)
        {
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 41: // Push
                        PushError(CalculatorReachVolleyIncrementTextBox, "Volley longer Range will have unintended implications with movement!");
                        break;
                    case 51: // Push
                        PushError(CalculatorDamageListBox, "No damage dice detected!");
                        break;
                    case 52: // Fix don't push
                        // N/A
                        break;
                    case 101: // Push
                        PushError(CalculatorEncounterNumberOfEncountersTextBox, "Number of encounters must be greater than 0!");
                        break;
                    case 102: // Push
                        PushError(CalculatorReachMovementSpeedTextBox, "Movement speed must be greater than 0!");
                        break;
                    case 103: // Push
                        PushError(CalculatorEncounterRoundsPerEncounterTextBox, "Rounds per encounter must be greater than 0!");
                        break;
                    case 104: // Push
                        PushError(CalculatorActionActionsPerRoundTextBox, "Actions per round must be positive!");
                        break;
                    case 105: // Push
                        PushError(CalculatorReachRangeIncrementTextBox, "Range increment must be positive!");
                        break;
                    case 106: // Push
                        PushError(CalculatorEncounterEngagementRangeTextBox, "Encounter distance must be positive!");
                        break;
                    case 201: // Push
                        PushError(CalculatorAttackBonusToHitTextBox, "No bonus to-hit value provided!");
                        break;
                    case 202: // Push
                        PushError(CalculatorAttackCriticalHitMinimumTextBox, "No crit at/below value provided!");
                        break;
                    case 203: // Push
                        PushError(CalculatorAttackACTextBox, "No AC value provided!");
                        break;
                    case 204: // Push
                        PushError(CalculatorAttackMAPModifierTextBox, "No MAP value provided!");
                        break;
                    case 205: // Push
                        PushError(CalculatorAmmunitionReloadTextBox, "No reload value provided!");
                        break;
                    case 206: // Push
                        PushError(CalculatorAmmunitionMagazineSizeTextBox, "No magazine size value provided!");
                        break;
                    case 207: // Push
                        PushError(CalculatorAmmunitionLongReloadTextBox, "No long reload value provided!");
                        break;
                    case 208: // Push
                        PushError(CalculatorAmmunitionDrawLengthTextBox, "No draw length value provided!");
                        break;
                    case 209: // Push
                        PushError(CalculatorReachRangeIncrementTextBox, "No range increment value provided!");
                        break;
                    case 210: // Push
                        PushError(CalculatorReachVolleyIncrementTextBox, "No volley increment value provided!");
                        break;
                    case 211: // Push
                        PushError(CalculatorEncounterNumberOfEncountersTextBox, "Number of encounters value not provided!");
                        break;
                    case 212: // Push
                        PushError(CalculatorEncounterRoundsPerEncounterTextBox, "No rounds per encounter value provided!");
                        break;
                    case 213: // Push
                        PushError(CalculatorActionActionsPerRoundTextBox, "No actions per round value provided!");
                        break;
                    case 214: // Push
                        PushError(CalculatorEncounterEngagementRangeTextBox, "No engagement range value provided!");
                        break;
                    case 215: // Push
                        PushError(CalculatorReachMovementSpeedTextBox, "No movement speed value provided!");
                        break;
                    case 216: // Push
                        PushError(CalculatorReachMovementSpeedTextBox, "No bonus draw action value provided!");
                        break;
                    case 217: // Push
                        PushError(CalculatorActionExtraLimitedActionsStrideNumericUpDown, "No bonus stride action value provided!", true);
                        break;
                    case 218: // Push
                        PushError(CalculatorActionExtraLimitedActionsReloadNumericUpDown, "No bonus reload action value provided!", true);
                        break;
                    case 219: // Push
                        PushError(CalculatorActionExtraLimitedActionsLongReloadNumericUpDown, "No bonus long reload action value provided!", true);
                        break;
                    case 220: // Push
                        PushError(CalculatorActionExtraLimitedActionsStrikeNumericUpDown, "No bonus draw strike value provided!", true);
                        break;
                    case 701: // Push
                        PushError(CalculatorBatchComputeButton, "No variables batched!");
                        break;
                }
            }
        }
        private void PushError(Control control, string errorString, bool isNumericUpDown = false, bool replace = false)
        {
            CalculatorErrorProvider.SetError(control, replace ? errorString : (CalculatorErrorProvider.GetError(control) + errorString));
            CalculatorErrorProvider.SetIconPadding(control, isNumericUpDown ? -36 : -18);
        }
        private void ClearError(Control control)
        {
            CalculatorErrorProvider.SetError(control, string.Empty);
        }
        // CONTROL SAFETY CLEARING
        private void NumericUpDown_ClearError(object sender, EventArgs e)
        {
            ClearError(sender as Control);
        }

        // DATA
        //
        private async Task<DamageStats> ComputeAverageDamage_SINGLE(int number_of_encounters,
                                                        int rounds_per_encounter,
                                                        RoundActions actions_per_round,
                                                        int reload_size,
                                                        int reload,
                                                        int long_reload,
                                                        int draw,
                                                        List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> damage_dice,
                                                        int bonus_to_hit,
                                                        int AC,
                                                        int crit_threshhold,
                                                        int MAP_modifier,
                                                        int engagement_range,
                                                        int move_speed,
                                                        bool seek_favorable_range,
                                                        int range,
                                                        int volley,
                                                        List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> damage_dice_DOT)
        {
            // Track Progress Bar
            Progress<int> progress = new();
            progress.ProgressChanged += SetProgressBar;

            // Set the flag for actively computing damage
            computing_damage = true;

            // Compute the damage
            Task<DamageStats> computeDamage = Task.Run(() => CalculateAverageDamage(number_of_encounters: number_of_encounters,
                                                        rounds_per_encounter: rounds_per_encounter,
                                                        actions_per_round: actions_per_round,
                                                        reload_size: reload_size,
                                                        reload: reload,
                                                        long_reload: long_reload,
                                                        draw: draw,
                                                        damage_dice: damage_dice,
                                                        bonus_to_hit: bonus_to_hit,
                                                        AC: AC,
                                                        crit_threshhold: crit_threshhold,
                                                        MAP_modifier: MAP_modifier,
                                                        engagement_range: engagement_range,
                                                        move_speed: move_speed,
                                                        seek_favorable_range: seek_favorable_range,
                                                        range: range,
                                                        volley: volley,
                                                        damage_dice_DOT: damage_dice_DOT,
                                                        progress: progress));

            damageStats = await computeDamage;
            computing_damage = false;

            return damageStats;
        }
        private EncounterSettings GetDamageSimulationVariables(EncounterSettings oldSettings)
        {
            EncounterSettings newSettings = new()
            {
                number_of_encounters = GetValueFromControl(CalculatorEncounterNumberOfEncountersTextBox),
                rounds_per_encounter = GetValueFromControl(CalculatorEncounterRoundsPerEncounterTextBox),
                actions_per_round = new RoundActions(any: GetValueFromControl(CalculatorActionActionsPerRoundTextBox),
                                                strike: GetValueFromControl(CalculatorActionExtraLimitedActionsStrikeNumericUpDown),
                                                reload: GetValueFromControl(CalculatorActionExtraLimitedActionsReloadNumericUpDown),
                                                long_reload: GetValueFromControl(CalculatorActionExtraLimitedActionsLongReloadNumericUpDown),
                                                draw: GetValueFromControl(CalculatorActionExtraLimitedActionsDrawNumericUpDown),
                                                stride: GetValueFromControl(CalculatorActionExtraLimitedActionsStrideNumericUpDown)),
                bonus_to_hit = GetValueFromControl(CalculatorAttackBonusToHitTextBox),
                AC = GetValueFromControl(CalculatorAttackACTextBox),
                crit_threshhold = GetValueFromControl(CalculatorAttackCriticalHitMinimumTextBox),
                MAP_modifier = GetValueFromControl(CalculatorAttackMAPModifierTextBox),
                draw = GetValueFromControl(CalculatorAmmunitionDrawLengthTextBox),
                reload = GetValueFromControl(CalculatorAmmunitionReloadTextBox),
                // Magazine / Long Reload
                magazine_size = GetValueFromControl(CalculatorAmmunitionMagazineSizeTextBox),
                // Magazine / Long Reload
                long_reload = GetValueFromControl(CalculatorAmmunitionLongReloadTextBox),
                // Engagement Range
                engagement_range = GetValueFromControl(CalculatorEncounterEngagementRangeTextBox),
                // Movement / Seek
                seek_favorable_range = GetValueFromControl(CalculatorReachMovementSpeedCheckBox) == 1,
                move_speed = GetValueFromControl(CalculatorReachMovementSpeedTextBox),
                // Range Increment
                range = GetValueFromControl(CalculatorReachRangeIncrementTextBox),
                // Range Increment
                volley = GetValueFromControl(CalculatorReachVolleyIncrementTextBox),
                damage_dice = oldSettings.damage_dice,
                damage_dice_DOT = oldSettings.damage_dice_DOT
            };

            return newSettings;
        }


        // FILTERING
        //

        // Filter normal digit entry out for TextBoxes.
        private void TextBox_KeyPressFilterToDigits(object sender, KeyPressEventArgs e)
        {
            TextBox textBox = sender as TextBox;

            if (CheckControlBatched(control_hash_to_setting[textBox.GetHashCode()]))
                return;

            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                if (!e.KeyChar.Equals('+') && !e.KeyChar.Equals('-') || textBox?.Text.Count(x => (x.Equals('+') || x.Equals('-'))) >= 1)
                {
                    e.Handled = true;
                }
            }

            ClearError(textBox);
        }
        // Filter non-digits and non-sign characters out of pasted entries using regex.
        private void TextBox_TextChangedFilterToDigitsAndSign(object sender, EventArgs e)
        {

            TextBox textBox = sender as TextBox;

            if (CheckControlBatched(control_hash_to_setting[textBox.GetHashCode()]))
                return;

            int currSelectStart = textBox.SelectionStart;

            // Strip characters out of pasted text
            textBox.Text = DigitSignRegex().Replace(input: textBox.Text, "");
            for (int index = textBox.Text.Length - 1; index > 0; index--)
            {
                if (textBox.Text.Length < index)
                    break;
                if (textBox.Text[index].Equals('+') || textBox.Text[index].Equals('-'))
                {
                    textBox.Text = textBox.Text.Remove(startIndex: index, count: 1); // Remove any pluses/minuses after the first
                }
            }

            textBox.SelectionStart = Math.Clamp((textBox.Text.Length > 0)
                                                    ? currSelectStart
                                                    : currSelectStart,
                                                0,
                                                (textBox.Text.Length > 0)
                                                    ? textBox.Text.Length
                                                    : 0);
        }
        // Filter digits out of pasted entries using regex.
        private void TextBox_TextChangedFilterToDigits(object sender, EventArgs e)
        { // Filter only if the textbox isn't batched

            TextBox textBox = sender as TextBox;

            if (CheckControlBatched(control_hash_to_setting[textBox.GetHashCode()]))
                return;

            // Strip characters out of pasted text
            textBox.Text = DigitRegex().Replace(input: textBox.Text, replacement: "");

            ClearError(textBox);
        }
        private void TextBox_TextChangedFilterToDigitsAndCommaAndSign(object sender, EventArgs e)
        {
            TextBox textBox = sender as TextBox;

            int currSelectStart = textBox.SelectionStart;

            // Strip characters out of pasted text
            textBox.Text = DigitCommaSignRegex().Replace(input: textBox.Text, replacement: "");
            textBox.Text = SingleCommaRegex().Replace(input: textBox.Text, replacement: "");
            textBox.Text = SingleSignRegex().Replace(input: textBox.Text, replacement: "");
            textBox.Text = SignIntoCommaRegex().Replace(input: textBox.Text, replacement: "");
            Match matches = DigitSignDigitRegex().Match(input: textBox.Text);
            if (matches.Length > 0)
            {
                textBox.Text = DigitSignDigitRegex().Replace(input: textBox.Text, replacement: ",$0");
                currSelectStart++;
            }

            textBox.SelectionStart = Math.Clamp((textBox.Text.Length > 0)
                                                    ? currSelectStart
                                                    : currSelectStart,
                                                0,
                                                (textBox.Text.Length > 0)
                                                    ? textBox.Text.Length
                                                    : 0);

            ClearError(textBox);
        }
        // Filter out lone plus/minus
        private void TextBox_LeaveClearLoneSymbol(object sender, EventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox.Text.Length == 1 && (textBox.Text[0].Equals('+') || textBox.Text[0].Equals('-')))
                textBox.Text = string.Empty;
        }
        private void TextBox_LeaveClearLoneOrTrailingComma(object sender, EventArgs e)
        {
            TextBox textBox = sender as TextBox;
            while (textBox.Text.Length > 0 && (textBox.Text[^1].Equals(',')
                                                || textBox.Text[^1].Equals('-')
                                                || textBox.Text[^1].Equals('+')))
                textBox.Text = textBox.Text[..(textBox.TextLength - 1)];
        }

        [GeneratedRegex(@"([^\d\,\+\-])")] // abc or = etc
        private static partial Regex DigitCommaSignRegex();
        [GeneratedRegex(@"[^\d-+]")] // abc or =, etc
        private static partial Regex DigitSignRegex();
        [GeneratedRegex(@"(?<=[\d])[\+\-](?=[\d])")] // 25-15 or 1+62
        private static partial Regex DigitSignDigitRegex();
        [GeneratedRegex(@"[^\d]")] // abc or =-, etc
        private static partial Regex DigitRegex();

        [GeneratedRegex(@"\,(?=\,)")] // ,, or ,,,, etc
        private static partial Regex SingleCommaRegex();
        [GeneratedRegex(@"[\+\-](?=[\+\-])")] // -- or ++ etc
        private static partial Regex SingleSignRegex();
        [GeneratedRegex(@"[\+\-](?=[\,])")] // -, or +,
        private static partial Regex SignIntoCommaRegex();

        // CARAT CONTROL
        [DllImport("user32.dll")] static extern bool HideCaret(IntPtr hWnd);
        void TextBox_GotFocusDisableCarat(object sender, EventArgs e)
        {
            HideCaret((sender as TextBox).Handle);
        }

        // CHECK BOXES
        private void CheckBox_CheckChangedToggleTextBoxes(object sender, EventArgs e)
        {
            SelectCheckBoxForToggle(sender as CheckBox);
        }
        private void SelectCheckBoxForToggle(CheckBox checkBox)
        {
            CheckboxToggleTextbox(checkBox, tickbox_to_textboxes[checkBox.GetHashCode()]);
        }
        private void CheckboxToggleTextbox(CheckBox checkBox, List<TextBox> textBoxes)
        {
            foreach (TextBox textBox in textBoxes)
            {
                if (checkBox.Checked)
                {
                    textBox.Text = GetValueFromControl(control: textBox).ToString();
                    textBox.Enabled = true;
                    textBox.ReadOnly = false;
                    textBox.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
                }
                else
                {
                    textBox.Text = string.Empty;
                    textBox.Enabled = false;
                    textBox.ReadOnly = true;
                    textBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                    ActiveControl = null;
                }
            }
        }

        // DAMAGE BUTTONS
        private void DamageDeleteButton_MouseClick(object sender, MouseEventArgs e)
        {
            int index = CalculatorDamageListBox.SelectedIndex;
            if (index != -1)
            { // Only remove if an index is selected
                // Check if there's any batched variables to potentially de-batch
                if (batched_variables.Count > 0)
                { // De-batch the given damage die
                    batched_variables.Where(batched_variable => batched_variable.Value.ContainsKey(index)) // Filter to batched variables at this index
                                     .ToList()
                                     .ForEach(removedSetting =>
                                     { // Remove each of the found batched variable at this index
                                         if (removedSetting.Value.Count > 1)
                                         { // Only remove this index if there's other batched variables
                                             batched_variables[removedSetting.Key].Remove(index);
                                         }
                                         else
                                         { // Remove the whole variable if it's the only one
                                             batched_variables.Remove(removedSetting.Key);
                                         }
                                     });
                }

                // Clear Old Data
                DeleteDamageDice(index);
                // Disable the UI
                SetDamageDiceFieldsEnabled(false);
            }
        }
        private void DamageAddButton_MouseClick(object sender, MouseEventArgs e)
        {
            AddDamageDice();
        }
        private void DamageSaveButton_MouseClick(object sender, MouseEventArgs e)
        {
            int index = CalculatorDamageListBox.SelectedIndex;
            if (index != -1)
            {
                // Clear Old Data
                DeleteDamageDice(index);

                // Add New Data
                AddDamageDice(index);

                // Disable the UI
                SetDamageDiceFieldsEnabled(false);
            }
        }

        // Damage Dice Helper Methods
        private void LoadDamageDice(int index)
        {

            // Store references to each dice/bonus
            var currDamageDie = currEncounterSettings.damage_dice[index];
            var currBleedDie = currEncounterSettings.damage_dice_DOT[index];

            // Save each dice to the GUI
            // Regular Damage Die
            CalculatorDamageDieCountTextBox.Text = currDamageDie.Item1.Item1.ToString();
            CalculatorDamageDieSizeTextBox.Text = currDamageDie.Item1.Item2.ToString();
            CalculatorDamageDieBonusTextBox.Text = currDamageDie.Item1.Item3.ToString();

            // Critical Damage Die
            if (currDamageDie.Item2.Item1 != 0 && currDamageDie.Item2.Item1 != currDamageDie.Item1.Item1
                || currDamageDie.Item2.Item2 != 0 && currDamageDie.Item2.Item2 != currDamageDie.Item1.Item2
                || currDamageDie.Item2.Item3 != 0 && currDamageDie.Item2.Item3 != currDamageDie.Item1.Item3)
            {
                CalculatorDamageCriticalDieCheckBox.Checked = true;
                CalculatorDamageCriticalDieCountTextBox.Text = (currDamageDie.Item2.Item1 != 0) // X != 0
                                                        ? currDamageDie.Item2.Item1.ToString()
                                                        : currDamageDie.Item1.Item1.ToString();

                CalculatorDamageCriticalDieSizeTextBox.Text = (currDamageDie.Item2.Item2 != 0) // Y != 0
                                                        ? currDamageDie.Item2.Item2.ToString()
                                                        : currDamageDie.Item1.Item2.ToString();

                CalculatorDamageCriticalDieBonusTextBox.Text = (currDamageDie.Item2.Item3 != 0) // Z != 0
                                                        ? currDamageDie.Item2.Item3.ToString()
                                                        : currDamageDie.Item1.Item3.ToString();
            }
            else
            { // Clear the critical damage die textboxes and checkbox
                CalculatorDamageCriticalDieCheckBox.Checked = false;
            }
            SelectCheckBoxForToggle(CalculatorDamageCriticalDieCheckBox);


            // Bleed Die
            if (currBleedDie.Item1.Item1 != 0
                || currBleedDie.Item1.Item2 != 0
                || currBleedDie.Item1.Item3 != 0)
            {
                CalculatorDamageBleedDieCheckBox.Checked = true;
                CalculatorDamageBleedDieCountTextBox.Text = (currBleedDie.Item1.Item1 != 0) // X != 0
                                                        ? currBleedDie.Item1.Item1.ToString()
                                                        : "0";

                CalculatorDamageBleedDieSizeTextBox.Text = (currBleedDie.Item1.Item2 != 0) // Y != 0
                                                        ? currBleedDie.Item1.Item2.ToString()
                                                        : "0";

                CalculatorDamageBleedDieBonusTextBox.Text = (currBleedDie.Item1.Item3 != 0) // Z != 0
                                                        ? currBleedDie.Item1.Item3.ToString()
                                                        : "0";

            }
            else
            { // Clear the bleed damage die textboxes and checkbox
                CalculatorDamageBleedDieCheckBox.Checked = false;
            }
            SelectCheckBoxForToggle(CalculatorDamageBleedDieCheckBox);

            // To-do: Fix tick boxes not getting auto-darkened properly in damage die
            // To-do: Fix tickboxes being the wrong color when set to disabled/readonly

            // Critical Bleed Damage Die
            if (currBleedDie.Item2.Item1 != 0 && currBleedDie.Item2.Item1 != currBleedDie.Item1.Item1
                || currBleedDie.Item2.Item2 != 0 && currBleedDie.Item2.Item2 != currBleedDie.Item1.Item2
                || currBleedDie.Item2.Item3 != 0 && currBleedDie.Item2.Item3 != currBleedDie.Item1.Item3)
            {
                CalculatorDamageCriticalBleedDieCheckBox.Checked = true;
                CalculatorDamageCriticalBleedDieCountTextBox.Text = (currBleedDie.Item2.Item1 != 0) // X != 0
                                                        ? currBleedDie.Item2.Item1.ToString()
                                                        : currBleedDie.Item1.Item1.ToString();

                CalculatorDamageCriticalBleedDieSizeTextBox.Text = (currBleedDie.Item2.Item2 != 0) // Y != 0
                                                        ? currBleedDie.Item2.Item2.ToString()
                                                        : currBleedDie.Item1.Item2.ToString();

                CalculatorDamageCriticalBleedDieBonusTextBox.Text = (currBleedDie.Item2.Item3 != 0) // Z != 0
                                                        ? currBleedDie.Item2.Item3.ToString()
                                                        : currBleedDie.Item1.Item3.ToString();
            }
            else
            { // Clear the critical damage die textboxes and checkbox
                CalculatorDamageCriticalBleedDieCheckBox.Checked = false;
            }
            SelectCheckBoxForToggle(CalculatorDamageCriticalBleedDieCheckBox);

            // Enable the UI
            SetDamageDiceFieldsEnabled(true);
        }
        private void DeleteDamageDice(int index)
        {
            currEncounterSettings.damage_dice.RemoveAt(index);
            currEncounterSettings.damage_dice_DOT.RemoveAt(index);
            CalculatorDamageListBox.Items.RemoveAt(index);
        }
        /// <summary>
        /// Reads damage dice text/check boxes and adds data accordingly to the back of the list, or at the given index.
        /// </summary>
        /// <param name="index"></param>
        private void AddDamageDice(int index = -1)
        {
            // Save each dice from the GUI
            var values = GetDamageControlValues();

            // Store references to each dice/bonus
            Tuple<Tuple<int, int, int>, Tuple<int, int, int>> currDamageDie = new(values.Item1, values.Item2);
            Tuple<Tuple<int, int, int>, Tuple<int, int, int>> currBleedDie = new(values.Item3, values.Item4);

            string entryString = CreateDamageListBoxString(currDamageDie, currBleedDie, index);

            // Store the damage die in the
            AddDamageDieToListBox(entryString, index);

            // Add the new string to the list
            if (index == -1)
            {
                currEncounterSettings.damage_dice.Add(item: currDamageDie); // Store the damage
                currEncounterSettings.damage_dice_DOT.Add(item: currBleedDie); // Store the edit_bleed damage
            }
            else
            {
                currEncounterSettings.damage_dice.Insert(index: index, item: currDamageDie); // Store the damage
                currEncounterSettings.damage_dice_DOT.Insert(index: index, item: currBleedDie); // Store the edit_bleed damage
            }
        }
        private void AddDamageDieToListBox(string newEntry, int index = -1)
        {
            if (index == -1)
                CalculatorDamageListBox.Items.Add(item: newEntry); // Store the text entry
            else if (index != -1)
                CalculatorDamageListBox.Items.Insert(index: index, item: newEntry); // Store the text entry
        }
        private string CreateDamageListBoxString(Tuple<Tuple<int, int, int>, Tuple<int, int, int>> currDamageDie,
                                                    Tuple<Tuple<int, int, int>, Tuple<int, int, int>> currBleedDie,
                                                    int index)
        {
            // Store references to each dice/bonus
            // Add a visual entry to the ListBox
            // Base Base Bools
            bool hasBaseBonus = currDamageDie.Item1.Item3 != 0;
            // Bleed Critical Bools
            bool hasCriticalCount = currDamageDie.Item2.Item1 != currDamageDie.Item1.Item1;
            bool hasCriticalCountBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_count_critical,
                                                                            out var criticalCountDict) && criticalCountDict.ContainsKey(index);
            bool hasCriticalSides = currDamageDie.Item2.Item2 != currDamageDie.Item1.Item2;
            bool hasCriticalSidesBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_size_critical,
                                                                            out var criticalSizeDict) && criticalSizeDict.ContainsKey(index);
            bool hasCriticalBonus = currDamageDie.Item2.Item3 != currDamageDie.Item1.Item3;
            bool hasCriticalBonusBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_bonus_critical,
                                                                            out var criticalBonusDict) && criticalBonusDict.ContainsKey(index);
            // Bleed Base Bools
            bool hasBaseBleedCount = currBleedDie.Item1.Item1 != 0;
            bool hasBaseBleedCountBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_count,
                                                                            out var bleedCountDict) && bleedCountDict.ContainsKey(index);
            bool hasBaseBleedSides = currBleedDie.Item1.Item2 != 0;
            bool hasBaseBleedSidesBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_size,
                                                                            out var bleedSizeDict) && bleedSizeDict.ContainsKey(index);
            bool hasBaseBleedBonus = currBleedDie.Item1.Item3 != 0;
            bool hasBaseBleedBonusBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_bonus,
                                                                            out var bleedBonusDict) && bleedBonusDict.ContainsKey(index);
            // Bleed Critical Bools
            bool hasCriticalBleedCount = currBleedDie.Item2.Item1 != currBleedDie.Item1.Item1;
            bool hasCriticalBleedCountBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_count,
                                                                            out var criticalBleedCountDict) && criticalBleedCountDict.ContainsKey(index);
            bool hasCriticalBleedSides = currBleedDie.Item2.Item2 != currBleedDie.Item1.Item2;
            bool hasCriticalBleedSidesBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_size,
                                                                            out var criticalBleedSizeDict) && criticalBleedSizeDict.ContainsKey(index);
            bool hasCriticalBleedBonus = currBleedDie.Item2.Item3 != currBleedDie.Item1.Item3;
            bool hasCriticalBleedBonusBATCHED = batched_variables.TryGetValue(EncounterSetting.damage_dice_DOT_bonus,
                                                                            out var criticalBleedBonusDict) && criticalBleedBonusDict.ContainsKey(index);
            // Store signs of each bonus
            string[] signs = new string[4] { (currDamageDie.Item1.Item3 < 0) ? "" : "+",
                                        (currDamageDie.Item2.Item3 < 0) ? "" : "+",
                                        (currBleedDie.Item1.Item3 < 0) ? "" : "+",
                                        (currBleedDie.Item2.Item3 < 0) ? "" : "+" };

            // Base damage dice
            string entryString = currDamageDie.Item1.Item1 + "D" + currDamageDie.Item1.Item2;
            if (hasBaseBonus)
                entryString += signs[0] + currDamageDie.Item1.Item3;
            // Critical dice
            if (hasCriticalCount || hasCriticalSides || hasCriticalBonus)
                entryString += " (🗲 "
                                + (hasCriticalCountBATCHED ? "*"
                                                           : hasCriticalCount ? currDamageDie.Item2.Item1.ToString()
                                                                              : "")
                                + ((hasCriticalCount || hasCriticalSides) ? "D" : "")
                                + (hasCriticalSidesBATCHED ? "*"
                                                           : hasCriticalSides ? currDamageDie.Item2.Item2.ToString()
                                                                              : "")
                                + (hasCriticalBonusBATCHED ? signs[1] + "*"
                                                           : hasCriticalBonus ? signs[1] + currDamageDie.Item2.Item3
                                                                              : "")
                                + ")";
            // Bleed dice
            if (hasBaseBleedCount || hasBaseBleedSides || hasBaseBleedBonus
                || hasCriticalBleedCount || hasCriticalBleedSides || hasCriticalBleedBonus)
                entryString += " 🗡 ";
            // Base dice
            if (hasBaseBleedCount || hasBaseBleedSides || hasBaseBleedBonus)
                entryString += (hasBaseBleedCountBATCHED ? "*"
                                                         : hasBaseBleedCount ? currBleedDie.Item1.Item1.ToString()
                                                                             : "")
                                + ((hasBaseBleedCount || hasBaseBleedSides) ? "D" : "")
                                + (hasBaseBleedSidesBATCHED ? "*"
                                                            : hasBaseBleedSides ? currBleedDie.Item1.Item2.ToString()
                                                                                : "")
                                + (hasBaseBleedBonusBATCHED ? "*"
                                                            : hasBaseBleedBonus ? signs[2] + currBleedDie.Item1.Item3
                                                                                : "");
            // Critical
            if (hasCriticalBleedCount || hasCriticalBleedSides || hasCriticalBleedBonus)
                entryString += " (🗲 "
                                + (hasCriticalBleedCountBATCHED ? "*"
                                                                : hasCriticalBleedCount ? currBleedDie.Item2.Item1.ToString()
                                                                                        : "")
                                + ((hasCriticalBleedCount || hasCriticalBleedSides) ? "D" : "")
                                + (hasCriticalBleedSidesBATCHED ? "*"
                                                                : hasCriticalBleedSides ? currBleedDie.Item2.Item2.ToString()
                                                                                        : "")
                                + (hasCriticalBleedBonusBATCHED ? "*"
                                                                : hasCriticalBleedBonus ? signs[3] + currBleedDie.Item2.Item3 : "")
                                + ")";

            return entryString;
        }
        private Tuple<Tuple<int, int, int>, Tuple<int, int, int>,
                    Tuple<int, int, int>, Tuple<int, int, int>> GetDamageControlValues()
        {
            // COUNT
            int damageCount = GetValueFromControl(CalculatorDamageDieCountTextBox);
            int damageCritCount = GetValueFromControl(CalculatorDamageCriticalDieCountTextBox);
            int damageBleedCount = GetValueFromControl(CalculatorDamageBleedDieCountTextBox);
            int damageCritBleedCount = GetValueFromControl(CalculatorDamageCriticalBleedDieCountTextBox);

            // SIZE
            int damageSize = GetValueFromControl(CalculatorDamageDieSizeTextBox);
            int damageCritSize = GetValueFromControl(CalculatorDamageCriticalDieSizeTextBox);
            int damageBleedSize = GetValueFromControl(CalculatorDamageBleedDieSizeTextBox);
            int damageCritBleedSize = GetValueFromControl(CalculatorDamageCriticalBleedDieSizeTextBox);

            // BONUS
            int damageBonus = GetValueFromControl(CalculatorDamageDieBonusTextBox);
            int damageCritBonus = GetValueFromControl(CalculatorDamageCriticalDieBonusTextBox);
            int damageBleedBonus = GetValueFromControl(CalculatorDamageBleedDieBonusTextBox);
            int damageCritBleedBonus = GetValueFromControl(CalculatorDamageCriticalBleedDieBonusTextBox);

            return new(new(damageCount, damageSize, damageBonus),
                        new(damageCritCount, damageCritSize, damageCritBonus),
                        new(damageBleedCount, damageBleedSize, damageBleedBonus),
                        new(damageCritBleedCount, damageCritBleedSize, damageCritBleedBonus));
        }
        private void SetDamageDiceFieldsEnabled(bool state)
        {
            if (state)
            { // Enable each textbox/checkbox
                // Base Damage
                CalculatorDamageDieCountTextBox.Enabled = true;
                CalculatorDamageDieCountTextBox.ReadOnly = false;
                CalculatorDamageDieCountTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
                CalculatorDamageDieSizeTextBox.Enabled = true;
                CalculatorDamageDieSizeTextBox.ReadOnly = false;
                CalculatorDamageDieSizeTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
                CalculatorDamageDieBonusTextBox.Enabled = true;
                CalculatorDamageDieBonusTextBox.ReadOnly = false;
                CalculatorDamageDieBonusTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
                // Critical Damage
                CalculatorDamageCriticalDieCheckBox.Enabled = true;
                CalculatorDamageCriticalDieCountTextBox.Enabled = CalculatorDamageCriticalDieCheckBox.Checked;
                CalculatorDamageCriticalDieCountTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalDieSizeTextBox.Enabled = CalculatorDamageCriticalDieCheckBox.Checked;
                CalculatorDamageCriticalDieSizeTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalDieBonusTextBox.Enabled = CalculatorDamageCriticalDieCheckBox.Checked;
                CalculatorDamageCriticalDieBonusTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                // Dot Damage
                CalculatorDamageBleedDieCheckBox.Enabled = true;
                CalculatorDamageBleedDieCountTextBox.Enabled = CalculatorDamageBleedDieCheckBox.Checked;
                CalculatorDamageBleedDieCountTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageBleedDieSizeTextBox.Enabled = CalculatorDamageBleedDieCheckBox.Checked;
                CalculatorDamageBleedDieSizeTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageBleedDieBonusTextBox.Enabled = CalculatorDamageBleedDieCheckBox.Checked;
                CalculatorDamageBleedDieBonusTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                // Critical Dot Damage
                CalculatorDamageCriticalBleedDieCheckBox.Enabled = true;
                CalculatorDamageCriticalBleedDieCountTextBox.Enabled = CalculatorDamageCriticalBleedDieCheckBox.Checked;
                CalculatorDamageCriticalBleedDieCountTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalBleedDieSizeTextBox.Enabled = CalculatorDamageCriticalBleedDieCheckBox.Checked;
                CalculatorDamageCriticalBleedDieSizeTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalBleedDieBonusTextBox.Enabled = CalculatorDamageCriticalBleedDieCheckBox.Checked;
                CalculatorDamageCriticalBleedDieBonusTextBox.BackColor = CalculatorDamageCriticalDieCheckBox.Checked
                                                                    ? PFKSettings.CurrentColorPallete.EntryNormal
                                                                    : PFKSettings.CurrentColorPallete.EntryDisabled;
            }
            else
            { // Disable each textbox/checkbox
                // Base Damage
                CalculatorDamageDieCountTextBox.Enabled = false;
                CalculatorDamageDieCountTextBox.ReadOnly = true;
                CalculatorDamageDieCountTextBox.Text = string.Empty;
                CalculatorDamageDieCountTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageDieSizeTextBox.Enabled = false;
                CalculatorDamageDieSizeTextBox.ReadOnly = true;
                CalculatorDamageDieSizeTextBox.Text = string.Empty;
                CalculatorDamageDieSizeTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageDieBonusTextBox.Enabled = false;
                CalculatorDamageDieBonusTextBox.ReadOnly = true;
                CalculatorDamageDieBonusTextBox.Text = string.Empty;
                CalculatorDamageDieBonusTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                // Critical Damage
                CalculatorDamageCriticalDieCheckBox.Enabled = false;
                CalculatorDamageCriticalDieCheckBox.Checked = false;
                CalculatorDamageCriticalDieCountTextBox.Enabled = false;
                CalculatorDamageCriticalDieCountTextBox.Text = string.Empty;
                CalculatorDamageCriticalDieCountTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalDieSizeTextBox.Enabled = false;
                CalculatorDamageCriticalDieSizeTextBox.Text = string.Empty;
                CalculatorDamageCriticalDieSizeTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalDieBonusTextBox.Enabled = false;
                CalculatorDamageCriticalDieBonusTextBox.Text = string.Empty;
                CalculatorDamageCriticalDieBonusTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                // Dot Damage
                CalculatorDamageBleedDieCheckBox.Enabled = false;
                CalculatorDamageBleedDieCheckBox.Checked = false;
                CalculatorDamageBleedDieCountTextBox.Enabled = false;
                CalculatorDamageBleedDieCountTextBox.Text = string.Empty;
                CalculatorDamageBleedDieCountTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageBleedDieSizeTextBox.Enabled = false;
                CalculatorDamageBleedDieSizeTextBox.Text = string.Empty;
                CalculatorDamageBleedDieSizeTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageBleedDieBonusTextBox.Enabled = false;
                CalculatorDamageBleedDieBonusTextBox.Text = string.Empty;
                CalculatorDamageBleedDieBonusTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                // Critical Dot Damage
                CalculatorDamageCriticalBleedDieCheckBox.Enabled = false;
                CalculatorDamageCriticalBleedDieCheckBox.Checked = false;
                CalculatorDamageCriticalBleedDieCountTextBox.Enabled = false;
                CalculatorDamageCriticalBleedDieCountTextBox.Text = string.Empty;
                CalculatorDamageCriticalBleedDieCountTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalBleedDieSizeTextBox.Enabled = false;
                CalculatorDamageCriticalBleedDieSizeTextBox.Text = string.Empty;
                CalculatorDamageCriticalBleedDieSizeTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
                CalculatorDamageCriticalBleedDieBonusTextBox.Enabled = false;
                CalculatorDamageCriticalBleedDieBonusTextBox.Text = string.Empty;
                CalculatorDamageCriticalBleedDieBonusTextBox.BackColor = PFKSettings.CurrentColorPallete.EntryDisabled;
            }
        }

        private void DefaultSettingsButton_Click(object sender, EventArgs e)
        {
            currEncounterSettings.ResetSettings();
            ReloadVisuals(currEncounterSettings);
        }

        // MOUSE
        //

        // MOUSE APPEARANCE
        private enum MouseAppearance
        {
            NormalHelpOff,
            NormalHelpOn,
            BatchHelpOff,
            BatchHelpOn,
            BatchClickOn,
        }
        private void UpdateMouse()
        {
            if (help_mode_enabled)
            {
                if (batch_mode_enabled)
                    if (hovering_entrybox) // To-do: Fix mouse not looking cool in batch mode
                        SetMouse(MouseAppearance.BatchClickOn);
                    else
                        SetMouse(MouseAppearance.BatchHelpOn);
                else
                    SetMouse(MouseAppearance.NormalHelpOn);
            }
            else
            {
                if (batch_mode_enabled)
                    // Disable Help Mode
                    SetMouse(MouseAppearance.BatchHelpOff);
                else
                    // Disable Regular Help Mode
                    SetMouse(MouseAppearance.NormalHelpOff);

            }
        }
        private void SetMouse(MouseAppearance mode)
        {
            switch (mode)
            {
                case MouseAppearance.NormalHelpOff:
                    Cursor = Cursors.Default;
                    break;
                case MouseAppearance.NormalHelpOn:
                    Cursor = Cursors.Help;
                    break;
                case MouseAppearance.BatchHelpOff:
                    Cursor = Cursors.Cross;
                    break;
                case MouseAppearance.BatchHelpOn:
                    Cursor = new(Assembly.GetExecutingAssembly().GetManifestResourceStream("Pickings_For_Kurtulmak.cross_question.cur"));
                    break;
                case MouseAppearance.BatchClickOn:
                    Cursor = new(Assembly.GetExecutingAssembly().GetManifestResourceStream("Pickings_For_Kurtulmak.cross_exclaim.cur"));
                    break;
            }
        }
        private void Control_MouseHoverShowTooltip(object sender, EventArgs e)
        {
            CalculatorHelpToolTip.SetToolTip(sender as Control, label_hashes_help[sender.GetHashCode()]);
        }

        // PROGRESS BAR
        public void SetProgressBar(object sender, int progress)
        {
            if (batch_mode_enabled)
            {
                CalculatorMiscStatisticsCalculateStatsProgressBars.Value = Math.Clamp(
                    value: (int)(CalculatorMiscStatisticsCalculateStatsProgressBars.Maximum
                    * computationStepsCurrent / computationStepsTotal) + 1,
                    min: 0,
                    max: CalculatorMiscStatisticsCalculateStatsProgressBars.Maximum);
                CalculatorMiscStatisticsCalculateStatsProgressBars.Value -= 1;
            }
            else
            {
                CalculatorMiscStatisticsCalculateStatsProgressBars.Value = Math.Clamp(progress + 1, 0, CalculatorMiscStatisticsCalculateStatsProgressBars.Maximum);
                CalculatorMiscStatisticsCalculateStatsProgressBars.Value = progress;
            }
        }

        // BATCH COMPUTATION
        // Toggle Batch Computation Mode
        private void CalculatorToggleBatchComputeButton_MouseClick(object sender, MouseEventArgs e)
        {
            if (batch_mode_enabled)
            { // Disable batch mode
                CalculatorBatchComputeButton.Text = "Enable Batch Mode";
                SetBatchMode(false);
                SetBatchGraphVisibility(false);

                foreach (var batchVar in batched_variables)
                {
                    SetBatchFieldVisibility(control: setting_to_control[batchVar.Key],
                                            show: false,
                                            setting: batchVar.Key,
                                            index: CalculatorDamageListBox.SelectedIndex);
                }
            }
            else
            { // Enable batch mode
                // Fix the scaling on the button & set the first text to be right
                CalculatorBatchComputeButton.Text = "Disable Batch Mode";
                SetBatchMode(true);
                SetBatchGraphVisibility(true);

                foreach (var batchVar in batched_variables)
                {
                    SetBatchFieldVisibility(control: setting_to_control[batchVar.Key],
                                            show: true,
                                            setting: batchVar.Key,
                                            index: CalculatorDamageListBox.SelectedIndex);
                }
            }
            UpdateMouse();
        }
        private void SetBatchMode(bool mode)
        {
            if (mode)
            { // Enable Batch Mode
                batch_mode_enabled = true;

                // Enable Warning Text
                CalculatorWarningLabel.Visible = true;
                CalculatorBCLGLabel.Visible = true;
                CalculatorEMELabel.Visible = true;
            }
            else
            { // Disable Batch Mode
                batch_mode_enabled = false;

                // Enable Warning Text
                CalculatorWarningLabel.Visible = false;
                CalculatorBCLGLabel.Visible = false;
                CalculatorEMELabel.Visible = false;

                // Reset Visible Batch Control
                HideBatchPopup();
            }
        }
        private void SetBatchGraphVisibility(bool state)
        {
            if (state)
            { // Show the batch graph
                CalculatorMiscStatisticsGroupBox.Visible = false;
                CalculatorEncounterStatisticsGroupBox.Visible = false;
                CalculatorDamageDistributionScottPlot.Visible = false;
                CalculatorBatchComputeScottPlot.Visible = true;
            }
            else
            { // Hide the batch graph
                CalculatorMiscStatisticsGroupBox.Visible = true;
                CalculatorEncounterStatisticsGroupBox.Visible = true;
                CalculatorDamageDistributionScottPlot.Visible = true;
                CalculatorBatchComputeScottPlot.Visible = false;
            }
        }

        // Batch Menu Buttons
        private void CalculatorBatchComputePopupXButton_Click(object sender, EventArgs e)
        { // Close/Discard the current batch settings
            SetControlAsBatched(control: batch_mode_selected,
                                batched: false,
                                index: CheckControlIsDamageDie(control_hash_to_setting[batch_mode_selected.GetHashCode()])
                                                    ? CalculatorDamageListBox.SelectedIndex
                                                    : -1);

            HideBatchPopup();
        }
        private void CalculatorBatchComputePopupSaveButton_Click(object sender, EventArgs e)
        { // Save the current batch settings
            try
            { // Try and store 
                CheckBatchParseSafety();

                BatchModeSettings settings = ReadBatchSettings();

                // Store the encouner setting to avoid recasting
                EncounterSetting encounterSetting = control_hash_to_setting[batch_mode_selected.GetHashCode()];
                // Check if currently on a list value
                int index = CheckControlIsDamageDie(encounterSetting)
                                    ? CalculatorDamageListBox.SelectedIndex
                                    : -1;

                // Check if an encounterSetting of this type has been batched yet
                if (batched_variables_last_value.TryGetValue(encounterSetting, out var last_value_dict))
                {
                    if (!last_value_dict.ContainsKey(index))
                    {
                        last_value_dict.Add(index, GetValueFromControl(batch_mode_selected));
                    }
                }
                else
                {
                    batched_variables_last_value.Add(encounterSetting, new() { { index, GetValueFromControl(batch_mode_selected) } });
                }

                // Update the control
                SetControlAsBatched(control: batch_mode_selected, batched: true, index: index, settings: settings);

                // Reset the window after saving
                HideBatchPopup();

                // Clear the no-batched-variables error
                ClearError(CalculatorBatchComputeButton);
            }
            catch (Exception ex)
            {
                PushBatchErrorMessages(ex);
            }
        }

        // Batch Interaction
        private void TextBox_MouseClickShowBatchComputation(object sender, EventArgs e)
        {
            // Store the control casted
            Control control = sender as Control;
            // Store the casted control to save excessive casting
            EncounterSetting encounterSetting = control_hash_to_setting[control.GetHashCode()];
            // Either store or default the selected index
            int index = CheckControlIsDamageDie(encounterSetting)
                                    ? CalculatorDamageListBox.SelectedIndex
                                    : -1;

            // Show the batch window at the new location
            if (batch_mode_enabled)
            {
                // Move the popup
                HideBatchPopup();
                ShowBatchPopup(control);

                // Check if this particular variable at this index has been batched before
                if (batched_variables.TryGetValue(encounterSetting, out var encounterSettingDict) && encounterSettingDict.ContainsKey(index))  // If this encounter setting has been batched
                { // Load the batch variable that's already set
                    // Load variables to the popup
                    LoadSettingsToBatchComputePopup(encounterSettingDict, index);
                }
            }
        }

        // Batch Data
        private BatchModeSettings ReadBatchSettings()
        {
            bool isDamageDie = setting_to_info.TryGetValue(control_hash_to_setting[batch_mode_selected.GetHashCode()], out var value);
            BatchModeSettings return_setting = new(encounterSetting: control_hash_to_setting[batch_mode_selected.GetHashCode()],
                                                    layer: (int)CalculatorBatchComputePopupLayerNumericUpDown.Value,
                                                    start: (int)CalculatorBatchComputePopupStartValueNumericUpDown.Value,
                                                    end: (int)CalculatorBatchComputePopupEndValueNumericUpDown.Value,
                                                    steps: GetIntListFromCommaString(CalculatorBatchComputePopupStepPatternTextBox.Text),
                                                    index: CheckControlIsDamageDie(control_hash_to_setting[batch_mode_selected.GetHashCode()])
                                                                ? CalculatorDamageListBox.SelectedIndex
                                                                : -1,
                                                    die_field: isDamageDie
                                                                ? value.Item1
                                                                : DieField.bonus,
                                                    critical: isDamageDie && value.Item3,
                                                    bleed: isDamageDie && value.Item2);

            return return_setting;
        }
        private bool CheckControlBatched(EncounterSetting encounterSetting)
        {
            return batched_variables.ContainsKey(encounterSetting);
        }
        private static List<int> GetIntListFromCommaString(string str)
        {
            return str.Split(',').Select(int.Parse).ToList();
        }

        // Show Batch Computation Menu
        private void LoadSettingsToBatchComputePopup(Dictionary<int, BatchModeSettings> settings, int index)
        { // Displays the given settings on the comptute popup
            CalculatorBatchComputePopupEndValueNumericUpDown.Value = settings[index].end;
            CalculatorBatchComputePopupStartValueNumericUpDown.Value = settings[index].start;
            CalculatorBatchComputePopupStepPatternTextBox.Text = string.Join(",", settings[index].steps.Select(x => x.ToString()));
            CalculatorBatchComputePopupLayerNumericUpDown.Value = settings[index].layer;
        }
        private void ShowBatchPopup(Control control)
        {
            // Update reference variable
            batch_mode_selected = control;

            // Position Panel next to the clicked TextBox
            CalculatorBatchComputePopupPanel.Location = MainTabControl.PointToClient(control.PointToScreen(new Point(0, 0)));
            CalculatorBatchComputePopupPanel.Location = new Point(x: CalculatorBatchComputePopupPanel.Location.X - 2,
                                                                  y: Math.Clamp(value: CalculatorBatchComputePopupPanel.Location.Y - 2, min: 0, max: 287));

            // Show Panel Regardless of wheter it was loaded or not
            CalculatorBatchComputePopupPanel.Visible = true;
            CalculatorBatchComputePopupPanel.BringToFront();
        }
        private void HideBatchPopup()
        { // Hide and reset the batch popup
            batch_mode_selected = null;
            CalculatorBatchComputePopupPanel.Visible = false;
            CalculatorBatchComputePopupStartValueNumericUpDown.Value = 0;
            CalculatorBatchComputePopupEndValueNumericUpDown.Value = 0;
            CalculatorBatchComputePopupStepPatternTextBox.Text = string.Empty;
            CalculatorBatchComputePopupLayerNumericUpDown.Value = 0;
        }
        private void SetControlAsBatched(Control control, bool batched, int index, BatchModeSettings settings = default)
        {
            EncounterSetting encounterSetting = control_hash_to_setting[batch_mode_selected.GetHashCode()];

            if (batched)
            { // Store the given encounterSetting

                TryRemoveBatchedVariable(setting: encounterSetting, index: index, removeLastValue: false);
                // Save the Settings
                if (batched_variables.TryGetValue(encounterSetting, out var batchDict))
                {
                    // Store the key if there are other values of this specific setting
                    batchDict.Add(key: index, value: settings);
                }
                else
                {
                    // Add a new entry as this value isn't batched already
                    batched_variables.Add(encounterSetting, new() { { index, settings } });
                }

                // Lock the Input on the Field
                SetFieldLookToBatched(control: control, locked: true, setting: encounterSetting, index: index);

                // Update Visuals of Batched Control
                batch_mode_selected.BackColor = PFKSettings.CurrentColorPallete.EntryBatched;
            }
            else
            { // Unbatch Variable

                // Unlock the Input on the Field
                SetFieldLookToBatched(control: control, locked: false, setting: encounterSetting, index: index);

                // Remove the encounterSetting batch settings
                TryRemoveBatchedVariable(setting: encounterSetting, index: index, removeLastValue: true);

                // Update visuals on Unbatched Control
                batch_mode_selected.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
            }
        }
        private void TryRemoveBatchedVariable(EncounterSetting setting, int index, bool removeLastValue = false)
        {
            // Remove the encounterSetting batch settings
            if (batched_variables.TryGetValue(setting, out var encounterDict))
            {
                encounterDict.Remove(index);
                if (encounterDict.Count == 0)
                { // Delete the encounterSetting dictionary if it was the last one
                    batched_variables.Remove(setting);
                }

                // Remove last_value if required
                if (removeLastValue)
                {
                    batched_variables_last_value[setting].Remove(index);
                    if (batched_variables_last_value[setting].Count == 0)
                    { // Delete the encounterSetting last value dictionary if it was the last one
                        batched_variables_last_value.Remove(setting);
                    }
                }
            }
        }
        private void SetBatchGraphSizeSmall(bool state)
        {
            if (state)
            { // SMALL
                // Adjust graph size to accomodate the intrusion
                // IDEAL LOCAT: (62%, 33.333%) % of page size
                CalculatorBatchComputeScottPlot.Location = new(x: (int)(CalculatorTabPage.Size.Width * 0.62),
                                                               y: (int)(CalculatorTabPage.Size.Height * 0.333));
                // IDEAL SIZES: (36.5%, 25.3%) % of page size
                CalculatorBatchComputeScottPlot.Size = new(width: (int)(CalculatorTabPage.Size.Width * 0.365),
                                                           height: (int)(CalculatorTabPage.Size.Height * 0.56));
            }
            else
            { // BIG
                // Adjust graph size to fill the gap
                // IDEAL LOCAT: (62%, 1.8%) % of page size
                CalculatorBatchComputeScottPlot.Location = new(x: (int)(CalculatorTabPage.Size.Width * 0.62),
                                                               y: (int)(CalculatorTabPage.Size.Height * 0.018));
                // IDEAL SIZES: (36.5%, 87.4%)  % of page size
                CalculatorBatchComputeScottPlot.Size = new(width: (int)(CalculatorTabPage.Size.Width * 0.365),
                                                           height: (int)(CalculatorTabPage.Size.Height * 0.874));
            }
        }
        private void SetBatchLayerViewControlVisibility(bool state)
        { // Show/Hide the Batch Layer View Control menu
            if (state)
            { // Show the graph layer selection menu

                // Show the layer view controls
                CalculatorBatchComputeLayerViewControlGroupBox.Visible = true;

                // REPOPULATE LAYER CONTROL MENU

                // Get each variable by layer
                Dictionary<int, List<BatchModeSettings>> layerGroupedBatchVariables = batched_variables.Values
                                .SelectMany(batchSettingsForEncounterSetting => batchSettingsForEncounterSetting.Values)
                                .GroupBy(batchSetting => batchSetting.layer) // Group settings by layer
                                .ToDictionary(sortedLayerGroup => sortedLayerGroup.Key, sortedLayerGroup => sortedLayerGroup.ToList()); // Create a dictionary by each layer

                // Highest/Fastest Iterated Layer
                int topLayer = layerGroupedBatchVariables.MaxBy(layerDict => layerDict.Key).Key;
                // Lowest/Slowest Iterated Layer
                int botLayer = layerGroupedBatchVariables.MinBy(layerDict => layerDict.Key).Key;

                // Iterate through each layer
                foreach (var layerGroup in layerGroupedBatchVariables.OrderBy(layer => layer.Key))
                { // Populate the controls menu with the layer's variables
                    int currentLayer = layerGroup.Key;
                    // Repopulate only if not on the fastest iterated layer
                    if (currentLayer != topLayer)
                    {
                        // FIRST LAYER LOGIC
                        if (currentLayer == botLayer)
                        { // Clear the old values from the layer selection and the value tracking window
                            CalculatorBatchComputeLayerViewControlLayerSelectListBox.Items.Clear();
                            layerViewControlEncounterSettings.Clear();
                        }

                        // LAYER LOGIC
                        // Add a new entry for when changing the edited layer at run-time
                        CalculatorBatchComputeLayerViewControlLayerSelectListBox.Items.Add("Layer " + currentLayer);
                        layerViewControlEncounterSettings.Add(currentLayer, layerGroup.Value);
                    }
                }

                // Set the default selections for the layer view control
                CalculatorBatchComputeLayerViewControlLayerSelectListBox.SelectedIndex = 0;
            }
            else
            { // Hide the graph layer selection menu
                // Hide the layer view controls
                CalculatorBatchComputeLayerViewControlGroupBox.Visible = false;
            }

        }
        private void SetBatchComputeLayerSelectionOnVariables(int selectedLayer)
        { // Repopulate the menu with the respective values
            // Repopulate the Variable Values
            CalculatorBatchComputeLayerViewControlValuesAtLayerListBox.Items.Clear();
            CalculatorBatchComputeLayerViewControlValuesAtLayerListBox
                .Items.AddRange(layerViewControlEncounterSettings.OrderBy(layer => layer.Key)
                                .ElementAt(selectedLayer).Value
                                .Select(batchSetting => setting_to_string[batchSetting.encounterSetting] // EncounterSetting
                                            + ((batchSetting.index == -1) ? string.Empty // Index (if applicable)
                                                                         : " (Index #" + (batchSetting.index + 1) + ")")
                                            + " : " + batchSetting.GetValueAtStep(batch_view_selected_steps[selectedLayer])) // Value
                    .ToArray());
        }
        private void SetBatchComputeLayerSelectionOnStep(int selectedLayer)
        {
            // Repopulate the Step Selection Options
            CalculatorBatchComputeLayerViewControlStepSelectComboBox.Items.Clear();
            CalculatorBatchComputeLayerViewControlStepSelectComboBox
                    .Items.AddRange(Enumerable.Range(0, layerViewControlEncounterSettings.OrderBy(layer => layer.Key)
                                    .ElementAt(selectedLayer).Value
                                    .Max(batchSetting => batchSetting.number_of_steps) + 1)
                                    .Select(stepValue => "Step #" + stepValue)
                                    .ToArray());
            // Default the selected index to the first
            CalculatorBatchComputeLayerViewControlStepSelectComboBox.SelectedIndex = 0;
        }
        private static bool CheckControlIsDamageDie(EncounterSetting setting)
        {
            return (int)setting >= 21;
        }

        // BATCH COMPUTATION ERROR CHECKING
        private void CheckBatchParseSafety()
        { // Check and throw any errors found
            Exception ex = new();

            // Empty Data Exceptions
            // Attack Category
            if (CalculatorBatchComputePopupStepPatternTextBox.TextLength == 0)
            {
                ex.Data.Add(531, "Missing Data Exception: Missing step pattern");
            }

            // Throw exception on problems
            if (ex.Data.Count > 0)
                throw ex;
        }
        private static void CheckBatchLogicSafety(BatchModeSettings setting)
        { // Check and throw any errors found
            Exception ex = new();

            // Empty Data Exceptions
            // Attack Category
            if (setting.end < setting.start && setting.step_direction > 0)
            {
                ex.Data.Add(551, "Bad Data Combination Exception: End is smaller than Start with non-negative Step Size");
            }
            if (setting.end > setting.start && setting.step_direction < 0)
            {
                ex.Data.Add(552, "Bad Data Combination Exception: End is larger than Start with negative Step Size");
            }
            if (setting.start == setting.end)
            {
                ex.Data.Add(553, "Bad Data Exception: Start and End values are the same");
            }
            if (setting.end != setting.start && setting.step_direction == 0)
            {
                ex.Data.Add(541, "Bad Data Exception: Net step size is zero");
            }

            // Throw exception on problems
            if (ex.Data.Count > 0)
                throw ex;
        }
        private void PushBatchErrorMessages(Exception ex)
        { // Push Error Codes to their respective boxes
            ClearBatchErrorMessages();
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 531: // Push
                        PushError(CalculatorBatchComputePopupStepPatternTextBox, "A step size is missing!");
                        break;
                    case 551: // Push
                        PushError(CalculatorBatchComputePopupStepPatternTextBox, "The End value cannot be smaller than the Start value with non-negative Step Size (impossible to reach)!");
                        break;
                    case 552: // Push
                        PushError(CalculatorBatchComputePopupStepPatternTextBox, "The End value cannot be larger the Start value with negative Step Size (impossible to reach)!");
                        break;
                    case 553: // Push
                        PushError(CalculatorBatchComputePopupStartValueNumericUpDown, "The Start value cannot be the same value as the End value", isNumericUpDown: true);
                        break;
                    case 541: // Push
                        PushError(CalculatorBatchComputePopupStepPatternTextBox, "The net step size cannot be zero (infinite steps)!");
                        break;
                }
            }
        }
        private void ClearBatchErrorMessages()
        {
            PushError(CalculatorBatchComputePopupStepPatternTextBox, "", replace: true);
        }

        // To-do: Go through and organize functions. This is a mess!

        // Batch Computation
        public static async Task<BatchResults> ComputeAverageDamage_BATCH(EncounterSettings encounter_settings,
                                                        SortedDictionary<int, List<Tuple<EncounterSetting, Dictionary<int, BatchModeSettings>>>> binned_compute_layers,
                                                        int[] maxSteps,
                                                        Dictionary<EncounterSetting, Dictionary<int, BatchModeSettings>> batched_variables,
                                                        IProgress<int> progress)
        {

            // Declare & Initialize the return value
            BatchResults batch_results = new();

            // COMPUTATION FLAGS

            // Set the flag for actively computing damage
            computing_damage = true;

            // Store the dimensions of the array
            batch_results.dimensions = maxSteps.ToList();

            // Set number of dimensions
            int dimensions = binned_compute_layers.Count;

            // Create an array to hold all the damage stats, sized by the most number of steps in each batch layer
            Array totalDamageStats = Array.CreateInstance(typeof(DamageStats), maxSteps);
            batch_results.tick_values = Array.CreateInstance(typeof(Dictionary<EncounterSetting, Dictionary<int, int>>), maxSteps);

            // Create an array to track current index such that
            // [X, Y, Z, ... END] represent the [LOWEST, SECOND LOWEST, THIRD LOWEST, ... HIGHEST] layers.
            int[] indices = new int[dimensions];

            // Calculate the total number of entries in the array
            int number_of_values = maxSteps.Aggregate((sum, next) => sum * next);
            computationStepsTotal = number_of_values;
            computationStepsCurrent = 1;

            // Use a dictionary to track the current value of each variable
            Dictionary<EncounterSetting, int> computeVariables = new()
            {
            // ENCOUNTER
                // Number of Encounters
                { EncounterSetting.number_of_encounters, encounter_settings.number_of_encounters },
                // Rounds Per Encounter
                { EncounterSetting.rounds_per_encounter, batched_variables.TryGetValue(EncounterSetting.rounds_per_encounter,
                                                                                        out var roundsPerEncounterSetting)
                                                            ? roundsPerEncounterSetting.First().Value.start
                                                            : encounter_settings.rounds_per_encounter },
                // Engagement Range
                { EncounterSetting.engagement_range, batched_variables.TryGetValue(EncounterSetting.engagement_range,
                                                                                        out var engagementRangeSetting)
                                                            ? engagementRangeSetting.First().Value.start
                                                            : encounter_settings.engagement_range },
            // ACTIONS
                    // Any
                { EncounterSetting.actions_per_round_any, batched_variables.TryGetValue(EncounterSetting.actions_per_round_any,
                                                                                        out var actionsPerRoundAnySetting)
                                                            ? actionsPerRoundAnySetting.First().Value.start
                                                            : encounter_settings.actions_per_round.any },
                    // Strike
                { EncounterSetting.actions_per_round_strike, batched_variables.TryGetValue(EncounterSetting.actions_per_round_strike,
                                                                                        out var actionsPerRoundStrikeSetting)
                                                            ? actionsPerRoundStrikeSetting.First().Value.start
                                                            : encounter_settings.actions_per_round.strike },
                    // Draw
                { EncounterSetting.actions_per_round_draw, batched_variables.TryGetValue(EncounterSetting.actions_per_round_draw,
                                                                                        out var actionsPerRoundDrawSetting)
                                                            ? actionsPerRoundDrawSetting.First().Value.start
                                                            : encounter_settings.actions_per_round.draw },
                    // Reload
                { EncounterSetting.actions_per_round_reload, batched_variables.TryGetValue(EncounterSetting.actions_per_round_reload,
                                                                                        out var actionsPerRoundReloadSetting)
                                                            ? actionsPerRoundReloadSetting.First().Value.start
                                                            : encounter_settings.actions_per_round.reload },
                    // Long Reload
                { EncounterSetting.actions_per_round_long_reload, batched_variables.TryGetValue(EncounterSetting.actions_per_round_long_reload,
                                                                                        out var actionsPerRoundLongReloadSetting)
                                                            ? actionsPerRoundLongReloadSetting.First().Value.start
                                                            : encounter_settings.actions_per_round.long_reload },
                    // Stride
                { EncounterSetting.actions_per_round_stride, batched_variables.TryGetValue(EncounterSetting.actions_per_round_stride,
                                                                                        out var actionsPerRoundStrideSetting)
                                                            ? actionsPerRoundStrideSetting.First().Value.start
                                                            : encounter_settings.actions_per_round.stride },
                    // Draw
                { EncounterSetting.draw, batched_variables.TryGetValue(EncounterSetting.draw,
                                                                                        out var drawSetting)
                                                            ? drawSetting.First().Value.start
                                                            : encounter_settings.draw },
            // RELOAD
                // Reload
                { EncounterSetting.reload, batched_variables.TryGetValue(EncounterSetting.reload,
                                                                                        out var reloadSetting)
                                                            ? reloadSetting.First().Value.start
                                                            : encounter_settings.reload },
                // Long Reload
                { EncounterSetting.long_reload, batched_variables.TryGetValue(EncounterSetting.long_reload,
                                                                                        out var longReloadSetting)
                                                            ? longReloadSetting.First().Value.start
                                                            : encounter_settings.long_reload },
                // Magazine Size
                { EncounterSetting.magazine_size, batched_variables.TryGetValue(EncounterSetting.magazine_size,
                                                                                        out var magazineSizeSetting)
                                                            ? magazineSizeSetting.First().Value.start
                                                            : encounter_settings.magazine_size },
            // ATTACK
                // Bonus To Hit
                { EncounterSetting.bonus_to_hit, batched_variables.TryGetValue(EncounterSetting.bonus_to_hit,
                                                                                        out var bonusToHitSetting)
                                                            ? bonusToHitSetting.First().Value.start
                                                            : encounter_settings.bonus_to_hit },
                // AC
                { EncounterSetting.AC, batched_variables.TryGetValue(EncounterSetting.AC,
                                                                                        out var ACSetting)
                                                            ? ACSetting.First().Value.start
                                                            : encounter_settings.AC },
                // Crit Threshhold
                { EncounterSetting.crit_threshhold, batched_variables.TryGetValue(EncounterSetting.crit_threshhold,
                                                                                        out var critThreshholdSetting)
                                                            ? critThreshholdSetting.First().Value.start
                                                            : encounter_settings.crit_threshhold },
                // MAP Modifier
                { EncounterSetting.MAP_modifier, batched_variables.TryGetValue(EncounterSetting.MAP_modifier,
                                                                                        out var MAPModifierSetting)
                                                            ? MAPModifierSetting.First().Value.start
                                                            : encounter_settings.MAP_modifier },
            // REACH
                // Range Increment
                { EncounterSetting.range, batched_variables.TryGetValue(EncounterSetting.range,
                                                                                        out var rangeSetting)
                                                            ? rangeSetting.First().Value.start
                                                            : encounter_settings.range },
                // Volley Increment
                { EncounterSetting.volley, batched_variables.TryGetValue(EncounterSetting.volley,
                                                                                        out var volleySetting)
                                                            ? volleySetting.First().Value.start
                                                            : encounter_settings.volley },
                // Move Speed
                { EncounterSetting.move_speed, batched_variables.TryGetValue(EncounterSetting.move_speed,
                                                                                        out var moveSpeedSetting)
                                                            ? moveSpeedSetting.First().Value.start
                                                            : encounter_settings.move_speed },
            };
            List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> computeVariablesDamageDice = new(encounter_settings.damage_dice);
            List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> computeVariablesDamageDiceDOT = new(encounter_settings.damage_dice_DOT);

            // BATCH COMPUTATION
            // Start a do-while loop to iterate through all elements of the array
            do
            {
                // Set initial damage die values
                // Iterate across each layer to check for/apply changes to the existing variables
                foreach (int layer_index in binned_compute_layers.Keys)
                {
                    // Iterate within each layer (through each "slice", representing a batched variable)
                    foreach (var slice in binned_compute_layers[layer_index])
                    {
                        // Iterate over each damage die
                        foreach (var settingDict in slice.Item2)
                        {
                            // Update the current iteration variable
                            if (settingDict.Value.index != -1)
                            { // Check if currently a damage die
                                List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> editedDamage = settingDict.Value.bleed
                                                    ? computeVariablesDamageDiceDOT
                                                    : computeVariablesDamageDice;

                                EditDamageDie(damage_dice: editedDamage,
                                                setting: settingDict.Value.die_field,
                                                value: settingDict.Value.GetValueAtStep(indices[binned_compute_layers.Keys.ToList().IndexOf(layer_index)]),
                                                index: settingDict.Value.index,
                                                edit_critical: settingDict.Value.critical);

                                if (!settingDict.Value.critical && binned_compute_layers.Values.Any(binnedComputeLayer => binnedComputeLayer.Any(encounterSettingDict => encounterSettingDict.Item2.ContainsKey(settingDict.Value.index))))
                                { // Also update the critical if critical isn't batched
                                    EditDamageDie(damage_dice: editedDamage,
                                                setting: settingDict.Value.die_field,
                                                value: settingDict.Value.GetValueAtStep(indices[binned_compute_layers.Keys.ToList().IndexOf(layer_index)]),
                                                index: settingDict.Value.index,
                                                edit_critical: true);
                                }
                            }
                        }
                    }
                }

                // Compute the Damage at the Current Iteration Step
                Task<DamageStats> computeDamage = Task.Run(() => CalculateAverageDamage(number_of_encounters: computeVariables[EncounterSetting.number_of_encounters],
                                                        rounds_per_encounter: computeVariables[EncounterSetting.rounds_per_encounter],
                                                        actions_per_round: new RoundActions(any: computeVariables[EncounterSetting.actions_per_round_any],
                                                                                strike: computeVariables[EncounterSetting.actions_per_round_strike],
                                                                                stride: computeVariables[EncounterSetting.actions_per_round_stride],
                                                                                draw: computeVariables[EncounterSetting.draw],
                                                                                reload: computeVariables[EncounterSetting.reload],
                                                                                long_reload: computeVariables[EncounterSetting.long_reload]),
                                                        reload_size: computeVariables[EncounterSetting.magazine_size],
                                                        reload: computeVariables[EncounterSetting.reload],
                                                        long_reload: computeVariables[EncounterSetting.long_reload],
                                                        draw: computeVariables[EncounterSetting.draw],
                                                        damage_dice: computeVariablesDamageDice,
                                                        bonus_to_hit: computeVariables[EncounterSetting.bonus_to_hit],
                                                        AC: computeVariables[EncounterSetting.AC],
                                                        crit_threshhold: computeVariables[EncounterSetting.crit_threshhold],
                                                        MAP_modifier: computeVariables[EncounterSetting.MAP_modifier],
                                                        engagement_range: computeVariables[EncounterSetting.engagement_range],
                                                        move_speed: computeVariables[EncounterSetting.move_speed],
                                                        seek_favorable_range: computeVariables[EncounterSetting.move_speed] > 0,
                                                        range: computeVariables[EncounterSetting.range],
                                                        volley: computeVariables[EncounterSetting.volley],
                                                        damage_dice_DOT: computeVariablesDamageDiceDOT,
                                                        progress: progress));

                // How to access the current element of the array using the GetValue method
                DamageStats currDamage = await computeDamage;

                // Track the highest damage encounter thus far
                batch_results.max_width = currDamage.highest_encounter_damage > batch_results.max_width
                                            ? currDamage.highest_encounter_damage
                                            : batch_results.max_width;

                // How to set the current element of the array using the SetValue method
                totalDamageStats.SetValue(value: currDamage, indices: indices);

                // Iterate across each layer to check for/apply changes to the existing variables
                foreach (int layer_index in binned_compute_layers.Keys)
                {
                    // Iterate within each layer (through each "slice", representing a batched variable)
                    foreach (var slice in binned_compute_layers[layer_index])
                    {
                        // To-do: Make 'gif' feature

                        foreach (var settingDict in slice.Item2)
                        {
                            int storedValue;
                            if (settingDict.Value.index != -1)
                            { // Store the current damageDie value
                                var readDamageList = settingDict.Value.bleed
                                                    ? computeVariablesDamageDiceDOT
                                                    : computeVariablesDamageDice;
                                var readDamageTuple = settingDict.Value.critical
                                                    ? readDamageList[settingDict.Value.index].Item2
                                                    : readDamageList[settingDict.Value.index].Item1;
                                storedValue = settingDict.Value.die_field switch
                                {
                                    DieField.count => readDamageTuple.Item1,
                                    DieField.size => readDamageTuple.Item2,
                                    DieField.bonus => readDamageTuple.Item3
                                };
                            }
                            else
                            { // Store the non-list variable
                                storedValue = computeVariables[slice.Item1];
                            }

                            // Store the value to the tick list
                            if (batch_results.tick_values.GetValue(indices: indices) is Dictionary<EncounterSetting, Dictionary<int, int>> tick_dictionary)
                            {
                                if (tick_dictionary.TryGetValue(slice.Item1, out var encounterSettingDict))
                                {
                                    encounterSettingDict.TryAdd(settingDict.Value.index, storedValue);
                                }
                                else
                                {
                                    tick_dictionary.Add(slice.Item1, new()
                                        {
                                            { settingDict.Value.index, storedValue }
                                        });
                                }
                            }
                            else
                            {
                                Dictionary<EncounterSetting, Dictionary<int, int>> new_tick_dictionary = new()
                                    {
                                        { slice.Item1, new() {
                                                                { settingDict.Value.index, storedValue }
                                                             } }
                                    };
                                batch_results.tick_values.SetValue(indices: indices, value: new_tick_dictionary);
                            }
                        }
                    }
                }

                // Increment the rightmost dimension (the last index), which will be the root iterator (highest layer)
                indices[dimensions - 1]++;

                // Cascade the current indices
                for (int dimension_index = dimensions - 1; dimension_index > 0; dimension_index--)
                {
                    // If the current dimension is larger than its limit, iterate the next index down the line
                    if (indices[dimension_index] >= totalDamageStats.GetLength(dimension_index)) // Check if the current index reached the back of the respective dimension
                    { // End of current dimension reached
                      // Cascade-revert all indices to the right of the current index, then iterate the next dimension
                        for (int cascade_index = dimension_index; cascade_index < dimensions; cascade_index++)
                        {
                            indices[dimension_index] = 0;
                        }

                        // Iterate the next higher dimension if not at the frontmost index
                        if (dimension_index != 0)
                            indices[dimension_index - 1]++;
                    }
                }

                // Iterate across each layer to check for/apply changes to the existing variables
                foreach (int layer_index in binned_compute_layers.Keys)
                {
                    // Iterate within each layer (through each "slice", representing a batched variable)
                    foreach (var slice in binned_compute_layers[layer_index])
                    {

                        // To-do: Make 'gif' feature

                        foreach (var settingDict in slice.Item2)
                        {
                            // Update the current iteration variable
                            if (settingDict.Value.index != -1)
                            { // Check if currently a damage die
                                List<Tuple<Tuple<int, int, int>, Tuple<int, int, int>>> editedDamage = settingDict.Value.bleed
                                                    ? computeVariablesDamageDiceDOT
                                                    : computeVariablesDamageDice;

                                EditDamageDie(damage_dice: editedDamage,
                                                setting: settingDict.Value.die_field,
                                                value: settingDict.Value.GetValueAtStep(indices[binned_compute_layers.Keys.ToList().IndexOf(layer_index)]),
                                                index: settingDict.Value.index,
                                                edit_critical: settingDict.Value.critical);
                            }
                            else
                            {
                                computeVariables[slice.Item1] = settingDict.Value.GetValueAtStep(indices[binned_compute_layers.Keys.ToList().IndexOf(layer_index)]);
                            }

                            if (indices[^1] == maxSteps[^1] - 1)
                            {
                                int storedValue;
                                // Get the current value for storing in the tick list
                                if (settingDict.Value.index != -1)
                                { // Store the current damageDie value
                                    var readDamageList = settingDict.Value.bleed
                                                        ? computeVariablesDamageDiceDOT
                                                        : computeVariablesDamageDice;
                                    var readDamageTuple = settingDict.Value.critical
                                                        ? readDamageList[settingDict.Value.index].Item2
                                                        : readDamageList[settingDict.Value.index].Item1;
                                    storedValue = settingDict.Value.die_field switch
                                    {
                                        DieField.count => readDamageTuple.Item1,
                                        DieField.size => readDamageTuple.Item2,
                                        DieField.bonus => readDamageTuple.Item3
                                    };
                                }// To-do: Fix simultaneous stepping of damage dice variables
                                else
                                { // Store the non-list variable
                                    storedValue = computeVariables[slice.Item1];
                                }

                                // Store the value to the tick list
                                if (batch_results.tick_values.GetValue(indices: indices) is Dictionary<EncounterSetting, Dictionary<int, int>> tick_dictionary)
                                {
                                    if (tick_dictionary.TryGetValue(slice.Item1, out var encounterSettingDict))
                                    {
                                        encounterSettingDict.TryAdd(settingDict.Value.index, storedValue);
                                    }
                                    else
                                    {
                                        tick_dictionary.Add(slice.Item1, new()
                                        {
                                            { settingDict.Value.index, storedValue }
                                        });
                                    }
                                }
                                else
                                {
                                    Dictionary<EncounterSetting, Dictionary<int, int>> new_tick_dictionary = new()
                                    {
                                        { slice.Item1, new() {
                                                                { settingDict.Value.index, storedValue }
                                                             } }
                                    };
                                    batch_results.tick_values.SetValue(indices: indices, value: new_tick_dictionary);
                                }
                            }
                        }
                    }
                }

                // Track progress thus far
                computationStepsCurrent++;
            } while (indices[0] < totalDamageStats.GetLength(0));

            // Reset the damage computation flag
            computing_damage = false;

            // Store the computed results
            batch_results.raw_data = totalDamageStats;

            return batch_results;
        }
        // Collects same-layered batch variables into a SortedDictionary
        public static SortedDictionary<int, List<Tuple<EncounterSetting, Dictionary<int, BatchModeSettings>>>> ComputeBinnedLayersForBatch(Dictionary<EncounterSetting, Dictionary<int, BatchModeSettings>> batched_vars)
        {
            // Collect all batched settings into layers
            SortedDictionary<int, List<Tuple<EncounterSetting, Dictionary<int, BatchModeSettings>>>> binned_compute_layers = new();
            foreach (var batched_setting in batched_vars)
            {
                foreach (var batched_var in batched_setting.Value)
                {
                    if (binned_compute_layers.TryGetValue(batched_var.Value.layer, out var bin))
                    { // Store the batched layer in the existing bin
                        if (bin.Any(settingDict => settingDict.Item1 == batched_setting.Key))
                        {
                            // This setting has been added already, just add to the index dictionary
                            bin.Where(settingDict => settingDict.Item1 == batched_setting.Key).First().Item2.Add(batched_var.Value.index, batched_var.Value);
                        }
                        else
                        {
                            // Create a new instance of this setting
                            bin.Add(item: new(batched_setting.Key, new() { { batched_var.Value.index, batched_var.Value } }));
                        }
                    }
                    else
                    { // Create a new bin for the given layer
                        binned_compute_layers.Add(batched_var.Value.layer, new() { new(batched_setting.Key, new() { { batched_var.Value.index, batched_var.Value } }) });
                    }
                }
            }
            return binned_compute_layers;
        }
        // Computes the dimensions array, i.e. the number of iteration steps necessary for each layer within a set of batch layers.
        public static int[] ComputeMaxStepsArrayForBatch(SortedDictionary<int, List<Tuple<EncounterSetting, Dictionary<int, BatchModeSettings>>>> binned_compute_layers)
        {
            // Get the size of each dimension
            return binned_compute_layers.Select(layer => layer.Value.Max(dict => dict.Item2.Max(setting => setting.Value.number_of_steps + 1)))
                                        .ToArray();
        }

        private void SetBatchFieldVisibility(EncounterSetting setting, bool show, Control control, int index)
        {
            if (show)
            {
                SetFieldLookToBatched(control, true, setting, index);

                // Update Visuals of Batched Control
                control.BackColor = PFKSettings.CurrentColorPallete.EntryBatched;
            }
            else
            { // Unbatch Variable
                SetFieldLookToBatched(control, false, setting, index);

                // Update visuals on Unbatched Control
                control.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
            }
        }
        private void SetFieldLookToBatched(Control control, bool locked, EncounterSetting setting, int index)
        {
            if (locked)
            {
                // Lock the Input on the Field
                if (control is TextBox textBox)
                {
                    textBox.ReadOnly = true;
                    textBox.Text = "BATCHED";
                }
                else if (control is NumericUpDown numericUpDown)
                {
                    numericUpDown.ReadOnly = true;
                    numericUpDown.Text = "BATCHED";
                }
            }
            else if (batched_variables.TryGetValue(setting, out var encounterSettingDict) && encounterSettingDict.ContainsKey(index))
            {
                // Unlock the Input on the Field
                if (control is TextBox textBox)
                {
                    textBox.ReadOnly = false;
                    textBox.Text = batched_variables_last_value[setting][index].ToString();
                }
                else if (control is NumericUpDown numericUpDown)
                {
                    numericUpDown.ReadOnly = false;
                    numericUpDown.Value = batched_variables_last_value[setting][index];
                }
            }
        }

        private void CalculatorDamageListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = CalculatorDamageListBox.SelectedIndex;
            if (index == -1)
                return;

            // Load damage die stats to the UI
            LoadDamageDice(index);

            // Adjust batched visuals if in batch mode
            if (batch_mode_enabled)
            {
                if (batched_variables.Count > 0)
                {
                    int selectedIndex = CalculatorDamageListBox.SelectedIndex;

                    // Unbatch each textbox item
                    batched_variables.ToList().ForEach(settingDict =>
                    {
                        settingDict.Value.ToList().ForEach(batchSetting =>
                        {
                            if (batchSetting.Value.index != -1 && batchSetting.Value.index != selectedIndex)
                            {
                                SetBatchFieldVisibility(setting: settingDict.Key, show: false, control: setting_to_control[settingDict.Key], index: batchSetting.Key);
                            }
                        });
                    });
                    // Rebatch each textbox item
                    batched_variables.ToList().ForEach(settingDict =>
                    {
                        settingDict.Value.ToList().ForEach(batchSetting =>
                        {
                            if (batchSetting.Value.index == selectedIndex)
                            {
                                SetBatchFieldVisibility(setting: settingDict.Key, show: true, control: setting_to_control[settingDict.Key], index: batchSetting.Key);
                            }
                        });
                    });
                }
            }

            // Reset the Damage UI Visuals
            SelectCheckBoxForToggle(CalculatorDamageBleedDieCheckBox);
            SelectCheckBoxForToggle(CalculatorDamageCriticalBleedDieCheckBox);
            SelectCheckBoxForToggle(CalculatorDamageCriticalDieCheckBox);
        }
        private void CalculatorBatchComputeLayerViewControlLayerSelectListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Populate each box with the selected value
            SetBatchComputeLayerSelectionOnVariables(CalculatorBatchComputeLayerViewControlLayerSelectListBox.SelectedIndex);
            SetBatchComputeLayerSelectionOnStep(CalculatorBatchComputeLayerViewControlLayerSelectListBox.SelectedIndex);
        }
        private void CalculatorBatchComputeLayerViewControlStepSelectComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            // Update the selected index
            batch_view_selected_steps[CalculatorBatchComputeLayerViewControlLayerSelectListBox.SelectedIndex] = CalculatorBatchComputeLayerViewControlStepSelectComboBox.SelectedIndex;

            // Update the variable values-at-current-step.
            SetBatchComputeLayerSelectionOnVariables(CalculatorBatchComputeLayerViewControlLayerSelectListBox.SelectedIndex);

            // Reload the graph with the newly selected variable results
            UpdateBatchGraph(damageStats_BATCH);

            // Visually Update The Graph
            CalculatorBatchComputeScottPlot.Refresh();
        }

        // FONT & COLOR MANAGEMENT
        //
        private void LoadColorPallete(PFKColorPalette pallete)
        {
            // Store the current pallete
            PFKSettings.CurrentColorPallete = new(pallete);
            ApplyCurrentColorPallete();
        }
        private void ApplyCurrentColorPallete()
        {
            Size sizeInitial = CalculatorActionActionsPerRoundLabel.Size;
            Size sizeScottPlotNormalInitial = CalculatorBatchComputeScottPlot.Size;
            Size sizeScottPlotBatchInitial = CalculatorDamageDistributionScottPlot.Size;

            Point locationScottPlotNormalInitial = CalculatorDamageDistributionScottPlot.Location;
            Point locationScottPlotBatchInitial = CalculatorBatchComputeScottPlot.Location;

            // Do TitleBar Styling
            this.Style.TitleBar.CloseButtonForeColor = PFKSettings.CurrentColorPallete.Text;
            this.Style.TitleBar.BackColor = PFKSettings.CurrentColorPallete.Background;


            this.Style.TitleBar.CloseButtonForeColor = PFKSettings.CurrentColorPallete.Text;
            this.Style.TitleBar.MinimizeButtonForeColor = PFKSettings.CurrentColorPallete.Text;

            this.Style.TitleBar.CloseButtonHoverBackColor = PFKSettings.CurrentColorPallete.ExitButtonHovered;
            this.Style.TitleBar.MinimizeButtonHoverBackColor = PFKSettings.CurrentColorPallete.MinimizeButtonHovered;

            this.Style.TitleBar.CloseButtonPressedBackColor = PFKSettings.CurrentColorPallete.ExitButtonPressed;
            this.Style.TitleBar.MinimizeButtonPressedBackColor = PFKSettings.CurrentColorPallete.MinimizeButtonPressed;

            this.Style.TitleBar.Font = new(family: new FontFamily(PFKSettings.CurrentColorPallete.FontName),
                                           emSize: this.Style.TitleBar.Font.Size,
                                           style: FontStyle.Bold,
                                           unit: this.Style.TitleBar.Font.Unit);

            this.Style.TitleBar.ForeColor = PFKSettings.CurrentColorPallete.Text;

            // Do every other sub-control's customizing
            int xHashCode = CalculatorBatchComputePopupXButton.GetHashCode();
            int oHashCode = CalculatorBatchComputePopupSaveButton.GetHashCode();
            int xColorHashCode = SettingsThemeColorPopupXButton.GetHashCode();
            int oColorHashCode = SettingsThemeColorPopupOButton.GetHashCode();

            // Iterate through every control, applying the respective color
            foreach (Control control in allWindowControls)
            {
                int controlHash = control.GetHashCode();
                if (controlHash != xHashCode
                 && controlHash != oHashCode
                 && controlHash != xColorHashCode
                 && controlHash != oColorHashCode)
                {
                    switch (control)
                    {
                        case Label:
                            control.BackColor = Color.Transparent;
                            break;
                        case CheckBox checkBox:
                            checkBox.BackColor = PFKSettings.CurrentColorPallete.CheckBoxUnticked;
                            break;
                        case Button button:
                            control.BackColor = PFKSettings.CurrentColorPallete.ButtonBackground;
                            button.FlatAppearance.BorderColor = Color.Black;
                            break;
                        case TextBox:
                        case NumericUpDown:
                            control.BackColor = PFKSettings.CurrentColorPallete.EntryNormal;
                            break;
                        case FormsPlot formsPlot:
                            formsPlot.Plot.Style(grid: PFKSettings.CurrentColorPallete.Border,
                                                    axisLabel: PFKSettings.CurrentColorPallete.Text,
                                                    dataBackground: PFKSettings.CurrentColorPallete.GraphBackground);

                            if (CalculatorDamageDistributionScottPlotBarPlot is not null)
                            {
                                CalculatorDamageDistributionScottPlotBarPlot.Color = PFKSettings.CurrentColorPallete.GraphBackground;
                                CalculatorDamageDistributionScottPlotBarPlot.FillColor = PFKSettings.CurrentColorPallete.GraphBars;
                            }
                            break;
                        default:
                            control.BackColor = PFKSettings.CurrentColorPallete.Background;
                            break;
                    }

                    switch (control)
                    {
                        case TabPageAdv tab:
                            tab.TabForeColor = PFKSettings.CurrentColorPallete.Text;
                            break;
                        case Button:
                            control.ForeColor = PFKSettings.CurrentColorPallete.ButtonText;
                            break;
                        case CheckBox checkBox:
                            checkBox.ForeColor = PFKSettings.CurrentColorPallete.CheckBoxCheck;
                            break;
                        case FormsPlot formsPlot:
                            // Style the X-Axis
                            formsPlot.Plot.XAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
                            formsPlot.Plot.XAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName,
                                                            fontSize: 16 + PFKSettings.CurrentColorPallete.FontSizeOverride);
                            formsPlot.Plot.XAxis.Color(PFKSettings.CurrentColorPallete.Text);

                            // Style the Y-Axis
                            formsPlot.Plot.YAxis.TickLabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
                            formsPlot.Plot.YAxis.LabelStyle(fontName: PFKSettings.CurrentColorPallete.FontName);
                            formsPlot.Plot.YAxis.Color(PFKSettings.CurrentColorPallete.Text);

                            // Style the legend
                            if (CalculatorDamageDistributionScottPlotLegend is not null)
                            {
                                CalculatorDamageDistributionScottPlotLegend.FillColor = PFKSettings.CurrentColorPallete.Background;
                                CalculatorDamageDistributionScottPlotLegend.FontColor = PFKSettings.CurrentColorPallete.Text;
                            }

                            // Style the Heatmap Colorbar
                            if (CalculatorBatchComputeScottPlotColorbar is not null)
                            {
                                CalculatorBatchComputeScottPlotColorbar.TickMarkColor = PFKSettings.CurrentColorPallete.Text;
                                CalculatorBatchComputeScottPlotColorbar.TickLabelFont.Color = PFKSettings.CurrentColorPallete.Text;
                            }
                            break;
                        default:
                            control.ForeColor = PFKSettings.CurrentColorPallete.Text;
                            break;
                    }
                    control.Font = new(familyName: PFKSettings.CurrentColorPallete.FontName,
                                       emSize: 9 + PFKSettings.CurrentColorPallete.FontSizeOverride,
                                       style: control.Font.Style,
                                       unit: control.Font.Unit);
                }
            }

            float sizeChange = (float)CalculatorActionActionsPerRoundLabel.Size.Width / sizeInitial.Width;
            ScaleForm(sizeChange);

            // Shift tickboxes to match their labels and textboxes checkbox.location = (textbox.X, label.Y + 2) 
            CalculatorAmmunitionMagazineSizeCheckBox.Location =
                new(x: (CalculatorAmmunitionMagazineSizeLabel.Location.X
                        + CalculatorAmmunitionMagazineSizeTextBox.Location.X
                        + CalculatorAmmunitionMagazineSizeCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorAmmunitionMagazineSizeLabel.Location.Y
                        + CalculatorAmmunitionMagazineSizeTextBox.Location.Y
                        + CalculatorAmmunitionMagazineSizeCheckBox.Size.Height) / 2 - 12);

            CalculatorDamageBleedDieCheckBox.Location =
                new(x: (CalculatorDamageBleedDieCountTextBox.Location.X
                        + CalculatorDamageBleedDieLabel.Location.X
                        + CalculatorDamageBleedDieCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorDamageBleedDieCountTextBox.Location.Y
                        + CalculatorDamageBleedDieLabel.Location.Y
                        + CalculatorDamageBleedDieCheckBox.Size.Height) / 2 - 12);

            CalculatorDamageCriticalBleedDieCheckBox.Location =
                new(x: (CalculatorDamageCriticalBleedDieCountTextBox.Location.X
                        + CalculatorDamageCriticalBleedDieLabel.Location.X
                        + CalculatorDamageCriticalBleedDieCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorDamageCriticalBleedDieCountTextBox.Location.Y
                        + CalculatorDamageCriticalBleedDieLabel.Location.Y
                        + CalculatorDamageCriticalBleedDieCheckBox.Size.Height) / 2 - 12);

            CalculatorDamageCriticalDieCheckBox.Location =
                new(x: (CalculatorDamageCriticalDieCountTextBox.Location.X
                        + CalculatorDamageCriticalDieLabel.Location.X
                        + CalculatorDamageCriticalDieCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorDamageCriticalDieCountTextBox.Location.Y
                        + CalculatorDamageCriticalDieLabel.Location.Y
                        + CalculatorDamageCriticalDieCheckBox.Size.Width) / 2 - 12);

            CalculatorEncounterEngagementRangeCheckBox.Location =
                new(x: (CalculatorEncounterEngagementRangeTextBox.Location.X
                        + CalculatorEncounterEngagementRangeLabel.Location.X
                        + CalculatorEncounterEngagementRangeCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorEncounterEngagementRangeTextBox.Location.Y
                        + CalculatorEncounterEngagementRangeLabel.Location.Y
                        + CalculatorEncounterEngagementRangeCheckBox.Size.Height) / 2 - 12);

            CalculatorReachMovementSpeedCheckBox.Location =
                new(x: (CalculatorReachMovementSpeedTextBox.Location.X
                        + CalculatorReachMovementSpeedLabel.Location.X
                        + CalculatorReachMovementSpeedCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorReachMovementSpeedTextBox.Location.Y
                        + CalculatorReachMovementSpeedLabel.Location.Y
                        + CalculatorReachMovementSpeedCheckBox.Size.Height) / 2 - 12);

            CalculatorReachRangeIncrementCheckBox.Location =
                new(x: (CalculatorReachRangeIncrementTextBox.Location.X
                        + CalculatorReachRangeIncrementLabel.Location.X
                        + CalculatorReachRangeIncrementCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorReachRangeIncrementTextBox.Location.Y
                        + CalculatorReachRangeIncrementLabel.Location.Y
                        + CalculatorReachRangeIncrementCheckBox.Size.Height) / 2 - 12);

            CalculatorReachVolleyIncrementCheckBox.Location =
                new(x: (CalculatorReachVolleyIncrementTextBox.Location.X
                        + CalculatorReachVolleyIncrementLabel.Location.X
                        + CalculatorReachVolleyIncrementCheckBox.Size.Width) / 2 - 12,
                    y: (CalculatorReachVolleyIncrementTextBox.Location.Y
                        + CalculatorReachVolleyIncrementLabel.Location.Y
                        + CalculatorReachVolleyIncrementCheckBox.Size.Height) / 2 - 12);

            SettingsThemeMockupWeaponTraitCheckbox.Location =
                new(x: (SettingsThemeMockupWeaponTraitTextBox.Location.X
                        + SettingsThemeMockupWeaponTraitLabel.Location.X
                        + SettingsThemeMockupWeaponTraitCheckbox.Size.Width) / 2 - 12,
                    y: (SettingsThemeMockupWeaponTraitTextBox.Location.Y
                        + SettingsThemeMockupWeaponTraitLabel.Location.Y
                        + SettingsThemeMockupWeaponTraitCheckbox.Size.Height) / 2 - 12);

            ReloadVisuals(currEncounterSettings);
        }
        private void ScaleForm(float scaleRatio)
        {
            // Get the current bounds of the form
            Rectangle currentBounds = Bounds;

            // Calculate the new bounds of the form based on the scale ratio
            Rectangle newBounds = new(
                currentBounds.X,
                currentBounds.Y,
                (int)(currentBounds.Width * scaleRatio),
                (int)(currentBounds.Height * scaleRatio)
            );

            // Scale the form and all controls inside
            this.Scale(new SizeF(scaleRatio, scaleRatio));

            // Set the new size and position of the form
            Bounds = newBounds;

            // Rescale Graphs
            RescaleGraph();
            CalculatorBatchComputeScottPlot.Refresh();
            CalculatorDamageDistributionScottPlot.Refresh();
        }
        private void RescaleGraph()
        {
            if (isBatchGraphSmall)
            { // SMALL
                // Adjust graph size to accomodate the intrusion
                // IDEAL LOCAT: (62%, 33.333%) % of page size
                CalculatorBatchComputeScottPlot.Location = new(x: (int)(CalculatorTabPage.Size.Width * 0.62),
                                                               y: (int)(CalculatorTabPage.Size.Height * 0.333));
                // IDEAL SIZES: (36.5%, 25.3%) % of page size
                CalculatorBatchComputeScottPlot.Size = new(width: (int)(CalculatorTabPage.Size.Width * 0.365),
                                                           height: (int)(CalculatorTabPage.Size.Height * 0.56));
            }
            else
            { // BIG
                // Adjust graph size to fill the gap
                // IDEAL LOCAT: (62%, 1.8%) % of page size
                CalculatorBatchComputeScottPlot.Location = new(x: (int)(CalculatorTabPage.Size.Width * 0.62),
                                                               y: (int)(CalculatorTabPage.Size.Height * 0.018));
                // IDEAL SIZES: (36.5%, 87.4%)  % of page size
                CalculatorBatchComputeScottPlot.Size = new(width: (int)(CalculatorTabPage.Size.Width * 0.365),
                                                           height: (int)(CalculatorTabPage.Size.Height * 0.874));
            }

            CalculatorBatchComputeScottPlot.Location = new(x: (int)(CalculatorTabPage.Size.Width * 0.62),
                                                           y: (int)(CalculatorTabPage.Size.Height * 0.49));
            CalculatorDamageDistributionScottPlot.Size = new(width: (int)(CalculatorTabPage.Size.Width * 0.365),
                                                             height: (int)(CalculatorTabPage.Size.Height * 0.4));
        }

        // SERIALIZATION
        //
        public bool ReadSettings(string filename)
        { // Read the current settings
            try
            {
                string settingsJsonString = File.ReadAllText(filename);
                var jsonOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    Converters = {
                        new ColorJsonConverter()
                    }
                };
                PFKSettings = JsonSerializer.Deserialize<PFKSettings>(settingsJsonString, jsonOptions)!;
                return true;
            }
            catch
            {
                return false;
            }
        }
        public void WriteSettings()
        { // Store the current settings
            string settingsFilename = "settings.json";

            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                Converters = {
                    new ColorJsonConverter()
                }
            };

            string settingsJsonString = JsonSerializer.Serialize(value: PFKSettings,
                                                                options: jsonOptions);
            File.WriteAllText(path: settingsFilename, contents: settingsJsonString);
        }

        public bool ReadColorPalettes()
        {
            // Get the themes folder
            string themesFolder = Path.GetDirectoryName(Environment.ProcessPath) + @"\Themes\";

            Directory.CreateDirectory(themesFolder);

            try
            {
                // Get all files in the themes folder filtered by .json only
                string[] rawJsons = Directory.GetFiles(themesFolder, "*.json");

                if (rawJsons.Length == 0)
                { // No files found, discarding this read
                    return false;
                }

                foreach (string rawJson in rawJsons)
                { // Read each color pallete
                    ReadColorPalette(rawJson);
                }

                if (colorPalettes.Count == 0 || colorPalettes.Any(colorPalette =>
                { // Check if any palettes read aren't the default colors
                    return !colorPalette.Key.Equals("pf2e")
                        && !colorPalette.Key.Equals("light")
                        && !colorPalette.Key.Equals("dark");
                }))
                { // No palettes valid, discarding this read and returning false
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        public bool ReadColorPalette(string path)
        {
            try
            { // Try to read the current theme
                // Store the .json to a string
                string currentReadTheme = File.ReadAllText(path);

                var jsonOptions = new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    Converters = {
                        new ColorJsonConverter()
                    }
                };

                // Deserialize the string to an object
                PFKColorPalette readPalette = JsonSerializer.Deserialize<PFKColorPalette>(json: currentReadTheme,
                                                                                            options: jsonOptions)!;

                // Replace any pre-existing 
                colorPalettes.Remove(readPalette.PaletteName);
                colorPalettes.Add(readPalette.PaletteName, readPalette);
                return true;
            }
            catch
            { // Discard this theme if there's an error
                return false;
            }
        }
        public static void WriteColorPalette(PFKColorPalette palette, string paletteName)
        {
            PFKColorPalette savedPalette = new(palette)
            {
                PaletteName = paletteName
            };

            string themesFolder = Path.GetDirectoryName(Environment.ProcessPath) + @"\Themes\";

            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                Converters = {
                    new ColorJsonConverter()
                }
            };

            string settingsJsonString = JsonSerializer.Serialize(value: savedPalette,
                                                                options: jsonOptions);

            File.WriteAllText(path: themesFolder + paletteName + ".json", contents: settingsJsonString);
        }
        public void EraseColorPalette(string palette)
        {
            File.Delete(path: Environment.CurrentDirectory + @"\Themes\" + palette + ".json");
        }


        // THEME CONTROL FUNCTIONS
        //
        private void SettingsThemeColorPopupXButton_MouseClick(object sender, MouseEventArgs e)
        {
            SettingsThemeColorPopupFirstColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupSecondColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupThirdColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupFourthColorPicker.SelectedColor = Color.Empty;
            HideColorPickerWindow();
        }
        private void SettingsThemeColorPopupOButton_MouseClick(object sender, MouseEventArgs e)
        {
            // Save the selected color settings to the respective settings
            switch (themePickedControl)
            {
                case ColorPickedControl.Button:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.ButtonBackground = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    // Second Color
                    if (SettingsThemeColorPopupSecondColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.ButtonText = SettingsThemeColorPopupSecondColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.ListBox:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.Background = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.NumericUpDown:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.EntryNormal = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.Label:
                    // Font Family
                    if (SettingsThemeColorPopupFirstFontComboBox.SelectedItem is not null)
                    {
                        string selectedFont = SettingsThemeColorPopupFirstFontComboBox.SelectedItem.ToString();
                        if (new InstalledFontCollection().Families.Any(font => font.Name.Equals(selectedFont)))
                            PFKSettings.CurrentColorPallete.FontName = selectedFont;
                    }
                    // Font Size Mod
                    if (SettingsThemeColorPopupThirdNumericUpDown.Value != PFKSettings.CurrentColorPallete.FontSizeOverride)
                        PFKSettings.CurrentColorPallete.FontSizeOverride = (int)SettingsThemeColorPopupThirdNumericUpDown.Value;
                    // Font Color
                    if (SettingsThemeColorPopupFourthColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.Text = SettingsThemeColorPopupFourthColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.TextBox:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.EntryNormal = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    // Second Color
                    if (SettingsThemeColorPopupSecondColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.EntryBatched = SettingsThemeColorPopupSecondColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.CheckBox:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.CheckBoxUnticked = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    // Second Color
                    if (SettingsThemeColorPopupSecondColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.CheckboxTicked = SettingsThemeColorPopupSecondColorPicker.SelectedColor;
                    // Third Color
                    if (SettingsThemeColorPopupThirdColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.CheckBoxPressed = SettingsThemeColorPopupThirdColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.FormPlot:
                    // First Color
                    // To-do: Maybe custom graph color support for heatmap????
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.GraphBars = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    if (SettingsThemeColorPopupSecondColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.GraphBackground = SettingsThemeColorPopupSecondColorPicker.SelectedColor;
                    break;
                case ColorPickedControl.TabbedPage:
                    // First Color
                    if (SettingsThemeColorPopupFirstColorPicker.SelectedColor != Color.Empty)
                        PFKSettings.CurrentColorPallete.Background = SettingsThemeColorPopupFirstColorPicker.SelectedColor;
                    break;
            }

            ApplyCurrentColorPallete();
            HideColorPickerWindow();
        }
        private void SettingsThemeNameAddButton_MouseClick(object sender, MouseEventArgs e)
        {
            string newColorPaletteName = SettingsThemeNameTextbox.Text;

            try
            {// Create a new default palette with the given name
                CheckAddColorPaletteSaftey();
                AddColorPelette(newColorPaletteName);
                UpdateSettingsThemesListbox();
            }
            catch (Exception ex)
            {
                PushCreateNewThemeErrors(ex);
            }
        }
        private void SettingsColorMockupClicked(object sender, MouseEventArgs e)
        {
            Control control = sender as Control;

            HideColorPickerWindow();
            ShowColorPickerWindow(control);
        }
        private void SettingsThemeNameListbox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ClearError(sender as Control);


        }
        private void SettingsThemeColorPopupColorPicker_ColorSelected(object sender, EventArgs e)
        {
            ColorPickerButton control = sender as ColorPickerButton;

            control.BackColor = control.SelectedColor;
            control.ForeColor = HelperFunctions.GetContrastingBAWColor(control.SelectedColor);
        }
        private void SettingsThemeNameLoadButton_MouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                CheckLoadColorPaletteSafety();
                if (SettingsThemeNameListbox.SelectedIndex != -1
                    && colorPalettes.TryGetValue(SettingsThemeNameListbox.Text, out var colorPalette))
                {
                    LoadColorPallete(colorPalette);
                }
            }
            catch (Exception ex)
            {
                PushLoadColorPaletteWindowErrors(ex);
            }
        }
        private void SettingsThemeNameDeleteButton_MouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                CheckDeleteColorPaletteSafety();
                DeleteColorPalette(PFKSettings.CurrentColorPallete.PaletteName);
            }
            catch (Exception ex)
            {
                PushDeleteThemeErrors(ex);
            }
        }
        private void SettingsThemeNameSaveButton_MouseClick(object sender, MouseEventArgs e)
        {
            try
            {
                CheckSaveColorPaletteSafety();
                colorPalettes.Remove(SettingsThemeNameListbox.SelectedItem.ToString());
                colorPalettes.Add(SettingsThemeNameListbox.SelectedItem.ToString(), PFKSettings.CurrentColorPallete);
                WriteColorPalette(PFKSettings.CurrentColorPallete, SettingsThemeNameListbox.SelectedItem.ToString());
            }
            catch (Exception ex)
            {
                PushSaveThemeErrors(ex);
            }
        }
        private void SettingsThemeMockupScottPlot_LeftClick(object sender, EventArgs e)
        {
            SettingsColorMockupClicked(sender: sender, null);
        }

        /// SETTINGS MENU
        /// 
        private void UpdateSettingsThemesListbox()
        {
            // Clear the list
            SettingsThemeNameListbox.Items.Clear();

            foreach (var theme in colorPalettes.OrderBy(paletteKVP => paletteKVP.Key))
            { // Populate the listbox with each color palette
                SettingsThemeNameListbox.Items.Add(theme.Key);
            }
        }
        private void DeleteColorPalette(string paletteName)
        {
            // Delete the palette file
            colorPalettes.Remove(paletteName);

            // Update the listbox
            UpdateSettingsThemesListbox();

            // Erase the palette file
            EraseColorPalette(paletteName);
        }
        private void AddColorPelette(string paletteName)
        {
            // Adds a new default "light" theme to the list with the given name
            colorPalettes.Add(paletteName, PFKColorPalette.GetDefaultPalette(type: "light", name: paletteName));
        }

        // ERROR CHECKING
        //
        private void ClearColorPickerErrors()
        {
            PushError(SettingsThemeNameListbox, "", replace: true);
        }
        private void CheckLoadColorPaletteSafety()
        {
            Exception loadPaletteException = new();

            // Throw an error if the popup can't be shown
            if (SettingsThemeNameListbox.SelectedIndex == -1)
            {
                loadPaletteException.Data.Add(1503, "Missing Data Exception: No theme selected");
            }

            if (loadPaletteException.Data.Count > 0)
            {
                throw loadPaletteException;
            }
        }
        private void CheckAddColorPaletteSaftey()
        {
            Exception addPaletteException = new();

            string themeName = SettingsThemeNameTextbox.Text;

            if (colorPalettes.ContainsKey(themeName))
            { // Throw an error if the given palette name exists already
                addPaletteException.Data.Add(1501, "Duplicate Data Exception: Duplicate color palette name");
            }
            if (string.IsNullOrEmpty(themeName))
            { // Throw an error if the given palette name is not valid
                addPaletteException.Data.Add(1502, "Missing Data Exception: No palette name provided");
            }

            if (addPaletteException.Data.Count > 0)
            {
                throw addPaletteException;
            }
        }
        private void CheckDeleteColorPaletteSafety()
        {
            Exception deletePaletteException = new();

            if (SettingsThemeNameListbox.SelectedIndex == -1)
            { // Check if no theme is selected
                deletePaletteException.Data.Add(1503, "Missing Data Exception: No theme selected!");
            }
            else
            { // Check if the selected theme is
                string selectedPalette = SettingsThemeNameListbox.SelectedItem.ToString();
                if (!colorPalettes.ContainsKey(selectedPalette)
                 && !File.Exists(Path.GetDirectoryName(Environment.ProcessPath) + selectedPalette + ".json"))
                {
                    deletePaletteException.Data.Add(1552, "Missing Data Exception: To-be-deleted theme not found!");
                }
            }

            if (deletePaletteException.Data.Count > 0)
            {
                throw deletePaletteException;
            }
        }
        private void CheckSaveColorPaletteSafety()
        {
            Exception savePaletteException = new();

            if (SettingsThemeNameListbox.SelectedIndex == -1)
            { // Check if no theme is selected
                savePaletteException.Data.Add(1503, "Missing Data Exception: No theme selected!");
            }

            if (savePaletteException.Data.Count > 0)
            {
                throw savePaletteException;
            }
        }
        private void PushCreateNewThemeErrors(Exception ex)
        {
            ClearColorPickerErrors();
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 1501: // Push
                        PushError(SettingsThemeNameListbox, "Duplicate color palette name!");
                        break;
                    case 1502:
                        PushError(SettingsThemeNameListbox, "No palette name provided!");
                        break;
                }
            }
        }
        private void PushLoadColorPaletteWindowErrors(Exception ex)
        {
            // To-do: Add an emergency key to reset fontSizeOverride & fontName of the current theme to 0 & segue ui
            ClearColorPickerErrors();
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 1503: // Push
                        PushError(SettingsThemeNameListbox, "No theme selected!");
                        break;
                }
            }
        }
        private void PushDeleteThemeErrors(Exception ex)
        {
            ClearColorPickerErrors();
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 1503: // Push
                        PushError(SettingsThemeNameListbox, "No theme selected!");
                        break;
                    case 1552: // Push
                        PushError(SettingsThemeNameListbox, "To-be-deleted theme not found!");
                        break;
                }
            }
        }
        private void PushSaveThemeErrors(Exception ex)
        {
            ClearColorPickerErrors();
            foreach (int errorCode in ex.Data.Keys)
            {
                switch (errorCode)
                {
                    case 1503: // Push
                        PushError(SettingsThemeNameListbox, "No theme selected!");
                        break;
                }
            }
        }

        // COLOR PICKER POPUP
        //
        public enum ColorPickedControl
        {
            Button,
            ListBox,
            NumericUpDown,
            Label,
            TextBox,
            CheckBox,
            FormPlot,
            TabbedPage
        }
        public ColorPickedControl themePickedControl;
        public Dictionary<string, PFKColorPalette> colorPalettes = new();

        private void ShowColorPickerWindow(Control control)
        { // Move and show the color picker popup to the given control
            // Move the popup
            SettingsThemeColorPopupGroupbox.Location = MainTabControl.PointToClient(control.PointToScreen(new Point(0, 0)));

            // Configure the popup
            ConfigureColorPickerWindow(control);

            // Show the popup
            SettingsThemeColorPopupGroupbox.Visible = true;
        }
        private void HideColorPickerWindow()
        {
            SettingsThemeColorPopupFirstColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupSecondColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupThirdColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupFourthColorPicker.SelectedColor = Color.Empty;
            SettingsThemeColorPopupGroupbox.Visible = false;
        }
        public void ConfigureColorPickerWindow(Control control)
        {
            ColorPickedControl pickedControl = control switch
            {
                Button => ColorPickedControl.Button,
                ListBox => ColorPickedControl.ListBox,
                NumericUpDown => ColorPickedControl.NumericUpDown,
                Label => ColorPickedControl.Label,
                TextBox => ColorPickedControl.TextBox,
                CheckBox => ColorPickedControl.CheckBox,
                FormsPlot => ColorPickedControl.FormPlot,
                TabControlAdv => ColorPickedControl.TabbedPage,
            };

            themePickedControl = pickedControl;
            switch (themePickedControl)
            {
                case ColorPickedControl.Button: // BUTTON RELATED COLORS
                    ReconfigureColorPopup(firstLabel: "Button BG", firstColor: PFKSettings.CurrentColorPallete.ButtonBackground,
                                          secondLabel: "Button Text", secondColor: PFKSettings.CurrentColorPallete.ButtonText);
                    break;
                case ColorPickedControl.ListBox:
                    ReconfigureColorPopup(firstLabel: "Window BG", firstColor: PFKSettings.CurrentColorPallete.Background);
                    break;
                case ColorPickedControl.NumericUpDown:
                    ReconfigureColorPopup(firstLabel: "Normal BG", firstColor: PFKSettings.CurrentColorPallete.EntryNormal);
                    break;
                case ColorPickedControl.Label:
                    ReconfigureColorPopup(firstLabel: "Font Name", selectedFont: PFKSettings.CurrentColorPallete.FontName,
                                          thirdLabel: "Font Size Increase",
                                          fourthLabel: "Font Color", fourthColor: PFKSettings.CurrentColorPallete.Text,
                                          fontOverrideSize: PFKSettings.CurrentColorPallete.FontSizeOverride);
                    break;
                case ColorPickedControl.TextBox:
                    ReconfigureColorPopup(firstLabel: "Normal BG", firstColor: PFKSettings.CurrentColorPallete.EntryNormal,
                                          secondLabel: "Batched BG", secondColor: PFKSettings.CurrentColorPallete.EntryBatched);
                    break;
                case ColorPickedControl.CheckBox:
                    ReconfigureColorPopup(firstLabel: "Unticked Color", firstColor: PFKSettings.CurrentColorPallete.CheckBoxUnticked,
                                          secondLabel: "Ticked Color", secondColor: PFKSettings.CurrentColorPallete.CheckboxTicked,
                                          thirdLabel: "Pressed Color", thirdColor: PFKSettings.CurrentColorPallete.CheckBoxPressed);
                    break;
                case ColorPickedControl.FormPlot:
                    ReconfigureColorPopup(firstLabel: "Bar Color", firstColor: PFKSettings.CurrentColorPallete.GraphBars,
                                          secondLabel: "Graph BG", secondColor: PFKSettings.CurrentColorPallete.GraphBackground);
                    break;
                case ColorPickedControl.TabbedPage:
                    ReconfigureColorPopup(firstLabel: "Window BG", firstColor: PFKSettings.CurrentColorPallete.Background);
                    break;
            }
        }
        private void ReconfigureColorPopup(string? firstLabel = null, Color? firstColor = null, string? selectedFont = null,
                                         string? secondLabel = null, Color? secondColor = null,
                                         string? thirdLabel = null, Color? thirdColor = null,
                                         string? fourthLabel = null, Color? fourthColor = null, int? fontOverrideSize = null)
        {
            bool firstLabelVisible = firstLabel is not null;
            SettingsThemeColorPopupFirstLabel.Visible = firstLabelVisible;
            SettingsThemeColorPopupFirstLabel.Text = firstLabelVisible ? firstLabel : string.Empty;
            SettingsThemeColorPopupFirstColorPicker.Visible = firstLabelVisible && selectedFont is null;
            SettingsThemeColorPopupFirstColorPicker.BackColor = (firstColor != null) ? (Color)firstColor : Color.Pink;
            SettingsThemeColorPopupFirstColorPicker.ForeColor = (firstColor != null) ? HelperFunctions.GetContrastingBAWColor((Color)firstColor) : Color.Pink;


            // Populate the Font Dropdown
            SettingsThemeColorPopupFirstFontComboBox.Items.Clear();
            SettingsThemeColorPopupFirstFontComboBox.Items.AddRange(new InstalledFontCollection().Families.Select(font => font.Name).ToArray());
            SettingsThemeColorPopupFirstFontComboBox.Visible = firstLabelVisible && selectedFont is not null;
            SettingsThemeColorPopupFirstFontComboBox.SelectedItem = selectedFont is not null
                                                                        ? new Font(familyName: PFKSettings.CurrentColorPallete.FontName,
                                                                               emSize: 1,
                                                                               style: Font.Style,
                                                                               unit: Font.Unit).FontFamily.Name
                                                                        : -1;

            bool secondLabelVisible = secondLabel is not null;
            SettingsThemeColorPopupSecondLabel.Visible = secondLabelVisible;
            SettingsThemeColorPopupSecondLabel.Text = secondLabelVisible ? secondLabel : string.Empty;
            SettingsThemeColorPopupSecondColorPicker.Visible = secondLabelVisible;
            SettingsThemeColorPopupSecondColorPicker.BackColor = (secondColor != null) ? (Color)secondColor : Color.Pink;
            SettingsThemeColorPopupSecondColorPicker.ForeColor = (secondColor != null) ? HelperFunctions.GetContrastingBAWColor((Color)secondColor) : Color.Pink;

            bool thirdLabelVisible = thirdLabel is not null;
            SettingsThemeColorPopupThirdLabel.Visible = thirdLabelVisible;
            SettingsThemeColorPopupThirdLabel.Text = thirdLabelVisible ? thirdLabel : string.Empty;
            SettingsThemeColorPopupThirdColorPicker.Visible = thirdLabelVisible && fontOverrideSize is null;
            SettingsThemeColorPopupThirdColorPicker.BackColor = (thirdColor != null) ? (Color)thirdColor : Color.Pink;
            SettingsThemeColorPopupThirdColorPicker.ForeColor = (thirdColor != null) ? HelperFunctions.GetContrastingBAWColor((Color)thirdColor) : Color.Pink;

            SettingsThemeColorPopupThirdNumericUpDown.Visible = thirdLabelVisible && fontOverrideSize is not null;
            SettingsThemeColorPopupThirdNumericUpDown.Value = fontOverrideSize is not null
                                                                ? (int)fontOverrideSize
                                                                : PFKSettings.CurrentColorPallete.FontSizeOverride;

            bool fourthLabelVisible = fourthLabel is not null;
            SettingsThemeColorPopupFourthLabel.Visible = fourthLabelVisible;
            SettingsThemeColorPopupFourthLabel.Text = fourthLabelVisible ? fourthLabel : string.Empty;
            SettingsThemeColorPopupFourthColorPicker.Visible = fourthLabelVisible;
            SettingsThemeColorPopupFourthColorPicker.BackColor = (fourthColor != null) ? (Color)fourthColor : Color.Pink;
            SettingsThemeColorPopupFourthColorPicker.ForeColor = (fourthColor != null) ? HelperFunctions.GetContrastingBAWColor((Color)fourthColor) : Color.Pink;
        }

        private void CalculatorSaveStatsButton_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void CalculatorLoadStatsButton_MouseClick(object sender, MouseEventArgs e)
        {

        }
    }
}
