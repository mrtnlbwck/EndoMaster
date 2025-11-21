using EndoMaster.Data;
using EndoMaster.ServerDb;
using EndoMaster.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
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
        private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item) return;

            switch (item.Tag)
            {
                case "patients":
                    SafeNavigate(typeof(PatientsView));
                    if (_db != null && EnsureContentFrame().Content is PatientsView pv)
                    {
                        await pv.Initialize(_db);
                        // zabezpieczenie przed wielokrotnym subskrybowaniem - najpierw odpinamy
                        //pv.PatientSelected = null;
                        //pv.StartExamRequested = null;
                        //pv.PatientDetailsRequested = null;

                        pv.PatientSelected += id => _currentPatientId = id;
                        pv.StartExamRequested += async pid =>
                        {
                            if (_db == null) return;
                            _currentPatientId = pid;
                            _currentExamId = await _db.CreateExamAsync(pid);

                            SafeNavigate(typeof(CameraView));
                            if (EnsureContentFrame().Content is CameraView cv)
                            {
                                cv.Initialize(_db);
                                cv.SetContext(_currentPatientId!.Value, _currentExamId!.Value);
                                await cv.StartWithDefaultCameraAsync();
                            }
                        };
                        pv.PatientDetailsRequested += async pid =>
                        {
                            if (_db == null) return;
                            _currentPatientId = pid;

                            SafeNavigate(typeof(PatientDetailsView));
                            if (EnsureContentFrame().Content is PatientDetailsView details)
                            {
                                await details.InitializeAsync(_db, pid);
                            }
                        };
                    }
                    break;

                case "camera":
                    if (_db == null || _currentPatientId is null)
                    {
                        // brak aktywnego pacjenta → wracamy do listy
                        if (NavView.MenuItems.Count > 0)
                            NavView.SelectedItem = NavView.MenuItems[0];
                        SafeNavigate(typeof(PatientsView));
                        return;
                    }

                    if (_currentExamId is null)
                        _currentExamId = await _db.CreateExamAsync(_currentPatientId.Value);

                    SafeNavigate(typeof(CameraView));
                    if (EnsureContentFrame().Content is CameraView cv2)
                    {
                        cv2.Initialize(_db!);
                        cv2.SetContext(_currentPatientId.Value, _currentExamId.Value);
                        await cv2.StartWithDefaultCameraAsync();
                    }
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

        private async Task AfterDbConnectedAsync()
        {
            if (_db == null) return;

            SafeNavigate(typeof(PatientsView));
            var frame = EnsureContentFrame();

            if (frame.Content is not PatientsView pv)
            {
                pv = new PatientsView();
                frame.Content = pv;
            }

            await pv.Initialize(_db);

            //pv.PatientSelected = null;
            //pv.StartExamRequested = null;
            //pv.PatientDetailsRequested = null;

            pv.PatientSelected += id => _currentPatientId = id;

            pv.StartExamRequested += async pid =>
            {
                if (_db == null) return;
                _currentPatientId = pid;
                _currentExamId = await _db.CreateExamAsync(pid);

                SafeNavigate(typeof(CameraView));
                var f2 = EnsureContentFrame();

                if (f2.Content is not CameraView cv)
                {
                    cv = new CameraView();
                    f2.Content = cv;
                }

                cv.Initialize(_db);
                cv.SetContext(_currentPatientId!.Value, _currentExamId!.Value);
                await cv.StartWithDefaultCameraAsync();
            };

            pv.PatientDetailsRequested += async pid =>
            {
                if (_db == null) return;
                _currentPatientId = pid;

                SafeNavigate(typeof(PatientDetailsView));
                var f3 = EnsureContentFrame();

                if (f3.Content is not PatientDetailsView details)
                {
                    details = new PatientDetailsView();
                    f3.Content = details;
                }

                await details.InitializeAsync(_db, pid);
            };
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
