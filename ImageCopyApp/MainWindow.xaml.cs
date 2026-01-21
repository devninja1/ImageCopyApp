using Microsoft.Win32;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Media;

using WinForms = System.Windows.Forms;

namespace ImageCopyApp
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings = new();

        private static readonly string[] ImageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".webp"
        };

        public MainWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            ApplySettingsToUI();
            UpdateFolderCounts();
        }

        private int CountImages(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return 0;
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Count(f => ImageExtensions.Contains(Path.GetExtension(f).ToLower()));
        }

        private void UpdateFolderCounts()
        {
            lblLowResCount.Text = CountImages(txtLowRes.Text).ToString();
            lblHiResCount.Text = CountImages(txtHiRes.Text).ToString();
            lblDestCount.Text = CountImages(txtDest.Text).ToString();
        }


        private void ApplySettingsToUI()
        {
            txtLowRes.Text = _settings.LowResFolder ?? "";
            txtHiRes.Text = _settings.HiResFolder ?? "";
            txtDest.Text = _settings.DestinationFolder ?? "";
            chkOverwrite.IsChecked = _settings.Overwrite;
            chkIncludeExtensions.IsChecked = _settings.MatchByNameOnly;
        }

        private void CaptureUIToSettings()
        {
            _settings.LowResFolder = txtLowRes.Text;
            _settings.HiResFolder = txtHiRes.Text;
            _settings.DestinationFolder = txtDest.Text;
            _settings.Overwrite = chkOverwrite.IsChecked == true;
            _settings.MatchByNameOnly = chkIncludeExtensions.IsChecked == true;
            _settings.Save();
        }

        private string? BrowseFolder(string? initialPath, string description = "Select folder")
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                ShowNewFolderButton = true,
                Description = description,
                UseDescriptionForTitle = true
            };

            // Environment.SpecialFolder is part of System
            dlg.RootFolder = Environment.SpecialFolder.MyComputer;

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                dlg.SelectedPath = initialPath;

            return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
        }

        private void BrowseLowRes_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder(_settings.LowResFolder, "select low res folder");
            if (path != null)
            {
                txtLowRes.Text = path;
                CaptureUIToSettings();
                UpdateFolderCounts();
            }
        }

        private void BrowseHiRes_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder(_settings.HiResFolder, "select Hi res folder");
            if (path != null)
            {
                txtHiRes.Text = path;
                CaptureUIToSettings();
                UpdateFolderCounts();
            }
        }

        private void BrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder(_settings.DestinationFolder, "select destination folder");
            if (path != null)
            {
                txtDest.Text = path;
                CaptureUIToSettings();
                UpdateFolderCounts();
            }
        }

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            //txtStatus.Text = "";
            CaptureUIToSettings();

            // Validate
            if (!Directory.Exists(_settings.LowResFolder))
            {
                AppendLog("Low Res folder not found.");
                return;
            }
            if (!Directory.Exists(_settings.HiResFolder))
            {
                AppendLog("Hi Res folder not found.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.DestinationFolder))
            {
                AppendLog("Destination folder not set.");
                return;
            }
            Directory.CreateDirectory(_settings.DestinationFolder);

            //btnCopy.IsEnabled = false;
            //progress.Value = 0;

            try
            {
                await Task.Run(() => CopyMatchingImages());
            }
            finally
            {
                btnCopy.IsEnabled = true;
            }
        }

        private void CopyMatchingImages()
        {
            var startTime = DateTime.Now;
            var lowResFiles = Directory.EnumerateFiles(_settings.LowResFolder!, "*.*", SearchOption.AllDirectories)
                                       .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLower()))
                                       .ToList();

            var hiResFiles = Directory.EnumerateFiles(_settings.HiResFolder!, "*.*", SearchOption.AllDirectories)
                                      .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLower()))
                                      .ToList();

            // Build lookup for hi-res files
            var hiLookup = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hr in hiResFiles)
            {
                var key = _settings.MatchByNameOnly
                    ? Path.GetFileNameWithoutExtension(hr)
                    : Path.GetFileName(hr);
                hiLookup[key] = hr;
            }

            int copied = 0, missing = 0, skipped = 0, errors = 0,i=0 ;
            int total = lowResFiles.Count;


            // Parallel copy
            Parallel.ForEach(lowResFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, lf =>
            {
                var lowKey = _settings.MatchByNameOnly
                    ? Path.GetFileNameWithoutExtension(lf)
                    : Path.GetFileName(lf);

                if (hiLookup.TryGetValue(lowKey, out var hiPath))
                {
                    var relativePath = Path.GetRelativePath(_settings.LowResFolder!, lf);
                    var destRelative = Path.Combine(Path.GetDirectoryName(relativePath) ?? "",
                                                    Path.GetFileName(hiPath));
                    var destPath = Path.Combine(_settings.DestinationFolder!, destRelative);

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                        if (File.Exists(destPath) && !_settings.Overwrite)
                        {
                            Interlocked.Increment(ref skipped);
                            AppendLog($"Skipped (exists): {destRelative}");
                        }
                        else
                        {
                            File.Copy(hiPath, destPath, overwrite: _settings.Overwrite);
                            Interlocked.Increment(ref copied);
                            AppendLog($"Copied: {destRelative}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
                        AppendLog($"Error copying {destRelative}: {ex.Message}");
                        PlayErrorSound();
                    }
                }
                else
                {
                    Interlocked.Increment(ref missing);
                    AppendLog($"No match for: {Path.GetFileName(lf)}");
                }

                // Update progress safely
                int done = copied + missing + skipped + errors;
                UpdateProgress(done, total, copied, missing, skipped, errors, startTime);
                
            });

            AppendStatus($"Done. Copied: {copied}, Missing: {missing}, Skipped: {skipped}, Errors: {errors}");
            PlaySuccessSound();
            Dispatcher.Invoke(UpdateFolderCounts);
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        //private void UpdateProgress(int current, int total, int copied, int missing, int skipped, int errors)
        //{
        //    Dispatcher.Invoke(() =>
        //    {
        //        progress.Maximum = total;
        //        progress.Value = current;
        //        txtStatus.Text = $"Processed {current}/{total} | Copied {copied} • Missing {missing} • Skipped {skipped} • Errors {errors}";
        //    });
        //}

        private void UpdateProgress(int done, int total, int copied, int missing, int skipped, int errors, DateTime startTime)
        {
            // If called from non-UI thread, re-dispatch asynchronously to avoid blocking worker threads
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                    UpdateProgress(done, total, copied, missing, skipped, errors, startTime)));
                return;
            }

            // UI-thread-safe code
            double percent = total > 0 ? (double)done / total * 100 : 0;
            progressBar.Value = percent;

            var elapsed = DateTime.Now - startTime;
            if (done > 0)
            {
                double avgPerItem = elapsed.TotalSeconds / done;
                double remainingSeconds = avgPerItem * (total - done);
                var eta = TimeSpan.FromSeconds(remainingSeconds);
                lblEta.Text = $"ETA: {eta:mm\\:ss} remaining";
            }
            else
            {
                lblEta.Text = "ETA: --";
            }
        }

        private void AppendStatus(string message)
        {
            Dispatcher.Invoke(() => txtStatus.Text = message);
        }

        protected override void OnClosed(EventArgs e)
        {
            CaptureUIToSettings();
            base.OnClosed(e);
        }

  

private void PlaySuccessSound()
    {
        SystemSounds.Exclamation.Play();   // or SystemSounds.Exclamation, Beep, Hand
    }

    private void PlayErrorSound()
    {
        SystemSounds.Hand.Play();       // "error" style sound
    }
}
}