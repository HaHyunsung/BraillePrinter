using System.IO;
using System.Xml.Serialization;
using BraillePrinter.Models;

namespace BraillePrinter.Managers
{
    /// <summary>
    /// 점자 출력 파라미터를 관리하는 싱글톤 매니저.
    /// 파라미터는 XML 파일로 저장·복원됩니다.
    /// 저장 경로: &lt;저장소 루트&gt;\config\parameters.xml (Git으로 추적됨)
    /// </summary>
    public sealed class ParameterManager
    {
        // ── 저장 경로 ─────────────────────────────────────────────────────
        // 실행 파일은 bin\Debug\... 에서 동작하므로, 저장소 루트(.git 또는
        // .slnx 마커)를 거슬러 올라가 찾아 그 안의 config 폴더를 사용한다.
        // 마커를 찾지 못하면 실행 파일 옆 config 폴더로 폴백한다.
        private static readonly string ConfigDirectory = ResolveConfigDirectory();

        private static readonly string ConfigFilePath =
            Path.Combine(ConfigDirectory, "parameters.xml");

        private static string ResolveConfigDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                bool isRepoRoot =
                    Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                    File.Exists(Path.Combine(dir.FullName, "BraillePrinter.slnx"));
                if (isRepoRoot)
                    return Path.Combine(dir.FullName, "config");
                dir = dir.Parent;
            }
            // 폴백: 배포 환경 등 저장소 마커를 찾지 못한 경우
            return Path.Combine(AppContext.BaseDirectory, "config");
        }

        private static readonly XmlSerializer Serializer =
            new(typeof(BrailleParameters));

        // ── 싱글톤 (경로/직렬화기 초기화 후에 와야 함) ───────────────────────
        public static readonly ParameterManager Instance = new();

        // ── 공개 인터페이스 ───────────────────────────────────────────────

        /// <summary>현재 파라미터. 직접 수정하지 말고 UpdateParameters()를 사용하세요.</summary>
        public BrailleParameters Parameters { get; private set; } = new();

        /// <summary>파라미터가 변경(저장)될 때 발생합니다.</summary>
        public event Action? ParametersChanged;

        // ── 생성자 (private — 싱글톤) ─────────────────────────────────────
        private ParameterManager() => Load();

        // ── 메서드 ────────────────────────────────────────────────────────

        /// <summary>새 파라미터를 적용하고 XML에 저장합니다.</summary>
        public void UpdateParameters(BrailleParameters newParams)
        {
            Parameters = newParams;
            Save();
            ParametersChanged?.Invoke();
        }

        /// <summary>현재 파라미터를 기본값으로 초기화하고 저장합니다.</summary>
        public void ResetToDefault()
        {
            Parameters = new BrailleParameters();
            Save();
            ParametersChanged?.Invoke();
        }

        // ── 내부 저장·복원 ───────────────────────────────────────────────

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                using var writer = new StreamWriter(ConfigFilePath, append: false,
                                                    System.Text.Encoding.UTF8);
                Serializer.Serialize(writer, Parameters);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParameterManager] 저장 오류: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigFilePath)) return;

                using var reader = new StreamReader(ConfigFilePath, System.Text.Encoding.UTF8);
                if (Serializer.Deserialize(reader) is BrailleParameters loaded)
                    Parameters = loaded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(ConfigDirectory);
                    File.WriteAllText(
                        Path.Combine(ConfigDirectory, "load_error.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
                catch { }
                Parameters = new BrailleParameters();
            }
        }
    }
}
