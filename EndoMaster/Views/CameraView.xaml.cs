using EndoMaster.ServerDb;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace EndoMaster.Views
{
    public sealed partial class CameraView : UserControl
    {
        private MediaCapture? _capture;
        private MediaPlayer? _player;
        private DeviceInformation? _currentDevice;

        private List<DeviceInformation> _devices = new();

        private List<VideoEncodingProperties> _recordProps = new();
        private List<VideoEncodingProperties> _previewProps = new();

        private bool _isPreviewing;
        private bool _isRecording;

        private Db? _db;
        private int? _currentPatientId;
        private int? _currentExamId;
        private StorageFile? _lastRecordedFile;

        public CameraView()
        {
            InitializeComponent();
        }

        // ===== interfejs dla MainWindow =====
        public void Initialize(Db db) => _db = db;

        public void SetContext(int patientId, int examId)
        {
            _currentPatientId = patientId;
            _currentExamId = examId;
            Status($"Aktywne badanie: examId={examId}, patientId={patientId}");
        }

        public async Task StartWithDefaultCameraAsync()
        {
            await RefreshDevicesAsync();
            if (_devices.Count > 0)
            {
                // samo ustawienie SelectedIndex wywoła handler CameraCombo_SelectionChanged,
                // który zawoła SwitchCameraAsync(_devices[CameraCombo.SelectedIndex])
                CameraCombo.SelectedIndex = 0;
            }
            else
            {
                Status("Nie znaleziono kamer.");
                Overlay.Visibility = Visibility.Visible;
            }
        }


        private void Status(string msg) => StatusText.Text = msg;

        // ===== Enumeracja kamer =====
        private async Task RefreshDevicesAsync()
        {
            try
            {
                var list = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                _devices = list.ToList();

                CameraCombo.ItemsSource = _devices.Select(d => d.Name).ToList();
                CameraCombo.IsEnabled = _devices.Count > 0;

                if (_devices.Count == 0)
                {
                    Overlay.Visibility = Visibility.Visible;
                    Status("Nie znaleziono żadnych kamer.");
                }
            }
            catch (Exception ex)
            {
                Overlay.Visibility = Visibility.Visible;
                Status($"Błąd enumeracji kamer: {ex.Message}");
            }
        }

        // ===== Inicjalizacja / przełączanie =====
        private async Task SwitchCameraAsync(DeviceInformation device)
        {
            try
            {
                await CleanupCaptureAsync();   // sprzątamy poprzednią kamerę

                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = device.Id,
                    StreamingCaptureMode = StreamingCaptureMode.AudioAndVideo,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl
                };

                MediaCapture? localCapture = new MediaCapture();
                bool initialized = false;

                // 1) próba ExclusiveControl
                try
                {
                    await localCapture.InitializeAsync(settings);
                    initialized = true;
                }
                catch
                {
                    // 2) fallback: SharedReadOnly
                    try
                    {
                        settings.SharingMode = MediaCaptureSharingMode.SharedReadOnly;
                        localCapture = new MediaCapture();
                        await localCapture.InitializeAsync(settings);
                        initialized = true;
                    }
                    catch (Exception ex2)
                    {
                        // tu kończymy – nie udało się zainicjalizować kamery
                        _capture = null;
                        Overlay.Visibility = Visibility.Visible;
                        Status($"Nie udało się zainicjalizować kamery: {ex2.Message}");
                        return;
                    }
                }

                // jeśli z jakiegoś powodu nadal nie zainicjalizowane – też wychodzimy
                if (!initialized || localCapture is null)
                {
                    _capture = null;
                    Overlay.Visibility = Visibility.Visible;
                    Status("Nie udało się zainicjalizować kamery (brak urządzenia?).");
                    return;
                }

                // ✔ od teraz mamy PEWNE _capture
                _capture = localCapture;
                _currentDevice = device;

                await LoadResolutionsAsync();

                // Player
                _player = new MediaPlayer();
                PlayerElement.SetMediaPlayer(_player);

                MediaFrameSource? colorSource =
                    _capture.FrameSources.Values
                        .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                    ?? _capture.FrameSources.Values.FirstOrDefault();

                if (colorSource is null)
                {
                    Overlay.Visibility = Visibility.Visible;
                    Status("Brak strumienia podglądu dla tej kamery.");
                    return;
                }

                var mediaSource = MediaSource.CreateFromMediaFrameSource(colorSource);
                _player.Source = mediaSource;
                _player.IsMuted = true;
                _player.Play();

                _isPreviewing = true;
                Overlay.Visibility = Visibility.Collapsed;
                Status($"Podgląd: {device.Name}");
            }
            catch (Exception ex)
            {
                _capture = null;
                Overlay.Visibility = Visibility.Visible;
                Status($"Błąd przełączania kamery: {ex.Message}");
            }
        }


        private async Task LoadResolutionsAsync()
        {
            if (_capture is null) return;

            _recordProps = _capture.VideoDeviceController
                .GetAvailableMediaStreamProperties(MediaStreamType.VideoRecord)
                .OfType<VideoEncodingProperties>()
                .ToList();

            _previewProps = _capture.VideoDeviceController
                .GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview)
                .OfType<VideoEncodingProperties>()
                .ToList();

            var items = _recordProps
                .Select(p => new ResItem(p))
                .Distinct()
                .OrderByDescending(r => r.Width)
                .ThenByDescending(r => r.Fps)
                .ToList();

            ResolutionCombo.ItemsSource = items;
            ResolutionCombo.SelectedIndex = items.Count > 0 ? 0 : -1;

            await Task.CompletedTask;
        }

        private sealed class ResItem
        {
            public uint Width { get; }
            public uint Height { get; }
            public double Fps { get; }
            public string Subtype { get; }

            public ResItem(VideoEncodingProperties p)
            {
                Width = p.Width;
                Height = p.Height;
                Fps = (p.FrameRate?.Numerator ?? 30) /
                      Math.Max(1.0, p.FrameRate?.Denominator ?? 1);
                Subtype = p.Subtype ?? "";
            }

            public override string ToString() => $"{Width}×{Height}@{Fps:0.#} {Subtype}";

            public override bool Equals(object? obj) =>
                obj is ResItem o &&
                o.Width == Width &&
                o.Height == Height &&
                Math.Abs(o.Fps - Fps) < 0.01 &&
                o.Subtype == Subtype;

            public override int GetHashCode() =>
                HashCode.Combine(Width, Height, Math.Round(Fps, 2), Subtype);
        }

        // ===== Handlery UI =====
        private async void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraCombo.SelectedIndex < 0 || CameraCombo.SelectedIndex >= _devices.Count)
                return;

            await SwitchCameraAsync(_devices[CameraCombo.SelectedIndex]);
        }

        private async void ResolutionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_capture is null) return;
            if (ResolutionCombo.SelectedItem is not ResItem item) return;

            try
            {
                // dopasuj tryb RECORD
                var rec = _recordProps.FirstOrDefault(p =>
                    p.Width == item.Width &&
                    p.Height == item.Height);

                if (rec != null)
                {
                    await _capture.VideoDeviceController
                        .SetMediaStreamPropertiesAsync(MediaStreamType.VideoRecord, rec);
                }

                // dopasuj najbliższy PREVIEW
                var prev = _previewProps
                    .OrderBy(p => Math.Abs((int)p.Width - (int)item.Width) +
                                  Math.Abs((int)p.Height - (int)item.Height))
                    .FirstOrDefault();

                if (prev != null)
                {
                    await _capture.VideoDeviceController
                        .SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, prev);
                }

                Status($"Ustawiono {item.Width}×{item.Height}@{item.Fps:0.#}");
            }
            catch (Exception ex)
            {
                Status($"Błąd zmiany rozdzielczości: {ex.Message}");
            }
        }

        private async void PhotoBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_capture is null)
                    return;

                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    $"IMG_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
                    CreationCollisionOption.GenerateUniqueName);

                await _capture.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateJpeg(), file);

                if (_db != null && _currentExamId.HasValue)
                {
                    var imgId = await _db.AddImageAsync(file.Path, _currentExamId.Value);
                    Status($"Zapisano zdjęcie (id_image={imgId}): {file.Path}");
                }
                else
                {
                    Status($"Zapisano zdjęcie (bez wpisu do DB): {file.Path}");
                }
            }
            catch (Exception ex)
            {
                Status($"Błąd zdjęcia: {ex.Message}");
            }
        }


        private async void RecordToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_capture is null) { RecordToggle.IsChecked = false; return; }
            try
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                    $"VID_{DateTime.Now:yyyyMMdd_HHmmss}.mp4",
                    CreationCollisionOption.GenerateUniqueName);

                _lastRecordedFile = file;

                var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
                await _capture.StartRecordToStorageFileAsync(profile, file);
                _isRecording = true;
                Status($"Nagrywanie… → {file.Path}");
            }
            catch (Exception ex)
            {
                RecordToggle.IsChecked = false;
                Status($"Błąd startu nagrywania: {ex.Message}");
            }
        }


        private async void RecordToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_capture is null || !_isRecording)
                return;

            try
            {
                await _capture.StopRecordAsync();
                _isRecording = false;

                if (_db != null && _currentExamId.HasValue && _lastRecordedFile != null)
                {
                    var movieId = await _db.AddMovieAsync(_lastRecordedFile.Path, _currentExamId.Value);
                    Status($"Nagrywanie zatrzymane • zapisano film (id_movie={movieId})");
                }
                else
                {
                    Status("Nagrywanie zatrzymane (bez wpisu do DB).");
                }
            }
            catch (Exception ex)
            {
                Status($"Błąd zatrzymania nagrywania: {ex.Message}");
            }
        }


        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDevicesAsync();
        }

        // ===== Sprzątanie =====
        private async Task CleanupCaptureAsync()
        {
            try { PlayerElement.SetMediaPlayer(null); } catch { }

            if (_player != null)
            {
                try { _player.Pause(); } catch { }
                try { _player.Dispose(); } catch { }
                _player = null;
            }

            if (_capture != null)
            {
                try
                {
                    if (_isRecording)
                    {
                        await _capture.StopRecordAsync();
                        _isRecording = false;
                    }
                }
                catch { }

                try
                {
                    if (_isPreviewing)
                    {
                        await _capture.StopPreviewAsync();
                        _isPreviewing = false;
                    }
                }
                catch { }

                try { _capture.Dispose(); } catch { }
                _capture = null;
            }

            Overlay.Visibility = Visibility.Visible;
        }
    }
}

