using EndoMaster.Data;
using EndoMaster.ServerDb;
using EndoMaster.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using WinRT.Interop;

namespace EndoMaster
{
    public sealed partial class MainWindow : Window
    {
        private Db? _db;
        private int? _currentPatientId;
        private int? _currentExamId;

        private bool _dbConnecting;
        private bool _dbReady;

        // cache do listy pacjentów
        private PatientsView? _patientsView;
        private bool _patientsEventsWired;   // czy eventy PatientsView są już podpięte

        public MainWindow()
        {
            InitializeComponent();
            Title = "EndoMaster";
            this.Closed += MainWindow_Closed;

            // Maksymalizacja okna
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            if (appWindow.Presenter is OverlappedPresenter p) p.Maximize();
        }

        // === helper: Frame ===
        private Frame EnsureContentFrame() => RootFrame;

        // === helper: nawigacja ===
        private void SafeNavigate(Type viewType)
        {
            var frame = EnsureContentFrame();

            if (frame.Content != null && frame.Content.GetType() == viewType)
                return;

            object? instance = Activator.CreateInstance(viewType);

            if (instance is UIElement element)
            {
                frame.Content = element;
            }
            else
            {
                ShowError(
                    "Błąd nawigacji",
                    new InvalidOperationException(
                        $"Typ {viewType.FullName} nie jest kontrolką XAML (UIElement)."));
            }
        }

        // === start po zbudowaniu drzewa ===
        private void Root_Loaded(object sender, RoutedEventArgs e)
        {
            var f = EnsureContentFrame();
            f.NavigationFailed += (s, e2) =>
            {
                e2.Handled = true;
                ShowError("NavigationFailed", e2.Exception);
            };

            // ustaw domyślnie "Lista pacjentów"
            if (NavView.MenuItems.Count > 0)
                NavView.SelectedItem = NavView.MenuItems[0];

            SafeNavigate(typeof(PatientsView));

            // start połączenia z DB
            _ = EnsureDbConnectedAsync();
        }

        // === obsługa NavigationView ===
        private async void NavView_SelectionChanged(
            NavigationView sender,
            NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
                return;

            switch (item.Tag)
            {
                case "patients":
                    if (_db == null) return;

                    // jeśli DB już połączona, upewniamy się, że _patientsView jest
                    // i ustawiamy ją jako zawartość ramki
                    await AfterDbConnectedAsync();
                    break;

                case "camera":
                    // Szybkie badanie = za każdym razem NOWY pacjent EMERGENCY
                    if (_db == null) return;

                    // 1) utwórz pacjenta EMERGENCY
                    var emergencyId = await _db.AddPatientAsync(
                        name: "EMERGENCY",
                        surname: "EMERGENCY",
                        pesel: "EMERGENCY"
                    );
                    _currentPatientId = emergencyId;

                    // 2) utwórz badanie dla tego pacjenta
                    _currentExamId = await _db.CreateExamAsync(emergencyId,
                        examType: "Szybkie badanie");

                    // 3) przejdź do kamery
                    SafeNavigate(typeof(CameraView));
                    var camFrame = EnsureContentFrame();

                    CameraView quickCv;
                    if (camFrame.Content is CameraView existingQuick)
                    {
                        quickCv = existingQuick;
                    }
                    else
                    {
                        quickCv = new CameraView();
                        camFrame.Content = quickCv;
                    }

                    // 4) ustaw kontekst i start kamery
                    quickCv.Initialize(_db);
                    quickCv.SetContext(_currentPatientId.Value, _currentExamId.Value);

                    // >>> to jest kluczowe dla przycisku "Zakończ badanie" <<<
                    quickCv.FinishExamRequested -= OnFinishExamRequested;
                    quickCv.FinishExamRequested += OnFinishExamRequested;

                    await quickCv.StartWithDefaultCameraAsync();
                    break;



                case "settings":
                    // TODO: SettingsView
                    break;
            }
        }

