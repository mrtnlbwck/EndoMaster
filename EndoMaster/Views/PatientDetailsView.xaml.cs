using EndoMaster.ServerDb;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Media.Playback;
using Windows.Media.Core;

namespace EndoMaster.Views
{
    public sealed partial class PatientDetailsView : UserControl
    {
        private Db? _db;
        private int _patientId;

        private List<ExamRow> _exams = new();
        private List<MediaRow> _media = new();
        private MediaRow? _selectedMedia;
        public event Action? BackRequested;

        private sealed record ExamRow(
            int Id,
            string Label,
            string Date,
            string? Description,
            bool Important
        );

        private sealed record MediaRow(
            bool IsMovie,
            int Id,
            string Kind,
            string Time,
            string FileName,
            string FullPath,
            string? Description,
            bool Important
        );

        public PatientDetailsView()
        {
            InitializeComponent();
        }

        public async Task InitializeAsync(Db db, int patientId)
        {
            _db = db;
            _patientId = patientId;

            if (_db == null) return;

            // ---------- nagłówek pacjenta ----------
            var p = await _db.GetPatientAsync(patientId);
            PatientNameText.Text = $"{p.name} {p.surname}";
            PatientPeselText.Text = $"PESEL: {p.pesel}";
            PatientExtraText.Text = string.Empty;

            await ReloadExamsAsync();
        }

        private async Task ReloadExamsAsync()
        {
            if (_db == null) return;

            var exams = await _db.GetExamsForPatientAsync(_patientId);

            _exams = exams.Select(e =>
                new ExamRow(
                    Id: e.id_exam,
                    Label: string.IsNullOrWhiteSpace(e.type) ? "Badanie" : e.type!,
                    Date: $"{e.date:yyyy-MM-dd} {e.time:hh\\:mm}",
                    Description: e.description,
                    Important: e.important
                )
            ).ToList();

            ExamsList.ItemsSource = _exams;

            if (_exams.Count > 0)
            {
                ExamsList.SelectedIndex = 0;
            }
            else
            {
                MediaList.ItemsSource = null;
                SelectedMediaHeader.Text = "Elementy badania – brak plików";
                ClearPreview();
                ExamDescriptionText.Text = "";
                EditDescriptionBtn.Content = "Edytuj opis";
                ExamDescriptionText.IsReadOnly = true;
            }
        }

        private async void ExamsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null || ExamsList.SelectedItem is not ExamRow row)
            {
                // brak wybranego badania – czyścimy
                MediaList.ItemsSource = null;
                SelectedMediaHeader.Text = "Elementy badania";
                ClearPreview();

                ExamDescriptionText.Text = "";
                ExamDescriptionText.IsReadOnly = true;
                EditDescriptionBtn.Content = "Edytuj opis";
                EditDescriptionBtn.IsEnabled = false;
                DeleteExamBtn.IsEnabled = false;

                DeleteMediaBtn.IsEnabled = false;
                _selectedMedia = null;
                await SetSelectedMediaAsync(null);

                return;
            }

            // opis badania
            ExamDescriptionText.Text = row.Description ?? string.Empty;
            ExamDescriptionText.IsReadOnly = true;
            EditDescriptionBtn.Content = "Edytuj opis";
            EditDescriptionBtn.IsEnabled = true;
            DeleteExamBtn.IsEnabled = true;

