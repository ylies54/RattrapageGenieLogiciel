using EasySave.Core;
using EasySave_G3_V1;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace EasySave_G3_V2_0
{
    public partial class MainWindow : Window
    {
        // ---------------------------------------------------------------------
        // Fields
        // ---------------------------------------------------------------------
        private readonly SocketServer _server;           // remote monitoring console
        private readonly ConsoleViewModel consoleViewModel = new();
        private readonly string exePath = Path.GetDirectoryName(
                                              Assembly.GetExecutingAssembly().Location);

        // ---------------------------------------------------------------------
        // Ctor : initialises socket, languages, scenarios, UI
        // ---------------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();

            /* 1) Start TCP server (viewer console) */
            _server = new SocketServer();
            _server.Start();

            /* 2) Detect + load languages */
            consoleViewModel.GetLangages().SearchLangages();
            string frPath = Path.Combine(AppPaths.LangDir, "French.json");
            Langage langage = new Langage("French.json", frPath);

            /* 3) Load scenarios list */
            ScenarioList scenarioList = consoleViewModel.GetScenarioList();

            try
            {
                langage.LoadLangage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error when loading language : {ex.Message}");
            }

            try
            {
                scenarioList.Load(AppPaths.Scenarios);
                SaveDataGrid.ItemsSource = scenarioList.Get();

                // Reset state to “Pending” on start-up
                foreach (var item in SaveDataGrid.Items)
                    if (item is Scenario sc) sc.SetState(BackupState.Pending);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error when loading scenario : {ex.Message}");
            }

            UpdateType();   // fill ComboBox with enum names
            ReadOnly();     // lock edition controls
        }

        // ---------------------------------------------------------------------
        // Helpers : lock/unlock edit panel
        // ---------------------------------------------------------------------
        private void ReadOnly()
        {
            SaveDataGrid.IsEnabled = true;
            TxtBoxName.IsEnabled = false;
            TxTBoxSource.IsEnabled = false;
            TxTBoxTarget.IsEnabled = false;
            TxTBoxDescription.IsEnabled = false;
            CbBox_Type.IsEnabled = false;
            Button_Validation.IsEnabled = false;
        }

        private void WriteOnly(
            int id = 0,
            string? name = null,
            string? source = null,
            string? target = null,
            string? description = null,
            BackupType type = BackupType.Full)
        {
            SaveDataGrid.IsEnabled = false;
            TxtBoxName.IsEnabled = true;
            TxTBoxSource.IsEnabled = true;
            TxTBoxTarget.IsEnabled = true;
            TxTBoxDescription.IsEnabled = true;
            CbBox_Type.IsEnabled = true;
            Button_Validation.IsEnabled = true;

            // Pre-fill inputs (edit mode)
            TxtBoxName.Text = name;
            TxTBoxSource.Text = source;
            TxTBoxTarget.Text = target;
            TxTBoxDescription.Text = description;
            CbBox_Type.SelectedItem = type;

            // Keep id inside the Grid name (legacy trick)
            Grid_Modify.Name = "Grid_Modify" + id;
        }

        // ---------------------------------------------------------------------
        // PLAY button – run all selected jobs *in parallel*
        // ---------------------------------------------------------------------
        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            /* 1) Collect selected scenarios */
            var selection = SaveDataGrid.Items
                                        .OfType<Scenario>()
                                        .Where(sc => sc.IsSelected)
                                        .ToList();
            if (selection.Count == 0)
            {
                MessageBox.Show("No scenario selected.");
                return;
            }

            /* 2) Fire every backup (ExecuteAsync) without awaiting */
            var tasks = selection.Select(sc => sc.ExecuteAsync()).ToList();

            /* 3) Await them all concurrently */
            var results = await Task.WhenAll(tasks);   // List<string>[]

            /* 4) Show aggregated errors (if any) */
            var errors = results
                         .SelectMany(lst => lst)
                         .Where(msg => msg.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                         .ToList();

            if (errors.Count > 0)
                MessageBox.Show(string.Join('\n', errors), "Backup errors");

            /* 5) Refresh DataGrid (state / progress) */
            SaveDataGrid.Items.Refresh();
        }

        // ---------------------------------------------------------------------
        // Toolbar – Add scenario
        // ---------------------------------------------------------------------
        private void AddScenario_Click(object sender, RoutedEventArgs e) => WriteOnly();

        // ---------------------------------------------------------------------
        // VALIDATE (Add / Edit)
        // ---------------------------------------------------------------------
        private void Button_Validation_Click(object sender, RoutedEventArgs e)
        {
            // Id encoded at the end of Grid name (0 when creating)
            int id = int.Parse(Grid_Modify.Name[^1..]);

            string name = TxtBoxName.Text;
            string source = TxTBoxSource.Text;
            string target = TxTBoxTarget.Text;
            string description = TxTBoxDescription.Text;
            BackupType type = GetBackupType(CbBox_Type.SelectedIndex);

            if (id != 0)
                consoleViewModel.GetScenarioList()
                                .Modify(id, id, name, source, target, type, description);
            else
                consoleViewModel.GetScenarioList()
                                .CreateScenario(name, source, target, type, description);

            ReadOnly();
            SaveDataGrid.ItemsSource = consoleViewModel.GetScenarioList().Get();
            SaveDataGrid.Items.Refresh();
            Grid_Modify.Name = "Grid_Modify";
        }

        // ---------------------------------------------------------------------
        // Populate ComboBox with “Full”, “Differential”, …
        // ---------------------------------------------------------------------
        private void UpdateType()
        {
            foreach (var enumName in Enum.GetNames(typeof(BackupType)))
                CbBox_Type.Items.Add(enumName);
        }

        // ---------------------------------------------------------------------
        // Delete scenario (with confirmation)
        // ---------------------------------------------------------------------
        private void DeleteScenario_Click(object sender, RoutedEventArgs e)
        {
            var element = ((Button)sender).DataContext as Scenario;
            if (element is null) return;

            var res = MessageBox.Show(
                "Are you sure to delete this scenario?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
                consoleViewModel.GetScenarioList().RemoveScenario(element.GetId());

            SaveDataGrid.Items.Refresh();
        }

        // Map ComboBox index → enum
        private static BackupType GetBackupType(int index) =>
            index switch
            {
                1 => BackupType.Differential,
                _ => BackupType.Full
            };

        // ---------------------------------------------------------------------
        // CANCEL Edition
        // ---------------------------------------------------------------------
        private void Button_Back_Click(object sender, RoutedEventArgs e)
        {
            ReadOnly();
            SaveDataGrid.Items.Refresh();
        }

        // ---------------------------------------------------------------------
        // EDIT scenario
        // ---------------------------------------------------------------------
        private void ModifyScenario_Click(object sender, RoutedEventArgs e)
        {
            var element = ((Button)sender).DataContext as Scenario;
            if (element is null) return;

            WriteOnly(element.GetId(),
                      element.GetName(),
                      element.GetSource(),
                      element.GetTarget(),
                      element.GetDescription(),
                      element.GetSceanrioType());
        }

        // ---------------------------------------------------------------------
        // Opens a CMD in old Console version (demo)
        // ---------------------------------------------------------------------
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                WorkingDirectory = @"..\..\..\..\EasySave_G3_V1_1\bin\Debug\net8.0"
            };
            Process.Start(psi);
        }

        // ---------------------------------------------------------------------
        // Open Settings dialog
        // ---------------------------------------------------------------------
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ParametresWindow { Owner = this };
            dlg.Show();
        }
    }
}
