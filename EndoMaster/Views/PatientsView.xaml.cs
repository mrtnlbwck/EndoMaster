using EndoMaster.ServerDb;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace EndoMaster.Views
{
    public sealed partial class PatientsView : UserControl
    {
        // interfejs do MainWindow
        public event Action<int>? PatientSelected;
        public event Action<int>? StartExamRequested;
        public event Action<int>? PatientDetailsRequested;

        private Db? _db;

        private sealed record Row(int Id, int Index, string Name, string Surname, string Pesel, string LastExam);

        public PatientsView()
        {
            InitializeComponent();
        }

        public async Task Initialize(Db db)
        {
            _db = db;
            await LoadPatientsAsync("");
            SearchBox.TextChanged += (_, __) => _ = LoadPatientsAsync(SearchBox.Text.Trim());
        }

        private async Task LoadPatientsAsync(string q)
        {
            if (_db == null) return;
            var rows = await _db.SearchPatientsAsync(q);
            int i = 1;
            PatientsList.ItemsSource = rows.Select(r =>
                new Row(r.id_patient, i++, r.name, r.surname, r.pesel, "")
            ).ToList();
            StartCameraBtn.IsEnabled = PatientsList.SelectedItem is Row;
        }

        private void PatientsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PatientsList.SelectedItem is Row r)
            {
                StartCameraBtn.IsEnabled = true;
                PatientSelected?.Invoke(r.Id);
            }
            else
            {
                StartCameraBtn.IsEnabled = false;
            }
        }

        private void PatientsList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (PatientsList.SelectedItem is Row r)
            {
                // double click == przejœcie do szczegó³ów pacjenta (lista badañ + media)
                PatientDetailsRequested?.Invoke(r.Id);
            }
        }



        private async void NewPatientBtn_Click(object sender, RoutedEventArgs e)
        {
            var name = new TextBox();
            var surname = new TextBox();
            var pesel = new TextBox();

            var sp = new StackPanel { Spacing = 6 };
            sp.Children.Add(new TextBlock { Text = "Imiê" }); sp.Children.Add(name);
            sp.Children.Add(new TextBlock { Text = "Nazwisko" }); sp.Children.Add(surname);
            sp.Children.Add(new TextBlock { Text = "PESEL" }); sp.Children.Add(pesel);

            var dlg = new ContentDialog
            {
                Title = "Nowy pacjent",
                Content = sp,
                PrimaryButtonText = "Zapisz",
                CloseButtonText = "Anuluj",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            var res = await dlg.ShowAsync();
            if (res != ContentDialogResult.Primary || _db is null) return;

            var id = await _db.AddPatientAsync(name.Text.Trim(), surname.Text.Trim(), pesel.Text.Trim());
            await LoadPatientsAsync(surname.Text.Trim());

            // zaznacz œwie¿o dodanego
            var items = PatientsList.Items?.Cast<Row>() ?? Enumerable.Empty<Row>();
            PatientsList.SelectedItem = items.FirstOrDefault(x => x.Id == id);
        }

        private void StartCameraBtn_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsList.SelectedItem is Row r)
                StartExamRequested?.Invoke(r.Id);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // handler zostaje, ale realne odœwie¿enie robi Initialize + lambda
        }
    }
}

