using System.IO;
using System.Xml.Serialization;
using BraillePrinter.Models;

namespace BraillePrinter.Managers
{
    /// <summary>
    /// 점자 출력 파라미터를 관리하는 싱글톤 매니저.
    /// 파라미터는 XML 파일로 저장·복원됩니다.
    /// 저장 경로: %AppData%\BraillePrinter\parameters.xml
    /// </summary>
    public sealed class ParameterManager
    {
        // ── 싱글톤 ───────────────────────────────────────────────────────
        public static readonly ParameterManager Instance = new();

        // ── 저장 경로 ─────────────────────────────────────────────────────
        private static readonly string ConfigDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "BraillePrinter");

        private static readonly string ConfigFilePath =
            Path.Combine(ConfigDirectory, "parameters.xml");

        private static readonly XmlSerializer Serializer =
            new(typeof(BrailleParameters));

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
                System.Diagnostics.Debug.WriteLine($"[ParameterManager] 로드 오류: {ex.Message}");
                Parameters = new BrailleParameters();
            }
        }
    }
}
