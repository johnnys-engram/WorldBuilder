using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using HanumanInstitute.MvvmDialogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Converters;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Lib.AceDb;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Generates Avalonia UI controls dynamically from settings metadata
    /// </summary>
    public class SettingsUIGenerator {
        private readonly object _settingsRoot;
        private readonly SettingsMetadataProvider _metadata;
        private readonly SettingsUIHandlers? _handlers;
        private readonly Dictionary<Type, Func<SettingPropertyMetadata, object, string, Control?>> _controlFactories;

        public SettingsUIGenerator(object settingsRoot, Window? window = null) {
            _settingsRoot = settingsRoot;
            _metadata = new SettingsMetadataProvider(settingsRoot.GetType());
            _handlers = window != null ? new SettingsUIHandlers(window) : null;

            _controlFactories = new Dictionary<Type, Func<SettingPropertyMetadata, object, string, Control?>> {
                { typeof(bool), CreateBoolControl },
                { typeof(int), CreateNumericControl },
                { typeof(long), CreateNumericControl },
                { typeof(float), CreateNumericControl },
                { typeof(double), CreateNumericControl },
                { typeof(decimal), CreateNumericControl },
                { typeof(short), CreateNumericControl },
                { typeof(byte), CreateNumericControl },
                { typeof(string), CreateStringControl },
                { typeof(Vector3), CreateVector3Control },
                { typeof(Vector4), CreateVector4Control }
            };
        }

        /// <summary>
        /// Generate navigation items for the settings categories
        /// </summary>
        public ListBox GenerateNavigation() {
            var listBox = new ListBox { Margin = new Thickness(0, 16, 0, 0) };

            foreach (var category in _metadata.RootCategories.OrderBy(c => c.Order)) {
                AddNavigationItems(listBox, category, isRoot: true);
            }

            return listBox;
        }

        private void AddNavigationItems(ListBox listBox, SettingCategoryMetadata category, bool isRoot,
            string? parentTag = null) {
            var tag = string.IsNullOrEmpty(parentTag)
                ? category.Name.ToLower().Replace(" ", "-")
                : $"{parentTag}-{category.Name.ToLower().Replace(" ", "-")}";

            var item = new ListBoxItem {
                Content = category.Name,
                Tag = tag,
                Classes = { isRoot ? "NavSection" : "NavSubSection" },
                IsEnabled = category.Properties.Any()
            };

            listBox.Items.Add(item);

            // Add subcategories
            foreach (var subCategory in category.SubCategories.OrderBy(c => c.Order)) {
                AddNavigationItems(listBox, subCategory, isRoot: false, tag);
            }
        }

        /// <summary>
        /// Generate content panels for all categories
        /// </summary>
        public Panel GenerateContentPanels() {
            var panel = new Panel();

            foreach (var category in _metadata.RootCategories) {
                GenerateCategoryPanels(panel, category);
            }

            return panel;
        }

        private void GenerateCategoryPanels(Panel parent, SettingCategoryMetadata category, string? parentTag = null) {
            var tag = string.IsNullOrEmpty(parentTag)
                ? category.Name.ToLower().Replace(" ", "-")
                : $"{parentTag}-{category.Name.ToLower().Replace(" ", "-")}";

            var scrollViewer = new ScrollViewer { Name = $"{tag.Replace("-", "")}Panel", IsVisible = false };

            var stackPanel = new StackPanel { Margin = new Thickness(16) };

            // Add title
            stackPanel.Children.Add(new TextBlock {
                Text = category.Name,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Add property controls
            var categoryInstance = GetCategoryInstance(category.Type);
            if (categoryInstance != null) {
                if (categoryInstance is AceWorldDatabaseSettings aceWorldTop) {
                    stackPanel.Children.Add(CreateAceWorldActionButtons(aceWorldTop));
                }

                foreach (var property in category.Properties) {
                    var control = GeneratePropertyControl(property, categoryInstance);
                    if (control != null) {
                        stackPanel.Children.Add(control);
                    }
                }
            }

            scrollViewer.Content = stackPanel;
            parent.Children.Add(scrollViewer);

            // Generate panels for subcategories
            foreach (var subCategory in category.SubCategories) {
                GenerateCategoryPanels(parent, subCategory, tag);
            }
        }

        private object? GetCategoryInstance(Type categoryType) {
            return FindInstance(categoryType, _settingsRoot);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075:Selectively keep properties for settings", Justification = "Settings classes are preserved by SourceGenerationContext")]
        private object? FindInstance(Type targetType, object current) {
            if (current.GetType() == targetType) return current;

            var properties = current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string) && p.GetMethod != null && p.GetIndexParameters().Length == 0);

            foreach (var prop in properties) {
                var child = prop.GetValue(current);
                if (child != null) {
                    var found = FindInstance(targetType, child);
                    if (found != null) return found;
                }
            }

            return null;
        }

        [UnconditionalSuppressMessage("Trimming",
            "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
            Justification = "<Pending>")]
        private Control? GeneratePropertyControl(SettingPropertyMetadata metadata, object instance) {
            var border = new Border { Classes = { "SettingGroup" }, Margin = new Thickness(0, 0, 0, 16) };

            var stackPanel = new StackPanel();
            var bindingPath = metadata.Property.Name;

            // Label with value display if applicable
            if (metadata.Range != null || !string.IsNullOrEmpty(metadata.Format)) {
                var dockPanel = new DockPanel();

                if (!string.IsNullOrEmpty(metadata.Format)) {
                    var valueDisplay = new TextBlock {
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    valueDisplay.Bind(TextBlock.TextProperty,
                        new Binding { Source = instance, Path = bindingPath, StringFormat = metadata.Format });
                    DockPanel.SetDock(valueDisplay, Dock.Right);
                    dockPanel.Children.Add(valueDisplay);
                }

                dockPanel.Children.Add(new TextBlock { Classes = { "SettingLabel" }, Text = metadata.DisplayName });

                stackPanel.Children.Add(dockPanel);
            }
            else {
                stackPanel.Children.Add(new TextBlock { Classes = { "SettingLabel" }, Text = metadata.DisplayName });
            }

            // Description
            if (!string.IsNullOrEmpty(metadata.Description)) {
                stackPanel.Children.Add(new TextBlock {
                    Classes = { "SettingDescription" },
                    Text = metadata.Description
                });
            }

            // Input control
            var inputControl = CreateInputControl(metadata, instance, bindingPath);
            if (inputControl != null) {
                stackPanel.Children.Add(inputControl);
            }

            border.Child = stackPanel;
            return border;
        }

        [UnconditionalSuppressMessage("Trimming",
            "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
            Justification = "<Pending>")]
        private Control? CreateInputControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            if (metadata.IsAceDatabase) {
                return CreateAceDatabaseControl(metadata, instance, bindingPath);
            }

            var propType = metadata.Property.PropertyType;

            if (_controlFactories.TryGetValue(propType, out var factory)) {
                return factory(metadata, instance, bindingPath);
            }

            if (propType.IsEnum) {
                return CreateEnumControl(metadata, instance, bindingPath);
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateAceDatabaseControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var services = App.Services;
            if (services == null) return null;

            var viewModel = services.GetRequiredService<AceDatabaseSelectionViewModel>();
            var view = new Views.AceDatabaseSelectionView { DataContext = viewModel };

            // Bind the AceDbId property bidirectionally
            var binding = new Binding {
                Source = viewModel,
                Path = nameof(AceDatabaseSelectionViewModel.SelectedManagedAceDb) + "." + nameof(ManagedAceDb.Id),
                Mode = BindingMode.TwoWay
            };
            
            // Link the view model to the setting property
            viewModel.DatabaseSelected += (s, guid) => {
                metadata.Property.SetValue(instance, guid);
            };

            // Set initial value
            var currentGuid = (Guid?)metadata.Property.GetValue(instance);
            if (currentGuid != null) {
                viewModel.SelectedAceSourceType = AceSourceType.Managed;
                viewModel.SelectedManagedAceDb = viewModel.ManagedAceDbs.FirstOrDefault(d => d.Id == currentGuid);
            } else {
                viewModel.SelectedAceSourceType = AceSourceType.None;
            }

            return view;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateBoolControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var checkBox = new CheckBox { Content = $"Enable {metadata.DisplayName.ToLower()}" };
            checkBox.Bind(ToggleButton.IsCheckedProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return checkBox;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateNumericControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            if (metadata.Range != null) {
                var slider = new Slider {
                    Minimum = metadata.Range.Minimum,
                    Maximum = metadata.Range.Maximum,
                    SmallChange = metadata.Range.SmallChange,
                    LargeChange = metadata.Range.LargeChange
                };
                slider.Bind(Slider.ValueProperty,
                    new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
                return slider;
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateStringControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            if (metadata.Path != null) {
                var dockPanel = new DockPanel();

                var button = new Button { Content = "Browse...", Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(button, Dock.Right);
                dockPanel.Children.Add(button);

                var textBox = new TextBox { Watermark = metadata.Path.DialogTitle ?? "Select path..." };
                textBox.Bind(TextBox.TextProperty,
                    new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
                dockPanel.Children.Add(textBox);

                if (_handlers != null && metadata.Path != null) {
                    _handlers.AttachBrowseHandler(button, metadata.Path, textBox);
                }

                return dockPanel;
            }

            return CreateDefaultControl(metadata, instance, bindingPath);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "<Pending>")]
        private Control? CreateEnumControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var comboBox = new ComboBox {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = Enum.GetValues(metadata.Property.PropertyType)
            };
            comboBox.Bind(SelectingItemsControl.SelectedItemProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return comboBox;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateVector3Control(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var colorPicker = new Avalonia.Controls.ColorPicker { HorizontalAlignment = HorizontalAlignment.Left };

            var converter = new Vector3ToColorConverter();
            colorPicker.Bind(Avalonia.Controls.ColorPicker.ColorProperty,
                new Binding {
                    Source = instance,
                    Path = bindingPath,
                    Mode = BindingMode.TwoWay,
                    Converter = converter
                });
            return colorPicker;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateVector4Control(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var colorPicker = new Avalonia.Controls.ColorPicker { HorizontalAlignment = HorizontalAlignment.Left };

            var converter = new Vector4ToColorConverter();
            colorPicker.Bind(Avalonia.Controls.ColorPicker.ColorProperty,
                new Binding {
                    Source = instance,
                    Path = bindingPath,
                    Mode = BindingMode.TwoWay,
                    Converter = converter
                });
            return colorPicker;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
        private Control? CreateDefaultControl(SettingPropertyMetadata metadata, object instance, string bindingPath) {
            var defaultTextBox = new TextBox();
            defaultTextBox.Bind(TextBox.TextProperty,
                new Binding { Source = instance, Path = bindingPath, Mode = BindingMode.TwoWay });
            return defaultTextBox;
        }

        private Control CreateAceWorldActionButtons(AceWorldDatabaseSettings aceWorld) {
            var row = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var testBtn = new Button { Content = "Test" };
            testBtn.Click += async (_, _) => {
                testBtn.IsEnabled = false;
                try {
                    var connector = new AceDbConnector(aceWorld.ToAceDbSettings());
                    var error = await connector.TestConnectionAsync();
                    var dlg = App.Services?.GetService<IDialogService>();
                    if (dlg != null) {
                        if (error == null)
                            await dlg.ShowMessageBoxAsync(null, "Connection successful!", "ACE MySQL");
                        else
                            await dlg.ShowMessageBoxAsync(null, $"Connection failed:\n{error}", "ACE MySQL");
                    }
                }
                finally {
                    testBtn.IsEnabled = true;
                }
            };

            var connectBtn = new Button { Content = "Connect" };
            connectBtn.Click += async (_, _) => {
                connectBtn.IsEnabled = false;
                try {
                    var connector = new AceDbConnector(aceWorld.ToAceDbSettings());
                    var dlg = App.Services?.GetService<IDialogService>();
                    if (dlg == null) return;

                    var encounters = await connector.GetEncountersAsync(5);
                    if (encounters.Count == 0) {
                        await dlg.ShowMessageBoxAsync(null,
                            "No rows returned from encounter table.\n\nEnsure Database = ace_world_release in settings.",
                            "ACE MySQL");
                        return;
                    }

                    var sb = new StringBuilder();

                    // ── Table 1: encounters ──────────────────────────────────────────
                    sb.AppendLine($"Database: {aceWorld.Database}");
                    sb.AppendLine();
                    sb.AppendLine("[ encounter ]");
                    sb.AppendLine($"{"Landblock",-12} {"WeenieId",-10} {"Name",-28} {"CellX",-7} CellY");
                    sb.AppendLine(new string('-', 70));
                    foreach (var r in encounters)
                        sb.AppendLine($"0x{r.Landblock:X5}      {r.WeenieClassId,-10} {r.WeenieName,-28} {r.CellX,-7} {r.CellY}");

                    // Table 2: weenie_properties_generator for first encounter
                    var first = encounters[0];
                    var generators = await connector.GetWeenieGeneratorsAsync(first.WeenieClassId, 2);

                    sb.AppendLine();
                    sb.AppendLine($"[ weenie_properties_generator  object_Id={first.WeenieClassId}  ({first.WeenieName}) ]");

                    if (generators.Count == 0) {
                        sb.AppendLine("  (no generator rows for this weenie)");
                    }
                    else {
                        sb.AppendLine($"{"Prob",-6} {"SpawnId",-9} {"SpawnName",-28} {"Delay",-8} {"Init",-6} {"Max",-6} When");
                        sb.AppendLine(new string('-', 70));
                        foreach (var g in generators)
                            sb.AppendLine($"{g.Probability,-6:F2} {g.SpawnWeenieClassId,-9} {g.SpawnWeenieName,-28} {g.Delay,-8:F0} {g.InitCreate,-6} {g.MaxCreate,-6} {g.WhenCreate}");
                    }

                    // Table 3: weenie_properties_d_i_d for the spawned mob
                    var spawnId = generators.Count > 0
                        ? generators[0].SpawnWeenieClassId
                        : first.WeenieClassId;
                    var spawnName = generators.Count > 0
                        ? generators[0].SpawnWeenieName
                        : first.WeenieName;

                    var dids = await connector.GetWeenieDidsAsync(spawnId);

                    sb.AppendLine();
                    sb.AppendLine($"[ weenie_properties_d_i_d  object_Id={spawnId}  ({spawnName}) ]");

                    if (dids.Count == 0) {
                        sb.AppendLine("  (no DID rows for this weenie)");
                    }
                    else {
                        sb.AppendLine($"{"Type",-6} {"Value (hex)",-14} Label");
                        sb.AppendLine(new string('-', 50));
                        foreach (var d in dids) {
                            var label = d.Type switch {
                                1  => "Setup",
                                2  => "MotionTable",
                                3  => "SoundTable",
                                4  => "CombatTable",
                                6  => "PaletteBase",
                                7  => "ClothingBase",
                                8  => "Icon",
                                22 => "PhysicsEffectTable",
                                _  => $"type {d.Type}"
                            };
                            sb.AppendLine($"{d.Type,-6} 0x{d.Value:X8}     {label}");
                        }
                    }

                    await dlg.ShowMessageBoxAsync(null, sb.ToString(), "Encounter + Generator + DIDs");
                }
                catch (Exception ex) {
                    var dlg = App.Services?.GetService<IDialogService>();
                    if (dlg != null)
                        await dlg.ShowMessageBoxAsync(null, $"Error: {ex.Message}", "ACE MySQL");
                }
                finally {
                    connectBtn.IsEnabled = true;
                }
            };

            row.Children.Add(testBtn);
            row.Children.Add(connectBtn);
            return row;
        }
    }
}