            // media dla wybranego badania
            await ReloadMediaForExamAsync(row.Id);
        }

        private async Task ReloadMediaForExamAsync(int examId)
        {
            if (_db == null) return;

            var media = await _db.GetMediaForExamAsync(examId);

            _media = media.Select(m =>
                new MediaRow(
                    IsMovie: m.isMovie,
                    Id: m.id,
                    Kind: m.isMovie ? "Nagranie" : "Zdjęcie",
                    Time: m.time.ToString("hh\\:mm\\:ss"),
                    FileName: Path.GetFileName(m.path),
                    FullPath: m.path,
                    Description: m.description,
                    Important: m.important
                )
            ).ToList();

            MediaList.ItemsSource = _media;

            if (_media.Count > 0)
                MediaList.SelectedIndex = 0;   // pierwsza pozycja od razu zaznaczona
            else
            {
                _selectedMedia = null;
                DeleteMediaBtn.IsEnabled = false;
                await SetSelectedMediaAsync(null);
            }

            SelectedMediaHeader.Text = _media.Count > 0
                ? $"Elementy badania ({_media.Count})"
                : "Elementy badania – brak plików";
        }

        private async void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaRow row)
            {
                _selectedMedia = row;
                DeleteMediaBtn.IsEnabled = true;
                await SetSelectedMediaAsync(row);   // pokaz zdjęcie / nagranie
            }
            else
            {
                _selectedMedia = null;
                DeleteMediaBtn.IsEnabled = false;
                await SetSelectedMediaAsync(null);  // wyczyść podgląd
            }
        }


        private async Task SetSelectedMediaAsync(MediaRow? row)
        {
            DeleteMediaBtn.IsEnabled = row is not null;
            _selectedMedia = row;
            if (row is null)
            {
                ClearPreview();
                return;
            }

            if (!File.Exists(row.FullPath))
            {
                ClearPreview();
                PreviewPlaceholder.Text = "Plik nie istnieje na dysku";
                return;
            }

            try
            {
                if (row.IsMovie)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewPlayer.Visibility = Visibility.Visible;

                    var file = await StorageFile.GetFileFromPathAsync(row.FullPath);
                    var ms = new MediaPlayer();
                    ms.Source = MediaSource.CreateFromStorageFile(file);
                    ms.IsMuted = false;

                    PreviewPlayer.SetMediaPlayer(ms);
                    ms.Play();
                }
                else
                {
                    PreviewPlayer.Visibility = Visibility.Collapsed;
                    PreviewPlayer.SetMediaPlayer(null);

                    PreviewImage.Visibility = Visibility.Visible;

                    var file = await StorageFile.GetFileFromPathAsync(row.FullPath);
                    using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(stream);
                    PreviewImage.Source = bmp;
                }

                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ClearPreview();
                PreviewPlaceholder.Text = "Nie udało się odtworzyć pliku";
            }
        }

        private void ClearPreview()
        {
            PreviewPlayer.SetMediaPlayer(null);
            PreviewPlayer.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholder.Text = "Brak wybranego elementu";
        }

        private async void MediaList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaRow row)
            {
                await SetSelectedMediaAsync(row);
            }
        }

        // ===== Usuwanie pojedynczego elementu (zdjęcie / nagranie) =====
        private async void DeleteMediaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null || MediaList.SelectedItem is not MediaRow row)
                return;

            // proste potwierdzenie (możesz podmienić na ContentDialog jak chcesz)
            var dlg = new ContentDialog
            {
                Title = "Usuń element",
                Content = $"Czy na pewno chcesz usunąć ten element:\n{row.FileName}?",
                PrimaryButtonText = "Usuń",
                CloseButtonText = "Anuluj",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var res = await dlg.ShowAsync();
            if (res != ContentDialogResult.Primary)
                return;

            await _db.DeleteMediaAsync(row.IsMovie, row.Id);

            // odśwież listę mediów dla bieżącego badania
            if (ExamsList.SelectedItem is ExamRow examRow)
                await ReloadMediaForExamAsync(examRow.Id);
        }



        // ===== Edycja / zapis opisu badania =====
        private async void EditDescriptionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;
            if (ExamsList.SelectedItem is not ExamRow row) return;

            if (ExamDescriptionText.IsReadOnly)
            {
                // tryb edycji
                ExamDescriptionText.IsReadOnly = false;
                EditDescriptionBtn.Content = "Zapisz opis";
                ExamDescriptionText.Focus(FocusState.Programmatic);
                ExamDescriptionText.Select(ExamDescriptionText.Text.Length, 0);
            }
            else
            {
                // zapis
                var newDesc = ExamDescriptionText.Text.Trim();
                await _db.UpdateExamDescriptionAsync(row.Id, newDesc);

                // odśwież lokalną listę _exams
                var idx = _exams.FindIndex(x => x.Id == row.Id);
                if (idx >= 0)
                {
                    _exams[idx] = _exams[idx] with { Description = newDesc };
                    ExamsList.ItemsSource = null;
                    ExamsList.ItemsSource = _exams;
                    ExamsList.SelectedItem = _exams[idx];
                }

                ExamDescriptionText.IsReadOnly = true;
                EditDescriptionBtn.Content = "Edytuj opis";
            }
        }

        // ===== Usuwanie całego badania =====
        private async void DeleteExamBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null) return;
            if (ExamsList.SelectedItem is not ExamRow row) return;

            var dlg = new ContentDialog
            {
                Title = "Usuń badanie",
                Content = $"Czy na pewno chcesz usunąć całe badanie z dnia {row.Date}?\n" +
                          "Zostaną również usunięte wszystkie powiązane zdjęcia i nagrania.",
                PrimaryButtonText = "Usuń badanie",
                CloseButtonText = "Anuluj",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var res = await dlg.ShowAsync();
            if (res != ContentDialogResult.Primary) return;

            await _db.DeleteExamAsync(row.Id);

            await ReloadExamsAsync();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke();
        }

    }
}
