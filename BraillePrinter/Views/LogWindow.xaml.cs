using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace BraillePrinter.Views
{
    /// <summary>
    /// liblouis 로그 파일을 실시간으로 표시하는 뷰어 창.
    /// </summary>
    public partial class LogWindow : Window
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Logs", "liblouis.log");

        private readonly DispatcherTimer _refreshTimer;
        private bool   _autoScroll = true;
        private long   _lastFileSize;

        public LogWindow()
        {
            InitializeComponent();

            TbLogPath.Text = LogPath;

            // 1초마다 파일 변경 감지
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += (_, _) => PollLogFile();
            _refreshTimer.Start();

            LoadLog();
        }

        // ── 로그 로드 ─────────────────────────────────────────────────────

        private void LoadLog()
        {
            if (!File.Exists(LogPath))
            {
                TbLog.Text   = $"[로그 파일 없음]\n{LogPath}";
                TbStatus.Text = "로그 파일이 아직 생성되지 않았습니다.";
                return;
            }

            try
            {
                // 공유 읽기 허용 (앱이 파일에 쓰는 중에도 읽기 가능)
                using var fs     = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string content   = reader.ReadToEnd();

                _lastFileSize = fs.Length;
                TbLog.Text    = content;
                TbStatus.Text = $"줄 수: {TbLog.LineCount:N0}  │  파일 크기: {fs.Length / 1024.0:F1} KB  │  마지막 갱신: {DateTime.Now:HH:mm:ss}";

                if (_autoScroll)
                    LogScrollViewer.ScrollToBottom();
            }
            catch (Exception ex)
            {
                TbStatus.Text = $"읽기 오류: {ex.Message}";
            }
        }

        private void PollLogFile()
        {
            if (!File.Exists(LogPath)) return;

            try
            {
                long size = new FileInfo(LogPath).Length;
                if (size != _lastFileSize)
                    LoadLog();
            }
            catch { /* 무시 */ }
        }

        // ── 버튼 이벤트 ──────────────────────────────────────────────────

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
            => LoadLog();

        private void BtnAutoScroll_Click(object sender, RoutedEventArgs e)
        {
            _autoScroll = !_autoScroll;
            BtnAutoScroll.Content = _autoScroll ? "↓  자동 스크롤: ON" : "↓  자동 스크롤: OFF";
            BtnAutoScroll.Foreground = _autoScroll
                ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0))
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(LogPath)) return;

            var result = MessageBox.Show(
                "로그 파일 내용을 모두 지우겠습니까?",
                "로그 지우기",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                File.WriteAllText(LogPath, string.Empty);
                LoadLog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"지우기 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer.Stop();
            base.OnClosed(e);
        }
    }
}
