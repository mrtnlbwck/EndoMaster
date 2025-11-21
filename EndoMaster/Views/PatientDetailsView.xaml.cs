using EndoMaster.ServerDb;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace EndoMaster.Views
{
    public sealed partial class PatientDetailsView : UserControl
    {
        private Db? _db;
        private int _patientId;

        private List<ExamRow> _exams = new();
        private List<MediaRow> _media = new();
        private MediaRow? _selectedMedia;

        private sealed record ExamRow(
            int Id,
            string Label,
            string Date,
            string? Description,
            bool Important
        );

        private sealed class MediaRow
        {
            public bool IsMovie { get; init; }
            public int Id { get; init; }
            public string Kind { get; init; } = "";
            public string Time { get; init; } = "";
            public string FileName { get; init; } = "";
            public string FullPath { get; init; } = "";
            public string? Description { get; set; }
            public bool Important { get; set; }
        }

        public PatientDetailsView()
        {
            InitializeComponent();
            ClearSelectionUi();
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

            // ---------- lista badań ----------
            var exams = await _db.GetExamsForPatientAsync(patientId);

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
                ClearMediaListUi("Brak badań dla tego pacjenta.");
            }
        }

        // wybór badania -> lista mediów
        private async void ExamsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_db == null || ExamsList.SelectedItem is not ExamRow row)
            {
                ClearMediaListUi("Brak wybranego badania");
                return;
            }

            await LoadMediaForExamAsync(row.Id);
        }

        private async Task LoadMediaForExamAsync(int examId)
        {
            if (_db == null) return;

            var media = await _db.GetMediaForExamAsync(examId);

            _media = media.Select(m =>
                new MediaRow
                {
                    IsMovie = m.isMovie,
                    Id = m.id,
                    Kind = m.isMovie ? "Video" : "Foto",
                    Time = m.time.ToString("hh\\:mm\\:ss"),
                    FileName = Path.GetFileName(m.path),
                    FullPath = m.path,
                    Description = m.description,
                    Important = m.important
                }
            ).ToList();

            MediaList.ItemsSource = _media;
            SelectedMediaHeader.Text = _media.Count > 0
                ? $"Elementy badania ({_media.Count})"
                : "Elementy badania – brak plików";

            ClearSelectionUi();
        }

        // wybór elementu z listy
        private async void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaRow row)
            {
                await SetSelectedMediaAsync(row);
            }
            else
            {
                ClearSelectionUi();
            }
        }

        // double-click – też wyświetla podgląd
        private async void MediaList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaRow row)
            {
                await SetSelectedMediaAsync(row);
            }
        }

        private async Task SetSelectedMediaAsync(MediaRow row)
        {
            _selectedMedia = row;

            ExamDescriptionText.Text = row.Description ?? "";
            ImportantCheckBox.IsChecked = row.Important;

            EditDescriptionBtn.IsEnabled = true;
            DeleteMediaBtn.IsEnabled = true;
            ImportantCheckBox.IsEnabled = true;

            await ShowPreviewAsync(row);
        }

        private void ClearMediaListUi(string headerText)
        {
            _media.Clear();
            MediaList.ItemsSource = null;
            SelectedMediaHeader.Text = headerText;
            ClearSelectionUi();
        }

        private void ClearSelectionUi()
        {
            _selectedMedia = null;
            ExamDescriptionText.Text = "";
            EditDescriptionBtn.IsEnabled = false;
            DeleteMediaBtn.IsEnabled = false;
            ImportantCheckBox.IsEnabled = false;
            ImportantCheckBox.IsChecked = false;

            // podgląd
            try
            {
                PreviewPlayer.MediaPlayer?.Pause();
                PreviewPlayer.MediaPlayer?.Dispose();
            }
            catch { }

            PreviewPlayer.Visibility = Visibility.Collapsed;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }

        private async Task ShowPreviewAsync(MediaRow row)
        {
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            if (row.IsMovie)
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewPlayer.Visibility = Visibility.Visible;

                var file = await StorageFile.GetFileFromPathAsync(row.FullPath);
                var source = MediaSource.CreateFromStorageFile(file);

                if (PreviewPlayer.MediaPlayer == null)
                {
                    PreviewPlayer.SetMediaPlayer(new MediaPlayer());
                }

                PreviewPlayer.MediaPlayer.Source = source;
                PreviewPlayer.MediaPlayer.IsMuted = false;
                PreviewPlayer.MediaPlayer.Play();
            }
            else
            {
                PreviewPlayer.Visibility = Visibility.Collapsed;

                var file = await StorageFile.GetFileFromPathAsync(row.FullPath);
                var bitmap = new BitmapImage();
                using (var stream = await file.OpenReadAsync())
                {
                    await bitmap.SetSourceAsync(stream);
                }

                PreviewImage.Source = bitmap;
                PreviewImage.Visibility = Visibility.Visible;
            }
        }

        // zapis opisu
        private async void EditDescriptionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null || _selectedMedia == null) return;

            _selectedMedia.Description = ExamDescriptionText.Text;
            _selectedMedia.Important = ImportantCheckBox.IsChecked == true;

            await _db.UpdateMediaAsync(
                _selectedMedia.IsMovie,
                _selectedMedia.Id,
                _selectedMedia.Description,
                _selectedMedia.Important);
        }

        // usuwanie elementu
        private async void DeleteMediaBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_db == null || _selectedMedia == null) return;

            var toDelete = _selectedMedia;

            await _db.DeleteMediaAsync(toDelete.IsMovie, toDelete.Id);

            _media.Remove(toDelete);
            MediaList.ItemsSource = null;
            MediaList.ItemsSource = _media;

            SelectedMediaHeader.Text = _media.Count > 0
                ? $"Elementy badania ({_media.Count})"
                : "Elementy badania – brak plików";

            ClearSelectionUi();
        }

        // zmiana checkboxa "ważne" – od razu zapis do DB
        private async void ImportantCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_db == null || _selectedMedia == null) return;

            _selectedMedia.Important = ImportantCheckBox.IsChecked == true;

            await _db.UpdateMediaAsync(
                _selectedMedia.IsMovie,
                _selectedMedia.Id,
                _selectedMedia.Description,
                _selectedMedia.Important);
        }
    }
}