        // === DB bootstrap + dialog ===
        private async Task EnsureDbConnectedAsync()
        {
            if (_dbReady || _dbConnecting) return;
            _dbConnecting = true;

            try
            {
                while (!_dbReady)
                {
                    var opt = AppConfig.LoadDbOptions();
                    DbStatus($"DB: używam CS = {AppConfig.BuildConnectionString(opt, maskPasswordInResult: true)}");

                    _db = new Db(AppConfig.BuildConnectionString(opt));
                    var (ok, sql, msg) = await _db.TryConnectAsync();

                    if (ok)
                    {
                        await _db.EnsureSchemaAsync();
                        DbStatus("DB: ✅ Połączono");
                        _dbReady = true;
                        await AfterDbConnectedAsync();
                        break;
                    }

                    DbStatus($"DB: ❌ {sql ?? ""} {msg ?? "Unknown error"} — wymagane ustawienie połączenia.");

                    var saved = await ShowDbConfigDialogAsync(opt);
                    if (!saved)
                    {
                        DbStatus("DB: anulowano konfigurację – zamykam aplikację.");
                        Application.Current?.Exit();
                        return;
                    }
                }
            }
            finally
            {
                _dbConnecting = false;
            }
        }

        // Po udanym połączeniu z DB – konfigurujemy PatientsView i eventy
        private async Task AfterDbConnectedAsync()
        {
            if (_db == null) return;

            // zawsze przełączamy się na PatientsView
            SafeNavigate(typeof(PatientsView));
            var frame = EnsureContentFrame();

            PatientsView pv;
            if (frame.Content is PatientsView existing)
            {
                pv = existing;          // już utworzony widok w ramce
            }
            else
            {
                pv = new PatientsView();
                frame.Content = pv;     // nowy widok
            }

            // sprawdzamy, czy to NOWA instancja, inna niż poprzednio
            bool isNewInstance = !ReferenceEquals(_patientsView, pv);
            _patientsView = pv;

            // zawsze odświeżamy listę
            await pv.Initialize(_db);

            // eventy podpinamy:
            // - pierwszy raz
            // - albo gdy pojawiła się nowa instancja PatientsView
            if (!_patientsEventsWired || isNewInstance)
            {
                _patientsEventsWired = true;

                // najpierw na wszelki wypadek odpinamy wszędzie nasze handlery,
                // żeby nie dublować (gdyby jednak były)
                pv.PatientSelected -= OnPatientSelected;
                pv.StartExamRequested -= OnStartExamRequested;
                pv.PatientDetailsRequested -= OnPatientDetailsRequested;

                pv.PatientSelected += OnPatientSelected;
                pv.StartExamRequested += OnStartExamRequested;
                pv.PatientDetailsRequested += OnPatientDetailsRequested;
            }
        }

        private void OnPatientSelected(int id)
        {
            _currentPatientId = id;
        }

        private async void OnStartExamRequested(int pid)
        {
            if (_db == null) return;
            _currentPatientId = pid;
            _currentExamId = await _db.CreateExamAsync(pid);

            SafeNavigate(typeof(CameraView));
            var f2 = EnsureContentFrame();

            CameraView cv;
            if (f2.Content is CameraView existingCv)
            {
                cv = existingCv;
            }
            else
            {
                cv = new CameraView();
                f2.Content = cv;
            }

            cv.Initialize(_db);
            cv.SetContext(_currentPatientId!.Value, _currentExamId!.Value);
            cv.FinishExamRequested -= OnFinishExamRequested;   // żeby się nie duplikowało
            cv.FinishExamRequested += OnFinishExamRequested;
            await cv.StartWithDefaultCameraAsync();
        }

        private async void OnPatientDetailsRequested(int pid)
        {
            if (_db == null) return;
            _currentPatientId = pid;

            SafeNavigate(typeof(PatientDetailsView));
            var f3 = EnsureContentFrame();

            PatientDetailsView details;
            if (f3.Content is PatientDetailsView existingDetails)
            {
                details = existingDetails;
            }
            else
            {
                details = new PatientDetailsView();
                f3.Content = details;
            }

            await details.InitializeAsync(_db, pid);

            details.BackRequested -= OnPatientDetailsBackRequested;
            details.BackRequested += OnPatientDetailsBackRequested;
        }

        private async void OnFinishExamRequested()
        {
            // po „Zakończ badanie” wracamy do listy pacjentów
            if (_db == null) return;

            if (NavView.MenuItems.Count > 0)
                NavView.SelectedItem = NavView.MenuItems[0];

            await AfterDbConnectedAsync();
        }



        // Powrót z widoku pacjenta do listy pacjentów
        private async void OnPatientDetailsBackRequested()
        {
            if (_db == null) return;

            // zaznacz „Lista pacjentów” w menu
            if (NavView.MenuItems.Count > 0)
                NavView.SelectedItem = NavView.MenuItems[0];

            // przywróć / odśwież PatientsView
            await AfterDbConnectedAsync();
        }







        // === dialog konfiguracji DB ===
        private async Task<bool> ShowDbConfigDialogAsync(DbOptions current)
        {
            var tbHost = new TextBox { Text = current.Host };
            var tbPort = new NumberBox { Value = current.Port, Minimum = 1, Maximum = 65535 };
            var tbDb = new TextBox { Text = current.Database };
            var tbUser = new TextBox { Text = current.Username };
            var pbPass = new PasswordBox { Password = AppConfig.DecryptIfNeeded(current.Password) };
            var info = new InfoBar { IsOpen = false, Severity = InfoBarSeverity.Informational, Margin = new Thickness(0, 8, 0, 0) };

            var sp = new StackPanel { Spacing = 6 };
            void Row(string label, UIElement el) { sp.Children.Add(new TextBlock { Text = label }); sp.Children.Add(el); }
            Row("Host", tbHost); Row("Port", tbPort); Row("Baza danych", tbDb); Row("Użytkownik", tbUser); Row("Hasło", pbPass);
            sp.Children.Add(info);

            var dlg = new ContentDialog
            {
                Title = "Konfiguracja bazy danych",
                Content = sp,
                PrimaryButtonText = "Zapisz i połącz",
                CloseButtonText = "Anuluj",
                DefaultButton = ContentDialogButton.Primary,
            };

            dlg.XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
                           ?? (NavView as FrameworkElement)?.XamlRoot;

            dlg.PrimaryButtonClick += async (s, e) =>
            {
                var def = e.GetDeferral();
                try
                {
                    info.IsOpen = true;
                    info.Message = "Sprawdzam połączenie…";

                    var opt = new DbOptions
                    {
                        Host = tbHost.Text.Trim(),
                        Port = (int)Math.Max(1, tbPort.Value),
                        Database = tbDb.Text.Trim(),
                        Username = tbUser.Text.Trim(),
                        Password = pbPass.Password
                    };

                    var testDb = new Db(AppConfig.BuildConnectionString(opt));
                    var (ok, sql, msg) = await testDb.TryConnectAsync();

                    if (!ok)
                    {
                        e.Cancel = true;
                        info.Severity = InfoBarSeverity.Error;
                        info.Message = $"Błąd połączenia: {(sql ?? "").Trim()} {(msg ?? "").Trim()}".Trim();
                        return;
                    }

                    AppConfig.SaveLocalEncrypted(opt);
                    info.Severity = InfoBarSeverity.Success;
                    info.Message = "Połączono i zapisano konfigurację.";
                }
                finally { def.Complete(); }
            };

            return await dlg.ShowAsync() == ContentDialogResult.Primary;
        }

        // === pomocnicze ===
        private async void ShowError(string title, Exception ex)
        {
            try
            {
                await new ContentDialog
                {
                    Title = title,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = ex.ToString(),
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                    PrimaryButtonText = "OK",
                    XamlRoot = (Content as FrameworkElement)?.XamlRoot
                }.ShowAsync();
            }
            catch { }
        }

        private void DbStatus(string msg)
        {
            try { DbStatusText.Text = msg; } catch { }
        }

        private void MainWindow_Closed(object? sender, WindowEventArgs e)
        {
        }
    }
}
